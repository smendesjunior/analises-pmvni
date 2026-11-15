using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Legislação › Leitura padronizada (correções 2026-11-15): o texto da lei aberta, estruturado pelo processador interno
/// (<see cref="LeituraPadronizada"/>), com epígrafe, ementa, faixas de título e capítulo e o rótulo de cada artigo em destaque; o sumário à
/// esquerda leva ao bloco. "PDF original" volta ao leitor de PDF.</summary>
public sealed partial class MainWindow
{
    static readonly SolidColorBrush Tinta = new(Windows.UI.Color.FromArgb(255, 0x1B, 0x2A, 0x4A));
    static readonly SolidColorBrush Destaque = new(Windows.UI.Color.FromArgb(255, 0x1A, 0xA7, 0xE0));
    static readonly SolidColorBrush AzulTitulo = new(Windows.UI.Color.FromArgb(255, 0x0F, 0x5E, 0x96));
    static readonly SolidColorBrush FundoTitulo = new(Windows.UI.Color.FromArgb(255, 0xE8, 0xF2, 0xFA));
    static readonly SolidColorBrush FundoEmenta = new(Windows.UI.Color.FromArgb(255, 0xF2, 0xF5, 0xF8));
    static readonly SolidColorBrush SeloArtigo = new(Windows.UI.Color.FromArgb(255, 0x0F, 0x4C, 0x75));

    bool leituraPadronizadaLigada;
    readonly Dictionary<object, FrameworkElement> alvosSumario = [];

