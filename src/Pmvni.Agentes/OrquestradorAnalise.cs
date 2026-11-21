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
    /// <summary>ChatGPT, modo conta: a rota escolhida em Ajustes (<c>herdar</c>, <c>openai</c> ou <c>codex-lb</c>), resolvida antes da partida por
    /// <see cref="Agentes.RotaCodex.Resolver"/>.</summary>
    public string? RotaCodex { get; init; }

    /// <summary>ChatGPT: continua o controle que já existe na pasta do processo (o controlador é chamado sem <c>--inputs</c>) em vez de começar do zero. Vale
    /// para a mesma análise retomada no mesmo registro; sem o <c>engine_state.json</c> do kit, a análise começa do zero.</summary>
    public bool Retomar { get; init; }

    /// <summary>Contrato da Tarefa 2 (código 21 / situação <c>DECISAO</c>): mostra a decisão ao analista e devolve a resposta (o <see cref="OpcaoDecisao.Valor"/>
    /// escolhido, ou o texto digitado); null = sem resposta agora (a análise para e espera, sem gastar mais nada). Sem quem pergunte (null), idem.</summary>
    public Func<DecisaoAnalise, CancellationToken, Task<string?>>? Decidir { get; init; }

    /// <summary>Gemini: o perfil (conta Ultra, API com créditos ou API gratuita) fixado para a análise inteira; null nos outros provedores e na leitura
    /// da situação.</summary>
    public PerfilGemini? Perfil { get; init; }
    public bool TrocarGemini { get; init; }

    /// <summary>Gemini, perfil gratuito: o usuário declarou no clique que o processo é fictício (o plugin recusa a partida sem essa declaração).</summary>
    public bool Ficticio { get; init; }
}

/// <summary>Uma linha de andamento para a tela (do Codex, ou do <c>auto-log.txt</c> do kit) e/ou a situação mais recente do kit.</summary>
public sealed record ProgressoAnalise(string? Linha, SituacaoKit? Situacao);

/// <summary><c>Situacao</c>: <c>concluida</c> | <c>falhou</c> | <c>cancelada</c> | <c>sem_resultado</c> | <c>interrompida</c> (também: parou para uma decisão do
/// analista que não foi respondida, <see cref="Decisao"/>) | <c>acompanhamento_interrompido</c> (Claude: o editor parou de acompanhar; a análise do kit
/// continua) | <c>cota_esgotada</c> (registros do Editor 2.2: a cota acabou na sessão controladora).</summary>
public sealed record FimAnalise(string Situacao, string? Documento, string? Pdf, string? Mensagem, int? Codigo)
{
    /// <summary>A decisão que o kit pediu e ficou sem resposta (a análise espera; "Continuar a análise" pergunta de novo).</summary>
    public DecisaoAnalise? Decisao { get; init; }
}

