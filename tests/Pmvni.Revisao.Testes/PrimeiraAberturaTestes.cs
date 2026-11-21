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
        + "Codex / analise    INSTALADO 3.1.0\r\nCodex / ferramentas PDF    CONFIGURADO\r\nClaude Code    AUSENTE\r\n";

    [Fact]
    public void PrimeiraAbertura_Chamada()
    {
        var chamadas = new List<(string Programa, string Argumento)>();
        void Iniciar(string programa, string argumento) => chamadas.Add((programa, argumento));

        // Incompleta: sem resumo, componente pendente ou falha → inicia o .cmd pelo explorer.exe.
        foreach (var resumo in new[] { null, "", "Blueprint Pipeline    PENDENTE\r\nPyMuPDF 2.1    INSTALADO\r\n", Completo + "FALHA    Plugin analise\r\n" })
        {
            chamadas.Clear();
            Assert.True(PrimeiraAbertura.Conferir(resumo, pasta, Iniciar));
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
        Assert.False(PrimeiraAbertura.Conferir("﻿" + Completo, pasta, Iniciar));
        Assert.Empty(chamadas);
        Assert.False(File.Exists(Path.Combine(pasta, PrimeiraAbertura.Cmd)));
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
