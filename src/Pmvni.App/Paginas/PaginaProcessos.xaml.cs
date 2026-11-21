using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

/// <summary>O que o menu de contexto, o duplo clique e o Enter pedem para o arquivo escolhido; quem faz é a janela.</summary>
public enum AcaoArquivo { Abrir, MostrarNoExplorador, CopiarCaminho }

/// <summary>Um grupo da grade de miniaturas: o título, os arquivos e se está recolhido (recolhido, a grade não recebe os arquivos; o
/// cabeçalho continua na tela). Lista de <see cref="object"/> porque é o que a grade agrupada do WinUI percorre.</summary>
public sealed class GrupoMiniaturas : List<object>
{
    public GrupoMiniaturas(string titulo, IReadOnlyList<ItemArquivo> todos, bool recolhido) : base(recolhido ? Array.Empty<object>() : todos)
    {
        Titulo = titulo;
        Todos = todos;
        Recolhido = recolhido;
    }

    public string Titulo { get; }
    public IReadOnlyList<ItemArquivo> Todos { get; }
    public bool Recolhido { get; }

    /// <summary>A seta do cabeçalho: para a direita recolhido, para baixo aberto (como no Explorador).</summary>
    public string Glifo => Recolhido ? Glifos.Avancar : Glifos.Proximo;
    public string Contagem => "(" + Todos.Count.ToString(CultureInfo.InvariantCulture) + ")";
    public string Dica => Recolhido ? "Mostrar os arquivos deste grupo" : "Recolher este grupo";

    /// <summary>O que o Narrador lê no cabeçalho: "Grupo Hoje, 2 arquivos, expandido".</summary>
    public string NomeAcessivel =>
        $"Grupo {Titulo}, {Todos.Count.ToString(CultureInfo.InvariantCulture)} {(Todos.Count == 1 ? "arquivo" : "arquivos")}, {(Recolhido ? "recolhido" : "expandido")}";

    public override string ToString() => NomeAcessivel;
}

/// <summary>Página do módulo Processos (desenho §9): só a parte visual. Quem a preenche e reage às escolhas é a janela
/// (<c>MainWindow.Pastas.cs</c>), pelos elementos <c>internal</c>. É criada uma vez e trocada por Visibility.
/// <para>Os arquivos do processo aparecem em Lista (três colunas: Relatórios, Despachos, Projeto e documentos) ou em Miniaturas (uma
/// grade só, como o Explorador), agrupados e ordenados pela escolha de Agrupar e Classificar
/// (<see cref="OrganizacaoArquivos"/>). Nos dois modos a escolha de um arquivo é a da <c>ListaDetalhe</c>, que fica fora da tela.</para></summary>
public sealed partial class PaginaProcessos : UserControl
{
    readonly DispatcherTimer esperaDoMouse = new() { Interval = TimeSpan.FromMilliseconds(300) };
    Windows.Foundation.Point pontoNaLista;
    LinhaProcesso? processoSobMouse;

    void AoMoverSobreProcessos(object sender, PointerRoutedEventArgs e)
    {
        esperaDoMouse.Stop();
        processoSobMouse = null;
        if (e.Pointer.PointerDeviceType != Microsoft.UI.Input.PointerDeviceType.Mouse ||
            e.OriginalSource is not DependencyObject origem) return;
        var ponto = e.GetCurrentPoint(ListaProcessos);
        while (origem is not ListViewItem && origem != ListaProcessos && VisualTreeHelper.GetParent(origem) is { } pai) origem = pai;
        if (ponto.IsInContact || origem is not ListViewItem item ||
            ListaProcessos.ItemFromContainer(item) is not LinhaProcesso linha || Equals(ListaProcessos.SelectedItem, linha)) return;
        pontoNaLista = ponto.Position;
        processoSobMouse = linha;
        esperaDoMouse.Start();
    }

    void SelecionarProcessoSobMouse(object? sender, object e)
    {
        esperaDoMouse.Stop();
        var linha = processoSobMouse;
        processoSobMouse = null;
        if (linha is null || !ListaProcessos.IsLoaded || !Trabalhando.VisivelDeFato(ListaProcessos) ||
            ListaProcessos.ContainerFromItem(linha) is not ListViewItem item) return;
        var local = ListaProcessos.TransformToVisual(item).TransformPoint(pontoNaLista);
        if (local.X >= 0 && local.Y >= 0 && local.X < item.ActualWidth && local.Y < item.ActualHeight)
            ListaProcessos.SelectedItem = linha;
    }

