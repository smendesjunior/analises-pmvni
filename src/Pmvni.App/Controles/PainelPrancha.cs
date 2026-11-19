using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

internal static class PainelPrancha
{
    internal static Grid Cartao()
        => new() { Style = (Style)Application.Current.Resources["PmvniPainelLateral"] };

    internal static Grid Quadro(FrameworkElement titulo, FrameworkElement conteudo, Symbol icone)
    {
        var cartao = Cartao();
        cartao.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cartao.RowDefinitions.Add(new RowDefinition());
        var faixa = new Grid { ColumnSpacing = 10, Padding = new Thickness(16, 12, 16, 12) };
        faixa.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        faixa.ColumnDefinitions.Add(new ColumnDefinition());
        faixa.Children.Add(new SymbolIcon(icone));
        Grid.SetColumn(titulo, 1);
        faixa.Children.Add(titulo);
        cartao.Children.Add(faixa);
        Grid.SetRow(conteudo, 1);
        cartao.Children.Add(conteudo);
        return cartao;
    }

    internal static TextBlock Titulo(string texto) => new()
    {
        Text = texto, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
    };

    internal static void Dimensionar(Window janela)
    {
        var area = DisplayArea.GetFromWindowId(janela.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var escala = (janela.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1;
        janela.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Min(1536 * escala, area.Width * .94),
            (int)Math.Min(960 * escala, area.Height * .94)));
    }
}
