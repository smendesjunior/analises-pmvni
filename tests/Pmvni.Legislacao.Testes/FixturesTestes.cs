using Pmvni.Legislacao.Testes.Ficticios;
using Xunit;

namespace Pmvni.Legislacao.Testes;

public sealed class FixturesTestes
{
    /// <summary>Os 9 PDFs versionados em tests/corpus/legislacao são os do gerador, byte a byte. Com <c>PMVNI_REGERAR_FIXTURES=1</c>
    /// o teste os grava (é o único jeito de mexer neles) e passa.</summary>
    [Fact]
    public void fixtures_versionadas_sao_as_do_gerador()
    {
        var regerar = Environment.GetEnvironmentVariable("PMVNI_REGERAR_FIXTURES") == "1";
        Assert.Equal(9, PdfLegislacaoFicticio.Todos.Count);
        foreach (var (rel, bytes) in PdfLegislacaoFicticio.Todos)
        {
            var caminho = Path.Combine(Ambiente.Corpus, rel.Replace('/', Path.DirectorySeparatorChar));
            if (regerar)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
                File.WriteAllBytes(caminho, bytes);
            }
            Assert.True(File.Exists(caminho), "fixture ausente: " + rel + " (rode com PMVNI_REGERAR_FIXTURES=1)");
            Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(caminho)), "fixture diferente do gerador: " + rel);
        }
    }

    [Fact]
    public void fixtures_sao_pequenas_e_nao_levam_texto_real()
    {
        foreach (var (rel, bytes) in PdfLegislacaoFicticio.Todos)
            Assert.True(bytes.Length < 8 * 1024, rel + " tem " + bytes.Length + " bytes");
    }

    [Fact]
    public void textos_das_fixtures()
    {
        var t = PdfLegislacaoFicticio.Textos;
        const string lei = "MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf";
        Assert.Equal(
            [
                ["LEI Nº 9.999, DE 1º DE JANEIRO DE 2099 (FICTÍCIA)", "Art. 1º Esta Lei fictícia trata do afastamento frontal das edificações."],
                ["Art. 2º O afastamento frontal mínimo é de 3,00 m.", "Art. 3º A taxa de ocupação máxima é de 60%."],
                ["ANEXO I – QUADRO FICTÍCIO", "Zona ZR1: afastamento de 3,00 m e taxa de ocupação de 60%."],
            ], t[lei]);
        var v2 = t["MUNICIPAL/LEI 9.999-2099 - FICTICIA (VERSAO 2).pdf"];
        Assert.Equal(4, v2.Length);
        Assert.Equal(t[lei], v2.Take(3));
        Assert.Equal(["Art. 4º Artigo incluído na versão 2 (fictícia)."], v2[3]);
        Assert.Equal(
            [
                ["NORMA TÉCNICA FICTÍCIA Nº 99/2099", "1 OBJETIVO"],
                ["5.1 O afastamento entre edificações fictícias é de 4,00 m."],
            ], t["ESTADUAL - CBMES/NT 99-2099 - FICTICIA.pdf"]);
        Assert.Equal(
            [["LEI Nº 99.999, DE 2 DE JANEIRO DE 2099 (FICTÍCIA)", "Art. 1º Esta Lei federal fictícia trata do parcelamento do solo."]],
            t["FEDERAL/LEI 99.999-2099 - FICTICIA.pdf"]);
        Assert.Equal([["Página com script fictício."]], t["OUTRAS/COM-SCRIPT - FICTICIA.pdf"]);
    }

    [Fact]
    public void estrutura_dos_pdfs_ficticios()
    {
        string Texto(string rel) => System.Text.Encoding.Latin1.GetString(PdfLegislacaoFicticio.Todos[rel]);
        Assert.StartsWith("%PDF-1.4", Texto("MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf"));
        Assert.Contains("/Producer (PdfLegislacaoFicticio)", Texto("MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf"));
        Assert.Contains("/CreationDate (D:20990101000000Z)", Texto("MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf"));
        Assert.Contains("/Count 4", Texto("MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf")); // marcadores
        Assert.Contains("/Count 5", Texto("MUNICIPAL/LEI 9.999-2099 - FICTICIA (VERSAO 2).pdf"));
        Assert.DoesNotContain("/Outlines", Texto("ESTADUAL - CBMES/NT 99-2099 - FICTICIA.pdf"));
        Assert.Contains("/Encrypt", Texto("OUTRAS/SENHA - FICTICIA.pdf"));
        Assert.Contains("/OpenAction", Texto("OUTRAS/COM-SCRIPT - FICTICIA.pdf"));
        Assert.Contains("https://exemplo.invalid/vazou", Texto("OUTRAS/COM-SCRIPT - FICTICIA.pdf"));
        Assert.Equal("%PDF-1.4\n" + new string('x', 2048), Texto("OUTRAS/CORROMPIDO - FICTICIA.pdf"));
        Assert.Equal("Arquivo de texto fictício, não é PDF.\n", System.Text.Encoding.UTF8.GetString(PdfLegislacaoFicticio.Todos["OUTRAS/NAO-E-PDF - FICTICIA.pdf"]));
    }
}
