namespace Pmvni.Agentes;

/// <summary>Quanto uma etapa levou numa execução anterior (uma etapa <c>EXECUTED</c> do <c>engine_state.json</c> ou do <c>uso-das-IAs.json</c> de algum processo,
/// <see cref="UsoDasIas.DuracoesDaPasta"/>), com o provedor da rota
/// (a pasta de controle: <c>analise</c> = ChatGPT, <c>analise-pro</c> = Claude, <c>analise-google</c> = Gemini).</summary>
public sealed record DuracaoEtapa(string Etapa, Provedor Provedor, TimeSpan Duracao);

/// <summary>A previsão de término do Monitor, calculada pelo Editor (Tarefa 13; antes a IA gravava <c>estimativa.json</c>): para cada etapa que falta, a
/// mediana das execuções anteriores da mesma etapa no mesmo provedor. A etapa atual conta pelo que ainda falta da mediana dela (nunca menos que zero).
/// Sem ao menos <see cref="MinimoDeExecucoes"/> execuções de cada etapa que falta, não há previsão (null): número inventado não aparece no Monitor.</summary>
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
}
