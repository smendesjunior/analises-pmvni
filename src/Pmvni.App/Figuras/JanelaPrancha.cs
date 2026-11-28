using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Pmvni.Revisao;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI;

namespace Pmvni.App;

/// <summary>"Inserir região da prancha" (Etapa 4, especificação §9): abre um PDF do projeto (por padrão os da pasta do processo; ou
/// "Escolher arquivo…") numa janela própria, mostra a página escolhida (Windows.Data.Pdf, local), amplia, e o usuário delimita o recorte
/// arrastando o mouse. O recorte é renderizado em PNG sem perdas a 200 dpi, guardado em <c>assets/&lt;sha256&gt;.png</c> do relatório e
/// entra como figura nova na seção IMAGENS DO PROJETO; o editor de marcações abre em seguida. O PDF original nunca é alterado: só é lido.</summary>
internal sealed class JanelaPrancha : Window
{
    /// <summary>Resolução do recorte inserido (especificação §9: 200 dpi).</summary>
    public const double DpiRecorte = 200;

    /// <summary>Lado maior da página mostrada na tela, em pixels (a ampliação além disso é do ScrollViewer; o recorte sai sempre a 200 dpi).</summary>
    const double LadoMaximoTela = 5000;

    readonly IHostFiguras host;
    readonly SessaoEdicao sessao;
    readonly string? depoisDe;

