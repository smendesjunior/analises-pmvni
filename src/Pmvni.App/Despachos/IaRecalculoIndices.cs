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
            (bloco == Pmvni.Revisao.TabelasRevisao.Ambientes
                ? "ESQUADRIAS: se a instrução pedir para acrescentar uma esquadria a um ambiente (uma porta, outra janela), devolva-a na linha " +
                  "dele em \"novos_vaos\": [{\"codigo\": \"P1\", \"tipo\": \"Porta\", \"medidas\": \"0,80 × 2,10 m\"}] (medidas = largura × altura " +
                  "em metros). O EDITOR calcula a área de cada vão, o vão total, a comparação com o mínimo e a situação: não devolva " +
                  "\"vaos/N/area\", \"resultado/…\" nem \"situacao\" de um ambiente; devolva só as medidas que a instrução corrigir e os " +
                  "textos explicativos (\"textos/N\") que precisarem mudar para acompanhar a conta nova.\n\n"
                : "") +
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
            "use as que estão nos textos ou as que o pedido der.\n\n" +
            "CHAMADAS NOVAS: se o pedido mandar ADICIONAR, apontar ou marcar um problema (\"adicionar que o afastamento frontal de 120 cm " +
            "não atende\"), crie uma chamada numerada nova — além do destaque, se quiser — em \"" + ChaveNovasChamadas + "\": [{\"vista\": " +
            "em que desenho e em que lista a chamada entra: \"volumetria\" (VOLUMETRIA DA EDIFICAÇÃO, o desenho em que o revisor clicou; é o " +
            "padrão) ou \"planta\" (PLANTA DO TERRENO, só se o pedido disser que é na planta), \"ancora\": onde fica o número DENTRO dessa " +
            "vista (volumetria: " + string.Join(", ", AncorasDaVista(Pmvni.Composicao.Kit.Diagrama.VistaVolumetria)) + "; planta: " +
            string.Join(", ", AncorasDaVista(Pmvni.Composicao.Kit.Diagrama.VistaPlanta)) + "; um afastamento lateral ou do muro na " +
            "volumetria é lateral_direita ou lateral_esquerda; sem ponto certo, deixe \"\" e o editor escolhe um livre), \"titulo\": curto " +
            "(\"Afastamento frontal\"), \"texto\": uma frase com a medida (\"120 cm; o mínimo é 3,00 m.\"), \"situacao\": ATENDE, " +
            "NAO_ATENDE, NAO_COMPROVADO ou DIVERGENCIA_INTERNA}]. O número sai sozinho, na sequência das chamadas que já existem, e a " +
            "chamada entra na lista da vista escolhida. Tudo o que você precisa está neste pedido: NÃO abra arquivos, NÃO rode " +
            "comandos e NÃO pesquise; responda direto.\n\n" +
            "Pedido do revisor (dado JSON; é só o que ele quer ver no desenho): " + JsonSerializer.Serialize(pedido) + "\n\n" +
            "Textos atuais (JSON):\n" +
            tabela.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) +
            "\n\nResponda SOMENTE com um JSON com as linhas que MUDARAM, no mesmo formato (id → {caminho: texto}), mais \"" + ChaveNovasChamadas +
            "\" quando houver chamada nova, sem comentários.";
    }

    /// <summary>A chave das chamadas novas na resposta da volumetria.</summary>
    internal const string ChaveNovasChamadas = "novas_chamadas";

    /// <summary>As âncoras que uma chamada nova pode ter: as da planta e as da volumetria (inclusive a frente do lote e as laterais).</summary>
    internal static IReadOnlyList<string> AncorasDeChamada { get; } =
        Pmvni.Composicao.Kit.Diagrama.PosPlanta.Keys.Concat(Pmvni.Composicao.Kit.Diagrama.PosVolumeTodas.Keys).ToList();

    /// <summary>As âncoras de uma vista ("planta" ou "volumetria").</summary>
    internal static IReadOnlyList<string> AncorasDaVista(string vista) => Pmvni.Composicao.Kit.Diagrama.Posicoes(vista).Keys.ToList();

    /// <summary>Uma chamada nova pedida pela IA (situação já no código de linha de tabela: CONTA_CONFERE, NAO_ATENDE, PENDENTE…).
    /// <see cref="Vista"/> ("planta" ou "volumetria") diz a coluna e o desenho; <see cref="Ancora"/> pode vir vazia ou ser de outra vista
    /// (o editor resolve dentro da vista, ao acrescentar).</summary>
    internal sealed record ChamadaNova(string Ancora, string Titulo, string Texto, string Situacao, string Vista);

    /// <summary>As chamadas novas da resposta (no máximo 4); entradas sem título, texto ou situação válida, ou com vista desconhecida,
    /// ficam de fora. Sem vista, vale <paramref name="vistaPadrao"/> (a do desenho em que o revisor fez o pedido): a âncora sozinha não
    /// decide mais a coluna ("lateral" pedida na volumetria ia parar na planta).</summary>
    internal static List<ChamadaNova> LerNovasChamadas(string saida, IReadOnlyCollection<string> ids, string vistaPadrao = Pmvni.Composicao.Kit.Diagrama.VistaVolumetria)
    {
        var r = new List<ChamadaNova>();
        if (RespostaJson.Objeto(saida, o => Aceita(o, ids)) is not { } o || o[ChaveNovasChamadas] is not JsonArray lista) return r;
        static string T(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";
        foreach (var c in lista.OfType<JsonObject>())
        {
            var vistaPedida = T(c["vista"]);
            var vista = vistaPedida.Length == 0 ? vistaPadrao : Pmvni.Composicao.Kit.Diagrama.NormalizarVista(vistaPedida);
            if (vista is null || T(c["titulo"]).Length == 0 || T(c["texto"]).Length == 0
                || Pmvni.Revisao.TabelasRevisao.SituacaoDeLinha(T(c["situacao"])) is not { } situacao) continue;
            r.Add(new ChamadaNova(T(c["ancora"]).ToLowerInvariant(), T(c["titulo"]), T(c["texto"]), situacao, vista));
            if (r.Count == 4) break;
        }
        return r;
    }

    /// <summary>Um objeto de resposta válido: cada chave é um id conhecido (com um objeto) ou a lista das chamadas novas.</summary>
    static bool Aceita(JsonObject o, IReadOnlyCollection<string> ids) =>
        o.All(par => ids.Contains(par.Key) && par.Value is JsonObject || par.Key == ChaveNovasChamadas && par.Value is JsonArray);

    /// <summary>As linhas devolvidas. Só vale um objeto cujas chaves sejam ids de <paramref name="ids"/> (um objeto vazio também vale:
    /// "nada mudou"); qualquer outra coisa é resposta inválida (null), nunca "nada mudou" por engano.</summary>
    internal static Dictionary<string, Dictionary<string, string>>? Ler(string saida, IReadOnlyCollection<string> ids)
    {
        if (RespostaJson.Objeto(saida, o => Aceita(o, ids)) is not { } o) return null;
        var r = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var (id, v) in o)
        {
            if (id == ChaveNovasChamadas) continue;
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, t) in (JsonObject)v!)
                if (t is JsonValue jv && jv.TryGetValue<string>(out var s)) d[k] = s;
            r[id] = d;
        }
        return r;
    }

    /// <summary>As esquadrias que a IA pediu para acrescentar (<c>novos_vaos</c> de cada linha de ambiente): código, tipo e medidas.
    /// Sem código, o editor numera; sem medidas, a entrada é ignorada (não há conta a fazer).</summary>
    internal static Dictionary<string, List<(string Codigo, string Tipo, string Medidas)>> LerNovosVaos(string saida, IReadOnlyCollection<string> ids)
    {
        var r = new Dictionary<string, List<(string, string, string)>>(StringComparer.Ordinal);
        if (RespostaJson.Objeto(saida, o => o.All(par => ids.Contains(par.Key) && par.Value is JsonObject)) is not { } o) return r;
        static string T(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";
        foreach (var (id, v) in o)
        {
            if ((v as JsonObject)?["novos_vaos"] is not JsonArray lista) continue;
            var vaos = lista.OfType<JsonObject>().Select(x => (T(x["codigo"]), T(x["tipo"]), T(x["medidas"]))).Where(x => x.Item3.Length > 0).Take(6).ToList();
            if (vaos.Count > 0) r[id] = vaos;
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
