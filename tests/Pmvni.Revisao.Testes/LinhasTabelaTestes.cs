using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Linhas das tabelas ÍNDICES URBANÍSTICOS e ILUMINAÇÃO E VENTILAÇÃO editáveis como os itens: ids estáveis, comandos,
/// desfazer/refazer, dados.json efetivo, projeto.json, histórico, comparação e conferência.</summary>
public class LinhasTabelaTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    private static readonly OpcoesRender Rascunho = new(false, true);

    private static ConteudoPacote Importar(string caso)
    {
        var caminho = Ambiente.Corpus(caso + ".json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    private static SessaoRevisao Sessao(ConteudoPacote c) => new(c.Projeto, c.Historico, Ambiente.Contexto());

    private static JsonObject Gerado(Projeto p) => JsonFiel.Ler(DadosEfetivos.Gerar(p).Json);

    [Theory]
    [InlineData("03/02/2026")]
    [InlineData("")]
    [InlineData(null)]
    public void protocolo_da_capa_preserva_original_e_rejeita_os_demais_campos(string? original)
    {
        var s = Sessao(Importar("curto"));
        const string id = TabelasRevisao.ProtocoloCapa;
        var capa = s.Projeto.Documento["capa"]!.AsObject();
        if (original is null) capa.Remove("protocolo"); else capa["protocolo"] = original;
        var antes = capa.DeepClone();
        s.Aplicar(new DefinirCampo(id, "protocolo", "PROTO 42/2026"));
        Assert.Equal("PROTO 42/2026", Gerado(s.Projeto)["capa"]!["protocolo"]!.GetValue<string>());
        Assert.True(JsonNode.DeepEquals(antes, capa));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo(id, "processo", "outro")));
        s.Desfazer();
        Assert.True(JsonNode.DeepEquals(antes, Gerado(s.Projeto)["capa"]));
        s.Refazer();
        Assert.Equal("PROTO 42/2026", TabelasRevisao.TextoEfetivo(s.Projeto, id, "protocolo"));
    }

    // ---------- ids ----------

    [Fact]
    public void ids_das_linhas_sao_o_bloco_e_a_posicao()
    {
        var p = Importar("longo").Projeto;
        var ids = TabelasRevisao.Ids(p);
        // 6 índices, 12 ambientes e, desde 2026-11-16, o diagrama: 6 chamadas, a planta e a volumetria.
        Assert.Equal(6 + 12 + 6 + 2 + 1, ids.Count);
        Assert.Equal("indices/01", ids[0]);
        Assert.Equal("indices/06", ids[5]);
        Assert.Equal("ambientes/01", ids[6]);
        Assert.Equal("ambientes/12", ids[17]);
        Assert.Equal("chamadas/01", ids[18]);
        Assert.Equal("planta/01", ids[24]);
        Assert.Equal("volumetria/01", ids[25]);
        Assert.Equal(TabelasRevisao.ProtocoloCapa, ids[26]);
        // A volumetria ganha a forma (pavimentos, telhado, destaque) mesmo que o original não a tenha; o documento não muda.
        Assert.Equal("2", TabelasRevisao.TextoOriginal(p, "volumetria/01", "pavimentos"));
        Assert.True(TabelasRevisao.Editavel(p, "volumetria/01", "telhado"));
        Assert.False(TabelasRevisao.Editavel(p, "chamadas/01", "ancora"));
        Assert.Null((p.Documento["diagrama"]!["volumetria"] as System.Text.Json.Nodes.JsonObject)!["pavimentos"]);
        Assert.True(TabelasRevisao.EhLinha("ambientes/03"));
        foreach (var x in new[] { "indices/1", "indices/00", "indices/x1", "figuras/01", "it_01", "indices/", "/01", "indices/001" })
            Assert.False(TabelasRevisao.EhLinha(x), x);
        Assert.False(TabelasRevisao.Existe(p, "indices/07"));
        Assert.Equal(TabelasRevisao.ProtocoloCapa, Assert.Single(TabelasRevisao.Ids(Importar("curto").Projeto)));
        Assert.Equal("ÍNDICE 02", TabelasRevisao.Rotulo("indices/02"));
        Assert.Equal("AMBIENTE 10", TabelasRevisao.Rotulo("ambientes/10"));
    }

    [Fact]
    public void caminhos_editaveis_sao_os_textos_nao_vazios()
    {
        var p = Importar("longo").Projeto;
        Assert.Equal(new[] { "nome", "descricao", "calculo", "projeto", "limite", "texto" }, TabelasRevisao.Caminhos(TabelasRevisao.Original(p, "indices/02")!));
        var amb = TabelasRevisao.Caminhos(TabelasRevisao.Original(p, "ambientes/03")!);
        Assert.Contains("nome", amb);
        Assert.Contains("detalhes/1", amb);
        Assert.Contains("vaos/0/sem_area", amb);
        Assert.DoesNotContain("vaos/0/area", amb); // vazio no original
        Assert.Contains("vaos/1/area", amb);
        Assert.Contains("minimo/0/texto", amb);
        Assert.Contains("situacao_titulo", amb);
        Assert.Contains("textos/0", amb);
        Assert.DoesNotContain("icone", amb);
        Assert.DoesNotContain("situacao", amb);
        Assert.Equal("Vão 2 — medidas", TabelasRevisao.NomeDoCampo("vaos/1/medidas"));
        Assert.Equal("Cálculo / descrição", TabelasRevisao.NomeDoCampo("calculo"));
    }

    // ---------- texto ----------

    [Fact]
    public void editar_texto_da_linha_muda_so_esse_texto_e_desfazer_volta_aos_mesmos_bytes()
    {
        var c = Importar("longo");
        var s = Sessao(c);
        var antesProjeto = ProjetoJson.Escrever(s.Projeto);
        var antesDados = DadosEfetivos.Gerar(s.Projeto).Json;

        var r = s.Aplicar(new DefinirCampo("indices/02", "calculo", "  711,82 ÷ 44.998,00 × 100 = 1,58%\r\n"));

        Assert.NotNull(r);
        Assert.Equal("indices/02", r.Item);
        Assert.Equal("calculo", r.Campo);
        Assert.Equal("188,00 ÷ 480,00 × 100 = 14%", r.Antes!["texto"]!.GetValue<string>());
        Assert.Equal("711,82 ÷ 44.998,00 × 100 = 1,58%", r.Depois!["texto"]!.GetValue<string>());
        Assert.Equal("711,82 ÷ 44.998,00 × 100 = 1,58%", TabelasRevisao.TextoEfetivo(s.Projeto, "indices/02", "calculo"));
        Assert.True(TabelasRevisao.Revisada(s.Projeto, "indices/02"));

        // O dados.json efetivo: a linha 2 tem o texto novo; o resto é igual ao original (chaves na mesma ordem).
        var g = Gerado(s.Projeto);
        var linha = g["indices"]!["linhas"]![1]!.AsObject();
        Assert.Equal("711,82 ÷ 44.998,00 × 100 = 1,58%", linha["calculo"]!.GetValue<string>());
        Assert.Equal(new[] { "icone", "nome", "descricao", "calculo", "projeto", "projeto_destaque", "limite", "situacao", "texto" }, linha.Select(k => k.Key));
        linha["calculo"] = "188,00 ÷ 480,00 × 100 = 14%";
        Assert.Equal(antesDados, JsonFiel.Escrever(g));
        // O original recebido não muda.
        Assert.Equal("188,00 ÷ 480,00 × 100 = 14%", TabelasRevisao.TextoOriginal(s.Projeto, "indices/02", "calculo"));

        s.Desfazer();
        Assert.Equal(antesProjeto, ProjetoJson.Escrever(s.Projeto));
        Assert.Equal(antesDados, DadosEfetivos.Gerar(s.Projeto).Json);
        s.Refazer();
        Assert.Equal("711,82 ÷ 44.998,00 × 100 = 1,58%", TabelasRevisao.TextoEfetivo(s.Projeto, "indices/02", "calculo"));
    }

    [Fact]
    public void editar_textos_aninhados_do_ambiente()
    {
        var c = Importar("longo");
        var s = Sessao(c);
        s.Aplicar(new DefinirCampo("ambientes/03", "vaos/1/medidas", "2,00 × 1,70 m"));
        s.Aplicar(new DefinirCampo("ambientes/03", "detalhes/0", "4 ambientes • piso 12,00 m²"));
        s.Aplicar(new DefinirCampo("ambientes/03", "minimo/0/texto", "12,00 ÷ 5 = 2,40 m²"));

        var a = Gerado(s.Projeto)["iluminacao"]!["ambientes"]![2]!;
        Assert.Equal("2,00 × 1,70 m", a["vaos"]![1]!["medidas"]!.GetValue<string>());
        Assert.Equal("4 ambientes • piso 12,00 m²", a["detalhes"]![0]!.GetValue<string>());
        Assert.Equal("Pavimento tipo", a["detalhes"]![1]!.GetValue<string>());
        Assert.Equal("12,00 ÷ 5 = 2,40 m²", a["minimo"]![0]!["texto"]!.GetValue<string>());
        Assert.True(a["minimo"]![0]!["destaque"]!.GetValue<bool>());

        // O HTML sai com os textos novos e a conferência do kit aceita.
        using var pasta = Ambiente.PastaDeTeste("linhas-html");
        c.ExtrairAssets(pasta);
        var html = RelatorioRenderer.Renderizar(DadosEfetivos.Gerar(s.Projeto).Json, Path.Combine(pasta, "dados.json"), Rascunho, Modelo);
        Assert.True(html.Ok, string.Join("; ", html.Erros));
        Assert.Contains("J2 • 2,00 × 1,70 m", html.Html);
        Assert.Contains("12,00 ÷ 5 = 2,40 m²", html.Html);
    }

    [Fact]
    public void voltar_ao_texto_original_zera_a_revisao()
    {
        var s = Sessao(Importar("longo"));
        var antes = ProjetoJson.Escrever(s.Projeto);
        s.Aplicar(new DefinirCampo("indices/01", "limite", "Lote ≥ 360 m²"));
        s.Aplicar(new DefinirCampo("indices/01", "limite", TabelasRevisao.TextoOriginal(s.Projeto, "indices/01", "limite")!));
        Assert.False(TabelasRevisao.Revisada(s.Projeto, "indices/01"));
        Assert.Empty(s.Projeto.Linhas);
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
    }

    [Fact]
    public void campo_inexistente_vazio_e_exclusao_sao_recusados()
    {
        var s = Sessao(Importar("longo"));
        var antes = ProjetoJson.Escrever(s.Projeto);
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo("indices/02", "icone", "x")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo("indices/02", "situacao", "x")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo("indices/02", "inventado", "x")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo("ambientes/03", "vaos/0/area", "1,00 m²"))); // vazio no original
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo("indices/02", "calculo", " \r\n ")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo("indices/09", "calculo", "x")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new Excluir("indices/02")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new Restaurar("indices/02")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirSituacao("indices/02", "ATENDE"))); // código de item
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
        Assert.False(s.PodeDesfazer);
    }

    // ---------- situação ----------

    [Fact]
    public void decisao_do_analista_fica_editavel_e_a_exportacao_segue_a_justificativa()
    {
        const string situacao = "DECISAO_DO_ANALISTA", id = "F13-TESTE", nota = "O ANALISTA DEVE DECIDIR. Conferir a peça.";
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonNode.Parse(File.ReadAllBytes(caminho))!.AsObject();
        var item = dados["itens"]![0]!.AsObject();
        item["id"] = id;
        item["situacao"] = situacao;
        item["base_legal"] = nota;
        dados["notas_pendencias_normativas"] = new JsonObject { [id] = nota };
        var linha = dados["iluminacao"]!["ambientes"]![0]!.AsObject();
        linha["apresentacao"] = new JsonObject { ["situacao"] = situacao, ["nota"] = nota };
        dados["deficits_vaos"] = new JsonArray(JsonValue.Create(nota));
        dados["ocorrencias_vaos"] = new JsonArray(new JsonObject { ["nota"] = nota, ["ambiente"] = linha["nome"]!.ToString() });
        var s = Sessao(Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto()));
        string linhaId = "ambientes/01", itemId = s.Projeto.Incluidos.First().Id;
        Assert.Equal(situacao, TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(s.Projeto, linhaId)));
        Assert.Equal(situacao, TabelasRevisao.SituacaoDeLinha(situacao));
        Assert.DoesNotContain(ResumoExigencias.Pendentes(ResumoExigencias.DoProjeto(s.Projeto)), i => i.Id == itemId);
        s.Aplicar(new DefinirSituacao(linhaId, "CONTA_CONFERE"));
        s.Aplicar(new DefinirJustificativa(linhaId, "O analista conferiu a abertura na peça."));
        s.Aplicar(new DefinirSituacao(itemId, "NAO_ATENDE"));
        s.Aplicar(new DefinirJustificativa(itemId, "A prancha revisada demonstra o déficit."));
        var gerado = JsonNode.Parse(DadosEfetivos.Gerar(s.Projeto).Json)!;
        Assert.Equal("CONTA_CONFERE", gerado["iluminacao"]!["ambientes"]![0]!["apresentacao"]!["situacao"]!.ToString());
        Assert.Contains("O analista conferiu", gerado["deficits_vaos"]![0]!.ToString());
        Assert.Null(gerado["notas_pendencias_normativas"]![id]);
        Assert.Equal("A prancha revisada demonstra o déficit.", gerado["itens"]![0]!["base_legal"]!.ToString());
        Assert.Contains(ResumoExigencias.Pendentes(ResumoExigencias.DoProjeto(s.Projeto)), i => i.Id == itemId);
        Assert.Equal(nota, s.Projeto.Documento["notas_pendencias_normativas"]![id]!.ToString());
    }

    [Fact]
    public void situacao_da_linha_com_justificativa_e_conferencia()
    {
        var s = Sessao(Importar("longo"));
        Assert.Equal("NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(s.Projeto, "indices/02"));

        var r = s.Aplicar(new DefinirSituacao("indices/02", "CONTA_CONFERE"));
        Assert.Equal("NAO_ATENDE", r!.Antes!.GetValue<string>());
        Assert.Equal("CONTA_CONFERE", r.Depois!.GetValue<string>());
        Assert.Equal("CONTA_CONFERE", Gerado(s.Projeto)["indices"]!["linhas"]![1]!["situacao"]!.GetValue<string>());

        // Sem justificativa: erro da conferência final, como nos itens.
        var erro = Assert.Single(ConferenciaRevisao.Erros(s.Projeto));
        Assert.Equal("ÍNDICE 02: a situação mudou de NÃO ATENDE para ATENDE sem justificativa", erro);

        s.Aplicar(new DefinirJustificativa("indices/02", "A conta confere com a parcela."));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));

        // Voltar à original zera a revisão; a justificativa digitada fica.
        s.Aplicar(new DefinirSituacao("indices/02", "NAO_ATENDE"));
        Assert.False(TabelasRevisao.Revisada(s.Projeto, "indices/02"));
        Assert.Equal("A conta confere com a parcela.", TabelasRevisao.Justificativa(s.Projeto, "indices/02"));

        Assert.Equal("CONTA_CONFERE", TabelasRevisao.SituacaoDeLinha("ATENDE"));
        Assert.Equal("NAO_COMPROVADO", TabelasRevisao.SituacaoDeItem("PENDENTE"));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirSituacao("ambientes/01", "CONFIRMADO")));
    }

    [Fact]
    public void restaurar_a_linha_inteira_e_desfazer()
    {
        var s = Sessao(Importar("longo"));
        s.Aplicar(new DefinirSituacao("ambientes/02", "DIVERGENCIA"));
        s.Aplicar(new DefinirCampo("ambientes/02", "nome", "Banheiros"));
        s.Aplicar(new DefinirJustificativa("ambientes/02", "Vão não confere."));
        var revisado = ProjetoJson.Escrever(s.Projeto);

        var r = s.Aplicar(new RestaurarItemOriginal("ambientes/02"));
        Assert.NotNull(r);
        Assert.False(TabelasRevisao.Revisada(s.Projeto, "ambientes/02"));
        Assert.Equal("Vão não confere.", TabelasRevisao.Justificativa(s.Projeto, "ambientes/02"));
        Assert.Null(s.Aplicar(new RestaurarItemOriginal("ambientes/02")));

        s.Desfazer();
        Assert.Equal(revisado, ProjetoJson.Escrever(s.Projeto));

        s.Aplicar(new RestaurarCampo("ambientes/02", "nome"));
        Assert.Equal(TabelasRevisao.TextoOriginal(s.Projeto, "ambientes/02", "nome"), TabelasRevisao.TextoEfetivo(s.Projeto, "ambientes/02", "nome"));
        Assert.Null(s.Aplicar(new RestaurarCampo("ambientes/02", "nome")));
    }

    // ---------- gravação, histórico e comparação ----------

    [Fact]
    public void projeto_json_guarda_as_linhas_so_quando_ha_revisao()
    {
        var c = Importar("longo");
        var sem = ProjetoJson.Escrever(c.Projeto);
        Assert.False(JsonFiel.Ler(sem).ContainsKey("linhas"));
        Assert.Equal(sem, ProjetoJson.Escrever(ProjetoJson.Ler(sem)));

        var s = Sessao(c);
        s.Aplicar(new DefinirCampo("ambientes/03", "vaos/1/tipo", "Correr, duas folhas"));
        s.Aplicar(new DefinirSituacao("indices/04", "PENDENTE"));
        s.Aplicar(new DefinirJustificativa("indices/04", "Falta a área permeável."));
        var com = ProjetoJson.Escrever(s.Projeto);
        var lido = ProjetoJson.Ler(com);
        Assert.Equal(com, ProjetoJson.Escrever(lido));
        Assert.Equal("Correr, duas folhas", TabelasRevisao.TextoEfetivo(lido, "ambientes/03", "vaos/1/tipo"));
        Assert.Equal("PENDENTE", TabelasRevisao.SituacaoEfetiva(lido, "indices/04"));
        Assert.Equal("Falta a área permeável.", TabelasRevisao.Justificativa(lido, "indices/04"));
        Assert.Equal(DadosEfetivos.Gerar(s.Projeto).Json, DadosEfetivos.Gerar(lido).Json);
    }

    [Fact]
    public void pacote_gravado_e_reaberto_mantem_as_linhas_e_o_diario_reaplica()
    {
        var c = Importar("longo");
        var original = c.Projeto.Clonar();
        var s = Sessao(c);
        s.Aplicar(new DefinirCampo("indices/01", "projeto", "44.998,00 m²"));
        using var pasta = Ambiente.PastaDeTeste("linhas-pacote");
        var arquivo = Path.Combine(pasta, "r.rpmvni");
        Pacote.Gravar(arquivo, c with { Projeto = s.Projeto.Clonar(), Historico = s.HistoricoCompleto.ToList() }, null);
        var aberto = Pacote.Ler(arquivo).Conteudo;
        Assert.Equal("44.998,00 m²", TabelasRevisao.TextoEfetivo(aberto.Projeto, "indices/01", "projeto"));

        // O diário de recuperação: os registros reaplicados sobre o original refazem a linha.
        var novo = new SessaoRevisao(original, c.Historico, Ambiente.Contexto());
        novo.Reaplicar(s.HistoricoCompleto);
        Assert.Equal(ProjetoJson.Escrever(s.Projeto), ProjetoJson.Escrever(novo.Projeto));
        Assert.True(novo.PodeDesfazer);
        novo.Desfazer();
        Assert.Empty(novo.Projeto.Linhas);
    }

    [Fact]
    public void historico_e_comparacao_descrevem_as_linhas()
    {
        var s = Sessao(Importar("longo"));
        s.Aplicar(new DefinirSituacao("indices/02", "CONTA_CONFERE"));
        s.Aplicar(new DefinirJustificativa("indices/02", "Confere."));
        s.Aplicar(new DefinirCampo("ambientes/03", "vaos/1/medidas", "2,00 × 1,70 m"));
        s.Desfazer();
        var textos = s.HistoricoCompleto.Select(r => DescricaoHistorico.Texto(r, s.Projeto)).ToList();
        Assert.Contains("Índice 02 · Situação: NÃO ATENDE → ATENDE", textos);
        Assert.Contains("Índice 02 · Justificativa definida", textos);
        Assert.Contains("Ambiente 03 · Vão 2 — medidas: texto editado", textos);
        Assert.Contains("Desfeito: Ambiente 03 · Vão 2 — medidas: texto editado", textos);

        s.Refazer();
        var dif = Comparacao.DiferencasLinhas(s.Projeto);
        Assert.Equal(new[] { "indices/02", "ambientes/03" }, dif.Select(d => d.Linha));
        Assert.Equal("NAO_ATENDE", dif[0].SituacaoOriginal);
        Assert.Equal("CONTA_CONFERE", dif[0].SituacaoRevisada);
        Assert.Equal("Confere.", dif[0].Justificativa);
        var campo = Assert.Single(dif[1].Campos);
        Assert.Equal("1.84 × 0.72 m", campo.Original);
        Assert.Equal("2,00 × 1,70 m", campo.Revisado);

        var texto = Comparacao.Texto(Comparacao.Diferencas(s.Projeto, s.HistoricoCompleto), [], dif);
        Assert.Contains("2 linhas de tabela alteradas:", texto);
        Assert.Contains("Índice 02 (Índice 2)", texto);
        Assert.Contains("  Situação: NÃO ATENDE → ATENDE", texto);
        Assert.Contains("  Vão 2 — medidas:\n    Original: 1.84 × 0.72 m\n    Revisado: 2,00 × 1,70 m", texto);
        // Os itens não mudaram: a lista de itens alterados continua vazia.
        Assert.Empty(Comparacao.Diferencas(s.Projeto, s.HistoricoCompleto));
        Assert.StartsWith("Nenhum item foi alterado", texto);
        Assert.DoesNotContain("linha de tabela", Comparacao.Texto([], []));
    }

    [Fact]
    public void sem_edicoes_o_html_continua_identico_ao_do_kit()
    {
        var c = Importar("longo");
        using var pasta = Ambiente.PastaDeTeste("linhas-v5");
        c.ExtrairAssets(pasta);
        var efetivo = RelatorioRenderer.Renderizar(DadosEfetivos.Gerar(ProjetoJson.Ler(ProjetoJson.Escrever(c.Projeto))).Json,
            Path.Combine(pasta, "dados.json"), Rascunho, Modelo);
        var kit = RelatorioRenderer.Renderizar(Ambiente.Corpus("longo.json"), Rascunho, Modelo);
        Assert.Equal(kit.Html, efetivo.Html);
    }
}
