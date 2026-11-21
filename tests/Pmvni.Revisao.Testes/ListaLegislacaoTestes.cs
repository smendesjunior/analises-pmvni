using Pmvni.App;
using Pmvni.Legislacao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Linhas da lista de leis anexadas (Ajustes › Legislação e a lista do módulo; desenho §11.1): uma linha por anexo, textos de
/// tamanho, páginas, total, selos e o resumo da anexação.</summary>
public class ListaLegislacaoTestes
{
    static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    static readonly DateTimeOffset Quando = new(2026, 11, 15, 14, 32, 0, TimeSpan.Zero);

    static Anexo A(string id, string titulo, Esfera e, string arquivo, long tamanho = 1834, int paginas = 3, int[]? semTexto = null) =>
        new(id, new string('a', 64), titulo, e, arquivo, tamanho, paginas, semTexto ?? [], Quando, null);

    [Fact]
    public void rampa_no_conteudo_inclui_decreto_na_lista_e_nos_contadores()
    {
        LinhaAnexo[] leis = [new("decreto", "Decreto 5.296", Esfera.Federal, "acessibilidade.pdf", "", "", "", null)];
        var conteudo = new HashSet<string> { "decreto" };
        Assert.Single(ListaLegislacao.Filtrar(leis, "rampa", null, null, new HashSet<string>(), conteudo));
        Assert.Equal(1, ListaLegislacao.ContarEsferas(leis, "rampa", conteudo)[Esfera.Federal]);
        Assert.Contains((TipoNorma.Decreto, 1), ListaLegislacao.ContarTipos(leis, "rampa", null, conteudo));
    }
    [Theory]
    [InlineData(1834, "2 KB")]
    [InlineData(1_048_576, "1,0 MB")]
    [InlineData(12_900_000, "12,3 MB")]
    [InlineData(1, "1 KB")]
    [InlineData(1024, "1 KB")]
    [InlineData(1025, "2 KB")]
    public void tamanho_em_texto(long b, string t) => Assert.Equal(t, ListaLegislacao.Tamanho(b));

    [Fact]
    public void paginas_e_total()
    {
        Assert.Equal("1 página", ListaLegislacao.Paginas(1));
        Assert.Equal("3 páginas", ListaLegislacao.Paginas(3));
        Assert.Equal("Nenhuma lei anexada", ListaLegislacao.Total([]));
        Assert.Equal("1 lei anexada · 2 KB", ListaLegislacao.Total([A("lg_1", "Lei", Esfera.Municipal, "lei.pdf")]));
        Assert.Equal("4 leis anexadas · 12,3 MB", ListaLegislacao.Total(
            [A("1", "a", Esfera.Federal, "a.pdf", 3_000_000), A("2", "b", Esfera.Estadual, "b.pdf", 3_000_000),
             A("3", "c", Esfera.Municipal, "c.pdf", 3_000_000), A("4", "d", Esfera.Outras, "d.pdf", 3_900_000)]));
    }

    [Fact]
    public void selos()
    {
        var pronto = A("lg_1", "Lei", Esfera.Municipal, "lei.pdf");
        Assert.Equal("Cópia do editor não encontrada", ListaLegislacao.Linha(pronto, EstadoAnexo.SemCopia, false, Utc).Selo);
        Assert.Equal("Cópia do editor não encontrada", ListaLegislacao.Linha(pronto, EstadoAnexo.SemCopia, true, Utc).Selo);
        Assert.Equal("Preparando a pesquisa…", ListaLegislacao.Linha(pronto, EstadoAnexo.SemIndice, true, Utc).Selo);
        var semTexto = A("lg_2", "Dig", Esfera.Outras, "dig.pdf", paginas: 1, semTexto: [1]);
        Assert.Equal("Sem texto pesquisável", ListaLegislacao.Linha(semTexto, EstadoAnexo.Pronto, false, Utc).Selo);
        var parte = A("lg_3", "Parte", Esfera.Outras, "p.pdf", paginas: 81, semTexto: [5, 9]);
        Assert.Equal("2 de 81 páginas sem texto", ListaLegislacao.Linha(parte, EstadoAnexo.Pronto, false, Utc).Selo);
        Assert.Null(ListaLegislacao.Linha(pronto, EstadoAnexo.Pronto, false, Utc).Selo);
        // Sem a cópia do editor: só Substituir e Excluir (título e esfera não editáveis); nos outros estados, editáveis.
        Assert.False(ListaLegislacao.Linha(pronto, EstadoAnexo.SemCopia, false, Utc).Editavel);
        Assert.True(ListaLegislacao.Linha(pronto, EstadoAnexo.SemIndice, true, Utc).Editavel);
        Assert.True(ListaLegislacao.Linha(semTexto, EstadoAnexo.Pronto, false, Utc).Editavel);
    }

