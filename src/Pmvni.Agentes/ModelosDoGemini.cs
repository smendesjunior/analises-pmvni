using Pmvni.Revisao;

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
    public static Task<(string? Json, string? Falha)> LerAsync(Pecas pecas, PerfilGemini perfil, string? chave, string pastaTrabalho, CancellationToken ct,
        IReadOnlyDictionary<string, string>? extra = null) =>
        Task.FromResult<(string?, string?)>((null, Provedores.RotaDesativada));
}
