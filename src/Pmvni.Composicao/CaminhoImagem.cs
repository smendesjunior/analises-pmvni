namespace Pmvni.Composicao;

/// <summary>Onde o renderizador (e a importação) leem a imagem de um <c>dados.json</c>: só caminhos relativos à pasta dele. Absoluto
/// (<c>C:\…</c>, <c>\…</c>, <c>C:x</c>), de rede (<c>\\host\share\…</c>, <c>//host/…</c>), de dispositivo (<c>\\?\…</c>) ou com <c>:</c>
/// é recusado sempre: um dados.json (ou um <c>.pmvrel</c> adulterado) nunca embute um arquivo qualquer do computador nem faz o Windows
/// abrir uma conexão SMB. <paramref name="soNaPasta"/> (a pasta de trabalho de um pacote, onde só há <c>assets\</c>): também nada de
/// <c>..</c>, e o caminho final tem de ficar dentro da pasta. Sem <paramref name="soNaPasta"/> o <c>..</c> relativo continua aceito,
/// como no gerador do kit: a análise do kit cita o mapa de uma pasta irmã (<c>../REANALISE_…/renders/mapa.png</c>).</summary>
public static class CaminhoImagem
{
    /// <summary>O caminho completo do arquivo, ou null se o caminho é recusado.</summary>
    public static string? Resolver(string pastaBase, string relativo, bool soNaPasta)
    {
        ArgumentNullException.ThrowIfNull(pastaBase);
        ArgumentNullException.ThrowIfNull(relativo);
        if (relativo.Length == 0 || relativo.Any(char.IsControl)) return null;
        if (relativo.Contains(':') || relativo.StartsWith('\\') || relativo.StartsWith('/') || Path.IsPathRooted(relativo)) return null;
        var partes = relativo.Split('/', '\\');
        if (soNaPasta && partes.Any(p => p == "..")) return null;
        string completo, raiz;
        try
        {
            raiz = Path.GetFullPath(pastaBase);
            completo = Path.GetFullPath(Path.Combine(raiz, relativo));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        // Depois de resolver, ainda local (nada de UNC por algum desvio) e, no modo estrito, dentro da pasta.
        if (completo.StartsWith(@"\\", StringComparison.Ordinal)) return raiz.StartsWith(@"\\", StringComparison.Ordinal) && Dentro(completo, raiz) ? completo : null;
        if (soNaPasta && !Dentro(completo, raiz)) return null;
        return completo;
    }

    static bool Dentro(string completo, string raiz)
    {
        var comBarra = raiz.EndsWith(Path.DirectorySeparatorChar) ? raiz : raiz + Path.DirectorySeparatorChar;
        return completo.StartsWith(comBarra, StringComparison.OrdinalIgnoreCase);
    }
}
