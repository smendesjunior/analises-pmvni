using System.Net;
using System.Text;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>O terceiro provedor (Gemini, pelo análise Google do kit): a matriz tipada de modelos e níveis, o ambiente do processo filho, a leitura das peças
/// e o "Testar". Tudo simulado ou automatizado: nenhum teste aqui fala com o Google nem roda o Antigravity de verdade.</summary>
[Collection("Ambiente")]
public class GeminiTestes
{
    static readonly string[] Etapas = { "prepare", "analyze", "technical_audit", "produce", "audit", "final_review" };

    [Fact]
    public void provedor_gemini_tem_nome_sufixo_variavel_e_roda_pelo_kit()
    {
        Assert.Equal(("Gemini", "gemini", "GEMINI_API_KEY"), (Provedores.Nome(Provedor.Gemini), Provedores.Sufixo(Provedor.Gemini), Provedores.VariavelChave(Provedor.Gemini)));
        Assert.True(Provedores.PeloKit(Provedor.Gemini));
        Assert.True(Provedores.PeloKit(Provedor.Claude));
        Assert.False(Provedores.PeloKit(Provedor.ChatGpt));
        Assert.Equal(@".pmvni-ai\analise-google", ResultadoAnalise.PastaControle(Provedor.Gemini));
        // Os dois de antes não mudaram.
        Assert.Equal(("ChatGPT", "chatgpt", "CODEX_API_KEY"), (Provedores.Nome(Provedor.ChatGpt), Provedores.Sufixo(Provedor.ChatGpt), Provedores.VariavelChave(Provedor.ChatGpt)));
        Assert.Equal(("Claude", "claude", "ANTHROPIC_API_KEY"), (Provedores.Nome(Provedor.Claude), Provedores.Sufixo(Provedor.Claude), Provedores.VariavelChave(Provedor.Claude)));
        Assert.Equal(@".pmvni-ai\analise", ResultadoAnalise.PastaControle(Provedor.ChatGpt));
        Assert.Equal(@".pmvni-ai\analise-pro", ResultadoAnalise.PastaControle(Provedor.Claude));
    }

    [Theory]
    // etapa, conta Ultra, API com créditos, API gratuita
    [InlineData("prepare", "gemini-4-hydrogen-high", "gemini-4-hydrogen-high", "gemini-4-hydrogen-high")]
    [InlineData("analyze", "gemini-4-carbon-high", "gemini-4-carbon-high", "gemini-4-hydrogen-xhigh")]
    [InlineData("technical_audit", "gemini-4-carbon-high", "gemini-4-carbon-high", "gemini-4-hydrogen-xhigh")]
    [InlineData("produce", "gemini-4-hydrogen-medium", "gemini-4-hydrogen-medium", "gemini-4-hydrogen-medium")]
    [InlineData("audit", "gemini-4-argon-high", "gemini-4-argon-high", "gemini-4-hydrogen-xhigh")]
    [InlineData("final_review", "gemini-4-carbon-high", "gemini-4-carbon-high", "gemini-4-hydrogen-xhigh")]
    public void matriz_das_seis_etapas_nos_tres_perfis(string etapa, string conta, string creditos, string gratuita)
    {
        string Id(PerfilGemini p) => MatrizGemini.Etapas(p).Single(e => e.Etapa == etapa).IdRuntime;
        Assert.Equal((conta, creditos, gratuita), (Id(PerfilGemini.ContaUltra), Id(PerfilGemini.ApiCreditos), Id(PerfilGemini.ApiGratuita)));
    }

