using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Pmvni.Agentes;

namespace Pmvni.App;

/// <summary>De onde vem a IA das perguntas curtas (revisão, ajustes, despacho), escolhida em Ajustes › Inteligência artificial › Modelos de IA
/// (2026-11-17): o Codex com a conta do app ChatGPT, o Claude Code com a conta do app Claude, ou as APIs com a chave guardada pelo Windows
/// (cartões de "Suas IAs"). As APIs também rodam pelo programa (Codex com <c>CODEX_API_KEY</c>, Claude Code com <c>ANTHROPIC_API_KEY</c>, só no
/// processo filho), como o Novo relatório faz no modo chave.</summary>
public enum FonteIa { Codex, ClaudeCode, OpenAiApi, AnthropicApi }

public static class FontesIa
{
    public static IReadOnlyList<FonteIa> Todas { get; } = [FonteIa.Codex, FonteIa.ClaudeCode, FonteIa.OpenAiApi, FonteIa.AnthropicApi];

    /// <summary>O valor em <c>ajustes.json</c> (<c>ia_revisao_fonte</c> e as chaves de <c>ia_modelos</c>).</summary>
    public static string Valor(FonteIa f) => f switch
    {
        FonteIa.Codex => "codex",
        FonteIa.ClaudeCode => "claude_code",
        FonteIa.OpenAiApi => "openai_api",
        _ => "claude_api",
    };

    public static FonteIa? DoValor(string? v) => Todas.Cast<FonteIa?>().FirstOrDefault(f => Valor(f!.Value) == v);

    public static string Nome(FonteIa f) => f switch
    {
        FonteIa.Codex => "Codex (app ChatGPT)",
        FonteIa.ClaudeCode => "Claude Code (app Claude)",
        FonteIa.OpenAiApi => "OpenAI API",
        _ => "Claude API",
    };

    /// <summary>Nome curto, para a espera ("Consultando a IA (Codex: GPT-6.1 Terra, raciocínio baixo)…").</summary>
    public static string NomeCurto(FonteIa f) => f switch
    {
        FonteIa.Codex => "Codex",
        FonteIa.ClaudeCode => "Claude Code",
        FonteIa.OpenAiApi => "OpenAI API",
        _ => "Claude API",
    };

    /// <summary>O programa que roda a pergunta: o Codex (conta ou chave da OpenAI) ou o Claude Code (conta ou chave da Anthropic).</summary>
    public static bool PeloCodex(FonteIa f) => f is FonteIa.Codex or FonteIa.OpenAiApi;

    /// <summary>A chave guardada (CofreChaves) que a fonte usa: o cartão ChatGPT guarda a da OpenAI, o Claude a da Anthropic.</summary>
    public static Provedor Provedor(FonteIa f) => PeloCodex(f) ? Pmvni.Agentes.Provedor.ChatGpt : Pmvni.Agentes.Provedor.Claude;

    public static bool PorChave(FonteIa f) => f is FonteIa.OpenAiApi or FonteIa.AnthropicApi;
}

/// <summary>Um modelo de uma fonte, com os níveis de raciocínio que ele aceita (na ordem, do menor ao maior) e o padrão dele.</summary>
public sealed record ModeloIa(string Id, string Nome, IReadOnlyList<string> Niveis, string? NivelPadrao);

/// <summary>A lista de modelos de uma fonte e de onde ela veio: <c>app</c> (lida agora do programa), <c>api</c> (lida agora da API),
/// <c>guardada</c> (a última leitura, do cache) ou <c>reserva</c> (a lista conhecida do editor, sem leitura nenhuma).
/// <see cref="ModeloDoApp"/>/<see cref="NivelDoApp"/>: o que vale quando o usuário deixa "Padrão do app".</summary>
public sealed record ListaModelos(IReadOnlyList<ModeloIa> Modelos, string Origem, DateTimeOffset? LidaEm = null, string? ModeloDoApp = null, string? NivelDoApp = null)
{
    public ModeloIa? Modelo(string? id) => string.IsNullOrEmpty(id) ? null : Modelos.FirstOrDefault(m => m.Id == id);
}

/// <summary>O modelo e o nível escolhidos para uma fonte. Vazio = o padrão do app (nada vai na linha de comando).</summary>
public sealed record ModeloEscolhido(string Modelo, string Nivel)
{
    public static readonly ModeloEscolhido DoApp = new("", "");
}

