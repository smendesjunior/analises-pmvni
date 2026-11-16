using System.Text;
using System.Text.Json.Nodes;
using Pmvni.Composicao.Figuras;
using Xunit;

namespace Pmvni.Composicao.Testes;

/// <summary>Figuras com marcações editáveis: medidas do kit, transformações de coordenadas, SVG composto e o render.</summary>
public class FiguraSvgTestes
{
    private static string Corpus(string nome) => Path.Combine(Path.GetDirectoryName(Referencias.Pasta)!, "corpus", nome);

    [Fact]
    public void medidas_seguem_as_formulas_do_annotate_v4()
    {
        // iw=800: size=max(18,20)=20; r=max(14,16)=16; gutter=round(32+36)=68; width=max(2,1)=2; head=max(12,13)=13
        var m = MedidasFigura.De(800, 600);
        Assert.Equal((20, 16, 68, 2, 5, 13), (m.Fonte, m.Raio, m.Margem, m.Traco, m.Halo, m.Ponta));
        Assert.Equal(800 + 2 * 68, m.LarguraTotal);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(.25, .75)]
    [InlineData(1, 1)]
    public void normalizado_e_pixel_vao_e_voltam(double x, double y)
    {
        var m = MedidasFigura.De(1234, 567);
        var p = m.ParaPixel(x, y);
        Assert.Equal(m.Margem + x * 1234, p.X, 6);
        var (nx, ny) = m.ParaNormalizado(p);
        Assert.Equal(x, nx, 9);
        Assert.Equal(y, ny, 9);
    }

    [Fact]
    public void seta_da_regiao_aponta_para_a_borda_voltada_ao_numero()
    {
        var m = MedidasFigura.De(800, 600);
        var f = GeometriaFigura.Forma(m, new MarcaDesenho(TiposMarcacao.Regiao, 1, null, [.5, .2, .7, .4], "esquerda", .3, null, null));
        Assert.Equal(m.Margem / 2.0, f.Numero!.Value.X, 6);
        Assert.Equal(m.Margem + .5 * 800, f.PontaSeta!.Value.X, 6); // borda esquerda da caixa
        Assert.Equal(.3 * 600, f.PontaSeta!.Value.Y, 6);
        var d = GeometriaFigura.Forma(m, new MarcaDesenho(TiposMarcacao.Regiao, 1, null, [.5, .2, .7, .4], "direita", .9, null, null));
        Assert.Equal(m.Margem + .7 * 800, d.PontaSeta!.Value.X, 6); // borda direita
        Assert.Equal(.4 * 600, d.PontaSeta!.Value.Y, 6);              // limitada à caixa
    }

    [Fact]
    public void numero_fica_dentro_da_altura_da_imagem()
    {
        var m = MedidasFigura.De(800, 600);
        Assert.Equal(m.Raio + 4, m.CentroNumero("esquerda", 0).Y, 6);
        Assert.Equal(600 - m.Raio - 4, m.CentroNumero("direita", 1).Y, 6);
    }

    [Fact]
    public void tamanho_do_png_lido_do_cabecalho()
    {
        var bytes = File.ReadAllBytes(Corpus(Path.Combine("img", "fig1.png")));
        Assert.NotNull(TamanhoImagem.Ler(bytes));
    }

    [Fact]
    public void svg_tem_imagem_base_caixa_hachurada_seta_e_numero()
    {
        var bytes = File.ReadAllBytes(Corpus(Path.Combine("img", "fig1.png")));
        var svg = SvgFigura.Compor(bytes, [
            new MarcaDesenho(TiposMarcacao.Regiao, 1, null, [.2, .2, .4, .4], "esquerda", .3, null, null),
            new MarcaDesenho(TiposMarcacao.Seta, 2, [.8, .6], null, "direita", .6, null, null),
            new MarcaDesenho(TiposMarcacao.Texto, null, null, null, "esquerda", .5, [.5, .9], "ver <cota> & nível"),
        ]);
        Assert.StartsWith("<svg ", svg);
        Assert.Contains("data:image/png;base64," + Convert.ToBase64String(bytes), svg); // a imagem do projeto sem alteração
        Assert.Contains("fill=\"url(#h)\"", svg);   // hachura
        Assert.Contains(">1</text>", svg);
        Assert.Contains(">2</text>", svg);
        Assert.Contains("ver &lt;cota&gt; &amp; nível", svg);
        Assert.Equal(2, CountOf(svg, "<polygon")/2); // 2 setas × (halo + vermelho)
    }

    private static int CountOf(string s, string t) => (s.Length - s.Replace(t, "", StringComparison.Ordinal).Length) / t.Length;

    [Fact]
    public void render_sem_marcacoes_e_o_do_kit_e_com_marcacoes_vira_svg()
    {
        var modelo = ModeloOficial.Carregar();
        var caminho = Corpus("longo.json");
        var bytes = File.ReadAllBytes(caminho);
        var r1 = RelatorioRenderer.Renderizar(bytes, caminho, new OpcoesRender(false, true), modelo);
        Assert.True(r1.Ok);


        var d = JsonNode.Parse(bytes)!.AsObject();
        d["figuras"]![0]!["marcacoes"] = JsonNode.Parse("""[{"tipo":"regiao","numero":3,"caixa":[0.1,0.1,0.3,0.3],"lado":"esquerda","altura":0.2}]""");
        var editado = Encoding.UTF8.GetBytes(d.ToJsonString());
        var r2 = RelatorioRenderer.Renderizar(editado, caminho, new OpcoesRender(false, true), modelo);
        Assert.True(r2.Ok);
        Assert.Equal(CountOf(r1.Html, "data:image/svg+xml;base64,") + 1, CountOf(r2.Html, "data:image/svg+xml;base64,"));
    }
}
