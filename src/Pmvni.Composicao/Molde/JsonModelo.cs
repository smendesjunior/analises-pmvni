using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Pmvni.Composicao;

/// <summary>Lê JSON para o modelo de objetos do molde (equivale ao <c>json.loads</c> do Python):
/// Dictionary&lt;string, object?&gt; (ordem das chaves preservada; chave repetida: o último valor vence na posição da primeira),
/// List&lt;object?&gt;, string, bool, null, long (inteiros; BigInteger se não couberem) e double (tokens com '.', 'e' ou 'E').</summary>
public static class JsonModelo
{
    public static object? Ler(string json)
    {
        if (json.Length > 0 && json[0] == '﻿') json = json[1..]; // utf-8-sig
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1000 });
        return Converter(doc.RootElement);
    }

    private static object? Converter(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var d = new Dictionary<string, object?>();
                foreach (var p in e.EnumerateObject()) d[p.Name] = Converter(p.Value);
                return d;
            case JsonValueKind.Array:
                var l = new List<object?>();
                foreach (var x in e.EnumerateArray()) l.Add(Converter(x));
                return l;
            case JsonValueKind.String: return e.GetString();
            case JsonValueKind.True: return true;
            case JsonValueKind.False: return false;
            case JsonValueKind.Null: return null;
            default:
                var bruto = e.GetRawText();
                if (bruto.AsSpan().IndexOfAny(".eE") >= 0)
                    return double.Parse(bruto, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (long.TryParse(bruto, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n)) return n;
                return BigInteger.Parse(bruto, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        }
    }
}
