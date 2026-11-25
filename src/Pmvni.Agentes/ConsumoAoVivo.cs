using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Amostras do provedor copiadas pelo kit para o controle da análise, inclusive em outra estação.</summary>
public sealed record ConsumoAoVivo(string Rota, DateTimeOffset? TokensObservadosEm, bool TokensParciais,
    double? Usada5h, DateTimeOffset? CotaObservadaEm, string? FonteCota,
    double? UsadaSemana, DateTimeOffset? CotaSemanaObservadaEm, string? FonteCotaSemana, bool? CotaAplicavel)
{
    const string Arquivo = "TELEMETRIA-IA.JSON";
    public long? Entrada { get; init; }
    public long? Saida { get; init; }
    public long? CacheLido { get; init; }
    public long? Total { get; init; }
    public string? EtapaEmExecucao { get; init; }
    public string? ModelosEmExecucao { get; init; }

    internal static string? ModelosAtivos(JsonElement registro, string? etapa, DateTimeOffset? agora, DateTime inicio)
    {
        if (agora is null || etapa is null || UsoDasIas.Objeto(registro, "sessoes") is not { } sessoes) return null;
        var modelos = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var propriedade in sessoes.EnumerateObject())
        {
            var sessao = propriedade.Value;
            if (sessao.ValueKind != JsonValueKind.Object || ProgressoLocal.Texto(sessao, "etapa") != etapa
                || ProgressoLocal.Texto(sessao, "estado") != "em_andamento"
                || Instante(sessao, "atividade_em") is not { } atividade || atividade.UtcDateTime < inicio
                || (agora.Value - atividade).TotalSeconds is < -5 or > 90) continue;
            var modelo = ProgressoLocal.Texto(sessao, "modelo_observado");
            modelos.Add(string.IsNullOrWhiteSpace(modelo) ? "Aguardando identificação do modelo" : modelo);
        }
        return modelos.Count > 0 ? string.Join(" · ", modelos) : null;
    }

    public bool SemJanela5h { get; init; }

    internal static (IReadOnlyList<UsoEtapa> Uso, ConsumoAoVivo? Amostra) Ler(string controle, DateTime desde, Provedor provedor,
        string execucao, OrdemDasEtapas ordem, ConsumoSessaoCodex? sessoes, DateTimeOffset? agora, DateTime inicioDaTentativa = default)
    {
        var uso = UsoDasIas.Ler(controle, desde, ordem, sessoes, agora);
        var rota = provedor switch { Provedor.ChatGpt => "codex", Provedor.Claude => "claude", Provedor.Gemini => "gemini", _ => "" };
        var processo = Directory.GetParent(controle)?.Parent?.Name;
        if (ArquivoJsonDoKit.Ler(Path.Combine(controle, Arquivo), desde) is not { ValueKind: JsonValueKind.Object } registro
            || ProgressoLocal.Inteiro(registro, "schema") != 1 || ProgressoLocal.Texto(registro, "rota") != rota
            || !string.Equals(ProgressoLocal.Texto(registro, "processo"), processo, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(ProgressoLocal.Texto(registro, "execucao_id"), execucao, StringComparison.OrdinalIgnoreCase)
            || Instante(registro, "atualizado_em") is not { } atualizado || atualizado.UtcDateTime < desde)
            return (uso, null);

        var tokens = UsoDasIas.Objeto(registro, "tokens");
        var etapas = UsoDasIas.Objeto(tokens, "etapas");
        var linhas = uso.ToList();
        if (etapas is { } medidas && Instante(tokens, "observados_em") is not null)
            foreach (var etapa in ordem.Todas)
                if (UsoDasIas.Objeto(medidas, etapa) is { } medida && Contador(medida, "total") is { } total)
                    IncorporarEtapa(linhas, etapa, UsoDasIas.Executor(null, rota, null), medida, total);

        var aplicavel = Booleano(registro, "cota_aplicavel");
        var cota = aplicavel == false ? null : UsoDasIas.Objeto(registro, "cota");
        var (curta, horaCurta, fonteCurta) = Janela(cota, 300);
        var (semana, horaSemana, fonteSemana) = Janela(UsoDasIas.Objeto(cota, "semana"), 10080);
        var leitura = new ConsumoAoVivo(rota, Instante(tokens, "observados_em"), Booleano(tokens, "parcial") == true,
            curta, horaCurta, fonteCurta, semana, horaSemana, fonteSemana, aplicavel)
        {
            EtapaEmExecucao = ProgressoLocal.Texto(registro, "etapa"),
            ModelosEmExecucao = ModelosAtivos(registro, ProgressoLocal.Texto(registro, "etapa"), agora, inicioDaTentativa),
            Entrada = Contador(tokens, "entrada"), Saida = Contador(tokens, "saida"),
            CacheLido = Contador(tokens, "entrada_cache"), Total = Contador(tokens, "total"),
            SemJanela5h = Booleano(cota, "sem_janela_5h") == true && curta is null && semana is not null
        };
        var etapasOrdenadas = ordem.Todas.ToList();
        var ordenadas = linhas.OrderBy(x => etapasOrdenadas.IndexOf(x.Etapa)).ToList();
        return (ordenadas, leitura);
    }

    static (double?, DateTimeOffset?, string?) Janela(JsonElement? valor, int minutos)
    {
        if (valor is not { } j || ProgressoLocal.Inteiro(j, "janela_min") != minutos
            || ProgressoLocal.Numero(j, "usada_pct") is not { } usada || usada is < 0 or > 100
            || Instante(j, "observada_em") is not { } hora) return (null, null, null);
        return (usada, hora, ProgressoLocal.Texto(j, "fonte"));
    }

    static DateTimeOffset? Instante(JsonElement? o, string chave) =>
        o is { } j && j.TryGetProperty(chave, out var v) && v.ValueKind == JsonValueKind.String && v.TryGetDateTimeOffset(out var d) ? d : null;

    static bool? Booleano(JsonElement? o, string chave) =>
        o is { } j && j.TryGetProperty(chave, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    static long? Contador(JsonElement? j, string chave) =>
        j is { } o && o.TryGetProperty(chave, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) && n >= 0 ? n : null;

    static long? Contagem(decimal? soma) => soma is >= 0 and <= long.MaxValue ? (long)soma.Value : null;

    static long? EntradaNormalizada(UsoEtapa u) => u.Executor == "Claude" && u.EntradaIncluiCache != true
        ? Contagem((decimal?)u.Entrada + u.CacheLido + u.CacheGravado) : u.Entrada;

    static long? TotalRegistrado(UsoEtapa u) => u.Total ?? Contagem((decimal?)EntradaNormalizada(u) + u.Saida);

    static void IncorporarEtapa(List<UsoEtapa> linhas, string etapa, string executor, JsonElement medida, long total)
    {
        var entrada = Contador(medida, "entrada");
        var saida = Contador(medida, "saida");
        var cache = Contador(medida, "entrada_cache");
        var anteriores = linhas.Where(u => u.Etapa == etapa && u.Executor != UsoDasIas.Programa).ToList();
        if (anteriores.Count == 1)
        {
            var anterior = anteriores[0];
            var registrado = TotalRegistrado(anterior);
            if (registrado > total) return;
            linhas[linhas.IndexOf(anterior)] = anterior with
            {
                Executor = anterior.Executor == TextoConsumo.Desconhecido ? executor : anterior.Executor,
                Entrada = entrada ?? anterior.Entrada, Saida = saida ?? anterior.Saida,
                CacheLido = cache ?? anterior.CacheLido, Total = total,
                EntradaIncluiCache = entrada.HasValue ? true : anterior.EntradaIncluiCache
            };
            return;
        }

        // Uma etapa pode ter várias tentativas. Preserve os números individuais e mostre apenas o saldo
        // ainda sem detalhamento. O agregado da etapa nunca é somado integralmente às tentativas.
        var registrados = anteriores.Sum(u => (decimal?)TotalRegistrado(u)) ?? 0m;
        if (anteriores.Count > 0 && registrados >= total) return;
        long? Saldo(long? medido, Func<UsoEtapa, long?> valor) =>
            medido is { } n && anteriores.All(u => valor(u).HasValue) ? Contagem(n - (anteriores.Sum(u => (decimal?)valor(u)) ?? 0m)) : null;
        linhas.Add(new UsoEtapa(etapa, executor, null, Saldo(entrada, EntradaNormalizada), Saldo(saida, u => u.Saida),
            Saldo(cache, u => u.CacheLido), null, null, null)
        {
            Total = Contagem(total - registrados), EntradaIncluiCache = true,
            Situacao = anteriores.Count > 0 ? "Medição da etapa ainda sem discriminação por tentativa" : null
        });
    }
}
