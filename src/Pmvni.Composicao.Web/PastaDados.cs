namespace Pmvni.Composicao.Web;

/// <summary>Pasta de dados do app. Padrão: %LOCALAPPDATA%\EditorRelatoriosPMVNI; a variável
/// PMVNI_DADOS_DIR troca a raiz (testes e execuções a partir do shell do Claude).</summary>
public static class PastaDados
{
    public const string VariavelAmbiente = "PMVNI_DADOS_DIR";

    public static string Raiz
    {
        get
        {
            var configurada = Environment.GetEnvironmentVariable(VariavelAmbiente);
            if (!string.IsNullOrWhiteSpace(configurada)) return Path.GetFullPath(configurada);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "EditorRelatoriosPMVNI");
        }
    }

    /// <summary>Pasta de dados do usuário do WebView2 (&lt;raiz&gt;\WebView2), criada se faltar.</summary>
    public static string WebView2()
    {
        var pasta = Path.Combine(Raiz, "WebView2");
        Directory.CreateDirectory(pasta);
        return pasta;
    }
}
