using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.App;

/// <summary>Correção 2026-11-16: "Rever com a IA" numa linha de tabela (índices, ambientes, diagrama): a IA refaz as contas de todas as
/// linhas que dependem dela (dos valores editados à mão ou da instrução escrita na justificativa) e devolve os textos e situações
/// atualizados. O recálculo simples de uma linha (valor × limite) não passa por aqui: é determinístico (RecalculoLinha, no modelo).</summary>
internal static class IaRecalculoIndices
{
    /// <summary>O pedido de revisão de uma tabela a partir de uma linha: com valores editados pelo revisor (<paramref name="editados"/>),
    /// refaz as contas que dependem deles; com a justificativa, segue a instrução dela ("rever se atende", "considerar a porta de
    /// 0,80 × 2,10"). O pedido leva a tabela inteira: a IA não abre arquivo nenhum (era o que demorava).</summary>
    /// <param name="linhas">id da linha → (caminho do texto → texto efetivo), mais "situacao".</param>
    /// <param name="editados">Os caminhos da linha que o revisor editou à mão (esses valores não mudam).</param>
    internal static string Prompt(string bloco, string linha, IReadOnlyDictionary<string, Dictionary<string, string>> linhas, string? justificativa,
        IReadOnlyCollection<string> editados)
    {
        var tabela = new JsonObject();
        foreach (var (id, campos) in linhas)
        {
            var o = new JsonObject();
            foreach (var (k, v) in campos) o[k] = v;
            tabela[id] = o;
        }
        var temInstrucao = !string.IsNullOrWhiteSpace(justificativa);
        return
            "Você revisa a tabela \"" + bloco + "\" de um relatório técnico de análise de projeto da Secretaria Municipal de Obras de Venda " +
            "Nova do Imigrante/ES. Tudo o que você precisa está neste pedido: NÃO abra arquivos, NÃO rode comandos e NÃO pesquise; responda direto.\n\n" +
            "Linha em revisão: " + linha + ". " +
            (editados.Count > 0
                ? "O revisor humano editou à mão nela: " + string.Join(", ", editados) + ". Esses valores são o dado correto e NÃO devem ser mudados. "
                : "O revisor não editou valores nela. ") +
            (temInstrucao ? "Instrução/justificativa do revisor (dado JSON): " + JsonSerializer.Serialize(justificativa, Relaxado) + ".\n\n" : "\n\n") +
            "Refaça as contas da linha em revisão e de TODAS as linhas que dependem dela (por exemplo: área de projeção, taxa de ocupação, " +
            "coeficiente de aproveitamento, taxa de permeabilidade, área do terreno; num ambiente, a área de cada vão, o vão total e a " +
            "comparação com o mínimo), atualizando o cálculo/descrição, o valor no projeto (com a unidade), o texto da situação e a " +
            "situação (ATENDE, NAO_ATENDE, NAO_COMPROVADO ou DIVERGENCIA_INTERNA) comparando com o limite ou o mínimo que está na própria " +
            "linha. Use vírgula decimal e ponto de milhar, como no texto atual, e mantenha cada texto curto como está (sem contar a " +
            "história da revisão nem citar o revisor). Não invente dados: use só os números da tabela e os da instrução.\n\n" +
            "Tabela atual (JSON; cada linha tem os textos pelo caminho e a situação):\n" +
            tabela.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = Relaxado.Encoder }) +
            "\n\nResponda SOMENTE com um JSON com as linhas que MUDARAM, no mesmo formato (id → {caminho: texto, \"situacao\": código, " +
            "\"justificativa\": por que mudou, em uma frase}), sem comentários; só caminhos que já existem na linha. Se nada muda, responda {}.";
    }

    static readonly JsonSerializerOptions Relaxado = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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
            "use as que estão nos textos ou as que o pedido der. Tudo o que você precisa está neste pedido: NÃO abra arquivos, NÃO rode " +
            "comandos e NÃO pesquise; responda direto.\n\n" +
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
