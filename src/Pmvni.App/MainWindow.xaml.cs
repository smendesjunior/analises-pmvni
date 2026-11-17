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
/// e trocadas por Visibility. Módulo Relatórios: o pacote <c>.rpmvni</c> (importar, abrir, salvar, salvar como, exportar, fechar) numa
/// <see cref="SessaoEdicao"/> e as folhas A4 na <see cref="PaginaRelatorios"/>. Nada pesado roda na thread da interface: só os awaits
/// voltam a ela.</summary>
public sealed partial class MainWindow : Window, ITelaEdicao
{
    const string TituloPadrao = "Análise de Projetos – PMVNI";
    /// <summary>O título fixo da barra (a marca é o brasão ao lado); o documento aberto vai no subtítulo.</summary>
    const string TituloDaBarra = "Análise de Projetos";
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
    int folhaAtual, totalFolhas;
    bool mudandoCaixaFolha;
    string textoTitulo = TituloPadrao, textoEstado = "";

    PaginaRelatorios R => Relatorios;

    // ---------- acessores do roteiro ----------
    internal SessaoEdicao? Sessao => sessao;
    internal Recomposicao? Aberto => sessao?.Atual;
    internal DocumentoWeb? Documento => documento;
    internal double Zoom => zoom;
    internal int FolhaAtual => folhaAtual;
    internal bool Ocupado => ocupado;
    /// <summary>O estado de gravação ("Salvo às …", "Não salvo"…), como aparece no subtítulo da barra ("" sem relatório).</summary>
    internal string TextoEstadoAtual => textoEstado;
    /// <summary>O título do documento aberto ("Proc. NNNN/AAAA · Requerente"), ou o nome do app sem relatório.</summary>
    internal string TextoTituloAtual => textoTitulo;
    internal string TextoBarraTitulo => TextoMarca.Text;
    /// <summary>O que o cabeçalho mostra depois da marca: processo, requerente e estado de gravação ("" sem relatório).</summary>
    internal string TextoSubtituloBarra => string.Join(" · ", new[] { TextoProcesso.Text, TextoRequerente.Text, textoEstado }.Where(x => x.Length > 0));
    internal string TextoComposicaoAtual => R.TextoComposicao.Text;
    /// <summary>Cabeçalho em duas linhas (correções 2026-11-15): "Proc. NNNN/AAAA" em cima e o requerente embaixo; o texto do selo de gravação.</summary>
    internal string TextoProcessoCabecalho => TextoProcesso.Text;
    internal string TextoRequerenteCabecalho => TextoRequerente.Text;
    internal bool ProcessoEmNegrito => TextoProcesso.FontWeight.Weight > TextoRequerente.FontWeight.Weight && TextoProcesso.FontSize > TextoRequerente.FontSize;
    internal string TextoSeloGravacao => SeloGravacao.Visibility == Visibility.Visible ? TextoGravacao.Text : "";
    /// <summary>Os rótulos sempre à vista de Desfazer, Refazer, Anterior e Próximo, na ordem.</summary>
    internal IReadOnlyList<string> RotulosDoCabecalho =>
        new[] { BotaoDesfazer, BotaoRefazer, BotaoPesquisaAnterior, BotaoPesquisaProxima }
            .Select(b => b.Content is Panel { Children: var filhos } ? filhos.OfType<TextBlock>().FirstOrDefault()?.Text ?? "" : "").ToList();
    /// <summary>Os nomes acessíveis das etiquetas de situação visíveis nas linhas realizadas da lista de seções.</summary>
    internal IReadOnlyList<string> EtiquetasNaListaDeSecoes()
    {
        var nomes = new List<string>();
        void Visitar(DependencyObject o)
        {
            if (o is EtiquetaSituacao { Visibility: Visibility.Visible } e) nomes.Add(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(e));
            for (var i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(o); i++) Visitar(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(o, i));
        }
        for (var i = 0; i < R.ListaNavegacao.Items.Count; i++)
            if (R.ListaNavegacao.ContainerFromIndex(i) is DependencyObject c) Visitar(c);
        return nomes;
    }
    internal string TextoFolhaAtual => totalFolhas == 0 ? "" : $"folha {folhaAtual} de {totalFolhas}";
    internal IndicadorAvisos IndicadorLayout => R.IndicadorLayout;
    internal AreaDeAvisos AreaAvisosRelatorios => R.AreaAvisos;
    /// <summary>A altura da área das folhas (os avisos no indicador não a mudam; as faixas no topo, sim).</summary>
    internal double AlturaDasFolhas => R.AreaCentral.ActualHeight;
    internal ListView ListaDeSecoes => R.ListaNavegacao;
    internal ListView ListaDeProcessos => PaginaDeProcessos.ListaProcessos;
    internal ListView ListaDeDespachos => PaginaDeDespachos.ListaDespachos;
    internal EstadoVazio EstadoVazioRelatorios => R.TextoVazio;
    internal EstadoVazio EstadoVazioProcessos => PaginaDeProcessos.Vazio;
    internal EstadoVazio EstadoVazioDespachos => PaginaDeDespachos.Vazio;
    internal TextBlock TextoListaVaziaProcessos => PaginaDeProcessos.TextoListaVazia;
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

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern uint ExtractIconExW(string arquivo, int indice, out IntPtr grande, out IntPtr pequeno, uint quantos);

