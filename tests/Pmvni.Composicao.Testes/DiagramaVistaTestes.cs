using Pmvni.Composicao.Kit;
using Xunit;

namespace Pmvni.Composicao.Testes;

/// <summary>Correção 2026-11-17: a coluna (lista) e o desenho de uma chamada seguem a "vista" explícita dela, não a âncora nem o número.
/// Caso do usuário: "Afastamento do muro" (7) pedido na volumetria, com âncora "lateral", ia para a planta.</summary>
public class DiagramaVistaTestes
{
    static Dictionary<string, object?> Chamada(string ancora, string titulo, string? vista = null)
    {
        var c = new Dictionary<string, object?> { ["ancora"] = ancora, ["titulo"] = titulo, ["situacao"] = "NAO_ATENDE", ["texto"] = "x" };
        if (vista is not null) c["vista"] = vista;
        return c;
    }

    static Dictionary<string, object?> Dados(params Dictionary<string, object?>[] chamadas) => new()
    {
        ["planta"] = new Dictionary<string, object?>
        {
            ["limites_confirmados"] = true, ["testada"] = "12 m", ["profundidade"] = "30 m", ["frente"] = "3 m", ["fundos"] = "5 m",
            ["permeavel"] = new Dictionary<string, object?> { ["titulo"] = "P", ["linha1"] = "a" },
            ["edificacao"] = new List<object?> { "E", "e" },
            ["lateral"] = new List<object?> { "1,50 m", "l" },
        },
        ["volumetria"] = new Dictionary<string, object?>
        {
            ["altura_esquerda"] = new List<object?> { "Corte AA", "7,97 m" },
            ["altura_direita"] = new List<object?> { "Corte BB", "7,86 m" },
        },
        ["chamadas"] = chamadas.Cast<object?>().ToList(),
    };

    static List<string> Titulos(Dictionary<string, object?> r, string chave) =>
        ((List<object?>)r[chave]!).Select(c => (string)((Dictionary<string, object?>)c!)["titulo"]!).ToList();

    static string Svg(Dictionary<string, object?> r, string chave) => ((Seguro)r[chave]!).Valor;

    static string Marca(int x, int y) => $"<circle cx=\"{x}\" cy=\"{y}\" r=\"8.5\"/><text x=\"{x}\"";

    [Fact]
    public void sem_vista_a_ancora_decide_como_no_kit()
    {
        var r = Diagrama.MontarDiagrama(Dados(Chamada("lote", "1"), Chamada("lateral", "4"), Chamada("telhado", "5")));
        Assert.Equal(["1", "4"], Titulos(r, "chamadas_planta"));
        Assert.Equal(["5"], Titulos(r, "chamadas_volumetria"));
    }

    [Fact]
    public void com_vista_volumetria_a_chamada_vai_para_a_volumetria_mesmo_com_ancora_da_planta()
    {
        var r = Diagrama.MontarDiagrama(Dados(
            Chamada("lote", "1"), Chamada("permeavel", "2"), Chamada("edificacao", "3"), Chamada("lateral", "4"),
            Chamada("telhado", "5"), Chamada("altura", "6"), Chamada("lateral", "Afastamento do muro", "volumetria")));
        Assert.Equal(["1", "2", "3", "4"], Titulos(r, "chamadas_planta"));
        Assert.Equal(["5", "6", "Afastamento do muro"], Titulos(r, "chamadas_volumetria"));
        var (x, y) = Diagrama.PosVolumeExtra["lateral_direita"];
        Assert.Contains(Marca(x, y), Svg(r, "volumetria_svg"), StringComparison.Ordinal);
        Assert.Contains(">7</text>", Svg(r, "volumetria_svg"), StringComparison.Ordinal);
        Assert.DoesNotContain(">7</text>", Svg(r, "planta_svg"), StringComparison.Ordinal);
    }

    [Fact]
    public void com_vista_planta_a_chamada_vai_para_a_planta()
    {
        var r = Diagrama.MontarDiagrama(Dados(Chamada("telhado", "5"), Chamada("", "Novo", "Planta do terreno")));
        Assert.Equal(["Novo"], Titulos(r, "chamadas_planta"));
        Assert.Contains(">2</text>", Svg(r, "planta_svg"), StringComparison.Ordinal);
    }

    [Fact]
    public void ancora_resolvida_dentro_da_vista()
    {
        Assert.Equal("altura", Diagrama.ResolverAncora("volumetria", "altura", []));
        Assert.Equal("lateral_direita", Diagrama.ResolverAncora("volumetria", "lateral", []));
        Assert.Equal("lateral_direita", Diagrama.ResolverAncora("volumetria", "", ["telhado", "altura"]));
        Assert.Equal("lateral_esquerda", Diagrama.ResolverAncora("volumetria", null, ["lateral_direita"]));
        Assert.Equal("lateral", Diagrama.ResolverAncora("planta", "lateral_direita", []));
        Assert.Equal("lote", Diagrama.ResolverAncora("planta", "xyz", ["lateral"]));
        Assert.Equal("volumetria", Diagrama.NormalizarVista("VOLUMETRIA DA EDIFICAÇÃO"));
        Assert.Equal("planta", Diagrama.NormalizarVista(" planta "));
        Assert.Null(Diagrama.NormalizarVista("fachada"));
        Assert.Null(Diagrama.NormalizarVista(null));
    }

    [Fact]
    public void duas_chamadas_no_mesmo_ponto_nao_se_cobrem()
    {
        var r = Diagrama.MontarDiagrama(Dados(Chamada("altura", "6"), Chamada("altura", "7", "volumetria")));
        var svg = Svg(r, "volumetria_svg");
        var (x, y) = Diagrama.PosVolume["altura"];
        Assert.Contains(Marca(x, y), svg, StringComparison.Ordinal);
        Assert.Contains(Marca(x + 20, y), svg, StringComparison.Ordinal);
    }
}
