using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Tarefa 12: nenhuma IA recebe pedido do editor. As diretrizes do analista e o tratamento dos dados vão no <c>diretrizes.md</c> do analise, e o
/// <c>pedido.md</c> das análises do Editor 2.2 (com a sessão controladora) continua legível.</summary>
public class PedidoAgenteTestes
{
    [Fact]
    public void diretrizes_do_analise_exatas()
    {
        Assert.Equal(PedidoAgente.TratamentoDoProcesso + "\n" + PedidoAgente.CabecalhoDiretrizes + "\nLinha 1\nLinha 2",
            PedidoAgente.Diretrizes("Linha 1\nLinha 2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  \r\n  ")]
    public void sem_texto_so_o_tratamento_dos_dados(string? texto) => Assert.Equal(PedidoAgente.TratamentoDoProcesso, PedidoAgente.Diretrizes(texto));

    [Fact]
    public void o_texto_vai_como_foi_digitado()
    {
        var texto = "  com espaços nas pontas  \r\ne \"aspas\" & <html> $analise ";
        Assert.EndsWith("\n" + PedidoAgente.CabecalhoDiretrizes + "\n" + texto, PedidoAgente.Diretrizes(texto));
    }

    [Fact]
    public void pedido_do_editor_2_2_ainda_e_lido()
    {
        using var t = Ambiente.PastaDeTeste("pedido-antigo");
        var processo = Ambiente.NovoProcesso(t);
        using var r = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, new DateTime(2026, 11, 15, 10, 0, 0));
        r.GravarPedido(string.Join("\n", PedidoAgente.PrimeiraLinha, "Pasta do processo: " + processo.Pasta, "Processo: 0001/2099",
            PedidoAgente.TratamentoDoProcesso, PedidoAgente.CabecalhoDiretrizes, "confira o muro"));
        Assert.Equal("confira o muro", r.PedidoOriginal());
        r.GravarPedido("já no formato novo");
        using var novo = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, new DateTime(2026, 11, 15, 10, 0, 1));
        novo.GravarPedido("confira a calçada");
        Assert.Equal("confira a calçada", novo.PedidoOriginal());
    }
}
