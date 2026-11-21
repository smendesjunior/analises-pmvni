namespace Pmvni.Agentes;

/// <summary>Tarefa 19 (correção 1): a lista de modelos que o Gemini oferece agora, pela própria rota Gemini do kit (<c>google_runtime.py --case P modelos</c>,
/// só leitura, sem gerar texto): <c>agy models</c> na Conta Antigravity, <c>GET v1beta/models</c> com a chave na API; o Gemini Web não lista e devolve os
/// modelos da tabela do kit (<c>lista_do_provedor: false</c>). É chamada como o pré-voo <c>conferir</c>: o modo e o perfil no ambiente do processo filho e,
/// na API, a chave só nele. Devolve a linha JSON do kit (nunca com a chave) ou o motivo de não ter lista.</summary>
public static class ModelosDoGemini
{
    public static readonly TimeSpan Limite = TimeSpan.FromSeconds(60);

    /// <param name="pastaTrabalho">Uma pasta qualquer que exista (o controlador exige <c>--case</c>, mas esta ação não lê nem grava nela).</param>
    /// <param name="extra">Só testes e roteiro: variáveis a mais no ambiente do filho (o agy falso).</param>
    public static async Task<(string? Json, string? Falha)> LerAsync(Pecas pecas, PerfilGemini perfil, string? chave, string pastaTrabalho, CancellationToken ct,
        IReadOnlyDictionary<string, string>? extra = null)
    {
        ArgumentNullException.ThrowIfNull(pecas);
        if (pecas.Python is null || pecas.GoogleRuntime is null) return (null, "a rota Gemini do kit não está instalada neste computador");
        var (exe, args) = LinhasDeComando.ProRuntime(pecas.Python, pecas.PythonPeloLauncher, pecas.GoogleRuntime, pastaTrabalho, "modelos");
        // Na API gratuita, o kit exige a declaração de processo fictício: listar modelos não envia conteúdo nenhum.
        void Ambiente(IDictionary<string, string?> env)
        {
            if (extra is not null)
                foreach (var (k, v) in extra) env[k] = v;
            AmbienteFilho.Aplicar(env, Provedor.Gemini, MatrizGemini.Modo(perfil), chave, perfil, ficticio: perfil == PerfilGemini.ApiGratuita);
            env.Remove(TabelaDeModelos.Variavel);
        }
        var r = await ExecucaoProcesso.RodarAsync(exe, args, pastaTrabalho, Ambiente, Limite, ct).ConfigureAwait(false);
        var saida = new RedatorChave(chave).Aplicar(r.Saida);
        var json = saida.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{') && l.EndsWith('}'));
        if (json is null) return (null, "a rota Gemini não devolveu a lista (" + Diagnostico.PrimeiraLinha(saida) + ")");
        return (json, null);
    }
}
