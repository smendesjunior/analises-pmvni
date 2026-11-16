using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using NetTopologySuite.Geometries;

namespace Pmvni.Imoveis;

/// <summary>Uma feição lida: o número do registro (0-based, a ordem do .dbf), a geometria (null = forma nula) e os atributos como texto.</summary>
public sealed record Feicao(int Indice, Geometry? Geometria, IReadOnlyDictionary<string, string?> Atributos)
{
    public string? Atributo(string nome) => Atributos.TryGetValue(nome, out var v) ? v : null;
}

/// <summary>Leitor somente leitura de shapefile (.shp + .dbf + .prj + .cpg): pontos, linhas e polígonos, com Z/M ignorados. Coordenadas
/// geográficas (o .prj começa com GEOGCS) são convertidas para SIRGAS 2000 / UTM 24S, o sistema da base municipal. Nada é gravado.</summary>
public static class Shapefile
{
    internal static readonly GeometryFactory Fabrica = new(new PrecisionModel(), 31984);

    static Shapefile() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static IReadOnlyList<Feicao> Ler(string caminhoShp, CancellationToken ct = default)
    {
        var dbf = Path.ChangeExtension(caminhoShp, ".dbf");
        var atributos = File.Exists(dbf) ? LerDbf(dbf, Codificacao(caminhoShp)) : [];
        var geografico = EhGeografico(caminhoShp);
        var geometrias = LerShp(caminhoShp, geografico, ct);
        var n = Math.Max(geometrias.Count, atributos.Count);
        var r = new List<Feicao>(n);
        for (var i = 0; i < n; i++)
            r.Add(new Feicao(i, i < geometrias.Count ? geometrias[i] : null,
                i < atributos.Count ? atributos[i] : new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)));
        return r;
    }

    /// <summary>O .prj diz que as coordenadas são graus (GEOGCS no topo, sem PROJCS).</summary>
    public static bool EhGeografico(string caminhoShp)
    {
        var prj = Path.ChangeExtension(caminhoShp, ".prj");
        if (!File.Exists(prj)) return false;
        return File.ReadAllText(prj).TrimStart().StartsWith("GEOGCS", StringComparison.OrdinalIgnoreCase);
    }

    static Encoding Codificacao(string caminhoShp)
    {
        var cpg = Path.ChangeExtension(caminhoShp, ".cpg");
        if (File.Exists(cpg))
        {
            var nome = File.ReadAllText(cpg).Trim();
            if (nome.Contains("UTF", StringComparison.OrdinalIgnoreCase) && nome.Contains('8')) return new UTF8Encoding(false);
            if (int.TryParse(nome, NumberStyles.Integer, CultureInfo.InvariantCulture, out var cp))
                try { return Encoding.GetEncoding(cp); } catch (ArgumentException) { } catch (NotSupportedException) { }
            if (nome.StartsWith("ISO", StringComparison.OrdinalIgnoreCase)) return Encoding.Latin1;
        }
        return Encoding.GetEncoding(1252); // sem .cpg: o padrão do ArcGIS/QGIS no Windows em português
    }

    // ---------- .dbf ----------

    internal static List<Dictionary<string, string?>> LerDbf(string caminho, Encoding cod)
    {
        var b = File.ReadAllBytes(caminho);
        var n = BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(4));
        var tamCab = BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(8));
        var tamReg = BinaryPrimitives.ReadInt16LittleEndian(b.AsSpan(10));
        var campos = new List<(string Nome, char Tipo, int Tamanho)>();
        for (var p = 32; p + 32 <= tamCab && b[p] != 0x0D; p += 32)
        {
            var nome = Encoding.Latin1.GetString(b, p, 11).Split('\0')[0].Trim(); // Latin-1: a base tem campo com acento ("Perímetro", na AUC)
            campos.Add((nome, (char)b[p + 11], b[p + 16]));
        }
        var r = new List<Dictionary<string, string?>>(n);
        for (var i = 0; i < n; i++)
        {
            var o = tamCab + i * tamReg;
            if (o + tamReg > b.Length) break;
            var d = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var p = o + 1; // o primeiro byte marca registro excluído
            foreach (var (nome, tipo, tam) in campos)
            {
                var s = cod.GetString(b, p, tam).Trim().TrimEnd('\0').Trim();
                p += tam;
                if (tipo is 'N' or 'F' && s.Length > 0 && s.All(c => c is '*' or ' ')) s = "";
                d[nome] = s.Length == 0 ? null : s;
            }
            r.Add(d);
        }
        return r;
    }

    // ---------- .shp ----------

    static List<Geometry?> LerShp(string caminho, bool geografico, CancellationToken ct)
    {
        var b = File.ReadAllBytes(caminho);
        var r = new List<Geometry?>();
        var p = 100;
        while (p + 8 <= b.Length)
        {
            ct.ThrowIfCancellationRequested();
            var tam = BinaryPrimitives.ReadInt32BigEndian(b.AsSpan(p + 4)) * 2;
            var conteudo = b.AsSpan(p + 8, Math.Max(0, Math.Min(tam, b.Length - p - 8)));
            p += 8 + tam;
            try { r.Add(Forma(conteudo, geografico)); }
            catch (Exception e) when (e is ArgumentException or IndexOutOfRangeException) { r.Add(null); }
        }
        return r;
    }

    static double D(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadDoubleLittleEndian(s[o..]);
    static int I(ReadOnlySpan<byte> s, int o) => BinaryPrimitives.ReadInt32LittleEndian(s[o..]);
    static Coordinate C(double x, double y, bool geo) => geo ? Utm.DeGraus(y, x) : new Coordinate(x, y);

    static Geometry? Forma(ReadOnlySpan<byte> s, bool geo)
    {
        if (s.Length < 4) return null;
        var tipo = I(s, 0);
        switch (tipo)
        {
            case 1 or 11 or 21:
                return Fabrica.CreatePoint(C(D(s, 4), D(s, 12), geo));
            case 8 or 18 or 28:
            {
                var n = I(s, 36);
                var pts = new Point[n];
                for (var i = 0; i < n; i++) pts[i] = Fabrica.CreatePoint(C(D(s, 40 + i * 16), D(s, 48 + i * 16), geo));
                return Fabrica.CreateMultiPoint(pts);
            }
            case 3 or 13 or 23 or 5 or 15 or 25:
            {
                var partes = I(s, 36);
                var n = I(s, 40);
                var inicios = new int[partes];
                for (var i = 0; i < partes; i++) inicios[i] = I(s, 44 + i * 4);
                var bp = 44 + partes * 4;
                var aneis = new List<Coordinate[]>(partes);
                for (var k = 0; k < partes; k++)
                {
                    var fim = k + 1 < partes ? inicios[k + 1] : n;
                    var c = new Coordinate[Math.Max(0, fim - inicios[k])];
                    for (var i = inicios[k]; i < fim; i++) c[i - inicios[k]] = C(D(s, bp + i * 16), D(s, bp + 8 + i * 16), geo);
                    aneis.Add(c);
                }
                return tipo is 3 or 13 or 23 ? Linhas(aneis) : Poligonos(aneis);
            }
            default: return null;
        }
    }

    static Geometry Linhas(List<Coordinate[]> partes)
    {
        var l = partes.Where(c => c.Length >= 2).Select(c => Fabrica.CreateLineString(c)).ToArray();
        return l.Length == 1 ? l[0] : Fabrica.CreateMultiLineString(l);
    }

    /// <summary>Anéis no sentido horário são cascas; anti-horários são furos da casca que os contém (regra do formato).</summary>
    static Geometry Poligonos(List<Coordinate[]> aneis)
    {
        var cascas = new List<(LinearRing Anel, List<LinearRing> Furos)>();
        var furos = new List<LinearRing>();
        foreach (var c in aneis)
        {
            if (c.Length < 3) continue;
            Coordinate[] fechado = c[0].Equals2D(c[^1]) ? c : [.. c, c[0].Copy()];
            if (fechado.Length < 4) continue;
            var anel = Fabrica.CreateLinearRing(fechado);
            if (NetTopologySuite.Algorithm.Orientation.IsCCW(anel.CoordinateSequence)) furos.Add(anel);
            else cascas.Add((anel, []));
        }
        if (cascas.Count == 0) { cascas.AddRange(furos.Select(f => (f, new List<LinearRing>()))); furos.Clear(); }
        foreach (var f in furos)
        {
            var dono = cascas.FirstOrDefault(c => Fabrica.CreatePolygon(c.Anel).Contains(Fabrica.CreatePoint(f.Coordinate)));
            if (dono.Anel is not null) dono.Furos.Add(f);
            else cascas.Add((f, []));
        }
        var pols = cascas.Select(c => Fabrica.CreatePolygon(c.Anel, c.Furos.ToArray())).ToArray();
        return pols.Length == 1 ? pols[0] : Fabrica.CreateMultiPolygon(pols);
    }
}

