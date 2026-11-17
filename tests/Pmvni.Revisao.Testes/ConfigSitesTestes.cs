using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Caso Izabela (2026-11-17): o login dos sites vem só de Ajustes; o navegador não preenche um login antigo ("izabelat").</summary>
public class ConfigSitesTestes
{
    [Fact]
    public void navegador_nao_preenche_login_por_conta_propria()
    {
        Assert.False(ConfigSites.PreenchimentoDoNavegador);
        Assert.True(ConfigSites.ApagarPreenchimentoGuardado);
        var fonte = File.ReadAllText(Path.Combine(Raiz(), "src", "Pmvni.App", "Paginas", "PaginaSites.cs"));
        Assert.Contains("IsPasswordAutosaveEnabled = ConfigSites.PreenchimentoDoNavegador", fonte);
        Assert.Contains("IsGeneralAutofillEnabled = ConfigSites.PreenchimentoDoNavegador", fonte);
        Assert.Contains("CoreWebView2BrowsingDataKinds.PasswordAutosave | CoreWebView2BrowsingDataKinds.GeneralAutofill", fonte);
    }

    static string Raiz()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !Directory.Exists(Path.Combine(d.FullName, "src", "Pmvni.App"))) d = d.Parent;
        return d?.FullName ?? throw new DirectoryNotFoundException("raiz do repositório");
    }
}
