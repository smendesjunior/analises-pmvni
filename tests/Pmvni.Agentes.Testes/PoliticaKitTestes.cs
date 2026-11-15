using Xunit;

namespace Pmvni.Agentes.Testes;

public class PoliticaKitTestes
{
    [Theory]
    [InlineData("raise ValueError('API paga desativada no analise-pro.')", false)]
    [InlineData("# API paga desativada", false)]
    [InlineData("route = 'api'", true)]
    [InlineData("", true)]
    [InlineData("api paga desativada", true)]   // a âncora é exata (sem caixa diferente)
    public void api_permitida_pela_ancora(string texto, bool esperado) => Assert.Equal(esperado, PoliticaKit.ApiPermitida(texto));

    [Fact]
    public void pro_runtime_do_kit_real_proibe_api()
    {
        // O kit é só lido, no lugar: a âncora que o editor procura existe no pro_runtime.py de hoje.
        var script = Path.Combine(Ambiente.RaizKit, "01 - PLUGINS", "plugins", "analise-pro", "skills", "analise-pro", "scripts", "pro_runtime.py");
        if (!File.Exists(script)) Assert.Fail("Kit PMVNI ausente: " + Ambiente.RaizKit + " (defina PMVNI_KIT_RAIZ)");
        Assert.False(PoliticaKit.ApiPermitida(File.ReadAllText(script)));
    }
}
