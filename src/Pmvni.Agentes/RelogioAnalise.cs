namespace Pmvni.Agentes;

public sealed class RelogioAnalise(TimeSpan acumulado, TimeProvider tempo)
{
    readonly long inicio = tempo.GetTimestamp();
    TimeSpan? encerrado;
    readonly object trava = new();

    public TimeSpan TempoAtivo
    {
        get { lock (trava) return encerrado ?? acumulado + tempo.GetElapsedTime(inicio); }
    }

    public void Parar()
    {
        lock (trava) encerrado ??= acumulado + tempo.GetElapsedTime(inicio);
    }
}
