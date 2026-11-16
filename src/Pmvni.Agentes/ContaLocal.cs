using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Agentes;

/// <summary>A conta que o programa da IA deixou gravada neste computador (Ajustes › Inteligência artificial, correções 2026-11-15): só para mostrar
/// nome, e-mail e assinatura no cartão, como o WinStatusBar faz. É indício local, nunca a prova de que a sessão vale no provedor. Nenhum token é
/// guardado, mostrado nem devolvido: os arquivos são lidos, só a presença do token e os rótulos de exibição saem daqui.</summary>
public sealed record ContaLocal(bool Conectado, string? Nome, string? Email, string? Assinatura)
{
    public static readonly ContaLocal Nenhuma = new(false, null, null, null);
}

/// <summary>Lê a conta do Claude Code (<c>.claude.json</c> e <c>.credentials.json</c>) e do Codex (<c>auth.json</c>). Só leitura; arquivo ausente,
/// ilegível, grande demais (mais de 1 MB) ou fora do formato vira "não conectado".</summary>
public static class ContasLocais
{
    const long MaxArquivo = 1024 * 1024;

    /// <summary>Conta do provedor a partir das opções (as pastas são injetáveis: os testes e o roteiro nunca leem as contas de verdade).</summary>
    public static ContaLocal Ler(Provedor p, OpcoesLocalizacao o) => p == Provedor.ChatGpt
        ? Codex(LerArquivo(Path.Combine(o.CodexHome, "auth.json")))
        : o.PastaClaude is null ? ContaLocal.Nenhuma
        : Claude(o.PerfilClaude is null ? null : LerArquivo(o.PerfilClaude), LerArquivo(Path.Combine(o.PastaClaude, ".credentials.json")));

    static string? LerArquivo(string caminho)
    {
        try
        {
            var f = new FileInfo(caminho);
            return f.Exists && f.Length <= MaxArquivo ? File.ReadAllText(caminho) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>Claude Code: sessão = <c>claudeAiOauth.accessToken</c> presente nas credenciais; nome e e-mail do <c>oauthAccount</c> do perfil;
    /// assinatura de <c>subscriptionType</c> (ou do tipo da organização).</summary>
    public static ContaLocal Claude(string? perfilJson, string? credenciaisJson)
    {
        var oauth = Objeto(credenciaisJson)?["claudeAiOauth"] as JsonObject;
        if (Texto(oauth?["accessToken"]) is null) return ContaLocal.Nenhuma;
        var conta = Objeto(perfilJson)?["oauthAccount"] as JsonObject;
        return new ContaLocal(true, Rotulo(Texto(conta?["displayName"]) ?? Texto(conta?["fullName"])), Email(conta?["emailAddress"]),
            NomeDaAssinatura(Provedor.Claude, Texto(oauth?["subscriptionType"]) ?? Texto(conta?["organizationType"])));
    }

    /// <summary>Codex: sessão = <c>tokens.access_token</c> presente; nome, e-mail e plano das declarações do <c>id_token</c> (só a parte do meio,
    /// decodificada para exibir; a assinatura não é conferida e nada é decidido por ela).</summary>
    public static ContaLocal Codex(string? authJson)
    {
        var tokens = Objeto(authJson)?["tokens"] as JsonObject;
        if (Texto(tokens?["access_token"]) is null) return ContaLocal.Nenhuma;
        string? nome = null, email = null, plano = null;
        if (Texto(tokens?["id_token"]) is { } jwt)
        {
            var partes = jwt.Split('.');
            if (partes.Length == 3 && partes[1].Length < 64 * 1024)
            {
                try
                {
                    var b64 = partes[1].Replace('-', '+').Replace('_', '/');
                    b64 = b64.PadRight((b64.Length + 3) / 4 * 4, '=');
                    var declaracoes = Objeto(Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                    email = Email(declaracoes?["email"]);
                    nome = Rotulo(Texto(declaracoes?["name"]));
                    plano = NomeDaAssinatura(Provedor.ChatGpt, Texto((declaracoes?["https://api.openai.com/auth"] as JsonObject)?["chatgpt_plan_type"]));
                }
                catch (FormatException) { }
            }
        }
        return new ContaLocal(true, nome, email, plano);
    }

    /// <summary>"max" → "Claude Max"; "plus" → "ChatGPT Plus"; "business"/"team" → "ChatGPT Business". Desconhecido: null.</summary>
    public static string? NomeDaAssinatura(Provedor p, string? bruto)
    {
        if (Rotulo(bruto) is not { } r) return null;
        var k = r.ToLowerInvariant();
        if (p == Provedor.Claude)
            return k.Contains("max") ? "Claude Max" : k.Contains("pro") ? "Claude Pro" : k.Contains("team") ? "Claude Team"
                : k.Contains("enterprise") ? "Claude Enterprise" : k.Contains("free") ? "Claude Free" : null;
        return k.Contains("enterprise") ? "ChatGPT Enterprise" : k.Contains("business") || k.Contains("team") ? "ChatGPT Business"
            : k.Contains("edu") ? "ChatGPT Edu" : k.Contains("pro") ? "ChatGPT Pro" : k.Contains("plus") ? "ChatGPT Plus"
            : k == "go" ? "ChatGPT Go" : k.Contains("free") ? "ChatGPT Free" : null;
    }

    static JsonObject? Objeto(string? json)
    {
        if (json is null) return null;
        try { return JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
    }

    static string? Texto(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    static string? Rotulo(string? s) => s is { Length: > 0 and <= 120 } && !s.Any(char.IsControl) ? s.Trim() : null;

    static string? Email(JsonNode? n) => Texto(n) is { Length: <= 254 } e && e.Contains('@') && !e.Any(char.IsControl) ? e.Trim() : null;
}
