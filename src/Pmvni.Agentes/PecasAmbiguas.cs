using System.Security.Cryptography;

namespace Pmvni.Agentes;

/// <summary>Uma versão possível da peça na escolha de peças ambíguas: o valor que volta ao kit (o caminho do PDF, como veio em <c>opcoes[].valor</c>), o texto
/// da opção, o arquivo no disco (null = fora da pasta do processo ou inexistente: não abre nem mostra miniatura) e a miniatura da primeira página no
/// <c>cache-pdf</c> (null = o kit ainda não a renderizou).</summary>
public sealed record PecaCandidata(string Valor, string Texto, string? Arquivo, string? Miniatura)
{
    /// <summary>O nome do arquivo (o que a tela mostra quando não há miniatura).</summary>
    public string Nome => Path.GetFileName(Arquivo ?? Valor.Replace('/', Path.DirectorySeparatorChar));
}

/// <summary>Tarefa 14: a decisão do tipo <c>pecas</c> (o kit achou mais de uma versão possível de uma peça). A resposta é o caminho do PDF escolhido, gravado
/// como as outras decisões (<see cref="RespostaDecisao"/>); o motivo da dúvida vem na pergunta. Nada aqui chama a IA: só lê a pasta do processo.</summary>
public static class PecasAmbiguas
{
    public const string Tipo = "pecas";

    public static bool EhEscolhaDePecas(DecisaoAnalise d) => string.Equals(d.Tipo, Tipo, StringComparison.OrdinalIgnoreCase);

    /// <summary>As opções da decisão com o arquivo e a miniatura de cada uma (lê e calcula o sha256 de cada PDF: chame fora da thread da janela).</summary>
    public static IReadOnlyList<PecaCandidata> Candidatas(DecisaoAnalise d, string pastaProcesso) =>
        d.Opcoes.Select(o =>
        {
            var arquivo = ArquivoNoProcesso(pastaProcesso, o.Valor);
            return new PecaCandidata(o.Valor, o.Texto, arquivo, arquivo is null ? null : Miniatura(pastaProcesso, arquivo));
        }).ToList();

    /// <summary>O caminho completo do PDF da opção (relativo = à pasta do processo), só se existir dentro do processo e fora dos controles (<c>.pmvni-ai</c>).</summary>
    public static string? ArquivoNoProcesso(string pastaProcesso, string valor)
    {
        try
        {
            var raiz = Path.GetFullPath(pastaProcesso).TrimEnd(Path.DirectorySeparatorChar);
            var c = Path.GetFullPath(Path.IsPathRooted(valor) ? valor : Path.Combine(raiz, valor));
            if (!c.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            if (Path.GetRelativePath(raiz, c).Split(Path.DirectorySeparatorChar).Contains(".pmvni-ai", StringComparer.OrdinalIgnoreCase)) return null;
            return File.Exists(c) ? c : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    /// <summary>A primeira página já renderizada pelo preparo do kit: <c>.pmvni-ai\cache-pdf\v1\&lt;motor&gt;\&lt;sha256&gt;\paginas\&lt;sha[:12]&gt;_p001_&lt;dpi&gt;dpi.png</c>.
    /// Com mais de um motor ou resolução, a de menor resolução (é só uma miniatura). null = sem cache para este arquivo.</summary>
    public static string? Miniatura(string pastaProcesso, string pdf)
    {
        try
        {
            var cache = Path.Combine(pastaProcesso, ".pmvni-ai", "cache-pdf", "v1");
            if (!Directory.Exists(cache)) return null;
            string sha;
            using (var f = new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                sha = Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
            return Directory.EnumerateDirectories(cache)
                .Select(motor => Path.Combine(motor, sha, "paginas"))
                .Where(Directory.Exists)
                .SelectMany(p => Directory.EnumerateFiles(p, sha[..12] + "_p001_*dpi.png"))
                .OrderBy(Dpi).ThenBy(x => x, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    static int Dpi(string png)
    {
        var nome = Path.GetFileNameWithoutExtension(png);
        var i = nome.LastIndexOf('_');
        return i >= 0 && nome.EndsWith("dpi", StringComparison.OrdinalIgnoreCase) && int.TryParse(nome[(i + 1)..^3], out var dpi) ? dpi : int.MaxValue;
    }
}
