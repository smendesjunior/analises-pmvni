using System.Text;
using System.Text.RegularExpressions;

namespace Pmvni.Legislacao;

/// <summary>Os tipos de bloco da leitura padronizada de uma lei (correções 2026-11-15).</summary>
public enum TipoBlocoLei { Epigrafe, Ementa, Titulo, Capitulo, Secao, Subsecao, Artigo, Paragrafo, Inciso, Alinea, Texto, Figura }

/// <summary>Um bloco da leitura padronizada: o <see cref="Rotulo"/> ("Art. 1º", "§ 2º", "TÍTULO I"…) e o texto que segue. Num
/// <see cref="TipoBlocoLei.Figura"/>, <see cref="Figura"/> é a imagem recortada do PDF, no ponto do texto em que ela aparece.</summary>
public sealed record BlocoLei(TipoBlocoLei Tipo, string Rotulo, string Texto, int Pagina, FiguraPdf? Figura = null)
{
    /// <summary>Artigo: a chave do resumo escrito pelo usuário (o rótulo; "Art. 1º (2)" na segunda vez que o mesmo rótulo aparece).</summary>
    public string Chave { get; init; } = "";

    /// <summary>Artigo: o resumo automático (<see cref="LeituraPadronizada.Resumir"/>), tirado do próprio texto do artigo.</summary>
    public string Resumo { get; init; } = "";
}

