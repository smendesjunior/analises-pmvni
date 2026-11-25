namespace Pmvni.Agentes;

public sealed class RelogioAnalise(TimeSpan acumulado, TimeProvider tempo, DateTimeOffset? inicioOriginal = null)
{
    readonly long inicio = tempo.GetTimestamp();
    readonly TimeSpan antesDaSessao = inicioOriginal is { } original ? tempo.GetUtcNow() - original : acumulado;
    long? fim, pausa;
    TimeSpan pausado;
    readonly object trava = new();

    public TimeSpan TempoAtivo
    {
        get { lock (trava) return acumulado + tempo.GetElapsedTime(inicio, pausa ?? fim ?? tempo.GetTimestamp()) - pausado; }
    }

    public TimeSpan TempoDecorrido
    {
        get { lock (trava) return (antesDaSessao > acumulado ? antesDaSessao : acumulado) + tempo.GetElapsedTime(inicio, fim ?? tempo.GetTimestamp()); }
    }

    public void EmEspera(bool espera)
    {
        lock (trava)
        {
            if (fim is not null) return;
            if (espera) pausa ??= tempo.GetTimestamp();
            else if (pausa is { } desde)
            {
                pausado += tempo.GetElapsedTime(desde);
                pausa = null;
            }
        }
    }

    public void Parar()
    {
        lock (trava) fim ??= tempo.GetTimestamp();
    }
}
