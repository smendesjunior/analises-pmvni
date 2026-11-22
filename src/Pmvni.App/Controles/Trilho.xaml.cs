using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Pmvni.App;

/// <summary>Trilho à esquerda com os módulos de <see cref="Faixas.Disponiveis"/> (só os que funcionam), no estilo do Gmail: ícone
/// colorido (<see cref="Modulos.ChaveIcone"/>) sobre um fundo arredondado que só aparece no ativo (e ao passar o mouse), barrinha de
/// destaque e rótulo. Clicar avisa por <see cref="ModuloEscolhido"/>; quem troca a página e a faixa é a janela, que depois chama
/// <see cref="Marcar"/>.</summary>
public sealed partial class Trilho : UserControl
{
    sealed record Item(Button Botao, Border Fundo, Border Indicador, TextBlock Rotulo, FrameworkElement? Icone);

    readonly Dictionary<Modulo, Item> itens = new();
    readonly HashSet<Modulo> sob = new();
    readonly HashSet<Modulo> pressionados = new();
    Modulo? atual;

    public Trilho()
    {
        InitializeComponent();
        SizeChanged += (_, _) => AjustarAlturaDosItens();
    }

    void AjustarAlturaDosItens()
    {
        if (itens.Count == 0 || ActualHeight <= 0) return;
        var altura = Math.Clamp(Math.Floor((ActualHeight - 48) / itens.Count), 54, 68);
        foreach (var (modulo, item) in itens)
            item.Fundo.Height = modulo == Modulo.Acompanhamento ? altura + 12 : altura;
    }

    public event Action<Modulo>? ModuloEscolhido;

    /// <summary>Os módulos do trilho, na ordem dada (Ajustes vai para o pé).</summary>
    public IReadOnlyList<Modulo> Modulos => itens.Keys.ToList();

    /// <summary>O módulo marcado como ativo (fundo, barrinha e "Selecionado" para o Narrador), ou null antes do primeiro <see cref="Marcar"/>.</summary>
    public Modulo? Ativo => atual;

    /// <summary>O ícone colorido do módulo está desenhado: o template de <c>IconesModulos.xaml</c> virou elementos (Viewbox com o desenho), com
    /// tamanho na tela; false = ficou o glifo de reserva ou o ícone não tem tamanho.</summary>
    public bool IconeCarregado(Modulo m) => itens.TryGetValue(m, out var i) && IconeDesenhado(i.Icone);

    /// <summary>Um ícone de <see cref="NovoIcone"/> está desenhado: tem largura e altura no layout e o Viewbox tem o Canvas com as formas.</summary>
    internal static bool IconeDesenhado(FrameworkElement? icone) =>
        icone is Image { ActualWidth: > 0, ActualHeight: > 0, Source: not null }
        || icone is Viewbox { ActualWidth: > 0, ActualHeight: > 0, Child: Canvas { Children.Count: > 0 } };

    /// <summary>O fundo arredondado do item está pintado (só o ativo, fora o "sobre" do mouse).</summary>
    public bool FundoVisivel(Modulo m) => itens.TryGetValue(m, out var i) && i.Fundo.Style == Estilo("PmvniTrilhoFundoAtivoStyle");

    /// <summary>O <c>ItemStatus</c> que o Narrador lê no item ("Selecionado" só no ativo).</summary>
    public string EstadoAcessivel(Modulo m) => itens.TryGetValue(m, out var i) ? AutomationProperties.GetItemStatus(i.Botao) : "";

    /// <summary>O botão do módulo (o roteiro foca e navega com as setas).</summary>
    internal Button? Botao(Modulo m) => itens.TryGetValue(m, out var i) ? i.Botao : null;

    static Style Estilo(string chave) => (Style)Application.Current.Resources[chave];

