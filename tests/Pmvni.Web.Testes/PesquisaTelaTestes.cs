using System.Security.Cryptography;
using System.Text.Json;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Ponte;
using Pmvni.Revisao;
using Xunit;

namespace Pmvni.Web.Testes;

/// <summary>Tarefa 11 da Etapa 2: a pesquisa no relatório (realce pela CSS Custom Highlight API) não cria nada no DOM, não muda o
/// texto nem a medida das folhas e não chega ao PDF. WebView2 real; a "tela" é o host que recebe o plano aplicado.</summary>
public sealed class PesquisaTelaTestes : IClassFixture<TelaFixture>
{
    static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);
    readonly HostOculto comp, tela;

    public PesquisaTelaTestes(TelaFixture f)
    {
        comp = f.Composicao;
        tela = f.Tela;
    }

    /// <summary>Contador independente da pesquisa da página: texto de cada nó da área útil sem acento nem caixa (remove as marcas
    /// combinantes da decomposição NFD), ocorrências não sobrepostas, fora do que o paginador oculta.</summary>
    const string ContadorJs = """
        (termo) => {
          const sem = (s) => s.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase();
          const alvo = sem(termo);
          let total = 0;
          for (const area of document.querySelectorAll('section.folha > .area')) {
            const w = document.createTreeWalker(area, NodeFilter.SHOW_TEXT);
            for (let n = w.nextNode(); n; n = w.nextNode()) {
              if (n.parentElement.closest('[data-pmvni-oculto], style, script')) continue;
              total += sem(n.data).split(alvo).length - 1;
            }
          }
          return total;
        }
        """;

    [Fact]
    public async Task pesquisa_realca_sem_mudar_o_dom_e_navega()
    {
        using var a = await AbrirAsync("longo");
        var textoAntes = await tela.Documento.TextoPorFolhaAsync();
        var elementosAntes = (await Js("document.body.querySelectorAll('*').length")).GetInt32();
        var contados = (await Js($"({ContadorJs})('VERIFICAÇÃO')")).GetInt32();
        Assert.True(contados >= 60, "o contador independente achou só " + contados);

        // Sem acento e sem caixa: o mesmo total que o contador de "VERIFICAÇÃO".
        var r = await PesquisarAsync("verificacao", 0);
        Assert.Equal(contados, r.GetProperty("total").GetInt32());
        Assert.Equal(1, r.GetProperty("atual").GetInt32());
        Assert.Equal(contados, (await Js("CSS.highlights.get('pmvni-busca').size")).GetInt32());
        Assert.Equal(1, (await Js("CSS.highlights.get('pmvni-busca-atual').size")).GetInt32());
        Assert.Equal(contados, (await PesquisarAsync("VERIFICAÇÃO", 0)).GetProperty("total").GetInt32());

        // Nenhum elemento novo no body, nenhuma mudança de texto.
        Assert.Equal(elementosAntes, (await Js("document.body.querySelectorAll('*').length")).GetInt32());
        Assert.Equal(textoAntes, await tela.Documento.TextoPorFolhaAsync());
        Assert.Equal(0, (await Js("document.querySelectorAll('section.folha mark, section.folha [data-pmvni-busca]').length")).GetInt32());

        // Navega: 1 → 2 → ... → total → 1 (volta ao começo); -1 volta ao fim; cada passo rola até a ocorrência.
        await PesquisarAsync("verificacao", 0);
        var ids = a.Ids.ToHashSet();
        for (var esperado = 2; esperado <= contados; esperado++)
        {
            var p = await PesquisarAsync("verificacao", 1);
            Assert.Equal(esperado, p.GetProperty("atual").GetInt32());
            if (esperado <= 4 || esperado == contados)
            {
                Assert.Contains(p.GetProperty("ref").GetString()!, ids);
                Assert.True(await OcorrenciaAtualVisivelAsync(), $"a ocorrência {esperado} deve estar à vista depois do passo");
            }
        }
        var volta = await PesquisarAsync("verificacao", 1);
        Assert.Equal(1, volta.GetProperty("atual").GetInt32());
        Assert.True(await OcorrenciaAtualVisivelAsync());
        var anterior = await PesquisarAsync("verificacao", -1);
        Assert.Equal(contados, anterior.GetProperty("atual").GetInt32());
        Assert.Equal(contados, anterior.GetProperty("total").GetInt32());
        // Passo 0 com o mesmo termo mantém a ocorrência atual.
        Assert.Equal(contados, (await PesquisarAsync("verificacao", 0)).GetProperty("atual").GetInt32());
        Assert.Equal(elementosAntes, (await Js("document.body.querySelectorAll('*').length")).GetInt32());
        Assert.Equal(textoAntes, await tela.Documento.TextoPorFolhaAsync());

        // O termo do título do ITEM 10 acha o item certo.
        var titulo = Titulo(a.Pacote.Projeto.Itens[9]);
        var dez = await PesquisarAsync(titulo, 0);
        Assert.True(dez.GetProperty("total").GetInt32() >= 1);
        Assert.Equal(a.Ids[9], dez.GetProperty("ref").GetString());

        // Sem ocorrência: total 0, atual 0, ref nulo e nada realçado; termo vazio limpa tudo.
        var nenhuma = await PesquisarAsync("zzzzqxj", 0);
        Assert.Equal(0, nenhuma.GetProperty("total").GetInt32());
        Assert.Equal(0, nenhuma.GetProperty("atual").GetInt32());
        Assert.Equal(JsonValueKind.Null, nenhuma.GetProperty("ref").ValueKind);
        var limpa = await PesquisarAsync("", 0);
        Assert.Equal(0, limpa.GetProperty("total").GetInt32());
        Assert.Equal(0, (await Js("CSS.highlights.size")).GetInt32());
        Assert.Equal(elementosAntes, (await Js("document.body.querySelectorAll('*').length")).GetInt32());
        Assert.Equal(textoAntes, await tela.Documento.TextoPorFolhaAsync());
        Assert.Empty(tela.Documento.Descartadas);
    }

    [Fact]
    public async Task pesquisa_refeita_depois_de_recompor_mantem_a_ocorrencia_sem_rolar()
    {
        using var a = await AbrirAsync("longo");
        await PesquisarAsync("verificacao", 0);
        await PesquisarAsync("verificacao", 1);
        var tres = await PesquisarAsync("verificacao", 1);
        Assert.Equal(3, tres.GetProperty("atual").GetInt32());
        var total = tres.GetProperty("total").GetInt32();

        // Recompor troca o documento (o estado da página recomeça) e o host reancora a rolagem; a pesquisa é refeita com a
        // ocorrência atual (3) e não rola: quem manda na rolagem é a âncora do host.
        await tela.Documento.CarregarAsync(a.E.Render, a.E.Config);
        await tela.Documento.AplicarPlanoAsync(a.C.Plano);
        await tela.Documento.ChamarAsync("definirZoom", new { zoom = 1.0 }, Limite);
        await Js("window.scrollTo(0, 1500); 0");
        var yAntes = (await Js("window.scrollY")).GetDouble();
        Assert.Equal(0, (await Js("CSS.highlights.size")).GetInt32());

        var r = await tela.Documento.ChamarAsync("pesquisar", new { termo = "verificacao", passo = 0, atual = 3 }, Limite);

        Assert.Equal(total, r.GetProperty("total").GetInt32());
        Assert.Equal(3, r.GetProperty("atual").GetInt32());
        Assert.Equal(yAntes, (await Js("window.scrollY")).GetDouble());
        Assert.Equal(total, (await Js("CSS.highlights.get('pmvni-busca').size")).GetInt32());
        Assert.Equal(1, (await Js("CSS.highlights.get('pmvni-busca-atual').size")).GetInt32());

        // Uma ocorrência além do total fica na última; "atual" só vale com passo 0 e a partir de 1.
        var alem = await tela.Documento.ChamarAsync("pesquisar", new { termo = "verificacao", passo = 0, atual = total + 50 }, Limite);
        Assert.Equal(total, alem.GetProperty("atual").GetInt32());
        var ex = await Assert.ThrowsAsync<ErroWebException>(() => tela.Documento.ChamarAsync("pesquisar", new { termo = "verificacao", passo = 1, atual = 3 }, Limite));
        Assert.Equal("pesquisa_invalida", ex.Codigo);
        ex = await Assert.ThrowsAsync<ErroWebException>(() => tela.Documento.ChamarAsync("pesquisar", new { termo = "verificacao", passo = 0, atual = 0 }, Limite));
        Assert.Equal("pesquisa_invalida", ex.Codigo);
        await PesquisarAsync("", 0);
    }

    [Fact]
    public async Task pesquisa_invalida_responde_erro()
    {
        using var a = await AbrirAsync("curto");
        var ex = await Assert.ThrowsAsync<ErroWebException>(() => tela.Documento.ChamarAsync("pesquisar", new { termo = 3, passo = 0 }, Limite));
        Assert.Equal("pesquisa_invalida", ex.Codigo);
        ex = await Assert.ThrowsAsync<ErroWebException>(() => tela.Documento.ChamarAsync("pesquisar", new { termo = "a", passo = 2 }, Limite));
        Assert.Equal("pesquisa_invalida", ex.Codigo);
    }

    [Fact]
    public async Task pesquisa_nao_entra_no_pdf()
    {
        using var a = await AbrirAsync("longo");
        var pdfSem = await tela.Documento.ImprimirPdfAsync();
        var r = await PesquisarAsync("verificacao", 0);
        Assert.True(r.GetProperty("total").GetInt32() > 0);
        Assert.Equal(1, (await Js("CSS.highlights.get('pmvni-busca-atual').size")).GetInt32());
        var pdfCom = await tela.Documento.ImprimirPdfAsync();

        var iA = InfoPdf.Ler(pdfSem);
        var iB = InfoPdf.Ler(pdfCom);
        Assert.Equal(a.C.Folhas, iA.Paginas);
        Assert.Equal(iA.Paginas, iB.Paginas);
        Assert.Equal(await comp.Documento.TextoPorFolhaAsync(), await tela.Documento.TextoPorFolhaAsync());

        // Página a página, os pixels são os mesmos: o realce só vale em @media screen.
        var dir = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "pesquisa");
        Directory.CreateDirectory(dir);
        var pa = Path.Combine(dir, "sem.pdf");
        var pb = Path.Combine(dir, "com.pdf");
        await File.WriteAllBytesAsync(pa, pdfSem);
        await File.WriteAllBytesAsync(pb, pdfCom);
        using var entradaA = File.OpenRead(pa).AsRandomAccessStream();
        var da = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(entradaA);
        using var entradaB = File.OpenRead(pb).AsRandomAccessStream();
        var db = await Windows.Data.Pdf.PdfDocument.LoadFromStreamAsync(entradaB);
        Assert.Equal(da.PageCount, db.PageCount);
        for (uint i = 0; i < da.PageCount; i++) Assert.Equal(await PixelsAsync(da, i), await PixelsAsync(db, i));

        // E o realce aparece de fato na tela (cor de fundo do ::highlight só em mídia de tela): a regra existe e é de tela.
        var css = (await Js("document.getElementById('pmvni-pesquisa-css').textContent")).GetString()!;
        Assert.StartsWith("\n@media screen", css);
        Assert.DoesNotContain("@media print", css);
        await PesquisarAsync("", 0);
    }

    // ---------- apoio ----------

    sealed record Aberto(ConteudoPacote Pacote, Recompositor Rec, Preparado E, ResultadoComposicao C, IReadOnlyList<string> Ids) : IDisposable
    {
        public void Dispose() => Rec.Dispose();
    }

    async Task<Aberto> AbrirAsync(string caso)
    {
        var caminho = Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus", caso + ".json");
        var contexto = new ContextoUsuario("teste", () => new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero), RandomNumberGenerator.Create());
        var pacote = Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, contexto);
        var rec = new Recompositor(pacote, Modelo);
        try
        {
            var e = rec.Preparar(pacote.Projeto);
            var c = await rec.ComporAsync(e, comp);
            await tela.Documento.CarregarAsync(e.Render, e.Config);
            await tela.Documento.AplicarPlanoAsync(c.Plano);
            await tela.Documento.ChamarAsync("definirZoom", new { zoom = 1.0 }, Limite);
            await Js("window.scrollTo(0, 0); 0");
            return new Aberto(pacote, rec, e, c, pacote.Projeto.Itens.Select(i => i.Id).ToList());
        }
        catch
        {
            rec.Dispose();
            throw;
        }
    }

    Task<JsonElement> PesquisarAsync(string termo, int passo) => tela.Documento.ChamarAsync("pesquisar", new { termo, passo }, Limite);

    /// <summary>A ocorrência atual (o único intervalo de pmvni-busca-atual) está dentro da janela.</summary>
    async Task<bool> OcorrenciaAtualVisivelAsync() => (await Js("""
        (() => {
          const r = [...CSS.highlights.get('pmvni-busca-atual')][0].getBoundingClientRect();
          return r.height > 0 && r.top >= 0 && r.bottom <= window.innerHeight;
        })()
        """)).GetBoolean();

    static string Json(object o) => JsonSerializer.Serialize(o);

    async Task<JsonElement> Js(string expressao)
    {
        var r = await tela.CdpAsync("Runtime.evaluate", Json(new { expression = expressao, returnByValue = true, awaitPromise = true }));
        using var doc = JsonDocument.Parse(r);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out var ex)) throw new InvalidOperationException("JS: " + ex.GetRawText());
        var resultado = doc.RootElement.GetProperty("result");
        return resultado.TryGetProperty("value", out var v) ? v.Clone() : default;
    }

    static async Task<string> PixelsAsync(Windows.Data.Pdf.PdfDocument doc, uint i)
    {
        using var pagina = doc.GetPage(i);
        using var fluxo = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await pagina.RenderToStreamAsync(fluxo);
        fluxo.Seek(0);
        var dec = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(fluxo);
        var px = await dec.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            new Windows.Graphics.Imaging.BitmapTransform(), Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        return dec.PixelWidth + "x" + dec.PixelHeight + ":" + Convert.ToHexString(SHA256.HashData(px.DetachPixelData()));
    }

    static string Titulo(ItemRevisao it) => ((System.Text.Json.Nodes.JsonValue)it.Campos["titulo"].Original!).GetValue<string>();
}
