using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Agentes;

/// <summary>Um plugin do kit instalado: a versão e quando foi instalado ou atualizado (null = data desconhecida).</summary>
public sealed record PluginInstalado(string Versao, DateTimeOffset? Em);

/// <summary>Os plugins do kit PMVNI instalados neste computador (Ajustes › Inteligência artificial, correções 2026-11-15), para o usuário acompanhar
/// as atualizações: no Codex, <c>&lt;CODEX_HOME&gt;\plugins\cache\pmvni-kit\&lt;plugin&gt;\&lt;versão&gt;</c> (a maior versão; a data é a da pasta dela);
/// no Claude Code, o <c>plugins\installed_plugins.json</c> (a entrada <c>&lt;plugin&gt;@…</c> de maior versão, com <c>lastUpdated</c>). Só lê.</summary>
public static class PluginsInstalados
{
    public const string Analise = "analise";
    public const string AnalisePro = "analise-pro";
    public const string AnaliseGoogle = "analise-google";

    /// <summary>O plugin no cache do Codex (pasta do kit <c>pmvni-kit</c>); null = não instalado.</summary>
    public static PluginInstalado? NoCodex(string codexHome, string plugin)
    {
        var (versao, pasta) = Localizacao.MaiorVersao(Path.Combine(codexHome, "plugins", "cache", "pmvni-kit", plugin));
        if (versao is null || pasta is null) return null;
        DateTimeOffset? em = null;
        try { em = new DateTimeOffset(Directory.GetCreationTimeUtc(pasta), TimeSpan.Zero); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new PluginInstalado(versao, em);
    }

    /// <summary>O plugin instalado no Claude Code (pasta <c>.claude</c>); null = não instalado ou pasta desconhecida.</summary>
    public static PluginInstalado? NoClaude(string? pastaClaude, string plugin)
    {
        if (pastaClaude is null) return null;
        try
        {
            var json = Path.Combine(pastaClaude, "plugins", "installed_plugins.json");
            var f = new FileInfo(json);
            return f.Exists && f.Length <= 4 * 1024 * 1024 ? DoRegistroDoClaude(File.ReadAllText(json), plugin) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>Lê o <c>installed_plugins.json</c> do Claude Code (versão 2: <c>plugins</c> → <c>"nome@loja": [ { version, installedAt, lastUpdated } ]</c>;
    /// versão 1: um objeto por chave em vez da lista). Só as chaves <c>plugin@…</c>; vale a maior versão.</summary>
    public static PluginInstalado? DoRegistroDoClaude(string json, string plugin)
    {
        JsonObject? raiz;
        try { raiz = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException) { return null; }
        if (raiz?["plugins"] is not JsonObject plugins) return null;
        PluginInstalado? melhor = null;
        Version? maior = null;
        foreach (var (chave, valor) in plugins)
        {
            if (!chave.StartsWith(plugin + "@", StringComparison.OrdinalIgnoreCase)) continue;
            IEnumerable<JsonNode?> entradas = valor switch { JsonArray a => a, JsonObject o => new JsonNode?[] { o }, _ => Array.Empty<JsonNode?>() };
            foreach (var e in entradas.OfType<JsonObject>())
            {
                if (Texto(e["version"]) is not { } versao || !Version.TryParse(versao, out var v)) continue;
                if (maior is not null && v <= maior) continue;
                maior = v;
                melhor = new PluginInstalado(versao, Data(Texto(e["lastUpdated"])) ?? Data(Texto(e["installedAt"])));
            }
        }
        return melhor;
    }

    /// <summary>Compara com a última versão vista: devolve a anterior quando mudou; null quando é a primeira vez, quando é a mesma ou quando o plugin
    /// sumiu (nada a avisar).</summary>
    public static string? VersaoAnteriorSeMudou(string? ultimaVista, string? atual) =>
        ultimaVista is not null && atual is not null && !string.Equals(ultimaVista, atual, StringComparison.OrdinalIgnoreCase) ? ultimaVista : null;

    static string? Texto(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s.Trim() : null;

    static DateTimeOffset? Data(string? s) =>
        s is not null && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
}
