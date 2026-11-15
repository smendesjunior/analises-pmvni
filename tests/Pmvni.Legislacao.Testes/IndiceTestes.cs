using Pmvni.Legislacao.Testes.Ficticios;
using Xunit;

namespace Pmvni.Legislacao.Testes;

public sealed class IndiceTestes : IDisposable
{
    readonly Cenario c = new("indice");

    public void Dispose() => c.Dispose();

    [Theory]
    [InlineData("Ocupação", "ocupacao")]
    [InlineData("  AFASTAMENTO   frontal\n", "afastamento frontal")]
    [InlineData("edifi­cações", "edificacoes")]
    [InlineData("Art. 1º", "art. 1º")]
    [InlineData("Ação\r\n\tCivil", "acao civil")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void normalizar(string entrada, string esperado) => Assert.Equal(esperado, TextoPesquisa.Normalizar(entrada));

    [Fact]
    public void mapa_aponta_cada_caractere_normalizado_para_o_original()
    {
        var (n, origem) = TextoPesquisa.ComMapa("  Ação  ­do\nSolo ");
        Assert.Equal("acao do solo", n);
        Assert.Equal(n.Length, origem.Length);
        // "Ação" começa no índice 2; "do" depois do hífen suave; "Solo" depois da quebra de linha.
        Assert.Equal([2, 3, 4, 5, 6, 9, 10, 11, 12, 13, 14, 15], origem);
    }

    Anexo Lei() => c.Anexar(Cenario.Lei);

    [Fact]
    public void pesquisa_em_todas_sem_acento_nem_caixa()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        var indice = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None);

        var r = indice.Pesquisar("AFASTAMENTO");
        Assert.Equal(4, r.Total);
        Assert.Equal(2, r.Leis);
        Assert.Equal([(nt.Id, 2), (lei.Id, 1), (lei.Id, 2), (lei.Id, 3)], r.Resultados.Select(x => (x.AnexoId, x.Pagina)));
        Assert.Equal("5.1 O afastamento entre edificações fictícias é de 4,00 m.", r.Resultados[0].Trecho);
        Assert.Equal("Art. 2º O afastamento frontal mínimo é de 3,00 m. Art. 3º A taxa de ocupação máxima é de 60%.", r.Resultados[2].Trecho);
        Assert.All(r.Resultados, x => Assert.Equal(1, x.OcorrenciasNaPagina));

