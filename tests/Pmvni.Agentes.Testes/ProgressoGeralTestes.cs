using Xunit;

namespace Pmvni.Agentes.Testes;

public class ProgressoGeralTestes
{
    static readonly DateTimeOffset Agora = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    static void Perto((double Geral, double Etapa, bool Inicial) esperado, (double? Geral, double? Etapa, bool Inicial) real)
    {
        Assert.Equal(esperado.Geral, real.Geral!.Value, 9);
        Assert.Equal(esperado.Etapa, real.Etapa!.Value, 9);
        Assert.Equal(esperado.Inicial, real.Inicial);
    }

    [Fact]
    public void Analise_UsaCoberturaDoManifestoMesmoSemHistorico()
    {
        var p = new Progresso("analyze", 1, 14, [], Provedor.ChatGpt, Agora.AddHours(-1)) { Ordem = ["prepare", "analyze", "produce"] };

        // Durações iniciais do fluxo linear: preparo 3, análise 17, produção 2 (22 min); o preparo feito e metade da análise = 11,5 min.
        Perto((100d * 11.5 / 22, 50d, true), ProgressoGeral.Calcular(p, null, Agora));
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

        // Preparo pela mediana (4 min) e análise pela duração inicial (17 min): 21 min no total.
        Perto((100d * 2 / 21, 50d, true), ProgressoGeral.Calcular(p, historico, Agora));
        Perto((100d * 3.8 / 21, 95d, true), ProgressoGeral.Calcular(p, historico, Agora.AddMinutes(5)));
        Assert.Equal((0d, 0d, true), ProgressoGeral.Calcular(p, historico, Agora.AddMinutes(-3)));
        Assert.Equal((0d, 0d, true), ProgressoGeral.Calcular(p with { InicioEtapa = null }, historico, Agora));
        Perto((100d * 2 / 20, 100d * 2 / 3, true), ProgressoGeral.Calcular(p, null, Agora));
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
        // Sem a amostra do preparo, ele pesa a duração inicial (3 min): 3 + 25% de 12 = 6 de 21 min.
        Perto((100d * 6 / 21, 25d, true), ProgressoGeral.Calcular(p, historico[..^1], Agora));
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
