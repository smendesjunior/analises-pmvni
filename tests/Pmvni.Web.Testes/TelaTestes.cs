using System.Text.Json;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Xunit;
using Xunit.Abstractions;

namespace Pmvni.Web.Testes;

public sealed class TelaFixture : IAsyncLifetime
{
    /// <summary>Host da composição (mídia de impressão, como na exportação) e host da "tela" (mídia de tela, escala 1),
    /// que fazem o papel do host oculto e do WebView2 visível do app.</summary>
    public HostOculto Composicao = null!, Tela = null!;

    public async Task InitializeAsync()
    {
        Composicao = await HostOculto.CriarAsync(1024, 1400);
        Tela = await HostOculto.CriarAsync(1024, 1400);
        await Tela.CdpAsync("Emulation.setDeviceMetricsOverride", """{"width":0,"height":0,"deviceScaleFactor":1,"mobile":false}""");
    }

    public async Task DisposeAsync()
    {
        await Composicao.DisposeAsync();
        await Tela.DisposeAsync();
    }
}

/// <summary>Tarefa 10: a aparência de tela (fundo cinza, sombras, vãos de 8 mm, zoom) vive só fora do DOM composto das folhas:
/// o zoom nunca recompõe e nada disso chega ao PDF.</summary>
public sealed class TelaTestes : IClassFixture<TelaFixture>
{
    static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    const double LarguraFolhaPx = 210 * 96 / 25.4; // 793,7 px
    readonly HostOculto comp, tela;
    readonly ITestOutputHelper saida;

    public TelaTestes(TelaFixture f, ITestOutputHelper saida)
    {
        comp = f.Composicao;
        tela = f.Tela;
        this.saida = saida;
    }

    /// <summary>Compõe no host oculto e aplica o mesmo plano na "tela" (como o app faz).</summary>
    async Task<(ResultadoRender R, ResultadoComposicao C, ResultadoAplicacao A)> AbrirAsync(string caso)
    {
        var r = RenderCorpus(caso);
        await comp.Documento.CarregarAsync(r, Cfg(caso));
        var c = await comp.Documento.ComporDetalhadoAsync();
        await tela.Documento.CarregarAsync(r, Cfg(caso));
        var a = await tela.Documento.AplicarPlanoAsync(c.Plano);
        return (r, c, a);
    }

    [Fact]
    public async Task zoom_nao_recompoe()
    {
        var (_, c, _) = await AbrirAsync("longo");
        Assert.True(c.Plano.Escala > 1, "o longo tem escala de impressão: pior caso para o zoom");
        var composto = await comp.Documento.TextoPorFolhaAsync();
        var textos = await tela.Documento.TextoPorFolhaAsync();
        Assert.Equal(composto, textos);
        var plano = c.Plano.ParaJson();
        var escala = await JsTexto("getComputedStyle(document.documentElement).getPropertyValue('--pmvni-k').trim()");
        // As mesmas folhas (objetos do DOM) antes e depois: nada é refeito.
        await Js("window.__folhas = [...document.querySelectorAll('section.folha')]; window.__folhas.length");

        foreach (var zoom in new[] { 0.5, 2.0, 1.0 })
        {
            var resposta = await tela.Documento.ChamarAsync("definirZoom", new { zoom }, TimeSpan.FromSeconds(10));
            Assert.Equal(zoom, resposta.GetProperty("zoom").GetDouble());
            Assert.Equal(c.Folhas, resposta.GetProperty("total").GetInt32());
            Assert.Equal(textos, await tela.Documento.TextoPorFolhaAsync());
            Assert.Equal(composto, await tela.Documento.TextoPorFolhaAsync());
            Assert.True((await Js("(() => { const f = [...document.querySelectorAll('section.folha')]; return f.length === window.__folhas.length && f.every((x, i) => x === window.__folhas[i]); })()")).GetBoolean(),
                $"zoom {zoom}: as folhas foram refeitas");
            Assert.Equal(escala, await JsTexto("getComputedStyle(document.documentElement).getPropertyValue('--pmvni-k').trim()"));
            Assert.Equal(1, (await Js("document.querySelectorAll('div.folha.pmvni-medida[data-pmvni-guardado]').length")).GetInt32());
            // A folha aparece na escala pedida, e a barra de rolagem acompanha o tamanho escalado.
            var largura = (await Js("document.querySelector('section.folha').getBoundingClientRect().width")).GetDouble();
            Assert.InRange(largura, LarguraFolhaPx * zoom - 1, LarguraFolhaPx * zoom + 1);
            var altura = (await Js("document.documentElement.scrollHeight")).GetDouble();
            var umaFolha = (await Js("document.querySelectorAll('section.folha')[1].getBoundingClientRect().top - document.querySelectorAll('section.folha')[0].getBoundingClientRect().top")).GetDouble();
            Assert.InRange(umaFolha, (297 + 8) * 96 / 25.4 * zoom - 1, (297 + 8) * 96 / 25.4 * zoom + 1); // 297 mm + vão de 8 mm
            Assert.InRange(altura, c.Folhas * umaFolha * 0.98, c.Folhas * umaFolha * 1.1 + 100);
            saida.WriteLine($"zoom {zoom}: folha {largura:0.#} px, passo {umaFolha:0.#} px, rolagem {altura:0} px");
        }
        Assert.Equal(plano, c.Plano.ParaJson());
        // Recompor agora daria o mesmo plano: o zoom não mexeu no documento do host de composição.
        Assert.Equal(plano, (await comp.Documento.ComporAsync()).ParaJson());
    }

