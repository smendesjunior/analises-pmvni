using System.Security.Cryptography;
using System.Text;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Composicao.Testes;

public class ModeloOficialTestes
{
    private static readonly string[] ArquivosEmbutidos =
    {
        "modelo_relatorio.html",
        "relatorio.css",
        "brasao.png",
        "dados_modelo.json",
        "exemplo_figura.png",
        "exemplo_mapa.png",
    };

    private static string Sha256(byte[] conteudo) =>
        Convert.ToHexString(SHA256.HashData(conteudo)).ToLowerInvariant();

    [Fact]
    public void cada_arquivo_embutido_confere_com_o_registro_rev14()
    {
        foreach (var nome in ArquivosEmbutidos)
        {
            Assert.True(
                ModeloOficial.HashesDoRegistro.ContainsKey(nome),
                $"Registro sem hash para {nome}.");

            var conteudo = ModeloOficial.LerArquivo(nome);

            Assert.Equal(ModeloOficial.HashesDoRegistro[nome], Sha256(conteudo));
        }
    }

    [Fact]
    public void registro_rev14_tem_os_sete_arquivos_do_kit()
    {
        var esperado = new[]
        {
            "gerar_relatorio.py",
            "modelo_relatorio.html",
            "relatorio.css",
            "brasao.png",
            "dados_modelo.json",
            "exemplo_figura.png",
            "exemplo_mapa.png",
        };

        Assert.Equal(esperado.OrderBy(x => x), ModeloOficial.HashesDoRegistro.Keys.OrderBy(x => x));
        Assert.Equal("044a4265ffcfb704008087a55c561d284d767301eff54f703028d806cfd7167a", ModeloOficial.HashesDoRegistro["gerar_relatorio.py"]);
    }

    [Fact]
    public void carregar_devolve_revisao_14()
    {
        var modelo = ModeloOficial.Carregar();

        Assert.Equal("14", modelo.Revisao);
        Assert.False(string.IsNullOrWhiteSpace(modelo.Html));
        Assert.False(string.IsNullOrWhiteSpace(modelo.Css));
        Assert.NotEmpty(modelo.Brasao);
        Assert.Equal(ArquivosEmbutidos.Length, modelo.Sha256.Count);
        Assert.Equal(ModeloOficial.HashesDoRegistro["modelo_relatorio.html"], modelo.Sha256["modelo_relatorio.html"]);
    }

    [Fact]
    public void carregar_devolve_html_e_css_com_conteudo_do_kit()
    {
        var modelo = ModeloOficial.Carregar();

        Assert.Equal(Encoding.UTF8.GetString(ModeloOficial.LerArquivo("modelo_relatorio.html")), modelo.Html);
        Assert.Equal(Encoding.UTF8.GetString(ModeloOficial.LerArquivo("relatorio.css")), modelo.Css);
        Assert.Equal(ModeloOficial.LerArquivo("brasao.png"), modelo.Brasao);
    }

    [Fact]
    public void ler_arquivo_inexistente_lanca_excecao()
    {
        Assert.Throws<ArgumentException>(() => ModeloOficial.LerArquivo("nao_existe.txt"));
    }
}
