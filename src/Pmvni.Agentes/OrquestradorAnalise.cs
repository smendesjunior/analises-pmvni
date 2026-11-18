using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

/// <summary>Tudo o que uma execução precisa: o provedor, o processo, as peças achadas, o modo de acesso, a pasta de dados do app (onde mora a chave), o texto
/// digitado, os anexos já arquivados e o registro criado no clique.</summary>
public sealed record PedidoExecucao(Provedor Provedor, Processo Processo, Pecas Pecas, ModoAcesso Modo, string RaizDados, string? Texto,
    IReadOnlyList<ArquivoArquivado> Anexos, RegistroExecucao Registro)
{
    /// <summary>ChatGPT: a sessão do Codex a continuar (<c>codex exec resume</c>) em vez de começar do zero.</summary>
    public string? RotaCodex { get; init; }
    public string? RetomarSessao { get; init; }

    /// <summary>Gemini: o perfil (conta Ultra, API com créditos ou API gratuita) fixado para a análise inteira; null nos outros provedores e na leitura
    /// da situação.</summary>
    public PerfilGemini? Perfil { get; init; }
    public bool TrocarGemini { get; init; }

    /// <summary>Gemini, perfil gratuito: o usuário declarou no clique que o processo é fictício (o plugin recusa a partida sem essa declaração).</summary>
    public bool Ficticio { get; init; }
}

/// <summary>Uma linha de andamento para a tela (do Codex, ou do <c>auto-log.txt</c> do kit) e/ou a situação mais recente do kit.</summary>
public sealed record ProgressoAnalise(string? Linha, SituacaoKit? Situacao);

/// <summary><c>Situacao</c>: <c>concluida</c> | <c>falhou</c> | <c>cancelada</c> | <c>sem_resultado</c> | <c>interrompida</c> | <c>acompanhamento_interrompido</c>
/// (Claude: o editor parou de acompanhar; a análise do kit continua) | <c>cota_esgotada</c> (ChatGPT: o limite de uso acabou; dá para continuar depois).</summary>
public sealed record FimAnalise(string Situacao, string? Documento, string? Pdf, string? Mensagem, int? Codigo);

/// <summary>O intermediário (desenho §12.1): inicia o agente instalado neste computador, acompanha, guarda o registro e localiza o resultado que o próprio kit
/// declara. Não analisa, não chama modelo e não interpreta o relatório.
/// ChatGPT: <c>codex exec</c> como processo filho (o cancelamento mata a árvore). Claude: dá a partida do analise-pro (<c>pro_runtime.py … iniciar</c>); a análise roda
/// sozinha, fora do editor, e o editor só acompanha (<c>situacao</c> e <c>auto-log.txt</c>); parar de acompanhar não a encerra (<see cref="CancelarClaudeAsync"/> sim).</summary>
public sealed class OrquestradorAnalise
{
    public const string AcompanhamentoInterrompido = "acompanhamento_interrompido";
    const int LinhasDoLog = 2000;
    const int FalhasSeguidasDaSituacao = 5;
    static readonly TimeSpan LimiteIniciar = TimeSpan.FromMinutes(2);
    static readonly TimeSpan LimiteSituacao = TimeSpan.FromMinutes(1);

    public TimeSpan IntervaloSituacao { get; init; } = TimeSpan.FromSeconds(10);

