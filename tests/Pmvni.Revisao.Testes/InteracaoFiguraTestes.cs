using Pmvni.App;
using Pmvni.Composicao.Figuras;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Editor de figura sem tela: o que fica sob o mouse, o arrasto (pixels → normalizado) e o comando que ele gera; e os auxiliares do
/// recorte de prancha.</summary>
public class InteracaoFiguraTestes
{
    static readonly MedidasFigura M = MedidasFigura.De(800, 600);

    static readonly MarcaDesenho Regiao = new(TiposMarcacao.Regiao, 1, null, [.25, .25, .5, .5], "esquerda", .4, null, null, "mk_r");
    static readonly MarcaDesenho Seta = new(TiposMarcacao.Seta, 2, [.8, .7], null, "direita", .7, null, null, "mk_s");

    [Fact]
    public void acerta_numero_caixa_canto_e_ponta()
    {
        var marcas = new[] { Regiao, Seta };
        var n = M.CentroNumero("esquerda", .4);
        Assert.Equal(new AcertoFigura("mk_r", AlcaFigura.Numero), InteracaoFigura.Acertar(M, marcas, n, null, 10));
        var meio = M.ParaPixel(.4, .4);
        Assert.Equal(new AcertoFigura("mk_r", AlcaFigura.Caixa), InteracaoFigura.Acertar(M, marcas, meio, null, 10));
        // canto só com a marcação selecionada
        var canto = M.ParaPixel(.5, .5);
        Assert.Equal(AlcaFigura.CantoInferiorDireito, InteracaoFigura.Acertar(M, marcas, canto, "mk_r", 10)!.Alca);
        var ponta = M.ParaPixel(.8, .7);
        Assert.Equal(new AcertoFigura("mk_s", AlcaFigura.Ponta), InteracaoFigura.Acertar(M, marcas, ponta, "mk_s", 10));
        Assert.Null(InteracaoFigura.Acertar(M, marcas, M.ParaPixel(.05, .95), null, 10));
    }

    [Fact]
    public void arrastar_caixa_move_dentro_da_imagem_e_gera_mover()
    {
        var de = M.ParaPixel(.4, .4);
        var para = M.ParaPixel(.9, .4); // empurra para fora da direita: a caixa para na borda
        var nova = InteracaoFigura.Arrastar(M, Regiao, AlcaFigura.Caixa, de, para);
        Assert.Equal(1, nova.Caixa![2], 9);
        Assert.Equal(.75, nova.Caixa![0], 9);
        var c = Assert.IsType<MoverMarcacao>(InteracaoFigura.ComandoDoArrasto("fig", Regiao, nova, AlcaFigura.Caixa));
        Assert.Equal("mk_r", c.Marcacao);
    }

    [Fact]
    public void arrastar_canto_redimensiona_sem_inverter()
    {
        var nova = InteracaoFigura.Arrastar(M, Regiao, AlcaFigura.CantoInferiorDireito, M.ParaPixel(.5, .5), M.ParaPixel(.1, .1));
        Assert.True(nova.Caixa![2] > nova.Caixa[0]);
        Assert.True(nova.Caixa![3] > nova.Caixa[1]);
        Assert.IsType<RedimensionarMarcacao>(InteracaoFigura.ComandoDoArrasto("fig", Regiao, nova, AlcaFigura.CantoInferiorDireito));
    }

    [Fact]
    public void arrastar_numero_troca_de_lado_e_altura()
    {
        var para = new Ponto(M.LarguraTotal - 5, 300);
        var nova = InteracaoFigura.Arrastar(M, Regiao, AlcaFigura.Numero, M.CentroNumero("esquerda", .4), para);
        Assert.Equal("direita", nova.Lado);
        Assert.Equal(.5, nova.Altura, 9);
    }

    [Fact]
    public void sem_movimento_nao_gera_comando()
    {
        var p = M.ParaPixel(.4, .4);
        var igual = InteracaoFigura.Arrastar(M, Regiao, AlcaFigura.Caixa, p, p);
        Assert.Null(InteracaoFigura.ComandoDoArrasto("fig", Regiao, igual, AlcaFigura.Caixa));
    }

    [Fact]
    public void caixa_do_arrasto_fica_na_imagem_e_ordenada()
    {
        var c = InteracaoFigura.CaixaDoArrasto(M, new Ponto(M.LarguraTotal, 700), new Ponto(0, -10));
        Assert.Equal(new double[] { 0, 0, 1, 1 }, c);
    }

    [Theory]
    [InlineData(@"C:\p\PROJETO\ARQ 01-03.pdf", "ARQ 01/03")]
    [InlineData(@"C:\p\PROJETO\arq_2_5.pdf", "ARQ 02/05")]
    [InlineData(@"C:\p\PROJETO\Planta baixa.pdf", "Planta baixa")]
    public void prancha_sugerida_pelo_nome(string caminho, string esperado) =>
        Assert.Equal(esperado, RecortePrancha.SugerirPrancha(caminho));

    [Fact]
    public void pdfs_do_processo_ficticio_vem_da_pasta_projeto()
    {
        var pasta = Path.Combine(Ambiente.Corpus("processos"), "2099", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)");
        var pdfs = RecortePrancha.PdfsDoProcesso(Path.Combine(pasta, "Relatorio.pmvrel"));
        Assert.Equal("ARQ 01-03.pdf", Path.GetFileName(pdfs[0]));
        Assert.DoesNotContain(pdfs, p => p.Contains("DESPACHO", StringComparison.OrdinalIgnoreCase));
    }
}
