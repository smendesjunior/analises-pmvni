using System.Globalization;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>O que o <c>pro_runtime.py … situacao</c> do kit informa (campos de <c>pro_auto.py</c>): chave faltando ou de tipo errado = null.
/// Contrato da Tarefa 2 (o mesmo do <c>executar_analise_62.py</c>): <see cref="RetomarEmUtc"/> em <c>AGUARDANDO_COTA</c>, <see cref="Decisao"/> em
/// <c>DECISAO</c> e <see cref="Motivo"/> em <c>FALHA</c>. Na rota ChatGPT o editor monta a mesma situação a partir da última linha do controlador.</summary>
public sealed record SituacaoKit(string Codigo, string? Rotulo, string? Etapa, string? Erro, string? Pdf, int? Pid, DateTimeOffset? Inicio, IReadOnlyList<string> Eventos)
{
    public DateTimeOffset? RetomarEmUtc { get; init; }
    public string? Motivo { get; init; }
    public DecisaoAnalise? Decisao { get; init; }
    internal PerfilRodadaChatGpt? RodadaChatGpt { get; init; }
}

public static class SituacaoAnalisePro
{
    static readonly HashSet<string> Terminais = new(StringComparer.Ordinal)
    {
        "CONCLUIDO", "FALHOU", "OBJECOES", "PAUSADO_COTA", "INTERROMPIDA", "NAO_PARTIU", "CANCELADO",
        // Contrato da Tarefa 2: o kit parou para o analista decidir (o editor pergunta e dá a partida de novo) ou falhou com o motivo.
        "DECISAO", "FALHA",
    };

    /// <summary>As etapas do controlador do kit na ordem antiga (kit 3.x: produce antes de audit): os ids conhecidos e a ordem de reserva
    /// (<see cref="OrdemDasEtapas.Padrao"/>). A ordem real da análise vem do <c>etapas.json</c> do kit (<see cref="OrdemDasEtapas.Ler"/>).</summary>
    public static IReadOnlyList<string> Etapas { get; } = OrdemDasEtapas.Padrao.Todas;

    static readonly string[] Nomes = { "Preparo", "Análise", "Auditoria técnica", "Produção do relatório", "Auditoria cruzada", "Revisão final" };

    public static string NomeEtapa(string etapa)
    {
        for (var i = 0; i < Etapas.Count; i++)
            if (Etapas[i] == etapa) return Nomes[i];
        return etapa == "autocorrecao" ? "Autocorreção" : etapa;
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
            var contrato = SituacaoAnalise62.Ler(r.GetRawText());
            return new SituacaoKit(
                Texto(r, "situacao") ?? "",
                Texto(r, "rotulo"), Texto(r, "etapa"), Texto(r, "erro"), Texto(r, "pdf"),
                r.TryGetProperty("pid", out var pid) && pid.ValueKind == JsonValueKind.Number && pid.TryGetInt32(out var n) ? n : null,
                Data(Texto(r, "inicio")),
                Eventos(r))
            {
                RetomarEmUtc = contrato.RetomarEmUtc, Motivo = contrato.Motivo, Decisao = contrato.Decisao,
            };
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
