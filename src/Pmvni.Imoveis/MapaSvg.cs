using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NetTopologySuite.Geometries;

namespace Pmvni.Imoveis;

/// <summary>Medida de texto sem biblioteca gráfica: os avanços da Segoe UI (normal e negrito, a fonte que o script do kit usa), em
/// milésimos do corpo, para os caracteres de 32 a 255; e a altura da tinta estimada pela forma das letras. Serve para o mesmo jogo de
/// encaixe de rótulos do script (o PIL mede com a fonte real; aqui a diferença fica em poucos décimos de pixel).</summary>
internal static class Letras
{
    static readonly short[] Normal =
    [
        274,284,392,591,539,818,800,230,302,302,417,684,217,400,217,390,539,539,539,539,539,539,539,539,539,539,217,217,684,684,684,448,955,645,573,619,701,506,488,686,710,266,357,580,471,898,748,754,560,754,598,531,524,687,621,934,590,553,570,302,379,302,684,415,268,509,588,462,589,523,313,589,566,242,242,497,242,861,566,586,588,589,348,424,339,566,479,723,459,484,452,302,239,302,684,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,274,284,539,539,556,539,239,448,414,890,392,506,684,400,890,415,377,684,366,366,282,577,458,217,205,351,431,506,906,931,952,448,645,645,645,645,645,645,860,619,506,506,506,506,266,266,266,266,701,748,754,754,754,754,754,684,754,687,687,687,687,553,560,544,509,509,509,509,509,509,832,462,523,523,523,523,242,242,242,242,559,566,586,586,586,586,586,684,586,566,566,566,566,484,588,484,
    ];
    static readonly short[] Negrito =
    [
        276,327,493,592,575,867,850,293,369,369,455,707,271,404,271,443,575,575,575,575,575,575,575,575,575,575,271,271,707,707,707,438,954,703,641,624,737,532,520,711,766,317,445,649,511,957,790,758,614,758,653,561,586,723,667,1005,655,607,607,369,436,369,707,415,314,538,620,480,619,541,383,619,602,284,284,559,284,916,605,611,620,619,398,440,389,605,542,797,552,538,479,369,326,369,707,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,276,327,575,575,556,575,326,485,462,874,410,581,707,404,874,415,380,707,404,404,303,613,509,271,215,394,456,581,952,965,979,438,703,703,703,703,703,703,935,624,532,532,532,532,317,317,317,317,737,790,758,758,758,758,758,707,758,723,723,723,723,607,614,628,538,538,538,538,538,538,828,480,541,541,541,541,284,284,284,284,593,605,611,611,611,611,611,707,611,605,605,605,605,538,620,538,
    ];

    /// <summary>A subida da fonte (da origem do texto até a linha de base), como o PIL posiciona: 1,079 do corpo na Segoe UI.</summary>
    public const double Subida = 1.0791;

    public static double Largura(string t, double tam, bool negrito)
    {
        var tab = negrito ? Negrito : Normal;
        var s = 0;
        foreach (var c in t) s += c >= 32 && c <= 255 && tab[c - 32] > 0 ? tab[c - 32] : c is '—' ? 1000 : 560;
        return s * tam / 1000;
    }

    /// <summary>Largura e tinta acima e abaixo da linha de base (a caixa que o <c>textbbox</c> do PIL devolveria, aproximada).</summary>
    public static (double Largura, double Cima, double Baixo) Medir(string t, double tam, bool negrito)
    {
        double cima = 0, baixo = 0;
        foreach (var c in t)
        {
            cima = Math.Max(cima, c switch
            {
                ' ' => 0, '.' or ',' or '_' => .12, '-' or '·' or '—' or '–' => .34, ':' or ';' => .49,
                '(' or ')' or '[' or ']' or '/' or '|' => .77,
                'Ç' => .70, 'ç' => .50,
                >= 'À' and <= 'Ý' => .90,
                >= 'à' and <= 'ÿ' => .74,
                'b' or 'd' or 'f' or 'h' or 'k' or 'l' or 'i' or 'j' => .74,
                't' => .64,
                >= 'a' and <= 'z' => .50,
                _ => .70,
            });
            baixo = Math.Max(baixo, c switch
            {
                'g' or 'j' or 'p' or 'q' or 'y' or 'ç' or 'Ç' => .20,
                '(' or ')' or '[' or ']' or '|' => .17,
                ',' or ';' or 'Q' or '/' => .13,
                _ => 0,
            });
        }
        return (Largura(t, tam, negrito), cima * tam, baixo * tam);
    }
}

