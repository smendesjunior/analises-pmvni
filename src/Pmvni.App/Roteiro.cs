using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Pmvni.App;

/// <summary>Modo de diagnóstico <c>--roteiro &lt;saida.json&gt;</c>: depois de abrir o relatório da linha de comando, exercita a tela
/// pelos mesmos caminhos dos botões (abrir, rolagem, zoom, navegação, exportação, corpus com estouro, tema escuro) e grava o
/// resultado em JSON. Como ninguém vê a janela, também grava capturas (PNG) em tests\saida\roteiro para a conferência visual.
/// Cada passo é independente: falha num não impede os seguintes.</summary>
internal static class Roteiro
{
    public static async Task ExecutarAsync(MainWindow j, Argumentos args)
    {
        var dadosLongo = args.Dados ?? throw new InvalidOperationException("--roteiro pede também o caminho do dados.json (tests\\corpus\\longo.json).");
        var corpus = Path.GetDirectoryName(dadosLongo)!;
        // Todas as saídas (capturas, PDFs, recibos) ficam na pasta do <saida.json>; nada é gravado nem apagado fora dela.
        var pasta = Path.GetDirectoryName(args.Roteiro!)!;
        Directory.CreateDirectory(pasta);
        var estouro = Path.Combine(corpus, "estouro.json");
        var saida = new JsonObject
        {
            ["gerado_em"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
            ["webview2"] = j.NucleoWeb.Environment.BrowserVersionString,
            ["escala_do_monitor"] = j.ElementoRaiz.XamlRoot?.RasterizationScale,
            ["janela_px"] = new JsonObject { ["largura"] = j.ElementoRaiz.ActualWidth, ["altura"] = j.ElementoRaiz.ActualHeight },
        };
        var passos = new JsonObject();
        saida["passos"] = passos;
        var falhas = new JsonArray();

        async Task Passo(string nome, Func<JsonObject, Task> corpoPasso)
        {
            var p = new JsonObject();
            try { await corpoPasso(p); p["ok"] ??= true; }
            catch (Exception e) { p["ok"] = false; p["excecao"] = e.GetType().Name + ": " + e.Message; }
            passos[nome] = p;
            if (p["ok"]?.GetValue<bool>() != true) falhas.Add(nome);
        }

        await Passo("1_abrir_longo", async p =>
        {
            var a = await j.AbrirAsync(dadosLongo) ?? throw new InvalidOperationException("não abriu: " + j.InfoMensagem.Message);
            var plano = a.Composicao.Plano;
            p["folhas"] = a.Composicao.Folhas;
            p["escala_de_impressao"] = plano.Escala;
            p["avisos_de_largura"] = plano.AvisosLargura.Count;
            p["estouros"] = plano.Estouros.Count;
            p["folhas_que_transbordam_na_tela"] = new JsonArray(a.Aplicacao.FolhasQueTransbordam.Select(n => (JsonNode)n).ToArray());
            p["titulo_da_barra"] = j.TextoTituloAtual;
            p["estado"] = j.TextoEstadoAtual;
            p["itens_na_lista"] = j.Itens.Count;
            p["primeiros_itens"] = new JsonArray(j.Itens.Take(6).Select(i => (JsonNode)$"{i.Rotulo} (f. {i.Folha})").ToArray());
            p["aviso_largura_aberto"] = j.InfoLargura.IsOpen;
            p["aviso_largura_texto"] = j.InfoLargura.Message;
            var reducao = (int)Math.Round((1 - 1 / plano.Escala) * 100, MidpointRounding.AwayFromZero);
            p["aviso_largura_confere"] = j.InfoLargura.IsOpen == (plano.AvisosLargura.Count > 0)
                && (plano.AvisosLargura.Count == 0 || j.InfoLargura.Message == $"Conteúdo mais largo que a página: o documento foi reduzido {reducao}% para caber (como na impressão do kit).");
            p["exportar_habilitado"] = j.ExportarHabilitado;
            p["rodape_folha"] = j.TextoFolhaAtual;
            p["convivencia_webview2"] = "tela visível + host oculto de composição abertos no mesmo processo, mesma pasta de dados";
            p["folhas_com_conteudo_alem_da_borda"] = new JsonArray(a.Aplicacao.FolhasQueTransbordamNaLargura.Select(n => (JsonNode)n).ToArray());
            p["aviso_fora_da_area_aberto"] = j.InfoForaDaArea.IsOpen;
            // Diagnóstico (só medidas): retângulos do conteúdo além da borda lateral da área, por folha.
            p["alem_da_borda_medidas"] = JsonNode.Parse((await Js(j, "(() => { const out=[]; document.querySelectorAll('section.folha').forEach((f,i)=>{ const area=f.querySelector('.area'); const r=area.getBoundingClientRect(); const g=document.createRange(); g.selectNodeContents(area); for (const q of g.getClientRects()) { if (q.width>0&&q.height>0&&(q.right>r.right+0.5||q.left<r.left-0.5)) out.push({folha:i+1, esquerda:Math.round((q.left-r.left)*10)/10, alemDaDireita:Math.round((q.right-r.right)*10)/10, largura:Math.round(q.width*10)/10}); } }); return out.slice(0,10); })()")).GetRawText());
            var faixas = (await Js(j, "document.querySelectorAll('section.folha[data-pmvni-transborda]').length")).GetInt32();
            p["folhas_com_faixa_vermelha"] = faixas;
            // D4: tela e host oculto InPrivate (nada do relatório no perfil); atalhos do navegador desligados nos dois.
            var host = await j.HostComposicao!.ExecutarAsync(core => Task.FromResult((core.Profile.IsInPrivateModeEnabled, core.Settings.AreBrowserAcceleratorKeysEnabled)));
            p["inprivate_tela"] = j.NucleoWeb.Profile.IsInPrivateModeEnabled;
            p["inprivate_host_oculto"] = host.Item1;
            p["atalhos_do_navegador_tela"] = j.NucleoWeb.Settings.AreBrowserAcceleratorKeysEnabled;
            p["atalhos_do_navegador_host_oculto"] = host.Item2;
            var protegido = j.NucleoWeb.Profile.IsInPrivateModeEnabled && host.Item1 && !j.NucleoWeb.Settings.AreBrowserAcceleratorKeysEnabled && !host.Item2;
            p["ok"] = a.Composicao.Folhas > 3 && plano.Estouros.Count == 0 && j.ExportarHabilitado && p["aviso_largura_confere"]!.GetValue<bool>()
                && protegido && faixas == a.Aplicacao.FolhasMarcadas.Count
                && j.InfoForaDaArea.IsOpen == (a.Aplicacao.FolhasQueTransbordamNaLargura.Count > 0);
            await Esperar(600);
            await CapturarWebAsync(j, Path.Combine(pasta, "01-tela-100.png"));
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "01-janela-xaml.png"));
        });

