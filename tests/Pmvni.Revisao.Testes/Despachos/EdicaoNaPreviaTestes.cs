using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

/// <summary>Edição direta na prévia (correções 2026-11-15): todo trecho variável tem chave; a edição volta ao rascunho, à prévia e ao DOCX
/// gerado (que passa na validação); os textos fixos do modelo não têm chave.</summary>
public sealed class EdicaoNaPreviaTestes
{
    static readonly DateOnly Hoje = new(2099, 3, 10);
    static readonly EtapaDespacho Etapa = new("DESPACHO 03 - 2099-03-10", "DESPACHO 03 - 2099-03-10", 3, Hoje, true);

    static RascunhoDespacho Montar(TipoDespacho t)
    {
        var c = RelatorioFicticio.Curto("0001/2099");
        return ConteudoDespacho.Montar(t, c.Projeto, ConteudoDespachoTestes.Info(t), Etapa);
    }

    static IReadOnlyList<string> Chaves(IEnumerable<ParagrafoPrevia> previa) =>
        previa.SelectMany(p => p.TrechosOuTexto).Where(t => t.Chave is not null).Select(t => t.Chave!).ToList();

    static List<string> TextosDoDocx(byte[] docx) => PreenchimentoDocxTestes.Paragrafos(PreenchimentoDocxTestes.Documento(docx)).Select(PreenchimentoDocxTestes.Texto).ToList();

    [Fact]
    public void correcao_todo_texto_variavel_e_editavel_e_volta_ao_docx()
    {
        var kit = Ambiente.ExigirKit();
        var t = TipoDespacho.Correcao;
        var info = ConteudoDespachoTestes.Info(t);
        var tx = TextosFixos.Ler(kit, t);
        var r = Montar(t);
        var id1 = r.Pendencias[0].ItemId;
        var id2 = r.Pendencias[1].ItemId;

        var chaves = Chaves(ConteudoDespacho.Previa(info, r, tx));
        Assert.Contains("cabecalho:protocolo", chaves);
        Assert.Contains("cabecalho:requerente", chaves);
        Assert.Contains("cabecalho:assunto", chaves);
        Assert.Contains("cabecalho:data", chaves);
        Assert.Contains("grupo:GERAL E DOCUMENTAÇÃO", chaves);
        Assert.Contains("exigencia:" + id1 + ":texto", chaves);
        Assert.Contains("exigencia:" + id1 + ":fundamento", chaves);

        // Edita como na folha: cada trecho pela sua chave.
        Assert.True(ConteudoDespacho.AplicarEdicao(r, "cabecalho:protocolo", "0001/2099 (editado)"));
        Assert.True(ConteudoDespacho.AplicarEdicao(r, "cabecalho:requerente", "FULANO EDITADO"));
        Assert.True(ConteudoDespacho.AplicarEdicao(r, "cabecalho:assunto", "ANÁLISE DE PROJETO ARQUITETÔNICO"));
        Assert.True(ConteudoDespacho.AplicarEdicao(r, "cabecalho:data", "11 de março de 2099"));
        Assert.True(ConteudoDespacho.AplicarEdicao(r, "grupo:GERAL E DOCUMENTAÇÃO", "PRANCHA 01/04"));
        Assert.True(ConteudoDespacho.AplicarEdicao(r, "exigencia:" + id1 + ":texto", "Apresentar a planta de situação ampliada."));
        Assert.True(ConteudoDespacho.AplicarEdicao(r, "exigencia:" + id2 + ":fundamento", "LC 70/1990, art. 10"));
        Assert.False(ConteudoDespacho.AplicarEdicao(r, "exigencia:nao-existe:texto", "x"));
        Assert.False(ConteudoDespacho.AplicarEdicao(r, "qualquer", "x"));

        Assert.Equal("11 de março de 2099", r.DataPorExtenso);
        Assert.Contains("cabecalho:data", r.CamposEditados);
        Assert.All(r.Pendencias, p => Assert.Equal("PRANCHA 01/04", p.Grupo));
        var previa = ConteudoDespacho.Previa(info, r, tx);
        var textos = previa.Select(p => p.Texto).ToList();
        Assert.Contains("Protocolo nº: 0001/2099 (editado)", textos);
        Assert.Contains("PRANCHA 01/04", textos);
        Assert.Contains(textos, x => x.StartsWith("01. Apresentar a planta de situação ampliada (", StringComparison.Ordinal));
        Assert.Contains(textos, x => x.StartsWith("02. ", StringComparison.Ordinal) && x.EndsWith(" (LC 70/1990, art. 10).", StringComparison.Ordinal));
        Assert.Contains("Venda Nova do Imigrante/ES, 11 de março de 2099", textos);

        // O mesmo vai para o DOCX, e o DOCX passa na validação do conteúdo.
        var modelo = PreenchimentoDocxTestes.Modelo(t);
        var d = r.Congelar(tx);
        var docx = PreenchimentoDocx.Preencher(modelo, d);
        var noDocx = TextosDoDocx(docx);
        Assert.Contains(noDocx, x => x.StartsWith("Protocolo nº:", StringComparison.Ordinal) && x.EndsWith("0001/2099 (editado)", StringComparison.Ordinal));
        Assert.Contains(noDocx, x => x.EndsWith("FULANO EDITADO", StringComparison.Ordinal));
        Assert.Contains(noDocx, x => x.EndsWith("ANÁLISE DE PROJETO ARQUITETÔNICO", StringComparison.Ordinal));
        Assert.Contains("PRANCHA 01/04", noDocx);
        Assert.Contains(noDocx, x => x.StartsWith("01. Apresentar a planta de situação ampliada (", StringComparison.Ordinal));
        Assert.Contains("Venda Nova do Imigrante/ES, 11 de março de 2099", noDocx);
        // A prévia mostra, parágrafo a parágrafo, o que o DOCX traz (do título até a data).
        foreach (var p in previa.Where(p => p.Estilo is EstiloPrevia.Grupo or EstiloPrevia.Corpo or EstiloPrevia.Data))
            Assert.Contains(p.Texto, noDocx);
        Assert.Empty(ValidacaoDespacho.Conteudo(modelo, docx, d));
        Assert.Empty(ValidacaoDespacho.Comparar(modelo, docx));

        // Apagar a data na folha a deixa a preencher; escrever a da etapa de novo volta ao original.
        ConteudoDespacho.AplicarEdicao(r, "cabecalho:data", "");
        Assert.Contains("Data", r.Faltando());
        ConteudoDespacho.AplicarEdicao(r, "cabecalho:data", "10 de março de 2099");
        Assert.DoesNotContain("cabecalho:data", r.CamposEditados);
    }

