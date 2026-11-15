using System.Text.RegularExpressions;
using Xunit;

namespace Pmvni.Legislacao.Testes;

/// <summary>Copiar e citar (desenho §11.7) e a página do leitor no servidor (§11.4).</summary>
public sealed class CitacaoTestes
{
    const string Titulo = "Lei nº 9.999/2099 (fictícia)";

    [Theory]
    [InlineData("O afastamento frontal mínimo é de 3,00 m.", 2, 2, "“O afastamento frontal mínimo é de 3,00 m.” (Lei nº 9.999/2099 (fictícia), página 2)")]
    [InlineData("  Art. 2º O afastamento\n frontal ", 2, 3, "“Art. 2º O afastamento frontal” (Lei nº 9.999/2099 (fictícia), páginas 2–3)")]
    [InlineData("a \r\n\tb", 3, 2, "“a b” (Lei nº 9.999/2099 (fictícia), páginas 2–3)")] // páginas fora de ordem: a menor primeiro
    public void citacao_exata(string texto, int pi, int pf, string esperado) => Assert.Equal(esperado, Citacao.De(texto, Titulo, pi, pf));

    [Theory]
    [InlineData("")]
    [InlineData("  \n ")]
    public void sem_texto_nao_ha_citacao(string texto) => Assert.Null(Citacao.De(texto, Titulo, 1, 1));

    [Fact]
    public void copiar_normaliza_quebras()
    {
        Assert.Equal("a\r\nb\r\nc", Citacao.TextoParaCopiar("a\nb\r\nc "));
        Assert.Equal("x\r\ny", Citacao.TextoParaCopiar("\n x\ry \n"));
        Assert.Equal("", Citacao.TextoParaCopiar("  \n "));
    }

    [Fact]
    public void leitor_com_csp_do_desenho()
    {
        const string Desenho = "default-src 'none'; script-src https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs https://leg.pmvni.invalid/pdfjs/web/pdf_viewer.mjs "
            + "https://leg.pmvni.invalid/leitor.js 'wasm-unsafe-eval'; worker-src https://leg.pmvni.invalid/pdfjs/build/pdf.worker.min.mjs; "
            + "connect-src https://leg.pmvni.invalid; img-src https://leg.pmvni.invalid data: blob:; font-src https://leg.pmvni.invalid data: blob:; "
            + "style-src https://leg.pmvni.invalid 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-src 'none'; object-src 'none'";
        Assert.Equal(Desenho, ServidorLegislacao.CspLeitor);
        var s = new ServidorLegislacao();
        var html = s.Responder("GET", "https://leg.pmvni.invalid/leitor.html", null)!;
        Assert.Equal(200, html.Status);
        Assert.Equal("text/html; charset=utf-8", html.TipoConteudo);
        Assert.Equal(Desenho, html.Cabecalhos.Single(h => h.Key == "Content-Security-Policy").Value);
        Assert.Contains(html.Cabecalhos, h => h.Key == "X-Content-Type-Options" && h.Value == "nosniff");
        Assert.Contains(html.Cabecalhos, h => h.Key == "Cache-Control" && h.Value == "no-store");

        var texto = System.Text.Encoding.UTF8.GetString(RecursosLegislacao.Ler("leitor.html")!);
        var meta = Regex.Match(texto, "<meta http-equiv=\"Content-Security-Policy\" content=\"([^\"]+)\"");
        Assert.True(meta.Success);
        Assert.Equal(Desenho, meta.Groups[1].Value);
        Assert.Single(Regex.Matches(texto, "<script type=\"module\" src=\"leitor.js\"></script>"));
        Assert.DoesNotContain("<script>", texto);
        // Ordem: pdf.min.mjs (pdfjsLib), pdf_viewer.mjs (pdfjsViewer), leitor.js.
        var i1 = texto.IndexOf("src=\"pdfjs/build/pdf.min.mjs\"", StringComparison.Ordinal);
        var i2 = texto.IndexOf("src=\"pdfjs/web/pdf_viewer.mjs\"", StringComparison.Ordinal);
        var i3 = texto.IndexOf("src=\"leitor.js\"", StringComparison.Ordinal);
        Assert.True(i1 >= 0 && i1 < i2 && i2 < i3);

        var js = s.Responder("GET", "https://leg.pmvni.invalid/leitor.js", null)!;
        Assert.Equal("text/javascript; charset=utf-8", js.TipoConteudo);
        Assert.DoesNotContain(js.Cabecalhos, h => h.Key == "Content-Security-Policy");
        var css = s.Responder("GET", "https://leg.pmvni.invalid/leitor.css", null)!;
        Assert.Equal(200, css.Status);
        Assert.Equal("text/css; charset=utf-8", css.TipoConteudo);
        Assert.Null(s.Responder("POST", "https://leg.pmvni.invalid/leitor.html", null));
        Assert.Null(s.Responder("GET", "https://leg.pmvni.invalid/leitor.html?x", null));
    }
}
