using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Acompanhamento;

/// <summary>O que o app achou sobre um processo fora da planilha (2026-11-17), para preencher as colunas vazias: o local e o autor do
/// projeto, com a fonte de cada um (o relatório analisado, a consulta QGIS…).</summary>
public sealed record Descoberta(string? Local, string? FonteLocal, string? Autor, string? FonteAutor)
{
    public static readonly Descoberta Nada = new(null, null, null, null);
    public bool Vazia => string.IsNullOrWhiteSpace(Local) && string.IsNullOrWhiteSpace(Autor);

    /// <summary>Junta duas descobertas: o que esta já tem fica; o que falta vem da outra.</summary>
    public Descoberta Completar(Descoberta outra) => new(
        string.IsNullOrWhiteSpace(Local) ? outra.Local : Local, string.IsNullOrWhiteSpace(Local) ? outra.FonteLocal : FonteLocal,
        string.IsNullOrWhiteSpace(Autor) ? outra.Autor : Autor, string.IsNullOrWhiteSpace(Autor) ? outra.FonteAutor : FonteAutor);
}

/// <summary>Lê o local e o autor do projeto do dados.json de um relatório (a capa do modelo oficial tem os quadros "LOCALIZAÇÃO" e
/// "AUTORIA INDICADA"/"AUTOR DO PROJETO", com o valor em <c>destaque</c>) e a inscrição do lote de uma consulta QGIS.</summary>
public static class DadosDoProcesso
{
    /// <summary>Valores que não identificam nada ("a confirmar", "não informado"…): não entram na planilha.</summary>
    static bool Util(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return false;
        var n = Planilha.Normal(s);
        return n.Length >= 3 && !n.StartsWith("nao ", StringComparison.Ordinal) && !n.Contains("a confirmar", StringComparison.Ordinal)
            && !n.Contains("nao informad", StringComparison.Ordinal) && !n.Contains("nao identificad", StringComparison.Ordinal) && n != "—" && n != "-";
    }

    public static Descoberta DaCapa(ReadOnlySpan<byte> json, string fonte)
    {
        JsonNode? raiz;
        try { raiz = JsonNode.Parse(json.ToArray(), documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }); }
        catch (JsonException) { return Descoberta.Nada; }
        if (raiz?["capa"]?["quadros"] is not JsonArray quadros) return Descoberta.Nada;
        string? local = null, autor = null;
        foreach (var q in quadros.OfType<JsonObject>())
        {
            var titulo = Planilha.Normal(q["titulo"]?.GetValueKind() == JsonValueKind.String ? q["titulo"]!.GetValue<string>() : "");
            var destaque = q["destaque"]?.GetValueKind() == JsonValueKind.String ? q["destaque"]!.GetValue<string>().Trim() : null;
            if (!Util(destaque)) continue;
            if (local is null && (titulo.StartsWith("localiza", StringComparison.Ordinal) || titulo.StartsWith("endereco", StringComparison.Ordinal))) local = destaque;
            else if (autor is null && (titulo.Contains("autor", StringComparison.Ordinal) || titulo.Contains("responsavel tecnico", StringComparison.Ordinal))) autor = destaque;
        }
        return new Descoberta(local, local is null ? null : fonte, autor, autor is null ? null : fonte);
    }

    /// <summary>A inscrição do lote de uma evidência QGIS (<c>lote_candidato</c>, <c>inscricao</c>…); null se não houver.</summary>
    public static string? InscricaoDaConsulta(ReadOnlySpan<byte> json)
    {
        try
        {
            if (JsonNode.Parse(json.ToArray()) is not JsonObject o) return null;
            foreach (var k in new[] { "inscricao", "inscricao_municipal", "lote_candidato", "inscricao_imobiliaria" })
                if (o[k] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() is { Length: >= 11 } s && s.All(char.IsAsciiDigit)) return s;
            if (o["candidate"] is JsonObject c)
                foreach (var k in new[] { "InscImob", "inscricao", "inscricao_municipal" })
                    if (c[k] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() is { Length: >= 11 } s && s.All(char.IsAsciiDigit)) return s;
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>"Av. Prefeito Nicolau Falchetto, nº 1890, Bananeiras" → em maiúsculas, como a planilha usa.</summary>
    public static string ParaPlanilha(string s) => s.Trim().ToUpper(new System.Globalization.CultureInfo("pt-BR"));
}
