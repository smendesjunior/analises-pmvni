using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>O fluxo do Gemini de ponta a ponta no editor (conferência, partida, acompanhamento, resultado, pausa, retomada, cancelamento), sempre contra
/// o agente falso, que faz o papel do <c>google_runtime.py</c>. SIMULADO: não prova o plugin de verdade nem o Google; prova o que o editor entrega ao
/// plugin (modo, perfil, chave só onde deve) e o que ele faz com cada resposta.</summary>
public partial class OrquestradorTestes
{
    static Pecas ComGemini(Cena c) => c.Pecas with
    {
        Agy = Ambiente.AgenteFalso,
        GoogleRuntime = Path.Combine(c.Pasta, "codex-home", "plugins", "cache", "pmvni-kit", "analise-google", "2.1.0", "skills", "analise-google", "scripts", "google_runtime.py"),
        VersaoAnaliseGoogle = "2.1.0", VersaoAnaliseGoogleKit = "2.1.0",
    };

    static Cena CenaGemini(string nome, Action<JsonObject>? ajustar = null)
    {
        var c = new Cena(nome, "claude_ok", Provedor.Gemini, ajustar ?? (_ => { }));
        c.Pecas = ComGemini(c);
        return c;
    }

    static PedidoExecucao PedidoGemini(Cena c, PerfilGemini? perfil, bool ficticio = false, string? texto = null) =>
        c.Pedido(Provedor.Gemini, perfil is { } p ? MatrizGemini.Modo(p) : ModoAcesso.Chave, texto) with { Perfil = perfil, Ficticio = ficticio };

    static string StatusGoogle(Cena c) => Path.Combine(c.Controle("analise-google"), "auto-status.json");

