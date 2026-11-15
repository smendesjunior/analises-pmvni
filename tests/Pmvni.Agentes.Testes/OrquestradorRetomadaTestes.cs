using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Tarefa 20 (desenho §12.7): o editor reaberto volta a acompanhar uma análise do Claude que seguiu sozinha, sem dar outra partida.</summary>
public partial class OrquestradorTestes
{
    [Fact]
    public async Task claude_retomado_so_acompanha_sem_nova_partida()
    {
        using var c = new Cena("orq-claude-retomar", "claude_ok", Provedor.Claude, j => j["auto"]!["etapa_ms"] = 1500);
        var orq = c.Orq();
        using (var cts = new CancellationTokenSource())
        {
            var seguindo = orq.ExecutarAsync(c.Pedido(Provedor.Claude, texto: "pedido"), c.Progresso, cts.Token);
            var status = Path.Combine(c.Controle("analise-pro"), "auto-status.json");
            Assert.True(Ambiente.EsperarAte(() =>
            {
                try { return SituacaoAnalisePro.Ler(File.ReadAllText(status)).Codigo == "EM_ANDAMENTO"; }
                catch (IOException) { return false; }
            }, TimeSpan.FromSeconds(60)), "a análise do kit não passou a EM_ANDAMENTO" + Diagnostico(c));
            cts.Cancel();     // o editor "fechou": parar de acompanhar não encerra o kit
            Assert.Equal("cancelada", (await Ambiente.Com(seguindo, TimeSpan.FromSeconds(15))).Situacao);
        }

        // A situação, só lida (um "situacao" e nenhum "iniciar" novo).
        var s = await Ambiente.Com(orq.SituacaoClaudeAsync(c.Processo.Pasta, c.Pecas), TimeSpan.FromSeconds(30));
        Assert.NotNull(s);
        Assert.Contains(s!.Codigo, new[] { "EM_ANDAMENTO", "CONCLUIDO" });

        // O registro da execução reaberto guarda o que já tinha (provedor, início, pedido) e recebe o fim.
        var inicio = DateTimeOffset.Parse(c.Execucao()["inicio"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        using var registro = RegistroExecucao.Abrir(c.Registro.Pasta);
        Assert.Equal(c.Registro.Pasta, registro.Pasta);
        var progresso = new Coletor();
        var pe = new PedidoExecucao(Provedor.Claude, c.Processo, c.Pecas, ModoAcesso.Conta, c.RaizDados, null, Array.Empty<Pmvni.Revisao.ArquivoArquivado>(), registro);
        var fim = await Ambiente.Com(orq.AcompanharClaudeAsync(pe, inicio, progresso, CancellationToken.None), TimeSpan.FromSeconds(60));
        Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem + Diagnostico(c));
        Assert.EndsWith("relatorio_dados.json", fim.Documento);
        Assert.Contains(progresso.Todos(), p => p.Situacao?.Codigo == "CONCLUIDO");

        var ex = c.Execucao();
        Assert.Equal("claude", ex["provedor"]!.GetValue<string>());
        Assert.Equal("conta", ex["modo"]!.GetValue<string>());
        Assert.Equal(inicio, DateTimeOffset.Parse(ex["inicio"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("concluida", ex["situacao"]!.GetValue<string>());
        Assert.Equal("pedido", File.ReadAllText(c.DoRegistro("pedido.md"), Encoding.UTF8));
        Assert.Single(File.ReadAllLines(c.Falso("chamadas.txt"), Encoding.UTF8), l => l.StartsWith("iniciar\t", StringComparison.Ordinal));

        // Sem peças, a situação não é lida (null) e nada é iniciado.
        Assert.Null(await orq.SituacaoClaudeAsync(c.Processo.Pasta, c.Pecas with { Python = null }));
    }

    [Fact]
    public void registro_aberto_de_pasta_que_nao_existe_e_recusado()
    {
        using var t = Ambiente.PastaDeTeste("registro-abrir");
        Assert.Throws<DirectoryNotFoundException>(() => RegistroExecucao.Abrir(Path.Combine(t, "nao-existe")));
        // execucao.json ilegível: começa vazio (nada é perdido do que é gravado depois).
        var pasta = Path.Combine(t, "r");
        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, "execucao.json"), "{ quebrado");
        using var r = RegistroExecucao.Abrir(pasta);
        r.Atualizar(o => o["situacao"] = "concluida");
        Assert.Equal("concluida", JsonNode.Parse(File.ReadAllText(Path.Combine(pasta, "execucao.json")))!["situacao"]!.GetValue<string>());
    }
}
