using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.Web.WebView2.Core;
using Pmvni.Agentes;
using Pmvni.Composicao.Web;

namespace Pmvni.App;

public sealed partial class PaginaNovoRelatorio
{
    const string OrigemMonitor = "https://monitor.pmvni.invalid";
    internal Task<CoreWebView2Environment>? AmbienteMonitor { get; set; }
    internal event EventHandler? EvidenciasSolicitadas;
    readonly DispatcherTimer pulsoHtml = new() { Interval = TimeSpan.FromMilliseconds(750) };
    bool navegandoHtml, abrindoHtml, htmlPronto, eventosWebLigados;
    object? ultimaVistaHtml;
    (string Decorrido, string Previsao) ultimoRelogioHtml;
    long versaoLogHtml, execucaoHtml;
    static readonly JsonSerializerOptions jsonMonitor = new(JsonSerializerDefaults.Web);

    async Task AbrirMonitorHtmlAsync()
    {
        if (abrindoHtml || navegandoHtml || AmbienteMonitor is null) return;
        if (htmlPronto) { AtualizarMonitorHtml(); pulsoHtml.Start(); return; }
        abrindoHtml = true;
        try
        {
            ErroWebMonitor.Visibility = Visibility.Collapsed;
            var ambiente = await AmbienteMonitor;
            if (WebMonitor.CoreWebView2 is null)
                await WebMonitor.EnsureCoreWebView2Async(ambiente, FabricaWebView2.OpcoesController(ambiente));
            var core = WebMonitor.CoreWebView2 ?? throw new InvalidOperationException("WebView2 indisponível.");
            if (!eventosWebLigados)
            {
                core.AddWebResourceRequestedFilter(OrigemMonitor + "/*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += ServirRecursoMonitor;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.NavigationStarting += (_, e) => e.Cancel = e.Uri != OrigemMonitor + "/monitor.html";
                core.NewWindowRequested += (_, e) => e.Handled = true;
                core.WebMessageReceived += MensagemMonitor;
                core.NavigationCompleted += (_, e) =>
                {
                    if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled) FalhaWeb(new IOException(e.WebErrorStatus.ToString()));
                };
                pulsoHtml.Tick += (_, _) => AtualizarMonitorHtml();
                Unloaded += (_, _) => PararMonitorHtml();
                Loaded += (_, _) => { if (VistaDaExecucao) _ = AbrirMonitorHtmlAsync(); };
                eventosWebLigados = true;
            }
            navegandoHtml = true;
            core.Navigate(OrigemMonitor + "/monitor.html");
        }
        catch (Exception ex) { FalhaWeb(ex); }
        finally { abrindoHtml = false; }
    }

    static void ServirRecursoMonitor(CoreWebView2 core, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var uri = new Uri(e.Request.Uri);
        var nome = uri.AbsolutePath.TrimStart('/');
        var tipo = nome switch
        {
            "monitor.html" => "text/html; charset=utf-8",
            "monitor.css" => "text/css; charset=utf-8",
            "monitor.js" => "text/javascript; charset=utf-8",
            "Inter-Regular.ttf" or "Inter-Medium.ttf" or "Inter-SemiBold.ttf" => "font/ttf",
            _ => null
        };
        var arquivo = tipo is null ? null : typeof(PaginaNovoRelatorio).Assembly.GetManifestResourceStream("monitor." + nome);
        e.Response = arquivo is null
            ? core.Environment.CreateWebResourceResponse(null, 404, "Not found", "")
            : core.Environment.CreateWebResourceResponse(arquivo.AsRandomAccessStream(), 200, "OK", "Content-Type: " + tipo);
    }

    void FalhaWeb(Exception ex)
    {
        RegistroErros.Gravar("monitor HTML", ex);
        navegandoHtml = false;
        htmlPronto = false;
        PararMonitorHtml();
        ErroWebMonitor.Visibility = Visibility.Visible;
    }

    void RecarregarMonitor(object sender, RoutedEventArgs e) => _ = AbrirMonitorHtmlAsync();
    void PararMonitorHtml() { pulsoHtml.Stop(); ultimaVistaHtml = null; }

