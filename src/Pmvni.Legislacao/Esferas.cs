using System.Globalization;
using System.Text;

namespace Pmvni.Legislacao;

/// <summary>Esfera da lei, na ordem em que a lista é mostrada.</summary>
public enum Esfera { Federal, Estadual, Municipal, Outras }

public static class Esferas
{
    public static IReadOnlyList<Esfera> Ordem { get; } = [Esfera.Federal, Esfera.Estadual, Esfera.Municipal, Esfera.Outras];

    public static string Rotulo(Esfera e) => e switch
    {
        Esfera.Federal => "Federal",
        Esfera.Estadual => "Estadual",
        Esfera.Municipal => "Municipal",
        _ => "Outras",
    };

    /// <summary>Esfera lida do catálogo (o rótulo); desconhecida ⇒ Outras.</summary>
    internal static Esfera DeRotulo(string? rotulo) =>
        Ordem.FirstOrDefault(e => string.Equals(Rotulo(e), rotulo, StringComparison.Ordinal), Esfera.Outras);

    /// <summary>Esfera sugerida pelo caminho do original (desenho §11.1): o nome do arquivo primeiro, depois as pastas, da mais
    /// próxima para a mais distante; o primeiro trecho que contém "federal" ⇒ Federal, "estadual" ou "cbmes" ⇒ Estadual,
    /// "municipal" ⇒ Municipal (sem caixa nem acento); senão Outras.</summary>
    public static Esfera Sugerir(string caminhoOriginal)
    {
        if (string.IsNullOrWhiteSpace(caminhoOriginal)) return Esfera.Outras;
        var partes = caminhoOriginal.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = partes.Length - 1; i >= 0; i--)
        {
            var t = SemAcentoNemCaixa(partes[i]);
            if (t.Contains("federal", StringComparison.Ordinal)) return Esfera.Federal;
            if (t.Contains("estadual", StringComparison.Ordinal) || t.Contains("cbmes", StringComparison.Ordinal)) return Esfera.Estadual;
            if (t.Contains("municipal", StringComparison.Ordinal)) return Esfera.Municipal;
        }
        return Esfera.Outras;
    }

    static string SemAcentoNemCaixa(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }
}
