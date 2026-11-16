using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Legislacao;
using Pmvni.Legislacao;
using Pmvni.Legislacao.Testes.Ficticios;
using Xunit;

namespace Pmvni.Web.Testes;

/// <summary>Extração de texto com o PDF.js 6.4.299 real, num WebView2 oculto (controller InPrivate sobre a janela oculta do
/// <see cref="HostOculto"/>), com PDFs fictícios gerados no próprio teste. Os dados do WebView2 vão para tests\saida\dados.</summary>
public sealed class ExtratorPdfJsTestes : IAsyncLifetime
{
    const string Lei = "MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf";
    const string Pagina = "https://leg.pmvni.invalid/extrator.html";

    HostOculto host = null!;
    ExtratorPdfJs extrator = null!;
    ServidorLegislacao servidor = null!;
    string pasta = null!;

    public async Task InitializeAsync()
    {
        pasta = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "legislacao-web", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(pasta);
        host = await HostOculto.CriarAsync(800, 600);
        servidor = new ServidorLegislacao();
        extrator = await host.ExecutarAsync(core => ExtratorPdfJs.CriarAsync(core.Environment, host.Janela, servidor));
    }

    public async Task DisposeAsync()
    {
        await extrator.DisposeAsync();
        await host.DisposeAsync();
        try { Directory.Delete(pasta, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    string Gravar(string nome, byte[] bytes)
    {
        var caminho = Path.Combine(pasta, nome);
        File.WriteAllBytes(caminho, bytes);
        return caminho;
    }

    string Fixture(string relativo) => Gravar(Path.GetFileName(relativo), PdfLegislacaoFicticio.Todos[relativo]);

    sealed class ProgressoSincrono(Action<(int Pagina, int Total)> acao) : IProgress<(int Pagina, int Total)>
    {
        public void Report((int Pagina, int Total) valor) => acao(valor);
    }

    static string Colapsar(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    static async Task Esperar(Func<bool> condicao, int milissegundos = 5000)
    {
        var relogio = Stopwatch.StartNew();
        while (!condicao() && relogio.ElapsedMilliseconds < milissegundos) await Task.Delay(25);
    }

    [Fact]
    public async Task extrai_paginas_e_texto_da_lei_ficticia()
    {
        var progresso = new List<(int, int)>();
        var r = await extrator.ExtrairAsync(Fixture(Lei), new ProgressoSincrono(progresso.Add), CancellationToken.None);

        Assert.Equal(3, r.Paginas);
        Assert.Empty(r.PaginasSemTexto);
        var esperado = PdfLegislacaoFicticio.Textos[Lei].Select(linhas => string.Join(" ", linhas)).ToArray();
        Assert.Equal(esperado, r.TextoPorPagina.Select(Colapsar));
        Assert.Equal("Art. 2º O afastamento frontal mínimo é de 3,00 m. Art. 3º A taxa de ocupação máxima é de 60%.", Colapsar(r.TextoPorPagina[1]));
        Assert.Equal([(1, 3), (2, 3), (3, 3)], progresso);
        Assert.Empty(extrator.RequisicoesBloqueadas);
        Assert.Equal(0, extrator.ViolacoesCsp);
    }

    [Fact]
    public async Task extrai_texto_com_quebra_de_linha_entre_as_linhas()
    {
        var r = await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None);
        Assert.Contains("\n", r.TextoPorPagina[0]); // o fim de linha do PDF.js vira \n
        Assert.DoesNotContain("frontal das edificações.Art", r.TextoPorPagina[0]);
    }

    [Fact]
    public async Task pdf_da_nt_e_o_federal_tambem_extraem()
    {
        var nt = await extrator.ExtrairAsync(Fixture("ESTADUAL - CBMES/NT 99-2099 - FICTICIA.pdf"), null, CancellationToken.None);
        Assert.Equal(2, nt.Paginas);
        Assert.Equal("5.1 O afastamento entre edificações fictícias é de 4,00 m.", Colapsar(nt.TextoPorPagina[1]));
        var federal = await extrator.ExtrairAsync(Fixture("FEDERAL/LEI 99.999-2099 - FICTICIA.pdf"), null, CancellationToken.None);
        Assert.Equal(1, federal.Paginas);
        Assert.Contains("parcelamento do solo", federal.TextoPorPagina[0]);
    }

    // ---- Figuras da leitura padronizada (correções 2026-11-15) ----

    [Fact]
    public async Task figuras_imagem_imagem_embutida_desenho_e_pagina_inteira_aparecem_no_ponto_certo()
    {
        var caminho = Gravar("COM-FIGURAS - FICTICIA.pdf", PdfLegislacaoFicticio.GerarComFiguras());
        var texto = await extrator.ExtrairAsync(caminho, null, CancellationToken.None);
        var figuras = await extrator.ExtrairFigurasAsync(caminho, CancellationToken.None);

        Assert.All(figuras, f => Assert.True(f.Dados.Length > 0 && FiguraPdf.AssinaturaConfere(f.Tipo, f.Dados), f.Origem));
        // Página 1: a imagem (objeto) entre a 2ª e a 3ª linha.
        var p1 = Assert.Single(figuras, f => f.Pagina == 1);
        Assert.Equal(OrigemFigura.Imagem, p1.Origem);
        Assert.Equal(2, p1.Linha);
        Assert.InRange(p1.Largura, 199, 206);
        Assert.InRange(p1.Y, (842 - 734.0) / 842 - 0.01, (842 - 730.0) / 842 + 0.01);
        // Página 2: o desenho (de cima) e a imagem embutida, os dois antes da última linha.
        var p2 = figuras.Where(f => f.Pagina == 2).ToList();
        Assert.Equal([OrigemFigura.Desenho, OrigemFigura.Imagem], p2.Select(f => f.Origem));
        Assert.All(p2, f => Assert.Equal(1, f.Linha));
        Assert.InRange(p2[1].X, 396 / 595.0, 404 / 595.0);
        // Página 3: degradê sem caixa conhecida ⇒ a página inteira.
        var p3 = Assert.Single(figuras, f => f.Pagina == 3);
        Assert.Equal(OrigemFigura.Pagina, p3.Origem);
        Assert.Equal((0, 0, 1, 1), (p3.X, p3.Y, p3.W, p3.H));

        // Na leitura padronizada, cada figura entra no ponto certo do texto.
        var blocos = LeituraPadronizada.Estruturar(texto.TextoPorPagina, figuras);
        Assert.Equal(
            ["Epigrafe", "Artigo:Art. 1º", "Figura:Figura da página 1", "Artigo:Art. 2º", "Artigo:Art. 3º", "Figura:Figura da página 2",
             "Figura:Figura da página 2", "Artigo:Art. 4º", "Artigo:Art. 5º", "Figura:Conteúdo gráfico da página 3"],
            blocos.Select(b => b.Tipo is TipoBlocoLei.Artigo or TipoBlocoLei.Figura ? $"{b.Tipo}:{b.Rotulo}" : b.Tipo.ToString()));
        Assert.Empty(extrator.RequisicoesBloqueadas);
        Assert.Equal(0, extrator.ViolacoesCsp);
    }

    [Fact]
    public async Task pdf_sem_figuras_nao_traz_figuras_e_lei_digitalizada_traz_o_desenho()
    {
        Assert.Empty(await extrator.ExtrairFigurasAsync(Fixture(Lei), CancellationToken.None));
        var digitalizada = await extrator.ExtrairFigurasAsync(Fixture("OUTRAS/DIGITALIZADA - FICTICIA.pdf"), CancellationToken.None);
        Assert.Equal(OrigemFigura.Desenho, Assert.Single(digitalizada).Origem);
    }

    [Fact]
    public async Task figuras_de_pdf_com_senha_sao_recusadas()
    {
        var e = await Assert.ThrowsAsync<ExtracaoRecusadaException>(() => extrator.ExtrairFigurasAsync(Fixture("OUTRAS/SENHA - FICTICIA.pdf"), CancellationToken.None));
        Assert.Equal(FalhaExtracao.Senha, e.Falha);
    }

    // Roteiro, passo 48 (2026-11-15): cancelar as figuras no meio (abrir outra lei, fechar, excluir) destruía o documento com o
    // getOperatorList pendente; essa promessa do PDF.js nunca terminava e prendia a fila da página: toda extração seguinte (texto ou
    // figuras) esperava os 10 minutos do limite.
    [Fact]
    public async Task cancelar_as_figuras_no_meio_nunca_prende_as_extracoes_seguintes()
    {
        var figuras = Gravar("COM-FIGURAS - FICTICIA.pdf", PdfLegislacaoFicticio.GerarComFiguras());
        var lei = Fixture(Lei);
        extrator.LimiteExtracao = TimeSpan.FromSeconds(10);
        foreach (var atraso in new[] { 0, 20, 40, 60, 80, 100, 130, 160, 200, 250, 300, 400 })
        {
            using var cts = new CancellationTokenSource();
            var lendo = extrator.ExtrairFigurasAsync(figuras, cts.Token);
            await Task.Delay(atraso);
            cts.Cancel();
            try { await lendo; } catch (OperationCanceledException) { }
            var relogio = Stopwatch.StartNew();
            Assert.Equal(3, (await extrator.ExtrairAsync(lei, null, CancellationToken.None)).Paginas);
            Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(5), $"texto depois de cancelar as figuras em {atraso} ms levou {relogio.Elapsed}");
        }
        // E as figuras seguintes também saem (a fila das figuras não ficou presa).
        Assert.Equal(4, (await extrator.ExtrairFigurasAsync(figuras, CancellationToken.None)).Count);
        extrator.LimiteExtracao = TimeSpan.FromMinutes(10);
    }

    [Fact]
    public async Task texto_nao_espera_as_figuras_de_outra_lei()
    {
        var grande = Gravar("figuras-grande.pdf", PdfLegislacaoFicticio.GerarComFigurasGrande(120));
        var figurasVistas = 0;
        extrator.Ponte.Recebida += m => { if (m.Tipo == "figurasPagina") Interlocked.Increment(ref figurasVistas); };
        using var cts = new CancellationTokenSource();
        var lendo = extrator.ExtrairFigurasAsync(grande, cts.Token);
        await Esperar(() => Volatile.Read(ref figurasVistas) > 0, 20000);
        Assert.True(figurasVistas > 0, "a leitura das figuras não começou");

        // Com as figuras em andamento, a extração de texto (anexar outra lei) sai na hora.
        var relogio = Stopwatch.StartNew();
        Assert.Equal(3, (await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None)).Paginas);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(5), "o texto esperou " + relogio.Elapsed);
        Assert.False(lendo.IsCompleted, "a leitura das figuras terminou antes do texto: o teste não provou nada");

        cts.Cancel();
        relogio.Restart();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lendo);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(5), "cancelar as figuras levou " + relogio.Elapsed);
    }

    [Fact]
    public async Task tempo_total_das_figuras_esgotado_recusa_e_libera_o_extrator()
    {
        var grande = Gravar("figuras-limite.pdf", PdfLegislacaoFicticio.GerarComFigurasGrande(120));
        extrator.LimiteFiguras = TimeSpan.FromMilliseconds(300);
        var relogio = Stopwatch.StartNew();
        var e = await Assert.ThrowsAsync<ExtracaoRecusadaException>(() => extrator.ExtrairFigurasAsync(grande, CancellationToken.None));
        Assert.Equal(FalhaExtracao.Falhou, e.Falha);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(5), "o limite levou " + relogio.Elapsed);
        extrator.LimiteFiguras = TimeSpan.FromMinutes(3);
        Assert.Equal(3, (await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None)).Paginas);
        Assert.Equal(OrigemFigura.Desenho, Assert.Single(await extrator.ExtrairFigurasAsync(Fixture("OUTRAS/DIGITALIZADA - FICTICIA.pdf"), CancellationToken.None)).Origem);
    }

    [Fact]
    public async Task figuras_de_pdf_danificado_sao_recusadas_e_o_extrator_segue()
    {
        var e = await Assert.ThrowsAsync<ExtracaoRecusadaException>(() => extrator.ExtrairFigurasAsync(Fixture("OUTRAS/CORROMPIDO - FICTICIA.pdf"), CancellationToken.None));
        Assert.Equal(FalhaExtracao.Invalido, e.Falha);
        Assert.Equal(3, (await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None)).Paginas);
    }

    [Fact]
    public void figura_com_assinatura_ou_origem_errada_e_descartada()
    {
        static JsonElement J(string json) => JsonDocument.Parse(json).RootElement.Clone();
        var png = Convert.ToBase64String([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1]);
        Assert.NotNull(ExtratorPdfJs.LerFigura(1, J($$"""{"linha":0,"x":0,"y":0,"w":1,"h":1,"largura":10,"origem":"imagem","tipo":"image/png","dados":"{{png}}"}""")));
        Assert.Null(ExtratorPdfJs.LerFigura(1, J($$"""{"linha":0,"x":0,"y":0,"w":1,"h":1,"largura":10,"origem":"imagem","tipo":"image/jpeg","dados":"{{png}}"}""")));
        Assert.Null(ExtratorPdfJs.LerFigura(1, J($$"""{"linha":0,"x":0,"y":0,"w":1,"h":1,"largura":10,"origem":"outra","tipo":"image/png","dados":"{{png}}"}""")));
        Assert.Null(ExtratorPdfJs.LerFigura(1, J("""{"linha":0,"x":0,"y":0,"w":1,"h":1,"largura":10,"origem":"imagem","tipo":"image/png","dados":"%%%"}""")));
        var falhou = ExtratorPdfJs.LerFigura(2, J("""{"linha":3,"x":0,"y":0,"w":1,"h":1,"largura":10,"origem":"falhou","tipo":"","dados":""}"""))!;
        Assert.Empty(falhou.Dados);
    }

    [Fact]
    public async Task pdf_sem_texto_vem_com_paginas_vazias()
    {
        var r = await extrator.ExtrairAsync(Fixture("OUTRAS/DIGITALIZADA - FICTICIA.pdf"), null, CancellationToken.None);
        Assert.Equal(1, r.Paginas);
        Assert.Equal([1], r.PaginasSemTexto);
        Assert.Equal("", Colapsar(r.TextoPorPagina[0]));
    }

    [Fact]
    public async Task pdf_com_senha_e_recusado()
    {
        var ex = await Assert.ThrowsAsync<ExtracaoRecusadaException>(() => extrator.ExtrairAsync(Fixture("OUTRAS/SENHA - FICTICIA.pdf"), null, CancellationToken.None));
        Assert.Equal(FalhaExtracao.Senha, ex.Falha);
        // O extrator continua utilizável depois de uma recusa.
        Assert.Equal(3, (await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None)).Paginas);
    }

    [Fact]
    public async Task pdf_danificado_e_recusado()
    {
        var ex = await Assert.ThrowsAsync<ExtracaoRecusadaException>(() => extrator.ExtrairAsync(Fixture("OUTRAS/CORROMPIDO - FICTICIA.pdf"), null, CancellationToken.None));
        Assert.Equal(FalhaExtracao.Invalido, ex.Falha);
        Assert.Equal(3, (await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None)).Paginas);
    }

    [Fact]
    public async Task arquivo_que_nao_e_pdf_e_recusado()
    {
        var ex = await Assert.ThrowsAsync<ExtracaoRecusadaException>(() => extrator.ExtrairAsync(Fixture("OUTRAS/NAO-E-PDF - FICTICIA.pdf"), null, CancellationToken.None));
        Assert.NotEqual(FalhaExtracao.Senha, ex.Falha);
    }

    [Fact]
    public async Task arquivo_inexistente_e_recusado_sem_travar()
    {
        var ex = await Assert.ThrowsAsync<ExtracaoRecusadaException>(() => extrator.ExtrairAsync(Path.Combine(pasta, "nao-existe.pdf"), null, CancellationToken.None));
        Assert.Equal(FalhaExtracao.Falhou, ex.Falha);
    }

    [Fact]
    public async Task script_do_pdf_nao_roda_e_nada_sai_do_host()
    {
        var r = await extrator.ExtrairAsync(Fixture("OUTRAS/COM-SCRIPT - FICTICIA.pdf"), null, CancellationToken.None);

        Assert.Equal(1, r.Paginas);
        Assert.Equal("Página com script fictício.", Colapsar(r.TextoPorPagina[0]));
        Assert.Empty(extrator.RequisicoesBloqueadas);
        Assert.Equal(0, extrator.ViolacoesCsp);
        Assert.NotEmpty(extrator.RequisicoesVistas);
        Assert.All(extrator.RequisicoesVistas, u => Assert.StartsWith("https://leg.pmvni.invalid/", u));
        Assert.DoesNotContain(extrator.RequisicoesVistas, u => u.Contains("exemplo.invalid"));
        Assert.Empty(extrator.Ponte.Descartadas);
    }

    [Fact]
    public async Task controller_oculto_inprivate_e_com_as_configuracoes_da_sandbox()
    {
        Assert.Equal(Pagina, (await extrator.AvaliarAsync("location.href")).GetProperty("value").GetString());
        var c = await extrator.NoControllerAsync(ctl => new
        {
            InPrivate = ctl.CoreWebView2.Profile.IsInPrivateModeEnabled,
            Visivel = ctl.IsVisible,
            Atalhos = ctl.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled,
            Zoom = ctl.CoreWebView2.Settings.IsZoomControlEnabled,
            Menus = ctl.CoreWebView2.Settings.AreDefaultContextMenusEnabled,
            Objetos = ctl.CoreWebView2.Settings.AreHostObjectsAllowed,
            Dialogos = ctl.CoreWebView2.Settings.AreDefaultScriptDialogsEnabled,
            Autopreenchimento = ctl.CoreWebView2.Settings.IsGeneralAutofillEnabled,
            Senhas = ctl.CoreWebView2.Settings.IsPasswordAutosaveEnabled,
        });
        Assert.True(c.InPrivate);
        Assert.False(c.Visivel);
        Assert.False(c.Atalhos);
        Assert.False(c.Zoom);
        Assert.False(c.Menus);
        Assert.False(c.Objetos);
        Assert.False(c.Dialogos);
        Assert.False(c.Autopreenchimento);
        Assert.False(c.Senhas);
    }

    [Fact]
    public async Task navegacao_para_outra_url_e_cancelada()
    {
        await extrator.AvaliarAsync("(() => { location.href = 'https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs'; return 1; })()");
        await Esperar(() => extrator.RequisicoesBloqueadas.Count >= 1);
        await Task.Delay(300);

        Assert.Single(extrator.RequisicoesBloqueadas);
        Assert.Equal("https://leg.pmvni.invalid/pdfjs/build/pdf.min.mjs", extrator.RequisicoesBloqueadas[0]);
        Assert.Equal(Pagina, (await extrator.AvaliarAsync("location.href")).GetProperty("value").GetString());
        // Recarregar a própria página também é cancelado (só a primeira navegação passa).
        await extrator.AvaliarAsync("(() => { location.reload(); return 1; })()");
        await Esperar(() => extrator.RequisicoesBloqueadas.Count >= 2);
        Assert.Equal(2, extrator.RequisicoesBloqueadas.Count);
        // E o extrator segue funcionando.
        Assert.Equal(3, (await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None)).Paginas);
    }

    [Fact]
    public async Task subrecurso_fora_da_lista_recebe_403_e_outro_host_viola_a_csp()
    {
        var status = await extrator.AvaliarAsync("fetch('https://leg.pmvni.invalid/catalogo.json').then(r => r.status)");
        Assert.Equal(403, status.GetProperty("value").GetInt32());
        Assert.Contains("https://leg.pmvni.invalid/catalogo.json", extrator.RequisicoesBloqueadas);
        Assert.Equal(0, extrator.ViolacoesCsp);

        var violacoes = new List<string>();
        extrator.Ponte.Recebida += m => { if (m.Tipo == "cspViolacao") lock (violacoes) violacoes.Add(m.Dados.GetRawText()); };
        var externo = await extrator.AvaliarAsync("fetch('https://exemplo.invalid/').then(() => 'saiu', () => 'falhou')");
        Assert.Equal("falhou", externo.GetProperty("value").GetString());
        await Esperar(() => extrator.ViolacoesCsp >= 1);
        await Task.Delay(300);
        Assert.True(extrator.ViolacoesCsp == 1, string.Join(" | ", violacoes));
        Assert.DoesNotContain(extrator.RequisicoesVistas, u => u.Contains("exemplo.invalid"));

        // Script em linha também é barrado pela CSP (o Runtime.evaluate do CDP não serve para provar o eval: ele ignora a CSP).
        await extrator.AvaliarAsync("(() => { const s = document.createElement('script'); s.textContent = 'window.__rodou = 1'; document.body.appendChild(s); return 1; })()");
        await Esperar(() => extrator.ViolacoesCsp >= 2);
        Assert.Equal(2, extrator.ViolacoesCsp);
        Assert.Equal("undefined", (await extrator.AvaliarAsync("typeof window.__rodou")).GetProperty("value").GetString());
    }

    [Fact]
    public async Task mensagens_invalidas_sao_descartadas()
    {
        var antes = extrator.Ponte.Descartadas.Count;
        await extrator.AvaliarAsync("""
            (() => {
              chrome.webview.postMessage({ v: 2, tipo: 'pronto', id: 'x', dados: {} });
              chrome.webview.postMessage({ v: 1, tipo: 'tipo-desconhecido', id: 'x', dados: {} });
              chrome.webview.postMessage({ v: 1, tipo: 'extrair', id: 'x', dados: { url: 'https://leg.pmvni.invalid/pdf/x.pdf' } });
              return 1;
            })()
            """);
        await Esperar(() => extrator.Ponte.Descartadas.Count >= antes + 3);

        Assert.Equal(antes + 3, extrator.Ponte.Descartadas.Count);
        Assert.Contains(extrator.Ponte.Descartadas, d => d.Motivo.StartsWith("versao nao suportada", StringComparison.Ordinal));
        Assert.Equal(2, extrator.Ponte.Descartadas.Count(d => d.Motivo.StartsWith("tipo desconhecido", StringComparison.Ordinal)));
        Assert.Equal(0, extrator.ViolacoesCsp);
        Assert.Equal(3, (await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None)).Paginas); // nada mais aconteceu
    }

    [Fact]
    public async Task cancelar_interrompe_a_extracao()
    {
        var grande = Gravar("grande-400.pdf", PdfLegislacaoFicticio.GerarGrande(400));
        using var cts = new CancellationTokenSource();
        var cancelouEm = new Stopwatch();
        var progresso = new ProgressoSincrono(p =>
        {
            if (p.Pagina == 1 && !cancelouEm.IsRunning) { cancelouEm.Start(); cts.Cancel(); }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extrator.ExtrairAsync(grande, progresso, cts.Token));

        Assert.True(cancelouEm.Elapsed < TimeSpan.FromSeconds(5), "cancelar levou " + cancelouEm.Elapsed);
        // Os restos da extração cancelada não contaminam a próxima.
        var seguinte = await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None);
        Assert.Equal(3, seguinte.Paginas);
        Assert.Equal("LEI Nº 9.999, DE 1º DE JANEIRO DE 2099 (FICTÍCIA) Art. 1º Esta Lei fictícia trata do afastamento frontal das edificações.", Colapsar(seguinte.TextoPorPagina[0]));
    }

    [Fact]
    public async Task tempo_esgotado_tambem_manda_a_pagina_parar()
    {
        var grande = Gravar("grande-timeout.pdf", PdfLegislacaoFicticio.GerarGrande(400));
        var paradas = new List<string>();
        extrator.Ponte.Recebida += m =>
        {
            if (m.Tipo == "erro" && m.Dados.TryGetProperty("mensagem", out var t) && t.GetString() == "Extracao cancelada.") lock (paradas) paradas.Add(m.Id);
        };
        extrator.LimiteExtracao = TimeSpan.FromMilliseconds(1); // esgota antes de a página terminar as 400 páginas

        var ex = await Assert.ThrowsAsync<ExtracaoRecusadaException>(() => extrator.ExtrairAsync(grande, null, CancellationToken.None));

        Assert.Equal(FalhaExtracao.Falhou, ex.Falha);
        Assert.Contains("não respondeu", ex.Message);
        await Esperar(() => { lock (paradas) return paradas.Count > 0; });
        lock (paradas) Assert.Single(paradas); // a página recebeu o `cancelar` e respondeu que parou
        extrator.LimiteExtracao = TimeSpan.FromMinutes(10);
        Assert.Equal(3, (await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None)).Paginas);
    }

    [Fact]
    public async Task cancelamento_antes_de_comecar_nao_toca_o_leitor()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => extrator.ExtrairAsync(Fixture(Lei), null, cts.Token));
        Assert.Equal(3, (await extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None)).Paginas);
    }

    [Fact]
    public async Task duas_extracoes_ao_mesmo_tempo_ficam_na_fila()
    {
        var a = extrator.ExtrairAsync(Fixture(Lei), null, CancellationToken.None);
        var b = extrator.ExtrairAsync(Fixture("ESTADUAL - CBMES/NT 99-2099 - FICTICIA.pdf"), null, CancellationToken.None);
        var resultados = await Task.WhenAll(a, b);
        Assert.Equal([3, 2], resultados.Select(r => r.Paginas));
    }

    [Fact]
    public async Task registro_do_pdf_sai_do_servidor_no_fim()
    {
        var caminho = Fixture(Lei);
        await extrator.ExtrairAsync(caminho, null, CancellationToken.None);
        // O token usado na extração já não responde: o servidor não guarda nada além do necessário.
        Assert.All(extrator.RequisicoesVistas.Where(u => u.Contains("/pdf/")), u => Assert.Null(servidor.Responder("GET", u, null)));
    }

    [Fact]
    public async Task range_e_cabecalhos_do_pdf_chegam_ao_navegador()
    {
        var caminho = Fixture(Lei);
        var tamanho = new FileInfo(caminho).Length;
        var url = servidor.RegistrarPdf(caminho);
        var js = $$"""
            (async () => {
              const inteiro = await fetch('{{url}}');
              const corpo = await inteiro.arrayBuffer();
              const parte = await fetch('{{url}}', { headers: { Range: 'bytes=0-9' } });
              const bytes = new Uint8Array(await parte.arrayBuffer());
              const fora = await fetch('{{url}}', { headers: { Range: 'bytes=99999999-' } });
              return JSON.stringify({
                inteiro: [inteiro.status, corpo.byteLength, inteiro.headers.get('accept-ranges'), inteiro.headers.get('content-length'), inteiro.headers.get('content-type')],
                parte: [parte.status, bytes.length, parte.headers.get('content-range'), String.fromCharCode(...bytes.slice(0, 5))],
                fora: [fora.status, fora.headers.get('content-range')],
              });
            })()
            """;
        var resultado = JsonDocument.Parse((await extrator.AvaliarAsync(js)).GetProperty("value").GetString()!).RootElement;
        servidor.RemoverPdf(url);

        Assert.Equal(200, resultado.GetProperty("inteiro")[0].GetInt32());
        Assert.Equal(tamanho, resultado.GetProperty("inteiro")[1].GetInt64());
        Assert.Equal("bytes", resultado.GetProperty("inteiro")[2].GetString());
        Assert.Equal(tamanho.ToString(), resultado.GetProperty("inteiro")[3].GetString());
        Assert.Equal("application/pdf", resultado.GetProperty("inteiro")[4].GetString());
        Assert.Equal(206, resultado.GetProperty("parte")[0].GetInt32());
        Assert.Equal(10, resultado.GetProperty("parte")[1].GetInt32());
        Assert.Equal($"bytes 0-9/{tamanho}", resultado.GetProperty("parte")[2].GetString());
        Assert.Equal("%PDF-", resultado.GetProperty("parte")[3].GetString());
        Assert.Equal(416, resultado.GetProperty("fora")[0].GetInt32());
        Assert.Equal($"bytes */{tamanho}", resultado.GetProperty("fora")[1].GetString());
    }

    [Fact]
    public async Task pdf_de_varios_megabytes_e_lido_em_fluxo()
    {
        // 3,5 MB (acima de 2 x rangeChunkSize): o PDF.js recebe o arquivo em fluxo (sem carregá-lo inteiro no host) e só pede
        // Range se faltar dado; o importante é concluir certo e sem nada bloqueado.
        var bytes = PdfLegislacaoFicticio.Gerar(PdfLegislacaoFicticio.Textos[Lei], bytesExtras: 3_500_000);
        Assert.InRange(bytes.Length, 3_500_000, 3_600_000);
        var r = await extrator.ExtrairAsync(Gravar("grande-mb.pdf", bytes), null, CancellationToken.None);

        Assert.Equal(3, r.Paginas);
        Assert.Equal("Art. 2º O afastamento frontal mínimo é de 3,00 m. Art. 3º A taxa de ocupação máxima é de 60%.", Colapsar(r.TextoPorPagina[1]));
        Assert.Empty(extrator.RequisicoesBloqueadas);
        Assert.Equal(0, extrator.ViolacoesCsp);
    }

    [Fact]
    public async Task tempo_de_extracao_de_200_paginas()
    {
        var pdf = Gravar("grande-200.pdf", PdfLegislacaoFicticio.GerarGrande(200));
        var relogio = Stopwatch.StartNew();
        var r = await extrator.ExtrairAsync(pdf, null, CancellationToken.None);
        relogio.Stop();

        Assert.Equal(200, r.Paginas);
        Assert.Empty(r.PaginasSemTexto);
        var saida = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "tempos-extracao.json");
        Directory.CreateDirectory(Path.GetDirectoryName(saida)!);
        File.WriteAllText(saida, JsonSerializer.Serialize(new { paginas = 200, milissegundos = relogio.ElapsedMilliseconds, pdfjs = RecursosLegislacao.VersaoPdfJs, bytes = new FileInfo(pdf).Length },
            new JsonSerializerOptions { WriteIndented = true }));
    }
}
