using System.Globalization;
using System.Text.RegularExpressions;
using NetTopologySuite.Geometries;

namespace Pmvni.Imoveis;

/// <summary>Um item da legenda do mapa (as classes <c>lk</c> do modelo: lote, edif, app, rio, dnit, zona).</summary>
public sealed record ItemLegenda(string Tipo, string Texto);

/// <summary>Uma linha da ficha da localização (<c>localizacao_dados</c> do kit): grupo, rótulo curto, valor e, se houver, a etiqueta
/// (<c>chip</c> com <c>alerta</c> r/a/g) e a ressalva curta (<c>nota</c>).</summary>
public sealed record DadoLocalizacao(string Grupo, string Rotulo, string Valor, string? Chip = null, string? Alerta = null, string? Nota = null);

/// <summary>As opções do mapa (as mesmas da linha de comando do <c>mapa_localizacao.py</c>).</summary>
public sealed record OpcoesMapa
{
    public double? AreaDeclarada { get; init; }
    public string AreaFonte { get; init; } = "";
    public string Endereco { get; init; } = "";
    /// <summary>A identidade do lote ainda não foi confirmada (traço mais fino no contorno).</summary>
    public bool Candidato { get; init; }
    /// <summary>Mostrar os nomes do cadastro (dado pessoal, uso interno).</summary>
    public bool ComDonos { get; init; } = true;
    public string NomeRio { get; init; } = "";
    /// <summary>Faixa de domínio da BR-262 a partir do eixo, em metros (40 por padrão; a confirmar com o DNIT).</summary>
    public double FaixaDominio { get; init; } = MapaLocalizacao.FaixaDominioBr262;
    /// <summary>O nome a mostrar como proprietário do lote; sem ele, o do cadastro do terreno (como no kit).</summary>
    public string? Proprietario { get; init; }
    /// <summary>As cotas do lote do projeto (2026-11-17): lados do lote, domínio do DNIT e eixo → terreno (BR-262 perto) e largura da rua
    /// em frente. Desligadas, o mapa fica como antes.</summary>
    public bool Cotas { get; init; } = true;
}

public sealed record CursoDagua(string Tipo, double Distancia, IReadOnlyList<int> App, bool AtingeLote, int Feicao, Coordinate? PontoLote, Coordinate? PontoCalha);
/// <summary>A BR-262 em relação ao lote. <see cref="PontoEixo"/> e <see cref="PontoLote"/>: o par de pontos mais próximos (eixo e borda do
/// lote), para as cotas do mapa; nulos quando o eixo cruza o lote.</summary>
public sealed record FaixaBr262(double DistanciaEixo, double Dominio, double NaoEdificavel, Coordinate? PontoEixo, bool AtingeDominio, bool AtingeNaoEdificavel, Coordinate? PontoLote = null);
public sealed record VizinhoMapa(string Inscricao, string Lote, string Proprietario);

/// <summary>O que o <c>mapa_localizacao.py</c> devolve: a imagem (aqui em SVG, 840 × 630, 4:3; o kit usa 840 × 525), a legenda, a ficha e os dados do lote.</summary>
public sealed class MapaGerado
{
    public required string Svg { get; init; }
    public required string Modo { get; init; }
    public required Envelope Extensao { get; init; }
    public required IReadOnlyList<ItemLegenda> Legenda { get; init; }
    public required IReadOnlyList<DadoLocalizacao> Dados { get; init; }
    public required IReadOnlyList<string> Ruas { get; init; }
    /// <summary>As linhas do balão do lote.</summary>
    public required IReadOnlyList<string> Ficha { get; init; }
    public string Inscricao { get; init; } = "";
    public string Lote { get; init; } = "";
    public string Quadra { get; init; } = "";
    public string Zona { get; init; } = "";
    public string Loteamento { get; init; } = "";
    public double? AreaBase { get; init; }
    public double AreaGeometria { get; init; }
    public IReadOnlyList<CursoDagua> CursosDagua { get; init; } = [];
    public bool AppAtingeLote { get; init; }
    public string Auc { get; init; } = "";
    public string PerimetroUrbano { get; init; } = "";
    public FaixaBr262? Br262 { get; init; }
    public int Edificacoes { get; init; }
    public double AreaEdificada { get; init; }
    public string Proprietario { get; init; } = "";
    public string NomeRio { get; init; } = "";
    public IReadOnlyList<VizinhoMapa> Confrontantes { get; init; } = [];
    public double? DiferencaAreaPct { get; init; }
    /// <summary>As camadas da base usadas (nome do arquivo, sem a extensão), para a linha FONTE.</summary>
    public IReadOnlyList<string> Camadas { get; init; } = [];

    /// <summary>A imagem como <c>data:image/svg+xml;base64,…</c>, para o <c>&lt;img&gt;</c> do bloco <c>.lmap</c> do modelo.</summary>
    public string SvgUri() => "data:image/svg+xml;base64," + Convert.ToBase64String(new System.Text.UTF8Encoding(false).GetBytes(Svg));
}

/// <summary>Uma feição no formato do script do kit: índice, tipo (A área, L linha, P ponto), partes (anéis ou linhas) e retângulo.</summary>
internal sealed record Fe(int Indice, char Tipo, List<Coordinate[]> Partes, Envelope Bb, Feicao At, Geometry G)
{
    /// <summary>O primeiro atributo preenchido entre os nomes (sem distinção de caixa), ou vazio.</summary>
    public string Campo(params string[] nomes)
    {
        foreach (var n in nomes) if (At.Atributo(n) is { Length: > 0 } v) return v;
        return "";
    }
}

