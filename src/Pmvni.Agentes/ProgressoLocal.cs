using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Um achado do <c>manifesto.json</c> do kit, como o Monitor mostra: a situação já na chave do relatório (<c>ATENDE</c>, <c>NAO_ATENDE</c>,
/// <c>NAO_COMPROVADO</c> ou <c>DIVERGENCIA_INTERNA</c>); <see cref="Verificado"/> depois que a auditoria técnica (<c>technical_audit</c>) terminou.</summary>
public sealed record AchadoResumo(string Id, int? Secao, string Situacao, string Titulo, bool Verificado);

/// <summary>O progresso de uma análise calculado pelo Editor (Tarefa 13): a etapa atual e as concluídas (do <c>engine_state.json</c>), as seções com
/// resultado e os achados (do <c>manifesto.json</c>). <see cref="Etapa"/> null = sem estado ou todas as etapas concluídas. <see cref="InicioEtapa"/>: quando
/// a etapa atual começou (o <c>started</c> dela, se está <c>RUNNING</c>, ou o <c>ended</c> da anterior), para a <see cref="PrevisaoTermino"/>.</summary>
public sealed record Progresso(string? Etapa, int EtapasConcluidas, int SecoesComResultado, IReadOnlyList<AchadoResumo> Achados,
    Provedor? Provedor = null, DateTimeOffset? InicioEtapa = null)
{
    /// <summary>As 27 seções técnicas e a 28ª de consolidação do kit.</summary>
    public int TotalSecoes { get; init; } = ProgressoLocal.TotalSecoesDoKit;

    /// <summary>As etapas desta análise na rota, na ordem do kit (<see cref="OrdemDasEtapas"/>), sem as que a rota desliga (a audit, por exemplo);
    /// null = a ordem padrão (kit 3.x).</summary>
    public IReadOnlyList<string>? Ordem { get; init; }

    /// <summary>As etapas que o estado confirma como concluídas antes da atual, mesmo se houver lacunas na ordem.</summary>
    internal IReadOnlyList<string>? EtapasExecutadas { get; init; }

    /// <summary>O que o kit permite contar na etapa atual: seções com resultado, seções auditadas ou saídas da montagem.</summary>
    internal (int Feitos, int Total)? SinalDaEtapa { get; init; }

    /// <summary>Data de gravação do artefato que sustenta a última contagem; uma consulta do relógio não renova a medição.</summary>
    internal DateTimeOffset? InstanteDaMedicao { get; init; }

    public bool Pausado { get; internal init; }

    internal double? FracaoObservada => SinalDaEtapa is { Total: > 0 } sinal ? Math.Clamp((double)sinal.Feitos / sinal.Total, 0, 1)
        : Etapa == "analyze" && SecoesComResultado > 0 && TotalSecoes > 0 ? Math.Clamp((double)SecoesComResultado / TotalSecoes, 0, 1) : null;

    /// <summary>Quantas etapas a análise tem nesta rota: as desligadas não entram.</summary>
    public int TotalEtapas => (Ordem ?? OrdemDasEtapas.Padrao.Todas).Count;

    /// <summary>true se algum dos arquivos do kit trouxe algo desta análise.</summary>
    public bool TemDados => Etapa is not null || EtapasConcluidas > 0 || SecoesComResultado > 0 || Achados.Count > 0;
}

/// <summary>Lê o progresso da pasta de controle do kit (<c>&lt;processo&gt;\.pmvni-ai\&lt;analise|analise-pro|analise-google&gt;</c>). Nenhuma IA grava mais
/// <c>progresso\*.json</c> nem <c>estimativa.json</c>: o kit já registra tudo isso no manifesto (schema 3) e no estado das etapas.
/// Barato para chamar a cada segundo: cada arquivo só é lido de novo quando muda (data e tamanho); JSON pela metade (o kit gravando) mantém o último
/// conteúdo válido, sem exceção.</summary>
public static class ProgressoLocal
{
    internal const int TotalSecoesDoKit = 28;

    /// <summary>A situação de uma etapa concluída no <c>engine_state.json</c> (<c>FAILED</c>, <c>OBJECTIONS</c>, <c>WAITING_QUOTA</c>… não contam).</summary>
    const string Executada = "EXECUTED";

    /// <summary>A etapa com a sessão aberta agora: o kit grava o registro com <c>started</c> e sem <c>ended</c> até ela terminar.</summary>
    const string EmAndamento = "RUNNING";

    /// <summary>A etapa parou à espera da cota ou de uma decisão do analista: não está concluída, e a espera não é tempo de trabalho.</summary>
    static bool Espera(string? status) => status is "WAITING_QUOTA" or "WAITING_DECISION";

    /// <param name="desdeUtc">Arquivos gravados antes disto (a análise anterior, já entregue) não contam.</param>
    /// <param name="ordemDoKit">A sequência das etapas que o kit define (<see cref="OrdemDasEtapas.Ler"/>); null = a ordem padrão (kit 3.x).</param>
    public static Progresso Ler(string pastaControle, DateTime desdeUtc = default, OrdemDasEtapas? ordemDoKit = null)
    {
        var provedor = ProvedorDaPasta(pastaControle);
        var ordem = (ordemDoKit ?? OrdemDasEtapas.Padrao).Para(provedor);
        var estado = ArquivoJsonDoKit.Ler(Path.Combine(pastaControle, "engine_state.json"), desdeUtc);
        var arquivoManifesto = Path.Combine(pastaControle, "manifesto.json");
        var manifesto = ArquivoJsonDoKit.Ler(arquivoManifesto, desdeUtc);
        DateTimeOffset? instanteDaMedicao = null;

        string? etapa = null;
        var pausado = false;
        var concluidas = new List<string>();
        DateTimeOffset? inicioEtapa = null;
        (int Feitos, int Total)? sinal = null;
        if (estado is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty("stages", out var stages) && stages.ValueKind == JsonValueKind.Object)
        {
            JsonElement? Registro(string chave) => stages.TryGetProperty(chave, out var r) && r.ValueKind == JsonValueKind.Object ? r : null;
            // A etapa atual é a que está RUNNING (a primeira, na ordem do kit); sem ela, a primeira que ainda não foi executada (parada por falha, objeção ou
            // espera de cota ou de decisão, ou ainda sem registro). As concluídas são as EXECUTED que vêm antes dela.
            var atual = -1;
            for (var i = 0; i < ordem.Count && atual < 0; i++)
                if (Registro(ordem[i]) is { } r && Texto(r, "status") == EmAndamento) atual = i;
            for (var i = 0; i < ordem.Count && atual < 0; i++)
                if (Registro(ordem[i]) is not { } r || Texto(r, "status") != Executada) atual = i;
            DateTimeOffset? fimAnterior = null;
            for (var i = 0; i < (atual < 0 ? ordem.Count : atual); i++)
                if (Registro(ordem[i]) is { } feita && Texto(feita, "status") == Executada)
                {
                    concluidas.Add(ordem[i]);
                    fimAnterior = Instante(feita, "ended") ?? fimAnterior;
                }
            if (atual >= 0)
            {
                etapa = ordem[atual];
                var registro = Registro(etapa);
                var situacao = registro is { } r ? Texto(r, "status") : null;
                pausado = situacao is not (null or EmAndamento);
                // RUNNING (ou registro ainda sem ended): o started dela, que o kit grava em todas as etapas; sem ele, o fim da anterior. Em espera de cota ou
                // de decisão o início não é confiável (a espera não é trabalho): a previsão conta a etapa inteira. Parada por falha ou objeção: o fim da anterior.
                inicioEtapa = Espera(situacao) ? null
                    : registro is { } atualRegistro && (situacao == EmAndamento || Instante(atualRegistro, "ended") is null)
                        ? Instante(atualRegistro, "started") ?? fimAnterior
                        : fimAnterior;
                if (etapa == "prepare" && situacao == EmAndamento && registro is { } preparo
                    && PastaDaTentativa(pastaControle, Texto(preparo, "run")) is { } tentativa)
                {
                    var contador = Path.Combine(tentativa, "preparo", "andamento.json");
                    if (ArquivoJsonDoKit.Ler(contador, inicioEtapa?.UtcDateTime ?? desdeUtc) is { ValueKind: JsonValueKind.Object } leitura
                        && Inteiro(leitura, "feitos") is int feitos && Inteiro(leitura, "total") is > 0 and int total && feitos >= 0 && feitos <= total)
                    {
                        sinal = (feitos, total);
                        instanteDaMedicao = File.GetLastWriteTimeUtc(contador);
                    }
                }
                if (etapa == "produce" && situacao == EmAndamento && registro is { } producao && Instante(producao, "started") is { } inicioProducao)
                    sinal = SaidasDaProducao(pastaControle, Texto(producao, "run"), inicioProducao.UtcDateTime, desdeUtc);
            }
        }

        var secoes = 0;
        var achados = new List<AchadoResumo>();
        if (manifesto is { ValueKind: JsonValueKind.Object } m)
        {
            var titulos = new Dictionary<int, string>();
            var comResultado = new HashSet<int>();
            if (m.TryGetProperty("section_coverage", out var cobertura) && cobertura.ValueKind == JsonValueKind.Array)
            {
                foreach (var s in cobertura.EnumerateArray())
                {
                    if (s.ValueKind != JsonValueKind.Object || Inteiro(s, "section") is not int numero || numero is < 1 or > TotalSecoesDoKit) continue;
                    if (Texto(s, "title") is { Length: > 0 } titulo) titulos[numero] = titulo;
                    if (Normalizar(Texto(s, "status")) is "ANALISADO" or "NAO APLICAVEL" or "NAO VERIFICAVEL") comResultado.Add(numero);
                }
                secoes = Math.Min(comResultado.Count, TotalSecoesDoKit);
                if (etapa == "analyze") sinal = (secoes, TotalSecoesDoKit);
            }
            if (etapa == "technical_audit" && (inicioEtapa is null || File.GetLastWriteTimeUtc(arquivoManifesto) >= inicioEtapa.Value.UtcDateTime)
                && m.TryGetProperty("section_audit", out var auditoria) && auditoria.ValueKind == JsonValueKind.Array)
            {
                var auditadas = new HashSet<int>();
                foreach (var item in auditoria.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object && Inteiro(item, "section") is int secao && secao is >= 1 and <= TotalSecoesDoKit)
                        auditadas.Add(secao);
                sinal = (auditadas.Count, TotalSecoesDoKit);
            }
            if (sinal is { Feitos: > 0 } && etapa is "analyze" or "technical_audit")
                instanteDaMedicao = File.GetLastWriteTimeUtc(arquivoManifesto);
            var verificados = concluidas.Contains("technical_audit");
            var porId = new Dictionary<string, int>(StringComparer.Ordinal);
            if (m.TryGetProperty("findings", out var lista) && lista.ValueKind == JsonValueKind.Array)
                foreach (var f in lista.EnumerateArray())
                {
                    if (f.ValueKind != JsonValueKind.Object || Texto(f, "id") is not { Length: > 0 } id || Situacao(Texto(f, "status")) is not { } situacao) continue;
                    var secao = Inteiro(f, "section");
                    var titulo = secao is { } n ? titulos.GetValueOrDefault(n, "Seção " + n.ToString(CultureInfo.InvariantCulture)) : "Sem seção";
                    var achado = new AchadoResumo(id, secao, situacao, titulo, verificados);
                    if (porId.TryGetValue(id, out var i)) achados[i] = achado;     // o mesmo id repetido: vale o último
                    else { porId[id] = achados.Count; achados.Add(achado); }
                }
        }
        // Fluxo linear do kit: o manifesto só aparece depois da montagem. Até lá, as seções prontas nas três frentes (secao-NN.json) alimentam a Análise;
        // com o manifesto, vale o maior dos dois (a barra não volta).
        if (etapa == "analyze")
        {
            var prontas = Math.Min(DetalhesDasEtapas.LerFrentes(pastaControle, UsoDasIas.Objeto(estado, "sessao_codex"), desdeUtc, out var ultimoResultado).Sum(f => f.Prontas), TotalSecoesDoKit);
            if (prontas > secoes)
            {
                secoes = prontas;
                sinal = (prontas, TotalSecoesDoKit);
                instanteDaMedicao = ultimoResultado;
            }
        }
        if (etapa == "technical_audit" && !pausado && MedicaoDaEstacao(pastaControle, etapa, inicioEtapa) is { } auditoriaLocal
            && auditoriaLocal.Secoes.Count > (sinal?.Feitos ?? 0))
        {
            sinal = (auditoriaLocal.Secoes.Count, TotalSecoesDoKit);
            instanteDaMedicao = auditoriaLocal.Escritura;
        }
        return new Progresso(etapa, concluidas.Count, secoes, achados, provedor, inicioEtapa)
            { Ordem = ordem, EtapasExecutadas = concluidas, Pausado = pausado, SinalDaEtapa = sinal, InstanteDaMedicao = instanteDaMedicao };
    }

    internal static (HashSet<int> Secoes, DateTime Escritura)? MedicaoDaEstacao(string controle, string etapa, DateTimeOffset? inicio)
    {
        if (inicio is null) return null;
        var arquivo = Path.Combine(controle, "monitor-unidades.json");
        if (ArquivoJsonDoKit.Ler(arquivo) is not { ValueKind: JsonValueKind.Object } leitura
            || Texto(leitura, "etapa") != etapa || Instante(leitura, "inicio") != inicio
            || !leitura.TryGetProperty("secoes", out var secoes) || secoes.ValueKind != JsonValueKind.Array) return null;
        var ids = new HashSet<int>();
        foreach (var item in secoes.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var n) && n is >= 1 and <= TotalSecoesDoKit) ids.Add(n);
        return (ids, File.GetLastWriteTimeUtc(arquivo));
    }

    // O kit pode registrar o UNC enquanto o Editor abriu a mesma pasta por uma letra mapeada.
    // Só o nome da tentativa é usado, sempre sob a pasta runs da execução que está aberta.
    internal static string? PastaDaTentativa(string controle, string? registrada)
    {
        if (string.IsNullOrWhiteSpace(registrada) || !Path.IsPathFullyQualified(registrada)) return null;
        try
        {
            var origem = new DirectoryInfo(registrada);
            if (!string.Equals(origem.Parent?.Name, "runs", StringComparison.OrdinalIgnoreCase)) return null;
            var runs = new DirectoryInfo(Path.Combine(controle, "runs"));
            var local = new DirectoryInfo(Path.Combine(runs.FullName, origem.Name));
            return local.Exists && local.LinkTarget is null && runs.LinkTarget is null ? local.FullName : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>O kit 4 grava dados e PDF na pasta da tentativa (campo run), depois publica o delivery na raiz do controle.</summary>
    static (int Feitos, int Total)? SaidasDaProducao(string pastaControle, string? tentativa, DateTime inicioUtc, DateTime desdeUtc)
    {
        if (string.IsNullOrWhiteSpace(tentativa) || !Path.IsPathFullyQualified(tentativa)) return null;
        try
        {
            var controle = new DirectoryInfo(pastaControle);
            var runs = new DirectoryInfo(Path.Combine(controle.FullName, "runs"));
            if (PastaDaTentativa(pastaControle, tentativa) is not { } local) return null;
            var pasta = new DirectoryInfo(local);
            // Só a tentativa registrada, diretamente dentro de runs, sem seguir links ou procurar resultados de tentativas anteriores.
            if (!string.Equals(pasta.Parent?.FullName, runs.FullName, StringComparison.OrdinalIgnoreCase)
                || !pasta.Exists || pasta.LinkTarget is not null || runs.LinkTarget is not null || controle.LinkTarget is not null) return null;

            var limite = inicioUtc > desdeUtc ? inicioUtc : desdeUtc;
            var dados = Path.Combine(pasta.FullName, "relatorio_dados.json");
            var pdf = new FileInfo(Path.Combine(pasta.FullName, "RELATORIO.pdf"));
            var feitos = 0;
            if (ArquivoJsonDoKit.Ler(dados, limite) is { ValueKind: JsonValueKind.Object }) feitos++;
            if (pdf.Exists && pdf.LinkTarget is null && pdf.LastWriteTimeUtc >= limite && pdf.Length > 0) feitos++;
            if (ArquivoJsonDoKit.Ler(Path.Combine(controle.FullName, "delivery.json"), limite) is { ValueKind: JsonValueKind.Object } entrega
                && string.Equals(Texto(entrega, "document"), dados, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Texto(entrega, "pdf"), pdf.FullName, StringComparison.OrdinalIgnoreCase)) feitos++;
            return (feitos, 3);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>O provedor da rota pela pasta de controle: <c>analise</c> = ChatGPT, <c>analise-pro</c> = Claude, <c>analise-google</c> = Gemini.</summary>
    public static Provedor? ProvedorDaPasta(string pastaControle) =>
        Path.GetFileName(Path.TrimEndingDirectorySeparator(pastaControle ?? "")).ToLowerInvariant() switch
        {
            "analise" => Provedor.ChatGpt, "analise-pro" => Provedor.Claude, "analise-google" => Provedor.Gemini, _ => null,
        };

    /// <summary>A situação de um achado do manifesto ("NÃO ATENDE", "NÃO COMPROVADO / NÃO VERIFICÁVEL", "DIVERGÊNCIA INTERNA"…) na chave do relatório; null se
    /// desconhecida (o Monitor não mostra o que não sabe classificar).</summary>
    public static string? Situacao(string? texto)
    {
        var n = Normalizar(texto);
        if (n == "ATENDE") return "ATENDE";
        if (n.StartsWith("NAO ATENDE", StringComparison.Ordinal)) return "NAO_ATENDE";
        if (n.Contains("DIVERGENCIA", StringComparison.Ordinal) || n.Contains("DIVERGENTE", StringComparison.Ordinal)) return "DIVERGENCIA_INTERNA";
        if (n.Contains("NAO COMPROVADO", StringComparison.Ordinal) || n.Contains("NAO VERIFICAVEL", StringComparison.Ordinal)
            || n.Contains("FALTA COMPROVAR", StringComparison.Ordinal)) return "NAO_COMPROVADO";
        return null;
    }

    /// <summary>Maiúsculas, sem acento, "_" como espaço e espaços simples.</summary>
    internal static string Normalizar(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var sb = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c == '_' ? ' ' : char.ToUpperInvariant(c));
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    internal static string? Texto(JsonElement o, string nome) =>
        o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    internal static int? Inteiro(JsonElement o, string nome) =>
        o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    internal static double? Numero(JsonElement o, string nome) =>
        o.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) && double.IsFinite(n) ? n : null;

    /// <summary>Os instantes do <c>engine_state.json</c> são segundos desde 1970 (o <c>time.time()</c> do Python).</summary>
    internal static DateTimeOffset? Instante(JsonElement o, string nome) =>
        Numero(o, nome) is { } s && s > 0 && s < 253402300799 ? DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(s * 1000)) : null;
}

/// <summary>Leitura dos JSON que o kit grava enquanto a análise roda. Cada arquivo só é relido quando a data ou o tamanho mudam; se o conteúdo novo não é JSON
/// válido (o kit está no meio da gravação), fica o último válido. O que vem de volta é uma cópia independente (<see cref="JsonElement.Clone"/>), segura para
/// ler de várias threads.</summary>
internal static class ArquivoJsonDoKit
{
    const long Limite = 64L * 1024 * 1024;

    sealed record Lido(DateTime Escrita, long Tamanho, JsonElement? Valido, DateTime EscritaDoValido);

    static readonly Dictionary<string, Lido> cache = new(StringComparer.OrdinalIgnoreCase);
    static readonly object trava = new();

    /// <summary>null se o arquivo não existe, nunca foi válido ou o último conteúdo válido é de antes de <paramref name="desdeUtc"/>.</summary>
    public static JsonElement? Ler(string arquivo, DateTime desdeUtc = default)
    {
        string caminho;
        FileInfo info;
        try
        {
            caminho = Path.GetFullPath(arquivo);
            info = new FileInfo(caminho);
            if (!info.Exists || info.LinkTarget is not null || info.Length > Limite)
            {
                lock (trava) cache.Remove(caminho);
                return null;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }

        Lido? anterior;
        lock (trava) cache.TryGetValue(caminho, out anterior);
        var escrita = info.LastWriteTimeUtc;
        var tamanho = info.Length;
        if (anterior is null || anterior.Escrita != escrita || anterior.Tamanho != tamanho)
        {
            JsonElement? valido = anterior?.Valido;
            var escritaDoValido = anterior?.EscritaDoValido ?? default;
            try
            {
                using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(fs, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                valido = doc.RootElement.Clone();
                escritaDoValido = escrita;
            }
            catch (JsonException) { /* o kit está gravando: fica o último válido até o arquivo mudar de novo */ }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Arquivo preso naquele instante: nada muda no cache, para a próxima chamada tentar de novo.
                return anterior?.Valido is { } antes && anterior.EscritaDoValido >= desdeUtc ? antes : null;
            }
            anterior = new Lido(escrita, tamanho, valido, escritaDoValido);
            lock (trava) cache[caminho] = anterior;
        }
        return anterior.Valido is { } v && anterior.EscritaDoValido >= desdeUtc ? v : null;
    }
}
