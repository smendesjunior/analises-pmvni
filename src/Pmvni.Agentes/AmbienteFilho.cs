namespace Pmvni.Agentes;

/// <summary>O ambiente do processo filho (desenho §12.9): as chaves herdadas saem sempre; só a variável do provedor entra, e só no modo chave.
/// Claude: a rota do análise pro vai declarada em <see cref="PoliticaKit.VariavelRota"/> (<c>api</c> só no modo chave; <c>conta</c> nos demais).</summary>
public static class AmbienteFilho
{
    public static void Aplicar(IDictionary<string, string?> env, Provedor p, ModoAcesso m, string? chave)
    {
        if (m == ModoAcesso.Chave && string.IsNullOrWhiteSpace(chave))
            throw new ArgumentException("O modo chave de API precisa de uma chave.", nameof(chave));

        foreach (var nome in env.Keys.Where(k => Provedores.VariaveisRemovidas.Contains(k, StringComparer.OrdinalIgnoreCase)
                                                 || k.Equals(PoliticaKit.VariavelRota, StringComparison.OrdinalIgnoreCase)).ToList())
            env.Remove(nome);
        if (m == ModoAcesso.Chave) env[Provedores.VariavelChave(p)] = chave;
        if (p == Provedor.Claude) env[PoliticaKit.VariavelRota] = m == ModoAcesso.Chave ? "api" : "conta";
    }
}
