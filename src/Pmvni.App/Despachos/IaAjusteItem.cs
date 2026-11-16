using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.App;

/// <summary>"Ajustar texto com a IA" (correção 2026-11-16): o usuário mudou a situação de um item e justificou; a IA revê a própria
/// conclusão e reescreve os campos para sustentar a situação escolhida, com base no projeto (pasta do processo) e na legislação do kit.</summary>
internal static class IaAjusteItem
{
    internal static string Prompt(string situacaoOriginal, string situacaoNova, string justificativa, IReadOnlyDictionary<string, string> campos, bool temLegislacao)
    {
        var atual = new JsonObject();
        foreach (var (k, v) in campos) atual[k] = v;
        return
            "Você é o analista de projetos da Secretaria Municipal de Obras de Venda Nova do Imigrante/ES e revisa um item do seu próprio " +
            "relatório técnico de análise. O revisor humano mudou a situação do item de \"" + situacaoOriginal + "\" para \"" + situacaoNova +
            "\" e justificou assim (dado JSON): " + JsonSerializer.Serialize(justificativa) + ".\n\n" +
            "Reveja a sua conclusão. A pasta de trabalho é a do processo (PROJETO, DOCUMENTOS, .pmvni-ai)" +
            (temLegislacao ? " e você também pode ler a legislação do kit (03 - LEGISLACAO)" : "") +
            ". Confira no projeto e na legislação o que sustenta a situação escolhida e reescreva o item para que fique coerente com ela: " +
            "a verificação técnica diz o que foi encontrado e por que a situação é essa; a providência diz o que o requerente deve fazer " +
            "(ou que nada é exigido, se atende); a base legal cita só dispositivos que você confirmou. Não invente medidas, artigos nem " +
            "documentos: se algo não puder ser confirmado, diga isso no texto. Mantenha o estilo técnico, curto e neutro, em português do " +
            "Brasil, e as citações \"item NN\" como estão. Não grave nem altere nenhum arquivo.\n\n" +
            "Item atual (JSON):\n" + atual.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
            "\n\nResponda SOMENTE com um JSON com as mesmas chaves (titulo, verificacao, providencia, base_legal, referencia), sem comentários.";
    }

    /// <summary>Os campos devolvidos; só aceita um objeto com ao menos uma das chaves do item.</summary>
    internal static Dictionary<string, string>? Ler(string saida)
    {
        string[] chaves = ["titulo", "verificacao", "providencia", "base_legal", "referencia"];
        if (RespostaJson.Objeto(saida, o => chaves.Any(o.ContainsKey)) is not { } o) return null;
        var r = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var k in chaves)
            if (o[k] is JsonValue jv && jv.TryGetValue<string>(out var s)) r[k] = s;
        return r;
    }
}
