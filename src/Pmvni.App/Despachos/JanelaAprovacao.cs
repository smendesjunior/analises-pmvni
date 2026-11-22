using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pmvni.Revisao;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Pmvni.App;

internal sealed record MarcacaoAprovacao(int Pagina, bool TemCarimbo, double X, double Y, PdfSharp.Drawing.XRect? Bloco, IReadOnlyList<ObservacaoPrancha> Observacoes);

internal sealed class JanelaAprovacao : Window
{
    readonly Grid raiz = new();
    sealed partial class FolhaInterativa : Canvas
    {
        readonly InputSystemCursor mao = InputSystemCursor.Create(InputSystemCursorShape.Hand);
        readonly InputSystemCursor mira = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        internal void MostrarMao(bool navegar) => ProtectedCursor = navegar ? mao : mira;
    }

    readonly FolhaInterativa folha = new();
    uint? ponteiroDeArraste;
    Point inicioArraste;
    double inicioHorizontal, inicioVertical;
    readonly Image pagina = new();
    readonly Image selo = new() { IsHitTestVisible = false, Opacity = .9, Visibility = Visibility.Collapsed };
    readonly TextBlock notas = new() { IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Colors.Black), Visibility = Visibility.Collapsed };
    readonly ScrollViewer vista = new() { ZoomMode = ZoomMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinZoomFactor = .1f, MaxZoomFactor = 5 };
    readonly TextBlock estado = new() { VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock orientacao = new() { TextAlignment = TextAlignment.Left, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    readonly TextBlock zoom = new() { Text = "100%" };
    readonly Button anterior, proxima;
    readonly bool apenasRevisao;
    readonly TaskCompletionSource<MarcacaoAprovacao?> fim = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly List<ObservacaoPrancha> personalizadas = [];
    List<ObservacaoPrancha> escolhidas = [];
    (double Largura, double Altura)[] dimensoes = [];
    PdfDocument? pdf;
    int indice, paginaSelo = -1, paginaNotas = -1;
    double x, y, nx, ny;
    enum Colocacao { Nenhuma, Selo, Notas }
    Colocacao modo;
    bool carregando;
    readonly Button confirmar = new() { Content = "Confirmar prancha", IsEnabled = false };
    const double DipPorPonto = 96d / 72;

    JanelaAprovacao(string nome, ElementTheme tema, TaskCompletionSource<bool?>? revisao = null)
    {
        MainWindow.AplicarIconeDaJanela(this);
        Title = "Emitir processo aprovado — " + nome;
        raiz.RequestedTheme = tema;
        Content = raiz;
        apenasRevisao = revisao is not null;
        var estilos = Application.Current.Resources;
        raiz.Style = (Style)estilos["PmvniFundoJanelaStyle"];
        estado.Style = orientacao.Style = zoom.Style = (Style)estilos["PmvniSecondaryTextStyle"];
        AutomationProperties.SetLiveSetting(estado, AutomationLiveSetting.Polite);
        AutomationProperties.SetLiveSetting(orientacao, AutomationLiveSetting.Polite);
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var topo = new Grid { Padding = new Thickness(16, 8, 16, 8), ColumnSpacing = 12 };
        topo.ColumnDefinitions.Add(new ColumnDefinition());
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var barra = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var ferramentas = new ScrollViewer { Content = barra, HorizontalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled };
        topo.Children.Add(ferramentas);
        Grid.SetColumn(estado, 1);
        topo.Children.Add(estado);
        raiz.Children.Add(topo);
        Button Acao(string nomeBotao, string descricao, Func<Task> executar, string? glifo = null,
            bool apenasIcone = false, bool dicaQuandoDesabilitado = false)
        {
            var botao = new Button { Content = nomeBotao, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(botao, nomeBotao);
            Dicas.Aplicar(botao, nomeBotao, descricao);
            if (glifo is not null)
            {
                var icone = Icone(glifo);
                botao.Content = apenasIcone ? icone : new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, Children = { icone, new TextBlock { Text = nomeBotao } },
                };
            }
            if (apenasIcone)
            {
                botao.MinHeight = botao.Height = botao.Width = 32;
                botao.Padding = new Thickness(0);
            }
            botao.Click += async (_, _) =>
            {
                if (carregando) return;
                try { await executar(); }
                catch (Exception e) { estado.Text = e.Message; }
            };
            barra.Children.Add(dicaQuandoDesabilitado ? DicasDeBotoesDoDespacho.Suporte(botao) : botao);
            return botao;
        }
        anterior = Acao("Página anterior", "Mostra a página anterior da prancha.", () => ExibirAsync(indice - 1), Glifos.Voltar, dicaQuandoDesabilitado: true);
        proxima = Acao("Próxima página", "Mostra a próxima página da prancha.", () => ExibirAsync(indice + 1), Glifos.Avancar, dicaQuandoDesabilitado: true);
        Acao("Diminuir o zoom", "Reduz a ampliação da prancha.", () => { vista.ChangeView(null, null, Math.Max(.1f, vista.ZoomFactor / 1.25f)); return Task.CompletedTask; },
            Glifos.Diminuir, apenasIcone: true);
        barra.Children.Add(zoom);
        Acao("Aumentar o zoom", "Amplia a prancha.", () => { vista.ChangeView(null, null, Math.Min(5, vista.ZoomFactor * 1.25f)); return Task.CompletedTask; },
            Glifos.Aumentar, apenasIcone: true);
        if (revisao is not null)
        {
            void Finalizar(bool incluir)
            {
                revisao.TrySetResult(incluir);
                Close();
            }
            var aceitar = Acao("Aceitar prancha", "Aceita a prancha conferida.", () => { Finalizar(false); return Task.CompletedTask; });
            Brilho.SetAtivo(aceitar, false);
            aceitar.Style = (Style)estilos["PmvniBotaoPrimarioStyle"];
            Acao("Adicionar carimbo ou observações", "Abre as ferramentas de marcação da prancha.", () => { Finalizar(true); return Task.CompletedTask; });
            Closed += (_, _) => revisao.TrySetResult(null);
        }
        else
        {
            Brilho.SetAtivo(confirmar, false);
            confirmar.Style = (Style)estilos["PmvniBotaoPrimarioStyle"];
            confirmar.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetName(confirmar, "Confirmar prancha");
            Dicas.Aplicar(confirmar, "Confirmar prancha", "Confirma as marcações posicionadas na prancha.");
            barra.Children.Add(DicasDeBotoesDoDespacho.Suporte(confirmar));
            Acao("Posicionar carimbo", "Ativa o posicionamento do carimbo de aprovação.", () => { modo = Colocacao.Selo; Atualizar(); return Task.CompletedTask; });
            Acao("Adicionar observações…", "Abre a lista de observações para inserir na prancha.", EscolherNotasAsync);
            Acao("Desfazer", "Remove a última marcação desta página.", () =>
            {
                if (paginaNotas == indice) { paginaNotas = -1; escolhidas.Clear(); }
                else if (paginaSelo == indice) paginaSelo = -1;
                modo = Colocacao.Nenhuma; Atualizar(); return Task.CompletedTask;
            });
            IconeDeAcao.SetGlifo(confirmar, Glifos.Conferir);
            confirmar.Click += (_, _) =>
            {
                if (!confirmar.IsEnabled) return;
                var tamanho = dimensoes[indice];
                var larguraNotas = Math.Min(420 / DipPorPonto, tamanho.Largura);
                var topoNotas = ny / DipPorPonto;
                var area = escolhidas.Count == 0 ? (PdfSharp.Drawing.XRect?)null :
                    new PdfSharp.Drawing.XRect(Math.Clamp(nx / DipPorPonto, 0, tamanho.Largura - larguraNotas),
                        topoNotas, larguraNotas, tamanho.Altura - topoNotas);
                fim.TrySetResult(new MarcacaoAprovacao(indice, paginaSelo == indice,
                    Math.Clamp(x / DipPorPonto, 0, Math.Max(0, tamanho.Largura - CarimboAprovado.LarguraPt)),
                    Math.Clamp(y / DipPorPonto, 0, Math.Max(0, tamanho.Altura - CarimboAprovado.AlturaPt)), area, escolhidas.ToArray()));
                Close();
            };
        }
        var esc = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape };
        esc.Invoked += (_, a) => { modo = Colocacao.Nenhuma; Atualizar(); a.Handled = true; };
        raiz.KeyboardAccelerators.Add(esc);
        Closed += (_, _) => fim.TrySetResult(null);
        folha.Background = new SolidColorBrush(Colors.White);
        folha.Children.Add(pagina); folha.Children.Add(selo); folha.Children.Add(notas);
        vista.Content = folha;
        vista.HorizontalContentAlignment = HorizontalAlignment.Center;
        vista.VerticalContentAlignment = VerticalAlignment.Center;
        vista.Style = (Style)Application.Current.Resources["PmvniRolagemFundoStyle"];
        vista.ViewChanged += (_, _) => zoom.Text = vista.ZoomFactor.ToString("P0");
        var titulo = new TextBlock
        {
            Text = "Prancha arquitetônica · PDF original",
            Style = (Style)estilos["PmvniTituloSecao"],
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        AutomationProperties.SetHeadingLevel(titulo, AutomationHeadingLevel.Level2);
        var iconePrancha = Icone(Glifos.Documento);
        iconePrancha.Style = (Style)estilos["PmvniIconeDestaqueStyle"];
        var cabecalho = new Grid { ColumnSpacing = 10, Padding = new Thickness(16, 12, 16, 12) };
        cabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cabecalho.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(titulo, 1);
        cabecalho.Children.Add(iconePrancha);
        cabecalho.Children.Add(titulo);
        var quadro = PainelPrancha.Cartao();
        quadro.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        quadro.RowDefinitions.Add(new RowDefinition());
        quadro.Children.Add(cabecalho);
        Grid.SetRow(vista, 1);
        quadro.Children.Add(vista);
        quadro.Margin = new Thickness(12);
        Grid.SetRow(quadro, 1);
        raiz.Children.Add(quadro);
        orientacao.Margin = new Thickness(20, 8, 20, 12);
        Grid.SetRow(orientacao, 2);
        raiz.Children.Add(orientacao);
        selo.Width = CarimboAprovado.LarguraPt * DipPorPonto; selo.Height = CarimboAprovado.AlturaPt * DipPorPonto;
        folha.PointerWheelChanged += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift) || carregando || pagina.Source is null) return;
            var ponto = e.GetCurrentPoint(folha);
            ZoomNoPonto(ponto.Position, e.GetCurrentPoint(vista).Position, ponto.Properties.MouseWheelDelta);
            e.Handled = true;
        };
        folha.PointerMoved += (_, e) =>
        {
            if (ponteiroDeArraste == e.Pointer.PointerId)
            {
                ArrastarAte(e.GetCurrentPoint(vista).Position);
                e.Handled = true;
            }
            else if (modo != Colocacao.Nenhuma && !carregando)
            {
                var p = e.GetCurrentPoint(folha).Position;
                Posicionar(p.X, p.Y, false);
            }
        };
        folha.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(folha);
            if (!p.Properties.IsLeftButtonPressed || carregando || pagina.Source is null) return;
            if (modo != Colocacao.Nenhuma) Posicionar(p.Position.X, p.Position.Y, true);
            else if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && folha.CapturePointer(e.Pointer))
            {
                ponteiroDeArraste = e.Pointer.PointerId;
                IniciarArraste(e.GetCurrentPoint(vista).Position);
            }
            else return;
            e.Handled = true;
        };
        folha.PointerReleased += (_, e) =>
        {
            if (ponteiroDeArraste != e.Pointer.PointerId) return;
            ArrastarAte(e.GetCurrentPoint(vista).Position);
            ponteiroDeArraste = null;
            folha.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        };
        folha.PointerCaptureLost += (_, e) => { if (ponteiroDeArraste == e.Pointer.PointerId) ponteiroDeArraste = null; };
        folha.PointerCanceled += (_, e) => { if (ponteiroDeArraste == e.Pointer.PointerId) ponteiroDeArraste = null; };
        raiz.Loaded += (_, _) => PainelPrancha.Dimensionar(this);
        Atualizar();
    }

    static FontIcon Icone(string glifo)
    {
        var icone = new FontIcon { Glyph = glifo, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(icone, AccessibilityView.Raw);
        return icone;
    }

    void ZoomNoPonto(Point naFolha, Point naVista, int delta)
    {
        var fator = (float)Math.Clamp(vista.ZoomFactor * Math.Pow(1.25, delta / 120.0), vista.MinZoomFactor, vista.MaxZoomFactor);
        var horizontal = Math.Clamp(naFolha.X * fator - naVista.X, 0, Math.Max(0, folha.Width * fator - vista.ViewportWidth));
        var vertical = Math.Clamp(naFolha.Y * fator - naVista.Y, 0, Math.Max(0, folha.Height * fator - vista.ViewportHeight));
        vista.ChangeView(horizontal, vertical, fator, disableAnimation: true);
    }

    void IniciarArraste(Point ponto)
    {
        inicioArraste = ponto;
        inicioHorizontal = vista.HorizontalOffset;
        inicioVertical = vista.VerticalOffset;
    }

    void ArrastarAte(Point ponto) => vista.ChangeView(inicioHorizontal + inicioArraste.X - ponto.X,
        inicioVertical + inicioArraste.Y - ponto.Y, null, disableAnimation: true);

    void Posicionar(double px, double py, bool aceita)
    {
        bool carimbo = modo == Colocacao.Selo;
        FrameworkElement elemento = carimbo ? selo : notas;
        if (!carimbo && escolhidas.Count == 0) return;
        var largura = carimbo ? selo.Width : Math.Min(420, folha.Width);
        if (largura > folha.Width || carimbo && selo.Height > folha.Height) return;
        elemento.Width = largura;
        px = Math.Clamp(px, 0, folha.Width - largura);
        elemento.Visibility = Visibility.Visible;
        if (!carimbo) notas.Measure(new Windows.Foundation.Size(largura, double.PositiveInfinity));
        py = Math.Clamp(py, 0, Math.Max(0, folha.Height - (carimbo ? selo.Height : notas.DesiredSize.Height)));
        Canvas.SetLeft(elemento, px); Canvas.SetTop(elemento, py); elemento.Visibility = Visibility.Visible;
        if (!aceita) return;
        if (carimbo) { x = px; y = py; paginaSelo = indice; }
        else { nx = px; ny = py; paginaNotas = indice; }
        modo = Colocacao.Nenhuma;
        Atualizar();
    }

    void Atualizar()
    {
        folha.MostrarMao(modo == Colocacao.Nenhuma);
        selo.Visibility = paginaSelo == indice ? Visibility.Visible : Visibility.Collapsed;
        notas.Visibility = paginaNotas == indice ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(selo, x); Canvas.SetTop(selo, y); Canvas.SetLeft(notas, nx); Canvas.SetTop(notas, ny);
        confirmar.IsEnabled = !carregando && (paginaSelo == indice || paginaNotas == indice)
            && (paginaSelo < 0 || paginaSelo == indice) && (escolhidas.Count == 0 || paginaNotas == indice);
        anterior.IsEnabled = !carregando && indice > 0;
        proxima.IsEnabled = !carregando && pdf is not null && indice + 1 < pdf.PageCount;
        string? aguarde = carregando || pdf is null ? "Aguarde o carregamento da prancha." : null;
        DicasDeBotoesDoDespacho.Atualizar(anterior, aguarde ?? "Esta é a primeira página.");
        DicasDeBotoesDoDespacho.Atualizar(proxima, aguarde ?? "Esta é a última página.");
        DicasDeBotoesDoDespacho.Atualizar(confirmar, aguarde ?? "Posicione as marcações na página para confirmar.");
        estado.Text = pdf is null ? "Carregando…" : $"Página {indice + 1} de {pdf.PageCount}";
        orientacao.Text = modo switch
        {
            Colocacao.Selo => "Clique na prancha para posicionar o carimbo de aprovação.",
            Colocacao.Notas => "Clique na prancha para posicionar as observações.",
            _ when apenasRevisao => "Confira a prancha e escolha se deseja acrescentar uma marcação.",
            _ when confirmar.IsEnabled => "Prancha preparada para confirmação.",
            _ => "Arraste para mover a prancha. Use Shift + roda para ampliar ou reduzir. Selecione o carimbo ou as observações para posicioná-los.",
        };
    }

    async Task EscolherNotasAsync()
    {
        var painel = new StackPanel { Spacing = 8 };
        var itens = ObservacaoPrancha.Predefinidas.Concat(personalizadas).ToList();
        var caixas = itens.Select(o => new CheckBox { Content = new TextBlock { Text = o.Texto + " " + o.Referencia, TextWrapping = TextWrapping.Wrap }, IsChecked = escolhidas.Contains(o) }).ToList();
        foreach (var c in caixas) painel.Children.Add(c);
        var nova = new TextBox { Header = "Criar nova observação", AcceptsReturn = true };
        var referencia = new TextBox { Header = "Referência (opcional)" };
        painel.Children.Add(nova); painel.Children.Add(referencia);
        var dialogo = new ContentDialog { XamlRoot = raiz.XamlRoot, Title = "Adicionar observações", Content = new ScrollViewer { Content = painel, MaxHeight = 520 }, PrimaryButtonText = "Aplicar", CloseButtonText = "Cancelar" };
        if (await dialogo.ShowAsync() != ContentDialogResult.Primary) return;
        escolhidas = itens.Where((_, i) => caixas[i].IsChecked == true).ToList();
        if (!string.IsNullOrWhiteSpace(nova.Text))
        {
            var obs = new ObservacaoPrancha(nova.Text.Trim(), referencia.Text.Trim());
            personalizadas.Add(obs); escolhidas.Add(obs);
        }
        notas.Text = "OBSERVAÇÕES:\n" + string.Join("\n\n", escolhidas.Select((o,i) => $"{i+1:00} – {o.Texto} {o.Referencia}"));
        notas.FontSize = 12;
        modo = escolhidas.Count == 0 ? Colocacao.Nenhuma : Colocacao.Notas;
        paginaNotas = -1; Atualizar();
    }

    async Task ExibirAsync(int nova)
    {
        if (carregando || pdf is null || nova < 0 || nova >= pdf.PageCount) return;
        carregando = true;
        try
        {
            using var p = pdf.GetPage((uint)nova);
            using var buffer = new InMemoryRandomAccessStream();
            var largura = (uint)Math.Clamp(p.Size.Width * 1.5, 1, 3200);
            await p.RenderToStreamAsync(buffer, new PdfPageRenderOptions { DestinationWidth = largura });
            buffer.Seek(0);
            var imagem = new BitmapImage(); await imagem.SetSourceAsync(buffer);
            var tamanho = dimensoes[nova];
            folha.Width = pagina.Width = tamanho.Largura * DipPorPonto;
            folha.Height = pagina.Height = tamanho.Altura * DipPorPonto;
            pagina.Stretch = Stretch.Fill;
            pagina.Source = imagem; indice = nova; modo = Colocacao.Nenhuma;
            vista.ChangeView(0, 0, (float)Math.Clamp(Math.Min(Math.Max(1, vista.ActualWidth - 24) / folha.Width, Math.Max(1, vista.ActualHeight - 24) / folha.Height), .1, 1));
        }
        finally { carregando = false; Atualizar(); }
    }

    public static async Task<bool?> RevisarAsync(byte[] bytes, string nome, int pagina, ElementTheme tema)
    {
        var revisao = new TaskCompletionSource<bool?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var janela = new JanelaAprovacao(nome, tema, revisao);
        janela.Activate();
        try
        {
            using var stream = new MemoryStream(bytes).AsRandomAccessStream();
            janela.dimensoes = await Task.Run(() => CarimboAprovado.DimensoesVisiveis(bytes));
            janela.pdf = await PdfDocument.LoadFromStreamAsync(stream);
            await janela.ExibirAsync(pagina);
            return await revisao.Task;
        }
        catch { janela.Close(); throw; }
    }

    internal static async Task<bool> ConferirNoRoteiroAsync(byte[] bytes, string pasta)
    {
        var resultado = await SelecionarAsync(bytes, "Prancha de teste.pdf", ElementTheme.Light, async janela =>
        {
            if (janela.pdf!.PageCount < 2) throw new InvalidOperationException("O teste de navegação precisa de duas páginas.");
            await janela.ExibirAsync(1);
            if (!janela.anterior.IsEnabled || janela.indice != 1) throw new InvalidOperationException("A navegação não mudou a página.");
            await janela.ExibirAsync(0);
            await Roteiro.CapturarXamlAsync(janela.raiz, Path.Combine(pasta, "29b-emissao.png"));
            var fator = janela.vista.ZoomFactor;
            var centro = new Point(janela.folha.Width / 2, janela.folha.Height / 2);
            var ponteiro = new Point(janela.vista.ViewportWidth / 2, janela.vista.ViewportHeight / 2);
            janela.ZoomNoPonto(centro, ponteiro, 360);
            await Task.Delay(150);
            if (janela.vista.ZoomFactor <= fator || janela.confirmar.IsEnabled) throw new InvalidOperationException("Falha no zoom ou na confirmação inicial.");
            janela.IniciarArraste(ponteiro);
            var xAntes = janela.vista.HorizontalOffset;
            var yAntes = janela.vista.VerticalOffset;
            janela.ArrastarAte(new Point(ponteiro.X - 30, ponteiro.Y - 20));
            await Task.Delay(150);
            if (janela.vista.HorizontalOffset <= xAntes && janela.vista.VerticalOffset <= yAntes)
                throw new InvalidOperationException("O arraste não deslocou a prancha ampliada.");
            janela.ZoomNoPonto(centro, ponteiro, -120);
            await Task.Delay(150);
            janela.modo = Colocacao.Selo;
            janela.Posicionar(45, 55, true);
            if (!janela.confirmar.IsEnabled) throw new InvalidOperationException("O carimbo não habilitou a confirmação.");
            var peer = new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer(janela.confirmar);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        });
        return resultado is { Pagina: 0, TemCarimbo: true } && Math.Abs(resultado.X - 45 / DipPorPonto) < .01;
    }

    public static async Task<MarcacaoAprovacao?> SelecionarAsync(byte[] bytes, string nome, ElementTheme tema,
        Func<JanelaAprovacao, Task>? aoCarregar = null)
    {
        var janela = new JanelaAprovacao(nome, tema);
        janela.Activate();
        try
        {
            using var stream = new MemoryStream(bytes).AsRandomAccessStream();
            janela.dimensoes = await Task.Run(() => CarimboAprovado.DimensoesVisiveis(bytes));
            janela.pdf = await PdfDocument.LoadFromStreamAsync(stream);
            using var arte = typeof(CarimboAprovado).Assembly.GetManifestResourceStream("Pmvni.CarimboAprovado.pdf")!;
            using var copia = new MemoryStream(); arte.CopyTo(copia); copia.Position = 0;
            using var modelo = copia.AsRandomAccessStream();
            var pdfSelo = await PdfDocument.LoadFromStreamAsync(modelo);
            using var pag = pdfSelo.GetPage(0);
            using var imagem = new InMemoryRandomAccessStream();
            await pag.RenderToStreamAsync(imagem, new PdfPageRenderOptions { DestinationWidth = 700 });
            imagem.Seek(0); var bmp = new BitmapImage(); await bmp.SetSourceAsync(imagem); janela.selo.Source = bmp;
            await janela.ExibirAsync(0);
            if (aoCarregar is not null) await aoCarregar(janela);
            return await janela.fim.Task;
        }
        catch { janela.Close(); throw; }
    }
}
