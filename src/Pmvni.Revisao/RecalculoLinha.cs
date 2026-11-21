using System.Globalization;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Um número lido de um texto do relatório, com a unidade e as casas decimais com que foi escrito.</summary>
public sealed record Medida(decimal Valor, string Unidade, int Casas);

/// <summary>O critério numérico de uma linha, lido do texto do limite: mínimo (<c>≥ 3,00 m</c>, <c>mínimo 3 m</c>), máximo
/// (<c>≤ 47%</c>, <c>máx. 9,00 m</c>, <c>até 60%</c>) ou faixa (<c>entre 1,50 e 3,00 m</c>).</summary>
public sealed record Criterio(Medida? Minimo, Medida? Maximo)
{
    public string Unidade => (Minimo ?? Maximo)!.Unidade;
    public int Casas => Math.Max(Minimo?.Casas ?? 0, Maximo?.Casas ?? 0);
}

/// <summary>O que a edição manual de um texto de linha de tabela vira: o <see cref="Comando"/> a aplicar (a própria edição, ou um
/// <see cref="Lote"/> com a edição e o recálculo) e o que dizer ao usuário. <see cref="Recalculado"/>: a situação e/ou o texto da
/// diferença foram refeitos; <see cref="NaoInterpretado"/>: a linha tem critério, mas o valor ou o limite não se leem como número —
/// nada é inventado e a tela sugere "Rever com a IA".</summary>
public sealed record EdicaoDeLinha(Comando Comando, bool Recalculado, bool NaoInterpretado, string? Mensagem);

/// <summary>Recálculo determinístico (sem IA) de uma linha de tabela quando o usuário edita o valor à mão (correções 2026-11-16).
/// Antes, a situação e o texto da diferença eram textos soltos do dados.json: mudar "2,80 m" para "3 m" não mexia em "NÃO ATENDE" nem em
/// "Faltam 0,20 m". Agora, se o valor no projeto e o limite se leem como números da mesma unidade, o editor refaz a comparação e põe
/// tudo num <see cref="Lote"/> (um Desfazer). Se não se leem, não inventa: só a edição entra.</summary>
public static partial class RecalculoLinha
{
    /// <summary>O começo da justificativa que o editor escreve quando o recálculo muda a situação (a conferência final exige justificativa).</summary>
    public const string MarcaJustificativa = "Recalculado pelo editor:";

    static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    // ---------- leitura de números ----------

    [GeneratedRegex(@"[-+]?[0-9][0-9.,]*", RegexOptions.CultureInvariant)]
    private static partial Regex Numeros();

    /// <summary>"3", "3,00", "3.00", "1.234,56", "1.234" (milhar): vírgula é sempre decimal; ponto é decimal, a não ser que venha junto
    /// com a vírgula ou em grupos de três dígitos ("1.234", "12.345.678").</summary>
    public static (decimal Valor, int Casas)? LerNumero(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var t = texto.Trim().TrimEnd('.', ',');
        if (t.Length == 0 || !Regex.IsMatch(t, @"^[-+]?[0-9][0-9.,]*\z", RegexOptions.CultureInvariant)) return null;
        string normal;
        var virgulas = t.Count(c => c == ',');
        var pontos = t.Count(c => c == '.');
        if (virgulas > 1) return null;
        if (virgulas == 1)
        {
            if (t.IndexOf('.') > t.IndexOf(',')) return null;   // "1,234.5": notação estrangeira, ambígua aqui
            normal = t.Replace(".", "", StringComparison.Ordinal).Replace(',', '.');
        }
        else if (pontos == 0) normal = t;
        else if (Regex.IsMatch(t, @"^[-+]?[0-9]{1,3}(\.[0-9]{3})+\z", RegexOptions.CultureInvariant)) normal = t.Replace(".", "", StringComparison.Ordinal);
        else if (pontos == 1) normal = t;
        else return null;
        if (!decimal.TryParse(normal, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v)) return null;
        var ponto = normal.IndexOf('.');
        return (v, ponto < 0 ? 0 : normal.Length - ponto - 1);
    }

