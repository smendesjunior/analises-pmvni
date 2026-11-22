namespace Pmvni.Agentes;

/// <summary>Pontos de partida em minutos, iguais nas três rotas. A primeira amostra válida da etapa neste computador substitui a duração inicial
/// pela mediana histórica, com preferência pelas amostras da mesma rota.</summary>
internal static class DuracoesIniciais
{
    internal static TimeSpan? DaEtapa(string etapa) => etapa switch
    {
        "prepare" => TimeSpan.FromMinutes(10),
        "analyze" => TimeSpan.FromMinutes(60),
        "technical_audit" => TimeSpan.FromMinutes(30),
        "audit" => TimeSpan.FromMinutes(25),
        "produce" => TimeSpan.FromMinutes(5),
        "final_review" => TimeSpan.FromMinutes(20),
        _ => null,
    };
}
