using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Tarefa 13: o progresso, a previsão de término e o consumo do Monitor saem dos arquivos do kit (<c>manifesto.json</c>, <c>engine_state.json</c> e
/// <c>uso-das-IAs.json</c>), calculados pelo Editor; nenhuma IA grava mais <c>progresso\*.json</c> nem <c>estimativa.json</c>. Dados fictícios.</summary>
public class ProgressoLocalTestes
{
    static readonly UTF8Encoding Utf8 = new(false);

    static string Controle(PastaTeste pasta, string plugin = "analise-pro")
    {
        var controle = Path.Combine(pasta, "processo-ficticio", ".pmvni-ai", plugin);
        Directory.CreateDirectory(controle);
        return controle;
    }

    static void Gravar(string controle, string nome, JsonNode json) => File.WriteAllText(Path.Combine(controle, nome), json.ToJsonString(), Utf8);

    static JsonObject Manifesto(int analisadas, params (string Id, int Secao, string Situacao)[] achados)
    {
        var secoes = new JsonArray();
        for (var i = 1; i <= analisadas; i++)
            secoes.Add(new JsonObject { ["section"] = i, ["title"] = "Seção fictícia " + i, ["status"] = "ANALISADO", ["finding_ids"] = new JsonArray() });
        var lista = new JsonArray();
        foreach (var (id, secao, situacao) in achados)
            lista.Add(new JsonObject { ["id"] = id, ["section"] = secao, ["status"] = situacao, ["observed"] = "Observação fictícia." });
        return new JsonObject { ["schema_version"] = 3, ["section_coverage"] = secoes, ["findings"] = lista };
    }

    static JsonObject Estado(params (string Etapa, JsonObject Registro)[] etapas)
    {
        var stages = new JsonObject();
        foreach (var (etapa, registro) in etapas) stages[etapa] = registro;
        return new JsonObject { ["version"] = 1, ["stages"] = stages, ["history"] = new JsonArray() };
    }

    static JsonObject Feita(double inicio, double fim, string? engine = "claude", string? executor = null)
    {
        var o = new JsonObject { ["status"] = "EXECUTED", ["started"] = inicio, ["ended"] = fim };
        if (engine is not null) o["engine"] = engine;
        if (executor is not null) o["executor"] = executor;
        return o;
    }

    [Fact]
    public void Progresso_DoManifesto()
    {
        using var pasta = Ambiente.PastaDeTeste("progresso-manifesto");
        var controle = Controle(pasta);
        Gravar(controle, "manifesto.json", Manifesto(10, ("AT-01", 6, "ATENDE"), ("AT-02", 7, "NÃO ATENDE"), ("AT-03", 8, "NÃO COMPROVADO / NÃO VERIFICÁVEL")));
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1600, executor: "programa", engine: null))));

        var p = ProgressoLocal.Ler(controle);

        Assert.Equal((10, 28), (p.SecoesComResultado, p.TotalSecoes));
        Assert.Equal(("analyze", 1), (p.Etapa, p.EtapasConcluidas));
        Assert.Equal(Provedor.Claude, p.Provedor);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1600), p.InicioEtapa);
        Assert.Equal(new[] { ("AT-01", "ATENDE"), ("AT-02", "NAO_ATENDE"), ("AT-03", "NAO_COMPROVADO") }, p.Achados.Select(a => (a.Id, a.Situacao)));
        Assert.Equal("Seção fictícia 6", p.Achados[0].Titulo);
        Assert.All(p.Achados, a => Assert.False(a.Verificado));     // a auditoria técnica ainda não passou

        // Seções NÃO APLICÁVEL e NÃO VERIFICÁVEL também têm resultado; PENDENTE (ou sem situação) não conta.
        var m = Manifesto(2, ("X1", 1, "DIVERGÊNCIA INTERNA"));
        m["section_coverage"]!.AsArray().Add(new JsonObject { ["section"] = 3, ["status"] = "NÃO APLICÁVEL" });
        m["section_coverage"]!.AsArray().Add(new JsonObject { ["section"] = 4, ["status"] = "NÃO VERIFICÁVEL" });
        m["section_coverage"]!.AsArray().Add(new JsonObject { ["section"] = 5, ["status"] = "PENDENTE" });
        Gravar(controle, "manifesto.json", m);
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1600)), ("analyze", Feita(1600, 3000)), ("technical_audit", Feita(3000, 3600))));
        p = ProgressoLocal.Ler(controle);
        Assert.Equal((4, "produce", 3), (p.SecoesComResultado, p.Etapa, p.EtapasConcluidas));
        Assert.Equal(("X1", "DIVERGENCIA_INTERNA", true), (p.Achados[0].Id, p.Achados[0].Situacao, p.Achados[0].Verificado));
    }

    [Fact]
    public void Progresso_ManifestoIncompleto()
    {
        using var pasta = Ambiente.PastaDeTeste("progresso-truncado");
        var controle = Controle(pasta);
        Gravar(controle, "manifesto.json", Manifesto(10, ("AT-01", 6, "ATENDE")));
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1600))));
        var valido = ProgressoLocal.Ler(controle);
        Assert.Equal(10, valido.SecoesComResultado);

        // O kit está gravando: o JSON chega pela metade (os dois arquivos). Fica o último progresso válido, sem exceção.
        var texto = Manifesto(12, ("AT-01", 6, "ATENDE"), ("AT-02", 7, "ATENDE")).ToJsonString();
        File.WriteAllText(Path.Combine(controle, "manifesto.json"), texto[..(texto.Length / 2)], Utf8);
        File.WriteAllText(Path.Combine(controle, "engine_state.json"), "{\"version\":1,\"stages\":{\"prepare\":{\"stat", Utf8);
        var p = ProgressoLocal.Ler(controle);
        Assert.Equal((10, "analyze", 1), (p.SecoesComResultado, p.Etapa, p.EtapasConcluidas));
        Assert.Single(p.Achados);

        // Sem nada válido ainda (pasta nova com JSON pela metade): progresso vazio, sem exceção.
        var outro = Controle(pasta, "analise-google");
        File.WriteAllText(Path.Combine(outro, "manifesto.json"), "{\"schema_version\":3,\"section_cov", Utf8);
        var vazio = ProgressoLocal.Ler(outro);
        Assert.Equal((0, null, 0, Provedor.Gemini), (vazio.SecoesComResultado, vazio.Etapa, vazio.EtapasConcluidas, vazio.Provedor));
        Assert.Empty(vazio.Achados);

        // Arquivos de antes desta análise (a anterior, já entregue) não contam.
        var p2 = ProgressoLocal.Ler(controle, desdeUtc: DateTime.UtcNow.AddMinutes(5));
        Assert.Equal((0, null, 0), (p2.SecoesComResultado, p2.Etapa, p2.EtapasConcluidas));
    }

    static List<DuracaoEtapa> Historico(Provedor p, params (string Etapa, double[] Minutos)[] etapas) =>
        etapas.SelectMany(e => e.Minutos.Select(m => new DuracaoEtapa(e.Etapa, p, TimeSpan.FromMinutes(m)))).ToList();

    [Fact]
    public void Previsao_SemHistorico_Nula()
    {
        var agora = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var atual = new Progresso("technical_audit", 2, 28, [], Provedor.Claude, agora.AddMinutes(-2));
        Assert.Null(PrevisaoTermino.Estimar(atual, [], agora));
        // Duas execuções de uma etapa que falta não bastam (mínimo de 3), mesmo com as outras completas.
        var poucas = Historico(Provedor.Claude, ("technical_audit", [10, 12, 14]), ("produce", [3, 4]), ("audit", [5, 6, 7]), ("final_review", [2, 2, 2]));
        Assert.Null(PrevisaoTermino.Estimar(atual, poucas, agora));
        // O histórico de outro provedor não vale.
        var outro = Historico(Provedor.ChatGpt, ("technical_audit", [10, 12, 14]), ("produce", [3, 4, 5]), ("audit", [5, 6, 7]), ("final_review", [2, 2, 2]));
        Assert.Null(PrevisaoTermino.Estimar(atual, outro, agora));
        // Sem etapa (análise concluída ou sem estado) não há previsão.
        Assert.Null(PrevisaoTermino.Estimar(atual with { Etapa = null }, outro, agora));
    }

    [Fact]
    public void Previsao_PelaMediana()
    {
        var agora = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        var historico = Historico(Provedor.Claude, ("technical_audit", [10, 30, 12, 14]), ("produce", [3, 4, 5]), ("audit", [5, 60, 7]), ("final_review", [2, 2, 3]),
            ("analyze", [90, 95, 100]));
        historico.AddRange(Historico(Provedor.Gemini, ("produce", [40, 40, 40])));
        // A etapa atual (technical_audit) começou há 2 min: falta a mediana dela (13) menos 2, mais 4 + 7 + 2 das seguintes.
        var atual = new Progresso("technical_audit", 2, 28, [], Provedor.Claude, agora.AddMinutes(-2));
        Assert.Equal(agora.AddMinutes(11 + 4 + 7 + 2), PrevisaoTermino.Estimar(atual, historico, agora));
        // Etapa atual já além da mediana: ela entra como zero (o término nunca fica no passado).
        Assert.Equal(agora.AddMinutes(4 + 7 + 2), PrevisaoTermino.Estimar(atual with { InicioEtapa = agora.AddHours(-1) }, historico, agora));
        // Sem a hora de início, a etapa atual conta inteira.
        Assert.Equal(agora.AddMinutes(13 + 4 + 7 + 2), PrevisaoTermino.Estimar(atual with { InicioEtapa = null }, historico, agora));
    }

    [Fact]
    public void Uso_MostraExecutorPrograma()
    {
        using var pasta = Ambiente.PastaDeTeste("uso-programa");
        var controle = Controle(pasta);
        var claude = Feita(2000, 2600);
        claude["requested_model"] = "claude-opus-ficticio";
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100, engine: null, executor: "programa")), ("analyze", claude),
            ("technical_audit", Feita(2600, 3000, engine: "codex")), ("produce", Feita(3000, 3060, engine: null, executor: "programa"))));
        Gravar(controle, "uso-das-IAs.json", JsonNode.Parse("""
            {"gerado_em":"2026-11-21 12:00:00","execucoes":[
              {"etapa":"analyze","ia":"Claude (Opus)","modelo_pedido":"claude-opus-ficticio","situacao":"EXECUTED","duracao_s":600.0,"tokens":1500,
               "cache_lido":90000,"tokens_detalhe":{"entrada":1000,"saida":400,"cache_lido":90000,"cache_gravado":100},"cota_usada_pp":{"5 h":7.0,"semana":1.5},"cota_fora":false},
              {"etapa":"technical_audit","ia":"Sol (Codex)","modelo_pedido":"gpt-ficticio","situacao":"EXECUTED","duracao_s":400.0,"tokens":89221,
               "cache_lido":null,"tokens_detalhe":null,"cota_usada_pp":{"5 h":"renovou","semana":2.0},"cota_fora":false}
            ]}
            """)!);

        var uso = UsoDasIas.Ler(controle);

        Assert.Equal(new[] { "prepare", "analyze", "technical_audit", "produce" }, uso.Select(u => u.Etapa));
        Assert.Equal(new[] { "programa", "Claude", "GPT", "programa" }, uso.Select(u => u.Executor));
        foreach (var programa in uso.Where(u => u.Executor == "programa"))
        {
            Assert.Null(programa.Entrada);
            Assert.Null(programa.Saida);
            Assert.Null(programa.Total);
            Assert.Null(programa.CacheLido);
            Assert.Null(programa.Cota5h);
        }
        Assert.Equal(TimeSpan.FromSeconds(100), uso[0].Duracao);
        var a = uso[1];
        Assert.Equal(("claude-opus-ficticio", 1000L, 400L, 90000L, TimeSpan.FromSeconds(600), 7.0, 1.5),
            (a.Modelo, a.Entrada, a.Saida, a.CacheLido, a.Duracao, a.Cota5h, a.CotaSemana));
        var t = uso[2];
        Assert.Equal(((long?)null, (long?)null, 89221L, (double?)null, 2.0), (t.Entrada, t.Saida, t.Total, t.Cota5h, t.CotaSemana));

        // Rota ChatGPT (analise): sem uso-das-IAs.json; o executor vem do engine_state (o modelo observado), sem tokens.
        var analise = Controle(pasta, "analise");
        Gravar(analise, "engine_state.json", Estado(("prepare", new JsonObject { ["status"] = "EXECUTED", ["executor"] = "programa", ["ended"] = 10.0 }),
            ("analyze", new JsonObject { ["status"] = "EXECUTED", ["executor"] = "gpt-ficticio-astra", ["modelo_observado"] = "gpt-ficticio-astra", ["ended"] = 20.0 })));
        var chatgpt = UsoDasIas.Ler(analise);
        Assert.Equal(new[] { ("prepare", "programa"), ("analyze", "GPT") }, chatgpt.Select(u => (u.Etapa, u.Executor)));
        Assert.Equal("gpt-ficticio-astra", chatgpt[1].Modelo);
        Assert.Null(chatgpt[1].Entrada);
        Assert.Empty(UsoDasIas.Ler(Controle(pasta, "analise-google")));
    }

    [Fact]
    public void Consumo_TextosDoCartao()
    {
        var programa = new UsoEtapa("prepare", "programa", null, null, null, null, TimeSpan.FromSeconds(30), null, null);
        Assert.Equal(("—", "—", "—"), (TextoConsumo.Tokens(programa), TextoConsumo.Numero(programa.CacheLido), TextoConsumo.Cota(programa)));
        var claude = new UsoEtapa("analyze", "Claude", "claude-ficticio", 850, 12_345, 1_520_000, null, 7, 1.5);
        Assert.Equal(("850 / 12,3 mil", "1,5 mi", "+7% · +1,5%"), (TextoConsumo.Tokens(claude), TextoConsumo.Numero(claude.CacheLido), TextoConsumo.Cota(claude)));
        var codex = new UsoEtapa("audit", "GPT", null, null, null, null, null, null, 2) { Total = 89_221 };
        Assert.Equal(("89,2 mil (total)", "— · +2%"), (TextoConsumo.Tokens(codex), TextoConsumo.Cota(codex)));
    }

    [Theory]
    [InlineData("programa", null, null, "programa")]
    [InlineData(null, "codex", "gpt-ficticio", "GPT")]
    [InlineData(null, "claude", "claude-opus-ficticio", "Claude")]
    [InlineData(null, "antigravity", null, "Gemini")]
    [InlineData(null, "agy", null, "Gemini")]
    [InlineData("gpt-ficticio-sol", null, null, "GPT")]
    [InlineData(null, null, "gemini-ficticio-flash", "Gemini")]
    public void Executor_PeloRegistro(string? executor, string? engine, string? modelo, string esperado) =>
        Assert.Equal(esperado, UsoDasIas.Executor(executor, engine, modelo));

    [Fact]
    public void Historico_DeTodosOsProcessos()
    {
        using var pasta = Ambiente.PastaDeTeste("uso-historico");
        string raiz = pasta;
        void Uso(string processo, string plugin, string json)
        {
            var controle = Path.Combine(raiz, "2026", processo, ".pmvni-ai", plugin);
            Directory.CreateDirectory(controle);
            File.WriteAllText(Path.Combine(controle, "uso-das-IAs.json"), json, Utf8);
        }
        Uso("0001 - 2026 - FICTICIO", "analise-pro", """
            {"execucoes":[{"etapa":"analyze","situacao":"EXECUTED","duracao_s":600},{"etapa":"analyze","situacao":"FAILED_QUOTA","duracao_s":50},
                          {"etapa":"inexistente","situacao":"EXECUTED","duracao_s":10},{"etapa":"audit","situacao":"EXECUTED","duracao_s":null}]}
            """);
        Uso("0002 - 2026 - FICTICIO", "analise-google", """{"execucoes":[{"etapa":"produce","situacao":"EXECUTED","duracao_s":120.5}]}""");
        Uso("0003 - 2026 - FICTICIO", "analise-pro", "{\"execucoes\":[{\"etapa\":");   // pela metade: ignorado

        var h = UsoDasIas.Historico(raiz);
        Assert.Equal(new[] { ("analyze", Provedor.Claude, TimeSpan.FromSeconds(600)), ("produce", Provedor.Gemini, TimeSpan.FromSeconds(120.5)) },
            h.Select(d => (d.Etapa, d.Provedor, d.Duracao)).OrderBy(d => d.Etapa));
        Assert.Empty(UsoDasIas.Historico(Path.Combine(raiz, "nao-existe")));
    }
}
