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
    public double? Percentual => Confirmada || Plano.Count > 0 ? 100d * Realizadas / Verificacoes : null;
}

internal sealed record MonitorAnaliseChatGpt(IReadOnlyList<RodadaDoMonitorChatGpt> Rodadas, string? Qgis, int? Candidatos)
{
    public static MonitorAnaliseChatGpt Aguardando { get; } = new([], null, null);
    public int RodadasConcluidas => Rodadas.Count(r => r.Confirmada);
    public int GruposConcluidos => Rodadas.Where(r => r.Confirmada).Sum(r => r.Grupos.Count);
    public int VerificacoesConcluidas => Rodadas.Where(r => r.Confirmada).Sum(r => r.Verificacoes);
    public int VerificacoesInformadas => Rodadas.Sum(r => r.Realizadas);
    public double? Percentual => Rodadas.Any(r => r.Percentual.HasValue) ? 100d * VerificacoesInformadas / Rodadas.Sum(r => r.Verificacoes) : null;
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
            var resultadoQgis = File.Exists(consulta) ? LerJsonChatGpt(consulta) : null;
            var rodadas = new List<RodadaDoMonitorChatGpt>();
            var caminho = Path.Combine(pasta, "matriz-evidencias.json");
            var matriz = File.Exists(caminho) ? JsonNode.Parse(LerJsonChatGpt(caminho))?["grupos"] as JsonArray : null;
            foreach (var grupo in grupos.OrderBy(g => g.Id))
            {
                var confirmado = matriz is not null && MatrizConfirmaGrupo(matriz, grupo);
                rodadas.Add(new(new(grupo.Id, grupo.Modelo, grupo.Esforco),
                    [new GrupoDoMonitorChatGpt(grupo.Id, grupo.Nome ?? $"Grupo {grupo.Id}", grupo.Secoes)], confirmado));
            }
            var qgis = resultadoQgis is null ? null : JsonNode.Parse(resultadoQgis);
            return new(rodadas, qgis?["situacao"]?.ToString(), (qgis?["candidatos"] as JsonArray)?.Count);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return MonitorAnaliseChatGpt.Aguardando;
        }
    }

    static IReadOnlyList<PassoDoPlanoChatGpt> LerPlanoDaRodada(string pasta, int numero, IEnumerable<int> secoes)
    {
        try
        {
            var arquivo = new FileInfo(Path.Combine(pasta, NomePlanoDaRodada(numero)));
            if (!arquivo.Exists || arquivo.LinkTarget is not null || arquivo.Length > 16 * 1024) return [];
            var plano = JsonSerializer.Deserialize<PlanoChatGpt>(LerJsonChatGpt(arquivo.FullName), JsonChatGpt);
            if (plano?.Rodada != numero || plano.Passos is not { } passos ||
                passos.Any(p => p is null || p.Estado is not ("pendente" or "em_andamento" or "concluida")) ||
                !passos.Select(p => p.Secao).Order().SequenceEqual(secoes.Order())) return [];
            return passos;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    static string LerJsonChatGpt(string arquivo)
    {
        using var entrada = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var leitor = new StreamReader(entrada);
        return leitor.ReadToEnd();
    }

    sealed record PlanoChatGpt(int Rodada, PassoDoPlanoChatGpt[] Passos);
}
