using System.Globalization;
using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Pmvni.Agentes;
using Windows.Foundation;

namespace Pmvni.App;

/// <summary>Monitor de análise nativo (3.5.1). Antes era uma página HTML num WebView2, que não deixava o Mica aparecer atrás do menu;
/// agora é XAML: o menu (NavigationView) e o cabeçalho ficam sobre o Mica, o cabeçalho de situação fica fixo e o conteúdo de cada página
/// (Resumo, Etapas, Detalhes, Consumo e Log) é desenhado aqui, a partir dos mesmos dados que alimentam os controles escondidos do monitor
/// antigo (<c>RolagemMonitor</c>). Efeitos: radar nas tags "ao vivo", trilha das etapas com brilho correndo, elevação dos cartões ao passar
/// o mouse (ThemeShadow), entrada escalonada das páginas e a pílula do menu do próprio NavigationView.</summary>
public sealed partial class PaginaNovoRelatorio
{
    internal event EventHandler? EvidenciasSolicitadas;
    readonly DispatcherTimer pulsoMonitor = new() { Interval = TimeSpan.FromMilliseconds(750) };
    readonly ThemeShadow sombraCartoes = new();
    readonly bool animacoes = new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
    bool monitorLigado, sincronizandoMenu;
    long execucaoHtml, versaoLogHtml;
    string chaveCabecalho = "", chaveConteudo = "", paginaDesenhada = "";
    (string Decorrido, string Previsao) ultimoRelogio;
    string? ultimaDicaPrevisao;
    TextBlock? valorDecorrido, valorPrevisao, legendaTempos;
    readonly List<Action> atualizarCamposCabecalho = [];
    readonly List<Action> atualizarCamposEtapas = [];
    string? filtroAchados;
    int? etapaAberta;
    bool todasEvidencias;
    ContentDialog? dialogoAberto;

