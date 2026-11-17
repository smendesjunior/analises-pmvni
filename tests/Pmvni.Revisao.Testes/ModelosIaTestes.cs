using Pmvni.Agentes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Ajustes › Inteligência artificial › Modelos de IA (2026-11-17): as listas lidas dos apps e das APIs, os níveis de raciocínio, a
/// escolha da fonte (com "Escolha por mim" e a reserva), a linha de comando e a migração do "Terra 6.1, esforço baixo" de antes.</summary>
public class ModelosIaTestes
{
    // ---------- leitura das listas ----------

    const string CatalogoCodex = """
        {"fetched_at":"2026-11-17","models":[
          {"slug":"gpt-6.1-sol","display_name":"GPT-6.1-Sol","visibility":"list","default_reasoning_level":"medium",
           "supported_reasoning_levels":[{"effort":"high"},{"effort":"low"},{"effort":"medium"},{"effort":"xhigh"},{"effort":"max"},{"effort":"ultra"}]},
          {"slug":"gpt-6.1-terra","display_name":"GPT-6.1 Terra","visibility":"list","default_reasoning_level":"low",
           "supported_reasoning_levels":[{"effort":"low"},{"effort":"medium"}]},
          {"slug":"gpt-reserve","display_name":"GPT-Reserve","visibility":"hide","supported_reasoning_levels":[{"effort":"low"}]},
          {"slug":"codexias-claude-opus-5-5","display_name":"Claude Opus 5.5","visibility":"list","supported_reasoning_levels":[{"effort":"low"}]},
          {"slug":"phi4-mini:latest","display_name":"Phi-4","visibility":"list","supported_reasoning_levels":[{"effort":"none"}]},
          {"slug":"gpt-6.1-sol","display_name":"repetido","visibility":"list"},
          {"slug":"gpt-x --dangerously","display_name":"ruim","visibility":"list"}
        ]}
        """;

    [Fact]
    public void o_catalogo_do_codex_fica_so_com_os_modelos_visiveis_da_openai()
    {
        var m = CatalogoIa.LerCodex(CatalogoCodex);
        Assert.Equal(new[] { "gpt-6.1-sol", "gpt-6.1-terra" }, m.Select(x => x.Id));
        Assert.Equal("GPT-6.1-Sol", m[0].Nome);
        Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max", "ultra" }, m[0].Niveis);   // em ordem, do menor ao maior
        Assert.Equal("medium", m[0].NivelPadrao);
        Assert.Equal("low", m[1].NivelPadrao);
        Assert.Empty(CatalogoIa.LerCodex("não é json"));
        Assert.Empty(CatalogoIa.LerCodex("{\"models\":5}"));
    }

    [Fact]
    public void o_config_do_codex_diz_o_modelo_e_o_nivel_do_app()
    {
        var toml = "model = \"gpt-6.1-sol\"\r\nmodel_catalog_json = \"C:\\\\Users\\\\x\\\\.codex\\\\cat.json\"\nmodel_reasoning_effort = \"low\"\n[profiles.rapido]\nmodel = \"gpt-6-luna\"\n";
        var (modelo, nivel, catalogo) = CatalogoIa.LerConfigCodex(toml);
        Assert.Equal("gpt-6.1-sol", modelo);   // só as chaves de cima: o perfil não conta
        Assert.Equal("low", nivel);
        Assert.Equal(@"C:\Users\x\.codex\cat.json", catalogo);
        Assert.Equal((null, null, null), CatalogoIa.LerConfigCodex(""));
    }

