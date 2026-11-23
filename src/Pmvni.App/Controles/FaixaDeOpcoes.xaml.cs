using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Pmvni.App;

/// <summary>Faixa de opções montada de uma <see cref="DefinicaoFaixa"/> (desenho §9.1): troca inteira com o módulo. Cada aba vira um
/// item do <see cref="SelectorBar"/>; os comandos da aba escolhida viram botões do <see cref="CommandBar"/>. Habilitado e ligado ficam
/// guardados por id e valem também para as abas que não estão à vista. Clicar avisa por <see cref="ComandoInvocado"/>.</summary>
public sealed partial class FaixaDeOpcoes : UserControl
{
    readonly Dictionary<string, bool> habilitado = new(StringComparer.Ordinal);
    readonly Dictionary<string, bool> ligado = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> dicas = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> rotulos = new(StringComparer.Ordinal);
    readonly Dictionary<string, MenuFlyoutItem> itensMenu = new(StringComparer.Ordinal);
    readonly Dictionary<string, AppBarButton> visiveis = new(StringComparer.Ordinal);
    readonly Dictionary<string, AppBarToggleButton> alternaveis = new(StringComparer.Ordinal);
    readonly Dictionary<string, MenuFlyout> menusDinamicos = new(StringComparer.Ordinal);
    readonly HashSet<string> marcados = new(StringComparer.Ordinal);
    DefinicaoFaixa? definicao;
    Button? primario;
    Button? secundario;
    bool montando;

    public FaixaDeOpcoes()
    {
        InitializeComponent();
        Barra.Loaded += (_, _) =>
        {
            if (Descendente(Barra, "MoreButton") is not Button mais) return;
            mais.Height = 32;
            mais.VerticalAlignment = VerticalAlignment.Center;
            mais.Padding = new Thickness(0);
            mais.HorizontalContentAlignment = HorizontalAlignment.Center;
            if (Descendente(mais, "EllipsisIcon") is FontIcon elipse)
            {
                elipse.Style = (Style)Application.Current.Resources["PmvniIconeAcao"];
                elipse.Glyph = Glifos.MaisOpcoes;
                elipse.FontSize = 16;
            }
            AutomationProperties.SetName(mais, "Mais comandos");
            Dicas.Aplicar(mais, "Mais comandos", "Abre os comandos da faixa que não cabem na barra.");
        };
    }

    public event Action<string>? ComandoInvocado;

    /// <summary>Os itens de um menu que a definição deixa vazio (ex.: Recentes), pedidos a quem monta a faixa cada vez que o menu abre:
    /// texto, valor (o caminho) e dica. Sem este provedor ou sem itens, o menu mostra "Nenhum item".</summary>
    public Func<string, IReadOnlyList<(string Texto, string Valor, string Dica)>>? ItensDinamicos { get; set; }

    /// <summary>Um item de menu dinâmico foi escolhido: id do menu e valor do item.</summary>
    public event Action<string, string>? ItemDinamicoEscolhido;

    /// <summary>A aba escolhida pelo usuário (nome), para a janela lembrar nos ajustes.</summary>
    public event Action<string>? AbaMudou;

    public DefinicaoFaixa? Definicao => definicao;

    public string? AbaAtual => definicao is null || Abas.SelectedItem is not SelectorBarItem i ? null : (string)i.Tag;

    /// <summary>Ids dos comandos da aba à vista, na ordem dos botões.</summary>
    public IReadOnlyList<string> IdsVisiveis => Barra.PrimaryCommands.OfType<FrameworkElement>().Select(e => (string)e.Tag).ToList();

    /// <summary>O id do botão primário à vista (o mesmo em todas as abas do módulo), ou null se o módulo não tem primário.</summary>
    public string? IdPrimario => primario?.Tag as string;

    /// <summary>O botão primário (o roteiro confere rótulo, estado e dica).</summary>
    internal Button? BotaoPrimario => primario;

    /// <summary>O rótulo à vista no botão primário ("" sem primário).</summary>
    public string RotuloPrimario => primario is null ? "" : AutomationProperties.GetName(primario);

    /// <summary>Rótulos compactos (só ícones) quando a janela é estreita.</summary>
    public bool Compacta
    {
        get => Barra.DefaultLabelPosition == CommandBarDefaultLabelPosition.Collapsed;
        set => Barra.DefaultLabelPosition = value ? CommandBarDefaultLabelPosition.Collapsed : CommandBarDefaultLabelPosition.Right;
    }

