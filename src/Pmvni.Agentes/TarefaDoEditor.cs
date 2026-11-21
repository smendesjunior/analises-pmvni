using Pmvni.Revisao;

namespace Pmvni.Agentes;

/// <summary>Grupo compartilhado das análises que podem ser encerradas juntas.</summary>
public sealed class TarefaDoEditor : GrupoDeProcessos
{
    public static TarefaDoEditor Padrao => padrao.Value;
    static readonly Lazy<TarefaDoEditor> padrao = new(() => new());
}