    public void Montar(IReadOnlyList<Modulo> modulos)
    {
        Cima.Children.Clear();
        Baixo.Children.Clear();
        itens.Clear();
        foreach (var m in modulos)
        {
            var icone = CriarIcone(m);
            // Como o Gmail (correções 2026-11-15): o fundo arredondado cobre o ícone e o rótulo juntos; a barrinha fica na borda esquerda.
            var conteudo = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Spacing = 3 };
            var rotulo = new TextBlock { Text = RotuloNoTrilho(m), Style = Estilo("PmvniTrilhoRotuloStyle"), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 110, LineHeight = 15, LineStackingStrategy = LineStackingStrategy.BlockLineHeight };
            var duasLinhas = rotulo.Text.Contains('\n');
            var faixaIcone = new Grid { Width = 114 };
            faixaIcone.Children.Add(icone);
            conteudo.Children.Add(faixaIcone);
            conteudo.Children.Add(rotulo);
            var fundo = new Border
            {
                Width = 114, Height = duasLinhas ? 72 : 60, CornerRadius = (CornerRadius)Application.Current.Resources["ControlCornerRadius"],
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Style = Estilo("PmvniTrilhoFundoInativoStyle"),
                Child = conteudo,
            };
            // Indicador de seleção no padrão do NavigationView do WinUI 3: pílula de 3 x 16 (PmvniRailIndicatorStyle), dentro do fundo
            // arredondado, junto da borda esquerda e centralizada na vertical.
            var indicador = new Border { Style = Estilo("PmvniRailIndicatorStyle"), Visibility = Visibility.Collapsed, Margin = new Thickness(4, 0, 0, 0), IsHitTestVisible = false };
            var grade = new Grid { Width = 114, HorizontalAlignment = HorizontalAlignment.Center };
            grade.Children.Add(fundo);
            grade.Children.Add(indicador);
            var botao = new Button
            {
                Width = 118,
                Content = grade,
                Style = Estilo("PmvniTrilhoItemStyle"),
                Tag = m,
                AccessKey = Pmvni.App.Modulos.Numero(m).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            AutomationProperties.SetName(botao, Pmvni.App.Modulos.Rotulo(m));
            if (Pmvni.App.Modulos.TemAtalho(m))
                AutomationProperties.SetAcceleratorKey(botao, "Ctrl+" + Pmvni.App.Modulos.Numero(m));
            Dicas.AplicarComando(botao, Pmvni.App.Modulos.Comando(m), Pmvni.App.Modulos.Rotulo(m));
            botao.Click += (_, _) => ModuloEscolhido?.Invoke(m);
            botao.PointerEntered += (_, _) => { sob.Add(m); Pintar(m); };
            botao.PointerExited += (_, _) => { sob.Remove(m); pressionados.Remove(m); Pintar(m); };
            botao.PointerCaptureLost += (_, _) => { pressionados.Remove(m); Pintar(m); };
            // O Button marca PointerPressed/Released como tratados: o trilho os ouve mesmo assim (handledEventsToo).
            botao.AddHandler(PointerPressedEvent, new PointerEventHandler((_, _) => { pressionados.Add(m); Pintar(m); }), true);
            botao.AddHandler(PointerReleasedEvent, new PointerEventHandler((_, _) => { pressionados.Remove(m); Pintar(m); }), true);
            (Pmvni.App.Modulos.Embaixo(m) ? Baixo : Cima).Children.Add(botao);
            itens[m] = new Item(botao, fundo, indicador, rotulo, icone);
        }
        AjustarAlturaDosItens();
        if (atual is { } a) Marcar(a);
    }

    FrameworkElement CriarIcone(Modulo m) => (FrameworkElement)((DataTemplate)Resources["IconeTrilho" + m]).LoadContent();

    /// <summary>O rótulo na largura do item do trilho (104): "Acompanhamento de processos" em duas linhas; os outros ficam como são.</summary>
    internal static string RotuloNoTrilho(Modulo m) => m == Modulo.Acompanhamento ? "Acompanhamento\nde processos" : Pmvni.App.Modulos.Rotulo(m);

