namespace Pmvni.Agentes;

/// <summary>Os três agentes locais: o ChatGPT (Codex CLI + plugin analise), o Claude (analise-pro do kit, pelo Claude Code) e o Gemini
/// (analise-google do kit, pelo Antigravity CLI <c>agy</c>).</summary>
public enum Provedor { ChatGpt, Claude, Gemini }

/// <summary>Como o programa se autentica: pelo login dele (<c>Conta</c>), pela conta Google no Gemini Web (<c>ContaGemini</c>) ou por chave de API entregue só como variável do processo filho.</summary>
public enum ModoAcesso { Conta, Chave, ContaGemini }

public static class Provedores
{
    public static IReadOnlyList<Provedor> Ativos { get; } = Array.AsReadOnly(new[] { Provedor.ChatGpt, Provedor.Claude });
    public const string RotaDesativada = "A análise pelo Google/Gemini foi desativada. Use o Codex ou o Claude para uma nova análise.";

    public static string Nome(Provedor p) => p switch { Provedor.ChatGpt => "ChatGPT", Provedor.Claude => "Claude", _ => "Gemini" };

    /// <summary>Em nomes de arquivo e pastas: <c>chatgpt</c>, <c>claude</c> ou <c>gemini</c>.</summary>
    public static string Sufixo(Provedor p) => p switch { Provedor.ChatGpt => "chatgpt", Provedor.Claude => "claude", _ => "gemini" };

    /// <summary>A variável com que o programa aceita a chave (<c>codex exec</c> lê CODEX_API_KEY; o <c>agy</c> lê GEMINI_API_KEY com <c>modelProvider: "gemini"</c>).</summary>
    public static string VariavelChave(Provedor p) => p switch { Provedor.ChatGpt => "CODEX_API_KEY", Provedor.Claude => "ANTHROPIC_API_KEY", _ => "GEMINI_API_KEY" };

    /// <summary>A análise roda pelo controlador do kit (Python: <c>iniciar</c>/<c>situacao</c>, seis etapas, segue sozinha): Claude e Gemini. O ChatGPT é o Codex
    /// como processo filho do editor.</summary>
    public static bool PeloKit(Provedor p) => p != Provedor.ChatGpt;

    /// <summary>Variáveis de chave que nunca passam do ambiente do editor para o processo filho (o kit também as remove dos filhos).</summary>
    public static IReadOnlyList<string> VariaveisRemovidas { get; } = new[]
    {
        "OPENAI_API_KEY", "CODEX_API_KEY", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "GEMINI_API_KEY", "GOOGLE_API_KEY",
    };
}