/// <summary>Graus (WGS 84 ≈ SIRGAS 2000) para UTM fuso 24 Sul (meridiano central -39°), pela série usual (erro milimétrico no fuso).</summary>
public static class Utm
{
    public static Coordinate DeGraus(double lat, double lon, double meridiano = -39)
    {
        const double a = 6378137.0, f = 1 / 298.257222101, k0 = 0.9996;
        var e2 = f * (2 - f);
        var ep2 = e2 / (1 - e2);
        var fi = lat * Math.PI / 180;
        var dl = (lon - meridiano) * Math.PI / 180;
        var n = a / Math.Sqrt(1 - e2 * Math.Sin(fi) * Math.Sin(fi));
        var t = Math.Tan(fi) * Math.Tan(fi);
        var c = ep2 * Math.Cos(fi) * Math.Cos(fi);
        var aa = Math.Cos(fi) * dl;
        var m = a * ((1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256) * fi
            - (3 * e2 / 8 + 3 * e2 * e2 / 32 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(2 * fi)
            + (15 * e2 * e2 / 256 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(4 * fi)
            - (35 * e2 * e2 * e2 / 3072) * Math.Sin(6 * fi));
        var x = k0 * n * (aa + (1 - t + c) * Math.Pow(aa, 3) / 6 + (5 - 18 * t + t * t + 72 * c - 58 * ep2) * Math.Pow(aa, 5) / 120) + 500000;
        var y = k0 * (m + n * Math.Tan(fi) * (aa * aa / 2 + (5 - t + 9 * c + 4 * c * c) * Math.Pow(aa, 4) / 24
            + (61 - 58 * t + t * t + 600 * c - 330 * ep2) * Math.Pow(aa, 6) / 720)) + 10000000;
        return new Coordinate(x, y);
    }
}
