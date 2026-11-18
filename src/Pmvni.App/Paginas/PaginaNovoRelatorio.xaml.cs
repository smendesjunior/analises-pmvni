using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

/// <summary>Página Novo relatório (desenho §12.2): só a parte visual. Quem preenche, decide e inicia o agente é a janela (<c>MainWindow.NovoRelatorio.cs</c>)
/// com a <see cref="SessaoNovoRelatorio"/>.</summary>
public sealed partial class PaginaNovoRelatorio : UserControl
{
    ScrollViewer? rolagemDasLinhas;

    public PaginaNovoRelatorio()
    {
        InitializeComponent();
        CaixaPedido.PlaceholderText = FluxoNovoRelatorio.DicaPedido;
        TextoPedidoClaude.Text = FluxoNovoRelatorio.TextoPedidoClaude;
        TextoArraste.Text = FluxoNovoRelatorio.ArrasteAqui;
        // Correções 2026-11-15 (image7): a coluna do formulário tem a largura da área visível, menos as margens, até 1080 (centralizada).
        PainelFormulario.SizeChanged += (_, e) => AjustarLarguraDoFormulario(e.NewSize.Width);
        PainelExecucao.SizeChanged += (_, e) => AjustarColunasExecucao(e.NewSize.Width);
    }

    /// <summary>Largura da coluna do formulário: a da área visível menos 24 de cada lado, entre 280 e 1080.</summary>
    internal static double LarguraDoFormulario(double larguraVisivel) => Math.Clamp(larguraVisivel - 48, 280, 1080);

    void AjustarLarguraDoFormulario(double larguraVisivel)
    {
        if (double.IsNaN(larguraVisivel) || larguraVisivel <= 0) return;
        var nova = Math.Round(LarguraDoFormulario(larguraVisivel));
        if (double.IsNaN(ColunaFormulario.Width) || Math.Abs(ColunaFormulario.Width - nova) >= 1) ColunaFormulario.Width = nova; // sem realimentar o layout por frações de pixel

    }

