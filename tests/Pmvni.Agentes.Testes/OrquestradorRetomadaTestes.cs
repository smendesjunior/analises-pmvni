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
            // Uma execução legítima mantém seu próprio grupo. O segundo monitor representa um registro de versão anterior.
            var dona = orq.ExecutarAsync(c.Pedido(Provedor.Claude, texto: "pedido"), c.Progresso, CancellationToken.None);
            var status = Path.Combine(c.Controle("analise-pro"), "auto-status.json");
            Assert.True(Ambiente.EsperarAte(() =>
            {
                try { return SituacaoAnalisePro.Ler(File.ReadAllText(status)).Codigo == "EM_ANDAMENTO"; }
                catch (IOException) { return false; }
            }, TimeSpan.FromSeconds(60)), "a análise do kit não passou a EM_ANDAMENTO" + Diagnostico(c));
            var inicioAnterior = DateTimeOffset.Parse(c.Execucao()["inicio"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
            var monitor = orq.AcompanharClaudeAsync(c.Pedido(Provedor.Claude), inicioAnterior, new Coletor(), cts.Token);
            cts.Cancel();
            Assert.Equal("acompanhamento_interrompido", (await Ambiente.Com(monitor, TimeSpan.FromSeconds(15))).Situacao);
            Assert.Equal("concluida", (await Ambiente.Com(dona, Limite)).Situacao);
        }

        // A situação, só lida (um "situacao" e nenhum "iniciar" novo).
        var s = await Ambiente.Com(orq.SituacaoClaudeAsync(c.Processo.Pasta, c.Pecas), TimeSpan.FromSeconds(30));
        Assert.NotNull(s);
        Assert.Contains(s!.Codigo, new[] { "EM_ANDAMENTO", "CONCLUIDO" });

        // O registro da execução reaberto guarda o que já tinha (provedor, início, pedido) e recebe o fim.
        var inicio = DateTimeOffset.Parse(c.Execucao()["inicio"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture);
        using var registro = RegistroExecucao.Abrir(c.Registro.Pasta, c.Processo.Pasta);
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
    public void registro_reaberto_so_dentro_do_editor_do_processo()
    {
        using var t = Ambiente.PastaDeTeste("registro-abrir");
        var processo = Ambiente.NovoProcesso(t).Pasta;
        var editor = Path.Combine(processo, ".pmvni-ai", "editor");
        var bom = Path.Combine(editor, "2099-01-01_100000-claude");
        Assert.Throws<DirectoryNotFoundException>(() => RegistroExecucao.Abrir(bom, processo));
        Directory.CreateDirectory(bom);
        Assert.Null(RegistroExecucao.MotivoInvalido(processo, bom));
        Assert.Null(RegistroExecucao.MotivoInvalido(processo, bom + Path.DirectorySeparatorChar));

        // Caminhos vindos de um ajustes.json editado à mão: fora do editor, mais fundo, com "..", o próprio editor, outro processo, vazio.
        var fora = Path.Combine(t, "fora");
        Directory.CreateDirectory(fora);
        var fundo = Path.Combine(bom, "sub");
        Directory.CreateDirectory(fundo);
        var outro = Path.Combine(t, "outro", ".pmvni-ai", "editor", "r");
        Directory.CreateDirectory(outro);
        foreach (var ruim in new[] { fora, fundo, Path.Combine(editor, "..", "..", "..", "fora"), editor, Path.Combine(processo, ".pmvni-ai"), outro, "", processo })
        {
            Assert.NotNull(RegistroExecucao.MotivoInvalido(processo, ruim));
            if (ruim.Length > 0) Assert.Throws<InvalidOperationException>(() => RegistroExecucao.Abrir(ruim, processo));
        }
        Assert.False(File.Exists(Path.Combine(fora, "execucao.json")));

        // Uma junção no lugar do registro (ou do editor) é recusada: o editor não grava através de atalhos.
        var juncao = Path.Combine(editor, "juncao-claude");
        if (Ambiente.CriarJuncao(juncao, fora))
        {
            Assert.NotNull(RegistroExecucao.MotivoInvalido(processo, juncao));
            Assert.Throws<InvalidOperationException>(() => RegistroExecucao.Abrir(juncao, processo));
            Assert.False(File.Exists(Path.Combine(fora, "execucao.json")));
            Directory.Delete(juncao);
        }

        // execucao.json ilegível: começa vazio (nada é perdido do que é gravado depois).
        File.WriteAllText(Path.Combine(bom, "execucao.json"), "{ quebrado");
        using var r = RegistroExecucao.Abrir(bom, processo);
        r.Atualizar(o => o["situacao"] = "concluida");
        Assert.Equal("concluida", JsonNode.Parse(File.ReadAllText(Path.Combine(bom, "execucao.json")))!["situacao"]!.GetValue<string>());
    }

    [Fact]
    public async Task trava_entregue_pelo_chamador_vale_ate_o_fim()
    {
        using var c = new Cena("orq-trava-entregue", "ok", Provedor.ChatGpt);
        var trava = TravaExecucao.Tentar(c.Processo.Pasta, Provedor.ChatGpt);
        Assert.NotNull(trava);
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None, trava), Limite);
        Assert.Equal("concluida", fim.Situacao);        // a trava do chamador não é tratada como "outra execução"
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));   // e é solta no fim

        // Recusa antes da partida (peça faltando): a trava entregue também é solta.
        var trava2 = TravaExecucao.Tentar(c.Processo.Pasta, Provedor.ChatGpt);
        c.Pecas = c.Pecas with { Codex = null };
        var recusa = await c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None, trava2);
        Assert.Equal("falhou", recusa.Situacao);
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
    }
}