    void MensagemMonitor(CoreWebView2 core, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.Source != OrigemMonitor + "/monitor.html") return;
        var mensagem = e.TryGetWebMessageAsString();
        if (mensagem == "pronto")
        {
            navegandoHtml = false;
            ErroWebMonitor.Visibility = Visibility.Collapsed;
            htmlPronto = true;
            ultimaVistaHtml = null;
            if (VistaDaExecucao) { AtualizarMonitorHtml(); pulsoHtml.Start(); }
        }
        else if (mensagem == "evidencias" && BotaoEvidenciasPdf.Visibility == Visibility.Visible)
            EvidenciasSolicitadas?.Invoke(this, EventArgs.Empty);
        else if (mensagem.StartsWith("topo:") && double.TryParse(mensagem.AsSpan(5),
            System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var topo) && double.IsFinite(topo))
        {
            BotoesExecucao.Margin = new Thickness(0, 0, Math.Max(8, MonitorHtml.ActualWidth * .02), 0);
            BotoesExecucao.Translation = new System.Numerics.Vector3(0, (float)topo, 0);
            BotoesExecucao.Visibility = Visivel(topo > -BotoesExecucao.ActualHeight);
        }
        else if (AbasMonitor.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Content, mensagem)) is { } item)
            AbasMonitor.SelectedItem = item;
    }

    void AtualizarMonitorHtml()
    {
        if (!htmlPronto || !VistaDaExecucao || !IsLoaded) return;
        var aba = (AbasMonitor.SelectedItem as NavigationViewItem)?.Content as string ?? "Resumo";
        var concluida = etapasMostradas.Length > 0 && etapasMostradas.All(e => e.Estado == EstadoEtapa.Feita);
        var dicaPrevisao = AutomationProperties.GetHelpText(TextoTempoEstimado);
        var relogio = (Decorrido: TextoTempoDecorrido.Text, Previsao: concluida ? "Concluída" : TextoTempoEstimado.Text);
        var vista = new { aba, execucaoHtml, versaoLogHtml, etapasMostradas, ProgressoAtual, EstimativaAtual, detalhesEtapas,
            consumoExibido, orcamentoExibido, achadosExibidos, emCurso, proc = ProcessoMonitor.Text, pessoa = TextoRequerente.Text,
            estado = TituloStatus.Text, progresso = TextoTotalEtapas.Text, erro = ErroDetalhes.Text,
            mensagem = TextoUltimaMensagem.Text, PainelUltimaMensagem.Visibility, evidencias = BotaoEvidenciasPdf.Visibility };
        if (Equals(vista, ultimaVistaHtml))
        {
            if (ultimoRelogioHtml != relogio)
            {
                ultimoRelogioHtml = relogio;
                WebMonitor.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { clock = true, elapsed = relogio.Decorrido, eta = relogio.Previsao, etaHelp = dicaPrevisao }));
            }
            return;
        }
        var etapas = etapasMostradas.Select((e, i) =>
        {
            var id = IdDaEtapa(e.Nome);
            var linha = linhasEtapas.ElementAtOrDefault(i);
            return new { name = e.Nome, state = e.Estado.ToString(), status = SituacaoDaEtapa(e.Estado, execucaoAtivaNaBarra),
                description = DescricaoDaEtapa(e.Nome), percent = PercentualDaEtapa(e.Nome,e.Estado).Valor,
                result = linha?.Resultado.Text, executor = linha?.Executor.Text,
                scope = id switch
                {
                    "prepare" => "Identificar as peças atuais, organizar as pranchas e levantar os documentos, as cotas e os dados de enquadramento que precisam de conferência.",
                    "analyze" => "Conferir as 28 seções, localizar cada achado na peça e na página e registrar as evidências e os cálculos que sustentam o relatório.",
                    "technical_audit" => "Confrontar cada achado com a figura citada, reabrir o original quando necessário e conferir o mérito, as referências e os cálculos. Registrar as objeções que restarem.",
                    "audit" => "Conferir a integridade do manifesto e dos controles certificados na auditoria técnica. Registrar divergências que impeçam a produção.",
                    "produce" => "Montar o PDF no modelo oficial e conferir se os textos, cálculos e figuras reproduzem os dados aprovados.",
                    "final_review" => "Inspecionar cada página do PDF e verificar as assinaturas de integridade dos arquivos antes da entrega.",
                    _ => DescricaoDaEtapa(e.Nome)
                },
                plannedWork = id != null ? detalhesEtapas.DescricoesDoPlano.GetValueOrDefault(id) : null,
                record = id != null ? detalhesEtapas.Registros.GetValueOrDefault(id) : null,
                objections = detalhesEtapas.Objecoes.Where(o => o.Etapa == id).Select(FluxoNovoRelatorio.TextoObjecao),
                measures = (consumoExibido ?? []).Where(u => u.Etapa == id).SelectMany(u => TextoConsumo.Metricas(u).Select(m => $"{m.Rotulo} · {m.Valor}")),
                plans = (consumoExibido ?? []).Where(u => u.Etapa == id && u.Plano != null).Select(u => u.Plano).Distinct(),
                fronts = id == "analyze" ? detalhesEtapas.Frentes : [] };
        }).ToArray();
        var dados = new {
            aba, run = execucaoHtml, process = ProcessoMonitor.Text, applicant = TextoRequerente.Text, live = emCurso,
            status = TituloStatus.Text, etaHelp = dicaPrevisao, elapsed = relogio.Decorrido, eta = relogio.Previsao,
            percent = TextoPercentual.Text, value = BarraResumoEtapas.Value, current = TextoEtapaAtual.Text,
            progress = TextoTotalEtapas.Text, estimated = RotuloEstimado.Visibility == Visibility.Visible,
            count = ContagemAtividades.Text, stages = etapas,
            findings = aba == "Resumo" ? achadosExibidos.Select(a => new { id = a.Id, title = a.Titulo,
                classification = Situacoes[a.Situacao].Texto, color = Situacoes[a.Situacao].Cor, verified = a.Verificado }).ToArray() : null,
            totals = new[]{TotalAtende.Text,TotalNaoAtende.Text,TotalComprovar.Text,TotalDivergentes.Text},
            evidence = BotaoEvidenciasPdf.Visibility == Visibility.Visible,
            events = aba == "Log" ? atividades.Select(i => i.Atividade).Select(a => new { time = a.HoraTexto,
                type = LinhaAndamento.Classificar(a).Nome,
                message = string.Join(" — ", new[]{a.Titulo,a.Detalhe}.Where(t => !string.IsNullOrWhiteSpace(t))) }).ToArray() : null,
            last = HoraUltimoEvento.Text, plan = detalhesEtapas.Plano.Select(FluxoNovoRelatorio.NomeDaEtapa),
            warnings = detalhesEtapas.Validacoes, error = ErroDetalhes.Text,
            lastMessage = PainelUltimaMensagem.Visibility == Visibility.Visible ? TextoUltimaMensagem.Text : null,
            usage = aba == "Consumo" ? DadosConsumoHtml() : null
        };
        var json = JsonSerializer.Serialize(dados,jsonMonitor);
        WebMonitor.CoreWebView2.PostWebMessageAsJson(json);
        ultimaVistaHtml = vista;
        ultimoRelogioHtml = relogio;
    }

    internal async Task CapturarMonitorHtmlAsync(string pasta, string pagina)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < limite)
        {
            if (htmlPronto && await WebMonitor.ExecuteScriptAsync(
                "document.querySelector('article .board')?.parentElement.getAttribute('aria-label')") == JsonSerializer.Serialize(pagina))
            {
                using var stream = File.Create(ArquivoGerado.Caminho(Path.Combine(pasta, $"html-{pagina}.png")));
                await WebMonitor.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream.AsRandomAccessStream());
                return;
            }
            await Task.Delay(100);
        }
        throw new TimeoutException($"A página {pagina} não foi renderizada no monitor.");
    }

    object DadosConsumoHtml()
    {
        var uso = consumoExibido ?? [];
        var ia = uso.Where(e => e.Executor != UsoDasIas.Programa).ToArray();
        var cache = EntradasComCache(ia);
        var soma = cache is { } c ? c.Nova + c.Cache : 0;
        return new {
            budget = orcamentoExibido,
            input = TextoConsumo.Numero(SomaMedida(ia.Select(e => e.Entrada))),
            output = TextoConsumo.Numero(SomaMedida(ia.Select(e => e.Saida))),
            cached = TextoConsumo.Numero(SomaMedida(ia.Select(e => e.CacheLido))),
            fresh = TextoConsumo.Numero(cache is { } t ? (long)t.Nova : null),
            cachePercent = soma > 0 ? 100 * cache!.Value.Cache / soma : (decimal?)null,
            outputPercent = soma > 0 && ia.All(e => e.Saida.HasValue) ? 100 * ia.Sum(e => (decimal)e.Saida!.Value) / soma : (decimal?)null,
            rows = uso.Select(e => new { stage = FluxoNovoRelatorio.NomeDaEtapa(e.Etapa), executor = NomeExecutor(e),
                details = TextoConsumo.Metricas(e).Select(m => $"{m.Rotulo} · {m.Valor}"),
                local = e.Executor == UsoDasIas.Programa, input = TextoConsumo.Numero(e.Entrada), output = TextoConsumo.Numero(e.Saida),
                cache = TextoConsumo.Numero(e.CacheLido), curta = e.Cota5h, semanal = e.CotaSemana, e.Usada5h, e.UsadaSemana,
                seconds = e.Duracao?.TotalSeconds, time = e.Duracao is { } d ? FluxoNovoRelatorio.TempoLegivel(d,true) : "—",
                cpu = $"{Ghz(e.CpuMediaMhz)} · pico {Ghz(e.CpuPicoMhz)}", ram = e.RamPicoBytes is { } b ? $"{b/1048576d:0.#} MiB" : "—" })
        };
    }
}
