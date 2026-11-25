using Xunit;

namespace Pmvni.Agentes.Testes;

public partial class OrquestradorTestes
{
    static Pecas ComGemini(Cena c) => c.Pecas with
    {
        Agy = Ambiente.AgenteFalso,
        GoogleRuntime = Ambiente.Controlador(c.Pasta, "gemini", "google_runtime.py"),
        VersaoAnaliseGoogle = "4.0.0", VersaoAnaliseGoogleKit = "4.0.0",
    };

    [Theory]
    [InlineData(ModoAcesso.Conta, PerfilGemini.ContaUltra)]
    [InlineData(ModoAcesso.ContaGemini, PerfilGemini.ContaGemini)]
    [InlineData(ModoAcesso.Chave, PerfilGemini.ApiCreditos)]
    [InlineData(ModoAcesso.Chave, PerfilGemini.ApiGratuita)]
    public async Task Google_desativado_bloqueia_inicio_retomada_e_acompanhamento(ModoAcesso modo, PerfilGemini perfil)
    {
        using var c = new Cena("gemini-desativado-" + perfil, "claude_ok", Provedor.Gemini);
        c.Pecas = ComGemini(c);
        var orquestrador = c.Orq();
        var pedido = c.Pedido(Provedor.Gemini, modo) with { Perfil = perfil, Ficticio = true };
        var registro = Path.Combine(c.Registro.Pasta, "execucao.json");
        var antes = File.Exists(registro) ? File.ReadAllBytes(registro) : null;
        foreach (var retomar in new[] { false, true })
        {
            var fim = await orquestrador.ExecutarAsync(pedido with { Retomar = retomar }, c.Progresso, CancellationToken.None);
            Assert.Equal(("falhou", Provedores.RotaDesativada), (fim.Situacao, fim.Mensagem));
        }
        var acompanhamento = await orquestrador.AcompanharClaudeAsync(pedido, DateTimeOffset.Now, c.Progresso, CancellationToken.None);
        Assert.Equal(Provedores.RotaDesativada, acompanhamento.Mensagem);
        Assert.Null(await orquestrador.SituacaoClaudeAsync(c.Processo.Pasta, c.Pecas, Provedor.Gemini));
        Assert.False(await orquestrador.CancelarClaudeAsync(c.Processo.Pasta, c.Pecas, Provedor.Gemini));
        Assert.False(File.Exists(c.Falso("chamadas.txt")));
        Assert.Equal(antes, File.Exists(registro) ? File.ReadAllBytes(registro) : null);
    }
}
