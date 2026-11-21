using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Pmvni.App;

/// <summary>Os sistemas da Prefeitura na barra lateral (2026-11-16): um navegador por site (WebView2), criado na primeira visita e mantido
/// enquanto o app está aberto. O perfil é próprio dos sites (separado do relatório), em <c>&lt;dados&gt;\Sites</c>.
/// 2026-11-17 — login persistente até o usuário sair (<see cref="CofreSites"/>): os cookies de cada site são guardados a cada página e devolvidos
/// antes da primeira página (os de sessão viram duradouros); a cada 5 minutos o app renova a sessão dos sites abertos; se mesmo assim o servidor
/// pedir login e houver usuário e senha guardados, o app entra sozinho. A criação é idempotente (uma tarefa por site; falha libera nova tentativa)
/// e <see cref="ObterAsync"/> dá o navegador de um site sem trocar o que está à vista (o BCI do GPI usa).</summary>
public sealed partial class PaginaSites : Grid
{
    static readonly Modulo[] Sites = [Modulo.Essencial, Modulo.Gpi, Modulo.Alfresco];
    readonly Dictionary<Modulo, WebView2> navegadores = new();
    readonly Dictionary<Modulo, Task<CoreWebView2>> criando = new();
    readonly Dictionary<Modulo, DateTime> ultimoLoginAutomatico = new();
    readonly Border progresso = Trabalhando.Quadro("Carregando a página…");
    readonly TextBlock? textoProgresso;
    readonly InfoBar aviso = new() { IsClosable = true, IsOpen = false, Severity = InfoBarSeverity.Warning, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(12) };
    /// <summary>Estado do BCI (o fluxo roda neste navegador, à vista do usuário).</summary>
    public InfoBar AvisoBci { get; } = new() { IsClosable = true, IsOpen = false, Severity = InfoBarSeverity.Informational, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(12) };
    static Task<CoreWebView2Environment>? ambiente;
    readonly DispatcherTimer renovar = new() { Interval = TimeSpan.FromMinutes(5) };
    // 2026-11-17 (correção): os sites de página única (Essencial, GPI) entram sem nova navegação; os cookies são guardados também a cada
    // minuto e logo depois de cada mudança de endereço, e de todos os domínios por onde o site passou (o login pode estar em outro).
    readonly DispatcherTimer guardar = new() { Interval = TimeSpan.FromMinutes(1) };
    readonly Dictionary<Modulo, HashSet<string>> hostsVistos = new();
    Modulo? atual;
    bool cookiesRestaurados;
    readonly HashSet<Modulo> carregando = [];

