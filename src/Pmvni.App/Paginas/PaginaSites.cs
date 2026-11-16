using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Pmvni.App;

/// <summary>Os sistemas da Prefeitura na barra lateral (2026-11-16): um navegador por site (WebView2), criado na primeira visita e mantido
/// enquanto o app está aberto (a sessão e a página continuam ao voltar ao módulo). O perfil é próprio dos sites (separado do relatório) e
/// fica em <c>%LOCALAPPDATA%\EditorRelatoriosPMVNI\Sites</c>, então o login é lembrado entre aberturas. Uma barra fina de progresso
/// aparece enquanto a página carrega; uma falha de rede mostra o aviso com o endereço.</summary>
public sealed partial class PaginaSites : Grid
{
    readonly Dictionary<Modulo, WebView2> navegadores = new();
    readonly ProgressBar progresso = new() { IsIndeterminate = true, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top };
    readonly InfoBar aviso = new() { IsClosable = true, IsOpen = false, Severity = InfoBarSeverity.Warning, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) };
    static Task<CoreWebView2Environment>? ambiente;
    Modulo? atual;

    /// <summary>Muda quando a navegação do site à vista muda (voltar/avançar disponíveis).</summary>
    public event Action? EstadoMudou;

    public PaginaSites()
    {
        Canvas.SetZIndex(progresso, 10);
        Canvas.SetZIndex(aviso, 11);
        Children.Add(progresso);
        Children.Add(aviso);
    }

    static Task<CoreWebView2Environment> Ambiente() => ambiente ??= CoreWebView2Environment.CreateWithOptionsAsync(
        string.Empty, Path.Combine(Pmvni.Composicao.Web.PastaDados.Raiz, "Sites"), new CoreWebView2EnvironmentOptions()).AsTask();

    public CoreWebView2? Atual => atual is { } m && navegadores.TryGetValue(m, out var w) ? w.CoreWebView2 : null;

    /// <summary>Mostra o site do módulo (cria o navegador na primeira vez).</summary>
    public async Task MostrarAsync(Modulo m)
    {
        atual = m;
        foreach (var (mod, w) in navegadores) w.Visibility = mod == m ? Visibility.Visible : Visibility.Collapsed;
        aviso.IsOpen = false;
        if (navegadores.ContainsKey(m)) { EstadoMudou?.Invoke(); return; }
        var web = new WebView2();
        navegadores[m] = web;
        Children.Insert(0, web);
        try
        {
            await web.EnsureCoreWebView2Async(await Ambiente());
            var core = web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.NavigationStarting += (_, _) => { if (atual == m) progresso.Visibility = Visibility.Visible; };
            core.NavigationCompleted += (_, e) =>
            {
                if (atual != m) return;
                progresso.Visibility = Visibility.Collapsed;
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    aviso.Title = "A página não abriu";
                    aviso.Message = $"{Modulos.Endereco(m)} não respondeu ({e.WebErrorStatus}). Alguns sistemas só abrem na rede da Prefeitura.";
                    aviso.IsOpen = true;
                }
                else aviso.IsOpen = false;
                EstadoMudou?.Invoke();
            };
            core.HistoryChanged += (_, _) => { if (atual == m) EstadoMudou?.Invoke(); };
            // Janelas novas (target=_blank) abrem no mesmo navegador, como uma aba só.
            core.NewWindowRequested += (_, e) => { e.Handled = true; core.Navigate(e.Uri); };
            core.Navigate(Modulos.Endereco(m));
        }
        catch (Exception e)
        {
            RegistroErros.Gravar("sites", e);
            aviso.Title = "O navegador não abriu";
            aviso.Message = e.Message;
            aviso.IsOpen = true;
        }
        EstadoMudou?.Invoke();
    }

    public void Voltar() { if (Atual is { CanGoBack: true } c) c.GoBack(); }
    public void Avancar() { if (Atual is { CanGoForward: true } c) c.GoForward(); }
    public void Atualizar() => Atual?.Reload();
    public void Inicio() { if (atual is { } m) Atual?.Navigate(Modulos.Endereco(m)); }
    public string? EnderecoAtual => Atual?.Source;
}
