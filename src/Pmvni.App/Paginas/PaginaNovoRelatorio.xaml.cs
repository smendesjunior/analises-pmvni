using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

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
        // Correções 2026-11-15 (image7): a coluna do formulário tem a largura da área visível, menos as margens, até 1400 (centralizada).
        PainelFormulario.SizeChanged += (_, e) => AjustarLarguraDoFormulario(e.NewSize.Width);
        PainelExecucao.SizeChanged += (_, e) => AjustarColunasExecucao(e.NewSize.Width);
    }

    /// <summary>Largura da coluna do formulário: a da área visível menos 24 de cada lado, entre 280 e 1400.</summary>
    internal static double LarguraDoFormulario(double larguraVisivel) => Math.Clamp(larguraVisivel - 48, 280, 1400);

    void AjustarLarguraDoFormulario(double larguraVisivel)
    {
        if (double.IsNaN(larguraVisivel) || larguraVisivel <= 0) return;
        var nova = Math.Round(LarguraDoFormulario(larguraVisivel));
        bool ampla = nova >= 940;
        GradeEtapas.ColumnSpacing = ampla ? 16 : 0;
        GradeEtapas.ColumnDefinitions[1].Width = ampla ? new GridLength(nova * .38) : new GridLength(0);
        Grid.SetRow(CartaoEscolhaAgente, ampla ? 0 : 1);
        Grid.SetColumn(CartaoEscolhaAgente, ampla ? 1 : 0);
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
        TextoEtapaAtual.Text = etapas.Count == 0 ? (rodando ? "Etapa atual: análise documental" : "Execução encerrada")
            : concluida ? $"{etapas.Count} de {etapas.Count} etapas concluídas"
            : temAtual ? $"Etapa atual: {atual.x.Nome} · {feitas} de {etapas.Count} concluídas"
            : falhou ? $"Interrompida · {feitas} de {etapas.Count} concluídas"
            : !rodando ? $"Parada · {feitas} de {etapas.Count} concluídas"
            : $"Preparando · {feitas} de {etapas.Count} concluídas";
        SeloAoVivo.Visibility = rodando && !falhou ? Visibility.Visible : Visibility.Collapsed;
        BotaoPausarAnalise.Visibility = BotaoCancelarAnalise.Visibility = rodando ? Visibility.Visible : Visibility.Collapsed;

        MostrarEtapas(etapas, rodando && !falhou);

        var proxima = temAtual && atual.i + 1 < etapas.Count ? etapas[atual.i + 1].Nome : null;
        TextoResumo.Text = falhou ? "A execução encontrou um problema." : concluida ? "Relatório pronto para importar." : rodando ? (temAtual ? atual.x.Nome : "Aguardando registro da etapa.") : "A análise não está mais rodando.";
        TextoProximaEtapa.Text = falhou ? erro!.Trim()
            : concluida ? "Todas as etapas terminaram."
            : !rodando ? "Execução encerrada."
            : !temAtual ? "Aguardando confirmação de progresso."
            : proxima is not null ? $"Próxima etapa: {proxima}."
            : "Última etapa em andamento.";
        CartaoProximaEtapa.BorderBrush = Recurso(falhou ? "SystemFillColorCriticalBrush" : "AccentFillColorDefaultBrush");
    }

    internal ProgressoEtapa? ProgressoAtual { get; private set; }
    private ProgressoEtapa? progressoDesenhado;
    internal EstimativaAnalise? EstimativaAtual { get; private set; }
    private EstimativaAnalise? estimativaDesenhada;
    private bool execucaoAtivaNaBarra;
    private (double Percentual, TimeSpan DuracaoAtiva)? previsaoPeloRitmo;

    internal void ReiniciarEstimativa()
    {
        previsaoPeloRitmo = null;
        MostrarEstimativa(null);
    }

    internal void MostrarEstimativa(EstimativaAnalise? estimativa)
    {
        if (EstimativaAtual == estimativa) return;
        EstimativaAtual = estimativa;
        MostrarEtapas(etapasMostradas, execucaoAtivaNaBarra);
    }

    internal void MostrarRelogios(TimeSpan decorrido, DateTimeOffset agora)
    {
        TextoTempoDecorrido.Text = "Decorrido · " + FluxoNovoRelatorio.TempoLegivel(decorrido, incluirSegundos: true);
        TimeSpan? restantePeloRitmo = null;
        if (execucaoAtivaNaBarra && decorrido > TimeSpan.Zero &&
            EstimativaAtual?.PercentualGeral is double percentual and >= 1 and < 100)
        {
            // Usa o tempo ativo acumulado e mantém a previsão até o progresso mudar.
            var duracaoEmSegundos = decorrido.TotalSeconds * 100 / percentual;
            if (previsaoPeloRitmo?.Percentual != percentual && duracaoEmSegundos < (DateTimeOffset.MaxValue - agora).TotalSeconds)
                previsaoPeloRitmo = (percentual, TimeSpan.FromSeconds(duracaoEmSegundos));
            if (previsaoPeloRitmo is { } previsao && previsao.Percentual == percentual)
                restantePeloRitmo = previsao.DuracaoAtiva - decorrido;
        }
        TextoTempoEstimado.Text = FluxoNovoRelatorio.TextoEstimativa(EstimativaAtual?.Termino?.ToLocalTime(), agora, restantePeloRitmo,
            EstimativaAtual?.PeloEditor ?? false);
    }

    internal void MostrarProgresso(ProgressoEtapa? progresso)
    {
        ProgressoAtual = progresso;
        TextoProgressoItens.Text = progresso is null ? "" :
            $"{Pmvni.Agentes.SituacaoAnalisePro.NomeEtapa(progresso.Etapa)} · {progresso.Avaliados} de {progresso.Total} "
            + (progresso.Secoes ? "seções com resultado" : "itens avaliados");
        TextoProgressoItens.Visibility = progresso is null ? Visibility.Collapsed : Visibility.Visible;
        MostrarEtapas(etapasMostradas, execucaoAtivaNaBarra);
    }

    IReadOnlyList<AchadoExecucao>? achadosExibidos;
    internal void MostrarAchados(IReadOnlyList<AchadoExecucao> achados)
    {
        if (achadosExibidos is not null && achadosExibidos.SequenceEqual(achados)) return;
        achadosExibidos = achados;
        var situacoes = Pmvni.Composicao.Kit.Situacoes.Sit;
        int Total(string chave) => achados.Count(a => situacoes[a.Situacao].Texto == situacoes[chave].Texto);
        TotalAtende.Text = Total("ATENDE").ToString();
        TotalNaoAtende.Text = Total("NAO_ATENDE").ToString();
        TotalComprovar.Text = Total("NAO_COMPROVADO").ToString();
        TotalDivergentes.Text = Total("DIVERGENCIA_INTERNA").ToString();
        for (var i = 0; i < achados.Count; i++)
        {
            var a = achados[i];
            if (i < ListaDescobertas.Children.Count && ListaDescobertas.Children[i] is Border existente && Equals(existente.Tag, a)) continue;
            var cartao = CartaoDescoberta(new($"{a.Id} · {a.Titulo} · {situacoes[a.Situacao].Texto}", a.Verificado));
            cartao.Tag = a;
            if (i < ListaDescobertas.Children.Count) ListaDescobertas.Children.RemoveAt(i);
            ListaDescobertas.Children.Insert(i, cartao);
        }
        while (ListaDescobertas.Children.Count > achados.Count)
            ListaDescobertas.Children.RemoveAt(ListaDescobertas.Children.Count - 1);
        TextoSemDescobertas.Visibility = achados.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    IReadOnlyList<Pmvni.Agentes.UsoEtapa>? consumoExibido;

    /// <summary>O cartão "Consumo" (Tarefa 13): uma linha por execução de etapa, com o selo de quem executou (programa ou a IA), tokens de entrada e saída,
    /// cache lido, tempo e a variação da cota de 5 h e da semanal; "—" no que o kit não informou. Sem linhas, o cartão some.</summary>
    internal void MostrarConsumo(IReadOnlyList<Pmvni.Agentes.UsoEtapa> uso)
    {
        if (consumoExibido is not null && consumoExibido.SequenceEqual(uso)) return;
        consumoExibido = uso;
        GradeConsumo.Children.Clear();
        GradeConsumo.RowDefinitions.Clear();
        GradeConsumo.ColumnDefinitions.Clear();
        CartaoConsumo.Visibility = uso.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (uso.Count == 0) return;
        for (var c = 0; c < 6; c++) GradeConsumo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        void Celula(UIElement e, int linha, int coluna)
        {
            Grid.SetRow((FrameworkElement)e, linha);
            Grid.SetColumn((FrameworkElement)e, coluna);
            GradeConsumo.Children.Add(e);
        }
        TextBlock Texto(string t, bool cabecalho = false) => new()
        {
            Text = t, FontSize = 11, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = !cabecalho,
            Foreground = cabecalho ? Recurso("TextFillColorSecondaryBrush") : Recurso("TextFillColorPrimaryBrush"),
        };
        GradeConsumo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] cabecalhos = ["Etapa", "Quem", "Tokens (entr. / saída)", "Cache", "Tempo", "Cota 5 h · semana"];
        for (var c = 0; c < cabecalhos.Length; c++) Celula(Texto(cabecalhos[c], cabecalho: true), 0, c);
        for (var i = 0; i < uso.Count; i++)
        {
            var u = uso[i];
            var linha = i + 1;
            GradeConsumo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Celula(Texto(Pmvni.Agentes.SituacaoAnalisePro.NomeEtapa(u.Etapa)), linha, 0);
            var programa = u.Executor == Pmvni.Agentes.UsoDasIas.Programa;
            var selo = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Recurso(programa ? "SubtleFillColorSecondaryBrush" : "SystemFillColorAttentionBackgroundBrush"),
                Child = new TextBlock { Text = u.Executor, FontSize = 11 },
            };
            if (!string.IsNullOrWhiteSpace(u.Modelo)) ToolTipService.SetToolTip(selo, u.Modelo);
            Celula(selo, linha, 1);
            Celula(Texto(Pmvni.Agentes.TextoConsumo.Tokens(u)), linha, 2);
            Celula(Texto(Pmvni.Agentes.TextoConsumo.Numero(u.CacheLido)), linha, 3);
            Celula(Texto(u.Duracao is { } d ? FluxoNovoRelatorio.TempoLegivel(d) : Pmvni.Agentes.TextoConsumo.Desconhecido), linha, 4);
            Celula(Texto(Pmvni.Agentes.TextoConsumo.Cota(u)), linha, 5);
        }
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

    internal string RotuloEtapa(string nome, EstadoEtapa estado, bool ativa)
    {
        var status = estado switch { EstadoEtapa.Feita => "Concluída", EstadoEtapa.Agora => ativa ? "Em andamento" : "Interrompida",
            EstadoEtapa.Erro => "Com erro", _ => "Pendente" };
        if (estado == EstadoEtapa.Feita) return status + " · 100%";
        if (estado == EstadoEtapa.Pendente) return status;
        if (EstimativaAtual is { Etapa: { } etapa, PercentualEtapa: { } percentual }
               && Pmvni.Agentes.SituacaoAnalisePro.NomeEtapa(etapa) == nome)
            return $"{status} · {percentual:0.#}% estimado pela IA";
        if (ProgressoAtual is { Total: > 0 } itens && Pmvni.Agentes.SituacaoAnalisePro.NomeEtapa(itens.Etapa) == nome)
            return $"{status} · {Math.Clamp(100d * itens.Avaliados / itens.Total, 0, 100):0.#}% " + (itens.Secoes ? "das seções com resultado" : "dos itens avaliados");
        return status;
    }

    static readonly string[] DescricoesEtapas = [
        "Identificar as pranchas e os documentos do processo e organizar o material para a leitura técnica.",
        "Conferir o projeto item por item e registrar a evidência que sustenta cada conclusão.",
        "Revisar os cálculos, as referências e as pendências identificadas na análise técnica.",
        "Organizar as constatações por seção e redigir o relatório com as evidências e exigências correspondentes.",
        "Comparar as conclusões das seções entre si e com os documentos para identificar divergências.",
        "Conferir a versão consolidada do relatório e verificar o arquivo final para entrega."
    ];

    static readonly string[] GlifosEtapas = ["", "", "", "", "", ""];

    /// <summary>As seis etapas em linha: concluída (verde ✓), em andamento (acento ▶), erro (vermelho !) ou pendente (cinza, ícone da etapa),
    /// ligadas por um traço cheio entre as concluídas e tracejado nas pendentes.</summary>
    internal void MostrarEtapas(IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas, bool animar)
    {
        var podeAnimar = animar && new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
        if (podeAnimar == animandoEtapas && execucaoAtivaNaBarra == animar && progressoDesenhado == ProgressoAtual
            && estimativaDesenhada == EstimativaAtual && etapasMostradas.SequenceEqual(etapas)) return;
        execucaoAtivaNaBarra = animar;
        progressoDesenhado = ProgressoAtual;
        estimativaDesenhada = EstimativaAtual;
        etapasMostradas = etapas.ToArray();
        animandoEtapas = podeAnimar;
        var feitas = etapas.Count(e => e.Estado == EstadoEtapa.Feita);
        var concluiu = etapas.Count > 0 && feitas == etapas.Count;
        double? percentualPorEtapas = etapas.Count > 0 ? 100d * feitas / etapas.Count : null;
        var percentual = concluiu ? 100d : EstimativaAtual?.PercentualGeral ?? percentualPorEtapas;
        BarraResumoEtapas.IsIndeterminate = animar && percentual is null;
        BarraResumoEtapas.Value = percentual ?? 0;
        BarraResumoEtapas.Visibility = etapas.Count > 0 || animar ? Visibility.Visible : Visibility.Collapsed;
        var contagemEtapas = etapas.Count == 0 ? "Aguardando as etapas da análise." : $"{feitas} de {etapas.Count} etapas concluídas";
        ContagemAtividades.Text = percentual is { } valor
            ? $"{contagemEtapas} · {valor:0.#}% geral" + (concluiu ? "" : EstimativaAtual?.PercentualGeral is not null ? " estimado pela IA" : " pelas etapas concluídas")
            : contagemEtapas + (animar ? " · Aguardando estimativa da IA" : "");
        EtapasAtividades.Children.Clear();
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
                Border? progresso = null;
                if (estado == EstadoEtapa.Agora && animar)
                {
                    var escala = new ScaleTransform { ScaleX = 0.45 };
                    progresso = new Border { Height = 2, VerticalAlignment = VerticalAlignment.Top,
                        Background = Recurso("AccentFillColorDefaultBrush"), RenderTransform = escala };
                    faixa.Children.Add(progresso);
                    if (podeAnimar)
                    {
                        var pulso = new DoubleAnimation { From = 0, To = 0.9, Duration = TimeSpan.FromSeconds(1.8),
                            RepeatBehavior = RepeatBehavior.Forever };
                        Storyboard.SetTarget(pulso, escala);
                        Storyboard.SetTargetProperty(pulso, "ScaleX");
                        var movimento = new Storyboard();
                        movimento.Children.Add(pulso);
                        faixa.Loaded += (_, _) => movimento.Begin();
                        faixa.Unloaded += (_, _) => movimento.Stop();
                    }
                }
                Grid.SetColumn(faixa, i);
                Grid.SetColumnSpan(faixa, 2);
                faixa.SizeChanged += (_, e) =>
                {
                    var w = e.NewSize.Width;
                    var margem = new Thickness(w / 4 + 15, 12, w / 4 + 15, 0);
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
                Width = 24, Height = 24, CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Center, Background = Recurso(fundo),
                BorderThickness = new Thickness(estado == EstadoEtapa.Pendente ? 1 : 0), BorderBrush = Recurso("ControlStrongStrokeColorDefaultBrush"),
                Child = new FontIcon { Glyph = glifo, FontSize = 15, Foreground = Recurso(frente) },
            };
            var situacao = RotuloEtapa(nome, estado, animar);
            var bloco = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center };
            var linhaResumo = new Grid { ColumnSpacing = 8, MinHeight = 23 };
            linhaResumo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
            linhaResumo.ColumnDefinitions.Add(new ColumnDefinition());
            FrameworkElement marcador;
            if (estado == EstadoEtapa.Agora && animar)
            {
                var anel = new ProgressRing { Width = 16, Height = 16, MinWidth = 0, MinHeight = 0, IsActive = true };
                Trabalhando.SetAutomatico(anel, false);
                marcador = anel;
            }
            else marcador = new FontIcon { Glyph = estado == EstadoEtapa.Pendente ? "\uEA3A" : glifo, FontSize = 14 };
            linhaResumo.Children.Add(marcador);
            var textos = new StackPanel { Spacing = 2 };
            textos.Children.Add(new TextBlock { Text = $"{nome}  ·  {situacao}", FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextAlignment = TextAlignment.Left, TextWrapping = TextWrapping.Wrap });
            var descricao = DescricoesEtapas[i];
            if (ProgressoAtual is { Total: > 0 } contagem && Pmvni.Agentes.SituacaoAnalisePro.NomeEtapa(contagem.Etapa) == nome)
                descricao += $" Itens avaliados nesta etapa: {contagem.Avaliados} de {contagem.Total}.";
            textos.Children.Add(new TextBlock { Text = descricao, FontSize = 12, TextAlignment = TextAlignment.Left,
                Foreground = Recurso("TextFillColorSecondaryBrush"), TextWrapping = TextWrapping.Wrap });
            Grid.SetColumn(textos, 1);
            linhaResumo.Children.Add(textos);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(linhaResumo, $"{nome}. {situacao}. {descricao}");
            EtapasAtividades.Children.Add(linhaResumo);
            var dica = new TextBlock { Text = nome + " · " + situacao, TextWrapping = TextWrapping.Wrap };
            var botao = new Button
            {
                Content = circulo, MinHeight = 24, MinWidth = 24, Padding = new Thickness(0), CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Center,
                Flyout = new Flyout { Content = dica }
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(botao, dica.Text);
            bloco.Children.Add(botao);
            bloco.Children.Add(new TextBlock
            {
                Text = nome, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0),
                FontWeight = estado == EstadoEtapa.Pendente ? Microsoft.UI.Text.FontWeights.Normal : Microsoft.UI.Text.FontWeights.SemiBold,
            });
            bloco.Children.Add(new TextBlock
            {
                Text = situacao, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                Foreground = Recurso(estado switch { EstadoEtapa.Agora => "AccentTextFillColorPrimaryBrush", EstadoEtapa.Erro => "SystemFillColorCriticalBrush", _ => "TextFillColorSecondaryBrush" }),
            });
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bloco, $"{nome}: {situacao}");
            Grid.SetColumn(bloco, i);
            PainelEtapas.Children.Add(bloco);
        }
    }

    /// <summary>Empilha os cartões quando a largura disponível não comporta as três colunas.</summary>
    internal void AjustarColunasExecucao(double largura)
    {
        var ampla = largura >= 1080;
        var estreita = largura < 760;
        GradeConteudo.ColumnDefinitions[1].Width = estreita ? new GridLength(0) : new GridLength(11, GridUnitType.Star);
        GradeConteudo.ColumnDefinitions[2].Width = ampla ? new GridLength(8, GridUnitType.Star) : new GridLength(0);
        GradeConteudo.RowDefinitions[1].Height = ampla ? new GridLength(0) : new GridLength(estreita ? 1 : 2, GridUnitType.Star);
        GradeConteudo.RowDefinitions[2].Height = estreita ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(ColunaLateral, estreita ? 0 : 1);
        Grid.SetRow(ColunaLateral, estreita ? 1 : 0);
        ColunaLateral.Margin = estreita ? new Thickness(0, 8, 0, 0) : new Thickness(8, 0, 0, 0);
        Grid.SetColumn(ColunaDetalhes, ampla ? 2 : 0);
        Grid.SetColumnSpan(ColunaDetalhes, ampla || estreita ? 1 : 2);
        Grid.SetRow(ColunaDetalhes, ampla ? 0 : estreita ? 2 : 1);
        ColunaDetalhes.Margin = ampla ? new Thickness(8, 0, 0, 0) : new Thickness(0, 8, 0, 0);

        var botoesEmbaixo = largura < 920;
        Grid.SetRow(BotoesExecucao, botoesEmbaixo ? 1 : 0);
        Grid.SetColumn(BotoesExecucao, botoesEmbaixo ? 0 : 2);
        Grid.SetColumnSpan(BotoesExecucao, botoesEmbaixo ? 3 : 1);
    }

    static Brush Recurso(string chave) => (Brush)Application.Current.Resources[chave];
}