    /// <summary>O quadro "Carregando a página…" só aparece se o site À VISTA está carregando.</summary>
    void MostrarProgresso()
    {
        var carregandoSite = atual is { } a && carregando.Contains(a);
        var estavaVisivel = progresso.Visibility == Visibility.Visible;
        progresso.Visibility = carregandoSite ? Visibility.Visible : Visibility.Collapsed;
        if (carregandoSite && !estavaVisivel && textoProgresso is not null
            && AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
        {
            FrameworkElementAutomationPeer.CreatePeerForElement(textoProgresso)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    /// <summary>Muda quando a navegação do site à vista muda (voltar/avançar disponíveis).</summary>
    public event Action? EstadoMudou;

    /// <summary>Uma janela nova pedida pelo site (target=_blank, window.open). Quem trata devolve true; senão (1.0.0) ela abre numa janela
    /// auxiliar de verdade (<see cref="JanelaPopupSite"/>), com window.opener e a sessão do site, e a página de origem fica como estava.</summary>
    public Func<Modulo, CoreWebView2NewWindowRequestedEventArgs, bool>? InterceptarJanela { get; set; }

    public PaginaSites()
    {
        progresso.VerticalAlignment = VerticalAlignment.Top;
        progresso.HorizontalAlignment = HorizontalAlignment.Center;
        progresso.Margin = new Thickness(12);
        if (progresso.Child is StackPanel painel)
        {
            textoProgresso = painel.Children.OfType<TextBlock>().FirstOrDefault();
            if (textoProgresso is not null)
            {
                textoProgresso.Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"];
                textoProgresso.TextAlignment = TextAlignment.Center;
                AutomationProperties.SetLiveSetting(textoProgresso, AutomationLiveSetting.Polite);
            }
            foreach (var anel in painel.Children.OfType<ProgressRing>()) Trabalhando.SetAutomatico(anel, false);
        }
        Canvas.SetZIndex(progresso, 10);
        Canvas.SetZIndex(aviso, 11);
        Canvas.SetZIndex(AvisoBci, 12);
        Children.Add(progresso);
        Children.Add(aviso);
        Children.Add(AvisoBci);
        renovar.Tick += async (_, _) => await RenovarSessoesAsync();
        guardar.Tick += async (_, _) =>
        {
            if (atual is { } m && Atual is { } c && !CofreSites.Saiu(Raiz, m)) await GuardarCookiesAsync(m, c);
        };
    }

    bool emFoco;

    internal void AtivarPeriodicos(bool ativo)
    {
        emFoco = ativo;
        AjustarMemoria();
        if (ativo) { renovar.Start(); guardar.Start(); }
        else { renovar.Stop(); guardar.Stop(); }
    }

    void AjustarMemoria()
    {
        foreach (var (modulo, web) in navegadores)
        {
            if (web.CoreWebView2 is not { } core) continue;
            try
            {
                core.MemoryUsageTargetLevel = emFoco && modulo == atual
                    ? CoreWebView2MemoryUsageTargetLevel.Normal : CoreWebView2MemoryUsageTargetLevel.Low;
            }
            catch (NotImplementedException) { } // Runtime anterior a essa opção.
        }
    }

    internal void FecharNavegadores()
    {
        AtivarPeriodicos(false);
        foreach (var web in navegadores.Values.ToArray())
            try { web.Close(); } catch (Exception e) { RegistroErros.Gravar("sites.fechar", e); }
        criando.Clear();
        navegadores.Clear();
    }

    HashSet<string> Hosts(Modulo m)
    {
        if (!hostsVistos.TryGetValue(m, out var s)) hostsVistos[m] = s = new(StringComparer.OrdinalIgnoreCase) { Host(m) };
        return s;
    }

    void Visto(Modulo m, CoreWebView2 core)
    {
        if (Uri.TryCreate(core.Source, UriKind.Absolute, out var u) && u.Scheme.StartsWith("http", StringComparison.Ordinal)) Hosts(m).Add(u.Host);
    }

    /// <summary>Depois de cada página ou mudança de endereço: tenta entrar (o formulário dos sites de página única aparece depois do
    /// carregamento) e guarda os cookies.</summary>
    async Task AposMudancaAsync(Modulo m, CoreWebView2 core)
    {
        Visto(m, core);
        foreach (var espera in new[] { 0, 1500, 4000 })
        {
            if (espera > 0) await Task.Delay(espera);
            if (await EntrarSozinhoAsync(m, core)) return;
        }
        await GuardarCookiesAsync(m, core);
    }

    static string Raiz => Pmvni.Composicao.Web.PastaDados.Raiz;

    internal static Task<CoreWebView2Environment> Ambiente()
    {
        if (ambiente is { IsFaulted: true } or { IsCanceled: true }) ambiente = null;   // falhou: a próxima visita tenta de novo
        return ambiente ??= CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, Path.Combine(Raiz, "Sites"),
            new CoreWebView2EnvironmentOptions { AreBrowserExtensionsEnabled = true }).AsTask();
    }

    /// <summary>Extensões do Edge/Chrome que valem nos sistemas (escolha do usuário, 2026-11-16): o iCloud Senhas (id do Edge e do Chrome).</summary>
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
        AjustarMemoria();
        foreach (var (mod, w) in navegadores) w.Visibility = mod == m ? Visibility.Visible : Visibility.Collapsed;
        MostrarProgresso();
        aviso.IsOpen = false;
        try { await ObterAsync(m); }
        catch (Exception e)
        {
            RegistroErros.Gravar("sites", e);
            aviso.Title = "O navegador não abriu";
            aviso.Message = e.Message;
            aviso.IsOpen = true;
        }
        if (atual == m && navegadores.TryGetValue(m, out var web)) web.Visibility = Visibility.Visible;
        EstadoMudou?.Invoke();
    }

    /// <summary>O navegador pronto do site (cria na primeira vez; uma criação em curso é reaproveitada; uma que falhou é refeita).</summary>
    public Task<CoreWebView2> ObterAsync(Modulo m)
    {
        if (criando.TryGetValue(m, out var t) && !t.IsFaulted && !t.IsCanceled) return t;
        return criando[m] = CriarAsync(m);
    }

    async Task<CoreWebView2> CriarAsync(Modulo m)
    {
        if (navegadores.Remove(m, out var velho)) { Children.Remove(velho); try { velho.Close(); } catch (Exception) { } }
        var web = new WebView2 { AllowDrop = true, Visibility = atual == m ? Visibility.Visible : Visibility.Collapsed };
        AutomationProperties.SetName(web, Modulos.Rotulo(m));
        navegadores[m] = web;
        Children.Insert(0, web);
        try
        {
            await web.EnsureCoreWebView2Async(await Ambiente());
            var core = web.CoreWebView2;
            AjustarMemoria();
            await CarregarExtensoesAsync(core.Profile);
            await PrepararPerfilAsync(core);
            core.Settings.AreDevToolsEnabled = false;
            // 2026-11-17 (correção, usuária Izabela): o preenchimento do próprio WebView2 trazia um login antigo ("izabelat") por cima do
            // login guardado em Ajustes. O único login que o app usa é o de Ajustes (CofreSites); sem ele, o campo fica vazio.
            core.Settings.IsPasswordAutosaveEnabled = ConfigSites.PreenchimentoDoNavegador;
            core.Settings.IsGeneralAutofillEnabled = ConfigSites.PreenchimentoDoNavegador;
            await LimparPreenchimentoAntigoAsync(core.Profile);
            // 1.0.0: o quadro "Carregando" acompanha o site à vista; antes, uma página que terminava de carregar com outro site à vista
            // deixava o quadro (e o brilho dele) aceso para sempre.
            core.NavigationStarting += (_, _) => { carregando.Add(m); MostrarProgresso(); };
            core.NavigationCompleted += async (_, e) =>
            {
                carregando.Remove(m);
                MostrarProgresso();
                if (atual == m)
                {
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
                }
                if (e.IsSuccess) await AposMudancaAsync(m, core);
            };
            core.HistoryChanged += (_, _) => { if (atual == m) EstadoMudou?.Invoke(); };
            core.SourceChanged += async (_, _) =>
            {
                try { await Task.Delay(2500); await AposMudancaAsync(m, core); }
                catch (Exception e) { RegistroErros.Gravar("sites.endereco", e); }
            };
            // 1.0.0: janela nova pedida pelo site = janela auxiliar de verdade (JanelaPopupSite), com window.opener e sessão preservados.
            // Antes o pedido navegava esta própria página para o endereço do popup, e o visualizador de documentos do Essencial abria cinza.
            core.NewWindowRequested += (s, e) =>
            {
                if (InterceptarJanela?.Invoke(m, e) == true) return;
                JanelaPopupSite.Atender(s, e, Modulos.Rotulo(m), ActualTheme, null);
            };
            core.Navigate(Modulos.Endereco(m));
            return core;
        }
        catch
        {
            carregando.Remove(m);
            MostrarProgresso();
            navegadores.Remove(m);
            Children.Remove(web);
            try { web.Close(); } catch (Exception) { }
            throw;
        }
    }

    /// <summary>Uma vez por abertura do app: devolve ao perfil dos sites os cookies guardados (o GPI invisível do boletim também chama).</summary>
    public Task PrepararPerfilAsync(CoreWebView2 core)
    {
        if (!cookiesRestaurados) { cookiesRestaurados = true; RestaurarCookies(core.CookieManager); }
        return Task.CompletedTask;
    }

    static Task? preenchimentoLimpo;

    /// <summary>Uma vez por abertura: apaga senhas e dados de formulário que o WebView2 guardou no perfil dos sites (não toca nos cookies).</summary>
    static Task LimparPreenchimentoAntigoAsync(CoreWebView2Profile perfil) => preenchimentoLimpo ??= LimparAsync(perfil);

    static async Task LimparAsync(CoreWebView2Profile perfil)
    {
        if (!ConfigSites.ApagarPreenchimentoGuardado) return;
        try { await perfil.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.PasswordAutosave | CoreWebView2BrowsingDataKinds.GeneralAutofill); }
        catch (Exception e) { RegistroErros.Gravar("sites.preenchimento", e); }
    }

