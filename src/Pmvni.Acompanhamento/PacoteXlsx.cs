using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Pmvni.Acompanhamento;

/// <summary>Acesso às partes do .xlsx (um ZIP): pasta de trabalho, abas, textos compartilhados e estilos. Usado pela leitura e pela gravação.</summary>
internal sealed class PacoteXlsx
{
    internal static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    internal static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    internal static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";
    internal static readonly XNamespace X14 = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    internal static readonly XNamespace XM = "http://schemas.microsoft.com/office/excel/2006/main";

    readonly ZipArchive zip;
    public IReadOnlyList<(string Nome, string Caminho)> Abas { get; }
    public IReadOnlyList<string> Compartilhados { get; }
    readonly XDocument? estilos;
    readonly List<int> formatoDoEstilo = [];
    readonly Dictionary<int, string> formatos = [];

    public PacoteXlsx(ZipArchive zip)
    {
        this.zip = zip;
        var wb = Ler("xl/workbook.xml") ?? throw new InvalidDataException("O arquivo não é uma planilha do Excel (.xlsx): falta xl/workbook.xml.");
        var rels = Relacoes("xl/workbook.xml");
        Abas = wb.Root!.Element(S + "sheets")?.Elements(S + "sheet")
            .Select(s => ((string?)s.Attribute("name") ?? "", rels.TryGetValue((string?)s.Attribute(R + "id") ?? "", out var alvo) ? alvo.Caminho : ""))
            .Where(a => a.Item2.Length > 0).ToList() ?? [];
        if (Abas.Count == 0) throw new InvalidDataException("A planilha não tem nenhuma aba.");

        var sst = Ler("xl/sharedStrings.xml");
        Compartilhados = sst?.Root!.Elements(S + "si").Select(TextoRico).ToList() ?? [];

        estilos = Ler("xl/styles.xml");
        if (estilos is not null)
        {
            foreach (var f in estilos.Root!.Element(S + "numFmts")?.Elements(S + "numFmt") ?? [])
                formatos[(int?)f.Attribute("numFmtId") ?? -1] = (string?)f.Attribute("formatCode") ?? "";
            foreach (var x in estilos.Root!.Element(S + "cellXfs")?.Elements(S + "xf") ?? [])
                formatoDoEstilo.Add((int?)x.Attribute("numFmtId") ?? 0);
        }
    }

    internal static string TextoRico(XElement si) =>
        si.Element(S + "t") is { } t ? t.Value : string.Concat(si.Elements(S + "r").Select(r => r.Element(S + "t")?.Value ?? ""));

    public XDocument? Ler(string caminho)
    {
        var e = zip.GetEntry(caminho);
        if (e is null) return null;
        using var s = e.Open();
        return XDocument.Load(s, LoadOptions.PreserveWhitespace);
    }

    public bool Existe(string caminho) => zip.GetEntry(caminho) is not null;

    public void Gravar(string caminho, XDocument doc)
    {
        zip.GetEntry(caminho)?.Delete();
        var e = zip.CreateEntry(caminho, CompressionLevel.Optimal);
        using var s = e.Open();
        doc.Save(s, SaveOptions.DisableFormatting);
    }

    public void Apagar(string caminho) => zip.GetEntry(caminho)?.Delete();

