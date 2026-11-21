using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Tarefa 20 (Editor): o Monitor segue a ordem das etapas que o kit define em <c>references\etapas.json</c> (plugin analise 4.0.0: prepare, analyze,
/// technical_audit, audit, produce, final_review; a audit pode ser desligada por rota), em vez de uma ordem fixa no código. Sem o arquivo ou com arquivo
/// inválido, vale a ordem antiga. Os estados de tentativa (<c>RUNNING</c>, <c>FAILED</c>, <c>WAITING_QUOTA</c>, <c>WAITING_DECISION</c>) e o <c>started</c>
/// de todas as etapas entram no progresso e na previsão. Dados fictícios.</summary>
public class OrdemDasEtapasTestes
{
    static readonly UTF8Encoding Utf8 = new(false);
    static readonly string[] OrdemKit4 = ["prepare", "analyze", "technical_audit", "audit", "produce", "final_review"];

    /// <summary>Um <c>etapas.json</c> no formato da versão 1 do kit (só os campos que o Editor lê, mais os que o kit exige).</summary>
    static string EtapasJson(string pasta, string nome, bool codex = true, bool claude = true, bool gemini = true, string[]? ordem = null)
    {
        var etapas = new JsonObject();
        foreach (var id in OrdemKit4) etapas[id] = new JsonObject { ["executor"] = "ia", ["sessao"] = "nova", ["depende_de"] = new JsonArray() };
        etapas["audit"] = new JsonObject
        {
            ["executor"] = "ia", ["sessao"] = "nova", ["opcional"] = true,
            ["ligada_por_rota"] = new JsonObject { ["codex"] = codex, ["claude"] = claude, ["gemini"] = gemini },
        };
        var raiz = new JsonObject
        {
            ["versao"] = 1, ["ordem"] = new JsonArray((ordem ?? OrdemKit4).Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
            ["final"] = "gate", ["max_ciclos_correcao"] = 2, ["etapas"] = etapas,
        };
        var arquivo = Path.Combine(pasta, nome);
        File.WriteAllText(arquivo, raiz.ToJsonString(), Utf8);
        return arquivo;
    }

    static string Controle(string pasta, string plugin = "analise-pro")
    {
        var controle = Path.Combine(pasta, "processo-ficticio", ".pmvni-ai", plugin);
        Directory.CreateDirectory(controle);
        return controle;
    }

    static void Gravar(string controle, string nome, JsonNode json) => File.WriteAllText(Path.Combine(controle, nome), json.ToJsonString(), Utf8);

    static JsonObject Estado(params (string Etapa, JsonObject Registro)[] etapas)
    {
        var stages = new JsonObject();
        foreach (var (etapa, registro) in etapas) stages[etapa] = registro;
        return new JsonObject { ["version"] = 1, ["stages"] = stages, ["history"] = new JsonArray() };
    }

    static JsonObject Registro(string status, double inicio, double? fim = null)
    {
        var o = new JsonObject { ["status"] = status, ["started"] = inicio, ["engine"] = "claude" };
        if (fim is { } f) o["ended"] = f;
        return o;
    }

    static JsonObject Feita(double inicio, double fim) => Registro("EXECUTED", inicio, fim);

    static DateTimeOffset T(double segundos) => DateTimeOffset.FromUnixTimeSeconds((long)segundos);

    [Fact]
    public void Ordem_LidaDoEtapasJson_ComAAuditAntesDaProducao()
    {
        using var pasta = Ambiente.PastaDeTeste("ordem-kit4");
        var arquivo = EtapasJson(pasta, "etapas.json");

        var ordem = OrdemDasEtapas.Ler(arquivo);

        Assert.True(ordem.DoArquivo);
        Assert.Equal(OrdemKit4, ordem.Todas);
        foreach (var p in Enum.GetValues<Provedor>()) Assert.Equal(OrdemKit4, ordem.Para(p));
        Assert.Equal(OrdemKit4, ordem.Para(null));
        // A ordem padrão (kit 3.x) continua a antiga.
        Assert.False(OrdemDasEtapas.Padrao.DoArquivo);
        Assert.Equal(new[] { "prepare", "analyze", "technical_audit", "produce", "audit", "final_review" }, OrdemDasEtapas.Padrao.Todas);
    }

    [Fact]
    public void Ordem_AuditDesligadaNaRota_SaiDaOrdemDaquelaRotaSo()
    {
        using var pasta = Ambiente.PastaDeTeste("ordem-audit-desligada");
        var ordem = OrdemDasEtapas.Ler(EtapasJson(pasta, "etapas.json", claude: false));

        Assert.Equal(OrdemKit4, ordem.Todas);
        Assert.Equal(new[] { "prepare", "analyze", "technical_audit", "produce", "final_review" }, ordem.Para(Provedor.Claude));
        Assert.Equal(OrdemKit4, ordem.Para(Provedor.ChatGpt));
        Assert.Equal(OrdemKit4, ordem.Para(Provedor.Gemini));
    }

    [Fact]
    public void Ordem_SemArquivoOuInvalido_UsaAOrdemAntigaSemErro()
    {
        using var pasta = Ambiente.PastaDeTeste("ordem-invalida");
        string raiz = pasta;
        string Texto(string nome, string conteudo)
        {
            var arquivo = Path.Combine(raiz, nome);
            File.WriteAllText(arquivo, conteudo, Utf8);
            return arquivo;
        }

        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(null));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(""));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(Path.Combine(raiz, "nao-existe.json")));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(Texto("lixo.json", "{ não é json")));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(Texto("lista.json", "[1, 2]")));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(Texto("sem-ordem.json", """{"versao":1,"etapas":{}}""")));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(Texto("vazia.json", """{"ordem":[]}""")));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(Texto("numero.json", """{"ordem":["prepare",2]}""")));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(Texto("repetida.json", """{"ordem":["prepare","prepare"]}""")));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(Texto("id-vazio.json", """{"ordem":["prepare",""]}""")));
        Assert.Same(OrdemDasEtapas.Padrao, OrdemDasEtapas.Ler(Texto("ordem-texto.json", """{"ordem":"prepare"}""")));

        // ligada_por_rota malformado não derruba o arquivo: a etapa fica ligada.
        var tolerante = OrdemDasEtapas.Ler(Texto("tolerante.json", """
            {"ordem":["prepare","audit"],"etapas":{"audit":{"opcional":true,"ligada_por_rota":{"claude":"nao"}}}}
            """));
        Assert.True(tolerante.DoArquivo);
        Assert.Equal(new[] { "prepare", "audit" }, tolerante.Para(Provedor.Claude));
        // ligada_por_rota só vale em etapa opcional.
        var naoOpcional = OrdemDasEtapas.Ler(Texto("nao-opcional.json", """
            {"ordem":["prepare","analyze"],"etapas":{"analyze":{"ligada_por_rota":{"claude":false}}}}
            """));
        Assert.Equal(new[] { "prepare", "analyze" }, naoOpcional.Para(Provedor.Claude));
    }

    [Fact]
    public void Progresso_DuranteAAudit_MostraAuditEProduceAindaPendente()
    {
        using var pasta = Ambiente.PastaDeTeste("progresso-audit");
        var controle = Controle(pasta);
        var ordem = OrdemDasEtapas.Ler(EtapasJson(pasta, "etapas.json"));
        // O kit 4.0: a audit vem antes da produção. A audit abriu a sessão em 2300 (RUNNING, sem ended); a technical_audit terminou em 2000.
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Feita(1100, 1700)), ("technical_audit", Feita(1700, 2000)),
            ("audit", Registro("RUNNING", 2300))));

        var p = ProgressoLocal.Ler(controle, ordemDoKit: ordem);

        Assert.Equal(("audit", 3, 6), (p.Etapa, p.EtapasConcluidas, p.TotalEtapas));
        Assert.Equal(OrdemKit4, p.Ordem);
        Assert.Equal(T(2300), p.InicioEtapa);        // o started da etapa em andamento, não o fim da anterior (2000)
        // Logo depois da technical_audit, antes da audit abrir a sessão: a audit é a próxima (a ordem fixa antiga mostraria a produção).
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Feita(1100, 1700)), ("technical_audit", Feita(1700, 2000))));
        var proxima = ProgressoLocal.Ler(controle, ordemDoKit: ordem);
        Assert.Equal(("audit", 3, T(2000)), (proxima.Etapa, proxima.EtapasConcluidas, proxima.InicioEtapa));
        var antiga = ProgressoLocal.Ler(controle);
        Assert.Equal(("produce", 3), (antiga.Etapa, antiga.EtapasConcluidas));

        // A audit termina: a próxima é a produção.
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Feita(1100, 1700)), ("technical_audit", Feita(1700, 2000)),
            ("audit", Feita(2300, 2450))));
        p = ProgressoLocal.Ler(controle, ordemDoKit: ordem);
        Assert.Equal(("produce", 4, T(2450)), (p.Etapa, p.EtapasConcluidas, p.InicioEtapa));
    }

    [Fact]
    public void Progresso_AuditDesligada_NaoContaNoTotalNemEntraNaPrevisao()
    {
        using var pasta = Ambiente.PastaDeTeste("progresso-sem-audit");
        string raiz = pasta;
        var ordem = OrdemDasEtapas.Ler(EtapasJson(raiz, "etapas.json", claude: false));
        var estado = Estado(("prepare", Feita(1000, 1100)), ("analyze", Feita(1100, 1700)), ("technical_audit", Feita(1700, 2000)));
        var claude = Controle(raiz, "analise-pro");
        var codex = Controle(raiz, "analise");
        Gravar(claude, "engine_state.json", estado);
        Gravar(codex, "engine_state.json", estado.DeepClone());

        var semAudit = ProgressoLocal.Ler(claude, ordemDoKit: ordem);
        Assert.Equal(("produce", 3, 5), (semAudit.Etapa, semAudit.EtapasConcluidas, semAudit.TotalEtapas));
        Assert.DoesNotContain("audit", semAudit.Ordem!);
        // A rota ChatGPT, com a audit ligada, continua com ela.
        var comAudit = ProgressoLocal.Ler(codex, ordemDoKit: ordem);
        Assert.Equal(("audit", 3, 6), (comAudit.Etapa, comAudit.EtapasConcluidas, comAudit.TotalEtapas));

        // A previsão da rota sem audit não pede (nem soma) a duração da audit.
        var agora = T(10_000);
        var historico = new List<DuracaoEtapa>();
        foreach (var (etapa, minutos) in new[] { ("produce", 4d), ("final_review", 2d) })
            for (var i = 0; i < 3; i++) historico.Add(new DuracaoEtapa(etapa, Provedor.Claude, TimeSpan.FromMinutes(minutos)));
        Assert.Equal(agora.AddMinutes(4 + 2), PrevisaoTermino.Estimar(semAudit with { InicioEtapa = null }, historico, agora));
        // Com a audit ligada (ChatGPT), sem amostras da audit não há previsão.
        var historicoGpt = historico.Select(d => d with { Provedor = Provedor.ChatGpt }).ToList();
        Assert.Null(PrevisaoTermino.Estimar(comAudit with { InicioEtapa = null }, historicoGpt, agora));
        historicoGpt.AddRange(Enumerable.Range(0, 3).Select(_ => new DuracaoEtapa("audit", Provedor.ChatGpt, TimeSpan.FromMinutes(5))));
        Assert.Equal(agora.AddMinutes(5 + 4 + 2), PrevisaoTermino.Estimar(comAudit with { InicioEtapa = null }, historicoGpt, agora));
    }

    [Fact]
    public void Progresso_SemEtapasJson_UsaAOrdemAntiga()
    {
        using var pasta = Ambiente.PastaDeTeste("progresso-ordem-antiga");
        var controle = Controle(pasta);
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Feita(1100, 1700)), ("technical_audit", Feita(1700, 2000)),
            ("produce", Feita(2000, 2100))));

        // Kit 3.x (sem o arquivo) e arquivo ilegível: a mesma ordem antiga, sem erro.
        foreach (var ordem in new[] { OrdemDasEtapas.Ler(null), OrdemDasEtapas.Ler(Path.Combine(pasta, "nao-existe.json")), null })
        {
            var p = ProgressoLocal.Ler(controle, ordemDoKit: ordem);
            Assert.Equal(("audit", 4, 6), (p.Etapa, p.EtapasConcluidas, p.TotalEtapas));
        }
    }

    [Fact]
    public void Progresso_EtapaRunning_ComecaNoStartedEWaitingNaoEConcluida()
    {
        using var pasta = Ambiente.PastaDeTeste("progresso-tentativas");
        var ordem = OrdemDasEtapas.Ler(EtapasJson(pasta, "etapas.json"));
        var controle = Controle(pasta);

        // RUNNING: é a etapa atual; começou no started (1150), não no fim da anterior (1100).
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Registro("RUNNING", 1150))));
        var p = ProgressoLocal.Ler(controle, ordemDoKit: ordem);
        Assert.Equal(("analyze", 1, T(1150)), (p.Etapa, p.EtapasConcluidas, p.InicioEtapa));

        // WAITING_QUOTA e WAITING_DECISION: a etapa não está concluída, continua sendo a atual, e o início não é confiável (a espera não é trabalho).
        foreach (var espera in new[] { "WAITING_QUOTA", "WAITING_DECISION" })
        {
            Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Feita(1100, 1700)),
                ("technical_audit", Registro(espera, 1700, 5000))));
            p = ProgressoLocal.Ler(controle, ordemDoKit: ordem);
            Assert.Equal(("technical_audit", 2, (DateTimeOffset?)null), (p.Etapa, p.EtapasConcluidas, p.InicioEtapa));
        }

        // FAILED: também não conclui; a etapa fica como a atual, com o fim da anterior como referência.
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Registro("FAILED", 1150, 1400))));
        p = ProgressoLocal.Ler(controle, ordemDoKit: ordem);
        Assert.Equal(("analyze", 1, T(1100)), (p.Etapa, p.EtapasConcluidas, p.InicioEtapa));

        // A etapa RUNNING tem prioridade sobre uma anterior parada por falha (a análise seguiu adiante).
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Feita(1100, 1700)),
            ("technical_audit", Registro("FAILED", 1700, 1800)), ("audit", Registro("RUNNING", 1900))));
        p = ProgressoLocal.Ler(controle, ordemDoKit: ordem);
        Assert.Equal(("audit", 2, T(1900)), (p.Etapa, p.EtapasConcluidas, p.InicioEtapa));
    }

    [Fact]
    public void Previsao_EtapaEmEspera_ContaAEtapaInteira()
    {
        var agora = T(20_000);
        var historico = new List<DuracaoEtapa>();
        foreach (var (etapa, minutos) in new[] { ("technical_audit", 10d), ("audit", 5d), ("produce", 4d), ("final_review", 2d) })
            for (var i = 0; i < 3; i++) historico.Add(new DuracaoEtapa(etapa, Provedor.Claude, TimeSpan.FromMinutes(minutos)));
        var ordem = OrdemKit4;
        var rodando = new Progresso("technical_audit", 2, 28, [], Provedor.Claude, agora.AddMinutes(-3)) { Ordem = ordem };
        Assert.Equal(agora.AddMinutes(7 + 5 + 4 + 2), PrevisaoTermino.Estimar(rodando, historico, agora));
        // Parada à espera da cota (sem início confiável): a mediana inteira.
        Assert.Equal(agora.AddMinutes(10 + 5 + 4 + 2), PrevisaoTermino.Estimar(rodando with { InicioEtapa = null }, historico, agora));
        // Etapa atual que não está na ordem da rota: sem previsão.
        Assert.Null(PrevisaoTermino.Estimar(rodando with { Etapa = "inexistente" }, historico, agora));
    }

    [Fact]
    public void Duracao_PrefereStartedAoFimDaEtapaAnterior()
    {
        using var pasta = Ambiente.PastaDeTeste("duracao-started");
        var ordem = OrdemDasEtapas.Ler(EtapasJson(pasta, "etapas.json"));
        var controle = Controle(pasta);
        // A audit abriu a sessão 300 s depois do fim da technical_audit (fila de cota, por exemplo): vale ended − started (150), não 450.
        // Uma etapa RUNNING e outra WAITING_QUOTA não têm duração.
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Feita(1100, 1700)), ("technical_audit", Feita(1700, 2000)),
            ("audit", Feita(2300, 2450)), ("produce", Registro("WAITING_QUOTA", 2500, 2560)), ("final_review", Registro("RUNNING", 2600))));

        var h = UsoDasIas.DuracoesDaPasta(controle, ordem);

        Assert.Equal(new[] { ("analyze", 600d), ("audit", 150d), ("prepare", 100d), ("technical_audit", 300d) },
            h.Select(d => (d.Etapa, d.Duracao.TotalSeconds)).OrderBy(d => d.Etapa));
        Assert.All(h, d => Assert.Equal(Provedor.Claude, d.Provedor));
    }

    [Fact]
    public void Uso_LinhasNaOrdemDoKit_ComStatusDaTentativa()
    {
        using var pasta = Ambiente.PastaDeTeste("uso-ordem-kit");
        var ordem = OrdemDasEtapas.Ler(EtapasJson(pasta, "etapas.json"));
        var controle = Controle(pasta);
        Gravar(controle, "engine_state.json", Estado(("prepare", Feita(1000, 1100)), ("analyze", Feita(1100, 1700)), ("technical_audit", Feita(1700, 2000)),
            ("audit", Feita(2300, 2450)), ("produce", Registro("RUNNING", 2500))));

        var uso = UsoDasIas.Ler(controle, ordemDoKit: ordem);
        Assert.Equal(OrdemKit4[..5], uso.Select(u => u.Etapa));
        Assert.Equal(new[] { "EXECUTED", "EXECUTED", "EXECUTED", "EXECUTED", "RUNNING" }, uso.Select(u => u.Situacao));
        Assert.Null(uso[4].Duracao);                       // em andamento: sem duração ainda
        Assert.Equal(TimeSpan.FromSeconds(150), uso[3].Duracao);

        // Sem o arquivo do kit, a ordem antiga (produce antes de audit).
        Assert.Equal(new[] { "prepare", "analyze", "technical_audit", "produce", "audit" }, UsoDasIas.Ler(controle).Select(u => u.Etapa));
    }

    [Fact]
    public void Localizacao_AchaOEtapasJsonDoPluginInstalado()
    {
        using var t = Ambiente.PastaDeTeste("loc-etapas");
        var cache = Path.Combine(t, "codex-home", "plugins", "cache", "pmvni-kit", "analise");
        void Arquivo(string v, string relativo, string conteudo)
        {
            var caminho = Path.Combine(cache, v, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
            File.WriteAllText(caminho, conteudo);
        }
        OpcoesLocalizacao Opcoes() => new(LocalAppData: Path.Combine(t, "local"), CodexHome: Path.Combine(t, "codex-home"),
            Path: [Path.Combine(t, "bin")], PastaKit: Path.Combine(t, "kit"), CodexEscolhido: null, PythonEscolhido: null);

        // Kit 3.x: sem o arquivo.
        Arquivo("3.1.0", Path.Combine("skills", "analise", "scripts", "executar_analise_62.py"), "# 3.1.0");
        Assert.Null(Localizacao.Ler(Opcoes()).EtapasJson);

        // Kit 4.x: o etapas.json da maior versão instalada.
        foreach (var v in new[] { "4.0.0", "4.0.1" }) Arquivo(v, Path.Combine("skills", "analise", "references", "etapas.json"), """{"ordem":["prepare"]}""");
        var achado = Localizacao.Ler(Opcoes()).EtapasJson;
        Assert.Equal(Path.Combine(cache, "4.0.1", "skills", "analise", "references", "etapas.json"), achado);
        Assert.Equal(new[] { "prepare" }, OrdemDasEtapas.Ler(achado).Todas);
    }
}
