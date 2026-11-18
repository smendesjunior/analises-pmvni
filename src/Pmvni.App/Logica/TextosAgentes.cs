using Pmvni.Agentes;

namespace Pmvni.App;

/// <summary>Os textos fixos de Ajustes › Inteligência artificial (desenho §12.8 e §12.9). Ficam aqui, sem tipos do WinUI, para a tela, o roteiro e
/// a página Novo relatório (Tarefa 20) usarem as mesmas palavras. O texto do aviso tem a versão <see cref="Ajustes.VersaoAviso"/>.</summary>
public static class TextosAgentes
{
    public const string Secao = "Inteligência artificial";

    public const string UsarConta = "Usar minha conta (login do programa)";
    public const string UsarChave = "Usar chave de API";
    public const string ChaveGuardada = "Chave guardada, protegida pelo Windows para este usuário.";
    public const string SemChave = "Nenhuma chave guardada neste computador.";
    public const string ChaveVazia = "Digite ou cole a chave de API antes de guardar.";
    public const string ProgramaInvalido = "Escolha um programa .exe que exista.";
    public const string NaoTestado = "Ainda não testado. Use Testar para ver a versão do programa e a sessão.";
    public const string Testando = "Testando…";

    /// <summary>A dica do modo chave do Claude quando o análise pro instalado não aceita a Anthropic API (anterior à 1.6.4).</summary>
    public const string ClaudeSoConta = "O análise pro instalado não aceita a Anthropic API (é preciso o 1.6.4 ou mais novo): com a chave marcada, a análise não é iniciada, "
        + "nem pela conta. Atualize pelo INSTALAR_KIT_PMVNI.cmd ou use Usar minha conta. A chave continua valendo para a Claude API em Modelos de IA.";

    /// <summary>A dica do modo chave do Claude com um análise pro que aceita a Anthropic API (1.6.4+): o que muda de verdade na análise.</summary>
    public const string ClaudeModoApi = "Modo Anthropic API: o análise pro usa esta chave, com os créditos e os limites da organização no Anthropic Console, "
        + "sem usar a cota do Claude Max. Se a API falhar (saldo, chave recusada ou limite de uso), a análise pausa ou para, sem passar para a conta. "
        + "Modelos e níveis de cada etapa continuam os do plugin.";

    /// <summary>A dica do modo chave do Gemini: o que a chave do Google AI Studio muda na análise e por que o perfil é escolhido à mão.</summary>
    public const string GeminiModoApi = "Modo Gemini API: o análise Google usa esta chave do Google AI Studio pelo Antigravity CLI, sem usar a cota da conta Google. "
        + "O plano Google AI Ultra não inclui sozinho créditos pagos da API: escolha abaixo se a chave tem créditos (camada paga) ou é gratuita. "
        + "Se a API falhar (cota, créditos, chave recusada ou modelo indisponível), a análise pausa ou para, sem passar para a conta nem para outro perfil.";

    /// <summary>A dica do modo conta do Gemini.</summary>
    public const string GeminiModoConta = "Modo conta Google: o análise Google roda pelo Antigravity CLI (agy) com o login da sua conta e a cota do plano. "
        + "A conversa do site gemini.google.com não serve para a análise (não lê arquivos nem roda os validadores): é o Antigravity que tem as ferramentas.";

    public const string PerfilGeminiTitulo = "Perfil da chave";
    public static readonly IReadOnlyList<string> OpcoesPerfilGemini =
        ["Escolha o perfil da chave…", "API com créditos (camada paga do Google AI Studio)", "API gratuita (só testes com processos fictícios)"];

    /// <summary>A posição do perfil na caixa (0 = não escolhido) e a volta.</summary>
    public static int IndiceDoPerfil(PerfilGemini? p) => p switch { PerfilGemini.ApiCreditos => 1, PerfilGemini.ApiGratuita => 2, _ => 0 };

    public static PerfilGemini? PerfilDoIndice(int i) => i switch { 1 => PerfilGemini.ApiCreditos, 2 => PerfilGemini.ApiGratuita, _ => null };

    /// <summary>Os modelos e níveis das seis etapas no perfil que vale agora (ou o pedido de escolha do perfil).</summary>
    public static string MatrizDoGemini(ModoAcesso modo, PerfilGemini? perfilApi) =>
        MatrizGemini.PerfilDoModo(modo, perfilApi) is { } p ? MatrizGemini.Nome(p) + " — " + MatrizGemini.Resumo(p) + "." : "Escolha o perfil da chave para ver os modelos de cada etapa.";

    public const string FicticioTitulo = "API gratuita: só processo fictício";
    public const string FicticioTexto = "Na camada gratuita do Google AI Studio, o Google pode usar o conteúdo enviado para melhorar os produtos dele. Por isso este perfil só pode "
        + "analisar processos de teste, com dados fictícios, nunca um processo real da Prefeitura. Este processo é fictício?";
    public const string FicticioSim = "É fictício: analisar";

