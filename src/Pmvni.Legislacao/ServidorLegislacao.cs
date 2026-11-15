using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Pmvni.Legislacao;

/// <summary>Resposta do servidor em memória: o que o filtro de requisições do WebView2 devolve no lugar da rede.</summary>
public sealed record RespostaLeg(int Status, string Razao, string TipoConteudo, Func<Stream>? AbrirCorpo, long? Tamanho,
    IReadOnlyList<KeyValuePair<string, string>> Cabecalhos);

/// <summary>O host virtual <c>leg.pmvni.invalid</c>, <b>sem pasta mapeada</b>: responde só, e da memória, as páginas do extrator e do
/// leitor (com os seus scripts e a folha de estilo do leitor), os arquivos do PDF.js listados no <c>VERSAO.json</c> e os PDFs que o próprio app registrou (token sorteado; o
/// caminho do disco nunca vai para a página). Qualquer outra requisição (outro host, <c>http:</c>, query, <c>..</c>, token
/// desconhecido, método diferente de GET) devolve null, e o app responde 403 (desenho §11.4).</summary>
public sealed partial class ServidorLegislacao
{
    public const string Host = "leg.pmvni.invalid";

    /// <summary>CSP da página do extrator (meta na página e cabeçalho da resposta): só o PDF.js e o extrator rodam.</summary>
    public static string CspExtrator { get; } =
        "default-src 'none'; script-src https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs https://leg.pmvni.invalid/extrator.js 'wasm-unsafe-eval'; "
        + "worker-src https://leg.pmvni.invalid/pdfjs/build/pdf.worker.min.mjs; connect-src https://leg.pmvni.invalid; "
        + "img-src https://leg.pmvni.invalid data: blob:; font-src https://leg.pmvni.invalid data: blob:; "
        + "style-src https://leg.pmvni.invalid 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-src 'none'; object-src 'none'";

    /// <summary>CSP da página do leitor (desenho §11.4, exatamente; meta na página e cabeçalho da resposta): só o PDF.js, o
    /// componente de visualização dele e o leitor rodam.</summary>
    public static string CspLeitor { get; } =
        "default-src 'none'; script-src https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs https://leg.pmvni.invalid/pdfjs/web/pdf_viewer.mjs "
        + "https://leg.pmvni.invalid/leitor.js 'wasm-unsafe-eval'; worker-src https://leg.pmvni.invalid/pdfjs/build/pdf.worker.min.mjs; "
        + "connect-src https://leg.pmvni.invalid; img-src https://leg.pmvni.invalid data: blob:; font-src https://leg.pmvni.invalid data: blob:; "
        + "style-src https://leg.pmvni.invalid 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-src 'none'; object-src 'none'";

    /// <summary>CSP do worker do PDF.js (cabeçalho da resposta do <c>pdf.worker.min.mjs</c>).</summary>
    public static string CspWorker { get; } = "default-src 'none'; connect-src https://leg.pmvni.invalid; script-src 'wasm-unsafe-eval'";

    const string CaminhoWorker = "build/pdf.worker.min.mjs";
    const string TipoJs = "text/javascript; charset=utf-8";
    const string TipoBinario = "application/octet-stream";

    readonly ConcurrentDictionary<string, string> pdfs = new(StringComparer.Ordinal);


    [GeneratedRegex(@"^/pdf/(?<t>[0-9a-f]{32})\.pdf$", RegexOptions.CultureInvariant)]
    private static partial Regex CaminhoPdf();

