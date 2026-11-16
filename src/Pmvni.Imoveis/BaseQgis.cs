using System.Globalization;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NetTopologySuite.Operation.Distance;

namespace Pmvni.Imoveis;

/// <summary>Uma camada carregada: as feições com geometria e o índice espacial.</summary>
public sealed class CamadaCarregada
{
    public Camada Camada { get; }
    public ArquivoFonte Fonte { get; }
    public IReadOnlyList<Feicao> Feicoes { get; }
    public bool Geografica { get; }
    readonly STRtree<Feicao> indice = new();

    public CamadaCarregada(Camada camada, ArquivoFonte fonte, IReadOnlyList<Feicao> feicoes, bool geografica)
    {
        Camada = camada;
        Fonte = fonte;
        Feicoes = feicoes;
        Geografica = geografica;
        foreach (var f in feicoes) if (f.Geometria is { IsEmpty: false } g) indice.Insert(g.EnvelopeInternal, f);
        indice.Build();
    }

    public IEnumerable<Feicao> Perto(Envelope e) => indice.Query(e);
}

/// <summary>A base QGIS da pasta escolhida: o cadastro (SQLite) e as camadas vetoriais, carregados uma vez (somente leitura) e mantidos
/// enquanto a pasta não muda. Camada que falta ou não abre fica de fora com aviso; o relatório diz o que não foi consultado.</summary>
public sealed class BaseQgis
{
    public Inventario Inventario { get; }
    public Cadastro Cadastro { get; }
    public DateTimeOffset LidaEm { get; }
    readonly Dictionary<Camada, CamadaCarregada> camadas = [];
    readonly Dictionary<string, List<Feicao>> lotesPorInscricao = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<Feicao>> lotesPorTerreno = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<Feicao>> edificacoesPorInscricao = new(StringComparer.Ordinal);
    public IReadOnlyList<string> Avisos { get; }

    BaseQgis(Inventario inventario, Cadastro cadastro, List<string> avisos)
    {
        Inventario = inventario;
        Cadastro = cadastro;
        Avisos = avisos;
        LidaEm = DateTimeOffset.Now;
    }

    public CamadaCarregada? this[Camada c] => camadas.GetValueOrDefault(c);

    /// <summary>Os 11 primeiros dígitos da inscrição (distrito, setor, quadra e lote), sem a unidade: identificam o terreno.</summary>
    public static string? Terreno(string inscricao) => inscricao.Length == 14 && inscricao.All(char.IsAsciiDigit) ? inscricao[..11] : null;

