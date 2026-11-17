namespace Pmvni.App;

/// <summary>O modelo e o esforço das perguntas curtas à IA na revisão do relatório ("Ajustar texto com a IA", "Rever a tabela com a IA",
/// volumetria): o Codex (app ChatGPT) com o Terra 6.1 em esforço baixo. É AQUI que se troca o padrão; o usuário escolhe outro em
/// Ajustes › Inteligência artificial › Revisão do relatório. Sem o Codex instalado, a pergunta cai no Claude Code, como antes.</summary>
public static class ModeloIaAjuste
{
    /// <summary>O slug do Terra 6.1 no catálogo do Codex (<c>%USERPROFILE%\.codex\models_cache.json</c>).</summary>
    public const string ModeloPadrao = "gpt-6.1-terra";

    /// <summary>O esforço de raciocínio (<c>model_reasoning_effort</c>) padrão: baixo.</summary>
    public const string EsforcoPadrao = "low";

    /// <summary>"O modelo que estiver configurado no Codex": nenhum <c>-m</c> nem <c>-c</c> vai na linha de comando.</summary>
    public const string DoCodex = "codex";

    /// <summary>Tempo máximo de uma pergunta curta (as do Novo despacho continuam com o limite de <c>IaDespacho.Limite</c>).</summary>
    public static readonly TimeSpan Limite = TimeSpan.FromMinutes(4);

    public static IReadOnlyList<(string Valor, string Texto)> Modelos { get; } =
    [
        (ModeloPadrao, "Terra 6.1 (padrão)"),
        ("gpt-6.1-sol", "Sol 6.1"),
        (DoCodex, "O que estiver no Codex"),
    ];

    public static IReadOnlyList<(string Valor, string Texto)> Esforcos { get; } =
    [
        (EsforcoPadrao, "Baixo (padrão)"),
        ("medium", "Médio"),
        ("high", "Alto"),
    ];

    public static bool ModeloValido(string? m) => m is not null && Modelos.Any(x => x.Valor == m);

    public static bool EsforcoValido(string? e) => e is not null && Esforcos.Any(x => x.Valor == e);

    /// <summary>Os argumentos do <c>codex exec</c> para o modelo e o esforço (sintaxe conferida em <c>codex exec --help</c>, 0.167):
    /// <c>-m gpt-6.1-terra -c model_reasoning_effort="low"</c>. Com <see cref="DoCodex"/>, nada: vale o config.toml do usuário.</summary>
    public static IReadOnlyList<string> ArgumentosCodex(string? modelo, string? esforco)
    {
        if (modelo == DoCodex) return [];
        var m = ModeloValido(modelo) ? modelo! : ModeloPadrao;
        var e = EsforcoValido(esforco) ? esforco! : EsforcoPadrao;
        return ["-m", m, "-c", "model_reasoning_effort=\"" + e + "\""];
    }

    /// <summary>"Terra 6.1, esforço baixo": o que a tela mostra enquanto espera.</summary>
    public static string Descricao(string? modelo, string? esforco)
    {
        if (modelo == DoCodex) return "modelo do Codex";
        var m = Modelos.FirstOrDefault(x => x.Valor == modelo).Texto ?? Modelos[0].Texto;
        var e = Esforcos.FirstOrDefault(x => x.Valor == esforco).Texto ?? Esforcos[0].Texto;
        return m.Replace(" (padrão)", "", StringComparison.Ordinal) + ", esforço " + e.Replace(" (padrão)", "", StringComparison.Ordinal).ToLowerInvariant();
    }

    /// <summary>"0:07", "1:23": o tempo de espera mostrado ao lado do botão.</summary>
    public static string Tempo(TimeSpan t) => ((int)t.TotalMinutes).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + t.Seconds.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
}
