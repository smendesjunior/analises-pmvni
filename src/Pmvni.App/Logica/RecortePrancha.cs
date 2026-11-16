namespace Pmvni.App;

/// <summary>"Inserir região da prancha" sem tela (testável): a pasta do processo do relatório, os PDFs de prancha dela e a etiqueta
/// sugerida a partir do nome do arquivo.</summary>
public static class RecortePrancha
{
    /// <summary>A pasta do processo: a do <c>.rpmvni</c> (o relatório fica na pasta do processo), ou a de cima se ele está em <c>.pmvni-ai</c>.</summary>
    public static string PastaDoProcesso(string pmvrel)
    {
        var d = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(pmvrel))!;
        for (var x = d; x is not null; x = System.IO.Path.GetDirectoryName(x))
            if (System.IO.Path.GetFileName(x).StartsWith(".pmvni-ai", StringComparison.OrdinalIgnoreCase)) return System.IO.Path.GetDirectoryName(x)!;
        // Relatórios também podem ser salvos em RELATORIOS ou numa subpasta da revisão.
        for (var x = d; x is not null; x = System.IO.Path.GetDirectoryName(x))
            if (Directory.Exists(System.IO.Path.Combine(x, "PROJETO")) && Directory.Exists(System.IO.Path.Combine(x, "DOCUMENTOS"))) return x;
        return d;
    }

    public static IReadOnlyList<string> PdfsDoProcesso(string pmvrel)
    {
        var pasta = PastaDoProcesso(pmvrel);
        var sep = System.IO.Path.DirectorySeparatorChar;
        try
        {
            // PROJETO (e PROJETOS/PRANCHAS) em qualquer profundidade: INICIAL, REVISÃO NN, DERIVADOS e subpastas. Depois DOCUMENTOS e o
            // resto da pasta do processo (até dois níveis). Nunca .pmvni-ai nem as pastas de despacho (são saídas, não o projeto).
            var todos = Directory.EnumerateFiles(pasta, "*.pdf", new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 8, IgnoreInaccessible = true })
                .Where(f =>
                {
                    var rel = System.IO.Path.GetRelativePath(pasta, f);
                    var partes = rel.Split(sep);
                    if (partes.Any(x => x.StartsWith(".pmvni-ai", StringComparison.OrdinalIgnoreCase) || x.StartsWith("DESPACHO", StringComparison.OrdinalIgnoreCase))) return false;
                    return Grupo(rel) < 2 || partes.Length <= 3;
                })
                .ToList();
            return todos.OrderBy(f => Grupo(System.IO.Path.GetRelativePath(pasta, f)))
                .ThenByDescending(f => Revisao(System.IO.Path.GetRelativePath(pasta, f)))
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    /// <summary>0 = projeto (PROJETO…, PRANCHA…, ou arquivo ARQ…), 1 = DOCUMENTOS, 2 = o resto.</summary>
    static int Grupo(string relativo)
    {
        var primeira = relativo.Split(System.IO.Path.DirectorySeparatorChar)[0];
        if (primeira.StartsWith("PROJETO", StringComparison.OrdinalIgnoreCase) || primeira.StartsWith("PRANCHA", StringComparison.OrdinalIgnoreCase)
            || System.IO.Path.GetFileName(relativo).StartsWith("ARQ", StringComparison.OrdinalIgnoreCase)) return 0;
        return primeira.StartsWith("DOCUMENTO", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
    }

    /// <summary>Ordem da apresentação: REVISÃO NN = NN, CORREÇÃO… = 0,5 (avulsa), INICIAL = 0; a mais recente aparece primeiro.</summary>
    static double Revisao(string relativo)
    {
        var m = System.Text.RegularExpressions.Regex.Match(relativo, @"REVIS[ÃA]O\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success && int.TryParse(m.Groups[1].Value, out var numero)) return numero;
        return relativo.Contains("INICIAL", StringComparison.OrdinalIgnoreCase) ? 0 : .5;
    }

    /// <summary>"ARQ 01-03.pdf" → "ARQ 01/03"; outro nome → o nome sem extensão.</summary>
    public static string SugerirPrancha(string caminho)
    {
        var nome = System.IO.Path.GetFileNameWithoutExtension(caminho).Trim();
        var m = System.Text.RegularExpressions.Regex.Match(nome, @"^([A-Za-z]{2,5})\s*[-_ ]?\s*(\d{1,3})\s*[-_/]\s*(\d{1,3})");
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() + " " + m.Groups[2].Value.PadLeft(2, '0') + "/" + m.Groups[3].Value.PadLeft(2, '0') : nome;
    }

}

/// <summary>Conversão do retângulo mostrado na tela para a página (DIPs) e para o PNG a 200 dpi.
/// As escalas dos dois eixos incluem o arredondamento da renderização da página.</summary>
public sealed record RecortePdf(double X, double Y, double Largura, double Altura, uint PixelsLargura, uint PixelsAltura)
{
    public static RecortePdf DeTela(double x0, double y0, double x1, double y1,
        double telaLargura, double telaAltura, double paginaLargura, double paginaAltura, double dpi = 200)
    {
        if (new[] { x0, y0, x1, y1, telaLargura, telaAltura, paginaLargura, paginaAltura, dpi }.Any(x => !double.IsFinite(x))
            || telaLargura <= 0 || telaAltura <= 0 || paginaLargura <= 0 || paginaAltura <= 0 || dpi <= 0)
            throw new ArgumentException("As medidas da página são inválidas.");
        var a = Math.Clamp(Math.Min(x0, x1), 0, telaLargura) / telaLargura * paginaLargura;
        var b = Math.Clamp(Math.Min(y0, y1), 0, telaAltura) / telaAltura * paginaAltura;
        var c = Math.Clamp(Math.Max(x0, x1), 0, telaLargura) / telaLargura * paginaLargura;
        var d = Math.Clamp(Math.Max(y0, y1), 0, telaAltura) / telaAltura * paginaAltura;
        var w = Math.Round((c - a) * dpi / 96);
        var h = Math.Round((d - b) * dpi / 96);
        if (w < 1 || h < 1) throw new ArgumentException("Selecione uma região maior da prancha.");
        if (w > 32767 || h > 32767 || w * h > 100_000_000)
            throw new ArgumentException("A região é muito grande. Recorte uma parte menor da prancha.");
        return new(a, b, c - a, d - b, (uint)w, (uint)h);
    }
}
