using System.Net;
using System.Text;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>O terceiro provedor (Gemini, pelo análise Google do kit): a matriz tipada de modelos e níveis, o ambiente do processo filho, a leitura das peças
/// e o "Testar". Tudo simulado ou automatizado: nenhum teste aqui fala com o Google nem roda o Antigravity de verdade.</summary>
[Collection("Ambiente")]
public class GeminiTestes
{
    /// <summary>As etapas de IA da matriz Gemini (fluxo linear, etapas.json versão 2): preparo, auditoria cruzada, produção e revisão final são de programa e não têm modelo.</summary>
    static readonly string[] Etapas = { "analyze", "technical_audit", "autocorrecao" };

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
    [InlineData("analyze", "gemini-3.8-flash-medium", "gemini-4-hydrogen-medium", "gemini-4-carbon-medium")]
    [InlineData("technical_audit", "gemini-3.8-flash-high", "gemini-4-hydrogen-high", "gemini-4-argon-medium")]
    [InlineData("autocorrecao", "gemini-3.8-flash-medium", "gemini-4-hydrogen-medium", "gemini-4-carbon-medium")]
    public void matriz_das_etapas_de_ia_por_rota(string etapa, string agy, string web, string api)
    {
        foreach (var perfil in Enum.GetValues<PerfilGemini>())
        {
            var id = MatrizGemini.Etapas(perfil).Single(e => e.Etapa == etapa).IdRuntime;
            Assert.Equal(perfil == PerfilGemini.ContaGemini ? web : perfil == PerfilGemini.ApiCreditos ? api : agy, id);
        }
    }

    [Fact]
    public void matriz_tem_so_as_etapas_de_ia_na_ordem_e_so_niveis_que_o_modelo_aceita()
    {
        foreach (var perfil in Enum.GetValues<PerfilGemini>())
        {
            var etapas = MatrizGemini.Etapas(perfil);
            Assert.Equal(Etapas, etapas.Select(e => e.Etapa));
            Assert.DoesNotContain(etapas, e => e.Etapa is "prepare" or "produce");   // as de programa não têm modelo
            Assert.All(etapas, e => Assert.Contains(e.Esforco, MatrizGemini.Esforcos(e.Modelo)));
        }
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
        // Tarefa 19: a matriz é a rota gemini da tabela única de modelos do plugin analise (references\modelos-por-rota.json).
        var arquivo = Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins", "analise", "skills", "analise", "references", "modelos-por-rota.json");
        if (!File.Exists(arquivo)) Assert.Fail("Tabela de modelos do plugin analise ausente do kit: " + arquivo + " (defina PMVNI_KIT_RAIZ)");
        var doKit = MatrizGemini.LerDaTabela(File.ReadAllText(arquivo, Encoding.UTF8));
        Assert.NotNull(doKit);
        Assert.Equal(Enum.GetValues<PerfilGemini>().Length, doKit!.Count);
        foreach (var perfil in Enum.GetValues<PerfilGemini>())
            Assert.Equal(MatrizGemini.Etapas(perfil), doKit[MatrizGemini.Codigo(perfil)]);
    }

    [Fact]
    public void ler_matriz_da_tabela_recusa_json_fora_do_formato()
    {
        Assert.Null(MatrizGemini.LerDaTabela("{ isso não é json"));
        Assert.Null(MatrizGemini.LerDaTabela("[]"));
        Assert.Null(MatrizGemini.LerDaTabela("{\"equivalencia\":{\"alto\":\"high\"},\"rotas\":{\"gemini\":{\"perfis\":{\"conta_ultra\":{\"etapas\":{\"prepare\":{\"modelo\":\"gemini-4-hydrogen\"}}}}}}}"));
        var ok = MatrizGemini.LerDaTabela("{\"equivalencia\":{\"alto\":\"high\"},\"rotas\":{\"gemini\":{\"perfis\":{\"conta_ultra\":{\"etapas\":{\"prepare\":{\"modelo\":\"gemini-4-hydrogen\",\"nivel\":\"alto\"}}}}}}}");
        Assert.Equal(new EtapaGemini("prepare", "gemini-4-hydrogen", "high"), Assert.Single(ok!["conta_ultra"]));
    }

    [Fact]
    public void testar_confere_as_etapas_da_tabela_efetiva()
    {
        // Tarefa 19 (correção 1): com o ajuste local, o "Testar" do Gemini confere o modelo que a análise vai pedir, não o da matriz padrão.
        var efetivas = MatrizGemini.Etapas(PerfilGemini.ContaUltra).Select(e => e.Etapa == "analyze" ? e with { Modelo = "gemini-5-nova", Esforco = "high" } : e).ToList();
        var ids = MatrizGemini.Etapas(PerfilGemini.ContaUltra).Select(e => e.IdRuntime).ToList();
        Assert.Empty(DiagnosticoGemini.EtapasSemModelo(PerfilGemini.ContaUltra, ids));
        Assert.Equal(["Análise (gemini-5-nova high)"], DiagnosticoGemini.EtapasSemModelo(PerfilGemini.ContaUltra, ids, efetivas));
        Assert.Empty(DiagnosticoGemini.EtapasSemModelo(PerfilGemini.ContaUltra, [.. ids, "gemini-5-nova-high"], efetivas));
        Assert.Equal(["Análise (gemini-5-nova)"], DiagnosticoGemini.EtapasSemModeloNaApi(PerfilGemini.ContaUltra, ["gemini-3.8-flash"], efetivas));
    }