    [Fact]
    public void fechamento_editavel_preserva_rotulos_e_numeracao()
    {
        var kit = Ambiente.ExigirKit();
        var t = TipoDespacho.Correcao;
        var info = ConteudoDespachoTestes.Info(t);
        var tx = TextosFixos.Ler(kit, t);
        var r = Montar(t);
        var previa = ConteudoDespacho.Previa(info, r, tx);

        foreach (var p in previa.Where(p => p.Estilo is EstiloPrevia.Titulo or EstiloPrevia.Destinatario or EstiloPrevia.Assinatura or EstiloPrevia.Vazio))
            Assert.All(p.TrechosOuTexto, x => Assert.Null(x.Chave));
        var fechamento = previa.Single(p => p.Texto == tx.Paragrafos.Single(x => x.Id == "fechamento").Texto);
        var chave = Assert.Single(fechamento.TrechosOuTexto).Chave!;
        Assert.True(ConteudoDespacho.AplicarEdicao(r, chave, "Fechamento revisado."));
        Assert.Contains(ConteudoDespacho.Previa(info, r, tx), p => p.Texto == "Fechamento revisado.");
        Assert.Contains("Fechamento revisado.", r.Congelar(tx).Corpo);
        // O rótulo é fixo; só o valor se edita. O número da exigência é fixo e em negrito.
        var protocolo = previa.Single(p => p.Estilo == EstiloPrevia.Rotulo && p.Texto.StartsWith("Protocolo", StringComparison.Ordinal));
        Assert.Null(protocolo.TrechosOuTexto[0].Chave);
        Assert.True(protocolo.TrechosOuTexto[1].Negrito);
        var exigencia = previa.First(p => p.Texto.StartsWith("01. ", StringComparison.Ordinal));
        Assert.Equal("01. ", exigencia.TrechosOuTexto[0].Texto);
        Assert.True(exigencia.TrechosOuTexto[0].Negrito);
        Assert.Null(exigencia.TrechosOuTexto[0].Chave);
        // Cada parágrafo aponta o parágrafo do modelo cuja formatação vale.
        Assert.All(previa, p => Assert.InRange(p.Modelo, 0, info.Paragrafos.Count - 1));
        Assert.Equal(PapelParagrafo.Vaga, info.Paragrafos[exigencia.Modelo].Papel);
    }

