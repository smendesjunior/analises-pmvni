using System.Security.Cryptography;
using System.Text.Json;
using Pmvni.Composicao.Kit;

namespace Pmvni.App;

internal sealed record AchadoExecucao(string Id, long Sequencia, string Titulo, string Situacao, bool Verificado);

internal sealed record ProgressoEtapa(string Etapa, long Sequencia, int Avaliados, int Total);
internal sealed record PainelAchados(List<AchadoExecucao> Itens, Dictionary<string, ProgressoEtapa> Etapas);

internal static class AchadosExecucao
{
    public static PainelAchados Ler(string pasta, string processo)
    {
        var ultimos = new Dictionary<string, AchadoExecucao>(StringComparer.Ordinal);
        var etapas = new Dictionary<string, ProgressoEtapa>(StringComparer.Ordinal);
        if (!Directory.Exists(pasta) || new DirectoryInfo(pasta).LinkTarget is not null) return new([], etapas);
        foreach (var arquivo in new DirectoryInfo(pasta).EnumerateFiles("*.json").OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            try
            {
                if (arquivo.LinkTarget is not null || arquivo.Length > 128 * 1024) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(arquivo.FullName));
                var e = doc.RootElement;
                string? Texto(string nome) => e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (e.ValueKind != JsonValueKind.Object) continue;
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
        return new(ultimos.Values.OrderBy(a => a.Id, StringComparer.Ordinal).ToList(), etapas);
    }

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
