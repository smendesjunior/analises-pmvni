using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;

namespace Pmvni.App;

internal enum EtapaAtualizacao { Verificando, EmDia, Baixando, Pronta, Falhou, Desligada }

internal sealed record EstadoAtualizacao(EtapaAtualizacao Etapa, string Texto, string? Notas = null);

/// <summary>
/// Atualização automática do editor (no padrão do <c>AppUpdater</c> do WinStatusBar): consulta a Release mais recente do GitHub
/// (<see cref="RegrasAtualizacao.UrlManifesto"/>), baixa o executável novo para <c>&lt;dados&gt;\atualizacoes</c>, confere tamanho e SHA-256 e
/// troca o .exe em uso (<see cref="RegrasAtualizacao.TrocarExecutavel"/>). Nada é instalado e nada pede administrador: a versão nova vale ao
/// abrir o editor de novo ("Reiniciar agora" em Ajustes › Sistema). Uma consulta por vez; o estado vai para a página por
/// <see cref="EstadoMudou"/>. Compilações de desenvolvimento (sem carimbo) e o autoteste não se atualizam.
/// </summary>
internal static class Atualizador
{
    static readonly object Trava = new();
    static Task<EstadoAtualizacao>? atual;
    static int falhasSeguidas;
    static string? compilacaoInstalada;
    static DateTimeOffset? reabrirPedidoEm;

    static string Pasta => Path.Combine(PastaDados.Raiz, "atualizacoes");
    static string Baixado => Path.Combine(Pasta, RegrasAtualizacao.NomeExecutavelPublicado);

