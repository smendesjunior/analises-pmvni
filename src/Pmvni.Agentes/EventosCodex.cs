using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Eventos <c>--json</c> do Codex (um JSON por linha) → a linha que a tela mostra (desenho §12.4). Tolerante: o que não é conhecido não aparece;
/// linha que não é JSON aparece como veio.</summary>
public static class EventosCodex
{
    public const int LimiteLinha = 2000;

    /// <summary><c>null</c> = não mostrar.</summary>
    public static string? Linha(string jsonl)
    {
        if (string.IsNullOrWhiteSpace(jsonl)) return null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(jsonl); }
        catch (JsonException) { return Cortar(jsonl); }
        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) return Cortar(jsonl);
            var tipo = Texto(raiz, "type");
            switch (tipo)
            {
                case "item.completed":
                    return raiz.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object ? Item(item) : null;
                case "error":
                    return Erro(Texto(raiz, "message"));
                case "turn.failed":
                    return raiz.TryGetProperty("error", out var erro) && erro.ValueKind == JsonValueKind.Object
                        ? Erro(Texto(erro, "message"))
                        : Erro(Texto(raiz, "message"));
                default:
                    return null;
            }
        }
    }

    static string? Item(JsonElement item)
    {
        switch (Texto(item, "type"))
        {
            case "agent_message":
                return Texto(item, "text") is { } texto ? Cortar("Mensagem: " + texto) : null;
            case "command_execution":
                return Texto(item, "command") is { } comando ? Cortar("Comando: " + comando) : null;
            case "file_change":
                return item.TryGetProperty("changes", out var mudancas) && mudancas.ValueKind == JsonValueKind.Array
                    ? "Arquivos alterados: " + mudancas.GetArrayLength()
                    : null;
            case "mcp_tool_call":
            {
                var servidor = Texto(item, "server");
                var ferramenta = Texto(item, "tool");
                if (ferramenta is null) return null;
                return Cortar("Ferramenta: " + (servidor is null ? ferramenta : servidor + "/" + ferramenta));
            }
            default:
                return null;
        }
    }

    static string? Erro(string? mensagem) => mensagem is null ? null : Cortar("Erro: " + mensagem);

    static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>Uma linha só (quebras viram espaço) e no máximo 2.000 caracteres.</summary>
    static string Cortar(string s)
    {
        if (s.Contains('\n') || s.Contains('\r'))
            s = string.Join(' ', s.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None));
        return s.Length > LimiteLinha ? s[..LimiteLinha] : s;
    }
}
