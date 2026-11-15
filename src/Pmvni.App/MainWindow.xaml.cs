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
    bool ocupado, fechada, fecharDeVez, fecharQuandoLivre;
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
        Faixa.ComandoInvocado += async id => await ExecutarComandoAsync(id);
        Faixa.AbaMudou += nome => { ajustes.AbaPorModulo[moduloAtual] = nome; SalvarAjustes(); };
        TrilhoModulos.Montar(Faixas.Disponiveis);
        TrilhoModulos.ModuloEscolhido += async m => await TentarAsync(() => MudarModuloAsync(m));
        MontarModulo(Faixas.Disponiveis.Contains(ajustes.Modulo) ? ajustes.Modulo : Faixas.Disponiveis[0]);

        AppWindow.Closing += AoFecharJanela;
        Closed += (_, _) =>
        {
            fechada = true;
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

    internal Task MudarModuloAsync(Modulo m)
    {
        if (!Faixas.Disponiveis.Contains(m)) throw new ArgumentOutOfRangeException(nameof(m), m, "Módulo indisponível.");
        if (m != moduloAtual || Faixa.Definicao?.Modulo != m) MontarModulo(m);
        return Task.CompletedTask;
    }

    void MontarModulo(Modulo m)
    {
        moduloAtual = m;
        R.Visibility = m == Modulo.Relatorios ? Visibility.Visible : Visibility.Collapsed;
        var f = Faixas.Para(m);
        ajustes.AbaPorModulo.TryGetValue(m, out var aba);
        Faixa.Montar(f, Faixas.IndiceDaAba(f, aba));
        TrilhoModulos.Marcar(m);
        RegistrarAtalhos(f);
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
    internal async Task ExecutarComandoAsync(string id)
    {
        if (!manipuladores.TryGetValue(id, out var acao) || !Faixa.Habilitado(id)) return;
        await TentarAsync(acao);
    }

    /// <summary>Ids de comando de alguma faixa (dos módulos disponíveis e de Ajustes) sem manipulador: tem de ser vazia.</summary>
    internal IReadOnlyList<string> ComandosSemManipulador() =>
        Faixas.Disponiveis.Append(Modulo.Ajustes).Distinct()
            .SelectMany(m => Faixas.Comandos(Faixas.Para(m)))
            .Where(c => !(c.Tipo == TipoComando.Menu && c.Itens is not null))
            .Select(c => c.Id).Distinct().Where(id => !manipuladores.ContainsKey(id)).ToList();

    /// <summary>Atalhos de teclado dos comandos da faixa do módulo (com o foco fora das folhas; dentro delas a página repassa a tecla).</summary>
    void RegistrarAtalhos(DefinicaoFaixa f)
    {
        Raiz.KeyboardAccelerators.Clear();
        foreach (var c in Faixas.Comandos(f).Where(c => c.Atalho is not null))
        {
            foreach (var (mods, tecla) in Teclas(c.Atalho!))
            {
                var k = new KeyboardAccelerator { Modifiers = mods, Key = tecla };
                var id = c.Id;
                k.Invoked += async (_, a) => { a.Handled = true; await ExecutarComandoAsync(id); };
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
        Faixa.Habilitar(ComandosApp.ArquivoExportarPdf, temRelatorio && PodeExportar(sessao?.Atual));
        Faixa.Habilitar(ComandosApp.ArquivoFechar, pronto && sessao is not null);
        Faixa.Habilitar(ComandosApp.ExibirMenos, temRelatorio && zoom > PassosZoom[0] + 0.001);
        Faixa.Habilitar(ComandosApp.ExibirMais, temRelatorio && zoom < PassosZoom[^1] - 0.001);
        Faixa.Habilitar(ComandosApp.ExibirLargura, temRelatorio);
        Faixa.Habilitar(ComandosApp.ExibirPagina, temRelatorio);
        Faixa.Ligar(ComandosApp.ExibirPainel, PainelVisivel);
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
        if (ocupado || documento is null) return false;
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
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível importar o dados.json", e.Message);
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

    /// <summary>Abre o pacote: fecha o atual (gravando o que faltar), lê, recompõe e aplica na tela. Devolve false se falhar (o motivo
    /// aparece num InfoBar).</summary>
    internal async Task<bool> AbrirPacoteAsync(string pmvrel)
    {
        if (ocupado || documento is null) return false;
        if (sessao is not null && !await FecharSessaoAsync()) return false;
        Ocupar("Abrindo o relatório…");
        SessaoEdicao? nova = null;
        try
        {
            modelo ??= await Task.Run(ModeloOficial.Carregar);
            nova = await SessaoEdicao.AbrirAsync(pmvrel, modelo, this);
            Ocupar("Compondo as folhas…");
            zoom = 1;
            modoZoom = "manual";
            var r = await nova.RecomporAsync();
            if (fechada) { await nova.DisposeAsync(); return false; }
            sessao = nova;
            nova = null;
            sessao.Mudou += AtualizarBarra;
            LimparAvisos();
            MostrarRecomposicao(r);
            if (sessao.SomenteLeitura)
                MostrarMensagem(InfoBarSeverity.Warning, "Aberto somente para leitura", sessao.MotivoSomenteLeitura ?? "O pacote não pode ser alterado.");
            R.Navegador.Opacity = 1;
            R.TextoVazio.Visibility = Visibility.Collapsed;
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
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível abrir o relatório", e is RecomposicaoRecusadaException rr ? string.Join(Environment.NewLine, rr.Erros.Take(8)) : e.Message);
            return false;
        }
        finally
        {
            if (nova is not null) await nova.DisposeAsync();
            Liberar();
        }
    }

    /// <summary>Fecha o relatório aberto. Alterações não gravadas: grava sozinho; só pergunta se a gravação falhar. Devolve false se
    /// o usuário preferiu não fechar.</summary>
    internal async Task<bool> FecharSessaoAsync()
    {
        if (sessao is not { } s) return true;
        if (s.Estado is EstadoGravacao.NaoSalvo or EstadoGravacao.Conflito)
        {
            string? erro = null;
            if (s.Estado == EstadoGravacao.NaoSalvo)
                try { await s.GravarAsync(); } catch (Exception e) { erro = e.Message; }
            if (s.Estado != EstadoGravacao.Salvo
                && !await Perguntas.Atual.ConfirmarAsync("Fechar sem salvar?",
                    "As alterações não puderam ser gravadas" + (erro is null ? "." : ": " + erro) + " Fechar mesmo assim descarta essas alterações.",
                    "Fechar sem salvar", "Cancelar"))
                return false;
        }
        sessao = null;
        s.Mudou -= AtualizarBarra;
        Reiniciar();
        await s.DisposeAsync();
        return true;
    }

    async void AoFecharJanela(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (fecharDeVez || (sessao is null && !ocupado)) return;
        args.Cancel = true;
        if (ocupado)
        {
            // Uma operação em andamento (abrir, exportar…) termina antes: a janela fecha logo depois (Liberar).
            fecharQuandoLivre = true;
            MostrarMensagem(InfoBarSeverity.Informational, "Aguarde", "A janela fecha assim que a operação em andamento terminar.");
            return;
        }
        if (await FecharSessaoAsync()) { fecharDeVez = true; Close(); }
    }

    // ---------- gravar ----------

    async Task SalvarAsync()
    {
        if (sessao is null) return;
        try { await sessao.GravarAsync(); }
        catch (ConflitoNoDiscoException)
        {
            MostrarMensagem(InfoBarSeverity.Error, "O arquivo mudou no disco",
                "Outro programa alterou o relatório depois que ele foi aberto; nada foi sobrescrito. Use Salvar como para guardar a sua versão.");
        }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "Não foi possível salvar", e.Message); }
    }

    async Task SalvarComoAsync()
    {
        if (sessao is null) return;
        var destino = await Perguntas.Atual.SalvarArquivoAsync(Path.GetFileName(sessao.Caminho), ".pmvrel");
        if (destino is null) return;
        if (!string.Equals(Path.GetFullPath(destino), sessao.Caminho, StringComparison.OrdinalIgnoreCase) && !DestinoNovo(destino)) return;
        try
        {
            await sessao.GravarComoAsync(destino);
            AtualizarBarra();
            MostrarMensagem(InfoBarSeverity.Success, "Relatório salvo", sessao.Caminho);
        }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "Não foi possível salvar", e.Message); }
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
        itensNavegacao.Clear();
        foreach (var item in r.Navegacao) itensNavegacao.Add(item);
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
            Faixa.DefinirDica(ComandosApp.ArquivoExportarPdf, "A exportação está bloqueada: " + DescreverRascunho(r.Preparado.ErrosFinais)
                + ". O PDF final só sai com o relatório completo.");
        // O motivo do bloqueio também à vista (um botão desabilitado não mostra a dica): o estouro já tem o seu aviso.
        R.AvisoBloqueio.IsOpen = estouros == 0 && r.Preparado.ErrosFinais.Count > 0;
        if (R.AvisoBloqueio.IsOpen)
            R.AvisoBloqueio.Message = DescreverRascunho(r.Preparado.ErrosFinais) + ". O PDF final só sai com o relatório completo: "
                + string.Join(" ", r.Preparado.ErrosFinais.Distinct().Take(3)) + (r.Preparado.ErrosFinais.Distinct().Count() > 3 ? " …" : "");
        else Faixa.DefinirDica(ComandosApp.ArquivoExportarPdf, "Exportar o relatório em PDF (as mesmas folhas da tela).");
        AtualizarBarra();
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
        itensNavegacao.Clear();
        LimparAvisos();
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
        if (sessao?.Atual is not { } r) return (false, null, "Nenhum relatório aberto.", null);
        if (ocupado) return (false, null, "A janela está ocupada.", null);
        var avisos = r.Preparado.AvisosExportacao;
        if (r.Preparado.ErrosFinais.Count == 0 && avisos.Count > 0
            && !await Perguntas.Atual.ConfirmarAsync("Exportar com avisos?", string.Join(Environment.NewLine, avisos), "Exportar", "Cancelar"))
            return (false, null, "Exportação cancelada.", null);
        Ocupar("Exportando o PDF…");
        R.AvisoMensagem.IsOpen = false;
        try
        {
            // O recibo leva o sha256 do pacote gravado: o que ainda não foi gravado vai antes para o arquivo.
            if (sessao.Estado == EstadoGravacao.NaoSalvo) await sessao.GravarAsync();
            var x = await sessao.ExportarAsync(caminhoPdf);
            MostrarMensagem(InfoBarSeverity.Success, "PDF exportado", $"{x.Folhas} folhas em {x.CaminhoPdf}. Recibo: {Path.GetFileName(x.CaminhoRecibo)}.");
            return (true, x, null, null);
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
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível exportar o PDF", e.Message);
            return (false, null, e.Message, e);
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
        if (e.ClickedItem is ItemNavegacao item) await TentarAsync(() => RolarParaAsync(item));
    }

    async void AoReceberDaWeb(Pmvni.Composicao.Web.Ponte.MensagemWeb m)
    {
        switch (m.Tipo)
        {
            case "folhaVisivel":
                AplicarEstadoDaWeb(m.Dados);
                break;
            case "atalho":
                // Tecla dentro das folhas (o WebView2 tem o foco): o comando da faixa do módulo com esse atalho.
                if (m.Dados.TryGetProperty("tecla", out var t) && t.ValueKind == JsonValueKind.String && Faixa.Definicao is { } f
                    && Faixas.Comandos(f).FirstOrDefault(c => c.Atalho == t.GetString()) is { } cmd)
                    await ExecutarComandoAsync(cmd.Id);
                break;
            case "editar":
            case "edicaoCancelada":
                // A edição no A4 chega na Tarefa 9; até lá a tela volta ao estado do host (sem o estado, a página fica parada).
                if (sessao is not null) await sessao.EnviarEstadoAsync();
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
        if (Faixa.Definicao is { } f)
            foreach (var c in Faixas.Comandos(f).Where(c => c.Atalho is not null))
            {
                var linha = new Grid { ColumnSpacing = 24 };
                linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                linha.Children.Add(new TextBlock { Text = c.Rotulo });
                var tecla = new TextBlock { Text = c.Atalho!.Replace("Plus", "+").Replace("Minus", "−"), Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] };
                Grid.SetColumn(tecla, 1);
                linha.Children.Add(tecla);
                lista.Children.Add(linha);
            }
        return MostrarDialogoAsync("Atalhos de teclado", lista);
    }

    Task MostrarSobreAsync()
    {
        var versao = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
            ?? typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "?";
        var webview = R.Navegador.CoreWebView2?.Environment.BrowserVersionString ?? FabricaWebView2.VersaoDisponivel() ?? "ausente";
        var texto = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Text = $"Editor de Relatórios Técnicos — PMVNI{Environment.NewLine}{Environment.NewLine}"
                + $"Versão do app: {versao}{Environment.NewLine}"
                + $"Modelo do relatório: rev{ModeloOficial.Revisao}{Environment.NewLine}"
                + $"WebView2: {webview}",
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
        if (fecharQuandoLivre) { fecharQuandoLivre = false; DispatcherQueue.TryEnqueue(Close); }
    }

    void LimparAvisos()
    {
        R.AvisoLargura.IsOpen = false;
        R.AvisoEstouro.IsOpen = false;
        R.AvisoForaDaArea.IsOpen = false;
        R.AvisoBloqueio.IsOpen = false;
        R.AvisoMensagem.IsOpen = false;
    }

    void MostrarMensagem(InfoBarSeverity severidade, string titulo, string mensagem)
    {
        if (fechada) return;
        R.AvisoMensagem.Severity = severidade;
        R.AvisoMensagem.Title = titulo;
        R.AvisoMensagem.Message = mensagem;
        R.AvisoMensagem.IsOpen = true;
    }

    async Task TentarAsync(Func<Task> acao)
    {
        try { await acao(); }
        catch (Exception e) { MostrarMensagem(InfoBarSeverity.Error, "A operação falhou", e.Message); }
    }
}
