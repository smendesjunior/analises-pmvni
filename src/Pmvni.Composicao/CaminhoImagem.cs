namespace Pmvni.Composicao;

/// <summary>Onde o renderizador (e a importação) leem a imagem de um <c>dados.json</c>: só caminhos relativos à pasta dele. Absoluto
/// (<c>C:\…</c>, <c>\…</c>, <c>C:x</c>), de rede (<c>\\host\share\…</c>, <c>//host/…</c>), de dispositivo (<c>\\?\…</c>) ou com <c>:</c>
/// é recusado sempre: um dados.json (ou um <c>.rpmvni</c> adulterado) nunca embute um arquivo qualquer do computador nem faz o Windows
/// abrir uma conexão SMB. <paramref name="soNaPasta"/> (a pasta de trabalho de um pacote, onde só há <c>assets\</c>): também nada de
/// <c>..</c>, e o caminho final tem de ficar dentro da pasta. Sem <paramref name="soNaPasta"/> o <c>..</c> relativo é aceito só se o
/// caminho resolvido ficar DENTRO da pasta do processo (a análise do kit cita o mapa de uma pasta irmã,
/// <c>../REANALISE_…/renders/mapa.png</c>): o ancestral que contém <c>.pmvni-ai</c> (<c>PROCESSOS\&lt;proc&gt;</c>) ou, sem ele, a pasta pai
/// da pasta do dados.json. Junções e links são resolvidos antes da comparação: uma junção que aponta para fora do limite recusa o caminho.</summary>
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
        string completo, raiz, limite;
        try
        {
            raiz = Path.GetFullPath(pastaBase);
            completo = Path.GetFullPath(Path.Combine(raiz, relativo));
            limite = soNaPasta ? raiz : PastaDoProcesso(raiz);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        // Depois de resolver, ainda local (nada de UNC por algum desvio) e dentro do limite (a pasta, ou a pasta do processo).
        if (completo.StartsWith(@"\\", StringComparison.Ordinal) && !raiz.StartsWith(@"\\", StringComparison.Ordinal)) return null;
        if (!Dentro(completo, limite)) return null;
        // Junção ou link para fora: compara os caminhos reais (links resolvidos) e recusa se não conseguir resolver.
        try
        {
            if (!Dentro(ResolverLinks(completo, 0), ResolverLinks(limite, 0))) return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        return completo;
    }

    /// <summary>Quantos níveis acima da pasta do dados.json a pasta do processo pode estar (o kit grava em <c>&lt;processo&gt;\.pmvni-ainalise</c>: 2).</summary>
    const int NiveisAcima = 3;

    /// <summary>A pasta do processo: o ancestral (a própria pasta incluída, até <see cref="NiveisAcima"/> acima) que contém <c>.pmvni-ai</c>; sem ele, a
    /// pasta pai. Não sobe até a raiz: um <c>.pmvni-ai</c> numa pasta de cima (a raiz de PROCESSOS, por exemplo) não amplia o limite a todos os processos.</summary>
    internal static string PastaDoProcesso(string pastaDados)
    {
        var nivel = 0;
        for (var d = pastaDados; d is not null && nivel <= NiveisAcima; d = Path.GetDirectoryName(d), nivel++)
        {
            var ia = Path.Combine(d, ".pmvni-ai");
            if (Directory.Exists(ia) && (File.GetAttributes(ia) & FileAttributes.ReparsePoint) == 0) return d;
        }
        return Path.GetDirectoryName(pastaDados) ?? pastaDados;
    }

    /// <summary>O caminho com todo link simbólico ou junção que exista nele trocado pelo alvo (recursivo, com limite de profundidade).</summary>
    static string ResolverLinks(string caminho, int fundo)
    {
        if (fundo > 16) throw new IOException("Links demais no caminho.");
        var raiz = Path.GetPathRoot(caminho) ?? "";
        var atual = raiz;
        var partes = caminho[raiz.Length..].Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < partes.Length; i++)
        {
            atual = Path.Combine(atual, partes[i]);
            FileSystemInfo info = Directory.Exists(atual) ? new DirectoryInfo(atual) : new FileInfo(atual);
            if (!info.Exists)
            {
                // O resto ainda não existe: não há links a resolver adiante.
                for (var j = i + 1; j < partes.Length; j++) atual = Path.Combine(atual, partes[j]);
                return atual;
            }
            if (info.LinkTarget is null) continue;
            var alvo = info.ResolveLinkTarget(returnFinalTarget: true) ?? throw new IOException("Link sem destino: " + atual);
            atual = ResolverLinks(Path.GetFullPath(alvo.FullName), fundo + 1);
        }
        return atual;
    }

    static bool Dentro(string completo, string raiz)
    {
        var comBarra = raiz.EndsWith(Path.DirectorySeparatorChar) ? raiz : raiz + Path.DirectorySeparatorChar;
        return completo.StartsWith(comBarra, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Só estas extensões de imagem são lidas pelo renderizador.</summary>
    public static bool ExtensaoPermitida(string relativo) =>
        Path.GetExtension(relativo).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif";

    /// <summary>Extensão normalizada (<c>png</c>, <c>jpg</c>, <c>webp</c>, <c>gif</c>) pela assinatura dos bytes; null se for outro formato.</summary>
    public static string? ExtensaoPelaAssinatura(ReadOnlySpan<byte> b)
    {
        if (b.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (b.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "jpg";
        if (b.StartsWith("GIF87a"u8) || b.StartsWith("GIF89a"u8)) return "gif";
        if (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b.Slice(8, 4).SequenceEqual("WEBP"u8)) return "webp";
        return null;
    }

    /// <summary>Imagem aceita pelo renderizador: extensão permitida, o arquivo existe e a assinatura (lida do início) confere.</summary>
    public static bool ImagemAceita(string relativo, string caminho)
    {
        if (!ExtensaoPermitida(relativo)) return false;
        try
        {
            using var f = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var cab = new byte[12];
            var n = f.Read(cab, 0, cab.Length);
            return ExtensaoPelaAssinatura(cab.AsSpan(0, n)) is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }
}