/// <summary>Processador interno da legislação importada (correções 2026-11-15): do texto extraído do PDF, página a página, monta a leitura
/// padronizada (epígrafe, ementa, títulos, capítulos, seções, artigos, parágrafos, incisos e alíneas), juntando as linhas quebradas pelo PDF
/// e tirando cabeçalhos e rodapés que se repetem nas páginas. Não muda o PDF: é só outra forma de ler o mesmo texto.</summary>
public static partial class LeituraPadronizada
{
    [GeneratedRegex(@"^(LEI(\s+COMPLEMENTAR|\s+ORDIN[ÁA]RIA)?|DECRETO(-LEI)?|RESOLU[ÇC][ÃA]O|PORTARIA|INSTRU[ÇC][ÃA]O\s+NORMATIVA|EMENDA)\b.*\b(N[º°O.]|N\.º)\s*[\d.]+.*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Epigrafe();

    [GeneratedRegex(@"^(T[ÍI]TULO|CAP[ÍI]TULO|SE[ÇC][ÃA]O|SUBSE[ÇC][ÃA]O)\s+([IVXLCDM]+|[ÚU]NIC[OA])\b\s*[-–—.:]?\s*(.*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Divisao();

    [GeneratedRegex(@"^(Art(?:igo)?\.?\s*\d+(?:\s*[º°o]|\.)?(?:-[A-Z])?)\s*[-–—.]?\s*(.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex Artigo();

    [GeneratedRegex(@"^((?:§\s*\d+\s*[º°o]?(?:-[A-Z])?)|Par[áa]grafo\s+[úu]nico)\s*[-–—.:]?\s*(.*)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Paragrafo();

    [GeneratedRegex(@"^([IVXLCDM]+)\s*[-–—)]\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Inciso();

    [GeneratedRegex(@"^([a-z])\)\s*(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex Alinea();

    [GeneratedRegex(@"^\s*(p[áa]g(ina)?\.?\s*)?\d+(\s*(de|/)\s*\d+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NumeroDePagina();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Espacos();

    /// <summary>Monta os blocos a partir do texto de cada página (a 1ª página é a 1). Com <paramref name="figuras"/>, cada figura entra
    /// logo depois do bloco em que está a linha dela (<see cref="FiguraPdf.Linha"/>): entre dois artigos, fica entre eles; no meio de um
    /// parágrafo, logo depois dele. Figuras repetidas no mesmo lugar em mais da metade das páginas (brasão do cabeçalho, por exemplo) saem.</summary>
    public static IReadOnlyList<BlocoLei> Estruturar(IReadOnlyList<string> paginas, IReadOnlyList<FiguraPdf>? figuras = null)
    {
        ArgumentNullException.ThrowIfNull(paginas);
        var repetidas = LinhasRepetidas(paginas);
        var porPagina = FigurasPorPagina(figuras ?? [], paginas.Count);
        var pendentes = new List<FiguraPdf>();
        var blocos = new List<BlocoLei>();
        var vezes = new Dictionary<string, int>(StringComparer.Ordinal);
        TipoBlocoLei? atual = null;
        string rotulo = "";
        var texto = new StringBuilder();
        var paginaDoBloco = 1;
        var viuArtigo = false;
        var viuEpigrafe = false;

        void Fechar()
        {
            if (atual is { } t && (rotulo.Length > 0 || texto.Length > 0))
            {
                var corpo = Espacos().Replace(texto.ToString(), " ").Trim();
                var bloco = new BlocoLei(t, rotulo, corpo, paginaDoBloco);
                if (t == TipoBlocoLei.Artigo)
                {
                    var n = vezes[rotulo] = vezes.GetValueOrDefault(rotulo) + 1;
                    bloco = bloco with { Chave = n == 1 ? rotulo : $"{rotulo} ({n})", Resumo = Resumir(corpo) };
                }
                blocos.Add(bloco);
            }
            atual = null;
            rotulo = "";
            texto.Clear();
            foreach (var f in pendentes) blocos.Add(BlocoDaFigura(f));
            pendentes.Clear();
        }

        void Enfileirar(int pagina, int ateLinha)
        {
            if (!porPagina.TryGetValue(pagina, out var fila)) return;
            while (fila.Count > 0 && fila.Peek().Linha <= ateLinha) pendentes.Add(fila.Dequeue());
        }

        void Abrir(TipoBlocoLei t, string r, string resto, int pagina)
        {
            Fechar();
            atual = t;
            rotulo = Espacos().Replace(r, " ").Trim();
            texto.Append(resto);
            paginaDoBloco = pagina;
        }

        for (var p = 0; p < paginas.Count; p++)
        {
            var brutas = (paginas[p] ?? "").Split('\n');
            for (var i = 0; i < brutas.Length; i++)
            {
                Enfileirar(p + 1, i);
                var linha = Espacos().Replace(brutas[i], " ").Trim();
                if (linha.Length == 0 || NumeroDePagina().IsMatch(linha) || repetidas.Contains(linha)) continue;
                Match m;
                if (!viuEpigrafe && !viuArtigo && (m = Epigrafe().Match(linha)).Success)
                {
                    Abrir(TipoBlocoLei.Epigrafe, "", linha, p + 1);
                    viuEpigrafe = true;
                    Fechar();
                    atual = TipoBlocoLei.Ementa;
                    paginaDoBloco = p + 1;
                    continue;
                }
                if ((m = Divisao().Match(linha)).Success)
                {
                    var tipo = m.Groups[1].Value.ToUpperInvariant() switch
                    {
                        var s when s.StartsWith("T", StringComparison.Ordinal) => TipoBlocoLei.Titulo,
                        var s when s.StartsWith("C", StringComparison.Ordinal) => TipoBlocoLei.Capitulo,
                        var s when s.StartsWith("SUB", StringComparison.Ordinal) => TipoBlocoLei.Subsecao,
                        _ => TipoBlocoLei.Secao,
                    };
                    Abrir(tipo, m.Groups[1].Value.ToUpperInvariant() + " " + m.Groups[2].Value.ToUpperInvariant(), m.Groups[3].Value, p + 1);
                    continue;
                }
                if ((m = Artigo().Match(linha)).Success)
                {
                    viuArtigo = true;
                    Abrir(TipoBlocoLei.Artigo, NormalizarArtigo(m.Groups[1].Value), m.Groups[2].Value, p + 1);
                    continue;
                }
                if (viuArtigo && (m = Paragrafo().Match(linha)).Success)
                {
                    Abrir(TipoBlocoLei.Paragrafo, m.Groups[1].Value, m.Groups[2].Value, p + 1);
                    continue;
                }
                if (viuArtigo && (m = Inciso().Match(linha)).Success)
                {
                    Abrir(TipoBlocoLei.Inciso, m.Groups[1].Value + " –", m.Groups[2].Value, p + 1);
                    continue;
                }
                if (viuArtigo && (m = Alinea().Match(linha)).Success)
                {
                    Abrir(TipoBlocoLei.Alinea, m.Groups[1].Value + ")", m.Groups[2].Value, p + 1);
                    continue;
                }
                // Linha de continuação: nome da divisão em linha própria (só maiúsculas) ou o texto que o PDF quebrou.
                if (atual is null) { Abrir(TipoBlocoLei.Texto, "", linha, p + 1); continue; }
                if (texto.Length > 0 && texto[^1] == '-' && char.IsLower(linha[0])) texto.Length--; // hifenização do PDF
                else if (texto.Length > 0) texto.Append(' ');
                texto.Append(linha);
            }
            Enfileirar(p + 1, int.MaxValue); // figuras abaixo da última linha (ou numa página sem texto)
        }
        foreach (var (_, fila) in porPagina.Where(x => x.Key > paginas.Count).OrderBy(x => x.Key)) pendentes.AddRange(fila);
        Fechar();
        return blocos;
    }

    static BlocoLei BlocoDaFigura(FiguraPdf f) => new(TipoBlocoLei.Figura,
        f.Origem is OrigemFigura.Pagina or OrigemFigura.Falhou ? $"Conteúdo gráfico da página {f.Pagina}" : $"Figura da página {f.Pagina}",
        "", f.Pagina, f);

    /// <summary>As figuras de cada página, na ordem da linha e da altura, sem as que se repetem no mesmo lugar em mais da metade das páginas
    /// (com 3 ou mais): cabeçalho ou rodapé gráfico (brasão, faixa), como as linhas de texto repetidas.</summary>
    static Dictionary<int, Queue<FiguraPdf>> FigurasPorPagina(IReadOnlyList<FiguraPdf> figuras, int totalPaginas)
    {
        static (int, int, int, int) Lugar(FiguraPdf f) =>
            ((int)Math.Round(f.X * 50), (int)Math.Round(f.Y * 50), (int)Math.Round(f.W * 50), (int)Math.Round(f.H * 50));
        var repetidas = new HashSet<(int, int, int, int)>();
        if (totalPaginas >= 3)
            foreach (var g in figuras.Where(f => f.Origem is OrigemFigura.Imagem or OrigemFigura.Desenho).GroupBy(Lugar))
                if (g.Select(f => f.Pagina).Distinct().Count() * 2 > totalPaginas) repetidas.Add(g.Key);
        return figuras
            .Where(f => !(f.Origem is OrigemFigura.Imagem or OrigemFigura.Desenho && repetidas.Contains(Lugar(f))))
            .GroupBy(f => f.Pagina)
            .ToDictionary(g => g.Key, g => new Queue<FiguraPdf>(g.OrderBy(f => f.Linha).ThenBy(f => f.Y)));
    }

    // ---- Resumo automático do artigo (correções 2026-11-15) ----

    public const int MaximoPalavrasResumo = 12;

    [GeneratedRegex(@"^\(?\s*(vetad[oa]|revogad[oa]|suprimid[oa])\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Vetado();

    // Fórmulas de abertura que não dizem do que o artigo trata ("Para os efeitos desta Lei", "Nos termos do art. 5º,", "Sem prejuízo de…,").
    [GeneratedRegex(@"^(?:(?:para|a)\s+(?:os\s+)?(?:fins|efeitos)\s+(?:desta|deste|da\s+presente|do\s+presente|do\s+disposto\s+n[oa])\s+(?:lei(?:\s+complementar)?|c[óo]digo|decreto|resolu[çc][ãa]o|norma|regulamento|cap[íi]tulo|se[çc][ãa]o|artigo|t[íi]tulo)\b\s*,?\s*"
        + @"|(?:nos\s+termos|na\s+forma|de\s+acordo\s+com|em\s+conformidade\s+com|conforme|observad[oa]s?|respeitad[oa]s?|sem\s+preju[íi]zo)\b[^,;:]{0,90},\s*)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FormulaDeAbertura();

    // Aposto curto entre vírgulas que só qualifica ("Qualquer construção, de iniciativa pública ou privada, somente…").
    [GeneratedRegex(@",\s(?:de|da|do|das|dos|em|no|na|nos|nas|inclusive|exceto|salvo|ainda|quando|se|sendo|para|por|com|sem|sob|bem\s+como)\s[^,;:()]{1,60},\s",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Aposto();

    [GeneratedRegex(@"\s*\([^()]*\)")]
    private static partial Regex Parenteses();

    // Fim da primeira ideia: ";", ":", ponto final de frase (não o de "art. 5º" ou "nº.") ou uma ressalva depois de vírgula.
    [GeneratedRegex(@"[;:]|(?<!\b(?:[Aa]rts?|[Nn]|[Nn]º|[Ii]nc|[Aa]l|[Pp]ar|[Cc]ap|[Ss]r|[Ss]ra|[Dd]r|[Dd]ra|[Aa]v|[Ee]x|[Oo]bs|[Ff]ls))\.(?=\s+\p{Lu}|\s*$)"
        + @"|,\s(?:ficando|desde\s+que|salvo|exceto|sob\s+pena|sendo\s+que|observad[oa]s?|nos\s+termos|conforme|de\s+acordo\s+com|mediante|ressalvad[oa]s?)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex FimDaIdeia();

    static readonly HashSet<string> PalavrasDeLigacao = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "o", "as", "os", "e", "ou", "de", "da", "do", "das", "dos", "em", "no", "na", "nos", "nas", "com", "por", "para", "pela", "pelo",
        "pelas", "pelos", "que", "ao", "aos", "à", "às", "se", "um", "uma", "uns", "umas", "sem", "sob", "entre", "cujo", "cuja", "seu", "sua",
    };

    /// <summary>Resumo bem pequeno do artigo (até <see cref="MaximoPalavrasResumo"/> palavras), tirado do próprio texto, de forma
    /// determinística — nada é inventado: tira as fórmulas de abertura ("Para os efeitos desta Lei"), os parênteses e os apostos curtos,
    /// fica com a primeira ideia (até o primeiro ";", ":", ponto ou ", desde que"/", ficando"…), corta em 12 palavras sem terminar em
    /// palavra de ligação e põe reticências quando cortou. "(VETADO)" vira "Vetado". Texto vazio: "".</summary>
    public static string Resumir(string? textoDoArtigo)
    {
        var t = Espacos().Replace(textoDoArtigo ?? "", " ").Trim().TrimStart('-', '–', '—', '.', ' ');
        if (t.Length == 0) return "";
        var v = Vetado().Match(t);
        if (v.Success) return char.ToUpperInvariant(v.Groups[1].Value[0]) + v.Groups[1].Value[1..].ToLowerInvariant();
        string anterior;
        do { anterior = t; t = FormulaDeAbertura().Replace(t, "", 1).TrimStart(); } while (t != anterior && t.Length > 0);
        if (t.Length == 0) t = anterior;
        var fim = FimDaIdeia().Match(t);
        if (fim.Success && fim.Index > 0) t = t[..fim.Index]; // a primeira ideia já é o resumo: sem reticências quando só ela ficou
        t = Parenteses().Replace(t, "");
        t = Aposto().Replace(t, " ", 1);
        t = Espacos().Replace(t, " ").Trim().TrimEnd(',', ';', ':', '.', '-', '–', '—', ' ');
        var palavras = t.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var cortou = palavras.Count > MaximoPalavrasResumo;
        if (cortou) palavras = palavras.Take(MaximoPalavrasResumo).ToList();
        while (cortou && palavras.Count > 3 && PalavrasDeLigacao.Contains(palavras[^1].TrimEnd(',', ';', ':'))) palavras.RemoveAt(palavras.Count - 1);
        if (palavras.Count == 0) return "";
        var r = string.Join(' ', palavras).TrimEnd(',', ';', ':', '.', '-', '–', '—', ' ');
        r = char.ToUpper(r[0], System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + r[1..];
        return cortou ? r + "…" : r;
    }

    static string NormalizarArtigo(string s)
    {
        var numero = new string(s.Where(char.IsDigit).ToArray());
        var sufixo = s.Contains('-', StringComparison.Ordinal) ? s[s.LastIndexOf('-')..] : "";
        return int.TryParse(numero, out var n) && n <= 9 ? $"Art. {n}º{sufixo}" : $"Art. {numero}{sufixo}";
    }

    /// <summary>Linhas curtas que aparecem em mais da metade das páginas (com 3 ou mais): cabeçalho ou rodapé.</summary>
    static HashSet<string> LinhasRepetidas(IReadOnlyList<string> paginas)
    {
        var r = new HashSet<string>(StringComparer.Ordinal);
        if (paginas.Count < 3) return r;
        var contagem = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pag in paginas)
            foreach (var l in (pag ?? "").Split('\n').Select(x => Espacos().Replace(x, " ").Trim()).Where(x => x.Length is > 0 and <= 120).Distinct())
                contagem[l] = contagem.GetValueOrDefault(l) + 1;
        foreach (var (l, n) in contagem)
            if (n * 2 > paginas.Count && !Artigo().IsMatch(l)) r.Add(l);
        return r;
    }
}
