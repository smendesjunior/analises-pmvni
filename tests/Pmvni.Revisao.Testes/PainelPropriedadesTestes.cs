using System.Xml.Linq;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>O painel Propriedades em cartões (correções 2026-11-16): os estilos reutilizáveis existem em Estilos.xaml só com recursos de
/// tema (claro e escuro), o painel tem as seções com cabeçalho e os controles que a janela usa; e os ajudantes das esquadrias do painel
/// (o piso e o divisor editáveis).</summary>
public class PainelPropriedadesTestes
{
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    static string Fonte(params string[] partes) => Path.Combine([Path.GetDirectoryName(Ambiente.PastaTestes)!, "src", "Pmvni.App", .. partes]);

    [Fact]
    public void estilos_do_painel_existem_e_usam_so_recursos_de_tema()
    {
        var doc = XDocument.Load(Fonte("Estilos.xaml"));
        var estilos = doc.Root!.Elements().Where(e => ((string?)e.Attribute(X + "Key"))?.StartsWith("PmvniPainel", StringComparison.Ordinal) == true).ToList();
        var chaves = estilos.Select(e => (string)e.Attribute(X + "Key")!).ToHashSet();
        foreach (var k in new[] { "PmvniPainelCartao", "PmvniPainelCartaoCabecalho", "PmvniPainelCartaoIcone", "PmvniPainelCartaoTitulo", "PmvniPainelCartaoCorpo",
                     "PmvniPainelRotulo", "PmvniPainelValor", "PmvniPainelValorForte", "PmvniPainelSeparador", "PmvniPainelMarca", "PmvniPainelMarcaTexto",
                     "PmvniPainelBotaoPequeno", "PmvniPainelResultadoOk", "PmvniPainelResultadoErro", "PmvniPainelResultadoNeutro" })
            Assert.Contains(k, chaves);
        // Cores sempre de tema (nada de cor fixa, que some no escuro).
        foreach (var setter in estilos.SelectMany(e => e.Elements()))
        {
            var valor = (string?)setter.Attribute("Value") ?? "";
            if ((string?)setter.Attribute("Property") is "Background" or "Foreground" or "BorderBrush")
                Assert.StartsWith("{ThemeResource ", valor);
        }
    }

    [Fact]
    public void painel_tem_as_secoes_em_cartoes_e_os_controles_da_janela()
    {
        var texto = File.ReadAllText(Fonte("Paginas", "PaginaRelatorios.xaml"));
        foreach (var secao in new[] { "\"ESTADO\"", "\"ESQUADRIAS\"", "\"CONTEÚDO\"", "\"HISTÓRICO\"", "\"SITUAÇÃO\"", "\"JUSTIFICATIVA\"", "\"INTELIGÊNCIA ARTIFICIAL\"", "\"NO PDF\"" })
            Assert.Contains(secao, texto);
        foreach (var nome in new[] { "PropNoPdf", "PropSituacao", "PropSituacaoOriginal", "PropJustificativa", "PropJustificativaAviso", "PropAjustarAviso",
                     "PropAjustarTexto", "PropAjustarRotulo", "PropAjustarCancelar", "PropAjustarProgresso", "PropAjustarAnel", "PropAjustarEstado",
                     "PropEsquadrias", "PropEsquadriasCorpo", "PropCampos", "PropRestaurarOriginal", "PropHistorico" })
            Assert.Contains("x:Name=\"" + nome + "\"", texto);
        Assert.True(texto.Split("Style=\"{StaticResource PmvniPainelCartao}\"").Length - 1 >= 4);
    }

    [Fact]
    public void piso_e_divisor_do_minimo_editaveis_no_painel()
    {
        var caminho = Ambiente.Corpus("longo.json");
        var c = Importador.Importar(File.ReadAllBytes(caminho), caminho, ModeloOficial.Carregar(), Ambiente.Contexto());
        // AMBIENTE 01 do corpus: "17,00 ÷ 5 = 2,00 m²" (o texto erra a conta; o painel refaz: 3,40 m²).
        Assert.Equal("minimo/0/texto", Esquadrias.CaminhoDaContaDoMinimo(c.Projeto, "ambientes/01"));
        Assert.Equal(3.4m, Esquadrias.Calcular(c.Projeto, "ambientes/01")!.Minimo);
        Assert.Equal("12,00 ÷ 6 = 2,00 m²", Esquadrias.TextoDaContaDoMinimo(12m, 6m));
        Assert.Equal("9,50 ÷ 8 = 1,19 m²", Esquadrias.TextoDaContaDoMinimo(9.5m, 8m));
        Assert.Equal("1,50", Esquadrias.TextoDoNumero(1.5m));
        Assert.Null(Esquadrias.CaminhoDaContaDoMinimo(c.Projeto, "indices/01"));
    }
}