    static string Host(Modulo m) => new Uri(Modulos.Endereco(m)).Host;

    /// <summary>Devolve ao perfil os cookies guardados de cada site (salvo os de quem saiu), como duradouros (10 anos).</summary>
    void RestaurarCookies(CoreWebView2CookieManager gestor)
    {
        foreach (var m in Sites)
        {
            if (CofreSites.Saiu(Raiz, m)) continue;
            foreach (var c in CofreSites.LerCookies(Raiz, m))
                try
                {
                    var k = gestor.CreateCookie(c.Nome, c.Valor, c.Dominio, c.Caminho);
                    k.IsSecure = c.Seguro;
                    k.IsHttpOnly = c.SoHttp;
                    k.SameSite = (CoreWebView2CookieSameSiteKind)c.MesmoSite;
                    k.Expires = DateTimeOffset.UtcNow.AddYears(10).ToUnixTimeSeconds();
                    gestor.AddOrUpdateCookie(k);
                }
                catch (Exception e) { RegistroErros.Gravar("sites.cookie", e); }
        }
    }

    /// <summary>Guarda todos os cookies do site (só depois que o usuário entrou: com campo de senha à vista, nada muda).</summary>
    async Task GuardarCookiesAsync(Modulo m, CoreWebView2 core)
    {
        try
        {
            if (await TemSenhaAVistaAsync(core)) return;
            Visto(m, core);
            var hosts = Hosts(m).ToList();
            var lista = (await core.CookieManager.GetCookiesAsync(null))
                .Where(c => hosts.Any(h => CofreSites.DoSite(c.Domain, h)))
                .Select(c => new CookieSite(c.Name, c.Value, c.Domain, c.Path, c.IsSecure, c.IsHttpOnly, (int)c.SameSite)).ToList();
            if (lista.Count == 0) return;
            CofreSites.Voltou(Raiz, m);
            CofreSites.GuardarCookies(Raiz, m, lista);
        }
        catch (Exception e) { RegistroErros.Gravar("sites.guardar", e); }
    }

