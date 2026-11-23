using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Uma linha do cartão "Consumo" do Monitor: quem executou a etapa (<c>programa</c>, <c>GPT</c>, <c>Claude</c> ou <c>Gemini</c>; "—" se o registro
/// não diz), o modelo, os tokens de entrada e de saída (ou só o <see cref="Total"/>, quando o executor informa apenas ele, como o Codex), o cache lido, a
/// duração e a variação, em pontos percentuais, da cota de 5 h e da semanal durante a etapa. null = métrica ausente (oculta no Monitor); nada é
/// estimado aqui.</summary>
public sealed record UsoEtapa(string Etapa, string Executor, string? Modelo, long? Entrada, long? Saida, long? CacheLido, TimeSpan? Duracao,
    double? Cota5h, double? CotaSemana)
{
    public double? ValorEquivalenteUsd { get; init; }
    internal string? PastaExecucao { get; init; }
    public long? Total { get; init; }
    public long? CacheGravado { get; init; }
    internal bool ForaDaCota { get; init; }
    public string? Plano { get; init; }
    public double? CpuMediaMhz { get; init; }
    public double? CpuPicoMhz { get; init; }
    public long? RamPicoBytes { get; init; }
    public double? Usada5h { get; init; }
    public double? UsadaSemana { get; init; }

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

    public static IEnumerable<(string Rotulo, string Valor)> Metricas(UsoEtapa u)
    {
        if (u.Entrada.HasValue) yield return ("Tokens de entrada", Numero(u.Entrada));
        if (u.Saida.HasValue) yield return ("Tokens de saída", Numero(u.Saida));
        if (u.Total.HasValue && (!u.Entrada.HasValue || !u.Saida.HasValue)) yield return ("Tokens totais", Numero(u.Total));
        if (u.CacheLido.HasValue) yield return ("Cache lido", Numero(u.CacheLido));
        if (u.ValorEquivalenteUsd is { } usd) yield return ("Custo equivalente (USD)", usd.ToString("0.00####", Br));
        if (u.Cota5h is { } h) yield return ("Variação da cota de 5 h", h.ToString("0.#", Br) + " p.p.");
        if (u.CotaSemana is { } w) yield return ("Variação da cota semanal", w.ToString("0.#", Br) + " p.p.");
    }

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
/// do <c>engine_state.json</c> e da sessão Codex vinculada pelo kit. E o histórico das durações de todos os processos, para a <see cref="PrevisaoTermino"/>.</summary>
public static class UsoDasIas
{
    public const string Arquivo = "uso-das-IAs.json";
    public const string Programa = "programa";

    /// <param name="desdeUtc">Arquivos gravados antes disto (a análise anterior) não contam.</param>
    /// <param name="ordemDoKit">A sequência das etapas que o kit define (<see cref="OrdemDasEtapas.Ler"/>); null = a ordem padrão (kit 3.x). As linhas saem
    /// nessa ordem.</param>
    public static IReadOnlyList<UsoEtapa> Ler(string pastaControle, DateTime desdeUtc = default, OrdemDasEtapas? ordemDoKit = null,
        ConsumoSessaoCodex? sessoesCodex = null, DateTimeOffset? agoraEmExecucao = null)
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
                var foraDaCota = x.TryGetProperty("cota_fora", out var fora) && fora.ValueKind == JsonValueKind.True;
                if (!foraDaCota && executor != "GPT" && executor != Programa
                    && x.TryGetProperty("cota_usada_pp", out var cota) && cota.ValueKind == JsonValueKind.Object)
                {
                    cota5h = ProgressoLocal.Numero(cota, "5 h");
                    cotaSemana = ProgressoLocal.Numero(cota, "semana");
                }
                var duracao = ProgressoLocal.Numero(x, "duracao_s") is { } s and > 0 ? TimeSpan.FromSeconds(s) : (TimeSpan?)null;
                linhas.Add(new UsoEtapa(etapa, executor, modelo, Inteiro(detalhe, "entrada"), Inteiro(detalhe, "saida"),
                        Inteiro(x, "cache_lido") ?? Inteiro(detalhe, "cache_lido"), duracao, cota5h, cotaSemana)
                    { Total = Inteiro(x, "tokens"), CacheGravado = Inteiro(detalhe, "cache_gravado"), ForaDaCota = foraDaCota, ValorEquivalenteUsd = ProgressoLocal.Numero(x, "valor_equivalente_usd"),
                      PastaExecucao = ProgressoLocal.Texto(x, "execucao"), Situacao = ProgressoLocal.Texto(x, "situacao") });
            }

        // O relatório pode conter só o preparo. Complete os campos da mesma tentativa com os registros do executor.
        if (ArquivoJsonDoKit.Ler(Path.Combine(pastaControle, "engine_state.json"), desdeUtc) is { ValueKind: JsonValueKind.Object } estado
            && estado.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Object)
            foreach (var etapa in etapas)
            {
                var registro = Objeto(stages, etapa);
                var sessao = Objeto(Objeto(estado, "sessao_codex"), etapa);
                if (registro is null && sessao is null) continue;
                var r = registro ?? sessao!.Value;
                var usoEtapa = Objeto(r, "usage");
                var tokens = Objeto(usoEtapa, "tokens");
                var daSessao = sessoesCodex?.LerEtapa(pastaControle, etapa, sessao, desdeUtc);
                var modelo = ProgressoLocal.Texto(r, "modelo_observado") ?? ProgressoLocal.Texto(r, "requested_model")
                    ?? (sessao is { } sess ? ProgressoLocal.Texto(sess, "modelo") : null);
                var executorDoRegistro = ProgressoLocal.Texto(r, "executor");
                var executor = Executor(executorDoRegistro, ProgressoLocal.Texto(r, "engine") ?? (sessao.HasValue ? "codex" : null), modelo);
                var inicio = ProgressoLocal.Instante(r, "started") ?? (sessao is { } ativa ? ProgressoLocal.Instante(ativa, "inicio") : null);
                var fim = ProgressoLocal.Instante(r, "ended");
                if (fim is null && ProgressoLocal.Texto(r, "status") == "RUNNING")
                    fim = agoraEmExecucao;
                var duracao = ProgressoLocal.Numero(r, "duracao_s") is { } segundos and >= 0 ? TimeSpan.FromSeconds(segundos)
                    : inicio is { } a && fim is { } b && b >= a ? b - a : (TimeSpan?)null;
                var pasta = ProgressoLocal.Texto(r, "run");
                var indice = linhas.FindLastIndex(l => l.Etapa == etapa && (l.PastaExecucao == pasta || l.PastaExecucao is null));
                var anterior = indice >= 0 ? linhas[indice] : null;
                // Uma troca de rota pode ter várias pastas filhas no relatório e uma pasta agregadora no estado.
                if (anterior is null && linhas.Any(l => l.Etapa == etapa) &&
                    (pasta is null || r.TryGetProperty("route_attempts", out var tentativas) && tentativas.ValueKind == JsonValueKind.Array))
                    continue;
                var cotaDaConta = anterior?.ForaDaCota != true && ProgressoLocal.Texto(r, "route") != "api" && executor != Programa;
                var amostraAntes = Objeto(r, "quota_before");
                var amostraDepois = Objeto(r, "quota_after");
                var antes = Objeto(amostraAntes, "windows");
                var depois = Objeto(amostraDepois, "windows");
                var contaAntes = Identificador(amostraAntes, "account_id");
                var contaDepois = Identificador(amostraDepois, "account_id");
                var usaLb = ProgressoLocal.Texto(r, "route") == "codex-lb" || Identificador(amostraAntes, "origem") == "codex-lb"
                    || Identificador(amostraDepois, "origem") == "codex-lb";
                var contaDaSessao = Identificador(sessao, "account_id") ?? daSessao?.Conta;
                var exigeConta = usaLb || executor == "GPT" || anterior?.Executor == "GPT";
                var mesmoPar = contaAntes == contaDepois && (!exigeConta || contaAntes is not null && contaAntes == contaDaSessao);
                double? Pontos(string chave, string alternativa, double? registrado, double? observado)
                {
                    if (!cotaDaConta) return null;
                    if (antes is not null || depois is not null)
                        return (mesmoPar ? Variacao(antes, depois, chave, alternativa) : null) ?? observado;
                    return observado ?? registrado;
                }
                var linha = new UsoEtapa(etapa, anterior?.Executor is { } quem && quem != TextoConsumo.Desconhecido ? quem : executor,
                    anterior?.Modelo ?? (executor == Programa ? null : modelo ?? executorDoRegistro),
                    anterior?.Entrada ?? Inteiro(tokens, "entrada") ?? daSessao?.Entrada,
                    anterior?.Saida ?? Inteiro(tokens, "saida") ?? daSessao?.Saida,
                    anterior?.CacheLido ?? Inteiro(usoEtapa, "cache_lido") ?? Inteiro(tokens, "cache_lido") ?? daSessao?.Cache,
                    anterior?.Duracao ?? duracao,
                    Pontos("primary", "five_hour", anterior?.Cota5h, daSessao?.Cota5h),
                    Pontos("secondary", "seven_day", anterior?.CotaSemana, daSessao?.CotaSemana))
                {
                    Plano = daSessao?.Plano,
                    CacheGravado = anterior?.CacheGravado ?? Inteiro(tokens, "cache_gravado"),
                    Usada5h = cotaDaConta ? PercentualUsado(mesmoPar ? depois : null, "primary", "five_hour") ?? daSessao?.Usada5h : null,
                    UsadaSemana = cotaDaConta ? PercentualUsado(mesmoPar ? depois : null, "secondary", "seven_day") ?? daSessao?.UsadaSemana : null,
                    Total = anterior?.Total ?? Inteiro(usoEtapa, "total_tokens") ?? daSessao?.Total,
                    ValorEquivalenteUsd = anterior?.ValorEquivalenteUsd ?? (usoEtapa is { } u ? ProgressoLocal.Numero(u, "valor_equivalente_usd") : null),
                    Situacao = anterior?.Situacao ?? ProgressoLocal.Texto(r, "status"),
                    PastaExecucao = anterior?.PastaExecucao ?? pasta
                };
                if (indice >= 0) linhas[indice] = linha;
                else linhas.Add(linha);
            }

        // Na ordem das etapas; dentro de uma etapa, as tentativas na ordem do relatório.
        for (var i = 0; i < linhas.Count; i++)
        {
            var linha = linhas[i];
            if (linha.Executor != Programa || string.IsNullOrWhiteSpace(linha.PastaExecucao)) continue;
            var caminho = Path.Combine(linha.PastaExecucao, "recursos.json");
            if (ArquivoJsonDoKit.Ler(caminho, desdeUtc) is not { ValueKind: JsonValueKind.Object } recurso) continue;
            linhas[i] = linha with
            {
                CpuMediaMhz = ProgressoLocal.Numero(recurso, "cpu_mhz_media"),
                CpuPicoMhz = ProgressoLocal.Numero(recurso, "cpu_mhz_pico"),
                RamPicoBytes = Inteiro(recurso, "ram_pico_bytes")
            };
        }
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

    /// <summary>O <see cref="Historico"/> lido uma vez por sessão do Editor (por pasta de processos), fora da thread de quem chama. A <paramref name="ordemDoKit"/>
    /// só vale na primeira chamada de cada pasta.</summary>
    public static Task<IReadOnlyList<DuracaoEtapa>> HistoricoDaSessaoAsync(string pastaProcessos, OrdemDasEtapas? ordemDoKit = null) =>
        historicos.GetOrAdd(pastaProcessos ?? "", raiz => new Lazy<Task<IReadOnlyList<DuracaoEtapa>>>(() => Task.Run(() => Historico(raiz, ordemDoKit, somenteFluxoLinear: true)))).Value;

    /// <summary>As durações das etapas executadas em todas as análises sob a pasta de processos
    /// (<c>&lt;raiz&gt;\…\&lt;processo&gt;\.pmvni-ai\&lt;analise|analise-pro|analise-google&gt;</c>, até três níveis de pasta), por <see cref="DuracoesDaPasta"/>.</summary>
    /// <param name="somenteFluxoLinear">Só as amostras do fluxo linear (<see cref="FluxoLinear"/>): controles de provedores com análise marcada pelo Editor,
    /// e só o que foi gravado a partir do primeiro início marcado. O Monitor usa assim; sem a marca, valem as durações iniciais.</param>
    public static IReadOnlyList<DuracaoEtapa> Historico(string pastaProcessos, OrdemDasEtapas? ordemDoKit = null, bool somenteFluxoLinear = false)
    {
        var saida = new List<DuracaoEtapa>();
        if (string.IsNullOrWhiteSpace(pastaProcessos) || !Directory.Exists(pastaProcessos)) return saida;
        // A pasta .pmvni-ai pode estar oculta: nada é pulado pelos atributos; a profundidade limita a busca (e qualquer atalho circular).
        var opcoes = new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 3, IgnoreInaccessible = true, AttributesToSkip = 0 };
        IEnumerable<string> pastas;
        try { pastas = Directory.EnumerateDirectories(pastaProcessos, ".pmvni-ai", opcoes).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return saida; }
        foreach (var pmvni in pastas)
        {
            var marcados = somenteFluxoLinear ? FluxoLinear.IniciosMarcados(pmvni) : null;
            foreach (var plugin in new[] { "analise", "analise-pro", "analise-google" })
            {
                var controle = Path.Combine(pmvni, plugin);
                DateTimeOffset? desde = null;
                if (marcados is not null)
                {
                    if (ProgressoLocal.ProvedorDaPasta(controle) is not { } p || !marcados.TryGetValue(p, out var inicio)) continue;
                    desde = inicio;
                }
                saida.AddRange(DuracoesDaPasta(controle, ordemDoKit, desde));
            }
        }
        return saida;
    }

    /// <summary>Uma execução de etapa achada numa das fontes: a pasta da execução (<c>run</c>/<c>execucao</c>), se o kit a informa, e a duração.</summary>
    readonly record struct Execucao(string Etapa, string? Pasta, TimeSpan Duracao);

    /// <summary>As durações das etapas executadas (<c>EXECUTED</c>) numa pasta de controle, de duas fontes (<paramref name="ordemDoKit"/>: a sequência das
    /// etapas do kit, que o kit 4.0 grava com <c>started</c> em todas, preferido ao fim da etapa anterior; null = a ordem padrão):
    /// <list type="bullet">
    /// <item>o <c>engine_state.json</c>, nas três rotas e também nas etapas de programa: as etapas atuais (<c>stages</c>) e as substituídas (<c>history[]</c>,
    /// nos dois formatos do kit: <c>{"replaced": {etapa: registro}}</c> no analise, <c>{etapa: registro}</c> no análise pro e no Google). Duração =
    /// <c>ended − started</c>; sem <c>started</c> (o analise só grava <c>ended</c>), o intervalo desde o <c>ended</c> da etapa que terminou logo antes, no mesmo quadro (pelo <c>ended</c>, não pela ordem do kit);</item>
    /// <item>o <c>uso-das-IAs.json</c> (<c>duracao_s</c>), que traz as tentativas de cada etapa.</item>
    /// </list>
    /// A mesma execução nas duas fontes conta uma vez só: mesma pasta de execução ou, sem ela, mesma etapa com durações a menos de 1 s uma da outra. Arquivo
    /// ilegível ou pela metade é ignorado.</summary>
    /// <param name="desde">Com valor, só as execuções que começaram a partir dele (no relatório de uso, que não traz horário, só as que têm a pasta da
    /// execução criada a partir dele).</param>
    public static IReadOnlyList<DuracaoEtapa> DuracoesDaPasta(string pastaControle, OrdemDasEtapas? ordemDoKit = null, DateTimeOffset? desde = null)
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
                // Sem started, a etapa começou quando terminou a que acabou logo antes dela pelo ended (a ordem real do que aconteceu), não pela ordem do
                // kit: uma análise antiga do ChatGPT (kit 3.x, produce antes de audit) lida com a ordem nova do kit 4.0 não distorce as durações.
                var fins = new List<DateTimeOffset>();
                foreach (var etapa in etapas)
                    if (quadro.TryGetProperty(etapa, out var o) && o.ValueKind == JsonValueKind.Object && ProgressoLocal.Instante(o, "ended") is { } f) fins.Add(f);
                foreach (var etapa in etapas)
                {
                    if (!quadro.TryGetProperty(etapa, out var r) || r.ValueKind != JsonValueKind.Object) continue;
                    var fim = ProgressoLocal.Instante(r, "ended");
                    var inicio = ProgressoLocal.Instante(r, "started")
                        ?? (fim is { } termina && fins.Any(f => f < termina) ? fins.Where(f => f < termina).Max() : (DateTimeOffset?)null);
                    if (ProgressoLocal.Texto(r, "status") != "EXECUTED" || inicio is not { } partida || fim is not { } termino || termino <= partida) continue;
                    if (desde is { } d && partida < d) continue;
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
                    if (desde is { } d && !CriadaDesde(execucao.Pasta, pastaControle, d)) continue;
                    // Só contra o estado, e pela duração mesmo com pastas diferentes: a tentativa final de uma etapa com troca de rota tem pasta própria no
                    // relatório de uso. As linhas do relatório já são execuções distintas entre si (o uso_ias.py junta por pasta).
                    if (!achadas.Take(doEstado).Any(a => Mesma(a, execucao, true))) achadas.Add(execucao);
                }

        return achadas.Select(x => new DuracaoEtapa(x.Etapa, provedor, x.Duracao)).ToList();
    }

    static bool CriadaDesde(string? pasta, string pastaControle, DateTimeOffset desde)
    {
        if (string.IsNullOrWhiteSpace(pasta)) return false;
        try
        {
            var caminho = Path.IsPathRooted(pasta) ? pasta : Path.Combine(pastaControle, pasta);
            return Directory.Exists(caminho) && Directory.GetCreationTimeUtc(caminho) >= desde.UtcDateTime;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return false; }
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

    static double? PercentualUsado(JsonElement? janelas, string chave, string alternativa) =>
        (Objeto(janelas, chave) ?? Objeto(janelas, alternativa)) is { } janela && ProgressoLocal.Numero(janela, "used_percent") is { } p and >= 0 and <= 100
            ? p : null;

    static double? Variacao(JsonElement? antes, JsonElement? depois, string chave, string alternativa)
    {
        var inicio = Objeto(antes, chave) ?? Objeto(antes, alternativa);
        var fim = Objeto(depois, chave) ?? Objeto(depois, alternativa);
        if (inicio is not { } a || fim is not { } b || ProgressoLocal.Numero(a, "used_percent") is not { } inicial
            || ProgressoLocal.Numero(b, "used_percent") is not { } final || final < inicial) return null;
        if (inicial is < 0 or > 100 || final is < 0 or > 100) return null;
        var minutos = chave == "primary" ? 300 : 10080;
        if (ProgressoLocal.Numero(a, "window_minutes") is { } m1 && m1 != minutos ||
            ProgressoLocal.Numero(b, "window_minutes") is { } m2 && m2 != minutos) return null;
        var r1 = Renovacao(a);
        var r2 = Renovacao(b);
        if (r1 is null || r2 is null || !MesmaRenovacao(r1.Value, r2.Value)) return null;
        return Math.Round(final - inicial, 1);
    }

    // As respostas podem converter "reinicia em N segundos" em instantes ligeiramente diferentes.
    internal static bool MesmaRenovacao(double a, double b) => Math.Abs(a - b) <= 5;

    static double? Renovacao(JsonElement janela)
    {
        if (ProgressoLocal.Numero(janela, "resets_at") is { } segundos) return segundos;
        return janela.TryGetProperty("resets_at", out var valor) && valor.ValueKind == JsonValueKind.String &&
            valor.TryGetDateTimeOffset(out var data) ? data.ToUnixTimeMilliseconds() / 1000d : null;
    }

    internal static string? Identificador(JsonElement? dados, string nome) =>
        dados is { } item && ProgressoLocal.Texto(item, nome) is { } id && !string.IsNullOrWhiteSpace(id) ? id : null;

    internal static JsonElement? Objeto(JsonElement? pai, string nome) =>
        pai is { ValueKind: JsonValueKind.Object } p && p.TryGetProperty(nome, out var filho) && filho.ValueKind == JsonValueKind.Object ? filho : null;

    internal static long? Inteiro(JsonElement? o, string nome) =>
        o is { } e && e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) && n >= 0
            ? (long)n : null;
}
