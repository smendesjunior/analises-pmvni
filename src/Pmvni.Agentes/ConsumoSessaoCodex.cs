using System.Globalization;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Contadores acumulados do evento token_count da sessão que o kit vinculou à etapa.
/// Mantém só números e a posição da última linha completa, mesmo com o executor escrevendo.</summary>
public sealed class ConsumoSessaoCodex(string codexHome)
{
    internal sealed record Contadores(long? Entrada, long? Saida, long? Cache, long? Total, double? Cota5h, double? CotaSemana, double? Usada5h, double? UsadaSemana)
    {
        public string? Plano { get; init; }
        internal string? Conta { get; init; }
    }
    readonly Dictionary<Guid, Leitura> sessoes = [];

    sealed class Leitura
    {
        public long Posicao;
        public Contadores? Uso;
        public string? ContaDireta;
        public string? ContaMedida;
        public bool ContaIndeterminada;
        public Janela CincoHoras = new();
        public Janela Semanal = new();
    }

    sealed class Janela
    {
        public double? Usada { get; private set; }
        double? inicial, renovacao, anterior;
        bool intervaloIncomparavel;
        public double? Variacao(JsonElement? janela, int minutos)
        {
            if (janela is not { } j || ProgressoLocal.Numero(j, "window_minutes") != minutos ||
                ProgressoLocal.Numero(j, "used_percent") is not { } usado || usado is < 0 or > 100 ||
                ProgressoLocal.Numero(j, "resets_at") is not { } fim) return null;
            if (inicial.HasValue && (usado < anterior || !UsoDasIas.MesmaRenovacao(renovacao!.Value, fim)))
                intervaloIncomparavel = true;
            Usada = usado;
            anterior = usado;
            if (inicial is null) { inicial = usado; renovacao = fim; return null; }
            return intervaloIncomparavel ? null : usado - inicial;
        }
    }

