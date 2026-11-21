using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Revisao;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>O serviço inteiro, do pedido ao resultado, sempre contra o agente falso (a trava <c>SomenteExecutaveisEm</c> impede qualquer programa real).</summary>
[Collection("Ambiente")]
public partial class OrquestradorTestes
{
    static readonly TimeSpan Limite = TimeSpan.FromSeconds(90);

    sealed class Cena : IDisposable
    {
        public PastaTeste Pasta { get; }
        public Processo Processo { get; }
        public string RaizDados { get; }
        public Pecas Pecas { get; set; }
        public string Cenario { get; }
        public RegistroExecucao Registro { get; }
        public Coletor Progresso { get; } = new();

        public Cena(string nome, string cenario, Provedor p, Action<JsonObject>? ajustar = null)
        {
            Pasta = Ambiente.PastaDeTeste(nome);
            Processo = Ambiente.NovoProcesso(Pasta);
            // Uma peça original no processo: é o que o editor passa ao controlador do analise em --inputs.
            Directory.CreateDirectory(Path.Combine(Processo.Pasta, "PROJETO", "INICIAL"));
            File.WriteAllText(Path.Combine(Processo.Pasta, "PROJETO", "INICIAL", "planta-ficticia.pdf"), "%PDF-1.4 planta fictícia");
            RaizDados = Path.Combine(Pasta, "dados");
            Pecas = Ambiente.PecasFalsas(Pasta);
            Cenario = ajustar is null ? Ambiente.Cenario(cenario) : Ambiente.CenarioAjustado(Pasta, cenario, ajustar);
            Registro = RegistroExecucao.Criar(Processo.Pasta, p, new DateTime(2026, 11, 15, 10, 30, 0));
        }

        public OrquestradorAnalise Orq(int intervaloMs = 100) => new()
        {
            IntervaloSituacao = TimeSpan.FromMilliseconds(intervaloMs),
            IntervaloLog = TimeSpan.FromMilliseconds(intervaloMs),
            AmbienteExtra = new Dictionary<string, string> { ["AGENTE_FALSO_CENARIO"] = Cenario },
            EsperaMinimaCota = TimeSpan.FromMilliseconds(200),
        };

        public PedidoExecucao Pedido(Provedor p, ModoAcesso modo = ModoAcesso.Conta, string? texto = null, IReadOnlyList<ArquivoArquivado>? anexos = null) =>
            new(p, Processo, Pecas, modo, RaizDados, texto, anexos ?? Array.Empty<ArquivoArquivado>(), Registro);

        public string Controle(string sub) => Path.Combine(Processo.Pasta, ".pmvni-ai", sub);

        public string Falso(string arquivo) => Path.Combine(Processo.Pasta, ".pmvni-ai", "agente-falso", arquivo);

        public string DoRegistro(string arquivo) => Path.Combine(Registro.Pasta, arquivo);

        public JsonObject Execucao() => JsonNode.Parse(File.ReadAllText(DoRegistro("execucao.json")))!.AsObject();

        public void Dispose()
        {
            Registro.Dispose();
            Matar();
            Pasta.Dispose();
        }

        /// <summary>Se um teste falhou no meio, não deixa processo do agente falso vivo.</summary>
        void Matar()
        {
            foreach (var arquivo in new[] { Falso("pids.json"), Controle("analise-pro\\auto-status.json") })
            {
                try
                {
                    var json = JsonNode.Parse(File.ReadAllText(arquivo))!.AsObject();
                    foreach (var chave in new[] { "pai", "filho", "pid" })
                        if (json[chave] is JsonValue v && v.TryGetValue<int>(out var pid) && Ambiente.Vivo(pid))
                        {
                            // Só o agente falso (um pid velho pode ter sido reaproveitado por outro programa).
                            using var processo = Process.GetProcessById(pid);
                            if (processo.ProcessName == "AgenteFalso") processo.Kill(true);
                        }
                }
                catch (Exception e) when (e is IOException or JsonException or ArgumentException or InvalidOperationException or NullReferenceException) { }
            }
        }
    }

    /// <summary>O estado do kit falso, para a mensagem de um teste que espera por ele: o auto-status.json, as chamadas e os AgenteFalso vivos.</summary>
    static string Diagnostico(Cena c)
    {
        string Ler(string arquivo)
        {
            try { return File.Exists(arquivo) ? File.ReadAllText(arquivo).ReplaceLineEndings(" ") : "(não existe)"; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return "(ilegível: " + e.Message + ")"; }
        }
        var vivos = string.Join(",", Process.GetProcessesByName("AgenteFalso").Select(p => p.Id));
        var acoes = File.Exists(c.Falso("chamadas.txt"))
            ? string.Join(",", File.ReadAllLines(c.Falso("chamadas.txt"), Encoding.UTF8).Select(l => l.Split('\t')[0]))
            : "(nenhuma)";
        return " | status: " + Ler(Path.Combine(c.Controle("analise-pro"), "auto-status.json"))
            + " | chamadas: " + acoes + " | erros do agente falso: " + Ler(c.Falso("erros.txt")) + " | partidas: " + Ler(c.Falso("partidas.txt"))
            + " | auto-log: " + Ler(Path.Combine(c.Controle("analise-pro"), "auto-log.txt")) + " | AgenteFalso vivos: " + vivos;
    }