    /// <summary>A unidade como se compara: "m2"/"M²" → "m²", "pavimentos"/"pavimento"/"pav." → "pav", sem espaços.</summary>
    static string Unidade(string u)
    {
        u = u.Trim().TrimEnd('.', ';', ',').Trim().ToLower(PtBr).Replace("m2", "m²", StringComparison.Ordinal);
        if (u.StartsWith("pav", StringComparison.Ordinal)) return "pav";
        if (u.StartsWith("vaga", StringComparison.Ordinal)) return "vaga";
        return u;
    }

    static readonly Dictionary<string, decimal> EmMetros = new(StringComparer.Ordinal) { ["m"] = 1m, ["cm"] = 0.01m, ["mm"] = 0.001m };

    /// <summary>Um texto que é UMA medida: um número só, com a unidade depois ("3 m", "360,00 m²", "52%", "2 pavimentos") e, no máximo,
    /// uma palavra curta antes ("cotado 2,80 m"). Dois números, ou nenhum, não é medida (null).</summary>
    public static Medida? LerMedida(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        texto = Regex.Replace(texto, @"(?<=[0-9\s]|^)(m|M)2(?![0-9\p{L}])", "m²", RegexOptions.CultureInvariant);   // "72 m2" é "72 m²", não dois números
        var ms = Numeros().Matches(texto);
        if (ms.Count != 1) return null;
        var m = ms[0];
        var antes = texto[..m.Index].Trim();
        if (antes.Length > 16 || antes.Any(char.IsDigit)) return null;
        if (LerNumero(m.Value) is not var (valor, casas)) return null;
        var depois = texto[(m.Index + m.Length)..].Trim();
        if (m.Value.EndsWith('.') || m.Value.EndsWith(',')) depois = depois.TrimStart();
        // A unidade é a primeira palavra depois do número; o que vier depois dela (um parêntese, uma observação) não muda a medida.
        var unidade = Regex.Match(depois, @"^(%|[\p{L}²³]+\.?)", RegexOptions.CultureInvariant).Value;
        return new Medida(valor, Unidade(unidade), casas);
    }

