using System.Globalization;
using System.Text;
using NetTopologySuite.Geometries;
using P = Pmvni.Imoveis.PontoTela;
using Ret = Pmvni.Imoveis.Caixa;

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
/// que desenha no dobro do tamanho e reduz pela metade: o SVG usa a grade dobrada no <c>viewBox</c>. O kit é 840 × 525 (16:10); aqui o
/// mapa é 4:3 por decisão do usuário (2026-11-17): 840 × 630, grade 1680 × 1260 (<see cref="MapaLocalizacao.Largura"/>). Paleta inspirada no
/// Apple Maps (modo claro), com cores firmes para o papel. Ordem das camadas: zonas, edificações, faixas da BR-262, ruas, APP e cursos
/// d'água, lotes, quadras, rótulos de zona, lote em vermelho com as suas edificações, números dos lotes, nomes de ruas e do rio, cota até a
/// calha, confrontantes, etiqueta do lote, escala e norte.</summary>
public static class MapaSvg
{
    const int S = 2;
    const string Fundo = "#FAFAF8", RuaBorda = "#D6CFC3", Rua = "#FFFFFF", Rodovia = "#FAD470", RodoviaBorda = "#E2AA3A";
    const string Edif = "#DED8CF", EdifBorda = "#C6BEB3", LoteLinha = "#B2AA9E", QuadraLinha = "#8C8478";
    const string Agua = "#78BAF0", AguaLinha = "#2884DE", AguaTxt = "#1660B0", Dnit = "#F29628", DnitBorda = BordaFaixa;
    const string Texto = "#2C2C30", Texto2 = "#6E6860", Vermelho = "#E22D28", EdifLote = "#5C5650";
    const string Fontes = "'Segoe UI',Arial,sans-serif";

    internal const string FundoZona = "#EFF2EE", LimiteZona = "#89988E", RotuloZona = "#506359";
    internal const string BordaFaixa = "#A35B15";
    static readonly double[] PosicoesNoTrecho = [0.5, 0.32, 0.68, 0.16, 0.84, 0.4, 0.6, 0.24, 0.76, 0.08, 0.92]; // onde tentar o rótulo ao longo de um trecho de rua ou rio
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static string N(double v) => Math.Round(v, 1).ToString("0.#", Inv);

    internal static (string Svg, List<string> Ruas, List<ItemLegenda> Legenda) Desenhar(BaseQgis b, Envelope ext, Fe alvo, string modo,
        string tLote, string cabecalhoLote, IReadOnlyList<(string Rotulo, string Valor)> dadosLote, IReadOnlyList<string> observacoes,
        IReadOnlyList<(Fe Lote, string Nome)> rotulosVizinhos, CursoDagua? rio, string nomeRio, FaixaBr262? dnit,
        bool cotasLote = true, bool cotasDnit = true, bool cotasRua = true)
    {
        const int W = MapaLocalizacao.Largura, H = MapaLocalizacao.Altura;
        double lw = W * S, lh = H * S;
        double sx = lw / ext.Width, sy = lh / ext.Height;
        P T(Coordinate p) => new((p.X - ext.MinX) * sx, (ext.MaxY - p.Y) * sy);
        var s = new StringBuilder();
        s.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{W}\" height=\"{H}\" viewBox=\"0 0 {W * S} {H * S}\" font-family=\"{Fontes}\">");
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
                    case GeometryCollection colecao: sb.Append(Caminho(colecao, soCascas)); break;
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
        foreach (var grupo in zonas.GroupBy(z => z.Campo("ZONA")))
        {
            var partes = grupo.Select(z => Cortar(z.G, 0)).Where(g => !g.IsEmpty).ToList();
            if (partes.Count == 0) continue;
            Geometry cobertura;
            try { cobertura = partes.Count == 1 ? partes[0] : Shapefile.Fabrica.BuildGeometry(partes).Union(); }
            catch (TopologyException) { cobertura = Shapefile.Fabrica.BuildGeometry(partes); }
            s.Append($"<path d=\"{Caminho(cobertura)}\" fill=\"{FundoZona}\" fill-rule=\"evenodd\" stroke=\"{LimiteZona}\" stroke-width=\"2.5\" stroke-dasharray=\"9 7\"/>");
        }
        // edificações
        s.Append($"<g fill=\"{Edif}\" stroke=\"{EdifBorda}\" stroke-width=\"1\">");
        foreach (var e in MapaLocalizacao.Em(b, Camada.Edificacoes, ext).Where(e => e.Tipo == 'A'))
            s.Append($"<path d=\"{Caminho(Cortar(e.G, 0))}\"/>");
        s.Append("</g>");