        await Passo("2_rolagem_continua", async p =>
        {
            var g = await Js(j, """
                (() => { const f = [...document.querySelectorAll('section.folha')];
                  const tops = f.map(x => x.getBoundingClientRect().top + scrollY);
                  const passos = tops.slice(1).map((t, i) => t - tops[i]);
                  return { folhas: f.length, passoMin: Math.min(...passos), passoMax: Math.max(...passos), passoEsperado: (297 + 8) * 96 / 25.4,
                           rolagemTotal: document.documentElement.scrollHeight, janela: document.documentElement.clientHeight,
                           fundoDaPagina: getComputedStyle(document.documentElement).backgroundColor, fundoDaFolha: getComputedStyle(f[0]).backgroundColor,
                           sombraDaFolha: getComputedStyle(f[0]).boxShadow, devicePixelRatio, topos: tops.slice(0, 3) }; })()
                """);
            p["geometria"] = JsonNode.Parse(g.GetRawText());
            var n = g.GetProperty("folhas").GetInt32();
            var passo = g.GetProperty("passoEsperado").GetDouble();
            var continuo = Math.Abs(g.GetProperty("passoMin").GetDouble() - passo) < 1 && Math.Abs(g.GetProperty("passoMax").GetDouble() - passo) < 1;
            var visitas = new JsonArray();
            var coerente = true;
            foreach (var alvo in new[] { 1, 4, 9, n / 2, n - 1, n })
            {
                await Js(j, $"window.scrollTo({{ top: document.querySelectorAll('section.folha')[{alvo - 1}].getBoundingClientRect().top + scrollY - 40, behavior: 'instant' }}); 1");
                await Esperar(450);
                visitas.Add(new JsonObject { ["rolou_para_folha"] = alvo, ["indicador"] = j.TextoFolhaAtual, ["folha_no_indicador"] = j.FolhaAtual });
                coerente &= j.FolhaAtual == alvo;
            }
            p["visitas_por_rolagem"] = visitas;
            p["folhas_contiguas_com_8mm"] = continuo;
            p["indicador_acompanha_a_rolagem"] = coerente;
            p["ok"] = continuo && coerente;
            await Js(j, "window.scrollTo({ top: 0, behavior: 'instant' }); 1");
            await Esperar(300);
        });

