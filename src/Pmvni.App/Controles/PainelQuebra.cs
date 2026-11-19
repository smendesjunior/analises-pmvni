using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Pmvni.App;

/// <summary>Painel que põe os filhos lado a lado e quebra para a linha de baixo quando não cabem (os botões do filtro rápido do painel de
/// seções, correções 2026-11-15: antes eram cortados na borda do painel).</summary>
public sealed partial class PainelQuebra : Panel
{
    public double Espaco { get; set; } = 6;
    public double EspacoLinhas { get; set; } = 6;

    protected override Size MeasureOverride(Size disponivel)
    {
        double x = 0, y = 0, alturaLinha = 0, larguraMax = 0;
        foreach (var filho in Children)
        {
            filho.Measure(new Size(disponivel.Width, double.PositiveInfinity));
            var d = filho.DesiredSize;
            if (x > 0 && x + d.Width > disponivel.Width + 0.5)
            {
                y += alturaLinha + EspacoLinhas;
                x = 0;
                alturaLinha = 0;
            }
            x += d.Width + Espaco;
            alturaLinha = Math.Max(alturaLinha, d.Height);
            larguraMax = Math.Max(larguraMax, x - Espaco);
        }
        var largura = Math.Min(Math.Ceiling(larguraMax), disponivel.Width);
        return new Size(largura, Children.Count == 0 ? 0 : y + alturaLinha);
    }

    protected override Size ArrangeOverride(Size final)
    {
        double x = 0, y = 0, alturaLinha = 0;
        foreach (var filho in Children)
        {
            var d = filho.DesiredSize;
            if (x > 0 && x + d.Width > final.Width + 0.5)
            {
                y += alturaLinha + EspacoLinhas;
                x = 0;
                alturaLinha = 0;
            }
            filho.Arrange(new Rect(x, y, d.Width, d.Height));
            x += d.Width + Espaco;
            alturaLinha = Math.Max(alturaLinha, d.Height);
        }
        return final;
    }
}
