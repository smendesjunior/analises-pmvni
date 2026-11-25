using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Pmvni.Agentes;

namespace Pmvni.App;

/// <summary>Página Novo relatório (desenho §12.2): só a parte visual. Quem preenche, decide e inicia o agente é a janela (<c>MainWindow.NovoRelatorio.cs</c>)
/// com a <see cref="SessaoNovoRelatorio"/>.</summary>
public sealed partial class PaginaNovoRelatorio : UserControl
{
    internal string TextoExecutorExibido => ExecutorDetalhes.Text;

    ScrollViewer? rolagemDasLinhas;

    public PaginaNovoRelatorio()
    {
        InitializeComponent();
        CaixaPedido.PlaceholderText = FluxoNovoRelatorio.DicaPedido;
        TextoPedidoClaude.Text = FluxoNovoRelatorio.TextoPedidoClaude;
        // Correções 2026-11-15 (image7): a coluna do formulário tem a largura da área visível, menos as margens, até 1400 (centralizada).
        PainelFormulario.SizeChanged += (_, e) => AjustarLarguraDoFormulario(e.NewSize.Width);
        RolagemMonitor.SizeChanged += (_, e) => AjustarColunasExecucao(e.NewSize.Width);
        BotaoEvidenciasPdf.SizeChanged += (_, e) => IconeAbrirEvidencias.Visibility =
            e.NewSize.Width >= 290 ? Visibility.Visible : Visibility.Collapsed;
        AbasMonitor.SelectionChanged += (_, _) => MostrarAba();
        AbasMonitor.SelectedItem = AbasMonitor.MenuItems[0];
        ListaLinhas.Loaded += (_, _) => RolarLinhasAoFim();
        RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => AtualizarAcoesRetomada());
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
        AtualizarAcoesRetomada();
        MonitorNativo.Visibility = Visivel(execucao);
        PainelExecucao.Visibility = Visivel(execucao);
        if (execucao) AbrirMonitor();
        else PararMonitor();
        AtualizarRadares();
    }

    /// <summary>"Retomar análise" e "Descartar análise" ficam na faixa de opções: só aparecem com o formulário desta página à vista.</summary>
    void AtualizarAcoesRetomada() =>
        AcoesRetomadaFormulario.Visibility = Visivel(Visibility == Visibility.Visible && PainelFormulario.Visibility == Visibility.Visible);

    /// <summary>Tira o painel "Retomar / Descartar análise" desta página para a janela colocá-lo na faixa de opções. A retirada é pela grade
    /// raiz: durante a construção da janela o WinUI ainda informa o pai do painel como nulo.</summary>
    internal StackPanel DestacarAcoesRetomada()
    {
        if (Content is Panel raiz) raiz.Children.Remove(AcoesRetomadaFormulario);
        AcoesRetomadaFormulario.Margin = new Thickness(0);
        return AcoesRetomadaFormulario;
    }

    internal event EventHandler? DetalhesSelecionados;
    internal bool DetalhesVisiveis => VistaDaExecucao && PainelDetalhes.Visibility == Visibility.Visible;

    void AbrirLogDoMonitor(object sender, RoutedEventArgs args)
    {
        AbasMonitor.SelectedItem = AbasMonitor.MenuItems.OfType<NavigationViewItem>().First(i => i.Content as string == "Log");
        MostrarLogCompleto(true);
        DispatcherQueue.TryEnqueue(RolarLinhasAoFim);
    }

    void AlternarLogCompleto(object sender, RoutedEventArgs args)
    {
        bool abrir = ListaLinhas.Visibility != Visibility.Visible;
        MostrarLogCompleto(abrir);
        if (abrir) DispatcherQueue.TryEnqueue(RolarLinhasAoFim);
    }

    void MostrarLogCompleto(bool abrir)
    {
        ListaLinhas.Visibility = Visivel(abrir);
        CabecalhoLog.Visibility = Visivel(abrir);
        DestaqueRecente.Visibility = Visivel(!abrir && atividades.Count > 0);
        LinkLogCompleto.Content = abrir ? "Recolher log completo" : "Abrir log completo";
    }

    void MostrarAba()
    {
        var aba = (AbasMonitor.SelectedItem as NavigationViewItem)?.Content as string ?? "Resumo";
        PainelResumo.Visibility = Visivel(aba == "Resumo");
        CartaoConsumo.Visibility = Visivel(aba == "Consumo");
        CartaoAtividades.Visibility = Visivel(aba == "Etapas");
        PainelLog.Visibility = Visivel(aba == "Log");
        PainelDetalhes.Visibility = Visivel(aba == "Detalhes");
        AtualizarMonitorHtml();
        if (aba == "Consumo") DesenharConsumo(consumoExibido ?? []);
        if (DetalhesVisiveis)
        {
            AtualizarNarrativa();
            DetalhesSelecionados?.Invoke(this, EventArgs.Empty);
        }
        AtualizarRadares();
    }

    static Visibility Visivel(bool mostrar) => mostrar ? Visibility.Visible : Visibility.Collapsed;

    void AtualizarRadares()
    {
        var ativo = VistaDaExecucao && emCurso;
        RadarStatus.Ativo = ativo && PainelResumo.Visibility == Visibility.Visible;
        RadarEtapas.Ativo = ativo && CartaoAtividades.Visibility == Visibility.Visible;
        RadarLog.Ativo = ativo && PainelLog.Visibility == Visibility.Visible;
    }

    internal bool VistaDaExecucao => MonitorNativo.Visibility == Visibility.Visible;

    /// <summary>A lista do andamento está no fim (ou ainda não rola): as linhas novas a mantêm no fim.</summary>
    bool LinhasNoFim()
    {
        rolagemDasLinhas ??= Achar<ScrollViewer>(ListaLinhas);
        return rolagemDasLinhas is null || rolagemDasLinhas.VerticalOffset >= rolagemDasLinhas.ScrollableHeight - 4;
    }

    void RolarLinhasAoFim()
    {
        if (PainelLog.Visibility == Visibility.Visible && ListaLinhas.Visibility == Visibility.Visible && ListaLinhas.Items.Count > 0) ListaLinhas.ScrollIntoView(ListaLinhas.Items[^1]);
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

    // ---------- Monitor de análise ----------

    readonly System.Collections.ObjectModel.ObservableCollection<ItemAtividade> atividades = [];
    System.Collections.ObjectModel.ObservableCollection<string>? origem;
    bool emCurso;
    string? etapaAtual;
    string? provedorAtual;
    (string Nome, EstadoEtapa Estado)[] etapasMostradas = [];
    int colunasDaRota = 6;

    /// <summary>Liga a lista de atividades às linhas da execução (null = desliga). Linhas antigas sem horário no registro não ganham um horário fictício.</summary>
    internal void LigarLinhas(System.Collections.ObjectModel.ObservableCollection<string>? linhas)
    {
        if (ReferenceEquals(origem, linhas)) return;
        if (origem is not null) origem.CollectionChanged -= LinhasMudaram;
        MostrarLogCompleto(true);
        execucaoHtml++;
        origem = linhas;
        atividades.Clear();
        if (linhas is not null)
        {
            foreach (var l in linhas) atividades.Add(new ItemAtividade(Atividades.DaLinha(l)));
            linhas.CollectionChanged += LinhasMudaram;
        }
        ListaLinhas.ItemsSource = linhas is null ? null : atividades;
        MarcarEmCurso();
        if (linhas is not null) EnfileirarRolagemAoFim(linhas);
    }

    void LinhasMudaram(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) Espelhar(s, e); else DispatcherQueue.TryEnqueue(() => Espelhar(s, e));
    }

    void Espelhar(object? fonte, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (origem is not { } linhas || !ReferenceEquals(linhas, fonte)) return;
        versaoLogHtml++;
        var noFim = LinhasNoFim();
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add && e.NewItems is { } novos && e.NewStartingIndex == atividades.Count)
            foreach (string l in novos) atividades.Add(new ItemAtividade(Atividades.DaLinha(l, DateTime.Now)));
        else
        {
            atividades.Clear();
            foreach (var l in linhas) atividades.Add(new ItemAtividade(Atividades.DaLinha(l)));
        }
        MarcarEmCurso();
        if (noFim) EnfileirarRolagemAoFim(linhas);
    }

    void EnfileirarRolagemAoFim(System.Collections.ObjectModel.ObservableCollection<string> linhas) =>
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (VistaDaExecucao && ReferenceEquals(origem, linhas)) RolarLinhasAoFim();
        });

    void MarcarEmCurso()
    {
        for (var i = 0; i < atividades.Count; i++) atividades[i].EmCurso = emCurso && i == atividades.Count - 1;
        var ultima = atividades.LastOrDefault()?.Atividade;
        DestaqueRecente.Visibility = Visivel(ultima is not null && ListaLinhas.Visibility != Visibility.Visible);
        LinkLogCompleto.IsEnabled = ultima is not null;
        AtividadeRecente.Item = ultima;
        AtividadeRecente.EmCurso = emCurso;
        var contagem = atividades.Count == 1 ? "1 atividade registrada" : $"{atividades.Count} atividades registradas";
        TextoContagem.Text = atividades.Count == 1 ? "1 evento registrado" : $"{atividades.Count} eventos registrados";
        HoraUltimoEvento.Text = ultima?.Hora is not null ? $"Último evento às {ultima.HoraTexto}" : "";
        TextoEtapaAtual.Text = etapaAtual is null ? "" : $"Etapa atual: {etapaAtual}";
        TextoEtapaAtual.Visibility = Visivel(etapaAtual is not null);
        AtualizarContagemResumo();
    }

    void AtualizarContagemResumo() => TextoTotalEtapas.Text = ResumoDaContagem(incluirExecutor: true);

    string ResumoDaContagem(bool incluirExecutor)
    {
        var feitas = etapasMostradas.Count(e => e.Estado == EstadoEtapa.Feita);
        var itens = atividades.Count == 1 ? "1 atividade registrada" : $"{atividades.Count} atividades registradas";
        return string.Join(" · ", new[] { $"{feitas} de {etapasMostradas.Length} etapas concluídas", itens, incluirExecutor ? ExecutorAtual() ?? provedorAtual : null }
            .Where(t => !string.IsNullOrWhiteSpace(t)));
    }

    internal void MostrarIdentificacao(string processo, string? requerente)
    {
        ProcessoMonitor.Text = processo;
        TextoRequerente.Text = string.IsNullOrWhiteSpace(requerente) ? "Não informado" : requerente.Trim();
    }

    internal void MostrarEvidencias(bool disponiveis)
    {
        LinkEvidenciasPdf.Visibility = BotaoEvidenciasPdf.Visibility = disponiveis ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void MostrarResumo(bool rodando, string? erro, IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas, string? provedor = null, string? situacao = null, bool emEspera = false)
    {
        var falhou = !string.IsNullOrWhiteSpace(erro);
        var concluida = !falhou && etapas.Count > 0 && etapas.All(x => x.Estado == EstadoEtapa.Feita);
        emCurso = rodando && !falhou && !concluida && !emEspera;
        var atual = etapas.ToList().FindIndex(x => x.Estado is EstadoEtapa.Agora or EstadoEtapa.Erro);
        var temAtual = atual >= 0;
        etapaAtual = temAtual ? etapas[atual].Nome : emCurso ? "Aguardando registro" : null;
        provedorAtual = provedor;
        MarcarEmCurso();
        ConsumoVazio.Text = emCurso ? "Aguardando o primeiro registro de consumo ou duração da etapa."
            : "Esta execução não registrou métricas de consumo ou duração por etapa.";

        TituloStatus.Text = falhou ? "Análise com problema" : concluida ? "Análise concluída" : emEspera ? "Análise pausada" : emCurso ? "Análise em andamento" : "Análise parada";
        IconeStatus.Glyph = falhou ? Glifos.Avisos : concluida ? Glifos.Situacao : Glifos.Pausado;
        IconeStatus.Style = Estilo(falhou ? "PmvniIconeSeloErro" : concluida ? "PmvniIconeSeloSucesso" : "PmvniIconeDestaqueStyle");
        SeloAoVivo.Visibility = SeloAtividades.Visibility = SeloLog.Visibility = Visivel(emCurso);
        AtualizarRadares();
        BotaoPausarAnalise.Visibility = BotaoCancelarAnalise.Visibility = rodando ? Visibility.Visible : Visibility.Collapsed;

        MostrarEtapas(etapas, emCurso);

        var proxima = temAtual && atual + 1 < etapas.Count ? etapas[atual + 1].Nome : null;
        ExecutorDetalhes.Text = ExecutorAtual() ?? provedor ?? "Aguardando identificação";
        SituacaoDetalhes.Text = string.IsNullOrWhiteSpace(situacao) ? TituloStatus.Text : situacao;
        TextoProximaEtapa.Text = emEspera ? situacao ?? "Aguardando retomada" : falhou || concluida || !emCurso ? "Execução encerrada"
            : !temAtual ? "Aguardando confirmação" : proxima ?? "Última etapa em andamento";
        ErroDetalhes.Text = erro?.Trim() ?? "";
        ErroDetalhes.Visibility = Visivel(falhou);
    }

    internal ProgressoEtapa? ProgressoAtual { get; private set; }
    ProgressoEtapa? progressoDesenhado;
    internal EstimativaAnalise? EstimativaAtual { get; private set; }
    EstimativaAnalise? estimativaDesenhada;
    bool execucaoAtivaNaBarra;
    (double Percentual, TimeSpan DuracaoAtiva)? previsaoPeloRitmo;

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

    internal void MostrarRelogios(TimeSpan ativo, DateTimeOffset agora, TimeSpan? decorrido = null)
    {
        TextoTempoDecorrido.Text = FluxoNovoRelatorio.TempoLegivel(decorrido ?? ativo, incluirSegundos: true);
        TextoTempoAtivo.Text = "Em atividade por " + FluxoNovoRelatorio.TempoLegivel(ativo, incluirSegundos: true);
        TimeSpan? restantePeloRitmo = null;
        if (execucaoAtivaNaBarra && EstimativaAtual is { PeloEditor: false } && ativo > TimeSpan.Zero &&
            EstimativaAtual?.PercentualGeral is double percentual and >= 1 and < 100)
        {
            // Usa o tempo ativo acumulado e mantém a previsão até o progresso mudar.
            var duracaoEmSegundos = ativo.TotalSeconds * 100 / percentual;
            if (previsaoPeloRitmo?.Percentual != percentual && duracaoEmSegundos < (DateTimeOffset.MaxValue - agora).TotalSeconds)
                previsaoPeloRitmo = (percentual, TimeSpan.FromSeconds(duracaoEmSegundos));
            if (previsaoPeloRitmo is { } previsao && previsao.Percentual == percentual)
                restantePeloRitmo = previsao.DuracaoAtiva - ativo;
        }
        var termino = EstimativaAtual?.Termino?.ToLocalTime();
        var peloEditor = EstimativaAtual is not { PeloEditor: false };
        var semDados = EstimativaAtual is null or { SemDados: true };
        var aproximada = EstimativaAtual?.Aproximada == true;
        var inicial = EstimativaAtual?.Inicial == true;
        TextoTempoEstimado.Text = FluxoNovoRelatorio.PrevisaoCurta(termino, agora, restantePeloRitmo, peloEditor, semDados, aproximada, inicial);
        var detalhes = FluxoNovoRelatorio.TextoEstimativa(termino, agora, restantePeloRitmo, peloEditor, semDados, aproximada, inicial);
        ToolTipService.SetToolTip(TextoTempoEstimado, detalhes);
        AutomationProperties.SetHelpText(TextoTempoEstimado, detalhes);
    }

    internal void MostrarProgresso(ProgressoEtapa? progresso)
    {
        ProgressoAtual = progresso;
        MostrarEtapas(etapasMostradas, execucaoAtivaNaBarra);
    }

    IReadOnlyList<AchadoExecucao> achadosExibidos = [];
    string? filtroDescobertas;
    bool listaCompleta;
    void AlternarPreviaEvidencias(object sender, RoutedEventArgs args)
    {
        listaCompleta = !listaCompleta;
        AtualizarListaDescobertas();
    }
    static readonly IReadOnlyDictionary<string, (string Texto, string Cor)> Situacoes = Pmvni.Composicao.Kit.Situacoes.Sit;

    void FiltrarDescobertas(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem item) return;
        listaCompleta = false;
        filtroDescobertas = item.Tag as string;
        AtualizarListaDescobertas();
        ColunaLateral.ChangeView(null, 0, null, true);
    }

    internal void ReiniciarFiltroDescobertas()
    {
        if (filtroDescobertas is null && !listaCompleta) return;
        listaCompleta = false;
        filtroDescobertas = null;
        FiltroTodas.IsChecked = true;
        AtualizarListaDescobertas();
        ColunaLateral.ChangeView(null, 0, null, true);
    }

    internal void MostrarAchados(IReadOnlyList<AchadoExecucao> achados)
    {
        if (achadosExibidos.SequenceEqual(achados)) return;
        achadosExibidos = achados;
        int Total(string chave) => achados.Count(a => Situacoes[a.Situacao].Texto == Situacoes[chave].Texto);
        TotalAtende.Text = Total("ATENDE").ToString();
        TotalNaoAtende.Text = Total("NAO_ATENDE").ToString();
        TotalComprovar.Text = Total("NAO_COMPROVADO").ToString();
        TotalDivergentes.Text = Total("DIVERGENCIA_INTERNA").ToString();
        AtualizarListaDescobertas();
    }

    void AtualizarListaDescobertas()
    {
        var filtro = filtroDescobertas is { } chave ? Situacoes[chave].Texto : null;
        var visiveis = filtro is null ? achadosExibidos : achadosExibidos.Where(a => Situacoes[a.Situacao].Texto == filtro).ToArray();
        var quantidade = listaCompleta ? visiveis.Count : Math.Min(4, visiveis.Count);
        for (var i = 0; i < quantidade; i++)
        {
            var a = visiveis[i];
            if (i < ListaDescobertas.Children.Count && ListaDescobertas.Children[i] is Border existente && Equals(existente.Tag, a)) continue;
            if (i < ListaDescobertas.Children.Count) ListaDescobertas.Children.RemoveAt(i);
            ListaDescobertas.Children.Insert(i, CartaoDescoberta(a));
        }
        while (ListaDescobertas.Children.Count > quantidade)
            ListaDescobertas.Children.RemoveAt(ListaDescobertas.Children.Count - 1);
        VazioDescobertas.Visibility = visiveis.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ColunaLateral.Visibility = visiveis.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TextoSemDescobertas.Text = filtro is null ? "Aguardando primeiras descobertas" : "Nenhuma descoberta nesta classificação";
        TextoAjudaDescobertas.Text = filtro is null
            ? "As constatações aparecerão aqui, com classificação, referência ao PDF e página e trechos da evidência."
            : "Altere o filtro para ver as demais classificações.";
        AlternarEvidencias.Visibility = Visivel(visiveis.Count > 4);
        AlternarEvidencias.Content = listaCompleta ? "Recolher para a prévia" : $"Ver todos os {visiveis.Count} registros";
        TextoFiltro.Text = listaCompleta || visiveis.Count <= 4 ? "" : $"Prévia de {quantidade} dos {visiveis.Count} registros identificados.";
        TextoFiltro.Visibility = Visivel(TextoFiltro.Text.Length != 0);
        TextoRegistros.Text = visiveis.Count == 1 ? "1 registro" : $"{visiveis.Count} registros";
    }

    Border CartaoDescoberta(AchadoExecucao achado)
    {
        var (situacao, cor) = Situacoes[achado.Situacao];
        var estiloDaClassificacao = cor switch
        {
            "g" => "MonitorRotuloSucesso",
            "r" => "MonitorRotuloErro",
            "a" => "MonitorRotuloCautela",
            _ => "MonitorRotuloDivergencia",
        };
        var classificacao = new TextBlock { Text = situacao, Style = Estilo(estiloDaClassificacao) };
        var titulo = new TextBlock
        {
            Text = $"{achado.Id} · {achado.Titulo}", Style = Estilo("PmvniAjusteTitulo"), IsTextSelectionEnabled = true,
        };
        var selo = new Border
        {
            Padding = new Thickness(6, 2, 6, 2), VerticalAlignment = VerticalAlignment.Center,
            Style = Estilo(achado.Verificado ? "PmvniSeloStatusSucessoStyle" : "PmvniSeloStatusAtencaoStyle"),
            Child = new TextBlock { Text = achado.Verificado ? "Verificada" : "Preliminar", Style = Estilo("CaptionTextBlockStyle") },
        };
        var cabecalho = new PainelQuebra { Espaco = 8, EspacoLinhas = 3 };
        cabecalho.Children.Add(cor == "conferir" ? Selo(situacao, "MnTagConferir", "MnTagTextoConferir") : classificacao);
        cabecalho.Children.Add(selo);
        var textos = new StackPanel { Spacing = 2 };
        textos.Children.Add(cabecalho);
        textos.Children.Add(titulo);
        return new Border
        {
            Tag = achado, Padding = new Thickness(20, 12, 20, 12), MinHeight = 70,
            Style = Estilo("MonitorCartao"), CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(0, 0, 0, 1), Child = textos,
        };
    }

    IReadOnlyList<UsoEtapa>? consumoExibido;

    OrcamentoDaAnalise? orcamentoExibido;

    ConsumoAoVivo? leituraExibida;

    internal void MostrarConsumo(IReadOnlyList<UsoEtapa> uso, OrcamentoDaAnalise? orcamento = null, ConsumoAoVivo? leitura = null)
    {
        if (consumoExibido is not null && consumoExibido.SequenceEqual(uso) &&
            orcamentoExibido == orcamento && leituraExibida == leitura) return;
        orcamentoExibido = orcamento;
        leituraExibida = leitura;
        ExecutorDetalhes.Text = ExecutorAtual() ?? provedorAtual ?? "Aguardando identificação";
        MostrarEtapas(etapasMostradas, execucaoAtivaNaBarra);
        consumoExibido = uso;
        if (DetalhesVisiveis) AtualizarNarrativa();
        if (CartaoConsumo.Visibility == Visibility.Visible) DesenharConsumo(uso);
    }

    void DesenharConsumo(IReadOnlyList<UsoEtapa> uso)
    {
        PreencherResumoConsumo(uso);
        GradeConsumo.Children.Clear();
        if (uso.Count > 0)
        {
            GradeConsumo.Children.Add(LinhaTabelaConsumo(["Etapa", "Executor", "Entrada", "Saída", "Cache lido", "Cota de 5 h", "Cota semanal", "Tempo"], true));
            foreach (var etapa in uso)
            {
                var tempo = etapa.Duracao is { } d ? FluxoNovoRelatorio.TempoLegivel(d, incluirSegundos: true) : "—";
                var linha = LinhaTabelaConsumo([FluxoNovoRelatorio.NomeDaEtapa(etapa.Etapa), NomeExecutor(etapa),
                    TextoConsumo.Numero(etapa.Entrada), TextoConsumo.Numero(etapa.Saida), TextoConsumo.Numero(etapa.CacheLido),
                    Pontos(etapa.Cota5h), Pontos(etapa.CotaSemana), tempo], false);
                var detalhe = TextoConsumo.Metricas(etapa).Select(x => $"{x.Rotulo} · {x.Valor}").ToList();
                if (etapa.Situacao is { } estado) detalhe.Insert(0, estado);
                if (detalhe.Count > 0)
                {
                    var texto = string.Join(Environment.NewLine, detalhe);
                    ToolTipService.SetToolTip(linha, texto);
                    AutomationProperties.SetHelpText(linha, texto);
                }
                GradeConsumo.Children.Add(linha);
            }
        }
        var quantas = uso.Select(e => e.Etapa).Distinct().Count();
        ContagemConsumo.Text = etapasMostradas.Length > 0 ? $"{quantas} de {etapasMostradas.Length} etapas com registro" : "";
        ConsumoVazio.Visibility = Visivel(uso.Count == 0);
        GraficosDeConsumo(PainelGraficos, uso);
    }

    static string SituacaoDaEtapa(EstadoEtapa estado, bool ativa) => estado switch
    {
        EstadoEtapa.Feita => "Concluída",
        EstadoEtapa.Agora => ativa ? "Em andamento" : "Interrompida",
        EstadoEtapa.Erro => "Com erro",
        _ => "Pendente",
    };

    /// <summary>De onde vem o percentual: com <see cref="EstimativaAnalise.Inicial"/>, parte dele saiu das durações iniciais (sem histórico neste computador).</summary>
    static string OrigemDoPercentual(EstimativaAnalise estimativa) => !estimativa.PeloEditor ? "estimado pela IA"
        : estimativa.PercentualEstimado ? "estimado pela duração da etapa" : "pelas etapas e pelos resultados registrados";

    internal string RotuloEtapa(string nome, EstadoEtapa estado, bool ativa)
    {
        var status = SituacaoDaEtapa(estado, ativa);
        if (estado == EstadoEtapa.Feita) return status + " · 100%";
        if (estado == EstadoEtapa.Pendente) return status;
        if (EstimativaAtual is { Etapa: { } etapa, PercentualEtapa: { } percentual } estimativa && FluxoNovoRelatorio.NomeDaEtapa(etapa) == nome)
            return $"{status} · {Math.Min(99, Math.Round(percentual)):0}% {OrigemDoPercentual(estimativa)}";
        if (ProgressoAtual is { Total: > 0 } itens && FluxoNovoRelatorio.NomeDaEtapa(itens.Etapa) == nome)
            return $"{status} · {Math.Clamp(Math.Round(100d * itens.Avaliados / itens.Total), 0, 99):0}% "
                + (itens.Secoes ? "das seções com resultado" : "dos itens avaliados");
        return status;
    }

    static string DescricaoDaEtapa(string nome) => nome switch
    {
        "Preparo" => "Identificar pranchas e organizar os arquivos do processo",
        // Fluxo linear do kit (2026-11-22): a análise roda em três frentes; auditoria cruzada, relatório e revisão final são programas locais.
        "Análise" => "Conferir os grupos do plano e registrar as evidências",
        "Auditoria técnica" => "Conferir cada achado com a figura citada e corrigir o mérito",
        "Auditoria cruzada" => "Conferência por programa dos controles certificados",
        "Produção do relatório" => "Montar o PDF no modelo oficial e conferir a fidelidade",
        "Revisão final" => "Inspecionar o PDF página por página e conferir os hashes",
        _ => "Etapa definida pelo kit PMVNI instalado.",
    };

    DetalhesDasEtapas detalhesEtapas = DetalhesDasEtapas.Vazio;
    DetalhesDasEtapas? detalhesDesenhados;
    string? executorDesenhado;

    /// <summary>Modelo e esforço (ou "Programa"), as frentes da Análise e as objeções pendentes, lidos pela janela do que o executor do kit grava
    /// (<see cref="DetalhesDasEtapas.Ler"/>). Sem dado, nada aparece no cartão.</summary>
    internal void MostrarDetalhesDasEtapas(DetalhesDasEtapas detalhes)
    {
        if (EquivalentesDetalhes(detalhesEtapas, detalhes)) return;
        detalhesEtapas = detalhes;
        MostrarEtapas(etapasMostradas, execucaoAtivaNaBarra);
    }

    static bool EquivalentesDetalhes(DetalhesDasEtapas a, DetalhesDasEtapas b) =>
        a.Plano.SequenceEqual(b.Plano) && a.DescricoesDoPlano.SequenceEqual(b.DescricoesDoPlano) && a.Validacoes.SequenceEqual(b.Validacoes) && a.Frentes.SequenceEqual(b.Frentes) && a.Grupos.SequenceEqual(b.Grupos) && a.Objecoes.SequenceEqual(b.Objecoes)
        && a.Registros.Count == b.Registros.Count && a.Registros.All(k => b.Registros.GetValueOrDefault(k.Key) == k.Value)
        && a.Etapas.Count == b.Etapas.Count && a.Etapas.All(k => b.Etapas.TryGetValue(k.Key, out var v) && v == k.Value);

    /// <summary>O id do kit da etapa mostrada com este nome (os cartões só conhecem o nome).</summary>
    string? IdDaEtapa(string nome) =>
        detalhesEtapas.Plano.Concat(detalhesEtapas.Etapas.Keys).Concat(detalhesEtapas.Registros.Keys).Concat(detalhesEtapas.Objecoes.Select(o => o.Etapa)).Append("analyze")
            .FirstOrDefault(id => FluxoNovoRelatorio.NomeDaEtapa(id) == nome);

    string? ExecutorAtual()
    {
        if (!emCurso || etapaAtual is null) return null;
        var id = IdDaEtapa(etapaAtual);
        if (id is not null && leituraExibida?.EtapaEmExecucao == id && leituraExibida.ModelosEmExecucao is { } modelos) return modelos;
        return id is not null && detalhesEtapas.Etapas.GetValueOrDefault(id)?.Programa == true
            ? "Programa local" : "Aguardando identificação do modelo";
    }

    void PreencherDetalhes(LinhaEtapa linha, string nome)
    {
        var id = IdDaEtapa(nome);
        var executor = emCurso && nome == etapaAtual ? ExecutorAtual() : id is null ? null : detalhesEtapas.TextoExecutor(id);
        linha.Executor.Text = executor ?? "";
        linha.Executor.Visibility = Visivel(executor is not null);

        IReadOnlyList<FrenteAnalise> frentes = id == "analyze" ? detalhesEtapas.Frentes : [];
        while (linha.Frentes.Children.Count > frentes.Count) linha.Frentes.Children.RemoveAt(linha.Frentes.Children.Count - 1);
        for (var i = 0; i < frentes.Count; i++)
        {
            if (i >= linha.Frentes.Children.Count)
            {
                var caixa = new StackPanel { Spacing = 2, MinWidth = 150 };
                caixa.Children.Add(new TextBlock { Style = Estilo("MonitorLegenda"), TextWrapping = TextWrapping.Wrap });
                caixa.Children.Add(new ProgressBar { Maximum = 100, Height = 3, MinHeight = 3 });
                linha.Frentes.Children.Add(caixa);
            }
            var f = frentes[i];
            var painel = (StackPanel)linha.Frentes.Children[i];
            var texto = FluxoNovoRelatorio.TextoFrente(f);
            if (f.Atividade is { } atividade) texto += " · " + atividade;
            ((TextBlock)painel.Children[0]).Text = texto;
            var barra = (ProgressBar)painel.Children[1];
            var valor = f.Total > 0 ? 100d * f.Prontas / f.Total : 0;
            if (barra.Value != valor) barra.Value = valor;
            AutomationProperties.SetName(painel, "Frente da análise: " + texto);
        }
        linha.Frentes.Visibility = Visivel(frentes.Count > 0);

        var objecao = id is null ? null : detalhesEtapas.Objecoes.FirstOrDefault(o => o.Etapa == id);
        linha.Objecao.Text = objecao is null ? "" : FluxoNovoRelatorio.TextoObjecao(objecao);
        linha.Objecao.Visibility = Visivel(objecao is not null);
    }

    internal void MostrarEtapas(IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas, bool animar)
    {
        var executorAtual = ExecutorAtual();
        if (executorDesenhado == executorAtual && execucaoAtivaNaBarra == animar && progressoDesenhado == ProgressoAtual && ReferenceEquals(detalhesDesenhados, detalhesEtapas)
            && estimativaDesenhada == EstimativaAtual && etapasMostradas.SequenceEqual(etapas)) return;
        executorDesenhado = executorAtual;
        detalhesDesenhados = detalhesEtapas;
        execucaoAtivaNaBarra = animar;
        progressoDesenhado = ProgressoAtual;
        estimativaDesenhada = EstimativaAtual;
        etapasMostradas = etapas.ToArray();

        var feitas = etapas.Count(e => e.Estado == EstadoEtapa.Feita);
        var concluiu = etapas.Count > 0 && feitas == etapas.Count;
        double? percentualPorEtapas = etapas.Count > 0 ? 100d * feitas / etapas.Count : null;
        var percentual = concluiu ? 100d : EstimativaAtual?.PercentualGeral ?? percentualPorEtapas;
        if (percentual is { } valor) percentual = Math.Clamp(Math.Round(valor, 1), 0, concluiu ? 100 : 99);
        BarraResumoEtapas.IsIndeterminate = animar && percentual is null;
        BarraResumoEtapas.Value = percentual ?? 0;
        var percentualEstimado = !concluiu && EstimativaAtual is { PercentualGeral: not null } origem && (!origem.PeloEditor || origem.PercentualEstimado);
        TextoPercentual.Text = percentual is { } p ? $"{(percentualEstimado ? "≈ " : "")}{p:0.#}%" : "—";
        BarraResumoEtapas.IsActive = percentual.HasValue || animar;
        TextoPercentual.Visibility = Visivel(percentual.HasValue);
        IconeStatus.Visibility = Visivel(!percentual.HasValue && !animar);
        RotuloEstimado.Visibility = Visivel(percentualEstimado);
        AtualizarContagemResumo();
        ContagemAtividades.Text = $"{feitas} de {etapas.Count} etapas concluídas";
        EstimativaAtividades.Text = $"Progresso total: {TextoPercentual.Text}";
        var origemPercentual = percentual is null ? FluxoNovoRelatorio.PrevisaoDepoisDasEtapas
            : !concluiu && EstimativaAtual is { PercentualGeral: not null } estimativa ? $"Progresso {OrigemDoPercentual(estimativa)}"
            : "Progresso pelas etapas concluídas";
        ToolTipService.SetToolTip(ContagemAtividades, origemPercentual);
        ToolTipService.SetToolTip(TextoPercentual, origemPercentual);
        AutomationProperties.SetHelpText(BarraResumoEtapas, origemPercentual);
        AutomationProperties.SetHelpText(ContagemAtividades, origemPercentual);

        bool mudouQuantidade = linhasEtapas.Count != etapas.Count;
        while (linhasEtapas.Count < etapas.Count)
        {
            var linha = CriarLinhaEtapa();
            PainelEtapas.Children.Add(linha.Trilha);
            EtapasAtividades.Children.Add(linha.Cartao);
            linhasEtapas.Add(linha);
        }
        while (linhasEtapas.Count > etapas.Count)
        {
            int ultimo = linhasEtapas.Count - 1;
            linhasEtapas.RemoveAt(ultimo);
            PainelEtapas.Children.RemoveAt(ultimo);
            EtapasAtividades.Children.RemoveAt(ultimo);
        }
        for (int i = 0; i < etapas.Count; i++)
        {
            var (nome, estado) = etapas[i];
            var linha = linhasEtapas[i];
            var atual = estado == EstadoEtapa.Agora && animar;
            var rotulo = RotuloEtapa(nome, estado, animar);
            var estilo = Estilo(estado switch
            {
                EstadoEtapa.Feita => "MonitorRotuloSucesso",
                EstadoEtapa.Erro => "MonitorRotuloErro",
                _ => atual ? "MonitorRotuloAtivo" : "MonitorLegenda",
            });
            var percentualEtapa = PercentualDaEtapa(nome, estado);
            var descricao = DescricaoDaEtapa(nome);
            linha.Resultado.Text = nome == "Análise" && detalhesEtapas.Grupos.Count > 0
                ? $"{detalhesEtapas.Grupos.Count(g => g.Concluido)} de {detalhesEtapas.Grupos.Count} grupos concluídos"
                : ProgressoAtual is { Total: > 0 } itens && FluxoNovoRelatorio.NomeDaEtapa(itens.Etapa) == nome
                ? $"{itens.Avaliados} de {itens.Total} {(itens.Secoes ? "seções com resultado" : "itens avaliados")}"
                : SituacaoDaEtapa(estado, atual);
            if (linha.EstaAtiva != atual)
            {
                linha.EstaAtiva = atual;
                linha.Expansor.IsExpanded = atual;
            }
            linha.Icone.Glyph = IdDaEtapa(nome) switch
            {
                "prepare" => Glifos.Pasta, "analyze" => Glifos.Pesquisar, "technical_audit" => Glifos.Conferir,
                "audit" => Glifos.MarcasRevisao, "produce" => Glifos.Relatorio, _ => Glifos.Concluido
            };
            linha.Legenda.Text = $"{i + 1:00} {(nome == "Produção do relatório" ? "Relatório" : nome)}";
            linha.Legenda.Style = linha.Selo.Style = estilo;
            linha.Titulo.Text = nome;
            linha.Descricao.Text = descricao;
            linha.MarcaTrilha.Text = estado switch
            {
                EstadoEtapa.Feita => "✓",
                EstadoEtapa.Erro => "!",
                _ => atual ? "●" : "○"
            };
            linha.MarcaTrilha.Style = estilo;
            linha.Selo.Text = SituacaoDaEtapa(estado, atual);
            linha.Percentual.Text = TextoPercentualEtapa(percentualEtapa);
            linha.Cartao.BorderThickness = atual ? new Thickness(2, 0, 0, 1) : new Thickness(0, 0, 0, 1);
            linha.Cartao.BorderBrush = (Brush)Application.Current.Resources[atual ? "PmvniTonalTexto" : "DividerStrokeColorDefaultBrush"];
            PreencherDetalhes(linha, nome);
            if (linha.Executor.Visibility == Visibility.Visible) descricao += " · " + linha.Executor.Text;
            if (linha.Objecao.Visibility == Visibility.Visible) descricao += ". " + linha.Objecao.Text;
            linha.Barra.IsActive = linha.Expansor.IsExpanded && percentualEtapa.Valor.HasValue;
            var valorBarra = percentualEtapa.Valor ?? 0;
            if (linha.Barra.Value != valorBarra) linha.Barra.Value = valorBarra;
            AutomationProperties.SetName(linha.Trilha, $"{nome} · {rotulo}");
            ToolTipService.SetToolTip(linha.Trilha, $"{nome} · {rotulo}");
            AutomationProperties.SetName(linha.Cartao, $"{nome}. {rotulo}. {descricao}");
        }
        if (mudouQuantidade) PosicionarEtapas();
        if (DetalhesVisiveis) AtualizarNarrativa();
    }

    /// <summary>O percentual de uma etapa (3.3.0, pedido do usuário): concluída 100, pendente 0; a atual, o que o Editor calculou para ela
    /// (<see cref="EstimativaAnalise.PercentualEtapa"/>) ou, sem isso, as seções/itens com resultado; null quando não há dado real.
    /// <c>Aproximado</c>: o valor saiu da estimativa inicial (sem histórico neste computador).</summary>
    (double? Valor, bool Aproximado) PercentualDaEtapa(string nome, EstadoEtapa estado)
    {
        if (estado == EstadoEtapa.Feita) return (100, false);
        if (estado == EstadoEtapa.Pendente) return (0, false);
        if (EstimativaAtual is { Etapa: { } etapa, PercentualEtapa: { } percentual } estimativa && FluxoNovoRelatorio.NomeDaEtapa(etapa) == nome)
            return (Math.Clamp(Math.Round(percentual, 1), 0, 99), estimativa.PercentualEstimado);
        if (ProgressoAtual is { Total: > 0 } itens && FluxoNovoRelatorio.NomeDaEtapa(itens.Etapa) == nome)
            return (Math.Clamp(Math.Round(100d * itens.Avaliados / itens.Total), 0, 99), false);
        return (null, false);
    }

    static string TextoPercentualEtapa((double? Valor, bool Aproximado) p) =>
        p.Valor is { } v ? $"{(p.Aproximado ? "≈ " : "")}{v:0.#}%" : "—";

    sealed record LinhaEtapa(Grid Trilha, TextBlock MarcaTrilha, TextBlock Legenda, Border Cartao,
        FontIcon Icone, TextBlock Titulo, TextBlock Selo, TextBlock Descricao, ProgressRing Barra, TextBlock Executor,
        PainelQuebra Frentes, TextBlock Objecao, Expander Expansor, TextBlock Percentual, TextBlock Resultado)
    {
        public bool EstaAtiva { get; set; }
    }
    readonly List<LinhaEtapa> linhasEtapas = [];

    LinhaEtapa CriarLinhaEtapa()
    {
        static Grid ComMarcador(TextBlock marca, FrameworkElement conteudo)
        {
            var grade = new Grid { ColumnSpacing = 6 };
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grade.ColumnDefinitions.Add(new ColumnDefinition());
            Grid.SetColumn(conteudo, 1);
            grade.Children.Add(marca);
            grade.Children.Add(conteudo);
            return grade;
        }
        var marcaTrilha = new TextBlock { FontSize = 16 };
        var legenda = new TextBlock { FontSize = 16, TextWrapping = TextWrapping.Wrap };
        var trilha = ComMarcador(marcaTrilha, legenda);
        var icone = new FontIcon { FontSize = 20, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
        var titulo = new TextBlock { Style = Estilo("MonitorTitulo"), HorizontalAlignment = HorizontalAlignment.Left };
        var selo = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var descricao = new TextBlock { Style = Estilo("MonitorLegenda"), TextWrapping = TextWrapping.Wrap };
        var cabecalho = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 5, 0, 5) };
        cabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cabecalho.ColumnDefinitions.Add(new ColumnDefinition());
        cabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cabecalho.Children.Add(icone);
        var rotulos = new StackPanel { Spacing = 3 };
        rotulos.Children.Add(titulo);
        rotulos.Children.Add(descricao);
        Grid.SetColumn(rotulos, 1);
        Grid.SetColumn(selo, 2);
        cabecalho.Children.Add(rotulos);
        cabecalho.Children.Add(selo);
        var barra = new ProgressRing { Maximum = 100, IsIndeterminate = false, Width = 46, Height = 46 };
        Trabalhando.SetAutomatico(barra, false);
        var percentual = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var anel = new Grid();
        anel.Children.Add(barra);
        anel.Children.Add(percentual);
        var resultado = new TextBlock { Style = Estilo("PmvniAjusteTitulo"), HorizontalAlignment = HorizontalAlignment.Left };
        var leitura = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        leitura.Children.Add(resultado);
        leitura.Children.Add(new TextBlock { Text = "Resultados recebidos nesta etapa", Style = Estilo("MonitorLegenda") });
        var progresso = new Grid { ColumnSpacing = 14 };
        progresso.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progresso.ColumnDefinitions.Add(new ColumnDefinition());
        progresso.Children.Add(anel);
        Grid.SetColumn(leitura, 1);
        progresso.Children.Add(leitura);
        var executor = new TextBlock { Style = Estilo("MonitorLegenda"), Visibility = Visibility.Collapsed, IsTextSelectionEnabled = true };
        var frentes = new PainelQuebra { Espaco = 20, EspacoLinhas = 6, Visibility = Visibility.Collapsed };
        var objecao = new TextBlock { Style = Estilo("MonitorRotuloErro"), TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
        var detalhes = new StackPanel { Spacing = 14 };
        detalhes.Children.Add(progresso);
        detalhes.Children.Add(frentes);
        detalhes.Children.Add(executor);
        detalhes.Children.Add(objecao);
        var expansor = new Expander { Header = cabecalho, Content = detalhes, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderThickness = new Thickness(0),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        expansor.Expanding += (_, _) => barra.IsActive = true;
        expansor.Collapsed += (_, _) => barra.IsActive = false;
        var cartao = new Border { Child = expansor };
        return new(trilha, marcaTrilha, legenda, cartao, icone, titulo, selo, descricao, barra, executor, frentes, objecao,
            expansor, percentual, resultado);
    }

    void PosicionarEtapas()
    {
        var total = PainelEtapas.Children.Count;
        var colunas = Math.Min(total, colunasDaRota);
        PainelEtapas.ColumnDefinitions.Clear();
        PainelEtapas.RowDefinitions.Clear();
        if (colunas == 0) return;
        for (var i = 0; i < colunas; i++) PainelEtapas.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < total; i += colunas) PainelEtapas.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var i = 0; i < total; i++)
        {
            var legenda = (FrameworkElement)PainelEtapas.Children[i];
            Grid.SetRow(legenda, i / colunas);
            Grid.SetColumn(legenda, i % colunas);
        }
    }

    void AjustarLadrilhosDescobertas(object sender, SizeChangedEventArgs e)
    {
        var colunas = e.NewSize.Width >= 350 ? 4 : 2;
        for (var i = 0; i < 4; i++) LadrilhosDescobertas.ColumnDefinitions[i].Width = new GridLength(i < colunas ? 1 : 0, GridUnitType.Star);
        for (var i = 0; i < LadrilhosDescobertas.Children.Count; i++)
        {
            var ladrilho = (FrameworkElement)LadrilhosDescobertas.Children[i];
            Grid.SetRow(ladrilho, i / colunas);
            Grid.SetColumn(ladrilho, i % colunas);
            ladrilho.MinHeight = colunas == 4 ? 108 : 82;
        }
    }

    internal void AjustarColunasExecucao(double largura)
    {
        if (largura <= 0) return;
        var margem = largura >= 1100 ? 32 : 16;
        PainelExecucao.Padding = new Thickness(margem, largura >= 860 ? 24 : 14, margem, 20);
        var compacto = largura < 860;
        var botoesEmbaixo = largura < 970;
        Grid.SetColumnSpan(TituloMonitor, botoesEmbaixo ? 2 : 1);
        var areaDaPagina = largura - 2 * margem - (compacto ? 60 : 182);
        var metricasEmbaixo = areaDaPagina < 710;
        Grid.SetColumnSpan(IdentificacaoResumo, metricasEmbaixo ? 2 : 1);
        Grid.SetRow(MetricasStatus, metricasEmbaixo ? 1 : 0);
        Grid.SetColumn(MetricasStatus, metricasEmbaixo ? 0 : 1);
        Grid.SetColumnSpan(MetricasStatus, metricasEmbaixo ? 2 : 1);
        DivisoriaTempos.Visibility = Visivel(!metricasEmbaixo);
        AbasMonitor.PaneDisplayMode = compacto ? NavigationViewPaneDisplayMode.LeftCompact : NavigationViewPaneDisplayMode.Left;
        var colunas = areaDaPagina >= 790 ? 6 : areaDaPagina >= 470 ? 3 : 2;
        if (colunas != colunasDaRota)
        {
            colunasDaRota = colunas;
            PosicionarEtapas();
        }
    }

    static readonly System.Globalization.CultureInfo Portugues = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");

    Style Estilo(string chave) => (Style)(PainelExecucao.Resources.TryGetValue(chave, out var estilo)
        ? estilo : Application.Current.Resources[chave]);
}
