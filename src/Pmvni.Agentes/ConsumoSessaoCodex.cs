using System.Globalization;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Contadores acumulados do evento token_count da sessão que o kit vinculou à etapa.
/// Mantém só números e a posição da última linha completa, mesmo com o executor escrevendo.</summary>
public sealed class ConsumoSessaoCodex(string codexHome)
{
    internal sealed record Contadores(long? Entrada, long? Saida, long? Cache, long? Total, double? Cota5h, double? CotaSemana);
    readonly Dictionary<Guid, Leitura> sessoes = [];

    sealed class Leitura
    {
        public long Posicao;
        public Contadores? Uso;
        public Janela CincoHoras = new();
        public Janela Semanal = new();
    }

    sealed class Janela
    {
        double? inicial, renovacao;
        bool renovou;
        public double? Variacao(JsonElement? janela, int minutos)
        {
            if (janela is not { } j || ProgressoLocal.Numero(j, "window_minutes") != minutos ||
                ProgressoLocal.Numero(j, "used_percent") is not { } usado ||
                ProgressoLocal.Numero(j, "resets_at") is not { } fim) return null;
            if (inicial is null) { inicial = usado; renovacao = fim; return null; }
            renovou |= fim != renovacao || usado < inicial;
            return renovou ? null : usado - inicial;
        }
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
        if (bytes.Span.IndexOf("\"token_count\""u8) < 0) return;
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var raiz = doc.RootElement;
            var payload = UsoDasIas.Objeto(raiz, "payload");
            if (raiz.ValueKind != JsonValueKind.Object || ProgressoLocal.Texto(raiz, "type") != "event_msg"
                || payload is not { } p || ProgressoLocal.Texto(p, "type") != "token_count") return;
            var t = UsoDasIas.Objeto(UsoDasIas.Objeto(p, "info"), "total_token_usage");
            var cotas = UsoDasIas.Objeto(p, "rate_limits");
            var anterior = leitura.Uso;
            leitura.Uso = new(UsoDasIas.Inteiro(t, "input_tokens") ?? anterior?.Entrada,
                UsoDasIas.Inteiro(t, "output_tokens") ?? anterior?.Saida,
                UsoDasIas.Inteiro(t, "cached_input_tokens") ?? anterior?.Cache,
                UsoDasIas.Inteiro(t, "total_tokens") ?? anterior?.Total,
                cotas is null ? anterior?.Cota5h : leitura.CincoHoras.Variacao(UsoDasIas.Objeto(cotas, "primary"), 300),
                cotas is null ? anterior?.CotaSemana : leitura.Semanal.Variacao(UsoDasIas.Objeto(cotas, "secondary"), 10080));
        }
        catch (JsonException) { } // Um evento inválido não descarta os anteriores.
    }
}
