using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>O texto que o usuário digita é texto puro: uma linha só (o modelo do kit não tem quebra de linha), sem espaços nas pontas.
/// Nada é escapado aqui (o kit escapa na impressão) e nada vira marcação.</summary>
public static partial class TextoEditado
{
    [GeneratedRegex(@"(?:\r\n|\r|\n|\u2028|\u2029)+", RegexOptions.CultureInvariant)]
    private static partial Regex Quebras();

    /// <summary>Cada sequência de quebras de linha (<c>\r\n</c>, <c>\r</c>, <c>\n</c>, U+2028, U+2029) vira um único espaço; as pontas são aparadas; nada mais muda.</summary>
    public static string Normalizar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        return Quebras().Replace(texto, " ").Trim();
    }
}
