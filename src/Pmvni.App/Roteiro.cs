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
    /// <summary>A pasta que o seletor de pastas "escolhe" (null = cancelado).</summary>
    public string? PastaEscolhida { get; set; }
    public Task<string?> EscolherPastaAsync() { Feitas.Add("escolher pasta"); return Task.FromResult(PastaEscolhida); }
    public Task<bool> ConfirmarAsync(string titulo, string texto, string sim, string nao) { Feitas.Add("confirmar " + titulo); return Task.FromResult(Confirmar); }
    public Task InformarAsync(string titulo, string texto) { Feitas.Add("informar " + titulo); Informados.Add((titulo, texto)); return Task.CompletedTask; }
    public List<(string Titulo, string Texto)> Informados { get; } = [];
    public Task<RespostaRecuperacao> RecuperarAsync(DateTimeOffset ultimo, int comandos) { Feitas.Add($"recuperar {comandos}"); return Task.FromResult(Recuperacao); }
    public RespostaFechar Fechar { get; set; } = RespostaFechar.FecharSemSalvar;
    public Task<RespostaFechar> FecharSemGravarAsync(string motivo, bool conflito)
    {
        Feitas.Add("fechar sem gravar" + (conflito ? " (conflito)" : "") + ": " + motivo);
        Fechamentos.Add(TextosGravacao.FecharSemGravar(motivo, conflito));
        return Task.FromResult(Fechar);
    }
    public List<string> Fechamentos { get; } = [];
}

