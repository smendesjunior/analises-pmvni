namespace Pmvni.Agentes;

/// <summary>Pontos de partida em minutos, iguais nas três rotas. A primeira amostra válida da etapa neste computador substitui a duração inicial
/// pela mediana histórica, com preferência pelas amostras da mesma rota.
/// Fluxo linear do kit (2026-11-22, "Orçamento previsto" do motor): preparo 1–4 min; análise em três frentes 12–20 min mais a montagem e o ensaio
/// (0,5–2); auditoria técnica 10–18 min mais o reparo pontual (2–5); auditoria cruzada, produção e revisão final locais, 1–3 min juntas. O ponto de
/// partida é o meio de cada faixa: 40 min no total, dentro da faixa de planejamento de 26–52 min.</summary>
internal static class DuracoesIniciais
{
    internal static TimeSpan? DaEtapa(string etapa) => etapa switch
    {
        "prepare" => TimeSpan.FromMinutes(3),
        "analyze" => TimeSpan.FromMinutes(17),
        "technical_audit" => TimeSpan.FromMinutes(16),
        "audit" => TimeSpan.FromMinutes(1),
        "produce" => TimeSpan.FromMinutes(2),
        "final_review" => TimeSpan.FromMinutes(1),
        _ => null,
    };

    /// <summary>A soma das durações iniciais das etapas do fluxo linear.</summary>
    internal static TimeSpan Total(IEnumerable<string> etapas) =>
        etapas.Aggregate(TimeSpan.Zero, (soma, e) => soma + (DaEtapa(e) ?? TimeSpan.Zero));
}
