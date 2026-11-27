using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

internal sealed record JanelaDaConta(int Minutos, double Usada, long? Reinicia);
internal sealed record AmostraDaConta(DateTimeOffset Hora, string? Conta, string? Plano, JanelaDaConta[] Janelas)
{
    internal static AmostraDaConta Ler(JsonObject resposta)
    {
        var limite = resposta["rateLimitsByLimitId"]?["codex"] ?? resposta["rateLimits"];
        if (limite is not JsonObject || limite["limitId"]?.ToString() is { } id && id != "codex")
            throw new InvalidDataException("A conta não retornou o limite do Codex.");
        var janelas = new List<JanelaDaConta>();
        foreach (var nome in new[] { "primary", "secondary" })
        {
            if (limite[nome] is not JsonObject janela ||
                janela["windowDurationMins"] is not JsonValue duracao || !duracao.TryGetValue<int>(out var minutos) || minutos <= 0 ||
                janela["usedPercent"] is not JsonValue pct || !pct.TryGetValue<double>(out var usada) || usada is < 0 or > 100) continue;
            janelas.Add(new(minutos, usada, (long?)janela["resetsAt"]));
        }
        var conta = resposta["accountId"]?.ToString();
        return new(DateTimeOffset.UtcNow, string.IsNullOrEmpty(conta) ? null :
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(conta))), limite["planType"]?.ToString(), janelas.ToArray());
    }

    internal static async Task<AmostraDaConta> ConsultarAsync(string exe, string pasta, Action<IDictionary<string,string?>> ambiente, CancellationToken ct, string rota = "openai")
    {
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        prazo.CancelAfter(TimeSpan.FromSeconds(6));
        var inicio = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = pasta,
            RedirectStandardInput = true, RedirectStandardOutput = true, StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8 };
        foreach (var arg in RotaCodex.ArgumentosConta(rota)) inicio.ArgumentList.Add(arg);
        inicio.ArgumentList.Add("app-server");
        ambiente(inicio.Environment);
        using var servidor = ProcessoInterno.Iniciar(inicio);
        using var cancelar = prazo.Token.Register(() => {
            try { if (!servidor.Processo.HasExited) servidor.Processo.Kill(entireProcessTree: true); }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
        async Task<JsonObject> Pedido(int id, string metodo, object? dados)
        {
            await servidor.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method = metodo, @params = dados })).ConfigureAwait(false);
            while (await servidor.StandardOutput.ReadLineAsync(prazo.Token).ConfigureAwait(false) is { } linha)
            {
                if (JsonNode.Parse(linha) is not JsonObject r || r["id"]?.ToString() != id.ToString()) continue;
                return r["result"] as JsonObject ?? throw new InvalidDataException("Consulta de cota recusada pelo Codex.");
            }
            throw new EndOfStreamException("O Codex encerrou a consulta de cota.");
        }
        await Pedido(1, "initialize", new { clientInfo = new { name = "pmvni_editor", version = "1.0" } }).ConfigureAwait(false);
        await servidor.StandardInput.WriteLineAsync("""{"jsonrpc":"2.0","method":"initialized"}""").ConfigureAwait(false);
        return Ler(await Pedido(2, "account/rateLimits/read", new { }).ConfigureAwait(false));
    }
}
