using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Fluxo linear do kit (2026-11-22): o Monitor mostra o modelo e o esforço que o executor gravou, as três frentes da Análise, as objeções na etapa
/// que as levantou e "programa" nas etapas locais. Dados fictícios, no formato que os scripts do kit gravam.</summary>
public class DetalhesDasEtapasTestes
{
    static readonly UTF8Encoding Utf8 = new(false);

    static string Controle(PastaTeste pasta, string plugin = "analise")
    {
        var controle = Path.Combine(pasta, "processo-ficticio", ".pmvni-ai", plugin);
        Directory.CreateDirectory(controle);
        return controle;
    }

    static void Gravar(string arquivo, JsonNode json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        File.WriteAllText(arquivo, json.ToJsonString(), Utf8);
    }

    static string EtapasJson(PastaTeste pasta)
    {
        var arquivo = Path.Combine(pasta, "kit", "etapas.json");
        Gravar(arquivo, new JsonObject
        {
            ["versao"] = 2,
            ["ordem"] = new JsonArray("prepare", "analyze", "technical_audit", "audit", "produce", "final_review"),
            ["etapas"] = new JsonObject
            {
                ["prepare"] = new JsonObject { ["executor"] = "programa" },
                ["analyze"] = new JsonObject { ["executor"] = "ia" },
                ["technical_audit"] = new JsonObject { ["executor"] = "ia" },
                ["audit"] = new JsonObject { ["executor"] = "programa", ["opcional"] = true },
                ["produce"] = new JsonObject { ["executor"] = "programa" },
                ["final_review"] = new JsonObject { ["executor"] = "programa" },
            },
        });
        return arquivo;
    }

    [Fact]
    public void Le_plano_frente_da_tentativa_e_registros_reais_incluindo_avisos()
    {
        using var pasta = Ambiente.PastaDeTeste("detalhes-plano");
        var controle = Controle(pasta);
        var partes = Path.Combine(controle, "runs", "secoes-atual");
        Gravar(Path.Combine(partes, "frente-01", "atividade.json"), JsonNode.Parse("""{"inicio":1,"fim":9,"status":"em_andamento","descricao":"Conferindo as fontes"}""")!);
        Gravar(Path.Combine(controle, "frentes_ativas.json"), new JsonObject { ["pasta"] = partes });
        Gravar(Path.Combine(controle, "plano_execucao.json"), JsonNode.Parse("""{"etapas":[{"id":"prepare"},{"id":"analyze","descricao":"Saídas previstas: manifesto.json"}]}""")!);
        Gravar(Path.Combine(controle, "engine_state.json"), JsonNode.Parse("""{"stages":{"prepare":{"note":"Leitura encerrada","evidence":[{"path":"planta.pdf"}]},"analyze":{"status":"RUNNING"}}}""")!);
        Gravar(Path.Combine(controle, "validadores", "avisos.json"), JsonNode.Parse("""[{"regra":"palavra_repetida","onde":"Resumo","texto":"item item"}]""")!);
        var dados = DetalhesDasEtapas.Ler(controle);
        Assert.Equal(new[] { "prepare", "analyze" }, dados.Plano);
        Assert.Equal("Saídas previstas: manifesto.json", dados.DescricoesDoPlano["analyze"]);
        Assert.Empty(dados.DescricoesDoPlano["prepare"]);
        Assert.Contains("Conferindo as fontes", dados.Frentes[0].Atividade);
        Assert.Contains("Leitura encerrada", dados.Registros["prepare"]);
        Assert.Contains("planta.pdf", dados.Registros["prepare"]);
        Assert.Contains("item item", Assert.Single(dados.Validacoes));
        Gravar(Path.Combine(controle, "engine_state.json"), JsonNode.Parse("""{"stages":{"analyze":{"status":"FAILED","error":"Erro do auditor"}}}""")!);
        Assert.Null(DetalhesDasEtapas.Ler(controle).Frentes.FirstOrDefault()?.Atividade);
    }

    [Fact]
    public void Codex_mostra_modelo_e_esforco_gravados_pelo_executor_e_programa_nas_etapas_locais()
    {
        using var pasta = Ambiente.PastaDeTeste("detalhes-codex");
        var controle = Controle(pasta);
        Gravar(Path.Combine(controle, "engine_state.json"), new JsonObject
        {
            ["stages"] = new JsonObject
            {
                ["prepare"] = new JsonObject { ["status"] = "EXECUTED", ["executor"] = "programa" },
                ["analyze"] = new JsonObject { ["status"] = "RUNNING" },
            },
            ["sessao_codex"] = new JsonObject
            {
                ["analyze"] = new JsonObject { ["rota"] = "codex-lb", ["modelo"] = "gpt-6.2-astra", ["esforco"] = "medium", ["nivel"] = "medio" },
            },
        });

        var d = DetalhesDasEtapas.Ler(controle, EtapasJson(pasta));

        Assert.Equal("gpt-6.2-astra · esforço médio", d.TextoExecutor("analyze"));
        Assert.Equal("Programa · sem consumo de IA", d.TextoExecutor("audit"));
        Assert.Equal("Programa · sem consumo de IA", d.TextoExecutor("produce"));
        Assert.Equal("Programa · sem consumo de IA", d.TextoExecutor("final_review"));
        // A auditoria técnica ainda não tem sessão: nada inventado.
        Assert.Null(d.TextoExecutor("technical_audit"));
        Assert.Empty(d.Frentes);
        Assert.False(d.TemObjecoes);
    }