    void AoSairDosProcessos(object sender, PointerRoutedEventArgs e) => esperaDoMouse.Stop();
    void AoUsarTecladoNosProcessos(object sender, KeyRoutedEventArgs e) => esperaDoMouse.Stop();

    bool sincronizando;
    ExibicaoArquivos exibicao = ExibicaoArquivos.Padrao;
    bool miniaturas;
    // Os grupos recolhidos na grade, pelo título; valem para o processo que está na tela.
    readonly HashSet<string> recolhidos = new(StringComparer.Ordinal);
    string pastaDosRecolhidos = "";
    IReadOnlyList<GrupoMiniaturas> gruposDaGrade = [];
    CollectionViewSource? fonteDaGrade;

    void AoDimensionarDetalhe(object sender, SizeChangedEventArgs e)
    {
        bool estreito = e.NewSize.Width < 850;
        Grid.SetRow(BarraExibicao, estreito ? 1 : 0);
        Grid.SetColumn(BarraExibicao, estreito ? 0 : 1);
        ((Grid)sender).RowSpacing = estreito ? 6 : 0;
    }

    internal void MostrarProtocolo(string? protocolo)
    {
        TextoProtocolo.Text = protocolo ?? "";
        SeloProtocolo.Visibility = string.IsNullOrEmpty(protocolo) ? Visibility.Collapsed : Visibility.Visible;
    }

