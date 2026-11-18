namespace Pmvni.Agentes;

/// <summary>O ambiente do processo filho (desenho §12.9): as chaves herdadas saem sempre; só a variável do provedor entra, e só no modo chave.
/// Claude: a rota do análise pro vai declarada em <see cref="PoliticaKit.VariavelRota"/> (<c>api</c> só no modo chave; <c>conta</c> nos demais).
/// Gemini: rota e perfil do análise Google vão declarados só quando há perfil (partida e conferência); sem perfil (leitura da situação) nada é declarado.</summary>
public static class AmbienteFilho
{
    static readonly string[] Declaradas =
    {
        PoliticaKit.VariavelRota, PoliticaKit.VariavelRotaGoogle, PoliticaKit.VariavelPerfilGoogle, PoliticaKit.VariavelFicticioGoogle,
    };

    public static void Aplicar(IDictionary<string, string?> env, Provedor p, ModoAcesso m, string? chave, PerfilGemini? perfil = null, bool ficticio = false)
    {
        if (m == ModoAcesso.Chave && string.IsNullOrWhiteSpace(chave))
            throw new ArgumentException("O modo chave de API precisa de uma chave.", nameof(chave));
        if (p == Provedor.Gemini && perfil is { } pg && MatrizGemini.Modo(pg) != m)
            throw new ArgumentException("O perfil do Gemini não corresponde ao modo de acesso.", nameof(perfil));

        foreach (var nome in env.Keys.Where(k => Provedores.VariaveisRemovidas.Contains(k, StringComparer.OrdinalIgnoreCase)
                                                 || Declaradas.Contains(k, StringComparer.OrdinalIgnoreCase)).ToList())
            env.Remove(nome);
        if (m == ModoAcesso.Chave) env[Provedores.VariavelChave(p)] = chave;
        if (p == Provedor.Claude) env[PoliticaKit.VariavelRota] = m == ModoAcesso.Chave ? "api" : "conta";
        if (p == Provedor.Gemini && perfil is { } g)
        {
            env[PoliticaKit.VariavelRotaGoogle] = MatrizGemini.Rota(g);
            env[PoliticaKit.VariavelPerfilGoogle] = MatrizGemini.Codigo(g);
            if (g == PerfilGemini.ApiGratuita && ficticio) env[PoliticaKit.VariavelFicticioGoogle] = "1";
        }
    }
}
