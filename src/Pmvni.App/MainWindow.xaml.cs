using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;
using VirtualKey = Windows.System.VirtualKey;
using VirtualKeyModifiers = Windows.System.VirtualKeyModifiers;

namespace Pmvni.App;

/// <summary>Shell do app (desenho §5.5, §9, §9.1): barra de título (ícone, "Proc. NNNN/AAAA · Requerente", estado de gravação), faixa de
/// opções do módulo (por dados: <see cref="Faixas"/>), trilho de módulos (<see cref="Faixas.Disponiveis"/>) e as páginas, criadas uma vez
/// e trocadas por Visibility. Módulo Relatórios: o pacote <c>.pmvrel</c> (importar, abrir, salvar, salvar como, exportar, fechar) numa
/// <see cref="SessaoEdicao"/> e as folhas A4 na <see cref="PaginaRelatorios"/>. Nada pesado roda na thread da interface: só os awaits
/// voltam a ela.</summary>
public sealed partial class MainWindow : Window, ITelaEdicao
{
    const string TituloPadrao = "Editor de Relatórios PMVNI";
    static readonly double[] PassosZoom = [0.5, 0.67, 0.75, 0.8, 0.9, 1, 1.1, 1.25, 1.5, 1.75, 2, 2.5, 3];
    static readonly TimeSpan LimiteTela = TimeSpan.FromSeconds(15);

    readonly ObservableCollection<ItemNavegacao> itensNavegacao = [];
    readonly DispatcherQueueTimer reajuste;
    readonly Dictionary<string, Func<Task>> manipuladores = new(StringComparer.Ordinal);
    readonly Ajustes ajustes;
    ModeloCarregado? modelo;
    HostOculto? hostComposicao;
    DocumentoWeb? documento;
    SessaoEdicao? sessao;
    Modulo moduloAtual = Modulo.Relatorios;
    bool ocupado, fechada, fecharDeVez, fechando;
    double zoom = 1;
    string modoZoom = "manual"; // manual | largura | pagina
    int folhaAtual;

    PaginaRelatorios R => Relatorios;

