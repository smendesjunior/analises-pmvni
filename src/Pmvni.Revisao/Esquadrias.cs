using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>A categoria de uma esquadria na tela (o painel as agrupa: Janelas, Portas, Outros vãos).</summary>
public enum CategoriaEsquadria { Janela, Porta, Outro }

/// <summary>Uma esquadria (vão) de um ambiente, lida dos textos da linha: o código ("J1"), o tipo, as medidas (largura × altura em
/// metros, quando legíveis) e a área — a escrita no relatório e a calculada (largura × altura). <see cref="Acrescentada"/>: criada
/// pelo usuário. <see cref="Conta"/>: entra no vão total (tem área escrita ou calculável e um lugar para escrevê-la).</summary>
public sealed record Esquadria(int Indice, string Codigo, string Tipo, string Medidas, CategoriaEsquadria Categoria,
    decimal? Largura, decimal? Altura, string AreaEscrita, decimal? Area, bool TemCampoDeArea, bool Acrescentada)
{
    public bool Conta => Area is not null && TemCampoDeArea;
    public string Caminho(string parte) => "vaos/" + Indice.ToString(CultureInfo.InvariantCulture) + "/" + parte;
}

/// <summary>A conta de iluminação e ventilação de um ambiente: as esquadrias, o vão total (a soma das áreas), o mínimo exigido (a área do
/// piso ÷ o divisor, quando o texto do mínimo traz a conta) e se atende. <see cref="Atende"/> null = não dá para comparar (sem mínimo
/// legível ou sem nenhuma área).</summary>
public sealed record ContaAmbiente(IReadOnlyList<Esquadria> Esquadrias, decimal? Total, decimal? Piso, decimal? Divisor, decimal? Minimo, bool? Atende)
{
    /// <summary>"3,48 m²".</summary>
    public string TextoTotal => Total is { } t ? Esquadrias_.M2(t) : "—";

    /// <summary>"12,00 ÷ 6 = 2,00 m²" ou "2,00 m²".</summary>
    public string TextoMinimo => Minimo is not { } m ? "—"
        : Piso is { } p && Divisor is { } d ? Esquadrias_.N(p) + " ÷ " + d.ToString("0.##", Esquadrias_.PtBr) + " = " + Esquadrias_.M2(m)
        : Esquadrias_.M2(m);

    /// <summary>"J1 1,80 + P1 1,68 = 3,48 m²" (uma só: "J1 1,80 m²").</summary>
    public string TextoConta
    {
        get
        {
            var contam = Esquadrias.Where(e => e.Conta).ToList();
            if (contam.Count == 0 || Total is not { } t) return "—";
            if (contam.Count == 1) return contam[0].Codigo + " " + Esquadrias_.M2(t);
            return string.Join(" + ", contam.Select(e => e.Codigo + " " + Esquadrias_.N(e.Area!.Value))) + " = " + Esquadrias_.M2(t);
        }
    }

    /// <summary>"Mínimo 2,00 m²; atende." / "Mínimo 2,00 m²; faltam 0,20 m²."</summary>
    public string TextoComparacao => Minimo is not { } m || Total is not { } t ? "—"
        : "Mínimo " + Esquadrias_.M2(m) + (t >= m ? "; atende." : "; " + (m - t == 1 ? "falta " : "faltam ") + Esquadrias_.M2(m - t) + ".");
}

/// <summary>Formatação comum (pt-BR, duas casas).</summary>
internal static class Esquadrias_
{
    internal static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    internal static string N(decimal v) => v.ToString("N2", PtBr);
    internal static string M2(decimal v) => N(v) + " m²";
}

