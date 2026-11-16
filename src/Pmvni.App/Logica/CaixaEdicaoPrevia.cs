namespace Pmvni.App;

/// <summary>As medidas da caixa flutuante de edição de um trecho da prévia do despacho, em DIP: a largura do conteúdo, a largura máxima do
/// cartão (o conteúdo mais a moldura) e a altura mínima e máxima da caixa de texto.</summary>
public readonly record struct MedidasCaixaEdicao(double LarguraConteudo, double LarguraCartao, double AlturaMinima, double AlturaMaxima);

/// <summary>Lógica da caixa de edição da prévia sem tipos WinUI (compilada também nos testes). O cartão de um <c>Flyout</c> tem largura máxima
/// própria (<c>FlyoutThemeMaxWidth</c>, 456 DIP) e uma rolagem interna: um conteúdo mais largo que ele sai cortado à direita, com uma barra
/// de rolagem horizontal embaixo (defeito de 2026-11-16). Por isso o conteúdo e o cartão recebem daqui medidas que combinam, e que cabem
/// na janela.</summary>
public static class CaixaEdicaoPrevia
{
    /// <summary>Largura preferida do conteúdo: dá para uma exigência de umas três linhas sem ficar maior que a folha.</summary>
    public const double LarguraPreferida = 480;

    /// <summary>O que o cartão soma ao conteúdo na largura: o preenchimento do <c>FlyoutPresenter</c> (16 de cada lado) e a borda (1 de cada lado).</summary>
    public const double MolduraCartao = 34;

    /// <summary>Distância mínima entre o cartão e a borda da janela, de cada lado.</summary>
    public const double FolgaJanela = 16;

    /// <summary>Altura de uma caixa de texto de uma linha (o padrão do WinUI): um trecho curto não ganha espaço em branco.</summary>
    public const double AlturaUmaLinha = 32;

    public const double LarguraMinima = 200;
    public const double AlturaMaximaPreferida = 240;

    /// <summary>A caixa cabe na janela (<paramref name="larguraJanela"/> × <paramref name="alturaJanela"/>): largura preferida, ou a que
    /// sobra entre as folgas; altura da caixa de texto de uma linha até 240 DIP ou 40 % da janela (a partir daí, rolagem vertical).
    /// Janela sem medida (0, negativa ou NaN): as medidas preferidas.</summary>
    public static MedidasCaixaEdicao Medir(double larguraJanela, double alturaJanela)
    {
        var conteudo = LarguraPreferida;
        if (double.IsFinite(larguraJanela) && larguraJanela > 0)
            conteudo = Math.Clamp(larguraJanela - 2 * FolgaJanela - MolduraCartao, LarguraMinima, LarguraPreferida);
        var maxima = AlturaMaximaPreferida;
        if (double.IsFinite(alturaJanela) && alturaJanela > 0)
            maxima = Math.Clamp(alturaJanela * 0.4, AlturaUmaLinha * 2, AlturaMaximaPreferida);
        return new MedidasCaixaEdicao(conteudo, conteudo + MolduraCartao, AlturaUmaLinha, maxima);
    }
}