    [Fact]
    public void aprovacao_tipo_de_obra_e_area_se_editam_no_texto()
    {
        var kit = Ambiente.ExigirKit();
        var t = TipoDespacho.Aprovacao;
        var info = ConteudoDespachoTestes.Info(t);
        var tx = TextosFixos.Ler(kit, t);
        var r = Montar(t);

        var antes = ConteudoDespacho.Previa(info, r, tx);
        var chaves = Chaves(antes);
        Assert.Contains(ConteudoDespacho.ChaveIntervencao, chaves);
        Assert.Contains(ConteudoDespacho.ChaveArea, chaves);
        Assert.Contains(antes.SelectMany(p => p.TrechosOuTexto), x => x.Chave == ConteudoDespacho.ChaveArea && x.Faltando is not null);

        Assert.True(ConteudoDespacho.AplicarEdicao(r, ConteudoDespacho.ChaveIntervencao, "obra nova, ampliação de obra"));
        Assert.True(ConteudoDespacho.AplicarEdicao(r, ConteudoDespacho.ChaveArea, "210,52"));
        Assert.Equal(new[] { Intervencao.ObraNova, Intervencao.AmpliacaoDeObra }, r.Intervencoes.Order());
        Assert.Equal("210,52", r.AreaTotal);
        Assert.Empty(r.Faltando());

        var textos = ConteudoDespacho.Previa(info, r, tx).Select(p => p.Texto).ToList();
        Assert.Contains(textos, x => x.Contains("obra nova e ampliação de obra", StringComparison.Ordinal));
        Assert.Contains(textos, x => x.Contains("210,52", StringComparison.Ordinal));
        var d = r.Congelar(tx);
        var modelo = PreenchimentoDocxTestes.Modelo(t);
        var docx = PreenchimentoDocx.Preencher(modelo, d);
        var noDocx = TextosDoDocx(docx);
        foreach (var c in d.Corpo) Assert.Contains(c, noDocx);
        Assert.Contains(noDocx, x => x.Contains("210,52", StringComparison.Ordinal));
        Assert.Empty(ValidacaoDespacho.Conteudo(modelo, docx, d));

        // Desmarcar tudo pela folha volta a pedir o tipo de obra (o "e" do texto também separa os nomes).
        ConteudoDespacho.AplicarEdicao(r, ConteudoDespacho.ChaveIntervencao, "");
        Assert.Empty(r.Intervencoes);
        ConteudoDespacho.AplicarEdicao(r, ConteudoDespacho.ChaveIntervencao, "alteração de obra e obra nova");
        Assert.Equal(new[] { Intervencao.ObraNova, Intervencao.AlteracaoDeObra }, r.Intervencoes.Order());
    }

    [Fact]
    public void trocar_a_etapa_mantem_a_data_editada()
    {
        var r = Montar(TipoDespacho.Correcao);
        ConteudoDespacho.AplicarEdicao(r, "cabecalho:data", "1º de abril de 2099");
        var outra = new EtapaDespacho("DESPACHO 04 - 2099-03-12", "DESPACHO 04 - 2099-03-12", 4, new DateOnly(2099, 3, 12), true);
        var c = RelatorioFicticio.Curto("0001/2099");
        var novo = ConteudoDespacho.Montar(TipoDespacho.Correcao, c.Projeto, ConteudoDespachoTestes.Info(TipoDespacho.Correcao), outra);
        Pmvni.App.FluxoNovoDespacho.TransferirEdicoes(r, novo);
        Assert.Equal("1º de abril de 2099", novo.DataPorExtenso);

        var semEdicao = ConteudoDespacho.Montar(TipoDespacho.Correcao, c.Projeto, ConteudoDespachoTestes.Info(TipoDespacho.Correcao), outra);
        Pmvni.App.FluxoNovoDespacho.TransferirEdicoes(Montar(TipoDespacho.Correcao), semEdicao);
        Assert.Equal("12 de março de 2099", semEdicao.DataPorExtenso);
    }
}
