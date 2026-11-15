using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

public sealed class PreenchimentoDocxTestes
{
    internal static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    static readonly XNamespace W14 = "http://schemas.microsoft.com/office/word/2010/wordml";

    internal static byte[] Modelo(TipoDespacho t) =>
        File.ReadAllBytes(RegistroModelos.Ler(Ambiente.ExigirKit()).Modelos.Single(m => m.Tipo == t).Caminho);

    internal static DespachoFinal Final(TipoDespacho t, int exigencias = 2, params string[] grupos)
    {
        if (t == TipoDespacho.Aprovacao)
            return new DespachoFinal(t, "0001/2099", "FULANO DE EXEMPLO (FICTICIO)", "SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA", "10 de março de 2099",
                Array.Empty<GrupoFinal>(), new[] { "Corpo um.", "Corpo dois.", "Corpo três.", "Corpo quatro." });
        var lista = new List<GrupoFinal>();
        var titulos = grupos.Length == 0 ? new[] { "GERAL E DOCUMENTAÇÃO" } : grupos;
        var n = 0;
        foreach (var g in titulos)
        {
            var ex = new List<ExigenciaFinal>();
            for (var i = 0; i < exigencias; i++) { n++; ex.Add(new ExigenciaFinal("Exigência fictícia " + n, " (Fundamento fictício " + n + ").")); }
            lista.Add(new GrupoFinal(g, ex));
        }
        return new DespachoFinal(t, "0001/2099", "FULANO DE EXEMPLO (FICTICIO)", "SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA", "10 de março de 2099",
            lista, new[] { "Fechamento fictício." });
    }

    internal static XDocument Documento(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var s = zip.GetEntry("word/document.xml")!.Open();
        return XDocument.Load(s, LoadOptions.PreserveWhitespace);
    }

    internal static List<XElement> Paragrafos(XDocument d) => d.Root!.Element(W + "body")!.Elements(W + "p").ToList();

    internal static string Texto(XElement p) => string.Concat(p.Descendants(W + "t").Select(t => t.Value));

    static List<XElement> Runs(XElement p) => p.Elements(W + "r").Where(r => r.Element(W + "t") is not null).ToList();

    static XElement Achar(XDocument d, Func<string, bool> f) => Paragrafos(d).Single(p => f(Texto(p)));

    static string PPr(XElement p) => p.Element(W + "pPr")?.ToString(SaveOptions.DisableFormatting) ?? "";

    [Theory]
    [InlineData("Correcao")]
    [InlineData("Aprovacao")]
    public void so_o_document_xml_muda(string tipo)
    {
        var t = Enum.Parse<TipoDespacho>(tipo);
        var modelo = Modelo(t);

        var saida = PreenchimentoDocx.Preencher(modelo, Final(t));

        using var a = new ZipArchive(new MemoryStream(modelo), ZipArchiveMode.Read);
        using var b = new ZipArchive(new MemoryStream(saida), ZipArchiveMode.Read);
        Assert.Equal(a.Entries.Select(e => e.FullName), b.Entries.Select(e => e.FullName));
        Assert.Equal(a.Entries.Select(e => e.LastWriteTime), b.Entries.Select(e => e.LastWriteTime));
        foreach (var e in a.Entries.Where(e => e.FullName != "word/document.xml"))
            Assert.True(Bytes(e).AsSpan().SequenceEqual(Bytes(b.GetEntry(e.FullName)!)), e.FullName);
        var xml = Encoding.UTF8.GetString(Bytes(b.GetEntry("word/document.xml")!));
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>", xml);
        Assert.NotEqual('﻿', xml[0]);
    }

