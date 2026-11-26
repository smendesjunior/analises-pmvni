using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Agentes;

internal sealed record ConsumoDoPlugin(string Processo, DateTimeOffset Inicio)
{
    public DateTimeOffset? Fim { get; set; }
    public string Estado { get; set; } = "Iniciada · aguardando registro de encerramento";
    public long? Entrada { get; set; }
    public long? Cache { get; set; }
    public long? Saida { get; set; }
    public int TurnosMedidos { get; set; }
    public AmostraDaConta? Inicial { get; set; }
    public AmostraDaConta? Ultima { get; set; }
    public bool CotaFinal { get; set; }
    public HashSet<int> JanelasInterrompidas { get; set; } = [];
    public string? FalhaCota { get; set; }
    public string? Origem { get; set; }

    internal string Texto()
    {
        var linhas = new List<string> { $"{Processo} · {Estado}", $"Início {Inicio.LocalDateTime:g}" +
            (Fim is { } fim ? $" · Fim {fim.LocalDateTime:g} · Duração {(fim - Inicio):hh\\:mm\\:ss}" : "") };
        linhas.Add(Entrada is { } entrada && Saida is { } saida
            ? $"Tokens dos turnos informados pela sessão Codex · {entrada + saida:N0} no total · entrada {entrada:N0} (cache {Cache?.ToString("N0") ?? "não informado"}) · saída {saida:N0} · {TurnosMedidos} turno(s) medido(s)"
            : "Tokens sem medição registrada pelo Codex.");
        if (Ultima is { } atual)
        {
            linhas.Add($"Cota da conta {atual.Plano} · leitura {atual.Hora.LocalDateTime:G}");
            foreach (var minutos in atual.Janelas.Select(j => j.Minutos).Append(300).Append(10080).Distinct().Order())
            {
                var nome = minutos switch { 300 => "5 horas", 10080 => "Semana", _ => $"{minutos} minutos" };
                var j = atual.Janelas.FirstOrDefault(x => x.Minutos == minutos);
                var antes = Inicial?.Janelas.FirstOrDefault(x => x.Minutos == minutos);
                if (j is null) { linhas.Add($"{nome} · janela não informada pela conta"); continue; }
                var comparavel = antes is not null && Inicial?.Hora < atual.Hora && !JanelasInterrompidas.Contains(minutos) && Inicial?.Conta is { } conta && conta == atual.Conta &&
                    antes.Reinicia.HasValue && antes.Reinicia == j.Reinicia && j.Usada >= antes.Usada;
                linhas.Add(comparavel ? $"{nome} · {antes!.Usada:0.##}% → {j.Usada:0.##}% · variação da conta {j.Usada - antes.Usada:0.##} p.p."
                    : $"{nome} · {j.Usada:0.##}% usados · sem amostras comparáveis para a variação");
            }
            if (Fim is not null && !CotaFinal) linhas.Add("A última leitura disponível é parcial.");
        }
        else
        {
            var situacao = Fim.HasValue ? "sem leitura registrada" : "aguardando leitura da conta";
            linhas.Add($"5 horas · {situacao}");
            linhas.Add($"Semana · {situacao}");
        }
        if (FalhaCota is { Length: > 0 }) linhas.Add(FalhaCota);
        if (Origem is { Length: > 0 }) linhas.Add(Origem);
        return string.Join(Environment.NewLine, linhas);
    }
}

internal sealed record EventoDoPlugin(DateTimeOffset Hora, string Tipo, string Titulo, string Detalhe);

internal sealed class DiarioAnaliseChatGpt : IDisposable
{
    internal const string Pasta = "monitor-editor";
    internal const string Sufixo = ".consumo.json";
    internal static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    readonly object trava = new();
    readonly string arquivoConsumo;
    readonly StreamWriter eventos;
    readonly ConsumoDoPlugin consumo;
    readonly Action<string> avisar;
    readonly Dictionary<string,(DateTime Data,long Bytes)> versoes = new(StringComparer.OrdinalIgnoreCase);

