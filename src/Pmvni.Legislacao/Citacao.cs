using System.Globalization;
using System.Text.RegularExpressions;

namespace Pmvni.Legislacao;

/// <summary>Copiar e citar o trecho selecionado no leitor (desenho §11.7). A página é a posição no PDF (1, 2, 3…), não o número
/// impresso na folha; o título é o da linha do Ajustes. Nada é inventado: sem seleção, não há citação.</summary>
public static partial class Citacao
{
    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Espacos();

    [GeneratedRegex(@"\r\n|\r|\n", RegexOptions.CultureInvariant)]
    private static partial Regex Quebras();

    /// <summary><c>“&lt;texto com espaços colapsados&gt;” (&lt;título&gt;, página N)</c>, ou <c>páginas N–M</c> quando a seleção atravessa
    /// páginas (travessão "–"); texto vazio ou só de espaços ⇒ null.</summary>
    public static string? De(string textoSelecionado, string titulo, int paginaInicial, int paginaFinal)
    {
        var texto = Espacos().Replace(textoSelecionado ?? "", " ").Trim();
        if (texto.Length == 0) return null;
        var (a, b) = paginaInicial <= paginaFinal ? (paginaInicial, paginaFinal) : (paginaFinal, paginaInicial);
        var paginas = a == b
            ? "página " + a.ToString(CultureInfo.InvariantCulture)
            : $"páginas {a.ToString(CultureInfo.InvariantCulture)}–{b.ToString(CultureInfo.InvariantCulture)}";
        return $"“{texto}” ({titulo}, {paginas})";
    }

    /// <summary>O texto selecionado como vai para a área de transferência: pontas aparadas e quebras de linha em <c>\r\n</c>.</summary>
    public static string TextoParaCopiar(string s) => Quebras().Replace((s ?? "").Trim(), "\r\n");
}
