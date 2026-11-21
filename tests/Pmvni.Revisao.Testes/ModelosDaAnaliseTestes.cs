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
    static string TabelaDoKit()
    {
        var arquivo = ModelosDaAnalise.ArquivoNoKit(Ambiente.RaizKit);
        if (!File.Exists(arquivo)) Assert.Fail("Tabela de modelos ausente do kit: " + arquivo + " (defina PMVNI_KIT_RAIZ com o kit de desenvolvimento)");
        return File.ReadAllText(arquivo, Encoding.UTF8);
    }

    static string EtapasDoKit() =>
        File.ReadAllText(Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins", "analise", "skills", "analise", "references", "etapas.json"), Encoding.UTF8);

    static TabelaModelosKit Kit() => ModelosDaAnalise.LerTabela(TabelaDoKit()) ?? throw new InvalidOperationException("tabela do kit ilegível");

    static ColunaModelos Coluna(TabelaModelosKit k, string rota, string perfil = "padrao") => k.Colunas.Single(c => c.Rota == rota && c.Perfil == perfil);

    static ModeloIa M(string id, params string[] niveis) => new(id, id, niveis.Length == 0 ? ["low", "medium", "high"] : niveis, null);

    static ListaDaRota Lista(params ModeloIa[] modelos) => new(modelos, Conferida: true, "do app (teste)");

    /// <summary>As listas que "o provedor devolve": Codex com os GPT do kit, Claude com Opus/Sonnet.</summary>
    static Func<string, ListaDaRota?> Listas(ListaDaRota? codex = null, ListaDaRota? claude = null) => rota => rota switch
    {
        "codex" => codex ?? Lista(M("gpt-6.2-sol"), M("gpt-6.2-astra")),
        "claude" => claude ?? Lista(M("claude-opus-5-5"), M("claude-sonnet-5-5")),
        _ => null,
    };

    static JsonObject CelulaJson(JsonObject t, string rota, string perfil, string etapa) => t["rotas"]![rota]!["perfis"]![perfil]!["etapas"]![etapa]!.AsObject();

    [Fact]
    public void tabela_padrao_reproduz_os_modelos_e_niveis_atuais_das_tres_rotas()
    {
        var k = Kit();
        var codex = new Dictionary<string, (string, string)>
        {
            ["prepare"] = ("gpt-6.2-sol", "baixo"), ["analyze"] = ("gpt-6.2-astra", "alto"), ["technical_audit"] = ("gpt-6.2-astra", "alto"),
            ["produce"] = ("gpt-6.2-sol", "baixo"), ["audit"] = ("gpt-6.2-sol", "medio"), ["final_review"] = ("gpt-6.2-sol", "baixo"),
        };
        var claude = new Dictionary<string, (string, string)>
        {
            ["prepare"] = ("claude-sonnet-5-5", "medio"), ["analyze"] = ("claude-opus-5-5", "alto"), ["technical_audit"] = ("claude-opus-5-5", "alto"),
            ["produce"] = ("claude-sonnet-5-5", "baixo"), ["audit"] = ("claude-sonnet-5-5", "alto"), ["final_review"] = ("claude-opus-5-5", "alto"),
        };
        foreach (var (etapa, (modelo, nivel)) in codex) Assert.Equal(new CelulaModelos(modelo, nivel), k.Celula(Coluna(k, "codex"), etapa));
        foreach (var (etapa, (modelo, nivel)) in claude) Assert.Equal(new CelulaModelos(modelo, nivel), k.Celula(Coluna(k, "claude"), etapa));
        // Gemini: igual à cópia tipada do Editor (MatrizGemini, a matriz de antes), perfil por perfil.
        foreach (var perfil in Enum.GetValues<PerfilGemini>())
            foreach (var e in MatrizGemini.Etapas(perfil))
            {
                var c = k.Celula(Coluna(k, "gemini", MatrizGemini.Codigo(perfil)), e.Etapa)!;
                Assert.Equal((e.Modelo, e.Esforco), (c.Modelo, k.Equivalencia[c.Nivel]));
            }
        Assert.Equal(6, k.Colunas.Count);   // ChatGPT, Claude e os quatro perfis do Gemini
        Assert.True(k.SobePorObjecao);
        Assert.Equal(0.15, k.DescePorCota);
        // Sem ajuste, a tabela efetiva repete o padrão (nenhuma célula muda, nenhum aviso).
        var efetiva = ModelosDaAnalise.Efetiva(k, new AjusteModelos(), ModelosDaAnalise.LerEtapas(EtapasDoKit(), k), Listas());
        Assert.Empty(efetiva.Avisos);
        Assert.Equal("gpt-6.2-astra", CelulaJson(efetiva.Json, "codex", "padrao", "analyze")["modelo"]!.GetValue<string>());
    }

    [Fact]
    public void as_etapas_vem_do_etapas_json_e_a_etapa_de_programa_nao_tem_modelo()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        Assert.Equal(["prepare", "analyze", "technical_audit", "audit", "produce", "final_review"], etapas.Select(e => e.Id));
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
        var padrao = k.Celula(Coluna(k, "codex"), "analyze")!;
        var opcoes = ModelosDaAnalise.OpcoesDeModelo(lista, padrao, null);
        Assert.Contains(opcoes, o => o.Id == "gpt-7-orion" && o.Texto.Contains("GPT-7 Orion", StringComparison.Ordinal));
        Assert.Contains(opcoes, o => o.Id == padrao.Modelo && o.Texto.Contains("padrão do kit", StringComparison.Ordinal));   // o padrão fica, marcado
        // Os níveis do modelo novo vêm da própria lista: sem "baixo".
        Assert.Equal(["medio", "alto"], ModelosDaAnalise.NiveisDoModelo(k, lista, "gpt-7-orion"));
        var etapa = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k).Single(e => e.Id == "analyze");
        var a = new AjusteModelos();
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "codex"), etapa, "gpt-7-orion", "alto", lista));
        Assert.Equal(new CelulaModelos("gpt-7-orion", "alto"), a.Celulas[("codex", "padrao", "analyze")]);
    }

    [Fact]
    public void modelo_inexistente_na_lista_do_provedor_e_recusado_com_mensagem()
    {
        var k = Kit();
        var etapa = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k).Single(e => e.Id == "analyze");
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
    public void lista_sem_o_modelo_configurado_avisa_e_usa_o_padrao_do_kit()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var a = new AjusteModelos();
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "claude"), etapas.Single(e => e.Id == "analyze"), "claude-fable-5-2", "alto",
            Lista(M("claude-opus-5-5"), M("claude-sonnet-5-5"), M("claude-fable-5-2"))));
        // Depois, a conta deixou de oferecer o Fable 5.2: a lista atualizada não tem mais o modelo.
        var efetiva = ModelosDaAnalise.Efetiva(k, a, etapas, Listas());
        var aviso = Assert.Single(efetiva.Avisos);
        Assert.Equal("claude", aviso.Rota);
        Assert.Equal("Claude, Análise: o modelo claude-fable-5-2 não aparece mais na lista do app (teste); esta etapa usa o padrão do kit (claude-opus-5-5, nível alto).", aviso.Texto);
        Assert.Equal("claude-opus-5-5", CelulaJson(efetiva.Json, "claude", "padrao", "analyze")["modelo"]!.GetValue<string>());
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
        Assert.Equal("claude-fable-5-2", CelulaJson(comFable.Json, "claude", "padrao", "analyze")["modelo"]!.GetValue<string>());
    }

    [Fact]
    public void etapa_tecnica_abaixo_de_medio_avisa_sem_bloquear()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var a = new AjusteModelos();
        Assert.Empty(ModelosDaAnalise.AvisosDaAba(k, a, etapas, Listas()));
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "codex"), etapas.Single(e => e.Id == "technical_audit"), "gpt-6.2-astra", "baixo", Listas()("codex")!));
        var aviso = Assert.Single(ModelosDaAnalise.AvisosDaAba(k, a, etapas, Listas()));
        Assert.Equal("ChatGPT, Auditoria técnica: nível baixo numa etapa técnica (que decide mérito). É permitido, mas o recomendado é médio ou alto.", aviso.Texto);
    }

    [Fact]
    public void ajuste_local_muda_o_modelo_o_controlador_recebe_e_restaurar_volta_ao_padrao()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var a = new AjusteModelos();
        var analyze = etapas.Single(e => e.Id == "analyze");
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "codex"), analyze, "gpt-6.2-sol", "medio", Listas()("codex")!));
        ModelosDaAnalise.Ligar(a, etapas.Single(e => e.Id == "audit"), "claude", false);
        using var pasta = new PastaTemporaria();
        // O ajuste local fica em %APPDATA%\EditorRelatoriosPMVNI\modelos-por-rota.local.json (aqui, a pasta de dados do teste) e volta igual.
        ModelosDaAnalise.GravarAjuste(pasta.Caminho, a);
        var lido = ModelosDaAnalise.LerAjuste(File.ReadAllText(ModelosDaAnalise.ArquivoLocal(pasta.Caminho)));
        Assert.Equal(new CelulaModelos("gpt-6.2-sol", "medio"), Assert.Single(lido.Celulas).Value);
        Assert.False(ModelosDaAnalise.Ligada(lido, etapas.Single(e => e.Id == "audit"), "claude"));

        // A tabela efetiva desta execução, lida pelo kit de verdade (o controlador da rota codex e o módulo da tabela).
        var efetiva = ModelosDaAnalise.Efetiva(k, lido, etapas, Listas()).ParaExecucao("codex");
        var arquivo = Path.Combine(pasta.Caminho, TabelaDeModelos.NomeDoArquivo);
        File.WriteAllText(arquivo, efetiva.Json, new UTF8Encoding(false));
        Assert.Equal(("gpt-6.2-sol", "medio"), ModeloQueOControladorUsa(arquivo, "analyze"));
        Assert.Equal(("gpt-6.2-sol", "baixo"), ModeloQueOControladorUsa(arquivo, "prepare"));
        Assert.Equal("False", RodarKit(arquivo, "import etapas; print('audit' in etapas.ordem('claude'))"));
        Assert.Equal("True", RodarKit(arquivo, "import etapas; print('audit' in etapas.ordem('codex'))"));

        // Restaurar padrão do kit: o ajuste fica vazio, o arquivo local some e o controlador volta ao padrão.
        var restaurado = ModelosDaAnalise.Restaurar();
        ModelosDaAnalise.GravarAjuste(pasta.Caminho, restaurado);
        Assert.False(File.Exists(ModelosDaAnalise.ArquivoLocal(pasta.Caminho)));
        File.WriteAllText(arquivo, ModelosDaAnalise.Efetiva(k, restaurado, etapas, Listas()).ParaExecucao("codex").Json, new UTF8Encoding(false));
        Assert.Equal(("gpt-6.2-astra", "alto"), ModeloQueOControladorUsa(arquivo, "analyze"));
        Assert.Equal("True", RodarKit(arquivo, "import etapas; print('audit' in etapas.ordem('claude'))"));
    }

    [Fact]
    public void publicar_grava_no_kit_mestre_e_dispara_o_manter_pelo_cmd()
    {
        var k = Kit();
        var etapas = ModelosDaAnalise.LerEtapas(EtapasDoKit(), k);
        var a = new AjusteModelos { SobePorObjecao = false };
        Assert.Null(ModelosDaAnalise.Ajustar(a, k, Coluna(k, "claude"), etapas.Single(e => e.Id == "produce"), "claude-sonnet-5-5", "medio", Listas()("claude")!));
        using var pasta = new PastaTemporaria();
        // Um kit mestre fictício (nunca o de verdade): a tabela no lugar do plugin e o instalador.
        var mestre = Path.Combine(pasta.Caminho, "KIT MESTRE FICTICIO");
        var destino = ModelosDaAnalise.ArquivoNoKit(mestre);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllText(destino, TabelaDoKit(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(mestre, ComponentesDoKit.InstaladorDoKit), "@echo off\r\n");
        var scripts = Path.Combine(pasta.Caminho, "scripts");
        var lancados = new List<(string Exe, string Arquivo)>();

        var texto = ModelosDaAnalise.Publicar(mestre, k, a, [], scripts, (exe, arq) => lancados.Add((exe, arq)));

        Assert.StartsWith("Tabela publicada no kit mestre", texto);
        var publicada = ModelosDaAnalise.LerTabela(File.ReadAllText(destino, Encoding.UTF8))!;
        Assert.Equal(new CelulaModelos("claude-sonnet-5-5", "medio"), publicada.Celula(Coluna(publicada, "claude"), "produce"));
        Assert.Equal(k.Celula(Coluna(k, "claude"), "analyze"), publicada.Celula(Coluna(publicada, "claude"), "analyze"));
        Assert.False(publicada.SobePorObjecao);
        Assert.DoesNotContain("\r\n", File.ReadAllText(destino, Encoding.UTF8));   // LF, como o arquivo do kit
        var (exe, cmd) = Assert.Single(lancados);
        Assert.EndsWith("explorer.exe", exe, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(scripts, ModelosDaAnalise.CmdPublicar), cmd);
        Assert.Contains("call \"" + Path.Combine(mestre, ComponentesDoKit.InstaladorDoKit) + "\" manter", File.ReadAllText(cmd));

        // Fora do computador do kit mestre, com análise em andamento ou com kit sem a tabela: nada é gravado nem aberto.
        lancados.Clear();
        Assert.Contains("não tem o kit mestre", ModelosDaAnalise.Publicar(null, k, a, [], scripts, (e, f) => lancados.Add((e, f))));
        Assert.Contains("andamento", ModelosDaAnalise.Publicar(mestre, k, a, ["P-1"], scripts, (e, f) => lancados.Add((e, f))));
        Assert.Contains("ainda não tem a tabela", ModelosDaAnalise.Publicar(Path.Combine(pasta.Caminho, "outro"), k, a, [], scripts, (e, f) => lancados.Add((e, f))));
        Assert.Empty(lancados);
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

    // ---------- o kit de verdade lendo a tabela ----------

    static string ScriptsDoKit => Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins", "analise", "skills", "analise", "scripts");

    /// <summary>O modelo e o nível que o controlador da rota codex (<c>analise_runtime.required_level</c>) exige, com a tabela passada em
    /// <c>PMVNI_MODELOS_POR_ROTA</c>.</summary>
    static (string, string) ModeloQueOControladorUsa(string tabela, string etapa)
    {
        var saida = RodarKit(tabela, "import sys, json; sys.path.insert(0, r'" + Path.Combine(ScriptsDoKit, "rotas", "codex")
            + "'); import analise_runtime as rt; r = rt.required_level('" + etapa + "'); print(r['modelo'] + '|' + r['nivel_exigido'])");
        var partes = saida.Split('|');
        return (partes[0], partes[1]);
    }

    static string RodarKit(string tabela, string codigo)
    {
        var si = new ProcessStartInfo("python") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = ScriptsDoKit, StandardOutputEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "-B", "-X", "utf8", "-c", "import sys; sys.path.insert(0, r'" + ScriptsDoKit + "'); " + codigo }) si.ArgumentList.Add(a);
        si.Environment[TabelaDeModelos.Variavel] = tabela;
        si.Environment["PYTHONDONTWRITEBYTECODE"] = "1";
        using var p = Process.Start(si)!;
        var saida = p.StandardOutput.ReadToEnd();
        var erro = p.StandardError.ReadToEnd();
        p.WaitForExit();
        Assert.True(p.ExitCode == 0, "python do kit falhou: " + erro);
        return saida.Trim();
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
