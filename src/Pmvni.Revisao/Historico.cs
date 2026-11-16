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

/// <summary>O texto, em português, de um registro do histórico (para a lista de Histórico da tela).</summary>
public static class DescricaoHistorico
{
    public static string Texto(RegistroHistorico r, Projeto p)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(p);
        if ((r.Tipo == "desfeito" || r.Tipo == "refeito") && r.Depois is JsonObject o && o["tipo"] is JsonValue t && t.TryGetValue<string>(out var tipo))
        {
            var original = r with { Tipo = tipo, Antes = Valor(r.Antes), Depois = Valor(r.Depois) };
            return (r.Tipo == "desfeito" ? "Desfeito: " : "Refeito: ") + Texto(original, p);
        }

        if (r.Tipo == nameof(Lote)) return TextoDoLote(r, p);
        if (TabelasRevisao.EhLinha(r.Item)) return TextoDaLinha(r);
        var item = Rotulo(r.Item, p);
        (string Nome, bool Feminino) campo = r.Campo is null ? ("campo", false) : NomeDoCampo(r.Campo);
        switch (r.Tipo)
        {
            case nameof(DefinirSituacao):
                return "Situação do " + item + ": " + ConferenciaRevisao.TextoSituacao(r.Antes?.GetValue<string>()) + " → "
                       + ConferenciaRevisao.TextoSituacao(r.Depois?.GetValue<string>());
            case nameof(DefinirJustificativa):
                var antes = r.Antes is JsonValue a && a.TryGetValue<string>(out var sa) && sa.Length > 0;
                var depois = r.Depois is JsonValue d && d.TryGetValue<string>(out var sd) && sd.Length > 0;
                return "Justificativa do " + item + (!depois ? " removida" : antes ? " alterada" : " definida");
            case nameof(DefinirCampo):
                return campo.Nome + " do " + item + (campo.Feminino ? " editada" : " editado");
            case nameof(RestaurarCampo):
                return campo.Nome + " do " + item + (campo.Feminino ? " restaurada" : " restaurado") + " ao original";
            case nameof(CriarItem):
                return p.Item(r.Item ?? "") is null ? "Criado um item novo" : "Criado o " + item + " (item novo)";
            case nameof(Excluir):
                return "Excluído o " + item;
            case nameof(Restaurar):
                return "Restaurado o " + item;
            case nameof(RestaurarItemOriginal):
                return item + " restaurado ao original";
            case Importador.TipoHistorico:
                var n = r.Depois is JsonArray lista ? lista.Count : 0;
                return "Importação do dados.json: " + n + (n == 1 ? " citação convertida" : " citações convertidas");
            default:
                return MotorFiguras.Descrever(r.Tipo, r.Item, r.Campo, p) ?? r.Tipo;
        }
    }

    /// <summary>Um lote: <c>ITEM 02 · Ajustado pela IA (4 alterações)</c>, <c>Índice 05 · Recalculado: … (3 alterações)</c>.</summary>
    private static string TextoDoLote(RegistroHistorico r, Projeto p)
    {
        var (descricao, passos) = Motor.ResumoDoLote(r.Depois);
        string dono;
        if (TabelasRevisao.EhLinha(r.Item))
        {
            dono = TabelasRevisao.Rotulo(r.Item!);
            dono = dono[..1] + dono[1..].ToLowerInvariant();
        }
        else dono = Rotulo(r.Item, p);
        return dono + " · " + (descricao.Length > 0 ? descricao : "Várias alterações") + " (" + passos + (passos == 1 ? " alteração)" : " alterações)");
    }

    /// <summary>Registro sobre uma linha de tabela: <c>Índice 02 · Situação: FALTA COMPROVAR → ATENDE</c>, <c>Ambiente 03 · Vão 1 — tipo: texto editado</c>.</summary>
    private static string TextoDaLinha(RegistroHistorico r)
    {
        var rotulo = TabelasRevisao.Rotulo(r.Item!);
        rotulo = rotulo[..1] + rotulo[1..].ToLowerInvariant() + " · ";
        var campo = r.Campo is null ? "" : TabelasRevisao.NomeDoCampo(r.Campo);
        switch (r.Tipo)
        {
            case nameof(DefinirSituacao):
                return rotulo + "Situação: " + ConferenciaRevisao.TextoSituacao(r.Antes?.GetValue<string>()) + " → "
                       + ConferenciaRevisao.TextoSituacao(r.Depois?.GetValue<string>());
            case nameof(DefinirJustificativa):
                var antes = r.Antes is JsonValue a && a.TryGetValue<string>(out var sa) && sa.Length > 0;
                var depois = r.Depois is JsonValue d && d.TryGetValue<string>(out var sd) && sd.Length > 0;
                return rotulo + "Justificativa " + (!depois ? "removida" : antes ? "alterada" : "definida");
            case nameof(DefinirCampo):
                return rotulo + campo + ": texto editado";
            case nameof(RestaurarCampo):
                return rotulo + campo + ": texto restaurado ao original";
            case nameof(RestaurarItemOriginal):
                return rotulo + "linha restaurada ao original";
            case MotorLinhas.TipoAcrescimo:
                var modelo = (r.Depois ?? r.Antes)?["modelo"] as JsonObject;
                string T(string chave) => modelo?[chave] is JsonValue mv && mv.TryGetValue<string>(out var ms) ? ms : "";
                var acrescentou = r.Depois is not null;
                return r.Campo is null
                    ? rotulo + "chamada " + (acrescentou ? "acrescentada" : "removida") + " (" + T("titulo") + ")"
                    : rotulo + "esquadria " + (acrescentou ? "acrescentada" : "removida") + " (" + T("codigo") + " • " + T("medidas") + ")";
            default:
                return rotulo + r.Tipo;
        }
    }

    private static JsonNode? Valor(JsonNode? embrulho) => embrulho is JsonObject o ? o["valor"]?.DeepClone() : null;

    private static string Rotulo(string? id, Projeto p)
    {
        if (id is null) return "item";
        var n = p.NumeroAtual(id) ?? p.Item(id)?.NumeroOriginal;
        return n is null ? "item " + id : "ITEM " + n.Value.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (string Nome, bool Feminino) NomeDoCampo(string campo) => campo switch
    {
        "titulo" => ("Título", false),
        "verificacao" => ("Verificação técnica", true),
        "providencia" => ("Providência", true),
        "base_legal" => ("Base legal", true),
        "referencia" => ("Referência", true),
        _ => (campo, false),
    };
}
