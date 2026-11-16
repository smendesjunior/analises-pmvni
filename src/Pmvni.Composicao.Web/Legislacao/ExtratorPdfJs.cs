using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web.Ponte;
using Pmvni.Legislacao;
using Windows.Foundation;

namespace Pmvni.Composicao.Web.Legislacao;

/// <summary>Lê as páginas e o texto de um PDF com o PDF.js num WebView2 <b>oculto</b> (InPrivate, pela <see cref="FabricaWebView2"/>),
/// na sandbox da <see cref="SegurancaLegislacao"/>: o host <c>leg.pmvni.invalid</c> servido da memória, CSP própria, o JavaScript
/// do PDF nunca roda e nada sai do host. Uma extração por vez (fila). Criar na thread da interface (ou na thread STA do
/// <see cref="HostOculto"/>): o contexto de sincronização dela é guardado e todo acesso ao WebView2 passa por ele; os métodos
/// públicos podem ser chamados de qualquer thread.</summary>
public sealed class ExtratorPdfJs : IExtratorPdf, IExtratorFiguras, IAsyncDisposable
{
    const string PaginaExtrator = SegurancaLegislacao.UrlBase + "extrator.html";
    const int MaxRegistros = 200;
    static readonly TimeSpan LimiteExtracaoPadrao = TimeSpan.FromMinutes(10);
    static readonly TimeSpan LimiteInicio = TimeSpan.FromSeconds(30);
    static readonly TimeSpan LimiteFigurasPadrao = TimeSpan.FromMinutes(3);

    readonly CoreWebView2Controller controller;
    readonly CoreWebView2 core;
    readonly ServidorLegislacao servidor;
    readonly SynchronizationContext contexto;
    readonly SemaphoreSlim fila = new(1, 1);
    readonly SemaphoreSlim filaFiguras = new(1, 1); // as figuras têm fila própria: nunca seguram a extração de texto (nem o contrário)
    readonly ConcurrentQueue<string> bloqueadas = new();
    readonly ConcurrentQueue<string> vistas = new();
    volatile bool inutilizavel;

    /// <summary>Tempo máximo de uma extração (10 min; os testes o reduzem).</summary>
    internal TimeSpan LimiteExtracao { get; set; } = LimiteExtracaoPadrao;

    /// <summary>Tempo máximo das figuras de uma lei (3 min; os testes o reduzem). Passou disso, a leitura segue sem figuras.</summary>
    internal TimeSpan LimiteFiguras { get; set; } = LimiteFigurasPadrao;
    int descartado;

