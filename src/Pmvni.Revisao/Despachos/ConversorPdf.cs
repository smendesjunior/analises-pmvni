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
/// uma <b>cópia</b> do DOCX, com <c>TEMP</c>/<c>TMP</c> na pasta de trabalho, sem janela e com prazo. O Word nunca é usado. Perfil próprio do
/// editor (Ruling T16-C4): <c>raizPerfil</c> (o app usa <c>&lt;dados&gt;\lo</c>), um só e persistente, criado na primeira conversão (um perfil
/// novo leva ~4 s a mais); sem ele, um perfil novo na pasta de trabalho a cada conversão. As impressoras do Windows nunca são consultadas
/// (<see cref="SemImpressoras"/>).</summary>
public sealed class ConversorLibreOffice(string? soffice, TimeSpan? limite = null, string? raizPerfil = null) : IConversorPdf
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

    /// <summary>O arquivo existe e é o <c>soffice.com</c> ou o <c>soffice.exe</c> do LibreOffice (pelo nome; nada é executado).</summary>
    public static bool ProgramaValido(string? caminho) =>
        !string.IsNullOrWhiteSpace(caminho) && File.Exists(caminho)
        && Path.GetFileName(caminho) is var n && (n.Equals("soffice.com", StringComparison.OrdinalIgnoreCase) || n.Equals("soffice.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>O programa escolhido em Ajustes (o <c>soffice.com</c> ao lado, se houver: o de console, sem janela) quando ele existe e é o
    /// soffice; senão (ou com null, "Automático"), <see cref="Localizar()"/>.</summary>
    public static string? Localizar(string? escolhido)
    {
        if (!ProgramaValido(escolhido)) return Localizar();
        var com = Path.Combine(Path.GetDirectoryName(escolhido!)!, "soffice.com");
        return File.Exists(com) ? com : escolhido;
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
        foreach (var v in SemImpressoras) psi.Environment[v] = "1";
        return psi;
    }

    /// <summary>O LibreOffice nunca consulta as impressoras do Windows (2026-11-17). Sem isto, o soffice pede ao spooler a impressora padrão
    /// e a lista de filas (vcl/source/gdi/print.cxx: <c>GetDefaultPrinterName</c> e <c>ImplInitPrnQueueList</c>); com uma impressora de rede
    /// ou WSD fora do ar como padrão, o Windows mostra "Aguardando conexão de impressora…" e cada conversão levava ~50 s (medido neste PC,
    /// com a OBR-Sede). Com as duas variáveis, o vcl não chama o spooler: a fila fica vazia e o documento usa a "impressora de tela"
    /// (<c>ImplInitDisplay</c>). O PDF sai igual pixel a pixel (modelos do kit comparados) e a conversão cai para ~1,7 s.</summary>
    internal static readonly string[] SemImpressoras = ["SAL_DISABLE_PRINTERLIST", "SAL_DISABLE_DEFAULTPRINTER"];

    /// <summary>O soffice desta conversão (null fora dela): <see cref="EncerrarEmCurso"/> o encerra de fora (o app fechando).</summary>
    static Process? emCurso;

    /// <summary>Encerra já, de forma síncrona, a árvore do soffice que está convertendo (o app fechando com um preparo em curso: sem isso o
    /// processo do app terminaria e o soffice ficaria órfão, segurando o perfil). Sem conversão em curso, nada.</summary>
    public static void EncerrarEmCurso()
    {
        var p = Volatile.Read(ref emCurso);
        if (p is null) return;
        try
        {
            p.Kill(entireProcessTree: true);
            p.WaitForExit(5_000);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException) { }
    }

    /// <summary>Uma conversão por vez neste computador (também entre duas janelas do app): o perfil é um só.</summary>
    const string NomeMutex = @"Local\PMVNI-Editor-LibreOffice-perfil";

    /// <summary>O soffice que convertia com o perfil de <paramref name="raizPerfil"/> e não foi encerrado (o app caiu no meio): se ainda está vivo
    /// (mesmo pid, mesmo início, nome soffice), a árvore dele é encerrada, para não deixar o perfil travado; sem ninguém vivo, a trava que
    /// ele deixou no perfil sai. Só é chamado com o mutex na mão: nenhuma conversão viva usa o perfil agora.</summary>
    internal static void EncerrarOrfao(string raizPerfil)
    {
        var arquivo = ArquivoPid(raizPerfil);
        if (File.Exists(arquivo))
        {
            try
            {
                var partes = File.ReadAllText(arquivo).Split(';');
                if (partes.Length == 2 && int.TryParse(partes[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid)
                    && long.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var inicio))
                {
                    using var orfao = Process.GetProcessById(pid);
                    if (orfao.ProcessName.StartsWith("soffice", StringComparison.OrdinalIgnoreCase)
                        && Math.Abs(orfao.StartTime.ToUniversalTime().Ticks - inicio) < TimeSpan.TicksPerSecond * 2)
                    {
                        orfao.Kill(entireProcessTree: true);
                        orfao.WaitForExit(10_000);
                    }
                }
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception or IOException or UnauthorizedAccessException) { }
            try { File.Delete(arquivo); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        // A trava do perfil (o LibreOffice a grava em user\.lock) de um soffice que não existe mais.
        foreach (var trava in new[] { Path.Combine(raizPerfil, ".lock"), Path.Combine(raizPerfil, "user", ".lock") })
            try { if (File.Exists(trava)) File.Delete(trava); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    /// <summary><c>&lt;raizPerfil&gt;.pid</c> (ao lado do perfil, fora dele): o pid e o início do soffice que está convertendo.</summary>
    public static string ArquivoPid(string raizPerfil) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(raizPerfil)) + ".pid";

    /// <summary>Converte uma cópia do DOCX em <c>&lt;pastaTrabalho&gt;\conv</c>, com o TEMP lá e o perfil em <c>raizPerfil</c> (um só, persistente:
    /// criado na primeira conversão e nunca apagado). Sem <c>raizPerfil</c>, o perfil fica na pasta de trabalho (Tarefa 15). Uma conversão
    /// por vez (mutex); um soffice órfão de uma queda, que segure o perfil, é encerrado antes.</summary>
    public ResultadoConversao Converter(string docx, string pastaTrabalho, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(docx);
        ArgumentNullException.ThrowIfNull(pastaTrabalho);
        if (!Disponivel) return new ResultadoConversao(null, "SEM_LIBREOFFICE", null);
        var conv = Path.Combine(pastaTrabalho, "conv");
        if (Directory.Exists(conv)) Directory.Delete(conv, true);
        Directory.CreateDirectory(Path.Combine(conv, "tmp"));
        var raiz = raizPerfil ?? Path.Combine(conv, "perfil");
        Directory.CreateDirectory(raiz);
        var copia = Path.Combine(conv, "copia.docx");
        File.Copy(docx, copia);
        // O LibreOffice recebe o nome curto do perfil e do TEMP (uma pasta de dados funda passaria do limite).
        var perfil = CaminhoCurto(raiz);
        var temp = CaminhoCurto(Path.Combine(conv, "tmp"));
        if (!PerfilCabe(perfil))
            return new ResultadoConversao(null, "CONVERSAO_FALHOU", "a pasta do perfil do LibreOffice é funda demais (perfil com "
                + perfil.Length.ToString(CultureInfo.InvariantCulture) + " caracteres; o máximo é " + MaximoPerfil.ToString(CultureInfo.InvariantCulture) + ")");

        using var mutex = new Mutex(false, NomeMutex);
        var comMutex = false;
        try
        {
            try { comMutex = mutex.WaitOne(TimeSpan.Zero); }
            catch (AbandonedMutexException) { comMutex = true; } // a outra janela caiu no meio: o perfil é nosso agora
            var espera = Stopwatch.StartNew();
            while (!comMutex)
            {
                ct.ThrowIfCancellationRequested();
                if (espera.Elapsed > _limite) return new ResultadoConversao(null, "TEMPO_ESGOTADO", "outra conversão do LibreOffice não terminou");
                try { comMutex = mutex.WaitOne(250); }
                catch (AbandonedMutexException) { comMutex = true; }
            }
            if (raizPerfil is not null) EncerrarOrfao(raiz);
            return Executar(copia, conv, perfil, temp, raizPerfil is null ? null : ArquivoPid(raiz), ct);
        }
        finally { if (comMutex) mutex.ReleaseMutex(); }
    }

    /// <summary>O que resta do limite depois de <paramref name="gasto"/>, com um piso de 2 s: o processo acabou de sair e as saídas só precisam chegar ao fim.</summary>
    TimeSpan RestoDoLimite(TimeSpan gasto) => TimeSpan.FromSeconds(Math.Max(2, (_limite - gasto).TotalSeconds));

    /// <summary>Encerra a árvore do processo (e a dos filhos que ficaram para trás, segurando os canais).</summary>
    static void Matar(Process p)
    {
        try { p.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or NotSupportedException) { }
    }

    ResultadoConversao Executar(string copia, string conv, string perfil, string temp, string? arquivoPid, CancellationToken ct)
    {
        ProcessoInterno programa;
        try { programa = ProcessoInterno.Iniciar(Preparar(soffice!, copia, conv, perfil, temp)); }
        catch (Win32Exception e) { return new ResultadoConversao(null, "CONVERSAO_FALHOU", e.Message); }
        using (programa)
        {
            var p = programa.Processo;
            Volatile.Write(ref emCurso, p);
            try
            {
                if (arquivoPid is not null)
                    try { File.WriteAllText(arquivoPid, p.Id.ToString(CultureInfo.InvariantCulture) + ";" + p.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception) { }
                var mensagens = new System.Collections.Concurrent.ConcurrentQueue<string>();
                async Task Guardar(StreamReader leitor)
                {
                    while (await leitor.ReadLineAsync().ConfigureAwait(false) is { } linha)
                        if (!string.IsNullOrWhiteSpace(linha) && mensagens.Count < 20) mensagens.Enqueue(linha.Trim());
                }
                var drenar = Task.WhenAll(Guardar(programa.StandardOutput), Guardar(programa.StandardError));
                try { programa.StandardInput.Close(); }
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
                // Saiu: espera também as saídas redirecionadas terminarem de chegar (sem argumento), no sucesso e na falha, mas só o que
                // resta do limite (e com o cancelamento): um soffice.bin filho que segure os canais não pode prender o mutex do perfil.
                bool preso;
                try { preso = !drenar.Wait(RestoDoLimite(relogio.Elapsed), ct); }
                catch (OperationCanceledException)
                {
                    Matar(p);
                    throw;
                }
                if (preso)
                {
                    Matar(p);
                    try { drenar.Wait(10_000); }
                    catch (AggregateException) { }
                }
                var pdf = Path.Combine(conv, "copia.pdf");
                var bytes = File.Exists(pdf) ? File.ReadAllBytes(pdf) : Array.Empty<byte>();
                if (bytes.Length >= TamanhoMinimoPdf && bytes.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
                    return new ResultadoConversao(bytes, null, null);
                var ultima = mensagens.LastOrDefault(m => m.Contains("Error", StringComparison.OrdinalIgnoreCase)) ?? mensagens.LastOrDefault();
                return new ResultadoConversao(null, "CONVERSAO_FALHOU", "código " + p.ExitCode.ToString(CultureInfo.InvariantCulture)
                    + (ultima is null ? "" : ": " + (ultima.Length > 200 ? ultima[..200] + "…" : ultima)));
            }
            finally
            {
                Volatile.Write(ref emCurso, null);
                if (arquivoPid is not null) try { File.Delete(arquivoPid); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
    }
}