    static byte[] Bytes(ZipArchiveEntry e)
    {
        using var s = e.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public void ler_modelo_acha_as_ancoras()
    {
        var c = PreenchimentoDocx.LerModelo(Modelo(TipoDespacho.Correcao), TipoDespacho.Correcao);
        Assert.Equal("SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA", c.Assunto);
        Assert.Equal("GERAL E DOCUMENTAÇÃO", c.TituloGrupo);
        Assert.Equal(4, c.Vagas);
        Assert.Equal(4, c.Paragrafos.Count(p => p.Papel == PapelParagrafo.Vaga));
        Assert.Equal("AO PROFISSIONAL RESPONSÁVEL:", c.Paragrafos.Single(p => p.Papel == PapelParagrafo.Destinatario).Texto);
        Assert.True(c.Paragrafos.Single(p => p.Papel == PapelParagrafo.Grupo).Negrito);
        Assert.Equal("right", c.Paragrafos.Single(p => p.Papel == PapelParagrafo.Data).Alinhamento);

        var a = PreenchimentoDocx.LerModelo(Modelo(TipoDespacho.Aprovacao), TipoDespacho.Aprovacao);
        Assert.Equal("", a.TituloGrupo);
        Assert.Equal(4, a.Vagas);
        Assert.DoesNotContain(a.Paragrafos, p => p.Papel == PapelParagrafo.Grupo);
        Assert.Equal("AO SETOR DE FISCALIZAÇÃO:", a.Paragrafos.Single(p => p.Papel == PapelParagrafo.Destinatario).Texto);
    }

    [Fact]
    public void valores_do_cabecalho_em_negrito_depois_do_rotulo()
    {
        var d = Documento(PreenchimentoDocx.Preencher(Modelo(TipoDespacho.Correcao), Final(TipoDespacho.Correcao)));

        var protocolo = Runs(Achar(d, t => t.StartsWith("Protocolo nº:", StringComparison.Ordinal)));
        Assert.Equal(new[] { "Protocolo nº:", " 0001/2099" }, protocolo.Select(r => r.Element(W + "t")!.Value));
        var rpr = protocolo[1].Element(W + "rPr")!;
        Assert.NotNull(rpr.Element(W + "b"));
        Assert.Equal("24", rpr.Element(W + "sz")!.Attribute(W + "val")!.Value);
        Assert.Null(protocolo[0].Element(W + "rPr")!.Element(W + "b"));

        var requerente = Runs(Achar(d, t => t.StartsWith("Requerente:", StringComparison.Ordinal)));
        Assert.Equal(new[] { "Requerente: ", "FULANO DE EXEMPLO (FICTICIO)" }, requerente.Select(r => r.Element(W + "t")!.Value));
        Assert.NotNull(requerente[1].Element(W + "rPr")!.Element(W + "b"));

        var assunto = Runs(Achar(d, t => t.StartsWith("Assunto:", StringComparison.Ordinal)));
        Assert.Equal(new[] { "Assunto: ", "SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA" }, assunto.Select(r => r.Element(W + "t")!.Value));
    }

    [Fact]
    public void assunto_editado_troca_so_o_run_em_negrito()
    {
        var f = Final(TipoDespacho.Aprovacao) with { Assunto = "OUTRO ASSUNTO" };
        var d = Documento(PreenchimentoDocx.Preencher(Modelo(TipoDespacho.Aprovacao), f));

        Assert.Equal("Assunto: OUTRO ASSUNTO", Texto(Achar(d, t => t.StartsWith("Assunto:", StringComparison.Ordinal))));
    }

    [Fact]
    public void data_por_extenso_nos_dois_modelos()
    {
        foreach (var t in new[] { TipoDespacho.Correcao, TipoDespacho.Aprovacao })
        {
            var d = Documento(PreenchimentoDocx.Preencher(Modelo(t), Final(t)));
            var textos = Paragrafos(d).Select(Texto).ToList();
            Assert.Contains("Venda Nova do Imigrante/ES, 10 de março de 2099", textos);
            Assert.DoesNotContain(textos, x => x.Contains("dia de mês de ano", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void exigencias_ocupam_as_vagas_e_as_que_sobram_saem()
    {
        var modelo = Modelo(TipoDespacho.Correcao);
        var pprVaga = PPr(Paragrafos(Documento(modelo)).First(p => Texto(p).Length == 0 && PPr(p).Contains("both", StringComparison.Ordinal)));

        var d = Documento(PreenchimentoDocx.Preencher(modelo, Final(TipoDespacho.Correcao)));
        var ps = Paragrafos(d);
        var grupo = ps.FindIndex(p => Texto(p) == "GERAL E DOCUMENTAÇÃO");
        var data = ps.FindIndex(p => Texto(p).StartsWith("Venda Nova", StringComparison.Ordinal));
        var meio = ps.Skip(grupo + 1).Take(data - grupo - 1).ToList();

        Assert.Equal(3, meio.Count);
        Assert.All(meio, p => Assert.Equal(pprVaga, PPr(p)));
        Assert.Equal(new[] { "Exigência fictícia 1", " (Fundamento fictício 1)." }, Runs(meio[0]).Select(r => r.Element(W + "t")!.Value));
        Assert.Equal(new[] { "Exigência fictícia 2", " (Fundamento fictício 2)." }, Runs(meio[1]).Select(r => r.Element(W + "t")!.Value));
        Assert.Equal(new[] { "Fechamento fictício." }, Runs(meio[2]).Select(r => r.Element(W + "t")!.Value));
        var linha = ps.FindIndex(p => Texto(p).StartsWith("____", StringComparison.Ordinal));
        Assert.Equal("", Texto(ps[linha - 1]));
        Assert.Equal(linha - 1, data + 1);
        Assert.Equal(1, ps.Count(p => Texto(p).Length == 0 && PPr(p) == pprVaga)); // só a linha em branco antes da assinatura
    }

    [Fact]
    public void mais_exigencias_que_vagas_clonam_a_vaga()
    {
        var modelo = Modelo(TipoDespacho.Correcao);
        var pprVaga = PPr(Paragrafos(Documento(modelo)).First(p => Texto(p).Length == 0 && PPr(p).Contains("both", StringComparison.Ordinal)));

        var ps = Paragrafos(Documento(PreenchimentoDocx.Preencher(modelo, Final(TipoDespacho.Correcao, 7))));

        Assert.Equal(8, ps.Count(p => Texto(p).Length > 0 && PPr(p) == pprVaga));
    }

    [Fact]
    public void grupos_clonam_o_titulo_de_grupo()
    {
        var ps = Paragrafos(Documento(PreenchimentoDocx.Preencher(Modelo(TipoDespacho.Correcao),
            Final(TipoDespacho.Correcao, 2, "GERAL E DOCUMENTAÇÃO", "PRANCHA 01"))));

        var g1 = ps.Single(p => Texto(p) == "GERAL E DOCUMENTAÇÃO");
        var g2 = ps.Single(p => Texto(p) == "PRANCHA 01");
        Assert.Equal(PPr(g1), PPr(g2));
        Assert.Equal(Runs(g1)[0].Element(W + "rPr")!.ToString(), Runs(g2)[0].Element(W + "rPr")!.ToString());
        var textos = ps.Select(Texto).ToList();
        Assert.Equal(new[] { "GERAL E DOCUMENTAÇÃO", "Exigência fictícia 1 (Fundamento fictício 1).", "Exigência fictícia 2 (Fundamento fictício 2).",
            "PRANCHA 01", "Exigência fictícia 3 (Fundamento fictício 3).", "Exigência fictícia 4 (Fundamento fictício 4).", "Fechamento fictício." },
            textos.Skip(textos.IndexOf("GERAL E DOCUMENTAÇÃO")).Take(7));
    }

    [Fact]
    public void aprovacao_ocupa_as_4_vagas_com_o_corpo()
    {
        var ps = Paragrafos(Documento(PreenchimentoDocx.Preencher(Modelo(TipoDespacho.Aprovacao), Final(TipoDespacho.Aprovacao))));
        var textos = ps.Select(Texto).ToList();

        var i = textos.IndexOf("Corpo um.");
        Assert.Equal(new[] { "Corpo um.", "Corpo dois.", "Corpo três.", "Corpo quatro." }, textos.Skip(i).Take(4));
        Assert.StartsWith("Venda Nova", textos[i + 4]);
        Assert.Contains("AO SETOR DE FISCALIZAÇÃO:", textos);
        var destinatario = ps.Single(p => Texto(p) == "AO SETOR DE FISCALIZAÇÃO:");
        Assert.Equal(3, Runs(destinatario).Count);
    }

    [Fact]
    public void clones_sem_paraId_repetido()
    {
        var d = Documento(PreenchimentoDocx.Preencher(Modelo(TipoDespacho.Correcao), Final(TipoDespacho.Correcao, 5, "G1", "G2", "G3")));

        var ids = d.Descendants().Select(e => e.Attribute(W14 + "paraId")?.Value).Where(v => v is not null).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        var textIds = d.Descendants().Select(e => e.Attribute(W14 + "textId")?.Value).Where(v => v is not null && v != "77777777").ToList();
        Assert.Equal(textIds.Count, textIds.Distinct().Count());
    }

    [Fact]
    public void caracteres_especiais_e_emoji_sao_texto()
    {
        const string especial = "a & <b> \"x\" 'y' 😀";
        var f = Final(TipoDespacho.Correcao) with
        {
            Grupos = new[] { new GrupoFinal("GERAL E DOCUMENTAÇÃO", new[] { new ExigenciaFinal(especial, "") }) },
        };

        var saida = PreenchimentoDocx.Preencher(Modelo(TipoDespacho.Correcao), f);

        var xml = Encoding.UTF8.GetString(Bytes(new ZipArchive(new MemoryStream(saida)).GetEntry("word/document.xml")!));
        XDocument.Parse(xml);
        var run = Documento(saida).Descendants(W + "r").Single(r => r.Element(W + "t")?.Value == especial);
        Assert.Null(run.Element(W + "rPr")?.Element(W + "b"));
    }

    [Fact]
    public void rotulo_fora_de_run_direto_lanca()
    {
        const string run = "<w:r><w:rPr><w:sz w:val=\"24\"/></w:rPr><w:t xml:space=\"preserve\">Requerente: </w:t></w:r>";
        var docx = ValidacaoDespachoTestes.Trocar(DocxFicticio.Correcao(), "word/document.xml", b =>
        {
            var xml = Encoding.UTF8.GetString(b);
            Assert.Contains(run, xml);
            return new UTF8Encoding(false).GetBytes(xml.Replace(run, "<w:hyperlink>" + run + "</w:hyperlink>", StringComparison.Ordinal));
        });

        Assert.Equal("Requerente:", Assert.Throws<ModeloForaDoPadraoException>(() => PreenchimentoDocx.LerModelo(docx, TipoDespacho.Correcao)).Ancora);
    }

    [Fact]
    public void ancora_ausente_ou_repetida_lanca()
    {
        var sem = DocxFicticio.Criar("Protocolo nº:", "Assunto: **X", "**AO X:", "**G", "", ">dia de mês de ano", "", "______________");
        Assert.Equal("Requerente:", Assert.Throws<ModeloForaDoPadraoException>(() => PreenchimentoDocx.LerModelo(sem, TipoDespacho.Correcao)).Ancora);

        var dois = DocxFicticio.Criar("Protocolo nº:", "Protocolo nº:", "Requerente: ", "Assunto: **X", "**AO X:", "**G", "", ">dia de mês de ano", "", "______________");
        Assert.Equal("Protocolo nº:", Assert.Throws<ModeloForaDoPadraoException>(() => PreenchimentoDocx.LerModelo(dois, TipoDespacho.Correcao)).Ancora);

        var ok = PreenchimentoDocx.LerModelo(DocxFicticio.Correcao(), TipoDespacho.Correcao);
        Assert.Equal("ASSUNTO FICTICIO", ok.Assunto);
        Assert.Equal("GRUPO FICTICIO", ok.TituloGrupo);
        Assert.Equal(2, ok.Vagas);
    }
}