    public static BaseQgis Carregar(string pasta, IProgress<string>? progresso = null, CancellationToken ct = default)
    {
        progresso?.Report("Conferindo os arquivos da pasta…");
        var inv = FontesQgis.Verificar(pasta, ct);
        if (inv.BaseCadastral is null) throw new FileNotFoundException(inv.Avisos.FirstOrDefault() ?? "Base cadastral não encontrada.");
        progresso?.Report("Lendo a base cadastral…");
        var cad = Cadastro.Carregar(inv.BaseCadastral.Caminho, ct);
        var avisos = new List<string>(inv.Avisos);
        var b = new BaseQgis(inv, cad, avisos);
        foreach (var (camada, fonte) in inv.Camadas)
        {
            ct.ThrowIfCancellationRequested();
            if (fonte is null) continue;
            progresso?.Report($"Lendo {FontesQgis.Rotulo(camada).ToLowerInvariant()}…");
            try
            {
                var feicoes = Shapefile.Ler(fonte.Caminho, ct);
                var invalidas = 0;
                var validas = new List<Feicao>(feicoes.Count);
                foreach (var f in feicoes)
                {
                    if (f.Geometria is not { IsEmpty: false } g) continue;
                    if (g is Polygon or MultiPolygon && !g.IsValid) { invalidas++; validas.Add(f with { Geometria = g.Buffer(0) }); }
                    else validas.Add(f);
                }
                if (invalidas > 0) avisos.Add($"{FontesQgis.Rotulo(camada)}: {invalidas} geometria(s) inválida(s) corrigida(s) só na memória (buffer 0) para os cálculos.");
                b.camadas[camada] = new CamadaCarregada(camada, fonte, validas, Shapefile.EhGeografico(fonte.Caminho));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
            {
                avisos.Add($"{FontesQgis.Rotulo(camada)} não abriu ({e.Message}).");
            }
        }
        if (b[Camada.Lotes] is { } lotes)
            foreach (var f in lotes.Feicoes)
                if (f.Atributo("InscImob") is { Length: > 0 } i)
                {
                    Acrescentar(b.lotesPorInscricao, i, f);
                    if (Terreno(i) is { } t) Acrescentar(b.lotesPorTerreno, t, f);
                }
        if (b[Camada.Edificacoes] is { } ed)
            foreach (var f in ed.Feicoes)
                if (f.Atributo("InscImob") is { Length: > 0 } i) Acrescentar(b.edificacoesPorInscricao, i, f);
        return b;
    }

    static void Acrescentar(Dictionary<string, List<Feicao>> d, string k, Feicao f)
    {
        if (!d.TryGetValue(k, out var l)) d[k] = l = [];
        l.Add(f);
    }

    /// <summary>A ligação do registro com o desenho: inscrição igual (texto, zeros contam); sem ela, um lote só do mesmo terreno.</summary>
    public (VinculoMapa Vinculo, IReadOnlyList<Feicao> Lotes) Vincular(string inscricao)
    {
        if (this[Camada.Lotes] is null) return (VinculoMapa.SemLote, []);
        if (lotesPorInscricao.TryGetValue(inscricao, out var exatos))
            return exatos.Count == 1 ? (VinculoMapa.Confirmado, exatos) : (VinculoMapa.Ambiguo, exatos);
        if (Terreno(inscricao) is { } t && lotesPorTerreno.TryGetValue(t, out var mesmos))
        {
            var distintos = mesmos.Select(f => f.Atributo("InscImob")).Distinct(StringComparer.Ordinal).Count();
            return distintos == 1 && mesmos.Count == 1 ? (VinculoMapa.MesmoTerreno, mesmos) : (VinculoMapa.Ambiguo, mesmos);
        }
        return (VinculoMapa.SemLote, []);
    }

    /// <summary>O vínculo sem montar a análise (para a coluna da tabela de resultados).</summary>
    public VinculoMapa VinculoRapido(string inscricao) => Vincular(inscricao).Vinculo;

    public IReadOnlyList<Feicao> EdificacoesDa(string inscricao) => edificacoesPorInscricao.TryGetValue(inscricao, out var l) ? l : [];

    /// <summary>Monta a análise territorial do registro (o que a base mostra; nunca uma conclusão normativa).</summary>
    public AnaliseImovel Analisar(RegistroCadastro r, CancellationToken ct = default) => AnaliseImovel.Montar(this, r, ct);
}

// ---------- análise ----------

public sealed record ParteZona(string Zona, double Area, double Percentual);
public sealed record Confrontante(int Numero, string? Inscricao, string Direcao, double Divisa, string? Endereco, string? Situacao, Geometry Geometria);
public sealed record Frente(string Logradouro, double Distancia, double Testada);
public sealed record Hidro(string Tipo, double? FaixaApp, double Distancia, double AreaNaFaixa, string? Nome, bool FaixaAtipica);
public sealed record Edificacao(string? Inscricao, double AreaNoLote, double AreaArmazenada);

/// <summary>O resultado dos cruzamentos de um imóvel. Com o lote não identificado com segurança (<see cref="Conclusivo"/> falso), zona e
/// APP são só informativos.</summary>
public sealed class AnaliseImovel
{
    public required RegistroCadastro Registro { get; init; }
    public required VinculoMapa Vinculo { get; init; }
    public IReadOnlyList<Feicao> Lotes { get; init; } = [];
    public Geometry? Lote { get; init; }
    public double? AreaArmazenada { get; init; }
    public IReadOnlyList<ParteZona> Zonas { get; init; } = [];
    public IReadOnlyList<Confrontante> Confrontantes { get; init; } = [];
    public IReadOnlyList<Frente> Frentes { get; init; } = [];
    public IReadOnlyList<Hidro> Hidrografia { get; init; } = [];
    public IReadOnlyList<Edificacao> Edificacoes { get; init; } = [];
    public string? PerimetroUrbano { get; init; }
    public string? Auc { get; init; }
    public string? Loteamento { get; init; }
    public string? Quadra { get; init; }
    public string? ZonaDaQuadra { get; init; }
    public string? NumeroLoteDesenho { get; init; }
    public IReadOnlyList<RegistroCadastro> UnidadesDoTerreno { get; init; } = [];
    public IReadOnlyList<string> NaoConsultadas { get; init; } = [];
    public bool Conclusivo => Vinculo == VinculoMapa.Confirmado && Lote is not null;

    const double ToleranciaDivisa = 0.5;   // m: lotes a menos disso dividem a mesma linha
    const double RaioHidro = 100;           // m: cursos d'água listados até esta distância
    const double RaioRua = 40;              // m: logradouros candidatos à frente

