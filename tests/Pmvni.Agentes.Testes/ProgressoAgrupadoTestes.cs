using System.Diagnostics;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>O ajudante para a tela: centenas de milhares de linhas viram no máximo 10 lotes por segundo.</summary>
[Collection("Ambiente")]
public class ProgressoAgrupadoTestes
{
    static SituacaoKit Kit(string codigo, string? etapa = null) => new(codigo, null, etapa, null, null, null, null, Array.Empty<string>());

    [Fact]
    public void coalesce_a_no_maximo_10_por_segundo_sem_perder_a_ordem()
    {
        const int N = 100_000;
        var lotes = new List<LoteProgresso>();
        var relogio = Stopwatch.StartNew();
        using (var p = new ProgressoAgrupado(l => { lock (lotes) lotes.Add(l); }))
        {
            for (var i = 1; i <= N; i++) p.Report(new ProgressoAnalise("linha " + i, i % 1000 == 0 ? Kit("EM_ANDAMENTO", "etapa " + i) : null));
            Thread.Sleep(350);
            p.Report(new ProgressoAnalise(null, Kit("CONCLUIDO")));
        }
        relogio.Stop();

        lock (lotes)
        {
            Assert.InRange(lotes.Count, 2, 2 + (int)(relogio.Elapsed.TotalSeconds * 10));        // ≤ 10 por segundo (mais o final)
            Assert.All(lotes, l => Assert.True(l.Linhas.Count <= 2000));
            Assert.Equal(N, lotes.Sum(l => l.Linhas.Count + l.Descartadas));                      // nada some sem ser contado
            Assert.Equal("linha " + N, lotes.Last(l => l.Linhas.Count > 0).Linhas[^1]);
            var todas = lotes.SelectMany(l => l.Linhas).Select(l => int.Parse(l[6..])).ToList();
            Assert.Equal(todas.OrderBy(x => x), todas);                                           // ordem preservada
            Assert.Equal("CONCLUIDO", lotes.Last(l => l.Situacao is not null).Situacao!.Codigo);  // só a mais recente de cada lote
        }
    }

    [Fact]
    public void dispose_entrega_o_que_faltava_e_depois_ignora()
    {
        var lotes = new List<LoteProgresso>();
        var p = new ProgressoAgrupado(l => { lock (lotes) lotes.Add(l); }, TimeSpan.FromHours(1));      // o temporizador nunca dispara sozinho
        p.Report(new ProgressoAnalise("a", null));
        p.Report(new ProgressoAnalise("b", Kit("EM_ANDAMENTO")));
        Assert.Empty(lotes);
        p.Dispose();
        var lote = Assert.Single(lotes);
        Assert.Equal(new[] { "a", "b" }, lote.Linhas);
        Assert.Equal("EM_ANDAMENTO", lote.Situacao!.Codigo);
        p.Report(new ProgressoAnalise("c", null));
        p.Dispose();
        Assert.Single(lotes);
    }

    [Fact]
    public void maximo_de_linhas_por_lote_descarta_as_mais_antigas()
    {
        var lotes = new List<LoteProgresso>();
        var p = new ProgressoAgrupado(l => lotes.Add(l), TimeSpan.FromHours(1), maximoDeLinhas: 3);
        for (var i = 1; i <= 10; i++) p.Report(new ProgressoAnalise("l" + i, null));
        p.Dispose();
        var lote = Assert.Single(lotes);
        Assert.Equal(new[] { "l8", "l9", "l10" }, lote.Linhas);
        Assert.Equal(7, lote.Descartadas);
    }
}
