namespace Pmvni.Agentes;

/// <summary>Calcula os percentuais do Monitor com as etapas do kit, as seções do manifesto e as durações do histórico.</summary>
public static class ProgressoGeral
{
    /// <summary>Pondera as etapas por suas medianas quando há histórico para todas, ou dá o mesmo peso a cada etapa. Na análise, a fração da etapa
    /// é a cobertura do manifesto. Nas demais, o tempo decorrido pode preencher até 95% da etapa enquanto ela estiver em andamento.</summary>
    public static (double? Geral, double? Etapa) Calcular(Progresso p, IReadOnlyList<DuracaoEtapa>? historico, DateTimeOffset agora)
    {
        if (!p.TemDados) return (null, null);
        if (p.Etapa is null && p.EtapasConcluidas >= p.TotalEtapas) return (100, null);

        var ordem = p.Ordem ?? OrdemDasEtapas.Padrao.Todas;
        var medianas = ordem.Select(e => (Etapa: e, Mediana: PrevisaoTermino.MedianaDaEtapa(e, p.Provedor, historico))).ToList();
        var medianaAtual = medianas.FirstOrDefault(e => e.Etapa == p.Etapa).Mediana;
        var fracao = FracaoDaEtapa(p, medianaAtual, agora);
        var porTempo = medianas.All(e => e.Mediana.HasValue);
        var concluidas = p.EtapasExecutadas ?? ordem.Where(e => e != p.Etapa).Take(p.EtapasConcluidas);
        var total = 0d;
        var avancado = 0d;
        foreach (var (etapa, mediana) in medianas)
        {
            var peso = porTempo ? mediana!.Value.Ticks : 1d;
            total += peso;
            if (etapa == p.Etapa) avancado += peso * fracao.GetValueOrDefault();
            else if (concluidas.Contains(etapa)) avancado += peso;
        }
        var geral = total > 0 ? Math.Clamp(100 * avancado / total, 0, p.Etapa is null ? 100 : 99.9) : 0;
        return (geral, 100 * fracao);
    }

    static double? FracaoDaEtapa(Progresso p, TimeSpan? mediana, DateTimeOffset agora)
    {
        if (p.Etapa is null) return null;
        if (p.Etapa == "analyze") return p.TotalSecoes > 0 ? Math.Clamp((double)p.SecoesComResultado / p.TotalSecoes, 0, 1) : null;
        if (p.InicioEtapa is not { } inicio || mediana is not { } duracao) return null;
        return Math.Clamp((agora - inicio).Ticks / (double)duracao.Ticks, 0, 0.95);
    }
}
