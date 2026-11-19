using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

/// <summary>Janela de impressão (2026-11-17): mostra um arquivo (PDF, imagem ou texto) no visualizador do WebView2, no mesmo ambiente
/// InPrivate do app, e abre sobre ele a impressão do Edge (<see cref="CoreWebView2PrintDialogKind.Browser"/>): a lista das impressoras
/// do Windows, "Microsoft Print to PDF" e "Salvar como PDF", com a prévia. A impressão do Edge fica dentro da janela e não trava o app
/// (nada de diálogo do sistema "aguardando a impressora"). Fechada a janela, a pasta temporária (o PDF gerado só para imprimir) é apagada.</summary>
internal sealed class JanelaImpressao : Window
{
    readonly Grid raiz = new();
    readonly WebView2 web = new() { DefaultBackgroundColor = Microsoft.UI.Colors.White };
    readonly Button botaoImprimir = new() { Content = "Imprimir…", IsEnabled = false };
    readonly TextBlock estado = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = .8 };
    readonly string arquivo;
    readonly string? pastaTemporaria;
    readonly Uri endereco;
    bool impressaoAberta, fechada;

    JanelaImpressao(string arquivo, string titulo, ElementTheme tema, string? pastaTemporaria)
    {
        this.arquivo = arquivo;
        this.pastaTemporaria = pastaTemporaria;
        endereco = new Uri(arquivo);
        MainWindow.AplicarIconeDaJanela(this);
        Title = "Imprimir — " + titulo;
        Montar(titulo);
        Content = raiz;
        raiz.RequestedTheme = tema;
        Closed += (_, _) => { fechada = true; ApagarTemporario(); };
        try { AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 900)); } catch (Exception) { /* tamanho inicial é só conveniência */ }
    }

    /// <summary>Abre a janela com <paramref name="arquivo"/> e, quando ele termina de carregar, a impressão do Edge.
    /// <paramref name="pastaTemporaria"/>: apagada ao fechar (só para arquivos gerados para imprimir).</summary>
    public static JanelaImpressao Abrir(CoreWebView2Environment ambiente, string arquivo, string titulo, ElementTheme tema, string? pastaTemporaria = null)
    {
        var j = new JanelaImpressao(arquivo, titulo, tema, pastaTemporaria);
        j.Activate();
        _ = j.IniciarAsync(ambiente);
        return j;
    }

    void Montar(string titulo)
    {
        raiz.Style = (Style)Application.Current.Resources["PmvniFundoJanelaStyle"];
        raiz.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        raiz.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var barra = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 8 };
        barra.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        barra.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        barra.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        estado.Text = "Abrindo " + titulo + "…";
        barra.Children.Add(estado);
        botaoImprimir.Style = (Style)Application.Current.Resources["PmvniBotaoPrimarioStyle"];
        Dicas.Aplicar(botaoImprimir, "Imprimir (Ctrl+P)", "Abre a impressão com a lista de impressoras e a opção Salvar como PDF.");
        botaoImprimir.Click += (_, _) => MostrarImpressao();
        Grid.SetColumn(botaoImprimir, 1);
        barra.Children.Add(botaoImprimir);
        var fechar = new Button { Content = "Fechar" };
        fechar.Click += (_, _) => Close();
        Grid.SetColumn(fechar, 2);
        barra.Children.Add(fechar);
        raiz.Children.Add(barra);
        Grid.SetRow(web, 1);
        raiz.Children.Add(web);
        var ctrlP = new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.P, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        ctrlP.Invoked += (_, a) => { a.Handled = true; MostrarImpressao(); };
        raiz.KeyboardAccelerators.Add(ctrlP);
        raiz.KeyboardAcceleratorPlacementMode = Microsoft.UI.Xaml.Input.KeyboardAcceleratorPlacementMode.Hidden;
    }

    async Task IniciarAsync(CoreWebView2Environment ambiente)
    {
        try
        {
            await web.EnsureCoreWebView2Async(ambiente, FabricaWebView2.OpcoesController(ambiente));
            if (fechada) return;
            var core = web.CoreWebView2;
            var s = core.Settings;
            s.AreHostObjectsAllowed = false;
            s.IsWebMessageEnabled = false;
            s.IsStatusBarEnabled = false;
            s.IsGeneralAutofillEnabled = false;
            s.IsPasswordAutosaveEnabled = false;
            // Só o arquivo pedido: nenhum link do PDF navega nem abre janela (o navegador do Windows fica com isso).
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.NavigationStarting += (_, e) =>
            {
                if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) || u != endereco) e.Cancel = true;
            };
            core.NavigationCompleted += async (_, e) =>
            {
                if (fechada) return;
                if (!e.IsSuccess)
                {
                    estado.Text = "Não foi possível abrir o arquivo para imprimir (" + e.WebErrorStatus + ").";
                    return;
                }
                estado.Text = Path.GetFileName(arquivo);
                botaoImprimir.IsEnabled = true;
                // O visualizador de PDF termina de montar depois da navegação: a impressão abre um instante depois, uma vez só.
                await Task.Delay(700);
                if (!fechada && !impressaoAberta) MostrarImpressao();
            };
            core.Navigate(endereco.AbsoluteUri);
        }
        catch (Exception e)
        {
            RegistroErros.Gravar("impressao", e);
            if (!fechada) estado.Text = "Não foi possível preparar a impressão: " + e.Message;
        }
    }

    /// <summary>A impressão do Edge (impressoras e Salvar como PDF), sobre o documento desta janela.</summary>
    void MostrarImpressao()
    {
        if (fechada || web.CoreWebView2 is not { } core || !botaoImprimir.IsEnabled) return;
        impressaoAberta = true;
        try { core.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); }
        catch (Exception e)
        {
            RegistroErros.Gravar("impressao", e);
            estado.Text = "Não foi possível abrir a impressão: " + e.Message;
        }
    }

    void ApagarTemporario()
    {
        try { web.Close(); } catch (Exception) { /* o WebView2 pode já ter caído: segue para apagar */ }
        if (pastaTemporaria is null) return;
        // O WebView2 solta o arquivo um pouco depois de fechar: tenta de novo em segundo plano; o que sobrar sai na próxima impressão.
        _ = Task.Run(async () =>
        {
            for (var i = 0; i < 10; i++)
            {
                try { if (Directory.Exists(pastaTemporaria)) Directory.Delete(pastaTemporaria, recursive: true); return; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { await Task.Delay(1000); }
            }
        });
    }
}
