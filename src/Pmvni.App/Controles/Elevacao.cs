using Microsoft.UI.Xaml;

namespace Pmvni.App;

/// <summary>Permite definir em um estilo a componente Z de Translation, preservando a posição no plano.</summary>
public static class Elevacao
{
    public static readonly DependencyProperty AlturaProperty = DependencyProperty.RegisterAttached(
        "Altura", typeof(double), typeof(Elevacao), new PropertyMetadata(0d, AoMudarAltura));

    public static double GetAltura(DependencyObject elemento) => (double)elemento.GetValue(AlturaProperty);
    public static void SetAltura(DependencyObject elemento, double valor) => elemento.SetValue(AlturaProperty, valor);

    static void AoMudarAltura(DependencyObject elemento, DependencyPropertyChangedEventArgs args)
    {
        if (elemento is UIElement superficie)
        {
            var posicao = superficie.Translation;
            posicao.Z = (float)(double)args.NewValue;
            superficie.Translation = posicao;
        }
    }
}