    public const string PerfilFixadoTitulo = "A análise continua no perfil em que começou";

    public static string PerfilFixadoTexto(PerfilGemini fixado, PerfilGemini? escolhido) =>
        $"A análise deste processo com o Gemini começou em \"{MatrizGemini.Nome(fixado)}\" e segue nesse perfil até o fim; em Ajustes está "
        + (escolhido is { } e ? $"\"{MatrizGemini.Nome(e)}\"" : "sem perfil escolhido") + ". Modo, modelos e cobrança não mudam no meio de uma análise. Continuar no perfil original?";

    public static string Titulo(Provedor p) => p switch
    {
        Provedor.ChatGpt => "ChatGPT (Codex · plugin analise)", Provedor.Claude => "Claude (Claude Code · plugin análise pro)", _ => "Gemini (Antigravity · plugin análise Google)",
    };

    /// <summary>O que "Escolher programa…" escolhe em cada cartão.</summary>
    public static string RotuloPrograma(Provedor p) => p switch { Provedor.ChatGpt => "Codex", Provedor.Claude => "Python (usado pelo análise pro)", _ => "Python (usado pelo análise Google)" };

    public static string PerguntaRemover(Provedor p) => $"Remover a chave de API do {Provedores.Nome(p)}?";

    public const string ExplicacaoRemover = "A chave guardada neste computador será apagada.";

    /// <summary>O aviso mostrado antes da primeira análise de cada provedor e em "Rever aviso" (versão <see cref="Ajustes.VersaoAviso"/>).</summary>
    public static string Aviso(Provedor p) => p == Provedor.Gemini
        ? "Analisar com o Gemini inicia o análise Google do kit PMVNI, que roda sozinho e chama o Antigravity CLI (agy) deste computador com a sua conta "
          + "Google ou, se escolhida em Ajustes, com a chave do Google AI Studio (Gemini 4 Hydrogen, Carbon e Argon, como o kit define). O Antigravity lê os "
          + "arquivos do processo, usa os programas e as pastas de rede deste computador e envia ao Google o conteúdo de que precisar. Com a chave gratuita, "
          + "o Google pode usar esse conteúdo para melhorar os produtos dele: por isso ela só serve para processos fictícios. O editor não envia nada por "
          + "conta própria: ele só dá a partida, mostra o andamento e importa o relatório no fim."
        : p == Provedor.ChatGpt
        ? "Analisar com o ChatGPT inicia o Codex instalado neste computador, com a sua configuração e o plugin analise do kit PMVNI. O Codex lê os "
          + "arquivos do processo, usa os programas e as pastas de rede deste computador e envia à OpenAI o pedido e o conteúdo de que precisar, como "
          + "faz no app do ChatGPT. O editor não envia nada por conta própria: ele só inicia o Codex, mostra o andamento e importa o relatório no fim."
        : "Analisar com o Claude inicia o análise pro do kit PMVNI, que roda sozinho numa janela minimizada e chama o Claude Code deste computador "
          + "com a sua conta do Claude ou, se escolhida em Ajustes, com a chave da Anthropic API (Sonnet 5.5 e Opus 5.5, como o kit define). O Claude Code lê os arquivos do processo, usa os programas e as "
          + "pastas de rede deste computador e envia à Anthropic o conteúdo de que precisar. O editor não envia nada por conta própria: ele só dá a "
          + "partida, mostra o andamento e importa o relatório no fim.";

    /// <summary>"Aviso aceito em 15/11/2026 (versão 1)." ou "Aviso ainda não aceito: o editor pede o aceite na primeira análise."</summary>
    public static string SituacaoDoAviso(AjustesAgente a) =>
        !a.AvisoAceito ? "Aviso ainda não aceito: o editor pede o aceite na primeira análise."
        : a.AvisoEm is { } em ? $"Aviso aceito em {em.ToLocalTime():dd/MM/yyyy} (versão {a.AvisoVersao})."
        : $"Aviso aceito (versão {a.AvisoVersao}).";

    /// <summary>Uma linha para o caminho do programa e de onde ele veio: "C:\…\codex.exe (padrão)", "… (escolhido)" ou "não encontrado".</summary>
    public static string LinhaDoPrograma(string rotulo, string? achado, string? escolhido) =>
        achado is not null ? $"{rotulo}: {achado} ({(string.IsNullOrWhiteSpace(escolhido) ? "padrão" : "escolhido")})"
        : string.IsNullOrWhiteSpace(escolhido) ? $"{rotulo}: não encontrado"
        : $"{rotulo}: o programa escolhido não existe mais ({escolhido})";

    // ---------- cartões no padrão do WinStatusBar (correções 2026-11-15) ----------

