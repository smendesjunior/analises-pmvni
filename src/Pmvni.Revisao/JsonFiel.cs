using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>JSON ↔ <see cref="JsonNode"/> com a semântica do <c>json.loads</c> do kit: UTF-8 estrito (aceita BOM), o que
/// <c>JsonModelo.Ler</c> aceita (sem comentários nem vírgula final), chave repetida (a última vence, na posição da
/// primeira) e números com o texto cru (<c>12.0</c>, <c>1e-05</c> e inteiros enormes voltam como vieram).</summary>
public static class JsonFiel
{
    private static readonly UTF8Encoding Utf8Estrito = new(false, true);

    private static readonly JsonWriterOptions OpcoesEscrita = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        MaxDepth = 1000,
    };

    /// <summary>Lê o JSON. A raiz precisa ser um objeto. <see cref="JsonException"/> se o texto não for UTF-8 válido ou JSON válido.</summary>
    public static JsonObject Ler(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        string texto;
        try { texto = Utf8Estrito.GetString(utf8); }
        catch (DecoderFallbackException e) { throw new JsonException("O arquivo não é UTF-8 válido.", e); }
        if (texto.Length > 0 && texto[0] == '﻿') texto = texto[1..]; // utf-8-sig

        using var doc = JsonDocument.Parse(texto, new JsonDocumentOptions { MaxDepth = 1000 });
        if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("A raiz do JSON precisa ser um objeto.");
        return (JsonObject)Converter(doc.RootElement)!;
    }

    /// <summary>UTF-8 sem BOM, compacto, sem escapar o que o JSON não exige (acentos, &lt; &gt; &amp; e emoji ficam como estão).</summary>
    public static byte[] Escrever(JsonNode no)
    {
        ArgumentNullException.ThrowIfNull(no);
        using var memoria = new MemoryStream();
        using (var w = new Utf8JsonWriter(memoria, OpcoesEscrita)) no.WriteTo(w);
        return memoria.ToArray();
    }

    private static JsonNode? Converter(JsonElement e)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                var o = new JsonObject();
                foreach (var p in e.EnumerateObject()) o[p.Name] = Converter(p.Value); // repetida: troca o valor, mantém a posição
                return o;
            case JsonValueKind.Array:
                var a = new JsonArray();
                foreach (var x in e.EnumerateArray()) a.Add(Converter(x));
                return a;
            case JsonValueKind.String: return JsonValue.Create(e.GetString());
            case JsonValueKind.True: return JsonValue.Create(true);
            case JsonValueKind.False: return JsonValue.Create(false);
            case JsonValueKind.Null: return null;
            default: return JsonValue.Create(e.Clone()); // número: o JsonElement guarda o texto cru e o devolve igual
        }
    }
}
