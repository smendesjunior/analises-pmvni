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

    public Trilho() => InitializeComponent();

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
        icone is Viewbox { ActualWidth: > 0, ActualHeight: > 0, Child: Canvas { Children.Count: > 0 } };

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
            var fundo = new Border
            {
                Width = 56, Height = 36, CornerRadius = new CornerRadius(10),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Style = Estilo("PmvniTrilhoFundoInativoStyle"),
                Child = (UIElement?)icone ?? new FontIcon { Glyph = Pmvni.App.Modulos.Glifo(m), FontSize = 18 },
            };
            var indicador = new Border { Style = Estilo("PmvniRailIndicatorStyle"), Visibility = Visibility.Collapsed, Margin = new Thickness(1, 0, 0, 0) };
            var rotulo = new TextBlock { Text = Pmvni.App.Modulos.Rotulo(m), Style = Estilo("PmvniTrilhoRotuloStyle") };
            var grade = new Grid();
            grade.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            grade.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grade.Children.Add(fundo);
            grade.Children.Add(indicador);
            Grid.SetRow(rotulo, 1);
            grade.Children.Add(rotulo);
            var botao = new Button
            {
                Content = grade,
                Style = Estilo("PmvniTrilhoItemStyle"),
                Tag = m,
                AccessKey = Pmvni.App.Modulos.Numero(m).ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
            AutomationProperties.SetName(botao, Pmvni.App.Modulos.Rotulo(m));
            AutomationProperties.SetAcceleratorKey(botao, "Ctrl+" + Pmvni.App.Modulos.Numero(m));
            ToolTipService.SetToolTip(botao, Pmvni.App.Modulos.Dica(m));
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
        if (atual is { } a) Marcar(a);
    }

    static FrameworkElement? CriarIcone(Modulo m) => NovoIcone(m, 28);

    /// <summary>O ícone colorido do módulo (o DataTemplate do dicionário, materializado com LoadContent: um Viewbox), <paramref name="lado"/> x
    /// <paramref name="lado"/>; null se o template não existe. As cores são ThemeResource e seguem o tema do lugar onde o ícone fica.</summary>
    internal static FrameworkElement? NovoIcone(Modulo m, double lado)
    {
        if (!Application.Current.Resources.TryGetValue(Pmvni.App.Modulos.ChaveIcone(m), out var t) || t is not DataTemplate template) return null;
        if (template.LoadContent() is not FrameworkElement icone) return null;
        icone.Width = lado;
        icone.Height = lado;
        icone.HorizontalAlignment = HorizontalAlignment.Center;
        icone.VerticalAlignment = VerticalAlignment.Center;
        icone.IsHitTestVisible = false;
        return icone;
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
        var chave = m == atual ? "PmvniTrilhoFundoAtivoStyle"
            : pressionados.Contains(m) ? "PmvniTrilhoFundoPressionadoStyle"
            : sob.Contains(m) ? "PmvniTrilhoFundoSobreStyle"
            : "PmvniTrilhoFundoInativoStyle";
        var estilo = Estilo(chave);
        if (i.Fundo.Style != estilo) i.Fundo.Style = estilo;
    }
}