    ExtratorPdfJs(CoreWebView2Controller controller, ServidorLegislacao servidor, SynchronizationContext contexto)
    {
        this.controller = controller;
        core = controller.CoreWebView2;
        this.servidor = servidor;
        this.contexto = contexto;
        Ponte = new PonteLegislacao(core);
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.BrowserProcessExited or CoreWebView2ProcessFailedKind.RenderProcessExited
                or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive) inutilizavel = true;
        };
    }

    /// <summary>A ponte com a página (testes: mensagens descartadas e violações de CSP).</summary>
    public PonteLegislacao Ponte { get; }

    /// <summary>URLs respondidas com 403 e navegações canceladas, as últimas 200.</summary>
    public IReadOnlyList<string> RequisicoesBloqueadas => bloqueadas.ToArray();

    /// <summary>Violações de CSP que a página informou.</summary>
    public int ViolacoesCsp => Ponte.ViolacoesCsp;

    /// <summary>Um processo do WebView2 caiu ou o extrator foi descartado: quem o usa deve criar outro.</summary>
    public bool Inutilizavel => inutilizavel || Volatile.Read(ref descartado) != 0;

    /// <summary>Toda requisição que o WebView2 fez (testes: provar que nada saiu do host).</summary>
    internal IReadOnlyList<string> RequisicoesVistas => vistas.ToArray();

    /// <summary>Cria o controller oculto (InPrivate) sobre a janela <paramref name="janela"/>, aplica a sandbox, abre
    /// <c>https://leg.pmvni.invalid/extrator.html</c> (a única navegação permitida) e espera o <c>pronto</c> da página, que confirma
    /// o PDF.js 6.4.299. Na thread do <paramref name="ambiente"/> (com contexto de sincronização).</summary>
    public static async Task<ExtratorPdfJs> CriarAsync(CoreWebView2Environment ambiente, nint janela, ServidorLegislacao s)
    {
        ArgumentNullException.ThrowIfNull(ambiente);
        ArgumentNullException.ThrowIfNull(s);
        var contexto = SynchronizationContext.Current
            ?? throw new InvalidOperationException("O extrator precisa ser criado na thread do WebView2 (sem contexto de sincronização).");
        var referencia = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)janela);
        var controller = await ambiente.CreateCoreWebView2ControllerAsync(referencia, FabricaWebView2.OpcoesController(ambiente));
        ExtratorPdfJs? extrator = null;
        try
        {
            // Sem interface: o controller não aparece sobre a janela do app, mesmo que ela seja a do editor.
            controller.IsVisible = false;
            controller.Bounds = new Rect(0, 0, 1024, 768);
            extrator = new ExtratorPdfJs(controller, s, contexto);
            await extrator.IniciarAsync();
            return extrator;
        }
        catch
        {
            if (extrator is not null) { try { extrator.Ponte.Dispose(); } catch { } }
            try { controller.Close(); } catch { }
            throw;
        }
    }

    async Task IniciarAsync()
    {
        await SegurancaLegislacao.AplicarAsync(core, servidor, PaginaExtrator, url =>
        {
            bloqueadas.Enqueue(url);
            while (bloqueadas.Count > MaxRegistros) bloqueadas.TryDequeue(out _);
        });
        core.WebResourceRequested += (remetente, e) =>
        {
            vistas.Enqueue(e.Request.Uri);
            while (vistas.Count > 1000) vistas.TryDequeue(out _);
        };

        var pronto = new TaskCompletionSource<MensagemWeb>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Ao(MensagemWeb m)
        {
            if (m.Tipo == "pronto") pronto.TrySetResult(m);
            else if (m.Tipo == "erro" && m.Id == "inicio") pronto.TrySetException(new InvalidOperationException("O extrator não iniciou: " + TextoDe(m.Dados, "mensagem")));
        }
        Ponte.Recebida += Ao;
        try
        {
            await HostOculto.NavegarParaUrlAsync(core, PaginaExtrator, LimiteInicio);
            var m = await pronto.Task.WaitAsync(LimiteInicio);
            var versao = TextoDe(m.Dados, "pdfjs");
            if (versao != RecursosLegislacao.VersaoPdfJs)
                throw new InvalidOperationException($"O PDF.js carregado é o {versao}; o esperado é o {RecursosLegislacao.VersaoPdfJs}.");
        }
        finally { Ponte.Recebida -= Ao; }
    }

    /// <summary>Registra o PDF no servidor, manda <c>extrair</c> (limite de 10 minutos), junta o <c>textoPagina</c> de cada página e
    /// informa o <c>progresso</c>. O registro sai no fim. <c>erro</c> da página ⇒ <see cref="ExtracaoRecusadaException"/>
    /// (<c>senha</c> → <see cref="FalhaExtracao.Senha"/>, <c>invalido</c> → <see cref="FalhaExtracao.Invalido"/>, outro →
    /// <see cref="FalhaExtracao.Falhou"/>). Cancelar manda <c>cancelar</c> à página e lança <see cref="OperationCanceledException"/>.</summary>
    public async Task<ResultadoExtracao> ExtrairAsync(string caminhoPdf, IProgress<(int Pagina, int Total)>? progresso, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoPdf);
        ct.ThrowIfCancellationRequested();
        if (Inutilizavel) throw new ExtracaoRecusadaException(FalhaExtracao.Falhou, "O leitor de PDF não está mais disponível.");
        await fila.WaitAsync(ct).ConfigureAwait(false);
        string? url = null;
        try
        {
            try { url = servidor.RegistrarPdf(caminhoPdf); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new ExtracaoRecusadaException(FalhaExtracao.Falhou, e.Message);
            }
            var arquivo = url;
            return await NaThreadAsync(() => ExtrairNaThreadAsync(arquivo, progresso, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            if (url is not null) servidor.RemoverPdf(url);
            fila.Release();
        }
    }

    async Task<ResultadoExtracao> ExtrairNaThreadAsync(string url, IProgress<(int Pagina, int Total)>? progresso, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var textos = new SortedDictionary<int, string>();
        void Evento(MensagemWeb m)
        {
            if (m.Tipo is not ("textoPagina" or "progresso")) return;
            if (TextoDe(m.Dados, "extracao") != id) return; // resto de outra extração (cancelada)
            if (!m.Dados.TryGetProperty("pagina", out var p) || !p.TryGetInt32(out var pagina) || pagina < 1) return;
            if (m.Tipo == "textoPagina") textos[pagina] = TextoDe(m.Dados, "texto");
            else if (m.Dados.TryGetProperty("total", out var t) && t.TryGetInt32(out var total)) progresso?.Report((pagina, total));
        }
        Ponte.Recebida += Evento;
        using var cancelamento = ct.Register(() => _ = EnviarCancelarAsync(id));
        try
        {
            var resposta = await Ponte.ChamarComIdAsync(id, "extrair", new { url }, LimiteExtracao, ct);
            var paginas = resposta.TryGetProperty("paginas", out var n) && n.TryGetInt32(out var np) ? np : -1;
            if (paginas < 0 || Enumerable.Range(1, paginas).Any(p => !textos.ContainsKey(p)))
                throw new ExtracaoRecusadaException(FalhaExtracao.Falhou, "O leitor não devolveu o texto de todas as páginas.");
            return new ResultadoExtracao([.. Enumerable.Range(1, paginas).Select(p => textos[p])]);
        }
        catch (ErroWebException e)
        {
            var falha = e.Codigo switch { "senha" => FalhaExtracao.Senha, "invalido" => FalhaExtracao.Invalido, _ => FalhaExtracao.Falhou };
            throw new ExtracaoRecusadaException(falha, e.Message);
        }
        catch (Exception e) when (e is TimeoutException or InvalidOperationException or ObjectDisposedException)
        {
            // Tempo esgotado: a página ainda trabalha nessa extração; manda parar (como no cancelamento do usuário).
            if (e is TimeoutException) await EnviarCancelarAsync(id);
            throw new ExtracaoRecusadaException(FalhaExtracao.Falhou, e.Message);
        }
        finally { Ponte.Recebida -= Evento; }
    }

    // ---- Figuras da leitura padronizada (correções 2026-11-15) ----

    const int MaximoBytesFigura = 12 * 1024 * 1024;
    const long MaximoBytesFiguras = 150L * 1024 * 1024;

    /// <summary>Lê as figuras do PDF (mensagem <c>figuras</c>; numa fila própria, aqui e na página, para nunca segurar a extração de
    /// texto, e com o limite <see cref="LimiteFiguras"/>; cancelar ou esgotar o tempo manda a página parar só esta extração): para
    /// cada página, as imagens e desenhos recortados da página desenhada, ou a página inteira quando o conteúdo gráfico não pôde ser
    /// localizado. Figura com tipo, assinatura ou tamanho fora do esperado é descartada; passado o total de 150 MB, as figuras seguintes
    /// viram só o aviso (<see cref="OrigemFigura.Falhou"/>). Erros como em <see cref="ExtrairAsync"/>.</summary>
    public async Task<IReadOnlyList<FiguraPdf>> ExtrairFigurasAsync(string caminhoPdf, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoPdf);
        ct.ThrowIfCancellationRequested();
        if (Inutilizavel) throw new ExtracaoRecusadaException(FalhaExtracao.Falhou, "O leitor de PDF não está mais disponível.");
        await filaFiguras.WaitAsync(ct).ConfigureAwait(false);
        string? url = null;
        try
        {
            try { url = servidor.RegistrarPdf(caminhoPdf); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new ExtracaoRecusadaException(FalhaExtracao.Falhou, e.Message);
            }
            var arquivo = url;
            return await NaThreadAsync(() => FigurasNaThreadAsync(arquivo, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            if (url is not null) servidor.RemoverPdf(url);
            filaFiguras.Release();
        }
    }

    async Task<IReadOnlyList<FiguraPdf>> FigurasNaThreadAsync(string url, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var figuras = new List<FiguraPdf>();
        var paginasVistas = new HashSet<int>();
        long total = 0;
        void Evento(MensagemWeb m)
        {
            if (m.Tipo != "figurasPagina" || TextoDe(m.Dados, "extracao") != id) return;
            if (!m.Dados.TryGetProperty("pagina", out var p) || !p.TryGetInt32(out var pagina) || pagina < 1 || !paginasVistas.Add(pagina)) return;
            if (!m.Dados.TryGetProperty("figuras", out var lista) || lista.ValueKind != JsonValueKind.Array) return;
            foreach (var f in lista.EnumerateArray())
            {
                if (LerFigura(pagina, f) is not { } figura) continue;
                if (figura.Dados.Length > 0 && (total += figura.Dados.Length) > MaximoBytesFiguras)
                    figura = figura with { Origem = OrigemFigura.Falhou, Tipo = "", Dados = [] };
                figuras.Add(figura);
            }
        }
        Ponte.Recebida += Evento;
        using var cancelamento = ct.Register(() => _ = EnviarCancelarAsync(id));
        try
        {
            await Ponte.ChamarComIdAsync(id, "figuras", new { url }, LimiteFiguras, ct);
            return [.. figuras.OrderBy(f => f.Pagina)];
        }
        catch (ErroWebException e)
        {
            var falha = e.Codigo switch { "senha" => FalhaExtracao.Senha, "invalido" => FalhaExtracao.Invalido, _ => FalhaExtracao.Falhou };
            throw new ExtracaoRecusadaException(falha, e.Message);
        }
        catch (Exception e) when (e is TimeoutException or InvalidOperationException or ObjectDisposedException)
        {
            if (e is TimeoutException) await EnviarCancelarAsync(id);
            throw new ExtracaoRecusadaException(FalhaExtracao.Falhou, e.Message);
        }
        finally { Ponte.Recebida -= Evento; }
    }

    /// <summary>Uma figura do evento <c>figurasPagina</c>, conferida (origem, tipo, base64, assinatura e tamanho); null se não vale.</summary>
    internal static FiguraPdf? LerFigura(int pagina, JsonElement f)
    {
        if (f.ValueKind != JsonValueKind.Object) return null;
        static double Num(JsonElement o, string nome) =>
            o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d) ? d : double.NaN;
        var origem = TextoDe(f, "origem");
        var tipo = TextoDe(f, "tipo");
        if (!OrigemFigura.Valida(origem)) return null;
        var (x, y, w, h, largura, linha) = (Num(f, "x"), Num(f, "y"), Num(f, "w"), Num(f, "h"), Num(f, "largura"), Num(f, "linha"));
        if (new[] { x, y, w, h, largura, linha }.Any(double.IsNaN)) return null;
        byte[] dados = [];
        if (origem != OrigemFigura.Falhou)
        {
            var base64 = TextoDe(f, "dados");
            if (!FiguraPdf.TipoValido(tipo) || base64.Length == 0 || base64.Length / 4 * 3 > MaximoBytesFigura) return null;
            try { dados = Convert.FromBase64String(base64); }
            catch (FormatException) { return null; }
            if (!FiguraPdf.AssinaturaConfere(tipo, dados)) return null;
        }
        else tipo = "";
        return new FiguraPdf(pagina, (int)Math.Clamp(linha, 0, int.MaxValue), Math.Clamp(x, 0, 1), Math.Clamp(y, 0, 1), Math.Clamp(w, 0, 1),
            Math.Clamp(h, 0, 1), origem, tipo, dados, Math.Max(0, largura));
    }

    /// <summary>Manda a página parar só a extração <paramref name="id"/> (texto e figuras correm em filas separadas).</summary>
    async Task EnviarCancelarAsync(string id)
    {
        try { await Ponte.EnviarAsync("cancelar", new { extracao = id }); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException or System.Runtime.InteropServices.COMException) { }
    }

    /// <summary>Avalia uma expressão JavaScript na página (CDP <c>Runtime.evaluate</c>, esperando a promessa) e devolve o objeto
    /// <c>result</c> (<c>value</c> quando serializável). Só para os testes.</summary>
    internal Task<JsonElement> AvaliarAsync(string expressao) => NaThreadAsync(async () =>
    {
        var resposta = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression = expressao, returnByValue = true, awaitPromise = true }));
        using var doc = JsonDocument.Parse(resposta);
        return doc.RootElement.GetProperty("result").Clone();
    }, CancellationToken.None);

    /// <summary>Lê algo do controller na thread dele (testes: configurações da sandbox).</summary>
    internal Task<T> NoControllerAsync<T>(Func<CoreWebView2Controller, T> ler) =>
        NaThreadAsync(() => Task.FromResult(ler(controller)), CancellationToken.None);

    Task<T> NaThreadAsync<T>(Func<Task<T>> acao, CancellationToken ct)
    {
        if (ReferenceEquals(SynchronizationContext.Current, contexto)) return acao();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        contexto.Post(async _ =>
        {
            try { tcs.TrySetResult(await acao()); }
            catch (OperationCanceledException e) when (ct.IsCancellationRequested) { tcs.TrySetCanceled(e.CancellationToken.IsCancellationRequested ? e.CancellationToken : ct); }
            catch (Exception e) { tcs.TrySetException(e); }
        }, null);
        return tcs.Task;
    }

    static string TextoDe(JsonElement dados, string nome) =>
        dados.ValueKind == JsonValueKind.Object && dados.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    /// <summary>Fecha o controller. Chamar antes de descartar o host/janela dele; espera no máximo 5 s pela thread do WebView2.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref descartado, 1) != 0) return;
        try
        {
            await NaThreadAsync(() =>
            {
                Ponte.Dispose();
                try { controller.Close(); } catch { }
                return Task.FromResult(true);
            }, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException) { /* a thread do WebView2 já não existe: nada a fechar */ }
        // A fila não é descartada: uma extração que ainda espera a vez recebe a falha do controller fechado, nunca ObjectDisposedException solta.
    }
}
