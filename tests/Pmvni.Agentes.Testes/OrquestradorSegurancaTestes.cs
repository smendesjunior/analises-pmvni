using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Rodada de correção da Tarefa 19: chave nunca em texto puro, limites de saída, neto que segura o canal e janela de início do cancelamento.</summary>
public partial class OrquestradorTestes
{
    static readonly JsonSerializerOptions Relaxado = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static string SemAspas(string json) => json[1..^1];

    /// <summary>A chave crua e as duas formas escapadas como JSON (a padrão, com ", e a relaxada, com \").</summary>
    static string[] FormasDaChave(string chave) => new[] { chave, SemAspas(JsonSerializer.Serialize(chave)), SemAspas(JsonSerializer.Serialize(chave, Relaxado)) };

    [Fact]
    public async Task chave_repetida_pelo_agente_nao_chega_a_arquivo_nem_a_tela()
    {
        // Chave com aspas, barra e símbolos que o JSON escapa: as três formas precisam sumir.
        var chave = "sk-teste-\"aspas\"-\\barra\\-+<&>-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        using var c = new Cena("orq-eco", "chave", Provedor.ChatGpt, o => o["ecoar_chave"] = true);
        CofreChaves.Guardar(c.RaizDados, Provedor.ChatGpt, chave);

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt, ModoAcesso.Chave, "x"), c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("concluida", fim.Situacao);

        // O filho recebeu a chave (o eco prova), e mesmo assim ela não está em lugar nenhum.
        Assert.Equal("CODEX_API_KEY", Json(c.Falso("recebido.json"))["variavel"]!.GetValue<string>());
        Assert.Contains("eco cru: ***", File.ReadAllText(c.DoRegistro("saida.jsonl")));
        Assert.Equal(3, Linhas(c.DoRegistro("saida.jsonl")).Count(l => l.Contains("***")));     // o eco cru e as duas linhas JSON (a padrão escapa "ã" como ã)
        Assert.Contains("eco erro: ***", File.ReadAllText(c.DoRegistro("erros.txt")));
        Assert.Contains("ultima: ***", File.ReadAllText(c.DoRegistro("ultima-mensagem.md")));