        var ruas = MapaLocalizacao.Em(b, Camada.Logradouros, ext).Where(r => r.Tipo == 'L').ToList();
        // faixa de domínio (DNIT) e faixa não edificável da BR-262, quando aparecem no recorte
        var br = ruas.Where(r => MapaLocalizacao.EhBr262(r.Campo("NomeLogra"))).ToList();
        var limitesDnit = new StringBuilder();
        if (br.Count > 0 && dnit is not null)
        {
            var moldura = Shapefile.Fabrica.ToGeometry(ext);
            foreach (var (metros, veu) in new[] { (dnit.Dominio + dnit.NaoEdificavel, false), (dnit.Dominio, true) })
                try
                {
                    var eixo = Shapefile.Fabrica.BuildGeometry(br.Select(r => Cortar(r.G, metros + 5)).ToList()).Union();
                    var faixa = eixo.Buffer(metros, 8);
                    if (veu) s.Append($"<path d=\"{Caminho(faixa.Intersection(moldura))}\" fill=\"{Dnit}\" fill-opacity=\".09\" fill-rule=\"evenodd\"/>");
                    var limite = Caminho(faixa.Boundary.Intersection(moldura));
                    if (limite.Length > 0)
                        limitesDnit.Append($"<path d=\"{limite}\" fill=\"none\" stroke=\"{DnitBorda}\" stroke-opacity=\".824\" stroke-width=\"3\" stroke-dasharray=\"10 6\" stroke-linejoin=\"round\"/>");
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
            s.Append("<g opacity=\".28\">");
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

        // ── rótulos (2026-11-17): colocação sem sobreposição, por prioridade (veja Rotulagem). Cada rótulo só entra se couber inteiro na
        // imagem sem cobrir outro já posto; senão tenta a próxima posição (ou letra menor, ou nome abreviado) e, por fim, fica oculto. ──
        var zonasSvg = new StringBuilder(); // os nomes das zonas são os últimos a entrar, mas ficam por baixo do lote no desenho
        var marcaZonas = s.Length;
        double m_ = 16 * S;
        var rosa = new Ret(lw - 110 * S, 0, lw, 110 * S);
        (double Larg, double Alt, double Cima, double Baixo) Medida(string texto, double tam, bool negrito, bool comFundo)
        {
            var (w, cima, baixo) = Letras.Medir(texto, tam, negrito);
            var h = cima + baixo;
            var (px, py) = comFundo ? ((int)(h * .65), (int)(h * .45)) : ((int)(h * .45), (int)(h * .22)); // com fundo: pílula; só halo: caixa justa
            return (w + 2 * px + 4, h + 2 * py + 4, cima, baixo);
        }
        bool Etiqueta(QuadroRotulos? q, StringBuilder? saida, P c, string texto, double tam, bool negrito, string corTxt, string? fundo, double ang, string? borda = null, double alfaFundo = 1)
        {
            var (tw, th, cima, baixo) = Medida(texto, tam, negrito, fundo is not null);
            var poli = Rotulagem.CaixaGirada(c, tw, th, ang);
            if (q is not null)
            {
                if (!q.Cabe(poli)) return false;
                q.Ocupar(poli);
            }
            if (saida is null) return true;
            var h = cima + baixo;
            var giro = ang != 0 ? $" transform=\"rotate({N(-ang)} {N(c.X)} {N(c.Y)})\"" : "";
            var peso = negrito ? " font-weight=\"700\"" : "";
            var linhaBase = c.Y + (cima - baixo) / 2;
            if (fundo is null)
                saida.Append($"<text x=\"{N(c.X)}\" y=\"{N(linhaBase)}\" font-size=\"{N(tam)}\"{peso} text-anchor=\"middle\" fill=\"{corTxt}\" stroke=\"#FFFFFF\" stroke-opacity=\".92\" stroke-width=\"{2 * Math.Max(3, (int)h / 5)}\" stroke-linejoin=\"round\" paint-order=\"stroke\"{giro}>{Html.E(texto)}</text>");
            else
            {
                var py = (int)(h * .45);
                saida.Append($"<g{giro}><rect x=\"{N(c.X - tw / 2 + 2)}\" y=\"{N(c.Y - th / 2 + 2)}\" width=\"{N(tw - 4 - 1)}\" height=\"{N(h + 2 * py - 1)}\" rx=\"{N((h + 2 * py) / 2)}\" fill=\"{fundo}\"{(alfaFundo < 1 ? $" fill-opacity=\"{N2(alfaFundo)}\"" : "")}{(borda is null ? "" : $" stroke=\"{borda}\" stroke-width=\"2\"")}/>");
                saida.Append($"<text x=\"{N(c.X)}\" y=\"{N(linhaBase)}\" font-size=\"{N(tam)}\"{peso} text-anchor=\"middle\" fill=\"{corTxt}\">{Html.E(texto)}</text></g>");
            }
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
        Coordinate Mundo(P p) => new(ext.MinX + p.X / sx, ext.MaxY - p.Y / sy);

        // lote do projeto
        var contorno = Caminho(alvo.G);
        s.Append($"<path d=\"{Caminho(alvo.G, soCascas: true)}\" fill=\"{Vermelho}\" fill-opacity=\".10\"/>");
        s.Append($"<path d=\"{contorno}\" fill=\"none\" stroke=\"#FFFFFF\" stroke-width=\"{9 * S / 2 + 4}\" stroke-linejoin=\"round\"/>");
        s.Append($"<path d=\"{contorno}\" fill=\"none\" stroke=\"{Vermelho}\" stroke-width=\"{(modo == "VERIFIED_LOT" ? 4 * S : 3 * S)}\" stroke-linejoin=\"round\"/>");
        var edifLote = MapaLocalizacao.EdificacoesDoLote(b, alvo);
        foreach (var e in edifLote)
            s.Append($"<path d=\"{Caminho(e.G)}\" fill=\"{EdifLote}\" stroke=\"#FFFFFF\" stroke-width=\"{2 * S}\" stroke-linejoin=\"round\"/>");
        s.Append(limitesDnit);

        var ptsLote = alvo.Partes.SelectMany(a => a).Select(T).ToList();
        var cl = new Ret(ptsLote.Min(p => p.X), ptsLote.Min(p => p.Y), ptsLote.Max(p => p.X), ptsLote.Max(p => p.Y));
        var centroLote = T(MapaLocalizacao.Centro(alvo.Partes));

        // 1º (prioridade máxima): a etiqueta vermelha "LOTE nº", fora do lote e encostada nele
        Ret? caixaEtiqueta = null;
        var posLote = centroLote;
        var (wTxtLote, cimaLote, baixoLote) = Letras.Medir(tLote, 19 * S, true);
        double we = wTxtLote + 30 * S, he = cimaLote + baixoLote + 20 * S; // o lugar é escolhido depois das linhas das cotas, para não cobri-las

        const double margem = 0.08;
        var vis = new Envelope(ext.MinX + ext.Width * margem, ext.MaxX - ext.Width * margem, ext.MinY + ext.Height * margem, ext.MaxY - ext.Height * margem);
        bool Visivel(Coordinate m) => vis.MinX < m.X && m.X < vis.MaxX && vis.MinY < m.Y && m.Y < vis.MaxY;

        // nomes das ruas e do curso d'água. Ordem: ruas junto do lote (testada), curso d'água, demais ruas pela distância ao lote
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
        var ordenados = trechos.OrderBy(t => Math.Round(distRua.GetValueOrDefault(t.Nome, 0) / 25)).ThenBy(t => -t.Comp).ThenBy(t => t.Desvio).ToList();
        TrechosRio? hidroRio = null;
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
            hidroRio = new TrechosRio(seg.OrderBy(t => -t.Comp).ThenBy(t => t.Desvio).Take(40).Select(t => (t.A, t.B, t.M)).ToList(),
                nomeRio.Length > 0 ? nomeRio : "Curso d'água (sem nome na base)");
        }

        // As ruas e o rio, num quadro (q). Com saida null é só um ensaio: mostra onde os nomes querem ficar, para a escala os evitar.
        List<string> RotularNomes(QuadroRotulos q, StringBuilder? saida)
        {
            var feitos = new HashSet<string>(StringComparer.Ordinal);
            var nomesRuas = new List<string>();
            void RotularRuas(Func<string, bool> aceita)
            {
                // a posição ao longo da rua; depois letra menor; por fim, o nome abreviado
                foreach (var (tam, nomear) in new (double, Func<string, string>)[] { (19 * S, MapaLocalizacao.NomeRua), (16 * S, MapaLocalizacao.NomeRua), (16 * S, MapaLocalizacao.NomeAbreviado) })
                    foreach (var (_, _, nome, a, c, m) in ordenados)
                    {
                        if (feitos.Contains(nome) || !aceita(nome)) continue;
                        // o nome fica sobre o trecho reto da rua: trecho mais curto que o texto deixaria o nome sobre as quadras
                        P ta = T(a), tc = T(c);
                        if (Dist(tc.X - ta.X, tc.Y - ta.Y) < Letras.Largura(nomear(nome), tam, true) * .95) continue;
                        if (Etiqueta(q, saida, T(m), nomear(nome), tam, true, Texto, null, Angulo(T(a), T(c)))) { feitos.Add(nome); nomesRuas.Add(nome); }
                    }
            }
            RotularRuas(n => distRua.GetValueOrDefault(n, 1e9) <= 15); // testada do lote primeiro
            if (hidroRio is { } hrio)
            {
                var feito = false;
                foreach (var tam in new[] { 18 * S, 15 * S })
                {
                    foreach (var (a, c, m) in hrio.Candidatos)
                        if (Etiqueta(q, saida, T(m), hrio.Texto, tam, true, AguaTxt, null, Angulo(T(a), T(c)))) { feito = true; break; }
                    if (feito) break;
                }
                if (!feito) // não coube ao longo da linha: na horizontal, encostado à linha
                {
                    var meia = Letras.Largura(hrio.Texto, 15 * S, true) / 2 + 14 * S;
                    foreach (var (_, _, m) in hrio.Candidatos)
                    {
                        var p = T(m);
                        if (new[] { 1, -1 }.Any(k => Etiqueta(q, saida, new P(p.X + k * meia, p.Y), hrio.Texto, 15 * S, true, AguaTxt, null, 0))) break;
                    }
                }
            }
            RotularRuas(_ => true);
            return nomesRuas;
        }

        // Cotas da BR-262 (2026-11-17), como o kit: o domínio do DNIT medido do eixo (FaixaDominioBr262, a confirmar com o DNIT para o
        // trecho) e a distância do eixo ao ponto mais próximo do lote (perpendicular ao eixo). A base não tem a faixa desenhada nem a largura
        // como atributo: só o eixo (Logradouro.shp, NomeLogra "BR 262"); a faixa sai do eixo ± a distância. Prioridade logo abaixo da
        // etiqueta do lote. As linhas só são desenhadas no passe final (saida); as etiquetas entram no quadro nos dois.
        // Valores horizontais. A chamada liga o rótulo ao segmento medido quando há afastamento.
        // As cotas dos lados do lote ficam sempre do lado de fora.
        bool Cota(QuadroRotulos q, StringBuilder? saida, P a, P c, string cor, string corTxt, string borda, string texto, string dica, int lado, bool soDeUmLado = false)
        {
            var compPx = Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y));
            if (compPx < 4 * S) return false;
            double ux = (c.X - a.X) / compPx, uy = (c.Y - a.Y) / compPx, nx = -uy, ny = ux, tk = 5 * S;
            foreach (var tamCota in new[] { 12.0 * S, 10.0 * S }) // sem lugar nenhum, letra menor
                if (CotaEm(tamCota)) return true;
            return false;

