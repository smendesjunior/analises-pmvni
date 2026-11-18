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

    public static bool ApiPermitida(string textoProRuntime) =>
        !textoProRuntime.Contains(AncoraApiDesativada, StringComparison.Ordinal) && textoProRuntime.Contains(VariavelRota, StringComparison.Ordinal);
}