        await Passo("3_zoom_nao_recompoe", async p =>
        {
            var a = j.Aberto!;
            var antes = await j.Documento!.TextoPorFolhaAsync();
            var planoAntes = a.Composicao.Plano.ParaJson();
            await Js(j, "window.__marcaFolhas = [...document.querySelectorAll('section.folha')]; 1");
            var amostras = new JsonArray();
            var igual = true;
            async Task Confere(string como)
            {
                await Esperar(400);
                var depois = await j.Documento!.TextoPorFolhaAsync();
                var mesmas = (await Js(j, "(() => { const f = [...document.querySelectorAll('section.folha')]; return f.length === window.__marcaFolhas.length && f.every((x, i) => x === window.__marcaFolhas[i]); })()")).GetBoolean();
                var larguraFolha = (await Js(j, "document.querySelector('section.folha').getBoundingClientRect().width")).GetDouble();
                igual &= depois.SequenceEqual(antes) && mesmas;
                amostras.Add(new JsonObject { ["como"] = como, ["zoom"] = j.Zoom, ["texto_igual"] = depois.SequenceEqual(antes), ["mesmas_folhas_no_dom"] = mesmas, ["largura_da_folha_px"] = Math.Round(larguraFolha, 1) });
            }
            foreach (var z in new[] { 0.5, 2.0, 3.0, 1.0 }) { await j.ZoomAsync(new { zoom = z }, "manual"); await Confere("definirZoom " + z); }
            await j.ZoomAsync(new { zoom = 1.0 }, "manual");
            foreach (var botao in new[] { j.BotoesZoom[1], j.BotoesZoom[1], j.BotoesZoom[0], j.BotoesZoom[2], j.BotoesZoom[3] })
            {
                var nomeBotao = botao.Content is string s ? s : Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(botao);
                ((IInvokeProvider)new ButtonAutomationPeer(botao).GetPattern(PatternInterface.Invoke)).Invoke();
                await Confere("botão " + nomeBotao);
            }
            await j.ZoomAsync(new { zoom = 1.0 }, "manual");
            var recomposto = (await j.HostComposicao!.Documento.ComporAsync()).ParaJson();
            p["amostras"] = amostras;
            p["plano_igual_depois_do_zoom"] = recomposto == planoAntes && a.Composicao.Plano.ParaJson() == planoAntes;
            p["texto_por_folha_igual"] = igual;
            p["ok"] = igual && recomposto == planoAntes;
            await j.ZoomAsync(new { zoom = 0.5 }, "manual");
            await Esperar(700);
            await CapturarWebAsync(j, Path.Combine(pasta, "03-tela-50.png"));
            await j.ZoomAsync(new { zoom = 1.0 }, "manual");
            await Esperar(300);
        });

