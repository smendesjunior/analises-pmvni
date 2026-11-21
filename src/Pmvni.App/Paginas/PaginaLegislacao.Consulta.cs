using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

public sealed partial class PaginaLegislacao
{
    private bool iniciandoPortal;
    private bool portalPronto;
    private readonly Dictionary<string, string> pdfsDaConsulta = new();

    internal async Task AbrirConsultaAsync()
    {
        Consulta.Visibility = Visibility.Visible;
        if (portalPronto || iniciandoPortal) return;
        iniciandoPortal = true;
        try
        {
            MostrarEstado("Abrindo o Leis.org…");
            var ambiente = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, Path.Combine(PastaDados.Raiz, "PortalLeis"), null);
            await Portal.EnsureCoreWebView2Async(ambiente);
            var core = Portal.CoreWebView2;
            core.HistoryChanged += (_, _) => AtualizarNavegacao();
            core.NavigationStarting += (_, _) =>
            {
                PararPortal.IsEnabled = true;
                SalvarPortal.IsEnabled = false;
                AtualizarDicasDoPortal();
            };
            core.NavigationCompleted += (_, e) =>
            {
                AtualizarNavegacao();
                PararPortal.IsEnabled = false;
                SalvarPortal.IsEnabled = e.IsSuccess;
                AtualizarDicasDoPortal();
                MostrarEstado(e.IsSuccess ? "" : "A página não carregou. Use Atualizar para tentar novamente.");
            };
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
            InicioPortal.IsEnabled = true;
            AtualizarDicasDoPortal();
            InicioConsulta(this, new RoutedEventArgs());
        }
        catch (Exception) { MostrarEstado("Não foi possível abrir o navegador. Use Atualizar para tentar novamente."); }
        finally { iniciandoPortal = false; }
    }

    private void MostrarEstado(string texto)
    {
        EstadoPortal.Text = texto;
        EstadoPortal.Visibility = texto.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void AtualizarNavegacao()
    {
        var core = Portal.CoreWebView2;
        VoltarPortal.IsEnabled = core.CanGoBack;
        AvancarPortal.IsEnabled = core.CanGoForward;
        AtualizarDicasDoPortal();
    }

    void AtualizarDicasDoPortal()
    {
        Dicas.Aplicar(VoltarPortal, "Voltar", "Volta para a página anterior do histórico.",
            motivo: VoltarPortal.IsEnabled ? null : "O histórico não tem página anterior.");
        Dicas.Aplicar(AvancarPortal, "Avançar", "Avança para a próxima página do histórico.",
            motivo: AvancarPortal.IsEnabled ? null : "O histórico não tem próxima página.");
        Dicas.Aplicar(PararPortal, "Parar", "Interrompe o carregamento da página.",
            motivo: PararPortal.IsEnabled ? null : "Nenhuma página está carregando.");
        Dicas.Aplicar(InicioPortal, "Página inicial", "Abre a página inicial do Leis.org.",
            motivo: InicioPortal.IsEnabled ? null : "Aguarde a abertura do navegador.");
        Dicas.Aplicar(SalvarPortal, "Salvar PDF…", "Escolhe onde salvar o PDF da página atual.",
            motivo: SalvarPortal.IsEnabled ? null : "A página precisa terminar de carregar.");
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
        MostrarEstado("Carregando o PDF da norma…");
        download.StateChanged += (_, _) =>
        {
            if (download.State == CoreWebView2DownloadState.Interrupted) MostrarEstado("O carregamento do PDF foi interrompido.");
            if (download.State != CoreWebView2DownloadState.Completed) return;
            try
            {
                using var entrada = File.OpenRead(arquivo);
                var marca = new byte[5];
                if (entrada.Read(marca) != 5 || !marca.AsSpan().SequenceEqual("%PDF-"u8)) { MostrarEstado("O portal não retornou um PDF válido."); return; }
                var local = new Uri(arquivo);
                pdfsDaConsulta[local.AbsoluteUri] = arquivo;
                Portal.Source = local;
            }
            catch (IOException) { MostrarEstado("Não foi possível ler o PDF recebido."); }
        };
    }

    private void VoltarNaConsulta(object s, RoutedEventArgs e) { if (Portal.CanGoBack) Portal.GoBack(); }
    private void AvancarNaConsulta(object s, RoutedEventArgs e) { if (Portal.CanGoForward) Portal.GoForward(); }
    internal void FecharPortal() => Portal.Close();

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
            else if (!await core.PrintToPdfAsync(destino, null)) { MostrarEstado("Não foi possível salvar esta página como PDF."); return; }
            MostrarEstado("PDF salvo.");
        }
        catch (Exception) { MostrarEstado("Não foi possível salvar o PDF no local escolhido."); }
    }
}
