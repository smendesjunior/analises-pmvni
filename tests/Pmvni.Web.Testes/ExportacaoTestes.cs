using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Ponte;
using Xunit;
using Xunit.Abstractions;

namespace Pmvni.Web.Testes;

public sealed class ExportacaoFixture : IAsyncLifetime
{
    public HostOculto Host = null!;
    public async Task InitializeAsync() => Host = await HostOculto.CriarAsync(1000, 1200);
    public async Task DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>Tarefa 8: aplicar o plano sem medir, texto por folha, captura e exportação do PDF com recibo.
/// A tela e o PDF aplicam o plano calculado no host oculto, logo compartilham a composição.</summary>
public sealed class ExportacaoTestes : IClassFixture<ExportacaoFixture>
{
    static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    static readonly string[] Corpus = ["curto", "longo", "extremos", "estouro", "sem_opcionais"];
    readonly HostOculto host;
    readonly ITestOutputHelper saida;

    public ExportacaoTestes(ExportacaoFixture f, ITestOutputHelper saida)
    {
        host = f.Host;
        this.saida = saida;
    }

    [Fact]
    public async Task aplicar_plano_reproduz_as_mesmas_folhas()
    {
        foreach (var caso in Corpus)
        {
            var r = RenderCorpus(caso);
            await host.Documento.CarregarAsync(r, Cfg(caso));
            var c = await host.Documento.ComporDetalhadoAsync();
            var composto = await host.Documento.TextoPorFolhaAsync();
            Assert.Equal(c.Folhas, composto.Count);
            Assert.All(composto, t => Assert.False(string.IsNullOrWhiteSpace(t), caso + ": folha sem texto"));

            // Mesmo documento (depois de compor) e documento recém-carregado, sem compor: as mesmas folhas.
            var a = await host.Documento.AplicarPlanoAsync(c.Plano);
            Assert.Equal(c.Folhas, a.Folhas);
            Assert.Equal(composto, await host.Documento.TextoPorFolhaAsync());
            var alturas = c.Plano.Estouros.Where(e => e.LarguraMm is null).Select(e => e.Folha).Distinct().Order().ToArray();
            Assert.Equal(alturas, a.FolhasQueTransbordam);

            await host.Documento.CarregarAsync(r, Cfg(caso));
            var b = await host.Documento.AplicarPlanoAsync(c.Plano);
            Assert.Equal(c.Folhas, b.Folhas);
            Assert.Equal(composto, await host.Documento.TextoPorFolhaAsync());
            saida.WriteLine($"{caso}: {c.Folhas} folhas, transbordam [{string.Join(",", b.FolhasQueTransbordam)}]");
        }
    }

    [Fact]
    public async Task plano_de_outro_documento_recusado()
    {
        var curto = RenderCorpus("curto");
        await host.Documento.CarregarAsync(curto, Cfg("curto"));
        var plano = await host.Documento.ComporAsync();

        var longo = RenderCorpus("longo");
        await host.Documento.CarregarAsync(longo, Cfg("longo"));
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => host.Documento.AplicarPlanoAsync(plano));
        Assert.Contains("outro documento", ex.Message);
        Assert.Empty(await host.Documento.TextoPorFolhaAsync()); // nada foi montado

        // O paginador também recusa por conta própria (sha do plano ≠ sha do documento) e valida o plano.
        var cfg = Cfg("longo");
        object Pedido(object p, string sha) => new
        {
            plano = p, sha256Documento = sha, refs = Array.Empty<object>(),
            config = new { cabecalhoUri = cfg.CabecalhoUri, rodapeEsquerda = cfg.RodapeEsquerda, rodapeDireitaModelo = cfg.RodapeDireitaModelo },
        };
        var shaLongo = PlanoQuebras.Sha256De(longo.Html);
        var web = await Assert.ThrowsAsync<ErroWebException>(() =>
            host.Documento.ChamarAsync("aplicarPlano", Pedido(plano, shaLongo), TimeSpan.FromSeconds(30)));
        Assert.Equal("plano_de_outro_documento", web.Codigo);
        var invalido = await Assert.ThrowsAsync<ErroWebException>(() =>
            host.Documento.ChamarAsync("aplicarPlano", Pedido(plano with { Sha256Html = shaLongo, Escala = 2 }, shaLongo), TimeSpan.FromSeconds(30)));
        Assert.Equal("plano_invalido", invalido.Codigo);
        var fora = await Assert.ThrowsAsync<ErroWebException>(() => host.Documento.ChamarAsync("aplicarPlano",
            Pedido(plano with { Sha256Html = shaLongo, InicioDasFolhas = [new PosicaoFluxo([9999], null, null, false)] }, shaLongo),
            TimeSpan.FromSeconds(30)));
        Assert.Equal("plano_invalido", fora.Codigo);
    }

