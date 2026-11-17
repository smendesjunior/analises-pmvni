using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web;
using Pmvni.Imoveis;

namespace Pmvni.App;

/// <summary>Módulo Imóveis (2026-11-16): a base QGIS da pasta escolhida em Ajustes › Fontes de dados é lida uma vez, fora da thread da
/// janela (cadastro SQLite e camadas), e fica na memória até a pasta mudar ou o usuário pedir "Ler a base de novo". A pesquisa é em memória;
/// "Abrir relatório" monta a análise territorial e o relatório HTML, mostrado no WebView2 da página (o mesmo ambiente InPrivate do app),
/// com Salvar PDF e Imprimir. Nada é gravado na pasta QGIS.</summary>
public sealed partial class MainWindow
{
    PaginaImoveis I => PaginaDeImoveis;
    BaseQgis? baseImoveis;
    Task<BaseQgis?>? carregandoBase;
    CancellationTokenSource cancelarBase = new();
    CancellationTokenSource? cancelarPesquisa;
    Consulta? consultaImoveis;
    PaginaResultado? resultadoImoveis;
    string? relatorioAberto;     // inscrição do relatório à vista (null = pesquisa)
    RegistroCadastro? registroAberto;
    bool cotasImoveis = true;    // cotas do lote no mapa (botão Cotas do relatório; só na sessão)
    WebView2? leitorImoveis;
    CoreWebView2? CoreImoveis => leitorImoveis?.CoreWebView2;

    // Larguras das colunas da tabela de resultados (as mesmas no cabeçalho e nas linhas).
    static readonly GridLength[] ColunasImoveis =
    [
        new(150), new(2, GridUnitType.Star), new(3, GridUnitType.Star), new(1.3, GridUnitType.Star), new(90), new(150), new(136),
    ];
    static readonly string[] TitulosImoveis = ["Inscrição municipal", "Contribuinte", "Endereço", "Bairro", "Situação", "Vínculo com o mapa", ""];

    internal string? PastaQgisAtual => ajustes.PastaQgis;
    internal BaseQgis? BaseImoveis => baseImoveis;

