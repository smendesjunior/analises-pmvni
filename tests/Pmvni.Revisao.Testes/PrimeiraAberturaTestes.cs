using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Tarefa 16: a primeira abertura (instalação incompleta → <c>primeira-abertura.ps1</c> fora do Editor, por <c>.cmd</c> +
/// <c>explorer.exe</c>) e o botão "Atualizar kit PMVNI" com análise em andamento. Nada é iniciado de verdade: o lançador é falso.</summary>
public sealed class PrimeiraAberturaTestes : IDisposable
{
    readonly string pasta = Path.Combine(Path.GetTempPath(), "pmvni-primeira-" + Guid.NewGuid().ToString("N"));

    public PrimeiraAberturaTestes()
    {
        Directory.CreateDirectory(pasta);
        File.WriteAllText(Path.Combine(pasta, PrimeiraAbertura.Script), "# fictício");
    }

    public void Dispose() { try { Directory.Delete(pasta, true); } catch (IOException) { } }

    const string Completo = "Blueprint Pipeline    INSTALADO\r\nFYP-Floorplan    INSTALADO\r\nDocling MCP 3.5.0    INSTALADO\r\nPyMuPDF 2.1    INSTALADO\r\n"
        + "Codex / analise    INSTALADO 3.1.0\r\nCodex / analise-pro    INSTALADO 2.1.0\r\nCodex / ferramentas PDF    CONFIGURADO\r\nClaude Code    AUSENTE\r\n";

    [Fact]
    public void PrimeiraAbertura_Chamada()
    {
        var chamadas = new List<(string Programa, string Argumento)>();
        void Iniciar(string programa, string argumento) => chamadas.Add((programa, argumento));

        // Incompleta: sem resumo, componente pendente ou falha → inicia o .cmd pelo explorer.exe.
        // Plugin de análise abaixo do mínimo do Editor (analise 3.1.0) também é incompleta.
        foreach (var resumo in new[] { null, "", "Blueprint Pipeline    PENDENTE\r\nPyMuPDF 2.1    INSTALADO\r\n", Completo + "FALHA    Plugin analise\r\n",
                     Completo.Replace("INSTALADO 3.1.0", "INSTALADO 3.0.1", StringComparison.Ordinal) })
        {
            chamadas.Clear();
            Assert.True(PrimeiraAbertura.Conferir(resumo, pasta, Iniciar, Agora, null));
            var (programa, argumento) = Assert.Single(chamadas);
            Assert.Equal("explorer.exe", Path.GetFileName(programa), ignoreCase: true);
            Assert.Equal(Path.Combine(pasta, PrimeiraAbertura.Cmd), argumento);
            var cmd = File.ReadAllText(argumento);
            Assert.Contains("powershell.exe", cmd);
            Assert.Contains("-File \"" + Path.Combine(pasta, PrimeiraAbertura.Script) + "\"", cmd);
            File.Delete(argumento);
        }

        // Completa (AUSENTE não conta: o instalador não instala o Claude Code): não inicia nada nem grava o .cmd.
        chamadas.Clear();
        Assert.False(PrimeiraAbertura.Conferir("\uFEFF" + Completo, pasta, Iniciar, Agora, null));
        Assert.Empty(chamadas);
        Assert.False(File.Exists(Path.Combine(pasta, PrimeiraAbertura.Cmd)));
    }

    static readonly DateTime Agora = new(2026, 11, 21, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void PrimeiraAbertura_falha_persistente_tenta_no_maximo_a_cada_6_horas()
    {
        var chamadas = 0;
        void Iniciar(string programa, string argumento) => chamadas++;
        const string Falhou = "PyMuPDF 2.1    INSTALADO\r\nFALHA    Docling indisponivel\r\n";

        Assert.False(PrimeiraAbertura.Conferir(Falhou, pasta, Iniciar, Agora, Agora.AddHours(-1)));
        Assert.False(PrimeiraAbertura.Conferir(Falhou, pasta, Iniciar, Agora, Agora.AddHours(-5.9)));
        Assert.Equal(0, chamadas);
        Assert.True(PrimeiraAbertura.Conferir(Falhou, pasta, Iniciar, Agora, Agora.AddHours(-6)));
        // Relógio que voltou (tentativa "no futuro"): não trava para sempre.
        Assert.True(PrimeiraAbertura.Conferir(Falhou, pasta, Iniciar, Agora, Agora.AddHours(3)));
        Assert.Equal(2, chamadas);
    }

    [Fact]
    public async Task AtualizarKit_ComAnaliseAtiva()
    {
        var iniciou = 0;
        Task<string> Iniciar() { iniciou++; return Task.FromResult("Atualização do kit solicitada."); }

        var texto = await AtualizacaoKit.SolicitarAsync(["19486-2026"], Iniciar);
        Assert.Equal(0, iniciou);
        Assert.Equal(AtualizacaoKit.AvisoAnaliseEmAndamento(["19486-2026"]), texto);
        Assert.Contains("análise em andamento (19486-2026)", texto);

        Assert.Equal("Atualização do kit solicitada.", await AtualizacaoKit.SolicitarAsync([], Iniciar));
        Assert.Equal(1, iniciou);
    }
}
