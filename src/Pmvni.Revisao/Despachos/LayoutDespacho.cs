using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Pmvni.Revisao;

/// <summary>A formatação de um parágrafo do corpo do modelo, em pontos: espaço antes e depois, entrelinha (múltiplo da linha simples),
/// alinhamento (<c>w:jc</c>), tamanho, negrito e sublinhado do texto, "manter com o próximo" e recuo à esquerda.</summary>
public sealed record FormatoParagrafo(double AntesPt, double DepoisPt, double Entrelinha, string Alinhamento, double TamanhoPt, bool Negrito,
    bool Sublinhado, bool MantemComProximo, double RecuoEsquerdoPt);

/// <summary>Um trecho de texto fixo do cabeçalho ou do rodapé do modelo.</summary>
public sealed record TrechoFixo(string Texto, bool Negrito, double TamanhoPt);

/// <summary>Um parágrafo do rodapé do modelo (posição já calculada: <see cref="TopoPt"/> a partir do alto da página).</summary>
public sealed record ParagrafoRodape(IReadOnlyList<TrechoFixo> Trechos, string Alinhamento, double RecuoEsquerdoPt, double TopoPt, double AlturaPt);

/// <summary>Uma imagem ancorada do cabeçalho (faixas e brasão, tarja lateral), em pontos a partir do canto da página, com o recorte do modelo
/// (<c>a:srcRect</c>, frações de 0 a 1). Os bytes são lidos do DOCX do kit em tempo de execução; nunca vão para o repositório.</summary>
public sealed record ImagemPagina(byte[] Bytes, double XPt, double YPt, double LarguraPt, double AlturaPt,
    double CorteEsquerdo, double CorteTopo, double CorteDireito, double CorteBaixo, bool AtrasDoTexto);

/// <summary>Uma linha do rodapé (a linha vermelha), em pontos a partir do canto da página; <see cref="Cor"/> em <c>#RRGGBB</c>.</summary>
public sealed record LinhaPagina(double X1Pt, double Y1Pt, double X2Pt, double Y2Pt, double EspessuraPt, string Cor);

