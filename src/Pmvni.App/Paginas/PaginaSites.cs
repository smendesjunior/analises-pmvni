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
        string.Empty, Path.Combine(Pmvni.Composicao.Web.PastaDados.Raiz, "Sites"),
        new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true }).AsTask();

    /// <summary>Extensões do Edge/Chrome que valem nos sistemas (escolha do usuário, 2026-11-16): o iCloud Senhas (id do Edge e do Chrome).
    /// O app carrega a versão instalada no navegador, do jeito que ela está; extensão que o WebView2 não aceita só fica de fora (registro).</summary>
    static readonly string[] Extensoes = ["mfbcdcnpokpoajjciilocoachedjkima", "pejdijmoenmkgeppbflobdenhhabjlaj"];
    static Task? extensoesCarregadas;

    static Task CarregarExtensoesAsync(CoreWebView2Profile perfil) => extensoesCarregadas ??= CarregarAsync(perfil);

    static async Task CarregarAsync(CoreWebView2Profile perfil)
    {
        try
        {
            var ja = (await perfil.GetBrowserExtensionsAsync()).Select(e => e.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (var navegador in new[] { Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Extensions"),
                                              Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Extensions") })
                foreach (var id in Extensoes)
                {
                    var pasta = Path.Combine(navegador, id);
                    if (!Directory.Exists(pasta)) continue;
                    var versao = Directory.GetDirectories(pasta).OrderByDescending(d => Version.TryParse(Path.GetFileName(d).Split('_')[0], out var v) ? v : new Version()).FirstOrDefault();
                    if (versao is null || !File.Exists(Path.Combine(versao, "manifest.json"))) continue;
                    try
                    {
                        // A cópia do navegador é carregada como pasta: se o id já está no perfil (de uma abertura anterior), sai e entra de novo
                        // para valer a versão atual.
                        if (ja.Contains(id)) foreach (var e in await perfil.GetBrowserExtensionsAsync()) if (e.Id == id) await e.RemoveAsync();
                        await perfil.AddBrowserExtensionAsync(versao);
                    }
                    catch (Exception e) { RegistroErros.Gravar("sites.extensao." + id, e); }
                }
        }
        catch (Exception e) { RegistroErros.Gravar("sites.extensoes", e); }
    }

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
            await CarregarExtensoesAsync(core.Profile);
            core.Settings.AreDevToolsEnabled = false;
            core.NavigationStarting += (_, _) => { if (atual == m) progresso.Visibility = Visibility.Visible; };
            core.NavigationCompleted += (_, e) =>
            {
                if (atual != m) return;
                progresso.Visibility = Visibility.Collapsed;
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                {
                    aviso.Title = "A página não abriu";
                    aviso.Message = e.WebErrorStatus == CoreWebView2WebErrorStatus.HostNameNotResolved
                        ? $"Este computador não encontrou o endereço {new Uri(Modulos.Endereco(m)).Host}: ele só existe na rede da Prefeitura (ou pela VPN). Conecte-se a ela e use Atualizar."
                        : $"{Modulos.Endereco(m)} não respondeu ({e.WebErrorStatus}). Confira a conexão e use Atualizar.";
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