/// <summary>Porte fiel do <c>mapa_localizacao.py</c> do kit (plugin analise): o mapa de localização do imóvel na base municipal e a ficha
/// da seção LOCALIZAÇÃO NA BASE MUNICIPAL. Mesmas camadas, enquadramento, cores, ordem de desenho, rótulos, balão e dados; a imagem sai em
/// SVG (o script usa o PIL e grava PNG). O desenho fica em <see cref="MapaSvg"/>; aqui ficam os cruzamentos e o fluxo (<c>gerar</c>).</summary>
public static class MapaLocalizacao
{
    public const string Crs = "EPSG:31984 — SIRGAS 2000 / UTM 24S";
    public const double FaixaDominioBr262 = 40.0;   // DNIT: distância a partir do eixo (a confirmar com o DNIT para o trecho)
    public const double FaixaNaoEdificavel = 15.0;  // Lei 6.766/1979, art. 4º, III (redutível por lei municipal até 5 m)
    // 4:3 por decisão do usuário (2026-11-17): o kit desenha 840 × 525 (16:10); aqui o mapa ocupa a largura toda do relatório e fica
    // mais alto, com o mesmo enquadramento (centro do lote a 60% / 58%, larguras mínimas) e o recorte seguindo a proporção 4:3.
    public const int Largura = 840, Altura = 630;
    /// <summary>Até esta distância (m) do lote, o eixo da BR-262 e a calha do curso d'água entram no recorte, com as suas cotas.</summary>
    public const double DistanciaCotas = 100;
    public const string GrupoId = "Identificação", GrupoAreas = "Áreas e edificação", GrupoZona = "Zona e condicionantes", GrupoCadastro = "Cadastro e entorno";

    static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    internal static string Num(double v, int casas = 2) => v.ToString("N" + casas, Br);

    // ───────────────────────── geometria (as mesmas contas do script) ─────────────────────────

    internal static Fe De(Feicao f) => De(f.Indice, f.Geometria!, f);

    internal static Fe De(int indice, Geometry g, Feicao at)
    {
        var partes = new List<Coordinate[]>();
        var tipo = '?';
        for (var i = 0; i < g.NumGeometries; i++)
            switch (g.GetGeometryN(i))
            {
                case Polygon p:
                    tipo = 'A';
                    partes.Add(p.ExteriorRing.Coordinates);
                    foreach (var furo in p.InteriorRings) partes.Add(furo.Coordinates);
                    break;
                case LineString l: tipo = 'L'; partes.Add(l.Coordinates); break;
                case Point pt: tipo = 'P'; partes.Add([pt.Coordinate]); break;
            }
        return new Fe(indice, partes.Count == 0 ? '?' : tipo, partes, g.EnvelopeInternal, at, g);
    }

    /// <summary>As feições da camada que tocam o retângulo, na ordem do arquivo (como o script percorre).</summary>
    internal static List<Fe> Em(BaseQgis b, Camada c, Envelope e) =>
        b[c] is { } cam ? cam.Perto(e).Where(f => f.Geometria!.EnvelopeInternal.Intersects(e)).OrderBy(f => f.Indice).Select(De).Where(f => f.Tipo != '?').ToList() : [];

    internal static IEnumerable<(Coordinate A, Coordinate B)> Bordas(List<Coordinate[]> partes)
    {
        foreach (var anel in partes)
            for (var i = 0; i < anel.Length; i++) yield return (anel[i], anel[(i + 1) % anel.Length]);
    }

    internal static bool Dentro(Coordinate pt, List<Coordinate[]> partes)
    {
        var ok = false;
        foreach (var (p1, p2) in Bordas(partes))
        {
            var dy = p2.Y - p1.Y;
            if ((p1.Y > pt.Y) != (p2.Y > pt.Y) && pt.X < (p2.X - p1.X) * (pt.Y - p1.Y) / (dy == 0 ? 1e-12 : dy) + p1.X) ok = !ok;
        }
        return ok;
    }

    internal static double Area(List<Coordinate[]> partes)
    {
        var s = 0.0;
        foreach (var anel in partes)
        {
            var t = 0.0;
            for (var i = 0; i < anel.Length; i++) { var (a, b) = (anel[i], anel[(i + 1) % anel.Length]); t += a.X * b.Y - b.X * a.Y; }
            s += t;
        }
        return Math.Abs(s) / 2;
    }

    /// <summary>O centro de área do anel com mais vértices (o do script; não é o centroide de todos os anéis).</summary>
    internal static Coordinate Centro(List<Coordinate[]> partes)
    {
        var a = partes[0];
        foreach (var p in partes) if (p.Length > a.Length) a = p;
        double s = 0, cx = 0, cy = 0;
        for (var i = 0; i < a.Length; i++)
        {
            var (p1, p2) = (a[i], a[(i + 1) % a.Length]);
            var f = p1.X * p2.Y - p2.X * p1.Y;
            s += f; cx += (p1.X + p2.X) * f; cy += (p1.Y + p2.Y) * f;
        }
        if (Math.Abs(s) < 1e-9) return new Coordinate(a.Average(p => p.X), a.Average(p => p.Y));
        return new Coordinate(cx / (3 * s), cy / (3 * s));
    }

