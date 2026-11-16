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
    public void concluir_regiao_preserva_o_comando_apos_perder_a_captura_e_nao_duplica()
    {
        var gesto = new GestoFigura();
        gesto.IniciarRegiao(M, M.ParaPixel(.2, .3));
        var comando = Assert.IsType<IncluirMarcacao>(gesto.Concluir("fig", M.ParaPixel(.6, .7)));
        gesto.Cancelar(); // PointerCaptureLost emitido pela liberação programática do mouse.
        Assert.False(gesto.Ativo);
        Assert.Equal(new[] { .2, .3, .6, .7 }, comando.Caixa!, new Aproximado());
        Assert.Null(gesto.Concluir("fig", M.ParaPixel(.6, .7)));
    }

    [Fact]
    public void cancelar_ou_clicar_sem_arrastar_nao_inclui_regiao()
    {
        var gesto = new GestoFigura();
        var ponto = M.ParaPixel(.2, .3);
        gesto.IniciarRegiao(M, ponto);
        gesto.Cancelar();
        Assert.Null(gesto.Concluir("fig", M.ParaPixel(.6, .7)));
        gesto.IniciarRegiao(M, ponto);
        Assert.Null(gesto.Concluir("fig", ponto));
    }

    [Fact]
    public void mover_e_redimensionar_concluem_com_a_posicao_da_soltura()
    {
        var gesto = new GestoFigura();
        gesto.IniciarArrasto(M, Regiao, AlcaFigura.Caixa, M.ParaPixel(.3, .3));
        gesto.Mover(M.ParaPixel(.4, .4));
        Assert.NotNull(gesto.Atual);
        var comando = Assert.IsType<MoverMarcacao>(gesto.Concluir("fig", M.ParaPixel(.5, .5)));
        gesto.Cancelar();
        Assert.Equal(.45, comando.Caixa![0], 8);
        Assert.Equal(.45, comando.Caixa![1], 8);
        gesto.IniciarArrasto(M, Regiao, AlcaFigura.CantoInferiorDireito, M.ParaPixel(.5, .5));
        Assert.IsType<RedimensionarMarcacao>(gesto.Concluir("fig", M.ParaPixel(.7, .8)));
    }

    sealed class Aproximado : IEqualityComparer<double>
    {
        public bool Equals(double x, double y) => Math.Abs(x - y) < 1e-8;
        public int GetHashCode(double obj) => 0;
    }

    [Fact]
    public void recorte_converte_os_dois_eixos_e_arredonda_so_no_png()
    {
        // Página de tamanho fracionário: a prévia é arredondada em pixels em cada eixo.
        var r = RecortePdf.DeTela(100, 150, 500, 450, 1000, 600, 500.25, 300.4);
        Assert.Equal(50.025, r.X, 8);
        Assert.Equal(75.1, r.Y, 8);
        Assert.Equal(200.1, r.Largura, 8);
        Assert.Equal(150.2, r.Altura, 8);
        Assert.Equal(417u, r.PixelsLargura);
        Assert.Equal(313u, r.PixelsAltura);
    }

    [Fact]
    public void recorte_invertido_e_fora_da_pagina_e_limitado_a_pagina()
    {
        var r = RecortePdf.DeTela(1200, 700, -10, -20, 1000, 600, 500, 300);
        Assert.Equal(0, r.X);
        Assert.Equal(0, r.Y);
        Assert.Equal(500, r.Largura);
        Assert.Equal(300, r.Altura);
    }

    [Fact]
    public void recorte_vazio_invalido_ou_grande_demais_e_recusado()
    {
        Assert.Throws<ArgumentException>(() => RecortePdf.DeTela(10, 10, 10, 20, 100, 100, 100, 100));
        Assert.Throws<ArgumentException>(() => RecortePdf.DeTela(0, 0, 20, 20, double.NaN, 100, 100, 100));
        Assert.Throws<ArgumentException>(() => RecortePdf.DeTela(0, 0, 100, 100, 100, 100, 20000, 20000));
    }

    [Fact]
    public void procura_projeto_quando_relatorio_esta_numa_subpasta_e_ordena_revisoes()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "pmvni-figuras-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var sub in new[] { "PROJETO/INICIAL", "PROJETO/REVISÃO 02", "DOCUMENTOS", "RELATORIOS/2026", ".pmvni-ai/analise", "DESPACHO" })
                Directory.CreateDirectory(Path.Combine(pasta, sub));
            var inicial = Path.Combine(pasta, "PROJETO/INICIAL/ARQ 01-03.pdf");
            var revisao = Path.Combine(pasta, "PROJETO/REVISÃO 02/ARQ 01-03.pdf");
            foreach (var pdf in new[] { inicial, revisao, Path.Combine(pasta, "DOCUMENTOS/doc.pdf"), Path.Combine(pasta, "DESPACHO/saida.pdf"), Path.Combine(pasta, ".pmvni-ai/analise/relatorio.pdf") })
                File.WriteAllBytes(pdf, []);
            var relatorio = Path.Combine(pasta, "RELATORIOS/2026/Relatorio.rpmvni");
            Assert.Equal(pasta, RecortePrancha.PastaDoProcesso(relatorio));
            Assert.Equal(pasta, RecortePrancha.PastaDoProcesso(Path.Combine(pasta, ".pmvni-ai/analise/Relatorio.rpmvni")));
            var pdfs = RecortePrancha.PdfsDoProcesso(relatorio);
            Assert.Equal(3, pdfs.Count);
            Assert.Equal(Path.GetFullPath(revisao), pdfs[0]);
            Assert.Equal(Path.GetFullPath(inicial), pdfs[1]);
        }
        finally { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); }
    }

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
    public void pdfs_do_processo_revisao_mais_recente_primeiro_e_subpastas_fundas()
    {
        var pasta = Path.Combine(Path.GetTempPath(), "pmvni-pdfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var r in new[] { @"PROJETO\INICIAL\ARQ\A.pdf", @"PROJETO\REVISÃO 01\B.pdf", @"PROJETO\REVISÃO 02\X\Y\C.pdf", @"DOCUMENTOS\D.pdf", @"DESPACHO 01\E.pdf", @".pmvni-ai\F.pdf" })
            {
                var c = Path.Combine(pasta, r);
                Directory.CreateDirectory(Path.GetDirectoryName(c)!);
                File.WriteAllText(c, "");
            }
            var nomes = RecortePrancha.PdfsDoProcesso(Path.Combine(pasta, "R.rpmvni")).Select(Path.GetFileName).ToArray();
            Assert.Equal(new[] { "C.pdf", "B.pdf", "A.pdf", "D.pdf" }, nomes);
        }
        finally { Directory.Delete(pasta, true); }
    }

    [Fact]
    public void pdfs_do_processo_ficticio_vem_da_pasta_projeto()
    {
        var pasta = Path.Combine(Ambiente.Corpus("processos"), "2099", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)");
        var pdfs = RecortePrancha.PdfsDoProcesso(Path.Combine(pasta, "Relatorio.rpmvni"));
        Assert.Equal("ARQ 01-03.pdf", Path.GetFileName(pdfs[0]));
        Assert.DoesNotContain(pdfs, p => p.Contains("DESPACHO", StringComparison.OrdinalIgnoreCase));
    }
}
