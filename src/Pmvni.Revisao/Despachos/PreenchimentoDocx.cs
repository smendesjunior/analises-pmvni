using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Pmvni.Revisao;

/// <summary>O papel de cada parágrafo do corpo do modelo de despacho (desenho §10.5).</summary>
public enum PapelParagrafo { Fixo, Protocolo, Requerente, Assunto, Destinatario, Grupo, Vaga, Data, LinhaAssinatura }

/// <summary>Um parágrafo do corpo do modelo: texto, negrito (todos os runs com texto em negrito), alinhamento (<c>w:jc</c>, "left" sem ele) e papel.</summary>
public sealed record ParagrafoModelo(string Texto, bool Negrito, string Alinhamento, PapelParagrafo Papel);

/// <summary>O que o app precisa saber do modelo DOCX: o assunto (texto em negrito do modelo), o título de grupo (só correção; "" na aprovação),
/// quantas vagas há e os parágrafos do corpo na ordem.</summary>
public sealed record InfoModelo(TipoDespacho Tipo, string Assunto, string TituloGrupo, int Vagas, IReadOnlyList<ParagrafoModelo> Paragrafos);

/// <summary>O modelo não tem uma âncora (ou a tem repetida): o tipo fica indisponível; nunca se improvisa.</summary>
public sealed class ModeloForaDoPadraoException(string ancora) : Exception("O modelo não está no formato esperado (âncora \"" + ancora + "\").")
{
    public string Ancora { get; } = ancora;
}

