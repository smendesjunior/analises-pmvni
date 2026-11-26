using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Agentes;

internal sealed record PerfilRodadaChatGpt(int Numero, string Modelo, string Esforco)
{
    public string Texto => Modelo + " · esforço " + DetalhesDasEtapas.NomeDoEsforco(Esforco);
}

internal sealed record GrupoDoMonitorChatGpt(int Id, string Nome, int[] Secoes);
internal sealed record PassoDoPlanoChatGpt(int Secao, string Estado);
internal sealed record RodadaDoMonitorChatGpt(PerfilRodadaChatGpt Perfil, IReadOnlyList<GrupoDoMonitorChatGpt> Grupos, bool Confirmada)
{
    public int Verificacoes => Grupos.Sum(g => g.Secoes.Length);
    public IReadOnlyList<PassoDoPlanoChatGpt> Plano { get; init; } = [];
    public int Realizadas => Confirmada ? Verificacoes : Plano.Count(p => p.Estado == "concluida");
    public double Percentual => 100d * Realizadas / Verificacoes;
}

internal sealed record MonitorAnaliseChatGpt(IReadOnlyList<RodadaDoMonitorChatGpt> Rodadas, string? Qgis, int? Candidatos)
{
    public static MonitorAnaliseChatGpt Aguardando { get; } = new([], null, null);
    public int RodadasConcluidas => Rodadas.Count(r => r.Confirmada);
    public int GruposConcluidos => Rodadas.Where(r => r.Confirmada).Sum(r => r.Grupos.Count);
    public int VerificacoesConcluidas => Rodadas.Where(r => r.Confirmada).Sum(r => r.Verificacoes);
    public int VerificacoesInformadas => Rodadas.Sum(r => r.Realizadas);
    public double? Percentual => Rodadas.Count > 0 ? 100d * VerificacoesInformadas / Rodadas.Sum(r => r.Verificacoes) : null;
}

public sealed partial class OrquestradorAnalise
{
    internal static string NomePlanoDaRodada(int numero) => $"plano-trabalho-rodada-{numero}.json";

    internal static MonitorAnaliseChatGpt LerMonitorChatGpt(string pasta)
    {
        try
        {
            var plano = Path.Combine(pasta, "grupos.json");
            if (!File.Exists(plano) || JsonSerializer.Deserialize<GrupoChatGpt[]>(LerJsonChatGpt(plano), JsonChatGpt) is not { } grupos ||
                !GruposChatGptValidos(grupos)) return MonitorAnaliseChatGpt.Aguardando;

            var consulta = Path.Combine(pasta, "consulta-qgis.json");
            var qgis = LerRegistroParcial(consulta);
            var rodadas = new List<RodadaDoMonitorChatGpt>();
            var caminho = Path.Combine(pasta, "matriz-evidencias.json");
            var matriz = LerRegistroParcial(caminho)?["grupos"] as JsonArray;
            var marca = Path.Combine(pasta, "id-execucao.txt");
            var idAtual = File.Exists(marca) ? LerJsonChatGpt(marca) : null;
            bool DaExecucao(JsonObject? item) => item is not null &&
                (idAtual is null || item["id_execucao"]?.ToString() == idAtual) &&
                !(item["auditoria"]?.ToString().Contains("sujeita a reconferência", StringComparison.OrdinalIgnoreCase) ?? false);
            foreach (var grupo in grupos.OrderBy(g => g.Id))
            {

                var registro = matriz?.OfType<JsonObject>().FirstOrDefault(g => g["id"]?.ToString() == grupo.Id.ToString());
                if (!DaExecucao(registro)) registro = null;
                var confirmado = registro is not null && MatrizConfirmaGrupo(matriz!, grupo);
                var resultado = registro?["resultado"] as JsonObject;
                var realizadas = SecoesComEvidencia(registro).Concat(SecoesComEvidencia(resultado)).ToHashSet();
                if (!confirmado && realizadas.Count < grupo.Secoes.Length)
                {
                    var individual = LerRegistroParcial(Path.Combine(pasta, $"grupo-{grupo.Id:00}.json"));
                    if (DaExecucao(individual) && (individual?["id"] ?? individual?["grupo"])?.ToString() == grupo.Id.ToString())
                    {
                        resultado = individual;
                        realizadas.UnionWith(SecoesComEvidencia(resultado));
                    }
                }
                // Alguns grupos salvam as seções como números e as evidências uma vez para o grupo.
                if (resultado?["evidencias"] is JsonArray { Count: > 0 } && resultado["secoes"] is JsonArray numeros)
                    foreach (var valor in numeros.OfType<JsonValue>())
                        if (valor.TryGetValue<int>(out var numero)) realizadas.Add(numero);
                rodadas.Add(new(new(grupo.Id, grupo.Modelo, grupo.Esforco),
                    [new GrupoDoMonitorChatGpt(grupo.Id, grupo.Nome ?? $"Grupo {grupo.Id}", grupo.Secoes)], confirmado)
                {
                    Plano = grupo.Secoes.Select(s => new PassoDoPlanoChatGpt(s, confirmado || realizadas.Contains(s) ? "concluida" : "pendente")).ToArray()
                });
            }
            return new(rodadas, qgis?["situacao"]?.ToString(), (qgis?["candidatos"] as JsonArray)?.Count);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return MonitorAnaliseChatGpt.Aguardando;
        }
    }

    static IEnumerable<int> SecoesComEvidencia(JsonObject? grupo)
    {
        if (grupo?["secoes"] is not JsonArray secoes) yield break;
        foreach (var secao in secoes.OfType<JsonObject>())
        {
            var estado = (secao["estado"] ?? secao["situacao"])?.ToString();
            if (estado is "ANALISADO" or "NÃO APLICÁVEL" or "NÃO VERIFICÁVEL" &&
                secao["numero"] is JsonValue valor && valor.TryGetValue<int>(out var numero) &&
                (!string.IsNullOrWhiteSpace(secao["evidencia"]?.ToString()) || secao["evidencias"] is JsonArray { Count: > 0 }))
                yield return numero;
        }
    }

    static JsonObject? LerRegistroParcial(string arquivo)
    {
        if (!File.Exists(arquivo)) return null;
        try { return JsonNode.Parse(LerJsonChatGpt(arquivo)) as JsonObject; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    static string LerJsonChatGpt(string arquivo)
    {
        using var entrada = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var leitor = new StreamReader(entrada);
        return leitor.ReadToEnd();
    }

}
