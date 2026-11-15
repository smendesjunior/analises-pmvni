using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>A IA no Novo despacho (correções 2026-11-15): resume as exigências vindas do relatório (só o que não atende) e aplica instruções de
/// modificação escritas na caixa acima da prévia. Usa o Claude Code (<c>claude -p</c>) ou, sem ele, o Codex (<c>codex exec</c>), com a
/// conta já logada no programa; nada é gravado: a resposta só troca as exigências do rascunho.</summary>
internal static class IaDespacho
{
    /// <summary>As regras de redação do plugin despacho do kit (SKILL.md), resumidas.</summary>
    internal const string Regras =
        "Você redige exigências de despacho de correção da Secretaria Municipal de Obras de Venda Nova do Imigrante/ES. " +
        "Texto curto, técnico e neutro, em português do Brasil. Só entram itens NÃO ATENDE, FALTA COMPROVAR e INFORMAÇÕES DIVERGENTES; " +
        "itens que atendem nunca aparecem. Cada exigência diz o problema, o local, a correção e o fundamento confirmado, nessa ordem " +
        "(primeiro o problema, depois o que fazer), sem repetir o texto técnico inteiro do relatório, sem hipóteses nem duplicações. " +
        "Não invente fundamento: mantenha o que veio ou deixe vazio.";

    internal const string PedidoResumo =
        "Resuma cada exigência de forma objetiva para o despacho, mantendo a ordem e os fundamentos; junte as duplicadas.";

    /// <summary>Monta o pedido: regras, rascunho atual em JSON e a instrução; pede só JSON de volta.</summary>
    internal static string Prompt(RascunhoDespacho r, string instrucao)
    {
        var atual = new JsonObject
        {
            ["protocolo"] = r.Protocolo,
            ["requerente"] = r.Requerente,
            ["assunto"] = r.Assunto,
            ["exigencias"] = new JsonArray(r.Pendencias.Where(p => p.Incluida)
                .Select(p => (JsonNode)new JsonObject { ["grupo"] = p.Grupo, ["texto"] = p.Texto, ["fundamento"] = p.Fundamento }).ToArray()),
        };
        return Regras + "\n\nDespacho atual (JSON):\n" + atual.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
               "\n\nInstrução: " + instrucao +
               "\n\nResponda SOMENTE com um JSON no mesmo formato (protocolo, requerente, assunto, exigencias[{grupo, texto, fundamento}]), sem comentários.";
    }

    internal sealed record Resposta(string? Protocolo, string? Requerente, string? Assunto, IReadOnlyList<(string Grupo, string Texto, string Fundamento)> Exigencias);

    /// <summary>Lê o primeiro objeto JSON da resposta (a IA às vezes cerca com ```json).</summary>
    internal static Resposta? Ler(string saida)
    {
        var ini = saida.IndexOf('{');
        var fim = saida.LastIndexOf('}');
        if (ini < 0 || fim <= ini) return null;
        try
        {
            if (JsonNode.Parse(saida[ini..(fim + 1)]) is not JsonObject o) return null;
            static string? T(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
            var lista = (o["exigencias"] as JsonArray ?? [])
                .OfType<JsonObject>()
                .Select(e => (T(e["grupo"]) ?? "", T(e["texto"]) ?? "", T(e["fundamento"]) ?? ""))
                .Where(e => e.Item2.Trim().Length > 0)
                .ToList();
            return new Resposta(T(o["protocolo"]), T(o["requerente"]), T(o["assunto"]), lista);
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Roda o programa da IA com o pedido no stdin e devolve a saída. Sem programa: <see cref="InvalidOperationException"/>.</summary>
    internal static async Task<string> PerguntarAsync(string? claude, string? codex, string prompt, CancellationToken ct)
    {
        ProcessStartInfo si;
        if (!string.IsNullOrEmpty(claude)) si = new ProcessStartInfo(claude, "-p --output-format text");
        else if (!string.IsNullOrEmpty(codex)) si = new ProcessStartInfo(codex, "exec --skip-git-repo-check -");
        else throw new InvalidOperationException("Nenhum programa de IA encontrado. Instale e entre no Claude Code ou no Codex em Ajustes › Inteligência artificial.");
        si.RedirectStandardInput = si.RedirectStandardOutput = si.RedirectStandardError = true;
        si.UseShellExecute = false;
        si.CreateNoWindow = true;
        si.StandardInputEncoding = si.StandardOutputEncoding = si.StandardErrorEncoding = new UTF8Encoding(false);
        si.WorkingDirectory = Path.GetTempPath();
        using var p = Process.Start(si) ?? throw new InvalidOperationException("O programa da IA não abriu.");
        await p.StandardInput.WriteAsync(prompt.AsMemory(), ct);
        p.StandardInput.Close();
        var saida = p.StandardOutput.ReadToEndAsync(ct);
        var erro = p.StandardError.ReadToEndAsync(ct);
        try { await p.WaitForExitAsync(ct); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch (InvalidOperationException) { } throw; }
        if (p.ExitCode != 0) throw new InvalidOperationException("A IA respondeu com erro: " + (await erro).Trim());
        return await saida;
    }
}
