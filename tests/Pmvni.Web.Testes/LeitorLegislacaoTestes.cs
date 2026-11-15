using System.Diagnostics;
using System.Text.Json;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Legislacao;
using Pmvni.Composicao.Web.Ponte;
using Pmvni.Legislacao;
using Pmvni.Legislacao.Testes.Ficticios;
using Xunit;

namespace Pmvni.Web.Testes;

/// <summary>O leitor da legislação (desenho §11.4) com o PDF.js 6.4.299 real num WebView2 com a sandbox do host
/// <c>leg.pmvni.invalid</c> (controller InPrivate sobre a janela oculta do <see cref="HostOculto"/>), sobre os PDFs fictícios.</summary>
public sealed class LeitorLegislacaoTestes : IAsyncLifetime
{
    const string Lei = "MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf";
    const string Lei2 = "MUNICIPAL/LEI 9.999-2099 - FICTICIA (VERSAO 2).pdf";
    const string Nt = "ESTADUAL - CBMES/NT 99-2099 - FICTICIA.pdf";
    const string Federal = "FEDERAL/LEI 99.999-2099 - FICTICIA.pdf";
    const string Digital = "OUTRAS/DIGITALIZADA - FICTICIA.pdf";
    const string Senha = "OUTRAS/SENHA - FICTICIA.pdf";
    const string Script = "OUTRAS/COM-SCRIPT - FICTICIA.pdf";

    HostOculto host = null!;
    LeitorPdfJs leitor = null!;
    ServidorLegislacao servidor = null!;
    string pasta = null!;
    readonly List<MensagemWeb> eventos = [];

    public async Task InitializeAsync()
    {
        pasta = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "legislacao-leitor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        host = await HostOculto.CriarAsync(800, 600);
        servidor = new ServidorLegislacao();
        leitor = await host.ExecutarAsync(core => LeitorPdfJs.CriarOcultoAsync(core.Environment, host.Janela, servidor));
        leitor.Evento += m => { lock (eventos) eventos.Add(m); };
    }

