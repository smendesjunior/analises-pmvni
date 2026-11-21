using System.Diagnostics;

namespace Pmvni.Revisao;

/// <summary>Trava de teste e de roteiro para os programas que o Editor inicia. Com <see cref="Permitidos"/> definido, exige um caminho
/// completo na lista antes de iniciar. Cada processo iniciado é anunciado em <see cref="AoIniciar"/>.</summary>
public static class TravaDeProgramas
{
    /// <summary>Pastas (vale tudo dentro delas) ou programas exatos permitidos; null = sem trava.</summary>
    public static IReadOnlyList<string>? Permitidos { get; set; }

    /// <summary>Chamado com o caminho completo de cada programa iniciado.</summary>
    public static Action<string>? AoIniciar { get; set; }

    /// <summary>Confere o programa contra <see cref="Permitidos"/> (antes de iniciar).</summary>
    public static void Conferir(string exe)
    {
        if (Permitidos is { } lista && !Permitido(exe, lista))
            throw new InvalidOperationException("Programa fora da pasta permitida: " + exe);
    }

    /// <summary>Inicia pelo mesmo caminho usado na conferência e registra só as partidas bem-sucedidas.</summary>
    public static Process? Iniciar(ProcessStartInfo inicio)
    {
        if (Permitidos is not null && inicio.UseShellExecute)
            throw new InvalidOperationException("A abertura pelo Windows está bloqueada durante a conferência.");
        Conferir(inicio.FileName);
        var processo = Process.Start(inicio);
        if (processo is not null) AoIniciar?.Invoke(Path.GetFullPath(inicio.FileName));
        return processo;
    }

    /// <summary>Confere o caminho completo sem resolver nomes pelo PATH ou associações de arquivo.</summary>
    public static bool Permitido(string exe, IReadOnlyList<string> lista)
    {
        if (!Path.IsPathFullyQualified(exe)) return false;
        try
        {
            var completo = Path.GetFullPath(exe);
            return lista.Any(permitido =>
            {
                var p = Path.GetFullPath(permitido);
                return string.Equals(p, completo, StringComparison.OrdinalIgnoreCase)
                    || completo.StartsWith(Path.TrimEndingDirectorySeparator(p) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
