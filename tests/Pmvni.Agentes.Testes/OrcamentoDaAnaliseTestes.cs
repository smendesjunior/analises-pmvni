using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

public class OrcamentoDaAnaliseTestes
{
    [Theory]
    [InlineData(null, "preparo-atual", 10, 2, true, false)]
    [InlineData(1, "preparo-atual", 10, 2, true, false)]
    [InlineData(2, "preparo-antigo", 10, 2, true, false)]
    [InlineData(2, "preparo-atual", 0, 0, false, false)]
    [InlineData(2, "preparo-atual", 8, 2, false, false)]
    [InlineData(2, "preparo-atual", -1, 2, true, false)]
    [InlineData(2, "preparo-atual", 8, -1, true, false)]
    [InlineData(2, "preparo-atual", 0, 2, false, true)]
    [InlineData(2, "preparo-atual", 0, 0, true, true)]
    [InlineData(2, "preparo-atual", 8.5, 2.25, true, true)]
    public void VinculaMedicaoAoPreparoEPreservaReserva(int? versao, string preparo, double medido, double reserva, bool temConta, bool valido)
    {
        using var pasta = Ambiente.PastaDeTeste("orcamento-analise");
        File.WriteAllText(Path.Combine(pasta, "engine_state.json"), """{"stages":{"prepare":{"certificado_id":"preparo-atual"}}}""");
        var recibo = new JsonObject
        {
            ["preparo"] = preparo, ["consumo_observado"] = medido, ["pontos_sem_atribuicao"] = reserva,
            ["contas_vinculadas"] = temConta ? new JsonArray("conta-teste") : new JsonArray()
        };
        if (versao.HasValue) recibo["versao_medicao"] = versao.Value;
        File.WriteAllText(Path.Combine(pasta, "orcamento_lb.json"), recibo.ToJsonString());
        var esperado = valido ? new OrcamentoDaAnalise(medido, reserva) : null;
        Assert.Equal(esperado, OrcamentoDaAnalise.Ler(pasta, default));
    }

    [Fact]
    public void DadosAusentesOuAnterioresAoRegistroFicamIndisponiveis()
    {
        using var pasta = Ambiente.PastaDeTeste("orcamento-indisponivel");
        Assert.Null(OrcamentoDaAnalise.Ler(pasta, default));
        File.WriteAllText(Path.Combine(pasta, "engine_state.json"), """{"stages":{"prepare":{"certificado_id":"atual"}}}""");
        var arquivo = Path.Combine(pasta, "orcamento_lb.json");
        File.WriteAllText(arquivo, """{"versao_medicao":2,"preparo":"atual","contas_vinculadas":["a"],"consumo_observado":4}""");
        Assert.Null(OrcamentoDaAnalise.Ler(pasta, default));
        File.WriteAllText(arquivo, """{"versao_medicao":2,"preparo":"atual","contas_vinculadas":["a"],"consumo_observado":4,"pontos_sem_atribuicao":1}""");
        File.SetLastWriteTimeUtc(arquivo, DateTime.UtcNow.AddHours(-1));
        Assert.Null(OrcamentoDaAnalise.Ler(pasta, DateTime.UtcNow.AddMinutes(-5)));
    }
}
