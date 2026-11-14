using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        _ = IniciarAsync();
    }

    async Task IniciarAsync()
    {
        try
        {
            var ambiente = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, PastaDados.WebView2(), new CoreWebView2EnvironmentOptions());
            await Navegador.EnsureCoreWebView2Async(ambiente);
            Navegador.CoreWebView2.NavigationCompleted += (_, e) =>
            {
                if (!Autoteste.Ativo) return;
                if (e.IsSuccess) Autoteste.GravarSucesso(ambiente.BrowserVersionString);
                else Autoteste.GravarErro("NavigationCompleted: " + e.WebErrorStatus);
                Application.Current.Exit();
            };
            Navegador.NavigateToString("<p>ok</p>");
        }
        catch (Exception e)
        {
            if (!Autoteste.Ativo) throw;
            Autoteste.GravarErro(e.ToString());
            Application.Current.Exit();
        }
    }
}
