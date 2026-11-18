using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pmvni.Agentes;

/// <summary>De onde ler as peças. Tudo é injetável (os testes montam pastas inteiras em <c>tests/saida</c>). <paramref name="PastaClaude"/> (a pasta
/// <c>.claude</c> do Claude Code: plugins e credenciais) e <paramref name="PerfilClaude"/> (o <c>.claude.json</c>) só servem para mostrar a conta e o plugin
/// analise do Claude em Ajustes; null = não ler (o roteiro e os testes nunca leem a conta de verdade).</summary>
public sealed record OpcoesLocalizacao(string LocalAppData, string CodexHome, IReadOnlyList<string> Path, string PastaKit, string? CodexEscolhido, string? PythonEscolhido,
    string? PastaClaude = null, string? PerfilClaude = null)
{
    /// <summary>O ambiente real: <c>%LOCALAPPDATA%</c>, <c>CODEX_HOME</c> (ou <c>%USERPROFILE%\.codex</c>), o <c>PATH</c> e o Claude Code
    /// (<c>CLAUDE_CONFIG_DIR</c>, ou <c>%USERPROFILE%\.claude</c> e <c>%USERPROFILE%\.claude.json</c>).</summary>
    public static OpcoesLocalizacao DoAmbiente(string pastaKit, string? codex, string? python)
    {
        var home = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (string.IsNullOrWhiteSpace(home))
            home = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
        var path = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var perfil = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var configClaude = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        var (pastaClaude, perfilClaude) = string.IsNullOrWhiteSpace(configClaude)
            ? (System.IO.Path.Combine(perfil, ".claude"), System.IO.Path.Combine(perfil, ".claude.json"))
            : (configClaude, System.IO.Path.Combine(configClaude, ".claude.json"));
        return new OpcoesLocalizacao(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), home, path, pastaKit, codex, python, pastaClaude, perfilClaude);
    }
}

/// <summary>O que foi achado neste computador. <c>null</c> = não achado.</summary>
public sealed record Pecas(
    string? Codex,
    string? Claude,
    string? Python,
    bool PythonPeloLauncher,
    string? ProRuntime,
    string? VersaoAnalisePro,
    string? VersaoAnaliseProKit,
    bool PluginAnaliseHabilitado,
    string? VersaoPluginAnalise,
    bool ApiPermitidaNoKit)
{
    /// <summary>O Antigravity CLI (<c>agy.exe</c>), que executa o análise Google pela conta Google ou pela chave do Google AI Studio.</summary>
    public string? Agy { get; init; }

    /// <summary>O <c>google_runtime.py</c> do análise Google instalado no Codex (a maior versão, se conhece a rota explícita).</summary>
    public string? GoogleRuntime { get; init; }

    public string? VersaoAnaliseGoogle { get; init; }

    public string? VersaoAnaliseGoogleKit { get; init; }

    /// <summary>O controlador do kit que o provedor usa: análise pro (Claude) ou análise Google (Gemini); null para o ChatGPT ou se não instalado.</summary>
    public string? RuntimeDoKit(Provedor p) => p switch { Provedor.Claude => ProRuntime, Provedor.Gemini => GoogleRuntime, _ => null };
}

