namespace Pmvni.Agentes;

/// <summary>Os dois agentes locais: o ChatGPT (Codex CLI + plugin analise) e o Claude (analise-pro do kit, pelo Claude Code).</summary>
public enum Provedor { ChatGpt, Claude }

/// <summary>Como o programa se autentica: pelo login dele (<c>Conta</c>) ou por chave de API entregue só como variável do processo filho.</summary>
public enum ModoAcesso { Conta, Chave }

public static class Provedores
{
    public static string Nome(Provedor p) => p == Provedor.ChatGpt ? "ChatGPT" : "Claude";

    /// <summary>Em nomes de arquivo e pastas: <c>chatgpt</c> ou <c>claude</c>.</summary>
    public static string Sufixo(Provedor p) => p == Provedor.ChatGpt ? "chatgpt" : "claude";

    /// <summary>A variável com que o programa aceita a chave (<c>codex exec</c> lê CODEX_API_KEY; conferido no passo 0 da Tarefa 19).</summary>
    public static string VariavelChave(Provedor p) => p == Provedor.ChatGpt ? "CODEX_API_KEY" : "ANTHROPIC_API_KEY";

    /// <summary>Variáveis de chave que nunca passam do ambiente do editor para o processo filho (o kit também as remove dos filhos).</summary>
    public static IReadOnlyList<string> VariaveisRemovidas { get; } = new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN" };
}