    readonly Grid vazio = PainelPrancha.Cartao();
    // Visual comum: o verde da janela é "Inserir no relatório", no rodapé.
    readonly Button abrirNoCentro = new() { Content = "Escolher arquivo…", HorizontalAlignment = HorizontalAlignment.Center };
    readonly Grid raiz = new();
    readonly ComboBox arquivos = new() { HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "Escolha o PDF das pranchas" };
    readonly Button escolher = new() { Content = "Escolher arquivo…" };
    readonly AppBarButton anterior = new() { Icon = new FontIcon { Glyph = Glifos.Anterior, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] }, Label = "Página anterior" };
    readonly AppBarButton proxima = new() { Icon = new FontIcon { Glyph = Glifos.Proximo, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] }, Label = "Próxima página" };
    readonly TextBlock textoPagina = new() { VerticalAlignment = VerticalAlignment.Center, Text = "Nenhuma página", Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] };
    readonly ScrollViewer rolagem = new() { ZoomMode = ZoomMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinZoomFactor = .1f, MaxZoomFactor = 8 }; // o WinUI recusa mínimo abaixo de 0,1 (a janela não abria)
    readonly Grid superficie = new() { Background = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Image pagina = new() { Stretch = Stretch.Fill };
    readonly Canvas camada = new() { Background = new SolidColorBrush(Colors.Transparent) };
    readonly Rectangle selecao = new() { Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 0x67, 0xC0)), StrokeThickness = 2, StrokeDashArray = { 5, 3 }, Fill = new SolidColorBrush(Color.FromArgb(40, 0, 0x67, 0xC0)), Visibility = Visibility.Collapsed };
    readonly TextBox caixaPrancha = new() { Header = "Prancha", PlaceholderText = "Ex.: ARQ 01/04" };
    readonly TextBox caixaTitulo = new() { Header = "Título da figura", Text = "Recorte da prancha", TextWrapping = TextWrapping.Wrap };
    readonly Button inserir = new() { Content = "Inserir no relatório", IsEnabled = false };
    readonly Button cancelar = new() { Content = "Cancelar" };
    readonly TextBlock estado = new() { TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"] };
    readonly InfoBar aviso = new() { Severity = InfoBarSeverity.Warning, IsClosable = true };
    readonly Border progresso = Trabalhando.Quadro("Trabalhando…");
    readonly Viewbox previa = new() { Stretch = Stretch.Uniform, MaxHeight = 240, Visibility = Visibility.Collapsed };
    readonly TextBlock orientacao = new() { Text = "Arraste sobre a página para selecionar a região da figura.", Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"] };
    readonly AppBarButton limpar = new() { Icon = new FontIcon { Glyph = Glifos.Limpar, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] }, Label = "Limpar recorte" };
    readonly AppBarButton selecionarTudo = new() { Icon = new FontIcon { Glyph = Glifos.SelecionarTudo, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] }, Label = "Selecionar página" };
    readonly TextBlock textoZoom = new() { Text = "100%", Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] };
    readonly CommandBar ferramentas = new() { HorizontalAlignment = HorizontalAlignment.Left, DefaultLabelPosition = CommandBarDefaultLabelPosition.Right, IsDynamicOverflowEnabled = true };

    PdfDocument? documento;
    IRandomAccessStream? fonteDocumento;
    string? caminhoPdf;
    string? shaPdf;
    uint indicePagina;
    Windows.Foundation.Size tamanhoPagina;
    Windows.Foundation.Point? inicio;
    Windows.Foundation.Rect? recorte; // em pixels da página mostrada
    bool ocupado, paginaPronta, fechada, iniciada, escolhendo;
    string ultimaSugestao = "";
    bool SessaoValida => !fechada && host.Sessao == sessao && !sessao.SomenteLeitura;

    public JanelaPrancha(IHostFiguras host, SessaoEdicao sessao, string? depoisDe)
    {
        this.host = host;
        this.sessao = sessao;
        this.depoisDe = depoisDe;
        MainWindow.AplicarIconeDaJanela(this);
        Title = "Inserir região da prancha — Editor de Relatórios PMVNI";
        Montar();
        Content = raiz;
        raiz.RequestedTheme = host.TemaAtual;

        raiz.Loaded += async (_, _) =>
        {
            if (iniciada) return;
            iniciada = true;
            PainelPrancha.Dimensionar(this);
            await PreencherArquivosAsync();
        };
        Closed += (_, _) => { fechada = true; documento = null; fonteDocumento?.Dispose(); fonteDocumento = null; };
        AtualizarControles();
    }

    void Montar()
    {
        raiz.Style = (Style)Application.Current.Resources["PmvniFundoJanelaStyle"]; // segue o RequestedTheme da janela, não o do Windows
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        raiz.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var colunaDetalhes = new ColumnDefinition();
        raiz.ColumnDefinitions.Add(colunaDetalhes);
        raiz.SizeChanged += (_, e) => colunaDetalhes.Width = new GridLength(Math.Clamp(e.NewSize.Width * .26, 210, 400));
        var topo = new StackPanel { Spacing = 4 };
        var arquivoLinha = new Grid { ColumnSpacing = 8, Margin = new Thickness(16, 2, 16, 8) };
        arquivoLinha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        arquivoLinha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        arquivoLinha.Children.Add(arquivos);
        Grid.SetColumn(escolher, 1);
        arquivoLinha.Children.Add(escolher);
        topo.Children.Add(new TextBlock { Text = "PDF das pranchas", Margin = new Thickness(16, 10, 16, 0),
            Style = (Style)Application.Current.Resources["PmvniPainelRotulo"] });
        topo.Children.Add(arquivoLinha);
        ferramentas.PrimaryCommands.Add(anterior);
        ferramentas.PrimaryCommands.Add(proxima);
        ferramentas.PrimaryCommands.Add(new AppBarSeparator());
        var menos = new AppBarButton { Icon = new FontIcon { Glyph = Glifos.Diminuir, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] }, Label = "Diminuir o zoom" };
        var mais = new AppBarButton { Icon = new FontIcon { Glyph = Glifos.Aumentar, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] }, Label = "Aumentar o zoom" };
        var ajustar = new AppBarButton { Icon = new FontIcon { Glyph = Glifos.PaginaInteira, Style = (Style)Application.Current.Resources["PmvniIconeAcao"] }, Label = "Página inteira" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(menos, "Diminuir");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mais, "Aumentar");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(anterior, "Página anterior");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(proxima, "Próxima página");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(arquivos, "PDF das pranchas");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ajustar, ajustar.Label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(selecionarTudo, selecionarTudo.Label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(limpar, limpar.Label);
        Dicas.Aplicar(escolher, "Escolher arquivo…", "Escolhe no computador outro PDF de prancha para recortar.");
        Dicas.Aplicar(abrirNoCentro, "Escolher arquivo…", "Escolhe no computador o PDF de prancha para recortar.");
        Dicas.Aplicar(menos, menos.Label, "Diminui o zoom da prancha.");
        Dicas.Aplicar(mais, mais.Label, "Aumenta o zoom da prancha, para recortar com mais precisão.");
        Dicas.Aplicar(ajustar, "Página inteira", "Ajusta o zoom para a página inteira caber na janela.");
        Dicas.Aplicar(cancelar, "Cancelar", "Fecha esta janela sem inserir nada.");
        menos.Click += (_, _) => rolagem.ChangeView(null, null, Math.Max(rolagem.MinZoomFactor, rolagem.ZoomFactor / 1.25f));
        mais.Click += (_, _) => rolagem.ChangeView(null, null, Math.Min(rolagem.MaxZoomFactor, rolagem.ZoomFactor * 1.25f));
        ajustar.Click += (_, _) => AjustarZoom();
        ferramentas.PrimaryCommands.Add(menos);
        ferramentas.PrimaryCommands.Add(new AppBarElementContainer { Content = textoZoom, VerticalContentAlignment = VerticalAlignment.Center });
        ferramentas.PrimaryCommands.Add(mais);
        ferramentas.PrimaryCommands.Add(ajustar);
        ferramentas.PrimaryCommands.Add(new AppBarSeparator());
        ferramentas.SecondaryCommands.Add(selecionarTudo);
        ferramentas.SecondaryCommands.Add(limpar);
        var leitura = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(12, 0, 8, 0) };
        leitura.Children.Add(textoPagina);

        ferramentas.Content = leitura;
        topo.Children.Add(ferramentas);
        progresso.Margin = new Thickness(16, 6, 16, 6);
        topo.Children.Add(progresso);
        Grid.SetColumnSpan(topo, 2);
        raiz.Children.Add(topo);

        aviso.IsOpen = false;
        Grid.SetRow(aviso, 1);
        Grid.SetColumnSpan(aviso, 2);
        raiz.Children.Add(aviso);

        superficie.Children.Add(pagina);
        camada.Children.Add(selecao);
        superficie.Children.Add(camada);
        rolagem.Content = superficie;
        rolagem.Style = (Style)Application.Current.Resources["PmvniRolagemFundoStyle"];
        var area = new Grid();
        area.Children.Add(rolagem);
        vazio.HorizontalAlignment = HorizontalAlignment.Center;
        vazio.VerticalAlignment = VerticalAlignment.Center;
        vazio.MaxWidth = 520;
        vazio.Margin = new Thickness(12);
        vazio.Padding = new Thickness(24);
        var mensagem = new StackPanel { Spacing = 16 };
        mensagem.Children.Add(new FontIcon { Glyph = Glifos.Documento, FontSize = 16 });
        mensagem.Children.Add(new TextBlock { Text = "Nenhum PDF carregado", TextAlignment = TextAlignment.Center,
            Style = (Style)Application.Current.Resources["PmvniTituloPaginaStyle"] });
        mensagem.Children.Add(new TextBlock { Text = "Selecione o PDF das pranchas ou escolha um arquivo do computador para visualizar as páginas.",
            Style = (Style)Application.Current.Resources["PmvniAjusteAjuda"], TextAlignment = TextAlignment.Center });
        mensagem.Children.Add(abrirNoCentro);
        abrirNoCentro.Click += async (_, _) => await EscolherArquivoAsync();
        vazio.Children.Add(mensagem);
        area.Children.Add(vazio);
        var desenho = PainelPrancha.Quadro(new TextBlock { Text = "Pré-visualização da prancha", Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] }, area, Glifos.Recortar);
        desenho.Margin = new Thickness(12, 12, 6, 12);
        Grid.SetRow(desenho, 2);
        raiz.Children.Add(desenho);

        var lateral = new StackPanel { Spacing = 14, Padding = new Thickness(16) };
        lateral.Children.Add(new TextBlock { Text = "Identificação", Style = (Style)Application.Current.Resources["PmvniPainelRotulo"] });
        lateral.Children.Add(caixaPrancha);
        lateral.Children.Add(caixaTitulo);
        lateral.Children.Add(new TextBlock { Text = "Prévia do recorte", Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] });
        lateral.Children.Add(orientacao);
        lateral.Children.Add(previa);
        var painel = new ScrollViewer { Content = lateral, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var dados = PainelPrancha.Quadro(new TextBlock { Text = "Nova figura", Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] }, painel, Glifos.Recortar);
        dados.Margin = new Thickness(6, 12, 12, 12);
        Grid.SetRow(dados, 2);
        Grid.SetColumn(dados, 1);
        raiz.Children.Add(dados);
        var rodape = new Grid { ColumnSpacing = 16, Padding = new Thickness(12) };
        rodape.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        rodape.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rodape.Children.Add(estado);
        var acoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        inserir.Style = (Style)Application.Current.Resources["PmvniBotaoPrimarioStyle"];
        inserir.VerticalAlignment = cancelar.VerticalAlignment = VerticalAlignment.Bottom;
        acoes.Children.Add(inserir);
        acoes.Children.Add(cancelar);
        Grid.SetColumn(acoes, 1);
        rodape.Children.Add(acoes);
        Grid.SetRow(rodape, 3);
        Grid.SetColumnSpan(rodape, 2);
        raiz.Children.Add(rodape);
        estado.Text = "Escolha a página, amplie se precisar e arraste o mouse sobre a região a recortar.";

        arquivos.SelectionChanged += async (_, _) => { if (arquivos.SelectedItem is ComboBoxItem { Tag: string c }) await AbrirPdfAsync(c); };
        IconeDeAcao.SetGlifo(escolher, Glifos.Abrir);
        IconeDeAcao.SetGlifo(abrirNoCentro, Glifos.Abrir);
        IconeDeAcao.SetGlifo(inserir, Glifos.Adicionar);
        IconeDeAcao.SetGlifo(cancelar, Glifos.NaoConfigurado);
        escolher.Click += async (_, _) => await EscolherArquivoAsync();
        anterior.Click += async (_, _) => { if (!ocupado && documento is not null && indicePagina > 0) { indicePagina--; await MostrarPaginaAsync(); } };
        proxima.Click += async (_, _) => { if (!ocupado && documento is not null && indicePagina + 1 < documento.PageCount) { indicePagina++; await MostrarPaginaAsync(); } };
        limpar.Click += (_, _) => LimparRecorte();
        selecionarTudo.Click += (_, _) =>
        {
            SelecionarRegiao(0, 0, 1, 1);
        };
        rolagem.ViewChanged += (_, _) =>
        {
            textoZoom.Text = rolagem.ZoomFactor.ToString("P0", CultureInfo.CurrentCulture);
            selecao.StrokeThickness = 2 / Math.Max(.1, rolagem.ZoomFactor);
        };
        cancelar.Click += (_, _) => Close();
        inserir.Click += async (_, _) => await InserirAsync();
        camada.PointerPressed += (_, e) =>
        {
            if (!paginaPronta || ocupado || !SessaoValida || !e.GetCurrentPoint(camada).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            LimparRecorte();
            inicio = e.GetCurrentPoint(camada).Position;
            if (!camada.CapturePointer(e.Pointer)) { LimparRecorte(); return; }
            AtualizarSelecao(inicio.Value);
        };
        camada.PointerMoved += (_, e) => { if (inicio is not null) AtualizarSelecao(e.GetCurrentPoint(camada).Position); };
        camada.PointerReleased += (_, e) =>
        {
            if (inicio is null) return;
            e.Handled = true;
            AtualizarSelecao(e.GetCurrentPoint(camada).Position);
            inicio = null;
            camada.ReleasePointerCapture(e.Pointer);
            ConferirRecorte();
        };
        camada.PointerCanceled += (_, _) => { if (inicio is not null) LimparRecorte(); };
        camada.PointerCaptureLost += (_, _) => { if (inicio is not null) LimparRecorte(); };
        raiz.KeyDown += (_, e) =>
        {
            if (e.Key == Windows.System.VirtualKey.Escape && inicio is not null) { LimparRecorte(); e.Handled = true; }
        };
    }

    static string Medida(double dip) => (dip / 96 * 25.4).ToString("0", CultureInfo.InvariantCulture) + " mm";

    RecortePdf MedidasRecorte(Windows.Foundation.Rect r) => RecortePdf.DeTela(r.X, r.Y, r.Right, r.Bottom,
        camada.Width, camada.Height, tamanhoPagina.Width, tamanhoPagina.Height, DpiRecorte);

    void AtualizarControles()
    {
        if (fechada) return;
        var livre = !ocupado && !escolhendo && SessaoValida;
        arquivos.IsEnabled = escolher.IsEnabled = abrirNoCentro.IsEnabled = livre;
        vazio.Visibility = documento is null && !ocupado ? Visibility.Visible : Visibility.Collapsed;
        ferramentas.IsEnabled = livre && documento is not null;
        selecionarTudo.IsEnabled = livre && paginaPronta;
        anterior.IsEnabled = livre && documento is not null && indicePagina > 0;
        proxima.IsEnabled = livre && documento is not null && indicePagina + 1 < documento.PageCount;
        inserir.IsEnabled = livre && paginaPronta && inicio is null && recorte is { Width: > 8, Height: > 8 } && previa.Child is not null;
        limpar.IsEnabled = livre && recorte is not null;
        var bloqueio = !SessaoValida ? "Abra um relatório editável para inserir o recorte."
            : !livre ? "Aguarde a operação atual."
            : !paginaPronta ? "Abra uma página do PDF." : null;
        Dicas.Aplicar(anterior, anterior.Label, "Volta para a página anterior do PDF.",
            motivo: anterior.IsEnabled ? null : bloqueio ?? "Esta é a primeira página.");
        Dicas.Aplicar(proxima, proxima.Label, "Avança para a próxima página do PDF.",
            motivo: proxima.IsEnabled ? null : bloqueio ?? "Esta é a última página.");
        Dicas.Aplicar(selecionarTudo, selecionarTudo.Label, "Seleciona a página inteira como região do recorte.",
            motivo: selecionarTudo.IsEnabled ? null : bloqueio);
        Dicas.Aplicar(limpar, limpar.Label, "Limpa a região selecionada na página.",
            motivo: limpar.IsEnabled ? null : bloqueio ?? "Nenhuma região selecionada.");
        Dicas.Aplicar(inserir, "Inserir no relatório", "Insere a região marcada como figura nova no relatório.",
            motivo: inserir.IsEnabled ? null : bloqueio ?? "Selecione uma região para inserir.");
        caixaPrancha.IsEnabled = caixaTitulo.IsEnabled = livre;
        progresso.Visibility = ocupado ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void SelecionarRegiao(double esquerda, double topo, double direita, double baseRegiao)
    {
        inicio = new Windows.Foundation.Point(esquerda * camada.Width, topo * camada.Height);
        AtualizarSelecao(new Windows.Foundation.Point(direita * camada.Width, baseRegiao * camada.Height));
        inicio = null;
        ConferirRecorte();
    }

    internal Button EscolherPdf => escolher;
    internal Button InserirRecorte => inserir;
    internal AppBarButton SelecionarPagina => selecionarTudo;
    internal bool ProntaParaEscolher => iniciada && escolher.IsEnabled;
    internal bool PaginaPronta => paginaPronta && !ocupado;

    internal bool PertenceASessao(SessaoEdicao atual) => sessao == atual;

    void LimparRecorte()
    {
        inicio = null;
        recorte = null;
        selecao.Visibility = previa.Visibility = Visibility.Collapsed;
        previa.Child = null;
        orientacao.Text = "Arraste sobre a página para selecionar a região da figura.";
        AtualizarControles();
    }

    void LimparPagina()
    {
        paginaPronta = false;
        pagina.Source = null;
        textoPagina.Text = "Sem página";
        LimparRecorte();
    }

    void ConferirRecorte()
    {
        if (recorte is not { Width: > 8, Height: > 8 } r)
        {
            LimparRecorte();
            estado.Text = "Arraste uma região maior para recortar.";
            return;
        }
        try
        {
            var medida = MedidasRecorte(r);
            var conteudo = new Canvas { Width = r.Width, Height = r.Height, Clip = new RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0, r.Width, r.Height) } };
            var imagem = new Image { Source = pagina.Source, Width = camada.Width, Height = camada.Height, Stretch = Stretch.Fill };
            Canvas.SetLeft(imagem, -r.X);
            Canvas.SetTop(imagem, -r.Y);
            conteudo.Children.Add(imagem);
            previa.Child = conteudo;
            previa.Visibility = Visibility.Visible;
            orientacao.Text = Medida(medida.Largura) + " × " + Medida(medida.Altura) + " · "
                + medida.PixelsLargura + " × " + medida.PixelsAltura + " pixels · 200 dpi";
            estado.Text = "Confira o recorte e o título; depois insira no relatório.";
        }
        catch (ArgumentException e)
        {
            previa.Child = null;
            previa.Visibility = Visibility.Collapsed;
            estado.Text = e.Message;
        }
        AtualizarControles();
    }

    void Avisar(string titulo, Exception erro)
    {
        if (fechada) return;
        aviso.Title = titulo;
        aviso.Message = erro is UnauthorizedAccessException ? "O arquivo está protegido ou sem permissão de leitura." : erro.Message;
        aviso.IsOpen = true;
    }

    async Task EscolherArquivoAsync()
    {
        if (ocupado || escolhendo || !SessaoValida) return;
        escolhendo = true;
        AtualizarControles();
        try
        {
            // O seletor pertence à janela de recorte, não à principal que está atrás dela.
            var perguntas = Perguntas.Atual is PerguntasDaJanela ? new PerguntasDaJanela(this, () => raiz.XamlRoot) : Perguntas.Atual;
            var caminho = await perguntas.AbrirArquivoAsync(".pdf");
            if (caminho is null || !SessaoValida) return;
            var item = arquivos.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag as string, caminho, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                item = new ComboBoxItem { Content = System.IO.Path.GetFileName(caminho), Tag = caminho };
                arquivos.Items.Add(item);
            }
            // Permite tentar novamente o mesmo arquivo após falha (SelectionChanged não dispararia).
            if (ReferenceEquals(arquivos.SelectedItem, item)) await AbrirPdfAsync(caminho);
            else arquivos.SelectedItem = item;
        }
        catch (Exception e) { Avisar("Não foi possível escolher o PDF", e); }
        finally { escolhendo = false; AtualizarControles(); }
    }

    void AtualizarSelecao(Windows.Foundation.Point p)
    {
        if (inicio is not { } a) return;
        var x0 = Math.Clamp(Math.Min(a.X, p.X), 0, camada.Width);
        var y0 = Math.Clamp(Math.Min(a.Y, p.Y), 0, camada.Height);
        var x1 = Math.Clamp(Math.Max(a.X, p.X), 0, camada.Width);
        var y1 = Math.Clamp(Math.Max(a.Y, p.Y), 0, camada.Height);
        recorte = new Windows.Foundation.Rect(x0, y0, x1 - x0, y1 - y0);
        selecao.Width = x1 - x0;
        selecao.Height = y1 - y0;
        selecao.StrokeThickness = 2 / Math.Max(.05, rolagem.ZoomFactor);
        Canvas.SetLeft(selecao, x0);
        Canvas.SetTop(selecao, y0);
        selecao.Visibility = Visibility.Visible;
    }

    /// <summary>Os PDFs do processo do relatório: as pastas PROJETO (e semelhantes) e a própria pasta do processo, sem descer demais.</summary>
    async Task PreencherArquivosAsync()
    {
        ocupado = true;
        estado.Text = "Procurando PDFs do processo…";
        AtualizarControles();
        try
        {
            var caminhos = await Task.Run(() => RecortePrancha.PdfsDoProcesso(sessao.Caminho));
            if (!SessaoValida) return;
            foreach (var c in caminhos)
                arquivos.Items.Add(new ComboBoxItem { Content = System.IO.Path.GetRelativePath(RecortePrancha.PastaDoProcesso(sessao.Caminho), c), Tag = c });
        }
        catch (Exception e) { Avisar("Não foi possível procurar os PDFs", e); }
        finally { ocupado = false; AtualizarControles(); }
        if (!SessaoValida) return;
        if (arquivos.Items.Count > 0) arquivos.SelectedIndex = 0;
        else estado.Text = "Nenhum PDF encontrado. Use «Escolher arquivo…» para abrir a prancha.";
    }

    async Task AbrirPdfAsync(string caminho)
    {
        if (ocupado || !SessaoValida) return;
        ocupado = true;
        aviso.IsOpen = false;
        documento = null;
        fonteDocumento?.Dispose();
        fonteDocumento = null;
        caminhoPdf = shaPdf = null;
        LimparPagina();
        estado.Text = "Abrindo o PDF…";
        AtualizarControles();
        try
        {
            var leitura = File.OpenRead(caminho);
            IRandomAccessStream? pendente = null;
            string hash;
            try
            {
                hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(leitura));
                if (!SessaoValida) return;
                leitura.Position = 0;
                pendente = leitura.AsRandomAccessStream();
                var pdf = await PdfDocument.LoadFromStreamAsync(pendente);
                if (!SessaoValida) return;
                documento = pdf;
                fonteDocumento = pendente;
            }
            finally
            {
                if (fonteDocumento is null) { pendente?.Dispose(); leitura.Dispose(); }
            }
            shaPdf = hash;
            caminhoPdf = caminho;
            indicePagina = 0;
            if (string.IsNullOrWhiteSpace(caixaPrancha.Text) || caixaPrancha.Text == ultimaSugestao)
                caixaPrancha.Text = RecortePrancha.SugerirPrancha(caminho);
            ultimaSugestao = RecortePrancha.SugerirPrancha(caminho);
            ocupado = false;
            await MostrarPaginaAsync();
        }
        catch (Exception e)
        {
            documento = null;
            caminhoPdf = shaPdf = null;
            LimparPagina();
            estado.Text = "Escolha outro PDF ou tente abrir o arquivo novamente.";
            Avisar("Não foi possível abrir o PDF", e);
        }
        finally { ocupado = false; AtualizarControles(); }
    }

    async Task MostrarPaginaAsync()
    {
        if (documento is null || ocupado || !SessaoValida) return;
        ocupado = true;
        aviso.IsOpen = false;
        LimparPagina();
        estado.Text = "Carregando a página…";
        AtualizarControles();
        try
        {
            using var p = documento.GetPage(indicePagina);
            var tamanho = p.Size; // DIPs (1/96 pol.), já com a rotação da página
            var escalaTela = Math.Min(2.0, LadoMaximoTela / Math.Max(tamanho.Width, tamanho.Height));
            var largura = Math.Max(1, Math.Round(tamanho.Width * escalaTela));
            var altura = Math.Max(1, Math.Round(tamanho.Height * escalaTela));
            using var fluxo = new InMemoryRandomAccessStream();
            await p.RenderToStreamAsync(fluxo, new PdfPageRenderOptions { DestinationWidth = (uint)largura, DestinationHeight = (uint)altura });
            fluxo.Seek(0);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(fluxo);
            if (!SessaoValida) return;
            tamanhoPagina = tamanho;
            pagina.Source = bmp;
            superficie.Width = pagina.Width = camada.Width = largura;
            superficie.Height = pagina.Height = camada.Height = altura;
            recorte = null;
            selecao.Visibility = Visibility.Collapsed;
            inserir.IsEnabled = false;
            paginaPronta = true;
            textoPagina.Text = "Página " + (indicePagina + 1).ToString(CultureInfo.InvariantCulture) + " de " + documento.PageCount.ToString(CultureInfo.InvariantCulture);
            estado.Text = "Arraste sobre a prancha para recortar a nova figura.";
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AjustarZoom);
        }
        catch (Exception e)
        {
            LimparPagina();
            estado.Text = "A página não foi carregada. Escolha outro PDF ou tente novamente.";
            Avisar("Não foi possível mostrar a página", e);
        }
        finally { ocupado = false; AtualizarControles(); }
    }

    void AjustarZoom()
    {
        if (!paginaPronta || fechada || !double.IsFinite(superficie.Width) || superficie.Width <= 0 || rolagem.ViewportWidth <= 0 || rolagem.ViewportHeight <= 0) return;
        var z = (float)Math.Min(rolagem.ViewportWidth / superficie.Width, rolagem.ViewportHeight / superficie.Height) * .96f;
        rolagem.ChangeView(0, 0, Math.Clamp(z, rolagem.MinZoomFactor, rolagem.MaxZoomFactor), disableAnimation: true);
    }

    async Task InserirAsync()
    {
        if (!paginaPronta || documento is null || recorte is not { Width: > 8, Height: > 8 } r || caminhoPdf is null || ocupado || inicio is not null) return;
        if (!SessaoValida) { Close(); return; }
        ocupado = true;
        estado.Text = "Preparando a figura para o relatório…";
        AtualizarControles();
        try
        {
            using var p = documento.GetPage(indicePagina);
            // Pixels da tela → DIPs da página (orientação mostrada, com a rotação) → pixels a 200 dpi.
            var medidas = MedidasRecorte(r);
            var fonte = new Windows.Foundation.Rect(medidas.X, medidas.Y, medidas.Largura, medidas.Altura);
            var largura = medidas.PixelsLargura;
            var altura = medidas.PixelsAltura;
            using var fluxo = new InMemoryRandomAccessStream();
            // O recurso do relatório é PNG, inclusive quando o renderizador do Windows usa outro formato por padrão.
            await p.RenderToStreamAsync(fluxo, new PdfPageRenderOptions { BitmapEncoderId = Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId, SourceRect = fonte, DestinationWidth = largura, DestinationHeight = altura });
            var png = await CodificarRecorteAsync(fluxo, largura, altura);
            if (!SessaoValida) return;
            var chave = sessao.AdicionarImagem(png);
            var origem = new JsonObject
            {
                ["pdf"] = System.IO.Path.GetFileName(caminhoPdf),
                ["sha256"] = shaPdf,
                ["pagina"] = (int)indicePagina + 1,
                ["rotacao"] = (int)p.Rotation * 90,
                // Retângulo em pontos PDF (1/72 pol.) na orientação mostrada (com a rotação), origem no canto superior esquerdo.
                ["retangulo_pt"] = new JsonArray(Pt(fonte.X), Pt(fonte.Y), Pt(fonte.X + fonte.Width), Pt(fonte.Y + fonte.Height)),
                ["dpi"] = DpiRecorte,
                ["largura_px"] = largura,
                ["altura_px"] = altura,
            };
            var prancha = string.IsNullOrWhiteSpace(caixaPrancha.Text) ? RecortePrancha.SugerirPrancha(caminhoPdf) : caixaPrancha.Text.Trim();
            var id = "fig_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(5)).ToLowerInvariant();
            var fonteTexto = prancha + ", página " + (indicePagina + 1).ToString(CultureInfo.InvariantCulture) + ".";
            var erros = await host.AplicarFiguraAsync(new IncluirFigura(id, chave, prancha, caixaTitulo.Text, fonteTexto, origem, depoisDe));
            if (erros.Count > 0)
            {
                aviso.Title = "O recorte não foi inserido";
                aviso.Message = string.Join(Environment.NewLine, erros);
                aviso.IsOpen = true;
                return;
            }
            if (!SessaoValida) return;
            host.AbrirEditorFigura(id);
            Close();
        }
        catch (Exception e)
        {
            Avisar("Não foi possível recortar a região", e);
        }
        finally
        {
            ocupado = false;
            AtualizarControles();
        }
    }

    // O renderizador de PDF pode multiplicar DestinationWidth/Height pela escala de exibição do Windows.
    // O recurso do relatório precisa das dimensões físicas calculadas a 200 dpi em qualquer monitor.
    static async Task<byte[]> CodificarRecorteAsync(InMemoryRandomAccessStream render, uint largura, uint altura)
    {
        render.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(render);
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
            new BitmapTransform { ScaledWidth = largura, ScaledHeight = altura, InterpolationMode = BitmapInterpolationMode.Fant },
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        using var saida = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, saida);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, largura, altura, DpiRecorte, DpiRecorte, pixels.DetachPixelData());
        await encoder.FlushAsync();
        using var leitor = new DataReader(saida.GetInputStreamAt(0));
        var bytes = new byte[checked((int)saida.Size)];
        await leitor.LoadAsync((uint)bytes.Length);
        leitor.ReadBytes(bytes);
        return bytes;
    }

    static JsonNode Pt(double dip) => JsonValue.Create(Math.Round(dip * 72 / 96, 2))!;
}