    internal static AnaliseImovel Montar(BaseQgis b, RegistroCadastro r, CancellationToken ct)
    {
        var insc = r.InscricaoMunicipal;
        var (vinculo, lotes) = b.Vincular(insc);
        var naoConsultadas = Enum.GetValues<Camada>().Where(c => b[c] is null).Select(FontesQgis.Rotulo).ToList();
        var unidades = BaseQgis.Terreno(insc) is { } t
            ? b.Cadastro.Registros.Where(x => x.LinhaFonte != r.LinhaFonte && x.InscricaoMunicipal.StartsWith(t, StringComparison.Ordinal)).ToList()
            : [];
        if (lotes.Count == 0)
            return new AnaliseImovel { Registro = r, Vinculo = vinculo, NaoConsultadas = naoConsultadas, UnidadesDoTerreno = unidades };

        var geoms = lotes.Select(f => f.Geometria!).ToList();
        var lote = geoms.Count == 1 ? geoms[0] : NetTopologySuite.Operation.Union.UnaryUnionOp.Union(geoms);
        double? areaArm = lotes.Sum(f => Numero(f.Atributo("AREA")) ?? 0) is var s && s > 0 ? s : null;
        ct.ThrowIfCancellationRequested();

        // Zonas: a parte do lote em cada zona (nunca "a primeira zona").
        var zonas = new List<ParteZona>();
        if (b[Camada.Zoneamento] is { } zon)
        {
            var porZona = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var z in zon.Perto(lote.EnvelopeInternal))
                if (z.Geometria!.Intersects(lote))
                {
                    var a = z.Geometria.Intersection(lote).Area;
                    if (a < 0.01) continue;
                    var nome = z.Atributo("ZONA") ?? "(sem nome)";
                    porZona[nome] = porZona.GetValueOrDefault(nome) + a;
                }
            var total = lote.Area;
            // Sobras de encaixe do desenho (menos de 5 m² e de 0,5% do lote) não contam como parte em outra zona.
            zonas = porZona.Where(p => p.Value >= 5 || (total > 0 && p.Value / total >= 0.005))
                .OrderByDescending(p => p.Value).Select(p => new ParteZona(p.Key, p.Value, total > 0 ? p.Value / total * 100 : 0)).ToList();
        }

        // Confrontantes: lotes a menos de 0,5 m, com o comprimento da divisa comum e a direção a partir do centro do lote.
        var confrontantes = new List<Confrontante>();
        if (b[Camada.Lotes] is { } camLotes)
        {
            var env = lote.EnvelopeInternal.Copy();
            env.ExpandBy(ToleranciaDivisa);
            var borda = lote.Boundary;
            var centro = lote.Centroid.Coordinate;
            var vizinhos = camLotes.Perto(env).Where(f => !lotes.Contains(f) && f.Geometria!.IsWithinDistance(lote, ToleranciaDivisa))
                .Select(f =>
                {
                    var comum = borda.Intersection(f.Geometria!.Buffer(ToleranciaDivisa));
                    return (F: f, Divisa: comum.Length, Meio: comum.IsEmpty ? f.Geometria!.InteriorPoint.Coordinate : comum.Centroid.Coordinate);
                })
                .Where(v => v.Divisa >= 0.3)
                .OrderBy(v => Azimute(centro, v.Meio)).ToList();
            var n = 1;
            foreach (var v in vizinhos)
            {
                var i = v.F.Atributo("InscImob");
                var cad = i is null ? null : b.Cadastro.PorInscricao(i).FirstOrDefault();
                confrontantes.Add(new Confrontante(n++, i, Direcao(Azimute(centro, v.Meio)), v.Divisa, cad?.Endereco, cad?.Situacao, v.F.Geometria!));
            }
        }
        ct.ThrowIfCancellationRequested();

        // Frente: logradouros a até 40 m; a testada é a parte da borda do lote a até (distância mínima + 5 m) do eixo da via.
        var frentes = new List<Frente>();
        if (b[Camada.Logradouros] is { } ruas)
        {
            var env = lote.EnvelopeInternal.Copy();
            env.ExpandBy(RaioRua);
            var candidatos = ruas.Perto(env).Select(f => (F: f, D: f.Geometria!.Distance(lote))).Where(x => x.D <= RaioRua)
                .GroupBy(x => x.F.Atributo("NomeLogra") ?? "(sem nome)").Select(g => g.OrderBy(x => x.D).First()).OrderBy(x => x.D).Take(3).ToList();
            foreach (var (f, d) in candidatos)
            {
                var testada = lote.Boundary.Intersection(f.Geometria!.Buffer(d + 5)).Length;
                frentes.Add(new Frente(f.Atributo("NomeLogra") ?? "(sem nome)", d, testada));
            }
        }