    void IniciarLeituraPadronizada()
    {
        P.ModoLeitura.SelectionChanged += (_, _) => AplicarModoLeitura();
        P.SumarioLei.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is ListViewItem { Tag: FrameworkElement alvo } || (e.ClickedItem is FrameworkElement f && alvosSumario.TryGetValue(f, out alvo!)))
                alvo.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = true });
        };
    }

    /// <summary>Depois de abrir a lei: estrutura o texto (fora da thread da janela) e mostra a leitura padronizada, se houver texto.</summary>
    async Task MostrarLeituraPadronizadaAsync(Anexo anexo)
    {
        var id = anexo.Id;
        var blocos = await Task.Run(() => legislacao.Acervo.TextoPorPagina(id) is { } paginas ? LeituraPadronizada.Estruturar(paginas) : []);
        if (fechada || leiAberta?.Id != id) return;
        Desenhar(anexo, blocos);
        P.ModoLeitura.Visibility = blocos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        leituraPadronizadaLigada = blocos.Count > 0;
        P.ModoLeitura.SelectedItem = leituraPadronizadaLigada ? P.ModoPadronizado : P.ModoPdf;
        AplicarModoLeitura();
    }

    void EsconderLeituraPadronizada()
    {
        leituraPadronizadaLigada = false;
        P.ModoLeitura.Visibility = Visibility.Collapsed;
        P.VistaPadronizada.Visibility = Visibility.Collapsed;
        P.TextoPadronizado.Children.Clear();
        P.SumarioLei.Items.Clear();
    }

    void AplicarModoLeitura()
    {
        var padronizada = leituraPadronizadaLigada && ReferenceEquals(P.ModoLeitura.SelectedItem, P.ModoPadronizado);
        P.VistaPadronizada.Visibility = padronizada ? Visibility.Visible : Visibility.Collapsed;
        P.Leitor.Visibility = padronizada ? Visibility.Collapsed : Visibility.Visible;
    }

    void Desenhar(Anexo anexo, IReadOnlyList<BlocoLei> blocos)
    {
        var t = P.TextoPadronizado;
        t.Children.Clear();
        P.SumarioLei.Items.Clear();
        alvosSumario.Clear();
        // Selos de cima: a esfera e o nome do arquivo.
        var selos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 8) };
        selos.Children.Add(Selo(Esferas.Rotulo(anexo.Esfera), Destaque, new SolidColorBrush(Colors.White)));
        selos.Children.Add(Selo("Leitura padronizada do texto do PDF", FundoEmenta, Tinta));
        t.Children.Add(selos);
        if (!blocos.Any(b => b.Tipo == TipoBlocoLei.Epigrafe)) t.Children.Add(Epigrafe(anexo.Titulo));
        foreach (var b in blocos)
        {
            FrameworkElement el = b.Tipo switch
            {
                TipoBlocoLei.Epigrafe => Epigrafe(b.Texto),
                TipoBlocoLei.Ementa => new Border
                {
                    Background = FundoEmenta, CornerRadius = new CornerRadius(6), Padding = new Thickness(20, 14, 20, 14), Margin = new Thickness(0, 4, 0, 12),
                    Child = new TextBlock { Text = b.Texto, FontStyle = Windows.UI.Text.FontStyle.Italic, FontSize = 17, Foreground = Tinta, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
                },
                TipoBlocoLei.Titulo or TipoBlocoLei.Capitulo or TipoBlocoLei.Secao or TipoBlocoLei.Subsecao => Divisao(b),
                TipoBlocoLei.Artigo => Artigo(b),
                _ => Dispositivo(b),
            };
            t.Children.Add(el);
            if (b.Tipo is TipoBlocoLei.Titulo or TipoBlocoLei.Capitulo or TipoBlocoLei.Secao or TipoBlocoLei.Artigo)
            {
                var recuo = b.Tipo switch { TipoBlocoLei.Titulo => 0, TipoBlocoLei.Capitulo => 12, TipoBlocoLei.Secao => 24, _ => 28 };
                var texto = b.Tipo == TipoBlocoLei.Artigo ? b.Rotulo : b.Rotulo + (b.Texto.Length > 0 ? " — " + b.Texto : "");
                var rotuloSumario = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, FontWeight = b.Tipo == TipoBlocoLei.Artigo ? FontWeights.Normal : FontWeights.SemiBold };
                alvosSumario[rotuloSumario] = el;
                P.SumarioLei.Items.Add(new ListViewItem { Tag = el, Padding = new Thickness(8 + recuo, 4, 8, 4), MinHeight = 28, Content = rotuloSumario });
            }
        }
        P.RolagemPadronizada.ChangeView(null, 0, null, true);
    }

    static Border Selo(string texto, Brush fundo, Brush tinta) => new()
    {
        Background = fundo, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 4, 14, 5),
        Child = new TextBlock { Text = texto, Foreground = tinta, FontSize = 14 },
    };

    static FrameworkElement Epigrafe(string texto)
    {
        var g = new Grid { ColumnSpacing = 18, Margin = new Thickness(0, 8, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border { Background = Destaque, CornerRadius = new CornerRadius(2) });
        var tb = new TextBlock { Text = texto.ToUpperInvariant(), FontSize = 30, FontWeight = FontWeights.Bold, Foreground = Tinta, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        return g;
    }

    static FrameworkElement Divisao(BlocoLei b)
    {
        var grande = b.Tipo == TipoBlocoLei.Titulo;
        var tb = new TextBlock { FontSize = grande ? 24 : b.Tipo == TipoBlocoLei.Capitulo ? 20 : 17, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        tb.Inlines.Add(new Run { Text = b.Rotulo, Foreground = Tinta });
        if (b.Texto.Length > 0) tb.Inlines.Add(new Run { Text = " — " + b.Texto.ToUpperInvariant(), Foreground = grande ? AzulTitulo : Tinta });
        return new Border
        {
            Background = FundoTitulo, BorderBrush = Destaque, BorderThickness = new Thickness(grande ? 5 : 3, 0, 0, 0), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(24, 12, 24, 12), Margin = new Thickness(0, grande ? 20 : 12, 0, 6), Child = tb,
        };
    }

    static FrameworkElement Artigo(BlocoLei b)
    {
        var tb = new RichTextBlock { FontSize = 17, LineHeight = 30, Foreground = new SolidColorBrush(Colors.Black), TextAlignment = TextAlignment.Justify, IsTextSelectionEnabled = true };
        var p = new Paragraph();
        p.Inlines.Add(new InlineUIContainer
        {
            Child = new Border
            {
                Background = SeloArtigo, CornerRadius = new CornerRadius(4), Padding = new Thickness(10, 1, 10, 3), Margin = new Thickness(0, 0, 10, -6),
                Child = new TextBlock { Text = b.Rotulo, Foreground = new SolidColorBrush(Colors.White), FontWeight = FontWeights.Bold, FontSize = 17 },
            },
        });
        p.Inlines.Add(new Run { Text = b.Texto });
        tb.Blocks.Add(p);
        return new StackPanel { Margin = new Thickness(0, 10, 0, 0), Children = { tb } };
    }

    static FrameworkElement Dispositivo(BlocoLei b)
    {
        var recuo = b.Tipo switch { TipoBlocoLei.Paragrafo => 24, TipoBlocoLei.Inciso => 40, TipoBlocoLei.Alinea => 60, _ => 0 };
        var tb = new TextBlock { FontSize = 16, LineHeight = 28, Foreground = new SolidColorBrush(Colors.Black), TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Justify, IsTextSelectionEnabled = true, Margin = new Thickness(recuo, 2, 0, 2) };
        if (b.Rotulo.Length > 0) tb.Inlines.Add(new Run { Text = b.Rotulo + " ", FontWeight = FontWeights.Bold, Foreground = Tinta });
        tb.Inlines.Add(new Run { Text = b.Texto });
        return tb;
    }
}
