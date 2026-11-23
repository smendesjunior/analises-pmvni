using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class AtividadesTestes
{
    [Theory]
    [InlineData("Ação: listando os arquivos do processo", "Documentos do processo identificados")]
    [InlineData("Ação: lendo as instruções da análise", "Instruções de análise carregadas")]
    [InlineData("Ação: lendo o texto extraído dos documentos", "Conteúdo dos documentos lido")]
    [InlineData("Ação: inspecionando as pranchas", "Conteúdo das pranchas examinado")]
    [InlineData("Ação: consultando a legislação (LC-136-2016)", "Legislação municipal consultada")]
    [InlineData("Ação: gerando o mapa de localização", "Mapa de localização preparado")]
    [InlineData("Ação: organizando as fontes documentais", "Fontes documentais organizadas")]
    [InlineData("Ação: gravou 1 arquivo de trabalho", "Arquivo de trabalho salvo")]
    [InlineData("Ação: gravou 3 arquivos de trabalho", "3 arquivos de trabalho salvos")]
    public void Acoes_viram_frases_naturais(string linha, string titulo)
    {
        var a = Atividades.DaLinha(linha);
        Assert.Equal(TipoAtividade.Acao, a.Tipo);
        Assert.Equal(titulo, a.Titulo);
    }

    [Fact]
    public void Legislacao_guarda_a_lei_no_detalhe() =>
        Assert.Equal("LC-136-2016", Atividades.DaLinha("Ação: consultando a legislação (LC-136-2016)").Detalhe);

    [Fact]
    public void Erro_e_mensagem()
    {
        Assert.Equal(TipoAtividade.Erro, Atividades.DaLinha("Erro: cota esgotada").Tipo);
        var m = Atividades.DaLinha("Mensagem: dois pavimentos comerciais");
        Assert.Equal(TipoAtividade.Mensagem, m.Tipo);
        Assert.Equal("Dois pavimentos comerciais", m.Detalhe);
    }

    [Fact]
    public void Descobertas_sao_as_ultimas_mensagens_mais_recente_primeiro()
    {
        string[] linhas = ["Mensagem: a", "Ação: x", "Mensagem: b", "Mensagem: c", "Mensagem: d", "Mensagem: e"];
        var d = Atividades.Descobertas(linhas, verificadas: false);
        Assert.Equal(["E", "D", "C", "B"], d.Select(x => x.Texto));
        Assert.All(d, x => Assert.False(x.Verificada));
    }

    [Theory]
    [InlineData("etapa prepare", "Preparação dos documentos")]
    [InlineData("etapa analyze", "Análise do projeto")]
    [InlineData("etapa technical_audit", "Conferência técnica")]
    [InlineData("Etapa produce: modelo nível medium", "Elaboração do relatório")]
    [InlineData("etapa audit", "Conferência do relatório")]
    [InlineData("pausa na etapa final_review", "Revisão final")]
    [InlineData("fim: CONCLUIDO", "Análise concluída")]
    [InlineData("análise iniciada", "Análise iniciada")]
    public void Eventos_do_kit_usam_titulo_legivel_e_horario_do_evento(string evento, string titulo)
    {
        var a = Atividades.DaLinha("[2026-11-18T19:59:35] " + evento, DateTime.Today);
        Assert.Equal(titulo, a.Titulo);
        Assert.Equal("19:59:35", a.HoraTexto);
    }

    [Fact]
    public void Horario_so_quando_disponivel()
    {
        Assert.Equal("", Atividades.DaLinha("Ação: x").HoraTexto);
        Assert.Equal("14:36:00", Atividades.DaLinha("Ação: x", new DateTime(2026, 11, 17, 14, 36, 0)).HoraTexto);
    }
}