    [Fact]
    public void Claude_e_Gemini_usam_o_modelo_e_o_esforco_pedidos_do_registro()
    {
        using var pasta = Ambiente.PastaDeTeste("detalhes-claude");
        var controle = Controle(pasta, "analise-pro");
        Gravar(Path.Combine(controle, "engine_state.json"), new JsonObject
        {
            ["stages"] = new JsonObject
            {
                ["technical_audit"] = new JsonObject { ["status"] = "RUNNING", ["engine"] = "claude", ["requested_model"] = "claude-opus-5-5", ["requested_effort"] = "medium" },
                ["produce"] = new JsonObject { ["status"] = "EXECUTED", ["executor"] = "programa" },
            },
        });

        var d = DetalhesDasEtapas.Ler(controle);

        Assert.Equal("claude-opus-5-5 · esforço médio", d.TextoExecutor("technical_audit"));
        Assert.Equal("Programa · sem consumo de IA", d.TextoExecutor("produce"));
    }

    [Fact]
    public void Frentes_seguem_os_arquivos_do_fluxo_linear_e_somem_sem_dado()
    {
        using var pasta = Ambiente.PastaDeTeste("detalhes-frentes");
        var controle = Controle(pasta);
        Assert.Empty(DetalhesDasEtapas.Ler(controle).Frentes);

        var partes = Path.Combine(controle, "runs", "secoes-abc123");
        Gravar(Path.Combine(partes, "frente-01", "concluida.json"), new JsonObject { ["arquivos"] = new JsonObject() });
        File.WriteAllText(Path.Combine(partes, "frente-10", "pedido.txt").Also(p => Directory.CreateDirectory(Path.GetDirectoryName(p)!)), "pedido", Utf8);
        foreach (var n in new[] { 10, 11, 12, 13 })
            Gravar(Path.Combine(partes, $"secao-{n:00}.json"), new JsonObject { ["coverage"] = new JsonObject { ["section"] = n } });

        var frentes = DetalhesDasEtapas.Ler(controle).Frentes;

        Assert.Equal(
        [
            new FrenteAnalise(1, 9, 9, true, true),
            new FrenteAnalise(10, 18, 4, true, false),
            new FrenteAnalise(19, 28, 0, false, false),
        ], frentes);
        Assert.Equal(10, frentes[2].Total);
    }

    [Fact]
    public void Frentes_registradas_pelo_executor_valem_sem_a_pasta_das_partes()
    {
        using var pasta = Ambiente.PastaDeTeste("detalhes-frentes-registro");
        var controle = Controle(pasta);
        JsonObject Frente(int a, int b) => new() { ["secoes"] = new JsonArray(a, b), ["sessao"] = "s-" + a, ["modelo"] = "gpt-6.2-astra", ["esforco"] = "medium" };
        Gravar(Path.Combine(controle, "engine_state.json"), new JsonObject
        {
            ["stages"] = new JsonObject(),
            ["sessao_codex"] = new JsonObject { ["analyze"] = new JsonObject { ["frentes"] = new JsonArray(Frente(1, 9), Frente(10, 18), Frente(19, 28)) } },
        });

        var d = DetalhesDasEtapas.Ler(controle);

        Assert.All(d.Frentes, f => Assert.True(f.Concluida));
        Assert.Equal([(1, 9), (10, 18), (19, 28)], d.Frentes.Select(f => (f.Inicio, f.Fim)));
        Assert.Equal("gpt-6.2-astra · esforço médio", d.TextoExecutor("analyze"));
    }

