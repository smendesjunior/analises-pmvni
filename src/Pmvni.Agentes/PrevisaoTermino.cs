namespace Pmvni.Agentes;

/// <summary>Quanto uma etapa levou numa execução anterior (uma etapa <c>EXECUTED</c> do <c>engine_state.json</c> ou do <c>uso-das-IAs.json</c> de algum processo,
/// <see cref="UsoDasIas.DuracoesDaPasta"/>), com o provedor da rota
/// (a pasta de controle: <c>analise</c> = ChatGPT, <c>analise-pro</c> = Claude, <c>analise-google</c> = Gemini).</summary>
public sealed record DuracaoEtapa(string Etapa, Provedor Provedor, TimeSpan Duracao);

/// <summary>A previsão de término do Monitor, calculada pelo Editor pelas medianas das etapas que faltam. A previsão firme exige ao menos
/// <see cref="MinimoDeExecucoes"/> execuções por etapa no mesmo provedor. A estimativa aproximada aceita um histórico menor da mesma rota ou o ritmo observado na etapa atual.
/// A estimativa inicial preenche as lacunas do histórico pelas durações de partida.</summary>
public static class PrevisaoTermino
{
    public const int MinimoDeExecucoes = 3;

    public static DateTimeOffset? Estimar(Progresso atual, IReadOnlyList<DuracaoEtapa> historico, DateTimeOffset? agora = null) =>
        EstimarMedido(atual, historico, agora ?? DateTimeOffset.UtcNow, MinimoDeExecucoes);

    /// <summary>Com histórico curto, aceita uma execução positiva da mesma rota por etapa restante.
    /// Se a etapa atual já entregou três ou mais unidades, usa o ritmo medido dessa execução.</summary>
    public static DateTimeOffset? EstimarAproximada(Progresso atual, IReadOnlyList<DuracaoEtapa> historico, DateTimeOffset? agora = null) =>
        EstimarMedido(atual, historico, agora ?? DateTimeOffset.UtcNow, 1);

    static DateTimeOffset? EstimarMedido(Progresso atual, IReadOnlyList<DuracaoEtapa> historico, DateTimeOffset agora, int minimo)
    {
        ArgumentNullException.ThrowIfNull(atual);
        if (atual.Provedor is null || atual.Etapa is null || historico is null) return null;
        var ordem = atual.Ordem ?? OrdemDasEtapas.Padrao.Todas;
        var indice = ordem.ToList().IndexOf(atual.Etapa);
        if (indice < 0) return null;
        var decorrido = atual.InicioEtapa is { } inicio && inicio < agora ? agora - inicio : TimeSpan.Zero;
        var restante = TimeSpan.Zero;
        foreach (var etapa in ordem.Skip(indice))
        {
            var amostras = historico.Where(d => d.Etapa == etapa && d.Provedor == atual.Provedor && d.Duracao > TimeSpan.Zero);
            TimeSpan? tempo;
            if (etapa != atual.Etapa) tempo = Mediana(amostras, minimo);
            else if (atual.FracaoObservada is > 0 and < 1 && atual.InstanteDaMedicao is { } medida &&
                atual.InicioEtapa is { } partida && medida > partida && medida <= agora &&
                (atual.SinalDaEtapa?.Feitos ?? atual.SecoesComResultado) >= 3)
            {
                // Com avanço medido, o ritmo é desta etapa. A previsão continua identificada como aproximada.
                if (minimo > 1) return null;
                var fracao = atual.FracaoObservada.Value;
                var medido = medida - partida;
                var duracaoProjetada = medido.Ticks / fracao;
                // Os arquivos das frentes podem chegar em lotes. Com poucas unidades, a mediana de execuções
                // comparáveis tem mais peso. À medida que chegam resultados, predomina a execução em curso.
                if (Mediana(amostras.Where(d => d.Duracao > medido), MinimoDeExecucoes) is { } mediana)
                    duracaoProjetada = (1 - fracao) * mediana.Ticks + fracao * duracaoProjetada;
                var ticks = duracaoProjetada - decorrido.Ticks;
                if (ticks <= 0 || ticks >= TimeSpan.MaxValue.Ticks) return null;
                tempo = TimeSpan.FromTicks((long)ticks);
            }
            else
            {
                // A execução ainda está aberta. Só durações que passaram deste ponto permitem prever o saldo.
                tempo = Mediana(amostras.Where(d => d.Duracao > decorrido), minimo) - decorrido;
            }
            if (tempo is null || tempo.Value > DateTimeOffset.MaxValue - agora - restante) return null;
            restante += tempo.Value;
        }
        return agora + restante;
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
