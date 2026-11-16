using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace Pmvni.App;

/// <summary>Pílulas de ligar e desligar como as do WinStatusBar (correções 2026-11-15): os estados nativos do ToggleButton/AppBarToggleButton,
/// cantos inteiramente arredondados e, quando ligado, o fundo na cor de destaque do Windows (o botão "acende"). Usadas onde o botão
/// mostra um estado: filtros do painel de seções, ferramentas do editor de figuras e os botões de alternar da faixa.</summary>
public static class Pilulas
{
    /// <summary>ToggleButton comum (filtros, ferramentas): 32 px de altura, raio 16, ligado = destaque do sistema (padrão do WinUI).</summary>
    public static void Estilizar(ToggleButton b)
    {
        b.ClearValue(Control.BackgroundProperty);
        b.ClearValue(Control.ForegroundProperty);
        b.ClearValue(Control.BorderBrushProperty);
        b.Height = 32;
        b.MinHeight = 0;
        b.MinWidth = 0;
        b.Padding = new Thickness(11, 0, 12, 0);
        b.CornerRadius = new CornerRadius(16);
        b.VerticalAlignment = VerticalAlignment.Center;
    }

    /// <summary>Botão de alternar da faixa: ligado acende na cor de destaque (o AppBarToggleButton nativo só muda um pouco o fundo).</summary>
    public static void Estilizar(AppBarToggleButton b)
    {
        b.CornerRadius = new CornerRadius(16);
        var r = b.Resources;
        r["AppBarToggleButtonBackgroundChecked"] = Recurso("AccentFillColorDefaultBrush");
        r["AppBarToggleButtonBackgroundCheckedPointerOver"] = Recurso("AccentFillColorSecondaryBrush");
        r["AppBarToggleButtonBackgroundCheckedPressed"] = Recurso("AccentFillColorTertiaryBrush");
        r["AppBarToggleButtonForegroundChecked"] = Recurso("TextOnAccentFillColorPrimaryBrush");
        r["AppBarToggleButtonForegroundCheckedPointerOver"] = Recurso("TextOnAccentFillColorPrimaryBrush");
        r["AppBarToggleButtonForegroundCheckedPressed"] = Recurso("TextOnAccentFillColorSecondaryBrush");
        r["AppBarToggleButtonBorderBrushChecked"] = Recurso("AccentFillColorDefaultBrush");
        r["AppBarToggleButtonBorderBrushCheckedPointerOver"] = Recurso("AccentFillColorSecondaryBrush");
        r["AppBarToggleButtonBorderBrushCheckedPressed"] = Recurso("AccentFillColorTertiaryBrush");
    }

    static object Recurso(string chave) => Application.Current.Resources.TryGetValue(chave, out var v) ? v : DependencyProperty.UnsetValue;
}
