using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Menu dos Ajustes por categorias (auditoria §3.2): 8 categorias na ordem do desenho, "Sobre e suporte" no rodapé.</summary>
public class CategoriasAjustesTestes
{
    [Fact]
    public void oito_categorias_na_ordem_do_desenho()
    {
        Assert.Equal(new[] { "Geral", "Aparência", "Relatórios e revisão", "Pastas e kit", "Fontes de dados", "Despachos e PDF", "Legislação", "Inteligência artificial", "Sobre e suporte" },
            CategoriasAjustes.Menu.Select(c => c.Titulo));
        Assert.Equal(CategoriasAjustes.Menu.Count, CategoriasAjustes.Menu.Select(c => c.Tag).Distinct().Count());
        Assert.Equal(new[] { CategoriasAjustes.Sobre }, CategoriasAjustes.Menu.Where(c => c.Rodape).Select(c => c.Tag));
        Assert.Equal(CategoriasAjustes.Sobre, CategoriasAjustes.Menu[^1].Tag);
        Assert.All(CategoriasAjustes.Menu, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Ajuda));
            Assert.Single(c.Glifo);
            Assert.InRange(c.Glifo[0], (char)0xE000, (char)0xF8FF);
        });
    }

    [Fact]
    public void etiqueta_desconhecida_leva_a_geral()
    {
        Assert.Equal(CategoriasAjustes.Legislacao, CategoriasAjustes.De("legislacao").Tag);
        Assert.Equal(CategoriasAjustes.Geral, CategoriasAjustes.De("nada").Tag);
        Assert.Equal(CategoriasAjustes.Geral, CategoriasAjustes.De(null).Tag);
    }
}