        foreach (var arquivo in Directory.GetFiles(c.Pasta, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)))
        {
            var texto = File.ReadAllText(arquivo);
            foreach (var forma in FormasDaChave(chave)) Assert.DoesNotContain(forma, texto);
            Assert.False(CofreChavesTestes.Contem(File.ReadAllBytes(arquivo), chave), "a chave apareceu em " + arquivo);
        }
        var linhas = c.Progresso.Linhas();
        Assert.Contains(linhas, l => l.Contains("***"));
        foreach (var l in linhas) foreach (var forma in FormasDaChave(chave)) Assert.DoesNotContain(forma, l);
    }

    [Fact]
    public void redator_troca_a_chave_nas_tres_formas_e_o_comeco_cortado()
    {
        var chave = "sk-abc\"def\\ghi+jkl-0123456789";
        var r = new RedatorChave(chave);
        foreach (var forma in FormasDaChave(chave)) Assert.Equal("a ***, b", r.Aplicar("a " + forma + ", b"));
        Assert.Equal("***|***", r.Aplicar(chave + "|" + chave));
        Assert.Equal("sem chave aqui", r.Aplicar("sem chave aqui"));
        Assert.Equal("", r.Aplicar(""));

        // Linha cortada no limite no meio da chave: o começo da chave também sai.
        var cortada = "texto " + chave[..12] + ExecucaoProcesso.MarcaLinhaTruncada;
        Assert.Equal("texto ***" + ExecucaoProcesso.MarcaLinhaTruncada, r.Aplicar(cortada));
        // Linha comum que só termina parecido com o começo da chave não é mexida.
        Assert.Equal("texto " + chave[..12], r.Aplicar("texto " + chave[..12]));

        Assert.False(new RedatorChave(null).Ativo);
        Assert.False(new RedatorChave("   ").Ativo);
        Assert.Equal("qualquer coisa", new RedatorChave(null).Aplicar("qualquer coisa"));
    }

    [Fact]
    public async Task linha_gigante_sem_quebra_e_cortada_e_o_agente_nao_trava()
    {
        using var c = new Cena("orq-flood-linha", "ok", Provedor.ChatGpt, o => { o["eventos"] = new JsonArray(); o["intervalo_ms"] = 0; o["inundacao_sem_quebra_mb"] = 8; });
        var relogio = Stopwatch.StartNew();
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), TimeSpan.FromSeconds(120));
        relogio.Stop();

        Assert.Equal("concluida", fim.Situacao);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(60), "levou " + relogio.Elapsed);
        var linhas = Linhas(c.DoRegistro("saida.jsonl"));
        Assert.Equal(2, linhas.Length);
        Assert.Equal(ExecucaoProcesso.LimiteLinha + ExecucaoProcesso.MarcaLinhaTruncada.Length, linhas[0].Length);
        Assert.EndsWith(ExecucaoProcesso.MarcaLinhaTruncada, linhas[0]);
        Assert.Equal("fim da inundação sem quebra", linhas[1]);
        Assert.All(c.Progresso.Linhas(), l => Assert.True(l.Length <= EventosCodex.LimiteLinha));
        Assert.True(new FileInfo(c.DoRegistro("saida.jsonl")).Length < 1_300_000);
    }

    [Fact]
    public async Task arquivos_de_saida_tem_limite_e_o_agente_continua_sendo_lido()
    {
        using var c = new Cena("orq-flood-arquivo", "ok", Provedor.ChatGpt, o => { o["eventos"] = new JsonArray(); o["intervalo_ms"] = 0; o["inundacao_linhas_mb"] = 2; o["inundacao_erro_mb"] = 2; });
        c.Registro.LimiteArquivoBytes = 200 * 1024;

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), TimeSpan.FromSeconds(120));
        Assert.Equal("concluida", fim.Situacao);     // o agente terminou: nunca ficou bloqueado pelos limites

        foreach (var arquivo in new[] { "saida.jsonl", "erros.txt" })
        {
            var caminho = c.DoRegistro(arquivo);
            Assert.InRange(new FileInfo(caminho).Length, 100 * 1024, 200 * 1024 + 200);
            var linhas = Linhas(caminho);
            Assert.StartsWith("saída truncada: limite de 200 KB do arquivo atingido", linhas[^1]);
            Assert.Single(linhas, l => l.StartsWith("saída truncada", StringComparison.Ordinal));
        }
        // A tela continua recebendo tudo (2048 linhas da inundação + a de fim).
        Assert.Equal(2049, c.Progresso.QuantidadeDeLinhas);
        Assert.Equal("fim da inundação de linhas", c.Progresso.Linhas()[^1]);
    }

    [Fact]
    public async Task neto_que_segura_o_canal_nao_prende_o_fim()
    {
        using var c = new Cena("orq-neto", "ok", Provedor.ChatGpt, o => o["filho"] = true);     // o filho "dormir" herda os canais e sobrevive ao agente
        var relogio = Stopwatch.StartNew();
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), TimeSpan.FromSeconds(60));
        relogio.Stop();

        Assert.Equal("concluida", fim.Situacao);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(20), "o Fim esperou " + relogio.Elapsed);
        var filho = Json(c.Falso("pids.json"))["filho"]!.GetValue<int>();
        Assert.True(Ambiente.Vivo(filho), "o neto devia estar vivo, segurando o canal (senão o teste não prova nada)");
        Assert.False(Ambiente.Vivo(Json(c.Falso("pids.json"))["pai"]!.GetValue<int>()));
    }

    [Fact]
    public async Task cancelar_claude_so_encerra_processo_que_partiu_perto_do_inicio()
    {
        using var c = new Cena("orq-claude-janela", "claude_ok", Provedor.Claude);
        var orq = c.Orq();
        var psi = new ProcessStartInfo(Ambiente.AgenteFalso) { UseShellExecute = false, CreateNoWindow = true };
        psi.ArgumentList.Add("dormir");       // dorme até 60 s e sai sozinho
        using var processo = Process.Start(psi)!;
        try
        {
            var partiu = processo.StartTime;
            var status = c.Controle("analise-pro\\auto-status.json");
            Directory.CreateDirectory(Path.GetDirectoryName(status)!);
            void Grava(DateTime inicio) => File.WriteAllText(status, new JsonObject
            {
                ["situacao"] = "EM_ANDAMENTO", ["pid"] = processo.Id, ["inicio"] = inicio.ToString("yyyy-MM-ddTHH:mm:ss"),
            }.ToJsonString());

            Grava(partiu.AddHours(-1));        // o "inicio" é de uma hora antes: o processo partiu tarde demais para ser o do kit
            Assert.False(await Ambiente.Com(orq.CancelarClaudeAsync(c.Processo.Pasta, c.Pecas), TimeSpan.FromSeconds(60)));
            Assert.True(Ambiente.Vivo(processo.Id));
            Grava(partiu.AddMinutes(10));      // o "inicio" é depois da partida do processo: outro programa (pid reaproveitado)
            Assert.False(await Ambiente.Com(orq.CancelarClaudeAsync(c.Processo.Pasta, c.Pecas), TimeSpan.FromSeconds(60)));
            Assert.True(Ambiente.Vivo(processo.Id));

            Grava(partiu.AddSeconds(-2));      // o do kit: partiu logo depois do "inicio"
            Assert.True(await Ambiente.Com(orq.CancelarClaudeAsync(c.Processo.Pasta, c.Pecas), TimeSpan.FromSeconds(60)));
            Assert.True(Ambiente.EsperarAte(() => !Ambiente.Vivo(processo.Id), TimeSpan.FromSeconds(5)));
        }
        finally
        {
            try { if (!processo.HasExited) processo.Kill(true); } catch (InvalidOperationException) { }
        }
    }
}
