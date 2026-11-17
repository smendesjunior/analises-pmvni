using System.Reflection;
using System.Text.RegularExpressions;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Atalhos de teclado (Tarefa 9): um mapa só (tecla canônica → comando) para a janela e para as teclas que a página repassa.</summary>
public class AtalhosTestes
{
    /// <summary>As teclas que a página repassa ao host: o <c>Set</c> ATALHOS de <c>web/paginador/src/edicao.ts</c>, lido do arquivo.</summary>
    private static IReadOnlyList<string> TeclasDaPonte()
    {
        var arquivo = Path.Combine(Path.GetDirectoryName(Ambiente.PastaTestes)!, "web", "paginador", "src", "edicao.ts");
        var m = Regex.Match(File.ReadAllText(arquivo), @"const ATALHOS[^=]*=\s*new Set\(\[(.*?)\]\)", RegexOptions.Singleline);
        Assert.True(m.Success, "edicao.ts não tem mais o Set ATALHOS.");
        return Regex.Matches(m.Groups[1].Value, "'([^']+)'").Select(x => x.Groups[1].Value).ToList();
    }

    private static HashSet<string> IdsConhecidos() =>
        typeof(ComandosApp).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(c => c.IsLiteral && c.FieldType == typeof(string))
            .Select(c => (string)c.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

    [Fact]
    public void atalhos_sao_as_22_teclas_da_ponte()
    {
        var ponte = TeclasDaPonte();
        Assert.Equal(22, ponte.Count);
        Assert.Equal(ponte.Order(StringComparer.Ordinal), Atalhos.Mapa.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(ComandosApp.ArquivoAbrir, Atalhos.Comando("Ctrl+O"));
        Assert.Equal(ComandosApp.InicioExcluirRestaurar, Atalhos.Comando("Delete"));
        Assert.Equal(ComandosApp.InicioItemProximo, Atalhos.Comando("Alt+ArrowDown"));
        Assert.Equal(ComandosApp.PesquisaFocar, Atalhos.Comando("Ctrl+F"));
        Assert.Equal(ComandosApp.ArquivoImprimir, Atalhos.Comando("Ctrl+P"));
        Assert.Null(Atalhos.Comando("Ctrl+Shift+P"));
        Assert.Null(Atalhos.Comando("ctrl+o"));
    }

    [Fact]
    public void todo_atalho_aponta_para_comando_conhecido()
    {
        var conhecidos = IdsConhecidos();
        Assert.All(Atalhos.Mapa.Values, id => Assert.Contains(id, conhecidos));
        Assert.Equal(Atalhos.Mapa.Count, Atalhos.Mapa.Values.Distinct(StringComparer.Ordinal).Count());
        foreach (var (tecla, id) in Atalhos.Mapa) Assert.Equal(tecla, Atalhos.Tecla(id));
        Assert.Null(Atalhos.Tecla(ComandosApp.ArquivoFechar));
    }

    [Fact]
    public void relatorios_abre_em_pagina_inicial()
    {
        var f = Faixas.Para(Modulo.Relatorios);
        Assert.Equal(new[] { "Arquivo", "Página Inicial", "Revisão", "Figuras", "Exibir", "Ajuda" }, f.Abas.Select(a => a.Nome));
        Assert.Equal("Página Inicial", f.Abas[f.AbaPadrao].Nome);
        Assert.Equal("Página Inicial", f.Abas[Faixas.IndiceDaAba(f, "aba que não existe mais")].Nome);
    }

    /// <summary>As teclas que o leitor da legislação repassa ao host: o <c>Set</c> ATALHOS de <c>web/paginador/src/legislacao/leitor.ts</c>.</summary>
    private static IReadOnlyList<string> TeclasDoLeitor()
    {
        var arquivo = Path.Combine(Path.GetDirectoryName(Ambiente.PastaTestes)!, "web", "paginador", "src", "legislacao", "leitor.ts");
        var m = Regex.Match(File.ReadAllText(arquivo), @"const ATALHOS[^=]*=\s*new Set\(\[(.*?)\]\)", RegexOptions.Singleline);
        Assert.True(m.Success, "leitor.ts não tem mais o Set ATALHOS.");
        return Regex.Matches(m.Groups[1].Value, "'([^']+)'").Select(x => x.Groups[1].Value).ToList();
    }

    [Fact]
    public void atalhos_da_legislacao_sao_os_do_leitor()
    {
        var leitor = TeclasDoLeitor();
        Assert.Equal(16, leitor.Count);
        Assert.Equal(leitor.Order(StringComparer.Ordinal), Atalhos.MapaLegislacao.Keys.Order(StringComparer.Ordinal));
        var faixa = Faixas.Comandos(Faixas.Para(Modulo.Legislacao)).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        Assert.All(Atalhos.MapaLegislacao.Values, id => Assert.True(faixa.Contains(id) || id == ComandosApp.PesquisaFocar || Modulos.DoComando(id) is not null, id));
        Assert.Equal(Atalhos.MapaLegislacao.Count, Atalhos.MapaLegislacao.Values.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal("Ctrl+Shift+C", Atalhos.Tecla(ComandosApp.LegislacaoCopiarCitacao, Modulo.Legislacao));
        Assert.Equal("F3", Atalhos.Tecla(ComandosApp.LegislacaoOcorrenciaProxima, Modulo.Legislacao));
    }

    [Fact]
    public void atalhos_do_relatorio_nao_mudam()
    {
        Assert.Equal(22, Atalhos.Mapa.Count);
        Assert.Equal(ComandosApp.ExibirLargura, Atalhos.Comando("Ctrl+0", Modulo.Relatorios));
        Assert.Equal(ComandosApp.LegislacaoAjustarLargura, Atalhos.Comando("Ctrl+0", Modulo.Legislacao));
        Assert.Equal(ComandosApp.InicioItemProximo, Atalhos.Comando("Alt+ArrowDown", Modulo.Relatorios));
        Assert.Equal(ComandosApp.LegislacaoPaginaProxima, Atalhos.Comando("Alt+ArrowDown", Modulo.Legislacao));
        Assert.Null(Atalhos.Comando("Ctrl+C", Modulo.Relatorios));
        Assert.Null(Atalhos.Comando("Delete", Modulo.Legislacao));
        Assert.Equal(ComandosApp.PesquisaFocar, Atalhos.Comando("Ctrl+F", Modulo.Despachos));
    }

    [Fact]
    public void ctrl_p_imprime_em_todo_modulo_com_documento()
    {
        // Imprimir (2026-11-17): Ctrl+P é o Imprimir do módulo; em Imóveis, o Imprimir do relatório do imóvel.
        foreach (var m in ComandosImpressao.Modulos)
        {
            Assert.Equal(ComandosApp.ArquivoImprimir, Atalhos.Comando("Ctrl+P", m));
            Assert.Equal("Ctrl+P", Atalhos.Tecla(ComandosApp.ArquivoImprimir, m));
        }
        Assert.Equal(ComandosApp.ImoveisImprimir, Atalhos.Comando("Ctrl+P", Modulo.Imoveis));
        Assert.Equal("Ctrl+P", Atalhos.Tecla(ComandosApp.ImoveisImprimir, Modulo.Imoveis));
        Assert.Null(Atalhos.Tecla(ComandosApp.ArquivoImprimir, Modulo.Imoveis));
        // O resto do mapa de Imóveis é o de sempre.
        Assert.Equal(Atalhos.Mapa.Keys.Order(StringComparer.Ordinal), Atalhos.MapaImoveis.Keys.Order(StringComparer.Ordinal));
        Assert.All(Atalhos.MapaImoveis.Where(p => p.Key != "Ctrl+P"), p => Assert.Equal(Atalhos.Mapa[p.Key], p.Value));
        Assert.Equal(ComandosApp.ArquivoImprimir, Atalhos.Comando("Ctrl+P", Modulo.Legislacao));
    }
}
