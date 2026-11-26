using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;
using Xunit;
using Xunit.Abstractions;

namespace Pmvni.Web.Testes;

/// <summary>Tarefa 6 da Etapa 2: o pacote vira relatório (dados.json efetivo → render com refs de sessão → composição) e o PDF
/// sai dos mesmos bytes efetivos, com o sha256 do pacote no recibo. WebView2 real, como na Etapa 1.</summary>
public sealed class RecomposicaoTestes : IClassFixture<TelaFixture>
{
    static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    readonly HostOculto comp, tela;
    readonly ITestOutputHelper saida;

    public RecomposicaoTestes(TelaFixture f, ITestOutputHelper saida)
    {
        comp = f.Composicao;
        tela = f.Tela;
        this.saida = saida;
    }

    [Fact]
    public async Task pacote_importado_compoe_o_mesmo_plano_que_o_dados_json()
    {
        var caminho = CaminhoCorpus("longo");
        var pacote = Importar("longo");
        var dados = DadosEfetivos.Gerar(pacote.Projeto, apresentarContaNominal: true).Json;
        using var arquivos = PastaTrabalho.Criar(pacote);
        var direto = RelatorioRenderer.Renderizar(dados, arquivos.CaminhoDados, new OpcoesRender(false, true, EditarTextos: true), Modelo);
        Assert.True(direto.Ok, string.Join("; ", direto.Erros));
        await comp.Documento.CarregarAsync(direto, ConfigFolhas.PadraoDeBytes(Modelo, dados));
        var esperado = await comp.Documento.ComporDetalhadoAsync();

        using var rec = new Recompositor(pacote, Modelo);
        var e = rec.Preparar(pacote.Projeto);
        var c = await rec.ComporAsync(e, comp);

        Assert.Equal(direto.Html, e.Render.Html);
        Assert.Equal(esperado.Plano.ParaJson(), c.Plano.ParaJson());
        Assert.Equal(esperado.Folhas, c.Folhas);
        Assert.Empty(e.ErrosFinais);
        Assert.Empty(e.AvisosExportacao);
    }

    [Fact]
    public async Task refs_de_sessao_dos_cards_sao_os_ids_estaveis()
    {
        var pacote = Importar("longo");
        using var rec = new Recompositor(pacote, Modelo);
        var e = rec.Preparar(pacote.Projeto);
        var c = await rec.ComporAsync(e, comp);

        var ids = c.RefsPorFolha.SelectMany(f => f).ToList();
        // Um card que passa de uma folha à outra aparece nas duas: conta-se por id distinto.
        var itens = ids.Where(i => i.StartsWith("it_", StringComparison.Ordinal)).Distinct().ToList();
        Assert.Equal(60, itens.Count);
        Assert.DoesNotContain(ids, i => i.StartsWith("item-", StringComparison.Ordinal));
        Assert.Equal(pacote.Projeto.Itens.Select(i => i.Id).Order(), itens.Order());
    }

    [Fact]
    public async Task excluir_item_recompoe_sem_o_item()
    {
        var pacote = Importar("curto");
        var itens = pacote.Projeto.Itens;
        var tituloDoItem2 = Titulo(itens[1]);
        var tituloDoItem3 = Titulo(itens[2]);
        var sessao = new SessaoRevisao(pacote.Projeto, pacote.Historico, Contexto());
        Assert.NotNull(sessao.Aplicar(new Excluir(itens[1].Id)));

        using var rec = new Recompositor(pacote, Modelo);
        var e = rec.Preparar(sessao.Projeto);
        await rec.ComporAsync(e, comp);
        var texto = Normalizar(string.Join("\n", await comp.Documento.TextoPorFolhaAsync()));

        Assert.DoesNotContain(Normalizar(tituloDoItem2), texto);
        Assert.Contains(Normalizar("ITEM 02 — " + tituloDoItem3), texto);
    }

