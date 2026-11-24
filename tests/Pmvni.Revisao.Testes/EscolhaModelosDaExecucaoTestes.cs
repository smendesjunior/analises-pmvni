using System.Text.Json.Nodes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class EscolhaModelosDaExecucaoTestes
{
    const string Pedido = """
        {"rota":"codex","perfil":"padrao","etapa":"reparo_julgamento","modelo_recusado":"gpt-a",
         "etapas":{"analyze":{"modelo":"gpt-b","nivel":"alto"},"reparo_julgamento":{"modelo":"gpt-a","nivel":"medio"}},
         "modelos_disponiveis":["gpt-b"],"fonte_modelos":"conta da sessão"}
        """;

    [Fact]
    public void RequerEscolhaDaCelulaRecusadaEEnviaSoAsMudancas()
    {
        var pedido = Assert.IsType<EscolhaModelosDaExecucao>(EscolhaModelosDaExecucao.Ler(Pedido));
        Assert.True(pedido.Oferta!.DaConta);
        Assert.Null(pedido.Resposta(pedido.Etapas, out _));
        var escolhas = new Dictionary<string, CelulaModelos>(pedido.Etapas) { [pedido.Etapa] = new("gpt-b", "medio") };
        var etapas = JsonNode.Parse(pedido.Resposta(escolhas, out _)!)!["etapas"]!.AsObject();
        Assert.Single(etapas);
        Assert.Equal("gpt-b", etapas["reparo_julgamento"]!["modelo"]!.GetValue<string>());
        Assert.Equal("gpt-a", pedido.Etapas[pedido.Etapa].Modelo);
    }

    [Fact]
    public void TrocarSomenteEsforcoDependeDoMotivoDaRecusa()
    {
        var pedido = EscolhaModelosDaExecucao.Ler(Pedido)!;
        var escolhas = new Dictionary<string, CelulaModelos>(pedido.Etapas) { [pedido.Etapa] = new("gpt-a", "alto") };
        Assert.Null(pedido.Resposta(escolhas, out _));
        Assert.NotNull((pedido with { RecusaEsforco = true }).Resposta(escolhas, out _));
    }

    [Fact]
    public void ListaGenericaDoBalanceadorPermaneceSugestao()
    {
        var dados = JsonNode.Parse(Pedido)!.AsObject();
        dados["catalogo"] = JsonNode.Parse("""{"modelos":["gpt-c"],"fonte":"balanceador","verificado":false}""");
        Assert.False(EscolhaModelosDaExecucao.Ler(dados.ToJsonString())!.Oferta!.DaConta);
    }
}
