using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web.Ponte;
using Pmvni.Legislacao;
using Windows.Foundation;

namespace Pmvni.Composicao.Web.Legislacao;

/// <summary>Um marcador (outline) do PDF: título em texto puro (até 300 caracteres) e os filhos; <see cref="Externo"/> = link para fora,
/// que o leitor mostra mas não segue.</summary>
public sealed record MarcadorLei(string Titulo, bool Externo, IReadOnlyList<MarcadorLei> Filhos);

/// <summary>Resultado da pesquisa na lei aberta: <see cref="Atual"/> de <see cref="Total"/>, na <see cref="Pagina"/> (null sem ocorrência).</summary>
public sealed record ProcuraLei(int Total, int Atual, int? Pagina);

/// <summary>O que <c>abrir</c> devolveu: páginas, marcadores, a escala inicial ("page-width") e, com termo, a primeira procura.</summary>
public sealed record LeiAberta(int Paginas, IReadOnlyList<MarcadorLei> Marcadores, double Escala, ProcuraLei? Procurado);

/// <summary>A seleção de texto na camada do PDF.js: texto ("" sem seleção) e as páginas do início e do fim.</summary>
public sealed record SelecaoLei(string Texto, int? PaginaInicial, int? PaginaFinal);

/// <summary>O leitor de PDF da legislação (desenho §11.4) sobre um <see cref="CoreWebView2"/>: a sandbox da
/// <see cref="SegurancaLegislacao"/> (host <c>leg.pmvni.invalid</c> da memória, CSP, 403 no resto, navegação só para
/// <c>leitor.html</c>, uma vez), a ponte tipada e os comandos do leitor. Trocar de PDF é mensagem, nunca navegação; o PDF aberto
/// fica registrado no <see cref="ServidorLegislacao"/> só enquanto aberto. Criar na thread do core; os métodos podem ser chamados de
/// qualquer thread (a ponte leva à thread do core).</summary>
public sealed class LeitorPdfJs : IAsyncDisposable
{
    public const string Pagina = SegurancaLegislacao.UrlBase + "leitor.html";
    const int MaxRegistros = 200;
    static readonly TimeSpan LimiteInicio = TimeSpan.FromSeconds(30);
    static readonly TimeSpan LimiteAbrir = TimeSpan.FromMinutes(2);
    static readonly TimeSpan LimiteComando = TimeSpan.FromSeconds(30);

    readonly CoreWebView2 core;
    readonly CoreWebView2Controller? controller;
    readonly ServidorLegislacao servidor;
    readonly SynchronizationContext contexto;
    readonly ConcurrentQueue<string> bloqueadas = new();
    readonly ConcurrentQueue<string> vistas = new();
    readonly SemaphoreSlim abertura = new(1, 1);
    string? urlAberta;
    int descartado;

    LeitorPdfJs(CoreWebView2 core, CoreWebView2Controller? controller, ServidorLegislacao servidor, SynchronizationContext contexto)
    {
        this.core = core;
        this.controller = controller;
        this.servidor = servidor;
        this.contexto = contexto;
        Ponte = new PonteLegislacao(core);
        Ponte.Recebida += m =>
        {
            if (m.Id.StartsWith("evento-", StringComparison.Ordinal) && m.Tipo is "paginaMudou" or "selecaoMudou" or "zoomMudou" or "atalho")
                Evento?.Invoke(m);
        };
    }

    public PonteLegislacao Ponte { get; }

    public CoreWebView2 Core => core;

    /// <summary>Eventos da página (<c>paginaMudou</c>, <c>selecaoMudou</c>, <c>zoomMudou</c>, <c>atalho</c>), na thread do core.</summary>
    public event Action<MensagemWeb>? Evento;

    /// <summary>URLs respondidas com 403 e navegações canceladas, as últimas 200.</summary>
    public IReadOnlyList<string> RequisicoesBloqueadas => bloqueadas.ToArray();

    public int ViolacoesCsp => Ponte.ViolacoesCsp;

    /// <summary>Toda requisição que o WebView2 fez (para provar que nada saiu do host).</summary>
    public IReadOnlyList<string> RequisicoesVistas => vistas.ToArray();

    /// <summary>A URL <c>https://leg.pmvni.invalid/pdf/&lt;token&gt;.pdf</c> do PDF aberto (null sem nenhum).</summary>
    public string? UrlAberta => Volatile.Read(ref urlAberta);

    /// <summary>Aplica a sandbox ao core de um WebView2 já criado (o controle visível do app), navega para <c>leitor.html</c> e espera
    /// o <c>pronto</c>, que confirma o PDF.js 6.4.299. Na thread do core, uma vez por core.</summary>
    public static Task<LeitorPdfJs> SobreAsync(CoreWebView2 core, ServidorLegislacao s) => IniciarAsync(core, null, s);

