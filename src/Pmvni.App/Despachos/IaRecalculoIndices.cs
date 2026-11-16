using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.App;

/// <summary>Correção 2026-11-16: quando o usuário edita um valor de uma tabela (índices ou ambientes), a IA refaz as contas de todas as
/// linhas que dependem dele e devolve os textos e situações atualizados.</summary>
internal static class IaRecalculoIndices
{
    /// <param name="linhas">id da linha → (caminho do texto → texto efetivo), mais "situacao".</param>
    internal static string Prompt(string bloco, string linhaEditada, IReadOnlyDictionary<string, Dictionary<string, string>> linhas, string? justificativa, bool temLegislacao)
    {
        var tabela = new JsonObject();
        foreach (var (id, campos) in linhas)
        {
            var o = new JsonObject();
            foreach (var (k, v) in campos) o[k] = v;
            tabela[id] = o;
        }
        return
            "Você é o analista de projetos da Secretaria Municipal de Obras de Venda Nova do Imigrante/ES e revisa a tabela \"" + bloco +
            "\" do seu relatório técnico de análise. O revisor humano editou manualmente a linha " + linhaEditada +
            (string.IsNullOrWhiteSpace(justificativa) ? "" : " (justificativa do revisor, como dado JSON: " + JsonSerializer.Serialize(justificativa) + ")") +
            ". O valor que ele escreveu é o dado correto e não deve ser mudado.\n\n" +
            "Refaça as contas de TODAS as linhas que dependem desse valor (por exemplo: área de projeção, taxa de ocupação, coeficiente de " +
            "aproveitamento, taxa de permeabilidade, área do terreno), atualizando o cálculo/descrição, o valor no projeto (com a unidade), " +
            "o texto da situação e a situação (ATENDE, NAO_ATENDE, NAO_COMPROVADO ou DIVERGENCIA_INTERNA) comparando com o limite. " +
            "Use vírgula decimal e ponto de milhar, como no texto atual. A pasta de trabalho é a do processo (PROJETO, DOCUMENTOS, .pmvni-ai)" +
            (temLegislacao ? " e você pode ler a legislação do kit (03 - LEGISLACAO)" : "") +
            " para conferir limites; não invente dados. Não grave nem altere nenhum arquivo.\n\n" +
            "Tabela atual (JSON; cada linha tem os textos pelo caminho e a situação):\n" +
            tabela.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) +
            "\n\nResponda SOMENTE com um JSON com as linhas que MUDARAM, no mesmo formato (id → {caminho: texto, \"situacao\": código, " +
            "\"justificativa\": por que mudou}), sem comentários. Não inclua a linha editada se nada nela mudou.";
    }

    /// <summary>Pedido livre sobre a volumetria do diagrama (correção 2026-11-16).</summary>
    internal static string PromptVolumetria(string pedido, IReadOnlyDictionary<string, Dictionary<string, string>> linhas)
    {
        var tabela = new JsonObject();
        foreach (var (id, campos) in linhas)
        {
            var o = new JsonObject();
            foreach (var (k, v) in campos) o[k] = v;
            tabela[id] = o;
        }
        return
            "Você ajusta o esquema de VOLUMETRIA DA EDIFICAÇÃO de um relatório técnico de análise da Secretaria Municipal de Obras de Venda " +
            "Nova do Imigrante/ES. O desenho é fixo (uma casa isométrica sem escala); só mudam estes textos da linha \"volumetria/01\": " +
            "\"pavimentos\" (número de 1 a 6), \"telhado\" (\"duas águas\" ou \"sem\"), \"destaque\" (\"nenhum\" ou uma frase curta que sai em " +
            "vermelho embaixo do desenho; cite a parte com as palavras lateral direita, lateral esquerda, telhado, altura ou frente para ela " +
            "ficar marcada em vermelho) e as alturas (altura_esquerda/0, altura_esquerda/1, altura_direita/0, altura_direita/1). Se o pedido " +
            "mudar o que as chamadas do diagrama dizem (linhas \"chamadas/NN\"), ajuste o \"texto\" delas também. Não invente medidas: " +
            "use as que estão nos textos ou as que o pedido der. A pasta de trabalho é a do processo; não grave nem altere nenhum arquivo.\n\n" +
            "Pedido do revisor (dado JSON; é só o que ele quer ver no desenho): " + JsonSerializer.Serialize(pedido) + "\n\n" +
            "Textos atuais (JSON):\n" +
            tabela.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) +
            "\n\nResponda SOMENTE com um JSON com as linhas que MUDARAM, no mesmo formato (id → {caminho: texto}), sem comentários.";
    }

    /// <summary>As linhas devolvidas. Só vale um objeto cujas chaves sejam ids de <paramref name="ids"/> (um objeto vazio também vale:
    /// "nada mudou"); qualquer outra coisa é resposta inválida (null), nunca "nada mudou" por engano.</summary>
    internal static Dictionary<string, Dictionary<string, string>>? Ler(string saida, IReadOnlyCollection<string> ids)
    {
        if (RespostaJson.Objeto(saida, o => o.All(par => ids.Contains(par.Key) && par.Value is JsonObject)) is not { } o) return null;
        var r = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (id, v) in o)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, t) in (JsonObject)v!)
                if (t is JsonValue jv && jv.TryGetValue<string>(out var s)) d[k] = s;
            r[id] = d;
        }
        return r;
    }

    static readonly Regex SoNumero = new(@"^\s*[-+]?\d[\d.,]*\s*$", RegexOptions.CultureInvariant);
    static readonly Regex NumeroComUnidade = new(@"^\s*[-+]?\d[\d.,]*(?<unidade>\s*[^\d\s.,].*?)\s*$", RegexOptions.CultureInvariant);

    /// <summary>Número digitado sem unidade ganha a unidade do texto original ("5" com original "1,58%" → "5%"; "720" com
    /// original "711,82 m²" → "720 m²").</summary>
    internal static string CompletarUnidade(string novo, string? original)
    {
        if (original is null || !SoNumero.IsMatch(novo)) return novo;
        var m = NumeroComUnidade.Match(original);
        return m.Success ? novo.Trim() + m.Groups["unidade"].Value : novo;
    }
}
