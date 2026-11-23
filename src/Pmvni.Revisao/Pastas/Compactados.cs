using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Um arquivo tirado de um compactado: onde ficou (pasta temporária), o caminho dentro do compactado e o nome do compactado.</summary>
public sealed record ArquivoExtraido(string Caminho, string Interno, string Compactado);

/// <summary>O que saiu de um compactado e os avisos (o que ficou de fora e por quê).</summary>
public sealed record ResultadoExtracao(IReadOnlyList<ArquivoExtraido> Arquivos, IReadOnlyList<string> Avisos);

/// <summary>Anexos compactados no Novo relatório (pedido do usuário, 2026-11-16: "às vezes baixamos arquivos compactados para envio"). O kit não
/// descompacta (o inventário da análise só lê os arquivos soltos), então o editor abre o .zip, o .rar ou o .7z numa pasta temporária e cada
/// arquivo de dentro vira um anexo comum, arquivado pela regra do kit (<see cref="Arquivamento"/>). O .zip pelo próprio .NET (nomes em UTF-8 ou
/// na página 850 do Windows); o .rar e o .7z pelo <c>tar.exe</c> do Windows (libarchive), que recusa caminhos com "..". Nada sai da pasta
/// temporária, compactados dentro de compactados são abertos (até 2 níveis) e o que é lixo de sistema (__MACOSX, Thumbs.db) fica de fora.</summary>
public static class Compactados
{
    public static IReadOnlySet<string> Extensoes { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".zip", ".rar", ".7z" };

    /// <summary>Limites de um compactado (contra bomba de compressão e engano): 4 GB descompactados e 3.000 arquivos.</summary>
    public const long LimiteBytes = 4L << 30;
    public const int LimiteArquivos = 3000;
    const int ProfundidadeMaxima = 2;

    static readonly string[] PastasLixo = ["__MACOSX"];
    static readonly string[] ArquivosLixo = ["Thumbs.db", "desktop.ini", ".DS_Store"];

    public static bool EhCompactado(string caminho) => Extensoes.Contains(Path.GetExtension(caminho));

    /// <summary>A pasta temporária dos compactados abertos (<c>%TEMP%\PmvniAnexos</c>).</summary>
    public static string PastaTemporaria() => Path.Combine(Path.GetTempPath(), "PmvniAnexos");