    internal Contadores? LerEtapa(string controle, string etapa, JsonElement? principal, DateTime desde)
    {
        var contadores = new List<Contadores>();
        var vistos = new HashSet<Guid>();
        void Somar(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.Object && Guid.TryParse(ProgressoLocal.Texto(item, "id"), out var id)
                && !vistos.Contains(id) && Ler(item) is { } medido)
            {
                vistos.Add(id);
                contadores.Add(medido);
            }
        }
        void SomarGrupo(JsonElement sessao, string grupo)
        {
            if (sessao.TryGetProperty(grupo, out var lista) && lista.ValueKind == JsonValueKind.Array)
                foreach (var item in lista.EnumerateArray()) Somar(item);
        }
        if (etapa == "analyze" && DetalhesDasEtapas.PastaAtualDasFrentes(controle, desde) is { } partes)
        {
            foreach (var (inicio, _) in DetalhesDasEtapas.Blocos)
            {
                var arquivo = Path.Combine(partes, $"frente-{inicio:00}", "sessao_monitor.json");
                if (ArquivoJsonDoKit.Ler(arquivo, desde) is not { ValueKind: JsonValueKind.Object } frente) continue;
                SomarGrupo(frente, "sessoes");
                Somar(frente);
            }
        }
        if (principal is { } p)
        {
            SomarGrupo(p, "blocos_monitor");
            Somar(p);
        }
        if (contadores.Count < 2) return contadores.FirstOrDefault();
        long? Total(Func<Contadores, long?> campo) => contadores.Any(x => campo(x).HasValue) ? contadores.Sum(x => campo(x)) : null;
        var planos = contadores.Select(x => x.Plano).OfType<string>().Distinct().ToList();
        // Cotas pertencem às contas e os intervalos das sessões podem se sobrepor.
        return new(Total(c => c.Entrada), Total(c => c.Saida), Total(c => c.Cache), Total(c => c.Total), null, null, null, null)
        { Plano = planos.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, planos) };
    }

    internal Contadores? Ler(JsonElement sessao)
    {
        if (!Guid.TryParseExact(ProgressoLocal.Texto(sessao, "id"), "D", out var id)
            || ProgressoLocal.Instante(sessao, "inicio") is not { } inicio) return null;
        try
        {
            var pasta = Path.Combine(codexHome, "sessions", inicio.LocalDateTime.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            if (!Directory.Exists(pasta)) return null;
            var arquivo = Directory.EnumerateFiles(pasta, $"rollout-*-{id:D}.jsonl").FirstOrDefault();
            if (arquivo is null) return null;
            using var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (!sessoes.TryGetValue(id, out var leitura) || fs.Length < leitura.Posicao)
                sessoes[id] = leitura = new Leitura();
            if (fs.Length == leitura.Posicao) return leitura.Uso;
            fs.Position = leitura.Posicao;
            var buffer = new byte[16384];
            using var linha = new MemoryStream();
            bool longa = false;
            int n;
            while ((n = fs.Read(buffer)) > 0)
            {
                int pos = 0;
                while (pos < n)
                {
                    var fim = Array.IndexOf(buffer, (byte)'\n', pos, n - pos);
                    var tamanho = (fim < 0 ? n : fim) - pos;
                    longa |= linha.Length + tamanho > 65536;
                    if (!longa) linha.Write(buffer, pos, tamanho);
                    if (fim < 0) break;
                    if (!longa) Registrar(linha, leitura);
                    leitura.Posicao = fs.Position - n + fim + 1;
                    linha.SetLength(0);
                    longa = false;
                    pos = fim + 1;
                }
            }
            return leitura.Uso;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static void Registrar(MemoryStream linha, Leitura leitura)
    {
        var bytes = linha.GetBuffer().AsMemory(0, (int)linha.Length);
        if (bytes.Span.IndexOf("\"token_count\""u8) < 0 && bytes.Span.IndexOf("update_plan"u8) < 0 && bytes.Span.IndexOf("\"session_meta\""u8) < 0) return;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var raiz = doc.RootElement;
            var payload = UsoDasIas.Objeto(raiz, "payload");
            if (raiz.ValueKind != JsonValueKind.Object) return;
            if (ProgressoLocal.Texto(raiz, "type") == "session_meta" && payload is { } meta)
            {
                leitura.ContaDireta = ProgressoLocal.Texto(meta, "model_provider") == "openai" ? UsoDasIas.Identificador(meta, "creator_account_id") : null;
                return;
            }
            if (ProgressoLocal.Texto(raiz, "type") == "response_item" && payload is { } chamada
                && ProgressoLocal.Texto(chamada, "type") == "function_call"
                && ProgressoLocal.Texto(chamada, "name") is "update_plan" or "functions.update_plan"
                && ProgressoLocal.Texto(chamada, "arguments") is { } argumentos)
            {
                using var args = JsonDocument.Parse(argumentos);
                if (args.RootElement.ValueKind == JsonValueKind.Object && args.RootElement.TryGetProperty("plan", out var plano)
                    && plano.ValueKind == JsonValueKind.Array)
                {
                    var linhas = new List<string>();
                    foreach (var passo in plano.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
                    {
                        if (ProgressoLocal.Texto(passo, "step") is not { } texto) continue;
                        var estado = ProgressoLocal.Texto(passo, "status") switch {
                            "in_progress" => "Em andamento", "completed" => "Concluído", "pending" => "Pendente", var valor => valor };
                        linhas.Add(string.Join(" · ", new[] { estado, texto }.OfType<string>()));
                    }
                    var contadores = leitura.Uso ?? new Contadores(null, null, null, null, null, null, null, null);
                    leitura.Uso = contadores with { Plano = linhas.Count > 0 ? string.Join(Environment.NewLine, linhas) : null };
                }
                return;
            }
            if (raiz.ValueKind != JsonValueKind.Object || ProgressoLocal.Texto(raiz, "type") != "event_msg"
                || payload is not { } p || ProgressoLocal.Texto(p, "type") != "token_count") return;
            var t = UsoDasIas.Objeto(UsoDasIas.Objeto(p, "info"), "total_token_usage");
            var cotas = UsoDasIas.Objeto(p, "rate_limits");
            // Outros modelos podem ter limites separados. O plano ("plan_type") também não identifica uma conta.
            if (cotas is { } limites && ProgressoLocal.Texto(limites, "limit_id") is { } limite && limite != "codex") cotas = null;
            var conta = UsoDasIas.Identificador(cotas, "account_id") ?? leitura.ContaDireta;
            // A conta do criador de uma conversa do LB pode diferir da conta que atendeu cada pedido.
            var cincoHoras = UsoDasIas.Objeto(cotas, "primary");
            var semanal = UsoDasIas.Objeto(cotas, "secondary");
            if (cincoHoras.HasValue || semanal.HasValue)
            {
                leitura.ContaIndeterminada |= conta is null || leitura.ContaMedida is not null && leitura.ContaMedida != conta;
                leitura.ContaMedida = conta;
            }
            var anterior = leitura.Uso;
            leitura.Uso = new(UsoDasIas.Inteiro(t, "input_tokens") ?? anterior?.Entrada,
                UsoDasIas.Inteiro(t, "output_tokens") ?? anterior?.Saida,
                UsoDasIas.Inteiro(t, "cached_input_tokens") ?? anterior?.Cache,
                UsoDasIas.Inteiro(t, "total_tokens") ?? anterior?.Total,
                leitura.ContaIndeterminada ? null : cincoHoras is null ? anterior?.Cota5h : leitura.CincoHoras.Variacao(cincoHoras, 300),
                leitura.ContaIndeterminada ? null : semanal is null ? anterior?.CotaSemana : leitura.Semanal.Variacao(semanal, 10080),
                leitura.ContaIndeterminada ? null : leitura.CincoHoras.Usada, leitura.ContaIndeterminada ? null : leitura.Semanal.Usada)
                { Plano = anterior?.Plano, Conta = leitura.ContaIndeterminada ? null : leitura.ContaMedida };
        }
        catch (JsonException) { } // Um evento inválido não descarta os anteriores.
    }
}