/// <summary>Preenchimento do modelo DOCX do kit por âncoras, com <see cref="ZipArchive"/> + XLinq: só <c>word/document.xml</c> muda; as
/// outras entradas são copiadas sem tocar, na mesma ordem e com a mesma data.</summary>
public static class PreenchimentoDocx
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";
    internal const string Documento = "word/document.xml";
    internal const string RotuloProtocolo = "Protocolo nº:", RotuloRequerente = "Requerente:", RotuloAssunto = "Assunto:";
    internal const string MarcaData = "dia de mês de ano";
    const string AncoraDestinatario = "AO …:", AncoraGrupo = "título de grupo em negrito", AncoraVagas = "parágrafos vazios justificados",
        AncoraAssinatura = "__________";

    /// <summary>Os parágrafos do corpo com o papel de cada um.</summary>
    sealed record Mapa(List<XElement> Paragrafos, PapelParagrafo[] Papeis)
    {
        public XElement Um(PapelParagrafo p) => Paragrafos[Array.IndexOf(Papeis, p)];

        public List<XElement> Todos(PapelParagrafo p) => Paragrafos.Where((_, i) => Papeis[i] == p).ToList();
    }

    public static InfoModelo LerModelo(byte[] docx, TipoDespacho t)
    {
        ArgumentNullException.ThrowIfNull(docx);
        var doc = LerDocumento(docx);
        var mapa = Mapear(doc, t);
        var paragrafos = mapa.Paragrafos.Select((p, i) => new ParagrafoModelo(Texto(p), Negrito(p), Alinhamento(p), mapa.Papeis[i])).ToList();
        var assunto = mapa.Um(PapelParagrafo.Assunto);
        var negritos = Runs(assunto).Where(RunNegrito).ToList();
        var textoAssunto = negritos.Count > 0
            ? string.Concat(negritos.Select(TextoRun)).Trim()
            : Texto(assunto).Trim()[RotuloAssunto.Length..].Trim();
        var grupo = t == TipoDespacho.Correcao ? Texto(mapa.Um(PapelParagrafo.Grupo)).Trim() : "";
        return new InfoModelo(t, textoAssunto, grupo, mapa.Todos(PapelParagrafo.Vaga).Count, paragrafos);
    }

    /// <summary>O modelo com o bloco de assinatura do profissional dos Ajustes (2026-11-17): depois da linha "____", o parágrafo com
    /// "MATRÍCULA" vira a matrícula, o com CAU/CREA o registro, o primeiro dos outros o nome e o seguinte a profissão. Só o texto muda (a
    /// formatação do primeiro run fica); campo vazio mantém o do modelo. Sem profissional (ou modelo fora do padrão): os mesmos bytes.</summary>
    public static byte[] ComProfissional(byte[] modelo, Pmvni.Composicao.Profissional? p)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        // 2026-11-17 (correção): com profissional definido, campo vazio sai em branco (nunca o texto do modelo, que traz os dados de outra pessoa).
        if (p is null) return modelo;
        XDocument doc; string declaracao;
        try { (doc, declaracao) = LerDocumentoComDeclaracao(modelo); }
        catch (ModeloForaDoPadraoException) { return modelo; }
        var corpo = doc.Root?.Element(W + "body");
        if (corpo is null) return modelo;
        var ps = corpo.Elements(W + "p").ToList();
        var linha = ps.FindIndex(x => Texto(x).Trim() is { Length: >= 10 } t && t.All(c => c == '_'));
        if (linha < 0) return modelo;
        var mudou = false;
        void Trocar(XElement par, string? novo)
        {
            novo ??= "";
            if (!Runs(par).Any() || Texto(par).Trim() == novo) return;
            TrocarTexto(par, novo);
            mudou = true;
        }
        var livres = new List<XElement>();
        foreach (var par in ps.Skip(linha + 1).Where(x => Texto(x).Trim().Length > 0))
        {
            var t = Texto(par).Trim().ToUpperInvariant();
            if (t.StartsWith("MATR", StringComparison.Ordinal)) Trocar(par, p.MatriculaLinha);
            else if (System.Text.RegularExpressions.Regex.IsMatch(t, @"\b(CAU|CREA|CFT|CRT)\b")) Trocar(par, p.RegistroLinha);
            else livres.Add(par);
        }
        if (livres.Count > 0) Trocar(livres[0], p.NomeLinha);
        if (livres.Count > 1) Trocar(livres[1], p.ProfissaoLinha);
        return mudou ? GravarDocx(modelo, declaracao + doc.Root!.ToString(SaveOptions.DisableFormatting)) : modelo;
    }

    public static byte[] Preencher(byte[] modelo, DespachoFinal d)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        ArgumentNullException.ThrowIfNull(d);
        var (doc, declaracao) = LerDocumentoComDeclaracao(modelo);
        var mapa = Mapear(doc, d.Tipo);

        var protocolo = mapa.Um(PapelParagrafo.Protocolo);
        var rPrRotulo = Runs(protocolo).First().Element(W + "rPr");
        AcrescentarValor(protocolo, d.Protocolo);
        AcrescentarValor(mapa.Um(PapelParagrafo.Requerente), d.Requerente);
        if (d.Destinatario is { } destinatario) TrocarTexto(mapa.Um(PapelParagrafo.Destinatario), destinatario);
        TrocarAssunto(mapa.Um(PapelParagrafo.Assunto), d.Assunto);
        TrocarData(mapa.Um(PapelParagrafo.Data), d.DataPorExtenso);
        ManterFechoJunto(mapa.Um(PapelParagrafo.Data));
        AfastarDoCabecalho(doc, LayoutDespacho.Ler(modelo));

        var vagas = mapa.Todos(PapelParagrafo.Vaga);
        var novos = new List<XElement>();
        var usadas = 0;
        XElement Vaga(IEnumerable<(string Texto, bool Negrito)> textos)
        {
            var p = usadas < vagas.Count ? vagas[usadas] : Clonar(vagas[0]);
            usadas++;
            foreach (var r in p.Elements(W + "r").ToList()) r.Remove();
            foreach (var (texto, negrito) in textos.Where(x => x.Texto.Length > 0)) p.Add(NovoRun(rPrRotulo, texto, negrito));
            return p;
        }

        XElement ancora;
        if (d.Tipo == TipoDespacho.Correcao)
        {
            var grupo = mapa.Um(PapelParagrafo.Grupo);
            ancora = grupo.ElementsBeforeSelf().Last();
            var primeiro = true;
            var numero = 0;
            foreach (var g in d.Grupos)
            {
                var titulo = primeiro ? grupo : Clonar(grupo);
                primeiro = false;
                TrocarTexto(titulo, g.Titulo);
                novos.Add(titulo);
                // Cada exigência começa pelo número em negrito ("01. "), seguido do texto e do fundamento (correções 2026-11-15).
                foreach (var e in g.Exigencias) novos.Add(Vaga(new[] { (ConteudoDespacho.Numero(++numero), true), (e.Texto, false), (e.Fundamento, false) }));
            }
            if (primeiro) grupo.Remove();
        }
        else
            ancora = vagas[0].ElementsBeforeSelf().Last();
        foreach (var c in d.Corpo) novos.Add(Vaga(new[] { (c, false) }));

        foreach (var p in novos.Concat(vagas).Distinct().Where(p => p.Parent is not null).ToList()) p.Remove();
        ancora.AddAfterSelf(novos);

        return GravarDocx(modelo, declaracao + doc.Root!.ToString(SaveOptions.DisableFormatting));
    }

    // ---------- leitura e mapa ----------

    internal static XDocument LerDocumento(byte[] docx) => LerDocumentoComDeclaracao(docx).Doc;

    static (XDocument Doc, string Declaracao) LerDocumentoComDeclaracao(byte[] docx)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
            var e = zip.GetEntry(Documento) ?? throw new ModeloForaDoPadraoException(Documento);
            string xml;
            using (var s = e.Open())
            using (var r = new StreamReader(s, new UTF8Encoding(false), true))
                xml = r.ReadToEnd();
            var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
            // A declaração e o que vem antes da raiz, como estão no modelo.
            var inicio = xml.IndexOf("?>", StringComparison.Ordinal);
            var declaracao = "";
            if (xml.StartsWith("<?xml", StringComparison.Ordinal) && inicio > 0)
            {
                var fim = inicio + 2;
                while (fim < xml.Length && char.IsWhiteSpace(xml[fim])) fim++;
                declaracao = xml[..fim];
            }
            return (doc, declaracao);
        }
        catch (Exception ex) when (ex is InvalidDataException or XmlException or IOException)
        {
            throw new ModeloForaDoPadraoException(Documento);
        }
    }

    static Mapa Mapear(XDocument doc, TipoDespacho t)
    {
        var corpo = doc.Root?.Element(W + "body") ?? throw new ModeloForaDoPadraoException("w:body");
        var ps = corpo.Elements(W + "p").ToList();
        var textos = ps.Select(p => Texto(p).Trim()).ToList();
        var papeis = new PapelParagrafo[ps.Count];

        int Unico(Func<string, bool> f, string ancora)
        {
            var achados = Enumerable.Range(0, ps.Count).Where(i => f(textos[i])).ToList();
            if (achados.Count != 1) throw new ModeloForaDoPadraoException(ancora);
            return achados[0];
        }

        // Rótulos, assunto e grupo são trocados run a run: todo o texto deles tem de estar em runs diretos do parágrafo.
        int Rotulo(string rotulo)
        {
            var i = Unico(x => x.StartsWith(rotulo, StringComparison.Ordinal), rotulo);
            if (!SoRunsDiretos(ps[i])) throw new ModeloForaDoPadraoException(rotulo);
            return i;
        }
        papeis[Rotulo(RotuloProtocolo)] = PapelParagrafo.Protocolo;
        papeis[Rotulo(RotuloRequerente)] = PapelParagrafo.Requerente;
        papeis[Rotulo(RotuloAssunto)] = PapelParagrafo.Assunto;
        var destinatario = Unico(x => x.StartsWith("AO ", StringComparison.Ordinal) && x.EndsWith(':'), AncoraDestinatario);
        papeis[destinatario] = PapelParagrafo.Destinatario;

        var i = destinatario + 1;
        if (t == TipoDespacho.Correcao)
        {
            if (i >= ps.Count || textos[i].Length == 0 || !Negrito(ps[i]) || !SoRunsDiretos(ps[i])) throw new ModeloForaDoPadraoException(AncoraGrupo);
            papeis[i++] = PapelParagrafo.Grupo;
        }
        while (i < ps.Count && textos[i].Length == 0 && !Justificado(ps[i])) i++;
        var vagas = 0;
        for (; i < ps.Count && textos[i].Length == 0 && Justificado(ps[i]); i++, vagas++) papeis[i] = PapelParagrafo.Vaga;
        if (vagas == 0) throw new ModeloForaDoPadraoException(AncoraVagas);

        var data = Unico(x => x.Contains(MarcaData, StringComparison.Ordinal), MarcaData);
        if (data < i || !ps[data].Descendants(W + "t").Any(x => x.Value.Contains(MarcaData, StringComparison.Ordinal)))
            throw new ModeloForaDoPadraoException(MarcaData);
        papeis[data] = PapelParagrafo.Data;
        var linha = Unico(x => x.Length >= 10 && x.All(c => c == '_'), AncoraAssinatura);
        if (linha < data) throw new ModeloForaDoPadraoException(AncoraAssinatura);
        papeis[linha] = PapelParagrafo.LinhaAssinatura;
        return new Mapa(ps, papeis);
    }

    // ---------- runs e texto ----------

    internal static string Texto(XElement p) => string.Concat(p.Descendants(W + "t").Select(t => t.Value));

    static bool SoRunsDiretos(XElement p) =>
        Runs(p).Any() && string.Concat(Runs(p).Select(TextoRun)) == Texto(p);

    static string TextoRun(XElement r) => string.Concat(r.Elements(W + "t").Select(t => t.Value));

    static IEnumerable<XElement> Runs(XElement p) => p.Elements(W + "r").Where(r => r.Element(W + "t") is not null);

    static bool RunNegrito(XElement r)
    {
        var b = r.Element(W + "rPr")?.Element(W + "b");
        if (b is null) return false;
        var v = (string?)b.Attribute(W + "val");
        return v is null || (v != "0" && v != "false" && v != "off");
    }

    static bool Negrito(XElement p)
    {
        var runs = Runs(p).Where(r => TextoRun(r).Length > 0).ToList();
        return runs.Count > 0 && runs.All(RunNegrito);
    }

    static string Alinhamento(XElement p) => (string?)p.Element(W + "pPr")?.Element(W + "jc")?.Attribute(W + "val") ?? "left";

    static bool Justificado(XElement p) => Alinhamento(p) == "both";

    static XElement NovoRun(XElement? rPrModelo, string texto, bool negrito)
    {
        var rPr = rPrModelo is null ? new XElement(W + "rPr") : new XElement(rPrModelo);
        if (negrito && rPr.Element(W + "b") is null)
        {
            var antes = rPr.Elements().LastOrDefault(e => e.Name == W + "rStyle" || e.Name == W + "rFonts");
            if (antes is null) rPr.AddFirst(new XElement(W + "b"));
            else antes.AddAfterSelf(new XElement(W + "b"));
        }
        var r = new XElement(W + "r");
        if (rPr.HasElements) r.Add(rPr);
        r.Add(T(texto));
        return r;
    }

    static XElement T(string texto)
    {
        var t = new XElement(W + "t", texto);
        if (texto.Length > 0 && (char.IsWhiteSpace(texto[0]) || char.IsWhiteSpace(texto[^1])))
            t.Add(new XAttribute(XNamespace.Xml + "space", "preserve"));
        return t;
    }

    /// <summary>Rótulo + run em negrito com o valor (<c>" " + valor</c> quando o rótulo não termina em espaço), com o <c>rPr</c> do rótulo.</summary>
    static void AcrescentarValor(XElement p, string valor)
    {
        var runs = Runs(p).ToList();
        var rotulo = runs[^1];
        var texto = Texto(p).EndsWith(' ') ? valor : " " + valor;
        rotulo.AddAfterSelf(NovoRun(runs[0].Element(W + "rPr"), texto, true));
    }

    static void TrocarAssunto(XElement p, string assunto)
    {
        var negritos = Runs(p).Where(RunNegrito).ToList();
        if (negritos.Count == 0)
        {
            var runs = Runs(p).ToList();
            runs[0].Element(W + "t")!.ReplaceWith(T(RotuloAssunto + " "));
            foreach (var r in runs.Skip(1)) r.Remove();
            AcrescentarValor(p, assunto);
            return;
        }
        TrocarTextoRun(negritos[0], assunto);
        foreach (var r in negritos.Skip(1)) r.Remove();
    }

    /// <summary>Data, linha e assinatura (nome, cargo, CAU, matrícula) não se separam entre páginas: "manter com o próximo" do
    /// parágrafo da data até o penúltimo do corpo.</summary>
    static void ManterFechoJunto(XElement data)
    {
        var seguintes = new List<XElement> { data };
        seguintes.AddRange(data.ElementsAfterSelf(W + "p"));
        for (var i = 0; i < seguintes.Count - 1; i++)
        {
            var p = seguintes[i];
            var ppr = p.Element(W + "pPr");
            if (ppr is null) { ppr = new XElement(W + "pPr"); p.AddFirst(ppr); }
            ppr.Element(W + "keepNext")?.Remove();
            var k = new XElement(W + "keepNext");
            // ordem do esquema: pStyle vem antes de keepNext
            if (ppr.Element(W + "pStyle") is { } estilo) estilo.AddAfterSelf(k); else ppr.AddFirst(k);
        }
    }

    static string Twips(double pt) => ((int)Math.Round(pt * 20)).ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A margem de cima que o DOCX gerado leva, em twips, quando a do modelo deixa o corpo por cima da faixa do cabeçalho
    /// (<see cref="LayoutDespacho.TopoDoCorpoPt"/>); null quando a margem do modelo já basta.</summary>
    internal static string? MargemDeCimaCorrigida(LayoutDespacho l) =>
        Math.Round(l.TopoDoCorpoPt * 20) > Math.Round(l.MargemTopoPt * 20) ? Twips(l.TopoDoCorpoPt) : null;

    /// <summary>A margem de cima do modelo, em twips, como está escrita no <c>w:pgMar</c>.</summary>
    internal static string MargemDeCimaDoModelo(LayoutDespacho l) => Twips(l.MargemTopoPt);

    /// <summary>O corpo começa abaixo da faixa do cabeçalho em todas as folhas (correção de 2026-11-16; o defeito é do modelo do kit, que não
    /// muda: a correção é só na cópia gerada). A margem de cima da seção passa a <see cref="MargemDeCimaCorrigida"/> e o parágrafo vazio que
    /// abre o corpo perde o mesmo tanto de espaço depois (<see cref="LayoutDespacho.CompensacaoInicialPt"/>), para a 1ª folha ficar igual.</summary>
    static void AfastarDoCabecalho(XDocument doc, LayoutDespacho l)
    {
        if (MargemDeCimaCorrigida(l) is not { } topo) return;
        var original = MargemDeCimaDoModelo(l);
        foreach (var mar in doc.Descendants(W + "sectPr").Select(s => s.Element(W + "pgMar")).Where(m => (string?)m?.Attribute(W + "top") == original).ToList())
            mar!.SetAttributeValue(W + "top", topo);

        if (l.CompensacaoInicialPt <= 0 || doc.Root!.Element(W + "body")!.Elements(W + "p").FirstOrDefault() is not { } primeiro) return;
        var espaco = Espacamento(primeiro);
        espaco.SetAttributeValue(W + "after", Twips(Math.Max(0, l.Formatos[0].DepoisPt - l.CompensacaoInicialPt)));
    }

    static XElement Espacamento(XElement paragrafo)
    {
        var ppr = paragrafo.Element(W + "pPr");
        if (ppr is null) { ppr = new XElement(W + "pPr"); paragrafo.AddFirst(ppr); }
        var espaco = ppr.Element(W + "spacing");
        if (espaco is null)
        {
            // ordem do esquema: spacing vem depois de pStyle, keepNext, keepLines… e antes de ind, jc e rPr
            espaco = new XElement(W + "spacing");
            var antes = ppr.Elements().LastOrDefault(e => e.Name.LocalName is "pStyle" or "keepNext" or "keepLines" or "pageBreakBefore" or "framePr"
                or "widowControl" or "numPr" or "suppressLineNumbers" or "pBdr" or "shd" or "tabs" or "suppressAutoHyphens" or "kinsoku" or "wordWrap"
                or "overflowPunct" or "topLinePunct" or "autoSpaceDE" or "autoSpaceDN" or "bidi" or "adjustRightInd" or "snapToGrid");
            if (antes is null) ppr.AddFirst(espaco); else antes.AddAfterSelf(espaco);
        }
        return espaco;
    }

    public const double EspacoAntesDaDataPt = 18;

    static void TrocarData(XElement p, string data)
    {
        var espaco = Espacamento(p);
        espaco.SetAttributeValue(W + "before", Twips(EspacoAntesDaDataPt));
        espaco.SetAttributeValue(W + "beforeLines", null);
        espaco.SetAttributeValue(W + "beforeAutospacing", "0");
        foreach (var t in p.Descendants(W + "t"))
            if (t.Value.Contains(MarcaData, StringComparison.Ordinal))
            {
                t.ReplaceWith(T(t.Value.Replace(MarcaData, data, StringComparison.Ordinal)));
                return;
            }
        throw new ModeloForaDoPadraoException(MarcaData); // a marca está dividida entre runs
    }

    static void TrocarTextoRun(XElement r, string texto)
    {
        var ts = r.Elements(W + "t").ToList();
        ts[0].ReplaceWith(T(texto));
        foreach (var t in ts.Skip(1)) t.Remove();
    }

    /// <summary>O primeiro run com texto fica com <paramref name="texto"/>; os outros runs com texto saem.</summary>
    static void TrocarTexto(XElement p, string texto)
    {
        var runs = Runs(p).ToList();
        TrocarTextoRun(runs[0], texto);
        foreach (var r in runs.Skip(1)) r.Remove();
    }

    /// <summary>Cópia sem <c>w14:paraId</c>/<c>w14:textId</c> (nunca há id repetido).</summary>
    static XElement Clonar(XElement p)
    {
        var c = new XElement(p);
        foreach (var e in c.DescendantsAndSelf())
        {
            e.Attribute(W14 + "paraId")?.Remove();
            e.Attribute(W14 + "textId")?.Remove();
        }
        return c;
    }

    // ---------- zip ----------

    /// <summary>Um ZIP novo com as entradas do modelo na mesma ordem, mesma data e conteúdo copiado, exceto o <c>document.xml</c>.</summary>
    static byte[] GravarDocx(byte[] modelo, string documentoXml)
    {
        using var origem = new ZipArchive(new MemoryStream(modelo), ZipArchiveMode.Read);
        using var ms = new MemoryStream();
        using (var destino = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var e in origem.Entries)
            {
                var nivel = e.CompressedLength == e.Length && e.Length > 0 ? CompressionLevel.NoCompression : CompressionLevel.Optimal;
                var n = destino.CreateEntry(e.FullName, nivel);
                n.LastWriteTime = e.LastWriteTime;
                using var w = n.Open();
                if (e.FullName == Documento) w.Write(new UTF8Encoding(false).GetBytes(documentoXml));
                else
                {
                    using var r = e.Open();
                    r.CopyTo(w);
                }
            }
        return ms.ToArray();
    }
}
