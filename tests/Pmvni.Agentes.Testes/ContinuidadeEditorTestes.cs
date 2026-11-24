using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Pmvni.Agentes.Testes;

public class ContinuidadeEditorTestes
{
    [Fact]
    public void RegistroPreservaArquivoQuandoDadosSaoIguais()
    {
        using var temporario = Ambiente.PastaDeTeste("registro-identico");
        var processo = Ambiente.NovoProcesso(temporario);
        using var registro = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, DateTime.Now);
        registro.Atualizar(dados => dados["etapa"] = "analyze");
        var arquivo = Path.Combine(registro.Pasta, "EXECUCAO.JSON");
        File.SetLastWriteTimeUtc(arquivo, DateTime.UtcNow.AddDays(-1));
        var anterior = File.GetLastWriteTimeUtc(arquivo);

        registro.Atualizar(dados => dados["etapa"] = "analyze");
        Assert.Equal(anterior, File.GetLastWriteTimeUtc(arquivo));
        registro.Atualizar(dados => dados["etapa"] = "technical_audit");
        Assert.NotEqual(anterior, File.GetLastWriteTimeUtc(arquivo));
        using var atual = JsonDocument.Parse(File.ReadAllBytes(arquivo));
        Assert.Equal("technical_audit", atual.RootElement.GetProperty("etapa").GetString());
    }

    [Fact]
    public void ReaproveitaRegistroCompativelERecusaMudancasOuDescarte()
    {
        using var temporario = Ambiente.PastaDeTeste("continuidade-editor");
        var processo = Ambiente.NovoProcesso(temporario);
        using var anterior = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, DateTime.Now);
        const string pedido = "Rever a prancha";
        // O pedido.md do Editor 2.2 (com a sessão controladora): o texto digitado vem depois do cabeçalho das diretrizes.
        anterior.GravarPedido(string.Join("\n", PedidoAgente.PrimeiraLinha, "Pasta do processo: " + processo.Pasta, PedidoAgente.TratamentoDoProcesso,
            PedidoAgente.CabecalhoDiretrizes, pedido));
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
