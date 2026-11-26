using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Página do módulo Ajustes (desenho §9): só a parte visual. Quem a preenche e reage às escolhas é a janela
/// (<c>MainWindow.Ajustes.cs</c>), pelos elementos <c>internal</c> e pelos eventos das linhas da legislação. É criada uma vez e
/// trocada por Visibility.</summary>
public sealed partial class PaginaAjustes : UserControl
{
    readonly Dictionary<string, StackPanel> categorias = new(StringComparer.Ordinal);
    readonly Dictionary<string, NavigationViewItem> itensMenu = new(StringComparer.Ordinal);

    public PaginaAjustes()
    {
        InitializeComponent();
        // O menu sai de CategoriasAjustes.Menu (a ordem é testada); "Sistema" vai para o rodapé, como no WinStatusBar.
        foreach (var pagina in Paginas.Children.OfType<StackPanel>())
            categorias.Add((string)pagina.Tag, pagina);
        foreach (var c in CategoriasAjustes.Menu)
        {
            var item = new NavigationViewItem { Content = c.Titulo, Tag = c.Tag, Icon = new FontIcon { Glyph = c.Glifo } };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, c.Titulo);
            if (c.Pai is { } pai && itensMenu.TryGetValue(pai, out var mae)) { mae.MenuItems.Add(item); mae.IsExpanded = true; }
            else (c.Rodape ? Menu.FooterMenuItems : Menu.MenuItems).Add(item);
            itensMenu[c.Tag] = item;
        }
        Menu.SelectedItem = itensMenu[CategoriasAjustes.Geral];
        // Menu só com ícones abaixo de 860 px (como o WinStatusBar).
        SizeChanged += (_, e) => Menu.PaneDisplayMode = e.NewSize.Width < 860 ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
    }

    /// <summary>A categoria à vista (a etiqueta de <see cref="CategoriasAjustes"/>).</summary>
    public string CategoriaAtual { get; private set; } = CategoriasAjustes.Geral;
    internal bool PermitirRestaurarModelos { get; set; }

    /// <summary>Os títulos do menu, na ordem, com as subpáginas logo depois da mãe (o roteiro confere as categorias).</summary>
    public IReadOnlyList<string> TitulosDoMenu =>
        Menu.MenuItems.Concat(Menu.FooterMenuItems).OfType<NavigationViewItem>()
            .SelectMany(i => new[] { i }.Concat(i.MenuItems.OfType<NavigationViewItem>())).Select(i => (string)i.Content).ToList();

    /// <summary>Mostra a categoria e rola até uma seção dela (por exemplo, as leis anexadas dentro de Módulos).</summary>
    public void MostrarSecao(string tag, FrameworkElement secao)
    {
        MostrarCategoria(tag);
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
            () => secao.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.0, AnimationDesired = false }));
    }

    /// <summary>Mostra a categoria (pelo menu, pela pesquisa ou pelos botões "Abrir Ajustes" dos módulos).</summary>
    public void MostrarCategoria(string tag)
    {
        var c = CategoriasAjustes.De(tag).Tag;
        if (itensMenu.TryGetValue(c, out var item) && !ReferenceEquals(Menu.SelectedItem, item)) Menu.SelectedItem = item;
        else Trocar(c);
    }

    void AoEscolherCategoria(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem { Tag: string tag }) Trocar(tag);
    }

    void Trocar(string tag)
    {
        var categoria = CategoriasAjustes.De(tag);
        IconeCategoria.Glyph = categoria.Glifo;
        TituloCategoria.Text = categoria.Titulo;
        DescricaoCategoria.Text = categoria.Ajuda;
        CategoriaAtual = tag;
        TrilhaModelos.Visibility = tag == CategoriasAjustes.Modelos ? Visibility.Visible : Visibility.Collapsed;
        BotaoRestaurarModelos.Visibility = PermitirRestaurarModelos ? TrilhaModelos.Visibility : Visibility.Collapsed;
        EstadoSalvoCategoria.Visibility = tag == CategoriasAjustes.Modelos ? Visibility.Collapsed : Visibility.Visible;
        foreach (var (t, pagina) in categorias) pagina.Visibility = t == tag ? Visibility.Visible : Visibility.Collapsed;
        Rolagem.ChangeView(null, 0, null, disableAnimation: true);
        CategoriaMostrada?.Invoke(tag);
    }

    /// <summary>Uma categoria passou a ser vista (a de Inteligência artificial consulta os agentes sozinha).</summary>
    internal event Action<string>? CategoriaMostrada;

    /// <summary>Linha de cartão: controle à direita ou embaixo, conforme a largura (<see cref="LinhaAjuste"/>).</summary>
    void AoMudarTamanhoDaLinha(object sender, SizeChangedEventArgs e)
    {
        if (sender is Grid linha) LinhaAjuste.Ajustar(linha, e.NewSize.Width);
    }

    /// <summary>Título digitado numa linha (ao sair da caixa ou com Enter): devolve a mensagem de recusa, ou null se valeu.</summary>
    internal Func<string, string, Task<string?>>? TituloEditado { get; set; }

    /// <summary>O usuário saiu de uma caixa de título (aplicada ou não): a janela refaz a tela adiada durante a edição.</summary>
    internal event Action? TituloConcluido;

    /// <summary>Esfera escolhida numa linha (id, esfera).</summary>
    internal event Action<string, Esfera>? EsferaEscolhida;

    /// <summary>"Substituir arquivo…" de uma linha (id).</summary>
    internal event Action<string>? SubstituirPedido;

    /// <summary>"Excluir" de uma linha (id).</summary>
    internal event Action<string>? ExcluirPedido;

    public static int IndiceDaEsfera(Esfera e) => Esferas.Indice(e);

    public static Visibility VisivelSe(string? texto) => string.IsNullOrEmpty(texto) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>A linha de um controle da lista: o ItemsRepeater põe o item no DataContext do elemento (e dos filhos).</summary>
    static LinhaAnexo? LinhaDe(object sender) => (sender as FrameworkElement)?.DataContext as LinhaAnexo;

    async void AoSairDoTitulo(object sender, RoutedEventArgs e)
    {
        await AplicarTituloAsync(sender as TextBox);
        TituloConcluido?.Invoke();
    }

    async void AoTeclarNoTitulo(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter) return;
        e.Handled = true;
        await AplicarTituloAsync(sender as TextBox);
    }

    async Task AplicarTituloAsync(TextBox? caixa)
    {
        if (caixa is null || LinhaDe(caixa) is not { } linha || TituloEditado is null) return;
        if (caixa.Text.Trim() == linha.Titulo) return;
        string? recusa;
        try { recusa = await TituloEditado(linha.Id, caixa.Text); }
        catch (Exception) { recusa = ""; } // a janela já mostrou o motivo; a caixa volta ao título anterior
        // Recusado: a caixa volta ao título anterior (a mensagem aparece na InfoBar da seção).
        if (recusa is not null && ReferenceEquals(LinhaDe(caixa), linha)) caixa.Text = linha.Titulo;
    }

    void AoMudarEsfera(object sender, SelectionChangedEventArgs e)
    {
        // A recarga da linha (ItemsRepeater reciclando o elemento) também muda a seleção: só vale o que difere do item atual.
        if (sender is not ComboBox { SelectedIndex: >= 0 } c || LinhaDe(c) is not { } linha) return;
        var esfera = Esferas.Ordem[c.SelectedIndex];
        if (esfera != linha.Esfera) EsferaEscolhida?.Invoke(linha.Id, esfera);
    }

    void AoSubstituir(object sender, RoutedEventArgs e) { if (LinhaDe(sender) is { } l) SubstituirPedido?.Invoke(l.Id); }

    void AoExcluir(object sender, RoutedEventArgs e) { if (LinhaDe(sender) is { } l) ExcluirPedido?.Invoke(l.Id); }
}
