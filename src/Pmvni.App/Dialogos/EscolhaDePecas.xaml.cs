using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pmvni.Agentes;

namespace Pmvni.App;

/// <summary>Tarefa 14: as versões possíveis de uma peça lado a lado, para o analista escolher a que vale (decisão do tipo <c>pecas</c>). Cada versão é um
/// botão de opção com a miniatura da primeira página (do <c>cache-pdf</c>; sem ela, o nome do arquivo) e "Abrir o PDF" (pelo mesmo caminho que o editor usa
/// para abrir os arquivos do processo). Nada aqui chama a IA.</summary>
public sealed partial class EscolhaDePecas : UserControl
{
    const string Grupo = "VersoesDaPeca";
    readonly IReadOnlyList<PecaCandidata> versoes;
    readonly List<RadioButton> opcoes = [];
    readonly List<Button> botoesAbrir = [];

    public EscolhaDePecas(string motivo, string aviso, IReadOnlyList<PecaCandidata> versoes)
    {
        InitializeComponent();
        this.versoes = versoes;
        TextoMotivo.Text = motivo;
        TextoAviso.Text = aviso;
        TextoAviso.Visibility = string.IsNullOrWhiteSpace(aviso) ? Visibility.Collapsed : Visibility.Visible;
        TextoEscolha.Text = "Escolha a versão que vale para a análise.";
        for (var i = 0; i < versoes.Count; i++) PainelVersoes.Children.Add(Cartao(i, versoes[i]));
    }

    /// <summary>A versão marcada; null = nenhuma ainda.</summary>
    public PecaCandidata? Escolhida { get; private set; }

    /// <summary>A marcação mudou (o diálogo habilita o botão principal).</summary>
    public event Action? EscolhaMudou;

    public int Quantas => versoes.Count;

    /// <summary>Se a versão <paramref name="i"/> mostra a miniatura do cache (senão, o nome do arquivo).</summary>
    public bool MostraMiniatura(int i) => opcoes[i].Tag is true;

    /// <summary>Se "Abrir o PDF" da versão <paramref name="i"/> está disponível (o arquivo existe dentro do processo).</summary>
    public bool PodeAbrir(int i) => botoesAbrir[i].IsEnabled;

    public string TextoDaEscolha => TextoEscolha.Text;

    /// <summary>Marca a versão <paramref name="i"/> (o clique no cartão faz o mesmo).</summary>
    public void Marcar(int i) => opcoes[i].IsChecked = true;

    /// <summary>"Abrir o PDF" da versão <paramref name="i"/>. false = não abriu (o motivo fica embaixo das versões).</summary>
    public bool Abrir(int i)
    {
        if (versoes[i].Arquivo is not { } arquivo) return false;
        if (AberturaExterna.Abrir(arquivo, out var motivo)) return true;
        TextoEscolha.Text = "O PDF não foi aberto: " + motivo;
        return false;
    }

    FrameworkElement Cartao(int i, PecaCandidata v)
    {
        var miniatura = Miniatura(v.Miniatura);
        var visual = new StackPanel { Spacing = 6, Width = 220 };
        if (miniatura is not null)
            visual.Children.Add(new Border
            {
                Height = 200, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                Child = new Image { Source = miniatura, Stretch = Stretch.Uniform },
            });
        else
        {
            var semMiniatura = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            semMiniatura.Children.Add(new FontIcon { Glyph = "", FontSize = 32 });
            semMiniatura.Children.Add(new TextBlock { Text = v.Nome, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 200 });
            visual.Children.Add(new Border
            {
                Height = 200, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1),
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"], Child = semMiniatura,
            });
        }
        visual.Children.Add(new TextBlock { Text = v.Texto, TextWrapping = TextWrapping.Wrap, Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["PmvniAjusteTitulo"] });
        visual.Children.Add(new TextBlock { Text = Relativo(v), TextWrapping = TextWrapping.Wrap, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] });

        var opcao = new RadioButton { GroupName = Grupo, Content = visual, Tag = miniatura is not null, VerticalContentAlignment = VerticalAlignment.Top };
        AutomationProperties.SetName(opcao, v.Texto + " (" + v.Nome + ")");
        opcao.Checked += (_, _) =>
        {
            Escolhida = v;
            TextoEscolha.Text = "Escolhida: " + v.Texto + ". A análise continua com esta versão.";
            EscolhaMudou?.Invoke();
        };
        opcoes.Add(opcao);

        var abrir = new Button { Content = "Abrir o PDF", IsEnabled = v.Arquivo is not null };
        ToolTipService.SetToolTip(abrir, v.Arquivo is null ? "O arquivo não foi encontrado na pasta do processo." : "Abre " + v.Nome + " no programa padrão do Windows.");
        AutomationProperties.SetName(abrir, "Abrir o PDF " + v.Nome);
        abrir.Click += (_, _) => Abrir(i);
        botoesAbrir.Add(abrir);

        var cartao = new StackPanel { Spacing = 8 };
        cartao.Children.Add(opcao);
        cartao.Children.Add(abrir);
        return new Border
        {
            Padding = new Thickness(12), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"], Child = cartao,
        };
    }

    static string Relativo(PecaCandidata v) => v.Valor.Replace('/', '\\');

    static BitmapImage? Miniatura(string? png)
    {
        if (png is null || !File.Exists(png)) return null;
        try { return new BitmapImage(new Uri(png)) { DecodePixelWidth = 440 }; }
        catch (Exception e) when (e is UriFormatException or ArgumentException) { return null; }
    }
}
