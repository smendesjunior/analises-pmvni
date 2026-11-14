using Microsoft.Web.WebView2.Core;

namespace Pmvni.Composicao.Web;

/// <summary>Sandbox do documento: só a pasta da sessão, mapeada para um host virtual, é acessível; tudo o mais recebe 403.
/// Camadas: mapeamento com acesso Deny, filtro de requisições (403), CSP no HTML da sessão (ver <see cref="DocumentoWeb"/>),
/// ajustes do WebView2 desligados, janelas novas e downloads cancelados e navegação restrita ao host virtual.</summary>
public static class Seguranca
{
    public const string HostDocumento = "doc.pmvni.invalid";
    public const string UrlBase = "https://" + HostDocumento + "/";

    public const string Csp =
        "default-src 'none'; img-src data: https://doc.pmvni.invalid; style-src 'unsafe-inline'; script-src https://doc.pmvni.invalid/paginador.js";

    /// <summary>Aplica a sandbox a <paramref name="core"/> e mapeia <paramref name="pastaSessao"/> para <see cref="HostDocumento"/>.
    /// Chamar na thread do CoreWebView2, uma vez por core (o <see cref="DocumentoWeb"/> já a aplica na construção). <paramref name="aoBloquear"/> recebe a URL de cada requisição respondida com 403.</summary>
    public static Task AplicarAsync(CoreWebView2 core, string pastaSessao, Action<string>? aoBloquear = null)
    {
        ArgumentNullException.ThrowIfNull(core);
        Directory.CreateDirectory(pastaSessao);

        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsStatusBarEnabled = false;
        s.AreHostObjectsAllowed = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsWebMessageEnabled = true; // a ponte tipada usa postMessage
#if DEBUG
        s.AreDevToolsEnabled = true;
#else
        s.AreDevToolsEnabled = false;
#endif

        core.SetVirtualHostNameToFolderMapping(HostDocumento, pastaSessao, CoreWebView2HostResourceAccessKind.Deny);

        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (sender, e) =>
        {
            var uri = e.Request.Uri;
            if (SubrecursoPermitido(uri)) return;
            e.Response = sender.Environment.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain");
            aoBloquear?.Invoke(uri);
        };

        core.NewWindowRequested += (_, e) => { e.Handled = true; };
        core.DownloadStarting += (_, e) => { e.Cancel = true; e.Handled = true; };
        core.NavigationStarting += (_, e) =>
        {
            if (!EhHostDocumento(e.Uri)) { e.Cancel = true; aoBloquear?.Invoke(e.Uri); }
        };
        return Task.CompletedTask;
    }

    /// <summary>Navegação de nível superior: somente https://doc.pmvni.invalid/.</summary>
    internal static bool EhHostDocumento(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && EhHostDocumento(u);

    internal static bool EhHostDocumento(Uri u) =>
        u.Scheme == Uri.UriSchemeHttps && string.Equals(u.Host, HostDocumento, StringComparison.OrdinalIgnoreCase);

    /// <summary>Subrecursos: o host virtual do documento e URIs sem rede (data:, blob:).</summary>
    internal static bool SubrecursoPermitido(string uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var u) && (EhHostDocumento(u) || u.Scheme is "data" or "blob");
}