    public async Task DisposeAsync()
    {
        await leitor.DisposeAsync();
        await host.DisposeAsync();
        try { Directory.Delete(pasta, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    string Fixture(string relativo)
    {
        var caminho = Path.Combine(pasta, Path.GetFileName(relativo));
        if (!File.Exists(caminho)) File.WriteAllBytes(caminho, PdfLegislacaoFicticio.Todos[relativo]);
        return caminho;
    }

    async Task<JsonElement> Js(string expressao)
    {
        var r = await leitor.AvaliarAsync(expressao);
        return r.TryGetProperty("value", out var v) ? v : r;
    }

    static async Task<bool> Esperar(Func<Task<bool>> condicao, int ms = 10000)
    {
        var relogio = Stopwatch.StartNew();
        while (relogio.ElapsedMilliseconds < ms)
        {
            if (await condicao()) return true;
            await Task.Delay(50);
        }
        return false;
    }

    /// <summary>Espera a camada de texto da página <paramref name="pagina"/> ter texto (o PDF.js a desenha quando a página aparece).</summary>
    async Task EsperarTexto(int pagina)
    {
        await leitor.IrPaginaAsync(pagina);
        Assert.True(await Esperar(async () => (await Js($"document.querySelectorAll('.page[data-page-number=\"{pagina}\"] .textLayer span').length")).GetInt32() > 0),
            "a camada de texto da página " + pagina + " não apareceu");
    }

    /// <summary>Seleciona, por Range, de <paramref name="inicio"/> (na página pi) até o fim de <paramref name="fim"/> (na página pf).</summary>
    Task<JsonElement> Selecionar(int pi, string inicio, int pf, string fim) => Js($$"""
        (() => {
          const achar = (p, t) => {
            const spans = [...document.querySelectorAll('.page[data-page-number="' + p + '"] .textLayer span')];
            for (const s of spans) { const n = s.firstChild; if (n && n.nodeType === 3 && n.data.includes(t)) return [n, n.data.indexOf(t)]; }
            return null;
          };
          const a = achar({{pi}}, {{JsonSerializer.Serialize(inicio)}}), b = achar({{pf}}, {{JsonSerializer.Serialize(fim)}});
          if (!a || !b) return false;
          const r = document.createRange();
          r.setStart(a[0], a[1]);
          r.setEnd(b[0], b[1] + {{JsonSerializer.Serialize(fim)}}.length);
          const s = document.getSelection(); s.removeAllRanges(); s.addRange(r);
          return true;
        })()
        """);

    async Task Tecla(string key, string code, int vk, int modificadores)
    {
        await leitor.CdpAsync("Input.dispatchKeyEvent", new { type = "rawKeyDown", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores });
        await leitor.CdpAsync("Input.dispatchKeyEvent", new { type = "keyUp", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores });
    }

    List<MensagemWeb> Eventos(string tipo) { lock (eventos) return eventos.Where(e => e.Tipo == tipo).ToList(); }

    [Fact]
    public async Task abre_a_lei_com_paginas_e_marcadores()
    {
        var a = await leitor.AbrirAsync(Fixture(Lei));
        Assert.Equal(3, a.Paginas);
        Assert.Equal(["Art. 1º", "Art. 2º", "Art. 3º", "Anexo I"], a.Marcadores.Select(m => m.Titulo));
        Assert.All(a.Marcadores, m => Assert.False(m.Externo));
        Assert.Null(a.Procurado);
        Assert.True(a.Escala > 0);
        Assert.Equal(a.Escala, await leitor.ZoomAsync("largura"), 3); // a escala inicial é a de "page-width"
        Assert.Matches(@"^https://leg\.pmvni\.invalid/pdf/[0-9a-f]{32}\.pdf$", leitor.UrlAberta);
    }

    [Fact]
    public async Task navega_por_pagina_e_marcador()
    {
        await leitor.AbrirAsync(Fixture(Lei));
        Assert.Equal((3, 3), await leitor.IrPaginaAsync(3));
        Assert.Equal(2, (await leitor.IrMarcadorAsync([1])).Atual);
        Assert.Equal(3, (await leitor.IrMarcadorAsync([3])).Atual);
        Assert.Equal((3, 3), await leitor.IrPaginaAsync(99));
        Assert.Equal((1, 3), await leitor.IrPaginaAsync(-5));
        Assert.True(await Esperar(() => Task.FromResult(Eventos("paginaMudou").Count > 0)));
    }

    [Fact]
    public async Task escala_mais_menos_largura_e_pagina()
    {
        var a = await leitor.AbrirAsync(Fixture(Lei));
        var mais = await leitor.ZoomAsync("mais");
        Assert.True(mais > a.Escala);
        var menos = await leitor.ZoomAsync("menos");
        Assert.True(menos < mais);
        var largura = await leitor.ZoomAsync("largura");
        var pagina = await leitor.ZoomAsync("pagina");
        Assert.NotEqual(largura, pagina, 3); // A4 retrato: caber a página inteira não é o mesmo que caber a largura
        for (var i = 0; i < 40; i++) await leitor.ZoomAsync("mais");
        Assert.Equal(4, await leitor.ZoomAsync("mais"), 3);
        for (var i = 0; i < 40; i++) await leitor.ZoomAsync("menos");
        Assert.Equal(0.25, await leitor.ZoomAsync("menos"), 3);
    }

    [Fact]
    public async Task procura_sem_acento_nem_caixa_e_conta()
    {
        await leitor.AbrirAsync(Fixture(Lei));
        Assert.Equal(new ProcuraLei(3, 1, 1), await leitor.ProcurarAsync("AFASTAMENTO", 0));
        Assert.Equal(new ProcuraLei(3, 2, 2), await leitor.ProcurarAsync("AFASTAMENTO", 1));
        Assert.Equal(new ProcuraLei(3, 1, 1), await leitor.ProcurarAsync("AFASTAMENTO", -1));
        Assert.Equal(new ProcuraLei(3, 3, 3), await leitor.ProcurarAsync("AFASTAMENTO", -1));
        Assert.Equal(new ProcuraLei(3, 3, 3), await leitor.ProcurarAsync("AFASTAMENTO", 0)); // o mesmo termo de novo não anda
        Assert.Equal(new ProcuraLei(2, 1, 2), await leitor.ProcurarAsync("ocupacao", 0)); // termo novo digitado: da primeira página
        Assert.Equal(new ProcuraLei(0, 0, null), await leitor.ProcurarAsync("inexistentexyz", 0));
        Assert.Equal(new ProcuraLei(0, 0, null), await leitor.ProcurarAsync("", 0));
        Assert.True(await Esperar(async () => (await Js("document.querySelectorAll('.textLayer .highlight').length")).GetInt32() == 0, 3000));
        // Depois de limpar, o mesmo termo recomeça do começo da página atual.
        await leitor.IrPaginaAsync(1);
        Assert.Equal(new ProcuraLei(3, 1, 1), await leitor.ProcurarAsync("afastamento", 0));
    }

    [Fact]
    public async Task procura_a_partir_da_pagina_pedida()
    {
        var a = await leitor.AbrirAsync(Fixture(Lei), 3, "afastamento");
        Assert.Equal(new ProcuraLei(3, 3, 3), a.Procurado);
        Assert.Equal((3, 3), await leitor.IrPaginaAsync(3));
    }

    [Fact]
    public async Task selecao_com_texto_e_paginas()
    {
        await leitor.AbrirAsync(Fixture(Lei));
        await leitor.ZoomAsync("pagina");
        Assert.Equal(new SelecaoLei("", null, null), await leitor.ObterSelecaoAsync());
        await EsperarTexto(2);
        Assert.True((await Selecionar(2, "O afastamento frontal", 2, "3,00 m.")).GetBoolean());
        Assert.Equal(new SelecaoLei("O afastamento frontal mínimo é de 3,00 m.", 2, 2), await leitor.ObterSelecaoAsync());
        Assert.True(await Esperar(() => Task.FromResult(Eventos("selecaoMudou").Any(e => e.Dados.GetProperty("temSelecao").GetBoolean()))));
        await EsperarTexto(3);
        Assert.True((await Selecionar(2, "Art. 3º", 3, "ANEXO I")).GetBoolean());
        var s = await leitor.ObterSelecaoAsync();
        Assert.Equal((2, 3), (s.PaginaInicial, s.PaginaFinal));
        await Js("document.getSelection().removeAllRanges(), true");
        Assert.Equal("", (await leitor.ObterSelecaoAsync()).Texto);
    }

    [Fact]
    public async Task atalhos_da_lista_vao_ao_host_e_os_outros_nao()
    {
        await leitor.AbrirAsync(Fixture(Lei));
        await Js("document.getElementById('container').focus(), true");
        await Tecla("C", "KeyC", 67, 2 | 8);
        await Tecla("F3", "F3", 114, 0);
        await Tecla("ArrowDown", "ArrowDown", 40, 1);
        Assert.True(await Esperar(() => Task.FromResult(Eventos("atalho").Count >= 3)), "os atalhos não chegaram");
        Assert.Equal(["Ctrl+Shift+C", "F3", "Alt+ArrowDown"], Eventos("atalho").Select(e => e.Dados.GetProperty("tecla").GetString()));
        var antes = Eventos("atalho").Count;
        await Tecla("p", "KeyP", 80, 2);
        await Task.Delay(1000);
        Assert.Equal(antes, Eventos("atalho").Count);
        var zoom = Eventos("zoomMudou").Count;
        await leitor.CdpAsync("Input.dispatchMouseEvent", new { type = "mouseWheel", x = 300, y = 300, deltaX = 0, deltaY = -120, modifiers = 2 });
        Assert.True(await Esperar(() => Task.FromResult(Eventos("zoomMudou").Count > zoom)), "Ctrl+roda não mudou a escala");
    }

    [Fact]
    public async Task pdf_sem_texto_e_pdf_com_senha()
    {
        var a = await leitor.AbrirAsync(Fixture(Digital));
        Assert.Equal(1, a.Paginas);
        Assert.Empty(a.Marcadores);
        Assert.Equal(0, (await leitor.ProcurarAsync("x", 0)).Total);
        var e = await Assert.ThrowsAsync<ExtracaoRecusadaException>(() => leitor.AbrirAsync(Fixture(Senha)));
        Assert.Equal(FalhaExtracao.Senha, e.Falha);
        Assert.Null(leitor.UrlAberta); // o PDF recusado não fica registrado
    }

    [Fact]
    public async Task link_externo_nao_navega_e_script_nao_roda()
    {
        var a = await leitor.AbrirAsync(Fixture(Script));
        Assert.Equal(1, a.Paginas);
        Assert.True(await Esperar(async () => (await Js("document.querySelectorAll('.annotationLayer a').length")).GetInt32() > 0), "o link não foi desenhado");
        var link = await Js("(() => { const a = document.querySelector('.annotationLayer a'); a.scrollIntoView({block:'center'}); const r = a.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, href: a.getAttribute('href') ?? '', title: a.title }; })()");
        Assert.Equal("", link.GetProperty("href").GetString());
        Assert.Contains("https://exemplo.invalid/", link.GetProperty("title").GetString());
        var x = link.GetProperty("x").GetDouble();
        var y = link.GetProperty("y").GetDouble();
        await leitor.CdpAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", buttons = 1, clickCount = 1 });
        await leitor.CdpAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", buttons = 0, clickCount = 1 });
        await Task.Delay(1000);
        Assert.Equal(LeitorPdfJs.Pagina, (await Js("location.href")).GetString());
        Assert.Empty(leitor.RequisicoesBloqueadas); // nem navegação nem janela nova nem requisição para fora
        Assert.Equal(0, leitor.ViolacoesCsp);       // o fetch do script do PDF nunca é tentado
        Assert.All(leitor.RequisicoesVistas, u => Assert.StartsWith("https://leg.pmvni.invalid/", u));
    }

    [Fact]
    public async Task nada_fora_do_host_com_as_fixtures()
    {
        foreach (var f in new[] { Lei, Lei2, Nt, Federal, Digital })
        {
            var a = await leitor.AbrirAsync(Fixture(f));
            Assert.True(a.Paginas > 0);
            await leitor.IrPaginaAsync(a.Paginas);
        }
        Assert.Equal(5, (await leitor.AbrirAsync(Fixture(Lei2))).Marcadores.Count);
        await leitor.FecharAsync();
        Assert.Null(leitor.UrlAberta);
        Assert.Empty(leitor.RequisicoesBloqueadas);
        Assert.Equal(0, leitor.ViolacoesCsp);
        Assert.All(leitor.RequisicoesVistas, u => Assert.StartsWith("https://leg.pmvni.invalid/", u));
    }

    [Fact]
    public async Task trocar_de_pdf_tira_o_anterior_do_servidor()
    {
        await leitor.AbrirAsync(Fixture(Lei));
        var primeira = leitor.UrlAberta!;
        await leitor.AbrirAsync(Fixture(Nt));
        Assert.NotEqual(primeira, leitor.UrlAberta);
        Assert.Null(servidor.Responder("GET", primeira, null));
        Assert.NotNull(servidor.Responder("GET", leitor.UrlAberta!, null));
        var fetch = await Js($"fetch({JsonSerializer.Serialize(primeira)}).then(r => r.status)");
        Assert.Equal(403, fetch.GetInt32());
    }
}
