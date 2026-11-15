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
    public void atalhos_sao_as_16_teclas_da_ponte()
    {
        var ponte = TeclasDaPonte();
        Assert.Equal(16, ponte.Count);
        Assert.Equal(ponte.Order(StringComparer.Ordinal), Atalhos.Mapa.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(ComandosApp.ArquivoAbrir, Atalhos.Comando("Ctrl+O"));
        Assert.Equal(ComandosApp.InicioExcluirRestaurar, Atalhos.Comando("Delete"));
        Assert.Equal(ComandosApp.InicioItemProximo, Atalhos.Comando("Alt+ArrowDown"));
        Assert.Equal(ComandosApp.PesquisaFocar, Atalhos.Comando("Ctrl+F"));
        Assert.Null(Atalhos.Comando("Ctrl+P"));
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
        Assert.Equal(new[] { "Arquivo", "Página Inicial", "Revisão", "Exibir", "Ajuda" }, f.Abas.Select(a => a.Nome));
        Assert.Equal("Página Inicial", f.Abas[f.AbaPadrao].Nome);
        Assert.Equal("Página Inicial", f.Abas[Faixas.IndiceDaAba(f, "aba que não existe mais")].Nome);
    }
}
