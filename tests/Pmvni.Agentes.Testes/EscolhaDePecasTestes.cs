using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Tarefa 14: o kit para porque há mais de uma versão possível de uma prancha (decisão do tipo <c>pecas</c>). O editor mostra as versões, grava o
/// caminho escolhido em <c>decisao.arquivo_resposta</c> (como as outras decisões) e chama o kit de novo; cancelar deixa a análise parada, sem gastar IA.
/// As três rotas valem igualmente.</summary>
public partial class OrquestradorTestes
{
    static JsonObject DecisaoDePecas(string arquivo = "decisao_resposta.json") => new()
    {
        ["tipo"] = "pecas", ["id"] = "pecas-1",
        ["pergunta"] = "Há duas versões da planta baixa com a mesma data. Qual vale para a análise?",
        ["opcoes"] = new JsonArray(
            new JsonObject { ["valor"] = @"PROJETO\INICIAL\planta-ficticia.pdf", ["texto"] = "Planta da pasta INICIAL" },
            new JsonObject { ["valor"] = @"PROJETO\REVISÃO 01\planta-ficticia.pdf", ["texto"] = "Planta da REVISÃO 01" }),
        ["arquivo_resposta"] = arquivo,
    };

    [Fact]
    public async Task Decisao_GravaEscolhaERetoma()
    {
        const string escolhido = @"PROJETO\REVISÃO 01\planta-ficticia.pdf";

        // ChatGPT: o controlador do kit para em DECISAO na preparação; a resposta vai para o arquivo da decisão e o controlador é chamado de novo.
        using (var c = new Cena("pecas-chatgpt", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "DECISAO", ["etapa"] = "prepare", ["decisao"] = DecisaoDePecas() },
            new JsonObject { ["situacao"] = "ENTREGUE", ["exigir_resposta"] = "decisao_resposta.json" })))
        {
            var perguntas = new List<DecisaoAnalise>();
            var pedido = c.Pedido(Provedor.ChatGpt) with { Decidir = (d, _) => { lock (perguntas) perguntas.Add(d); return Task.FromResult<string?>(d.Opcoes[1].Valor); } };
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);

            Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
            var d = Assert.Single(perguntas);
            Assert.True(PecasAmbiguas.EhEscolhaDePecas(d));
            Assert.Equal("Há duas versões da planta baixa com a mesma data. Qual vale para a análise?", d.Pergunta);
            var resposta = Json(Path.Combine(c.Controle("analise"), "decisao_resposta.json"));
            Assert.Equal(("pecas", "pecas-1", escolhido), (resposta["tipo"]!.GetValue<string>(), resposta["id"]!.GetValue<string>(), resposta["resposta"]!.GetValue<string>()));
            var chamadas = Chamadas(c, "analise62");
            Assert.Equal(2, chamadas.Count);
            Assert.DoesNotContain("--inputs", Argv(chamadas[1]));      // retomada de onde parou
            Assert.Single(Chamadas(c, "resposta"));
            Assert.False(File.Exists(Path.Combine(c.Controle("analise"), "pecas_escolhidas.json")));
        }

        // Claude e Gemini: o kit para com a decisão no situacao; o editor grava a resposta e dá a partida de novo.
        foreach (var p in new[] { Provedor.Claude, Provedor.Gemini })
        {
            using var c = new Cena("pecas-" + p.ToString().ToLowerInvariant(), "claude_ok", p, j =>
            {
                j["auto"]!["etapa_ms"] = 50;
                j["auto"]!["decisao"] = DecisaoDePecas();
            });
            var pedido = c.Pedido(p);
            if (p == Provedor.Gemini) { c.Pecas = ComGemini(c); pedido = c.Pedido(p, ModoAcesso.Conta) with { Perfil = PerfilGemini.ContaUltra }; }
            var perguntas = new List<DecisaoAnalise>();
            pedido = pedido with { Decidir = (d, _) => { lock (perguntas) perguntas.Add(d); return Task.FromResult<string?>(escolhido); } };
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);

            Assert.True(fim.Situacao == "concluida", p + " " + fim.Situacao + ": " + fim.Mensagem + Diagnostico(c));
            Assert.Equal(PecasAmbiguas.Tipo, Assert.Single(perguntas).Tipo);
            Assert.Equal(2, Chamadas(c, "iniciar").Count);
            var controle = c.Controle(p == Provedor.Claude ? "analise-pro" : "analise-google");
            Assert.Equal(escolhido, Json(Path.Combine(controle, "decisao_resposta.json"))["resposta"]!.GetValue<string>());
        }
    }

    [Fact]
    public async Task Decisao_Cancelada_NaoGastaIA()
    {
        // ChatGPT: cancelar a escolha deixa a análise parada na preparação, antes de qualquer sessão de IA (nenhum "codex exec", nenhuma nova chamada).
        using (var c = new Cena("pecas-cancelada", "ok", Provedor.ChatGpt, o => o["analise62"] = new JsonArray(
            new JsonObject { ["situacao"] = "DECISAO", ["etapa"] = "prepare", ["decisao"] = DecisaoDePecas() })))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt) with { Decidir = (_, _) => Task.FromResult<string?>(null) },
                c.Progresso, CancellationToken.None), Limite);

            Assert.Equal("interrompida", fim.Situacao);
            Assert.Equal(PecasAmbiguas.Tipo, fim.Decisao?.Tipo);
            Assert.Contains("Qual vale para a análise?", fim.Mensagem);
            Assert.Contains("Nada mais foi gasto.", fim.Mensagem);
            Assert.Single(Chamadas(c, "analise62"));
            Assert.Empty(Chamadas(c, "resposta"));
            Assert.False(File.Exists(c.Falso("recebido.json")) && Json(c.Falso("recebido.json"))["argv"]![0]!.GetValue<string>() == "exec");
            Assert.False(File.Exists(Path.Combine(c.Controle("analise"), "decisao_resposta.json")));
            Assert.False(TravaExecucao.EmAndamento(c.Processo.Pasta));
        }

        // Claude: sem resposta, o kit não recebe nova partida.
        using (var c = new Cena("pecas-cancelada-claude", "claude_ok", Provedor.Claude, j =>
        {
            j["auto"]!["etapa_ms"] = 50;
            j["auto"]!["decisao"] = DecisaoDePecas();
        }))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude) with { Decidir = (_, _) => Task.FromResult<string?>(null) },
                c.Progresso, CancellationToken.None), Limite);
            Assert.NotEqual("concluida", fim.Situacao);
            Assert.Single(Chamadas(c, "iniciar"));
            Assert.False(File.Exists(Path.Combine(c.Controle("analise-pro"), "decisao_resposta.json")));
        }
    }
}

