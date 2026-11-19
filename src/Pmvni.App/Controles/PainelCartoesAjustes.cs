using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Pmvni.App;

/// <summary>Distribui os cartões de Ajustes na coluna com espaço disponível mais perto do topo.</summary>
public sealed partial class PainelCartoesAjustes : Panel
{
    const double Espaco = 10;

    protected override Size MeasureOverride(Size disponivel) => Distribuir(disponivel.Width, medir: true);

    protected override Size ArrangeOverride(Size final)
    {
        Distribuir(final.Width, medir: false);
        return final;
    }

    Size Distribuir(double largura, bool medir)
    {
        bool duasColunas = double.IsFinite(largura) && largura >= 820;
        double larguraCartao = duasColunas ? (largura - Espaco) / 2 : largura;
        double esquerda = 0, direita = 0, maiorLargura = 0;
        foreach (var cartao in Children)
        {
            if (cartao.Visibility == Visibility.Collapsed) continue;
            if (medir) cartao.Measure(new Size(larguraCartao, double.PositiveInfinity));
            bool usarDireita = duasColunas && direita < esquerda;
            double topo = usarDireita ? direita : esquerda;
            double altura = cartao.DesiredSize.Height;
            if (!medir)
                cartao.Arrange(new Rect(usarDireita ? larguraCartao + Espaco : 0, topo, larguraCartao, altura));
            double proximo = topo + altura + Espaco;
            if (usarDireita) direita = proximo;
            else esquerda = proximo;
            maiorLargura = Math.Max(maiorLargura, cartao.DesiredSize.Width);
        }
        return new Size(double.IsFinite(largura) ? largura : maiorLargura, Math.Max(0, Math.Max(esquerda, direita) - Espaco));
    }
}