/// <summary>Modo de diagnóstico <c>--roteiro &lt;saida.json&gt;</c>: exercita o shell e o módulo Relatórios pelos mesmos caminhos dos
/// comandos da faixa (importar, rolagem, zoom, navegação, exportação, corpus com estouro, tema escuro, campos em aberto, pacote mudado no
/// disco, faixa por módulo, reabrir o pacote; Tarefa 9: seleção e edição no A4, situação e justificativa, exclusão e marca, desfazer e
/// refazer, salvar/reabrir/exportar, menu de contexto e atalhos, latência; Tarefa 10: salvamento automático, gravação que falha sem
/// perder nada, recuperação depois de uma queda, trava e somente leitura, conflito sem sobrescrever; Tarefa 11: pesquisa no relatório,
/// tema do app, Recentes e Comparar com o original; Tarefa 13: módulos Processos e Despachos sobre uma cópia da árvore fictícia, pasta de processos
/// nos Ajustes, faixa e pesquisa por módulo, pastas intactas) e grava o resultado em JSON. Tudo o que o roteiro cria (pacotes, PDFs, capturas PNG) fica
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
        // Progresso em <pasta>\progresso.log (uma linha por etapa que começa e que termina): um roteiro que trava mostra onde.
        var progresso = Path.Combine(pasta, "progresso.log");
        File.WriteAllText(progresso, "");
        void Registrar(string linha) { try { File.AppendAllText(progresso, DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " " + linha + Environment.NewLine); } catch (IOException) { } }
        Registrar("início da limpeza das rodadas anteriores");
        var perguntas = new PerguntasFixas();
        Perguntas.Atual = perguntas;
        // Tarefa 13: Processos e Despachos leem uma CÓPIA da árvore fictícia (nunca a pasta real de processos) e nenhum arquivo é aberto fora
        // do app (as aberturas só são registradas). A cópia e a "fotografia" dela (caminho → sha256) são feitas antes de qualquer módulo.
        AberturaExterna.Simular = true;
        var copiaProcessos = Path.Combine(pasta, "processos");
        if (Directory.Exists(copiaProcessos)) ApagarArvore(copiaProcessos);
        CopiarArvore(Path.Combine(corpus, "processos"), copiaProcessos);
        var antesDasPastas = FotografarArvore(copiaProcessos);
        j.DefinirPastaProcessos(copiaProcessos);
        var pacoteLongo = Path.Combine(pasta, "longo.pmvrel");
        // Pacotes de rodadas anteriores (só os do roteiro, só nesta pasta), com travas, cópias de conflito e diários de recuperação
        // (na pasta de dados do roteiro, tests\saida\dados-app): a importação nunca sobrescreve e nada de antes vira "recuperação".
        var recuperacao = Path.Combine(PastaDados.Raiz, "recuperacao");
        foreach (var nome in new[] { "longo", "estouro", "curto-colchetes", "curto-muda", "curto-auto", "recentes-a", "recentes-b", "curto-citacoes" })
        {
            var arquivos = Directory.GetFiles(pasta, nome + ".pmvrel*").Concat(Directory.GetFiles(pasta, ".~" + nome + ".pmvrel*"))
                .Concat(Directory.GetFiles(pasta, nome + " (cópia *")).Concat(Directory.GetFiles(pasta, ".~" + nome + " (cópia *")).ToList();
            // ToList: o laço abaixo acrescenta a "arquivos" (uma consulta preguiçosa sobre ela falharia numa rodada com restos da anterior).
            var pacotes = arquivos.Where(a => a.EndsWith(".pmvrel", StringComparison.OrdinalIgnoreCase)).Append(Path.Combine(pasta, nome + ".pmvrel")).ToList();
            if (Directory.Exists(recuperacao))
                foreach (var pc in pacotes)
                    arquivos.AddRange(Directory.GetFiles(recuperacao, Path.GetFileNameWithoutExtension(Diario.CaminhoDe(PastaDados.Raiz, pc)) + "*"));
            foreach (var a in arquivos.Distinct()) { File.SetAttributes(a, FileAttributes.Normal); File.Delete(a); }
        }
        // Tarefa 10: salvamento automático com 15 s nos ajustes do roteiro (o menor intervalo permitido).
        Registrar("limpeza concluída");
        j.DefinirIntervaloAutosave(15);
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
            Registrar("início " + nome);
            try { await corpoPasso(p); p["ok"] ??= true; }
            catch (Exception e) { p["ok"] = false; p["excecao"] = e.GetType().Name + ": " + e.Message; }
            passos[nome] = p;
            if (p["ok"]?.GetValue<bool>() != true) falhas.Add(nome);
            Registrar("fim " + nome + (p["ok"]?.GetValue<bool>() == true ? " ok" : " FALHOU"));
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

        // ---------- Tarefa 10: salvamento automático, gravação que falha, recuperação, trava e conflito (no curto) ----------
        var pacoteAuto = Path.Combine(pasta, "curto-auto.pmvrel");
        var raizDados = PastaDados.Raiz;
        string SituacaoEfetiva(ItemRevisao it) => it.Situacao.Revisada ?? it.Situacao.Original!.GetValue<string>();

        await Passo("21_autosave", async p =>
        {
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), pacoteAuto)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var item = s.Revisao.Projeto.Incluidos[0];
            var nova = Faixas.OrdemSituacoes.First(c => c != SituacaoEfetiva(item));
            var relogio = System.Diagnostics.Stopwatch.StartNew();
            await j.AplicarAsync(new DefinirSituacao(item.Id, nova));
            var reg = s.Revisao.HistoricoCompleto[^1];
            p["estado_depois_do_comando"] = j.TextoEstadoAtual;
            var naoSalvo = s.Estado == EstadoGravacao.NaoSalvo;
            p["diario_tem_o_registro"] = Diario.Ler(raizDados, pacoteAuto, s.Disco.Sha256)?.Registros.Any(r => r.N == reg.N) == true;
            var salvou = await EsperarAteAsync(() => Task.FromResult(s.Estado == EstadoGravacao.Salvo), 25000);
            var segundos = relogio.Elapsed.TotalSeconds;
            p["salvo_sozinho_em_s"] = Math.Round(segundos, 1);
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            var lido = Pacote.Ler(pacoteAuto);
            p["pacote_tem_o_registro"] = lido.Conteudo.Historico.Any(r => r.N == reg.N && r.Tipo == reg.Tipo);
            var linhas = LinhasDoDiario(Diario.CaminhoDe(raizDados, pacoteAuto));
            p["linhas_do_diario"] = linhas.Length;
            var soCabecalho = linhas.Length == 1 && linhas[0].Contains("\"cabecalho\"", StringComparison.Ordinal) && linhas[0].Contains(lido.Disco.Sha256, StringComparison.Ordinal);
            p["diario_so_com_o_cabecalho_do_sha_novo"] = soCabecalho;
            p["ok"] = naoSalvo && p["diario_tem_o_registro"]!.GetValue<bool>() && salvou && segundos >= 14 && j.TextoEstadoAtual.StartsWith("Salvo ", StringComparison.Ordinal)
                && p["pacote_tem_o_registro"]!.GetValue<bool>() && soCabecalho;
        });

        await Passo("22_gravacao_falha_sem_perder", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var item = s.Revisao.Projeto.Incluidos[0];
            var antes = await File.ReadAllBytesAsync(pacoteAuto);
            await j.AplicarAsync(new DefinirJustificativa(item.Id, "Justificativa gravada depois de soltar o arquivo."));
            var reg = s.Revisao.HistoricoCompleto[^1];
            bool gravouPreso, igual, erroVisivel, naoSalvo, diarioTem, semTmp;
            // O roteiro segura o arquivo (como o OneDrive ou o antivírus): a gravação falha antes da troca.
            using (var preso = new FileStream(pacoteAuto, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                gravouPreso = await j.GravarAgoraAsync();
                naoSalvo = j.TextoEstadoAtual == "Não salvo";
                erroVisivel = j.InfoMensagem.IsOpen && j.InfoMensagem.Severity == Microsoft.UI.Xaml.Controls.InfoBarSeverity.Error;
                p["mensagem_de_erro"] = j.InfoMensagem.Title + ": " + j.InfoMensagem.Message;
                using var ms = new MemoryStream();
                preso.CopyTo(ms);
                igual = ms.ToArray().AsSpan().SequenceEqual(antes);
                semTmp = !File.Exists(pacoteAuto + ".tmp");
                diarioTem = Diario.Ler(raizDados, pacoteAuto, s.Disco.Sha256)?.Registros.Any(r => r.N == reg.N) == true;
            }
            p["gravou_com_o_arquivo_preso"] = gravouPreso;
            p["estado_nao_salvo"] = naoSalvo;
            p["infobar_de_erro"] = erroVisivel;
            p["pacote_em_disco_igual_ao_de_antes"] = igual;
            p["sem_tmp_deixado"] = semTmp;
            p["diario_tem_o_registro"] = diarioTem;
            var gravouSolto = await j.GravarAgoraAsync();
            var lido = Pacote.Ler(pacoteAuto);
            p["solto_grava"] = gravouSolto;
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["pacote_tem_o_registro"] = lido.Conteudo.Historico.Any(r => r.N == reg.N);
            p["ok"] = !gravouPreso && naoSalvo && erroVisivel && igual && semTmp && diarioTem && gravouSolto && s.Estado == EstadoGravacao.Salvo
                && p["pacote_tem_o_registro"]!.GetValue<bool>();
        });

        await Passo("23_recuperacao_apos_queda", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            var proj = s.Revisao.Projeto;
            await j.AplicarAsync(new DefinirCampo(proj.Incluidos[1].Id, "titulo", "Título alterado antes da queda"));
            await j.AplicarAsync(new DefinirCampo(proj.Incluidos[2].Id, "verificacao", "Verificação alterada antes da queda."));
            var projetoAntes = Convert.ToHexString(ProjetoJson.Escrever(proj));
            var registros = s.Revisao.HistoricoCompleto.Count;
            j.SimularQuedaParaRoteiro();
            p["trava_solta"] = !File.Exists(Trava.CaminhoDe(pacoteAuto));
            p["diario_ficou_com"] = Diario.Ler(raizDados, pacoteAuto, EstadoDisco.Ler(pacoteAuto).Sha256)?.Registros.Count ?? 0;
            perguntas.Recuperacao = RespostaRecuperacao.Recuperar;
            var feitas = perguntas.Feitas.Count;
            try
            {
                if (!await j.AbrirPacoteAsync(pacoteAuto)) throw new InvalidOperationException("não reabriu: " + j.InfoMensagem.Message);
            }
            finally { perguntas.Recuperacao = RespostaRecuperacao.Descartar; }
            var pergunta = perguntas.Feitas.Skip(feitas).FirstOrDefault(f => f.StartsWith("recuperar", StringComparison.Ordinal));
            var r = j.Sessao!;
            var mesmo = Convert.ToHexString(ProjetoJson.Escrever(r.Revisao.Projeto)) == projetoAntes;
            p["pergunta"] = pergunta;
            p["mesmo_projeto_json"] = mesmo;
            p["historico_igual"] = r.Revisao.HistoricoCompleto.Count == registros;
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["aviso"] = j.InfoMensagem.Message;
            var diarioNovo = Diario.Ler(raizDados, pacoteAuto, r.Disco.Sha256);
            p["diario_novo_com_os_pendentes"] = diarioNovo?.Registros.Count ?? 0;
            var naoSalvo = r.Estado == EstadoGravacao.NaoSalvo;
            var gravou = await j.GravarAgoraAsync();
            p["gravado_depois"] = gravou;
            p["ok"] = pergunta == "recuperar 2" && mesmo && naoSalvo && p["diario_ficou_com"]!.GetValue<int>() == 2 && p["trava_solta"]!.GetValue<bool>()
                && diarioNovo?.Registros.Count == 2 && p["historico_igual"]!.GetValue<bool>() && gravou;
        });

        await Passo("24_trava_e_somente_leitura", async p =>
        {
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
            var trava = Trava.CaminhoDe(pacoteAuto);
            p["trava_solta_ao_fechar"] = !File.Exists(trava);
            var inicio = DateTimeOffset.UtcNow.AddMinutes(-10);
            // Outra instância viva, em outra máquina, com batimento agora.
            EscreverTrava(trava, "OUTRA-MAQUINA", "outro.usuario", 4242, inicio, DateTimeOffset.UtcNow);
            if (!await j.AbrirPacoteAsync(pacoteAuto)) throw new InvalidOperationException("não abriu: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var esperado = $"Aberto por outro.usuario em OUTRA-MAQUINA desde {inicio.ToLocalTime():HH:mm}: somente leitura.";
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            p["aviso"] = j.InfoSomenteLeitura.Message;
            var aviso = j.InfoSomenteLeitura.IsOpen && j.InfoSomenteLeitura.Message == esperado;
            await j.SelecionarAsync(s.Revisao.Projeto.Incluidos[0].Id);
            var excluirSomente = j.FaixaAtual.Habilitado(ComandosApp.InicioExcluirRestaurar);
            var salvarSomente = j.FaixaAtual.Habilitado(ComandosApp.ArquivoSalvar);
            var registros = s.Revisao.HistoricoCompleto.Count;
            await j.ExecutarComandoAsync(ComandosApp.InicioExcluirRestaurar);
            var nadaMudou = s.Revisao.HistoricoCompleto.Count == registros && s.Revisao.Projeto.Incluidos.Count == 3;
            var somenteLeitura = j.TextoEstadoAtual == "Somente leitura";
            p["excluir_habilitado"] = excluirSomente;
            p["salvar_habilitado"] = salvarSomente;
            p["nada_mudou"] = nadaMudou;
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou o somente leitura");
            var doOutro = LerPidDaTrava(trava) == 4242;
            p["trava_do_outro_intacta"] = doOutro;

            // Batimento de 3 min atrás: abandonada, retomada; abre editável.
            EscreverTrava(trava, "OUTRA-MAQUINA", "outro.usuario", 4242, inicio, DateTimeOffset.UtcNow.AddMinutes(-3));
            if (!await j.AbrirPacoteAsync(pacoteAuto)) throw new InvalidOperationException("não abriu: " + j.InfoMensagem.Message);
            var e = j.Sessao!;
            p["estado_com_trava_abandonada"] = j.TextoEstadoAtual;
            await j.SelecionarAsync(e.Revisao.Projeto.Incluidos[0].Id);
            var excluirEditavel = j.FaixaAtual.Habilitado(ComandosApp.InicioExcluirRestaurar);
            var nossa = LerPidDaTrava(trava) == Environment.ProcessId;
            p["excluir_habilitado_editavel"] = excluirEditavel;
            p["trava_retomada_por_este_app"] = nossa;
            var editavel = !e.SomenteLeitura && j.TextoEstadoAtual.StartsWith("Salvo", StringComparison.Ordinal) && excluirEditavel && nossa && !j.InfoSomenteLeitura.IsOpen;

            // Trava perdida com o arquivo aberto (outro programa a apagou): o batimento vira somente leitura; fechar sem salvar guarda a
            // recuperação, e reabrir a oferece.
            const string TituloAntesDePerder = "Título feito antes de perder a trava";
            var alvo = e.Revisao.Projeto.Incluidos[1];
            await j.AplicarAsync(new DefinirCampo(alvo.Id, "titulo", TituloAntesDePerder));
            File.SetAttributes(trava, FileAttributes.Normal);
            File.Delete(trava);
            var bateu = await e.BaterAsync();
            var perdida = !bateu && e.SomenteLeitura && j.TextoEstadoAtual == "Somente leitura" && j.InfoSomenteLeitura.IsOpen
                && j.InfoSomenteLeitura.Message == TextosGravacao.TravaPerdida && !j.FaixaAtual.Habilitado(ComandosApp.InicioExcluirRestaurar);
            p["trava_perdida_vira_somente_leitura"] = perdida;
            // Exportar com alterações que o arquivo não tem (e não pode receber): recusado, apontando Salvar como.
            var pdfSemTrava = Path.Combine(pasta, "curto-auto-sem-trava.pdf");
            File.Delete(pdfSemTrava);
            var (exportouSemTrava, _, erroSemTrava, excSemTrava) = await j.ExportarParaAsync(pdfSemTrava);
            var exportarRecusado = !exportouSemTrava && excSemTrava is ExportacaoDivergenteException && erroSemTrava == TextosGravacao.ExportarSemGravar(false)
                && !File.Exists(pdfSemTrava) && !File.Exists(Exportador.CaminhoReciboDe(pdfSemTrava));
            p["exportar_sem_trava_recusado"] = exportarRecusado;
            p["mensagem_exportar_sem_trava"] = erroSemTrava;
            var feitas = perguntas.Feitas.Count;
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou depois de perder a trava");
            var perguntouAoFechar = perguntas.Feitas.Skip(feitas).Any(f => f.StartsWith("fechar sem gravar", StringComparison.Ordinal));
            p["perguntou_ao_fechar"] = perguntouAoFechar;
            perguntas.Recuperacao = RespostaRecuperacao.Recuperar;
            try { if (!await j.AbrirPacoteAsync(pacoteAuto)) throw new InvalidOperationException("não reabriu: " + j.InfoMensagem.Message); }
            finally { perguntas.Recuperacao = RespostaRecuperacao.Descartar; }
            var pergunta = perguntas.Feitas.Skip(feitas).FirstOrDefault(f => f.StartsWith("recuperar", StringComparison.Ordinal));
            var rec = j.Sessao!;
            var recuperou = pergunta == "recuperar 1" && rec.Estado == EstadoGravacao.NaoSalvo
                && DadosEfetivos.TextoEfetivo(rec.Revisao.Projeto, rec.Revisao.Projeto.Item(alvo.Id)!, "titulo") == TituloAntesDePerder;
            p["recuperou_depois_de_fechar_sem_salvar"] = recuperou;
            var gravou = await j.GravarAgoraAsync();
            p["ok"] = p["trava_solta_ao_fechar"]!.GetValue<bool>() && somenteLeitura && aviso && !excluirSomente && !salvarSomente && nadaMudou && doOutro
                && editavel && perdida && exportarRecusado && perguntouAoFechar && recuperou && gravou;
        });

        await Passo("25_conflito_nao_sobrescreve", async p =>
        {
            var s = j.Sessao ?? throw new InvalidOperationException("nenhum relatório aberto");
            if (s.SomenteLeitura) throw new InvalidOperationException("aberto somente para leitura");
            // Outro programa grava o mesmo pacote depois de aberto.
            var outro = Pacote.Ler(pacoteAuto);
            outro.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO PROGRAMA GRAVOU DEPOIS DE ABRIR";
            Pacote.Gravar(pacoteAuto, outro.Conteudo, outro.Disco);
            var bytesDoOutro = await File.ReadAllBytesAsync(pacoteAuto);
            const string Titulo = "Título alterado antes do conflito";
            var item = s.Revisao.Projeto.Incluidos[0];
            await j.AplicarAsync(new DefinirCampo(item.Id, "titulo", Titulo));
            var reg = s.Revisao.HistoricoCompleto[^1];
            var gravou = await j.GravarAgoraAsync();
            p["gravou"] = gravou;
            p["estado_de_gravacao"] = j.TextoEstadoAtual;
            var conflito = j.TextoEstadoAtual == "Conflito" && j.InfoConflito.IsOpen;
            p["infobar_conflito"] = j.InfoConflito.IsOpen;
            var intacto = (await File.ReadAllBytesAsync(pacoteAuto)).AsSpan().SequenceEqual(bytesDoOutro);
            p["disco_com_os_bytes_do_outro"] = intacto;
            var pdf = Path.Combine(pasta, "curto-auto-conflito.pdf");
            File.Delete(pdf);
            var (exportou, _, erroExportar, excecao) = await j.ExportarParaAsync(pdf);
            var exportarRecusa = !exportou && excecao is ExportacaoDivergenteException && !File.Exists(pdf) && erroExportar == TextosGravacao.ExportarSemGravar(true);
            p["exportar_recusa"] = exportarRecusa;
            p["erro_ao_exportar"] = erroExportar;

            var copiou = await j.SalvarCopiaDeConflitoAsync();
            var copia = s.Caminho;
            var lidaCopia = Pacote.Ler(copia);
            var copiaConfere = copiou && !string.Equals(copia, pacoteAuto, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(copia).StartsWith("curto-auto (cópia ", StringComparison.Ordinal)
                && lidaCopia.Conteudo.Historico.Any(r => r.N == reg.N)
                && DadosEfetivos.TextoEfetivo(lidaCopia.Conteudo.Projeto, lidaCopia.Conteudo.Projeto.Item(item.Id)!, "titulo") == Titulo;
            p["copia"] = Path.GetFileName(copia);
            p["copia_tem_as_alteracoes"] = copiaConfere;
            var originalIntacto = (await File.ReadAllBytesAsync(pacoteAuto)).AsSpan().SequenceEqual(bytesDoOutro);
            p["original_continua_o_do_outro"] = originalIntacto;
            p["estado_depois_da_copia"] = j.TextoEstadoAtual;
            var travas = !File.Exists(Trava.CaminhoDe(pacoteAuto)) && LerPidDaTrava(Trava.CaminhoDe(copia)) == Environment.ProcessId;
            p["trava_passou_para_a_copia"] = travas;
            var copiaOk = copiaConfere && originalIntacto && s.Estado == EstadoGravacao.Salvo && !j.InfoConflito.IsOpen && travas;

            // Recarregar: outro programa muda a cópia; a alteração desta sessão vai para um órfão do diário, nunca é apagada.
            var outra = Pacote.Ler(copia);
            outra.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO PROGRAMA DE NOVO";
            Pacote.Gravar(copia, outra.Conteudo, outra.Disco);
            await j.AplicarAsync(new DefinirCampo(s.Revisao.Projeto.Incluidos[1].Id, "titulo", "Título que o Recarregar guarda no órfão"));
            var conflitoDeNovo = !await j.GravarAgoraAsync() && s.Estado == EstadoGravacao.Conflito;
            var prefixo = Path.GetFileNameWithoutExtension(Diario.CaminhoDe(raizDados, copia));
            int Orfaos() => Directory.GetFiles(Path.Combine(raizDados, "recuperacao"), prefixo + ".orfao-*").Length;
            var orfaosAntes = Orfaos();
            var recarregou = await j.RecarregarAsync();
            var r = j.Sessao;
            var requerente = r?.Revisao.Projeto.Documento["capa"]?["requerente"]?.GetValue<string>();
            p["recarregou"] = recarregou;
            p["requerente_depois_de_recarregar"] = requerente;
            p["orfao_guardado"] = Orfaos() == orfaosAntes + 1;
            var recarregarOk = conflitoDeNovo && recarregou && requerente == "OUTRO PROGRAMA DE NOVO" && r!.Estado == EstadoGravacao.Salvo
                && Orfaos() == orfaosAntes + 1 && !j.InfoConflito.IsOpen;
            // Fechar em conflito: a pergunta não promete a recuperação (o arquivo é outra versão) e a saída principal é Salvar como cópia.
            var aberto = r!.Caminho;
            var terceiro = Pacote.Ler(aberto);
            terceiro.Conteudo.Projeto.Documento["capa"]!["requerente"] = "OUTRO PROGRAMA PELA TERCEIRA VEZ";
            Pacote.Gravar(aberto, terceiro.Conteudo, terceiro.Disco);
            const string TituloAoFechar = "Título salvo na cópia ao fechar em conflito";
            await j.AplicarAsync(new DefinirCampo(r.Revisao.Projeto.Incluidos[2].Id, "titulo", TituloAoFechar));
            var regFechar = r.Revisao.HistoricoCompleto[^1];
            var conflitoAoFechar = !await j.GravarAgoraAsync() && r.Estado == EstadoGravacao.Conflito;
            var feitas = perguntas.Feitas.Count;
            var copiasAntes = Directory.GetFiles(pasta, Path.GetFileNameWithoutExtension(aberto) + " (cópia *.pmvrel").ToHashSet();
            perguntas.Fechar = RespostaFechar.SalvarCopia;
            bool fechou;
            try { fechou = await j.FecharSessaoAsync(); }
            finally { perguntas.Fechar = RespostaFechar.FecharSemSalvar; }
            var perguntaConflito = perguntas.Feitas.Skip(feitas).FirstOrDefault(f => f.StartsWith("fechar sem gravar", StringComparison.Ordinal));
            var textoConflito = perguntas.Fechamentos.LastOrDefault() ?? "";
            var novaCopia = Directory.GetFiles(pasta, Path.GetFileNameWithoutExtension(aberto) + " (cópia *.pmvrel").Where(c => !copiasAntes.Contains(c)).ToList();
            var copiaAoFechar = novaCopia.Count == 1 && Pacote.Ler(novaCopia[0]).Conteudo.Historico.Any(x => x.N == regFechar.N);
            p["pergunta_ao_fechar_em_conflito"] = perguntaConflito;
            p["texto_ao_fechar_em_conflito"] = textoConflito;
            p["copia_ao_fechar"] = novaCopia.Count == 1 ? Path.GetFileName(novaCopia[0]) : null;
            var fecharOk = conflitoAoFechar && fechou && j.Sessao is null && perguntaConflito?.StartsWith("fechar sem gravar (conflito)", StringComparison.Ordinal) == true
                && !textoConflito.Contains("oferece recuperá-las", StringComparison.Ordinal) && textoConflito.Contains("não serão oferecidas de novo", StringComparison.Ordinal)
                && copiaAoFechar;
            p["fechar_em_conflito_salva_copia"] = fecharOk;
            p["ok"] = !gravou && conflito && intacto && exportarRecusa && copiaOk && recarregarOk && fecharOk;
        });

        // ---------- Tarefa 11: pesquisa, tema, recentes e comparar com o original (no longo e em cópias do curto) ----------
        // O contador de ocorrências é independente da pesquisa da página: texto de cada nó da área útil sem acento nem caixa.
        const string ContadorJs = "((termo) => { const sem = (s) => s.normalize('NFD').replace(/[\\u0300-\\u036f]/g, '').toLowerCase(); const alvo = sem(termo); let total = 0; " +
            "for (const area of document.querySelectorAll('section.folha > .area')) { const w = document.createTreeWalker(area, NodeFilter.SHOW_TEXT); " +
            "for (let n = w.nextNode(); n; n = w.nextNode()) { if (n.parentElement.closest('[data-pmvni-oculto], style, script')) continue; total += sem(n.data).split(alvo).length - 1; } } return total; })";
        const string OcorrenciaAtualJs = "(() => { const h = CSS.highlights.get('pmvni-busca-atual'); return (h && h.size === 1) ? [...h][0] : null; })()";

        await Passo("26_pesquisa", async p =>
        {
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var proj = s.Revisao.Projeto;
            var item10 = proj.Incluidos[9];
            var titulo = DadosEfetivos.TextoEfetivo(proj, item10, "titulo")!;
            var elementosAntes = (await Js(j, "document.body.querySelectorAll('*').length")).GetInt32();
            var textoAntes = await j.Documento!.TextoPorFolhaAsync();

            // Ctrl+F foca a caixa da barra de título.
            await j.ExecutarComandoAsync(ComandosApp.PesquisaFocar);
            p["caixa_habilitada"] = j.CaixaPesquisaHabilitada;
            p["ctrl_f_foca_a_caixa"] = j.CaixaPesquisaComFoco;

            // O título do ITEM 10: ao menos uma ocorrência, "1 de N", a página mostra a primeira e ela está dentro do card do item 10.
            await j.DigitarPesquisaAsync(titulo);
            p["titulo_pesquisado"] = titulo;
            p["total_do_titulo"] = j.PesquisaTotal;
            p["resultado_do_titulo"] = j.TextoResultadoPesquisa;
            p["ref_do_titulo"] = j.PesquisaRef;
            var dentroDoCard = (await Js(j, $"((r) => !r ? false : [...document.querySelectorAll('section.folha .card[data-ref=\"{item10.Id}\"]')].some((c) => {{ const a = r.getBoundingClientRect(), b = c.getBoundingClientRect(); return a.top >= b.top - 1 && a.bottom <= b.bottom + 1 && a.height > 0; }}))({OcorrenciaAtualJs})")).GetBoolean();
            var naJanela = (await Js(j, $"((r) => !r ? false : (() => {{ const a = r.getBoundingClientRect(); return a.top >= 0 && a.bottom <= window.innerHeight; }})())({OcorrenciaAtualJs})")).GetBoolean();
            p["rolou_para_o_item_10"] = dentroDoCard && naJanela;
            var tituloOk = j.PesquisaTotal >= 1 && j.TextoResultadoPesquisa == $"1 de {j.PesquisaTotal}" && j.PesquisaRef == item10.Id && dentroDoCard && naJanela;
            await CapturarWebAsync(j, Path.Combine(pasta, "26-pesquisa-titulo.png"));
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "26-janela-xaml-pesquisa.png"));

            // Sem acento e sem caixa; Enter avança, Shift+Enter volta, com volta ao fim e ao começo (os mesmos caminhos das teclas).
            await j.DigitarPesquisaAsync("VERIFICAÇÃO");
            var totalMaiusculo = j.PesquisaTotal;
            await j.DigitarPesquisaAsync("verificacao");
            var total = j.PesquisaTotal;
            var contados = (await Js(j, ContadorJs + "('verificacao')")).GetInt32();
            p["total_verificacao"] = total;
            p["contador_independente"] = contados;
            var semAcento = total >= 60 && total == totalMaiusculo && total == contados && j.TextoResultadoPesquisa == $"1 de {total}";
            await j.IrNaPesquisaAsync(+1);
            var enter = j.PesquisaAtual == 2 && j.TextoResultadoPesquisa == $"2 de {total}";
            await j.IrNaPesquisaAsync(-1);
            var shiftEnter = j.PesquisaAtual == 1;
            await j.IrNaPesquisaAsync(-1);
            var voltaAoFim = j.PesquisaAtual == total && j.TextoResultadoPesquisa == $"{total} de {total}";
            await j.IrNaPesquisaAsync(+1);
            var voltaAoComeco = j.PesquisaAtual == 1;
            p["enter_avanca"] = enter;
            p["shift_enter_volta"] = shiftEnter;
            p["volta_ao_fim_e_ao_comeco"] = voltaAoFim && voltaAoComeco;

            // O realce não toca o DOM das folhas.
            var elementosDepois = (await Js(j, "document.body.querySelectorAll('*').length")).GetInt32();
            var textoDepois = await j.Documento!.TextoPorFolhaAsync();
            var realcesNaPagina = (await Js(j, "CSS.highlights.get('pmvni-busca').size")).GetInt32();
            p["elementos_do_body_antes_e_depois"] = new JsonArray(elementosAntes, elementosDepois);
            p["ocorrencias_realcadas"] = realcesNaPagina;
            var domIgual = elementosAntes == elementosDepois && textoAntes.SequenceEqual(textoDepois) && realcesNaPagina == total;

            // Depois de recompor (um comando), a pesquisa é refeita sobre as folhas novas, na mesma ocorrência, sem rolar.
            await j.IrNaPesquisaAsync(+1);
            await j.IrNaPesquisaAsync(+1);
            var atualAntes = j.PesquisaAtual;
            var item = proj.Incluidos[19];
            var atual = item.Situacao.Revisada ?? item.Situacao.Original!.GetValue<string>();
            await j.AplicarAsync(new DefinirSituacao(item.Id, Faixas.OrdemSituacoes.First(c => c != atual)));
            var refeita = await EsperarAteAsync(async () => (await Js(j, "(CSS.highlights.get('pmvni-busca') || { size: 0 }).size")).GetInt32() == total, 8000);
            p["pesquisa_refeita_depois_de_recompor"] = refeita;
            p["ocorrencia_atual_mantida"] = j.PesquisaAtual == atualAntes && atualAntes == 3;
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            await EsperarAteAsync(async () => (await Js(j, "(CSS.highlights.get('pmvni-busca') || { size: 0 }).size")).GetInt32() == total, 8000);

            // Exportar com o realce na tela: o PDF sai das folhas do host de composição, sem realce nem marcas.
            var pdf = Path.Combine(pasta, "longo-com-pesquisa.pdf");
            File.Delete(pdf);
            var (ok, x, erro, _) = await j.ExportarParaAsync(pdf);
            if (!ok || x is null) throw new InvalidOperationException("exportação falhou: " + erro);
            var info = InfoPdf.Ler(await File.ReadAllBytesAsync(pdf));
            var html = j.Aberto!.Preparado.Render.Html;
            var paginasIguais = info.Paginas == j.Aberto.Composicao.Folhas;
            var planoIgual = x.Plano.ParaJson() == j.Aberto.Composicao.Plano.ParaJson();
            // "<mark>" como elemento (o SVG do kit tem <marker>, que não conta).
            var htmlLimpo = !html.Contains("pmvni-busca", StringComparison.Ordinal) && !html.Contains("data-pmvni", StringComparison.Ordinal)
                && !System.Text.RegularExpressions.Regex.IsMatch(html, @"<mark[\s>/]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var pdfOk = paginasIguais && planoIgual && htmlLimpo;
            p["pdf_com_pesquisa_ativa"] = $"{info.Paginas} páginas, plano igual, HTML sem realce";

            // Termo vazio limpa tudo.
            await j.DigitarPesquisaAsync("");
            var limpou = j.TextoResultadoPesquisa == "" && j.PesquisaTotal == 0 && (await Js(j, "CSS.highlights.size")).GetInt32() == 0;
            p["termo_vazio_limpa"] = limpou;
            p["conferencias"] = new JsonObject
            {
                ["caixa_habilitada"] = j.CaixaPesquisaHabilitada, ["titulo"] = tituloOk, ["sem_acento_e_sem_caixa"] = semAcento, ["dom_igual"] = domIgual,
                ["pdf"] = pdfOk, ["pdf_paginas_iguais"] = paginasIguais, ["pdf_plano_igual"] = planoIgual, ["html_sem_realce"] = htmlLimpo, ["limpa"] = limpou, ["total_maiusculo"] = totalMaiusculo,
            };
            p["ok"] = j.CaixaPesquisaHabilitada && p["ctrl_f_foca_a_caixa"]!.GetValue<bool>() && tituloOk && semAcento && enter && shiftEnter && voltaAoFim && voltaAoComeco
                && domIgual && refeita && p["ocorrencia_atual_mantida"]!.GetValue<bool>() && pdfOk && limpou;
        });

        await Passo("27_tema_chrome", async p =>
        {
            // O tema do app (janela, faixa, painéis) pelos comandos Exibir › Tema; as folhas continuam brancas nos dois. Guardado em ajustes.json.
            if (j.Sessao is null) throw new InvalidOperationException("nenhum relatório aberto");
            const string Cores = "({ escuro: matchMedia('(prefers-color-scheme: dark)').matches, fundoDaPagina: getComputedStyle(document.documentElement).backgroundColor, " +
                "fundoDaFolha: getComputedStyle(document.querySelector('section.folha')).backgroundColor })";
            var ajustesJson = Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo);
            string? Guardado() => JsonNode.Parse(File.ReadAllText(ajustesJson))?["tema"]?.GetValue<string>();
            var textoAntes = await j.Documento!.TextoPorFolhaAsync();
            var faixa = j.FaixaAtual;

            await j.ExecutarComandoAsync(ComandosApp.ExibirTemaEscuro);
            await Esperar(900);
            var escuro = await Js(j, Cores);
            var escuroOk = j.TemaAtual == ElementTheme.Dark && j.TemaDosAjustes == "escuro" && Guardado() == "escuro"
                && faixa.Marcado(ComandosApp.ExibirTemaEscuro) && !faixa.Marcado(ComandosApp.ExibirTemaClaro) && !faixa.Marcado(ComandosApp.ExibirTemaSistema)
                && j.TemaMarcadoNaPagina == "escuro" && escuro.GetProperty("escuro").GetBoolean();
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "27-janela-xaml-escuro.png"));
            await CapturarWebAsync(j, Path.Combine(pasta, "27-tela-escuro.png"));

            await j.ExecutarComandoAsync(ComandosApp.ExibirTemaClaro);
            await Esperar(900);
            var claro = await Js(j, Cores);
            var claroOk = j.TemaAtual == ElementTheme.Light && j.TemaDosAjustes == "claro" && Guardado() == "claro"
                && faixa.Marcado(ComandosApp.ExibirTemaClaro) && !faixa.Marcado(ComandosApp.ExibirTemaEscuro)
                && j.TemaMarcadoNaPagina == "claro" && !claro.GetProperty("escuro").GetBoolean();
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "27-janela-xaml-claro.png"));

            // Pela página Ajustes (o mesmo caminho do clique na opção) e de volta ao sistema.
            j.EscolherTema("escuro");
            var pelaPagina = j.TemaAtual == ElementTheme.Dark && Guardado() == "escuro";
            await j.ExecutarComandoAsync(ComandosApp.ExibirTemaSistema);
            await Esperar(400);
            var sistemaOk = j.TemaDosAjustes == "sistema" && Guardado() == "sistema" && faixa.Marcado(ComandosApp.ExibirTemaSistema) && j.TemaMarcadoNaPagina == "sistema";

            // Página Ajustes (módulo do trilho, embaixo): tema, intervalo do salvamento, pasta de recuperação e versões.
            await j.MudarModuloAsync(Modulo.Ajustes);
            var pag = j.AjustesPagina;
            var noTrilho = j.TrilhoAtual.Modulos.Contains(Modulo.Ajustes);
            var visivel = j.PaginaAjustesVisivel && j.ModuloAtual == Modulo.Ajustes && j.FaixaAtual.Definicao!.Modulo == Modulo.Ajustes && !j.CaixaPesquisaHabilitada;
            var versoes = pag.TextoVersoes.Text;
            var recuperacao = pag.TextoPastaRecuperacao.Text;
            var textosOk = versoes.Contains("Versão do app: ", StringComparison.Ordinal) && versoes.Contains($"Modelo do relatório: rev{ModeloOficial.Revisao}", StringComparison.Ordinal)
                && versoes.Contains("WebView2: " + j.NucleoWeb.Environment.BrowserVersionString, StringComparison.Ordinal)
                && recuperacao == Path.Combine(PastaDados.Raiz, "recuperacao");
            var intervaloAntes = pag.CaixaIntervalo.SelectedItem is Microsoft.UI.Xaml.Controls.ComboBoxItem { Tag: int i0 } ? i0 : -1;
            pag.CaixaIntervalo.SelectedIndex = Ajustes.Intervalos.ToList().IndexOf(60); // como a escolha do usuário na caixa
            string? Intervalo() => JsonNode.Parse(File.ReadAllText(ajustesJson))?["intervalo_autosave_s"]?.GetValue<int>().ToString();
            var intervaloOk = intervaloAntes == 15 && j.IntervaloAutosaveAtual == 60 && Intervalo() == "60";
            pag.CaixaIntervalo.SelectedIndex = Ajustes.Intervalos.ToList().IndexOf(15);
            intervaloOk = intervaloOk && j.IntervaloAutosaveAtual == 15 && Intervalo() == "15";
            await Esperar(300);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "27-janela-xaml-ajustes.png"));
            p["pagina_ajustes"] = new JsonObject { ["no_trilho"] = noTrilho, ["visivel_com_a_faixa_dos_ajustes"] = visivel, ["versoes"] = versoes, ["pasta_de_recuperacao"] = recuperacao, ["intervalo_guardado"] = intervaloOk };
            await j.MudarModuloAsync(Modulo.Relatorios);
            var voltou = !j.PaginaAjustesVisivel && j.ModuloAtual == Modulo.Relatorios && j.CaixaPesquisaHabilitada;
            var ajustesOk = noTrilho && visivel && textosOk && intervaloOk && voltou;
            p["escuro"] = JsonNode.Parse(escuro.GetRawText());
            p["claro"] = JsonNode.Parse(claro.GetRawText());
            p["tema_guardado_em_ajustes"] = Guardado();
            var fundoMuda = escuro.GetProperty("fundoDaPagina").GetString() != claro.GetProperty("fundoDaPagina").GetString();
            var folhasBrancas = escuro.GetProperty("fundoDaFolha").GetString() == "rgb(255, 255, 255)" && claro.GetProperty("fundoDaFolha").GetString() == "rgb(255, 255, 255)";
            p["folhas_brancas_nos_dois"] = folhasBrancas;
            p["texto_por_folha_igual"] = (await j.Documento!.TextoPorFolhaAsync()).SequenceEqual(textoAntes);
            p["conferencias"] = new JsonObject
            {
                ["escuro"] = escuroOk, ["claro"] = claroOk, ["pela_pagina_ajustes"] = pelaPagina, ["sistema"] = sistemaOk, ["fundo_muda"] = fundoMuda, ["pagina_ajustes"] = ajustesOk,
                ["tema_depois_de_escuro"] = j.TemaDosAjustes,
            };
            p["ok"] = escuroOk && claroOk && pelaPagina && sistemaOk && fundoMuda && folhasBrancas && p["texto_por_folha_igual"]!.GetValue<bool>() && ajustesOk;
        });

        var recenteA = Path.Combine(pasta, "recentes-a.pmvrel");
        var recenteB = Path.Combine(pasta, "recentes-b.pmvrel");
        await Passo("28_recentes", async p =>
        {
            // Dois pacotes abertos (o mais recente primeiro no menu), abrir pelo menu, arquivo apagado some do menu, no máximo 10.
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), recenteA)) throw new InvalidOperationException("não importou A: " + j.InfoMensagem.Message);
            if (!await j.ImportarParaAsync(Path.Combine(corpus, "curto.json"), recenteB)) throw new InvalidOperationException("não importou B: " + j.InfoMensagem.Message);
            var faixa = j.FaixaAtual;
            faixa.EscolherAba("Arquivo");
            await Esperar(250);
            var valores = faixa.AtualizarMenuDinamico(ComandosApp.ArquivoRecentes);
            p["menu_recentes"] = new JsonArray(faixa.TextosDoMenuDinamico(ComandosApp.ArquivoRecentes).Take(4).Select(t => (JsonNode)t).ToArray());
            var doisNoTopo = valores.Count >= 2 && valores[0] == Path.GetFullPath(recenteB) && valores[1] == Path.GetFullPath(recenteA);
            p["os_dois_no_topo_mais_recente_primeiro"] = doisNoTopo;
            p["menu_habilitado"] = faixa.Habilitado(ComandosApp.ArquivoRecentes);
            string? GuardadoNoTopo() => JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?["recentes"]?[0]?.GetValue<string>();
            var guardado = GuardadoNoTopo() == Path.GetFullPath(recenteB);

            // Escolher o A no menu abre o A e ele sobe ao topo (o B fecha).
            await j.AbrirRecenteAsync(valores[1]);
            var abriuA = string.Equals(j.Sessao?.Caminho, Path.GetFullPath(recenteA), StringComparison.OrdinalIgnoreCase);
            var topoAgora = faixa.AtualizarMenuDinamico(ComandosApp.ArquivoRecentes);
            var aNoTopo = topoAgora.Count >= 2 && topoAgora[0] == Path.GetFullPath(recenteA) && topoAgora[1] == Path.GetFullPath(recenteB);

            // Apagar o B (fechado) por fora: some do menu (nada desabilitado); a tentativa de abrir um item velho avisa.
            foreach (var f in Directory.GetFiles(pasta, "recentes-b.pmvrel*").Concat(Directory.GetFiles(pasta, ".~recentes-b.pmvrel*"))) { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
            var depois = faixa.AtualizarMenuDinamico(ComandosApp.ArquivoRecentes);
            var sumiu = !depois.Contains(Path.GetFullPath(recenteB), StringComparer.OrdinalIgnoreCase) && depois.Count > 0 && depois[0] == Path.GetFullPath(recenteA);
            var abertoAntes = j.Sessao?.Caminho;
            await j.AbrirRecenteAsync(Path.GetFullPath(recenteB));
            var avisou = j.InfoMensagem.IsOpen && j.InfoMensagem.Title == "O arquivo não existe mais" && j.Sessao?.Caminho == abertoAntes;
            var max = j.RecentesNosAjustes.Count <= Ajustes.MaxRecentes && j.RecentesNosAjustes.Count == j.RecentesNosAjustes.Distinct(StringComparer.OrdinalIgnoreCase).Count();
            p["depois_de_abrir_o_a"] = new JsonArray(topoAgora.Take(2).Select(t => (JsonNode)Path.GetFileName(t)).ToArray());
            p["depois_de_apagar_o_b"] = new JsonArray(depois.Take(3).Select(t => (JsonNode)Path.GetFileName(t)).ToArray());
            p["aviso_de_arquivo_ausente"] = avisou;
            p["recentes_guardados"] = j.RecentesNosAjustes.Count;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "28-janela-xaml-arquivo.png"));
            p["ok"] = doisNoTopo && guardado && abriuA && aNoTopo && sumiu && avisou && max && faixa.Habilitado(ComandosApp.ArquivoRecentes);
        });

        var pacoteCitacoes = Path.Combine(pasta, "curto-citacoes.pmvrel");
        await Passo("29_comparar_com_o_original", async p =>
        {
            // O longo, com as alterações dos passos 13–17 (ITEM 03: verificação, situação e justificativa; ITEM 05 excluído).
            if (!await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não reabriu o longo: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            var proj = s.Revisao.Projeto;
            var dados = JsonNode.Parse(await File.ReadAllTextAsync(dadosLongo))!["itens"]!.AsArray();
            var item3 = dados[2]!.AsObject();
            var codigoOriginal = item3["situacao"]!.GetValue<string>();
            var verificacaoOriginal = item3["verificacao"]!.GetValue<string>();
            await j.ExecutarComandoAsync(ComandosApp.RevisaoComparar);
            var mostrada = perguntas.Informados.LastOrDefault();
            var esperado = string.Join("\n",
                "2 itens alterados em relação ao original recebido:",
                "",
                "ITEM 03",
                "  Situação: " + Pmvni.Composicao.Kit.Situacoes.Sit[codigoOriginal].Texto + " → " + Pmvni.Composicao.Kit.Situacoes.Sit["ATENDE"].Texto,
                "  Justificativa: " + Justificativa,
                "  Verificação técnica:",
                "    Original: " + verificacaoOriginal,
                "    Revisado: " + NovaVerificacao,
                "",
                "Item excluído (era o 05)",
                "  Excluído do relatório (não sai no PDF).",
                "",
                "Citações convertidas na importação (0):",
                "  Nenhuma.");
            p["texto_mostrado"] = mostrada.Texto;
            p["titulo_do_dialogo"] = mostrada.Titulo;
            var diferencas = Comparacao.Diferencas(proj, s.Revisao.HistoricoCompleto);
            p["itens_alterados"] = new JsonArray(diferencas.Select(d => (JsonNode)Comparacao.Rotulo(d)).ToArray());
            var longoOk = mostrada.Titulo == "Comparar com o original" && mostrada.Texto == esperado && j.TextoComparacao() == esperado
                && diferencas.Count == 2 && diferencas[1].Excluido && diferencas[1].NumeroOriginal == 5;
            p["longo_confere_os_textos_exatos"] = longoOk;

            // Citações convertidas na importação aparecem em seção própria, mesmo no item sem alteração (PF13).
            var copia = Path.Combine(pasta, "curto-citacoes.json");
            var no = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(corpus, "curto.json")))!;
            var v3 = no["itens"]![2]!["verificacao"]!.GetValue<string>();
            no["itens"]![2]!["verificacao"] = v3 + " Ver item 02 e ITEM 01.";
            await File.WriteAllTextAsync(copia, no.ToJsonString());
            try
            {
                if (!await j.ImportarParaAsync(copia, pacoteCitacoes)) throw new InvalidOperationException("não importou: " + j.InfoMensagem.Message);
            }
            finally { File.Delete(copia); }
            await j.ExecutarComandoAsync(ComandosApp.RevisaoComparar);
            var citacoes = perguntas.Informados.LastOrDefault().Texto;
            var esperadoCitacoes = string.Join("\n",
                "Nenhum item foi alterado em relação ao original recebido.",
                "",
                "Citações convertidas na importação (2):",
                "  ITEM 03 · Verificação técnica: «item 02» → ITEM 02",
                "  ITEM 03 · Verificação técnica: «ITEM 01» → ITEM 01");
            p["texto_com_citacoes"] = citacoes;
            var citacoesOk = citacoes == esperadoCitacoes;
            p["citacoes_em_secao_propria"] = citacoesOk;
            p["ok"] = longoOk && citacoesOk && j.FaixaAtual.Habilitado(ComandosApp.RevisaoComparar);
        });

        // ---------- Tarefa 13: módulos Processos e Despachos, pasta de processos nos Ajustes ----------
        // Numeração: o plano previa 28 (processos), 29 (despachos), 31, 32 e 33; 28 e 29 já são Recentes e Comparar (Tarefa 11) e o 30 fica
        // reservado à Legislação (Tarefa 18). Aqui: 31 processos, 32 despachos, 33 pasta nos Ajustes, 34 faixa e pesquisa por módulo, 35 pastas intactas.
        // Tudo sobre a CÓPIA da árvore fictícia (<pasta>\processos, feita no início); nenhuma abertura externa acontece (só é registrada).
        var processo0001 = Path.Combine(copiaProcessos, "2099", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)");
        var pmvrel1 = Path.Combine(processo0001, "Relatorio_0001_2099.pmvrel");
        var pmvrel2 = Path.Combine(processo0001, "Relatorio_0001_2099_2.pmvrel");
        var nomesDosProcessos = new[]
        {
            "0012-2099 - BELTRANO DE EXEMPLO (FICTICIO)", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)", "pasta fora do padrao", "0007 - 2098 - CICLANO DE EXEMPLO (FICTICIO)",
        };

        await Passo("31_processos", async p =>
        {
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var faixa = j.FaixaAtual;
            string[] Nomes() => j.ProcessosMostrados.Select(l => l.Processo.NomePasta).ToArray();
            string[] Detalhe() => j.DetalheMostrado.Select(l => l.Texto).ToArray();
            async Task<bool> EscolherProcessoAsync(string inicio)
            {
                await j.EscolherProcessoNaListaAsync(inicio);
                return await EsperarAteAsync(() => Task.FromResult(j.ProcessoEscolhido?.NomePasta.StartsWith(inicio, StringComparison.Ordinal) == true && j.DetalheMostrado.Count > 0), 5000);
            }

            // A lista: os 4 processos da árvore fictícia, na ordem da Tarefa 12; a pasta e a dica certas; a página de Processos à vista.
            var ordemOk = Nomes().SequenceEqual(nomesDosProcessos);
            p["processos_na_ordem"] = new JsonArray(Nomes().Select(n => (JsonNode)n).ToArray());
            p["contagem"] = j.TextoContagemProcessos;
            var listaOk = ordemOk && j.TextoContagemProcessos == "4 processos" && j.PaginaProcessosVisivel && !j.PaginaDespachosVisivel
                && j.ModuloAtual == Modulo.Processos && !j.AvisoPastaProcessos.IsOpen && j.PastaProcessosEfetiva == copiaProcessos;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "31-janela-xaml-processos.png"));

            // A pesquisa da barra de título filtra por número ou requerente (sem caixa nem acento): "fulano" → 1; vazio → 4.
            await j.DigitarPesquisaAsync("fulano");
            var fulano = Nomes();
            var textoFulano = j.TextoResultadoPesquisa;
            await j.DigitarPesquisaAsync("FÚLANO");
            var acento = Nomes().Length == 1;
            await j.DigitarPesquisaAsync("12");
            var numero = Nomes().SequenceEqual([nomesDosProcessos[0]]);
            await j.DigitarPesquisaAsync("");
            p["filtro_fulano"] = new JsonArray(fulano.Select(n => (JsonNode)n).ToArray());
            p["texto_do_filtro"] = textoFulano;
            var filtroOk = fulano.Length == 1 && fulano[0].StartsWith("0001", StringComparison.Ordinal) && textoFulano == "1 de 4" && acento && numero
                && Nomes().Length == 4 && j.TextoResultadoPesquisa == "";

            // Filtro por ano (menu dinâmico, anos da pasta) e ordenar por requerente (✓ no menu); de volta ao padrão.
            faixa.EscolherAba("Página Inicial");
            await Esperar(250);
            var anos = faixa.AtualizarMenuDinamico(ComandosApp.ProcessosAno);
            var textosAno = faixa.TextosDoMenuDinamico(ComandosApp.ProcessosAno);
            await j.DefinirAnoAsync("2098");
            var so2098 = Nomes().SequenceEqual([nomesDosProcessos[3]]) && j.TextoContagemProcessos == "1 de 4 processos · 2098";
            faixa.AtualizarMenuDinamico(ComandosApp.ProcessosAno);
            var textosAno2 = faixa.TextosDoMenuDinamico(ComandosApp.ProcessosAno);
            await j.DefinirAnoAsync("");
            p["menu_ano"] = new JsonArray(textosAno.Select(t => (JsonNode)t).ToArray());
            var anoOk = anos.SequenceEqual(["", "2099", "2098"]) && textosAno[0].StartsWith('✓') && so2098 && textosAno2[2].StartsWith('✓') && Nomes().Length == 4;
            faixa.EscolherAba("Exibir");
            await Esperar(250);
            await j.ExecutarComandoAsync(ComandosApp.ProcessosOrdenarRequerente);
            var porRequerente = Nomes();
            var marcaRequerente = faixa.Marcado(ComandosApp.ProcessosOrdenarRequerente) && !faixa.Marcado(ComandosApp.ProcessosOrdenarNumero);
            await j.ExecutarComandoAsync(ComandosApp.ProcessosOrdenarNumero);
            p["por_requerente"] = new JsonArray(porRequerente.Select(n => (JsonNode)n).ToArray());
            var ordenarOk = porRequerente.SequenceEqual([nomesDosProcessos[0], nomesDosProcessos[3], nomesDosProcessos[1], nomesDosProcessos[2]])
                && marcaRequerente && Nomes().SequenceEqual(nomesDosProcessos) && j.OrdemDeProcessos == OrdemProcessos.Numero;
            faixa.EscolherAba("Página Inicial");
            await Esperar(250);

            // O detalhe do 0001: relatório (a análise da IA), despachos e, se pedido, PROJETO e DOCUMENTOS.
            var escolheu = await EscolherProcessoAsync("0001");
            var detalhe = Detalhe();
            p["detalhe_do_0001"] = new JsonArray(detalhe.Select(t => (JsonNode)t).ToArray());
            var detalheOk = escolheu && detalhe.Take(3).SequenceEqual(["Relatórios (1)", "dados_0001.json", "Despachos (2)"])
                && !detalhe.Any(t => t.StartsWith("PROJETO", StringComparison.Ordinal))
                && j.DetalheMostrado.Count(l => l.Tipo == TipoLinhaDetalhe.ArquivoDeDespacho) == 3;
            await j.ExecutarComandoAsync(ComandosApp.ProcessosMostrarPastas);
            var comPastas = Detalhe();
            var pastasOk = comPastas.Contains("PROJETO (2)") && comPastas.Contains("DOCUMENTOS (1)") && faixa.Ligado(ComandosApp.ProcessosMostrarPastas);

            // Abrir arquivo: um PDF do PROJETO é só registrado (modo roteiro); um script (.cmd) é recusado com a mensagem exata e nada é registrado.
            AberturaExterna.Registradas.Clear();
            var escolheuPdf = j.EscolherEntradaNaLista(l => l.Texto == "ARQ 01-03.pdf");
            await j.ExecutarComandoAsync(ComandosApp.ProcessosAbrirArquivo);
            var pdfRegistrado = AberturaExterna.Registradas.SequenceEqual([Path.Combine(processo0001, "PROJETO", "ARQ 01-03.pdf")]);
            var escolheuScript = j.EscolherEntradaNaLista(l => l.Texto == "script.cmd");
            await j.ExecutarComandoAsync(ComandosApp.ProcessosAbrirArquivo);
            var scriptRecusado = AberturaExterna.Registradas.Count == 1 && j.MensagemDeProcessos.IsOpen && j.MensagemDeProcessos.Title == "O arquivo não foi aberto"
                && j.MensagemDeProcessos.Message == "Este tipo de arquivo não é aberto pelo editor (.cmd). Use Mostrar no Explorador.";
            p["mensagem_do_script"] = j.MensagemDeProcessos.Message;
            // Abrir pasta no Explorador: só registra (modo roteiro).
            await j.ExecutarComandoAsync(ComandosApp.ProcessosAbrirPasta);
            var pastaRegistrada = AberturaExterna.Registradas.Count == 2 && AberturaExterna.Registradas[1] == "explorador: " + processo0001;
            await j.ExecutarComandoAsync(ComandosApp.ProcessosMostrarPastas);
            var semPastas = !Detalhe().Any(t => t.StartsWith("PROJETO", StringComparison.Ordinal)) && !faixa.Ligado(ComandosApp.ProcessosMostrarPastas);
            p["registradas"] = new JsonArray(AberturaExterna.Registradas.Select(t => (JsonNode)t).ToArray());
            var abrirOk = escolheuPdf && pdfRegistrado && escolheuScript && scriptRecusado && pastaRegistrada && semPastas;

            // Importar análise da IA do 0001: cria Relatorio_0001_2099.pmvrel na pasta do processo e abre no módulo Relatórios com 3 itens.
            var escolheuAnalise = j.EscolherEntradaNaLista(l => l.Tipo == TipoLinhaDetalhe.Analise);
            await j.ExecutarComandoAsync(ComandosApp.ProcessosImportarAnalise);
            var primeira = File.Exists(pmvrel1) && j.ModuloAtual == Modulo.Relatorios && j.Sessao is not null
                && string.Equals(j.Sessao.Caminho, pmvrel1, StringComparison.OrdinalIgnoreCase) && j.Sessao.Revisao.Projeto.Incluidos.Count == 3;
            p["primeira_importacao_itens"] = j.Sessao?.Revisao.Projeto.Incluidos.Count;
            var titulo1 = j.TextoTituloAtual;
            var sha1 = File.Exists(pmvrel1) ? Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pmvrel1))) : "";

            // Repetir: nunca sobrescreve (o nome livre seguinte, _2) e o primeiro fica como estava.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var escolheuDeNovo = j.EscolherEntradaNaLista(l => l.Tipo == TipoLinhaDetalhe.Analise);
            var apareceu = Detalhe().Contains("Relatorio_0001_2099.pmvrel");
            await j.ExecutarComandoAsync(ComandosApp.ProcessosImportarAnalise);
            var segunda = File.Exists(pmvrel2) && string.Equals(j.Sessao?.Caminho, pmvrel2, StringComparison.OrdinalIgnoreCase) && j.ModuloAtual == Modulo.Relatorios
                && j.Sessao!.Revisao.Projeto.Incluidos.Count == 3;
            var primeiraIntacta = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(pmvrel1))) == sha1;
            var criados = Directory.GetFiles(processo0001, "*.pmvrel").Select(f => Path.GetFileName(f)).Order(StringComparer.Ordinal).ToArray();
            p["arquivos_criados"] = new JsonArray(criados.Select(n => (JsonNode)n).ToArray());

            // Abrir relatório: o .pmvrel listado abre no módulo Relatórios.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var escolheuPacote = j.EscolherEntradaNaLista(l => l.Tipo == TipoLinhaDetalhe.Relatorio && l.Texto == "Relatorio_0001_2099.pmvrel");
            await j.ExecutarComandoAsync(ComandosApp.ProcessosAbrirRelatorio);
            var abriu = escolheuPacote && string.Equals(j.Sessao?.Caminho, pmvrel1, StringComparison.OrdinalIgnoreCase) && j.ModuloAtual == Modulo.Relatorios;
            p["titulo_da_janela"] = titulo1;
            var importarOk = escolheuAnalise && primeira && titulo1.Length > 0 && escolheuDeNovo && apareceu && segunda && primeiraIntacta && abriu;
            p["conferencias"] = new JsonObject
            {
                ["lista"] = listaOk, ["filtro"] = filtroOk, ["ano"] = anoOk, ["ordenar"] = ordenarOk, ["detalhe"] = detalheOk && pastasOk, ["abrir_arquivo_e_pasta"] = abrirOk,
                ["primeira_importacao"] = primeira, ["segunda_importacao_nome_livre"] = segunda, ["primeira_intacta"] = primeiraIntacta, ["abrir_relatorio"] = abriu,
            };
            p["ok"] = listaOk && filtroOk && anoOk && ordenarOk && detalheOk && pastasOk && abrirOk && importarOk;
        });

        await Passo("32_despachos", async p =>
        {
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.AguardarLeiturasAsync();
            var faixa = j.FaixaAtual;
            List<LinhaDespacho> Despachos() => j.DespachosMostrados.Where(i => i.EhDespacho).Select(i => i.Linha!).ToList();
            string[] Datas() => Despachos().Select(d => d.Despacho.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray();
            var esperadas = new[] { "2099-03-05", "2099-02-20", "2099-01-15", "2098-03-02" };
            p["datas_de_todos"] = new JsonArray(Datas().Select(d => (JsonNode)d).ToArray());
            p["contagem"] = j.TextoContagemDespachos;
            var todosOk = Datas().SequenceEqual(esperadas) && j.TextoContagemDespachos == "4 despachos" && j.PaginaDespachosVisivel && !j.PaginaProcessosVisivel
                && j.DicaDaPesquisa == "Pesquisar despachos" && j.CaixaPesquisaHabilitada && !j.AvisoPastaDespachos.IsOpen;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "32-janela-xaml-despachos.png"));

            // Filtro por tipo: Aprovação → 2 (os dois de aprovação); Correção → 1; Todos → 4.
            await j.ExecutarComandoAsync(ComandosApp.DespachosFiltroAprovacao);
            var aprovacao = Datas();
            var marcaAprovacao = faixa.Marcado(ComandosApp.DespachosFiltroAprovacao) && !faixa.Marcado(ComandosApp.DespachosFiltroTodos);
            await j.ExecutarComandoAsync(ComandosApp.DespachosFiltroCorrecao);
            var correcao = Datas();
            await j.ExecutarComandoAsync(ComandosApp.DespachosFiltroTodos);
            p["aprovacao"] = new JsonArray(aprovacao.Select(d => (JsonNode)d).ToArray());
            var filtroOk = aprovacao.SequenceEqual(["2099-02-20", "2098-03-02"]) && marcaAprovacao && correcao.SequenceEqual(["2099-01-15"]) && Datas().Length == 4;

            // A pesquisa da barra de título é pelo nome do arquivo, sem caixa nem acento.
            await j.DigitarPesquisaAsync("CORREÇÃO");
            var porNome = Datas();
            var textoNome = j.TextoResultadoPesquisa;
            await j.DigitarPesquisaAsync("");
            var pesquisaOk = porNome.SequenceEqual(["2099-01-15"]) && textoNome == "1 de 4" && Datas().Length == 4;

            // Abrir PDF do despacho de aprovação do 0001: só registra (modo roteiro). Abrir prefere o DOCX; o do 0012 só tem DOCX (sem Abrir PDF).
            AberturaExterna.Registradas.Clear();
            var aprovacao0001 = Path.Combine(processo0001, "DESPACHO 02 - 2099-02-20", "DESPACHO_APROVACAO_0001_2099.pdf");
            var escolheuAprovacao = j.EscolherDespachoNaLista(l => l.Despacho.Tipo == TipoDespacho.Aprovacao && l.Despacho.Processo?.Numero == 1);
            var pdfHabilitado = faixa.Habilitado(ComandosApp.DespachosAbrirPdf);
            await j.ExecutarComandoAsync(ComandosApp.DespachosAbrirPdf);
            var pdfRegistrado = AberturaExterna.Registradas.SequenceEqual([aprovacao0001]);
            var escolheuCorrecao = j.EscolherDespachoNaLista(l => l.Despacho.Tipo == TipoDespacho.Correcao);
            await j.ExecutarComandoAsync(ComandosApp.DespachosAbrir);
            var docxRegistrado = AberturaExterna.Registradas.Count == 2
                && AberturaExterna.Registradas[1] == Path.Combine(processo0001, "DESPACHO 01 - 2099-01-15", "DESPACHO_CORRECAO_0001_2099.docx");
            var escolheuSoDocx = j.EscolherDespachoNaLista(l => l.Despacho.Processo?.Numero == 12);
            var semPdf = !faixa.Habilitado(ComandosApp.DespachosAbrirPdf) && faixa.Habilitado(ComandosApp.DespachosAbrir);
            await j.ExecutarComandoAsync(ComandosApp.DespachosAbrirPdf);
            var recusouSemPdf = AberturaExterna.Registradas.Count == 2 && j.MensagemDeDespachos.IsOpen
                && j.MensagemDeDespachos.Message == "«Abrir PDF» não está disponível agora: o despacho escolhido não tem PDF.";
            p["registradas"] = new JsonArray(AberturaExterna.Registradas.Select(t => (JsonNode)t).ToArray());

            // A abertura pela AberturaExterna recusa o atalho .lnk com a mensagem exata e nada é registrado.
            var atalho = Path.Combine(copiaProcessos, "MODELOS", "atalho.lnk");
            var abriuAtalho = AberturaExterna.Abrir(atalho, out var motivoAtalho);
            p["motivo_do_atalho"] = motivoAtalho;
            var atalhoOk = !abriuAtalho && motivoAtalho == "Este tipo de arquivo não é aberto pelo editor (.lnk). Use Mostrar no Explorador." && AberturaExterna.Registradas.Count == 2;
            await j.ExecutarComandoAsync(ComandosApp.DespachosMostrar);
            var mostrarOk = AberturaExterna.Registradas.Count == 3 && AberturaExterna.Registradas[2].StartsWith("explorador: ", StringComparison.Ordinal);
            var abrirOk = escolheuAprovacao && pdfHabilitado && pdfRegistrado && escolheuCorrecao && docxRegistrado && escolheuSoDocx && semPdf && recusouSemPdf && atalhoOk && mostrarOk;

            // Agrupar por processo (um título por processo) e ordenar pelos mais antigos; de volta a "por data" e "mais recentes".
            await j.ExecutarComandoAsync(ComandosApp.DespachosAgruparProcesso);
            var titulosGrupo = j.DespachosMostrados.Where(i => i.EhGrupo).Select(i => i.Titulo).ToArray();
            await j.ExecutarComandoAsync(ComandosApp.DespachosOrdenarAntigos);
            var antigos = Datas();
            var marcaOrdem = faixa.Marcado(ComandosApp.DespachosOrdenarAntigos) && !faixa.Marcado(ComandosApp.DespachosOrdenarRecentes) && faixa.Marcado(ComandosApp.DespachosAgruparProcesso);
            await j.ExecutarComandoAsync(ComandosApp.DespachosAgruparData);
            await j.ExecutarComandoAsync(ComandosApp.DespachosOrdenarRecentes);
            p["grupos_por_processo"] = new JsonArray(titulosGrupo.Select(t => (JsonNode)t).ToArray());
            var agruparOk = titulosGrupo.Length == 3 && titulosGrupo[0].StartsWith("0012/2099", StringComparison.Ordinal) && marcaOrdem
                && antigos.Length == 4 && Datas().SequenceEqual(esperadas);

            // Ir ao processo do despacho escolhido (módulo Processos, processo escolhido, detalhe lido) e "só do processo escolhido" de volta aqui.
            var escolheuDoUm = j.EscolherDespachoNaLista(l => l.Despacho.Processo?.Numero == 1 && l.Despacho.Tipo == TipoDespacho.Correcao);
            await j.ExecutarComandoAsync(ComandosApp.DespachosIrAoProcesso);
            await j.AguardarLeiturasAsync();
            var foiAoProcesso = escolheuDoUm && j.ModuloAtual == Modulo.Processos && j.ProcessoEscolhido?.NomePasta.StartsWith("0001", StringComparison.Ordinal) == true
                && j.DetalheMostrado.Any(l => l.Texto == "dados_0001.json");
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.AguardarLeiturasAsync();
            await j.ExecutarComandoAsync(ComandosApp.DespachosDoProcesso);
            var soDoUm = Datas();
            var ligado = faixa.Ligado(ComandosApp.DespachosDoProcesso);
            var textoDoUm = j.TextoContagemDespachos;
            await j.ExecutarComandoAsync(ComandosApp.DespachosDoProcesso);
            p["so_do_processo"] = new JsonArray(soDoUm.Select(d => (JsonNode)d).ToArray());
            p["contagem_do_processo"] = textoDoUm;
            var escopoOk = foiAoProcesso && soDoUm.SequenceEqual(["2099-02-20", "2099-01-15"]) && ligado && textoDoUm == "2 despachos · só do processo 0001/2099" && Datas().Length == 4;

            p["conferencias"] = new JsonObject
            {
                ["todos_por_data"] = todosOk, ["filtro_por_tipo"] = filtroOk, ["pesquisa_por_nome"] = pesquisaOk, ["abrir"] = abrirOk, ["agrupar_e_ordenar"] = agruparOk,
                ["ir_ao_processo_e_so_do_processo"] = escopoOk,
            };
            p["ok"] = todosOk && filtroOk && pesquisaOk && abrirOk && agruparOk && escopoOk;
        });

        await Passo("33_ajustes_pastas", async p =>
        {
            // Pasta inexistente: a mensagem exata com o botão "Abrir Ajustes", nos dois módulos (nada da pasta real é lido).
            var inexistente = Path.Combine(pasta, "nao-existe-processos");
            if (Directory.Exists(inexistente)) Directory.Delete(inexistente, true);
            j.DefinirPastaProcessos(inexistente);
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var mensagem = $"A pasta de processos não foi encontrada: {inexistente}. Escolha a pasta em Ajustes.";
            p["mensagem"] = j.AvisoPastaProcessos.Message;
            var botao = j.BotaoAbrirAjustesProcessos;
            var processosOk = j.AvisoPastaProcessos.IsOpen && j.AvisoPastaProcessos.Message == mensagem && botao.Content as string == "Abrir Ajustes"
                && j.ProcessosMostrados.Count == 0 && j.TextoContagemProcessos == "";
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.AguardarLeiturasAsync();
            var despachosOk = j.AvisoPastaDespachos.IsOpen && j.AvisoPastaDespachos.Message == mensagem && j.DespachosMostrados.Count == 0;
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "33-janela-xaml-pasta-ausente.png"));

            // O botão leva à página Ajustes, que mostra a pasta em uso e avisa que ela não existe.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            ((IInvokeProvider)new ButtonAutomationPeer(j.BotaoAbrirAjustesProcessos).GetPattern(PatternInterface.Invoke)).Invoke();
            var foiAosAjustes = await EsperarAteAsync(() => Task.FromResult(j.ModuloAtual == Modulo.Ajustes && j.PaginaAjustesVisivel), 5000);
            await j.AguardarLeiturasAsync();
            var textoPasta = j.TextoPastaNosAjustes.Text;
            var avisoPasta = j.TextoAvisoPastaNosAjustes.Text;
            p["pagina_ajustes"] = new JsonObject { ["pasta"] = textoPasta, ["aviso"] = avisoPasta };
            var ajustesOk = foiAosAjustes && textoPasta == inexistente && avisoPasta.StartsWith("A pasta não foi encontrada", StringComparison.Ordinal);
            await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, "33-janela-xaml-ajustes-pasta.png"));

            // Escolher a pasta (seletor com resposta fixa) pelo botão: vale na hora, fica em ajustes.json e o aviso some.
            perguntas.PastaEscolhida = copiaProcessos;
            ((IInvokeProvider)new ButtonAutomationPeer(j.AjustesPagina.BotaoEscolherPasta).GetPattern(PatternInterface.Invoke)).Invoke();
            var escolheu = await EsperarAteAsync(() => Task.FromResult(j.PastaProcessosEscolhida == copiaProcessos), 5000);
            await j.AguardarLeiturasAsync();
            var guardada = JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?["pasta_processos"]?.GetValue<string>();
            var escolhaOk = escolheu && guardada == copiaProcessos && j.TextoPastaNosAjustes.Text == copiaProcessos
                && j.TextoAvisoPastaNosAjustes.Visibility == Microsoft.UI.Xaml.Visibility.Collapsed && perguntas.Feitas.Contains("escolher pasta");
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            var voltou = j.ProcessosMostrados.Count == 4 && !j.AvisoPastaProcessos.IsOpen;
            p["conferencias"] = new JsonObject
            {
                ["mensagem_em_processos"] = processosOk, ["mensagem_em_despachos"] = despachosOk, ["botao_leva_aos_ajustes"] = ajustesOk,
                ["escolher_pasta"] = escolhaOk, ["lista_volta"] = voltou,
            };
            p["ok"] = processosOk && despachosOk && ajustesOk && escolhaOk && voltou;
        });

        await Passo("34_faixa_e_pesquisa_por_modulo", async p =>
        {
            // O relatório fica aberto atrás (com um item selecionado) enquanto se visita cada módulo.
            await j.MudarModuloAsync(Modulo.Relatorios);
            if (j.Sessao is null && !await j.AbrirPacoteAsync(pacoteLongo)) throw new InvalidOperationException("não abriu o longo: " + j.InfoMensagem.Message);
            var s = j.Sessao!;
            await j.SelecionarAsync(s.Revisao.Projeto.Incluidos[0].Id);
            string? Guardado(string chave) => JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?[chave]?.GetValue<string>();
            string? AbaGuardada(Modulo m) => JsonNode.Parse(File.ReadAllText(Path.Combine(PastaDados.Raiz, Ajustes.NomeArquivo)))?["aba_por_modulo"]?[m.ToString()]?.GetValue<string>();
            var dicas = new Dictionary<Modulo, string>
            {
                [Modulo.Relatorios] = "Pesquisar no relatório", [Modulo.Despachos] = "Pesquisar despachos", [Modulo.Processos] = "Pesquisar processos",
            };
            var linhas = new JsonArray();
            var tudoOk = true;
            foreach (var m in Faixas.Disponiveis)
            {
                await j.MudarModuloAsync(m);
                await j.AguardarLeiturasAsync();
                var def = Faixas.Para(m);
                var abas = j.FaixaAtual.Definicao!.Abas.Select(a => a.Nome).ToArray();
                var abasOk = j.FaixaAtual.Definicao.Modulo == m && abas.SequenceEqual(def.Abas.Select(a => a.Nome));
                var dicaOk = m == Modulo.Ajustes ? !j.CaixaPesquisaHabilitada : j.DicaDaPesquisa == dicas[m] && j.CaixaPesquisaHabilitada;
                var semManipulador = j.ComandosSemManipulador();
                var abaAberta = j.FaixaAtual.AbaAtual;
                var padraoOk = abaAberta == (AbaGuardada(m) ?? def.Abas[def.AbaPadrao].Nome); // a lembrada, ou a padrão (a segunda; em Ajustes, a primeira)
                var ok = abasOk && dicaOk && semManipulador.Count == 0 && padraoOk && j.ModuloAtual == m;
                tudoOk &= ok;
                linhas.Add(new JsonObject
                {
                    ["modulo"] = m.ToString(), ["abas"] = new JsonArray(abas.Select(a => (JsonNode)a).ToArray()), ["aba_aberta"] = abaAberta, ["dica_da_pesquisa"] = j.DicaDaPesquisa,
                    ["caixa_habilitada"] = j.CaixaPesquisaHabilitada, ["comandos_sem_manipulador"] = new JsonArray(semManipulador.Select(c => (JsonNode)c).ToArray()), ["ok"] = ok,
                });
                await CapturarXamlAsync(j.ElementoRaiz, Path.Combine(pasta, $"34-janela-xaml-{m.ToString().ToLowerInvariant()}.png"));
            }
            p["modulos"] = linhas;


            // Módulo e aba lembrados: Exibir em Processos; sair e voltar mantém a aba; ajustes.json guarda a aba e o módulo.
            await j.MudarModuloAsync(Modulo.Processos);
            await j.AguardarLeiturasAsync();
            j.FaixaAtual.EscolherAba("Exibir");
            await Esperar(250);
            await j.MudarModuloAsync(Modulo.Despachos);
            await j.MudarModuloAsync(Modulo.Processos);
            var abaLembrada = j.FaixaAtual.AbaAtual == "Exibir" && AbaGuardada(Modulo.Processos) == "Exibir" && Guardado("modulo") == "Processos";
            j.FaixaAtual.EscolherAba("Página Inicial");
            await Esperar(250);

            // Cada módulo guarda o seu termo de pesquisa: "fulano" em Processos some em Despachos e volta ao voltar.
            await j.DigitarPesquisaAsync("fulano");
            await j.MudarModuloAsync(Modulo.Despachos);
            var vazioEmDespachos = j.TermoPesquisa == "" && j.DicaDaPesquisa == "Pesquisar despachos";
            await j.MudarModuloAsync(Modulo.Processos);
            var voltouTermo = j.TermoPesquisa == "fulano" && j.ProcessosMostrados.Count == 1;
            await j.DigitarPesquisaAsync("");
            var termosOk = vazioEmDespachos && voltouTermo;

            // Com Processos à vista, os comandos do relatório (Excluir, Desfazer) não mexem no relatório escondido.
            var historicoAntes = s.Revisao.HistoricoCompleto.Count;
            var incluidosAntes = s.Revisao.Projeto.Incluidos.Count;
            await j.ExecutarComandoAsync(ComandosApp.InicioExcluirRestaurar);
            await j.ExecutarComandoAsync(ComandosApp.EditarDesfazer);
            var escondidoIntacto = s.Revisao.HistoricoCompleto.Count == historicoAntes && s.Revisao.Projeto.Incluidos.Count == incluidosAntes
                && !j.InfoMensagemAtual.IsOpen && j.ModuloAtual == Modulo.Processos;

            // A pesquisa de Relatórios volta como estava: o termo digitado nas folhas é lembrado ao sair e voltar.
            await j.MudarModuloAsync(Modulo.Relatorios);
            await j.DigitarPesquisaAsync("verificacao");
            var total = j.PesquisaTotal;
            await j.MudarModuloAsync(Modulo.Processos);
            await j.MudarModuloAsync(Modulo.Relatorios);
            var pesquisaVolta = total > 0 && j.TermoPesquisa == "verificacao" && j.TextoResultadoPesquisa == $"1 de {total}";
            await j.DigitarPesquisaAsync("");
            p["conferencias"] = new JsonObject
            {
                ["faixas_e_dicas"] = tudoOk, ["aba_lembrada"] = abaLembrada, ["termo_por_modulo"] = termosOk, ["relatorio_escondido_intacto"] = escondidoIntacto,
                ["pesquisa_do_relatorio_volta"] = pesquisaVolta,
            };
            p["ok"] = tudoOk && abaLembrada && termosOk && escondidoIntacto && pesquisaVolta;

        });

        await Passo("35_pastas_intactas", async p =>
        {
            // Fecha o relatório (solta a trava) e compara a árvore com a de antes de tudo: só os .pmvrel das importações são novos.
            if (!await j.FecharSessaoAsync()) throw new InvalidOperationException("não fechou");
            var depois = FotografarArvore(copiaProcessos);
            var novos = depois.Keys.Except(antesDasPastas.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
            var sumiram = antesDasPastas.Keys.Except(depois.Keys, StringComparer.OrdinalIgnoreCase).ToList();
            var mudaram = antesDasPastas.Where(kv => depois.TryGetValue(kv.Key, out var sha) && sha != kv.Value).Select(kv => kv.Key).ToList();
            p["arquivos_antes"] = antesDasPastas.Count;
            p["arquivos_depois"] = depois.Count;
            p["novos"] = new JsonArray(novos.Select(n => (JsonNode)n).ToArray());
            p["sumiram"] = new JsonArray(sumiram.Select(n => (JsonNode)n).ToArray());
            p["mudaram"] = new JsonArray(mudaram.Select(n => (JsonNode)n).ToArray());
            var esperadosNovos = new[]
            {
                Path.Combine("2099", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)", "Relatorio_0001_2099.pmvrel"),
                Path.Combine("2099", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)", "Relatorio_0001_2099_2.pmvrel"),
            };
            // Nenhum .lock, .bak ou .tmp ficou ao lado dos pacotes depois de fechar.
            var restos = depois.Keys.Where(k => k.EndsWith(".lock", StringComparison.OrdinalIgnoreCase) || k.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                || k.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)).ToList();
            p["restos"] = new JsonArray(restos.Select(n => (JsonNode)n).ToArray());
            p["ok"] = novos.SequenceEqual(esperadosNovos.Order(StringComparer.Ordinal), StringComparer.OrdinalIgnoreCase) && sumiram.Count == 0 && mudaram.Count == 0 && restos.Count == 0;
        });

        await j.FecharSessaoAsync();
        saida["perguntas_feitas"] = new JsonArray(perguntas.Feitas.Select(s => (JsonNode)s).ToArray());
        saida["falhas"] = falhas;
        saida["ok"] = falhas.Count == 0;
        Gravar(args.Roteiro!, saida);
        Application.Current.Exit();
    }

    /// <summary>Copia a árvore (pastas e arquivos, ocultos inclusive) para um destino novo.</summary>
    static void CopiarArvore(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var d in Directory.GetDirectories(origem, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destino, Path.GetRelativePath(origem, d)));
        foreach (var f in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
            File.Copy(f, Path.Combine(destino, Path.GetRelativePath(origem, f)));
    }

    /// <summary>Apaga uma árvore que o próprio roteiro criou antes (arquivos ocultos ou só-leitura inclusive).</summary>
    static void ApagarArvore(string pasta)
    {
        foreach (var f in Directory.GetFiles(pasta, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(pasta, true);
    }

    /// <summary>Caminho relativo → sha256 de cada arquivo da árvore (para provar que o app só leu).</summary>
    static Dictionary<string, string> FotografarArvore(string raiz) =>
        Directory.GetFiles(raiz, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(raiz, f), f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))), StringComparer.OrdinalIgnoreCase);

    /// <summary>As linhas do diário (aberto pela sessão para escrita; lido sem atrapalhar).</summary>
    static string[] LinhasDoDiario(string arquivo)
    {
        if (!File.Exists(arquivo)) return [];
        using var f = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var r = new StreamReader(f);
        return r.ReadToEnd().Split('\n').Where(l => l.Trim().Length > 0).ToArray();
    }

    /// <summary>Uma trava "de outro" no formato da <see cref="Trava"/> (datas UTC com 7 casas).</summary>
    static void EscreverTrava(string caminho, string maquina, string usuario, int pid, DateTimeOffset inicio, DateTimeOffset batimento)
    {
        static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
        if (File.Exists(caminho)) { File.SetAttributes(caminho, FileAttributes.Normal); File.Delete(caminho); }
        File.WriteAllText(caminho, new JsonObject { ["maquina"] = maquina, ["usuario"] = usuario, ["pid"] = pid, ["inicio_utc"] = Utc(inicio), ["batimento_utc"] = Utc(batimento) }.ToJsonString());
    }

    static int? LerPidDaTrava(string caminho)
    {
        try
        {
            using var f = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonNode.Parse(f)?["pid"]?.GetValue<int>();
        }
        catch (Exception e) when (e is IOException or JsonException) { return null; }
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
