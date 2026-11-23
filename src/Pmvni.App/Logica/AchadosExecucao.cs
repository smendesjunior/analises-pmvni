using System.Security.Cryptography;
using System.Text.Json;
using Pmvni.Composicao.Kit;

namespace Pmvni.App;

internal sealed record AchadoExecucao(string Id, long Sequencia, string Titulo, string Situacao, bool Verificado);

/// <summary>O progresso de uma etapa: itens avaliados (eventos antigos da IA) ou, com <paramref name="Secoes"/>, as seções do manifesto com resultado.</summary>
internal sealed record ProgressoEtapa(string Etapa, long Sequencia, int Avaliados, int Total, bool Secoes = false);

/// <summary>A previsão do Monitor. <paramref name="PeloEditor"/>: calculada pelo Editor (Tarefa 13: mediana do histórico, <see cref="Pmvni.Agentes.PrevisaoTermino"/>);
/// senão, a estimativa que a IA gravava em <c>estimativa.json</c> numa análise antiga. <paramref name="SemDados"/>: o kit ainda não registrou nada desta análise.
/// <paramref name="Aproximada"/>: a previsão saiu do histórico curto ou de outros provedores. <paramref name="Inicial"/>: algum percentual ou a previsão
/// usou uma duração inicial.</summary>
internal sealed record EstimativaAnalise(string? Etapa, double? PercentualEtapa, double? PercentualGeral, DateTimeOffset? Termino, bool PeloEditor = false,
    bool SemDados = false, bool Aproximada = false, bool Inicial = false, bool PercentualEstimado = false);
internal sealed record PainelAchados(List<AchadoExecucao> Itens, Dictionary<string, ProgressoEtapa> Etapas, EstimativaAnalise? Estimativa);

internal static class AchadosExecucao
{
    /// <summary>O painel do Monitor para uma execução (Tarefa 13). Uma análise iniciada por um Editor antigo grava eventos em <c>progresso\*.json</c> do
    /// registro, usados quando o kit ainda não tem progresso próprio. Um achado legado isolado não esconde a previsão nem as medições que o kit já
    /// registra (<see cref="Pmvni.Agentes.ProgressoLocal"/>), com previsão quando há histórico suficiente.</summary>
    public static PainelAchados LerDaExecucao(string pastaLegado, string processo, DateTime inicioUtc, string pastaControle, DateTime desdeUtc,
        IReadOnlyList<Pmvni.Agentes.DuracaoEtapa>? historico, DateTimeOffset agora, Pmvni.Agentes.OrdemDasEtapas? ordemDoKit = null, bool iniciando = false)
    {
        var legado = Ler(pastaLegado, processo, inicioUtc);
        var p = Pmvni.Agentes.ProgressoLocal.Ler(pastaControle, desdeUtc, ordemDoKit);
        if (!p.TemDados && (legado.Etapas.Count > 0 || legado.Estimativa is not null)) return legado;
        if (iniciando && !p.TemDados && p.Ordem is { Count: > 0 } ordem)
            p = p with { Etapa = ordem[0], InicioEtapa = new DateTimeOffset(inicioUtc.ToUniversalTime()) };
        var atual = DoProgresso(p, historico, agora);
        return atual.Itens.Count == 0 && legado.Itens.Count > 0 ? atual with { Itens = legado.Itens } : atual;
    }

    /// <summary>O <see cref="Pmvni.Agentes.Progresso"/> no formato do Monitor: os achados (verificados depois da auditoria técnica), as seções com resultado
    /// durante a análise (<c>analyze</c>), os percentuais inteiros e a previsão de término arredondada ao minuto.</summary>
    internal static PainelAchados DoProgresso(Pmvni.Agentes.Progresso p, IReadOnlyList<Pmvni.Agentes.DuracaoEtapa>? historico, DateTimeOffset agora)
    {
        var itens = p.Achados.Select(a => new AchadoExecucao(a.Id, 0, a.Titulo, a.Situacao, a.Verificado)).ToList();
        var etapas = new Dictionary<string, ProgressoEtapa>(StringComparer.Ordinal);
        if (p.Etapa == "analyze") etapas["analyze"] = new("analyze", p.SecoesComResultado, p.SecoesComResultado, p.TotalSecoes, Secoes: true);
        // Sem nada do kit ainda: a previsão continua sendo do Editor (o Monitor mostra "Previsão depois das primeiras etapas", nunca "…da IA").
        if (!p.TemDados) return new(itens, etapas, new EstimativaAnalise(null, null, null, null, PeloEditor: true, SemDados: true));
        var (geral, etapa) = Pmvni.Agentes.ProgressoGeral.Calcular(p);
        if (p.Pausado) return new(itens, etapas, new EstimativaAnalise(p.Etapa, etapa, geral, null, PeloEditor: true));
        var termino = historico is null ? null : Pmvni.Agentes.PrevisaoTermino.Estimar(p, historico, agora);
        var aproximada = false;
        if (termino is null && historico is not null)
        {
            termino = Pmvni.Agentes.PrevisaoTermino.EstimarAproximada(p, historico, agora);
            aproximada = termino.HasValue;
        }
        var inicial = false;
        if (termino is null)
        {
            termino = Pmvni.Agentes.PrevisaoTermino.EstimarInicial(p, historico, agora);
            inicial = termino.HasValue;
        }
        if (termino is { } t && t.Ticks % TimeSpan.TicksPerMinute is var resto and > 0) termino = t.AddTicks(TimeSpan.TicksPerMinute - resto);
        return new(itens, etapas, new EstimativaAnalise(p.Etapa, etapa, geral, termino, PeloEditor: true, Aproximada: aproximada, Inicial: inicial));
    }

