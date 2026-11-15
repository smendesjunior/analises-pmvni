using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Uma execução por processo (desenho §12.7) e o registro da execução (§12.10).</summary>
public class TravaExecucaoTestes
{
    static string Lock(string pasta) => Path.Combine(pasta, ".pmvni-ai", "editor", "execucao.lock");

    [Fact]
    public void uma_execucao_por_processo()
    {
        using var t = Ambiente.PastaDeTeste("trava");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        Assert.False(TravaExecucao.EmAndamento(pasta));

        var primeira = TravaExecucao.Tentar(pasta, Provedor.ChatGpt);
        Assert.NotNull(primeira);
        Assert.True(TravaExecucao.EmAndamento(pasta));
        Assert.Null(TravaExecucao.Tentar(pasta, Provedor.Claude));        // a segunda, de qualquer provedor, não passa
        Assert.Null(TravaExecucao.Tentar(pasta, Provedor.ChatGpt));

        // Outro processo é outro caso.
        var outro = Pmvni.Revisao.CriacaoProcesso.Criar(Path.Combine(t, "processos"), 2, 2099, "Beltrano").Processo.Pasta;   // a raiz já existe (do primeiro)
        using (TravaExecucao.Tentar(outro, Provedor.Claude)!) Assert.True(TravaExecucao.EmAndamento(outro));

        primeira!.Dispose();
        Assert.False(File.Exists(Lock(pasta)));
        Assert.False(TravaExecucao.EmAndamento(pasta));
        using var depois = TravaExecucao.Tentar(pasta, Provedor.Claude);       // livre de novo
        Assert.NotNull(depois);
        primeira.Dispose();                                                    // Dispose repetido não apaga a trava dos outros
        Assert.True(File.Exists(Lock(pasta)));
    }

    [Fact]
    public void conteudo_da_trava_tem_pid_provedor_e_inicio()
    {
        using var t = Ambiente.PastaDeTeste("trava-conteudo");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        using var trava = TravaExecucao.Tentar(pasta, Provedor.Claude)!;
        var json = JsonNode.Parse(File.ReadAllText(Lock(pasta)))!.AsObject();
        Assert.Equal(Environment.ProcessId, json["pid"]!.GetValue<int>());
        Assert.Equal("claude", json["provedor"]!.GetValue<string>());
        Assert.True(DateTimeOffset.TryParse(json["inicio"]!.GetValue<string>(), out _));
    }

    [Fact]
    public void trava_com_pid_morto_e_substituida()
    {
        using var t = Ambiente.PastaDeTeste("trava-morta");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        Directory.CreateDirectory(Path.GetDirectoryName(Lock(pasta))!);
        var morto = ProcessoMorto();
        File.WriteAllText(Lock(pasta), new JsonObject { ["pid"] = morto, ["provedor"] = "chatgpt", ["inicio"] = DateTimeOffset.UtcNow.ToString("o") }.ToJsonString());
        Assert.False(TravaExecucao.EmAndamento(pasta));
        using var nova = TravaExecucao.Tentar(pasta, Provedor.Claude);
        Assert.NotNull(nova);
        Assert.Equal(Environment.ProcessId, JsonNode.Parse(File.ReadAllText(Lock(pasta)))!["pid"]!.GetValue<int>());
    }

    [Fact]
    public void trava_ilegivel_e_antiga_e_abandonada()
    {
        using var t = Ambiente.PastaDeTeste("trava-ilegivel");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        Directory.CreateDirectory(Path.GetDirectoryName(Lock(pasta))!);
        File.WriteAllText(Lock(pasta), "lixo");
        File.SetLastWriteTimeUtc(Lock(pasta), DateTime.UtcNow.AddMinutes(-5));
        using var nova = TravaExecucao.Tentar(pasta, Provedor.ChatGpt);
        Assert.NotNull(nova);
    }

    /// <summary>Pid que já existiu e morreu: o do agente falso depois de terminar.</summary>
    static int ProcessoMorto()
    {
        var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Ambiente.AgenteFalso, "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return p.Id;
    }