    [Fact]
    public async Task plano_aplicado_com_escala_150_nao_muda_conteudo_das_folhas()
    {
        await using var tela = await HostOculto.CriarAsync(1000, 1200);
        await tela.CdpAsync("Emulation.setDeviceMetricsOverride", """{"width":0,"height":0,"deviceScaleFactor":1.5,"mobile":false}""");
        foreach (var caso in new[] { "longo", "extremos", "estouro" })
        {
            var r = RenderCorpus(caso);
            await host.Documento.CarregarAsync(r, Cfg(caso));
            var c = await host.Documento.ComporDetalhadoAsync();
            var composto = await host.Documento.TextoPorFolhaAsync();

            await tela.Documento.CarregarAsync(r, Cfg(caso));
            Assert.Equal(1.5, await Js(tela, "devicePixelRatio"));
            Assert.Equal("screen", await JsTexto(tela, "matchMedia('print').matches ? 'print' : 'screen'"));
            var a = await tela.Documento.AplicarPlanoAsync(c.Plano);
            Assert.Equal(c.Folhas, a.Folhas);
            Assert.Equal(c.Plano.Escala.ToString("R", System.Globalization.CultureInfo.InvariantCulture), await JsTexto(tela, "getComputedStyle(document.documentElement).getPropertyValue('--pmvni-k').trim()"));
            Assert.Equal(composto, await tela.Documento.TextoPorFolhaAsync());
            saida.WriteLine($"{caso}: k = {c.Plano.Escala}, {a.Folhas} folhas; na tela (DSF 1,5) transbordam [{string.Join(",", a.FolhasQueTransbordam)}]");
        }
    }

