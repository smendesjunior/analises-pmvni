using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao.Testes;

/// <summary>DOCX e kit fictícios, montados no próprio teste (nada copiado do kit: os modelos e textos do kit só são lidos no lugar).</summary>
public static class DocxFicticio
{
    const string NsW = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    const string NsW14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    /// <summary>DOCX mínimo: cada parágrafo é um run <c>sz 24</c>; <c>""</c> = vaga (<c>jc both</c>, sem texto); prefixo <c>&gt;</c> = <c>jc right</c>;
    /// <c>**</c> = negrito do que vem depois (no começo: o parágrafo todo; no meio: <c>"Assunto: **X"</c> vira dois runs, o 2º em negrito).</summary>
    public static byte[] Criar(params string[] paragrafos)
    {
        var corpo = new StringBuilder();
        var id = 0x10000000;
        foreach (var p in paragrafos)
        {
            var texto = p;
            var jc = texto.Length == 0 ? "both" : null;
            if (texto.StartsWith('>')) { jc = "right"; texto = texto[1..]; }
            corpo.Append(CultureInfo.InvariantCulture, $"<w:p w14:paraId=\"{id++:X8}\" w14:textId=\"77777777\"><w:pPr>");
            if (jc is not null) corpo.Append("<w:jc w:val=\"").Append(jc).Append("\"/>");
            corpo.Append("</w:pPr>");
            var i = texto.IndexOf("**", StringComparison.Ordinal);
            if (i < 0) Run(corpo, texto, false);
            else
            {
                Run(corpo, texto[..i], false);
                Run(corpo, texto[(i + 2)..], true);
            }
            corpo.Append("</w:p>");
        }
        var documento = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n"
            + $"<w:document xmlns:w=\"{NsW}\" xmlns:w14=\"{NsW14}\"><w:body>" + corpo
            + "<w:sectPr><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:top=\"1417\" w:right=\"1701\" w:bottom=\"2113\" w:left=\"1701\""
            + " w:header=\"708\" w:footer=\"680\" w:gutter=\"0\"/></w:sectPr></w:body></w:document>";

        var partes = new (string Nome, string Conteudo)[]
        {
            ("[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n"
                + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
                + "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
                + "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
                + "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>"
                + "<Override PartName=\"/word/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml\"/>"
                + "</Types>"),
            ("_rels/.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>"
                + "</Relationships>"),
            ("word/document.xml", documento),
            ("word/_rels/document.xml.rels", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n"
                + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                + "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>"
                + "</Relationships>"),
            ("word/styles.xml", "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n"
                + $"<w:styles xmlns:w=\"{NsW}\"><w:docDefaults><w:rPrDefault><w:rPr>"
                + "<w:rFonts w:ascii=\"Verdana\" w:hAnsi=\"Verdana\" w:cs=\"Verdana\"/><w:sz w:val=\"24\"/></w:rPr></w:rPrDefault></w:docDefaults></w:styles>"),
        };
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (nome, conteudo) in partes)
            {
                var e = zip.CreateEntry(nome, CompressionLevel.Optimal);
                e.LastWriteTime = new DateTimeOffset(2099, 1, 2, 3, 4, 6, TimeSpan.Zero);
                using var s = e.Open();
                s.Write(new UTF8Encoding(false).GetBytes(conteudo));
            }
        return ms.ToArray();
    }

    static void Run(StringBuilder sb, string texto, bool negrito)
    {
        if (texto.Length == 0) return;
        sb.Append("<w:r><w:rPr>").Append(negrito ? "<w:b/>" : "").Append("<w:sz w:val=\"24\"/></w:rPr><w:t xml:space=\"preserve\">")
          .Append(SecurityElement.Escape(texto)).Append("</w:t></w:r>");
    }

    /// <summary>Modelo de correção fictício com todas as âncoras (2 vagas).</summary>
    public static byte[] Correcao() => Criar(
        "**TITULO FICTICIO", "Protocolo nº:", "Requerente: ", "Assunto: **ASSUNTO FICTICIO", "**AO DESTINATARIO FICTICIO:",
        "**GRUPO FICTICIO", "", "", ">Cidade Ficticia, dia de mês de ano", "", "______________", "NOME FICTICIO");

    /// <summary>Modelo de aprovação fictício com todas as âncoras (4 vagas).</summary>
    public static byte[] Aprovacao() => Criar(
        "**TITULO FICTICIO", "Protocolo nº:", "Requerente: ", "Assunto: **ASSUNTO FICTICIO", "**AO OUTRO SETOR FICTICIO:",
        "", "", "", "", ">Cidade Ficticia, dia de mês de ano", "", "______________", "NOME FICTICIO");

    public const string MdCorrecao = "modelo-despacho-analise-correcao.md";
    public const string MdAprovacao = "modelo-despacho-aprovacao-gabinete.md";

    /// <summary>Texto de referência fictício da correção (formato do kit, conteúdo inventado).</summary>
    public static string TextoCorrecao(string fechamento = "Para prosseguimento ficticio, revisar os itens no prazo ficticio.") =>
        "# Ficticio\n\n## Conteúdo na ordem do documento\n\n<!-- estilo: Normal -->\nTITULO FICTICIO\n\n<!-- estilo: Normal -->\n"
        + "Exigencia ficticia.\n\n<!-- estilo: Normal -->\n" + fechamento + "\n\n## Seção 1: estrutura\n\n- Ficticio\n";

    /// <summary>Texto de referência fictício da aprovação (formato do kit, conteúdo inventado; só as âncoras são as do kit).</summary>
    public static string TextoAprovacao(string area = "Considerando ficticio que a área total ficticia é de 99,99m².",
        string encaminho = "<!-- estilo: Normal -->\nEncaminho ficticio ao setor ficticio.\n") =>
        "# Ficticio\n\n## Conteúdo na ordem do documento\n\n<!-- estilo: Normal -->\nTITULO FICTICIO\n\n"
        + "<!-- estilo: Normal -->\nConsiderando ficticio que o requerente solicitou algo para obra nova/alteração de obra/ampliação de obra.\n\n"
        + "<!-- estilo: Normal -->\nConsiderando ficticio que o projeto foi analisado.\n\n"
        + "<!-- estilo: Normal -->\n" + area + "\n\n" + encaminho + "\n## Seção 1: estrutura\n\n- Ficticio\n";

    /// <summary>Grava em <paramref name="pasta"/> uma árvore de kit fictícia: registro (revisão 10, com report, correction e approval_simple),
    /// os dois DOCX fictícios e os dois textos de referência fictícios. <paramref name="ajustar"/> mexe no registro antes de gravar.</summary>
    public static string KitFicticio(string pasta, Action<JsonObject>? ajustar = null)
    {
        var modelos = Path.Combine(pasta, "02 - MODELOS DE DOCUMENTOS");
        var despacho = Path.Combine(modelos, "DESPACHO");
        Directory.CreateDirectory(despacho);
        var correcao = Correcao();
        var aprovacao = Aprovacao();
        File.WriteAllBytes(Path.Combine(despacho, "CORRECAO FICTICIA.docx"), correcao);
        File.WriteAllBytes(Path.Combine(despacho, "APROVACAO FICTICIA.docx"), aprovacao);
        var refs = Path.Combine(pasta, TextosFixos.PastaReferencias.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(refs);
        File.WriteAllText(Path.Combine(refs, MdCorrecao), TextoCorrecao(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(refs, MdAprovacao), TextoAprovacao(), new UTF8Encoding(true));

        var registro = new JsonObject
        {
            ["templates_revision"] = 10,
            ["templates"] = new JsonArray
            {
                new JsonObject { ["role"] = "report", ["format"] = "html", ["folder"] = "02 - MODELOS DE DOCUMENTOS/RELATORIO", ["files"] = new JsonObject() },
                new JsonObject { ["role"] = "correction", ["path"] = "02 - MODELOS DE DOCUMENTOS/DESPACHO/CORRECAO FICTICIA.docx", ["sha256"] = Sha(correcao) },
                new JsonObject { ["role"] = "approval_simple", ["path"] = "02 - MODELOS DE DOCUMENTOS/DESPACHO/APROVACAO FICTICIA.docx", ["sha256"] = Sha(aprovacao) },
            },
        };
        ajustar?.Invoke(registro);
        File.WriteAllText(Path.Combine(modelos, "TEMPLATES_OFICIAIS.json"), registro.ToJsonString(), new UTF8Encoding(false));
        return pasta;
    }

    public static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));
}