    /// <summary>Apaga as pastas de extração mais velhas que <paramref name="idade"/> (de sessões que fecharam sem limpar). A pasta
    /// <paramref name="exceto"/> (a da sessão aberta, com anexos ainda na lista) nunca é apagada, por mais velha que seja.</summary>
    public static void LimparAntigos(TimeSpan idade, string? exceto = null)
    {
        var raiz = PastaTemporaria();
        if (!Directory.Exists(raiz)) return;
        var manter = exceto is null ? null : Path.GetFullPath(exceto).TrimEnd(Path.DirectorySeparatorChar);
        foreach (var d in Directory.EnumerateDirectories(raiz))
        {
            if (manter is not null && string.Equals(Path.GetFullPath(d).TrimEnd(Path.DirectorySeparatorChar), manter, StringComparison.OrdinalIgnoreCase)) continue;
            try { if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(d) > idade) Directory.Delete(d, recursive: true); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    /// <summary>Abre o compactado numa subpasta nova de <paramref name="pastaBase"/>. Erro que impede abrir (senha, arquivo corrompido, formato
    /// não suportado): <see cref="InvalidDataException"/> com a mensagem em português.</summary>
    public static ResultadoExtracao Extrair(string compactado, string pastaBase, CancellationToken ct) =>
        Extrair(compactado, Path.GetFileName(compactado), "", pastaBase, 0, ct);

    static ResultadoExtracao Extrair(string compactado, string nomeExibido, string prefixo, string pastaBase, int nivel, CancellationToken ct)
    {
        var destino = Path.Combine(pastaBase, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destino);
        var avisos = new List<string>();
        if (Path.GetExtension(compactado).Equals(".zip", StringComparison.OrdinalIgnoreCase)) ExtrairZip(compactado, nomeExibido, destino, ct);
        else ExtrairComTar(compactado, nomeExibido, destino, ct);

        var raiz = Path.GetFullPath(destino) + Path.DirectorySeparatorChar;
        var arquivos = new List<ArquivoExtraido>();
        long total = 0;
        foreach (var f in Directory.EnumerateFiles(destino, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            var completo = Path.GetFullPath(f);
            if (!completo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) continue;   // nunca aceita nada fora da pasta de extração
            var interno = prefixo + completo[raiz.Length..].Replace('\\', '/');
            var partes = interno.Split('/');
            if (partes.Any(p => PastasLixo.Contains(p, StringComparer.OrdinalIgnoreCase)) || ArquivosLixo.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                || Path.GetFileName(f).StartsWith("._", StringComparison.Ordinal))
                continue;
            var info = new FileInfo(f);
            if (info.LinkTarget is not null) { avisos.Add($"{nomeExibido}: {interno} é um atalho e ficou de fora."); continue; }
            var fisico = ArquivoGerado.Caminho(completo);
            if (!string.Equals(completo, fisico, StringComparison.Ordinal)) File.Move(completo, fisico);
            if (EhCompactado(f) && nivel + 1 < ProfundidadeMaxima)
            {
                try
                {
                    var dentro = Extrair(fisico, nomeExibido, interno + "/", pastaBase, nivel + 1, ct);
                    arquivos.AddRange(dentro.Arquivos);
                    avisos.AddRange(dentro.Avisos);
                    continue;
                }
                catch (InvalidDataException e) { avisos.Add($"{nomeExibido}: {interno} não pôde ser aberto ({e.Message}); vai como está."); }
                catch (IOException e) { avisos.Add($"{nomeExibido}: {interno} não pôde ser aberto ({MensagensDeErro.Traduzir(e).TrimEnd('.')}); vai como está."); }
            }
            total += info.Length;
            if (total > LimiteBytes) throw new InvalidDataException($"{nomeExibido} passa de 4 GB descompactado; descompacte à mão e anexe os arquivos.");
            arquivos.Add(new ArquivoExtraido(fisico, interno, nomeExibido));
            if (arquivos.Count > LimiteArquivos) throw new InvalidDataException($"{nomeExibido} tem mais de {LimiteArquivos} arquivos; descompacte à mão e anexe só os do processo.");
        }
        if (arquivos.Count == 0 && nivel == 0) avisos.Add($"{nomeExibido} não tem arquivos para anexar.");
        return new ResultadoExtracao(arquivos, avisos);
    }

    static void ExtrairZip(string zip, string nomeExibido, string destino, CancellationToken ct)
    {
        // Sem a marca de UTF-8, o Explorador do Windows grava os nomes na página 850 (OEM do Brasil): "Memória.pdf" sai certo.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var raiz = Path.GetFullPath(destino) + Path.DirectorySeparatorChar;
        try
        {
            using var arquivo = ZipFile.Open(zip, ZipArchiveMode.Read, Encoding.GetEncoding(850));
            long total = 0;
            var n = 0;
            var buffer = new byte[1 << 20];
            foreach (var e in arquivo.Entries)
            {
                ct.ThrowIfCancellationRequested();
                if (e.FullName.EndsWith('/') || e.FullName.EndsWith('\\')) continue;
                var alvo = Path.GetFullPath(Path.Combine(destino, e.FullName.Replace('\\', '/').TrimStart('/')));
                if (!alvo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"{nomeExibido} tem um caminho que sai da pasta ({e.FullName}); por segurança não foi aberto.");
                if (++n > LimiteArquivos) throw new InvalidDataException($"{nomeExibido} tem mais de {LimiteArquivos} arquivos; descompacte à mão e anexe só os do processo.");
                Directory.CreateDirectory(Path.GetDirectoryName(alvo)!);
                // Mesmo nome duas vezes (ou só diferente na caixa, no Windows): o segundo vira "nome (2).ext", como na regra do kit.
                for (var k = 2; File.Exists(alvo) || Directory.Exists(alvo); k++)
                    alvo = Path.Combine(Path.GetDirectoryName(alvo)!, $"{Path.GetFileNameWithoutExtension(e.Name)} ({k}){Path.GetExtension(e.Name)}");
                // O limite conta os bytes de fato escritos (o tamanho declarado na entrada pode estar errado).
                using (var origem = e.Open())
                using (var saida = new FileStream(alvo, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    int lidos;
                    while ((lidos = origem.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        total += lidos;
                        if (total > LimiteBytes) throw new InvalidDataException($"{nomeExibido} passa de 4 GB descompactado; descompacte à mão e anexe os arquivos.");
                        saida.Write(buffer, 0, lidos);
                    }
                }
                try { File.SetLastWriteTime(alvo, e.LastWriteTime.DateTime); } catch (ArgumentOutOfRangeException) { }
            }
        }
        catch (InvalidDataException e) when (!e.Message.StartsWith(nomeExibido, StringComparison.Ordinal))
        {
            throw new InvalidDataException(MotivoZip(nomeExibido, e), e);
        }
        catch (NotSupportedException e) { throw new InvalidDataException(MotivoZip(nomeExibido, e), e); }
    }

    static string MotivoZip(string nome, Exception e) =>
        e.Message.Contains("encrypt", StringComparison.OrdinalIgnoreCase) || e.Message.Contains("criptograf", StringComparison.OrdinalIgnoreCase)
            ? $"{nome} tem senha; descompacte à mão e anexe os arquivos."
            : $"{nome} não pôde ser aberto (arquivo corrompido ou num formato que o editor não abre).";

    /// <summary>O .rar e o .7z pelo <c>tar.exe</c> do Windows (bsdtar/libarchive): sem a opção -P ele recusa caminhos absolutos e com "..".</summary>
    static void ExtrairComTar(string compactado, string nomeExibido, string destino, CancellationToken ct)
    {
        var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tar)) throw new InvalidDataException($"{nomeExibido}: este Windows não tem o tar.exe para abrir {Path.GetExtension(compactado)}; descompacte à mão e anexe os arquivos.");
        var si = new ProcessStartInfo(tar) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true, StandardErrorEncoding = Encoding.UTF8 };
        foreach (var a in new[] { "-x", "-f", compactado, "-C", destino }) si.ArgumentList.Add(a);
        using var p = ProcessoInterno.Iniciar(si);
        var erro = p.StandardError.ReadToEndAsync(CancellationToken.None);
        _ = p.StandardOutput.ReadToEndAsync(CancellationToken.None);
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limite.CancelAfter(TimeSpan.FromMinutes(10));
            // O tar.exe não tem limite de tamanho: a pasta é medida a cada segundo enquanto ele extrai, e passou de 4 GB ou de 3.000
            // arquivos, o tar é parado na hora (sem encher o disco do %TEMP%).
            var fim = p.Processo.WaitForExitAsync(limite.Token);
            while (!fim.Wait(TimeSpan.FromSeconds(3))) // a cada 3 s: percorrer a pasta toda a cada segundo pesava com milhares de arquivos
            {
                long bytes = 0;
                var n = 0;
                try
                {
                    foreach (var f in new DirectoryInfo(destino).EnumerateFiles("*", SearchOption.AllDirectories)) { bytes += f.Length; n++; }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }
                if (bytes > LimiteBytes || n > LimiteArquivos)
                {
                    try { p.Processo.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
                    throw new InvalidDataException(bytes > LimiteBytes
                        ? $"{nomeExibido} passa de 4 GB descompactado; descompacte à mão e anexe os arquivos."
                        : $"{nomeExibido} tem mais de {LimiteArquivos} arquivos; descompacte à mão e anexe só os do processo.");
                }
            }
            fim.GetAwaiter().GetResult();
        }
        catch (AggregateException e) when (e.InnerException is OperationCanceledException)
        {
            try { p.Processo.Kill(true); } catch (Exception x) when (x is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            ct.ThrowIfCancellationRequested();
            throw new InvalidDataException($"{nomeExibido} demorou demais para abrir; descompacte à mão e anexe os arquivos.");
        }
        catch (OperationCanceledException)
        {
            try { p.Processo.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            ct.ThrowIfCancellationRequested();
            throw new InvalidDataException($"{nomeExibido} demorou demais para abrir; descompacte à mão e anexe os arquivos.");
        }
        if (p.Processo.ExitCode == 0) return;
        var texto = erro.Wait(TimeSpan.FromSeconds(2)) ? erro.Result : "";
        var temArquivo = Directory.EnumerateFiles(destino, "*", SearchOption.AllDirectories).Any();
        if (Regex.IsMatch(texto, "passphrase|password|encrypt", RegexOptions.IgnoreCase))
            throw new InvalidDataException($"{nomeExibido} tem senha; descompacte à mão e anexe os arquivos.");
        if (!temArquivo)
            throw new InvalidDataException($"{nomeExibido} não pôde ser aberto (arquivo corrompido ou num formato que o editor não abre).");
        // Abriu em parte (uma entrada ruim): segue com o que saiu; quem chama avisa pelo número de arquivos.
    }

    // ---------- destino sugerido (regra de organização do kit) ----------

    static readonly Regex Documento = new(
        @"\b(requerimento|matricula|certidao|certidoes|escritura|art|rrt|trt|procuracao|memorial|bci|espelho|cadastr\w*|iptu|rg|cpf|cnh|cnpj|contrato|declaracao|comprovante|alvara|habite\w*|documentos?|identidade|taxa|boleto|guia|oficio|carta|anuencia|licenca)\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>O destino que a regra do kit dá ao arquivo de um compactado: peça administrativa (requerimento, matrícula, certidão, ART/RRT,
    /// procuração, memorial, BCI…) → <see cref="DestinoAnexo.Documento"/>; o resto é projeto (<paramref name="projeto"/>: INICIAL ou a revisão).
    /// O usuário confere na linha e pode trocar, como em qualquer anexo.</summary>
    public static DestinoAnexo SugerirDestino(string interno, DestinoAnexo projeto)
    {
        var semAcento = new string(interno.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        static string Palavras(string s) => Regex.Replace(s, @"[_\-\.\s/\\]+", " ");   // "ART_projeto" vira "ART projeto": cada termo é palavra
        // O nome do arquivo manda (uma "Matrícula.pdf" dentro de PROJETO é documento); depois a pasta do compactado (PROJETO/… é projeto).
        if (Documento.IsMatch(Palavras(Path.GetFileName(semAcento)))) return DestinoAnexo.Documento;
        var tudo = Palavras(semAcento);
        if (Regex.IsMatch(tudo, @"\b(projeto|pranchas?|plantas?|arquitetonico|dwg|dxf)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return projeto;
        return Documento.IsMatch(tudo) ? DestinoAnexo.Documento : projeto;
    }
}
