namespace Pmvni.App;

/// <summary>"Inserir região da prancha" sem tela (testável): a pasta do processo do relatório, os PDFs de prancha dela e a etiqueta
/// sugerida a partir do nome do arquivo.</summary>
public static class RecortePrancha
{
    /// <summary>A pasta do processo: a do <c>.pmvrel</c> (o relatório fica na pasta do processo), ou a de cima se ele está em <c>.pmvni-ai</c>.</summary>
    public static string PastaDoProcesso(string pmvrel)
    {
        var d = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(pmvrel))!;
        for (var x = d; x is not null; x = System.IO.Path.GetDirectoryName(x))
            if (System.IO.Path.GetFileName(x).StartsWith(".pmvni-ai", StringComparison.OrdinalIgnoreCase)) return System.IO.Path.GetDirectoryName(x)!;
        return d;
    }

    public static IReadOnlyList<string> PdfsDoProcesso(string pmvrel)
    {
        var pasta = PastaDoProcesso(pmvrel);
        var lista = new List<string>();
        try
        {
            // Primeiro as pastas de projeto (PROJETO, PRANCHAS, ARQUITETONICO…), depois o resto, até dois níveis.
            var todos = Directory.EnumerateFiles(pasta, "*.pdf", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2, IgnoreInaccessible = true })
                .Where(f => !f.Contains(System.IO.Path.DirectorySeparatorChar + ".pmvni-ai", StringComparison.OrdinalIgnoreCase)
                            && !System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(f)!).StartsWith("DESPACHO", StringComparison.OrdinalIgnoreCase))
                .ToList();
            static bool DeProjeto(string f) => f.Contains("PROJETO", StringComparison.OrdinalIgnoreCase) || f.Contains("PRANCHA", StringComparison.OrdinalIgnoreCase)
                                               || System.IO.Path.GetFileName(f).StartsWith("ARQ", StringComparison.OrdinalIgnoreCase);
            lista.AddRange(todos.Where(DeProjeto).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            lista.AddRange(todos.Where(f => !DeProjeto(f)).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return lista;
    }

    /// <summary>"ARQ 01-03.pdf" → "ARQ 01/03"; outro nome → o nome sem extensão.</summary>
    public static string SugerirPrancha(string caminho)
    {
        var nome = System.IO.Path.GetFileNameWithoutExtension(caminho).Trim();
        var m = System.Text.RegularExpressions.Regex.Match(nome, @"^([A-Za-z]{2,5})\s*[-_ ]?\s*(\d{1,3})\s*[-_/]\s*(\d{1,3})");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() + " " + m.Groups[2].Value.PadLeft(2, '0') + "/" + m.Groups[3].Value.PadLeft(2, '0') : nome;
    }

}
