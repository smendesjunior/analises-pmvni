using System.Diagnostics;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>"Testar" em Ajustes (desenho §12.8): só comandos locais, 15 s cada, sempre o agente falso.</summary>
[Collection("Ambiente")]
public class DiagnosticoTestes
{
    [Fact]
    public async Task diagnostico_com_o_agente_falso()
    {
        using var t = Ambiente.PastaDeTeste("diag-ok");
        using var _ = new VariavelDeAmbiente("AGENTE_FALSO_CENARIO", Ambiente.Cenario("ok"));
        var pecas = Ambiente.PecasFalsas(t);

        var chatgpt = await Diagnostico.TestarAsync(Provedor.ChatGpt, pecas, ModoAcesso.Conta, false, CancellationToken.None);
        Assert.Equal("Codex 0.99.0 · conectado com a conta do ChatGPT · plugin analise 4.0.0 habilitado", chatgpt.Linha);
        Assert.Empty(chatgpt.Avisos);

        var claude = await Diagnostico.TestarAsync(Provedor.Claude, pecas, ModoAcesso.Conta, false, CancellationToken.None);
        Assert.Equal("Claude Code 2.9.0 · conectado com a conta do Claude · Análise — rota Claude 4.0.0 (kit: 4.0.0) · Python 3.14.0", claude.Linha);
        Assert.Empty(claude.Avisos);

        // Modo chave com chave guardada: o aviso fixo; a linha é a mesma.
        var comChave = await Diagnostico.TestarAsync(Provedor.ChatGpt, pecas, ModoAcesso.Chave, true, CancellationToken.None);
        Assert.Equal(chatgpt.Linha, comChave.Linha);
        Assert.Equal(new[] { "Chave guardada; ela só é conferida pelo provedor na primeira análise." }, comChave.Avisos);
        var semChave = await Diagnostico.TestarAsync(Provedor.ChatGpt, pecas, ModoAcesso.Chave, false, CancellationToken.None);
        Assert.Empty(semChave.Avisos);
    }

    [Fact]
    public async Task diagnostico_sem_login_e_com_plugin_desligado()
    {
        using var t = Ambiente.PastaDeTeste("diag-login");
        using var _ = new VariavelDeAmbiente("AGENTE_FALSO_CENARIO", Ambiente.CenarioAjustado(t, "ok", o => o["login"] = "nao"));
        var pecas = Ambiente.PecasFalsas(t);

        var chatgpt = await Diagnostico.TestarAsync(Provedor.ChatGpt, pecas, ModoAcesso.Conta, false, CancellationToken.None);
        Assert.Equal("Codex 0.99.0 · não conectado · plugin analise 4.0.0 habilitado", chatgpt.Linha);
        var claude = await Diagnostico.TestarAsync(Provedor.Claude, pecas, ModoAcesso.Conta, false, CancellationToken.None);
        Assert.Equal("Claude Code 2.9.0 · não conectado · Análise — rota Claude 4.0.0 (kit: 4.0.0) · Python 3.14.0", claude.Linha);

        var desligado = await Diagnostico.TestarAsync(Provedor.ChatGpt, Ambiente.PecasFalsas(t, pluginHabilitado: false) with { VersaoPluginAnalise = null },
            ModoAcesso.Conta, false, CancellationToken.None);
        Assert.Equal("Codex 0.99.0 · não conectado · plugin analise não habilitado no Codex", desligado.Linha);
    }

    [Fact]
    public async Task diagnostico_de_peca_ausente_e_aviso_de_versao_do_kit()
    {
        using var t = Ambiente.PastaDeTeste("diag-ausente");
        using var _ = new VariavelDeAmbiente("AGENTE_FALSO_CENARIO", Ambiente.Cenario("ok"));
        var pecas = Ambiente.PecasFalsas(t);

        var semCodex = await Diagnostico.TestarAsync(Provedor.ChatGpt, pecas with { Codex = null }, ModoAcesso.Conta, false, CancellationToken.None);
        Assert.Equal("Codex não encontrado", semCodex.Linha);

        var semTudo = await Diagnostico.TestarAsync(Provedor.Claude, pecas with { Claude = null, Python = null, ProRuntime = null, VersaoAnalisePro = null }, ModoAcesso.Conta, false, CancellationToken.None);
        Assert.Equal("Claude Code não encontrado · Análise — rota Claude não instalada no Codex · Python não encontrado", semTudo.Linha);

        var kitNovo = await Diagnostico.TestarAsync(Provedor.Claude, pecas with { VersaoAnaliseProKit = "4.1.0" }, ModoAcesso.Conta, false, CancellationToken.None);
        Assert.Contains("Análise — rota Claude 4.0.0 (kit: 4.1.0)", kitNovo.Linha);
        Assert.Equal(new[] { "O kit tem o plugin analise 4.1.0; instalado 4.0.0. Atualize pelo INSTALAR_KIT_PMVNI.cmd." }, kitNovo.Avisos);

        // py.exe -3: o launcher leva o -3 também na pergunta da versão.
        var launcher = await Diagnostico.TestarAsync(Provedor.Claude, pecas with { PythonPeloLauncher = true }, ModoAcesso.Conta, false, CancellationToken.None);
        Assert.EndsWith("Python 3.14.0", launcher.Linha);
    }