    [Fact]
    public void edicao_que_a_conferencia_do_kit_recusa_lanca_recomposicao_recusada()
    {
        var pacote = Importar("curto");
        var sessao = new SessaoRevisao(pacote.Projeto, pacote.Historico, Contexto());
        // O comando em si já é recusado (campo vazio); a recomposição é a segunda barreira, para um projeto que chegue assim.
        Assert.Throws<ComandoRecusadoException>(() => sessao.Aplicar(new DefinirCampo(pacote.Projeto.Itens[0].Id, "providencia", "")));
        var projeto = pacote.Projeto.Clonar();
        projeto.Itens[0].Campos["providencia"].Revisado = "";

        using var rec = new Recompositor(pacote, Modelo);
        var ex = Assert.Throws<RecomposicaoRecusadaException>(() => rec.Preparar(projeto));
        Assert.Contains("ITEM 01: campo vazio providencia", ex.Erros);
        Assert.NotNull(rec.Preparar(pacote.Projeto)); // o projeto sem a edição prepara normalmente
    }

    [Fact]
    public void marcacao_nova_em_figura_do_kit_passa_pela_recomposicao_e_sai_dela()
    {
        // O caminho do editor de figura (2026-11-17): Testar → Preparar → Aplicar, numa figura do kit com marcações gravadas.
        var pacote = Importar("longo");
        var sessao = new SessaoRevisao(pacote.Projeto, pacote.Historico, Contexto());
        var fig = CamadaFiguras.Inicial(sessao.Projeto)[0].Id;
        using var rec = new Recompositor(pacote, Modelo);
        var svgAntes = Regex.Count(rec.Preparar(sessao.Projeto).Render.Html, "data:image/svg\\+xml;base64,");

        var incluir = new IncluirMarcacao(fig, "mk_web", Pmvni.Composicao.Figuras.TiposMarcacao.Regiao, Caixa: [.2, .2, .4, .4], Elemento: "Janela J4");
        var e = rec.Preparar(sessao.Testar(incluir));
        Assert.NotNull(sessao.Aplicar(incluir));
        Assert.Equal(svgAntes + 1, Regex.Count(e.Render.Html, "data:image/svg\\+xml;base64,"));

        var excluir = new ExcluirMarcacao(fig, "mk_web");
        var depois = rec.Preparar(sessao.Testar(excluir));
        Assert.NotNull(sessao.Aplicar(excluir));
        Assert.Equal(svgAntes, Regex.Count(depois.Render.Html, "data:image/svg\\+xml;base64,"));
        Assert.Null(sessao.Projeto.Figuras!.Single(f => f.Id == fig).Buscar("mk_web"));
    }

    [Fact]
    public async Task exportar_pacote_grava_sha256_do_pacote_e_dos_dados_efetivos()
    {
        var pacote = Importar("curto");
        using var rec = new Recompositor(pacote, Modelo);
        var e = rec.Preparar(pacote.Projeto);
        Assert.Empty(e.ErrosFinais);
        var c = await rec.ComporAsync(e, comp);
        var shaPacote = Hex(SHA256.HashData("pacote de teste"u8.ToArray()));
        var pdf = CaminhoSaida("pacote.pdf");

        var x = await Exportador.ExportarFinalAsync(e.Dados, e.CaminhoDados, e.Config, pdf, c.Plano, Modelo, shaPacote);

        using var recibo = JsonDocument.Parse(await File.ReadAllBytesAsync(x.CaminhoRecibo));
        var j = recibo.RootElement;
        Assert.Equal(shaPacote, j.GetProperty("sha256_pacote").GetString());
        Assert.Equal(Hex(SHA256.HashData(e.Dados)), j.GetProperty("sha256_dados").GetString());
        Assert.False(j.GetProperty("rascunho").GetBoolean());
        Assert.Equal(c.Folhas, x.Folhas);
        Assert.Equal(c.Plano.ParaJson(), x.Plano.ParaJson());

        // Sem informar o pacote, o recibo não leva a chave.
        var pdf2 = CaminhoSaida("sem-pacote.pdf");
        var y = await Exportador.ExportarFinalAsync(e.Dados, e.CaminhoDados, e.Config, pdf2, c.Plano, Modelo);
        using var recibo2 = JsonDocument.Parse(await File.ReadAllBytesAsync(y.CaminhoRecibo));
        Assert.False(recibo2.RootElement.TryGetProperty("sha256_pacote", out _));
    }