    [Fact]
    public async Task capturar_folha_gera_png_do_tamanho_da_folha()
    {
        await host.Documento.CarregarAsync(RenderCorpus("curto"), Cfg("curto"));
        var c = await host.Documento.ComporDetalhadoAsync();
        Assert.True(c.Folhas >= 2);
        var (l96, a96) = TamanhoPng(await host.Documento.CapturarFolhaAsync(2, 96));
        Assert.InRange(l96, 793, 795);   // 210 mm a 96 dpi = 793,7 px
        Assert.InRange(a96, 1122, 1124); // 297 mm = 1122,5 px
        var (l150, a150) = TamanhoPng(await host.Documento.CapturarFolhaAsync(1, 150));
        Assert.InRange(l150, 1239, 1242);  // 210 mm a 150 dpi = 1240,2 px
        Assert.InRange(a150, 1752, 1756);  // 297 mm = 1754,0 px
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => host.Documento.CapturarFolhaAsync(c.Folhas + 1, 96));
    }

    [Fact]
    public async Task exportacao_recusada_com_estouro()
    {
        var r = RenderCorpus("estouro");
        var pdf = CaminhoSaida("estouro.pdf");
        var recibo = Exportador.CaminhoReciboDe(pdf);
        File.Delete(pdf);
        File.Delete(recibo);
        var ex = await Assert.ThrowsAsync<ExportacaoRecusadaException>(() => Exportador.ExportarAsync(r, Cfg("estouro"), pdf, CaminhoCorpus("estouro"), Modelo));
        saida.WriteLine(ex.Message);
        var e = Assert.Single(ex.Estouros);
        Assert.Equal(3, e.Folha);
        Assert.Equal("secao-2", e.Ref);
        Assert.Matches(@"Bloco .*secao-2.* excede a página 3", ex.Message);
        // Só folha, ref e medidas: nunca o texto do conteúdo (nem o título da seção).
        Assert.All(r.Refs.Where(x => x.Rotulo is { Length: > 3 }), x => Assert.DoesNotContain(x.Rotulo!, ex.Message));
        Assert.False(File.Exists(pdf));
        Assert.False(File.Exists(recibo));
    }

    [Fact]
    public async Task pdf_tem_o_mesmo_numero_de_folhas()
    {
        var resumo = new JsonArray();
        foreach (var caso in Corpus.Where(c => c != "estouro"))
        {
            var r = RenderCorpus(caso);
            await host.Documento.CarregarAsync(r, Cfg(caso));
            var c = await host.Documento.ComporDetalhadoAsync();

            var pdf = CaminhoSaida(caso + ".pdf");
            var x = await Exportador.ExportarAsync(r, Cfg(caso), pdf, CaminhoCorpus(caso), Modelo);
            var bytes = await File.ReadAllBytesAsync(pdf);
            var info = InfoPdf.Ler(bytes);
            var (paginasWin, larguraWin, alturaWin) = await LerComWindows(pdf);
            saida.WriteLine($"{caso}: folhas {c.Folhas}, exportadas {x.Folhas}, páginas {info.Paginas} (Windows.Data.Pdf {paginasWin}); " +
                $"página {info.LarguraMm:0.###} × {info.AlturaMm:0.###} mm (Windows.Data.Pdf {larguraWin:0.###} × {alturaWin:0.###})");
            Assert.Equal(c.Folhas, x.Folhas);
            Assert.Equal(x.Folhas, info.Paginas);
            Assert.Equal((uint)x.Folhas, paginasWin);
            Assert.Equal(c.Plano.ParaJson(), x.Plano.ParaJson()); // o host novo compõe igual
            Assert.InRange(info.LarguraMm, 209.5, 210.5);
            Assert.InRange(info.AlturaMm, 296.5, 297.5);
            Assert.InRange(larguraWin, 209.5, 210.5);
            Assert.InRange(alturaWin, 296.5, 297.5);
            Assert.Equal(info.LarguraMm, x.LarguraPaginaMm);
            Assert.Equal(info.AlturaMm, x.AlturaPaginaMm);
            resumo.Add(new JsonObject
            {
                ["caso"] = caso, ["folhas"] = c.Folhas, ["paginas"] = info.Paginas, ["paginasWindows"] = paginasWin,
                ["larguraMm"] = Math.Round(info.LarguraMm, 3), ["alturaMm"] = Math.Round(info.AlturaMm, 3), ["escala"] = c.Plano.Escala,
                ["bytes"] = bytes.Length,
            });
        }
        File.WriteAllText(CaminhoSaida("exportacao-corpus.json"), resumo.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public async Task recibo_gravado_com_hashes()
    {
        var r = RenderCorpus("curto");
        var pdf = CaminhoSaida("recibo-curto.pdf");
        var x = await Exportador.ExportarAsync(r, Cfg("curto"), pdf, CaminhoCorpus("curto"), Modelo);
        Assert.Equal(Exportador.CaminhoReciboDe(pdf), x.CaminhoRecibo);
        Assert.Equal(Path.Combine(Path.GetDirectoryName(pdf)!, "recibo-curto.recibo-editor.json"), x.CaminhoRecibo);
        using var doc = JsonDocument.Parse(await File.ReadAllBytesAsync(x.CaminhoRecibo));
        var j = doc.RootElement;
        Assert.Equal(new[] { "recibo_versao", "modelo_revisao", "sha256_modelo", "sha256_dados", "sha256_html", "plano_versao", "webview2",
            "sha256_pdf", "folhas", "gerado_em", "pagina_mm" }, j.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, j.GetProperty("recibo_versao").GetInt32());
        Assert.Equal(10, j.GetProperty("modelo_revisao").GetInt32());
        var modelo = j.GetProperty("sha256_modelo").Deserialize<Dictionary<string, string>>()!;
        Assert.Equal(Modelo.Sha256.OrderBy(p => p.Key, StringComparer.Ordinal), modelo.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(Sha(File.ReadAllBytes(CaminhoCorpus("curto"))), j.GetProperty("sha256_dados").GetString());
        Assert.Equal(PlanoQuebras.Sha256De(r.Html), j.GetProperty("sha256_html").GetString());
        Assert.Equal(PlanoQuebras.VersaoAtual, j.GetProperty("plano_versao").GetInt32());
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+", j.GetProperty("webview2").GetString());
        var bytes = File.ReadAllBytes(pdf);
        Assert.Equal(Sha(bytes), j.GetProperty("sha256_pdf").GetString());
        Assert.Equal(x.Sha256Pdf, j.GetProperty("sha256_pdf").GetString());
        Assert.Equal(InfoPdf.Ler(bytes).Paginas, j.GetProperty("folhas").GetInt32());
        Assert.Equal(x.Folhas, j.GetProperty("folhas").GetInt32());
        var gerado = DateTimeOffset.Parse(j.GetProperty("gerado_em").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Assert.InRange(gerado, DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.InRange(j.GetProperty("pagina_mm").GetProperty("largura").GetDouble(), 209.5, 210.5);
        Assert.InRange(j.GetProperty("pagina_mm").GetProperty("altura").GetDouble(), 296.5, 297.5);
    }

    [Fact]
    public void leitor_de_pdf_conta_paginas_e_le_o_tamanho()
    {
        // PDF mínimo à mão: 2 páginas A4 (595,28 × 841,89 pt) herdando o MediaBox do nó /Pages.
        var texto = "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n"
            + "2 0 obj << /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 /MediaBox [0 0 595.28 841.89] >> endobj\n"
            + "3 0 obj << /Type /Page /Parent 2 0 R >> endobj\n4 0 obj << /Type/Page /Parent 2 0 R >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF";
        var info = InfoPdf.Ler(System.Text.Encoding.Latin1.GetBytes(texto));
        Assert.Equal(2, info.Paginas);
        Assert.InRange(info.LarguraMm, 209.99, 210.01);
        Assert.InRange(info.AlturaMm, 296.99, 297.01);
        Assert.Throws<InvalidDataException>(() => InfoPdf.Ler(System.Text.Encoding.Latin1.GetBytes(texto.Replace("/Count 2", "/Count 3"))));
        Assert.Throws<InvalidDataException>(() => InfoPdf.Ler(System.Text.Encoding.Latin1.GetBytes("%PDF-1.5\n1 0 obj << /Type /ObjStm /N 3 >> endobj")));
        Assert.Throws<InvalidDataException>(() => InfoPdf.Ler([1, 2, 3]));
    }

    // ---------- apoio ----------

    static ConfigFolhas Cfg(string caso) => ConfigFolhas.PadraoDeDados(Modelo, CaminhoCorpus(caso));

    static string CaminhoCorpus(string caso) => Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus", caso + ".json");

    static string CaminhoSaida(string nome)
    {
        var pasta = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "exportacao");
        Directory.CreateDirectory(pasta);
        return Path.Combine(pasta, nome);
    }

    static ResultadoRender RenderCorpus(string caso)
    {
        var r = RelatorioRenderer.Renderizar(CaminhoCorpus(caso), new OpcoesRender(false, true), Modelo);
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        return r;
    }

    static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    static (int largura, int altura) TamanhoPng(byte[] png)
    {
        Assert.True(png.Length > 24 && png[1] == 'P' && png[2] == 'N' && png[3] == 'G', "não é PNG");
        return (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)),
            System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20)));
    }

    /// <summary>Leitura independente do PDF (Windows.Data.Pdf): páginas e tamanho da 1ª página (DIPs → mm).</summary>
    static async Task<(uint paginas, double larguraMm, double alturaMm)> LerComWindows(string caminho)
    {
        var arquivo = await Windows.Storage.StorageFile.GetFileFromPathAsync(caminho);
        var doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(arquivo);
        using var pagina = doc.GetPage(0);
        return (doc.PageCount, pagina.Size.Width * 25.4 / 96, pagina.Size.Height * 25.4 / 96);
    }

    static async Task<double> Js(HostOculto h, string expressao) => (await Avaliar(h, expressao)).GetDouble();
    static async Task<string> JsTexto(HostOculto h, string expressao) => (await Avaliar(h, expressao)).GetString()!;

    static async Task<JsonElement> Avaliar(HostOculto h, string expressao)
    {
        var r = await h.CdpAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = expressao, returnByValue = true, awaitPromise = true }));
        using var doc = JsonDocument.Parse(r);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out var ex)) throw new InvalidOperationException("JS: " + ex.GetRawText());
        return doc.RootElement.GetProperty("result").GetProperty("value").Clone();
    }
}
