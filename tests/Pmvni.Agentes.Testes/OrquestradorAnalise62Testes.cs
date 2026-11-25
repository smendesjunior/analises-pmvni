using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Tarefa 12 (erros 1–7 do diagnóstico do processo 19486): a rota ChatGPT roda o controlador do kit (<c>executar_analise_62.py</c>) direto, sem
/// sessão Codex controladora, e lê o contrato da Tarefa 2 (códigos 0/20/21/1 e o JSON da última linha). Claude e Gemini leem o mesmo contrato no
/// <c>situacao</c> e passam pela mesma checagem de versão mínima.</summary>
public partial class OrquestradorTestes
{
    static string Planta(Cena c) => Path.Combine(c.Processo.Pasta, "PROJETO", "INICIAL", "planta-ficticia.pdf");

    static string[] Argv(string[] chamada) => JsonSerializer.Deserialize<string[]>(chamada[1])!;

    static JsonObject DecisaoFicticia(string arquivo = "decisao_resposta.json") => new()
    {
        ["tipo"] = "revalidar_preparo", ["id"] = "d-1",
        ["pergunta"] = "O controlador mudou desde o preparo. Revalidar o preparo?",
        ["opcoes"] = new JsonArray(new JsonObject { ["valor"] = "revalidar", ["texto"] = "Revalidar o preparo" },
            new JsonObject { ["valor"] = "manter", ["texto"] = "Manter o preparo aprovado" }),
        ["arquivo_resposta"] = arquivo,
    };