    internal DiarioAnaliseChatGpt(string trabalho, string processo, Action<string> avisar)
    {
        this.avisar = avisar;
        consumo = new(processo, DateTimeOffset.UtcNow);
        var pasta = Directory.CreateDirectory(Path.Combine(trabalho, Pasta)).FullName;
        var nome = consumo.Inicio.ToString("yyyyMMddTHHmmssfffffff") + "-" + Guid.NewGuid().ToString("N");
        arquivoConsumo = Path.Combine(pasta, nome + Sufixo);
        eventos = new(new FileStream(LogDe(arquivoConsumo), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false)) { AutoFlush = true };
        Salvar();
    }

    internal static string LogDe(string arquivo) => arquivo[..^Sufixo.Length] + ".eventos.jsonl";

    internal static IReadOnlyList<(string Arquivo, ConsumoDoPlugin Consumo)> Listar(string pasta)
    {
        if (!Directory.Exists(pasta)) return [];
        var lista = new List<(string Arquivo, ConsumoDoPlugin Consumo)>();
        foreach (var arquivo in Directory.GetFiles(pasta, "*" + Sufixo))
        {
            try {
                using var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (JsonSerializer.Deserialize<ConsumoDoPlugin>(fs) is { } resumo) lista.Add((arquivo,resumo));
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { }
        }
        return lista.OrderByDescending(x => x.Consumo.Inicio).ToList();
    }

    void Salvar()
    {
        var tmp = arquivoConsumo + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(consumo, Json));
        File.Move(tmp, arquivoConsumo, true);
    }

    public void Registrar(string tipo, string titulo, string detalhe = "")
    {
        lock (trava)
        {
            var hora = DateTimeOffset.UtcNow;
            eventos.WriteLine(JsonSerializer.Serialize(new EventoDoPlugin(hora,tipo,titulo,detalhe), Json));
            avisar($"[{hora.LocalDateTime:HH:mm:ss}] {titulo}");
        }
    }

