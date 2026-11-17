using System.Globalization;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Pmvni.Composicao.Figuras;
using Pmvni.Revisao;
using Windows.System;
using Windows.UI;

namespace Pmvni.App;

/// <summary>Ferramenta ativa do editor de figura.</summary>
internal enum FerramentaFigura { Selecionar, Regiao, Seta, Texto, Remover }

/// <summary>Editor de marcações de uma figura (Etapa 4, especificação §9): barra Inserir região | Selecionar | Marcar região | Seta | Texto | Desfazer/Refazer | zoom | Concluir sobre a
/// imagem, a camada de marcações desenhada em XAML por cima da imagem (que nunca é alterada) e, ao lado, os textos da figura e a lista das
/// marcações com a legenda e o vínculo aos itens. Cada gesto concluído vira um comando da janela principal (histórico, desfazer/refazer,
/// recomposição das folhas); durante o arrasto só a tela do editor muda. A geometria é a mesma do SVG do relatório
/// (<see cref="GeometriaFigura"/>), então o que se vê aqui é o que sai no A4 e no PDF.</summary>
internal sealed class JanelaFigura : Window
{
    static readonly Color Vermelho = Color.FromArgb(255, 0xC0, 0, 0);

    readonly IHostFiguras host;
    readonly SessaoEdicao sessao;
    readonly string figuraId;

    // ---------- controles ----------
    readonly Grid raiz = new();
    readonly ScrollViewer rolagem = new() { ZoomMode = ZoomMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinZoomFactor = .1f, MaxZoomFactor = 8 };
    readonly Grid superficie = new() { Background = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Image imagem = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
    readonly Canvas camada = new() { Background = new SolidColorBrush(Colors.Transparent) };
    readonly Dictionary<FerramentaFigura, AppBarToggleButton> botoes = new();
    readonly InfoBar avisoGravadas = new() { Severity = InfoBarSeverity.Informational, IsClosable = false, Title = "Marcações gravadas na imagem" };
    readonly InfoBar avisoErro = new() { Severity = InfoBarSeverity.Warning, IsClosable = true, Title = "A alteração não foi aplicada" };
    readonly TextBox caixaPrancha = new() { Header = "Prancha" };
    readonly TextBox caixaTitulo = new() { Header = "Título da figura" };
    readonly TextBox caixaFonte = new() { Header = "Fonte" };
    readonly ListView listaMarcacoes = new() { SelectionMode = ListViewSelectionMode.Single, MaxHeight = 220 };
    readonly StackPanel painelMarcacao = new() { Spacing = 8 };
    readonly TextBlock tituloMarcacao = new() { Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] };
    readonly TextBox caixaElemento = new() { Header = "O que é (negrito na legenda)" };
    readonly TextBox caixaTexto = new() { Header = "Solução ou o que foi conferido", AcceptsReturn = false, TextWrapping = TextWrapping.Wrap };
    readonly ListView listaItens = new() { SelectionMode = ListViewSelectionMode.Multiple, MaxHeight = 220, Header = "Itens ligados a esta marcação" };
    readonly Button botaoRemover = new() { Content = "Remover marcação" };
    readonly TextBlock estado = new() { Opacity = .8, TextWrapping = TextWrapping.Wrap };
    readonly AppBarButton botaoDesfazer = new() { Icon = new SymbolIcon(Symbol.Undo), Label = "Desfazer" };
    readonly AppBarButton botaoRefazer = new() { Icon = new SymbolIcon(Symbol.Redo), Label = "Refazer" };
    readonly AppBarButton botaoInserir = new() { Icon = new SymbolIcon(Symbol.Add), Label = "Inserir região da prancha" };
    readonly TextBlock textoZoom = new() { Text = "100%", VerticalAlignment = VerticalAlignment.Center, MinWidth = 52, TextAlignment = TextAlignment.Center };

    // ---------- estado ----------
    FiguraRevisao? figura;
    MedidasFigura? medidas;
    string? imagemCarregada;
    string? selecionada;
    FerramentaFigura ferramenta = FerramentaFigura.Selecionar;
    bool preenchendo, aplicando, fechada;
    readonly SemaphoreSlim filaEdicoes = new(1, 1);
    int edicoesPendentes;
    // arrasto em curso
    readonly GestoFigura gesto = new();
    Rectangle? retanguloNovo;