/// <summary>O desenho do mapa de localização (o <c>desenhar()</c> do <c>mapa_localizacao.py</c>), em SVG. As medidas são as do script,
/// que desenha no dobro do tamanho (1680 × 1050) e reduz para 840 × 525: o SVG usa essa mesma grade no <c>viewBox</c>. Paleta inspirada no
/// Apple Maps (modo claro), com cores firmes para o papel. Ordem das camadas: zonas, edificações, faixas da BR-262, ruas, APP e cursos
/// d'água, lotes, quadras, rótulos de zona, lote em vermelho com as suas edificações, números dos lotes, nomes de ruas e do rio, cota até a
/// calha, confrontantes, etiqueta do lote, escala, balão e norte.</summary>
public static class MapaSvg
{
    const int S = 2;
    const string Fundo = "#F0ECE4", RuaBorda = "#D6CFC3", Rua = "#FFFFFF", Rodovia = "#FAD470", RodoviaBorda = "#E2AA3A";
    const string Edif = "#DED8CF", EdifBorda = "#C6BEB3", LoteLinha = "#B2AA9E", QuadraLinha = "#8C8478";
    const string Agua = "#78BAF0", AguaLinha = "#2884DE", AguaTxt = "#1660B0", Dnit = "#F29628", DnitBorda = "#E37400";
    const string Texto = "#2C2C30", Texto2 = "#6E6860", Vermelho = "#E22D28", EdifLote = "#5C5650";
    const string Fontes = "'Segoe UI',Arial,sans-serif";