    public void Montar(DefinicaoFaixa f, int aba)
    {
        montando = true;
        try
        {
            definicao = f;
            Abas.Items.Clear();
            foreach (var a in f.Abas)
            {
                var itemAba = new SelectorBarItem { Text = a.Nome, Tag = a.Nome };
                AutomationProperties.SetName(itemAba, a.Nome);
                Dicas.Aplicar(itemAba, a.Nome, $"Mostra os comandos da aba {a.Nome}.");
                AjustarIndicador(itemAba);
                Abas.Items.Add(itemAba);
            }
            Abas.SelectedItem = Abas.Items[Math.Clamp(aba, 0, f.Abas.Count - 1)];
            MontarBotoes();
            MontarPrimario();
        }
        finally { montando = false; }
    }

    /// <summary>Escolhe a aba pelo nome (o roteiro e os ajustes lembrados usam).</summary>
    public void EscolherAba(string nome)
    {
        var item = Abas.Items.FirstOrDefault(i => (string)i.Tag == nome);
        if (item is not null) Abas.SelectedItem = item;
    }

    public void Habilitar(string id, bool sim)
    {
        var mudou = Habilitado(id) != sim;
        habilitado[id] = sim;
        if (primario is not null && primario.Tag as string == id) primario.IsEnabled = sim;
        if (secundario is not null && secundario.Tag as string == id) secundario.IsEnabled = sim;
        if (visiveis.TryGetValue(id, out var b)) b.IsEnabled = sim;
        if (alternaveis.TryGetValue(id, out var t)) t.IsEnabled = sim;
        if (itensMenu.TryGetValue(id, out var i)) i.IsEnabled = sim;
        // O motivo (terceira linha da dica) só aparece com o botão desabilitado.
        if (mudou) AtualizarDicas(id);
    }

    /// <summary>Troca o rótulo do botão (ex.: "Excluir item" ⇄ "Restaurar item"); vale também quando a aba voltar à vista.</summary>
    public void DefinirRotulo(string id, string texto)
    {
        if (rotulos.TryGetValue(id, out var r) && r == texto) return;
        rotulos[id] = texto;
        if (visiveis.TryGetValue(id, out var b))
        {
            b.Label = texto;
            AutomationProperties.SetName(b, texto);
            if (id == ComandosApp.InicioExcluirRestaurar && b.Icon is FontIcon icone)
                icone.Glyph = texto == "Restaurar item" ? Glifos.Restaurar : Glifos.Excluir;
        }
        if (alternaveis.TryGetValue(id, out var t)) { t.Label = texto; AutomationProperties.SetName(t, texto); }
        AtualizarDicas(id);
    }

    /// <summary>O rótulo atual do comando (o da definição, se nunca foi trocado).</summary>
    public string Rotulo(string id) =>
        rotulos.TryGetValue(id, out var r) ? r : definicao is null ? "" : Faixas.Comandos(definicao).FirstOrDefault(c => c.Id == id)?.Rotulo ?? "";

    /// <summary>Refaz os itens do menu dinâmico <paramref name="menu"/> agora e devolve o valor de cada um, na ordem (só existe com a aba
    /// do menu à vista; senão, lista vazia).</summary>
    public IReadOnlyList<string> AtualizarMenuDinamico(string menu)
    {
        if (!menusDinamicos.TryGetValue(menu, out var m)) return [];
        PreencherMenuDinamico(menu, m);
        return m.Items.OfType<MenuFlyoutItem>().Where(i => i.Tag is string).Select(i => (string)i.Tag).ToList();
    }

    /// <summary>O texto de cada item do menu dinâmico, como o usuário o lê.</summary>
    public IReadOnlyList<string> TextosDoMenuDinamico(string menu) =>
        menusDinamicos.TryGetValue(menu, out var m) ? m.Items.OfType<MenuFlyoutItem>().Where(i => i.Tag is string).Select(i => i.Text).ToList() : [];

    /// <summary>Marca (✓) ou desmarca um item de menu (ex.: o tema em uso); vale também quando o menu for montado de novo.</summary>
    public void MarcarItem(string id, bool sim)
    {
        if (sim) marcados.Add(id); else marcados.Remove(id);
        if (itensMenu.TryGetValue(id, out var i) && i is ToggleMenuFlyoutItem opcao) opcao.IsChecked = sim;
    }

