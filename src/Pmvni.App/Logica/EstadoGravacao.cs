using System.Globalization;

namespace Pmvni.App;

/// <summary>Estado de gravação do pacote aberto, mostrado na barra de título.</summary>
public enum EstadoGravacao { Salvo, Salvando, NaoSalvo, Conflito, SomenteLeitura }

/// <summary>Tom do selo de gravação no cabeçalho: verde, neutro, amarelo ou vermelho.</summary>
public enum TomSelo { Sucesso, Neutro, Atencao, Erro }

public static class EstadoGravacaoTexto
{
    /// <summary>Tom do selo (correções 2026-11-15): "Salvo" verde, "Não salvo" amarelo, "Conflito" vermelho, os outros neutros.</summary>
    public static TomSelo Tom(EstadoGravacao e) => e switch
    {
        EstadoGravacao.Salvo => TomSelo.Sucesso,
        EstadoGravacao.NaoSalvo => TomSelo.Atencao,
        EstadoGravacao.Conflito => TomSelo.Erro,
        _ => TomSelo.Neutro,
    };

    /// <summary>Ícone do selo (Segoe Fluent): ✓ em círculo, sincronizar, aviso, erro ou cadeado.</summary>
    public static string Glifo(EstadoGravacao e) => e switch
    {
        EstadoGravacao.Salvo => Glifos.Concluido,
        EstadoGravacao.Salvando => Glifos.Sincronizar,
        EstadoGravacao.NaoSalvo => Glifos.Avisos,
        EstadoGravacao.Conflito => Glifos.Erro,
        EstadoGravacao.SomenteLeitura => Glifos.Bloqueado,
        _ => throw new ArgumentOutOfRangeException(nameof(e)),
    };

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
