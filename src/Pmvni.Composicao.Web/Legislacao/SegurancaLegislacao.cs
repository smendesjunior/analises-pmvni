using System.Text;
using Microsoft.Web.WebView2.Core;
using Pmvni.Legislacao;

namespace Pmvni.Composicao.Web.Legislacao;

/// <summary>Sandbox do WebView2 da legislação (extrator e, na Tarefa 18, leitor): o host <c>leg.pmvni.invalid</c> <b>sem pasta
/// mapeada</b>, servido da memória pelo <see cref="ServidorLegislacao"/>; o que o servidor não reconhece recebe 403. As mesmas
/// configurações da <see cref="Seguranca"/> (menus, preenchimento, atalhos do navegador desligados) e, aqui, também o zoom.</summary>
public static class SegurancaLegislacao
{
    public const string UrlBase = "https://" + ServidorLegislacao.Host + "/";

    /// <summary>Aplica a sandbox a <paramref name="core"/>: chamar na thread do CoreWebView2, uma vez por core.
    /// <paramref name="paginaPermitida"/> é a única navegação de nível superior aceita, e só uma vez; recarga, F5 ou qualquer outra
    /// URL são canceladas. <paramref name="aoBloquear"/> recebe a URL de cada requisição respondida com 403 e de cada navegação cancelada.</summary>
    public static Task AplicarAsync(CoreWebView2 core, ServidorLegislacao s, string paginaPermitida, Action<string>? aoBloquear)
    {
        ArgumentNullException.ThrowIfNull(core);
        ArgumentNullException.ThrowIfNull(s);
        ArgumentException.ThrowIfNullOrEmpty(paginaPermitida);

        var cfg = core.Settings;
        cfg.AreDefaultContextMenusEnabled = false;
        cfg.IsGeneralAutofillEnabled = false;
        cfg.IsPasswordAutosaveEnabled = false;
        cfg.IsStatusBarEnabled = false;
        cfg.AreHostObjectsAllowed = false;
        cfg.AreDefaultScriptDialogsEnabled = false;
        cfg.IsWebMessageEnabled = true; // a ponte tipada usa postMessage
        cfg.AreBrowserAcceleratorKeysEnabled = false; // Ctrl+P, F5/Ctrl+R, Ctrl+F, Ctrl+S, Ctrl+roda…: a escala é do leitor
        cfg.IsZoomControlEnabled = false;
#if DEBUG
        cfg.AreDevToolsEnabled = true;
#else
        cfg.AreDevToolsEnabled = false;
#endif

        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (sender, e) =>
        {
            var uri = e.Request.Uri;
            string? range = null;
            try { if (e.Request.Headers.Contains("Range")) range = e.Request.Headers.GetHeader("Range"); }
            catch (Exception ex) when (ex is ArgumentException or System.Runtime.InteropServices.COMException) { }
            var resposta = s.Responder(e.Request.Method, uri, range);
            if (resposta is null)
            {
                e.Response = sender.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain");
                aoBloquear?.Invoke(uri);
                return;
            }
            e.Response = Criar(sender.Environment, resposta);
        };

        var navegacaoUsada = false;
        core.NavigationStarting += (_, e) =>
        {
            if (!navegacaoUsada && string.Equals(e.Uri, paginaPermitida, StringComparison.Ordinal)) { navegacaoUsada = true; return; }
            e.Cancel = true;
            aoBloquear?.Invoke(e.Uri);
        };
        core.NewWindowRequested += (_, e) => { e.Handled = true; };
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.PermissionRequested += (_, e) => { e.State = CoreWebView2PermissionState.Deny; };
        return Task.CompletedTask;
    }

    static CoreWebView2WebResourceResponse Criar(CoreWebView2Environment ambiente, RespostaLeg r)
    {
        var cabecalhos = new StringBuilder("Content-Type: ").Append(r.TipoConteudo).Append("\r\n");
        foreach (var (nome, valor) in r.Cabecalhos) cabecalhos.Append(nome).Append(": ").Append(valor).Append("\r\n");
        var corpo = r.AbrirCorpo?.Invoke();
        if (corpo is not null && r.Tamanho is { } n) cabecalhos.Append("Content-Length: ").Append(n).Append("\r\n");
        return ambiente.CreateWebResourceResponse(corpo?.AsRandomAccessStream(), r.Status, r.Razao, cabecalhos.ToString());
    }

    /// <summary>Origem de uma mensagem web: só <c>https://leg.pmvni.invalid/…</c> (a página do próprio extrator ou leitor).</summary>
    internal static bool EhOrigemLegislacao(string? origem) =>
        Uri.TryCreate(origem, UriKind.Absolute, out var u)
        && u.Scheme == Uri.UriSchemeHttps && string.Equals(u.Host, ServidorLegislacao.Host, StringComparison.OrdinalIgnoreCase);
}