    public bool Marcado(string id) => marcados.Contains(id);

    static FontIcon Icone(string glifo) => new()
    {
        Glyph = glifo,
        Style = (Style)Application.Current.Resources["PmvniIconeAcao"],
    };

    internal static string GlifoDoComando(string id, string glifo) => id switch
    {
        ComandosApp.ProcessosAbrirPasta => Glifos.AbrirPasta,
        ComandosApp.FigurasInserirRegiao => Glifos.Recortar,
        ComandosApp.RevisaoComparar => Glifos.Comparar,
        ComandosApp.InicioRestaurarOriginal => Glifos.Restaurar,
        ComandosApp.EmitirProcessoAprovado => Glifos.EmitirProcessoAprovado,
        ComandosApp.DespachosAbrirPdf => Glifos.Abrir,
        ComandosApp.DespachosNovo => Glifos.NovoDespacho,
        _ => glifo,
    };

    static string RotuloDoBotao(ComandoFaixa c) => c.Id switch
    {
        ComandosApp.ArquivoAbrir or ComandosApp.DespachosAbrir => "Abrir…",
        ComandosApp.ArquivoSalvarComo => "Salvar como…",
        ComandosApp.EmitirProcessoAprovado => "Emitir processo aprovado…",
        _ => c.Rotulo,
    };

    void PreencherMenuDinamico(string menu, MenuFlyout m)
    {
        m.Items.Clear();
        var itens = ItensDinamicos?.Invoke(menu) ?? [];
        foreach (var (texto, valor, dica) in itens)
        {
            var mi = new MenuFlyoutItem { Text = texto, Tag = valor };
            AutomationProperties.SetName(mi, texto);
            Dicas.Aplicar(mi, new TextoDica(texto, dica));
            mi.Click += (_, _) => ItemDinamicoEscolhido?.Invoke(menu, valor);
            m.Items.Add(mi);
        }
        if (itens.Count == 0)
        {
            var vazio = new MenuFlyoutItem { Text = "Nenhum item", IsEnabled = false };
            AutomationProperties.SetName(vazio, vazio.Text);
            Dicas.Aplicar(vazio, vazio.Text, "Não há opções para mostrar neste menu.");
            m.Items.Add(vazio);
        }
    }

    public bool Ligado(string id) => ligado.TryGetValue(id, out var l) && l;

    public bool Habilitado(string id) => !habilitado.TryGetValue(id, out var h) || h;

    public void Ligar(string id, bool sim)
    {
        ligado[id] = sim;
        if (alternaveis.TryGetValue(id, out var t)) t.IsChecked = sim;
        if (id == ComandosApp.InicioOriginal && visiveis.TryGetValue(id, out var original)) IndicarOriginal(original, sim);
    }

    /// <summary>O texto dinâmico do comando (ex.: por que ele está desabilitado). A dica mostra sempre nome, atalho e a descrição de
    /// <see cref="DicasComandos"/>; este texto entra como terceira linha enquanto o botão está desabilitado.</summary>
    public void DefinirDica(string id, string texto)
    {
        if (dicas.TryGetValue(id, out var d) && d == texto) return;
        dicas[id] = texto;
        AtualizarDicas(id);
    }

    public string Dica(string id) => dicas.TryGetValue(id, out var d) ? d : "";

    /// <summary>A dica do comando no formato do app: o nome à vista (o texto completo), o atalho do módulo, a descrição e, com o
    /// botão desabilitado, o motivo guardado por <see cref="DefinirDica"/>.</summary>
    internal TextoDica TextoDaDica(string id, string? nome = null)
    {
        var c = definicao is null ? null : Faixas.Comandos(definicao).FirstOrDefault(x => x.Id == id);
        nome ??= rotulos.TryGetValue(id, out var r) ? r : c?.TextoCompleto ?? id;
        var motivo = Habilitado(id) ? null : Dica(id) is { Length: > 0 } detalhe ? detalhe : "Indisponível no contexto atual.";
        return DicasComandos.DoComando(id, nome, definicao?.Modulo ?? Modulo.Relatorios, motivo);
    }

