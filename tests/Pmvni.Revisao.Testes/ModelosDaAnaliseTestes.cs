using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Pmvni.Agentes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Tarefa 19: Ajustes › Inteligência artificial › Modelos da análise. O padrão é a tabela do kit (<c>modelos-por-rota.json</c>, lida no kit de
/// <c>PMVNI_KIT_RAIZ</c>); o ajuste local só guarda o que muda; a tabela efetiva vai ao controlador, que a lê de verdade (Python do kit).</summary>
public class ModelosDaAnaliseTestes
{
    [Fact]
    public void ReconferenciaEconomicaDependeDaEscolhaLocalEDoSuporteDoKit()
    {
        var kit = Kit();
        kit.Bruto.Remove("reconferencia_economica");
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), kit);
        var ajuste = ModelosDaAnalise.LerAjuste("""{"reconferencia_economica":true}""");
        Assert.True(ModelosDaAnalise.LerAjuste(ModelosDaAnalise.SerializarAjuste(ajuste)).ReconferenciaEconomica);
        Assert.Null(ModelosDaAnalise.Efetiva(kit, ajuste, etapas, Listas()).Json["reconferencia_economica"]);
        kit.Bruto["reconferencia_economica"] = false;
        Assert.Null(ModelosDaAnalise.Efetiva(kit, new(), etapas, Listas()).Json["reconferencia_economica"]);
        var efetiva = ModelosDaAnalise.Efetiva(kit, ajuste, etapas, Listas());
        foreach (var rota in new[] { "codex", "claude" })
            Assert.True(JsonNode.Parse(efetiva.ParaExecucao(rota).Json)!["reconferencia_economica"]!.GetValue<bool>());
        Assert.True(ModelosDaAnalise.SemOPublicado(ajuste).ReconferenciaEconomica);
        Assert.False(ModelosDaAnalise.TabelaParaPublicar(kit, ajuste).Contains("\"reconferencia_economica\": true", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("""{"rotas":{"codex":{"perfis":{"padrao":{"etapas":{"analyze":{"modelo":"gpt-6.2-sol","nivel":"medio"},"technical_audit":{"modelo":"gpt-6.2-sol","nivel":false}}}}}}}""")]
    [InlineData("""{"regras":{"desce_com_cota_abaixo_de":true}}""")]
    [InlineData("""{"ligada_por_rota":{"audit":{"claude":"sim"}}}""")]
    public void AjusteInvalidoERecusadoIntegralmente(string conteudo)
    {
        using var pasta = new PastaTemporaria();
        Assert.True(ModelosDaAnalise.LerArquivoLocal(pasta.Caminho).Vazio);
        var arquivo = ModelosDaAnalise.ArquivoLocal(pasta.Caminho);
        File.WriteAllText(arquivo, conteudo);
        Assert.Throws<InvalidDataException>(() => ModelosDaAnalise.LerArquivoLocal(pasta.Caminho));
        Assert.Equal(conteudo, File.ReadAllText(arquivo));
        File.WriteAllText(arquivo, """{"regras":{"desce_com_cota_abaixo_de":0.15}}""");
        var lido = ModelosDaAnalise.LerArquivoLocal(pasta.Caminho);
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var tabela = ModelosDaAnalise.Efetiva(k, lido, etapas, Listas());
        Assert.Equal(0.15, tabela.Json["cota_baixa_da_execucao"]!.GetValue<double>());
    }

    static string TabelaDoKit() => File.ReadAllText(Ambiente.Corpus("modelos-legados/modelos-por-rota.json"));
    static string EtapasDoKit() => File.ReadAllText(Ambiente.Corpus("modelos-legados/etapas.json"));

    static TabelaModelosKit Kit() => ModelosDaAnalise.LerTabela(TabelaDoKit()) ?? throw new InvalidOperationException("tabela do kit ilegível");

    static ColunaModelos Coluna(TabelaModelosKit k, string rota, string perfil = "padrao") => k.Colunas.Single(c => c.Rota == rota && c.Perfil == perfil);

    static ModeloIa M(string id, params string[] niveis) => new(id, id, niveis.Length == 0 ? ["low", "medium", "high"] : niveis, null);

    static ListaDaRota Lista(params ModeloIa[] modelos) => new(modelos, Conferida: true, "do app (teste)");

    /// <summary>As listas que "o provedor devolve": Codex com os GPT do kit, Claude com Opus/Sonnet.</summary>
    static Func<ColunaModelos, ListaDaRota?> Listas(ListaDaRota? codex = null, ListaDaRota? claude = null, ListaDaRota? gemini = null) => c => c.Rota switch
    {
        "codex" => codex ?? Lista(M("gpt-6.2-sol"), M("gpt-6.2-astra"), M("gpt-6.1-terra")),
        "claude" => claude ?? Lista(M("claude-opus-5-5"), M("claude-sonnet-5-5")),
        _ => gemini,
    };

    static ListaDaRota ListaDe(TabelaModelosKit k, string rota, string perfil = "padrao") => Listas()(Coluna(k, rota, perfil))!;

    static JsonObject CelulaJson(JsonObject t, string rota, string perfil, string etapa) => t["rotas"]![rota]!["perfis"]![perfil]!["etapas"]![etapa]!.AsObject();

    /// <summary>A célula do padrão do kit lida direto do JSON (os modelos e níveis mudam com o kit; o teste não os fixa).</summary>
    static CelulaModelos CelulaBruta(string rota, string perfil, string etapa)
    {
        var c = CelulaJson(JsonNode.Parse(TabelaDoKit())!.AsObject(), rota, perfil, etapa);
        return new(c["modelo"]!.GetValue<string>(), c["nivel"]!.GetValue<string>());
    }

    /// <summary>Uma célula válida diferente do padrão do kit (outro modelo da lista do provedor, mesmo nível), para ajustar.</summary>
    static CelulaModelos Outra(TabelaModelosKit k, string rota, string etapa)
    {
        var padrao = k.Celula(Coluna(k, rota), etapa)!;
        return new(ListaDe(k, rota).Modelos.First(m => m.Id != padrao.Modelo).Id, padrao.Nivel);
    }

    [Fact]
    public void tabela_padrao_reproduz_os_modelos_e_niveis_atuais_das_tres_rotas()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var deIa = etapas.Where(e => !e.Programa).Select(e => e.Id).ToList();
        Assert.Equal(["analyze", "technical_audit", "autocorrecao"], deIa);   // fluxo linear (etapas.json versão 2): o resto é programa
        foreach (var rota in new[] { "codex", "claude" })
            foreach (var etapa in deIa) Assert.Equal(CelulaBruta(rota, "padrao", etapa), k.Celula(Coluna(k, rota), etapa));
        // As etapas de programa (preparo, auditoria cruzada, produção, revisão final): nenhuma rota nem perfil tem célula de modelo para elas.
        Assert.DoesNotContain(k.Celulas.Keys, c => !deIa.Contains(c.Etapa));
        Assert.All(etapas.Where(e => e.Id is "prepare" or "audit" or "produce" or "final_review"), e => Assert.True(e.Programa));
        // Gemini: igual à cópia tipada do Editor (MatrizGemini, a matriz de antes), perfil por perfil.
        foreach (var perfil in Enum.GetValues<PerfilGemini>())
            foreach (var e in MatrizGemini.Etapas(perfil))
            {
                var c = k.Celula(Coluna(k, "gemini", MatrizGemini.Codigo(perfil)), e.Etapa)!;
                Assert.Equal((e.Modelo, e.Esforco), (c.Modelo, k.Equivalencia[c.Nivel]));
            }
        Assert.Equal(6, k.Colunas.Count);   // ChatGPT, Claude e os quatro perfis do Gemini
        var regras = JsonNode.Parse(TabelaDoKit())!["regras"]!;
        Assert.Equal(regras["sobe_por_objecao_tecnica"]!.GetValue<bool>(), k.SobePorObjecao);
        Assert.Equal(regras["desce_com_cota_abaixo_de"]?.GetValue<double>(), k.DescePorCota);
        // Sem ajuste, a tabela efetiva repete o padrão (nenhuma célula muda, nenhum aviso).
        var efetiva = ModelosDaAnalise.Efetiva(k, new AjusteModelos(), etapas, Listas());
        Assert.Empty(efetiva.Avisos);
        Assert.Equal(k.Celula(Coluna(k, "codex"), "analyze")!.Modelo, CelulaJson(efetiva.Json, "codex", "padrao", "analyze")["modelo"]!.GetValue<string>());
    }

    /// <summary>Revisão final I4: a partida não espera a lista do provedor (que roda em segundo plano) e só troca a célula do ajuste pelo padrão do kit
    /// quando a última lista conferida já sabe que o modelo não existe.</summary>
    [Fact]
    public async Task partida_usa_a_lista_guardada_e_atualiza_em_segundo_plano()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var a = new AjusteModelos();
        var comFable = Lista(M("claude-opus-5-5"), M("claude-sonnet-5-5"), M("claude-fable-5-2"));
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "claude"), etapas.Single(e => e.Id == "technical_audit"), "claude-fable-5-2", "alto", comFable));
        var nunca = new TaskCompletionSource();   // o provedor que não responde (o agy podia levar 60 s)
        var chamadas = 0;
        Task Atualizar() { chamadas++; return nunca.Task; }
        static string Analise(TabelaDeModelos t) => JsonNode.Parse(t.Json)!["rotas"]!["claude"]!["perfis"]!["padrao"]!["etapas"]!["technical_audit"]!["modelo"]!.GetValue<string>();

        // A lista ainda não foi lida (não conferida): o ajuste vale, sem aviso, e a partida volta na hora, com a atualização já disparada.
        var naoLida = ModelosDaAnalise.ParaPartida(k, a, etapas,
            Listas(claude: new ListaDaRota([M("claude-opus-5-5")], Conferida: false, "conhecida do editor")), "claude", Atualizar);
        Assert.Equal(1, chamadas);
        Assert.False(nunca.Task.IsCompleted);
        Assert.Empty(naoLida.Avisos);
        Assert.Equal("claude-fable-5-2", Analise(naoLida));

        // A última lista conferida ainda oferece o modelo: vale.
        Assert.Equal("claude-fable-5-2", Analise(ModelosDaAnalise.ParaPartida(k, a, etapas, Listas(claude: comFable), "claude", Atualizar)));

        // A lista guardada pode estar desatualizada. A conta decide e uma recusa pede escolha ao analista.
        var sabido = ModelosDaAnalise.ParaPartida(k, a, etapas, Listas(), "claude", Atualizar);
        Assert.Equal("claude-fable-5-2", Analise(sabido));
        Assert.Contains(sabido.Avisos, x => x.Contains("claude-fable-5-2 não aparece mais", StringComparison.Ordinal));
        Assert.Equal(3, chamadas);

        // Falha da atualização em segundo plano vai para o registro, sem quebrar a partida (síncrona ou assíncrona).
        var erros = new List<Exception>();
        ModelosDaAnalise.ParaPartida(k, a, etapas, Listas(), "claude", () => throw new IOException("agy ausente"), erros.Add);
        var falhou = new TaskCompletionSource();
        ModelosDaAnalise.ParaPartida(k, a, etapas, Listas(), "claude", () => falhou.Task, erros.Add);
        falhou.SetException(new TimeoutException("sem resposta"));
        await Task.Delay(50);
        Assert.Collection(erros, e => Assert.IsType<IOException>(e), e => Assert.IsType<TimeoutException>(e));
        // Sem atualização (fonte indisponível): só monta a tabela.
        Assert.Equal("claude-fable-5-2", Analise(ModelosDaAnalise.ParaPartida(k, a, etapas, Listas(), "claude", null)));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("claude")]
    public void opcao_antiga_de_analise_nao_substitui_o_perfil_por_tarefa(string rota)
    {
        var kit = Kit();
        var coluna = Coluna(kit, rota);
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), kit);
        var ajuste = new AjusteModelos();
        ajuste.Celulas[(rota, "padrao", "analyze")] = new("modelo-anterior", "alto");
        var partida = ModelosDaAnalise.ParaPartida(kit, ajuste, etapas, Listas(), rota, null);
        var json = JsonNode.Parse(partida.Json)!.AsObject();
        Assert.Equal(kit.Celula(coluna, "analyze")!.Modelo, CelulaJson(json, rota, "padrao", "analyze")["modelo"]!.GetValue<string>());
        Assert.DoesNotContain("modelo-anterior", ModelosDaAnalise.TabelaParaPublicar(kit, ajuste));
    }

    [Theory]
    [InlineData("codex", "ultra")]
    [InlineData("claude", "max")]
    public void esforco_da_auditoria_respeita_rota_e_catalogo_e_sobrevive_ao_salvamento(string rota, string esforco)
    {
        var bruto = JsonNode.Parse(TabelaDoKit())!.AsObject();
        bruto["niveis_auditoria_por_rota"] = JsonNode.Parse("""{"codex":["baixo","medio","alto","xhigh","max","ultra"],"claude":["baixo","medio","alto","xhigh","max"]}""");
        var kit = ModelosDaAnalise.LerTabela(bruto.ToJsonString())!;
        var col = Coluna(kit, rota);
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), kit);
        var audit = etapas.Single(e => e.Id == "technical_audit");
        var modelo = kit.Celula(col, audit.Id)!.Modelo;
        var lista = Lista(M(modelo, "medium", esforco));
        var ajuste = new AjusteModelos();
        Assert.Null(ModelosDaAnalise.Ajustar(ajuste, kit, col, audit, modelo, esforco, lista));
        Assert.NotNull(ModelosDaAnalise.Ajustar(ajuste, kit, col, audit, modelo, "xhigh", lista));
        using var pasta = new PastaTemporaria();
        ModelosDaAnalise.GravarAjuste(pasta.Caminho, ajuste);
        var salvo = ModelosDaAnalise.LerAjuste(File.ReadAllText(ModelosDaAnalise.ArquivoLocal(pasta.Caminho)));
        var efetiva = ModelosDaAnalise.Efetiva(kit, salvo, etapas, _ => lista);
        Assert.Equal(esforco, CelulaJson(efetiva.Json, rota, col.Perfil, audit.Id)["nivel"]!.GetValue<string>());
        var publicado = ModelosDaAnalise.LerTabela(ModelosDaAnalise.TabelaParaPublicar(kit, salvo))!;
        Assert.Equal(esforco, publicado.Celula(col, audit.Id)!.Nivel);
    }

    [Fact]
    public void partida_preserva_esforco_da_auditoria_quando_catalogo_omite_modelo_padrao()
    {
        var kit = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), kit);
        var ajuste = new AjusteModelos();
        var coluna = Coluna(kit, "codex");
        var auditoria = etapas.Single(e => e.Id == "technical_audit");
        var modelo = kit.Celula(coluna, auditoria.Id)!.Modelo;
        Assert.Null(ModelosDaAnalise.Ajustar(ajuste, kit, coluna, auditoria, modelo, "medio", Lista(M(modelo))));

        var partida = ModelosDaAnalise.ParaPartida(kit, ajuste, etapas,
            Listas(codex: Lista(M("outro-modelo"))), "codex", null);
        var celula = CelulaJson(JsonNode.Parse(partida.Json)!.AsObject(), "codex", "padrao", auditoria.Id);
        Assert.Equal(modelo, celula["modelo"]!.GetValue<string>());
        Assert.Equal("medio", celula["nivel"]!.GetValue<string>());
    }

    [Fact]
    public void as_etapas_vem_do_etapas_json_e_a_etapa_de_programa_nao_tem_modelo()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        Assert.Equal(["prepare", "analyze", "technical_audit", "autocorrecao", "audit", "produce", "final_review"], etapas.Select(e => e.Id));
        var audit = etapas.Single(e => e.Id == "audit");
        Assert.True(audit.Opcional);
        Assert.All(ModelosDaAnalise.Rotas, r => Assert.True(ModelosDaAnalise.Ligada(new AjusteModelos(), audit, r)));

        // Um etapas.json em que o preparo é de programa (a Tarefa 4): a grade mostra "programa" e não aceita modelo.
        var comPrograma = EtapasDoKit().Replace("\"executor\": \"ia\"", "\"executor\": \"programa\"", StringComparison.Ordinal);
        var prepare = ModelosDaAnalise.LerEtapas(comPrograma, k).First();
        Assert.True(prepare.Programa);
        var a = new AjusteModelos();
        Assert.Contains("programa", ModelosDaAnalise.Ajustar(a, k, Coluna(k, "codex"), prepare, "gpt-6.2-astra", "alto", Lista(M("gpt-6.2-astra"))));
        Assert.True(a.Vazio);
    }

    [Fact]
    public void modelo_novo_devolvido_pelo_provedor_aparece_como_opcao_sem_mudar_codigo()
    {
        var k = Kit();
        // O catálogo do app ChatGPT ganhou um modelo que nem o Editor nem o kit conhecem.
        var lido = CatalogoIa.LerCodex("""{"models":[{"slug":"gpt-6.2-sol","display_name":"GPT-6.2 Sol","visibility":"list","supported_reasoning_levels":[{"effort":"low"},{"effort":"medium"},{"effort":"high"}]},{"slug":"gpt-7-orion","display_name":"GPT-7 Orion","visibility":"list","supported_reasoning_levels":[{"effort":"medium"},{"effort":"high"}]}]}""");
        var lista = new ListaDaRota(lido, Conferida: true, "do app ChatGPT");
        var padrao = k.Celula(Coluna(k, "codex"), "technical_audit")!;
        var opcoes = ModelosDaAnalise.OpcoesDeModelo(lista, padrao, null);
        Assert.Contains(opcoes, o => o.Id == "gpt-7-orion" && o.Texto.Contains("GPT-7 Orion", StringComparison.Ordinal));
        Assert.Contains(opcoes, o => o.Id == padrao.Modelo && o.Texto.Contains("padrão do kit", StringComparison.Ordinal));   // o padrão fica, marcado
        // Os níveis do modelo novo vêm da própria lista: sem "baixo".
        Assert.Equal(["medio", "alto"], ModelosDaAnalise.NiveisDoModelo(k, lista, "gpt-7-orion"));
        var etapa = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k).Single(e => e.Id == "technical_audit");
        var a = new AjusteModelos();
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "codex"), etapa, "gpt-7-orion", "alto", lista));
        Assert.Equal(new CelulaModelos("gpt-7-orion", "alto"), a.Celulas[("codex", "padrao", "technical_audit")]);
    }

    [Fact]
    public void modelo_inexistente_na_lista_do_provedor_e_recusado_com_mensagem()
    {
        var k = Kit();
        var etapa = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k).Single(e => e.Id == "technical_audit");
        var a = new AjusteModelos();
        var recusa = ModelosDaAnalise.Ajustar(a, k, Coluna(k, "claude"), etapa, "claude-inexistente-9", "alto", Lista(M("claude-opus-5-5")));
        Assert.Equal("O modelo claude-inexistente-9 não está na lista do app (teste) (Claude). Atualize a lista ou escolha um modelo oferecido.", recusa);
        Assert.True(a.Vazio);
        // Nível que o modelo não aceita, e um modelo que a rota Gemini não conhece: também recusados.
        Assert.Contains("não aceita o nível baixo", ModelosDaAnalise.Ajustar(a, k, Coluna(k, "claude"), etapa, "claude-opus-5-5", "baixo", Lista(M("claude-opus-5-5", "medium", "high"))));
        Assert.Contains("não está na lista", ModelosDaAnalise.Ajustar(a, k, Coluna(k, "gemini", "conta_ultra"), etapa, "gemini-9-ultra", "alto", ModelosDaAnalise.ListaGemini(k)));
        Assert.Contains("não aceita o nível", ModelosDaAnalise.Ajustar(a, k, Coluna(k, "gemini", "conta_ultra"), etapa, "gemini-3.1-pro", "baixo", ModelosDaAnalise.ListaGemini(k)));
        Assert.Contains("inválido", ModelosDaAnalise.Ajustar(a, k, Coluna(k, "codex"), etapa, "gpt --sandbox x", "alto", Lista(M("gpt-6.2-astra"))));
        Assert.True(a.Vazio);
        // Com a lista conhecida do Editor (nunca lida do provedor), nada é recusado por faltar na lista.
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "claude"), etapa, "claude-novo-6", "alto", new ListaDaRota([M("claude-opus-5-5")], Conferida: false, "conhecida do editor")));
    }

    [Fact]
    public void lista_sem_o_modelo_configurado_avisa_e_preserva_a_escolha_ate_a_resposta_da_conta()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var a = new AjusteModelos();
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "claude"), etapas.Single(e => e.Id == "technical_audit"), "claude-fable-5-2", "alto",
            Lista(M("claude-opus-5-5"), M("claude-sonnet-5-5"), M("claude-fable-5-2"))));
        // Depois, a conta deixou de oferecer o Fable 5.2: a lista atualizada não tem mais o modelo.
        var efetiva = ModelosDaAnalise.Efetiva(k, a, etapas, Listas());
        var aviso = Assert.Single(efetiva.Avisos);
        Assert.Equal("claude", aviso.Rota);
        Assert.Equal("Claude, Auditoria técnica: o modelo claude-fable-5-2 não aparece mais na lista do app (teste). Se a conta recusar, escolha o substituto nesta execução.", aviso.Texto);
        Assert.Equal("claude-fable-5-2", CelulaJson(efetiva.Json, "claude", "padrao", "technical_audit")["modelo"]!.GetValue<string>());
        Assert.Equal("alto", CelulaJson(efetiva.Json, "claude", "padrao", "technical_audit")["nivel"]!.GetValue<string>());
        // O aviso vai junto da tabela para o histórico da execução (só os da rota que roda).
        var execucao = efetiva.ParaExecucao("claude");
        Assert.Equal([aviso.Texto], execucao.Avisos);
        Assert.Contains("claude-fable-5-2", JsonNode.Parse(execucao.Json)!["avisos"]![0]!.GetValue<string>());
        Assert.Empty(efetiva.ParaExecucao("codex").Avisos);
        // A aba mostra o mesmo aviso.
        Assert.Contains(ModelosDaAnalise.AvisosDaAba(k, a, etapas, Listas()), x => x.Texto == aviso.Texto);
        // Com a lista ainda oferecendo o modelo, ele vale e não há aviso.
        var comFable = ModelosDaAnalise.Efetiva(k, a, etapas, Listas(claude: Lista(M("claude-opus-5-5"), M("claude-sonnet-5-5"), M("claude-fable-5-2"))));
        Assert.Empty(comFable.Avisos);
        Assert.Equal("claude-fable-5-2", CelulaJson(comFable.Json, "claude", "padrao", "technical_audit")["modelo"]!.GetValue<string>());
    }

    [Fact]
    public void etapa_tecnica_abaixo_de_medio_avisa_sem_bloquear()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var a = new AjusteModelos();
        Assert.Empty(ModelosDaAnalise.AvisosDaAba(k, a, etapas, Listas()));
        var analise = etapas.Single(e => e.Id == "technical_audit");
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "codex"), analise, "gpt-6.2-astra", "baixo", ListaDe(k, "codex")));
        var aviso = Assert.Single(ModelosDaAnalise.AvisosDaAba(k, a, etapas, Listas()));
        Assert.Equal($"ChatGPT, {analise.Nome}: nível baixo numa etapa técnica (que decide mérito). É permitido, mas o recomendado é médio ou alto.", aviso.Texto);
    }

    [Fact]
    public void ajuste_local_muda_o_modelo_o_controlador_recebe_e_restaurar_volta_ao_padrao()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var a = new AjusteModelos();
        var analyze = etapas.Single(e => e.Id == "technical_audit");
        var padrao = k.Celula(Coluna(k, "codex"), "technical_audit")!;
        var outra = Outra(k, "codex", "technical_audit");
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "codex"), analyze, outra.Modelo, outra.Nivel, ListaDe(k, "codex")));
        ModelosDaAnalise.Ligar(a, etapas.Single(e => e.Id == "audit"), "claude", false);
        using var pasta = new PastaTemporaria();
        // O ajuste local fica em %APPDATA%\EditorRelatoriosPMVNI\modelos-por-rota.local.json (aqui, a pasta de dados do teste) e volta igual.
        ModelosDaAnalise.GravarAjuste(pasta.Caminho, a);
        var lido = ModelosDaAnalise.LerAjuste(File.ReadAllText(ModelosDaAnalise.ArquivoLocal(pasta.Caminho)));
        Assert.Equal(outra, Assert.Single(lido.Celulas).Value);
        Assert.False(ModelosDaAnalise.Ligada(lido, etapas.Single(e => e.Id == "audit"), "claude"));

        // A tabela efetiva desta execução, lida pelo kit de verdade (o controlador da rota codex e o módulo da tabela).
        var efetiva = ModelosDaAnalise.Efetiva(k, lido, etapas, Listas()).ParaExecucao("codex");
        var arquivo = Path.Combine(pasta.Caminho, TabelaDeModelos.NomeDoArquivo);
        File.WriteAllText(arquivo, efetiva.Json, new UTF8Encoding(false));
        // O Editor manda a célula ajustada no arquivo da execução...
        Assert.Equal(outra.Modelo, CelulaJson(JsonNode.Parse(efetiva.Json)!.AsObject(), "codex", "padrao", "technical_audit")["modelo"]!.GetValue<string>());
        Assert.Equal(outra.Nivel, CelulaJson(JsonNode.Parse(File.ReadAllText(arquivo))!.AsObject(), "codex", "padrao", "technical_audit")["nivel"]!.GetValue<string>());
        Assert.Equal(["analyze", "technical_audit", "autocorrecao"], JsonNode.Parse(efetiva.Json)!["rotas"]!["codex"]!["perfis"]!["padrao"]!["etapas"]!.AsObject().Select(x => x.Key));

        // Restaurar padrão do kit: o ajuste fica vazio, o arquivo local some e o controlador volta ao padrão.
        var restaurado = ModelosDaAnalise.Restaurar();
        ModelosDaAnalise.GravarAjuste(pasta.Caminho, restaurado);
        Assert.False(File.Exists(ModelosDaAnalise.ArquivoLocal(pasta.Caminho)));
        File.WriteAllText(arquivo, ModelosDaAnalise.Efetiva(k, restaurado, etapas, Listas()).ParaExecucao("codex").Json, new UTF8Encoding(false));
        Assert.Equal(padrao.Modelo, CelulaJson(JsonNode.Parse(File.ReadAllText(arquivo))!.AsObject(), "codex", "padrao", "technical_audit")["modelo"]!.GetValue<string>());
    }

    /// <summary>Um ajuste local antigo (de antes do analise 4.0.0) traz células de preparo e produção, que agora são de programa: a tabela efetiva não as leva, a aba e o
    /// histórico avisam que foram ignoradas, e publicar não é bloqueado por elas (o kit também as ignora).</summary>
    [Fact]
    public void ajuste_antigo_com_celulas_de_etapa_de_programa_e_ignorado_com_aviso()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var antigo = ModelosDaAnalise.LerAjuste("""{"versao":1,"rotas":{"codex":{"perfis":{"padrao":{"etapas":{"prepare":{"modelo":"gpt-6.2-sol","nivel":"medio"},"produce":{"modelo":"gpt-6.2-sol","nivel":"alto"},"analyze":{"modelo":"gpt-6.2-sol","nivel":"medio"}}}}}}}""");
        Assert.Equal(3, antigo.Celulas.Count);
        var efetiva = ModelosDaAnalise.Efetiva(k, antigo, etapas, Listas());
        var etapasDoJson = efetiva.Json["rotas"]!["codex"]!["perfis"]!["padrao"]!["etapas"]!.AsObject().Select(x => x.Key).ToList();
        Assert.Equal(["analyze"], etapasDoJson.Intersect(["analyze"]));
        Assert.DoesNotContain(etapasDoJson, id => id is "prepare" or "produce");   // sem preparo nem produção
        Assert.Equal("gpt-6-luna", CelulaJson(efetiva.Json, "codex", "padrao", "analyze")["modelo"]!.GetValue<string>());   // a célula de IA do mesmo ajuste vale
        Assert.Equal(2, efetiva.Avisos.Count);
        Assert.All(efetiva.Avisos, a => { Assert.Equal("codex", a.Rota); Assert.Contains("roda por programa", a.Texto); Assert.Contains("ignorado", a.Texto); });
        Assert.Contains(efetiva.Avisos, a => a.Texto.StartsWith("ChatGPT, Preparo:", StringComparison.Ordinal));
        Assert.Contains(efetiva.Avisos, a => a.Texto.StartsWith("ChatGPT, Produção do relatório:", StringComparison.Ordinal));
        Assert.Equal(2, ModelosDaAnalise.AvisosDaAba(k, antigo, etapas, Listas()).Count(a => a.Texto.Contains("roda por programa", StringComparison.Ordinal)));
        Assert.Equal(2, efetiva.ParaExecucao("codex").Avisos.Count);
        Assert.Empty(efetiva.ParaExecucao("claude").Avisos);
        // Publicar: as células de programa não bloqueiam nem entram na tabela do kit mestre.
        Assert.Empty(ModelosDaAnalise.ProblemasParaPublicar(k, antigo, etapas, Listas()));
        var publicada = JsonNode.Parse(ModelosDaAnalise.TabelaParaPublicar(k, antigo))!;
        Assert.DoesNotContain(publicada["rotas"]!["codex"]!["perfis"]!["padrao"]!["etapas"]!.AsObject().Select(x => x.Key), id => id is "prepare" or "produce");
        // Sem ajuste, nenhum aviso.
        Assert.Empty(ModelosDaAnalise.Efetiva(k, new AjusteModelos(), etapas, Listas()).Avisos);
    }

    /// <summary>Um kit mestre fictício (nunca o de verdade): a tabela atual do kit mestre, o instalador e a pasta dos scripts.</summary>
    static (string Mestre, string Destino, string Scripts) KitMestreFicticio(PastaTemporaria pasta, string tabela)
    {
        var mestre = Path.Combine(pasta.Caminho, "KIT MESTRE FICTICIO");
        var destino = ModelosDaAnalise.ArquivoNoKit(mestre);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllText(destino, tabela, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(mestre, ComponentesDoKit.InstaladorDoKit), "@echo off\r\n");
        return (mestre, destino, Path.Combine(pasta.Caminho, "scripts"));
    }

    /// <summary>A conferência do próprio kit (o modelos_por_rota.py do kit de PMVNI_KIT_RAIZ), pelo Python do computador.</summary>
    static string? ConferirNoKit(string arquivo) =>
        JsonNode.Parse(File.ReadAllText(arquivo))?["etapas_tecnicas"] is JsonArray ? null : "etapas_tecnicas deve ser uma lista";

    [Fact]
    public void publicar_parte_do_kit_mestre_confere_pelo_kit_grava_e_dispara_o_manter()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        // A revisão final e a auditoria cruzada agora são de programa (sem modelo): o ajuste publicado é numa etapa de IA.
        var a = new AjusteModelos { SobePorObjecao = !k.SobePorObjecao };
        var tecnica = Outra(k, "claude", "technical_audit");
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "claude"), etapas.Single(e => e.Id == "technical_audit"), tecnica.Modelo, tecnica.Nivel, ListaDe(k, "claude")));
        Assert.Contains("roda por programa", ModelosDaAnalise.Ajustar(a, k, Coluna(k, "claude"), etapas.Single(e => e.Id == "final_review"), "claude-sonnet-5-5", "medio", ListaDe(k, "claude")));
        // Um modelo Gemini que o provedor oferece e a tabela ainda não conhece: publicado, entra em rotas.gemini.modelos.
        var gemini = new ListaDaRota([M("gemini-3.8-flash"), M("gemini-5-nova", "high")], Conferida: true, "do Gemini (agy models)");
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "gemini", "conta_ultra"), etapas.Single(e => e.Id == "analyze"), "gemini-5-nova", "alto", gemini));
        using var pasta = new PastaTemporaria();
        // O kit mestre tem uma edição que ainda não foi instalada (o motivo da auditoria cruzada no ChatGPT): ela tem de ficar.
        var editada = JsonNode.Parse(TabelaDoKit())!.AsObject();
        editada["rotas"]!["codex"]!["perfis"]!["padrao"]!["etapas"]!["technical_audit"]!["motivo"] = "edição do kit mestre ainda não instalada";
        var (mestre, destino, scripts) = KitMestreFicticio(pasta, editada.ToJsonString());
        var lancados = new List<(string Exe, string Arquivo)>();
        var conferidos = new List<string>();

        var r = ModelosDaAnalise.Publicar(mestre, a, etapas, Listas(gemini: gemini), [], scripts,
            arq => { conferidos.Add(arq); return ConferirNoKit(arq); }, (exe, arq) => lancados.Add((exe, arq)));

        Assert.True(r.Publicada, r.Texto);
        Assert.StartsWith("Tabela publicada no kit mestre", r.Texto);
        Assert.Equal(destino + ".tmp", Assert.Single(conferidos));
        var texto = File.ReadAllText(destino, Encoding.UTF8);
        Assert.Null(ConferirNoKit(destino));                                    // o kit aceita a tabela publicada
        var publicada = ModelosDaAnalise.LerTabela(texto)!;
        Assert.Equal(tecnica, publicada.Celula(Coluna(publicada, "claude"), "technical_audit"));
        Assert.Null(publicada.Celula(Coluna(publicada, "claude"), "final_review"));   // etapa de programa: não entra na tabela publicada
        Assert.Equal(k.Celula(Coluna(k, "claude"), "analyze"), publicada.Celula(Coluna(publicada, "claude"), "analyze"));
        Assert.Equal(new CelulaModelos("gemini-5-nova", "alto"), publicada.Celula(Coluna(publicada, "gemini", "conta_ultra"), "analyze"));
        Assert.Equal(["alto"], publicada.ModelosGemini["gemini-5-nova"]);
        Assert.Equal(!k.SobePorObjecao, publicada.SobePorObjecao);
        Assert.Equal(JsonNode.Parse(TabelaDoKit())!["reparo"]?.ToJsonString(), JsonNode.Parse(texto)!["reparo"]?.ToJsonString());   // o nível do reparo do kit fica
        Assert.Equal("edição do kit mestre ainda não instalada", JsonNode.Parse(texto)!["rotas"]!["codex"]!["perfis"]!["padrao"]!["etapas"]!["technical_audit"]!["motivo"]!.GetValue<string>());
        Assert.DoesNotContain("\r\n", texto);   // LF, como o arquivo do kit
        var (exe, cmd) = Assert.Single(lancados);
        Assert.EndsWith("explorer.exe", exe, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(scripts, ModelosDaAnalise.CmdPublicar), cmd);
        Assert.Contains("call \"" + Path.Combine(mestre, ComponentesDoKit.InstaladorDoKit) + "\" manter", File.ReadAllText(cmd));

        // Depois de publicar, o ajuste local pode ser limpo: fica só o liga/desliga da audit (que não se publica).
        ModelosDaAnalise.Ligar(a, etapas.Single(e => e.Id == "audit"), "gemini", false);
        var limpo = ModelosDaAnalise.SemOPublicado(a);
        Assert.Empty(limpo.Celulas);
        Assert.Null(limpo.SobePorObjecao);
        Assert.False(ModelosDaAnalise.Ligada(limpo, etapas.Single(e => e.Id == "audit"), "gemini"));

        // Fora do computador do kit mestre, com análise em andamento ou com kit sem a tabela: nada é gravado nem aberto.
        lancados.Clear();
        string? Nunca(string _) => throw new InvalidOperationException("não devia conferir");
        Assert.Contains("não tem o kit mestre", ModelosDaAnalise.Publicar(null, a, etapas, Listas(), [], scripts, Nunca, (e, f) => lancados.Add((e, f))).Texto);
        Assert.Contains("andamento", ModelosDaAnalise.Publicar(mestre, a, etapas, Listas(), ["P-1"], scripts, Nunca, (e, f) => lancados.Add((e, f))).Texto);
        Assert.Contains("ainda não tem a tabela", ModelosDaAnalise.Publicar(Path.Combine(pasta.Caminho, "outro"), a, etapas, Listas(), [], scripts, Nunca, (e, f) => lancados.Add((e, f))).Texto);
        Assert.Empty(lancados);
    }

    [Fact]
    public void publicar_recusa_ajuste_antigo_invalido_e_tabela_que_o_kit_recusa_sem_mudar_o_kit_mestre()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        using var pasta = new PastaTemporaria();
        var (mestre, destino, scripts) = KitMestreFicticio(pasta, TabelaDoKit());
        var antes = File.ReadAllText(destino, Encoding.UTF8);
        var lancados = new List<(string Exe, string Arquivo)>();

        // Um ajuste antigo, gravado quando o provedor ainda oferecia o modelo (o arquivo local é lido como está).
        var antigo = ModelosDaAnalise.LerAjuste("""{"versao":1,"rotas":{"codex":{"perfis":{"padrao":{"etapas":{"technical_audit":{"modelo":"gpt-5-aposentado","nivel":"alto"}}}}},"gemini":{"perfis":{"perfil_que_saiu":{"etapas":{"technical_audit":{"modelo":"gemini-3.8-flash","nivel":"alto"}}}}}}}""");
        var r = ModelosDaAnalise.Publicar(mestre, antigo, etapas, Listas(), [], scripts, ConferirNoKit, (e, f) => lancados.Add((e, f)));
        Assert.False(r.Publicada);
        Assert.Contains("gpt-5-aposentado não aparece mais na lista", r.Texto);
        Assert.Contains("perfil_que_saiu", r.Texto);
        Assert.Equal(antes, File.ReadAllText(destino, Encoding.UTF8));

        // A tabela final que a conferência do próprio kit recusa (aqui, o kit mestre tem um erro): nada é trocado.
        var quebrada = JsonNode.Parse(TabelaDoKit())!.AsObject();
        quebrada["etapas_tecnicas"] = "technical_audit";
        File.WriteAllText(destino, quebrada.ToJsonString(), new UTF8Encoding(false));
        antes = File.ReadAllText(destino, Encoding.UTF8);
        var a = new AjusteModelos();
        var tecnica = Outra(k, "codex", "technical_audit");
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "codex"), etapas.Single(e => e.Id == "technical_audit"), tecnica.Modelo, tecnica.Nivel, ListaDe(k, "codex")));
        r = ModelosDaAnalise.Publicar(mestre, a, etapas, Listas(), [], scripts, ConferirNoKit, (e, f) => lancados.Add((e, f)));
        Assert.False(r.Publicada);
        Assert.Contains("a conferência do kit recusou a tabela (etapas_tecnicas deve ser uma lista", r.Texto);
        Assert.Equal(antes, File.ReadAllText(destino, Encoding.UTF8));
        Assert.False(File.Exists(destino + ".tmp"));
        Assert.Empty(lancados);
    }

    [Fact]
    public void lista_do_gemini_vem_do_provedor_pela_rota_do_kit()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        // A saída do google_runtime.py modelos (agy models): um modelo novo e um nível que a tabela ainda não conhece.
        var lida = ModelosDaAnalise.LerListaGemini("""{"ok":true,"modo":"conta","perfil":"conta_ultra","fonte":"agy models (Antigravity)","lista_do_provedor":true,"modelos":[{"modelo":"gemini-3.8-flash","niveis":["baixo","medio"]},{"modelo":"gemini-5-nova","niveis":["alto"]}]}""", k)!;
        Assert.True(lida.Conferida);
        Assert.Equal("do Gemini (agy models (Antigravity))", lida.Fonte);
        var col = Coluna(k, "gemini", "conta_ultra");
        var analyze = etapas.Single(e => e.Id == "analyze");
        var padrao = k.Celula(col, "analyze")!;
        Assert.Contains(ModelosDaAnalise.OpcoesDeModelo(lida, padrao, null), o => o.Id == "gemini-5-nova");
        Assert.Equal(["alto"], ModelosDaAnalise.NiveisDoModelo(k, lida, "gemini-5-nova"));
        var a = new AjusteModelos();
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, col, analyze, "gemini-5-nova", "alto", lida));
        Assert.Contains("não está na lista", ModelosDaAnalise.Ajustar(a, k, col, analyze, "gemini-9-ultra", "alto", lida));
        Assert.Equal("api", ModelosDaAnalise.GrupoGemini("api_gratuita"));
        Assert.Equal("web", ModelosDaAnalise.GrupoGemini("conta_gemini"));

        // O modelo sai da lista do provedor: a etapa usa o padrão do kit, com aviso só da rota Gemini no arquivo da execução.
        var semNova = ModelosDaAnalise.LerListaGemini("""{"lista_do_provedor":true,"fonte":"agy models (Antigravity)","modelos":[{"modelo":"gemini-3.8-flash","niveis":["baixo","medio"]}]}""", k)!;
        // A lista do agy vale para o perfil da Conta Antigravity (os outros perfis Gemini têm a lista deles).
        Func<ColunaModelos, ListaDaRota?> SoAntigravity(ListaDaRota l) => c => c.Rota == "gemini" ? (c.Perfil == "conta_ultra" ? l : null) : Listas()(c);
        var t = ModelosDaAnalise.Efetiva(k, a, etapas, SoAntigravity(semNova));
        Assert.Equal("gemini-5-nova", CelulaJson(t.Json, "gemini", "conta_ultra", "analyze")["modelo"]!.GetValue<string>());
        var execucao = t.ParaExecucao("gemini");
        Assert.Contains("gemini-5-nova não aparece mais na lista do Gemini", Assert.Single(execucao.Avisos));
        Assert.Single(JsonNode.Parse(execucao.Json)!["avisos"]!.AsArray());
        Assert.Empty(JsonNode.Parse(t.ParaExecucao("codex").Json)!["avisos"]!.AsArray());   // o arquivo da rota ChatGPT não leva aviso do Gemini
        // As etapas Gemini efetivas do perfil, para o "Testar" do cartão Gemini.
        Assert.Contains(ModelosDaAnalise.EtapasGemini(ModelosDaAnalise.Efetiva(k, a, etapas, SoAntigravity(lida)), k, "conta_ultra"),
            e => e == new EtapaGemini("analyze", "gemini-5-nova", "high"));
        // Gemini Web: sem lista do provedor, os modelos da tabela (só esses valem) e a fonte diz isso.
        var web = ModelosDaAnalise.LerListaGemini("""{"lista_do_provedor":false,"modelos":[{"modelo":"gemini-4-hydrogen","niveis":["medio","alto"]}]}""", k)!;
        Assert.Contains("não lista modelos", web.Fonte);
        Assert.Contains("não lista modelos", ModelosDaAnalise.ListaGemini(k).Fonte);
        Assert.Null(ModelosDaAnalise.LerListaGemini("{ não é json", k));
    }

    [Fact]
    public async Task modelos_do_google_desativados_preservam_a_tabela_historica()
    {
        var historica = JsonNode.Parse(TabelaDoKit())!.AsObject();
        var ativa = ModelosDaAnalise.LerTabela(historica.ToJsonString(), apenasAtivas: true)!;
        Assert.Equal(new[] { "codex", "claude" }, ativa.Colunas.Select(c => c.Rota).Distinct());
        var anterior = new AjusteModelos();
        anterior.Celulas[("gemini", "conta_ultra", "analyze")] = new("legado", "medio");
        anterior.Ligadas[("audit", "gemini")] = false;
        anterior.Celulas[("codex", "padrao", "analyze")] = new("atual", "alto");
        var ativos = ModelosDaAnalise.AjustesAtivos(anterior);
        Assert.Single(ativos.Celulas);
        Assert.Empty(ativos.Ligadas);
        var preservados = ModelosDaAnalise.PreservarRotasInativas(anterior, ModelosDaAnalise.Restaurar());
        Assert.Equal("legado", Assert.Single(preservados.Celulas).Value.Modelo);
        Assert.False(Assert.Single(preservados.Ligadas).Value);
        historica["rotas"]!.AsObject().Remove("gemini");
        Assert.NotNull(ModelosDaAnalise.LerTabela(historica.ToJsonString(), apenasAtivas: true));
        var pecas = new Pecas(null, null, "nao-executar", false, null, null, null, false, null, true) { GoogleRuntime = "nao-executar" };
        foreach (var perfil in Enum.GetValues<PerfilGemini>())
        {
            var (json, falha) = await ModelosDoGemini.LerAsync(pecas, perfil, null, ".", CancellationToken.None);
            Assert.Null(json);
            Assert.Equal(Provedores.RotaDesativada, falha);
        }
    }

    [Fact]
    public void regra_de_objecao_diz_as_rotas_em_que_vale()
    {
        // A regra geral vale nas rotas sem exceção que a desligue (como regras() do modelos_por_rota.py do kit: a exceção da rota por cima da geral).
        var k = Kit();
        var excecoes = JsonNode.Parse(TabelaDoKit())!["regras"]?["excecoes_por_rota"] as JsonObject;
        Assert.Equal(ModelosDaAnalise.Rotas.Where(r => excecoes?[r]?["sobe_por_objecao_tecnica"]?.GetValue<bool>() ?? true), k.RotasQueSobem);
        // Fixture: a tabela com claude e gemini fora da regra (as exceções do kit anterior).
        var t = JsonNode.Parse(TabelaDoKit())!.AsObject();
        t["regras"]!["excecoes_por_rota"] = JsonNode.Parse("""{"claude":{"sobe_por_objecao_tecnica":false},"gemini":{"sobe_por_objecao_tecnica":false,"nota":"Gemini: nada muda."}}""");
        var comExcecoes = ModelosDaAnalise.LerTabela(t.ToJsonString())!;
        Assert.Equal(["codex"], comExcecoes.RotasQueSobem);
        Assert.Equal("Gemini: nada muda.", comExcecoes.NotasDasRotas["gemini"]);
        // Sem exceções, a regra (quando ligada) vale nas três rotas.
        t["regras"]!["excecoes_por_rota"] = new JsonObject();
        Assert.Equal(ModelosDaAnalise.Rotas, ModelosDaAnalise.LerTabela(t.ToJsonString())!.RotasQueSobem);
    }

    [Fact]
    public void kit_mestre_so_no_computador_do_instalador_com_kit_local()
    {
        using var pasta = new PastaTemporaria();
        var pmvni = Path.Combine(pasta.Caminho, "PMVNI");
        var codex = Path.Combine(pasta.Caminho, "codex");
        Directory.CreateDirectory(pmvni);
        Directory.CreateDirectory(codex);
        var o = new OrigensDoKit(codex, null, pasta.Caminho, pmvni, Path.Combine(pasta.Caminho, "KIT"), null);
        Assert.Null(ComponentesDoKit.KitMestre(o));
        File.WriteAllText(Path.Combine(pmvni, "instalacao.json"), "{\"modo_kit\":\"copia\",\"kit_root\":\"C:\\\\kit-copia\"}");
        Assert.Null(ComponentesDoKit.KitMestre(o));
        File.WriteAllText(Path.Combine(pmvni, "instalacao.json"), "{\"modo_kit\":\"local\",\"kit_root\":\"C:\\\\kit-mestre-ficticio\"}");
        Assert.Equal(@"C:\kit-mestre-ficticio", ComponentesDoKit.KitMestre(o));
    }

    [Fact]
    public void DetalhesLeemAPastaDaExecucaoInclusiveAposEscolhaLocal()
    {
        using var pasta = new PastaTemporaria();
        var arquivo = Path.Combine(pasta.Caminho, Pmvni.Agentes.TabelaDeModelos.NomeDoArquivo);
        string Ler() => EscolhaModelosDaExecucao.ResumoConfigurado(pasta.Caminho, Pmvni.Agentes.Provedor.ChatGpt, null, "codex-lb");
        Assert.Contains("indisponível", Ler());
        var tabela = JsonNode.Parse("""{"rotas":{"codex":{"perfis":{"padrao":{"etapas":{"analyze":{"modelo":"gpt-a","nivel":"baixo"}}}}}},"reparo_julgamento_previsto":{"modelo":"gpt-b","nivel":"medio"}}""")!;
        File.WriteAllText(arquivo, tabela.ToJsonString());
        Assert.Contains("gpt-a · esforço Baixo", Ler());
        Assert.Contains("gpt-b · esforço Médio", Ler());
        Assert.Contains("Luna 6", Ler());
        tabela["preparo_codex"] = new JsonObject { ["modelo"] = "gpt-6.1-terra", ["nivel"] = "medio" };
        tabela["reparo_julgamento_da_execucao"] = new JsonObject { ["modelo"] = "gpt-c", ["nivel"] = "alto" };
        File.WriteAllText(arquivo, tabela.ToJsonString());
        Assert.Contains("gpt-c · esforço Alto", Ler());
        Assert.DoesNotContain("gpt-b", Ler());
        Assert.Contains("Preparo · todos os documentos, plantas e imagens · gpt-6.1-terra · esforço Médio", Ler());
        Assert.DoesNotContain("Luna 6", Ler());
    }

    sealed class PastaTemporaria : IDisposable
    {
        public string Caminho { get; } = Directory.CreateTempSubdirectory("pmvni-modelos-").FullName;

        public void Dispose()
        {
            try { Directory.Delete(Caminho, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
