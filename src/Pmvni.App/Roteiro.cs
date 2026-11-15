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
using Pmvni.Revisao;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Pmvni.App;

/// <summary>Respostas fixas do roteiro: nenhum seletor nem diálogo abre (ninguém está olhando a janela). Confirmar responde
/// <see cref="Confirmar"/>; cada pergunta fica registrada.</summary>
internal sealed class PerguntasFixas : IPerguntas
{
    public bool Confirmar { get; set; } = true;
    public RespostaRecuperacao Recuperacao { get; set; } = RespostaRecuperacao.Descartar;
    public List<string> Feitas { get; } = [];

    public Task<string?> AbrirArquivoAsync(string extensao) { Feitas.Add("abrir " + extensao); return Task.FromResult<string?>(null); }
    public Task<string?> SalvarArquivoAsync(string nomeSugerido, string extensao) { Feitas.Add("salvar " + nomeSugerido); return Task.FromResult<string?>(null); }
    public Task<bool> ConfirmarAsync(string titulo, string texto, string sim, string nao) { Feitas.Add("confirmar " + titulo); return Task.FromResult(Confirmar); }
    public Task InformarAsync(string titulo, string texto) { Feitas.Add("informar " + titulo); Informados.Add((titulo, texto)); return Task.CompletedTask; }
    public List<(string Titulo, string Texto)> Informados { get; } = [];
    public Task<RespostaRecuperacao> RecuperarAsync(DateTimeOffset ultimo, int comandos) { Feitas.Add($"recuperar {comandos}"); return Task.FromResult(Recuperacao); }
}

