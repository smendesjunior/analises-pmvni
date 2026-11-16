using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

public sealed class ConteudoDespachoTestes
{
    static readonly DateOnly Hoje = new(2099, 3, 10);
    static readonly EtapaDespacho Etapa = new("DESPACHO 03 - 2099-03-10", "DESPACHO 03 - 2099-03-10", 3, Hoje, true);

    internal static InfoModelo Info(TipoDespacho t)
    {
        var m = RegistroModelos.Ler(Ambiente.ExigirKit()).Modelos.Single(x => x.Tipo == t);
        return PreenchimentoDocx.LerModelo(File.ReadAllBytes(m.Caminho), t);
    }

    static SessaoRevisao Sessao(string processo = "0001/2099")
    {
        var c = RelatorioFicticio.Curto(processo);
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    static string Id(SessaoRevisao s, int n) => s.Projeto.Incluidos[n - 1].Id;

    static Processo Processo0001 =>
        Processos.Listar(Ambiente.Corpus("processos"), CancellationToken.None).Itens.Single(p => p.NomePasta.StartsWith("0001 - 2099", StringComparison.Ordinal));

    static RascunhoDespacho Correcao(SessaoRevisao? s = null) =>
        ConteudoDespacho.Montar(TipoDespacho.Correcao, (s ?? Sessao()).Projeto, Info(TipoDespacho.Correcao), Etapa);

    static RascunhoDespacho Aprovacao() => ConteudoDespacho.Montar(TipoDespacho.Aprovacao, Sessao().Projeto, Info(TipoDespacho.Aprovacao), Etapa);

    [Fact]
    public void correcao_traz_os_itens_que_nao_atendem_na_ordem()
    {
        var s = Sessao();
        var r = Correcao(s);

        Assert.Equal(new[] { (2, "NAO_COMPROVADO"), (3, "NAO_ATENDE") }, r.Pendencias.Select(p => (p.Numero, p.Situacao)));
        Assert.All(r.Pendencias, p => Assert.True(p.Incluida));
        Assert.All(r.Pendencias, p => Assert.Equal("GERAL E DOCUMENTAÇÃO", p.Grupo));
        var item2 = s.Projeto.Incluidos[1];
        Assert.Equal(item2.Id, r.Pendencias[0].ItemId);
        Assert.Equal(DadosEfetivos.TextoEfetivo(s.Projeto, item2, "providencia"), r.Pendencias[0].Texto);
        Assert.Equal(DadosEfetivos.TextoEfetivo(s.Projeto, item2, "titulo"), r.Pendencias[0].Titulo);
        Assert.Equal("Código de Obras (LC nº 70/1990), art. 102.", r.Pendencias[0].Fundamento);
        Assert.Equal(r.Pendencias[0].Texto, r.Pendencias[0].TextoOriginal);
        Assert.False(r.Pendencias[0].Editada);
    }

    [Fact]
    public void situacao_e_texto_revisados_valem_e_excluido_nao_entra()
    {
        var s = Sessao();
        string id1 = Id(s, 1), id2 = Id(s, 2), id3 = Id(s, 3);
        s.Aplicar(new DefinirSituacao(id1, "NAO_ATENDE"));
        s.Aplicar(new DefinirJustificativa(id1, "Revisado no teste."));
        s.Aplicar(new Excluir(id3));
        s.Aplicar(new DefinirCampo(id2, "providencia", "Apresentar a cota do recuo."));

        var r = Correcao(s);

        Assert.Equal(new[] { (1, "NAO_ATENDE"), (2, "NAO_COMPROVADO") }, r.Pendencias.Select(p => (p.Numero, p.Situacao)));
        Assert.Equal(new[] { id1, id2 }, r.Pendencias.Select(p => p.ItemId));
        Assert.Equal("Apresentar a cota do recuo.", r.Pendencias[1].Texto);
    }

    [Fact]
    public void textos_do_relatorio_tambem_sao_normalizados()
    {
        var d = JsonFiel.Ler(RelatorioFicticio.CurtoJson());
        var item = (System.Text.Json.Nodes.JsonObject)d["itens"]![1]!;
        item["providencia"] = "  Apresentar\r\na cota. ";
        item["base_legal"] = " LC 70/1990.\n";
        var c = Importador.Importar(JsonFiel.Escrever(d), Ambiente.Corpus("curto.json"), Pmvni.Composicao.ModeloOficial.Carregar(), Ambiente.Contexto());

        var r = ConteudoDespacho.Montar(TipoDespacho.Correcao, c.Projeto, Info(TipoDespacho.Correcao), Etapa);

        Assert.Equal("Apresentar a cota.", r.Pendencias[0].Texto);
        Assert.Equal("LC 70/1990.", r.Pendencias[0].Fundamento);
        Assert.False(r.Pendencias[0].Editada);
        Assert.Empty(r.CamposEditados);
    }

    [Fact]
    public void cabecalho_vem_da_capa_e_do_modelo()
    {
        var r = Correcao();

        Assert.Equal(TipoDespacho.Correcao, r.Tipo);
        Assert.Equal("0001/2099", r.Protocolo);
        Assert.Equal("FULANO DE EXEMPLO (FICTICIO)", r.Requerente);
        Assert.Equal("SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA", r.Assunto);
        Assert.Equal(Hoje, r.Data);
        Assert.Empty(r.Faltando());
        Assert.Empty(r.CamposEditados);
    }

    [Fact]
    public void exigencia_com_fundamento()
    {
        Assert.Equal(new ExigenciaFinal("Apresentar a cota", " (Código de Obras (LC nº 70/1990), art. 102)."),
            ConteudoDespacho.Exigencia("Apresentar a cota.", "Código de Obras (LC nº 70/1990), art. 102."));
        Assert.Equal(new ExigenciaFinal("Apresentar a cota", ""), ConteudoDespacho.Exigencia("Apresentar a cota", ""));
        Assert.Equal(new ExigenciaFinal("Apresentar a cota", " (LC 70/1990)"), ConteudoDespacho.Exigencia("Apresentar a cota", "LC 70/1990"));
        Assert.Equal(new ExigenciaFinal("Apresentar a cota.", ""), ConteudoDespacho.Exigencia(" Apresentar a cota. ", " "));
        Assert.Equal(new ExigenciaFinal("Apresentar a cota.", ""), ConteudoDespacho.Exigencia("Apresentar a cota.", "."));
    }

    [Fact]
    public void faltando_lista_o_que_o_usuario_precisa_preencher()
    {
        var r = Correcao();
        var segundo = r.Pendencias[0].ItemId;
        r.DefinirTexto(segundo, "");
        Assert.Equal(new[] { "ITEM 02: texto da exigência" }, r.Faltando());

        r.DefinirTexto(segundo, "Texto.");
        r.DefinirGrupo(segundo, " ");
        Assert.Equal(new[] { "ITEM 02: grupo" }, r.Faltando());
        r.DefinirGrupo(segundo, "GERAL");

        foreach (var p in r.Pendencias.ToList()) r.Marcar(p.ItemId, false);
        Assert.Equal(new[] { "Ao menos uma exigência marcada" }, r.Faltando());

        r.DefinirCabecalho("requerente", "");
        Assert.Contains("Requerente", r.Faltando());
        r.DefinirCabecalho("protocolo", "");
        r.DefinirCabecalho("assunto", "");
        Assert.Equal(new[] { "Protocolo nº", "Requerente", "Assunto", "Ao menos uma exigência marcada" }, r.Faltando());

        Assert.Equal(new[] { "Tipo de obra (obra nova, alteração de obra ou ampliação de obra)", "Área total a construir (m², ex.: 210,52)" },
            Aprovacao().Faltando());
        Assert.Throws<ArgumentException>(() => r.DefinirCabecalho("outro", "x"));
    }

    [Theory]
    [InlineData("210,52", true)]
    [InlineData("1.234,56", true)]
    [InlineData("1234,56", true)]
    [InlineData("210.52", false)]
    [InlineData("210", false)]
    [InlineData("12.34,56", false)]
    [InlineData("", false)]
    public void area_total_no_formato_brasileiro(string area, bool valida) => Assert.Equal(valida, ConteudoDespacho.AreaValida(area));

    [Fact]
    public void intervencoes_por_extenso()
    {
        Assert.Equal("obra nova", ConteudoDespacho.Intervencoes(new[] { Intervencao.ObraNova }));
        Assert.Equal("alteração de obra e ampliação de obra",
            ConteudoDespacho.Intervencoes(new[] { Intervencao.AmpliacaoDeObra, Intervencao.AlteracaoDeObra }));
        Assert.Equal("obra nova, alteração de obra e ampliação de obra",
            ConteudoDespacho.Intervencoes(new[] { Intervencao.AmpliacaoDeObra, Intervencao.ObraNova, Intervencao.AlteracaoDeObra }));
    }

    [Fact]
    public void congelar_resolve_lacunas_e_grupos()
    {
        var kit = Ambiente.ExigirKit();
        var a = Aprovacao();
        a.DefinirIntervencao(Intervencao.ObraNova, true);
        a.DefinirArea(" 210,52 ");
        var da = a.Congelar(TextosFixos.Ler(kit, TipoDespacho.Aprovacao));
        Assert.Equal(4, da.Corpo.Count);
        Assert.EndsWith("para a execução de obra nova.", da.Corpo[0]);
        Assert.Contains("é de 210,52m².", da.Corpo[2]);
        Assert.DoesNotContain(da.Corpo, c => c.Contains('{'));
        Assert.Empty(da.Grupos);
        Assert.Equal("10 de março de 2099", da.DataPorExtenso);

        // [G, "PRANCHA 01", G]: com o item 1 também pendente, há 3 exigências; a do meio vai para "PRANCHA 01".
        var s = Sessao();
        s.Aplicar(new DefinirSituacao(Id(s, 1), "NAO_ATENDE"));
        s.Aplicar(new DefinirJustificativa(Id(s, 1), "Revisado no teste."));
        var c = Correcao(s);
        c.DefinirGrupo(c.Pendencias[1].ItemId, "PRANCHA 01");
        var tx = TextosFixos.Ler(kit, TipoDespacho.Correcao);
        var dc = c.Congelar(tx);
        Assert.Equal(new[] { ("GERAL E DOCUMENTAÇÃO", 2), ("PRANCHA 01", 1) }, dc.Grupos.Select(g => (g.Titulo, g.Exigencias.Count)));
        Assert.Equal(new[] { tx.Paragrafos[0].Texto }, dc.Corpo);
        Assert.Equal(ConteudoDespacho.Exigencia(c.Pendencias[2].Texto, c.Pendencias[2].Fundamento), dc.Grupos[0].Exigencias[1]);
        Assert.Equal("0001/2099", dc.Protocolo);
    }

    [Fact]
    public void mover_e_desmarcar_mudam_o_despacho()
    {
        var c = Correcao();
        var (primeiro, segundo) = (c.Pendencias[0].ItemId, c.Pendencias[1].ItemId);
        c.Mover(segundo, -1);
        Assert.Equal(new[] { segundo, primeiro }, c.Pendencias.Select(p => p.ItemId));
        c.Mover(segundo, -5);
        Assert.Equal(new[] { segundo, primeiro }, c.Pendencias.Select(p => p.ItemId));
        c.Marcar(primeiro, false);
        var d = c.Congelar(TextosFixos.Ler(Ambiente.ExigirKit(), TipoDespacho.Correcao));
        Assert.Single(Assert.Single(d.Grupos).Exigencias);
    }

    [Fact]
    public void congelar_com_faltando_lanca()
    {
        var r = Aprovacao();
        var e = Assert.Throws<DespachoIncompletoException>(() => r.Congelar(TextosFixos.Ler(Ambiente.ExigirKit(), TipoDespacho.Aprovacao)));
        Assert.Equal(r.Faltando(), e.Faltando);
    }

    [Fact]
    public void textos_do_usuario_sao_texto_puro()
    {
        var r = Correcao();
        var id = r.Pendencias[0].ItemId;

        r.DefinirTexto(id, "  a\r\nb <i>c</i> & d ");

        Assert.Equal("a b <i>c</i> & d", r.Pendencias[0].Texto);
        Assert.True(r.Pendencias[0].Editada);
        Assert.Contains("exigencia:" + id + ":texto", r.CamposEditados);
        r.DefinirCabecalho("assunto", " OUTRO\nASSUNTO ");
        Assert.Equal("OUTRO ASSUNTO", r.Assunto);
        Assert.Contains("cabecalho:assunto", r.CamposEditados);
    }

    [Fact]
    public void bloqueios_do_relatorio()
    {
        var p = Processo0001;
        var s = Sessao();
        Assert.Empty(ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, s.Projeto, p, false));

        s.Aplicar(new DefinirSituacao(Id(s, 2), "ATENDE"));
        var erros = ConferenciaRevisao.Erros(s.Projeto);
        Assert.Contains(Assert.Single(erros), ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, s.Projeto, p, false));

