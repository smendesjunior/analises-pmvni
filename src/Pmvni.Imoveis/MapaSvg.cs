using System.Globalization;
using System.Text;
using NetTopologySuite.Geometries;

namespace Pmvni.Imoveis;

/// <summary>O mapa temático da localização (SVG, 16:10): terreno creme, zonas em tons pastel, lotes vizinhos, edificações em cinza escuro,
/// ruas brancas, cursos d'água em azul com a faixa de APP translúcida, o lote em vermelho, confrontantes numerados, norte e escala.</summary>
public static class MapaSvg
{
    public const int Largura = 640, Altura = 400;
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static readonly string[] Pasteis = ["#CFE3F4", "#D8ECD3", "#F6E3C6", "#E6D9F1", "#F3D5DC", "#D3EEEA", "#F1EDC6", "#E2E2E2"];

    public static string CorDaZona(string zona)
    {
        var h = 0;
        foreach (var c in zona) h = unchecked(h * 31 + c);
        return Pasteis[(h & 0x7FFFFFFF) % Pasteis.Length];
    }

    public static string Gerar(BaseQgis b, AnaliseImovel a)
    {
        if (a.Lote is not { } lote) return "";
        // Enquadramento: o lote com folga (no mínimo 60 m de cada lado), na proporção 16:10.
        var e = lote.EnvelopeInternal.Copy();
        var folga = Math.Max(60, Math.Max(e.Width, e.Height) * 0.9);
        e.ExpandBy(folga);
        var cx = e.Centre.X;
        var cy = e.Centre.Y;
        var w = Math.Max(e.Width, e.Height * Largura / Altura);
        var h = w * Altura / Largura;
        var vista = new Envelope(cx - w / 2, cx + w / 2, cy - h / 2, cy + h / 2);
        var escala = Largura / w;
        var recorte = Shapefile.Fabrica.ToGeometry(vista);

        string X(double x) => ((x - vista.MinX) * escala).ToString("0.#", Inv);
        string Y(double y) => ((vista.MaxY - y) * escala).ToString("0.#", Inv);
        string Caminho(Geometry g)
        {
            var sb = new StringBuilder();
            void Anel(Coordinate[] cs, bool fecha)
            {
                for (var i = 0; i < cs.Length; i++) sb.Append(i == 0 ? 'M' : 'L').Append(X(cs[i].X)).Append(',').Append(Y(cs[i].Y));
                if (fecha) sb.Append('Z');
            }
            for (var i = 0; i < g.NumGeometries; i++)
                switch (g.GetGeometryN(i))
                {
                    case Polygon p:
                        Anel(p.ExteriorRing.Coordinates, true);
                        foreach (var furo in p.InteriorRings) Anel(furo.Coordinates, true);
                        break;
                    case LineString l: Anel(l.Coordinates, false); break;
                }
            return sb.ToString();
        }
        Geometry Cortar(Geometry g) => g.EnvelopeInternal.Intersects(vista) ? (vista.Contains(g.EnvelopeInternal) ? g : g.Intersection(recorte)) : Shapefile.Fabrica.CreateGeometryCollection();

        var s = new StringBuilder();
        s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {Largura} {Altura}\" class=\"mapa\" role=\"img\" aria-label=\"Mapa de localização do lote\">");
        s.Append($"<rect width=\"{Largura}\" height=\"{Altura}\" fill=\"#F5F0E4\"/>");

