using System.Text.Json.Nodes;
using Pmvni.App;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Correções 2026-11-16: as esquadrias de um ambiente se calculam (área = largura × altura; vão total; mínimo = piso ÷ divisor;
/// situação) sem IA, e o usuário (ou a IA, a pedido dele) pode acrescentar uma esquadria — "adicionar cálculo da porta de 80x210cm e
/// rever se atende". Os acréscimos são uma camada do projeto: o documento recebido não muda.</summary>
public class EsquadriasTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    const string A = "ambientes/01";

    /// <summary>O relatório "longo" com o AMBIENTE 01 do usuário: Quarto 1, piso 12,00 m², J1 1,50 × 1,20 m (1,80 m²), mínimo 12 ÷ 6 = 2,00 m².</summary>
    private static SessaoRevisao Quarto(string situacao = "CONTA_CONFERE")
    {
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        dados["iluminacao"]!["ambientes"]![0] = new JsonObject
        {
            ["icone"] = "bed", ["nome"] = "Quarto 1", ["detalhes"] = new JsonArray("piso 12,00 m²", "Pavimento superior"), ["situacao"] = situacao,
            ["vaos"] = new JsonArray(new JsonObject { ["codigo"] = "J1", ["medidas"] = "1,50 × 1,20 m", ["tipo"] = "Janela de correr", ["area"] = "1,80 m²", ["sem_area"] = "" }),
            ["minimo"] = new JsonArray(new JsonObject { ["texto"] = "12,00 ÷ 6 = 2,00 m²", ["destaque"] = true }, new JsonObject { ["texto"] = "(art. 127)", ["destaque"] = false }),
            ["situacao_titulo"] = "Conferência por ambiente",
            ["textos"] = new JsonArray("A janela atende a iluminação; a ventilação é metade do vão (0,90 m²)."),
            ["resultado"] = new JsonObject { ["total"] = "1,80 m²", ["conta"] = "J1 1,80 m²", ["comparacao"] = "Mínimo 2,00 m² para iluminação; confira a esquadria." },
        };
        var c = Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto());
        pacote = c;
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    /// <summary>O pacote da última importação (as imagens dele servem ao render).</summary>
    [ThreadStatic] private static ConteudoPacote? pacote;

    private static JsonObject Gerado(Projeto p) => JsonFiel.Ler(DadosEfetivos.Gerar(p).Json)["iluminacao"]!["ambientes"]![0]!.AsObject();

    // ---------- leitura e conta ----------

    [Theory]
    [InlineData("1,50 × 1,20 m", 1.5, 1.2)]
    [InlineData("1.96 x 1.26 m", 1.96, 1.26)]
    [InlineData("80x210cm", 0.8, 2.1)]
    [InlineData("80 X 210", 0.8, 2.1)]              // sem unidade e grande demais para metros: centímetros
    [InlineData("0,80 m × 2,10 m", 0.8, 2.1)]
    [InlineData("800 x 2100 mm", 0.8, 2.1)]
    [InlineData("0,8*2,1", 0.8, 2.1)]
    public void medidas_sao_largura_vezes_altura(string texto, double largura, double altura)
    {
        var m = Esquadrias.LerMedidas(texto);
        Assert.NotNull(m);
        Assert.Equal((decimal)largura, m.Value.Largura);
        Assert.Equal((decimal)altura, m.Value.Altura);
    }

    [Fact]
    public void medidas_ilegiveis_nao_viram_conta()
    {
        foreach (var t in new[] { "", "1,50", "largura 1,50", "1,50 × m", "0 × 2,10", "a × b" }) Assert.Null(Esquadrias.LerMedidas(t));
        Assert.Equal("0,80 × 2,10 m", Esquadrias.TextoMedidas(0.8m, 2.1m));
        Assert.Equal(1.68m, Esquadrias.Area(0.8m, 2.1m));
        Assert.Equal("1,68 m²", Esquadrias.TextoArea(1.68m));
        Assert.Equal((12m, 6m, 2m), Esquadrias.LerMinimo(["12,00 ÷ 6 = 2,00 m²", "(art. 127)"]));
        Assert.Equal((null, null, 0.25m), Esquadrias.LerMinimo(["mínimo 0,25 m²"]));
        Assert.Equal((null, null, null), Esquadrias.LerMinimo(["(art. 127)"]));
        // A conta do mínimo é refeita (piso ÷ divisor), não copiada do texto.
        Assert.Equal(2.5m, Esquadrias.LerMinimo(["15,00 ÷ 6 = 9,99 m²"]).Minimo);
        Assert.Equal(CategoriaEsquadria.Porta, Esquadrias.Categoria("P1", "Porta de abrir"));
        Assert.Equal(CategoriaEsquadria.Janela, Esquadrias.Categoria("J4", "Basculante"));
        Assert.Equal(CategoriaEsquadria.Porta, Esquadrias.Categoria("P2", ""));
        Assert.Equal(CategoriaEsquadria.Outro, Esquadrias.Categoria("V1", "Vão livre"));
    }

    [Fact]
    public void conta_do_ambiente_com_os_textos_gravados_e_com_os_que_estao_sendo_digitados()
    {
        var s = Quarto();
        var c = Esquadrias.Calcular(s.Projeto, A)!;
        var j1 = Assert.Single(c.Esquadrias);
        Assert.Equal(("J1", CategoriaEsquadria.Janela, 1.5m, 1.2m, 1.8m), (j1.Codigo, j1.Categoria, j1.Largura, j1.Altura, j1.Area));
        Assert.False(j1.Acrescentada);
        Assert.Equal((1.8m, 12m, 6m, 2m, false), (c.Total, c.Piso, c.Divisor, c.Minimo, c.Atende));
        Assert.Equal("12,00 ÷ 6 = 2,00 m²", c.TextoMinimo);
        Assert.Equal("J1 1,80 m²", c.TextoConta);
        Assert.Equal("Mínimo 2,00 m²; faltam 0,20 m².", c.TextoComparacao);

        // Tempo real: a largura que o usuário está digitando vale na conta, sem gravar nada.
        var antes = ProjetoJson.Escrever(s.Projeto);
        var ao = Esquadrias.Calcular(s.Projeto, A, new Dictionary<string, string> { ["vaos/0/medidas"] = "1,70 × 1,20 m" })!;
        Assert.Equal((2.04m, true), (ao.Total, ao.Atende));
        Assert.Equal("Mínimo 2,00 m²; atende.", ao.TextoComparacao);
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
        Assert.Null(Esquadrias.Calcular(s.Projeto, "indices/01"));
    }

    // ---------- acrescentar a porta (o pedido do usuário) ----------

    [Fact]
    public void acrescentar_a_porta_de_80_por_210_refaz_a_conta_e_a_situacao()
    {
        var s = Quarto("NAO_ATENDE");
        var documento = s.Projeto.Documento.ToJsonString();
        var antes = ProjetoJson.Escrever(s.Projeto);

        var lote = Esquadrias.Recalcular(s.Projeto, A, [Esquadrias.Nova(A, "P1", "Porta de abrir", "80x210cm")], "Esquadria acrescentada: P1");
        var r = s.Aplicar(lote);

        Assert.NotNull(r);
        Assert.Equal(documento, s.Projeto.Documento.ToJsonString());            // o recebido não muda
        Assert.True(TabelasRevisao.Acrescentado(s.Projeto, A, "vaos/1"));
        Assert.Equal(2, TabelasRevisao.Vaos(s.Projeto, A));
        Assert.Equal("0,80 × 2,10 m", TabelasRevisao.TextoEfetivo(s.Projeto, A, "vaos/1/medidas"));
        Assert.Equal("1,68 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "vaos/1/area"));
        Assert.Equal("3,48 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/total"));
        Assert.Equal("J1 1,80 + P1 1,68 = 3,48 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/conta"));
        Assert.Equal("Mínimo 2,00 m²; atende.", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/comparacao"));
        Assert.Equal("CONTA_CONFERE", TabelasRevisao.SituacaoEfetiva(s.Projeto, A));
        Assert.StartsWith(RecalculoLinha.MarcaJustificativa, TabelasRevisao.Justificativa(s.Projeto, A));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
        Assert.True(TabelasRevisao.Revisada(s.Projeto, A));
        Assert.Contains("vaos/1/medidas", TabelasRevisao.Caminhos(TabelasRevisao.Original(s.Projeto, A)!));
        Assert.Equal("Vão 2 — medidas", TabelasRevisao.NomeDoCampo("vaos/1/medidas"));

        // No dados.json efetivo (folha e PDF), a porta é o segundo vão do ambiente; o relatório renderiza.
        var g = Gerado(s.Projeto);
        var vaos = g["vaos"]!.AsArray();
        Assert.Equal(2, vaos.Count);
        Assert.Equal("P1", vaos[1]!["codigo"]!.GetValue<string>());
        Assert.Equal("Porta de abrir", vaos[1]!["tipo"]!.GetValue<string>());
        Assert.Equal("3,48 m²", g["resultado"]!["total"]!.GetValue<string>());
        Assert.Equal("CONTA_CONFERE", g["situacao"]!.GetValue<string>());
        using var pasta = Ambiente.PastaDeTeste("esquadrias");
        pacote!.ExtrairAssets(pasta);
        var render = RelatorioRenderer.Renderizar(DadosEfetivos.Gerar(s.Projeto).Json, Path.Combine(pasta, "dados.json"), new OpcoesRender(false, true), Modelo);
        Assert.True(render.Ok, string.Join("; ", render.Erros));
        Assert.Contains("P1 • 0,80 × 2,10 m", render.Html);

        // projeto.json: vai e volta com o acréscimo.
        var relido = ProjetoJson.Ler(ProjetoJson.Escrever(s.Projeto));
        Assert.Equal(ProjetoJson.Escrever(s.Projeto), ProjetoJson.Escrever(relido));
        Assert.True(TabelasRevisao.Acrescentado(relido, A, "vaos/1"));
        // Histórico e comparação dizem o que foi acrescentado.
        Assert.Equal("Ambiente 01 · Esquadria acrescentada: P1 (6 alterações)", DescricaoHistorico.Texto(r, s.Projeto));
        var d = Assert.Single(Comparacao.DiferencasLinhas(s.Projeto));
        Assert.Contains(d.Campos, c => c.Campo == "vaos/1/medidas" && c.Original == "" && c.Revisado == "0,80 × 2,10 m");

        // Um Desfazer tira a porta e volta a conta, a situação e a justificativa.
        s.Desfazer();
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
        Assert.Equal(1, TabelasRevisao.Vaos(s.Projeto, A));
        s.Refazer();
        Assert.Equal("3,48 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/total"));
    }

    [Fact]
    public void esquadria_acrescentada_se_edita_e_so_a_ultima_se_remove()
    {
        var s = Quarto("NAO_ATENDE");
        s.Aplicar(Esquadrias.Recalcular(s.Projeto, A, [Esquadrias.Nova(A, "P1", "Porta", "0,80 × 2,10")], "x"));
        s.Aplicar(Esquadrias.Recalcular(s.Projeto, A, [Esquadrias.Nova(A, "J2", "Janela", "1,00 × 1,00 m")], "x"));
        Assert.Equal(3, TabelasRevisao.Vaos(s.Projeto, A));
        Assert.Equal("4,48 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/total"));

        // A porta acrescentada se edita como as outras; a conta acompanha.
        s.Aplicar(Esquadrias.Recalcular(s.Projeto, A, [new DefinirCampo(A, "vaos/1/medidas", "0,90 × 2,10 m")], "x"));
        Assert.Equal("1,89 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "vaos/1/area"));
        Assert.Equal("J1 1,80 + P1 1,89 + J2 1,00 = 4,69 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/conta"));

        // Só a última acrescentada sai (as posições das outras não mudam); o que veio no relatório nunca sai.
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new RemoverAcrescimo(A, "vaos/1")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new RemoverAcrescimo(A, "vaos/0")));
        var estado = ProjetoJson.Escrever(s.Projeto);
        s.Aplicar(Esquadrias.Recalcular(s.Projeto, A, [new RemoverAcrescimo(A, "vaos/2")], "Esquadria removida: J2"));
        Assert.Equal(2, TabelasRevisao.Vaos(s.Projeto, A));
        Assert.Equal("3,69 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/total"));
        s.Desfazer();
        Assert.Equal(estado, ProjetoJson.Escrever(s.Projeto));
        // Removida a porta editada, o Desfazer devolve também a edição dela.
        s.Aplicar(new RemoverAcrescimo(A, "vaos/2"));
        s.Aplicar(new RemoverAcrescimo(A, "vaos/1"));
        Assert.False(s.Projeto.Linhas[A].Campos.ContainsKey("vaos/1/medidas"));
        s.Desfazer();
        Assert.Equal("0,90 × 2,10 m", TabelasRevisao.TextoEfetivo(s.Projeto, A, "vaos/1/medidas"));
    }

    [Fact]
    public void acrescimos_invalidos_sao_recusados()
    {
        var s = Quarto();
        Assert.Throws<ComandoRecusadoException>(() => Esquadrias.Nova(A, "P1", "Porta", "grande"));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarVao("indices/01", "P1", "0,80 × 2,10 m", "Porta", "1,68 m²")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarVao("ambientes/99", "P1", "0,80 × 2,10 m", "Porta", "1,68 m²")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarVao(A, " ", "0,80 × 2,10 m", "Porta", "1,68 m²")));
        Assert.False(s.PodeDesfazer);
        Assert.True(TabelasRevisao.EhChaveDeAcrescimo("ambientes/01/vaos/2"));
        Assert.True(TabelasRevisao.EhChaveDeAcrescimo("chamadas/07"));
        Assert.True(TabelasRevisao.EhChaveDeAcrescimo("ambientes/13"));   // ambiente inteiro acrescentado (2026-11-16)
        foreach (var k in new[] { "indices/07", "ambientes/01/minimo/2", "ambientes/01/vaos/x", "ambientes/01/vaos/1/tipo", "x", "chamadas/7" })
            Assert.False(TabelasRevisao.EhChaveDeAcrescimo(k), k);
        Assert.Throws<System.Text.Json.JsonException>(() => ProjetoJson.Ler(System.Text.Encoding.UTF8.GetBytes(
            "{\"versao_esquema\":\"1\",\"itens\":[],\"documento\":{},\"acrescimos\":{\"indices/09\":{}}}")));
    }

    // ---------- editar as medidas à mão (na folha ou no painel) ----------

    [Fact]
    public void editar_as_medidas_recalcula_area_total_e_situacao()
    {
        var s = Quarto("NAO_ATENDE");
        var e = RecalculoLinha.AoEditar(s.Projeto, A, "vaos/0/medidas", "1,70 × 1,20 m");
        Assert.True(e.Recalculado);
        Assert.Contains("vão total 2,04 m²", e.Mensagem);
        s.Aplicar(e.Comando);
        Assert.Equal("2,04 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "vaos/0/area"));
        Assert.Equal("2,04 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/total"));
        Assert.Equal("CONTA_CONFERE", TabelasRevisao.SituacaoEfetiva(s.Projeto, A));
        // O texto livre da situação não é reescrito pelo editor.
        Assert.Equal("A janela atende a iluminação; a ventilação é metade do vão (0,90 m²).", TabelasRevisao.TextoEfetivo(s.Projeto, A, "textos/0"));
        s.Desfazer();
        Assert.Equal("1,80 m²", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/total"));
        Assert.Equal("NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(s.Projeto, A));

        // Medidas que não são largura × altura: só o texto entra, com o aviso.
        var ruim = RecalculoLinha.AoEditar(s.Projeto, A, "vaos/0/medidas", "ver quadro");
        Assert.True(ruim.NaoInterpretado);
        Assert.IsType<DefinirCampo>(ruim.Comando);
        // O piso mudou no texto do mínimo: o mínimo é refeito e a comparação acompanha.
        var piso = RecalculoLinha.AoEditar(s.Projeto, A, "minimo/0/texto", "9,00 ÷ 6 = 1,50 m²");
        Assert.True(piso.Recalculado);
        s.Aplicar(piso.Comando);
        Assert.Equal("Mínimo 1,50 m²; atende.", TabelasRevisao.TextoEfetivo(s.Projeto, A, "resultado/comparacao"));
        Assert.Equal("CONTA_CONFERE", TabelasRevisao.SituacaoEfetiva(s.Projeto, A));
        // Outros textos do ambiente não disparam conta nenhuma.
        Assert.IsType<DefinirCampo>(RecalculoLinha.AoEditar(s.Projeto, A, "nome", "Quarto 2").Comando);
    }

    // ---------- a IA pede a esquadria; o editor faz a conta ----------

    [Fact]
    public void a_ia_devolve_a_esquadria_nova_e_o_pedido_explica_o_formato()
    {
        var linhas = new Dictionary<string, Dictionary<string, string>> { [A] = new() { ["nome"] = "Quarto 1", ["situacao"] = "NAO_ATENDE" } };
        var pedido = IaRecalculoIndices.Prompt("ambientes", A, linhas, "adicionar cálculo da porta de 80x210cm e rever se atende", []);
        Assert.Contains("novos_vaos", pedido);
        Assert.Contains("O EDITOR calcula a área", pedido);
        Assert.DoesNotContain("novos_vaos", IaRecalculoIndices.Prompt("indices", "indices/01", linhas, "rever", []));

        var saida = "{\"ambientes/01\": {\"novos_vaos\": [{\"codigo\": \"P1\", \"tipo\": \"Porta\", \"medidas\": \"0,80 × 2,10 m\"}, {\"codigo\": \"X\"}], \"textos/0\": \"A janela e a porta atendem a iluminação.\"}}";
        var vaos = IaRecalculoIndices.LerNovosVaos(saida, linhas.Keys);
        Assert.Equal(("P1", "Porta", "0,80 × 2,10 m"), Assert.Single(vaos[A]));   // sem medidas, a entrada não vale
        var textos = IaRecalculoIndices.Ler(saida, linhas.Keys)!;
        Assert.Equal("A janela e a porta atendem a iluminação.", textos[A]["textos/0"]);
        Assert.False(textos[A].ContainsKey("novos_vaos"));
        Assert.Empty(IaRecalculoIndices.LerNovosVaos("{\"outra/01\": {}}", linhas.Keys));
    }
}
