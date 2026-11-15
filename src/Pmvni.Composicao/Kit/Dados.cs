using System.Collections;
using System.Globalization;
using System.Text;

namespace Pmvni.Composicao.Kit;

/// <summary>Acesso aos dados no modelo de objetos do molde, com as semânticas do Python
/// (<c>d.get(k)</c>, <c>d[k]</c>, <c>len()</c>, iteração) e erros como <see cref="MoldeErro"/>.</summary>
internal static class Dados
{
    public static IReadOnlyDictionary<string, object?> Dict(object? o) =>
        o as IReadOnlyDictionary<string, object?>
        ?? throw new MoldeErro("'" + PythonTexto.NomeTipo(o) + "' object has no attribute 'get'");

    /// <summary><c>d.get(k)</c>.</summary>
    public static object? Obter(object? d, string k) => Dict(d).TryGetValue(k, out var v) ? v : null;

    /// <summary><c>d.get(k, padrao)</c>: o padrão só vale se a chave não existir (valor null do JSON é devolvido).</summary>
    public static object? Obter(object? d, string k, object? padrao) => Dict(d).TryGetValue(k, out var v) ? v : padrao;

    /// <summary><c>d[k]</c>.</summary>
    public static object? Exigir(object? d, string k) =>
        Dict(d).TryGetValue(k, out var v) ? v : throw new MoldeErro(PythonTexto.ReprTexto(k));

    /// <summary>Itera uma lista como o <c>for</c> do Python (None, números etc. não são iteráveis).</summary>
    public static List<object?> Lista(object? o) =>
        o is IEnumerable e and not string and not IReadOnlyDictionary<string, object?>
            ? e.Cast<object?>().ToList()
            : throw new MoldeErro("'" + PythonTexto.NomeTipo(o) + "' object is not iterable");

    /// <summary><c>len(x)</c>.</summary>
    public static int Len(object? o) => o switch
    {
        string s => s.EnumerateRunes().Count(),
        IReadOnlyDictionary<string, object?> d => d.Count,
        ICollection c => c.Count,
        _ => throw new MoldeErro("object of type '" + PythonTexto.NomeTipo(o) + "' has no len()"),
    };

    /// <summary>f'{x}' / str(x) / format(x) no Python (None vira "None").</summary>
    public static string Txt(object? o) => PythonTexto.StrPython(o);

    /// <summary>html.escape(str(t)): a função <c>e</c> do kit.</summary>
    public static string E(object? t) => PythonTexto.Escape(PythonTexto.StrPython(t));

    /// <summary>f'{x:.1f}' do Python: arredondamento exato (metade para o par, como o Python).</summary>
    public static string F1(double x)
    {
        if (double.IsNaN(x)) return "nan";
        if (double.IsInfinity(x)) return x > 0 ? "inf" : "-inf";
        var neg = x < 0 || (x == 0 && double.IsNegative(x));
        var s = Math.Abs(x).ToString("F60", CultureInfo.InvariantCulture); // expansão decimal exata
        var ponto = s.IndexOf('.');
        var inteiro = s[..ponto];
        var frac = s[(ponto + 1)..];
        var digito = frac[0] - '0';
        var resto = frac[1..];
        var sobeUm = resto[0] > '5' ||
                     (resto[0] == '5' && (resto[1..].Any(c => c != '0') || digito % 2 == 1)); // empate exato: para o par
        var todos = (inteiro + digito.ToString(CultureInfo.InvariantCulture)).ToCharArray();
        if (sobeUm)
        {
            var i = todos.Length - 1;
            while (i >= 0)
            {
                if (todos[i] == '9') { todos[i] = '0'; i--; }
                else { todos[i]++; break; }
            }
            var t = new string(todos);
            if (i < 0) t = "1" + t;
            todos = t.ToCharArray();
        }
        var txt = new string(todos);
        return (neg ? "-" : "") + txt[..^1] + "." + txt[^1];
    }

    /// <summary>Compara por pontos de código (como o <c>sorted</c> de str do Python).</summary>
    public static int ComparaPontosDeCodigo(string a, string b)
    {
        var ra = a.EnumerateRunes().GetEnumerator();
        var rb = b.EnumerateRunes().GetEnumerator();
        while (true)
        {
            var ta = ra.MoveNext();
            var tb = rb.MoveNext();
            if (!ta || !tb) return ta == tb ? 0 : (ta ? 1 : -1);
            var c = ra.Current.Value.CompareTo(rb.Current.Value);
            if (c != 0) return c;
        }
    }

    public static string DeRunes(IEnumerable<int> pontos)
    {
        var sb = new StringBuilder();
        foreach (var p in pontos) sb.Append(new Rune(p).ToString());
        return sb.ToString();
    }
}
