using System.Text.Json;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Ponte;
using Xunit;

namespace Pmvni.Web.Testes;

public sealed class PlanoGoldenFixture : IAsyncLifetime
{
    public HostOculto Host = null!;
    public async Task InitializeAsync() => Host = await HostOculto.CriarAsync(1024, 1400);
    public async Task DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>Tarefa 9: o plano de quebras dos casos com escala de impressão k &gt; 1 (<c>longo</c>, <c>extremos</c>) fica fixado
/// em <c>tests/Pmvni.Web.Testes/golden/plano-&lt;caso&gt;.json</c> (inícios das folhas, inclusive <c>forcada</c>, e escala).
/// Uma mudança em folha.css ou no paginador que mude a composição falha aqui; se for intencional (e conferida pela V3 contra
/// o gerador do kit), atualize o golden no mesmo commit e explique a mudança.</summary>
public sealed class PlanoGoldenTestes(PlanoGoldenFixture f) : IClassFixture<PlanoGoldenFixture>
{
    static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    sealed record Golden(string Caso, int Folhas, double Escala, List<PosicaoFluxo> InicioDasFolhas);

    [Theory]
    [InlineData("longo")]
    [InlineData("extremos")]
    public async Task plano_igual_ao_golden(string caso)
    {
        var raiz = Ambiente.RaizRepositorio();
        var dados = Path.Combine(raiz, "tests", "corpus", caso + ".json");
        var golden = JsonSerializer.Deserialize<Golden>(
            File.ReadAllText(Path.Combine(raiz, "tests", "Pmvni.Web.Testes", "golden", "plano-" + caso + ".json")), Mensagens.Opcoes)!;
        Assert.True(golden.Escala > 1, "o golden deve ser de um caso com k > 1");

        var r = RelatorioRenderer.Renderizar(dados, new OpcoesRender(false, true), Modelo);
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        await f.Host.Documento.CarregarAsync(r, ConfigFolhas.PadraoDeDados(Modelo, dados));
        var c = await f.Host.Documento.ComporDetalhadoAsync();

        Assert.Equal(golden.Folhas, c.Folhas);
        Assert.Equal(golden.Escala, c.Plano.Escala, 9);
        Assert.Equal(golden.InicioDasFolhas.Count, c.Plano.InicioDasFolhas.Count);
        for (var i = 0; i < golden.InicioDasFolhas.Count; i++)
            Assert.True(golden.InicioDasFolhas[i].Equals(c.Plano.InicioDasFolhas[i]),
                $"{caso}: início da folha {i + 1} mudou: golden {JsonSerializer.Serialize(golden.InicioDasFolhas[i], Mensagens.Opcoes)}"
                + $", atual {JsonSerializer.Serialize(c.Plano.InicioDasFolhas[i], Mensagens.Opcoes)}");
        Assert.Empty(c.Plano.Estouros);
    }
}
