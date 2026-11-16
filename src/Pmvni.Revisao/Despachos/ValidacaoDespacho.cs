using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace Pmvni.Revisao;

/// <summary>Validação do DOCX gerado contra o modelo do kit (desenho §10.6), antes de qualquer gravação.</summary>
public static class ValidacaoDespacho
{
    static readonly XNamespace W = PreenchimentoDocx.W;

    /// <summary>Porta de <c>validate()</c> do <c>validate_template_fidelity.py</c> do kit, com as mesmas mensagens: partes fixas idênticas,
    /// seções preservadas, fontes e tamanhos diretos só com valores que o modelo já tem, tabelas iguais.</summary>
    public static IReadOnlyList<string> Comparar(byte[] modelo, byte[] documento)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        ArgumentNullException.ThrowIfNull(documento);
        var erros = new List<string>();
        try
        {
            var fonte = Entradas(modelo);
            var saida = Entradas(documento);
            var xmlFonte = Xml(fonte[PreenchimentoDocx.Documento]);
            var xmlSaida = Xml(saida[PreenchimentoDocx.Documento]);

            foreach (var (nome, bytes) in fonte)
            {
                var fixa = nome is "word/styles.xml" or "word/fontTable.xml" or "word/numbering.xml"
                           || nome.StartsWith("word/theme/", StringComparison.Ordinal) || nome.StartsWith("word/media/", StringComparison.Ordinal)
                           || (CabecalhoOuRodape(nome) && nome.EndsWith(".xml", StringComparison.Ordinal));
                if (fixa && (!saida.TryGetValue(nome, out var b) || !b.AsSpan().SequenceEqual(bytes)))
                    erros.Add("Componente original alterado/ausente: " + nome);
            }

            var secoesFonte = xmlFonte.Descendants(W + "sectPr").Select(Serializar).ToList();
            var secoesSaida = xmlSaida.Descendants(W + "sectPr").Select(Serializar).ToList();
            if (secoesSaida.Count < secoesFonte.Count || secoesSaida.Any(s => !secoesFonte.Contains(s)))
                erros.Add("Seções, margens, formato, orientação ou vínculos de cabeçalho/rodapé alterados.");

            var tags = new[] { "rFonts", "sz", "szCs" };
            var permitidos = tags.ToDictionary(t => t, _ => new HashSet<string>(StringComparer.Ordinal));
            foreach (var (nome, bytes) in fonte)
            {
                if (!(nome == "word/styles.xml" || nome == PreenchimentoDocx.Documento || (CabecalhoOuRodape(nome) && nome.EndsWith(".xml", StringComparison.Ordinal))))
                    continue;
                var x = nome == PreenchimentoDocx.Documento ? xmlFonte : Xml(bytes);
                foreach (var t in tags)
                    foreach (var e in x.Descendants(W + t)) permitidos[t].Add(Atributos(e));
            }
            foreach (var t in tags)
                if (xmlSaida.Descendants(W + t).Any(e => !permitidos[t].Contains(Atributos(e))))
                    erros.Add("Fonte ou tamanho direto não existente no template: " + t);

            var antes = Tabelas(xmlFonte);
            var depois = Tabelas(xmlSaida);
            if (depois.Count < antes.Count || !depois.Take(antes.Count).SequenceEqual(antes))
                erros.Add("Estrutura, propriedades ou colunas das tabelas originais alteradas.");
        }
        catch (Exception e) when (e is InvalidDataException or IOException or KeyNotFoundException or XmlException)
        {
            erros.Add("Não foi possível comparar os DOCX: " + e.Message);
        }
        return erros;
    }

    /// <summary>Conferência do conteúdo: nenhum resto do modelo, os valores congelados presentes (na ordem), a linha em branco antes da
    /// assinatura e toda parte ≠ <c>word/document.xml</c> igual à do modelo.</summary>
    public static IReadOnlyList<string> Conteudo(byte[] modelo, byte[] documento, DespachoFinal d)
    {
        ArgumentNullException.ThrowIfNull(modelo);
        ArgumentNullException.ThrowIfNull(documento);
        ArgumentNullException.ThrowIfNull(d);
        var erros = new List<string>();
        try
        {
            var fonte = Entradas(modelo);
            var saida = Entradas(documento);
            var textos = Xml(saida[PreenchimentoDocx.Documento]).Root!.Element(W + "body")!.Elements(W + "p").Select(PreenchimentoDocx.Texto).ToList();

            foreach (var resto in new[] { PreenchimentoDocx.MarcaData, "XX", "[ ]", "{{", TextosFixos.LacunaIntervencao, TextosFixos.LacunaArea })
                if (textos.Any(t => t.Contains(resto, StringComparison.Ordinal)))
                    erros.Add("Texto do modelo não preenchido: " + resto);

            void Valor(string rotulo, string nome, string valor)
            {
                var p = textos.FirstOrDefault(t => t.TrimStart().StartsWith(rotulo, StringComparison.Ordinal));
                if (valor.Length == 0 || p is null || !p.Contains(valor, StringComparison.Ordinal)) erros.Add("O despacho não traz o valor de " + nome + ".");
            }
            Valor(PreenchimentoDocx.RotuloProtocolo, "Protocolo nº", d.Protocolo);
            Valor(PreenchimentoDocx.RotuloRequerente, "Requerente", d.Requerente);
            Valor(PreenchimentoDocx.RotuloAssunto, "Assunto", d.Assunto);
            if (!textos.Any(t => t.Contains(d.DataPorExtenso, StringComparison.Ordinal))) erros.Add("O despacho não traz o valor de data.");

            var cursor = 0;
            var numero = 0;
            int Achar(string texto)
            {
                for (var i = cursor; i < textos.Count; i++)
                    if (textos[i] == texto) return i;
                return -1;
            }
            foreach (var g in d.Grupos)
            {
                var titulo = Achar(g.Titulo);
                if (titulo >= 0) cursor = titulo + 1;
                for (var n = 0; n < g.Exigencias.Count; n++)
                {
                    var i = titulo < 0 ? -1 : Achar(ConteudoDespacho.Numero(++numero) + g.Exigencias[n].Texto + g.Exigencias[n].Fundamento);
                    if (i < 0) erros.Add("O despacho não traz a exigência " + (n + 1) + " do grupo " + g.Titulo + ".");
                    else cursor = i + 1;
                }
            }
            for (var n = 0; n < d.Corpo.Count; n++)
            {
                var i = Achar(d.Corpo[n]);
                if (i < 0) erros.Add("O despacho não traz o parágrafo " + (n + 1) + " do corpo.");
                else cursor = i + 1;
            }

            var linha = textos.FindIndex(t => t.Trim().Length >= 10 && t.Trim().All(c => c == '_'));
            if (linha <= 0 || textos[linha - 1].Trim().Length != 0) erros.Add("Falta a linha em branco antes da assinatura.");

            foreach (var (nome, bytes) in fonte)
                if (nome != PreenchimentoDocx.Documento && (!saida.TryGetValue(nome, out var b) || !b.AsSpan().SequenceEqual(bytes)))
                    erros.Add("Parte do modelo alterada: " + nome);
            foreach (var nome in saida.Keys.Where(n => !fonte.ContainsKey(n)))
                erros.Add("Parte do modelo alterada: " + nome);
        }
        catch (Exception e) when (e is InvalidDataException or IOException or KeyNotFoundException or XmlException or NullReferenceException)
        {
            erros.Add("Não foi possível comparar os DOCX: " + e.Message);
        }
        return erros;
    }

    static bool CabecalhoOuRodape(string nome) =>
        nome.StartsWith("word/header", StringComparison.Ordinal) || nome.StartsWith("word/footer", StringComparison.Ordinal);

    /// <summary>Entradas do ZIP (nome → conteúdo descomprimido), na ordem; nome repetido: a última vence (como o <c>zipfile</c>).</summary>
    static Dictionary<string, byte[]> Entradas(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var e in zip.Entries)
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            d[e.FullName] = ms.ToArray();
        }
        return d;
    }

    static XDocument Xml(byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return XDocument.Load(ms, LoadOptions.None);
    }

    static string Serializar(XElement? e) => e is null ? "" : e.ToString(SaveOptions.DisableFormatting);

    /// <summary>Os atributos (sem as declarações de namespace) ordenados, como <c>tuple(sorted(e.attrib.items()))</c>.</summary>
    static string Atributos(XElement e) =>
        string.Join("\u0001", e.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => a.Name + "=" + a.Value).Order(StringComparer.Ordinal));

    static List<(string, string)> Tabelas(XDocument x) =>
        x.Descendants(W + "tbl").Select(t => (Serializar(t.Element(W + "tblPr")), Serializar(t.Element(W + "tblGrid")))).ToList();
}
