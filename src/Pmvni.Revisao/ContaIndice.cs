using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>A conta escrita no "Cálculo / descrição" de um índice urbanístico (correção 2026-11-17): <c>126,40 ÷ 360,00 × 100 = 35,1%</c>
/// (taxa, em %) ou <c>162,00 ÷ 360,00 = 0,45</c> (coeficiente). Pode vir no meio de uma frase ("10% informado no projeto; 126,40 ÷
/// 360,00 × 100 = 35,1%."). Só uma divisão por texto: com duas, não se sabe qual vale e nada é lido. Guarda onde está cada número no
/// texto, para trocar só o número (o resto da frase fica como o analista escreveu).</summary>
public sealed partial record ContaIndice(Medida Dividendo, Medida Divisor, bool Percentual, Medida Resultado,
    Range PosDividendo, Range PosDivisor, Range PosResultado)
{
    /// <summary>Casas do resultado refeito pelo editor: 1 numa taxa (%), 2 num coeficiente.</summary>
    public int CasasDoResultado => Percentual ? 1 : 2;

    /// <summary>Casas de uma área (m²) escrita pelo editor.</summary>
    public const int CasasDeArea = 2;

    const string Num = @"[0-9]+(?:[.,][0-9]+)*";

    [GeneratedRegex(@"(?<a>" + Num + @")\s*(?:m²|m2)?\s*[÷/]\s*(?<b>" + Num + @")\s*(?:m²|m2)?\s*(?:(?<x>[×xX*])\s*100\s*)?=\s*(?<r>" + Num + @")\s*(?<pct>%)?",
        RegexOptions.CultureInvariant)]
    private static partial Regex Divisao();

    /// <summary>A conta do texto; null se não há exatamente uma divisão "a ÷ b [× 100] = r" com números legíveis e divisor diferente de zero.</summary>
    public static ContaIndice? Ler(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var ms = Divisao().Matches(texto);
        if (ms.Count != 1) return null;
        var m = ms[0];
        if (RecalculoLinha.LerNumero(m.Groups["a"].Value) is not var (a, ca) || RecalculoLinha.LerNumero(m.Groups["b"].Value) is not var (b, cb)
            || RecalculoLinha.LerNumero(m.Groups["r"].Value) is not var (r, cr) || b == 0) return null;
        var percentual = m.Groups["x"].Success || m.Groups["pct"].Success;
        static Range Pos(Group g) => new(g.Index, g.Index + g.Length);
        return new ContaIndice(new Medida(a, "m²", ca), new Medida(b, "m²", cb), percentual, new Medida(r, percentual ? "%" : "", cr),
            Pos(m.Groups["a"]), Pos(m.Groups["b"]), Pos(m.Groups["r"]));
    }

    /// <summary>O resultado exato da conta (sem arredondar).</summary>
    public decimal Calcular() => Dividendo.Valor / Divisor.Valor * (Percentual ? 100m : 1m);

    /// <summary>O resultado arredondado como o editor o escreve (1 casa em %, 2 no coeficiente).</summary>
    public decimal Arredondado => Math.Round(Calcular(), CasasDoResultado, MidpointRounding.AwayFromZero);

    /// <summary>O resultado escrito confere com a conta, na precisão em que foi escrito.</summary>
    public bool ResultadoConfere => Concorda(Resultado.Valor, Resultado.Casas, Calcular(), CasasDoResultado);

    /// <summary>O texto com o resultado refeito (só o número muda; "%" e o resto da frase ficam). Igual se o resultado já confere.</summary>
    public string Refazer(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        if (ResultadoConfere) return texto;
        return Trocar(texto, PosResultado, RecalculoLinha.Formatar(Arredondado, CasasDoResultado, ""));
    }

    /// <summary>O texto com o resultado trocado por <paramref name="numero"/> (só o número; "%" e o resto da frase ficam).</summary>
    public string ComResultado(string texto, string numero)
    {
        ArgumentNullException.ThrowIfNull(texto);
        ArgumentNullException.ThrowIfNull(numero);
        return Trocar(texto, PosResultado, numero);
    }

    /// <summary>O texto com outro divisor (a área do terreno) e o resultado refeito.</summary>
    public string TrocarDivisor(string texto, decimal novo)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var t = Trocar(texto, PosDivisor, RecalculoLinha.Formatar(novo, CasasDeArea, ""));
        return Ler(t) is { } c ? c.Refazer(t) : t;
    }

    static string Trocar(string texto, Range pos, string novo)
    {
        var (inicio, tamanho) = pos.GetOffsetAndLength(texto.Length);
        return texto[..inicio] + novo + texto[(inicio + tamanho)..];
    }

    /// <summary>Dois números escritos com casas diferentes são o mesmo valor na precisão do menos preciso: "35%" e 35,1% concordam;
    /// "10%" e 35,1% não.</summary>
    public static bool Concorda(decimal a, int casasA, decimal b, int casasB)
    {
        var casas = Math.Min(casasA, casasB);
        var tolerancia = 0.5m;
        for (var i = 0; i < casas; i++) tolerancia /= 10m;
        return Math.Abs(a - b) <= tolerancia;
    }

    /// <summary>O valor "no projeto" é o mesmo que o resultado da conta (mesma unidade, na precisão do menos preciso).</summary>
    public bool ConcordaCom(Medida valor)
    {
        ArgumentNullException.ThrowIfNull(valor);
        if (valor.Unidade != Resultado.Unidade) return false;
        return Concorda(valor.Valor, valor.Casas, Resultado.Valor, Resultado.Casas);
    }
}