    void MatarGoogle(Cena c)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(StatusGoogle(c)))?["pid"] is JsonValue v && v.TryGetValue<int>(out var pid) && Ambiente.Vivo(pid))
            {
                using var processo = Process.GetProcessById(pid);
                if (processo.ProcessName == "AgenteFalso") processo.Kill(true);
            }
        }
        catch (Exception e) when (e is IOException or JsonException or ArgumentException or InvalidOperationException) { }
    }

    [Fact]
    public async Task gemini_conta_confere_parte_acompanha_e_entrega()
    {
        using var c = CenaGemini("orq-gemini-conta");
        using var herdada = new VariavelDeAmbiente("GEMINI_API_KEY", "herdada-nao-usar");
        using var herdada2 = new VariavelDeAmbiente("GOOGLE_API_KEY", "herdada-nao-usar");
        CofreChaves.Guardar(c.RaizDados, Provedor.Gemini, "gemini-ficticia-guardada-mas-nao-usada");
        const string texto = "Conferir as pendências do despacho anterior (fictício).";

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(PedidoGemini(c, PerfilGemini.ContaUltra, texto: texto), c.Progresso, CancellationToken.None), Limite);

        Assert.Equal("concluida", fim.Situacao);
        Assert.Equal(Path.Combine(c.Controle("analise-google"), "relatorio_dados.json"), fim.Documento);
        Assert.Equal(Path.Combine(c.Controle("analise-google"), "relatorio.pdf"), fim.Pdf);
        Assert.False(Directory.Exists(c.Controle("analise-pro")));                         // nada do Claude foi tocado

        // A conferência vem antes da partida, as duas com a rota e o perfil e sem chave; a situação só lê (sem rota, sem perfil, sem chave).
        var acoes = File.ReadAllLines(c.Falso("chamadas.txt"), Encoding.UTF8).Select(l => l.Split('\t')[0]).ToList();
        Assert.Equal("conferir", acoes[0]);
        Assert.Equal("iniciar", acoes[1]);
        var conferir = Assert.Single(Chamadas(c, "conferir"));
        var iniciar = Assert.Single(Chamadas(c, "iniciar"));
        foreach (var chamada in new[] { conferir, iniciar })
            Assert.Equal(("CHAVE_AUSENTE", "conta", "conta_ultra", "-"), (chamada[2], chamada[3], chamada[4], chamada[5]));
        Assert.True(Chamadas(c, "situacao").Count >= 3);
        Assert.All(Chamadas(c, "situacao"), s => Assert.Equal(("CHAVE_AUSENTE", "-", "-"), (s[2], s[3], s[4])));
        Assert.Single(Chamadas(c, "auto"));
        var esperada = LinhasDeComando.ProRuntime(Ambiente.AgenteFalso, false, c.Pecas.GoogleRuntime!, c.Processo.Pasta, "iniciar").Args;
        Assert.Equal(esperada, JsonSerializer.Deserialize<string[]>(iniciar[1]));

        // As seis etapas passam pela tela, na ordem do kit; o pedido complementar vira a diretriz do plugin.
        var etapas = c.Progresso.Todos().SelectMany(p => p.Situacao?.Eventos ?? []).Where(e => e.StartsWith("Etapa ", StringComparison.Ordinal)).Select(e => e[6..]).Distinct().ToList();
        foreach (var etapa in SituacaoAnalisePro.Etapas) Assert.Contains(etapa, etapas);
        Assert.Equal(SituacaoAnalisePro.Etapas, File.ReadAllLines(c.Falso("etapas.txt"), Encoding.UTF8));
        Assert.Equal(texto + "\n" + c.Registro.InstrucoesProgresso(), File.ReadAllText(Path.Combine(c.Controle("analise-google"), "diretrizes.md")));

        // Registro: provedor, modo, autenticação e perfil (nunca a chave), o plugin e a versão, e a conferência.
        var ex = c.Execucao();
        Assert.Equal(("gemini", "conta", "conta_google", "conta_ultra"),
            (ex["provedor"]!.GetValue<string>(), ex["modo"]!.GetValue<string>(), ex["autenticacao"]!.GetValue<string>(), ex["perfil"]!.GetValue<string>()));
        Assert.Equal(("análise Google", "2.1.0", "concluida"), (ex["plugin"]!.GetValue<string>(), ex["versao_do_plugin"]!.GetValue<string>(), ex["situacao"]!.GetValue<string>()));
        Assert.Contains("\"ok\":true", ex["conferencia_gemini"]!.GetValue<string>());
        Assert.EndsWith("-gemini", Path.GetFileName(c.Registro.Pasta));
        Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
    }

    [Theory]
    [InlineData(PerfilGemini.ApiCreditos, "api_creditos", "-")]
    [InlineData(PerfilGemini.ApiGratuita, "api_gratuita", "1")]
    public async Task gemini_api_entrega_a_chave_so_na_conferencia_e_na_partida(PerfilGemini perfil, string codigo, string ficticio)
    {
        using var c = CenaGemini("orq-gemini-" + codigo);
        using var herdada = new VariavelDeAmbiente("GEMINI_API_KEY", "herdada-nao-usar");
        var chave = "gemini-ficticia-teste-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(20));
        CofreChaves.Guardar(c.RaizDados, Provedor.Gemini, chave);

        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(PedidoGemini(c, perfil, ficticio: true), c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("concluida", fim.Situacao);

        var sha8 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chave)))[..8];
        foreach (var chamada in new[] { Assert.Single(Chamadas(c, "conferir")), Assert.Single(Chamadas(c, "iniciar")) })
            Assert.Equal(("CHAVE_PRESENTE " + sha8, "api", codigo, ficticio), (chamada[2], chamada[3], chamada[4], chamada[5]));
        Assert.All(Chamadas(c, "situacao"), s => Assert.Equal(("CHAVE_AUSENTE", "-", "-"), (s[2], s[3], s[4])));
        var ex = c.Execucao();
        Assert.Equal(("chave", "gemini_api", codigo), (ex["modo"]!.GetValue<string>(), ex["autenticacao"]!.GetValue<string>(), ex["perfil"]!.GetValue<string>()));
        Assert.Equal(SituacaoAnalisePro.Etapas, File.ReadAllLines(c.Falso("etapas.txt"), Encoding.UTF8));

        // A chave não aparece em argumento, log, registro, status nem em qualquer arquivo do processo (só no cofre protegido).
        foreach (var f in Directory.GetFiles(c.Pasta, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)))
            Assert.False(CofreChavesTestes.Contem(File.ReadAllBytes(f), chave), "a chave apareceu em " + f);
    }

    [Fact]
    public async Task gemini_recusas_antes_da_partida_nao_iniciam_nada()
    {
        async Task<FimAnalise> Rodar(Cena c, PedidoExecucao pedido) => await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);
        void NadaPartiu(Cena c) => Assert.False(File.Exists(c.Falso("chamadas.txt")), "o plugin foi chamado");

        // Modo chave sem perfil escolhido: o plano da chave não é presumido.
        using (var c = CenaGemini("orq-gemini-sem-perfil"))
        {
            CofreChaves.Guardar(c.RaizDados, Provedor.Gemini, "gemini-ficticia-ficticia");
            var fim = await Rodar(c, PedidoGemini(c, null));
            Assert.Equal(("falhou", OrquestradorAnalise.SemPerfilApi), (fim.Situacao, fim.Mensagem));
            NadaPartiu(c);
        }
        // Perfil de API sem chave guardada (falha de autenticação local): pede a chave, não usa a conta.
        using (var c = CenaGemini("orq-gemini-sem-chave"))
        {
            var fim = await Rodar(c, PedidoGemini(c, PerfilGemini.ApiCreditos));
            Assert.Equal(("falhou", "Guarde a chave de API em Ajustes › Inteligência artificial ou use a sua conta."), (fim.Situacao, fim.Mensagem));
            NadaPartiu(c);
        }
        // Perfil gratuito sem a declaração de processo fictício.
        using (var c = CenaGemini("orq-gemini-gratuita-real"))
        {
            CofreChaves.Guardar(c.RaizDados, Provedor.Gemini, "gemini-ficticia-ficticia");
            var fim = await Rodar(c, PedidoGemini(c, PerfilGemini.ApiGratuita, ficticio: false));
            Assert.Equal("falhou", fim.Situacao);
            Assert.Contains("só roda testes com dados fictícios", fim.Mensagem);
            NadaPartiu(c);
        }
        // Perfil e modo em desacordo: nunca troca de modo sozinho.
        using (var c = CenaGemini("orq-gemini-incoerente"))
        {
            var fim = await Rodar(c, c.Pedido(Provedor.Gemini, ModoAcesso.Conta) with { Perfil = PerfilGemini.ApiCreditos });
            Assert.Equal("falhou", fim.Situacao);
            Assert.Contains("não corresponde ao modo escolhido", fim.Mensagem);
            NadaPartiu(c);
        }
        // Peças: Python, plugin e Antigravity, nessa ordem.
        using (var c = CenaGemini("orq-gemini-pecas"))
        {
            c.Pecas = c.Pecas with { Agy = null };
            Assert.Equal(OrquestradorAnalise.SemAgy, (await Rodar(c, PedidoGemini(c, PerfilGemini.ContaUltra))).Mensagem);
            c.Pecas = c.Pecas with { GoogleRuntime = null };
            Assert.Equal(OrquestradorAnalise.SemAnaliseGoogle, (await Rodar(c, PedidoGemini(c, PerfilGemini.ContaUltra))).Mensagem);
            c.Pecas = c.Pecas with { Python = null };
            Assert.Equal("O Python não foi encontrado neste computador (o análise Google do kit precisa dele).", (await Rodar(c, PedidoGemini(c, PerfilGemini.ContaUltra))).Mensagem);
            NadaPartiu(c);
        }
    }

    [Fact]
    public async Task gemini_modelo_indisponivel_na_conferencia_diz_perfil_e_etapa_e_nao_parte()
    {
        using var c = CenaGemini("orq-gemini-modelo", o => o["conferir"] = new JsonObject
        {
            ["codigo"] = 2,
            ["saida"] = new JsonObject
            {
                ["ok"] = false,
                ["faltas"] = new JsonArray("perfil conta_ultra, etapa audit: modelo gemini-4-argon-high indisponível nesta conta."),
            },
        });
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(PedidoGemini(c, PerfilGemini.ContaUltra), c.Progresso, CancellationToken.None), Limite);

        Assert.Equal("falhou", fim.Situacao);
        Assert.Equal("O Gemini não está pronto para a análise (Conta Google (plano Ultra)): perfil conta_ultra, etapa audit: modelo gemini-4-argon-high indisponível nesta conta. "
            + "Nada foi iniciado e nenhum outro modo, modelo ou perfil foi usado.", fim.Mensagem);
        Assert.Single(Chamadas(c, "conferir"));
        Assert.Empty(Chamadas(c, "iniciar"));                 // nada partiu, nada foi gasto
        Assert.Empty(Chamadas(c, "auto"));
        Assert.False(File.Exists(StatusGoogle(c)));
        Assert.Equal("falhou", c.Execucao()["situacao"]!.GetValue<string>());
    }

    [Fact]
    public void mensagem_da_conferencia_sem_json_usa_a_primeira_linha()
    {
        Assert.Equal("O análise Google recusou a partida (API gratuita (só testes fictícios)): Perfil gratuito exige processo fictício.",
            OrquestradorAnalise.MensagemDaConferencia("Perfil gratuito exige processo fictício.\n", PerfilGemini.ApiGratuita));
        Assert.Equal("O Gemini não está pronto para a análise (API com créditos (Google AI Studio)): chave recusada; etapa analyze sem modelo.",
            OrquestradorAnalise.MensagemDaConferencia("aviso\n{\"ok\":false,\"faltas\":[\"chave recusada.\",\"etapa analyze sem modelo\"]}", PerfilGemini.ApiCreditos));
    }

    [Theory]
    [InlineData("cota_conta", "PAUSADO_COTA", "interrompida", "Cota da conta Google esgotada na etapa audit; nenhum outro modelo foi usado.")]
    [InlineData("cota_api", "PAUSADO_COTA", "interrompida", "Créditos da Gemini API esgotados na etapa audit.")]
    [InlineData("modelo_indisponivel", "PAUSADO_COTA", "interrompida", "Modelo gemini-4-argon-high indisponível na etapa audit.")]
    [InlineData("autenticacao_api", "FALHOU", "falhou", "Chave da Gemini API recusada na etapa audit.")]
    [InlineData("validacao", "FALHOU", "falhou", "ANALYSIS_AUDIT_OK falhou na etapa audit: manifesto sem a seção 12.")]
    public async Task gemini_parada_na_etapa_preserva_o_ponto_e_retoma_da_etapa_pendente(string motivo, string situacaoKit, string situacao, string erro)
    {
        using var c = CenaGemini("orq-gemini-" + motivo, o => o["auto"] = new JsonObject
        {
            ["etapa_ms"] = 150, ["final"] = "CONCLUIDO", ["pausar_na_etapa"] = "audit", ["pausa_situacao"] = situacaoKit, ["motivo"] = motivo, ["erro"] = erro,
        });
        var perfil = motivo.EndsWith("_api", StringComparison.Ordinal) ? PerfilGemini.ApiCreditos : PerfilGemini.ContaUltra;
        if (perfil == PerfilGemini.ApiCreditos) CofreChaves.Guardar(c.RaizDados, Provedor.Gemini, "gemini-ficticia-ficticia-de-teste");

        // 1ª partida: para na auditoria cruzada, com o motivo do plugin à vista e as quatro etapas anteriores guardadas.
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(PedidoGemini(c, perfil), c.Progresso, CancellationToken.None), Limite);
        Assert.Equal(situacao, fim.Situacao);
        Assert.EndsWith(erro, fim.Mensagem);
        Assert.Null(fim.Documento);
        Assert.Equal(new[] { "prepare", "analyze", "technical_audit", "produce" }, File.ReadAllLines(c.Falso("etapas.txt"), Encoding.UTF8));
        Assert.Equal("audit", c.Progresso.Todos().Last(p => p.Situacao is not null).Situacao!.Etapa);       // a etapa afetada é a que a tela mostra
        Assert.Equal(situacao, c.Execucao()["situacao"]!.GetValue<string>());

        // O perfil ficou fixado na análise: a retomada usa o mesmo, qualquer que seja o de Ajustes.
        Assert.Equal(perfil, GeminiFixado.Ler(c.Processo.Pasta));

        // 2ª partida (Continuar a análise): só as etapas pendentes rodam; nenhuma concluída se repete (sem custo repetido).
        using var registro2 = RegistroExecucao.Criar(c.Processo.Pasta, Provedor.Gemini, new DateTime(2026, 11, 15, 11, 0, 0));
        var retomada = PedidoGemini(c, GeminiFixado.Ler(c.Processo.Pasta)) with { Registro = registro2 };
        var fim2 = await Ambiente.Com(c.Orq().ExecutarAsync(retomada, new Coletor(), CancellationToken.None), Limite);
        Assert.Equal("concluida", fim2.Situacao);
        Assert.Equal(SituacaoAnalisePro.Etapas, File.ReadAllLines(c.Falso("etapas.txt"), Encoding.UTF8));     // cada etapa uma vez, na ordem
        Assert.Equal(2, Chamadas(c, "iniciar").Count);
        Assert.All(Chamadas(c, "iniciar"), i => Assert.Equal(MatrizGemini.Codigo(perfil), i[4]));
        Assert.Null(GeminiFixado.Ler(c.Processo.Pasta));                                                       // concluída: nada mais fixado
    }

    [Fact]
    public async Task gemini_cancelar_encerra_o_processo_do_kit_e_parar_de_acompanhar_nao()
    {
        using var c = CenaGemini("orq-gemini-cancelar", o => o["auto"] = new JsonObject { ["etapa_ms"] = 200, ["parar_na_etapa"] = "analyze", ["dormir_ms"] = 60000, ["final"] = "CONCLUIDO" });
        try
        {
            var orq = c.Orq();
            using var cts = new CancellationTokenSource();
            var seguindo = orq.ExecutarAsync(PedidoGemini(c, PerfilGemini.ContaUltra), c.Progresso, cts.Token);
            var pid = 0;
            Assert.True(Ambiente.EsperarAte(() =>
            {
                try
                {
                    var s = SituacaoAnalisePro.Ler(File.ReadAllText(StatusGoogle(c)));
                    if (s.Codigo != "EM_ANDAMENTO" || s.Etapa != "analyze" || s.Pid is null) return false;
                    pid = s.Pid.Value;
                    return true;
                }
                catch (IOException) { return false; }
            }, TimeSpan.FromSeconds(60)), "a análise do Gemini não chegou à etapa analyze");

            // Cancelar pelo provedor errado não acha nada (as pastas de controle são separadas) e não mata ninguém.
            Assert.False(await Ambiente.Com(orq.CancelarClaudeAsync(c.Processo.Pasta, c.Pecas), TimeSpan.FromSeconds(30)));
            Assert.True(Ambiente.Vivo(pid));

            Assert.True(await Ambiente.Com(orq.CancelarClaudeAsync(c.Processo.Pasta, c.Pecas, Provedor.Gemini), TimeSpan.FromSeconds(30)));
            Assert.True(Ambiente.EsperarAte(() => !Ambiente.Vivo(pid), TimeSpan.FromSeconds(5)), "o processo do kit continua vivo");
            var fim = await Ambiente.Com(seguindo, TimeSpan.FromSeconds(30));
            Assert.Equal("interrompida", fim.Situacao);
            Assert.Equal(new[] { "prepare" }, File.ReadAllLines(c.Falso("etapas.txt"), Encoding.UTF8));     // o que estava pronto fica guardado
            Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));

            // A situação lida pelo provedor certo.
            var s = await orq.SituacaoClaudeAsync(c.Processo.Pasta, c.Pecas, Provedor.Gemini);
            Assert.Equal("INTERROMPIDA", s!.Codigo);
            Assert.Null(await orq.SituacaoClaudeAsync(c.Processo.Pasta, c.Pecas with { GoogleRuntime = null }, Provedor.Gemini));
        }
        finally { MatarGoogle(c); }
    }

    [Fact]
    public async Task gemini_parar_de_acompanhar_deixa_a_analise_seguir_e_o_editor_volta_a_acompanhar()
    {
        using var c = CenaGemini("orq-gemini-seguir", o => o["auto"] = new JsonObject { ["etapa_ms"] = 700, ["final"] = "CONCLUIDO" });
        try
        {
            var inicio = DateTimeOffset.UtcNow;
            using var cts = new CancellationTokenSource();
            var seguindo = c.Orq().ExecutarAsync(PedidoGemini(c, PerfilGemini.ContaUltra), c.Progresso, cts.Token);
            Assert.True(Ambiente.EsperarAte(() =>
            {
                try { return SituacaoAnalisePro.Ler(File.ReadAllText(StatusGoogle(c))).Codigo == "EM_ANDAMENTO"; }
                catch (IOException) { return false; }
            }, TimeSpan.FromSeconds(60)));
            cts.Cancel();
            var fim = await Ambiente.Com(seguindo, TimeSpan.FromSeconds(15));
            Assert.Equal("acompanhamento_interrompido", fim.Situacao);          // nunca "cancelada": o kit segue sozinho

            // O editor reaberto volta a acompanhar pelo registro (sem outra partida) e recebe o relatório.
            var pedido = c.Pedido(Provedor.Gemini);
            var fim2 = await Ambiente.Com(c.Orq().AcompanharClaudeAsync(pedido, inicio, new Coletor(), CancellationToken.None), Limite);
            Assert.Equal("concluida", fim2.Situacao);
            Assert.Equal(Path.Combine(c.Controle("analise-google"), "relatorio_dados.json"), fim2.Documento);
            Assert.Single(Chamadas(c, "iniciar"));
            Assert.Single(Chamadas(c, "auto"));
        }
        finally { MatarGoogle(c); }
    }
}
