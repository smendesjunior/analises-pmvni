using System.Text.Json;

namespace Pmvni.Agentes;

public sealed record AvisosDaEntrega(IReadOnlyList<string> Acoes, string Registro)
{
    public string Resumo => string.Join(" ", Acoes.Take(2))
        + (Acoes.Count > 2 ? " Consulte os demais avisos no registro." : "");

    static string UmaLinha(string texto, int limite)
    {
        var linha = string.Join(" ", texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return linha.Length <= limite ? linha : linha[..limite] + "…";
    }

    public static AvisosDaEntrega Ler(string controle, DateTime desde, string? pastaDoHistorico = null)
    {
        var fonte = Path.Combine(controle, "validadores", "avisos.json");
        var json = ArquivoJsonDoKit.Ler(fonte, desde);
        if (json is not { ValueKind: JsonValueKind.Array } avisos || avisos.GetArrayLength() == 0) return new([], fonte);
        var grupos = new HashSet<(string Regra, string Item)>();
        var acoes = new List<string>();
        foreach (var aviso in avisos.EnumerateArray())
        {
            if (aviso.ValueKind != JsonValueKind.Object ||
                ProgressoLocal.Texto(aviso, "regra") is not { Length: > 0 } regra || regra == "codigo_interno") continue;
            var onde = ProgressoLocal.Texto(aviso, "onde") ?? "";
            var local = onde;
            if (onde.StartsWith("itens[", StringComparison.Ordinal) && onde.IndexOf(']') is var fim && fim > 6
                && int.TryParse(onde.AsSpan(6, fim - 6), out var indice) && indice >= 0)
                local = $"item {(long)indice + 1}";
            if (!grupos.Add((regra, local))) continue;
            var acao = regra switch
            {
                "palavra_repetida" => "Confira a palavra repetida",
                "espaco_antes_do_numero" => "Confira o espaço antes do número",
                _ => "Confira " + UmaLinha(regra.Replace('_', ' '), 45)
            };
            var trecho = UmaLinha(ProgressoLocal.Texto(aviso, "texto") ?? "", 45);
            acoes.Add(acao + (local.Length > 0 ? $" ({UmaLinha(local, 35)})" : "")
                + (trecho.Length > 0 ? $" — “{trecho}”." : "."));
        }
        if (pastaDoHistorico is null) return new(acoes, fonte);
        // Uma retomada pode produzir outros avisos. Cada versão do registro continua disponível.
        var bytes = System.Text.Encoding.UTF8.GetBytes(avisos.GetRawText());
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        var registro = Path.Combine(pastaDoHistorico, $"AVISOS-{hash}.txt");
        var temporario = registro + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!File.Exists(registro))
            {
                File.WriteAllBytes(temporario, bytes);
                File.Move(temporario, registro, true);
            }
            return new(acoes, registro);
        }
        catch (Exception erro) when (erro is IOException or UnauthorizedAccessException)
        {
            return new(acoes, fonte);
        }
        finally { try { File.Delete(temporario); } catch (Exception erro) when (erro is IOException or UnauthorizedAccessException) { } }
    }
}
