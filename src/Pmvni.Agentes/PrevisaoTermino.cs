namespace Pmvni.Agentes;

/// <summary>Quanto uma etapa levou numa execução anterior (uma etapa <c>EXECUTED</c> do <c>engine_state.json</c> ou do <c>uso-das-IAs.json</c> de algum processo,
/// <see cref="UsoDasIas.DuracoesDaPasta"/>), com o provedor da rota
/// (a pasta de controle: <c>analise</c> = ChatGPT, <c>analise-pro</c> = Claude, <c>analise-google</c> = Gemini).</summary>
public sealed record DuracaoEtapa(string Etapa, Provedor Provedor, TimeSpan Duracao);

/// <summary>A previsão de término do Monitor, calculada pelo Editor pelas medianas das etapas que faltam. A previsão firme exige ao menos
/// <see cref="MinimoDeExecucoes"/> execuções por etapa no mesmo provedor. A estimativa aproximada aceita um histórico menor ou de outros provedores.
/// A estimativa inicial preenche as lacunas do histórico pelas durações de partida.</summary>
public static class PrevisaoTermino
{
    public const int MinimoDeExecucoes = 3;

    public static DateTimeOffset? Estimar(Progresso atual, IReadOnlyList<DuracaoEtapa> historico, DateTimeOffset? agora = null)
    {
        ArgumentNullException.ThrowIfNull(atual);
        if (atual.Provedor is not { } provedor || historico is null) return null;
        return EstimarPorDuracoes(atual, etapa => Mediana(historico.Where(d => d.Provedor == provedor && d.Etapa == etapa
            && d.Duracao > TimeSpan.Zero), MinimoDeExecucoes), agora);
    }

    /// <summary>Estima o término pela mediana de cada etapa que falta, preferindo as execuções do mesmo provedor e usando as de todos os provedores
    /// quando não houver uma dele. Retorna null se faltar histórico para alguma dessas etapas.</summary>
    public static DateTimeOffset? EstimarAproximada(Progresso atual, IReadOnlyList<DuracaoEtapa> historico, DateTimeOffset? agora = null)
    {
        ArgumentNullException.ThrowIfNull(atual);
        if (historico is null) return null;
        return EstimarPorDuracoes(atual, etapa => MedianaDaEtapa(etapa, atual.Provedor, historico), agora);
    }

    /// <summary>Estima o término com as medianas disponíveis, completando as etapas sem amostras com a duração inicial.</summary>
    public static DateTimeOffset? EstimarInicial(Progresso atual, IReadOnlyList<DuracaoEtapa>? historico, DateTimeOffset? agora = null)
    {
        ArgumentNullException.ThrowIfNull(atual);
        return EstimarPorDuracoes(atual, etapa => MedianaDaEtapa(etapa, atual.Provedor, historico) ?? DuracoesIniciais.DaEtapa(etapa), agora);
    }

    static DateTimeOffset? EstimarPorDuracoes(Progresso atual, Func<string, TimeSpan?> duracaoDaEtapa, DateTimeOffset? agora)
    {
        if (atual.Etapa is not { } etapa) return null;
        var etapas = atual.Ordem ?? OrdemDasEtapas.Padrao.Todas;
        var indice = etapas.ToList().IndexOf(etapa);
        if (indice < 0) return null;
        var hora = agora ?? DateTimeOffset.UtcNow;
        var restante = TimeSpan.Zero;
        var fracao = atual.FracaoObservada;
        for (var i = indice; i < etapas.Count; i++)
        {
            if (duracaoDaEtapa(etapas[i]) is not { } duracao) return null;
            if (i == indice)
            {
                if (fracao is { } observada) duracao *= 1 - observada;
                else if (atual.InicioEtapa is { } inicio && inicio <= hora)
                    duracao = TimeSpan.FromTicks(Math.Max(0, (duracao - (hora - inicio)).Ticks));
            }
            restante += duracao;
        }
        return hora + restante;
    }

    /// <summary>A mediana das durações positivas da etapa, com preferência pelas amostras do mesmo provedor.</summary>
    internal static TimeSpan? MedianaDaEtapa(string etapa, Provedor? provedor, IReadOnlyList<DuracaoEtapa>? historico)
    {
        if (historico is null) return null;
        var daEtapa = historico.Where(d => d.Etapa == etapa && d.Duracao > TimeSpan.Zero).ToList();
        return Mediana(daEtapa.Where(d => d.Provedor == provedor)) ?? Mediana(daEtapa);
    }

    static TimeSpan? Mediana(IEnumerable<DuracaoEtapa> duracoes, int minimo = 1)
    {
        var amostras = duracoes.Select(d => d.Duracao.Ticks).Order().ToList();
        if (amostras.Count < minimo) return null;
        var meio = amostras.Count / 2;
        if (amostras.Count % 2 == 1) return TimeSpan.FromTicks(amostras[meio]);
        var inferior = amostras[meio - 1];
        return TimeSpan.FromTicks(inferior + (amostras[meio] - inferior) / 2);
    }
}
