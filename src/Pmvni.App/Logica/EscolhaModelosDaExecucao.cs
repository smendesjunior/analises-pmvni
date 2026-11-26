using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Agentes;

namespace Pmvni.App;

internal sealed record OfertaDeModelos(IReadOnlyList<string> Modelos, string Fonte, bool DaConta);

internal sealed record EscolhaModelosDaExecucao(string Rota, string Perfil, string Etapa, string Recusado, bool RecusaEsforco,
    IReadOnlyDictionary<string, CelulaModelos> Etapas, OfertaDeModelos? Oferta)
{
    public static readonly string[] Niveis = ["baixo", "medio", "alto"];
    internal static string NomeEtapa(string etapa) => etapa == "reparo_julgamento" ? "Reparo de julgamento técnico" : SituacaoAnalisePro.NomeEtapa(etapa);
    static string? Texto(JsonNode? valor) => valor is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
    static bool Marcado(JsonNode? valor) => valor is JsonValue v && v.TryGetValue<bool>(out var b) && b;

    internal static IReadOnlyList<string> Ids(IEnumerable<JsonNode?> modelos) =>
        modelos.Select(Texto).OfType<string>().Where(CatalogoIa.ModeloValido).Distinct(StringComparer.Ordinal).ToList();

    internal static string ResumoConfigurado(string pasta, Provedor provedor, string? perfil, string? acesso)
    {
        var rota = ModelosDaAnalise.RotaDoProvedor(provedor);
        var linhas = new List<string> { string.Join(" · ", new[] { Provedores.Nome(provedor), acesso, perfil }.Where(v => !string.IsNullOrWhiteSpace(v))) };
        try
        {
            var arquivo = Path.Combine(pasta, TabelaDeModelos.NomeDoArquivo);
            if (File.Exists(arquivo) && JsonNode.Parse(File.ReadAllText(arquivo)) is JsonObject tabela
                && tabela["rotas"]?[rota]?["perfis"]?[rota == "gemini" ? perfil ?? "" : "padrao"]?["etapas"] is JsonObject etapas)
            {
                void Adicionar(string titulo, JsonNode? celula)
                {
                    var valor = celula is JsonObject c && Texto(c["modelo"]) is { } modelo && Texto(c["nivel"]) is { } nivel
                        ? $"{modelo} · esforço {ModelosDaAnalise.NomeDoNivel(nivel)}" : "Configuração não registrada";
                    linhas.Add($"{titulo} · {valor}");
                }
                Adicionar("Análise das seções", etapas["analyze"]);
                Adicionar("Auditoria técnica", etapas["technical_audit"]);
                Adicionar("Autocorreção, se necessária", etapas["autocorrecao"]);
                if (rota == "codex") Adicionar("Reparo de julgamento, se necessário", tabela["reparo_julgamento_da_execucao"] ?? tabela["reparo_julgamento_previsto"]);
                if (rota is "codex" or "claude")
                {
                    var visual = rota == "codex" ? "Terra 6.1" : "Sonnet 5.5";
                    if (rota == "codex" && tabela["preparo_codex"] is JsonObject preparo)
                        Adicionar("Preparo · todos os documentos, plantas e imagens", preparo);
                    else
                    {
                        var texto = rota == "codex" ? "Luna 6" : "Sonnet 5.5";
                        linhas.Add($"Preparo · documentos: {texto} alto · plantas e imagens: {visual} médio");
                    }
                    linhas.Add($"Relatório · redação: {visual} baixo · conferência final: {visual} médio · PDF montado pelo programa");
                }
                else linhas.Add("Preparo e montagem conforme a configuração da rota");
                return string.Join(Environment.NewLine, linhas);
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { }
        linhas.Add("Tabela de modelos ainda indisponível no registro desta execução.");
        return string.Join(Environment.NewLine, linhas);
    }

    public static EscolhaModelosDaExecucao? Ler(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject d || Texto(d["rota"]) is not ("claude" or "codex" or "gemini")
                || Texto(d["etapa"]) is not { } etapa || Texto(d["modelo_recusado"]) is not { } recusado
                || d["etapas"] is not JsonObject matriz) return null;
            var celulas = new Dictionary<string, CelulaModelos>();
            foreach (var (nome, item) in matriz)
            {
                if (item is not JsonObject c || Texto(c["modelo"]) is not { } modelo || !CatalogoIa.ModeloValido(modelo)
                    || Texto(c["nivel"]) is not { } nivel || !Niveis.Contains(nivel)) return null;
                celulas[nome] = new(modelo, nivel);
            }
            if (!celulas.ContainsKey(etapa)) return null;
            OfertaDeModelos? oferta = null;
            if (d["catalogo"] is JsonObject catalogo && catalogo["modelos"] is JsonArray lista)
                oferta = new(Ids(lista), Texto(catalogo["fonte"]) ?? "catálogo da rota", Marcado(catalogo["verificado"]));
            else if (d["modelos_disponiveis"] is JsonArray candidatas && Texto(d["fonte_modelos"]) is { Length: > 0 } fonte)
                oferta = new(Ids(candidatas), fonte, true);
            return new(Texto(d["rota"])!, Texto(d["perfil"]) ?? "padrao", etapa, recusado, Marcado(d["recusa_esforco"]), celulas, oferta);
        }
        catch (JsonException) { return null; }
    }

    public string? Resposta(IReadOnlyDictionary<string, CelulaModelos> escolhas, out string aviso)
    {
        aviso = "";
        if (!escolhas.TryGetValue(Etapa, out var atual) || (atual.Modelo == Recusado && (!RecusaEsforco || atual.Nivel == Etapas[Etapa].Nivel)))
        {
            aviso = (RecusaEsforco ? "Escolha outro modelo ou esforço" : "Escolha outro modelo") + " para " + NomeEtapa(Etapa) + ".";
            return null;
        }
        var mudancas = new JsonObject();
        foreach (var (etapa, celula) in escolhas)
        {
            if (!Etapas.TryGetValue(etapa, out var anterior) || !CatalogoIa.ModeloValido(celula.Modelo) || !Niveis.Contains(celula.Nivel))
            {
                aviso = "Confira o identificador do modelo e o esforço em " + NomeEtapa(etapa) + ".";
                return null;
            }
            if (celula != anterior || etapa == Etapa)
                mudancas[etapa] = new JsonObject { ["modelo"] = celula.Modelo, ["nivel"] = celula.Nivel };
        }
        return new JsonObject { ["etapas"] = mudancas }.ToJsonString();
    }
}
