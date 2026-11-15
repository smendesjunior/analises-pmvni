using System.Globalization;

namespace Pmvni.App;

/// <summary>Estado de gravação do pacote aberto, mostrado na barra de título.</summary>
public enum EstadoGravacao { Salvo, Salvando, NaoSalvo, Conflito, SomenteLeitura }

public static class EstadoGravacaoTexto
{
    /// <summary><c>"Salvo 14:32"</c> (hora local da última gravação ou abertura), <c>"Salvando…"</c>, <c>"Não salvo"</c>, <c>"Conflito"</c>,
    /// <c>"Somente leitura"</c>.</summary>
    public static string Texto(EstadoGravacao e, DateTime? salvoEmLocal) => e switch
    {
        EstadoGravacao.Salvo => salvoEmLocal is { } t ? "Salvo " + t.ToString("HH:mm", CultureInfo.InvariantCulture) : "Salvo",
        EstadoGravacao.Salvando => "Salvando…",
        EstadoGravacao.NaoSalvo => "Não salvo",
        EstadoGravacao.Conflito => "Conflito",
        EstadoGravacao.SomenteLeitura => "Somente leitura",
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };
}
