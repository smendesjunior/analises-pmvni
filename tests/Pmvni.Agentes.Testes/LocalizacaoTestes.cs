using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Onde estão o Codex, o Python e as peças do kit (desenho §12.4–§12.5), sobre pastas montadas no teste.</summary>
public class LocalizacaoTestes
{
    static string Arquivo(string caminho, string conteudo = "x", DateTime? escritaUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
        if (escritaUtc is { } d) File.SetLastWriteTimeUtc(caminho, d);
        return caminho;
    }

    static OpcoesLocalizacao Opcoes(string t, string[]? path = null, string? codex = null, string? python = null) => new(
        LocalAppData: Path.Combine(t, "local"), CodexHome: Path.Combine(t, "codex-home"),
        Path: path ?? new[] { Path.Combine(t, "bin") }, PastaKit: Path.Combine(t, "kit"), CodexEscolhido: codex, PythonEscolhido: python);

    [Fact]
    public void codex_mais_novo_da_pasta_do_app()
    {
        using var t = Ambiente.PastaDeTeste("loc-codex");
        var a = Arquivo(Path.Combine(t, "local", "OpenAI", "Codex", "bin", "a", "codex.exe"), escritaUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var b = Arquivo(Path.Combine(t, "local", "OpenAI", "Codex", "bin", "b", "codex.exe"), escritaUtc: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        var doPath = Arquivo(Path.Combine(t, "bin", "codex.exe"));
        Assert.Equal(b, Localizacao.Ler(Opcoes(t)).Codex);                 // o mais novo, antes do PATH

        File.SetLastWriteTimeUtc(a, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(a, Localizacao.Ler(Opcoes(t)).Codex);                 // pela data, não pelo nome

        Directory.Delete(Path.Combine(t, "local"), true);
        Assert.Equal(doPath, Localizacao.Ler(Opcoes(t)).Codex);            // sem o app: PATH\codex.exe

        File.Delete(doPath);
        Arquivo(Path.Combine(t, "bin", "codex.cmd"));                      // shim do npm não é executado
        Arquivo(Path.Combine(t, "bin", "codex.ps1"));
        Assert.Null(Localizacao.Ler(Opcoes(t)).Codex);

        var escolhido = Arquivo(Path.Combine(t, "escolhido", "meu-codex.exe"));
        Arquivo(Path.Combine(t, "bin", "codex.exe"));
        Assert.Equal(escolhido, Localizacao.Ler(Opcoes(t, codex: escolhido)).Codex);              // escolhido vale mais que tudo
        Assert.Null(Localizacao.Ler(Opcoes(t, codex: Path.Combine(t, "nao-existe.exe"))).Codex);  // escolhido inexistente: nada (não cai no padrão)
    }

    [Fact]
    public void python_ignora_o_atalho_da_loja()
    {
        using var t = Ambiente.PastaDeTeste("loc-python");
        var loja = Path.Combine(t, "local", "Microsoft", "WindowsApps");
        var real = Path.Combine(t, "Python314");
        Arquivo(Path.Combine(loja, "python.exe"));
        var certo = Arquivo(Path.Combine(real, "python.exe"));
        var p = Localizacao.Ler(Opcoes(t, new[] { loja, real }));
        Assert.Equal(certo, p.Python);
        Assert.False(p.PythonPeloLauncher);

        // Só o atalho da loja e o py.exe: o launcher.
        var py = Arquivo(Path.Combine(t, "launcher", "py.exe"));
        var s = Localizacao.Ler(Opcoes(t, new[] { loja, Path.Combine(t, "launcher") }));
        Assert.Equal(py, s.Python);
        Assert.True(s.PythonPeloLauncher);

        // Escolhido pelo usuário vale mais.
        var meu = Arquivo(Path.Combine(t, "meu", "python.exe"));
        var e = Localizacao.Ler(Opcoes(t, new[] { loja, real }, python: meu));
        Assert.Equal(meu, e.Python);
        Assert.False(e.PythonPeloLauncher);
        Assert.True(Localizacao.Ler(Opcoes(t, python: py)).PythonPeloLauncher);
        Assert.Null(Localizacao.Ler(Opcoes(t, new[] { loja }, python: Path.Combine(t, "nao-existe.exe"))).Python);
        Assert.Null(Localizacao.Ler(Opcoes(t, new[] { loja })).Python);
    }

    [Fact]
    public void claude_do_path()
    {
        using var t = Ambiente.PastaDeTeste("loc-claude");
        Assert.Null(Localizacao.Ler(Opcoes(t)).Claude);
        var c = Arquivo(Path.Combine(t, "bin", "claude.exe"));
        Assert.Equal(c, Localizacao.Ler(Opcoes(t)).Claude);
    }

    [Fact]
    public void plugin_unico_tres_controladores_e_versao_do_kit()
    {
        // Tarefa 18: um só plugin (analise 4.0.0+); os três controladores na pasta da rota, dentro da maior versão instalada.
        using var t = Ambiente.PastaDeTeste("loc-unico");
        var cache = Path.Combine(t, "codex-home", "plugins", "cache", "pmvni-kit", "analise");
        string Rota(string v, string rota, string script) => Path.Combine(cache, v, "skills", "analise", "scripts", "rotas", rota, script);
        foreach (var v in new[] { "4.0.0", "4.0.1", "4.10.0" })
        {
            Arquivo(Rota(v, "codex", "executar_analise_62.py"), "# " + v);
            Arquivo(Rota(v, "claude", "pro_runtime.py"), "# " + v);
            Arquivo(Rota(v, "gemini", "google_runtime.py"), "ROTA_VAR = 'PMVNI_ANALISE_GOOGLE_ROTA'  # " + v);
        }
        Arquivo(Path.Combine(t, "kit", "01 - PLUGINS", "plugins", "analise", ".codex-plugin", "plugin.json"), "{ \"name\": \"analise\", \"version\": \"4.0.1\" }");
        // Restos de uma instalação antiga (analise-pro, analise-google) não são lidos.
        Arquivo(Path.Combine(t, "codex-home", "plugins", "cache", "pmvni-kit", "analise-pro", "9.9.9", "skills", "analise-pro", "scripts", "pro_runtime.py"), "# antigo");
        Arquivo(Path.Combine(t, "codex-home", "plugins", "cache", "pmvni-kit", "analise-google", "9.9.9", "skills", "analise-google", "scripts", "google_runtime.py"),
            "PMVNI_ANALISE_GOOGLE_ROTA");

        var p = Localizacao.Ler(Opcoes(t));
        Assert.Equal("4.10.0", p.VersaoPluginAnalise);                   // por número, não por texto (4.10.0 > 4.0.1)
        Assert.Equal(Rota("4.10.0", "codex", "executar_analise_62.py"), p.Analise62);
        Assert.Equal(Rota("4.10.0", "claude", "pro_runtime.py"), p.ProRuntime);
        Assert.Null(p.GoogleRuntime);
        Assert.Equal(p.ProRuntime, p.RuntimeDoKit(Provedor.Claude));
        Assert.Equal(p.GoogleRuntime, p.RuntimeDoKit(Provedor.Gemini));
        Assert.Equal(("4.10.0", (string?)null), (p.VersaoAnalisePro, p.VersaoAnaliseGoogle));   // uma só versão para as três rotas
        Assert.Equal(("4.0.1", (string?)null), (p.VersaoAnaliseProKit, p.VersaoAnaliseGoogleKit));
        foreach (var pv in Provedores.Ativos) Assert.Null(PoliticaKit.PluginDesatualizado(pv, p.VersaoPluginAnalise));

        // Sem o cache, não há controlador nem versão instalada.
        Directory.Delete(Path.Combine(t, "codex-home"), true);
        var sem = Localizacao.Ler(Opcoes(t));
        Assert.Equal((null, null, null), (sem.Analise62, sem.ProRuntime, sem.GoogleRuntime));
        Assert.Equal((null, null, null), (sem.VersaoPluginAnalise, sem.VersaoAnalisePro, sem.VersaoAnaliseGoogle));
        Assert.Equal("4.0.1", sem.VersaoAnaliseProKit);
        Assert.True(sem.ApiPermitidaNoKit);   // nada que proíba

        // plugin.json ilegível não derruba a leitura.
        Arquivo(Path.Combine(t, "kit", "01 - PLUGINS", "plugins", "analise", ".codex-plugin", "plugin.json"), "{ não é json");
        Assert.Null(Localizacao.Ler(Opcoes(t)).VersaoAnaliseProKit);
    }

    [Fact]
    public void plugin_antigo_3x_sem_rotas_pede_atualizacao_nas_tres_rotas()
    {
        // O analise 3.x (com o executar_analise_62.py fora de rotas\) e os antigos analise-pro/analise-google: nenhum controlador é usado, e cada botão
        // pede a atualização do kit (analise 4.0.0).
        using var t = Ambiente.PastaDeTeste("loc-antigo");
        var cache = Path.Combine(t, "codex-home", "plugins", "cache", "pmvni-kit");
        Arquivo(Path.Combine(cache, "analise", "3.1.0", "skills", "analise", "scripts", "executar_analise_62.py"), "# 3.1.0");
        Arquivo(Path.Combine(cache, "analise-pro", "2.1.0", "skills", "analise-pro", "scripts", "pro_runtime.py"), "PMVNI_ANALISE_PRO_ROTA");
        Arquivo(Path.Combine(cache, "analise-google", "2.1.0", "skills", "analise-google", "scripts", "google_runtime.py"), "PMVNI_ANALISE_GOOGLE_ROTA");

        var p = Localizacao.Ler(Opcoes(t));
        Assert.Equal((null, null, null), (p.Analise62, p.ProRuntime, p.GoogleRuntime));
        Assert.Equal(("3.1.0", "3.1.0", (string?)null), (p.VersaoPluginAnalise, p.VersaoAnalisePro, p.VersaoAnaliseGoogle));
        foreach (var pv in Provedores.Ativos)
        {
            var m = PoliticaKit.PluginDesatualizado(pv, OrquestradorAnalise.VersaoDoPlugin(pv, p));
            Assert.NotNull(m);
            Assert.StartsWith("Atualize o kit PMVNI (analise 4.0.0 ou mais novo): o analise instalado neste computador é o 3.1.0.", m);
        }
    }

    [Fact]
    public void plugin_analise_habilitado_no_config_do_codex()
    {
        using var t = Ambiente.PastaDeTeste("loc-plugin");
        var home = Path.Combine(t, "codex-home");
        var config = Path.Combine(home, "config.toml");
        Arquivo(Path.Combine(home, "plugins", "cache", "pmvni-kit", "analise", "2.3.0", ".keep"));
        Arquivo(Path.Combine(home, "plugins", "cache", "pmvni-kit", "analise", "2.4.0", ".keep"));

        Assert.False(Localizacao.Ler(Opcoes(t)).PluginAnaliseHabilitado);        // sem config.toml

        Arquivo(config, "model = \"x\"\n[plugins.\"analise@pmvni-kit\"]\nenabled = true\n\n[plugins.\"despacho@pmvni-kit\"]\nenabled = false\n");
        var p = Localizacao.Ler(Opcoes(t));
        Assert.True(p.PluginAnaliseHabilitado);
        Assert.Equal("2.4.0", p.VersaoPluginAnalise);

        Arquivo(config, "[plugins.\"analise@pmvni-kit\"]\nenabled = false\n");
        Assert.False(Localizacao.Ler(Opcoes(t)).PluginAnaliseHabilitado);

        Arquivo(config, "[plugins.\"analise@pmvni-kit\"]\nnome = \"x\"\n");       // sem a chave enabled
        Assert.False(Localizacao.Ler(Opcoes(t)).PluginAnaliseHabilitado);

        // O enabled = true de OUTRO plugin não conta.
        Arquivo(config, "[plugins.\"analise-pro@pmvni-kit\"]\nenabled = true\n[plugins.\"analise@pmvni-kit\"]\nnome = \"x\"\n");
        Assert.False(Localizacao.Ler(Opcoes(t)).PluginAnaliseHabilitado);

        // Comentário e espaços.
        Arquivo(config, "# comentário\n[plugins.\"analise@pmvni-kit\"]   # o plugin\n  enabled   =   true   # ligado\n");
        Assert.True(Localizacao.Ler(Opcoes(t)).PluginAnaliseHabilitado);
    }

    [Fact]
    public void codex_home_do_ambiente_e_respeitado()
    {
        using var t = Ambiente.PastaDeTeste("loc-home");
        using (new VariavelDeAmbiente("CODEX_HOME", Path.Combine(t, "outro-home")))
            Assert.Equal(Path.Combine(t, "outro-home"), OpcoesLocalizacao.DoAmbiente(Path.Combine(t, "kit"), null, null).CodexHome);
        using (new VariavelDeAmbiente("CODEX_HOME", null))
            Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex"),
                OpcoesLocalizacao.DoAmbiente(Path.Combine(t, "kit"), "c.exe", "p.exe").CodexHome);
        var o = OpcoesLocalizacao.DoAmbiente("kit", "c.exe", "p.exe");
        Assert.Equal(("c.exe", "p.exe", "kit"), (o.CodexEscolhido, o.PythonEscolhido, o.PastaKit));
        Assert.NotEmpty(o.Path);
    }

    [Fact]
    public void api_do_analise_pro_conforme_o_kit()
    {
        using var t = Ambiente.PastaDeTeste("loc-api");
        var script = Ambiente.Controlador(t, "claude", "pro_runtime.py");
        Arquivo(script, "if route not in ('conta', 'local'): raise ValueError('API paga desativada no analise-pro.')\n");
        Assert.False(Localizacao.Ler(Opcoes(t)).ApiPermitidaNoKit);
        Arquivo(script, "if route not in ('conta', 'api'): raise ValueError('rota inválida')\n");      // 1.6.3: sem rota explícita
        Assert.False(Localizacao.Ler(Opcoes(t)).ApiPermitidaNoKit);
        Arquivo(script, "ROTA_VAR = 'PMVNI_ANALISE_PRO_ROTA'\n");                                       // 1.6.4+
        Assert.True(Localizacao.Ler(Opcoes(t)).ApiPermitidaNoKit);
    }
}
