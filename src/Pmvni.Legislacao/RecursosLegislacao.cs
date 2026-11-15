using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Pmvni.Legislacao;

/// <summary>Os arquivos de <c>web\</c> embutidos no assembly: o PDF.js vendorizado (<c>pdfjs/…</c>, com o <c>VERSAO.json</c> de
/// sha256), a página do extrator e o seu script. O nome lógico é <c>leg/&lt;caminho&gt;</c> (o MSBuild grava <c>\</c> nas
/// subpastas; aqui tudo vira <c>/</c>).</summary>
public static class RecursosLegislacao
{
    public const string VersaoPdfJs = "6.4.299";

    static readonly Assembly Montagem = typeof(RecursosLegislacao).Assembly;
    static readonly Lazy<Dictionary<string, string>> Nomes = new(() =>
    {
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var nome in Montagem.GetManifestResourceNames())
            if (nome.StartsWith("leg/", StringComparison.Ordinal)) mapa[nome[4..].Replace('\\', '/')] = nome;
        return mapa;
    });
    static readonly Lazy<IReadOnlyDictionary<string, string>> Shas = new(LerVersao);

    /// <summary>Os arquivos do PDF.js: caminho relativo a <c>pdfjs/</c> (<c>build/pdf.min.mjs</c>…) → sha256 (minúsculas), do <c>VERSAO.json</c>.</summary>
    public static IReadOnlyDictionary<string, string> ShaPdfJs => Shas.Value;

    /// <summary>Soma dos tamanhos dos arquivos do PDF.js listados no <c>VERSAO.json</c>.</summary>
    public static long BytesPdfJs => Bytes.Value;

    static readonly Lazy<long> Bytes = new(() => Shas.Value.Keys.Sum(c => TamanhoDe("pdfjs/" + c)));

    /// <summary>O conteúdo de um recurso (<c>extrator.html</c>, <c>pdfjs/build/pdf.min.mjs</c>…), ou null se não existe.</summary>
    public static byte[]? Ler(string caminhoRelativo)
    {
        using var s = Abrir(caminhoRelativo);
        if (s is null) return null;
        using var ms = new MemoryStream((int)s.Length);
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Fluxo do recurso, sem copiar para a memória (quem chama fecha). Caminho vazio, absoluto, com <c>..</c> ou <c>\</c> ⇒ null.</summary>
    internal static Stream? Abrir(string caminhoRelativo)
    {
        if (string.IsNullOrEmpty(caminhoRelativo) || caminhoRelativo.Contains('\\') || caminhoRelativo.StartsWith('/')
            || caminhoRelativo.Split('/').Any(p => p is ".." or "." or "")) return null;
        return Nomes.Value.TryGetValue(caminhoRelativo, out var nome) ? Montagem.GetManifestResourceStream(nome) : null;
    }

    static long TamanhoDe(string caminho)
    {
        using var s = Abrir(caminho);
        return s?.Length ?? 0;
    }

    /// <summary>Confere o sha256 de cada arquivo do <c>VERSAO.json</c> com o embutido; falta, diferença ou arquivo a mais em
    /// <c>pdfjs/</c> ficam em <paramref name="divergentes"/>.</summary>
    public static bool ConferirPdfJs(out IReadOnlyList<string> divergentes)
    {
        var lista = new List<string>();
        foreach (var (caminho, sha) in ShaPdfJs)
        {
            using var s = Abrir("pdfjs/" + caminho);
            if (s is null) { lista.Add(caminho + " (ausente)"); continue; }
            if (!string.Equals(Convert.ToHexStringLower(SHA256.HashData(s)), sha, StringComparison.Ordinal)) lista.Add(caminho + " (sha256 diferente)");
        }
        foreach (var nome in Nomes.Value.Keys)
            if (nome.StartsWith("pdfjs/", StringComparison.Ordinal) && nome != "pdfjs/VERSAO.json" && !ShaPdfJs.ContainsKey(nome[6..]))
                lista.Add(nome[6..] + " (fora do VERSAO.json)");
        lista.Sort(StringComparer.Ordinal);
        divergentes = lista;
        return lista.Count == 0;
    }

    static IReadOnlyDictionary<string, string> LerVersao()
    {
        var bytes = Ler("pdfjs/VERSAO.json") ?? throw new InvalidOperationException("O VERSAO.json do PDF.js não está embutido.");
        var raiz = JsonNode.Parse(bytes) as JsonObject ?? throw new InvalidDataException("VERSAO.json inválido.");
        if (raiz["versao"]?.GetValue<string>() != VersaoPdfJs) throw new InvalidDataException("O VERSAO.json não é da versão " + VersaoPdfJs + ".");
        var arquivos = raiz["arquivos"] as JsonObject ?? throw new InvalidDataException("VERSAO.json sem a lista de arquivos.");
        return arquivos.ToDictionary(p => p.Key, p => p.Value!.GetValue<string>(), StringComparer.Ordinal);
    }
}
