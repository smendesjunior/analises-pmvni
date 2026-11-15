using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web.Ponte;

namespace Pmvni.Composicao.Web.Legislacao;

/// <summary>Ponte tipada com a página da legislação (envelope <c>{ v, tipo, id, dados }</c>, validado nos dois lados). Criar na
/// thread do <see cref="CoreWebView2"/> (o contexto de sincronização dela é guardado); <see cref="ChamarAsync"/> e
/// <see cref="EnviarAsync"/> podem ser chamados de qualquer thread. Mensagem de origem diferente de
/// <c>https://leg.pmvni.invalid/</c>, inválida ou do sentido errado é descartada e registrada.</summary>
public sealed class PonteLegislacao : IDisposable
{
    const int MaxRegistros = 200;
    static readonly TimeSpan LimitePadrao = TimeSpan.FromSeconds(60);

    readonly CoreWebView2 core;
    readonly SynchronizationContext contexto;
    readonly ConcurrentDictionary<string, TaskCompletionSource<MensagemWeb>> chamadas = new();
    readonly ConcurrentQueue<Descarte> descartes = new();
    int violacoesCsp;
    int descartada;

    public PonteLegislacao(CoreWebView2 core)
    {
        this.core = core ?? throw new ArgumentNullException(nameof(core));
        contexto = SynchronizationContext.Current
            ?? throw new InvalidOperationException("A ponte da legislação precisa ser criada na thread do CoreWebView2 (sem contexto de sincronização).");
        core.WebMessageReceived += AoReceber;
        core.ProcessFailed += AoFalharProcesso;
    }

    /// <summary>Toda mensagem válida vinda da web (inclusive as respostas de <see cref="ChamarAsync"/>), na thread do core.</summary>
    public event Action<MensagemWeb>? Recebida;

    /// <summary>Mensagens descartadas (inválidas ou de origem não permitida), as últimas 200.</summary>
    public IReadOnlyList<Descarte> Descartadas => descartes.ToArray();

    /// <summary>Quantas violações de CSP a página informou (<c>cspViolacao</c>).</summary>
    public int ViolacoesCsp => Volatile.Read(ref violacoesCsp);

    /// <summary>Envia uma mensagem host→web e espera a resposta com o mesmo id. Uma resposta <c>erro</c> vira
    /// <see cref="ErroWebException"/>; sem resposta em <paramref name="limite"/>, <see cref="TimeoutException"/>.</summary>
    public Task<JsonElement> ChamarAsync(string tipo, object dados, TimeSpan? limite = null, CancellationToken ct = default) =>
        ChamarComIdAsync(Guid.NewGuid().ToString("N"), tipo, dados, limite, ct);

    /// <summary>Como <see cref="ChamarAsync"/>, com o id escolhido por quem chama
    /// (o extrator usa o id para reconhecer os eventos da sua extração).</summary>
    internal async Task<JsonElement> ChamarComIdAsync(string id, string tipo, object dados, TimeSpan? limite, CancellationToken ct)
    {
        var json = MensagensLegislacao.SerializarParaWeb(tipo, id, dados);
        var espera = new TaskCompletionSource<MensagemWeb>(TaskCreationOptions.RunContinuationsAsynchronously);
        chamadas[id] = espera;
        var tempo = limite ?? LimitePadrao;
        try
        {
            await NaThreadAsync(() => core.PostWebMessageAsJson(json)).ConfigureAwait(false);
            var resposta = await espera.Task.WaitAsync(tempo, ct).ConfigureAwait(false);
            return resposta.Dados;
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"A web não respondeu à mensagem '{tipo}' em {tempo.TotalSeconds:0.#} s.");
        }
        finally { chamadas.TryRemove(id, out _); }
    }

    /// <summary>Envia uma mensagem host→web sem esperar resposta (<c>cancelar</c>).</summary>
    public Task EnviarAsync(string tipo, object dados)
    {
        var json = MensagensLegislacao.SerializarParaWeb(tipo, Guid.NewGuid().ToString("N"), dados);
        return NaThreadAsync(() => core.PostWebMessageAsJson(json));
    }

    Task NaThreadAsync(Action acao)
    {
        if (ReferenceEquals(SynchronizationContext.Current, contexto)) { acao(); return Task.CompletedTask; }
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        contexto.Post(_ =>
        {
            try { acao(); tcs.TrySetResult(); }
            catch (Exception e) { tcs.TrySetException(e); }
        }, null);
        return tcs.Task;
    }

    void AoReceber(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.WebMessageAsJson; }
        catch (Exception ex) { Descartar("mensagem ilegivel: " + ex.Message, ""); return; }
        if (!SegurancaLegislacao.EhOrigemLegislacao(e.Source)) { Descartar("origem nao permitida: " + e.Source, json); return; }
        if (!MensagensLegislacao.TentarLerDaWeb(json, out var m, out var motivo)) { Descartar(motivo!, json); return; }
        if (m!.Tipo == "cspViolacao") Interlocked.Increment(ref violacoesCsp);
        if (chamadas.TryRemove(m.Id, out var espera))
        {
            if (m.Tipo == "erro")
            {
                var codigo = m.Dados.TryGetProperty("codigo", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "erro";
                var mensagem = m.Dados.TryGetProperty("mensagem", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "A web respondeu com erro: " + codigo;
                espera.TrySetException(new ErroWebException(codigo, mensagem, m.Dados));
            }
            else espera.TrySetResult(m);
        }
        try { Recebida?.Invoke(m); }
        catch (Exception ex) { Descartar("manipulador de Recebida lancou: " + ex.Message, json); }
    }

    void AoFalharProcesso(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            FalharChamadas(new InvalidOperationException("Processo do WebView2 falhou: " + e.ProcessFailedKind + "."));
    }

    void FalharChamadas(Exception erro)
    {
        foreach (var espera in chamadas.Values) espera.TrySetException(erro);
    }

    void Descartar(string motivo, string conteudo)
    {
        descartes.Enqueue(new Descarte(motivo, conteudo.Length > 500 ? conteudo[..500] : conteudo));
        while (descartes.Count > MaxRegistros) descartes.TryDequeue(out _);
    }

    /// <summary>Solta os eventos e falha as chamadas pendentes. Na thread do CoreWebView2.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref descartada, 1) != 0) return;
        FalharChamadas(new ObjectDisposedException(nameof(PonteLegislacao)));
        try { core.WebMessageReceived -= AoReceber; } catch { }
        try { core.ProcessFailed -= AoFalharProcesso; } catch { }
    }
}
