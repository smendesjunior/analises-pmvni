using System.Text;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Composicao.Testes;

/// <summary>Acesso às referências geradas pelo próprio kit (tests/referencias, versionadas).</summary>
public static class Referencias
{
    public static string Pasta { get; } = Localizar();

    private static string Localizar()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "tests", "referencias");
            if (Directory.Exists(c)) return c;
        }
        Assert.Fail("tests/referencias não encontrada; rode: python -I tools/gerar_referencias.py --kit <pasta do kit>");
        return "";
    }

    public static string Texto(string nome) => new UTF8Encoding(false, true).GetString(File.ReadAllBytes(Path.Combine(Pasta, nome)));

    public static object? Json(string nome) => JsonModelo.Ler(Texto(nome));

    public static IEnumerable<string> Nomes(string prefixo, string sufixo) =>
        Directory.GetFiles(Pasta, prefixo + "*" + sufixo).Select(Path.GetFileName).OfType<string>().OrderBy(n => n, StringComparer.Ordinal);
}