    void LigarMonitorNativo()
    {
        if (monitorLigado) return;
        monitorLigado = true;
        sombraCartoes.Receivers.Add(ReceptorSombras);
        pulsoMonitor.Tick += (_, _) => AtualizarMonitor();
        Unloaded += (_, _) => pulsoMonitor.Stop();
        Loaded += (_, _) => { if (VistaDaExecucao) { AtualizarMonitor(true); pulsoMonitor.Start(); } };
        MenuMonitor.SelectionChanged += (_, e) =>
        {
            if (sincronizandoMenu || e.SelectedItem is not NavigationViewItem { Content: string aba }) return;
            if (AbasMonitor.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Content, aba)) is { } item)
                AbasMonitor.SelectedItem = item;
        };
        MenuMonitor.SelectedItem = MenuMonitor.MenuItems[0];
    }

    void AbrirMonitor()
    {
        LigarMonitorNativo();
        SincronizarMenu();
        AtualizarMonitor(true);
        pulsoMonitor.Start();
    }

    void PararMonitor() => pulsoMonitor.Stop();

    /// <summary>O menu nativo acompanha a aba escolhida pelo código (por exemplo, "Ver registros" leva ao Log).</summary>
    void SincronizarMenu()
    {
        var aba = AbaAtual;
        if (MenuMonitor.SelectedItem is NavigationViewItem { Content: string atual } && atual == aba) return;
        sincronizandoMenu = true;
        MenuMonitor.SelectedItem = MenuMonitor.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => Equals(i.Content, aba));
        sincronizandoMenu = false;
    }

    string AbaAtual => (AbasMonitor.SelectedItem as NavigationViewItem)?.Content as string ?? "Resumo";

    void AtualizarMonitorHtml() => AtualizarMonitor();

    void AtualizarMonitor(bool forcar = false)
    {
        if (!VistaDaExecucao || !IsLoaded) return;
        SincronizarMenu();
        NavProcesso.Text = string.IsNullOrWhiteSpace(ProcessoMonitor.Text) ? "—" : ProcessoMonitor.Text;
        NavRequerente.Text = NomeProprio(TextoRequerente.Text);
        ToolTipService.SetToolTip(NavRequerente, NavRequerente.Text);

        var concluida = etapasMostradas.Length > 0 && etapasMostradas.All(e => e.Estado == EstadoEtapa.Feita);
        var relogio = (Decorrido: TextoTempoDecorrido.Text, Previsao: concluida ? "Concluída" : TextoTempoEstimado.Text);
        var dicaPrevisao = AutomationProperties.GetHelpText(TextoTempoEstimado);

        var trilha = string.Join(";", etapasMostradas.Select(e => e.Nome + ":" + e.Estado));
        var cabecalho = string.Join("|", TituloStatus.Text, emCurso, execucaoAtivaNaBarra, TextoEtapaAtual.Text,
            string.IsNullOrWhiteSpace(TextoTotalEtapas.Text), RotuloEstimado.Visibility, trilha);
        if (forcar || cabecalho != chaveCabecalho)
        {
            chaveCabecalho = cabecalho;
            atualizarCamposCabecalho.Clear();
            CabecalhoFixo.Child = CabecalhoDeSituacao(relogio, dicaPrevisao);
        }
        else
        {
            foreach (var atualizar in atualizarCamposCabecalho) atualizar();
            if (ultimoRelogio.Decorrido != relogio.Decorrido)
            {
                MostrarTempo(valorDecorrido, relogio.Decorrido, relogio.Decorrido);
                if (legendaTempos is not null) legendaTempos.Text = $"Onde os {relogio.Decorrido} foram gastos";
            }
            if (ultimoRelogio.Previsao != relogio.Previsao || ultimaDicaPrevisao != dicaPrevisao)
                MostrarTempo(valorPrevisao, relogio.Previsao, string.IsNullOrEmpty(dicaPrevisao) ? relogio.Previsao : dicaPrevisao);
        }
        ultimoRelogio = relogio;
        ultimaDicaPrevisao = dicaPrevisao;

        var aba = AbaAtual;
        var conteudo = aba switch
        {
            "Log" => string.Join("|", versaoLogHtml, atividades.Count, HoraUltimoEvento.Text, TituloStatus.Text, emCurso, BotaoEvidenciasPdf.Visibility),
            "Etapas" => ChaveEstruturaEtapas(trilha),
            "Detalhes" => string.Join("|", ModelosConfigurados, ChaveEtapas(), ErroDetalhes.Text, TextoUltimaMensagem.Text, PainelUltimaMensagem.Visibility,
                string.Join(";", detalhesEtapas.Validacoes), string.Join(";", detalhesEtapas.Plano)),
            "Consumo" => string.Join("|", ChaveConsumo(), orcamentoExibido),
            _ => string.Join("|", string.Join(";", achadosExibidos.Select(a => a.Id + a.Situacao + a.Verificado + a.Titulo)), filtroAchados, todasEvidencias,
                BotaoEvidenciasPdf.Visibility, TotalAtende.Text, TotalNaoAtende.Text, TotalComprovar.Text, TotalDivergentes.Text),
        };
        if (aba == "Resumo") conteudo += "|" + ConferenciaNumericaExibida;
        conteudo = aba + "|" + execucaoHtml + "|" + conteudo;
        if (!forcar && conteudo == chaveConteudo)
        {
            if (aba == "Etapas")
                foreach (var atualizar in atualizarCamposEtapas) atualizar();
            return;
        }
        chaveConteudo = conteudo;
        DesenharPagina(aba);
    }

    int IndiceEtapaAberta => etapaAberta ?? Array.FindIndex(etapasMostradas, e => e.Estado == EstadoEtapa.Agora);

    string ChaveEstruturaEtapas(string trilha)
    {
        var indice = IndiceEtapaAberta;
        var paineis = "";
        if (indice >= 0 && indice < etapasMostradas.Length && IdDaEtapa(etapasMostradas[indice].Nome) == "analyze")
            paineis = detalhesEtapas.Grupos.Count > 0 ? $"grupos:{detalhesEtapas.Grupos.Count}"
                : string.Join(";", detalhesEtapas.Frentes.Select(f => $"{f.Inicio}-{f.Fim}"));
        return string.Join("|", trilha, indice, paineis, TituloStatus.Text, emCurso, execucaoAtivaNaBarra);
    }

    string ChaveEtapas() => string.Join(";", etapasMostradas.Select((e, i) => e.Nome + e.Estado + PercentualDaEtapa(e.Nome, e.Estado)
        + linhasEtapas.ElementAtOrDefault(i)?.Resultado.Text + linhasEtapas.ElementAtOrDefault(i)?.Executor.Text))
        + string.Join(";", detalhesEtapas.Frentes) + string.Join(";", detalhesEtapas.Grupos) + string.Join(";", detalhesEtapas.Registros.Select(r => r.Key + r.Value))
        + string.Join(";", detalhesEtapas.Objecoes) + ChaveConsumo();

    string ChaveConsumo() => leituraExibida?.ToString() + "|" + string.Join(";", (consumoExibido ?? []).Select(u => u.ToString() + u.Usada5h + u.UsadaSemana + u.Plano + u.CpuMediaMhz + u.RamPicoBytes));

    // ───────────────────────────── desenho das páginas ─────────────────────────────

    void DesenharPagina(string aba)
    {
        // Preserva o foco e a rolagem do analista quando os dados mudam (a página só "entra" de novo quando muda de aba).
        var mudou = aba != paginaDesenhada;
        paginaDesenhada = aba;
        var focado = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        var nomeFocado = focado is not null && Contem(ConteudoMonitor, focado) ? AutomationProperties.GetName(focado) : null;
        var rolagem = RolagemConteudo.VerticalOffset;
        var noFim = rolagem >= RolagemConteudo.ScrollableHeight - 4;

        ConteudoMonitor.ChildrenTransitions = mudou && animacoes
            ? [new EntranceThemeTransition { IsStaggeringEnabled = true, FromVerticalOffset = 18 }]
            : null;
        ConteudoMonitor.Children.Clear();
        atualizarCamposEtapas.Clear();
        legendaTempos = null;
        switch (aba)
        {
            case "Etapas": PaginaEtapas(); break;
            case "Detalhes": PaginaDetalhes(); break;
            case "Consumo": PaginaConsumo(); break;
            case "Log": PaginaLog(); break;
            default: PaginaResumo(); break;
        }
        AutomationProperties.SetName(ConteudoMonitor, aba);
        ConteudoMonitor.UpdateLayout();
        if (mudou) RolagemConteudo.ChangeView(null, 0, null, true);
        else if (aba == "Log" && noFim) RolagemConteudo.ChangeView(null, RolagemConteudo.ScrollableHeight, null, true);
        else RolagemConteudo.ChangeView(null, rolagem, null, true);
        if (!mudou && nomeFocado is { Length: > 0 } nome && Procurar(ConteudoMonitor, nome) is Control alvo)
            alvo.Focus(FocusState.Programmatic);
    }

    static bool Contem(DependencyObject raiz, DependencyObject alvo)
    {
        for (var o = alvo; o is not null; o = VisualTreeHelper.GetParent(o))
            if (ReferenceEquals(o, raiz)) return true;
        return false;
    }

    static DependencyObject? Procurar(DependencyObject raiz, string nome)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is Control && AutomationProperties.GetName(filho) == nome) return filho;
            if (Procurar(filho, nome) is { } achado) return achado;
        }
        return null;
    }

    // ── Cabeçalho fixo: anel, situação, tempos e trilha das etapas ──

    UIElement CabecalhoDeSituacao((string Decorrido, string Previsao) relogio, string? dicaPrevisao)
    {
        var cartao = Cartao();
        cartao.Padding = new Thickness(0);
        Elevar(cartao, levantar: false);
        var raiz = new Grid { RowDefinitions = { new() { Height = GridLength.Auto }, new() { Height = GridLength.Auto } } };

        var topo = new Grid { Padding = new Thickness(20, 16, 20, 14), ColumnSpacing = 20, CornerRadius = new CornerRadius(7, 7, 0, 0) };
        // Brilho verde que nasce no canto superior esquerdo do cartão (o mesmo do monitor aprovado), em qualquer tema.
        topo.Background = new RadialGradientBrush
        {
            Center = new Point(0, 0), GradientOrigin = new Point(0, 0), RadiusX = 0.6, RadiusY = 1.5,
            GradientStops = { new() { Color = Windows.UI.Color.FromArgb(52, 47, 160, 95), Offset = 0 }, new() { Color = Windows.UI.Color.FromArgb(0, 47, 160, 95), Offset = 1 } },
        };
        topo.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        topo.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        topo.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

        var anel = new Grid { Width = 92, Height = 92, VerticalAlignment = VerticalAlignment.Center };
        // Sombra projetada embaixo do anel, no formato dele, para destacá-lo do brilho verde do cartão.
        var aro = Anel(92, 8, [(BarraResumoEtapas.Value, "MnArcoVerde")]);
        anel.Children.Add(SombraDoAnel(aro, 92));
        anel.Children.Add(aro);
        var centro = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var percentual = Id(Texto(string.IsNullOrEmpty(TextoPercentual.Text) ? "—" : TextoPercentual.Text, "MnPercentual"), "percent");
        centro.Children.Add(percentual);
        var valorArco = BarraResumoEtapas.Value;
        atualizarCamposCabecalho.Add(() =>
        {
            var texto = string.IsNullOrEmpty(TextoPercentual.Text) ? "—" : TextoPercentual.Text;
            if (percentual.Text != texto)
            {
                percentual.Text = texto;
                AutomationProperties.SetName(anel, "Progresso global da análise: " + TextoPercentual.Text);
            }
            if (valorArco == BarraResumoEtapas.Value) return;
            valorArco = BarraResumoEtapas.Value;
            AtualizarArcos(aro, 92, 8, [(valorArco, "MnArcoVerde")]);
        });
        if (RotuloEstimado.Visibility == Visibility.Visible)
        {
            var est = Texto("estimado", "MnLegenda");
            est.HorizontalAlignment = HorizontalAlignment.Center;
            centro.Children.Add(est);
        }
        anel.Children.Add(centro);
        AutomationProperties.SetName(anel, "Progresso global da análise: " + TextoPercentual.Text);
        topo.Children.Add(anel);

        var info = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        var linhaStatus = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var status = Id(Texto(TituloStatus.Text, "MnStatus"), "status");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        linhaStatus.Children.Add(status);
        if (emCurso) linhaStatus.Children.Add(TagAoVivo("Ao vivo"));
        info.Children.Add(linhaStatus);
        if (!string.IsNullOrWhiteSpace(TextoEtapaAtual.Text))
        {
            var atual = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            atual.Children.Add(Glifo(Glifos.Executar, "MnGlifo", 13));
            atual.Children.Add(Texto(TextoEtapaAtual.Text, "MnCorpo"));
            info.Children.Add(atual);
        }
        if (!string.IsNullOrWhiteSpace(TextoTotalEtapas.Text))
        {
            var etapa = Selo(TextoTotalEtapas.Text, "MnTag", "MnTagTexto");
            atualizarCamposCabecalho.Add(() =>
            {
                if (((TextBlock)etapa.Child).Text == TextoTotalEtapas.Text) return;
                ((TextBlock)etapa.Child).Text = TextoTotalEtapas.Text;
                AutomationProperties.SetName(etapa, TextoTotalEtapas.Text);
            });
            etapa.HorizontalAlignment = HorizontalAlignment.Left;
            info.Children.Add(etapa);
        }
        Grid.SetColumn(info, 1);
        topo.Children.Add(info);

        var tempos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        tempos.Children.Add(LadrilhoDeTempo(Glifos.Relogio, "Tempo decorrido", relogio.Decorrido, relogio.Decorrido, out valorDecorrido));
        Id(valorDecorrido, "elapsed");
        tempos.Children.Add(LadrilhoDeTempo(Glifos.Ano, "Previsão de término", relogio.Previsao,
            string.IsNullOrEmpty(dicaPrevisao) ? relogio.Previsao : dicaPrevisao, out valorPrevisao));
        Id(valorPrevisao, "eta");
        Grid.SetColumn(tempos, 2);
        topo.Children.Add(tempos);
        raiz.Children.Add(topo);

        var trilha = TrilhaDasEtapas();
        Grid.SetRow(trilha, 1);
        raiz.Children.Add(trilha);
        cartao.Child = raiz;
        return cartao;
    }

    Border LadrilhoDeTempo(string glifo, string rotulo, string valor, string dica, out TextBlock campo)
    {
        var ladrilho = new Border { Style = Estilo<Style>("MnLadrilho"), MinWidth = 190 };
        var grade = new Grid { ColumnSpacing = 12 };
        grade.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grade.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grade.Children.Add(IconeEmLadrilho(glifo, "MnIconeTile", 36));
        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        textos.Children.Add(Texto(rotulo, "MnLegenda"));
        campo = Texto("", "MnValor");
        MostrarTempo(campo, valor, dica);
        textos.Children.Add(campo);
        Grid.SetColumn(textos, 1);
        grade.Children.Add(textos);
        ladrilho.Child = grade;
        AutomationProperties.SetName(ladrilho, rotulo);
        Elevar(ladrilho);
        return ladrilho;
    }

    /// <summary>"12 min 05 s": os segundos em tamanho menor, como no monitor aprovado.</summary>
    static void MostrarTempo(TextBlock? campo, string valor, string dica)
    {
        if (campo is null) return;
        campo.Inlines.Clear();
        var texto = string.IsNullOrWhiteSpace(valor) ? "—" : valor;
        var m = System.Text.RegularExpressions.Regex.Match(texto, @"^(.+?) (\d{1,2} s)$");
        if (m.Success)
        {
            campo.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = m.Groups[1].Value + " " });
            campo.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = m.Groups[2].Value, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.Normal });
        }
        else campo.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = texto });
        ToolTipService.SetToolTip(campo, dica);
        AutomationProperties.SetName(campo, texto);
    }

    /// <summary>Trilha das etapas: colunas iguais, marcador no centro de cada uma e a linha passando de centro a centro. O trecho que leva à
    /// etapa em andamento tem um brilho correndo; o marcador atual pulsa como radar.</summary>
    Grid TrilhaDasEtapas()
    {
        var trilha = new Grid { Padding = new Thickness(12, 10, 12, 12), BorderThickness = new Thickness(0, 1, 0, 0) };
        trilha.BorderBrush = Estilo<Brush>("DividerStrokeColorDefaultBrush");
        AutomationProperties.SetName(trilha, "Progresso das etapas");
        var n = etapasMostradas.Length;
        for (var i = 0; i < n; i++)
        {
            trilha.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var (nome, estado) = etapasMostradas[i];
            var celula = new Grid { RowDefinitions = { new() { Height = new GridLength(22) }, new() { Height = GridLength.Auto } } };
            var linhas = new Grid();
            linhas.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            linhas.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            if (i > 0) linhas.Children.Add(Segmento(etapasMostradas[i - 1].Estado, estado, 0));
            if (i < n - 1) linhas.Children.Add(Segmento(estado, etapasMostradas[i + 1].Estado, 1));
            celula.Children.Add(linhas);
            celula.Children.Add(Marcador(estado));
            var prefixo = $"{i + 1:00} {nome}";
            var legenda = Texto(prefixo, estado is EstadoEtapa.Feita or EstadoEtapa.Agora ? "MnTagTextoSucesso" : "MnLegenda");
            if (estado == EstadoEtapa.Agora)
            {
                void AtualizarRotulo()
                {
                    var p = PercentualDaEtapa(nome, estado);
                    AtualizarTexto(legenda, p.Valor is null ? prefixo : prefixo + " · " + TextoPercentualEtapa(p));
                }
                AtualizarRotulo();
                atualizarCamposCabecalho.Add(AtualizarRotulo);
            }
            legenda.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            legenda.FontSize = 12;
            legenda.HorizontalAlignment = HorizontalAlignment.Center;
            legenda.TextTrimming = TextTrimming.CharacterEllipsis;
            legenda.Margin = new Thickness(4, 4, 4, 0);
            Grid.SetRow(legenda, 1);
            celula.Children.Add(legenda);
            AutomationProperties.SetName(celula, $"{nome} · {SituacaoDaEtapa(estado, execucaoAtivaNaBarra)}");
            Grid.SetColumn(celula, i);
            trilha.Children.Add(celula);
        }
        return trilha;
    }

    FrameworkElement Segmento(EstadoEtapa de, EstadoEtapa para, int lado)
    {
        var feito = de == EstadoEtapa.Feita;
        var chegando = emCurso && de == EstadoEtapa.Feita && para == EstadoEtapa.Agora;
        var grade = new Grid { Height = chegando ? 4 : 2, VerticalAlignment = VerticalAlignment.Center };
        grade.Children.Add(new Rectangle { Style = Estilo<Style>(feito ? "MnFormaVerde" : "MnFormaTrilho"), RadiusX = 1, RadiusY = 1 });
        Grid.SetColumn(grade, lado);
        if (chegando && animacoes)
        {
            // Brilho que corre pelo trecho da etapa em andamento.
            var brilho = new Rectangle
            {
                Width = 48, HorizontalAlignment = HorizontalAlignment.Left,
                Fill = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5),
                    GradientStops = { new() { Color = Windows.UI.Color.FromArgb(0, 255, 255, 255), Offset = 0 },
                        new() { Color = Windows.UI.Color.FromArgb(170, 255, 255, 255), Offset = 0.5 },
                        new() { Color = Windows.UI.Color.FromArgb(0, 255, 255, 255), Offset = 1 } },
                },
                RenderTransform = new TranslateTransform(),
            };
            grade.Children.Add(brilho);
            grade.SizeChanged += (_, e) =>
            {
                grade.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
                var anim = new DoubleAnimation { From = -48, To = e.NewSize.Width, Duration = TimeSpan.FromSeconds(1.1), RepeatBehavior = RepeatBehavior.Forever };
                Storyboard.SetTarget(anim, brilho.RenderTransform);
                Storyboard.SetTargetProperty(anim, "X");
                var sb = new Storyboard { Children = { anim } };
                sb.Begin();
            };
        }
        return grade;
    }

    FrameworkElement Marcador(EstadoEtapa estado)
    {
        var caixa = new Grid { Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var fundo = Estilo<Brush>("CardBackgroundFillColorDefaultBrush");
        switch (estado)
        {
            case EstadoEtapa.Feita:
                caixa.Children.Add(new Ellipse { Style = Estilo<Style>("MnPontoVerde") });
                var ok = Glifo(Glifos.Marca, "MnGlifo", 10);
                ok.Foreground = Estilo<Brush>("TextOnAccentFillColorPrimaryBrush");
                caixa.Children.Add(ok);
                break;
            case EstadoEtapa.Agora:
                if (emCurso && animacoes) caixa.Children.Add(Radar(new Ellipse { Style = Estilo<Style>("MnPontoVerde") }, 2.1));
                caixa.Children.Add(new Ellipse { Fill = fundo, Stroke = Estilo<Brush>("MnVerde"), StrokeThickness = 2 });
                caixa.Children.Add(new Ellipse { Width = 8, Height = 8, Style = Estilo<Style>("MnPontoVerde") });
                break;
            case EstadoEtapa.Erro:
                caixa.Children.Add(new Ellipse { Fill = fundo, Stroke = Estilo<Brush>("SystemFillColorCriticalBrush"), StrokeThickness = 2 });
                break;
            default:
                caixa.Children.Add(new Ellipse { Fill = fundo, Stroke = Estilo<Brush>("MnCinzaMedio"), StrokeThickness = 1.5 });
                break;
        }
        return caixa;
    }

    // ── Resumo ──

    void PaginaResumo()
    {
        Adicionar(TituloDePagina(Glifos.Painel, "Principais descobertas", "Constatações preliminares até a auditoria cruzada."), 0);

        var contagens = new Grid { ColumnSpacing = 12 };
        (string Estilo, string EstiloTexto, string Glifo, string Rotulo, string Valor, string Legenda, string Id)[] ladrilhos =
        [
            ("MnContagemAtende", "MnTagTextoSucesso", Glifos.Marca, "Atende", TotalAtende.Text, "conformidades", "ok"),
            ("MnContagemNaoAtende", "MnTagTextoErro", Glifos.NaoConfigurado, "Não atende", TotalNaoAtende.Text, "não conformidades", "fail"),
            ("MnContagemFalta", "MnTagTextoAtencao", Glifos.Avisos, "Falta comprovar", TotalComprovar.Text, "sem evidência", "unknown"),
            ("MnContagemDivergencia", "MnTagTextoIa", Glifos.Original, "Divergências", TotalDivergentes.Text, "inconsistências", "conflicts"),
        ];
        for (var i = 0; i < ladrilhos.Length; i++)
        {
            var l = ladrilhos[i];
            contagens.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var cartao = new Border { Style = Estilo<Style>(l.Estilo) };
            var pilha = new StackPanel { Spacing = 4 };
            var topo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var g = Texto(l.Glifo, l.EstiloTexto);
            g.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            g.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
            AutomationProperties.SetAccessibilityView(g, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            topo.Children.Add(g);
            var rotulo = Texto(l.Rotulo, l.EstiloTexto);
            rotulo.HorizontalAlignment = HorizontalAlignment.Left;
            topo.Children.Add(rotulo);
            pilha.Children.Add(topo);
            var base_ = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var valor = Id(Texto(string.IsNullOrEmpty(l.Valor) ? "0" : l.Valor, l.EstiloTexto), l.Id);
            valor.FontSize = 28;
            valor.HorizontalAlignment = HorizontalAlignment.Left;
            base_.Children.Add(valor);
            var leg = Texto(l.Legenda, "MnLegenda");
            leg.VerticalAlignment = VerticalAlignment.Center;
            base_.Children.Add(leg);
            pilha.Children.Add(base_);
            cartao.Child = pilha;
            AutomationProperties.SetName(cartao, $"{l.Rotulo}: {l.Valor} {l.Legenda}");
            Elevar(cartao);
            Grid.SetColumn(cartao, i);
            contagens.Children.Add(cartao);
        }
        Adicionar(contagens, 14);
        if (ConferenciaNumericaExibida is { } numeros)
        {
            var cartao = Cartao();
            var dados = new StackPanel { Margin = new Thickness(16, 12, 16, 12), Spacing = 6 };
            dados.Children.Add(Texto(numeros.Titulo, "MnCorpoForte"));
            dados.Children.Add(Id(Texto(numeros.Resumo, "MnSecundario"), "conferencia-numerica-resumo"));
            dados.Children.Add(new Expander { Header = "Ver índices e fontes", HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new TextBlock { Text = numeros.Detalhes, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } });
            cartao.Child = dados;
            Adicionar(Id(cartao, "conferencia-numerica-local"), 12);
        }

        var linhas = filtroAchados is null ? achadosExibidos : achadosExibidos.Where(a => Classificacao(a) == filtroAchados).ToArray();
        var filtro = new DropDownButton { Content = BotaoConteudo(Glifos.Filtro, filtroAchados ?? "Filtrar") };
        AutomationProperties.SetName(filtro, "Filtrar descobertas");
        var menu = new MenuFlyout();
        foreach (var opcao in new string?[] { null }.Concat(achadosExibidos.Select(Classificacao).Distinct()))
        {
            var item = new RadioMenuFlyoutItem { Text = opcao ?? "Todas as classificações", GroupName = "Classificacao", IsChecked = opcao == filtroAchados };
            item.Click += (_, _) => FiltrarAchados(opcao);
            menu.Items.Add(item);
        }
        filtro.Flyout = menu;
        Adicionar(CabecalhoDeSecao("Evidências identificadas", $"{linhas.Count} registros", filtro, idLegenda: "count"), 22);

        var lista = Cartao();
        Elevar(lista, levantar: false);
        var pilhaLista = new StackPanel();
        foreach (var a in linhas.Take(todasEvidencias ? int.MaxValue : 4))
            pilhaLista.Children.Add(LinhaDeEvidencia(a, pilhaLista.Children.Count > 0));
        if (linhas.Count == 0)
            pilhaLista.Children.Add(Vazio(filtroAchados is null ? "Aguardando primeiras descobertas." : "Nenhuma descoberta nesta classificação."));
        lista.Child = pilhaLista;
        Adicionar(lista, 8);

        var acoes = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        if (linhas.Count > 4)
            acoes.Children.Add(BotaoAcao(todasEvidencias ? Glifos.Anterior : Glifos.Proximo, todasEvidencias ? "Recolher para a prévia" : $"Ver todos os {linhas.Count} registros",
                () => { todasEvidencias = !todasEvidencias; AtualizarMonitor(true); }));
        if (BotaoEvidenciasPdf.Visibility == Visibility.Visible)
            acoes.Children.Add(BotaoAcao(Glifos.Relatorio, "Ver evidências por PDF e página", () => EvidenciasSolicitadas?.Invoke(this, EventArgs.Empty)));
        if (acoes.Children.Count > 0) Adicionar(acoes, 12);
    }

    static string Classificacao(AchadoExecucao a) => Situacoes[a.Situacao].Cor switch
    {
        "g" => "Atende",
        "r" => "Não atende",
        "a" => "Falta comprovar",
        _ => "Informações divergentes",
    };

    Button LinhaDeEvidencia(AchadoExecucao a, bool divisoria)
    {
        var cor = Situacoes[a.Situacao].Cor;
        var (faixa, estiloTexto) = cor switch
        {
            "g" => ("MnFormaVerde", "MnTagTextoSucesso"),
            "r" => ("MnFormaErro", "MnTagTextoErro"),
            "a" => ("MnFormaAtencao", "MnTagTextoAtencao"),
            _ => ("MnFormaInfo", "MnTagTextoIa"),
        };
        var grade = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 12, 16, 12) };
        grade.ColumnDefinitions.Add(new() { Width = new GridLength(3) });
        grade.ColumnDefinitions.Add(new() { Width = new GridLength(250) });
        grade.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grade.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grade.Children.Add(new Rectangle { Style = Estilo<Style>(faixa), Margin = new Thickness(0, -12, 0, -12) });
        var meta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var classe = Texto(Classificacao(a), estiloTexto);
        classe.HorizontalAlignment = HorizontalAlignment.Left;
        meta.Children.Add(classe);
        meta.Children.Add(Id(a.Verificado ? Selo("Verificada", "MnTagSucesso", "MnTagTextoSucesso") : Selo("Preliminar", "MnTagAtencao", "MnTagTextoAtencao"), "verification"));
        Grid.SetColumn(meta, 1);
        grade.Children.Add(meta);
        var texto = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        texto.Children.Add(Texto(a.Id, "MnCorpoForte"));
        texto.Children.Add(Texto(a.Titulo, "MnCorpo"));
        Grid.SetColumn(texto, 2);
        grade.Children.Add(texto);
        var seta = Glifo(Glifos.ExpandirLinha, "MnGlifoNeutro", 12);
        Grid.SetColumn(seta, 3);
        grade.Children.Add(seta);
        var botao = LinhaClicavel(grade, a.Titulo, divisoria, () => Mostrar(a.Id + " · " + a.Titulo,
            Situacoes[a.Situacao].Texto + " · " + (a.Verificado ? "Verificada" : "Preliminar")));
        return botao;
    }

    // ── Etapas ──

    void PaginaEtapas()
    {
        Adicionar(TituloDePagina(Glifos.Lista, "Atividades da análise", "Acompanhe as etapas e os registros de execução.",
            emCurso ? TagAoVivo("Em andamento") : Selo(TituloStatus.Text, "MnTag", "MnTagTexto")), 0);
        var atual = IndiceEtapaAberta;
        var lista = Cartao();
        Elevar(lista, levantar: false);
        var pilha = new StackPanel();
        for (var i = 0; i < etapasMostradas.Length; i++)
        {
            var (nome, estado) = etapasMostradas[i];
            var aberta = i == atual;
            var p = PercentualDaEtapa(nome, estado);
            var linha = new StackPanel();
            var cabeca = new Grid { ColumnSpacing = 14, Padding = new Thickness(16, 12, 16, 12) };
            cabeca.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            cabeca.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            cabeca.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            cabeca.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            cabeca.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            cabeca.Children.Add(IconeDaEtapa(nome, estado));
            var textos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(Texto(nome, "MnCorpoForte"));
            textos.Children.Add(Texto(DescricaoDaEtapa(nome), "MnSecundario"));
            Grid.SetColumn(textos, 1);
            cabeca.Children.Add(textos);
            if (!aberta)
            {
                var pct = Texto(TextoPercentualEtapa(p), estado == EstadoEtapa.Pendente ? "MnLegenda" : "MnVerdeForte");
                pct.Visibility = Visivel(p.Valor.HasValue);
                atualizarCamposEtapas.Add(() =>
                {
                    var percentual = PercentualDaEtapa(nome, estado);
                    AtualizarTexto(pct, TextoPercentualEtapa(percentual));
                    var visibilidade = Visivel(percentual.Valor.HasValue);
                    if (pct.Visibility != visibilidade) pct.Visibility = visibilidade;
                });
                pct.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(pct, 2);
                cabeca.Children.Add(pct);
            }
            var selo = SeloDaEtapa(estado);
            Grid.SetColumn(selo, 3);
            cabeca.Children.Add(selo);
            var seta = Glifo(aberta ? Glifos.Anterior : Glifos.Proximo, "MnGlifoNeutro", 12);
            Grid.SetColumn(seta, 4);
            cabeca.Children.Add(seta);
            linha.Children.Add(cabeca);
            if (aberta) linha.Children.Add(FaixaDaEtapaAberta(i, nome, estado, p));
            var indice = i;
            var botao = LinhaClicavel(linha, $"{nome} · {SituacaoDaEtapa(estado, execucaoAtivaNaBarra)}", i > 0, () =>
            {
                etapaAberta = aberta ? -1 : indice;
                AtualizarMonitor(true);
            });
            AutomationProperties.SetHelpText(botao, aberta ? "Aberta" : "Fechada");
            if (aberta)
            {
                var destaque = new Grid();
                destaque.Children.Add(botao);
                destaque.Children.Add(new Rectangle { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Style = Estilo<Style>("MnFormaVerde") });
                pilha.Children.Add(destaque);
            }
            else pilha.Children.Add(botao);
        }
        lista.Child = pilha;
        Adicionar(lista, 18);
        Adicionar(BotaoAcao(Glifos.Recentes, "Ver registros", AbrirLogDoMonitorNativo), 14);
    }

    void AbrirLogDoMonitorNativo() => AbasMonitor.SelectedItem = AbasMonitor.MenuItems.OfType<NavigationViewItem>().First(i => Equals(i.Content, "Log"));

    FrameworkElement IconeDaEtapa(string nome, EstadoEtapa estado)
    {
        var glifo = estado == EstadoEtapa.Feita ? Glifos.Concluido : IdDaEtapa(nome) switch
        {
            "prepare" => Glifos.Pasta,
            "analyze" => Glifos.Relatorio,
            "final_review" => Glifos.Marca,
            _ => Glifos.EmitirProcessoAprovado,
        };
        var estilo = estado switch
        {
            EstadoEtapa.Feita => "MnIconeTile",
            EstadoEtapa.Agora => "MnIconeTileAtivo",
            EstadoEtapa.Erro => "MnIconeTileErro",
            _ => "MnIconeTileNeutro",
        };
        var icone = IconeEmLadrilho(glifo, estilo, 32);
        if (estado == EstadoEtapa.Agora && ((Border)icone).Child is FontIcon f) f.Foreground = Estilo<Brush>("TextOnAccentFillColorPrimaryBrush");
        if (estado == EstadoEtapa.Pendente && ((Border)icone).Child is FontIcon fp) fp.Foreground = Estilo<Brush>("TextFillColorSecondaryBrush");
        return icone;
    }

    FrameworkElement SeloDaEtapa(EstadoEtapa estado)
    {
        var texto = SituacaoDaEtapa(estado, execucaoAtivaNaBarra);
        FrameworkElement selo = estado switch
        {
            EstadoEtapa.Agora when execucaoAtivaNaBarra && emCurso => TagAoVivo(texto),
            EstadoEtapa.Feita or EstadoEtapa.Agora => Selo(texto, "MnTagSucesso", "MnTagTextoSucesso"),
            EstadoEtapa.Erro => Selo(texto, "MnTagErro", "MnTagTextoErro"),
            _ => Selo(texto, "MnTag", "MnTagTexto"),
        };
        selo.MinWidth = 110;
        return selo;
    }

    /// <summary>Etapa aberta: numa linha só, anel + situação | frentes de seções com minibarra | modelo da IA.</summary>
    FrameworkElement FaixaDaEtapaAberta(int i, string nome, EstadoEtapa estado, (double? Valor, bool Aproximado) p)
    {
        var faixa = new Grid { ColumnSpacing = 20, Padding = new Thickness(62, 0, 16, 16) };
        faixa.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        faixa.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        faixa.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

        var guia = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        var anel = new Grid { Width = 52, Height = 52 };
        var arco = Anel(52, 5, [(p.Valor ?? 0, "MnArcoVerde")]);
        anel.Children.Add(arco);
        var pct = Id(Texto(TextoPercentualEtapa(p), "MnVerdeForte"), "stagePercent");
        pct.FontSize = 12;
        pct.HorizontalAlignment = HorizontalAlignment.Center;
        pct.VerticalAlignment = VerticalAlignment.Center;
        anel.Children.Add(pct);
        guia.Children.Add(anel);
        var situacao = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var origem = linhasEtapas[i];
        var resultado = Id(Texto(origem.Resultado.Text, "MnCorpoForte"), "result");
        situacao.Children.Add(resultado);
        atualizarCamposEtapas.Add(() =>
        {
            AtualizarTexto(resultado, origem.Resultado.Text);
            var progresso = PercentualDaEtapa(nome, estado);
            if (progresso == p) return;
            AtualizarTexto(pct, TextoPercentualEtapa(progresso));
            if (p.Valor != progresso.Valor) AtualizarArcos(arco, 52, 5, [(progresso.Valor ?? 0, "MnArcoVerde")]);
            p = progresso;
        });
        situacao.Children.Add(Texto("Resultados recebidos nesta etapa", "MnLegenda"));
        guia.Children.Add(situacao);
        faixa.Children.Add(guia);

        var id = IdDaEtapa(nome);
        IReadOnlyList<FrenteAnalise> frentes = id == "analyze" ? detalhesEtapas.Frentes : [];
        if (id == "analyze" && detalhesEtapas.Grupos.Count > 0)
        {
            var grupos = new PainelQuebra { Espaco = 10, EspacoLinhas = 10 };
            for (var k = 0; k < detalhesEtapas.Grupos.Count; k++)
            {
                var indice = k;
                var grupo = detalhesEtapas.Grupos[indice];
                var painel = new Border { Style = Estilo<Style>("MnPainel"), Width = 220 };
                var pilha = new StackPanel { Spacing = 6 };
                var titulo = Texto(grupo.Nome, "MnCorpoForte");
                var contagem = Texto("", "MnLegenda");
                var barra = BarraFina(0, "MnFormaVerde");
                void Preencher(GrupoAnalise g)
                {
                    AtualizarTexto(titulo, g.Nome);
                    AtualizarTexto(contagem, g.Concluido ? "Concluído" : $"{g.Prontas} de {g.Total} verificações");
                    contagem.Style = Estilo<Style>(g.Concluido ? "MnVerdeForte" : "MnLegenda");
                    AtualizarBarraFina(barra, g.Total > 0 ? 100d * g.Prontas / g.Total : 0);
                }
                Preencher(grupo);
                atualizarCamposEtapas.Add(() =>
                {
                    var novo = detalhesEtapas.Grupos[indice];
                    if (grupo == novo) return;
                    Preencher(novo);
                    grupo = novo;
                });
                pilha.Children.Add(titulo);
                pilha.Children.Add(contagem);
                pilha.Children.Add(barra);
                painel.Child = pilha;
                grupos.Children.Add(painel);
            }
            Grid.SetColumn(grupos, 1);
            faixa.Children.Add(grupos);
        }
        else if (frentes.Count > 0)
        {
            var grade = new Grid { ColumnSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
            for (var k = 0; k < frentes.Count; k++)
            {
                var f = frentes[k];
                grade.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                var painel = new Border { Style = Estilo<Style>("MnPainel") };
                var pilha = new StackPanel { Spacing = 6 };
                var topo = new Grid();
                topo.Children.Add(Texto($"Seções {f.Inicio}–{f.Fim}", "MnCorpoForte"));
                var conta = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                var marca = Glifo(Glifos.Marca, "MnGlifo", 11);
                marca.Visibility = Visivel(f.Concluida);
                conta.Children.Add(marca);
                var quantidade = Id(Texto($"{f.Prontas} de {f.Total}", f.Concluida ? "MnVerdeForte" : "MnLegenda"), "count" + k);
                conta.Children.Add(quantidade);
                topo.Children.Add(conta);
                pilha.Children.Add(topo);
                var barra = BarraFina(f.Total > 0 ? 100d * f.Prontas / f.Total : 0, "MnFormaVerde");
                pilha.Children.Add(barra);
                var indice = k;
                atualizarCamposEtapas.Add(() =>
                {
                    var nova = detalhesEtapas.Frentes[indice];
                    if (f == nova) return;
                    marca.Visibility = Visivel(nova.Concluida);
                    AtualizarTexto(quantidade, $"{nova.Prontas} de {nova.Total}");
                    quantidade.Style = Estilo<Style>(nova.Concluida ? "MnVerdeForte" : "MnLegenda");
                    AtualizarBarraFina(barra, nova.Total > 0 ? 100d * nova.Prontas / nova.Total : 0);
                    ToolTipService.SetToolTip(painel, nova.Atividade);
                    f = nova;
                });
                painel.Child = pilha;
                if (!string.IsNullOrWhiteSpace(f.Atividade)) ToolTipService.SetToolTip(painel, f.Atividade);
                Elevar(painel);
                Grid.SetColumn(painel, k);
                grade.Children.Add(painel);
            }
            Grid.SetColumn(grade, 1);
            faixa.Children.Add(grade);
        }
        var modelo = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(16, 0, 0, 0), BorderThickness = new Thickness(1, 0, 0, 0) };
        modelo.BorderBrush = Estilo<Brush>("DividerStrokeColorDefaultBrush");
        modelo.Children.Add(Texto("Modelo", "MnLegenda"));
        var executor = Texto(origem.Executor.Text, "MnCorpoForte");
        modelo.Children.Add(executor);
        modelo.Visibility = origem.Executor.Visibility;
        atualizarCamposEtapas.Add(() =>
        {
            AtualizarTexto(executor, origem.Executor.Text);
            if (modelo.Visibility != origem.Executor.Visibility) modelo.Visibility = origem.Executor.Visibility;
        });
        Grid.SetColumn(modelo, 2);
        faixa.Children.Add(modelo);
        return faixa;
    }

    // ── Detalhes ──

    internal string ModelosConfigurados { get; set; } = "";
    internal ConferenciaNumerica? ConferenciaNumericaExibida { get; set; }

    void PaginaDetalhes()
    {
        Adicionar(TituloDePagina(Glifos.Documento, "Detalhes da análise", "Plano, registros e resultados de cada etapa do processo.",
            Selo(etapasMostradas.Length == 1 ? "1 etapa mapeada" : $"{etapasMostradas.Length} etapas mapeadas", "MnTagSucesso", "MnTagTextoSucesso")), 0);
        var modelos = Cartao();
        var grade = new StackPanel { Spacing = 5, Margin = new Thickness(16, 12, 16, 12) };
        grade.Children.Add(Texto("Modelos configurados para esta análise", "MnCorpoForte"));
        grade.Children.Add(Texto(string.IsNullOrEmpty(ModelosConfigurados) ? "Lendo a configuração desta execução…" : ModelosConfigurados, "MnSecundario"));
        modelos.Child = grade;
        Adicionar(modelos, 12);
        var lista = Cartao();
        Elevar(lista, levantar: false);
        var pilha = new StackPanel();
        for (var i = 0; i < etapasMostradas.Length; i++)
        {
            var (nome, estado) = etapasMostradas[i];
            var feita = estado == EstadoEtapa.Feita;
            var id = IdDaEtapa(nome);
            var linha = new StackPanel { BorderThickness = new Thickness(0, i > 0 ? 1 : 0, 0, 0), BorderBrush = Estilo<Brush>("DividerStrokeColorDefaultBrush") };
            var cabeca = new Grid { ColumnSpacing = 12, Padding = new Thickness(16, 12, 16, 12) };
            cabeca.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            cabeca.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            cabeca.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            cabeca.Children.Add(IconeDaEtapa(nome, estado));
            var textos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
            textos.Children.Add(Id(Texto($"{i + 1:00}  {nome}", "MnCorpoForte"), "stage"));
            textos.Children.Add(Texto(EscopoDaEtapa(id, nome), "MnSecundario"));
            Grid.SetColumn(textos, 1);
            cabeca.Children.Add(textos);
            var direita = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            var uso = (consumoExibido ?? []).Where(u => u.Etapa == id).ToArray();
            var executor = linhasEtapas.ElementAtOrDefault(i)?.Executor.Text;
            direita.Children.Add(SeloDaEtapa(estado));
            Grid.SetColumn(direita, 2);
            cabeca.Children.Add(direita);
            linha.Children.Add(cabeca);
            // Modelo e medições numa linha própria (quebra se não couber), sem disputar espaço com o nome da etapa.
            var fatos = new[] { executor }.Concat(uso.SelectMany(u => TextoConsumo.Metricas(u).Select(m => $"{m.Rotulo} · {m.Valor}"))).Where(f => !string.IsNullOrWhiteSpace(f)).ToArray();
            if (fatos.Length > 0)
            {
                var fichas = new PainelQuebra { Espaco = 6, EspacoLinhas = 6, Margin = new Thickness(62, 0, 16, 12) };
                foreach (var fato in fatos) fichas.Children.Add(Selo(fato!, "MnTag", "MnTagTexto"));
                linha.Children.Add(fichas);
            }

            var paineis = new PainelQuebra { Espaco = 12, EspacoLinhas = 12, Margin = new Thickness(62, 0, 16, 16) };
            var plano = id is null ? null : detalhesEtapas.DescricoesDoPlano.GetValueOrDefault(id);
            if (plano?.StartsWith("Saídas previstas: ", StringComparison.Ordinal) == true)
                ListaDeVerificacao(paineis, "Entregas previstas", DividirEntregas(plano["Saídas previstas: ".Length..]).Select(t => (t, (bool?)feita)));
            else if (!string.IsNullOrWhiteSpace(plano))
                ListaDeVerificacao(paineis, "Plano do preparo", Linhas(plano).Select(t => (t, (bool?)feita)));
            ListaDeVerificacao(paineis, "Passos da IA", uso.Where(u => u.Plano != null).Select(u => u.Plano!).Distinct().SelectMany(Linhas).Select(t => (t, (bool?)feita)));
            if (id == "analyze" && detalhesEtapas.Grupos.Count > 0)
                ListaDeVerificacao(paineis, "Grupos de análise", detalhesEtapas.Grupos.Select(g => (
                    $"{g.Nome} · {g.Prontas} de {g.Total} verificações", (bool?)g.Concluido)));
            else if (id == "analyze")
                ListaDeVerificacao(paineis, "Frentes de análise", detalhesEtapas.Frentes.Select(f => (
                    $"Seções {f.Inicio}–{f.Fim} · {f.Prontas} de {f.Total}" + (string.IsNullOrWhiteSpace(f.Atividade) ? "" : " · " + f.Atividade), (bool?)(feita || f.Concluida))));
            var registros = id is null ? null : detalhesEtapas.Registros.GetValueOrDefault(id);
            ListaDeVerificacao(paineis, "Registros da execução", Linhas(registros).Select(t => (t == "nova" ? "Sessão iniciada" : t, (bool?)true)));
            ListaDeVerificacao(paineis, "Objeções", detalhesEtapas.Objecoes.Where(o => o.Etapa == id).Select(o => (FluxoNovoRelatorio.TextoObjecao(o), (bool?)null)));
            if (paineis.Children.Count > 0) linha.Children.Add(paineis);
            pilha.Children.Add(linha);
        }
        lista.Child = pilha;
        Adicionar(lista, 18);

        foreach (var (titulo, itens, situacao) in new (string, IEnumerable<string>, bool?)[]
        {
            ("Avisos e consertos registrados", detalhesEtapas.Validacoes, null),
            ("Falha da execução", [ErroDetalhes.Text], null),
            ("Última mensagem", PainelUltimaMensagem.Visibility == Visibility.Visible ? [TextoUltimaMensagem.Text] : [], true),
        })
        {
            var linhas = itens.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
            if (linhas.Length == 0) continue;
            var extra = new PainelQuebra();
            ListaDeVerificacao(extra, titulo, linhas.Select(t => (t, situacao)));
            if (extra.Children.FirstOrDefault() is Border painel)
            {
                extra.Children.Clear();
                painel.Style = Estilo<Style>("MnCartao");
                painel.Padding = new Thickness(16, 12, 16, 12);
                Elevar(painel);
                Adicionar(painel, 12);
            }
        }
    }

    string EscopoDaEtapa(string? id, string nome) => id switch
    {
        "prepare" => "Identificar as peças atuais, organizar as pranchas e levantar os documentos, as cotas e os dados de enquadramento que precisam de conferência.",
        "analyze" => "Conferir os grupos do plano, localizar cada achado na peça e na página e registrar as evidências e os cálculos que sustentam o relatório.",
        "technical_audit" => "Confrontar cada achado com a figura citada, reabrir o original quando necessário e conferir o mérito, as referências e os cálculos.",
        "audit" => "Conferir a integridade do manifesto e dos controles certificados na auditoria técnica.",
        "produce" => "Montar o PDF no modelo oficial e conferir se os textos, cálculos e figuras reproduzem os dados aprovados.",
        "final_review" => "Inspecionar cada página do PDF e verificar as assinaturas de integridade dos arquivos antes da entrega.",
        _ => DescricaoDaEtapa(nome),
    };

    static IEnumerable<string> Linhas(string? texto) =>
        (texto ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);

    static IEnumerable<string> DividirEntregas(string texto) =>
        System.Text.RegularExpressions.Regex.Split(texto, @", (?![^()]*\))").Select(t => t.Trim()).Where(t => t.Length > 0);

    /// <summary>Painel com ✓ (concluído), ○ (pendente) ou ! (atenção, quando <c>null</c>) na frente de cada item e a contagem de concluídos.</summary>
    void ListaDeVerificacao(Panel destino, string titulo, IEnumerable<(string Texto, bool? Feito)> itens)
    {
        var lista = itens.Where(i => !string.IsNullOrWhiteSpace(i.Texto)).ToArray();
        if (lista.Length == 0) return;
        var painel = new Border { Style = Estilo<Style>("MnPainel"), Width = 300 };
        var pilha = new StackPanel { Spacing = 6 };
        var topo = new Grid();
        topo.Children.Add(Texto(titulo, "MnCorpoForte"));
        var conta = Texto($"{lista.Count(i => i.Feito == true)}/{lista.Length}", "MnLegenda");
        conta.HorizontalAlignment = HorizontalAlignment.Right;
        topo.Children.Add(conta);
        pilha.Children.Add(topo);
        foreach (var (texto, feito) in lista)
        {
            var linha = new Grid { ColumnSpacing = 8 };
            linha.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            linha.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            var marca = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 0, 0) };
            if (feito == true)
            {
                marca.Children.Add(new Ellipse { Style = Estilo<Style>("MnPontoVerde") });
                var ok = Glifo(Glifos.Marca, "MnGlifo", 9);
                ok.Foreground = Estilo<Brush>("TextOnAccentFillColorPrimaryBrush");
                marca.Children.Add(ok);
                AutomationProperties.SetName(marca, "Concluído");
            }
            else if (feito == false)
            {
                marca.Children.Add(new Ellipse { Stroke = Estilo<Brush>("MnCinzaMedio"), StrokeThickness = 1.5 });
                AutomationProperties.SetName(marca, "Pendente");
            }
            else
            {
                marca.Children.Add(new Ellipse { Fill = Estilo<Brush>("SystemFillColorCautionBackgroundBrush"), Stroke = Estilo<Brush>("SystemFillColorCautionBrush"), StrokeThickness = 1 });
                var aviso = Glifo("!", "MnGlifo", 10);
                aviso.FontFamily = new FontFamily("Segoe UI");
                aviso.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
                aviso.Foreground = Estilo<Brush>("SystemFillColorCautionBrush");
                marca.Children.Add(aviso);
                AutomationProperties.SetName(marca, "Atenção");
            }
            linha.Children.Add(marca);
            var t = Id(Texto(texto, feito == false ? "MnSecundario" : "MnCorpo"), "item");
            t.TextWrapping = TextWrapping.Wrap;
            t.TextTrimming = TextTrimming.None;
            t.IsTextSelectionEnabled = true;
            Grid.SetColumn(t, 1);
            linha.Children.Add(t);
            pilha.Children.Add(linha);
        }
        painel.Child = pilha;
        Elevar(painel);
        destino.Children.Add(painel);
    }

    // ── Consumo ──

    (long? Total, long? Entrada, long? Saida, long? Cache, long? Nova, bool Parcial, DateTimeOffset? Hora) TotaisMedidos(IReadOnlyList<UsoEtapa> ia)
    {
        static long? Tokens(UsoEtapa l) => l.Total ?? (l.Executor == "Claude" && l.EntradaIncluiCache != true
            ? l.Entrada + l.Saida + l.CacheLido + l.CacheGravado : l.Entrada + l.Saida);
        var total = SomaMedida(ia.Select(Tokens));
        if (leituraExibida is { Total: { } atual } leitura && (total is null || atual >= total))
            return (atual, leitura.Entrada, leitura.Saida, leitura.CacheLido,
                leitura.CacheLido <= leitura.Entrada ? leitura.Entrada - leitura.CacheLido : null,
                leitura.TokensParciais, leitura.TokensObservadosEm);
        var cache = EntradasComCache(ia);
        return (total, SomaMedida(ia.Select(e => e.Entrada)), SomaMedida(ia.Select(e => e.Saida)),
            SomaMedida(ia.Select(e => e.CacheLido)), cache is { } c ? (long?)c.Nova : null, ia.Any(e => Tokens(e) is null), null);
    }

    void PaginaConsumo()
    {
        var uso = consumoExibido ?? [];
        var ia = uso.Where(e => e.Executor != UsoDasIas.Programa).ToArray();
        var fichas = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var totais = TotaisMedidos(ia);
        var total = totais.Total is { } t ? TextoConsumo.Numero(t) : "Aguardando medição";
        fichas.Children.Add(Selo((totais.Parcial ? "Tokens parciais · " : "Tokens totais · ") + total, "MnTag", "MnTagTexto"));
        if (totais.Hora is { } hora)
            fichas.Children.Add(Selo("Medidos às " + hora.ToLocalTime().ToString("HH:mm:ss"), "MnTag", "MnTagTexto"));
        Adicionar(TituloDePagina(Glifos.Grafico, "Consumo e desempenho", "Tokens, cotas e tempo gastos pela análise até agora.", fichas), 0);

        if (orcamentoExibido is { } orc)
        {
            var nota = Cartao();
            nota.Padding = new Thickness(16, 10, 16, 10);
            var pilhaNota = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
            pilhaNota.Children.Add(Texto(orc.MedicaoCompleta ? "Cota de 5 h desta análise" : "Medição parcial da cota de 5 h", "MnCorpoForte"));
            pilhaNota.Children.Add(Texto($"{Pontos(orc.Medido)} observados em contas sem compartilhamento detectado", "MnSecundario"));
            if (orc.SemAtribuicao > 0) pilhaNota.Children.Add(Texto($"{Pontos(orc.SemAtribuicao)} sem atribuição individual", "MnSecundario"));
            if (!orc.MedicaoCompleta) pilhaNota.Children.Add(Texto("Inclui uso de conta com cota semanal. Consulte os tokens por etapa.", "MnSecundario"));
            pilhaNota.Children.Add(Texto("Meta por análise completa · 50 p.p.", "MnSecundario"));
            nota.Child = pilhaNota;
            Adicionar(Id(nota, "budget"), 14);
        }

        var graficos = new Grid { ColumnSpacing = 12 };
        for (var k = 0; k < 3; k++) graficos.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        graficos.Children.Add(GraficoComposicao(ia));
        var cotas = GraficoCotas(ia);
        Grid.SetColumn(cotas, 1);
        graficos.Children.Add(cotas);
        var tempos = GraficoTempos(uso);
        Grid.SetColumn(tempos, 2);
        graficos.Children.Add(tempos);
        Adicionar(graficos, 16);

        var executores = Cartao();
        var pilhaEx = new StackPanel();
        pilhaEx.Children.Add(CabecalhoDeSecao("Consumo por executor", "Cota gasta por modelo de IA e recursos da máquina no processamento local", null, true));
        foreach (var grupo in uso.GroupBy(NomeExecutor))
        {
            var r = grupo.Last();
            var local = r.Executor == UsoDasIas.Programa;
            var linha = new Grid { ColumnSpacing = 20, Padding = new Thickness(20, 12, 20, 12), BorderThickness = new Thickness(0, 1, 0, 0), BorderBrush = Estilo<Brush>("DividerStrokeColorDefaultBrush") };
            linha.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            linha.ColumnDefinitions.Add(new() { Width = new GridLength(1.3, GridUnitType.Star) });
            linha.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            linha.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            linha.Children.Add(SeloExecutor(local, 32));
            var quem = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            quem.Children.Add(Texto(grupo.Key, "MnCorpoForte"));
            quem.Children.Add(Texto(string.Join(", ", grupo.Select(g => FluxoNovoRelatorio.NomeDaEtapa(g.Etapa)).Distinct()), "MnLegenda"));
            Grid.SetColumn(quem, 1);
            linha.Children.Add(quem);
            var medidas = local
                ? new (string, string, double?, string?)[] { ("CPU", $"{Ghz(r.CpuMediaMhz)} · pico {Ghz(r.CpuPicoMhz)}", null, null), ("RAM", r.RamPicoBytes is { } b ? $"{b / 1048576d:0.#} MiB" : "—", null, null) }
                : [("Variação · 5 h", Pontos(r.Cota5h), r.Cota5h, "MnFormaOcre"), ("Variação · semana", Pontos(r.CotaSemana), r.CotaSemana, "MnFormaLilas")];
            for (var k = 0; k < medidas.Length; k++)
            {
                var (rotulo, valor, barra, cor) = medidas[k];
                var m = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
                var topo = new Grid();
                topo.Children.Add(Texto(rotulo, "MnLegenda"));
                var v = Texto(valor, "MnCorpoForte");
                v.HorizontalAlignment = HorizontalAlignment.Right;
                topo.Children.Add(v);
                m.Children.Add(topo);
                if (cor is not null) m.Children.Add(BarraFina(Math.Clamp(barra ?? 0, 0, 100), cor));
                Grid.SetColumn(m, 2 + k);
                linha.Children.Add(m);
            }
            pilhaEx.Children.Add(linha);
        }
        if (uso.Count == 0) pilhaEx.Children.Add(Vazio("Aguardando medição."));
        executores.Child = pilhaEx;
        Elevar(executores);
        Adicionar(executores, 16);

        var tabela = Cartao();
        var pilhaTab = new StackPanel();
        pilhaTab.Children.Add(CabecalhoDeSecao("Consumo por etapa", $"{uso.Select(u => u.Etapa).Distinct().Count()} de {etapasMostradas.Length} etapas com registro", null, true));
        var colunas = new[] { 1.2, 1.6, .8, .8, .8, .8, .9, .9 };
        GridLength[] larguras = [.. colunas.Select(c => new GridLength(c, GridUnitType.Star))];
        var cab = LinhaDeTabela(larguras, ["Etapa", "Executor", "Entrada", "Saída", "Cache lido", "Cota 5 h", "Cota semanal", "Tempo"], cabecalho: true);
        pilhaTab.Children.Add(cab);
        foreach (var r in uso)
        {
            var local = r.Executor == UsoDasIas.Programa;
            var linha = LinhaDeTabela(larguras, [FluxoNovoRelatorio.NomeDaEtapa(r.Etapa), NomeExecutor(r), TextoConsumo.Numero(r.Entrada), TextoConsumo.Numero(r.Saida),
                TextoConsumo.Numero(r.CacheLido), Pontos(r.Cota5h), Pontos(r.CotaSemana), r.Duracao is { } d ? FluxoNovoRelatorio.TempoLegivel(d, true) : "—"], local: local);
            var detalhes = string.Join("\n", TextoConsumo.Metricas(r).Select(m => $"{m.Rotulo} · {m.Valor}"));
            pilhaTab.Children.Add(LinhaClicavel(linha, $"{FluxoNovoRelatorio.NomeDaEtapa(r.Etapa)} · {NomeExecutor(r)}", true,
                () => Mostrar($"{FluxoNovoRelatorio.NomeDaEtapa(r.Etapa)} · {NomeExecutor(r)}", detalhes.Length > 0 ? detalhes : "Sem medição.")));
        }
        if (uso.Count == 0) pilhaTab.Children.Add(Vazio("Aguardando medição."));
        tabela.Child = pilhaTab;
        Elevar(tabela);
        Adicionar(tabela, 16);
    }

    Grid LinhaDeTabela(GridLength[] larguras, string[] valores, bool cabecalho = false, bool local = false)
    {
        var linha = new Grid { ColumnSpacing = 12, Padding = new Thickness(20, cabecalho ? 8 : 10, 20, cabecalho ? 8 : 10) };
        if (cabecalho) linha.Style = Estilo<Style>("MnCabecalhoTabela");
        foreach (var l in larguras) linha.ColumnDefinitions.Add(new() { Width = l });
        for (var k = 0; k < valores.Length; k++)
        {
            FrameworkElement celula;
            if (!cabecalho && k == 1)
            {
                var ex = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
                ex.Children.Add(SeloExecutor(local, 24));
                ex.Children.Add(Texto(valores[k], "MnCorpo"));
                celula = ex;
            }
            else
            {
                var t = Texto(cabecalho ? valores[k].ToUpper(CultureInfo.GetCultureInfo("pt-BR")) : valores[k],
                    cabecalho ? "MnLegenda" : k == 0 ? "MnCorpoForte" : "MnCorpo");
                if (cabecalho) { t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold; }
                if (k >= 2) t.HorizontalAlignment = HorizontalAlignment.Right;
                celula = t;
            }
            celula.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(celula, k);
            linha.Children.Add(celula);
        }
        return linha;
    }

    FrameworkElement SeloExecutor(bool local, double tamanho)
    {
        var selo = new Border { Width = tamanho, Height = tamanho, CornerRadius = new CornerRadius(tamanho / 4),
            Background = Estilo<Brush>(local ? "SubtleFillColorSecondaryBrush" : "MnIaFundo"), VerticalAlignment = VerticalAlignment.Center };
        var t = Texto(local ? "PC" : "IA", local ? "MnTagTexto" : "MnTagTextoIa");
        t.FontSize = tamanho > 28 ? 12 : 10;
        selo.Child = t;
        AutomationProperties.SetName(selo, local ? "Programa local" : "Inteligência artificial");
        return selo;
    }

    Border GraficoComposicao(IReadOnlyList<UsoEtapa> ia)
    {
        var cartao = CartaoDeGrafico("Composição dos tokens de entrada", "Quanto veio do cache e quanto foi processado de novo", out var corpo);
        var medidos = TotaisMedidos(ia);
        var soma = (decimal?)medidos.Nova + medidos.Cache;
        var percentual = soma is > 0 ? (double?)(100m * medidos.Cache / soma) : null;
        var grade = new Grid { ColumnSpacing = 20 };
        grade.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grade.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var anel = new Grid { Width = 112, Height = 112 };
        anel.Children.Add(Anel(112, 9, [(percentual ?? 0, "MnArcoVerde")]));
        var centro = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var pct = Id(Texto(percentual is { } v ? $"{v.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"))}%" : "—", "MnValor"), "cachePercent");
        pct.HorizontalAlignment = HorizontalAlignment.Center;
        centro.Children.Add(pct);
        var em = Texto("em cache", "MnLegenda");
        em.HorizontalAlignment = HorizontalAlignment.Center;
        centro.Children.Add(em);
        anel.Children.Add(centro);
        grade.Children.Add(anel);
        var legenda = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        legenda.Children.Add(ItemDeLegenda("MnPontoVerde", "Cache lido", TextoConsumo.Numero(medidos.Cache)));
        legenda.Children.Add(ItemDeLegenda("MnPontoOcre", "Entrada nova", TextoConsumo.Numero(medidos.Nova)));
        legenda.Children.Add(new Border { Style = Estilo<Style>("MnDivisoria") });
        legenda.Children.Add(ItemDeLegenda("MnPontoLilas", "Saída gerada", TextoConsumo.Numero(medidos.Saida)));
        var saidaPct = soma is > 0 ? 100m * medidos.Saida / soma : null;
        legenda.Children.Add(Id(Texto(saidaPct is { } sp ? $"Saída: {sp.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"))}% do volume de entrada" : "Saída informada pela IA", "MnLegenda"), "outputHelp"));
        Grid.SetColumn(legenda, 1);
        grade.Children.Add(legenda);
        corpo.Children.Add(grade);
        return cartao;
    }

    Border GraficoCotas(IReadOnlyList<UsoEtapa> ia)
    {
        var cartao = CartaoDeGrafico("Uso das cotas", "Uso da conta e variação no intervalo observado", out var corpo);
        var curta = ia.LastOrDefault(r => r.Usada5h != null);
        var semana = ia.LastOrDefault(r => r.UsadaSemana != null);
        var leitura = leituraExibida;
        var usadaCurta = leitura is null ? curta?.Usada5h : leitura.Usada5h;
        var usadaSemana = leitura is null ? semana?.UsadaSemana : leitura.UsadaSemana;
        var aneis = new Grid { ColumnSpacing = 12 };
        aneis.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        aneis.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        aneis.Children.Add(AnelDeCota("Cota de 5 h", usadaCurta, curta?.Cota5h, "MnArcoOcre", "MnTagTextoAtencao", "shortPercent", leitura?.CotaObservadaEm));
        var s = AnelDeCota("Cota semanal", usadaSemana, semana?.CotaSemana, "MnArcoLilas", "MnTagTextoIa", "weeklyPercent", leitura?.CotaSemanaObservadaEm);
        Grid.SetColumn(s, 1);
        aneis.Children.Add(s);
        corpo.Children.Add(aneis);
        var legenda = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        var esta = ItemDeLegenda("MnPontoOcre", "Variação da conta", null);
        esta.Children.Insert(1, new Ellipse { Width = 8, Height = 8, Style = Estilo<Style>("MnPontoLilas"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-4, 0, 0, 0) });
        legenda.Children.Add(esta);
        legenda.Children.Add(ItemDeLegenda("MnPontoCinza", "Uso anterior", null));
        legenda.Children.Add(ItemDeLegenda("MnPontoTrilho", "Disponível", null));
        corpo.Children.Add(legenda);
        return cartao;
    }

    StackPanel AnelDeCota(string titulo, double? usada, double? variacao, string arco, string estiloDelta, string id, DateTimeOffset? observado)
    {
        var pilha = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
        var anel = new Grid { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center };
        var total = usada ?? 0;
        var anterior = variacao is { } d ? Math.Max(0, total - Math.Min(d, total)) : total;
        anel.Children.Add(Anel(96, 7, variacao is null ? [(total, "MnArcoCinza")] : [(total, arco), (anterior, "MnArcoCinza")]));
        var centro = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var pct = Id(Texto(usada is { } u ? $"{u.ToString("0.#", CultureInfo.GetCultureInfo("pt-BR"))}%" : "—", "MnValor"), id);
        pct.FontSize = 18;
        pct.HorizontalAlignment = HorizontalAlignment.Center;
        centro.Children.Add(pct);
        var usadaT = Texto(usada.HasValue ? "usada" : leituraExibida?.CotaAplicavel == false ? "inaplicável"
            : id == "shortPercent" && leituraExibida?.SemJanela5h == true ? "cota semanal" : "sem leitura", "MnLegenda");
        usadaT.HorizontalAlignment = HorizontalAlignment.Center;
        centro.Children.Add(usadaT);
        anel.Children.Add(centro);
        pilha.Children.Add(anel);
        var t = Texto(titulo, "MnCorpoForte");
        t.HorizontalAlignment = HorizontalAlignment.Center;
        pilha.Children.Add(t);
        if (observado is { } hora)
            pilha.Children.Add(Texto(hora.ToLocalTime().ToString("dd/MM HH:mm:ss"), "MnLegenda"));
        var delta = Texto(Pontos(variacao) + " no intervalo", variacao is null ? "MnLegenda" : estiloDelta);
        delta.FontWeight = Microsoft.UI.Text.FontWeights.Normal;
        delta.HorizontalAlignment = HorizontalAlignment.Center;
        pilha.Children.Add(delta);
        return pilha;
    }

    Border GraficoTempos(IReadOnlyList<UsoEtapa> uso)
    {
        var cartao = CartaoDeGrafico("Tempo por etapa", $"Onde os {TextoTempoDecorrido.Text} foram gastos", out var corpo);
        legendaTempos = ((StackPanel)((Border)cartao).Child).Children.OfType<TextBlock>().Skip(1).FirstOrDefault();
        var grupos = uso.Where(r => r.Duracao != null).GroupBy(r => FluxoNovoRelatorio.NomeDaEtapa(r.Etapa))
            .Select(g => (Nome: g.Key, Segundos: g.Sum(r => r.Duracao!.Value.TotalSeconds), Lista: g.ToArray())).ToArray();
        var maximo = Math.Max(1, grupos.Select(g => g.Segundos).DefaultIfEmpty(1).Max());
        var pilha = new StackPanel { Spacing = 10 };
        foreach (var g in grupos)
        {
            var item = new StackPanel { Spacing = 4 };
            var topo = new Grid();
            topo.Children.Add(Texto(g.Nome, "MnCorpo"));
            var v = Texto(g.Lista.Length == 1 ? FluxoNovoRelatorio.TempoLegivel(g.Lista[0].Duracao!.Value, true) : FluxoNovoRelatorio.TempoLegivel(TimeSpan.FromSeconds(g.Segundos), true), "MnLegenda");
            v.HorizontalAlignment = HorizontalAlignment.Right;
            topo.Children.Add(v);
            item.Children.Add(topo);
            item.Children.Add(BarraFina(100 * g.Segundos / maximo, "MnFormaVerde"));
            pilha.Children.Add(item);
        }
        if (grupos.Length == 0) pilha.Children.Add(Texto("Aguardando o primeiro registro de duração.", "MnLegenda"));
        corpo.Children.Add(pilha);
        return cartao;
    }

    Border CartaoDeGrafico(string titulo, string legenda, out StackPanel corpo)
    {
        var cartao = Cartao();
        cartao.Padding = new Thickness(16, 14, 16, 16);
        var pilha = new StackPanel { Spacing = 2 };
        pilha.Children.Add(Texto(titulo, "MnTituloSecao"));
        pilha.Children.Add(Texto(legenda, "MnLegenda"));
        corpo = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        pilha.Children.Add(corpo);
        cartao.Child = pilha;
        Elevar(cartao);
        return cartao;
    }

    StackPanel ItemDeLegenda(string ponto, string rotulo, string? valor)
    {
        var item = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        item.Children.Add(new Ellipse { Width = 8, Height = 8, Style = Estilo<Style>(ponto), VerticalAlignment = VerticalAlignment.Center });
        item.Children.Add(Texto(rotulo, valor is null ? "MnLegenda" : "MnCorpo"));
        if (valor is not null)
        {
            var grade = new Grid();
            grade.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            grade.Children.Add(item);
            var v = Texto(valor, "MnCorpoForte");
            Grid.SetColumn(v, 1);
            grade.Children.Add(v);
            var envoltorio = new StackPanel();
            envoltorio.Children.Add(grade);
            return envoltorio;
        }
        return item;
    }

    // ── Log ──

    void PaginaLog()
    {
        Adicionar(TituloDePagina(Glifos.Recentes, "Log de execução", "Registros cronológicos por hora do evento e ações realizadas durante a análise.",
            emCurso ? TagAoVivo("Em execução") : Selo(TituloStatus.Text, "MnTag", "MnTagTexto")), 0);
        Adicionar(CabecalhoDeSecao(atividades.Count == 1 ? "1 evento registrado" : $"{atividades.Count} eventos registrados", HoraUltimoEvento.Text, null, idTitulo: "count"), 18);
        var lista = Cartao();
        Elevar(lista, levantar: false);
        var pilha = new StackPanel();
        var larguras = new[] { new GridLength(90), new GridLength(120), new GridLength(1, GridUnitType.Star) };
        var cab = new Grid { Style = Estilo<Style>("MnCabecalhoTabela"), ColumnSpacing = 16 };
        foreach (var l in larguras) cab.ColumnDefinitions.Add(new() { Width = l });
        string[] titulos = ["HORA", "TIPO", "EVENTO"];
        for (var k = 0; k < 3; k++)
        {
            var t = Texto(titulos[k], "MnLegenda");
            t.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
           
            if (k == 1) t.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(t, k);
            cab.Children.Add(t);
        }
        pilha.Children.Add(cab);
        foreach (var item in atividades)
        {
            var a = item.Atividade;
            var tipo = LinhaAndamento.Classificar(a).Nome;
            var mensagem = string.Join(" — ", new[] { a.Titulo, a.Detalhe }.Where(t => !string.IsNullOrWhiteSpace(t)));
            var linha = new Grid { ColumnSpacing = 16, Padding = new Thickness(20, 9, 20, 9) };
            foreach (var l in larguras) linha.ColumnDefinitions.Add(new() { Width = l });
            linha.Children.Add(Id(Texto(string.IsNullOrEmpty(a.HoraTexto) ? "—" : a.HoraTexto, "MnSecundario"), "time"));
            var (fundo, texto) = tipo switch
            {
                "IA" => ("MnTagIa", "MnTagTextoIa"),
                "CONCLUÍDO" => ("MnTagSucesso", "MnTagTextoSucesso"),
                "ERRO" => ("MnTagErro", "MnTagTextoErro"),
                "ATENÇÃO" => ("MnTagAtencao", "MnTagTextoAtencao"),
                _ => ("MnTag", "MnTagTexto"),
            };
            var tag = Selo(tipo, fundo, texto);
            tag.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(tag, 1);
            linha.Children.Add(tag);
            var msg = Texto(mensagem, "MnCorpo");
            Grid.SetColumn(msg, 2);
            linha.Children.Add(msg);
            FrameworkElement conteudo = linha;
            if (tipo == "ERRO")
            {
                var comFaixa = new Grid();
                comFaixa.Children.Add(linha);
                comFaixa.Children.Add(new Rectangle { Width = 3, HorizontalAlignment = HorizontalAlignment.Left, Style = Estilo<Style>("MnFormaErro") });
                conteudo = comFaixa;
            }
            pilha.Children.Add(LinhaClicavel(conteudo, mensagem, true, () => Mostrar((string.IsNullOrEmpty(a.HoraTexto) ? "—" : a.HoraTexto) + " · " + tipo, mensagem)));
        }
        if (atividades.Count == 0) pilha.Children.Add(Vazio("Aguardando registros de execução."));
        lista.Child = pilha;
        Adicionar(lista, 10);
        if (BotaoEvidenciasPdf.Visibility == Visibility.Visible)
            Adicionar(BotaoAcao(Glifos.Relatorio, "Ver evidências por PDF e página", () => EvidenciasSolicitadas?.Invoke(this, EventArgs.Empty)), 12);
    }

    // ───────────────────────────── peças comuns ─────────────────────────────

    void Adicionar(FrameworkElement elemento, double acima)
    {
        elemento.Margin = new Thickness(elemento.Margin.Left, acima, elemento.Margin.Right, elemento.Margin.Bottom);
        ConteudoMonitor.Children.Add(elemento);
    }

    T Estilo<T>(string chave) where T : class
    {
        var tema = AltoContraste ? "HighContrast" : ActualTheme == ElementTheme.Dark ? "Default" : "Light";
        foreach (var d in new[] { Resources, Application.Current.Resources })
            if (NoTema(d, tema, chave) is T porTema) return porTema;
        if (Resources.TryGetValue(chave, out var local) && local is T t) return t;
        foreach (var m in Resources.MergedDictionaries)
            if (m.TryGetValue(chave, out var v) && v is T achado) return achado;
        return (T)Application.Current.Resources[chave];
    }

    static object? NoTema(ResourceDictionary d, string tema, string chave)
    {
        if (d.ThemeDictionaries.TryGetValue(tema, out var td) && td is ResourceDictionary rd && rd.TryGetValue(chave, out var v)) return v;
        foreach (var m in d.MergedDictionaries)
            if (NoTema(m, tema, chave) is { } achado) return achado;
        return null;
    }

    static bool AltoContraste => new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;

    TextBlock Texto(string texto, string estilo) => new() { Text = texto, Style = Estilo<Style>(estilo) };

    static void AtualizarTexto(TextBlock campo, string texto)
    {
        if (campo.Text != texto) campo.Text = texto;
    }

    FontIcon Glifo(string glifo, string estilo, double tamanho)
    {
        var f = new FontIcon { Glyph = glifo, Style = Estilo<Style>(estilo), FontSize = tamanho, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAccessibilityView(f, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return f;
    }

    FrameworkElement IconeEmLadrilho(string glifo, string estilo, double tamanho)
    {
        var b = new Border { Style = Estilo<Style>(estilo), Width = tamanho, Height = tamanho, VerticalAlignment = VerticalAlignment.Center };
        b.Child = Glifo(glifo, "MnGlifo", tamanho * .47);
        return b;
    }

    Border Cartao() => new() { Style = Estilo<Style>("MnCartao") };

    Border Selo(string texto, string estiloFundo, string estiloTexto)
    {
        var b = new Border { Style = Estilo<Style>(estiloFundo) };
        b.Child = Texto(texto, estiloTexto);
        AutomationProperties.SetName(b, texto);
        return b;
    }

    /// <summary>Tag "ao vivo" (verde sólido, texto branco) com uma onda de radar saindo da borda enquanto a análise roda.</summary>
    FrameworkElement TagAoVivo(string texto)
    {
        var tag = new Border { Style = Estilo<Style>("MnTagAtiva") };
        var conteudo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        conteudo.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = Estilo<Brush>("TextOnAccentFillColorPrimaryBrush"), VerticalAlignment = VerticalAlignment.Center });
        conteudo.Children.Add(Texto(texto, "MnTagTextoAtiva"));
        tag.Child = conteudo;
        AutomationProperties.SetName(tag, texto);
        if (!animacoes) return tag;
        var onda = new Border { Style = Estilo<Style>("MnTagAtiva"), IsHitTestVisible = false };
        var grade = new Grid { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        grade.Children.Add(Radar(onda, 1.25, 1.7));
        grade.Children.Add(tag);
        return grade;
    }

    /// <summary>Onda de radar: o elemento cresce e some, sem parar (respeita "Efeitos de animação" do Windows).</summary>
    static FrameworkElement Radar(FrameworkElement onda, double escalaX, double? escalaY = null)
    {
        var escala = new ScaleTransform();
        onda.RenderTransform = escala;
        onda.RenderTransformOrigin = new Point(0.5, 0.5);
        onda.IsHitTestVisible = false;
        onda.Opacity = 0;
        var sb = new Storyboard { RepeatBehavior = RepeatBehavior.Forever };
        void Animar(DependencyObject alvo, string prop, double de, double para)
        {
            var a = new DoubleAnimation { From = de, To = para, Duration = TimeSpan.FromSeconds(1.8), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            Storyboard.SetTarget(a, alvo);
            Storyboard.SetTargetProperty(a, prop);
            sb.Children.Add(a);
        }
        Animar(escala, "ScaleX", 1, escalaX);
        Animar(escala, "ScaleY", 1, escalaY ?? escalaX);
        Animar(onda, "Opacity", 0.55, 0);
        onda.Loaded += (_, _) => sb.Begin();
        onda.Unloaded += (_, _) => sb.Stop();
        return onda;
    }

    /// <summary>Elevação ao passar o mouse: o cartão sobe 2 px e a sombra (ThemeShadow) cresce, como nos ladrilhos de tempo aprovados.</summary>
    void Elevar(FrameworkElement elemento, bool comSombra = true, bool levantar = true)
    {
        // A profundidade (Z) é o que faz a ThemeShadow aparecer: 16 em repouso já dá a sombra suave de cartão; 40 ao passar o mouse.
        elemento.TranslationTransition = new Vector3Transition { Duration = TimeSpan.FromMilliseconds(180) };
        if (comSombra)
        {
            elemento.Shadow = sombraCartoes;
            elemento.Translation = new Vector3(0, 0, 16);
        }
        if (!levantar) return;
        var repouso = elemento.Translation;
        elemento.PointerEntered += (_, _) => { if (animacoes) elemento.Translation = new Vector3(0, -2, comSombra ? 40 : 0); };
        elemento.PointerExited += (_, _) => elemento.Translation = repouso;
        elemento.PointerCanceled += (_, _) => elemento.Translation = repouso;
    }

    Grid TituloDePagina(string glifo, string titulo, string subtitulo, FrameworkElement? direita = null)
    {
        var grade = new Grid { ColumnSpacing = 12 };
        grade.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grade.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grade.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grade.RowDefinitions.Add(new() { Height = GridLength.Auto });
        grade.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var icone = IconeEmLadrilho(glifo, "MnIconeTile", 34);
        grade.Children.Add(icone);
        var t = Texto(titulo, "MnTituloPagina");
        t.VerticalAlignment = VerticalAlignment.Center;
        AutomationProperties.SetHeadingLevel(t, AutomationHeadingLevel.Level2);
        Grid.SetColumn(t, 1);
        grade.Children.Add(t);
        var sub = Texto(subtitulo, "MnSecundario");
        sub.FontSize = 13;
        Grid.SetColumn(sub, 1);
        Grid.SetRow(sub, 1);
        grade.Children.Add(sub);
        if (direita is not null)
        {
            direita.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(direita, 2);
            Grid.SetRowSpan(direita, 2);
            grade.Children.Add(direita);
        }
        return grade;
    }

    Grid CabecalhoDeSecao(string titulo, string? legenda, FrameworkElement? acao, bool dentroDoCartao = false, string? idLegenda = null, string? idTitulo = null)
    {
        var grade = new Grid { ColumnSpacing = 12, MinHeight = 32 };
        if (dentroDoCartao) grade.Padding = new Thickness(20, 14, 20, 10);
        grade.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grade.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grade.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        var t = Texto(titulo, "MnTituloSecao");
        t.VerticalAlignment = VerticalAlignment.Center;
        if (idTitulo is not null) Id(t, idTitulo);
        grade.Children.Add(t);
        if (!string.IsNullOrWhiteSpace(legenda))
        {
            var l = Texto(legenda, "MnSecundario");
            if (idLegenda is not null) Id(l, idLegenda);
            l.FontSize = 13;
            l.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(l, 1);
            grade.Children.Add(l);
        }
        if (acao is not null)
        {
            acao.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(acao, 2);
            grade.Children.Add(acao);
        }
        return grade;
    }

    static StackPanel BotaoConteudo(string glifo, string texto)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        p.Children.Add(new FontIcon { Glyph = glifo, FontSize = 14 });
        p.Children.Add(new TextBlock { Text = texto });
        return p;
    }

    Button BotaoAcao(string glifo, string texto, Action acao)
    {
        var b = new Button { Content = BotaoConteudo(glifo, texto), HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(b, texto);
        b.Click += (_, _) => acao();
        return b;
    }

    Button LinhaClicavel(FrameworkElement conteudo, string nome, bool divisoria, Action acao)
    {
        var b = new Button { Style = Estilo<Style>("MnBotaoLinha"), Content = conteudo };
        if (divisoria)
        {
            b.BorderThickness = new Thickness(0, 1, 0, 0);
            b.BorderBrush = Estilo<Brush>("DividerStrokeColorDefaultBrush");
            // O modelo da linha não desenha borda: a divisória vai no conteúdo.
            if (conteudo is Grid grade) { grade.BorderThickness = new Thickness(0, 1, 0, 0); grade.BorderBrush = b.BorderBrush; }
            else if (conteudo is StackPanel pilha) { pilha.BorderThickness = new Thickness(0, 1, 0, 0); pilha.BorderBrush = b.BorderBrush; }
            else if (conteudo is Border borda) { borda.BorderThickness = new Thickness(0, 1, 0, 0); borda.BorderBrush = b.BorderBrush; }
        }
        AutomationProperties.SetName(b, nome);
        b.Click += (_, _) => acao();
        return b;
    }

    TextBlock Vazio(string texto)
    {
        var t = Texto(texto, "MnSecundario");
        t.Margin = new Thickness(20, 16, 20, 16);
        return t;
    }

    Grid BarraFina(double percentual, string estilo)
    {
        var grade = new Grid { Height = 4 };
        grade.ColumnDefinitions.Add(new());
        grade.ColumnDefinitions.Add(new());
        AtualizarBarraFina(grade, percentual);
        var fundo = new Rectangle { Style = Estilo<Style>("MnFormaTrilho"), RadiusX = 2, RadiusY = 2 };
        Grid.SetColumnSpan(fundo, 2);
        grade.Children.Add(fundo);
        grade.Children.Add(new Rectangle { Style = Estilo<Style>(estilo), RadiusX = 2, RadiusY = 2 });
        return grade;
    }

    static void AtualizarBarraFina(Grid grade, double percentual)
    {
        var valor = Math.Clamp(percentual, 0, 100);
        grade.ColumnDefinitions[0].Width = new GridLength(valor, GridUnitType.Star);
        grade.ColumnDefinitions[1].Width = new GridLength(100 - valor, GridUnitType.Star);
    }

    /// <summary>Sombra suave (DropShadow da composição) com o contorno da trilha do anel, deslocada para baixo.</summary>
    static FrameworkElement SombraDoAnel(Grid aro, double tamanho)
    {
        var hospede = new Border { Width = tamanho, Height = tamanho, IsHitTestVisible = false };
        if (aro.Children.FirstOrDefault() is not Shape trilha) return hospede;
        trilha.Loaded += (_, _) =>
        {
            var compositor = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(hospede).Compositor;
            var sombra = compositor.CreateDropShadow();
            sombra.Mask = trilha.GetAlphaMask();
            sombra.BlurRadius = 9;
            sombra.Offset = new Vector3(0, 4, 0);
            sombra.Color = Windows.UI.Color.FromArgb(70, 0, 0, 0);
            var visual = compositor.CreateSpriteVisual();
            // A máscara tem o tamanho da própria trilha (o anel menos a margem da espessura).
            visual.Size = new Vector2((float)trilha.ActualWidth, (float)trilha.ActualHeight);
            visual.Offset = new Vector3((float)trilha.Margin.Left, (float)trilha.Margin.Top, 0);
            visual.Shadow = sombra;
            Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.SetElementChildVisual(hospede, visual);
        };
        return hospede;
    }

    /// <summary>Anel de progresso: a trilha e, por cima, cada fatia (valor 0–100) no estilo de arco indicado, começando no topo.</summary>
    Grid Anel(double tamanho, double espessura, (double Valor, string Estilo)[] fatias)
    {
        var grade = new Grid { Width = tamanho, Height = tamanho };
        grade.Children.Add(new Ellipse { Stroke = Estilo<Brush>("MnTrilho"), StrokeThickness = espessura, Margin = new Thickness(espessura / 2) });
        AtualizarArcos(grade, tamanho, espessura, fatias);
        AutomationProperties.SetAccessibilityView(grade, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        return grade;
    }

    void AtualizarArcos(Grid grade, double tamanho, double espessura, (double Valor, string Estilo)[] fatias)
    {
        while (grade.Children.Count > 1) grade.Children.RemoveAt(grade.Children.Count - 1);
        foreach (var (valor, estilo) in fatias)
        {
            var v = Math.Clamp(valor, 0, 100);
            if (v <= 0) continue;
            var r = (tamanho - espessura) / 2;
            var c = tamanho / 2;
            var figura = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
            if (v >= 100)
            {
                figura.Segments.Add(new ArcSegment { Point = new Point(c, c + r), Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise });
                figura.Segments.Add(new ArcSegment { Point = new Point(c, c - r), Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise });
            }
            else
            {
                var angulo = v / 100 * 2 * Math.PI;
                var fim = new Point(c + r * Math.Sin(angulo), c - r * Math.Cos(angulo));
                figura.Segments.Add(new ArcSegment { Point = fim, Size = new Size(r, r), IsLargeArc = v > 50, SweepDirection = SweepDirection.Clockwise });
            }
            grade.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Style = Estilo<Style>(estilo), StrokeThickness = espessura,
                Data = new PathGeometry { Figures = { figura } },
            });
        }
    }

    /// <summary>Identifica um campo para o roteiro e para o Narrador (o mesmo nome do antigo data-field do monitor HTML).</summary>
    static T Id<T>(T elemento, string id) where T : FrameworkElement
    {
        AutomationProperties.SetAutomationId(elemento, id);
        return elemento;
    }

    internal string? TituloDoDialogo { get; private set; }
    internal string? TextoDoDialogo { get; private set; }
    internal void FecharDialogo() => dialogoAberto?.Hide();
    internal void FiltrarAchados(string? classificacao) { filtroAchados = classificacao; todasEvidencias = false; AtualizarMonitor(true); }

    async void Mostrar(string titulo, string texto)
    {
        if (dialogoAberto is not null || XamlRoot is null) return;
        TituloDoDialogo = titulo;
        TextoDoDialogo = texto;
        var corpo = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        dialogoAberto = new ContentDialog
        {
            Title = titulo, CloseButtonText = "Fechar", DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
            Content = new ScrollViewer { Content = corpo, MaxHeight = 420 }, RequestedTheme = ActualTheme,
        };
        try { await dialogoAberto.ShowAsync(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        finally { dialogoAberto = null; TituloDoDialogo = TextoDoDialogo = null; }
    }

    /// <summary>"EDESIO MINET ZAVARIZE" → "Edesio Minet Zavarize" (preposições em minúsculas), como no monitor aprovado.</summary>
    internal static string NomeProprio(string? nome)
    {
        if (string.IsNullOrWhiteSpace(nome) || nome == "Não informado") return "Não informado";
        var cultura = CultureInfo.GetCultureInfo("pt-BR");
        var palavras = nome.Trim().ToLower(cultura).Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select((p, i) => i > 0 && p is "de" or "da" or "do" or "das" or "dos" or "e" ? p : cultura.TextInfo.ToTitleCase(p));
        return string.Join(' ', palavras);
    }

    /// <summary>Modo de demonstração (--captura): PNG da página do monitor desenhada.</summary>
    internal async Task CapturarMonitorAsync(string pasta, string pagina)
    {
        var limite = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < limite && AutomationProperties.GetName(ConteudoMonitor) != pagina) await Task.Delay(100);
        await Roteiro.CapturarXamlAsync(MonitorNativo, System.IO.Path.Combine(pasta, $"nativo-{pagina}.png"));
    }
}
