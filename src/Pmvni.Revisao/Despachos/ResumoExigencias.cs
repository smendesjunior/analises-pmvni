using System.Globalization;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>As exigências do despacho de correção montadas em código, sem IA (Tarefa 15). Antes, ao anexar o relatório, a IA "resumia"
/// as exigências sozinha; agora o editor usa a mesma regra da correção automática — entram os itens incluídos cuja situação efetiva é
/// diferente de ATENDE, na ordem do relatório, com a providência e a base legal como estão — e a IA só entra a pedido (a instrução na
/// caixa acima da prévia ou "Ajustar com IA" numa exigência). <see cref="ConteudoDespacho.Montar"/> usa esta regra para o rascunho;
/// <see cref="Montar"/> dá o texto das exigências no modelo fixo do despacho (serve também ao despacho feito inteiro no editor).</summary>
public static class ResumoExigencias
{
    /// <summary>Um item do relatório revisado: o id, o número entre os incluídos, a situação efetiva (código de item: ATENDE, NAO_ATENDE,
    /// NAO_COMPROVADO, DIVERGENCIA_INTERNA), se está incluído e o texto da exigência (providência) com o fundamento (base legal).</summary>
    public sealed record Item(string Id, int Numero, string Situacao, bool Incluido, string Texto, string Fundamento, string Titulo = "");

    /// <summary>A regra da correção: incluído e situação efetiva diferente de ATENDE.</summary>
    public static bool Pendente(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item.Incluido && item.Situacao != "ATENDE";
    }

    /// <summary>Só os pendentes (<see cref="Pendente"/>), na ordem recebida.</summary>
    public static IReadOnlyList<Item> Pendentes(IEnumerable<Item> itens)
    {
        ArgumentNullException.ThrowIfNull(itens);
        return itens.Where(Pendente).ToList();
    }

    /// <summary>Os itens incluídos do relatório revisado, na ordem, com a situação, a providência e a base legal efetivas (as revisadas
    /// valem no lugar das originais). Os excluídos não estão no relatório efetivo e não aparecem.</summary>
    public static IReadOnlyList<Item> DoProjeto(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        return DosItens(ConteudoDespacho.Itens(p, out _));
    }

    internal static IReadOnlyList<Item> DosItens(IEnumerable<(string Id, int Numero, JsonObject Item)> itens) =>
        itens.Select(x => new Item(x.Id, x.Numero, ConteudoDespacho.SituacaoDe(x.Item), true,
            TextoEditado.Normalizar(Texto(x.Item["providencia"]) ?? ""), TextoEditado.Normalizar(Texto(x.Item["base_legal"]) ?? ""),
            Texto(x.Item["titulo"]) ?? "")).ToList();

    static string? Texto(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    /// <summary>O texto das exigências no modelo fixo do despacho: uma por linha, numeradas na ordem ("01. ", "02. "…), com o fundamento
    /// entre parênteses como no documento (<see cref="ConteudoDespacho.Exigencia"/>). Itens que atendem e excluídos ficam de fora, mesmo
    /// que venham na lista. Sem nenhum pendente: texto vazio.</summary>
    public static string Montar(IReadOnlyList<Item> pendentes)
    {
        ArgumentNullException.ThrowIfNull(pendentes);
        var linhas = new List<string>();
        foreach (var item in Pendentes(pendentes))
        {
            var e = ConteudoDespacho.Exigencia(item.Texto, item.Fundamento);
            var texto = e.Texto.Length > 0 ? e.Texto + e.Fundamento
                : "(o relatório não traz a providência do ITEM " + item.Numero.ToString("D2", CultureInfo.InvariantCulture) + ")" + e.Fundamento;
            linhas.Add(ConteudoDespacho.Numero(linhas.Count + 1) + texto);
        }
        return string.Join("\n", linhas);
    }
}