    public JanelaFigura(IHostFiguras host, SessaoEdicao sessao, string figuraId)
    {
        this.host = host;
        this.sessao = sessao;
        this.figuraId = figuraId;
        Title = "Editar figura — Editor de Relatórios PMVNI";
        Montar();
        Content = raiz;
        raiz.RequestedTheme = host.TemaAtual;
        sessao.Revisao.Registrado += AoRegistrar;
        Closed += (_, _) => { fechada = true; sessao.Revisao.Registrado -= AoRegistrar; };
        try { AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 900)); } catch (Exception) { /* tamanho inicial é só conveniência */ }
        Atualizar();
    }

    /// <summary>Id da figura editada.</summary>
    public string Figura => figuraId;
    internal bool PertenceASessao(SessaoEdicao atual) => sessao == atual;

    void AoRegistrar(RegistroHistorico _) => DispatcherQueue.TryEnqueue(() => { if (!aplicando) Atualizar(); });

    // ---------- montagem ----------

    void Montar()
    {
        raiz.Style = (Style)Application.Current.Resources["PmvniFundoJanelaStyle"]; // segue o RequestedTheme da janela, não o do Windows
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });

        // Barra de ferramentas
        // Uma barra só, em grupos: origem (nova figura do PDF) | ferramentas de marcação | histórico | zoom | Concluir.
        // "Remover" deixou de ser ferramenta: a marcação selecionada sai pelo botão do painel ou pela tecla Del (eram três caminhos).
        var cabecalho = new Grid();
        cabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var barra = new CommandBar { DefaultLabelPosition = CommandBarDefaultLabelPosition.Right, IsDynamicOverflowEnabled = true, HorizontalAlignment = HorizontalAlignment.Left };
        Dicas.Aplicar(botaoInserir, "Inserir região da prancha", "Recorta um PDF do projeto e insere uma nova figura depois desta.");
        botaoInserir.Click += (_, _) => host.AbrirRecortePrancha(figuraId);
        barra.PrimaryCommands.Add(botaoInserir);
        barra.PrimaryCommands.Add(new AppBarSeparator());
        Botao(barra, FerramentaFigura.Selecionar, "", "Selecionar", "Selecionar, mover e redimensionar marcações (arraste as alças)");
        Botao(barra, FerramentaFigura.Regiao, "", "Marcar região", "Arraste um retângulo sobre o elemento: caixa vermelha, seta e número");
        Botao(barra, FerramentaFigura.Seta, "", "Seta", "Clique no ponto que a seta deve indicar: seta e número");
        Botao(barra, FerramentaFigura.Texto, "", "Texto", "Clique onde o texto deve ficar e escreva no painel ao lado");
        barra.PrimaryCommands.Add(new AppBarSeparator());
        botaoDesfazer.Click += async (_, _) => await HistoricoAsync(false);
        botaoRefazer.Click += async (_, _) => await HistoricoAsync(true);
        barra.PrimaryCommands.Add(botaoDesfazer);
        barra.PrimaryCommands.Add(botaoRefazer);
        var menos = new AppBarButton { Icon = new SymbolIcon(Symbol.ZoomOut), Label = "Diminuir", LabelPosition = CommandBarLabelPosition.Collapsed };
        var mais = new AppBarButton { Icon = new SymbolIcon(Symbol.ZoomIn), Label = "Aumentar", LabelPosition = CommandBarLabelPosition.Collapsed };
        var ajustar = new AppBarButton { Icon = new FontIcon { Glyph = "" }, Label = "Ajustar" };
        Dicas.Aplicar(botaoDesfazer, "Desfazer", "Desfaz a última alteração do relatório.", "Ctrl+Z");
        Dicas.Aplicar(botaoRefazer, "Refazer", "Refaz a última alteração desfeita do relatório.", "Ctrl+Y");
        Dicas.Aplicar(menos, "Diminuir", "Diminui o zoom da figura.");
        Dicas.Aplicar(mais, "Aumentar", "Aumenta o zoom da figura.");
        Dicas.Aplicar(ajustar, "Ajustar", "Ajusta o zoom para a figura inteira caber na janela.");
        menos.Click += (_, _) => rolagem.ChangeView(null, null, Math.Max(rolagem.MinZoomFactor, rolagem.ZoomFactor / 1.25f));
        mais.Click += (_, _) => rolagem.ChangeView(null, null, Math.Min(rolagem.MaxZoomFactor, rolagem.ZoomFactor * 1.25f));
        ajustar.Click += (_, _) => AjustarZoom();
        barra.PrimaryCommands.Add(new AppBarSeparator());
        barra.PrimaryCommands.Add(menos);
        barra.PrimaryCommands.Add(new AppBarElementContainer { Content = textoZoom, VerticalContentAlignment = VerticalAlignment.Center });
        barra.PrimaryCommands.Add(mais);
        barra.PrimaryCommands.Add(ajustar);
        var concluir = new Button { Content = "Concluir", Style = (Style)Application.Current.Resources["AccentButtonStyle"], Margin = new Thickness(8, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        Dicas.Aplicar(concluir, "Concluir", "Fecha o editor. Cada alteração já foi aplicada ao relatório; desfaça com Ctrl+Z na janela principal.");
        concluir.Click += (_, _) => Close();
        Grid.SetColumn(concluir, 1);
        cabecalho.Children.Add(barra);
        cabecalho.Children.Add(concluir);
        Grid.SetColumnSpan(cabecalho, 2);
        raiz.Children.Add(cabecalho);
        estado.Margin = new Thickness(12, 8, 12, 8);
        Grid.SetRow(estado, 2);
        Grid.SetColumnSpan(estado, 2);
        raiz.Children.Add(estado);

        // Superfície: imagem + camada de marcações (mesmas coordenadas: pixels da figura composta)
        superficie.Children.Add(imagem);
        superficie.Children.Add(camada);
        rolagem.Content = superficie;
        rolagem.Style = (Style)Application.Current.Resources["PmvniRolagemFundoStyle"];
        Grid.SetRow(rolagem, 1);
        raiz.Children.Add(rolagem);
        // As alças têm tamanho fixo na tela: redesenha ao terminar uma mudança de zoom.
        rolagem.ViewChanged += (_, e) =>
        {
            textoZoom.Text = rolagem.ZoomFactor.ToString("P0", CultureInfo.CurrentCulture);
            if (!e.IsIntermediate && !gesto.Ativo) Desenhar();
        };
        camada.PointerPressed += AoPressionar;
        camada.PointerMoved += AoMover;
        camada.PointerReleased += AoSoltar;
        camada.PointerCanceled += (_, _) => CancelarArrasto();
        camada.PointerCaptureLost += (_, _) => { if (gesto.Ativo) CancelarArrasto(); };

        // Painel lateral
        var painel = new StackPanel { Spacing = 10, Padding = new Thickness(14) };
        painel.Children.Add(avisoGravadas);
        painel.Children.Add(avisoErro);
        painel.Children.Add(caixaPrancha);
        painel.Children.Add(caixaTitulo);
        painel.Children.Add(caixaFonte);
        LigarTextoFigura(caixaPrancha, "prancha");
        LigarTextoFigura(caixaTitulo, "titulo");
        LigarTextoFigura(caixaFonte, "fonte");
        painel.Children.Add(new TextBlock { Text = "Marcações", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], Margin = new Thickness(0, 6, 0, 0) });
        painel.Children.Add(new TextBlock
        {
            Text = "Os números se refazem sozinhos ao incluir, remover ou mover (margem esquerda de cima para baixo, depois a direita).",
            TextWrapping = TextWrapping.Wrap, Opacity = .75,
        });
        listaMarcacoes.SelectionChanged += (_, _) =>
        {
            if (preenchendo) return;
            selecionada = (listaMarcacoes.SelectedItem as FrameworkElement)?.Tag as string;
            Desenhar();
            PreencherMarcacao();
        };
        painel.Children.Add(listaMarcacoes);
        painelMarcacao.Children.Add(tituloMarcacao);
        painelMarcacao.Children.Add(caixaElemento);
        painelMarcacao.Children.Add(caixaTexto);
        painelMarcacao.Children.Add(listaItens);
        painelMarcacao.Children.Add(botaoRemover);
        LigarLegenda(caixaElemento, elemento: true);
        LigarLegenda(caixaTexto, elemento: false);
        listaItens.SelectionChanged += async (_, _) =>
        {
            if (preenchendo || selecionada is null) return;
            var ids = listaItens.SelectedItems.OfType<FrameworkElement>().Select(e => (string)e.Tag).ToList();
            await AplicarAsync(new VincularItens(figuraId, selecionada, ids));
        };
        botaoRemover.Click += async (_, _) => { if (selecionada is { } s) await AplicarAsync(new ExcluirMarcacao(figuraId, s)); };
        painel.Children.Add(painelMarcacao);
        var lateral = new ScrollViewer { Content = painel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(lateral, 1);
        Grid.SetColumn(lateral, 1);
        raiz.Children.Add(lateral);

        // Teclado: Delete remove; Esc volta a Selecionar; Ctrl+Z / Ctrl+Y.
        raiz.KeyDown += async (_, e) =>
        {
            if (FocoEmTexto()) return;
            if (e.Key == VirtualKey.Delete && selecionada is { } s && figura?.Buscar(s) is { Gravada: false } && !sessao.SomenteLeitura) { e.Handled = true; await AplicarAsync(new ExcluirMarcacao(figuraId, s)); }
            else if (e.Key == VirtualKey.Escape) { e.Handled = true; Escolher(FerramentaFigura.Selecionar); }
        };
        var desfazer = new KeyboardAccelerator { Key = VirtualKey.Z, Modifiers = VirtualKeyModifiers.Control };
        desfazer.Invoked += async (_, a) => { if (FocoEmTexto()) return; a.Handled = true; await HistoricoAsync(false); };
        var refazer = new KeyboardAccelerator { Key = VirtualKey.Y, Modifiers = VirtualKeyModifiers.Control };
        refazer.Invoked += async (_, a) => { if (FocoEmTexto()) return; a.Handled = true; await HistoricoAsync(true); };
        raiz.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden; // sem a dica automática "Ctrl+Z" solta sobre a janela
        raiz.KeyboardAccelerators.Add(desfazer);
        raiz.KeyboardAccelerators.Add(refazer);
        Escolher(FerramentaFigura.Selecionar);
    }

    static StackPanel Rotulo(string glifo, string texto)
    {
        var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        s.Children.Add(new FontIcon { Glyph = glifo, FontSize = 16 });
        s.Children.Add(new TextBlock { Text = texto });
        return s;
    }

    static void AutomationNome(FrameworkElement e, string nome) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(e, nome);

    void Botao(CommandBar barra, FerramentaFigura f, string glifo, string texto, string dica)
    {
        var b = new AppBarToggleButton { Icon = new FontIcon { Glyph = glifo }, Label = texto };
        Dicas.Aplicar(b, texto, dica.EndsWith('.') ? dica : dica + ".");
        AutomationNome(b, texto);
        b.Click += (_, _) => Escolher(f);
        botoes[f] = b;
        barra.PrimaryCommands.Add(b);
    }

    void Escolher(FerramentaFigura f)
    {
        CancelarArrasto();
        ferramenta = f;
        foreach (var (k, b) in botoes) b.IsChecked = k == f;
        estado.Text = f switch
        {
            FerramentaFigura.Regiao => "Marcar região: arraste um retângulo sobre o elemento.",
            FerramentaFigura.Seta => "Seta: clique no ponto a indicar.",
            FerramentaFigura.Texto => "Texto: clique onde o texto deve ficar.",
            FerramentaFigura.Remover => "Remover: clique na marcação a tirar.",
            _ => "Selecionar: clique numa marcação; arraste o número, a caixa, os cantos ou a ponta da seta.",
        };
    }

    void LigarTextoFigura(TextBox caixa, string campo)
    {
        async Task Gravar()
        {
            if (preenchendo || figura is null || figura.TextoBase(campo) == caixa.Text) return;
            await AplicarAsync(new DefinirTextoFigura(figuraId, campo, caixa.Text));
        }
        caixa.LostFocus += async (_, _) => await Gravar();
        caixa.KeyDown += async (_, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; await Gravar(); } };
    }

    void LigarLegenda(TextBox caixa, bool elemento)
    {
        async Task Gravar()
        {
            if (preenchendo || caixa.Tag is not string id || figura?.Buscar(id) is not { } m) return;
            var atual = elemento ? m.Elemento : m.Texto;
            if (atual == caixa.Text) return;
            await AplicarAsync(elemento ? new DefinirLegenda(figuraId, m.Id, caixa.Text, null) : new DefinirLegenda(figuraId, m.Id, null, caixa.Text));
        }
        caixa.LostFocus += async (_, _) => await Gravar();
        caixa.KeyDown += async (_, e) => { if (e.Key == VirtualKey.Enter) { e.Handled = true; await Gravar(); } };
    }

    // ---------- estado vindo do projeto ----------

    /// <summary>Relê a figura do projeto (depois de qualquer comando, aqui ou na janela principal) e redesenha.</summary>
    void Atualizar()
    {
        if (fechada || edicoesPendentes > 0) return;
        if (host.Sessao != sessao) { Close(); return; }
        var p = sessao.Revisao.Projeto;
        var camadaFiguras = p.Figuras ?? CamadaFiguras.Inicial(p);
        figura = camadaFiguras.FirstOrDefault(f => f.Id == figuraId);
        var disponivel = figura is { Incluida: true };
        var somenteLeitura = sessao.SomenteLeitura || !disponivel;
        foreach (var b in botoes.Values) b.IsEnabled = !somenteLeitura;
        botaoInserir.IsEnabled = !sessao.SomenteLeitura;
        caixaPrancha.IsReadOnly = caixaTitulo.IsReadOnly = caixaFonte.IsReadOnly = caixaElemento.IsReadOnly = caixaTexto.IsReadOnly = somenteLeitura;
        botaoDesfazer.IsEnabled = !sessao.SomenteLeitura && sessao.Revisao.PodeDesfazer;
        botaoRefazer.IsEnabled = !sessao.SomenteLeitura && sessao.Revisao.PodeRefazer;
        if (figura is null || !figura.Incluida)
        {
            CancelarArrasto();
            estado.Text = "Esta figura não está mais no relatório (foi excluída ou desfeita).";
            camada.Children.Clear();
            imagem.Source = null;
            imagemCarregada = null;
            medidas = null;
            selecionada = null;
            preenchendo = true;
            try { listaMarcacoes.Items.Clear(); }
            finally { preenchendo = false; }
            painelMarcacao.Visibility = Visibility.Collapsed;
            avisoGravadas.IsOpen = false;
            return;
        }
        Title = MotorFigurasRotulo(p) + " — editar marcações";
        try
        {
            if (imagemCarregada != figura.Imagem) CarregarImagem(figura.Imagem);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            imagem.Source = null;
            imagemCarregada = null;
            medidas = null;
            camada.Children.Clear();
            avisoErro.Title = "Não foi possível carregar a imagem";
            avisoErro.Message = e.Message;
            avisoErro.IsOpen = true;
        }
        if (selecionada is not null && figura.Buscar(selecionada) is null) selecionada = null;
        var gravadas = figura.Marcacoes.Count(m => m.Gravada);
        avisoGravadas.IsOpen = gravadas > 0;
        avisoGravadas.Message = gravadas == 1
            ? "O número 1 veio desenhado na imagem pelo kit: só a legenda dele é editável. As marcações novas ficam numa camada por cima e podem ser movidas, redimensionadas e removidas."
            : "Os números 1 a " + gravadas.ToString(CultureInfo.InvariantCulture) + " vieram desenhados na imagem pelo kit: só as legendas deles são editáveis. As marcações novas ficam numa camada por cima e podem ser movidas, redimensionadas e removidas.";
        preenchendo = true;
        try
        {
            AtualizarTexto(caixaPrancha, figura.TextoBase("prancha"));
            AtualizarTexto(caixaTitulo, figura.TextoBase("titulo"));
            AtualizarTexto(caixaFonte, figura.TextoBase("fonte"));
            PreencherLista();
        }
        finally { preenchendo = false; }
        PreencherMarcacao();
        Desenhar();
    }

    static void AtualizarTexto(TextBox caixa, string texto)
    {
        // Uma recomposição de outra alteração não pode apagar o campo que ainda está sendo digitado.
        if (caixa.FocusState == FocusState.Unfocused && caixa.Text != texto) caixa.Text = texto;
    }

    bool FocoEmTexto()
    {
        for (var alvo = FocusManager.GetFocusedElement(raiz.XamlRoot) as DependencyObject; alvo is not null; alvo = VisualTreeHelper.GetParent(alvo))
            if (alvo is TextBox) return true;
        return false;
    }

    string MotorFigurasRotulo(Projeto p)
    {
        var n = 0;
        foreach (var f in (p.Figuras ?? CamadaFiguras.Inicial(p)).Where(x => x.Incluida))
        {
            n++;
            if (f.Id == figuraId) return "Figura " + n.ToString(CultureInfo.InvariantCulture);
        }
        return "Figura";
    }

    void CarregarImagem(string chave)
    {
        var caminho = System.IO.Path.Combine(sessao.Recompositor.PastaTrabalho, chave.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var bytes = File.ReadAllBytes(caminho);
        var tam = TamanhoImagem.Ler(bytes) ?? throw new InvalidOperationException("Não foi possível ler o tamanho da imagem da figura.");
        medidas = MedidasFigura.De(tam.Largura, tam.Altura);
        superficie.Width = medidas.LarguraTotal;
        superficie.Height = medidas.AlturaTotal;
        camada.Width = medidas.LarguraTotal;
        camada.Height = medidas.AlturaTotal;
        imagem.Width = medidas.LarguraImagem;
        imagem.Height = medidas.AlturaImagem;
        imagem.Margin = new Thickness(medidas.Margem, 0, 0, 0);
        var bmp = new BitmapImage();
        using (var fluxo = new MemoryStream(bytes))
            bmp.SetSource(fluxo.AsRandomAccessStream());
        imagem.Source = bmp;
        imagemCarregada = chave;
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AjustarZoom);
    }

    void AjustarZoom()
    {
        if (medidas is null || rolagem.ViewportWidth <= 0) return;
        var z = (float)Math.Min(rolagem.ViewportWidth / medidas.LarguraTotal, rolagem.ViewportHeight / medidas.AlturaTotal) * .96f;
        rolagem.ChangeView(0, 0, Math.Clamp(z, rolagem.MinZoomFactor, rolagem.MaxZoomFactor), disableAnimation: true);
    }

    void PreencherLista()
    {
        listaMarcacoes.Items.Clear();
        if (figura is null) return;
        var numeros = CamadaFiguras.Numeradas(figura).ToDictionary(x => x.Marcacao.Id, x => x.Numero, StringComparer.Ordinal);
        var ordem = CamadaFiguras.Numeradas(figura).Select(x => x.Marcacao).Concat(figura.Marcacoes.Where(m => !m.Numerada));
        foreach (var m in ordem)
        {
            var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Tag = m.Id };
            var marca = new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = new SolidColorBrush(m.Numerada ? Vermelho : Colors.Transparent),
                BorderBrush = new SolidColorBrush(Vermelho), BorderThickness = new Thickness(m.Numerada ? 0 : 1.5),
                Child = new TextBlock
                {
                    Text = m.Numerada && numeros.TryGetValue(m.Id, out var n) ? n.ToString(CultureInfo.InvariantCulture) : "T",
                    Foreground = new SolidColorBrush(m.Numerada ? Colors.White : Vermelho), FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontSize = 12,
                },
            };
            linha.Children.Add(marca);
            var texto = m.Numerada ? m.Elemento : "Texto: " + m.Texto;
            var itens = m.Numerada && m.Itens.Length > 0 ? "  (" + CamadaFiguras.ItensImpressos(sessao.Revisao.Projeto, m) + ")" : "";
            linha.Children.Add(new TextBlock { Text = texto + itens + (m.Gravada ? "  · gravada na imagem" : ""), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 240, VerticalAlignment = VerticalAlignment.Center });
            AutomationNome(linha, (m.Numerada && numeros.TryGetValue(m.Id, out var nn) ? "Marcação " + nn.ToString(CultureInfo.InvariantCulture) + ": " : "") + texto);
            listaMarcacoes.Items.Add(linha);
            if (m.Id == selecionada) listaMarcacoes.SelectedItem = linha;
        }
    }

    void PreencherMarcacao()
    {
        var m = selecionada is null ? null : figura?.Buscar(selecionada);
        painelMarcacao.Visibility = m is null ? Visibility.Collapsed : Visibility.Visible;
        if (m is null || figura is null) return;
        preenchendo = true;
        try
        {
            var numero = CamadaFiguras.Numero(figura, m.Id);
            tituloMarcacao.Text = m.Numerada ? "Marcação " + numero?.ToString(CultureInfo.InvariantCulture) + (m.Gravada ? " (gravada na imagem)" : "") : "Texto livre na imagem";
            caixaElemento.Visibility = m.Numerada ? Visibility.Visible : Visibility.Collapsed;
            listaItens.Visibility = m.Numerada ? Visibility.Visible : Visibility.Collapsed;
            caixaTexto.Header = m.Numerada ? "Solução ou o que foi conferido" : "Texto escrito na imagem";
            caixaElemento.Tag = caixaTexto.Tag = m.Id;
            AtualizarTexto(caixaElemento, m.Elemento);
            AtualizarTexto(caixaTexto, m.Texto);
            botaoRemover.IsEnabled = !m.Gravada && !sessao.SomenteLeitura;
            Dicas.Aplicar(botaoRemover, "Remover marcação", "Tira esta marcação da figura.", "Del",
                m.Gravada ? "O número está desenhado na imagem recebida e não sai dela." : null);
            listaItens.Items.Clear();
            if (m.Numerada)
            {
                var p = sessao.Revisao.Projeto;
                var citados = CamadaFiguras.ItensCitados(m.Itens).ToHashSet(StringComparer.Ordinal);
                var n = 0;
                foreach (var it in p.Incluidos)
                {
                    n++;
                    var titulo = DadosEfetivos.TextoEfetivo(p, it, "titulo") ?? "";
                    var linha = new TextBlock { Text = "ITEM " + n.ToString("D2", CultureInfo.InvariantCulture) + " — " + titulo, Tag = it.Id, TextTrimming = TextTrimming.CharacterEllipsis };
                    listaItens.Items.Add(linha);
                    if (citados.Contains(it.Id)) listaItens.SelectedItems.Add(linha);
                }
                listaItens.IsEnabled = !sessao.SomenteLeitura;
            }
        }
        finally { preenchendo = false; }
    }

    // ---------- desenho da camada ----------

    IReadOnlyList<MarcaDesenho> MarcasAtuais()
    {
        if (figura is not { Incluida: true }) return Array.Empty<MarcaDesenho>();
        var lista = CamadaFiguras.Desenho(figura).ToList();
        if (gesto.Atual is { } atual) lista = lista.Select(x => x.Id == atual.Id ? atual : x).ToList();
        return lista;
    }

    double Escala => Math.Max(.05, rolagem.ZoomFactor);

    void Desenhar()
    {
        camada.Children.Clear();
        if (medidas is not { } m || figura is not { Incluida: true }) return;
        var formas = GeometriaFigura.Calcular(m, MarcasAtuais());
        var vermelho = new SolidColorBrush(Vermelho);
        var branco = new SolidColorBrush(Colors.White);

        foreach (var f in formas)
        {
            if (f.Caixa is not { } c) continue;
            var r = new Rectangle
            {
                Width = c.X1 - c.X0, Height = c.Y1 - c.Y0, RadiusX = f.RaioCaixa, RadiusY = f.RaioCaixa,
                Fill = new SolidColorBrush(Color.FromArgb(46, 0xC0, 0, 0)), Stroke = vermelho, StrokeThickness = m.Traco,
            };
            Canvas.SetLeft(r, c.X0);
            Canvas.SetTop(r, c.Y0);
            camada.Children.Add(r);
        }
        foreach (var (cor, largura, abertura) in new[] { (branco, (double)m.Halo, .42), (vermelho, (double)m.Traco, .32) })
        {
            foreach (var f in formas)
            {
                if (f.InicioSeta is not { } a || f.PontaSeta is not { } p) continue;
                var fim = GeometriaFigura.FimDaHaste(a, p, m.Ponta);
                camada.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = fim.X, Y2 = fim.Y, Stroke = cor, StrokeThickness = largura, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
                var (v1, v2, v3) = GeometriaFigura.Cabeca(a, p, m.Ponta, abertura);
                camada.Children.Add(new Polygon { Fill = cor, Points = { new(v1.X, v1.Y), new(v2.X, v2.Y), new(v3.X, v3.Y) } });
            }
        }
        foreach (var f in formas)
        {
            if (f.Numero is not { } c || f.Marca.Numero is not { } numero) continue;
            var circulo = new Grid { Width = 2 * (m.Raio + 2), Height = 2 * (m.Raio + 2) };
            circulo.Children.Add(new Ellipse { Fill = branco });
            circulo.Children.Add(new Ellipse { Fill = vermelho, Margin = new Thickness(2) });
            circulo.Children.Add(new TextBlock
            {
                Text = numero.ToString(CultureInfo.InvariantCulture), Foreground = branco, FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = m.Fonte,
                FontFamily = new FontFamily("Arial"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            Canvas.SetLeft(circulo, c.X - m.Raio - 2);
            Canvas.SetTop(circulo, c.Y - m.Raio - 2);
            camada.Children.Add(circulo);
        }
        foreach (var f in formas)
        {
            if (f.Texto is not { } t) continue;
            var (w, h) = InteracaoFigura.TamanhoTexto(m, f.Marca.Texto ?? "");
            // Fundo branco opaco com contorno, igual ao SVG do relatório (SvgFigura).
            var caixa = new Border
            {
                Width = w + m.Fonte * .6, Height = h + m.Fonte * .3, Background = branco, BorderBrush = vermelho,
                BorderThickness = new Thickness(Math.Max(1, m.Traco / 2.0)), CornerRadius = new CornerRadius(m.Fonte * .2),
            };
            var grade = new Grid();
            caixa.Child = grade;
            grade.Children.Add(new TextBlock
            {
                Text = f.Marca.Texto ?? "", Foreground = vermelho, FontWeight = Microsoft.UI.Text.FontWeights.Bold, FontSize = m.Fonte,
                FontFamily = new FontFamily("Arial"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            Canvas.SetLeft(caixa, t.X - caixa.Width / 2);
            Canvas.SetTop(caixa, t.Y - caixa.Height / 2);
            camada.Children.Add(caixa);
        }

        // Seleção: contorno tracejado e alças (do tamanho certo em qualquer zoom).
        if (selecionada is not null && formas.FirstOrDefault(f => f.Marca.Id == selecionada) is { } sel)
        {
            var alca = 10 / Escala;
            var azul = new SolidColorBrush(Color.FromArgb(255, 0x00, 0x67, 0xC0));
            void Alca(double x, double y, bool redonda = false)
            {
                Shape s = redonda ? new Ellipse() : new Rectangle();
                s.Width = s.Height = alca;
                s.Fill = branco;
                s.Stroke = azul;
                s.StrokeThickness = 1.5 / Escala;
                Canvas.SetLeft(s, x - alca / 2);
                Canvas.SetTop(s, y - alca / 2);
                camada.Children.Add(s);
            }
            if (sel.Caixa is { } c)
            {
                var contorno = new Rectangle { Width = c.X1 - c.X0, Height = c.Y1 - c.Y0, Stroke = azul, StrokeThickness = 1.5 / Escala, StrokeDashArray = { 4, 3 } };
                Canvas.SetLeft(contorno, c.X0);
                Canvas.SetTop(contorno, c.Y0);
                camada.Children.Add(contorno);
                Alca(c.X0, c.Y0); Alca(c.X1, c.Y0); Alca(c.X1, c.Y1); Alca(c.X0, c.Y1);
            }
            if (sel.Marca.Tipo == TiposMarcacao.Seta && sel.PontaSeta is { } ponta) Alca(ponta.X, ponta.Y, redonda: true);
            if (sel.Numero is { } n)
            {
                var anel = new Ellipse { Width = 2 * (m.Raio + 6), Height = 2 * (m.Raio + 6), Stroke = azul, StrokeThickness = 2 / Escala, StrokeDashArray = { 3, 2 } };
                Canvas.SetLeft(anel, n.X - m.Raio - 6);
                Canvas.SetTop(anel, n.Y - m.Raio - 6);
                camada.Children.Add(anel);
            }
            if (sel.Texto is { } t)
            {
                var (w, h) = InteracaoFigura.TamanhoTexto(m, sel.Marca.Texto ?? "");
                var contorno = new Rectangle { Width = w + 8, Height = h + 4, Stroke = azul, StrokeThickness = 1.5 / Escala, StrokeDashArray = { 4, 3 } };
                Canvas.SetLeft(contorno, t.X - w / 2 - 4);
                Canvas.SetTop(contorno, t.Y - h / 2 - 2);
                camada.Children.Add(contorno);
            }
        }
        if (retanguloNovo is not null) camada.Children.Add(retanguloNovo);
    }

    // ---------- mouse ----------

    Ponto PontoDe(PointerRoutedEventArgs e)
    {
        var p = e.GetCurrentPoint(camada).Position;
        return new Ponto(p.X, p.Y);
    }

    async void AoPressionar(object sender, PointerRoutedEventArgs e)
    {
        if (medidas is not { } m || figura is not { Incluida: true } || aplicando || sessao.SomenteLeitura) return;
        if (!e.GetCurrentPoint(camada).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        CancelarArrasto();
        var p = PontoDe(e);
        var (nx, ny) = m.ParaNormalizado(p);
        var dentro = nx is >= 0 and <= 1 && ny is >= 0 and <= 1;
        var tolerancia = 12 / Escala;
        switch (ferramenta)
        {
            case FerramentaFigura.Selecionar:
            {
                var acerto = InteracaoFigura.Acertar(m, CamadaFiguras.Desenho(figura), p, selecionada, tolerancia);
                selecionada = acerto?.Marcacao;
                SincronizarLista();
                if (acerto is not null && CamadaFiguras.Desenho(figura).FirstOrDefault(x => x.Id == acerto.Marcacao) is { } x)
                {
                    gesto.IniciarArrasto(m, x, acerto.Alca, p);
                    if (!camada.CapturePointer(e.Pointer)) CancelarArrasto();
                }
                Desenhar();
                break;
            }
            case FerramentaFigura.Regiao when dentro:
                gesto.IniciarRegiao(m, p);
                retanguloNovo = new Rectangle { Stroke = new SolidColorBrush(Vermelho), StrokeThickness = 2 / Escala, StrokeDashArray = { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(40, 0xC0, 0, 0)) };
                if (!camada.CapturePointer(e.Pointer)) { CancelarArrasto(); break; }
                AtualizarRetangulo(p);
                break;
            case FerramentaFigura.Seta when dentro:
            {
                var id = Marcacao.NovoId();
                if (await AplicarAsync(new IncluirMarcacao(figuraId, id, TiposMarcacao.Seta, Alvo: [nx, ny]))) Selecionar(id);
                break;
            }
            case FerramentaFigura.Texto when dentro:
            {
                var id = Marcacao.NovoId();
                if (await AplicarAsync(new IncluirMarcacao(figuraId, id, TiposMarcacao.Texto, Posicao: [nx, ny], Texto: "Texto")))
                {
                    Selecionar(id);
                    caixaTexto.Focus(FocusState.Programmatic);
                    caixaTexto.SelectAll();
                }
                break;
            }
            case FerramentaFigura.Remover:
            {
                var acerto = InteracaoFigura.Acertar(m, CamadaFiguras.Desenho(figura), p, null, tolerancia);
                if (acerto is not null) await AplicarAsync(new ExcluirMarcacao(figuraId, acerto.Marcacao));
                break;
            }
        }
    }

    void AoMover(object sender, PointerRoutedEventArgs e)
    {
        if (medidas is null || !gesto.Ativo) return;
        var p = PontoDe(e);
        if (retanguloNovo is not null) { AtualizarRetangulo(p); return; }
        gesto.Mover(p);
        Desenhar();
    }

    async void AoSoltar(object sender, PointerRoutedEventArgs e)
    {
        if (!gesto.Ativo) return;
        e.Handled = true;
        var nova = gesto.NovaRegiao;
        // Consome o estado antes de PointerCaptureLost disparar ao liberar o mouse.
        var comando = gesto.Concluir(figuraId, PontoDe(e));
        retanguloNovo = null;
        camada.ReleasePointerCapture(e.Pointer);
        Desenhar();
        if (comando is null)
        {
            if (nova) estado.Text = "Arraste um retângulo maior para marcar a região.";
            return;
        }
        if (await AplicarAsync(comando) && comando is IncluirMarcacao incluir) Selecionar(incluir.Marcacao);
    }

    void CancelarArrasto()
    {
        gesto.Cancelar();
        retanguloNovo = null;
        Desenhar();
    }

    void AtualizarRetangulo(Ponto p)
    {
        if (retanguloNovo is null || medidas is not { } m) return;
        var c = InteracaoFigura.CaixaDoArrasto(m, gesto.Inicio, p);
        var a = m.ParaPixel(c[0], c[1]);
        var b = m.ParaPixel(c[2], c[3]);
        var x0 = a.X;
        var y0 = a.Y;
        retanguloNovo.Width = b.X - a.X;
        retanguloNovo.Height = b.Y - a.Y;
        Canvas.SetLeft(retanguloNovo, x0);
        Canvas.SetTop(retanguloNovo, y0);
        if (!camada.Children.Contains(retanguloNovo)) camada.Children.Add(retanguloNovo);
    }

    void Selecionar(string id)
    {
        selecionada = id;
        Atualizar();
    }

    void SincronizarLista()
    {
        preenchendo = true;
        try { listaMarcacoes.SelectedItem = listaMarcacoes.Items.OfType<FrameworkElement>().FirstOrDefault(x => x.Tag as string == selecionada); }
        finally { preenchendo = false; }
        PreencherMarcacao();
    }

    // ---------- comandos ----------

    /// <summary>Manda o comando para a janela principal (fila, histórico, recomposição). Recusa: aviso aqui, nada muda.</summary>
    async Task<bool> AplicarAsync(Comando c)
    {
        edicoesPendentes++;
        await filaEdicoes.WaitAsync();
        aplicando = true;
        try
        {
            if (fechada || host.Sessao != sessao || sessao.SomenteLeitura) return false;
            var erros = await host.AplicarFiguraAsync(c);
            avisoErro.Title = "A alteração não foi aplicada";
            avisoErro.IsOpen = erros.Count > 0;
            avisoErro.Message = string.Join(Environment.NewLine, erros);
            return erros.Count == 0;
        }
        catch (Exception e)
        {
            avisoErro.Title = "A alteração não foi aplicada";
            avisoErro.Message = e.Message;
            avisoErro.IsOpen = true;
            return false;
        }
        finally
        {
            aplicando = false;
            edicoesPendentes--;
            filaEdicoes.Release();
            Atualizar();
        }
    }

    async Task HistoricoAsync(bool refazer)
    {
        CancelarArrasto();
        edicoesPendentes++;
        await filaEdicoes.WaitAsync();
        aplicando = true;
        try
        {
            if (fechada || host.Sessao != sessao || sessao.SomenteLeitura) return;
            if (refazer) await host.RefazerFiguraAsync();
            else await host.DesfazerFiguraAsync();
        }
        catch (Exception e)
        {
            avisoErro.Title = refazer ? "Não foi possível refazer" : "Não foi possível desfazer";
            avisoErro.Message = e.Message;
            avisoErro.IsOpen = true;
        }
        finally
        {
            aplicando = false;
            edicoesPendentes--;
            filaEdicoes.Release();
            Atualizar();
        }
    }
}
