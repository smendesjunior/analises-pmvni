using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

public sealed partial class PaginaLegislacao
{
    private bool iniciandoPortal;
    private bool portalPronto;
    private Uri? acervoPortal;
    private readonly Dictionary<string, string> pdfsDaConsulta = new();

    internal async Task AbrirConsultaAsync()
    {
        Consulta.Visibility = Visibility.Visible;
        if (portalPronto || iniciandoPortal) return;
        UfPortal.ItemsSource = "AC AL AP AM BA CE DF ES GO MA MT MS MG PA PB PR PE PI RJ RN RS RO RR SC SP SE TO".Split(' ');
        iniciandoPortal = true;
        try
        {
            EstadoPortal.Text = "Abrindo o Leis.org…";
            var ambiente = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, Path.Combine(PastaDados.Raiz, "PortalLeis"), null);
            await Portal.EnsureCoreWebView2Async(ambiente);
            var core = Portal.CoreWebView2;
            core.HistoryChanged += (_, _) => AtualizarNavegacao();
            core.NavigationStarting += (_, _) => { PararPortal.IsEnabled = true; SalvarPortal.IsEnabled = false; };
            core.NavigationCompleted += (_, e) =>
            {
                AtualizarNavegacao();
                PararPortal.IsEnabled = false;
                SalvarPortal.IsEnabled = e.IsSuccess;
                EstadoPortal.Text = e.IsSuccess ? core.DocumentTitle : "A página não carregou. Use Atualizar para tentar novamente.";
            };
            core.SourceChanged += (_, _) => AtualizarNavegacao();
            core.NewWindowRequested += (_, e) =>
            {
                if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var destino) && destino.Scheme is "https" or "blob")
                {
                    e.Handled = true;
                    core.Navigate(e.Uri);
                }
            };
            core.DownloadStarting += AbrirPdfBaixado;
            portalPronto = true;
            InicioConsulta(this, new RoutedEventArgs());
        }
        catch (Exception) { EstadoPortal.Text = "Não foi possível abrir o navegador. Use Atualizar para tentar novamente."; }
        finally { iniciandoPortal = false; }
    }

    private void MudarEsfera(object sender, SelectionChangedEventArgs e)
    {
        if (CidadePortal == null || UfPortal == null) return;
        CidadePortal.IsEnabled = EsferaPortal.SelectedIndex == 0;
        UfPortal.IsEnabled = EsferaPortal.SelectedIndex != 2;
    }

    private void AbrirAcervo(object sender, RoutedEventArgs e)
    {
        if (!portalPronto) return;
        var uf = (UfPortal.SelectedItem as string)?.ToLowerInvariant();
        var cidade = System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(CidadePortal.Text.Normalize(System.Text.NormalizationForm.FormD), @"\p{M}", "")
                .ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        string? caminho = EsferaPortal.SelectedIndex switch
        {
            2 => "federais",
            1 when uf != null => "estaduais/" + uf,
            0 when uf != null && cidade.Length > 0 => $"municipais/{uf}/{cidade}",
            _ => null
        };
        if (caminho == null) { EstadoPortal.Text = "Preencha os campos do acervo selecionado."; return; }
        Portal.Source = new Uri("https://leis.org/" + caminho);
    }

    private void AtualizarNavegacao()
    {
        var core = Portal.CoreWebView2;
        VoltarPortal.IsEnabled = core.CanGoBack;
        AvancarPortal.IsEnabled = core.CanGoForward;
        acervoPortal = null;
        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var uri) && uri.Host == "leis.org")
        {
            var p = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            int n = p.FirstOrDefault() switch
            {
                "municipais" when p.Length >= 3 => 3,
                "estaduais" or "institucionais" when p.Length >= 2 => 2,
                "federais" => 1,
                _ => 0
            };
            if (n > 0) acervoPortal = new Uri("https://leis.org/" + string.Join("/", p.Take(n)) + "/");
        }
        TermoPortal.IsEnabled = acervoPortal != null;
    }

    private void PesquisarPortal(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (acervoPortal != null && !string.IsNullOrWhiteSpace(args.QueryText))
            Portal.Source = new Uri(acervoPortal, "busca?termo=" + Uri.EscapeDataString(args.QueryText.Trim()));
    }

    private void AbrirPdfBaixado(CoreWebView2 sender, CoreWebView2DownloadStartingEventArgs e)
    {
        var download = e.DownloadOperation;
        if (download.MimeType != "application/pdf" && !Path.GetExtension(e.ResultFilePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase)) return;
        var pasta = Path.Combine(PastaDados.Raiz, "PortalLeis", "PDFs");
        Directory.CreateDirectory(pasta);
        string arquivo = Path.Combine(pasta, Guid.NewGuid().ToString("N") + ".pdf");
        e.ResultFilePath = arquivo;
        e.Handled = true;
        EstadoPortal.Text = "Carregando o PDF da norma…";
        download.StateChanged += (_, _) =>
        {
            if (download.State == CoreWebView2DownloadState.Interrupted) EstadoPortal.Text = "O carregamento do PDF foi interrompido.";
            if (download.State != CoreWebView2DownloadState.Completed) return;
            try
            {
                using var entrada = File.OpenRead(arquivo);
                var marca = new byte[5];
                if (entrada.Read(marca) != 5 || !marca.AsSpan().SequenceEqual("%PDF-"u8)) { EstadoPortal.Text = "O portal não retornou um PDF válido."; return; }
                var local = new Uri(arquivo);
                pdfsDaConsulta[local.AbsoluteUri] = arquivo;
                Portal.Source = local;
            }
            catch (IOException) { EstadoPortal.Text = "Não foi possível ler o PDF recebido."; }
        };
    }

    private void VoltarNaConsulta(object s, RoutedEventArgs e) { if (Portal.CanGoBack) Portal.GoBack(); }
    private void AvancarNaConsulta(object s, RoutedEventArgs e) { if (Portal.CanGoForward) Portal.GoForward(); }
    private void PararConsulta(object s, RoutedEventArgs e) => Portal.CoreWebView2?.Stop();
    private async void AtualizarConsulta(object s, RoutedEventArgs e) { if (portalPronto) Portal.Reload(); else await AbrirConsultaAsync(); }
    private void InicioConsulta(object s, RoutedEventArgs e) { if (portalPronto) Portal.Source = new Uri("https://leis.org/"); }
    private void FecharConsulta(object s, RoutedEventArgs e) => Consulta.Visibility = Visibility.Collapsed;

    private async void SalvarConsulta(object s, RoutedEventArgs e)
    {
        if (!portalPronto) return;
        var core = Portal.CoreWebView2;
        var origem = core.Source;
        try
        {
            var destino = await Perguntas.Atual.SalvarArquivoAsync("Legislação.pdf", ".pdf");
            if (destino == null || origem != core.Source) return;
            if (pdfsDaConsulta.TryGetValue(origem, out var arquivo)) File.Copy(arquivo, destino, true);
            else if (!await core.PrintToPdfAsync(destino, null)) { EstadoPortal.Text = "Não foi possível salvar esta página como PDF."; return; }
            EstadoPortal.Text = "PDF salvo.";
        }
        catch (Exception) { EstadoPortal.Text = "Não foi possível salvar o PDF no local escolhido."; }
    }
}
