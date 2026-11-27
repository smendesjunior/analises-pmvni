using System.Globalization;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Agentes;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Pmvni.App;

/// <summary>Novo relatório pelos agentes locais (Tarefa 20; desenho §12.2–§12.7, §12.11): "Novo relatório" na faixa de Relatórios e de Processos abre a página
/// Novo relatório no lugar das folhas (processo, anexos por botão ou arrastando, pedido, "Analisar com o ChatGPT"/"Analisar com o Claude"), o aviso único por
/// provedor, a vista da execução (andamento, cancelar, registro), as execuções em segundo plano (pertencem à janela; o título mostra a mais antiga), o fechamento
/// com análises e a retomada do Claude na abertura. O editor só inicia o agente do computador e importa o relatório que o kit declara. Analisar entra na fila de
/// comandos só até a partida: a análise em andamento nunca prende a fila.</summary>
public sealed partial class MainWindow
{
    /// <summary>Um relatório da IA que ainda não foi importado: a análise do Claude que terminou com o editor fechado (sem <see cref="Documento"/>: procurado no
    /// <c>delivery.json</c>) ou uma importação que falhou ou foi cancelada (com o documento já localizado). Sai da lista só depois de importado.</summary>
    internal sealed record ImportacaoPendente(Provedor Provedor, Processo Processo, string Registro, string? Documento, string? Pdf,
        bool ParaRevisao = false);
    sealed record AcaoEntrega(ImportacaoPendente Pendente, bool Corrigir);

    /// <summary>Uma linha da lista de anexos (nada é gravado antes de Analisar).</summary>
    sealed class AnexoNovo(string origem, long tamanho, DestinoAnexo destino, string? compactado = null, string? interno = null)
    {
        public string Origem { get; } = origem;
        /// <summary>O compactado de onde o arquivo saiu (.zip, .rar, .7z) e o caminho dentro dele; null num anexo comum.</summary>
        public string? Compactado { get; } = compactado;
        public string? Interno { get; } = interno;
        public string Nome => Path.GetFileName(Origem);
        public long Tamanho { get; } = tamanho;
        public DestinoAnexo Destino { get; set; } = destino;
        /// <summary>O usuário escolheu o destino (trocar de processo não o muda).</summary>
        public bool Escolhido { get; set; }
        public string Previa { get; set; } = "";
    }

    SessaoNovoRelatorio sessaoNR = null!;
    bool novoRelatorioAberto, partindoNR, preenchendoNR;
    ExecucaoEmCurso? execucaoMostrada, ultimoResultadoNR;
    IReadOnlyList<Processo> processosNR = [];
    Processo? processoNR;
    string? revisaoAtualNR;
    readonly List<AnexoNovo> anexosNR = [];
    readonly List<string> recusasNR = [];
    /// <summary>A pasta temporária desta janela onde os compactados anexados são abertos (apagada ao fechar; sobras de mais de 1 dia, ao abrir outro).</summary>
    string? pastaCompactadosNR;
    readonly List<ImportacaoPendente> importacoesPendentes = [];
    Task tarefaListaNR = Task.CompletedTask, tarefaPreviaNR = Task.CompletedTask;
    int sequenciaPreviaNR;
    DispatcherQueueTimer relogioAnalise = null!;
    /// <summary>Estados da pílula da análise no cabeçalho (redesign 2026-11-17). Inativa = sem análise (a pílula some).</summary>
    internal enum EstadoPilulaAnalise { Inativa, EmAndamento, Concluida, Pausada, Erro }
    EstadoPilulaAnalise estadoPilula;
    /// <summary>O fim mais recente, mostrado na pílula até o usuário clicar nela ou outra análise começar (a concluída sai sozinha).</summary>
    (EstadoPilulaAnalise Estado, string Detalhe, string? Mensagem)? fimNaPilula;
    DispatcherQueueTimer? relogioFimPilula;
    internal EstadoPilulaAnalise EstadoDaPilulaAnalise => estadoPilula;

    PaginaNovoRelatorio NR => PaginaDeNovoRelatorio;

    // ---------- acessores do roteiro ----------
    internal PaginaNovoRelatorio NovoRelatorio => PaginaDeNovoRelatorio;
    internal bool PaginaNovoRelatorioVisivel => PaginaDeNovoRelatorio.Visibility == Visibility.Visible;
    internal bool FolhasVisiveis => R.Visibility == Visibility.Visible;
    /// <summary>Quem inicia os agentes (o roteiro põe o cenário do agente falso e intervalos curtos).</summary>
    internal OrquestradorAnalise Orquestrador { get; set; } = new();
    internal Processo? ProcessoDoNovoRelatorio => processoNR;
    internal IReadOnlyList<Processo> ProcessosDoNovoRelatorio => processosNR;
    internal IReadOnlyList<(string Origem, DestinoAnexo Destino, string Previa)> AnexosDoNovoRelatorio => anexosNR.Select(a => (a.Origem, a.Destino, a.Previa)).ToList();
    internal IReadOnlyList<string> RecusasDoNovoRelatorio => recusasNR;
    /// <summary>Os rótulos da caixa Destino de uma linha, como a tela os mostra.</summary>
    internal IReadOnlyList<string> OpcoesDeDestino => FluxoNovoRelatorio.OpcoesDestino(revisaoAtualNR).Select(o => o.Rotulo).ToList();
    internal IReadOnlyList<ExecucaoEmCurso> ExecucoesEmCurso => sessaoNR.Execucoes;
    internal ExecucaoEmCurso? ExecucaoMostrada => execucaoMostrada;
    internal IReadOnlyList<ExecucaoClaudePendente> ExecucoesClaudePendentes => ajustes.ExecucoesClaude;
    internal IReadOnlyList<ImportacaoPendente> PendentesParaImportar => importacoesPendentes;
    internal bool BotaoAnaliseHabilitado(Provedor p) => BotaoDoProvedor(p).IsEnabled;

    Button BotaoDoProvedor(Provedor p) => p switch { Provedor.ChatGpt => NR.BotaoChatGpt, Provedor.Claude => NR.BotaoClaude, _ => NR.BotaoGemini };

