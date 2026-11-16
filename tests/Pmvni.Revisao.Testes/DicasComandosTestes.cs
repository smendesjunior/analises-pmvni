using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Dicas dos botões (correções 2026-11-15): todo comando da faixa, do cabeçalho e do trilho tem uma descrição curta; o título
/// traz o nome e o atalho entre parênteses; o motivo de um botão desabilitado entra como terceira linha.</summary>
public class DicasComandosTestes
{
    [Fact]
    public void todo_comando_de_todas_as_faixas_tem_descricao()
    {
        var faltando = Faixas.Disponiveis
            .SelectMany(m => Faixas.Comandos(Faixas.Para(m)).Select(c => (m, c.Id)))
            .Where(x => string.IsNullOrWhiteSpace(DicasComandos.Descricao(x.Id)))
            .Select(x => $"{x.m}: {x.Id}")
            .Distinct()
            .ToList();
        Assert.Empty(faltando);
    }

    [Fact]
    public void cabecalho_e_trilho_tem_descricao()
    {
        foreach (var id in new[] { ComandosApp.EditarDesfazer, ComandosApp.EditarRefazer, ComandosApp.PesquisaFocar })
            Assert.False(string.IsNullOrWhiteSpace(DicasComandos.Descricao(id)), id);
        foreach (var m in Enum.GetValues<Modulo>())
            Assert.False(string.IsNullOrWhiteSpace(DicasComandos.Descricao(Modulos.Comando(m))), m.ToString());
        foreach (var f in FiltroNavegacao.Ordem)
            Assert.False(string.IsNullOrWhiteSpace(DicasComandos.DescricaoFiltro(f)), f.ToString());
    }

    [Fact]
    public void descricoes_sao_frases_curtas()
    {
        var todas = DicasComandos.Descricoes.Values
            .Concat(Faixas.OrdemSituacoes.Select(c => DicasComandos.Descricao(ComandosApp.ComandoDaSituacao(c))));
        foreach (var d in todas)
        {
            var palavras = d.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.True(palavras <= 16, $"Descrição longa demais ({palavras} palavras): {d}");
            Assert.EndsWith(".", d);
            Assert.True(char.IsUpper(d[0]), d);
        }
    }

    [Fact]
    public void titulo_tem_o_nome_e_o_atalho_do_modulo()
    {
        Assert.Equal("Abrir (Ctrl+O)", DicasComandos.DoComando(ComandosApp.ArquivoAbrir, "Abrir", Modulo.Relatorios).Titulo);
        Assert.Equal("Abre um relatório .rpmvni salvo no computador.", DicasComandos.DoComando(ComandosApp.ArquivoAbrir, "Abrir", Modulo.Relatorios).Descricao);
        // Na Legislação vale o mapa do leitor: Copiar é Ctrl+C; sem atalho, só o nome.
        Assert.Equal("Copiar (Ctrl+C)", DicasComandos.DoComando(ComandosApp.LegislacaoCopiar, "Copiar", Modulo.Legislacao).Titulo);
        Assert.Equal("Conferência", DicasComandos.DoComando(ComandosApp.RevisaoConferencia, "Conferência", Modulo.Relatorios).Titulo);
        Assert.Equal("Relatórios (Ctrl+1)", DicasComandos.DoComando(ComandosApp.ModuloRelatorios, "Relatórios", Modulo.Relatorios).Titulo);
    }

    [Fact]
    public void motivo_vira_terceira_linha_so_quando_diz_algo_novo()
    {
        var d = DicasComandos.DoComando(ComandosApp.LegislacaoCopiar, "Copiar", Modulo.Legislacao, "Selecione um trecho do texto.");
        Assert.Equal("Selecione um trecho do texto.", d.Motivo);
        Assert.Equal(string.Join(Environment.NewLine, "Copiar (Ctrl+C)", "Copia o trecho selecionado da lei.", "Selecione um trecho do texto."), d.ToString());
        Assert.Null(DicasComandos.DoComando(ComandosApp.LegislacaoCopiar, "Copiar", Modulo.Legislacao, "  ").Motivo);
        Assert.Null(DicasComandos.DoComando(ComandosApp.LegislacaoCopiar, "Copiar", Modulo.Legislacao, "Copia o trecho selecionado da lei.").Motivo);
    }

    [Fact]
    public void itens_de_situacao_usam_o_texto_do_kit()
    {
        foreach (var c in Faixas.OrdemSituacoes)
            Assert.Contains(Pmvni.Composicao.Kit.Situacoes.Sit[c].Texto, DicasComandos.Descricao(ComandosApp.ComandoDaSituacao(c)));
        Assert.Equal("", DicasComandos.Descricao("nao.existe"));
    }
}