    static async Task<bool> TemSenhaAVistaAsync(CoreWebView2 core)
    {
        try
        {
            var r = await core.ExecuteScriptAsync(ScriptsSites.Chamar(ScriptsSites.Login, null));
            using var d = JsonDocument.Parse(r);
            return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.GetProperty("senha").GetBoolean();
        }
        catch (Exception) { return false; }
    }

    /// <summary>Com campo de senha à vista e login guardado: entra sozinho (no máximo uma vez por minuto, para não insistir num login recusado).</summary>
    internal async Task<bool> EntrarSozinhoAsync(Modulo m, CoreWebView2 core)
    {
        try
        {
            if (CofreSites.Saiu(Raiz, m) || CofreSites.LerLogin(Raiz, m) is not { } cred) return false;
            if (ultimoLoginAutomatico.TryGetValue(m, out var q) && DateTime.UtcNow - q < TimeSpan.FromMinutes(1)) return false;
            if (!await TemSenhaAVistaAsync(core)) return false;
            ultimoLoginAutomatico[m] = DateTime.UtcNow;
            var r = await core.ExecuteScriptAsync(ScriptsSites.Chamar(ScriptsSites.Login, new { usuario = cred.Usuario, senha = cred.Senha }));
            using var d = JsonDocument.Parse(r);
            return d.RootElement.ValueKind == JsonValueKind.Object && d.RootElement.GetProperty("enviado").GetBoolean();
        }
        catch (Exception e) { RegistroErros.Gravar("sites.login", e); return false; }
    }