    [Fact]
    public void linha_com_os_campos_do_desenho()
    {
        var l = ListaLegislacao.Linha(A("lg_1", "LEI 9.999-2099 - FICTICIA", Esfera.Municipal, "LEI 9.999-2099 - FICTICIA.pdf"), EstadoAnexo.Pronto, false, Utc);
        Assert.Equal(new LinhaAnexo("lg_1", "LEI 9.999-2099 - FICTICIA", Esfera.Municipal, "LEI 9.999-2099 - FICTICIA.pdf", "2 KB", "3 páginas",
            "anexado em 15/11/2026 14:32", null), l);
        var saoPaulo = TimeZoneInfo.CreateCustomTimeZone("menos3", TimeSpan.FromHours(-3), "menos3", "menos3");
        Assert.Equal("anexado em 15/11/2026 11:32", ListaLegislacao.Linha(A("lg_1", "x", Esfera.Outras, "x.pdf"), EstadoAnexo.Pronto, false, saoPaulo).AnexadoEm);
        Assert.Equal("lei.pdf · 2 KB · 3 páginas · anexado em 15/11/2026 14:32", (l with { Arquivo = "lei.pdf" }).Detalhe);
    }

    [Fact]
    public void uma_linha_por_anexo_agrupada_e_filtrada()
    {
        var linhas = new[]
        {
            A("lg_m", "LEI 9.999-2099 - FICTICIA", Esfera.Municipal, "LEI 9.999-2099 - FICTICIA.pdf"),
            A("lg_e", "NT 99-2099 - FICTICIA", Esfera.Estadual, "NT 99-2099 - FICTICIA.pdf"),
            A("lg_f", "LEI 99.999-2099 - FICTICIA", Esfera.Federal, "LEI 99.999-2099 - FICTICIA.pdf"),
            A("lg_o", "Digitalizada", Esfera.Outras, "DIGITALIZADA - FICTÍCIA.pdf"),
        }.Select(a => ListaLegislacao.Linha(a, EstadoAnexo.Pronto, false, Utc)).ToList();

        var grupos = ListaLegislacao.Agrupar(linhas, null);
        Assert.Equal(new[] { "Federal", "Estadual", "Municipal", "Outras" }, grupos.Select(g => g.Grupo));
        Assert.All(grupos, g => Assert.Single(g.Linhas));
        Assert.Equal(new[] { "lg_f", "lg_e", "lg_m", "lg_o" }, grupos.Select(g => g.Linhas[0].Id));

        Assert.Equal(4, ListaLegislacao.Agrupar(linhas, "ficticia").Sum(g => g.Linhas.Count)); // sem acento: "FICTÍCIA" no nome do arquivo também
        var nt = ListaLegislacao.Agrupar(linhas, "  NT 99 ");
        Assert.Equal("Estadual", Assert.Single(nt).Grupo);
        Assert.Equal("lg_e", Assert.Single(nt[0].Linhas).Id);
        Assert.Empty(ListaLegislacao.Agrupar(linhas, "inexistente"));

        // Duas leis na mesma esfera: duas linhas (nunca agrupadas nem resumidas), na ordem recebida.
        var duas = ListaLegislacao.Agrupar([linhas[0], linhas[0] with { Id = "lg_m2", Titulo = "Outra" }], null);
        Assert.Equal(new[] { "lg_m", "lg_m2" }, Assert.Single(duas).Linhas.Select(l => l.Id));
    }

