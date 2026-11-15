namespace Pmvni.Revisao;

/// <summary>"Próximo pendente": o item a olhar em seguida na revisão.</summary>
public static class Pendencias
{
    /// <summary>O id do próximo item incluído, depois de <paramref name="atual"/>, com situação efetiva diferente de ATENDE ou com a
    /// justificativa faltando; a busca é circular (volta ao primeiro) e pode devolver o próprio <paramref name="atual"/> se for o único.
    /// Sem <paramref name="atual"/> (ou se ele não está entre os incluídos) começa do primeiro. Null se nada estiver pendente.</summary>
    public static string? Proximo(Projeto p, string? atual)
    {
        ArgumentNullException.ThrowIfNull(p);
        var incluidos = p.Incluidos;
        var inicio = 0;
        for (var i = 0; i < incluidos.Count; i++)
            if (string.Equals(incluidos[i].Id, atual, StringComparison.Ordinal)) { inicio = i + 1; break; }

        for (var k = 0; k < incluidos.Count; k++)
        {
            var it = incluidos[(inicio + k) % incluidos.Count];
            if (Pendente(it)) return it.Id;
        }
        return null;
    }

    private static bool Pendente(ItemRevisao it) =>
        Motor.SituacaoEfetiva(it) != "ATENDE" || ConferenciaRevisao.FaltaJustificativa(it);
}
