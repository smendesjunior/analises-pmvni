using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Tarefa 18: análise em andamento iniciada num plugin antigo (analise 3.x, analise-pro, analise-google). O kit 4.0.0 não mistura os controles:
/// devolve a DECISAO <c>plugin_antigo</c> (recomeçar pelo plugin novo ou não continuar agora), e o editor só a mostra pelo fluxo genérico de decisão,
/// gravando a resposta em <c>decisao.arquivo_resposta</c>. O histórico fica a cargo do kit (<c>&lt;controle&gt;\historico</c>). As três rotas valem igualmente.</summary>
public partial class OrquestradorTestes
{
    static JsonObject DecisaoPluginAntigo(string plugin) => new()
    {
        ["tipo"] = "plugin_antigo", ["id"] = "antigo-1",
        ["pergunta"] = "Esta análise foi iniciada pelo plugin " + plugin + ", que não existe mais. Recomeçar pelo plugin novo? "
            + "O controle antigo vai para a pasta historico.",
        ["opcoes"] = new JsonArray(
            new JsonObject { ["valor"] = "recomecar", ["texto"] = "Recomeçar pelo plugin novo" },
            new JsonObject { ["valor"] = "cancelar", ["texto"] = "Não continuar agora" }),
        ["arquivo_resposta"] = "decisao_resposta.json",
    };

    static void ConferirPerguntaPluginAntigo(DecisaoAnalise d, string plugin)
    {
        Assert.Equal("plugin_antigo", d.Tipo);
        Assert.False(PecasAmbiguas.EhEscolhaDePecas(d));                 // diálogo genérico, não o da escolha de peças
        Assert.StartsWith("Esta análise foi iniciada pelo plugin " + plugin, d.Pergunta);
        Assert.Equal(new[] { ("recomecar", "Recomeçar pelo plugin novo"), ("cancelar", "Não continuar agora") }, d.Opcoes.Select(x => (x.Valor, x.Texto)));
    }

    [Fact]
    public async Task PluginAntigo_Recomecar_NasTresRotas()
    {
        // ChatGPT: o controlador para em DECISAO na retomada; com "recomecar" gravado, é chamado de novo (sem --inputs: quem arquiva e recomeça é o kit).
        using (var c = new Cena("antigo-chatgpt", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "DECISAO", ["etapa"] = "prepare", ["decisao"] = DecisaoPluginAntigo("analise 3.1.0") },
            new JsonObject { ["situacao"] = "ENTREGUE", ["exigir_resposta"] = "decisao_resposta.json" })))
        {
            var perguntas = new List<DecisaoAnalise>();
            var pedido = c.Pedido(Provedor.ChatGpt) with { Decidir = (d, _) => { lock (perguntas) perguntas.Add(d); return Task.FromResult<string?>("recomecar"); } };
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);

            Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
            ConferirPerguntaPluginAntigo(Assert.Single(perguntas), "analise 3.1.0");
            var resposta = Json(Path.Combine(c.Controle("analise"), "decisao_resposta.json"));
            Assert.Equal(("plugin_antigo", "antigo-1", "recomecar"),
                (resposta["tipo"]!.GetValue<string>(), resposta["id"]!.GetValue<string>(), resposta["resposta"]!.GetValue<string>()));
            var chamadas = Chamadas(c, "analise62");
            Assert.Equal(2, chamadas.Count);
            Assert.Equal(c.Pecas.Analise62, Argv(chamadas[1])[3]);       // o controlador da rota codex do plugin único
            Assert.DoesNotContain("--inputs", Argv(chamadas[1]));
        }

        // Claude e Gemini: a DECISAO vem no situacao; o editor grava a resposta e dá a partida de novo no controlador da rota.
        foreach (var (p, plugin) in new[] { (Provedor.Claude, "analise-pro 2.1.0") })
        {
            using var c = new Cena("antigo-" + p.ToString().ToLowerInvariant(), "claude_ok", p, j =>
            {
                j["auto"]!["etapa_ms"] = 50;
                j["auto"]!["decisao"] = DecisaoPluginAntigo(plugin);
            });
            var pedido = c.Pedido(p);
            var perguntas = new List<DecisaoAnalise>();
            pedido = pedido with { Decidir = (d, _) => { lock (perguntas) perguntas.Add(d); return Task.FromResult<string?>("recomecar"); } };
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);

            Assert.True(fim.Situacao == "concluida", p + " " + fim.Situacao + ": " + fim.Mensagem + Diagnostico(c));
            ConferirPerguntaPluginAntigo(Assert.Single(perguntas), plugin);
            var iniciar = Chamadas(c, "iniciar");
            Assert.Equal(2, iniciar.Count);
            Assert.All(iniciar, x => Assert.Equal(c.Pecas.RuntimeDoKit(p), Argv(x)[3]));   // o controlador da rota no plugin único
            var controle = c.Controle(p == Provedor.Claude ? "analise-pro" : "analise-google");      // a pasta de controle não muda
            var resposta = Json(Path.Combine(controle, "decisao_resposta.json"));
            Assert.Equal(("plugin_antigo", "antigo-1", "recomecar"),
                (resposta["tipo"]!.GetValue<string>(), resposta["id"]!.GetValue<string>(), resposta["resposta"]!.GetValue<string>()));
        }
    }

    [Fact]
    public async Task PluginAntigo_NaoContinuar_NaoGastaNada()
    {
        // "Não continuar agora": a resposta vai ao kit, que termina em FALHA sem gastar nada; o editor mostra o motivo do kit.
        using (var c = new Cena("antigo-cancelar", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "DECISAO", ["etapa"] = "prepare", ["decisao"] = DecisaoPluginAntigo("analise 3.1.0") },
            new JsonObject
            {
                ["situacao"] = "FALHA", ["exigir_resposta"] = "decisao_resposta.json",
                ["motivo"] = "O analista decidiu não recomeçar agora. O controle antigo ficou como estava.",
            })))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt) with { Decidir = (_, _) => Task.FromResult<string?>("cancelar") },
                c.Progresso, CancellationToken.None), Limite);
            Assert.Equal("falhou", fim.Situacao);
            Assert.Contains("O analista decidiu não recomeçar agora.", fim.Mensagem);
            Assert.Equal("cancelar", Json(Path.Combine(c.Controle("analise"), "decisao_resposta.json"))["resposta"]!.GetValue<string>());
            Assert.Equal(2, Chamadas(c, "analise62").Count);
        }

        // Pergunta fechada sem resposta: a análise fica parada, nada é gravado e o kit não é chamado de novo (Claude e Gemini).
        foreach (var p in new[] { Provedor.Claude })
        {
            using var c = new Cena("antigo-fechada-" + p.ToString().ToLowerInvariant(), "claude_ok", p, j =>
            {
                j["auto"]!["etapa_ms"] = 50;
                j["auto"]!["decisao"] = DecisaoPluginAntigo("analise-pro 2.1.0");
            });
            var pedido = c.Pedido(p);
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido with { Decidir = (_, _) => Task.FromResult<string?>(null) }, c.Progresso, CancellationToken.None), Limite);
            Assert.NotEqual("concluida", fim.Situacao);
            Assert.Equal("plugin_antigo", fim.Decisao?.Tipo);
            Assert.Single(Chamadas(c, "iniciar"));
            Assert.False(File.Exists(Path.Combine(c.Controle(p == Provedor.Claude ? "analise-pro" : "analise-google"), "decisao_resposta.json")));
        }
    }
}