    [Fact]
    public async Task diagnostico_de_programa_que_nao_responde()
    {
        using var t = Ambiente.PastaDeTeste("diag-trava");
        var pidArquivo = Path.Combine(t, "pid.txt");
        using var _ = new VariavelDeAmbiente("AGENTE_FALSO_CENARIO",
            Ambiente.CenarioAjustado(t, "ok", o => { o["trava"] = "versao"; o["pid_arquivo"] = pidArquivo; }));

        var relogio = Stopwatch.StartNew();
        var r = await Diagnostico.TestarAsync(Provedor.ChatGpt, Ambiente.PecasFalsas(t), ModoAcesso.Conta, false, CancellationToken.None);
        relogio.Stop();

        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(16), "levou " + relogio.Elapsed);
        Assert.Equal("Codex não verificado: não respondeu em 15 s", r.Linha);
        Assert.True(File.Exists(pidArquivo));
        var pid = int.Parse(File.ReadAllText(pidArquivo));
        Assert.True(Ambiente.EsperarAte(() => !Ambiente.Vivo(pid), TimeSpan.FromSeconds(5)), "o processo que travou continua vivo");
    }

    [Fact]
    public async Task diagnostico_cancelado_para_na_hora()
    {
        using var t = Ambiente.PastaDeTeste("diag-cancelar");
        using var _ = new VariavelDeAmbiente("AGENTE_FALSO_CENARIO", Ambiente.CenarioAjustado(t, "ok", o => { o["trava"] = "versao"; o["pid_arquivo"] = Path.Combine(t, "pid.txt"); }));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Diagnostico.TestarAsync(Provedor.ChatGpt, Ambiente.PecasFalsas(t), ModoAcesso.Conta, false, cts.Token));
        var pid = int.Parse(File.ReadAllText(Path.Combine(t, "pid.txt")));
        Assert.True(Ambiente.EsperarAte(() => !Ambiente.Vivo(pid), TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData("codex-cli 0.167.0-alpha.4.1", "0.167.0")]
    [InlineData("codex-cli 0.99.0\n", "0.99.0")]
    [InlineData("WARNING: proxy\ncodex-cli 1.2.3", "1.2.3")]
    [InlineData("outra coisa", null)]
    [InlineData("", null)]
    public void versao_do_codex(string saida, string? esperado) => Assert.Equal(esperado, Diagnostico.VersaoCodex(saida));

    [Theory]
    [InlineData("2.1.318 (Claude Code)", "2.1.318")]
    [InlineData("2.9.0 (Claude Code)\r\n", "2.9.0")]
    [InlineData("Claude Code 3.0.1", "3.0.1")]
    [InlineData("nada", null)]
    public void versao_do_claude(string saida, string? esperado) => Assert.Equal(esperado, Diagnostico.VersaoClaude(saida));

    [Theory]
    [InlineData("Python 3.14.10", "3.14.10")]
    [InlineData("Python 3.14.0\r\n", "3.14.0")]
    [InlineData("Python 3.13.0rc1", "3.13.0")]
    [InlineData("", null)]
    public void versao_do_python(string saida, string? esperado) => Assert.Equal(esperado, Diagnostico.VersaoPython(saida));

    [Theory]
    [InlineData(0, "Logged in using ChatGPT", "conectado com a conta do ChatGPT")]
    [InlineData(0, "Logged in using an API key - sk-...abcd", "conectado com chave de API")]
    [InlineData(1, "Not logged in", "não conectado")]
    [InlineData(-1, "não respondeu em 15 s", "não verificado: não respondeu em 15 s")]
    [InlineData(2, "Error: algo\noutra linha", "não verificado: Error: algo")]
    [InlineData(1, "", "não conectado")]
    public void sessao_do_codex(int codigo, string saida, string esperado) => Assert.Equal(esperado, Diagnostico.SessaoCodex(codigo, saida));

    [Theory]
    [InlineData(0, "{\"loggedIn\":true,\"authMethod\":\"claude.ai\"}", "conectado com a conta do Claude")]
    [InlineData(1, "{\"loggedIn\":false,\"authMethod\":\"none\"}", "não conectado")]
    [InlineData(-1, "não respondeu em 15 s", "não verificado: não respondeu em 15 s")]
    [InlineData(0, "isto não é json", "não verificado: isto não é json")]
    [InlineData(0, "{\"authMethod\":\"claude.ai\"}", "não verificado: {\"authMethod\":\"claude.ai\"}")]
    public void sessao_do_claude(int codigo, string saida, string esperado) => Assert.Equal(esperado, Diagnostico.SessaoClaude(codigo, saida));
}
