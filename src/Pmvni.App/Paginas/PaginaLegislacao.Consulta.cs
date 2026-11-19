using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

public sealed partial class PaginaLegislacao
{
    private async void ConsultarOutrasLeis(object sender, RoutedEventArgs args)
    {
        var termo = new TextBox { Header = "Número, assunto ou palavra-chave", PlaceholderText = "Ex.: plano diretor" };
        var buscar = new Button { Content = "Pesquisar" };
        var aviso = new TextBlock { Text = "Escolha o acervo no portal. Depois, pesquise por número ou assunto.", TextWrapping = TextWrapping.Wrap };
        var resultados = new ListView { Visibility = Visibility.Collapsed, MaxHeight = 310, IsItemClickEnabled = true, SelectionMode = ListViewSelectionMode.None };
        var fonte = new WebView2 { Height = 310 };
        var mostrar = new ToggleSwitch { IsOn = true, Header = "Selecionar acervo ou ler norma no Leis.org", OnContent = "Visível", OffContent = "Oculta" };
        mostrar.Toggled += (_, _) => {
            fonte.Visibility = mostrar.IsOn ? Visibility.Visible : Visibility.Collapsed;
            resultados.Visibility = mostrar.IsOn ? Visibility.Collapsed : Visibility.Visible;
        };
        var painel = new StackPanel { Spacing = 8, MinWidth = 320 };
        foreach (var item in new UIElement[] { termo, buscar, aviso, mostrar, resultados, fonte }) painel.Children.Add(item);
        var dialogo = new ContentDialog { Title = "Consultar outras leis", XamlRoot = XamlRoot, Content = new ScrollViewer { Content = painel }, CloseButtonText = "Fechar" };
        using var vida = new CancellationTokenSource();
        var token = vida.Token;
        Uri? acervo = null;
        fonte.Source = new Uri("https://leis.org/");
        fonte.NavigationCompleted += (_, e) =>
        {
            if (!e.IsSuccess || fonte.Source is not { Host: "leis.org" } atual) return;
            var partes = atual.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int tamanho = partes.FirstOrDefault() switch
            {
                "municipais" when partes.Length >= 3 => 3,
                "estaduais" or "institucionais" when partes.Length >= 2 => 2,
                "federais" => 1,
                _ => 0
            };
            if (tamanho > 0)
                acervo = new Uri("https://leis.org/" + string.Join("/", partes.Take(tamanho)) + "/");
            else acervo = null;
        };
        async void Pesquisar(object? origem, RoutedEventArgs evento)
        {
            if (!buscar.IsEnabled || string.IsNullOrWhiteSpace(termo.Text)) return;
            if (acervo is null)
            {
                mostrar.IsOn = true;
                aviso.Text = "Selecione no portal o município, estado ou acervo que deseja consultar.";
                return;
            }
            var uri = new Uri(acervo, "busca?termo=" + Uri.EscapeDataString(termo.Text.Trim()));
            buscar.IsEnabled = false;
            resultados.Items.Clear();
            mostrar.IsOn = false;
            aviso.Text = "Consultando o Leis.org…";
            try
            {
                await fonte.EnsureCoreWebView2Async();
                token.ThrowIfCancellationRequested();
                var navegou = new TaskCompletionSource<bool>();
                void Pronta(WebView2 w, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e) => navegou.TrySetResult(e.IsSuccess);
                fonte.NavigationCompleted += Pronta;
                try
                {
                    fonte.CoreWebView2.Navigate(uri.AbsoluteUri);
                    if (!await navegou.Task.WaitAsync(TimeSpan.FromSeconds(30), token)) throw new InvalidOperationException();
                }
                finally { fonte.NavigationCompleted -= Pronta; }
                for (int i = 0; i < 12; i++)
                {
                    token.ThrowIfCancellationRequested();
                    var json = await fonte.ExecuteScriptAsync("""
                        Array.from(document.querySelectorAll('a[href*="/lei/"]'), a => [a.href, a.innerText.trim()]).filter(x => x[1])
                        """);
                    var links = JsonSerializer.Deserialize<string[][]>(json) ?? [];
                    foreach (var link in links.DistinctBy(x => x[0]))
                        if (Uri.TryCreate(link[0], UriKind.Absolute, out var lei) && lei.Scheme == "https" && lei.Host == "leis.org")
                            resultados.Items.Add(new TextBlock { Text = link[1], Tag = lei, TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.CharacterEllipsis });
                    if (resultados.Items.Count > 0)
                    {
                        aviso.Text = "Selecione uma norma para ler a íntegra.";
                        return;
                    }
                    await Task.Delay(500, token);
                }
                mostrar.IsOn = true;
                aviso.Text = "Confira o resultado na página do Leis.org abaixo.";
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) when (!token.IsCancellationRequested) { aviso.Text = "A consulta não foi concluída. Tente novamente ou confira a página do Leis.org."; }
            catch (Exception) when (token.IsCancellationRequested) { }
            finally { buscar.IsEnabled = true; }
        }
        buscar.Click += Pesquisar;
        resultados.ItemClick += (_, e) => {
            if (e.ClickedItem is TextBlock { Tag: Uri lei }) { mostrar.IsOn = true; fonte.Source = lei; }
        };
        try { await dialogo.ShowAsync(); }
        finally { vida.Cancel(); fonte.Close(); }
    }
}