    public static PainelAchados Ler(string pasta, string processo, DateTime inicioUtc = default)
    {
        var ultimos = new Dictionary<string, AchadoExecucao>(StringComparer.Ordinal);
        var etapas = new Dictionary<string, ProgressoEtapa>(StringComparer.Ordinal);
        EstimativaAnalise? estimativa = null;
        if (!Directory.Exists(pasta) || new DirectoryInfo(pasta).LinkTarget is not null) return new([], etapas, null);
        foreach (var arquivo in new DirectoryInfo(pasta).EnumerateFiles("*.json").OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            try
            {
                if (arquivo.LinkTarget is not null || arquivo.Length > 128 * 1024) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(arquivo.FullName));
                var e = doc.RootElement;
                string? Texto(string nome) => e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (e.ValueKind != JsonValueKind.Object) continue;
                if (arquivo.Name.Equals(Pmvni.Agentes.RegistroExecucao.ArquivoEstimativa, StringComparison.OrdinalIgnoreCase))
                {
                    var escrita = arquivo.LastWriteTimeUtc;
                    if (escrita >= inicioUtc)
                    {
                        var idEtapa = Texto("etapa");
                        var etapaValida = idEtapa is not null && Pmvni.Agentes.SituacaoAnalisePro.Etapas.Contains(idEtapa);
                        var termino = e.TryGetProperty("termino_estimado_utc", out var data) && data.ValueKind == JsonValueKind.String
                            && data.TryGetDateTimeOffset(out var hora) && hora.Offset == TimeSpan.Zero && hora > escrita
                            ? (DateTimeOffset?)hora : null;
                        estimativa = new(etapaValida ? idEtapa : null, etapaValida ? Percentual(e, "percentual_etapa") : null,
                            Percentual(e, "percentual_geral"), termino);
                    }
                    continue;
                }
                if (Texto("etapa") is { } etapa && Pmvni.Agentes.SituacaoAnalisePro.Etapas.Contains(etapa)
                    && e.TryGetProperty("avaliados", out var a) && a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out var avaliados)
                    && e.TryGetProperty("total", out var t) && t.ValueKind == JsonValueKind.Number && t.TryGetInt32(out var total)
                    && avaliados >= 0 && total >= avaliados)
                {
                    long ordem = avaliados;
                    var ordemValida = !e.TryGetProperty("sequencia_etapa", out var numero) ||
                        numero.ValueKind == JsonValueKind.Number && numero.TryGetInt64(out ordem) && ordem >= 0;
                    if (ordemValida && (!etapas.TryGetValue(etapa, out var antes) || ordem > antes.Sequencia))
                        etapas[etapa] = new(etapa, ordem, avaliados, total);
                }
                if (Texto("item_id") is not { Length: > 0 } id ||
                    Texto("titulo") is not { Length: > 0 } titulo || Texto("situacao") is not { } situacao ||
                    !Situacoes.SitItem.Contains(situacao) || !e.TryGetProperty("sequencia", out var n) ||
                    n.ValueKind != JsonValueKind.Number || !n.TryGetInt64(out var seq) || seq < 0 ||
                    ultimos.TryGetValue(id, out var antigo) && antigo.Sequencia >= seq) continue;
                var verificado = ConferirProva(processo, Texto("evidencia_verificacao"), Texto("sha256_verificacao"));
                ultimos[id] = new(id, seq, titulo, situacao, verificado);
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { }
        }
        return new(ultimos.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToList(), etapas, estimativa);
    }

    static double? Percentual(JsonElement dados, string chave) =>
        dados.TryGetProperty(chave, out var valor) && valor.ValueKind == JsonValueKind.Number
        && valor.TryGetDouble(out var numero) && numero is >= 0 and <= 99.9 ? numero : null;

    static bool ConferirProva(string processo, string? relativo, string? hash)
    {
        if (string.IsNullOrWhiteSpace(relativo) || Path.IsPathRooted(relativo) || hash?.Length != 64) return false;
        var raiz = Path.TrimEndingDirectorySeparator(Path.GetFullPath(processo));
        var prova = new FileInfo(Path.GetFullPath(Path.Combine(raiz, relativo)));
        if (!prova.FullName.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !prova.Exists || prova.LinkTarget is not null) return false;
        for (var pasta = prova.Directory; pasta is not null && !string.Equals(pasta.FullName, raiz, StringComparison.OrdinalIgnoreCase); pasta = pasta.Parent)
            if (pasta.LinkTarget is not null) return false;
        using var entrada = prova.OpenRead();
        return Convert.ToHexString(SHA256.HashData(entrada)).Equals(hash, StringComparison.OrdinalIgnoreCase);
    }
}
