namespace Pmvni.Agentes;

/// <summary>O que a tela recebe de uma vez: as linhas novas (as mais antigas já descartadas, <c>Descartadas</c>) e a situação mais recente do kit, se mudou.</summary>
public sealed record LoteProgresso(IReadOnlyList<string> Linhas, SituacaoKit? Situacao, int Descartadas);

/// <summary>Ajudante para a tela (e para o roteiro): recebe o progresso do <see cref="OrquestradorAnalise"/>, que pode chegar às centenas de milhares de linhas, e
/// entrega no máximo <c>10 vezes por segundo</c> (um lote por intervalo), com no máximo <c>maximoDeLinhas</c> linhas por lote. Quem recebe o lote roda na thread do
/// temporizador: a tela deve passar o lote ao dispatcher dela. Nunca bloqueia quem chama <see cref="Report"/>.</summary>
public sealed class ProgressoAgrupado : IProgress<ProgressoAnalise>, IDisposable
{
    readonly Action<LoteProgresso> aoAtualizar;
    readonly int maximoDeLinhas;
    readonly object trava = new();
    readonly object entrega = new();
    readonly Queue<string> linhas = new();
    readonly Timer temporizador;
    SituacaoKit? situacao;
    int descartadas;
    bool descartado;

    public ProgressoAgrupado(Action<LoteProgresso> aoAtualizar, TimeSpan? intervalo = null, int maximoDeLinhas = 2000)
    {
        this.aoAtualizar = aoAtualizar;
        this.maximoDeLinhas = Math.Max(1, maximoDeLinhas);
        var i = intervalo ?? TimeSpan.FromMilliseconds(100);
        temporizador = new Timer(_ => Entregar(), null, i, i);
    }

    public void Report(ProgressoAnalise value)
    {
        lock (trava)
        {
            if (descartado) return;
            if (value.Linha is not null)
            {
                linhas.Enqueue(value.Linha);
                if (linhas.Count > maximoDeLinhas) { linhas.Dequeue(); descartadas++; }
            }
            if (value.Situacao is not null) situacao = value.Situacao;
        }
    }

    void Entregar()
    {
        lock (entrega)          // um lote de cada vez, mesmo que o temporizador dispare de novo enquanto a tela ainda trabalha
        {
            LoteProgresso lote;
            lock (trava)
            {
                if (linhas.Count == 0 && situacao is null && descartadas == 0) return;
                lote = new LoteProgresso(linhas.ToArray(), situacao, descartadas);
                linhas.Clear();
                situacao = null;
                descartadas = 0;
            }
            aoAtualizar(lote);
        }
    }

    /// <summary>Entrega o que faltar e para o temporizador.</summary>
    public void Dispose()
    {
        lock (trava)
        {
            if (descartado) return;
            descartado = true;
        }
        temporizador.Dispose();
        Entregar();
    }
}
