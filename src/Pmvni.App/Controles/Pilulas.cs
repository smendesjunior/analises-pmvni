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

    /// <summary>Botão de alternar da faixa: ligado acende na cor de destaque (o AppBarToggleButton nativo só muda um pouco o fundo). As cores
    /// seguem o tema do botão (claro ou escuro do app), não o do Windows, e são refeitas quando o tema muda (auditoria 2026-11-17).</summary>
    public static void Estilizar(AppBarToggleButton b)
    {
        b.CornerRadius = new CornerRadius(16);
        Aplicar(b);
        b.ActualThemeChanged += (_, _) => Aplicar(b);
    }

    /// <summary>O verde institucional de App.xaml (SystemAccentColor*): no claro, o Dark1; no escuro, o Light2, como o WinUI faz com o destaque.</summary>
    static void Aplicar(AppBarToggleButton b)
    {
        var escuro = b.ActualTheme == ElementTheme.Dark;
        var fundo = escuro ? Cor(0x5C, 0xB9, 0x83) : Cor(0x17, 0x66, 0x3A);
        var texto = escuro ? Cor(0x00, 0x00, 0x00) : Cor(0xFF, 0xFF, 0xFF);
        var r = b.Resources;
        r["AppBarToggleButtonBackgroundChecked"] = Pincel(fundo, 1);
        r["AppBarToggleButtonBackgroundCheckedPointerOver"] = Pincel(fundo, .9);
        r["AppBarToggleButtonBackgroundCheckedPressed"] = Pincel(fundo, .8);
        r["AppBarToggleButtonForegroundChecked"] = Pincel(texto, 1);
        r["AppBarToggleButtonForegroundCheckedPointerOver"] = Pincel(texto, 1);
        r["AppBarToggleButtonForegroundCheckedPressed"] = Pincel(texto, .8);
        r["AppBarToggleButtonBorderBrushChecked"] = Pincel(fundo, 1);
        r["AppBarToggleButtonBorderBrushCheckedPointerOver"] = Pincel(fundo, .9);
        r["AppBarToggleButtonBorderBrushCheckedPressed"] = Pincel(fundo, .8);
    }

    static Windows.UI.Color Cor(byte r, byte g, byte b) => Windows.UI.Color.FromArgb(255, r, g, b);
    static Microsoft.UI.Xaml.Media.SolidColorBrush Pincel(Windows.UI.Color c, double opacidade) => new(c) { Opacity = opacidade };}
