using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web.Ponte;

namespace Pmvni.Composicao.Web;

/// <summary>
/// Documento do relatório numa sandbox: grava o HTML da sessão (com CSP e o paginador.js injetados) na pasta da sessão,
/// navega até ele e conversa com o paginador pela ponte tipada (<see cref="Mensagens"/>).
/// Trabalha sobre o <see cref="CoreWebView2"/> do WinRT, para servir ao host oculto e ao controle XAML do app.
/// Os métodos públicos podem ser chamados de qualquer thread quando o documento vem de um <see cref="HostOculto"/>;
/// com o construtor público, devem rodar na thread do CoreWebView2. Os eventos disparam na thread do CoreWebView2.
/// </summary>
public sealed class DocumentoWeb : IDisposable
{
    const int MaxRegistros = 200;
    static readonly Regex AberturaHead = new(@"<head(?:\s[^>]*)?>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly TimeSpan LimiteProntoPadrao = TimeSpan.FromSeconds(15);

    readonly CoreWebView2 core;
    readonly Func<Func<Task>, Task> naThread;
    readonly ConcurrentDictionary<string, TaskCompletionSource<MensagemWeb>> chamadas = new();
    readonly ConcurrentQueue<Descarte> descartes = new();
    readonly ConcurrentQueue<string> bloqueadas = new();
    TaskCompletionSource? pronto;
    bool segurancaAplicada;
    int carregamentos;
    int desanexado, pastaApagada;

    /// <summary>Documento na thread corrente (use no app WinUI, na thread da interface).</summary>
    public DocumentoWeb(CoreWebView2 core) : this(core, acao => acao()) { }

    internal DocumentoWeb(CoreWebView2 core, Func<Func<Task>, Task> naThread)
    {
        this.core = core ?? throw new ArgumentNullException(nameof(core));
        this.naThread = naThread;
        PastaSessao = Path.Combine(PastaDados.Raiz, "sessao", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PastaSessao);
        core.WebMessageReceived += AoReceberMensagem;
        core.ProcessFailed += AoFalharProcesso;
    }

    /// <summary>Pasta da sessão (<c>sessao\&lt;guid&gt;</c>), mapeada para doc.pmvni.invalid e apagada no Dispose.</summary>
    public string PastaSessao { get; }

    /// <summary>Último render e configuração carregados.</summary>
    public ResultadoRender? Render { get; private set; }
    public ConfigFolhas? Config { get; private set; }

    /// <summary>Toda mensagem válida vinda da web (inclusive as respostas de <see cref="ChamarAsync"/>).</summary>
    public event Action<MensagemWeb>? Recebida;

    /// <summary>Mensagens descartadas (inválidas ou de origem não permitida), as últimas 200.</summary>
    public IReadOnlyList<Descarte> Descartadas => descartes.ToArray();

    /// <summary>URLs respondidas com 403 pelo filtro de requisições, as últimas 200.</summary>
    public IReadOnlyList<string> RequisicoesBloqueadas => bloqueadas.ToArray();

    /// <summary>Grava a cópia de sessão do HTML, navega até ela e espera o <c>pronto</c> do paginador.
    /// O HTML do <paramref name="r"/> não é alterado.</summary>
    public Task CarregarAsync(ResultadoRender r, ConfigFolhas cfg)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(cfg);
        if (r.Erros.Count > 0) throw new ArgumentException("Render com erros de conferência não pode ser carregado: " + string.Join("; ", r.Erros), nameof(r));
        var html = InjetarNoHead(r.Html);
        return naThread(async () =>
        {
            ObjectDisposedException.ThrowIf(desanexado != 0, this);
            if (!segurancaAplicada)
            {
                await Seguranca.AplicarAsync(core, PastaSessao, RegistrarBloqueio);
                segurancaAplicada = true;
            }
            await File.WriteAllTextAsync(Path.Combine(PastaSessao, "documento.html"), html, new UTF8Encoding(false));
            await File.WriteAllBytesAsync(Path.Combine(PastaSessao, "paginador.js"), LerPaginador());
            Render = r;
            Config = cfg;
            var espera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pronto = espera;
            // O parâmetro evita que o WebView2 trate a navegação como recarga do mesmo documento.
            await HostOculto.NavegarParaUrlAsync(core, Seguranca.UrlBase + "documento.html?c=" + Interlocked.Increment(ref carregamentos));
            await espera.Task.WaitAsync(LimiteProntoPadrao);
        });
    }

