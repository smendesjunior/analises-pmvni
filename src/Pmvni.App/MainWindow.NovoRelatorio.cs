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
    internal sealed record ImportacaoPendente(Provedor Provedor, Processo Processo, string Registro, string? Documento, string? Pdf);

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
    readonly Dictionary<ExecucaoEmCurso, Task<FimAnalise>> conclusoes = new();
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
    internal bool BotaoAnaliseHabilitado(Provedor p) => (p == Provedor.ChatGpt ? NR.BotaoChatGpt : NR.BotaoClaude).IsEnabled;
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
        sessaoNR = new SessaoNovoRelatorio(DispatcherQueue.GetForCurrentThread(), () => Orquestrador, () => pecasAgentes, p => ajustes.Agentes[p].Modo, PastaDados.Raiz);
        sessaoNR.Atualizada += e => { if (ReferenceEquals(e, execucaoMostrada)) RenderizarExecucao(e, linhasNovas: true); };
        manipuladores[ComandosApp.RelatoriosNovo] = async () => await AbrirNovoRelatorioNaVezAsync();

        var n = NR;
        n.CaixaFiltro.TextChanged += (_, _) => PreencherProcessosNR();
        n.CaixaProcesso.SelectionChanged += (_, _) =>
        {
            if (!preenchendoNR && n.CaixaProcesso.SelectedItem is ComboBoxItem { Tag: Processo p }) _ = TentarAsync(() => EscolherProcessoNovoRelatorioAsync(p));
        };
        n.BotaoCriarProcesso.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(async () => await CriarProcessoNaVezAsync()));
        n.BotaoAnexar.Click += async (_, _) => await TentarAsync(AnexarAsync);
        n.AreaAnexos.DragOver += AoArrastarSobreAnexos;
        n.AreaAnexos.DragLeave += (_, _) => n.TextoArraste.Text = FluxoNovoRelatorio.ArrasteAqui;
        n.AreaAnexos.Drop += AoSoltarAnexos;
        n.CaixaPedido.TextChanged += (_, _) => { };
        n.BotaoChatGpt.Click += async (_, _) => await AnalisarAsync(Provedor.ChatGpt);
        n.BotaoClaude.Click += async (_, _) => await AnalisarAsync(Provedor.Claude);
        n.BotaoVoltarRelatorio.Click += (_, _) => FecharPaginaNovoRelatorio();
        n.BotaoExecucoes.Click += (_, _) => { if (sessaoNR.Execucoes.FirstOrDefault(e => !e.Abandonada) is { } e) MostrarExecucaoNR(e); };
        n.BotaoNovaAnalise.Click += (_, _) => MostrarFormularioNR();
        n.BotaoCancelarAnalise.Click += async (_, _) => { if (execucaoMostrada is { } e) await TentarAsync(async () => await CancelarAnaliseAsync(e)); };
        n.BotaoMostrarRegistro.Click += (_, _) => { if (execucaoMostrada is { } e) AberturaExterna.MostrarNoExplorador(e.Registro.Pasta); };
        n.BotaoMostrarRegistroResultado.Click += (_, _) => { if (ultimoResultadoNR is { } e) AberturaExterna.MostrarNoExplorador(e.Registro.Pasta); };
        n.BotaoContinuar.Click += async (_, _) => await TentarAsync(async () => await ContinuarAnaliseAsync());
        n.BotaoImportarPendente.Click += async (_, _) => await TentarAsync(async () => { if (importacoesPendentes.FirstOrDefault() is { } imp) await ImportarPendenteAsync(imp); });
        n.ExpansorUltimaMensagem.Expanding += async (_, _) => { if (execucaoMostrada is { } e) await MostrarUltimaMensagemAsync(e); };
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
        relogioAnalise.Interval = TimeSpan.FromSeconds(30);
        relogioAnalise.IsRepeating = true;
        relogioAnalise.Tick += (_, _) =>
        {
            AtualizarTituloAnalise();
            if (execucaoMostrada is { } e) RenderizarExecucao(e, linhasNovas: false);
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
        AtualizarBotoesNR();
        return true;
    }

    void FecharPaginaNovoRelatorio()
    {
        if (!novoRelatorioAberto) return;
        novoRelatorioAberto = false;
        AtualizarPaginasDeRelatorios();
        AtualizarComandos();
    }

    /// <summary>Relatórios mostra as folhas ou, com a página aberta, a página Novo relatório no lugar delas.</summary>
    void AtualizarPaginasDeRelatorios()
    {
        if (fechada) return;
        var relatorios = moduloAtual == Modulo.Relatorios;
        R.Visibility = relatorios && !novoRelatorioAberto ? Visibility.Visible : Visibility.Collapsed;
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
        var linhas = ListasPastas.FiltrarProcessos(processosNR.Select(p => new LinhaProcesso(p, DateTime.MinValue)), n.CaixaFiltro.Text, null, OrdemProcessos.Numero)
            .Select(l => l.Processo).ToList();
        // O processo escolhido fica sempre à vista, mesmo fora do filtro: o Analisar parte para ele.
        if (escolher is not null && !linhas.Any(p => p.Pasta == escolher.Pasta)) linhas.Insert(0, escolher);
        preenchendoNR = true;
        try
        {
            n.CaixaProcesso.Items.Clear();
            foreach (var processo in linhas)
                n.CaixaProcesso.Items.Add(new ComboBoxItem
                {
                    Content = ListasPastas.NumeroDoProcesso(processo) is { } num ? num + " · " + (processo.Requerente ?? processo.NomePasta) : processo.NomePasta,
                    Tag = processo,
                });
            n.CaixaProcesso.SelectedItem = n.CaixaProcesso.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is Processo p && escolher is not null && p.Pasta == escolher.Pasta);
        }
        finally { preenchendoNR = false; }
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

    /// <summary>"Anexar arquivos…": seleção múltipla, qualquer tipo.</summary>
    internal async Task AnexarAsync()
    {
        var caminhos = await Perguntas.Atual.AbrirArquivosAsync();
        if (caminhos is not null) await AdicionarAnexosAsync(caminhos, []);
    }

    /// <summary>O corpo do <c>Drop</c> (o roteiro chama direto): pastas e atalhos são recusados com a mensagem exata; o resto vira linha.</summary>
    internal Task SoltarArquivosAsync(IReadOnlyList<string> caminhos) => AdicionarAnexosAsync(caminhos, []);

    void AoArrastarSobreAnexos(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = FluxoNovoRelatorio.SolteParaAnexar;
            NR.TextoArraste.Text = FluxoNovoRelatorio.SolteParaAnexar;
        }
        else e.AcceptedOperation = DataPackageOperation.None;
    }

    async void AoSoltarAnexos(object sender, DragEventArgs e)
    {
        NR.TextoArraste.Text = FluxoNovoRelatorio.ArrasteAqui;
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
            await AdicionarAnexosAsync(caminhos, semCaminho);
        }
        catch (Exception x) { MostrarMensagem(InfoBarSeverity.Error, "Não foi possível anexar", Erro(x)); }
        finally { adiamento.Complete(); }
    }

    /// <summary>Cada caminho novo (o mesmo caminho duas vezes entra uma vez) é conferido só lendo: recusado (pasta, atalho, ilegível) vai para a lista de recusas;
    /// o resto vira uma linha com o destino padrão do processo. Nada é gravado.</summary>
    async Task AdicionarAnexosAsync(IReadOnlyList<string> caminhos, IReadOnlyList<string> recusasDeFora)
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
            NR.TextoArraste.Text = FluxoNovoRelatorio.ArrasteAqui;
            caminhos = lista;
            recusasDeFora = [.. recusasDeFora, .. avisosCompactados];
        }
        var novos = new List<string>();
        foreach (var c in caminhos)
        {
            string completo;
            try { completo = Path.GetFullPath(c); }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (anexosNR.Any(a => string.Equals(a.Origem, completo, StringComparison.OrdinalIgnoreCase)) || novos.Contains(completo, StringComparer.OrdinalIgnoreCase)) continue;
            novos.Add(completo);
        }
        var pasta = processoNR?.Pasta;
        var base_ = pasta ?? ajustes.PastaProcessosEfetiva;   // sem processo, a conferência só lê o anexo
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
            if (anexosNR.Any(a => string.Equals(a.Origem, origem, StringComparison.OrdinalIgnoreCase))) continue;
            if (deCompactado.TryGetValue(origem, out var x))
            {
                var sugerido = Compactados.SugerirDestino(x.Interno, padrao);
                // Documento sugerido fica escolhido (trocar de processo não o leva para o projeto); projeto segue o padrão do processo.
                anexosNR.Add(new AnexoNovo(origem, tamanho, sugerido, x.Compactado, x.Interno) { Escolhido = sugerido == DestinoAnexo.Documento });
            }
            else anexosNR.Add(new AnexoNovo(origem, tamanho, padrao));
        }
        if (abertos.Count > 0)
            MostrarMensagem(InfoBarSeverity.Informational, "Compactado aberto",
                string.Join("; ", abertos) + ". Cada arquivo entrou como anexo, com o destino pela regra do kit (documentos em DOCUMENTOS, o resto no projeto); confira e troque na linha se precisar.");
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
        var lista = NR.ListaAnexos;
        lista.Children.Clear();
        var opcoes = FluxoNovoRelatorio.OpcoesDestino(revisaoAtualNR);
        // Sem processo, um aviso só no topo (antes, "Escolha o processo." se repetia em cada linha) e a coluna do destino final some.
        var semProcesso = processoNR is null && anexosNR.Count > 0;
        if (semProcesso)
            lista.Children.Add(new TextBlock { Text = "Escolha o processo no passo 1 para ver onde cada arquivo será gravado.", TextWrapping = TextWrapping.Wrap,
                Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"], Margin = new Thickness(2, 0, 0, 2) });
        string? grupoAtual = null;
        for (var i = 0; i < anexosNR.Count; i++)
        {
            var a = anexosNR[i];
            // Os arquivos de um mesmo compactado ficam juntos, com um título ("Envio do requerente.zip · 4 arquivos").
            if (a.Compactado is { } zip && zip != grupoAtual)
            {
                var qtd = anexosNR.Skip(i).TakeWhile(x => x.Compactado == zip).Count(); // só a sequência deste compactado
                lista.Children.Add(new TextBlock { Text = $"{zip} · {qtd} {(qtd == 1 ? "arquivo" : "arquivos")}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(2, 6, 0, 0) });
            }
            grupoAtual = a.Compactado;
            var grade = new Grid { ColumnSpacing = 10, Padding = new Thickness(10, 6, 10, 6), CornerRadius = new CornerRadius(6),
                Style = (Style)Application.Current.Resources["PmvniGradeCartaoStyle"] };
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 240, MaxWidth = 340 });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = semProcesso ? new GridLength(0) : new GridLength(3, GridUnitType.Star) });
            grade.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var nome = new StackPanel();
            nome.Children.Add(new TextBlock { Text = a.Nome, TextTrimming = TextTrimming.CharacterEllipsis });
            var detalhe = ListasPastas.TextoDoTamanho(a.Tamanho);
            if (a.Compactado is not null && a.Interno is { } dentro && dentro.Contains('/'))
                detalhe = "pasta " + dentro[..dentro.LastIndexOf('/')].Replace("/", " › ") + " · " + detalhe;
            nome.Children.Add(new TextBlock { Text = detalhe, TextTrimming = TextTrimming.CharacterEllipsis, Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] });
            grade.Children.Add(nome);
            var caixa = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Center };
            AutomationPropertiesNome(caixa, "Destino de " + a.Nome);
            foreach (var o in opcoes) caixa.Items.Add(new ComboBoxItem { Content = o.Rotulo, Tag = o.Destino });
            caixa.SelectedItem = caixa.Items.OfType<ComboBoxItem>().FirstOrDefault(x => x.Tag is DestinoAnexo d && d == a.Destino);
            if (caixa.SelectedItem is ComboBoxItem { Content: string rotuloDestino }) ToolTipService.SetToolTip(caixa, rotuloDestino);
            caixa.SelectionChanged += (_, _) => { if (caixa.SelectedItem is ComboBoxItem { Tag: DestinoAnexo d } && d != a.Destino) DefinirDestino(a, d); };
            Grid.SetColumn(caixa, 1);
            grade.Children.Add(caixa);
            var previa = new TextBlock { Text = semProcesso ? "" : a.Previa, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, IsTextSelectionEnabled = true };
            Grid.SetColumn(previa, 2);
            grade.Children.Add(previa);
            var remover = new Button { Content = new FontIcon { Glyph = Glifos.Excluir, FontSize = 14 }, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 7, 10, 7) };
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
        FluxoNovoRelatorio.MotivoDesabilitado(p, processoNR, processoNR is not null && sessaoNR.EmAndamento(processoNR.Pasta), pecasAgentes, ajustes.Agentes[p].Modo,
            ajustes.Agentes[p].Modo == ModoAcesso.Chave && CofreChaves.Existe(PastaDados.Raiz, p));

    void AtualizarBotoesNR()
    {
        if (fechada) return;
        foreach (var p in Enum.GetValues<Provedor>())
        {
            var botao = p == Provedor.ChatGpt ? NR.BotaoChatGpt : NR.BotaoClaude;
            var texto = p == Provedor.ChatGpt ? NR.TextoMotivoChatGpt : NR.TextoMotivoClaude;
            var motivo = MotivoBotao(p);
            botao.IsEnabled = motivo is null && !partindoNR;
            var servico = p == Provedor.ChatGpt ? "ChatGPT" : "Claude";
            var dica = DicasComandos.Livre("Analisar com o " + servico, $"Manda o processo, os anexos e o pedido para o {servico} analisar e montar o relatório.", motivo: motivo);
            if (Dicas.De(botao) != dica) Dicas.Aplicar(botao, dica);
            texto.Text = motivo ?? "";
            texto.Visibility = motivo is null ? Visibility.Collapsed : Visibility.Visible;
            // 2026-11-16: a versão instalada do plugin que vai fazer a análise, sempre à vista (também gravada no execucao.json).
            var versao = Pmvni.Agentes.OrquestradorAnalise.VersaoDoPlugin(p, pecasAgentes);
            var rotulo = (p == Provedor.ChatGpt ? "Pelo Codex · " : "Pelo Claude Code · ") + "kit "
                + Pmvni.Agentes.OrquestradorAnalise.PluginUsado(p).Replace("analise", "análise", StringComparison.Ordinal)
                + " " + (versao is not null ? "v" + versao : ReferenceEquals(pecasAgentes, PecasNaoLidas) ? "(verificando versão…)" : "(versão não encontrada)");
            var vaga = p == Provedor.ChatGpt ? NR.TextoPluginChatGpt : NR.TextoPluginClaude;
            if (vaga.Text != rotulo) vaga.Text = rotulo;
        }
        NR.AnelPartida.IsActive = partindoNR;
        AtualizarComandosNovoRelatorio();
    }

    /// <summary>O clique em "Analisar com o …": na fila de comandos só até a partida. null = não partiu (o motivo à vista).</summary>
    internal Task<ExecucaoEmCurso?> AnalisarAsync(Provedor p) => NaFilaAsync(() => AnalisarNaVezAsync(p));

    async Task<ExecucaoEmCurso?> AnalisarNaVezAsync(Provedor p)
    {
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
        return await PartirAsync(() => sessaoNR.AnalisarAsync(p, processo, anexos, PedidoTexto), instantaneo);
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
            return null;
        }
        catch (Exception x)
        {
            MostrarMensagem(InfoBarSeverity.Error, "A análise não foi iniciada", Erro(x) + ". Nada foi iniciado.");
            return null;
        }
        finally
        {
            partindoNR = false;
            AtualizarBotoesNR();
        }
        if (e is null) return null;
        if (e.Provedor == Provedor.Claude && !ajustes.ExecucoesClaude.Any(x => string.Equals(x.Registro, e.Registro.Pasta, StringComparison.OrdinalIgnoreCase)))
        {
            ajustes.ExecucoesClaude.Add(new ExecucaoClaudePendente(e.Processo.Pasta, e.Registro.Pasta));
            SalvarAjustes();
        }
        if (e.Provedor == Provedor.ChatGpt && !ajustes.ExecucoesChatGpt.Any(x => MesmoRegistro(x.Registro, e.Registro.Pasta)))
        {
            // Guardada já na partida: se o editor cair, a reabertura oferece continuar de onde parou.
            ajustes.ExecucoesChatGpt.Add(new ExecucaoClaudePendente(e.Processo.Pasta, e.Registro.Pasta));
            SalvarAjustes();
        }
        conclusoes[e] = ConcluirAsync(e);
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

    /// <summary>"Continuar a análise" (Claude parado ou interrompido): roda <c>iniciar</c> de novo, que o kit retoma de onde parou.</summary>
    internal Task<ExecucaoEmCurso?> ContinuarAnaliseAsync() => NaFilaAsync(async () =>
    {
        if (continuarChatGpt is { } cg)
        {
            // ChatGPT (2026-11-17): continua a mesma sessão do Codex (codex exec resume), no mesmo registro.
            continuarChatGpt = null;
            NR.AvisoResultado.IsOpen = false;
            await LerPecasAsync();
            return await PartirAsync(() => sessaoNR.RetomarChatGptAsync(cg.Processo, cg.Registro, cg.Sessao), enviados: null);
        }
        if (ultimoResultadoNR is not { Provedor: Provedor.Claude } anterior) return null;
        NR.AvisoResultado.IsOpen = false;
        await LerPecasAsync();
        return await PartirAsync(() => sessaoNR.AnalisarAsync(Provedor.Claude, anterior.Processo, [], anterior.Texto), enviados: null);
    });

    // ---------- vista da execução ----------

    void MostrarExecucaoNR(ExecucaoEmCurso e)
    {
        if (fechada) return;
        execucaoMostrada = e;
        NR.LigarLinhas(e.LinhasObservaveis);
        NR.MostrarExecucao(true);
        NR.TextoUltimaMensagem.Text = "";
        NR.ExpansorUltimaMensagem.IsExpanded = false;
        RenderizarExecucao(e, linhasNovas: true);
        AtualizarComandosNovoRelatorio();
    }

    void MostrarFormularioNR()
    {
        execucaoMostrada = null;
        NR.LigarLinhas(null);
        NR.MostrarExecucao(false);
        AtualizarBotoesNR();
    }

    void RenderizarExecucao(ExecucaoEmCurso e, bool linhasNovas)
    {
        if (fechada) return;
        var n = NR;
        n.TextoCabecalho.Text = FluxoNovoRelatorio.TituloExecucao(e.Provedor, e.Processo, DateTime.UtcNow - e.InicioUtc) + (e.Retomada ? " (retomada)" : "");
        var claude = e.Provedor == Provedor.Claude;
        n.PainelClaude.Visibility = claude ? Visibility.Visible : Visibility.Collapsed;
        n.PainelUltimaMensagem.Visibility = claude ? Visibility.Collapsed : Visibility.Visible;
        var rodando = e.Fim is null || !e.Fim.IsCompleted;
        string? erro = null;
        IReadOnlyList<(string Nome, EstadoEtapa Estado)> etapas = [];
        if (claude)
        {
            var s = e.Situacao;
            erro = string.IsNullOrWhiteSpace(s?.Erro) ? null : s!.Erro;
            etapas = FluxoNovoRelatorio.Etapas(s?.Etapa, s?.Codigo ?? "");
            // A etapa em que o kit informou erro fica vermelha.
            if (erro is not null) etapas = etapas.Select(x => x.Estado == EstadoEtapa.Agora ? (x.Nome, EstadoEtapa.Erro) : x).ToList();
            n.TextoRotulo.Text = s is null ? "Iniciando a análise detalhada…" : $"Situação: {s.Codigo}" + (s.Rotulo is null ? "" : " · " + s.Rotulo) + (erro is null ? "" : Environment.NewLine + "Erro: " + erro);
            n.TextoEventos.Text = string.Join(Environment.NewLine, e.Eventos.TakeLast(8));
        }
        else
        {
            n.TextoRotulo.Text = $"Provedor: {Provedores.Nome(e.Provedor)} · Registro: {e.Registro.Pasta}";
            n.TextoEventos.Text = "";
        }
        if (!claude && e.Linhas.LastOrDefault() is { } ultima && ultima.StartsWith("Erro: ", StringComparison.Ordinal)) erro = ultima[6..];
        n.MostrarResumo(rodando, erro, etapas, e.Linhas);
        if (linhasNovas && n.LinhasNoFim()) n.RolarLinhasAoFim();
    }

    async Task MostrarUltimaMensagemAsync(ExecucaoEmCurso e)
    {
        string texto;
        try
        {
            texto = await Task.Run(() =>
            {
                if (!File.Exists(e.UltimaMensagem)) return "O ChatGPT ainda não deixou a última mensagem.";
                using var fs = new FileStream(e.UltimaMensagem, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var leitor = new StreamReader(fs, Encoding.UTF8);
                var buffer = new char[200_000];
                var lidos = leitor.ReadBlock(buffer, 0, buffer.Length);
                return new string(buffer, 0, lidos);
            });
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { texto = "A última mensagem não pôde ser lida: " + Erro(x); }
        if (ReferenceEquals(execucaoMostrada, e)) NR.TextoUltimaMensagem.Text = texto;
    }

    /// <summary>"Cancelar análise", com a confirmação do provedor. true = cancelou (o Codex foi encerrado, ou o processo do kit).</summary>
    internal async Task<bool> CancelarAnaliseAsync(ExecucaoEmCurso e)
    {
        var chatgpt = e.Provedor == Provedor.ChatGpt;
        // Ajustes › Inteligência artificial › "Confirmar antes de cancelar uma análise" (padrão: ligado, como sempre).
        if (ajustes.ConfirmarCancelar && !await Perguntas.Atual.ConfirmarAsync(chatgpt ? FluxoNovoRelatorio.CancelarChatGptTitulo : FluxoNovoRelatorio.CancelarClaudeTitulo,
                chatgpt ? FluxoNovoRelatorio.CancelarChatGptTexto : FluxoNovoRelatorio.CancelarClaudeTexto, FluxoNovoRelatorio.CancelarAnalise, FluxoNovoRelatorio.Voltar))
            return false;
        var ok = await sessaoNR.CancelarAsync(e);
        if (!ok) MostrarMensagem(InfoBarSeverity.Warning, "A análise não foi cancelada", "O processo da análise do kit não foi encontrado (ele pode já ter terminado).");
        return ok;
    }

    /// <summary>O fim da execução, já com a importação (o <c>.rpmvni</c> aberto) ou com o motivo à vista.</summary>
    internal Task<FimAnalise> AguardarAnaliseAsync(ExecucaoEmCurso e) => conclusoes.TryGetValue(e, out var t) ? t : e.Fim;

    async Task<FimAnalise> ConcluirAsync(ExecucaoEmCurso e)
    {
        // Começa dentro do comando Analisar (que está na vez da fila): o fim chega muito depois, fora dele, e a importação espera a vez como qualquer comando.
        naVez.Value = false;
        FimAnalise fim;
        try { fim = await e.Fim; }
        catch (Exception x) { fim = new FimAnalise("falhou", null, null, "A análise não pôde ser executada: " + Erro(x) + ". Veja o registro: " + e.Registro.Pasta + ".", null); }
        if (fechada) return fim;
        sessaoNR.Remover(e);
        if (e.Abandonada) { AtualizarTituloAnalise(); AtualizarBotoesNR(); return fim; }
        var comRelatorio = fim.Situacao == "concluida" && fim.Documento is not null;
        // A análise do Claude sai dos ajustes quando acaba sem relatório; com relatório, só depois de importado (até lá o editor reaberto ainda o oferece).
        if (e.Provedor == Provedor.Claude && !comRelatorio) EsquecerPendenteClaude(e.Registro.Pasta);
        // ChatGPT: fica nos ajustes só o que dá para continuar (cota esgotada ou falha com a sessão do Codex guardada).
        string? sessaoCodex = null;
        if (e.Provedor == Provedor.ChatGpt)
        {
            sessaoCodex = fim.Situacao is "cota_esgotada" or "falhou" ? await Task.Run(() => LerSessaoCodex(e.Registro.Pasta)) : null;
            if (sessaoCodex is null) EsquecerPendenteChatGpt(e.Registro.Pasta);
        }
        var naVista = ReferenceEquals(execucaoMostrada, e) && PaginaNovoRelatorioVisivel;
        if (ReferenceEquals(execucaoMostrada, e)) MostrarFormularioNR();
        // Ruling FD-I3: o fim em segundo plano nunca troca de módulo nem fecha o que está aberto; abre direto só com o usuário na vista desta execução,
        // sem relatório aberto e sem despacho em edição (conferido na vez da fila). Senão, "Relatório pronto" com "Abrir relatório".
        RegistrarFimNaPilula(e, fim);
        if (comRelatorio)
            await ImportarAsync(new ImportacaoPendente(e.Provedor, e.Processo, e.Registro.Pasta, fim.Documento, fim.Pdf), () => naVista && PodeAbrirSemInterromper);
        else MostrarResultadoNR(e, fim, sessaoCodex);
        AtualizarTituloAnalise();
        AtualizarBotoesNR();
        return fim;
    }

    /// <summary>A análise do ChatGPT a continuar com "Continuar a análise" (fim com cota esgotada/falha, ou achada na reabertura).</summary>
    sealed record ContinuacaoChatGpt(Processo Processo, string Registro, string Sessao);

    ContinuacaoChatGpt? continuarChatGpt;

    /// <summary>A sessão do Codex guardada no <c>execucao.json</c> do registro; null sem ela.</summary>
    static string? LerSessaoCodex(string pastaRegistro)
    {
        try
        {
            var arq = Path.Combine(pastaRegistro, "execucao.json");
            if (!File.Exists(arq)) return null;
            return System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(arq, Encoding.UTF8))?["sessao_codex"]?.GetValue<string>() is { Length: > 0 and <= 128 } s ? s : null;
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
        foreach (var pend in ajustes.ExecucoesChatGpt.ToList())
        {
            if (sessaoNR.EmAndamento(pend.Processo)) continue;
            var (valido, sessao) = await Task.Run(() =>
                (RegistroExecucao.MotivoInvalido(pend.Processo, pend.Registro) is null && Directory.Exists(pend.Processo) && Directory.Exists(pend.Registro),
                 LerSessaoCodex(pend.Registro)));
            if (!valido || sessao is null) { EsquecerPendenteChatGpt(pend.Registro); continue; }
            var processo = await ProcessoDaPastaAsync(pend.Processo);
            continuarChatGpt = new ContinuacaoChatGpt(processo, pend.Registro, sessao);
            var aviso = NR.AvisoResultado;
            aviso.Severity = InfoBarSeverity.Warning;
            aviso.Title = "Uma análise com o ChatGPT ficou pela metade";
            aviso.Message = $"A análise de {FluxoNovoRelatorio.Identificacao(processo)} parou antes do fim (o editor fechou ou a cota acabou). Clique em \"Continuar a análise\" para seguir de onde parou.";
            NR.BotaoContinuar.Visibility = Visibility.Visible;
            NR.BotaoImportarPendente.Visibility = Visibility.Collapsed;
            NR.BotaoMostrarRegistroResultado.Visibility = Visibility.Collapsed;
            aviso.IsOpen = true;
            // 2026-11-17 (pedido do usuário): pergunta na hora se retoma, e com qual IA. "Agora não" deixa o aviso com "Continuar a análise".
            var escolha = await PerguntarRetomadaAsync(processo, Provedor.ChatGpt);
            if (escolha == Provedor.ChatGpt) await ContinuarAnaliseAsync();
            else if (escolha == Provedor.Claude)
            {
                continuarChatGpt = null;
                EsquecerPendenteChatGpt(pend.Registro);
                await ContinuarComOutraIaAsync(Provedor.Claude, processo);
            }
            else if (!PaginaNovoRelatorioVisivel) MostrarMensagem(aviso.Severity, aviso.Title, aviso.Message + " (em Novo relatório)");
            break;   // uma de cada vez: a próxima aparece na próxima abertura
        }
    }

    /// <summary>A análise terminou sem relatório: a mensagem exata, "Mostrar registro" e, no Claude parado ou interrompido, "Continuar a análise".</summary>
    void MostrarResultadoNR(ExecucaoEmCurso e, FimAnalise fim, string? sessaoCodex = null)
    {
        ultimoResultadoNR = e;
        continuarChatGpt = sessaoCodex is null ? null : new ContinuacaoChatGpt(e.Processo, e.Registro.Pasta, sessaoCodex);
        var aviso = NR.AvisoResultado;
        var nome = Provedores.Nome(e.Provedor);
        aviso.Severity = fim.Situacao == "cancelada" ? InfoBarSeverity.Informational : InfoBarSeverity.Error;
        aviso.Title = fim.Situacao switch
        {
            "cancelada" => $"A análise com o {nome} foi cancelada",
            "interrompida" => $"A análise com o {nome} parou",
            "cota_esgotada" => $"A cota do {nome} acabou",
            _ => $"A análise com o {nome} não gerou relatório",
        };
        if (fim.Situacao == "cota_esgotada") aviso.Severity = InfoBarSeverity.Warning;
        aviso.Message = (fim.Mensagem ?? fim.Situacao)
            + (fim.Situacao == "falhou" && sessaoCodex is not null ? " Você pode clicar em \"Continuar a análise\" para seguir de onde parou." : "");
        NR.BotaoContinuar.Visibility = (e.Provedor == Provedor.Claude && fim.Situacao is "falhou" or "interrompida") || sessaoCodex is not null
            ? Visibility.Visible : Visibility.Collapsed;
        NR.BotaoImportarPendente.Visibility = Visibility.Collapsed;
        NR.BotaoMostrarRegistroResultado.Visibility = Visibility.Visible;
        aviso.IsOpen = true;
        if (!PaginaNovoRelatorioVisivel) MostrarMensagem(aviso.Severity, aviso.Title, aviso.Message);
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
            return FluxoNovoRelatorio.TituloExecucao(e.Provedor, e.Processo, DateTime.UtcNow - e.InicioUtc)
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
        var titulo = FluxoNovoRelatorio.TituloExecucao(e.Provedor, e.Processo, DateTime.UtcNow - e.InicioUtc);
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
        DetalheBotaoAnalise.Text = detalhe;
        DetalheBotaoAnalise.Visibility = detalhe.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var emAndamento = estado == EstadoPilulaAnalise.EmAndamento;
        AnelAnalise.IsActive = emAndamento;
        AnelAnalise.Visibility = emAndamento ? Visibility.Visible : Visibility.Collapsed;
        static Visibility Se(bool v) => v ? Visibility.Visible : Visibility.Collapsed;
        IconeAnaliseConcluida.Visibility = Se(estado == EstadoPilulaAnalise.Concluida);
        IconeAnalisePausada.Visibility = Se(estado == EstadoPilulaAnalise.Pausada);
        IconeAnaliseErro.Visibility = Se(estado == EstadoPilulaAnalise.Erro);
        // Em andamento, o contorno na cor de destaque; nos outros estados, a borda neutra do tema.
        ContornoAnalise.Visibility = Se(emAndamento);
        BordaAnalise.Visibility = Se(!emAndamento);
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

    /// <summary>Antes do fechamento de sempre (desenho §12.7): ChatGPT ⇒ "Cancelar a análise e fechar?" (Continuar ⇒ não fecha; sim ⇒ cancela e espera até 5 s);
    /// Claude ⇒ só informa (a análise segue na janela do kit e fica nos ajustes para a retomada). true = pode fechar.</summary>
    /// <param name="tratadas">As execuções já perguntadas/informadas neste fechamento (a segunda checagem, na fila, só olha as novas).</param>
    internal async Task<bool> PodeFecharComAnalisesAsync(HashSet<ExecucaoEmCurso>? tratadas = null)
    {
        var ativas = sessaoNR.Execucoes.Where(e => !e.Abandonada && tratadas?.Contains(e) != true).ToList();
        foreach (var e in ativas.Where(e => e.Provedor == Provedor.ChatGpt))
        {
            // Ajustes › Inteligência artificial › "Avisar ao fechar…" desligado: fecha e cancela sem perguntar.
            if (ajustes.ConfirmarFechar && !await Perguntas.Atual.ConfirmarAsync(FluxoNovoRelatorio.FecharTitulo, FluxoNovoRelatorio.TextoFecharChatGpt(e.Processo), FluxoNovoRelatorio.FecharSim, FluxoNovoRelatorio.FecharNao))
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
        var claudes = ativas.Where(e => e.Provedor == Provedor.Claude).ToList();
        if (claudes.Count > 0)
        {
            foreach (var e in claudes.Where(e => !ajustes.ExecucoesClaude.Any(x => string.Equals(x.Registro, e.Registro.Pasta, StringComparison.OrdinalIgnoreCase))))
                ajustes.ExecucoesClaude.Add(new ExecucaoClaudePendente(e.Processo.Pasta, e.Registro.Pasta));
            SalvarAjustes();
            await Perguntas.Atual.InformarAsync("A análise com o Claude continua", FluxoNovoRelatorio.TextoFecharClaude);
        }
        tratadas?.UnionWith(ativas);
        return true;
    }

    /// <summary>O que o fechamento faz com o Claude que segue (o roteiro chama, sem fechar a janela): para de acompanhar; a análise do kit continua.</summary>
    internal async Task PararDeAcompanharClaudeAsync()
    {
        foreach (var e in sessaoNR.Execucoes.Where(e => e.Provedor == Provedor.Claude && !e.Abandonada).ToList())
        {
            sessaoNR.PararDeAcompanhar(e);
            try { await AguardarAnaliseAsync(e); } catch (Exception x) { System.Diagnostics.Debug.WriteLine("[claude] " + x.Message); }
        }
        if (execucaoMostrada is { Abandonada: true }) MostrarFormularioNR();
        AtualizarTituloAnalise();
    }

    /// <summary>Na abertura (desenho §12.7): a situação de cada análise do Claude que seguia quando o editor fechou. Ainda em andamento ⇒ volta a acompanhar;
    /// <c>CONCLUIDO</c> ⇒ InfoBar com "Importar relatório"; parou de outro jeito ⇒ o rótulo e o erro do kit; pasta sumida ⇒ sai da lista. Os caminhos vêm do
    /// <c>ajustes.json</c> (editável): um registro fora de <c>&lt;processo&gt;\.pmvni-ai\editor</c> (ou atrás de um atalho) é descartado com o motivo à vista.</summary>
    internal async Task RetomarExecucoesClaudeAsync()
    {
        foreach (var pend in ajustes.ExecucoesClaude.ToList())
        {
            if (sessaoNR.EmAndamento(pend.Processo)) continue;
            if (await Task.Run(() => RegistroExecucao.MotivoInvalido(pend.Processo, pend.Registro)) is { } invalido)
            {
                EsquecerPendenteClaude(pend.Registro);
                MostrarMensagem(InfoBarSeverity.Warning, "Uma análise guardada nos ajustes foi descartada", invalido);
                continue;
            }
            var existe = await Task.Run(() => Directory.Exists(pend.Processo) && Directory.Exists(pend.Registro));
            if (!existe) { EsquecerPendenteClaude(pend.Registro); continue; }
            var pecas = await LerPecasAsync();
            var s = await Orquestrador.SituacaoClaudeAsync(pend.Processo, pecas);
            if (s is null) continue;     // sem resposta do kit agora: fica para a próxima abertura
            var processo = await ProcessoDaPastaAsync(pend.Processo);
            switch (s.Codigo)
            {
                case "CONCLUIDO":
                    if (!importacoesPendentes.Any(i => MesmoRegistro(i.Registro, pend.Registro)))
                        importacoesPendentes.Add(new ImportacaoPendente(Provedor.Claude, processo, pend.Registro, null, null));
                    MostrarImportacaoPendente(processo, FluxoNovoRelatorio.TextoClaudeTerminou(processo), InfoBarSeverity.Success);
                    break;
                case "EM_ANDAMENTO" or "AGUARDANDO_COTA" or "INICIANDO":
                    if (await Task.Run(() => LerInicio(pend.Registro)) is not { } inicio)
                    {
                        // Sem a hora de início não há como saber se o delivery.json do kit é desta análise: não acompanha nem importa às cegas.
                        EsquecerPendenteClaude(pend.Registro);
                        MostrarMensagem(InfoBarSeverity.Warning, "A análise com o Claude não foi retomada", FluxoNovoRelatorio.TextoSemInicio(processo, pend.Registro));
                        break;
                    }
                    try
                    {
                        var e = await sessaoNR.RetomarClaudeAsync(processo, pend.Registro, inicio);
                        conclusoes[e] = ConcluirAsync(e);
                    }
                    catch (Exception x) when (x is AnaliseRecusadaException or IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        MostrarMensagem(InfoBarSeverity.Warning, "A análise com o Claude não foi retomada", x.Message);
                    }
                    break;
                default:
                    EsquecerPendenteClaude(pend.Registro);
                    var motivo = (s.Rotulo ?? s.Codigo) + (string.IsNullOrWhiteSpace(s.Erro) ? "" : ": " + s.Erro);
                    // 2026-11-17: a análise parou com o editor fechado; pergunta se retoma de onde parou, e com qual IA.
                    if (await PerguntarRetomadaAsync(processo, Provedor.Claude, motivo) is { } ia) await ContinuarComOutraIaAsync(ia, processo);
                    else MostrarMensagem(InfoBarSeverity.Warning, $"A análise com o Claude de {FluxoNovoRelatorio.Identificacao(processo)} parou", motivo);
                    break;
            }
        }
        AtualizarTituloAnalise();
        AtualizarBotoesNR();
    }

    /// <summary>"Retomar a análise?" na reabertura: continuar com o Claude, com o ChatGPT ou agora não (null). A IA que fazia a análise vem primeiro.</summary>
    async Task<Provedor?> PerguntarRetomadaAsync(Processo processo, Provedor original, string? motivo = null)
    {
        var outra = original == Provedor.Claude ? Provedor.ChatGpt : Provedor.Claude;
        string[] itens = [$"Continuar com o {Provedores.Nome(original)} (de onde parou)", $"Continuar com o {Provedores.Nome(outra)}", "Agora não"];
        var texto = $"A análise de {FluxoNovoRelatorio.Identificacao(processo)} com o {Provedores.Nome(original)} não terminou"
                    + (string.IsNullOrWhiteSpace(motivo) ? "" : $" ({motivo.TrimEnd('.')})")
                    + ". Os arquivos de trabalho já gravados são aproveitados. Qual IA deve continuar?";
        return await Perguntas.Atual.EscolherNaListaAsync(FluxoNovoRelatorio.RetomarTitulo, texto, itens, 0, "Continuar") switch
        {
            0 => original,
            1 => outra,
            _ => null,
        };
    }

    /// <summary>Uma análise nova no mesmo processo, pedindo à IA que siga do ponto em que a anterior parou (sem anexos novos).</summary>
    Task<ExecucaoEmCurso?> ContinuarComOutraIaAsync(Provedor p, Processo processo) => NaFilaAsync(async () =>
    {
        NR.AvisoResultado.IsOpen = false;
        await LerPecasAsync();
        return await PartirAsync(() => sessaoNR.AnalisarAsync(p, processo, [], FluxoNovoRelatorio.PedidoContinuar), enviados: null);
    });

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
    void MostrarImportacaoPendente(Processo processo, string mensagem, InfoBarSeverity severidade)
    {
        if (fechada) return;
        var aviso = NR.AvisoResultado;
        aviso.Severity = severidade;
        aviso.Title = severidade == InfoBarSeverity.Success ? "Análise concluída" : "O relatório não foi importado";
        aviso.Message = mensagem;
        NR.BotaoImportarPendente.Visibility = Visibility.Visible;
        NR.BotaoContinuar.Visibility = Visibility.Collapsed;
        NR.BotaoMostrarRegistroResultado.Visibility = Visibility.Collapsed;
        aviso.IsOpen = true;
        if (!PaginaNovoRelatorioVisivel)
        {
            var modulo = AvisoDoModulo;
            if (!(modulo.IsOpen && modulo.Severity == InfoBarSeverity.Error)) MostrarMensagem(severidade, aviso.Title, mensagem);
            var botao = new Button { Content = "Importar relatório" };
            Dicas.Aplicar(botao, "Importar relatório", "Traz para o app o relatório da análise que terminou com o editor fechado.");
            botao.Click += async (_, _) => await TentarAsync(async () => { if (importacoesPendentes.FirstOrDefault() is { } imp) await ImportarPendenteAsync(imp); });
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
            MostrarMensagem(InfoBarSeverity.Warning, "O relatório não foi importado", FluxoNovoRelatorio.TextoSemInicio(imp.Processo, imp.Registro));
            return false;
        }
        var r = await Task.Run(() => ResultadoAnalise.Localizar(imp.Processo.Pasta, Provedor.Claude, inicio.UtcDateTime, imp.Registro));
        if (r.Documento is null)
        {
            MostrarImportacaoPendente(imp.Processo, r.Motivo ?? "", InfoBarSeverity.Error);
            return false;
        }
        return await ImportarAsync(imp with { Documento = r.Documento, Pdf = r.Pdf });
    }

    /// <summary>Nada em curso que a abertura de um relatório interromperia: a página Novo relatório à frente, nenhum relatório aberto e nenhum despacho
    /// em edição (passo 2 ou 3, preparando ou gravando).</summary>
    bool PodeAbrirSemInterromper =>
        PaginaNovoRelatorioVisivel && sessao is null
        && novo is not ({ Passo: PassoNovoDespacho.Conteudo or PassoNovoDespacho.Conferencia } or { Preparando: true } or { Gravando: true });

    /// <summary>Importa o documento que o kit declarou e abre o <c>.rpmvni</c>; só então a pendência sai e o registro recebe o <c>.rpmvni</c>.
    /// <paramref name="abrirDireto"/> (lido na vez da fila) false ⇒ só cria o <c>.rpmvni</c> e mostra "Relatório pronto" com "Abrir relatório";
    /// null = gesto do usuário ("Importar relatório"): abre.</summary>
    async Task<bool> ImportarAsync(ImportacaoPendente imp, Func<bool>? abrirDireto = null)
    {
        var abriu = false;
        var criado = await NaFilaAsync(async () =>
        {
            abriu = abrirDireto?.Invoke() ?? true;
            return await ImportarAnaliseParaAsync(imp.Processo, imp.Documento!, abriu);
        });
        var s = abriu ? sessao : null;
        if (criado is null || (abriu && s is null))
        {
            if (!importacoesPendentes.Any(i => MesmoRegistro(i.Registro, imp.Registro))) importacoesPendentes.Add(imp);
            else importacoesPendentes[importacoesPendentes.FindIndex(i => MesmoRegistro(i.Registro, imp.Registro))] = imp;
            MostrarImportacaoPendente(imp.Processo, FluxoNovoRelatorio.TextoNaoImportado(imp.Provedor, imp.Processo), InfoBarSeverity.Error);
            return false;
        }
        EsquecerPendenteClaude(imp.Registro);
        var pmvrel = s?.Caminho ?? criado;
        try
        {
            using var registro = RegistroExecucao.Abrir(imp.Registro, imp.Processo.Pasta);
            registro.Atualizar(o => { o["pmvrel"] = pmvrel; o["document"] = imp.Documento; o["situacao"] = "concluida"; });
        }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException or InvalidOperationException) { System.Diagnostics.Debug.WriteLine("[registro] " + x.Message); }
        if (abriu) MostrarMensagem(InfoBarSeverity.Success, "Relatório importado", FluxoNovoRelatorio.TextoImportado(imp.Provedor, pmvrel, imp.Pdf));
        else MostrarRelatorioPronto(imp, pmvrel);
        return true;
    }

    /// <summary>"Relatório pronto" no módulo à vista, com "Abrir relatório" (na fila, como Abrir; quem decide abrir é o usuário).</summary>
    void MostrarRelatorioPronto(ImportacaoPendente imp, string pmvrel)
    {
        if (fechada) return;
        MostrarMensagem(InfoBarSeverity.Success, FluxoNovoRelatorio.RelatorioProntoTitulo, FluxoNovoRelatorio.TextoRelatorioPronto(imp.Provedor, pmvrel, imp.Pdf));
        var botao = new Button { Content = FluxoNovoRelatorio.AbrirRelatorio };
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

    static DateTimeOffset? LerInicio(string pastaRegistro)
    {
        try
        {
            var j = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(pastaRegistro, "execucao.json")));
            return j?["inicio"]?.GetValue<string>() is { } t && DateTimeOffset.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }
}
