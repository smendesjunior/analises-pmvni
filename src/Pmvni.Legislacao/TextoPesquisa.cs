using System.Globalization;
using System.Text;

namespace Pmvni.Legislacao;

/// <summary>Regra de caixa e acento da pesquisa: NFD sem marcas combinantes, minúsculas invariantes, hífen suave removido,
/// espaços (inclusive NBSP e quebras de linha) colapsados e pontas aparadas.</summary>
public static class TextoPesquisa
{
    public static string Normalizar(string s) => ComMapa(s).Normalizado;

    /// <summary><c>Origem[i]</c> é o índice, no texto original, do i-ésimo caractere do texto normalizado.</summary>
    public static (string Normalizado, int[] Origem) ComMapa(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var sb = new StringBuilder(s.Length);
        var origem = new List<int>(s.Length);
        for (var j = 0; j < s.Length; j++)
        {
            var c = s[j];
            if (c == '­') continue;
            if (c < 0x80)
            {
                Acrescentar(sb, origem, char.ToLowerInvariant(c), j);
                continue;
            }
            foreach (var d in c.ToString().Normalize(NormalizationForm.FormD))
            {
                if (d == '­' || CharUnicodeInfo.GetUnicodeCategory(d) == UnicodeCategory.NonSpacingMark) continue;
                Acrescentar(sb, origem, char.ToLowerInvariant(d), j);
            }
        }
        if (sb.Length > 0 && sb[^1] == ' ') { sb.Length--; origem.RemoveAt(origem.Count - 1); }
        return (sb.ToString(), origem.ToArray());
    }

    static void Acrescentar(StringBuilder sb, List<int> origem, char c, int j)
    {
        if (char.IsWhiteSpace(c))
        {
            if (sb.Length == 0 || sb[^1] == ' ') return;
            c = ' ';
        }
        sb.Append(c);
        origem.Add(j);
    }

    /// <summary>Texto com os espaços (inclusive NBSP e quebras) colapsados num espaço e as pontas aparadas; o resto fica como está.</summary>
    internal static string ColapsarEspacos(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (char.IsWhiteSpace(c)) { if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' '); }
            else sb.Append(c);
        }
        if (sb.Length > 0 && sb[^1] == ' ') sb.Length--;
        return sb.ToString();
    }
}