    static string[] Linhas(string arquivo) => File.ReadAllLines(arquivo, Encoding.UTF8);

    static JsonObject Json(string arquivo) => JsonNode.Parse(File.ReadAllText(arquivo))!.AsObject();

    // ------------------------------------------------------------------ ChatGPT (analise: executar_analise_62.py, Tarefa 12)

    [Fact]
    public async Task chatgpt_do_inicio_ao_resultado()
    {
        using var c = new Cena("orq-chatgpt-ok", "ok", Provedor.ChatGpt, o => o["eventos"] = new JsonArray(
            "prepare: gpt-6.2-luna / low", "analyze: gpt-6.2-astra / high", "{\"saida\":\"de ferramenta\"}", "texto solto do controlador"));
        const string texto = "Analise a reapresentação.\nSegunda linha do pedido.";
        var anexo = Path.Combine(c.Processo.Pasta, "PROJETO", "INICIAL", "planta.pdf");
        File.WriteAllText(anexo, "%PDF-1.4 anexo fictício");
        var anexos = new[] { new ArquivoArquivado(@"D:\x\planta.pdf", @"PROJETO\INICIAL\planta.pdf", "aa", 1, false) };

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt, texto: texto, anexos: anexos), c.Progresso, CancellationToken.None), Limite);

        Assert.Equal("concluida", fim.Situacao);
        Assert.Equal(Path.Combine(c.Processo.Pasta, ".pmvni-ai", "analise", "relatorio_dados.json"), fim.Documento);
        Assert.Null(fim.Pdf);
        Assert.Equal(0, fim.Codigo);

        // Progresso: a rota usada, as etapas que o controlador anunciou e o texto dele; JSON nunca aparece.
        var config = Path.Combine(c.Pecas.CodexHome!, "config.toml");
        Assert.Equal(new[]
        {
            "Rota do Codex: OpenAI direto (configuração do Codex neste PC (" + config + "))",
            "Etapa Preparo: gpt-6.2-luna, raciocínio low", "Etapa Análise: gpt-6.2-astra, raciocínio high", "texto solto do controlador",
        }, c.Progresso.Linhas());
        Assert.Contains(c.Progresso.Todos(), p => p.Situacao is { Codigo: "EM_ANDAMENTO", Etapa: "analyze" });
        Assert.Equal("CONCLUIDO", c.Progresso.Todos().Last(p => p.Situacao is not null).Situacao!.Codigo);

        // Registro: a saída inteira (com a situação do contrato na última linha), o pedido digitado e os anexos.
        var saida = Linhas(c.DoRegistro("saida.jsonl"));
        Assert.Equal(5, saida.Length);
        Assert.Equal("prepare: gpt-6.2-luna / low", saida[0]);
        Assert.Equal("ENTREGUE", SituacaoAnalise62.Ler(saida[^1]).Situacao);
        Assert.Equal(texto, File.ReadAllText(c.DoRegistro("pedido.md")));
        Assert.Single(JsonNode.Parse(File.ReadAllText(c.DoRegistro("anexos.json")))!.AsArray());

        // O que o controlador falso recebeu: a linha exata (o anexo primeiro, depois o resto do processo), nada pela entrada, sem chave.
        var recebido = Json(c.Falso("recebido.json"));
        var esperado = LinhasDeComando.Analise62(Ambiente.AgenteFalso, false, c.Pecas.Analise62!, c.Processo.Pasta, [anexo, Planta(c)]);
        Assert.Equal(esperado.Args, recebido["argv"]!.AsArray().Select(x => x!.GetValue<string>()).ToList());
        Assert.Equal(c.Processo.Pasta, recebido["cwd"]!.GetValue<string>());
        Assert.Equal("", recebido["entrada"]!.GetValue<string>());
        Assert.Equal("CHAVE_AUSENTE", recebido["chave"]!.GetValue<string>());

        // execucao.json.
        var ex = c.Execucao();
        Assert.Equal("chatgpt", ex["provedor"]!.GetValue<string>());
        Assert.Equal("conta", ex["modo"]!.GetValue<string>());
        Assert.Equal("concluida", ex["situacao"]!.GetValue<string>());
        Assert.Equal(0, ex["codigo"]!.GetValue<int>());
        Assert.Equal(Ambiente.AgenteFalso, ex["programa"]!.GetValue<string>());
        Assert.Equal(Path.Combine(c.Processo.Pasta, ".pmvni-ai", "analise", "relatorio_dados.json"), ex["document"]!.GetValue<string>());
        Assert.True(DateTimeOffset.Parse(ex["fim"]!.GetValue<string>()) >= DateTimeOffset.Parse(ex["inicio"]!.GetValue<string>()));
        Assert.Equal(esperado.Args.ToArray(), ex["linha_de_comando"]!.AsArray().Skip(1).Select(x => x!.GetValue<string>()).ToArray());
        Assert.Equal(("openai", false), (ex["rota_codex_tecnica"]!.GetValue<string>(), ex["retomada"]!.GetValue<bool>()));
        Assert.False(File.Exists(c.DoRegistro("erros.txt")) && new FileInfo(c.DoRegistro("erros.txt")).Length > 0);

        // A trava foi solta.
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
    }

    [Fact]
    public async Task chatgpt_retomada_chama_o_controlador_sem_inputs()
    {
        using var c = new Cena("orq-chatgpt-retomada", "ok", Provedor.ChatGpt);
        Directory.CreateDirectory(c.Controle("analise"));
        File.WriteAllText(Path.Combine(c.Controle("analise"), "engine_state.json"), "{\"version\":1}");
        // O controle existe, mas este registro nunca falou com o kit (a primeira chamada de uma reanálise falhou antes): não retoma a análise anterior.
        using (var antes = new Cena("orq-chatgpt-retomada-alheia", "ok", Provedor.ChatGpt))
        {
            Directory.CreateDirectory(antes.Controle("analise"));
            File.WriteAllText(Path.Combine(antes.Controle("analise"), "engine_state.json"), "{\"version\":1}");
            Assert.False(OrquestradorAnalise.PodeRetomarChatGpt(antes.Processo.Pasta, antes.Registro.Pasta));
            // Uma FALHA ou NAO_PARTIU deste registro (ou uma linha que não é do contrato) não prova que o controle é dele: o kit pode ter falhado antes de criá-lo.
            antes.Registro.AcrescentarSituacao("{\"situacao\":\"FALHA\",\"etapa\":\"prepare\",\"retomar_em_utc\":null,\"motivo\":\"Controle existente.\",\"decisao\":null}");
            antes.Registro.AcrescentarSituacao("{\"situacao\":\"NAO_PARTIU\",\"etapa\":null,\"motivo\":\"x\"}");
            antes.Registro.AcrescentarSituacao("{\"situacao\":\"QUALQUER\"}");
            antes.Registro.Descarregar();
            Assert.False(OrquestradorAnalise.PodeRetomarChatGpt(antes.Processo.Pasta, antes.Registro.Pasta));
            File.Delete(Path.Combine(antes.Registro.Pasta, "situacoes.jsonl"));
            Assert.Equal("concluida", (await Ambiente.Com(antes.Orq().ExecutarAsync(antes.Pedido(Provedor.ChatGpt) with { Retomar = true }, antes.Progresso,
                CancellationToken.None), Limite)).Situacao);
            Assert.Contains("--inputs", Argv(Assert.Single(Chamadas(antes, "analise62"))));
        }
        // Este registro já recebeu uma situação do contrato: aí sim, retoma sem --inputs.
        c.Registro.AcrescentarSituacao("{\"situacao\":\"AGUARDANDO_COTA\",\"etapa\":\"analyze\",\"retomar_em_utc\":null,\"motivo\":\"\",\"decisao\":null}");
        c.Registro.Descarregar();
        Assert.True(OrquestradorAnalise.PodeRetomarChatGpt(c.Processo.Pasta, c.Registro.Pasta));
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt) with { Retomar = true }, c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("concluida", fim.Situacao);
        Assert.Equal(new[] { "-B", "-X", "utf8", c.Pecas.Analise62!, "--case", c.Processo.Pasta }, Argv(Assert.Single(Chamadas(c, "analise62"))));
        Assert.True(c.Execucao()["retomada"]!.GetValue<bool>());

        // Sem o controle do kit (a análise anterior nem começou), "retomar" começa do zero, com as peças.
        using var semControle = new Cena("orq-chatgpt-retomada-sem-controle", "ok", Provedor.ChatGpt);
        Assert.Equal("concluida", (await Ambiente.Com(semControle.Orq().ExecutarAsync(semControle.Pedido(Provedor.ChatGpt) with { Retomar = true }, semControle.Progresso,
            CancellationToken.None), Limite)).Situacao);
        Assert.Contains("--inputs", Argv(Assert.Single(Chamadas(semControle, "analise62"))));
    }

    [Fact]
    public async Task chatgpt_falha_e_sem_resultado()
    {
        using (var c = new Cena("orq-chatgpt-falha", "falha", Provedor.ChatGpt))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("falhou", fim.Situacao);
            Assert.Equal(2, fim.Codigo);
            Assert.Equal($"O controlador do analise parou na etapa Revisão final: falhou de propósito. Veja o registro: {c.Registro.Pasta}.", fim.Mensagem);
            Assert.Null(fim.Documento);
            Assert.Equal(new[] { "erro do agente falso" }, Linhas(c.DoRegistro("erros.txt")));
            Assert.Equal("falhou", c.Execucao()["situacao"]!.GetValue<string>());
        }

        // Controlador que morre sem a situação (o 3.0.1 imprimia "ANALISE_62_INTERROMPIDA: …"): a última linha vira o motivo.
        using (var c = new Cena("orq-chatgpt-sem-json", "falha", Provedor.ChatGpt, o =>
        {
            o["eventos"] = new JsonArray("ANALISE_62_INTERROMPIDA: Cota indisponível. Modelo x, rota openai, código 1.");
            o["sem_json"] = true;
            o["codigo"] = 1;
        }))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("falhou", fim.Situacao);
            Assert.StartsWith("O controlador do analise parou: ANALISE_62_INTERROMPIDA: Cota indisponível", fim.Mensagem);
            Assert.False(File.Exists(c.DoRegistro("situacoes.jsonl")));
        }

        using (var c = new Cena("orq-chatgpt-sem-resultado", "sem_resultado", Provedor.ChatGpt))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("sem_resultado", fim.Situacao);
            Assert.Equal($"A análise terminou, mas não deixou relatório novo (delivery.json não foi atualizado). Veja o registro: {c.Registro.Pasta}.", fim.Mensagem);
            Assert.Equal(0, fim.Codigo);
            Assert.Null(fim.Documento);
        }

        using (var c = new Cena("orq-chatgpt-json-invalido", "json_invalido", Provedor.ChatGpt))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("sem_resultado", fim.Situacao);
            Assert.StartsWith("O delivery.json da análise não pôde ser lido: ", fim.Mensagem);
        }

        using (var c = new Cena("orq-chatgpt-fora", "fora_do_processo", Provedor.ChatGpt))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("sem_resultado", fim.Situacao);
            Assert.StartsWith("O relatório indicado pela análise está fora da pasta do processo: ", fim.Mensagem);
            Assert.Null(fim.Documento);
        }
    }

    [Fact]
    public async Task cancelar_mata_a_arvore()
    {
        using var c = new Cena("orq-chatgpt-cancelar", "lento", Provedor.ChatGpt);
        using var cts = new CancellationTokenSource();
        var tarefa = c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, cts.Token);

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
            catch (Exception e) when (e is IOException or JsonException or NullReferenceException) { return false; }
        }, TimeSpan.FromSeconds(30)), "o agente falso não partiu");
        Assert.True(Ambiente.Vivo(pai));
        Assert.True(Ambiente.Vivo(filho));

        cts.Cancel();
        var relogio = Stopwatch.StartNew();
        var fim = await Ambiente.Com(tarefa, TimeSpan.FromSeconds(15));
        Assert.True(Ambiente.EsperarAte(() => !Ambiente.Vivo(pai) && !Ambiente.Vivo(filho), TimeSpan.FromSeconds(5)), "a árvore do agente continua viva");
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(5), "levou " + relogio.Elapsed);

        Assert.Equal("cancelada", fim.Situacao);
        Assert.Null(fim.Documento);
        Assert.False(File.Exists(Path.Combine(c.Controle("analise"), "relatorio_dados.json")));      // nada importado
        Assert.Equal("cancelada", c.Execucao()["situacao"]!.GetValue<string>());
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
    }

    [Fact]
    public async Task saida_grande_nao_trava()
    {
        const int N = 200_000;
        using var c = new Cena("orq-saida-grande", "ok", Provedor.ChatGpt, o => { o["linhas"] = N; o["eventos"] = new JsonArray(); o["intervalo_ms"] = 0; });
        var relogio = Stopwatch.StartNew();
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), TimeSpan.FromSeconds(120));
        relogio.Stop();

        Assert.Equal("concluida", fim.Situacao);
        Assert.Equal(N + 1, Linhas(c.DoRegistro("saida.jsonl")).Length);     // + a situação do contrato na última linha
        Assert.Equal(N + 1, c.Progresso.QuantidadeDeLinhas);                  // + a rota do Codex, na partida
        Assert.StartsWith("Rota do Codex: ", c.Progresso.Linhas()[0]);
        Assert.Equal("linha 1", c.Progresso.Linhas()[1]);
        Assert.Equal("linha " + N, c.Progresso.Linhas()[^1]);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(60), "levou " + relogio.Elapsed);
    }

    [Fact]
    public async Task chave_nunca_em_arquivo_gravado()
    {
        using var c = new Cena("orq-chave", "chave", Provedor.ChatGpt);
        var chave = "sk-teste-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        CofreChaves.Guardar(c.RaizDados, Provedor.ChatGpt, chave);

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt, ModoAcesso.Chave, "texto qualquer"), c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("concluida", fim.Situacao);

        // O filho recebeu a chave só pela variável do provedor (o falso informa o início do sha256, nunca a chave).
        var recebido = Json(c.Falso("recebido.json"));
        var sha8 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chave)))[..8];
        Assert.Equal("CHAVE_PRESENTE " + sha8, recebido["chave"]!.GetValue<string>());
        Assert.Equal("CODEX_API_KEY", recebido["variavel"]!.GetValue<string>());
        Assert.Equal("chave", c.Execucao()["modo"]!.GetValue<string>());

        // Nenhum arquivo gravado (menos o .dpapi) guarda a chave em UTF-8, UTF-16 ou base64.
        var arquivos = Directory.GetFiles(c.Pasta, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(arquivos);
        foreach (var f in arquivos)
            Assert.False(CofreChavesTestes.Contem(File.ReadAllBytes(f), chave), "a chave apareceu em " + f);
        Assert.True(File.Exists(Path.Combine(c.RaizDados, "segredos", "chatgpt.dpapi")));
    }

    [Fact]
    public async Task conta_nao_leva_chave_herdada_do_ambiente()
    {
        using var c = new Cena("orq-conta", "chave", Provedor.ChatGpt);
        using var a = new VariavelDeAmbiente("OPENAI_API_KEY", "sk-herdada-do-ambiente");
        using var b = new VariavelDeAmbiente("CODEX_API_KEY", "sk-herdada-2");
        using var d = new VariavelDeAmbiente("ANTHROPIC_API_KEY", "sk-herdada-3");
        CofreChaves.Guardar(c.RaizDados, Provedor.ChatGpt, "sk-guardada-mas-nao-usada");

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt, ModoAcesso.Conta), c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("concluida", fim.Situacao);
        Assert.Equal("CHAVE_AUSENTE", Json(c.Falso("recebido.json"))["chave"]!.GetValue<string>());
        Assert.Equal("conta", c.Execucao()["modo"]!.GetValue<string>());
    }

    [Fact]
    public async Task programas_iniciados_ficam_registrados_e_os_recusados_nao()
    {
        // O roteiro (Tarefa 14) lista no resultado os programas que de fato partiram; o que a trava recusa nunca entra na lista.
        var vistos = new List<string>();
        ExecucaoProcesso.AoIniciar = c => { lock (vistos) vistos.Add(c); };
        try
        {
            var (codigo, _) = await ExecucaoProcesso.RodarAsync(Ambiente.AgenteFalso, new[] { "--version" }, Ambiente.PastaTestes, _ => { }, TimeSpan.FromSeconds(30), CancellationToken.None);
            Assert.Equal(0, codigo);
            var real = Path.Combine(Environment.SystemDirectory, "whoami.exe");
            await Assert.ThrowsAsync<InvalidOperationException>(() => ExecucaoProcesso.RodarAsync(real, Array.Empty<string>(), Ambiente.PastaTestes, _ => { }, TimeSpan.FromSeconds(5), CancellationToken.None));
        }
        finally { ExecucaoProcesso.AoIniciar = null; }
        Assert.Equal(new[] { Path.GetFullPath(Ambiente.AgenteFalso) }, vistos);
    }

    [Fact]
    public async Task testes_nunca_iniciam_programa_real()
    {
        var real = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenAI", "Codex", "bin", "x", "codex.exe");
        Assert.Equal(Path.GetDirectoryName(Ambiente.AgenteFalso), ExecucaoProcesso.SomenteExecutaveisEm);
        // Observa apenas partidas feitas pelo código sob teste; processos externos podem encerrar.
        var iniciados = new List<string>();
        ExecucaoProcesso.AoIniciar = iniciados.Add;
        try
        {

        var e = Assert.Throws<InvalidOperationException>(() => ExecucaoProcesso.Iniciar(real, new[] { "--version" }, Ambiente.PastaTestes, null, _ => { }, _ => { }, _ => { }));
        Assert.Equal("Programa fora da pasta permitida: " + real, e.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ExecucaoProcesso.RodarAsync(real, new[] { "--version" }, Ambiente.PastaTestes, _ => { }, TimeSpan.FromSeconds(1), CancellationToken.None));
        // Nem com uma pasta de nome parecido, nem por "..".
        var parecido = Path.GetDirectoryName(Ambiente.AgenteFalso) + "-outro\\AgenteFalso.exe";
        Assert.Throws<InvalidOperationException>(() => ExecucaoProcesso.Iniciar(parecido, Array.Empty<string>(), Ambiente.PastaTestes, null, _ => { }, _ => { }, _ => { }));
        var subida = Path.Combine(Path.GetDirectoryName(Ambiente.AgenteFalso)!, "..", "..", "..", "..", "..", "Windows", "System32", "cmd.exe");
        Assert.Throws<InvalidOperationException>(() => ExecucaoProcesso.Iniciar(subida, new[] { "/c", "exit" }, Ambiente.PastaTestes, null, _ => { }, _ => { }, _ => { }));

        Assert.Empty(iniciados);
        }
        finally { ExecucaoProcesso.AoIniciar = null; }
    }

    static async Task<FimAnalise> RodarSemPecas(string nome, Provedor p, Func<Pecas, Pecas> mudar, ModoAcesso modo = ModoAcesso.Conta)
    {
        using var c = new Cena(nome, "ok", p);
        c.Pecas = mudar(c.Pecas);
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(p, modo), c.Progresso, CancellationToken.None), Limite);
        Assert.False(File.Exists(c.Falso("recebido.json")));          // nada foi iniciado
        Assert.False(File.Exists(c.Falso("chamadas.txt")));
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
        return fim;
    }

    [Fact]
    public async Task pedido_sem_as_pecas_falha_sem_iniciar_nada()
    {
        var f1 = await RodarSemPecas("orq-sem-codex", Provedor.ChatGpt, p => p with { Codex = null });
        Assert.Equal("falhou", f1.Situacao);
        Assert.Equal("O Codex não foi encontrado neste computador. Instale o app do ChatGPT/Codex ou escolha o programa em Ajustes › Inteligência artificial.", f1.Mensagem);
        Assert.Equal("O plugin analise do kit PMVNI não está habilitado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).",
            (await RodarSemPecas("orq-sem-plugin", Provedor.ChatGpt, p => p with { PluginAnaliseHabilitado = false })).Mensagem);
        Assert.Equal("Guarde a chave de API em Ajustes › Inteligência artificial ou use a sua conta.",
            (await RodarSemPecas("orq-sem-chave", Provedor.ChatGpt, p => p, ModoAcesso.Chave)).Mensagem);

        Assert.Equal("O Python não foi encontrado neste computador (o análise pro do kit precisa dele).",
            (await RodarSemPecas("orq-sem-python", Provedor.Claude, p => p with { Python = null })).Mensagem);
        Assert.Equal("A rota Claude do plugin analise do kit PMVNI não está instalada no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).",
            (await RodarSemPecas("orq-sem-pro", Provedor.Claude, p => p with { ProRuntime = null })).Mensagem);
        Assert.Equal("O Claude Code não foi encontrado neste computador (claude.exe).",
            (await RodarSemPecas("orq-sem-claude", Provedor.Claude, p => p with { Claude = null })).Mensagem);
        // 2026-11-17: o modo escolhido nunca é trocado. Análise pro que não aceita a API: o modo chave do Claude é recusado sem iniciar nada.
        var proibe = Ambiente.PecasFalsas(Path.GetTempPath());
        Assert.Equal(ModoAcesso.Chave, OrquestradorAnalise.ModoEfetivo(Provedor.Claude, ModoAcesso.Chave, proibe));
        Assert.Equal(ModoAcesso.Chave, OrquestradorAnalise.ModoEfetivo(Provedor.ChatGpt, ModoAcesso.Chave, proibe));
        Assert.Equal(OrquestradorAnalise.ApiNaoSuportada("4.0.0"),
            (await RodarSemPecas("orq-claude-chave-sem-api", Provedor.Claude, p => p, ModoAcesso.Chave)).Mensagem);
        // Se o kit passar a permitir API, o modo chave do Claude deixa de ser recusado por esta regra (a chave guardada ainda é exigida).
        Assert.Equal("Guarde a chave de API em Ajustes › Inteligência artificial ou use a sua conta.",
            (await RodarSemPecas("orq-claude-chave-liberada", Provedor.Claude, p => p with { ApiPermitidaNoKit = true }, ModoAcesso.Chave)).Mensagem);
    }

    [Fact]
    public async Task segunda_execucao_no_mesmo_processo_e_recusada()
    {
        using var c = new Cena("orq-trava", "ok", Provedor.ChatGpt);
        using var trava = TravaExecucao.Tentar(c.Processo.Pasta, Provedor.Claude);
        Assert.NotNull(trava);
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("falhou", fim.Situacao);
        Assert.Equal("Já há uma análise em andamento neste processo.", fim.Mensagem);
        Assert.False(File.Exists(c.Falso("recebido.json")));
        Assert.True(TravaExecucao.EmAndamento(c.Processo.Pasta));    // a trava de quem já estava continua
    }

    // ------------------------------------------------------------------ Claude (análise pro)

    static List<string[]> Chamadas(Cena c, string acao)
    {
        var arquivo = c.Falso("chamadas.txt");
        if (!File.Exists(arquivo)) return new();
        return File.ReadAllLines(arquivo, Encoding.UTF8).Select(l => l.Split('\t')).Where(p => p[0] == acao).ToList();
    }

    [Fact]
    public async Task claude_modo_api_entrega_chave_e_rota_so_na_partida()
    {
        // 2026-11-17 (análise pro 1.6.4): modo chave = Anthropic API de fato. A chave e a rota "api" vão só ao "iniciar" (o kit as passa ao processo
        // isolado da análise); o acompanhamento ("situacao") nunca recebe a chave e declara "conta".
        using var c = new Cena("orq-claude-api", "claude_ok", Provedor.Claude);
        c.Pecas = c.Pecas with { ApiPermitidaNoKit = true };
        using var herdada = new VariavelDeAmbiente("ANTHROPIC_API_KEY", "sk-ant-herdada-nao-usar");
        using var rota = new VariavelDeAmbiente("PMVNI_ANALISE_PRO_ROTA", "conta");
        var chave = "sk-ant-teste-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
        CofreChaves.Guardar(c.RaizDados, Provedor.Claude, chave);

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude, ModoAcesso.Chave), c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("concluida", fim.Situacao);

        var sha8 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chave)))[..8];
        var iniciar = Assert.Single(Chamadas(c, "iniciar"));
        Assert.Equal(("CHAVE_PRESENTE " + sha8, "api"), (iniciar[2], iniciar[3]));
        Assert.All(Chamadas(c, "situacao"), s => Assert.Equal(("CHAVE_AUSENTE", "conta"), (s[2], s[3])));
        var ex = c.Execucao();
        Assert.Equal(("chave", "anthropic_api"), (ex["modo"]!.GetValue<string>(), ex["autenticacao"]!.GetValue<string>()));

        foreach (var f in Directory.GetFiles(c.Pasta, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)))
            Assert.False(CofreChavesTestes.Contem(File.ReadAllBytes(f), chave), "a chave apareceu em " + f);
    }

    [Fact]
    public async Task claude_modo_conta_nunca_leva_chave_nem_rota_api()
    {
        using var c = new Cena("orq-claude-conta", "claude_ok", Provedor.Claude);
        c.Pecas = c.Pecas with { ApiPermitidaNoKit = true };
        using var herdada = new VariavelDeAmbiente("ANTHROPIC_API_KEY", "sk-ant-herdada-nao-usar");
        using var rota = new VariavelDeAmbiente("PMVNI_ANALISE_PRO_ROTA", "api");
        CofreChaves.Guardar(c.RaizDados, Provedor.Claude, "sk-ant-guardada-mas-nao-usada");

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude, ModoAcesso.Conta), c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("concluida", fim.Situacao);
        var iniciar = Assert.Single(Chamadas(c, "iniciar"));
        Assert.Equal(("CHAVE_AUSENTE", "conta"), (iniciar[2], iniciar[3]));
        Assert.Equal("conta_claude", c.Execucao()["autenticacao"]!.GetValue<string>());
    }

    [Fact]
    public async Task claude_iniciar_acompanhar_e_resultado()
    {
        using var c = new Cena("orq-claude-ok", "claude_ok", Provedor.Claude);
        const string texto = "Pedido escrito que o kit automático não recebe.";
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude, texto: texto), c.Progresso, CancellationToken.None), Limite);

        Assert.Equal("concluida", fim.Situacao);
        Assert.Equal(Path.Combine(c.Controle("analise-pro"), "relatorio_dados.json"), fim.Documento);
        Assert.Equal(Path.Combine(c.Controle("analise-pro"), "relatorio.pdf"), fim.Pdf);

        // "iniciar" uma vez, com a linha exata; "situacao" repetido; um só "auto".
        var iniciar = Assert.Single(Chamadas(c, "iniciar"));
        var esperada = LinhasDeComando.ProRuntime(Ambiente.AgenteFalso, false, c.Pecas.ProRuntime!, c.Processo.Pasta, "iniciar").Args;
        Assert.Equal(esperada, JsonSerializer.Deserialize<string[]>(iniciar[1]));
        Assert.True(Chamadas(c, "situacao").Count >= 3);
        Assert.Single(Chamadas(c, "auto"));

        // Progresso: passa por todas as etapas do kit; as linhas do auto-log.txt chegam. A etapa de cada leitura é uma amostra (com a suíte inteira, a
        // primeira leitura pode chegar depois de "prepare"): a prova é a etapa lida OU o evento "Etapa X" que o próprio kit registrou e o editor mostrou.
        var etapas = c.Progresso.Todos().Where(p => p.Situacao?.Etapa is not null).Select(p => p.Situacao!.Etapa!)
            .Concat(c.Progresso.Todos().SelectMany(p => p.Situacao?.Eventos ?? []).Where(e => e.StartsWith("Etapa ", StringComparison.Ordinal)).Select(e => e[6..]))
            .Distinct().ToList();
        foreach (var etapa in SituacaoAnalisePro.Etapas) Assert.Contains(etapa, etapas);
        Assert.Contains(c.Progresso.Todos(), p => p.Situacao?.Codigo == "CONCLUIDO");
        var linhas = c.Progresso.Linhas();
        foreach (var etapa in SituacaoAnalisePro.Etapas) Assert.Contains(linhas, l => l.EndsWith("etapa " + etapa, StringComparison.Ordinal));
        Assert.Equal(linhas.Count, linhas.Distinct().Count());           // cada linha do log chega uma vez só

        // Registro.
        Assert.Equal(texto, File.ReadAllText(c.DoRegistro("pedido.md")));
        var situacoes = Linhas(c.DoRegistro("situacoes.jsonl"));
        Assert.True(situacoes.Length >= 6);
        Assert.All(situacoes, s => JsonNode.Parse(s));
        Assert.Equal(situacoes.Length, situacoes.Distinct().Count());     // só quando muda
        Assert.Equal("CONCLUIDO", SituacaoAnalisePro.Ler(situacoes[^1]).Codigo);
        var ex = c.Execucao();
        Assert.Equal("claude", ex["provedor"]!.GetValue<string>());
        Assert.Equal("concluida", ex["situacao"]!.GetValue<string>());
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
    }

    [Fact]
    public async Task claude_ja_em_andamento()
    {
        using var c = new Cena("orq-claude-ja", "ja_em_andamento", Provedor.Claude);
        var (exe, args) = LinhasDeComando.ProRuntime(Ambiente.AgenteFalso, false, c.Pecas.ProRuntime!, c.Processo.Pasta, "iniciar");
        // Uma análise já está rodando (partida de antes, por fora do orquestrador).
        var antes = await ExecucaoProcesso.RodarAsync(exe, args, c.Processo.Pasta, env => env["AGENTE_FALSO_CENARIO"] = c.Cenario, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Equal(0, antes.Codigo);
        Assert.Contains("INICIADA", antes.Saida);
        Assert.True(Ambiente.EsperarAte(() =>
        {
            try { return SituacaoAnalisePro.Ler(File.ReadAllText(Path.Combine(c.Controle("analise-pro"), "auto-status.json"))).Codigo == "EM_ANDAMENTO"; }
            catch (IOException) { return false; }
        }, TimeSpan.FromSeconds(60)));

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude), c.Progresso, CancellationToken.None), Limite);

        Assert.Equal("concluida", fim.Situacao);
        Assert.Equal(2, Chamadas(c, "iniciar").Count);       // a de antes e a do orquestrador, que recebeu JA_EM_ANDAMENTO…
        Assert.Single(Chamadas(c, "auto"));                  // …e não partiu uma segunda análise
    }

    [Fact]
    public async Task claude_parada_do_kit_objecoes_e_cota()
    {
        using (var c = new Cena("orq-claude-objecoes", "objecoes", Provedor.Claude))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("falhou", fim.Situacao);
            Assert.Equal("parou com objeções da revisão final: A revisão final apontou objeções ao relatório.", fim.Mensagem);
            Assert.Null(fim.Documento);
            Assert.Equal("falhou", c.Execucao()["situacao"]!.GetValue<string>());
        }
        using (var c = new Cena("orq-claude-cota", "pausado_cota", Provedor.Claude))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude), c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("interrompida", fim.Situacao);
            Assert.Equal("pausada: cota do Claude esgotada: Cota do Claude esgotada.", fim.Mensagem);
            Assert.Equal("interrompida", c.Execucao()["situacao"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task cancelar_claude_encerra_o_pid_do_kit()
    {
        using var c = new Cena("orq-claude-cancelar", "claude_lento", Provedor.Claude);
        var orq = c.Orq();
        using var cts = new CancellationTokenSource();
        var seguindo = orq.ExecutarAsync(c.Pedido(Provedor.Claude), c.Progresso, cts.Token);

        var status = Path.Combine(c.Controle("analise-pro"), "auto-status.json");
        int pid = 0;
        Assert.True(Ambiente.EsperarAte(() =>
        {
            try
            {
                var s = SituacaoAnalisePro.Ler(File.ReadAllText(status));
                if (s.Codigo != "EM_ANDAMENTO" || s.Etapa != "analyze" || s.Pid is null) return false;
                pid = s.Pid.Value;
                return true;
            }
            catch (IOException) { return false; }
        }, TimeSpan.FromSeconds(60)), "a análise do kit não chegou à etapa analyze" + Diagnostico(c));
        Assert.True(Ambiente.Vivo(pid));

        // Um pid de outro programa (o próprio processo de teste) é recusado e continua vivo.
        var outro = Ambiente.NovoProcesso(Path.Combine(c.Pasta, "x")).Pasta;
        Directory.CreateDirectory(Path.Combine(outro, ".pmvni-ai", "analise-pro"));
        File.WriteAllText(Path.Combine(outro, ".pmvni-ai", "analise-pro", "auto-status.json"),
            new JsonObject { ["situacao"] = "EM_ANDAMENTO", ["pid"] = Environment.ProcessId, ["inicio"] = DateTime.Now.AddMinutes(-1).ToString("yyyy-MM-ddTHH:mm:ss") }.ToJsonString());
        Assert.False(await Ambiente.Com(orq.CancelarClaudeAsync(outro, c.Pecas), TimeSpan.FromSeconds(30)));
        Assert.True(Ambiente.Vivo(Environment.ProcessId));

        // O pid do kit é encerrado em até 5 s.
        var relogio = Stopwatch.StartNew();
        Assert.True(await Ambiente.Com(orq.CancelarClaudeAsync(c.Processo.Pasta, c.Pecas), TimeSpan.FromSeconds(30)));
        Assert.True(Ambiente.EsperarAte(() => !Ambiente.Vivo(pid), TimeSpan.FromSeconds(5)), "o pid do kit continua vivo");
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(10));

        // O kit passa a "interrompida" e o acompanhamento termina assim.
        var fim = await Ambiente.Com(seguindo, TimeSpan.FromSeconds(30));
        Assert.Equal("interrompida", fim.Situacao);
        Assert.Equal("INTERROMPIDA", c.Progresso.Todos().Last(p => p.Situacao is not null).Situacao!.Codigo);

        // Sem análise em andamento, não há o que cancelar.
        Assert.False(await Ambiente.Com(orq.CancelarClaudeAsync(c.Processo.Pasta, c.Pecas), TimeSpan.FromSeconds(30)));
    }

    [Fact]
    public async Task claude_parar_de_acompanhar_nao_encerra_a_analise_do_kit()
    {
        using var c = new Cena("orq-claude-parar", "claude_lento", Provedor.Claude);
        using var cts = new CancellationTokenSource();
        var seguindo = c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude), c.Progresso, cts.Token);
        var status = Path.Combine(c.Controle("analise-pro"), "auto-status.json");
        int pid = 0;
        Assert.True(Ambiente.EsperarAte(() =>
        {
            try { var s = SituacaoAnalisePro.Ler(File.ReadAllText(status)); pid = s.Pid ?? 0; return s.Codigo == "EM_ANDAMENTO" && pid != 0; }
            catch (IOException) { return false; }
        }, TimeSpan.FromSeconds(60)), "a análise do kit não passou a EM_ANDAMENTO" + Diagnostico(c));

        cts.Cancel();
        var fim = await Ambiente.Com(seguindo, TimeSpan.FromSeconds(15));
        Assert.Equal("acompanhamento_interrompido", fim.Situacao);
        Assert.Equal("acompanhamento_interrompido", c.Execucao()["situacao"]!.GetValue<string>());      // nunca "cancelada": a análise do kit segue
        Assert.True(Ambiente.Vivo(pid), "a análise do kit roda sozinha: parar de acompanhar não a encerra");
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
        Process.GetProcessById(pid).Kill(true);
    }
}
