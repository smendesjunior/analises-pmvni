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
        return (InteiroEmAndamento(geral), fracao is { } medida ? InteiroEmAndamento(100 * medida) : null);
    }

    public static (double? Geral, double? Etapa, bool Estimado) ParaMonitor(Progresso p, IReadOnlyList<DuracaoEtapa>? historico, DateTimeOffset agora)
    {
        var (geral, etapa) = Calcular(p);
        var estimado = false;
        // Algumas ferramentas entregam um lote inteiro de uma vez. Até a primeira unidade, a tela
        // identifica a projeção pelo tempo como aproximada. O relógio sozinho nunca conclui uma etapa.
        if (!p.Pausado && p.Etapa is { } nome && p.InicioEtapa is { } inicio && agora > inicio
            && p.FracaoObservada is null or 0
            && (PrevisaoTermino.MedianaDaEtapa(nome, p.Provedor, historico)
                ?? DuracoesIniciais.DaEtapa(nome)) is { } duracao && duracao > TimeSpan.Zero)
        {
            var minutos = (agora - inicio).TotalMinutes;
            var fracao = Math.Min(.95, minutos / (minutos + duracao.TotalMinutes));
            etapa = Math.Round(100 * fracao, 1);
            geral = Math.Round(100 * (p.EtapasConcluidas + fracao) / p.TotalEtapas, 1);
            geral = Math.Min(99, geral.Value);
            estimado = true;
        }
        return (geral, etapa, estimado);
    }

    static double InteiroEmAndamento(double valor) => Math.Clamp(Math.Round(valor, MidpointRounding.AwayFromZero), 0, 99);
}