/// <summary>As esquadrias de um ambiente (correções 2026-11-16): leitura das medidas, cálculo determinístico (área = largura × altura;
/// vão total = soma; mínimo = piso ÷ divisor; atende se o total ≥ o mínimo) e os comandos que gravam o resultado. O painel de
/// Propriedades usa <see cref="Calcular"/> a cada tecla (com os textos em edição) e <see cref="Recalcular"/> ao aplicar; a edição das
/// medidas na folha e a esquadria pedida à IA passam pelo mesmo <see cref="Recalcular"/>.</summary>
public static partial class Esquadrias
{
    // "1,50 × 1,20 m", "1.96 x 1.26 m", "80x210cm", "0,80 m × 2,10 m", "80 × 210".
    [GeneratedRegex(@"^\s*(?<l>[0-9][0-9.,]*)\s*(?<ul>mm|cm|m)?\s*[×xX*]\s*(?<a>[0-9][0-9.,]*)\s*(?<ua>mm|cm|m)?\s*\.?\s*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Dimensoes();

    /// <summary>Largura e altura em metros; null se o texto não é "largura × altura". Sem unidade: metros, a não ser que o número seja
    /// grande demais para um vão (mais de 20), quando são centímetros ("80x210").</summary>
    public static (decimal Largura, decimal Altura)? LerMedidas(string? medidas)
    {
        if (string.IsNullOrWhiteSpace(medidas)) return null;
        var m = Dimensoes().Match(medidas);
        if (!m.Success || RecalculoLinha.LerNumero(m.Groups["l"].Value) is not var (l, _) || RecalculoLinha.LerNumero(m.Groups["a"].Value) is not var (a, _)) return null;
        var ua = m.Groups["ua"].Success ? m.Groups["ua"].Value.ToLowerInvariant() : null;
        var ul = m.Groups["ul"].Success ? m.Groups["ul"].Value.ToLowerInvariant() : ua;
        ua ??= ul;
        static decimal EmMetros(decimal v, string? u) => u switch { "cm" => v / 100m, "mm" => v / 1000m, "m" => v, _ => v > 20m ? v / 100m : v };
        var (largura, altura) = (EmMetros(l, ul), EmMetros(a, ua));
        return largura > 0 && altura > 0 ? (largura, altura) : null;
    }

    /// <summary>"0,80 × 2,10 m": as medidas como o relatório as escreve.</summary>
    public static string TextoMedidas(decimal largura, decimal altura) => Esquadrias_.N(largura) + " × " + Esquadrias_.N(altura) + " m";

    /// <summary>A área do vão, arredondada como o relatório mostra (duas casas).</summary>
    public static decimal Area(decimal largura, decimal altura) => Math.Round(largura * altura, 2, MidpointRounding.AwayFromZero);

    public static string TextoArea(decimal area) => Esquadrias_.M2(area);

    public static CategoriaEsquadria Categoria(string codigo, string tipo)
    {
        var t = (tipo ?? "").ToLowerInvariant();
        if (t.Contains("porta", StringComparison.Ordinal)) return CategoriaEsquadria.Porta;
        if (t.Contains("janela", StringComparison.Ordinal) || t.Contains("bascul", StringComparison.Ordinal) || t.Contains("maxim", StringComparison.Ordinal)
            || t.Contains("venezian", StringComparison.Ordinal) || t.Contains("vitr", StringComparison.Ordinal)) return CategoriaEsquadria.Janela;
        var c = (codigo ?? "").TrimStart().ToUpperInvariant();
        return c.StartsWith('P') ? CategoriaEsquadria.Porta : c.StartsWith('J') ? CategoriaEsquadria.Janela : CategoriaEsquadria.Outro;
    }

    /// <summary>O próximo código livre da categoria no ambiente: "P1", "P2"…; "J3".</summary>
    public static string ProximoCodigo(IReadOnlyList<Esquadria> esquadrias, CategoriaEsquadria categoria)
    {
        var letra = categoria == CategoriaEsquadria.Porta ? "P" : categoria == CategoriaEsquadria.Janela ? "J" : "V";
        for (var n = 1; ; n++)
        {
            var codigo = letra + n.ToString(CultureInfo.InvariantCulture);
            if (!esquadrias.Any(e => string.Equals(e.Codigo.Trim(), codigo, StringComparison.OrdinalIgnoreCase))) return codigo;
        }
    }

    // "12,00 ÷ 6 = 2,00 m²" (a conta do mínimo: piso ÷ divisor).
    [GeneratedRegex(@"(?<p>[0-9][0-9.,]*)\s*(?:m²|m2)?\s*[÷/:]\s*(?<d>[0-9][0-9.,]*)\s*=\s*(?<m>[0-9][0-9.,]*)\s*(?:m²|m2)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ContaDoMinimo();

    // "mínimo 0,25 m²", "≥ 2,00 m²", "2,00 m²".
    [GeneratedRegex(@"(?<m>[0-9][0-9.,]*)\s*(?:m²|m2)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AreaSolta();

    /// <summary>O mínimo exigido lido dos textos "Mínimo exigido" da linha: com a conta (piso ÷ divisor = mínimo), o mínimo é REFEITO
    /// (piso ÷ divisor, duas casas); sem a conta, o valor em m² que estiver escrito (um só).</summary>
    public static (decimal? Piso, decimal? Divisor, decimal? Minimo) LerMinimo(IEnumerable<string> textos)
    {
        foreach (var t in textos)
        {
            var m = ContaDoMinimo().Match(t ?? "");
            if (m.Success && RecalculoLinha.LerNumero(m.Groups["p"].Value) is var (p, _) && RecalculoLinha.LerNumero(m.Groups["d"].Value) is var (d, _) && d > 0)
                return (p, d, Math.Round(p / d, 2, MidpointRounding.AwayFromZero));
        }
        foreach (var t in textos)
        {
            var ms = AreaSolta().Matches(t ?? "");
            if (ms.Count == 1 && RecalculoLinha.LerNumero(ms[0].Groups["m"].Value) is var (v, _)) return (null, null, v);
        }
        return (null, null, null);
    }

    /// <summary>A conta do ambiente com os textos de agora. <paramref name="emEdicao"/>: textos que o usuário está digitando no painel
    /// (caminho → texto), que valem no lugar dos gravados — é o cálculo em tempo real, sem gravar nada.</summary>
    public static ContaAmbiente? Calcular(Projeto p, string ambiente, IReadOnlyDictionary<string, string>? emEdicao = null)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (!TabelasRevisao.TentarLer(ambiente, out var bloco, out _) || bloco != TabelasRevisao.Ambientes || TabelasRevisao.Original(p, ambiente) is not { } original) return null;
        var caminhos = TabelasRevisao.Caminhos(original);
        string Texto(string caminho) =>
            emEdicao is not null && emEdicao.TryGetValue(caminho, out var digitado) ? digitado : TabelasRevisao.TextoEfetivo(p, ambiente, caminho) ?? "";

        var lista = new List<Esquadria>();
        var n = (original["vaos"] as JsonArray)?.Count ?? 0;
        for (var i = 0; i < n; i++)
        {
            var e = new Esquadria(i, "", "", "", CategoriaEsquadria.Outro, null, null, "", null, false, false);
            var codigo = Texto(e.Caminho("codigo"));
            var tipo = Texto(e.Caminho("tipo"));
            var medidas = Texto(e.Caminho("medidas"));
            var areaEscrita = Texto(e.Caminho("area"));
            var temCampo = caminhos.Contains(e.Caminho("area"));
            var lidas = LerMedidas(medidas);
            // A área vem das medidas (largura × altura); sem medidas legíveis, vale a que está escrita.
            decimal? area = lidas is var (l, a) ? Area(l, a) : RecalculoLinha.LerMedida(areaEscrita) is { Unidade: "m²" or "" } escrita ? escrita.Valor : null;
            lista.Add(new Esquadria(i, codigo, tipo, medidas, Categoria(codigo, tipo), lidas?.Largura, lidas?.Altura, areaEscrita, area, temCampo,
                TabelasRevisao.Acrescentado(p, ambiente, "vaos/" + i.ToString(CultureInfo.InvariantCulture))));
        }
        var contam = lista.Where(e => e.Conta).ToList();
        decimal? total = contam.Count == 0 ? null : contam.Sum(e => e.Area!.Value);
        var (piso, divisor, minimo) = LerMinimo(caminhos.Where(c => c.StartsWith("minimo/", StringComparison.Ordinal)).Select(Texto).ToList());
        bool? atende = total is { } t && minimo is { } m ? t >= m : null;
        return new ContaAmbiente(lista, total, piso, divisor, minimo, atende);
    }

    /// <summary>As alterações (<paramref name="edicoes"/>: medidas editadas, esquadria acrescentada ou removida…) mais o recálculo
    /// determinístico do ambiente depois delas — a área de cada vão, o vão total, a conta, a comparação com o mínimo, a situação e a
    /// justificativa do editor —, tudo num <see cref="Lote"/> (um Desfazer). Só grava nos textos que existem na linha; o texto livre da
    /// situação não é tocado. Recusa das edições: <see cref="ComandoRecusadoException"/>, como sempre.</summary>
    public static Lote Recalcular(Projeto p, string ambiente, IReadOnlyList<Comando> edicoes, string descricao)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(edicoes);
        var comandos = new List<Comando>(edicoes);
        // O projeto como fica depois das edições (numa cópia): é sobre ele que a conta é refeita.
        var depois = p.Clonar();
        foreach (var c in edicoes)
            if (Motor.Calcular(depois, c) is { } ef) Motor.Definir(depois, ef.Tipo, ef.Item, ef.Campo, ef.Depois);
        if (Calcular(depois, ambiente) is not { } conta || TabelasRevisao.Original(depois, ambiente) is not { } original) return new Lote(ambiente, comandos, descricao);
        var caminhos = TabelasRevisao.Caminhos(original);
        void Gravar(string caminho, string texto)
        {
            if (caminhos.Contains(caminho) && TabelasRevisao.TextoEfetivo(depois, ambiente, caminho) != texto) comandos.Add(new DefinirCampo(ambiente, caminho, texto));
        }
        foreach (var e in conta.Esquadrias)
            if (e.Largura is not null && e.Area is { } area && e.TemCampoDeArea) Gravar(e.Caminho("area"), TextoArea(area));
        if (conta.Total is not null)
        {
            Gravar("resultado/total", conta.TextoTotal);
            Gravar("resultado/conta", conta.TextoConta);
            if (conta.Minimo is not null) Gravar("resultado/comparacao", conta.TextoComparacao);
        }
        if (conta.Atende is { } atende && TabelasRevisao.SituacaoEfetiva(depois, ambiente) is { } situacao)
        {
            var nova = atende ? "CONTA_CONFERE" : "NAO_ATENDE";
            if (nova != situacao) comandos.Add(new DefinirSituacao(ambiente, nova));
            var justificativa = TabelasRevisao.Justificativa(depois, ambiente);
            if (string.IsNullOrWhiteSpace(justificativa) || justificativa!.StartsWith(RecalculoLinha.MarcaJustificativa, StringComparison.Ordinal))
            {
                var volta = nova == TabelasRevisao.SituacaoOriginal(depois, ambiente);
                var texto = volta ? null : RecalculoLinha.MarcaJustificativa + " vão total " + conta.TextoTotal + "; mínimo " + Esquadrias_.M2(conta.Minimo!.Value) + ".";
                if (texto != justificativa && !(volta && string.IsNullOrWhiteSpace(justificativa))) comandos.Add(new DefinirJustificativa(ambiente, texto));
            }
        }
        return new Lote(ambiente, comandos, descricao);
    }

    /// <summary>O comando de uma esquadria nova com a área já calculada das medidas. Medidas ilegíveis: <see cref="ComandoRecusadoException"/>.</summary>
    public static AcrescentarVao Nova(string ambiente, string codigo, string tipo, string medidas)
    {
        if (LerMedidas(medidas) is not var (l, a))
            throw new ComandoRecusadoException("As medidas da esquadria têm de ser largura × altura (por exemplo, 0,80 × 2,10 m).");
        return new AcrescentarVao(ambiente, codigo, TextoMedidas(l, a), tipo, TextoArea(Area(l, a)));
    }
}
