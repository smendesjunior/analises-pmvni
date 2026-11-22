using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

/// <summary>Área de ações (botões) dentro de uma <see cref="InfoBar"/>: só ocupa espaço quando algum botão está à vista. Com todos os botões
/// escondidos, o painel e a margem dele deixavam o aviso mais alto e o texto fora do centro (3.2.2).</summary>
public static class AcoesDeAviso
{
    public static readonly DependencyProperty AutoOcultarProperty = DependencyProperty.RegisterAttached(
        "AutoOcultar", typeof(bool), typeof(AcoesDeAviso), new PropertyMetadata(false, AoMudar));

    public static bool GetAutoOcultar(DependencyObject o) => (bool)o.GetValue(AutoOcultarProperty);
    public static void SetAutoOcultar(DependencyObject o, bool valor) => o.SetValue(AutoOcultarProperty, valor);

    static void AoMudar(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not Panel painel || e.NewValue is not true) return;
        void Ajustar() => painel.Visibility = painel.Children.Any(c => c.Visibility == Visibility.Visible) ? Visibility.Visible : Visibility.Collapsed;
        painel.Loaded += (_, _) =>
        {
            foreach (var c in painel.Children) c.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Ajustar());
            Ajustar();
        };
    }
}
