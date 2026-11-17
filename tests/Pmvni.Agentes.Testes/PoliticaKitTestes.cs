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
        var script = Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins", "analise-pro", "skills", "analise-pro", "scripts", "pro_runtime.py");
        if (!File.Exists(script)) Assert.Fail("Kit PMVNI ausente: " + Ambiente.RaizKit + " (defina PMVNI_KIT_RAIZ)");
        Assert.True(PoliticaKit.ApiPermitida(File.ReadAllText(script)));
    }
}
