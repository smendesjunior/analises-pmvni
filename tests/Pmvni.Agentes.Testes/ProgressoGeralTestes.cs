using Xunit;

namespace Pmvni.Agentes.Testes;

public class ProgressoGeralTestes
{
    static readonly DateTimeOffset Agora = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Analise_UsaCoberturaDoManifestoMesmoSemHistorico()
    {
        var p = new Progresso("analyze", 1, 14, [], Provedor.ChatGpt, Agora.AddHours(-1)) { Ordem = ["prepare", "analyze", "produce"] };

        Assert.Equal((50d, 50d), ProgressoGeral.Calcular(p, null, Agora));
        Assert.Equal(0d, ProgressoGeral.Calcular(p with { SecoesComResultado = 0 }, null, Agora).Etapa);
    }

    [Fact]
    public void EtapaPorTempo_UsaMedianaComTetoEExigeHistoricoEInicio()
    {
        var p = new Progresso("prepare", 0, 0, [], Provedor.ChatGpt, Agora.AddMinutes(-2)) { Ordem = ["prepare", "analyze"] };
        DuracaoEtapa[] historico =
        [
            new("prepare", Provedor.Claude, TimeSpan.FromMinutes(3)),
            new("prepare", Provedor.Gemini, TimeSpan.FromMinutes(5)),
        ];

        Assert.Equal((25d, 50d), ProgressoGeral.Calcular(p, historico, Agora));
        Assert.Equal((47.5d, 95d), ProgressoGeral.Calcular(p, historico, Agora.AddMinutes(5)));
        Assert.Equal((0d, 0d), ProgressoGeral.Calcular(p, historico, Agora.AddMinutes(-3)));
        Assert.Equal(((double?)0, (double?)null), ProgressoGeral.Calcular(p with { InicioEtapa = null }, historico, Agora));
        Assert.Equal(((double?)0, (double?)null), ProgressoGeral.Calcular(p, null, Agora));
    }

    [Fact]
    public void PesosPorTempo_ExigemTodasAsMedianasECrescemComAFracaoDaEtapa()
    {
        var p = new Progresso("analyze", 1, 7, [], Provedor.ChatGpt) { Ordem = ["prepare", "analyze", "produce"] };
        DuracaoEtapa[] historico =
        [
            new("analyze", Provedor.ChatGpt, TimeSpan.FromMinutes(12)),
            new("analyze", Provedor.Claude, TimeSpan.FromMinutes(100)),
            new("produce", Provedor.Claude, TimeSpan.FromMinutes(6)),
            new("prepare", Provedor.ChatGpt, TimeSpan.FromMinutes(2)),
        ];

        Assert.Equal((25d, 25d), ProgressoGeral.Calcular(p, historico, Agora));
        Assert.Equal((40d, 50d), ProgressoGeral.Calcular(p with { SecoesComResultado = 14 }, historico, Agora));
        Assert.Equal(100 * 1.25 / 3, ProgressoGeral.Calcular(p, historico[..^1], Agora).Geral);
    }

    [Fact]
    public void Geral_ReservaCemPorCentoParaAConclusaoESemDadosNulo()
    {
        var p = new Progresso("analyze", 1, 28, []) { Ordem = ["prepare", "analyze"] };

        Assert.Equal((99.9d, 100d), ProgressoGeral.Calcular(p, null, Agora));
        Assert.Equal(((double?)100, (double?)null), ProgressoGeral.Calcular(p with { Etapa = null, EtapasConcluidas = 2 }, null, Agora));
        Assert.Equal(((double?)0, (double?)null), ProgressoGeral.Calcular(p with { Etapa = null, EtapasConcluidas = 0 }, null, Agora));
        Assert.Equal(((double?)null, (double?)null), ProgressoGeral.Calcular(new Progresso(null, 0, 0, []), null, Agora));
    }
}
