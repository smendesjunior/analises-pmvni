using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

/// <summary>Apresenta a ação com ícone sem trocar o Content usado pelas dicas, pelos atalhos e pelos rótulos dinâmicos.</summary>
public static class IconeDeAcao
{
    public static readonly DependencyProperty GlifoProperty = DependencyProperty.RegisterAttached(
        "Glifo", typeof(string), typeof(IconeDeAcao), new PropertyMetadata(null, AoMudarGlifo));

    public static string GetGlifo(DependencyObject alvo) => (string)alvo.GetValue(GlifoProperty);
    public static void SetGlifo(DependencyObject alvo, string valor) => alvo.SetValue(GlifoProperty, valor);

    static void AoMudarGlifo(DependencyObject alvo, DependencyPropertyChangedEventArgs e)
    {
        if (alvo is Button botao)
            botao.ContentTemplate = (DataTemplate)Application.Current.Resources["PmvniRotuloComIcone"];
    }

    public static readonly DependencyProperty LerDoBotaoProperty = DependencyProperty.RegisterAttached(
        "LerDoBotao", typeof(bool), typeof(IconeDeAcao), new PropertyMetadata(false, AoConfigurarLeitura));

    public static bool GetLerDoBotao(DependencyObject alvo) => (bool)alvo.GetValue(LerDoBotaoProperty);
    public static void SetLerDoBotao(DependencyObject alvo, bool valor) => alvo.SetValue(LerDoBotaoProperty, valor);

    static void AoConfigurarLeitura(DependencyObject alvo, DependencyPropertyChangedEventArgs e)
    {
        if (alvo is not FontIcon icone) return;
        icone.Loaded -= LerGlifo;
        if (e.NewValue is true) icone.Loaded += LerGlifo;
    }

    static void LerGlifo(object sender, RoutedEventArgs e)
    {
        var icone = (FontIcon)sender;
        for (var pai = VisualTreeHelper.GetParent(icone); pai != null; pai = VisualTreeHelper.GetParent(pai))
            if (pai is Button botao)
            {
                icone.Glyph = GetGlifo(botao);
                return;
            }
    }
}