        await Passo("4_navegacao", async p =>
        {
            var lista = j.Itens;
            var escolhidos = new List<ItemNavegacao> { lista[0] };
            escolhidos.AddRange(lista.Where(i => i.Secao).Skip(1).Take(3));
            escolhidos.AddRange(lista.Where(i => !i.Secao).Where((_, k) => k % 11 == 5).Take(4));
            escolhidos.Add(lista[^1]);
            var amostras = new JsonArray();
            var todos = true;
            foreach (var item in escolhidos)
            {
                await j.RolarParaAsync(item);
                await Esperar(450);
                var v = await Js(j, $"(() => {{ const f = document.querySelectorAll('section.folha')[{item.Folha - 1}].getBoundingClientRect(); return {{ topo: f.top, base: f.bottom, janela: document.documentElement.clientHeight }}; }})()");
                var visivel = v.GetProperty("topo").GetDouble() < v.GetProperty("janela").GetDouble() && v.GetProperty("base").GetDouble() > 0;
                var certa = j.FolhaAtual == item.Folha || j.FolhaAtual == item.Folha + 1;
                todos &= visivel && certa;
                amostras.Add(new JsonObject { ["item"] = item.Rotulo, ["folha_do_item"] = item.Folha, ["folha_na_tela"] = j.FolhaAtual, ["folha_visivel"] = visivel,
                    ["indicador_do_trilho_marcado"] = j.Itens.Any(i => i.Atual), ["rodape"] = j.TextoFolhaAtual });
            }
            p["amostras"] = amostras;
            p["ok"] = todos;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "04-janela-xaml-navegacao.png"));
            await CapturarWebAsync(j, Path.Combine(pasta, "04-tela-depois-de-navegar.png"));
        });

        await Passo("5_exportar_pdf", async p =>
        {
            var pdf = Path.Combine(pasta, "longo.pdf");
            File.Delete(pdf);
            var (ok, x, erro, _) = await j.ExportarParaAsync(pdf);
            if (!ok || x is null) throw new InvalidOperationException("exportação falhou: " + erro);
            var bytes = await File.ReadAllBytesAsync(pdf);
            var info = InfoPdf.Ler(bytes);
            using var recibo = JsonDocument.Parse(await File.ReadAllBytesAsync(x.CaminhoRecibo));
            var a = j.Aberto!;
            p["paginas_do_pdf"] = info.Paginas;
            p["folhas_na_tela"] = a.Composicao.Folhas;
            p["pagina_mm"] = $"{info.LarguraMm:0.##} x {info.AlturaMm:0.##}";
            p["recibo_sha256_html_confere"] = recibo.RootElement.GetProperty("sha256_html").GetString() == PlanoQuebras.Sha256De(a.Render.Html);
            p["recibo_sha256_pdf_confere"] = recibo.RootElement.GetProperty("sha256_pdf").GetString() == Convert.ToHexStringLower(SHA256.HashData(bytes));
            p["plano_do_pdf_igual_ao_da_tela"] = x.Plano.ParaJson() == a.Composicao.Plano.ParaJson();
            p["aviso_de_sucesso"] = j.InfoMensagem.IsOpen && j.InfoMensagem.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
            p["ok"] = info.Paginas == a.Composicao.Folhas && p["recibo_sha256_html_confere"]!.GetValue<bool>() && p["recibo_sha256_pdf_confere"]!.GetValue<bool>()
                && p["plano_do_pdf_igual_ao_da_tela"]!.GetValue<bool>() && p["aviso_de_sucesso"]!.GetValue<bool>();
        });

        await Passo("6_estouro_recusado", async p =>
        {
            var a = await j.AbrirAsync(estouro) ?? throw new InvalidOperationException("não abriu o estouro: " + j.InfoMensagem.Message);
            p["estouros"] = new JsonArray(a.Composicao.Plano.Estouros.Select(e => (JsonNode)$"folha {e.Folha}, ref {e.Ref}").ToArray());
            p["estado"] = j.TextoEstadoAtual;
            p["aviso_de_erro_aberto"] = j.InfoEstouro.IsOpen && j.InfoEstouro.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
            p["aviso_de_erro_texto"] = j.InfoEstouro.Message;
            p["exportar_habilitado"] = j.ExportarHabilitado;
            p["dica_do_botao"] = j.DicaExportar;
            var pdf = Path.Combine(pasta, "estouro.pdf");
            File.Delete(pdf);
            var (ok, _, erro, excecao) = await j.ExportarParaAsync(pdf);
            p["exportacao_recusada"] = !ok && excecao is ExportacaoRecusadaException;
            p["mensagem_da_recusa"] = erro;
            p["nenhum_pdf_gravado"] = !File.Exists(pdf);
            var faixas = (await Js(j, "document.querySelectorAll('section.folha[data-pmvni-transborda]').length")).GetInt32();
            p["folhas_com_faixa_vermelha"] = faixas;
            p["folhas_que_transbordam_na_tela"] = new JsonArray(a.Aplicacao.FolhasQueTransbordam.Select(n => (JsonNode)n).ToArray());
            p["ok"] = a.Composicao.Plano.Estouros.Count > 0 && !j.ExportarHabilitado && p["aviso_de_erro_aberto"]!.GetValue<bool>()
                && p["exportacao_recusada"]!.GetValue<bool>() && !File.Exists(pdf) && faixas == a.Aplicacao.FolhasMarcadas.Count;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "06-janela-xaml-estouro.png"));
            await j.RolarParaAsync(new ItemNavegacao("folha do estouro", a.Composicao.Plano.Estouros[0].Folha, null, false));
            await Esperar(500);
            var vista = await Js(j, "(() => { const f = [...document.querySelectorAll('section.folha')]; const i = f.findIndex(x => { const r = x.getBoundingClientRect(); return r.top <= innerHeight / 2 && r.bottom >= innerHeight / 2; }); const e = f[i]; return { folhaNaTela: i + 1, comFaixa: e.hasAttribute('data-pmvni-transborda'), sombra: getComputedStyle(e).boxShadow, esquerda: e.getBoundingClientRect().left, zoom: getComputedStyle(document.documentElement).getPropertyValue('--pmvni-zoom') }; })()");
            p["folha_vista_apos_rolar_ate_o_estouro"] = JsonNode.Parse(vista.GetRawText());
            p["faixa_vermelha_na_folha_vista"] = vista.GetProperty("comFaixa").GetBoolean() && vista.GetProperty("folhaNaTela").GetInt32() == a.Composicao.Plano.Estouros[0].Folha;
            if (!p["faixa_vermelha_na_folha_vista"]!.GetValue<bool>()) p["ok"] = false;
            await CapturarWebAsync(j, Path.Combine(pasta, "06-tela-estouro.png"));
        });

        await Passo("7_tema_escuro", async p =>
        {
            var a = await j.AbrirAsync(dadosLongo) ?? throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var antes = await j.Documento!.TextoPorFolhaAsync();
            const string Cores = "({ escuro: matchMedia('(prefers-color-scheme: dark)').matches, fundoDaPagina: getComputedStyle(document.documentElement).backgroundColor, " +
                "corpo: getComputedStyle(document.body).backgroundColor, fundoDaFolha: getComputedStyle(document.querySelector('section.folha')).backgroundColor })";
            j.DefinirTema(ElementTheme.Light);
            await Esperar(700);
            var claro = await Js(j, Cores);
            await CapturarWebAsync(j, Path.Combine(pasta, "07-tema-claro.png"));
            j.DefinirTema(ElementTheme.Dark);
            await Esperar(900);
            var escuro = await Js(j, Cores);
            var depois = await j.Documento!.TextoPorFolhaAsync();
            await CapturarWebAsync(j, Path.Combine(pasta, "07-tema-escuro.png"));
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "07-janela-xaml-escuro.png"));
            p["claro"] = JsonNode.Parse(claro.GetRawText());
            p["escuro"] = JsonNode.Parse(escuro.GetRawText());
            var fundoMuda = claro.GetProperty("fundoDaPagina").GetString() != escuro.GetProperty("fundoDaPagina").GetString();
            var folhaBranca = claro.GetProperty("fundoDaFolha").GetString() == "rgb(255, 255, 255)" && escuro.GetProperty("fundoDaFolha").GetString() == "rgb(255, 255, 255)";
            p["so_o_fundo_fora_das_folhas_muda"] = fundoMuda && folhaBranca;
            p["texto_por_folha_igual"] = depois.SequenceEqual(antes);
            p["ok"] = escuro.GetProperty("escuro").GetBoolean() && !claro.GetProperty("escuro").GetBoolean() && fundoMuda && folhaBranca && depois.SequenceEqual(antes) && a.Composicao.Folhas > 0;
            j.DefinirTema(ElementTheme.Default);
        });

        // Cópias do curto (sem imagens) na pasta do roteiro: tudo o que este passo grava e apaga fica nela.
        await Passo("8_campos_em_aberto_bloqueiam_exportar", async p =>
        {
            var copia = Path.Combine(pasta, "curto-colchetes.json");
            var no = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(corpus, "curto.json")))!;
            no["capa"]!["requerente"] = "[Nome do requerente]";
            await File.WriteAllTextAsync(copia, no.ToJsonString());
            try
            {
                var a = await j.AbrirAsync(copia) ?? throw new InvalidOperationException("não abriu: " + j.InfoMensagem.Message);
                p["erros_finais"] = a.ErrosFinais.Count;
                p["exportar_habilitado"] = j.ExportarHabilitado;
                p["dica_do_botao"] = j.DicaExportar;
                p["ok"] = a.ErrosFinais.Count > 0 && !j.ExportarHabilitado && j.DicaExportar.StartsWith("A exportação está bloqueada", StringComparison.Ordinal);
            }
            finally { File.Delete(copia); }
        });

        await Passo("9_arquivo_mudou_recusa_exportar", async p =>
        {
            var copia = Path.Combine(pasta, "curto-muda.json");
            var original = await File.ReadAllTextAsync(Path.Combine(corpus, "curto.json"));
            await File.WriteAllTextAsync(copia, original);
            var pdf = Path.Combine(pasta, "curto-muda.pdf");
            File.Delete(pdf);
            try
            {
                _ = await j.AbrirAsync(copia) ?? throw new InvalidOperationException("não abriu: " + j.InfoMensagem.Message);
                var no = JsonNode.Parse(original)!;
                no["capa"]!["requerente"] = "OUTRO REQUERENTE DEPOIS DE ABRIR";
                await File.WriteAllTextAsync(copia, no.ToJsonString());
                var (ok, _, erro, excecao) = await j.ExportarParaAsync(pdf);
                p["recusada"] = !ok && excecao is ExportacaoDivergenteException;
                p["mensagem"] = erro;
                p["nenhum_pdf_gravado"] = !File.Exists(pdf) && !File.Exists(Exportador.CaminhoReciboDe(pdf));
                p["ok"] = p["recusada"]!.GetValue<bool>() && erro == ExportacaoDivergenteException.ArquivoMudou && p["nenhum_pdf_gravado"]!.GetValue<bool>();
            }
            finally { File.Delete(copia); File.Delete(pdf); File.Delete(Exportador.CaminhoReciboDe(pdf)); }
        });

        saida["falhas"] = falhas;
        saida["ok"] = falhas.Count == 0;
        Gravar(args.Roteiro!, saida);
        Application.Current.Exit();
    }

    static async Task<JsonElement> Js(MainWindow j, string expressao)
    {
        var json = await j.NucleoWeb.ExecuteScriptAsync(expressao);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    static Task Esperar(int ms) => Task.Delay(ms);

    static async Task CapturarWebAsync(MainWindow j, string arquivo)
    {
        using var fluxo = new InMemoryRandomAccessStream();
        await j.NucleoWeb.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, fluxo);
        fluxo.Seek(0);
        var bytes = new byte[fluxo.Size];
        await fluxo.ReadAsync(bytes.AsBuffer(), (uint)fluxo.Size, InputStreamOptions.None);
        await File.WriteAllBytesAsync(arquivo, bytes);
    }

    static async Task CapturarXamlAsync(FrameworkElement elemento, string arquivo)
    {
        // O Mica não entra na captura: fundo sólido do tema só durante a captura, para o texto não sumir.
        var grade = elemento as Microsoft.UI.Xaml.Controls.Grid;
        var anterior = grade?.Background;
        if (grade is not null) grade.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(elemento.ActualTheme == ElementTheme.Dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243));
        var rtb = new RenderTargetBitmap();
        try { await rtb.RenderAsync(elemento); }
        finally { if (grade is not null) grade.Background = anterior; }
        var pixels = await rtb.GetPixelsAsync();
        using var fluxo = new InMemoryRandomAccessStream();
        var codificador = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fluxo);
        codificador.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels.ToArray());
        await codificador.FlushAsync();
        fluxo.Seek(0);
        var bytes = new byte[fluxo.Size];
        await fluxo.ReadAsync(bytes.AsBuffer(), (uint)fluxo.Size, InputStreamOptions.None);
        await File.WriteAllBytesAsync(arquivo, bytes);
    }

    // Grava num temporário ao lado e renomeia: quem espera pelo arquivo nunca lê JSON pela metade.
    static void Gravar(string arquivo, JsonObject dados)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        var temporario = arquivo + ".tmp";
        File.WriteAllText(temporario, dados.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        File.Move(temporario, arquivo, overwrite: true);
    }
}