    [Fact]
    public async Task aparencia_de_tela_nao_entra_no_pdf()
    {
        var (r, c, _) = await AbrirAsync("longo");
        var antes = await tela.Documento.ImprimirPdfAsync();      // folhas sem nenhuma aparência de tela
        await tela.Documento.ChamarAsync("definirZoom", new { zoom = 0.5, transbordam = new[] { 1, 2 } }, TimeSpan.FromSeconds(10));
        Assert.Equal("rgb(255, 255, 255)", await JsTexto("getComputedStyle(document.querySelector('section.folha')).backgroundColor")); // a folha segue branca
        Assert.NotEqual("rgb(255, 255, 255)", await JsTexto("getComputedStyle(document.documentElement).backgroundColor")); // a tela tem fundo cinza
        var depois = await tela.Documento.ImprimirPdfAsync();     // mesmo DOM, com a aparência de tela ligada e zoom de 50 %

        var iA = InfoPdf.Ler(antes);
        var iB = InfoPdf.Ler(depois);
        Assert.Equal(c.Folhas, iA.Paginas);
        Assert.Equal(iA.Paginas, iB.Paginas);
        Assert.Equal(iA.LarguraMm, iB.LarguraMm, 3);
        Assert.Equal(iA.AlturaMm, iB.AlturaMm, 3);
        Assert.Equal(await comp.Documento.TextoPorFolhaAsync(), await tela.Documento.TextoPorFolhaAsync());

        // Página a página, os pixels do PDF são idênticos (o PDF do Windows vem do mesmo Chromium, sem sombra nem fundo cinza).
        var dir = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "tela");
        Directory.CreateDirectory(dir);
        var pa = Path.Combine(dir, "antes.pdf");
        var pb = Path.Combine(dir, "depois.pdf");
        await File.WriteAllBytesAsync(pa, antes);
        await File.WriteAllBytesAsync(pb, depois);
        var da = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(pa));
        var db = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(pb));
        Assert.Equal(da.PageCount, db.PageCount);
        for (uint i = 0; i < da.PageCount; i++)
        {
            Assert.Equal(await Pixels(da, i), await Pixels(db, i));
        }

        // O PDF exportado depois de abrir na tela é o mesmo do Verificador: mesmo HTML (sha256), mesmas páginas.
        var pdf = Path.Combine(dir, "exportado.pdf");
        var x = await Exportador.ExportarAsync(r, Cfg("longo"), pdf, CaminhoCorpus("longo"), Modelo);
        using var recibo = JsonDocument.Parse(await File.ReadAllBytesAsync(x.CaminhoRecibo));
        Assert.Equal(PlanoQuebras.Sha256De(r.Html), recibo.RootElement.GetProperty("sha256_html").GetString());
        Assert.Equal(c.Folhas, recibo.RootElement.GetProperty("folhas").GetInt32());
        Assert.Equal(iA.Paginas, InfoPdf.Ler(await File.ReadAllBytesAsync(pdf)).Paginas);
        Assert.Equal(c.Plano.ParaJson(), x.Plano.ParaJson());
    }

    [Fact]
    public async Task rolar_e_ajustar_o_zoom_informam_a_folha_visivel()
    {
        var (_, c, _) = await AbrirAsync("longo");
        var recebidas = new List<int>();
        void Ouvir(Pmvni.Composicao.Web.Ponte.MensagemWeb m)
        {
            if (m.Tipo == "folhaVisivel" && m.Id == "folhaVisivel") lock (recebidas) recebidas.Add(m.Dados.GetProperty("folha").GetInt32());
        }
        tela.Documento.Recebida += Ouvir;
        try
        {
            var z = await tela.Documento.ChamarAsync("definirZoom", new { zoom = 1.0 }, TimeSpan.FromSeconds(10));
            Assert.Equal(1, z.GetProperty("folha").GetInt32());
            var r = await tela.Documento.ChamarAsync("rolarPara", new { folha = 4 }, TimeSpan.FromSeconds(10));
            Assert.Equal(4, r.GetProperty("folha").GetInt32());
            var topo = (await Js("document.querySelectorAll('section.folha')[3].getBoundingClientRect().top")).GetDouble();
            Assert.InRange(topo, -2, 40);
            var fim = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < fim) { lock (recebidas) if (recebidas.Contains(4)) break; await Task.Delay(50); }
            lock (recebidas) Assert.Contains(4, recebidas);

            await Assert.ThrowsAsync<Pmvni.Composicao.Web.Ponte.ErroWebException>(() =>
                tela.Documento.ChamarAsync("rolarPara", new { folha = c.Folhas + 1 }, TimeSpan.FromSeconds(10)));
            var primeiroRef = c.RefsPorFolha[5][0];
            var porRef = await tela.Documento.ChamarAsync("rolarPara", new { @ref = primeiroRef }, TimeSpan.FromSeconds(10));
            Assert.Equal(6, porRef.GetProperty("folha").GetInt32());

            var largura = await tela.Documento.ChamarAsync("definirZoom", new { ajustar = "largura" }, TimeSpan.FromSeconds(10));
            var visivel = (await Js("document.documentElement.clientWidth")).GetDouble();
            Assert.InRange(largura.GetProperty("zoom").GetDouble() * LarguraFolhaPx, visivel * 0.85, visivel);
            var pagina = await tela.Documento.ChamarAsync("definirZoom", new { ajustar = "pagina" }, TimeSpan.FromSeconds(10));
            var alto = (await Js("document.documentElement.clientHeight")).GetDouble();
            Assert.True(pagina.GetProperty("zoom").GetDouble() <= largura.GetProperty("zoom").GetDouble() + 1e-9);
            Assert.True(pagina.GetProperty("zoom").GetDouble() * 297 * 96 / 25.4 <= alto + 1);
        }
        finally { tela.Documento.Recebida -= Ouvir; }
    }

    [Fact]
    public async Task folha_que_transborda_ganha_faixa_vermelha_fora_da_folha()
    {
        var (_, c, _) = await AbrirAsync("longo");
        Assert.True(c.Folhas >= 3);
        await tela.Documento.ChamarAsync("definirZoom", new { zoom = 1.0, transbordam = new[] { 2 } }, TimeSpan.FromSeconds(10));
        Assert.Equal(1, (await Js("document.querySelectorAll('section.folha[data-pmvni-transborda]').length")).GetInt32());
        await tela.Documento.ChamarAsync("rolarPara", new { folha = 2 }, TimeSpan.FromSeconds(10));
        var f = await Js("(() => { const r = document.querySelectorAll('section.folha')[1].getBoundingClientRect(); return { x: r.left + scrollX, y: r.top + scrollY, h: Math.min(r.height, 400) }; })()");
        var esquerda = f.GetProperty("x").GetDouble();
        // A faixa (5 px, #c42b1c) fica à esquerda da folha, na margem cinza: há tinta vermelha ali e nenhuma dentro da folha.
        async Task<int> Vermelhos(double x, double largura)
        {
            var png = await tela.CdpAsync("Page.captureScreenshot", JsonSerializer.Serialize(new
            {
                format = "png", clip = new { x = x + 0, y = f.GetProperty("y").GetDouble() + 0, width = largura, height = f.GetProperty("h").GetDouble(), scale = 1 },
            }));
            using var doc = JsonDocument.Parse(png);
            var bytes = Convert.FromBase64String(doc.RootElement.GetProperty("data").GetString()!);
            var dec = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(new MemoryStream(bytes).AsRandomAccessStream());
            var px = (await dec.GetPixelDataAsync()).DetachPixelData();
            var n = 0;
            for (var i = 0; i < px.Length; i += 4) if (px[i + 2] > 170 && px[i + 1] < 80 && px[i] < 60) n++;
            return n;
        }
        Assert.True(await Vermelhos(esquerda - 6, 6) > 100, "a faixa vermelha não aparece à esquerda da folha");
        Assert.Equal(0, await Vermelhos(esquerda + 20, 200));
        await tela.Documento.ChamarAsync("definirZoom", new { transbordam = Array.Empty<int>() }, TimeSpan.FromSeconds(10));
        Assert.Equal(0, (await Js("document.querySelectorAll('section.folha[data-pmvni-transborda]').length")).GetInt32());
    }

    // ---------- apoio ----------

    static async Task<string> Pixels(Windows.Data.Pdf.PdfDocument doc, uint i)
    {
        using var pagina = doc.GetPage(i);
        using var fluxo = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await pagina.RenderToStreamAsync(fluxo);
        fluxo.Seek(0);
        var dec = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(fluxo);
        var px = await dec.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            new Windows.Graphics.Imaging.BitmapTransform(), Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        return dec.PixelWidth + "x" + dec.PixelHeight + ":" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(px.DetachPixelData()));
    }

    static ConfigFolhas Cfg(string caso) => ConfigFolhas.PadraoDeDados(Modelo, CaminhoCorpus(caso));

    static string CaminhoCorpus(string caso) => Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus", caso + ".json");

    static ResultadoRender RenderCorpus(string caso)
    {
        var r = RelatorioRenderer.Renderizar(CaminhoCorpus(caso), new OpcoesRender(false, true), Modelo);
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        return r;
    }

    async Task<JsonElement> Js(string expressao)
    {
        var r = await tela.CdpAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = expressao, returnByValue = true, awaitPromise = true }));
        using var doc = JsonDocument.Parse(r);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out var ex)) throw new InvalidOperationException("JS: " + ex.GetRawText());
        return doc.RootElement.GetProperty("result").GetProperty("value").Clone();
    }

    async Task<string> JsTexto(string expressao) => (await Js(expressao)).GetString()!;
}