        Assert.Equal([(lei.Id, 2), (lei.Id, 3)], indice.Pesquisar("ocupacao").Resultados.Select(x => (x.AnexoId, x.Pagina)));
        Assert.Equal([(lei.Id, 1), (lei.Id, 2)], indice.Pesquisar("afastamento frontal").Resultados.Select(x => (x.AnexoId, x.Pagina)));
        // Acento no termo também vale, e a página conta uma vez só.
        Assert.Equal(2, indice.Pesquisar("OCUPAÇÃO").Total);
        Assert.Equal(0, indice.Pesquisar("inexistente").Total);
    }

    [Fact]
    public void ocorrencias_na_pagina_contam_todas()
    {
        var lei = Lei();
        c.Acervo.GravarIndice(lei.Id, new ResultadoExtracao(["Art. 1º foo bar Foo", "x", "FOO foo foo foo"]));
        var r = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None).Pesquisar("foo");
        Assert.Equal([(1, 2), (3, 4)], r.Resultados.Select(x => (x.Pagina, x.OcorrenciasNaPagina)));
        Assert.Equal(2, r.Total); // um resultado por página
    }

    [Fact]
    public void trecho_de_ate_160_com_reticencias()
    {
        var lei = Lei();
        var pagina = new string('x', 499) + "OCUPAÇÃO" + new string('y', 493); // 1.000 caracteres, o termo na posição 500
        Assert.Equal(1000, pagina.Length);
        c.Acervo.GravarIndice(lei.Id, new ResultadoExtracao([pagina, "ocupação no começo " + new string('z', 500), new string('w', 500) + " fim ocupação"]));

        var r = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None).Pesquisar("ocupacao");

        var t = r.Resultados[0].Trecho;
        Assert.StartsWith("…", t);
        Assert.EndsWith("…", t);
        Assert.InRange(t.Length, 1, 162);
        Assert.Contains("OCUPAÇÃO", t); // com a caixa original
        Assert.StartsWith("ocupação no começo", r.Resultados[1].Trecho); // sem reticências no começo
        Assert.EndsWith("…", r.Resultados[1].Trecho);
        Assert.DoesNotContain("…", r.Resultados[1].Trecho[..^1]);
        Assert.StartsWith("…", r.Resultados[2].Trecho);
        Assert.EndsWith("fim ocupação", r.Resultados[2].Trecho); // sem reticências no fim
    }

    [Fact]
    public void trecho_colapsa_espacos_do_texto_original()
    {
        var lei = Lei();
        c.Acervo.GravarIndice(lei.Id, new ResultadoExtracao(["Linha um\nLinha  dois   com Ação\n\nlinha três"]));
        var r = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None).Pesquisar("acao");
        Assert.Equal("Linha um Linha dois com Ação linha três", r.Resultados.Single().Trecho);
    }

    [Fact]
    public void termo_curto_nao_pesquisa()
    {
        Lei();
        var indice = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None);
        Assert.Equal(0, indice.Pesquisar("a").Total);
        Assert.Equal(0, indice.Pesquisar("  a  ").Total);
        Assert.Equal(0, indice.Pesquisar("").Total);
        Assert.Empty(indice.Pesquisar("­").Resultados);
        Assert.True(indice.Pesquisar("ar").Total > 0);
    }

    [Fact]
    public void limita_a_200_e_informa_o_total()
    {
        var lei = Lei();
        c.Acervo.GravarIndice(lei.Id, new ResultadoExtracao([.. Enumerable.Range(1, 300).Select(i => "afastamento " + i)]));

        var r = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None).Pesquisar("afastamento");

        Assert.Equal(200, r.Resultados.Count);
        Assert.Equal(300, r.Total);
        Assert.Equal(1, r.Leis);
        Assert.Equal(Enumerable.Range(1, 200), r.Resultados.Select(x => x.Pagina));
        Assert.Equal(5, IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None).Pesquisar("afastamento", 5).Resultados.Count);
    }

    [Fact]
    public void atualizar_tira_o_excluido_e_traz_o_novo()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        var indice = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None);
        Assert.Equal(4, indice.Pesquisar("afastamento").Total);

        c.Acervo.Excluir(nt.Id);
        indice.Atualizar(c.Acervo);
        Assert.All(indice.Pesquisar("afastamento").Resultados, x => Assert.Equal(lei.Id, x.AnexoId));
        Assert.Equal(3, indice.Pesquisar("afastamento").Total);

        var federal = c.Anexar(Cenario.Federal);
        indice.Atualizar(c.Acervo);
        Assert.Equal([(federal.Id, 1)], indice.Pesquisar("parcelamento").Resultados.Select(x => (x.AnexoId, x.Pagina)));
    }

    [Fact]
    public void atualizar_acompanha_titulo_e_esfera_e_o_indice_que_chega_depois()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        File.Delete(Path.Combine(c.Dados, nt.Sha256 + ".texto.json.gz"));
        var indice = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None);
        Assert.Equal(3, indice.Pesquisar("afastamento").Total); // a NT sem índice fica fora

        c.Acervo.GravarIndice(nt.Id, c.Extrair(Path.Combine(c.Dados, nt.Sha256 + ".pdf")));
        c.Acervo.DefinirEsfera(lei.Id, Esfera.Federal);
        indice.Atualizar(c.Acervo);

        Assert.Equal([(lei.Id, 1), (lei.Id, 2), (lei.Id, 3), (nt.Id, 2)], indice.Pesquisar("afastamento").Resultados.Select(x => (x.AnexoId, x.Pagina)));
    }

    [Fact]
    public void carregar_respeita_o_cancelamento()
    {
        Lei();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => IndiceLegislacao.Carregar(c.Acervo, cts.Token));
    }
}