/// <summary>Ler as listas de modelos (programa, API, cache) e os níveis — sem rede nem processo: só texto entra (testável).
/// Portado do seletor do WinStatusBar (ClaudeModelList, CodexModelLevels, ApiModelCatalog), sem depender daquele projeto.</summary>
public static class CatalogoIa
{
    /// <summary>Todos os níveis que algum programa aceita, do menor ao maior.</summary>
    public static IReadOnlyList<string> NiveisConhecidos { get; } = ["none", "minimal", "low", "medium", "high", "xhigh", "max", "ultra"];

    public static string NomeNivel(string nivel) => nivel switch
    {
        "none" => "Sem raciocínio",
        "minimal" => "Mínimo",
        "low" => "Baixo",
        "medium" => "Médio",
        "high" => "Alto",
        "xhigh" => "Muito alto",
        "max" => "Máximo",
        "ultra" => "Ultra",
        "" => "Padrão",
        _ => nivel,
    };

    static readonly Regex IdValido = new(@"^[A-Za-z0-9][A-Za-z0-9._:\-\[\]/]{0,99}$", RegexOptions.CultureInvariant);

    /// <summary>Id que pode ir na linha de comando (nada de espaço, aspas ou opção disfarçada).</summary>
    public static bool ModeloValido(string? id) => id is not null && IdValido.IsMatch(id);

    public static bool NivelValido(string? n) => n is not null && NiveisConhecidos.Contains(n);

    // ---------- Codex (app ChatGPT) ----------

    /// <summary>O catálogo do Codex (<c>models_cache.json</c>, o <c>model_catalog_json</c> do config ou a saída de <c>codex debug models</c>):
    /// <c>{"models":[{"slug","display_name","visibility","supported_reasoning_levels":[{"effort"}],"default_reasoning_level"}]}</c>.
    /// Ficam só os modelos da OpenAI visíveis no app (<c>gpt-…</c>, <c>o…</c>): os ocultos e os que outros apps põem no catálogo
    /// (WinStatusBar, Ollama, atalhos de API) saem.</summary>
    public static IReadOnlyList<ModeloIa> LerCodex(string json)
    {
        var lista = new List<ModeloIa>();
        JsonNode? raiz;
        try { raiz = JsonNode.Parse(json); }
        catch (JsonException) { return lista; }
        if (raiz?["models"] is not JsonArray modelos) return lista;
        foreach (var m in modelos.OfType<JsonObject>())
        {
            if (Texto(m["slug"]) is not { } slug || !ModeloValido(slug)) continue;
            if (Texto(m["visibility"]) == "hide") continue;
            if (!Regex.IsMatch(slug, @"^(gpt-\d|o\d)", RegexOptions.CultureInvariant)) continue;
            if (lista.Any(x => x.Id == slug)) continue;
            var niveis = (m["supported_reasoning_levels"] as JsonArray)?.OfType<JsonObject>().Select(x => Texto(x["effort"]))
                .OfType<string>().Where(NivelValido).Distinct().ToList() ?? [];
            var padrao = Texto(m["default_reasoning_level"]);
            lista.Add(new ModeloIa(slug, Texto(m["display_name"]) is { Length: > 0 } nome ? nome : slug, Ordenar(niveis), padrao is not null && niveis.Contains(padrao) ? padrao : null));
        }
        return lista;
    }

