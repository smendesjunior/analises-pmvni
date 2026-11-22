namespace Pmvni.Agentes;

/// <summary>Quanto uma etapa levou numa execução anterior (uma etapa <c>EXECUTED</c> do <c>engine_state.json</c> ou do <c>uso-das-IAs.json</c> de algum processo,
/// <see cref="UsoDasIas.DuracoesDaPasta"/>), com o provedor da rota
/// (a pasta de controle: <c>analise</c> = ChatGPT, <c>analise-pro</c> = Claude, <c>analise-google</c> = Gemini).</summary>
public sealed record DuracaoEtapa(string Etapa, Provedor Provedor, TimeSpan Duracao);

/// <summary>A previsão de término do Monitor, calculada pelo Editor pelas medianas das etapas que faltam. A previsão firme exige ao menos
/// <see cref="MinimoDeExecucoes"/> execuções por etapa no mesmo provedor. A estimativa aproximada aceita um histórico menor ou de outros provedores.</summary>
public static class PrevisaoTermino
{
    public const int MinimoDeExecucoes = 3;

    public static DateTimeOffset? Estimar(Progresso atual, IReadOnlyList<DuracaoEtapa> historico, DateTimeOffset? agora = null)
    {
        ArgumentNullException.ThrowIfNull(atual);
        if (atual.Etapa is not { } etapa || atual.Provedor is not { } provedor || historico is null) return null;
        // As etapas que faltam seguem a ordem do kit nesta rota (Progresso.Ordem); etapa desligada na rota não entra na previsão.
        var etapas = atual.Ordem ?? OrdemDasEtapas.Padrao.Todas;
        var indice = etapas.ToList().IndexOf(etapa);
        if (indice < 0) return null;
        var hora = agora ?? DateTimeOffset.UtcNow;
        var restante = TimeSpan.Zero;
        for (var i = indice; i < etapas.Count; i++)
        {
            var amostras = historico.Where(d => d.Provedor == provedor && d.Etapa == etapas[i] && d.Duracao > TimeSpan.Zero)
                .Select(d => d.Duracao.Ticks).Order().ToList();
            if (amostras.Count < MinimoDeExecucoes) return null;
            var meio = amostras.Count / 2;
            var mediana = TimeSpan.FromTicks(amostras.Count % 2 == 1 ? amostras[meio] : (amostras[meio - 1] + amostras[meio]) / 2);
            if (i == indice && atual.InicioEtapa is { } inicio && inicio <= hora)
            {
                var falta = mediana - (hora - inicio);
                mediana = falta > TimeSpan.Zero ? falta : TimeSpan.Zero;
            }
            restante += mediana;
        }
        return hora + restante;
    }

    /// <summary>Estima o término pela mediana de cada etapa que falta, preferindo as execuções do mesmo provedor e usando as de todos os provedores
    /// quando não houver uma dele. Retorna null se faltar histórico para alguma dessas etapas. Da mediana da etapa atual desconta o tempo já decorrido,
    /// até o mínimo de zero.</summary>
    public static DateTimeOffset? EstimarAproximada(Progresso atual, IReadOnlyList<DuracaoEtapa> historico, DateTimeOffset? agora = null)
    {
        ArgumentNullException.ThrowIfNull(atual);
        if (atual.Etapa is not { } etapa || historico is null) return null;
        var etapas = atual.Ordem ?? OrdemDasEtapas.Padrao.Todas;
        var indice = etapas.ToList().IndexOf(etapa);
        if (indice < 0) return null;
        var hora = agora ?? DateTimeOffset.UtcNow;
        var restante = TimeSpan.Zero;
        for (var i = indice; i < etapas.Count; i++)
        {
            if (MedianaDaEtapa(etapas[i], atual.Provedor, historico) is not { } mediana) return null;
            if (i == indice && atual.InicioEtapa is { } inicio && inicio <= hora)
                mediana = TimeSpan.FromTicks(Math.Max(0, (mediana - (hora - inicio)).Ticks));
            restante += mediana;
        }
        return hora + restante;
    }

    /// <summary>A mediana das durações positivas da etapa, com preferência pelas amostras do mesmo provedor.</summary>
    internal static TimeSpan? MedianaDaEtapa(string etapa, Provedor? provedor, IReadOnlyList<DuracaoEtapa>? historico)
    {
        if (historico is null) return null;
        var daEtapa = historico.Where(d => d.Etapa == etapa && d.Duracao > TimeSpan.Zero).ToList();
        if (daEtapa.Count == 0) return null;
        var doProvedor = daEtapa.Where(d => d.Provedor == provedor).ToList();
        var amostras = (doProvedor.Count > 0 ? doProvedor : daEtapa).Select(d => d.Duracao.Ticks).Order().ToList();
        var meio = amostras.Count / 2;
        if (amostras.Count % 2 == 1) return TimeSpan.FromTicks(amostras[meio]);
        var inferior = amostras[meio - 1];
        return TimeSpan.FromTicks(inferior + (amostras[meio] - inferior) / 2);
    }
}