/// <summary>O intermediário (desenho §12.1): inicia o controlador do kit instalado neste computador, acompanha, guarda o registro e localiza o resultado que o
/// próprio kit declara. Não analisa, não chama modelo e não interpreta o relatório.
/// ChatGPT (Tarefa 12): o <c>executar_analise_62.py</c> do analise como processo filho, sem sessão Codex controladora (o cancelamento mata a árvore dele);
/// a situação vem do contrato da Tarefa 2 (códigos 0/20/21/1 e o JSON da última linha): cota esgotada é esperada até a hora informada e retomada sozinha,
/// decisão é perguntada ao analista. Claude e Gemini: dá a partida do controlador do kit (<c>pro_runtime.py … iniciar</c>); a análise roda sozinha, fora do
/// editor, e o editor só acompanha (<c>situacao</c> e <c>auto-log.txt</c>); parar de acompanhar não a encerra (<see cref="CancelarClaudeAsync"/> sim).</summary>
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
        var impedimento = Conferir(pe, out var rotaCodex);
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
                ? await ExecutarChatGptAsync(pe, rotaCodex!, chave, inicio, progresso, ct).ConfigureAwait(false)
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

    /// <summary>As peças que faltam e os motivos exatos (desenho §12.11): nada é iniciado nem arquivado. ChatGPT: <paramref name="rota"/> é a rota do Codex
    /// resolvida aqui, uma vez só, e usada na execução inteira (nunca relida do config.toml depois da partida).</summary>
    static FimAnalise? Conferir(PedidoExecucao pe, out RotaResolvida? rota)
    {
        rota = null;
        var pecas = pe.Pecas;
        if (pe.Provedor == Provedor.ChatGpt)
        {
            if (pecas.Codex is null)
                return Falha("O Codex não foi encontrado neste computador. Instale o app do ChatGPT/Codex ou escolha o programa em Ajustes › Inteligência artificial.");
            if (!pecas.PluginAnaliseHabilitado)
                return Falha("O plugin analise do kit PMVNI não está habilitado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).");
            if (pecas.Python is null) return Falha(SemPythonAnalise);
            if (PoliticaKit.PluginDesatualizado(Provedor.ChatGpt, pecas.VersaoPluginAnalise) is { } antigo) return Falha(antigo);
            if (pecas.Analise62 is null) return Falha(SemControladorAnalise(pecas.VersaoPluginAnalise));
            // Erros 6 e 7: a rota é resolvida aqui, antes de qualquer processo; provedor que a análise não aceita é recusado agora.
            rota = RotaCodex.Resolver(pe.Modo, pe.RotaCodex, pecas.CodexHome);
            if (rota.Recusa is not null || rota.Rota is null) return Falha(rota.Recusa ?? "A rota do Codex não pôde ser resolvida. Nada foi iniciado.");
        }
        else if (pe.Provedor == Provedor.Gemini)
        {
            if (pecas.Python is null) return Falha("O Python não foi encontrado neste computador (o análise Google do kit precisa dele).");
            // Tarefa 18: um analise anterior ao 4.0.0 não tem o controlador da rota (rotas\gemini): pede a atualização do kit, não "não instalado".
            if (pecas.VersaoAnaliseGoogle is not null && PoliticaKit.PluginDesatualizado(Provedor.Gemini, pecas.VersaoAnaliseGoogle) is { } kitAntigo) return Falha(kitAntigo);
            if (pecas.GoogleRuntime is null) return Falha(SemAnaliseGoogle);
            if (pe.Modo != ModoAcesso.ContaGemini && pecas.Agy is null) return Falha(SemAgy);
            if (pe.Perfil is not { } perfil)
                return Falha(pe.Modo == ModoAcesso.Chave ? SemPerfilApi : "O perfil do Gemini não foi informado. Nada foi iniciado.");
            if (MatrizGemini.Modo(perfil) != pe.Modo)
                return Falha("O perfil do Gemini (" + MatrizGemini.Nome(perfil) + ") não corresponde ao modo escolhido. Nada foi iniciado: a análise não troca de modo sozinha.");
            if (perfil == PerfilGemini.ApiGratuita && !pe.Ficticio)
                return Falha("O perfil API gratuita só roda testes com dados fictícios e precisa da sua confirmação no clique. Nada foi iniciado.");
            if (PoliticaKit.PluginDesatualizado(Provedor.Gemini, pecas.VersaoAnaliseGoogle) is { } antigo) return Falha(antigo);
        }
        else
        {
            if (pecas.Python is null) return Falha("O Python não foi encontrado neste computador (o análise pro do kit precisa dele).");
            // Tarefa 18: um analise anterior ao 4.0.0 não tem o controlador da rota (rotas\claude): pede a atualização do kit, não "não instalado".
            if (pecas.VersaoAnalisePro is not null && PoliticaKit.PluginDesatualizado(Provedor.Claude, pecas.VersaoAnalisePro) is { } kitAntigo) return Falha(kitAntigo);
            if (pecas.ProRuntime is null)
                return Falha("O plugin análise pro do kit PMVNI não está instalado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).");
            if (pecas.Claude is null) return Falha("O Claude Code não foi encontrado neste computador (claude.exe).");
            if (PoliticaKit.PluginDesatualizado(Provedor.Claude, pecas.VersaoAnalisePro) is { } antigo) return Falha(antigo);
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

    public const string SemPythonAnalise = "O Python não foi encontrado neste computador (o analise do kit precisa dele).";

    /// <summary>O analise instalado não tem o controlador que o editor roda (<c>executar_analise_62.py</c>).</summary>
    public static string SemControladorAnalise(string? versao) =>
        $"Atualize o kit PMVNI (analise {PoliticaKit.VersoesMinimas[Provedor.ChatGpt].Minima.ToString(3)} ou mais novo): o controlador executar_analise_62.py "
        + $"não foi encontrado no analise instalado ({versao ?? "versão desconhecida"}). Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor). Nada foi iniciado.";

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

    // ------------------------------------------------------------------ ChatGPT (analise: executar_analise_62.py)

    /// <summary>Quantas vezes, no máximo, uma execução chama o controlador (etapas, esperas de cota e decisões): um controlador que nunca termina não prende o
    /// editor para sempre.</summary>
    const int MaximoDeChamadas = 200;

    /// <summary>Quanto esperar quando o controlador diz "aguardando cota" sem informar a hora (contrato: <c>retomar_em_utc</c> nulo).</summary>
    public TimeSpan EsperaSemHorario { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>A espera mínima por cota (horário já passado ou muito perto): evita chamadas imediatas que gastariam o teto de chamadas.</summary>
    public TimeSpan EsperaMinimaCota { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>A espera máxima com o editor aberto: horário mais distante para a análise (retomável por "Continuar a análise").</summary>
    public TimeSpan EsperaMaximaCota { get; init; } = TimeSpan.FromDays(7);

    /// <summary>Tarefa 12 (erros 1–7): roda o controlador do analise direto, sem sessão Codex controladora. Primeira chamada com <c>--inputs</c> (as peças do
    /// processo); as seguintes sem (o controlador continua o controle). A última linha diz o que fazer: entregue → resultado; etapa concluída → chama de
    /// novo; aguardando cota → espera até <c>retomar_em_utc</c> e chama de novo sozinho; decisão → pergunta ao analista, grava a resposta e chama de novo;
    /// falha → para com o motivo.</summary>
    async Task<FimAnalise> ExecutarChatGptAsync(PedidoExecucao pe, RotaResolvida rota, string? chave, DateTimeOffset inicio, IProgress<ProgressoAnalise> progresso,
        CancellationToken ct)
    {
        var registro = pe.Registro;
        var pasta = pe.Processo.Pasta;
        var pecas = pe.Pecas;
        var rotaCodex = rota.Rota!;      // Conferir só deixa passar openai ou codex-lb
        var controle = Path.Combine(pasta, ResultadoAnalise.PastaControle(Provedor.ChatGpt));
        // Só retoma o controle que ESTE registro criou: um engine_state.json de outra análise (a anterior, entregue) nunca é continuado por engano.
        var retomar = pe.Retomar && PodeRetomarChatGpt(pasta, registro.Pasta);
        IReadOnlyList<string>? entradas = null;
        if (!retomar)
        {
            entradas = await Task.Run(() => EntradasDoProcesso(pe, ct), ct).ConfigureAwait(false);
            if (entradas.Count == 0)
                return Falha("O processo não tem peças em PROJETO nem em DOCUMENTOS para o analise conferir. Anexe as peças e analise de novo. Nada foi iniciado.");
        }

        registro.GravarPedido(pe.Texto ?? "");
        // As diretrizes do analista e o tratamento dos dados vão no diretrizes.md do kit (como no análise pro); nenhuma IA recebe pedido do editor.
        // Tarefa 13: o progresso e a previsão do Monitor saem do manifesto e do engine_state (ProgressoLocal); nada de instruções de progresso para a IA.
        GravarDiretrizes(controle, PedidoAgente.Diretrizes(pe.Texto));
        registro.GravarAnexos(pe.Anexos);

        var (exe, args) = LinhasDeComando.Analise62(pecas.Python!, pecas.PythonPeloLauncher, pecas.Analise62!, pasta, entradas);
        Iniciou(pe, inicio, exe, new[] { exe }.Concat(args).ToList());
        registro.Atualizar(o =>
        {
            o["controlador"] = null;
            o["rota_codex_tecnica"] = rota.Rota;       // o que o kit lê do registro: nunca "herdar" (resolvido aqui)
            o["rota_codex_origem"] = rota.Origem;
            o["entrada_padrao"] = null;
            o["retomada"] = retomar;
        });
        // Erro 7: a tela diz qual rota será usada antes de qualquer sessão; a situação inicial põe o Monitor no mesmo painel do Claude e do Gemini.
        Anunciar(progresso, new ProgressoAnalise(RotaCodex.Descrever(rota), SituacaoDoControlador("EM_ANDAMENTO", "em andamento", null)));

        var ambiente = AmbienteAnalise62(pe, chave, rotaCodex, registro);
        var redator = new RedatorChave(chave);      // a chave nunca vai para o registro nem para a tela, nem se o controlador ou um filho a repetir
        for (var chamada = 1; ; chamada++)
        {
            if (chamada > MaximoDeChamadas)
                return Falha($"O controlador do analise foi chamado {MaximoDeChamadas} vezes sem concluir. Veja o registro: {registro.Pasta}.");
            var (codigo, s, json, ultima) = await RodarControladorAsync(exe, args, pe, ambiente, redator, progresso, ct).ConfigureAwait(false);
            if (json is not null) registro.AcrescentarSituacao(Compactar(json));
            registro.Descarregar();
            // Da segunda chamada em diante, sempre a retomada: o controlador continua o controle que a primeira criou.
            (exe, args) = LinhasDeComando.Analise62(pecas.Python!, pecas.PythonPeloLauncher, pecas.Analise62!, pasta, null);

            var situacao = s?.Situacao ?? (codigo switch
            {
                SituacaoAnalise62.CodigoOk => SituacaoAnalise62.Entregue,
                SituacaoAnalise62.CodigoCota => SituacaoAnalise62.AguardandoCota,
                _ => SituacaoAnalise62.Falha,
            });
            switch (situacao)
            {
                case SituacaoAnalise62.Entregue:
                {
                    Anunciar(progresso, new ProgressoAnalise(null, SituacaoDoControlador("CONCLUIDO", "concluída", s?.Etapa)));
                    var r = ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, inicio.UtcDateTime, registro.Pasta);
                    return r.Documento is not null ? new FimAnalise("concluida", r.Documento, r.Pdf, null, codigo) : new FimAnalise("sem_resultado", null, null, r.Motivo, codigo);
                }
                case SituacaoAnalise62.EtapaOk when codigo == SituacaoAnalise62.CodigoOk:
                    Anunciar(progresso, new ProgressoAnalise(null, SituacaoDoControlador("EM_ANDAMENTO", "etapa concluída", s?.Etapa)));
                    continue;
                case SituacaoAnalise62.AguardandoCota:
                {
                    // Erro 5: a cota acabou numa etapa técnica. O controlador já guardou a etapa; o editor espera a hora informada e chama de novo, sem clique.
                    var agora = DateTimeOffset.UtcNow;
                    var informado = s?.RetomarEmUtc ?? agora + EsperaSemHorario;
                    if (informado - agora > EsperaMaximaCota)
                    {
                        // Longe demais para o editor esperar aberto (e além do que um timer aceita): a análise fica parada, retomável.
                        var volta = informado.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                        Anunciar(progresso, new ProgressoAnalise("A cota só volta em " + volta + ".",
                            SituacaoDoControlador(SituacaoAnalise62.AguardandoCota, "cota só em " + volta, s?.Etapa) with { RetomarEmUtc = informado, Motivo = s?.Motivo }));
                        registro.Atualizar(o => o["aguardando_cota_ate_utc"] = informado.ToString("o", CultureInfo.InvariantCulture));
                        return new FimAnalise("interrompida", null, null, $"A cota do ChatGPT só volta em {volta}. O que já foi feito está guardado; "
                            + "clique em \"Continuar a análise\" quando a cota voltar.", codigo);
                    }
                    // Nunca menos que a espera mínima: horário já passado ou muito perto não vira uma sequência de chamadas imediatas.
                    var quando = informado - agora < EsperaMinimaCota ? agora + EsperaMinimaCota : informado;
                    var rotulo = SituacaoAnalise62.TextoAguardandoCota(quando);
                    Anunciar(progresso, new ProgressoAnalise(rotulo + (string.IsNullOrWhiteSpace(s?.Motivo) ? "" : " (" + redator.Aplicar(s!.Motivo!) + ")"),
                        SituacaoDoControlador(SituacaoAnalise62.AguardandoCota, rotulo, s?.Etapa) with { RetomarEmUtc = quando, Motivo = s?.Motivo }));
                    registro.Atualizar(o => o["aguardando_cota_ate_utc"] = quando.ToString("o", CultureInfo.InvariantCulture));
                    registro.Descarregar();
                    var espera = quando - DateTimeOffset.UtcNow;
                    if (espera > TimeSpan.Zero) await Task.Delay(espera, ct).ConfigureAwait(false);
                    registro.Atualizar(o => o.Remove("aguardando_cota_ate_utc"));
                    Anunciar(progresso, new ProgressoAnalise("Retomando a análise de onde parou.", SituacaoDoControlador("EM_ANDAMENTO", "em andamento", s?.Etapa)));
                    continue;
                }
                case SituacaoAnalise62.PedeDecisao when s?.Decisao is { } d:
                {
                    // Erro 4: ninguém responde dentro de um exec. O controlador para com a pergunta; o editor a mostra ao analista e chama de novo com a resposta.
                    Anunciar(progresso, new ProgressoAnalise("Decisão necessária: " + d.Pergunta,
                        SituacaoDoControlador(SituacaoAnalise62.PedeDecisao, "aguardando a sua decisão", s.Etapa) with { Decisao = d }));
                    var resposta = await PerguntarAsync(pe, d, ct).ConfigureAwait(false);
                    if (resposta is null) return Parado(d, codigo);
                    Responder(pe, Provedor.ChatGpt, d, resposta);
                    Anunciar(progresso, new ProgressoAnalise("Resposta gravada; a análise continua.", SituacaoDoControlador("EM_ANDAMENTO", "em andamento", s.Etapa)));
                    continue;
                }
                default:
                {
                    var etapa = s?.Etapa is { } e ? " na etapa " + SituacaoAnalisePro.NomeEtapa(e) : "";
                    var motivo = !string.IsNullOrWhiteSpace(s?.Motivo) ? redator.Aplicar(s!.Motivo!).Trim().TrimEnd('.')
                        : ultima is not null ? PrimeiraLinha(ultima) : $"código {codigo}";
                    Anunciar(progresso, new ProgressoAnalise(null, SituacaoDoControlador("FALHOU", "parou com erro", s?.Etapa) with { Erro = motivo, Motivo = s?.Motivo }));
                    return new FimAnalise("falhou", null, null, $"O controlador do analise parou{etapa}: {motivo}. Veja o registro: {registro.Pasta}.", codigo);
                }
            }
        }
    }

    /// <summary>A situação que a tela mostra para a rota ChatGPT, no mesmo formato da que o análise pro informa.</summary>
    static SituacaoKit SituacaoDoControlador(string codigo, string rotulo, string? etapa) => new(codigo, rotulo, etapa, null, null, null, null, Array.Empty<string>());

    /// <summary>As peças originais do processo para o <c>--inputs</c>: os anexos desta análise (já copiados no processo) e os arquivos de PROJETO e DOCUMENTOS,
    /// sem repetir e sem nada das pastas de controle (<c>.pmvni-ai</c>). A escolha das peças vigentes é do kit (por regra; o empate vem como decisão).</summary>
    static IReadOnlyList<string> EntradasDoProcesso(PedidoExecucao pe, CancellationToken ct)
    {
        var raiz = Path.GetFullPath(pe.Processo.Pasta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var lista = new List<string>();
        void Por(string caminho)
        {
            try
            {
                var f = Path.GetFullPath(caminho);
                if (!f.StartsWith(raiz, StringComparison.OrdinalIgnoreCase) || f[raiz.Length..].Split(Path.DirectorySeparatorChar).Contains(".pmvni-ai", StringComparer.OrdinalIgnoreCase)) return;
                if (File.Exists(f) && !ArquivoTransitorio(f) && !lista.Contains(f, StringComparer.OrdinalIgnoreCase)) lista.Add(f);
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        foreach (var a in pe.Anexos) Por(Path.Combine(pe.Processo.Pasta, a.Relativo));
        var detalhe = Processos.Detalhar(pe.Processo, ct);
        foreach (var a in detalhe.Projeto.Concat(detalhe.Documentos)) Por(a.Caminho);
        return lista;
    }

    static readonly string[] NomesDoSistema = ["desktop.ini", "thumbs.db", "ehthumbs.db", ".ds_store", "icon\r"];
    static readonly string[] ExtensoesTransitorias = [".tmp", ".temp", ".crdownload", ".partial", ".part", ".download", ".swp", ".swo", ".lock", ".~tmp"];

    /// <summary>Arquivo que não é peça do processo e pode sumir ou mudar sozinho (o kit grava o sha256 de cada entrada e recusa a etapa se mudou): oculto ou de
    /// sistema, trava do Office (<c>~$…</c>) e do LibreOffice (<c>.~lock.…#</c>), <c>desktop.ini</c>/<c>Thumbs.db</c>, temporários e downloads pela metade.</summary>
    internal static bool ArquivoTransitorio(string caminho)
    {
        var nome = Path.GetFileName(caminho);
        if (nome.Length == 0 || nome.StartsWith("~$", StringComparison.Ordinal) || nome.StartsWith(".~lock.", StringComparison.OrdinalIgnoreCase)
            || (nome.StartsWith('~') && nome.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            || NomesDoSistema.Contains(nome.ToLowerInvariant()) || ExtensoesTransitorias.Contains(Path.GetExtension(nome).ToLowerInvariant()))
            return true;
        try { return (File.GetAttributes(caminho) & (FileAttributes.Hidden | FileAttributes.System | FileAttributes.Temporary)) != 0; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return true; }
    }

    /// <summary>Minor 1 da revisão: o ChatGPT só continua sem <c>--inputs</c> quando o controle do analise existe E este registro já conversou com o kit — há
    /// uma situação do contrato no <c>situacoes.jsonl</c> do registro, ou o <c>continuidade.json</c> do registro é desta execução. Uma reanálise cuja primeira
    /// chamada falhou antes de o kit criar o controle novo não retoma a análise anterior. Só contam as situações que o kit dá depois de ter o controle
    /// (<c>ETAPA_OK</c>, <c>AGUARDANDO_COTA</c>, <c>DECISAO</c>, <c>ENTREGUE</c>); <c>FALHA</c>, <c>NAO_PARTIU</c> e o que não for do contrato não provam nada.</summary>
    public static bool PodeRetomarChatGpt(string pastaProcesso, string pastaRegistro)
    {
        if (!File.Exists(Path.Combine(pastaProcesso, ResultadoAnalise.PastaControle(Provedor.ChatGpt), "engine_state.json"))) return false;
        try
        {
            var situacoes = Path.Combine(pastaRegistro, "situacoes.jsonl");
            if (File.Exists(situacoes))
                using (var fs = new FileStream(situacoes, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var leitor = new StreamReader(fs))
                    for (var linha = leitor.ReadLine(); linha is not null; linha = leitor.ReadLine())
                        if (SituacaoAnalise62.Ler(linha).Situacao is SituacaoAnalise62.EtapaOk or SituacaoAnalise62.AguardandoCota
                            or SituacaoAnalise62.PedeDecisao or SituacaoAnalise62.Entregue) return true;
            var continuidade = Path.Combine(pastaRegistro, "continuidade.json");
            return File.Exists(continuidade) && JsonNode.Parse(File.ReadAllText(continuidade))?["execution"] is JsonValue v
                && v.TryGetValue<string>(out var execucao) && execucao == Path.GetFileName(Path.GetFullPath(pastaRegistro).TrimEnd(Path.DirectorySeparatorChar));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) { return false; }
    }

    /// <summary>O ambiente do controlador: o de sempre (chaves herdadas fora; a do modo chave pela variável do provedor; o registro da execução), a rota em
    /// <see cref="RotaCodex.Variavel"/> e a pasta do Codex achado pelo editor no começo do PATH (o controlador chama <c>codex</c>).</summary>
    Action<IDictionary<string, string?>> AmbienteAnalise62(PedidoExecucao pe, string? chave, string rota, RegistroExecucao registro)
    {
        var basico = Ambiente(Provedor.ChatGpt, pe.Modo, chave, registro: registro);
        var pastaCodex = pe.Pecas.Codex is { } codex ? Path.GetDirectoryName(codex) : null;
        return env =>
        {
            basico(env);
            env[RotaCodex.Variavel] = rota;
            if (string.IsNullOrEmpty(pastaCodex)) return;
            var chavePath = env.Keys.FirstOrDefault(k => k.Equals("PATH", StringComparison.OrdinalIgnoreCase)) ?? "PATH";
            env.TryGetValue(chavePath, out var atual);
            env[chavePath] = string.IsNullOrEmpty(atual) ? pastaCodex : pastaCodex + Path.PathSeparator + atual;
        };
    }

    static void GravarDiretrizes(string controle, string texto)
    {
        try
        {
            Directory.CreateDirectory(controle);
            File.WriteAllText(Path.Combine(controle, "diretrizes.md"), texto, new UTF8Encoding(false));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Registrar(e); }
    }

    /// <summary>Uma chamada do controlador: a saída vai para o <c>saida.jsonl</c> e para a tela (<see cref="EventosCodex.LinhaDoControlador"/>), o erro para o
    /// <c>erros.txt</c>, o pid e a hora da partida para o <c>execucao.json</c> (<c>controlador</c>, o critério de <see cref="CancelarChatGptAsync"/>). Devolve o
    /// código, a situação da última linha e o texto dela.</summary>
    async Task<(int Codigo, SituacaoAnalise62? Situacao, string? Json, string? Ultima)> RodarControladorAsync(string exe, IReadOnlyList<string> args, PedidoExecucao pe,
        Action<IDictionary<string, string?>> ambiente, RedatorChave redator, IProgress<ProgressoAnalise> progresso, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var registro = pe.Registro;
        const int Cauda = 400;
        var cauda = new Queue<string>();
        var canal = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        using var execucao = ExecucaoProcesso.Iniciar(exe, args, pe.Processo.Pasta, null, ambiente,
            linha => canal.Writer.TryWrite(redator.Aplicar(linha)), linha => registro.AcrescentarErro(redator.Aplicar(linha)), TarefaCodex);
        var partida = DateTimeOffset.Now;
        registro.Atualizar(o => o["controlador"] = new JsonObject
        {
            ["pid"] = execucao.Pid, ["inicio"] = partida.ToString("o", CultureInfo.InvariantCulture), ["programa"] = exe,
        });
        registro.Descarregar();
        using var cancelamento = ct.Register(execucao.Cancelar);

        // A saída vai para uma fila sem limite: o controlador nunca espera pelo editor (nem pela tela). Um só consumidor grava o saida.jsonl e anuncia.
        var consumidor = Task.Run(async () =>
        {
            var leitor = canal.Reader;
            var desdeODescarregar = 0;
            while (await leitor.WaitToReadAsync().ConfigureAwait(false))
            {
                while (leitor.TryRead(out var linha))
                {
                    // Nada aqui pode matar o consumidor (o andamento congelaria): o registro não lança e o resto é contido.
                    try
                    {
                        registro.AcrescentarSaida(linha);
                        cauda.Enqueue(linha.Length > 64 * 1024 ? linha[..(64 * 1024)] : linha);
                        if (cauda.Count > Cauda) cauda.Dequeue();
                        if (EventosCodex.EtapaDoControlador(linha) is { } etapa)
                            Anunciar(progresso, new ProgressoAnalise(null, SituacaoDoControlador("EM_ANDAMENTO", "em andamento", etapa)));
                        if (EventosCodex.LinhaDoControlador(linha) is { } texto) Anunciar(progresso, new ProgressoAnalise(texto, null));
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
        registro.Descarregar();
        if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
        var linhas = cauda.ToArray();
        var json = SituacaoAnalise62.JsonDaSaida(linhas);
        var ultima = linhas.LastOrDefault(l => !string.IsNullOrWhiteSpace(l));
        return (codigo, json is null ? null : SituacaoAnalise62.Ler(json), json, ultima);
    }

    /// <summary>Pergunta ao analista (<see cref="PedidoExecucao.Decidir"/>); null = sem resposta agora.</summary>
    static async Task<string?> PerguntarAsync(PedidoExecucao pe, DecisaoAnalise d, CancellationToken ct)
    {
        if (pe.Decidir is null) return null;
        var resposta = await pe.Decidir(d, ct).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
        return string.IsNullOrWhiteSpace(resposta) ? null : resposta.Trim();
    }

    /// <summary>Grava a resposta onde a decisão pediu (dentro da pasta de controle do kit) e guarda no registro o que foi perguntado e respondido.</summary>
    static void Responder(PedidoExecucao pe, Provedor p, DecisaoAnalise d, string resposta)
    {
        var arquivo = RespostaDecisao.Gravar(pe.Processo.Pasta, p, d, resposta);
        pe.Registro.Atualizar(o =>
        {
            var lista = o["decisoes"] as JsonArray ?? new JsonArray();
            lista.Add(new JsonObject
            {
                ["tipo"] = d.Tipo, ["id"] = d.Id, ["pergunta"] = d.Pergunta, ["resposta"] = resposta, ["arquivo"] = arquivo,
                ["em"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            });
            o["decisoes"] = lista;
        });
    }

    /// <summary>A análise parou numa decisão sem resposta: espera, sem gastar mais nada; "Continuar a análise" chama o controlador e a pergunta volta.</summary>
    static FimAnalise Parado(DecisaoAnalise d, int? codigo) =>
        new("interrompida", null, null, "A análise parou para a sua decisão: " + d.Pergunta.Trim()
            + " Nada mais foi gasto. Clique em \"Continuar a análise\" para responder e seguir de onde parou.", codigo) { Decisao = d };

    /// <summary>Cancelar a análise do ChatGPT pelo registro (o editor não segura mais o processo, por exemplo depois de reaberto): o mesmo critério do Claude —
    /// encerra a árvore do <c>pid</c> gravado em <c>controlador</c> no <c>execucao.json</c> só se estiver vivo, se for um Python e se tiver partido perto da hora
    /// gravada. Com o editor segurando o processo, o cancelamento comum já encerra a árvore do próprio filho (e só ela). false = nada encerrado.</summary>
    public async Task<bool> CancelarChatGptAsync(string pastaRegistro, Pecas pecas)
    {
        int pid;
        DateTimeOffset inicio;
        try
        {
            var arquivo = Path.Combine(pastaRegistro, "execucao.json");
            if (!File.Exists(arquivo) || JsonNode.Parse(File.ReadAllText(arquivo))?["controlador"] is not JsonObject c
                || c["pid"] is not JsonValue p || !p.TryGetValue(out pid)
                || c["inicio"] is not JsonValue i || !i.TryGetValue<string>(out var texto)
                || !DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out inicio)) return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException) { return false; }
        return await EncerrarControladorAsync(pid, inicio, pecas).ConfigureAwait(false);
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
            File.WriteAllText(diretrizes, (pe.Texto ?? "").Trim() + "\n", new UTF8Encoding(false));     // só o texto do analista (Tarefa 13)
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

        for (var partidas = 1; ; partidas++)
        {
            var partida = await ExecucaoProcesso.RodarAsync(exe, argsIniciar, pasta, ambiente, LimiteIniciar, ct).ConfigureAwait(false);
            var saidaPartida = redator.Aplicar(partida.Saida);
            var iniciada = partida.Codigo == 0 ? SituacaoAnalisePro.Ler(DoPrimeiroJson(saidaPartida)) : null;
            if (iniciada is null || iniciada.Codigo is not ("INICIADA" or "JA_EM_ANDAMENTO"))
                return Falha($"O {nomeKit} não partiu: " + PrimeiraLinha(saidaPartida) + ". Veja o registro: " + registro.Pasta + ".", partida.Codigo);
            if (iniciada.Codigo == "JA_EM_ANDAMENTO") log.MostrarOFimDoLog();     // uma análise que já rodava: mostra o que ela já escreveu
            // Só a partida precisa da chave (o kit a passa ao processo isolado da análise); "situacao" só lê arquivos e roda sem ela, como na retomada.
            var fim = await AcompanharAsync(pe, Ambiente(p, ModoAcesso.Conta, null), redator, log, inicio, progresso, ct).ConfigureAwait(false);
            // Contrato da Tarefa 2: o kit parou para o analista decidir. O editor pergunta, grava a resposta e dá a partida de novo (o kit continua de onde parou).
            if (fim.Decisao is not { } d || partidas >= MaximoDeChamadas) return fim;
            var resposta = await PerguntarAsync(pe, d, ct).ConfigureAwait(false);
            if (resposta is null) return fim;
            Responder(pe, p, d, resposta);
            Anunciar(progresso, new ProgressoAnalise("Resposta gravada; a análise continua.", null));
        }
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
                // Contrato da Tarefa 2: a espera de cota com a hora da retomada aparece como "Aguardando cota até HH:MM".
                if (situacao is { Codigo: SituacaoAnalise62.AguardandoCota, RetomarEmUtc: { } quando })
                    situacao = situacao with { Rotulo = SituacaoAnalise62.TextoAguardandoCota(quando) };
                if (situacao is null || situacao.Codigo.Length == 0)
                {
                    if (++falhasSeguidas >= FalhasSeguidasDaSituacao)
                        return Falha($"Não foi possível ler a situação do {NomeDoKit(pe.Provedor)}: " + PrimeiraLinha(saidaSituacao) + ". Veja o registro: " + registro.Pasta + ".", r.Codigo);
                }
                else
                {
                    falhasSeguidas = 0;
                    var chaveAtual = string.Join('|', situacao.Codigo, situacao.Etapa, situacao.Rotulo, situacao.Erro, situacao.Pdf, situacao.Eventos.Count, situacao.Eventos.LastOrDefault(),
                        situacao.RetomarEmUtc, situacao.Motivo, situacao.Decisao?.Json);
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
        var detalhe = string.IsNullOrWhiteSpace(s.Erro) ? s.Motivo : s.Erro;
        var mensagem = (s.Rotulo ?? s.Codigo) + (string.IsNullOrWhiteSpace(detalhe) ? "" : ": " + detalhe);
        switch (s.Codigo)
        {
            case "CONCLUIDO":
            case SituacaoAnalise62.Entregue:
                var r = ResultadoAnalise.Localizar(pe.Processo.Pasta, pe.Provedor, inicio.UtcDateTime, pe.Registro.Pasta);
                return r.Documento is null
                    ? new FimAnalise("sem_resultado", null, null, r.Motivo, null)
                    : new FimAnalise("concluida", r.Documento, r.Pdf ?? ResultadoAnalise.PdfDentro(pe.Processo.Pasta, s.Pdf), null, null);
            case "FALHOU":
            case "OBJECOES":
            case SituacaoAnalise62.Falha:
                return new FimAnalise("falhou", null, null, mensagem, null);
            case SituacaoAnalise62.PedeDecisao when s.Decisao is { } d:
                return Parado(d, null);
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
        return await EncerrarControladorAsync(pid, inicio, pecas).ConfigureAwait(false);
    }

    /// <summary>O critério comum de cancelamento (Claude, Gemini e ChatGPT): encerra a árvore do <paramref name="pid"/> só se estiver vivo, se for um Python
    /// (<c>python*.exe</c>, <c>py.exe</c> ou o programa escolhido) e se tiver partido entre <paramref name="inicio"/> − 1 min e <paramref name="inicio"/> + 5 min.</summary>
    static async Task<bool> EncerrarControladorAsync(int pid, DateTimeOffset inicio, Pecas pecas)
    {
        if (pecas.Python is null) return false;
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
