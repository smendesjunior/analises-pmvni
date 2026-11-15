using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace Pmvni.Revisao;

/// <summary>Resultado de uma conversão DOCX → PDF. <see cref="Motivo"/>: null (PDF gerado), <c>SEM_LIBREOFFICE</c>, <c>CONVERSAO_FALHOU</c>
/// ou <c>TEMPO_ESGOTADO</c>; <see cref="Detalhe"/> vai para a mensagem (ex.: "código 1").</summary>
public sealed record ResultadoConversao(byte[]? Pdf, string? Motivo, string? Detalhe);

/// <summary>Quem converte o DOCX em PDF. Os testes usam um conversor falso; o LibreOffice real só roda no app e no roteiro.</summary>
public interface IConversorPdf
{
    bool Disponivel { get; }

    string? Versao { get; }

    ResultadoConversao Converter(string docx, string pastaTrabalho, CancellationToken ct);
}

/// <summary>O LibreOffice, como o kit (<c>programas_pmvni.py docx-pdf</c>, desenho §10.7): <c>soffice.com --headless --convert-to pdf</c> sobre
/// uma <b>cópia</b> do DOCX, com perfil próprio e <c>TEMP</c>/<c>TMP</c> na pasta de trabalho, sem janela e com prazo. O Word nunca é usado.</summary>
public sealed class ConversorLibreOffice(string? soffice, TimeSpan? limite = null) : IConversorPdf
{
    const int TamanhoMinimoPdf = 1000;

    readonly TimeSpan _limite = limite ?? TimeSpan.FromMinutes(5);

    public bool Disponivel => soffice is not null && File.Exists(soffice);

    /// <summary><c>"LibreOffice " + ProductVersion</c> do <c>soffice.exe</c> ao lado (sem abrir processo); null se não houver.</summary>
    public string? Versao
    {
        get
        {
            if (!Disponivel) return null;
            var exe = Path.Combine(Path.GetDirectoryName(soffice!)!, "soffice.exe");
            if (!File.Exists(exe)) return null;
            var v = FileVersionInfo.GetVersionInfo(exe).ProductVersion;
            return string.IsNullOrWhiteSpace(v) ? null : "LibreOffice " + v.Trim();
        }
    }

    /// <summary><c>PMVNI_SOFFICE</c> (se o arquivo existe) → <c>%ProgramFiles%\LibreOffice*\program\soffice.com</c> → <c>soffice.exe</c> →
    /// o mesmo em <c>%ProgramFiles(x86)%</c> → <c>soffice.com</c>/<c>soffice.exe</c> no PATH; null se nada.</summary>
    public static string? Localizar()
    {
        var env = Environment.GetEnvironmentVariable("PMVNI_SOFFICE");
        if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
        foreach (var raiz in new[] { Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetEnvironmentVariable("ProgramFiles(x86)") })
        {
            if (string.IsNullOrWhiteSpace(raiz) || !Directory.Exists(raiz)) continue;
            string[] pastas;
            try { pastas = Directory.GetDirectories(raiz, "LibreOffice*").Order(StringComparer.OrdinalIgnoreCase).ToArray(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
            foreach (var nome in new[] { "soffice.com", "soffice.exe" })
                foreach (var p in pastas)
                {
                    var c = Path.Combine(p, "program", nome);
                    if (File.Exists(c)) return c;
                }
        }
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var nome in new[] { "soffice.com", "soffice.exe" })
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                try
                {
                    var c = Path.Combine(dir.Trim('"'), nome);
                    if (File.Exists(c)) return c;
                }
                catch (ArgumentException) { }
            }
        return null;
    }

    /// <summary>A linha de comando do kit, com perfil (<c>-env:UserInstallation</c>) e <c>TEMP</c>/<c>TMP</c> em <paramref name="pastaConv"/>.</summary>
    public static ProcessStartInfo Preparar(string soffice, string copia, string pastaConv)
    {
        ArgumentNullException.ThrowIfNull(soffice);
        ArgumentNullException.ThrowIfNull(copia);
        ArgumentNullException.ThrowIfNull(pastaConv);
        var psi = new ProcessStartInfo(soffice) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = pastaConv };
        psi.ArgumentList.Add("-env:UserInstallation=" + new Uri(Path.Combine(pastaConv, "perfil")).AbsoluteUri);
        foreach (var a in new[] { "--headless", "--norestore", "--convert-to", "pdf", "--outdir", pastaConv, copia }) psi.ArgumentList.Add(a);
        var tmp = Path.Combine(pastaConv, "tmp");
        psi.Environment["TEMP"] = tmp;
        psi.Environment["TMP"] = tmp;
        return psi;
    }

    public ResultadoConversao Converter(string docx, string pastaTrabalho, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docx);
        ArgumentNullException.ThrowIfNull(pastaTrabalho);
        if (!Disponivel) return new ResultadoConversao(null, "SEM_LIBREOFFICE", null);
        var conv = Path.Combine(pastaTrabalho, "conv");
        if (Directory.Exists(conv)) Directory.Delete(conv, true);
        Directory.CreateDirectory(Path.Combine(conv, "tmp"));
        var copia = Path.Combine(conv, "copia.docx");
        File.Copy(docx, copia);

        Process? p;
        try { p = Process.Start(Preparar(soffice!, copia, conv)); }
        catch (Win32Exception e) { return new ResultadoConversao(null, "CONVERSAO_FALHOU", e.Message); }
        if (p is null) return new ResultadoConversao(null, "CONVERSAO_FALHOU", "o processo não abriu");
        using (p)
        {
            var relogio = Stopwatch.StartNew();
            while (!p.WaitForExit(250))
            {
                if (ct.IsCancellationRequested || relogio.Elapsed > _limite)
                {
                    try { p.Kill(entireProcessTree: true); }
                    catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
                    p.WaitForExit(10_000);
                    ct.ThrowIfCancellationRequested();
                    return new ResultadoConversao(null, "TEMPO_ESGOTADO", "tempo esgotado");
                }
            }
            var pdf = Path.Combine(conv, "copia.pdf");
            var bytes = File.Exists(pdf) ? File.ReadAllBytes(pdf) : Array.Empty<byte>();
            if (bytes.Length >= TamanhoMinimoPdf && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                return new ResultadoConversao(bytes, null, null);
            return new ResultadoConversao(null, "CONVERSAO_FALHOU", "código " + p.ExitCode.ToString(CultureInfo.InvariantCulture));
        }
    }
}