    public PaginaProcessos()
    {
        InitializeComponent();
        esperaDoMouse.Tick += SelecionarProcessoSobMouse;
        // A frase da tela vazia acompanha o texto que a janela escreve em TextoListaVazia.
        TextoListaVazia.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) => Vazio.DefinirFrase(TextoListaVazia.Text));
        // As linhas do detalhe vão para as colunas (ou para a grade); a escolha numa delas é a escolha da ListaDetalhe.
        ListaDetalhe.RegisterPropertyChangedCallback(ItemsControl.ItemsSourceProperty, (_, _) =>
        {
            // O processo foi lido (de novo): um arquivo pode ter mudado, as miniaturas guardadas não valem mais.
            MiniaturaArquivo.EsquecerGuardadas();
            if (!string.Equals(pastaDosRecolhidos, TextoPastaDetalhe.Text, StringComparison.OrdinalIgnoreCase))
            {
                pastaDosRecolhidos = TextoPastaDetalhe.Text;
                recolhidos.Clear();
            }
            Distribuir();
        });
        foreach (var v in Visoes)
        {
            v.SelectionChanged += AoEscolherNaVisao;
            v.DoubleTapped += (_, e) => { if (ItemEm(e.OriginalSource as DependencyObject) is { Selecionavel: true }) AcaoPedida?.Invoke(AcaoArquivo.Abrir); };
            v.KeyDown += AoTeclar;
            v.ContextRequested += AoPedirMenu;
        }
        ListaDetalhe.SelectionChanged += (_, _) =>
        {
            if (sincronizando) return;
            sincronizando = true;
            try { RefletirEscolha(); }
            finally { sincronizando = false; }
        };
        // A grade só pede a miniatura dos blocos que estão na tela; o bloco que vai para a reserva cancela o pedido dele.
        GradeMiniaturas.ContainerContentChanging += (_, e) =>
        {
            if ((e.ItemContainer.ContentTemplateRoot as Panel)?.Children.OfType<MiniaturaArquivo>().FirstOrDefault() is not { } miniatura) return;
            if (e.InRecycleQueue) miniatura.Cancelar();
            else miniatura.Garantir();
        };
        SincronizarMenus();
        Distribuir();
    }

    /// <summary>A escolha de Agrupar ou Classificar ou o modo (Lista/Miniaturas) mudou pela página: a janela guarda nos Ajustes.</summary>
    internal event Action? ExibicaoMudou;

    /// <summary>Duplo clique, Enter ou menu de contexto sobre o arquivo escolhido.</summary>
    internal event Action<AcaoArquivo>? AcaoPedida;

    internal ExibicaoArquivos Exibicao => exibicao;
    internal bool EmMiniaturas => miniaturas;

    /// <summary>As três colunas do modo Lista.</summary>
    internal IReadOnlyList<ListView> Colunas => [ColunaRelatorios, ColunaDespachos, ColunaProjeto];

    /// <summary>Os grupos que a grade de miniaturas mostra (vazio no modo Lista).</summary>
    internal IReadOnlyList<GrupoMiniaturas> GruposDaGrade => gruposDaGrade;

    /// <summary>O último menu de contexto de arquivo aberto (o roteiro confere as entradas).</summary>
    internal MenuFlyout? UltimoMenuDeArquivo { get; private set; }

    IReadOnlyList<ListViewBase> Visoes => [ColunaRelatorios, ColunaDespachos, ColunaProjeto, GradeMiniaturas];

    /// <summary>A escolha guardada nos Ajustes (ao abrir o app e em "Restaurar os ajustes"); não dispara <see cref="ExibicaoMudou"/>.</summary>
    internal void DefinirExibicao(ExibicaoArquivos nova, bool emMiniaturas)
    {
        if (nova == exibicao && emMiniaturas == miniaturas) return;
        exibicao = nova;
        miniaturas = emMiniaturas;
        SincronizarMenus();
        Distribuir();
    }

    void Mudar(ExibicaoArquivos nova, bool emMiniaturas)
    {
        var mudou = nova != exibicao || emMiniaturas != miniaturas;
        exibicao = nova;
        miniaturas = emMiniaturas;
        SincronizarMenus(); // sempre: o item clicado inverteu a própria marca
        if (!mudou) return;
        Distribuir();
        ExibicaoMudou?.Invoke();
    }

    void AoEscolherOrdem(object sender, RoutedEventArgs e) => Mudar(exibicao with
    {
        Ordenar = ReferenceEquals(sender, ItemOrdemData) ? OrdemArquivos.Data : ReferenceEquals(sender, ItemOrdemTipo) ? OrdemArquivos.Tipo : OrdemArquivos.Nome,
    }, miniaturas);

    void AoEscolherSentido(object sender, RoutedEventArgs e) => Mudar(exibicao with { Decrescente = ReferenceEquals(sender, ItemDecrescente) }, miniaturas);

    void AoEscolherGrupo(object sender, RoutedEventArgs e) => Mudar(exibicao with
    {
        Agrupar = ReferenceEquals(sender, ItemGrupoTipo) ? AgrupamentoArquivos.Tipo
            : ReferenceEquals(sender, ItemGrupoData) ? AgrupamentoArquivos.Data
            : ReferenceEquals(sender, ItemGrupoNenhum) ? AgrupamentoArquivos.Nenhum : AgrupamentoArquivos.Etapa,
    }, miniaturas);

    void AoEscolherModo(object sender, RoutedEventArgs e) => Mudar(exibicao, ReferenceEquals(sender, ItemModoMiniaturas));

    /// <summary>Marca (✓) nos três menus o que está escolhido.</summary>
    void SincronizarMenus()
    {
        ItemOrdemNome.IsChecked = exibicao.Ordenar == OrdemArquivos.Nome;
        ItemOrdemData.IsChecked = exibicao.Ordenar == OrdemArquivos.Data;
        ItemOrdemTipo.IsChecked = exibicao.Ordenar == OrdemArquivos.Tipo;
        ItemCrescente.IsChecked = !exibicao.Decrescente;
        ItemDecrescente.IsChecked = exibicao.Decrescente;
        ItemGrupoEtapa.IsChecked = exibicao.Agrupar == AgrupamentoArquivos.Etapa;
        ItemGrupoTipo.IsChecked = exibicao.Agrupar == AgrupamentoArquivos.Tipo;
        ItemGrupoData.IsChecked = exibicao.Agrupar == AgrupamentoArquivos.Data;
        ItemGrupoNenhum.IsChecked = exibicao.Agrupar == AgrupamentoArquivos.Nenhum;
        ItemModoLista.IsChecked = !miniaturas;
        ItemModoMiniaturas.IsChecked = miniaturas;
    }

    /// <summary>Monta o modo em uso com as linhas da ListaDetalhe: as três colunas ou a grade de miniaturas. Sem processo lido, nenhum
    /// dos dois aparece (fica a frase "Escolha um processo na lista.").</summary>
    void Distribuir()
    {
        var linhas = ListaDetalhe.ItemsSource as IReadOnlyList<LinhaDetalhe>;
        var hoje = DateOnly.FromDateTime(DateTime.Now);
        sincronizando = true;
        try
        {
            if (miniaturas)
            {
                foreach (var c in Colunas) c.ItemsSource = null;
                var grupos = OrganizacaoArquivos.Organizar(linhas ?? [], exibicao, hoje)
                    .Select(g => g.Titulo.Length == 0 ? g with { Titulo = OrganizacaoArquivos.TituloSemGrupo } : g)
                    .Select(g => new GrupoMiniaturas(g.Titulo, g.Itens, recolhidos.Contains(g.Titulo))).ToList();
                gruposDaGrade = grupos;
                fonteDaGrade = new CollectionViewSource { IsSourceGrouped = true, Source = new ObservableCollection<GrupoMiniaturas>(grupos) };
                GradeMiniaturas.ItemsSource = fonteDaGrade.View;
                TextoSemMiniaturas.Visibility = linhas is not null && grupos.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                GradeMiniaturas.ItemsSource = null;
                fonteDaGrade = null;
                gruposDaGrade = [];
                var (rel, desp, proj) = OrganizacaoArquivos.EmColunas(linhas ?? [], exibicao, hoje);
                static string Contagem(IEnumerable<ItemArquivo> itens)
                {
                    var total = itens.Count(item => item.Selecionavel);
                    return total == 1 ? "1 arquivo" : $"{total} arquivos";
                }
                TotalRelatorios.Text = Contagem(rel);
                TotalDespachos.Text = Contagem(desp);
                TotalProjeto.Text = Contagem(proj);
                ColunaRelatorios.ItemsSource = rel;
                ColunaDespachos.ItemsSource = desp;
                ColunaProjeto.ItemsSource = proj;
            }
            GradeColunas.Visibility = linhas is not null && !miniaturas ? Visibility.Visible : Visibility.Collapsed;
            CartaoMiniaturas.Visibility = linhas is not null && miniaturas ? Visibility.Visible : Visibility.Collapsed;
            RefletirEscolha();
        }
        finally { sincronizando = false; }
    }

    IEnumerable<ItemArquivo> ItensDe(ListViewBase visao) => ReferenceEquals(visao, GradeMiniaturas)
        ? gruposDaGrade.SelectMany(g => g.OfType<ItemArquivo>())
        : visao.ItemsSource as IEnumerable<ItemArquivo> ?? [];

    /// <summary>Mostra nas colunas (ou na grade) a linha escolhida na ListaDetalhe. Se ela não está à vista (o grupo dela foi
    /// recolhido), deixa de estar escolhida: nenhum comando age sobre um arquivo que não aparece.</summary>
    void RefletirEscolha()
    {
        var linha = ListaDetalhe.SelectedItem as LinhaDetalhe;
        var achou = false;
        foreach (var v in Visoes)
        {
            var item = linha is null ? null : ItensDe(v).FirstOrDefault(i => ReferenceEquals(i.Linha, linha));
            v.SelectedItem = item;
            achou |= item is not null;
        }
        if (linha is not null && !achou) ListaDetalhe.SelectedItem = null;
    }

    void AoEscolherNaVisao(object sender, SelectionChangedEventArgs e)
    {
        if (sincronizando || sender is not ListViewBase visao || visao.SelectedItem is null) return;
        sincronizando = true;
        try
        {
            var linha = (visao.SelectedItem as ItemArquivo)?.Linha;
            foreach (var outra in Visoes)
                if (!ReferenceEquals(outra, visao)) outra.SelectedItem = null;
            if (linha is null) visao.SelectedItem = null; // título de grupo ou aviso de "nenhum": não se escolhe
            ListaDetalhe.SelectedItem = linha;
        }
        finally { sincronizando = false; }
    }

    // ---------- duplo clique, teclado e menu de contexto ----------

    /// <summary>O bloco (ou a linha) da lista em que <paramref name="origem"/> está, subindo pela árvore visual.</summary>
    static SelectorItem? Recipiente(DependencyObject? origem)
    {
        for (var d = origem; d is not null and not ListViewBase; d = VisualTreeHelper.GetParent(d))
            if (d is SelectorItem s) return s;
        return null;
    }

    static ItemArquivo? ItemEm(DependencyObject? origem) => Recipiente(origem)?.Content as ItemArquivo;

    /// <summary>Enter abre o arquivo escolhido (as setas, Home, End e a tecla de menu já são da própria lista).</summary>
    void AoTeclar(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter || sender is not ListViewBase { SelectedItem: ItemArquivo { Selecionavel: true } }) return;
        e.Handled = true;
        AcaoPedida?.Invoke(AcaoArquivo.Abrir);
    }

    /// <summary>Botão direito (ou a tecla de menu, ou Shift+F10) num arquivo: escolhe o arquivo e mostra o menu dele.</summary>
    void AoPedirMenu(UIElement sender, ContextRequestedEventArgs e)
    {
        if (sender is not ListViewBase visao || Recipiente(e.OriginalSource as DependencyObject) is not { Content: ItemArquivo { Linha: { } linha } item } recipiente) return;
        visao.SelectedItem = item;
        var menu = new MenuFlyout();
        void Entrada(string texto, string glifo, AcaoArquivo acao, string? tecla = null)
        {
            var entrada = new MenuFlyoutItem { Text = texto, Icon = new FontIcon { Glyph = glifo, FontSize = 16 } };
            if (tecla is not null) entrada.KeyboardAcceleratorTextOverride = tecla;
            entrada.Click += (_, _) => AcaoPedida?.Invoke(acao);
            menu.Items.Add(entrada);
        }
        Entrada(linha.Tipo switch
        {
            TipoLinhaDetalhe.Relatorio => "Abrir relatório",
            TipoLinhaDetalhe.Analise => "Importar análise",
            _ => "Abrir arquivo",
        }, linha.Tipo == TipoLinhaDetalhe.Analise ? Glifos.ImportarAnalise : Glifos.Abrir, AcaoArquivo.Abrir, "Enter");
        Entrada("Mostrar no Explorador", Glifos.Explorador, AcaoArquivo.MostrarNoExplorador);
        menu.Items.Add(new MenuFlyoutSeparator());
        Entrada("Copiar caminho", Glifos.Copiar, AcaoArquivo.CopiarCaminho);
        UltimoMenuDeArquivo = menu;
        if (e.TryGetPosition(recipiente, out var ponto)) menu.ShowAt(recipiente, new FlyoutShowOptions { Position = ponto });
        else menu.ShowAt(recipiente);
        e.Handled = true;
    }

    // ---------- grupos da grade ----------

    /// <summary>Clique no cabeçalho de um grupo da grade: recolhe ou mostra os arquivos dele. A grade é montada de novo (um grupo vazio
    /// só com o cabeçalho), a rolagem volta para onde estava e o foco, para o cabeçalho clicado.</summary>
    void AoAlternarGrupo(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GrupoMiniaturas grupo) return;
        if (!recolhidos.Remove(grupo.Titulo)) recolhidos.Add(grupo.Titulo);
        var posicao = Descendentes<ScrollViewer>(GradeMiniaturas).FirstOrDefault()?.VerticalOffset ?? 0;
        var titulo = grupo.Titulo;
        Distribuir();
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (Descendentes<ScrollViewer>(GradeMiniaturas).FirstOrDefault() is { } rolagem)
                rolagem.ChangeView(null, Math.Min(posicao, rolagem.ScrollableHeight), null, true);
            DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
                Descendentes<Button>(GradeMiniaturas).FirstOrDefault(b => b.DataContext is GrupoMiniaturas g && g.Titulo == titulo)?.Focus(FocusState.Programmatic));
        });
    }

    static IEnumerable<T> Descendentes<T>(DependencyObject raiz) where T : DependencyObject
    {
        var n = VisualTreeHelper.GetChildrenCount(raiz);
        for (var i = 0; i < n; i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T t) yield return t;
            foreach (var d in Descendentes<T>(filho)) yield return d;
        }
    }
}
