using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

public sealed class EtapasNomesTestes
{
    static readonly DateOnly Hoje = new(2099, 3, 10);

    static (PastaTeste Pasta, ArvoreFicticia Arvore) Arvore(string nome)
    {
        var pasta = Ambiente.PastaDeTeste(nome);
        return (pasta, RelatorioFicticio.Arvore(pasta));
    }

    static string[] Subpastas(string p) => Directory.GetDirectories(p).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray()!;

    [Fact]
    public void etapa_nova_e_a_seguinte_das_existentes()
    {
        var (pasta, a) = Arvore("etapa-nova");
        using var _ = pasta;
        var antes = Subpastas(a.Processo.Pasta);

        var e = Etapas.Nova(a.Processo.Pasta, Hoje);

        Assert.Equal("DESPACHO 03 - 2099-03-10", e.Nome);
        Assert.Equal(Path.Combine(a.Processo.Pasta, "DESPACHO 03 - 2099-03-10"), e.Pasta);
        Assert.Equal(3, e.Numero);
        Assert.Equal(Hoje, e.Data);
        Assert.True(e.Nova);
        Assert.Equal(antes, Subpastas(a.Processo.Pasta));
    }

    [Fact]
    public void pasta_sem_data_tambem_conta_no_numero()
    {
        var (pasta, a) = Arvore("etapa-sem-data");
        using var _ = pasta;
        Directory.CreateDirectory(Path.Combine(a.Processo.Pasta, "despacho 7 rascunho"));

        Assert.Equal("DESPACHO 08 - 2099-03-10", Etapas.Nova(a.Processo.Pasta, Hoje).Nome);
        Assert.DoesNotContain(Etapas.Existentes(a.Processo.Pasta), x => x.Numero == 7);
    }

    [Fact]
    public void processo_sem_despacho_comeca_em_01()
    {
        using var pasta = Ambiente.PastaDeTeste("etapa-vazia");
        Directory.CreateDirectory(Path.Combine(pasta, "PROJETO"));
        Directory.CreateDirectory(Path.Combine(pasta, "DESPACHOS"));

        var e = Etapas.Nova(pasta, Hoje);

        Assert.Equal("DESPACHO 01 - 2099-03-10", e.Nome);
        Assert.Empty(Etapas.Existentes(pasta));
        Assert.Equal(e, Etapas.Sugerida(pasta, Hoje));
    }

    [Fact]
    public void sugerida_e_a_ultima_etapa_ainda_sem_despacho()
    {
        var (pasta, a) = Arvore("etapa-sugerida");
        using var _ = pasta;

        var s = Etapas.Sugerida(a.Processo.Pasta, Hoje);
        Assert.Equal("DESPACHO 03 - 2099-03-10", s.Nome);
        Assert.True(s.Nova);

        // Pasta sem despacho mas de outro dia: o despacho de hoje vai para uma nova, com a data de hoje (2026-11-17).
        var antiga = Path.Combine(a.Processo.Pasta, "DESPACHO 03 - 2099-03-01");
        Directory.CreateDirectory(antiga);
        Assert.Equal("DESPACHO 04 - 2099-03-10", Etapas.Sugerida(a.Processo.Pasta, Hoje).Nome);

        // Pasta de hoje ainda sem despacho: reutilizada.
        var hoje = Path.Combine(a.Processo.Pasta, "DESPACHO 04 - 2099-03-10");
        Directory.CreateDirectory(hoje);
        File.WriteAllText(Path.Combine(hoje, "relatorio.pdf"), "x");
        s = Etapas.Sugerida(a.Processo.Pasta, Hoje);
        Assert.Equal(new EtapaDespacho(hoje, "DESPACHO 04 - 2099-03-10", 4, Hoje, false), s);

        File.WriteAllText(Path.Combine(hoje, "despacho_correcao_x.docx"), "x");
        Assert.Equal("DESPACHO 05 - 2099-03-10", Etapas.Sugerida(a.Processo.Pasta, Hoje).Nome);
    }

    [Fact]
    public void existentes_mais_recente_primeiro()
    {
        var (pasta, a) = Arvore("etapa-existentes");
        using var _ = pasta;
        Directory.CreateDirectory(Path.Combine(a.Processo.Pasta, "DESPACHO 05 - 2099-13-40"));

        var e = Etapas.Existentes(a.Processo.Pasta);

        Assert.Equal(new[] { (2, new DateOnly(2099, 2, 20)), (1, new DateOnly(2099, 1, 15)) }, e.Select(x => (x.Numero, x.Data)));
        Assert.All(e, x => Assert.False(x.Nova));
        Assert.Equal("DESPACHO 02 - 2099-02-20", e[0].Nome);
    }

    [Fact]
    public void data_por_extenso()
    {
        Assert.Equal("14 de novembro de 2026", Etapas.DataPorExtenso(new DateOnly(2026, 11, 14)));
        Assert.Equal("1º de dezembro de 2026", Etapas.DataPorExtenso(new DateOnly(2026, 12, 1)));
        Assert.Equal("5 de março de 2099", Etapas.DataPorExtenso(new DateOnly(2099, 3, 5)));
    }

    [Fact]
    public void nome_base_como_o_kit()
    {
        Assert.Equal("DESPACHO_CORRECAO_1_2099_FULANO_DE_EXEMPLO_FICTICIO",
            NomesDespacho.Base(TipoDespacho.Correcao, 1, 2099, "FULANO DE EXEMPLO (FICTICIO)"));
        Assert.Equal("DESPACHO_APROVACAO_9001_2026_CONDOMINIO_RESIDENCIAL_EXEMPLO_ALFA",
            NomesDespacho.Base(TipoDespacho.Aprovacao, 9001, 2026, "CONDOMÍNIO RESIDENCIAL EXEMPLO ALFA"));
    }

    [Fact]
    public void nome_longo_corta_em_palavra()
    {
        var b = NomesDespacho.Base(TipoDespacho.Correcao, 1, 2099, string.Join(' ', Enumerable.Repeat("PALAVRA", 30)));

        const string prefixo = "DESPACHO_CORRECAO_1_2099_";
        Assert.StartsWith(prefixo, b);
        var nome = b[prefixo.Length..];
        Assert.True(nome.Length <= 60, nome);
        Assert.True(nome.Length > 50, nome);
        Assert.EndsWith("PALAVRA", nome);
    }

    [Fact]
    public void nomes_livres_nunca_sobrescrevem()
    {
        using var pasta = Ambiente.PastaDeTeste("nomes-livres");

        Assert.Equal(new NomesLivres(Path.Combine(pasta, "BASE.DOCX"), Path.Combine(pasta, "BASE.PDF"), Path.Combine(pasta, "BASE.RECIBO-EDITOR.JSON")),
            NomesDespacho.Livres(pasta, "base"));

        File.WriteAllText(Path.Combine(pasta, "base.pdf"), "x");
        Assert.Equal(Path.Combine(pasta, "BASE (2).DOCX"), NomesDespacho.Livres(pasta, "base").Docx);

        File.WriteAllText(Path.Combine(pasta, "base (2).recibo-editor.json"), "x");
        var l = NomesDespacho.Livres(pasta, "base");
        Assert.Equal(new NomesLivres(Path.Combine(pasta, "BASE (3).DOCX"), Path.Combine(pasta, "BASE (3).PDF"), Path.Combine(pasta, "BASE (3).RECIBO-EDITOR.JSON")), l);
    }
}