/// <summary>A página do modelo de despacho (correções 2026-11-15, prévia no modelo): tamanho e margens, fonte e tamanho padrão, a
/// formatação de cada parágrafo do corpo (na ordem de <c>w:body/w:p</c>, a mesma do <see cref="InfoModelo.Paragrafos"/>), as imagens do
/// cabeçalho, a linha e os parágrafos do rodapé. Tudo em pontos, lido do próprio DOCX do kit.</summary>
public sealed record LayoutDespacho(double LarguraPt, double AlturaPt, double MargemTopoPt, double MargemDireitaPt, double MargemBaixoPt,
    double MargemEsquerdaPt, string Fonte, double TamanhoPt, IReadOnlyList<FormatoParagrafo> Formatos, IReadOnlyList<ImagemPagina> Imagens,
    IReadOnlyList<LinhaPagina> Linhas, IReadOnlyList<ParagrafoRodape> Rodape)
{
    static readonly XNamespace W = PreenchimentoDocx.W;
    static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    static readonly XNamespace Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    static readonly XNamespace WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    static readonly XNamespace V = "urn:schemas-microsoft-com:vml";

    /// <summary>Altura da linha simples em relação ao tamanho da fonte (Verdana: ascendente 2059 + descendente 430 sobre 2048).</summary>
    public const double LinhaSimples = 1.2153;

    /// <summary>Distância mínima entre o fim da faixa do cabeçalho e o começo do corpo, em pontos.</summary>
    public const double FolgaCabecalhoPt = 4.5;

    public double LarguraUtilPt => LarguraPt - MargemEsquerdaPt - MargemDireitaPt;

    /// <summary>A altura do corpo em cada folha: de <see cref="TopoDoCorpoPt"/> até a margem de baixo.</summary>
    public double AlturaUtilPt => AlturaPt - TopoDoCorpoPt - MargemBaixoPt;

    /// <summary>Onde termina a faixa do cabeçalho (o brasão e as faixas coloridas), a partir do alto da página: o fim mais baixo das imagens do
    /// cabeçalho que passam pela coluna de texto, começam acima da margem de cima e terminam no terço de cima da página (a tarja lateral fica
    /// fora da coluna e não conta). Sem imagem assim: 0.</summary>
    public double FimDoCabecalhoPt => Imagens
        .Where(i => i.YPt < MargemTopoPt && i.XPt < LarguraPt - MargemDireitaPt && i.XPt + i.LarguraPt > MargemEsquerdaPt && i.YPt + i.AlturaPt <= AlturaPt / 3)
        .Select(i => i.YPt + i.AlturaPt).DefaultIfEmpty(0).Max();

    /// <summary>Onde o corpo começa em cada folha (correção de 2026-11-16). No modelo do kit a margem de cima (2,5 cm) é menor que o fim da
    /// faixa do cabeçalho (2,82 cm): a imagem é flutuante e não empurra o texto, e da 2ª folha em diante a primeira linha saía por cima do
    /// brasão (a 1ª folha só escapa porque começa por um parágrafo vazio). O corpo começa então no maior entre a margem de cima e o fim da
    /// faixa mais a <see cref="FolgaCabecalhoPt"/>, arredondado para cima em twips; é este o valor gravado na margem do DOCX gerado
    /// (<see cref="PreenchimentoDocx.Preencher"/>) e usado na prévia.</summary>
    public double TopoDoCorpoPt
    {
        get
        {
            var fim = FimDoCabecalhoPt;
            return fim <= 0 ? MargemTopoPt : Math.Max(MargemTopoPt, Math.Ceiling((fim + FolgaCabecalhoPt) * 20 - 1e-6) / 20.0);
        }
    }

    /// <summary>O primeiro parágrafo do corpo do modelo não tem texto (é o espaçador que afasta o título do cabeçalho na 1ª folha).</summary>
    public bool PrimeiroParagrafoVazio { get; init; }

    /// <summary>O quanto o espaço depois do primeiro parágrafo do corpo diminui para a 1ª folha continuar como no modelo, mesmo com o corpo
    /// começando mais abaixo: só quando esse parágrafo é vazio (o espaçador do modelo), e no máximo o espaço que ele tem.</summary>
    public double CompensacaoInicialPt =>
        PrimeiroParagrafoVazio && Formatos.Count > 0 ? Math.Min(Math.Max(0, TopoDoCorpoPt - MargemTopoPt), Formatos[0].DepoisPt) : 0;

    /// <summary>A formatação do parágrafo <paramref name="i"/> do corpo (fora da lista: o padrão do modelo), como vai para a folha: o
    /// primeiro já com a <see cref="CompensacaoInicialPt"/> descontada do espaço depois.</summary>
    public FormatoParagrafo Formato(int i)
    {
        if (i < 0 || i >= Formatos.Count) return new FormatoParagrafo(0, 8, 259 / 240.0, "left", TamanhoPt, false, false, false, 0);
        return i == 0 && CompensacaoInicialPt > 0 ? Formatos[0] with { DepoisPt = Formatos[0].DepoisPt - CompensacaoInicialPt } : Formatos[i];
    }

    static double Twips(string? v, double padrao = 0) =>
        v is not null && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d / 20.0 : padrao;

    static double Emu(string? v) => v is not null && long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var d) ? d / 12700.0 : 0;

    static bool Ligado(XElement? e)
    {
        if (e is null) return false;
        var v = (string?)e.Attribute(W + "val");
        return v is null || (v != "0" && v != "false" && v != "off" && v != "none");
    }

    /// <summary>Lê a página do modelo. Partes que faltam ficam de fora (sem imagem, sem rodapé); o corpo falta: <see cref="ModeloForaDoPadraoException"/>.</summary>
    public static LayoutDespacho Ler(byte[] docx)
    {
        ArgumentNullException.ThrowIfNull(docx);
        try
        {
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            var doc = Xml(zip, PreenchimentoDocx.Documento) ?? throw new ModeloForaDoPadraoException(PreenchimentoDocx.Documento);
            var corpo = doc.Root?.Element(W + "body") ?? throw new ModeloForaDoPadraoException("w:body");
            var sect = corpo.Elements(W + "sectPr").LastOrDefault();
            var pg = sect?.Element(W + "pgSz");
            var mar = sect?.Element(W + "pgMar");
            var largura = Twips((string?)pg?.Attribute(W + "w"), 595.3);
            var altura = Twips((string?)pg?.Attribute(W + "h"), 841.9);
            double M(string n, double p) => Twips((string?)mar?.Attribute(W + n), p);
            var (topo, direita, baixo, esquerda) = (M("top", 70.85), M("right", 85.05), M("bottom", 70.85), M("left", 85.05));
            var distCabecalho = M("header", 35.4);
            var distRodape = M("footer", 35.4);

            var estilos = Xml(zip, "word/styles.xml");
            var rPrPadrao = estilos?.Root?.Element(W + "docDefaults")?.Element(W + "rPrDefault")?.Element(W + "rPr");
            var fonte = (string?)rPrPadrao?.Element(W + "rFonts")?.Attribute(W + "ascii") ?? "Verdana";
            var szPadrao = (string?)rPrPadrao?.Element(W + "sz")?.Attribute(W + "val");
            var tamanho = szPadrao is not null && double.TryParse(szPadrao, NumberStyles.Float, CultureInfo.InvariantCulture, out var meios) ? meios / 2.0 : 11; // meios-pontos
            var normal = Estilo(estilos, null);
            var protocolo = corpo.Elements(W + "p").FirstOrDefault(p => PreenchimentoDocx.Texto(p).Trim().StartsWith(PreenchimentoDocx.RotuloProtocolo, StringComparison.Ordinal));
            var runProtocolo = protocolo?.Elements(W + "r").FirstOrDefault(r => r.Element(W + "t") is not null);
            var tamanhoVaga = protocolo is null ? tamanho : TamanhoRun(runProtocolo?.Element(W + "rPr"), protocolo, tamanho);
            var formatos = corpo.Elements(W + "p").Select(p => FormatoDe(p, normal, tamanho, tamanhoVaga)).ToList();

            var rels = Relacoes(zip, "word/_rels/document.xml.rels");
            string? Parte(string tipo)
            {
                var refs = sect?.Elements(W + tipo + "Reference").ToList() ?? [];
                var id = (string?)(refs.FirstOrDefault(r => (string?)r.Attribute(W + "type") == "default") ?? refs.FirstOrDefault())?.Attribute(R + "id");
                return id is not null && rels.TryGetValue(id, out var alvo) ? "word/" + alvo : null;
            }

            var imagens = new List<ImagemPagina>();
            if (Parte("header") is { } cab && Xml(zip, cab) is { } hdr)
            {
                var relsCab = Relacoes(zip, Path.GetDirectoryName(cab)!.Replace('\\', '/') + "/_rels/" + Path.GetFileName(cab) + ".rels");
                var estiloCab = Estilo(estilos, "header");
                var y = distCabecalho;
                foreach (var p in hdr.Root!.Elements(W + "p"))
                {
                    foreach (var ancora in p.Descendants(WP + "anchor"))
                        if (Imagem(zip, ancora, relsCab, esquerda, topo, y) is { } img) imagens.Add(img);
                    y += AlturaLinha(p, estiloCab, tamanho);
                }
            }

            var linhas = new List<LinhaPagina>();
            var rodape = new List<ParagrafoRodape>();
            if (Parte("footer") is { } rod && Xml(zip, rod) is { } ftr)
            {
                var estiloRod = Estilo(estilos, "footer");
                var ps = ftr.Root!.Elements(W + "p").ToList();
                var alturas = ps.Select(p => AlturaLinha(p, estiloRod, tamanho)).ToList();
                var y = altura - distRodape - alturas.Sum();
                for (var i = 0; i < ps.Count; i++)
                {
                    var p = ps[i];
                    // A linha vermelha (VML; no modelo da aprovação é o "Fallback" do desenho, com as mesmas medidas).
                    if (p.Descendants(V + "line").FirstOrDefault() is { } l && Linha(l, esquerda, y) is { } linha) linhas.Add(linha);
                    var trechos = p.Elements(W + "r").Where(r => r.Element(W + "t") is not null)
                        .Select(r => new TrechoFixo(string.Concat(r.Elements(W + "t").Select(t => t.Value)), Ligado(r.Element(W + "rPr")?.Element(W + "b")),
                            TamanhoRun(r.Element(W + "rPr"), p, tamanho))).ToList();
                    var ppr = p.Element(W + "pPr");
                    rodape.Add(new ParagrafoRodape(trechos, (string?)ppr?.Element(W + "jc")?.Attribute(W + "val") ?? "left",
                        Twips((string?)ppr?.Element(W + "ind")?.Attribute(W + "left")), y, alturas[i]));
                    y += alturas[i];
                }
            }
            return new LayoutDespacho(largura, altura, topo, direita, baixo, esquerda, fonte, tamanho, formatos, imagens, linhas, rodape)
            {
                PrimeiroParagrafoVazio = corpo.Elements(W + "p").FirstOrDefault() is { } primeiro && PreenchimentoDocx.Texto(primeiro).Trim().Length == 0,
            };
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException)
        {
            throw new ModeloForaDoPadraoException(PreenchimentoDocx.Documento);
        }
    }

    // ---------- partes ----------

    static XDocument? Xml(ZipArchive zip, string nome)
    {
        var e = zip.GetEntry(nome);
        if (e is null) return null;
        using var s = e.Open();
        return XDocument.Load(s, LoadOptions.PreserveWhitespace);
    }

    static Dictionary<string, string> Relacoes(ZipArchive zip, string nome) =>
        Xml(zip, nome)?.Root?.Elements(Rel + "Relationship")
            .Where(r => r.Attribute("Id") is not null && r.Attribute("Target") is not null)
            .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!, StringComparer.Ordinal)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);

    static byte[]? Bytes(ZipArchive zip, string nome)
    {
        var e = zip.GetEntry(nome);
        if (e is null) return null;
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>O <c>w:pPr</c> do estilo de parágrafo (pelo nome: null = o padrão "Normal"), para herdar espaçamento.</summary>
    static XElement? Estilo(XDocument? estilos, string? nome)
    {
        var lista = estilos?.Root?.Elements(W + "style").Where(s => (string?)s.Attribute(W + "type") == "paragraph").ToList() ?? [];
        var s = nome is null
            ? lista.FirstOrDefault(x => (string?)x.Attribute(W + "default") == "1")
            : lista.FirstOrDefault(x => string.Equals((string?)x.Element(W + "name")?.Attribute(W + "val"), nome, StringComparison.OrdinalIgnoreCase));
        return s?.Element(W + "pPr");
    }

    static double TamanhoRun(XElement? rPr, XElement p, double padrao)
    {
        var sz = (string?)rPr?.Element(W + "sz")?.Attribute(W + "val") ?? (string?)p.Element(W + "pPr")?.Element(W + "rPr")?.Element(W + "sz")?.Attribute(W + "val");
        return sz is not null && double.TryParse(sz, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v / 2.0 : padrao;
    }

    static string? Espaco(XElement p, XElement? estilo, string atributo) =>
        (string?)p.Element(W + "pPr")?.Element(W + "spacing")?.Attribute(W + atributo) ?? (string?)estilo?.Element(W + "spacing")?.Attribute(W + atributo);

    static double Entrelinha(XElement p, XElement? estilo)
    {
        var linha = Espaco(p, estilo, "line");
        var regra = Espaco(p, estilo, "lineRule") ?? "auto";
        if (linha is null || !double.TryParse(linha, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return 1;
        return regra == "auto" ? v / 240.0 : 1; // "exact"/"atLeast" não aparecem nos modelos do kit
    }

    /// <summary>A altura de uma linha do parágrafo (cabeçalho, rodapé): a linha simples do tamanho do primeiro texto (ou da marca do parágrafo).</summary>
    static double AlturaLinha(XElement p, XElement? estilo, double padrao)
    {
        var run = p.Elements(W + "r").FirstOrDefault(r => r.Element(W + "t") is not null);
        var tam = TamanhoRun(run?.Element(W + "rPr"), p, padrao);
        return tam * LinhaSimples * Entrelinha(p, estilo) + Twips(Espaco(p, estilo, "before")) + Twips(Espaco(p, estilo, "after"));
    }

    /// <summary>A formatação de um parágrafo do corpo. As vagas (vazias e justificadas, sem formato de texto) recebem no DOCX os runs com o
    /// formato do rótulo "Protocolo nº:" (<see cref="PreenchimentoDocx.Preencher"/>): o tamanho delas é o <paramref name="tamanhoVaga"/>.</summary>
    static FormatoParagrafo FormatoDe(XElement p, XElement? normal, double padrao, double tamanhoVaga)
    {
        var ppr = p.Element(W + "pPr");
        var run = p.Elements(W + "r").FirstOrDefault(r => r.Elements(W + "t").Any(t => t.Value.Trim().Length > 0));
        var rPr = run?.Element(W + "rPr") ?? ppr?.Element(W + "rPr");
        var jc = (string?)ppr?.Element(W + "jc")?.Attribute(W + "val") ?? "left";
        var vaga = run is null && rPr is null && jc == "both";
        return new FormatoParagrafo(Twips(Espaco(p, normal, "before")), Twips(Espaco(p, normal, "after")), Entrelinha(p, normal),
            jc, vaga ? tamanhoVaga : TamanhoRun(rPr, p, padrao), Ligado(rPr?.Element(W + "b")),
            rPr?.Element(W + "u") is { } u && (string?)u.Attribute(W + "val") != "none", ppr?.Element(W + "keepNext") is { } k && Ligado(k),
            Twips((string?)ppr?.Element(W + "ind")?.Attribute(W + "left")));
    }

    static ImagemPagina? Imagem(ZipArchive zip, XElement ancora, Dictionary<string, string> rels, double margemEsq, double margemTopo, double yParagrafo)
    {
        var blip = ancora.Descendants(A + "blip").FirstOrDefault();
        var id = (string?)blip?.Attribute(R + "embed");
        if (id is null || !rels.TryGetValue(id, out var alvo)) return null;
        var bytes = Bytes(zip, "word/" + alvo.TrimStart('/'));
        if (bytes is null) return null;
        var ext = ancora.Element(WP + "extent");
        var (w, h) = (Emu((string?)ext?.Attribute("cx")), Emu((string?)ext?.Attribute("cy")));
        var ph = ancora.Element(WP + "positionH");
        var pv = ancora.Element(WP + "positionV");
        var offX = Emu(ph?.Element(WP + "posOffset")?.Value);
        var offY = Emu(pv?.Element(WP + "posOffset")?.Value);
        // Horizontal: da página ou da coluna (margem esquerda). Vertical: da página, da margem de cima ou do parágrafo do cabeçalho.
        var x = (string?)ph?.Attribute("relativeFrom") is "page" or "leftMargin" ? offX : margemEsq + offX;
        var y = (string?)pv?.Attribute("relativeFrom") switch
        {
            "page" or "topMargin" => offY,
            "margin" => margemTopo + offY,
            _ => yParagrafo + offY,
        };

        var corte = ancora.Descendants(A + "srcRect").FirstOrDefault();
        double C(string n) => corte?.Attribute(n) is { } a && double.TryParse(a.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v / 100000.0 : 0;
        return new ImagemPagina(bytes, x, y, w, h, C("l"), C("t"), C("r"), C("b"), (string?)ancora.Attribute("behindDoc") is "1" or "true");
    }

    /// <summary>A linha VML: <c>from</c>/<c>to</c> em pontos a partir da coluna e do parágrafo; <c>strokecolor</c> (nome ou #hex) e
    /// <c>strokeweight</c> (pt ou mm).</summary>
    static LinhaPagina? Linha(XElement l, double margemEsq, double yParagrafo)
    {
        static (double X, double Y)? Ponto(string? v)
        {
            var partes = v?.Split(',');
            if (partes is not { Length: 2 }) return null;
            return Medida(partes[0]) is { } x && Medida(partes[1]) is { } y ? (x, y) : null;
        }
        if (Ponto((string?)l.Attribute("from")) is not { } de || Ponto((string?)l.Attribute("to")) is not { } ate) return null;
        var cor = ((string?)l.Attribute("strokecolor") ?? "black").Trim();
        cor = cor.ToLowerInvariant() switch
        {
            "red" => "#FF0000", "black" => "#000000", "blue" => "#0000FF", "green" => "#008000", "white" => "#FFFFFF",
            _ => cor.StartsWith('#') && cor.Length == 7 ? cor.ToUpperInvariant() : "#000000",
        };
        var espessura = Medida((string?)l.Attribute("strokeweight")) ?? 0.75;
        return new LinhaPagina(margemEsq + de.X, yParagrafo + de.Y, margemEsq + ate.X, yParagrafo + ate.Y, espessura, cor);
    }

    /// <summary>Uma medida CSS do VML ("6.1pt", ".79mm", "1cm", "0.5in") em pontos; sem unidade = pontos.</summary>
    internal static double? Medida(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return null;
        v = v.Trim();
        var (num, fator) = v.EndsWith("pt", StringComparison.Ordinal) ? (v[..^2], 1.0)
            : v.EndsWith("mm", StringComparison.Ordinal) ? (v[..^2], 72 / 25.4)
            : v.EndsWith("cm", StringComparison.Ordinal) ? (v[..^2], 72 / 2.54)
            : v.EndsWith("in", StringComparison.Ordinal) ? (v[..^2], 72.0)
            : v.EndsWith("px", StringComparison.Ordinal) ? (v[..^2], 0.75)
            : (v, 1.0);
        return double.TryParse(num, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d * fator : null;
    }
}
