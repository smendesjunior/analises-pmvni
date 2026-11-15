using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>Uma linha do histórico permanente (<c>historico.jsonl</c>): um comando aplicado, desfeito ou refeito.
/// <see cref="Ref"/> aponta para o <see cref="N"/> do registro a que este se refere (ex.: um "desfeito").</summary>
public sealed record RegistroHistorico(
    int N,
    DateTimeOffset DataUtc,
    string Usuario,
    string Tipo,
    string? Item,
    string? Campo,
    JsonNode? Antes,
    JsonNode? Depois,
    int? Ref);

/// <summary><c>historico.jsonl</c>: um objeto JSON por linha, terminada em <c>\n</c>, com as chaves
/// <c>n, data_utc, usuario, tipo, alvo{item,campo}, antes, depois, ref</c> sempre presentes.</summary>
public static class HistoricoJsonl
{
    public static string Linha(RegistroHistorico r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var o = new JsonObject
        {
            ["n"] = r.N,
            ["data_utc"] = Manifesto.TextoUtc(r.DataUtc),
            ["usuario"] = r.Usuario,
            ["tipo"] = r.Tipo,
            ["alvo"] = new JsonObject { ["item"] = r.Item, ["campo"] = r.Campo },
            ["antes"] = r.Antes?.DeepClone(),
            ["depois"] = r.Depois?.DeepClone(),
            ["ref"] = r.Ref,
        };
        return Encoding.UTF8.GetString(JsonFiel.Escrever(o));
    }

    public static byte[] Escrever(IEnumerable<RegistroHistorico> registros)
    {
        ArgumentNullException.ThrowIfNull(registros);
        var sb = new StringBuilder();
        foreach (var r in registros) sb.Append(Linha(r)).Append('\n');
        return new UTF8Encoding(false).GetBytes(sb.ToString());
    }

    /// <summary>Lê o jsonl; linhas em branco são ignoradas. <see cref="JsonException"/> se uma linha for inválida.</summary>
    public static IReadOnlyList<RegistroHistorico> Ler(byte[] utf8)
    {
        ArgumentNullException.ThrowIfNull(utf8);
        string texto;
        try { texto = new UTF8Encoding(false, true).GetString(utf8); }
        catch (DecoderFallbackException e) { throw new JsonException("historico.jsonl não é UTF-8 válido.", e); }
        if (texto.Length > 0 && texto[0] == '﻿') texto = texto[1..];

        var lista = new List<RegistroHistorico>();
        foreach (var linha in texto.Split('\n'))
        {
            if (linha.Trim().Length == 0) continue;
            lista.Add(Registro(JsonFiel.Ler(Encoding.UTF8.GetBytes(linha))));
        }
        return lista;
    }

    private static RegistroHistorico Registro(JsonObject j)
    {
        try
        {
            var alvo = j["alvo"] as JsonObject;
            return new RegistroHistorico(
                j["n"]!.GetValue<int>(),
                Manifesto.DataUtc(j["data_utc"]!.GetValue<string>()),
                j["usuario"]!.GetValue<string>(),
                j["tipo"]!.GetValue<string>(),
                alvo?["item"]?.GetValue<string>(),
                alvo?["campo"]?.GetValue<string>(),
                j["antes"]?.DeepClone(),
                j["depois"]?.DeepClone(),
                j["ref"]?.GetValue<int>());
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException or FormatException)
        {
            throw new JsonException("Linha de histórico inválida: " + e.Message, e);
        }
    }
}