    // O registro técnico fica em AppData. Mascara credenciais que um comando poderia imprimir.
    static readonly Regex Segredos = new("""(?i)(Bearer\s+)[\w.~/+=-]+|(\b(?:access_token|refresh_token|id_token|api[_-]?key|authorization|password)\b["']?\s*[:=]\s*["']?)[^\s,"'\\}]+|\b(?:sk-(?:proj-)?|gh[pousr]_)[A-Za-z0-9_-]{16,}""",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    static string Limpar(string texto) => Segredos.Replace(texto, "$1$2***");

    public void Linha(string original, bool erro)
    {
        var linha = Limpar(original);
        if (erro) { Registrar("stderr", "Diagnóstico do Codex", linha); return; }
        JsonObject? obj;
        try { obj = JsonNode.Parse(linha) as JsonObject; }
        catch (JsonException) { obj = null; }
        var tipo = obj?["type"]?.ToString() ?? "stdout";
        if (tipo == "turn.completed" && obj?["usage"] is JsonObject uso &&
            Inteiro(uso,"input_tokens") is { } entrada && Inteiro(uso,"output_tokens") is { } saida)
        {
            lock (trava)
            {
                consumo.Entrada = (consumo.Entrada ?? 0) + entrada;
                consumo.Saida = (consumo.Saida ?? 0) + saida;
                var cache = Inteiro(uso,"cached_input_tokens");
                if (consumo.TurnosMedidos == 0) consumo.Cache = cache;
                else consumo.Cache += cache;
                consumo.TurnosMedidos++;
                Registrar(tipo, $"Turno concluído · {entrada:N0} tokens de entrada · {saida:N0} de saída", linha);
                Salvar();
            }
            return;
        }
        var item = obj?["item"] as JsonObject;
        var descricao = EventosCodex.Linha(linha) ?? item?["command"]?.ToString() ?? item?["tool"]?.ToString() ?? item?["type"]?.ToString();
        Registrar(tipo, descricao is null ? tipo : $"{tipo} · {descricao[..Math.Min(240,descricao.Length)]}", linha);
    }

    static long? Inteiro(JsonObject obj,string nome) => obj[nome] is JsonValue v && v.TryGetValue<long>(out var n) && n >= 0 ? n : null;

    public void Arquivos(string pasta)
    {
        string[] arquivos;
        try { arquivos = Directory.GetFiles(pasta, "*.json"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Registrar("aviso", "Leitura dos arquivos da análise indisponível", ex.GetType().Name);
            return;
        }
        foreach (var nome in arquivos.Select(Path.GetFileName).OfType<string>())
        {
            if (!(nome.StartsWith("grupo-", StringComparison.OrdinalIgnoreCase) ||
                nome.ToLowerInvariant() is "grupos.json" or "entrada-editor.json" or "fontes-kit.json" or "consulta-qgis.json" or "matriz-evidencias.json" or "auditoria-final.json" or "conferencia.json")) continue;
            try
            {
                var info = new FileInfo(Path.Combine(pasta,nome));
                var versao = (info.LastWriteTimeUtc, info.Length);
                if (versoes.TryGetValue(nome, out var anterior) && versao == anterior) continue;
                using var fs = new FileStream(info.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var doc = JsonDocument.Parse(fs);
                Registrar("arquivo", "Registro da análise · " + nome, Limpar(doc.RootElement.GetRawText()));
                versoes[nome] = versao;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        }
    }

    public async Task MedirAsync(string exe, string pasta, Action<IDictionary<string,string?>> ambiente, string fase, CancellationToken ct)
    {
        try
        {
            var amostra = await AmostraDaConta.ConsultarAsync(exe,pasta,ambiente,ct).ConfigureAwait(false);
            lock (trava)
            {
                if (fase == "inicial") consumo.Inicial = amostra;
                if (consumo.Ultima is { } anterior)
                    foreach (var j in anterior.Janelas)
                    {
                        var nova = amostra.Janelas.FirstOrDefault(n => n.Minutos == j.Minutos);
                        if (anterior.Conta != amostra.Conta || nova is null || nova.Reinicia != j.Reinicia || nova.Usada < j.Usada)
                            consumo.JanelasInterrompidas.Add(j.Minutos);
                    }
                consumo.Ultima = amostra;
                consumo.CotaFinal = fase == "final";
                consumo.FalhaCota = null;
                Registrar("cota", $"Cota da conta · leitura {fase}", JsonSerializer.Serialize(amostra,Json));
                Salvar();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or FormatException)
        {
            if (ct.IsCancellationRequested) return;
            lock (trava)
            {
                consumo.FalhaCota = $"Leitura {fase} de cota indisponível.";
                Registrar("cota", consumo.FalhaCota, ex.GetType().Name);
                Salvar();
            }
        }
    }

    public async Task AcompanharAsync(string exe,string pasta,Action<IDictionary<string,string?>> ambiente,CancellationToken ct)
    {
        var proxima = DateTimeOffset.UtcNow.AddMinutes(1);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                Arquivos(pasta);
                if (DateTimeOffset.UtcNow >= proxima)
                {
                    await MedirAsync(exe,pasta,ambiente,"intermediária",ct).ConfigureAwait(false);
                    proxima = DateTimeOffset.UtcNow.AddMinutes(1);
                }
                await Task.Delay(2000,ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }

    public void Encerrar(string situacao, string? detalhe)
    {
        lock (trava)
        {
            consumo.Estado = situacao;
            consumo.Fim = DateTimeOffset.UtcNow;
            Registrar("fim", situacao, detalhe ?? "");
            Salvar();
        }
    }
    public void Dispose() => eventos.Dispose();
}