    [GeneratedRegex(@"^\s*(?:de\s+|entre\s+)?(?<a>[-+]?[0-9][0-9.,]*)\s*(?<ua>%|[\p{L}²³]+)?\s*(?:a|e|até|–|-)\s*(?<b>[0-9][0-9.,]*)\s*(?<ub>%|[\p{L}²³]+)?\s*\.?\s*$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Faixa();

    [GeneratedRegex(@"^\s*(?<op>≥|>=|=>|≤|<=|=<|>|<|no\s+m[íi]nimo|no\s+m[áa]ximo|m[íi]nimo(?:\s+de)?|m[áa]ximo(?:\s+de)?|m[íi]n\.?|m[áa]x\.?|at[ée]|a\s+partir\s+de|pelo\s+menos)\s*:?\s*(?<resto>.+)$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Operador();

    /// <summary>O critério do texto do limite; null se não é um mínimo, um máximo nem uma faixa legíveis ("conforme a zona", "—").</summary>
    public static Criterio? LerCriterio(string? limite)
    {
        if (string.IsNullOrWhiteSpace(limite)) return null;
        var f = Faixa().Match(limite);
        if (f.Success && LerNumero(f.Groups["a"].Value) is var (a, ca) && LerNumero(f.Groups["b"].Value) is var (b, cb) && a <= b)
        {
            var ub = Unidade(f.Groups["ub"].Value);
            var ua = f.Groups["ua"].Success ? Unidade(f.Groups["ua"].Value) : ub;
            if (ua == ub) return new Criterio(new Medida(a, ua, ca), new Medida(b, ub, cb));
        }
        var o = Operador().Match(limite);
        if (!o.Success || LerMedida(o.Groups["resto"].Value) is not { } medida) return null;
        // Depois do operador vem só a medida (nenhuma palavra antes do número).
        if (!Regex.IsMatch(o.Groups["resto"].Value, @"^\s*[-+]?[0-9]", RegexOptions.CultureInvariant)) return null;
        var op = o.Groups["op"].Value.ToLower(PtBr);
        var minimo = op is "≥" or ">=" or "=>" or ">" || op.Contains("mín", StringComparison.Ordinal) || op.Contains("min", StringComparison.Ordinal)
                     || op.StartsWith("a partir", StringComparison.Ordinal) || op.StartsWith("pelo", StringComparison.Ordinal);
        return minimo ? new Criterio(medida, null) : new Criterio(null, medida);
    }

    /// <summary>O valor na unidade do critério; null se as unidades não combinam (m × m², % × m). Comprimentos se convertem (cm → m);
    /// um dos dois sem unidade vale a do outro.</summary>
    static decimal? NaUnidade(Medida valor, string unidade)
    {
        if (valor.Unidade == unidade || valor.Unidade.Length == 0 || unidade.Length == 0) return valor.Valor;
        if (EmMetros.TryGetValue(valor.Unidade, out var de) && EmMetros.TryGetValue(unidade, out var para)) return valor.Valor * de / para;
        return null;
    }

    /// <summary>"0,20 m", "3 %"→"3%", "1.234,50 m²".</summary>
    public static string Formatar(decimal valor, int casas, string unidade)
    {
        var n = valor.ToString("N" + casas.ToString(CultureInfo.InvariantCulture), PtBr);
        return unidade.Length == 0 ? n : unidade == "%" ? n + "%" : n + " " + unidade;
    }

    // ---------- comparação ----------

    /// <summary>O resultado da comparação do valor com o critério: atende ou não, e o texto curto da diferença ("Dentro do limite.",
    /// "Faltam 0,20 m.", "Excede em 5,00%."). Null se as unidades não combinam.</summary>
    public static (bool Atende, string Texto)? Comparar(Medida valor, Criterio c)
    {
        ArgumentNullException.ThrowIfNull(valor);
        ArgumentNullException.ThrowIfNull(c);
        if (NaUnidade(valor, c.Unidade) is not { } v) return null;
        var casas = Math.Max(c.Casas, valor.Unidade == c.Unidade || valor.Unidade.Length == 0 ? valor.Casas : 0);
        if (c.Minimo is { } min && v < min.Valor)
        {
            var falta = min.Valor - v;
            return (false, (falta == 1 ? "Falta " : "Faltam ") + Formatar(falta, Visiveis(falta, casas), c.Unidade) + ".");
        }
        if (c.Maximo is { } max && v > max.Valor) return (false, "Excede em " + Formatar(v - max.Valor, Visiveis(v - max.Valor, casas), c.Unidade) + ".");
        return (true, "Dentro do limite.");
    }

    /// <summary>As casas para uma diferença não sair zero ("Excede em 0,003%" e não "0,0%", quando a comparação é pelo valor exato da conta).</summary>
    static int Visiveis(decimal diferenca, int casas)
    {
        while (casas < 10 && diferenca != 0m && Math.Round(diferenca, casas, MidpointRounding.AwayFromZero) == 0m) casas++;
        return casas;
    }

    // ---------- a edição manual de uma linha ----------

    const string CampoValor = "projeto", CampoLimite = "limite", CampoTexto = "texto";

    [GeneratedRegex(@"\s*\((?:item|itens|ITEM|Item)\s+[0-9]{1,3}(?:\s*(?:,|e)\s*[0-9]{1,3})*\)", RegexOptions.CultureInvariant)]
    private static partial Regex CitacaoFinal();

    /// <summary>A edição manual de <paramref name="campo"/> da linha <paramref name="linha"/>. Só a edição (<see cref="DefinirCampo"/>)
    /// quando o campo não entra em conta nenhuma; senão a edição mais o recálculo do que depende dela, num <see cref="Lote"/>: num índice,
    /// o valor, o limite e o cálculo (<see cref="AoEditarIndice"/>); num ambiente, as esquadrias. Nunca lança por causa do texto: o que
    /// não se lê fica como está.</summary>
    public static EdicaoDeLinha AoEditar(Projeto p, string linha, string campo, string texto)
    {
        ArgumentNullException.ThrowIfNull(p);
        var edicao = new DefinirCampo(linha, campo, texto);
        var simples = new EdicaoDeLinha(edicao, false, false, null);
        if (!TabelasRevisao.TentarLer(linha, out var bloco, out _)) return simples;
        if (bloco == TabelasRevisao.Ambientes) return AoEditarAmbiente(p, linha, campo, texto, simples);
        if (bloco != TabelasRevisao.Indices || campo is not (CampoValor or CampoLimite or CampoCalculo)) return simples;
        return AoEditarIndice(p, linha, campo, texto, simples);
    }

    [GeneratedRegex(@"^(vaos/[0-9]+/medidas|minimo/[0-9]+/texto)\z", RegexOptions.CultureInvariant)]
    private static partial Regex CampoDeConta();

    /// <summary>A edição manual das medidas de uma esquadria (ou do mínimo exigido) de um ambiente: a área do vão, o vão total, a
    /// comparação com o mínimo e a situação são refeitos (<see cref="Esquadrias.Recalcular"/>). Medidas que não são "largura × altura":
    /// só a edição entra, com o aviso.</summary>
    static EdicaoDeLinha AoEditarAmbiente(Projeto p, string linha, string campo, string texto, EdicaoDeLinha simples)
    {
        if (!CampoDeConta().IsMatch(campo) || TabelasRevisao.Original(p, linha) is not { } original || !TabelasRevisao.Caminhos(original).Contains(campo)) return simples;
        var novo = TextoEditado.Normalizar(texto);
        if (novo.Length == 0 || novo == TabelasRevisao.TextoEfetivo(p, linha, campo)) return simples;
        var rotulo = TabelasRevisao.Rotulo(linha);
        if (campo.EndsWith("/medidas", StringComparison.Ordinal) && Esquadrias.LerMedidas(novo) is null)
            return new EdicaoDeLinha(simples.Comando, false, true,
                rotulo + ": o texto mudou, mas \"" + novo + "\" não é largura × altura; a área, o vão total e a situação ficaram como estavam. " +
                "Confira-os, ou escreva na justificativa o que rever e use «Rever com a IA» nas Propriedades.");
        Lote lote;
        try { lote = Esquadrias.Recalcular(p, linha, [simples.Comando], "Recalculado: " + TabelasRevisao.NomeDoCampo(campo) + " " + novo); }
        catch (ComandoRecusadoException) { return simples; }   // a recusa aparece quando a edição for aplicada
        if (lote.Comandos.Count <= 1) return simples;
        var depois = p.Clonar();
        foreach (var c in lote.Comandos)
            if (Motor.Calcular(depois, c) is { } ef) Motor.Definir(depois, ef.Tipo, ef.Item, ef.Campo, ef.Depois);
        var conta = Esquadrias.Calcular(depois, linha);
        var mensagem = rotulo + " recalculado pelo editor (sem IA): vão total " + (conta?.TextoTotal ?? "—")
                       + (conta?.Minimo is not null ? "; mínimo " + conta.TextoMinimo : "")
                       + "; situação " + ConferenciaRevisao.TextoSituacao(TabelasRevisao.SituacaoEfetiva(depois, linha))
                       + ". Confira o texto da situação; um Desfazer volta tudo.";
        return new EdicaoDeLinha(lote, true, false, mensagem);
    }

    [GeneratedRegex(@"^\s*(Faltam?\s+[0-9]|Excede\s+em\s+[0-9]|Dentro\s+do\s+limite|Atende\b|Acima\s+do\s+(limite|m[áa]ximo)|Abaixo\s+do\s+(limite|m[íi]nimo))", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex TextoDeDiferenca();

    static bool EhTextoDeDiferenca(string texto) => TextoDeDiferenca().IsMatch(texto);
}
