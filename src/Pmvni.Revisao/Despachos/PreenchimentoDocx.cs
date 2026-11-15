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
        TrocarAssunto(mapa.Um(PapelParagrafo.Assunto), d.Assunto);
        TrocarData(mapa.Um(PapelParagrafo.Data), d.DataPorExtenso);

        var vagas = mapa.Todos(PapelParagrafo.Vaga);
        var novos = new List<XElement>();
        var usadas = 0;
        XElement Vaga(IEnumerable<string> textos)
        {
            var p = usadas < vagas.Count ? vagas[usadas] : Clonar(vagas[0]);
            usadas++;
            foreach (var r in p.Elements(W + "r").ToList()) r.Remove();
            foreach (var texto in textos.Where(x => x.Length > 0)) p.Add(NovoRun(rPrRotulo, texto, false));
            return p;
        }

        XElement ancora;
        if (d.Tipo == TipoDespacho.Correcao)
        {
            var grupo = mapa.Um(PapelParagrafo.Grupo);
            ancora = grupo.ElementsBeforeSelf().Last();
            var primeiro = true;
            foreach (var g in d.Grupos)
            {
                var titulo = primeiro ? grupo : Clonar(grupo);
                primeiro = false;
                TrocarTexto(titulo, g.Titulo);
                novos.Add(titulo);
                foreach (var e in g.Exigencias) novos.Add(Vaga(new[] { e.Texto, e.Fundamento }));
            }
            if (primeiro) grupo.Remove();
        }
        else
            ancora = vagas[0].ElementsBeforeSelf().Last();
        foreach (var c in d.Corpo) novos.Add(Vaga(new[] { c }));

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

    static void TrocarData(XElement p, string data)
    {
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