    [Fact]
    public void matriz_tem_as_seis_etapas_na_ordem_e_so_niveis_que_o_modelo_aceita()
    {
        foreach (var perfil in Enum.GetValues<PerfilGemini>())
        {
            var etapas = MatrizGemini.Etapas(perfil);
            Assert.Equal(Etapas, etapas.Select(e => e.Etapa));
            Assert.Equal(SituacaoAnalisePro.Etapas, etapas.Select(e => e.Etapa));
            Assert.All(etapas, e => Assert.Contains(e.Esforco, MatrizGemini.Esforcos(e.Modelo)));
            Assert.All(etapas, e => Assert.NotEqual("low", e.Esforco));      // nenhuma etapa roda em low
        }
        Assert.DoesNotContain("xhigh", MatrizGemini.Esforcos(MatrizGemini.Carbon));
        Assert.DoesNotContain("xhigh", MatrizGemini.Esforcos(MatrizGemini.Argon));
        Assert.Contains("xhigh", MatrizGemini.Esforcos(MatrizGemini.Hydrogen));
        Assert.All(MatrizGemini.Etapas(PerfilGemini.ApiGratuita), e => Assert.Equal(MatrizGemini.Hydrogen, e.Modelo));     // a camada gratuita só tem o Hydrogen
        Assert.True(MatrizGemini.EtapaDeMerito("analyze") && MatrizGemini.EtapaDeMerito("technical_audit") && !MatrizGemini.EtapaDeMerito("audit"));
        Assert.Equal((2400, 131072), (MatrizGemini.TimeoutContaSegundos, MatrizGemini.MaxSaidaApi));
    }

    [Fact]
    public void perfil_codigo_rota_e_modo()
    {
        foreach (var p in Enum.GetValues<PerfilGemini>()) Assert.Equal(p, MatrizGemini.DoCodigo(MatrizGemini.Codigo(p)));
        Assert.Null(MatrizGemini.DoCodigo("outro"));
        Assert.Null(MatrizGemini.DoCodigo(null));
        Assert.Equal(("conta", ModoAcesso.Conta), (MatrizGemini.Rota(PerfilGemini.ContaUltra), MatrizGemini.Modo(PerfilGemini.ContaUltra)));
        Assert.Equal(("api", ModoAcesso.Chave), (MatrizGemini.Rota(PerfilGemini.ApiCreditos), MatrizGemini.Modo(PerfilGemini.ApiCreditos)));
        Assert.Equal(("api", ModoAcesso.Chave), (MatrizGemini.Rota(PerfilGemini.ApiGratuita), MatrizGemini.Modo(PerfilGemini.ApiGratuita)));

        // Conta ⇒ sempre a conta Ultra. Chave ⇒ só o perfil de API escolhido; sem escolha (ou com a conta no lugar), nenhum é presumido.
        Assert.Equal(PerfilGemini.ContaUltra, MatrizGemini.PerfilDoModo(ModoAcesso.Conta, PerfilGemini.ApiGratuita));
        Assert.Equal(PerfilGemini.ApiCreditos, MatrizGemini.PerfilDoModo(ModoAcesso.Chave, PerfilGemini.ApiCreditos));
        Assert.Equal(PerfilGemini.ApiGratuita, MatrizGemini.PerfilDoModo(ModoAcesso.Chave, PerfilGemini.ApiGratuita));
        Assert.Null(MatrizGemini.PerfilDoModo(ModoAcesso.Chave, null));
        Assert.Null(MatrizGemini.PerfilDoModo(ModoAcesso.Chave, PerfilGemini.ContaUltra));
    }

    [Fact]
    public void matriz_do_editor_e_igual_a_do_plugin_no_kit()
    {
        // O kit é só lido, no lugar: quem executa é o plugin; a cópia tipada do editor não pode divergir dele.
        var arquivo = Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins", "analise-google", "skills", "analise-google", "references", "matriz-gemini.json");
        if (!File.Exists(arquivo)) Assert.Fail("Plugin analise-google ausente do kit: " + arquivo + " (defina PMVNI_KIT_RAIZ)");
        var doKit = MatrizGemini.LerDoKit(File.ReadAllText(arquivo, Encoding.UTF8));
        Assert.NotNull(doKit);
        Assert.Equal(3, doKit!.Count);
        foreach (var perfil in Enum.GetValues<PerfilGemini>())
            Assert.Equal(MatrizGemini.Etapas(perfil), doKit[MatrizGemini.Codigo(perfil)]);
    }

