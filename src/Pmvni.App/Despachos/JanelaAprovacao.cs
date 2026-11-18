using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pmvni.Revisao;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace Pmvni.App;

internal sealed record MarcacaoAprovacao(int Pagina, double X, double Y, PdfSharp.Drawing.XRect? Bloco, IReadOnlyList<ObservacaoPrancha> Observacoes);

internal sealed class JanelaAprovacao : Window
{
    readonly Grid raiz = new();
    readonly Canvas folha = new();
    readonly Image pagina = new();
    readonly Image selo = new() { IsHitTestVisible = false, Opacity = .9, Visibility = Visibility.Collapsed };
    readonly TextBlock notas = new() { IsHitTestVisible = false, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Colors.Black), Visibility = Visibility.Collapsed };
    readonly ScrollViewer vista = new() { ZoomMode = ZoomMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinZoomFactor = .1f, MaxZoomFactor = 5 };
    readonly TextBlock estado = new() { VerticalAlignment = VerticalAlignment.Center };
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
        Title = "Emitir Processo Aprovado — " + nome;
        raiz.RequestedTheme = tema;
        Content = raiz;
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var barra = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8) };
        raiz.Children.Add(barra);
        Button Acao(string nomeBotao, Func<Task> executar)
        {
            var botao = new Button { Content = nomeBotao };
            botao.Click += async (_, _) =>
            {
                if (carregando) return;
                try { await executar(); }
                catch (Exception e) { estado.Text = e.Message; }
            };
            barra.Children.Add(botao);
            return botao;
        }
        Acao("Anterior", () => ExibirAsync(indice - 1));
        Acao("Próxima", () => ExibirAsync(indice + 1));
        Acao("−", () => { vista.ChangeView(null, null, Math.Max(.1f, vista.ZoomFactor / 1.25f)); return Task.CompletedTask; });
        Acao("+", () => { vista.ChangeView(null, null, Math.Min(5, vista.ZoomFactor * 1.25f)); return Task.CompletedTask; });
        if (revisao is not null)
        {
            void Finalizar(bool incluir)
            {
                revisao.TrySetResult(incluir);
                Close();
            }
            Acao("Adicionar outro carimbo", () => { Finalizar(true); return Task.CompletedTask; });
            Acao("Aceitar esta prancha", () => { Finalizar(false); return Task.CompletedTask; });
            Closed += (_, _) => revisao.TrySetResult(null);
        }
        else
        {
            Acao("Carimbo Aprovado", () => { modo = Colocacao.Selo; return Task.CompletedTask; });
            Acao("Adicionar observações", EscolherNotasAsync);
            Acao("Desfazer", () =>
            {
                if (paginaNotas == indice) { paginaNotas = -1; escolhidas.Clear(); }
                else if (paginaSelo == indice) paginaSelo = -1;
                modo = Colocacao.Nenhuma; Atualizar(); return Task.CompletedTask;
            });
            barra.Children.Add(confirmar);
            barra.Children.Add(estado);
            confirmar.Click += (_, _) =>
            {
                if (paginaSelo < 0 || escolhidas.Count != 0 && paginaNotas != paginaSelo) return;
                var area = escolhidas.Count == 0 ? (PdfSharp.Drawing.XRect?)null :
                    new PdfSharp.Drawing.XRect(nx / DipPorPonto, ny / DipPorPonto, notas.Width / DipPorPonto, (folha.Height - ny) / DipPorPonto);
                fim.TrySetResult(new MarcacaoAprovacao(paginaSelo, x / DipPorPonto, y / DipPorPonto, area, escolhidas.ToArray()));
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
        Grid.SetRow(vista, 1); raiz.Children.Add(vista);
        selo.Width = CarimboAprovado.LarguraPt * DipPorPonto; selo.Height = CarimboAprovado.AlturaPt * DipPorPonto;
        folha.PointerMoved += (_, e) =>
        {
            var p = e.GetCurrentPoint(folha).Position;
            if (modo != Colocacao.Nenhuma) Posicionar(p.X, p.Y, false);
        };
        folha.PointerPressed += (_, e) =>
        {
            var p = e.GetCurrentPoint(folha);
            if (modo == Colocacao.Nenhuma || !p.Properties.IsLeftButtonPressed) return;
            Posicionar(p.Position.X, p.Position.Y, true); e.Handled = true;
        };
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1350, 900));
    }

    void Posicionar(double px, double py, bool aceita)
    {
        bool carimbo = modo == Colocacao.Selo;
        FrameworkElement elemento = carimbo ? selo : notas;
        if (!carimbo && escolhidas.Count == 0) return;
        var largura = carimbo ? selo.Width : Math.Min(420, folha.Width);
        if (largura > folha.Width || carimbo && selo.Height > folha.Height) return;
        elemento.Width = largura;
        px = Math.Clamp(px, 0, folha.Width - largura);
        py = Math.Clamp(py, 0, Math.Max(0, folha.Height - (carimbo ? selo.Height : notas.ActualHeight)));
        Canvas.SetLeft(elemento, px); Canvas.SetTop(elemento, py); elemento.Visibility = Visibility.Visible;
        if (!aceita) return;
        if (carimbo) { x = px; y = py; paginaSelo = indice; }
        else { nx = px; ny = py; paginaNotas = indice; }
        modo = Colocacao.Nenhuma;
        Atualizar();
    }

    void Atualizar()
    {
        selo.Visibility = paginaSelo == indice ? Visibility.Visible : Visibility.Collapsed;
        notas.Visibility = paginaNotas == indice ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(selo, x); Canvas.SetTop(selo, y); Canvas.SetLeft(notas, nx); Canvas.SetTop(notas, ny);
        confirmar.IsEnabled = !carregando && paginaSelo == indice && (escolhidas.Count == 0 || paginaNotas == indice);
        estado.Text = $"Página {indice + 1} de {pdf?.PageCount}";
    }

    async Task EscolherNotasAsync()
    {
        var painel = new StackPanel { Spacing = 6 };
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
            vista.ChangeView(0, 0, (float)Math.Clamp(Math.Min(1050 / p.Size.Width, 720 / p.Size.Height), .1, 1));
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

    public static async Task<MarcacaoAprovacao?> SelecionarAsync(byte[] bytes, string nome, ElementTheme tema)
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
            return await janela.fim.Task;
        }
        catch { janela.Close(); throw; }
    }
}
