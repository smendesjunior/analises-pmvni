using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>O JSON da última linha do <c>executar_analise_62.py</c> (contrato da Tarefa 2), lido de forma tolerante, e a rota do Codex resolvida pelo editor.</summary>
public class SituacaoAnalise62Testes
{
    [Fact]
    public void contrato_lido()
    {
        var entregue = SituacaoAnalise62.Ler("{\"situacao\":\"ENTREGUE\",\"etapa\":\"final_review\",\"retomar_em_utc\":null,\"motivo\":\"\",\"decisao\":null}");
        Assert.Equal(("ENTREGUE", "final_review", null, null, null), (entregue.Situacao, entregue.Etapa, entregue.RetomarEmUtc, entregue.Motivo, entregue.Decisao));

        var cota = SituacaoAnalise62.Ler("{\"situacao\":\"AGUARDANDO_COTA\",\"etapa\":\"analyze\",\"retomar_em_utc\":\"2026-11-21T18:40:00Z\",\"motivo\":\"Try again in 300s\"}");
        Assert.Equal(SituacaoAnalise62.AguardandoCota, cota.Situacao);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 18, 40, 0, TimeSpan.Zero), cota.RetomarEmUtc);
        Assert.Equal("Try again in 300s", cota.Motivo);
        Assert.Equal("Aguardando cota até " + cota.RetomarEmUtc!.Value.ToLocalTime().ToString("HH:mm"), SituacaoAnalise62.TextoAguardandoCota(cota.RetomarEmUtc.Value));

        var decisao = SituacaoAnalise62.Ler("{\"situacao\":\"DECISAO\",\"etapa\":\"prepare\",\"decisao\":{\"tipo\":\"pecas\",\"texto\":\"Qual A01 vale?\","
            + "\"opcoes\":[\"a.pdf\",{\"id\":\"b\",\"rotulo\":\"b.pdf (REVISÃO 02)\"},7],\"resposta_em\":\"pecas_escolhidas.json\"}}");
        Assert.Equal(SituacaoAnalise62.PedeDecisao, decisao.Situacao);
        var d = decisao.Decisao!;
        Assert.Equal(("pecas", "Qual A01 vale?", "pecas_escolhidas.json"), (d.Tipo, d.Pergunta, d.ArquivoResposta));
        Assert.Equal(new[] { ("a.pdf", "a.pdf"), ("b", "b.pdf (REVISÃO 02)") }, d.Opcoes.Select(o => (o.Valor, o.Texto)));
        Assert.Contains("\"tipo\":\"pecas\"", d.Json);

        var falha = SituacaoAnalise62.Ler("{\"situacao\":\"FALHA\",\"etapa\":\"produce\",\"motivo\":\"O Edge não gerou o PDF.\"}");
        Assert.Equal(("FALHA", "O Edge não gerou o PDF."), (falha.Situacao, falha.Motivo));
    }

    [Theory]
    [InlineData("")]
    [InlineData("ANALISE_62_INTERROMPIDA: Cota indisponível.")]
    [InlineData("[1,2]")]
    [InlineData("{\"situacao\":7,\"retomar_em_utc\":\"ontem\",\"decisao\":\"sim\"}")]
    public void linha_ilegivel_vira_vazia(string linha)
    {
        var s = SituacaoAnalise62.Ler(linha);
        Assert.Equal("", s.Situacao);
        Assert.Null(s.RetomarEmUtc);
        Assert.Null(s.Decisao);
    }

    [Fact]
    public void ultima_linha_json_da_saida()
    {
        Assert.Equal("ETAPA_OK", SituacaoAnalise62.DaSaida(["analyze: gpt-6.2-astra / high", "{\"situacao\":\"ETAPA_OK\",\"etapa\":\"analyze\"}", ""])?.Situacao);
        // O controlador antigo imprimia o JSON com recuo (várias linhas): também é lido.
        Assert.Equal("ENTREGUE", SituacaoAnalise62.DaSaida(["prepare: x / low", "{", "  \"situacao\": \"ENTREGUE\",", "  \"etapa\": \"gate\"", "}"])?.Situacao);
        Assert.Null(SituacaoAnalise62.DaSaida(["prepare: x / low", "ANALISE_62_INTERROMPIDA: falhou"]));
        Assert.Null(SituacaoAnalise62.DaSaida([]));
    }

    [Theory]
    [InlineData("model_provider = \"codex-lb\"\n", "codex-lb")]
    [InlineData("model_provider='openai'  # direto\n", "openai")]
    [InlineData("model = \"gpt\"\n[model_providers.codex-lb]\nmodel_provider = \"codex-lb\"\n", "openai")]   // só a chave do topo vale
    [InlineData("", "openai")]
    public void rota_herdada_do_config_toml(string toml, string esperada)
    {
        using var t = Ambiente.PastaDeTeste("rota-toml");
        File.WriteAllText(Path.Combine(t, "config.toml"), toml);
        var r = RotaCodex.Resolver(ModoAcesso.Conta, "herdar", t);
        Assert.Equal((esperada, null), (r.Rota, r.Recusa));
        Assert.Contains("config.toml", r.Origem);
    }

    [Fact]
    public void provedor_incompativel_e_recusado()
    {
        using var t = Ambiente.PastaDeTeste("rota-ruim");
        File.WriteAllText(Path.Combine(t, "config.toml"), "model_provider = \"ollama\"\n");
        var r = RotaCodex.Resolver(ModoAcesso.Conta, null, t);
        Assert.Null(r.Rota);
        Assert.Contains("\"ollama\"", r.Recusa);
        Assert.EndsWith("Nada foi iniciado.", r.Recusa);
        Assert.Equal(("codex-lb", "Ajustes"), (RotaCodex.Resolver(ModoAcesso.Conta, "codex-lb", t).Rota, RotaCodex.Resolver(ModoAcesso.Conta, "codex-lb", t).Origem[..7]));
    }
}