    [Fact]
    public async Task exportacao_recusa_se_o_efetivo_mudou_depois_da_tela()
    {
        var pacote = Importar("curto");
        var sessao = new SessaoRevisao(pacote.Projeto, pacote.Historico, Contexto());
        using var rec = new Recompositor(pacote, Modelo);
        var daTela = await rec.ComporAsync(rec.Preparar(sessao.Projeto), comp);

        Assert.NotNull(sessao.Aplicar(new DefinirCampo(sessao.Projeto.Itens[0].Id, "titulo", "Título editado depois de a tela abrir")));
        var depois = rec.Preparar(sessao.Projeto);
        var pdf = CaminhoSaida("mudou.pdf");
        File.Delete(pdf);
        File.Delete(Exportador.CaminhoReciboDe(pdf));

        var ex = await Assert.ThrowsAsync<ExportacaoDivergenteException>(
            () => Exportador.ExportarFinalAsync(depois.Dados, depois.CaminhoDados, depois.Config, pdf, daTela.Plano, Modelo));
        Assert.Equal(ExportacaoDivergenteException.ArquivoMudou, ex.Message);
        Assert.False(File.Exists(pdf));
        Assert.False(File.Exists(Exportador.CaminhoReciboDe(pdf)));
    }

    [Fact]
    public void pasta_de_trabalho_leva_as_imagens_e_some_no_dispose()
    {
        var pacote = Importar("longo");
        string caminho;
        using (var rec = new Recompositor(pacote, Modelo))
        {
            caminho = rec.PastaTrabalho;
            Assert.StartsWith(Path.Combine(PastaDados.Raiz, "edicao") + Path.DirectorySeparatorChar, caminho);
            Assert.NotEmpty(pacote.Assets);
            Assert.All(pacote.Assets.Keys, k => Assert.True(File.Exists(Path.Combine(caminho, k.Replace('/', Path.DirectorySeparatorChar)))));
            Assert.False(File.Exists(Path.Combine(caminho, "dados.json"))); // o efetivo vai por bytes, não por arquivo
        }
        Assert.False(Directory.Exists(caminho));
    }