/// <summary>Modo de diagnóstico <c>--roteiro &lt;saida.json&gt;</c>: exercita o shell e o módulo Relatórios pelos mesmos caminhos dos
/// comandos da faixa (importar, rolagem, zoom, navegação, exportação, corpus com estouro, tema escuro, campos em aberto, pacote mudado no
/// disco, faixa por módulo, reabrir o pacote; Tarefa 9: seleção e edição no A4, situação e justificativa, exclusão e marca, desfazer e
/// refazer, salvar/reabrir/exportar, menu de contexto e atalhos, latência) e grava o resultado em JSON. Tudo o que o roteiro cria (pacotes, PDFs, capturas PNG) fica
/// na pasta do &lt;saida.json&gt; (tests\saida\roteiro). As perguntas têm respostas fixas (<see cref="PerguntasFixas"/>). Cada passo é
/// independente: falha num não impede os seguintes.</summary>
internal static class Roteiro
{
    public static async Task ExecutarAsync(MainWindow j, Argumentos args)
    {
        var dadosLongo = args.Dados ?? throw new InvalidOperationException("--roteiro pede também o caminho do dados.json (tests\\corpus\\longo.json).");
        var corpus = Path.GetDirectoryName(dadosLongo)!;
        var pasta = Path.GetDirectoryName(args.Roteiro!)!;
        Directory.CreateDirectory(pasta);
        var perguntas = new PerguntasFixas();
        Perguntas.Atual = perguntas;
        var pacoteLongo = Path.Combine(pasta, "longo.pmvrel");
        // Pacotes de rodadas anteriores (só os do roteiro, só nesta pasta): a importação nunca sobrescreve.
        foreach (var nome in new[] { "longo", "estouro", "curto-colchetes", "curto-muda" })
            foreach (var ext in new[] { ".pmvrel", ".pmvrel.bak" }) File.Delete(Path.Combine(pasta, nome + ext));
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
        string? planoDaImportacao = null;

        async Task Passo(string nome, Func<JsonObject, Task> corpoPasso)
        {
            var p = new JsonObject();
            try { await corpoPasso(p); p["ok"] ??= true; }
            catch (Exception e) { p["ok"] = false; p["excecao"] = e.GetType().Name + ": " + e.Message; }
            passos[nome] = p;
            if (p["ok"]?.GetValue<bool>() != true) falhas.Add(nome);
        }

        await Passo("1_importar_longo", async p =>
        {
            if (!await j.ImportarParaAsync(dadosLongo, pacoteLongo)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            var a = j.Aberto!;
            var plano = a.Composicao.Plano;
            planoDaImportacao = plano.ParaJson();
            var capa = JsonNode.Parse(await File.ReadAllTextAsync(dadosLongo))!["capa"]!;
            var tituloEsperado = TituloJanela.De(capa["processo"]?.GetValue<string>(), capa["requerente"]?.GetValue<string>(), "longo");
            p["pacote_criado"] = File.Exists(pacoteLongo);
            p["folhas"] = a.Composicao.Folhas;
            p["escala_de_impressao"] = plano.Escala;
            p["avisos_de_largura"] = plano.AvisosLargura.Count;
            p["estouros"] = plano.Estouros.Count;
            p["folhas_que_transbordam_na_tela"] = new JsonArray(a.Aplicacao.FolhasQueTransbordam.Select(n => (JsonNode)n).ToArray());
            p["titulo_da_barra"] = j.TextoTituloAtual;
            p["titulo_confere"] = j.TextoTituloAtual == tituloEsperado && tituloEsperado.StartsWith("Proc. ", StringComparison.Ordinal);
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["composicao"] = j.TextoComposicaoAtual;
            p["itens_na_lista"] = j.Itens.Count;
            p["primeiros_itens"] = new JsonArray(j.Itens.Take(6).Select(i => (JsonNode)$"{i.Rotulo} (f. {i.Folha})").ToArray());
            p["aviso_largura_aberto"] = j.InfoLargura.IsOpen;
            p["aviso_largura_texto"] = j.InfoLargura.Message;
            var reducao = (int)Math.Round((1 - 1 / plano.Escala) * 100, MidpointRounding.AwayFromZero);
            p["aviso_largura_confere"] = j.InfoLargura.IsOpen == (plano.AvisosLargura.Count > 0)
                && (plano.AvisosLargura.Count == 0 || j.InfoLargura.Message == $"Conteúdo mais largo que a página: o documento foi reduzido {reducao}% para caber (como na impressão do kit).");
            p["exportar_habilitado"] = j.ExportarHabilitado;
            p["rodape_folha"] = j.TextoFolhaAtual;
            p["latencia_recomposicao_ms"] = a.Milissegundos;
            p["convivencia_webview2"] = "tela visível + host oculto de composição abertos no mesmo processo, mesma pasta de dados";
            p["folhas_com_conteudo_alem_da_borda"] = new JsonArray(a.Aplicacao.FolhasQueTransbordamNaLargura.Select(n => (JsonNode)n).ToArray());
            p["aviso_fora_da_area_aberto"] = j.InfoForaDaArea.IsOpen;
            var faixas = (await Js(j, "document.querySelectorAll('section.folha[data-pmvni-transborda]').length")).GetInt32();
            p["folhas_com_faixa_vermelha"] = faixas;
            // O estado do editor chegou à página (o CSS da edição só entra com o primeiro estadoEditor).
            p["estado_do_editor_aplicado"] = (await Js(j, "!!document.getElementById('pmvni-edicao-css')")).GetBoolean();
            // D4: tela e host oculto InPrivate (nada do relatório no perfil); atalhos do navegador desligados nos dois.
            var host = await j.HostComposicao!.ExecutarAsync(core => Task.FromResult((core.Profile.IsInPrivateModeEnabled, core.Settings.AreBrowserAcceleratorKeysEnabled)));
            p["inprivate_tela"] = j.NucleoWeb.Profile.IsInPrivateModeEnabled;
            p["inprivate_host_oculto"] = host.Item1;
            p["atalhos_do_navegador_tela"] = j.NucleoWeb.Settings.AreBrowserAcceleratorKeysEnabled;
            p["atalhos_do_navegador_host_oculto"] = host.Item2;
            var protegido = j.NucleoWeb.Profile.IsInPrivateModeEnabled && host.Item1 && !j.NucleoWeb.Settings.AreBrowserAcceleratorKeysEnabled && !host.Item2;
            p["ok"] = File.Exists(pacoteLongo) && a.Composicao.Folhas > 3 && plano.Estouros.Count == 0 && j.ExportarHabilitado
                && p["aviso_largura_confere"]!.GetValue<bool>() && p["titulo_confere"]!.GetValue<bool>()
                && j.TextoEstadoAtual.StartsWith("Salvo", StringComparison.Ordinal) && p["estado_do_editor_aplicado"]!.GetValue<bool>()
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
            // Rodapé (botões) e faixa (aba Exibir): os mesmos comandos.
            foreach (var botao in new[] { j.BotoesZoom[1], j.BotoesZoom[1], j.BotoesZoom[0], j.BotoesZoom[2], j.BotoesZoom[3] })
            {
                var nomeBotao = botao.Content is string s ? s : Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(botao);
                ((IInvokeProvider)new ButtonAutomationPeer(botao).GetPattern(PatternInterface.Invoke)).Invoke();
                await Confere("botão " + nomeBotao);
            }
            foreach (var id in new[] { ComandosApp.ExibirMais, ComandosApp.ExibirMenos, ComandosApp.ExibirPagina, ComandosApp.ExibirLargura })
            {
                await j.ExecutarComandoAsync(id);
                await Confere("comando " + id);
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
                    ["indicador_da_lista_marcado"] = j.Itens.Any(i => i.Atual), ["rodape"] = j.TextoFolhaAtual });
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
            var shaPacote = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(pacoteLongo)));
            p["paginas_do_pdf"] = info.Paginas;
            p["folhas_na_tela"] = a.Composicao.Folhas;
            p["pagina_mm"] = $"{info.LarguraMm:0.##} x {info.AlturaMm:0.##}";
            p["recibo_sha256_html_confere"] = recibo.RootElement.GetProperty("sha256_html").GetString() == PlanoQuebras.Sha256De(a.Preparado.Render.Html);
            p["recibo_sha256_pdf_confere"] = recibo.RootElement.GetProperty("sha256_pdf").GetString() == Convert.ToHexStringLower(SHA256.HashData(bytes));
            p["recibo_sha256_pacote_confere"] = recibo.RootElement.TryGetProperty("sha256_pacote", out var sp) && sp.GetString() == shaPacote;
            p["plano_do_pdf_igual_ao_da_tela"] = x.Plano.ParaJson() == a.Composicao.Plano.ParaJson();
            p["aviso_de_sucesso"] = j.InfoMensagem.IsOpen && j.InfoMensagem.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Success;
            p["ok"] = info.Paginas == a.Composicao.Folhas && p["recibo_sha256_html_confere"]!.GetValue<bool>() && p["recibo_sha256_pdf_confere"]!.GetValue<bool>()
                && p["recibo_sha256_pacote_confere"]!.GetValue<bool>() && p["plano_do_pdf_igual_ao_da_tela"]!.GetValue<bool>() && p["aviso_de_sucesso"]!.GetValue<bool>();
        });

        await Passo("6_estouro_recusado", async p =>
        {
            var pacote = Path.Combine(pasta, "estouro.pmvrel");
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "estouro.json"), pacote)) throw new InvalidOperationException("não importou o estouro: " + j.InfoMensagem.Message);
            var a = j.Aberto!;
            p["estouros"] = new JsonArray(a.Composicao.Plano.Estouros.Select(e => (JsonNode)$"folha {e.Folha}, ref {e.Ref}").ToArray());
            p["composicao"] = j.TextoComposicaoAtual;
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
            p["ok"] = a.Composicao.Plano.Estouros.Count > 0 && !j.ExportarHabilitado && j.DicaExportar.StartsWith("A exportação está bloqueada", StringComparison.Ordinal) && p["aviso_de_erro_aberto"]!.GetValue<bool>()
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
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var a = j.Aberto!;
            var antes = await j.Documento!.TextoPorFolhaAsync();
            const string Cores = "({ escuro: matchMedia('(prefers-color-scheme: dark)').matches, fundoDaPagina: getComputedStyle(document.documentElement).backgroundColor, " +
                "corpo: getComputedStyle(document.body).backgroundColor, fundoDaFolha: getComputedStyle(document.querySelector('section.folha')).backgroundColor })";
            j.DefinirTema(ElementTheme.Light);
            await Esperar(700);
            var claro = await Js(j, Cores);
            await CapturarWebAsync(j, Path.Combine(pasta, "07-tema-claro.png"));
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "07-janela-xaml-claro.png"));
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

        // Cópia do curto (sem imagens) na pasta do roteiro: tudo o que este passo grava e apaga fica nela.
        await Passo("8_campos_em_aberto_bloqueiam_exportar", async p =>
        {
            var copia = Path.Combine(pasta, "curto-colchetes.json");
            var no = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(corpus, "curto.json")))!;
            no["capa"]!["requerente"] = "[Nome do requerente]";
            await File.WriteAllTextAsync(copia, no.ToJsonString());
            var pdf = Path.Combine(pasta, "curto-colchetes.pdf");
            File.Delete(pdf);
            try
            {
                if (!await j.ImportarParaAsync(copia, Path.Combine(pasta, "curto-colchetes.pmvrel"))) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
                var a = j.Aberto!;
                p["erros_finais"] = a.Preparado.ErrosFinais.Count;
                p["exportar_habilitado"] = j.ExportarHabilitado;
                p["dica_do_botao"] = j.DicaExportar;
                p["composicao"] = j.TextoComposicaoAtual;
                var (ok, _, erro, excecao) = await j.ExportarParaAsync(pdf);
                p["exportacao_bloqueada"] = !ok && excecao is ConferenciaFinalException;
                p["mensagem"] = erro;
                p["aviso_de_bloqueio_aberto"] = j.InfoBloqueio.IsOpen;
                p["aviso_de_bloqueio_texto"] = j.InfoBloqueio.Message;
                p["ok"] = j.InfoBloqueio.IsOpen && a.Preparado.ErrosFinais.Count > 0 && !j.ExportarHabilitado && j.DicaExportar.StartsWith("A exportação está bloqueada", StringComparison.Ordinal)
                    && p["exportacao_bloqueada"]!.GetValue<bool>() && !File.Exists(pdf);
            }
            finally { File.Delete(copia); }
        });

        await Passo("9_pacote_mudou_recusa_exportar", async p =>
        {
            var pacote = Path.Combine(pasta, "curto-muda.pmvrel");
            var pdf = Path.Combine(pasta, "curto-muda.pdf");
            File.Delete(pdf);
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), pacote)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            // Outro programa grava o mesmo pacote, com o requerente trocado, depois de aberto.
            var lido = Pacote.Ler(pacote);
            lido.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO REQUERENTE DEPOIS DE ABRIR";
            Pacote.Gravar(pacote, lido.Conteudo, lido.Disco);
            var (ok, _, erro, excecao) = await j.ExportarParaAsync(pdf);
            p["recusada"] = !ok && excecao is ExportacaoDivergenteException;
            p["mensagem"] = erro;
            p["nenhum_pdf_gravado"] = !File.Exists(pdf) && !File.Exists(Exportador.CaminhoReciboDe(pdf));
            p["ok"] = p["recusada"]!.GetValue<bool>() && erro == ExportacaoDivergenteException.ArquivoMudou && p["nenhum_pdf_gravado"]!.GetValue<bool>();
        });

        await Passo("10_faixa_por_modulo", async p =>
        {
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            await j.MudarModuloAsync(Modulo.Relatorios);
            var sem = j.ComandosSemManipulador();
            var def = Faixas.Para(Modulo.Relatorios);
            var faixa = j.FaixaAtual;
            p["modulo_atual"] = j.ModuloAtual.ToString();
            p["comandos_sem_manipulador"] = new JsonArray(sem.Select(s => (JsonNode)s).ToArray());
            p["modulos_no_trilho"] = new JsonArray(j.TrilhoAtual.Modulos.Select(m => (JsonNode)m.ToString()).ToArray());
            p["abas"] = new JsonArray(faixa.Definicao!.Abas.Select(a => (JsonNode)a.Nome).ToArray());
            p["aba_aberta"] = faixa.AbaAtual;
            var abasConferem = faixa.Definicao.Modulo == Modulo.Relatorios && faixa.Definicao.Abas.Select(a => a.Nome).SequenceEqual(def.Abas.Select(a => a.Nome));
            var trilhoConfere = j.TrilhoAtual.Modulos.SequenceEqual(Faixas.Disponiveis);
            var ajustesJson = Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo);
            string? AbaLembrada() => JsonNode.Parse(File.ReadAllText(ajustesJson))?["aba_por_modulo"]?["Relatorios"]?.GetValue<string>();
            faixa.EscolherAba("Ajuda");
            await Esperar(200);
            var botoesAjuda = faixa.IdsVisiveis;
            var lembradaAjuda = AbaLembrada();
            faixa.EscolherAba("Arquivo");
            await Esperar(200);
            var botoesArquivo = faixa.IdsVisiveis;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "10-janela-xaml-arquivo.png"));
            faixa.EscolherAba("Exibir");
            await Esperar(200);
            var lembradaExibir = AbaLembrada();
            var painelAntes = j.PainelVisivel;
            await j.ExecutarComandoAsync(ComandosApp.ExibirPainel);
            var painelDepois = j.PainelVisivel;
            await j.ExecutarComandoAsync(ComandosApp.ExibirPainel);
            p["botoes_aba_ajuda"] = new JsonArray(botoesAjuda.Select(s => (JsonNode)s).ToArray());
            p["botoes_aba_arquivo"] = new JsonArray(botoesArquivo.Select(s => (JsonNode)s).ToArray());
            p["aba_lembrada_em_ajustes"] = new JsonArray(lembradaAjuda, lembradaExibir);
            p["painel_alterna"] = painelAntes && !painelDepois && j.PainelVisivel;
            p["ok"] = sem.Count == 0 && abasConferem && trilhoConfere && lembradaAjuda == "Ajuda" && lembradaExibir == "Exibir"
                && botoesAjuda.SequenceEqual(def.Abas[^1].Comandos.Select(c => c.Id)) && botoesArquivo.SequenceEqual(def.Abas[0].Comandos.Select(c => c.Id))
                && p["painel_alterna"]!.GetValue<bool>();
        });

        await Passo("11_reabrir_pacote", async p =>
        {
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
            p["fechado_sem_relatorio"] = j.Sessao is null && j.TextoTituloAtual == "Editor de Relatórios PMVNI" && !j.ExportarHabilitado;
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var a = j.Aberto!;
            p["plano_igual_ao_da_importacao"] = planoDaImportacao is not null && a.Composicao.Plano.ParaJson() == planoDaImportacao;
            p["historico"] = j.Sessao!.Revisao.HistoricoCompleto.Count;
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["titulo_da_barra"] = j.TextoTituloAtual;
            p["ok"] = p["plano_igual_ao_da_importacao"]!.GetValue<bool>() && p["fechado_sem_relatorio"]!.GetValue<bool>()
                && j.TextoEstadoAtual.StartsWith("Salvo", StringComparison.Ordinal) && j.TextoTituloAtual.StartsWith("Proc. ", StringComparison.Ordinal);
        });

        await Passo("12_salvar_duas_vezes_sem_conflito", async p =>
        {
            // Ctrl+S duas vezes (e mais um Salvar direto) ao mesmo tempo: as gravações vão em fila, nunca em conflito consigo mesmas.
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            await Task.WhenAll(j.ExecutarComandoAsync(ComandosApp.ArquivoSalvar), j.ExecutarComandoAsync(ComandosApp.ArquivoSalvar), s.GravarAsync());
            var lido = Pacote.Ler(pacoteLongo);
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["disco_confere"] = lido.Disco.Sha256 == s.Disco.Sha256;
            p["aviso_de_erro"] = j.InfoMensagem.IsOpen && j.InfoMensagem.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
            var pdf = Path.Combine(pasta, "longo-depois-de-salvar.pdf");
            File.Delete(pdf);
            var (ok, _, erro, _) = await j.ExportarParaAsync(pdf);
            p["exporta_depois"] = ok;
            p["erro_ao_exportar"] = erro;
            p["ok"] = s.Estado == EstadoGravacao.Salvo && j.TextoEstadoAtual.StartsWith("Salvo", StringComparison.Ordinal)
                && p["disco_confere"]!.GetValue<bool>() && !p["aviso_de_erro"]!.GetValue<bool>() && ok;
        });

        // ---------- Tarefa 9: revisão dos itens no app (o longo continua aberto, salvo) ----------
        string? id3 = null, id5 = null;
        const string NovaVerificacao = "Verificação refeita pelo roteiro: cota & recuo <conferidos> \"na planta\".";
        const string Justificativa = "Conferido na planta de implantação pelo roteiro.";

        await Passo("13_selecionar_e_editar_no_a4", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            var item = proj.Incluidos[2];
            id3 = item.Id;
            // Clique (CDP) no título do ITEM 03: a página manda "selecionado" e as Propriedades mostram o item.
            var ponto = await PontoDoCampoAsync(j, item.Id, "titulo");
            await CliqueAsync(j, ponto);
            await EsperarAteAsync(() => Task.FromResult(j.Selecionado == item.Id), 3000);
            p["selecionado_pelo_clique"] = j.Selecionado == item.Id;
            p["propriedades_titulo"] = j.TextoPropriedadesTitulo;
            var camposNaTela = (await Js(j, "document.querySelectorAll('section.folha [data-campo]').length")).GetInt32();
            p["campos_marcados_na_tela"] = camposNaTela;
            // Duplo clique abre a edição no A4 (pedirEdicao → editarCampo); Esc cancela (edicaoCancelada → estadoEditor).
            await GarantirTecladoAsync(j);
            await CliqueAsync(j, ponto, cliques: 2);
            var abriu = await EsperarAteAsync(async () => (await Js(j, "!!document.querySelector('[data-pmvni-editando]')")).GetBoolean(), 3000);
            p["duplo_clique_abre_a_edicao"] = abriu;
            await TeclaAsync(j, "Escape", "Escape", 27);
            p["esc_cancela_a_edicao"] = await EsperarAteAsync(async () => !(await Js(j, "!!document.querySelector('[data-pmvni-editando]')")).GetBoolean(), 3000);
            // A edição pelo host (a mesma rota do Enter no A4).
            await j.EditarTextoAsync(item.Id, "verificacao", NovaVerificacao);
            var efetivo = DadosEfetivos.TextoEfetivo(proj, item, "verificacao");
            var folhas = await j.Documento!.TextoPorFolhaAsync();
            var html = j.Aberto!.Preparado.Render.Html;
            p["erros_da_edicao"] = new JsonArray(j.UltimosErros.Select(e => (JsonNode)e).ToArray());
            p["modelo_tem_o_texto_novo"] = efetivo == NovaVerificacao;
            p["folhas_tem_o_texto_novo"] = folhas.Any(f => f.Contains(NovaVerificacao, StringComparison.Ordinal));
            p["data_campo_fora_do_html_preparado"] = !html.Contains("data-campo", StringComparison.Ordinal);
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["lista_marca_alterado"] = j.Itens.FirstOrDefault(i => i.Ref == item.Id)?.Alterado == true;
            // F2: edita o último campo clicado (o título).
            await j.ExecutarComandoAsync(ComandosApp.InicioEditarTexto);
            var campoF2 = await EsperarValorAsync(j, "(() => { const e = document.querySelector('[data-pmvni-editando]'); return e ? e.getAttribute('data-campo') : null; })()", 3000);
            p["f2_edita_o_ultimo_campo_clicado"] = campoF2;
            await TeclaAsync(j, "Escape", "Escape", 27);
            await EsperarAteAsync(async () => !(await Js(j, "!!document.querySelector('[data-pmvni-editando]')")).GetBoolean(), 3000);
            p["ok"] = j.Selecionado == item.Id && j.TextoPropriedadesTitulo == "ITEM 03" && camposNaTela > 0 && abriu
                && efetivo == NovaVerificacao && p["folhas_tem_o_texto_novo"]!.GetValue<bool>() && p["data_campo_fora_do_html_preparado"]!.GetValue<bool>()
                && j.TextoEstadoAtual == "Não salvo" && p["lista_marca_alterado"]!.GetValue<bool>() && campoF2 == "titulo";
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "13-janela-xaml-propriedades.png"));
            await CapturarWebAsync(j, Path.Combine(pasta, "13-tela-editado.png"));
        });

        await Passo("14_situacao_e_justificativa", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var item = s.Revisao.Projeto.Item(id3 ?? throw new InvalidOperationException("passo 13 não rodou"))!;
            var original = item.Situacao.Original!.GetValue<string>();
            await j.AplicarAsync(new DefinirSituacao(item.Id, "ATENDE"));
            var esperado = $"ITEM 03: a situação mudou de {Pmvni.Composicao.Kit.Situacoes.Sit[original].Texto} para ATENDE sem justificativa";
            var erros = j.Aberto!.Preparado.ErrosFinais;
            p["erros_finais"] = new JsonArray(erros.Select(e => (JsonNode)e).ToArray());
            p["mensagem_esperada"] = esperado;
            p["mensagem_exata"] = erros.Contains(esperado);
            p["erros_sem_repeticao"] = erros.Distinct().Count() == erros.Count;
            var habilitadoSem = j.ExportarHabilitado;
            p["exportar_habilitado_sem_justificativa"] = habilitadoSem;
            var pdf = Path.Combine(pasta, "longo-sem-justificativa.pdf");
            File.Delete(pdf);
            var (ok, _, erro, excecao) = await j.ExportarParaAsync(pdf);
            p["exportacao_recusada"] = !ok && excecao is ConferenciaFinalException && !File.Exists(pdf);
            p["mensagem_da_recusa"] = erro;
            var conferencia = j.TextoConferencia();
            await j.ExecutarComandoAsync(ComandosApp.RevisaoConferencia);
            var mostrada = perguntas.Informados.LastOrDefault();
            p["conferencia_mostra_o_erro"] = conferencia.Contains(esperado, StringComparison.Ordinal) && mostrada.Titulo == "Conferência" && mostrada.Texto == conferencia;
            p["propriedades_aviso_de_justificativa"] = j.TextoPropriedadesTitulo;
            await j.AplicarAsync(new DefinirJustificativa(item.Id, Justificativa));
            p["erros_finais_com_justificativa"] = j.Aberto!.Preparado.ErrosFinais.Count;
            p["exportar_habilitado_com_justificativa"] = j.ExportarHabilitado;
            p["ok"] = p["mensagem_exata"]!.GetValue<bool>() && p["erros_sem_repeticao"]!.GetValue<bool>() && !habilitadoSem
                && p["exportacao_recusada"]!.GetValue<bool>() && p["conferencia_mostra_o_erro"]!.GetValue<bool>()
                && j.Aberto!.Preparado.ErrosFinais.Count == 0 && j.ExportarHabilitado && item.Situacao.Justificativa == Justificativa;
        });

        await Passo("15_excluir_e_marca", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            var antes = proj.Incluidos.Count;
            var quinto = proj.Incluidos[4];
            var sexto = proj.Incluidos[5];
            id5 = quinto.Id;
            await j.SelecionarAsync(quinto.Id);
            p["rotulo_antes"] = j.RotuloExcluirRestaurar;
            await j.ExecutarComandoAsync(ComandosApp.InicioExcluirRestaurar);
            p["itens_incluidos"] = proj.Incluidos.Count;
            var rotuloDoSexto = await Js(j, $"(() => {{ const e = document.querySelector('section.folha .card[data-ref=\"{sexto.Id}\"] .ct span.id'); return e ? e.textContent : null; }})()");
            p["rotulo_do_antigo_06"] = rotuloDoSexto.ValueKind == JsonValueKind.String ? rotuloDoSexto.GetString() : null;
            p["aba_excluidos"] = j.TextoAbaExcluidos;
            p["linhas_excluidos"] = j.LinhasExcluidos;
            p["rotulo_depois"] = j.RotuloExcluirRestaurar;
            var marcas = await Js(j, "(() => { const b = [...document.querySelectorAll('#pmvni-camada .pmvni-marca-excl')]; return { n: b.length, textos: b.map(x => x.textContent), dentroDeFolha: b.some(x => !!x.closest('section.folha')) }; })()");
            p["marcas_de_excluido"] = JsonNode.Parse(marcas.GetRawText());
            var textos = marcas.GetProperty("textos").EnumerateArray().Select(x => x.GetString()).ToList();
            p["ok"] = proj.Incluidos.Count == antes - 1 && p["rotulo_do_antigo_06"]?.GetValue<string>() == "ITEM 05"
                && j.TextoAbaExcluidos == "Excluídos (1)" && j.LinhasExcluidos == 1 && textos.Contains("◦ excl. 05")
                && !marcas.GetProperty("dentroDeFolha").GetBoolean() && j.RotuloExcluirRestaurar == "Restaurar item";
            await CapturarWebAsync(j, Path.Combine(pasta, "15-tela-excluido.png"));
        });

        await Passo("16_desfazer_refazer", async p =>
        {
            // Quatro comandos desde a importação (edição, situação, justificativa, exclusão): desfazer 4× volta ao plano da importação.
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var depoisDosComandos = j.Aberto!.Composicao.Plano.ParaJson();
            for (var k = 0; k < 4; k++) await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            var voltou = j.Aberto!.Composicao.Plano.ParaJson() == planoDaImportacao;
            p["plano_igual_ao_da_importacao"] = voltou;
            p["itens_incluidos_desfeito"] = s.Revisao.Projeto.Incluidos.Count;
            var podeDesfazerMais = s.Revisao.PodeDesfazer;
            p["pode_desfazer_mais"] = podeDesfazerMais;
            var item3 = s.Revisao.Projeto.Item(id3!)!;
            var semRevisao = item3.Situacao.Revisada is null && item3.Campos.Values.All(c => c.Revisado is null) && item3.Situacao.Justificativa is null;
            p["item_03_sem_revisao_depois_de_desfazer"] = semRevisao;
            p["item_03_depois_de_desfazer"] = $"revisada={item3.Situacao.Revisada ?? "null"}; justificativa={item3.Situacao.Justificativa ?? "null"}; campos revisados={item3.Campos.Count(c => c.Value.Revisado is not null)}";
            for (var k = 0; k < 4; k++) await j.ExecutarComandoAsync(ComandosApp.EditarRefazer);
            var refeito = j.Aberto!.Composicao.Plano.ParaJson() == depoisDosComandos;
            p["plano_refeito_igual"] = refeito;
            var registros = s.Revisao.HistoricoCompleto;
            p["registros_desfeito"] = registros.Count(r => r.Tipo == "desfeito");
            p["registros_refeito"] = registros.Count(r => r.Tipo == "refeito");
            p["historico_na_tela"] = new JsonArray(j.LinhasHistorico.Take(3).Select(l => (JsonNode)l).ToArray());
            p["linhas_do_historico"] = $"{j.LinhasHistorico.Count} na tela, {registros.Count} no histórico";
            p["item_05_excluido_de_novo"] = s.Revisao.Projeto.Item(id5!)!.Incluido == false;
            p["ok"] = voltou && !podeDesfazerMais && semRevisao && refeito && s.Revisao.Projeto.Item(id5!)!.Incluido == false
                && registros.Count(r => r.Tipo == "desfeito") == 4 && registros.Count(r => r.Tipo == "refeito") == 4
                && j.LinhasHistorico.Count == registros.Count && j.LinhasHistorico[0].Contains("Refeito: ", StringComparison.Ordinal);
        });

        await Passo("17_salvar_reabrir_exportar", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            // Ctrl+S com o foco nas folhas: a página repassa o atalho (mensagem "atalho") e a janela salva.
            await GarantirTecladoAsync(j);
            await TeclaAsync(j, "s", "KeyS", 83, 2);
            var salvou = await EsperarAteAsync(() => Task.FromResult(s.Estado == EstadoGravacao.Salvo), 10000);
            p["ctrl_s_salvou"] = salvou;
            var registrosAntes = s.Revisao.HistoricoCompleto.Count;
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var proj = j.Sessao!.Revisao.Projeto;
            var item3 = proj.Item(id3!)!;
            var reaberto = proj.Item(id5!)!.Incluido == false && item3.Situacao.Revisada == "ATENDE" && item3.Situacao.Justificativa == Justificativa
                && DadosEfetivos.TextoEfetivo(proj, item3, "verificacao") == NovaVerificacao;
            p["alteracoes_reabertas"] = reaberto;
            var historico = j.Sessao.Revisao.HistoricoCompleto;
            var porN = historico.ToDictionary(r => r.N);
            var desfeitosRefeitos = historico.Where(r => r.Tipo is "desfeito" or "refeito").ToList();
            var refsValidas = desfeitosRefeitos.All(r => r.Ref is { } n && porN.TryGetValue(n, out var o) && o.Tipo is not ("desfeito" or "refeito")
                && r.Depois is JsonObject d && d["tipo"]?.GetValue<string>() == o.Tipo);
            p["registros_no_pacote"] = historico.Count;
            p["desfeitos_e_refeitos_com_ref"] = refsValidas && desfeitosRefeitos.Count == 8;
            var pdf = Path.Combine(pasta, "longo-revisado.pdf");
            File.Delete(pdf);
            var (ok, x, erro, _) = await j.ExportarParaAsync(pdf);
            if (!ok || x is null) throw new InvalidOperationException("exportação falhou: " + erro);
            var info = InfoPdf.Ler(await File.ReadAllBytesAsync(pdf));
            p["paginas_do_pdf"] = info.Paginas;
            p["folhas_na_tela"] = j.Aberto!.Composicao.Folhas;
            p["ok"] = salvou && reaberto && historico.Count == registrosAntes && refsValidas && desfeitosRefeitos.Count == 8
                && info.Paginas == j.Aberto.Composicao.Folhas && x.Plano.ParaJson() == j.Aberto.Composicao.Plano.ParaJson();
        });

        await Passo("18_menu_de_contexto_e_atalhos", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            var alvo = proj.Incluidos[9];
            // Botão direito (CDP) sobre o ITEM 10: menuContexto → MenuFlyout com 5 entradas.
            await CliqueAsync(j, await PontoDoCampoAsync(j, alvo.Id, "titulo"), botao: "right");
            var abriu = await EsperarAteAsync(() => Task.FromResult(j.UltimoMenuContexto is { IsOpen: true }), 3000);
            var menu = j.UltimoMenuContexto;
            p["menu_aberto"] = abriu;
            p["menu_entradas"] = new JsonArray((menu?.Items ?? []).Select(i => (JsonNode)(i switch
            {
                Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem sub => sub.Text + " ▸",
                Microsoft.UI.Xaml.Controls.MenuFlyoutItem mi => mi.Text,
                _ => i.GetType().Name,
            })).ToArray());
            var entradas = menu?.Items.Count ?? 0;
            menu?.Hide();
            p["selecionado_pelo_menu"] = j.Selecionado == alvo.Id;
            await Esperar(300);

            // Delete nas folhas (mensagem "atalho"): exclui o selecionado.
            var antes = proj.Incluidos.Count;
            await GarantirTecladoAsync(j);
            await TeclaAsync(j, "Delete", "Delete", 46);
            var excluiu = await EsperarAteAsync(() => Task.FromResult(proj.Incluidos.Count == antes - 1 && !j.Ocupado && !j.Aplicando), 6000);
            p["delete_excluiu"] = excluiu;
            p["selecionado_depois_do_delete"] = j.Selecionado == alvo.Id;
            // Alt+↓: o próximo incluído depois do excluído.
            var proximo = proj.Incluidos.First(i => i.Ordem > alvo.Ordem).Id;
            await TeclaAsync(j, "ArrowDown", "ArrowDown", 40, 1);
            var foi = await EsperarAteAsync(() => Task.FromResult(j.Selecionado == proximo), 3000);
            p["alt_seta_seleciona_o_proximo"] = foi;
            p["selecionado_depois_de_alt_seta"] = j.Selecionado == alvo.Id ? "o excluído" : j.Selecionado == proximo ? "o próximo" : j.Selecionado ?? "nenhum";
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            p["desfeito_o_delete"] = proj.Item(alvo.Id)!.Incluido;

            // Marca "◦ excl. 05": clique abre o flyout com o título e Restaurar.
            var botao = await Js(j, "(() => { const b = document.querySelector('#pmvni-camada .pmvni-marca-excl'); if (!b) return null; b.scrollIntoView({ block: 'center', behavior: 'instant' }); const r = b.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()");
            var flyout = false;
            if (botao.ValueKind == JsonValueKind.Object)
            {
                await Esperar(200);
                botao = await Js(j, "(() => { const r = document.querySelector('#pmvni-camada .pmvni-marca-excl').getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2 }; })()");
                await CliqueAsync(j, (botao.GetProperty("x").GetDouble(), botao.GetProperty("y").GetDouble()));
                flyout = await EsperarAteAsync(() => Task.FromResult(j.UltimoFlyoutExcluido is { IsOpen: true }), 3000);
                j.UltimoFlyoutExcluido?.Hide();
            }
            p["flyout_do_excluido"] = flyout;

            // Revisão e Exibir: Somente alterados filtra a lista; Marcas tira e põe a camada; Propriedades recolhe.
            await j.ExecutarComandoAsync(ComandosApp.RevisaoSomenteAlterados);
            var itensFiltrados = j.Itens.Where(i => !i.Secao).ToList();
            var filtra = itensFiltrados.Count > 0 && itensFiltrados.All(i => i.Alterado);
            await j.ExecutarComandoAsync(ComandosApp.RevisaoSomenteAlterados);
            await j.ExecutarComandoAsync(ComandosApp.RevisaoMarcas);
            await Esperar(300);
            var semMarcas = !(await Js(j, "!!document.getElementById('pmvni-camada')")).GetBoolean();
            await j.ExecutarComandoAsync(ComandosApp.RevisaoMarcas);
            await Esperar(300);
            var comMarcas = (await Js(j, "!!document.getElementById('pmvni-camada')")).GetBoolean();
            await j.ExecutarComandoAsync(ComandosApp.ExibirPropriedades);
            var recolheu = !j.PropriedadesVisivel;
            await j.ExecutarComandoAsync(ComandosApp.ExibirPropriedades);
            p["somente_alterados_filtra"] = filtra;
            p["marcas_alternam"] = semMarcas && comMarcas;
            p["propriedades_recolhe"] = recolheu && j.PropriedadesVisivel;
            p["aceleradores_de_teclado"] = j.AceleradoresDeTeclado;
            var sem = j.AtalhosSemManipulador();
            p["atalhos_sem_manipulador"] = new JsonArray(sem.Select(x => (JsonNode)x).ToArray());
            p["comandos_sem_manipulador"] = new JsonArray(j.ComandosSemManipulador().Select(x => (JsonNode)x).ToArray());
            p["ok"] = abriu && entradas == 5 && j.Selecionado is not null && excluiu && foi && proj.Item(alvo.Id)!.Incluido && flyout
                && filtra && semMarcas && comMarcas && recolheu && j.PropriedadesVisivel && sem.Count == 0 && j.ComandosSemManipulador().Count == 0;
        });

        await Passo("19_latencia", async p =>
        {
            // Mediana de 5 comandos no longo (situação de um item, cada vez outra); só registra (meta 1,5 s, desenho §8.4).
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var item = s.Revisao.Projeto.Incluidos[19];
            var medidas = new List<long>();
            var registros = s.Revisao.HistoricoCompleto.Count;
            for (var k = 0; k < 5; k++)
            {
                var atual = item.Situacao.Revisada ?? item.Situacao.Original!.GetValue<string>();
                var ordem = Faixas.OrdemSituacoes.ToList();
                await j.AplicarAsync(new DefinirSituacao(item.Id, ordem[(ordem.IndexOf(atual) + 1) % ordem.Count]));
                medidas.Add(j.UltimaLatenciaMs);
            }
            var aplicados = s.Revisao.HistoricoCompleto.Count - registros;
            for (var k = 0; k < aplicados; k++) await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            var ordenadas = medidas.Order().ToList();
            p["medidas_ms"] = new JsonArray(medidas.Select(m => (JsonNode)m).ToArray());
            p["latencia_ms"] = ordenadas[ordenadas.Count / 2];
            p["meta_ms"] = 1500;
            p["dentro_da_meta"] = ordenadas[ordenadas.Count / 2] <= 1500;
            p["ok"] = aplicados == 5;
        });

        await Passo("20_comandos_em_sequencia", async p =>
        {
            // Justificativa (como ao sair da caixa) e, sem esperar, Situação e Excluir pela faixa; depois uma situação e, sem esperar, um
            // "editar" da página. Nada é descartado: tudo entra na fila e é aplicado na ordem, cada um testado contra o projeto de então.
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            var item = proj.Incluidos[10];
            var outro = proj.Incluidos[11];
            await j.SelecionarAsync(item.Id);
            var registros = s.Revisao.HistoricoCompleto.Count;
            var atual = item.Situacao.Revisada ?? item.Situacao.Original!.GetValue<string>();
            var nova = Faixas.OrdemSituacoes.First(c => c != atual);
            var t1 = j.AplicarAsync(new DefinirJustificativa(item.Id, "Justificativa digitada antes de mudar a situação."));
            var t2 = j.ExecutarComandoAsync(ComandosApp.ComandoDaSituacao(nova));
            var t3 = j.ExecutarComandoAsync(ComandosApp.InicioExcluirRestaurar);
            p["na_fila_ao_mesmo_tempo"] = j.ComandosNaFila;
            await Task.WhenAll(t1, t2, t3);
            var tipos = s.Revisao.HistoricoCompleto.Skip(registros).Select(r => r.Tipo).ToList();
            p["registros_em_ordem"] = new JsonArray(tipos.Select(t => (JsonNode)t).ToArray());
            var primeiros = tipos.SequenceEqual(["DefinirJustificativa", "DefinirSituacao", "Excluir"])
                && item.Situacao.Revisada == nova && item.Situacao.Justificativa is not null && !item.Incluido;

            var atualOutro = outro.Situacao.Revisada ?? outro.Situacao.Original!.GetValue<string>();
            var novaOutro = Faixas.OrdemSituacoes.First(c => c != atualOutro);
            const string TextoNaFila = "Texto digitado no A4 enquanto outra alteração era aplicada.";
            var t4 = j.AplicarAsync(new DefinirSituacao(outro.Id, novaOutro));
            var t5 = j.EditarTextoAsync(outro.Id, "providencia", TextoNaFila);
            await Task.WhenAll(t4, t5);
            var depois = s.Revisao.HistoricoCompleto.Skip(registros).Select(r => r.Tipo).ToList();
            var edicaoEntrou = outro.Situacao.Revisada == novaOutro && DadosEfetivos.TextoEfetivo(proj, outro, "providencia") == TextoNaFila
                && depois.Skip(3).SequenceEqual(["DefinirSituacao", "DefinirCampo"]);
            var folhas = await j.Documento!.TextoPorFolhaAsync();
            p["edicao_durante_outra_entrou"] = edicaoEntrou;
            p["tela_tem_o_texto"] = folhas.Any(f => f.Contains(TextoNaFila, StringComparison.Ordinal));
            var aplicados = s.Revisao.HistoricoCompleto.Count - registros;
            for (var k = 0; k < aplicados; k++) await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            p["desfeito_tudo"] = item.Incluido && item.Situacao.Revisada is null && outro.Situacao.Revisada is null
                && outro.Campos["providencia"].Revisado is null;
            p["ok"] = primeiros && edicaoEntrou && p["tela_tem_o_texto"]!.GetValue<bool>() && aplicados == 5 && p["desfeito_tudo"]!.GetValue<bool>();
        });

        await j.FecharSessaoAsync();
        saida["perguntas_feitas"] = new JsonArray(perguntas.Feitas.Select(s => (JsonNode)s).ToArray());
        saida["falhas"] = falhas;
        saida["ok"] = falhas.Count == 0;
        Gravar(args.Roteiro!, saida);
        Application.Current.Exit();
    }

    /// <summary>Centro (um pouco à esquerda, no texto) do campo <paramref name="campo"/> do item, depois de rolar até ele.</summary>
    static async Task<(double X, double Y)> PontoDoCampoAsync(MainWindow j, string item, string campo)
    {
        var achar = $"[...document.querySelectorAll('section.folha .card[data-ref=\"{item}\"] [data-campo=\"{campo}\"]')][0]";
        var ok = await Js(j, $"(() => {{ const e = {achar}; if (!e) return false; e.scrollIntoView({{ block: 'center', behavior: 'instant' }}); return true; }})()");
        if (ok.ValueKind != JsonValueKind.True) throw new InvalidOperationException($"o campo {campo} do item {item} não está nas folhas");
        await Esperar(250);
        var r = await Js(j, $"(() => {{ const r = {achar}.getBoundingClientRect(); return {{ x: r.left + Math.min(r.width / 2, 60), y: r.top + Math.min(r.height / 2, 9) }}; }})()");
        return (r.GetProperty("x").GetDouble(), r.GetProperty("y").GetDouble());
    }

    static async Task CliqueAsync(MainWindow j, (double X, double Y) ponto, int cliques = 1, string botao = "left")
    {
        var mascara = botao == "right" ? 2 : 1;
        await Cdp(j, "Input.dispatchMouseEvent", new { type = "mouseMoved", x = ponto.X, y = ponto.Y, button = "none", buttons = 0 });
        for (var n = 1; n <= cliques; n++)
        {
            await Cdp(j, "Input.dispatchMouseEvent", new { type = "mousePressed", x = ponto.X, y = ponto.Y, button = botao, buttons = mascara, clickCount = n });
            await Cdp(j, "Input.dispatchMouseEvent", new { type = "mouseReleased", x = ponto.X, y = ponto.Y, button = botao, buttons = 0, clickCount = n });
        }
        await Esperar(150);
    }

    /// <summary>Tecla pelo CDP (modificadores: 1 Alt, 2 Ctrl, 8 Shift).</summary>
    static async Task TeclaAsync(MainWindow j, string key, string code, int vk, int modificadores = 0)
    {
        await Cdp(j, "Input.dispatchKeyEvent", new { type = "rawKeyDown", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores });
        await Cdp(j, "Input.dispatchKeyEvent", new { type = "keyUp", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores });
    }

    /// <summary>Foco nas folhas e o teclado chegando à página: manda Shift (fora da lista de atalhos, sem efeito) até ela registrar um keydown.</summary>
    static async Task GarantirTecladoAsync(MainWindow j)
    {
        j.FocarFolhas();
        await Js(j, "window.__pmvniTeclas = 0; if (!window.__pmvniEscuta) { window.__pmvniEscuta = true; addEventListener('keydown', () => { window.__pmvniTeclas++; }, true); } 1");
        for (var k = 0; k < 40; k++)
        {
            await TeclaAsync(j, "Shift", "ShiftLeft", 16, 8);
            await Esperar(50);
            if ((await Js(j, "window.__pmvniTeclas")).GetInt32() > 0) return;
        }
        throw new InvalidOperationException("O teclado não chegou à página (40 tentativas com Shift).");
    }

    static Task Cdp(MainWindow j, string metodo, object parametros) =>
        j.NucleoWeb.CallDevToolsProtocolMethodAsync(metodo, JsonSerializer.Serialize(parametros)).AsTask();

    static async Task<bool> EsperarAteAsync(Func<Task<bool>> condicao, int ms)
    {
        var fim = DateTime.UtcNow.AddMilliseconds(ms);
        while (true)
        {
            if (await condicao()) return true;
            if (DateTime.UtcNow > fim) return false;
            await Esperar(50);
        }
    }

    static async Task<string?> EsperarValorAsync(MainWindow j, string expressao, int ms)
    {
        string? valor = null;
        await EsperarAteAsync(async () => { var v = await Js(j, expressao); valor = v.ValueKind == JsonValueKind.String ? v.GetString() : null; return valor is not null; }, ms);
        return valor;
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
