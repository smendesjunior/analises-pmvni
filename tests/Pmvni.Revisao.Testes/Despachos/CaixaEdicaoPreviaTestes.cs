using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

/// <summary>A caixa flutuante de edição da prévia (defeito de 2026-11-16: conteúdo de 640 DIP num cartão de 456 DIP, cortado à direita e com
/// rolagem horizontal; caixa de texto alta demais para uma linha).</summary>
public sealed class CaixaEdicaoPreviaTestes
{
    /// <summary>Largura máxima padrão do cartão de um Flyout do WinUI (<c>FlyoutThemeMaxWidth</c>).</summary>
    const double CartaoPadraoWinUi = 456;

    [Theory]
    [InlineData(1265, 701)]
    [InlineData(1920, 1040)]
    [InlineData(700, 500)]
    [InlineData(420, 300)]
    [InlineData(200, 120)]
    public void o_conteudo_cabe_no_cartao_e_o_cartao_na_janela(double larguraJanela, double alturaJanela)
    {
        var m = CaixaEdicaoPrevia.Medir(larguraJanela, alturaJanela);

        // O cartão é feito sob medida para o conteúdo: nada é cortado, nada rola na horizontal.
        Assert.Equal(m.LarguraConteudo + CaixaEdicaoPrevia.MolduraCartao, m.LarguraCartao);
        Assert.True(m.LarguraConteudo <= CaixaEdicaoPrevia.LarguraPreferida);
        Assert.True(m.LarguraConteudo >= CaixaEdicaoPrevia.LarguraMinima);
        // Dentro da janela, com folga dos dois lados (uma janela menor que a largura mínima é o único caso em que não dá).
        if (larguraJanela >= CaixaEdicaoPrevia.LarguraMinima + CaixaEdicaoPrevia.MolduraCartao + 2 * CaixaEdicaoPrevia.FolgaJanela)
            Assert.True(m.LarguraCartao <= larguraJanela - 2 * CaixaEdicaoPrevia.FolgaJanela);
        // Altura conforme o conteúdo: de uma linha até o máximo, que nunca passa de 40 % da janela (salvo o piso de duas linhas).
        Assert.Equal(CaixaEdicaoPrevia.AlturaUmaLinha, m.AlturaMinima);
        Assert.True(m.AlturaMaxima >= 2 * CaixaEdicaoPrevia.AlturaUmaLinha);
        Assert.True(m.AlturaMaxima <= CaixaEdicaoPrevia.AlturaMaximaPreferida);
        Assert.True(m.AlturaMaxima <= Math.Max(alturaJanela * 0.4, 2 * CaixaEdicaoPrevia.AlturaUmaLinha));
    }

    [Fact]
    public void janela_larga_usa_a_largura_preferida_e_nao_a_do_cartao_padrao()
    {
        var m = CaixaEdicaoPrevia.Medir(1265, 701);
        Assert.Equal(CaixaEdicaoPrevia.LarguraPreferida, m.LarguraConteudo);
        // O cartão passa do padrão do WinUI: por isso a página define a largura máxima do cartão junto com a do conteúdo.
        Assert.True(m.LarguraCartao > CartaoPadraoWinUi);
        Assert.Equal(240, m.AlturaMaxima);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(double.NaN, double.NaN)]
    [InlineData(-1, double.PositiveInfinity)]
    public void janela_sem_medida_usa_as_medidas_preferidas(double l, double a)
    {
        var m = CaixaEdicaoPrevia.Medir(l, a);
        Assert.Equal(CaixaEdicaoPrevia.LarguraPreferida, m.LarguraConteudo);
        Assert.Equal(CaixaEdicaoPrevia.AlturaMaximaPreferida, m.AlturaMaxima);
    }

    /// <summary>A página usa as medidas daqui e desliga a rolagem horizontal do cartão (o defeito voltaria com uma largura fixa no código).</summary>
    [Fact]
    public void a_pagina_usa_estas_medidas_e_desliga_a_rolagem_horizontal()
    {
        var fonte = File.ReadAllText(Path.Combine(Ambiente.PastaTestes, "..", "src", "Pmvni.App", "Paginas", "PaginaNovoDespacho.Folhas.cs"));
        Assert.Contains("CaixaEdicaoPrevia.Medir(", fonte, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled", fonte, StringComparison.Ordinal);
        Assert.Contains("FrameworkElement.MaxWidthProperty, m.LarguraCartao", fonte, StringComparison.Ordinal);
        Assert.DoesNotContain("Width = 640", fonte, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight = 160", fonte, StringComparison.Ordinal);
    }
}