    /// <summary>Ícone do app (Recursos\app.ico, embutido no executável) também na janela e na barra de tarefas.</summary>
    private void AplicarIconeDaJanela()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null || ExtractIconExW(exe, 0, out var grande, out _, 1) == 0 || grande == IntPtr.Zero) return;
            AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(grande));
        }
        catch (Exception e)
        {
            RegistroErros.Gravar("icone", e);
        }
    }


    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(BarraTitulo);
        Perguntas.Atual = new PerguntasDaJanela(this, () => Raiz.XamlRoot);
        ajustes = CarregarAjustes();
        AplicarTemaDosAjustes();
        Raiz.Loaded += (_, _) => DimensionarJanela();
        // Fora da passada de layout (2026-11-16): mexer em larguras dentro do SizeChanged, com a escala do monitor mudando, entrava em
        // ciclo de layout e o editor fechava ao trocar de monitor.
        Raiz.SizeChanged += (_, e) => { var l = e.NewSize.Width; DispatcherQueue.TryEnqueue(() => { if (!fechada) AplicarLargura(l); }); };
        CabecalhoEsquerdo.SizeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { if (!fechada) AjustarPesquisa(Raiz.ActualWidth); }); // processo aberto/fechado muda o espaço da pesquisa
        // O resultado da pesquisa só ocupa lugar quando tem texto: vazio, deixava o espaço entre a caixa e Anterior/Próximo maior que o
        // do outro lado (2026-11-16).
        TextoPesquisa.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
        {
            TextoPesquisa.Visibility = string.IsNullOrEmpty(TextoPesquisa.Text) ? Visibility.Collapsed : Visibility.Visible;
            DispatcherQueue.TryEnqueue(() => { if (!fechada) AjustarPesquisa(Raiz.ActualWidth); });
        });
        R.ListaNavegacao.ItemsSource = itensNavegacao;
        R.ListaNavegacao.ItemClick += AoClicarItem;
        R.BotaoMenos.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.ExibirMenos);
        R.BotaoMais.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.ExibirMais);
        R.BotaoLargura.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.ExibirLargura);
        R.BotaoPagina.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.ExibirPagina);
        // "Folha n de M" (M4): digitar o número e Enter leva à folha; "Ir à folha N" do indicador de layout (A2) também.
        R.CaixaFolha.ValueChanged += async (_, e) =>
        {
            if (mudandoCaixaFolha || double.IsNaN(e.NewValue) || sessao?.Atual is null) return;
            await TentarAsync(() => IrParaFolhaAsync((int)e.NewValue));
        };
        R.IndicadorLayout.IrParaFolha += async folha => await TentarAsync(() => IrParaFolhaAsync(folha));
        IniciarEstadoVazio();
        R.Navegador.Opacity = 0; // nada aberto: só o texto de "importe ou abra"
        reajuste = DispatcherQueue.GetForCurrentThread().CreateTimer();
        reajuste.Interval = TimeSpan.FromMilliseconds(250);
        reajuste.IsRepeating = false;
        reajuste.Tick += async (_, _) => { if (modoZoom != "manual" && sessao?.Atual is not null) await TentarAsync(() => AjustarAsync(modoZoom)); };
        R.AreaCentral.SizeChanged += (_, _) => { if (modoZoom != "manual") reajuste.Start(); };
        Raiz.ActualThemeChanged += (_, _) => AplicarTemaWeb();
        AplicarIconeDaJanela();

        RegistrarManipuladores();
        RegistrarManipuladoresRevisao();
        RegistrarManipuladoresFiguras();
        IniciarRevisao();
        RegistrarAtalhos();
        IniciarGravacao();
        IniciarPesquisa();
        IniciarAjustes();
        IniciarLegislacaoAjustes();
        IniciarLegislacao();
        IniciarImoveis();
        IniciarAcompanhamento();
        IniciarPastas();
        IniciarNovoDespacho();
        IniciarAssinatura();
        IniciarAgentes();
        IniciarNovoRelatorio();
        Faixa.ComandoInvocado += async id => await ExecutarComandoAsync(id);
        Faixa.AbaMudou += nome => { ajustes.AbaPorModulo[moduloAtual] = nome; SalvarAjustes(); };
        TrilhoModulos.Montar(Faixas.Disponiveis);
        TrilhoModulos.ModuloEscolhido += async m => await TentarAsync(() => MudarModuloAsync(m));
        // Ajustes › Geral › "Ao abrir o editor": o último módulo usado (padrão) ou o escolhido.
        var inicial = ajustes.ModuloAoAbrir();
        MontarModulo(Faixas.Disponiveis.Contains(inicial) ? inicial : Faixas.Disponiveis[0]);
        AplicarPaineisDosAjustes();
        AppWindow.Changed += (_, e) => { if (e.DidSizeChange || e.DidPositionChange || e.DidPresenterChange) GuardarRetanguloDaJanela(); };

        AppWindow.Closing += AoFecharJanela;
        Closed += (_, _) =>
        {
            GuardarJanelaNosAjustes();
            fechada = true;
            sessaoNR.Encerrar();      // nenhum Codex fica sem dono (nem um que ainda ia partir)
            leituraDaLista?.Cancel(); // a leitura das pastas de processos em segundo plano para junto com a janela
            leituraDoDetalhe?.Cancel();
            // Um preparo em curso para (até ~3 s; senão a árvore do soffice é encerrada já, antes de o processo terminar) e a pasta de
            // trabalho do despacho sai.
            novo?.EncerrarAoFechar(TimeSpan.FromSeconds(3));
            PararTemporizadores();
            relogioAnalise.Stop();
            cancelarAnexo?.Cancel(); // a anexação em andamento para (o arquivo em processamento não entra)
            if (pastaCompactadosNR is { } tmpCompactados) try { Directory.Delete(tmpCompactados, recursive: true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // compactados abertos no Novo relatório
            cancelarReindexacao.Cancel();
            // O leitor e o extrator da legislação (controller oculto sobre esta janela) saem antes da janela.
            try { _ = leitorPdf?.DisposeAsync().AsTask(); } catch { }
            try { _ = legislacao.DisposeAsync().AsTask(); } catch { }
            try { documento?.Dispose(); } catch { }
            try { hostComposicao?.Dispose(); } catch { }
        };
        _ = IniciarAsync();
    }

    // ---------- inicialização ----------

    /// <summary>Tamanho inicial em DIPs (1600 x 1000), reduzido para caber na área de trabalho do monitor; com Ajustes › Geral › "Lembrar
    /// tamanho e posição" ligado (fora do roteiro), o retângulo guardado, se ainda cabe num monitor.</summary>
    void DimensionarJanela()
    {
        if (ajustes.LembrarJanela && ajustes.Janela is { Valida: true } g && Argumentos.Atual.Roteiro is null)
        {
            var retangulo = new Windows.Graphics.RectInt32(g.X, g.Y, g.Largura, g.Altura);
            if (DisplayArea.GetFromRect(retangulo, DisplayAreaFallback.None) is not null)
            {
                AppWindow.MoveAndResize(retangulo);
                if (g.Maximizada && AppWindow.Presenter is OverlappedPresenter p) p.Maximize();
                return;
            }
        }
        var escala = Raiz.XamlRoot?.RasterizationScale ?? 1.0;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Min(1600 * escala, area.Width * 0.96), (int)Math.Min(1000 * escala, area.Height * 0.96)));
    }

    Windows.Graphics.RectInt32? ultimoRetangulo;

    /// <summary>O último retângulo da janela fora de maximizada (é o que volta ao reabrir, maximizada ou não).</summary>
    void GuardarRetanguloDaJanela()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
                ultimoRetangulo = new Windows.Graphics.RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[janela] " + e.Message); }
    }

    void GuardarJanelaNosAjustes()
    {
        if (!ajustes.LembrarJanela || Argumentos.Atual.Roteiro is not null) return;
        try
        {
            var maximizada = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
            var r = ultimoRetangulo ?? new Windows.Graphics.RectInt32(AppWindow.Position.X, AppWindow.Position.Y, AppWindow.Size.Width, AppWindow.Size.Height);
            var g = new JanelaGuardada(r.X, r.Y, r.Width, r.Height, maximizada);
            if (!g.Valida) return;
            ajustes.Janela = g;
            SalvarAjustes();
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[janela] " + e.Message); }
    }

    /// <summary>Ajustes › Aparência › "Rótulos da faixa" (automático: só ícones abaixo de 1000 px) e a pesquisa entre 180 e 320 px (M8).</summary>
    void AplicarLargura(double largura)
    {
        if (double.IsNaN(largura) || largura <= 0) largura = Raiz.ActualWidth;
        Faixa.Compacta = ajustes.FaixaRotulos switch { "sempre" => false, "icones" => true, _ => largura < 1000 };
        // Cabeçalho (correções 2026-11-15): os rótulos de Desfazer, Refazer, Anterior e Próximo ficam sempre; em janelas estreitas some
        // primeiro o nome "Análise de Projetos" (o brasão fica), depois o processo e a pesquisa encolhem.
        TextoMarca.Visibility = DivisoriaMarca.Visibility = largura < 1600 ? Visibility.Collapsed : Visibility.Visible;
        BlocoProcesso.MaxWidth = largura < 1250 ? 180 : 300;
        // Auditoria de interface (2026-11-17): em 1366 px a 125% os rótulos cortavam ("Próxim", "Pes"); abaixo de 1300 ficam só os
        // ícones (a dica e o Narrador continuam com o nome).
        RotuloDesfazer.Visibility = RotuloRefazer.Visibility = RotuloAnterior.Visibility = RotuloProximo.Visibility =
            largura < 1300 ? Visibility.Collapsed : Visibility.Visible;
        // Ainda mais estreita: o selo fica só com o ícone (o texto vai na dica) e o requerente sai do cabeçalho (fica na dica do processo).
        TextoGravacao.Visibility = largura < 1300 ? Visibility.Collapsed : Visibility.Visible;
        AjustarPesquisa(largura);
        if (sessao is not null) AjustarPropriedadesALargura();
    }

    /// <summary>Caixa de pesquisa do cabeçalho (correções 2026-11-15): ocupa o espaço que sobra na barra de título, entre 200 e 640 px, e
    /// acompanha a largura da janela (refeita quando a janela ou o lado esquerdo do cabeçalho muda de tamanho).</summary>
    void AjustarPesquisa(double largura)
    {
        if (double.IsNaN(largura) || largura <= 0) largura = Raiz.ActualWidth;
        if (largura <= 0) return;
        double Largura(FrameworkElement e) => e.Visibility == Visibility.Visible ? e.ActualWidth + e.Margin.Left + e.Margin.Right : 0;
        var ocupado = Largura(CabecalhoEsquerdo) + Largura(BotaoAnalise) + Largura(GrupoHistorico) + Largura(TextoPesquisa) + Largura(GrupoOcorrencias);
        const double botoesDaJanelaEFolgas = 310; // minimizar, maximizar, fechar (área reservada pela TitleBar, ~255 px medidos), espaçamentos e arrasto
        // Só muda com diferença visível: larguras que oscilam frações de pixel entre monitores não refazem o layout sem parar.
        var nova = Math.Round(Math.Clamp(largura - ocupado - botoesDaJanelaEFolgas, 140, 640));
        if (Math.Abs(CaixaPesquisa.Width - nova) >= 1) CaixaPesquisa.Width = nova;
    }

    /// <summary>Ajustes › Relatórios e revisão › "Painéis ao abrir": com "Lembrar", o painel lateral e as Propriedades como ficaram.</summary>
    void AplicarPaineisDosAjustes()
    {
        if (!ajustes.LembrarPaineis) return;
        R.PainelNavegacao.Visibility = ajustes.PainelLateral ? Visibility.Visible : Visibility.Collapsed;
        Faixa.Ligar(ComandosApp.ExibirPainel, ajustes.PainelLateral);
        R.PainelPropriedades.Visibility = ajustes.PainelPropriedades ? Visibility.Visible : Visibility.Collapsed;
        Faixa.Ligar(ComandosApp.ExibirPropriedades, ajustes.PainelPropriedades);
    }

    /// <summary>Guarda o estado dos dois painéis (só com "Lembrar" ligado).</summary>
    void LembrarPaineis()
    {
        if (!ajustes.LembrarPaineis) return;
        var lateral = R.PainelNavegacao.Visibility == Visibility.Visible;
        var propriedades = R.PainelPropriedades.Visibility == Visibility.Visible;
        if (lateral == ajustes.PainelLateral && propriedades == ajustes.PainelPropriedades) return;
        ajustes.PainelLateral = lateral;
        ajustes.PainelPropriedades = propriedades;
        SalvarAjustes();
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
            ambienteDaTela.TrySetResult(ambiente);
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
            // As análises do Claude que seguiam quando o editor fechou (desenho §12.7). O roteiro chama no passo dele.
            if (args.Roteiro is null) _ = TentarAsync(RetomarExecucoesClaudeAsync);
            if (args.Roteiro is not null) await Roteiro.ExecutarAsync(this, args);
            else if (args.Captura is { } pastaCaptura)
            {
                // Desenvolvimento: um PNG da janela em cada módulo, e fecha.
                Directory.CreateDirectory(pastaCaptura);
                foreach (var m in Faixas.Disponiveis)
                {
                    try
                    {
                        await MudarModuloAsync(m);
                        await Task.Delay(900);
                        await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "janela-" + m.ToString().ToLowerInvariant() + ".png"));
                    }
                    catch (Exception e) { RegistroErros.Gravar("captura", e); }
                }
                // A página de um processo, em Lista e em Miniaturas (o processo cujo nome começa por PMVNI_CAPTURA_PROCESSO, ou o primeiro).
                try
                {
                    await MudarModuloAsync(Modulo.Processos);
                    await AguardarLeiturasAsync();
                    var inicio = Environment.GetEnvironmentVariable("PMVNI_CAPTURA_PROCESSO");
                    if (ProcessosMostrados.Count > 0)
                    {
                        if (!string.IsNullOrEmpty(inicio)) await EscolherProcessoNaListaAsync(inicio);
                        else await EscolherProcessoAsync(ProcessosMostrados[0].Processo);
                        await AguardarLeiturasAsync();
                        var exibicao = ExibicaoArquivos.DosAjustes(ajustes.ArquivosAgrupar, ajustes.ArquivosOrdenar, ajustes.ArquivosDecrescente);
                        PaginaDoProcesso.DefinirExibicao(exibicao, false);
                        await Task.Delay(1200);
                        await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "processo-lista.png"));
                        PaginaDoProcesso.DefinirExibicao(exibicao, true);
                        await Task.Delay(6000);
                        await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "processo-miniaturas.png"));
                    }
                }
                catch (Exception e) { RegistroErros.Gravar("captura", e); }
                // Acompanhamento de processos com uma planilha de teste (PMVNI_CAPTURA_PLANILHA = um .xlsx; uma cópia é usada) e o processo
                // escolhido no painel; depois Ajustes › Fontes de dados e Sobre e suporte.
                try
                {
                    if (Environment.GetEnvironmentVariable("PMVNI_CAPTURA_PLANILHA") is { Length: > 0 } xlsx && File.Exists(xlsx))
                    {
                        var copia = Path.Combine(pastaCaptura, "acompanhamento.xlsx");
                        File.Copy(xlsx, copia, true);
                        ajustes.PlanilhaAcompanhamento = copia;
                        ajustes.AcompanhamentoDetectar = false;
                        planilha = null;
                        await MudarModuloAsync(Modulo.Acompanhamento);
                        await EntrarNoAcompanhamentoAsync();
                        await Task.Delay(1500);
                        await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "acompanhamento.png"));
                        if (PaginaDeAcompanhamento.Lista.Items.Count > 0) PaginaDeAcompanhamento.Lista.SelectedIndex = 0;
                        await Task.Delay(900);
                        await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "acompanhamento-painel.png"));
                        await MudarModuloAsync(Modulo.Ajustes);
                        PaginaDeAjustes.MostrarCategoria(CategoriasAjustes.FontesDados);
                        await Task.Delay(900);
                        await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "ajustes-fontes.png"));
                        PaginaDeAjustes.MostrarCategoria(CategoriasAjustes.Sobre);
                        await Task.Delay(900);
                        await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "ajustes-sobre.png"));
                        // Ajustes › Inteligência artificial › Modelos de IA (2026-11-17): espera a leitura das peças e das listas e rola até a seção.
                        PaginaDeAjustes.MostrarCategoria(CategoriasAjustes.Ia);
                        await Task.Delay(1500);
                        try { await tarefaDosAgentes; } catch (Exception e) { System.Diagnostics.Debug.WriteLine("[captura] " + e.Message); }
                        for (var i = 0; i < 60 && CatalogoDeModelos.Atualizando; i++) await Task.Delay(1000);
                        PaginaDeAjustes.CaixaIaFonte.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0.0, AnimationDesired = false });
                        await Task.Delay(900);
                        await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "ajustes-modelos-ia.png"));
                        await Roteiro.CapturarXamlAsync(TrilhoModulos, Path.Combine(pastaCaptura, "trilho.png"));
                        // Erro de gravação com a planilha presa (como aberta no Excel): o aviso fixo das pendentes.
                        await MudarModuloAsync(Modulo.Acompanhamento);
                        if (planilha?.Linhas.FirstOrDefault() is { } primeira && planilha.Coluna(Pmvni.Acompanhamento.ChaveColuna.Status) is { } colStatus)
                        {
                            using (new FileStream(copia, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                await MudarCampoAsync(primeira, colStatus, colStatus.Opcoes.LastOrDefault() ?? "ARQUIVADO");
                                await Task.Delay(900);
                                await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "acompanhamento-pendentes.png"));
                            }
                            await GravarPendentesAsync(manual: true);
                            await Task.Delay(900);
                            await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "acompanhamento-pendentes-gravadas.png"));
                            // Diálogo (fica fora da árvore da janela): marca no disco para quem captura a tela inteira.
                            _ = EditarProcessoAsync(planilha.Linhas.FirstOrDefault());
                            await Task.Delay(1500);
                            File.WriteAllText(Path.Combine(pastaCaptura, "dialogo-pronto.txt"), "editar");
                            await Task.Delay(5000);
                        }
                    }
                }
                catch (Exception e) { RegistroErros.Gravar("captura", e); }
                // Um relatório de teste (PMVNI_CAPTURA_DADOS = um dados.json) com o painel de Propriedades de um item e de um ambiente.
                try
                {
                    if (Environment.GetEnvironmentVariable("PMVNI_CAPTURA_DADOS") is { Length: > 0 } dadosCaptura && File.Exists(dadosCaptura))
                    {
                        await MudarModuloAsync(Modulo.Relatorios);
                        var pacoteCaptura = Path.Combine(pastaCaptura, "exemplo.rpmvni");
                        if (File.Exists(pacoteCaptura)) File.Delete(pacoteCaptura);
                        if (await ImportarParaAsync(dadosCaptura, pacoteCaptura) && sessao is { } sc)
                        {
                            await Task.Delay(1500);
                            var p = sc.Revisao.Projeto;
                            if (itensNavegacao.FirstOrDefault(i => !i.Secao && i.Ref is not null && p.Item(i.Ref) is not null)?.Ref is { } item)
                            {
                                await SelecionarAsync(item);
                                await Task.Delay(1200);
                                await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "propriedades-item.png"));
                            }
                            if (TabelasRevisao.Ids(p).FirstOrDefault(i => i.Contains("ambiente", StringComparison.OrdinalIgnoreCase)) is { } amb)
                            {
                                await SelecionarAsync(amb);
                                await Task.Delay(1200);
                                await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "propriedades-ambiente.png"));
                            }
                        }
                    }
                }
                catch (Exception e) { RegistroErros.Gravar("captura", e); }
                // Novo relatório com um compactado anexado (PMVNI_CAPTURA_COMPACTADO = um .zip/.rar/.7z).
                try
                {
                    if (Environment.GetEnvironmentVariable("PMVNI_CAPTURA_COMPACTADO") is { Length: > 0 } compactado && File.Exists(compactado))
                    {
                        await MudarModuloAsync(Modulo.Relatorios);
                        if (await NovoRelatorioAsync())
                        {
                            await AguardarNovoRelatorioAsync();
                            await SoltarArquivosAsync([compactado]);
                            await Task.Delay(1500);
                            await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "novo-relatorio-compactado.png"));
                        }
                    }
                }
                catch (Exception e) { RegistroErros.Gravar("captura", e); }
                // Novo despacho de correção (com a pasta de processos do ajuste, se houver).
                try
                {
                    await MudarModuloAsync(Modulo.Despachos);
                    await AbrirNovoDespachoAsync(TipoDespacho.Correcao);
                    await Task.Delay(2500);
                    await Roteiro.CapturarXamlAsync(Raiz, Path.Combine(pastaCaptura, "novo-despacho.png"));
                }
                catch (Exception e) { RegistroErros.Gravar("captura", e); }
                Application.Current.Exit();
            }
            else if (args.AnexarLeis is { } pastaLeis && Directory.Exists(pastaLeis))
            {
                await MudarModuloAsync(Modulo.Legislacao);
                await AnexarLegislacaoAsync(Directory.GetFiles(pastaLeis, "*.pdf", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase).ToList());
            }
            else if (args.Dados is { } arquivo)
            {
                if (Path.GetExtension(arquivo).ToLowerInvariant() is ".rpmvni" or ".pmvrel") await AbrirPacoteAsync(arquivo);
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
        AplicarTemaLeitor();
    }

    /// <summary>O ambiente do WebView2 da tela, quando ficar pronto (o leitor da Legislação e o extrator esperam por ele, 2026-11-16: abrir o
    /// app direto na Legislação fazia o leitor nascer antes da tela, falhar e ser trocado por um WebView2 fora do lugar).</summary>
    readonly TaskCompletionSource<Microsoft.Web.WebView2.Core.CoreWebView2Environment> ambienteDaTela = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void DefinirTema(ElementTheme tema) => Raiz.RequestedTheme = tema;

    void AplicarTemaDosAjustes() => Raiz.RequestedTheme = ajustes.Tema switch
    {
        "claro" => ElementTheme.Light,
        "escuro" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    static Ajustes CarregarAjustes()
    {
        try { return Ajustes.Carregar(PastaDados.Itinerante); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return new Ajustes(); }
    }

    void SalvarAjustes()
    {
        try { ajustes.Salvar(PastaDados.Itinerante); }
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
        AtualizarPaginasDeRelatorios();
        AtualizarPaginasDeDespachos();
        PaginaDeProcessos.Visibility = m == Modulo.Processos ? Visibility.Visible : Visibility.Collapsed;
        PaginaDeLegislacao.Visibility = m == Modulo.Legislacao ? Visibility.Visible : Visibility.Collapsed;
        PaginaDeAjustes.Visibility = m == Modulo.Ajustes ? Visibility.Visible : Visibility.Collapsed;
        PaginaDeSites.Visibility = Modulos.EhSite(m) ? Visibility.Visible : Visibility.Collapsed;
        PaginaDeImoveis.Visibility = m == Modulo.Imoveis ? Visibility.Visible : Visibility.Collapsed;
        PaginaDeAcompanhamento.Visibility = m == Modulo.Acompanhamento ? Visibility.Visible : Visibility.Collapsed;
        if (Modulos.EhSite(m)) Soltar(() => PaginaDeSites.MostrarAsync(m));
        if (m == Modulo.Ajustes) { PaginaDeAjustes.TextoVersoes.Text = RelatorioDiagnostico(); AtualizarPaginaDePastas(); AtualizarAgentes(); Soltar(EntrarNaLegislacaoDosAjustesAsync); }
        var f = Faixas.Para(m);
        ajustes.AbaPorModulo.TryGetValue(m, out var aba);
        Faixa.Montar(f, Faixas.IndiceDaAba(f, aba));
        Soltar(() => AtualizarModelosAsync(forcar: false)); // o registro de modelos do kit, lido uma vez por pasta do kit (habilita o Novo despacho)
        TrilhoModulos.Marcar(m);
        TrocarPesquisaDeModulo(anterior, m);
        EntrarNoModuloDePastas(m);
        SincronizarPastas();
        AtualizarComandos();
        // Na fila (logo depois do comando que trocou de módulo, antes dos seguintes): a lista e o leitor ficam prontos na ordem dos comandos.
        if (m == Modulo.Legislacao) Soltar(() => NaFilaAsync(EntrarNaLegislacaoAsync));
        if (m == Modulo.Imoveis) Soltar(EntrarEmImoveisAsync);
        if (m == Modulo.Acompanhamento) Soltar(EntrarNoAcompanhamentoAsync);
        if (ajustes.Modulo != m) { ajustes.Modulo = m; SalvarAjustes(); }
    }

    void RegistrarManipuladores()
    {
        // Sistemas da Prefeitura (navegador embutido).
        manipuladores[ComandosApp.SiteVoltar] = () => { PaginaDeSites.Voltar(); return Task.CompletedTask; };
        manipuladores[ComandosApp.SiteAvancar] = () => { PaginaDeSites.Avancar(); return Task.CompletedTask; };
        manipuladores[ComandosApp.SiteAtualizar] = () => { PaginaDeSites.Atualizar(); return Task.CompletedTask; };
        manipuladores[ComandosApp.SiteInicio] = () => { PaginaDeSites.Inicio(); return Task.CompletedTask; };
        manipuladores[ComandosApp.SiteNavegador] = () =>
        {
            if (PaginaDeSites.EnderecoAtual is { } u && Uri.TryCreate(u, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
                _ = Windows.System.Launcher.LaunchUriAsync(uri);
            return Task.CompletedTask;
        };
        PaginaDeSites.EstadoMudou += AtualizarComandos;
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
        // Ctrl+1 … Ctrl+5: o módulo do trilho (como clicar nele).
        foreach (var m in Faixas.Disponiveis)
        {
            var modulo = m;
            manipuladores[Modulos.Comando(modulo)] = () => MudarModuloAsync(modulo);
        }
    }

    /// <summary>Executa o comando da faixa (ou do atalho) pelo id, se ele existe e está habilitado.</summary>
    /// <summary>Na fila de comandos (<see cref="NaFilaAsync"/>): espera o anterior terminar e, na sua vez, confere se está habilitado (o
    /// motivo aparece se não estiver). Ctrl+F foca a caixa de pesquisa da barra de título.</summary>
    internal Task ExecutarComandoAsync(string id) => NaFilaAsync(async () =>
    {
        if (!manipuladores.TryGetValue(id, out var acao)) return;
        // Delete, Ctrl+Z, zoom e os outros comandos do relatório não valem com a lista de processos à vista: o relatório fica escondido.
        if (moduloAtual != Modulo.Relatorios && ComandosApp.SoDoRelatorio(id)) return;
        // Com a página Novo relatório à frente, as folhas estão escondidas: os comandos delas não valem.
        if (novoRelatorioAberto && ComandosApp.SoDoRelatorio(id)) return;
        if (id.StartsWith("processos.", StringComparison.Ordinal) && moduloAtual != Modulo.Processos) return;
        // Os comandos do leitor só valem com a Legislação à vista.
        if (id.StartsWith("legislacao.", StringComparison.Ordinal) && moduloAtual != Modulo.Legislacao) return;
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
        // Correção 2026-11-16: com atalhos na raiz, o WinUI cria sozinho uma dica com o primeiro deles ("Ctrl+O"), que aparecia solta
        // sobre a janela sempre que o ponteiro parava num espaço vazio. As dicas dos botões já mostram cada atalho.
        Raiz.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;
        Raiz.KeyboardAccelerators.Clear();
        foreach (var atalho in Atalhos.Mapa.Keys.Union(Atalhos.MapaLegislacao.Keys, StringComparer.Ordinal))
        {
            foreach (var (mods, tecla) in Teclas(atalho))
            {
                var k = new KeyboardAccelerator { Modifiers = mods, Key = tecla };
                k.Invoked += async (_, a) =>
                {
                    // A mesma tecla vale o comando do módulo à vista (Ctrl+0: largura das folhas ou da lei; Ctrl+C só no leitor).
                    if (Atalhos.Comando(atalho, moduloAtual) is not { } id || TeclaDoControleComFoco(id)) { a.Handled = false; return; }
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
            // Ctrl+1 … Ctrl+5 (trilho): "1" não pode ir para Enum.TryParse (viraria o valor numérico 1, o botão esquerdo do mouse).
            case [var d] when d is >= '1' and <= '9':
                yield return (mods, VirtualKey.Number0 + (d - '0'));
                yield return (mods, VirtualKey.NumberPad0 + (d - '0'));
                break;
            case "ArrowUp": yield return (mods, VirtualKey.Up); break;
            case "ArrowDown": yield return (mods, VirtualKey.Down); break;
            case var t when Enum.TryParse<VirtualKey>(t, out var k): yield return (mods, k); break;
        }
    }

    /// <summary>Habilita cada comando pelo contexto (nada aberto, ocupado, somente leitura, exportação bloqueada).</summary>
    void AtualizarComandos()
    {
        if (fechada) return;
        AtualizarComandosDeImoveis();
        AtualizarComandosDoAcompanhamento();
        var pronto = documento is not null && !ocupado;
        var temRelatorio = pronto && sessao?.Atual is not null;
        var folhas = temRelatorio && !novoRelatorioAberto;     // a página Novo relatório esconde as folhas: os comandos delas ficam desabilitados
        Faixa.Habilitar(ComandosApp.ArquivoImportar, pronto);
        Faixa.Habilitar(ComandosApp.SiteVoltar, PaginaDeSites.Atual?.CanGoBack == true);
        Faixa.Habilitar(ComandosApp.SiteAvancar, PaginaDeSites.Atual?.CanGoForward == true);
        Faixa.Habilitar(ComandosApp.SiteAtualizar, PaginaDeSites.Atual is not null);
        Faixa.Habilitar(ComandosApp.SiteInicio, PaginaDeSites.Atual is not null);
        Faixa.Habilitar(ComandosApp.SiteNavegador, PaginaDeSites.Atual is not null);
        Faixa.Habilitar(ComandosApp.ArquivoAbrir, pronto);
        Faixa.Habilitar(ComandosApp.ArquivoSalvar, temRelatorio && sessao is { SomenteLeitura: false });
        Faixa.Habilitar(ComandosApp.ArquivoSalvarComo, temRelatorio);
        var desatualizada = sessao is { Desatualizada: true };
        Faixa.Habilitar(ComandosApp.ArquivoExportarPdf, temRelatorio && PodeExportar(sessao?.Atual) && !desatualizada);
        if (desatualizada) Faixa.DefinirDica(ComandosApp.ArquivoExportarPdf, "A exportação está bloqueada: " + SessaoEdicao.TextoDesatualizada);
        R.AvisoDesatualizado.IsOpen = desatualizada;
        Faixa.Habilitar(ComandosApp.ArquivoFechar, pronto && sessao is not null);
        Faixa.Habilitar(ComandosApp.ArquivoRecentes, pronto && haRecentes);
        AtualizarPesquisaHabilitada(folhas);
        Faixa.Habilitar(ComandosApp.ExibirMenos, folhas && zoom > PassosZoom[0] + 0.001);
        Faixa.Habilitar(ComandosApp.ExibirMais, folhas && zoom < PassosZoom[^1] - 0.001);
        Faixa.Habilitar(ComandosApp.ExibirLargura, folhas);
        Faixa.Habilitar(ComandosApp.ExibirPagina, folhas);
        Faixa.Ligar(ComandosApp.ExibirPainel, PainelVisivel);
        AtualizarComandosRevisao(folhas);
        AtualizarComandosPastas(pronto);
        AtualizarComandosNovoDespacho();
        AtualizarComandosNovoRelatorio();
        AtualizarComandosLegislacao();
        R.BotaoMenos.IsEnabled = Faixa.Habilitado(ComandosApp.ExibirMenos);
        R.BotaoMais.IsEnabled = Faixa.Habilitado(ComandosApp.ExibirMais);
        R.BotaoLargura.IsEnabled = R.BotaoPagina.IsEnabled = temRelatorio;
        // Desabilitado diz o motivo (M6); habilitado, o atalho.
        DicaDoRodape(R.BotaoMenos, ComandosApp.ExibirMenos, "Diminuir o zoom", temRelatorio ? null : "Abra um relatório para mudar o zoom.");
        DicaDoRodape(R.BotaoMais, ComandosApp.ExibirMais, "Aumentar o zoom", temRelatorio ? null : "Abra um relatório para mudar o zoom.");
        DicaDoRodape(R.BotaoLargura, ComandosApp.ExibirLargura, "Ajustar largura", temRelatorio ? null : "Abra um relatório para ajustar a largura.");
        DicaDoRodape(R.BotaoPagina, ComandosApp.ExibirPagina, "Página inteira", temRelatorio ? null : "Abra um relatório para ver a página inteira.");
    }

    /// <summary>A dica de um botão do rodapé (o mesmo comando da faixa): só refeita quando muda.</summary>
    static void DicaDoRodape(DependencyObject botao, string id, string nome, string? motivo)
    {
        var d = DicasComandos.DoComando(id, nome, Modulo.Relatorios, motivo);
        if (Dicas.De(botao) != d) Dicas.Aplicar(botao, d);
    }

    /// <summary>Leva as folhas à folha <paramref name="folha"/> (1 = primeira), da caixa "Folha n de M" ou de "Ir à folha N" do indicador.</summary>
    internal async Task IrParaFolhaAsync(int folha)
    {
        if (documento is null || sessao?.Atual is null) return;
        var d = await documento.ChamarAsync("rolarPara", new { folha = Math.Clamp(folha, 1, Math.Max(1, totalFolhas)) }, LimiteTela);
        AplicarEstadoDaWeb(d);
    }

    /// <summary>A tela vazia de Relatórios (M5): Novo relatório, Abrir e os recentes que ainda existem.</summary>
    void IniciarEstadoVazio()
    {
        R.TextoVazio.Definir(Modulo.Relatorios, "Nenhum relatório aberto",
            "Peça um relatório novo à IA, importe o dados.json do kit (Arquivo › Importar) ou abra um relatório (.rpmvni).",
            ("Novo relatório", Glifos.Adicionar, ComandosApp.RelatoriosNovo), ("Abrir…", ComandosApp.ArquivoAbrir));
        R.TextoVazio.ComandoEscolhido += async id => await ExecutarComandoAsync(id);
        R.TextoVazio.RecenteEscolhido += async caminho => await AbrirRecenteAsync(caminho);
        // Processos e Despachos (M5 completo): título, frase (o texto da lista vazia) e os botões de atualizar e de onde vêm os itens.
        PaginaDeProcessos.Vazio.Definir(Modulo.Processos, "Nenhum processo", "",
            ("Atualizar lista", Glifos.Atualizar, ComandosApp.ProcessosAtualizar), ("Abrir Ajustes", ComandosApp.ModuloAjustes));
        PaginaDeDespachos.Vazio.Definir(Modulo.Despachos, "Criar um despacho",
            "Escolha o tipo: correção ou aprovação. O relatório é opcional; anexado, a IA resume só os itens com pendência. Os despachos já emitidos ficam em Processos.",
            ("Despacho de correção", Glifos.Adicionar, ComandosApp.DespachosNovoCorrecao), ("Despacho de aprovação", ComandosApp.DespachosNovoAprovacao), Glifos.Adicionar);
        PaginaDeProcessos.Vazio.ComandoEscolhido += async id => await ExecutarComandoAsync(id);
        PaginaDeDespachos.Vazio.ComandoEscolhido += async id => await ExecutarComandoAsync(id);
    }

    // ---------- importar e abrir ----------

    async Task ImportarPeloSeletorAsync()
    {
        var json = await Perguntas.Atual.AbrirArquivoAsync(".json");
        if (json is not null) await ImportarComSeletorAsync(json);
    }

    /// <summary>Importar um dados.json escolhido (pelo seletor ou na linha de comando): pergunta onde gravar o <c>.rpmvni</c> novo, com
    /// o nome <c>Relatorio_NNNN_AAAA.rpmvni</c> do processo da capa.</summary>
    async Task ImportarComSeletorAsync(string json)
    {
        string? processo = null;
        try { processo = DadosRelatorio.DeBytes(await File.ReadAllBytesAsync(json), "").Processo; }
        catch (Exception e) when (e is JsonException or IOException) { /* a importação diz o que há de errado */ }
        var (n, a) = NomesArquivo.DoProcessoDaCapa(processo);
        var pmvrel = await Perguntas.Atual.SalvarArquivoAsync(NomesArquivo.Importacao(n, a), ".rpmvni");
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

    /// <summary>Importa o dados.json do kit para o <c>.rpmvni</c> novo <paramref name="pmvrel"/> (recusa se ele existe) e o abre.
    /// <paramref name="abrir"/> = false: só cria o <c>.rpmvni</c> (vai para os Recentes; o que está aberto não muda).</summary>
    internal async Task<bool> ImportarParaAsync(string json, string pmvrel, bool abrir = true)
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
        if (!abrir)
        {
            RegistrarRecente(pmvrel);
            return true;
        }
        return await AbrirPacoteAsync(pmvrel);
    }

    async Task AbrirPeloSeletorAsync()
    {
        var arquivo = await Perguntas.Atual.AbrirArquivoAsync(".rpmvni;.pmvrel");
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
        FecharPaginaNovoRelatorio();   // o relatório aberto aparece nas folhas (a página Novo relatório fica como estava, por trás)
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
        if (fecharDeVez || (sessao is null && novo is null && !ocupado && naFila == 0 && !sessaoNR.Execucoes.Any(e => !e.Abandonada))) return;
        args.Cancel = true;
        if (fechando) return;
        fechando = true;
        try
        {
            // Análises em andamento (desenho §12.7): o ChatGPT pergunta (Continuar ⇒ não fecha); o Claude só informa e segue sozinho.
            var tratadas = new HashSet<ExecucaoEmCurso>();
            if (!await PodeFecharComAnalisesAsync(tratadas)) return;
            if (ocupado || naFila > 0)
                MostrarMensagem(InfoBarSeverity.Informational, "Aguarde", "A janela fecha assim que a operação em andamento terminar.");
            var fechar = await NaFilaAsync(async () =>
            {
                // Abrir, importar ou exportar chamados fora da fila (linha de comando, roteiro): termina antes.
                while (ocupado && !fechada) await Task.Delay(100);
                // Um despacho em edição (passo 2 ou 3) pergunta antes de ser descartado; nada dele foi gravado na pasta do processo.
                if (!await SairDoNovoDespachoAsync()) return false;
                // Uma análise que partiu enquanto a janela esperava a vez (o Analisar copiava os anexos) pergunta agora, como as outras.
                if (!await PodeFecharComAnalisesAsync(tratadas)) return false;
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
        var destino = await Perguntas.Atual.SalvarArquivoAsync(Path.GetFileName(sessao.Caminho), ".rpmvni");
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
        textoTitulo = titulo ?? TituloPadrao;
        textoEstado = sessao is null ? "" : EstadoGravacaoTexto.Texto(sessao.Estado, sessao.SalvoEmLocal);
        // Cabeçalho (correções 2026-11-15): a marca fica sempre "Análise de Projetos"; o processo aberto vai em duas linhas (o número em
        // cima, maior e em negrito; o requerente embaixo) e o estado de gravação num selo com ícone. O Subtitle da TitleBar não é mais usado.
        TextoMarca.Text = TituloDaBarra;
        BarraTitulo.Subtitle = "";
        var (principal, secundaria) = r is null || sessao is null ? ("", "")
            : TituloJanela.Linhas(r.Dados.Processo, r.Dados.Requerente, Path.GetFileNameWithoutExtension(sessao.Caminho));
        TextoProcesso.Text = principal;
        TextoRequerente.Text = secundaria;
        TextoRequerente.Visibility = secundaria.Length == 0 || Raiz.ActualWidth is > 0 and < 1300 ? Visibility.Collapsed : Visibility.Visible;
        if (titulo is null) ToolTipService.SetToolTip(BlocoProcesso, null);
        else Dicas.Aplicar(BlocoProcesso, titulo, "O processo e o requerente do relatório aberto.");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TextoProcesso, principal.Length == 0 ? "" : "Processo aberto: " + principal);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(TextoRequerente, secundaria.Length == 0 ? "" : "Requerente: " + secundaria);
        AtualizarSeloGravacao();
        Title = titulo is null ? TituloPadrao : titulo + " — " + TituloPadrao;
        AtualizarComandos();
    }

    /// <summary>O selo do estado de gravação no cabeçalho: texto ("Salvo 14:32"), ícone e tom (verde, amarelo, vermelho ou neutro).</summary>
    void AtualizarSeloGravacao()
    {
        if (sessao is not { } s || textoEstado.Length == 0) { SeloGravacao.Visibility = Visibility.Collapsed; return; }
        var tom = EstadoGravacaoTexto.Tom(s.Estado);
        var nome = tom switch { TomSelo.Sucesso => "Sucesso", TomSelo.Atencao => "Atencao", TomSelo.Erro => "Erro", _ => "Neutro" };
        SeloGravacao.Style = (Style)Application.Current.Resources["PmvniSelo" + nome];
        IconeGravacao.Style = (Style)Application.Current.Resources["PmvniIconeSelo" + nome];
        IconeGravacao.Glyph = EstadoGravacaoTexto.Glifo(s.Estado);
        TextoGravacao.Text = textoEstado;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(SeloGravacao, "Estado de gravação: " + textoEstado);
        Dicas.Aplicar(SeloGravacao, textoEstado, "Mostra se as alterações do relatório já estão gravadas no arquivo; Ctrl+S salva.");
        SeloGravacao.Visibility = moduloAtual == Modulo.Relatorios ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Auditoria de interface (2026-11-17): o processo aberto e o selo "Salvo" são do relatório: só aparecem no módulo Relatórios (no
    /// Novo despacho de outro processo, o "Proc. 9001/2026 · Salvo" no topo confundia).</summary>
    void AtualizarCabecalhoDoModulo()
    {
        var noRelatorio = moduloAtual == Modulo.Relatorios && TextoProcesso.Text.Length > 0;
        BlocoProcesso.Visibility = DivisoriaProcesso.Visibility = noRelatorio ? Visibility.Visible : Visibility.Collapsed;
        SeloGravacao.Visibility = sessao is not null && textoEstado.Length > 0 && moduloAtual == Modulo.Relatorios ? Visibility.Visible : Visibility.Collapsed;
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
            ? $"{r.Composicao.Folhas} folhas"
            : estouros == 1 ? "1 bloco excede a página" : $"{estouros} blocos excedem a página";
        if (r.Preparado.ErrosFinais.Count > 0) R.TextoComposicao.Text += " · " + DescreverRascunho(r.Preparado.ErrosFinais);

        // Avisos de layout (auditoria A2): no indicador do rodapé (padrão) ou, com Ajustes › Aparência › "Faixas no topo", nas InfoBars
        // de antes. A lista é a mesma nos dois modos: nenhum aviso some.
        var foraDaArea = r.Aplicacao.FolhasQueTransbordamNaLargura;
        var avisosLayout = AvisosLayout.Montar(plano.Escala, plano.AvisosLargura.Count, foraDaArea,
            plano.Estouros.Select(e => (e.Folha, DescreverEstouro(e))).ToList());
        var emFaixas = ajustes.AvisosLayout == "faixas";
        R.IndicadorLayout.Mostrar(emFaixas ? Array.Empty<AvisoLayout>() : avisosLayout);

        R.AvisoLargura.IsOpen = false;
        if (emFaixas && plano.AvisosLargura.Count > 0)
        {
            R.AvisoLargura.Title = "";
            R.AvisoLargura.Message = AvisosLayout.TextoReducao(plano.Escala);
            R.AvisoLargura.IsOpen = true;
        }

        R.AvisoForaDaArea.IsOpen = false;
        if (emFaixas && foraDaArea.Count > 0)
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
            R.AvisoEstouro.IsOpen = emFaixas;
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
        totalFolhas = 0;
        R.PainelFolha.Visibility = Visibility.Collapsed;
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
            && !await Perguntas.Atual.ConfirmarAsync("Exportar com avisos?", string.Join(Environment.NewLine, avisos), "Exportar", "Cancelar", destrutiva: false))
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
        // Abrir (Ajustes › Relatórios e revisão › "Zoom ao abrir"): largura = 100 %, ou o que couber na largura se a janela for mais estreita que
        // a folha (o padrão, como sempre); página inteira; ou 100 % fixo. Recompor: o zoom que estava.
        if (primeiraVez && ajustes.ZoomInicial == "pagina") await ZoomAsync(new { ajustar = "pagina" }, "pagina");
        else if (primeiraVez && ajustes.ZoomInicial == "100") await ZoomAsync(new { zoom = 1.0 }, "manual");
        else if (primeiraVez) await ZoomAsync(new { ajustar = "largura", maximo = 1.0, transbordam = a.FolhasMarcadas });
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
            totalFolhas = t.GetInt32();
            R.TextoFolha.Text = $"de {totalFolhas}";
            mudandoCaixaFolha = true;
            try
            {
                R.CaixaFolha.Maximum = Math.Max(1, totalFolhas);
                R.CaixaFolha.Value = folhaAtual;
            }
            finally { mudandoCaixaFolha = false; }
            R.PainelFolha.Visibility = totalFolhas > 0 ? Visibility.Visible : Visibility.Collapsed;
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
        LembrarPaineis();
    }

    // ---------- ajuda ----------

    Task MostrarAtalhosAsync()
    {
        var lista = new StackPanel { Spacing = 6 };
        // Só os atalhos que fazem algo (todos, desde a pesquisa da Tarefa 11).
        foreach (var (atalho, id) in Atalhos.MapaDe(moduloAtual).Where(a => manipuladores.ContainsKey(a.Value)))
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

    /// <summary>Ajuda › Sobre: leva a Ajustes › Sobre e suporte (2026-11-17, como o WinStatusBar).</summary>
    async Task MostrarSobreAsync()
    {
        await MudarModuloAsync(Modulo.Ajustes);
        PaginaDeAjustes.MostrarCategoria(CategoriasAjustes.Sobre);
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
        R.IndicadorLayout.Mostrar(Array.Empty<AvisoLayout>());
    }

    /// <summary>O aviso do módulo à vista: Processos e Despachos têm o seu; os outros usam o da página Relatórios.</summary>
    InfoBar AvisoDoModulo => moduloAtual switch
    {
        Modulo.Processos => PaginaDeProcessos.AvisoMensagem,
        Modulo.Legislacao => PaginaDeLegislacao.AvisoMensagem,
        Modulo.Despachos => novo is not null ? PaginaDeNovoDespacho.AvisoMensagem : PaginaDeDespachos.AvisoMensagem,
        Modulo.Relatorios when novoRelatorioAberto => PaginaDeNovoRelatorio.AvisoMensagem,
        _ => R.AvisoMensagem,
    };

    internal InfoBar InfoMensagemAtual => AvisoDoModulo;

    /// <summary>Erro que ninguém tratou (auditoria 2026-11-16): o editor continua aberto, com o aviso; o detalhe já foi para o registro.
    /// Avisos repetidos em menos de 5 s não se empilham.</summary>
    internal void AvisarErroInesperado()
    {
        if (fechada || (DateTime.UtcNow - ultimoErroInesperado).TotalSeconds < 5) return;
        ultimoErroInesperado = DateTime.UtcNow;
        try { MostrarMensagem(InfoBarSeverity.Error, "Erro inesperado", "A última operação falhou e pode não ter sido concluída. O editor continua aberto; se repetir, veja Ajustes › Sobre e suporte."); }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[erro] " + e.Message); }
    }
    DateTime ultimoErroInesperado;

    void MostrarMensagem(InfoBarSeverity severidade, string titulo, string mensagem)
    {
        if (fechada) return;
        var aviso = AvisoDoModulo;
        aviso.ActionButton = null;   // o botão de uma mensagem anterior (ex.: "Importar relatório") não fica na seguinte
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