/// <summary>Onde estão o Codex, o Claude Code, o Python e as peças do kit (desenho §12.4–§12.5). Só lê; nada falha: peça ilegível é peça ausente.</summary>
public static class Localizacao
{
    static readonly Regex Habilitado = new(@"^enabled\s*=\s*true\s*(#.*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly Regex DottedHabilitado = new(@"^plugins\.[""']analise@pmvni-kit[""']\.enabled\s*=\s*true\s*(#.*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly Regex InlineHabilitado = new(@"^plugins\.[""']analise@pmvni-kit[""']\s*=\s*\{[^}]*\benabled\s*=\s*true\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static Pecas Ler(OpcoesLocalizacao o)
    {
        var codex = AcharCodex(o);
        var claude = NoPath(o, "claude.exe");
        var (python, launcher) = AcharPython(o);

        var cacheKit = Path.Combine(o.CodexHome, "plugins", "cache", "pmvni-kit");
        var (versaoPro, pastaPro) = MaiorVersao(Path.Combine(cacheKit, "analise-pro"));
        string? proRuntime = null;
        if (pastaPro is not null)
        {
            var script = Path.Combine(pastaPro, "skills", "analise-pro", "scripts", "pro_runtime.py");
            if (File.Exists(script)) proRuntime = script;
        }

        var apiPermitida = true;
        if (proRuntime is not null)
        {
            try { apiPermitida = PoliticaKit.ApiPermitida(File.ReadAllText(proRuntime)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        var (versaoPlugin, _) = MaiorVersao(Path.Combine(cacheKit, "analise"));

        var (versaoGoogle, pastaGoogle) = MaiorVersao(Path.Combine(cacheKit, PluginsInstalados.AnaliseGoogle));
        string? googleRuntime = null;
        if (pastaGoogle is not null)
        {
            var script = Path.Combine(pastaGoogle, "skills", PluginsInstalados.AnaliseGoogle, "scripts", "google_runtime.py");
            try { if (File.Exists(script) && PoliticaKit.RuntimeGoogleValido(File.ReadAllText(script))) googleRuntime = script; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }

        return new Pecas(codex, claude, python, launcher, proRuntime, versaoPro, VersaoDoKit(o), PluginHabilitado(o.CodexHome), versaoPlugin, apiPermitida)
        {
            Agy = AcharAgy(o), GoogleRuntime = googleRuntime, VersaoAnaliseGoogle = versaoGoogle, VersaoAnaliseGoogleKit = VersaoDoKit(o, PluginsInstalados.AnaliseGoogle),
        };
    }

    static bool EhExe(string caminho) => caminho.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    static string? NoPath(OpcoesLocalizacao o, string nome, Func<string, bool>? ignorarPasta = null)
    {
        foreach (var dir in o.Path)
        {
            if (ignorarPasta?.Invoke(dir) == true) continue;
            try
            {
                var f = Path.Combine(dir, nome);
                if (File.Exists(f)) return f;
            }
            catch (ArgumentException) { /* entrada inválida no PATH */ }
        }
        return null;
    }

    /// <summary>O escolhido em Ajustes (se não existir, nada: não cai no padrão); senão o mais novo de <c>%LOCALAPPDATA%\OpenAI\Codex\bin\*\codex.exe</c>;
    /// senão <c>codex.exe</c> no PATH. Shims <c>.cmd</c>/<c>.ps1</c> (npm) nunca são executados.</summary>
    static string? AcharCodex(OpcoesLocalizacao o)
    {
        if (!string.IsNullOrWhiteSpace(o.CodexEscolhido))
            return EhExe(o.CodexEscolhido) && File.Exists(o.CodexEscolhido) ? o.CodexEscolhido : null;

        var bin = Path.Combine(o.LocalAppData, "OpenAI", "Codex", "bin");
        try
        {
            if (Directory.Exists(bin))
            {
                var maisNovo = Directory.EnumerateDirectories(bin)
                    .Select(d => Path.Combine(d, "codex.exe"))
                    .Where(File.Exists)
                    .OrderByDescending(f => File.GetLastWriteTimeUtc(f))
                    .ThenByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
                if (maisNovo is not null) return maisNovo;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return NoPath(o, "codex.exe");
    }

    /// <summary>O <c>agy.exe</c> do instalador oficial (<c>%LOCALAPPDATA%\agy\bin</c>); senão o do PATH.</summary>
    static string? AcharAgy(OpcoesLocalizacao o)
    {
        try
        {
            var local = Path.Combine(o.LocalAppData, "agy", "bin", "agy.exe");
            if (File.Exists(local)) return local;
        }
        catch (ArgumentException) { }
        return NoPath(o, "agy.exe");
    }

    static bool EhAtalhoDaLoja(string dir) =>
        dir.TrimEnd('\\', '/').EndsWith(Path.Combine("Microsoft", "WindowsApps"), StringComparison.OrdinalIgnoreCase);

    /// <summary>O escolhido; senão o <c>python.exe</c> do PATH que não seja o atalho da Microsoft Store; senão o <c>py.exe</c> (launcher, com <c>-3</c>).</summary>
    static (string? Python, bool Launcher) AcharPython(OpcoesLocalizacao o)
    {
        if (!string.IsNullOrWhiteSpace(o.PythonEscolhido))
        {
            if (!EhExe(o.PythonEscolhido) || !File.Exists(o.PythonEscolhido)) return (null, false);
            return (o.PythonEscolhido, Path.GetFileName(o.PythonEscolhido).Equals("py.exe", StringComparison.OrdinalIgnoreCase));
        }
        var python = NoPath(o, "python.exe", EhAtalhoDaLoja);
        if (python is not null) return (python, false);
        var py = NoPath(o, "py.exe");
        return py is not null ? (py, true) : (null, false);
    }

    /// <summary>A maior versão (por <see cref="Version"/>, não por texto) entre as subpastas de nome numérico.</summary>
    internal static (string? Versao, string? Pasta) MaiorVersao(string pasta)
    {
        try
        {
            if (!Directory.Exists(pasta)) return (null, null);
            var melhor = Directory.EnumerateDirectories(pasta)
                .Select(d => (Nome: Path.GetFileName(d), Pasta: d, Ok: Version.TryParse(Path.GetFileName(d), out var v), V: v))
                .Where(x => x.Ok)
                .OrderByDescending(x => x.V)
                .FirstOrDefault();
            return melhor.Nome is null ? (null, null) : (melhor.Nome, melhor.Pasta);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (null, null); }
    }

    static string? VersaoDoKit(OpcoesLocalizacao o, string plugin = PluginsInstalados.AnalisePro)
    {
        var json = Path.Combine(o.PastaKit, "01 - PLUGINS", "plugins", plugin, ".codex-plugin", "plugin.json");
        try
        {
            if (!File.Exists(json)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(json));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    /// <summary><c>[plugins."analise@pmvni-kit"]</c> com <c>enabled = true</c> no <c>config.toml</c> do Codex (leitura simples; o editor não interpreta o resto).</summary>
    static bool PluginHabilitado(string codexHome)
    {
        try
        {
            var config = Path.Combine(codexHome, "config.toml");
            if (!File.Exists(config)) return false;
            var naSecao = false;
            foreach (var bruta in File.ReadLines(config))
            {
                var linha = bruta.Trim();
                if (linha.Length == 0 || linha[0] == '#') continue;
                if (linha[0] == '[')
                {
                    var fim = linha.IndexOf(']');
                    var cabecalho = fim > 0 ? linha[1..fim].Replace(" ", "").Replace('\'', '"') : "";
                    naSecao = cabecalho == "plugins.\"analise@pmvni-kit\"";
                    continue;
                }
                if (naSecao && Habilitado.IsMatch(linha)) return true;
                if (DottedHabilitado.IsMatch(linha) || InlineHabilitado.IsMatch(linha)) return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return false;
    }
}
