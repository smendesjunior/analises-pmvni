using System.Globalization;
using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>O relatório que o kit declara no <c>delivery.json</c>, ou o motivo de não haver.</summary>
public sealed record Resultado(string? Documento, string? Pdf, string? Motivo);

/// <summary>Localiza o resultado (desenho §12.6): o <c>document</c> do <c>delivery.json</c> escrito pelo kit, nunca uma busca por nome de arquivo.
/// O editor não julga o mérito: só confere que o arquivo é novo, legível e está dentro da pasta do processo.</summary>
public static class ResultadoAnalise
{
    static readonly TimeSpan Tolerancia = TimeSpan.FromSeconds(2);

    /// <summary>A pasta de controle do kit dentro do processo: <c>.pmvni-ai\analise</c> (ChatGPT) ou <c>.pmvni-ai\analise-pro</c> (Claude).</summary>
    public static string PastaControle(Provedor p) => p == Provedor.ChatGpt ? @".pmvni-ai\analise" : @".pmvni-ai\analise-pro";

    public static Resultado Localizar(string pastaProcesso, Provedor p, DateTime inicioUtc, string pastaRegistro)
    {
        var delivery = Path.Combine(pastaProcesso, PastaControle(p), "delivery.json");
        FileInfo info;
        try
        {
            info = new FileInfo(delivery);
            if (!info.Exists || info.LastWriteTimeUtc < inicioUtc - Tolerancia)
                return Falha($"A análise terminou, mas não deixou relatório novo (delivery.json não foi atualizado). Veja o registro: {pastaRegistro}.");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Falha($"A análise terminou, mas não deixou relatório novo (delivery.json não foi atualizado). Veja o registro: {pastaRegistro}.");
        }

        string documento;
        string? pdf = null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(delivery));
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) throw new JsonException("o conteúdo não é um objeto JSON");
            if (!raiz.TryGetProperty("document", out var d) || d.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(d.GetString()))
                throw new JsonException("não há o campo document");
            documento = d.GetString()!;
            if (raiz.TryGetProperty("pdf", out var pf) && pf.ValueKind == JsonValueKind.String) pdf = pf.GetString();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            var motivo = e.Message.TrimEnd('.', ' ', '\r', '\n');
            var primeira = motivo.Split('\n')[0].TrimEnd('\r');
            return Falha($"O delivery.json da análise não pôde ser lido: {primeira}.");
        }

        var foraDaPasta = $"O relatório indicado pela análise está fora da pasta do processo: {documento}.";
        string completo;
        try { completo = Path.GetFullPath(Path.IsPathRooted(documento) ? documento : Path.Combine(pastaProcesso, documento)); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return Falha(foraDaPasta); }

        var raizProcesso = Path.GetFullPath(pastaProcesso).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!completo.StartsWith(raizProcesso + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || TemLink(raizProcesso, completo))
            return Falha(foraDaPasta);
        if (!File.Exists(completo))
            return Falha($"O relatório indicado pela análise não foi encontrado: {documento}.");

        return new Resultado(completo, PdfDentro(pastaProcesso, pdf), null);
    }

    /// <summary>O PDF que o kit declara, com a mesma regra do documento: relativo vale em relação à pasta do processo; fora dela, link ou junção no caminho, ou
    /// arquivo ausente ⇒ null (o editor só mostra um caminho que ele mesmo abriria).</summary>
    public static string? PdfDentro(string pastaProcesso, string? pdf)
    {
        if (string.IsNullOrWhiteSpace(pdf)) return null;
        try
        {
            var c = Path.GetFullPath(Path.IsPathRooted(pdf) ? pdf : Path.Combine(pastaProcesso, pdf));
            var raizProcesso = Path.GetFullPath(pastaProcesso).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!c.StartsWith(raizProcesso + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || TemLink(raizProcesso, c)) return null;
            return File.Exists(c) ? c : null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    static Resultado Falha(string motivo) => new(null, null, motivo);

    /// <summary>Link simbólico ou junção em qualquer parte do caminho (da pasta do processo até o arquivo, inclusive): o editor não segue links.</summary>
    static bool TemLink(string raizProcesso, string completo)
    {
        try
        {
            var atual = new FileInfo(completo).Exists ? (FileSystemInfo)new FileInfo(completo) : new DirectoryInfo(completo);
            while (atual is not null && atual.FullName.Length > raizProcesso.Length)
            {
                if (atual.Exists && atual.LinkTarget is not null) return true;
                atual = atual is FileInfo f ? f.Directory : ((DirectoryInfo)atual).Parent;
            }
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return true; }
    }
}