    static readonly (string Claro, string Escuro)[] ZonasPastel =
    [
        ("#D6CAF2", "#6042AA"), ("#F7CFB4", "#AA521E"), ("#C0E4C8", "#247440"), ("#E2D0EC", "#803C96"),
        ("#F0DEA8", "#8A640C"), ("#F0C4D6", "#9C3060"), ("#DCE6AC", "#606E12"), ("#E0D6C4", "#6A5A40"),
    ];
    static readonly double[] PosicoesNoTrecho = [0.5, 0.32, 0.68, 0.16, 0.84]; // onde tentar o rótulo ao longo de um trecho de rua ou rio
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Tom pastel fixo por zona (mesma zona, mesma cor em todos os mapas, igual ao kit) e tom escuro para o rótulo.</summary>
    public static (string Claro, string Escuro) CorDaZona(string nome)
    {
        var hex = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(nome)))[..6];
        return ZonasPastel[int.Parse(hex, NumberStyles.HexNumber, Inv) % ZonasPastel.Length];
    }

    readonly record struct P(double X, double Y);
    readonly record struct Ret(double X0, double Y0, double X1, double Y1);

    static string N(double v) => Math.Round(v, 1).ToString("0.#", Inv);

    internal static (string Svg, List<string> Ruas, List<ItemLegenda> Legenda) Desenhar(BaseQgis b, Envelope ext, Fe alvo, string modo,
        IReadOnlyList<string> ficha, IReadOnlyList<(Fe Lote, string Nome)> rotulosVizinhos, CursoDagua? rio, string nomeRio, FaixaBr262? dnit)
    {
        const int W = MapaLocalizacao.Largura, H = MapaLocalizacao.Altura;
        double lw = W * S, lh = H * S;
        double sx = lw / ext.Width, sy = lh / ext.Height;
        P T(Coordinate p) => new((p.X - ext.MinX) * sx, (ext.MaxY - p.Y) * sy);
        var s = new StringBuilder();
        s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W * S} {H * S}\" font-family=\"{Fontes}\">");
        s.Append("<defs><filter id=\"sombra\" x=\"-20%\" y=\"-40%\" width=\"140%\" height=\"190%\"><feGaussianBlur stdDeviation=\"20\"/></filter></defs>");
        s.Append($"<rect width=\"{W * S}\" height=\"{H * S}\" fill=\"{Fundo}\"/>");

        // Só o que aparece vai para o arquivo: a geometria é cortada com folga (em metros) além do recorte.
        Geometry Cortar(Geometry g, double folga)
        {
            var e = ext.Copy();
            e.ExpandBy(folga + 30 / sx);
            if (e.Contains(g.EnvelopeInternal)) return g;
            try { return g.Intersection(Shapefile.Fabrica.ToGeometry(e)); }
            catch (TopologyException) { return g; }
        }
        string Caminho(Geometry g, bool soCascas = false)
        {
            var sb = new StringBuilder();
            void Anel(Coordinate[] cs, bool fecha)
            {
                for (var i = 0; i < cs.Length - (fecha ? 1 : 0); i++) { var p = T(cs[i]); sb.Append(i == 0 ? 'M' : 'L').Append(N(p.X)).Append(',').Append(N(p.Y)); }
                if (fecha) sb.Append('Z');
            }
            for (var i = 0; i < g.NumGeometries; i++)
                switch (g.GetGeometryN(i))
                {
                    case Polygon p:
                        Anel(p.ExteriorRing.Coordinates, true);
                        if (!soCascas) foreach (var furo in p.InteriorRings) Anel(furo.Coordinates, true);
                        break;
                    case LineString l: Anel(l.Coordinates, false); break;
                }
            return sb.ToString();
        }
        void Traco(Geometry g, string cor, double largura, bool redondo = false, double folga = 0)
        {
            var d = Caminho(Cortar(g, folga));
            if (d.Length > 0) s.Append($"<path d=\"{d}\" fill=\"none\" stroke=\"{cor}\" stroke-width=\"{N(largura)}\" stroke-linejoin=\"round\"{(redondo ? " stroke-linecap=\"round\"" : "")}/>");
        }

        // zonas (tons suaves sobre o terreno)
        var zonas = MapaLocalizacao.Em(b, Camada.Zoneamento, ext).Where(z => z.Tipo == 'A').ToList();
        foreach (var z in zonas)
        {
            var d = Caminho(Cortar(z.G, 0), soCascas: true);
            if (d.Length > 0) s.Append($"<path d=\"{d}\" fill=\"{CorDaZona(z.Campo("ZONA") is { Length: > 0 } zn ? zn : "?").Claro}\" fill-opacity=\".784\"/>");
        }
        // edificações
        s.Append($"<g fill=\"{Edif}\" stroke=\"{EdifBorda}\" stroke-width=\"1\">");
        foreach (var e in MapaLocalizacao.Em(b, Camada.Edificacoes, ext).Where(e => e.Tipo == 'A'))
            s.Append($"<path d=\"{Caminho(Cortar(e.G, 0))}\"/>");
        s.Append("</g>");

        var ruas = MapaLocalizacao.Em(b, Camada.Logradouros, ext).Where(r => r.Tipo == 'L').ToList();
        // faixa de domínio (DNIT) e faixa não edificável da BR-262, quando aparecem no recorte
        var br = ruas.Where(r => MapaLocalizacao.EhBr262(r.Campo("NomeLogra"))).ToList();
        if (br.Count > 0 && dnit is not null)
        {
            var moldura = Shapefile.Fabrica.ToGeometry(ext);
            foreach (var (metros, veu) in new[] { (dnit.Dominio + dnit.NaoEdificavel, false), (dnit.Dominio, true) })
                try
                {
                    var eixo = Shapefile.Fabrica.BuildGeometry(br.Select(r => Cortar(r.G, metros + 5)).ToList()).Union();
                    var faixa = eixo.Buffer(metros, 8).Intersection(moldura);
                    var d = Caminho(faixa);
                    if (d.Length == 0) continue;
                    if (veu) s.Append($"<path d=\"{d}\" fill=\"{Dnit}\" fill-opacity=\".188\" fill-rule=\"evenodd\"/>");
                    s.Append($"<path d=\"{d}\" fill=\"none\" stroke=\"{DnitBorda}\" stroke-opacity=\".824\" stroke-width=\"6.5\" stroke-linejoin=\"round\"/>");
                }
                catch (TopologyException) { }
        }
        // ruas: borda e miolo; as rodovias por cima, em amarelo
        foreach (var (rod, larg, cor) in new[] { (false, 13 * S, RuaBorda), (false, 10 * S, Rua), (true, 17 * S, RodoviaBorda), (true, 13 * S, Rodovia) })
            foreach (var r in ruas)
                if (MapaLocalizacao.EhRodovia(r.Campo("NomeLogra")) == rod) Traco(r.G, cor, larg);

        // faixa de APP (véu azul) e cursos d'água
        var extHidro = ext.Copy();
        extHidro.ExpandBy(60);
        var hidro = MapaLocalizacao.Em(b, Camada.HidroApp, extHidro).Where(h => h.Tipo == 'L').ToList();
        if (hidro.Count > 0)
        {
            s.Append("<g opacity=\".373\">");
            foreach (var h in hidro)
            {
                var larg = MapaLocalizacao.FaixaApp(h.Campo("APP"));
                if (larg.Count > 0) Traco(h.G, Agua, (int)(2 * larg.Max() * sx), redondo: true, folga: larg.Max());
            }
            s.Append("</g>");
            foreach (var h in hidro)
            {
                Traco(h.G, "#FFFFFF", 7 * S);
                Traco(h.G, AguaLinha, 4 * S);
            }
        }
        // lotes e quadras
        s.Append($"<g fill=\"none\" stroke=\"{LoteLinha}\" stroke-width=\"2\">");
        foreach (var l in MapaLocalizacao.Em(b, Camada.Lotes, ext).Where(l => l.Tipo == 'A')) s.Append($"<path d=\"{Caminho(Cortar(l.G, 0))}\"/>");
        s.Append($"</g><g fill=\"none\" stroke=\"{QuadraLinha}\" stroke-width=\"2\">");
        foreach (var q in MapaLocalizacao.Em(b, Camada.Quadras, ext).Where(q => q.Tipo == 'A')) s.Append($"<path d=\"{Caminho(Cortar(q.G, 0))}\"/>");
        s.Append("</g>");

        // ── rótulos: cada um só entra se couber inteiro na imagem e não cobrir outro já posto ──
        var colocadas = new List<P[]>();
        bool Etiqueta(P c, string texto, double tam, bool negrito, string corTxt, string? fundo, List<P[]> postas, double ang, string? borda = null, double alfaFundo = 1)
        {
            var (w, cima, baixo) = Letras.Medir(texto, tam, negrito);
            var h = cima + baixo;
            var (px, py) = fundo is not null ? ((int)(h * .65), (int)(h * .45)) : ((int)(h * .45), (int)(h * .22)); // com fundo: pílula; só halo: caixa justa
            double tw = w + 2 * px + 4, th = h + 2 * py + 4;
            var poli = CaixaGirada(c, tw, th, ang);
            if (poli.Any(p => !(2 <= p.X && p.X <= lw - 2 && 2 <= p.Y && p.Y <= lh - 2))) return false;
            if (postas.Any(o => Sobrepoe(poli, o))) return false;
            var giro = ang != 0 ? $" transform=\"rotate({N(-ang)} {N(c.X)} {N(c.Y)})\"" : "";
            var peso = negrito ? " font-weight=\"700\"" : "";
            var linhaBase = c.Y + (cima - baixo) / 2;
            if (fundo is null)
                s.Append($"<text x=\"{N(c.X)}\" y=\"{N(linhaBase)}\" font-size=\"{N(tam)}\"{peso} text-anchor=\"middle\" fill=\"{corTxt}\" stroke=\"#FFFFFF\" stroke-opacity=\".92\" stroke-width=\"{2 * Math.Max(3, (int)h / 5)}\" stroke-linejoin=\"round\" paint-order=\"stroke\"{giro}>{Html.E(texto)}</text>");
            else
            {
                s.Append($"<g{giro}><rect x=\"{N(c.X - tw / 2 + 2)}\" y=\"{N(c.Y - th / 2 + 2)}\" width=\"{N(w + 2 * px - 1)}\" height=\"{N(h + 2 * py - 1)}\" rx=\"{N((h + 2 * py) / 2)}\" fill=\"{fundo}\"{(alfaFundo < 1 ? $" fill-opacity=\"{N2(alfaFundo)}\"" : "")}{(borda is null ? "" : $" stroke=\"{borda}\" stroke-width=\"2\"")}/>");
                s.Append($"<text x=\"{N(c.X)}\" y=\"{N(linhaBase)}\" font-size=\"{N(tam)}\"{peso} text-anchor=\"middle\" fill=\"{corTxt}\">{Html.E(texto)}</text></g>");
            }
            postas.Add(poli);
            return true;
        }
        static double Angulo(P a, P c)
        {
            var ang = Math.Atan2(-(c.Y - a.Y), c.X - a.X) * 180 / Math.PI;
            if (ang > 90) ang -= 180;
            if (ang < -90) ang += 180;
            return ang;
        }
        bool NoRecorte(Coordinate p) => ext.MinX < p.X && p.X < ext.MaxX && ext.MinY < p.Y && p.Y < ext.MaxY;

        // zona: etiqueta discreta
        foreach (var z in zonas)
        {
            var nome = z.Campo("ZONA");
            var c = MapaLocalizacao.Centro(z.Partes);
            if (!NoRecorte(c))
            {
                var pts = z.Partes.SelectMany(a => a).Where(NoRecorte).ToList();
                if (pts.Count == 0) continue;
                c = new Coordinate(pts.Average(p => p.X), pts.Average(p => p.Y));
            }
            if (nome.Length > 0) Etiqueta(T(c), nome, 18 * S, true, CorDaZona(nome).Escuro, null, colocadas, 0);
        }
        var nZonas = colocadas.Count; // daqui em diante: nomes de ruas, do rio e cotas (a ficha e a escala evitam cobri-los)

        // lote do projeto
        var contorno = Caminho(alvo.G);
        s.Append($"<path d=\"{Caminho(alvo.G, soCascas: true)}\" fill=\"{Vermelho}\" fill-opacity=\".216\"/>");
        s.Append($"<path d=\"{contorno}\" fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"{9 * S / 2 + 4}\" stroke-linejoin=\"round\"/>");
        s.Append($"<path d=\"{contorno}\" fill=\"none\" stroke=\"{Vermelho}\" stroke-width=\"{(modo == "VERIFIED_LOT" ? 4 * S : 3 * S)}\" stroke-linejoin=\"round\"/>");
        var edifLote = MapaLocalizacao.EdificacoesDoLote(b, alvo);
        foreach (var e in edifLote)
            s.Append($"<path d=\"{Caminho(e.G)}\" fill=\"{EdifLote}\" stroke=\"#FFFFFF\" stroke-width=\"{2 * S}\" stroke-linejoin=\"round\"/>");

        // números dos lotes (só em volta do lote do projeto)
        var bbl = alvo.Bb;
        var perto = bbl.Copy();
        perto.ExpandBy(Math.Max(35.0, (bbl.Width + bbl.Height) / 2));
        foreach (var n in MapaLocalizacao.Em(b, Camada.NumerosLote, perto))
        {
            var p = n.Partes[0][0];
            var t = n.Campo("LOTE");
            if (t.Length == 0 || MapaLocalizacao.Dentro(p, alvo.Partes)) continue; // o do projeto vai na etiqueta vermelha
            var q = T(p);
            var (w, cima, baixo) = Letras.Medir(t, 16 * S, false);
            var h = cima + baixo;
            if (q.X - w < 4 || q.Y - h < 4 || q.X + w > lw - 4 || q.Y + h > lh - 4) continue;
            s.Append($"<text x=\"{N(q.X)}\" y=\"{N(q.Y + (cima - baixo) / 2)}\" font-size=\"{16 * S}\" text-anchor=\"middle\" fill=\"{Texto2}\" stroke=\"{Fundo}\" stroke-width=\"{2 * S}\" stroke-linejoin=\"round\" paint-order=\"stroke\">{Html.E(t)}</text>");
        }
        var centroLote = T(MapaLocalizacao.Centro(alvo.Partes));
        var tLote = ficha.Count > 0 ? ficha[0].Split(" · ")[0] : "";
        if (ficha.Count > 0) // o lugar da etiqueta "LOTE nº" (desenhada por último) fica reservado
        {
            var (w, cima, baixo) = Letras.Medir(tLote, 19 * S, true);
            colocadas.Add(CaixaGirada(centroLote, w + 30 * S, cima + baixo + 20 * S, 0));
        }
        const double margem = 0.08;
        var vis = new Envelope(ext.MinX + ext.Width * margem, ext.MaxX - ext.Width * margem, ext.MinY + ext.Height * margem, ext.MaxY - ext.Height * margem);
        bool Visivel(Coordinate m) => vis.MinX < m.X && m.X < vis.MaxX && vis.MinY < m.Y && m.Y < vis.MaxY;

        // nomes das ruas e do curso d'água. Ordem: ruas junto do lote (testada), curso d'água, demais ruas pela distância ao lote
        var feitos = new HashSet<string>(StringComparer.Ordinal);
        var trechos = new List<(double Comp, double Desvio, string Nome, Coordinate A, Coordinate B, Coordinate M)>();
        var distRua = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var r in ruas)
        {
            var nome = r.Campo("NomeLogra", "NOME", "LOGRADOURO");
            if (nome.Length == 0) continue;
            distRua[nome] = Math.Min(distRua.GetValueOrDefault(nome, 1e9), MapaLocalizacao.DistanciaLinhaLote(r.Partes, alvo.Partes).Dist);
            foreach (var linha in r.Partes)
                for (var i = 0; i + 1 < linha.Length; i++)
                {
                    var (a, c) = (linha[i], linha[i + 1]);
                    var comp = a.Distance(c);
                    if (comp < ext.Width * 0.06) continue;
                    foreach (var t in PosicoesNoTrecho)
                    {
                        var m = new Coordinate(a.X + (c.X - a.X) * t, a.Y + (c.Y - a.Y) * t);
                        if (Visivel(m)) trechos.Add((comp, Math.Abs(t - .5), nome, a, c, m));
                    }
                }
        }
        var nomesRuas = new List<string>();
        var ordenados = trechos.OrderBy(t => Math.Round(distRua.GetValueOrDefault(t.Nome, 0) / 25)).ThenBy(t => -t.Comp).ThenBy(t => t.Desvio).ToList();
        void RotularRuas(Func<string, bool> aceita)
        {
            // letra menor e, por fim, nome abreviado
            foreach (var (tam, nomear) in new (double, Func<string, string>)[] { (19 * S, MapaLocalizacao.NomeRua), (16 * S, MapaLocalizacao.NomeRua), (16 * S, MapaLocalizacao.NomeAbreviado) })
                foreach (var (_, _, nome, a, c, m) in ordenados)
                {
                    if (feitos.Contains(nome) || !aceita(nome)) continue;
                    if (Etiqueta(T(m), nomear(nome), tam, true, Texto, null, colocadas, Angulo(T(a), T(c)))) { feitos.Add(nome); nomesRuas.Add(nome); }
                }
        }
        RotularRuas(n => distRua.GetValueOrDefault(n, 1e9) <= 15); // testada do lote primeiro
        if (rio is not null && hidro.FirstOrDefault(x => x.Indice == rio.Feicao) is { } hr)
        {
            var seg = new List<(double Comp, double Desvio, Coordinate A, Coordinate B, Coordinate M)>();
            foreach (var linha in hr.Partes)
                for (var i = 0; i + 1 < linha.Length; i++)
                    foreach (var t in PosicoesNoTrecho)
                    {
                        var (a, c) = (linha[i], linha[i + 1]);
                        var m = new Coordinate(a.X + (c.X - a.X) * t, a.Y + (c.Y - a.Y) * t);
                        if (Visivel(m)) seg.Add((a.Distance(c), Math.Abs(t - .5), a, c, m));
                    }
            var candidatos = seg.OrderBy(t => -t.Comp).ThenBy(t => t.Desvio).Take(40).ToList();
            var txt = nomeRio.Length > 0 ? nomeRio : "Curso d'água (sem nome na base)";
            var feito = false;
            foreach (var tam in new[] { 18 * S, 15 * S })
            {
                foreach (var (_, _, a, c, m) in candidatos)
                    if (Etiqueta(T(m), txt, tam, true, AguaTxt, null, colocadas, Angulo(T(a), T(c)))) { feito = true; break; }
                if (feito) break;
            }
            if (!feito) // não coube ao longo da linha: na horizontal, encostado à linha
            {
                var meia = Letras.Largura(txt, 15 * S, true) / 2 + 14 * S;
                foreach (var (_, _, _, _, m) in candidatos)
                {
                    var p = T(m);
                    if (new[] { 1, -1 }.Any(k => Etiqueta(new P(p.X + k * meia, p.Y), txt, 15 * S, true, AguaTxt, null, colocadas, 0))) break;
                }
            }
        }
        RotularRuas(_ => true);
        // cota do lote até a calha, quando o curso d'água não toca o lote
        if (rio is { PontoLote: { } pl, PontoCalha: { } pcalha, Distancia: > 0 } && T(pcalha) is var pb && pb.X >= 0 && pb.X <= lw && pb.Y >= 0 && pb.Y <= lh)
        {
            var pa = T(pl);
            var comp = Math.Sqrt((pb.X - pa.X) * (pb.X - pa.X) + (pb.Y - pa.Y) * (pb.Y - pa.Y));
            if (comp == 0) comp = 1;
            double nx = -(pb.Y - pa.Y) / comp, ny = (pb.X - pa.X) / comp, tk = 9 * S;
            const string cor = "#1E3A8A";
            s.Append($"<path d=\"M{N(pa.X)},{N(pa.Y)}L{N(pb.X)},{N(pb.Y)}\" stroke=\"#FFFFFF\" stroke-width=\"{6 * S}\"/>");
            s.Append($"<path d=\"M{N(pa.X)},{N(pa.Y)}L{N(pb.X)},{N(pb.Y)}");
            foreach (var p in new[] { pa, pb }) s.Append($"M{N(p.X - nx * tk)},{N(p.Y - ny * tk)}L{N(p.X + nx * tk)},{N(p.Y + ny * tk)}");
            s.Append($"\" stroke=\"{cor}\" stroke-width=\"{2 * S}\" fill=\"none\"/>");
            var ang = Angulo(pa, pb);
            var txt = $"{MapaLocalizacao.Num(rio.Distancia, 1)} m";
            foreach (var k in new[] { 1, -1, 2, -2 }) // ao lado da linha, acompanhando a direção dela
                if (Etiqueta(new P((pa.X + pb.X) / 2 + nx * 17 * S * k, (pa.Y + pb.Y) / 2 + ny * 17 * S * k), txt, 16 * S, true, cor, "#FFFFFF", colocadas, ang, borda: "#BFDBFE", alfaFundo: .94)) break;
        }
        // proprietários dos lotes confrontantes (nome abreviado, com halo branco)
        foreach (var (l, nome) in rotulosVizinhos)
        {
            var num = MapaLocalizacao.Em(b, Camada.NumerosLote, l.Bb).FirstOrDefault(n => MapaLocalizacao.Dentro(n.Partes[0][0], l.Partes));
            var p = T(num is not null ? num.Partes[0][0] : MapaLocalizacao.Centro(l.Partes));
            var y = p.Y + (num is not null ? 17 * S : 0);
            var w = Letras.Largura(nome, 15 * S, false);
            if (p.X - w / 2 < 4 || p.X + w / 2 > lw - 4 || y < 4 || y + 20 * S > lh) continue;
            var (_, cima, _) = Letras.Medir(nome, 15 * S, false);
            s.Append($"<text x=\"{N(p.X)}\" y=\"{N(y + cima)}\" font-size=\"{15 * S}\" text-anchor=\"middle\" fill=\"{Texto}\" stroke=\"#FFFFFF\" stroke-width=\"{2 * (3 * S / 2 + 1)}\" stroke-linejoin=\"round\" paint-order=\"stroke\">{Html.E(nome)}</text>");
        }
        // etiqueta do número do lote do projeto
        if (ficha.Count > 0) Etiqueta(centroLote, tLote, 19 * S, true, "#FFFFFF", Vermelho, [], 0);

        // escala gráfica e balão do lote (no canto livre mais próximo), com a linha de chamada até a borda do lote
        var ptsLote = alvo.Partes.SelectMany(a => a).Select(T).ToList();
        var cl = new Ret(ptsLote.Min(p => p.X), ptsLote.Min(p => p.Y), ptsLote.Max(p => p.X), ptsLote.Max(p => p.Y));
        double m_ = 16 * S;
        var rosa = new Ret(lw - 110 * S, 0, lw, 110 * S);
        Ret[] evitar = [rosa, new Ret(cl.X0 - m_, cl.Y0 - m_, cl.X1 + m_, cl.Y1 + m_)];
        var nomes = colocadas.Skip(nZonas).Select(Envolvente).ToList();
        var painel = PainelEscala(s, ext, sx, lw, lh, evitar, nomes);
        if (ficha.Count > 0) Balao(s, ficha, centroLote, cl, ptsLote, lw, lh, [painel], nomes);
        Norte(s, lw - 50 * S, 50 * S);
        s.Append("</svg>");

        var legenda = new List<ItemLegenda> { new("lote", "Lote do projeto") };
        if (edifLote.Count > 0) legenda.Add(new("edif", "Edificação no lote"));
        if (hidro.Count > 0) { legenda.Add(new("app", "Faixa de APP")); legenda.Add(new("rio", "Curso d'água")); }
        if (br.Count > 0 && dnit is not null) legenda.Add(new("dnit", "Faixas da BR-262"));
        legenda.Add(new("zona", "Zonas do PDM"));
        return (s.ToString(), nomesRuas, legenda);
    }

    static string N2(double v) => v.ToString("0.###", Inv);

    /// <summary>Cantos do retângulo w × h centrado em c, girado ang graus (sentido anti-horário, como o rotate do PIL).</summary>
    static P[] CaixaGirada(P c, double w, double h, double ang)
    {
        var a = ang * Math.PI / 180;
        double co = Math.Cos(a), si = Math.Sin(a);
        return new[] { (-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2) }.Select(q => new P(c.X + q.Item1 * co + q.Item2 * si, c.Y - q.Item1 * si + q.Item2 * co)).ToArray();
    }

    /// <summary>Dois polígonos convexos se sobrepõem? (teorema do eixo separador)</summary>
    static bool Sobrepoe(P[] p, P[] q)
    {
        foreach (var poli in new[] { p, q })
            for (var i = 0; i < poli.Length; i++)
            {
                var (a, c) = (poli[i], poli[(i + 1) % poli.Length]);
                double nx = c.Y - a.Y, ny = a.X - c.X;
                var pa = p.Select(v => nx * v.X + ny * v.Y).ToList();
                var pq = q.Select(v => nx * v.X + ny * v.Y).ToList();
                if (pa.Max() < pq.Min() || pq.Max() < pa.Min()) return false;
            }
        return true;
    }

    static Ret Envolvente(P[] p) => new(p.Min(v => v.X), p.Min(v => v.Y), p.Max(v => v.X), p.Max(v => v.Y));

    static double Sobre(double x, double y, double larg, double alt, Ret r) =>
        Math.Max(0, Math.Min(x + larg, r.X1) - Math.Max(x, r.X0)) * Math.Max(0, Math.Min(y + alt, r.Y1) - Math.Max(y, r.Y0));

    /// <summary>A escala gráfica num painel compacto, no canto que menos cobre o lote (a legenda fica no relatório, não na imagem).</summary>
    static Ret PainelEscala(StringBuilder s, Envelope ext, double sx, double lw, double lh, Ret[] evitar, List<Ret> suaves)
    {
        int[] passos = [10, 20, 25, 50, 100, 200, 250, 500, 1000];
        var passo = passos.MinBy(v => Math.Abs(2 * v - ext.Width * 0.09));
        var total = passo * 2;
        var px = total * sx;
        var tEsc = $"{total} m";
        double pad = 10 * S, lhh = 21 * S, m = 10 * S;
        var (wt, cima, baixo) = Letras.Medir(tEsc, 12 * S, true);
        var larg = (int)(px + 8 * S + wt) + 2 * pad;
        var alt = lhh + 2 * pad - 5 * S;
        (double X, double Y)[] cantos = [(m, lh - alt - m), (lw - larg - m, lh - alt - m), (m, m), (lw - larg - 112 * S, m)];
        var (x0, y0) = cantos.OrderBy(c => evitar.Sum(r => Sobre(c.X, c.Y, larg, alt, r))).ThenBy(c => suaves.Sum(r => Sobre(c.X, c.Y, larg, alt, r))).First();
        s.Append($"<rect x=\"{N(x0)}\" y=\"{N(y0)}\" width=\"{N(larg)}\" height=\"{N(alt)}\" rx=\"{9 * S}\" fill=\"#FFFFFF\" stroke=\"#E2DED6\" stroke-width=\"{S}\"/>");
        var y = y0 + pad + (lhh - 5 * S) / 2;
        var x = x0 + pad;
        s.Append($"<rect x=\"{N(x)}\" y=\"{N(y - 3 * S)}\" width=\"{N(px / 2)}\" height=\"{6 * S}\" fill=\"#3C3C43\"/><rect x=\"{N(x + px / 2)}\" y=\"{N(y - 3 * S)}\" width=\"{N(px / 2)}\" height=\"{6 * S}\" fill=\"#AEAEB2\"/>");
        s.Append($"<text x=\"{N(x + px + 8 * S)}\" y=\"{N(y + (cima - baixo) / 2)}\" font-size=\"{12 * S}\" font-weight=\"700\" fill=\"{Texto}\">{Html.E(tEsc)}</text>");
        return new Ret(x0, y0, x0 + larg, y0 + alt);
    }

    /// <summary>O balão branco com os dados do lote: fica onde não cobre o lote, a escala nem a rosa dos ventos; depois, onde não cobre
    /// nomes de ruas e do rio; por fim, o mais perto do lote. A linha de chamada termina na borda do lote, com o ponto vermelho.</summary>
    static void Balao(StringBuilder s, IReadOnlyList<string> linhas, P alvoPx, Ret caixaLote, List<P> contorno, double lw, double lh, Ret[] evitar, List<Ret> suaves)
    {
        double f1 = 19 * S, f2 = 15 * S;
        var larg = linhas.Select((t, i) => Letras.Largura(t, i == 0 ? f1 : f2, i == 0)).Max() + 52 * S;
        double alt = 26 * S + 24 * S * linhas.Count, m = 16 * S;
        var proibidos = new List<Ret> { new(lw - 110 * S, 0, lw, 110 * S) };
        proibidos.AddRange(evitar);
        proibidos.Add(new Ret(caixaLote.X0 - m, caixaLote.Y0 - m, caixaLote.X1 + m, caixaLote.Y1 + m));
        var xs = new List<int>();
        for (var x = (int)m; x < (int)(lw - larg - m); x += 20 * S) xs.Add(x);
        if (xs.Count == 0) xs.Add((int)m);
        var ys = new SortedSet<int> { (int)m, (int)((lh - alt) / 2), (int)(lh - alt - m) };
        var (x0, y0) = ys.SelectMany(y => xs.Select(x => (X: (double)x, Y: (double)y)))
            .OrderBy(c => proibidos.Sum(r => Sobre(c.X, c.Y, larg, alt, r)))
            .ThenBy(c => suaves.Sum(r => Sobre(c.X, c.Y, larg, alt, r)) > 0)
            .ThenBy(c => Dist(c.X + larg / 2 - alvoPx.X, c.Y + alt / 2 - alvoPx.Y)).First();

        P[] ancoras = [new(x0, y0 + alt / 2), new(x0 + larg, y0 + alt / 2), new(x0 + larg / 2, y0), new(x0 + larg / 2, y0 + alt)];
        var a = ancoras.MinBy(p => Dist(p.X - alvoPx.X, p.Y - alvoPx.Y));
        var fim = contorno.Count > 0 ? contorno.MinBy(q => Dist(q.X - a.X, q.Y - a.Y)) : alvoPx; // na borda do lote, no ponto mais próximo do balão
        s.Append($"<path d=\"M{N(a.X)},{N(a.Y)}L{N(fim.X)},{N(fim.Y)}\" stroke=\"#FFFFFF\" stroke-width=\"{7 * S}\"/>");
        s.Append($"<path d=\"M{N(a.X)},{N(a.Y)}L{N(fim.X)},{N(fim.Y)}\" stroke=\"{Vermelho}\" stroke-width=\"{3 * S}\"/>");
        s.Append($"<circle cx=\"{N(fim.X)}\" cy=\"{N(fim.Y)}\" r=\"{8 * S - 1.5 * S}\" fill=\"{Vermelho}\" stroke=\"#FFFFFF\" stroke-width=\"{3 * S}\"/>");
        s.Append($"<rect x=\"{N(x0)}\" y=\"{N(y0 + 6 * S)}\" width=\"{N(larg)}\" height=\"{N(alt)}\" rx=\"{16 * S}\" fill=\"#000000\" fill-opacity=\".18\" filter=\"url(#sombra)\"/>");
        s.Append($"<rect x=\"{N(x0)}\" y=\"{N(y0)}\" width=\"{N(larg)}\" height=\"{N(alt)}\" rx=\"{16 * S}\" fill=\"#FFFFFF\" stroke=\"#E8E4DC\" stroke-width=\"{S}\"/>");
        s.Append($"<circle cx=\"{N(x0 + 18 * S)}\" cy=\"{N(y0 + 27 * S)}\" r=\"{6 * S}\" fill=\"{Vermelho}\"/>");
        var y = y0 + 15 * S;
        for (var i = 0; i < linhas.Count; i++)
        {
            var t = linhas[i];
            var cor = i == 0 ? "#1C1C1E" : t.StartsWith("APP ATINGE", StringComparison.Ordinal) ? Vermelho : "#636366";
            var tam = i == 0 ? f1 : f2;
            s.Append($"<text x=\"{N(x0 + 32 * S)}\" y=\"{N(y + Letras.Subida * tam)}\" font-size=\"{N(tam)}\"{(i == 0 ? " font-weight=\"700\"" : "")} fill=\"{cor}\">{Html.E(t)}</text>");
            y += 24 * S;
        }
    }

    static double Dist(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    /// <summary>A rosa dos ventos: disco branco com a ponta vermelha para o norte (o mapa é sempre norte para cima).</summary>
    static void Norte(StringBuilder s, double x, double y)
    {
        s.Append($"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"{30 * S}\" fill=\"#FFFFFF\" stroke=\"#E8E4DC\" stroke-width=\"{S}\"/>");
        s.Append($"<path d=\"M{N(x)},{N(y - 22 * S)}L{N(x - 7 * S)},{N(y - 2 * S)}L{N(x + 7 * S)},{N(y - 2 * S)}Z\" fill=\"{Vermelho}\"/>");
        s.Append($"<path d=\"M{N(x)},{N(y + 18 * S)}L{N(x - 7 * S)},{N(y - 2 * S)}L{N(x + 7 * S)},{N(y - 2 * S)}Z\" fill=\"#AEAEB2\"/>");
    }
}
