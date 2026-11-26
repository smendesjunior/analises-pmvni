using Xunit;

namespace Pmvni.Agentes.Testes;

public class PoliticaKitTestes
{
    [Theory]
    [InlineData("raise ValueError('API paga desativada no analise-pro.')", false)]
    [InlineData("# API paga desativada", false)]
    [InlineData("route = 'api'", false)]                                   // 1.6.3: deduzia a rota pela chave; sem a rota explícita, não
    [InlineData("", false)]
    [InlineData("ROTA_VAR = 'PMVNI_ANALISE_PRO_ROTA'", true)]              // 1.6.4+: a rota é declarada pelo editor
    [InlineData("PMVNI_ANALISE_PRO_ROTA # API paga desativada", false)]   // a âncora ainda proíbe
    [InlineData("PMVNI_ANALISE_PRO_ROTA api paga desativada", true)]      // a âncora é exata (sem caixa diferente)
    public void api_permitida_pela_ancora(string texto, bool esperado) => Assert.Equal(esperado, PoliticaKit.ApiPermitida(texto));

    [Fact]
    public void kit_instalavel_fornece_somente_o_plugin_chatgpt_com_gerador_proprio()
    {
        var plugins = Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins");
        var plugin = Assert.Single(Directory.GetDirectories(plugins));
        Assert.Equal("analise-chatgpt", Path.GetFileName(plugin));
        foreach (var manifesto in new[] { "plugin.json", Path.Combine(".codex-plugin", "plugin.json") })
        {
            var dados = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(plugin, manifesto)))!;
            Assert.True(Version.TryParse(dados["version"]?.ToString(), out _));
        }
        var skill = Path.Combine(plugin, "skills", "analise-chatgpt");
        Assert.True(File.Exists(Path.Combine(skill, "scripts", "gerar-rpmvni.mjs")));
        Assert.Empty(Directory.GetFiles(plugin, "*.py", SearchOption.AllDirectories));
        Assert.False(Directory.Exists(Path.Combine(skill, "scripts", "rotas")));
    }

    [Fact]
    public void plugin_define_os_quinze_grupos_e_cobre_as_vinte_e_oito_secoes()
    {
        var arquivo = Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins", "analise-chatgpt", "skills", "analise-chatgpt", "references", "grupos.json");
        var grupos = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(arquivo))!.AsArray();
        Assert.Equal(Enumerable.Range(1, 15), grupos.Select(g => (int)g!["id"]!));
        Assert.Equal(Enumerable.Range(1, 28), grupos.SelectMany(g => g!["secoes"]!.AsArray()).Select(s => (int)s!).Distinct().Order());
    }
}
