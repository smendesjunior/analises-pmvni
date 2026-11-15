using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

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

    /// <summary>O maior caminho de perfil que o LibreOffice aguenta: o arquivo mais fundo do perfil fica 134 caracteres abaixo da pasta dele
    /// (medido no LibreOffice 26.8) e o Windows corta em 260; com o perfil mais fundo, o <c>soffice.bin</c> aborta (0xC0000409).</summary>
    const int MaximoPerfil = 120;

    internal static bool PerfilCabe(string perfil) => perfil.Length <= MaximoPerfil;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetShortPathNameW")]
    static extern uint GetShortPathName(string longo, [Out] char[]? curto, uint tamanho);

    /// <summary>O nome curto (8.3) de uma pasta que existe, se o volume o tem; senão o caminho como veio. Só para o LibreOffice: o perfil e o
    /// TEMP continuam na pasta de trabalho, só o endereço que ele recebe é mais curto.</summary>
    internal static string CaminhoCurto(string pasta)
    {
        if (!OperatingSystem.IsWindows() || !Directory.Exists(pasta)) return pasta;
        var n = GetShortPathName(pasta, null, 0);
        if (n == 0) return pasta;
        var b = new char[n];
        var m = GetShortPathName(pasta, b, n);
        return m == 0 || m >= n ? pasta : new string(b, 0, (int)m);
    }

    /// <summary>A linha de comando do kit, com perfil (<c>-env:UserInstallation</c>) e <c>TEMP</c>/<c>TMP</c> em <paramref name="pastaConv"/>
    /// (ou em <paramref name="perfil"/> e <paramref name="temp"/>, os caminhos curtos dessas pastas).</summary>
    public static ProcessStartInfo Preparar(string soffice, string copia, string pastaConv, string? perfil = null, string? temp = null)
    {
        ArgumentNullException.ThrowIfNull(soffice);
        ArgumentNullException.ThrowIfNull(copia);
        ArgumentNullException.ThrowIfNull(pastaConv);
        // Entrada e saídas por pipes: o app é de janela (sem console). O conversor fecha a entrada e esvazia as saídas; as últimas
        // mensagens do soffice vão para o detalhe de uma falha.
        var psi = new ProcessStartInfo(soffice)
        {
            UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = pastaConv,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-env:UserInstallation=" + new Uri(perfil ?? Path.Combine(pastaConv, "perfil")).AbsoluteUri);
        foreach (var a in new[] { "--headless", "--norestore", "--convert-to", "pdf", "--outdir", pastaConv, copia }) psi.ArgumentList.Add(a);
        var tmp = temp ?? Path.Combine(pastaConv, "tmp");
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
        Directory.CreateDirectory(Path.Combine(conv, "perfil"));
        var copia = Path.Combine(conv, "copia.docx");
        File.Copy(docx, copia);
        // O perfil e o TEMP ficam na pasta de trabalho; o LibreOffice recebe o nome curto delas (uma pasta de dados funda passaria do limite).
        var perfil = CaminhoCurto(Path.Combine(conv, "perfil"));
        var temp = CaminhoCurto(Path.Combine(conv, "tmp"));
        if (!PerfilCabe(perfil))
            return new ResultadoConversao(null, "CONVERSAO_FALHOU", "a pasta de trabalho é funda demais para o LibreOffice (perfil com "
                + perfil.Length.ToString(CultureInfo.InvariantCulture) + " caracteres; o máximo é " + MaximoPerfil.ToString(CultureInfo.InvariantCulture) + ")");

        Process? p;
        try { p = Process.Start(Preparar(soffice!, copia, conv, perfil, temp)); }
        catch (Win32Exception e) { return new ResultadoConversao(null, "CONVERSAO_FALHOU", e.Message); }
        if (p is null) return new ResultadoConversao(null, "CONVERSAO_FALHOU", "o processo não abriu");
        using (p)
        {
            var mensagens = new System.Collections.Concurrent.ConcurrentQueue<string>();
            void Guardar(object _, DataReceivedEventArgs e) { if (!string.IsNullOrWhiteSpace(e.Data) && mensagens.Count < 20) mensagens.Enqueue(e.Data.Trim()); }
            p.OutputDataReceived += Guardar;
            p.ErrorDataReceived += Guardar;
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            try { p.StandardInput.Close(); }
            catch (IOException) { /* o processo já saiu */ }
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
            p.WaitForExit(2_000); // as saídas terminam de chegar (com prazo: um soffice.bin filho pode segurar os pipes)
            var ultima = mensagens.LastOrDefault(m => m.Contains("Error", StringComparison.OrdinalIgnoreCase)) ?? mensagens.LastOrDefault();
            return new ResultadoConversao(null, "CONVERSAO_FALHOU", "código " + p.ExitCode.ToString(CultureInfo.InvariantCulture)
                + (ultima is null ? "" : ": " + (ultima.Length > 200 ? ultima[..200] + "…" : ultima)));
        }
    }
}
