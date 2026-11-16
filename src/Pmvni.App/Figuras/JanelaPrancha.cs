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

    readonly Grid raiz = new();
    readonly ComboBox arquivos = new() { MinWidth = 320, PlaceholderText = "Escolha o PDF das pranchas" };
    readonly Button escolher = new() { Content = "Escolher arquivo…" };
    readonly Button anterior = new() { Content = "◂" };
    readonly Button proxima = new() { Content = "▸" };
    readonly TextBlock textoPagina = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 120, TextAlignment = TextAlignment.Center };
    readonly ScrollViewer rolagem = new() { ZoomMode = ZoomMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinZoomFactor = .05f, MaxZoomFactor = 8 };
    readonly Grid superficie = new() { Background = new SolidColorBrush(Colors.White), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Image pagina = new() { Stretch = Stretch.Fill };
    readonly Canvas camada = new() { Background = new SolidColorBrush(Colors.Transparent) };
    readonly Rectangle selecao = new() { Stroke = new SolidColorBrush(Color.FromArgb(255, 0, 0x67, 0xC0)), StrokeThickness = 2, StrokeDashArray = { 5, 3 }, Fill = new SolidColorBrush(Color.FromArgb(40, 0, 0x67, 0xC0)), Visibility = Visibility.Collapsed };
    readonly TextBox caixaPrancha = new() { Header = "Prancha (etiqueta azul)", Width = 160 };
    readonly TextBox caixaTitulo = new() { Header = "Título da figura", Width = 360, Text = "Recorte da prancha" };
    readonly Button inserir = new() { Content = "Inserir no relatório", IsEnabled = false };
    readonly Button cancelar = new() { Content = "Cancelar" };
    readonly TextBlock estado = new() { TextWrapping = TextWrapping.Wrap, Opacity = .8, VerticalAlignment = VerticalAlignment.Center };
    readonly InfoBar aviso = new() { Severity = InfoBarSeverity.Warning, IsClosable = true };

    PdfDocument? documento;
    string? caminhoPdf;
    string? shaPdf;
    uint indicePagina;
    double escalaTela = 1; // pixels da tela por DIP da página
    Windows.Foundation.Point? inicio;
    Windows.Foundation.Rect? recorte; // em pixels da página mostrada
    bool ocupado;

    public JanelaPrancha(IHostFiguras host, SessaoEdicao sessao, string? depoisDe)
    {
        this.host = host;
        this.sessao = sessao;
        this.depoisDe = depoisDe;
        Title = "Inserir região da prancha — Editor de Relatórios PMVNI";
        Montar();
        Content = raiz;
        raiz.RequestedTheme = host.TemaAtual;
        try { AppWindow.Resize(new Windows.Graphics.SizeInt32(1400, 950)); } catch (Exception) { /* conveniência */ }
        PreencherArquivos();
    }

    void Montar()
    {
        raiz.Background = (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"];
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var topo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Padding = new Thickness(12, 10, 12, 6) };
        topo.Children.Add(arquivos);
        topo.Children.Add(escolher);
        topo.Children.Add(new AppBarSeparator());
        topo.Children.Add(anterior);
        topo.Children.Add(textoPagina);
        topo.Children.Add(proxima);
        topo.Children.Add(new AppBarSeparator());
        var menos = new Button { Content = "−" };
        var mais = new Button { Content = "+" };
        var ajustar = new Button { Content = "Página inteira" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(menos, "Diminuir");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mais, "Aumentar");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(anterior, "Página anterior");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(proxima, "Próxima página");
        menos.Click += (_, _) => rolagem.ChangeView(null, null, Math.Max(rolagem.MinZoomFactor, rolagem.ZoomFactor / 1.25f));
        mais.Click += (_, _) => rolagem.ChangeView(null, null, Math.Min(rolagem.MaxZoomFactor, rolagem.ZoomFactor * 1.25f));
        ajustar.Click += (_, _) => AjustarZoom();
        topo.Children.Add(menos);
        topo.Children.Add(mais);
        topo.Children.Add(ajustar);
        raiz.Children.Add(topo);

        aviso.IsOpen = false;
        Grid.SetRow(aviso, 1);
        raiz.Children.Add(aviso);

        superficie.Children.Add(pagina);
        camada.Children.Add(selecao);
        superficie.Children.Add(camada);
        rolagem.Content = superficie;
        rolagem.Background = new SolidColorBrush(Color.FromArgb(255, 0x8a, 0x8a, 0x8a));
        Grid.SetRow(rolagem, 2);
        raiz.Children.Add(rolagem);

        var rodape = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Padding = new Thickness(12, 8, 12, 12) };
        rodape.Children.Add(caixaPrancha);
        rodape.Children.Add(caixaTitulo);
        inserir.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        inserir.VerticalAlignment = cancelar.VerticalAlignment = VerticalAlignment.Bottom;
        rodape.Children.Add(inserir);
        rodape.Children.Add(cancelar);
        rodape.Children.Add(estado);
        Grid.SetRow(rodape, 3);
        raiz.Children.Add(rodape);
        estado.Text = "Escolha a página, amplie se precisar e arraste o mouse sobre a região a recortar.";

        arquivos.SelectionChanged += async (_, _) => { if (arquivos.SelectedItem is ComboBoxItem { Tag: string c }) await AbrirPdfAsync(c); };
        escolher.Click += async (_, _) =>
        {
            var c = await Perguntas.Atual.AbrirArquivoAsync(".pdf");
            if (c is null) return;
            var item = new ComboBoxItem { Content = System.IO.Path.GetFileName(c), Tag = c };
            arquivos.Items.Add(item);
            arquivos.SelectedItem = item;
        };
        anterior.Click += async (_, _) => { if (documento is not null && indicePagina > 0) { indicePagina--; await MostrarPaginaAsync(); } };
        proxima.Click += async (_, _) => { if (documento is not null && indicePagina + 1 < documento.PageCount) { indicePagina++; await MostrarPaginaAsync(); } };
        cancelar.Click += (_, _) => Close();
        inserir.Click += async (_, _) => await InserirAsync();
        camada.PointerPressed += (_, e) =>
        {
            if (documento is null || ocupado) return;
            inicio = e.GetCurrentPoint(camada).Position;
            camada.CapturePointer(e.Pointer);
            AtualizarSelecao(inicio.Value);
        };
        camada.PointerMoved += (_, e) => { if (inicio is not null) AtualizarSelecao(e.GetCurrentPoint(camada).Position); };
        camada.PointerReleased += (_, e) =>
        {
            if (inicio is null) return;
            AtualizarSelecao(e.GetCurrentPoint(camada).Position);
            camada.ReleasePointerCapture(e.Pointer);
            inicio = null;
            inserir.IsEnabled = recorte is { Width: > 8, Height: > 8 };
            if (recorte is { } r)
                estado.Text = "Recorte de " + Medida(r.Width / escalaTela) + " × " + Medida(r.Height / escalaTela) + " na prancha; sai a "
                              + DpiRecorte.ToString(CultureInfo.InvariantCulture) + " dpi (" + Px(r.Width) + " × " + Px(r.Height) + " pixels).";
        };
    }

    static string Medida(double dip) => (dip / 96 * 25.4).ToString("0", CultureInfo.InvariantCulture) + " mm";

    string Px(double telaPx) => Math.Round(telaPx / escalaTela * DpiRecorte / 96).ToString("0", CultureInfo.InvariantCulture);

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
    void PreencherArquivos()
    {
        foreach (var c in RecortePrancha.PdfsDoProcesso(sessao.Caminho))
            arquivos.Items.Add(new ComboBoxItem { Content = System.IO.Path.GetRelativePath(RecortePrancha.PastaDoProcesso(sessao.Caminho), c), Tag = c });
        if (arquivos.Items.Count > 0) arquivos.SelectedIndex = 0;
        else estado.Text = "Nenhum PDF de prancha foi achado na pasta do processo. Use «Escolher arquivo…».";
    }

    async Task AbrirPdfAsync(string caminho)
    {
        if (ocupado) return;
        ocupado = true;
        aviso.IsOpen = false;
        try
        {
            var bytes = await Task.Run(() => File.ReadAllBytes(caminho));
            shaPdf = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var arquivo = await StorageFile.GetFileFromPathAsync(caminho);
            documento = await PdfDocument.LoadFromFileAsync(arquivo);
            caminhoPdf = caminho;
            indicePagina = 0;
            if (string.IsNullOrWhiteSpace(caixaPrancha.Text)) caixaPrancha.Text = RecortePrancha.SugerirPrancha(caminho);
            ocupado = false;
            await MostrarPaginaAsync();
        }
        catch (Exception e)
        {
            documento = null;
            aviso.Title = "Não foi possível abrir o PDF";
            aviso.Message = e is UnauthorizedAccessException ? "O arquivo está protegido ou sem permissão de leitura." : e.Message;
            aviso.IsOpen = true;
        }
        finally { ocupado = false; }
    }

    async Task MostrarPaginaAsync()
    {
        if (documento is null || ocupado) return;
        ocupado = true;
        try
        {
            using var p = documento.GetPage(indicePagina);
            var tamanho = p.Size; // DIPs (1/96 pol.), já com a rotação da página
            escalaTela = Math.Min(2.0, LadoMaximoTela / Math.Max(tamanho.Width, tamanho.Height));
            var largura = Math.Max(1, Math.Round(tamanho.Width * escalaTela));
            var altura = Math.Max(1, Math.Round(tamanho.Height * escalaTela));
            using var fluxo = new InMemoryRandomAccessStream();
            await p.RenderToStreamAsync(fluxo, new PdfPageRenderOptions { DestinationWidth = (uint)largura, DestinationHeight = (uint)altura });
            fluxo.Seek(0);
            var bmp = new BitmapImage();
            await bmp.SetSourceAsync(fluxo);
            pagina.Source = bmp;
            superficie.Width = pagina.Width = camada.Width = largura;
            superficie.Height = pagina.Height = camada.Height = altura;
            recorte = null;
            selecao.Visibility = Visibility.Collapsed;
            inserir.IsEnabled = false;
            textoPagina.Text = "Página " + (indicePagina + 1).ToString(CultureInfo.InvariantCulture) + " de " + documento.PageCount.ToString(CultureInfo.InvariantCulture);
            anterior.IsEnabled = indicePagina > 0;
            proxima.IsEnabled = indicePagina + 1 < documento.PageCount;
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, AjustarZoom);
        }
        catch (Exception e)
        {
            aviso.Title = "Não foi possível mostrar a página";
            aviso.Message = e.Message;
            aviso.IsOpen = true;
        }
        finally { ocupado = false; }
    }

    void AjustarZoom()
    {
        if (superficie.Width is double.NaN or <= 0 || rolagem.ViewportWidth <= 0) return;
        var z = (float)Math.Min(rolagem.ViewportWidth / superficie.Width, rolagem.ViewportHeight / superficie.Height) * .96f;
        rolagem.ChangeView(0, 0, Math.Clamp(z, rolagem.MinZoomFactor, rolagem.MaxZoomFactor), disableAnimation: true);
    }

    async Task InserirAsync()
    {
        if (documento is null || recorte is not { } r || caminhoPdf is null || ocupado) return;
        if (host.Sessao != sessao) { Close(); return; }
        ocupado = true;
        inserir.IsEnabled = false;
        try
        {
            using var p = documento.GetPage(indicePagina);
            // Pixels da tela → DIPs da página (orientação mostrada, com a rotação) → pixels a 200 dpi.
            var fonte = new Windows.Foundation.Rect(r.X / escalaTela, r.Y / escalaTela, r.Width / escalaTela, r.Height / escalaTela);
            var largura = (uint)Math.Max(1, Math.Round(fonte.Width * DpiRecorte / 96));
            var altura = (uint)Math.Max(1, Math.Round(fonte.Height * DpiRecorte / 96));
            using var fluxo = new InMemoryRandomAccessStream();
            // Sem BitmapEncoderId: PNG (sem perdas).
            await p.RenderToStreamAsync(fluxo, new PdfPageRenderOptions { SourceRect = fonte, DestinationWidth = largura, DestinationHeight = altura });
            var png = new byte[fluxo.Size];
            fluxo.Seek(0);
            using (var leitor = new DataReader(fluxo.GetInputStreamAt(0)))
            {
                await leitor.LoadAsync((uint)fluxo.Size);
                leitor.ReadBytes(png);
            }
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
            host.AbrirEditorFigura(id);
            Close();
        }
        catch (Exception e)
        {
            aviso.Title = "Não foi possível recortar a região";
            aviso.Message = e.Message;
            aviso.IsOpen = true;
        }
        finally
        {
            ocupado = false;
            inserir.IsEnabled = recorte is not null;
        }
    }

    static JsonNode Pt(double dip) => JsonValue.Create(Math.Round(dip * 72 / 96, 2))!;
}
