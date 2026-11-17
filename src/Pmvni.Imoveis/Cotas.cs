using NetTopologySuite.Geometries;

namespace Pmvni.Imoveis;

/// <summary>Um lado do lote para a cota: os extremos (coordenadas da base, metros) e o comprimento medido ao longo dos trechos juntados.</summary>
public sealed record LadoCota(Coordinate A, Coordinate B, double Comprimento);

/// <summary>A largura da rua em frente ao lote. <see cref="Inteira"/>: de alinhamento a alinhamento (do lote até o primeiro lote ou quadra do
/// outro lado, atravessando o eixo); senão, só do eixo ao alinhamento do lote (o outro lado é área aberta).</summary>
public sealed record LarguraRua(Coordinate Inicio, Coordinate Fim, double Metros, bool Inteira);

/// <summary>As contas das cotas do mapa (2026-11-17), sem desenho, para testar: os lados do lote (trechos minúsculos ou quase alinhados
/// juntados num lado só), o lado de fora de cada lado e a largura da rua medida na base (o Logradouro.shp não tem largura como atributo:
/// só o eixo).</summary>
public static class Cotas
{
    public const double LadoMinimo = 0.5;   // m: trecho menor que isto se junta ao vizinho
    public const double GiroMinimo = 3.0;   // graus: mudança de direção menor que isto não separa lados

    /// <summary>Os lados de um anel (fechado ou não), juntando os vértices em que a direção muda menos de <paramref name="giroMinimo"/>
    /// graus e os trechos menores que <paramref name="ladoMinimo"/>. O comprimento de cada lado é a soma dos trechos originais.</summary>
    public static IReadOnlyList<LadoCota> Lados(IReadOnlyList<Coordinate> anel, double ladoMinimo = LadoMinimo, double giroMinimo = GiroMinimo)
    {
        var v = anel.ToList();
        if (v.Count > 1 && v[0].Equals2D(v[^1])) v.RemoveAt(v.Count - 1);
        // remove vértices repetidos
        for (var i = v.Count - 1; i > 0; i--) if (v[i].Equals2D(v[i - 1])) v.RemoveAt(i);
        if (v.Count < 3) return [];
        // índice de cada vértice mantido no anel original, para somar os trechos
        var mantidos = Enumerable.Range(0, v.Count).ToList();
        bool mudou;
        do
        {
            mudou = false;
            for (var k = 0; k < mantidos.Count && mantidos.Count > 3; k++)
            {
                var a = v[mantidos[(k - 1 + mantidos.Count) % mantidos.Count]];
                var b = v[mantidos[k]];
                var c = v[mantidos[(k + 1) % mantidos.Count]];
                if (a.Distance(b) < ladoMinimo || b.Distance(c) < ladoMinimo || Giro(a, b, c) < giroMinimo)
                {
                    mantidos.RemoveAt(k);
                    mudou = true;
                    break;
                }
            }
        } while (mudou);
        var lados = new List<LadoCota>();
        for (var k = 0; k < mantidos.Count; k++)
        {
            int i0 = mantidos[k], i1 = mantidos[(k + 1) % mantidos.Count];
            double comp = 0;
            for (var i = i0; i != i1; i = (i + 1) % v.Count) comp += v[i].Distance(v[(i + 1) % v.Count]);
            lados.Add(new LadoCota(v[i0], v[i1], comp));
        }
        return lados;
    }

    /// <summary>Quanto a direção muda (graus, 0 a 180) ao passar por b, vindo de a e indo para c.</summary>
    public static double Giro(Coordinate a, Coordinate b, Coordinate c)
    {
        var a1 = Math.Atan2(b.Y - a.Y, b.X - a.X);
        var a2 = Math.Atan2(c.Y - b.Y, c.X - b.X);
        var d = Math.Abs(a2 - a1) * 180 / Math.PI;
        return d > 180 ? 360 - d : d;
    }

    /// <summary>A normal unitária do lado AB que aponta para fora do polígono (coordenadas da base, y para cima).</summary>
    public static (double X, double Y) NormalParaFora(LadoCota lado, Geometry poligono)
    {
        double dx = lado.B.X - lado.A.X, dy = lado.B.Y - lado.A.Y, l = Math.Sqrt(dx * dx + dy * dy);
        if (l == 0) return (0, 0);
        (double X, double Y) n = (dy / l, -dx / l);
        var meio = new Coordinate((lado.A.X + lado.B.X) / 2, (lado.A.Y + lado.B.Y) / 2);
        var teste = Shapefile.Fabrica.CreatePoint(new Coordinate(meio.X + n.X * Math.Min(0.05, l / 10), meio.Y + n.Y * Math.Min(0.05, l / 10)));
        return poligono.Covers(teste) ? (-n.X, -n.Y) : n;
    }

    /// <summary>Ângulo de leitura (graus, anti-horário, entre −90 e 90: o texto nunca fica de cabeça para baixo) da reta de a para b na tela.</summary>
    public static double AnguloLegivel(PontoTela a, PontoTela b)
    {
        var ang = Math.Atan2(-(b.Y - a.Y), b.X - a.X) * 180 / Math.PI;
        if (ang > 90) ang -= 180;
        if (ang <= -90) ang += 180;
        return ang;
    }

    /// <summary>A largura da rua em frente ao lote, medida na base: do ponto do lote mais perto do eixo (<paramref name="pontoLote"/>),
    /// perpendicular ao eixo (<paramref name="pontoEixo"/>, o pé da perpendicular), atravessando-o até a primeira borda de lote ou quadra do
    /// outro lado (até <paramref name="alcance"/> metros além do eixo). Sem nada do outro lado: só do eixo ao alinhamento.</summary>
    public static LarguraRua? LarguraDaRua(Coordinate pontoLote, Coordinate pontoEixo, IEnumerable<Geometry> outroLado, double alcance = 40)
    {
        var meia = pontoLote.Distance(pontoEixo);
        if (meia <= 0.01) return null;
        double ux = (pontoEixo.X - pontoLote.X) / meia, uy = (pontoEixo.Y - pontoLote.Y) / meia;
        var raio = Shapefile.Fabrica.CreateLineString([
            new Coordinate(pontoEixo.X + ux * 0.01, pontoEixo.Y + uy * 0.01),
            new Coordinate(pontoEixo.X + ux * alcance, pontoEixo.Y + uy * alcance)]);
        Coordinate? melhor = null;
        var dMelhor = double.MaxValue;
        foreach (var g in outroLado)
        {
            if (!g.EnvelopeInternal.Intersects(raio.EnvelopeInternal)) continue;
            Geometry corte;
            try { corte = raio.Intersection(g is Polygon or MultiPolygon ? g.Boundary : g); }
            catch (TopologyException) { continue; }
            foreach (var c in corte.Coordinates)
            {
                var d = c.Distance(pontoEixo);
                if (d < dMelhor) { dMelhor = d; melhor = c; }
            }
        }
        return melhor is null
            ? new LarguraRua(pontoEixo, pontoLote, meia, false)
            : new LarguraRua(pontoLote, melhor, pontoLote.Distance(melhor), true);
    }
}