    /// <summary>Refaz a dica do comando onde ele está à vista (primário, botão da aba, item de menu).</summary>
    void AtualizarDicas(string id)
    {
        if (primario is not null && primario.Tag as string == id) Dicas.Aplicar(primario, TextoDaDica(id));
        if (secundario is not null && secundario.Tag as string == id) Dicas.Aplicar(secundario, TextoDaDica(id));
        if (visiveis.TryGetValue(id, out var b)) Dicas.Aplicar(b, TextoDaDica(id));
        if (alternaveis.TryGetValue(id, out var t)) Dicas.Aplicar(t, TextoDaDica(id));
        if (itensMenu.TryGetValue(id, out var i)) Dicas.Aplicar(i, TextoDaDica(id, i.Text));
    }

    /// <summary>A dica de cada botão à vista (o primário, os da aba e os itens dos menus deles), na ordem: o roteiro confere que todos
    /// têm título e descrição.</summary>
    internal IReadOnlyList<(string Id, TextoDica? Dica)> DicasVisiveis
    {
        get
        {
            var lista = new List<(string, TextoDica?)>();
            if (primario is not null) lista.Add(((string)primario.Tag, Dicas.De(primario)));
            if (secundario is not null) lista.Add(((string)secundario.Tag, Dicas.De(secundario)));
            foreach (var (id, mi) in itensDoPrimario) lista.Add((id, Dicas.De(mi)));
            foreach (var e in Barra.PrimaryCommands.OfType<FrameworkElement>())
            {
                lista.Add(((string)e.Tag, Dicas.De(e)));
                if (e is AppBarButton { Flyout: MenuFlyout m } && !menusDinamicos.ContainsKey((string)e.Tag))
                    foreach (var i in m.Items.OfType<MenuFlyoutItem>().Where(i => i.Tag is string))
                        lista.Add(((string)i.Tag, Dicas.De(i)));
            }
            return lista;
        }
    }

    /// <summary>O item <paramref name="id"/> do menu de um botão da aba à vista (não do primário), ou null (roteiro).</summary>
    internal MenuFlyoutItem? ItemDoMenuDaAba(string id) =>
        Barra.PrimaryCommands.OfType<AppBarButton>().Select(b => b.Flyout).OfType<MenuFlyout>()
            .SelectMany(m => m.Items.OfType<MenuFlyoutItem>()).FirstOrDefault(i => i.Tag as string == id);

