using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>O perfil de execução do análise Google, fixo do início ao fim de cada análise: a conta Google pelo Antigravity (plano Ultra), a conta Google
/// pelo login do Gemini (cota do plano), a chave do Google AI Studio com créditos (camada paga) ou a chave gratuita (só testes com dados fictícios).</summary>
public enum PerfilGemini { ContaUltra, ApiCreditos, ApiGratuita, ContaGemini }

/// <summary>Uma etapa da matriz: o modelo, o nível de raciocínio e o identificador que o runtime (<c>agy</c>) aceita e informa (<c>modelo-nível</c>).</summary>
public sealed record EtapaGemini(string Etapa, string Modelo, string Esforco)
{
    public string IdRuntime => Modelo + "-" + Esforco;
}

/// <summary>A matriz de modelos e níveis do análise Google por perfil e etapa (cópia tipada do padrão do kit: a rota gemini de
/// <c>references/modelos-por-rota.json</c> do plugin, que é quem executa; um teste compara as duas). Só as etapas de IA (análise, auditoria técnica e autocorreção): o preparo (<c>prepare</c>),
/// a auditoria cruzada (<c>audit</c>), a montagem (<c>produce</c>) e a revisão final (<c>final_review</c>) são de programa (<c>executor: programa</c> no <c>etapas.json</c>, analise 4.0.0) e não têm modelo nem célula na tabela. O ajuste local de "Modelos da análise" pode trocar
/// uma célula neste computador; o kit confere os modelos no pré-voo. O editor só a usa para mostrar e conferir: nunca troca modelo, nível, modo ou perfil sozinho.</summary>
public static class MatrizGemini
{
    public const string Hydrogen = "gemini-4-hydrogen";
    public const string Flash = "gemini-3.8-flash";
    public const string Pro = "gemini-3.1-pro";
    public const string Carbon = "gemini-4-carbon";
    public const string Argon = "gemini-4-argon";

    /// <summary>Limite da resposta sem tela nas etapas de mérito pela conta (segundos); só o que o runtime aceita (<c>--print-timeout</c>).</summary>
    public const int TimeoutContaSegundos = 2400;

    /// <summary>Limite de saída pedido pela API nas etapas de mérito. O <c>agy</c> não expõe esse parâmetro: o plugin registra o pedido e que não foi aplicado.</summary>
    public const int MaxSaidaApi = 131072;

    // Fluxo linear (etapas.json versão 2): a autocorreção usa uma sessão separada das etapas de mérito; audit, produce e final_review são de programa.
    static readonly EtapaGemini[] Antigravity =
    {
        new("analyze", Flash, "medium"), new("technical_audit", Flash, "high"), new("autocorrecao", Flash, "medium"),
    };

    static readonly EtapaGemini[] ApiCreditos =
    {
        new("analyze", Carbon, "medium"), new("technical_audit", Argon, "medium"), new("autocorrecao", Carbon, "medium"),
    };

    static readonly EtapaGemini[] Web =
    {
        new("analyze", Hydrogen, "medium"), new("technical_audit", Hydrogen, "high"), new("autocorrecao", Hydrogen, "medium"),
    };

    public static IReadOnlyList<EtapaGemini> Etapas(PerfilGemini perfil) => perfil switch
    {
        PerfilGemini.ContaGemini => Web,
        PerfilGemini.ApiCreditos => ApiCreditos,
        _ => Antigravity,
    };

    public static IReadOnlyList<string> Esforcos(string modelo) => modelo switch
    {
        Flash => new[] { "low", "medium", "high" },
        Hydrogen => new[] { "medium", "high" },
        Pro => new[] { "high" },
        Carbon or Argon => new[] { "medium" },
        _ => Array.Empty<string>(),
    };

    /// <summary>As etapas de mérito, que recebem o tempo e a saída estendidos.</summary>
    public static bool EtapaDeMerito(string etapa) => etapa is "analyze" or "technical_audit";

    public static string Codigo(PerfilGemini p) => p switch
    {
        PerfilGemini.ContaUltra => "conta_ultra", PerfilGemini.ContaGemini => "conta_gemini", PerfilGemini.ApiCreditos => "api_creditos", _ => "api_gratuita",
    };