    public TimeSpan IntervaloLog { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>Só testes e roteiro (<c>AGENTE_FALSO_CENARIO</c>): variáveis a mais no ambiente do filho, postas ANTES de tirar as chaves herdadas.</summary>
    public IReadOnlyDictionary<string, string>? AmbienteExtra { get; init; }

    /// <summary>O job em que o Codex entra (encerrado quando o editor termina, até numa queda). Padrão: o do editor; sem job (Windows recusou), segue sem.</summary>
    public TarefaDoEditor? TarefaCodex { get; init; } = TarefaPadrao();

    static TarefaDoEditor? TarefaPadrao()
    {
        try { return TarefaDoEditor.Padrao; }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    // ------------------------------------------------------------------ comum

    Action<IDictionary<string, string?>> Ambiente(Provedor p, ModoAcesso modo, string? chave, PerfilGemini? perfil = null, bool ficticio = false, bool trocarGemini = false, RegistroExecucao? registro = null) => env =>
    {
        if (AmbienteExtra is not null)
            foreach (var (k, v) in AmbienteExtra) env[k] = v;
        AmbienteFilho.Aplicar(env, p, modo, chave, perfil, ficticio, trocarGemini);
        if (registro is not null)
        {
            env["PMVNI_EXECUCAO_PASTA"] = registro.Pasta;
            env["PMVNI_EXECUCAO_ID"] = Path.GetFileName(registro.Pasta);
        }
    };

    static FimAnalise Falha(string mensagem, int? codigo = null) => new("falhou", null, null, mensagem, codigo);

    /// <param name="travaPega">A trava do processo que quem chama já pegou (a página pega antes de copiar os anexos e a entrega aqui, sem soltar no meio);
    /// passa a ser deste método, que a solta no fim. null = o método pega a sua.</param>
    public async Task<FimAnalise> ExecutarAsync(PedidoExecucao pe, IProgress<ProgressoAnalise> progresso, CancellationToken ct, TravaExecucao? travaPega = null)
    {
        pe = pe with { Modo = ModoEfetivo(pe.Provedor, pe.Modo, pe.Pecas) };
        var impedimento = Conferir(pe);
        if (impedimento is not null)
        {
            travaPega?.Dispose();
            Encerrar(pe, impedimento, DateTimeOffset.UtcNow);
            return impedimento;
        }

        using var trava = travaPega ?? TravaExecucao.Tentar(pe.Processo.Pasta, pe.Provedor);
        if (trava is null)
        {
            var recusa = Falha("Já há uma análise em andamento neste processo.");
            Encerrar(pe, recusa, DateTimeOffset.UtcNow);
            return recusa;
        }

        var inicio = DateTimeOffset.UtcNow;
        string? chave = null;
        if (pe.Modo == ModoAcesso.Chave) chave = CofreChaves.Ler(pe.RaizDados, pe.Provedor);   // já conferida em Conferir

        FimAnalise fim;
        try
        {
            fim = pe.Provedor == Provedor.ChatGpt
                ? await ExecutarChatGptAsync(pe, chave, inicio, progresso, ct).ConfigureAwait(false)
                : await ExecutarPeloKitAsync(pe, chave, inicio, progresso, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Claude: o editor só parou de acompanhar (fechou); a análise do kit segue, então o registro nunca diz "cancelada".
            fim = pe.Provedor == Provedor.ChatGpt
                ? new FimAnalise("cancelada", null, null, "A análise foi cancelada.", null)
                : new FimAnalise(AcompanhamentoInterrompido, null, null, "O acompanhamento foi interrompido; a análise do kit continua sozinha na janela minimizada.", null);
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            fim = Falha("A análise não pôde ser executada: " + e.Message.TrimEnd('.') + ". Veja o registro: " + pe.Registro.Pasta + ".");
        }
        Encerrar(pe, fim, inicio);
        return fim;
    }

    /// <summary>As peças que faltam e os motivos exatos (desenho §12.11): nada é iniciado nem arquivado.</summary>
    static FimAnalise? Conferir(PedidoExecucao pe)
    {
        var pecas = pe.Pecas;
        if (pe.Provedor == Provedor.ChatGpt)
        {
            if (pecas.Codex is null)
                return Falha("O Codex não foi encontrado neste computador. Instale o app do ChatGPT/Codex ou escolha o programa em Ajustes › Inteligência artificial.");
            if (!pecas.PluginAnaliseHabilitado)
                return Falha("O plugin analise do kit PMVNI não está habilitado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).");
        }
        else if (pe.Provedor == Provedor.Gemini)
        {
            if (pecas.Python is null) return Falha("O Python não foi encontrado neste computador (o análise Google do kit precisa dele).");
            if (pecas.GoogleRuntime is null) return Falha(SemAnaliseGoogle);
            if (pe.Modo != ModoAcesso.ContaGemini && pecas.Agy is null) return Falha(SemAgy);
            if (pe.Perfil is not { } perfil)
                return Falha(pe.Modo == ModoAcesso.Chave ? SemPerfilApi : "O perfil do Gemini não foi informado. Nada foi iniciado.");
            if (MatrizGemini.Modo(perfil) != pe.Modo)
                return Falha("O perfil do Gemini (" + MatrizGemini.Nome(perfil) + ") não corresponde ao modo escolhido. Nada foi iniciado: a análise não troca de modo sozinha.");
            if (perfil == PerfilGemini.ApiGratuita && !pe.Ficticio)
                return Falha("O perfil API gratuita só roda testes com dados fictícios e precisa da sua confirmação no clique. Nada foi iniciado.");
        }
        else
        {
            if (pecas.Python is null) return Falha("O Python não foi encontrado neste computador (o análise pro do kit precisa dele).");
            if (pecas.ProRuntime is null)
                return Falha("O plugin análise pro do kit PMVNI não está instalado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).");
            if (pecas.Claude is null) return Falha("O Claude Code não foi encontrado neste computador (claude.exe).");
            // 2026-11-17: modo chave = Anthropic API de fato. Plugin que não aceita a rota explícita: recusa, nunca a conta no lugar.
            if (pe.Modo == ModoAcesso.Chave && !pecas.ApiPermitidaNoKit) return Falha(ApiNaoSuportada(pecas.VersaoAnalisePro));
        }
        if (pe.Modo == ModoAcesso.Chave && CofreChaves.Ler(pe.RaizDados, pe.Provedor) is null)
            return Falha("Guarde a chave de API em Ajustes › Inteligência artificial ou use a sua conta.");
        return null;
    }

    /// <summary>O modo que a análise usa de fato: sempre o escolhido em Ajustes (2026-11-17, análise pro 1.6.4). Não há mais conversão de "chave" para
    /// "conta": com um análise pro que não aceita a Anthropic API, o modo chave é recusado (<see cref="ApiNaoSuportada"/>), nunca trocado.</summary>
    public static ModoAcesso ModoEfetivo(Provedor p, ModoAcesso m, Pecas pecas)
    {
        ArgumentNullException.ThrowIfNull(pecas);
        _ = p;
        return m;
    }

    /// <summary>A recusa do modo chave do Claude quando o análise pro instalado não aceita a rota API explícita (anterior à 1.6.4).</summary>
    public static string ApiNaoSuportada(string? versaoInstalada) =>
        $"O análise pro instalado ({versaoInstalada ?? "versão desconhecida"}) não aceita a Anthropic API; é preciso o 1.6.4 ou mais novo. "
        + "Atualize pelo INSTALAR_KIT_PMVNI.cmd (fora do editor) ou escolha Usar minha conta em Ajustes › Inteligência artificial. "
        + "Nada foi iniciado: a análise não passa para a conta sem a sua escolha.";

    /// <summary>Como o análise pro se autentica no Claude, para o registro: <c>anthropic_api</c> (modo chave) ou <c>conta_claude</c>.</summary>
    public static string AutenticacaoAnalisePro(ModoAcesso m) => m == ModoAcesso.Chave ? "anthropic_api" : "conta_claude";

    /// <summary>Como o análise Google se autentica, para o registro: <c>gemini_api</c> (chave do Google AI Studio) ou <c>conta_google</c> (login do Antigravity).</summary>
    public static string AutenticacaoAnaliseGoogle(ModoAcesso m) => m switch
    {
        ModoAcesso.Chave => "gemini_api",
        ModoAcesso.ContaGemini => "gemini_web",
        _ => "conta_google",
    };

    public const string SemAnaliseGoogle = "O plugin análise Google do kit PMVNI não está instalado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).";
    public const string SemAgy = "O Antigravity CLI (agy.exe) não foi encontrado neste computador. Instale-o pelo instalador oficial do Google Antigravity e entre na conta Google.";
    public const string SemPerfilApi = "Escolha o perfil da chave do Gemini em Ajustes › Inteligência artificial: API com créditos ou API gratuita (só testes fictícios). "
        + "O plano de uma chave não pode ser deduzido, e o editor não presume nenhum.";

    /// <summary>O nome do plugin do kit nas mensagens: "análise pro" (Claude) ou "análise Google" (Gemini).</summary>
    public static string NomeDoKit(Provedor p) => p == Provedor.Gemini ? "análise Google" : "análise pro";

    static string Usuario()
    {
        try { return Environment.UserName; }
        catch (InvalidOperationException) { return ""; }
    }

    /// <summary>O plugin do kit que cada provedor usa: ChatGPT (Codex) = analise; Claude = análise pro.</summary>
    public static string PluginUsado(Provedor p) => p switch { Provedor.ChatGpt => "analise", Provedor.Claude => "análise pro", _ => "análise Google" };

    /// <summary>A versão instalada desse plugin (a maior no cache de plugins do Codex); null se não foi achada.</summary>
    public static string? VersaoDoPlugin(Provedor p, Pecas? pecas) =>
        p switch { Provedor.ChatGpt => pecas?.VersaoPluginAnalise, Provedor.Claude => pecas?.VersaoAnalisePro, _ => pecas?.VersaoAnaliseGoogle };

    static string VersaoEditor() =>
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? typeof(OrquestradorAnalise).Assembly.GetName().Version?.ToString() ?? "";

    static void Iniciou(PedidoExecucao pe, DateTimeOffset inicio, string programa, IReadOnlyList<string> linha)
    {
        pe.Registro.Atualizar(o =>
        {
            o["provedor"] = Provedores.Sufixo(pe.Provedor);
            o["modo"] = pe.Modo switch { ModoAcesso.Chave => "chave", ModoAcesso.ContaGemini => "conta_gemini", _ => "conta" };    // nunca a chave
            o["programa"] = programa;
            o["linha_de_comando"] = new JsonArray(linha.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray());
            o["processo"] = pe.Processo.Pasta;
            o["inicio"] = inicio.ToString("o", CultureInfo.InvariantCulture);
            o["usuario"] = Usuario();
            o["versao_do_editor"] = VersaoEditor();
            // 2026-11-16: o plugin do kit e a versão instalada que fizeram a análise (auditável depois).
            o["plugin"] = PluginUsado(pe.Provedor);
            o["versao_do_plugin"] = VersaoDoPlugin(pe.Provedor, pe.Pecas) ?? "não encontrada";
            if (pe.Provedor == Provedor.Claude) o["autenticacao"] = AutenticacaoAnalisePro(pe.Modo);    // nunca a chave
            if (pe.Provedor == Provedor.Gemini)
            {
                o["autenticacao"] = AutenticacaoAnaliseGoogle(pe.Modo);    // nunca a chave
                o["perfil"] = pe.Perfil is { } perfil ? MatrizGemini.Codigo(perfil) : null;
            }
            o["situacao"] = "em_andamento";
        });
    }

    /// <summary>Grava o fim no execucao.json (a chave não entra) e esvazia o que estava na memória do registro.</summary>
    static void Encerrar(PedidoExecucao pe, FimAnalise fim, DateTimeOffset inicio)
    {
        try
        {
            pe.Registro.Descarregar();      // não lança; o que não pôde ser gravado vira o aviso abaixo
            var aviso = pe.Registro.Aviso;
            pe.Registro.Atualizar(o =>
            {
                if (aviso is not null) o["aviso_registro"] = aviso;
                o["provedor"] ??= Provedores.Sufixo(pe.Provedor);
                o["modo"] ??= pe.Modo switch { ModoAcesso.Chave => "chave", ModoAcesso.ContaGemini => "conta_gemini", _ => "conta" };
                o["inicio"] ??= inicio.ToString("o", CultureInfo.InvariantCulture);
                o["fim"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                o["situacao"] = fim.Situacao;
                o["codigo"] = fim.Codigo;
                o["mensagem"] = fim.Mensagem;
                o["document"] = fim.Documento;
                o["pdf"] = fim.Pdf;
            });
            pe.Registro.Descarregar();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* o registro não pode derrubar o resultado */ }
    }

    static void Registrar(Exception e)
    {
        try { MensagensDeErro.Registrar?.Invoke(e); }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
    }

    static void Anunciar(IProgress<ProgressoAnalise> progresso, ProgressoAnalise p)
    {
        try { progresso.Report(p); }
        catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { /* a tela já foi embora */ }
    }

    // ------------------------------------------------------------------ ChatGPT (Codex)

    async Task<FimAnalise> ExecutarChatGptAsync(PedidoExecucao pe, string? chave, DateTimeOffset inicio, IProgress<ProgressoAnalise> progresso, CancellationToken ct)
    {
        var registro = pe.Registro;
        var pasta = pe.Processo.Pasta;
        var ultima = Path.Combine(registro.Pasta, "ultima-mensagem.md");
        string pedido;
        (string Exe, IReadOnlyList<string> Args) linha;
        if (pe.RetomarSessao is { } sessao)
        {
            // Continuação (2026-11-17): o pedido e os anexos originais ficam como estavam; o Codex recebe só a ordem de seguir.
            pedido = PedidoAgente.Continuar;
            linha = LinhasDeComando.CodexRetomar(pe.Pecas.Codex!, sessao, ultima);
        }
        else
        {
            pedido = PedidoAgente.Codex(pasta, pe.Processo, pe.Anexos, pe.Texto);
            registro.GravarPedido(pedido);
            registro.GravarAnexos(pe.Anexos);
            linha = LinhasDeComando.CodexExec(pe.Pecas.Codex!, pasta, ultima);
        }
        pedido += "\n" + registro.InstrucoesProgresso();
        var (exe, args) = linha;
        if (pe.Modo == ModoAcesso.Conta && pe.RotaCodex is "openai" or "codex-lb")
            args = ["-c", $"model_provider=\"{pe.RotaCodex}\"", .. args];
        Iniciou(pe, inicio, exe, new[] { exe }.Concat(args).ToList());
        registro.Atualizar(o =>
        {
            o["entrada_padrao"] = pe.RetomarSessao is null ? "pedido.md" : "continuar";
            if (pe.RetomarSessao is { } s) { o["sessao_codex"] = s; o["continuacoes"] = (o["continuacoes"]?.GetValue<int>() ?? 0) + 1; }
        });
        var cota = false;

        var redator = new RedatorChave(chave);      // a chave nunca vai para o registro nem para a tela, nem se o agente a repetir
        var canal = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        using var execucao = ExecucaoProcesso.Iniciar(exe, args, pasta, pedido, Ambiente(Provedor.ChatGpt, pe.Modo, chave, registro: registro),
            linha => canal.Writer.TryWrite(redator.Aplicar(linha)),
            linha => { if (EventosCodex.CotaEsgotada(linha)) cota = true; registro.AcrescentarErro(redator.Aplicar(linha)); }, TarefaCodex);
        using var cancelamento = ct.Register(execucao.Cancelar);

        // A saída vai para uma fila sem limite: o agente nunca espera pelo editor (nem pela tela). Um só consumidor grava o saida.jsonl e anuncia.
        var consumidor = Task.Run(async () =>
        {
            var leitor = canal.Reader;
            var desdeODescarregar = 0;
            while (await leitor.WaitToReadAsync().ConfigureAwait(false))
            {
                while (leitor.TryRead(out var linha))
                {
                    // Nada aqui pode matar o consumidor (o andamento congelaria e o fim viraria "falhou"): o registro não lança e o resto é contido.
                    try
                    {
                        registro.AcrescentarSaida(linha);
                        if (EventosCodex.Sessao(linha) is { } id) { registro.Atualizar(o => o["sessao_codex"] = id); registro.Descarregar(); }
                        if (EventosCodex.Linha(linha) is { } texto)
                        {
                            if (texto.StartsWith("Erro: ", StringComparison.Ordinal) && EventosCodex.CotaEsgotada(texto)) cota = true;
                            Anunciar(progresso, new ProgressoAnalise(texto, null));
                        }
                    }
                    catch (Exception e) when (e is not OutOfMemoryException) { Registrar(e); }
                    if (++desdeODescarregar >= 5000) { registro.Descarregar(); desdeODescarregar = 0; }
                }
                registro.Descarregar();
                desdeODescarregar = 0;
            }
        }, CancellationToken.None);

        var codigo = await execucao.Fim.ConfigureAwait(false);
        canal.Writer.TryComplete();
        await consumidor.ConfigureAwait(false);
        RedigirArquivo(Path.Combine(registro.Pasta, "ultima-mensagem.md"), redator);     // este arquivo é escrito pelo próprio Codex
        registro.Descarregar();

        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        if (cota)
            return new FimAnalise("cota_esgotada", null, null,
                "A cota de uso do ChatGPT acabou no meio da análise. O que já foi feito está guardado: quando a cota voltar, clique em \"Continuar a análise\" e ela segue de onde parou.", codigo);
        if (codigo != 0)
            return new FimAnalise("falhou", null, null, $"O Codex terminou com o código {codigo}. Veja o registro: {registro.Pasta}.", codigo);

        var r = ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, inicio.UtcDateTime, registro.Pasta);
        return r.Documento is null
            ? new FimAnalise("sem_resultado", null, null, r.Motivo, codigo)
            : new FimAnalise("concluida", r.Documento, r.Pdf, null, codigo);
    }

    /// <summary>Tira a chave de um arquivo que o agente escreveu dentro do registro (até 20 MB; o que passar disso é cortado, com aviso).</summary>
    static void RedigirArquivo(string arquivo, RedatorChave redator)
    {
        if (!redator.Ativo) return;
        const int Maximo = 20 * 1024 * 1024;
        try
        {
            if (!File.Exists(arquivo)) return;
            var info = new FileInfo(arquivo);
            string texto;
            using (var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var leitor = new StreamReader(fs, new UTF8Encoding(false)))
            {
                var buffer = new char[Maximo];
                var n = leitor.ReadBlock(buffer, 0, buffer.Length);
                texto = new string(buffer, 0, n);
            }
            var novo = redator.Aplicar(texto);
            if (info.Length > Maximo) novo += "\n[arquivo cortado em 20 MB]";
            if (novo != texto || info.Length > Maximo) File.WriteAllText(arquivo, novo, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* o arquivo é do agente; sem acesso, nada a fazer */ }
    }

    // ------------------------------------------------------------------ Claude (análise pro)

    async Task<FimAnalise> ExecutarPeloKitAsync(PedidoExecucao pe, string? chave, DateTimeOffset inicio, IProgress<ProgressoAnalise> progresso, CancellationToken ct)
    {
        var registro = pe.Registro;
        var pasta = pe.Processo.Pasta;
        var pecas = pe.Pecas;
        var p = pe.Provedor;
        var nomeKit = NomeDoKit(p);
        var runtime = pecas.RuntimeDoKit(p)!;
        registro.GravarPedido(pe.Texto ?? "");
        // 2026-11-16: o análise pro (1.6.2+) lê .pmvni-ai\analise-pro\diretrizes.md em cada etapa, como diretriz complementar.
        // Sem pedido, o arquivo sai (nenhuma diretriz antiga vale para a análise nova).
        try
        {
            var controle = Path.Combine(pasta, ResultadoAnalise.PastaControle(p));
            var diretrizes = Path.Combine(controle, "diretrizes.md");
            Directory.CreateDirectory(controle);
            File.WriteAllText(diretrizes, (pe.Texto ?? "").Trim() + "\n" + registro.InstrucoesProgresso(), new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        registro.GravarAnexos(pe.Anexos);

        var (exe, argsIniciar) = LinhasDeComando.ProRuntime(pecas.Python!, pecas.PythonPeloLauncher, runtime, pasta, "iniciar");
        Iniciou(pe, inicio, exe, new[] { exe }.Concat(argsIniciar).ToList());
        var ambiente = Ambiente(p, pe.Modo, chave, pe.Perfil, pe.Ficticio, pe.TrocarGemini, registro);

        var redator = new RedatorChave(chave);
        var log = new LeitorLog(Path.Combine(pasta, ResultadoAnalise.PastaControle(p), "auto-log.txt"));
        log.IgnorarOQueJaHa();      // o kit acrescenta ao log: só o que vier depois da partida é desta execução

        if (p == Provedor.Gemini && await ConferirGeminiAsync(pe, ambiente, redator, ct).ConfigureAwait(false) is { } impedimento) return impedimento;

        var partida = await ExecucaoProcesso.RodarAsync(exe, argsIniciar, pasta, ambiente, LimiteIniciar, ct).ConfigureAwait(false);
        var saidaPartida = redator.Aplicar(partida.Saida);
        var iniciada = partida.Codigo == 0 ? SituacaoAnalisePro.Ler(DoPrimeiroJson(saidaPartida)) : null;
        if (iniciada is null || iniciada.Codigo is not ("INICIADA" or "JA_EM_ANDAMENTO"))
            return Falha($"O {nomeKit} não partiu: " + PrimeiraLinha(saidaPartida) + ". Veja o registro: " + registro.Pasta + ".", partida.Codigo);
        if (iniciada.Codigo == "JA_EM_ANDAMENTO") log.MostrarOFimDoLog();     // uma análise que já rodava: mostra o que ela já escreveu
        // Só a partida precisa da chave (o kit a passa ao processo isolado da análise); "situacao" só lê arquivos e roda sem ela, como na retomada.
        return await AcompanharAsync(pe, Ambiente(p, ModoAcesso.Conta, null), redator, log, inicio, progresso, ct).ConfigureAwait(false);
    }

    /// <summary>Gemini, antes da partida: o plugin confere (sem gerar texto nem gastar cota) o runtime, a autenticação do modo escolhido e o modelo e o nível
    /// de cada uma das seis etapas do perfil. Faltando qualquer um, nada é iniciado e a mensagem diz o perfil e a etapa. null = pode partir.</summary>
    async Task<FimAnalise?> ConferirGeminiAsync(PedidoExecucao pe, Action<IDictionary<string, string?>> ambiente, RedatorChave redator, CancellationToken ct)
    {
        var pecas = pe.Pecas;
        var (exe, args) = LinhasDeComando.ProRuntime(pecas.Python!, pecas.PythonPeloLauncher, pecas.GoogleRuntime!, pe.Processo.Pasta, "conferir");
        var r = await ExecucaoProcesso.RodarAsync(exe, args, pe.Processo.Pasta, ambiente, LimiteIniciar, ct).ConfigureAwait(false);
        var saida = redator.Aplicar(r.Saida);
        pe.Registro.Atualizar(o => o["conferencia_gemini"] = Compactar(DoPrimeiroJson(saida)));
        if (r.Codigo == 0) return null;
        return Falha(MensagemDaConferencia(saida, pe.Perfil) + " Nada foi iniciado e nenhum outro modo, modelo ou perfil foi usado.", r.Codigo);
    }

    /// <summary>O que a conferência do análise Google recusou, em uma frase: as <c>faltas</c> do JSON (perfil, etapa e modelo) ou a primeira linha da saída.</summary>
    public static string MensagemDaConferencia(string saida, PerfilGemini? perfil)
    {
        var nome = perfil is { } pf ? MatrizGemini.Nome(pf) : "perfil não informado";
        try
        {
            if (JsonNode.Parse(DoPrimeiroJson(saida)) is JsonObject o && o["faltas"] is JsonArray faltas)
            {
                var itens = faltas.Select(f => f is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim().TrimEnd('.') : null).Where(s => !string.IsNullOrEmpty(s)).Take(6).ToList();
                if (itens.Count > 0) return $"O Gemini não está pronto para a análise ({nome}): " + string.Join("; ", itens) + ".";
            }
        }
        catch (JsonException) { }
        return $"O análise Google recusou a partida ({nome}): " + PrimeiraLinha(saida) + ".";
    }

    /// <summary>O editor reaberto volta a acompanhar uma análise do Claude que seguiu sozinha (desenho §12.7): só <c>situacao</c> e o <c>auto-log.txt</c>, sem
    /// outra partida (<c>iniciar</c>) e sem trava (quem chama a pega). <paramref name="inicio"/> é o da execução original (o <c>delivery.json</c> tem de ser mais
    /// novo que ele). O fim vai para o <c>execucao.json</c> do registro reaberto, como numa execução comum.</summary>
    public async Task<FimAnalise> AcompanharClaudeAsync(PedidoExecucao pe, DateTimeOffset inicio, IProgress<ProgressoAnalise> progresso, CancellationToken ct)
    {
        FimAnalise fim;
        if (pe.Pecas.Python is null || pe.Pecas.RuntimeDoKit(pe.Provedor) is null)
            fim = Falha(pe.Pecas.Python is null ? $"O Python não foi encontrado neste computador (o {NomeDoKit(pe.Provedor)} do kit precisa dele)."
                : $"O plugin {NomeDoKit(pe.Provedor)} do kit PMVNI não está instalado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).");
        else
        {
            var log = new LeitorLog(Path.Combine(pe.Processo.Pasta, ResultadoAnalise.PastaControle(pe.Provedor), "auto-log.txt"));
            log.MostrarOFimDoLog();
            try { fim = await AcompanharAsync(pe, Ambiente(pe.Provedor, ModoAcesso.Conta, null), new RedatorChave(null), log, inicio, progresso, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                fim = new FimAnalise(AcompanhamentoInterrompido, null, null, "O acompanhamento foi interrompido; a análise do kit continua sozinha na janela minimizada.", null);
            }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
            {
                fim = Falha("A análise não pôde ser acompanhada: " + e.Message.TrimEnd('.') + ". Veja o registro: " + pe.Registro.Pasta + ".");
            }
        }
        Encerrar(pe, fim, inicio);
        return fim;
    }

    /// <summary>Uma leitura da situação do análise pro (<c>situacao</c>, só leitura); null se as peças faltam ou a resposta não é legível.</summary>
    public async Task<SituacaoKit?> SituacaoClaudeAsync(string pastaProcesso, Pecas pecas, Provedor p = Provedor.Claude)
    {
        if (pecas.Python is null || pecas.RuntimeDoKit(p) is not { } runtime) return null;
        var (exe, args) = LinhasDeComando.ProRuntime(pecas.Python, pecas.PythonPeloLauncher, runtime, pastaProcesso, "situacao");
        try
        {
            var r = await ExecucaoProcesso.RodarAsync(exe, args, pastaProcesso, Ambiente(p, ModoAcesso.Conta, null), LimiteSituacao, CancellationToken.None).ConfigureAwait(false);
            if (r.Codigo != 0) return null;
            var s = SituacaoAnalisePro.Ler(DoPrimeiroJson(r.Saida));
            return s.Codigo.Length == 0 ? null : s;
        }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    async Task<FimAnalise> AcompanharAsync(PedidoExecucao pe, Action<IDictionary<string, string?>> ambiente, RedatorChave redator, LeitorLog log, DateTimeOffset inicio,
        IProgress<ProgressoAnalise> progresso, CancellationToken ct)
    {
        var registro = pe.Registro;
        var pasta = pe.Processo.Pasta;
        var pecas = pe.Pecas;
        var (exe, argsSituacao) = LinhasDeComando.ProRuntime(pecas.Python!, pecas.PythonPeloLauncher, pecas.RuntimeDoKit(pe.Provedor)!, pasta, "situacao");
        var relogio = Stopwatch.StartNew();
        TimeSpan? ultimaSituacao = null;
        string? chaveAnterior = null;
        var falhasSeguidas = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var linha in log.Novas()) Anunciar(progresso, new ProgressoAnalise(redator.Aplicar(linha), null));

            if (ultimaSituacao is null || relogio.Elapsed - ultimaSituacao >= IntervaloSituacao)
            {
                ultimaSituacao = relogio.Elapsed;
                var r = await ExecucaoProcesso.RodarAsync(exe, argsSituacao, pasta, ambiente, LimiteSituacao, ct).ConfigureAwait(false);
                var saidaSituacao = redator.Aplicar(r.Saida);
                var situacao = r.Codigo == 0 ? SituacaoAnalisePro.Ler(DoPrimeiroJson(saidaSituacao)) : null;
                if (situacao is { Pdf: not null }) situacao = situacao with { Pdf = ResultadoAnalise.PdfDentro(pasta, situacao.Pdf) };   // só um PDF que o editor abriria
                if (situacao is null || situacao.Codigo.Length == 0)
                {
                    if (++falhasSeguidas >= FalhasSeguidasDaSituacao)
                        return Falha($"Não foi possível ler a situação do {NomeDoKit(pe.Provedor)}: " + PrimeiraLinha(saidaSituacao) + ". Veja o registro: " + registro.Pasta + ".", r.Codigo);
                }
                else
                {
                    falhasSeguidas = 0;
                    var chaveAtual = string.Join('|', situacao.Codigo, situacao.Etapa, situacao.Rotulo, situacao.Erro, situacao.Pdf, situacao.Eventos.Count, situacao.Eventos.LastOrDefault());
                    if (chaveAtual != chaveAnterior)
                    {
                        chaveAnterior = chaveAtual;
                        registro.AcrescentarSituacao(Compactar(DoPrimeiroJson(saidaSituacao)));
                        Anunciar(progresso, new ProgressoAnalise(null, situacao));
                    }
                    if (SituacaoAnalisePro.Terminal(situacao.Codigo))
                    {
                        foreach (var linha in log.Novas(ateOFim: true)) Anunciar(progresso, new ProgressoAnalise(redator.Aplicar(linha), null));
                        registro.Descarregar();
                        return Terminou(pe, situacao, inicio);
                    }
                }
            }
            registro.Descarregar();
            await Task.Delay(IntervaloLog, ct).ConfigureAwait(false);
        }
    }

    static FimAnalise Terminou(PedidoExecucao pe, SituacaoKit s, DateTimeOffset inicio)
    {
        var mensagem = (s.Rotulo ?? s.Codigo) + (string.IsNullOrWhiteSpace(s.Erro) ? "" : ": " + s.Erro);
        switch (s.Codigo)
        {
            case "CONCLUIDO":
                var r = ResultadoAnalise.Localizar(pe.Processo.Pasta, pe.Provedor, inicio.UtcDateTime, pe.Registro.Pasta);
                return r.Documento is null
                    ? new FimAnalise("sem_resultado", null, null, r.Motivo, null)
                    : new FimAnalise("concluida", r.Documento, r.Pdf ?? ResultadoAnalise.PdfDentro(pe.Processo.Pasta, s.Pdf), null, null);
            case "FALHOU":
            case "OBJECOES":
                return new FimAnalise("falhou", null, null, mensagem, null);
            case "CANCELADO":
                return new FimAnalise("cancelada", null, null, mensagem, null);
            default:     // PAUSADO_COTA, INTERROMPIDA, NAO_PARTIU: o kit retoma de onde parou ("Continuar a análise")
                return new FimAnalise("interrompida", null, null, mensagem, null);
        }
    }

    /// <summary>A saída do <c>pro_runtime.py</c> pode trazer avisos antes do JSON: lê a partir do primeiro "{".</summary>
    static string DoPrimeiroJson(string saida)
    {
        var i = saida.IndexOf('{');
        return i < 0 ? saida : saida[i..];
    }

    static string Compactar(string json)
    {
        try { return JsonNode.Parse(json)!.ToJsonString(); }
        catch (Exception e) when (e is JsonException or NullReferenceException) { return json.ReplaceLineEndings(" "); }
    }

    static string PrimeiraLinha(string saida)
    {
        var linha = saida.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "sem resposta";
        return (linha.Length > 200 ? linha[..200] : linha).TrimEnd('.');
    }

    /// <summary>Cancelar a análise do kit (desenho §12.5): encerra a árvore do <c>pid</c> informado pelo <c>situacao</c>, só se estiver vivo, se for um Python
    /// (<c>python*.exe</c>, <c>py.exe</c> ou o programa escolhido) e se tiver começado depois de <c>inicio</c> − 1 min. Devolve false se algo não conferir.</summary>
    public async Task<bool> CancelarClaudeAsync(string pastaProcesso, Pecas pecas, Provedor p = Provedor.Claude)
    {
        if (pecas.Python is null || pecas.RuntimeDoKit(p) is not { } runtime) return false;
        var (exe, args) = LinhasDeComando.ProRuntime(pecas.Python, pecas.PythonPeloLauncher, runtime, pastaProcesso, "situacao");
        (int Codigo, string Saida) r;
        try { r = await ExecucaoProcesso.RodarAsync(exe, args, pastaProcesso, Ambiente(p, ModoAcesso.Conta, null), LimiteSituacao, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception e) when (e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
        if (r.Codigo != 0) return false;

        var s = SituacaoAnalisePro.Ler(DoPrimeiroJson(r.Saida));
        if (s.Codigo is not ("EM_ANDAMENTO" or "AGUARDANDO_COTA") || s.Pid is not { } pid || s.Inicio is not { } inicio) return false;

        Process processo;
        try { processo = Process.GetProcessById(pid); }
        catch (ArgumentException) { return false; }
        using (processo)
        {
            try
            {
                if (processo.HasExited) return false;
                var nome = processo.ProcessName;
                var permitido = nome.StartsWith("python", StringComparison.OrdinalIgnoreCase)
                                || nome.Equals("py", StringComparison.OrdinalIgnoreCase)
                                || nome.Equals(Path.GetFileNameWithoutExtension(pecas.Python), StringComparison.OrdinalIgnoreCase);
                if (!permitido) return false;
                // O processo do kit parte logo depois do "inicio" que ele mesmo gravou: muito antes ou muito depois é outro programa (pid reaproveitado).
                var partiu = new DateTimeOffset(processo.StartTime);
                if (partiu < inicio - TimeSpan.FromMinutes(1) || partiu > inicio + TimeSpan.FromMinutes(5)) return false;
                processo.Kill(entireProcessTree: true);
                await Task.WhenAny(processo.WaitForExitAsync(), Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
                return processo.HasExited;
            }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { return false; }
        }
    }

    /// <summary>Lê só o que o kit acrescentou ao <c>auto-log.txt</c> (somente leitura, sem prender o arquivo), em linhas completas.</summary>
    internal sealed class LeitorLog
    {
        internal const int TetoDaLeitura = 4 * 1024 * 1024;
        readonly string caminho;
        long deslocamento;
        bool ultimasLinhas;
        byte[] sobra = Array.Empty<byte>();

        public LeitorLog(string caminho) => this.caminho = caminho;

        /// <summary>Começa do fim atual do arquivo.</summary>
        public void IgnorarOQueJaHa()
        {
            try { deslocamento = File.Exists(caminho) ? new FileInfo(caminho).Length : 0; }
            catch (IOException) { deslocamento = 0; }
        }

        /// <summary>Começa do início, mostrando só as últimas 2.000 linhas na primeira leitura.</summary>
        public void MostrarOFimDoLog()
        {
            deslocamento = 0;
            ultimasLinhas = true;
        }

        public IReadOnlyList<string> Novas(bool ateOFim = false)
        {
            try
            {
                if (!File.Exists(caminho)) return Array.Empty<string>();
                using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (fs.Length < deslocamento) { deslocamento = 0; sobra = Array.Empty<byte>(); }   // o log foi recomeçado
                // No máximo 4 MB por leitura (o log pode crescer muito entre duas leituras): o que passar disso fica de fora da tela (o arquivo é do kit).
                if (fs.Length - deslocamento > TetoDaLeitura) { deslocamento = fs.Length - TetoDaLeitura; sobra = Array.Empty<byte>(); }
                fs.Seek(deslocamento, SeekOrigin.Begin);
                var novos = new byte[fs.Length - deslocamento];
                var lidos = 0;
                while (lidos < novos.Length)
                {
                    var n = fs.Read(novos, lidos, novos.Length - lidos);
                    if (n <= 0) break;
                    lidos += n;
                }
                deslocamento += lidos;
                var todos = new byte[sobra.Length + lidos];
                Buffer.BlockCopy(sobra, 0, todos, 0, sobra.Length);
                Buffer.BlockCopy(novos, 0, todos, sobra.Length, lidos);

                var ultimoFim = Array.LastIndexOf(todos, (byte)'\n');
                int tamanhoUtil;
                if (ateOFim) { tamanhoUtil = todos.Length; sobra = Array.Empty<byte>(); }
                else
                {
                    tamanhoUtil = ultimoFim + 1;           // só linhas completas; o resto espera a próxima leitura
                    sobra = todos[tamanhoUtil..];
                }
                if (tamanhoUtil == 0) return Array.Empty<string>();

                var linhas = Encoding.UTF8.GetString(todos, 0, tamanhoUtil)
                    .Split('\n')
                    .Select(l => l.TrimEnd('\r'))
                    .Where(l => l.Length > 0)
                    .ToList();
                if (ultimasLinhas)
                {
                    ultimasLinhas = false;
                    if (linhas.Count > LinhasDoLog) linhas = linhas.Skip(linhas.Count - LinhasDoLog).ToList();
                }
                return linhas;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
        }
    }
}
