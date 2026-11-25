using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pmvni.Agentes;

/// <summary>O que a chave do Google AI Studio respondeu na lista de modelos (não gera texto nem gasta cota).</summary>
public sealed record ChaveGemini(bool Aceita, string Mensagem, IReadOnlyList<string> Modelos);

/// <summary>"Testar" do Gemini (Ajustes › Inteligência artificial): o Antigravity CLI (<c>agy --version</c>), o login da conta (<c>agy models</c>: lista
/// vazia = sem login), o plugin análise Google, o Python e, no modo chave, a conferência real da chave na API do Google (<c>GET /v1beta/models</c>, com a
/// chave só no cabeçalho). Nada pede texto ao modelo. A lista do modo conta e a da chave dizem quais modelos e níveis de cada etapa existem de fato.</summary>
public static class DiagnosticoGemini
{
    public const string EnderecoModelos = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200";
    public const string VersaoMinimaAgy = "1.5.7";

    static readonly Regex Versao = new(@"(\d+\.\d+\.\d+)", RegexOptions.CultureInvariant);

    public static string? VersaoAgy(string saida) => Versao.Match(saida) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>Os identificadores de <c>agy models</c> (linhas "id&lt;tab&gt;nome"; o resto, como "Fetching available models...", é ignorado).</summary>
    public static IReadOnlyList<string> ModelosDoAgy(string saida)
    {
        var lista = new List<string>();
        foreach (var bruta in saida.Split('\n'))
        {
            var partes = bruta.Trim().Split('\t', 2, StringSplitOptions.TrimEntries);
            if (partes.Length == 2 && partes[0].Length > 0 && partes[1].Length > 0 && !partes[0].Contains(' ') && !lista.Contains(partes[0])) lista.Add(partes[0]);
        }
        return lista;
    }

    /// <summary>As etapas do perfil cujo identificador (<c>modelo-nível</c>) não está na lista do runtime: "Análise (gemini-4-carbon high)".</summary>
    /// <param name="etapas">Tarefa 19: as etapas da tabela efetiva (padrão do kit + ajuste local) do perfil; null = a matriz padrão tipada.</param>
    public static IReadOnlyList<string> EtapasSemModelo(PerfilGemini perfil, IReadOnlyCollection<string> idsDoRuntime, IReadOnlyList<EtapaGemini>? etapas = null) =>
        (etapas ?? MatrizGemini.Etapas(perfil)).Where(e => !idsDoRuntime.Contains(e.IdRuntime, StringComparer.OrdinalIgnoreCase))
            .Select(e => $"{SituacaoAnalisePro.NomeEtapa(e.Etapa)} ({e.Modelo} {e.Esforco})").ToList();

    /// <summary>As etapas do perfil cujo modelo base não está na lista da API (a API lista o modelo sem o nível).</summary>
    public static IReadOnlyList<string> EtapasSemModeloNaApi(PerfilGemini perfil, IReadOnlyCollection<string> modelosDaApi, IReadOnlyList<EtapaGemini>? etapas = null) =>
        (etapas ?? MatrizGemini.Etapas(perfil)).Where(e => !modelosDaApi.Contains(e.Modelo, StringComparer.OrdinalIgnoreCase))
            .Select(e => $"{SituacaoAnalisePro.NomeEtapa(e.Etapa)} ({e.Modelo})").Distinct().ToList();

    public static Task<ChaveGemini> ConferirChaveAsync(string chave, CancellationToken ct) =>
        Task.FromResult(new ChaveGemini(false, Provedores.RotaDesativada, []));

    public static string MensagemDaRecusa(HttpStatusCode codigo, string corpo)
    {
        var n = (int)codigo;
        if (n is 400 or 401 or 403 && (corpo.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) || corpo.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) || n == 401))
            return "chave recusada pelo Google (inválida ou apagada): gere outra no Google AI Studio e guarde-a de novo";
        if (n == 403) return "chave sem permissão para a Gemini API (projeto bloqueado ou API desativada): confira o projeto no Google AI Studio";
        if (n == 429) return "limite de uso da chave atingido agora (cota ou taxa): tente mais tarde ou confira o plano no Google AI Studio";
        return $"a API do Google respondeu {n}; a chave não foi conferida";
    }

    /// <summary>Os modelos de <c>/v1beta/models</c> que geram conteúdo, sem o prefixo <c>models/</c>.</summary>
    public static IReadOnlyList<string> ModelosDaApi(string json)
    {
        var lista = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("models", out var modelos) || modelos.ValueKind != JsonValueKind.Array) return lista;
            foreach (var m in modelos.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("name", out var nome) || nome.ValueKind != JsonValueKind.String) continue;
                var gera = !m.TryGetProperty("supportedGenerationMethods", out var g) || g.ValueKind != JsonValueKind.Array
                           || g.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "generateContent");
                var id = nome.GetString()!.Replace("models/", "", StringComparison.Ordinal);
                if (gera && id.Length > 0 && !lista.Contains(id)) lista.Add(id);
            }
        }
        catch (JsonException) { }
        return lista;
    }

    /// <summary>O teste inteiro. <paramref name="chave"/> só é usada no modo chave (lida do cofre por quem chama) e nunca aparece na linha nem nos avisos.</summary>
    /// <param name="etapasDoPerfil">Tarefa 19: as etapas da tabela efetiva de cada perfil (o que a análise vai pedir); null = a matriz padrão tipada.</param>
    public static Task<SituacaoProvedor> TestarAsync(Pecas pecas, ModoAcesso modo, PerfilGemini? perfilApi, string? chave, CancellationToken ct,
        Func<PerfilGemini, IReadOnlyList<EtapaGemini>?>? etapasDoPerfil = null) =>
        Task.FromResult(new SituacaoProvedor(Provedores.RotaDesativada, []));
}