    [Fact]
    public async Task ChatGpt_NaoAbreCodexExec()
    {
        using var c = new Cena("orq-62-ok", "ok", Provedor.ChatGpt);
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt, texto: "Confira o muro de divisa."), c.Progresso, CancellationToken.None), Limite);

        Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
        Assert.Equal(Path.Combine(c.Controle("analise"), "relatorio_dados.json"), fim.Documento);

        // A linha iniciada é o Python do controlador do kit; nenhum "codex exec" e nenhum pedido "Use $analise" pela entrada padrão.
        Assert.False(File.Exists(c.Falso("recebido.json")) && Json(c.Falso("recebido.json"))["argv"]![0]!.GetValue<string>() == "exec");
        var chamada = Assert.Single(Chamadas(c, "analise62"));
        var argv = Argv(chamada);
        Assert.Equal(new[] { "-B", "-X", "utf8", c.Pecas.Analise62!, "--case", c.Processo.Pasta, "--inputs", Planta(c) }, argv);
        Assert.Equal(LinhasDeComando.Analise62(Ambiente.AgenteFalso, false, c.Pecas.Analise62!, c.Processo.Pasta, [Planta(c)]).Args, argv);
        Assert.DoesNotContain("exec", argv);
        var recebido = Json(c.Falso("recebido.json"));
        Assert.Equal("", recebido["entrada"]!.GetValue<string>());
        Assert.DoesNotContain("$analise", File.ReadAllText(c.DoRegistro("execucao.json")));

        // As diretrizes e o tratamento dos dados vão no diretrizes.md do analise (como no analise pro).
        var diretrizes = File.ReadAllText(Path.Combine(c.Controle("analise"), "diretrizes.md"), Encoding.UTF8);
        Assert.Contains(PedidoAgente.TratamentoDoProcesso, diretrizes);
        Assert.Contains("Confira o muro de divisa.", diretrizes);
        Assert.Equal(PedidoAgente.Diretrizes("Confira o muro de divisa."), diretrizes);     // Tarefa 13: sem instruções de progresso
        Assert.Equal("Confira o muro de divisa.", File.ReadAllText(c.DoRegistro("pedido.md"), Encoding.UTF8));

        var ex = c.Execucao();
        Assert.Equal(Ambiente.AgenteFalso, ex["programa"]!.GetValue<string>());
        Assert.Equal(argv, ex["linha_de_comando"]!.AsArray().Skip(1).Select(x => x!.GetValue<string>()).ToArray());
        Assert.Equal("openai", ex["rota_codex_tecnica"]!.GetValue<string>());
        Assert.Equal("ENTREGUE", SituacaoAnalise62.Ler(Linhas(c.DoRegistro("situacoes.jsonl"))[^1]).Situacao);
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
    }

    [Fact]
    public async Task ChatGpt_CotaNaEtapaTecnica_AgendaRetomada()
    {
        using var c = new Cena("orq-62-cota", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "AGUARDANDO_COTA", ["etapa"] = "analyze", ["retomar_em_s"] = 2, ["motivo"] = "Rate limit exceeded. Try again in 2s" },
            new JsonObject { ["situacao"] = "ENTREGUE" }));
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);

        Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
        var chamadas = Chamadas(c, "analise62");
        Assert.Equal(2, chamadas.Count);                                   // a segunda veio sozinha, sem clique
        Assert.Contains("--inputs", Argv(chamadas[0]));
        Assert.DoesNotContain("--inputs", Argv(chamadas[1]));             // retomada = o controlador sem --inputs

        var cota = SituacaoAnalise62.Ler(Linhas(c.DoRegistro("situacoes.jsonl"))[0]);
        Assert.Equal(SituacaoAnalise62.AguardandoCota, cota.Situacao);
        var quando = cota.RetomarEmUtc!.Value;
        var rotulo = "Aguardando cota até " + quando.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);
        Assert.Contains(c.Progresso.Todos(), p => p.Situacao is { Codigo: "AGUARDANDO_COTA", Etapa: "analyze" } s && s.Rotulo == rotulo && s.RetomarEmUtc == quando);
        Assert.Contains(c.Progresso.Linhas(), l => l.StartsWith(rotulo, StringComparison.Ordinal));
        var segunda = DateTimeOffset.Parse(chamadas[1][4], CultureInfo.InvariantCulture);
        Assert.True(segunda >= quando - TimeSpan.FromMilliseconds(50), $"a retomada partiu às {segunda:o}, antes de {quando:o}");
    }

    [Fact]
    public async Task ChatGpt_Decisao_AbrePergunta()
    {
        using var c = new Cena("orq-62-decisao", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "DECISAO", ["etapa"] = "prepare", ["decisao"] = DecisaoFicticia() },
            new JsonObject { ["situacao"] = "ENTREGUE", ["exigir_resposta"] = "decisao_resposta.json" }));
        var perguntas = new List<DecisaoAnalise>();
        var pedido = c.Pedido(Provedor.ChatGpt) with
        {
            Decidir = (d, _) => { lock (perguntas) perguntas.Add(d); return Task.FromResult<string?>("manter"); },
        };
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);

        Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
        var d = Assert.Single(perguntas);
        Assert.Equal("O controlador mudou desde o preparo. Revalidar o preparo?", d.Pergunta);
        Assert.Equal(new[] { ("revalidar", "Revalidar o preparo"), ("manter", "Manter o preparo aprovado") }, d.Opcoes.Select(x => (x.Valor, x.Texto)));
        var resposta = Json(Path.Combine(c.Controle("analise"), "decisao_resposta.json"));
        Assert.Equal(("manter", "revalidar_preparo", "d-1"), (resposta["resposta"]!.GetValue<string>(), resposta["tipo"]!.GetValue<string>(), resposta["id"]!.GetValue<string>()));
        var chamadas = Chamadas(c, "analise62");
        Assert.Equal(2, chamadas.Count);
        Assert.DoesNotContain("--inputs", Argv(chamadas[1]));
        Assert.Contains(Chamadas(c, "resposta"), r => r[1].Contains("\"manter\"", StringComparison.Ordinal));

        // Sem quem responda (ou o analista fechou a pergunta): a análise para e espera, sem chamar o controlador de novo.
        using var semResposta = new Cena("orq-62-decisao-aberta", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "DECISAO", ["etapa"] = "prepare", ["decisao"] = DecisaoFicticia() }));
        var parado = await Ambiente.Com(semResposta.Orq().ExecutarAsync(semResposta.Pedido(Provedor.ChatGpt) with { Decidir = (_, _) => Task.FromResult<string?>(null) },
            semResposta.Progresso, CancellationToken.None), Limite);
        Assert.Equal("interrompida", parado.Situacao);
        Assert.Contains("Revalidar o preparo?", parado.Mensagem);
        Assert.Single(Chamadas(semResposta, "analise62"));
        Assert.False(File.Exists(Path.Combine(semResposta.Controle("analise"), "decisao_resposta.json")));
    }

    [Fact]
    public async Task LimiteDeCota_EsperarMantemAPerguntaPendente()
    {
        var decisao = DecisaoFicticia();
        decisao["tipo"] = "limite_cota";
        decisao["opcoes"] = new JsonArray(new JsonObject { ["valor"] = "esperar", ["texto"] = "Esperar" });
        using var c = new Cena("cota-preventiva", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "DECISAO", ["etapa"] = "analyze", ["decisao"] = decisao }));
        var pedido = c.Pedido(Provedor.ChatGpt) with { Decidir = (_, _) => Task.FromResult<string?>("esperar") };
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("limite_cota", fim.Decisao?.Tipo);
        Assert.Single(Chamadas(c, "analise62"));
        Assert.False(File.Exists(Path.Combine(c.Controle("analise"), "decisao_resposta.json")));
    }

    [Fact]
    public async Task Rota_ResolvidaAntesDeIniciar()
    {
        // Provedor incompatível no config.toml (rota "herdar"): recusa antes de iniciar qualquer processo (erro 7).
        using (var c = new Cena("orq-62-rota-ruim", "ok", Provedor.ChatGpt))
        {
            Directory.CreateDirectory(c.Pecas.CodexHome!);
            File.WriteAllText(Path.Combine(c.Pecas.CodexHome!, "config.toml"), "model = \"gpt-6\"\nmodel_provider = \"azure-corp\"\n\n[model_providers.azure-corp]\nbase_url = \"https://x\"\n");
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt) with { RotaCodex = "herdar" }, c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("falhou", fim.Situacao);
            Assert.Contains("azure-corp", fim.Mensagem);
            Assert.EndsWith("Nada foi iniciado.", fim.Mensagem);
            Assert.False(File.Exists(c.Falso("chamadas.txt")));
            Assert.False(File.Exists(c.Falso("recebido.json")));
            Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
        }

        // "herdar" com codex-lb no config.toml: o Editor diz qual rota usa e o controlador recebe a mesma (variável e registro).
        using (var c = new Cena("orq-62-rota-herdar", "ok", Provedor.ChatGpt))
        {
            Directory.CreateDirectory(c.Pecas.CodexHome!);
            File.WriteAllText(Path.Combine(c.Pecas.CodexHome!, "config.toml"), "# comentário\nmodel_provider = 'codex-lb' # do WinStatusBar\n[model_providers.codex-lb]\nmodel_provider = \"outro\"\n");
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt) with { RotaCodex = "herdar" }, c.Progresso, CancellationToken.None), Limite);
            Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
            Assert.Equal("codex-lb", Json(c.Falso("recebido.json"))["rota"]!.GetValue<string>());
            Assert.Equal("codex-lb", c.Execucao()["rota_codex_tecnica"]!.GetValue<string>());
            Assert.Contains(c.Progresso.Linhas(), l => l.StartsWith("Rota do Codex: Codex-LB", StringComparison.Ordinal) && l.Contains("config.toml", StringComparison.Ordinal));
        }

        // Escolhas explícitas: openai e codex-lb chegam ao controlador pela mesma variável nas duas camadas (erro 6); o PATH começa pelo Codex escolhido.
        foreach (var rota in new[] { "openai", "codex-lb" })
        {
            using var c = new Cena("orq-62-rota-" + rota, "ok", Provedor.ChatGpt);
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt) with { RotaCodex = rota }, c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("concluida", fim.Situacao);
            var recebido = Json(c.Falso("recebido.json"));
            Assert.Equal(rota, recebido["rota"]!.GetValue<string>());
            Assert.Equal(Path.GetDirectoryName(Ambiente.AgenteFalso), recebido["path0"]!.GetValue<string>());
            Assert.Equal(rota, c.Execucao()["rota_codex_tecnica"]!.GetValue<string>());
        }

        // Modo chave: sempre a OpenAI direta (a chave vai por CODEX_API_KEY), qualquer que seja a rota da conta.
        var chave = RotaCodex.Resolver(ModoAcesso.Chave, "codex-lb", null);
        Assert.Equal(("openai", null), (chave.Rota, chave.Recusa));
        Assert.Equal("openai", RotaCodex.Resolver(ModoAcesso.Conta, "herdar", Path.Combine(Ambiente.PastaTestes, "nao-existe")).Rota);
    }

    [Fact]
    public async Task Cancelar_MataSoOControlador()
    {
        using var c = new Cena("orq-62-cancelar", "lento", Provedor.ChatGpt);
        var outroPsi = new ProcessStartInfo(Ambiente.AgenteFalso) { UseShellExecute = false, CreateNoWindow = true };
        outroPsi.ArgumentList.Add("dormir");      // outro "Python" deste computador (outra análise, o Motor PDF): nunca é encerrado
        using var outro = Process.Start(outroPsi)!;
        try
        {
            using var cts = new CancellationTokenSource();
            var orq = c.Orq();
            var tarefa = orq.ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, cts.Token);
            int pai = 0, filho = 0;
            Assert.True(Ambiente.EsperarAte(() =>
            {
                try { var pids = Json(c.Falso("pids.json")); pai = pids["pai"]!.GetValue<int>(); filho = pids["filho"]!.GetValue<int>(); return true; }
                catch (Exception e) when (e is IOException or JsonException or NullReferenceException) { return false; }
            }, TimeSpan.FromSeconds(30)), "o controlador falso não partiu");
            var registrado = c.Execucao()["controlador"]!.AsObject();
            Assert.Equal(pai, registrado["pid"]!.GetValue<int>());

            // O critério do Claude: o pid registrado só é encerrado se for um Python que partiu perto do início gravado.
            using var outroRegistro = RegistroExecucao.Criar(c.Processo.Pasta, Provedor.ChatGpt, new DateTime(2026, 11, 15, 11, 0, 0));
            outroRegistro.Atualizar(o => o["controlador"] = new JsonObject
            {
                ["pid"] = outro.Id, ["inicio"] = new DateTimeOffset(outro.StartTime).AddHours(-1).ToString("o", CultureInfo.InvariantCulture),
            });
            Assert.False(await Ambiente.Com(orq.CancelarChatGptAsync(outroRegistro.Pasta, c.Pecas), TimeSpan.FromSeconds(30)));
            outroRegistro.Atualizar(o => o["controlador"] = new JsonObject { ["pid"] = Environment.ProcessId, ["inicio"] = DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture) });
            Assert.False(await Ambiente.Com(orq.CancelarChatGptAsync(outroRegistro.Pasta, c.Pecas), TimeSpan.FromSeconds(30)));     // não é Python
            Assert.True(Ambiente.Vivo(outro.Id));

            // Cancelar a análise: a árvore do controlador (ele e a sessão que ele abriu) sai; o outro Python continua.
            cts.Cancel();
            var fim = await Ambiente.Com(tarefa, TimeSpan.FromSeconds(15));
            Assert.Equal("cancelada", fim.Situacao);
            Assert.True(Ambiente.EsperarAte(() => !Ambiente.Vivo(pai) && !Ambiente.Vivo(filho), TimeSpan.FromSeconds(5)), "a árvore do controlador continua viva");
            Assert.True(Ambiente.Vivo(outro.Id));
            Assert.False(await Ambiente.Com(orq.CancelarChatGptAsync(c.Registro.Pasta, c.Pecas), TimeSpan.FromSeconds(30)));     // já não há o que encerrar

            // O registro que aponta o próprio controlador (partiu logo depois do início gravado) é encerrado.
            outroRegistro.Atualizar(o => o["controlador"] = new JsonObject
            {
                ["pid"] = outro.Id, ["inicio"] = new DateTimeOffset(outro.StartTime).AddSeconds(-2).ToString("o", CultureInfo.InvariantCulture),
            });
            Assert.True(await Ambiente.Com(orq.CancelarChatGptAsync(outroRegistro.Pasta, c.Pecas), TimeSpan.FromSeconds(30)));
            Assert.True(Ambiente.EsperarAte(() => !Ambiente.Vivo(outro.Id), TimeSpan.FromSeconds(5)));
        }
        finally
        {
            try { if (!outro.HasExited) outro.Kill(true); } catch (InvalidOperationException) { }
        }
    }

    [Fact]
    public async Task PluginAntigo_InformaComponente()
    {
        // Tarefa 18: as três rotas exigem o plugin único analise 4.0.0; o 3.x (sem rotas\<rota>) é recusado com a mesma mensagem, sem iniciar nada.
        var f = await RodarSemPecas("orq-62-antigo", Provedor.ChatGpt, p => p with { VersaoPluginAnalise = "3.1.0" });
        Assert.Equal("falhou", f.Situacao);
        Assert.Equal(PoliticaKit.PluginDesatualizado(Provedor.ChatGpt, "3.1.0"), f.Mensagem);
        Assert.StartsWith("Atualize o kit PMVNI (analise 4.0.0 ou mais novo): o analise instalado neste computador é o 3.1.0.", f.Mensagem);

        var semControlador = await RodarSemPecas("orq-62-sem-controlador", Provedor.ChatGpt, p => p with { Analise62 = null });
        Assert.StartsWith("Atualize o kit PMVNI (analise 4.0.0 ou mais novo)", semControlador.Mensagem);
        Assert.Equal("O Python não foi encontrado neste computador (o analise do kit precisa dele).",
            (await RodarSemPecas("orq-62-sem-python", Provedor.ChatGpt, p => p with { Python = null })).Mensagem);

        var claude = await RodarSemPecas("orq-pro-antigo", Provedor.Claude, p => p with { VersaoAnalisePro = "3.1.0" });
        Assert.StartsWith("Atualize o kit PMVNI (analise 4.0.0 ou mais novo): o analise instalado neste computador é o 3.1.0.", claude.Mensagem);
        // Kit 3.x de verdade: o analise 3.1.0 não tem rotas\claude\pro_runtime.py. Pede a atualização, não "não instalado".
        var claudeSemRota = await RodarSemPecas("orq-pro-antigo-sem-rota", Provedor.Claude, p => p with { VersaoAnalisePro = "3.1.0", ProRuntime = null });
        Assert.StartsWith("Atualize o kit PMVNI (analise 4.0.0 ou mais novo)", claudeSemRota.Mensagem);
    }

    [Fact]
    public async Task Claude_AguardandoCota_MostraHorario()
    {
        using var c = new Cena("orq-claude-aguarda-cota", "claude_ok", Provedor.Claude, j =>
        {
            j["auto"]!["etapa_ms"] = 200;
            j["auto"]!["aguardar_cota_na_etapa"] = "analyze";
            j["auto"]!["aguardar_s"] = 3;
        });
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude), c.Progresso, CancellationToken.None), Limite);

        Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem + Diagnostico(c));
        var cota = c.Progresso.Todos().Select(p => p.Situacao).FirstOrDefault(s => s?.Codigo == "AGUARDANDO_COTA");
        Assert.NotNull(cota);
        Assert.NotNull(cota!.RetomarEmUtc);
        Assert.Equal("Aguardando cota até " + cota.RetomarEmUtc!.Value.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture), cota.Rotulo);
    }

    [Fact]
    public async Task Claude_Decisao_AbrePergunta()
    {
        using var c = new Cena("orq-claude-decisao", "claude_ok", Provedor.Claude, j =>
        {
            j["auto"]!["etapa_ms"] = 100;
            j["auto"]!["decisao"] = DecisaoFicticia();
        });
        var perguntas = new List<DecisaoAnalise>();
        var pedido = c.Pedido(Provedor.Claude) with { Decidir = (d, _) => { lock (perguntas) perguntas.Add(d); return Task.FromResult<string?>("revalidar"); } };
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);

        Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem + Diagnostico(c));
        Assert.Equal("O controlador mudou desde o preparo. Revalidar o preparo?", Assert.Single(perguntas).Pergunta);
        Assert.Equal(2, Chamadas(c, "iniciar").Count);              // a partida de novo, com a resposta gravada
        Assert.Contains(Chamadas(c, "resposta"), r => r[1].Contains("\"revalidar\"", StringComparison.Ordinal));
        Assert.Equal("revalidar", Json(Path.Combine(c.Controle("analise-pro"), "decisao_resposta.json"))["resposta"]!.GetValue<string>());
    }

    [Fact]
    public async Task Claude_Falha_MostraMotivo()
    {
        using var c = new Cena("orq-claude-falha-motivo", "claude_ok", Provedor.Claude, j =>
        {
            j["auto"]!["etapa_ms"] = 50;
            j["auto"]!["final"] = "FALHA";
            j["auto"]!["erro"] = "A sessão técnica da analyze foi recusada (cota_semanal).";
        });
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude), c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("falhou", fim.Situacao);
        Assert.Contains("A sessão técnica da analyze foi recusada (cota_semanal).", fim.Mensagem);
    }

    // ------------------------------------------------------------------ rodada de correção 1 da Tarefa 12

    [Fact]
    public async Task ChatGpt_Inputs_SemArquivosTransitorios()
    {
        using var c = new Cena("orq-62-transitorios", "ok", Provedor.ChatGpt);
        var docs = Path.Combine(c.Processo.Pasta, "DOCUMENTOS");
        var inicial = Path.Combine(c.Processo.Pasta, "PROJETO", "INICIAL");
        Directory.CreateDirectory(docs);
        var memorial = Path.Combine(docs, "memorial.docx");
        File.WriteAllText(memorial, "memorial fictício");
        // O que some ou muda sozinho (trava do Word aberto, do LibreOffice, miniaturas, temporários, ocultos e de sistema) não é peça do processo.
        var transitorios = new[]
        {
            Path.Combine(docs, "~$memorial.docx"), Path.Combine(docs, ".~lock.memorial.odt#"), Path.Combine(docs, "desktop.ini"), Path.Combine(inicial, "Thumbs.db"),
            Path.Combine(inicial, "planta.pdf.tmp"), Path.Combine(inicial, "~WRL0001.tmp"), Path.Combine(docs, "baixando.pdf.crdownload"),
            Path.Combine(docs, "oculto.pdf"), Path.Combine(docs, "sistema.pdf"),
        };
        foreach (var t in transitorios) File.WriteAllText(t, "x");
        File.SetAttributes(Path.Combine(docs, "oculto.pdf"), FileAttributes.Hidden);
        File.SetAttributes(Path.Combine(docs, "sistema.pdf"), FileAttributes.System);
        try
        {
            var anexo = new Pmvni.Revisao.ArquivoArquivado(@"D:\x\~$memorial.docx", @"DOCUMENTOS\~$memorial.docx", "aa", 1, false);
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt, anexos: [anexo]), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("concluida", fim.Situacao);
            var argv = Argv(Assert.Single(Chamadas(c, "analise62")));
            var entradas = argv.SkipWhile(a => a != "--inputs").Skip(1).ToArray();
            Assert.Equal(new[] { Planta(c), memorial }.Order(StringComparer.OrdinalIgnoreCase), entradas.Order(StringComparer.OrdinalIgnoreCase));
            foreach (var t in transitorios) Assert.True(OrquestradorAnalise.ArquivoTransitorio(t), t);
            Assert.False(OrquestradorAnalise.ArquivoTransitorio(memorial));
        }
        finally
        {
            foreach (var t in transitorios) if (File.Exists(t)) File.SetAttributes(t, FileAttributes.Normal);
        }
    }

    [Fact]
    public async Task Rota_ResolvidaUmaVezParaAExecucaoInteira()
    {
        using var c = new Cena("orq-62-rota-uma-vez", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "AGUARDANDO_COTA", ["etapa"] = "analyze", ["retomar_em_s"] = 2 },
            new JsonObject { ["situacao"] = "ENTREGUE" }));
        var config = Path.Combine(c.Pecas.CodexHome!, "config.toml");
        Directory.CreateDirectory(c.Pecas.CodexHome!);
        File.WriteAllText(config, "model_provider = \"codex-lb\"\n");
        var tarefa = c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt) with { RotaCodex = "herdar" }, c.Progresso, CancellationToken.None);
        Assert.True(Ambiente.EsperarAte(() => Chamadas(c, "analise62").Count == 1, TimeSpan.FromSeconds(30)), "o controlador não partiu");
        // O config.toml muda no meio da espera de cota: a execução segue na rota resolvida na partida (nunca relida, nunca recusada no meio).
        File.WriteAllText(config, "model_provider = \"azure-corp\"\n");
        var fim = await Ambiente.Com(tarefa, Limite);
        Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
        var chamadas = Chamadas(c, "analise62");
        Assert.Equal(2, chamadas.Count);
        Assert.All(chamadas, ch => Assert.Equal("codex-lb", ch[3]));
        Assert.Equal("codex-lb", c.Execucao()["rota_codex_tecnica"]!.GetValue<string>());
    }

    [Fact]
    public async Task ChatGpt_EsperaDeCota_TemLimites()
    {
        // Horário já passado: espera o mínimo em vez de chamar de novo na hora (e gastar o teto de chamadas).
        using (var c = new Cena("orq-62-cota-passada", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "AGUARDANDO_COTA", ["etapa"] = "analyze", ["retomar_em_s"] = -120 },
            new JsonObject { ["situacao"] = "ENTREGUE" })))
        {
            var orq = new OrquestradorAnalise
            {
                IntervaloSituacao = TimeSpan.FromMilliseconds(100), IntervaloLog = TimeSpan.FromMilliseconds(100),
                AmbienteExtra = new Dictionary<string, string> { ["AGENTE_FALSO_CENARIO"] = c.Cenario }, EsperaMinimaCota = TimeSpan.FromSeconds(2),
            };
            var fim = await Ambiente.Com(orq.ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("concluida", fim.Situacao);
            var chamadas = Chamadas(c, "analise62");
            Assert.Equal(2, chamadas.Count);
            var intervalo = DateTimeOffset.Parse(chamadas[1][4], CultureInfo.InvariantCulture) - DateTimeOffset.Parse(chamadas[0][4], CultureInfo.InvariantCulture);
            Assert.True(intervalo >= TimeSpan.FromSeconds(1.9), "a retomada veio depois de só " + intervalo);
            Assert.Contains(c.Progresso.Todos(), p => p.Situacao is { Codigo: "AGUARDANDO_COTA", RetomarEmUtc: { } q } && q > DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        // Horário longe demais (30 dias, além do que um timer aceita): a análise fica parada e retomável, sem exceção e sem nova chamada.
        using (var c = new Cena("orq-62-cota-longe", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "AGUARDANDO_COTA", ["etapa"] = "analyze", ["retomar_em_s"] = 30 * 24 * 3600 })))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("interrompida", fim.Situacao);
            Assert.StartsWith("A cota do ChatGPT só volta em ", fim.Mensagem);
            Assert.Single(Chamadas(c, "analise62"));
            Assert.Equal("interrompida", c.Execucao()["situacao"]!.GetValue<string>());
            Assert.NotNull(c.Execucao()["aguardando_cota_ate_utc"]);
        }
    }

    [Fact]
    public async Task Gemini_PluginAntigo_InformaComponente()
    {
        using var c = new Cena("orq-gemini-antigo", "claude_ok", Provedor.Gemini);
        c.Pecas = ComGemini(c) with { VersaoAnaliseGoogle = "3.1.0" };
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Gemini, ModoAcesso.Conta) with { Perfil = PerfilGemini.ContaUltra }, c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("falhou", fim.Situacao);
        Assert.Equal(Provedores.RotaDesativada, fim.Mensagem);
        Assert.False(File.Exists(c.Falso("chamadas.txt")));

        // Kit 3.x de verdade: sem rotas\gemini\google_runtime.py, também pede a atualização (não "não instalado").
        c.Pecas = ComGemini(c) with { VersaoAnaliseGoogle = "3.1.0", GoogleRuntime = null };
        var semRota = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Gemini, ModoAcesso.Conta) with { Perfil = PerfilGemini.ContaUltra }, c.Progresso, CancellationToken.None), Limite);
        Assert.Equal(Provedores.RotaDesativada, semRota.Mensagem);
        Assert.False(File.Exists(c.Falso("chamadas.txt")));
    }
}