    /// <summary>Registra o PDF e devolve a URL <c>https://leg.pmvni.invalid/pdf/&lt;32 hex&gt;.pdf</c> (token novo a cada registro).</summary>
    public string RegistrarPdf(string caminho)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caminho);
        var inteiro = Path.GetFullPath(caminho);
        if (!File.Exists(inteiro)) throw new FileNotFoundException("O PDF a servir não existe.", inteiro);
        var token = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        pdfs[token] = inteiro;
        return $"https://{Host}/pdf/{token}.pdf";
    }

    public void RemoverPdf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return;
        var m = CaminhoPdf().Match(u.AbsolutePath);
        if (m.Success) pdfs.TryRemove(m.Groups["t"].Value, out _);
    }

    /// <summary>A resposta para a requisição, ou null (o app responde 403). <paramref name="range"/> é o cabeçalho <c>Range</c>, se houver.</summary>
    public RespostaLeg? Responder(string metodo, string uri, string? range)
    {
        if (!string.Equals(metodo, "GET", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(uri)) return null;
        // Contra desvios de caminho: nada de ..  \  %  ?  #  na URL crua (o Uri normalizaria o que o filtro precisa recusar).
        if (uri.Contains("..", StringComparison.Ordinal) || uri.IndexOfAny(['\\', '%', '?', '#']) >= 0) return null;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u)) return null;
        if (u.Scheme != Uri.UriSchemeHttps || !string.Equals(u.Host, Host, StringComparison.OrdinalIgnoreCase)
            || !u.IsDefaultPort || u.UserInfo.Length > 0 || u.Query.Length > 0 || u.Fragment.Length > 0) return null;

        var caminho = u.AbsolutePath;
        switch (caminho)
        {
            case "/extrator.html":
                return Recurso("extrator.html", "text/html; charset=utf-8", CspExtrator);
            case "/extrator.js":
                return Recurso("extrator.js", TipoJs, null);
            case "/leitor.html":
                return Recurso("leitor.html", "text/html; charset=utf-8", CspLeitor);
            case "/leitor.js":
                return Recurso("leitor.js", TipoJs, null);
            case "/leitor.css":
                return Recurso("leitor.css", "text/css; charset=utf-8", null);
        }
        if (caminho.StartsWith("/pdfjs/", StringComparison.Ordinal))
        {
            var rel = caminho[7..];
            if (!RecursosLegislacao.ShaPdfJs.ContainsKey(rel)) return null;
            return Recurso("pdfjs/" + rel, TipoDe(rel), rel == CaminhoWorker ? CspWorker : null);
        }
        var m = CaminhoPdf().Match(caminho);
        return m.Success && pdfs.TryGetValue(m.Groups["t"].Value, out var arquivo) ? ResponderPdf(arquivo, range) : null;
    }

    static string TipoDe(string rel) => Path.GetExtension(rel).ToLowerInvariant() switch
    {
        ".mjs" or ".js" => TipoJs,
        ".css" => "text/css; charset=utf-8",
        ".wasm" => "application/wasm",
        ".svg" => "image/svg+xml",
        ".gif" => "image/gif",
        _ => TipoBinario, // .icc, .bcmap, .pfb, .ttf e os LICENSE*
    };

    static List<KeyValuePair<string, string>> CabecalhosBase() =>
    [
        new("X-Content-Type-Options", "nosniff"),
        new("Cache-Control", "no-store"),
    ];

    static RespostaLeg? Recurso(string caminho, string tipo, string? csp)
    {
        long tamanho;
        using (var s = RecursosLegislacao.Abrir(caminho))
        {
            if (s is null) return null;
            tamanho = s.Length;
        }
        var cab = CabecalhosBase();
        if (csp is not null) cab.Add(new("Content-Security-Policy", csp));
        return new RespostaLeg(200, "OK", tipo, () => RecursosLegislacao.Abrir(caminho) ?? throw new FileNotFoundException(caminho), tamanho, cab);
    }

    static RespostaLeg? ResponderPdf(string arquivo, string? range)
    {
        long total;
        try
        {
            var info = new FileInfo(arquivo);
            if (!info.Exists) return null;
            total = info.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }

        var cab = CabecalhosBase();
        cab.Add(new("Accept-Ranges", "bytes"));
        switch (LerIntervalo(range, total, out var inicio, out var fim))
        {
            case Intervalo.Insatisfeito:
                cab.Add(new("Content-Range", $"bytes */{total}"));
                return new RespostaLeg(416, "Range Not Satisfiable", "application/pdf", null, null, cab);
            case Intervalo.Parcial:
                cab.Add(new("Content-Range", $"bytes {inicio}-{fim}/{total}"));
                return new RespostaLeg(206, "Partial Content", "application/pdf", () => AbrirPdf(arquivo, inicio, fim - inicio + 1), fim - inicio + 1, cab);
            default:
                return new RespostaLeg(200, "OK", "application/pdf", () => AbrirPdf(arquivo, 0, null), total, cab);
        }
    }

    /// <summary>Abre o arquivo para leitura (<c>FileShare.ReadWrite | Delete</c>: excluir a lei aberta no leitor não esbarra no
    /// servidor), no trecho pedido.</summary>
    static Stream AbrirPdf(string arquivo, long inicio, long? tamanho)
    {
        var fs = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.RandomAccess);
        if (tamanho is null) return fs;
        try { return new FluxoLimitado(fs, inicio, tamanho.Value); }
        catch { fs.Dispose(); throw; }
    }

    enum Intervalo { Inteiro, Parcial, Insatisfeito }

    /// <summary><c>bytes=a-b</c>, <c>bytes=a-</c> e <c>bytes=-n</c>; cabeçalho ilegível ou com vários intervalos ⇒ o arquivo inteiro.</summary>
    static Intervalo LerIntervalo(string? range, long total, out long inicio, out long fim)
    {
        inicio = 0;
        fim = total - 1;
        if (string.IsNullOrWhiteSpace(range)) return Intervalo.Inteiro;
        var texto = range.Trim();
        if (!texto.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase) || texto.Contains(',')) return Intervalo.Inteiro;
        var par = texto[6..].Split('-');
        if (par.Length != 2) return Intervalo.Inteiro;
        var a = par[0].Trim();
        var b = par[1].Trim();
        if (a.Length == 0)
        {
            if (!long.TryParse(b, out var ultimos) || ultimos < 0) return Intervalo.Inteiro;
            if (ultimos == 0 || total == 0) return Intervalo.Insatisfeito;
            inicio = Math.Max(0, total - ultimos);
            return Intervalo.Parcial;
        }
        if (!long.TryParse(a, out inicio) || inicio < 0) return Intervalo.Inteiro;
        if (b.Length > 0)
        {
            if (!long.TryParse(b, out fim) || fim < inicio) return Intervalo.Inteiro;
        }
        if (inicio >= total) return Intervalo.Insatisfeito;
        fim = Math.Min(fim, total - 1);
        return Intervalo.Parcial;
    }

    /// <summary>Janela de leitura (e de posição) de um trecho do arquivo, para o <c>206</c>; fechar o trecho fecha o arquivo.</summary>
    sealed class FluxoLimitado : Stream
    {
        readonly Stream inner;
        readonly long inicio, tamanho;
        long posicao;

        public FluxoLimitado(Stream inner, long inicio, long tamanho)
        {
            this.inner = inner;
            this.inicio = inicio;
            this.tamanho = tamanho;
            inner.Seek(inicio, SeekOrigin.Begin);
        }

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => tamanho;
        public override long Position { get => posicao; set => Seek(value, SeekOrigin.Begin); }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var restante = tamanho - posicao;
            if (restante <= 0) return 0;
            var lido = inner.Read(buffer[..(int)Math.Min(buffer.Length, restante)]);
            posicao += lido;
            return lido;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var restante = tamanho - posicao;
            if (restante <= 0) return 0;
            var lido = await inner.ReadAsync(buffer[..(int)Math.Min(buffer.Length, restante)], ct).ConfigureAwait(false);
            posicao += lido;
            return lido;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var alvo = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => posicao + offset,
                _ => tamanho + offset,
            };
            if (alvo < 0) throw new IOException("Posição antes do início do trecho.");
            posicao = Math.Min(alvo, tamanho);
            inner.Seek(inicio + posicao, SeekOrigin.Begin);
            return posicao;
        }

        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