    /// <summary>Carimbo aaaaMMddHHmmss gravado pelo build-release (<c>-p:CompilacaoAtualizacao=…</c>); null num build de desenvolvimento.</summary>
    public static string? Compilacao { get; } = typeof(Atualizador).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "CompilacaoAtualizacao")?.Value is { } c && RegrasAtualizacao.CompilacaoValida(c) ? c : null;

    public static string Versao { get; } = typeof(Atualizador).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "0.0.0";
    /// <summary>A versão como o usuário a vê ("1.0" para a 1.0.0).</summary>
    public static string VersaoExibida => RegrasAtualizacao.VersaoExibida(Versao);

    /// <summary>O executável em uso (o .exe único publicado).</summary>
    static string? ExeAtual => Environment.ProcessPath is { } p && p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : null;

    /// <summary>Executável publicado e carimbado, fora do autoteste.</summary>
    /// A cópia de desenvolvimento (Aplicativo\ do projeto) não se atualiza: só a cópia de uso, instalada pelo tools\instalar.cmd.
    public static bool Habilitado => Compilacao is not null && ExeAtual is not null && !Autoteste.Ativo && Argumentos.Atual.Roteiro is null
        && !Pmvni.Composicao.Web.PastaDados.CopiaDeDesenvolvimento
        && Environment.GetEnvironmentVariable("PMVNI_SEM_ATUALIZACAO") is not "1";

    public static EstadoAtualizacao? UltimoEstado { get; private set; }
    public static event Action<EstadoAtualizacao>? EstadoMudou;

    /// <summary>A consulta automática vale (Ajustes › Sistema › "Atualizar automaticamente"); lido a cada volta.</summary>
    public static Func<bool> AutomaticaLigada { get; set; } = () => true;

    static readonly HttpClient Http = CriarCliente();

    /// <summary>Ao abrir: apaga os .exe antigos de trocas anteriores e começa as consultas automáticas.</summary>
    public static void Iniciar()
    {
        if (ExeAtual is { } exe && Path.GetDirectoryName(exe) is { } pasta) RegrasAtualizacao.LimparAntigos(pasta);
        if (!Habilitado) return;
        relogio = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
        relogio.IsRepeating = false;
        proxima = DateTimeOffset.UtcNow + RegrasAtualizacao.PrimeiraConsulta;
        relogio.Tick += async (_, _) =>
        {
            consultando = true;
            try
            {
                if (AutomaticaLigada()) await VerificarAsync();
            }
            finally
            {
                consultando = false;
                proxima = DateTimeOffset.UtcNow + RegrasAtualizacao.ProximaConsulta(falhasSeguidas);
                Agendar(ativa);
            }
        };
    }

    static Microsoft.UI.Dispatching.DispatcherQueueTimer? relogio;
    static DateTimeOffset proxima;
    static bool ativa, consultando;

    public static void Agendar(bool janelaAtiva)
    {
        ativa = janelaAtiva;
        if (relogio is null) return;
        relogio.Stop();
        if (!ativa || consultando || UltimoEstado?.Etapa == EtapaAtualizacao.Pronta) return;
        var restante = proxima - DateTimeOffset.UtcNow;
        relogio.Interval = restante > TimeSpan.Zero ? restante : TimeSpan.FromSeconds(1);
        relogio.Start();
    }

    /// <summary>Consulta agora (botão "Verificar atualizações" ou automática). Se já houver uma em andamento, devolve a mesma.</summary>
    public static Task<EstadoAtualizacao> VerificarAsync()
    {
        lock (Trava)
        {
            if (atual is { IsCompleted: false }) return atual;
            atual = Task.Run(ExecutarAsync);
            return atual;
        }
    }

    static async Task<EstadoAtualizacao> ExecutarAsync()
    {
        if (!Habilitado) return Avisar(new(EtapaAtualizacao.Desligada, Pmvni.Composicao.Web.PastaDados.CopiaDeDesenvolvimento ? "Cópia de desenvolvimento (Aplicativo\\ do projeto): não se atualiza. A cópia de uso, instalada pelo tools\\instalar.cmd, se atualiza pelo GitHub." : "Compilação de desenvolvimento: a atualização automática fica desligada."));
        if (compilacaoInstalada is { } ja)
            return Avisar(new(EtapaAtualizacao.Pronta, $"A versão nova (compilação {ja}) já foi instalada. Ela vale ao abrir o editor de novo.", UltimoEstado?.Notas));
        var parcial = Baixado + ".part";
        try
        {
            Avisar(new(EtapaAtualizacao.Verificando, "Procurando…"));
            var m = RegrasAtualizacao.Ler(await LerManifestoAsync())
                ?? throw new InvalidDataException("O arquivo de controle da atualização está inválido. O editor tenta de novo sozinho em 15 minutos.");
            if (!RegrasAtualizacao.MaisNova(m, Compilacao!))
            {
                falhasSeguidas = 0;
                try { if (File.Exists(Baixado)) File.Delete(Baixado); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                return Avisar(new(EtapaAtualizacao.EmDia, $"Você já tem a versão mais recente ({VersaoExibida})", m.Notas));
            }
            if (!RegrasAtualizacao.Aceitavel(m, Compilacao!))
                throw new InvalidDataException("A versão encontrada não passou na conferência de segurança e foi ignorada.");

            var rotulo = m.Versao is { } mv ? RegrasAtualizacao.VersaoExibida(mv) : m.Compilacao;
            Directory.CreateDirectory(Pasta);
            if (!RegrasAtualizacao.PodeReaproveitar(Baixado, m))
            {
                Avisar(new(EtapaAtualizacao.Baixando, $"Baixando a versão {rotulo}… 0%", m.Notas));
                await BaixarAsync(m.Url!, parcial, m.Tamanho, rotulo, m.Notas);
                RegrasAtualizacao.Conferir(parcial, m);
                File.Move(parcial, ArquivoGerado.Caminho(Baixado), overwrite: true);
            }
            RegrasAtualizacao.TrocarExecutavel(ExeAtual!, Baixado, m);
            try { File.Delete(Baixado); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            compilacaoInstalada = m.Compilacao;
            falhasSeguidas = 0;
            return Avisar(new(EtapaAtualizacao.Pronta, $"Versão {rotulo} instalada. Ela vale ao abrir o editor de novo (ou clique em Reiniciar agora).", m.Notas));
        }
        catch (Exception e)
        {
            try { if (File.Exists(parcial)) File.Delete(parcial); } catch { }
            falhasSeguidas++;
            RegistroErros.Gravar("atualizacao", e);
            return Avisar(new(EtapaAtualizacao.Falhou, Explicar(e), UltimoEstado?.Notas));
        }
    }

    /// <summary>"Reiniciar agora": a janela fecha pelo caminho normal (grava e pergunta o que for preciso) e o editor abre de novo, já na
    /// versão nova, assim que o processo terminar (<see cref="ReabrirSePedido"/>). Vale por 10 minutos: quem desistiu de fechar não é
    /// surpreendido por uma reabertura depois.</summary>
    public static void PedirReabertura() => reabrirPedidoEm = DateTimeOffset.UtcNow;

    /// <summary>Chamado no fim do <c>Main</c>: abre o executável (já trocado) se "Reiniciar agora" foi pedido há pouco.</summary>
    public static void ReabrirSePedido()
    {
        if (reabrirPedidoEm is not { } quando || DateTimeOffset.UtcNow - quando > TimeSpan.FromMinutes(10) || ExeAtual is not { } exe) return;
        try { TravaDeProgramas.Iniciar(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose(); }
        catch (Exception e) { RegistroErros.Gravar("atualizacao.reabrir", e); }
    }

    static async Task<string> LerManifestoAsync()
    {
        using var pedido = new HttpRequestMessage(HttpMethod.Get, RegrasAtualizacao.UrlManifesto);
        pedido.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var resposta = await Http.SendAsync(pedido);
        resposta.EnsureSuccessStatusCode();
        return await resposta.Content.ReadAsStringAsync();
    }

    static async Task BaixarAsync(string url, string destino, long total, string rotulo, string? notas)
    {
        // Sem limite de tempo total; só desiste se ficar 2 minutos sem receber nada.
        using var parado = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var resposta = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, parado.Token);
        resposta.EnsureSuccessStatusCode();
        await using var origem = await resposta.Content.ReadAsStreamAsync(parado.Token);
        await using var arquivo = new FileStream(ArquivoGerado.Caminho(destino), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 20];
        long feito = 0;
        int ultimo = 0;
        while (true)
        {
            parado.CancelAfter(TimeSpan.FromMinutes(2));
            int lidos = await origem.ReadAsync(buffer, parado.Token);
            if (lidos == 0) break;
            await arquivo.WriteAsync(buffer.AsMemory(0, lidos), parado.Token);
            feito += lidos;
            if (feito > total) throw new InvalidDataException("O download veio maior que a publicação; a atualização fica para a próxima consulta.");
            int pct = (int)(feito * 100 / total);
            if (pct != ultimo) { ultimo = pct; Avisar(new(EtapaAtualizacao.Baixando, $"Baixando a versão {rotulo}… {pct}%", notas)); }
        }
    }

    const string TentaDeNovo = " O editor tenta de novo sozinho em 15 minutos.";
    const int DiscoCheio = unchecked((int)0x80070070), DiscoCheio2 = unchecked((int)0x80070027);

    /// <summary>Por que a verificação falhou, em português simples (como o <c>UpdateErrorText</c> do WinStatusBar).</summary>
    static string Explicar(Exception e) => e switch
    {
        InvalidDataException => e.Message,
        IOException io when io.HResult is DiscoCheio or DiscoCheio2 => "Falta espaço no disco para baixar a atualização (cerca de 150 MB).",
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "Ainda não há versão publicada no GitHub." + TentaDeNovo,
        HttpRequestException or SocketException => "Sem conexão com o GitHub." + TentaDeNovo,
        OperationCanceledException or TimeoutException => "O GitHub demorou demais para responder." + TentaDeNovo,
        UnauthorizedAccessException => "Sem permissão para trocar o executável na pasta do editor. Copie a versão nova manualmente ou mova o editor para uma pasta sua.",
        HttpIOException or IOException { InnerException: SocketException } => "A conexão com o GitHub caiu durante o download." + TentaDeNovo,
        IOException => "Não foi possível trocar o executável (" + e.Message + ")." + TentaDeNovo,
        _ => "Não foi possível verificar atualizações: " + e.Message,
    };

    static EstadoAtualizacao Avisar(EstadoAtualizacao e)
    {
        UltimoEstado = e;
        try { EstadoMudou?.Invoke(e); } catch (Exception ex) { RegistroErros.Gravar("atualizacao.aviso", ex); }
        return e;
    }

    static HttpClient CriarCliente()
    {
        var http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            // Proxy corporativo com login do Windows (NTLM/Kerberos): sem isto, toda consulta falharia com 407.
            DefaultProxyCredentials = CredentialCache.DefaultCredentials,
        }) { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("EditorRelatoriosPMVNI-Atualizador/" + Versao);
        return http;
    }
}
