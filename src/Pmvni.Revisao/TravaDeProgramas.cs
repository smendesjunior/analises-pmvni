namespace Pmvni.Revisao;

/// <summary>Trava de teste e de roteiro para todo programa que o editor inicia (o agente pelo <c>ExecucaoProcesso</c> e o LibreOffice do despacho
/// pelo <c>ConversorLibreOffice</c>): com <see cref="Permitidos"/> definido, um programa fora da lista lança <see cref="InvalidOperationException"/>
/// antes de iniciar; o que passa é anunciado em <see cref="AoIniciar"/> (o roteiro lista os programas iniciados no resultado). Sem lista, nada é
/// conferido (o uso normal do editor).</summary>
public static class TravaDeProgramas
{
    /// <summary>Pastas (vale tudo dentro delas) ou programas exatos permitidos; null = sem trava.</summary>
    public static IReadOnlyList<string>? Permitidos { get; set; }

    /// <summary>Chamado com o caminho completo de cada programa que a trava deixou passar, logo antes de iniciá-lo; o que ela recusa nunca chega aqui.</summary>
    public static Action<string>? AoIniciar { get; set; }

    /// <summary>Confere o programa contra <see cref="Permitidos"/> (antes de iniciar).</summary>
    public static void Conferir(string exe)
    {
        if (Permitidos is { } lista)
        {
            string completo;
            try { completo = Path.GetFullPath(exe); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidOperationException("Programa fora da pasta permitida: " + exe);
            }
            if (!lista.Any(p => Permite(p, completo))) throw new InvalidOperationException("Programa fora da pasta permitida: " + exe);
        }
    }

    /// <summary>O programa partiu de fato (depois do <c>Process.Start</c>): vai para <see cref="AoIniciar"/>. Um início que falhou não conta.</summary>
    public static void Iniciou(string exe) => AoIniciar?.Invoke(Path.GetFullPath(exe));

    /// <summary>Quantos dos <paramref name="iniciados"/> ficam fora de <paramref name="pasta"/> (no roteiro: os do LibreOffice, fora do agente falso).</summary>
    public static int ForaDe(IEnumerable<string> iniciados, string pasta) =>
        iniciados.Count(c => !c.StartsWith(Path.TrimEndingDirectorySeparator(pasta) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

    static bool Permite(string permitido, string completo)
    {
        try
        {
            var p = Path.GetFullPath(permitido);
            if (string.Equals(p, completo, StringComparison.OrdinalIgnoreCase)) return true;        // o programa exato (o soffice do roteiro)
            return completo.StartsWith(Path.TrimEndingDirectorySeparator(p) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);   // uma pasta
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }
}