    [Theory]
    [InlineData(2, 0, "2 PDFs anexados.")]
    [InlineData(1, 0, "1 PDF anexado.")]
    [InlineData(0, 1, "Nenhum PDF anexado; 1 recusado:")]
    [InlineData(1, 4, "1 PDF anexado; 4 recusados:")]
    [InlineData(0, 0, "Nenhum PDF anexado.")]
    public void resumo_da_anexacao(int anexados, int recusas, string primeiraLinha)
    {
        var lista = Enumerable.Range(1, recusas).Select(i => $"“r{i}.pdf” não é um arquivo PDF.").ToList();
        var esperado = string.Join("\n", new[] { primeiraLinha }.Concat(lista));
        Assert.Equal(esperado, ListaLegislacao.Resumo(anexados, lista));
    }

    [Fact]
    public void resumo_conta_as_leis_ja_anexadas_como_ignoradas()
    {
        Assert.Equal("Nenhum PDF anexado (1 já estava anexado e foi ignorado).", ListaLegislacao.Resumo(0, [], 1));
        Assert.Equal("2 PDFs anexados (3 já estavam anexados e foram ignorados).", ListaLegislacao.Resumo(2, [], 3));
    }

    [Theory]
    [InlineData("Lei nº 6.766/1979", TipoNorma.Lei)]
    [InlineData("LEI 9.999-2099 - FICTICIA", TipoNorma.Lei)]
    [InlineData("Decreto nº 5.296/2004", TipoNorma.Decreto)]
    [InlineData("Lei Complementar nº 70/1990", TipoNorma.LeiComplementar)]
    [InlineData("LC 70-1990 Código de Obras", TipoNorma.LeiComplementar)]
    [InlineData("NT 99-2099 - FICTICIA", TipoNorma.NormaTecnica)]
    [InlineData("Norma Técnica 10 CBMES", TipoNorma.NormaTecnica)]
    [InlineData("Portaria 12/2020", TipoNorma.Outro)]
    [InlineData("Lcd qualquer", TipoNorma.Outro)]
    public void tipo_da_norma_sai_do_titulo(string titulo, TipoNorma esperado) => Assert.Equal(esperado, ListaLegislacao.TipoDoTitulo(titulo));

    [Fact]
    public void filtro_por_esfera_e_tipo_com_favoritas_no_topo()
    {
        var linhas = new[]
        {
            A("f1", "Lei nº 1/2000", Esfera.Federal, "a.pdf"),
            A("f2", "Decreto nº 2/2001", Esfera.Federal, "b.pdf"),
            A("e1", "NT 01 Estadual", Esfera.Estadual, "c.pdf"),
            A("m1", "Lei Complementar 70", Esfera.Municipal, "d.pdf"),
            A("o1", "Portaria 9", Esfera.Outras, "e.pdf"),
        }.Select(a => ListaLegislacao.Linha(a, EstadoAnexo.Pronto, false, Utc)).ToList();
        var nenhuma = new HashSet<string>();
        Assert.Equal(new[] { "f1", "f2", "e1", "m1", "o1" }, ListaLegislacao.Filtrar(linhas, null, null, null, nenhuma).Select(l => l.Id));
        Assert.Equal(new[] { "f1", "f2" }, ListaLegislacao.Filtrar(linhas, null, Esfera.Federal, null, nenhuma).Select(l => l.Id));
        Assert.Equal(new[] { "f2" }, ListaLegislacao.Filtrar(linhas, null, Esfera.Federal, TipoNorma.Decreto, nenhuma).Select(l => l.Id));
        Assert.Equal(new[] { "m1", "f1", "f2", "e1", "o1" }, ListaLegislacao.Filtrar(linhas, null, null, null, new HashSet<string> { "m1" }).Select(l => l.Id));
        Assert.Equal(new[] { "e1" }, ListaLegislacao.Filtrar(linhas, "estadual", null, null, nenhuma).Select(l => l.Id));

        var esferas = ListaLegislacao.ContarEsferas(linhas, null);
        Assert.Equal(2, esferas[Esfera.Federal]);
        Assert.Equal(1, esferas[Esfera.Municipal]);
        Assert.False(esferas.ContainsKey(Esfera.Outras));
        var tipos = ListaLegislacao.ContarTipos(linhas, null, Esfera.Federal);
        Assert.Equal(new (TipoNorma?, int)[] { (null, 2), (TipoNorma.Lei, 1), (TipoNorma.Decreto, 1), (TipoNorma.LeiComplementar, 0), (TipoNorma.NormaTecnica, 0) }, tipos);
    }
}