    /// <summary>O <c>config.toml</c> do Codex: o modelo e o nível que o app usa (chaves do nível de cima) e o catálogo próprio, se houver.</summary>
    public static (string? Modelo, string? Nivel, string? Catalogo) LerConfigCodex(string toml)
    {
        var topo = (toml ?? "").Replace("\r\n", "\n").Split("\n[", 2)[0];
        string? Chave(string nome) => Regex.Match(topo, $@"(?m)^\s*{nome}\s*=\s*""([^""]*)""", RegexOptions.CultureInvariant) is { Success: true } m ? m.Groups[1].Value : null;
        return (Chave("model"), Chave("model_reasoning_effort"), Chave("model_catalog_json")?.Replace(@"\\", @"\"));
    }

    // ---------- Claude Code (app Claude) ----------

    /// <summary>A resposta do pedido <c>initialize</c> do Claude Code em stream-json (não chama modelo nenhum):
    /// <c>{"type":"control_response","response":{"response":{"models":[{"value","resolvedModel","displayName","supportedEffortLevels"}]}}}</c>.
    /// "default" não vira opção (é o "Padrão do app"), mas diz qual modelo ele é; apelidos ("opus") viram o modelo de verdade.</summary>
    public static ListaModelos? LerClaudeInitialize(string json, DateTimeOffset? lidaEm = null)
    {
        JsonNode? raiz;
        try { raiz = JsonNode.Parse(json); }
        catch (JsonException) { return null; }
        var corpo = raiz?["response"]?["response"] as JsonObject ?? raiz as JsonObject;
        if (corpo?["models"] is not JsonArray modelos) return null;
        var lista = new List<ModeloIa>();
        string? doApp = null;
        foreach (var m in modelos.OfType<JsonObject>())
        {
            if (Texto(m["value"]) is not { Length: > 0 } valor) continue;
            var id = Texto(m["resolvedModel"]) is { Length: > 0 } r ? r : valor;
            if (!ModeloValido(id)) continue;
            if (valor == "default") { doApp = id; continue; }
            if (lista.Any(x => x.Id == id)) continue;
            var niveis = (m["supportedEffortLevels"] as JsonArray)?.Select(Texto).OfType<string>().Where(NivelValido).Distinct().ToList() ?? [];
            var nome = Texto(m["displayName"]) is { Length: > 0 } d ? d : NomeClaude(id);
            lista.Add(new ModeloIa(id, nome, Ordenar(niveis), niveis.Contains("medium") ? "medium" : niveis.FirstOrDefault()));
        }
        // O "default" sem entrada própria (ex.: o modelo padrão não aparece em mais nenhum lugar) entra na lista também.
        if (doApp is not null && lista.All(x => x.Id != doApp))
        {
            var niveis = (modelos.OfType<JsonObject>().FirstOrDefault(m => Texto(m["value"]) == "default")?["supportedEffortLevels"] as JsonArray)?
                .Select(Texto).OfType<string>().Where(NivelValido).ToList() ?? [];
            lista.Insert(0, new ModeloIa(doApp, NomeClaude(doApp), Ordenar(niveis), niveis.Contains("medium") ? "medium" : niveis.FirstOrDefault()));
        }
        return lista.Count == 0 ? null : new ListaModelos(lista, "app", lidaEm, doApp);
    }

    /// <summary>"claude-opus-5-5" → "Opus 5.5".</summary>
    public static string NomeClaude(string id)
    {
        var m = Regex.Match(id, @"^claude-([a-z]+)-(\d+)(?:-(\d{1,2}))?(?!\d)", RegexOptions.CultureInvariant);
        if (!m.Success) return id;
        var familia = char.ToUpperInvariant(m.Groups[1].Value[0]) + m.Groups[1].Value[1..];
        return familia + " " + m.Groups[2].Value + (m.Groups[3].Success ? "." + m.Groups[3].Value : "");
    }

    // ---------- APIs ----------

    /// <summary><c>GET https://api.openai.com/v1/models</c> (<c>{"data":[{"id"}]}</c>): só os modelos de conversa das duas gerações GPT mais
    /// novas (sem datas, áudio, imagem, embeddings…), mais novos primeiro. Os níveis vêm da tabela conhecida e, para um modelo que ela não
    /// conhece, do catálogo do Codex (<paramref name="codex"/>), sem "ultra" (nível do Codex, não da API).</summary>
    public static IReadOnlyList<ModeloIa> LerOpenAiApi(string json, IReadOnlyList<ModeloIa>? codex = null)
    {
        JsonNode? raiz;
        try { raiz = JsonNode.Parse(json); }
        catch (JsonException) { return []; }
        if (raiz?["data"] is not JsonArray dados) return [];
        string[] fora = ["image", "audio", "tts", "transcribe", "realtime", "search", "embedding", "moderation", "instruct", "codex"];
        var ids = dados.OfType<JsonObject>().Select(m => Texto(m["id"])).OfType<string>().Distinct()
            .Where(id => ModeloValido(id) && Regex.IsMatch(id, @"^gpt-\d", RegexOptions.CultureInvariant)
                && !Regex.IsMatch(id, @"-\d{4}-\d{2}-\d{2}$", RegexOptions.CultureInvariant)
                && !fora.Any(x => id.Contains(x, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var versoes = ids.Select(VersaoGpt).Distinct().OrderDescending().Take(2).ToList();
        return ids.Where(id => versoes.Contains(VersaoGpt(id)))
            .OrderByDescending(VersaoGpt).ThenBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Select(id =>
            {
                var doCodex = codex?.FirstOrDefault(m => m.Id == id);
                var niveis = NiveisOpenAiApi(id) is { Count: > 0 } t ? t : doCodex?.Niveis.Where(n => n != "ultra").ToList() ?? [];
                var padrao = doCodex?.NivelPadrao is { } p && niveis.Contains(p) ? p : niveis.Contains("medium") ? "medium" : niveis.FirstOrDefault();
                return new ModeloIa(id, doCodex?.Nome ?? NomeGpt(id), niveis, padrao);
            }).ToList();
    }

    static Version VersaoGpt(string id) =>
        Regex.Match(id, @"^gpt-(\d+)(?:\.(\d+))?", RegexOptions.CultureInvariant) is { Success: true } m
            ? new Version(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Success ? int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) : 0)
            : new Version(0, 0);

    /// <summary>"gpt-6.1-terra" → "GPT-6.1-Terra".</summary>
    public static string NomeGpt(string id) =>
        string.Join("-", id.Split('-').Select((p, i) => i == 0 ? p.ToUpperInvariant() : p.Length > 0 && char.IsLetter(p[0]) ? char.ToUpperInvariant(p[0]) + p[1..] : p));

    /// <summary>Os níveis conferidos na API da OpenAI (tabela do WinStatusBar).</summary>
    public static IReadOnlyList<string> NiveisOpenAiApi(string id) => id switch
    {
        "gpt-6-astra" or "gpt-6.1-sol" => ["low", "medium", "high", "xhigh", "max"],
        "gpt-6.1-terra" or "gpt-6-sol" or "gpt-6-luna" => ["none", "low", "medium", "high", "xhigh", "max"],
        _ when id.StartsWith("gpt-5.6", StringComparison.Ordinal) => ["none", "low", "medium", "high", "xhigh", "max"],
        "gpt-5.5" or "gpt-5.4" or "gpt-5.4-mini" or "gpt-5.4-nano" or "gpt-5.2" => ["none", "low", "medium", "high", "xhigh"],
        "gpt-5.1" => ["none", "low", "medium", "high"],
        "gpt-5" or "gpt-5-mini" or "gpt-5-nano" => ["minimal", "low", "medium", "high"],
        _ => [],
    };

    /// <summary><c>GET https://api.anthropic.com/v1/models</c>: <c>{"data":[{"id","display_name","capabilities":{"effort":{"supported",
    /// "low":{"supported"},…}}}]}</c>. Os níveis são os que a API diz que o modelo aceita; sem a informação, nenhum (vale o padrão).</summary>
    public static IReadOnlyList<ModeloIa> LerAnthropicApi(string json)
    {
        JsonNode? raiz;
        try { raiz = JsonNode.Parse(json); }
        catch (JsonException) { return []; }
        if (raiz?["data"] is not JsonArray dados) return [];
        var lista = new List<ModeloIa>();
        foreach (var m in dados.OfType<JsonObject>())
        {
            if (Texto(m["id"]) is not { } id || !ModeloValido(id) || lista.Any(x => x.Id == id)) continue;
            var esforco = m["capabilities"]?["effort"] as JsonObject;
            var niveis = esforco is not null && Logico(esforco["supported"])
                ? new[] { "low", "medium", "high", "xhigh", "max" }.Where(n => Logico(esforco[n]?["supported"])).ToList()
                : [];
            lista.Add(new ModeloIa(id, Texto(m["display_name"]) is { Length: > 0 } nome ? nome : NomeClaude(id), niveis, null));
        }
        return lista;
    }

    // ---------- reserva e cache ----------

    static readonly string[] NiveisPadrao = ["low", "medium", "high", "xhigh", "max"];

    /// <summary>A lista conhecida (2026-11), usada só quando nunca houve leitura e o programa ou a API não responde.</summary>
    public static ListaModelos Reserva(FonteIa f) => f switch
    {
        FonteIa.Codex => new([
            new("gpt-6.1-sol", "GPT-6.1-Sol", NiveisPadrao, "medium"),
            new("gpt-6.1-terra", "GPT-6.1 Terra", NiveisPadrao, "low"),
            new("gpt-6-astra", "GPT-6-Astra", NiveisPadrao, "low"),
            new("gpt-6-sol", "GPT-6-Sol", NiveisPadrao, "medium"),
            new("gpt-6-luna", "GPT-6-Luna", NiveisPadrao, "medium"),
        ], "reserva"),
        FonteIa.OpenAiApi => new([
            new("gpt-6.1-sol", "GPT-6.1-Sol", NiveisOpenAiApi("gpt-6.1-sol"), "medium"),
            new("gpt-6.1-terra", "GPT-6.1-Terra", NiveisOpenAiApi("gpt-6.1-terra"), "medium"),
            new("gpt-6-astra", "GPT-6-Astra", NiveisOpenAiApi("gpt-6-astra"), "medium"),
        ], "reserva"),
        _ => new([
            new("claude-opus-5-5", "Opus 5.5", NiveisPadrao, "medium"),
            new("claude-fable-5-2", "Fable 5.2", NiveisPadrao, "medium"),
            new("claude-sonnet-5-5", "Sonnet 5.5", NiveisPadrao, "medium"),
            new("claude-haiku-5-5", "Haiku 5.5", NiveisPadrao, "medium"),
        ], "reserva", null, f == FonteIa.ClaudeCode ? "claude-opus-5-5" : null),
    };

    /// <summary>O arquivo <c>&lt;dados&gt;\cache\modelos-ia.json</c>: as últimas listas lidas (nunca chave nem token).</summary>
    public static string SerializarCache(IReadOnlyDictionary<FonteIa, ListaModelos> listas)
    {
        var o = new JsonObject();
        foreach (var (f, l) in listas.OrderBy(p => p.Key))
            o[FontesIa.Valor(f)] = new JsonObject
            {
                ["lida_em"] = l.LidaEm?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                ["modelo_do_app"] = l.ModeloDoApp,
                ["nivel_do_app"] = l.NivelDoApp,
                ["modelos"] = new JsonArray(l.Modelos.Select(m => (JsonNode)new JsonObject
                {
                    ["id"] = m.Id,
                    ["nome"] = m.Nome,
                    ["niveis"] = new JsonArray(m.Niveis.Select(n => (JsonNode)n).ToArray()),
                    ["padrao"] = m.NivelPadrao,
                }).ToArray()),
            };
        return new JsonObject { ["versao"] = 1, ["listas"] = o }.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Lê o cache; arquivo quebrado ou de outro formato = nada guardado. As listas voltam com a origem <c>guardada</c>.</summary>
    public static Dictionary<FonteIa, ListaModelos> LerCache(string? json)
    {
        var r = new Dictionary<FonteIa, ListaModelos>();
        if (string.IsNullOrWhiteSpace(json)) return r;
        JsonNode? raiz;
        try { raiz = JsonNode.Parse(json); }
        catch (JsonException) { return r; }
        if (raiz?["listas"] is not JsonObject listas) return r;
        foreach (var f in FontesIa.Todas)
        {
            if (listas[FontesIa.Valor(f)] is not JsonObject l || l["modelos"] is not JsonArray ms) continue;
            var modelos = ms.OfType<JsonObject>()
                .Where(m => ModeloValido(Texto(m["id"])))
                .Select(m =>
                {
                    var niveis = (m["niveis"] as JsonArray)?.Select(Texto).OfType<string>().Where(NivelValido).ToList() ?? [];
                    var padrao = Texto(m["padrao"]);
                    return new ModeloIa(Texto(m["id"])!, Texto(m["nome"]) ?? Texto(m["id"])!, Ordenar(niveis), padrao is not null && niveis.Contains(padrao) ? padrao : null);
                }).ToList();
            if (modelos.Count == 0) continue;
            DateTimeOffset? quando = DateTimeOffset.TryParse(Texto(l["lida_em"]), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var q) ? q : null;
            var doApp = Texto(l["modelo_do_app"]);
            var nivelApp = Texto(l["nivel_do_app"]);
            r[f] = new ListaModelos(modelos, "guardada", quando, ModeloValido(doApp) ? doApp : null, NivelValido(nivelApp) ? nivelApp : null);
        }
        return r;
    }

    static IReadOnlyList<string> Ordenar(IEnumerable<string> niveis) => niveis.OrderBy(n => NiveisConhecidos.ToList().IndexOf(n)).ToList();

    static string? Texto(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    static bool Logico(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) && b;
}

/// <summary>Qual fonte responde, com que modelo e nível, e a linha de comando disso (puro: testável).</summary>
public static class EscolhaIa
{
    /// <summary>"Escolha por mim": o comportamento de antes — o Codex quando existe, senão o Claude Code.</summary>
    public const string Automatico = "auto";

    public static IReadOnlyList<string> Opcoes { get; } = [Automatico, .. FontesIa.Todas.Select(FontesIa.Valor)];

    /// <summary>A ordem de "Escolha por mim" e de reserva quando a escolhida não está disponível. As APIs (cobradas por uso) nunca entram
    /// sozinhas: só quando escolhidas.</summary>
    public static IReadOnlyList<FonteIa> OrdemAutomatica { get; } = [FonteIa.Codex, FonteIa.ClaudeCode];

    /// <summary>O padrão de cada fonte. No Codex, o de antes (Terra 6.1, esforço baixo: <see cref="ModeloIaAjuste"/>); nas outras, o padrão do app.</summary>
    public static ModeloEscolhido Padrao(FonteIa f) => f == FonteIa.Codex ? new(ModeloIaAjuste.ModeloPadrao, ModeloIaAjuste.EsforcoPadrao) : ModeloEscolhido.DoApp;

    /// <summary>A escolha antiga (<c>ia_ajuste_modelo</c>/<c>ia_ajuste_esforco</c>, até a 0.4) vira o modelo do Codex. "codex" = o do app.</summary>
    public static ModeloEscolhido Migrar(string? modeloAntigo, string? esforcoAntigo) =>
        modeloAntigo == ModeloIaAjuste.DoCodex ? ModeloEscolhido.DoApp
        : new(CatalogoIa.ModeloValido(modeloAntigo) ? modeloAntigo! : ModeloIaAjuste.ModeloPadrao, CatalogoIa.NivelValido(esforcoAntigo) ? esforcoAntigo! : ModeloIaAjuste.EsforcoPadrao);

    /// <summary>A fonte que vai responder: a escolhida, se está disponível; senão (ou com "Escolha por mim") a primeira disponível de
    /// <see cref="OrdemAutomatica"/>. <c>Substituida</c>: havia uma escolha que não pôde ser usada. Nenhuma disponível: Fonte null.</summary>
    public static (FonteIa? Fonte, bool Substituida) Resolver(string? escolha, Func<FonteIa, bool> disponivel)
    {
        var pedida = FontesIa.DoValor(escolha);
        if (pedida is { } p && disponivel(p)) return (p, false);
        foreach (var f in OrdemAutomatica)
            if (disponivel(f)) return (f, pedida is not null);
        return (null, pedida is not null);
    }

    /// <summary>O nível que vale para o modelo: o pedido, se o modelo o aceita; senão o padrão do modelo; vazio se o modelo não tem níveis
    /// (ou se o pedido era vazio). Sem o modelo na lista (lista ainda não lida), o pedido fica.</summary>
    public static string AjustarNivel(ModeloIa? modelo, string? nivel)
    {
        if (string.IsNullOrEmpty(nivel)) return "";
        if (modelo is null) return CatalogoIa.NivelValido(nivel) ? nivel : "";
        if (modelo.Niveis.Contains(nivel)) return nivel;
        return modelo.NivelPadrao ?? "";
    }

    /// <summary>Os argumentos da escolha na linha de comando: Codex <c>-m &lt;modelo&gt; -c model_reasoning_effort="&lt;nível&gt;"</c>
    /// (sintaxe de <c>codex exec --help</c>); Claude Code <c>--model &lt;modelo&gt; --effort &lt;nível&gt;</c>. Vazio ou inválido não vai
    /// (vale o padrão do app).</summary>
    public static IReadOnlyList<string> Argumentos(FonteIa f, ModeloEscolhido e)
    {
        var args = new List<string>();
        var modelo = CatalogoIa.ModeloValido(e.Modelo) ? e.Modelo : null;
        var nivel = CatalogoIa.NivelValido(e.Nivel) ? e.Nivel : null;
        if (FontesIa.PeloCodex(f))
        {
            if (modelo is not null) args.AddRange(["-m", modelo]);
            if (nivel is not null) args.AddRange(["-c", "model_reasoning_effort=\"" + nivel + "\""]);
        }
        else
        {
            if (modelo is not null) args.AddRange(["--model", modelo]);
            if (nivel is not null && nivel is not ("none" or "minimal" or "ultra")) args.AddRange(["--effort", nivel]);
        }
        return args;
    }

    /// <summary>"Codex: GPT-6.1 Terra, raciocínio baixo" — o que a tela mostra enquanto espera.</summary>
    public static string Descricao(FonteIa f, ModeloEscolhido e, ListaModelos? lista)
    {
        var nome = string.IsNullOrEmpty(e.Modelo) ? "modelo padrão" : lista?.Modelo(e.Modelo)?.Nome ?? e.Modelo;
        var nivel = string.IsNullOrEmpty(e.Nivel) ? "" : ", raciocínio " + CatalogoIa.NomeNivel(e.Nivel).ToLowerInvariant();
        return FontesIa.NomeCurto(f) + ": " + nome + nivel;
    }
}