    /// <summary>O nome no alto do cartão (o programa que o editor inicia).</summary>
    public static string NomeDoServico(Provedor p) => p switch { Provedor.ChatGpt => "Codex (ChatGPT)", Provedor.Claude => "Claude Code", _ => "Antigravity (Gemini)" };

    /// <summary>O link da conta embaixo do e-mail.</summary>
    public static string RotuloConta(Provedor p) => p switch { Provedor.ChatGpt => "Conta OpenAI", Provedor.Claude => "Conta Anthropic", _ => "Conta Google" };

    public static string EnderecoConta(Provedor p) => p switch
    {
        Provedor.ChatGpt => "https://chatgpt.com/#settings/Account", Provedor.Claude => "https://claude.ai/settings/account", _ => "https://myaccount.google.com/",
    };

    /// <summary>A nota do bloco da conta do Gemini: o login é o do Antigravity e só "Testar" o confere.</summary>
    public const string GeminiContaNaoVerificada = "O login é o do Antigravity CLI (agy). Use Testar para conferir; para entrar, clique em Entrar e siga o login na janela.";
    public const string GeminiLoginAberto = "Janela do Antigravity aberta: para entrar, siga o login do Google; para sair, digite /logout. Ao voltar ao editor, o cartão testa de novo.";

    public const string NaoConectado = "Não conectado";
    public const string EmailNaoInformado = "e-mail não informado pelo programa";
    public const string SemSessao = "Nenhuma sessão encontrada neste computador. Clique em Entrar para fazer login no programa.";
    public const string LoginAberto = "Janela do login aberta. Conclua lá; o cartão se atualiza quando você voltar ao editor.";
    public const string Configurada = "Configurada";
    public const string NaoConfigurada = "Não configurada";
    public const string Encontrado = "Encontrado";
    public const string NaoEncontrado = "Não encontrado";

    /// <summary>"Entrar" sem sessão; "Sair" com sessão.</summary>
    public static string AcaoDaConta(ContaLocal c) => c.Conectado ? "Sair" : "Entrar";

    /// <summary>O plano lido do computador; "—" sem sessão.</summary>
    public static string Assinatura(ContaLocal c) => !c.Conectado ? "—" : c.Assinatura ?? "Não informada";

    /// <summary>O nome grande do bloco da conta (o nome, senão o e-mail, senão "Conectado").</summary>
    public static string NomeDaConta(ContaLocal c) => !c.Conectado ? NaoConectado : c.Nome ?? c.Email ?? "Conectado";

    /// <summary>A linha do e-mail (vazia quando o e-mail já é o nome ou sem sessão).</summary>
    public static string EmailDaConta(ContaLocal c) => !c.Conectado ? "" : c.Nome is null ? (c.Email is null ? EmailNaoInformado : "") : c.Email ?? EmailNaoInformado;

    public static string PerguntaSair(Provedor p) => $"Sair do {NomeDoServico(p)}?";

    public const string ExplicacaoSair = "O comando oficial do programa encerra o login neste computador. Outros apps que usam a mesma sessão podem pedir login de novo. "
        + "A chave de API guardada no editor não muda.";

    /// <summary>Título da linha de um plugin do kit.</summary>
    public static string TituloPlugin(string plugin) =>
        plugin == PluginsInstalados.AnalisePro ? "Plugin análise pro" : plugin == PluginsInstalados.AnaliseGoogle ? "Plugin análise Google" : "Plugin analise";

    /// <summary>"No Codex · instalado ou atualizado em 15/11/2026 às 13:50" (ou "· não instalado").</summary>
    public static string DescricaoPlugin(string onde, PluginInstalado? p) =>
        p is null ? onde + " · não instalado"
        : p.Em is { } em ? $"{onde} · instalado ou atualizado em {em.ToLocalTime():dd/MM/yyyy} às {em.ToLocalTime():HH:mm}"
        : onde + " · data de instalação desconhecida";

    /// <summary>Onde cada plugin mora, na descrição da linha.</summary>
    public static string OndePlugin(Provedor p, string plugin, bool habilitadoNoCodex) =>
        p == Provedor.ChatGpt ? (habilitadoNoCodex ? "No Codex, habilitado" : "No Codex, não habilitado")
        : plugin == PluginsInstalados.AnaliseGoogle ? "No Codex (é o que o Gemini usa nas análises)"
        : plugin == PluginsInstalados.AnalisePro ? "No Codex (é o que o Claude usa nas análises)"
        : "No Claude Code";

    /// <summary>O valor à direita: "Versão 2.5.0" ou "Não instalado".</summary>
    public static string SeloPlugin(PluginInstalado? p) => p is null ? "Não instalado" : "Versão " + p.Versao;

    /// <summary>O aviso discreto quando a versão mudou desde a última vez que a página foi vista.</summary>
    public static string AtualizadoDesde(string anterior) => $"Atualizado desde a última vez (antes: {anterior})";
}
