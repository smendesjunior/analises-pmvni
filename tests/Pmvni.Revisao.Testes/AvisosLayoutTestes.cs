using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Indicador de layout (auditoria A2): os avisos da composição viram uma lista pura (tipo, texto, folha) com um resumo para o
/// rodapé; nenhum aviso some e o estouro continua bloqueando.</summary>
public class AvisosLayoutTestes
{
    [Fact]
    public void sem_nada_nao_ha_aviso_e_o_resumo_e_vazio()
    {
        var a = AvisosLayout.Montar(1.0, 0, [], []);
        Assert.Empty(a);
        Assert.Equal("", AvisosLayout.Resumo(a));
        Assert.False(AvisosLayout.Bloqueia(a));
    }

    [Fact]
    public void reducao_e_alem_da_borda_viram_avisos_com_folha()
    {
        var a = AvisosLayout.Montar(1.25, 3, [4, 9], []);
        Assert.Equal(3, a.Count);
        Assert.Equal(TipoAvisoLayout.Reducao, a[0].Tipo);
        Assert.Null(a[0].Folha);
        // O mesmo texto da faixa antiga (a conferência do roteiro e o usuário leem a mesma frase).
        Assert.Equal("Conteúdo mais largo que a página: o documento foi reduzido 20% para caber (como na impressão do kit).", a[0].Texto);
        Assert.Equal(new int?[] { 4, 9 }, a.Skip(1).Select(x => x.Folha));
        Assert.All(a.Skip(1), x => Assert.Equal(TipoAvisoLayout.AlemDaBorda, x.Tipo));
        Assert.StartsWith("Folha 4: há conteúdo além da borda lateral", a[1].Texto);
        Assert.Equal("3 avisos de layout", AvisosLayout.Resumo(a));
        Assert.False(AvisosLayout.Bloqueia(a));
        Assert.Equal("1 aviso de layout", AvisosLayout.Resumo(AvisosLayout.Montar(1.0, 0, [2], [])));
    }

    [Fact]
    public void estouro_vem_primeiro_e_bloqueia()
    {
        var a = AvisosLayout.Montar(1.1, 1, [], [(7, "Folha 7: bloco 'x' não cabe na página (altura do conteúdo: 300 mm).")]);
        Assert.Equal(TipoAvisoLayout.Estouro, a[0].Tipo);
        Assert.Equal(7, a[0].Folha);
        Assert.True(a[0].Bloqueia);
        Assert.True(AvisosLayout.Bloqueia(a));
        Assert.Equal("1 bloqueio · 1 aviso", AvisosLayout.Resumo(a));
        var dois = AvisosLayout.Montar(1.0, 0, [], [(1, "a"), (2, "b")]);
        Assert.Equal("2 bloqueios", AvisosLayout.Resumo(dois));
        Assert.Equal("2 bloqueios · 2 avisos", AvisosLayout.Resumo(AvisosLayout.Montar(1.0, 0, [3, 5], [(1, "a"), (2, "b")])));
    }

    [Fact]
    public void reducao_so_com_aviso_de_largura()
    {
        // A escala sozinha (sem aviso de largura do plano) não vira aviso; o aviso sem escala diferente de 1 diz 0%.
        Assert.Empty(AvisosLayout.Montar(1.3, 0, [], []));
        Assert.Contains("reduzido 0%", AvisosLayout.Montar(1.0, 2, [], [])[0].Texto);
        Assert.Equal(20, AvisosLayout.ReducaoPercentual(1.25));
        Assert.Equal(9, AvisosLayout.ReducaoPercentual(1.1));
    }

    [Fact]
    public void modos_de_exibicao()
    {
        Assert.Equal(new[] { "indicador", "faixas" }, AvisosLayout.Modos);
        Assert.Equal("indicador", AvisosLayout.ModoPadrao);
    }
}
