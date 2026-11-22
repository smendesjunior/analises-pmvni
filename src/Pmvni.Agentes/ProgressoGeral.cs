namespace Pmvni.Agentes;

/// <summary>Calcula os percentuais do Monitor com as etapas do kit, as seções do manifesto e as durações do histórico.</summary>
public static class ProgressoGeral
{
    /// <summary>Pondera cada etapa pela mediana histórica ou pela duração inicial. Para a etapa atual, prioriza o sinal do kit. Quando só há tempo
    /// decorrido, ele pode preencher até 95% da etapa. O indicador Inicial informa se algum percentual usou uma duração inicial.</summary>
    public static (double? Geral, double? Etapa, bool Inicial) Calcular(Progresso p, IReadOnlyList<DuracaoEtapa>? historico, DateTimeOffset agora)
    {
        if (!p.TemDados) return (null, null, false);
        if (p.Etapa is null && p.EtapasConcluidas >= p.TotalEtapas) return (100, null, false);

        var ordem = p.Ordem ?? OrdemDasEtapas.Padrao.Todas;
        var duracoes = ordem.Select(e => (Etapa: e, Mediana: PrevisaoTermino.MedianaDaEtapa(e, p.Provedor, historico))).ToList();
        var concluidas = p.EtapasExecutadas ?? ordem.Where(e => e != p.Etapa).Take(p.EtapasConcluidas);
        var fracaoObservada = p.FracaoObservada;
        var fracao = fracaoObservada ?? 0;
        var total = 0d;
        var avancado = 0d;
        var temTodosOsPesos = true;
        var pesoInicial = false;
        var fracaoInicial = false;
        foreach (var (etapa, mediana) in duracoes)
        {
            if ((mediana ?? DuracoesIniciais.DaEtapa(etapa)) is not { } duracao)
            {
                temTodosOsPesos = false;
                continue;
            }
            pesoInicial |= mediana is null;
            var peso = (double)duracao.Ticks;
            total += peso;
            if (etapa == p.Etapa)
            {
                if (fracaoObservada is null && p.InicioEtapa is { } inicio)
                {
                    fracao = Math.Clamp((agora - inicio).Ticks / peso, 0, 0.95);
                    fracaoInicial = mediana is null;
                }
                avancado += peso * fracao;
            }
            else if (concluidas.Contains(etapa)) avancado += peso;
        }
        double? geral = temTodosOsPesos && total > 0 ? Math.Clamp(100 * avancado / total, 0, 99.9) : null;
        double? percentualEtapa = p.Etapa is null ? null : Math.Min(100 * fracao, 99.9);
        return (geral, percentualEtapa, fracaoInicial || (geral.HasValue && pesoInicial));
    }
}