        Assert.Equal(new[] { "O processo da capa (9001/2026) não é o da pasta (0001/2099). Confira se o relatório é deste processo." },
            ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, Sessao("9001/2026").Projeto, p, false));
        Assert.Empty(ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, Sessao(" 01 / 2099 ").Projeto, p, false));
        Assert.Equal(new[] { "O processo da capa (sem número) não está no formato número/ano; confira o relatório." },
            ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, Sessao("sem número").Projeto, p, false));
        Assert.Empty(ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, Sessao("9001/2026").Projeto, p with { Numero = null, Ano = null }, false));
        Assert.Equal(new[] { "Este relatório foi gravado por uma versão mais nova do editor (somente leitura); o despacho não pode ser emitido a partir dele." },
            ConteudoDespacho.Bloqueios(TipoDespacho.Aprovacao, Sessao().Projeto, p, true));
    }

    [Fact]
    public void correcao_sem_item_pendente_e_bloqueada()
    {
        var s = Sessao();
        foreach (var n in new[] { 2, 3 })
        {
            s.Aplicar(new DefinirSituacao(Id(s, n), "ATENDE"));
            s.Aplicar(new DefinirJustificativa(Id(s, n), "Atendido no teste."));
        }

        Assert.Equal(new[] { "Nenhum item incluído tem situação diferente de ATENDE: não há exigência para o despacho de correção." },
            ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, s.Projeto, Processo0001, false));
        Assert.Empty(ConteudoDespacho.Bloqueios(TipoDespacho.Aprovacao, s.Projeto, Processo0001, false));
    }

    [Fact]
    public void aprovacao_com_pendencias_avisa()
    {
        var s = Sessao();
        Assert.Equal(new[] { "O relatório tem 2 itens com situação diferente de ATENDE (ITEM 02, ITEM 03). Emitir a aprovação mesmo assim?" },
            ConteudoDespacho.Avisos(TipoDespacho.Aprovacao, s.Projeto));
        Assert.Empty(ConteudoDespacho.Avisos(TipoDespacho.Correcao, s.Projeto));

        s.Aplicar(new Excluir(Id(s, 2)));
        Assert.Equal(new[] { "O relatório tem 1 item com situação diferente de ATENDE (ITEM 02). Emitir a aprovação mesmo assim?" },
            ConteudoDespacho.Avisos(TipoDespacho.Aprovacao, s.Projeto));
    }

    [Fact]
    public void previa_segue_o_modelo_e_marca_o_que_falta()
    {
        var kit = Ambiente.ExigirKit();
        var info = Info(TipoDespacho.Correcao);
        var r = Correcao();
        var tx = TextosFixos.Ler(kit, TipoDespacho.Correcao);
        var d = r.Congelar(tx);

        var previa = ConteudoDespacho.Previa(info, r, tx);
        var textos = previa.Select(p => p.Texto).ToList();

        var esperados = new[]
        {
            "INFORMAÇÕES GERAIS", "Protocolo nº: 0001/2099", "Requerente: FULANO DE EXEMPLO (FICTICIO)", "GERAL E DOCUMENTAÇÃO",
            "01. " + d.Grupos[0].Exigencias[0].Texto + d.Grupos[0].Exigencias[0].Fundamento,
            "02. " + d.Grupos[0].Exigencias[1].Texto + d.Grupos[0].Exigencias[1].Fundamento,
            tx.Paragrafos[0].Texto, "Venda Nova do Imigrante/ES, 10 de março de 2099",
        };
        var pos = esperados.Select(e => textos.IndexOf(e)).ToList();
        Assert.DoesNotContain(-1, pos);
        Assert.Equal(pos.Order(), pos);
        Assert.All(previa, p => Assert.Null(p.Faltando));
        Assert.Equal(EstiloPrevia.Rotulo, previa[textos.IndexOf("Protocolo nº: 0001/2099")].Estilo);
        Assert.Equal(EstiloPrevia.Grupo, previa[textos.IndexOf("GERAL E DOCUMENTAÇÃO")].Estilo);
        Assert.Equal(EstiloPrevia.Data, previa[textos.IndexOf("Venda Nova do Imigrante/ES, 10 de março de 2099")].Estilo);
        var linha = info.Paragrafos.ToList().FindIndex(p => p.Papel == PapelParagrafo.LinhaAssinatura);
        var cauda = info.Paragrafos.Skip(linha).Select(p => p.Texto).ToList();
        Assert.Equal(cauda, textos.TakeLast(cauda.Count));
        Assert.Equal(EstiloPrevia.Assinatura, previa[^cauda.Count].Estilo);

        var a = Aprovacao();
        a.DefinirIntervencao(Intervencao.ObraNova, true);
        var pa = ConteudoDespacho.Previa(Info(TipoDespacho.Aprovacao), a, TextosFixos.Ler(kit, TipoDespacho.Aprovacao));
        var falta = Assert.Single(pa, p => p.Faltando is not null);
        Assert.Equal("Área total a construir (m², ex.: 210,52)", falta.Faltando);
        Assert.Contains("AO SETOR DE FISCALIZAÇÃO:", pa.Select(p => p.Texto));
        Assert.Contains(pa, p => p.Texto.EndsWith("para a execução de obra nova.", StringComparison.Ordinal));
    }
}
