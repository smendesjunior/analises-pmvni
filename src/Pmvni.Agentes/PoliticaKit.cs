namespace Pmvni.Agentes;

/// <summary>O que o kit PMVNI decide e o editor só obedece (desenho §12.8): o <c>pro_runtime.py</c> instalado proíbe API paga enquanto contiver a âncora.</summary>
public static class PoliticaKit
{
    public const string AncoraApiDesativada = "API paga desativada";

    /// <summary>False se o texto do <c>pro_runtime.py</c> contém "API paga desativada" (se o kit mudar, a opção passa a funcionar sem mudar o editor).</summary>
    public static bool ApiPermitida(string textoProRuntime) => !textoProRuntime.Contains(AncoraApiDesativada, StringComparison.Ordinal);
}
