using System.Text;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Tarefa 13: o Editor calcula o progresso e a previsão; o <c>diretrizes.md</c> que o kit lê nas três rotas leva só o que o analista pediu (e, no
/// analise, o tratamento dos dados), sem as instruções antigas para a IA gravar <c>progresso\*.json</c> e <c>estimativa.json</c>.</summary>
public partial class OrquestradorTestes
{
    [Fact]
    public async Task Pedido_NaoTemInstrucoesDeProgresso()
    {
        const string texto = "Confira o recuo frontal (fictício).";
        string Diretrizes(Cena c, string plugin) => File.ReadAllText(Path.Combine(c.Controle(plugin), "diretrizes.md"), Encoding.UTF8);
        void SemProgresso(Cena c, string diretrizes)
        {
            Assert.DoesNotContain("progresso", diretrizes, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("estimativa", diretrizes, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(Path.Combine(c.Registro.Pasta, "progresso")));
        }

        using (var c = new Cena("orq-pedido-chatgpt", "ok", Provedor.ChatGpt))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt, texto: texto), c.Progresso, CancellationToken.None), Limite);
            Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
            var d = Diretrizes(c, "analise");
            Assert.Equal(PedidoAgente.Diretrizes(texto), d);
            SemProgresso(c, d);
        }
        using (var c = new Cena("orq-pedido-claude", "claude_ok", Provedor.Claude, j => j["auto"]!["etapa_ms"] = 50))
        {
            var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.Claude, texto: "  " + texto + "\n"), c.Progresso, CancellationToken.None), Limite);
            Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
            var d = Diretrizes(c, "analise-pro");
            Assert.Equal(texto + "\n", d);
            SemProgresso(c, d);
        }
    }
}