    public static PerfilGemini? DoCodigo(string? codigo) => codigo switch
    {
        "conta_ultra" => PerfilGemini.ContaUltra, "conta_gemini" => PerfilGemini.ContaGemini, "api_creditos" => PerfilGemini.ApiCreditos, "api_gratuita" => PerfilGemini.ApiGratuita, _ => null,
    };

    public static string Nome(PerfilGemini p) => p switch
    {
        PerfilGemini.ContaUltra => "Conta Google (plano Ultra)",
        PerfilGemini.ContaGemini => "Conta Google (login do Gemini)",
        PerfilGemini.ApiCreditos => "API com créditos (Google AI Studio)",
        _ => "API gratuita (só testes fictícios)",
    };

    /// <summary>A rota que o perfil usa: <c>conta</c> (Antigravity), <c>web</c> (login do Gemini) ou <c>api</c>.</summary>
    public static string Rota(PerfilGemini p) => p switch
    {
        PerfilGemini.ContaUltra => "conta",
        PerfilGemini.ContaGemini => "web",
        _ => "api",
    };

    public static ModoAcesso Modo(PerfilGemini p) => p switch
    {
        PerfilGemini.ContaUltra => ModoAcesso.Conta,
        PerfilGemini.ContaGemini => ModoAcesso.ContaGemini,
        _ => ModoAcesso.Chave,
    };

    /// <summary>O perfil que vale para o modo escolhido: conta ⇒ conta Ultra; conta Gemini ⇒ login do Gemini; chave ⇒ o perfil de API escolhido (null = ainda não escolhido,
    /// e o editor pede a escolha: o plano de uma chave não é dedutível e nada é presumido).</summary>
    public static PerfilGemini? PerfilDoModo(ModoAcesso modo, PerfilGemini? perfilApi) =>
        modo == ModoAcesso.Conta ? PerfilGemini.ContaUltra
        : modo == ModoAcesso.ContaGemini ? PerfilGemini.ContaGemini
        : perfilApi is PerfilGemini.ApiCreditos or PerfilGemini.ApiGratuita ? perfilApi : null;

    /// <summary>"Análise: Carbon medium · Auditoria técnica: Argon medium · …", para a tela.</summary>
    public static string Resumo(PerfilGemini p) =>
        string.Join(" · ", Etapas(p).Select(e => $"{SituacaoAnalisePro.NomeEtapa(e.Etapa)}: {e.Modelo.Replace("gemini-4-", "", StringComparison.Ordinal)} {e.Esforco}"));

    /// <summary>Tarefa 19: lê a matriz da rota gemini da tabela única do kit (<c>references\modelos-por-rota.json</c>: <c>rotas.gemini.perfis.&lt;perfil&gt;.etapas
    /// .&lt;etapa&gt;</c> = <c>{modelo, nivel}</c>, com o nível traduzido pela <c>equivalencia</c>): perfil → etapas. Fora do formato ⇒ null.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<EtapaGemini>>? LerDaTabela(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("equivalencia", out var eq) || eq.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("rotas", out var rotas) || rotas.ValueKind != JsonValueKind.Object
                || !rotas.TryGetProperty("gemini", out var g) || g.ValueKind != JsonValueKind.Object
                || !g.TryGetProperty("perfis", out var perfis) || perfis.ValueKind != JsonValueKind.Object) return null;
            var saida = new Dictionary<string, IReadOnlyList<EtapaGemini>>(StringComparer.Ordinal);
            foreach (var perfil in perfis.EnumerateObject())
            {
                if (!perfil.Value.TryGetProperty("etapas", out var etapas) || etapas.ValueKind != JsonValueKind.Object) return null;
                var lista = new List<EtapaGemini>();
                foreach (var etapa in etapas.EnumerateObject())
                {
                    if (!etapa.Value.TryGetProperty("modelo", out var m) || m.GetString() is not { } modelo
                        || !etapa.Value.TryGetProperty("nivel", out var n) || n.GetString() is not { } nivel
                        || !eq.TryGetProperty(nivel, out var e) || e.GetString() is not { } esforco) return null;
                    lista.Add(new EtapaGemini(etapa.Name, modelo, esforco));
                }
                saida[perfil.Name] = lista;
            }
            return saida;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException) { return null; }
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