    /// <summary>Controller oculto (InPrivate, pela <see cref="FabricaWebView2"/>) sobre a janela <paramref name="janela"/> (testes).</summary>
    public static async Task<LeitorPdfJs> CriarOcultoAsync(CoreWebView2Environment ambiente, nint janela, ServidorLegislacao s, int largura = 1024, int altura = 900)
    {
        var referencia = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)janela);
        var controller = await ambiente.CreateCoreWebView2ControllerAsync(referencia, FabricaWebView2.OpcoesController(ambiente));
        try
        {
            controller.IsVisible = true; // a camada de texto e a rolagem precisam de layout; a janela do host é oculta
            controller.Bounds = new Rect(0, 0, largura, altura);
            return await IniciarAsync(controller.CoreWebView2, controller, s);
        }
        catch
        {
            try { controller.Close(); } catch { }
            throw;
        }
    }

    static async Task<LeitorPdfJs> IniciarAsync(CoreWebView2 core, CoreWebView2Controller? controller, ServidorLegislacao s)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(s);
        var contexto = SynchronizationContext.Current
            ?? throw new InvalidOperationException("O leitor precisa ser criado na thread do WebView2 (sem contexto de sincronização).");
        LeitorPdfJs? leitor = null;
        try
        {
            leitor = new LeitorPdfJs(core, controller, s, contexto);
            await SegurancaLegislacao.AplicarAsync(core, s, Pagina, url =>
            {
                leitor.bloqueadas.Enqueue(url);
                while (leitor.bloqueadas.Count > MaxRegistros) leitor.bloqueadas.TryDequeue(out _);
            });
            core.Settings.IsPinchZoomEnabled = false;
            core.WebResourceRequested += (remetente, e) =>
            {
                leitor.vistas.Enqueue(e.Request.Uri);
                while (leitor.vistas.Count > 1000) leitor.vistas.TryDequeue(out _);
            };
            var pronto = new TaskCompletionSource<MensagemWeb>(TaskCreationOptions.RunContinuationsAsynchronously);
            void Ao(MensagemWeb m)
            {
                if (m.Tipo == "pronto") pronto.TrySetResult(m);
                else if (m.Tipo == "erro" && m.Id == "inicio") pronto.TrySetException(new InvalidOperationException("O leitor não iniciou: " + Texto(m.Dados, "mensagem")));
            }
            leitor.Ponte.Recebida += Ao;
            try
            {
                await HostOculto.NavegarParaUrlAsync(core, Pagina, LimiteInicio);
                var m = await pronto.Task.WaitAsync(LimiteInicio);
                var versao = Texto(m.Dados, "pdfjs");
                if (versao != RecursosLegislacao.VersaoPdfJs)
                    throw new InvalidOperationException($"O PDF.js carregado é o {versao}; o esperado é o {RecursosLegislacao.VersaoPdfJs}.");
            }
            finally { leitor.Ponte.Recebida -= Ao; }
            return leitor;
        }
        catch
        {
            leitor?.Ponte.Dispose();
            throw;
        }
    }

    /// <summary>Abre o PDF <paramref name="caminhoPdf"/> (a cópia do editor): registra-o no servidor (token novo), fecha o anterior
    /// (o registro dele sai) e manda <c>abrir</c>. <c>erro</c> ⇒ <see cref="ExtracaoRecusadaException"/> (senha, inválido, falhou).</summary>
    public async Task<LeiAberta> AbrirAsync(string caminhoPdf, int? pagina = null, string? termo = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminhoPdf);
        await abertura.WaitAsync();
        try
        {
            var anterior = Interlocked.Exchange(ref urlAberta, null);
            if (anterior is not null) servidor.RemoverPdf(anterior);
            var url = servidor.RegistrarPdf(caminhoPdf);
            Volatile.Write(ref urlAberta, url);
            JsonElement d;
            try { d = await Ponte.ChamarAsync("abrir", new { url, pagina, termo }, LimiteAbrir); }
            catch (ErroWebException e)
            {
                servidor.RemoverPdf(url);
                Interlocked.CompareExchange(ref urlAberta, null, url);
                throw new ExtracaoRecusadaException(e.Codigo switch { "senha" => FalhaExtracao.Senha, "invalido" => FalhaExtracao.Invalido, _ => FalhaExtracao.Falhou }, e.Message);
            }
            var paginas = d.TryGetProperty("paginas", out var p) && p.TryGetInt32(out var n) ? n : 0;
            var escala = d.TryGetProperty("escala", out var es) && es.ValueKind == JsonValueKind.Number ? es.GetDouble() : 0;
            var marcadores = d.TryGetProperty("marcadores", out var mk) ? LerMarcadores(mk) : [];
            ProcuraLei? procurado = d.TryGetProperty("procurado", out var pr) && pr.ValueKind == JsonValueKind.Object ? LerProcura(pr) : null;
            return new LeiAberta(paginas, marcadores, escala, procurado);
        }
        finally { abertura.Release(); }
    }

    /// <summary>Fecha o PDF aberto (a página o solta e o registro dele sai do servidor: a URL passa a responder 403).</summary>
    public async Task FecharAsync()
    {
        await abertura.WaitAsync();
        try
        {
            var url = Interlocked.Exchange(ref urlAberta, null);
            if (url is not null) servidor.RemoverPdf(url);
            await Ponte.ChamarAsync("fechar", new { }, LimiteComando);
        }
        finally { abertura.Release(); }
    }

    public async Task<(int Atual, int Total)> IrPaginaAsync(int pagina) => LerPagina(await Ponte.ChamarAsync("irPagina", new { pagina }, LimiteComando));

    public async Task<(int Atual, int Total)> IrMarcadorAsync(IReadOnlyList<int> caminho) =>
        LerPagina(await Ponte.ChamarAsync("irMarcador", new { caminho }, LimiteComando));

    /// <summary><c>mais</c>, <c>menos</c>, <c>largura</c> ou <c>pagina</c>; devolve a escala aplicada (1 = 100%).</summary>
    public async Task<double> ZoomAsync(string acao)
    {
        var d = await Ponte.ChamarAsync("zoom", new { acao }, LimiteComando);
        return d.TryGetProperty("escala", out var e) && e.ValueKind == JsonValueKind.Number ? e.GetDouble() : 0;
    }

    /// <summary>Pesquisa na lei aberta (passo 0 = a partir da página atual; 1/-1 = próxima/anterior); termo vazio limpa o realce.</summary>
    public async Task<ProcuraLei> ProcurarAsync(string termo, int passo) =>
        LerProcura(await Ponte.ChamarAsync("procurar", new { termo, passo }, LimiteAbrir));

    public async Task<SelecaoLei> ObterSelecaoAsync()
    {
        var d = await Ponte.ChamarAsync("obterSelecao", new { }, LimiteComando);
        return new SelecaoLei(Texto(d, "texto"), Inteiro(d, "paginaInicial"), Inteiro(d, "paginaFinal"));
    }

    /// <summary>Avalia uma expressão JavaScript na página (CDP <c>Runtime.evaluate</c>, esperando a promessa) e devolve <c>result</c>.
    /// Só para os testes e o roteiro.</summary>
    public Task<JsonElement> AvaliarAsync(string expressao) => NaThreadAsync(async () =>
    {
        var resposta = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
            JsonSerializer.Serialize(new { expression = expressao, returnByValue = true, awaitPromise = true }));
        using var doc = JsonDocument.Parse(resposta);
        return doc.RootElement.GetProperty("result").Clone();
    });

    /// <summary>Chama um método do CDP (testes e roteiro: clique e tecla de verdade).</summary>
    public Task CdpAsync(string metodo, object parametros) => NaThreadAsync(async () =>
    {
        await core.CallDevToolsProtocolMethodAsync(metodo, JsonSerializer.Serialize(parametros));
        return true;
    });

    Task<T> NaThreadAsync<T>(Func<Task<T>> acao)
    {
        if (ReferenceEquals(SynchronizationContext.Current, contexto)) return acao();
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        contexto.Post(async _ =>
        {
            try { tcs.TrySetResult(await acao()); }
            catch (Exception e) { tcs.TrySetException(e); }
        }, null);
        return tcs.Task;
    }

    static (int, int) LerPagina(JsonElement d) => (Inteiro(d, "atual") ?? 0, Inteiro(d, "total") ?? 0);

    static ProcuraLei LerProcura(JsonElement d) => new(Inteiro(d, "total") ?? 0, Inteiro(d, "atual") ?? 0, Inteiro(d, "pagina"));

    static IReadOnlyList<MarcadorLei> LerMarcadores(JsonElement lista)
    {
        if (lista.ValueKind != JsonValueKind.Array) return [];
        return lista.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object).Select(e =>
        {
            var titulo = Texto(e, "titulo");
            if (titulo.Length > 300) titulo = titulo[..300];
            var externo = e.TryGetProperty("externo", out var x) && x.ValueKind == JsonValueKind.True;
            return new MarcadorLei(titulo, externo, e.TryGetProperty("filhos", out var f) ? LerMarcadores(f) : []);
        }).ToList();
    }

    static int? Inteiro(JsonElement d, string nome) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    static string Texto(JsonElement d, string nome) =>
        d.ValueKind == JsonValueKind.Object && d.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";

    /// <summary>Solta a ponte, o PDF registrado e (no controller oculto dos testes) o controller.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref descartado, 1) != 0) return;
        var url = Interlocked.Exchange(ref urlAberta, null);
        if (url is not null) servidor.RemoverPdf(url);
        try
        {
            await NaThreadAsync(() =>
            {
                Ponte.Dispose();
                try { controller?.Close(); } catch { }
                return Task.FromResult(true);
            }).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException) { }
        abertura.Dispose();
    }
}
