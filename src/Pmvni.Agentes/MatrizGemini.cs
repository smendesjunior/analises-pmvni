using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>O perfil de execução do análise Google, fixo do início ao fim de cada análise: a conta Google (plano Ultra, pelo login do Antigravity), a chave
/// do Google AI Studio com créditos (camada paga) ou a chave gratuita (só testes com dados fictícios: a camada gratuita pode usar o conteúdo para melhorar
/// os produtos do Google).</summary>
public enum PerfilGemini { ContaUltra, ApiCreditos, ApiGratuita }

/// <summary>Uma etapa da matriz: o modelo, o nível de raciocínio e o identificador que o runtime (<c>agy</c>) aceita e informa (<c>modelo-nível</c>).</summary>
public sealed record EtapaGemini(string Etapa, string Modelo, string Esforco)
{
    public string IdRuntime => Modelo + "-" + Esforco;
}

/// <summary>A matriz de modelos e níveis do análise Google por perfil e etapa (cópia tipada de <c>references/matriz-gemini.json</c> do plugin, que é quem
/// executa; um teste compara as duas). O editor só a usa para mostrar e conferir: nunca troca modelo, nível, modo ou perfil sozinho.</summary>
public static class MatrizGemini
{
    public const string Hydrogen = "gemini-4-hydrogen";
    public const string Carbon = "gemini-4-carbon";
    public const string Argon = "gemini-4-argon";

    /// <summary>Limite da resposta sem tela nas etapas de mérito pela conta (segundos); só o que o runtime aceita (<c>--print-timeout</c>).</summary>
    public const int TimeoutContaSegundos = 2400;

    /// <summary>Limite de saída pedido pela API nas etapas de mérito. O <c>agy</c> não expõe esse parâmetro: o plugin registra o pedido e que não foi aplicado.</summary>
    public const int MaxSaidaApi = 131072;

    static readonly EtapaGemini[] Pagos =
    {
        new("prepare", Hydrogen, "high"), new("analyze", Carbon, "high"), new("technical_audit", Carbon, "high"),
        new("produce", Hydrogen, "medium"), new("audit", Argon, "high"), new("final_review", Carbon, "high"),
    };

    static readonly EtapaGemini[] Gratuito =
    {
        new("prepare", Hydrogen, "high"), new("analyze", Hydrogen, "xhigh"), new("technical_audit", Hydrogen, "xhigh"),
        new("produce", Hydrogen, "medium"), new("audit", Hydrogen, "xhigh"), new("final_review", Hydrogen, "xhigh"),
    };

    /// <summary>As seis etapas, na ordem do controlador do kit.</summary>
    public static IReadOnlyList<EtapaGemini> Etapas(PerfilGemini perfil) => perfil == PerfilGemini.ApiGratuita ? Gratuito : Pagos;

    /// <summary>Os níveis que cada modelo aceita no runtime (conferido em <c>agy models</c>, 1.5.10): Carbon e Argon até <c>high</c>; Hydrogen até <c>xhigh</c>.</summary>
    public static IReadOnlyList<string> Esforcos(string modelo) =>
        modelo == Hydrogen ? new[] { "low", "medium", "high", "xhigh" } : new[] { "low", "medium", "high" };

    /// <summary>As etapas de mérito, que recebem o tempo e a saída estendidos.</summary>
    public static bool EtapaDeMerito(string etapa) => etapa is "analyze" or "technical_audit";

    public static string Codigo(PerfilGemini p) => p switch
    {
        PerfilGemini.ContaUltra => "conta_ultra", PerfilGemini.ApiCreditos => "api_creditos", _ => "api_gratuita",
    };

    public static PerfilGemini? DoCodigo(string? codigo) => codigo switch
    {
        "conta_ultra" => PerfilGemini.ContaUltra, "api_creditos" => PerfilGemini.ApiCreditos, "api_gratuita" => PerfilGemini.ApiGratuita, _ => null,
    };

