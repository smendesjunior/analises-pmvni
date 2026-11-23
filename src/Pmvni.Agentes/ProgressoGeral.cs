namespace Pmvni.Agentes;

/// <summary>Avanço no roteiro do kit. Cada etapa vale uma unidade e só recebe crédito parcial por resultados contados.</summary>
public static class ProgressoGeral
{
    public static (double? Geral, double? Etapa) Calcular(Progresso p)
    {
        if (!p.TemDados || p.TotalEtapas == 0) return (null, null);
        if (p.Etapa is null && p.EtapasConcluidas >= p.TotalEtapas) return (100, null);
        var fracao = p.FracaoObservada;
        var geral = 100 * (p.EtapasConcluidas + (fracao ?? 0)) / p.TotalEtapas;
        // O recibo de conclusão libera 100%, inclusive quando a etapa ainda está consolidando todas as seções.
        return (PercentualEmAndamento(geral), fracao is { } medida ? PercentualEmAndamento(100 * medida) : null);
    }

    // Conserva a primeira fração visível no preparo de lotes grandes.
    static double PercentualEmAndamento(double valor) =>
        Math.Clamp(Math.Round(valor, valor is > 0 and < 1 ? 1 : 0, MidpointRounding.AwayFromZero), 0, 99);
}
