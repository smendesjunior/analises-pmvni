using Pmvni.Agentes;

namespace Pmvni.App;

/// <summary>Os textos fixos de Ajustes › Inteligência artificial (desenho §12.8 e §12.9). Ficam aqui, sem tipos do WinUI, para a tela, o roteiro e
/// a página Novo relatório (Tarefa 20) usarem as mesmas palavras. O texto do aviso tem a versão <see cref="Ajustes.VersaoAviso"/>.</summary>
public static class TextosAgentes
{
    public const string Secao = "Inteligência artificial";

    public const string Introducao =
        "O editor não faz a análise: ele inicia o ChatGPT (Codex) ou o Claude instalados neste computador, com a configuração, os plugins do kit PMVNI, "
        + "os programas (QGIS…) e as pastas de rede deles, e importa o relatório no fim.";

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
}
