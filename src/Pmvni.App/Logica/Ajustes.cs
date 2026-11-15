using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Ajustes locais do app em <c>&lt;raiz de dados&gt;\ajustes.json</c> (nunca no pacote): módulo e aba ativos, tema, intervalo
/// do salvamento automático, marcas de revisão, recentes e pasta de processos. Arquivo ilegível vira <c>ajustes.corrompido.json</c>
/// (sem sobrescrever outro) e o app segue com o padrão; valores fora do permitido voltam ao padrão.</summary>
public sealed class Ajustes
{
    public const string NomeArquivo = "ajustes.json";
    public static readonly IReadOnlyList<int> Intervalos = [15, 30, 60, 120];
    public static readonly IReadOnlyList<string> Temas = ["sistema", "claro", "escuro"];
    public const int MaxRecentes = 10;

    public Modulo Modulo { get; set; } = Modulo.Relatorios;
    public Dictionary<Modulo, string> AbaPorModulo { get; } = new();
    public string Tema { get; set; } = "sistema";
    public int IntervaloAutosaveS { get; set; } = 30;
    public bool MarcasRevisao { get; set; } = true;
    public List<string> Recentes { get; } = new();
    public string? PastaProcessos { get; set; }

    public static Ajustes Carregar(string raizDados)
    {
        var arquivo = Path.Combine(raizDados, NomeArquivo);
        if (!File.Exists(arquivo)) return new Ajustes();
        JsonObject j;
        try { j = JsonNode.Parse(File.ReadAllText(arquivo)) as JsonObject ?? throw new JsonException("não é um objeto"); }
        catch (JsonException)
        {
            File.Move(arquivo, NomesArquivo.Livre(raizDados, "ajustes.corrompido.json"));
            return new Ajustes();
        }

        var a = new Ajustes();
        if (Texto(j, "modulo") is { } m && Enum.TryParse<Modulo>(m, out var modulo) && Enum.IsDefined(modulo)) a.Modulo = modulo;
        if (j["aba_por_modulo"] is JsonObject abas)
            foreach (var (chave, valor) in abas)
                if (Enum.TryParse<Modulo>(chave, out var mm) && Enum.IsDefined(mm) && valor is JsonValue v && v.TryGetValue<string>(out var aba))
                    a.AbaPorModulo[mm] = aba;
        if (Texto(j, "tema") is { } t && Temas.Contains(t)) a.Tema = t;
        if (j["intervalo_autosave_s"] is JsonValue iv && iv.TryGetValue<int>(out var s) && Intervalos.Contains(s)) a.IntervaloAutosaveS = s;
        if (j["marcas_revisao"] is JsonValue mv && mv.TryGetValue<bool>(out var marcas)) a.MarcasRevisao = marcas;
        if (j["recentes"] is JsonArray rec)
            foreach (var r in rec)
                if (r is JsonValue rv && rv.TryGetValue<string>(out var c) && !string.IsNullOrWhiteSpace(c) && a.Recentes.Count < MaxRecentes) a.Recentes.Add(c);
        a.PastaProcessos = Texto(j, "pasta_processos");
        return a;
    }

    /// <summary>Grava num temporário ao lado e troca: o arquivo nunca fica pela metade.</summary>
    public void Salvar(string raizDados)
    {
        Directory.CreateDirectory(raizDados);
        var abas = new JsonObject();
        foreach (var (m, aba) in AbaPorModulo.OrderBy(p => p.Key)) abas[m.ToString()] = aba;
        var j = new JsonObject
        {
            ["modulo"] = Modulo.ToString(),
            ["aba_por_modulo"] = abas,
            ["tema"] = Tema,
            ["intervalo_autosave_s"] = IntervaloAutosaveS,
            ["marcas_revisao"] = MarcasRevisao,
            ["recentes"] = new JsonArray(Recentes.Take(MaxRecentes).Select(r => (JsonNode)r).ToArray()),
            ["pasta_processos"] = PastaProcessos,
        };
        var arquivo = Path.Combine(raizDados, NomeArquivo);
        var tmp = arquivo + ".tmp";
        File.WriteAllText(tmp, j.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, arquivo, overwrite: true);
    }

    static string? Texto(JsonObject j, string nome) => j[nome] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