    /// <summary>Envia uma mensagem host→web e espera a resposta com o mesmo id. Uma resposta <c>erro</c> vira
    /// <see cref="ErroWebException"/>; sem resposta em <paramref name="limite"/>, <see cref="TimeoutException"/>.</summary>
    public async Task<JsonElement> ChamarAsync(string tipo, object dados, TimeSpan limite)
    {
        var id = Guid.NewGuid().ToString("N");
        var json = Mensagens.SerializarParaWeb(tipo, id, dados);
        var espera = new TaskCompletionSource<MensagemWeb>(TaskCreationOptions.RunContinuationsAsynchronously);
        chamadas[id] = espera;
        try
        {
            await EnviarBrutoAsync(json);
            var resposta = await espera.Task.WaitAsync(limite);
            return resposta.Dados;
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"A web não respondeu à mensagem '{tipo}' em {limite.TotalSeconds:0.#} s.");
        }
        finally { chamadas.TryRemove(id, out _); }
    }

    internal Task EnviarBrutoAsync(string json) => naThread(() =>
    {
        ObjectDisposedException.ThrowIf(desanexado != 0, this);
        core.PostWebMessageAsJson(json);
        return Task.CompletedTask;
    });

    /// <summary>Solta os eventos e falha as chamadas pendentes. Na thread do CoreWebView2.</summary>
    internal void Desanexar()
    {
        if (Interlocked.Exchange(ref desanexado, 1) != 0) return;
        FalharChamadas(new ObjectDisposedException(nameof(DocumentoWeb)));
        try { core.WebMessageReceived -= AoReceberMensagem; } catch { }
        try { core.ProcessFailed -= AoFalharProcesso; } catch { }
        try { if (segurancaAplicada) core.ClearVirtualHostNameToFolderMapping(Seguranca.HostDocumento); } catch { }
    }

    /// <summary>Apaga a pasta da sessão (com tentativas: o WebView2 pode demorar a soltar os arquivos).</summary>
    internal void ApagarPasta()
    {
        if (Interlocked.Exchange(ref pastaApagada, 1) != 0) return;
        for (var i = 0; i < 40; i++)
        {
            try { if (Directory.Exists(PastaSessao)) Directory.Delete(PastaSessao, true); return; }
            catch (IOException) { Thread.Sleep(50); }
            catch (UnauthorizedAccessException) { Thread.Sleep(50); }
        }
    }

    public void Dispose()
    {
        Desanexar();
        ApagarPasta();
    }

    void AoReceberMensagem(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string json;
        try { json = e.WebMessageAsJson; }
        catch (Exception ex) { Descartar("mensagem ilegivel: " + ex.Message, ""); return; }
        if (!Uri.TryCreate(e.Source, UriKind.Absolute, out var origem) || !string.Equals(origem.Host, Seguranca.HostDocumento, StringComparison.OrdinalIgnoreCase))
        {
            Descartar("origem nao permitida: " + e.Source, json);
            return;
        }
        if (!Mensagens.TentarLerDaWeb(json, out var m, out var motivo))
        {
            Descartar(motivo!, json);
            return;
        }
        if (m!.Tipo == "pronto") pronto?.TrySetResult();
        if (chamadas.TryRemove(m.Id, out var espera))
        {
            if (m.Tipo == "erro")
            {
                var codigo = m.Dados.TryGetProperty("codigo", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString()! : "erro";
                espera.TrySetException(new ErroWebException(codigo, "A web respondeu com erro: " + codigo, m.Dados));
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
        pronto?.TrySetException(erro);
    }

    void Descartar(string motivo, string conteudo)
    {
        System.Diagnostics.Debug.WriteLine("[ponte] mensagem descartada: " + motivo);
        Registrar(descartes, new Descarte(motivo, conteudo.Length > 500 ? conteudo[..500] : conteudo));
    }

    void RegistrarBloqueio(string url) => Registrar(bloqueadas, url);

    static void Registrar<T>(ConcurrentQueue<T> fila, T item)
    {
        fila.Enqueue(item);
        while (fila.Count > MaxRegistros) fila.TryDequeue(out _);
    }

    /// <summary>CSP e paginador.js entram logo após a abertura do &lt;head&gt; (só na cópia da sessão).</summary>
    static string InjetarNoHead(string html)
    {
        var m = AberturaHead.Match(html);
        if (!m.Success) throw new InvalidOperationException("O HTML do relatório não tem <head>.");
        var injecao = $"<meta http-equiv=\"Content-Security-Policy\" content=\"{Seguranca.Csp}\">"
                    + $"<script src=\"{Seguranca.UrlBase}paginador.js\" defer></script>";
        return html.Insert(m.Index + m.Length, injecao);
    }

    static byte[] LerPaginador()
    {
        using var fluxo = typeof(DocumentoWeb).Assembly.GetManifestResourceStream("paginador.js")
            ?? throw new InvalidOperationException("Recurso embutido paginador.js não encontrado (rode npm run build em web/paginador).");
        using var memoria = new MemoryStream();
        fluxo.CopyTo(memoria);
        return memoria.ToArray();
    }
}
