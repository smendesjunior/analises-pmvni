using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Página Inicial › Figuras ▾ (correções 2026-11-16): abre o editor de figuras direto da faixa, escolhendo a figura no menu.</summary>
public class MenuFigurasTestes
{
    [Fact]
    public void figuras_na_pagina_inicial_e_um_menu_dinamico_com_dica()
    {
        var inicio = Faixas.Para(Modulo.Relatorios).Abas.Single(a => a.Nome == "Página Inicial");
        var c = inicio.Comandos.Single(x => x.Id == ComandosApp.FigurasAbrirEditor);
        Assert.Equal("Figuras", c.Rotulo);
        Assert.Equal(TipoComando.Menu, c.Tipo);
        Assert.Empty(c.Itens!);   // os itens vêm da janela quando o menu abre (como Recentes)
        Assert.Equal(Glifos.Figuras, c.Glifo);
        Assert.Equal("Abrir uma figura no editor de figuras", c.TextoCompleto);
        Assert.True(ComandosApp.SoDoRelatorio(ComandosApp.FigurasAbrirEditor));
        Assert.Contains("editor", DicasComandos.Descricao(ComandosApp.FigurasAbrirEditor));
        // A aba Figuras continua com os seus comandos.
        Assert.Contains(Faixas.Para(Modulo.Relatorios).Abas.Single(a => a.Nome == "Figuras").Comandos, x => x.Id == ComandosApp.FigurasEditar);
    }

    [Fact]
    public void itens_do_menu_numeram_as_figuras_com_o_titulo()
    {
        var itens = MenuFiguras.Itens([("f1", "Implantação"), ("f2", null), ("f3", "  Volumetria ")]);
        Assert.Equal(new[] { ("f1", "Figura 1 — Implantação"), ("f2", "Figura 2"), ("f3", "Figura 3 — Volumetria") }, itens);
        Assert.Empty(MenuFiguras.Itens([]));
    }
}
