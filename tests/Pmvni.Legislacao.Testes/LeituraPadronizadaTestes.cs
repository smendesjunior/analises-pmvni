using Xunit;

namespace Pmvni.Legislacao.Testes;

public sealed class LeituraPadronizadaTestes
{
    [Fact]
    public void Estrutura_epigrafe_ementa_divisoes_artigos_paragrafos_e_incisos()
    {
        var paginas = new[]
        {
            "PREFEITURA MUNICIPAL\nLEI COMPLEMENTAR Nº 70, DE 20 DE DEZEMBRO DE 1990\nInstitui as normas gerais para as edificações\nnos perímetros urbanos.\nTÍTULO I\nPARTE GERAL\nCAPÍTULO I - DAS DISPOSIÇÕES PRELIMINARES\nArt. 1º Qualquer construção ou re-\nforma somente poderá ser executada\n1",
            "PREFEITURA MUNICIPAL\nmediante licença.\nParágrafo único. A licença é pessoal.\nArt. 2 São documentos:\nI - planta de situação;\nII – memorial;\na) assinado;\n2",
            "PREFEITURA MUNICIPAL\nArt. 10. Esta lei entra em vigor.\n§ 1º Revogam-se as disposições.\n3",
        };
        var b = LeituraPadronizada.Estruturar(paginas);
        Assert.Equal(TipoBlocoLei.Epigrafe, b[0].Tipo);
        Assert.Equal("LEI COMPLEMENTAR Nº 70, DE 20 DE DEZEMBRO DE 1990", b[0].Texto);
        Assert.Equal(TipoBlocoLei.Ementa, b[1].Tipo);
        Assert.StartsWith("Institui as normas gerais", b[1].Texto);
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Titulo, Rotulo: "TÍTULO I", Texto: "PARTE GERAL" });
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Capitulo, Rotulo: "CAPÍTULO I", Texto: "DAS DISPOSIÇÕES PRELIMINARES" });
        var art1 = b.Single(x => x.Rotulo == "Art. 1º");
        Assert.Equal("Qualquer construção ou reforma somente poderá ser executada mediante licença.", art1.Texto);
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Paragrafo, Rotulo: "Parágrafo único" });
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Artigo, Rotulo: "Art. 2º" });
        Assert.Equal(2, b.Count(x => x.Tipo == TipoBlocoLei.Inciso));
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Alinea, Rotulo: "a)" });
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Artigo, Rotulo: "Art. 10" });
        Assert.DoesNotContain(b, x => x.Texto.Contains("PREFEITURA MUNICIPAL", StringComparison.Ordinal));
    }

    [Fact]
    public void Texto_vazio_nao_gera_blocos() => Assert.Empty(LeituraPadronizada.Estruturar(["", "  "]));
}