    public static string Nome(PerfilGemini p) => p switch
    {
        PerfilGemini.ContaUltra => "Conta Google (plano Ultra)",
        PerfilGemini.ApiCreditos => "API com créditos (Google AI Studio)",
        _ => "API gratuita (só testes fictícios)",
    };

    /// <summary>A rota que o perfil usa: <c>conta</c> ou <c>api</c>.</summary>
    public static string Rota(PerfilGemini p) => p == PerfilGemini.ContaUltra ? "conta" : "api";

    public static ModoAcesso Modo(PerfilGemini p) => p == PerfilGemini.ContaUltra ? ModoAcesso.Conta : ModoAcesso.Chave;

    /// <summary>O perfil que vale para o modo escolhido: conta ⇒ sempre a conta Ultra; chave ⇒ o perfil de API escolhido (null = ainda não escolhido,
    /// e o editor pede a escolha: o plano de uma chave não é dedutível e nada é presumido).</summary>
    public static PerfilGemini? PerfilDoModo(ModoAcesso modo, PerfilGemini? perfilApi) =>
        modo == ModoAcesso.Conta ? PerfilGemini.ContaUltra : perfilApi is PerfilGemini.ApiCreditos or PerfilGemini.ApiGratuita ? perfilApi : null;

    /// <summary>"Preparo: Hydrogen high · Análise: Carbon high · …", para a tela.</summary>
    public static string Resumo(PerfilGemini p) =>
        string.Join(" · ", Etapas(p).Select(e => $"{SituacaoAnalisePro.NomeEtapa(e.Etapa)}: {e.Modelo.Replace("gemini-4-", "", StringComparison.Ordinal)} {e.Esforco}"));

    /// <summary>Lê a matriz do plugin (<c>matriz-gemini.json</c>): perfil → etapas. JSON ilegível ou fora do formato ⇒ null.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<EtapaGemini>>? LerDoKit(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("perfis", out var perfis) || perfis.ValueKind != JsonValueKind.Object) return null;
            var saida = new Dictionary<string, IReadOnlyList<EtapaGemini>>(StringComparer.Ordinal);
            foreach (var perfil in perfis.EnumerateObject())
            {
                if (perfil.Value.ValueKind != JsonValueKind.Object) return null;
                var etapas = new List<EtapaGemini>();
                foreach (var etapa in perfil.Value.EnumerateObject())
                {
                    if (etapa.Value.ValueKind != JsonValueKind.Array || etapa.Value.GetArrayLength() != 2) return null;
                    if (etapa.Value[0].GetString() is not { } modelo || etapa.Value[1].GetString() is not { } esforco) return null;
                    etapas.Add(new EtapaGemini(etapa.Name, modelo, esforco));
                }
                saida[perfil.Name] = etapas;
            }
            return saida;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException) { return null; }
    }
}

/// <summary>O modo e o perfil com que uma análise do Gemini começou neste processo (gravados pelo plugin em <c>.pmvni-ai\analise-google</c>): valem para o
/// trabalho inteiro, inclusive na retomada. null = não há análise pendente (ou os arquivos não dizem).</summary>
public static class GeminiFixado
{
    public static PerfilGemini? Ler(string pastaProcesso)
    {
        var controle = Path.Combine(pastaProcesso, ResultadoAnalise.PastaControle(Provedor.Gemini));
        if (Campo(Path.Combine(controle, "auto-status.json"), "situacao") is "CONCLUIDO") return null;     // trabalho encerrado: nada a retomar
        if (!File.Exists(Path.Combine(controle, "engine_state.json"))) return null;                        // nenhuma etapa preparada: nada fixado ainda
        return MatrizGemini.DoCodigo(Campo(Path.Combine(controle, "engine_state.json"), "perfil"))
               ?? MatrizGemini.DoCodigo(Campo(Path.Combine(controle, "auto-status.json"), "perfil"));
    }

    static string? Campo(string arquivo, string nome)
    {
        try
        {
            var f = new FileInfo(arquivo);
            if (!f.Exists || f.Length > 16 * 1024 * 1024) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(arquivo));
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
