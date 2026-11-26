using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Pmvni.App;
using Pmvni.Composicao;
using Pmvni.Revisao.Testes.DespachosTestes;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Tarefa 15: o despacho de correção e o "Rever" das tabelas de índices e de esquadrias são feitos em código, sem IA por
/// padrão; a IA fica só sob comando ("Ajustar com IA", a instrução do despacho ou uma instrução livre digitada na justificativa). E o C#
/// dá o mesmo resultado do Python do kit nos casos-ouro (tests/referencias/casos-ouro.json, copiado do kit).</summary>
public class SemIaPorPadraoTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    static readonly DateOnly Hoje = new(2099, 3, 10);
    static readonly EtapaDespacho Etapa = new("DESPACHO 03 - 2099-03-10", "DESPACHO 03 - 2099-03-10", 3, Hoje, true);

    static string Referencia(string nome) => Path.Combine(Ambiente.PastaTestes, "referencias", nome);

    static string FonteApp(params string[] partes) => Path.Combine([Path.GetDirectoryName(Ambiente.PastaTestes)!, "src", "Pmvni.App", .. partes]);

    // ---------- despacho de correção ----------

    [Fact]
    public void Resumo_SoPendentes()
    {
        var itens = new ResumoExigencias.Item[]
        {
            new("a", 1, "ATENDE", true, "Nada a corrigir.", "LC 70/1990"),
            new("b", 2, "NAO_ATENDE", true, "Apresentar a cota do recuo frontal.", "LC 70/1990, art. 12."),
            new("c", 3, "NAO_COMPROVADO", false, "Item excluído pelo revisor.", ""),
            new("d", 4, "DIVERGENCIA_INTERNA", true, "Corrigir a área do quadro", ""),
            new("e", 5, "NAO_COMPROVADO", true, "Apresentar a matrícula atualizada", "Lei 1.382/2020"),
        };

        Assert.Equal(["b", "d", "e"], ResumoExigencias.Pendentes(itens).Select(i => i.Id));
        Assert.Equal(
            "01. Apresentar a cota do recuo frontal (LC 70/1990, art. 12).\n" +
            "02. Corrigir a área do quadro\n" +
            "03. Apresentar a matrícula atualizada (Lei 1.382/2020)",
            ResumoExigencias.Montar(itens));
        Assert.Equal("", ResumoExigencias.Montar([itens[0], itens[2]]));

        // No relatório revisado: a situação revisada vale, o excluído sai e a ordem é a do relatório.
        var c = RelatorioFicticio.Curto();
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        string id1 = s.Projeto.Incluidos[0].Id, id2 = s.Projeto.Incluidos[1].Id, id3 = s.Projeto.Incluidos[2].Id;
        Assert.Equal([id2, id3], ResumoExigencias.Pendentes(ResumoExigencias.DoProjeto(s.Projeto)).Select(i => i.Id));
        s.Aplicar(new DefinirSituacao(id1, "NAO_ATENDE"));
        s.Aplicar(new DefinirJustificativa(id1, "Revisado no teste."));
        s.Aplicar(new Excluir(id2));
        var pendentes = ResumoExigencias.Pendentes(ResumoExigencias.DoProjeto(s.Projeto));
        Assert.Equal([id1, id3], pendentes.Select(i => i.Id));
        Assert.Equal([1, 2], pendentes.Select(i => i.Numero));
        Assert.StartsWith("01. ", ResumoExigencias.Montar(ResumoExigencias.DoProjeto(s.Projeto)));
    }

    [Fact]
    public void Correcao_NaoChamaIA()
    {
        // Montar o despacho de correção é só código: as exigências são as do ResumoExigencias, na ordem, com os textos do relatório.
        var c = RelatorioFicticio.Curto();
        var r = ConteudoDespacho.Montar(TipoDespacho.Correcao, c.Projeto, ConteudoDespachoTestes.Info(TipoDespacho.Correcao), Etapa);
        var pendentes = ResumoExigencias.Pendentes(ResumoExigencias.DoProjeto(c.Projeto));
        Assert.Equal(pendentes.Select(x => (x.Id, x.Numero, x.Situacao, x.Texto, x.Fundamento)),
            r.Pendencias.Select(x => (x.ItemId, x.Numero, x.Situacao, x.Texto, x.Fundamento)));
        Assert.All(r.Pendencias, x => Assert.False(x.Editada));

        // O pedido automático de resumo à IA não existe mais, e o Novo despacho só chama a IA com o texto da caixa de instrução.
        Assert.Null(typeof(IaDespacho).GetField("PedidoResumo", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static));
        var fonte = File.ReadAllText(FonteApp("MainWindow.NovoDespacho.cs"));
        Assert.DoesNotContain("PedidoResumo", fonte, StringComparison.Ordinal);
        var chamadas = fonte.Split("AplicarInstrucaoAsync(").Skip(1).ToList();
        Assert.Equal(3, chamadas.Count);   // o clique, o Enter e a definição
        Assert.Equal(2, chamadas.Count(x => x.StartsWith("n.CaixaInstrucao.Text)", StringComparison.Ordinal)));
        Assert.Single(chamadas, x => x.StartsWith("string instrucao)", StringComparison.Ordinal));
        Assert.Contains("MostrarMensagem(InfoBarSeverity.Informational, FluxoNovoDespacho.TituloExigenciasSemIa, FluxoNovoDespacho.ExigenciasSemIa(r));", fonte, StringComparison.Ordinal);

        // A IA continua sob comando: a caixa de instrução e o "Ajustar com IA" da exigência.
        Assert.Contains("n.BotaoAplicarInstrucao.Click += async (_, _) => await AplicarInstrucaoAsync(n.CaixaInstrucao.Text);", fonte, StringComparison.Ordinal);
        Assert.Contains("\"Ajustar com IA\"", File.ReadAllText(FonteApp("Paginas", "PaginaNovoDespacho.Folhas.cs")), StringComparison.Ordinal);
        Assert.Contains("Instrução: junte os itens de cotas", IaDespacho.Prompt(r, "junte os itens de cotas"), StringComparison.Ordinal);

        // O aviso diz que entrou sem IA e onde pedir a IA.
        var aviso = FluxoNovoDespacho.ExigenciasSemIa(r);
        Assert.StartsWith("2 exigências do relatório: 1 NÃO ATENDE e 1 FALTA COMPROVAR.", aviso, StringComparison.Ordinal);
        Assert.Contains("sem IA", aviso, StringComparison.Ordinal);
        Assert.Contains("«Ajustar com IA»", aviso, StringComparison.Ordinal);
    }

    // ---------- "Rever" das tabelas ----------

    const string TO = "indices/02", Ambiente01 = "ambientes/01";

    /// <summary>O relatório "longo" com a taxa de ocupação de 240,01 m² ÷ 300,00 m² escrita "80,0%" ao lado de "≤ 80%" e marcada ATENDE
    /// (a conta exata é 80,0033%), e o AMBIENTE 01 com J1 de 1,80 m² e mínimo de 2,00 m² marcado ATENDE.</summary>
    static SessaoRevisao Relatorio(params JsonObject[] maisVaos)
    {
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        var to = dados["indices"]!["linhas"]![1]!.AsObject();
        to["icone"] = "to";
        to["nome"] = "Taxa de ocupação";
        to["calculo"] = "240,01 ÷ 300,00 × 100 = 80,0%";
        to["projeto"] = "80,0%";
        to["limite"] = "≤ 80%";
        to["situacao"] = "CONTA_CONFERE";
        to["texto"] = "Dentro do limite.";
        dados["iluminacao"]!["ambientes"]![0] = new JsonObject
        {
            ["icone"] = "bed", ["nome"] = "Quarto 1", ["detalhes"] = new JsonArray("piso 12,00 m²"), ["situacao"] = "CONTA_CONFERE",
            ["vaos"] = new JsonArray([new JsonObject { ["codigo"] = "J1", ["medidas"] = "1,50 × 1,20 m", ["tipo"] = "Janela de correr", ["area"] = "1,80 m²", ["sem_area"] = "" },
                .. maisVaos]),
            ["minimo"] = new JsonArray(new JsonObject { ["texto"] = "12,00 ÷ 6 = 2,00 m²", ["destaque"] = true }, new JsonObject { ["texto"] = "(art. 127)", ["destaque"] = false }),
            ["situacao_titulo"] = "Conferência por ambiente",
            ["textos"] = new JsonArray("Conferir o vão."),
            ["resultado"] = new JsonObject { ["total"] = "1,80 m²", ["conta"] = "J1 1,80 m²", ["comparacao"] = "Mínimo 2,00 m²; atende." },
        };
        var c = Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto());
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    [Fact]
    public void ReverTabela_RecalculaLocal()
    {
        var s = Relatorio();
        var antes = ProjetoJson.Escrever(s.Projeto);

        // Índices: a conta exata não atende; o "No projeto" e o resultado ganham as casas que mostram isso; antes e depois na mensagem.
        var r = RecalculoLinha.Rever(s.Projeto, TO);
        Assert.NotNull(r.Lote);
        Assert.Contains(new ValorRevisto(TO, "No projeto", "80,0%", "80,003%"), r.Mudancas);
        Assert.Contains(new ValorRevisto(TO, "Cálculo / descrição", "240,01 ÷ 300,00 × 100 = 80,0%", "240,01 ÷ 300,00 × 100 = 80,003%"), r.Mudancas);
        Assert.Contains(new ValorRevisto(TO, "situação", "ATENDE", "NÃO ATENDE"), r.Mudancas);
        Assert.StartsWith("Revisto pelo editor, sem IA: ", r.Mensagem, StringComparison.Ordinal);
        Assert.Contains("ÍNDICE 02 — No projeto: 80,0% → 80,003%", r.Mensagem, StringComparison.Ordinal);
        Assert.Contains("ÍNDICE 02 — situação: ATENDE → NÃO ATENDE", r.Mensagem, StringComparison.Ordinal);
        Assert.EndsWith("Um Desfazer volta tudo.", r.Mensagem, StringComparison.Ordinal);
        s.Aplicar(r.Lote!);
        Assert.Equal("NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(s.Projeto, TO));
        Assert.Equal("Excede em 0,003%.", TabelasRevisao.TextoEfetivo(s.Projeto, TO, "texto"));
        Assert.StartsWith(RecalculoLinha.MarcaJustificativa, TabelasRevisao.Justificativa(s.Projeto, TO));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
        // Rever de novo não muda mais nada.
        var deNovo = RecalculoLinha.Rever(s.Projeto, TO);
        Assert.Null(deNovo.Lote);
        Assert.Contains("nada mudou", deNovo.Mensagem, StringComparison.Ordinal);

        // Esquadrias: 1,80 m² não atende o mínimo de 2,00 m².
        var e = RecalculoLinha.Rever(s.Projeto, Ambiente01);
        Assert.Contains(new ValorRevisto(Ambiente01, "situação", "ATENDE", "NÃO ATENDE"), e.Mudancas);
        Assert.Contains(new ValorRevisto(Ambiente01, "Comparação do vão total", "Mínimo 2,00 m²; atende.", "Mínimo 2,00 m²; faltam 0,20 m²."), e.Mudancas);
        s.Aplicar(e.Lote!);
        Assert.Equal("NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(s.Projeto, Ambiente01));

        // Um Desfazer por "Rever".
        s.Desfazer();
        s.Desfazer();
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));

        // O "Rever" sem IA é das tabelas com conta; a IA é a segunda opção, oferecida só com texto novo na justificativa.
        Assert.True(RecalculoLinha.ReveSemIa(TO) && RecalculoLinha.ReveSemIa(Ambiente01));
        Assert.False(RecalculoLinha.ReveSemIa("chamadas/01"));
        Assert.False(RecalculoLinha.OfereceReverComIa(TO, null, false));
        Assert.False(RecalculoLinha.OfereceReverComIa(TO, "Conferido na prancha 02.", false));   // já atendida
        Assert.True(RecalculoLinha.OfereceReverComIa(TO, "Considerar a projeção de 230,00 m².", true));
        Assert.True(RecalculoLinha.OfereceReverComIa(Ambiente01, "Adicionar a porta de 80x210cm.", true));
        Assert.False(RecalculoLinha.OfereceReverComIa(Ambiente01, RecalculoLinha.MarcaJustificativa + " vão total 1,80 m²; mínimo 2,00 m².", true));
        Assert.False(RecalculoLinha.OfereceReverComIa("chamadas/01", "Marcar o recuo.", true));   // no diagrama o único "Rever" já é com a IA
        Assert.Throws<ArgumentException>(() => RecalculoLinha.Rever(s.Projeto, "chamadas/01"));

        var propriedades = File.ReadAllText(FonteApp("MainWindow.Propriedades.cs"));
        Assert.Contains("R.PropAjustarRotulo.Text = \"Rever com a IA\";", propriedades, StringComparison.Ordinal);
        var revisao = File.ReadAllText(FonteApp("MainWindow.Revisao.cs"));
        Assert.DoesNotContain("await ReverSemIaAsync(", revisao, StringComparison.Ordinal);
        Assert.Contains("await RecalcularTabelaComIaAsync(id);", revisao, StringComparison.Ordinal);
    }

    [Fact]
    public void rever_sem_ia_continua_disponivel_depois_de_trocar_a_situacao_e_justificar()
    {
        // O fluxo comum: o analista troca a situação à mão e, como a conferência exige, justifica. A justificativa fica "pendente"
        // (mudou depois dos textos), mas o "Rever" sem IA continua sendo o botão principal; a IA é só a segunda opção oferecida.
        var s = Relatorio();
        s.Aplicar(new DefinirSituacao(TO, "NAO_ATENDE"));
        Assert.True(TabelasRevisao.FaltaJustificativa(s.Projeto.Linhas[TO]));
        s.Aplicar(new DefinirJustificativa(TO, "Conferido na prancha 02."));
        var pendente = AjustePendente.JustificativaMudou(s.Projeto, s.HistoricoCompleto, TO);
        Assert.True(pendente);
        Assert.True(RecalculoLinha.ReveSemIa(TO));
        Assert.True(RecalculoLinha.OfereceReverComIa(TO, TabelasRevisao.Justificativa(s.Projeto, TO), pendente));

        // E o Rever sem IA funciona: refaz a conta (no projeto e cálculo com as casas fiéis); a situação que o analista deu confere com a
        // conta e fica, assim como a justificativa dele.
        var r = RecalculoLinha.Rever(s.Projeto, TO);
        Assert.NotNull(r.Lote);
        Assert.Contains(new ValorRevisto(TO, "No projeto", "80,0%", "80,003%"), r.Mudancas);
        s.Aplicar(r.Lote!);
        Assert.Equal("NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(s.Projeto, TO));
        Assert.Equal("Conferido na prancha 02.", TabelasRevisao.Justificativa(s.Projeto, TO));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
    }

    [Fact]
    public void ambiente_sem_minimo_legivel_fica_pendente_como_no_kit()
    {
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        dados["iluminacao"]!["ambientes"]![0] = new JsonObject
        {
            ["icone"] = "bed", ["nome"] = "Quarto 1", ["detalhes"] = new JsonArray("Piso não informado"), ["situacao"] = "CONTA_CONFERE",
            ["vaos"] = new JsonArray(new JsonObject { ["codigo"] = "J1", ["medidas"] = "1,50 × 1,20 m", ["tipo"] = "Janela", ["area"] = "1,80 m²", ["sem_area"] = "" }),
            ["minimo"] = new JsonArray(new JsonObject { ["texto"] = "Área do piso não informada", ["destaque"] = true }),
            ["situacao_titulo"] = "", ["textos"] = new JsonArray("Conferir o vão."),
            ["resultado"] = new JsonObject { ["total"] = "1,80 m²", ["conta"] = "J1 1,80 m²", ["comparacao"] = "Área do piso não informada" },
        };
        var c = Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto());
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        var conta = Esquadrias.Calcular(s.Projeto, Ambiente01)!;
        Assert.Null(conta.Minimo);
        Assert.Equal(("PENDENTE", (bool?)null), (conta.Situacao, conta.Atende));
        Assert.Equal("Falta comprovar o mínimo exigido (a área do piso ou a fração).", conta.TextoComparacao);
        // A regra é a do calcular_vaos.py: sem piso, PENDENTE; o resto como antes.
        Assert.Equal("PENDENTE", Esquadrias.SituacaoDoAmbiente(1.8m, null, 0));
        Assert.Equal("CONTA_CONFERE", Esquadrias.SituacaoDoAmbiente(2m, 2m, 1));
        Assert.Equal("PENDENTE", Esquadrias.SituacaoDoAmbiente(1.68m, 2m, 1));
        Assert.Equal("NAO_ATENDE", Esquadrias.SituacaoDoAmbiente(1.68m, 2m, 0));
        Assert.Null(Esquadrias.SituacaoDoAmbiente(null, 2m, 0));

        s.Aplicar(RecalculoLinha.Rever(s.Projeto, Ambiente01).Lote!);
        Assert.Equal("PENDENTE", TabelasRevisao.SituacaoEfetiva(s.Projeto, Ambiente01));
        Assert.Equal(RecalculoLinha.MarcaJustificativa + " vão total 1,80 m²; falta o mínimo exigido (a área do piso ou a fração).",
            TabelasRevisao.Justificativa(s.Projeto, Ambiente01));
    }

    [Fact]
    public void esquadria_sem_medida_deixa_o_ambiente_pendente_e_fora_da_soma_nao_conta()
    {
        // P1 sem medida no relatório (como o kit escreve: medidas e área vazias, "MEDIDA NÃO INFORMADA").
        var s = Relatorio(new JsonObject { ["codigo"] = "P1", ["medidas"] = "", ["tipo"] = "Porta", ["area"] = "", ["sem_area"] = "MEDIDA NÃO INFORMADA" });
        s.Aplicar(RecalculoLinha.Rever(s.Projeto, Ambiente01).Lote!);
        var conta = Esquadrias.Calcular(s.Projeto, Ambiente01)!;
        Assert.Equal(["P1"], conta.SemMedida);
        Assert.Equal(("PENDENTE", (bool?)null), (conta.Situacao, conta.Atende));
        Assert.Equal("Mínimo 2,00 m²; falta a medida de P1.", conta.TextoComparacao);
        Assert.Equal("PENDENTE", TabelasRevisao.SituacaoEfetiva(s.Projeto, Ambiente01));
        Assert.Contains("falta a medida de P1", TabelasRevisao.Justificativa(s.Projeto, Ambiente01));

        // A medida que falta não muda o resultado quando os vãos medidos já alcançam o mínimo (como no kit).
        s.Aplicar(new DefinirCampo(Ambiente01, "vaos/0/medidas", "1,80 × 1,20 m"));
        Assert.Equal("CONTA_CONFERE", Esquadrias.Calcular(s.Projeto, Ambiente01)!.Situacao);

        // "(fora da soma)" no tipo, como o kit escreve: não entra no vão total.
        var fora = Esquadrias.Recalcular(s.Projeto, Ambiente01, [new AcrescentarVao(Ambiente01, "P2", "0,60 × 2,10 m", "Porta — armário (fora da soma)", "1,26 m²")], "P2");
        s.Aplicar(fora);
        var c2 = Esquadrias.Calcular(s.Projeto, Ambiente01)!;
        Assert.True(c2.Esquadrias.Single(x => x.Codigo == "P2").ForaDaSoma);
        Assert.Equal(2.16m, c2.Total);
    }

    [Fact]
    public void editar_o_valor_compara_pela_conta_exata_sem_trocar_o_texto_do_usuario()
    {
        var s = Relatorio();
        s.Aplicar(new DefinirSituacao(TO, "PENDENTE"));
        s.Aplicar(new DefinirJustificativa(TO, "Conferir."));
        var e = RecalculoLinha.AoEditar(s.Projeto, TO, "projeto", "80,0 %");
        s.Aplicar(e.Comando);
        Assert.Equal("80,0 %", TabelasRevisao.TextoEfetivo(s.Projeto, TO, "projeto"));
        Assert.Equal("NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(s.Projeto, TO));
        Assert.Equal("Excede em 0,003%.", TabelasRevisao.TextoEfetivo(s.Projeto, TO, "texto"));
    }

    [Fact]
    public void minimo_exato_aparece_com_as_casas_que_mostram_a_falta()
    {
        var s = ComAmbiente(JsonNode.Parse("""
            {"ambiente": "Cozinha", "piso_m2": 23.06, "vaos": [{"codigo": "J1", "tipo": "JANELA", "largura": 1.2, "altura": 1.0},
             {"codigo": "P1", "tipo": "PORTA", "largura": 0.8, "altura": 2.1}]}
            """)!.AsObject(), (1, 8), "CONTA_CONFERE");
        var c = Esquadrias.Calcular(s.Projeto, Ambiente01)!;
        Assert.Equal((2.88m, 2.8825m, "NAO_ATENDE"), (c.Total!.Value, c.Minimo!.Value, c.Situacao));
        Assert.Equal("23,06 ÷ 8 = 2,883 m²", c.TextoMinimo);
        Assert.Equal("Mínimo 2,883 m²; faltam 0,003 m².", c.TextoComparacao);
        // Longe do limite, duas casas como sempre.
        Assert.Equal("2,00 m²", Esquadrias.Calcular(Relatorio().Projeto, Ambiente01)!.TextoDoMinimo);
    }

    // ---------- casos-ouro: o mesmo resultado do Python do kit ----------

    static string F4(decimal v) => Math.Round(v, 4, MidpointRounding.AwayFromZero).ToString("0.0000", CultureInfo.InvariantCulture);

    static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    static string Br(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("N2", PtBr);

    static IEnumerable<JsonObject> Casos(string funcao) =>
        JsonNode.Parse(File.ReadAllText(Referencia("casos-ouro.json")))!["casos"]!.AsArray().Select(c => c!.AsObject())
            .Where(c => c["funcao"]!.GetValue<string>() == funcao);

    [Fact]
    public void CasosOuro_CSharp()
    {
        var anexo = JsonNode.Parse(File.ReadAllText(Referencia("anexo-vi-zonas.json")))!.AsObject();
        var indices = Casos("calcular_indices").ToList();
        var vaos = Casos("calcular_vaos").ToList();
        Assert.Equal(8, indices.Count);
        Assert.Equal(6, vaos.Count);

        foreach (var caso in indices)
        {
            var id = caso["id"]!.GetValue<string>();
            var entrada = caso["entrada"]!.AsObject();
            var esperado = caso["esperado"]!.AsObject();
            var linha = RecalculoIndices.Calcular(anexo, entrada["zona"]!.GetValue<string>(), entrada["operandos"]!.AsObject())
                .Single(l => l.Icone == esperado["linha"]!.GetValue<string>());
            var valor = esperado["valor"]?.GetValue<string>();
            // O porte e a tabela do relatório usam a MESMA conta (RecalculoLinha.Atende, com o critério lido do texto do limite).
            if (linha.Valor is { } exatoDaConta)
                Assert.True(RecalculoLinha.Atende(exatoDaConta, linha.Icone == "ca" ? "" : "%", RecalculoLinha.LerCriterio(linha.Limite)!) == (linha.Situacao != "NAO_ATENDE"), id);
            Assert.True((linha.Situacao, linha.Valor is { } v ? F4(v) : null) == (esperado["situacao"]!.GetValue<string>(), valor),
                $"{id}: C# deu {linha.Situacao} {linha.Valor}");

            // A mesma linha na tabela do relatório, marcada com a situação errada: o "Rever" sem IA chega à do kit.
            var s = ComIndice(linha, linha.Situacao == "CONTA_CONFERE" ? "NAO_ATENDE" : linha.Situacao == "NAO_ATENDE" ? "CONTA_CONFERE" : linha.Situacao);
            if (RecalculoLinha.Rever(s.Projeto, "indices/01").Lote is { } lote) s.Aplicar(lote);
            Assert.True(TabelasRevisao.SituacaoEfetiva(s.Projeto, "indices/01") == linha.Situacao, $"{id}: o Rever deu {TabelasRevisao.SituacaoEfetiva(s.Projeto, "indices/01")}");
            if (linha.Valor is { } exato)
                Assert.True(ContaIndice.Ler(TabelasRevisao.TextoEfetivo(s.Projeto, "indices/01", "calculo"))!.Calcular() == exato, id);
        }

        foreach (var caso in vaos)
        {
            var id = caso["id"]!.GetValue<string>();
            var amb = caso["entrada"]!["ambientes"]![0]!.AsObject();
            var esperado = caso["esperado"]!.AsObject();
            var fracao = Esquadrias.FracaoArt127(amb["tipo_ambiente"]!.GetValue<string>());
            Assert.True(fracao is not null, id + ": tipo fora da tabela do art. 127");
            Assert.Equal(esperado["fracao"]!.GetValue<string>(), fracao!.Value.Numerador + "/" + fracao.Value.Denominador);

            var s = ComAmbiente(amb, fracao.Value, esperado["situacao"]!.GetValue<string>() == "CONTA_CONFERE" ? "NAO_ATENDE" : "CONTA_CONFERE");
            var conta = Esquadrias.Calcular(s.Projeto, Ambiente01)!;
            Assert.True((conta.Situacao, F4(conta.Total ?? 0m), F4(conta.Minimo!.Value))
                        == (esperado["situacao"]!.GetValue<string>(), esperado["total"]!.GetValue<string>(), esperado["exigido"]!.GetValue<string>()),
                $"{id}: C# deu {conta.Situacao} total {conta.Total} exigido {conta.Minimo}");
            s.Aplicar(RecalculoLinha.Rever(s.Projeto, Ambiente01).Lote!);
            Assert.Equal(esperado["situacao"]!.GetValue<string>(), TabelasRevisao.SituacaoEfetiva(s.Projeto, Ambiente01));
        }
    }

    /// <summary>A tabela do art. 127 do C# é a do kit (regras-de-projeto.json, "tabela_art127", copiado do kit).</summary>
    [Fact]
    public void tabela_do_art_127_igual_a_do_kit()
    {
        var t = JsonNode.Parse(File.ReadAllText(Referencia("regras-de-projeto.json")))!["tabela_art127"]!.AsObject();
        var doKit = t.Where(x => x.Value is JsonObject o && o["iluminacao"] is not null)
            .ToDictionary(x => x.Key, x => x.Value!["iluminacao"]!.GetValue<string>());
        Assert.Equal(doKit.OrderBy(x => x.Key), Esquadrias.TabelaArt127.ToDictionary(x => x.Key, x => x.Value.Numerador + "/" + x.Value.Denominador).OrderBy(x => x.Key));
        Assert.Equal((1, 5), Esquadrias.FracaoArt127("Suíte"));
        Assert.Equal((1, 5), Esquadrias.FracaoArt127("QUARTO"));
        Assert.Equal((1, 10), Esquadrias.FracaoArt127("Corredor"));
        Assert.Null(Esquadrias.FracaoArt127("garagem"));
        Assert.Throws<ArgumentException>(() => RecalculoIndices.ZonaCanonica(JsonNode.Parse(File.ReadAllText(Referencia("anexo-vi-zonas.json")))!.AsObject(), "ZN-1"));
    }

    /// <summary>O "longo" com a 1ª linha dos índices trocada pela linha calculada.</summary>
    static SessaoRevisao ComIndice(RecalculoIndices.IndiceCalculado l, string situacao)
    {
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        var linha = dados["indices"]!["linhas"]![0]!.AsObject();
        linha["icone"] = l.Icone;
        linha["nome"] = l.Nome;
        linha["calculo"] = l.Calculo;
        linha["projeto"] = l.Projeto;
        linha["limite"] = l.Limite;
        linha["situacao"] = situacao;
        linha["texto"] = l.Texto.Length > 0 ? l.Texto : "Dentro do limite.";
        var c = Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto());
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    /// <summary>O "longo" com o AMBIENTE 01 escrito como o calcular_vaos.py escreve a linha (linhas_relatorio).</summary>
    static SessaoRevisao ComAmbiente(JsonObject amb, (int Numerador, int Denominador) fracao, string situacao)
    {
        static decimal? N(JsonNode? n) => n is null ? null : n.GetValue<decimal>();
        var piso = N(amb["piso_m2"])!.Value;
        var vaos = new JsonArray();
        foreach (var v in amb["vaos"]!.AsArray().Select(x => x!.AsObject()))
        {
            var (l, a) = (N(v["largura"]), N(v["altura"]));
            var medida = l is not null && a is not null;
            vaos.Add(new JsonObject
            {
                ["codigo"] = v["codigo"]!.GetValue<string>(),
                ["medidas"] = medida ? Br(l!.Value) + " × " + Br(a!.Value) + " m" : "",
                ["tipo"] = v["tipo"]!.GetValue<string>() == "PORTA" ? "Porta" : "Janela",
                ["area"] = medida ? Br(l!.Value * a!.Value) + " m²" : "",
                ["sem_area"] = medida ? "" : "MEDIDA NÃO INFORMADA",
            });
        }
        var exigido = piso * fracao.Numerador / fracao.Denominador;
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        dados["iluminacao"]!["ambientes"]![0] = new JsonObject
        {
            ["icone"] = "sun", ["nome"] = amb["ambiente"]!.GetValue<string>(), ["detalhes"] = new JsonArray("Piso: " + Br(piso) + " m²"),
            ["situacao"] = situacao, ["vaos"] = vaos,
            ["minimo"] = new JsonArray(new JsonObject { ["texto"] = Br(piso) + " ÷ " + fracao.Denominador + " = " + Br(exigido) + " m²", ["destaque"] = true },
                new JsonObject { ["texto"] = "(LC 70/1990, art. 127)", ["destaque"] = false }),
            ["situacao_titulo"] = "",
            ["textos"] = new JsonArray("Conferir o vão."),
            ["resultado"] = new JsonObject { ["total"] = "—", ["conta"] = "—", ["comparacao"] = "—" },
        };
        var c = Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto());
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }
}
