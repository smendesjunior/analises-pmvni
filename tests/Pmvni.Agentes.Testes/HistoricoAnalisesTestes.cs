using System.Text.Json;
using Xunit;

namespace Pmvni.Agentes.Testes;

[Collection("Ambiente")]
public sealed class HistoricoAnalisesTestes
{
    [Fact]
    public void HistoricoRecuperaExecucoesComESemRelatorioNoMesmoProcesso()
    {
        using var pasta = Ambiente.PastaDeTeste("historico-registros");
        var processo = Ambiente.NovoProcesso(pasta);
        var pacote = Path.Combine(processo.Pasta, "Relatorio.rpmvni");
        File.WriteAllText(pacote, "fictício");
        var estados = new[] { "concluida", "falhou", "sem_resultado", "cancelada", "interrompida", "em_andamento" };
        foreach (var estado in estados)
        {
            var registro = Directory.CreateDirectory(Path.Combine(processo.Pasta, ".pmvni-ai", "editor", estado));
            File.WriteAllText(Path.Combine(registro.FullName, "execucao.json"), JsonSerializer.Serialize(new
            {
                situacao = estado, provedor = "chatgpt", sessao_codex = "sessao-ficticia",
                pmvrel = estado == "concluida" ? pacote : null
            }));
        }
        var itens = AnaliseHistorica.Listar(Path.Combine(pasta, "processos"));
        Assert.Equal(estados.Order(), itens.Select(h => h.Situacao).Order());
        Assert.Equal(pacote, itens.Single(h => h.Situacao == "concluida").Pacote);
        Assert.All(itens, h => Assert.Equal("sessao-ficticia", h.Sessao));
    }
}