    void IniciarImoveis()
    {
        foreach (var c in TextosConsulta.Condicoes) I.CaixaCondicao.Items.Add(new ComboBoxItem { Content = TextosConsulta.Rotulo(c), Tag = c });
        I.CaixaCondicao.SelectedIndex = 0;
        MontarLinha(I.CabecalhoTabela, TitulosImoveis.Select(t => (FrameworkElement)new TextBlock { Text = t, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }).ToArray());
        I.BotaoPesquisar.Click += async (_, _) => await PesquisarImoveisAsync(1);
        I.CaixaValor.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            await PesquisarImoveisAsync(1);
        };
        I.CaixaValor.TextChanged += (_, _) => AtualizarDicaDeteccao();
        I.CaixaParametro.SelectionChanged += (_, _) => AtualizarDicaDeteccao();
        I.BotaoAnterior.Click += async (_, _) => { if (consultaImoveis is { } c) await PesquisarImoveisAsync(c.Pagina - 1); };
        I.BotaoProxima.Click += async (_, _) => { if (consultaImoveis is { } c) await PesquisarImoveisAsync(c.Pagina + 1); };
        I.BotaoConfigurar.Click += async (_, _) => await ConfigurarPastaQgisAsync();
        I.BotaoConfigurarVazio.Click += async (_, _) => await ConfigurarPastaQgisAsync();
        I.BotaoVoltar.Click += (_, _) => VoltarAPesquisaDeImoveis();
        I.BotaoPdf.Click += async (_, _) => await SalvarPdfDoImovelAsync();
        I.BotaoImprimir.Click += (_, _) => CoreImoveis?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser);
        I.BotaoCotas.Click += async (_, _) =>
        {
            cotasImoveis = I.BotaoCotas.IsChecked == true; // vale para os próximos relatórios, até fechar o app
            if (registroAberto is { } r) await AbrirRelatorioDoImovelAsync(r);
        };

        manipuladores[ComandosApp.ImoveisConfigurar] = ConfigurarPastaQgisAsync;
        manipuladores[ComandosApp.ImoveisRecarregar] = () => { InvalidarBaseImoveis(); return EntrarEmImoveisAsync(); };
        manipuladores[ComandosApp.ImoveisVoltar] = () => { VoltarAPesquisaDeImoveis(); return Task.CompletedTask; };
        manipuladores[ComandosApp.ImoveisSalvarPdf] = SalvarPdfDoImovelAsync;
        manipuladores[ComandosApp.ImoveisImprimir] = () => { CoreImoveis?.ShowPrintUI(CoreWebView2PrintDialogKind.Browser); return Task.CompletedTask; };

        // Ajustes › Fontes de dados
        var a = PaginaDeAjustes;
        a.BotaoEscolherQgis.Click += async (_, _) => await EscolherPastaQgisAsync();
        a.BotaoVerificarQgis.Click += async (_, _) => await VerificarPastaQgisAsync();
        a.BotaoAbrirQgis.Click += (_, _) => { if (ajustes.PastaQgis is { } p) AbrirPastaDosAjustes(p); };
        AtualizarAjustesQgis();
    }

    static void MontarLinha(Grid g, FrameworkElement[] celulas)
    {
        g.ColumnDefinitions.Clear();
        g.Children.Clear();
        for (var i = 0; i < ColunasImoveis.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = ColunasImoveis[i] });
            if (i >= celulas.Length) continue;
            celulas[i].VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(celulas[i], i);
            g.Children.Add(celulas[i]);
        }
    }

    // ---------- entrar, carregar ----------

    /// <summary>Ao abrir o módulo: sem pasta, a tela pede a configuração; com pasta, a base é lida (uma vez) em segundo plano.</summary>
    async Task EntrarEmImoveisAsync()
    {
        AtualizarComandosDeImoveis();
        var pasta = ajustes.PastaQgis;
        I.TextoPasta.Text = string.IsNullOrWhiteSpace(pasta) ? "Nenhuma pasta QGIS configurada." : "Base QGIS: " + pasta;
        if (string.IsNullOrWhiteSpace(pasta))
        {
            MostrarVazioImoveis("Escolha a pasta dos arquivos QGIS em Ajustes › Fontes de dados para começar a pesquisar.", configurar: true);
            return;
        }
        if (baseImoveis is not null) { MostrarResultadosOuVazio(); return; }
        await CarregarBaseAsync(pasta);
    }

    Task<BaseQgis?> CarregarBaseAsync(string pasta)
    {
        if (carregandoBase is { IsCompleted: false } emCurso) return emCurso;
        var ct = cancelarBase.Token;
        I.Progresso.Visibility = Visibility.Visible;
        I.Aviso.IsOpen = false;
        MostrarVazioImoveis("Lendo a base QGIS…", configurar: false);
        var progresso = new Progress<string>(t => { if (!fechada) I.TextoContagem.Text = t; });
        return carregandoBase = CarregarAsync();

        async Task<BaseQgis?> CarregarAsync()
        {
            try
            {
                var b = await Task.Run(() => BaseQgis.Carregar(pasta, progresso, ct), ct);
                if (ct.IsCancellationRequested || fechada) return null;
                baseImoveis = b;
                PreencherParametros(b);
                var faltam = b.Inventario.Faltando.Select(FontesQgis.Rotulo).ToList();
                I.TextoContagem.Text = $"{b.Cadastro.Registros.Count.ToString("N0", CultureInfo.GetCultureInfo("pt-BR"))} imóveis vigentes no cadastro. Escolha o parâmetro, a condição, digite o valor e tecle Enter.";
                if (faltam.Count > 0) MostrarAvisoImoveis(InfoBarSeverity.Warning, "Camadas que não estão na pasta", string.Join(", ", faltam) + ". O relatório indica o que não foi consultado.");
                MostrarResultadosOuVazio();
                return b;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception e)
            {
                RegistroErros.Gravar("imoveis.carregar", e);
                MostrarAvisoImoveis(InfoBarSeverity.Error, "A base QGIS não abriu", e.Message + " Confira a pasta em Ajustes › Fontes de dados e use Ler a base de novo.");
                MostrarVazioImoveis("A base QGIS não pôde ser lida.", configurar: true);
                I.TextoContagem.Text = "";
                return null;
            }
            finally
            {
                if (!fechada) I.Progresso.Visibility = Visibility.Collapsed;
                AtualizarComandosDeImoveis();
            }
        }
    }

    void PreencherParametros(BaseQgis b)
    {
        var anterior = (I.CaixaParametro.SelectedItem as ComboBoxItem)?.Tag as string;
        I.CaixaParametro.Items.Clear();
        // "Detectar automaticamente" é a primeira opção e o padrão (2026-11-17); as colunas da base vêm depois, como antes.
        I.CaixaParametro.Items.Add(new ComboBoxItem { Content = DeteccaoPesquisa.RotuloAutomatico, Tag = DeteccaoPesquisa.Automatico });
        foreach (var c in b.Cadastro.Campos) I.CaixaParametro.Items.Add(new ComboBoxItem { Content = c.Rotulo, Tag = c.Id });
        var alvo = I.CaixaParametro.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (anterior ?? DeteccaoPesquisa.Automatico));
        I.CaixaParametro.SelectedItem = alvo ?? I.CaixaParametro.Items.FirstOrDefault();
        bairrosImoveis = b.Cadastro.Registros.Select(r => r.Bairro).Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        loteamentosImoveis = b.Cadastro.Registros.Select(r => r.Campo("loteamento") ?? "").Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        AtualizarDicaDeteccao();
    }

    List<string> bairrosImoveis = [], loteamentosImoveis = [];

    bool DetectandoImoveis => (I.CaixaParametro.SelectedItem as ComboBoxItem)?.Tag as string == DeteccaoPesquisa.Automatico;

    /// <summary>Com "Detectar automaticamente": mostra, enquanto o usuário digita, o campo que a pesquisa vai usar.</summary>
    void AtualizarDicaDeteccao()
    {
        var valor = I.CaixaValor.Text;
        if (!DetectandoImoveis || baseImoveis is not { } b || string.IsNullOrWhiteSpace(valor)) { I.PainelDeteccao.Visibility = Visibility.Collapsed; return; }
        I.TextoDeteccao.Text = DeteccaoPesquisa.Detectar(valor, b.Cadastro.Campos, bairrosImoveis, loteamentosImoveis).Descricao;
        I.PainelDeteccao.Visibility = Visibility.Visible;
    }

    /// <summary>A pasta mudou (ou "Ler a base de novo"): cancela leitura e pesquisa em curso e esquece a base, a consulta e os resultados.</summary>
    void InvalidarBaseImoveis()
    {
        cancelarBase.Cancel();
        cancelarBase = new CancellationTokenSource();
        cancelarPesquisa?.Cancel();
        carregandoBase = null;
        baseImoveis = null;
        consultaImoveis = null;
        resultadoImoveis = null;
        I.ListaResultados.Items.Clear();
        I.CaixaParametro.Items.Clear();
        I.TextoPaginacao.Text = "";
        I.BotaoAnterior.IsEnabled = I.BotaoProxima.IsEnabled = false;
        VoltarAPesquisaDeImoveis();
    }

    // ---------- pesquisa ----------

    async Task PesquisarImoveisAsync(int pagina)
    {
        if (baseImoveis is null)
        {
            if (string.IsNullOrWhiteSpace(ajustes.PastaQgis)) { await ConfigurarPastaQgisAsync(); return; }
            if (await CarregarBaseAsync(ajustes.PastaQgis) is null) return;
        }
        var b = baseImoveis!;
        if ((I.CaixaParametro.SelectedItem as ComboBoxItem)?.Tag is not string campo || (I.CaixaCondicao.SelectedItem as ComboBoxItem)?.Tag is not Condicao condicao) return;
        var valor = I.CaixaValor.Text;
        if (string.IsNullOrWhiteSpace(valor))
        {
            MostrarAvisoImoveis(InfoBarSeverity.Informational, "Digite um valor", "A pesquisa não lista o cadastro inteiro: digite o que procurar.");
            I.CaixaValor.Focus(FocusState.Programmatic);
            return;
        }
        I.Aviso.IsOpen = false;
        cancelarPesquisa?.Cancel();
        var cts = cancelarPesquisa = new CancellationTokenSource();
        var consulta = new Consulta(campo, condicao, valor.Trim(), Math.Max(1, pagina), 50);
        I.Progresso.Visibility = Visibility.Visible;
        try
        {
            var (r, selos) = await Task.Run(() =>
            {
                var p = Pesquisa.Aplicar(b.Cadastro.Registros, b.Cadastro.Campos, consulta, cts.Token);
                return (p, p.Itens.Select(i => TextosConsulta.Selo(i, b.VinculoRapido(i),
                    b.Cadastro.Sucessoras(i.InscricaoMunicipal).Select(s => s.InscricaoMunicipal).FirstOrDefault())).ToList());
            }, cts.Token);
            if (cts.IsCancellationRequested || fechada) return;
            consultaImoveis = consulta;
            resultadoImoveis = r;
            MostrarResultados(r, selos);
            if (r.Deteccao is { } dt) { I.TextoDeteccao.Text = dt; I.PainelDeteccao.Visibility = Visibility.Visible; }
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException e) { MostrarAvisoImoveis(InfoBarSeverity.Warning, "Pesquisa não feita", e.Message); }
        finally { if (ReferenceEquals(cancelarPesquisa, cts)) I.Progresso.Visibility = Visibility.Collapsed; }
    }

    /// <summary>Pesquisa uma inscrição (a que substituiu uma inscrição baixada): Inscrição municipal, Igual.</summary>
    async Task PesquisarInscricaoAsync(string inscricao)
    {
        if (I.CaixaParametro.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == "inscricao_municipal") is { } campo) I.CaixaParametro.SelectedItem = campo;
        if (I.CaixaCondicao.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is Condicao.Igual) is { } igual) I.CaixaCondicao.SelectedItem = igual;
        I.CaixaValor.Text = inscricao;
        await PesquisarImoveisAsync(1);
    }

    void MostrarResultados(PaginaResultado r, IReadOnlyList<SeloVinculo> selos)
    {
        var br = CultureInfo.GetCultureInfo("pt-BR");
        I.ListaResultados.Items.Clear();
        for (var i = 0; i < r.Itens.Count; i++)
        {
            var reg = r.Itens[i];
            var sv = selos[i];
            var linha = new Grid { Padding = new Thickness(12, 8, 12, 8), ColumnSpacing = 12, Style = (Style)Application.Current.Resources["PmvniGradeLinhaStyle"], BorderThickness = new Thickness(0, 0, 0, 1) };
            TextBlock T(string? s, bool forte = false) => new() { Text = s ?? "", TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = forte ? FontWeights.SemiBold : FontWeights.Normal, IsTextSelectionEnabled = true };
            var selo = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                Style = (Style)Application.Current.Resources[sv.Tom switch
                {
                    TomVinculo.Sucesso or TomVinculo.Neutro => "PmvniSeloStatusSucessoStyle",
                    TomVinculo.Erro => "PmvniSeloStatusErroStyle",
                    _ => "PmvniSeloStatusAtencaoStyle",
                }],
                Child = new TextBlock { Text = sv.Texto, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis },
            };
            // Inscrição baixada: selo neutro (cinza), não é erro de ligação com o mapa.
            if (sv.Tom == TomVinculo.Neutro) selo.Background = (Brush)Application.Current.Resources["SystemFillColorNeutralBackgroundBrush"];
            ToolTipService.SetToolTip(selo, sv.Dica);
            FrameworkElement celulaSelo = selo;
            if (sv.Sucessora is { } suc)
            {
                var ver = new HyperlinkButton { Content = "Ver a inscrição atual", Padding = new Thickness(0), FontSize = 12 };
                ToolTipService.SetToolTip(ver, $"Pesquisa a inscrição {suc}, que substituiu esta.");
                ver.Click += async (_, _) => await PesquisarInscricaoAsync(suc);
                celulaSelo = new StackPanel { Spacing = 2, Children = { selo, ver } };
            }
            var abrir = new Button { Content = "Abrir relatório", HorizontalAlignment = HorizontalAlignment.Stretch };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(abrir, "Abrir relatório do imóvel " + reg.InscricaoMunicipal);
            abrir.Click += async (_, _) => await AbrirRelatorioDoImovelAsync(reg);
            MontarLinha(linha, [T(reg.InscricaoMunicipal, true), T(reg.Contribuinte), T(reg.Endereco), T(reg.Bairro), T(reg.Situacao), celulaSelo, abrir]);
            ToolTipService.SetToolTip(linha.Children[2], reg.Endereco);
            I.ListaResultados.Items.Add(linha);
        }
        var de = r.Total == 0 ? 0 : (r.Pagina - 1) * r.Tamanho + 1;
        var ate = (r.Pagina - 1) * r.Tamanho + r.Itens.Count;
        I.TextoContagem.Text = r.Total switch
        {
            0 => "Nenhum imóvel encontrado. Confira o valor ou tente outra condição.",
            1 => "1 imóvel encontrado.",
            _ => $"{r.Total.ToString("N0", br)} imóveis encontrados.",
        };
        I.TextoPaginacao.Text = r.Total == 0 ? "" : $"{de}–{ate} de {r.Total.ToString("N0", br)} • página {r.Pagina} de {r.Paginas}";
        I.BotaoAnterior.IsEnabled = r.Pagina > 1;
        I.BotaoProxima.IsEnabled = r.Pagina < r.Paginas;
        MostrarResultadosOuVazio();
    }

    void MostrarResultadosOuVazio()
    {
        var tem = resultadoImoveis is { Total: > 0 };
        I.ListaResultados.Visibility = tem ? Visibility.Visible : Visibility.Collapsed;
        I.Vazio.Visibility = tem ? Visibility.Collapsed : Visibility.Visible;
        I.BotaoConfigurarVazio.Visibility = Visibility.Collapsed;
        I.TextoVazio.Text = resultadoImoveis is null ? "Os imóveis encontrados aparecem aqui, com o vínculo com o mapa e o botão Abrir relatório." : "Nenhum imóvel encontrado.";
    }

    void MostrarVazioImoveis(string texto, bool configurar)
    {
        I.ListaResultados.Visibility = Visibility.Collapsed;
        I.Vazio.Visibility = Visibility.Visible;
        I.TextoVazio.Text = texto;
        I.BotaoConfigurarVazio.Visibility = configurar ? Visibility.Visible : Visibility.Collapsed;
    }

    void MostrarAvisoImoveis(InfoBarSeverity s, string titulo, string mensagem)
    {
        I.Aviso.Severity = s;
        I.Aviso.Title = titulo;
        I.Aviso.Message = mensagem;
        I.Aviso.IsOpen = true;
    }

    // ---------- relatório ----------

    async Task AbrirRelatorioDoImovelAsync(RegistroCadastro r)
    {
        if (baseImoveis is not { } b) return;
        I.VistaPesquisa.Visibility = Visibility.Collapsed;
        I.VistaRelatorio.Visibility = Visibility.Visible;
        I.TituloRelatorio.Text = $"Relatório cadastral e territorial • {r.InscricaoMunicipal} • {r.Endereco}";
        I.ProgressoRelatorio.IsActive = true;
        relatorioAberto = r.InscricaoMunicipal;
        registroAberto = r;
        I.BotaoCotas.IsChecked = cotasImoveis;
        AtualizarComandosDeImoveis();
        try
        {
            var brasao = Pmvni.Composicao.ModeloOficial.LerArquivo("brasao.png");
            var cotas = cotasImoveis;
            var html = await Task.Run(() => RelatorioHtml.Gerar(b, b.Analisar(r), brasao, DateTimeOffset.Now, cotas));
            if (relatorioAberto != r.InscricaoMunicipal || fechada) return;
            var core = await GarantirLeitorImoveisAsync();
            core.NavigateToString(html);
        }
        catch (Exception e)
        {
            RegistroErros.Gravar("imoveis.relatorio", e);
            VoltarAPesquisaDeImoveis();
            MostrarAvisoImoveis(InfoBarSeverity.Error, "O relatório não foi gerado", e.Message);
        }
        finally { I.ProgressoRelatorio.IsActive = false; }
    }

    async Task<CoreWebView2> GarantirLeitorImoveisAsync()
    {
        if (CoreImoveis is { } pronto) return pronto;
        var ambiente = R.Navegador.CoreWebView2?.Environment ?? await ambienteDaTela.Task.WaitAsync(TimeSpan.FromSeconds(60));
        var web = new WebView2 { DefaultBackgroundColor = Microsoft.UI.Colors.White };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(web, "Relatório do imóvel");
        I.HostLeitor.Children.Add(web);
        I.HostLeitor.UpdateLayout();
        await web.EnsureCoreWebView2Async(ambiente, FabricaWebView2.OpcoesController(ambiente));
        leitorImoveis = web;
        var core = web.CoreWebView2!;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        // Página só local: nada de navegar para fora do relatório (links externos abrem no navegador do Windows).
        core.NavigationStarting += (s, e) =>
        {
            if (e.Uri.StartsWith("data:", StringComparison.Ordinal) || e.Uri.StartsWith("about:", StringComparison.Ordinal)) return;
            e.Cancel = true;
            if (Uri.TryCreate(e.Uri, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") _ = Windows.System.Launcher.LaunchUriAsync(u);
        };
        core.NewWindowRequested += (_, e) => e.Handled = true;
        core.NavigationCompleted += (_, e) =>
        {
            if (e.IsSuccess || e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
            RegistroErros.Gravar("imoveis.relatorio.navegacao", new InvalidOperationException(e.WebErrorStatus.ToString()));
            MostrarMensagem(InfoBarSeverity.Error, "O relatório não abriu", $"O visualizador recusou a página ({e.WebErrorStatus}).");
        };
        return core;
    }

    void VoltarAPesquisaDeImoveis()
    {
        relatorioAberto = null;
        registroAberto = null;
        I.VistaRelatorio.Visibility = Visibility.Collapsed;
        I.VistaPesquisa.Visibility = Visibility.Visible;
        AtualizarComandosDeImoveis();
    }

    async Task SalvarPdfDoImovelAsync()
    {
        if (relatorioAberto is not { } insc || CoreImoveis is not { } core) return;
        var destino = await Perguntas.Atual.SalvarArquivoAsync($"Relatorio cadastral {insc}.pdf", ".pdf");
        if (destino is null) return;
        var cfg = core.Environment.CreatePrintSettings();
        cfg.ShouldPrintHeaderAndFooter = false;
        cfg.ShouldPrintBackgrounds = true;
        cfg.MarginTop = cfg.MarginBottom = cfg.MarginLeft = cfg.MarginRight = 0;
        var ok = await core.PrintToPdfAsync(destino, cfg);
        if (ok) MostrarMensagem(InfoBarSeverity.Success, "PDF gravado", destino);
        else MostrarMensagem(InfoBarSeverity.Error, "O PDF não foi gravado", "Confira se o arquivo não está aberto em outro programa e tente de novo.");
    }

    void AtualizarComandosDeImoveis()
    {
        var comRelatorio = relatorioAberto is not null;
        Faixa.Habilitar(ComandosApp.ImoveisVoltar, comRelatorio);
        Faixa.Habilitar(ComandosApp.ImoveisSalvarPdf, comRelatorio);
        Faixa.Habilitar(ComandosApp.ImoveisImprimir, comRelatorio);
        Faixa.Habilitar(ComandosApp.ImoveisRecarregar, !string.IsNullOrWhiteSpace(ajustes.PastaQgis) && carregandoBase is not { IsCompleted: false });
        Faixa.Habilitar(ComandosApp.ImoveisConfigurar, true);
    }

    // ---------- Ajustes › Fontes de dados ----------

    async Task ConfigurarPastaQgisAsync()
    {
        await MudarModuloAsync(Modulo.Ajustes);
        PaginaDeAjustes.MostrarCategoria(CategoriasAjustes.FontesDados);
    }

    void AtualizarAjustesQgis()
    {
        var a = PaginaDeAjustes;
        a.CaixaPastaQgis.Text = ajustes.PastaQgis ?? "";
        a.BotaoVerificarQgis.IsEnabled = a.BotaoAbrirQgis.IsEnabled = !string.IsNullOrWhiteSpace(ajustes.PastaQgis);
    }

    async Task EscolherPastaQgisAsync()
    {
        var pasta = await Perguntas.Atual.EscolherPastaAsync();
        if (pasta is null) return; // cancelar mantém a escolha anterior
        var anterior = ajustes.PastaQgis;
        ajustes.PastaQgis = pasta;
        try { ajustes.Salvar(PastaDados.Itinerante); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ajustes.PastaQgis = anterior;
            MostrarResultadoQgis(InfoBarSeverity.Error, "A pasta não foi salva", e.Message);
            return;
        }
        if (!string.Equals(anterior, pasta, StringComparison.OrdinalIgnoreCase)) InvalidarBaseImoveis();
        AtualizarAjustesQgis();
        await VerificarPastaQgisAsync();
    }

    async Task VerificarPastaQgisAsync()
    {
        if (ajustes.PastaQgis is not { } pasta) return;
        var a = PaginaDeAjustes;
        a.ProgressoQgis.Visibility = Visibility.Visible;
        try
        {
            var inv = await Task.Run(() => FontesQgis.Verificar(pasta));
            var avisos = inv.Avisos.Where(x => !x.StartsWith("Camada não encontrada", StringComparison.Ordinal)).Take(6).ToList();
            var texto = FontesQgis.Resumo(inv) + (avisos.Count > 0 ? "\n" + string.Join("\n", avisos) : "");
            if (!inv.Utilizavel) MostrarResultadoQgis(InfoBarSeverity.Warning, "Pasta salva, mas sem a base cadastral", texto);
            else if (inv.Faltando.Any()) MostrarResultadoQgis(InfoBarSeverity.Warning, "Pasta acessível, com camadas faltando", texto);
            else MostrarResultadoQgis(InfoBarSeverity.Success, "Pasta acessível e completa", texto);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MostrarResultadoQgis(InfoBarSeverity.Error, "A pasta não está acessível", e.Message + " Tente de novo quando ela estiver disponível.");
        }
        finally { a.ProgressoQgis.Visibility = Visibility.Collapsed; }
    }

    void MostrarResultadoQgis(InfoBarSeverity s, string titulo, string mensagem)
    {
        var b = PaginaDeAjustes.ResultadoQgis;
        b.Severity = s;
        b.Title = titulo;
        b.Message = mensagem;
        b.IsOpen = true;
    }
}