    [Fact]
    public void pastas_de_trabalho_orfas_de_mais_de_um_dia_sao_apagadas()
    {
        var raiz = Path.Combine(PastaDados.Raiz, "edicao");
        var velha = Path.Combine(raiz, "orfa-" + Guid.NewGuid().ToString("N"));
        var nova = Path.Combine(raiz, "recente-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(velha);
        Directory.CreateDirectory(nova);
        Directory.SetLastWriteTimeUtc(velha, DateTime.UtcNow.AddDays(-2));
        try
        {
            using var p = PastaTrabalho.Criar(Importar("curto"));
            Assert.False(Directory.Exists(velha));
            Assert.True(Directory.Exists(nova));
        }
        finally { try { Directory.Delete(nova, true); } catch (IOException) { } }
    }

    [Fact]
    public void batimento_toca_a_pasta_de_trabalho_e_uma_sessao_viva_nunca_vira_orfa()
    {
        // Uma sessão aberta há mais de um dia sem recompor (só lendo): o batimento da trava toca a pasta, e a varredura de órfãs de outra
        // instância não a apaga.
        using var rec = new Recompositor(Importar("curto"), Modelo);
        Directory.SetLastWriteTimeUtc(rec.PastaTrabalho, DateTime.UtcNow.AddDays(-2));
        rec.TocarPasta();
        Assert.True(DateTime.UtcNow - Directory.GetLastWriteTimeUtc(rec.PastaTrabalho) < TimeSpan.FromMinutes(1));
        using var outra = PastaTrabalho.Criar(Importar("curto")); // a criação de outra varre as órfãs
        Assert.True(Directory.Exists(rec.PastaTrabalho));
    }

    /// <summary>PF9: a latência por comando (Preparar + compor + aplicar na "tela") é medida e registrada em
    /// tests\saida\tempos-comando.json, ao lado da linha de base da Etapa 1; a meta de 1,5 s no longo vai para
    /// docs/notas-tecnicas.md, não é barreira. O que o teste afirma: as medidas são finitas e positivas e a recomposição deu certo.</summary>
    [Fact]
    public async Task tempos_de_recompor_um_comando_do_corpus()
    {
        var resumo = new JsonObject();
        foreach (var caso in new[] { "curto", "longo", "extremos" })
        {
            var pacote = Importar(caso);
            var sessao = new SessaoRevisao(pacote.Projeto, pacote.Historico, Contexto());
            using var rec = new Recompositor(pacote, Modelo);
            var preparar = new List<double>();
            var compor = new List<double>();
            var aplicar = new List<double>();
            var total = new List<double>();
            var folhas = 0;
            for (var i = 0; i < 3; i++)
            {
                Assert.NotNull(sessao.Aplicar(new DefinirCampo(sessao.Projeto.Itens[0].Id, "titulo", "Título medido " + (i + 1))));
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var e = rec.Preparar(sessao.Projeto);
                var tPreparar = sw.Elapsed.TotalMilliseconds;
                var c = await rec.ComporAsync(e, comp);
                var tCompor = sw.Elapsed.TotalMilliseconds - tPreparar;
                await tela.Documento.CarregarAsync(e.Render, e.Config);
                var a = await tela.Documento.AplicarPlanoAsync(c.Plano);
                var tTotal = sw.Elapsed.TotalMilliseconds;
                preparar.Add(tPreparar);
                compor.Add(tCompor);
                aplicar.Add(tTotal - tPreparar - tCompor);
                total.Add(tTotal);
                Assert.Equal(c.Folhas, a.Folhas);
                Assert.Contains(Normalizar("Título medido " + (i + 1)), Normalizar(string.Join("\n", await tela.Documento.TextoPorFolhaAsync())));
                folhas = c.Folhas;
            }
            Assert.All(total.Concat(preparar).Concat(compor).Concat(aplicar), t => Assert.True(double.IsFinite(t) && t > 0, "medida inválida: " + t));
            resumo[caso] = new JsonObject
            {
                ["folhas"] = folhas,
                ["total_ms"] = Math.Round(Mediana(total)),
                ["preparar_ms"] = Math.Round(Mediana(preparar)),
                ["compor_ms"] = Math.Round(Mediana(compor)),
                ["aplicar_ms"] = Math.Round(Mediana(aplicar)),
                ["total_ms_rodadas"] = new JsonArray(total.Select(x => (JsonNode)Math.Round(x)).ToArray()),
            };
            saida.WriteLine($"{caso}: {folhas} folhas; comando {Mediana(total):0} ms (preparar {Mediana(preparar):0}, compor {Mediana(compor):0}, aplicar {Mediana(aplicar):0})"
                + $" [{string.Join("/", total.Select(x => x.ToString("0")))}]");
        }
        var dir = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida");
        Directory.CreateDirectory(dir);
        var arquivo = Path.Combine(dir, "tempos-comando.json");
        File.WriteAllText(arquivo, resumo.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        Assert.Equal(3, JsonNode.Parse(File.ReadAllText(arquivo))!.AsObject().Count);
    }

    // ---------- apoio ----------

    static double Mediana(List<double> v) => v.Order().ElementAt(v.Count / 2);

    static string Hex(byte[] b) => Convert.ToHexStringLower(b);

    static string CaminhoCorpus(string caso) => Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus", caso + ".json");

    static ContextoUsuario Contexto() =>
        new("teste", () => new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero), RandomNumberGenerator.Create());

    static ConteudoPacote Importar(string caso)
    {
        var caminho = CaminhoCorpus(caso);
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Contexto());
    }

    static string Titulo(ItemRevisao it) => ((JsonValue)it.Campos["titulo"].Original!).GetValue<string>();

    /// <summary>O texto das folhas vem com a caixa e os espaços da impressão (CSS): compara-se sem diferença de caixa nem de espaços.</summary>
    static string Normalizar(string s) => Regex.Replace(s, @"\s+", " ").ToUpperInvariant();

    static string CaminhoSaida(string nome)
    {
        var pasta = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "recomposicao");
        Directory.CreateDirectory(pasta);
        return Path.Combine(pasta, nome);
    }
}
