using System.Text;
using System.Text.Json.Nodes;
using Pmvni.Revisao;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Falha de E/S no registro da execução (OneDrive segurando o arquivo, disco cheio): nunca para a leitura do agente nem muda o resultado.</summary>
public partial class OrquestradorTestes
{
    /// <summary>Cria (vazio) e prende o arquivo do registro sem compartilhar nada, como o cliente do OneDrive durante o envio.</summary>
    static FileStream Prender(string arquivo) => new(arquivo, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    [Fact]
    public void registro_preso_nao_lanca_e_grava_tudo_quando_solta()
    {
        using var pasta = Ambiente.PastaDeTeste("registro-preso");
        var processo = Ambiente.NovoProcesso(pasta);
        using var r = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, new DateTime(2026, 11, 15, 10, 30, 0));
        var arquivo = Path.Combine(r.Pasta, "saida.jsonl");
        var preso = Prender(arquivo);
        try
        {
            for (var i = 1; i <= 3; i++) r.AcrescentarSaida("linha " + i);
            r.Descarregar();                                    // não lança
            // Linhas grandes forçam a descarga automática (acima de 4 MB): também não lança.
            var grande = new string('g', 1024 * 1024);
            for (var i = 0; i < 6; i++) r.AcrescentarErro(grande);
            r.Descarregar();
            Assert.True(r.FalhasDeGravacao > 0);
        }
        finally { preso.Dispose(); }

        r.AcrescentarSaida("linha 4");
        r.Descarregar();
        Assert.Equal(new[] { "linha 1", "linha 2", "linha 3", "linha 4" }, File.ReadAllLines(arquivo, Encoding.UTF8));
        Assert.Equal(6, File.ReadAllLines(Path.Combine(r.Pasta, "erros.txt"), Encoding.UTF8).Length);
    }