    [Fact]
    public void conta_gemini_usa_web_sem_chave_de_api()
    {
        var perfil = MatrizGemini.PerfilDoModo(ModoAcesso.ContaGemini, PerfilGemini.ApiCreditos);
        Assert.Equal(PerfilGemini.ContaGemini, perfil);
        Assert.Null(MatrizGemini.PerfilDoModo(ModoAcesso.Chave, perfil));

        var env = Herdado();
        AmbienteFilho.Aplicar(env, Provedor.Gemini, ModoAcesso.ContaGemini, null, perfil);
        Assert.Equal("web", env["PMVNI_ANALISE_GOOGLE_ROTA"]);
        Assert.Equal("conta_gemini", env["PMVNI_ANALISE_GOOGLE_PERFIL"]);
        Assert.DoesNotContain("GEMINI_API_KEY", env.Keys);
        Assert.DoesNotContain("GOOGLE_API_KEY", env.Keys);
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
    public void localizacao_acha_o_agy_e_a_rota_gemini_do_plugin_analise()
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
        // Tarefa 18: o google_runtime.py é o controlador da rota gemini do plugin único (analise 4.0.0+).
        string Scripts(string versao) => Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", "analise", versao, "skills", "analise", "scripts", "rotas", "gemini");
        Directory.CreateDirectory(Scripts("4.0.0"));
        File.WriteAllText(Path.Combine(Scripts("4.0.0"), "google_runtime.py"), "ROTA_VAR = 'PMVNI_ANALISE_GOOGLE_ROTA'\n");
        Directory.CreateDirectory(Path.Combine(kit, "01 - PLUGINS", "plugins", "analise", ".codex-plugin"));
        File.WriteAllText(Path.Combine(kit, "01 - PLUGINS", "plugins", "analise", ".codex-plugin", "plugin.json"), "{\"version\":\"4.0.1\"}");

        var p = Localizacao.Ler(opcoes);
        Assert.Null(p.Agy);
        Assert.Null(p.GoogleRuntime);
        Assert.Null(p.RuntimeDoKit(Provedor.Gemini));
        Assert.Null(p.VersaoAnaliseGoogle);
        Assert.Null(p.VersaoAnaliseGoogleKit);

        // Um runtime sem a rota explícita (contrato com o editor) não é usado.
        Directory.CreateDirectory(Scripts("4.1.0"));
        File.WriteAllText(Path.Combine(Scripts("4.1.0"), "google_runtime.py"), "# sem a marca do contrato\n");
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
        + "gemini-3.8-flash-low\tGemini 3.8 Flash (Low)\n"
        + "gemini-3.8-flash-medium\tGemini 3.8 Flash (Medium)\n"
        + "gemini-3.8-flash-high\tGemini 3.8 Flash (High)\n";

    [Fact]
    public void modelos_do_agy_e_etapas_sem_modelo()
    {
        var ids = DiagnosticoGemini.ModelosDoAgy(ListaDoAgy);
        Assert.Equal(3, ids.Count);
        Assert.DoesNotContain(ids, i => i.Contains(' '));
        foreach (var perfil in Enum.GetValues<PerfilGemini>().Where(p => p is PerfilGemini.ContaUltra or PerfilGemini.ApiGratuita)) Assert.Empty(DiagnosticoGemini.EtapasSemModelo(perfil, ids));

        // Análise e autocorreção usam Flash médio, e a auditoria técnica usa Flash alto.
        Assert.Empty(DiagnosticoGemini.EtapasSemModelo(PerfilGemini.ContaUltra, ids.Where(i => i != "gemini-3.8-flash-low").ToList()));
        var semMedio = ids.Where(i => i != "gemini-3.8-flash-medium").ToList();
        Assert.Equal(["Análise (gemini-3.8-flash medium)", "Autocorreção (gemini-3.8-flash medium)"], DiagnosticoGemini.EtapasSemModelo(PerfilGemini.ContaUltra, semMedio));
        Assert.Single(DiagnosticoGemini.EtapasSemModelo(PerfilGemini.ApiGratuita, ids.Where(i => i != "gemini-3.8-flash-high").ToList()));
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
        Assert.Single(DiagnosticoGemini.EtapasSemModeloNaApi(PerfilGemini.ApiCreditos, modelos));    // argon (auditoria técnica)
        Assert.Equal(3, DiagnosticoGemini.EtapasSemModeloNaApi(PerfilGemini.ApiGratuita, modelos).Count);
        Assert.Empty(DiagnosticoGemini.ModelosDaApi("não é json"));

        Assert.StartsWith("chave recusada pelo Google", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"API key not valid.\",\"status\":\"INVALID_ARGUMENT\",\"details\":[{\"reason\":\"API_KEY_INVALID\"}]}}"));
        Assert.StartsWith("chave recusada pelo Google", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.Unauthorized, ""));
        Assert.StartsWith("chave sem permissão", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.Forbidden, "{\"error\":{\"status\":\"PERMISSION_DENIED\"}}"));
        Assert.StartsWith("limite de uso da chave", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.TooManyRequests, "{\"error\":{\"status\":\"RESOURCE_EXHAUSTED\"}}"));
        Assert.StartsWith("a API do Google respondeu 500", DiagnosticoGemini.MensagemDaRecusa(HttpStatusCode.InternalServerError, ""));
    }

    [Fact]
    public async Task diagnostico_google_desativado_recusa_todos_os_modos()
    {
        var pecas = new Pecas(null, null, null, false, null, null, null, false, null, true);
        foreach (var modo in Enum.GetValues<ModoAcesso>())
        {
            var resultado = await Diagnostico.TestarAsync(Provedor.Gemini, pecas, modo, true, CancellationToken.None);
            Assert.Equal(Provedores.RotaDesativada, resultado.Linha);
        }
        var chave = await DiagnosticoGemini.ConferirChaveAsync("ficticia", CancellationToken.None);
        Assert.False(chave.Aceita);
        Assert.Equal(Provedores.RotaDesativada, chave.Mensagem);
    }

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