    [Fact]
    public void Objecoes_ficam_na_etapa_que_as_levantou()
    {
        using var pasta = Ambiente.PastaDeTeste("detalhes-objecoes");
        var controle = Controle(pasta);
        Gravar(Path.Combine(controle, "engine_state.json"), new JsonObject
        {
            ["stages"] = new JsonObject
            {
                ["technical_audit"] = new JsonObject { ["status"] = "OBJECTIONS", ["note"] = "Cota fictícia sem fonte." },
                ["final_review"] = new JsonObject { ["status"] = "OBJECTIONS", ["objecoes"] = "Página fictícia sem hash." },
            },
            ["objecoes_sem_ciclo"] = new JsonObject { ["etapa"] = "technical_audit", ["motivo"] = "Achado fictício sem evidência.", ["arquivo"] = "objecoes_technical_audit.json" },
        });

        var d = DetalhesDasEtapas.Ler(controle);

        Assert.True(d.TemObjecoes);
        Assert.Equal(
        [
            new ObjecaoPendente("technical_audit", "Achado fictício sem evidência.", "objecoes_technical_audit.json"),
            new ObjecaoPendente("final_review", "Página fictícia sem hash.", null),
        ], d.Objecoes);
        Assert.DoesNotContain(d.Objecoes, o => o.Etapa == "analyze");
    }

    [Fact]
    public void Producao_de_teste_so_com_copia_certificada_dentro_do_controle()
    {
        using var pasta = Ambiente.PastaDeTeste("producao-teste");
        var controle = Controle(pasta);
        Assert.False(ProducaoTeste.TemCopiaCertificada(controle));

        var copia = Path.Combine(controle, "certificados", "cert-ficticio");
        Gravar(Path.Combine(controle, "engine_state.json"), new JsonObject
        {
            ["stages"] = new JsonObject { ["technical_audit"] = new JsonObject { ["status"] = "EXECUTED", ["copia_certificada"] = copia } },
        });
        Assert.False(ProducaoTeste.TemCopiaCertificada(controle));     // sem o certificado.json

        Gravar(Path.Combine(copia, "certificado.json"), new JsonObject { ["arquivos"] = new JsonObject() });
        Assert.True(ProducaoTeste.TemCopiaCertificada(controle));

        var fora = Path.Combine(pasta, "outra-pasta", "cert");
        Gravar(Path.Combine(fora, "certificado.json"), new JsonObject());
        Gravar(Path.Combine(controle, "engine_state.json"), new JsonObject
        {
            ["stages"] = new JsonObject { ["technical_audit"] = new JsonObject { ["status"] = "EXECUTED", ["copia_certificada"] = fora, ["pad"] = "x" } },
        });
        Assert.False(ProducaoTeste.TemCopiaCertificada(controle));
    }

    [Fact]
    public void Linha_da_producao_de_teste_segue_o_contrato_do_kit()
    {
        var (exe, args) = LinhasDeComando.ProducaoTeste(@"C:\py\python.exe", true, @"C:\kit\producao_teste.py", @"C:\proc", Provedor.Claude, @"C:\proc\saida");
        Assert.Equal(@"C:\py\python.exe", exe);
        Assert.Equal(["-3", "-B", "-X", "utf8", @"C:\kit\producao_teste.py", @"C:\proc", "--rota", "claude", "--saida", @"C:\proc\saida"], args);
        Assert.Equal("codex", ProducaoTeste.Rota(Provedor.ChatGpt));
        Assert.Equal(@"C:\x\producao-teste-1\RELATORIO.pdf", ProducaoTeste.PdfDaSaida("aviso\r\nC:\\x\\producao-teste-1\\RELATORIO.pdf\r\n"));
        Assert.Null(ProducaoTeste.PdfDaSaida("Produção de teste recusada: cópia alterada"));
    }

    static JsonObject Registro(DateTimeOffset inicio, double minutos) => new()
    {
        ["status"] = "EXECUTED", ["started"] = inicio.ToUnixTimeSeconds(), ["ended"] = inicio.AddMinutes(minutos).ToUnixTimeSeconds(),
    };

    [Fact]
    public void Previsao_so_usa_amostras_marcadas_com_o_fluxo_linear()
    {
        using var pasta = Ambiente.PastaDeTeste("historico-fluxo-linear");
        var controle = Controle(pasta);
        var marca = new DateTimeOffset(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);
        // Ciclo antigo (antes da marca) e um history de etapas substituídas: análise de 60 min. Fluxo linear: análise de 15 min.
        Gravar(Path.Combine(controle, "engine_state.json"), new JsonObject
        {
            ["stages"] = new JsonObject { ["prepare"] = Registro(marca.AddMinutes(1), 3), ["analyze"] = Registro(marca.AddMinutes(5), 15) },
            ["history"] = new JsonArray(new JsonObject { ["replaced"] = new JsonObject { ["analyze"] = Registro(marca.AddDays(-2), 60) } }),
        });
        var raiz = Path.Combine(pasta, "");

        // Sem marca: o histórico da previsão fica vazio (valem as durações iniciais); sem o filtro, as três amostras.
        Assert.Empty(UsoDasIas.Historico(raiz, somenteFluxoLinear: true));
        Assert.Equal(3, UsoDasIas.Historico(raiz).Count);

        var registro = Path.Combine(pasta, "processo-ficticio", ".pmvni-ai", "editor", "2026-11-22_100000-chatgpt");
        Gravar(Path.Combine(registro, "execucao.json"), new JsonObject
        {
            ["provedor"] = "chatgpt", ["inicio"] = marca.ToString("o"), [FluxoLinear.Chave] = FluxoLinear.Assinatura,
        });
        var h = UsoDasIas.Historico(raiz, somenteFluxoLinear: true);
        Assert.Equal([("analyze", 15d), ("prepare", 3d)], h.Select(d => (d.Etapa, d.Duracao.TotalMinutes)).OrderBy(d => d.Etapa));

        // A marca de outro provedor não libera este controle.
        Gravar(Path.Combine(registro, "execucao.json"), new JsonObject
        {
            ["provedor"] = "claude", ["inicio"] = marca.ToString("o"), [FluxoLinear.Chave] = FluxoLinear.Assinatura, ["x"] = 1,
        });
        Assert.Empty(UsoDasIas.Historico(raiz, somenteFluxoLinear: true));
    }