    /// <summary>O ponto do segmento ab mais próximo de p.</summary>
    static Coordinate Pe(Coordinate p, Coordinate a, Coordinate b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, l = dx * dx + dy * dy;
        var t = l == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / l));
        return new Coordinate(a.X + t * dx, a.Y + t * dy);
    }

    static double DistPontoSegmento(Coordinate p, Coordinate a, Coordinate b) => p.Distance(Pe(p, a, b));

    static bool Cruzam(Coordinate a, Coordinate b, Coordinate c, Coordinate d)
    {
        static double O(Coordinate p, Coordinate q, Coordinate r) => (q.X - p.X) * (r.Y - p.Y) - (q.Y - p.Y) * (r.X - p.X);
        return O(a, b, c) * O(a, b, d) < 0 && O(c, d, a) * O(c, d, b) < 0;
    }

    /// <summary>A menor distância (m) entre as linhas e o lote (0 se tocam ou atravessam) e o par de pontos mais próximos
    /// (o do lote e o da linha), para a cota no mapa.</summary>
    internal static (double Dist, (Coordinate Lote, Coordinate Linha)? Par) DistanciaLinhaLote(List<Coordinate[]> linhas, List<Coordinate[]> lote)
    {
        var bordas = Bordas(lote).ToList();
        var melhor = double.PositiveInfinity;
        (Coordinate, Coordinate)? par = null;
        foreach (var linha in linhas)
            for (var i = 0; i + 1 < linha.Length; i++)
            {
                var (a, b) = (linha[i], linha[i + 1]);
                if (Dentro(a, lote) || Dentro(b, lote)) return (0, null);
                foreach (var (c, d) in bordas)
                {
                    if (Cruzam(a, b, c, d)) return (0, null);
                    Testar(a, c, d, true); Testar(b, c, d, true); Testar(c, a, b, false); Testar(d, a, b, false);
                }
            }
        return (melhor, par);

        void Testar(Coordinate q, Coordinate u, Coordinate v, bool qNaLinha)
        {
            var f = Pe(q, u, v);
            var dist = q.Distance(f);
            if (dist < melhor) { melhor = dist; par = qNaLinha ? (f, q) : (q, f); }
        }
    }

    /// <summary>Largura(s) de APP gravada(s) na base: "30" → [30]; "1530" → [15, 30] (uma por margem).</summary>
    internal static List<int> FaixaApp(string? valor)
    {
        var v = new string((valor ?? "").Where(char.IsAsciiDigit).ToArray());
        if (v.Length == 0) return [];
        if (v.Length <= 2) return [int.Parse(v, CultureInfo.InvariantCulture)];
        var meio = v.Length / 2;
        return new SortedSet<int> { int.Parse(v[..meio], CultureInfo.InvariantCulture), int.Parse(v[meio..], CultureInfo.InvariantCulture) }.ToList();
    }

    static double? Numero(string? v) => AnaliseImovel.Numero(string.IsNullOrWhiteSpace(v) ? null : v);

    // ───────────────────────── nomes ─────────────────────────

    static readonly (string Longo, string Curto)[] Abrev =
    [
        ("Avenida", "Av."), ("Rua", "R."), ("Travessa", "Tv."), ("Rodovia", "Rod."), ("Estrada", "Estr."), ("Prefeito", "Pref."),
        ("Doutor", "Dr."), ("Professora", "Profa."), ("Professor", "Prof."), ("Vereador", "Ver."), ("Coronel", "Cel."),
        ("Padre", "Pe."), ("Engenheiro", "Eng."), ("Deputado", "Dep."), ("Governador", "Gov."), ("Presidente", "Pres."),
        ("Senador", "Sen."), ("Monsenhor", "Mons."), ("Capitão", "Cap."), ("Tenente", "Ten."), ("Sargento", "Sgt."),
    ];

    /// <summary>O <c>str.title()</c> do Python: maiúscula no começo de cada sequência de letras, minúsculas no resto.</summary>
    internal static string Titulo(string s)
    {
        var r = new char[s.Length];
        var antes = false;
        for (var i = 0; i < s.Length; i++)
        {
            var letra = char.IsLetter(s[i]);
            r[i] = !letra ? s[i] : antes ? char.ToLower(s[i], Br) : char.ToUpper(s[i], Br);
            antes = letra;
        }
        return new string(r);
    }

    static bool TudoMaiusculo(string s) => s.Any(char.IsLetter) && !s.Any(char.IsLower);

    /// <summary>Nome em caixa normal, preservando siglas de rodovia: "BR 262" → "BR-262".</summary>
    public static string NomeRua(string nome)
    {
        var m = Regex.Match(nome, @"^\s*(BR|ES)\s*-?\s*(\d+)\s*$", RegexOptions.IgnoreCase);
        if (m.Success) return $"{m.Groups[1].Value.ToUpperInvariant()}-{m.Groups[2].Value}";
        var t = TudoMaiusculo(nome) ? Titulo(nome) : nome;
        return Regex.Replace(t, @"\b(Br|Es)\s*-?\s*(\d+)", x => x.Groups[1].Value.ToUpperInvariant() + "-" + x.Groups[2].Value);
    }

    /// <summary>"Avenida Prefeito Nicolau Falchetto" → "Av. Pref. Nicolau Falchetto" (só para caber no mapa).</summary>
    public static string NomeAbreviado(string nome)
    {
        var t = NomeRua(nome);
        foreach (var (longo, curto) in Abrev) t = Regex.Replace(t, $@"\b{longo}\b", curto);
        return t;
    }

    public static bool EhRodovia(string nome)
    {
        var n = nome.ToUpperInvariant();
        return n.StartsWith("BR ") || n.StartsWith("BR-") || n.StartsWith("ROD") || n.StartsWith("ES ") || n.StartsWith("ES-");
    }

    public static bool EhBr262(string nome) => nome.Contains("262") && nome.Contains("BR", StringComparison.OrdinalIgnoreCase);

    /// <summary>O cadastro marca o dono desconhecido com um texto ("PROPRIETARIO NAO IDENTIFICADO"), que não é nome de pessoa.</summary>
    public static bool SemNome(string nome)
    {
        var k = DeteccaoPesquisa.Chave(nome);
        return k.Contains("NAO IDENTIFICAD", StringComparison.Ordinal) || k.Contains("NAO INFORMAD", StringComparison.Ordinal)
            || k.Contains("DESCONHECID", StringComparison.Ordinal) || k is "" or "-" or "—";
    }

    /// <summary>Primeiro e último nome, para caber sobre o lote vizinho (sem complementos do cadastro). O texto do cadastro para dono
    /// desconhecido vira "Proprietário não identificado" (antes saía "Proprietario Identificad": primeira e última palavra, cortado em 24
    /// letras, com o sentido trocado). Nome longo é encurtado por palavras inteiras, nunca no meio.</summary>
    public static string Curto(string nome)
    {
        if (SemNome(nome)) return "Proprietário não identificado";
        var limpo = Regex.Replace(nome.Split(" e outros")[0], @"\(\s*[A-Z]\s*\)|\b(E\s+)?(SOCIOS?|OUTROS?|OUTRA|ESPOLIO DE|ESPÓLIO DE)\b", " ", RegexOptions.IgnoreCase);
        string[] ligacoes = ["E", "DE", "DA", "DO", "DOS", "DAS"];
        var palavras = limpo.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Where(w => !ligacoes.Contains(w.ToUpper(Br))).ToArray();
        if (palavras.Length == 0) palavras = nome.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var t = Titulo(palavras.Length <= 2 ? string.Join(" ", palavras) : palavras[0] + " " + palavras[^1]);
        if (t.Length <= 24) return t;
        var primeiro = Titulo(palavras[0]);
        return primeiro.Length <= 24 ? primeiro : primeiro[..23] + "…";
    }

    /// <summary>A forma mais curta do rótulo do vizinho, quando o nome curto não cabe: "Osmar Souza" → "O. Souza";
    /// "Proprietário não identificado" → "Não identificado".</summary>
    public static string Abreviado(string curto)
    {
        if (curto == "Proprietário não identificado") return "Não identificado";
        var p = curto.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return p.Length >= 2 && p[0].Length > 1 ? $"{p[0][0]}. {string.Join(" ", p[1..])}" : curto;
    }

    /// <summary>Corta o texto em palavras inteiras até <paramref name="max"/> letras, com reticências.</summary>
    internal static string Encurtar(string t, int max) =>
        t.Length <= max ? t : (t[..max].LastIndexOf(' ') is var i && i > max / 2 ? t[..i] : t[..(max - 1)]).TrimEnd(' ', ',', '(', '-') + "…";

    // ───────────────────────── cruzamentos ─────────────────────────

    static Envelope Expandido(Envelope e, double m)
    {
        var c = e.Copy();
        c.ExpandBy(m);
        return c;
    }

    /// <summary>Nome oficial (AGERH/GEOBASES) do trecho mais próximo do ponto da calha municipal, até <paramref name="raio"/> metros.</summary>
    static string NomeCurso(BaseQgis b, Coordinate? ponto, double raio = 60.0)
    {
        if (ponto is null) return "";
        var perto = Expandido(new Envelope(ponto), raio);
        var melhor = (D: raio, Nome: "");
        foreach (var f in Em(b, Camada.CursosAgerh, perto))
        {
            var n = f.Campo("nom_hidrog");
            if (n.Length == 0 || n.StartsWith("sem nome", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var linha in f.Partes)
                for (var i = 0; i + 1 < linha.Length; i++)
                {
                    var d = DistPontoSegmento(ponto, linha[i], linha[i + 1]);
                    if (d < melhor.D) melhor = (d, n);
                }
        }
        return melhor.Nome;
    }

    /// <summary>APP e cursos d'água próximos, AUC e perímetro urbano do lote (base municipal; não é laudo).</summary>
    static (List<CursoDagua> Cursos, bool AppAtinge, string Auc, string Perimetro) Condicionantes(BaseQgis b, Fe alvo, double raio = 250.0)
    {
        var cen = Centro(alvo.Partes);
        var rios = new List<CursoDagua>();
        foreach (var h in Em(b, Camada.HidroApp, Expandido(alvo.Bb, raio)).Where(h => h.Tipo == 'L'))
        {
            var (dist, par) = DistanciaLinhaLote(h.Partes, alvo.Partes);
            var larg = FaixaApp(h.Campo("APP"));
            var tipo = h.Campo("TIPO");
            rios.Add(new CursoDagua(tipo.Length > 0 ? tipo : "Curso d'água", Math.Round(dist, 1), larg, larg.Count > 0 && dist < larg.Max(), h.Indice, par?.Lote, par?.Linha));
        }
        rios = rios.OrderBy(r => r.Distancia).ToList();
        var ponto = new Envelope(cen);
        var auc = Em(b, Camada.Auc, ponto).Where(a => a.Tipo == 'A' && Dentro(cen, a.Partes)).Select(a => a.Campo("Perímetro", "Perimetro")).FirstOrDefault() ?? "";
        var per = Em(b, Camada.PerimetroUrbano, ponto).Where(a => a.Tipo == 'A' && Dentro(cen, a.Partes)).Select(a => a.Campo("NOME")).FirstOrDefault() ?? "";
        return (rios.Take(3).ToList(), rios.Any(r => r.AtingeLote), auc, per);
    }

    /// <summary>Distância do lote ao eixo da BR-262 e se as faixas (domínio DNIT e não edificável) atingem o lote.</summary>
    static FaixaBr262? FaixaDaBr262(BaseQgis b, Fe alvo, double dominio, double naoEdif = FaixaNaoEdificavel, double raio = 250.0)
    {
        var eixos = Em(b, Camada.Logradouros, Expandido(alvo.Bb, raio)).Where(r => r.Tipo == 'L' && EhBr262(r.Campo("NomeLogra"))).ToList();
        if (eixos.Count == 0) return null;
        var (dist, par) = eixos.Select(r => DistanciaLinhaLote(r.Partes, alvo.Partes)).MinBy(t => t.Dist);
        return new FaixaBr262(Math.Round(dist, 1), dominio, naoEdif, par?.Linha, dist < dominio, dist < dominio + naoEdif, par?.Lote);
    }

    /// <summary>Edificações da base cujo centro fica dentro do lote do projeto.</summary>
    internal static List<Fe> EdificacoesDoLote(BaseQgis b, Fe alvo) =>
        Em(b, Camada.Edificacoes, alvo.Bb).Where(e => e.Tipo == 'A' && Dentro(Centro(e.Partes), alvo.Partes)).ToList();

    /// <summary>Lotes que fazem divisa com o lote do projeto (bordas a menos de <paramref name="tol"/> metros).</summary>
    static List<Fe> Vizinhos(BaseQgis b, Fe alvo, IReadOnlySet<int> proprios, double tol = 0.6)
    {
        var pa = alvo.Partes.SelectMany(a => a).ToList();
        var ba = Bordas(alvo.Partes).ToList();
        var r = new List<Fe>();
        foreach (var l in Em(b, Camada.Lotes, Expandido(alvo.Bb, tol)))
        {
            if (proprios.Contains(l.Indice) || l.Tipo != 'A') continue;
            var bl = Bordas(l.Partes).ToList();
            var perto = l.Partes.SelectMany(a => a).Count(q => ba.Min(s => DistPontoSegmento(q, s.A, s.B)) < tol)
                      + pa.Count(q => bl.Min(s => DistPontoSegmento(q, s.A, s.B)) < tol);
            if (perto >= 2) r.Add(l);
        }
        return r;
    }

    /// <summary>Nome do contribuinte por inscrição (cadastro vigente, só leitura): o primeiro do terreno e "e outros N". DADO PESSOAL.</summary>
    static Dictionary<string, string> Proprietarios(BaseQgis b, IEnumerable<string> inscricoes)
    {
        var r = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var ins in inscricoes.Where(i => i.Length > 0).Distinct(StringComparer.Ordinal))
        {
            var nomes = b.Cadastro.NomesDoTerreno(ins);
            if (nomes.Count > 0) r[ins] = nomes[0] + (nomes.Count > 1 ? $" e outros {nomes.Count - 1}" : "");
        }
        return r;
    }

    static string Capitalizar(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Br) + s[1..].ToLower(Br);

    // ───────────────────────── fluxo ─────────────────────────

    /// <summary>Como o kit: o lote da base pela inscrição imobiliária (só os dígitos contam; havendo mais de um polígono, o maior).</summary>
    public static MapaGerado PorInscricao(BaseQgis b, string inscricao, OpcoesMapa? opcoes = null)
    {
        // a chave normalizada (Inscricoes): "0201041118001" no desenho é a 02010410118001 do cadastro
        string Chave(string? s) => Inscricoes.Normalizar(s) ?? new string((s ?? "").Where(char.IsAsciiDigit).ToArray());
        var dig = Chave(inscricao);
        var achados = (b[Camada.Lotes]?.Feicoes ?? []).Where(f => f.Geometria is Polygon or MultiPolygon
            && dig.Length > 0 && Chave(f.Atributo("InscImob")) == dig).ToList();
        if (achados.Count == 0) throw new ArgumentException($"Inscrição {inscricao} não encontrada em Lotes.shp.");
        var alvo = achados.MaxBy(f => Area(De(f).Partes))!;
        return Gerar(b, alvo.Geometria!, [alvo], opcoes);
    }

    internal static (string Zona, string Motivo) CruzarZoneamento(Geometry lote, IEnumerable<Fe> zonas)
    {
        try
        {
            var partes = new Dictionary<string, Geometry>();
            foreach (var z in zonas.Where(z => z.Tipo == 'A'))
            {
                if (!z.G.IsValid || !lote.IsValid) return ("", "Geometria inválida na base cartográfica");
                var inter = lote.Intersection(z.G);
                if (inter.Area <= 0) continue;
                var nome = z.Campo("ZONA");
                partes[nome] = partes.TryGetValue(nome, out var anterior) ? anterior.Union(inter) : inter;
            }
            if (partes.Count > 1) return ("", "Lote intersecta mais de uma zona");
            if (partes.Count == 1 && partes.First() is var par && par.Key.Length > 0 && par.Value.Covers(lote))
                return (par.Key, "Cobertura integral");
            return ("", "Cobertura cartográfica parcial ou sem zona identificada");
        }
        catch (TopologyException) { return ("", "Geometria inválida na base cartográfica"); }
    }

    /// <summary>O mapa e a ficha do lote já ligado ao cadastro pelo módulo Imóveis (traço fino enquanto o vínculo não for seguro).</summary>
    public static MapaGerado? DaAnalise(BaseQgis b, AnaliseImovel a, OpcoesMapa? opcoes = null) =>
        a.Lote is { } lote ? Gerar(b, lote, a.Lotes, (opcoes ?? new OpcoesMapa()) with { Candidato = !a.Conclusivo }) : null;

    /// <summary>O <c>gerar()</c> do script para um lote já escolhido: enquadramento, cruzamentos, balão, desenho e ficha.</summary>
    public static MapaGerado Gerar(BaseQgis b, Geometry lote, IReadOnlyList<Feicao> feicoes, OpcoesMapa? opcoes = null)
    {
        var o = opcoes ?? new OpcoesMapa();
        var alvo = De(feicoes[0].Indice, lote, feicoes[0]);
        if (alvo.Tipo != 'A') throw new ArgumentException("O lote não tem polígono na base.");
        var proprios = feicoes.Select(f => f.Indice).ToHashSet();
        var modo = o.Candidato ? "CANDIDATE_POINT" : "VERIFIED_LOT";
        const double fx = 0.60, fy = 0.58; // posição do lote no recorte (fração da largura / da altura, a partir do topo)
        const double W = Largura, H = Altura;

        var bb = alvo.Bb;
        var c = new Coordinate((bb.MinX + bb.MaxX) / 2, (bb.MinY + bb.MaxY) / 2);
        var lado = Math.Max(bb.Width, bb.Height);
        // com as cotas, o recorte mínimo cai de 200 para 120 m: o lote fica maior e as cotas dos lados cabem em letra normal
        var larg = new[] { o.Cotas ? 120.0 : 200.0, lado * 4.0, bb.Width / 0.34, bb.Height / 0.40 * W / H }.Max(); // lote inteiro, com folga

        var (cursos, appAtinge, auc, perimetro) = Condicionantes(b, alvo);
        var rio0 = cursos.Count > 0 ? cursos[0] : null;
        var br = FaixaDaBr262(b, alvo, o.FaixaDominio);
        var ver = new List<Coordinate>();
        // os pontos obrigatórios (2026-11-17): o eixo da BR-262 e a calha do curso d'água a até 100 m do lote, para as cotas aparecerem
        if (br is { DistanciaEixo: <= DistanciaCotas, PontoEixo: { } pe }) ver.Add(pe);
        if (rio0 is { Distancia: <= DistanciaCotas, PontoCalha: { } pc }) ver.Add(pc);
        var minima = larg;
        foreach (var p in ver) // cada ponto obrigatório dentro do recorte, a 8% da borda
        {
            double dx = p.X - c.X, dy = p.Y - c.Y;
            var lx = dx < 0 ? fx - .08 : 1 - fx - .08;
            var ly = dy < 0 ? 1 - fy - .08 : fy - .08;
            larg = Math.Max(larg, Math.Max(Math.Abs(dx) / lx, Math.Abs(dy) / ly * W / H));
        }
        var alt = larg * H / W;
        var ext = new Envelope(c.X - fx * larg, c.X + (1 - fx) * larg, c.Y - (1 - fy) * alt, c.Y + fy * alt);
        if (ver.Count > 0)
        {
            // Com o lote fixo a 60% / 58%, um ponto do lado curto abre muito o recorte e o lote fica pequeno (sem lugar para as cotas dos
            // lados). Alternativa: enquadrar o lote e os pontos juntos, centrados, a 8% da borda; fica a que der o recorte mais estreito.
            var u = bb.Copy();
            foreach (var p in ver) u.ExpandToInclude(p);
            var largU = Math.Max(minima, Math.Max(u.Width / .84, u.Height / .84 * W / H));
            if (largU < larg)
            {
                // o lote fica o mais perto possível da posição do kit (60% / 58%), desde que o lote e os pontos caibam a 8% da borda
                var altU = largU * H / W;
                var x0 = Math.Max(u.MaxX + .08 * largU - largU, Math.Min(c.X - fx * largU, u.MinX - .08 * largU));
                var y1 = Math.Max(u.MaxY + .08 * altU, Math.Min(c.Y + fy * altU, u.MinY - .08 * altU + altU));
                ext = new Envelope(x0, x0 + largU, y1 - altU, y1);
            }
        }

        var ins = alvo.Campo("InscImob");
        var somaAreas = feicoes.Sum(f => Numero(f.Atributo("AREA")) ?? 0);
        double? areaBase = somaAreas > 0 ? somaAreas : null;
        var areaGeo = Area(alvo.Partes);
        var cen = Centro(alvo.Partes);
        var numeros = Em(b, Camada.NumerosLote, alvo.Bb);
        var loteN = numeros.Where(n => Dentro(n.Partes[0][0], alvo.Partes)).Select(n => n.Campo("LOTE")).FirstOrDefault() ?? "";
        var pontoCen = new Envelope(cen);
        var quadra = Em(b, Camada.Quadras, pontoCen).Where(q => q.Tipo == 'A' && Dentro(cen, q.Partes)).Select(q => q.Campo("QUADRA")).FirstOrDefault() ?? "";
        var (zona, motivoZona) = CruzarZoneamento(lote, Em(b, Camada.Zoneamento, alvo.Bb));
        var envLot = alvo.Bb.Copy();
        envLot.ExpandToInclude(cen);
        var loteamento = Em(b, Camada.Loteamentos, envLot)
            .Where(n => (n.Tipo == 'A' && Dentro(cen, n.Partes)) || (n.Tipo == 'P' && Dentro(n.Partes[0][0], alvo.Partes))).Select(n => n.Campo("nome")).FirstOrDefault() ?? "";

        var edif = EdificacoesDoLote(b, alvo);
        var areaEdif = edif.Count > 0 ? Math.Round(edif.Sum(e => Numero(e.Campo("area", "AREA")) is { } v && v != 0 ? v : Area(e.Partes)), 2) : 0;
        var viz = o.ComDonos ? Vizinhos(b, alvo, proprios) : [];
        var donos = o.ComDonos ? Proprietarios(b, viz.Select(v => v.Campo("InscImob")).Prepend(ins)) : [];
        var proprietario = o.ComDonos ? o.Proprietario ?? donos.GetValueOrDefault(ins, "") : "";
        var cad = ins.Length > 0 ? b.Cadastro.PorInscricao(ins).FirstOrDefault() : null;
        var confrontantes = new List<VizinhoMapa>();
        foreach (var v in viz)
        {
            var vi = v.Campo("InscImob");
            var vn = Em(b, Camada.NumerosLote, v.Bb).Where(n => Dentro(n.Partes[0][0], v.Partes)).Select(n => n.Campo("LOTE")).FirstOrDefault() ?? "";
            confrontantes.Add(new VizinhoMapa(vi, vn, donos.GetValueOrDefault(vi, "")));
        }
        var rotulos = viz.Where(v => donos.ContainsKey(v.Campo("InscImob"))).Select(v => (v, Curto(donos[v.Campo("InscImob")]))).ToList();

        // o balão do lote
        var ficha = new List<string> { "LOTE " + (loteN.Length > 0 ? loteN : "s/ nº") + (quadra.Length > 0 ? $" · QUADRA {quadra}" : ""), $"Inscrição {ins}" };
        if (proprietario.Length > 0) ficha.Insert(1, Encurtar(proprietario, 44));
        var areas = string.Join(" · ", new[] { areaBase is > 0 ? $"base {Num(areaBase.Value)} m²" : $"calculada {Num(Area(alvo.Partes))} m²", o.AreaDeclarada is > 0 ? $"declarada {Num(o.AreaDeclarada.Value)} m²" : "" }.Where(x => x.Length > 0));
        ficha.Add(string.Join(" · ", new[] { zona.Length > 0 ? $"Zona {zona}" : "", areas }.Where(x => x.Length > 0)));
        if (appAtinge) ficha.Add("APP ATINGE O LOTE");
        // como o kit (atinge_dominio / atinge_nao_edificavel): o domínio do DNIT (< 40 m do eixo) é o caso mais grave
        if (br is { AtingeDominio: true }) ficha.Add("DOMÍNIO DNIT DA BR-262 ATINGE O LOTE");
        else if (br is { AtingeNaoEdificavel: true }) ficha.Add("FAIXA DA BR-262 ATINGE O LOTE");

        var nomeRio = o.NomeRio;
        var nomeInformado = nomeRio.Length > 0;
        if (!nomeInformado && rio0 is not null)
        {
            // calha separada do lote: o ponto da calha; rio cruzando o lote: os vértices DELE junto do lote (nunca o centro do lote)
            if (rio0.PontoCalha is not null) nomeRio = NomeCurso(b, rio0.PontoCalha);
            else
            {
                var jan = Expandido(alvo.Bb, 20);
                var h = b[Camada.HidroApp]?.Feicoes.FirstOrDefault(f => f.Indice == rio0.Feicao && f.Geometria is not null);
                var pts = h is null ? [] : De(h).Partes.SelectMany(l => l).Where(q => jan.Contains(q)).ToList();
                nomeRio = pts.Select(q => NomeCurso(b, q)).Where(n => n.Length > 0).GroupBy(n => n).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";
            }
        }

        var (svg, ruas, legenda) = MapaSvg.Desenhar(b, ext, alvo, modo, ficha, rotulos, rio0, nomeRio, br, o.Cotas, o.Cotas, o.Cotas);

        double? dif = null;
        if (o.AreaDeclarada is > 0 && (areaBase is > 0 || areaGeo > 0))
        {
            var refe = areaBase is > 0 ? areaBase.Value : Math.Round(areaGeo, 2);
            dif = Math.Round((o.AreaDeclarada.Value - refe) / refe * 100, 2);
        }

        var dados = new List<DadoLocalizacao>();
        void Dado(string grupo, string rotulo, string valor, string? chip = null, string? alerta = null, string? nota = null) =>
            dados.Add(new DadoLocalizacao(grupo, rotulo, valor, chip, chip is null ? null : alerta, string.IsNullOrEmpty(nota) ? null : nota));
        if (o.Endereco.Length > 0) Dado(GrupoId, "Endereço", o.Endereco);
        if (proprietario.Length > 0) Dado(GrupoId, "Proprietário", proprietario);
        Dado(GrupoId, "Inscrição", ins);
        Dado(GrupoId, "Lote / quadra", (loteN.Length > 0 ? loteN : "s/ nº") + (quadra.Length > 0 ? $" / {quadra}" : "") + (loteamento.Length > 0 ? $" — {loteamento}" : ""));
        if (areaBase is > 0) Dado(GrupoAreas, "Área na base", $"{Num(areaBase.Value)} m²");
        if (o.AreaDeclarada is > 0) Dado(GrupoAreas, "Área declarada", $"{Num(o.AreaDeclarada.Value)} m²" + (o.AreaFonte.Length > 0 ? $" ({o.AreaFonte})" : ""));
        if (dif is { } df)
            Dado(GrupoAreas, "Diferença", $"{(df > 0 ? "+" : "")}{Num(df)}% sobre a base", Math.Abs(df) > 5 ? "DIVERGE" : "CONFERE", Math.Abs(df) > 5 ? "a" : "g");
        const string indisponivel = "Camada indisponível na base consultada";
        Dado(GrupoAreas, "Edificação", b[Camada.Edificacoes] is null ? indisponivel :
            edif.Count > 0 ? $"{Num(areaEdif)} m²" + (edif.Count > 1 ? $" em {edif.Count} blocos" : "") : "Nenhuma cadastrada no lote");
        Dado(GrupoZona, "Zona (2025)", zona.Length > 0 ? zona : motivoZona);
        if (cursos.Count > 0)
        {
            var r0 = cursos[0];
            var largApp = r0.App.Count > 0 ? string.Join(" / ", r0.App.Select(x => $"{x} m")) : "não informada";
            var nome = nomeRio.Length > 0 ? nomeRio : Capitalizar(r0.Tipo);
            var larguraPendente = cursos.Any(curso => curso.App.Count == 0);
            Dado(GrupoZona, "APP / rio", $"{nome} · calha a {Num(r0.Distancia, 1)} m · APP de {largApp} na base",
                appAtinge ? "ATINGE O LOTE" : larguraPendente ? "FAIXA A CONFERIR" : "NÃO ATINGE", appAtinge ? "r" : larguraPendente ? "a" : "g",
                nomeInformado || nomeRio.Length == 0 ? null : "Nome pela AGERH/GEOBASES-ES — conferir.");
        }
        else if (b[Camada.HidroApp] is null) Dado(GrupoZona, "APP / rio", indisponivel);
        else Dado(GrupoZona, "APP / rio", "Nenhum curso d'água na base num raio de 250 m", "NÃO ATINGE", "g");
        if (br is not null)
            Dado(GrupoZona, "BR-262", $"Eixo a {Num(br.DistanciaEixo, 1)} m · domínio {Num(br.Dominio, 0)} m + {Num(br.NaoEdificavel, 0)} m não edificáveis",
                br.AtingeNaoEdificavel ? "ATINGE O LOTE" : "NÃO ATINGE", br.AtingeNaoEdificavel ? "r" : "g",
                "Domínio a confirmar no DNIT; faixa não edificável: Lei 6.766/1979, art. 4º, III.");
        Dado(GrupoZona, "AUC", b[Camada.Auc] is null ? indisponivel : auc.Length > 0 ? $"Sim — AUC {auc}" : "Não (fora das AUCs da base)");
        Dado(GrupoZona, "Perímetro", b[Camada.PerimetroUrbano] is null ? indisponivel : perimetro.Length > 0 ? perimetro : "Fora do perímetro urbano de 2022");
        if (cad is not null)
        {
            var matricula = cad.Campo("matricula")?.Trim() ?? "";
            Dado(GrupoCadastro, "Situação", string.Join(" · ", new[] { cad.Situacao.Trim(), Capitalizar((cad.Campo("ocupacao") ?? "").Trim()) }.Where(x => x.Length > 0))
                + (matricula.Length > 0 ? $" · matrícula {matricula}" : ""));
        }
        Dado(GrupoCadastro, "Parcelamento", b[Camada.Loteamentos] is null ? indisponivel : loteamento.Length > 0 ? $"Loteamento aprovado {loteamento}" : "Fora dos loteamentos da base",
            nota: "Indício; a regularidade se comprova pela matrícula.");
        if (confrontantes.Count > 0)
            Dado(GrupoCadastro, "Confrontantes", string.Join(" · ", confrontantes.Select(v =>
                $"Lote {(v.Lote.Length > 0 ? v.Lote : "s/ nº")}: {(v.Proprietario.Length > 0 ? v.Proprietario : "sem nome no cadastro")}")));
        if (ruas.Count > 0) Dado(GrupoCadastro, "Ruas", string.Join(" · ", ruas.Take(4).Select(NomeRua)));

        Camada[] usadas = [Camada.Lotes, Camada.Logradouros, Camada.Zoneamento, Camada.Quadras, Camada.NumerosLote, Camada.Edificacoes, Camada.HidroApp, Camada.Auc, Camada.PerimetroUrbano, Camada.CursosAgerh, Camada.Loteamentos];
        return new MapaGerado
        {
            Svg = svg, Modo = modo, Extensao = ext, Legenda = legenda, Dados = dados, Ruas = ruas, Ficha = ficha,
            Inscricao = ins, Lote = loteN, Quadra = quadra, Zona = zona, Loteamento = loteamento, AreaBase = areaBase, AreaGeometria = Math.Round(areaGeo, 2),
            CursosDagua = cursos, AppAtingeLote = appAtinge, Auc = auc, PerimetroUrbano = perimetro, Br262 = br, Edificacoes = edif.Count, AreaEdificada = areaEdif,
            Proprietario = proprietario, NomeRio = nomeRio, Confrontantes = confrontantes, DiferencaAreaPct = dif,
            Camadas = usadas.Where(k => b[k] is not null).Select(k => Path.GetFileNameWithoutExtension(b[k]!.Fonte.Caminho)).ToList(),
        };
    }
}
