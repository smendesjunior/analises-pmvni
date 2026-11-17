using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

/// <summary>O fecho do despacho (da data à matrícula) na prévia: inteiro na última folha do texto, ou inteiro numa folha nova (defeito de
/// 2026-11-16: a linha da assinatura numa folha e o nome na seguinte, por cima do cabeçalho).</summary>
public sealed class FechoDaPreviaTestes
{
    const double Util = 868; // a altura do corpo de uma folha A4 do modelo, em DIP (≈ 651 pt)

    [Fact]
    public void cabendo_fica_logo_depois_do_texto()
    {
        Assert.Equal(300, FechoDaPrevia.NaUltimaFolha(300, 260, Util));
        Assert.Equal(608, FechoDaPrevia.NaUltimaFolha(608, 260, Util)); // cabe justo
        Assert.Equal(608.4, FechoDaPrevia.NaUltimaFolha(608.4, 260, Util)); // dentro do arredondamento do layout
    }

    [Fact]
    public void nao_cabendo_vai_inteiro_para_a_folha_seguinte()
    {
        // Sobram 258 DIP e o fecho tem 260: nenhum pedaço dele fica nesta folha.
        Assert.Null(FechoDaPrevia.NaUltimaFolha(610, 260, Util));
        Assert.Null(FechoDaPrevia.NaUltimaFolha(Util, 260, Util));
        Assert.Null(FechoDaPrevia.NaUltimaFolha(Util + 50, 260, Util)); // folha cheia medida a mais
    }

    [Theory]
    [InlineData(0, 260)]
    [InlineData(-1, 260)]
    [InlineData(double.NaN, 260)]
    [InlineData(300, 0)]
    [InlineData(300, double.NaN)]
    [InlineData(300, double.PositiveInfinity)]
    public void medida_que_nao_serve_nunca_sobrepoe_o_fecho_ao_texto(double usado, double fecho) =>
        Assert.Null(FechoDaPrevia.NaUltimaFolha(usado, fecho, Util));

    /// <summary>A página desenha o corpo a partir do começo corrigido (abaixo da faixa do cabeçalho) e separa o fecho pela data.</summary>
    [Fact]
    public void a_pagina_usa_o_topo_corrigido_e_o_bloco_do_fecho()
    {
        var fonte = File.ReadAllText(Path.Combine(Ambiente.PastaTestes, "..", "src", "Pmvni.App", "Paginas", "PaginaNovoDespacho.Folhas.cs"));
        Assert.Contains("Canvas.SetTop(corpo, l.TopoDoCorpoPt * K)", fonte, StringComparison.Ordinal);
        Assert.DoesNotContain("l.MargemTopoPt * K", fonte, StringComparison.Ordinal);
        Assert.Contains("FechoDaPrevia.NaUltimaFolha(", fonte, StringComparison.Ordinal);
        Assert.Contains("p.Estilo == EstiloPrevia.Data", fonte, StringComparison.Ordinal);
    }
}