    [Fact]
    public void registro_da_execucao()
    {
        using var t = Ambiente.PastaDeTeste("registro");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        var agora = new DateTime(2026, 11, 15, 14, 32, 5);
        var r = RegistroExecucao.Criar(pasta, Provedor.ChatGpt, agora);
        Assert.Equal(Path.Combine(pasta, ".pmvni-ai", "editor", "2026-11-15_143205-chatgpt"), r.Pasta);
        Assert.True(Directory.Exists(r.Pasta));

        // Repetida no mesmo segundo: _2, _3.
        Assert.EndsWith("2026-11-15_143205-chatgpt_2", RegistroExecucao.Criar(pasta, Provedor.ChatGpt, agora).Pasta);
        Assert.EndsWith("2026-11-15_143205-chatgpt_3", RegistroExecucao.Criar(pasta, Provedor.ChatGpt, agora).Pasta);
        Assert.EndsWith("2026-11-15_143205-claude", RegistroExecucao.Criar(pasta, Provedor.Claude, agora).Pasta);

        r.GravarPedido("Pedido com acentuação: ção\nsegunda linha");
        Assert.Equal("Pedido com acentuação: ção\nsegunda linha", File.ReadAllText(Path.Combine(r.Pasta, "pedido.md")));
        Assert.False(File.ReadAllBytes(Path.Combine(r.Pasta, "pedido.md")).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "sem BOM");

        r.GravarAnexos(new[]
        {
            new Pmvni.Revisao.ArquivoArquivado(@"D:\a\planta.pdf", @"PROJETO\INICIAL\planta.pdf", "abc", 10, false),
            new Pmvni.Revisao.ArquivoArquivado(@"D:\a\req.pdf", @"DOCUMENTOS\req.pdf", "def", 20, true),
        });
        var anexos = JsonNode.Parse(File.ReadAllText(Path.Combine(r.Pasta, "anexos.json")))!.AsArray();
        Assert.Equal(2, anexos.Count);
        Assert.Equal(("planta.pdf", @"PROJETO\INICIAL\planta.pdf", "abc", 10L, "COPIADO"),
            (anexos[0]!["origem"]!.GetValue<string>(), anexos[0]!["destino"]!.GetValue<string>(), anexos[0]!["sha256"]!.GetValue<string>(), anexos[0]!["tamanho"]!.GetValue<long>(), anexos[0]!["situacao"]!.GetValue<string>()));
        Assert.Equal("JA_ARQUIVADO", anexos[1]!["situacao"]!.GetValue<string>());

        r.AcrescentarSaida("{\"a\":1}");
        r.AcrescentarSaida("segunda");
        r.AcrescentarErro("erro 1");
        r.AcrescentarSituacao("{\"situacao\":\"EM_ANDAMENTO\"}");
        r.Descarregar();
        Assert.Equal(new[] { "{\"a\":1}", "segunda" }, File.ReadAllLines(Path.Combine(r.Pasta, "saida.jsonl")));
        Assert.Equal(new[] { "erro 1" }, File.ReadAllLines(Path.Combine(r.Pasta, "erros.txt")));
        Assert.Equal(new[] { "{\"situacao\":\"EM_ANDAMENTO\"}" }, File.ReadAllLines(Path.Combine(r.Pasta, "situacoes.jsonl")));
        r.Dispose();
    }

    [Fact]
    public async Task execucao_json_e_sempre_json_valido()
    {
        using var t = Ambiente.PastaDeTeste("registro-atomico");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        using var r = RegistroExecucao.Criar(pasta, Provedor.Claude, new DateTime(2026, 11, 15, 8, 0, 0));
        var caminho = Path.Combine(r.Pasta, "execucao.json");
        r.Atualizar(o => o["provedor"] = "claude");
        Assert.Equal("claude", JsonNode.Parse(File.ReadAllText(caminho))!["provedor"]!.GetValue<string>());

        using var parar = new CancellationTokenSource();
        var leituras = 0;
        var leitor = Task.Run(() =>
        {
            while (!parar.IsCancellationRequested)
            {
                try
                {
                    var texto = File.ReadAllText(caminho);
                    JsonNode.Parse(texto);                 // lança se vier pela metade
                    leituras++;
                }
                catch (IOException) { /* o Windows pode negar a abertura no instante da troca; o conteúdo lido nunca é parcial */ }
            }
        });
        for (var i = 0; i < 300; i++) r.Atualizar(o => { o["contador"] = i; o["texto"] = new string('x', 5000 + i); });
        parar.Cancel();
        await leitor;
        Assert.True(leituras > 0);
        var final = JsonNode.Parse(File.ReadAllText(caminho))!.AsObject();
        Assert.Equal(299, final["contador"]!.GetValue<int>());
        Assert.Equal("claude", final["provedor"]!.GetValue<string>());       // mudanças se acumulam
        Assert.DoesNotContain(Directory.GetFiles(r.Pasta), f => f.EndsWith(".tmp"));
    }
}
