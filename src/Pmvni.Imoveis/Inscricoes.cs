namespace Pmvni.Imoveis;

/// <summary>A chave da inscrição municipal para ligar o cadastro ao desenho (2026-11-17). A inscrição tem 14 dígitos: distrito (2), setor (2),
/// quadra (3), lote (4) e unidade (3); os 11 primeiros identificam o terreno. Na base real há chaves digitadas fora do padrão, dos dois lados:
/// pontos e sufixos ("01020100139001(D.A)", "03030090166001m"), lote com 3 dígitos ("0201041118001", sem o zero do lote) ou com 5
/// ("010201400218001", um zero a mais), e a inscrição repetida com a unidade ("01010520076001001"). <see cref="Normalizar"/> leva cada uma
/// à forma de 14 dígitos quando a leitura não é ambígua; quando é, devolve null (e o registro fica sem a ligação, como antes).</summary>
public static class Inscricoes
{
    public const int Digitos = 14;

    /// <summary>Só os dígitos, na forma de 14; null quando não dá para saber qual é a inscrição.</summary>
    public static string? Normalizar(string? inscricao)
    {
        if (string.IsNullOrWhiteSpace(inscricao)) return null;
        var d = new string(inscricao.Where(char.IsAsciiDigit).ToArray());
        return d.Length switch
        {
            Digitos => d,
            13 => d[..7] + "0" + d[7..],                              // lote com 3 dígitos: falta o zero à esquerda do lote
            15 when d[7] == '0' => d[..7] + d[8..],                   // lote com 5 dígitos: um zero à esquerda a mais
            17 when d[11..14] == d[14..] => d[..Digitos],             // a unidade repetida no fim
            _ => null,
        };
    }

    /// <summary>Distrito, setor, quadra e lote (os 11 primeiros dígitos da forma normalizada), sem a unidade.</summary>
    public static string? Terreno(string? inscricao) => Normalizar(inscricao) is { } n ? n[..11] : null;

    /// <summary>A inscrição para mostrar: 01.01.118.0077.001 (distrito.setor.quadra.lote.unidade).</summary>
    public static string Formatar(string? inscricao) =>
        Normalizar(inscricao) is { } n ? $"{n[..2]}.{n[2..4]}.{n[4..7]}.{n[7..11]}.{n[11..]}" : inscricao ?? "";
}
