using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>O JSON de <c>situacao</c> do <c>pro_runtime.py</c> (formato lido do pro_auto.py do kit), lido de forma tolerante.</summary>
public class SituacaoAnaliseProTestes
{
    [Fact]
    public void situacao_do_kit_lida()
    {
        var iniciada = SituacaoAnalisePro.Ler("{\"situacao\":\"INICIADA\",\"processo\":\"C:\\\\P\",\"aviso\":\"A análise roda sozinha\"}");
        Assert.Equal("INICIADA", iniciada.Codigo);
        Assert.Null(iniciada.Etapa);
        Assert.Null(iniciada.Pid);
        Assert.Null(iniciada.Inicio);
        Assert.Empty(iniciada.Eventos);

        var andamento = SituacaoAnalisePro.Ler(
            "{\"case\":\"C:\\\\P\",\"situacao\":\"EM_ANDAMENTO\",\"rotulo\":\"em andamento\",\"etapa\":\"analyze\",\"pid\":4321,\"inicio\":\"2026-11-15T10:20:30\","
            + "\"eventos\":[{\"quando\":\"2026-11-15T10:20:31\",\"texto\":\"Etapa prepare\"},{\"quando\":\"2026-11-15T10:21:00\",\"texto\":\"Etapa analyze\"},\"texto solto\"]}");
        Assert.Equal("EM_ANDAMENTO", andamento.Codigo);
        Assert.Equal("em andamento", andamento.Rotulo);
        Assert.Equal("analyze", andamento.Etapa);
        Assert.Equal(4321, andamento.Pid);
        Assert.Equal(new DateTime(2026, 11, 15, 10, 20, 30, DateTimeKind.Local), andamento.Inicio!.Value.LocalDateTime);
        Assert.Equal(new[] { "Etapa prepare", "Etapa analyze", "texto solto" }, andamento.Eventos);
        Assert.Null(andamento.Erro);
        Assert.Null(andamento.Pdf);

        var concluido = SituacaoAnalisePro.Ler("{\"situacao\":\"CONCLUIDO\",\"rotulo\":\"concluída\",\"pdf\":\"C:\\\\P\\\\relatorio.pdf\",\"inicio\":\"2026-11-15T10:00:00-03:00\"}");
        Assert.Equal("CONCLUIDO", concluido.Codigo);
        Assert.Equal(@"C:\P\relatorio.pdf", concluido.Pdf);
        Assert.Equal(TimeSpan.FromHours(-3), concluido.Inicio!.Value.Offset);

        var cota = SituacaoAnalisePro.Ler("{\"situacao\":\"PAUSADO_COTA\",\"rotulo\":\"pausada: cota do Claude esgotada\",\"erro\":\"Cota do Claude esgotada.\"}");
        Assert.Equal("Cota do Claude esgotada.", cota.Erro);

        var ja = SituacaoAnalisePro.Ler("{\"situacao\":\"JA_EM_ANDAMENTO\",\"etapa\":\"audit\",\"resumo\":\"C:\\\\P\\\\RESUMO.md\"}");
        Assert.Equal("JA_EM_ANDAMENTO", ja.Codigo);
        Assert.Equal("audit", ja.Etapa);
    }

    [Theory]
    [InlineData("")]
    [InlineData("não é json")]
    [InlineData("[1,2]")]
    [InlineData("{}")]
    [InlineData("{\"situacao\":7,\"pid\":\"x\",\"inicio\":\"ontem\",\"eventos\":\"nenhum\",\"etapa\":[1]}")]
    public void chaves_faltando_ou_erradas_viram_null(string json)
    {
        var s = SituacaoAnalisePro.Ler(json);
        Assert.Equal("", s.Codigo);
        Assert.Null(s.Rotulo);
        Assert.Null(s.Etapa);
        Assert.Null(s.Erro);
        Assert.Null(s.Pdf);
        Assert.Null(s.Pid);
        Assert.Null(s.Inicio);
        Assert.Empty(s.Eventos);
    }

    [Theory]
    [InlineData("CONCLUIDO", true)]
    [InlineData("FALHOU", true)]
    [InlineData("OBJECOES", true)]
    [InlineData("PAUSADO_COTA", true)]
    [InlineData("INTERROMPIDA", true)]
    [InlineData("NAO_PARTIU", true)]
    [InlineData("CANCELADO", true)]
    [InlineData("EM_ANDAMENTO", false)]
    [InlineData("AGUARDANDO_COTA", false)]
    [InlineData("INICIANDO", false)]
    [InlineData("INICIADA", false)]
    [InlineData("NAO_INICIADA", false)]
    [InlineData("JA_EM_ANDAMENTO", false)]
    [InlineData("", false)]
    public void terminal_certo(string codigo, bool esperado) => Assert.Equal(esperado, SituacaoAnalisePro.Terminal(codigo));

    [Fact]
    public void etapas_e_nomes()
    {
        Assert.Equal(new[] { "prepare", "analyze", "technical_audit", "produce", "audit", "final_review" }, SituacaoAnalisePro.Etapas);
        Assert.Equal(new[] { "Preparo", "Análise", "Auditoria técnica", "Produção do relatório", "Auditoria cruzada", "Revisão final" },
            SituacaoAnalisePro.Etapas.Select(SituacaoAnalisePro.NomeEtapa));
        Assert.Equal("etapa_nova", SituacaoAnalisePro.NomeEtapa("etapa_nova"));
    }
}
