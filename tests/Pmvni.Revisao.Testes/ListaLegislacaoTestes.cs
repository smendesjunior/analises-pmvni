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
}
