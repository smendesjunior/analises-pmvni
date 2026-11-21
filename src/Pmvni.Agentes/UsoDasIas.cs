using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Uma linha do cartão "Consumo" do Monitor: quem executou a etapa (<c>programa</c>, <c>GPT</c>, <c>Claude</c> ou <c>Gemini</c>; "—" se o registro
/// não diz), o modelo, os tokens de entrada e de saída (ou só o <see cref="Total"/>, quando o executor informa apenas ele, como o Codex), o cache lido, a
/// duração e a variação, em pontos percentuais, da cota de 5 h e da semanal durante a etapa. null = o kit não informou (o Monitor mostra "—"); nada é
/// estimado aqui.</summary>
public sealed record UsoEtapa(string Etapa, string Executor, string? Modelo, long? Entrada, long? Saida, long? CacheLido, TimeSpan? Duracao,
    double? Cota5h, double? CotaSemana)
{
    public long? Total { get; init; }

    /// <summary>A situação da execução no kit (<c>EXECUTED</c>, <c>FAILED</c>, <c>FAILED_QUOTA</c>…), se informada.</summary>
    public string? Situacao { get; init; }
}

/// <summary>Os textos do cartão "Consumo": números curtos em português ("89,2 mil", "1,5 mi"), "—" no que não foi informado.</summary>
public static class TextoConsumo
{
    public const string Desconhecido = "—";
    static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    public static string Numero(long? n) => n switch
    {
        null => Desconhecido,
        < 1000 => n.Value.ToString("N0", Br),
        < 1_000_000 => (n.Value / 1000d).ToString("0.#", Br) + " mil",
        _ => (n.Value / 1_000_000d).ToString("0.#", Br) + " mi",
    };

    /// <summary>"entrada / saída"; só o total quando o executor não separa (o Codex); "—" sem nada (as etapas de programa).</summary>
    public static string Tokens(UsoEtapa u) =>
        u.Entrada is not null || u.Saida is not null ? Numero(u.Entrada) + " / " + Numero(u.Saida)
        : u.Total is not null ? Numero(u.Total) + " (total)"
        : Desconhecido;

    /// <summary>A variação da cota de 5 h e da semanal, em pontos percentuais: "+7% · +1,5%".</summary>
    public static string Cota(UsoEtapa u)
    {
        if (u.Cota5h is null && u.CotaSemana is null) return Desconhecido;
        static string Pontos(double? v) => v is { } x ? (x >= 0 ? "+" : "") + x.ToString("0.#", Br) + "%" : Desconhecido;
        return Pontos(u.Cota5h) + " · " + Pontos(u.CotaSemana);
    }
}

/// <summary>O consumo de cada etapa, lido do <c>uso-das-IAs.json</c> que o <c>uso_ias.py</c> do kit grava na pasta de controle (análise pro e análise Google) e
/// do <c>engine_state.json</c> (as etapas que o relatório de uso ainda não traz, as de programa — <c>{"executor": "programa"}</c> — e a rota ChatGPT, que
/// não tem relatório de uso). E o histórico das durações de todos os processos, para a <see cref="PrevisaoTermino"/>.</summary>
public static class UsoDasIas
{
    public const string Arquivo = "uso-das-IAs.json";
    public const string Programa = "programa";

    /// <param name="desdeUtc">Arquivos gravados antes disto (a análise anterior) não contam.</param>
    /// <param name="ordemDoKit">A sequência das etapas que o kit define (<see cref="OrdemDasEtapas.Ler"/>); null = a ordem padrão (kit 3.x). As linhas saem
    /// nessa ordem.</param>
    public static IReadOnlyList<UsoEtapa> Ler(string pastaControle, DateTime desdeUtc = default, OrdemDasEtapas? ordemDoKit = null)
    {
        var linhas = new List<UsoEtapa>();
        var etapas = (ordemDoKit ?? OrdemDasEtapas.Padrao).Todas.ToList();
        if (ArquivoJsonDoKit.Ler(Path.Combine(pastaControle, Arquivo), desdeUtc) is { ValueKind: JsonValueKind.Object } uso
            && uso.TryGetProperty("execucoes", out var execucoes) && execucoes.ValueKind == JsonValueKind.Array)
            foreach (var x in execucoes.EnumerateArray())
            {
                if (x.ValueKind != JsonValueKind.Object || ProgressoLocal.Texto(x, "etapa") is not { } etapa || !etapas.Contains(etapa)) continue;
                var modelo = PrimeiroModelo(x) ?? ProgressoLocal.Texto(x, "modelo_pedido");
                var executor = Executor(ProgressoLocal.Texto(x, "executor"), ProgressoLocal.Texto(x, "ia"), modelo);
                JsonElement? detalhe = x.TryGetProperty("tokens_detalhe", out var d) && d.ValueKind == JsonValueKind.Object ? d : null;
                double? cota5h = null, cotaSemana = null;
                if (!(x.TryGetProperty("cota_fora", out var fora) && fora.ValueKind == JsonValueKind.True)
                    && x.TryGetProperty("cota_usada_pp", out var cota) && cota.ValueKind == JsonValueKind.Object)
                {
                    cota5h = ProgressoLocal.Numero(cota, "5 h");
                    cotaSemana = ProgressoLocal.Numero(cota, "semana");
                }
                var duracao = ProgressoLocal.Numero(x, "duracao_s") is { } s and > 0 ? TimeSpan.FromSeconds(s) : (TimeSpan?)null;
                linhas.Add(new UsoEtapa(etapa, executor, modelo, Inteiro(detalhe, "entrada"), Inteiro(detalhe, "saida"),
                        Inteiro(x, "cache_lido") ?? Inteiro(detalhe, "cache_lido"), duracao, cota5h, cotaSemana)
                    { Total = Inteiro(x, "tokens"), Situacao = ProgressoLocal.Texto(x, "situacao") });
            }

        // As etapas do estado que o relatório de uso não traz: as de programa (nunca passam por IA), a rota ChatGPT (sem relatório de uso) e a etapa que
        // terminou depois da última geração do relatório.
        if (ArquivoJsonDoKit.Ler(Path.Combine(pastaControle, "engine_state.json"), desdeUtc) is { ValueKind: JsonValueKind.Object } estado
            && estado.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Object)
            foreach (var etapa in etapas)
            {
                if (!stages.TryGetProperty(etapa, out var r) || r.ValueKind != JsonValueKind.Object || linhas.Any(l => l.Etapa == etapa)) continue;
                var executorDoRegistro = ProgressoLocal.Texto(r, "executor");
                var executor = Executor(executorDoRegistro, ProgressoLocal.Texto(r, "engine"),
                    ProgressoLocal.Texto(r, "requested_model") ?? ProgressoLocal.Texto(r, "modelo_observado"));
                var modelo = executor == Programa ? null
                    : ProgressoLocal.Texto(r, "modelo_observado") ?? ProgressoLocal.Texto(r, "requested_model") ?? executorDoRegistro;
                var inicio = ProgressoLocal.Instante(r, "started");
                var fim = ProgressoLocal.Instante(r, "ended");
                var duracao = inicio is { } a && fim is { } b && b > a ? b - a : (TimeSpan?)null;
                linhas.Add(new UsoEtapa(etapa, executor, modelo, null, null, null, duracao, null, null) { Situacao = ProgressoLocal.Texto(r, "status") });
            }

        // Na ordem das etapas; dentro de uma etapa, as tentativas na ordem do relatório.
        return linhas.Select((l, i) => (l, i)).OrderBy(p => etapas.IndexOf(p.l.Etapa)).ThenBy(p => p.i).Select(p => p.l).ToList();
    }

    /// <summary>Quem executou: <c>programa</c> quando o registro diz <c>"executor": "programa"</c>; senão pelo motor ou pelo modelo: codex/gpt → <c>GPT</c>,
    /// claude → <c>Claude</c>, gemini/agy/antigravity → <c>Gemini</c>; "—" se nada disso aparece.</summary>
    public static string Executor(string? executor, string? engine, string? modelo)
    {
        if (string.Equals(executor?.Trim(), Programa, StringComparison.OrdinalIgnoreCase)
            || string.Equals(engine?.Trim(), Programa, StringComparison.OrdinalIgnoreCase)) return Programa;
        var texto = string.Join(' ', executor, engine, modelo).ToLowerInvariant();
        if (texto.Contains("claude") || texto.Contains("opus") || texto.Contains("sonnet")) return "Claude";
        if (texto.Contains("gemini") || texto.Contains("antigravity") || string.Equals(engine?.Trim(), "agy", StringComparison.OrdinalIgnoreCase)
            || string.Equals(executor?.Trim(), "agy", StringComparison.OrdinalIgnoreCase)) return "Gemini";
        if (texto.Contains("codex") || texto.Contains("gpt")) return "GPT";
        return "—";
    }

    static readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyList<DuracaoEtapa>>>> historicos = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>O <see cref="Historico"/> lido uma vez por sessão do Editor (por pasta de processos), fora da thread de quem chama.</summary>
    public static Task<IReadOnlyList<DuracaoEtapa>> HistoricoDaSessaoAsync(string pastaProcessos, OrdemDasEtapas? ordemDoKit = null) =>
        historicos.GetOrAdd(pastaProcessos ?? "", raiz => new Lazy<Task<IReadOnlyList<DuracaoEtapa>>>(() => Task.Run(() => Historico(raiz, ordemDoKit)))).Value;

    /// <summary>As durações das etapas executadas em todas as análises sob a pasta de processos
    /// (<c>&lt;raiz&gt;\…\&lt;processo&gt;\.pmvni-ai\&lt;analise|analise-pro|analise-google&gt;</c>, até três níveis de pasta), por <see cref="DuracoesDaPasta"/>.</summary>
    public static IReadOnlyList<DuracaoEtapa> Historico(string pastaProcessos, OrdemDasEtapas? ordemDoKit = null)
    {
        var saida = new List<DuracaoEtapa>();
        if (string.IsNullOrWhiteSpace(pastaProcessos) || !Directory.Exists(pastaProcessos)) return saida;
        // A pasta .pmvni-ai pode estar oculta: nada é pulado pelos atributos; a profundidade limita a busca (e qualquer atalho circular).
        var opcoes = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = 0 };
        IEnumerable<string> pastas;
        try { pastas = Directory.EnumerateDirectories(pastaProcessos, ".pmvni-ai", opcoes).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return saida; }
        foreach (var pmvni in pastas)
            foreach (var plugin in new[] { "analise", "analise-pro", "analise-google" })
                saida.AddRange(DuracoesDaPasta(Path.Combine(pmvni, plugin), ordemDoKit));
        return saida;
    }

    /// <summary>Uma execução de etapa achada numa das fontes: a pasta da execução (<c>run</c>/<c>execucao</c>), se o kit a informa, e a duração.</summary>
    readonly record struct Execucao(string Etapa, string? Pasta, TimeSpan Duracao);

    /// <summary>As durações das etapas executadas (<c>EXECUTED</c>) numa pasta de controle, de duas fontes (<paramref name="ordemDoKit"/>: a sequência das
    /// etapas do kit, que o kit 4.0 grava com <c>started</c> em todas, preferido ao fim da etapa anterior; null = a ordem padrão):
    /// <list type="bullet">
    /// <item>o <c>engine_state.json</c>, nas três rotas e também nas etapas de programa: as etapas atuais (<c>stages</c>) e as substituídas (<c>history[]</c>,
    /// nos dois formatos do kit: <c>{"replaced": {etapa: registro}}</c> no analise, <c>{etapa: registro}</c> no análise pro e no Google). Duração =
    /// <c>ended − started</c>; sem <c>started</c> (o analise só grava <c>ended</c>), o intervalo desde o <c>ended</c> da etapa anterior do mesmo quadro;</item>
    /// <item>o <c>uso-das-IAs.json</c> (<c>duracao_s</c>), que traz as tentativas de cada etapa.</item>
    /// </list>
    /// A mesma execução nas duas fontes conta uma vez só: mesma pasta de execução ou, sem ela, mesma etapa com durações a menos de 1 s uma da outra. Arquivo
    /// ilegível ou pela metade é ignorado.</summary>
    public static IReadOnlyList<DuracaoEtapa> DuracoesDaPasta(string pastaControle, OrdemDasEtapas? ordemDoKit = null)
    {
        if (ProgressoLocal.ProvedorDaPasta(pastaControle) is not { } provedor) return [];
        var etapas = (ordemDoKit ?? OrdemDasEtapas.Padrao).Todas.ToList();
        var achadas = new List<Execucao>();
        // Mesma execução: mesma pasta; sem a pasta numa das duas (ou, contra o estado, com pastas diferentes), mesma etapa e duração a menos de 1 s.
        static bool Mesma(Execucao a, Execucao x, bool pelaDuracao) => a.Etapa == x.Etapa
            && (a.Pasta is not null && x.Pasta is not null && string.Equals(a.Pasta, x.Pasta, StringComparison.OrdinalIgnoreCase)
                || (pelaDuracao || a.Pasta is null || x.Pasta is null) && (a.Duracao - x.Duracao).Duration() < TimeSpan.FromSeconds(1));

        if (LerJson(Path.Combine(pastaControle, "engine_state.json")) is { ValueKind: JsonValueKind.Object } estado)
        {
            var quadros = new List<JsonElement>();
            if (estado.TryGetProperty("history", out var historia) && historia.ValueKind == JsonValueKind.Array)
                foreach (var foto in historia.EnumerateArray())
                    if (foto.ValueKind == JsonValueKind.Object)
                        quadros.Add(foto.TryGetProperty("replaced", out var substituidas) && substituidas.ValueKind == JsonValueKind.Object ? substituidas : foto);
            if (estado.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Object) quadros.Add(stages);
            foreach (var quadro in quadros)
            {
                DateTimeOffset? fimAnterior = null;
                foreach (var etapa in etapas)
                {
                    if (!quadro.TryGetProperty(etapa, out var r) || r.ValueKind != JsonValueKind.Object) continue;
                    var inicio = ProgressoLocal.Instante(r, "started") ?? fimAnterior;
                    var fim = ProgressoLocal.Instante(r, "ended");
                    if (fim is not null) fimAnterior = fim;
                    if (ProgressoLocal.Texto(r, "status") != "EXECUTED" || inicio is not { } partida || fim is not { } termino || termino <= partida) continue;
                    var x = new Execucao(etapa, ProgressoLocal.Texto(r, "run"), termino - partida);
                    if (!achadas.Any(a => Mesma(a, x, false))) achadas.Add(x);     // o análise pro repete a mesma etapa em vários quadros do histórico
                }
            }
        }

        var doEstado = achadas.Count;
        if (LerJson(Path.Combine(pastaControle, Arquivo)) is { ValueKind: JsonValueKind.Object } uso
            && uso.TryGetProperty("execucoes", out var execucoes) && execucoes.ValueKind == JsonValueKind.Array)
            foreach (var x in execucoes.EnumerateArray())
                if (x.ValueKind == JsonValueKind.Object && ProgressoLocal.Texto(x, "etapa") is { } etapa && etapas.Contains(etapa)
                    && ProgressoLocal.Texto(x, "situacao") == "EXECUTED" && ProgressoLocal.Numero(x, "duracao_s") is { } s and > 0)
                {
                    var execucao = new Execucao(etapa, ProgressoLocal.Texto(x, "execucao"), TimeSpan.FromSeconds(s));
                    // Só contra o estado, e pela duração mesmo com pastas diferentes: a tentativa final de uma etapa com troca de rota tem pasta própria no
                    // relatório de uso. As linhas do relatório já são execuções distintas entre si (o uso_ias.py junta por pasta).
                    if (!achadas.Take(doEstado).Any(a => Mesma(a, execucao, true))) achadas.Add(execucao);
                }

        return achadas.Select(x => new DuracaoEtapa(x.Etapa, provedor, x.Duracao)).ToList();
    }

    static JsonElement? LerJson(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo)) return null;
            using var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var doc = JsonDocument.Parse(fs);
            return doc.RootElement.Clone();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    static string? PrimeiroModelo(JsonElement x) =>
        x.TryGetProperty("modelos_confirmados", out var lista) && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray().Where(m => m.ValueKind == JsonValueKind.String).Select(m => m.GetString()).FirstOrDefault(m => !string.IsNullOrWhiteSpace(m))
            : null;

    static long? Inteiro(JsonElement? o, string nome) =>
        o is { } e && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) && n >= 0
            ? (long)n : null;
}
