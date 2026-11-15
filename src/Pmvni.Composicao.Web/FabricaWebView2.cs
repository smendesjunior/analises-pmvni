using Microsoft.Web.WebView2.Core;

namespace Pmvni.Composicao.Web;

/// <summary>Única fábrica do ambiente e das opções de controller do WebView2, para o controle XAML do app e para o
/// <see cref="HostOculto"/> (as duas instâncias convivem no mesmo processo e precisam das mesmas opções).
/// Os controllers são InPrivate (D4): o conteúdo do relatório (HTML, imagens, cache, armazenamento) não é gravado no
/// perfil em <c>&lt;dados&gt;\WebView2</c>; ao fechar, nada dele fica no disco.</summary>
public static class FabricaWebView2
{
    /// <summary>Ambiente com as opções padrão e a pasta de dados <paramref name="pastaDadosUsuario"/> (padrão:
    /// <see cref="PastaDados.WebView2"/>).</summary>
    public static async Task<CoreWebView2Environment> CriarAmbienteAsync(string? pastaDadosUsuario = null) =>
        await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, pastaDadosUsuario ?? PastaDados.WebView2(), new CoreWebView2EnvironmentOptions());

    /// <summary>Opções de controller comuns: InPrivate ligado.</summary>
    public static CoreWebView2ControllerOptions OpcoesController(CoreWebView2Environment ambiente)
    {
        ArgumentNullException.ThrowIfNull(ambiente);
        var opcoes = ambiente.CreateCoreWebView2ControllerOptions();
        opcoes.IsInPrivateModeEnabled = true;
        return opcoes;
    }

    /// <summary>Versão do WebView2 Runtime instalado, ou null se não houver runtime (o app explica e não abre a visualização).</summary>
    public static string? VersaoDisponivel()
    {
        try
        {
            var v = CoreWebView2Environment.GetAvailableBrowserVersionString();
            return string.IsNullOrWhiteSpace(v) ? null : v;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or FileNotFoundException or InvalidOperationException
                                      or UnauthorizedAccessException or TypeInitializationException or DllNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Texto simples para quem não tem o WebView2 Runtime.</summary>
    public const string ExplicacaoSemRuntime =
        "Este computador não tem o Microsoft Edge WebView2 Runtime, que o programa usa para mostrar e imprimir o relatório. "
        + "Ele já vem com o Windows 11; se foi removido, instale o \"Microsoft Edge WebView2 Runtime\" (versão Evergreen) "
        + "pelo site oficial da Microsoft ou pelo Windows Update e abra o programa de novo.";
}