    // ---------- acessores do roteiro ----------
    internal SessaoEdicao? Sessao => sessao;
    internal Recomposicao? Aberto => sessao?.Atual;
    internal DocumentoWeb? Documento => documento;
    internal double Zoom => zoom;
    internal int FolhaAtual => folhaAtual;
    internal bool Ocupado => ocupado;
    internal string TextoEstadoAtual => BarraTitulo.Subtitle ?? "";
    internal string TextoTituloAtual => BarraTitulo.Title ?? "";
    internal string TextoComposicaoAtual => R.TextoComposicao.Text;
    internal string TextoFolhaAtual => R.TextoFolha.Text;
    internal bool ExportarHabilitado => Faixa.Habilitado(ComandosApp.ArquivoExportarPdf);
    internal string DicaExportar => Faixa.Dica(ComandosApp.ArquivoExportarPdf);
    internal IReadOnlyList<ItemNavegacao> Itens => itensNavegacao;
    internal InfoBar InfoLargura => R.AvisoLargura;
    internal InfoBar InfoEstouro => R.AvisoEstouro;
    internal InfoBar InfoForaDaArea => R.AvisoForaDaArea;
    internal InfoBar InfoBloqueio => R.AvisoBloqueio;
    internal InfoBar InfoMensagem => R.AvisoMensagem;
    internal FrameworkElement ElementoRaiz => Raiz;
    internal CoreWebView2 NucleoWeb => R.Navegador.CoreWebView2;
    internal HostOculto? HostComposicao => hostComposicao;
    internal Button[] BotoesZoom => [R.BotaoMenos, R.BotaoMais, R.BotaoLargura, R.BotaoPagina];
    internal Modulo ModuloAtual => moduloAtual;
    internal FaixaDeOpcoes FaixaAtual => Faixa;
    internal Trilho TrilhoAtual => TrilhoModulos;
    internal bool PainelVisivel => R.PainelNavegacao.Visibility == Visibility.Visible;

    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(BarraTitulo);
        Perguntas.Atual = new PerguntasDaJanela(this, () => Raiz.XamlRoot);
        ajustes = CarregarAjustes();
        AplicarTemaDosAjustes();
        Raiz.Loaded += (_, _) => DimensionarJanela();
        Raiz.SizeChanged += (_, e) => Faixa.Compacta = e.NewSize.Width < 1000;
        R.ListaNavegacao.ItemsSource = itensNavegacao;
        R.ListaNavegacao.ItemClick += AoClicarItem;
        R.BotaoMenos.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.ExibirMenos);
        R.BotaoMais.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.ExibirMais);
        R.BotaoLargura.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.ExibirLargura);
        R.BotaoPagina.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.ExibirPagina);
        R.Navegador.Opacity = 0; // nada aberto: só o texto de "importe ou abra"
        reajuste = DispatcherQueue.GetForCurrentThread().CreateTimer();
        reajuste.Interval = TimeSpan.FromMilliseconds(250);
        reajuste.IsRepeating = false;
        reajuste.Tick += async (_, _) => { if (modoZoom != "manual" && sessao?.Atual is not null) await TentarAsync(() => AjustarAsync(modoZoom)); };
        R.AreaCentral.SizeChanged += (_, _) => { if (modoZoom != "manual") reajuste.Start(); };
        Raiz.ActualThemeChanged += (_, _) => AplicarTemaWeb();

        RegistrarManipuladores();
        RegistrarManipuladoresRevisao();
        IniciarRevisao();
        RegistrarAtalhos();
        IniciarGravacao();
        IniciarPesquisa();
        IniciarAjustes();
        IniciarPastas();
        IniciarNovoDespacho();
        Faixa.ComandoInvocado += async id => await ExecutarComandoAsync(id);
        Faixa.AbaMudou += nome => { ajustes.AbaPorModulo[moduloAtual] = nome; SalvarAjustes(); };
        TrilhoModulos.Montar(Faixas.Disponiveis);
        TrilhoModulos.ModuloEscolhido += async m => await TentarAsync(() => MudarModuloAsync(m));
        MontarModulo(Faixas.Disponiveis.Contains(ajustes.Modulo) ? ajustes.Modulo : Faixas.Disponiveis[0]);

        AppWindow.Closing += AoFecharJanela;
        Closed += (_, _) =>
        {
            fechada = true;
            leituraDaLista?.Cancel(); // a leitura das pastas de processos em segundo plano para junto com a janela
            leituraDoDetalhe?.Cancel();
            // Um preparo em curso para (até ~3 s; senão a árvore do soffice é encerrada já, antes de o processo terminar) e a pasta de
            // trabalho do despacho sai.
            novo?.EncerrarAoFechar(TimeSpan.FromSeconds(3));
            PararTemporizadores();
            try { documento?.Dispose(); } catch { }
            try { hostComposicao?.Dispose(); } catch { }
        };
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
                AtualizarComandos();
                return;
            }
            // Mesma fábrica do host oculto (mesmas opções de ambiente e de controller, InPrivate) e mesma pasta de dados:
            // as duas instâncias do WebView2 convivem no mesmo processo, e nada do relatório fica gravado no perfil.
            var ambiente = await FabricaWebView2.CriarAmbienteAsync();
            await R.Navegador.EnsureCoreWebView2Async(ambiente, FabricaWebView2.OpcoesController(ambiente));
            if (Autoteste.Ativo)
            {
                R.Navegador.CoreWebView2.NavigationCompleted += (_, e) =>
                {
                    if (e.IsSuccess) Autoteste.GravarSucesso(ambiente.BrowserVersionString);
                    else Autoteste.GravarErro("NavigationCompleted: " + e.WebErrorStatus);
                    Application.Current.Exit();
                };
                R.Navegador.NavigateToString("<p>ok</p>");
                return;
            }

            documento = new DocumentoWeb(R.Navegador.CoreWebView2); // aplica a sandbox ao controle visível
            R.Navegador.CoreWebView2.Settings.IsZoomControlEnabled = false; // o zoom é só o da tela do editor (transform), nunca o do navegador
            R.Navegador.CoreWebView2.Settings.IsPinchZoomEnabled = false;
            documento.Recebida += AoReceberDaWeb;
            AplicarTemaWeb();
            AtualizarComandos();

            var args = Argumentos.Atual;
            if (args.Roteiro is not null) await Roteiro.ExecutarAsync(this, args);
            else if (args.Dados is { } arquivo)
            {
                if (string.Equals(Path.GetExtension(arquivo), ".pmvrel", StringComparison.OrdinalIgnoreCase)) await AbrirPacoteAsync(arquivo);
                else await ImportarComSeletorAsync(arquivo);
            }
        }
        catch (Exception e)
        {
            if (Autoteste.Ativo) { Autoteste.GravarErro(e.ToString()); Application.Current.Exit(); return; }
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível iniciar a visualização", Erro(e));
        }
    }

    /// <summary>O tema do app vai para a página (prefers-color-scheme): só o fundo fora das folhas muda; as folhas seguem brancas.
    /// O perfil InPrivate não repassa <c>Profile.PreferredColorScheme</c> à página depois de criado: o tema vai também pela
    /// emulação do CDP (só a característica prefers-color-scheme; a mídia continua a de tela).</summary>
    async void AplicarTemaWeb()
    {
        try
        {
            if (R.Navegador.CoreWebView2 is not { } web) return;
            var escuro = Raiz.ActualTheme == ElementTheme.Dark;
            web.Profile.PreferredColorScheme = escuro ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            await web.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia",
                "{\"features\":[{\"name\":\"prefers-color-scheme\",\"value\":\"" + (escuro ? "dark" : "light") + "\"}]}");
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[tema] " + e.Message); }
    }

    internal void DefinirTema(ElementTheme tema) => Raiz.RequestedTheme = tema;

    void AplicarTemaDosAjustes() => Raiz.RequestedTheme = ajustes.Tema switch
    {
        "claro" => ElementTheme.Light,
        "escuro" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    static Ajustes CarregarAjustes()
    {
        try { return Ajustes.Carregar(PastaDados.Raiz); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new Ajustes(); }
    }

    void SalvarAjustes()
    {
        try { ajustes.Salvar(PastaDados.Raiz); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { System.Diagnostics.Debug.WriteLine("[ajustes] " + e.Message); }
    }

    // ---------- módulos e faixa ----------

    internal async Task MudarModuloAsync(Modulo m)
    {
        if (!Faixas.Disponiveis.Contains(m)) throw new ArgumentOutOfRangeException(nameof(m), m, "Módulo indisponível.");
        // Sair de Despachos com um despacho em edição (passo 2 ou 3) pergunta antes de descartar; "Continuar editando" fica onde está.
        if (m != moduloAtual && !await SairDoNovoDespachoAsync())
        {
            TrilhoModulos.Marcar(moduloAtual);
            return;
        }
        // Trocar de módulo grava o que falta (na fila de comandos, como todo Salvar). Seguro sem await: GravarNaVezAsync trata toda
        // exceção (InfoBar de erro ou de conflito) e devolve false.
        if (m != moduloAtual && sessao is { Estado: EstadoGravacao.NaoSalvo }) _ = GravarAgoraAsync();
        if (m != moduloAtual || Faixa.Definicao?.Modulo != m) MontarModulo(m);
    }

    void MontarModulo(Modulo m)
    {
        var anterior = moduloAtual;
        moduloAtual = m;
        R.Visibility = m == Modulo.Relatorios ? Visibility.Visible : Visibility.Collapsed;
        AtualizarPaginasDeDespachos();
        PaginaDeProcessos.Visibility = m == Modulo.Processos ? Visibility.Visible : Visibility.Collapsed;
        PaginaDeAjustes.Visibility = m == Modulo.Ajustes ? Visibility.Visible : Visibility.Collapsed;
        if (m == Modulo.Ajustes) { PaginaDeAjustes.TextoVersoes.Text = TextoDasVersoes(); AtualizarPaginaDePastas(); }
        var f = Faixas.Para(m);
        ajustes.AbaPorModulo.TryGetValue(m, out var aba);
        Faixa.Montar(f, Faixas.IndiceDaAba(f, aba));
        _ = AtualizarModelosAsync(forcar: false); // o registro de modelos do kit, lido uma vez por pasta do kit (habilita o Novo despacho)
        TrilhoModulos.Marcar(m);
        TrocarPesquisaDeModulo(anterior, m);
        EntrarNoModuloDePastas(m);
        SincronizarPastas();
        AtualizarComandos();
        if (ajustes.Modulo != m) { ajustes.Modulo = m; SalvarAjustes(); }
    }

    void RegistrarManipuladores()
    {
        manipuladores[ComandosApp.ArquivoImportar] = ImportarPeloSeletorAsync;
        manipuladores[ComandosApp.ArquivoAbrir] = AbrirPeloSeletorAsync;
        manipuladores[ComandosApp.ArquivoSalvar] = SalvarAsync;
        manipuladores[ComandosApp.ArquivoSalvarComo] = SalvarComoAsync;
        manipuladores[ComandosApp.ArquivoExportarPdf] = ExportarPeloSeletorAsync;
        manipuladores[ComandosApp.ArquivoFechar] = async () => await FecharSessaoAsync();
        manipuladores[ComandosApp.ExibirMenos] = DiminuirAsync;
        manipuladores[ComandosApp.ExibirMais] = () => ZoomAsync(new { zoom = PassosZoom.FirstOrDefault(p => p > zoom + 0.001, PassosZoom[^1]) }, "manual");
        manipuladores[ComandosApp.ExibirLargura] = () => AjustarAsync("largura");
        manipuladores[ComandosApp.ExibirPagina] = () => AjustarAsync("pagina");
        manipuladores[ComandosApp.ExibirPainel] = () => { AlternarPainel(); return Task.CompletedTask; };
        manipuladores[ComandosApp.AjudaAtalhos] = MostrarAtalhosAsync;
        manipuladores[ComandosApp.AjudaSobre] = MostrarSobreAsync;
    }

    /// <summary>Executa o comando da faixa (ou do atalho) pelo id, se ele existe e está habilitado.</summary>
    /// <summary>Na fila de comandos (<see cref="NaFilaAsync"/>): espera o anterior terminar e, na sua vez, confere se está habilitado (o
    /// motivo aparece se não estiver). Ctrl+F foca a caixa de pesquisa da barra de título.</summary>
    internal Task ExecutarComandoAsync(string id) => NaFilaAsync(async () =>
    {
        if (!manipuladores.TryGetValue(id, out var acao)) return;
        // Delete, Ctrl+Z, zoom e os outros comandos do relatório não valem com a lista de processos à vista: o relatório fica escondido.
        if (moduloAtual != Modulo.Relatorios && ComandosApp.SoDoRelatorio(id)) return;
        if (id.StartsWith("processos.", StringComparison.Ordinal) && moduloAtual != Modulo.Processos) return;
        if (id.StartsWith("despachos.", StringComparison.Ordinal) && moduloAtual != Modulo.Despachos && !ComandosApp.ValeForaDeDespachos(id)) return;
        if (!Faixa.Habilitado(id)) { AvisarIndisponivel(id); return; }
        await TentarAsync(acao);
    });

    /// <summary>Ids de comando de alguma faixa (dos módulos disponíveis e de Ajustes) sem manipulador: tem de ser vazia.</summary>
    internal IReadOnlyList<string> ComandosSemManipulador() =>
        Faixas.Disponiveis.Append(Modulo.Ajustes).Distinct()
            .SelectMany(m => Faixas.Comandos(Faixas.Para(m)))
            .Where(c => !(c.Tipo == TipoComando.Menu && c.Itens is not null))
            .Select(c => c.Id).Distinct().Where(id => !manipuladores.ContainsKey(id)).ToList();

    /// <summary>Atalhos de teclado (<see cref="Atalhos.Mapa"/>) com o foco fora das folhas; dentro delas a página repassa a tecla na
    /// mensagem <c>atalho</c>, pelo mesmo mapa. Numa caixa de texto, Delete, Ctrl+Z e Ctrl+Y ficam com ela.</summary>
    void RegistrarAtalhos()
    {
        Raiz.KeyboardAccelerators.Clear();
        foreach (var (atalho, id) in Atalhos.Mapa)
        {
            foreach (var (mods, tecla) in Teclas(atalho))
            {
                var k = new KeyboardAccelerator { Modifiers = mods, Key = tecla };
                k.Invoked += async (_, a) =>
                {
                    if (TeclaDoControleComFoco(id)) { a.Handled = false; return; }
                    a.Handled = true;
                    await ExecutarComandoAsync(id);
                };
                Raiz.KeyboardAccelerators.Add(k);
            }
        }
    }

    static IEnumerable<(VirtualKeyModifiers, VirtualKey)> Teclas(string atalho)
    {
        var partes = atalho.Split('+');
        var mods = VirtualKeyModifiers.None;
        foreach (var p in partes[..^1])
            mods |= p switch { "Ctrl" => VirtualKeyModifiers.Control, "Shift" => VirtualKeyModifiers.Shift, "Alt" => VirtualKeyModifiers.Menu, _ => VirtualKeyModifiers.None };
        switch (partes[^1])
        {
            case "Plus": yield return (mods, (VirtualKey)187); yield return (mods, VirtualKey.Add); break;
            case "Minus": yield return (mods, (VirtualKey)189); yield return (mods, VirtualKey.Subtract); break;
            case "0": yield return (mods, VirtualKey.Number0); yield return (mods, VirtualKey.NumberPad0); break;
            case "ArrowUp": yield return (mods, VirtualKey.Up); break;
            case "ArrowDown": yield return (mods, VirtualKey.Down); break;
            case var t when Enum.TryParse<VirtualKey>(t, out var k): yield return (mods, k); break;
        }
    }

    /// <summary>Habilita cada comando pelo contexto (nada aberto, ocupado, somente leitura, exportação bloqueada).</summary>
    void AtualizarComandos()
    {
        if (fechada) return;
        var pronto = documento is not null && !ocupado;
        var temRelatorio = pronto && sessao?.Atual is not null;
        Faixa.Habilitar(ComandosApp.ArquivoImportar, pronto);
        Faixa.Habilitar(ComandosApp.ArquivoAbrir, pronto);
        Faixa.Habilitar(ComandosApp.ArquivoSalvar, temRelatorio && sessao is { SomenteLeitura: false });
        Faixa.Habilitar(ComandosApp.ArquivoSalvarComo, temRelatorio);
        var desatualizada = sessao is { Desatualizada: true };
        Faixa.Habilitar(ComandosApp.ArquivoExportarPdf, temRelatorio && PodeExportar(sessao?.Atual) && !desatualizada);
        if (desatualizada) Faixa.DefinirDica(ComandosApp.ArquivoExportarPdf, "A exportação está bloqueada: " + SessaoEdicao.TextoDesatualizada);
        R.AvisoDesatualizado.IsOpen = desatualizada;
        Faixa.Habilitar(ComandosApp.ArquivoFechar, pronto && sessao is not null);
        Faixa.Habilitar(ComandosApp.ArquivoRecentes, pronto && haRecentes);
        AtualizarPesquisaHabilitada(temRelatorio);
        Faixa.Habilitar(ComandosApp.ExibirMenos, temRelatorio && zoom > PassosZoom[0] + 0.001);
        Faixa.Habilitar(ComandosApp.ExibirMais, temRelatorio && zoom < PassosZoom[^1] - 0.001);
        Faixa.Habilitar(ComandosApp.ExibirLargura, temRelatorio);
        Faixa.Habilitar(ComandosApp.ExibirPagina, temRelatorio);
        Faixa.Ligar(ComandosApp.ExibirPainel, PainelVisivel);
        AtualizarComandosRevisao(temRelatorio);
        AtualizarComandosPastas(pronto);
        AtualizarComandosNovoDespacho();
        R.BotaoMenos.IsEnabled = Faixa.Habilitado(ComandosApp.ExibirMenos);
        R.BotaoMais.IsEnabled = Faixa.Habilitado(ComandosApp.ExibirMais);
        R.BotaoLargura.IsEnabled = R.BotaoPagina.IsEnabled = temRelatorio;
    }

    // ---------- importar e abrir ----------

    async Task ImportarPeloSeletorAsync()
    {
        var json = await Perguntas.Atual.AbrirArquivoAsync(".json");
        if (json is not null) await ImportarComSeletorAsync(json);
    }

    /// <summary>Importar um dados.json escolhido (pelo seletor ou na linha de comando): pergunta onde gravar o <c>.pmvrel</c> novo, com
    /// o nome <c>Relatorio_NNNN_AAAA.pmvrel</c> do processo da capa.</summary>
    async Task ImportarComSeletorAsync(string json)
    {
        string? processo = null;
        try { processo = DadosRelatorio.DeBytes(await File.ReadAllBytesAsync(json), "").Processo; }
        catch (Exception e) when (e is JsonException or IOException) { /* a importação diz o que há de errado */ }
        var (n, a) = NomesArquivo.DoProcessoDaCapa(processo);
        var pmvrel = await Perguntas.Atual.SalvarArquivoAsync(NomesArquivo.Importacao(n, a), ".pmvrel");
        if (pmvrel is null || !DestinoNovo(pmvrel)) return;
        await ImportarParaAsync(json, pmvrel);
    }

    /// <summary>O seletor de salvar pode criar o arquivo vazio: esse pode sair. Um arquivo com conteúdo nunca é sobrescrito.</summary>
    bool DestinoNovo(string caminho)
    {
        try
        {
            var info = new FileInfo(caminho);
            if (!info.Exists) return true;
            if (info.Length == 0) { info.Delete(); return true; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        MostrarMensagem(InfoBarSeverity.Warning, "O arquivo já existe", $"{Path.GetFileName(caminho)} já existe e não será sobrescrito. Escolha outro nome.");
        return false;
    }

    /// <summary>Importa o dados.json do kit para o <c>.pmvrel</c> novo <paramref name="pmvrel"/> (recusa se ele existe) e o abre.</summary>
    internal async Task<bool> ImportarParaAsync(string json, string pmvrel)
    {
        if (ocupado || aplicando || documento is null) { AvisarOcupado(); return false; }
        Ocupar("Importando o dados.json…");
        try
        {
            modelo ??= await Task.Run(ModeloOficial.Carregar);
            await SessaoEdicao.ImportarAsync(json, pmvrel, modelo);
        }
        catch (ImportacaoRecusadaException e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "O dados.json foi recusado", string.Join(Environment.NewLine, e.Erros.Take(8)));
            return false;
        }
        catch (Exception e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível importar o dados.json", Erro(e));
            return false;
        }
        finally { Liberar(); }
        return await AbrirPacoteAsync(pmvrel);
    }

    async Task AbrirPeloSeletorAsync()
    {
        var arquivo = await Perguntas.Atual.AbrirArquivoAsync(".pmvrel");
        if (arquivo is not null) await AbrirPacoteAsync(arquivo);
    }

    /// <summary>Abre o pacote: fecha o atual (gravando o que faltar), lê e trava, oferece a recuperação do diário, recompõe e aplica na
    /// tela, e começa o diário. Devolve false se falhar (o motivo aparece num InfoBar).</summary>
    internal async Task<bool> AbrirPacoteAsync(string pmvrel)
    {
        if (ocupado || aplicando || documento is null) { AvisarOcupado(); return false; }
        // Abrir um relatório leva a Relatórios: um despacho em edição pergunta antes de ser descartado.
        if (!await SairDoNovoDespachoAsync()) return false;
        if (sessao is not null && !await FecharSessaoAsync()) return false;
        // Abrir (ou importar, ou escolher um recente) com os Ajustes à vista: o relatório aparece no módulo Relatórios.
        if (moduloAtual != Modulo.Relatorios) MontarModulo(Modulo.Relatorios);
        Ocupar("Abrindo o relatório…");
        SessaoEdicao? nova = null;
        try
        {
            modelo ??= await Task.Run(ModeloOficial.Carregar);
            nova = await SessaoEdicao.AbrirAsync(pmvrel, modelo, this, PastaDados.Raiz);
            // Diário deixado por uma queda: antes de compor (a recuperação muda o projeto) e antes de começar o diário novo.
            var avisoRecuperacao = await RecuperarSeHouverAsync(nova);
            Ocupar("Compondo as folhas…");
            zoom = 1;
            modoZoom = "manual";
            var r = await nova.RecomporAsync();
            if (fechada) { await nova.DisposeAsync(); return false; }
            sessao = nova;
            nova = null;
            selecionado = ultimoCampo = itemNasPropriedades = null;
            mostrarOriginal = false;
            LimparAvisos();
            LigarSessao(sessao);
            MostrarRecomposicao(r);
            sessao.IniciarDiario();
            if (sessao.SomenteLeitura) MostrarSomenteLeitura(sessao);
            if (avisoRecuperacao is not null) MostrarMensagem(InfoBarSeverity.Informational, "Recuperação", avisoRecuperacao);
            if (sessao.Estado == EstadoGravacao.NaoSalvo) ReiniciarAutosave(); // o que foi recuperado também é gravado sozinho
            R.Navegador.Opacity = 1;
            R.TextoVazio.Visibility = Visibility.Collapsed;
            RegistrarRecente(sessao.Caminho);
            return true;
        }
        catch (PacoteInvalidoException e)
        {
            Reiniciar();
            MostrarMensagem(InfoBarSeverity.Error, "O arquivo não é um relatório válido", e.Message);
            return false;
        }
        catch (Exception e)
        {
            Reiniciar();
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível abrir o relatório", e is RecomposicaoRecusadaException rr ? string.Join(Environment.NewLine, rr.Erros.Take(8)) : Erro(e));
            return false;
        }
        finally
        {
            if (nova is not null) await nova.DisposeAsync();
            Liberar();
        }
    }

    /// <summary>Fechar a janela (desenho §5.1): cancela o fechamento e, na fila de comandos (depois do que já está nela e de uma operação
    /// em andamento), grava e fecha; só pergunta se a gravação falhar (<see cref="FecharSessaoAsync"/>).</summary>
    async void AoFecharJanela(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (fecharDeVez || (sessao is null && novo is null && !ocupado && naFila == 0)) return;
        args.Cancel = true;
        if (fechando) return;
        fechando = true;
        try
        {
            if (ocupado || naFila > 0)
                MostrarMensagem(InfoBarSeverity.Informational, "Aguarde", "A janela fecha assim que a operação em andamento terminar.");
            var fechar = await NaFilaAsync(async () =>
            {
                // Abrir, importar ou exportar chamados fora da fila (linha de comando, roteiro): termina antes.
                while (ocupado && !fechada) await Task.Delay(100);
                // Um despacho em edição (passo 2 ou 3) pergunta antes de ser descartado; nada dele foi gravado na pasta do processo.
                if (!await SairDoNovoDespachoAsync()) return false;
                return await FecharSessaoAsync();
            });
            if (fechar && !fechada) { fecharDeVez = true; Close(); }
        }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "Não foi possível fechar", Erro(e)); }
        finally { fechando = false; }
    }

    // ---------- gravar ----------

    Task SalvarAsync() => GravarNaVezAsync();

    async Task SalvarComoAsync()
    {
        if (sessao is null) return;
        await ConcluirEdicaoAntesAsync(sessao); // o texto digitado no A4 entra na cópia
        if (sessao is null) return;
        var destino = await Perguntas.Atual.SalvarArquivoAsync(Path.GetFileName(sessao.Caminho), ".pmvrel");
        if (destino is null) return;
        if (sessao.RecusaSalvarComo(destino) is { } recusa)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Não foi possível salvar", recusa);
            return;
        }
        if (!string.Equals(Path.GetFullPath(destino), sessao.Caminho, StringComparison.OrdinalIgnoreCase) && !DestinoNovo(destino)) return;
        try
        {
            await sessao.GravarComoAsync(destino);
            RegistrarRecente(sessao.Caminho);
            AtualizarBarra();
            MostrarMensagem(InfoBarSeverity.Success, "Relatório salvo", sessao.Caminho);
        }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "Não foi possível salvar", Erro(e)); }
    }

    // ---------- estado na tela ----------

    void AtualizarBarra()
    {
        if (fechada) return;
        var r = sessao?.Atual;
        // O nome do arquivo entra no título quando a capa não tem processo nem requerente: segue o arquivo atual (Salvar como).
        var titulo = r is null || sessao is null ? null : TituloJanela.De(r.Dados.Processo, r.Dados.Requerente, Path.GetFileNameWithoutExtension(sessao.Caminho));
        BarraTitulo.Title = titulo ?? TituloPadrao;
        BarraTitulo.Subtitle = sessao is null ? "" : EstadoGravacaoTexto.Texto(sessao.Estado, sessao.SalvoEmLocal);
        Title = titulo is null ? TituloPadrao : titulo + " — " + TituloPadrao;
        AtualizarComandos();
    }

    /// <summary>Exportar só com o relatório composto sem estouro e sem erros finais (campos [ ] em aberto, conferência da revisão).</summary>
    static bool PodeExportar(Recomposicao? r) => r is not null && r.Composicao.Plano.Estouros.Count == 0 && r.Preparado.ErrosFinais.Count == 0;

    void MostrarRecomposicao(Recomposicao r)
    {
        if (fechada) return;
        navegacaoCompleta = r.Navegacao;
        AtualizarPaineis();
        var plano = r.Composicao.Plano;
        var estouros = plano.Estouros.Count;
        R.TextoComposicao.Text = estouros == 0
            ? $"Composto: {r.Composicao.Folhas} folhas"
            : estouros == 1 ? "1 bloco excede a página" : $"{estouros} blocos excedem a página";
        if (r.Preparado.ErrosFinais.Count > 0) R.TextoComposicao.Text += " · " + DescreverRascunho(r.Preparado.ErrosFinais);

        R.AvisoLargura.IsOpen = false;
        if (plano.AvisosLargura.Count > 0)
        {
            var reducao = (int)Math.Round((1 - 1 / plano.Escala) * 100, MidpointRounding.AwayFromZero);
            R.AvisoLargura.Title = "";
            R.AvisoLargura.Message = $"Conteúdo mais largo que a página: o documento foi reduzido {reducao}% para caber (como na impressão do kit).";
            R.AvisoLargura.IsOpen = true;
        }

        var foraDaArea = r.Aplicacao.FolhasQueTransbordamNaLargura;
        R.AvisoForaDaArea.IsOpen = false;
        if (foraDaArea.Count > 0)
        {
            R.AvisoForaDaArea.Title = "";
            R.AvisoForaDaArea.Message = DescreverForaDaArea(foraDaArea);
            R.AvisoForaDaArea.IsOpen = true;
        }

        R.AvisoEstouro.IsOpen = false;
        if (estouros > 0)
        {
            R.AvisoEstouro.Title = estouros == 1 ? "Há um bloco maior que a página" : $"Há {estouros} blocos maiores que a página";
            R.AvisoEstouro.Message = string.Join(Environment.NewLine, plano.Estouros.Select(DescreverEstouro));
            R.AvisoEstouro.IsOpen = true;
            Faixa.DefinirDica(ComandosApp.ArquivoExportarPdf, "A exportação está bloqueada: " + string.Join(" ", plano.Estouros.Select(DescreverEstouro))
                + " Corrija o conteúdo para exportar o PDF.");
        }
        else if (r.Preparado.ErrosFinais.Count > 0)
        {
            Faixa.DefinirDica(ComandosApp.ArquivoExportarPdf, "A exportação está bloqueada: " + DescreverRascunho(r.Preparado.ErrosFinais)
                + ". O PDF final só sai com o relatório completo.");
        }
        else
        {
            Faixa.DefinirDica(ComandosApp.ArquivoExportarPdf, "Exportar o relatório em PDF (as mesmas folhas da tela).");
        }

        // O motivo do bloqueio também à vista (um botão desabilitado não mostra a dica): o estouro já tem o seu aviso.
        R.AvisoBloqueio.IsOpen = estouros == 0 && r.Preparado.ErrosFinais.Count > 0;
        if (R.AvisoBloqueio.IsOpen)
        {
            R.AvisoBloqueio.Message = DescreverRascunho(r.Preparado.ErrosFinais) + ". O PDF final só sai com o relatório completo: "
                + string.Join(" ", r.Preparado.ErrosFinais.Distinct().Take(3)) + (r.Preparado.ErrosFinais.Distinct().Count() > 3 ? " …" : "");
        }
        AtualizarBarra();
        ReaplicarPesquisa();
    }

    /// <summary>"Folha 14: há conteúdo além da borda lateral da área útil …" (informativo: nada é cortado no editor).</summary>
    internal static string DescreverForaDaArea(IReadOnlyList<int> folhas) =>
        (folhas.Count == 1 ? $"Folha {folhas[0]}: há conteúdo" : $"Folhas {string.Join(", ", folhas)}: há conteúdo")
        + " além da borda lateral da área útil (marcado com a faixa vermelha). Nada é cortado no PDF do editor; o gerador do kit cortaria esse trecho.";

    /// <summary>"Rascunho: há campos [ ] não preenchidos (N)" quando a conferência sem rascunho recusa colchetes.</summary>
    internal static string DescreverRascunho(IReadOnlyList<string> erros)
    {
        var colchetes = erros.FirstOrDefault(e => e.StartsWith("Campos ainda não preenchidos", StringComparison.Ordinal));
        if (colchetes is null) return $"Rascunho: a conferência tem {erros.Distinct().Count()} erro(s)";
        var n = colchetes[(colchetes.IndexOf(':') + 1)..].Split("; ").Length;
        return $"Rascunho: há campos [ ] não preenchidos ({n}{(colchetes.EndsWith('…') ? "+" : "")})";
    }

    void Reiniciar()
    {
        if (fechada) return;
        LimparPaineis();
        LimparAvisos();
        LimparPesquisa();
        R.TextoComposicao.Text = "";
        R.TextoFolha.Text = "";
        R.Navegador.Opacity = 0;
        R.TextoVazio.Visibility = Visibility.Visible;
        Faixa.DefinirDica(ComandosApp.ArquivoExportarPdf, "Abra um relatório para exportar o PDF.");
        AtualizarBarra();
    }

    static string DescreverEstouro(Estouro e)
    {
        var bloco = e.Ref is null ? "bloco sem identificação" : $"bloco '{e.Ref}'";
        var ci = CultureInfo.GetCultureInfo("pt-BR");
        return e.LarguraMm is { } l
            ? string.Format(ci, "Folha {0}: {1} mais largo que a página ({2:0.#} mm).", e.Folha, bloco, l)
            : string.Format(ci, "Folha {0}: {1} não cabe na página (altura do conteúdo: {2:0.#} mm).", e.Folha, bloco, e.AlturaMm);
    }

    // ---------- exportar ----------

    async Task ExportarPeloSeletorAsync()
    {
        if (sessao?.Atual is not { } r) return;
        var nome = r.Dados.Processo is { } p ? "Relatório " + string.Concat(p.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c)) : "Relatório";
        var arquivo = await Perguntas.Atual.SalvarArquivoAsync(nome, ".pdf");
        if (arquivo is null) return;
        var ok = (await ExportarParaAsync(arquivo)).Ok;
        // O seletor cria o arquivo vazio; se a exportação não gravou nada, não deixa o zero byte para trás.
        if (!ok) try { if (new FileInfo(arquivo).Length == 0) File.Delete(arquivo); } catch { }
    }

    /// <summary>Exporta o relatório aberto para <paramref name="caminhoPdf"/>: bloqueado com erros finais; avisos (citações a itens
    /// excluídos) pedem confirmação; recusa se o pacote mudou no disco. Devolve o resultado ou o motivo da recusa (também num InfoBar).</summary>
    internal async Task<(bool Ok, ResultadoExportacao? Resultado, string? Erro, Exception? Excecao)> ExportarParaAsync(string caminhoPdf)
    {
        if (sessao?.Atual is null) return (false, null, "Nenhum relatório aberto.", null);
        if (ocupado || aplicando) { AvisarOcupado(); return (false, null, "A janela está ocupada.", null); }
        // O campo em edição no A4 entra antes (como o Enter): o PDF sai com o texto digitado.
        await ConcluirEdicaoAntesAsync(sessao);
        if (sessao?.Atual is not { } r) return (false, null, "Nenhum relatório aberto.", null);
        if (sessao.Desatualizada)
        {
            var desatualizada = new ExportacaoDivergenteException(SessaoEdicao.TextoDesatualizada);
            MostrarMensagem(InfoBarSeverity.Error, "Exportação recusada", desatualizada.Message);
            return (false, null, desatualizada.Message, desatualizada);
        }
        // Alterações que o arquivo não tem e não pode receber agora (trava perdida, conflito): o PDF viria da memória e o recibo levaria
        // o sha de um pacote sem elas. Recusa e aponta Salvar como / Salvar como cópia.
        if (sessao.TemAlteracoesNaoGravadas && (sessao.SomenteLeitura || sessao.Estado == EstadoGravacao.Conflito))
        {
            var semGravar = new ExportacaoDivergenteException(TextosGravacao.ExportarSemGravar(sessao.Estado == EstadoGravacao.Conflito));
            if (sessao.Estado == EstadoGravacao.Conflito) MostrarConflito();
            MostrarMensagem(InfoBarSeverity.Error, "Exportação recusada", semGravar.Message);
            return (false, null, semGravar.Message, semGravar);
        }
        var avisos = r.Preparado.AvisosExportacao;
        if (r.Preparado.ErrosFinais.Count == 0 && avisos.Count > 0
            && !await Perguntas.Atual.ConfirmarAsync("Exportar com avisos?", string.Join(Environment.NewLine, avisos), "Exportar", "Cancelar"))
            return (false, null, "Exportação cancelada.", null);
        Ocupar("Exportando o PDF…");
        R.AvisoMensagem.IsOpen = false;
        try
        {
            // O recibo leva o sha256 do pacote gravado: o que ainda não foi gravado vai antes para o arquivo.
            // Com um salvamento automático em curso, espera ele (a gravação vai na fila do gravador): o disco e o "esperado" batem.
            if (sessao.Estado is EstadoGravacao.NaoSalvo or EstadoGravacao.Salvando) await sessao.GravarAsync();
            var x = await sessao.ExportarAsync(caminhoPdf);
            MostrarMensagem(InfoBarSeverity.Success, "PDF exportado", $"{x.Folhas} folhas em {x.CaminhoPdf}. Recibo: {Path.GetFileName(x.CaminhoRecibo)}.");
            return (true, x, null, null);
        }
        catch (ConflitoNoDiscoException e)
        {
            MostrarConflito();
            MostrarMensagem(InfoBarSeverity.Error, "Exportação recusada", ExportacaoDivergenteException.ArquivoMudou);
            return (false, null, ExportacaoDivergenteException.ArquivoMudou, e);
        }
        catch (ConferenciaFinalException e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Exportação bloqueada: o relatório ainda não está completo", string.Join(Environment.NewLine, e.Erros));
            return (false, null, e.Message, e);
        }
        catch (Exception e) when (e is ExportacaoRecusadaException or ExportacaoDivergenteException)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Exportação recusada", e.Message);
            return (false, null, e.Message, e);
        }
        catch (Exception e)
        {
            var texto = Erro(e);
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível exportar o PDF", texto);
            return (false, null, texto, e);
        }
        finally { Liberar(); }
    }

    // ---------- ITelaEdicao ----------

    DocumentoWeb ITelaEdicao.Tela => documento ?? throw new InvalidOperationException("A tela ainda não está pronta.");

    bool ITelaEdicao.MarcasVisiveis => ajustes.MarcasRevisao;

    async Task<HostOculto> ITelaEdicao.HostAsync()
    {
        if (hostComposicao is { Inutilizavel: true }) await DescartarHostComposicaoAsync(); // processo do WebView2 caiu
        return hostComposicao ??= await HostOculto.CriarAsync(1024, 1400);
    }

    Task ITelaEdicao.DescartarHostAsync() => DescartarHostComposicaoAsync();

    async Task ITelaEdicao.AjustarZoomAsync(bool primeiraVez, ResultadoAplicacao a)
    {
        // Abrir: 100 %, ou o que couber na largura se a janela for mais estreita que a folha. Recompor: o zoom que estava.
        if (primeiraVez) await ZoomAsync(new { ajustar = "largura", maximo = 1.0, transbordam = a.FolhasMarcadas });
        else if (modoZoom == "manual") await ZoomAsync(new { zoom });
        else await ZoomAsync(new { ajustar = modoZoom });
    }

    async Task DescartarHostComposicaoAsync()
    {
        var h = hostComposicao;
        hostComposicao = null;
        if (h is not null) try { await h.DisposeAsync(); } catch (Exception e) { System.Diagnostics.Debug.WriteLine("[host] " + e.Message); }
    }

    // ---------- zoom, rolagem e folha visível ----------

    Task DiminuirAsync()
    {
        if (zoom <= PassosZoom[0] + 0.001) return Task.CompletedTask; // piso do zoom manual: 50 % (os ajustes podem ir a 25 %)
        return ZoomAsync(new { zoom = PassosZoom.LastOrDefault(p => p < zoom - 0.001, PassosZoom[0]) }, "manual");
    }

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
        if (e.ClickedItem is not ItemNavegacao item) return;
        // Item do relatório: seleciona (folhas, Propriedades e faixa) e rola até ele; seção, capa ou figura: só rola.
        if (!item.Secao && item.Ref is not null && sessao?.Revisao.Projeto.Item(item.Ref) is not null) await NaFilaAsync(() => TentarAsync(() => SelecionarAsync(item.Ref)));
        else await TentarAsync(() => RolarParaAsync(item));
    }

    async void AoReceberDaWeb(Pmvni.Composicao.Web.Ponte.MensagemWeb m)
    {
        switch (m.Tipo)
        {
            case "folhaVisivel":
                AplicarEstadoDaWeb(m.Dados);
                break;
            default:
                // Eventos de interação (selecionado, pedirEdicao, editar, edicaoCancelada, atalho, menuContexto, excluidoClicado)
                // chegam com id "evento-N"; as respostas às chamadas do host (mesmos tipos, outro id) já foram entregues a quem chamou.
                if (m.Id.StartsWith("evento-", StringComparison.Ordinal))
                    try { await AoEventoDaPaginaAsync(m); }
                    catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "A operação falhou", Erro(e)); }
                break;
        }
    }

    void AplicarEstadoDaWeb(JsonElement d)
    {
        if (fechada) return;
        if (d.TryGetProperty("zoom", out var z) && z.ValueKind == JsonValueKind.Number)
        {
            zoom = z.GetDouble();
            R.TextoZoom.Text = Math.Round(zoom * 100).ToString("0", CultureInfo.InvariantCulture) + " %";
            AtualizarComandos();
        }
        if (d.TryGetProperty("folha", out var f) && f.ValueKind == JsonValueKind.Number && d.TryGetProperty("total", out var t))
        {
            folhaAtual = f.GetInt32();
            R.TextoFolha.Text = $"folha {folhaAtual} de {t.GetInt32()}";
            MarcarItemAtual(folhaAtual);
        }
    }

    /// <summary>O item da lista da folha na tela: o primeiro que começa nela ou, se nenhum, o último que começou antes.</summary>
    void MarcarItemAtual(int folha)
    {
        ItemNavegacao? escolhido = itensNavegacao.FirstOrDefault(i => i.Folha == folha) ?? itensNavegacao.LastOrDefault(i => i.Folha < folha);
        foreach (var i in itensNavegacao) i.Atual = ReferenceEquals(i, escolhido);
    }

    void AlternarPainel()
    {
        R.PainelNavegacao.Visibility = PainelVisivel ? Visibility.Collapsed : Visibility.Visible;
        Faixa.Ligar(ComandosApp.ExibirPainel, PainelVisivel);
    }

    // ---------- ajuda ----------

    Task MostrarAtalhosAsync()
    {
        var lista = new StackPanel { Spacing = 6 };
        // Só os atalhos que fazem algo (todos, desde a pesquisa da Tarefa 11).
        foreach (var (atalho, id) in Atalhos.Mapa.Where(a => manipuladores.ContainsKey(a.Value)))
        {
            var rotulo = RotuloDoComando(id);
            var linha = new Grid { ColumnSpacing = 24 };
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            linha.Children.Add(new TextBlock { Text = rotulo });
            var tecla = new TextBlock { Text = Atalhos.Legivel(atalho), Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] };
            Grid.SetColumn(tecla, 1);
            linha.Children.Add(tecla);
            lista.Children.Add(linha);
        }
        return MostrarDialogoAsync("Atalhos de teclado", lista);
    }

    Task MostrarSobreAsync()
    {
        var texto = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Editor de Relatórios Técnicos — PMVNI{Environment.NewLine}{Environment.NewLine}" + TextoDasVersoes(),
        };
        return MostrarDialogoAsync("Sobre", texto);
    }

    async Task MostrarDialogoAsync(string titulo, UIElement conteudo)
    {
        var d = new ContentDialog { XamlRoot = Raiz.XamlRoot, Title = titulo, Content = conteudo, CloseButtonText = "Fechar" };
        await d.ShowAsync();
    }

    // ---------- estado da janela ----------

    void Ocupar(string texto)
    {
        if (fechada) return;
        ocupado = true;
        R.TextoEspera.Text = texto;
        R.Anel.IsActive = true;
        R.Espera.Visibility = Visibility.Visible;
        AtualizarComandos();
    }

    void Liberar()
    {
        if (fechada) return;
        ocupado = false;
        R.Anel.IsActive = false;
        R.Espera.Visibility = Visibility.Collapsed;
        AtualizarComandos();
    }

    void AvisarOcupado() =>
        MostrarMensagem(InfoBarSeverity.Warning, "Aguarde", "Outra operação ainda está em andamento; tente de novo quando ela terminar.");

    void LimparAvisos()
    {
        R.AvisoLargura.IsOpen = false;
        R.AvisoEstouro.IsOpen = false;
        R.AvisoForaDaArea.IsOpen = false;
        R.AvisoBloqueio.IsOpen = false;
        R.AvisoMensagem.IsOpen = false;
        R.AvisoConflito.IsOpen = false;
        R.AvisoSomenteLeitura.IsOpen = false;
        R.AvisoDiario.IsOpen = false;
        R.AvisoDesatualizado.IsOpen = false;
    }

    /// <summary>O aviso do módulo à vista: Processos e Despachos têm o seu; os outros usam o da página Relatórios.</summary>
    InfoBar AvisoDoModulo => moduloAtual switch
    {
        Modulo.Processos => PaginaDeProcessos.AvisoMensagem,
        Modulo.Despachos => novo is not null ? PaginaDeNovoDespacho.AvisoMensagem : PaginaDeDespachos.AvisoMensagem,
        _ => R.AvisoMensagem,
    };

    internal InfoBar InfoMensagemAtual => AvisoDoModulo;

    void MostrarMensagem(InfoBarSeverity severidade, string titulo, string mensagem)
    {
        if (fechada) return;
        var aviso = AvisoDoModulo;
        aviso.Severity = severidade;
        aviso.Title = titulo;
        aviso.Message = mensagem;
        aviso.IsOpen = true;
    }

    /// <summary>O texto de uma falha para um aviso: falha de disco ou de permissão em português (o detalhe técnico só no registro);
    /// as exceções do editor, já em português, como vieram.</summary>
    static string Erro(Exception e) => MensagensDeErro.ParaUsuario(e);

    async Task TentarAsync(Func<Task> acao)
    {
        try { await acao(); }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "A operação falhou", Erro(e)); }
    }
}