    [Fact]
    public void ler_matriz_do_kit_recusa_json_fora_do_formato()
    {
        Assert.Null(MatrizGemini.LerDoKit("{ isso não é json"));
        Assert.Null(MatrizGemini.LerDoKit("[]"));
        Assert.Null(MatrizGemini.LerDoKit("{\"perfis\":{\"conta_ultra\":{\"prepare\":[\"gemini-4-hydrogen\"]}}}"));
        var ok = MatrizGemini.LerDoKit("{\"perfis\":{\"conta_ultra\":{\"prepare\":[\"gemini-4-hydrogen\",\"high\"]}}}");
        Assert.Equal(new EtapaGemini("prepare", "gemini-4-hydrogen", "high"), Assert.Single(ok!["conta_ultra"]));
    }

    // ------------------------------------------------------------------ ambiente do processo filho

    static Dictionary<string, string?> Herdado() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["PATH"] = @"C:\Windows", ["GEMINI_API_KEY"] = "herdada-gemini", ["GOOGLE_API_KEY"] = "herdada-google", ["ANTHROPIC_API_KEY"] = "herdada-anthropic",
        ["PMVNI_ANALISE_GOOGLE_ROTA"] = "api", ["PMVNI_ANALISE_GOOGLE_PERFIL"] = "api_creditos", ["PMVNI_ANALISE_GOOGLE_FICTICIO"] = "1",
    };

    [Fact]
    public void ambiente_da_conta_nunca_leva_chave_e_declara_rota_e_perfil()
    {
        var env = Herdado();
        AmbienteFilho.Aplicar(env, Provedor.Gemini, ModoAcesso.Conta, null, PerfilGemini.ContaUltra);
        Assert.False(env.ContainsKey("GEMINI_API_KEY"));
        Assert.False(env.ContainsKey("GOOGLE_API_KEY"));
        Assert.False(env.ContainsKey("ANTHROPIC_API_KEY"));
        Assert.Equal(("conta", "conta_ultra"), (env["PMVNI_ANALISE_GOOGLE_ROTA"], env["PMVNI_ANALISE_GOOGLE_PERFIL"]));
        Assert.False(env.ContainsKey("PMVNI_ANALISE_GOOGLE_FICTICIO"));
        Assert.False(env.ContainsKey("PMVNI_ANALISE_PRO_ROTA"));
        Assert.Equal(@"C:\Windows", env["PATH"]);
    }

    [Theory]
    [InlineData(PerfilGemini.ApiCreditos, false, "api_creditos", null)]
    [InlineData(PerfilGemini.ApiCreditos, true, "api_creditos", null)]       // a declaração de fictício só vale no perfil gratuito
    [InlineData(PerfilGemini.ApiGratuita, false, "api_gratuita", null)]      // sem a confirmação do usuário, o plugin recusa
    [InlineData(PerfilGemini.ApiGratuita, true, "api_gratuita", "1")]
    public void ambiente_da_api_leva_a_chave_do_cofre_a_rota_e_o_perfil(PerfilGemini perfil, bool ficticio, string codigo, string? marca)
    {
        var env = Herdado();
        AmbienteFilho.Aplicar(env, Provedor.Gemini, ModoAcesso.Chave, "chave-do-cofre", perfil, ficticio);
        Assert.Equal("chave-do-cofre", env["GEMINI_API_KEY"]);       // a herdada do Windows nunca é a usada
        Assert.False(env.ContainsKey("GOOGLE_API_KEY"));
        Assert.Equal(("api", codigo), (env["PMVNI_ANALISE_GOOGLE_ROTA"], env["PMVNI_ANALISE_GOOGLE_PERFIL"]));
        Assert.Equal(marca, env.TryGetValue("PMVNI_ANALISE_GOOGLE_FICTICIO", out var v) ? v : null);
    }

    [Fact]
    public void ambiente_sem_perfil_nao_declara_nada_e_perfil_incoerente_e_recusado()
    {
        // Leitura da situação: sem perfil, sem rota, sem chave.
        var env = Herdado();
        AmbienteFilho.Aplicar(env, Provedor.Gemini, ModoAcesso.Conta, null);
        Assert.DoesNotContain(env.Keys, k => k.StartsWith("PMVNI_ANALISE_GOOGLE_", StringComparison.Ordinal) || k is "GEMINI_API_KEY" or "GOOGLE_API_KEY");

        // O perfil define o modo; os dois em desacordo nunca partem (nem a conta com chave, nem a chave com o perfil da conta).
        Assert.Throws<ArgumentException>(() => AmbienteFilho.Aplicar(Herdado(), Provedor.Gemini, ModoAcesso.Conta, null, PerfilGemini.ApiCreditos));
        Assert.Throws<ArgumentException>(() => AmbienteFilho.Aplicar(Herdado(), Provedor.Gemini, ModoAcesso.Chave, "k", PerfilGemini.ContaUltra));
        Assert.Throws<ArgumentException>(() => AmbienteFilho.Aplicar(Herdado(), Provedor.Gemini, ModoAcesso.Chave, " ", PerfilGemini.ApiCreditos));

        // Os outros provedores não recebem as variáveis do Google, e as chaves do Google herdadas saem deles também.
        var claude = Herdado();
        AmbienteFilho.Aplicar(claude, Provedor.Claude, ModoAcesso.Conta, null);
        Assert.DoesNotContain(claude.Keys, k => k.StartsWith("PMVNI_ANALISE_GOOGLE_", StringComparison.Ordinal) || k is "GEMINI_API_KEY" or "GOOGLE_API_KEY");
        Assert.Equal("conta", claude["PMVNI_ANALISE_PRO_ROTA"]);
    }

    // ------------------------------------------------------------------ chave protegida e peças

    [Fact]
    public void chave_do_gemini_fica_no_cofre_proprio_protegida()
    {
        using var pasta = Ambiente.PastaDeTeste("gemini-cofre");
        const string chave = "gemini-ficticia-chave-ficticia-de-teste-0123456789";
        Assert.False(CofreChaves.Existe(pasta, Provedor.Gemini));
        CofreChaves.Guardar(pasta, Provedor.Gemini, "  " + chave + "\r\n");
        var arquivo = Path.Combine(pasta, "segredos", "gemini.dpapi");
        Assert.True(File.Exists(arquivo));
        Assert.False(CofreChavesTestes.Contem(File.ReadAllBytes(arquivo), chave));      // nunca em texto
        Assert.Equal(chave, CofreChaves.Ler(pasta, Provedor.Gemini));
        Assert.Null(CofreChaves.Ler(pasta, Provedor.Claude));                           // um provedor não lê a chave do outro
        CofreChaves.Remover(pasta, Provedor.Gemini);
        Assert.False(CofreChaves.Existe(pasta, Provedor.Gemini));
    }

    [Fact]
    public void localizacao_acha_o_agy_e_o_analise_google_instalado()
    {
        using var pasta = Ambiente.PastaDeTeste("gemini-pecas");
        var local = Path.Combine(pasta, "local");
        var codexHome = Path.Combine(pasta, "codex-home");
        var kit = Path.Combine(pasta, "kit");
        var opcoes = new OpcoesLocalizacao(local, codexHome, [], kit, null, null);

        var nada = Localizacao.Ler(opcoes);
        Assert.Null(nada.Agy);
        Assert.Null(nada.GoogleRuntime);
        Assert.Null(nada.RuntimeDoKit(Provedor.Gemini));

        Directory.CreateDirectory(Path.Combine(local, "agy", "bin"));
        File.WriteAllText(Path.Combine(local, "agy", "bin", "agy.exe"), "");
        string Scripts(string versao) => Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise-google", versao, "skills", "analise-google", "scripts");
        Directory.CreateDirectory(Scripts("1.0.0"));
        File.WriteAllText(Path.Combine(Scripts("1.0.0"), "google_runtime.py"), "ROTA_VAR = 'PMVNI_ANALISE_GOOGLE_ROTA'\n");
        Directory.CreateDirectory(Path.Combine(kit, "01 - PLUGINS", "plugins", "analise-google", ".codex-plugin"));
        File.WriteAllText(Path.Combine(kit, "01 - PLUGINS", "plugins", "analise-google", ".codex-plugin", "plugin.json"), "{\"version\":\"1.0.1\"}");

        var p = Localizacao.Ler(opcoes);
        Assert.Equal(Path.Combine(local, "agy", "bin", "agy.exe"), p.Agy);
        Assert.Equal(Path.Combine(Scripts("1.0.0"), "google_runtime.py"), p.GoogleRuntime);
        Assert.Equal(("1.0.0", "1.0.1"), (p.VersaoAnaliseGoogle, p.VersaoAnaliseGoogleKit));
        Assert.Equal(p.GoogleRuntime, p.RuntimeDoKit(Provedor.Gemini));
        Assert.Null(p.RuntimeDoKit(Provedor.ChatGpt));
        Assert.Equal("1.0.0", OrquestradorAnalise.VersaoDoPlugin(Provedor.Gemini, p));
        Assert.Equal("análise Google", OrquestradorAnalise.PluginUsado(Provedor.Gemini));
        Assert.Equal("1.0.0", PluginsInstalados.NoCodex(codexHome, PluginsInstalados.AnaliseGoogle)?.Versao);

        // Um runtime sem a rota explícita (contrato com o editor) não é usado.
        Directory.CreateDirectory(Scripts("1.1.0"));
        File.WriteAllText(Path.Combine(Scripts("1.1.0"), "google_runtime.py"), "# sem a marca do contrato\n");
        Assert.Null(Localizacao.Ler(opcoes).GoogleRuntime);
        Assert.False(PoliticaKit.RuntimeGoogleValido(""));
        Assert.True(PoliticaKit.RuntimeGoogleValido("x PMVNI_ANALISE_GOOGLE_ROTA y"));
    }

    [Fact]
    public void linhas_de_comando_do_gemini()
    {
        var (exe, args) = LinhasDeComando.ProRuntime(@"C:\py\python.exe", false, @"C:\k\google_runtime.py", @"C:\proc\0001", "conferir");
        Assert.Equal(@"C:\py\python.exe", exe);
        Assert.Equal(new[] { "-B", "-X", "utf8", @"C:\k\google_runtime.py", "--case", @"C:\proc\0001", "conferir" }, args);
        Assert.Throws<ArgumentException>(() => LinhasDeComando.ProRuntime("p", false, "s", "c", "run"));
        Assert.Equal(new[] { "--version" }, LinhasDeComando.AgyVersao("agy.exe").Args);
        Assert.Equal(new[] { "models" }, LinhasDeComando.AgyModelos("agy.exe").Args);
    }

    // ------------------------------------------------------------------ o "Testar"

    const string ListaDoAgy = "Fetching available models...\n"
        + "gemini-4-carbon-medium\tGemini 4 Carbon (Medium)\ngemini-4-carbon-high\tGemini 4 Carbon (High)\ngemini-4-argon-high\tGemini 4 Argon (High)\n"
        + "gemini-4-hydrogen-high\tGemini 4 Hydrogen (High)\ngemini-4-hydrogen-xhigh\tGemini 4 Hydrogen (XHigh)\ngemini-4-hydrogen-medium\tGemini 4 Hydrogen (Medium)\n";

    [Fact]
    public void modelos_do_agy_e_etapas_sem_modelo()
    {
        var ids = DiagnosticoGemini.ModelosDoAgy(ListaDoAgy);
        Assert.Equal(6, ids.Count);
        Assert.DoesNotContain(ids, i => i.Contains(' '));
        foreach (var perfil in Enum.GetValues<PerfilGemini>()) Assert.Empty(DiagnosticoGemini.EtapasSemModelo(perfil, ids));

        // Sem o Argon high, só a auditoria cruzada dos perfis pagos fica sem modelo; o perfil gratuito (só Hydrogen) continua inteiro.
        var semArgon = ids.Where(i => !i.StartsWith("gemini-4-argon", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "Auditoria cruzada (gemini-4-argon high)" }, DiagnosticoGemini.EtapasSemModelo(PerfilGemini.ContaUltra, semArgon));
        Assert.Empty(DiagnosticoGemini.EtapasSemModelo(PerfilGemini.ApiGratuita, semArgon));
        Assert.Empty(DiagnosticoGemini.ModelosDoAgy("Fetching available models...\n"));       // sem login: lista vazia
        Assert.Equal("1.5.10", DiagnosticoGemini.VersaoAgy("1.5.10\n"));
        Assert.Null(DiagnosticoGemini.VersaoAgy("sem versão"));
    }

    [Fact]
    public void modelos_da_api_e_recusas_em_portugues()
    {
        const string corpo = "{\"models\":[{\"name\":\"models/gemini-4-hydrogen\",\"supportedGenerationMethods\":[\"generateContent\"]},"
            + "{\"name\":\"models/gemini-4-carbon\",\"supportedGenerationMethods\":[\"generateContent\",\"countTokens\"]},"
            + "{\"name\":\"models/text-embedding-9\",\"supportedGenerationMethods\":[\"embedContent\"]}]}";
        var modelos = DiagnosticoGemini.ModelosDaApi(corpo);
        Assert.Equal(new[] { "gemini-4-hydrogen", "gemini-4-carbon" }, modelos);
        Assert.Equal(new[] { "Auditoria cruzada (gemini-4-argon)" }, DiagnosticoGemini.EtapasSemModeloNaApi(PerfilGemini.ApiCreditos, modelos));
        Assert.Empty(DiagnosticoGemini.EtapasSemModeloNaApi(PerfilGemini.ApiGratuita, modelos));
        Assert.Empty(DiagnosticoGemini.ModelosDaApi("não é json"));

        Assert.StartsWith("chave recusada pelo Google", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"API key not valid.\",\"status\":\"INVALID_ARGUMENT\",\"details\":[{\"reason\":\"API_KEY_INVALID\"}]}}"));
        Assert.StartsWith("chave recusada pelo Google", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.Unauthorized, ""));
        Assert.StartsWith("chave sem permissão", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.Forbidden, "{\"error\":{\"status\":\"PERMISSION_DENIED\"}}"));
        Assert.StartsWith("limite de uso da chave", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.TooManyRequests, "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}"));
        Assert.StartsWith("a API do Google respondeu 500", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.InternalServerError, ""));
    }

    sealed class RespostaFixa(HttpStatusCode codigo, string corpo) : HttpMessageHandler
    {
        public HttpRequestMessage? Pedido { get; private set; }
        public string? ChaveNoCabecalho { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedido = request;
            ChaveNoCabecalho = request.Headers.TryGetValues("x-goog-api-key", out var v) ? v.Single() : null;
            return Task.FromResult(new HttpResponseMessage(codigo) { Content = new StringContent(corpo, Encoding.UTF8, "application/json") });
        }
    }

    sealed class SemRede : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new HttpRequestException("sem rede");
    }

    [Fact]
    public async Task conferir_chave_simulada_aceita_recusada_e_sem_rede()
    {
        // SIMULADO: a resposta da API do Google é fixa; nenhuma chave real e nenhuma rede.
        const string chave = "gemini-ficticia-chave-ficticia-de-teste-0123456789";
        try
        {
            var aceita = new RespostaFixa(HttpStatusCode.OK, "{\"models\":[{\"name\":\"models/gemini-4-hydrogen\"}]}");
            DiagnosticoGemini.Manipulador = aceita;
            var r = await DiagnosticoGemini.ConferirChaveAsync(chave, CancellationToken.None);
            Assert.True(r.Aceita);
            Assert.Equal(new[] { "gemini-4-hydrogen" }, r.Modelos);
            Assert.Equal(chave, aceita.ChaveNoCabecalho);                                         // só no cabeçalho…
            Assert.DoesNotContain(chave, aceita.Pedido!.RequestUri!.ToString());                  // …nunca no endereço
            Assert.DoesNotContain(chave, r.Mensagem);

            DiagnosticoGemini.Manipulador = new RespostaFixa(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"API key not valid. Please pass a valid API key.\"}}");
            r = await DiagnosticoGemini.ConferirChaveAsync(chave, CancellationToken.None);
            Assert.False(r.Aceita);
            Assert.StartsWith("chave recusada pelo Google", r.Mensagem);
            Assert.DoesNotContain(chave, r.Mensagem);

            DiagnosticoGemini.Manipulador = new RespostaFixa(HttpStatusCode.TooManyRequests, "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}");
            Assert.StartsWith("limite de uso da chave", (await DiagnosticoGemini.ConferirChaveAsync(chave, CancellationToken.None)).Mensagem);

            DiagnosticoGemini.Manipulador = new SemRede();
            r = await DiagnosticoGemini.ConferirChaveAsync(chave, CancellationToken.None);
            Assert.False(r.Aceita);
            Assert.Contains("a chave não foi conferida", r.Mensagem);
        }
        finally { DiagnosticoGemini.Manipulador = null; }
    }

    [Fact]
    public async Task testar_sem_pecas_diz_o_que_falta_e_pede_o_perfil_da_chave()
    {
        var nada = new Pecas(null, null, null, false, null, null, null, false, null, false);

        var conta = await Diagnostico.TestarAsync(Provedor.Gemini, nada, ModoAcesso.Conta, false, CancellationToken.None);
        Assert.Equal("Antigravity CLI (agy) não encontrado · análise Google não instalado no Codex · Python não encontrado", conta.Linha);
        Assert.Null(conta.ContaConectada);
        Assert.Empty(conta.Avisos);

        // Modo chave sem chave e sem perfil: nada é presumido; o aviso pede a escolha do perfil.
        var chave = await Diagnostico.TestarAsync(Provedor.Gemini, nada, ModoAcesso.Chave, false, CancellationToken.None);
        Assert.EndsWith("nenhuma chave guardada", chave.Linha);
        Assert.Contains(OrquestradorAnalise.SemPerfilApi, chave.Avisos);

        try
        {
            DiagnosticoGemini.Manipulador = new RespostaFixa(HttpStatusCode.OK, "{\"models\":[{\"name\":\"models/gemini-4-hydrogen\"}]}");
            var gratuita = await Diagnostico.TestarAsync(Provedor.Gemini, nada, ModoAcesso.Chave, true, CancellationToken.None, PerfilGemini.ApiGratuita, "gemini-ficticia-ficticia");
            Assert.EndsWith("chave aceita pelo Google", gratuita.Linha);
            Assert.Contains(gratuita.Avisos, a => a.StartsWith("API gratuita: só para testes com processos fictícios", StringComparison.Ordinal));
            Assert.DoesNotContain(gratuita.Avisos, a => a.StartsWith("A chave não lista o modelo", StringComparison.Ordinal));     // o gratuito só usa o Hydrogen
            Assert.DoesNotContain("gemini-ficticia-ficticia", gratuita.Linha + string.Join("", gratuita.Avisos));

            // A mesma chave no perfil com créditos: faltam o Carbon e o Argon na lista da chave, e o aviso diz as etapas.
            var creditos = await Diagnostico.TestarAsync(Provedor.Gemini, nada, ModoAcesso.Chave, true, CancellationToken.None, PerfilGemini.ApiCreditos, "gemini-ficticia-ficticia");
            var falta = Assert.Single(creditos.Avisos, a => a.StartsWith("A chave não lista o modelo", StringComparison.Ordinal));
            Assert.Contains("Análise (gemini-4-carbon)", falta);
            Assert.Contains("Auditoria cruzada (gemini-4-argon)", falta);
        }
        finally { DiagnosticoGemini.Manipulador = null; }
    }

    [Theory]
    [InlineData("ok", true, "conectado com a conta Google")]
    [InlineData("sem_login", false, "não conectado (abra um terminal, rode \"agy\" e entre na conta Google)")]
    public async Task testar_conta_com_o_agy_falso_diz_o_login_e_as_etapas_sem_modelo(string caso, bool conectado, string sessao)
    {
        // SIMULADO: o agente falso faz o papel do agy (--version e models); nenhuma conta Google é consultada.
        using var pasta = Ambiente.PastaDeTeste("gemini-testar-" + caso);
        var cenario = Ambiente.CenarioAjustado(pasta, "ok", o =>
        {
            o["versoes"] = new System.Text.Json.Nodes.JsonArray("1.5.10", "Python 3.14.0");
            if (caso == "sem_login") o["login"] = "nao";
            else o["modelos_agy"] = new System.Text.Json.Nodes.JsonArray("gemini-4-hydrogen-high", "gemini-4-hydrogen-medium", "gemini-4-carbon-high");     // sem o Argon
        });
        using var v = new VariavelDeAmbiente("AGENTE_FALSO_CENARIO", cenario);
        var pecas = new Pecas(null, null, Ambiente.AgenteFalso, false, null, null, null, false, null, false)
        {
            Agy = Ambiente.AgenteFalso, GoogleRuntime = Path.Combine(pasta, "google_runtime.py"), VersaoAnaliseGoogle = "1.0.0", VersaoAnaliseGoogleKit = "1.0.1",
        };

        var r = await Diagnostico.TestarAsync(Provedor.Gemini, pecas, ModoAcesso.Conta, false, CancellationToken.None);

        Assert.Equal("Antigravity CLI 1.5.10 · " + sessao + " · análise Google 1.0.0 (kit: 1.0.1) · Python 3.14.0", r.Linha);
        Assert.Equal(conectado, r.ContaConectada);
        Assert.Contains("O kit tem o análise Google 1.0.1; instalado 1.0.0. Atualize pelo INSTALAR_KIT_PMVNI.cmd.", r.Avisos);
        if (conectado)
            Assert.Contains("A conta não oferece o modelo e o nível de: Auditoria cruzada (gemini-4-argon high). A análise não começa sem eles (nenhum outro modelo é usado no lugar).", r.Avisos);
        else Assert.DoesNotContain(r.Avisos, a => a.StartsWith("A conta não oferece", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ o perfil fixado na análise

    [Fact]
    public void perfil_fixado_vem_dos_arquivos_do_plugin_e_some_quando_a_analise_conclui()
    {
        using var pasta = Ambiente.PastaDeTeste("gemini-fixado");
        var controle = Path.Combine(pasta, ".pmvni-ai", "analise-google");
        Assert.Null(GeminiFixado.Ler(pasta));                                   // nada começou

        Directory.CreateDirectory(controle);
        File.WriteAllText(Path.Combine(controle, "auto-status.json"), "{\"situacao\":\"PAUSADO_COTA\",\"modo\":\"api\",\"perfil\":\"api_creditos\"}");
        Assert.Null(GeminiFixado.Ler(pasta));                                   // sem engine_state.json nenhuma etapa foi preparada: nada fixado

        File.WriteAllText(Path.Combine(controle, "engine_state.json"), "{\"modo\":\"api\",\"perfil\":\"api_creditos\",\"stages\":{}}");
        Assert.Equal(PerfilGemini.ApiCreditos, GeminiFixado.Ler(pasta));

        File.WriteAllText(Path.Combine(controle, "engine_state.json"), "{\"stages\":{}}");     // estado sem o campo: vale o do status
        Assert.Equal(PerfilGemini.ApiCreditos, GeminiFixado.Ler(pasta));

        File.WriteAllText(Path.Combine(controle, "auto-status.json"), "{\"situacao\":\"CONCLUIDO\",\"perfil\":\"api_creditos\"}");
        Assert.Null(GeminiFixado.Ler(pasta));                                   // concluída: a próxima análise escolhe de novo

        File.WriteAllText(Path.Combine(controle, "auto-status.json"), "{ ilegível");
        File.WriteAllText(Path.Combine(controle, "engine_state.json"), "{\"perfil\":\"outro\"}");
        Assert.Null(GeminiFixado.Ler(pasta));                                   // ilegível ou desconhecido não fixa nada
    }
}
