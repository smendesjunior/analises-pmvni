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
    ScrollViewer? rolagemDasLinhas;

    public PaginaNovoRelatorio()
    {
        InitializeComponent();
        CaixaPedido.PlaceholderText = FluxoNovoRelatorio.DicaPedido;
        TextoPedidoClaude.Text = FluxoNovoRelatorio.TextoPedidoClaude;
        TextoArraste.Text = FluxoNovoRelatorio.ArrasteAqui;
        // Correções 2026-11-15 (image7): a coluna do formulário tem a largura da área visível, menos as margens, até 1400 (centralizada).
        PainelFormulario.SizeChanged += (_, e) => AjustarLarguraDoFormulario(e.NewSize.Width);
        RolagemMonitor.SizeChanged += (_, e) => AjustarColunasExecucao(e.NewSize.Width);
        BotaoEvidenciasPdf.SizeChanged += (_, e) => IconeAbrirEvidencias.Visibility =
            e.NewSize.Width >= 290 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var controle in new FrameworkElement[] { GradeCabecalho, CartaoStatus, PainelClaude, CartaoConsumo,
                     CabecalhoLog, BotaoEvidenciasPdf, ExpansorUltimaMensagem })
            controle.SizeChanged += (_, _) => AjustarAlturaDoMonitor();
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
        PainelFormulario.Visibility = AcoesRetomadaFormulario.Visibility = execucao ? Visibility.Collapsed : Visibility.Visible;
        RolagemMonitor.Visibility = PainelExecucao.Visibility = execucao ? Visibility.Visible : Visibility.Collapsed;
    }

    internal bool VistaDaExecucao => RolagemMonitor.Visibility == Visibility.Visible;

    /// <summary>A lista do andamento está no fim (ou ainda não rola): as linhas novas a mantêm no fim.</summary>
    bool LinhasNoFim()
    {
        rolagemDasLinhas ??= Achar<ScrollViewer>(ListaLinhas);
        return rolagemDasLinhas is null || rolagemDasLinhas.VerticalOffset >= rolagemDasLinhas.ScrollableHeight - 4;
    }

    void RolarLinhasAoFim()
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
        if (linhas is not null) EnfileirarRolagemAoFim(linhas);
    }

    void LinhasMudaram(object? s, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) Espelhar(s, e); else DispatcherQueue.TryEnqueue(() => Espelhar(s, e));
    }

    void Espelhar(object? fonte, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (origem is not { } linhas || !ReferenceEquals(linhas, fonte)) return;
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
        var contagem = atividades.Count == 1 ? "1 atividade registrada" : $"{atividades.Count} atividades registradas";
        var hora = atividades.LastOrDefault(item => item.Atividade.Hora is not null)?.Atividade.HoraTexto ?? "—";
        TextoContagem.Text = $"{contagem} · Última atualização {hora}";
        var status = etapaAtual is null ? contagem : $"Etapa atual: {etapaAtual} · {contagem}";
        TextoEtapaAtual.Text = provedorAtual is null ? status : $"{status} · {provedorAtual}";
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

    internal void MostrarResumo(bool rodando, string? erro, IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas, string? provedor = null)
    {
        var falhou = !string.IsNullOrWhiteSpace(erro);
        var concluida = !falhou && etapas.Count > 0 && etapas.All(x => x.Estado == EstadoEtapa.Feita);
        emCurso = rodando && !falhou && !concluida;
        var atual = etapas.ToList().FindIndex(x => x.Estado is EstadoEtapa.Agora or EstadoEtapa.Erro);
        var temAtual = atual >= 0;
        etapaAtual = temAtual ? etapas[atual].Nome : emCurso ? "Aguardando registro" : null;
        provedorAtual = provedor;
        MarcarEmCurso();

        TituloStatus.Text = falhou ? "Análise com problema" : concluida ? "Análise concluída" : emCurso ? "Análise em andamento" : "Análise parada";
        IconeStatus.Glyph = falhou ? Glifos.Avisos : concluida ? Glifos.Situacao : Glifos.Pausado;
        IconeStatus.Style = Estilo(falhou ? "PmvniIconeSeloErro" : concluida ? "PmvniIconeSeloSucesso" : "PmvniIconeDestaqueStyle");
        FundoIconeStatus.Style = Estilo(falhou ? "MonitorFundoErro" : concluida ? "MonitorFundoSucesso" : "MonitorFundoAtivo");
        AnelStatus.IsActive = emCurso;
        AnelStatus.Visibility = SeloAoVivo.Visibility = SeloAtividades.Visibility = SeloLog.Visibility = emCurso ? Visibility.Visible : Visibility.Collapsed;
        IconeStatus.Visibility = emCurso ? Visibility.Collapsed : Visibility.Visible;
        BotaoPausarAnalise.Visibility = BotaoCancelarAnalise.Visibility = rodando ? Visibility.Visible : Visibility.Collapsed;

        MostrarEtapas(etapas, emCurso);

        var proxima = temAtual && atual + 1 < etapas.Count ? etapas[atual + 1].Nome : null;
        TextoResumo.Text = falhou ? "A execução encontrou um problema." : concluida ? "Relatório pronto para importar."
            : emCurso ? etapaAtual! : "A análise não está mais rodando.";
        TextoProximaEtapa.Text = falhou ? erro!.Trim()
            : concluida ? "Todas as etapas terminaram."
            : !emCurso ? "Execução encerrada."
            : !temAtual ? "Aguardando confirmação de progresso."
            : proxima is not null ? $"Próxima etapa: {proxima}."
            : "Última etapa em andamento.";
        CartaoProximaEtapa.Style = Estilo(falhou ? "MonitorFundoErro" : "MonitorFundoAtivo");
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

    internal void MostrarRelogios(TimeSpan decorrido, DateTimeOffset agora)
    {
        TextoTempoDecorrido.Text = FluxoNovoRelatorio.TempoLegivel(decorrido, incluirSegundos: true);
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
        var termino = EstimativaAtual?.Termino?.ToLocalTime();
        var peloEditor = EstimativaAtual is not { PeloEditor: false };
        var semDados = EstimativaAtual is null or { SemDados: true };
        TextoTempoEstimado.Text = FluxoNovoRelatorio.PrevisaoCurta(termino, agora, restantePeloRitmo, peloEditor, semDados,
            aproximada: EstimativaAtual?.Aproximada == true);
        var detalhes = FluxoNovoRelatorio.TextoEstimativa(termino, agora, restantePeloRitmo, peloEditor, semDados);
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
    static readonly IReadOnlyDictionary<string, (string Texto, string Cor)> Situacoes = Pmvni.Composicao.Kit.Situacoes.Sit;

    void FiltrarDescobertas(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioMenuFlyoutItem item) return;
        filtroDescobertas = item.Tag as string;
        AtualizarListaDescobertas();
        ColunaLateral.ChangeView(null, 0, null, true);
    }

    internal void ReiniciarFiltroDescobertas()
    {
        if (filtroDescobertas is null) return;
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
        for (var i = 0; i < visiveis.Count; i++)
        {
            var a = visiveis[i];
            if (i < ListaDescobertas.Children.Count && ListaDescobertas.Children[i] is Border existente && Equals(existente.Tag, a)) continue;
            if (i < ListaDescobertas.Children.Count) ListaDescobertas.Children.RemoveAt(i);
            ListaDescobertas.Children.Insert(i, CartaoDescoberta(a));
        }
        while (ListaDescobertas.Children.Count > visiveis.Count)
            ListaDescobertas.Children.RemoveAt(ListaDescobertas.Children.Count - 1);
        VazioDescobertas.Visibility = visiveis.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ColunaLateral.Visibility = visiveis.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        TextoSemDescobertas.Text = filtro is null ? "Aguardando primeiras descobertas" : "Nenhuma descoberta nesta classificação";
        TextoAjudaDescobertas.Text = filtro is null
            ? "As constatações aparecerão aqui, com classificação, referência ao PDF e página e trechos da evidência."
            : "Altere o filtro para ver as demais classificações.";
        TextoFiltro.Text = $"Exibição: {filtro?.ToLower(Portugues) ?? "todas as classificações"}";
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
        cabecalho.Children.Add(classificacao);
        cabecalho.Children.Add(selo);
        var textos = new StackPanel { Spacing = 2 };
        textos.Children.Add(cabecalho);
        textos.Children.Add(titulo);
        return new Border
        {
            Tag = achado, CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 6, 8, 6),
            Style = Estilo("PmvniCartaoCompactoStyle"), Child = textos,
        };
    }

    IReadOnlyList<UsoEtapa>? consumoExibido;

    /// <summary>A última etapa registrada, com a tabela de uso do kit disponível em "Todas as etapas".</summary>
    internal void MostrarConsumo(IReadOnlyList<UsoEtapa> uso)
    {
        if (consumoExibido is not null && consumoExibido.SequenceEqual(uso)) return;
        consumoExibido = uso;
        GradeConsumo.Children.Clear();
        GradeConsumo.RowDefinitions.Clear();
        GradeConsumo.ColumnDefinitions.Clear();

        string Tempo(UsoEtapa u) => u.Duracao is { } duracao ? FluxoNovoRelatorio.TempoLegivel(duracao, incluirSegundos: true) : TextoConsumo.Desconhecido;
        var recente = uso.LastOrDefault();
        TextoEtapaConsumo.Text = recente is null ? TextoConsumo.Desconhecido : FluxoNovoRelatorio.NomeDaEtapa(recente.Etapa);
        TextoTokensConsumo.Text = recente is null ? "— / —" : TextoConsumo.Tokens(recente);
        TextoCacheConsumo.Text = TextoConsumo.Numero(recente?.CacheLido);
        TextoExecutorConsumo.Text = recente is null ? "Nenhum uso registrado ainda"
            : $"{Tempo(recente)} · {recente.Executor}" + (string.IsNullOrWhiteSpace(recente.Modelo) ? "" : $" · {recente.Modelo}");
        ExpansorConsumo.IsEnabled = recente is not null;
        if (recente is null)
        {
            ExpansorConsumo.IsExpanded = false;
            return;
        }

        for (var c = 0; c < 6; c++) GradeConsumo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        void Celula(FrameworkElement e, int linha, int coluna)
        {
            Grid.SetRow(e, linha);
            Grid.SetColumn(e, coluna);
            GradeConsumo.Children.Add(e);
        }
        TextBlock Texto(string t, bool cabecalho = false) => new()
        {
            Text = t, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = !cabecalho,
            Style = Estilo(cabecalho ? "PmvniPainelRotulo" : "CaptionTextBlockStyle"),
        };
        GradeConsumo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] cabecalhos = ["Etapa", "Quem", "Tokens (entr. / saída)", "Cache", "Tempo", "Cota 5 h · semana"];
        for (var c = 0; c < cabecalhos.Length; c++) Celula(Texto(cabecalhos[c], cabecalho: true), 0, c);
        for (var i = 0; i < uso.Count; i++)
        {
            var u = uso[i];
            var linha = i + 1;
            GradeConsumo.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Celula(Texto(FluxoNovoRelatorio.NomeDaEtapa(u.Etapa)), linha, 0);
            var selo = new Border
            {
                CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Style = Estilo(u.Executor == UsoDasIas.Programa ? "MonitorLadrilho" : "MonitorFundoModelo"),
                Child = Texto(u.Executor),
            };
            if (!string.IsNullOrWhiteSpace(u.Modelo)) ToolTipService.SetToolTip(selo, u.Modelo);
            Celula(selo, linha, 1);
            Celula(Texto(TextoConsumo.Tokens(u)), linha, 2);
            Celula(Texto(TextoConsumo.Numero(u.CacheLido)), linha, 3);
            Celula(Texto(Tempo(u)), linha, 4);
            Celula(Texto(TextoConsumo.Cota(u)), linha, 5);
        }
    }

    static string SituacaoDaEtapa(EstadoEtapa estado, bool ativa) => estado switch
    {
        EstadoEtapa.Feita => "Concluída",
        EstadoEtapa.Agora => ativa ? "Em andamento" : "Interrompida",
        EstadoEtapa.Erro => "Com erro",
        _ => "Pendente",
    };

    static string OrigemDoPercentual(EstimativaAnalise estimativa) => estimativa.PeloEditor ? "calculado pelo Editor" : "estimado pela IA";

    internal string RotuloEtapa(string nome, EstadoEtapa estado, bool ativa)
    {
        var status = SituacaoDaEtapa(estado, ativa);
        if (estado == EstadoEtapa.Feita) return status + " · 100%";
        if (estado == EstadoEtapa.Pendente) return status;
        if (EstimativaAtual is { Etapa: { } etapa, PercentualEtapa: { } percentual } estimativa && FluxoNovoRelatorio.NomeDaEtapa(etapa) == nome)
            return $"{status} · {percentual:0.#}% {OrigemDoPercentual(estimativa)}";
        if (ProgressoAtual is { Total: > 0 } itens && FluxoNovoRelatorio.NomeDaEtapa(itens.Etapa) == nome)
            return $"{status} · {Math.Clamp(100d * itens.Avaliados / itens.Total, 0, 100):0.#}% "
                + (itens.Secoes ? "das seções com resultado" : "dos itens avaliados");
        return status;
    }

    static string DescricaoDaEtapa(string nome) => nome switch
    {
        "Preparo" => "Identificar pranchas e organizar os arquivos do processo",
        "Análise" => "Conferir itens, registrar conclusões e evidências",
        "Auditoria técnica" => "Revisar cálculos, referências e pendências",
        "Auditoria cruzada" => "Confrontar as conclusões e localizar divergências",
        "Produção do relatório" => "Organizar constatações e redigir exigências",
        "Revisão final" => "Conferir a versão consolidada e o arquivo final",
        _ => "Etapa definida pelo kit PMVNI instalado.",
    };

    internal void MostrarEtapas(IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas, bool animar)
    {
        if (execucaoAtivaNaBarra == animar && progressoDesenhado == ProgressoAtual
            && estimativaDesenhada == EstimativaAtual && etapasMostradas.SequenceEqual(etapas)) return;
        execucaoAtivaNaBarra = animar;
        progressoDesenhado = ProgressoAtual;
        estimativaDesenhada = EstimativaAtual;
        etapasMostradas = etapas.ToArray();

        var feitas = etapas.Count(e => e.Estado == EstadoEtapa.Feita);
        var concluiu = etapas.Count > 0 && feitas == etapas.Count;
        double? percentualPorEtapas = etapas.Count > 0 ? 100d * feitas / etapas.Count : null;
        var percentual = concluiu ? 100d : EstimativaAtual?.PercentualGeral ?? percentualPorEtapas;
        if (percentual is { } valor) percentual = Math.Clamp(valor, 0, 100);
        BarraResumoEtapas.IsIndeterminate = animar && percentual is null;
        BarraResumoEtapas.Value = percentual ?? 0;
        TextoPercentual.Text = percentual is { } p ? $"{p:0}%" : "—";
        TextoTotalEtapas.Text = $"{feitas} de {etapas.Count} etapas concluídas";
        TextoEtapasStatus.Text = $"{feitas} / {etapas.Count} concluídas";
        ContagemAtividades.Text = $"{feitas} / {etapas.Count} · {TextoPercentual.Text}";
        var origemPercentual = percentual is null ? FluxoNovoRelatorio.PrevisaoDepoisDasEtapas
            : !concluiu && EstimativaAtual is { PercentualGeral: not null } estimativa ? $"Progresso {OrigemDoPercentual(estimativa)}"
            : "Progresso pelas etapas concluídas";
        ToolTipService.SetToolTip(ContagemAtividades, origemPercentual);
        ToolTipService.SetToolTip(TextoPercentual, origemPercentual);
        AutomationProperties.SetHelpText(BarraResumoEtapas, origemPercentual);
        AutomationProperties.SetHelpText(ContagemAtividades, origemPercentual);

        EtapasAtividades.Children.Clear();
        PainelEtapas.Children.Clear();
        for (var i = 0; i < etapas.Count; i++)
        {
            var (nome, estado) = etapas[i];
            var atual = estado == EstadoEtapa.Agora && animar;
            var rotulo = RotuloEtapa(nome, estado, animar);
            var estilo = Estilo(estado switch
            {
                EstadoEtapa.Feita => "MonitorRotuloSucesso",
                EstadoEtapa.Erro => "MonitorRotuloErro",
                _ => atual ? "MonitorRotuloAtivo" : "MonitorLegenda",
            });
            var nomeCurto = nome == "Produção do relatório" ? "Relatório" : nome;
            var legenda = new TextBlock
            {
                Text = $"{i + 1:00} {nomeCurto}", Style = estilo,
            };
            var trilha = new Grid { ColumnSpacing = 5, VerticalAlignment = VerticalAlignment.Top };
            trilha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            trilha.ColumnDefinitions.Add(new ColumnDefinition());
            var ponto = new TextBlock { Text = "●", Style = estilo, VerticalAlignment = VerticalAlignment.Top };
            AutomationProperties.SetAccessibilityView(ponto, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            trilha.Children.Add(ponto);
            Grid.SetColumn(legenda, 1);
            trilha.Children.Add(legenda);
            ToolTipService.SetToolTip(trilha, $"{nome} · {rotulo}");
            AutomationProperties.SetName(trilha, $"{nome} · {rotulo}");
            PainelEtapas.Children.Add(trilha);

            var descricao = DescricaoDaEtapa(nome);
            if (atual && ProgressoAtual is { Total: > 0 } itens && FluxoNovoRelatorio.NomeDaEtapa(itens.Etapa) == nome)
                descricao += itens.Secoes ? $" · {itens.Avaliados} de {itens.Total} seções com resultado" : $" · {itens.Avaliados} de {itens.Total} itens avaliados";
            EtapasAtividades.Children.Add(CartaoDaEtapa(nome, descricao, estado, atual, estilo, rotulo));
        }
        PosicionarEtapas();
    }

    Border CartaoDaEtapa(string nome, string descricao, EstadoEtapa estado, bool atual, Style estilo, string rotulo)
    {
        FrameworkElement marcador = estado switch
        {
            EstadoEtapa.Feita => new FontIcon { Glyph = Glifos.Concluido, FontSize = 12, Style = Estilo("PmvniIconeSeloSucesso") },
            EstadoEtapa.Erro => new FontIcon { Glyph = Glifos.Avisos, FontSize = 12, Style = Estilo("PmvniIconeSeloErro") },
            _ => new TextBlock { Text = atual ? "●" : "○", Style = estilo },
        };
        marcador.Margin = new Thickness(0, 2, 0, 0);
        marcador.VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetAccessibilityView(marcador, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);

        var titulo = new TextBlock { Text = nome, Style = Estilo("PmvniAjusteTitulo") };
        var cabecalho = new Grid { ColumnSpacing = 5 };
        cabecalho.ColumnDefinitions.Add(new ColumnDefinition());
        cabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        cabecalho.Children.Add(titulo);
        if (estado != EstadoEtapa.Pendente)
        {
            var selo = new TextBlock
            {
                Text = SituacaoDaEtapa(estado, atual).ToUpper(Portugues), Style = estilo,
            };
            Grid.SetColumn(selo, 1);
            cabecalho.Children.Add(selo);
        }
        var textos = new StackPanel { Spacing = 2 };
        textos.Children.Add(cabecalho);
        textos.Children.Add(new TextBlock { Text = descricao, Style = Estilo("PmvniSecondaryTextStyle"), TextWrapping = TextWrapping.Wrap });
        var grade = new Grid { ColumnSpacing = 8 };
        grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grade.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(textos, 1);
        grade.Children.Add(marcador);
        grade.Children.Add(textos);
        var cartao = new Border
        {
            CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 5, 8, 5), MinHeight = 47, Child = grade,
            Style = atual ? Estilo("MonitorFundoAtivo") : estado == EstadoEtapa.Erro ? Estilo("MonitorFundoErro") : null,
        };
        ToolTipService.SetToolTip(cartao, $"{nome} · {rotulo}");
        AutomationProperties.SetName(cartao, $"{nome}. {rotulo}. {descricao}");
        return cartao;
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
            ladrilho.MinHeight = colunas == 4 ? 86 : 68;
        }
    }

    const double LarguraTresColunas = 1110;
    const double LarguraDuasColunas = 720;
    const double AlturaAtividadesEmpilhadas = 290;
    const double AlturaDescobertasEmpilhadas = 365;
    const double AlturaAtividadesEDescobertas = 330;

    /// <summary>Empilha os cartões e move as ações do cabeçalho conforme a largura útil do Monitor.</summary>
    internal void AjustarColunasExecucao(double largura)
    {
        if (largura <= 0) return;
        var ampla = largura >= LarguraTresColunas;
        var estreita = largura < LarguraDuasColunas;
        PainelExecucao.Padding = largura >= 1400 ? new Thickness(30, 16, 30, 12) : new Thickness(16, 8, 16, 10);
        var espacosa = RolagemMonitor.ActualHeight >= 680;
        GradeCabecalho.MinHeight = espacosa ? 68 : 52;
        CartaoStatus.MinHeight = espacosa ? 78 : 68;
        PainelClaude.MinHeight = espacosa ? 122 : 96;
        GradeConteudo.ColumnSpacing = estreita ? 0 : 12;
        GradeConteudo.RowSpacing = ampla ? 0 : 10;
        GradeConteudo.ColumnDefinitions[1].Width = new GridLength(estreita ? 0 : 13.5, GridUnitType.Star);
        GradeConteudo.ColumnDefinitions[2].Width = new GridLength(ampla ? 10 : 0, GridUnitType.Star);
        GradeConteudo.RowDefinitions[0].Height = ampla ? new GridLength(1, GridUnitType.Star)
            : new GridLength(estreita ? AlturaAtividadesEmpilhadas : AlturaAtividadesEDescobertas);
        GradeConteudo.RowDefinitions[1].Height = ampla ? new GridLength(0)
            : estreita ? new GridLength(AlturaDescobertasEmpilhadas) : new GridLength(1, GridUnitType.Star);
        GradeConteudo.RowDefinitions[2].Height = estreita ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(CartaoDescobertas, estreita ? 0 : 1);
        Grid.SetRow(CartaoDescobertas, estreita ? 1 : 0);
        Grid.SetColumn(ColunaDetalhes, ampla ? 2 : 0);
        Grid.SetColumnSpan(ColunaDetalhes, ampla || estreita ? 1 : 2);
        Grid.SetRow(ColunaDetalhes, ampla ? 0 : estreita ? 2 : 1);

        var botoesEmbaixo = largura < 1080;
        var identificacaoEmbaixo = largura < 540;
        IdentificacaoMonitor.MaxWidth = largura is >= 1080 and < 1250 ? 250 : 340;
        Grid.SetRow(IdentificacaoMonitor, identificacaoEmbaixo ? 1 : 0);
        Grid.SetColumn(IdentificacaoMonitor, identificacaoEmbaixo ? 0 : 1);
        Grid.SetColumnSpan(IdentificacaoMonitor, identificacaoEmbaixo ? 3 : 1);
        IdentificacaoMonitor.HorizontalAlignment = identificacaoEmbaixo ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        SeparadorCabecalho.Visibility = botoesEmbaixo ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(BotoesExecucao, identificacaoEmbaixo ? 2 : botoesEmbaixo ? 1 : 0);
        Grid.SetColumn(BotoesExecucao, botoesEmbaixo ? 0 : 2);
        Grid.SetColumnSpan(BotoesExecucao, botoesEmbaixo ? 3 : 1);
        GradeCabecalho.RowSpacing = botoesEmbaixo ? 6 : 0;

        var metricasEmbaixo = largura < 740;
        Grid.SetRow(MetricasStatus, metricasEmbaixo ? 1 : 0);
        Grid.SetColumn(MetricasStatus, metricasEmbaixo ? 0 : 1);
        Grid.SetColumnSpan(MetricasStatus, metricasEmbaixo ? 2 : 1);
        MetricasStatus.Margin = metricasEmbaixo ? new Thickness(0, 7, 0, 0) : new Thickness(0);

        var colunas = largura >= 790 ? 6 : largura >= 470 ? 3 : 2;
        if (colunas != colunasDaRota)
        {
            colunasDaRota = colunas;
            PosicionarEtapas();
        }
        AjustarAlturaDoMonitor();
    }

    void AjustarAlturaDoMonitor()
    {
        if (!VistaDaExecucao || RolagemMonitor.ActualHeight <= 0) return;
        var alturaTopo = GradeCabecalho.ActualHeight + CartaoStatus.ActualHeight + PainelClaude.ActualHeight
            + PainelExecucao.Padding.Top + PainelExecucao.Padding.Bottom + 3 * PainelExecucao.RowSpacing;
        // Reserva uma linha e seus controles no log. O restante continua rolando dentro de cada cartão.
        const double linhaDoLogMaisMargens = 78;
        var alturaDetalhes = CartaoConsumo.ActualHeight + ColunaDetalhes.RowSpacing + CabecalhoLog.ActualHeight
            + ExpansorUltimaMensagem.ActualHeight
            + (BotaoEvidenciasPdf.Visibility == Visibility.Visible ? BotaoEvidenciasPdf.ActualHeight : 0) + linhaDoLogMaisMargens;
        var largura = RolagemMonitor.ActualWidth;
        var alturaColunas = largura >= LarguraTresColunas ? Math.Max(305, alturaDetalhes)
            : alturaDetalhes + (largura < LarguraDuasColunas ? AlturaAtividadesEmpilhadas + AlturaDescobertasEmpilhadas
                + 2 * GradeConteudo.RowSpacing : AlturaAtividadesEDescobertas + 2 * GradeConteudo.RowSpacing);
        var altura = Math.Ceiling(Math.Max(RolagemMonitor.ActualHeight, alturaTopo + alturaColunas));
        if (PainelExecucao.Height != altura) PainelExecucao.Height = altura;
    }

    static readonly System.Globalization.CultureInfo Portugues = System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
    Style Estilo(string chave) => (Style)(PainelExecucao.Resources.TryGetValue(chave, out var estilo)
        ? estilo : Application.Current.Resources[chave]);
}