            bool CotaEm(double tamCota)
            {
                var (tw, th, _, _) = Medida(texto, tamCota, true, true);
                int[] lados = lado == 0 ? [1, -1] : soDeUmLado ? [lado] : [lado, -lado];
                var posicoes = new List<P>();
                var afastamento = Math.Abs(nx) * tw / 2 + Math.Abs(ny) * th / 2 + 3 * S;
                foreach (var extra in new[] { 0, 6, 14, 26, 42, 64, 88 })
                    foreach (var t in new[] { .5, .35, .65, .2, .8, .1, .9 })
                        foreach (var ld in lados)
                        {
                            var d = (afastamento + extra * S) * ld;
                            posicoes.Add(new P(a.X + (c.X - a.X) * t + nx * d, a.Y + (c.Y - a.Y) * t + ny * d));
                        }
                foreach (var centro in posicoes)
                {
                    var t = Math.Clamp(((centro.X-a.X)*ux + (centro.Y-a.Y)*uy) / compPx, 0, 1);
                    var pe = new P(a.X + (c.X-a.X)*t, a.Y + (c.Y-a.Y)*t);
                    var dx = centro.X-pe.X;
                    var dy = centro.Y-pe.Y;
                    var comp = Dist(dx,dy);
                    var razao = Math.Min(tw/2/Math.Max(.001,Math.Abs(dx)), th/2/Math.Max(.001,Math.Abs(dy)));
                    var fim = new P(centro.X-dx*razao,centro.Y-dy*razao);
                    var inicio = new P(pe.X+dx*3*S/comp,pe.Y+dy*3*S/comp);
                    P[]? chamada = null;
                    if (Dist(pe.X-fim.X,pe.Y-fim.Y) > 5*S) {
                        chamada = Rotulagem.CaixaGirada(new P((inicio.X+fim.X)/2,(inicio.Y+fim.Y)/2),
                            Dist(inicio.X-fim.X,inicio.Y-fim.Y), S, Cotas.AnguloLegivel(inicio,fim));
                        if (!q.Cabe(chamada)) continue;
                    }
                    var marca = saida?.Length ?? 0;
                    if (saida is not null)
                    {
                        saida.Append($"<g><title>{Html.E(dica)}</title><path d=\"M{N(a.X)},{N(a.Y)}L{N(c.X)},{N(c.Y)}");
                        foreach (var p in new[] { a, c }) saida.Append($"M{N(p.X - nx * tk)},{N(p.Y - ny * tk)}L{N(p.X + nx * tk)},{N(p.Y + ny * tk)}");
                        saida.Append($"\" stroke=\"{cor}\" stroke-width=\"{S}\" fill=\"none\"/>");
                    }
                    if (Etiqueta(q, saida, centro, texto, tamCota, true, corTxt, "#FFFFFF", 0, borda: borda, alfaFundo: .96))
                    {
                        if (chamada is not null)
                        {
                            q.Ocupar(chamada);
                            saida?.Append($"<path d=\"M{N(pe.X)},{N(pe.Y)}L{N(fim.X)},{N(fim.Y)}\" stroke=\"{cor}\" stroke-width=\"1.5\" fill=\"none\"/>");
                        }
                        saida?.Append("</g>");
                        return true;
                    }
                    saida?.Remove(marca, saida.Length - marca);
                }
                return false;
            }
        }

        // Cotas dos lados do lote do projeto: só com o lote a 100 px ou mais na imagem (200 na grade do desenho), lados juntados (trechos
        // menores que 0,5 m ou quase alinhados), linha 6 px fora do lote, valor do lado de fora.
        var poligonoLote = alvo.G;
        var ladosLote = new List<(P A, P C, string Texto)>();
        if (cotasLote && Math.Max(cl.Largura, cl.Altura) >= 50 * S)
            for (var i = 0; i < poligonoLote.NumGeometries; i++)
                if (poligonoLote.GetGeometryN(i) is Polygon pg)
                    foreach (var lado in Cotas.Lados(pg.ExteriorRing.Coordinates))
                    {
                        var (nxm, nym) = Cotas.NormalParaFora(lado, pg);
                        var off = 6 * S / sx; // em metros
                        var a = T(new Coordinate(lado.A.X + nxm * off, lado.A.Y + nym * off));
                        var c = T(new Coordinate(lado.B.X + nxm * off, lado.B.Y + nym * off));
                        ladosLote.Add((a, c, $"{MapaLocalizacao.Num(lado.Comprimento)} m"));
                    }
        int LadoDeFora(P a, P c)
        {
            // +1 se a normal (−uy, ux) da linha aponta para longe do centro do lote
            double ux = c.X - a.X, uy = c.Y - a.Y, mx = (a.X + c.X) / 2 - centroLote.X, my = (a.Y + c.Y) / 2 - centroLote.Y;
            return -uy * mx + ux * my >= 0 ? 1 : -1;
        }
        void CotasDoLote(QuadroRotulos q, StringBuilder? saida)
        {
            foreach (var (a, c, texto) in ladosLote)
                Cota(q, saida, a, c, Vermelho, "#B0201C", "#F3B4B1", texto, "Lado do lote medido no desenho da base (não substitui o levantamento).", LadoDeFora(a, c), soDeUmLado: true);
        }

        // Cotas da BR-262, como o kit: o domínio do DNIT medido do eixo (FaixaDominioBr262, a confirmar com o DNIT para o trecho) e a
        // distância do eixo ao ponto mais próximo do lote (perpendicular ao eixo). A base não tem a faixa desenhada nem a largura como
        // atributo: só o eixo (Logradouro.shp, NomeLogra "BR 262"); a faixa sai do eixo ± a distância. Só com o ponto do eixo na imagem.
        const string DicaDnit = "Domínio a confirmar no DNIT para o trecho; faixa não edificável: Lei 6.766/1979, art. 4º, III.";
        // as duas cotas paralelas, afastadas 10 px uma da outra: o domínio de um lado, o terreno do outro (calculadas antes, para as linhas
        // entrarem no quadro como obstáculos)
        (P A, P C, string Texto, string Dica, int Lado)[] cotasDnitLinhas = [];
        if (cotasDnit && br.Count > 0 && dnit is { PontoEixo: { } pe, PontoLote: { } pl0 } && T(pe) is var pa0 && pa0.X >= 0 && pa0.X <= lw && pa0.Y >= 0 && pa0.Y <= lh
            && pe.Distance(pl0) is var dEixo && dEixo > 0)
        {
            var pt = T(pl0);
            double ux = (pl0.X - pe.X) / dEixo, uy = (pl0.Y - pe.Y) / dEixo;
            var pDom = T(new Coordinate(pe.X + ux * dnit.Dominio, pe.Y + uy * dnit.Dominio));
            var compPx = Math.Sqrt((pt.X - pa0.X) * (pt.X - pa0.X) + (pt.Y - pa0.Y) * (pt.Y - pa0.Y));
            if (compPx > 0)
            {
                double nx = -(pt.Y - pa0.Y) / compPx, ny = (pt.X - pa0.X) / compPx, d = 10 * S;
                P Desl(P p, double k) => new(p.X + nx * d * k, p.Y + ny * d * k);
                cotasDnitLinhas =
                [
                    (Desl(pa0, 1), Desl(pDom, 1), $"Domínio DNIT {MapaLocalizacao.Num(dnit.Dominio)} m", DicaDnit + " Medido do eixo da rodovia.", 1),
                    (Desl(pa0, -1), Desl(pt, -1), $"Eixo → terreno {MapaLocalizacao.Num(dEixo)} m", DicaDnit + " Do eixo ao ponto mais próximo do lote, perpendicular ao eixo.", -1),
                ];
            }
        }
        void CotasBr262(QuadroRotulos q, StringBuilder? saida)
        {
            const string corDnit = "#E37400", txtDnit = "#8A4500", bordaDnit = "#F6C48A";
            foreach (var (a, c, texto, dica, lado) in cotasDnitLinhas) Cota(q, saida, a, c, corDnit, txtDnit, bordaDnit, texto, dica, lado);
        }

        // Cota do lote até a calha do curso d'água (quando ele não toca o lote e a calha está na imagem), na mesma linguagem das outras,
        // com a faixa de APP gravada na base (como o faixa_app do kit). A linha só aparece com o valor.
        (P A, P C, string Texto)? cotaRio = null;
        if (rio is { PontoLote: { } plr, PontoCalha: { } pcalha, Distancia: > 0 } && T(pcalha) is var pbr && pbr.X >= 0 && pbr.X <= lw && pbr.Y >= 0 && pbr.Y <= lh)
            cotaRio = (T(plr), pbr, $"Rio → terreno {MapaLocalizacao.Num(rio.Distancia, 1)} m" + (rio.App.Count > 0 ? $" · APP {string.Join("/", rio.App)} m" : ""));
        void CotaDoRio(QuadroRotulos q, StringBuilder? saida)
        {
            if (cotaRio is not { } cr) return;
            Cota(q, saida, cr.A, cr.C, "#1E3A8A", "#1E3A8A", "#BFDBFE", cr.Texto,
                "Do lote ao eixo (calha) do curso d'água, medida na base; APP: largura gravada na base (Hidro_app), a conferir.", 0);
        }

        // Largura da rua em frente ao lote (o Logradouro.shp não tem largura): medida na base, do alinhamento do lote, perpendicular ao
        // eixo, até o primeiro lote ou quadra do outro lado; sem nada do outro lado, só do eixo ao alinhamento. Só com a medida na imagem.
        LarguraRua? largura = null;
        if (cotasRua)
        {
            var frente = ruas.Where(r => !MapaLocalizacao.EhRodovia(r.Campo("NomeLogra")))
                .Select(r => (R: r, D: MapaLocalizacao.DistanciaLinhaLote(r.Partes, alvo.Partes))).Where(x => x.D.Par is not null && x.D.Dist <= 25)
                .OrderBy(x => x.D.Dist).FirstOrDefault();
            if (frente.D.Par is { } par)
            {
                var env = new Envelope(par.Linha);
                env.ExpandBy(45);
                var outro = MapaLocalizacao.Em(b, Camada.Lotes, env).Where(l => l.Tipo == 'A' && !alvo.G.EqualsTopologically(l.G)).Select(l => l.G)
                    .Concat(MapaLocalizacao.Em(b, Camada.Quadras, env).Where(x => x.Tipo == 'A' && !x.G.Covers(alvo.G.InteriorPoint)).Select(x => x.G));
                largura = Cotas.LarguraDaRua(par.Lote, par.Linha, outro);
            }
        }
        void CotaDaRua(QuadroRotulos q, StringBuilder? saida)
        {
            if (largura is not { } lr) return;
            P a = T(lr.Inicio), c = T(lr.Fim);
            if (new[] { a, c }.Any(p => p.X < 0 || p.X > lw || p.Y < 0 || p.Y > lh)) return;
            var texto = lr.Inteira ? $"Rua {MapaLocalizacao.Num(lr.Metros)} m" : $"Eixo → alinhamento {MapaLocalizacao.Num(lr.Metros)} m";
            var dica = lr.Inteira
                ? "Largura da rua de alinhamento a alinhamento, medida na base (conferir no local)."
                : "Do eixo da rua ao alinhamento do lote, medida na base (sem lote ou quadra do outro lado; conferir no local).";
            Cota(q, saida, a, c, "#8E877C", "#4A453E", "#D9D3C8", texto, dica, 0);
        }

        // As linhas das cotas são fixas (não mudam de lugar): entram nos dois quadros como obstáculos, para nenhum texto ficar por cima
        // delas; a pílula de cada cota fica ao lado da linha, sem cobri-la.
        var linhasFixas = new List<P[]>();
        void Linha(P a, P c) { var comp = Dist(c.X - a.X, c.Y - a.Y); if (comp > 0) linhasFixas.Add(Rotulagem.CaixaGirada(new P((a.X + c.X) / 2, (a.Y + c.Y) / 2), comp, 2.5 * S, Cotas.AnguloLegivel(a, c))); }
        foreach (var (a, c, _) in ladosLote) Linha(a, c);
        if (cotaRio is { } cr0) Linha(cr0.A, cr0.C);
        foreach (var (a, c, _, _, _) in cotasDnitLinhas) Linha(a, c);
        if (largura is { } lr0) Linha(T(lr0.Inicio), T(lr0.Fim));
        var topo = new StringBuilder();
        var cartucho = QuadroDoLote(topo, cabecalhoLote, dadosLote, observacoes);
        if (tLote.Length > 0)
        {
            posLote = PosicaoEtiquetaLote(ptsLote, centroLote, we, he, lw, lh, [rosa.Contorno(), cartucho.Contorno(), .. linhasFixas]);
            caixaEtiqueta = new Ret(posLote.X - we / 2, posLote.Y - he / 2, posLote.X + we / 2, posLote.Y + he / 2);
        }

        // Ensaio dos nomes, para a escala escolher onde ficar sem cobri-los.
        var ensaio = new QuadroRotulos(lw, lh);
        ensaio.Ocupar(rosa);
        ensaio.Ocupar(cartucho);
        if (caixaEtiqueta is { } ce0) ensaio.Ocupar(ce0);
        foreach (var l in linhasFixas) ensaio.Ocupar(l);
        var jaPostos = ensaio.Ocupados.Count;
        // prioridade (2026-11-17): cotas do lote > rio/APP > BR-262 > largura da rua > nomes de ruas e do rio > números > vizinhos > zonas
        CotasDoLote(ensaio, null);
        CotaDoRio(ensaio, null);
        CotasBr262(ensaio, null);
        CotaDaRua(ensaio, null);
        var cotasEnsaio = ensaio.Ocupados.Skip(jaPostos).Select(Rotulagem.Envolvente).ToList(); // a escala não as cobre
        jaPostos = ensaio.Ocupados.Count;
        RotularNomes(ensaio, null);
        var nomesEnsaio = ensaio.Ocupados.Skip(jaPostos).ToList();
        // os números e os nomes dos confrontantes não podem mudar de lote: a escala também prefere não cobri-los
        var perto = alvo.Bb.Copy();
        perto.ExpandBy(Math.Max(35.0, (alvo.Bb.Width + alvo.Bb.Height) / 2));
        var numerosPerto = MapaLocalizacao.Em(b, Camada.NumerosLote, perto)
            .Where(n => n.Campo("LOTE").Length > 0 && !MapaLocalizacao.Dentro(n.Partes[0][0], alvo.Partes)).ToList();
        foreach (var n in numerosPerto) nomesEnsaio.Add(Rotulagem.CaixaGirada(T(n.Partes[0][0]), Letras.Largura(n.Campo("LOTE"), 16 * S, false) + 2 * S, 20 * S, 0));
        foreach (var (l, nome) in rotulosVizinhos)
        {
            var (tw, th, _, _) = Medida(nome, 15 * S, false, false);
            nomesEnsaio.Add(Rotulagem.CaixaGirada(T(CentroInterno(l.G)), tw, th, 0));
        }

        // escala gráfica (no canto livre mais próximo), reservada antes do posicionamento final dos nomes
        var evitar = new List<Ret> { rosa, cartucho, new Ret(cl.X0 - m_, cl.Y0 - m_, cl.X1 + m_, cl.Y1 + m_) };
        if (caixaEtiqueta is { } ce1) evitar.Add(ce1);
        evitar.AddRange(cotasEnsaio);
        evitar.AddRange(linhasFixas.Select(Rotulagem.Envolvente));
        var painel = PainelEscala(topo, ext, sx, lw, lh, evitar.ToArray(), nomesEnsaio.Select(Rotulagem.Envolvente).ToList());
        var quadro = new QuadroRotulos(lw, lh);
        quadro.Ocupar(rosa);
        quadro.Ocupar(painel);
        quadro.Ocupar(cartucho);
        if (caixaEtiqueta is { } ce3) quadro.Ocupar(ce3);
        foreach (var l in linhasFixas) quadro.Ocupar(l);
        var cotasSvg = new StringBuilder();
        CotasDoLote(quadro, cotasSvg);
        CotaDoRio(quadro, cotasSvg);
        CotasBr262(quadro, cotasSvg);
        CotaDaRua(quadro, cotasSvg);
        var nomesRuas = RotularNomes(quadro, s);
        // números dos lotes (só em volta do lote do projeto), cada um no seu ponto: se não couber, não aparece
        foreach (var n in numerosPerto) // o do lote do projeto vai na etiqueta vermelha
        {
            var t = n.Campo("LOTE");
            var q = T(n.Partes[0][0]);
            var (w, cima, baixo) = Letras.Medir(t, 16 * S, false);
            var h = cima + baixo;
            var poli = Rotulagem.CaixaGirada(q, w + 2 * S, h + 2 * S, 0);
            if (!quadro.Cabe(poli)) continue;
            quadro.Ocupar(poli);
            s.Append($"<text x=\"{N(q.X)}\" y=\"{N(q.Y + (cima - baixo) / 2)}\" font-size=\"{16 * S}\" text-anchor=\"middle\" fill=\"{Texto2}\" stroke=\"{Fundo}\" stroke-width=\"{2 * S}\" stroke-linejoin=\"round\" paint-order=\"stroke\">{Html.E(t)}</text>");
        }

        // proprietários dos lotes confrontantes (nome curto, com halo branco): abaixo do número do lote, no centro do maior círculo que cabe
        // no lote e em volta dele, sempre dentro do lote; não cabendo, as iniciais do primeiro nome; senão, fica de fora
        foreach (var (l, nome) in rotulosVizinhos)
        {
            var num = MapaLocalizacao.Em(b, Camada.NumerosLote, l.Bb).FirstOrDefault(n => MapaLocalizacao.Dentro(n.Partes[0][0], l.Partes));
            var centros = new List<P>();
            if (num is not null) { var pn = T(num.Partes[0][0]); centros.Add(new P(pn.X, pn.Y + 17 * S)); centros.Add(new P(pn.X, pn.Y - 17 * S)); }
            var miolo = CentroInterno(l.G);
            var tamNome = 15 * S;
            var feito = false;
            foreach (var texto in new[] { nome, MapaLocalizacao.Abreviado(nome) }.Distinct())
            {
                var (tw, th, _, _) = Medida(texto, tamNome, false, false);
                var cands = centros.Concat(Rotulagem.AoRedor(T(miolo), tw, th, 2 * S).Select(c => c.Centro))
                    .Where(c => MapaLocalizacao.Dentro(Mundo(c), l.Partes));
                foreach (var c in cands)
                    if (Etiqueta(quadro, s, c, texto, tamNome, false, Texto, null, 0)) { feito = true; break; }
                if (feito) break;
            }
        }

        // nomes das zonas por último (discretos): ficam por baixo do lote no desenho e só entram onde sobrou lugar, nunca sobre o lote
        quadro.Ocupar(cl);
        foreach (var z in zonas)
        {
            var nome = z.Campo("ZONA");
            var c = MapaLocalizacao.Centro(z.Partes);
            if (!NoRecorte(c))
            {
                var pts = z.Partes.SelectMany(a => a).Where(NoRecorte).ToList();
                c = pts.Count == 0 ? ext.Centre : new Coordinate(pts.Average(p => p.X), pts.Average(p => p.Y));
            }
            if (nome.Length == 0) continue;
            var (tw, th, _, _) = Medida(nome, 14 * S, true, true);
            // o centro e em volta; depois, pontos da parte visível da zona, do mais perto do centro ao mais longe
            var vz = z.Bb.Intersection(ext);
            var pontos = new List<Coordinate>();
            if (!vz.IsNull)
                for (var i = 1; i < 10; i++)
                    for (var j = 1; j < 7; j++)
                    {
                        var g = new Coordinate(vz.MinX + vz.Width * i / 10, vz.MinY + vz.Height * j / 7);
                        if (MapaLocalizacao.Dentro(g, z.Partes)) pontos.Add(g);
                    }
            var cands = Rotulagem.AoRedor(T(c), tw, th, 6 * S).Select(k => k.Centro)
                .Concat(pontos.OrderBy(g => g.Distance(c)).Select(T));
            foreach (var cand in cands)
                if (MapaLocalizacao.Dentro(Mundo(cand), z.Partes) && Etiqueta(quadro, zonasSvg, cand, nome, 14 * S, true, "#FFFFFF", RotuloZona, 0)) break;
        }
        s.Insert(marcaZonas, zonasSvg.ToString());

        // a etiqueta do número do lote do projeto (o lugar já estava reservado), a escala e o norte por cima de tudo
        if (tLote.Length > 0) Etiqueta(null, s, posLote, tLote, 19 * S, true, "#FFFFFF", Vermelho, 0);
        s.Append(topo);
        Norte(s, lw - 50 * S, 50 * S);
        if (cotasSvg.Length > 0) s.Append("<g id=\"cotas\">").Append(cotasSvg).Append("</g>");
        s.Append("</svg>");

        // a legenda (numa linha só, abaixo do mapa no relatório) lista só o que foi desenhado
        var rotuloLote = modo == "CANDIDATE_POINT" ? "Lote na base municipal" : "Lote do projeto";
        var legenda = new List<ItemLegenda> { new("lote", rotuloLote) };
        if (edifLote.Count > 0) legenda.Add(new("edif", "Edificação no lote"));
        if (hidro.Any(h => MapaLocalizacao.FaixaApp(h.Campo("APP")).Count > 0)) legenda.Add(new("app", "Faixa de APP"));
        if (hidro.Count > 0) legenda.Add(new("rio", "Curso d'água"));
        if (br.Count > 0 && dnit is not null) legenda.Add(new("dnit", $"Domínio DNIT {MapaLocalizacao.Num(dnit.Dominio, 0)} m (a confirmar) + {MapaLocalizacao.Num(dnit.NaoEdificavel, 0)} m não edificáveis"));
        if (zonas.Count > 0) legenda.Add(new("zona", "Zonas do PDM"));
        return (s.ToString(), nomesRuas, legenda);
    }

    sealed record TrechosRio(List<(Coordinate A, Coordinate B, Coordinate M)> Candidatos, string Texto);

    static string N2(double v) => v.ToString("0.###", Inv);

    /// <summary>O centro do maior círculo que cabe no lote (o lugar mais "dentro" do polígono); sem ele, um ponto interno.</summary>
    static Coordinate CentroInterno(Geometry g)
    {
        try
        {
            var tol = Math.Max(0.05, Math.Max(g.EnvelopeInternal.Width, g.EnvelopeInternal.Height) / 200);
            return new NetTopologySuite.Algorithm.Construct.MaximumInscribedCircle(g, tol).GetCenter().Coordinate;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or TopologyException)
        {
            return g.InteriorPoint.Coordinate;
        }
    }

    /// <summary>Onde fica a etiqueta vermelha "LOTE nº" (2026-11-17): fora da projeção do lote, encostada nele (abaixo, acima, à direita,
    /// à esquerda e nos cantos), para não cobrir o desenho. Prefere um lugar que caiba na imagem e não cubra os obstáculos; sem nenhum,
    /// volta ao centro do lote.</summary>
    static P PosicaoEtiquetaLote(IReadOnlyList<P> ptsLote, P centro, double w, double h, double lw, double lh, IReadOnlyList<P[]> postas)
    {
        if (ptsLote.Count == 0) return centro;
        double x0 = ptsLote.Min(p => p.X), y0 = ptsLote.Min(p => p.Y), x1 = ptsLote.Max(p => p.X), y1 = ptsLote.Max(p => p.Y);
        // encostada no lote; se as linhas das cotas estiverem ali, um pouco mais longe
        var candidatos = new[] { 6.0 * S, 14.0 * S, 30.0 * S, 50.0 * S }.SelectMany(folga => new[]
        {
            new P(centro.X, y1 + folga + h / 2), new P(centro.X, y0 - folga - h / 2),
            new P(x1 + folga + w / 2, centro.Y), new P(x0 - folga - w / 2, centro.Y),
            new P(x1 + folga + w / 2, y1 + folga + h / 2), new P(x0 - folga - w / 2, y1 + folga + h / 2),
            new P(x1 + folga + w / 2, y0 - folga - h / 2), new P(x0 - folga - w / 2, y0 - folga - h / 2),
        }).ToArray();
        bool Cabe(P c) => c.X - w / 2 >= 2 && c.X + w / 2 <= lw - 2 && c.Y - h / 2 >= 2 && c.Y + h / 2 <= lh - 2;
        var cabem = candidatos.Where(Cabe).ToList();
        return cabem.FirstOrDefault(c => !postas.Any(o => Rotulagem.Sobrepoe(Rotulagem.CaixaGirada(c, w, h, 0), o)), cabem.Count > 0 ? cabem[0] : centro);
    }

    static double Sobre(double x, double y, double larg, double alt, Ret r) =>
        Math.Max(0, Math.Min(x + larg, r.X1) - Math.Max(x, r.X0)) * Math.Max(0, Math.Min(y + alt, r.Y1) - Math.Max(y, r.Y0));

    static Ret QuadroDoLote(StringBuilder svg, string titulo, IReadOnlyList<(string Rotulo, string Valor)> dados, IReadOnlyList<string> observacoes)
    {
        const double corpo = 13 * S, margem = 11 * S, entrelinhas = 17 * S;
        double coluna = dados.Max(c => Letras.Largura(c.Rotulo, corpo, false)) + 11 * S;
        double areaValor = Math.Clamp(dados.Max(c => Letras.Largura(c.Valor, corpo, true)), 125 * S, 195 * S);
        var linhas = new List<(string Nome, string Valor)>();
        foreach (var (nome, valor) in dados)
        {
            var partes = QuebrarValor(string.IsNullOrWhiteSpace(valor) ? "Sem informação" : valor, areaValor, corpo);
            for (var i = 0; i < partes.Count; i++) linhas.Add((i == 0 ? nome : "", partes[i]));
        }
        double w = margem * 2 + coluna + areaValor;
        var notas = observacoes.SelectMany((nota, i) => QuebrarValor(i == 0 ? "Obs. " + nota : nota, w - 2 * margem, corpo, abreviar: false)).ToList();
        double h = 36 * S + (linhas.Count + notas.Count) * entrelinhas + 7 * S;
        const double recuo = 12 * S;
        var caixa = new Ret(recuo, recuo, recuo + w, recuo + h);
        var x = caixa.X0 + margem;
        var y = caixa.Y0 + 19 * S;
        svg.Append($"<g id=\"dados-do-lote\"><title>{Html.E(titulo)} — {Html.E(string.Join(" · ", dados.Select(c => c.Rotulo + " " + c.Valor)))}</title>");
        svg.Append($"<rect x=\"{N(caixa.X0)}\" y=\"{N(caixa.Y0)}\" width=\"{N(w)}\" height=\"{N(h)}\" rx=\"{9 * S}\" ry=\"{9 * S}\" fill=\"#FFFFFF\" stroke=\"#AAB7AF\" stroke-width=\"{S}\"/>");
        svg.Append($"<g><text x=\"{N(x)}\" y=\"{N(y)}\" font-size=\"{14 * S}\" font-weight=\"700\" fill=\"#344C40\">{Html.E(titulo)}</text>");
        y += 8 * S;
        svg.Append($"<path d=\"M{N(x)},{N(y)}h{N(w-2*margem)}\" stroke=\"#DFE5E0\" stroke-width=\"{S}\"/>");
        y += entrelinhas;
        foreach (var (nome, valor) in linhas)
        {
            if (nome.Length > 0) svg.Append($"<text x=\"{N(x)}\" y=\"{N(y)}\" font-size=\"{corpo}\" fill=\"#505F56\">{Html.E(nome)}</text>");
            svg.Append($"<text x=\"{N(x + coluna)}\" y=\"{N(y)}\" font-size=\"{corpo}\" font-weight=\"700\" fill=\"#28382F\">{Html.E(valor)}</text>");
            y += entrelinhas;
        }
        foreach (var nota in notas)
        {
            svg.Append($"<text x=\"{N(x)}\" y=\"{N(y)}\" font-size=\"{corpo}\" font-weight=\"700\" fill=\"#A52323\">{Html.E(nota)}</text>");
            y += entrelinhas;
        }
        svg.Append("</g></g>");
        return caixa;
    }

    static List<string> QuebrarValor(string valor, double limite, double corpo, bool abreviar = true)
    {
        var linhas = new List<string>();
        var restante = valor.Trim();
        while (restante.Length > 0)
        {
            if (Letras.Largura(restante, corpo, true) <= limite) { linhas.Add(restante); break; }
            // A ficha completa e o título acessível preservam valores longos.
            var ultima = abreviar && linhas.Count == 2;
            var disponivel = ultima ? limite - Letras.Largura("…", corpo, true) : limite;
            var n = 1;
            while (n < restante.Length && Letras.Largura(restante[..(n + 1)], corpo, true) <= disponivel) n++;
            var espaco = restante.LastIndexOf(' ', n - 1);
            if (espaco > 0) n = espaco;
            linhas.Add(restante[..n].TrimEnd() + (ultima ? "…" : ""));
            if (ultima) break;
            restante = restante[n..].TrimStart();
        }
        return linhas;
    }

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

    static double Dist(double dx, double dy) => Math.Sqrt(dx * dx + dy * dy);

    /// <summary>A rosa dos ventos: disco branco com a ponta vermelha para o norte (o mapa é sempre norte para cima).</summary>
    static void Norte(StringBuilder s, double x, double y)
    {
        s.Append($"<circle cx=\"{N(x)}\" cy=\"{N(y)}\" r=\"{30 * S}\" fill=\"#FFFFFF\" stroke=\"#E8E4DC\" stroke-width=\"{S}\"/>");
        s.Append($"<path d=\"M{N(x)},{N(y - 22 * S)}L{N(x - 7 * S)},{N(y - 2 * S)}L{N(x + 7 * S)},{N(y - 2 * S)}Z\" fill=\"{Vermelho}\"/>");
        s.Append($"<path d=\"M{N(x)},{N(y + 18 * S)}L{N(x - 7 * S)},{N(y - 2 * S)}L{N(x + 7 * S)},{N(y - 2 * S)}Z\" fill=\"#AEAEB2\"/>");
    }
}
