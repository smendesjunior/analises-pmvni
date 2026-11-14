using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace Pmvni.Composicao;

/// <summary>Texto marcado como seguro (equivale ao <c>Seguro(str)</c> do kit): não é escapado na saída.</summary>
public sealed record Seguro(string Valor);

/// <summary>Erro do molde (equivale ao ValueError/TypeError do Python do kit).</summary>
public sealed class MoldeErro : Exception
{
    public MoldeErro(string mensagem) : base(mensagem) { }
}

/// <summary>Reproduz, para valores vindos de JSON, o comportamento de <c>str()</c>, <c>repr()</c>,
/// <c>html.escape()</c> e da veracidade do Python.</summary>
public static class PythonTexto
{
    /// <summary>html.escape(s) do Python (quote=True).</summary>
    public static string Escape(string s)
    {
        if (s.AsSpan().IndexOfAny("&<>\"'") < 0) return s;
        var sb = new StringBuilder(s.Length + 16);
        foreach (var c in s)
        {
            switch (c)
            {
                case '&': sb.Append("&amp;"); break;
                case '<': sb.Append("&lt;"); break;
                case '>': sb.Append("&gt;"); break;
                case '"': sb.Append("&quot;"); break;
                case '\'': sb.Append("&#x27;"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Como o gerador imprime: None vira "" (o resto é <c>str(v)</c>).</summary>
    public static string Str(object? v) => v is null ? "" : StrPython(v);

    /// <summary><c>str(v)</c> literal do Python (None vira "None").</summary>
    public static string StrPython(object? v) => v switch
    {
        null => "None",
        string s => s,
        Seguro sg => sg.Valor,
        _ => Repr(v),
    };

    /// <summary><c>repr(v)</c> do Python.</summary>
    public static string Repr(object? v)
    {
        switch (v)
        {
            case null: return "None";
            case bool b: return b ? "True" : "False";
            case string s: return ReprTexto(s);
            case Seguro sg: return ReprTexto(sg.Valor);
            case double d: return ReprDouble(d);
            case float f: return ReprDouble(double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture));
            case decimal m: return m.ToString(CultureInfo.InvariantCulture);
            case BigInteger bi: return bi.ToString(CultureInfo.InvariantCulture);
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return Convert.ToString(v, CultureInfo.InvariantCulture)!;
            case IReadOnlyDictionary<string, object?> rd:
                return "{" + string.Join(", ", rd.Select(p => ReprTexto(p.Key) + ": " + Repr(p.Value))) + "}";
            case IDictionary<string, object?> dd:
                return "{" + string.Join(", ", dd.Select(p => ReprTexto(p.Key) + ": " + Repr(p.Value))) + "}";
            case IEnumerable<object?> lista:
                return "[" + string.Join(", ", lista.Select(Repr)) + "]";
            default: return v.ToString() ?? "";
        }
    }

    /// <summary>Veracidade do Python: None/False/0/0.0/""/lista ou dict vazio são falsos.</summary>
    public static bool Verdadeiro(object? v) => v switch
    {
        null => false,
        bool b => b,
        string s => s.Length > 0,
        Seguro sg => sg.Valor.Length > 0,
        double d => d != 0.0,
        float f => f != 0.0f,
        decimal m => m != 0m,
        BigInteger bi => !bi.IsZero,
        sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToDecimal(v, CultureInfo.InvariantCulture) != 0m,
        IReadOnlyDictionary<string, object?> rd => rd.Count > 0,
        IDictionary<string, object?> dd => dd.Count > 0,
        ICollection c => c.Count > 0,
        IReadOnlyCollection<object?> rc => rc.Count > 0,
        _ => true,
    };

    // ---- float: repr do Python (menor representação que volta ao mesmo número) ----

    public static string ReprDouble(double d)
    {
        if (double.IsNaN(d)) return "nan";
        if (double.IsPositiveInfinity(d)) return "inf";
        if (double.IsNegativeInfinity(d)) return "-inf";
        var neg = d < 0 || (d == 0 && double.IsNegative(d));
        var r = Math.Abs(d).ToString("R", CultureInfo.InvariantCulture); // menor ida-e-volta: ex. "1E-05", "123.45"
        var e10 = 0;
        var ie = r.IndexOfAny(new[] { 'E', 'e' });
        var mant = r;
        if (ie >= 0)
        {
            e10 = int.Parse(r[(ie + 1)..], CultureInfo.InvariantCulture);
            mant = r[..ie];
        }
        var ponto = mant.IndexOf('.');
        var digitos = ponto >= 0 ? mant.Remove(ponto, 1) : mant;
        var decpt = (ponto >= 0 ? ponto : mant.Length) + e10;
        var zerosEsq = 0;
        while (zerosEsq < digitos.Length && digitos[zerosEsq] == '0') zerosEsq++;
        digitos = digitos[zerosEsq..];
        decpt -= zerosEsq;
        digitos = digitos.TrimEnd('0');
        string corpo;
        if (digitos.Length == 0)
        {
            corpo = "0.0";
        }
        else
        {
            var exp = decpt - 1;
            if (exp < -4 || exp >= 16)
            {
                var m = digitos.Length > 1 ? digitos[0] + "." + digitos[1..] : digitos;
                corpo = m + "e" + (exp < 0 ? "-" : "+") + Math.Abs(exp).ToString("00", CultureInfo.InvariantCulture);
            }
            else if (decpt <= 0)
            {
                corpo = "0." + new string('0', -decpt) + digitos;
            }
            else if (digitos.Length <= decpt)
            {
                corpo = digitos + new string('0', decpt - digitos.Length) + ".0";
            }
            else
            {
                corpo = digitos[..decpt] + "." + digitos[decpt..];
            }
        }
        return neg ? "-" + corpo : corpo;
    }

    // ---- str: repr do Python ----

    public static string ReprTexto(string s)
    {
        var aspas = s.Contains('\'') && !s.Contains('"') ? '"' : '\'';
        var sb = new StringBuilder(s.Length + 2);
        sb.Append(aspas);
        foreach (var rune in s.EnumerateRunes())
        {
            var cp = rune.Value;
            if (cp == aspas || cp == '\\') { sb.Append('\\').Append((char)cp); }
            else if (cp == '\t') sb.Append("\\t");
            else if (cp == '\n') sb.Append("\\n");
            else if (cp == '\r') sb.Append("\\r");
            else if (cp < 0x20 || cp == 0x7f) sb.Append("\\x").Append(cp.ToString("x2", CultureInfo.InvariantCulture));
            else if (cp < 0x7f || Imprimivel(rune)) sb.Append(rune.ToString());
            else if (cp <= 0xff) sb.Append("\\x").Append(cp.ToString("x2", CultureInfo.InvariantCulture));
            else if (cp <= 0xffff) sb.Append("\\u").Append(cp.ToString("x4", CultureInfo.InvariantCulture));
            else sb.Append("\\U").Append(cp.ToString("x8", CultureInfo.InvariantCulture));
        }
        sb.Append(aspas);
        return sb.ToString();
    }

    private static bool Imprimivel(Rune r)
    {
        switch (Rune.GetUnicodeCategory(r))
        {
            case UnicodeCategory.Control:
            case UnicodeCategory.Format:
            case UnicodeCategory.Surrogate:
            case UnicodeCategory.PrivateUse:
            case UnicodeCategory.OtherNotAssigned:
            case UnicodeCategory.LineSeparator:
            case UnicodeCategory.ParagraphSeparator:
            case UnicodeCategory.SpaceSeparator:
                return false;
            default:
                return true;
        }
    }

    // ---- str.lower() / str.upper() com as regras especiais do Python ----

    public static string Minusculas(string s)
    {
        var sb = new StringBuilder(s.Length);
        var runes = s.EnumerateRunes().ToArray();
        for (var i = 0; i < runes.Length; i++)
        {
            var r = runes[i];
            if (r.Value == 0x130) { sb.Append("i̇"); continue; } // İ
            if (r.Value == 0x3A3) // Σ: sigma final
            {
                var antes = false;
                for (var j = i - 1; j >= 0; j--)
                {
                    if (Ignoravel(runes[j])) continue;
                    antes = Rune.IsLetter(runes[j]);
                    break;
                }
                var depois = false;
                for (var j = i + 1; j < runes.Length; j++)
                {
                    if (Ignoravel(runes[j])) continue;
                    depois = Rune.IsLetter(runes[j]);
                    break;
                }
                sb.Append(antes && !depois ? 'ς' : 'σ');
                continue;
            }
            sb.Append(Rune.ToLowerInvariant(r).ToString());
        }
        return sb.ToString();
    }

    private static bool Ignoravel(Rune r)
    {
        var c = Rune.GetUnicodeCategory(r);
        return c is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format
            || r.Value == '\'' || r.Value == 0x2019 || r.Value == '.' || r.Value == ':' || r.Value == 0xB7;
    }

    private static readonly Dictionary<int, string> UpperEspecial = new()
    {
        [0xDF] = "SS", [0x149] = "ʼN", [0x1F0] = "J̌", [0x390] = "Ϊ́", [0x3B0] = "Ϋ́",
        [0x587] = "ԵՒ", [0x1E96] = "H̱", [0x1E97] = "T̈", [0x1E98] = "W̊", [0x1E99] = "Y̊", [0x1E9A] = "Aʾ",
        [0xFB00] = "FF", [0xFB01] = "FI", [0xFB02] = "FL", [0xFB03] = "FFI", [0xFB04] = "FFL", [0xFB05] = "ST", [0xFB06] = "ST",
    };

    public static string Maiusculas(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var r in s.EnumerateRunes())
        {
            if (UpperEspecial.TryGetValue(r.Value, out var esp)) sb.Append(esp);
            else sb.Append(Rune.ToUpperInvariant(r).ToString());
        }
        return sb.ToString();
    }

    // ---- int(v) do Python ----

    public static BigInteger ParaInteiro(object? v)
    {
        switch (v)
        {
            case bool b: return b ? BigInteger.One : BigInteger.Zero;
            case BigInteger bi: return bi;
            case sbyte or byte or short or ushort or int or uint or long or ulong:
                return new BigInteger(Convert.ToDecimal(v, CultureInfo.InvariantCulture));
            case double d:
                if (double.IsNaN(d)) throw new MoldeErro("cannot convert float NaN to integer");
                if (double.IsInfinity(d)) throw new MoldeErro("cannot convert float infinity to integer");
                return new BigInteger(Math.Truncate(d));
            case float f: return ParaInteiro((double)f);
            case decimal m: return new BigInteger(decimal.Truncate(m));
            case string s: return AnalisarInteiro(s);
            case Seguro sg: return AnalisarInteiro(sg.Valor);
            case null: throw new MoldeErro("int() argument must be a string, a bytes-like object or a real number, not 'NoneType'");
            default: throw new MoldeErro("int() argument must be a string, a bytes-like object or a real number, not '" + NomeTipo(v) + "'");
        }
    }

    private static BigInteger AnalisarInteiro(string original)
    {
        var s = original.Trim();
        var i = 0;
        var neg = false;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) { neg = s[i] == '-'; i++; }
        var digitos = new StringBuilder();
        var ultimoSublinhado = true; // não pode começar com "_"
        var ok = i < s.Length;
        for (; i < s.Length && ok; i++)
        {
            var c = s[i];
            if (c == '_')
            {
                if (ultimoSublinhado) ok = false;
                ultimoSublinhado = true;
            }
            else if (char.IsDigit(c))
            {
                digitos.Append(((int)char.GetNumericValue(c)).ToString(CultureInfo.InvariantCulture));
                ultimoSublinhado = false;
            }
            else ok = false;
        }
        if (!ok || ultimoSublinhado || digitos.Length == 0)
            throw new MoldeErro("invalid literal for int() with base 10: " + ReprTexto(original));
        var n = BigInteger.Parse(digitos.ToString(), CultureInfo.InvariantCulture);
        return neg ? -n : n;
    }

    /// <summary>f'{int(v):02d}' do Python.</summary>
    public static string DoisDigitos(object? v)
    {
        var n = ParaInteiro(v);
        var abs = BigInteger.Abs(n).ToString(CultureInfo.InvariantCulture);
        return n.Sign < 0 ? "-" + abs.PadLeft(1, '0') : abs.PadLeft(2, '0');
    }

    public static string NomeTipo(object? v) => v switch
    {
        null => "NoneType",
        bool => "bool",
        string or Seguro => "str",
        double or float or decimal => "float",
        BigInteger or sbyte or byte or short or ushort or int or uint or long or ulong => "int",
        IReadOnlyDictionary<string, object?> or IDictionary<string, object?> => "dict",
        IEnumerable => "list",
        _ => v.GetType().Name,
    };
}
