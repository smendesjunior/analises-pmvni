using System.Globalization;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>O que o <c>pro_runtime.py … situacao</c> do kit informa (campos de <c>pro_auto.py</c>): chave faltando ou de tipo errado = null.</summary>
public sealed record SituacaoKit(string Codigo, string? Rotulo, string? Etapa, string? Erro, string? Pdf, int? Pid, DateTimeOffset? Inicio, IReadOnlyList<string> Eventos);

public static class SituacaoAnalisePro
{
    static readonly HashSet<string> Terminais = new(StringComparer.Ordinal)
    {
        "CONCLUIDO", "FALHOU", "OBJECOES", "PAUSADO_COTA", "INTERROMPIDA", "NAO_PARTIU", "CANCELADO",
    };

    /// <summary>As etapas do controlador do kit, na ordem em que ele as executa.</summary>
    public static IReadOnlyList<string> Etapas { get; } = new[] { "prepare", "analyze", "technical_audit", "produce", "audit", "final_review" };

    static readonly string[] Nomes = { "Preparo", "Análise", "Auditoria técnica", "Produção do relatório", "Auditoria cruzada", "Revisão final" };

    public static string NomeEtapa(string etapa)
    {
        for (var i = 0; i < Etapas.Count; i++)
            if (Etapas[i] == etapa) return Nomes[i];
        return etapa;
    }

    /// <summary>Situações em que o kit parou de vez (o que sobra é andamento ou espera da cota, que o kit continua sozinho).</summary>
    public static bool Terminal(string codigo) => Terminais.Contains(codigo);

    public static SituacaoKit Ler(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return Vazia; }
        using (doc)
        {
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return Vazia;
            return new SituacaoKit(
                Texto(r, "situacao") ?? "",
                Texto(r, "rotulo"), Texto(r, "etapa"), Texto(r, "erro"), Texto(r, "pdf"),
                r.TryGetProperty("pid", out var pid) && pid.ValueKind == JsonValueKind.Number && pid.TryGetInt32(out var n) ? n : null,
                Data(Texto(r, "inicio")),
                Eventos(r));
        }
    }

    static SituacaoKit Vazia => new("", null, null, null, null, null, null, Array.Empty<string>());

    static string? Texto(JsonElement e, string nome) =>
        e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    /// <summary>O kit grava a hora local sem fuso (<c>datetime.now().isoformat()</c>): sem fuso, vale a hora local deste computador.</summary>
    static DateTimeOffset? Data(string? texto) =>
        texto is not null && DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d) ? d : null;

    static IReadOnlyList<string> Eventos(JsonElement r)
    {
        if (!r.TryGetProperty("eventos", out var lista) || lista.ValueKind != JsonValueKind.Array) return Array.Empty<string>();
        var itens = new List<string>();
        foreach (var e in lista.EnumerateArray())
        {
            if (e.ValueKind == JsonValueKind.String && e.GetString() is { } s) itens.Add(s);
            else if (e.ValueKind == JsonValueKind.Object && Texto(e, "texto") is { } t) itens.Add(t);
        }
        return itens;
    }
}