    /// <summary>O provedor do kit dono de um registro guardado nos ajustes (<c>provedor</c> do <c>execucao.json</c>; sem ele, o sufixo da pasta): Gemini ou,
    /// em qualquer outro caso, Claude (como sempre foi).</summary>
    static Provedor ProvedorDoRegistro(string pastaRegistro)
    {
        try
        {
            if (System.Text.Json.Nodes.JsonNode.Parse(RegistroExecucao.LerTextoDaExecucao(pastaRegistro))?["provedor"] is System.Text.Json.Nodes.JsonValue v
                && v.TryGetValue<string>(out var nome))
                return nome == Provedores.Sufixo(Provedor.Gemini) ? Provedor.Gemini : Provedor.Claude;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { }
        return Path.GetFileName(pastaRegistro.TrimEnd('\\', '/')).EndsWith("-" + Provedores.Sufixo(Provedor.Gemini), StringComparison.OrdinalIgnoreCase) ? Provedor.Gemini : Provedor.Claude;
    }

    bool RoteiroLegado =>
#if PMVNI_DISTRIBUICAO
        false;
#else
        Argumentos.Atual.Roteiro is not null && TravaDeProgramas.Permitidos is not null;
#endif
    const string SemPluginClaude = "O plugin específico de análise para o Claude ainda está indisponível.";
    const string RetomadaLegada = "Esta execução pertence a um fluxo aposentado. O registro continua no histórico. Para analisar as peças atuais, abra Novo relatório.";

    async Task<ExecucaoEmCurso?> AnalisarComPerfilAsync(Provedor p, Processo processo, IReadOnlyList<(string Origem, DestinoAnexo Destino)> anexos, string? texto, IReadOnlyList<AnexoNovo>? enviados, string? registroAnterior = null, bool corrigirEntrega = false)
    {
        if (!Provedores.Ativos.Contains(p)) return null;
        if (!RoteiroLegado)
        {
            if (p != Provedor.ChatGpt || (registroAnterior is not null && !OrquestradorAnalise.RegistroAnaliseChatGpt(registroAnterior)))
            {
                MostrarMensagem(InfoBarSeverity.Informational, "Análise indisponível", registroAnterior is not null ? RetomadaLegada : SemPluginClaude);
                return null;
            }
            return await PartirAsync(() => sessaoNR.AnalisarAsync(p, processo, anexos, texto,
                registroAnterior: registroAnterior, pastaQgis: ajustes.PastaQgis, pastaKit: ajustes.PastaKit, usarAnaliseChatGpt: true), enviados);
        }
        var pastaKitAnalise = ajustes.PastaKitEfetiva;
        if (Argumentos.Atual.Roteiro is null || OrigensDoKitDoRoteiro is not null)
        {
            var origens = OrigensDoKitDoComputador();
            var leitura = await ComponentesDoKit.LerAsync(origens, pastaKitAnalise: pastaKitAnalise);
            var divergentes = leitura.Linhas.Where(l => l.Situacao != SituacaoComponente.EmDia).ToList();
            if (divergentes.Count > 0)
            {
                var detalhes = string.Join(Environment.NewLine, divergentes.Select(l => l.Componente.Nome + " · " + l.Detalhe));
                if (!await Perguntas.Atual.ConfirmarAsync("O kit precisa de atualização", detalhes,
                        "Atualizar kit e continuar", "Cancelar", destrutiva: false)) return null;
                await AtualizarComponenteAsync(null);
                leitura = await ComponentesDoKit.LerAsync(origens, pastaKitAnalise: pastaKitAnalise);
                if (leitura.Linhas.Any(l => l.Situacao != SituacaoComponente.EmDia))
                {
                    MostrarMensagem(InfoBarSeverity.Warning, "Componentes do kit", "A atualização ainda tem pendências. Confira as linhas em Configurações.");
                    return null;
                }
            }
        }
        ModoAcesso? modoOriginal = null;
        string? rotaOriginal = null;
        if (corrigirEntrega && registroAnterior is not null)
        {
            using var original = RegistroExecucao.Abrir(registroAnterior, processo.Pasta);
            var acesso = original.AcessoDaExecucao();
            modoOriginal = acesso.Modo switch { "conta" => ModoAcesso.Conta, "conta_gemini" => ModoAcesso.ContaGemini, "chave" => ModoAcesso.Chave, _ => null };
            rotaOriginal = acesso.RotaCodex;
        }
        RecarregarAjusteDosModelos();
        if (erroAjusteModelos is { } erro)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Modelos da análise", erro);
            return null;
        }
        TabelaDeModelos? tabela;
        try { tabela = TabelaParaAnalise(p); }
        catch (InvalidDataException ex)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Modelos da análise", ex.Message);
            return null;
        }
        return await PartirAsync(() => sessaoNR.AnalisarAsync(p, processo, anexos, texto, registroAnterior: registroAnterior,
            modelos: tabela, pastaQgis: ajustes.PastaQgis, pastaKit: pastaKitAnalise, modoOriginal: modoOriginal, rotaOriginal: rotaOriginal), enviados);
    }
    internal string? AvisoVersaoDoAgente(Provedor p) => ajustes.Agentes[p].AvisoVersao?.ToString(CultureInfo.InvariantCulture);
    internal void EsquecerAviso(Provedor p) { ajustes.Agentes[p].AvisoVersao = null; ajustes.Agentes[p].AvisoEm = null; SalvarAjustes(); }
    /// <summary>A pílula do título mostra uma análise em andamento (o fim fica um tempo na pílula, mas como concluída, pausada ou com erro).</summary>
    internal bool BotaoDoTituloVisivel => BotaoAnalise.Visibility == Visibility.Visible && estadoPilula == EstadoPilulaAnalise.EmAndamento;

    /// <summary>Roteiro: começa sem análises do Claude pendentes de uma rodada anterior.</summary>
    internal void EsquecerExecucoesClaude()
    {
        ajustes.ExecucoesClaude.Clear();
        importacoesPendentes.Clear();
        SalvarAjustes();
    }

    /// <summary>Roteiro: um item de <c>execucoes_claude</c> como se o <c>ajustes.json</c> tivesse sido editado à mão.</summary>
    internal void AdicionarPendenteClaude(ExecucaoClaudePendente pend)
    {
        ajustes.ExecucoesClaude.Add(pend);
        SalvarAjustes();
    }

    /// <summary>Relê as peças (Codex, Python, kit) e atualiza os botões "Analisar".</summary>
    internal async Task AtualizarPecasNovoRelatorioAsync()
    {
        await LerPecasAsync();
        AtualizarBotoesNR();
    }

    internal string PedidoTexto
    {
        get => NR.CaixaPedido.Text;
        set => NR.CaixaPedido.Text = value;
    }

    /// <summary>Espera a leitura da lista de processos e a prévia dos anexos que estão em curso.</summary>
    internal async Task AguardarNovoRelatorioAsync()
    {
        await tarefaListaNR;
        await tarefaPreviaNR;
    }

    void IniciarNovoRelatorio()
    {
        sessaoNR = new SessaoNovoRelatorio(DispatcherQueue.GetForCurrentThread(), () => Orquestrador, () => pecasAgentes, p => ajustes.Agentes[p].Modo, PastaDados.Raiz, () => ajustes.RotaCodex,
            DecidirAsync);
        sessaoNR.Atualizada += e => { if (ReferenceEquals(e, execucaoMostrada)) RenderizarExecucao(e); };
        manipuladores[ComandosApp.RelatoriosNovo] = async () => await AbrirNovoRelatorioNaVezAsync();

        var n = NR;
        if (!RoteiroLegado) n.CartaoClaudeIndisponivel.Opacity = .68;
        n.EvidenciasSolicitadas += (_, _) => AbrirEvidenciasDoMonitor();
        n.ParcelaSolicitada += (evidencia, recorte) =>
        {
            if (execucaoMostrada is { } e) ExibirEvidenciaDaParcela(e.Processo.Pasta, e.Registro.Pasta, evidencia, recorte);
        };
        n.CaixaFiltro.TextChanged += (_, e) =>
        {
            PreencherProcessosNR();
            n.CaixaFiltro.IsSuggestionListOpen = e.Reason == AutoSuggestionBoxTextChangeReason.UserInput
                && n.CaixaFiltro.ItemsSource is SugestaoProcesso[] { Length: > 0 } && n.CaixaFiltro.Text.Trim().Length > 0;
        };
        n.CaixaFiltro.QuerySubmitted += async (_, e) =>
        {
            if (e.ChosenSuggestion is SugestaoProcesso selecionada)
                await TentarAsync(() => EscolherProcessoNovoRelatorioAsync(selecionada.Processo));
        };
        n.CaixaProcesso.SelectionChanged += (_, _) =>
        {
            if (!preenchendoNR && n.CaixaProcesso.SelectedItem is ComboBoxItem { Tag: Processo p }) _ = TentarAsync(() => EscolherProcessoNovoRelatorioAsync(p));
        };
        n.BotaoCriarProcesso.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(async () => await CriarProcessoNaVezAsync()));
        MontarCategoriasDeAnexos();
        n.CaixaPedido.TextChanged += (_, _) => { };
        n.BotaoChatGpt.Click += async (_, _) => await AnalisarAsync(Provedor.ChatGpt);
        n.BotaoClaude.Click += async (_, _) => await AnalisarAsync(Provedor.Claude);
        n.BotaoVoltarRelatorio.Click += (_, _) => FecharPaginaNovoRelatorio();
        n.BotaoExecucoes.Click += (_, _) => { if (sessaoNR.Execucoes.FirstOrDefault(e => !e.Abandonada) is { } e) MostrarExecucaoNR(e); };
        n.BotaoNovaAnalise.Click += (_, _) => MostrarFormularioNR();
        n.BotaoPausarAnalise.Click += async (_, _) =>
        {
            if (execucaoMostrada is not { } e || e.PausaSolicitada) return;
            await TentarAsync(async () =>
            {
                e.PausaSolicitada = true;
                try
                {
                    if (await sessaoNR.CancelarAsync(e)) return;
                    MostrarMensagem(InfoBarSeverity.Warning, "A pausa não foi confirmada", "O controlador pode já ter terminado a execução.");
                }
                catch { e.PausaSolicitada = false; throw; }
                e.PausaSolicitada = false;
            });
        };
        n.BotaoCancelarAnalise.Click += async (_, _) => { if (execucaoMostrada is { } e) await TentarAsync(async () => await CancelarAnaliseAsync(e)); };
        n.BotaoHistoricoMonitor.Click += async (_, _) => await TentarAsync(() => ConsultarMonitorSalvoAsync(null));
        n.BotaoEvidenciasPdf.Click += (_, _) => AbrirEvidenciasDoMonitor();
        n.LinkEvidenciasPdf.Click += (_, _) => AbrirEvidenciasDoMonitor();
        n.BotaoMostrarRegistro.Click += (_, _) => { if (execucaoMostrada is { } e) AberturaExterna.MostrarNoExplorador(e.RegistroDeTrabalho); };
        n.BotaoMostrarRegistroResultado.Click += (_, _) => { if (ultimoResultadoNR is { } e) AberturaExterna.MostrarNoExplorador(e.RegistroDeTrabalho); };
        n.BotaoDescartarPendencias.Click += async (_, _) => await TentarAsync(EscolherDescarteAsync);
        n.BotaoDescartar.Click += async (_, _) => await TentarAsync(() => { DescartarAnalisePendente(); return Task.CompletedTask; });
        n.LinkRemontarTeste.Click += async (_, _) => { if (ultimoResultadoNR is { } e) await TentarAsync(async () => await RemontarRelatorioTesteAsync(e)); };
        n.BotaoContinuar.Click += async (_, _) => await TentarAsync(async () => await EscolherContinuacaoAsync());
        // 3.5.1: Retomar e Descartar análise sobem para a faixa de opções, ao lado do "Novo relatório" (pedido do usuário).
        Faixa.VagaExtra.Content = n.DestacarAcoesRetomada();
        n.BotaoImportarPendente.Click += async (_, _) => await TentarAsync(() => ResolverEntregaPendenteAsync(n.BotaoImportarPendente.Tag as AcaoEntrega));
        n.DetalhesSelecionados += async (_, _) =>
        {
            if (execucaoMostrada is { Provedor: Provedor.ChatGpt } e) await MostrarUltimaMensagemAsync(e);
        };
        BotaoAnalise.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(async () =>
        {
            if (estadoPilula is EstadoPilulaAnalise.Concluida or EstadoPilulaAnalise.Pausada or EstadoPilulaAnalise.Erro)
            {
                // A análise já terminou: a pílula sai; pausada ou com erro, abre o Novo relatório, onde estão o motivo e "Continuar a análise".
                var estado = estadoPilula;
                fimNaPilula = null;
                AtualizarTituloAnalise();
                if (estado != EstadoPilulaAnalise.Concluida) await AbrirNovoRelatorioNaVezAsync();
                return;
            }
            if (sessaoNR.Execucoes.FirstOrDefault(e => !e.Abandonada) is not { } e) return;
            if (!await AbrirNovoRelatorioNaVezAsync()) return;
            MostrarExecucaoNR(e);
        }));
        Dicas.Aplicar(BotaoAnalise, "Análise em andamento", "Abre a tela que acompanha a análise que a IA está fazendo agora.");

        relogioAnalise = DispatcherQueue.GetForCurrentThread().CreateTimer();
        relogioAnalise.Interval = TimeSpan.FromSeconds(1);
        relogioAnalise.IsRepeating = true;
        relogioAnalise.Tick += (_, _) =>
        {
            var monitorVisivel = PaginaNovoRelatorioVisivel && NR.VistaDaExecucao;
            if ((!janelaAtiva && !monitorVisivel) || !AppWindow.IsVisible ||
                AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized }) return;
            AtualizarTituloAnalise();
            if (monitorVisivel && execucaoMostrada is { Fim.IsCompleted: false } e)
            {
                AtualizarRelogioDaExecucao(e);
                _ = AtualizarAchadosAsync(e);
            }
        };
        AtualizarBotoesNR();
    }

    // ---------- abrir e fechar a página ----------

    /// <summary>"Novo relatório" (o caminho do comando, com a origem do módulo atual): na fila de comandos.</summary>
    internal Task<bool> NovoRelatorioAsync() => NaFilaAsync(AbrirNovoRelatorioNaVezAsync);

    async Task<bool> AbrirNovoRelatorioNaVezAsync()
    {
        var origem = moduloAtual;
        var selecionado = origem == Modulo.Processos ? processoEscolhido : null;
        var aberto = origem == Modulo.Relatorios ? sessao?.Caminho : null;
        if (moduloAtual != Modulo.Relatorios) await MudarModuloAsync(Modulo.Relatorios);
        if (moduloAtual != Modulo.Relatorios) return false;
        novoRelatorioAberto = true;
        AtualizarPaginasDeRelatorios();
        RegistrarPaginaVisitada();
        AtualizarComandos();
        // Com execução em andamento, a página abre na vista dela (a do processo sugerido, se houver; senão a mais antiga).
        var emCurso = sessaoNR.Execucoes.Where(e => !e.Abandonada).ToList();
        if (execucaoMostrada is null && emCurso.Count > 0) MostrarExecucaoNR(emCurso[0]);
        else if (execucaoMostrada is null) NR.MostrarExecucao(false);
        // Ajustes › Inteligência artificial › Pedido padrão: o texto que já vem na caixa (só se ela estiver vazia; nada digitado se perde).
        if (string.IsNullOrEmpty(NR.CaixaPedido.Text) && ajustes.PedidoPadrao.Length > 0) NR.CaixaPedido.Text = ajustes.PedidoPadrao;
        tarefaListaNR = CarregarProcessosNRAsync(origem, selecionado, aberto);
        await tarefaListaNR;
        await LerPecasAsync();
        await MostrarContasAtivasAsync();
        AtualizarBotoesNR();
        return true;
    }

    async Task MostrarContasAtivasAsync()
    {
        var linha = NR.TextoContasChatGpt;
        linha.Visibility = Visibility.Collapsed;
        if (!RoteiroLegado) return;
        var pecas = pecasAgentes;
        if (RotaCodex.Resolver(ajustes.Agentes[Provedor.ChatGpt].Modo, ajustes.RotaCodex, pecas.CodexHome).Rota != "codex-lb") return;
        var scripts = pecas.Analise62 is null ? null : Directory.GetParent(pecas.Analise62)?.Parent?.Parent;
        var consulta = scripts is null ? null : Path.Combine(scripts.FullName, "orcamento_lb.py");
        var texto = "Leitura de cota do Codex-LB indisponível.";
        if (pecas.Python is not null && File.Exists(consulta))
        {
            var args = new List<string>();
            if (pecas.PythonPeloLauncher) args.Add("-3");
            args.AddRange(["-B", "-X", "utf8", consulta!, "--contas"]);
            try
            {
                var leitura = await ExecucaoProcesso.RodarAsync(pecas.Python, args, scripts!.FullName, _ => { },
                    TimeSpan.FromSeconds(5), CancellationToken.None);
                if (leitura.Codigo == 0 && !string.IsNullOrWhiteSpace(leitura.Saida)) texto = leitura.Saida.Trim();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception or OperationCanceledException) { }
        }
        if (fechada) return;
        linha.Text = texto;
        linha.Visibility = Visibility.Visible;
    }

    void FecharPaginaNovoRelatorio()
    {
        if (!novoRelatorioAberto) return;
        novoRelatorioAberto = false;
        AtualizarPaginasDeRelatorios();
        RegistrarPaginaVisitada();
        AtualizarComandos();
    }

    /// <summary>Relatórios mostra as folhas ou, com a página aberta, a página Novo relatório no lugar delas.</summary>
    void AtualizarPaginasDeRelatorios()
    {
        if (fechada) return;
        var relatorios = moduloAtual == Modulo.Relatorios;
        R.Visibility = relatorios && !novoRelatorioAberto ? Visibility.Visible : Visibility.Collapsed;
        SincronizarRelogioRevisao();
        NR.Visibility = relatorios && novoRelatorioAberto ? Visibility.Visible : Visibility.Collapsed;
    }

    void AtualizarComandosNovoRelatorio()
    {
        Faixa.Habilitar(ComandosApp.RelatoriosNovo, documento is not null);
        if (fechada) return;
        NR.BotaoVoltarRelatorio.Visibility = sessao?.Atual is not null ? Visibility.Visible : Visibility.Collapsed;
        NR.BotaoExecucoes.Visibility = sessaoNR?.Execucoes.Any(e => !e.Abandonada) == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- processo ----------

    async Task CarregarProcessosNRAsync(Modulo origem, Processo? selecionado, string? aberto)
    {
        var raiz = ajustes.PastaProcessosEfetiva;
        IReadOnlyList<Processo> lista;
        try { lista = await Task.Run(() => Directory.Exists(raiz) ? Processos.Listar(raiz, CancellationToken.None).Itens : (IReadOnlyList<Processo>)[]); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Não foi possível ler a pasta de processos", Erro(e));
            lista = [];
        }
        if (fechada) return;
        processosNR = lista;
        var sugerido = FluxoNovoRelatorio.Sugerir(origem, selecionado, aberto, lista);
        var escolher = Mesmo(sugerido) ?? Mesmo(processoNR);
        PreencherProcessosNR(escolher);
        if (lista.Count == 0)
            MostrarMensagem(InfoBarSeverity.Informational, "Nenhum processo", "Nenhum processo foi encontrado na pasta de processos (" + raiz + "). Use Criar processo….");
        await EscolherProcessoNovoRelatorioAsync(escolher);
    }

    /// <summary>O processo da lista lida com a mesma pasta (a lista de Processos é de outra leitura).</summary>
    Processo? Mesmo(Processo? p) =>
        p is null ? null : processosNR.FirstOrDefault(x => string.Equals(Path.GetFullPath(x.Pasta), Path.GetFullPath(p.Pasta), StringComparison.OrdinalIgnoreCase));

    void PreencherProcessosNR(Processo? escolher = null)
    {
        if (fechada) return;
        escolher ??= processoNR;
        var n = NR;
        // 1.0.0: filtro por número, ano e requerente (Processos.Filtrar), com o resultado à vista. O processo escolhido fica sempre na lista,
        // mesmo fora do filtro (marcado), porque o Analisar parte para ele; limpar o filtro devolve a lista inteira.
        var (itens, unico, texto) = ListasPastas.FiltrarNovoRelatorio(processosNR, n.CaixaFiltro.Text, escolher);
        n.CaixaFiltro.ItemsSource = itens.Where(i => !i.ForaDoFiltro)
            .Select(i => new SugestaoProcesso(i.Processo)).ToArray();
        n.TextoFiltroProcessos.Text = texto;
        n.TextoFiltroProcessos.Visibility = texto.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        preenchendoNR = true;
        try
        {
            n.CaixaProcesso.Items.Clear();
            foreach (var (processo, fora) in itens)
                n.CaixaProcesso.Items.Add(new ComboBoxItem
                {
                    Content = (ListasPastas.NumeroDoProcesso(processo) is { } num ? num + " · " + (processo.Requerente ?? processo.NomePasta) : processo.NomePasta)
                        + (fora ? "  (escolhido)" : ""),
                    Tag = processo,
                });
            n.CaixaProcesso.SelectedItem = n.CaixaProcesso.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is Processo p && escolher is not null && p.Pasta == escolher.Pasta);
        }
        finally { preenchendoNR = false; }
        // Um único processo achado pelo filtro e nenhum escolhido: passa a ser o escolhido, como se clicado na lista.
        if (unico is not null && n.CaixaFiltro.FocusState != FocusState.Unfocused)
            n.CaixaProcesso.SelectedItem = n.CaixaProcesso.Items.OfType<ComboBoxItem>().FirstOrDefault(i => ReferenceEquals(i.Tag, unico));
    }

    /// <summary>O processo da análise: a pasta à vista, os destinos padrão das linhas que o usuário não escolheu e a prévia de novo.</summary>
    internal async Task EscolherProcessoNovoRelatorioAsync(Processo? p)
    {
        processoNR = p;
        NR.TextoPastaProcesso.Text = p?.Pasta ?? "";
        NR.TextoPastaProcesso.Visibility = string.IsNullOrEmpty(NR.TextoPastaProcesso.Text) ? Visibility.Collapsed : Visibility.Visible;
        var mostrado = (NR.CaixaProcesso.SelectedItem as ComboBoxItem)?.Tag as Processo;
        if (p is not null && mostrado?.Pasta != p.Pasta) PreencherProcessosNR(p);
        if (p is not null)
        {
            var (padrao, revisao) = await Task.Run(() => (Arquivamento.Padrao(p.Pasta), Arquivamento.RevisaoAtual(p.Pasta)));
            if (!ReferenceEquals(processoNR, p)) return;
            revisaoAtualNR = revisao;
            foreach (var a in anexosNR)
            {
                if (!a.Escolhido) a.Destino = padrao;
                if (a.Destino == DestinoAnexo.ReapresentacaoAtual && revisao is null) a.Destino = DestinoAnexo.ReapresentacaoNova;
            }
        }
        else revisaoAtualNR = null;
        AtualizarBotoesNR();
        await AtualizarPreviaNRAsync();
    }

    /// <summary>"Criar processo…" (desenho §12.3): a pasta nova (ou a existente, com o aviso) passa a ser a escolhida. null = cancelado ou recusado.</summary>
    internal Task<Processo?> CriarProcessoAsync() => NaFilaAsync(CriarProcessoNaVezAsync);

    /// <summary>Pergunta número, ano e requerente (sugerido) e cria a pasta do processo (ou usa a existente). null = cancelado ou recusado.
    /// Usado pelo "Criar processo…", pelo "Novo processo" de Processos e Imóveis e pelo boletim cadastral (2026-11-17).</summary>
    internal async Task<Processo?> CriarPastaProcessoAsync(string? requerente = null)
    {
        var dados = await Perguntas.Atual.PedirNovoProcessoAsync(requerente ?? "");
        if (dados is null) return null;
        var raiz = ajustes.PastaProcessosEfetiva;
        Processo p;
        bool criado;
        try { (p, criado) = await Task.Run(() => CriacaoProcesso.Criar(raiz, dados.Numero, dados.Ano, dados.Requerente)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            MostrarMensagem(InfoBarSeverity.Error, "O processo não foi criado", e is ArgumentException ? e.Message.Split(" (Parameter")[0] : Erro(e));
            return null;
        }
        if (criado) MostrarMensagem(InfoBarSeverity.Success, "Processo criado", p.Pasta);
        else MostrarMensagem(InfoBarSeverity.Informational, "O processo já existe", FluxoNovoRelatorio.TextoProcessoExiste(dados.Numero, dados.Ano, p.Pasta));
        processosLidos = false;   // Processos lê a pasta de novo na próxima visita
        return p;
    }

    async Task<Processo?> CriarProcessoNaVezAsync()
    {
        if (await CriarPastaProcessoAsync() is not { } p) return null;
        var raiz = ajustes.PastaProcessosEfetiva;
        var lista = await Task.Run(() => Processos.Listar(raiz, CancellationToken.None).Itens);
        processosNR = lista.Any(x => string.Equals(x.Pasta, p.Pasta, StringComparison.OrdinalIgnoreCase)) ? lista : lista.Append(p).ToList();
        var escolhido = Mesmo(p) ?? p;
        NR.CaixaFiltro.Text = "";
        PreencherProcessosNR(escolhido);
        await EscolherProcessoNovoRelatorioAsync(escolhido);
        return escolhido;
    }

    // ---------- anexos ----------

    static readonly (DestinoAnexo Destino, string Rotulo)[] CategoriasDoFormulario =
    [
        (DestinoAnexo.ProjetoInicial, "PROJETOS"),
        (DestinoAnexo.Bci, "BCI"),
        (DestinoAnexo.ArtRrt, "ART/RRT"),
        (DestinoAnexo.EscrituraCertidao, "ESCRITURA OU CERTIDÃO DE INTEIRO TEOR"),
        (DestinoAnexo.OutrosDocumentos, "OUTROS DOCUMENTOS")
    ];
    readonly Dictionary<DestinoAnexo, StackPanel> listasDeAnexos = [];

    static DestinoAnexo CategoriaDoDestino(DestinoAnexo destino) => destino switch
    {
        DestinoAnexo.ReapresentacaoAtual or DestinoAnexo.ReapresentacaoNova => DestinoAnexo.ProjetoInicial,
        DestinoAnexo.Documento => DestinoAnexo.OutrosDocumentos,
        _ => destino
    };

    void MontarCategoriasDeAnexos()
    {
        foreach (var (destino, rotulo) in CategoriasDoFormulario)
        {
            var painel = new StackPanel { Spacing = 6 };
            var cabecalho = new Grid { ColumnSpacing = 8 };
            cabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cabecalho.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cabecalho.Children.Add(new TextBlock { Text = rotulo, Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"],
                TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
            var escolher = new Button { Content = "Selecionar arquivos…" };
            IconeDeAcao.SetGlifo(escolher, Glifos.Anexar);
            AutomationPropertiesNome(escolher, "Selecionar arquivos em " + rotulo);
            escolher.Click += async (_, _) => await TentarAsync(() => AnexarAsync(destino));
            Grid.SetColumn(escolher, 1);
            cabecalho.Children.Add(escolher);
            painel.Children.Add(cabecalho);
            if (destino == DestinoAnexo.OutrosDocumentos)
                painel.Children.Add(new TextBlock { Text = "Inclua aqui declarações, autorizações e outros documentos complementares.",
                    Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"], TextWrapping = TextWrapping.Wrap });
            var lista = new StackPanel { Spacing = 4 };
            listasDeAnexos.Add(destino, lista);
            painel.Children.Add(lista);
            var zona = new Grid { Tag = destino, AllowDrop = true, Padding = new Thickness(10, 8, 10, 8), CornerRadius = new CornerRadius(6),
                Style = (Style)Application.Current.Resources["PmvniGradeCartaoStyle"] };
            AutomationPropertiesNome(zona, rotulo);
            zona.DragOver += (_, e) =>
            {
                e.AcceptedOperation = e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
                e.DragUIOverride.Caption = "Anexar em " + rotulo;
                e.Handled = true;
            };
            zona.Drop += (_, e) => AoSoltarAnexos(destino, e);
            zona.Children.Add(painel);
            NR.CategoriasAnexos.Children.Add(zona);
        }
    }

    internal async Task AnexarAsync(DestinoAnexo? categoria = null)
    {
        var caminhos = await Perguntas.Atual.AbrirArquivosAsync();
        if (caminhos is not null) await AdicionarAnexosAsync(caminhos, [], categoria);
    }

    internal Task SoltarArquivosAsync(IReadOnlyList<string> caminhos, DestinoAnexo? categoria = null) =>
        AdicionarAnexosAsync(caminhos, [], categoria);

    async void AoSoltarAnexos(DestinoAnexo categoria, DragEventArgs e)
    {
        e.Handled = true;
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var adiamento = e.GetDeferral();
        try
        {
            var itens = await e.DataView.GetStorageItemsAsync();
            var caminhos = new List<string>();
            var semCaminho = new List<string>();
            foreach (var item in itens)
            {
                // Um anexo arrastado do e-mail chega sem caminho no disco: não há o que copiar.
                if (string.IsNullOrEmpty(item.Path)) semCaminho.Add(FluxoNovoRelatorio.ItemSemCaminho(item.Name));
                else caminhos.Add(item.Path);
            }
            await AdicionarAnexosAsync(caminhos, semCaminho, categoria);
        }
        catch (Exception x) { MostrarMensagem(InfoBarSeverity.Error, "Não foi possível anexar", Erro(x)); }
        finally { adiamento.Complete(); }
    }

    /// <summary>Confere cada arquivo e conserva a escolha por categoria na lista antes de arquivá-lo.</summary>
    async Task AdicionarAnexosAsync(IReadOnlyList<string> caminhos, IReadOnlyList<string> recusasDeFora, DestinoAnexo? categoria)
    {
        // Compactados (.zip, .rar, .7z; pedido do usuário, 2026-11-16): o kit não descompacta, então o editor abre o arquivo numa pasta
        // temporária e cada arquivo de dentro entra como um anexo comum, com o destino sugerido pela regra do kit (documento ou projeto).
        var deCompactado = new Dictionary<string, ArquivoExtraido>(StringComparer.OrdinalIgnoreCase);
        var avisosCompactados = new List<string>();
        var abertos = new List<string>();
        if (caminhos.Any(c => Compactados.EhCompactado(c) && File.Exists(c)))
        {
            pastaCompactadosNR ??= Path.Combine(Compactados.PastaTemporaria(), Guid.NewGuid().ToString("N"));
            var lista = new List<string>();
            foreach (var c in caminhos)
            {
                if (!Compactados.EhCompactado(c) || !File.Exists(c)) { lista.Add(c); continue; }
                NR.TextoArraste.Text = "Abrindo " + Path.GetFileName(c) + "…";
                // Compactado grande pode levar minutos: "Cancelar abertura" para a extração (nada dele entra como anexo).
                using var cancelar = new CancellationTokenSource();
                void AoCancelar(object s, RoutedEventArgs a) => cancelar.Cancel();
                NR.BotaoCancelarAbertura.Click += AoCancelar;
                NR.BotaoCancelarAbertura.Visibility = Visibility.Visible;
                try
                {
                    var sessaoNR = pastaCompactadosNR;
                    var token = cancelar.Token;
                    var r = await Task.Run(() => { Compactados.LimparAntigos(TimeSpan.FromDays(1), exceto: sessaoNR); return Compactados.Extrair(c, sessaoNR, token); });
                    foreach (var a in r.Arquivos) { lista.Add(a.Caminho); deCompactado[Path.GetFullPath(a.Caminho)] = a; }
                    avisosCompactados.AddRange(r.Avisos);
                    if (r.Arquivos.Count > 0) abertos.Add($"{Path.GetFileName(c)} ({r.Arquivos.Count} {(r.Arquivos.Count == 1 ? "arquivo" : "arquivos")})");
                }
                catch (OperationCanceledException)
                {
                    avisosCompactados.Add($"A abertura de {Path.GetFileName(c)} foi cancelada; nenhum arquivo dele entrou.");
                }
                catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    RegistroErros.Gravar("anexo.compactado", e);
                    avisosCompactados.Add(e is InvalidDataException ? e.Message : $"{Path.GetFileName(c)} não pôde ser aberto: {Erro(e).TrimEnd('.')}.");
                }
                finally
                {
                    NR.BotaoCancelarAbertura.Click -= AoCancelar;
                    NR.BotaoCancelarAbertura.Visibility = Visibility.Collapsed;
                }
            }
            NR.TextoArraste.Text = "";
            caminhos = lista;
            recusasDeFora = [.. recusasDeFora, .. avisosCompactados];
        }
        var novos = new List<string>();
        foreach (var c in caminhos)
        {
            string completo;
            try { completo = Path.GetFullPath(c); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (novos.Contains(completo, StringComparer.OrdinalIgnoreCase)) continue;
            novos.Add(completo);
        }
        var pasta = processoNR?.Pasta;
        var base_ = RoteiroLegado ? pasta ?? ajustes.PastaProcessosEfetiva : Path.Combine(PastaDados.Raiz, "prever-anexos");   // sem processo, a conferência só lê o anexo
        var conferidos = await Task.Run(() =>
        {
            var padrao = pasta is null ? DestinoAnexo.ProjetoInicial : Arquivamento.Padrao(pasta);
            var plano = Arquivamento.Planejar(base_, novos.Select(n => (n, DestinoAnexo.Documento)).ToList());
            return plano.Select(x =>
            {
                long tamanho = 0;
                if (x.Recusa is null) try { tamanho = new FileInfo(x.Origem).Length; } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                return (x.Origem, x.Recusa, Tamanho: tamanho, Padrao: padrao);
            }).ToList();
        });
        if (fechada) return;
        recusasNR.Clear();
        recusasNR.AddRange(recusasDeFora);
        foreach (var (origem, recusa, tamanho, padrao) in conferidos)
        {
            if (recusa is not null) { recusasNR.Add(recusa); continue; }
            deCompactado.TryGetValue(origem, out var extraido);
            var destino = categoria switch
            {
                DestinoAnexo.ProjetoInicial => padrao,
                { } escolhido => escolhido,
                _ => extraido is null ? padrao : Compactados.SugerirDestino(extraido.Interno, padrao)
            };
            if (anexosNR.Any(a => CategoriaDoDestino(a.Destino) == CategoriaDoDestino(destino) &&
                string.Equals(a.Origem, origem, StringComparison.OrdinalIgnoreCase))) continue;
            anexosNR.Add(new AnexoNovo(origem, tamanho, destino, extraido?.Compactado, extraido?.Interno)
                { Escolhido = CategoriaDoDestino(destino) != DestinoAnexo.ProjetoInicial });
        }
        if (abertos.Count > 0)
            MostrarMensagem(InfoBarSeverity.Informational, "Compactado aberto", string.Join(", ", abertos) + ".");
        NR.TextoRecusas.Text = string.Join(Environment.NewLine, recusasNR);
        NR.TextoRecusas.Visibility = recusasNR.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        await AtualizarPreviaNRAsync();
    }

    /// <summary>A caixa Destino da linha (começa em 0; o roteiro escolhe pela posição na tela, resolvida na hora).</summary>
    internal void DefinirDestino(int linha, DestinoAnexo d) => DefinirDestino(anexosNR[linha], d);

    internal void RemoverAnexo(int linha) => RemoverAnexo(anexosNR[linha]);

    // Os controles de cada linha guardam o próprio anexo (não a posição): um clique que chega antes de a lista ser redesenhada nunca muda outra linha.
    void DefinirDestino(AnexoNovo a, DestinoAnexo d)
    {
        if (!anexosNR.Contains(a)) return;
        a.Destino = d;
        a.Escolhido = true;
        tarefaPreviaNR = AtualizarPreviaNRAsync();
    }

    void RemoverAnexo(AnexoNovo a)
    {
        if (!anexosNR.Remove(a)) return;
        tarefaPreviaNR = AtualizarPreviaNRAsync();
    }

    /// <summary>A prévia de cada linha por <see cref="Arquivamento.Planejar"/> (só lê), recalculada ao mudar destino, linhas ou processo.</summary>
    Task AtualizarPreviaNRAsync() => tarefaPreviaNR = PreverAsync();

    async Task PreverAsync()
    {
        var minha = ++sequenciaPreviaNR;
        if (!RoteiroLegado)
        {
            foreach (var a in anexosNR) a.Previa = "Anexado para leitura pela IA";
            RenderizarAnexos();
            AtualizarBotoesNR();
            return;
        }
        var p = processoNR;
        var itens = anexosNR.Select(a => (a.Origem, a.Destino)).ToList();
        IReadOnlyList<PlanoAnexo>? plano = null;
        if (p is not null && itens.Count > 0)
        {
            try { plano = await Task.Run(() => Arquivamento.Planejar(p.Pasta, itens)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { plano = null; }
        }
        if (fechada || minha != sequenciaPreviaNR) return;
        for (var i = 0; i < anexosNR.Count; i++)
            anexosNR[i].Previa = p is null ? FluxoNovoRelatorio.EscolhaOProcesso : plano is not null && i < plano.Count ? FluxoNovoRelatorio.PreviaDestino(plano[i]) : "";
        RenderizarAnexos();
        AtualizarBotoesNR();
    }

    void RenderizarAnexos()
    {
        foreach (var listaCategoria in listasDeAnexos.Values) listaCategoria.Children.Clear();
        var opcoes = FluxoNovoRelatorio.OpcoesDestino(revisaoAtualNR);
        var semProcesso = processoNR is null;
        foreach (var a in anexosNR)
        {
            var categoria = CategoriaDoDestino(a.Destino);
            var lista = listasDeAnexos[categoria];
            var grade = new Grid { ColumnSpacing = 10, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(6),
                Style = (Style)Application.Current.Resources["PmvniGradeCartaoStyle"] };
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = RoteiroLegado && categoria == DestinoAnexo.ProjetoInicial ? new GridLength(2, GridUnitType.Star) : new GridLength(0), MaxWidth = 340 });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = semProcesso ? new GridLength(0) : new GridLength(3, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nome = new StackPanel();
            nome.Children.Add(new TextBlock { Text = a.Nome, TextTrimming = TextTrimming.CharacterEllipsis });
            var detalhe = ListasPastas.TextoDoTamanho(a.Tamanho);
            if (a.Compactado is { } arquivoZip) detalhe = Path.GetFileName(arquivoZip) + " · " + detalhe;
            if (a.Compactado is not null && a.Interno is { } dentro && dentro.Contains('/'))
                detalhe = "pasta " + dentro[..dentro.LastIndexOf('/')].Replace("/", " › ") + " · " + detalhe;
            nome.Children.Add(new TextBlock { Text = detalhe, TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] });
            grade.Children.Add(nome);
            if (RoteiroLegado && categoria == DestinoAnexo.ProjetoInicial)
            {
                var caixa = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
                AutomationPropertiesNome(caixa, "Destino de " + a.Nome);
                foreach (var o in opcoes.Where(o => CategoriaDoDestino(o.Destino) == DestinoAnexo.ProjetoInicial)) caixa.Items.Add(new ComboBoxItem { Content = o.Rotulo, Tag = o.Destino });
                caixa.SelectedItem = caixa.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag is DestinoAnexo d && d == a.Destino);
                if (caixa.SelectedItem is ComboBoxItem { Content: string rotuloDestino }) ToolTipService.SetToolTip(caixa, rotuloDestino);
                caixa.SelectionChanged += (_, _) => { if (caixa.SelectedItem is ComboBoxItem { Tag: DestinoAnexo d } && d != a.Destino) DefinirDestino(a, d); };
                Grid.SetColumn(caixa, 1);
                grade.Children.Add(caixa);
            }
            var previa = new TextBlock { Text = semProcesso ? "" : a.Previa, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true };
            Grid.SetColumn(previa, 2);
            grade.Children.Add(previa);
            var remover = new Button
            {
                Content = new FontIcon { Glyph = Glifos.Excluir, Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["PmvniIconeAcao"] },
                Width = 32, Height = 32, MinWidth = 32, MinHeight = 32,
                VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
            };
            AutomationPropertiesNome(remover, "Remover " + a.Nome + " da lista");
            remover.Click += (_, _) => RemoverAnexo(a);
            Dicas.Aplicar(remover, "Remover da lista", "Tira este arquivo da análise; o arquivo continua no computador.");
            Grid.SetColumn(remover, 3);
            grade.Children.Add(remover);
            lista.Children.Add(grade);
        }
    }

    static void AutomationPropertiesNome(DependencyObject d, string nome) => Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(d, nome);

    // ---------- os botões Analisar ----------

    /// <summary>Por que "Analisar com o …" está desabilitado (desenho §12.11); null = pode analisar.</summary>
    internal string? MotivoBotao(Provedor p) =>
        !RoteiroLegado && p == Provedor.Claude ? SemPluginClaude :
        FluxoNovoRelatorio.MotivoDesabilitado(p, processoNR, processoNR is not null && sessaoNR.EmAndamento(processoNR.Pasta), pecasAgentes, ajustes.Agentes[p].Modo,
            ajustes.Agentes[p].Modo == ModoAcesso.Chave && CofreChaves.Existe(PastaDados.Raiz, p), p == Provedor.Gemini ? PerfilDoGemini : null,
            usarAnaliseChatGpt: p == Provedor.ChatGpt && !RoteiroLegado);

    void AtualizarBotoesNR()
    {
        if (fechada) return;
        foreach (var p in Provedores.Ativos)
        {
            var botao = BotaoDoProvedor(p);
            var texto = p switch { Provedor.ChatGpt => NR.TextoMotivoChatGpt, Provedor.Claude => NR.TextoMotivoClaude, _ => NR.TextoMotivoGemini };
            var motivo = MotivoBotao(p) ?? (partindoNR ? "Uma análise está sendo iniciada." : null);
            var servico = Provedores.Nome(p);
            var dica = DicasComandos.Livre("Analisar com o " + servico, $"Manda o processo, os anexos e o pedido para o {servico} analisar e montar o relatório.", motivo: motivo);
            AtualizarDicaDeDisponibilidade(botao, dica);
            texto.Text = motivo ?? "";
            texto.Visibility = motivo is null || p == Provedor.Claude && !RoteiroLegado ? Visibility.Collapsed : Visibility.Visible;
            // 2026-11-16: a versão instalada do plugin que vai fazer a análise, sempre à vista (também gravada no execucao.json).
            var direto = p == Provedor.ChatGpt && !RoteiroLegado;
            var versao = direto ? pecasAgentes.VersaoAnaliseChatGpt : Pmvni.Agentes.OrquestradorAnalise.VersaoDoPlugin(p, pecasAgentes);
            var rotulo = (p switch { Provedor.ChatGpt => "Pelo Codex · ", Provedor.Claude => "Pelo Claude Code · ", _ => "Pelo Antigravity · " })
                + (direto ? OrquestradorAnalise.NomeAnaliseChatGpt : TextosAgentes.NomeDaRota(p)) + " " + (versao is not null ? "v" + versao : ReferenceEquals(pecasAgentes, PecasNaoLidas) ? "(verificando versão…)" : "(versão não encontrada)");
            var vaga = p switch { Provedor.ChatGpt => NR.TextoPluginChatGpt, Provedor.Claude => NR.TextoPluginClaude, _ => NR.TextoPluginGemini };
            if (p == Provedor.Claude && !RoteiroLegado) vaga.Text = SemPluginClaude;
            else if (vaga.Text != rotulo) vaga.Text = rotulo;
            if (p == Provedor.Gemini)
            {
                // O modo e o perfil que a análise vai usar, à vista antes do clique (o que vale é o de Ajustes; numa retomada, o da própria análise).
                var perfil = PerfilDoGemini is { } pf ? "Perfil: " + MatrizGemini.Nome(pf) : "Perfil da chave não escolhido";
                if (NR.TextoPerfilGemini.Text != perfil) NR.TextoPerfilGemini.Text = perfil;
            }
        }
        NR.AnelPartida.IsActive = partindoNR;
        AtualizarComandosNovoRelatorio();
    }

    /// <summary>O clique em "Analisar com o …": na fila de comandos só até a partida. null = não partiu (o motivo à vista).</summary>
    internal Task<ExecucaoEmCurso?> AnalisarAsync(Provedor p) => NaFilaAsync(() => AnalisarNaVezAsync(p));

    async Task<ExecucaoEmCurso?> AnalisarNaVezAsync(Provedor p)
    {
        if (!Provedores.Ativos.Contains(p)) return null;
        NR.AvisoMensagem.IsOpen = false;
        NR.AvisoResultado.IsOpen = false;
        await LerPecasAsync();    // as peças do disco, como estão agora
        if (MotivoBotao(p) is { } motivo)
        {
            AtualizarBotoesNR();
            MostrarMensagem(InfoBarSeverity.Warning, "Não é possível analisar agora", motivo);
            return null;
        }
        var a = ajustes.Agentes[p];
        if (FluxoNovoRelatorio.PrecisaAviso(a))
        {
            if (!await Perguntas.Atual.ConfirmarAsync(FluxoNovoRelatorio.AvisoTitulo(p), FluxoNovoRelatorio.TextoAviso(p), FluxoNovoRelatorio.AvisoSim, FluxoNovoRelatorio.AvisoNao, destrutiva: false))
                return null;    // recusar não copia nada e não inicia nada
            a.AvisoVersao = Ajustes.VersaoAviso;
            a.AvisoEm = DateTimeOffset.UtcNow;
            SalvarAjustes();
            MostrarAgentes();
        }
        var processo = processoNR!;
        var instantaneo = anexosNR.ToList();      // o que foi enviado: um anexo posto durante a cópia fica na lista para a próxima
        var anexos = instantaneo.Select(x => (x.Origem, x.Destino)).ToList();
        return await AnalisarComPerfilAsync(p, processo, anexos, PedidoTexto, instantaneo);
    }

    /// <param name="enviados">Os anexos da lista que foram para esta partida (saem da lista depois dela); null = a lista não muda.</param>
    async Task<ExecucaoEmCurso?> PartirAsync(Func<Task<ExecucaoEmCurso?>> partir, IReadOnlyList<AnexoNovo>? enviados)
    {
        partindoNR = true;
        AtualizarBotoesNR();
        ExecucaoEmCurso? e;
        try { e = await partir(); }
        catch (AnaliseRecusadaException x)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "A análise não foi iniciada", x.Message);
            return null;
        }
        catch (CopiaInterrompidaException x)
        {
            MostrarMensagem(InfoBarSeverity.Error, "A análise não foi iniciada", FluxoNovoRelatorio.TextoCopiaInterrompida(Erro(x.InnerException!), x.Pasta));
            NotificacoesWindows.Mostrar("A análise não foi iniciada", "A preparação dos anexos foi interrompida. Confira o aviso no Editor.");
            return null;
        }
        catch (Exception x)
        {
            MostrarMensagem(InfoBarSeverity.Error, "A análise não foi iniciada", Erro(x) + ". Nada foi iniciado.");
            NotificacoesWindows.Mostrar("A análise não foi iniciada", "Abra o Editor para conferir a falha de inicialização.");
            return null;
        }
        finally
        {
            partindoNR = false;
            AtualizarBotoesNR();
        }
        if (e is null) return null;
        if (Provedores.PeloKit(e.Provedor)) EsquecerPendenteChatGpt(e.Registro.Pasta);
        else EsquecerPendenteClaude(e.Registro.Pasta);
        if (Provedores.PeloKit(e.Provedor) && !ajustes.ExecucoesClaude.Any(x => string.Equals(x.Registro, e.Registro.Pasta, StringComparison.OrdinalIgnoreCase)))
        {
            // Claude e Gemini (controladores do kit, que seguem sozinhos) ficam na mesma lista; o provedor de cada um está no execucao.json do registro.
            ajustes.ExecucoesClaude.Add(new ExecucaoClaudePendente(e.Processo.Pasta, e.Registro.Pasta));
            SalvarAjustes();
        }
        if (e.Provedor == Provedor.ChatGpt && !ajustes.ExecucoesChatGpt.Any(x => MesmoRegistro(x.Registro, e.Registro.Pasta)))
        {
            // Guardada já na partida: se o editor cair, a reabertura oferece continuar de onde parou.
            ajustes.ExecucoesChatGpt.Add(new ExecucaoClaudePendente(e.Processo.Pasta, e.Registro.Pasta));
            SalvarAjustes();
        }
        e.Conclusao = ConcluirAsync(e);
        _ = CarregarHistoricoAsync();
        if (enviados is not null)
        {
            // Os anexos enviados já estão na pasta do processo: saem da lista (o processo, o pedido e o que foi posto durante a cópia ficam).
            foreach (var a in enviados) anexosNR.Remove(a);
            recusasNR.Clear();
            // Um anexo que ficou ilegível entre a prévia e o clique não foi enviado: o motivo fica à vista.
            recusasNR.AddRange(e.Recusas);
            NR.TextoRecusas.Text = string.Join(Environment.NewLine, recusasNR);
            NR.TextoRecusas.Visibility = recusasNR.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            RenderizarAnexos();
        }
        if (fechada) return e;
        if (novoRelatorioAberto) MostrarExecucaoNR(e);
        AtualizarTituloAnalise();
        AtualizarBotoesNR();
        return e;
    }

    async Task EscolherContinuacaoAsync()
    {
        var processo = continuarChatGpt?.Processo ?? ultimoResultadoNR?.Processo;
        var registro = continuarChatGpt?.Registro ?? ultimoResultadoNR?.Registro.Pasta;
        var original = continuarChatGpt is null ? ultimoResultadoNR?.Provedor : Provedor.ChatGpt;
        if (processo is null || registro is null || original is null || NR.BotaoDescartar.Visibility != Visibility.Visible)
        {
            await EscolherHistoricoPendenteAsync();
            return;
        }
        var (escolha, _) = await PerguntarRetomadaAsync(processo, registro, original.Value);
        if (escolha is null) return;
        var iniciada = escolha == original && (continuarChatGpt is not null || Provedores.PeloKit(original.Value))
            ? await ContinuarAnaliseAsync()
            : await ContinuarComOutraIaAsync(escolha.Value, processo, registro);
        if (iniciada is not null) NR.BotaoDescartar.Visibility = Visibility.Collapsed;
    }

    internal void DescartarAnalisePendente()
    {
        var registro = continuarChatGpt?.Registro ?? ultimoResultadoNR?.Registro.Pasta;
        if (registro is null) return;
        var processo = continuarChatGpt?.Processo ?? ultimoResultadoNR?.Processo;
        if (processo is null) return;
        DescartarAnalisePendente(processo, registro);
    }

    void DescartarAnalisePendente(Processo processo, string registro)
    {
        using var historico = RegistroExecucao.Abrir(registro, processo.Pasta);
        historico.Atualizar(o => o["situacao"] = "descartada");
        // ChatGPT: um controlador do analise que ficou vivo desta análise (o editor de antes caiu sem levá-lo) é encerrado pelo critério de pid e hora de partida.
        if (RoteiroLegado && !sessaoNR.EmAndamento(processo.Pasta)) _ = Orquestrador.CancelarChatGptAsync(registro, pecasAgentes);
        EsquecerPendenteChatGpt(registro);
        EsquecerPendenteClaude(registro);
        _ = CarregarHistoricoAsync();
        bool chat = continuarChatGpt is { } cg && MesmoRegistro(cg.Registro, registro);
        bool resultado = ultimoResultadoNR is { } r && MesmoRegistro(r.Registro.Pasta, registro);
        if (chat) continuarChatGpt = null;
        if (resultado) ultimoResultadoNR = null;
        if (!chat && !resultado) return;
        NR.BotaoDescartar.Visibility = Visibility.Collapsed;
        NR.AvisoResultado.IsOpen = false;
    }

    internal Task<ExecucaoEmCurso?> ContinuarAnaliseAsync() => NaFilaAsync(async () =>
    {
        if (continuarChatGpt is { } cg)
        {
            // ChatGPT (Tarefa 12): o controlador do analise é chamado de novo no mesmo registro, sem --inputs, e continua o controle de onde parou.
            NR.AvisoResultado.IsOpen = false;
            await LerPecasAsync();
            var iniciada = await AnalisarComPerfilAsync(Provedor.ChatGpt, cg.Processo, [], FluxoNovoRelatorio.PedidoContinuar, null, cg.Registro);
            if (iniciada is not null) continuarChatGpt = null;
            return iniciada;
        }
        if (ultimoResultadoNR is not { } anterior || !Provedores.PeloKit(anterior.Provedor)) return null;
        NR.AvisoResultado.IsOpen = false;
        await LerPecasAsync();
        // Gemini: a retomada segue no perfil em que a análise começou (o plugin continua da etapa pendente, sem refazer as concluídas).
        return await AnalisarComPerfilAsync(anterior.Provedor, anterior.Processo, [], anterior.Texto, enviados: null, registroAnterior: anterior.Registro.Pasta);
    });

    // ---------- vista da execução ----------

    static string PastaEvidenciasDoMonitor(ExecucaoEmCurso e) => Path.Combine(e.PastaDeTrabalho, ".pmvni-ai", "cache-pdf");

    void AbrirEvidenciasDoMonitor()
    {
        if (execucaoMostrada is { } e && Directory.Exists(PastaEvidenciasDoMonitor(e)))
            AberturaExterna.MostrarNoExplorador(PastaEvidenciasDoMonitor(e));
    }

    void MostrarExecucaoNR(ExecucaoEmCurso e)
    {
        if (fechada) return;
        execucaoMostrada = e;
        consumoSessao = e.Provedor == Provedor.ChatGpt ? new ConsumoSessaoCodex(OpcoesAgentes().CodexHome) : null;
        NR.MostrarEvidencias(Directory.Exists(PastaEvidenciasDoMonitor(e)));
        NR.MostrarAchados([]);
        NR.ReiniciarFiltroDescobertas();
        NR.ReiniciarEstimativa();
        NR.MostrarProgresso(null);
        NR.MostrarConsumo([]);
        NR.ModelosConfigurados = "";
        NR.ConferenciaNumericaExibida = null;
        NR.MostrarDetalhesDasEtapas(DetalhesDasEtapas.Vazio);
        NR.ConsumoDoPlugin = null;
        NR.ArquivoDoDiario = null;
        NR.MonitorChatGpt = e.PastaAnaliseChatGpt is null ? null : MonitorAnaliseChatGpt.Aguardando;
        if (e.PastaAnaliseChatGpt is { } pastaPlugin) _ = CarregarReferenciaAsync(e, pastaPlugin);
        _ = AtualizarAchadosAsync(e);
        NR.LigarLinhas(e.LinhasObservaveis);
        NR.MostrarExecucao(true);
        NR.TextoUltimaMensagem.Text = "";
        ultimaMensagemLida = null;
        RenderizarExecucao(e);
        AtualizarComandosNovoRelatorio();
    }

    async Task CarregarReferenciaAsync(ExecucaoEmCurso execucao, string pasta)
    {
        var referencia = await Task.Run(() =>
        {
            var raiz = Directory.GetParent(pasta)?.Parent;
            if (raiz is null) return (TimeSpan?)null;
            try
            {
                var duracoes = new List<TimeSpan>();
                foreach (var processo in raiz.EnumerateDirectories())
                foreach (var anterior in processo.EnumerateDirectories("*-chatgpt"))
                {
                    if (string.Equals(anterior.FullName, pasta, StringComparison.OrdinalIgnoreCase)) continue;
                    var inicio = new FileInfo(Path.Combine(anterior.FullName, "grupos.json"));
                    var fim = new FileInfo(Path.Combine(anterior.FullName, "grupo-15.json"));
                    if (inicio.Exists && fim.Exists && File.Exists(Path.Combine(anterior.FullName, "relatorio.rpmvni.publicacao.json"))
                        && fim.LastWriteTimeUtc > inicio.LastWriteTimeUtc)
                        duracoes.Add(fim.LastWriteTimeUtc - inicio.LastWriteTimeUtc);
                }
                duracoes.Sort();
                return duracoes.Count == 0 ? (TimeSpan?)null : duracoes[duracoes.Count / 2];
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException) { return null; }
        });
        if (!fechada && ReferenceEquals(execucaoMostrada, execucao)) NR.ReferenciaDeVerificacoes = referencia;
    }

    void MostrarFormularioNR()
    {
        execucaoMostrada = null;
        NR.LigarLinhas(null);
        NR.MostrarExecucao(false);
        AtualizarBotoesNR();
    }

    ConsumoSessaoCodex? consumoSessao;
    readonly PerfilAoVivoCodex perfilAoVivo = new();
    bool lendoAchados;

    /// <summary>Tarefa 13: o progresso, a previsão e o consumo saem dos arquivos do kit (manifesto, engine_state e uso-das-IAs.json), lidos pelo Editor; a IA
    /// não grava mais nada para o Monitor. A leitura é barata (cada arquivo só é relido quando muda) e roda a cada segundo, fora da thread da janela.</summary>
    async Task AtualizarAchadosAsync(ExecucaoEmCurso e)
    {
        if (lendoAchados) return;
        lendoAchados = true;
        try
        {
            if (e.PastaAnaliseChatGpt is { } pasta)
            {
                var casaCodex = OpcoesAgentes().CodexHome;
                var (monitor, resumo) = await Task.Run(() => (OrquestradorAnalise.LerMonitorChatGpt(pasta) with { PerfilAtivo = perfilAoVivo.Ler(pasta, casaCodex) },
                    DiarioAnaliseChatGpt.Listar(Path.Combine(pasta, DiarioAnaliseChatGpt.Pasta)).FirstOrDefault()));
                if (!fechada && ReferenceEquals(e, execucaoMostrada))
                {
                    NR.ConsumoDoPlugin = resumo.Consumo;
                    NR.ArquivoDoDiario = resumo.Arquivo;
                    if (monitor.Rodadas.Count > 0) NR.MonitorChatGpt = monitor;
                    RenderizarExecucao(e);
                }
                return;
            }
            var controle = Path.Combine(e.PastaDeTrabalho, ResultadoAnalise.PastaControle(e.Provedor));
            var historico = HistoricoDeDuracoes();
            var ordemDoKit = OrdemDasEtapasDoKit();
            var sessoes = consumoSessao;
            var etapasJson = pecasAgentes.EtapasJson;
            var desde = e.InicioDoControleUtc ??= await Task.Run(() => InicioDoRegistro(e));
            var achados = await Task.Run(() => AchadosExecucao.LerDaExecucao(Path.Combine(e.RegistroDeTrabalho, "progresso"), e.PastaDeTrabalho,
                e.InicioUtc, controle, desde, historico, DateTimeOffset.UtcNow, ordemDoKit, iniciando: e.Fim?.IsCompleted != true));
            if (!fechada && ReferenceEquals(e, execucaoMostrada))
            {
                NR.MostrarAchados(achados.Itens);
                var mudouEstimativa = NR.EstimativaAtual != achados.Estimativa;
                NR.MostrarEstimativa(achados.Estimativa);
                var etapa = e.Situacao?.Etapa;
                var progresso = etapa is null
                    ? ordemDoKit.Para(e.Provedor).Select(chave => achados.Etapas.GetValueOrDefault(chave)).LastOrDefault(p => p is not null)
                    : achados.Etapas.GetValueOrDefault(etapa);
                if (NR.ProgressoAtual != progresso)
                {
                    NR.MostrarProgresso(progresso);
                    RenderizarExecucao(e);
                }
                else if (mudouEstimativa) RenderizarExecucao(e);
                var ((consumo, amostra), detalhes, orcamento, evidencias, modelos, numerica) = await Task.Run(() => (
                    ConsumoAoVivo.Ler(controle, desde, e.Provedor, Path.GetFileName(e.Registro.Pasta), ordemDoKit, sessoes,
                        e.Fim?.IsCompleted == true ? null : DateTimeOffset.UtcNow, e.InicioUtc),
                    DetalhesDasEtapas.Ler(controle, etapasJson, desde),
                    e.Provedor == Provedor.ChatGpt ? OrcamentoDaAnalise.Ler(controle, desde) : null,
                    Directory.Exists(PastaEvidenciasDoMonitor(e)),
                    e.ResumoModelos(), ConferenciaNumerica.Ler(e.PastaDeTrabalho, e.Provedor, e.RegistroDeTrabalho)));
                if (fechada || !ReferenceEquals(e, execucaoMostrada)) return;
                NR.ModelosConfigurados = modelos;
                NR.ConferenciaNumericaExibida = numerica;
                NR.MostrarEvidencias(evidencias);
                NR.MostrarDetalhesDasEtapas(detalhes);
                NR.MostrarConsumo(consumo, orcamento, amostra);
            }
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { RegistroErros.Gravar("analise.achados", x); }
        finally { lendoAchados = false; }
    }

    /// <summary>As durações das etapas de todas as análises sob a pasta de processos (para a previsão de término), lidas uma vez por sessão em segundo plano;
    /// null enquanto a leitura não terminou (o Monitor mostra "histórico insuficiente" até lá).</summary>
    IReadOnlyList<DuracaoEtapa>? HistoricoDeDuracoes()
    {
        var leitura = UsoDasIas.HistoricoDaSessaoAsync(ajustes.PastaProcessosEfetiva, OrdemDasEtapasDoKit());
        return leitura.IsCompletedSuccessfully ? leitura.Result : null;
    }

    /// <summary>A sequência das etapas que o kit define (<c>references\etapas.json</c> do plugin analise instalado); sem o arquivo (kit 3.x) ou com arquivo
    /// inválido, a ordem padrão. O Monitor, o progresso e a previsão seguem esta ordem, não uma fixa no código. Tarefa 19: com o kit que tem a tabela de
    /// modelos, vale também o liga/desliga da auditoria cruzada deste computador (Modelos da análise).</summary>
    OrdemDasEtapas OrdemDasEtapasDoKit()
    {
        var ordem = OrdemDasEtapas.Ler(pecasAgentes.EtapasJson);
        return pecasAgentes.ModelosPorRota is null ? ordem
            : ordem.ComAjuste(AjusteDosModelos.Ligadas.Select(k => (k.Key.Etapa, ModelosDaAnalise.ProvedorDaRota(k.Key.Rota), k.Value)));
    }

    /// <summary>Desde quando os arquivos do kit são desta análise: a criação da pasta do registro (o mesmo registro numa retomada), com 5 s de folga. O que o
    /// kit gravou antes disso (a análise anterior, já entregue) não aparece no Monitor.</summary>
    static DateTime InicioDoRegistro(ExecucaoEmCurso e)
    {
        var inicio = e.InicioUtc;
        try
        {
            var criacao = Directory.GetCreationTimeUtc(e.Registro.Pasta);
            if (criacao.Year > 2000 && criacao < inicio) inicio = criacao;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
        return inicio.AddSeconds(-5);
    }

    void AtualizarRelogioDaExecucao(ExecucaoEmCurso e)
    {
        NR.MostrarIdentificacao(FluxoNovoRelatorio.Identificacao(e.Processo), e.Processo.Requerente);
        if (NR.DetalhesVisiveis && e.Provedor == Provedor.ChatGpt) _ = MostrarUltimaMensagemAsync(e);
        NR.MostrarRelogios(e.TempoAtivo, DateTimeOffset.Now, e.TempoDecorrido);
    }

    void RenderizarExecucao(ExecucaoEmCurso e)
    {
        if (fechada) return;
        var n = NR;
        // Claude e Gemini: o kit informa a etapa; ChatGPT (Tarefa 12): o editor monta a mesma situação a partir do controlador (etapa, cota, decisão).
        var claude = Provedores.PeloKit(e.Provedor) || e.Situacao is not null;
        n.PainelClaude.Visibility = Visibility.Visible;
        n.PainelUltimaMensagem.Visibility = e.Provedor == Provedor.ChatGpt ? Visibility.Visible : Visibility.Collapsed;
        var rodando = e.Fim is null || !e.Fim.IsCompleted;
        string? erro = null;
        IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas = [];
        var chatgpt = e.PastaAnaliseChatGpt is not null;
        var ordem = chatgpt ? OrquestradorAnalise.EtapasAnaliseChatGpt : OrdemDasEtapasDoKit().Para(e.Provedor);
        if (claude)
        {
            var s = e.Situacao;
            erro = string.IsNullOrWhiteSpace(s?.Erro) ? null : s!.Erro;
            var etapa = s?.Etapa ?? n.ProgressoAtual?.Etapa ?? n.EstimativaAtual?.Etapa;
            if (chatgpt && etapa == "analisar_grupos" && n.MonitorChatGpt is { Rodadas.Count: > 0 } monitor &&
                monitor.Rodadas.Where(r => r.Perfil.Numero < 15).All(r => r.Realizadas == r.Verificacoes))
                etapa = "auditar_relatorio";
            etapas = FluxoNovoRelatorio.Etapas(etapa, s?.Codigo ?? "",
                ordem);
            // A etapa em que o kit informou erro fica vermelha.
            if (erro is not null) etapas = etapas.Select(x => x.Estado == EstadoEtapa.Agora ? (x.Nome, EstadoEtapa.Erro) : x).ToList();
        }
        else
        {
            etapas = FluxoNovoRelatorio.Etapas(n.ProgressoAtual?.Etapa ?? n.EstimativaAtual?.Etapa, "", ordem);
        }
        if (e.Fim is { IsCompletedSuccessfully: true } fim && fim.Result.Situacao == "concluida"
            || n.EstimativaAtual is { PeloEditor: true, Etapa: null, PercentualGeral: 100 } && erro is null)
            etapas = FluxoNovoRelatorio.Etapas(null, "CONCLUIDO", ordem);
        if (!claude && e.Linhas.LastOrDefault() is { } ultima && ultima.StartsWith("Erro: ", StringComparison.Ordinal))
        {
            erro = ultima[6..];
            etapas = etapas.Select(x => x.Estado == EstadoEtapa.Agora ? (x.Nome, EstadoEtapa.Erro) : x).ToList();
        }
        if (chatgpt)
        {
            var fimChatGpt = e.Fim is { IsCompletedSuccessfully: true } terminou ? terminou.Result : null;
            n.PerfilChatGptAtivo = rodando ? n.MonitorChatGpt?.PerfilAtivo ?? e.Situacao?.RodadaChatGpt : null;
            n.AtividadeChatGpt = fimChatGpt?.Mensagem ?? e.Situacao?.Rotulo;
            if (fimChatGpt?.Situacao == "falhou")
            {
                erro = n.AtividadeChatGpt ?? "A análise foi interrompida. Confira os registros.";
                etapas = etapas.Select(x => x.Estado == EstadoEtapa.Agora ? (x.Nome, EstadoEtapa.Erro) : x).ToList();
            }
        }
        n.MostrarResumo(rodando, erro, etapas, Provedores.Nome(e.Provedor), e.Situacao?.Rotulo ?? e.Situacao?.Codigo,
            e.Situacao?.Codigo is "DECISAO" or "AGUARDANDO_COTA");
        AtualizarRelogioDaExecucao(e);
    }

    (string Caminho, DateTime Escrita, long Tamanho)? ultimaMensagemLida;
    ExecucaoEmCurso? lendoMensagem;

    async Task MostrarUltimaMensagemAsync(ExecucaoEmCurso e)
    {
        if (ReferenceEquals(lendoMensagem, e)) return;
        lendoMensagem = e;
        try
        {
            var anterior = ultimaMensagemLida;
            var resultado = await Task.Run(() =>
            {
                var arquivo = new FileInfo(e.UltimaMensagem);
                var chave = (e.UltimaMensagem, arquivo.LastWriteTimeUtc, arquivo.Exists ? arquivo.Length : -1L);
                if (anterior == chave) return (chave, texto: (string?)null);
                if (!arquivo.Exists) return (chave, texto: "O ChatGPT ainda não deixou a última mensagem.");
                using var fs = new FileStream(e.UltimaMensagem, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var leitor = new StreamReader(fs, Encoding.UTF8);
                var buffer = new char[200_000];
                var lidos = leitor.ReadBlock(buffer, 0, buffer.Length);
                return (chave, texto: lidos == 0 ? "O ChatGPT ainda não deixou a última mensagem." : new string(buffer, 0, lidos));
            });
            if (ReferenceEquals(execucaoMostrada, e) && resultado.texto is { } texto)
            {
                ultimaMensagemLida = resultado.chave;
                NR.TextoUltimaMensagem.Text = texto;
            }
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException)
        {
            if (ReferenceEquals(execucaoMostrada, e))
            {
                ultimaMensagemLida = null;
                NR.TextoUltimaMensagem.Text = "A última mensagem não pôde ser lida: " + Erro(x);
            }
        }
        finally { if (ReferenceEquals(lendoMensagem, e)) lendoMensagem = null; }
    }

    /// <summary>Contrato da Tarefa 2 (código 21 / <c>DECISAO</c>): o kit parou para o analista decidir. Mostra a pergunta e as opções (ou uma caixa de texto,
    /// sem opções) e devolve o valor escolhido; null = "Cancelar" (a análise fica parada, sem gastar nada, e "Continuar a análise" pergunta de novo).
    /// A escolha de peças ambíguas (decisão do tipo <c>pecas</c>, Tarefa 14) abre o diálogo com as versões lado a lado (<see cref="EscolherVersaoDaPecaAsync"/>).</summary>
    async Task<string?> DecidirAsync(ExecucaoEmCurso e, DecisaoAnalise d, CancellationToken ct)
    {
        if (fechada || ct.IsCancellationRequested) return null;
        if (d.Tipo == "modelos_indisponiveis")
        {
            var configuracaoRecusada = EscolhaModelosDaExecucao.Ler(d.Json);
            if (configuracaoRecusada is null || ModelosDaAnalise.ProvedorDaRota(configuracaoRecusada.Rota) != e.Provedor) return null;
            NotificacoesWindows.Mostrar("A conta recusou a configuração de uma etapa", "Abra o Editor para escolher o modelo e retomar a análise.");
            var catalogo = CatalogoDeModelos;
            var oferta = await Task.Run(() => catalogo.ConsultarParaDecisaoAsync(e, configuracaoRecusada, ct), ct);
            if (fechada || ct.IsCancellationRequested) return null;
            var tituloModelo = "Modelos desta análise • " + FluxoNovoRelatorio.Identificacao(e.Processo);
            return await Perguntas.Atual.EscolherModelosAsync(tituloModelo, configuracaoRecusada, oferta, ct);
        }
        if (d.Tipo is not ("limite_cota" or "revalidar_preparo" or "plugin_antigo"))
        {
            if (d.Tipo == "entradas_mudaram")
                return d.Opcoes.FirstOrDefault(o => o.Valor is "nova_analise" or "refazer")?.Valor;
            throw new InvalidOperationException("O controlador não conseguiu continuar automaticamente. " + d.Pergunta);
        }
        NotificacoesWindows.Mostrar("A análise precisa de uma decisão", "Abra o Editor para responder; a análise está parada e não gasta cota enquanto espera.");
        var titulo = (d.Tipo == "limite_cota" ? "Limite preventivo da cota de 5 horas • " : "Decisão na análise de ") + FluxoNovoRelatorio.Identificacao(e.Processo);
        var texto = d.Pergunta.Trim() + Environment.NewLine + Environment.NewLine
            + $"A análise com o {Provedores.Nome(e.Provedor)} está parada e não gasta cota enquanto espera. \"Cancelar\" mantém a pausa. Use \"Retomar análise\" para ver a pergunta de novo.";
        if (d.Opcoes.Count == 0)
            return await Perguntas.Atual.PedirTextoAsync(titulo, texto, "", botao: "Responder") is { } livre && !string.IsNullOrWhiteSpace(livre) ? livre.Trim() : null;
        var escolha = await Perguntas.Atual.EscolherNaListaAsync(titulo, texto, d.Opcoes.Select(o => o.Texto).ToList(), -1, "Responder");
        return escolha is { } i && i >= 0 && i < d.Opcoes.Count ? d.Opcoes[i].Valor : null;
    }

    /// <summary>Tarefa 14: o kit achou mais de uma versão possível de uma peça. Mostra as versões (miniatura do <c>cache-pdf</c> ou o nome do arquivo, e "Abrir
    /// o PDF"), com o motivo da dúvida; devolve o caminho escolhido (o <c>opcoes[].valor</c>, gravado pelo orquestrador em <c>decisao.arquivo_resposta</c>)
    /// ou null = "Cancelar" (a análise fica parada antes de gastar IA). Vale para as três rotas.</summary>
    internal async Task<string?> EscolherVersaoDaPecaAsync(string pastaProcesso, string titulo, Provedor provedor, DecisaoAnalise d)
    {
        var versoes = await Task.Run(() => PecasAmbiguas.Candidatas(d, pastaProcesso));
        if (fechada) return null;
        var aviso = $"A análise com o {Provedores.Nome(provedor)} está parada e não gasta cota enquanto espera. \"Cancelar\" mantém a pausa. "
            + "Use \"Retomar análise\" para ver a pergunta de novo.";
        return await Perguntas.Atual.EscolherPecaAsync(titulo, d.Pergunta.Trim(), aviso, versoes);
    }

    /// <summary>"Cancelar análise", com a confirmação do provedor. true = cancelou (o Codex foi encerrado, ou o processo do kit).</summary>
    internal async Task<bool> CancelarAnaliseAsync(ExecucaoEmCurso e)
    {
        var chatgpt = e.Provedor == Provedor.ChatGpt;
        // Ajustes › Inteligência artificial › "Confirmar antes de cancelar uma análise" (padrão: ligado, como sempre).
        if (ajustes.ConfirmarCancelar && !await Perguntas.Atual.ConfirmarAsync(chatgpt ? FluxoNovoRelatorio.CancelarChatGptTitulo
                : e.Provedor == Provedor.Gemini ? FluxoNovoRelatorio.CancelarGeminiTitulo : FluxoNovoRelatorio.CancelarClaudeTitulo,
                chatgpt ? FluxoNovoRelatorio.CancelarChatGptTexto : FluxoNovoRelatorio.CancelarClaudeTexto, FluxoNovoRelatorio.CancelarAnalise, FluxoNovoRelatorio.Voltar))
            return false;
        var ok = await sessaoNR.CancelarAsync(e);
        if (!ok) MostrarMensagem(InfoBarSeverity.Warning, "A análise não foi cancelada", "O processo da análise do kit não foi encontrado (ele pode já ter terminado).");
        return ok;
    }

    /// <summary>O fim da execução, já com a importação (o <c>.rpmvni</c> aberto) ou com o motivo à vista.</summary>
    internal Task<FimAnalise> AguardarAnaliseAsync(ExecucaoEmCurso e) => e.Conclusao ?? e.Fim;

    internal void MostrarAvisosDaEntrega(AvisosDaEntrega avisos)
    {
        if (avisos.Acoes.Count == 0) return;
        MostrarMensagem(InfoBarSeverity.Warning, "Avisos desta análise", avisos.Resumo);
        var botao = new Button { Content = "Ver registro" };
        botao.Click += (_, _) =>
        {
            if (!AberturaExterna.Abrir(avisos.Registro, out _))
                AberturaExterna.MostrarNoExplorador(avisos.Registro);
        };
        AvisoDoModulo.ActionButton = botao;
    }

    async Task<FimAnalise> ConcluirAsync(ExecucaoEmCurso e)
    {
        // Começa dentro do comando Analisar (que está na vez da fila): o fim chega muito depois, fora dele, e a importação espera a vez como qualquer comando.
        naVez.Value = false;
        FimAnalise fim;
        try { fim = await e.Fim; }
        catch (Exception x) { fim = new FimAnalise("falhou", null, null, "A análise não pôde ser executada: " + Erro(x) + ". Veja o registro: " + e.Registro.Pasta + ".", null); }
        sessaoNR.Remover(e);
        e.Cancelamento.Dispose();
        if (fechada) return fim;
        if (e.Abandonada) { AtualizarTituloAnalise(); AtualizarBotoesNR(); return fim; }
        var paraRevisao = fim.Situacao == "para_revisao";
        var comRelatorio = (fim.Situacao == "concluida" || paraRevisao) && fim.Documento is not null;
        // A análise do Claude sai dos ajustes quando acaba sem relatório; com relatório, só depois de importado (até lá o editor reaberto ainda o oferece).
        if (Provedores.PeloKit(e.Provedor) && !comRelatorio && !e.PausaSolicitada && !fim.AcompanhamentoPendente && fim.Decisao is null) EsquecerPendenteClaude(e.Registro.Pasta);
        // ChatGPT: fica nos ajustes só o que dá para continuar (o controle do kit ficou na pasta do processo: falha, cancelamento, decisão sem resposta).
        var continuavel = false;
        if (e.Provedor == Provedor.ChatGpt)
        {
            continuavel = fim.Situacao is "cota_esgotada" or "falhou" or "cancelada" or "sem_resultado" or "interrompida"
                && await Task.Run(() => ContinuavelChatGpt(e.Processo.Pasta, e.Registro.Pasta));
            if (!comRelatorio && !continuavel && !e.PausaSolicitada && fim.Situacao != "sem_resultado") EsquecerPendenteChatGpt(e.Registro.Pasta);
        }
        var naVista = ReferenceEquals(execucaoMostrada, e) && PaginaNovoRelatorioVisivel;
        if (ReferenceEquals(execucaoMostrada, e)) MostrarFormularioNR();
        // Ruling FD-I3: o fim em segundo plano nunca troca de módulo nem fecha o que está aberto; abre direto só com o usuário na vista desta execução,
        // sem relatório aberto e sem despacho em edição (conferido na vez da fila). Senão, "Relatório pronto" com "Abrir relatório".
        if (comRelatorio)
        {
            string? recusa = null;
            var importado = await ImportarAsync(new ImportacaoPendente(e.Provedor, e.Processo, e.Registro.Pasta, fim.Documento, fim.Pdf, paraRevisao),
                () => naVista && PodeAbrirSemInterromper, motivo => recusa = motivo);
            if (recusa is not null) fim = fim with { Situacao = "interrompida", Mensagem = recusa };
            if (e.PastaAnaliseChatGpt is null)
            {
                var controle = Path.Combine(e.PastaDeTrabalho, ResultadoAnalise.PastaControle(e.Provedor));
                var avisos = await Task.Run(() => AvisosDaEntrega.Ler(controle, e.InicioUtc, e.Registro.Pasta));
                if (!fechada) MostrarAvisosDaEntrega(avisos);
            }
            if (!fechada)
                NotificacoesWindows.Mostrar(importado && paraRevisao ? "Versão para revisão disponível" : importado ? "Relatório pronto" : recusa is null ? "Relatório aguardando importação" : "Entrega recusada pelo Editor",
                    importado && paraRevisao ? fim.Mensagem ?? "O relatório identifica as etapas pendentes."
                    : importado ? "A análise foi concluída e o relatório está disponível para revisão."
                        : recusa is not null ? "Abra o Editor para conferir os motivos e retomar a correção."
                        : "A análise terminou. Abra o Editor para tentar importar o relatório novamente.");
        }
        else
        {
            MostrarResultadoNR(e, fim, continuavel);
            if (!e.PausaSolicitada && fim.Situacao != "cancelada")
            {
                var titulo = fim.Situacao switch
                {
                    "interrompida" when fim.Decisao?.Tipo == "limite_cota" => "Análise pausada por cota",
                    "interrompida" => "Análise interrompida",
                    "cota_esgotada" => "Cota da IA esgotada",
                    _ => "A análise não gerou relatório",
                };
                NotificacoesWindows.Mostrar(titulo, "Abra o Editor para conferir o registro e as opções de retomada.");
            }
        }
        RegistrarFimNaPilula(e, fim);
        AtualizarTituloAnalise();
        AtualizarBotoesNR();
        await CarregarHistoricoAsync();
        return fim;
    }

    /// <summary>A análise do ChatGPT a continuar com "Continuar a análise" (fim com falha, cancelamento ou decisão sem resposta, ou achada na reabertura).</summary>
    sealed record ContinuacaoChatGpt(Processo Processo, string Registro);

    ContinuacaoChatGpt? continuarChatGpt;

    /// <summary>Dá para continuar a análise do ChatGPT: o controle do analise ficou na pasta do processo e é deste registro
    /// (<see cref="OrquestradorAnalise.PodeRetomarChatGpt"/>; o controlador continua sem <c>--inputs</c>) ou o registro é do Editor 2.2 e guardou a sessão
    /// controladora (a análise recomeça no mesmo registro, com as mesmas peças).</summary>
    static bool ContinuavelChatGpt(string pastaProcesso, string pastaRegistro)
    {
        if (OrquestradorAnalise.RegistroAnaliseChatGpt(pastaRegistro))
            return Directory.Exists(OrquestradorAnalise.PastaAnaliseChatGpt(PastaDados.Raiz, pastaProcesso, pastaRegistro));
        if (Argumentos.Atual.Roteiro is null || TravaDeProgramas.Permitidos is null) return false;
        var pasta = OrquestradorAnalise.ProcessoDoTrabalho(pastaProcesso, pastaRegistro);
        return OrquestradorAnalise.PodeRetomarChatGpt(pasta, OrquestradorAnalise.RegistroDoTrabalho(pasta, pastaRegistro))
            || RecuperacaoAnaliseLocal.Existe(pastaProcesso, Path.GetFileName(pastaRegistro))
            || LerSessaoCodex(pastaRegistro) is not null;
    }

    /// <summary>A sessão do Codex guardada no <c>execucao.json</c> por um registro do Editor 2.2; null sem ela.</summary>
    static string? LerSessaoCodex(string pastaRegistro)
    {
        try
        {
            return System.Text.Json.Nodes.JsonNode.Parse(RegistroExecucao.LerTextoDaExecucao(pastaRegistro))?["sessao_codex"]?.GetValue<string>() is { Length: > 0 and <= 128 } s ? s : null;
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    void EsquecerPendenteChatGpt(string registro)
    {
        if (ajustes.ExecucoesChatGpt.RemoveAll(x => MesmoRegistro(x.Registro, registro)) > 0) SalvarAjustes();
    }

    /// <summary>Na abertura (2026-11-17): uma análise do ChatGPT que não terminou (o editor caiu ou a cota acabou) é oferecida para continuar de onde parou.</summary>
    internal async Task RetomarExecucoesChatGptAsync()
    {
        await AtualizarReferenciasDeRegistroAsync(ajustes.ExecucoesChatGpt);
        await CarregarHistoricoAsync();
        if (fechada) return;
        var anterior = historicoAnalises.DistinctBy(h => h.Processo.Pasta, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(h => string.Equals(h.Usuario, Environment.UserName, StringComparison.OrdinalIgnoreCase) && h.Provedor == Provedor.ChatGpt && h.Situacao == "sem_resultado"
            && ContinuavelChatGpt(h.Processo.Pasta, h.Registro) && File.Exists(Path.Combine(h.Registro, "continuidade.json")));
        if (anterior is not null && !ajustes.ExecucoesChatGpt.Any(p => MesmoRegistro(p.Registro, anterior.Registro)))
        {
            ajustes.ExecucoesChatGpt.Insert(0, new ExecucaoClaudePendente(anterior.Processo.Pasta, anterior.Registro));
            SalvarAjustes();
        }
        foreach (var pend in ajustes.ExecucoesChatGpt.ToList())
        {
            if (!RoteiroLegado && !OrquestradorAnalise.RegistroAnaliseChatGpt(pend.Registro)) continue;
            if (sessaoNR.EmAndamento(pend.Processo)) continue;
            var historico = historicoAnalises.FirstOrDefault(h => MesmoRegistro(h.Registro, pend.Registro));
            if (historico is { Situacao: "descartada" } or { Situacao: "concluida", Pacote: not null })
            {
                EsquecerPendenteChatGpt(pend.Registro);
                continue;
            }
            var valido = await Task.Run(() => RegistroExecucao.MotivoInvalido(pend.Processo, pend.Registro) is not null
                ? false : RegistroExecucao.PastaDisponivel(pend.Registro));
            if (valido is null) continue;
            if (valido == false) { EsquecerPendenteChatGpt(pend.Registro); continue; }
            var processo = await ProcessoDaPastaAsync(pend.Processo);
            if (await MostrarRecusaGuardadaAsync(Provedor.ChatGpt, processo, pend.Registro)) break;
            if (historico?.Situacao == "concluida")
            {
                var entrega = new ImportacaoPendente(Provedor.ChatGpt, processo, pend.Registro, null, null);
                if (!importacoesPendentes.Any(i => MesmoRegistro(i.Registro, pend.Registro))) importacoesPendentes.Add(entrega);
                MostrarImportacaoPendente(entrega, FluxoNovoRelatorio.TextoKitTerminou(Provedor.ChatGpt, processo), InfoBarSeverity.Success);
                break;
            }
            continuarChatGpt = new ContinuacaoChatGpt(processo, pend.Registro);
            var aviso = NR.AvisoResultado;
            aviso.Severity = InfoBarSeverity.Warning;
            aviso.Title = "Uma análise com o ChatGPT ficou pela metade";
            aviso.Message = $"A análise de {FluxoNovoRelatorio.Identificacao(processo)} parou antes da entrega. Clique em \"Retomar análise\" para seguir de onde parou.";
            if (fechada) return;
            NR.BotaoDescartar.Visibility = Visibility.Visible;
            NR.BotaoImportarPendente.Visibility = Visibility.Collapsed;
            NR.BotaoMostrarRegistroResultado.Visibility = Visibility.Collapsed; NR.LinkRemontarTeste.Visibility = Visibility.Collapsed;
            aviso.IsOpen = true;
            // Fluxo linear do kit: objeção pendente bloqueia a entrega. O Editor não pergunta nem retoma sozinho (a retomada mandaria a análise adiante
            // com a objeção); fica o aviso, com "Retomar análise" e "Descartar análise" à mão.
            if (RoteiroLegado && await Task.Run(() => ObjecaoPendenteDo(OrquestradorAnalise.ProcessoDoTrabalho(pend.Processo, pend.Registro), Provedor.ChatGpt)) is { } objecaoChatGpt)
            {
                aviso.Message = FluxoNovoRelatorio.TextoRetomadaComObjecao(objecaoChatGpt);
                if (!PaginaNovoRelatorioVisivel) MostrarAvisoDeRetomada(aviso, processo, pend.Registro);
                break;
            }
            // 2026-11-17 (pedido do usuário): pergunta na hora se retoma, e com qual IA. "Agora não" deixa o aviso com "Continuar a análise".
            var (escolha, descartada) = await PerguntarRetomadaAsync(processo, pend.Registro, Provedor.ChatGpt);
            if (descartada) continue;
            if (escolha == Provedor.ChatGpt) await ContinuarAnaliseAsync();
            else if (escolha is { } alternativa)
            {
                if (await ContinuarComOutraIaAsync(alternativa, processo, pend.Registro) is not null) continuarChatGpt = null;
            }
            else if (!PaginaNovoRelatorioVisivel) MostrarAvisoDeRetomada(aviso, processo, pend.Registro);
            break;   // uma de cada vez: a próxima aparece na próxima abertura
        }
    }

    /// <summary>A análise terminou sem relatório: mostra o motivo e oferece acesso ao registro e à retomada quando disponível.</summary>
    void MostrarResultadoNR(ExecucaoEmCurso e, FimAnalise fim, bool continuavelChatGpt = false)
    {
        ultimoResultadoNR = e;
        continuarChatGpt = continuavelChatGpt ? new ContinuacaoChatGpt(e.Processo, e.Registro.Pasta) : null;
        var aviso = NR.AvisoResultado;
        var nome = Provedores.Nome(e.Provedor);
        aviso.Severity = fim.Situacao == "cancelada" ? InfoBarSeverity.Informational : InfoBarSeverity.Error;
        aviso.Title = fim.Situacao switch
        {
            "cancelada" => $"A análise com o {nome} foi cancelada",
            "interrompida" when fim.Decisao?.Tipo == "limite_cota" => "Análise pausada por cota",
            "interrompida" => $"A análise com o {nome} parou",
            "cota_esgotada" => $"A cota do {nome} acabou",
            _ => $"A análise com o {nome} não gerou relatório",
        };
        if (fim.Situacao == "cota_esgotada") aviso.Severity = InfoBarSeverity.Warning;
        aviso.Message = (fim.Mensagem ?? fim.Situacao)
            + (fim.Situacao == "falhou" && continuavelChatGpt ? " Use \"Retomar análise\" para seguir de onde parou." : "");
        if (fim.Decisao is not null) aviso.Severity = InfoBarSeverity.Warning;
        NR.BotaoDescartar.Visibility = e.PausaSolicitada || (Provedores.PeloKit(e.Provedor) && fim.Situacao is "falhou" or "interrompida" or "cota_esgotada" or "cancelada") || continuavelChatGpt
            ? Visibility.Visible : Visibility.Collapsed;
        if (e.PausaSolicitada && NR.BotaoDescartar.Visibility == Visibility.Visible)
        {
            aviso.Severity = InfoBarSeverity.Informational;
            aviso.Title = $"Análise com {nome} pausada";
            aviso.Message = "O tempo ativo foi guardado. Retome a análise para continuar.";
        }
        NR.BotaoImportarPendente.Visibility = Visibility.Collapsed;
        NR.BotaoMostrarRegistroResultado.Visibility = Visibility.Visible;
        aviso.IsOpen = true;
        if (!PaginaNovoRelatorioVisivel)
        {
            if (continuavelChatGpt) MostrarAvisoDeRetomada(aviso, e.Processo, e.Registro.Pasta);
            else MostrarMensagem(aviso.Severity, aviso.Title, aviso.Message);
        }
        _ = AtualizarRemontagemTesteAsync(e);
    }

    void MostrarAvisoDeRetomada(InfoBar origem, Processo processo, string registro)
    {
        MostrarMensagem(origem.Severity, origem.Title, origem.Message);
        var aviso = AvisoDoModulo;
        var botao = new Button { Content = "Retomar análise" };
        IconeDeAcao.SetGlifo(botao, Glifos.Executar);
        Dicas.Aplicar(botao, "Retomar análise", $"Continuar a análise de {FluxoNovoRelatorio.Identificacao(processo)}.");
        botao.Click += async (_, _) =>
        {
            botao.IsEnabled = false;
            try { await TentarAsync(() => AbrirHistoricoAsync(processo, registro)); }
            finally { botao.IsEnabled = true; }
        };
        aviso.ActionButton = botao;
    }

    bool remontandoTeste;

    /// <summary>"Remontar relatório (teste)" só aparece com o <c>producao_teste.py</c> no kit e a cópia certificada da auditoria técnica nesta análise.</summary>
    async Task AtualizarRemontagemTesteAsync(ExecucaoEmCurso e)
    {
        NR.LinkRemontarTeste.Visibility = Visibility.Collapsed;
        if (!RoteiroLegado || pecasAgentes.ProducaoTeste is null || pecasAgentes.Python is null || remontandoTeste) return;
        var controle = Path.Combine(e.PastaDeTrabalho, ResultadoAnalise.PastaControle(e.Provedor));
        var pode = await Task.Run(() => ProducaoTeste.TemCopiaCertificada(controle));
        if (!fechada && ReferenceEquals(ultimoResultadoNR, e) && NR.BotaoMostrarRegistroResultado.Visibility == Visibility.Visible)
            NR.LinkRemontarTeste.Visibility = pode ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Roda o <c>producao_teste.py</c> do kit (contrato: <c>PROCESSO --rota R --saida PASTA</c>), com a saída dentro do registro desta análise, e mostra
    /// o PDF gerado no Explorador. Não mexe no controle da análise nem chama IA.</summary>
    async Task RemontarRelatorioTesteAsync(ExecucaoEmCurso e)
    {
        if (!RoteiroLegado || remontandoTeste || pecasAgentes.ProducaoTeste is not { } script || pecasAgentes.Python is not { } python) return;
        remontandoTeste = true;
        NR.LinkRemontarTeste.IsEnabled = false;
        try
        {
            var saida = Path.Combine(e.Registro.Pasta, "producao-teste");
            Directory.CreateDirectory(saida);
            var (exe, args) = LinhasDeComando.ProducaoTeste(python, pecasAgentes.PythonPeloLauncher, script, e.PastaDeTrabalho, e.Provedor, saida);
            MostrarMensagem(InfoBarSeverity.Informational, "Remontando o relatório (teste)", "O PDF está sendo gerado numa pasta separada, a partir da cópia certificada.");
            var (codigo, texto) = await ExecucaoProcesso.RodarAsync(exe, args, e.PastaDeTrabalho, _ => { }, TimeSpan.FromMinutes(10), CancellationToken.None);
            if (fechada) return;
            if (codigo == 0 && ProducaoTeste.PdfDaSaida(texto) is { } pdf && File.Exists(pdf))
            {
                MostrarMensagem(InfoBarSeverity.Success, "Relatório remontado (teste)", "PDF gerado em " + pdf + ". A análise não foi alterada.");
                AberturaExterna.MostrarNoExplorador(Path.GetDirectoryName(pdf)!);
            }
            else
            {
                var motivo = texto.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? $"código {codigo}";
                MostrarMensagem(InfoBarSeverity.Warning, "O relatório não foi remontado", motivo);
            }
        }
        finally
        {
            remontandoTeste = false;
            NR.LinkRemontarTeste.IsEnabled = true;
        }
    }

    // ---------- barra de título ----------

    /// <summary>"Analisando com o ChatGPT · 0001/2099 · 12 min" da execução mais antiga ("+N" com mais de uma); null sem execução.</summary>
    internal string? TituloAnalise
    {
        get
        {
            var ativas = sessaoNR.Execucoes.Where(e => !e.Abandonada).ToList();
            if (ativas.Count == 0) return null;
            var e = ativas[0];
            return FluxoNovoRelatorio.TituloExecucao(e.Provedor, e.Processo, e.TempoAtivo)
                   + (ativas.Count > 1 ? " +" + (ativas.Count - 1).ToString(CultureInfo.InvariantCulture) : "");
        }
    }

    void AtualizarTituloAnalise()
    {
        if (fechada) return;
        var titulo = TituloAnalise;
        if (titulo is not null)
        {
            // Em andamento: o texto vem do serviço ("Analisando com o Claude · 19486/2026 · 13 min"), em duas linhas.
            fimNaPilula = null;
            relogioFimPilula?.Stop();
            var corte = titulo.IndexOf(" · ", StringComparison.Ordinal);
            MostrarPilulaAnalise(EstadoPilulaAnalise.EmAndamento, corte < 0 ? titulo : titulo[..corte], corte < 0 ? "" : titulo[(corte + 3)..], null);
            if (!relogioAnalise.IsRunning) relogioAnalise.Start();
            return;
        }
        relogioAnalise.Stop();
        if (fimNaPilula is { } f)
            MostrarPilulaAnalise(f.Estado, f.Estado switch
            {
                EstadoPilulaAnalise.Concluida => "Análise concluída",
                EstadoPilulaAnalise.Pausada => "Análise pausada",
                _ => "Análise com erro",
            }, f.Detalhe, f.Mensagem);
        else MostrarPilulaAnalise(EstadoPilulaAnalise.Inativa, "", "", null);
    }

    /// <summary>O fim da execução na pílula (redesign 2026-11-17), com o processo e o tempo que ela levou. Cancelada não fica: a pílula some.</summary>
    void RegistrarFimNaPilula(ExecucaoEmCurso e, FimAnalise fim)
    {
        EstadoPilulaAnalise? estado = fim.Situacao switch
        {
            "concluida" when fim.Documento is not null => EstadoPilulaAnalise.Concluida,
            "concluida" => EstadoPilulaAnalise.Erro,   // terminou sem relatório para importar (o motivo fica no Novo relatório)
            "interrompida" or "cota_esgotada" => EstadoPilulaAnalise.Pausada,
            "falhou" or "sem_resultado" => EstadoPilulaAnalise.Erro,
            _ => null,
        };
        if (estado is null) { fimNaPilula = null; return; }
        var titulo = FluxoNovoRelatorio.TituloExecucao(e.Provedor, e.Processo, e.TempoAtivo);
        var corte = titulo.IndexOf(" · ", StringComparison.Ordinal);
        fimNaPilula = (estado.Value, corte < 0 ? FluxoNovoRelatorio.Identificacao(e.Processo) : titulo[(corte + 3)..], fim.Mensagem);
        // A concluída sai sozinha depois de um minuto (o relatório já foi aberto ou oferecido em "Relatório pronto").
        relogioFimPilula?.Stop();
        if (estado != EstadoPilulaAnalise.Concluida) return;
        relogioFimPilula ??= DispatcherQueue.GetForCurrentThread().CreateTimer();
        relogioFimPilula.Interval = TimeSpan.FromMinutes(1);
        relogioFimPilula.IsRepeating = false;
        relogioFimPilula.Tick -= AoVencerFimPilula;
        relogioFimPilula.Tick += AoVencerFimPilula;
        relogioFimPilula.Start();
    }

    void AoVencerFimPilula(DispatcherQueueTimer t, object o)
    {
        if (fimNaPilula is { Estado: EstadoPilulaAnalise.Concluida }) { fimNaPilula = null; AtualizarTituloAnalise(); }
    }

    /// <summary>Pinta a pílula: o anel (na cor de destaque do Windows) gira só em andamento; nos outros estados, o ícone do estado na cor
    /// semântica do tema (sucesso, atenção, crítico). O texto completo vai na dica e no nome para o Narrador.</summary>
    void MostrarPilulaAnalise(EstadoPilulaAnalise estado, string principal, string detalhe, string? mensagem)
    {
        estadoPilula = estado;
        TextoBotaoAnalise.Text = principal;
        DetalheBotaoAnalise.Text = detalhe.Length == 0 ? "" : "· " + detalhe;
        DetalheBotaoAnalise.Visibility = detalhe.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var emAndamento = estado == EstadoPilulaAnalise.EmAndamento;
        HaloDoMonitor.Ativo = emAndamento;
        AnelAnalise.IsActive = emAndamento;
        AnelAnalise.Visibility = emAndamento ? Visibility.Visible : Visibility.Collapsed;
        static Visibility Se(bool v) => v ? Visibility.Visible : Visibility.Collapsed;
        IconeAnaliseConcluida.Visibility = Se(estado == EstadoPilulaAnalise.Concluida);
        IconeAnalisePausada.Visibility = Se(estado == EstadoPilulaAnalise.Pausada);
        IconeAnaliseErro.Visibility = Se(estado == EstadoPilulaAnalise.Erro);

        var nome = estado switch
        {
            EstadoPilulaAnalise.EmAndamento => "Análise em andamento",
            EstadoPilulaAnalise.Concluida => "Análise concluída",
            EstadoPilulaAnalise.Pausada => "Análise pausada",
            EstadoPilulaAnalise.Erro => "Análise com erro",
            _ => "Análise",
        };
        var descricao = estado switch
        {
            EstadoPilulaAnalise.EmAndamento => $"{principal} · {detalhe}. Abre a tela que acompanha a análise que a IA está fazendo agora.",
            EstadoPilulaAnalise.Concluida => $"{detalhe}. Clique para dispensar.",
            _ => $"{detalhe}. {mensagem ?? ""} Clique para ver o resultado no Novo relatório.".Replace("  ", " ", StringComparison.Ordinal),
        };
        Dicas.Aplicar(BotaoAnalise, nome, descricao);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(BotaoAnalise, estado == EstadoPilulaAnalise.Inativa ? nome : $"{nome}: {principal}, {detalhe}");
        BotaoAnalise.Visibility = estado == EstadoPilulaAnalise.Inativa ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- fechar e reabrir ----------

    /// <summary>Confirma a pausa das análises, guarda a retomada e encerra os processos pertencentes à sessão.</summary>
    /// <param name="tratadas">As execuções já perguntadas/informadas neste fechamento (a segunda checagem, na fila, só olha as novas).</param>
    internal async Task<bool> PodeFecharComAnalisesAsync(HashSet<ExecucaoEmCurso>? tratadas = null)
    {
        var ativas = sessaoNR.Execucoes.Where(e => !e.Abandonada && tratadas?.Contains(e) != true).ToList();
        foreach (var e in ativas)
        {
            // Ajustes › Inteligência artificial › "Avisar ao fechar…" desligado: fecha e cancela sem perguntar.
            if (ajustes.ConfirmarFechar && !await Perguntas.Atual.ConfirmarAsync(FluxoNovoRelatorio.FecharTitulo, FluxoNovoRelatorio.TextoFechar(e.Provedor, e.Processo), FluxoNovoRelatorio.FecharSim, FluxoNovoRelatorio.FecharNao))
                return false;
        }
        foreach (var e in ativas.Where(e => e.Provedor == Provedor.ChatGpt))
        {
            await sessaoNR.CancelarAsync(e);
            await Task.WhenAny(e.Fim, Task.Delay(TimeSpan.FromSeconds(5)));
            // Pausada ao fechar (2026-11-17): fica nos ajustes para a reabertura perguntar se retoma de onde parou (codex exec resume).
            if (!ajustes.ExecucoesChatGpt.Any(x => MesmoRegistro(x.Registro, e.Registro.Pasta)))
                ajustes.ExecucoesChatGpt.Add(new ExecucaoClaudePendente(e.Processo.Pasta, e.Registro.Pasta));
            SalvarAjustes();
        }
        var claudes = ativas.Where(e => Provedores.PeloKit(e.Provedor)).ToList();
        if (claudes.Count > 0)
        {
            foreach (var e in claudes.Where(e => !ajustes.ExecucoesClaude.Any(x => string.Equals(x.Registro, e.Registro.Pasta, StringComparison.OrdinalIgnoreCase))))
                ajustes.ExecucoesClaude.Add(new ExecucaoClaudePendente(e.Processo.Pasta, e.Registro.Pasta));
            SalvarAjustes();
            foreach (var e in claudes)
            {
                e.PausaSolicitada = true;
                await sessaoNR.CancelarAsync(e);
                if (!e.Fim.IsCompleted) e.Cancelamento.Cancel();
                await Task.WhenAny(e.Fim, Task.Delay(TimeSpan.FromSeconds(5)));
            }
        }
        tratadas?.UnionWith(ativas);
        return true;
    }

    /// <summary>Na abertura (desenho §12.7): a situação de cada análise do Claude que seguia quando o editor fechou. Ainda em andamento ⇒ volta a acompanhar;
    /// <c>CONCLUIDO</c> ⇒ InfoBar com "Importar relatório"; parou de outro jeito ⇒ o rótulo e o erro do kit; pasta sumida ⇒ sai da lista. Os caminhos vêm do
    /// <c>ajustes.json</c> (editável): um registro fora de <c>&lt;processo&gt;\.pmvni-ai\editor</c> (ou atrás de um atalho) é descartado com o motivo à vista.</summary>
    internal async Task RetomarExecucoesClaudeAsync()
    {
        if (!RoteiroLegado) return;
        await AtualizarReferenciasDeRegistroAsync(ajustes.ExecucoesClaude);
        foreach (var pend in ajustes.ExecucoesClaude.ToList())
        {
            if (sessaoNR.EmAndamento(pend.Processo)) continue;
            if (await Task.Run(() => RegistroExecucao.MotivoInvalido(pend.Processo, pend.Registro)) is { } invalido)
            {
                EsquecerPendenteClaude(pend.Registro);
                MostrarMensagem(InfoBarSeverity.Warning, "Uma análise guardada nos ajustes foi descartada", invalido);
                continue;
            }
            var existe = await Task.Run(() => RegistroExecucao.PastaDisponivel(pend.Registro));
            if (existe is null) continue;
            if (existe == false) { EsquecerPendenteClaude(pend.Registro); continue; }
            var ia = await Task.Run(() => ProvedorDoRegistro(pend.Registro));
            if (!Provedores.Ativos.Contains(ia)) continue;
            // A publicação concluída já foi verificada e pode ser importada mesmo que a área do kit tenha sido liberada.
            Resultado? publicada;
            try
            {
                publicada = await Task.Run(() =>
                {
                    if (!File.Exists(Path.Combine(pend.Registro, "execucao.json"))) return null;
                    var registro = System.Text.Json.Nodes.JsonNode.Parse(RegistroExecucao.LerTextoDaExecucao(pend.Registro));
                    return registro?["situacao"]?.GetValue<string>() == "concluida" && registro["pmvrel"] is null
                        ? RecuperacaoAnaliseLocal.EntregaPublicada(pend.Processo, Path.GetFileName(pend.Registro)) : null;
                });
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException)
            {
                MostrarMensagem(InfoBarSeverity.Warning, "Não foi possível conferir a entrega guardada", erro.Message);
                continue;
            }
            if (publicada?.Documento is not null)
            {
                var origem = await ProcessoDaPastaAsync(pend.Processo);
                if (await MostrarRecusaGuardadaAsync(ia, origem, pend.Registro)) continue;
                var oferta = new ImportacaoPendente(ia, origem, pend.Registro, null, null);
                if (!importacoesPendentes.Any(i => MesmoRegistro(i.Registro, pend.Registro))) importacoesPendentes.Add(oferta);
                MostrarImportacaoPendente(oferta, FluxoNovoRelatorio.TextoKitTerminou(ia, origem), InfoBarSeverity.Success);
                continue;
            }
            var pecas = await LerPecasAsync();
            var nomeIa = Provedores.Nome(ia);
            var pastaDeTrabalho = OrquestradorAnalise.ProcessoDoTrabalho(pend.Processo, pend.Registro);
            var s = await Orquestrador.SituacaoClaudeAsync(pastaDeTrabalho, pecas, ia);
            if (s is null) continue;     // sem resposta do kit agora: fica para a próxima abertura
            var processo = await ProcessoDaPastaAsync(pend.Processo);
            switch (s.Codigo)
            {
                case "CONCLUIDO" when pastaDeTrabalho == pend.Processo:
                    if (await MostrarRecusaGuardadaAsync(ia, processo, pend.Registro)) break;
                    var entrega = new ImportacaoPendente(ia, processo, pend.Registro, null, null);
                    if (!importacoesPendentes.Any(i => MesmoRegistro(i.Registro, pend.Registro)))
                        importacoesPendentes.Add(entrega);
                    MostrarImportacaoPendente(entrega, FluxoNovoRelatorio.TextoKitTerminou(ia, processo), InfoBarSeverity.Success);
                    NotificacoesWindows.Mostrar("Relatório aguardando importação",
                        "A análise terminou com o Editor fechado. Use Importar relatório para revisar o resultado.");
                    break;
                case "CONCLUIDO": // Publica a entrega final que ficou em Documentos com o Editor fechado.
                case "EM_ANDAMENTO" or "AGUARDANDO_COTA" or "INICIANDO":
                    if (await Task.Run(() => LerInicio(pend.Registro)) is not { } inicio)
                    {
                        // Sem a hora de início não há como saber se o delivery.json do kit é desta análise: não acompanha nem importa às cegas.
                        EsquecerPendenteClaude(pend.Registro);
                        MostrarMensagem(InfoBarSeverity.Warning, $"A análise com o {nomeIa} não foi retomada", FluxoNovoRelatorio.TextoSemInicio(processo, pend.Registro, ia));
                        break;
                    }
                    try
                    {
                        var e = await sessaoNR.RetomarClaudeAsync(processo, pend.Registro, inicio, ia);
                        e.Conclusao = ConcluirAsync(e);
        _ = CarregarHistoricoAsync();
                    }
                    catch (Exception x) when (x is AnaliseRecusadaException or IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        MostrarMensagem(InfoBarSeverity.Warning, $"A análise com o {nomeIa} não foi retomada", x.Message);
                    }
                    break;
                default:
                    var motivo = (s.Rotulo ?? s.Codigo) + (string.IsNullOrWhiteSpace(s.Erro) ? "" : ": " + s.Erro);
                    // Fluxo linear do kit: com objeção pendente, nada de retomar sozinho (nem com outra IA): só o aviso.
                    if (await Task.Run(() => ObjecaoPendenteDo(pastaDeTrabalho, ia)) is { } objecaoKit)
                    {
                        MostrarMensagem(InfoBarSeverity.Warning, $"A análise com o {nomeIa} de {FluxoNovoRelatorio.Identificacao(processo)} parou",
                            FluxoNovoRelatorio.TextoRetomadaComObjecao(objecaoKit));
                        break;
                    }
                    // 2026-11-17: a análise parou com o editor fechado; pergunta se retoma de onde parou, e com qual IA.
                    var (escolha, descartada) = await PerguntarRetomadaAsync(processo, pend.Registro, ia, motivo);
                    if (descartada) break;
                    if (escolha is { } outraIa) await ContinuarComOutraIaAsync(outraIa, processo, pend.Registro);
                    else MostrarMensagem(InfoBarSeverity.Warning, $"A análise com o {nomeIa} de {FluxoNovoRelatorio.Identificacao(processo)} parou", motivo);
                    break;
            }
        }
        AtualizarTituloAnalise();
        AtualizarBotoesNR();
    }

    /// <summary>A objeção pendente que bloqueia a entrega na pasta de controle da rota (<see cref="DetalhesDasEtapas"/>); null sem objeção.</summary>
    static ObjecaoPendente? ObjecaoPendenteDo(string pastaProcesso, Provedor p) =>
        DetalhesDasEtapas.Ler(Path.Combine(pastaProcesso, ResultadoAnalise.PastaControle(p))).Objecoes.FirstOrDefault();

    /// <summary>"Retomar a análise?" na reabertura: continuar com o Claude, com o ChatGPT ou agora não (null). A IA que fazia a análise vem primeiro.</summary>
    async Task<(Provedor? Ia, bool Descartada)> PerguntarRetomadaAsync(Processo processo, string registro, Provedor original, string? motivo = null)
    {
        if (!RoteiroLegado && (original != Provedor.ChatGpt || !OrquestradorAnalise.RegistroAnaliseChatGpt(registro)))
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Análise histórica", RetomadaLegada);
            return (null, false);
        }
        var provedores = RoteiroLegado ? Provedores.Ativos.OrderByDescending(p => p == original).ToArray() : [Provedor.ChatGpt];
        var itens = provedores.Select(p => $"Retomar com o {Provedores.Nome(p)}").Append("Agora não").ToArray();
        var texto = $"A análise de {FluxoNovoRelatorio.Identificacao(processo)} com o {Provedores.Nome(original)} parou"
            + (string.IsNullOrWhiteSpace(motivo) ? "." : $": {motivo}")
            + (RoteiroLegado ? " Qual IA deve continuar a partir da última etapa válida?" : " A retomada usa as rodadas já confirmadas no registro.");
        var indice = await Perguntas.Atual.EscolherNaListaAsync(FluxoNovoRelatorio.RetomarTitulo, texto, itens, 0, "Retomar", "Descartar análise");
        if (indice == itens.Length)
        {
            DescartarAnalisePendente(processo, registro);
            return (null, true);
        }
        return (indice is int i && i >= 0 && i < provedores.Length ? provedores[i] : null, false);
    }

    /// <summary>Uma análise nova no mesmo processo, pedindo à IA que siga do ponto em que a anterior parou (sem anexos novos).</summary>
    Task<ExecucaoEmCurso?> ContinuarComOutraIaAsync(Provedor p, Processo processo, string pastaRegistro, bool corrigirEntrega = false) => NaFilaAsync(async () =>
    {
        NR.AvisoResultado.IsOpen = false;
        await LerPecasAsync();
        return await AnalisarComPerfilAsync(p, processo, [], FluxoNovoRelatorio.PedidoContinuar, enviados: null, registroAnterior: pastaRegistro, corrigirEntrega: corrigirEntrega);
    });

    async Task AtualizarReferenciasDeRegistroAsync(List<ExecucaoClaudePendente> pendentes)
    {
        var alterou = false;
        foreach (var item in pendentes.ToArray())
        {
            var atual = await Task.Run(() => ControlesDoProcesso.RegistroAtual(item.Processo, item.Registro));
            var indice = pendentes.IndexOf(item);
            if (indice < 0 || MesmoRegistro(item.Registro, atual)) continue;
            var migrado = item with { Registro = atual };
            if (pendentes.Contains(migrado)) pendentes.RemoveAt(indice);
            else pendentes[indice] = migrado;
            alterou = true;
        }
        if (alterou) SalvarAjustes();
    }

    static bool MesmoRegistro(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
    }

    /// <summary>A análise sai de <c>execucoes_claude</c> (e da lista do "Importar relatório").</summary>
    void EsquecerPendenteClaude(string registro)
    {
        importacoesPendentes.RemoveAll(i => MesmoRegistro(i.Registro, registro));
        if (ajustes.ExecucoesClaude.RemoveAll(x => MesmoRegistro(x.Registro, registro)) > 0) SalvarAjustes();
    }

    /// <summary>"Importar relatório" à vista (na página e, fora dela, na mensagem do módulo, sem apagar o motivo de uma falha que já esteja lá).</summary>
    async Task ResolverEntregaPendenteAsync(AcaoEntrega? acao)
    {
        if (acao is null) return;
        var c = acao.Pendente;
        if (acao.Corrigir)
        {
            if (await Task.Run(() => RegistroExecucao.MotivoDaRecusaAtual(c.Processo.Pasta, c.Provedor, c.Registro)) is null)
            {
                await ImportarPendenteAsync(c with { Documento = null, Pdf = null });
                return;
            }
            if (await ContinuarComOutraIaAsync(c.Provedor, c.Processo, c.Registro, corrigirEntrega: true) is not null)
                importacoesPendentes.RemoveAll(i => MesmoRegistro(i.Registro, c.Registro));
        }
        else await ImportarPendenteAsync(c);
    }

    async Task<bool> MostrarRecusaGuardadaAsync(Provedor ia, Processo processo, string registro)
    {
        var motivo = await Task.Run(() => RegistroExecucao.MotivoDaRecusaAtual(processo.Pasta, ia, registro));
        if (motivo is null) return false;
        var pendente = new ImportacaoPendente(ia, processo, registro, null, null);
        if (!importacoesPendentes.Any(i => MesmoRegistro(i.Registro, registro))) importacoesPendentes.Add(pendente);
        MostrarImportacaoPendente(pendente, motivo, InfoBarSeverity.Error, corrigir: true);
        return true;
    }

    void MostrarImportacaoPendente(ImportacaoPendente imp, string mensagem, InfoBarSeverity severidade, bool corrigir = false)
    {
        if (fechada) return;
        var aviso = NR.AvisoResultado;
        aviso.Severity = severidade;
        aviso.Title = severidade == InfoBarSeverity.Success ? "Análise concluída" : "O relatório não foi importado";
        aviso.Message = mensagem;
        var acao = corrigir ? "Corrigir entrega e retomar" : "Importar relatório";
        var alvo = new AcaoEntrega(imp, corrigir);
        NR.BotaoImportarPendente.Tag = alvo;
        NR.BotaoImportarPendente.Content = acao;
        AutomationPropertiesNome(NR.BotaoImportarPendente, acao);
        NR.BotaoImportarPendente.Visibility = Visibility.Visible;
        NR.BotaoDescartar.Visibility = Visibility.Collapsed;
        NR.BotaoMostrarRegistroResultado.Visibility = Visibility.Collapsed; NR.LinkRemontarTeste.Visibility = Visibility.Collapsed;
        aviso.IsOpen = true;
        if (!PaginaNovoRelatorioVisivel)
        {
            var modulo = AvisoDoModulo;
            if (!(modulo.IsOpen && modulo.Severity == InfoBarSeverity.Error)) MostrarMensagem(severidade, aviso.Title, mensagem);
            var botao = new Button { Content = acao };
            IconeDeAcao.SetGlifo(botao, Glifos.ImportarAnalise);
            AutomationPropertiesNome(botao, acao);
            Dicas.Aplicar(botao, acao, mensagem);
            botao.Click += async (_, _) => await TentarAsync(() => ResolverEntregaPendenteAsync(alvo));
            AvisoDoModulo.ActionButton = botao;
        }
    }

    /// <summary>"Importar relatório": a do Claude que terminou com o editor fechado procura o <c>delivery.json</c> do kit (mais novo que o início da execução);
    /// a que já tem o documento (a importação de antes não aconteceu) só importa. A pendência só sai da lista (e dos ajustes) depois de importar; uma falha ou
    /// um cancelamento deixa o "Importar relatório" à vista para tentar de novo.</summary>
    internal async Task<bool> ImportarPendenteAsync(ImportacaoPendente imp)
    {
        NR.AvisoResultado.IsOpen = false;
        if (imp.Documento is not null) return await ImportarAsync(imp);
        if (await Task.Run(() => RegistroExecucao.MotivoInvalido(imp.Processo.Pasta, imp.Registro)) is { } invalido)
        {
            EsquecerPendenteClaude(imp.Registro);
            MostrarMensagem(InfoBarSeverity.Warning, "Uma análise guardada nos ajustes foi descartada", invalido);
            return false;
        }
        if (await Task.Run(() => LerInicio(imp.Registro)) is not { } inicio)
        {
            EsquecerPendenteClaude(imp.Registro);
            MostrarMensagem(InfoBarSeverity.Warning, "O relatório não foi importado", FluxoNovoRelatorio.TextoSemInicio(imp.Processo, imp.Registro, Provedores.PeloKit(imp.Provedor) ? imp.Provedor : Provedor.Claude));
            return false;
        }
        var doKit = imp.Provedor;
        var r = await Task.Run(() => RecuperacaoAnaliseLocal.EntregaPublicada(imp.Processo.Pasta, Path.GetFileName(imp.Registro))
            ?? ResultadoAnalise.Localizar(imp.Processo.Pasta, doKit, inicio.UtcDateTime, imp.Registro));
        if (r.Documento is null)
        {
            MostrarImportacaoPendente(imp, r.Motivo ?? "", InfoBarSeverity.Error);
            return false;
        }
        return await ImportarAsync(imp with { Documento = r.Documento, Pdf = r.Pdf, ParaRevisao = r.ParaRevisao });
    }

    /// <summary>Nada em curso que a abertura de um relatório interromperia: a página Novo relatório à frente, nenhum relatório aberto e nenhum despacho
    /// em edição (passo 2 ou 3, preparando ou gravando).</summary>
    bool PodeAbrirSemInterromper =>
        PaginaNovoRelatorioVisivel && sessao is null
        && novo is not ({ Passo: PassoNovoDespacho.Conteudo or PassoNovoDespacho.Conferencia } or { Preparando: true } or { Gravando: true });

    /// <summary>Importa o documento que o kit declarou e abre o <c>.rpmvni</c>; só então a pendência sai e o registro recebe o <c>.rpmvni</c>.
    /// <paramref name="abrirDireto"/> (lido na vez da fila) false ⇒ só cria o <c>.rpmvni</c> e mostra "Relatório pronto" com "Abrir relatório";
    /// null = gesto do usuário ("Importar relatório"): abre.</summary>
    async Task<bool> ImportarAsync(ImportacaoPendente imp, Func<bool>? abrirDireto = null, Action<string>? aoRecusar = null)
    {
        var abriu = false;
        ImportacaoRecusadaException? recusa = null;
        var criado = await NaFilaAsync(async () =>
        {
            abriu = abrirDireto?.Invoke() ?? true;
            if (string.Equals(Path.GetExtension(imp.Documento), ".rpmvni", StringComparison.OrdinalIgnoreCase))
            {
                var pacote = imp.Documento!;
                if (!File.Exists(pacote)) return null;
                RegistrarRecente(pacote);
                if (abriu) abriu = await AbrirPacoteAsync(pacote);
                return pacote;
            }
            return await ImportarAnaliseParaAsync(imp.Processo, imp.Documento!, abriu, e => recusa = e);
        });
        var s = abriu ? sessao : null;
        if (criado is null || (abriu && s is null))
        {
            if (!importacoesPendentes.Any(i => MesmoRegistro(i.Registro, imp.Registro))) importacoesPendentes.Add(imp);
            else importacoesPendentes[importacoesPendentes.FindIndex(i => MesmoRegistro(i.Registro, imp.Registro))] = imp;
            if (recusa?.Sha256 is { } sha)
            {
                aoRecusar?.Invoke(recusa.Message);
                try
                {
                    var erros = recusa.Erros;
                    await Task.Run(() =>
                    {
                        using var registro = RegistroExecucao.Abrir(imp.Registro, imp.Processo.Pasta);
                        registro.RecusarEntrega(imp.Provedor, imp.Documento!, sha, erros);

                    });
                    MostrarImportacaoPendente(imp, string.Join(Environment.NewLine, erros.Take(8)), InfoBarSeverity.Error, corrigir: true);
                    return false;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    MostrarImportacaoPendente(imp, recusa.Message + Environment.NewLine + "Falha ao registrar a recusa: " + Erro(ex), InfoBarSeverity.Error);
                    return false;
                }
            }
            MostrarImportacaoPendente(imp, FluxoNovoRelatorio.TextoNaoImportado(imp.Provedor, imp.Processo), InfoBarSeverity.Error);
            return false;
        }
        EsquecerPendenteClaude(imp.Registro);
        if (imp.Provedor == Provedor.ChatGpt) EsquecerPendenteChatGpt(imp.Registro);
        var pmvrel = s?.Caminho ?? criado;
        try
        {
            using var registro = RegistroExecucao.Abrir(imp.Registro, imp.Processo.Pasta);
            registro.Atualizar(o =>
            {
                o["pmvrel"] = pmvrel;
                o["document"] = imp.Documento;
                o["situacao"] = imp.ParaRevisao ? "para_revisao" : "concluida";
            });
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or InvalidOperationException) { System.Diagnostics.Debug.WriteLine("[registro] " + x.Message); }
        if (abriu) MostrarMensagem(InfoBarSeverity.Success, imp.ParaRevisao ? "Versão para revisão importada" : "Relatório importado",
            imp.ParaRevisao ? FluxoNovoRelatorio.TextoVersaoParaRevisao(pmvrel, imp.Pdf) : FluxoNovoRelatorio.TextoImportado(imp.Provedor, pmvrel, imp.Pdf));
        else MostrarRelatorioPronto(imp, pmvrel);
        return true;
    }

    /// <summary>"Relatório pronto" no módulo à vista, com "Abrir relatório" (na fila, como Abrir; quem decide abrir é o usuário).</summary>
    void MostrarRelatorioPronto(ImportacaoPendente imp, string pmvrel)
    {
        if (fechada) return;
        MostrarMensagem(InfoBarSeverity.Success, imp.ParaRevisao ? "Versão para revisão disponível" : FluxoNovoRelatorio.RelatorioProntoTitulo,
            imp.ParaRevisao ? FluxoNovoRelatorio.TextoVersaoParaRevisao(pmvrel, imp.Pdf) : FluxoNovoRelatorio.TextoRelatorioPronto(imp.Provedor, pmvrel, imp.Pdf));
        var botao = new Button { Content = FluxoNovoRelatorio.AbrirRelatorio };
        IconeDeAcao.SetGlifo(botao, Glifos.Abrir);
        AutomationPropertiesNome(botao, FluxoNovoRelatorio.AbrirRelatorio);
        Dicas.Aplicar(botao, FluxoNovoRelatorio.AbrirRelatorio, "Abre o relatório que a IA acabou de montar, para revisar.");
        botao.Click += async (_, _) => await AbrirRelatorioProntoAsync(pmvrel);
        AvisoDoModulo.ActionButton = botao;
        RelatorioPronto = pmvrel;
    }

    /// <summary>O último "Relatório pronto" oferecido (o roteiro aperta "Abrir relatório" por aqui).</summary>
    internal string? RelatorioPronto { get; private set; }

    internal Task AbrirRelatorioProntoAsync(string pmvrel) => NaFilaAsync(() => TentarAsync(async () =>
    {
        AvisoDoModulo.IsOpen = false;
        await AbrirPacoteAsync(pmvrel);
    }));

    async Task<Processo> ProcessoDaPastaAsync(string pasta)
    {
        var raiz = ajustes.PastaProcessosEfetiva;
        try
        {
            var achado = await Task.Run(() => Directory.Exists(raiz)
                ? Processos.Listar(raiz, CancellationToken.None).Itens.FirstOrDefault(p => string.Equals(Path.GetFullPath(p.Pasta), Path.GetFullPath(pasta), StringComparison.OrdinalIgnoreCase))
                : null);
            if (achado is not null) return achado;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new Processo(pasta, Path.GetFileName(pasta.TrimEnd('\\')), null, null, null);
    }

    static DateTimeOffset? LerInicio(string pastaRegistro) => RegistroExecucao.LerInicio(pastaRegistro);
}

internal sealed record SugestaoProcesso(Processo Processo)
{
    public string Rotulo => Processo.Protocolo is { } numero ? numero + " · " + (Processo.Requerente ?? Processo.NomePasta) : Processo.NomePasta;
}