    [Fact]
    public void o_claude_code_lista_os_modelos_da_conta_sem_o_default_e_sem_apelidos()
    {
        var linha = """
            {"type":"control_response","response":{"subtype":"success","request_id":"pmvni-modelos","response":{"models":[
              {"value":"default","resolvedModel":"claude-opus-5-5","displayName":"Default (recommended)","supportedEffortLevels":["low","medium","high","xhigh","max"]},
              {"value":"opus","resolvedModel":"claude-opus-5-5","displayName":"Opus 5.5","supportedEffortLevels":["low","medium","high","xhigh","max"]},
              {"value":"sonnet","resolvedModel":"claude-sonnet-5-5","displayName":"Sonnet 5.5","supportedEffortLevels":["max","low","medium","high"]},
              {"value":"claude-opus-4-6","resolvedModel":"claude-opus-4-6","displayName":"Opus 4.6","supportedEffortLevels":["low","medium","high","max"]},
              {"value":"claude-haiku-4-5-20251001","resolvedModel":"claude-haiku-4-5-20251001","displayName":"Haiku 4.5"}
            ]}}}
            """;
        var l = CatalogoIa.LerClaudeInitialize(linha)!;
        Assert.Equal("app", l.Origem);
        Assert.Equal("claude-opus-5-5", l.ModeloDoApp);
        Assert.Equal(new[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-opus-4-6", "claude-haiku-4-5-20251001" }, l.Modelos.Select(m => m.Id));
        Assert.Equal(new[] { "low", "medium", "high", "max" }, l.Modelo("claude-sonnet-5-5")!.Niveis);
        Assert.DoesNotContain("xhigh", l.Modelo("claude-opus-4-6")!.Niveis);
        Assert.Empty(l.Modelo("claude-haiku-4-5-20251001")!.Niveis);   // sem níveis: só "Padrão"
        Assert.Null(CatalogoIa.LerClaudeInitialize("{\"type\":\"control_response\"}"));
        Assert.Null(CatalogoIa.LerClaudeInitialize("quebrado"));
        Assert.Equal("Opus 5.5", CatalogoIa.NomeClaude("claude-opus-5-5"));
        Assert.Equal("Haiku 4.5", CatalogoIa.NomeClaude("claude-haiku-4-5-20251001"));
        Assert.Equal("Opus 5", CatalogoIa.NomeClaude("claude-opus-5"));
    }

    [Fact]
    public void a_api_da_openai_mostra_so_as_duas_geracoes_mais_novas_com_os_niveis_da_api()
    {
        var json = """
            {"object":"list","data":[{"id":"gpt-6.1-sol"},{"id":"gpt-6.1-terra"},{"id":"gpt-6-astra"},{"id":"gpt-5.6-sol"},{"id":"gpt-6.1-sol-2026-10-01"},
              {"id":"gpt-6.1-audio"},{"id":"text-embedding-4"},{"id":"gpt-6.1-nova"},{"id":"dall-e-5"}]}
            """;
        var codex = new List<ModeloIa> { new("gpt-6.1-nova", "GPT-6.1 Nova", ["low", "medium", "ultra"], "low") };
        var m = CatalogoIa.LerOpenAiApi(json, codex);
        Assert.Equal(new[] { "gpt-6.1-nova", "gpt-6.1-sol", "gpt-6.1-terra", "gpt-6-astra" }, m.Select(x => x.Id));
        Assert.Equal(new[] { "none", "low", "medium", "high", "xhigh", "max" }, m.Single(x => x.Id == "gpt-6.1-terra").Niveis);
        var nova = m.Single(x => x.Id == "gpt-6.1-nova");
        Assert.Equal(new[] { "low", "medium" }, nova.Niveis);   // do Codex, sem "ultra" (nível só do Codex)
        Assert.Equal("low", nova.NivelPadrao);
        Assert.Equal("GPT-6.1 Nova", nova.Nome);
        Assert.Equal("GPT-6-Astra", m.Single(x => x.Id == "gpt-6-astra").Nome);
        Assert.Empty(CatalogoIa.LerOpenAiApi("{\"error\":{}}"));
    }

    [Fact]
    public void a_api_da_anthropic_diz_os_niveis_de_cada_modelo()
    {
        var json = """
            {"data":[
              {"id":"claude-opus-5-5","display_name":"Claude Opus 5.5","capabilities":{"effort":{"supported":true,"low":{"supported":true},"medium":{"supported":true},"high":{"supported":true},"xhigh":{"supported":true},"max":{"supported":true}}}},
              {"id":"claude-opus-4-6","display_name":"Claude Opus 4.6","capabilities":{"effort":{"supported":true,"low":{"supported":true},"medium":{"supported":true},"high":{"supported":true},"xhigh":{"supported":false},"max":{"supported":true}}}},
              {"id":"claude-haiku-4-5","display_name":"Claude Haiku 4.5","capabilities":{"effort":{"supported":false}}},
              {"id":"claude-velho"}
            ],"has_more":false}
            """;
        var m = CatalogoIa.LerAnthropicApi(json);
        Assert.Equal(4, m.Count);
        Assert.Equal(new[] { "low", "medium", "high", "xhigh", "max" }, m[0].Niveis);
        Assert.Equal(new[] { "low", "medium", "high", "max" }, m[1].Niveis);
        Assert.Empty(m[2].Niveis);
        Assert.Empty(m[3].Niveis);
        Assert.Equal("claude-velho", m[3].Nome);
    }

    [Fact]
    public void as_listas_guardadas_voltam_iguais_e_cache_quebrado_e_ignorado()
    {
        var lidaEm = new DateTimeOffset(2026, 11, 17, 13, 0, 0, TimeSpan.Zero);
        var listas = new Dictionary<FonteIa, ListaModelos>
        {
            [FonteIa.Codex] = new(CatalogoIa.LerCodex(CatalogoCodex), "app", lidaEm, "gpt-6.1-sol", "low"),
            [FonteIa.ClaudeCode] = CatalogoIa.Reserva(FonteIa.ClaudeCode),
        };
        var volta = CatalogoIa.LerCache(CatalogoIa.SerializarCache(listas));
        Assert.Equal(2, volta.Count);
        Assert.Equal("guardada", volta[FonteIa.Codex].Origem);
        Assert.Equal(lidaEm, volta[FonteIa.Codex].LidaEm);
        Assert.Equal("gpt-6.1-sol", volta[FonteIa.Codex].ModeloDoApp);
        Assert.Equal("low", volta[FonteIa.Codex].NivelDoApp);
        Assert.Equal(listas[FonteIa.Codex].Modelos.Select(m => (m.Id, m.Nome, string.Join(',', m.Niveis), m.NivelPadrao)),
            volta[FonteIa.Codex].Modelos.Select(m => (m.Id, m.Nome, string.Join(',', m.Niveis), m.NivelPadrao)));
        Assert.Empty(CatalogoIa.LerCache("{ quebrado"));
        Assert.Empty(CatalogoIa.LerCache(null));
        // Id que não pode ir na linha de comando não volta do cache.
        Assert.Empty(CatalogoIa.LerCache("{\"listas\":{\"codex\":{\"modelos\":[{\"id\":\"x --yolo\"}]}}}"));
    }

    [Fact]
    public void os_niveis_tem_nomes_em_portugues()
    {
        Assert.Equal(new[] { "Mínimo", "Baixo", "Médio", "Alto", "Muito alto", "Máximo" },
            new[] { "minimal", "low", "medium", "high", "xhigh", "max" }.Select(CatalogoIa.NomeNivel));
        Assert.Equal("Padrão", CatalogoIa.NomeNivel(""));
        Assert.Equal("Sem raciocínio", CatalogoIa.NomeNivel("none"));
    }

    // ---------- a escolha ----------

    [Fact]
    public void escolha_por_mim_e_o_comportamento_de_antes_codex_senao_claude_code()
    {
        Assert.Equal((FonteIa.Codex, false), EscolhaIa.Resolver(EscolhaIa.Automatico, _ => true));
        Assert.Equal((FonteIa.ClaudeCode, false), EscolhaIa.Resolver(EscolhaIa.Automatico, f => f != FonteIa.Codex));
        // As APIs nunca entram sozinhas.
        Assert.Equal(((FonteIa?)null, false), EscolhaIa.Resolver(EscolhaIa.Automatico, f => FontesIa.PorChave(f)));
        Assert.Equal((FonteIa.Codex, false), EscolhaIa.Resolver("valor estranho", _ => true));
    }

    [Fact]
    public void a_ia_escolhida_responde_e_sem_ela_vale_a_ordem_de_reserva()
    {
        Assert.Equal((FonteIa.AnthropicApi, false), EscolhaIa.Resolver("claude_api", _ => true));
        Assert.Equal((FonteIa.ClaudeCode, false), EscolhaIa.Resolver("claude_code", _ => true));
        Assert.Equal((FonteIa.Codex, true), EscolhaIa.Resolver("openai_api", f => f != FonteIa.OpenAiApi));
        Assert.Equal((FonteIa.ClaudeCode, true), EscolhaIa.Resolver("codex", f => f == FonteIa.ClaudeCode));
        Assert.Equal(((FonteIa?)null, true), EscolhaIa.Resolver("codex", _ => false));
        Assert.Equal(new[] { "auto", "codex", "claude_code", "openai_api", "claude_api" }, EscolhaIa.Opcoes);
        foreach (var f in FontesIa.Todas) Assert.Equal(f, FontesIa.DoValor(FontesIa.Valor(f)));
        Assert.Equal(Provedor.ChatGpt, FontesIa.Provedor(FonteIa.OpenAiApi));
        Assert.Equal(Provedor.Claude, FontesIa.Provedor(FonteIa.AnthropicApi));
    }

    [Fact]
    public void o_nivel_acompanha_o_que_o_modelo_aceita()
    {
        var terra = new ModeloIa("gpt-6.1-terra", "Terra", ["low", "medium"], "low");
        var semNiveis = new ModeloIa("claude-haiku-4-5", "Haiku", [], null);
        Assert.Equal("medium", EscolhaIa.AjustarNivel(terra, "medium"));
        Assert.Equal("low", EscolhaIa.AjustarNivel(terra, "xhigh"));   // não aceita: o padrão do modelo
        Assert.Equal("", EscolhaIa.AjustarNivel(semNiveis, "high"));
        Assert.Equal("", EscolhaIa.AjustarNivel(terra, ""));
        Assert.Equal("high", EscolhaIa.AjustarNivel(null, "high"));   // lista ainda não lida: fica o pedido
        Assert.Equal("", EscolhaIa.AjustarNivel(null, "turbo"));
    }

    [Fact]
    public void a_linha_de_comando_de_cada_programa()
    {
        Assert.Equal(new[] { "-m", "gpt-6.1-sol", "-c", "model_reasoning_effort=\"xhigh\"" }, EscolhaIa.Argumentos(FonteIa.Codex, new("gpt-6.1-sol", "xhigh")));
        Assert.Equal(new[] { "-m", "gpt-6.1-sol" }, EscolhaIa.Argumentos(FonteIa.OpenAiApi, new("gpt-6.1-sol", "")));
        Assert.Equal(new[] { "--model", "claude-sonnet-5-5", "--effort", "high" }, EscolhaIa.Argumentos(FonteIa.ClaudeCode, new("claude-sonnet-5-5", "high")));
        Assert.Equal(new[] { "--model", "claude-opus-5-5[1m]" }, EscolhaIa.Argumentos(FonteIa.AnthropicApi, new("claude-opus-5-5[1m]", "ultra")));
        Assert.Empty(EscolhaIa.Argumentos(FonteIa.ClaudeCode, ModeloEscolhido.DoApp));
        // Valor mexido à mão nunca vira opção na linha de comando.
        Assert.Empty(EscolhaIa.Argumentos(FonteIa.Codex, new("x --dangerously-bypass", "\"; rm")));
        Assert.Empty(EscolhaIa.Argumentos(FonteIa.Codex, new("-c", "low\"")));

        var lista = new ListaModelos(CatalogoIa.LerCodex(CatalogoCodex), "app");
        Assert.Equal("Codex: GPT-6.1 Terra, raciocínio baixo", EscolhaIa.Descricao(FonteIa.Codex, new("gpt-6.1-terra", "low"), lista));
        Assert.Equal("Claude Code: modelo padrão", EscolhaIa.Descricao(FonteIa.ClaudeCode, ModeloEscolhido.DoApp, null));
        Assert.Equal("Claude API: claude-x, raciocínio muito alto", EscolhaIa.Descricao(FonteIa.AnthropicApi, new("claude-x", "xhigh"), null));
    }

    [Fact]
    public void a_pergunta_ao_claude_code_leva_o_modelo_e_o_nivel()
    {
        var opcoes = new IaDespacho.OpcoesIa(SemArquivos: true, ArgumentosClaude: EscolhaIa.Argumentos(FonteIa.ClaudeCode, new("claude-haiku-5-5", "low")));
        var c = IaDespacho.Montar(@"C:\x\claude.exe", null, @"C:\vazia", comPasta: false, [], opcoes);
        Assert.False(c.Codex);
        Assert.Equal(new[] { "--model", "claude-haiku-5-5", "--effort", "low" }, c.Argumentos.TakeLast(4));
        Assert.Contains("Read Grep Glob", c.Argumentos[c.Argumentos.ToList().IndexOf("--disallowedTools") + 1]);
    }

    // ---------- ajustes.json ----------

    [Fact]
    public void a_escolha_antiga_do_terra_vira_o_modelo_do_codex()
    {
        using var pasta = Ambiente.PastaDeTeste("modelos-ia-migracao");
        var arquivo = Path.Combine(pasta, "ajustes.json");

        // Arquivo novo: o padrão é o de antes (Escolha por mim; Codex com Terra 6.1, baixo; o resto no padrão do app).
        var novo = new Ajustes();
        Assert.Equal("auto", novo.IaRevisaoFonte);
        Assert.Equal(new ModeloEscolhido("gpt-6.1-terra", "low"), novo.IaModelos[FonteIa.Codex]);
        Assert.Equal(ModeloEscolhido.DoApp, novo.IaModelos[FonteIa.ClaudeCode]);

        File.WriteAllText(arquivo, "{\"ia_ajuste_modelo\":\"gpt-6.1-sol\",\"ia_ajuste_esforco\":\"high\"}");
        Assert.Equal(new ModeloEscolhido("gpt-6.1-sol", "high"), Ajustes.Carregar(pasta).IaModelos[FonteIa.Codex]);
        File.WriteAllText(arquivo, "{\"ia_ajuste_modelo\":\"codex\",\"ia_ajuste_esforco\":\"low\"}");
        Assert.Equal(ModeloEscolhido.DoApp, Ajustes.Carregar(pasta).IaModelos[FonteIa.Codex]);   // "o que estiver no Codex"
        // Com a chave nova, a antiga não manda mais.
        File.WriteAllText(arquivo, "{\"ia_ajuste_modelo\":\"gpt-6.1-sol\",\"ia_modelos\":{\"codex\":{\"modelo\":\"gpt-6-luna\",\"nivel\":\"medium\"}}}");
        Assert.Equal(new ModeloEscolhido("gpt-6-luna", "medium"), Ajustes.Carregar(pasta).IaModelos[FonteIa.Codex]);
    }

    [Fact]
    public void a_fonte_e_os_modelos_sao_guardados_sem_chave_nenhuma()
    {
        using var pasta = Ambiente.PastaDeTeste("modelos-ia-gravacao");
        var a = new Ajustes { IaRevisaoFonte = "claude_api" };
        a.IaModelos[FonteIa.AnthropicApi] = new("claude-opus-5-5", "xhigh");
        a.IaModelos[FonteIa.ClaudeCode] = new("claude-sonnet-5-5", "");
        a.Salvar(pasta);
        var texto = File.ReadAllText(Path.Combine(pasta, "ajustes.json"));
        Assert.Contains("\"ia_revisao_fonte\": \"claude_api\"", texto);
        Assert.DoesNotContain("sk-", texto);
        Assert.DoesNotContain("api_key", texto, StringComparison.OrdinalIgnoreCase);

        var b = Ajustes.Carregar(pasta);
        Assert.Equal("claude_api", b.IaRevisaoFonte);
        Assert.Equal(new ModeloEscolhido("claude-opus-5-5", "xhigh"), b.IaModelos[FonteIa.AnthropicApi]);
        Assert.Equal(new ModeloEscolhido("claude-sonnet-5-5", ""), b.IaModelos[FonteIa.ClaudeCode]);
        Assert.Equal(new ModeloEscolhido("gpt-6.1-terra", "low"), b.IaModelos[FonteIa.Codex]);

        // Valores mexidos à mão: fonte desconhecida volta a "Escolha por mim"; modelo ou nível inválido fica vazio (padrão do app).
        File.WriteAllText(Path.Combine(pasta, "ajustes.json"),
            "{\"ia_revisao_fonte\":\"gemini\",\"ia_modelos\":{\"claude_code\":{\"modelo\":\"x --y\",\"nivel\":\"turbo\"},\"openai_api\":5}}");
        var c = Ajustes.Carregar(pasta);
        Assert.Equal("auto", c.IaRevisaoFonte);
        Assert.Equal(ModeloEscolhido.DoApp, c.IaModelos[FonteIa.ClaudeCode]);
        Assert.Equal(ModeloEscolhido.DoApp, c.IaModelos[FonteIa.OpenAiApi]);

        b.RestaurarPadrao();
        Assert.Equal("auto", b.IaRevisaoFonte);
        Assert.Equal(ModeloEscolhido.DoApp, b.IaModelos[FonteIa.AnthropicApi]);
        Assert.Equal(new ModeloEscolhido("gpt-6.1-terra", "low"), b.IaModelos[FonteIa.Codex]);
    }
}
