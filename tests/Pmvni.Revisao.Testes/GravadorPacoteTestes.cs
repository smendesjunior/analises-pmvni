using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Gravações do mesmo pacote em fila (Tarefa 8, correção): duas gravações seguidas nunca dão conflito consigo mesmas.</summary>
public class GravadorPacoteTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static (string Caminho, GravadorPacote Gravador, ConteudoPacote Conteudo) Novo(PastaTeste pasta)
    {
        var json = Ambiente.Corpus("curto.json");
        var c = Importador.Importar(File.ReadAllBytes(json), json, Modelo, Ambiente.Contexto());
        var caminho = Path.Combine(pasta, "a.pmvrel");
        var disco = Pacote.Gravar(caminho, c, null);
        return (caminho, new GravadorPacote(caminho, disco), c);
    }

    [Fact]
    public async Task gravacoes_simultaneas_nao_conflitam()
    {
        using var pasta = Ambiente.PastaDeTeste("gravador");
        var (caminho, g, c) = Novo(pasta);
        var sessao = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        sessao.Aplicar(new Excluir(c.Projeto.Itens[1].Id));
        ConteudoPacote Atual() => c with { Projeto = sessao.Projeto.Clonar(), Historico = sessao.HistoricoCompleto.ToList() };

        var tarefas = Enumerable.Range(0, 4).Select(_ => g.GravarAsync(Atual)).ToArray();
        await Task.WhenAll(tarefas);

        var lido = Pacote.Ler(caminho);
        Assert.Equal(lido.Disco.Sha256, g.Disco.Sha256);
        Assert.False(lido.Conteudo.Projeto.Itens[1].Incluido);
        Assert.Equal(2, lido.Conteudo.Historico.Count);
    }

    [Fact]
    public async Task gravar_como_e_gravar_simultaneos_seguem_o_arquivo_novo()
    {
        using var pasta = Ambiente.PastaDeTeste("gravador-como");
        var (caminho, g, c) = Novo(pasta);
        var novo = Path.Combine(pasta, "b.pmvrel");

        await Task.WhenAll(g.GravarComoAsync(novo, () => c), g.GravarAsync(() => c));

        Assert.Equal(novo, g.Caminho);
        Assert.Equal(Pacote.Ler(novo).Disco.Sha256, g.Disco.Sha256);
        Assert.True(File.Exists(caminho));
    }

    [Fact]
    public async Task gravar_como_nao_sobrescreve()
    {
        using var pasta = Ambiente.PastaDeTeste("gravador-existe");
        var (_, g, c) = Novo(pasta);
        var outro = Path.Combine(pasta, "outro.pmvrel");
        File.WriteAllText(outro, "x");

        await Assert.ThrowsAsync<IOException>(() => g.GravarComoAsync(outro, () => c));
        Assert.Equal("x", File.ReadAllText(outro));
    }

    [Fact]
    public async Task mudanca_de_outro_programa_ainda_e_conflito()
    {
        using var pasta = Ambiente.PastaDeTeste("gravador-conflito");
        var (caminho, g, c) = Novo(pasta);
        var lido = Pacote.Ler(caminho);
        lido.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO";
        Pacote.Gravar(caminho, lido.Conteudo, lido.Disco);

        await Assert.ThrowsAsync<ConflitoNoDiscoException>(() => g.GravarAsync(() => c));
    }
}
