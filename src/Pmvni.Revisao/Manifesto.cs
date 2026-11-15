using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>Conteúdo do <c>manifesto.json</c> do pacote: versão do esquema, quem gerou, o modelo (revisão e sha256 do CSS) e o sha256 de cada parte
/// (<c>original/recebido.json</c>, <c>projeto.json</c>, <c>historico.jsonl</c>, <c>assets/…</c>).</summary>
public sealed record Manifesto(
    string VersaoEsquema,
    string AppVersao,
    int TemplateRevisao,
    string Sha256Css,
    DateTimeOffset CriadoEm,
    IReadOnlyDictionary<string, string> Partes)
{
    public const string VersaoEsquemaAtual = "1.0.0";

    internal const string FormatoData = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";

    public static Manifesto Ler(byte[] utf8)
    {
        var j = JsonFiel.Ler(utf8);
        try
        {
            var partes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (nome, valor) in j["partes"]!.AsObject()) partes[nome] = valor!.GetValue<string>();
            return new Manifesto(
                j["versao_esquema"]!.GetValue<string>(),
                j["app_versao"]!.GetValue<string>(),
                j["template_revisao"]!.GetValue<int>(),
                j["sha256_css"]!.GetValue<string>(),
                DataUtc(j["criado_em"]!.GetValue<string>()),
                partes);
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException or FormatException)
        {
            throw new JsonException("manifesto.json inválido: " + e.Message, e);
        }
    }

    public byte[] Escrever()
    {
        var partes = new JsonObject();
        foreach (var (nome, sha) in Partes) partes[nome] = sha;
        return JsonFiel.Escrever(new JsonObject
        {
            ["versao_esquema"] = VersaoEsquema,
            ["app_versao"] = AppVersao,
            ["template_revisao"] = TemplateRevisao,
            ["sha256_css"] = Sha256Css,
            ["criado_em"] = TextoUtc(CriadoEm),
            ["partes"] = partes,
        });
    }

    /// <summary>Instante em UTC, com 7 casas de fração (sem perda) e o sufixo Z.</summary>
    internal static string TextoUtc(DateTimeOffset t) => t.UtcDateTime.ToString(FormatoData, CultureInfo.InvariantCulture);

    internal static DateTimeOffset DataUtc(string texto) =>
        DateTimeOffset.ParseExact(texto, FormatoData, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