    /// <summary>Logo depois de "Guardar login": se o site já está aberto pedindo senha, entra.</summary>
    public async Task EntrarAgoraAsync(Modulo m)
    {
        ultimoLoginAutomatico.Remove(m);
        if (navegadores.TryGetValue(m, out var w) && w.CoreWebView2 is { } core) await AposMudancaAsync(m, core);
    }

    /// <summary>A cada 5 minutos: renova a sessão dos sites abertos e guarda os cookies (que o servidor pode ter trocado).</summary>
    async Task RenovarSessoesAsync()
    {
        foreach (var (m, w) in navegadores.ToList())
        {
            if (m != atual || w.CoreWebView2 is not { } core || CofreSites.Saiu(Raiz, m)) continue;
            try
            {
                await core.ExecuteScriptAsync(ScriptsSites.Chamar(ScriptsSites.ManterViva, null));
                if (!await EntrarSozinhoAsync(m, core)) await GuardarCookiesAsync(m, core);
            }
            catch (Exception e) { RegistroErros.Gravar("sites.renovar", e); }
        }
    }

    /// <summary>Ao fechar o app: guarda os cookies uma última vez.</summary>
    public async Task GuardarTudoAsync()
    {
        renovar.Stop();
        guardar.Stop();
        JanelaPopupSite.FecharTodas();
        foreach (var (m, w) in navegadores.ToList())
            if (w.CoreWebView2 is { } core && !CofreSites.Saiu(Raiz, m)) await GuardarCookiesAsync(m, core);
    }

    /// <summary>"Sair" (Ajustes): apaga os cookies do site no perfil e no cofre, o login guardado, e volta à página inicial.</summary>
    public async Task SairAsync(Modulo m)
    {
        CofreSites.Sair(Raiz, m);
        try
        {
            CoreWebView2? core = navegadores.TryGetValue(m, out var w) ? w.CoreWebView2 : null;
            if (core is null && navegadores.Count > 0) core = navegadores.Values.First().CoreWebView2;
            if (core is null) return;
            var hosts = Hosts(m).ToList();
            foreach (var c in await core.CookieManager.GetCookiesAsync(null))
                if (hosts.Any(h => CofreSites.DoSite(c.Domain, h))) core.CookieManager.DeleteCookie(c);
            if (navegadores.TryGetValue(m, out var web) && web.CoreWebView2 is { } dele) dele.Navigate(Modulos.Endereco(m));
        }
        catch (Exception e) { RegistroErros.Gravar("sites.sair", e); }
    }

    /// <summary>Situação para Ajustes: "Conectado (lembrado)", "Saiu" ou "Ainda não entrou".</summary>
    public static string Situacao(Modulo m) =>
        CofreSites.Saiu(Raiz, m) ? "Saiu: entre de novo pelo site" :
        CofreSites.LerCookies(Raiz, m).Count > 0 ? "Login lembrado neste computador" + (CofreSites.TemLogin(Raiz, m) ? " · entra sozinho se o site pedir" : "") :
        CofreSites.TemLogin(Raiz, m) ? "Entra sozinho com o login guardado" : "Ainda não entrou por aqui";

    public void Voltar() { if (Atual is { CanGoBack: true } c) c.GoBack(); }
    public void Avancar() { if (Atual is { CanGoForward: true } c) c.GoForward(); }
    public void Atualizar() => Atual?.Reload();
    public void Inicio() { if (atual is { } m) Atual?.Navigate(Modulos.Endereco(m)); }
    public string? EnderecoAtual => Atual?.Source;
}