    [Fact]
    public void registro_preso_retem_no_maximo_o_teto_e_avisa_o_que_perdeu()
    {
        using var pasta = Ambiente.PastaDeTeste("registro-preso-teto");
        var processo = Ambiente.NovoProcesso(pasta);
        using var r = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, new DateTime(2026, 11, 15, 10, 30, 0));
        r.LimitePendenteBytes = 64 * 1024;
        var arquivo = Path.Combine(r.Pasta, "saida.jsonl");
        using (Prender(arquivo))
        {
            var linha = new string('x', 1000);
            for (var i = 0; i < 200; i++) r.AcrescentarSaida(linha);      // ~200 KB > 64 KB
            r.Descarregar();
        }
        r.Descarregar();
        var linhas = File.ReadAllLines(arquivo, Encoding.UTF8);
        Assert.True(linhas.Length < 200);
        Assert.StartsWith("registro: ", linhas[^1]);
        Assert.Contains("não puderam ser gravadas", linhas[^1]);
    }

    [Fact]
    public async Task chatgpt_com_registro_preso_termina_e_importa()
    {
        // O Codex escreve 4 MB no erro (mais que o buffer do canal) e muitas linhas na saída, com saida.jsonl e erros.txt presos o tempo todo.
        using var c = new Cena("orq-registro-preso", "ok", Provedor.ChatGpt, o => { o["linhas"] = 20_000; o["eventos"] = new JsonArray(); o["inundacao_erro_mb"] = 4; o["intervalo_ms"] = 0; });
        var presos = new[] { Prender(c.DoRegistro("saida.jsonl")), Prender(c.DoRegistro("erros.txt")) };
        FimAnalise fim;
        try { fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite); }
        finally { foreach (var p in presos) p.Dispose(); }

        Assert.Equal("concluida", fim.Situacao);
        Assert.Equal(Path.Combine(c.Processo.Pasta, ".pmvni-ai", "analise", "relatorio_dados.json"), fim.Documento);
        Assert.Equal(20_000, c.Progresso.QuantidadeDeLinhas);          // o andamento na tela não congelou
        var ex = c.Execucao();
        Assert.Equal("concluida", ex["situacao"]!.GetValue<string>());
        Assert.StartsWith("O registro não pôde gravar ", ex["aviso_registro"]!.GetValue<string>());
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
    }

    [Fact]
    public async Task claude_com_registro_preso_termina_concluida()
    {
        using var c = new Cena("orq-claude-registro-preso", "claude_ok", Provedor.Claude);
        FimAnalise fim;
        using (Prender(c.DoRegistro("situacoes.jsonl")))
            fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude), c.Progresso, CancellationToken.None), Limite);

        Assert.Equal("concluida", fim.Situacao);
        Assert.Equal(Path.Combine(c.Controle("analise-pro"), "relatorio_dados.json"), fim.Documento);
        Assert.Equal("concluida", c.Execucao()["situacao"]!.GetValue<string>());
    }

    [Fact]
    public async Task callback_que_lanca_nao_para_a_leitura_do_processo()
    {
        using var pasta = Ambiente.PastaDeTeste("callback-lanca");
        var cenario = Ambiente.CenarioAjustado(pasta, "ok", o => { o["inundacao_erro_mb"] = 4; });
        var processo = Ambiente.NovoProcesso(pasta);
        var (exe, args) = LinhasDeComando.CodexExec(Ambiente.AgenteFalso, processo.Pasta, Path.Combine(pasta, "ultima.md"));
        var erros = 0;
        using var execucao = ExecucaoProcesso.Iniciar(exe, args, processo.Pasta, "pedido", env => env["AGENTE_FALSO_CENARIO"] = cenario,
            _ => throw new IOException("saída presa"), _ => { Interlocked.Increment(ref erros); throw new IOException("erro preso"); });
        var codigo = await Ambiente.Com(execucao.Fim, Limite);
        Assert.Equal(0, codigo);
        Assert.Equal(4 * 1024, Volatile.Read(ref erros));               // cada linha do erro chegou, apesar do callback lançar
    }

    [Fact]
    public async Task codex_fica_no_job_e_morre_com_o_editor()
    {
        // O editor que termina (até numa queda) fecha o job: o Codex e os filhos dele são encerrados (o ChatGPT pertence à janela).
        using var c = new Cena("orq-chatgpt-job", "lento", Provedor.ChatGpt);
        var tarefa = new TarefaDoEditor();
        var orq = new OrquestradorAnalise
        {
            IntervaloSituacao = TimeSpan.FromMilliseconds(100), IntervaloLog = TimeSpan.FromMilliseconds(100),
            AmbienteExtra = new Dictionary<string, string> { ["AGENTE_FALSO_CENARIO"] = c.Cenario }, TarefaCodex = tarefa,
        };
        var execucao = orq.ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None);
        int pai = 0, filho = 0;
        Assert.True(Ambiente.EsperarAte(() =>
        {
            try
            {
                var pids = Json(c.Falso("pids.json"));
                pai = pids["pai"]!.GetValue<int>();
                filho = pids["filho"]!.GetValue<int>();
                return true;
            }
            catch (Exception e) when (e is IOException or System.Text.Json.JsonException or NullReferenceException) { return false; }
        }, TimeSpan.FromSeconds(30)), "o agente falso não partiu");
        using (var p = System.Diagnostics.Process.GetProcessById(pai)) Assert.True(tarefa.Contem(p));
        using (var f = System.Diagnostics.Process.GetProcessById(filho)) Assert.True(tarefa.Contem(f));

        tarefa.Dispose();     // o que o Windows faz quando o processo do editor termina
        Assert.True(Ambiente.EsperarAte(() => !Ambiente.Vivo(pai) && !Ambiente.Vivo(filho), TimeSpan.FromSeconds(10)), "o Codex sobreviveu ao editor");
        var fim = await Ambiente.Com(execucao, Limite);
        Assert.NotEqual("concluida", fim.Situacao);
    }

    [Fact]
    public void leitura_do_log_tem_teto_de_4_mb()
    {
        using var pasta = Ambiente.PastaDeTeste("log-teto");
        var log = Path.Combine(pasta, "auto-log.txt");
        File.WriteAllText(log, "");
        var leitor = new OrquestradorAnalise.LeitorLog(log);
        leitor.IgnorarOQueJaHa();
        var linha = new string('l', 999);
        using (var w = new StreamWriter(log, append: true, new UTF8Encoding(false)) { NewLine = "\n" })
            for (var i = 0; i < 10 * 1024; i++) w.WriteLine(i.ToString("D6") + linha);       // ~10 MB de uma vez
        var novas = leitor.Novas();
        Assert.True(novas.Sum(l => l.Length + 1) <= OrquestradorAnalise.LeitorLog.TetoDaLeitura);
        Assert.StartsWith((10 * 1024 - 1).ToString("D6"), novas[^1]);      // o fim mais recente chega
    }
}
