namespace Pmvni.Composicao.Web;

/// <summary>Pasta de dados do app. Padrão: %LOCALAPPDATA%\EditorRelatoriosPMVNI; a variável
/// PMVNI_DADOS_DIR troca a raiz (testes e execuções a partir do shell do Claude). A cópia de desenvolvimento (o .exe em Aplicativo\ do
/// projeto) usa uma pasta própria, limpa: <c>%LOCALAPPDATA%\EditorRelatoriosPMVNI-Desenvolvimento</c> (2026-11-16).</summary>
public static class PastaDados
{
    public const string VariavelAmbiente = "PMVNI_DADOS_DIR";
    public const string NomePastaDesenvolvimento = "EditorRelatoriosPMVNI-Desenvolvimento";

    /// <summary>O .exe em uso é a cópia de desenvolvimento: está numa pasta cuja pasta-mãe tem a solução do projeto
    /// (<c>EditorRelatoriosPMVNI.sln</c>). A cópia de uso (instalada em %LOCALAPPDATA%\Programs) nunca é.</summary>
    public static bool CopiaDeDesenvolvimento { get; } = EhDesenvolvimento(Environment.ProcessPath);

    public static bool EhDesenvolvimento(string? exe)
    {
        try
        {
            if (string.IsNullOrEmpty(exe) || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return false;
            var mae = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(exe)));
            return mae is not null && File.Exists(Path.Combine(mae, "EditorRelatoriosPMVNI.sln"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
    }

    public static string Raiz
    {
        get
        {
            var configurada = Environment.GetEnvironmentVariable(VariavelAmbiente);
            if (!string.IsNullOrWhiteSpace(configurada)) return Path.GetFullPath(configurada);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (CopiaDeDesenvolvimento) return Path.Combine(local, NomePastaDesenvolvimento);
            return Path.Combine(local, "EditorRelatoriosPMVNI");
        }
    }

    /// <summary>Dados que acompanham o usuário em outro PC (2026-11-16): <c>%APPDATA%\EditorRelatoriosPMVNI</c> (AppData\Roaming), com o
    /// ajustes.json e a legislação anexada. O resto (recuperação, sessões, logs, chaves DPAPI, WebView2, LibreOffice) fica em <see cref="Raiz"/>
    /// (AppData\Local), que é deste computador. Com <c>PMVNI_DADOS_DIR</c> (testes), tudo fica nessa pasta. Na primeira abertura, o que já
    /// existia no Local é copiado para cá (o original fica).</summary>
    public static string Itinerante
    {
        get
        {
            var configurada = Environment.GetEnvironmentVariable(VariavelAmbiente);
            if (!string.IsNullOrWhiteSpace(configurada)) return Path.GetFullPath(configurada);
            if (CopiaDeDesenvolvimento) return Raiz; // a cópia de desenvolvimento não leva nem traz ajustes do usuário
            var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EditorRelatoriosPMVNI");
            Migrar(Raiz, pasta);
            return pasta;
        }
    }

    static bool migrado;

    static void Migrar(string local, string itinerante)
    {
        if (migrado) return;
        migrado = true;
        try
        {
            Directory.CreateDirectory(itinerante);
            var ajustes = Path.Combine(local, "ajustes.json");
            var novo = Path.Combine(itinerante, "ajustes.json");
            if (File.Exists(ajustes) && !File.Exists(novo)) File.Copy(ajustes, novo);
            var leis = Path.Combine(local, "legislacao");
            var leisNovas = Path.Combine(itinerante, "legislacao");
            if (Directory.Exists(leis) && !Directory.Exists(leisNovas))
            {
                foreach (var d in Directory.GetDirectories(leis, "*", SearchOption.AllDirectories))
                    Directory.CreateDirectory(Path.Combine(leisNovas, Path.GetRelativePath(leis, d)));
                Directory.CreateDirectory(leisNovas);
                foreach (var f in Directory.GetFiles(leis, "*", SearchOption.AllDirectories))
                    File.Copy(f, Path.Combine(leisNovas, Path.GetRelativePath(leis, f)), overwrite: false);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* sem cópia: o app começa do padrão */ }
    }

    /// <summary>Pasta de dados do usuário do WebView2 (&lt;raiz&gt;\WebView2), criada se faltar.</summary>
    public static string WebView2()
    {
        var pasta = Path.Combine(Raiz, "WebView2");
        Directory.CreateDirectory(pasta);
        return pasta;
    }
}
