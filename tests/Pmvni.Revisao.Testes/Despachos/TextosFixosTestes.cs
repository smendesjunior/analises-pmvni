using System.Text;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

public sealed class TextosFixosTestes
{
    [Fact]
    public void aprovacao_tem_4_paragrafos_com_as_lacunas()
    {
        var tx = TextosFixos.Ler(Ambiente.ExigirKit(), TipoDespacho.Aprovacao);

        Assert.Equal(TipoDespacho.Aprovacao, tx.Tipo);
        Assert.Equal("modelo-despacho-aprovacao-gabinete.md", tx.Arquivo);
        Assert.Equal("0a0da0207f9ce04e478b2e1abd5e3f0e1a86a8f54fc04035fddc6e539eb4dbe7", tx.Sha256);
        Assert.Equal(new[] { "considerando_pedido", "considerando_analise", "considerando_area", "encaminho" }, tx.Paragrafos.Select(p => p.Id));
        var pedido = tx.Paragrafos[0].Texto;
        Assert.Single(pedido.Split("{intervencao}")[1..]);
        Assert.DoesNotContain("obra nova/", pedido);
        var area = tx.Paragrafos[2].Texto;
        Assert.Contains("{area}m²", area);
        Assert.DoesNotContain(area, char.IsAsciiDigit);
        Assert.StartsWith("Considerando", tx.Paragrafos[1].Texto);
        Assert.StartsWith("Encaminho", tx.Paragrafos[3].Texto);
        Assert.All(tx.Paragrafos, p => Assert.DoesNotContain("XX", p.Texto));
    }

    [Fact]
    public void correcao_tem_o_fechamento()
    {
        var tx = TextosFixos.Ler(Ambiente.ExigirKit(), TipoDespacho.Correcao);

        Assert.Equal("modelo-despacho-analise-correcao.md", tx.Arquivo);
        Assert.Equal("465d6432580f867ef60b0537931fc77c44b54cc9e63c3e1fe92bf007ed441e21", tx.Sha256);
        var p = Assert.Single(tx.Paragrafos);
        Assert.Equal("fechamento", p.Id);
        Assert.StartsWith("Para prosseguimento", p.Texto);
    }

    [Fact]
    public void referencia_ficticia_com_bom_e_lida()
    {
        using var pasta = Ambiente.PastaDeTeste("textos-bom");
        DocxFicticio.KitFicticio(pasta); // a aprovação fictícia é gravada com BOM

        var tx = TextosFixos.Ler(pasta, TipoDespacho.Aprovacao);

        Assert.Equal("Considerando ficticio que o requerente solicitou algo para {intervencao}.", tx.Paragrafos[0].Texto);
        Assert.Equal("Considerando ficticio que a área total ficticia é de {area}m².", tx.Paragrafos[2].Texto);
    }

    [Theory]
    [InlineData("sem_secao")]
    [InlineData("dois_encaminho")]
    [InlineData("sem_lacuna_de_area")]
    [InlineData("sem_fechamento")]
    public void referencia_fora_do_formato_lanca(string caso)
    {
        using var pasta = Ambiente.PastaDeTeste("textos-" + caso);
        DocxFicticio.KitFicticio(pasta);
        var refs = Path.Combine(pasta, TextosFixos.PastaReferencias);
        var (tipo, arquivo, texto) = caso switch
        {
            "sem_secao" => (TipoDespacho.Aprovacao, DocxFicticio.MdAprovacao,
                DocxFicticio.TextoAprovacao().Replace("## Conteúdo na ordem do documento", "## Outra seção", StringComparison.Ordinal)),
            "dois_encaminho" => (TipoDespacho.Aprovacao, DocxFicticio.MdAprovacao,
                DocxFicticio.TextoAprovacao(encaminho: "<!-- estilo: Normal -->\nEncaminho um.\n\n<!-- estilo: Normal -->\nEncaminho dois.\n")),
            "sem_lacuna_de_area" => (TipoDespacho.Aprovacao, DocxFicticio.MdAprovacao,
                DocxFicticio.TextoAprovacao(area: "Considerando ficticio que a área total ficticia será informada.")),
            _ => (TipoDespacho.Correcao, DocxFicticio.MdCorrecao, DocxFicticio.TextoCorrecao("Fim ficticio sem o começo esperado.")),
        };
        File.WriteAllText(Path.Combine(refs, arquivo), texto, new UTF8Encoding(false));

        var e = Assert.Throws<TextosForaDoPadraoException>(() => TextosFixos.Ler(pasta, tipo));

        Assert.StartsWith("O texto de referência do kit (" + arquivo + ")", e.Message);
    }
}