        // Hidrografia (eixo) e a faixa de APP do atributo APP: o lote alcança a faixa quando a distância ao eixo é menor que ela.
        var hidro = new List<Hidro>();
        if (b[Camada.HidroApp] is { } hid)
        {
            var env = lote.EnvelopeInternal.Copy();
            env.ExpandBy(RaioHidro);
            foreach (var (f, d) in hid.Perto(env).Select(f => (F: f, D: f.Geometria!.Distance(lote))).Where(x => x.D <= RaioHidro).OrderBy(x => x.D).Take(4))
            {
                var faixa = Numero(f.Atributo("APP"));
                var atipica = faixa is null or <= 0 or > 200;
                var area = !atipica && d < faixa ? lote.Intersection(f.Geometria!.Buffer(faixa!.Value)).Area : 0;
                hidro.Add(new Hidro(f.Atributo("TIPO") ?? "Curso d'água", faixa, d, area, NomeAgerh(b, f.Geometria!, lote), atipica));
            }
        }

        // Projeção das edificações dentro do lote (telhados; não é área construída total).
        var edificacoes = new List<Edificacao>();
        if (b[Camada.Edificacoes] is { } ed)
            foreach (var f in ed.Perto(lote.EnvelopeInternal).Where(f => f.Geometria!.Intersects(lote)))
            {
                var a = f.Geometria!.Intersection(lote).Area;
                if (a >= 1) edificacoes.Add(new Edificacao(f.Atributo("InscImob"), a, Numero(f.Atributo("area")) ?? f.Geometria.Area));
            }

        var ponto = lote.InteriorPoint;
        string? Contem(Camada c, string campo) => b[c]?.Perto(ponto.EnvelopeInternal).FirstOrDefault(f => f.Geometria!.Covers(ponto))?.Atributo(campo);
        var quadra = b[Camada.Quadras]?.Perto(ponto.EnvelopeInternal).FirstOrDefault(f => f.Geometria!.Covers(ponto));
        var numeros = b[Camada.NumerosLote]?.Perto(lote.EnvelopeInternal).Where(f => lote.Covers(f.Geometria!)).Select(f => f.Atributo("LOTE")).Where(x => x is not null).Distinct().ToList();
        var perUrb = b[Camada.PerimetroUrbano] is { } pu
            ? (pu.Perto(ponto.EnvelopeInternal).FirstOrDefault(f => f.Geometria!.Covers(ponto)) is { } p ? p.Atributo("NOME") ?? "Dentro do perímetro urbano" : "Fora do perímetro urbano")
            : null;

        return new AnaliseImovel
        {
            Registro = r, Vinculo = vinculo, Lotes = lotes, Lote = lote, AreaArmazenada = areaArm, Zonas = zonas,
            Confrontantes = confrontantes, Frentes = frentes, Hidrografia = hidro, Edificacoes = edificacoes,
            PerimetroUrbano = perUrb, Auc = Contem(Camada.Auc, "Perímetro") ?? (b[Camada.Auc] is null ? null : ""),
            Loteamento = Contem(Camada.Loteamentos, "nome"), Quadra = quadra?.Atributo("QUADRA"), ZonaDaQuadra = quadra?.Atributo("ZONA"),
            NumeroLoteDesenho = numeros is { Count: > 0 } ? string.Join(", ", numeros) : null,
            UnidadesDoTerreno = unidades, NaoConsultadas = naoConsultadas,
        };
    }

    /// <summary>O nome AGERH do curso d'água: o trecho AGERH mais próximo (até 60 m) do ponto do eixo municipal mais perto do lote.</summary>
    static string? NomeAgerh(BaseQgis b, Geometry eixo, Geometry lote)
    {
        if (b[Camada.CursosAgerh] is not { } ag) return null;
        var pts = DistanceOp.NearestPoints(eixo, lote);
        var p = Shapefile.Fabrica.CreatePoint(pts[0]);
        var env = p.EnvelopeInternal.Copy();
        env.ExpandBy(60);
        var nome = ag.Perto(env).Select(f => (F: f, D: f.Geometria!.Distance(p))).Where(x => x.D <= 60).OrderBy(x => x.D)
            .Select(x => x.F.Atributo("nom_hidrog")).FirstOrDefault();
        return string.IsNullOrWhiteSpace(nome) ? null : nome;
    }

    internal static double? Numero(string? s) =>
        s is not null && double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    /// <summary>Azimute (0 = norte, sentido horário) do ponto <paramref name="b"/> visto de <paramref name="a"/>.</summary>
    static double Azimute(Coordinate a, Coordinate b)
    {
        var az = Math.Atan2(b.X - a.X, b.Y - a.Y) * 180 / Math.PI;
        return az < 0 ? az + 360 : az;
    }

    static string Direcao(double az) => ((int)Math.Round(az / 45) % 8) switch
    {
        0 => "Norte", 1 => "Nordeste", 2 => "Leste", 3 => "Sudeste", 4 => "Sul", 5 => "Sudoeste", 6 => "Oeste", _ => "Noroeste",
    };
}
