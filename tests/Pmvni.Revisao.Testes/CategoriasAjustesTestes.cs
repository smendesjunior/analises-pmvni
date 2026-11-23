using System.Xml.Linq;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Menu dos Ajustes por categorias (reorganização de 2026-11-23): 8 categorias e uma subpágina, "Sistema" no rodapé.</summary>
public class CategoriasAjustesTestes
{
    [Fact]
    public void categorias_na_ordem_do_desenho()
    {
        Assert.Equal(new[] { "Geral", "Profissional e assinatura", "Pastas e fontes de dados", "Relatórios e revisão", "Módulos", "Inteligência artificial", "Modelos da análise", "Kit PMVNI", "Sistema" },
            CategoriasAjustes.Menu.Select(c => c.Titulo));
        // Modelos da análise é subpágina de Inteligência artificial e vem logo depois dela; nenhuma outra tem mãe.
        Assert.Equal(new[] { (CategoriasAjustes.Modelos, CategoriasAjustes.Ia) }, CategoriasAjustes.Menu.Where(c => c.Pai is not null).Select(c => (c.Tag, c.Pai!)));
        Assert.Equal(CategoriasAjustes.Ia, CategoriasAjustes.Menu[CategoriasAjustes.Menu.ToList().FindIndex(c => c.Tag == CategoriasAjustes.Modelos) - 1].Tag);
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
    public void linhas_de_acoes_preservam_o_texto_e_um_unico_grupo()
    {
        var pagina = XDocument.Load(Path.Combine(Ambiente.PastaTestes, "..", "src", "Pmvni.App", "Paginas", "PaginaAjustes.xaml"));
        XNamespace local = "using:Pmvni.App";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var linhas = pagina.Descendants(local + "LinhaComAcoes").ToList();
        Assert.NotEmpty(linhas);
        Assert.All(linhas, linha => Assert.Equal(2, linha.Elements().Count()));
        // A pasta de processos fica em Pastas e fontes de dados; a do kit, em Kit PMVNI: um cartão em cada, com os mesmos três botões.
        var cartoes = new[] { "CartoesDasPastas", "CartoesDoKit" }.Select(nome => Assert.Single(pagina.Descendants(local + "PainelCartoesAjustes"),
            painel => (string?)painel.Attribute(x + "Name") == nome)).ToList();
        Assert.All(cartoes, c => Assert.Single(c.Elements()));
        foreach (var cartao in cartoes.SelectMany(c => c.Elements()))
        {
            var linha = Assert.Single(cartao.Descendants(local + "LinhaComAcoes"));
            var grupo = linha.Elements().Last();
            Assert.Equal("8", (string?)grupo.Attribute("Spacing"));
            Assert.Equal(new[] { "Alterar…", "Abrir", "Padrão" },
                grupo.Elements().Select(botao => (string?)botao.Attribute("Content")));
        }
    }

    [Fact]
    public void etiqueta_desconhecida_leva_a_geral()
    {
        Assert.Equal(CategoriasAjustes.Modulos, CategoriasAjustes.De("modulos").Tag);
        // Etiquetas antigas (aparencia, fontes_dados, despachos, legislacao) não existem mais: levam a Geral.
        Assert.Equal(CategoriasAjustes.Geral, CategoriasAjustes.De("legislacao").Tag);
        Assert.Equal(CategoriasAjustes.Geral, CategoriasAjustes.De("nada").Tag);
        Assert.Equal(CategoriasAjustes.Geral, CategoriasAjustes.De(null).Tag);
    }
}
