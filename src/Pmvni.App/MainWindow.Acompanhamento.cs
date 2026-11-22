using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Pmvni.Acompanhamento;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;
using Windows.UI;
using Rect = Windows.Foundation.Rect;
using LinhaAc = Pmvni.Acompanhamento.LinhaProcesso;
using Coluna = Pmvni.Acompanhamento.Coluna;
using Celula = Pmvni.Acompanhamento.Celula;

namespace Pmvni.App;

/// <summary>Módulo Acompanhamento de processos (2026-11-17): a planilha padrão (Ajustes › Fontes de dados) é lida fora da thread da janela e
/// mostrada com todas as colunas, as listas e as cores da formatação condicional dela; o cabeçalho tem o resumo por status (clicar filtra)
/// e o gráfico de barras do andamento. Tudo o que muda aqui (novo, edição, status, exclusão, local e autor detectados) é gravado na
/// planilha, mexendo só nas células alteradas, com uma cópia de segurança antes. Mudou no Excel: a página relê sozinha.</summary>
public sealed partial class MainWindow
{
    PaginaAcompanhamento Ac => PaginaDeAcompanhamento;
    PlanilhaLida? planilha;
    LinhaAc? linhaEscolhida;
    ChaveColuna? ordemChave = ChaveColuna.Processo;
    int ordemIndice = -1;
    bool ordemDecrescente;
    readonly HashSet<string> filtroStatus = new(StringComparer.Ordinal);
    readonly HashSet<string> filtroPrioridade = new(StringComparer.Ordinal);
    readonly HashSet<string> filtroAprovacao = new(StringComparer.Ordinal);
    readonly HashSet<string> filtroProfissional = new(StringComparer.Ordinal);
    readonly Dictionary<string, List<(ChaveColuna Campo, string Fonte)>> detectados = new(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> planilhasJaDetectadas = new(StringComparer.OrdinalIgnoreCase);
    FileSystemWatcher? vigiaPlanilha;
    DateTime gravouPlanilhaEm;
    bool gravandoPlanilha;
    DispatcherTimer? releituraPlanilha;
    List<LinhaAc> processosNaTela = [];
    bool preenchendoAba;

    static string PastaCopiasPlanilha => Path.Combine(PastaDados.Raiz, "acompanhamento", "copias");

    /// <summary>A largura da coluna: a que o usuário arrastou (lembrada nos Ajustes) ou a padrão.</summary>
    double Largura(Coluna c) => ajustes.AcompanhamentoLarguras.TryGetValue(ChaveColunaAjustes(c), out var w) ? w : LarguraPadrao(c);

    /// <summary>Como a coluna é lembrada nos Ajustes: pelo título normalizado (o mesmo em qualquer aba ou cópia da planilha).</summary>
    static string ChaveColunaAjustes(Coluna c) => Planilha.Normal(c.Titulo);

    /// <summary>Colunas de etiqueta (status, aprovação, prioridade, dias, listas com cores): conteúdo e título centralizados.</summary>
    static bool Centralizada(Coluna c) => c.Chave is ChaveColuna.Status or ChaveColuna.Aprovacao or ChaveColuna.Prioridade or ChaveColuna.DiasSemAtualizacao
        || (c.Chave == ChaveColuna.Outra && c.Tipo == TipoColuna.Lista);

    static double LarguraPadrao(Coluna c) => c.Chave switch
    {
        ChaveColuna.Numero => 56,
        ChaveColuna.DataInicial or ChaveColuna.Recebido or ChaveColuna.Atualizacao => 112,
        ChaveColuna.Prioridade => 100,
        ChaveColuna.Processo => 118,
        ChaveColuna.Requerente => 250,
        ChaveColuna.Local => 240,
        ChaveColuna.Status => 230,
        ChaveColuna.Aprovacao => 150,
        ChaveColuna.Profissional => 170,
        ChaveColuna.DiasSemAtualizacao => 104,
        ChaveColuna.Observacao => 340,
        _ => 110,
    };

    void IniciarAcompanhamento()
    {
        Ac.AddHandler(UIElement.PointerMovedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, e) =>
        {
            if (origemMovimento == null) return;
            var ponto = e.GetCurrentPoint(null);
            if (!ponto.Properties.IsLeftButtonPressed) EncerrarArraste();
            else MostrarArraste(ponto.Position.X);
        }), true);
        Ac.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => EncerrarArraste()), true);
        Ac.Unloaded += (_, _) => EncerrarArraste();
        Ac.BotaoEscolherVazio.Click += async (_, _) => await ImportarPlanilhaAsync();
        Ac.BotaoAjustesVazio.Click += async (_, _) => await ConfigurarPlanilhaAsync();
        Ac.MenuExportarCsv.Click += async (_, _) => await ExportarCsvAsync();
        Ac.MenuCopiarLista.Click += (_, _) => { AreaTransferencia.Copiar(TextoDaLista('\t')); MostrarAvisoAcompanhamento(InfoBarSeverity.Success, "Lista copiada", "Cole no Excel ou no e-mail."); };
        Ac.BotaoEditar.Click += async (_, _) => await EditarProcessoAsync(linhaEscolhida);
        Ac.BotaoFecharPainel.Click += (_, _) => { Ac.Lista.SelectedItem = null; MostrarPainel(null); };
        Ac.BotaoAbrirPasta.Click += (_, _) => AbrirPastaDoProcesso(linhaEscolhida);
        Ac.CaixaBusca.TextChanged += (_, _) => PreencherLista();
        Ac.Lista.SelectionChanged += (_, _) =>
        {
            if (Ac.Lista.SelectedItem is FrameworkElement { Tag: LinhaAc l }) MostrarPainel(l);
            AtualizarComandosDoAcompanhamento();
        };
        Ac.Lista.DoubleTapped += async (_, _) => { if (linhaEscolhida is { } l) await EditarProcessoAsync(l); };
        Ac.ActualThemeChanged += (_, _) => { if (planilha is not null) MostrarPlanilha(); };
        // O resumo lembra se ficou aberto ou recolhido (Ajustes); clicar é escolha do usuário e desliga o recolhimento automático.
        Ac.BotaoResumo.IsChecked = ajustes.AcompanhamentoResumo;
        Ac.BotaoResumo.Click += (_, _) =>
        {
            resumoRecolhidoSozinho = false;
            ajustes.AcompanhamentoResumo = Ac.BotaoResumo.IsChecked == true;
            SalvarAjustes();
            AjustarResumo();
        };
        Ac.SizeChanged += (_, _) => { AjustarResumo(); if (linhaEscolhida is not null) PosicionarPainel(); };
        Ac.BotaoLimparFiltros.Click += (_, _) => LimparFiltrosAcompanhamento();
        // Teclado (auditoria de acessibilidade): Enter edita, Delete exclui e a tecla Menu/Shift+F10 abre o menu do processo escolhido.
        Ac.Lista.KeyDown += async (_, e) =>
        {
            if (linhaEscolhida is not { } l) return;
            if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; await EditarProcessoAsync(l); }
            else if (e.Key == Windows.System.VirtualKey.Delete) { e.Handled = true; await ExcluirProcessoAsync(l); }
        };
        Ac.Lista.ContextRequested += (_, e) =>
        {
            if (linhaEscolhida is not { } l || e.Handled) return;
            var m = new MenuFlyout();
            MontarMenuDoProcesso(m, l);
            var alvo = Ac.Lista.ContainerFromItem(Ac.Lista.SelectedItem) as FrameworkElement ?? Ac.Lista;
            m.ShowAt(alvo);
            e.Handled = true;
        };
        // O Narrador lê a linha inteira no item da lista (o nome no conteúdo de dentro nem sempre era lido).
        Ac.Lista.ContainerContentChanging += (_, e) =>
        {
            if (e.Item is FrameworkElement { Tag: LinhaAc l })
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(e.ItemContainer, NomeAcessivelDaLinha(l));
        };
        Ac.RolagemTabela.ViewChanged += (_, _) => FixarColunas(soSeMudou: true);
        Ac.CaixaAba.SelectionChanged += async (_, _) =>
        {
            if (preenchendoAba || Ac.CaixaAba.SelectedItem is not string aba || aba == ajustes.PlanilhaAba && planilha is { AbaPedidaAusente: false }) return;
            if (pendentesPlanilha.Count > 0)
            {
                MostrarAvisoAcompanhamento(InfoBarSeverity.Warning, "Há alterações ainda não gravadas", "Feche a planilha no Excel para o app gravar as alterações pendentes e depois troque de aba.");
                MostrarPlanilha();
                return;
            }
            ajustes.PlanilhaAba = aba;
            SalvarAjustes();
            LimparFiltros();
            await CarregarPlanilhaAsync();
        };

        manipuladores[ComandosApp.AcompanhamentoNovo] = NovoProcessoNaPlanilhaAsync;
        manipuladores[ComandosApp.AcompanhamentoEditar] = () => EditarProcessoAsync(linhaEscolhida);
        manipuladores[ComandosApp.AcompanhamentoExcluir] = () => ExcluirProcessoAsync(linhaEscolhida);
        manipuladores[ComandosApp.AcompanhamentoRecarregar] = () => CarregarPlanilhaAsync();
        manipuladores[ComandosApp.AcompanhamentoAbrirExcel] = () => { AbrirPlanilhaNoExcel(); return Task.CompletedTask; };
        manipuladores[ComandosApp.AcompanhamentoImportar] = ImportarPlanilhaAsync;
        manipuladores[ComandosApp.AcompanhamentoDetectar] = () => DetectarLocalEAutorAsync(manual: true);
        manipuladores[ComandosApp.AcompanhamentoConfigurar] = ConfigurarPlanilhaAsync;

        // Ajustes › Fontes de dados
        var a = PaginaDeAjustes;
        a.BotaoEscolherPlanilha.Click += async (_, _) => await EscolherPlanilhaAsync(depoisAbrir: false);
        a.BotaoVerificarPlanilha.Click += async (_, _) => await VerificarPlanilhaAsync();
        a.BotaoAbrirPlanilha.Click += (_, _) => AbrirPlanilhaNoExcel();
        AtualizarAjustesPlanilha();
        CarregarPendentes();
        MostrarPendentes();
    }

    // ---------- entrar, carregar ----------

    async Task EntrarNoAcompanhamentoAsync()
    {
        AtualizarComandosDoAcompanhamento();
        if (janelaAtiva && pendentesPlanilha.Count > 0) tentarPendentes?.Start();
        if (string.IsNullOrWhiteSpace(ajustes.PlanilhaAcompanhamento))
        {
            MostrarVazioAcompanhamento("Escolha a planilha de acompanhamento de processos (o .xlsx no modelo padrão). Ela também pode ser escolhida em Ajustes › Fontes de dados.");
            return;
        }
        if (vigiaPlanilha is null || planilha is null || !string.Equals(planilha.Caminho, ajustes.PlanilhaAcompanhamento, StringComparison.OrdinalIgnoreCase)) await CarregarPlanilhaAsync();
        else MostrarPlanilha();
        if (ajustes.AcompanhamentoDetectar && planilha is not null && planilhasJaDetectadas.Add(planilha.Caminho)) await DetectarLocalEAutorAsync(manual: false);
    }

    async Task CarregarPlanilhaAsync()
    {
        var caminho = ajustes.PlanilhaAcompanhamento;
        if (string.IsNullOrWhiteSpace(caminho)) { MostrarVazioAcompanhamento("Nenhuma planilha escolhida."); return; }
        Ac.Progresso.Visibility = Visibility.Visible;
        try
        {
            var aba = ajustes.PlanilhaAba;
            planilha = await Task.Run(() => LeitorPlanilha.Ler(caminho, aba));
            Ac.Aviso.IsOpen = Ac.Aviso.Severity == InfoBarSeverity.Error ? false : Ac.Aviso.IsOpen;
            VigiarPlanilha(caminho);
            if (planilha.AbaPedidaAusente)
                MostrarAvisoAcompanhamento(InfoBarSeverity.Warning, $"A aba \"{aba}\" não está mais na planilha",
                    $"Mostrando a aba \"{planilha.Aba}\" só para leitura. Escolha a aba certa ao lado do título; até lá, nada é gravado.");
            else if (aba is null) { ajustes.PlanilhaAba = planilha.Aba; SalvarAjustes(); }
            MostrarPlanilha();
            if (pendentesPlanilha.Count > 0 && !gravandoPlanilha) _ = GravarPendentesAsync(manual: false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            planilha = null;
            MostrarVazioAcompanhamento("A planilha não pôde ser lida: " + Erro(e) + " Confira se o arquivo existe e abre no Excel.");
        }
        finally
        {
            Ac.Progresso.Visibility = Visibility.Collapsed;
            AtualizarComandosDoAcompanhamento();
        }
    }

    /// <summary>A planilha mudou no disco (Excel, OneDrive): relê 1,5 s depois do último aviso; as gravações do próprio app não contam.</summary>
    void PararVigiaPlanilha()
    {
        vigiaPlanilha?.Dispose();
        vigiaPlanilha = null;
        releituraPlanilha?.Stop();
        tentarPendentes?.Stop();
    }

    void VigiarPlanilha(string caminho)
    {
        if (fechada || !janelaAtiva || moduloAtual != Modulo.Acompanhamento) return;
        if (vigiaPlanilha is not null && string.Equals(Path.Combine(vigiaPlanilha.Path, vigiaPlanilha.Filter), caminho, StringComparison.OrdinalIgnoreCase)) return;
        vigiaPlanilha?.Dispose();
        vigiaPlanilha = null;
        try
        {
            var w = new FileSystemWatcher(Path.GetDirectoryName(caminho)!, Path.GetFileName(caminho)) { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName };
            void Mudou(object? _, FileSystemEventArgs __) => DispatcherQueue.TryEnqueue(() =>
            {
                if (vigiaPlanilha != w || gravandoPlanilha || (DateTime.UtcNow - gravouPlanilhaEm).TotalSeconds < 4) return;
                releituraPlanilha ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                releituraPlanilha.Stop();
                releituraPlanilha.Tick -= Reler;
                releituraPlanilha.Tick += Reler;
                releituraPlanilha.Start();
            });
            w.Changed += Mudou;
            w.Renamed += (s, e) => Mudou(s, e);
            w.Created += Mudou;
            w.EnableRaisingEvents = true;
            vigiaPlanilha = w;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { }
    }

    void Reler(object? s, object e)
    {
        releituraPlanilha?.Stop();
        if (!gravandoPlanilha) Soltar(CarregarPlanilhaAsync);
    }

    void MostrarVazioAcompanhamento(string texto)
    {
        Ac.BarraBusca.Visibility = Visibility.Collapsed;
        Ac.Vazio.Visibility = Visibility.Visible;
        Ac.RolagemTabela.Visibility = Visibility.Collapsed;
        Ac.Resumo.Visibility = Visibility.Collapsed;
        Ac.TextoVazio.Text = texto;
        Ac.TextoSubtitulo.Text = "Controle de tramitação";
        Ac.Rodape.Text = "";
        MostrarPainel(null);
    }

    // ---------- desenhar ----------

    bool Escuro => Ac.ActualTheme == ElementTheme.Dark;

    static Color? Hex(string? h)
    {
        if (h is null || h.Length != 7) return null;
        return Color.FromArgb(255, Convert.ToByte(h[1..3], 16), Convert.ToByte(h[3..5], 16), Convert.ToByte(h[5..7], 16));
    }

    static Color Misturar(Color a, Color b, double t) =>
        Color.FromArgb(255, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));

    /// <summary>As cores de uma etiqueta: no tema claro, as da planilha (fundo claro, texto escuro); no escuro, invertidas.</summary>
    (Brush Fundo, Brush Texto)? CoresEtiqueta(Cor? c)
    {
        if (acessibilidadeConsulta.HighContrast) return null;
        var fundo = Hex(c?.Fundo);
        var texto = Hex(c?.Texto);
        if (fundo is null && texto is null) return null;
        var f = fundo ?? Misturar(texto!.Value, Colors.White, .85);
        var t = texto ?? Misturar(fundo!.Value, Colors.Black, .7);
        return Escuro
            ? (new SolidColorBrush(Misturar(t, Colors.Black, .1)), new SolidColorBrush(f))
            : (new SolidColorBrush(f), new SolidColorBrush(t));
    }

    /// <summary>A cor da fatia e da barrinha do cartão: o meio entre o fundo e o texto da regra (lê bem nos dois temas).</summary>
    Brush CorDaFatia(Cor? c, int i)
    {
        var recursos = Application.Current.Resources;
        if (acessibilidadeConsulta.HighContrast) return (Brush)recursos["TextFillColorPrimaryBrush"];
        var fundo = Hex(c?.Fundo);
        var texto = Hex(c?.Texto);
        if (fundo is { } f && texto is { } t) return new SolidColorBrush(Misturar(f, t, .8)); // mais perto do texto: cores mais distintas
        if (texto is { } t2) return new SolidColorBrush(t2);
        return (Brush)recursos[CoresDosStatus[i % CoresDosStatus.Length]];
    }

    static readonly string[] CoresDosStatus =
    [
        "AccentFillColorDefaultBrush", "SystemFillColorCautionBrush", "SystemFillColorSuccessBrush",
        "SystemFillColorCriticalBrush", "TextFillColorSecondaryBrush",
    ];

    FrameworkElement Etiqueta(string texto, Cor? c)
    {
        if (CoresEtiqueta(c) is not { } cores) return TextoCelula(texto);
        return new Border
        {
            Background = cores.Fundo, CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 3, 10, 4), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new TextBlock { Text = texto, Style = (Style)Application.Current.Resources["PmvniPainelRotulo"],
                Foreground = cores.Texto, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis },
        };
    }

    /// <summary>A etiqueta de status no desenho da etiqueta de situação do editor (<see cref="EtiquetaSituacao"/>): fundo e borda da cor da
    /// regra da planilha (ajustados ao tema) e bolinha na cor do texto.</summary>
    FrameworkElement EtiquetaStatus(string texto, Cor? cor)
    {
        var cores = CoresEtiqueta(cor);
        var painel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        painel.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Fill = cores?.Texto ?? CorDaFatia(cor, 0) });
        var rotulo = new TextBlock
        {
            Text = Titulo(texto), Style = (Style)Application.Current.Resources["PmvniPainelRotulo"],
            TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 190,
        };
        if (cores is { } c) rotulo.Foreground = c.Texto;
        painel.Children.Add(rotulo);
        var b = new Border
        {
            Child = painel, CornerRadius = new CornerRadius(11), Padding = new Thickness(7, 2, 9, 3), BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
        };
        // A borda é um pincel próprio (mexer na opacidade do pincel do texto apagaria o texto também).
        if (cores is { } c2) { b.Background = c2.Fundo; if (c2.Texto is SolidColorBrush t) b.BorderBrush = new SolidColorBrush(t.Color) { Opacity = .35 }; }
        ToolTipService.SetToolTip(b, Titulo(texto));
        return b;
    }

    static TextBlock TextoCelula(string texto, bool apagado = false)
    {
        var t = new TextBlock { Text = texto.Length == 0 ? "—" : texto, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.Wrap, MaxLines = 2, VerticalAlignment = VerticalAlignment.Center };
        if (texto.Length == 0 || apagado) t.Opacity = .55;
        if (texto.Length > 28) ToolTipService.SetToolTip(t, texto);
        return t;
    }

    static readonly HashSet<string> Minusculas = new(StringComparer.Ordinal)
    {
        "da", "de", "do", "das", "dos", "e", "em", "na", "no", "nas", "nos", "a", "o", "as", "os", "ao", "aos", "à", "às",
        "para", "por", "com", "sem", "pelo", "pela", "pelos", "pelas",
    };

    /// <summary>"POLICIA CIENTIFICA DO ESTADO" → "Policia Cientifica do Estado" (só para a tela; a planilha fica como está).</summary>
    static string Titulo(string s)
    {
        if (s.Length == 0 || s.Any(char.IsLower)) return s;
        var pt = new CultureInfo("pt-BR");
        var palavras = s.ToLower(pt).Split(' ');
        for (var i = 0; i < palavras.Length; i++)
        {
            var w = palavras[i];
            if (w.Length == 0 || (i > 0 && Minusculas.Contains(w))) continue;
            if (w.Length <= 3 && w.Contains('.')) { palavras[i] = w.ToUpper(pt); continue; } // "a.s.s." e siglas com ponto
            palavras[i] = char.ToUpper(w[0], pt) + w[1..];
        }
        return string.Join(' ', palavras);
    }

    void MostrarPlanilha()
    {
        if (planilha is not { } p) return;
        Ac.BarraBusca.Visibility = Visibility.Visible;
        Ac.Vazio.Visibility = Visibility.Collapsed;
        Ac.RolagemTabela.Visibility = Visibility.Visible;
        CalcularColunasVisiveis(p);
        var partes = new[] { p.Subtitulo.Length > 0 ? p.Subtitulo : "Controle de tramitação", p.Aba }.Where(x => x.Length > 0);
        Ac.TextoSubtitulo.Text = string.Join(" • ", partes);
        ToolTipService.SetToolTip(Ac.TextoSubtitulo, p.Titulo.Length > 0 ? p.Titulo : null);
        preenchendoAba = true;
        Ac.CaixaAba.ItemsSource = p.Abas.ToList();
        Ac.CaixaAba.SelectedItem = p.AbaPedidaAusente ? null : p.Aba;
        Ac.CaixaAba.Visibility = p.Abas.Count > 1 || p.AbaPedidaAusente ? Visibility.Visible : Visibility.Collapsed;
        preenchendoAba = false;
        MostrarResumo();
        AjustarResumo(); // a altura da página decide: compacto ou recolhido (antes só rodava antes de a planilha carregar)
        MontarCabecalho();
        MontarFiltros();
        PreencherLista();
    }

    void MostrarResumo()
    {
        var p = planilha!;
        var compacto = ResumoCompacto;
        resumoCompactoMostrado = compacto;
        Ac.CartaoAprovacao.Visibility = p.Coluna(ChaveColuna.Aprovacao) is null ? Visibility.Collapsed : Visibility.Visible;
        Ac.BarraAndamento.Children.Clear();
        Ac.BarraAndamento.ColumnDefinitions.Clear();
        Ac.TotalResumo.Text = p.Linhas.Count == 1 ? "1 processo" : $"{p.Linhas.Count} processos";
        var status = p.Coluna(ChaveColuna.Status);
        var cont = p.Contagem(ChaveColuna.Status);
        Ac.CartoesStatus.Children.Clear();
        var i = 0;
        foreach (var (valor, n) in cont)
        {
            var cor = status?.CorDe(valor, null);
            var corFatia = CorDaFatia(cor, i++);
            if (n > 0)
            {
                var segmento = new Border { Background = corFatia };
                Grid.SetColumn(segmento, Ac.BarraAndamento.ColumnDefinitions.Count);
                Ac.BarraAndamento.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(n, GridUnitType.Star) });
                Ac.BarraAndamento.Children.Add(segmento);
                ToolTipService.SetToolTip(segmento, $"{valor}: {n}");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(segmento, $"{valor}: {n} processos");
            }
            var ativo = filtroStatus.Contains(Planilha.Normal(valor));
            // Status sem processo não ocupa cartão (a não ser que esteja no filtro); a barra representa as contagens positivas.
            if (n == 0 && !ativo) continue;
            var bolinha = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 10, Height = 10, Fill = corFatia, VerticalAlignment = VerticalAlignment.Center };
            FrameworkElement conteudo;
            if (compacto)
            {
                var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                linha.Children.Add(bolinha);
                linha.Children.Add(new TextBlock { Text = Titulo(valor), Style = (Style)Application.Current.Resources["PmvniPainelCartaoTitulo"] });
                linha.Children.Add(new TextBlock { Text = n.ToString(CultureInfo.InvariantCulture), Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] });
                conteudo = linha;
            }
            else
            {
                var pilha = new StackPanel { Spacing = 4 };
                var titulo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Left, Height = 30 };
                titulo.Children.Add(bolinha);
                titulo.Children.Add(new TextBlock { Text = Titulo(valor), Style = (Style)Application.Current.Resources["PmvniPainelCartaoTitulo"],
                    TextWrapping = TextWrapping.Wrap, MaxLines = 2, MaxWidth = 90 });
                pilha.Children.Add(titulo);
                pilha.Children.Add(new TextBlock { Text = n.ToString(CultureInfo.InvariantCulture), Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] });
                conteudo = pilha;
            }
            var b = new ToggleButton { Style = (Style)Application.Current.Resources["PmvniCartaoStatus"], Content = conteudo, IsChecked = ativo, Padding = compacto ? new Thickness(10, 4, 10, 4) : new Thickness(10, 8, 10, 8) };
            var chave = Planilha.Normal(valor);
            b.Click += (_, _) =>
            {
                if (!filtroStatus.Remove(chave)) filtroStatus.Add(chave);
                MostrarResumo();
                MontarFiltros();
                PreencherLista();
            };
            ToolTipService.SetToolTip(b, ativo ? $"Mostrar todos os status" : $"Mostrar só {valor}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, $"{valor}: {n} processos");
            Ac.CartoesStatus.Children.Add(b);
        }


        Ac.DistribuirCartoes();
        Ac.BlocoAprovacao.Children.Clear();
        var aprov = p.Coluna(ChaveColuna.Aprovacao);
        if (aprov is not null)
        {
            foreach (var (valor, n) in p.Contagem(ChaveColuna.Aprovacao))
                AdicionarAprovacao(valor, n, aprov.CorDe(valor, null));
            AdicionarAprovacao("Sem decisão", p.Linhas.Count(l => string.IsNullOrWhiteSpace(l.Aprovacao)), null);
        }
    }

    void AdicionarAprovacao(string titulo, int total, Cor? cor)
    {
        var cores = CoresEtiqueta(cor);
        var linha = new Grid { ColumnSpacing = 8, Padding = new Thickness(10, 3, 10, 3), CornerRadius = new CornerRadius(6),
            Background = cores?.Fundo ?? (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"] };
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        linha.ColumnDefinitions.Add(new ColumnDefinition());
        linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var marca = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 7, Height = 7, Fill = cores?.Texto ?? (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var nome = new TextBlock { Text = Titulo(titulo), Style = (Style)Application.Current.Resources["PmvniPainelCartaoTitulo"] };
        var numero = new TextBlock { Text = total.ToString(CultureInfo.InvariantCulture), Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"] };
        if (cores.HasValue) nome.Foreground = numero.Foreground = cores.Value.Texto;
        Grid.SetColumn(nome, 1);
        Grid.SetColumn(numero, 2);
        linha.Children.Add(marca);
        linha.Children.Add(nome);
        linha.Children.Add(numero);
        Ac.BlocoAprovacao.Children.Add(linha);
    }

    /// <summary>As colunas da tabela (auditoria de interface 2026-11-17): Nº, Processo e Status primeiro (o Status não some com o painel
    /// aberto), depois as outras na ordem da planilha; colunas vazias em todos os processos ficam escondidas (continuam no formulário).</summary>
    IReadOnlyList<Coluna> colunasVisiveis = [];
    IReadOnlyList<Coluna> ColunasVisiveis => colunasVisiveis;
    IReadOnlyList<string> colunasEscondidas = [];

    void CalcularColunasVisiveis(PlanilhaLida p)
    {
        static int Peso(Coluna c) => c.Chave switch
        {
            ChaveColuna.Numero => 0, ChaveColuna.Processo => 1, ChaveColuna.Status => 2, ChaveColuna.Requerente => 3, _ => 10,
        };
        var vazias = p.Linhas.Count == 0 ? [] : p.Colunas.Where(c => c.Chave is not (ChaveColuna.Processo or ChaveColuna.Status or ChaveColuna.Requerente)
            && p.Linhas.All(l => p.TextoDe(l, c).Length == 0)).ToList();
        // Colunas que o usuário tirou pelo botão "Colunas" (o Processo fica sempre: é por ele que a linha é achada).
        var ocultas = p.Colunas.Where(c => c.Chave != ChaveColuna.Processo && ajustes.AcompanhamentoColunasOcultas.Contains(ChaveColunaAjustes(c))).ToList();
        int Ordem(Coluna c) => ajustes.AcompanhamentoColunasOrdem.IndexOf(ChaveColunaAjustes(c)) is var i && i >= 0 ? i : int.MaxValue;
        colunasVisiveis = p.Colunas.Where(c => !vazias.Contains(c) || ajustes.AcompanhamentoColunasExibidas.Contains(ChaveColunaAjustes(c)))
            .Except(ocultas).OrderBy(Ordem).ThenBy(Peso).ThenBy(c => c.Indice).ToList();
        colunasEscondidas = vazias.Except(colunasVisiveis).Select(c => c.Titulo).ToList();
        MontarMenuColunas(p, vazias);
        ColunasFixas = colunasVisiveis.TakeWhile(c => c.Chave is ChaveColuna.Numero or ChaveColuna.Processo).Count();
    }

    /// <summary>Altura pequena (notebook 1366 × 768): o resumo vira uma faixa de uma linha, sem a pizza, para a tabela nunca sumir.</summary>
    bool ResumoCompacto => Ac.ActualHeight is > 0 and < 720;
    bool? resumoCompactoMostrado;

    /// <summary>O app recolheu o resumo sozinho (página baixa demais); volta sozinho quando a página crescer, se o usuário não mexeu.</summary>
    bool resumoRecolhidoSozinho;

    void AjustarResumo()
    {
        if (planilha is null) return;
        // Página muito baixa (notebook com avisos abertos): sem o resumo, para a tabela ter pelo menos algumas linhas.
        var baixa = Ac.ActualHeight is > 0 and < 560;
        if (baixa && Ac.BotaoResumo.IsChecked == true && ajustes.AcompanhamentoResumo) { Ac.BotaoResumo.IsChecked = false; resumoRecolhidoSozinho = true; }
        else if (!baixa && resumoRecolhidoSozinho) { Ac.BotaoResumo.IsChecked = true; resumoRecolhidoSozinho = false; }
        Ac.Resumo.Visibility = Ac.BotaoResumo.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        if (resumoCompactoMostrado != ResumoCompacto) MostrarResumo();
    }

    void LimparFiltros() { filtroStatus.Clear(); filtroPrioridade.Clear(); filtroAprovacao.Clear(); filtroProfissional.Clear(); }

    void LimparFiltrosAcompanhamento()
    {
        LimparFiltros();
        Ac.CaixaBusca.Text = "";
        if (moduloAtual == Modulo.Acompanhamento) CaixaPesquisa.Text = "";
        MostrarResumo();
        MontarFiltros();
        PreencherLista();
    }

    /// <summary>O que o Narrador lê na linha: "Processo 123/2026, Fulano de Tal, Em Análise, 12 dias sem atualização".</summary>
    string NomeAcessivelDaLinha(LinhaAc l)
    {
        var partes = new List<string> { "Processo " + l.Processo };
        if (l.Texto(ChaveColuna.Requerente) is { Length: > 0 } r) partes.Add(Titulo(r));
        if (l.Status.Length > 0) partes.Add(Titulo(l.Status));
        if (l.Dias(DateTime.Today) is { } d) partes.Add(d == 1 ? "1 dia sem atualização" : $"{d} dias sem atualização");
        return string.Join(", ", partes);
    }

    // ---------- colunas fixas (Nº e Processo) ----------

    /// <summary>As células das colunas fixas (cabeçalho e linhas): ao rolar a tabela para o lado, elas andam junto com a rolagem e ficam
    /// sempre à vista, com um fundo opaco por baixo (como "congelar painéis" do Excel).</summary>
    readonly List<Border> celulasFixas = [];

    /// <summary>Quantas colunas da frente são fixas (Nº e Processo); calculado junto com as colunas visíveis, não a cada célula.</summary>
    int ColunasFixas;
    double ultimoDeslocamento = -1;

    /// <summary>Célula fixa em três camadas (fundo da página › cartão › faixa do título), que repetem as da tabela: rolada, a parte fixa
    /// tem a mesma cor do resto. <paramref name="titulo"/> = célula do cabeçalho (ganha a terceira camada).</summary>
    Border CelulaFixa(FrameworkElement conteudo, int coluna, Thickness padding, bool titulo = false)
    {
        // Cobre o espaço entre colunas (10) com folga de 1 px de cada lado: em 125/150 % de escala sobrava uma fresta por onde aparecia
        // o texto das colunas que passam por baixo.
        // A primeira coluna cobre também a margem da linha dentro da lista (uns 4 px): por ali apareciam pedaços das etiquetas de status.
        var esquerda = coluna == 0 ? padding.Left + (titulo ? 0 : 6) : 6;
        var interno = new Border { Child = conteudo, Padding = new Thickness(esquerda, padding.Top, 6, padding.Bottom), Tag = titulo };
        var b = new Border
        {
            Child = new Border { Child = interno },
            Margin = new Thickness(-esquerda, -padding.Top, -6, -padding.Bottom),
            RenderTransform = new TranslateTransform(),
            Tag = coluna,
        };
        Canvas.SetZIndex(b, 2);
        celulasFixas.Add(b);
        return b;
    }

    /// <summary><paramref name="soSeMudou"/>: a rolagem chama a cada quadro (também na vertical); só refaz quando o deslocamento lateral muda.</summary>
    void FixarColunas(bool soSeMudou = false)
    {
        var x = Ac.RolagemTabela.HorizontalOffset;
        if (soSeMudou && x == ultimoDeslocamento) return;
        ultimoDeslocamento = x;
        var rolada = x > 0.5;
        var r = Ac.RaizPagina.Resources;
        Style? Estilo(string chave) => rolada ? (Style)r[chave] : null;
        void Por(Border b, Style? s)
        {
            if (ReferenceEquals(b.Style, s)) return;
            b.Style = s;
            if (s is null) { b.ClearValue(Border.BackgroundProperty); b.ClearValue(Border.BorderBrushProperty); b.ClearValue(Border.BorderThicknessProperty); }
        }
        // As do cabeçalho vêm direto da grade do título: na lista elas se perdiam ao redesenhar as linhas e o título fixo ficava
        // parado enquanto as linhas andavam (2026-11-17).
        var cabecalho = Ac.CabecalhoTabela.Children.OfType<Border>().Where(b => b.Tag is int);
        foreach (var b in celulasFixas.Where(c => !ReferenceEquals(c.Parent, Ac.CabecalhoTabela)).Concat(cabecalho))
        {
            if (b.RenderTransform is TranslateTransform t) t.X = x;
            var ultima = b.Tag is int col && col == ColunasFixas - 1;
            Por(b, Estilo(ultima ? "PmvniCelulaFixaBordaStyle" : "PmvniCelulaFixaStyle"));
            if (b.Child is Border cartao)
            {
                Por(cartao, Estilo("PmvniCelulaFixaCartaoStyle"));
                if (cartao.Child is Border { Tag: true } faixa) Por(faixa, Estilo("PmvniCelulaFixaTituloStyle"));
            }
        }
    }

    void DefinirColunas(Grid g)
    {
        g.ColumnDefinitions.Clear();
        foreach (var c in ColunasVisiveis) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Largura(c)) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        g.ColumnSpacing = 10;
    }

    double LarguraDaTabela => ColunasVisiveis.Sum(Largura) + 48 + 10 * ColunasVisiveis.Count + 24 + 8;

    void MontarCabecalho()
    {
        Ac.MenuOrdenacao.Items.Clear();
        Ac.Ordenacao.Content = "Ordenar";
        var g = Ac.CabecalhoTabela;
        celulasFixas.RemoveAll(b => ReferenceEquals(b.Parent, g));
        g.Children.Clear();
        DefinirColunas(g);
        Ac.Tabela.Width = LarguraDaTabela;
        var i = 0;
        foreach (var c in ColunasVisiveis)
        {
            var ativa = c.Chave != ChaveColuna.Outra ? ordemChave == c.Chave : ordemIndice == c.Indice;
            var painel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            painel.Children.Add(new TextBlock { Text = c.Chave == ChaveColuna.Aprovacao ? "Aprovação" : c.Titulo,
                Style = (Style)Application.Current.Resources["PmvniPainelRotulo"], TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis });
            if (ativa) painel.Children.Add(new FontIcon { Glyph = ordemDecrescente ? "\uE74B" : "\uE74A",
                FontSize = 16, Style = (Style)Application.Current.Resources["PmvniIconeSecundarioStyle"] });
            var b = new Button { Content = painel, Style = (Style)Application.Current.Resources["PmvniBarraButtonStyle"],
                Padding = new Thickness(0, 10, 0, 10), HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = Centralizada(c) ? HorizontalAlignment.Center : HorizontalAlignment.Left };
            // O sentido da ordem não fica só no ícone: o nome e a dica dizem (Narrador).
            var nomeCol = c.Chave == ChaveColuna.Aprovacao ? "Aprovação" : c.Titulo;
            var dicaOrdem = ativa ? $"{nomeCol}, em ordem {(ordemDecrescente ? "decrescente" : "crescente")}. Clique para a ordem {(ordemDecrescente ? "crescente" : "decrescente")}." : $"Ordenar por {nomeCol}";
            ToolTipService.SetToolTip(b, dicaOrdem);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, dicaOrdem);
            var col = c;
            void OrdenarColuna()
            {
                var mesma = col.Chave != ChaveColuna.Outra ? ordemChave == col.Chave : ordemIndice == col.Indice;
                ordemDecrescente = mesma && !ordemDecrescente;
                ordemChave = col.Chave != ChaveColuna.Outra ? col.Chave : null;
                ordemIndice = col.Chave == ChaveColuna.Outra ? col.Indice : -1;
                MontarCabecalho();
                PreencherLista();
            }
            var opcao = new ToggleMenuFlyoutItem { Text = nomeCol, IsChecked = ativa };
            opcao.Click += (_, _) => OrdenarColuna();
            Ac.MenuOrdenacao.Items.Add(opcao);
            if (ativa) Ac.Ordenacao.Content = $"Ordenar: {Titulo(nomeCol)}";
            b.Click += (_, _) => OrdenarColuna();
            var superficie = new CabecalhoMovel();
            superficie.CliqueDoPonteiro += OrdenarColuna;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(superficie, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            // Alça de largura na borda direita do título (pedido do usuário, 2026-11-17: colunas redimensionáveis, como no Excel).
            ConfigurarMovimento(superficie, b, col, i);
            var indice = i;
            var alca = new AlcaColuna(() => g.ColumnDefinitions[indice].Width.Value) { Margin = new Thickness(0, 0, -11, 0) };
            ToolTipService.SetToolTip(alca, "Arraste para mudar a largura; dois cliques voltam ao padrão");
            alca.Mudando += w => AplicarLargura(indice, w);
            alca.Mudou += w => { ajustes.AcompanhamentoLarguras[ChaveColunaAjustes(col)] = w; SalvarAjustes(); AplicarLargura(indice, w); };
            alca.Restaurar += () => { ajustes.AcompanhamentoLarguras.Remove(ChaveColunaAjustes(col)); SalvarAjustes(); AplicarLargura(indice, LarguraPadrao(col)); };
            var titulo = new Grid();
            titulo.Children.Add(b);
            titulo.Children.Add(superficie);
            titulo.Children.Add(alca);
            Canvas.SetZIndex(alca, 1);
            FrameworkElement celula = i < ColunasFixas ? CelulaFixa(titulo, i, g.Padding, titulo: true) : titulo;
            Grid.SetColumn(celula, i++);
            g.Children.Add(celula);
        }
        FixarColunas();
    }

    (string Chave, int Indice, double X)? origemMovimento;
    readonly List<(FrameworkElement Celula, int Coluna, int Camada)> celulasEmMovimento = [];
    readonly List<(int Coluna, Rect Limites, int Camada)> destinosMovimento = [];

    void EncerrarArraste()
    {
        foreach (var (celula, _, camada) in celulasEmMovimento)
        {
            celula.Translation = default;
            Canvas.SetZIndex(celula, camada);
        }
        celulasEmMovimento.Clear();
        destinosMovimento.Clear();
        origemMovimento = null;
    }

    void MostrarArraste(double x)
    {
        if (origemMovimento is not { } origem || (celulasEmMovimento.Count == 0 && Math.Abs(x - origem.X) < 5)) return;
        if (celulasEmMovimento.Count == 0)
        {
            foreach (var linha in Ac.Lista.Items.OfType<Grid>().Prepend(Ac.CabecalhoTabela))
                foreach (var celula in linha.Children.OfType<FrameworkElement>())
                {
                    var coluna = Grid.GetColumn(celula);
                    if (coluna >= ColunasVisiveis.Count) continue;
                    var camada = Canvas.GetZIndex(celula);
                    celulasEmMovimento.Add((celula, coluna, camada));
                    if (coluna == origem.Indice) Canvas.SetZIndex(celula, 4);
                }
        }
        var destino = destinosMovimento.FirstOrDefault(d => x >= d.Limites.Left && x <= d.Limites.Right);
        var alvo = destino.Limites.Width > 0 ? destino.Coluna : origem.Indice;
        float largura = (float)(Ac.CabecalhoTabela.ColumnDefinitions[origem.Indice].ActualWidth + Ac.CabecalhoTabela.ColumnSpacing);
        foreach (var (celula, coluna, _) in celulasEmMovimento)
        {
            float deslocamento = coluna == origem.Indice ? (float)(x - origem.X)
                : coluna > origem.Indice && coluna <= alvo ? -largura
                : coluna < origem.Indice && coluna >= alvo ? largura : 0;
            celula.Translation = new System.Numerics.Vector3(deslocamento, 0, 0);
        }
    }

    void ConfigurarMovimento(CabecalhoMovel superficie, Button botao, Coluna coluna, int indice)
    {
        superficie.InicioDoArraste += ponto =>
        {
            EncerrarArraste();
            origemMovimento = (ChaveColunaAjustes(coluna), indice, ponto.X);
            foreach (var celula in Ac.CabecalhoTabela.Children.OfType<FrameworkElement>().OrderByDescending(c => Canvas.GetZIndex(c)))
            {
                var limites = celula.TransformToVisual(null).TransformBounds(new Rect(0, 0, celula.ActualWidth, celula.ActualHeight));
                destinosMovimento.Add((Grid.GetColumn(celula), limites, Canvas.GetZIndex(celula)));
            }
        };
        superficie.SoltouColuna += ponto =>
        {
            var origem = origemMovimento;
            var alvo = destinosMovimento.FirstOrDefault(d => d.Limites.Contains(ponto));
            EncerrarArraste();
            if (origem is { } inicio && Math.Abs(inicio.X - ponto.X) >= 5 && alvo.Limites.Width > 0)
                MoverColuna(inicio.Chave, alvo.Coluna);
        };
        var menu = new MenuFlyout();
        foreach (var (texto, alvo) in new[] { ("Mover para a esquerda", indice - 1), ("Mover para a direita", indice + 1) })
        {
            var item = new MenuFlyoutItem { Text = texto, IsEnabled = alvo >= 0 && alvo < ColunasVisiveis.Count };
            if (!item.IsEnabled) Dicas.Aplicar(item, texto, "", motivo: "A coluna já está neste extremo da tabela.");
            item.Click += (_, _) => MoverColuna(ChaveColunaAjustes(coluna), alvo);
            menu.Items.Add(item);
        }
        botao.ContextFlyout = menu;
        superficie.ContextFlyout = menu;
        ToolTipService.SetToolTip(superficie, "Clique para ordenar ou arraste o título para mover a coluna.");
    }

    void MoverColuna(string chave, int destino)
    {
        var ordem = ColunasVisiveis.Select(ChaveColunaAjustes).ToList();
        if (destino < 0 || destino >= ordem.Count || ordem[destino] == chave || !ordem.Remove(chave)) return;
        ordem.Insert(destino, chave);
        var salvas = ajustes.AcompanhamentoColunasOrdem;
        var remanescentes = salvas.Except(ordem).ToList();
        salvas.Clear();
        salvas.AddRange(ordem);
        salvas.AddRange(remanescentes);
        SalvarAjustes();
        RefazerTabela();
    }

    void AplicarLargura(int indice, double largura)
    {
        if (indice >= Ac.CabecalhoTabela.ColumnDefinitions.Count) return;
        var antes = Ac.CabecalhoTabela.ColumnDefinitions[indice].Width.Value;
        if (Math.Abs(antes - largura) < 0.5) return;
        var nova = new GridLength(largura);
        Ac.CabecalhoTabela.ColumnDefinitions[indice].Width = nova;
        var total = Ac.Tabela.Width + largura - antes;
        Ac.Tabela.Width = total;
        foreach (var item in Ac.Lista.Items)
            if (item is Grid linha && indice < linha.ColumnDefinitions.Count)
            {
                linha.ColumnDefinitions[indice].Width = nova;
                linha.Width = total - 8;
            }
    }

    // ---------- escolher as colunas (botão "Colunas") ----------

    /// <summary>Uma marca por coluna da planilha: desmarcar tira a coluna da tabela (lembrado nos Ajustes). Processo não sai; colunas
    /// vazias em todos os processos ficam desativadas ("sem dados"). No fim, "Mostrar todas" e "Larguras padrão".</summary>
    void MontarMenuColunas(PlanilhaLida p, IReadOnlyCollection<Coluna> vazias)
    {
        var menu = Ac.MenuColunas;
        menu.Items.Clear();
        foreach (var c in p.Colunas)
        {
            var nome = c.Chave == ChaveColuna.Aprovacao ? "Aprovação" : c.Titulo;
            var vazia = vazias.Contains(c);
            var item = new ToggleMenuFlyoutItem
            {
                Text = vazia ? nome + " (sem dados)" : nome,
                IsChecked = ColunasVisiveis.Contains(c),
                IsEnabled = c.Chave != ChaveColuna.Processo,
            };
            if (!item.IsEnabled) Dicas.Aplicar(item, nome, "", motivo: "A coluna Processo precisa permanecer visível.");
            var col = c;
            item.Click += (_, _) =>
            {
                var chave = ChaveColunaAjustes(col);
                ajustes.AcompanhamentoColunasOcultas.Remove(chave);
                ajustes.AcompanhamentoColunasExibidas.Remove(chave);
                if (item.IsChecked) ajustes.AcompanhamentoColunasExibidas.Add(chave);
                if (!item.IsChecked) ajustes.AcompanhamentoColunasOcultas.Add(chave);
                SalvarAjustes();
                RefazerTabela();
            };
            menu.Items.Add(item);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        var todas = new MenuFlyoutItem { Text = "Mostrar todas", IsEnabled = ColunasVisiveis.Count < p.Colunas.Count };
        if (!todas.IsEnabled) Dicas.Aplicar(todas, todas.Text, "", motivo: "Todas as colunas já estão visíveis.");
        todas.Click += (_, _) => { ajustes.AcompanhamentoColunasExibidas.Clear(); ajustes.AcompanhamentoColunasExibidas.AddRange(p.Colunas.Select(ChaveColunaAjustes)); ajustes.AcompanhamentoColunasOcultas.Clear(); SalvarAjustes(); RefazerTabela(); };
        menu.Items.Add(todas);
        var padrao = new MenuFlyoutItem { Text = "Restaurar larguras", IsEnabled = ajustes.AcompanhamentoLarguras.Count > 0 };
        if (!padrao.IsEnabled) Dicas.Aplicar(padrao, padrao.Text, "", motivo: "As colunas já estão com as larguras padrão.");
        padrao.Click += (_, _) => { ajustes.AcompanhamentoLarguras.Clear(); SalvarAjustes(); RefazerTabela(); };
        menu.Items.Add(padrao);
        var restaurar = new MenuFlyoutItem { Text = "Restaurar ordem", IsEnabled = ajustes.AcompanhamentoColunasOrdem.Count > 0 };
        if (!restaurar.IsEnabled) Dicas.Aplicar(restaurar, restaurar.Text, "", motivo: "As colunas já estão na ordem padrão.");
        restaurar.Click += (_, _) => { ajustes.AcompanhamentoColunasOrdem.Clear(); SalvarAjustes(); RefazerTabela(); };
        menu.Items.Add(restaurar);
        var ocultas = p.Colunas.Count(c => !vazias.Contains(c) && c.Chave != ChaveColuna.Processo && ajustes.AcompanhamentoColunasOcultas.Contains(ChaveColunaAjustes(c)));
        Ac.TextoColunas.Text = ocultas == 0 ? "Colunas" : $"Colunas ({ocultas} oculta{(ocultas == 1 ? "" : "s")})";
    }

    void RefazerTabela()
    {
        if (planilha is not { } p) return;
        CalcularColunasVisiveis(p);
        MontarCabecalho();
        PreencherLista();
    }

    bool PassaNoFiltro(LinhaAc l)
    {
        if (filtroStatus.Count > 0 && !filtroStatus.Contains(Planilha.Normal(l.Status))) return false;
        if (filtroPrioridade.Count > 0 && !filtroPrioridade.Contains(Planilha.Normal(l.Texto(ChaveColuna.Prioridade)))) return false;
        if (filtroAprovacao.Count > 0 && !filtroAprovacao.Contains(Planilha.Normal(l.Aprovacao))) return false;
        if (filtroProfissional.Count > 0 && !filtroProfissional.Contains(Planilha.Normal(l.Texto(ChaveColuna.Profissional)))) return false;
        var busca = Planilha.Normal(Ac.CaixaBusca.Text);
        if (busca.Length == 0) return true;
        return planilha!.Colunas.Any(c => Planilha.Normal(planilha.TextoDe(l, c)).Contains(busca, StringComparison.Ordinal))
            || Planilha.Normal(l.ProcessoNormalizado).Contains(busca.Replace(" ", ""), StringComparison.Ordinal);
    }

    IComparable Chave(LinhaAc l)
    {
        var c = ordemChave is { } k ? planilha!.Coluna(k) : planilha!.Colunas.FirstOrDefault(x => x.Indice == ordemIndice);
        if (c is null) return l.Linha;
        if (c.Chave == ChaveColuna.Processo)
        {
            var m = Regex.Match(l.ProcessoNormalizado, @"^(\d+)/(\d{4})");
            return m.Success ? long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 1_000_000 + long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : long.MaxValue;
        }
        if (c.Chave == ChaveColuna.DiasSemAtualizacao) return l.Dias(DateTime.Today) ?? int.MinValue;
        if (c.Tipo == TipoColuna.Data) return Planilha.ComoData(l.Celula(c.Indice)) ?? DateTime.MinValue;
        if (c.Tipo == TipoColuna.Numero) return l.Celula(c.Indice).Numero ?? double.MinValue;
        if (c.Tipo == TipoColuna.Lista && c.Opcoes.Count > 0)
        {
            var i = c.Opcoes.Select(Planilha.Normal).ToList().IndexOf(Planilha.Normal(l.Celula(c.Indice).Texto));
            return i < 0 ? int.MaxValue : i;
        }
        return Planilha.Normal(planilha.TextoDe(l, c));
    }

    void PreencherLista()
    {
        if (planilha is not { } p) return;
        var filtrados = p.Linhas.Where(PassaNoFiltro);
        processosNaTela = (ordemDecrescente ? filtrados.OrderByDescending(Chave) : filtrados.OrderBy(Chave)).ThenBy(l => l.Linha).ToList();
        var escolhido = linhaEscolhida?.ProcessoNormalizado;
        celulasFixas.RemoveAll(b => !ReferenceEquals(b.Parent, Ac.CabecalhoTabela));
        Ac.Lista.Items.Clear();
        FrameworkElement? reescolher = null;
        foreach (var l in processosNaTela)
        {
            var linha = LinhaDaTabela(l);
            Ac.Lista.Items.Add(linha);
            if (escolhido is not null && string.Equals(l.ProcessoNormalizado, escolhido, StringComparison.OrdinalIgnoreCase)) reescolher = linha;
        }
        if (reescolher is not null) Ac.Lista.SelectedItem = reescolher;
        else MostrarPainel(null);
        var filtros = filtroStatus.Count + filtroPrioridade.Count + filtroAprovacao.Count + filtroProfissional.Count;
        Ac.StatusRapido.Content = filtroStatus.Count switch
        {
            0 => "Todos os status",
            1 => Titulo(p.Opcoes(ChaveColuna.Status).FirstOrDefault(v => filtroStatus.Contains(Planilha.Normal(v))) ?? filtroStatus.First()),
            _ => $"{filtroStatus.Count} status",
        };
        foreach (var item in Ac.MenuStatusRapido.Items.OfType<ToggleMenuFlyoutItem>())
            item.IsChecked = ReferenceEquals(item, Ac.MenuStatusRapido.Items[0]) ? filtroStatus.Count == 0 : filtroStatus.Contains(Planilha.Normal(item.Text));
        Ac.TextoFiltrar.Text = filtros == 0 ? "Filtrar" : $"Filtrar ({filtros})";
        var ultima = p.Linhas.Select(l => l.Atualizacao).Where(d => d is not null).DefaultIfEmpty(null).Max();
        static string Processos(int n) => n == 1 ? "1 processo" : $"{n} processos";
        var mostrando = processosNaTela.Count == p.Linhas.Count ? Processos(p.Linhas.Count) : $"{processosNaTela.Count} de {Processos(p.Linhas.Count)}";
        Ac.Rodape.Text = mostrando + (ultima is { } u ? $"  •  Última atualização em {u:dd/MM/yyyy}" : "") + $"  •  Planilha lida às {p.LidoEm:HH:mm} (aba {p.Aba})"
            + (colunasEscondidas.Count == 0 ? "" : $"  •  Colunas sem dados, fora da tabela: {string.Join(", ", colunasEscondidas)}");
        ToolTipService.SetToolTip(Ac.Rodape, p.Caminho);
        // Sem nenhuma linha (planilha vazia ou filtros): a frase e a saída ficam na área da tabela.
        var semFiltro = filtros == 0 && Ac.CaixaBusca.Text.Length == 0;
        Ac.SemResultado.Visibility = processosNaTela.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Ac.TextoSemResultado.Text = p.Linhas.Count == 0 ? "A planilha ainda não tem processos. Use \"Novo processo\" para acrescentar o primeiro." : "Nenhum processo com esses filtros.";
        Ac.BotaoLimparFiltros.Visibility = p.Linhas.Count > 0 && !semFiltro ? Visibility.Visible : Visibility.Collapsed;
        FixarColunas();
    }

    FrameworkElement LinhaDaTabela(LinhaAc l)
    {
        var p = planilha!;
        var g = new Grid { Padding = new Thickness(12, 6, 12, 6), MinHeight = 52, Tag = l, Width = LarguraDaTabela - 8 };
        DefinirColunas(g);
        var i = 0;
        foreach (var c in ColunasVisiveis)
        {
            var texto = p.TextoDe(l, c);
            FrameworkElement el;
            if (texto.Length > 0 && c.Chave == ChaveColuna.Status)
            {
                // Status como a etiqueta de situação do editor (pedido do usuário, 2026-11-17: "mesmo botão ATENDE"): pílula com o fundo da
                // cor da planilha, bolinha e o texto em maiúsculas, negrito e espaçado; dá para bater o olho na coluna.
                el = EtiquetaStatus(texto, c.CorDe(texto, null));
            }
            else if (texto.Length > 0 && (c.Tipo == TipoColuna.Lista || c.Chave is ChaveColuna.Status or ChaveColuna.Aprovacao or ChaveColuna.Prioridade or ChaveColuna.DiasSemAtualizacao)
                && c.CorDe(texto, double.TryParse(texto, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : null) is { } cor)
                el = Etiqueta(c.Chave == ChaveColuna.DiasSemAtualizacao ? texto + (texto == "1" ? " dia" : " dias") : c.Chave == ChaveColuna.Prioridade ? Titulo(texto) : texto, cor);
            else if (c.Chave == ChaveColuna.DiasSemAtualizacao && texto.Length > 0)
                el = TextoCelula(texto + (texto == "1" ? " dia" : " dias"));
            else if (c.Chave is ChaveColuna.Requerente or ChaveColuna.Profissional or ChaveColuna.Local)
                el = TextoCelula(Titulo(texto));
            else if (c.Chave == ChaveColuna.Processo)
            el = new TextBlock { Text = texto, Style = (Style)Application.Current.Resources["PmvniPainelValorForte"], IsTextSelectionEnabled = false };
            else el = TextoCelula(texto);
            el.VerticalAlignment = VerticalAlignment.Center;
            if (Centralizada(c))
            {
                el.HorizontalAlignment = HorizontalAlignment.Center;
                if (el is TextBlock tb) tb.TextAlignment = TextAlignment.Center;
            }
            FrameworkElement celula = i < ColunasFixas ? CelulaFixa(el, i, g.Padding) : el;
            Grid.SetColumn(celula, i++);
            g.Children.Add(celula);
        }
        var mais = new Button { Content = new FontIcon { Glyph = Glifos.MaisOpcoes, Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["PmvniIconeAcao"] },
            Style = (Style)Application.Current.Resources["PmvniBarraButtonStyle"], Padding = new Thickness(0), Width = 32, Height = 32 };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(mais, "Mais ações do processo " + l.Processo);
        ToolTipService.SetToolTip(mais, "Mais ações");
        mais.Click += (_, _) => { var m = new MenuFlyout(); MontarMenuDoProcesso(m, l); m.ShowAt(mais); };
        Grid.SetColumn(mais, i);
        g.Children.Add(mais);
        g.ContextRequested += (_, e) =>
        {
            var m = new MenuFlyout();
            MontarMenuDoProcesso(m, l);
            if (e.TryGetPosition(g, out var ponto)) m.ShowAt(g, new FlyoutShowOptions { Position = ponto }); else m.ShowAt(g);
            e.Handled = true;
        };
        return g;
    }

    /// <summary>O menu da linha (botão '…' e botão direito) é montado só quando é pedido: antes, cada redesenho montava dois menus completos por linha.</summary>
    void MontarMenuDoProcesso(MenuFlyout m, LinhaAc l)
    {
        var pasta = new MenuFlyoutItem { Text = "Abrir pasta", Icon = new FontIcon { Glyph = Glifos.AbrirPasta, Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["PmvniIconeAcao"] } };
        pasta.Click += (_, _) => AbrirPastaDoProcesso(l);
        m.Items.Add(pasta);
        m.Items.Add(new MenuFlyoutSeparator());
        var editar = new MenuFlyoutItem { Text = "Editar processo…", Icon = new FontIcon { Glyph = Glifos.Editar, Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["PmvniIconeAcao"] },
            KeyboardAcceleratorTextOverride = "Enter" };
        editar.Click += async (_, _) => await EditarProcessoAsync(l);
        m.Items.Add(editar);
        void Submenu(ChaveColuna k, string titulo, string glifo)
        {
            if (planilha?.Coluna(k) is not { } c) return;
            var sub = new MenuFlyoutSubItem { Text = titulo, Icon = new FontIcon { Glyph = glifo, Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["PmvniIconeAcao"] } };
            foreach (var op in planilha.Opcoes(k).Concat(k == ChaveColuna.Aprovacao ? [""] : []))
            {
                var item = new ToggleMenuFlyoutItem { Text = op.Length == 0 ? "Sem decisão" : op, IsChecked = string.Equals(Planilha.Normal(l.Texto(k)), Planilha.Normal(op), StringComparison.Ordinal) };
                var valor = op;
                item.Click += async (_, _) => await MudarCampoAsync(l, c, valor);
                sub.Items.Add(item);
            }
            m.Items.Add(sub);
        }
        Submenu(ChaveColuna.Status, "Alterar status", Glifos.Situacao);
        Submenu(ChaveColuna.Aprovacao, "Alterar aprovação", Glifos.Conferir);
        Submenu(ChaveColuna.Prioridade, "Alterar prioridade", Glifos.Avisos);
        m.Items.Add(new MenuFlyoutSeparator());
        var copiar = new MenuFlyoutItem { Text = "Copiar número", Icon = new FontIcon { Glyph = Glifos.Copiar, Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["PmvniIconeAcao"] } };
        copiar.Click += (_, _) => AreaTransferencia.Copiar(l.ProcessoNormalizado);
        m.Items.Add(copiar);
        m.Items.Add(new MenuFlyoutSeparator());
        var excluir = new MenuFlyoutItem { Text = "Excluir da planilha…", Icon = new FontIcon { Glyph = Glifos.Excluir, Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["PmvniIconeAcao"] },
            KeyboardAcceleratorTextOverride = "Del" };
        excluir.Click += async (_, _) => await ExcluirProcessoAsync(l);
        m.Items.Add(excluir);
    }

    void MontarFiltros()
    {
        if (planilha is not { } p) return;
        var painel = Ac.PainelFiltros;
        painel.Children.Clear();
        void Grupo(string titulo, IEnumerable<string> opcoes, HashSet<string> marcados)
        {
            var lista = opcoes.Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (lista.Count == 0) return;
            painel.Children.Add(new TextBlock { Text = titulo, Style = (Style)Application.Current.Resources["PmvniPainelRotulo"], Margin = new Thickness(0, painel.Children.Count == 0 ? 0 : 10, 0, 2) });
            foreach (var o in lista)
            {
                var chave = Planilha.Normal(o);
                var cb = new CheckBox { Content = o, IsChecked = marcados.Contains(chave), MinHeight = 28, Padding = new Thickness(6, 4, 0, 4) };
                cb.Checked += (_, _) => { marcados.Add(chave); MostrarResumo(); PreencherLista(); };
                cb.Unchecked += (_, _) => { marcados.Remove(chave); MostrarResumo(); PreencherLista(); };
                painel.Children.Add(cb);
            }
        }
        Ac.MenuStatusRapido.Items.Clear();
        void SelecionarStatus(string? valor)
        {
            filtroStatus.Clear();
            if (valor is not null) filtroStatus.Add(Planilha.Normal(valor));
            MostrarResumo();
            MontarFiltros();
            PreencherLista();
        }
        var todos = new ToggleMenuFlyoutItem { Text = "Todos os status", IsChecked = filtroStatus.Count == 0 };
        todos.Click += (_, _) => SelecionarStatus(null);
        Ac.MenuStatusRapido.Items.Add(todos);
        foreach (var valor in p.Opcoes(ChaveColuna.Status))
        {
            var opcao = new ToggleMenuFlyoutItem { Text = Titulo(valor), IsChecked = filtroStatus.Contains(Planilha.Normal(valor)) };
            opcao.Click += (_, _) => SelecionarStatus(valor);
            Ac.MenuStatusRapido.Items.Add(opcao);
        }
        Grupo("Status", p.Opcoes(ChaveColuna.Status), filtroStatus);
        Grupo("Prioridade", p.Opcoes(ChaveColuna.Prioridade), filtroPrioridade);
        Grupo("Aprovação", p.Opcoes(ChaveColuna.Aprovacao), filtroAprovacao);
        Grupo("Profissional", p.Linhas.Select(l => l.Texto(ChaveColuna.Profissional)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase), filtroProfissional);
        var limpar = new Button { Content = "Limpar filtros", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        Dicas.Aplicar(limpar, "Limpar filtros", "Limpa a pesquisa e remove os filtros da tabela.");
        limpar.Click += (_, _) => LimparFiltrosAcompanhamento();
        painel.Children.Add(limpar);
    }

    void MostrarPainel(LinhaAc? l)
    {
        linhaEscolhida = l;
        Ac.Painel.Visibility = l is null ? Visibility.Collapsed : Visibility.Visible;
        PosicionarPainel();
        AtualizarComandosDoAcompanhamento();
        if (l is null || planilha is not { } p) return;
        Ac.TituloPainel.Text = "Processo " + l.Processo;
        var campos = Ac.CamposPainel;
        campos.Children.Clear();
        detectados.TryGetValue(l.ProcessoNormalizado, out var achados);
        foreach (var c in p.Colunas.Where(c => c.Chave != ChaveColuna.Processo))
        {
            var texto = p.TextoDe(l, c);
            if (c.Chave == ChaveColuna.DiasSemAtualizacao && texto.Length > 0) texto += texto == "1" ? " dia" : " dias";
            // Mesma grafia da tabela ("Fulano de Tal", "Baixa", "Em Análise"); a planilha fica como está.
            if (c.Chave is ChaveColuna.Requerente or ChaveColuna.Profissional or ChaveColuna.Local or ChaveColuna.Prioridade or ChaveColuna.Status) texto = Titulo(texto);
            var bloco = new StackPanel { Style = (Style)Application.Current.Resources["PmvniBlocoCampoStyle"] };
            bloco.Children.Add(new TextBlock { Text = c.Chave == ChaveColuna.Aprovacao ? "Aprovação" : c.Titulo, Style = (Style)Application.Current.Resources["PmvniPainelRotulo"] });
            if (texto.Length > 0 && (c.Tipo == TipoColuna.Lista || c.Chave == ChaveColuna.DiasSemAtualizacao) && c.CorDe(p.TextoDe(l, c), l.Dias(DateTime.Today)) is { } cor)
                bloco.Children.Add(Etiqueta(texto, cor));
            else
                bloco.Children.Add(new TextBlock { Text = texto.Length == 0 ? "—" : texto, Style = (Style)Application.Current.Resources["PmvniPainelValor"], Opacity = texto.Length == 0 ? .55 : 1 });
            if (achados?.FirstOrDefault(x => x.Campo == c.Chave) is { Fonte: { } fonte })
            {
                var nota = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
                nota.Children.Add(new FontIcon { Glyph = Glifos.Sobre, FontSize = 12, Style = (Style)Application.Current.Resources["PmvniIconeDestaqueStyle"] });
                nota.Children.Add(new TextBlock { Text = "Preenchido sozinho: " + fonte, Style = (Style)Application.Current.Resources["PmvniTextoDestaqueStyle"] });
                bloco.Children.Add(nota);
            }
            campos.Children.Add(bloco);
        }
        MenuTexto.Aplicar(campos);
    }

    /// <summary>Janela larga: o painel fica ao lado da tabela. Estreita (menos de 1300 de largura útil): o painel abre por cima da tabela,
    /// encostado à direita, e a tabela não encolhe (as colunas da frente continuam à vista).</summary>
    void PosicionarPainel()
    {
        var aberto = linhaEscolhida is not null;
        var sobreposto = Ac.ActualWidth is > 0 and < 1300;
        Ac.ColunaPainel.Width = aberto && !sobreposto ? new GridLength(340) : new GridLength(0);
        // Sobreposto, o painel sai da área da tabela e cobre da faixa dos avisos até o rodapé (no notebook a área da tabela é baixa demais
        // e o painel ficava só com o título e os botões).
        Grid.SetRow(Ac.Painel, sobreposto ? 1 : 3);
        Grid.SetRowSpan(Ac.Painel, sobreposto ? 3 : 1);
        Canvas.SetZIndex(Ac.Painel, sobreposto ? 10 : 0);
        Ac.Painel.Translation = sobreposto ? new System.Numerics.Vector3(0, 0, 32) : default;
        if (sobreposto) Ac.Painel.Shadow ??= new ThemeShadow();
        else Ac.Painel.Shadow = null;
    }

    void AtualizarComandosDoAcompanhamento()
    {
        var tem = !string.IsNullOrWhiteSpace(ajustes.PlanilhaAcompanhamento);
        var lida = planilha is not null;
        Faixa.Habilitar(ComandosApp.AcompanhamentoNovo, lida);
        Faixa.Habilitar(ComandosApp.AcompanhamentoEditar, lida && linhaEscolhida is not null);
        Faixa.Habilitar(ComandosApp.AcompanhamentoExcluir, lida && linhaEscolhida is not null);
        Faixa.Habilitar(ComandosApp.AcompanhamentoDetectar, lida && !string.IsNullOrWhiteSpace(ajustes.PastaProcessos));
        Faixa.DefinirDica(ComandosApp.AcompanhamentoDetectar, !lida ? "Escolha a planilha de acompanhamento primeiro."
            : string.IsNullOrWhiteSpace(ajustes.PastaProcessos) ? "Indisponível: escolha a pasta de processos em Ajustes › Pastas e kit."
            : "Preenche o Local e o Profissional vazios a partir dos relatórios, da consulta QGIS e das pranchas.");
        Faixa.Habilitar(ComandosApp.AcompanhamentoRecarregar, tem);
        Faixa.Habilitar(ComandosApp.AcompanhamentoAbrirExcel, tem);
        Faixa.Habilitar(ComandosApp.AcompanhamentoImportar, true);
        Faixa.Habilitar(ComandosApp.AcompanhamentoConfigurar, true);
        Ac.BotaoExportar.IsEnabled = lida;
        Ac.BotaoFiltrar.IsEnabled = lida;
        const string semPlanilha = "Abra uma planilha de acompanhamento.";
        Dicas.Aplicar(Ac.BotaoExportar, "Exportar", "Abre as opções de exportação e cópia da lista filtrada.",
            motivo: lida ? null : semPlanilha);
        Dicas.Aplicar(Ac.BotaoFiltrar, "Filtrar", "Escolhe os filtros detalhados da tabela.",
            motivo: lida ? null : semPlanilha);
    }

    void MostrarAvisoAcompanhamento(InfoBarSeverity s, string titulo, string mensagem, ButtonBase? acao = null)
    {
        var b = Ac.Aviso;
        b.Severity = s;
        b.Title = titulo;
        b.Message = mensagem;
        b.ActionButton = acao;
        b.IsOpen = true;
    }

    // ---------- gravar na planilha ----------

    /// <summary>Uma alteração que ainda não entrou na planilha (presa no Excel, ou um erro passageiro de disco/OneDrive): fica numa fila
    /// guardada em disco, com aviso fixo, e é gravada quando der (releitura, a cada 15 s ou no "Tentar de novo"). <see cref="Aba"/> é a aba
    /// em que foi feita (trocar de aba depois não a leva para outra).</summary>
    sealed record PendentePlanilha(IReadOnlyList<Alteracao> Alteracoes, string Descricao, string? Aba);

    readonly List<PendentePlanilha> pendentesPlanilha = [];
    readonly SemaphoreSlim filaPlanilha = new(1, 1);
    DispatcherTimer? tentarPendentes;
    string? motivoPendentes;

    static string ArquivoPendentes => Path.Combine(PastaDados.Raiz, "acompanhamento", "pendentes.json");

    /// <summary>Grava na planilha (uma gravação por vez) e relê. Presa no Excel ou com erro passageiro: a alteração vai para a fila de
    /// pendentes (nunca se perde por fechar um aviso). Só um conflito (a linha ou a aba sumiu da planilha) descarta, com aviso. Devolve as
    /// linhas gravadas ou null.</summary>
    async Task<IReadOnlyList<int>?> GravarNaPlanilhaAsync(IReadOnlyList<Alteracao> alteracoes, string sucesso)
    {
        if (ajustes.PlanilhaAcompanhamento is null || alteracoes.Count == 0) return null;
        var pendente = new PendentePlanilha(alteracoes, sucesso, ajustes.PlanilhaAba);
        // Com algo na fila, a ordem importa: a nova entra atrás e a fila é tentada inteira.
        if (pendentesPlanilha.Count > 0)
        {
            pendentesPlanilha.Add(pendente);
            GuardarPendentes();
            await GravarPendentesAsync(manual: false);
            return null;
        }
        var (resultado, linhas, motivo) = await GravarUmaAsync(pendente, avisar: true);
        if (resultado is ResultadoGravacao.EmUso or ResultadoGravacao.Erro)
        {
            motivoPendentes = motivo;
            pendentesPlanilha.Add(pendente);
            GuardarPendentes();
            MostrarPendentes();
        }
        return linhas;
    }

    /// <summary>Gravou; EmUso (aberta em outro programa); Erro (passageiro ou não, a alteração fica na fila); Descartada (conflito).</summary>
    enum ResultadoGravacao { Gravou, EmUso, Erro, Descartada }

    async Task<(ResultadoGravacao Resultado, IReadOnlyList<int>? Linhas, string? Motivo)> GravarUmaAsync(PendentePlanilha p, bool avisar)
    {
        if (ajustes.PlanilhaAcompanhamento is not { } caminho) return (ResultadoGravacao.Descartada, null, null);
        var abaAtual = ajustes.PlanilhaAba;
        await filaPlanilha.WaitAsync();
        Ac.Progresso.Visibility = Visibility.Visible;
        gravandoPlanilha = true;
        try
        {
            var linhas = await Task.Run(() => GravadorPlanilha.Gravar(caminho, p.Alteracoes, PastaCopiasPlanilha, aba: p.Aba));
            gravouPlanilhaEm = DateTime.UtcNow;
            planilha = await Task.Run(() => LeitorPlanilha.Ler(caminho, abaAtual));
            MostrarPlanilha();
            ConferirNotasAguardando();
            if (avisar) MostrarAvisoAcompanhamento(InfoBarSeverity.Success, p.Descricao, $"Gravado na planilha às {DateTime.Now:HH:mm}.");
            return (ResultadoGravacao.Gravou, linhas, null);
        }
        catch (PlanilhaEmUsoException) // o aviso fixo já explica "aberta no Excel"
        {
            return (ResultadoGravacao.EmUso, null, null);
        }
        catch (ConflitoPlanilhaException e)
        {
            // A alteração não entrou: o aviso diz isso com todas as letras (antes só dizia que a planilha mudou).
            await CarregarPlanilhaAsync();
            return (ResultadoGravacao.Descartada, null, e.Message);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            // A planilha não foi trocada (a versão nova é conferida num .tmp antes); a alteração fica na fila e as cópias de antes, na pasta
            // das cópias. Um erro passageiro (o OneDrive trocando o arquivo) passa na próxima tentativa.
           return (ResultadoGravacao.Erro, null, Erro(e));
        }
        finally
        {
            gravandoPlanilha = false;
            Ac.Progresso.Visibility = Visibility.Collapsed;
            filaPlanilha.Release();
        }
    }

    /// <summary>Tenta gravar a fila de pendentes, na ordem. Para na primeira que ainda não pôde ser gravada.</summary>
    async Task GravarPendentesAsync(bool manual)
    {
        if (tentandoPendentes) return;
        tentandoPendentes = true;
        try { await GravarPendentesNaVezAsync(manual); }
        finally { tentandoPendentes = false; }
    }

    bool tentandoPendentes;

    async Task GravarPendentesNaVezAsync(bool manual)
    {
        var gravadas = 0;
        while (pendentesPlanilha.Count > 0)
        {
            var p = pendentesPlanilha[0];
            var (r, _, motivo) = await GravarUmaAsync(p, avisar: false);
            if (r is ResultadoGravacao.EmUso or ResultadoGravacao.Erro) { motivoPendentes = motivo; break; }
            pendentesPlanilha.RemoveAt(0);
            GuardarPendentes();
            if (r == ResultadoGravacao.Gravou) gravadas++;
        }
        MostrarPendentes();
        if (pendentesPlanilha.Count == 0 && gravadas > 0)
            MostrarAvisoAcompanhamento(InfoBarSeverity.Success, gravadas == 1 ? "Alteração pendente gravada" : $"{gravadas} alterações pendentes gravadas", $"Gravado na planilha às {DateTime.Now:HH:mm}.");
        else if (manual && pendentesPlanilha.Count > 0)
            MostrarAvisoAcompanhamento(InfoBarSeverity.Informational, "A planilha ainda não pôde ser gravada", motivoPendentes ?? "Feche-a no Excel; o app grava sozinho quando ela for liberada.");
    }

    /// <summary>O aviso fixo das pendentes (não fecha no X nem numa releitura), com o motivo da última tentativa, e a nova tentativa a cada 15 s.</summary>
    void MostrarPendentes()
    {
        var n = pendentesPlanilha.Count;
        var b = Ac.AvisoPendentes;
        if (n == 0)
        {
            b.IsOpen = false;
            tentarPendentes?.Stop();
            return;
        }
        // Situação sob controle (as alterações estão guardadas): aviso, não erro; a frase não manda "tentar de novo" e "esperar" ao mesmo tempo.
        b.Severity = InfoBarSeverity.Warning;
        b.Title = n == 1 ? "1 alteração aguardando gravação" : $"{n} alterações aguardando gravação";
        b.Message = (motivoPendentes is { } motivo
                ? "A planilha não pôde ser gravada (" + motivo.TrimEnd('.') + "). O app tenta de novo a cada 15 segundos com o Acompanhamento em foco."
                : "A planilha está aberta em outro programa (provavelmente o Excel). Feche-a e volte ao Acompanhamento para gravar automaticamente.")
            + " As alterações estão guardadas; até lá a tabela mostra os valores de antes. Aguardando: "
            + string.Join("; ", pendentesPlanilha.Select(x => x.Descricao).Take(3)) + (n > 3 ? "…" : ".");
        var tentar = new Button { Content = "Gravar agora" };
        Dicas.Aplicar(tentar, "Gravar agora", "Tenta gravar as alterações pendentes na planilha.");
        tentar.Click += async (_, _) => await GravarPendentesAsync(manual: true);
        b.ActionButton = tentar;
        b.IsOpen = true;
        if (tentarPendentes is null)
        {
            tentarPendentes = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            tentarPendentes.Tick += async (_, _) => { if (!gravandoPlanilha && pendentesPlanilha.Count > 0) await GravarPendentesAsync(manual: false); };
        }
        if (janelaAtiva && moduloAtual == Modulo.Acompanhamento) tentarPendentes.Start();
    }

    /// <summary>A fila de pendentes vai para o disco (sobrevive a fechar o editor), com a planilha e a aba de cada uma.</summary>
    void GuardarPendentes()
    {
        try
        {
            if (pendentesPlanilha.Count == 0) { if (File.Exists(ArquivoPendentes)) File.Delete(ArquivoPendentes); return; }
            var lista = new System.Text.Json.Nodes.JsonArray();
            foreach (var p in pendentesPlanilha)
            {
                var alts = new System.Text.Json.Nodes.JsonArray();
                foreach (var a in p.Alteracoes)
                {
                    var vals = new System.Text.Json.Nodes.JsonObject();
                    foreach (var (c, v) in a.Valores)
                        vals[c.ToString(CultureInfo.InvariantCulture)] = new System.Text.Json.Nodes.JsonObject { ["t"] = v.Texto, ["d"] = v.Data?.ToString("O", CultureInfo.InvariantCulture), ["n"] = v.Numero };
                    alts.Add(new System.Text.Json.Nodes.JsonObject { ["linha"] = a.Linha, ["processo"] = a.ProcessoEsperado, ["excluir"] = a.Excluir, ["vazias"] = a.SomenteSeVazias, ["valores"] = vals });
                }
                lista.Add(new System.Text.Json.Nodes.JsonObject { ["descricao"] = p.Descricao, ["aba"] = p.Aba, ["alteracoes"] = alts });
            }
            var raiz = new System.Text.Json.Nodes.JsonObject { ["planilha"] = ajustes.PlanilhaAcompanhamento, ["pendentes"] = lista };
            Directory.CreateDirectory(Path.GetDirectoryName(ArquivoPendentes)!);
            // Troca segura (tmp + mover): uma queda no meio não apaga a fila guardada.
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { RegistroErros.Gravar("acompanhamento.pendentes", e); }
    }

    /// <summary>Na abertura: as pendentes guardadas da mesma planilha voltam para a fila (e o aviso aparece). Entrada que não se lê é pulada.</summary>
    void CarregarPendentes()
    {
        System.Text.Json.Nodes.JsonObject? raiz;
        try
        {
            if (!File.Exists(ArquivoPendentes)) return;
            raiz = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ArquivoPendentes)) as System.Text.Json.Nodes.JsonObject;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            RegistroErros.Gravar("acompanhamento.pendentes", e);
            return;
        }
        try { LerPendentes(raiz); }
        catch (Exception e) when (e is InvalidOperationException or FormatException) { RegistroErros.Gravar("acompanhamento.pendentes", e); }
    }

    /// <summary>Um valor de tipo errado no arquivo (GetValue) é InvalidOperationException: quem chama registra e segue sem as pendentes.</summary>
    void LerPendentes(System.Text.Json.Nodes.JsonObject? raiz)
    {
        if (raiz is null || !string.Equals(raiz["planilha"]?.GetValue<string>(), ajustes.PlanilhaAcompanhamento, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var p in (raiz["pendentes"] as System.Text.Json.Nodes.JsonArray) ?? [])
        {
            if (p is not System.Text.Json.Nodes.JsonObject po || po["alteracoes"] is not System.Text.Json.Nodes.JsonArray lista) continue;
            var alts = new List<Alteracao>();
            foreach (var a in lista)
            {
                if (a is not System.Text.Json.Nodes.JsonObject ao || ao["valores"] is not System.Text.Json.Nodes.JsonObject vo) continue;
                var vals = new Dictionary<int, ValorNovo>();
                foreach (var (k, v) in vo)
                {
                    if (!int.TryParse(k, NumberStyles.Integer, CultureInfo.InvariantCulture, out var col) || v is not System.Text.Json.Nodes.JsonObject cel) continue;
                    DateTime? data = cel["d"]?.GetValue<string>() is { } d && DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
                    vals[col] = new ValorNovo(cel["t"]?.GetValue<string>(), data, cel["n"]?.GetValue<double>());
                }
                alts.Add(new Alteracao(ao["linha"]?.GetValue<int>(), ao["processo"]?.GetValue<string>(), vals,
                    ao["excluir"]?.GetValue<bool>() ?? false, ao["vazias"]?.GetValue<bool>() ?? false));
            }
            if (alts.Count > 0) pendentesPlanilha.Add(new PendentePlanilha(alts, po["descricao"]?.GetValue<string>() ?? "alteração", po["aba"]?.GetValue<string>()));
        }
    }

    /// <summary>Mudar status, aprovação ou prioridade pelo menu: a data de atualização vira hoje.</summary>
    async Task MudarCampoAsync(LinhaAc l, Coluna c, string valor)
    {
        var valores = new Dictionary<int, ValorNovo> { [c.Indice] = valor.Length == 0 ? ValorNovo.Vazio : new ValorNovo(Texto: valor) };
        if (c.Chave != ChaveColuna.Prioridade && planilha?.Coluna(ChaveColuna.Atualizacao) is { } at) valores[at.Indice] = new ValorNovo(Data: DateTime.Today);
        await GravarNaPlanilhaAsync([new Alteracao(l.Linha, l.Processo, valores)], $"{c.Titulo} de {l.Processo}: {(valor.Length == 0 ? "sem decisão" : valor)}");
    }

    async Task ExcluirProcessoAsync(LinhaAc? l)
    {
        if (l is null) return;
        if (!await Perguntas.Atual.ConfirmarAsync($"Excluir o processo {l.Processo} da planilha?", $"A linha de {Titulo(l.Texto(ChaveColuna.Requerente))} será apagada da planilha de acompanhamento. Antes, o app guarda uma cópia da planilha na pasta de dados.", "Excluir processo", "Manter")) return;
        linhaEscolhida = null;
        await GravarNaPlanilhaAsync([new Alteracao(l.Linha, l.Processo, new Dictionary<int, ValorNovo>(), Excluir: true)], $"Processo {l.Processo} excluído da planilha");
    }

    async Task NovoProcessoNaPlanilhaAsync()
    {
        if (planilha is null) { await ImportarPlanilhaAsync(); return; }
        await EditarProcessoAsync(null);
    }

    /// <summary>O formulário do processo (todas as colunas, menos as calculadas). <paramref name="l"/> null = processo novo.</summary>
    async Task EditarProcessoAsync(LinhaAc? l)
    {
        if (planilha is not { } p) return;
        var novo = l is null;
        var pt = new CultureInfo("pt-BR");
        var form = new Grid { ColumnSpacing = 16, RowSpacing = 12, Width = 620 };
        form.ColumnDefinitions.Add(new ColumnDefinition());
        form.ColumnDefinitions.Add(new ColumnDefinition());
        var leitores = new List<(Coluna C, Func<ValorNovo> Valor, ValorNovo Antes)>();
        var pos = 0;
        CheckBox? hoje = null;
        TextBox? caixaProcesso = null;

        foreach (var c in p.Colunas.Where(c => c.Tipo != TipoColuna.Calculada))
        {
            var cel = l?.Celula(c.Indice) ?? Celula.Vazia;
            var textoAtual = l is null ? "" : p.TextoDe(l, c);
            FrameworkElement campo;
            Func<ValorNovo> ler;
            ValorNovo antes;
            var rotulo = c.Chave == ChaveColuna.Aprovacao ? "Aprovação" : c.Titulo;
            switch (c.Tipo)
            {
                case TipoColuna.Data:
                {
                    var data = Planilha.ComoData(cel);
                    if (novo && c.Chave == ChaveColuna.DataInicial) data = DateTime.Now;
                    if (novo && c.Chave == ChaveColuna.Atualizacao) data = DateTime.Today;
                    var dp = new CalendarDatePicker { Header = rotulo, Date = data, DateFormat = "{day.integer(2)}/{month.integer(2)}/{year.full}", HorizontalAlignment = HorizontalAlignment.Stretch, PlaceholderText = "dd/mm/aaaa", Language = "pt-BR" };
                    var original = data;
                    antes = original is { } o ? new ValorNovo(Data: o) : ValorNovo.Vazio;
                    ler = () =>
                    {
                        if (dp.Date is not { } d) return ValorNovo.Vazio;
                        // A hora da data inicial (ex.: 15:02) fica se o dia não mudou.
                        if (original is { } o2 && o2.Date == d.Date) return new ValorNovo(Data: o2);
                        return new ValorNovo(Data: d.Date);
                    };
                    campo = dp;
                    break;
                }
                case TipoColuna.Lista:
                {
                    var cb = new ComboBox { Header = rotulo, IsEditable = true, HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = c.Opcoes.Concat(c.Chave == ChaveColuna.Aprovacao ? [""] : []).ToList() };
                    var inicial = textoAtual;
                    if (novo && c.Chave == ChaveColuna.Status) inicial = c.Opcoes.FirstOrDefault(o => Planilha.Normal(o) == "aguardando analise") ?? c.Opcoes.FirstOrDefault() ?? "";
                    if (novo && c.Chave == ChaveColuna.Prioridade) inicial = c.Opcoes.FirstOrDefault() ?? "";
                    cb.Text = inicial;
                    cb.SelectedItem = c.Opcoes.FirstOrDefault(o => Planilha.Normal(o) == Planilha.Normal(inicial));
                    antes = textoAtual.Length == 0 ? ValorNovo.Vazio : new ValorNovo(Texto: textoAtual);
                    ler = () => { var t = (cb.SelectedItem as string ?? cb.Text ?? "").Trim(); return t.Length == 0 ? ValorNovo.Vazio : new ValorNovo(Texto: t.ToUpper(pt)); };
                    campo = cb;
                    break;
                }
                case TipoColuna.Numero:
                {
                    var inicial = textoAtual;
                    if (novo && c.Chave == ChaveColuna.Numero)
                        inicial = ((int)(p.Linhas.Select(x => x.Celula(c.Indice).Numero ?? 0).DefaultIfEmpty(0).Max() + 1)).ToString(CultureInfo.InvariantCulture);
                    var tb = new TextBox { Header = rotulo, Text = inicial, InputScope = new Microsoft.UI.Xaml.Input.InputScope { Names = { new Microsoft.UI.Xaml.Input.InputScopeName(Microsoft.UI.Xaml.Input.InputScopeNameValue.Number) } } };
                    antes = cel.Numero is { } n ? new ValorNovo(Numero: n) : textoAtual.Length == 0 ? ValorNovo.Vazio : new ValorNovo(Texto: textoAtual);
                    ler = () =>
                    {
                        var t = tb.Text.Trim();
                        if (t.Length == 0) return ValorNovo.Vazio;
                        return double.TryParse(t.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? new ValorNovo(Numero: v) : new ValorNovo(Texto: t);
                    };
                    campo = tb;
                    break;
                }
                default:
                {
                    var longo = c.Chave is ChaveColuna.Observacao or ChaveColuna.Local or ChaveColuna.Requerente;
                    var tb = new TextBox { Header = rotulo, Text = textoAtual, TextWrapping = longo ? TextWrapping.Wrap : TextWrapping.NoWrap, AcceptsReturn = false };
                    if (c.Chave == ChaveColuna.Processo) { tb.PlaceholderText = "00000/" + DateTime.Today.Year; caixaProcesso = tb; }
                    antes = textoAtual.Length == 0 ? ValorNovo.Vazio : new ValorNovo(Texto: textoAtual);
                    var maiusculas = c.Chave is ChaveColuna.Requerente or ChaveColuna.Local or ChaveColuna.Profissional or ChaveColuna.Observacao;
                    ler = () => { var t = tb.Text.Trim(); return t.Length == 0 ? ValorNovo.Vazio : new ValorNovo(Texto: maiusculas ? t.ToUpper(pt) : t); };
                    campo = tb;
                    if (detectados.TryGetValue(l?.ProcessoNormalizado ?? "", out var ach) && ach.FirstOrDefault(x => x.Campo == c.Chave) is { Fonte: { } f })
                        ToolTipService.SetToolTip(tb, "Preenchido sozinho: " + f);
                    break;
                }
            }
            var largo = c.Chave is ChaveColuna.Observacao or ChaveColuna.Requerente or ChaveColuna.Local;
            if (largo && pos % 2 == 1) pos++;
            var linhaGrade = pos / 2;
            while (form.RowDefinitions.Count <= linhaGrade) form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(campo, linhaGrade);
            Grid.SetColumn(campo, pos % 2);
            if (largo) { Grid.SetColumnSpan(campo, 2); pos += 2; } else pos++;
            form.Children.Add(campo);
            leitores.Add((c, ler, antes));
        }
        if (!novo && p.Coluna(ChaveColuna.Atualizacao) is not null)
        {
            hoje = new CheckBox { Content = "Ao gravar, a data de Atualização passa a ser hoje", IsChecked = true };
            if (pos % 2 == 1) pos++;
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(hoje, pos / 2);
            Grid.SetColumnSpan(hoje, 2);
            form.Children.Add(hoje);
        }
        var erro = new TextBlock { Style = (Style)Application.Current.Resources["PmvniTextoErroStyle"], Visibility = Visibility.Collapsed };
        var conteudo = new StackPanel { Spacing = 12 };
        conteudo.Children.Add(form);
        conteudo.Children.Add(erro);
        var dlg = new ContentDialog
        {
            XamlRoot = Raiz.XamlRoot, Title = novo ? "Novo processo" : "Editar processo " + l!.Processo,
            Content = new ScrollViewer { Content = conteudo, MaxHeight = 560, Padding = new Thickness(0, 0, 14, 0) },
            PrimaryButtonText = "Gravar na planilha", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Primary,
            RequestedTheme = Raiz.ActualTheme,
        };
        dlg.Resources["ContentDialogMaxWidth"] = 760.0;
        Dictionary<int, ValorNovo>? mudancas = null;
        dlg.PrimaryButtonClick += (_, e) =>
        {
            var valores = leitores.ToDictionary(x => x.C.Indice, x => x.Valor());
            var proc = p.Coluna(ChaveColuna.Processo)!;
            var numero = valores.TryGetValue(proc.Indice, out var vp) ? Planilha.ProcessoNormal(vp.Texto ?? "") : "";
            string? problema = null;
            if (numero.Length == 0) problema = "Informe o número do processo (ex.: 00123/" + DateTime.Today.Year + ").";
            else if (p.Linhas.Any(x => x != l && string.Equals(x.ProcessoNormalizado, numero, StringComparison.OrdinalIgnoreCase))) problema = $"O processo {numero} já está na planilha.";
            if (problema is not null)
            {
                erro.Text = problema;
                erro.Visibility = Visibility.Visible;
                caixaProcesso?.Focus(FocusState.Programmatic); // o foco volta ao campo do problema
                e.Cancel = true;
                return;
            }
            mudancas = new Dictionary<int, ValorNovo>();
            foreach (var (c, _, antes) in leitores)
            {
                var v = valores[c.Indice];
                // Só os campos que o formulário põe em maiúsculas (listas, requerente, local, profissional, observação) comparam sem a caixa.
                var ignorarCaixa = c.Tipo == TipoColuna.Lista || c.Chave is ChaveColuna.Requerente or ChaveColuna.Local or ChaveColuna.Profissional or ChaveColuna.Observacao;
                if (novo ? !v.EhVazio : !MesmoValor(v, antes, ignorarCaixa)) mudancas[c.Indice] = v;
            }
            if (!novo && hoje?.IsChecked == true && mudancas.Count > 0 && p.Coluna(ChaveColuna.Atualizacao) is { } at && !mudancas.ContainsKey(at.Indice))
                mudancas[at.Indice] = new ValorNovo(Data: DateTime.Today);
        };
        if (await dlg.ShowAsync() != ContentDialogResult.Primary || mudancas is null) return;
        if (mudancas.Count == 0) { MostrarAvisoAcompanhamento(InfoBarSeverity.Informational, "Nada mudou", "A planilha ficou como estava."); return; }
        var numeroFinal = mudancas.TryGetValue(p.Coluna(ChaveColuna.Processo)!.Indice, out var np) ? np.Texto : l?.Processo;
        await GravarNaPlanilhaAsync([new Alteracao(l?.Linha, l?.Processo, mudancas)], novo ? $"Processo {numeroFinal} acrescentado" : $"Processo {numeroFinal} atualizado");
        if (novo && planilha?.Linhas.FirstOrDefault(x => string.Equals(x.ProcessoNormalizado, Planilha.ProcessoNormal(numeroFinal ?? ""), StringComparison.OrdinalIgnoreCase)) is { } criado)
        {
            linhaEscolhida = criado;
            PreencherLista();
            if (ajustes.AcompanhamentoDetectar) await DetectarLocalEAutorAsync(manual: false, so: criado.ProcessoNormalizado);
        }
    }

    /// <summary>O formulário põe o texto em maiúsculas; um campo que só difere na caixa ("Fulano de Tal" × "FULANO DE TAL") não mudou e não é regravado.</summary>
    static bool MesmoValor(ValorNovo a, ValorNovo b, bool ignorarCaixa) =>
        a == b || (ignorarCaixa && a.Data is null && b.Data is null && a.Numero == b.Numero
            && string.Compare((a.Texto ?? "").Trim(), (b.Texto ?? "").Trim(), CulturaPt, CompareOptions.IgnoreCase) == 0);

    static readonly CultureInfo CulturaPt = new("pt-BR");

    // ---------- planilha: escolher, importar, abrir, exportar ----------

    async Task ConfigurarPlanilhaAsync()
    {
        await MudarModuloAsync(Modulo.Ajustes);
        PaginaDeAjustes.MostrarCategoria(CategoriasAjustes.FontesDados);
    }

    async Task ImportarPlanilhaAsync() => await EscolherPlanilhaAsync(depoisAbrir: true);

    async Task EscolherPlanilhaAsync(bool depoisAbrir)
    {
        if (pendentesPlanilha.Count > 0)
        {
            // As pendentes são da planilha atual: trocar agora as gravaria na outra.
            MostrarResultadoPlanilha(InfoBarSeverity.Warning, "Há alterações ainda não gravadas", "Feche a planilha atual no Excel para o app gravar as alterações pendentes e depois troque de planilha.");
            if (depoisAbrir) MostrarAvisoAcompanhamento(InfoBarSeverity.Warning, "Há alterações ainda não gravadas", "Feche a planilha atual no Excel para o app gravar as alterações pendentes e depois troque de planilha.");
            return;
        }
        var arquivo = await Perguntas.Atual.AbrirArquivoAsync(".xlsx;.xlsm");
        if (arquivo is null) return;
        PlanilhaLida lida;
        try { lida = await Task.Run(() => LeitorPlanilha.Ler(arquivo)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            MostrarResultadoPlanilha(InfoBarSeverity.Error, "Essa planilha não segue o modelo padrão", Erro(e));
            if (depoisAbrir) MostrarAvisoAcompanhamento(InfoBarSeverity.Error, "Essa planilha não segue o modelo padrão", Erro(e));
            return;
        }
        var anterior = ajustes.PlanilhaAcompanhamento;
        var abaAnterior = ajustes.PlanilhaAba;
        ajustes.PlanilhaAcompanhamento = arquivo;
        ajustes.PlanilhaAba = lida.Aba;
        try { ajustes.Salvar(PastaDados.Itinerante); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            ajustes.PlanilhaAcompanhamento = anterior;
            ajustes.PlanilhaAba = abaAnterior;
            MostrarResultadoPlanilha(InfoBarSeverity.Error, "A escolha não foi salva", Erro(e));
            return;
        }
        planilha = lida;
        detectados.Clear();
        LimparFiltros();
        VigiarPlanilha(arquivo);
        AtualizarAjustesPlanilha();
        MostrarResultadoPlanilha(InfoBarSeverity.Success, "Planilha pronta", ResumoDaPlanilha(lida));
        if (depoisAbrir)
        {
            if (moduloAtual != Modulo.Acompanhamento) await MudarModuloAsync(Modulo.Acompanhamento);
            else await EntrarNoAcompanhamentoAsync();
            MostrarAvisoAcompanhamento(InfoBarSeverity.Success, "Planilha importada", ResumoDaPlanilha(lida));
        }
    }

    static string ResumoDaPlanilha(PlanilhaLida p) =>
        $"{p.Linhas.Count} processos na aba {p.Aba}; {p.Colunas.Count} colunas ({string.Join(", ", p.Colunas.Select(c => c.Titulo))}); "
        + $"{p.Colunas.Count(c => c.Opcoes.Count > 0)} listas e {p.Colunas.Sum(c => c.Regras.Count)} regras de cor.";

    async Task VerificarPlanilhaAsync()
    {
        if (ajustes.PlanilhaAcompanhamento is not { } arquivo) return;
        try
        {
            var aba = ajustes.PlanilhaAba;
            var lida = await Task.Run(() => LeitorPlanilha.Ler(arquivo, aba));
            MostrarResultadoPlanilha(lida.AbaPedidaAusente ? InfoBarSeverity.Warning : InfoBarSeverity.Success,
                lida.AbaPedidaAusente ? $"A aba \"{aba}\" não está mais na planilha" : "Planilha acessível", ResumoDaPlanilha(lida));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            MostrarResultadoPlanilha(InfoBarSeverity.Error, "A planilha não pôde ser lida", Erro(e));
        }
    }

    void AtualizarAjustesPlanilha()
    {
        var a = PaginaDeAjustes;
        a.CaixaPlanilhaAcompanhamento.Text = ajustes.PlanilhaAcompanhamento ?? "";
        a.BotaoVerificarPlanilha.IsEnabled = a.BotaoAbrirPlanilha.IsEnabled = !string.IsNullOrWhiteSpace(ajustes.PlanilhaAcompanhamento);
    }

    void MostrarResultadoPlanilha(InfoBarSeverity s, string titulo, string mensagem)
    {
        var b = PaginaDeAjustes.ResultadoPlanilha;
        b.Severity = s;
        b.Title = titulo;
        b.Message = mensagem;
        b.IsOpen = true;
    }

    void AbrirPlanilhaNoExcel()
    {
        if (ajustes.PlanilhaAcompanhamento is not { } arquivo) return;
        if (!AberturaExterna.Abrir(arquivo, out var motivo))
            MostrarAvisoAcompanhamento(InfoBarSeverity.Error, "A planilha não abriu", motivo ?? arquivo);
    }

    string TextoDaLista(char separador)
    {
        if (planilha is not { } p) return "";
        var sb = new StringBuilder();
        string Campo(string s) => separador == ';' && (s.Contains(';') || s.Contains('"') || s.Contains('\n')) ? "\"" + s.Replace("\"", "\"\"") + "\"" : s.Replace('\t', ' ').Replace('\n', ' ');
        sb.AppendLine(string.Join(separador, p.Colunas.Select(c => Campo(c.Titulo))));
        foreach (var l in processosNaTela) sb.AppendLine(string.Join(separador, p.Colunas.Select(c => Campo(p.TextoDe(l, c)))));
        return sb.ToString();
    }

    async Task ExportarCsvAsync()
    {
        if (planilha is null) return;
        var destino = await Perguntas.Atual.SalvarArquivoAsync("Acompanhamento de processos " + DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ".csv");
        if (destino is null) return;
        try
        {
            await File.WriteAllTextAsync(destino, TextoDaLista(';'), new UTF8Encoding(true));
            MostrarAvisoAcompanhamento(InfoBarSeverity.Success, "Lista exportada", destino);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { MostrarAvisoAcompanhamento(InfoBarSeverity.Error, "A lista não foi exportada", Erro(e)); }
    }

    // ---------- pasta do processo e detecção de local e autor ----------

    /// <summary>Notas "Preenchido sozinho" de detecções cuja gravação ainda pode estar na fila: (processo, campo, valor detectado, origem).</summary>
    readonly List<(string Processo, ChaveColuna Campo, string Valor, string Fonte)> notasAguardando = [];

    /// <summary>Passa para o painel as notas cujo valor já está na planilha (a célula pode ter sido preenchida por alguém durante a busca:
    /// aí a nota não vale e sai).</summary>
    void ConferirNotasAguardando()
    {
        if (planilha is not { } p || notasAguardando.Count == 0) return;
        foreach (var n in notasAguardando.ToList())
        {
            if (p.Linhas.FirstOrDefault(x => string.Equals(x.ProcessoNormalizado, n.Processo, StringComparison.OrdinalIgnoreCase)) is not { } l) continue;
            var atual = l.Texto(n.Campo);
            if (atual.Length == 0 && pendentesPlanilha.Count > 0) continue; // ainda na fila
            notasAguardando.Remove(n);
            if (!string.Equals(atual, n.Valor, StringComparison.Ordinal)) continue;
            if (!detectados.TryGetValue(n.Processo, out var lista)) detectados[n.Processo] = lista = [];
            lista.RemoveAll(x => x.Campo == n.Campo);
            lista.Add((n.Campo, n.Fonte));
        }
        if (linhaEscolhida is { } e) MostrarPainel(p.Linhas.FirstOrDefault(x => x.Linha == e.Linha));
    }

    static (int Numero, int Ano)? NumeroAno(string processo)
    {
        var m = Regex.Match(Planilha.ProcessoNormal(processo), @"^0*(\d{1,6})/(\d{4})");
        return m.Success ? (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)) : null;
    }

    async Task<IReadOnlyList<Processo>> PastasDeProcessoAsync()
    {
        if (string.IsNullOrWhiteSpace(ajustes.PastaProcessos)) return [];
        var raiz = ajustes.PastaProcessos;
        try { return (await Task.Run(() => Processos.Listar(raiz, CancellationToken.None))).Itens; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return []; }
    }

    async void AbrirPastaDoProcesso(LinhaAc? l)
    {
        if (l is null) return;
        if (string.IsNullOrWhiteSpace(ajustes.PastaProcessos))
        {
            MostrarAvisoAcompanhamento(InfoBarSeverity.Warning, "Sem pasta de processos", "Escolha a pasta de processos em Ajustes › Pastas e kit.");
            return;
        }
        var na = NumeroAno(l.Processo);
        var pasta = na is { } x ? (await PastasDeProcessoAsync()).FirstOrDefault(p => p.Numero == x.Numero && p.Ano == x.Ano) : null;
        if (pasta is null) MostrarAvisoAcompanhamento(InfoBarSeverity.Warning, "Pasta não encontrada", $"Não há pasta do processo {l.Processo} em {ajustes.PastaProcessos}.");
        else AberturaExterna.AbrirPasta(pasta.Pasta);
    }

    /// <summary>Preenche o Local e o Profissional (autor do projeto) vazios: primeiro o relatório analisado mais recente (capa: LOCALIZAÇÃO e
    /// AUTORIA), depois a consulta QGIS do processo (inscrição → endereço no cadastro do módulo Imóveis) e, por fim, o carimbo das pranchas.
    /// Só células vazias: o que foi digitado nunca é trocado. <paramref name="manual"/>: carrega a base QGIS se preciso e sempre avisa.</summary>
    async Task DetectarLocalEAutorAsync(bool manual, string? so = null)
    {
        if (planilha is not { } p) return;
        var colLocal = p.Coluna(ChaveColuna.Local);
        var colAutor = p.Coluna(ChaveColuna.Profissional);
        if (colLocal is null && colAutor is null) return;
        if (string.IsNullOrWhiteSpace(ajustes.PastaProcessos))
        {
            if (manual) MostrarAvisoAcompanhamento(InfoBarSeverity.Warning, "Sem pasta de processos", "Para achar local e autor, escolha a pasta de processos em Ajustes › Pastas e kit.");
            return;
        }
        var alvos = p.Linhas.Where(l => so is null || string.Equals(l.ProcessoNormalizado, so, StringComparison.OrdinalIgnoreCase))
            .Where(l => (colLocal is not null && l.Texto(ChaveColuna.Local).Length == 0) || (colAutor is not null && l.Texto(ChaveColuna.Profissional).Length == 0)).ToList();
        if (alvos.Count == 0)
        {
            if (manual) MostrarAvisoAcompanhamento(InfoBarSeverity.Informational, "Nada a preencher", "Todos os processos já têm local e profissional.");
            return;
        }
        Ac.Progresso.Visibility = Visibility.Visible;
        try
        {
            var pastas = await PastasDeProcessoAsync();
            Imoveis.BaseQgis? baseQgis = baseImoveis;
            if (baseQgis is null && manual && !string.IsNullOrWhiteSpace(ajustes.PastaQgis)) baseQgis = await CarregarBaseAsync(ajustes.PastaQgis);
            var achados = await Task.Run(() =>
            {
                var r = new List<(LinhaAc L, Descoberta D)>();
                foreach (var l in alvos)
                {
                    if (NumeroAno(l.Processo) is not { } na || pastas.FirstOrDefault(x => x.Numero == na.Numero && x.Ano == na.Ano) is not { } pasta) continue;
                    var d = DetectorProcesso.Descobrir(pasta, baseQgis);
                    if (!d.Vazia) r.Add((l, d));
                }
                return r;
            });
            var alteracoes = new List<Alteracao>();
            var notas = new List<(string Processo, ChaveColuna Campo, string Fonte)>();
            var valoresDetectados = new Dictionary<(string, ChaveColuna), string>();
            foreach (var (l, d) in achados)
            {
                var v = new Dictionary<int, ValorNovo>();
                if (colLocal is not null && l.Texto(ChaveColuna.Local).Length == 0 && !string.IsNullOrWhiteSpace(d.Local))
                {
                    v[colLocal.Indice] = new ValorNovo(Texto: DadosDoProcesso.ParaPlanilha(d.Local));
                    valoresDetectados[(l.ProcessoNormalizado, ChaveColuna.Local)] = DadosDoProcesso.ParaPlanilha(d.Local);
                    notas.Add((l.ProcessoNormalizado, ChaveColuna.Local, d.FonteLocal ?? ""));
                }
                if (colAutor is not null && l.Texto(ChaveColuna.Profissional).Length == 0 && !string.IsNullOrWhiteSpace(d.Autor))
                {
                    v[colAutor.Indice] = new ValorNovo(Texto: DadosDoProcesso.ParaPlanilha(d.Autor));
                    valoresDetectados[(l.ProcessoNormalizado, ChaveColuna.Profissional)] = DadosDoProcesso.ParaPlanilha(d.Autor);
                    notas.Add((l.ProcessoNormalizado, ChaveColuna.Profissional, d.FonteAutor ?? ""));
                }
                if (v.Count > 0) alteracoes.Add(new Alteracao(l.Linha, l.Processo, v, SomenteSeVazias: true));
            }
            if (alteracoes.Count == 0)
            {
                if (manual) MostrarAvisoAcompanhamento(InfoBarSeverity.Informational, "Nada encontrado", $"Procurei em {alvos.Count} processos (relatórios, consulta QGIS e pranchas) e não achei local nem autor novos."
                    + (baseQgis is null && !string.IsNullOrWhiteSpace(ajustes.PastaQgis) ? "" : string.IsNullOrWhiteSpace(ajustes.PastaQgis) ? " Sem a pasta QGIS em Ajustes, a consulta ao cadastro fica de fora." : ""));
                return;
            }
            // As notas de origem só valem para o que de fato entrou: ficam aguardando e são conferidas depois de cada gravação (também
            // quando a alteração foi para a fila de pendentes e entra mais tarde).
            foreach (var (proc, campo, fonte) in notas)
                notasAguardando.Add((proc, campo, valoresDetectados.GetValueOrDefault((proc, campo)) ?? "", fonte));
            await GravarNaPlanilhaAsync(alteracoes, $"Local e autor preenchidos em {alteracoes.Count} {(alteracoes.Count == 1 ? "processo" : "processos")}");
            ConferirNotasAguardando();
        }
        finally { Ac.Progresso.Visibility = Visibility.Collapsed; }
    }
}
