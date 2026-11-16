using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Painel de navegação (correções 2026-11-15): filtro rápido por situação, pesquisa, seções recolhíveis com contador e Somente
/// alterados.</summary>
public class FiltroNavegacaoTestes
{
    static readonly IReadOnlySet<string> Nenhuma = new HashSet<string>();

    static readonly List<EntradaNavegacao> Lista =
    [
        new("Capa", true, null, false),                                           // 0
        new("LOCALIZAÇÃO", true, null, false),                                    // 1
        new("ANÁLISE", true, null, false),                                        // 2
        new("01  Calçada e fachada", false, "NAO_ATENDE", false),                 // 3
        new("02  Afastamento da cobertura", false, "NAO_COMPROVADO", true),       // 4
        new("03  Afastamento da calçada", false, "DIVERGENCIA_INTERNA", false),   // 5
        new("04  Recuo do telhado", false, "ATENDE", true),                       // 6
        new("IMAGENS DO PROJETO", true, null, false),                             // 7
        new("Figura 1  Planta do térreo", false, null, false),                    // 8
    ];

    [Fact]
    public void sem_filtro_mostra_tudo_e_conta_por_secao()
    {
        var r = FiltroNavegacao.Aplicar(Lista, FiltroSituacao.Todos, null, false, Nenhuma);
        Assert.Equal(Enumerable.Range(0, Lista.Count), r.Visiveis);
        Assert.Equal(0, r.ItensPorSecao[1]);
        Assert.Equal(4, r.ItensPorSecao[2]);
        Assert.Equal(1, r.ItensPorSecao[7]);
        Assert.Equal(4, r.ItensPorFiltro[FiltroSituacao.Todos]);
        Assert.Equal(1, r.ItensPorFiltro[FiltroSituacao.NaoAtende]);
        Assert.Equal(1, r.ItensPorFiltro[FiltroSituacao.FaltaComprovar]);
        Assert.Equal(1, r.ItensPorFiltro[FiltroSituacao.Divergentes]);
        Assert.Equal(1, r.ItensPorFiltro[FiltroSituacao.Atende]);
    }

    [Theory]
    [InlineData(FiltroSituacao.NaoAtende, 3)]
    [InlineData(FiltroSituacao.FaltaComprovar, 4)]
    [InlineData(FiltroSituacao.Divergentes, 5)]
    [InlineData(FiltroSituacao.Atende, 6)]
    public void filtro_por_situacao_mostra_so_a_secao_com_o_item(FiltroSituacao f, int item)
    {
        var r = FiltroNavegacao.Aplicar(Lista, f, null, false, Nenhuma);
        Assert.Equal([2, item], r.Visiveis);
        Assert.Equal(1, r.ItensPorSecao[2]);
    }

    [Fact]
    public void situacoes_de_tabela_entram_no_mesmo_filtro()
    {
        Assert.True(FiltroNavegacao.Passa(FiltroSituacao.Atende, "CONTA_CONFERE"));
        Assert.True(FiltroNavegacao.Passa(FiltroSituacao.FaltaComprovar, "PENDENTE"));
        Assert.True(FiltroNavegacao.Passa(FiltroSituacao.Divergentes, "DIVERGENCIA"));
        Assert.False(FiltroNavegacao.Passa(FiltroSituacao.Atende, null));
        Assert.True(FiltroNavegacao.Passa(FiltroSituacao.Todos, null));
    }

    [Fact]
    public void pesquisa_ignora_acentos_e_acha_pela_situacao()
    {
        var r = FiltroNavegacao.Aplicar(Lista, FiltroSituacao.Todos, "  CALCADA ", false, Nenhuma);
        Assert.Equal([2, 3, 5], r.Visiveis);
        r = FiltroNavegacao.Aplicar(Lista, FiltroSituacao.Todos, "falta comprovar", false, Nenhuma);
        Assert.Equal([2, 4], r.Visiveis);
        r = FiltroNavegacao.Aplicar(Lista, FiltroSituacao.Todos, "imagens", false, Nenhuma); // o título da seção também
        Assert.Equal([7], r.Visiveis);
        r = FiltroNavegacao.Aplicar(Lista, FiltroSituacao.Todos, "nada disso", false, Nenhuma);
        Assert.Empty(r.Visiveis);
    }

    [Fact]
    public void secao_recolhida_fica_com_o_contador_e_esconde_os_itens()
    {
        var r = FiltroNavegacao.Aplicar(Lista, FiltroSituacao.Todos, null, false, new HashSet<string> { "ANÁLISE" });
        Assert.Equal([0, 1, 2, 7, 8], r.Visiveis);
        Assert.Equal(4, r.ItensPorSecao[2]);
    }

    [Fact]
    public void somente_alterados()
    {
        var r = FiltroNavegacao.Aplicar(Lista, FiltroSituacao.Todos, null, true, Nenhuma);
        Assert.Equal([2, 4, 6], r.Visiveis);
        r = FiltroNavegacao.Aplicar(Lista, FiltroSituacao.Atende, null, true, Nenhuma);
        Assert.Equal([2, 6], r.Visiveis);
    }

    [Fact]
    public void rotulos_e_normalizacao()
    {
        Assert.Equal(["Todos", "Não atende", "Falta comprovar", "Divergentes", "Atende"], FiltroNavegacao.Ordem.Select(FiltroNavegacao.Rotulo));
        Assert.Equal("afastamento da calcada", FiltroNavegacao.Normalizar("  Afastamento   da CALÇADA "));
        Assert.Equal("", FiltroNavegacao.Normalizar(null));
    }
}
