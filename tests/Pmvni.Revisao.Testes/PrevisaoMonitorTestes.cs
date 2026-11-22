using Pmvni.Agentes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Tarefa 13, correção 1 (M-1): a previsão do Monitor é do Editor; sem nada do kit ainda, o texto não fala em IA. Só a estimativa antiga
/// (<c>estimativa.json</c> de uma análise iniciada por um Editor anterior) continua com os textos "…estimativa da IA".</summary>
public class PrevisaoMonitorTestes
{
    static readonly DateTimeOffset Agora = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Sem_dados_do_kit_a_previsao_espera_as_primeiras_etapas_sem_falar_em_IA()
    {
        var painel = AchadosExecucao.DoProgresso(new Progresso(null, 0, 0, []), historico: null, Agora);
        Assert.Equal(new EstimativaAnalise(null, null, null, null, PeloEditor: true, SemDados: true), painel.Estimativa);

        var texto = FluxoNovoRelatorio.TextoEstimativa(null, Agora, peloEditor: true, semDados: true);
        Assert.Equal("Previsão depois das primeiras etapas", texto);
        Assert.Equal(FluxoNovoRelatorio.PrevisaoDepoisDasEtapas, texto);
        Assert.DoesNotContain("IA", texto, StringComparison.Ordinal);

        // O manifesto já informa o progresso, mesmo sem nenhum histórico.
        var comDados = AchadosExecucao.DoProgresso(new Progresso("analyze", 1, 4, [], Provedor.ChatGpt, Agora.AddMinutes(-3)), [], Agora);
        Assert.Equal(new EstimativaAnalise("analyze", 14.3, 19, null, PeloEditor: true), comDados.Estimativa);
        Assert.Equal(FluxoNovoRelatorio.HistoricoInsuficiente, FluxoNovoRelatorio.TextoEstimativa(null, Agora, peloEditor: true));
        Assert.Equal("Histórico insuficiente", FluxoNovoRelatorio.PrevisaoCurta(null, Agora, peloEditor: true));
        Assert.Equal("Após a 1ª etapa", FluxoNovoRelatorio.PrevisaoCurta(null, Agora, peloEditor: true, semDados: true));
        Assert.DoesNotContain("IA", FluxoNovoRelatorio.HistoricoInsuficiente, StringComparison.Ordinal);

        // O caminho antigo não muda.
        Assert.Equal("Aguardando estimativa da IA", FluxoNovoRelatorio.TextoEstimativa(null, Agora));
    }

    [Fact]
    public void Previsao_do_Editor_arredondada_ao_minuto_e_secoes_so_na_analise()
    {
        var historico = new[] { "technical_audit", "produce", "audit", "final_review" }
            .SelectMany(e => Enumerable.Range(0, 3).Select(_ => new DuracaoEtapa(e, Provedor.Claude, TimeSpan.FromSeconds(90)))).ToList();
        var painel = AchadosExecucao.DoProgresso(new Progresso("technical_audit", 2, 28, [], Provedor.Claude, Agora.AddSeconds(-10)), historico, Agora);
        // 80 s + 3 × 90 s = 350 s → 12:05:50, arredondado para 12:06.
        Assert.Equal(new EstimativaAnalise("technical_audit", 11.1, 35.2, Agora.AddMinutes(6), PeloEditor: true), painel.Estimativa);
        Assert.Empty(painel.Etapas);
        var analise = AchadosExecucao.DoProgresso(new Progresso("analyze", 1, 9, [], Provedor.Claude), historico, Agora);
        Assert.Equal(new ProgressoEtapa("analyze", 9, 9, 28, Secoes: true), analise.Etapas["analyze"]);
    }

    [Fact]
    public void DaPrioridadeAPrevisaoFirmeEMarcaAproximadaQuandoSoHaHistoricoCurto()
    {
        var progresso = new Progresso("produce", 1, 28, [], Provedor.ChatGpt, Agora) { Ordem = ["analyze", "produce", "final_review"] };
        DuracaoEtapa[] historico =
        [
            new("produce", Provedor.ChatGpt, TimeSpan.FromSeconds(15)),
            new("final_review", Provedor.Claude, TimeSpan.FromSeconds(90)),
        ];
        var aproximada = AchadosExecucao.DoProgresso(progresso, historico, Agora);
        Assert.Equal(new EstimativaAnalise("produce", 0, 33.3, Agora.AddMinutes(2), PeloEditor: true, Aproximada: true), aproximada.Estimativa);

        var firme = Enumerable.Repeat(historico[0], 3)
            .Concat(Enumerable.Repeat(new DuracaoEtapa("final_review", Provedor.ChatGpt, TimeSpan.FromMinutes(3)), 3)).ToList();
        var painel = AchadosExecucao.DoProgresso(progresso, firme, Agora);
        Assert.Equal(new EstimativaAnalise("produce", 0, 33.3, Agora.AddMinutes(4), PeloEditor: true), painel.Estimativa);
    }

    [Fact]
    public void HistoricoCarregando_EntregaPercentuaisMesmoSemPrevisao()
    {
        var p = new Progresso("analyze", 1, 9, [], Provedor.ChatGpt);
        var painel = AchadosExecucao.DoProgresso(p, historico: null, Agora);

        Assert.Equal(new EstimativaAnalise("analyze", 32.1, 22, null, PeloEditor: true), painel.Estimativa);
    }

    [Theory]
    [InlineData(false, "Término estimado", "12:15")]
    [InlineData(true, "Término aproximado", "≈ 12:15")]
    public void TextosDaPrevisaoDoEditor_IdentificamSeEHistoricoCurto(bool aproximada, string rotulo, string horaCurta)
    {
        var termino = Agora.AddMinutes(15);
        var ritmo = TimeSpan.FromMinutes(10);

        Assert.Equal($"{rotulo} · 12:15 (15 min restantes)", FluxoNovoRelatorio.TextoEstimativa(termino, Agora, ritmo, peloEditor: true, aproximada: aproximada));
        Assert.Equal($"{horaCurta} · 15 min", FluxoNovoRelatorio.PrevisaoCurta(termino, Agora, ritmo, peloEditor: true, aproximada: aproximada));
    }

    [Fact]
    public void TextosDaPrevisao_UsamORitmoQuandoOHorarioDoHistoricoPassou()
    {
        var termino = Agora.AddMinutes(-1);
        var ritmo = TimeSpan.FromMinutes(10);
        Assert.Equal("Término aproximado pelo ritmo · 12:10 (10 min restantes)", FluxoNovoRelatorio.TextoEstimativa(termino, Agora, ritmo, peloEditor: true, aproximada: true));
        Assert.Equal("≈ 12:10 · 10 min", FluxoNovoRelatorio.PrevisaoCurta(termino, Agora, ritmo, peloEditor: true, aproximada: true));
        Assert.Equal("Término previsto para os próximos minutos", FluxoNovoRelatorio.TextoEstimativa(termino, Agora, peloEditor: true, aproximada: true));
        Assert.Equal("Nos próximos minutos", FluxoNovoRelatorio.PrevisaoCurta(termino, Agora, peloEditor: true, aproximada: true));
    }
}
