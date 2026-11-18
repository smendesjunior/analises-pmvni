using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace Pmvni.App;

/// <summary>Janela auxiliar dos sites da Prefeitura (1.0.0, correção do visualizador de documentos do Essencial "Minha mesa").
/// Causa confirmada: o <c>NewWindowRequested</c> dos sites marcava o pedido como tratado e navegava o próprio navegador principal para o
/// endereço do popup. O visualizador do portal abre uma janela nova (window.open) que busca os dados do documento na janela que a abriu
/// (window.opener) e só então preenche o modelo; navegado no lugar da página do protocolo, não havia opener, o modelo ficava cru
/// ({{DATA.DOCUMENTO.NOME}}, {{paginas.total}}) sobre um fundo cinza, e a página do protocolo era perdida.
/// Agora cada pedido de janela vira uma janela WinUI 3 de verdade, com um WebView2 do MESMO ambiente e do MESMO perfil (sessão, cookies e
/// sessionStorage herdados), entregue ao WebView2 em <c>e.NewWindow</c> sob um deferral — o popup não é navegado à mão, então window.opener,
/// POST, about:blank + document.write, blob: e o visualizador de PDF (zoom, páginas, imprimir, baixar) funcionam como num navegador.
/// window.close() fecha a janela; fechar a janela descarta o WebView2; a página original continua intacta na janela principal.</summary>
internal sealed class JanelaPopupSite : Window
{
    static readonly List<JanelaPopupSite> abertas = [];
    readonly WebView2 web = new() { AllowDrop = true };
    readonly string rotuloSite;
    bool fechada;

    /// <summary>Quantas janelas auxiliares estão abertas (diagnóstico e autoteste).</summary>
    public static int Abertas => abertas.Count;

    JanelaPopupSite(string rotuloSite, ElementTheme tema, CoreWebView2WindowFeatures? f)
    {
        this.rotuloSite = rotuloSite;
        Title = rotuloSite;
        var raiz = new Grid { RequestedTheme = tema };
        raiz.Children.Add(web);
        Content = raiz;
        try
        {
            if (f is { HasSize: true } && f.Width >= 200 && f.Height >= 150)
                AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Min(f.Width, 2400), (int)Math.Min(f.Height, 1600)));
            else AppWindow.Resize(new Windows.Graphics.SizeInt32(1100, 900));
            if (f is { HasPosition: true }) AppWindow.Move(new Windows.Graphics.PointInt32((int)f.Left, (int)f.Top));
        }
        catch (Exception) { /* tamanho e posição são só conveniência */ }
        Closed += (_, _) => Descartar();
    }

    /// <summary>Atende um pedido de janela nova de um site: cria a janela, entrega o WebView2 dela ao pedido e conclui o deferral (sempre, mesmo
    /// em falha). Se a janela não puder ser criada, o pedido segue para o comportamento padrão do WebView2 (janela própria do navegador),
    /// que também preserva o opener — nunca mais navega a página do protocolo.</summary>
    public static async void Atender(CoreWebView2 origem, CoreWebView2NewWindowRequestedEventArgs e, string rotuloSite, ElementTheme tema,
        Action<CoreWebView2>? configurar)
    {
        Windows.Foundation.Deferral adiamento;
        try { adiamento = e.GetDeferral(); }
        catch (Exception ex) { RegistroErros.Gravar("sites.popup.deferral", ex); return; }
        JanelaPopupSite? j = null;
        try
        {
            j = new JanelaPopupSite(rotuloSite, tema, e.WindowFeatures);
            abertas.Add(j);
            j.Activate();
            // Mesmo ambiente e mesmo perfil (o padrão do ambiente dos sites): exigência do WebView2 para aceitar o NewWindow.
            await j.web.EnsureCoreWebView2Async(origem.Environment);
            var core = j.web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = origem.Settings.IsPasswordAutosaveEnabled;
            core.Settings.IsGeneralAutofillEnabled = origem.Settings.IsGeneralAutofillEnabled;
            configurar?.Invoke(core);
            core.DocumentTitleChanged += (_, _) => { if (!j.fechada) j.Title = string.IsNullOrWhiteSpace(core.DocumentTitle) ? j.rotuloSite : core.DocumentTitle + " — " + j.rotuloSite; };
            core.WindowCloseRequested += (_, _) => { if (!j.fechada) j.Close(); };   // window.close() do portal
            // Popups abertos pelo popup (ex.: imprimir em outra janela) recebem o mesmo tratamento.
            core.NewWindowRequested += (s, e2) => Atender(s, e2, rotuloSite, tema, configurar);
            e.NewWindow = core;
            e.Handled = true;
        }
        catch (Exception ex)
        {
            RegistroErros.Gravar("sites.popup", ex);
            if (j is not null && !j.fechada) { try { j.Close(); } catch (Exception) { } }
            try { e.Handled = false; } catch (Exception) { }
        }
        finally { try { adiamento.Complete(); } catch (Exception ex) { RegistroErros.Gravar("sites.popup.concluir", ex); } }
    }

    void Descartar()
    {
        if (fechada) return;
        fechada = true;
        abertas.Remove(this);
        try { web.Close(); } catch (Exception) { }
    }

    /// <summary>Ao fechar o app: fecha as janelas auxiliares ainda abertas.</summary>
    public static void FecharTodas()
    {
        foreach (var j in abertas.ToList()) { try { j.Close(); } catch (Exception) { j.Descartar(); } }
    }
}
