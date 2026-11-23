using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

/// <summary>Ajustes › Sistema (2026-11-17), no padrão do WinStatusBar: Suporte (contato e informações para o relato),
/// Diagnóstico (relatório com versões, pastas e memória, com Atualizar e Copiar) e Sobre (ícone, nome, versão · compilação, descrição,
/// desenvolvedor, direitos, sistema, runtime e créditos). Ajuda › Sobre leva a esta página.</summary>
public sealed partial class MainWindow
{
    const string Desenvolvedor = "Sidney Junior";
    const string EmailContato = "s.mendesjunior@outlook.com";

    static string TextoSistema => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
    static string TextoRuntime => $".NET {Environment.Version} · {RuntimeInformation.ProcessArchitecture}";

    /// <summary>"Versão 1.0 · compilação 20261117120000 · 17/11/2026 09:00" (sem compilação: "compilação local").</summary>
    static string TextoVersaoCompleta()
    {
        var c = Atualizador.Compilacao;
        var quando = c is { Length: 14 } && DateTime.TryParseExact(c, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d) ? $" · {d.ToLocalTime():dd/MM/yyyy HH:mm}" : "";
        return $"Versão {Atualizador.VersaoExibida} · compilação {c ?? "local"}{quando}";
    }

    void IniciarSobre()
    {
        var p = PaginaDeAjustes;
        p.TextoSobreVersao.Text = TextoVersaoCompleta();
        p.TextoDesenvolvedor.Text = Desenvolvedor;
        p.TextoDireitos.Text = $"© {DateTime.Now.Year} {Desenvolvedor}. Todos os direitos reservados.";
        p.TextoSistema.Text = TextoSistema;
        p.TextoRuntime.Text = TextoRuntime;
        p.LinkContato.Content = EmailContato;
        Dicas.Aplicar(p.LinkContato, "Enviar e-mail ao desenvolvedor", "Abre o aplicativo de e-mail com o endereço do suporte.");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(p.TextoCopiado, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        if (Argumentos.Atual.Roteiro is null)
            p.LinkContato.NavigateUri = new Uri("mailto:" + EmailContato + "?subject=" + Uri.EscapeDataString("Análise de Projetos " + Atualizador.VersaoExibida));
        p.CategoriaMostrada += tag =>
        {
            if (tag == CategoriasAjustes.Sobre && PaginaAjustesVisivel) p.TextoVersoes.Text = RelatorioDiagnostico();
        };
        p.BotaoAtualizarDiagnostico.Click += (_, _) => p.TextoVersoes.Text = RelatorioDiagnostico();
        p.BotaoCopiarInformacoes.Click += (_, _) =>
        {
            AreaTransferencia.Copiar($"Análise de Projetos – PMVNI {TextoVersaoCompleta()}\nWindows: {TextoSistema}\nRuntime: {TextoRuntime}\nExecutável: {Environment.ProcessPath}\nDados: {PastaDados.Raiz}");
            p.TextoCopiado.Text = "Informações copiadas. Cole no e-mail ao relatar um problema.";
            p.TextoCopiado.Visibility = Visibility.Visible;
        };
        p.BotaoLicencaPdfJs.Click += async (_, _) => await TentarAsync(MostrarLicencaPdfJsAsync);
        if (ImagemDoApp() is { } img) p.ImagemApp.Source = img;
    }

    /// <summary>O relatório do Diagnóstico: versões (as de sempre, que o roteiro confere), executável, pastas, memória e as fontes de dados.</summary>
    string RelatorioDiagnostico()
    {
        var sb = new StringBuilder();
        var n = Environment.NewLine;
        sb.Append(TextoDasVersoes()).Append(n);
        sb.Append($"Compilação: {Atualizador.Compilacao ?? "local"} · {DateTime.Now:dd/MM/yyyy HH:mm:ss}").Append(n);
        sb.Append($"Executável: {Environment.ProcessPath}").Append(n);
        sb.Append($"Dados: {PastaDados.Raiz}").Append(n);
        try
        {
            using var proc = Process.GetCurrentProcess();
            sb.Append($"Processo: CPU total {proc.TotalProcessorTime.TotalSeconds:0.0}s · memória privada {proc.PrivateMemorySize64 / 1048576} MB · working set {proc.WorkingSet64 / 1048576} MB").Append(n);
        }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        sb.Append(n).Append("— Sistema").Append(n);
        sb.Append($"  Windows: {TextoSistema}").Append(n);
        sb.Append($"  Runtime: {TextoRuntime}").Append(n);
        sb.Append(n).Append("— Pastas e fontes de dados").Append(n);
        sb.Append($"  Processos: {ajustes.PastaProcessos ?? "(não escolhida)"}").Append(n);
        sb.Append($"  Kit PMVNI: {ajustes.PastaKit ?? "(padrão)"}").Append(n);
        sb.Append($"  Base QGIS: {ajustes.PastaQgis ?? "(não escolhida)"}{(baseImoveis is not null ? " · carregada" : "")}").Append(n);
        sb.Append($"  Planilha de acompanhamento: {ajustes.PlanilhaAcompanhamento ?? "(não escolhida)"}").Append(n);
        if (planilha is { } pl) sb.Append($"    aba {pl.Aba} · {pl.Linhas.Count} processos · {pl.Colunas.Count} colunas · lida às {pl.LidoEm:HH:mm:ss}").Append(n);
        sb.Append($"  LibreOffice: {ajustes.Soffice ?? "(automático)"}").Append(n);
        return sb.ToString().TrimEnd();
    }

    Task MostrarLicencaPdfJsAsync()
    {
        var licenca = Pmvni.Legislacao.RecursosLegislacao.Ler("pdfjs/LICENSE") is { } b ? Encoding.UTF8.GetString(b) : "Licença não encontrada.";
        var texto = new TextBlock
        {
            Text = licenca,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"],
        };
        MenuTexto.Aplicar(texto);
        return MostrarDialogoAsync("Licença do PDF.js", new ScrollViewer { MaxHeight = 420, Content = texto });
    }

    static Microsoft.UI.Xaml.Media.Imaging.BitmapImage? ImagemDoApp()
    {
        try
        {
            using var recurso = typeof(MainWindow).Assembly.GetManifestResourceStream("Pmvni.App.icone-app.png");
            if (recurso is null) return null;
            using var fluxo = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            using var escrita = System.IO.WindowsRuntimeStreamExtensions.AsStreamForWrite(fluxo);
            recurso.CopyTo(escrita);
            escrita.Flush();
            fluxo.Seek(0);
            var imagem = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
            imagem.SetSource(fluxo);
            return imagem;
        }
        catch (Exception e) when (e is IOException or System.Runtime.InteropServices.COMException) { return null; }
    }
}
