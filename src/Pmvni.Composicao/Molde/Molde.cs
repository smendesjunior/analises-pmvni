using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Pmvni.Composicao.Kit;

namespace Pmvni.Composicao;

/// <summary>Porta fiel da mini-linguagem de marcadores do kit (<c>compilar</c>, <c>valor</c>, <c>render</c>
/// de gerar_relatorio.py): <c>{{ a.b.0|filtro }}</c>, <c>{% for %}</c>, <c>{% if %}/{% else %}</c>, <c>{# #}</c>.</summary>
public sealed class Molde
{
    private abstract record No;
    private sealed record Texto(string Valor) : No;
    private sealed record Var(string Expr) : No;
    private sealed record TextoRevisavel(string Slot, string Valor, bool Variavel) : No;
    private sealed record Para(string Nome, string Expr, List<No> Corpo) : No;
    private sealed record Se(string Expr, List<No> Corpo, List<No> Outro) : No;

    private static readonly Regex Tok = new(@"(\{\{.*?\}\}|\{%.*?%\}|\{#.*?#\})", RegexOptions.Singleline | RegexOptions.CultureInvariant);
    private static readonly Regex ParaRegex = new(@"\Afor\s+(\w+)\s+in\s+(.+)\z", RegexOptions.CultureInvariant);

    /// <summary>Filtros que não dependem de dados do kit (os demais — pill, cor, icone — vêm de fora).</summary>
    public static IReadOnlyDictionary<string, Func<object?, object?>> FiltrosBasicos { get; } =
        new Dictionary<string, Func<object?, object?>>
        {
            ["safe"] = v => new Seguro(v is null ? "" : PythonTexto.StrPython(v)),
            ["n2"] = PythonTexto.DoisDigitos,
            ["lower"] = v => PythonTexto.Minusculas(PythonTexto.StrPython(v)),
            ["upper"] = v => PythonTexto.Maiusculas(PythonTexto.StrPython(v)),
        };

    private readonly List<No> _nos;

    private Molde(List<No> nos) => _nos = nos;

    public static Molde Compilar(string fonte)
    {
        var toks = Tok.Split(fonte);
        var pos = 0;
        return new Molde(Bloco(toks, ref pos, Array.Empty<string>()).Nos);
    }

    private static (List<No> Nos, string? Fim) Bloco(string[] toks, ref int pos, string[] fins)
    {
        var saida = new List<No>();
        while (pos < toks.Length)
        {
            var t = toks[pos++];
            if (t.StartsWith("{#", StringComparison.Ordinal)) continue;
            if (t.StartsWith("{{", StringComparison.Ordinal))
            {
                saida.Add(new Var(PyStrip(Fatia(t))));
                continue;
            }
            if (t.StartsWith("{%", StringComparison.Ordinal))
            {
                var tag = PyStrip(Fatia(t));
                var partes = tag.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
                if (partes.Length == 0) throw new MoldeErro("list index out of range");
                var kw = partes[0];
                if (Array.IndexOf(fins, kw) >= 0) return (saida, kw);
                if (kw is "texto_fixo" or "texto_variavel")
                {
                    var trecho = tag.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                    saida.Add(new TextoRevisavel(trecho[1], trecho[2], kw == "texto_variavel"));
                    continue;
                }
                if (kw == "for")
                {
                    var (corpo, _) = Bloco(toks, ref pos, new[] { "endfor" });
                    var m = ParaRegex.Match(tag);
                    if (!m.Success) throw new MoldeErro("'NoneType' object is not subscriptable");
                    saida.Add(new Para(m.Groups[1].Value, PyStrip(m.Groups[2].Value), corpo));
                    continue;
                }
                if (kw == "if")
                {
                    var (corpo, fim) = Bloco(toks, ref pos, new[] { "else", "endif" });
                    var outro = new List<No>();
                    if (fim == "else") outro = Bloco(toks, ref pos, new[] { "endif" }).Nos;
                    saida.Add(new Se(PyStrip(tag[2..]), corpo, outro));
                    continue;
                }
                throw new MoldeErro("Marcador desconhecido no modelo: {% " + tag + " %}");
            }
            saida.Add(new Texto(t));
        }
        if (fins.Length > 0) throw new MoldeErro("Bloco sem fechamento no modelo: " + string.Join("/", fins));
        return (saida, null);
    }

    // t[2:-2] do Python (vazio se o pedaço for curto demais, ex.: texto "{{x")
    private static string Fatia(string t) => t.Length > 4 ? t[2..^2] : "";

    private static string PyStrip(string s) => s.Trim();

    public string Renderizar(IDictionary<string, object?> ctx) => Renderizar(ctx, FiltrosBasicos);

    public string Renderizar(IDictionary<string, object?> ctx, IReadOnlyDictionary<string, Func<object?, object?>> filtros) =>
        Renderizar(ctx, filtros, null);

    internal string Renderizar(IDictionary<string, object?> ctx, IReadOnlyDictionary<string, Func<object?, object?>> filtros, TextosDoModelo? textos)
    {
        var sb = new StringBuilder();
        Render(_nos, ctx, filtros, sb, textos, "");
        return sb.ToString();
    }

