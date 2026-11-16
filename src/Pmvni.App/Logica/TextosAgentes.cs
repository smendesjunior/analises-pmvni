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

    /// <summary>Por que "Usar chave de API" fica desabilitado para o Claude enquanto o <c>pro_runtime.py</c> instalado proibir a API paga.</summary>
    public const string ClaudeSoConta = "O análise pro do kit usa somente a conta do Claude (política do kit de 14/11/2026).";

    public static string Titulo(Provedor p) => p == Provedor.ChatGpt ? "ChatGPT (Codex · plugin analise)" : "Claude (Claude Code · plugin análise pro)";

    /// <summary>O que "Escolher programa…" escolhe em cada cartão.</summary>
    public static string RotuloPrograma(Provedor p) => p == Provedor.ChatGpt ? "Codex" : "Python (usado pelo análise pro)";

    public static string PerguntaRemover(Provedor p) => $"Remover a chave de API do {Provedores.Nome(p)}?";

    public const string ExplicacaoRemover = "A chave guardada neste computador será apagada.";

    /// <summary>O aviso mostrado antes da primeira análise de cada provedor e em "Rever aviso" (versão <see cref="Ajustes.VersaoAviso"/>).</summary>
    public static string Aviso(Provedor p) => p == Provedor.ChatGpt
        ? "Analisar com o ChatGPT inicia o Codex instalado neste computador, com a sua configuração e o plugin analise do kit PMVNI. O Codex lê os "
          + "arquivos do processo, usa os programas e as pastas de rede deste computador e envia à OpenAI o pedido e o conteúdo de que precisar, como "
          + "faz no app do ChatGPT. O editor não envia nada por conta própria: ele só inicia o Codex, mostra o andamento e importa o relatório no fim."
        : "Analisar com o Claude inicia o análise pro do kit PMVNI, que roda sozinho numa janela minimizada e chama o Claude Code deste computador "
          + "com a sua conta do Claude (Sonnet 5.5 e Opus 5.5, como o kit define). O Claude Code lê os arquivos do processo, usa os programas e as "
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
    public static string NomeDoServico(Provedor p) => p == Provedor.ChatGpt ? "Codex (ChatGPT)" : "Claude Code";

    /// <summary>O link da conta embaixo do e-mail.</summary>
    public static string RotuloConta(Provedor p) => p == Provedor.ChatGpt ? "Conta OpenAI" : "Conta Anthropic";

    public static string EnderecoConta(Provedor p) => p == Provedor.ChatGpt ? "https://chatgpt.com/#settings/Account" : "https://claude.ai/settings/account";

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
    public static string TituloPlugin(string plugin) => plugin == PluginsInstalados.AnalisePro ? "Plugin análise pro" : "Plugin analise";

    /// <summary>"No Codex · instalado ou atualizado em 15/11/2026 às 13:50" (ou "· não instalado").</summary>
    public static string DescricaoPlugin(string onde, PluginInstalado? p) =>
        p is null ? onde + " · não instalado"
        : p.Em is { } em ? $"{onde} · instalado ou atualizado em {em.ToLocalTime():dd/MM/yyyy} às {em.ToLocalTime():HH:mm}"
        : onde + " · data de instalação desconhecida";

    /// <summary>Onde cada plugin mora, na descrição da linha.</summary>
    public static string OndePlugin(Provedor p, string plugin, bool habilitadoNoCodex) =>
        p == Provedor.ChatGpt ? (habilitadoNoCodex ? "No Codex, habilitado" : "No Codex, não habilitado")
        : plugin == PluginsInstalados.AnalisePro ? "No Codex (é o que o Claude usa nas análises)"
        : "No Claude Code";

    /// <summary>O valor à direita: "Versão 2.5.0" ou "Não instalado".</summary>
    public static string SeloPlugin(PluginInstalado? p) => p is null ? "Não instalado" : "Versão " + p.Versao;

    /// <summary>O aviso discreto quando a versão mudou desde a última vez que a página foi vista.</summary>
    public static string AtualizadoDesde(string anterior) => $"Atualizado desde a última vez (antes: {anterior})";
}
