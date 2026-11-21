using System.Xml.Linq;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Menu dos Ajustes por categorias (auditoria §3.2): 8 categorias na ordem do desenho, "Sobre e suporte" no rodapé.</summary>
public class CategoriasAjustesTestes
{
    [Fact]
    public void categorias_na_ordem_do_desenho()
    {
        Assert.Equal(new[] { "Geral", "Aparência", "Relatórios e revisão", "Pastas e kit", "Fontes de dados", "Despachos e PDF", "Legislação", "Inteligência artificial", "Modelos da análise", "Sobre e suporte" },
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
    public void linhas_de_acoes_preservam_o_texto_e_um_unico_grupo()
    {
        var pagina = XDocument.Load(Path.Combine(Ambiente.PastaTestes, "..", "src", "Pmvni.App", "Paginas", "PaginaAjustes.xaml"));
        XNamespace local = "using:Pmvni.App";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var linhas = pagina.Descendants(local + "LinhaComAcoes").ToList();
        Assert.NotEmpty(linhas);
        Assert.All(linhas, linha => Assert.Equal(2, linha.Elements().Count()));
        var pastas = Assert.Single(pagina.Descendants(local + "PainelCartoesAjustes"),
            painel => (string?)painel.Attribute(x + "Name") == "CartoesDasPastas");
        Assert.Equal(2, pastas.Elements().Count());
        foreach (var cartao in pastas.Elements())
        {
            var linha = Assert.Single(cartao.Descendants(local + "LinhaComAcoes"));
            var grupo = linha.Elements().Last();
            Assert.Equal("8", (string?)grupo.Attribute("Spacing"));
            Assert.Equal(new[] { "Abrir", "Alterar…", "Padrão" },
                grupo.Elements().Select(botao => (string?)botao.Attribute("Content")));
        }
    }

    [Fact]
    public void etiqueta_desconhecida_leva_a_geral()
    {
        Assert.Equal(CategoriasAjustes.Legislacao, CategoriasAjustes.De("legislacao").Tag);
        Assert.Equal(CategoriasAjustes.Geral, CategoriasAjustes.De("nada").Tag);
        Assert.Equal(CategoriasAjustes.Geral, CategoriasAjustes.De(null).Tag);
    }
}
