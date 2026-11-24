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
    public async Task exportacao_final_recusada_com_campo_entre_colchetes()
    {
        var origem = CaminhoCorpus("curto");
        var temp = Path.Combine(Path.GetDirectoryName(origem)!, "tmp-colchetes-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var pdf = CaminhoSaida("colchetes.pdf");
        File.Delete(pdf); File.Delete(Exportador.CaminhoReciboDe(pdf));
        try
        {
            var no = JsonNode.Parse(File.ReadAllText(origem))!;
            no["capa"]!["requerente"] = "[Nome do requerente]";
            File.WriteAllText(temp, no.ToJsonString());
            // Em rascunho (a visão da tela) o relatório abre; na exportação final a conferência do kit recusa.
            Assert.True(RelatorioRenderer.Renderizar(temp, new OpcoesRender(false, true), Modelo).Ok);
            Assert.False(RelatorioRenderer.Renderizar(temp, new OpcoesRender(false, false), Modelo).Ok);
            var planoTemp = await PlanoDaTela(RelatorioRenderer.Renderizar(temp, new OpcoesRender(false, true), Modelo), ConfigFolhas.PadraoDeDados(Modelo, temp));
            var ex = await Assert.ThrowsAsync<ConferenciaFinalException>(() => Exportador.ExportarFinalAsync(temp, ConfigFolhas.PadraoDeDados(Modelo, temp), pdf, planoTemp, Modelo));
            Assert.Contains(ex.Erros, e => e.Contains("colchetes") && e.Contains("[Nome do requerente]"));
            Assert.False(File.Exists(pdf));
            Assert.False(File.Exists(Exportador.CaminhoReciboDe(pdf)));
            // Sem colchetes, a mesma rota exporta; o recibo diz que não é rascunho e leva o sha256 dos bytes renderizados.
            var ok = await Exportador.ExportarFinalAsync(origem, Cfg("curto"), pdf, await PlanoDaTela(RenderCorpus("curto"), Cfg("curto")), Modelo);
            Assert.True(File.Exists(ok.CaminhoPdf));
            using var recibo = JsonDocument.Parse(await File.ReadAllBytesAsync(ok.CaminhoRecibo));
            Assert.False(recibo.RootElement.GetProperty("rascunho").GetBoolean());
            Assert.Equal(Sha(File.ReadAllBytes(origem)), recibo.RootElement.GetProperty("sha256_dados").GetString());
        }
        finally { File.Delete(temp); }
    }

    /// <summary>D2: o PDF é a impressão das folhas que a tela mostra. Se o dados.json mudar depois de aberto, o HTML final
    /// não é o da tela e a exportação recusa, sem gravar PDF nem recibo; com outro plano (inícios ou escala), também.</summary>
    [Fact]
    public async Task exportacao_final_recusada_se_o_arquivo_mudou_depois_de_aberto()
    {
        var origem = CaminhoCorpus("curto");
        var temp = Path.Combine(Path.GetDirectoryName(origem)!, "tmp-muda-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        var pdf = CaminhoSaida("muda.pdf");
        var recibo = Exportador.CaminhoReciboDe(pdf);
        File.Delete(pdf); File.Delete(recibo);
        try
        {
            File.Copy(origem, temp);
            var cfg = ConfigFolhas.PadraoDeDados(Modelo, temp);
            var tela = RelatorioRenderer.Renderizar(temp, new OpcoesRender(false, true), Modelo); // o que o app abre (rascunho)
            var plano = await PlanoDaTela(tela, cfg);

            var no = JsonNode.Parse(File.ReadAllText(temp))!;
            no["capa"]!["requerente"] = "REQUERENTE ALTERADO DEPOIS DE ABRIR";
            File.WriteAllText(temp, no.ToJsonString());
            var ex = await Assert.ThrowsAsync<ExportacaoDivergenteException>(() => Exportador.ExportarFinalAsync(temp, cfg, pdf, plano, Modelo));
            Assert.Equal("O arquivo mudou desde que foi aberto; reabra antes de exportar.", ex.Message);
            Assert.False(File.Exists(pdf));
            Assert.False(File.Exists(recibo));

            // Arquivo de volta como foi aberto, mas plano da tela diferente do que a exportação compõe: recusado também.
            File.Copy(origem, temp, overwrite: true);
            foreach (var outro in new[] { plano with { Escala = 1.2 }, plano with { InicioDasFolhas = plano.InicioDasFolhas.Take(1).ToArray() } })
            {
                var dif = await Assert.ThrowsAsync<ExportacaoDivergenteException>(() => Exportador.ExportarFinalAsync(temp, cfg, pdf, outro, Modelo));
                Assert.Equal(ExportacaoDivergenteException.ComposicaoDiferente, dif.Message);
                Assert.False(File.Exists(pdf));
                Assert.False(File.Exists(recibo));
            }
            // E com o plano certo, exporta.
            Assert.True(File.Exists((await Exportador.ExportarFinalAsync(temp, cfg, pdf, plano, Modelo)).CaminhoPdf));
        }
        finally { File.Delete(temp); File.Delete(pdf); File.Delete(recibo); }
    }

    /// <summary>Conteúdo além da borda lateral da área (nada cortado): aplicarPlano informa as folhas, só informativo.</summary>
    [Fact]
    public async Task conteudo_alem_da_borda_da_area_e_informado()
    {
        var r = Ambiente.Resultado("<p>curto</p><p style=\"white-space:nowrap\">" + string.Concat(Enumerable.Repeat("PALAVRA ", 60)) + "</p>");
        await host.Documento.CarregarAsync(r, Ambiente.Cfg);
        var c = await host.Documento.ComporDetalhadoAsync();
        var a = await host.Documento.AplicarPlanoAsync(c.Plano);
        Assert.Equal(new[] { 1 }, a.FolhasQueTransbordamNaLargura);
        Assert.Equal(new[] { 1 }, a.FolhasMarcadas);
        await host.Documento.CarregarAsync(Ambiente.Resultado("<p>cabe na área</p>"), Ambiente.Cfg);
        var b = await host.Documento.AplicarPlanoAsync(await host.Documento.ComporAsync());
        Assert.Empty(b.FolhasQueTransbordamNaLargura);
        foreach (var caso in new[] { "curto", "longo", "extremos" })
        {
            var rc = RenderCorpus(caso);
            await host.Documento.CarregarAsync(rc, Cfg(caso));
            var x = await host.Documento.AplicarPlanoAsync(await host.Documento.ComporAsync());
            saida.WriteLine($"{caso}: além da borda lateral nas folhas [{string.Join(",", x.FolhasQueTransbordamNaLargura)}]");
            if (caso == "curto") Assert.Empty(x.FolhasQueTransbordamNaLargura);
            else Assert.NotEmpty(x.FolhasQueTransbordamNaLargura); // a etiqueta INFORMAÇÕES DIVERGENTES (nowrap) passa da borda
        }
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

    /// <summary>Tarefa 9 (achado da V2/V3 em <c>longo</c>): com k &gt; 1 a área é diagramada com 254k mm de altura e pintada
    /// em 1/k. Na impressão, o Chromium fragmentava o conteúdo da área pelo layout NÃO escalado (28 + 254k mm passa da folha)
    /// e empurrava o bloco indivisível do pé da folha (a PROVIDÊNCIA do ITEM 05 na folha 2) para a página seguinte, onde ele
    /// era recortado: sumia do PDF, embora estivesse na tela. Corrigido com <c>contain: size</c> na área das folhas (caixa
    /// monolítica: o conteúdo dela não se fragmenta). Conferência: as linhas (y) com tinta na área útil de cada folha da tela
    /// também têm tinta na página do PDF (±2 px da captura de 96 dpi).</summary>
    [Theory]
    [InlineData("longo")]
    [InlineData("extremos")]
    public async Task pdf_com_escala_mostra_as_mesmas_linhas_que_a_tela(string caso)
    {
        var r = RenderCorpus(caso);
        var pdf = CaminhoSaida("linhas-" + caso + ".pdf");
        var x = await Exportador.ExportarAsync(r, Cfg(caso), pdf, CaminhoCorpus(caso), Modelo);
        Assert.True(x.Plano.Escala > 1);

        await using var tela = await HostOculto.CriarAsync(1024, 1400);
        await tela.CdpAsync("Emulation.setDeviceMetricsOverride", """{"width":0,"height":0,"deviceScaleFactor":1,"mobile":false}""");
        await tela.Documento.CarregarAsync(r, Cfg(caso));
        await tela.Documento.AplicarPlanoAsync(x.Plano);

        var arquivo = await Windows.Storage.StorageFile.GetFileFromPathAsync(pdf);
        var doc = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(arquivo);
        Assert.Equal((uint)x.Folhas, doc.PageCount);
        var falhas = new List<string>();
        for (var i = 1; i <= x.Folhas; i++)
        {
            var (lt, at, pt) = await Decodificar(new MemoryStream(await tela.Documento.CapturarFolhaAsync(i, 96)).AsRandomAccessStream());
            using var pagina = doc.GetPage((uint)(i - 1));
            using var fluxo = new Windows.Storage.Streams.InMemoryRandomAccessStream();
            await pagina.RenderToStreamAsync(fluxo); // tamanho do Windows.Data.Pdf (DIPs × escala do monitor)
            var (lp, ap, pp) = await Decodificar(fluxo);
            Assert.Equal(lt / (double)at, lp / (double)ap, 2);
            int[] Faixa(int altura, int largura) => [(int)(28 * altura / 297.0), (int)(282 * altura / 297.0), (int)(14 * largura / 210.0), (int)(196 * largura / 210.0)];
            var ft = Faixa(at, lt);
            var fp = Faixa(ap, lp);
            var linhasTela = LinhasComTinta(pt, lt, ft[0], ft[1], ft[2], ft[3]);
            // linhas do PDF levadas para a escala da captura (mm iguais)
            var linhasPdf = LinhasComTinta(pp, lp, fp[0], fp[1], fp[2], fp[3]).Select(y => (int)Math.Round(y * at / (double)ap)).ToHashSet();
            var faltam = linhasTela.Count(y => !Enumerable.Range(y - 2, 5).Any(linhasPdf.Contains));
            if (faltam > 0) falhas.Add($"folha {i}: {faltam} de {linhasTela.Count} linhas com tinta na tela sem tinta no PDF");
        }
        Assert.True(falhas.Count == 0, caso + ": " + string.Join("; ", falhas));
    }

    static async Task<(int Largura, int Altura, byte[] Bgra)> Decodificar(Windows.Storage.Streams.IRandomAccessStream fluxo)
    {
        fluxo.Seek(0);
        var dec = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(fluxo);
        var px = await dec.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            new Windows.Graphics.Imaging.BitmapTransform(), Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        return ((int)dec.PixelWidth, (int)dec.PixelHeight, px.DetachPixelData());
    }

    /// <summary>Linhas y (em [y0; y1)) com algum pixel escuro (cinza &lt; 100: texto; não as barras coloridas dos cartões nem
    /// os fundos claros) entre x0 e x1.</summary>
    static HashSet<int> LinhasComTinta(byte[] bgra, int largura, int y0, int y1, int x0, int x1)
    {
        var linhas = new HashSet<int>();
        for (var y = y0; y < y1; y++)
            for (var c = x0; c < x1; c++)
            {
                var o = (y * largura + c) * 4;
                if (0.114 * bgra[o] + 0.587 * bgra[o + 1] + 0.299 * bgra[o + 2] < 100) { linhas.Add(y); break; }
            }
        return linhas;
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
        Assert.Equal(new[] { "recibo_versao", "modelo_revisao", "sha256_modelo", "sha256_dados", "sha256_html", "rascunho", "plano_versao", "webview2",
            "sha256_pdf", "folhas", "gerado_em", "pagina_mm" }, j.EnumerateObject().Select(p => p.Name));
        Assert.True(j.GetProperty("rascunho").GetBoolean()); // ExportarAsync sem dizer o contrário: rascunho
        Assert.Equal(1, j.GetProperty("recibo_versao").GetInt32());
        Assert.Equal(13, j.GetProperty("modelo_revisao").GetInt32());
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

    /// <summary>Plano composto no host oculto, como o app faz ao abrir (o que a tela mostra).</summary>
    async Task<PlanoQuebras> PlanoDaTela(ResultadoRender r, ConfigFolhas cfg)
    {
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        await host.Documento.CarregarAsync(r, cfg);
        return await host.Documento.ComporAsync();
    }

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
