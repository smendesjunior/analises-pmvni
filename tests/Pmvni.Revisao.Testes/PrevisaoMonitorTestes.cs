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

        // Com etapas registradas, mas sem 3 execuções anteriores de cada etapa que falta.
        var comDados = AchadosExecucao.DoProgresso(new Progresso("analyze", 1, 4, [], Provedor.ChatGpt, Agora.AddMinutes(-3)), [], Agora);
        Assert.Equal(new EstimativaAnalise("analyze", null, null, null, PeloEditor: true), comDados.Estimativa);
        Assert.Equal(FluxoNovoRelatorio.HistoricoInsuficiente, FluxoNovoRelatorio.TextoEstimativa(null, Agora, peloEditor: true));
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
        Assert.Equal(Agora.AddMinutes(6), painel.Estimativa!.Termino);
        Assert.Empty(painel.Etapas);
        var analise = AchadosExecucao.DoProgresso(new Progresso("analyze", 1, 9, [], Provedor.Claude), historico, Agora);
        Assert.Equal(new ProgressoEtapa("analyze", 9, 9, 28, Secoes: true), analise.Etapas["analyze"]);
    }
}
