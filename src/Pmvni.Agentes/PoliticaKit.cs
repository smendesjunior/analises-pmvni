namespace Pmvni.Agentes;

/// <summary>O que o kit PMVNI decide e o editor só obedece (desenho §12.8): o <c>pro_runtime.py</c> instalado diz se o análise pro aceita a Anthropic API.</summary>
public static class PoliticaKit
{
    public const string AncoraApiDesativada = "API paga desativada";

    /// <summary>A variável com que o editor declara a rota do análise pro ao processo filho: <c>conta</c> ou <c>api</c> (análise pro 1.6.4+). O kit só usa a
    /// chave com a rota <c>api</c> declarada e recusa chave sem rota (nem conta, nem API): nunca há troca silenciosa entre os modos.</summary>
    public const string VariavelRota = "PMVNI_ANALISE_PRO_ROTA";

    /// <summary>True só se o <c>pro_runtime.py</c> NÃO contém "API paga desativada" e conhece a rota explícita (<see cref="VariavelRota"/>, 1.6.4+). Um runtime
    /// sem nenhuma das duas marcas (o 1.6.3, que deduzia a rota pela chave) não é usado no modo API: o editor recusa em vez de arriscar a troca de modo.</summary>
    /// <summary>Análise Google: a rota (<c>conta</c> ou <c>api</c>), o perfil (<see cref="MatrizGemini.Codigo"/>) e a declaração de que o processo é fictício
    /// (exigida pelo perfil gratuito). O plugin recusa a partida sem rota e perfil coerentes: nunca há troca de modo, modelo ou cobrança sem escolha.</summary>
    public const string VariavelRotaGoogle = "PMVNI_ANALISE_GOOGLE_ROTA";
    public const string VariavelPerfilGoogle = "PMVNI_ANALISE_GOOGLE_PERFIL";
    public const string VariavelFicticioGoogle = "PMVNI_ANALISE_GOOGLE_FICTICIO";

    /// <summary>O <c>google_runtime.py</c> instalado conhece a rota explícita (a marca do contrato com o editor).</summary>
    public static bool RuntimeGoogleValido(string textoGoogleRuntime) => textoGoogleRuntime.Contains(VariavelRotaGoogle, StringComparison.Ordinal);

    /// <summary>As versões mínimas que o Editor exige. Tarefa 18: um só plugin de análise para as três rotas (o analise 4.0.0, com os controladores em
    /// <c>skills\analise\scripts\rotas\&lt;rota&gt;</c>); o contrato da Tarefa 2 (códigos 0/20/21/1, JSON da situação, espera de cota e decisão do analista)
    /// continua o mesmo.</summary>
    public static IReadOnlyDictionary<Provedor, (string Plugin, Version Minima)> VersoesMinimas { get; } = new Dictionary<Provedor, (string, Version)>
    {
        [Provedor.ChatGpt] = (PluginsInstalados.Analise, new Version(4, 0, 0)),
        [Provedor.Claude] = (PluginsInstalados.Analise, new Version(4, 0, 0)),
        [Provedor.Gemini] = (PluginsInstalados.Analise, new Version(4, 0, 0)),
    };

    /// <summary>null se o plugin instalado do provedor atende à versão mínima; senão "Atualize o kit PMVNI (analise 4.0.0 ou mais novo)…", com a versão
    /// instalada. Versão desconhecida ou ilegível conta como antiga: o editor não presume o contrato novo.</summary>
    public static string? PluginDesatualizado(Provedor p, string? versaoInstalada)
    {
        var (plugin, minima) = VersoesMinimas[p];
        if (versaoInstalada is not null && Version.TryParse(versaoInstalada.Split('-', '+')[0], out var v) && v >= minima) return null;
        return $"Atualize o kit PMVNI ({plugin} {minima.ToString(3)} ou mais novo): o {plugin} instalado neste computador é "
            + (versaoInstalada is null ? "de versão desconhecida" : "o " + versaoInstalada)
            + ". Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor). Nada foi iniciado.";
    }

    public static bool ApiPermitida(string textoProRuntime) =>
        !textoProRuntime.Contains(AncoraApiDesativada, StringComparison.Ordinal) && textoProRuntime.Contains(VariavelRota, StringComparison.Ordinal);
}