    /// <summary>As relações de uma parte: id → (tipo, caminho absoluto no pacote).</summary>
    public Dictionary<string, (string Tipo, string Caminho)> Relacoes(string parte)
    {
        var dir = parte.Contains('/') ? parte[..parte.LastIndexOf('/')] : "";
        var nomeRels = (dir.Length > 0 ? dir + "/" : "") + "_rels/" + parte[(parte.LastIndexOf('/') + 1)..] + ".rels";
        var doc = Ler(nomeRels);
        var r = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var rel in doc?.Root!.Elements(PR + "Relationship") ?? [])
        {
            var alvo = (string?)rel.Attribute("Target") ?? "";
            if (((string?)rel.Attribute("TargetMode")) == "External") continue;
            r[(string?)rel.Attribute("Id") ?? ""] = ((string?)rel.Attribute("Type") ?? "", Resolver(dir, alvo));
        }
        return r;
    }

    internal static string NomeRels(string parte)
    {
        var dir = parte.Contains('/') ? parte[..parte.LastIndexOf('/')] : "";
        return (dir.Length > 0 ? dir + "/" : "") + "_rels/" + parte[(parte.LastIndexOf('/') + 1)..] + ".rels";
    }

    static string Resolver(string dir, string alvo)
    {
        if (alvo.StartsWith('/')) return alvo[1..];
        var partes = (dir.Length > 0 ? dir.Split('/') : []).ToList();
        foreach (var p in alvo.Split('/'))
        {
            if (p == "..") { if (partes.Count > 0) partes.RemoveAt(partes.Count - 1); }
            else if (p != ".") partes.Add(p);
        }
        return string.Join('/', partes);
    }

    /// <summary>O estilo da célula mostra data? (formatos 14–22 e 45–47, ou um formato próprio com d/m/a fora das aspas).</summary>
    public bool EstiloDeData(int estilo)
    {
        if (estilo < 0 || estilo >= formatoDoEstilo.Count) return false;
        var id = formatoDoEstilo[estilo];
        if (id is >= 14 and <= 22 or >= 45 and <= 47) return true;
        if (!formatos.TryGetValue(id, out var codigo)) return false;
        var limpo = Regex.Replace(codigo, "\"[^\"]*\"|\\\\.|\\[[^\\]]*\\]", "");
        return limpo.IndexOfAny(['d', 'm', 'y', 'D', 'M', 'Y']) >= 0;
    }

    /// <summary>As cores dos estilos diferenciais (dxf), usados pela formatação condicional.</summary>
    public Cor? CorDxf(int id)
    {
        var dxf = estilos?.Root!.Element(S + "dxfs")?.Elements(S + "dxf").ElementAtOrDefault(id);
        if (dxf is null) return null;
        var texto = Rgb(dxf.Element(S + "font")?.Element(S + "color"));
        var pf = dxf.Element(S + "fill")?.Element(S + "patternFill");
        var fundo = Rgb(pf?.Element(S + "bgColor")) ?? Rgb(pf?.Element(S + "fgColor"));
        return texto is null && fundo is null ? null : new Cor(fundo, texto);
    }

    static string? Rgb(XElement? cor)
    {
        var v = (string?)cor?.Attribute("rgb");
        if (v is null || v.Length < 6) return null;
        var hex = v[^6..];
        // "00000000" é o "sem cor" que o Excel grava em fgColor de dxf.
        if (v.Length == 8 && v.StartsWith("00", StringComparison.Ordinal) && hex == "000000") return null;
        return "#" + hex.ToUpperInvariant();
    }

    /// <summary>O valor da célula como (texto, número, fórmula).</summary>
    public Celula Valor(XElement c)
    {
        var t = (string?)c.Attribute("t");
        var v = c.Element(S + "v")?.Value;
        var f = c.Element(S + "f");
        string? formula = f is null ? null : f.Value.Length > 0 ? f.Value : "(compartilhada)";
        switch (t)
        {
            case "s":
                return new Celula(int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < Compartilhados.Count ? Compartilhados[i] : "", null, formula);
            case "inlineStr":
                return new Celula(c.Element(S + "is") is { } isEl ? TextoRico(isEl) : "", null, formula);
            case "str":
            case "e":
                return new Celula(v ?? "", null, formula);
            case "b":
                return new Celula(v == "1" ? "VERDADEIRO" : "FALSO", null, formula);
            default:
                if (v is null || v.Length == 0) return new Celula("", null, formula);
                return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? new Celula("", n, formula) : new Celula(v, null, formula);
        }
    }
}
