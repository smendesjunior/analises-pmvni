using System.Text.Encodings.Web;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Tira a chave de API de tudo o que o editor grava ou mostra (desenho §12.9). O Codex tem uma ferramenta de shell e a chave está no ambiente dele: um
/// <c>echo</c> dele, ou uma mensagem de erro, poria a chave em texto puro no <c>saida.jsonl</c>, no <c>erros.txt</c> e na tela, dentro de uma pasta sincronizada
/// pelo OneDrive. Cada ocorrência (crua ou escapada como JSON) vira <c>***</c>.</summary>
internal sealed class RedatorChave
{
    public const string Substituto = "***";
    const int PrefixoMinimo = 6;

    static readonly JsonSerializerOptions Relaxado = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly string[] formas;

    public RedatorChave(string? chave)
    {
        var lista = new List<string>();
        if (!string.IsNullOrWhiteSpace(chave))
        {
            foreach (var f in new[] { chave, SemAspas(JsonSerializer.Serialize(chave)), SemAspas(JsonSerializer.Serialize(chave, Relaxado)) })
                if (f.Length > 0 && !lista.Contains(f, StringComparer.Ordinal)) lista.Add(f);
        }
        formas = lista.OrderByDescending(f => f.Length).ToArray();
    }

    static string SemAspas(string json) => json[1..^1];

    public bool Ativo => formas.Length > 0;

    public string Aplicar(string texto)
    {
        if (formas.Length == 0 || texto.Length == 0) return texto;
        foreach (var f in formas)
            texto = texto.Replace(f, Substituto, StringComparison.Ordinal);

        // Uma linha cortada no limite pode terminar no meio da chave: tira esse começo (a marca de corte é preservada).
        var marca = ExecucaoProcesso.MarcaLinhaTruncada;
        var cortada = texto.EndsWith(marca, StringComparison.Ordinal);
        if (!cortada) return texto;
        var corpo = texto[..^marca.Length];
        foreach (var f in formas)
            for (var tamanho = Math.Min(f.Length - 1, corpo.Length); tamanho >= PrefixoMinimo; tamanho--)
                if (corpo.EndsWith(f[..tamanho], StringComparison.Ordinal))
                {
                    corpo = corpo[..^tamanho] + Substituto;
                    break;
                }
        return corpo + marca;
    }
}