    /// <summary>O ícone colorido do módulo (o DataTemplate do dicionário, materializado com LoadContent: um Viewbox), <paramref name="lado"/> x
    /// <paramref name="lado"/>; null se o template não existe. As cores são ThemeResource e seguem o tema do lugar onde o ícone fica.</summary>
    internal static FrameworkElement? NovoIcone(Modulo m, double lado)
    {
        // Correção 2026-11-16: os ícones novos (PNG em alta, do pacote Fluent); o desenho vetorial antigo fica de reserva.
        if (ImagemDoModulo(m, lado) is { } imagem)
            return new Image
            {
                Source = imagem, Width = lado, Height = lado, Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false,
            };
        if (!Application.Current.Resources.TryGetValue(Pmvni.App.Modulos.ChaveIcone(m), out var t) || t is not DataTemplate template) return null;
        if (template.LoadContent() is not FrameworkElement icone) return null;
        icone.Width = lado;
        icone.Height = lado;
        icone.HorizontalAlignment = HorizontalAlignment.Center;
        icone.VerticalAlignment = VerticalAlignment.Center;
        icone.IsHitTestVisible = false;
        return icone;
    }

    static readonly Dictionary<string, Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> imagens = new(StringComparer.Ordinal);

    /// <summary>O PNG embutido do módulo no tamanho que dá nitidez até 200 % de escala (64 px até 32 de lado; 128 acima); null se faltar.</summary>
    static Microsoft.UI.Xaml.Media.Imaging.BitmapImage? ImagemDoModulo(Modulo m, double lado)
    {
        var nome = "Pmvni.App.modulos." + m.ToString().ToLowerInvariant() + (lado <= 32 ? "-64" : "-128") + ".png";
        if (imagens.TryGetValue(nome, out var pronta)) return pronta;
        Microsoft.UI.Xaml.Media.Imaging.BitmapImage? imagem = null;
        try
        {
            using var recurso = typeof(Trilho).Assembly.GetManifestResourceStream(nome);
            if (recurso is not null)
            {
                var fluxo = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                // Sem using: fechar o adaptador fecharia o fluxo que a imagem ainda lê (ObjectDisposedException, 2026-11-16).
                var escrita = System.IO.WindowsRuntimeStreamExtensions.AsStreamForWrite(fluxo);
                recurso.CopyTo(escrita);
                escrita.Flush();
                fluxo.Seek(0);
                imagem = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                imagem.SetSource(fluxo);
            }
        }
        catch (Exception e) { RegistroErros.Gravar("trilho.icone", e); imagem = null; }
        imagens[nome] = imagem;
        return imagem;
    }

    /// <summary>Marca o módulo ativo (e só ele): fundo arredondado, barrinha, rótulo forte e "Selecionado" para o Narrador.</summary>
    public void Marcar(Modulo ativo)
    {
        atual = ativo;
        foreach (var (m, i) in itens)
        {
            var sim = m == ativo;
            i.Indicador.Visibility = sim ? Visibility.Visible : Visibility.Collapsed;
            i.Rotulo.Style = Estilo(sim ? "PmvniTrilhoRotuloAtivoStyle" : "PmvniTrilhoRotuloStyle");
            AutomationProperties.SetItemStatus(i.Botao, sim ? "Selecionado" : "");
            Pintar(m);
        }
    }

    void Pintar(Modulo m)
    {
        if (!itens.TryGetValue(m, out var i)) return;
        var chave = pressionados.Contains(m) ? "PmvniTrilhoFundoPressionadoStyle"
            : sob.Contains(m) ? "PmvniTrilhoFundoSobreStyle"
            : m == atual ? "PmvniTrilhoFundoAtivoStyle" : "PmvniTrilhoFundoInativoStyle";
        var estilo = Estilo(chave);
        if (i.Fundo.Style != estilo) i.Fundo.Style = estilo;
    }
}
