using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pmvni.Agentes;

/// <summary>O que "Testar" mostra em Ajustes: a linha de situação do provedor e os avisos.</summary>
public sealed record SituacaoProvedor(string Linha, IReadOnlyList<string> Avisos)
{
    /// <summary>Gemini, modo conta: o login do Antigravity respondeu com modelos (true), sem modelos (false) ou não foi possível saber (null).</summary>
    public bool? ContaConectada { get; init; }
}

/// <summary>"Testar" (desenho §12.8): só comandos locais de cada programa (<c>--version</c>, <c>login status</c>, <c>auth status</c>), 15 s cada, nunca um pedido ao
/// modelo (não gasta cota). A sessão só informa: login desconhecido não bloqueia a análise.</summary>
public static class Diagnostico
{
    /// <summary>15 s por comando; mexe só em teste.</summary>
    internal static TimeSpan LimiteComando { get; set; } = TimeSpan.FromSeconds(15);

    static readonly Regex Codex = new(@"codex-cli\s+(\d+\.\d+\.\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly Regex ClaudeAntes = new(@"^\s*(\d+\.\d+\.\d+)\s*\(Claude Code\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Multiline);
    static readonly Regex ClaudeDepois = new(@"Claude Code\s+(\d+\.\d+\.\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly Regex Python = new(@"Python\s+(\d+\.\d+(?:\.\d+)?)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string? VersaoCodex(string saida) => Codex.Match(saida) is { Success: true } m ? m.Groups[1].Value : null;

    public static string? VersaoClaude(string saida) =>
        ClaudeAntes.Match(saida) is { Success: true } a ? a.Groups[1].Value
        : ClaudeDepois.Match(saida) is { Success: true } d ? d.Groups[1].Value
        : null;

    public static string? VersaoPython(string saida) => Python.Match(saida) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>A primeira linha não vazia (até 200 caracteres), para "não verificado: …".</summary>
    internal static string PrimeiraLinha(string saida)
    {
        var linha = saida.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (linha is null) return "sem resposta";
        return linha.Length > 200 ? linha[..200] : linha;
    }

    public static string SessaoCodex(int codigo, string saida)
    {
        var primeira = PrimeiraLinha(saida);
        if (codigo == -1) return "não verificado: " + primeira;
        if (saida.Contains("Not logged in", StringComparison.OrdinalIgnoreCase)) return "não conectado";
        if (codigo == 0 && primeira.Contains("Logged in", StringComparison.OrdinalIgnoreCase))
            return primeira.Contains("API key", StringComparison.OrdinalIgnoreCase) ? "conectado com chave de API" : "conectado com a conta do ChatGPT";
        if (string.IsNullOrWhiteSpace(saida)) return "não conectado";
        return "não verificado: " + primeira;
    }

    public static string SessaoClaude(int codigo, string saida)
    {
        if (codigo != -1 && LoggedIn(saida) is { } logado)
            return logado ? "conectado com a conta do Claude" : "não conectado";
        return "não verificado: " + PrimeiraLinha(saida);
    }

    /// <summary>O <c>loggedIn</c> do JSON de <c>claude auth status</c> (que sai com 1 quando não há login, mas imprime o JSON do mesmo jeito).</summary>
    static bool? LoggedIn(string saida)
    {
        var inicio = saida.IndexOf('{');
        if (inicio < 0) return null;
        try
        {
            using var doc = JsonDocument.Parse(saida[inicio..]);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("loggedIn", out var v)
                   && v.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? v.GetBoolean()
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <param name="perfilGemini">Gemini, modo chave: o perfil de API escolhido (null = ainda não escolhido).</param>
    /// <param name="chaveGemini">Gemini, modo chave: a chave lida do cofre por quem chama, só para a conferência na API do Google; nunca é mostrada.</param>
    public static async Task<SituacaoProvedor> TestarAsync(Provedor p, Pecas pecas, ModoAcesso modo, bool chaveGuardada, CancellationToken ct,
        PerfilGemini? perfilGemini = null, string? chaveGemini = null, Func<PerfilGemini, IReadOnlyList<EtapaGemini>?>? etapasGemini = null)
    {
        if (p == Provedor.Gemini) return await DiagnosticoGemini.TestarAsync(pecas, modo, perfilGemini, modo == ModoAcesso.Chave ? chaveGemini : null, ct, etapasGemini);
        var avisos = new List<string>();
        var linha = p == Provedor.ChatGpt ? await ChatGptAsync(pecas, ct) : await ClaudeAsync(pecas, avisos, ct);
        if (modo == ModoAcesso.Chave && chaveGuardada)
            avisos.Add("Chave guardada; ela só é conferida pelo provedor na primeira análise.");
        return new SituacaoProvedor(linha, avisos);
    }

    /// <summary>Sempre sem chaves herdadas: o diagnóstico olha o login do programa, não a chave.</summary>
    static Task<(int Codigo, string Saida)> Rodar((string Exe, IReadOnlyList<string> Args) linha, Provedor p, CancellationToken ct) =>
        ExecucaoProcesso.RodarAsync(linha.Exe, linha.Args, Path.GetDirectoryName(linha.Exe) is { Length: > 0 } pasta ? pasta : Environment.CurrentDirectory,
            env => AmbienteFilho.Aplicar(env, p, ModoAcesso.Conta, null), LimiteComando, ct);

    static async Task<string> ChatGptAsync(Pecas pecas, CancellationToken ct)
    {
        if (pecas.Codex is null) return "Codex não encontrado";
        var versao = await Rodar(LinhasDeComando.CodexVersao(pecas.Codex), Provedor.ChatGpt, ct);
        var numero = versao.Codigo == 0 ? VersaoCodex(versao.Saida) : null;
        if (numero is null) return "Codex não verificado: " + PrimeiraLinha(versao.Saida);

        var login = await Rodar(LinhasDeComando.CodexLoginStatus(pecas.Codex), Provedor.ChatGpt, ct);
        var plugin = pecas.PluginAnaliseHabilitado
            ? "plugin analise " + (pecas.VersaoPluginAnalise is null ? "" : pecas.VersaoPluginAnalise + " ") + "habilitado"
            : "plugin analise não habilitado no Codex";
        return string.Join(" · ", "Codex " + numero, SessaoCodex(login.Codigo, login.Saida), plugin);
    }

    static async Task<string> ClaudeAsync(Pecas pecas, List<string> avisos, CancellationToken ct)
    {
        var partes = new List<string>();
        if (pecas.Claude is null) partes.Add("Claude Code não encontrado");
        else
        {
            var versao = await Rodar(LinhasDeComando.ClaudeVersao(pecas.Claude), Provedor.Claude, ct);
            var numero = versao.Codigo == 0 ? VersaoClaude(versao.Saida) : null;
            if (numero is null) partes.Add("Claude Code não verificado: " + PrimeiraLinha(versao.Saida));
            else
            {
                partes.Add("Claude Code " + numero);
                var sessao = await Rodar(LinhasDeComando.ClaudeAuthStatus(pecas.Claude), Provedor.Claude, ct);
                partes.Add(SessaoClaude(sessao.Codigo, sessao.Saida));
            }
        }

        if (pecas.ProRuntime is null) partes.Add("Análise — rota Claude não instalada no Codex");
        else
        {
            partes.Add($"Análise — rota Claude {pecas.VersaoAnalisePro} (kit: {pecas.VersaoAnaliseProKit ?? "não encontrado"})");
            if (Version.TryParse(pecas.VersaoAnaliseProKit, out var doKit) && Version.TryParse(pecas.VersaoAnalisePro, out var instalada) && doKit > instalada)
                avisos.Add($"O kit tem o plugin analise {pecas.VersaoAnaliseProKit}; instalado {pecas.VersaoAnalisePro}. Atualize pelo INSTALAR_KIT_PMVNI.cmd.");
        }

        if (pecas.Python is null) partes.Add("Python não encontrado");
        else
        {
            var py = await Rodar(LinhasDeComando.PythonVersao(pecas.Python, pecas.PythonPeloLauncher), Provedor.Claude, ct);
            var numero = py.Codigo == 0 ? VersaoPython(py.Saida) : null;
            partes.Add(numero is null ? "Python não verificado: " + PrimeiraLinha(py.Saida) : "Python " + numero);
        }
        return string.Join(" · ", partes);
    }
}
