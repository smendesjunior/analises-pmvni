using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

public sealed class TextosDoRelatorioTestes
{
    [Fact]
    public void EdicaoEVisibilidadeSobrevivemAoPacoteEAoHistorico()
    {
        var modelo = ModeloOficial.Carregar();
        var original = Ambiente.Corpus("longo.json");
        var pacote = Importador.Importar(File.ReadAllBytes(original), original, modelo, Ambiente.Contexto());
        using var pasta = Ambiente.PastaDeTeste("textos-do-relatorio");
        pacote.ExtrairAssets(pasta);
        var caminho = Path.Combine(pasta, "dados.json");
        ResultadoRender Render(Projeto p) => RelatorioRenderer.Renderizar(
            DadosEfetivos.Gerar(p, apresentarContaNominal: true).Json, caminho, new(false, true, ImagensSoNaPasta: true), modelo);
        var sessao = new SessaoRevisao(pacote.Projeto, pacote.Historico, Ambiente.Contexto());
        var antes = Render(sessao.Projeto);
        Assert.True(antes.Ok, string.Join(" ", antes.Erros));
        var titulo = Assert.Single(antes.Textos, t => t.Value.Original == "DIAGRAMA DOS ÍNDICES").Key;
        var ambiente = TabelasRevisao.Id(TabelasRevisao.Ambientes, 1);
        Assert.True(TabelasRevisao.Editavel(sessao.Projeto, ambiente, "vaos/0/codigo"));
        var desenho = new DefinirTextoDoModelo(titulo, "Esquemas revisados & conferidos", true);
        Assert.NotNull(sessao.Aplicar(desenho));
        Assert.NotNull(sessao.Aplicar(new DefinirVisibilidadeDoCampo(ambiente, "vaos/0", false)));
        var alterado = Render(sessao.Projeto);
        Assert.True(alterado.Ok);
        Assert.Contains("Esquemas revisados &amp; conferidos", alterado.Html);
        Assert.Contains("data-vista-diagrama=\"planta\"", alterado.Html);
        Assert.Contains("data-vista-diagrama=\"volumetria\"", alterado.Html);
        Assert.True(antes.Textos[titulo].Visivel);
        Assert.False(TextosRevisados.CampoVisivel(sessao.Projeto, ambiente, "vaos/0"));
        Assert.NotNull(sessao.Desfazer());
        Assert.True(TextosRevisados.CampoVisivel(sessao.Projeto, ambiente, "vaos/0"));
        Assert.NotNull(sessao.Refazer());
        Assert.False(TextosRevisados.CampoVisivel(sessao.Projeto, ambiente, "vaos/0"));
        var saida = Path.Combine(pasta, "revisado.rpmvni");
        Pacote.Gravar(saida, pacote with { Projeto = sessao.Projeto, Historico = sessao.HistoricoCompleto }, null);
        var reaberto = Pacote.Ler(saida);
        Assert.False(reaberto.SomenteLeitura);
        Assert.False(TextosRevisados.CampoVisivel(reaberto.Conteudo.Projeto, ambiente, "vaos/0"));
        Assert.Contains("Esquemas revisados &amp; conferidos", Render(reaberto.Conteudo.Projeto).Html);
        Assert.NotNull(TabelasRevisao.TextoOriginal(reaberto.Conteudo.Projeto, ambiente, "vaos/0/codigo"));
    }
}
