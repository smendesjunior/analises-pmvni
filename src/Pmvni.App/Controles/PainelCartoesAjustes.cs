using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Pmvni.App;

/// <summary>Um cartão por linha, na largura disponível da categoria.</summary>
public sealed partial class PainelCartoesAjustes : StackPanel
{
    public PainelCartoesAjustes() => Spacing = 10;
}

/// <summary>Alinha o texto e o grupo de ações. Quando falta espaço, o grupo desce inteiro.</summary>
public sealed partial class LinhaComAcoes : Panel
{
    const double Espaco = 8;
    const double LarguraMinimaDoTexto = 240;
    bool empilhada;

    protected override Size MeasureOverride(Size disponivel)
    {
        if (Children.Count != 2) return default;
        var texto = Children[0];
        var acoes = Children[1];
        if (acoes is StackPanel grupo)
        {
            double larguraNatural = 0;
            int visiveis = 0;
            foreach (var item in grupo.Children)
            {
                if (item.Visibility == Visibility.Collapsed) continue;
                item.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                larguraNatural += item.DesiredSize.Width;
                visiveis++;
            }
            grupo.Orientation = larguraNatural + Math.Max(0, visiveis - 1) * grupo.Spacing > disponivel.Width
                ? Orientation.Vertical : Orientation.Horizontal;
        }
        acoes.Measure(disponivel);
        double larguraAcoes = acoes.DesiredSize.Width;
        empilhada = disponivel.Width < LarguraMinimaDoTexto + Espaco + larguraAcoes;
        texto.Measure(new Size(empilhada ? disponivel.Width : Math.Max(0, disponivel.Width - larguraAcoes - Espaco), disponivel.Height));
        var t = texto.DesiredSize;
        var a = acoes.DesiredSize;
        return empilhada ? new Size(Math.Max(t.Width, a.Width), t.Height + Espaco + a.Height)
            : new Size(t.Width + Espaco + a.Width, Math.Max(t.Height, a.Height));
    }

    protected override Size ArrangeOverride(Size final)
    {
        if (Children.Count != 2) return final;
        var texto = Children[0];
        var acoes = Children[1];
        double largura = Math.Min(acoes.DesiredSize.Width, final.Width);
        double altura = acoes.DesiredSize.Height;
        texto.Arrange(new Rect(0, 0, empilhada ? final.Width : Math.Max(0, final.Width - largura - Espaco),
            empilhada ? texto.DesiredSize.Height : final.Height));
        acoes.Arrange(new Rect(empilhada ? 0 : final.Width - largura,
            empilhada ? texto.DesiredSize.Height + Espaco : (final.Height - altura) / 2, largura, altura));
        return final;
    }
}
