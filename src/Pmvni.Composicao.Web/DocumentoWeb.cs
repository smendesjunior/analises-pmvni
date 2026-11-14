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
    int carregamentos;
    int desanexado, pastaApagada;

    /// <summary>Documento na thread corrente (use no app WinUI, na thread da interface).</summary>
    public DocumentoWeb(CoreWebView2 core) : this(core, acao => acao()) { }

    internal DocumentoWeb(CoreWebView2 core, Func<Func<Task>, Task> naThread)
    {
        this.core = core ?? throw new ArgumentNullException(nameof(core));
        this.naThread = naThread;
        var raizSessoes = Path.Combine(PastaDados.Raiz, "sessao");
        LimparOrfas(raizSessoes);
        PastaSessao = Path.Combine(raizSessoes, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(PastaSessao);
        // A sandbox vale desde a construção: o core nunca fica exposto, nem a navegações feitas fora de CarregarAsync.
        // Se falhar, a construção falha (sem nova tentativa, logo sem manipuladores duplicados).
        try { Seguranca.AplicarAsync(core, PastaSessao, RegistrarBloqueio).GetAwaiter().GetResult(); }
        catch { ApagarPasta(); throw; }
        core.WebMessageReceived += AoReceberMensagem;
        core.ProcessFailed += AoFalharProcesso;
    }

    /// <summary>Sessões com mais de <see cref="IdadeOrfa"/> sem alteração são restos de uma queda do app: apagadas na criação
    /// de um documento. Só entram no alcance subpastas diretas de <c>&lt;dados&gt;\sessao</c> (nunca links simbólicos).</summary>
    internal static readonly TimeSpan IdadeOrfa = TimeSpan.FromDays(1);

    static void LimparOrfas(string raizSessoes)
    {
        if (!Directory.Exists(raizSessoes)) return;
        var raiz = Path.TrimEndingDirectorySeparator(Path.GetFullPath(raizSessoes)) + Path.DirectorySeparatorChar;
        foreach (var pasta in Directory.EnumerateDirectories(raizSessoes))
        {
            try
            {
                var info = new DirectoryInfo(pasta);
                var completo = Path.GetFullPath(info.FullName);
                if (!completo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) continue;
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (DateTime.UtcNow - info.LastWriteTimeUtc < IdadeOrfa) continue;
                Directory.Delete(completo, true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                System.Diagnostics.Debug.WriteLine("[sessao] orfa nao apagada: " + e.Message);
            }
        }
    }

    /// <summary>Preenchido quando a pasta da sessão não pôde ser apagada no Dispose (arquivo preso).</summary>
    public Exception? ErroAoApagarPasta { get; private set; }

    /// <summary>Disparado (em qualquer thread) quando a pasta da sessão não pôde ser apagada.</summary>
    public event Action<Exception>? FalhaAoApagarPasta;

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
    public Task CarregarAsync(ResultadoRender r, ConfigFolhas cfg, TimeSpan? limiteNavegacao = null)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(cfg);
        if (r.Erros.Count > 0) throw new ArgumentException("Render com erros de conferência não pode ser carregado: " + string.Join("; ", r.Erros), nameof(r));
        var html = InjetarNoHead(r.Html);
        return naThread(async () =>
        {
            ObjectDisposedException.ThrowIf(desanexado != 0, this);
            await File.WriteAllTextAsync(Path.Combine(PastaSessao, "documento.html"), html, new UTF8Encoding(false));
            await File.WriteAllBytesAsync(Path.Combine(PastaSessao, "paginador.js"), LerPaginador());
            Render = r;
            Config = cfg;
            var espera = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pronto = espera;
            // O parâmetro evita que o WebView2 trate a navegação como recarga do mesmo documento.
            await HostOculto.NavegarParaUrlAsync(core, Seguranca.UrlBase + "documento.html?c=" + Interlocked.Increment(ref carregamentos), limiteNavegacao);
            await espera.Task.WaitAsync(limiteNavegacao ?? LimiteProntoPadrao);
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

    /// <summary>Tempo-limite padrão de <see cref="ComporAsync"/> (relatórios longos com muitas figuras).</summary>
    public static readonly TimeSpan LimiteComporPadrao = TimeSpan.FromSeconds(120);

    /// <summary>Compõe o documento carregado em folhas A4 e devolve o plano de quebras. Ver <see cref="ComporDetalhadoAsync"/>.</summary>
    public async Task<PlanoQuebras> ComporAsync(TimeSpan? limite = null, TimeSpan? limiteEstabilidade = null) =>
        (await ComporDetalhadoAsync(limite, limiteEstabilidade)).Plano;

    /// <summary>Compõe o documento carregado: antes, fixa no core deste documento a escala 1 e a mídia de impressão
    /// (CDP <c>Emulation.setDeviceMetricsOverride</c> e <c>Emulation.setEmulatedMedia</c>), por isso deve rodar no
    /// <see cref="HostOculto"/>, nunca no WebView da tela. Depois manda <c>compor</c> ao paginador, com o sha256 do HTML,
    /// os refs e o cabeçalho/rodapé, e devolve o plano, o número de folhas e os refs de cada folha.
    /// Se o documento não estabilizar (fontes/imagens carregando, altura mudando) em <paramref name="limiteEstabilidade"/>
    /// (padrão do paginador: 30 s), falha com <see cref="ErroWebException"/> de código <c>instavel</c>, sem plano.</summary>
    public async Task<ResultadoComposicao> ComporDetalhadoAsync(TimeSpan? limite = null, TimeSpan? limiteEstabilidade = null)
    {
        var json = await ComporJsonAsync(limite, limiteEstabilidade);
        var plano = PlanoQuebras.DeJson(json.GetProperty("plano"));
        var refs = json.GetProperty("refsPorFolha").Deserialize<List<List<string>>>(Mensagens.Opcoes)
            ?? throw new InvalidDataException("refsPorFolha ausente.");
        var composicao = new ResultadoComposicao(plano, json.GetProperty("folhas").GetInt32(), refs.Select(r => (IReadOnlyList<string>)r).ToList());
        if (plano.Sha256Html != PlanoQuebras.Sha256De(Render!.Html)) throw new InvalidDataException("O plano devolvido é de outro documento.");
        if (plano.InicioDasFolhas.Count != composicao.Folhas || refs.Count != composicao.Folhas)
            throw new InvalidDataException("Plano incoerente: " + plano.InicioDasFolhas.Count + " inícios para " + composicao.Folhas + " folhas.");
        return composicao;
    }

    /// <summary>Resposta bruta (<c>composto</c>) do paginador, para os testes de ida e volta do JSON.</summary>
    internal async Task<JsonElement> ComporJsonAsync(TimeSpan? limite = null, TimeSpan? limiteEstabilidade = null)
    {
        var r = Render ?? throw new InvalidOperationException("Nenhum documento carregado: chame CarregarAsync antes de compor.");
        var cfg = Config!;
        await naThread(async () =>
        {
            ObjectDisposedException.ThrowIf(desanexado != 0, this);
            await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride",
                """{"width":0,"height":0,"deviceScaleFactor":1,"mobile":false}""");
            await core.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia", """{"media":"print"}""");
        });
        var dados = new
        {
            sha256Html = PlanoQuebras.Sha256De(r.Html),
            refs = r.Refs.Select(x => new { seletor = x.Seletor, indice = x.Indice, id = x.Id }).ToArray(),
            config = new { cabecalhoUri = cfg.CabecalhoUri, rodapeEsquerda = cfg.RodapeEsquerda, rodapeDireitaModelo = cfg.RodapeDireitaModelo },
            limiteEstabilidadeMs = limiteEstabilidade?.TotalMilliseconds,
        };
        return await ChamarAsync("compor", dados, limite ?? LimiteComporPadrao);
    }

    /// <summary>Tempo-limite padrão de <see cref="AplicarPlanoAsync"/> (montagem sem medir, espera de imagens).</summary>
    public static readonly TimeSpan LimiteAplicarPadrao = TimeSpan.FromSeconds(60);

    /// <summary>Reconstrói as folhas do documento carregado pelas posições de <paramref name="p"/>, <b>sem medir para
    /// decidir</b>: escala de impressão do plano (<c>--pmvni-k</c>), refs do render, margem de cima pela regra da quebra
    /// forçada, acabamento e numeração. Não muda a emulação do core (serve à tela, com qualquer escala de monitor, e ao
    /// host oculto). A única medida é a altura de cada folha, para informar <see cref="ResultadoAplicacao.FolhasQueTransbordam"/>.
    /// Recusa com <see cref="ArgumentException"/> se o <see cref="PlanoQuebras.Sha256Html"/> não for o do HTML carregado.</summary>
    public async Task<ResultadoAplicacao> AplicarPlanoAsync(PlanoQuebras p, TimeSpan? limite = null, TimeSpan? limiteEstabilidade = null)
    {
        ArgumentNullException.ThrowIfNull(p);
        var r = Render ?? throw new InvalidOperationException("Nenhum documento carregado: chame CarregarAsync antes de aplicar o plano.");
        var sha = PlanoQuebras.Sha256De(r.Html);
        if (!string.Equals(p.Sha256Html, sha, StringComparison.Ordinal))
            throw new ArgumentException("O plano de quebras é de outro documento: foi calculado para o HTML " + p.Sha256Html
                + ", e o carregado é " + sha + ".", nameof(p));
        var cfg = Config!;
        var dados = new
        {
            plano = p,
            sha256Documento = sha,
            refs = r.Refs.Select(x => new { seletor = x.Seletor, indice = x.Indice, id = x.Id }).ToArray(),
            config = new { cabecalhoUri = cfg.CabecalhoUri, rodapeEsquerda = cfg.RodapeEsquerda, rodapeDireitaModelo = cfg.RodapeDireitaModelo },
            limiteEstabilidadeMs = limiteEstabilidade?.TotalMilliseconds,
        };
        var json = await ChamarAsync("aplicarPlano", dados, limite ?? LimiteAplicarPadrao);
        var folhas = json.GetProperty("folhas").GetInt32();
        var transbordam = json.GetProperty("folhasQueTransbordam").Deserialize<List<int>>(Mensagens.Opcoes)
            ?? throw new InvalidDataException("folhasQueTransbordam ausente.");
        if (folhas != p.InicioDasFolhas.Count)
            throw new InvalidDataException("Plano aplicado incoerente: " + folhas + " folhas para " + p.InicioDasFolhas.Count + " inícios.");
        return new ResultadoAplicacao(folhas, transbordam);
    }

    /// <summary>Texto visível da área útil de cada folha (espaços normalizados), na ordem. Sem folhas, lista vazia.</summary>
    public async Task<IReadOnlyList<string>> TextoPorFolhaAsync(TimeSpan? limite = null)
    {
        var json = await ChamarAsync("textoPorFolha", new { }, limite ?? TimeSpan.FromSeconds(30));
        return json.GetProperty("folhas").Deserialize<List<string>>(Mensagens.Opcoes) ?? throw new InvalidDataException("folhas ausente.");
    }

    /// <summary>PNG da folha <paramref name="folha"/> (1 = primeira) em <paramref name="dpi"/>: CDP <c>Page.captureScreenshot</c>
    /// com o <c>clip</c> da folha (coordenadas do documento), <c>scale = dpi / 96</c> e <c>captureBeyondViewport</c>.</summary>
    public Task<byte[]> CapturarFolhaAsync(int folha, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(folha, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(dpi, 24);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(dpi, 1200);
        return NaThreadAsync(async () =>
        {
            var expressao = "(() => { const f = document.querySelectorAll('section.folha')[" + (folha - 1) + "]; if (!f) return null;"
                + " const r = f.getBoundingClientRect(); return { x: r.left + scrollX, y: r.top + scrollY, w: r.width, h: r.height }; })()";
            var resposta = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate",
                JsonSerializer.Serialize(new { expression = expressao, returnByValue = true }));
            using var doc = JsonDocument.Parse(resposta);
            var valor = doc.RootElement.GetProperty("result");
            if (!valor.TryGetProperty("value", out var q) || q.ValueKind != JsonValueKind.Object)
                throw new ArgumentOutOfRangeException(nameof(folha), folha, "O documento não tem a folha " + folha + ".");
            var clip = new
            {
                x = q.GetProperty("x").GetDouble(), y = q.GetProperty("y").GetDouble(),
                width = q.GetProperty("w").GetDouble(), height = q.GetProperty("h").GetDouble(), scale = dpi / 96.0,
            };
            var captura = await core.CallDevToolsProtocolMethodAsync("Page.captureScreenshot",
                JsonSerializer.Serialize(new { format = "png", clip, captureBeyondViewport = true, fromSurface = true }));
            using var c = JsonDocument.Parse(captura);
            return Convert.FromBase64String(c.RootElement.GetProperty("data").GetString()!);
        });
    }

    /// <summary>Imprime o DOM atual (as folhas compostas) com CDP <c>Page.printToPDF</c>: tamanho da regra @page de folha.css,
    /// fundos, sem cabeçalho/rodapé do navegador, margens 0.</summary>
    internal Task<byte[]> ImprimirPdfAsync() =>
        NaThreadAsync(async () =>
        {
            var r = await core.CallDevToolsProtocolMethodAsync("Page.printToPDF", JsonSerializer.Serialize(new
            {
                preferCSSPageSize = true, printBackground = true, displayHeaderFooter = false,
                marginTop = 0, marginBottom = 0, marginLeft = 0, marginRight = 0,
            }));
            using var doc = JsonDocument.Parse(r);
            return Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!);
        });

    /// <summary>Versão do runtime do WebView2 deste documento (<c>BrowserVersionString</c>).</summary>
    internal Task<string> VersaoWebView2Async() => NaThreadAsync(() => Task.FromResult(core.Environment.BrowserVersionString));

    /// <summary>Roda <paramref name="acao"/> na thread do core (documento ainda anexado) e devolve o resultado.</summary>
    async Task<T> NaThreadAsync<T>(Func<Task<T>> acao)
    {
        T resultado = default!;
        await naThread(async () =>
        {
            ObjectDisposedException.ThrowIf(desanexado != 0, this);
            resultado = await acao();
        });
        return resultado;
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
        try { core.ClearVirtualHostNameToFolderMapping(Seguranca.HostDocumento); } catch { }
    }

    /// <summary>Apaga a pasta da sessão, com tentativas (o WebView2 pode demorar a soltar os arquivos). Se não conseguir,
    /// registra em <see cref="ErroAoApagarPasta"/> e dispara <see cref="FalhaAoApagarPasta"/>.</summary>
    internal async Task ApagarPastaAsync()
    {
        if (Interlocked.Exchange(ref pastaApagada, 1) != 0) return;
        Exception? ultimo = null;
        for (var i = 0; i < 40; i++)
        {
            try { if (Directory.Exists(PastaSessao)) Directory.Delete(PastaSessao, true); return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { ultimo = e; }
            await Task.Delay(50).ConfigureAwait(false);
        }
        ErroAoApagarPasta = ultimo;
        if (ultimo is not null) try { FalhaAoApagarPasta?.Invoke(ultimo); } catch { }
    }

    /// <summary>Versão síncrona (para <see cref="Dispose"/>); roda fora do contexto de sincronização para não travar.</summary>
    internal void ApagarPasta() => Task.Run(ApagarPastaAsync).GetAwaiter().GetResult();

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
        if (!Seguranca.EhHostDocumento(e.Source))
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
