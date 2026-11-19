using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Pmvni.Agentes.Testes;

public class ContinuidadeEditorTestes
{
    [Fact]
    public void ReaproveitaRegistroCompativelERecusaMudancasOuDescarte()
    {
        using var temporario = Ambiente.PastaDeTeste("continuidade-editor");
        var processo = Ambiente.NovoProcesso(temporario);
        using var anterior = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, DateTime.Now);
        const string pedido = "Rever a prancha";
        anterior.GravarPedido(PedidoAgente.Codex(processo.Pasta, processo, [], pedido));
        anterior.Atualizar(o => o["situacao"] = "interrompida");
        var original = Path.Combine(processo.Pasta, "prancha.pdf");
        File.WriteAllText(original, "primeira versão");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(original)));
        File.WriteAllText(Path.Combine(anterior.Pasta, "continuidade.json"), JsonSerializer.Serialize(new
        {
            execution = Path.GetFileName(anterior.Pasta), stages = new { prepare = new { status = "EXECUTED" } },
            inputs = new[] { new[] { original, hash } },
        }));
        using var retomada = RegistroExecucao.BuscarInterrompida(processo, pedido, []);
        Assert.Equal(anterior.Pasta, retomada?.Pasta);
        Assert.Null(RegistroExecucao.BuscarInterrompida(processo, "Outro pedido", []));
        File.AppendAllText(original, " alterada");
        Assert.Null(RegistroExecucao.BuscarInterrompida(processo, pedido, []));
        File.WriteAllText(original, "primeira versão");
        anterior.Atualizar(o => o["situacao"] = "descartada");
        Assert.Null(RegistroExecucao.BuscarInterrompida(processo, pedido, []));
    }
}
