using System.Reflection;
using System.Xml.Linq;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Trilho colorido (auditoria da interface, §1): ícones vetoriais do dicionário, paleta nos três temas, atalhos Ctrl+1 … Ctrl+5
/// (M2).</summary>
public class TrilhoTestes
{
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    static XDocument Dicionario() =>
        XDocument.Load(Path.Combine(Path.GetDirectoryName(Ambiente.PastaTestes)!, "src", "Pmvni.App", "Recursos", "IconesModulos.xaml"));

    [Fact]
    public void toda_chave_de_icone_existe_no_dicionario()
    {
        var d = Dicionario();
        var modelos = d.Root!.Elements().Where(e => e.Name.LocalName == "DataTemplate").Select(e => (string?)e.Attribute(X + "Key")).ToHashSet();
        foreach (var m in Enum.GetValues<Modulo>())
        {
            Assert.Equal("PmvniIcone" + m, Modulos.ChaveIcone(m));
            Assert.Contains(Modulos.ChaveIcone(m), modelos);
        }
        Assert.Equal(Enum.GetValues<Modulo>().Length, modelos.Count);
    }

    [Fact]
    public void paleta_nos_tres_temas_com_as_mesmas_chaves()
    {
        var temas = Dicionario().Root!.Descendants().Where(e => e.Name.LocalName == "ResourceDictionary" && e.Attribute(X + "Key") is not null)
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Elements().Select(b => (string)b.Attribute(X + "Key")!).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(new[] { "Dark", "HighContrast", "Light" }, temas.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(temas["Light"], temas["Dark"]);
        Assert.Equal(temas["Light"], temas["HighContrast"]);
        foreach (var chave in new[] { "PmvniIconeAzul", "PmvniIconeVerde", "PmvniIconeAmarelo", "PmvniIconeRosa", "PmvniIconeMarrom", "PmvniTrilhoSelecionado", "PmvniTonalFundo", "PmvniTonalTexto" })
            Assert.Contains(chave, temas["Light"]);
        // Todo pincel usado pelos ícones existe na paleta.
        var usados = Dicionario().Descendants().SelectMany(e => e.Attributes()).Select(a => a.Value)
            .Where(v => v.StartsWith("{ThemeResource Pmvni", StringComparison.Ordinal)).Select(v => v["{ThemeResource ".Length..^1]).Distinct();
        Assert.All(usados, u => Assert.Contains(u, temas["Light"]));
    }

    [Fact]
    public void alto_contraste_no_ativo_nao_pinta_o_icone_sobre_a_cor_de_selecao()
    {
        // Revisão I5: os ícones em alto contraste ficam na cor do texto dos botões; o fundo do ativo não pode ser a cor de seleção
        // (Highlight claro + ButtonText branco = sem contraste). O destaque vai para a borda.
        var hc = Dicionario().Root!.Descendants().First(e => e.Name.LocalName == "ResourceDictionary" && (string?)e.Attribute(X + "Key") == "HighContrast");
        string Cor(string chave) => (string)hc.Elements().First(b => (string?)b.Attribute(X + "Key") == chave).Attribute("Color")!;
        Assert.Equal("{ThemeResource SystemColorButtonFaceColor}", Cor("PmvniTrilhoSelecionado"));
        Assert.Equal("{ThemeResource SystemColorHighlightColor}", Cor("PmvniTrilhoSelecionadoBorda"));
    }

    [Fact]
    public void cada_modulo_tem_atalho_e_comando()
    {
        // Os sistemas da Prefeitura (2026-11-16) ficam no trilho sem atalho: Ctrl+1 … Ctrl+5 continuam os mesmos.
        Assert.Equal(new[] { 1, 2, 3, 4, 9, 6, 7, 8, 5 }, Faixas.Disponiveis.Select(Modulos.Numero));
        Assert.Equal(new[] { Modulo.Essencial, Modulo.Gpi, Modulo.Alfresco }, Faixas.Disponiveis.Where(Modulos.EhSite));
        Assert.All(Faixas.Disponiveis.Where(Modulos.EhSite), m => Assert.StartsWith("http", Modulos.Endereco(m), StringComparison.Ordinal));
        foreach (var m in Faixas.Disponiveis.Where(Modulos.TemAtalho))
        {
            var tecla = "Ctrl+" + Modulos.Numero(m);
            Assert.Equal(Modulos.Comando(m), Atalhos.Comando(tecla));
            Assert.Equal(Modulos.Comando(m), Atalhos.Comando(tecla, Modulo.Legislacao));
            Assert.Equal(tecla, Atalhos.Tecla(Modulos.Comando(m)));
            Assert.Equal(m, Modulos.DoComando(Modulos.Comando(m)));
            Assert.False(ComandosApp.SoDoRelatorio(Modulos.Comando(m)));
        }
        Assert.Equal("Relatórios (Ctrl+1)", Modulos.Dica(Modulo.Relatorios));
        Assert.Equal("Ajustes (Ctrl+5)", Modulos.Dica(Modulo.Ajustes));
        Assert.Null(Modulos.DoComando(ComandosApp.ArquivoAbrir));
    }
}
