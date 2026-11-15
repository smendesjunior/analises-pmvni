using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Id estável de item: <c>it_</c> + ULID de 26 caracteres (48 bits de milissegundos + 80 bits sorteados, Crockford base 32).
/// Ids de instantes diferentes ordenam pelo instante.</summary>
public static partial class IdItem
{
    private const string Alfabeto = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    [GeneratedRegex("^it_[0-9A-HJKMNP-TV-Z]{26}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex Formato();

    public static string Novo(DateTimeOffset agora, RandomNumberGenerator rng)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var ms = (ulong)agora.ToUnixTimeMilliseconds() & 0xFFFF_FFFF_FFFFUL;
        Span<byte> sorteio = stackalloc byte[10];
        rng.GetBytes(sorteio);

        UInt128 valor = ms;
        foreach (var b in sorteio) valor = (valor << 8) | b;

        var chars = new char[26];
        for (var i = 25; i >= 0; i--)
        {
            chars[i] = Alfabeto[(int)(valor & 31)];
            valor >>= 5;
        }
        return "it_" + new string(chars);
    }

    public static bool Valido(string id) => id is not null && Formato().IsMatch(id);
}