    [Fact]
    public void Kit_linear_pela_versao_do_etapas_json()
    {
        using var pasta = Ambiente.PastaDeTeste("fluxo-linear-kit");
        Assert.True(FluxoLinear.DoKit(EtapasJson(pasta)));
        var antigo = Path.Combine(pasta, "kit-antigo", "etapas.json");
        Gravar(antigo, new JsonObject { ["versao"] = 1, ["ordem"] = new JsonArray("prepare") });
        Assert.False(FluxoLinear.DoKit(antigo));
        Assert.False(FluxoLinear.DoKit(null));
    }

    [Fact]
    public void Analise_usa_as_frentes_ate_o_manifesto_e_nunca_volta()
    {
        using var pasta = Ambiente.PastaDeTeste("progresso-frentes");
        var controle = Controle(pasta);
        Gravar(Path.Combine(controle, "engine_state.json"), new JsonObject
        {
            ["stages"] = new JsonObject
            {
                ["prepare"] = new JsonObject { ["status"] = "EXECUTED", ["started"] = 1_790_000_000, ["ended"] = 1_790_000_100 },
                ["analyze"] = new JsonObject { ["status"] = "RUNNING", ["started"] = 1_790_000_100 },
            },
        });
        var partes = Path.Combine(controle, "runs", "secoes-ficticia");
        foreach (var n in new[] { 1, 2, 3, 10, 11, 19, 20 })
            Gravar(Path.Combine(partes, $"secao-{n:00}.json"), new JsonObject { ["coverage"] = new JsonObject { ["section"] = n } });

        var p = ProgressoLocal.Ler(controle);
        Assert.Equal("analyze", p.Etapa);
        Assert.Equal(7, p.SecoesComResultado);
        Assert.Equal(7d / 28, p.FracaoObservada);

        // Manifesto com menos seções: fica o maior (as frentes).
        static JsonObject Manifesto(int n) => new()
        {
            ["section_coverage"] = new JsonArray(Enumerable.Range(1, n).Select(i => (JsonNode)new JsonObject { ["section"] = i, ["status"] = "ANALISADO" }).ToArray()),
        };
        Gravar(Path.Combine(controle, "manifesto.json"), Manifesto(3));
        Assert.Equal(7, ProgressoLocal.Ler(controle).SecoesComResultado);

        // Manifesto montado com as 28: vale o manifesto.
        Gravar(Path.Combine(controle, "manifesto.json"), Manifesto(28));
        Assert.Equal(1d, ProgressoLocal.Ler(controle).FracaoObservada);
    }

    [Fact]
    public void Duracoes_iniciais_seguem_o_orcamento_do_fluxo_linear()
    {
        string[] ordem = ["prepare", "analyze", "technical_audit", "audit", "produce", "final_review"];
        var total = DuracoesIniciais.Total(ordem);
        Assert.InRange(total.TotalMinutes, 26, 52);
        // As etapas locais (programa) não passam de alguns minutos juntas.
        Assert.True((DuracoesIniciais.DaEtapa("audit")!.Value + DuracoesIniciais.DaEtapa("produce")!.Value + DuracoesIniciais.DaEtapa("final_review")!.Value).TotalMinutes <= 5);
        Assert.InRange(DuracoesIniciais.DaEtapa("analyze")!.Value.TotalMinutes, 12.5, 22);
        Assert.InRange(DuracoesIniciais.DaEtapa("technical_audit")!.Value.TotalMinutes, 10, 23);
    }
}

static class ExtensoesDeTeste
{
    public static T Also<T>(this T valor, Action<T> acao)
    {
        acao(valor);
        return valor;
    }
}
