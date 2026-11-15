using System.Globalization;
using System.Text;

namespace Pmvni.Legislacao;

/// <summary>Regra de caixa e acento da pesquisa: NFD sem marcas combinantes, minúsculas invariantes, hífen suave removido,
/// espaços (inclusive NBSP e quebras de linha) colapsados e pontas aparadas. Trabalha por <see cref="Rune"/> (pares de
/// surrogates, como emoji e letras matemáticas, são um caractere só); um surrogate solto (texto de PDF mal formado) passa
/// sem alteração, nunca é normalizado.</summary>
public static class TextoPesquisa
{
    public static string Normalizar(string s) => ComMapa(s).Normalizado;

    /// <summary><c>Origem[i]</c> é o índice (em unidades UTF-16), no texto original, do início do caractere que gerou o i-ésimo
    /// caractere UTF-16 do texto normalizado; <c>Origem.Length == Normalizado.Length</c>.</summary>
    public static (string Normalizado, int[] Origem) ComMapa(string s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var sb = new StringBuilder(s.Length);
        var origem = new List<int>(s.Length);
        for (var j = 0; j < s.Length;)
        {
            var c = s[j];
            if (char.IsHighSurrogate(c) && j + 1 < s.Length && char.IsLowSurrogate(s[j + 1]))
            {
                AcrescentarRune(sb, origem, new Rune(c, s[j + 1]), j);
                j += 2;
                continue;
            }
            if (char.IsSurrogate(c)) { sb.Append(c); origem.Add(j); j++; continue; } // surrogate solto: como está
            if (c == '­') { j++; continue; }
            if (c < 0x80) Acrescentar(sb, origem, new Rune(char.ToLowerInvariant(c)), j);
            else AcrescentarRune(sb, origem, new Rune(c), j);
            j++;
        }
        if (sb.Length > 0 && sb[^1] == ' ') { sb.Length--; origem.RemoveAt(origem.Count - 1); }
        return (sb.ToString(), origem.ToArray());
    }

    /// <summary>Decompõe (NFD) um caractere válido, tira as marcas combinantes e o hífen suave e põe em minúsculas.</summary>
    static void AcrescentarRune(StringBuilder sb, List<int> origem, Rune r, int j)
    {
        foreach (var d in r.ToString().Normalize(NormalizationForm.FormD).EnumerateRunes())
        {
            if (d.Value == 0x00AD || Rune.GetUnicodeCategory(d) == UnicodeCategory.NonSpacingMark) continue;
            Acrescentar(sb, origem, Rune.ToLowerInvariant(d), j);
        }
    }

    static void Acrescentar(StringBuilder sb, List<int> origem, Rune r, int j)
    {
        if (Rune.IsWhiteSpace(r))
        {
            if (sb.Length == 0 || sb[^1] == ' ') return;
            r = new Rune(' ');
        }
        for (var k = 0; k < r.Utf16SequenceLength; k++) origem.Add(j);
        sb.Append(r.ToString());
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
