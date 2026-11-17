using System.Globalization;

namespace Pmvni.Composicao.Kit;

/// <summary>Diagrama vetorial (planta e volumetria) desenhado a partir dos rótulos dos dados
/// (<c>planta_svg</c>, <c>volumetria_svg</c> e <c>montar_diagrama</c> do gerar_relatorio.py do kit).</summary>
public static class Diagrama
{
    public static IReadOnlyDictionary<string, (int X, int Y)> PosPlanta { get; } = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal)
    {
        ["lote"] = (286, 270), ["permeavel"] = (60, 58), ["edificacao"] = (118, 212), ["lateral"] = (298, 140),
    };

    public static IReadOnlyDictionary<string, (int X, int Y)> PosVolume { get; } = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal)
    {
        ["telhado"] = (184, 76), ["altura"] = (274, 150),
    };

    /// <summary>Âncoras da volumetria além das do kit (correções 2026-11-16): as chamadas que o usuário acrescenta na revisão podem marcar
    /// também a frente do lote (o afastamento frontal, no chão diante da fachada). <see cref="PosVolume"/> continua igual ao kit.</summary>
    public static IReadOnlyDictionary<string, (int X, int Y)> PosVolumeExtra { get; } = new Dictionary<string, (int X, int Y)>(StringComparer.Ordinal)
    {
        ["frente"] = (226, 228),
        // Correção 2026-11-17: afastamentos laterais (muro, divisa) no chão ao lado do prédio, um de cada lado.
        ["lateral_direita"] = (288, 199),
        ["lateral_esquerda"] = (60, 208),
    };

    /// <summary>Todas as âncoras da volumetria: as do kit e as extras.</summary>
    public static IReadOnlyDictionary<string, (int X, int Y)> PosVolumeTodas { get; } =
        PosVolume.Concat(PosVolumeExtra).ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);

    // ---------- vista de cada chamada (correção 2026-11-17) ----------
    // Antes, o desenho e a coluna de uma chamada saíam só da âncora: uma chamada pedida na volumetria ("afastamento do muro") com a
    // âncora "lateral" (que é da planta) ia para a coluna da PLANTA, com a marca no desenho da planta. Agora a chamada acrescentada
    // leva a "vista" explícita (planta ou volumetria); a coluna e o desenho seguem a vista, e a âncora é resolvida dentro dela.

    public const string VistaPlanta = "planta";
    public const string VistaVolumetria = "volumetria";

    /// <summary>"planta"/"volumetria" (aceita variações: "Planta do terreno", "volume", "VOLUMETRIA DA EDIFICAÇÃO"); null se não é vista.</summary>
    public static string? NormalizarVista(object? vista)
    {
        if (vista is not string s) return null;
        var t = s.Trim().ToLowerInvariant();
        if (t.StartsWith("planta", StringComparison.Ordinal)) return VistaPlanta;
        if (t.StartsWith("volum", StringComparison.Ordinal)) return VistaVolumetria;
        return null;
    }

    /// <summary>As posições das âncoras da vista.</summary>
    public static IReadOnlyDictionary<string, (int X, int Y)> Posicoes(string vista) =>
        vista == VistaPlanta ? PosPlanta : PosVolumeTodas;

    /// <summary>A vista de uma âncora pelo nome (o comportamento do kit, para as chamadas sem vista); null se desconhecida.</summary>
    public static string? VistaDaAncora(string? ancora) =>
        ancora is null ? null : PosPlanta.ContainsKey(ancora) ? VistaPlanta : PosVolumeTodas.ContainsKey(ancora) ? VistaVolumetria : null;

    /// <summary>Âncoras de outra vista (ou nomes próximos) que têm equivalente nesta vista.</summary>
    private static readonly Dictionary<(string Vista, string Ancora), string> Equivalentes = new()
    {
        [(VistaVolumetria, "lateral")] = "lateral_direita",
        [(VistaVolumetria, "lateral_dir")] = "lateral_direita",
        [(VistaVolumetria, "direita")] = "lateral_direita",
        [(VistaVolumetria, "esquerda")] = "lateral_esquerda",
        [(VistaVolumetria, "beiral")] = "telhado",
        [(VistaVolumetria, "cobertura")] = "telhado",
        [(VistaVolumetria, "edificacao")] = "altura",
        [(VistaVolumetria, "lote")] = "frente",
        [(VistaPlanta, "lateral_direita")] = "lateral",
        [(VistaPlanta, "lateral_esquerda")] = "lateral",
        [(VistaPlanta, "telhado")] = "edificacao",
        [(VistaPlanta, "altura")] = "edificacao",
        [(VistaPlanta, "frente")] = "lote",
    };

    /// <summary>Ordem em que se procura um ponto livre quando a âncora pedida não serve na vista.</summary>
    private static readonly Dictionary<string, string[]> OrdemLivre = new(StringComparer.Ordinal)
    {
        [VistaPlanta] = ["lateral", "lote", "edificacao", "permeavel"],
        [VistaVolumetria] = ["lateral_direita", "lateral_esquerda", "frente", "altura", "telhado"],
    };

    /// <summary>A âncora da chamada dentro da <paramref name="vista"/>: a pedida, se é dessa vista; senão a equivalente nela ("lateral"
    /// na volumetria = "lateral_direita"); senão o primeiro ponto da vista que nenhuma outra chamada (<paramref name="usadas"/>) ocupa;
    /// se todos estão ocupados, o primeiro (a marca repetida sai deslocada).</summary>
    public static string ResolverAncora(string vista, string? ancora, IEnumerable<string> usadas)
    {
        var pos = Posicoes(vista);
        var a = (ancora ?? "").Trim().ToLowerInvariant();
        if (pos.ContainsKey(a)) return a;
        if (Equivalentes.TryGetValue((vista, a), out var eq)) return eq;
        var ocupadas = new HashSet<string>(usadas, StringComparer.Ordinal);
        var ordem = OrdemLivre[vista];
        return ordem.FirstOrDefault(o => !ocupadas.Contains(o)) ?? ordem[0];
    }

    private static string E(object? t) => Dados.E(t);

    private static string Mk(int x, int y, int n) =>
        $"""<g class="mk"><circle cx="{x}" cy="{y}" r="8.5"/><text x="{x}" y="{PythonTexto.Str(y + 3.2)}">{n}</text></g>""";

    /// <summary>As marcas numeradas; duas chamadas no mesmo ponto não se cobrem: a repetida sai 20 px ao lado.</summary>
    private static string Marcadores(IEnumerable<(string Ancora, int Numero)> marcas, IReadOnlyDictionary<string, (int X, int Y)> pos)
    {
        var vezes = new Dictionary<string, int>(StringComparer.Ordinal);
        var sb = new System.Text.StringBuilder();
        foreach (var m in marcas)
        {
            vezes.TryGetValue(m.Ancora, out var k);
            vezes[m.Ancora] = k + 1;
            var (x, y) = pos[m.Ancora];
            sb.Append(Mk(x + 20 * k, y, m.Numero));
        }
        return sb.ToString();
    }

    private static object? Elem(object? lista, int i)
    {
        var l = Dados.Lista(lista);
        return i < l.Count ? l[i] : throw new MoldeErro("list index out of range");
    }

    /// <summary>Texto de <c>x[1] if len(x) &gt; 1 else ''</c>.</summary>
    private static string Segundo(object? lista) => Dados.Len(lista) > 1 ? E(Elem(lista, 1)) : "";

    public static string PlantaSvg(IReadOnlyDictionary<string, object?> p, IEnumerable<(string Ancora, int Numero)> marcas)
    {
        var conf = PythonTexto.Verdadeiro(Dados.Obter(p, "limites_confirmados"));
        var borda = conf ? "" : " stroke-dasharray=\"6 4\"";
        var cor = conf ? "#2F8F5B" : "#E2AE1C";
        var pm = Dados.Exigir(p, "permeavel");
        var ed = Dados.Exigir(p, "edificacao");
        var la = Dados.Exigir(p, "lateral");
        var linhas = new[]
        {
            """<svg viewBox="0 0 330 332" class="dg">""",
            """<defs><pattern id="dots" width="7" height="7" patternUnits="userSpaceOnUse"><circle cx="3.5" cy="3.5" r=".8" fill="#8CCB9E"/></pattern>""",
            """<marker id="ar" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M0 1L9 5L0 9z" fill="#6B7785"/></marker></defs>""",
            """<rect x="0" y="294" width="330" height="34" rx="6" fill="#E6EAEF"/><line x1="10" y1="311" x2="320" y2="311" stroke="#fff" stroke-width="2" stroke-dasharray="12 9"/>""",
            $"""<text x="165" y="306" class="street">{E(Dados.Obter(p, "via", "RUA"))}</text>""",
            $"""<rect x="48" y="46" width="250" height="234" rx="8" fill="#EEF7F1"/><rect x="48" y="46" width="250" height="234" rx="8" fill="url(#dots)" stroke="{cor}" stroke-width="1.6"{borda}/>""",
            $"""<line x1="48" y1="27" x2="298" y2="27" class="dim" marker-start="url(#ar)" marker-end="url(#ar)"/><text x="173" y="20" class="t7 mu">{E(Dados.Exigir(p, "testada"))}</text>""",
            $"""<line x1="28" y1="46" x2="28" y2="280" class="dim" marker-start="url(#ar)" marker-end="url(#ar)"/><text x="20" y="163" class="t7 mu" transform="rotate(-90 20 163)">{E(Dados.Exigir(p, "profundidade"))}</text>""",
            """<rect x="60" y="58" width="112" height="40" rx="7" fill="#fff" stroke="#CFE8D7"/>""",
            $"""<text x="116" y="72" class="t7 b gr">{E(Dados.Exigir(pm, "titulo"))}</text><text x="116" y="82.5" class="t65">{E(Dados.Exigir(pm, "linha1"))}</text><text x="116" y="92" class="t65{(PythonTexto.Verdadeiro(Dados.Obter(pm, "linha2_destaque")) ? " or" : "")}">{E(Dados.Obter(pm, "linha2", ""))}</text>""",
            """<rect x="112" y="114" width="116" height="104" rx="4" fill="none" stroke="#9AA6B3" stroke-dasharray="3 3"/>""",
            $"""<text x="228" y="109" class="t6 mu" text-anchor="end">{E(Dados.Obter(p, "beiral", ""))}</text>""",
            """<rect x="118" y="120" width="104" height="92" rx="3" fill="#DDE4EB" stroke="#5B6878" stroke-width="1.1"/>""",
            $"""<text x="170" y="162" class="t7 b">{E(Elem(ed, 0))}</text><text x="170" y="174" class="t65 mu">{Segundo(ed)}</text>""",
            """<line x1="228" y1="166" x2="298" y2="166" class="dim" marker-start="url(#ar)" marker-end="url(#ar)"/>""",
            $"""<text x="263" y="159" class="t65 b">{E(Elem(la, 0))}</text><text x="263" y="180" class="t6 mu">{Segundo(la)}</text>""",
            $"""<line x1="170" y1="218" x2="170" y2="280" class="dsh"/><text x="176" y="254" class="t6 mu" text-anchor="start">{E(Dados.Exigir(p, "frente"))}</text>""",
            $"""<line x1="200" y1="46" x2="200" y2="114" class="dsh"/><text x="206" y="76" class="t6 mu" text-anchor="start">{E(Dados.Exigir(p, "fundos"))}</text>""",
            Marcadores(marcas, PosPlanta),
            "</svg>",
        };
        return string.Join("\n", linhas);
    }

    private static string Poly((double X, double Y)[] pts, string c) =>
        "<polygon class=\"" + c + "\" points=\"" + string.Join(' ', pts.Select(p => Dados.F1(p.X) + "," + Dados.F1(p.Y))) + "\"/>";

    private static (double, double) P(double u, double z) => (150 + 103.9 * u, 250 - 60 * u - z);

    private static (double, double) L(double s, double z) => (150 - 77.9 * s, 250 - 45 * s - z);

    private static string Win(Func<double, double, (double, double)> f, double a0, double a1, double z0, double z1, string c = "win") =>
        Poly(new[] { f(a0, z0), f(a1, z0), f(a1, z1), f(a0, z1) }, c);

    /// <summary>Correção 2026-11-16: a volumetria aceita, além das alturas, a forma (opcional, como texto): <c>pavimentos</c> (1 a 6;
    /// padrão 2), <c>telhado</c> ("duas águas" = padrão; "sem", "plano", "laje" = cobertura plana) e <c>destaque</c> ("nenhum" = padrão;
    /// senão o texto sai em vermelho embaixo, e a parte citada — lateral direita, lateral esquerda, telhado, altura, frente — fica
    /// marcada em vermelho). Sem essas chaves o desenho é o mesmo de sempre.</summary>
    public static string VolumetriaSvg(IReadOnlyDictionary<string, object?> v, IEnumerable<(string Ancora, int Numero)> marcas)
    {
        if (!v.ContainsKey("pavimentos") && !v.ContainsKey("telhado") && !v.ContainsKey("destaque")) return VolumetriaOriginal(v, marcas);
        var pav = int.TryParse(Dados.Obter(v, "pavimentos", "2") as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var np) ? Math.Clamp(np, 1, 6) : 2;
        var telhadoTexto = (Dados.Obter(v, "telhado", "duas águas") as string ?? "").Trim().ToLowerInvariant();
        var comTelhado = !(telhadoTexto.StartsWith("sem", StringComparison.Ordinal) || telhadoTexto.Contains("plan", StringComparison.Ordinal) || telhadoTexto.Contains("laje", StringComparison.Ordinal));
        var destaque = (Dados.Obter(v, "destaque", "nenhum") as string ?? "").Trim();
        var dl = destaque.ToLowerInvariant();
        var temDestaque = dl.Length > 0 && dl != "nenhum";
        bool Fala(params string[] chaves) => temDestaque && chaves.Any(c => dl.Contains(c, StringComparison.Ordinal));
        var dDir = Fala("direit"); var dEsq = Fala("esquerd"); var dTel = Fala("telhad", "cobert"); var dAlt = Fala("altur", "gabarit"); var dFre = Fala("frent", "recuo");

        // Altura de cada pavimento: 60 até 2; acima disso o prédio cresce até 200 no total.
        var h = pav <= 2 ? 60.0 : Math.Max(33.0, 200.0 / pav);
        var H = h * pav;
        var cume = comTelhado ? 30.0 : 0.0;
        (double, double) Q(double u, double sp, double z) => (150 + 103.9 * u - 77.9 * sp, 250 - 60 * u - 45 * sp - z);
        string Pl((double, double)[] pts, string c, string extra = "") =>
            "<polygon class=\"" + c + "\"" + extra + " points=\"" + string.Join(' ', pts.Select(t => Dados.F1(t.Item1) + "," + Dados.F1(t.Item2))) + "\"/>";
        const string Vermelho = " style=\"stroke:#D64545;stroke-width:2.4\"";

        var w = new System.Text.StringBuilder();
        for (var k = 0; k < pav; k++)
        {
            var z0 = h * k;
            double Z(double rel) => z0 + rel * h / 60.0;
            var frenteJanelas = k == 0
                ? new (double, double, double, double)[] { (.12, .32, 18, 42), (.65, .85, 18, 42) }
                : new (double, double, double, double)[] { (.12, .32, 18, 42), (.42, .58, 18, 42), (.65, .85, 18, 42) };
            foreach (var q in frenteJanelas) w.Append(Win(P, q.Item1, q.Item2, Z(q.Item3), Z(q.Item4)));
            if (k == 0) w.Append(Win(P, .42, .53, 0, Z(44), "door"));
            foreach (var q in new (double, double, double, double)[] { (.18, .42, 20, 42), (.58, .82, 20, 42) })
                w.Append(Win(L, q.Item1, q.Item2, Z(q.Item3), Z(q.Item4)));
        }
        var pisos = new System.Text.StringBuilder();
        for (var k = 1; k < pav; k++)
        {
            var (ax, ay) = Q(0, 0, h * k); var (bx, by) = Q(1, 0, h * k); var (cx, cy) = Q(0, 1, h * k);
            pisos.Append($"""<line x1="{Dados.F1(ax)}" y1="{Dados.F1(ay)}" x2="{Dados.F1(bx)}" y2="{Dados.F1(by)}" class="fl"/><line x1="{Dados.F1(ax)}" y1="{Dados.F1(ay)}" x2="{Dados.F1(cx)}" y2="{Dados.F1(cy)}" class="fl"/>""");
        }

        // Paredes: a esquerda leva a empena (o triângulo do telhado) quando há telhado de duas águas.
        var paredeEsq = comTelhado
            ? new[] { Q(0, 0, 0), Q(0, 1, 0), Q(0, 1, H), Q(0, .5, H + cume), Q(0, 0, H) }
            : new[] { Q(0, 0, 0), Q(0, 1, 0), Q(0, 1, H), Q(0, 0, H) };
        var paredeDir = new[] { Q(0, 0, 0), Q(1, 0, 0), Q(1, 0, H), Q(0, 0, H) };
        var cobertura = comTelhado
            ? Pl(new[] { Q(0, 1, H), Q(1, 1, H), Q(1, .5, H + cume), Q(0, .5, H + cume) }, "rfb", dTel ? Vermelho : "")
              + Pl(new[] { Q(0, 0, H), Q(1, 0, H), Q(1, .5, H + cume), Q(0, .5, H + cume) }, "rff", dTel ? Vermelho : "")
            : Pl(new[] { Q(0, 0, H), Q(1, 0, H), Q(1, 1, H), Q(0, 1, H) }, "rff", dTel ? Vermelho : "");

        var ae = Dados.Exigir(v, "altura_esquerda");
        var ad = Dados.Exigir(v, "altura_direita");
        var topo = Q(1, .5, H + cume).Item2;
        var y0 = Math.Min(0, Math.Floor(topo - 14));
        var altE1 = 212 - H - cume; var altD1 = 192 - H - cume - 12;
        var corDim = dAlt ? " style=\"stroke:#D64545\"" : "";
        var frente = dFre
            ? Pl(new[] { Q(0, 0, 0), Q(1, 0, 0), Q(1.25, -.35, 0), Q(-.1, -.35, 0) }, "gnd", " style=\"fill:#F6D5D5;stroke:#D64545;stroke-width:2\"")
            : "";
        var aviso = temDestaque
            ? $"""<text x="150" y="314" class="t7 b" style="fill:#C03535" text-anchor="middle">{E(destaque)}</text>"""
            : "";
        var posMarcas = new Dictionary<string, (int X, int Y)>(PosVolumeExtra, StringComparer.Ordinal)
        {
            ["telhado"] = (184, (int)Math.Round(76 - (H - 120) - (30 - cume))),
            ["altura"] = (274, (int)Math.Round(150 - (H - 120) / 2)),
        };
        var linhas = new[]
        {
            $"""<svg viewBox="-22 {Dados.F1(y0)} 344 {Dados.F1(322 - y0)}" class="dg">""",
            """<defs><marker id="ar2" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M0 1L9 5L0 9z" fill="#6B7785"/></marker></defs>""",
            Poly(new[] { (150d, 285d), (301.6, 197.5), (180.4, 127.5), (28.8, 215d) }, "gnd") + Poly(new[] { (150d, 285d), (28.8, 215d), (-5.8, 235d), (115.4, 305d) }, "str") + frente,
            $"""<text x="52" y="266" class="street" transform="rotate(30 52 266)">{E(Dados.Obter(v, "via", "RUA"))}</text>""",
            Pl(paredeEsq, "wl", dEsq ? Vermelho : ""),
            Pl(paredeDir, "wr", dDir ? Vermelho : "") + cobertura,
            pisos.ToString() + w,
            $"""<line x1="44" y1="212" x2="44" y2="{Dados.F1(altE1)}" class="dim"{corDim} marker-start="url(#ar2)" marker-end="url(#ar2)"/>""",
            $"""<text x="36" y="{Dados.F1((212 + altE1) / 2 - 5)}" class="t7 b" text-anchor="end">{E(Elem(ae, 0))}</text><text x="36" y="{Dados.F1((212 + altE1) / 2 + 6)}" class="t7" text-anchor="end">{Segundo(ae)}</text>""",
            $"""<line x1="274" y1="192" x2="274" y2="{Dados.F1(altD1)}" class="dim"{corDim} marker-start="url(#ar2)" marker-end="url(#ar2)"/>""",
            $"""<text x="281" y="{Dados.F1((192 + altD1) / 2 - 7)}" class="t7 b" text-anchor="start">{E(Elem(ad, 0))}</text><text x="281" y="{Dados.F1((192 + altD1) / 2 + 4)}" class="t7" text-anchor="start">{Segundo(ad)}</text>""",
            Marcadores(marcas, posMarcas),
            aviso,
            "</svg>",
        };
        return string.Join("\n", linhas.Where(l => l.Length > 0));
    }

    static string VolumetriaOriginal(IReadOnlyDictionary<string, object?> v, IEnumerable<(string Ancora, int Numero)> marcas)
    {
        var w = string.Concat(new (double, double, double, double)[]
        {
            (.12, .32, 18, 42), (.65, .85, 18, 42), (.12, .32, 78, 102), (.42, .58, 78, 102), (.65, .85, 78, 102),
        }.Select(q => Win(P, q.Item1, q.Item2, q.Item3, q.Item4)));
        w += Win(P, .42, .53, 0, 44, "door") + string.Concat(new (double, double, double, double)[]
        {
            (.18, .42, 20, 42), (.58, .82, 20, 42), (.18, .42, 80, 102), (.58, .82, 80, 102),
        }.Select(q => Win(L, q.Item1, q.Item2, q.Item3, q.Item4)));
        var ae = Dados.Exigir(v, "altura_esquerda");
        var ad = Dados.Exigir(v, "altura_direita");
        var linhas = new[]
        {
            """<svg viewBox="-22 0 344 318" class="dg">""",
            """<defs><marker id="ar2" viewBox="0 0 10 10" refX="5" refY="5" markerWidth="5" markerHeight="5" orient="auto-start-reverse"><path d="M0 1L9 5L0 9z" fill="#6B7785"/></marker></defs>""",
            Poly(new[] { (150d, 285d), (301.6, 197.5), (180.4, 127.5), (28.8, 215d) }, "gnd") + Poly(new[] { (150d, 285d), (28.8, 215d), (-5.8, 235d), (115.4, 305d) }, "str"),
            $"""<text x="52" y="266" class="street" transform="rotate(30 52 266)">{E(Dados.Obter(v, "via", "RUA"))}</text>""",
            Poly(new[] { (72.1, 85d), (176d, 25d), (214.95, 17.5), (111.05, 77.5) }, "rfb") + Poly(new[] { (150d, 250d), (72.1, 205d), (72.1, 85d), (111.05, 77.5), (150d, 130d) }, "wl"),
            Poly(new[] { (150d, 250d), (253.9, 190d), (253.9, 70d), (150d, 130d) }, "wr") + Poly(new[] { (150d, 130d), (253.9, 70d), (214.95, 17.5), (111.05, 77.5) }, "rff"),
            """<line x1="150" y1="190" x2="253.9" y2="130" class="fl"/><line x1="150" y1="190" x2="72.1" y2="145" class="fl"/>""" + w,
            """<line x1="44" y1="212" x2="44" y2="62" class="dim" marker-start="url(#ar2)" marker-end="url(#ar2)"/>""",
            $"""<text x="36" y="132" class="t7 b" text-anchor="end">{E(Elem(ae, 0))}</text><text x="36" y="143" class="t7" text-anchor="end">{Segundo(ae)}</text>""",
            """<line x1="274" y1="192" x2="274" y2="30" class="dim" marker-start="url(#ar2)" marker-end="url(#ar2)"/>""",
            $"""<text x="281" y="104" class="t7 b" text-anchor="start">{E(Elem(ad, 0))}</text><text x="281" y="115" class="t7" text-anchor="start">{Segundo(ad)}</text>""",
            Marcadores(marcas, PosVolumeTodas),
            "</svg>",
        };
        return string.Join("\n", linhas);
    }

    /// <summary>montar_diagrama(d): numera as chamadas, separa por âncora (planta/volumetria) e desenha os dois SVGs.</summary>
    public static Dictionary<string, object?> MontarDiagrama(IReadOnlyDictionary<string, object?> d)
    {
        var planta = new List<(string, int)>();
        var volume = new List<(string, int)>();
        var cp = new List<object?>();
        var cv = new List<object?>();
        var n = 0;
        foreach (var item in Dados.Lista(Dados.Exigir(d, "chamadas")))
        {
            n++;
            var a = Dados.Exigir(item, "ancora");
            var c = new Dictionary<string, object?>();
            foreach (var par in Dados.Dict(item)) c[par.Key] = par.Value;
            c["numero"] = (long)n;
            // Chamada com vista explícita (as acrescentadas na revisão): a vista manda na coluna e no desenho; a âncora vale dentro dela.
            if (NormalizarVista(Dados.Obter(item, "vista")) is { } vista)
            {
                var marcas = vista == VistaPlanta ? planta : volume;
                var ancora = ResolverAncora(vista, a as string, marcas.Select(m => m.Item1));
                c["ancora"] = ancora;
                c["vista"] = vista;
                marcas.Add((ancora, n));
                (vista == VistaPlanta ? cp : cv).Add(c);
            }
            else if (a is string s && PosPlanta.ContainsKey(s)) { planta.Add((s, n)); cp.Add(c); }
            else if (a is string s2 && PosVolumeTodas.ContainsKey(s2)) { volume.Add((s2, n)); cv.Add(c); }
            else
                throw new MoldeErro("Âncora desconhecida no diagrama: " + PythonTexto.Repr(a) + " (use " +
                                    string.Join(", ", PosPlanta.Keys.Concat(PosVolume.Keys)) + ")");
            var sit = Dados.Exigir(c, "situacao");
            if (sit is not string ss || !Situacoes.SitIndice.Contains(ss))
                throw new MoldeErro("Situação inválida na chamada " + n.ToString(CultureInfo.InvariantCulture) + ": " + PythonTexto.Repr(sit));
        }
        var r = new Dictionary<string, object?>();
        foreach (var par in d) r[par.Key] = par.Value;
        r["planta_svg"] = new Seguro(PlantaSvg(Dados.Dict(Dados.Exigir(d, "planta")), planta));
        r["volumetria_svg"] = new Seguro(VolumetriaSvg(Dados.Dict(Dados.Exigir(d, "volumetria")), volume));
        r["chamadas_planta"] = cp;
        r["chamadas_volumetria"] = cv;
        return r;
    }
}