    /// <summary>Formulário (false) ou vista da execução (true).</summary>
    internal void MostrarExecucao(bool execucao)
    {
        PainelFormulario.Visibility = execucao ? Visibility.Collapsed : Visibility.Visible;
        PainelExecucao.Visibility = execucao ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool VistaDaExecucao => PainelExecucao.Visibility == Visibility.Visible;

    /// <summary>A lista do andamento está no fim (ou ainda não rola): as linhas novas a mantêm no fim.</summary>
    internal bool LinhasNoFim()
    {
        rolagemDasLinhas ??= Achar<ScrollViewer>(ListaLinhas);
        return rolagemDasLinhas is null || rolagemDasLinhas.VerticalOffset >= rolagemDasLinhas.ScrollableHeight - 4;
    }

    internal void RolarLinhasAoFim()
    {
        if (ListaLinhas.Items.Count > 0) ListaLinhas.ScrollIntoView(ListaLinhas.Items[^1]);
    }

    static T? Achar<T>(DependencyObject raiz) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(raiz); i++)
        {
            var filho = VisualTreeHelper.GetChild(raiz, i);
            if (filho is T t) return t;
            if (Achar<T>(filho) is { } achado) return achado;
        }
        return null;
    }

    // ---------- redesenho 2026-11-17: atividades, etapas, descobertas e resumo ----------

    readonly System.Collections.ObjectModel.ObservableCollection<ItemAtividade> atividades = [];
    System.Collections.ObjectModel.ObservableCollection<string>? origem;
    bool emCurso = true;
    (string Nome, EstadoEtapa Estado)[] etapasMostradas = [];
    bool animandoEtapas;

    /// <summary>Liga a lista de atividades às linhas da execução (null = desliga). As linhas que já existiam entram sem horário;
    /// as que chegam depois, com o horário da chegada.</summary>
    internal void LigarLinhas(System.Collections.ObjectModel.ObservableCollection<string>? linhas)
    {
        if (origem is not null) origem.CollectionChanged -= LinhasMudaram;
        origem = linhas;
        atividades.Clear();
        if (linhas is not null)
        {
            foreach (var l in linhas) atividades.Add(new ItemAtividade(Atividades.DaLinha(l)));
            linhas.CollectionChanged += LinhasMudaram;
        }
        ListaLinhas.ItemsSource = linhas is null ? null : atividades;
        MarcarEmCurso();
    }

    void LinhasMudaram(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) Espelhar(e); else DispatcherQueue.TryEnqueue(() => Espelhar(e));
    }

    void Espelhar(System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (origem is null) return;
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems is { } novos && e.NewStartingIndex == atividades.Count)
            foreach (string l in novos) atividades.Add(new ItemAtividade(Atividades.DaLinha(l, DateTime.Now)));
        else
        {
            // Outra mudança (inserção no meio, remoção, limpeza): refaz sem horários.
            atividades.Clear();
            foreach (var l in origem) atividades.Add(new ItemAtividade(Atividades.DaLinha(l)));
        }
        MarcarEmCurso();
    }

    void MarcarEmCurso()
    {
        for (var i = 0; i < atividades.Count; i++) atividades[i].EmCurso = emCurso && i == atividades.Count - 1;
        TextoContagem.Text = atividades.Count == 1 ? "1 atividade registrada" : $"{atividades.Count} atividades registradas";
    }

    /// <summary>Resumo do topo, selo "ao vivo", descobertas e próxima etapa. <paramref name="etapas"/> vazio = provedor sem etapas (ChatGPT).</summary>
    internal void MostrarResumo(bool rodando, string? erro, IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas, IEnumerable<string> linhas)
    {
        emCurso = rodando;
        MarcarEmCurso();
        var falhou = !string.IsNullOrWhiteSpace(erro);
        var concluida = etapas.Count > 0 && etapas.All(x => x.Estado == EstadoEtapa.Feita);
        var atual = etapas.Select((x, i) => (x, i)).FirstOrDefault(t => t.x.Estado is EstadoEtapa.Agora or EstadoEtapa.Erro);
        var temAtual = etapas.Any(x => x.Estado is EstadoEtapa.Agora or EstadoEtapa.Erro);
        var feitas = etapas.Count(x => x.Estado == EstadoEtapa.Feita);

        TituloStatus.Text = falhou ? "Análise com problema" : concluida ? "Análise concluída" : rodando ? "Análise em andamento" : "Análise parada";
        IconeStatus.Glyph = falhou ? "" : concluida ? "" : "";
        FundoIconeStatus.Background = Recurso(falhou ? "SystemFillColorCriticalBrush" : concluida ? "SystemFillColorSuccessBrush" : "AccentFillColorDefaultBrush");
        PontoEtapa.Fill = Recurso(falhou ? "SystemFillColorCriticalBrush" : concluida ? "SystemFillColorSuccessBrush" : "AccentFillColorDefaultBrush");
        TextoEtapaAtual.Text = etapas.Count == 0 ? (rodando ? "Etapa atual: análise documental" : "Execução encerrada")
            : concluida ? $"{etapas.Count} de {etapas.Count} etapas concluídas"
            : temAtual ? $"Etapa atual: {atual.x.Nome} · {feitas} de {etapas.Count} concluídas"
            : $"Preparando · {feitas} de {etapas.Count} concluídas";
        SeloAoVivo.Visibility = rodando && !falhou ? Visibility.Visible : Visibility.Collapsed;
        BotaoCancelarAnalise.Visibility = rodando ? Visibility.Visible : Visibility.Collapsed;

        MostrarEtapas(etapas, rodando && !falhou);

        // Descobertas: o que a IA relatou; verificadas depois da auditoria cruzada (as cinco primeiras etapas feitas).
        var lista = linhas.ToList();
        var verificadas = etapas.Count >= 5 && etapas.Take(5).All(x => x.Estado == EstadoEtapa.Feita);
        var descobertas = Atividades.Descobertas(lista, verificadas);
        ListaDescobertas.Children.Clear();
        foreach (var d in descobertas) ListaDescobertas.Children.Add(CartaoDescoberta(d));
        TextoSemDescobertas.Visibility = descobertas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var proxima = temAtual && atual.i + 1 < etapas.Count ? etapas[atual.i + 1].Nome : null;
        TextoResumo.Text = falhou ? "A execução encontrou um problema." : concluida ? "Relatório pronto para importar." : rodando ? "Projeto em análise." : "A análise não está mais rodando.";
        TextoProximaEtapa.Text = falhou ? erro!.Trim()
            : concluida ? "Todas as etapas terminaram."
            : proxima is not null ? $"Próxima etapa: {proxima}."
            : etapas.Count == 0 ? "As atividades aparecem à esquerda conforme a IA trabalha."
            : "Última etapa em andamento.";
        CartaoProximaEtapa.BorderBrush = Recurso(falhou ? "SystemFillColorCriticalBrush" : "AccentFillColorDefaultBrush");
    }

    static Border CartaoDescoberta(DescobertaAnalise d)
    {
        var selo = new Border
        {
            CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 2, 8, 2), VerticalAlignment = VerticalAlignment.Top,
            Background = Recurso(d.Verificada ? "SystemFillColorSuccessBackgroundBrush" : "SystemFillColorCautionBackgroundBrush"),
            Child = new TextBlock { Text = d.Verificada ? "Verificada" : "Preliminar", FontSize = 11 },
        };
        var texto = new TextBlock { Text = d.Texto, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        var icone = new Border
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(6), VerticalAlignment = VerticalAlignment.Top, Background = Recurso("ControlFillColorDefaultBrush"),
            Child = new FontIcon { Glyph = d.Verificada ? "" : "", FontSize = 16, Foreground = Recurso(d.Verificada ? "SystemFillColorSuccessBrush" : "SystemFillColorCautionBrush") },
        };
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(texto, 1);
        Grid.SetColumn(selo, 2);
        g.Children.Add(icone); g.Children.Add(texto); g.Children.Add(selo);
        return new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 8, 10, 8), Background = Recurso("SubtleFillColorSecondaryBrush"), Child = g };
    }

    static readonly string[] GlifosEtapas = ["", "", "", "", "", ""];

    /// <summary>As seis etapas em linha: concluída (verde ✓), em andamento (acento ▶), erro (vermelho !) ou pendente (cinza, ícone da etapa),
    /// ligadas por um traço cheio entre as concluídas e tracejado nas pendentes.</summary>
    internal void MostrarEtapas(IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas, bool animar)
    {
        var podeAnimar = animar && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        if (podeAnimar == animandoEtapas && etapasMostradas.SequenceEqual(etapas)) return;
        etapasMostradas = etapas.ToArray();
        animandoEtapas = podeAnimar;
        PainelEtapas.Children.Clear();
        PainelEtapas.ColumnDefinitions.Clear();
        for (var i = 0; i < etapas.Count; i++)
        {
            PainelEtapas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var (nome, estado) = etapas[i];
            if (i < etapas.Count - 1)
            {
                var seguinteFeita = estado == EstadoEtapa.Feita;
                var linha = new Microsoft.UI.Xaml.Shapes.Line
                {
                    X1 = 0, X2 = 1, Stretch = Stretch.Fill, StrokeThickness = 2, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 17, 0, 0),
                    Stroke = Recurso(seguinteFeita ? "SystemFillColorSuccessBrush" : "ControlStrongStrokeColorDefaultBrush"),
                    StrokeDashArray = seguinteFeita ? null : [3, 3],
                };
                // Do centro desta coluna ao centro da próxima: ocupa a metade direita desta e a esquerda da próxima.
                // (largura de 2 colunas: cada centro fica a 1/4 da borda; 22 = raio do círculo + folga)
                var faixa = new Grid();
                faixa.Children.Add(linha);
                ProgressBar? progresso = null;
                if (estado == EstadoEtapa.Agora && podeAnimar)
                {
                    progresso = new ProgressBar
                    {
                        IsIndeterminate = true, Height = 3, VerticalAlignment = VerticalAlignment.Top,
                    };
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(progresso, "Progresso indeterminado de " + nome);
                    faixa.Children.Add(progresso);
                }
                Grid.SetColumn(faixa, i);
                Grid.SetColumnSpan(faixa, 2);
                faixa.SizeChanged += (_, e) =>
                {
                    var w = e.NewSize.Width;
                    var margem = new Thickness(w / 4 + 22, 17, w / 4 + 22, 0);
                    linha.Margin = margem;
                    if (progresso is not null) progresso.Margin = margem;
                };
                PainelEtapas.Children.Add(faixa);
            }

            var (fundo, glifo, frente) = estado switch
            {
                EstadoEtapa.Feita => ("SystemFillColorSuccessBrush", "", "TextOnAccentFillColorPrimaryBrush"),
                EstadoEtapa.Agora => ("AccentFillColorDefaultBrush", "", "TextOnAccentFillColorPrimaryBrush"),
                EstadoEtapa.Erro => ("SystemFillColorCriticalBrush", "", "TextOnAccentFillColorPrimaryBrush"),
                _ => ("ControlFillColorDefaultBrush", GlifosEtapas[Math.Min(i, GlifosEtapas.Length - 1)], "TextFillColorSecondaryBrush"),
            };
            var circulo = new Border
            {
                Width = 36, Height = 36, CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Center, Background = Recurso(fundo),
                BorderThickness = new Thickness(estado == EstadoEtapa.Pendente ? 1 : 0), BorderBrush = Recurso("ControlStrongStrokeColorDefaultBrush"),
                Child = new FontIcon { Glyph = glifo, FontSize = 15, Foreground = Recurso(frente) },
            };
            var situacao = estado switch { EstadoEtapa.Feita => "Concluída · 100%", EstadoEtapa.Agora => animar ? "Em andamento" : "Interrompida", EstadoEtapa.Erro => "Com erro", _ => "Pendente" };
            var bloco = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
            bloco.Children.Add(circulo);
            bloco.Children.Add(new TextBlock
            {
                Text = nome, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
                FontWeight = estado == EstadoEtapa.Pendente ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold,
            });
            bloco.Children.Add(new TextBlock
            {
                Text = situacao, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Recurso(estado switch { EstadoEtapa.Agora => "AccentTextFillColorPrimaryBrush", EstadoEtapa.Erro => "SystemFillColorCriticalBrush", _ => "TextFillColorSecondaryBrush" }),
            });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bloco, $"{nome}: {situacao}");
            Grid.SetColumn(bloco, i);
            PainelEtapas.Children.Add(bloco);
        }
    }

    /// <summary>Abaixo de 1000 px, a coluna lateral desce para baixo das atividades.</summary>
    internal void AjustarColunasExecucao(double largura)
    {
        var estreita = largura < 1000;
        GradeConteudo.ColumnDefinitions[1].Width = estreita ? new GridLength(0) : new GridLength(10, GridUnitType.Star);
        GradeConteudo.ColumnSpacing = estreita ? 0 : 12;
        GradeConteudo.RowDefinitions[1].Height = estreita ? GridLength.Auto : new GridLength(0);
        Grid.SetColumn(ColunaLateral, estreita ? 0 : 1);
        Grid.SetRow(ColunaLateral, estreita ? 1 : 0);
        CartaoAtividades.MinHeight = estreita ? 320 : 0;
        BotoesExecucao.Orientation = largura < 760 ? Orientation.Vertical : Orientation.Horizontal;
    }

    static Brush Recurso(string chave) => (Brush)Application.Current.Resources[chave];
}
