using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Agentes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Ajustes de um agente local (ChatGPT ou Claude), sem nenhum segredo: a chave de API fica só no <c>CofreChaves</c> (DPAPI), nunca aqui.</summary>
public sealed class AjustesAgente
{
    /// <summary>Entra pela conta do programa (padrão) ou pela chave de API guardada.</summary>
    public ModoAcesso Modo { get; set; } = ModoAcesso.Conta;

    /// <summary>O programa escolhido em Ajustes (ChatGPT: <c>codex.exe</c>; Claude: <c>python.exe</c>); null = o padrão, achado pelo editor.</summary>
    public string? Executavel { get; set; }

    /// <summary>A versão do aviso que o usuário aceitou (<see cref="Ajustes.VersaoAviso"/>); null = ainda não aceitou.</summary>
    public int? AvisoVersao { get; set; }

    /// <summary>Quando aceitou o aviso (UTC).</summary>
    public DateTimeOffset? AvisoEm { get; set; }

    /// <summary>O aviso vigente já foi aceito (texto novo, com versão maior, pede de novo).</summary>
    public bool AvisoAceito => AvisoVersao is { } v && v >= Ajustes.VersaoAviso;
}

/// <summary>Ajustes locais do app em <c>&lt;raiz de dados&gt;\ajustes.json</c> (nunca no pacote): módulo e aba ativos, tema, intervalo
/// do salvamento automático, marcas de revisão, recentes, pasta de processos e pasta do kit PMVNI. Arquivo ilegível vira <c>ajustes.corrompido.json</c>
/// (sem sobrescrever outro) e o app segue com o padrão; valores fora do permitido voltam ao padrão.</summary>
public sealed class Ajustes
{
    public const string NomeArquivo = "ajustes.json";
    public static readonly IReadOnlyList<int> Intervalos = [15, 30, 60, 120];
    public static readonly IReadOnlyList<string> Temas = ["sistema", "claro", "escuro"];
    public const int MaxRecentes = 10;

    /// <summary>A versão do texto do aviso dos agentes (desenho §12.8): texto novo pede o aceite de novo.</summary>
    public const int VersaoAviso = 1;

    /// <summary>Os ajustes dos dois agentes locais (sempre os dois, com o padrão no que faltar).</summary>
    public IReadOnlyDictionary<Provedor, AjustesAgente> Agentes { get; } = new Dictionary<Provedor, AjustesAgente>
    {
        [Provedor.ChatGpt] = new(),
        [Provedor.Claude] = new(),
    };

    public Modulo Modulo { get; set; } = Modulo.Relatorios;
    public Dictionary<Modulo, string> AbaPorModulo { get; } = new();
    public string Tema { get; set; } = "sistema";
    public int IntervaloAutosaveS { get; set; } = 30;
    public bool MarcasRevisao { get; set; } = true;
    public List<string> Recentes { get; } = new();
    /// <summary>A pasta de processos escolhida em Ajustes; null = a padrão (<see cref="PastaProcessosPadrao"/>).</summary>
    public string? PastaProcessos { get; set; }

    /// <summary>A pasta de processos do usuário neste computador (<c>%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\PROCESSOS</c>).</summary>
    public static string PastaProcessosPadrao { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "PMVNI", "SIDNEY JUNIOR", "PROCESSOS");

    /// <summary>A pasta que Processos e Despachos leem: a escolhida em Ajustes ou, sem escolha, a padrão.</summary>
    public string PastaProcessosEfetiva => string.IsNullOrWhiteSpace(PastaProcessos) ? PastaProcessosPadrao : PastaProcessos;

    /// <summary>A pasta do kit PMVNI escolhida em Ajustes (de onde os modelos de despacho são lidos, no lugar); null = a padrão.</summary>
    public string? PastaKit { get; set; }

    /// <summary>O kit PMVNI neste computador (<c>%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)</c>).</summary>
    public static string PastaKitPadrao { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "PMVNI", "SIDNEY JUNIOR", "KIT DE TREINAMENTO DE IA (PMVNI)");

    /// <summary>O kit que o Novo despacho lê: <c>PMVNI_KIT_RAIZ</c> (testes) se definida, senão a escolhida em Ajustes, senão a padrão.</summary>
    public string PastaKitEfetiva =>
        Environment.GetEnvironmentVariable("PMVNI_KIT_RAIZ") is { } env && !string.IsNullOrWhiteSpace(env) ? env
        : string.IsNullOrWhiteSpace(PastaKit) ? PastaKitPadrao : PastaKit;

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
        a.PastaKit = Texto(j, "pasta_kit");
        if (j["agentes"] is JsonObject agentes)
            foreach (var p in Enum.GetValues<Provedor>())
                if (agentes[Provedores.Sufixo(p)] is JsonObject o) LerAgente(o, a.Agentes[p]);
        return a;
    }

    /// <summary>Põe o arquivo no topo dos recentes (sem repetir, ignorando maiúsculas; no máximo <see cref="MaxRecentes"/>).</summary>
    public void AdicionarRecente(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return;
        Recentes.RemoveAll(r => string.Equals(r, caminho, StringComparison.OrdinalIgnoreCase));
        Recentes.Insert(0, caminho);
        if (Recentes.Count > MaxRecentes) Recentes.RemoveRange(MaxRecentes, Recentes.Count - MaxRecentes);
    }

    /// <summary>Os recentes que ainda existem, do mais novo ao mais antigo: o que sumiu não aparece (nada fica desabilitado). A lista
    /// guardada não muda: um arquivo numa unidade que está fora do ar pode voltar.</summary>
    public IReadOnlyList<string> RecentesExistentes() => Recentes.Where(r => File.Exists(r)).ToList();

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
            ["pasta_kit"] = PastaKit,
            ["agentes"] = new JsonObject(Agentes.Select(par => KeyValuePair.Create(Provedores.Sufixo(par.Key), (JsonNode?)EscreverAgente(par.Value)))),
        };
        var arquivo = Path.Combine(raizDados, NomeArquivo);
        var tmp = arquivo + ".tmp";
        File.WriteAllText(tmp, j.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, arquivo, overwrite: true);
    }

    // Só valores conhecidos entram; qualquer outra coisa fica no padrão. Nenhuma chave de API é lida nem escrita aqui.
    static void LerAgente(JsonObject o, AjustesAgente a)
    {
        if (Texto(o, "modo") is { } m)
            a.Modo = m == "chave" ? ModoAcesso.Chave : ModoAcesso.Conta;
        a.Executavel = Texto(o, "executavel") is { } e && !string.IsNullOrWhiteSpace(e) ? e : null;
        if (o["aviso_versao"] is JsonValue v && v.TryGetValue<int>(out var versao) && versao > 0) a.AvisoVersao = versao;
        if (Texto(o, "aviso_em") is { } em && DateTimeOffset.TryParse(em, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var quando)) a.AvisoEm = quando;
    }

    static JsonObject EscreverAgente(AjustesAgente a) => new()
    {
        ["modo"] = a.Modo == ModoAcesso.Chave ? "chave" : "conta",
        ["executavel"] = a.Executavel,
        ["aviso_versao"] = a.AvisoVersao,
        ["aviso_em"] = a.AvisoEm?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
    };

    static string? Texto(JsonObject j, string nome) => j[nome] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
