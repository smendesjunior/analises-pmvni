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

    [Fact]
    public void Encontra_mesmo_pedido_e_duracoes_na_estacao_ou_no_checkpoint_sem_duplicar_o_legado()
    {
        using var raiz = Ambiente.PastaDeTeste("consulta-retomada");
        using var estacao = new PastaTeste(Directory.CreateTempSubdirectory("pmvni-consulta-teste-").FullName);
        var processo = Ambiente.NovoProcesso(raiz);
        using var registro = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, DateTime.Now);
        var id = Path.GetFileName(registro.Pasta);
        const string pedido = "Analisar a revisão";
        registro.GravarPedido(pedido);
        var inicio = DateTimeOffset.UtcNow.AddMinutes(-5);
        registro.Atualizar(o =>
        {
            o["area_local_solicitada"] = true;
            o["provedor"] = "chatgpt";
            o["situacao"] = "interrompida";
            o["inicio"] = inicio.ToString("o");
            o[FluxoLinear.Chave] = FluxoLinear.Assinatura;
        });
        var arquivo = Path.Combine(processo.Pasta, "planta.pdf");
        File.WriteAllText(arquivo, "versao A");
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(arquivo)));
        var pe = new PedidoExecucao(Provedor.ChatGpt, processo, Ambiente.PecasFalsas(raiz), ModoAcesso.Conta, raiz, pedido, [], registro);
        var area = AreaAnaliseLocal.Preparar(pe, [arquivo], default, estacao);
        registro.Atualizar(o => { o["area_local_versao"] = 1; o["area_local_id"] = area.Identificador; });
        var etapas = new { analyze = new { status = "EXECUTED", started = inicio.AddMinutes(1).ToUnixTimeSeconds(), ended = inicio.AddMinutes(3).ToUnixTimeSeconds() } };
        var estado = JsonSerializer.Serialize(new { editor_execution = id, stages = etapas });
        var controle = ResultadoAnalise.PastaControle(Provedor.ChatGpt);
        foreach (var caso in new[] { area.ProcessoLocal, processo.Pasta })
        {
            var pasta = Directory.CreateDirectory(Path.Combine(caso, controle)).FullName;
            File.WriteAllText(Path.Combine(pasta, "engine_state.json"), caso == processo.Pasta ? estado.Replace("EXECUTED", "PENDING") : estado);
        }
        File.WriteAllText(Path.Combine(area.ProcessoLocal, controle, UsoDasIas.Arquivo), JsonSerializer.Serialize(new
        {
            execucoes = new[] { new { etapa = "prepare", situacao = "EXECUTED", duracao_s = 12 } }
        }));
        var continuidade = Path.Combine(area.RegistroLocal, "continuidade.json");
        string Manifesto(string execucao, string entrada) => JsonSerializer.Serialize(new
        {
            execution = execucao, @case = area.ProcessoLocal, stages = etapas, inputs = new[] { new[] { entrada, hash } }
        });
        var peca = Path.Combine(area.ProcessoLocal, "planta.pdf");
        File.WriteAllText(continuidade, Manifesto(id, peca));
        RecuperacaoAnaliseLocal.Salvar(processo.Pasta, area.ProcessoLocal, id, controle);
        var outra = Path.Combine(estacao, "estacao-vazia");
        foreach (var docs in new[] { estacao.Caminho, outra })
        {
            using var encontrada = RegistroExecucao.BuscarInterrompida(processo, pedido, [], Provedor.ChatGpt, docs);
            Assert.Equal(registro.Pasta, encontrada?.Pasta);
            Assert.Null(RegistroExecucao.BuscarInterrompida(processo, "pedido diferente", [], Provedor.ChatGpt, docs));
            var historico = UsoDasIas.Historico(raiz, null, true, docs);
            Assert.Equal(2, historico.Count);
            var duracao = Assert.Single(historico, x => x.Etapa == "analyze");
            Assert.Equal(TimeSpan.FromMinutes(2), duracao.Duracao);
            Assert.Equal(Provedor.ChatGpt, duracao.Provedor);
            Assert.Equal(TimeSpan.FromSeconds(12), Assert.Single(historico, x => x.Etapa == "prepare").Duracao);
        }
        Assert.False(Directory.Exists(outra));
        File.WriteAllText(continuidade, Manifesto("outra-execucao", peca));
        Assert.Null(RegistroExecucao.BuscarInterrompida(processo, pedido, [], null, estacao));
        File.WriteAllText(continuidade, Manifesto(id, arquivo));
        Assert.Null(RegistroExecucao.BuscarInterrompida(processo, pedido, [], null, estacao));
        File.WriteAllText(arquivo, "versao B");
        Assert.Null(RegistroExecucao.BuscarInterrompida(processo, pedido, [], null, outra));
        registro.Atualizar(o => o["area_local_id"] = Guid.NewGuid().ToString("N"));
        Assert.Throws<IOException>(() => RegistroExecucao.BuscarInterrompida(processo, pedido, [], null, outra));
    }
}
