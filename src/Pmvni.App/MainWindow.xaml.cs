using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Windows.Storage.Pickers;

namespace Pmvni.App;

/// <summary>Resultado de abrir um relatório: render, configuração das folhas, plano composto (no host oculto) e o que a tela mostra.</summary>
internal sealed record Aberto(string Caminho, ResultadoRender Render, ConfigFolhas Config, DadosRelatorio Dados, ResultadoComposicao Composicao,
    ResultadoAplicacao Aplicacao, IReadOnlyList<ItemNavegacao> Navegacao, IReadOnlyList<string> ErrosFinais);

/// <summary>Tela do editor: o relatório inteiro como folhas A4 empilhadas (WebView2 visível), lista de seções à esquerda, zoom e
/// indicador de folha no rodapé e exportação do PDF. Somente leitura. Fluxo: renderiza (kit) → compõe no host oculto → aplica o
/// mesmo plano no WebView2 visível. Nada disso roda na thread da interface: só os awaits voltam a ela.</summary>
public sealed partial class MainWindow : Window
{
    static readonly double[] PassosZoom = [0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3];
    static readonly TimeSpan LimiteTela = TimeSpan.FromSeconds(15);

    readonly ObservableCollection<ItemNavegacao> itensNavegacao = [];
    readonly DispatcherQueueTimer reajuste;
    ModeloCarregado? modelo;
    HostOculto? hostComposicao;
    DocumentoWeb? documento;
    Aberto? aberto;
    bool ocupado, fechada;
    double zoom = 1;
    string modoZoom = "manual"; // manual | largura | pagina
    int folhaAtual;

