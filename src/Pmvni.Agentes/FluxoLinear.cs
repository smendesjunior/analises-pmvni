using System.Globalization;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>A assinatura do fluxo linear do kit (2026-11-22: análise em três frentes, auditoria cruzada, relatório e revisão final locais). O Editor grava
/// <see cref="Assinatura"/> em <c>execucao.json</c> (<c>"fluxo"</c>) de cada análise iniciada com o kit linear (<c>etapas.json</c> versão 2 ou maior).
/// O histórico de durações da previsão só aceita amostras de controles com essa marca, gravadas depois do primeiro início marcado; as do ciclo antigo
/// (análise de ~60 min, auditorias por IA) ficam de fora, e valem as durações iniciais novas.</summary>
public static class FluxoLinear
{
    public const string Assinatura = "linear-1";
    public const string Chave = "fluxo";

    /// <summary>O <c>etapas.json</c> do kit instalado descreve o fluxo linear (<c>versao</c> ≥ 2).</summary>
    public static bool DoKit(string? etapasJson) =>
        !string.IsNullOrWhiteSpace(etapasJson) && ArquivoJsonDoKit.Ler(etapasJson) is { ValueKind: JsonValueKind.Object } raiz
        && raiz.TryGetProperty("versao", out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) && n >= 2;

    /// <summary>O primeiro início, por provedor, das análises marcadas com a assinatura em <c>&lt;.pmvni-ai&gt;\editor\*\execucao.json</c>.</summary>
    public static IReadOnlyDictionary<Provedor, DateTimeOffset> IniciosMarcados(string pastaPmvniAi)
    {
        var saida = new Dictionary<Provedor, DateTimeOffset>();
        var editor = Path.Combine(pastaPmvniAi, "editor");
        IEnumerable<string> registros;
        try { registros = Directory.Exists(editor) ? Directory.EnumerateDirectories(editor).ToList() : []; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return saida; }
        foreach (var registro in registros)
        {
            if (ArquivoJsonDoKit.Ler(Path.Combine(registro, "execucao.json")) is not { ValueKind: JsonValueKind.Object } o
                || ProgressoLocal.Texto(o, Chave) != Assinatura || ProgressoLocal.Texto(o, "provedor") is not { } sufixo
                || !DateTimeOffset.TryParse(ProgressoLocal.Texto(o, "inicio"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var inicio))
                continue;
            Provedor? p = sufixo switch { "chatgpt" => Provedor.ChatGpt, "claude" => Provedor.Claude, "gemini" => Provedor.Gemini, _ => null };
            if (p is { } provedor && (!saida.TryGetValue(provedor, out var antes) || inicio < antes)) saida[provedor] = inicio;
        }
        return saida;
    }
}
