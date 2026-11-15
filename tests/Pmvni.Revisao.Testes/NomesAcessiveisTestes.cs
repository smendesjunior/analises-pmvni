using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Nomes acessíveis das listas (auditoria A3/M10): o Narrador lia o nome do tipo ("Pmvni.App.ItemNavegacao"); agora cada linha
/// diz o que mostra, inclusive a situação que antes era só a cor do ponto. Datas num formato só (B10).</summary>
public class NomesAcessiveisTestes
{
    [Fact]
    public void item_do_relatorio_com_situacao_alterado_e_folha()
    {
        Assert.Equal("Item 07, Recuos, Não atende, alterado, folha 4", NomesAcessiveis.ItemNavegacao("07  Recuos", false, "Não atende", true, 4));
        Assert.Equal("Item 12, Taxa de ocupação, folha 9", NomesAcessiveis.ItemNavegacao("12  Taxa de ocupação", false, "", false, 9));
        Assert.Equal("Figura 3, Planta de situação, folha 2", NomesAcessiveis.ItemNavegacao("Figura 3  Planta de situação", false, "", false, 2));
        Assert.Equal("Seção LOCALIZAÇÃO NA BASE MUNICIPAL, folha 1", NomesAcessiveis.ItemNavegacao("LOCALIZAÇÃO NA BASE MUNICIPAL", true, "", false, 1));
        Assert.Equal("Capa, folha 1", NomesAcessiveis.ItemNavegacao("Capa", true, "", false, 1));
    }

    [Fact]
    public void linhas_de_processos_e_despachos_dizem_o_que_mostram()
    {
        var p = new Processo(Path.Combine("x", "2099", "0012-2099 FULANO"), "0012-2099 FULANO", 12, 2099, "FULANO DE TAL");
        var lp = new LinhaProcesso(p, new DateTime(2099, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(lp.Titulo + ", " + lp.Detalhe, lp.NomeAcessivel);
        Assert.Equal(lp.NomeAcessivel, lp.ToString());
        Assert.DoesNotContain("Pmvni.", lp.ToString());

        var titulo = new LinhaDetalhe(TipoLinhaDetalhe.Titulo, "Relatórios");
        Assert.Equal("Grupo Relatórios", titulo.NomeAcessivel);
        var arquivo = new LinhaDetalhe(TipoLinhaDetalhe.Arquivo, "planta.pdf", "PROJETO", "c:\\x\\planta.pdf");
        Assert.Equal("planta.pdf, PROJETO", arquivo.NomeAcessivel);
        Assert.Equal("Nada aqui", new LinhaDetalhe(TipoLinhaDetalhe.Vazio, "Nada aqui").NomeAcessivel);

        var grupo = ItemDespacho.DoGrupo("Processo 0012/2099");
        Assert.Equal("Grupo Processo 0012/2099", grupo.NomeAcessivel);
        Assert.Equal(grupo.NomeAcessivel, grupo.ToString());
    }

    [Fact]
    public void datas_num_formato_so()
    {
        Assert.Equal("02/01/2099", Formatos.Data(new DateTime(2099, 1, 2)));
        Assert.Equal("02/01/2099 13:05", Formatos.DataHora(new DateTime(2099, 1, 2, 13, 5, 9)));
        Assert.Equal("pt-BR", Formatos.Cultura.Name);
    }
}
