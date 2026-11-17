using System.IO.Compression;
using System.Text;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

public sealed class ValidacaoDespachoTestes
{
    /// <summary>Uma cópia do DOCX com a entrada <paramref name="nome"/> trocada por <paramref name="f"/> (as outras copiadas).</summary>
    internal static byte[] Trocar(byte[] docx, string nome, Func<byte[], byte[]> f)
    {
        using var origem = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        using var ms = new MemoryStream();
        using (var destino = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var e in origem.Entries)
            {
                byte[] bytes;
                using (var s = e.Open()) { using var m = new MemoryStream(); s.CopyTo(m); bytes = m.ToArray(); }
                if (e.FullName == nome) bytes = f(bytes);
                var n = destino.CreateEntry(e.FullName);
                n.LastWriteTime = e.LastWriteTime;
                using var w = n.Open();
                w.Write(bytes);
            }
        return ms.ToArray();
    }

    internal static byte[] TrocarXml(byte[] docx, string de, string para) =>
        Trocar(docx, "word/document.xml", b =>
        {
            var xml = Encoding.UTF8.GetString(b);
            Assert.Contains(de, xml);
            return new UTF8Encoding(false).GetBytes(xml.Replace(de, para, StringComparison.Ordinal));
        });

    [Theory]
    [InlineData("Correcao")]
    [InlineData("Aprovacao")]
    public void despacho_gerado_passa_na_comparacao_do_kit(string tipo)
    {
        var t = Enum.Parse<TipoDespacho>(tipo);
        var modelo = PreenchimentoDocxTestes.Modelo(t);
        var f = PreenchimentoDocxTestes.Final(t, 6, "GERAL E DOCUMENTAÇÃO", "PRANCHA 01");

        var docx = PreenchimentoDocx.Preencher(modelo, f);

        Assert.Empty(ValidacaoDespacho.Comparar(modelo, docx));
        Assert.Empty(ValidacaoDespacho.Conteudo(modelo, docx, f));
    }

    [Fact]
    public void parte_fixa_alterada_e_apontada()
    {
        var modelo = PreenchimentoDocxTestes.Modelo(TipoDespacho.Correcao);
        var f = PreenchimentoDocxTestes.Final(TipoDespacho.Correcao);
        var docx = Trocar(PreenchimentoDocx.Preencher(modelo, f), "word/styles.xml", b =>
        {
            var c = (byte[])b.Clone();
            c[^3] ^= 0x01;
            return c;
        });

        Assert.Contains("Componente original alterado/ausente: word/styles.xml", ValidacaoDespacho.Comparar(modelo, docx));
        Assert.Contains("Parte do modelo alterada: word/styles.xml", ValidacaoDespacho.Conteudo(modelo, docx, f));
    }

    [Fact]
    public void fonte_direta_nova_e_apontada()
    {
        var modelo = PreenchimentoDocxTestes.Modelo(TipoDespacho.Correcao);
        var docx = TrocarXml(PreenchimentoDocx.Preencher(modelo, PreenchimentoDocxTestes.Final(TipoDespacho.Correcao)),
            "<w:t>INFORMAÇÕES GERAIS</w:t>", "<w:t>INFORMAÇÕES GERAIS</w:t></w:r><w:r><w:rPr><w:rFonts w:ascii=\"Arial\" w:hAnsi=\"Arial\"/></w:rPr><w:t>x</w:t>");

        Assert.Equal(new[] { "Fonte ou tamanho direto não existente no template: rFonts" }, ValidacaoDespacho.Comparar(modelo, docx));
    }

    [Fact]
    public void secao_alterada_e_apontada()
    {
        var modelo = PreenchimentoDocxTestes.Modelo(TipoDespacho.Aprovacao);
        var docx = TrocarXml(PreenchimentoDocx.Preencher(modelo, PreenchimentoDocxTestes.Final(TipoDespacho.Aprovacao)),
            "w:bottom=\"2113\"", "w:bottom=\"2114\""); // a margem de cima o editor corrige (CabecalhoRodapeTestes); as outras não mudam

        Assert.Equal(new[] { "Seções, margens, formato, orientação ou vínculos de cabeçalho/rodapé alterados." }, ValidacaoDespacho.Comparar(modelo, docx));
    }

    [Fact]
    public void docx_ilegivel_e_apontado()
    {
        var modelo = PreenchimentoDocxTestes.Modelo(TipoDespacho.Aprovacao);

        var erro = Assert.Single(ValidacaoDespacho.Comparar(modelo, new byte[] { 1, 2, 3 }));

        Assert.StartsWith("Não foi possível comparar os DOCX: ", erro);
    }

    [Fact]
    public void resto_do_modelo_e_apontado()
    {
        var modelo = PreenchimentoDocxTestes.Modelo(TipoDespacho.Aprovacao);
        var f = PreenchimentoDocxTestes.Final(TipoDespacho.Aprovacao);
        var docx = TrocarXml(PreenchimentoDocx.Preencher(modelo, f), "Corpo um.", "Corpo um dia de mês de ano {area}.");

        var erros = ValidacaoDespacho.Conteudo(modelo, docx, f);

        Assert.Contains("Texto do modelo não preenchido: dia de mês de ano", erros);
        Assert.Contains("Texto do modelo não preenchido: {area}", erros);
    }

    [Fact]
    public void falta_de_linha_antes_da_assinatura_e_apontada()
    {
        var modelo = PreenchimentoDocxTestes.Modelo(TipoDespacho.Correcao);
        var f = PreenchimentoDocxTestes.Final(TipoDespacho.Correcao);
        var docx = Trocar(PreenchimentoDocx.Preencher(modelo, f), "word/document.xml", b =>
        {
            var d = System.Xml.Linq.XDocument.Parse(Encoding.UTF8.GetString(b), System.Xml.Linq.LoadOptions.PreserveWhitespace);
            var ps = PreenchimentoDocxTestes.Paragrafos(d);
            var linha = ps.FindIndex(p => PreenchimentoDocxTestes.Texto(p).StartsWith("____", StringComparison.Ordinal));
            ps[linha - 1].Remove();
            return new UTF8Encoding(false).GetBytes(d.Declaration + d.Root!.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        });

        Assert.Equal(new[] { "Falta a linha em branco antes da assinatura." }, ValidacaoDespacho.Conteudo(modelo, docx, f));
    }

    [Fact]
    public void valor_ausente_e_apontado()
    {
        var modelo = PreenchimentoDocxTestes.Modelo(TipoDespacho.Correcao);
        var f = PreenchimentoDocxTestes.Final(TipoDespacho.Correcao);
        var docx = TrocarXml(PreenchimentoDocx.Preencher(modelo, f), ">FULANO DE EXEMPLO (FICTICIO)<", "><");

        Assert.Equal(new[] { "O despacho não traz o valor de Requerente." }, ValidacaoDespacho.Conteudo(modelo, docx, f));

        var semExigencia = TrocarXml(PreenchimentoDocx.Preencher(modelo, f), ">Exigência fictícia 2<", ">Outra<");
        Assert.Equal(new[] { "O despacho não traz a exigência 2 do grupo GERAL E DOCUMENTAÇÃO." }, ValidacaoDespacho.Conteudo(modelo, semExigencia, f));
    }
}
