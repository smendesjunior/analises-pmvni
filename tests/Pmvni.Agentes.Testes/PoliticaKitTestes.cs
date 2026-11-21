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
    public void pro_runtime_do_kit_real_aceita_api_com_rota_explicita()
    {
        // O kit é só lido, no lugar: o análise pro 1.6.4+ do kit aceita a Anthropic API, com a rota declarada pelo editor.
        var script = Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins", "analise", "skills", "analise", "scripts", "rotas", "claude", "pro_runtime.py");
        if (!File.Exists(script)) Assert.Fail("Kit PMVNI ausente: " + Ambiente.RaizKit + " (defina PMVNI_KIT_RAIZ)");
        Assert.True(PoliticaKit.ApiPermitida(File.ReadAllText(script)));
    }

    [Fact]
    public void plugin_unico_do_kit_real_tem_os_tres_controladores_onde_o_editor_procura()
    {
        // Tarefa 18 (só leitura do kit): o Editor monta os três caminhos a partir da mesma pasta do plugin analise; o kit tem cada controlador na sua rota,
        // na versão mínima do Editor, e sem os plugins antigos.
        var plugins = Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins");
        var json = Path.Combine(plugins, "analise", ".codex-plugin", "plugin.json");
        if (!File.Exists(json)) Assert.Fail("Kit PMVNI ausente: " + Ambiente.RaizKit + " (defina PMVNI_KIT_RAIZ)");
        var versao = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(json))!["version"]!.GetValue<string>();
        foreach (var p in Enum.GetValues<Provedor>())
        {
            Assert.Null(PoliticaKit.PluginDesatualizado(p, versao));
            var (rota, nome) = Localizacao.RotaDoControlador(p);
            var script = Path.Combine(plugins, "analise", "skills", "analise", "scripts", "rotas", rota, nome);
            Assert.True(File.Exists(script), "controlador ausente: " + script);
            if (p == Provedor.Gemini) Assert.True(PoliticaKit.RuntimeGoogleValido(File.ReadAllText(script)));
        }
        Assert.False(Directory.Exists(Path.Combine(plugins, "analise-pro")));
        Assert.False(Directory.Exists(Path.Combine(plugins, "analise-google")));
    }
}