    private static void Render(List<No> nos, IDictionary<string, object?> ctx,
        IReadOnlyDictionary<string, Func<object?, object?>> filtros, StringBuilder sb, TextosDoModelo? textos, string ocorrencia)
    {
        foreach (var no in nos)
        {
            switch (no)
            {
                case Texto t: sb.Append(t.Valor); break;
                case TextoRevisavel t:
                    var original = t.Variavel
                        ? PythonTexto.Escape(Valor(t.Valor, ctx, filtros) is { } v ? PythonTexto.StrPython(v) : "")
                        : System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(t.Valor));
                    if (textos is null) sb.Append(original);
                    else textos.Anexar(sb, t.Slot, ocorrencia, original);
                    break;
                case Var variavel:
                    var val = Valor(variavel.Expr, ctx, filtros);
                    if (val is Seguro sg) sb.Append(sg.Valor);
                    else sb.Append(PythonTexto.Escape(val is null ? "" : PythonTexto.StrPython(val)));
                    break;
                case Se se:
                    Render(PythonTexto.Verdadeiro(Valor(se.Expr, ctx, filtros)) ? se.Corpo : se.Outro, ctx, filtros, sb, textos, ocorrencia);
                    break;
                case Para p:
                    var bruto = Valor(p.Expr, ctx, filtros);
                    if (!PythonTexto.Verdadeiro(bruto)) break;
                    var seq = Iterar(bruto);
                    for (var i = 0; i < seq.Count; i++)
                    {
                        if (seq[i] is IReadOnlyDictionary<string, object?> entrada &&
                            Equals(Dados.Obter(entrada, "_oculto_na_revisao"), true)) continue;
                        var c = new Dictionary<string, object?>(ctx)
                        {
                            [p.Nome] = seq[i],
                        };
                        c["loop"] = new Dictionary<string, object?>
                        {
                            ["index"] = (long)(i + 1),
                            ["n2"] = (i + 1).ToString("00", CultureInfo.InvariantCulture),
                            ["first"] = i == 0,
                            ["last"] = i == seq.Count - 1,
                            ["even"] = i % 2 == 1,
                        };
                        var coletar = p.Expr is "indices.linhas" or "iluminacao.ambientes" or "itens" ? null : textos;
                        var identidade = seq[i] is IReadOnlyDictionary<string, object?> dado && Dados.Obter(dado, "id") is string id
                            ? Uri.EscapeDataString(id) : i.ToString(CultureInfo.InvariantCulture);
                        Render(p.Corpo, c, filtros, sb, coletar, ocorrencia + "/" + identidade);
                    }
                    break;
            }
        }
    }

    // Iteração do Python sobre o valor: lista -> itens; dict -> chaves; str -> caracteres.
    private static List<object?> Iterar(object? v)
    {
        switch (v)
        {
            case string s: return s.EnumerateRunes().Select(r => (object?)r.ToString()).ToList();
            case Seguro sg: return sg.Valor.EnumerateRunes().Select(r => (object?)r.ToString()).ToList();
            case IReadOnlyDictionary<string, object?> rd: return rd.Keys.Cast<object?>().ToList();
            case IDictionary<string, object?> dd: return dd.Keys.Cast<object?>().ToList();
            case System.Collections.IEnumerable e and not string: return e.Cast<object?>().ToList();
            default: throw new MoldeErro("'" + PythonTexto.NomeTipo(v) + "' object is not iterable");
        }
    }

    private static object? Valor(string expr, IDictionary<string, object?> ctx,
        IReadOnlyDictionary<string, Func<object?, object?>> filtros)
    {
        var partes = expr.Split('|').Select(PyStrip).ToArray();
        var b = partes[0];
        if (b.StartsWith("not ", StringComparison.Ordinal)) return !PythonTexto.Verdadeiro(Valor(b[4..], ctx, filtros));
        if (b.Length == 0) throw new MoldeErro("string index out of range");
        object? v;
        if (b[0] == b[^1] && (b[0] == '"' || b[0] == '\''))
        {
            v = b.Length >= 2 ? b[1..^1] : "";
        }
        else
        {
            v = ctx;
            foreach (var p in b.Split('.'))
            {
                if (v is IReadOnlyDictionary<string, object?> rd) v = rd.TryGetValue(p, out var x) ? x : null;
                else if (v is IDictionary<string, object?> dd) v = dd.TryGetValue(p, out var y) ? y : null;
                else if (v is System.Collections.IList lista && v is not string && SoDigitos(p))
                    v = long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var idx) && idx < lista.Count ? lista[(int)idx] : null;
                else if (v is IReadOnlyList<object?> rl && SoDigitos(p))
                    v = long.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var idx2) && idx2 < rl.Count ? rl[(int)idx2] : null;
                else v = null;
                if (v is null) break;
            }
        }
        for (var i = 1; i < partes.Length; i++)
        {
            if (!filtros.TryGetValue(partes[i], out var f)) throw new MoldeErro("Filtro desconhecido: " + partes[i]);
            v = f(v);
        }
        return v;
    }

    private static bool SoDigitos(string p)
    {
        if (p.Length == 0) return false;
        foreach (var c in p) if (c < '0' || c > '9') return false;
        return true;
    }
}