        // Zonas
        var rotulosZona = new List<(string Nome, Coordinate C)>();
        if (b[Camada.Zoneamento] is { } zon)
            foreach (var z in zon.Perto(vista))
            {
                var g = Cortar(z.Geometria!);
                if (g.IsEmpty) continue;
                var nome = z.Atributo("ZONA") ?? "";
                s.Append($"<path d=\"{Caminho(g)}\" fill=\"{CorDaZona(nome)}\" stroke=\"#FFFFFF\" stroke-width=\"1.2\" fill-rule=\"evenodd\"/>");
                if (g.Area * escala * escala > 2500 && nome.Length > 0) rotulosZona.Add((nome, g.InteriorPoint.Coordinate));
            }
        // Faixa de APP (buffer do eixo pelo valor do atributo APP)
        if (b[Camada.HidroApp] is { } hid)
            foreach (var f in hid.Perto(vista))
                if (AnaliseImovel.Numero(f.Atributo("APP")) is { } faixa and > 0 and <= 200)
                {
                    var g = Cortar(f.Geometria!.Buffer(faixa));
                    if (!g.IsEmpty) s.Append($"<path d=\"{Caminho(g)}\" fill=\"#3B82C4\" fill-opacity=\".16\" stroke=\"none\"/>");
                }
        // Lotes vizinhos
        if (b[Camada.Lotes] is { } lotes)
            foreach (var f in lotes.Perto(vista))
            {
                var g = Cortar(f.Geometria!);
                if (!g.IsEmpty) s.Append($"<path d=\"{Caminho(g)}\" fill=\"none\" stroke=\"#A69D8B\" stroke-width=\".8\"/>");
            }
        // Edificações
        if (b[Camada.Edificacoes] is { } ed)
            foreach (var f in ed.Perto(vista))
            {
                var g = Cortar(f.Geometria!);
                if (!g.IsEmpty) s.Append($"<path d=\"{Caminho(g)}\" fill=\"#55524D\" fill-opacity=\".85\" stroke=\"none\"/>");
            }
        // Ruas (eixos): contorno cinza e miolo branco; nome no trecho mais longo
        var nomesRua = new Dictionary<string, (double Comp, LineString L)>();
        if (b[Camada.Logradouros] is { } ruas)
        {
            var linhas = ruas.Perto(vista).Select(f => (F: f, G: Cortar(f.Geometria!))).Where(x => !x.G.IsEmpty).ToList();
            foreach (var (_, g) in linhas) s.Append($"<path d=\"{Caminho(g)}\" fill=\"none\" stroke=\"#C9C2B4\" stroke-width=\"9\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
            foreach (var (f, g) in linhas)
            {
                s.Append($"<path d=\"{Caminho(g)}\" fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"7\" stroke-linecap=\"round\" stroke-linejoin=\"round\"/>");
                var nome = f.Atributo("NomeLogra");
                if (string.IsNullOrWhiteSpace(nome)) continue;
                for (var i = 0; i < g.NumGeometries; i++)
                    if (g.GetGeometryN(i) is LineString ls && ls.Length > nomesRua.GetValueOrDefault(nome).Comp) nomesRua[nome] = (ls.Length, ls);
            }
        }
        // Cursos d'água
        if (b[Camada.HidroApp] is { } hid2)
            foreach (var f in hid2.Perto(vista))
            {
                var g = Cortar(f.Geometria!);
                if (!g.IsEmpty) s.Append($"<path d=\"{Caminho(g)}\" fill=\"none\" stroke=\"#2F7FC1\" stroke-width=\"2\"/>");
            }
        // Lote
        s.Append($"<path d=\"{Caminho(lote)}\" fill=\"#D92D20\" fill-opacity=\".18\" stroke=\"#C0261B\" stroke-width=\"2.6\" stroke-linejoin=\"round\" fill-rule=\"evenodd\"/>");
        // Rótulos (por cima)
        foreach (var (nome, c) in rotulosZona)
            s.Append($"<text x=\"{X(c.X)}\" y=\"{Y(c.Y)}\" class=\"zona\">{Html.E(nome)}</text>");
        foreach (var (nome, (_, ls)) in nomesRua)
        {
            var meio = ls.Interpolar(0.5);
            var (p1, p2) = (ls.Interpolar(0.45), ls.Interpolar(0.55));
            var ang = Math.Atan2(-(p2.Y - p1.Y), p2.X - p1.X) * 180 / Math.PI;
            if (ang > 90) ang -= 180;
            if (ang < -90) ang += 180;
            s.Append($"<text x=\"{X(meio.X)}\" y=\"{Y(meio.Y)}\" class=\"rua\" transform=\"rotate({ang.ToString("0", Inv)} {X(meio.X)} {Y(meio.Y)})\">{Html.E(nome)}</text>");
        }
        foreach (var c in a.Confrontantes)
        {
            var p = c.Geometria.Intersection(recorte) is { IsEmpty: false } gi ? gi.InteriorPoint.Coordinate : c.Geometria.InteriorPoint.Coordinate;
            s.Append($"<circle cx=\"{X(p.X)}\" cy=\"{Y(p.Y)}\" r=\"8\" fill=\"#142D47\"/><text x=\"{X(p.X)}\" y=\"{Y(p.Y)}\" class=\"num\">{c.Numero}</text>");
        }
        // Norte e escala
        s.Append($"<g transform=\"translate({Largura - 30},30)\"><circle r=\"15\" fill=\"#FFFFFF\" stroke=\"#B9B2A3\"/><path d=\"M0,-11 L6,7 L0,3 L-6,7 Z\" fill=\"#C0261B\"/><text y=\"-17\" class=\"norte\">N</text></g>");
        var metros = Arredondar(w / 5);
        var px = metros * escala;
        s.Append($"<g transform=\"translate(14,{Altura - 18})\"><rect x=\"-6\" y=\"-16\" width=\"{(px + 60).ToString("0", Inv)}\" height=\"26\" rx=\"4\" fill=\"#FFFFFF\" fill-opacity=\".9\"/>");
        s.Append($"<rect width=\"{(px / 2).ToString("0.#", Inv)}\" height=\"5\" fill=\"#161E28\"/><rect x=\"{(px / 2).ToString("0.#", Inv)}\" width=\"{(px / 2).ToString("0.#", Inv)}\" height=\"5\" fill=\"#9AA3AE\"/>");
        s.Append($"<text x=\"{(px + 6).ToString("0", Inv)}\" y=\"5\" class=\"escala\">{metros.ToString("0", Inv)} m</text></g>");
        s.Append("</svg>");
        return s.ToString();
    }

    static double Arredondar(double v)
    {
        foreach (var c in new double[] { 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 }) if (c >= v * 0.8) return c;
        return Math.Round(v, -3);
    }

    static Coordinate Interpolar(this LineString l, double fracao) =>
        new NetTopologySuite.LinearReferencing.LengthIndexedLine(l).ExtractPoint(l.Length * fracao);
}