    void AoMudarAba(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (montando || definicao is null) return;
        MontarBotoes();
        if (IsLoaded && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled)
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(Barra);
            var entrada = visual.Compositor.CreateScalarKeyFrameAnimation();
            entrada.InsertKeyFrame(0, .55f);
            entrada.InsertKeyFrame(1, 1);
            entrada.Duration = TimeSpan.FromMilliseconds(160);
            visual.StartAnimation("Opacity", entrada);
        }
        if (AbaAtual is { } nome) AbaMudou?.Invoke(nome);
    }

    void MontarBotoes()
    {
        Barra.PrimaryCommands.Clear();
        visiveis.Clear();
        alternaveis.Clear();
        itensMenu.Clear();
        menusDinamicos.Clear();
        // Os itens do menu do primário (Novo despacho ▾) ficam valendo em qualquer aba: Habilitar e DefinirDica os acham por id.
        foreach (var (id, mi) in itensDoPrimario) itensMenu[id] = mi;
        if (definicao is null || AbaAtual is not { } nome) return;
        var aba = definicao.Abas.First(a => a.Nome == nome);
        foreach (var c in aba.Comandos.OrderBy(c => AcaoPerigosa(c.Id))) Barra.PrimaryCommands.Add(Botao(c));
    }

    static bool AcaoPerigosa(string id) => id is ComandosApp.InicioExcluirRestaurar or ComandosApp.FigurasExcluir or ComandosApp.AcompanhamentoExcluir;

    readonly List<(string Id, MenuFlyoutItem Item)> itensDoPrimario = [];

    /// <summary>O botão primário do módulo (à esquerda dos comandos, em todas as abas): Button, ou DropDownButton com os itens do
    /// menu (Novo despacho ▾). Habilitado, dica e rótulo guardados por id valem para ele como para os da barra.</summary>
    /// <summary>Correções 2026-11-15: o traço da aba selecionada ocupa a largura do texto (o modelo do WinUI tem um traço de 4 px que a
    /// seleção estica 4 vezes, ScaleX = 4, a partir do centro): a largura do traço passa a ser 1/4 da largura do texto da aba.</summary>
    internal static void AjustarIndicador(SelectorBarItem aba)
    {
        aba.Loaded += (_, _) => PilulaNaLarguraDoTexto(aba);
        aba.SizeChanged += (_, _) => PilulaNaLarguraDoTexto(aba);
    }

    static void PilulaNaLarguraDoTexto(SelectorBarItem aba)
    {
        if (Descendente(aba, "PART_SelectionVisual") is not FrameworkElement pilula || Descendente(aba, "PART_TextVisual") is not FrameworkElement texto) return;
        if (texto.ActualWidth <= 0) return;
        // Só com diferença visível (2026-11-16): frações de pixel ao mudar de monitor não realimentam o layout.
        var nova = Math.Max(4, Math.Round(texto.ActualWidth / 4));
        if (double.IsNaN(pilula.Width) || Math.Abs(pilula.Width - nova) >= 1) pilula.Width = nova;
    }

    static DependencyObject? Descendente(DependencyObject raiz, string nome)
    {
        for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(raiz, i);
            if (filho is FrameworkElement { Name: var n } && n == nome) return filho;
            if (Descendente(filho, nome) is { } achado) return achado;
        }
        return null;
    }
    void MontarPrimario()
    {
        // Só sai o registro que ainda aponta para o item do primário antigo: o mesmo id pode já ser de um item do menu da aba
        // (Novo despacho ▾ na Página Inicial de Processos/Relatórios depois de passar por Despachos), que precisa continuar recebendo Habilitar e dica.
        foreach (var (id, mi) in itensDoPrimario)
            if (itensMenu.TryGetValue(id, out var atual) && ReferenceEquals(atual, mi)) itensMenu.Remove(id);
        itensDoPrimario.Clear();
        primario = secundario = null;
        VagaPrimario.Content = null;
        if (definicao?.Primario is not { } c) { VagaPrimario.Visibility = Visibility.Collapsed; return; }
        VagaPrimario.Visibility = Visibility.Visible;
        primario = BotaoDaVaga(c, destaque: true);
        if (definicao.Secundario is { } s2)
        {
            secundario = BotaoDaVaga(s2, destaque: false);
            VagaPrimario.Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { primario, secundario } };
        }
        else VagaPrimario.Content = primario;
    }

    Button BotaoDaVaga(ComandoFaixa c, bool destaque)
    {
        var rotulo = rotulos.TryGetValue(c.Id, out var r) ? r : RotuloDoBotao(c);
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        conteudo.Children.Add(Icone(GlifoDoComando(c.Id, c.Glifo)));
        // 12 px no próprio TextBlock: o estilo implícito de TextBlock fixa 14 e não herda o FontSize do botão.
        conteudo.Children.Add(new TextBlock { Text = rotulo, Style = (Style)Application.Current.Resources["PmvniRotuloFaixa"] });
        Button b;
        if (c.Tipo == TipoComando.Menu && c.Itens is { Count: > 0 } itens)
        {
            var menu = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedLeft };
            foreach (var i in itens)
            {
                var mi = CriarItemMenu(c, i);
                menu.Items.Add(mi);
                itensDoPrimario.Add((i.Id, mi));
            }
            b = new DropDownButton { Content = conteudo, Flyout = menu };
        }
        else
        {
            b = new Button { Content = conteudo };
            b.Click += (_, _) => ComandoInvocado?.Invoke(c.Id);
        }
        if (destaque)
        {
            b.Style = (Style)Application.Current.Resources[b is DropDownButton ? "PmvniFaixaMenuPrimarioStyle" : "PmvniBotaoPrimarioStyle"];
            Brilho.SetAtivo(b, false);
        }
        else
        {
            b.Style = (Style)Application.Current.Resources["PmvniComandoFaixa"];
        }
        b.Tag = c.Id;
        // Rótulo de 12 px, o mesmo dos AppBarButton com rótulo à direita: todos os comandos da faixa com o mesmo tamanho de texto.
        b.FontSize = (double)Application.Current.Resources["PmvniTamanhoRotuloFaixa"];
        b.MinHeight = b.Height = 32;
        b.Padding = new Thickness(10, 0, 10, 0);
        b.CornerRadius = (CornerRadius)Application.Current.Resources["PmvniRaioFaixa"];
        b.VerticalAlignment = VerticalAlignment.Center;
        b.VerticalContentAlignment = VerticalAlignment.Center;
        b.IsEnabled = Habilitado(c.Id);
        AutomationProperties.SetName(b, c.Descricao ?? c.Rotulo);
        Dicas.Aplicar(b, TextoDaDica(c.Id));
        return b;
    }

    /// <summary>O atalho do comando no módulo da faixa (o mapa do leitor na Legislação, auditoria M1).</summary>
    string? AtalhoLegivel(string id) =>
        Atalhos.Tecla(id, definicao?.Modulo ?? Modulo.Relatorios) is { } tecla ? Atalhos.Legivel(tecla) : null;

    MenuFlyoutItem CriarItemMenu(ComandoFaixa pai, ComandoFaixa comando)
    {
        var escolha = pai.Id is ComandosApp.ExibirTema or ComandosApp.DespachosFiltro or ComandosApp.DespachosAgrupar
            or ComandosApp.DespachosOrdenar or ComandosApp.ProcessosOrdenar or ComandosApp.LegislacaoEscopo;
        MenuFlyoutItem item = escolha
            ? new ToggleMenuFlyoutItem { IsChecked = Marcado(comando.Id) }
            : new MenuFlyoutItem();
        item.Text = comando.Rotulo;
        item.Tag = comando.Id;
        item.IsEnabled = Habilitado(comando.Id);
        if (comando.Glifo.Length > 0) item.Icon = Icone(GlifoDoComando(comando.Id, comando.Glifo));
        if (AtalhoLegivel(comando.Id) is { } atalho) item.KeyboardAcceleratorTextOverride = atalho;
        AutomationProperties.SetName(item, comando.Rotulo);
        Dicas.Aplicar(item, TextoDaDica(comando.Id, comando.Rotulo));
        item.Click += (_, _) =>
        {
            // A escolha é atualizada pelo módulo quando ele aceita o comando.
            if (item is ToggleMenuFlyoutItem opcao) opcao.IsChecked = Marcado(comando.Id);
            ComandoInvocado?.Invoke(comando.Id);
        };
        itensMenu[comando.Id] = item;
        return item;
    }

    static void IndicarOriginal(AppBarButton botao, bool ativo)
    {
        // O visto sinaliza a seleção atual, assim como nos menus de opções.
        if (botao.Icon is FontIcon icone) icone.Glyph = ativo ? Glifos.Marca : Glifos.Original;
        AutomationProperties.SetItemStatus(botao, ativo ? "Exibindo o original" : "Exibindo a revisão");
    }

    internal double DesalinhamentoOriginal()
    {
        var comandos = new[] { ComandosApp.InicioEditarTexto, ComandosApp.InicioOriginal, ComandosApp.InicioRestaurarOriginal };
        var medidas = comandos.Select(id =>
        {
            var botao = visiveis[id];
            var texto = (FrameworkElement)Descendente(botao, "TextLabel")!;
            var y = texto.TransformToVisual(Barra).TransformPoint(new Windows.Foundation.Point()).Y;
            var topo = botao.TransformToVisual(Barra).TransformPoint(new Windows.Foundation.Point()).Y;
            var icone = (FrameworkElement)botao.Icon;
            var yIcone = icone.TransformToVisual(Barra).TransformPoint(new Windows.Foundation.Point()).Y;
            return (y, topo, botao.ActualHeight, yIcone);
        }).ToArray();
        return new[] { medidas.Max(x => x.y) - medidas.Min(x => x.y), medidas.Max(x => x.topo) - medidas.Min(x => x.topo),
            medidas.Max(x => x.ActualHeight) - medidas.Min(x => x.ActualHeight), medidas.Max(x => x.yIcone) - medidas.Min(x => x.yIcone),
            medidas.Max(x => Math.Abs(x.yIcone + 8 - x.topo - x.ActualHeight / 2)) }.Max();
    }

    static void AlinharRotulo(Control controle)
    {
        controle.Loaded += (_, _) =>
        {
            // Em LabelOnRight o modelo pode medir 42 DIPs dentro de um comando de 32.
            // Centraliza só o conteúdo excedente. O fundo e a área clicável continuam no botão.
            if (Descendente(controle, "ContentRoot") is FrameworkElement raiz)
            {
                var ajuste = new Microsoft.UI.Xaml.Media.TranslateTransform();
                raiz.RenderTransform = ajuste;
                void Centralizar() => ajuste.Y = Math.Min(0, (controle.ActualHeight - raiz.ActualHeight) / 2);
                void AoRedimensionar(object sender, SizeChangedEventArgs args) => Centralizar();
                void AoDescarregar(object sender, RoutedEventArgs args)
                {
                    raiz.SizeChanged -= AoRedimensionar;
                    controle.SizeChanged -= AoRedimensionar;
                    controle.Unloaded -= AoDescarregar;
                }
                raiz.SizeChanged += AoRedimensionar;
                controle.SizeChanged += AoRedimensionar;
                controle.Unloaded += AoDescarregar;
                Centralizar();
            }
            if (Descendente(controle, "TextLabel") is TextBlock texto)
            {
                texto.FontSize = (double)Application.Current.Resources["PmvniTamanhoRotuloFaixa"];
                texto.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { Y = -3 };
            }
        };
    }

    ICommandBarElement Botao(ComandoFaixa c)
    {
        var icone = Icone(GlifoDoComando(c.Id, c.Glifo));
        if (c.Id == ComandosApp.InicioExcluirRestaurar && rotulos.TryGetValue(c.Id, out var acao) && acao == "Restaurar item")
            icone.Glyph = Glifos.Restaurar;
        var rotulo = rotulos.TryGetValue(c.Id, out var r) ? r : RotuloDoBotao(c);
        var atalho = AtalhoLegivel(c.Id);
        var dica = TextoDaDica(c.Id);
        // O nome acessível é o texto completo (o rótulo pode ter sido encurtado para caber na faixa).
        var nome = rotulos.ContainsKey(c.Id) ? rotulo : c.Descricao ?? c.Rotulo;
        if (c.Tipo == TipoComando.Alternar && c.Id != ComandosApp.InicioOriginal)
        {
            var t = new AppBarToggleButton { Label = rotulo, Icon = icone, Tag = c.Id, IsEnabled = Habilitado(c.Id), IsChecked = ligado.TryGetValue(c.Id, out var l) && l, Height = 32, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(t, nome);
            if (atalho is not null) t.KeyboardAcceleratorTextOverride = atalho;
            t.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
            Pilulas.Estilizar(t);
            AlinharRotulo(t);
            Dicas.Aplicar(t, dica);
            t.Click += (_, _) => ComandoInvocado?.Invoke(c.Id);
            alternaveis[c.Id] = t;
            return t;
        }
        // 3.2.2: 32 px de altura, a mesma dos botões do início da faixa: o hover de todos os comandos tem o mesmo tamanho.
        var b = new AppBarButton { Label = rotulo, Icon = icone, Tag = c.Id, IsEnabled = Habilitado(c.Id), Height = 32, VerticalAlignment = VerticalAlignment.Center };
        b.CornerRadius = (CornerRadius)Application.Current.Resources["PmvniRaioFaixa"];
        // Correções 2026-11-15: o estado "rótulo à direita" do WinUI anima a margem do texto para 16/10 (StaticResource, não dá para
        // trocar pelo recurso); o texto sobe 3 px por transformação (a animação não mexe nela) e fica na linha do ícone e do primário.
        AlinharRotulo(b);
        if (atalho is not null) b.KeyboardAcceleratorTextOverride = atalho;
        // A dica automática do atalho (só "Ctrl+O") não pode tomar o lugar da dica do app, que já traz o atalho no título.
        b.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
        AutomationProperties.SetName(b, nome);
        Dicas.Aplicar(b, dica);
        if (c.Tipo == TipoComando.Menu && c.Itens is { } itens)
        {
            var menu = new MenuFlyout();
            if (itens.Count == 0)
            {
                menusDinamicos[c.Id] = menu;
                menu.Opening += (_, _) => PreencherMenuDinamico(c.Id, menu);
                PreencherMenuDinamico(c.Id, menu);
            }
            foreach (var i in itens) menu.Items.Add(CriarItemMenu(c, i));
            b.Flyout = menu;
        }
        else b.Click += (_, _) => ComandoInvocado?.Invoke(c.Id);
        if (c.Id == ComandosApp.InicioOriginal) IndicarOriginal(b, Ligado(c.Id));
        visiveis[c.Id] = b;
        return b;
    }
}
