using System.Text.RegularExpressions;
using Pmvni.Legislacao.Testes.Ficticios;
using Xunit;

namespace Pmvni.Legislacao.Testes;

public sealed class ServidorLegislacaoTestes : IDisposable
{
    readonly Cenario c = new("servidor");

    public void Dispose() => c.Dispose();

    static string Cabecalho(RespostaLeg r, string nome) =>
        r.Cabecalhos.Where(h => string.Equals(h.Key, nome, StringComparison.OrdinalIgnoreCase)).Select(h => h.Value).SingleOrDefault() ?? "";

    static byte[] Corpo(RespostaLeg r)
    {
        using var s = r.AbrirCorpo!();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Theory]
    [InlineData("https://leg.pmvni.invalid/extrator.html", "text/html; charset=utf-8")]
    [InlineData("https://leg.pmvni.invalid/extrator.js", "text/javascript; charset=utf-8")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs", "text/javascript; charset=utf-8")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/build/pdf.worker.min.mjs", "text/javascript; charset=utf-8")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/wasm/openjpeg.wasm", "application/wasm")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/standard_fonts/FoxitSerif.pfb", "application/octet-stream")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/web/pdf_viewer.css", "text/css; charset=utf-8")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/LICENSE", "application/octet-stream")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/cmaps/78-EUC-H.bcmap", "application/octet-stream")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/iccs/CGATS001Compat-v2-micro.icc", "application/octet-stream")]
    public void responde_o_permitido(string uri, string tipo)
    {
        var s = new ServidorLegislacao();
        var r = s.Responder("GET", uri, null);

        Assert.NotNull(r);
        Assert.Equal(200, r.Status);
        Assert.Equal("OK", r.Razao);
        Assert.Equal(tipo, r.TipoConteudo);
        Assert.Equal("nosniff", Cabecalho(r, "X-Content-Type-Options"));
        Assert.Equal("no-store", Cabecalho(r, "Cache-Control"));
        Assert.True(r.Tamanho > 0);
        Assert.Equal(r.Tamanho, Corpo(r).Length);
        var esperadoCsp = uri.EndsWith("/extrator.html", StringComparison.Ordinal) ? ServidorLegislacao.CspExtrator
            : uri.EndsWith("/pdf.worker.min.mjs", StringComparison.Ordinal) ? ServidorLegislacao.CspWorker : "";
        Assert.Equal(esperadoCsp, Cabecalho(r, "Content-Security-Policy"));
    }

    [Fact]
    public void csp_do_extrator_e_do_worker()
    {
        Assert.Equal(
            "default-src 'none'; script-src https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs https://leg.pmvni.invalid/extrator.js 'wasm-unsafe-eval'; "
            + "worker-src https://leg.pmvni.invalid/pdfjs/build/pdf.worker.min.mjs; connect-src https://leg.pmvni.invalid; "
            + "img-src https://leg.pmvni.invalid data: blob:; font-src https://leg.pmvni.invalid data: blob:; "
            + "style-src https://leg.pmvni.invalid 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-src 'none'; object-src 'none'",
            ServidorLegislacao.CspExtrator);
        Assert.Equal("default-src 'none'; connect-src https://leg.pmvni.invalid; script-src 'wasm-unsafe-eval'", ServidorLegislacao.CspWorker);
        Assert.DoesNotContain("pdf_viewer", ServidorLegislacao.CspExtrator);
        Assert.DoesNotContain("leitor", ServidorLegislacao.CspExtrator);
    }

    [Fact]
    public void meta_csp_do_extrator_html_e_a_do_servidor()
    {
        var html = System.Text.Encoding.UTF8.GetString(RecursosLegislacao.Ler("extrator.html")!);
        var m = Regex.Match(html, "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]+)\"");
        Assert.True(m.Success);
        Assert.Equal(ServidorLegislacao.CspExtrator, m.Groups[1].Value.Replace("&#39;", "'"));
        Assert.Contains("<script type=\"module\" src=\"pdfjs/build/pdf.min.mjs\"></script>", html);
        Assert.Contains("<script type=\"module\" src=\"extrator.js\"></script>", html);
        Assert.DoesNotContain("<script>", html); // nada de script em linha: a CSP não deixaria
    }

    [Fact]
    public void pdf_registrado_em_fluxo_e_com_range()
    {
        var s = new ServidorLegislacao();
        var caminho = c.Original(Cenario.Lei);
        var bytes = File.ReadAllBytes(caminho);
        var url = s.RegistrarPdf(caminho);

        Assert.Matches(@"^https://leg\.pmvni\.invalid/pdf/[0-9a-f]{32}\.pdf$", url);
        Assert.DoesNotContain("FICTICIA", url);
        Assert.DoesNotContain(c.Originais, url); // o caminho do disco nunca vai para a página

        var inteiro = s.Responder("GET", url, null)!;
        Assert.Equal(200, inteiro.Status);
        Assert.Equal("application/pdf", inteiro.TipoConteudo);
        Assert.Equal("bytes", Cabecalho(inteiro, "Accept-Ranges"));
        Assert.Equal(bytes.Length, inteiro.Tamanho);
        Assert.Equal(bytes, Corpo(inteiro));
        Assert.Equal("nosniff", Cabecalho(inteiro, "X-Content-Type-Options"));
        Assert.Equal("no-store", Cabecalho(inteiro, "Cache-Control"));

        var parte = s.Responder("GET", url, "bytes=0-9")!;
        Assert.Equal(206, parte.Status);
        Assert.Equal("Partial Content", parte.Razao);
        Assert.Equal(10, parte.Tamanho);
        Assert.Equal($"bytes 0-9/{bytes.Length}", Cabecalho(parte, "Content-Range"));
        Assert.Equal(bytes[..10], Corpo(parte));

        var resto = s.Responder("GET", url, "bytes=5-")!;
        Assert.Equal(206, resto.Status);
        Assert.Equal($"bytes 5-{bytes.Length - 1}/{bytes.Length}", Cabecalho(resto, "Content-Range"));
        Assert.Equal(bytes[5..], Corpo(resto));

        var sufixo = s.Responder("GET", url, "bytes=-7")!;
        Assert.Equal(206, sufixo.Status);
        Assert.Equal(bytes[^7..], Corpo(sufixo));

        var alem = s.Responder("GET", url, $"bytes=0-{bytes.Length + 100}")!; // o fim é ajustado ao arquivo
        Assert.Equal(206, alem.Status);
        Assert.Equal(bytes, Corpo(alem));

        var fora = s.Responder("GET", url, "bytes=999999-")!;
        Assert.Equal(416, fora.Status);
        Assert.Equal($"bytes */{bytes.Length}", Cabecalho(fora, "Content-Range"));
        Assert.Null(fora.AbrirCorpo);

        // Range ilegível ou com vários intervalos: o arquivo inteiro.
        Assert.Equal(200, s.Responder("GET", url, "bytes=abc")!.Status);
        Assert.Equal(200, s.Responder("GET", url, "bytes=0-1,5-6")!.Status);
        Assert.Equal(200, s.Responder("GET", url, "items=0-1")!.Status);

        s.RemoverPdf(url);
        Assert.Null(s.Responder("GET", url, null));
    }

    [Fact]
    public void cada_registro_tem_o_seu_token_e_o_arquivo_pode_ser_apagado_enquanto_servido()
    {
        var s = new ServidorLegislacao();
        var copia = Path.Combine(c.Originais, "servida.pdf");
        File.Copy(c.Original(Cenario.Lei), copia);
        var a = s.RegistrarPdf(copia);
        var b = s.RegistrarPdf(copia);
        Assert.NotEqual(a, b);
        using var aberto = s.Responder("GET", a, null)!.AbrirCorpo!();
        File.Delete(copia); // FileShare.Delete: excluir uma lei aberta no leitor não esbarra no servidor
        Assert.False(File.Exists(copia));
        Assert.True(aberto.ReadByte() >= 0);
    }

    [Theory]
    [InlineData("http://leg.pmvni.invalid/extrator.html")]
    [InlineData("https://doc.pmvni.invalid/extrator.html")]
    [InlineData("https://leg.pmvni.invalid/extrator.html?x=1")]
    [InlineData("https://leg.pmvni.invalid/extrator.html?")]
    [InlineData("https://leg.pmvni.invalid/extrator.html#a")]
    [InlineData("https://leg.pmvni.invalid:8443/extrator.html")]
    [InlineData("https://usuario@leg.pmvni.invalid/extrator.html")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/../catalogo.json")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/%2e%2e/catalogo.json")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/%2E%2E/catalogo.json")]
    [InlineData("https://leg.pmvni.invalid/pdfjs%2fbuild/pdf.min.mjs")]
    [InlineData("https://leg.pmvni.invalid/pdfjs\\build\\pdf.min.mjs")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/build/pdf.mjs")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/build/pdf.sandbox.min.mjs")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/wasm/quickjs-eval.wasm")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/wasm/openjpeg_nowasm_fallback.js")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/web/viewer.mjs")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs.map")]
    [InlineData("https://leg.pmvni.invalid/pdfjs/VERSAO.json")]
    [InlineData("https://leg.pmvni.invalid/pdf/0123456789abcdef0123456789abcdef.pdf")]
    [InlineData("https://leg.pmvni.invalid/pdf/")]
    [InlineData("https://leg.pmvni.invalid/catalogo.json")]
    [InlineData("https://leg.pmvni.invalid/")]
    [InlineData("https://leg.pmvni.invalid/leitor.html")]
    [InlineData("https://exemplo.invalid/x.pdf")]
    [InlineData("not a uri")]
    public void recusa_o_resto(string uri)
    {
        var s = new ServidorLegislacao();
        Assert.Null(s.Responder("GET", uri, null));
        Assert.Null(s.Responder("POST", "https://leg.pmvni.invalid/extrator.html", null));
        Assert.Null(s.Responder("HEAD", "https://leg.pmvni.invalid/extrator.html", null));
        Assert.Null(s.Responder("GET", null!, null));
    }

    [Fact]
    public void recusa_outros_metodos_inclusive_no_pdf_registrado()
    {
        var s = new ServidorLegislacao();
        var url = s.RegistrarPdf(c.Original(Cenario.Lei));
        Assert.Null(s.Responder("POST", url, null));
        Assert.Null(s.Responder("PUT", url, null));
        Assert.NotNull(s.Responder("get", url, null)); // o método vem do WebView2 em maiúsculas; a caixa não importa
    }

    [Fact]
    public void arquivo_registrado_que_sumiu_e_recusado()
    {
        var s = new ServidorLegislacao();
        var copia = Path.Combine(c.Originais, "some.pdf");
        File.Copy(c.Original(Cenario.Lei), copia);
        var url = s.RegistrarPdf(copia);
        File.Delete(copia);
        Assert.Null(s.Responder("GET", url, null));
    }
}
