using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;
using Xunit;

namespace Pmvni.Web.Testes;

public sealed class TextosDoRelatorioWebTestes(TelaFixture f) : IClassFixture<TelaFixture>
{
    [Fact]
    public async Task EditaTextoEPaginaNaPreviaENoPdf()
    {
        var modelo = ModeloOficial.Carregar();
        var caminho = Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus", "curto.json");
        var usuario = ContextoUsuario.Atual();
        var pacote = Importador.Importar(File.ReadAllBytes(caminho), caminho, modelo, usuario);
        using var rec = new Recompositor(pacote, modelo);
        var inicial = rec.Preparar(pacote.Projeto);
        var titulo = Assert.Single(inicial.Render.Textos, t => t.Value.Original == "CONCLUSÃO").Key;
        var sessao = new SessaoRevisao(pacote.Projeto, pacote.Historico, usuario);
        Assert.NotNull(sessao.Aplicar(new DefinirTextoDoModelo(titulo, "Conclusão revisada no teste", true)));
        Assert.NotNull(sessao.Aplicar(new DefinirTextoDoModelo("rodape-direita", "PÁGINA {n} DE {t} · TESTE", true)));
        var preparado = rec.Preparar(sessao.Projeto);
        Assert.Empty(preparado.ErrosFinais);
        var comp = await rec.ComporAsync(preparado, f.Composicao);
        var previa = string.Join(" ", await f.Composicao.Documento.TextoPorFolhaAsync());
        Assert.Contains("Conclusão revisada no teste", previa, StringComparison.OrdinalIgnoreCase);
        var saida = Path.Combine(rec.PastaTrabalho, "revisado.pdf");
        var pdf = await Exportador.ExportarFinalAsync(preparado.Dados, preparado.CaminhoDados, preparado.Config, saida, comp.Plano, modelo);
        Assert.Equal(comp.Folhas, pdf.Folhas);
        Assert.True(new FileInfo(saida).Length > 1000);
        Assert.Equal(comp.Plano.ParaJson(), pdf.Plano.ParaJson());
    }
}