/// <summary>Tarefa 14: as versões da peça para o diálogo (arquivo dentro do processo e miniatura do <c>cache-pdf</c>), sem IA.</summary>
public class PecasAmbiguasTestes : IDisposable
{
    readonly string pasta = Path.Combine(Path.GetTempPath(), "pmvni-pecas-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose() { try { Directory.Delete(pasta, true); } catch (IOException) { } }

    string Criar(string relativo, string conteudo)
    {
        var c = Path.Combine(pasta, relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(c)!);
        File.WriteAllText(c, conteudo);
        return c;
    }

    static DecisaoAnalise Decisao(params string[] valores) =>
        new(PecasAmbiguas.Tipo, "p", "Duas versões da planta.", valores.Select(v => new OpcaoDecisao(v, "Opção " + v)).ToList(), null, "{}");

    [Fact]
    public void Candidatas_MiniaturaDoCacheENome()
    {
        var a = Criar(@"PROJETO\INICIAL\planta.pdf", "versão A");
        var b = Criar(@"PROJETO\REVISÃO 01\planta.pdf", "versão B");
        var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(a))).ToLowerInvariant();
        var paginas = Path.Combine(pasta, ".pmvni-ai", "cache-pdf", "v1", "pdfium-4.30.0", sha, "paginas");
        var p200 = Criar(Path.Combine(paginas, sha[..12] + "_p001_200dpi.png"), "png");
        Criar(Path.Combine(paginas, sha[..12] + "_p001_300dpi.png"), "png");
        Criar(Path.Combine(paginas, sha[..12] + "_p002_150dpi.png"), "png");     // outra página: não é a miniatura

        var r = PecasAmbiguas.Candidatas(Decisao(@"PROJETO\INICIAL\planta.pdf", b), pasta);

        Assert.Equal(2, r.Count);
        Assert.Equal((a, p200), (r[0].Arquivo, r[0].Miniatura));        // relativo à pasta do processo; a menor resolução
        Assert.Equal(@"PROJETO\INICIAL\planta.pdf", r[0].Valor);          // o valor volta ao kit como veio
        Assert.Equal((b, (string?)null), (r[1].Arquivo, r[1].Miniatura)); // sem cache: só o nome e o botão de abrir
        Assert.Equal("planta.pdf", r[1].Nome);
        Assert.Equal("Opção " + b, r[1].Texto);
    }

    [Fact]
    public void Candidatas_ForaDoProcesso_NaoAbre()
    {
        Directory.CreateDirectory(pasta);
        var fora = Path.Combine(Path.GetTempPath(), "fora-" + Guid.NewGuid().ToString("N")[..8] + ".pdf");
        File.WriteAllText(fora, "x");
        try
        {
            Criar(@".pmvni-ai\analise\interno.pdf", "x");
            var r = PecasAmbiguas.Candidatas(Decisao(fora, @"..\outro.pdf", @".pmvni-ai\analise\interno.pdf", @"PROJETO\nao-existe.pdf"), pasta);
            Assert.All(r, x => Assert.Null(x.Arquivo));
            Assert.All(r, x => Assert.Null(x.Miniatura));
            Assert.Equal("nao-existe.pdf", r[3].Nome);
        }
        finally { File.Delete(fora); }
    }
}