    internal Aberto? Aberto => aberto;
    internal DocumentoWeb? Documento => documento;
    internal double Zoom => zoom;
    internal int FolhaAtual => folhaAtual;
    internal bool Ocupado => ocupado;
    internal string TextoEstadoAtual => TextoEstado.Text;
    internal string TextoTituloAtual => TextoTitulo.Text;
    internal string TextoFolhaAtual => TextoFolha.Text;
    internal bool ExportarHabilitado => BotaoExportar.IsEnabled;
    internal string DicaExportar => ToolTipService.GetToolTip(InvolucroExportar) as string ?? "";
    internal IReadOnlyList<ItemNavegacao> Itens => itensNavegacao;
    internal InfoBar InfoLargura => AvisoLargura;
    internal InfoBar InfoEstouro => AvisoEstouro;
    internal InfoBar InfoForaDaArea => AvisoForaDaArea;
    internal InfoBar InfoMensagem => AvisoMensagem;
    internal FrameworkElement ElementoRaiz => Raiz;
    internal CoreWebView2 NucleoWeb => Navegador.CoreWebView2;
    internal HostOculto? HostComposicao => hostComposicao;
    internal Button[] BotoesZoom => [BotaoMenos, BotaoMais, BotaoLargura, BotaoPagina];

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(ZonaDeArrasto);
        Raiz.Loaded += (_, _) => DimensionarJanela();
        ListaNavegacao.ItemsSource = itensNavegacao;
        reajuste = DispatcherQueue.GetForCurrentThread().CreateTimer();
        reajuste.Interval = TimeSpan.FromMilliseconds(250);
        reajuste.IsRepeating = false;
        reajuste.Tick += async (_, _) => { if (modoZoom != "manual") await TentarAsync(() => AjustarAsync(modoZoom)); };
        AreaCentral.SizeChanged += (_, _) => { if (modoZoom != "manual") reajuste.Start(); };
        Raiz.ActualThemeChanged += (_, _) => AplicarTemaWeb();
        Closed += (_, _) =>
        {
            fechada = true;
            try { documento?.Dispose(); } catch { }
            try { hostComposicao?.Dispose(); } catch { }
        };
        DefinirDicaExportar("Abra um relatório para exportar o PDF.");
        _ = IniciarAsync();
    }

    // ---------- inicialização ----------

    /// <summary>Tamanho inicial em DIPs (1600 x 1000), reduzido para caber na área de trabalho do monitor.</summary>
    void DimensionarJanela()
    {
        var escala = Raiz.XamlRoot?.RasterizationScale ?? 1.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Min(1600 * escala, area.Width * 0.96), (int)Math.Min(1000 * escala, area.Height * 0.96)));
    }

    async Task IniciarAsync()
    {
        try
        {
            if (FabricaWebView2.VersaoDisponivel() is null)
            {
                if (Autoteste.Ativo) { Autoteste.GravarErro("WebView2 Runtime ausente."); Application.Current.Exit(); return; }
                MostrarMensagem(InfoBarSeverity.Error, "Falta o WebView2 Runtime", FabricaWebView2.ExplicacaoSemRuntime);
                BotaoAbrir.IsEnabled = false;
                return;
            }
            // Mesma fábrica do host oculto (mesmas opções de ambiente e de controller, InPrivate) e mesma pasta de dados:
            // as duas instâncias do WebView2 convivem no mesmo processo, e nada do relatório fica gravado no perfil.
            var ambiente = await FabricaWebView2.CriarAmbienteAsync();
            await Navegador.EnsureCoreWebView2Async(ambiente, FabricaWebView2.OpcoesController(ambiente));
            if (Autoteste.Ativo)
            {
                Navegador.CoreWebView2.NavigationCompleted += (_, e) =>
                {
                    if (e.IsSuccess) Autoteste.GravarSucesso(ambiente.BrowserVersionString);
                    else Autoteste.GravarErro("NavigationCompleted: " + e.WebErrorStatus);
                    Application.Current.Exit();
                };
                Navegador.NavigateToString("<p>ok</p>");
                return;
            }

            documento = new DocumentoWeb(Navegador.CoreWebView2); // aplica a sandbox ao controle visível
            Navegador.CoreWebView2.Settings.IsZoomControlEnabled = false; // o zoom é só o da tela do editor (transform), nunca o do navegador
            Navegador.CoreWebView2.Settings.IsPinchZoomEnabled = false;
            documento.Recebida += AoReceberDaWeb;
            AplicarTemaWeb();

            var args = Argumentos.Atual;
            if (args.Roteiro is not null) await Roteiro.ExecutarAsync(this, args);
            else if (args.Dados is not null) await AbrirAsync(args.Dados);
        }
        catch (Exception e)
        {
            if (Autoteste.Ativo) { Autoteste.GravarErro(e.ToString()); Application.Current.Exit(); return; }
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível iniciar a visualização", e.Message);
        }
    }

    /// <summary>O tema do app vai para a página (prefers-color-scheme): só o fundo fora das folhas muda; as folhas seguem brancas.
    /// O perfil InPrivate não repassa <c>Profile.PreferredColorScheme</c> à página depois de criado: o tema vai também pela
    /// emulação do CDP (só a característica prefers-color-scheme; a mídia continua a de tela).</summary>
    async void AplicarTemaWeb()
    {
        try
        {
            if (Navegador.CoreWebView2 is not { } web) return;
            var escuro = Raiz.ActualTheme == ElementTheme.Dark;
            web.Profile.PreferredColorScheme = escuro ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            await web.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia",
                "{\"features\":[{\"name\":\"prefers-color-scheme\",\"value\":\"" + (escuro ? "dark" : "light") + "\"}]}");
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[tema] " + e.Message); }
    }

    internal void DefinirTema(ElementTheme tema) => Raiz.RequestedTheme = tema;

    // ---------- abrir ----------

    async void AoAbrir(object sender, RoutedEventArgs e) => await TentarAsync(AbrirPeloSeletorAsync);

    async Task AbrirPeloSeletorAsync()
    {
        var seletor = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(seletor, WinRT.Interop.WindowNative.GetWindowHandle(this));
        seletor.FileTypeFilter.Add(".json");
        var arquivo = await seletor.PickSingleFileAsync();
        if (arquivo is not null) await AbrirAsync(arquivo.Path);
    }

    /// <summary>Abre o relatório: renderiza, compõe no host oculto e aplica o mesmo plano na tela. Devolve null se falhar
    /// (o motivo aparece num InfoBar).</summary>
    internal async Task<Aberto?> AbrirAsync(string caminho)
    {
        if (ocupado || documento is null) return null;
        Ocupar("Lendo o relatório…");
        var tocouATela = false;
        var usouHost = false;
        try
        {
            modelo ??= await Task.Run(ModeloOficial.Carregar);
            var m = modelo;
            // Rascunho: a conferência do kit não recusa dados incompletos (o editor serve justamente para completá-los).
            var (render, config, dados, errosFinais) = await Task.Run<(ResultadoRender, ConfigFolhas?, DadosRelatorio?, IReadOnlyList<string>)>(() =>
            {
                var r = RelatorioRenderer.Renderizar(caminho, new OpcoesRender(false, true), m);
                if (!r.Ok) return (r, null, null, []);
                // A conferência sem rascunho diz o que impediria o PDF final (campos [ ] não preenchidos).
                var final = RelatorioRenderer.Renderizar(caminho, new OpcoesRender(false, false), m);
                return (r, ConfigFolhas.PadraoDeDados(m, caminho), DadosRelatorio.Ler(caminho), final.Erros);
            });
            if (!render.Ok || config is null || dados is null)
            {
                MostrarMensagem(InfoBarSeverity.Error, "O relatório tem erros de conferência", string.Join(Environment.NewLine, render.Erros.Take(8)));
                return null;
            }

            Ocupar("Compondo as folhas…");
            if (hostComposicao is { Inutilizavel: true }) await DescartarHostComposicaoAsync(); // processo do WebView2 caiu
            hostComposicao ??= await HostOculto.CriarAsync(1024, 1400);
            usouHost = true;
            await hostComposicao.Documento.CarregarAsync(render, config);
            var composicao = await hostComposicao.Documento.ComporDetalhadoAsync();

            Ocupar("Montando a visualização…");
            tocouATela = true;
            await documento.CarregarAsync(render, config);
            var aplicacao = await documento.AplicarPlanoAsync(composicao.Plano);
            zoom = 1;
            modoZoom = "manual";
            var nav = dados.Navegacao(render, composicao);
            aberto = new Aberto(caminho, render, config, dados, composicao, aplicacao, nav, errosFinais);
            if (fechada) return null;
            LimparAvisos();

            itensNavegacao.Clear();
            foreach (var item in nav) itensNavegacao.Add(item);
            MostrarEstado(aberto);
            // 100 %, ou o que couber na largura se a janela for mais estreita que a folha.
            await ZoomAsync(new { ajustar = "largura", maximo = 1.0, transbordam = aplicacao.FolhasMarcadas });
            TextoVazio.Visibility = Visibility.Collapsed;
            return aberto;
        }
        catch (Exception e)
        {
            // Falha depois de mexer na tela: nada de estado meio aberto (o que sobrou não corresponde a nenhum relatório).
            if (tocouATela) Reiniciar();
            // Composição que falhou, caiu ou estourou o tempo: o host pode ter ficado inutilizável; o próximo abrir cria outro.
            if (usouHost && !tocouATela) await DescartarHostComposicaoAsync();
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível abrir o relatório", e.Message);
            return null;
        }
        finally { Liberar(); }
    }

    async Task DescartarHostComposicaoAsync()
    {
        var h = hostComposicao;
        hostComposicao = null;
        if (h is not null) try { await h.DisposeAsync(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine("[host] " + e.Message); }
    }

    /// <summary>Exportar só com o relatório composto sem estouro e sem erros da conferência final (campos [ ] em aberto).</summary>
    static bool PodeExportar(Aberto? a) => a is not null && a.Composicao.Plano.Estouros.Count == 0 && a.ErrosFinais.Count == 0;

    void MostrarEstado(Aberto a)
    {
        if (fechada) return;
        var plano = a.Composicao.Plano;
        TextoTitulo.Text = a.Dados.Titulo;
        Title = a.Dados.Titulo + " — Editor de Relatórios PMVNI";
        var estouros = plano.Estouros.Count;
        TextoEstado.Text = estouros == 0
            ? $"Composto: {a.Composicao.Folhas} folhas"
            : estouros == 1 ? "1 bloco excede a página" : $"{estouros} blocos excedem a página";
        if (a.ErrosFinais.Count > 0) TextoEstado.Text += " · " + DescreverRascunho(a.ErrosFinais);

        if (plano.AvisosLargura.Count > 0)
        {
            var reducao = (int)Math.Round((1 - 1 / plano.Escala) * 100, MidpointRounding.AwayFromZero);
            AvisoLargura.Title = "";
            AvisoLargura.Message = $"Conteúdo mais largo que a página: o documento foi reduzido {reducao}% para caber (como na impressão do kit).";
            AvisoLargura.IsOpen = true;
        }

        var foraDaArea = a.Aplicacao.FolhasQueTransbordamNaLargura;
        if (foraDaArea.Count > 0)
        {
            AvisoForaDaArea.Title = "";
            AvisoForaDaArea.Message = DescreverForaDaArea(foraDaArea);
            AvisoForaDaArea.IsOpen = true;
        }

        if (estouros > 0)
        {
            AvisoEstouro.Title = estouros == 1 ? "Há um bloco maior que a página" : $"Há {estouros} blocos maiores que a página";
            AvisoEstouro.Message = string.Join(Environment.NewLine, plano.Estouros.Select(DescreverEstouro));
            AvisoEstouro.IsOpen = true;
            DefinirDicaExportar("A exportação está bloqueada: " + string.Join(" ", plano.Estouros.Select(DescreverEstouro))
                + " Corrija o conteúdo para exportar o PDF.");
        }
        else if (a.ErrosFinais.Count > 0)
            DefinirDicaExportar("A exportação está bloqueada: " + DescreverRascunho(a.ErrosFinais)
                + ". O PDF final só sai com o relatório completo: preencha os campos e abra o relatório de novo.");
        else DefinirDicaExportar("Exportar o relatório em PDF (as mesmas folhas da tela).");
        BotaoExportar.IsEnabled = PodeExportar(a);
    }

    /// <summary>"Folha 14: há conteúdo além da borda lateral da área útil …" (informativo: nada é cortado no editor).</summary>
    internal static string DescreverForaDaArea(IReadOnlyList<int> folhas) =>
        (folhas.Count == 1 ? $"Folha {folhas[0]}: há conteúdo" : $"Folhas {string.Join(", ", folhas)}: há conteúdo")
        + " além da borda lateral da área útil (marcado com a faixa vermelha). Nada é cortado no PDF do editor; o gerador do kit cortaria esse trecho.";

    /// <summary>"Rascunho: há campos [ ] não preenchidos (N)" quando a conferência sem rascunho recusa colchetes.</summary>
    internal static string DescreverRascunho(IReadOnlyList<string> erros)
    {
        var colchetes = erros.FirstOrDefault(e => e.StartsWith("Campos ainda não preenchidos", StringComparison.Ordinal));
        if (colchetes is null) return $"Rascunho: a conferência do kit tem {erros.Count} erro(s)";
        var n = colchetes[(colchetes.IndexOf(':') + 1)..].Split("; ").Length;
        return $"Rascunho: há campos [ ] não preenchidos ({n}{(colchetes.EndsWith('…') ? "+" : "")})";
    }

    void Reiniciar()
    {
        aberto = null;
        itensNavegacao.Clear();
        LimparAvisos();
        TextoTitulo.Text = "Editor de Relatórios Técnicos — PMVNI";
        TextoEstado.Text = "";
        TextoFolha.Text = "";
        BotaoExportar.IsEnabled = false;
        DefinirDicaExportar("Abra um relatório para exportar o PDF.");
        TextoVazio.Visibility = Visibility.Visible;
    }

    static string DescreverEstouro(Estouro e)
    {
        var bloco = e.Ref is null ? "bloco sem identificação" : $"bloco '{e.Ref}'";
        var ci = CultureInfo.GetCultureInfo("pt-BR");
        return e.LarguraMm is { } l
            ? string.Format(ci, "Folha {0}: {1} mais largo que a página ({2:0.#} mm).", e.Folha, bloco, l)
            : string.Format(ci, "Folha {0}: {1} não cabe na página (altura do conteúdo: {2:0.#} mm).", e.Folha, bloco, e.AlturaMm);
    }

    void DefinirDicaExportar(string texto) => ToolTipService.SetToolTip(InvolucroExportar, texto);

    // ---------- exportar ----------

    async void AoExportar(object sender, RoutedEventArgs e) => await TentarAsync(ExportarPeloSeletorAsync);

    async Task ExportarPeloSeletorAsync()
    {
        if (aberto is null) return;
        var seletor = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        WinRT.Interop.InitializeWithWindow.Initialize(seletor, WinRT.Interop.WindowNative.GetWindowHandle(this));
        seletor.FileTypeChoices.Add("PDF", [".pdf"]);
        var nome = aberto.Dados.Processo is { } p ? "Relatório " + string.Concat(p.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)) : "Relatório";
        seletor.SuggestedFileName = nome;
        var arquivo = await seletor.PickSaveFileAsync();
        if (arquivo is null) return;
        var ok = (await ExportarParaAsync(arquivo.Path)).Ok;
        // O seletor cria o arquivo vazio; se a exportação não gravou nada, não deixa o zero byte para trás.
        if (!ok) try { if (new FileInfo(arquivo.Path).Length == 0) File.Delete(arquivo.Path); } catch { }
    }

    /// <summary>Exporta o relatório aberto para <paramref name="caminhoPdf"/> (host oculto novo; recusa com estouro).
    /// Devolve o resultado, ou null se foi recusada ou falhou (o motivo aparece num InfoBar).</summary>
    internal async Task<(bool Ok, ResultadoExportacao? Resultado, string? Erro, Exception? Excecao)> ExportarParaAsync(string caminhoPdf)
    {
        if (aberto is null) return (false, null, "Nenhum relatório aberto.", null);
        if (ocupado) return (false, null, "A janela está ocupada.", null);
        Ocupar("Exportando o PDF…");
        AvisoMensagem.IsOpen = false;
        try
        {
            // Exportação final: re-renderiza SEM rascunho; campos [ ] não preenchidos bloqueiam (sem confirmar nem ignorar).
            // D2: o PDF tem de ser o que a tela mostra; se o arquivo mudou desde a abertura, a exportação recusa.
            var x = await Exportador.ExportarFinalAsync(aberto.Caminho, aberto.Config, caminhoPdf, aberto.Composicao.Plano, modelo);
            MostrarMensagem(InfoBarSeverity.Success, "PDF exportado", $"{x.Folhas} folhas em {x.CaminhoPdf}. Recibo: {Path.GetFileName(x.CaminhoRecibo)}.");
            return (true, x, null, null);
        }
        catch (ConferenciaFinalException e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Exportação bloqueada: o relatório ainda não está completo", string.Join(Environment.NewLine, e.Erros));
            return (false, null, e.Message, e);
        }
        catch (ExportacaoRecusadaException e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Exportação recusada", e.Message);
            return (false, null, e.Message, e);
        }
        catch (ExportacaoDivergenteException e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Exportação recusada", e.Message);
            return (false, null, e.Message, e);
        }
        catch (Exception e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível exportar o PDF", e.Message);
            return (false, null, e.Message, e);
        }
        finally { Liberar(); }
    }

    // ---------- zoom, rolagem e folha visível ----------

    async void AoAumentar(object sender, RoutedEventArgs e) =>
        await TentarAsync(() => ZoomAsync(new { zoom = PassosZoom.FirstOrDefault(p => p > zoom + 0.001, PassosZoom[^1]) }, "manual"));

    async void AoDiminuir(object sender, RoutedEventArgs e)
    {
        if (zoom <= PassosZoom[0] + 0.001) return; // piso do zoom manual: 50 % (os ajustes podem ir a 25 %)
        await TentarAsync(() => ZoomAsync(new { zoom = PassosZoom.LastOrDefault(p => p < zoom - 0.001, PassosZoom[0]) }, "manual"));
    }

    async void AoAjustarLargura(object sender, RoutedEventArgs e) => await TentarAsync(() => AjustarAsync("largura"));
    async void AoPaginaInteira(object sender, RoutedEventArgs e) => await TentarAsync(() => AjustarAsync("pagina"));

    Task AjustarAsync(string modo) => ZoomAsync(new { ajustar = modo }, modo);

    /// <summary>Manda o zoom à página web (transform: scale; nunca recompõe) e atualiza o rodapé com a resposta.</summary>
    internal async Task ZoomAsync(object pedido, string? modo = null)
    {
        if (documento is null) return;
        var d = await documento.ChamarAsync("definirZoom", pedido, LimiteTela);
        if (modo is not null) modoZoom = modo;
        AplicarEstadoDaWeb(d);
    }

    internal async Task RolarParaAsync(ItemNavegacao item)
    {
        if (documento is null) return;
        var d = item.Ref is null
            ? await documento.ChamarAsync("rolarPara", new { folha = item.Folha }, LimiteTela)
            : await documento.ChamarAsync("rolarPara", new { @ref = item.Ref }, LimiteTela);
        AplicarEstadoDaWeb(d);
    }

    async void AoClicarItem(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ItemNavegacao item) await TentarAsync(() => RolarParaAsync(item));
    }

    void AoReceberDaWeb(Pmvni.Composicao.Web.Ponte.MensagemWeb m)
    {
        if (m.Tipo == "folhaVisivel") AplicarEstadoDaWeb(m.Dados);
    }

    void AplicarEstadoDaWeb(JsonElement d)
    {
        if (fechada) return;
        if (d.TryGetProperty("zoom", out var z) && z.ValueKind == JsonValueKind.Number)
        {
            zoom = z.GetDouble();
            TextoZoom.Text = Math.Round(zoom * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
            BotaoMenos.IsEnabled = zoom > PassosZoom[0] + 0.001;
            BotaoMais.IsEnabled = zoom < PassosZoom[^1] - 0.001;
            BotaoLargura.IsEnabled = BotaoPagina.IsEnabled = true;
        }
        if (d.TryGetProperty("folha", out var f) && f.ValueKind == JsonValueKind.Number && d.TryGetProperty("total", out var t))
        {
            folhaAtual = f.GetInt32();
            TextoFolha.Text = $"folha {folhaAtual} de {t.GetInt32()}";
            MarcarItemAtual(folhaAtual);
        }
    }

    /// <summary>O item do trilho da folha na tela: o primeiro que começa nela ou, se nenhum, o último que começou antes.</summary>
    void MarcarItemAtual(int folha)
    {
        ItemNavegacao? escolhido = itensNavegacao.FirstOrDefault(i => i.Folha == folha) ?? itensNavegacao.LastOrDefault(i => i.Folha < folha);
        foreach (var i in itensNavegacao) i.Atual = ReferenceEquals(i, escolhido);
    }

    void AoAlternarPainel(object sender, RoutedEventArgs e) =>
        PainelNavegacao.Visibility = PainelNavegacao.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

    // ---------- estado da janela ----------

    void Ocupar(string texto)
    {
        if (fechada) return;
        ocupado = true;
        TextoEspera.Text = texto;
        Anel.IsActive = true;
        Espera.Visibility = Visibility.Visible;
        BotaoAbrir.IsEnabled = false;
        BotaoExportar.IsEnabled = false;
    }

    void Liberar()
    {
        if (fechada) return;
        ocupado = false;
        Anel.IsActive = false;
        Espera.Visibility = Visibility.Collapsed;
        BotaoAbrir.IsEnabled = true;
        BotaoExportar.IsEnabled = PodeExportar(aberto);
    }

    void LimparAvisos()
    {
        AvisoLargura.IsOpen = false;
        AvisoEstouro.IsOpen = false;
        AvisoForaDaArea.IsOpen = false;
        AvisoMensagem.IsOpen = false;
    }

    void MostrarMensagem(InfoBarSeverity severidade, string titulo, string mensagem)
    {
        if (fechada) return;
        AvisoMensagem.Severity = severidade;
        AvisoMensagem.Title = titulo;
        AvisoMensagem.Message = mensagem;
        AvisoMensagem.IsOpen = true;
    }

    async Task TentarAsync(Func<Task> acao)
    {
        try { await acao(); }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "A operação falhou", e.Message); }
    }
}
