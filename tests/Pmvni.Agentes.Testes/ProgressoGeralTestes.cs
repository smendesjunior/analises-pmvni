using Xunit;

namespace Pmvni.Agentes.Testes;

public class ProgressoGeralTestes
{
    [Fact]
    public void GeralContaEtapasConfirmadas_EAFracaoComResultado()
    {
        var p = new Progresso("analyze", 1, 7, []) { Ordem = ["prepare", "analyze", "produce"] };
        Assert.Equal((42d, 25d), ProgressoGeral.Calcular(p));
        Assert.Equal((50d, 50d), ProgressoGeral.Calcular(p with { SecoesComResultado = 14 }));
    }

    [Fact]
    public void SemContador_EtapaIndeterminadaMesmoDepoisDeHoras()
    {
        var p = new Progresso("technical_audit", 1, 28, [], InicioEtapa: DateTimeOffset.UtcNow.AddHours(-2))
            { Ordem = ["analyze", "technical_audit"] };
        Assert.Equal(((double?)50, (double?)null), ProgressoGeral.Calcular(p));
        Assert.Equal((50d, 0d), ProgressoGeral.Calcular(p with { SinalDaEtapa = (0, 28) }));
        Assert.Equal((63d, 25d), ProgressoGeral.Calcular(p with { SinalDaEtapa = (7, 28) }));
    }

    [Fact]
    public void CemPorCentoDependeDoReciboDeConclusao()
    {
        var p = new Progresso("analyze", 1, 28, []) { Ordem = ["prepare", "analyze"] };
        Assert.Equal((99d, 99d), ProgressoGeral.Calcular(p));
        Assert.Equal(((double?)100, (double?)null), ProgressoGeral.Calcular(p with { Etapa = null, EtapasConcluidas = 2 }));
        Assert.Equal(((double?)null, (double?)null), ProgressoGeral.Calcular(new Progresso(null, 0, 0, [])));
    }
}
