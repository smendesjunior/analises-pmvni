namespace Pmvni.App;

/// <summary>Onde o fecho do despacho (da data à matrícula) vai na prévia. No DOCX esses parágrafos têm "manter com o próximo" e nunca se
/// separam entre páginas; na prévia o texto corre de folha em folha sem essa regra, então o fecho é um bloco à parte: ou cabe inteiro no
/// que sobra da última folha do texto, ou vai inteiro para uma folha nova (correção de 2026-11-16: a linha da assinatura ficava numa folha
/// e o nome na seguinte). Sem tipos WinUI (compilada também nos testes).</summary>
public static class FechoDaPrevia
{
    /// <summary>Tolerância de arredondamento do layout, em DIP.</summary>
    public const double Folga = 0.5;

    /// <summary>O deslocamento do fecho a partir do alto do corpo da última folha do texto, ou null se ele vai para uma folha nova.
    /// <paramref name="usado"/>: a altura que o texto ocupa nessa folha; <paramref name="alturaFecho"/>: a do bloco do fecho;
    /// <paramref name="alturaUtil"/>: a do corpo de uma folha. Medida que não serve (NaN, negativa, ou texto que diz ocupar nada):
    /// folha nova, que nunca sobrepõe o fecho ao texto.</summary>
    public static double? NaUltimaFolha(double usado, double alturaFecho, double alturaUtil)
    {
        if (!double.IsFinite(usado) || !double.IsFinite(alturaFecho) || !double.IsFinite(alturaUtil)) return null;
        if (usado <= 0 || alturaFecho <= 0 || alturaUtil <= 0) return null;
        usado = Math.Min(usado, alturaUtil);
        return usado + alturaFecho <= alturaUtil + Folga ? usado : null;
    }
}
