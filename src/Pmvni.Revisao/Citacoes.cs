using System.Globalization;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Uma citação "item NN" convertida em token: o trecho original ("item 07"), o número citado (7) e o id do item citado.</summary>
public sealed record CitacaoConvertida(string Trecho, int Numero, string Alvo);

/// <summary>Citações de itens dentro dos textos ("item 07"). Na importação, só a citação inequívoca vira o token
/// <c>{{ref:&lt;id&gt;:&lt;largura&gt;}}</c>, que substitui apenas os dígitos (a palavra "item/ITEM/Item" fica literal, então a caixa se mantém);
/// na impressão o token vira o número atual do item citado, com zeros à esquerda até a largura do original.</summary>
public static partial class Citacoes
{
    // Dígitos sempre [0-9]: em .NET "\d" casa também dígitos não ASCII (ex.: "３"), que o int.Parse recusa.
    // Regra de conversão (desenho §8.5): item/ITEM/Item + 1 a 3 dígitos, palavra inteira, sem decimal ("item 3.2") e sem norma
    // depois ("item 3 do art. 5"). Tudo o que não casa fica literal.
    [GeneratedRegex(@"(?<![\p{L}\p{N}])(item|ITEM|Item)\s+([0-9]{1,3})(?![\p{L}\p{N}])(?![.,][0-9])(?!\s*[,;]?\s*(?:do|da|de|dos|das)\b)", RegexOptions.CultureInvariant)]
    private static partial Regex Inequivoca();

    // "item NN" ou "itens NN" (a forma "itens?" casaria "iten"/"itens" e nunca "item").
    [GeneratedRegex(@"\b(?:item|itens)\s+[0-9]{1,3}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Suspeita();

    [GeneratedRegex(@"\{\{ref:([^:{}]+):([0-9]+)\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    /// <summary>Converte as citações inequívocas. <paramref name="idsPorNumero"/>[N-1] é o id do item de número N; NN fora de 1..Count fica literal.</summary>
    public static (string Texto, IReadOnlyList<CitacaoConvertida> Convertidas) Converter(string texto, IReadOnlyList<string> idsPorNumero)
    {
        ArgumentNullException.ThrowIfNull(texto);
        ArgumentNullException.ThrowIfNull(idsPorNumero);
        var convertidas = new List<CitacaoConvertida>();
        var saida = Inequivoca().Replace(texto, m =>
        {
            var digitos = m.Groups[2];
            var numero = int.Parse(digitos.Value, CultureInfo.InvariantCulture);
            if (numero < 1 || numero > idsPorNumero.Count) return m.Value;
            var alvo = idsPorNumero[numero - 1];
            convertidas.Add(new CitacaoConvertida(m.Value, numero, alvo));
            return m.Value[..(digitos.Index - m.Index)] + "{{ref:" + alvo + ":" + digitos.Length.ToString(CultureInfo.InvariantCulture) + "}}";
        });
        return (saida, convertidas);
    }

    /// <summary>Troca cada token pelo número atual do item (com zeros à esquerda até a largura do original); se o item foi excluído
    /// (<paramref name="numeroAtual"/> devolve null), imprime o <paramref name="numeroOriginal"/> dele. O texto pode ser digitado pelo
    /// usuário e precisa continuar imprimível: um token que não resolve (id desconhecido, isto é, os dois delegados devolvem null, ou
    /// largura fora de 1..3) fica literal, exatamente como foi escrito, e nunca lança.</summary>
    public static string Imprimir(string texto, Func<string, int?> numeroAtual, Func<string, int?> numeroOriginal)
    {
        ArgumentNullException.ThrowIfNull(texto);
        ArgumentNullException.ThrowIfNull(numeroAtual);
        ArgumentNullException.ThrowIfNull(numeroOriginal);
        return Token().Replace(texto, m =>
        {
            var id = m.Groups[1].Value;
            if (!int.TryParse(m.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var largura) || largura < 1 || largura > 3)
                return m.Value;
            var n = numeroAtual(id) ?? numeroOriginal(id);
            if (n is null) return m.Value;
            return n.Value.ToString("D" + largura.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        });
    }

    /// <summary>Os trechos "item NN"/"itens NN" que continuam literais no texto (não viraram token): candidatos ao aviso de numeração.</summary>
    public static IReadOnlyList<string> LiteraisSuspeitos(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        return Suspeita().Matches(texto).Select(m => m.Value).ToList();
    }
}
