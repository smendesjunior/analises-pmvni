using Xunit;

namespace Pmvni.Agentes.Testes;

public class ProgressoGeralTestes
{
    static readonly DateTimeOffset Agora = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Analise_UsaCoberturaDoManifestoMesmoSemHistorico()
    {
        var p = new Progresso("analyze", 1, 14, [], Provedor.ChatGpt, Agora.AddHours(-1)) { Ordem = ["prepare", "analyze", "produce"] };

        Assert.Equal((100d * 40 / 75, 50d, true), ProgressoGeral.Calcular(p, null, Agora));
        Assert.Equal(0d, ProgressoGeral.Calcular(p with { SecoesComResultado = 0, SinalDaEtapa = (0, 28) }, null, Agora).Etapa);
    }

    [Fact]
    public void EtapaPorTempo_PrefereMedianaUsaEstimativaInicialSemAmostraEExigeInicio()
    {
        var p = new Progresso("prepare", 0, 0, [], Provedor.ChatGpt, Agora.AddMinutes(-2)) { Ordem = ["prepare", "analyze"] };
        DuracaoEtapa[] historico =
        [
            new("prepare", Provedor.Claude, TimeSpan.FromMinutes(3)),
            new("prepare", Provedor.Gemini, TimeSpan.FromMinutes(5)),
        ];

        Assert.Equal((3.125d, 50d, true), ProgressoGeral.Calcular(p, historico, Agora));
        Assert.Equal((5.9375d, 95d, true), ProgressoGeral.Calcular(p, historico, Agora.AddMinutes(5)));
        Assert.Equal((0d, 0d, true), ProgressoGeral.Calcular(p, historico, Agora.AddMinutes(-3)));
        Assert.Equal((0d, 0d, true), ProgressoGeral.Calcular(p with { InicioEtapa = null }, historico, Agora));
        Assert.Equal((100d * 2 / 70, 20d, true), ProgressoGeral.Calcular(p, null, Agora));
        Assert.Equal(95d, ProgressoGeral.Calcular(p, null, Agora.AddHours(1)).Etapa);

        var comMesmoProvedor = historico.Append(new DuracaoEtapa("prepare", Provedor.ChatGpt, TimeSpan.FromMinutes(8))).ToList();
        Assert.Equal(25d, ProgressoGeral.Calcular(p, comMesmoProvedor, Agora).Etapa);
    }

    [Fact]
    public void PesosPorTempo_CompletamAsMedianasFaltantesComDuracoesIniciais()
    {
        var p = new Progresso("analyze", 1, 7, [], Provedor.ChatGpt) { Ordem = ["prepare", "analyze", "produce"] };
        DuracaoEtapa[] historico =
        [
            new("analyze", Provedor.ChatGpt, TimeSpan.FromMinutes(12)),
            new("analyze", Provedor.Claude, TimeSpan.FromMinutes(100)),
            new("produce", Provedor.Claude, TimeSpan.FromMinutes(6)),
            new("prepare", Provedor.ChatGpt, TimeSpan.FromMinutes(2)),
        ];

        Assert.Equal((25d, 25d, false), ProgressoGeral.Calcular(p, historico, Agora));
        Assert.Equal((40d, 50d, false), ProgressoGeral.Calcular(p with { SecoesComResultado = 14 }, historico, Agora));
        Assert.Equal((100d * 13 / 28, 25d, true), ProgressoGeral.Calcular(p, historico[..^1], Agora));
    }

    [Fact]
    public void SinalObservadoTemPrioridadeSobreOTempoMesmoEmZeroOuSemInicio()
    {
        var p = new Progresso("technical_audit", 0, 28, [], Provedor.ChatGpt, Agora.AddMinutes(-25))
            { Ordem = ["technical_audit"], SinalDaEtapa = (7, 28) };
        DuracaoEtapa[] h = [new("technical_audit", Provedor.ChatGpt, TimeSpan.FromMinutes(20))];

        Assert.Equal((25d, 25d, false), ProgressoGeral.Calcular(p, h, Agora));
        Assert.Equal((25d, 25d, true), ProgressoGeral.Calcular(p with { InicioEtapa = null }, [], Agora));
        Assert.Equal((0d, 0d, false), ProgressoGeral.Calcular(p with { SinalDaEtapa = (0, 28) }, h, Agora));
        Assert.Equal((95d, 95d, false), ProgressoGeral.Calcular(p with { SinalDaEtapa = null }, h, Agora));
    }

    [Fact]
    public void Geral_ReservaCemPorCentoParaAConclusaoESemDadosNulo()
    {
        var p = new Progresso("analyze", 1, 28, []) { Ordem = ["prepare", "analyze"] };

        Assert.Equal((99.9d, 99.9d, true), ProgressoGeral.Calcular(p, null, Agora));
        Assert.Equal(((double?)100, (double?)null, false), ProgressoGeral.Calcular(p with { Etapa = null, EtapasConcluidas = 2 }, null, Agora));
        Assert.Equal(((double?)0, (double?)null, true), ProgressoGeral.Calcular(p with { Etapa = null, EtapasConcluidas = 0 }, null, Agora));
        Assert.Equal(((double?)null, (double?)null, false), ProgressoGeral.Calcular(new Progresso(null, 0, 0, []), null, Agora));
    }
}
