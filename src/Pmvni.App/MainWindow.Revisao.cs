using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Pmvni.Composicao.Kit;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Ponte;
using Pmvni.Revisao;
using Windows.Foundation;

namespace Pmvni.App;

/// <summary>Revisão dos itens no módulo Relatórios (Tarefa 9; desenho §5.3–§5.5 e §8.4): seleção, Página Inicial e Revisão da faixa,
/// painel lateral (Seções | Excluídos | Histórico), Propriedades, menu de contexto da folha e a edição no A4. Todo comando passa por
/// <see cref="AplicarAsync"/>: conclui a edição aberta → <see cref="SessaoRevisao.Testar"/> → <see cref="Recompositor.Preparar"/> do
/// candidato (recusa ⇒ "A alteração não foi aplicada", nada muda) → <see cref="SessaoRevisao.Aplicar"/> → recomposição. A página recebe
/// <c>estadoEditor</c> sempre, também quando nada foi aplicado (sem ele, uma edição recusada ficaria parada na tela).</summary>
public sealed partial class MainWindow
{
    static readonly string[] NomesCampos = ["Título", "Verificação técnica", "Providência", "Base legal", "Referência"];

    string? selecionado;          // id do item selecionado (incluído ou excluído)
    string? ultimoCampo;          // último campo clicado no item selecionado (F2 edita esse; sem ele, a verificação)
    string? itemNasPropriedades;  // item cujos valores estão no painel (a justificativa digitada vai para ele)
    bool mostrarOriginal, somenteAlterados, aplicando, atualizandoPropriedades;
    IReadOnlyList<ItemNavegacao> navegacaoCompleta = [];
    FiltroSituacao filtroSituacao = FiltroSituacao.Todos;
    readonly HashSet<string> secoesRecolhidas = new(StringComparer.Ordinal);
    readonly List<string> ultimosErros = [];
    (string Item, string Campo, string Texto)? edicaoRecusada; // o campo que a última conclusão da edição no A4 teve recusado pela conferência
    long ultimaLatenciaMs;

    // ---------- acessores do roteiro ----------
    internal string? Selecionado => selecionado;
    internal IReadOnlyList<string> UltimosErros => ultimosErros;
    internal long UltimaLatenciaMs => ultimaLatenciaMs;
    internal bool Aplicando => aplicando;
    internal bool PropriedadesVisivel => R.PainelPropriedades.Visibility == Visibility.Visible;
    internal string TextoPropriedadesTitulo => R.PropTitulo.Text;
    internal string TextoAbaExcluidos => R.AbaExcluidos.Text;
    internal int LinhasExcluidos => R.ListaExcluidos.Items.Count;
    internal IReadOnlyList<string> LinhasHistorico => R.ListaHistorico.ItemsSource as IReadOnlyList<string> ?? [];
    internal string RotuloExcluirRestaurar => Faixa.Rotulo(ComandosApp.InicioExcluirRestaurar);
    internal MenuFlyout? UltimoMenuContexto { get; private set; }
    /// <summary>O último menu aberto pela etiqueta de situação da lista de seções (o roteiro confere as opções).</summary>
    internal MenuFlyout? UltimoMenuEtiqueta { get; private set; }
    internal Flyout? UltimoFlyoutExcluido { get; private set; }
    internal int AceleradoresDeTeclado => Raiz.KeyboardAccelerators.Count;
    internal void FocarFolhas() => R.Navegador.Focus(FocusState.Programmatic);

    /// <summary>Ids do mapa de atalhos sem manipulador: tem de ser vazia.</summary>
    internal IReadOnlyList<string> AtalhosSemManipulador() =>
        Atalhos.Mapa.Values.Where(id => !manipuladores.ContainsKey(id)).ToList();

    ItemRevisao? ItemSelecionado => selecionado is null ? null : sessao?.Revisao.Projeto.Item(selecionado);

    /// <summary>A linha de tabela selecionada (ÍNDICES URBANÍSTICOS, ILUMINAÇÃO E VENTILAÇÃO): o id dela (<c>indices/02</c>), ou null.</summary>
    string? LinhaSelecionada => TabelasRevisao.EhLinha(selecionado) && sessao?.Revisao.Projeto is { } p && TabelasRevisao.Existe(p, selecionado) ? selecionado : null;

    bool Editavel => sessao is { SomenteLeitura: false, Atual: not null } && documento is not null && !ocupado;

    // ---------- ligação dos controles ----------

    void IniciarRevisao()
    {
        R.AbasPainel.SelectionChanged += (_, _) => MostrarAbaDoPainel();
        R.EtiquetaClicada += async (item, alvo) => await MostrarMenuDaEtiquetaAsync(item, alvo);
        // Painel de navegação (correções 2026-11-15): seções recolhíveis, pesquisa e filtro rápido por situação.
        R.SecaoAlternada += item =>
        {
            if (!secoesRecolhidas.Remove(item.Rotulo)) secoesRecolhidas.Add(item.Rotulo);
            MontarSecoes();
        };
        R.CaixaFiltroSecoes.TextChanged += (_, _) => MontarSecoes();
        MontarBotoesFiltro();
        R.ListaExcluidos.ItemClick += async (_, e) => { if (e.ClickedItem is FrameworkElement { Tag: string id }) await NaFilaAsync(() => TentarAsync(() => SelecionarAsync(id))); };
        R.BotaoFecharPropriedades.Click += (_, _) => MostrarPropriedades(false);
        R.PropNoPdf.Click += async (_, _) =>
        {
            if (ItemSelecionado is not { } it) return;
            await TentarAsync(() => AplicarAsync(it.Incluido ? new Excluir(it.Id) : new Restaurar(it.Id)));
        };
        foreach (var c in Faixas.OrdemSituacoes) R.PropSituacao.Items.Add(new ComboBoxItem { Content = Situacoes.Sit[c].Texto, Tag = c });
        R.PropSituacao.SelectionChanged += async (_, _) =>
        {
            if (atualizandoPropriedades || R.PropSituacao.SelectedItem is not ComboBoxItem { Tag: string c }) return;
            if (LinhaSelecionada is { } linha)
            {
                // Linha de tabela: a lista mostra as situações de item com o mesmo texto impresso (ATENDE = CONTA_CONFERE…).
                if (c == TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(sessao!.Revisao.Projeto, linha))) return;
                await TentarAsync(() => AplicarAsync(new DefinirSituacao(linha, TabelasRevisao.SituacaoDeLinha(c)!)));
                return;
            }
            if (ItemSelecionado is not { } it || c == SituacaoEfetiva(it)) return;
            await TentarAsync(() => AplicarAsync(new DefinirSituacao(it.Id, c)));
        };
        R.PropJustificativa.LostFocus += async (_, _) => await AplicarJustificativaAsync();
        R.PropJustificativa.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            await AplicarJustificativaAsync();
        };
        R.PropAjustarTexto.Click += async (_, _) => await AjustarTextoComIaAsync();
        R.PropReverComIa.Click += async (_, _) => await ReverComIaAsync();
        R.PropAjustarCancelar.Click += (_, _) => CancelarIa();
        R.PropRestaurarOriginal.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.InicioRestaurarOriginal);
        BotaoDesfazer.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.EditarDesfazer);
        BotaoRefazer.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.EditarRefazer);
        Dicas.AplicarComando(BotaoDesfazer, ComandosApp.EditarDesfazer, "Desfazer");
        Dicas.AplicarComando(BotaoRefazer, ComandosApp.EditarRefazer, "Refazer");
        R.AvisoDesatualizado.Message = SessaoEdicao.TextoDesatualizada;
        R.BotaoRecompor.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(RecomporDesatualizadaAsync));
        AtualizarPropriedades();
    }

    void RegistrarManipuladoresRevisao()
    {
        foreach (var codigo in Faixas.OrdemSituacoes)
            manipuladores[ComandosApp.ComandoDaSituacao(codigo)] = () =>
                ItemSelecionado is { Incluido: true } it ? AplicarAsync(new DefinirSituacao(it.Id, codigo))
                : LinhaSelecionada is { } linha ? AplicarAsync(new DefinirSituacao(linha, TabelasRevisao.SituacaoDeLinha(codigo)!))
                : Task.CompletedTask;
        manipuladores[ComandosApp.InicioExcluirRestaurar] = () =>
            ItemSelecionado is { } it ? AplicarAsync(it.Incluido ? new Excluir(it.Id) : new Restaurar(it.Id)) : Task.CompletedTask;
        manipuladores[ComandosApp.InicioNovoItem] = NovoItemAsync;
        manipuladores[ComandosApp.InicioEditarTexto] = EditarSelecionadoAsync;
        manipuladores[ComandosApp.InicioOriginal] = () =>
        {
            mostrarOriginal = !mostrarOriginal;
            if (mostrarOriginal && !PropriedadesVisivel) MostrarPropriedades(true);
            AtualizarPropriedades();
            AtualizarComandos();
            return Task.CompletedTask;
        };
        manipuladores[ComandosApp.InicioRestaurarOriginal] = () =>
            ItemSelecionado is { } it ? AplicarAsync(new RestaurarItemOriginal(it.Id))
            : LinhaSelecionada is { } linha ? AplicarAsync(new RestaurarItemOriginal(linha))
            : Task.CompletedTask;
        manipuladores[ComandosApp.InicioItemAnterior] = () => IrParaItemAsync(-1);
        manipuladores[ComandosApp.InicioItemProximo] = () => IrParaItemAsync(+1);
        manipuladores[ComandosApp.InicioProximoPendente] = ProximoPendenteAsync;
        manipuladores[ComandosApp.RevisaoMarcas] = AlternarMarcasAsync;
        manipuladores[ComandosApp.RevisaoSomenteAlterados] = () =>
        {
            somenteAlterados = !somenteAlterados;
            MontarSecoes();
            AtualizarComandos();
            return Task.CompletedTask;
        };
        manipuladores[ComandosApp.RevisaoConferencia] = () => Perguntas.Atual.InformarAsync("Conferência", TextoConferencia());
        manipuladores[ComandosApp.ExibirPropriedades] = () => { MostrarPropriedades(!PropriedadesVisivel); return Task.CompletedTask; };
        manipuladores[ComandosApp.EditarDesfazer] = () => ExecutarRevisaoAsync(null, s => s.Revisao.Desfazer());
        manipuladores[ComandosApp.EditarRefazer] = () => ExecutarRevisaoAsync(null, s => s.Revisao.Refazer());
        manipuladores[ComandosApp.JanelaRegioes] = AlternarRegiaoAsync;
    }

    /// <summary>Habilita, liga e rotula os comandos da revisão pelo item selecionado e pelo estado do relatório.</summary>
    void AtualizarComandosRevisao(bool temRelatorio)
    {
        var editavel = temRelatorio && Editavel;
        var it = temRelatorio ? ItemSelecionado : null;
        var linha = temRelatorio ? LinhaSelecionada : null;
        // Uma linha de tabela se revisa como um item incluído (situação, texto, original), mas não se exclui.
        var incluido = it is { Incluido: true } || linha is not null;
        Faixa.Habilitar(ComandosApp.InicioSituacao, editavel && incluido);
        foreach (var c in Faixas.OrdemSituacoes) Faixa.Habilitar(ComandosApp.ComandoDaSituacao(c), editavel && incluido);
        Faixa.Habilitar(ComandosApp.InicioNovoItem, editavel);
        Faixa.Habilitar(ComandosApp.InicioExcluirRestaurar, editavel && it is not null);
        Faixa.DefinirRotulo(ComandosApp.InicioExcluirRestaurar, it is { Incluido: false } ? "Restaurar item" : "Excluir item");
        Faixa.Habilitar(ComandosApp.InicioEditarTexto, editavel && incluido);
        Faixa.Habilitar(ComandosApp.InicioOriginal, temRelatorio && (it is not null || linha is not null));
        Faixa.Ligar(ComandosApp.InicioOriginal, mostrarOriginal);
        Faixa.Habilitar(ComandosApp.InicioRestaurarOriginal, editavel && (it is { Criado: false } && TemRevisao(it)
            || linha is not null && TabelasRevisao.Revisada(sessao!.Revisao.Projeto, linha)));
        Faixa.Habilitar(ComandosApp.InicioItemAnterior, temRelatorio);
        Faixa.Habilitar(ComandosApp.InicioItemProximo, temRelatorio);
        Faixa.Habilitar(ComandosApp.InicioProximoPendente, temRelatorio);
        Faixa.Habilitar(ComandosApp.RevisaoMarcas, temRelatorio);
        Faixa.Ligar(ComandosApp.RevisaoMarcas, ajustes.MarcasRevisao);
        Faixa.Habilitar(ComandosApp.RevisaoSomenteAlterados, temRelatorio);
        Faixa.Ligar(ComandosApp.RevisaoSomenteAlterados, somenteAlterados);
        Faixa.Habilitar(ComandosApp.RevisaoConferencia, temRelatorio);
        Faixa.Habilitar(ComandosApp.RevisaoComparar, temRelatorio);
        Faixa.Ligar(ComandosApp.ExibirPropriedades, PropriedadesVisivel);
        // Fora de Relatórios o relatório fica escondido: Desfazer e Refazer (faixa e barra de título) não valem lá (ComandosApp.SoDoRelatorio).
        var desfazer = editavel && moduloAtual == Modulo.Relatorios && sessao!.Revisao.PodeDesfazer;
        var refazer = editavel && moduloAtual == Modulo.Relatorios && sessao!.Revisao.PodeRefazer;
        Faixa.Habilitar(ComandosApp.EditarDesfazer, desfazer);
        Faixa.Habilitar(ComandosApp.EditarRefazer, refazer);
        BotaoDesfazer.IsEnabled = desfazer;
        BotaoRefazer.IsEnabled = refazer;
        AtualizarComandosFiguras(temRelatorio);
    }

    // ---------- o fluxo de um comando ----------

    /// <summary>Aplica um comando de revisão (desenho §8.4) e recompõe. Recusado pela sessão ou pela conferência do kit: InfoBar
    /// "A alteração não foi aplicada" com os erros (também em <see cref="UltimosErros"/>) e nada muda.</summary>
    internal Task AplicarAsync(Comando c) => ExecutarRevisaoAsync(s => s.Revisao.Testar(c), s => s.Revisao.Aplicar(c));

    /// <summary>A edição de um campo pelo host: a mesma rota do <c>editar</c> da página (Enter no A4). Numa linha de tabela, o comando
    /// vem de <see cref="ComandoDeEdicao"/>: a edição do valor (ou do limite) de um índice leva junto o recálculo da situação.</summary>
    internal Task EditarTextoAsync(string itemId, string campo, string texto) =>
        NaFilaAsync(() => AplicarAsync(sessao?.Revisao.Projeto is { } pj ? ComandoDeEdicao(pj, itemId, campo, texto) : new DefinirCampo(itemId, campo, texto)));

    /// <summary>O que a última edição manual de uma linha de tabela recalculou (ou não conseguiu recalcular): vira o aviso ao fim do comando.</summary>
    EdicaoDeLinha? avisoRecalculo;

    /// <summary>O último aviso de recálculo mostrado (o roteiro confere).</summary>
    internal string? UltimoAvisoRecalculo { get; private set; }

    /// <summary>O comando da edição manual de um texto (correções 2026-11-16). Item: <see cref="DefinirCampo"/>. Linha de tabela: o
    /// número digitado sem unidade ganha a do original e, se o texto é o valor no projeto ou o limite de um índice com critério numérico,
    /// a situação e o texto da diferença são refeitos pelo editor, sem IA (<see cref="RecalculoLinha.AoEditar"/>), tudo num só Desfazer.
    /// O critério ilegível não é adivinhado: a edição entra sozinha e o aviso sugere «Rever com a IA».</summary>
    Comando ComandoDeEdicao(Projeto pj, string itemId, string campo, string texto)
    {
        if (!TabelasRevisao.EhLinha(itemId)) return new DefinirCampo(itemId, campo, texto);
        texto = IaRecalculoIndices.CompletarUnidade(texto, TabelasRevisao.TextoOriginal(pj, itemId, campo));
        if (!TabelasRevisao.Existe(pj, itemId)) return new DefinirCampo(itemId, campo, texto);
        var e = RecalculoLinha.AoEditar(pj, itemId, campo, texto);
        avisoRecalculo = e.Mensagem is null ? null : e;
        return e.Comando;
    }

    /// <summary>Depois de um comando: diz o que o editor recalculou (ou por que não recalculou) na linha editada à mão.</summary>
    void MostrarAvisoDeRecalculo()
    {
        if (avisoRecalculo is not { Mensagem: { } texto } e) return;
        avisoRecalculo = null;
        if (ultimosErros.Count > 0) return; // a edição foi recusada: o aviso de recusa já está na tela
        UltimoAvisoRecalculo = texto;
        if (e.Recalculado) MostrarMensagem(InfoBarSeverity.Success, "Linha recalculada", texto);
        else MostrarMensagem(InfoBarSeverity.Informational, "Situação não recalculada", texto);
    }

    // ---------- fila de comandos ----------

    // Uma fila só (FIFO) para tudo o que muda o relatório ou depende da tela composta: comandos da faixa e dos atalhos, comandos de
    // revisão (Propriedades, menus, edição no A4) e seleção pela lista. Nada é descartado por chegar durante outro: espera a vez e é
    // conferido de novo contra o projeto como está nessa hora. Quem já está na vez (um manipulador que chama AplicarAsync) não
    // espera de novo: a vez segue no fluxo assíncrono (AsyncLocal).
    readonly SemaphoreSlim fila = new(1, 1);
    static readonly AsyncLocal<bool> naVez = new();
    int naFila;

    /// <summary>Comandos esperando a vez ou rodando.</summary>
    internal int ComandosNaFila => naFila;

    async Task NaFilaAsync(Func<Task> acao)
    {
        if (naVez.Value) { await acao(); return; }
        naFila++;
        await fila.WaitAsync();
        try
        {
            naVez.Value = true;
            await acao();
        }
        finally
        {
            naVez.Value = false;
            fila.Release();
            naFila--;
        }
    }

    /// <summary>Uma tarefa solta (fire-and-forget) iniciada de dentro de um comando da fila não herda a vez: o que ela puser na fila espera o comando
    /// atual terminar, em vez de rodar em paralelo com o próximo (a mudança do AsyncLocal fica só no fluxo desta tarefa).</summary>
    static void Soltar(Func<Task> tarefa)
    {
        _ = ForaDaVez(tarefa);
        static async Task ForaDaVez(Func<Task> t)
        {
            naVez.Value = false;
            await t();
        }
    }

    /// <summary><see cref="NaFilaAsync(Func{Task})"/> com resultado.</summary>
    async Task<T> NaFilaAsync<T>(Func<Task<T>> acao)
    {
        T resultado = default!;
        // Corpo em bloco: com uma expressão, o lambda viraria Func<Task<T>> e chamaria esta mesma sobrecarga sem fim.
        Func<Task> semResultado = async () => { resultado = await acao(); };
        await NaFilaAsync(semResultado);
        return resultado;
    }

    /// <summary>"«Excluir item» não está disponível agora: selecione um item…" — todo comando que não pode rodar diz por quê.</summary>
    void AvisarIndisponivel(string id)
    {
        var rotulo = RotuloDoComando(id);
        string motivo;
        if (id == ComandosApp.ArquivoImprimir)
        {
            // Imprimir vale em vários módulos: o motivo é o do módulo à vista (a dica do botão desabilitado).
            var porque = Faixa.Dica(id).Trim().TrimEnd('.');
            MostrarMensagem(InfoBarSeverity.Informational, "Comando indisponível", $"«{rotulo}» não está disponível agora: {(porque.Length > 0 ? char.ToLowerInvariant(porque[0]) + porque[1..] : "não há documento para imprimir")}.");
            return;
        }
        if (id.StartsWith("processos.", StringComparison.Ordinal) || id.StartsWith("despachos.", StringComparison.Ordinal))
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Comando indisponível", $"«{rotulo}» não está disponível agora: {MotivoIndisponivelDasPastas(id)}.");
            return;
        }
        if (documento is null) motivo = "a visualização ainda não está pronta";
        else if (ocupado) motivo = "aguarde a operação em andamento terminar";
        else if (sessao?.Atual is null) motivo = "abra ou importe um relatório";
        else if (sessao.SomenteLeitura && id is not (ComandosApp.ArquivoSalvarComo or ComandosApp.ArquivoExportarPdf)) motivo = "o relatório está aberto somente para leitura";
        else if (LinhaSelecionada is not null && id == ComandosApp.InicioExcluirRestaurar)
            motivo = "as linhas das tabelas não se excluem; edite os textos ou a situação";
        else if (ItemSelecionado is { Criado: true } && id == ComandosApp.InicioRestaurarOriginal)
            motivo = "o item foi criado na revisão e não tem original";
        else if (ItemSelecionado is null && LinhaSelecionada is null && (id.StartsWith("inicio.", StringComparison.Ordinal) && id is not (ComandosApp.InicioItemAnterior or ComandosApp.InicioItemProximo or ComandosApp.InicioProximoPendente or ComandosApp.InicioNovoItem)))
            motivo = "selecione um item nas folhas ou na lista de seções";
        else if (ItemSelecionado is { Incluido: false } && (id == ComandosApp.InicioEditarTexto || ComandosApp.SituacaoDoComando(id) is not null || id == ComandosApp.InicioSituacao))
            motivo = "o item selecionado está excluído (restaure-o antes)";
        else if (id == ComandosApp.EditarDesfazer) motivo = "não há nada para desfazer";
        else if (id == ComandosApp.EditarRefazer) motivo = "não há nada para refazer";
        else if (id == ComandosApp.ArquivoExportarPdf) motivo = Faixa.Dica(id);
        else motivo = "não há o que fazer com o relatório como está";
        MostrarMensagem(InfoBarSeverity.Informational, "Comando indisponível", $"«{rotulo}» não está disponível agora: {motivo}.");
    }

    string RotuloDoComando(string id)
    {
        if (RotulosForaDaFaixa.TryGetValue(id, out var r)) return r;
        // Ctrl+1 … Ctrl+5 (lista de atalhos): "Ir para Relatórios" etc.
        if (Modulos.DoComando(id) is { } m) return "Ir para " + Modulos.Rotulo(m);
        var f = Faixa.Rotulo(id);
        return f.Length > 0 ? f : id;
    }

    static readonly IReadOnlyDictionary<string, string> RotulosForaDaFaixa = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [ComandosApp.EditarDesfazer] = "Desfazer",
        [ComandosApp.EditarRefazer] = "Refazer",
        [ComandosApp.JanelaRegioes] = "Alternar entre faixa, painel, folhas e propriedades",
        [ComandosApp.PesquisaFocar] = "Pesquisar no app (ajustes, funções, arquivos, leis)",
        [ComandosApp.InicioItemAnterior] = "Item anterior",
        [ComandosApp.InicioItemProximo] = "Próximo item",
        [ComandosApp.InicioExcluirRestaurar] = "Excluir ou restaurar o item",
        [ComandosApp.ArquivoImprimir] = "Imprimir ou salvar como PDF",
    };

    /// <summary><paramref name="testar"/>: o projeto como ficaria (null = não precisa testar: desfazer e refazer voltam a estados que já
    /// foram compostos); <paramref name="aplicar"/>: aplica e devolve o registro (null = nada mudou). Entra na fila: um comando que chega
    /// durante outro espera a vez e é testado contra o projeto de então.</summary>
    Task ExecutarRevisaoAsync(Func<SessaoEdicao, Projeto>? testar, Func<SessaoEdicao, RegistroHistorico?> aplicar) =>
        NaFilaAsync(() => ExecutarNaVezAsync(testar, aplicar));

    async Task ExecutarNaVezAsync(Func<SessaoEdicao, Projeto>? testar, Func<SessaoEdicao, RegistroHistorico?> aplicar)
    {
        if (sessao is not { Atual: not null } s)
        {
            avisoRecalculo = null;
            MostrarMensagem(InfoBarSeverity.Warning, "A alteração não foi aplicada", "Nenhum relatório aberto.");
            return;
        }
        if (ocupado)
        {
            avisoRecalculo = null;
            // Abrir, importar ou exportar chamados fora da fila (o roteiro): a alteração não entra e a página volta ao estado do host.
            MostrarMensagem(InfoBarSeverity.Warning, "A alteração não foi aplicada", "Outra operação ainda está em andamento; tente de novo em seguida.");
            await s.EnviarEstadoAsync();
            return;
        }
        aplicando = true;
        var relogio = Stopwatch.StartNew();
        ultimosErros.Clear();
        var estadoEnviado = false;
        try
        {
            if (s.SomenteLeitura) { Recusar([s.MotivoSomenteLeitura ?? "O relatório está aberto somente para leitura."]); return; }
            var recompor = false;
            Preparado? preparado = null;
            try
            {
                // O texto de um campo em edição entra antes (como o Enter). Se o comando seguinte for recusado, a tela ainda é recomposta
                // com o texto aplicado: tela e modelo nunca ficam diferentes.
                recompor = await ConcluirEdicaoAbertaAsync(s) == true;
                if (testar is not null)
                {
                    var candidato = testar(s);
                    preparado = await Task.Run(() => s.Recompositor.Preparar(candidato));
                }
                if (aplicar(s) is not null) recompor = true; // sem efeito: o candidato é o projeto de agora e vale como preparado
            }
            catch (ComandoRecusadoException e) { Recusar([e.Message]); preparado = null; }
            catch (RecomposicaoRecusadaException e) { Recusar(e.Erros); preparado = null; }
            if (!recompor) return;
            estadoEnviado = true; // a recomposição manda o estado do editor (no finally dela)
            var r = await s.RecomporAsync(preparado);
            MostrarRecomposicao(r);
        }
        catch (Exception e)
        {
            var texto = Erro(e);
            ultimosErros.Add(texto);
            // A alteração entrou no relatório e só a tela ficou para trás: o aviso de folhas desatualizadas (com Recompor) diz isso.
            if (s.Desatualizada) MostrarMensagem(InfoBarSeverity.Error, "As folhas não foram recompostas", texto + ". A alteração foi aplicada ao relatório; use Recompor.");
            else MostrarMensagem(InfoBarSeverity.Error, "Não foi possível aplicar a alteração", texto);
        }
        finally
        {
            if (!estadoEnviado) await s.EnviarEstadoAsync();
            ultimaLatenciaMs = relogio.ElapsedMilliseconds;
            aplicando = false;
            AtualizarPaineis();
            AtualizarComandos();
            MostrarAvisoDeRecalculo();
        }
    }

    /// <summary>Campo em edição no A4: conclui antes (como o Enter) e aplica o texto, sem recompor ainda. Devolve true se mudou o projeto,
    /// false se havia edição mas nada mudou (texto igual ou recusado: a página espera o estado do editor) e null se não havia edição.</summary>
    async Task<bool?> ConcluirEdicaoAbertaAsync(SessaoEdicao s)
    {
        edicaoRecusada = null;
        if (documento is null) return null;
        JsonElement d;
        try { d = await documento.ChamarAsync("concluirEdicao", new { }, LimiteTela); }
        catch (Exception e) { Debug.WriteLine("[concluirEdicao] " + e.Message); return null; }
        if (!d.TryGetProperty("ref", out var rf) || rf.GetString() is not { } item
            || !d.TryGetProperty("campo", out var cp) || cp.GetString() is not { } campo
            || !d.TryGetProperty("texto", out var tx) || tx.GetString() is not { } texto)
            return null; // semEdicao
        var c = ComandoDeEdicao(s.Revisao.Projeto, item, campo, texto);
        try
        {
            var candidato = s.Revisao.Testar(c);
            await Task.Run(() => s.Recompositor.Preparar(candidato));
            return s.Revisao.Aplicar(c) is not null;
        }
        catch (ComandoRecusadoException e) { ultimosErros.Add(e.Message); edicaoRecusada = (item, campo, texto); }
        catch (RecomposicaoRecusadaException e) { ultimosErros.AddRange(e.Erros); edicaoRecusada = (item, campo, texto); }
        avisoRecalculo = null; // recusada: nada foi recalculado
        return false;
    }

    /// <summary>O texto de um campo em edição que a conferência recusou ao concluir antes de fechar, com o motivo.</summary>
    internal sealed record RecusaDeEdicao(string Item, string Campo, string Texto, string Motivo);

    /// <summary>Salvar, Exportar, Fechar (e Abrir outro, que fecha) concluem antes o campo em edição no A4, como o Enter: o texto digitado
    /// entra no relatório e a tela é recomposta; o arquivo e o PDF nunca saem sem ele. Recusado pela conferência: o aviso de sempre e a
    /// página volta ao texto anterior. Recomposição que falha: a sessão fica desatualizada (exportar recusa até recompor). Devolve a recusa (o
    /// texto e o motivo) quando a conferência não aceitou o campo em edição: quem fecha o relatório pergunta antes de deixar o texto para trás;
    /// null = nada a dizer (sem edição, texto igual ou aplicado).</summary>
    async Task<RecusaDeEdicao?> ConcluirEdicaoAntesAsync(SessaoEdicao s)
    {
        if (s.SomenteLeitura || s.Atual is null || ocupado || !ReferenceEquals(sessao, s)) return null;
        ultimosErros.Clear();
        var mudou = await ConcluirEdicaoAbertaAsync(s);
        if (mudou is null) return null;
        try
        {
            if (mudou == false)
            {
                var recusa = edicaoRecusada is var (item, campo, texto) && ultimosErros.Count > 0
                    ? new RecusaDeEdicao(item, campo, texto, string.Join(" ", ultimosErros.Distinct().Take(3)))
                    : null;
                if (ultimosErros.Count > 0) Recusar(ultimosErros.ToList());
                await s.EnviarEstadoAsync();
                return recusa;
            }
            var r = await s.RecomporAsync();
            MostrarRecomposicao(r);
        }
        catch (Exception e)
        {
            ultimosErros.Add(Erro(e));
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível aplicar a alteração", Erro(e));
        }
        finally
        {
            AtualizarPaineis();
            AtualizarComandos();
            MostrarAvisoDeRecalculo();
        }
        return null;
    }

    /// <summary>"Voltar à edição": o campo volta a ser editado no A4 com o texto que a conferência recusou (a página já voltou ao anterior).</summary>
    async Task DevolverEdicaoAsync(RecusaDeEdicao r)
    {
        if (documento is null || sessao is null || !Editavel) return;
        selecionado ??= r.Item;
        R.Navegador.Focus(FocusState.Programmatic);
        try { await documento.ChamarAsync("editarCampo", new { @ref = r.Item, campo = r.Campo, texto = r.Texto }, LimiteTela); }
        catch (Exception e) { Debug.WriteLine("[devolverEdicao] " + e.Message); }
    }

    /// <summary>"Recompor" do aviso de folhas desatualizadas: compõe de novo o relatório como está (a alteração já foi aplicada). true = a tela
    /// voltou a mostrar o relatório atual (e a exportação volta a valer).</summary>
    internal async Task<bool> RecomporDesatualizadaAsync()
    {
        if (sessao is not { Atual: not null } s || ocupado) return false;
        try
        {
            var r = await s.RecomporAsync();
            MostrarRecomposicao(r);
            return true;
        }
        catch (Exception e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível recompor", Erro(e));
            return false;
        }
        finally
        {
            AtualizarPaineis();
            AtualizarComandos();
        }
    }

    void Recusar(IReadOnlyList<string> erros)
    {
        ultimosErros.AddRange(erros);
        MostrarMensagem(InfoBarSeverity.Warning, "A alteração não foi aplicada", string.Join(Environment.NewLine, erros.Distinct().Take(8)));
    }

    // ---------- seleção e navegação entre itens ----------

    /// <summary>Seleciona o item (null = nenhum): nas folhas (com rolagem, se ele está incluído), nas Propriedades e na faixa.</summary>
    internal async Task SelecionarAsync(string? itemId)
    {
        if (sessao is not { } s) return;
        var it = itemId is null ? null : s.Revisao.Projeto.Item(itemId);
        var linha = itemId is not null && TabelasRevisao.Existe(s.Revisao.Projeto, itemId);
        if (itemId is not null && it is null && !linha) throw new ArgumentException("O item não existe no relatório.", nameof(itemId));
        if (itemId != selecionado) ultimoCampo = null;
        selecionado = itemId;
        s.Selecao = itemId;
        MarcarSelecionadoNaLista();
        AtualizarPropriedades();
        AtualizarComandos();
        if (documento is null || s.Atual is null) return;
        try
        {
            if (it is { Incluido: true } || linha) await documento.ChamarAsync("selecionar", new { @ref = itemId, rolar = true }, LimiteTela);
            else await documento.ChamarAsync("selecionar", new { @ref = (string?)null, rolar = false }, LimiteTela);
        }
        catch (ErroWebException e) { Debug.WriteLine("[selecionar] " + e.Codigo); }
    }

    /// <summary>◂ Item / Item ▸ (Alt+↑/↓): o incluído anterior ou seguinte ao selecionado (de um excluído, pela posição dele).</summary>
    async Task IrParaItemAsync(int passo)
    {
        if (sessao?.Atual is null) return;
        var p = sessao.Revisao.Projeto;
        var incluidos = p.Incluidos;
        if (incluidos.Count == 0) return;
        ItemRevisao? alvo;
        if (ItemSelecionado is not { } atual) alvo = passo > 0 ? incluidos[0] : incluidos[^1];
        else if (passo > 0) alvo = incluidos.FirstOrDefault(i => i.Ordem > atual.Ordem) ?? (atual.Incluido ? null : incluidos[^1]);
        else alvo = incluidos.LastOrDefault(i => i.Ordem < atual.Ordem) ?? (atual.Incluido ? null : incluidos[0]);
        if (alvo is not null) await SelecionarAsync(alvo.Id);
    }

    async Task ProximoPendenteAsync()
    {
        if (sessao?.Atual is null) return;
        var id = Pendencias.Proximo(sessao.Revisao.Projeto, selecionado);
        if (id is null)
        {
            MostrarMensagem(InfoBarSeverity.Success, "Nada pendente", "Todos os itens incluídos estão ATENDE e nenhuma justificativa falta.");
            return;
        }
        await SelecionarAsync(id);
    }

    // ---------- novo item ----------

    /// <summary>O id do último item criado por «Novo item» (o roteiro confere).</summary>
    internal string? UltimoItemCriado { get; private set; }

    /// <summary>«Novo item» (correções 2026-11-16): cria um item de análise logo depois do item selecionado (sem item selecionado, no
    /// fim), com os mesmos campos dos outros e os textos de <see cref="ItemNovo"/> para trocar. É um comando como os demais: entra no
    /// histórico, Desfazer o tira, vai para o .rpmvni, o PDF e o resumo. Depois de criado, fica selecionado, com as Propriedades abertas.</summary>
    internal async Task NovoItemAsync()
    {
        if (sessao is not { Revisao.Projeto: { } p } s) return;
        if (!Editavel) { AvisarIndisponivel(ComandosApp.InicioNovoItem); return; }
        string id;
        using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            do id = IdItem.Novo(DateTimeOffset.UtcNow, rng); while (p.Item(id) is not null);
        var depois = ItemSelecionado?.Id;
        await AplicarAsync(new CriarItem(id, depois));
        if (!ReferenceEquals(sessao, s) || p.Item(id) is null) return;   // recusado: o aviso já está na tela
        UltimoItemCriado = id;
        await TentarAsync(() => SelecionarAsync(id));
        if (!PropriedadesVisivel) MostrarPropriedades(true);
        MostrarMensagem(InfoBarSeverity.Success, "Novo item",
            $"ITEM {p.NumeroAtual(id):00} criado{(depois is null ? " no fim do relatório" : "")}. Preencha o título, a situação e os textos (nas Propriedades ou nas folhas, com F2); "
            + "enquanto algum estiver por preencher, o PDF não é exportado. Desfazer tira o item.");
    }

    // ---------- edição no A4 ----------

    /// <summary>Editar texto / F2: o último campo clicado do item selecionado, ou a verificação.</summary>
    Task EditarSelecionadoAsync() =>
        ItemSelecionado is { Incluido: true } it ? IniciarEdicaoAsync(it.Id, ultimoCampo ?? "verificacao")
        : LinhaSelecionada is { } linha ? IniciarEdicaoAsync(linha, ultimoCampo ?? TabelasRevisao.CampoPadrao(linha))
        : Task.CompletedTask;

    /// <summary>Põe o campo em edição no A4 com o texto efetivo (as citações já numeradas). O foco vai às folhas para a digitação.</summary>
    async Task IniciarEdicaoAsync(string itemId, string campo)
    {
        if (documento is null || sessao is null) return;
        if (!Editavel) { AvisarIndisponivel(ComandosApp.InicioEditarTexto); return; }
        var p = sessao.Revisao.Projeto;
        string texto;
        if (TabelasRevisao.EhLinha(itemId))
        {
            // Linha de tabela: o texto do caminho (a célula, ou parte dela). Texto vazio no original não se edita.
            if (!TabelasRevisao.Editavel(p, itemId, campo))
            {
                if (TabelasRevisao.Existe(p, itemId))
                    MostrarMensagem(InfoBarSeverity.Informational, "Este texto não pode ser editado", "Ele está vazio no relatório original recebido.");
                return;
            }
            if (selecionado != itemId) await SelecionarAsync(itemId);
            texto = TabelasRevisao.TextoEfetivo(p, itemId, campo) ?? "";
        }
        else
        {
            if (p.Item(itemId) is not { Incluido: true } it || !Campos.Editaveis.Contains(campo)) return;
            if (selecionado != itemId) await SelecionarAsync(itemId);
            texto = DadosEfetivos.TextoEfetivo(p, it, campo) ?? "";
        }
        ultimoCampo = campo;
        R.Navegador.Focus(FocusState.Programmatic);
        try { await documento.ChamarAsync("editarCampo", new { @ref = itemId, campo, texto }, LimiteTela); }
        catch (ErroWebException e)
        {
            var msg = e.Dados.TryGetProperty("mensagem", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : e.Codigo;
            MostrarMensagem(InfoBarSeverity.Warning, "Este campo não pode ser editado nas folhas", msg ?? e.Message);
        }
    }

    // ---------- mensagens de interação da página ----------

    /// <summary>Eventos de interação da página (id <c>evento-N</c>; as respostas às chamadas do host não passam por aqui).</summary>
    async Task AoEventoDaPaginaAsync(MensagemWeb m)
    {
        var d = m.Dados;
        string? Texto(string nome) => d.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        double Numero(string nome) => d.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
        switch (m.Tipo)
        {
            case "selecionado":
                SelecionadoNaPagina(Texto("ref"), Texto("campo"));
                if (d.TryGetProperty("volumetria", out var vol) && vol.ValueKind == JsonValueKind.True)
                    MostrarPedidoVolumetria(new Point(Numero("x"), Numero("y")));
                break;
            case "pedirEdicao":
                if (Texto("ref") is { } r && Texto("campo") is { } c) await NaFilaAsync(() => IniciarEdicaoAsync(r, c));
                else if (FiguraDoRef(Texto("ref")) is { } fig) AbrirEditorFigura(fig); // duplo clique numa figura (Etapa 4)
                break;
            case "editar":
                if (sessao is null) break;
                // Chegando durante outro comando, a edição espera a vez na fila (nunca é descartada).
                if (Texto("ref") is { } item && Texto("campo") is { } campo && Texto("texto") is { } texto)
                    await EditarTextoAsync(item, campo, texto);
                else await sessao.EnviarEstadoAsync(); // mensagem incompleta: a página espera o estado
                break;
            case "edicaoCancelada":
                if (sessao is not null) await NaFilaAsync(() => sessao?.EnviarEstadoAsync() ?? Task.CompletedTask);
                break;
            case "atalho":
                if (Texto("tecla") is { } tecla && Atalhos.Comando(tecla) is { } id) await ExecutarComandoAsync(id);
                break;
            case "menuContexto":
                SelecionadoNaPagina(Texto("ref"), Texto("campo"));
                if (Texto("ref") is not null) MostrarMenuContexto(new Point(Numero("x"), Numero("y")));
                break;
            case "excluidoClicado":
                if (Texto("id") is { } excl) MostrarFlyoutExcluido(excl, new Point(Numero("x"), Numero("y")));
                break;
        }
    }

    void SelecionadoNaPagina(string? @ref, string? campo)
    {
        if (sessao is not { } s) return;
        figuraSelecionada = FiguraDoRef(@ref); // clique numa figura (Etapa 4)
        if (@ref is not null && s.Revisao.Projeto.Item(@ref) is null && !TabelasRevisao.Existe(s.Revisao.Projeto, @ref)) @ref = null;
        selecionado = @ref;
        ultimoCampo = @ref is null ? null : campo;
        s.Selecao = @ref;
        MarcarSelecionadoNaLista();
        AtualizarPropriedades();
        AtualizarComandos();
    }

    /// <summary>Botão direito sobre um item: Situação ▸, Editar texto, Excluir/Restaurar, Original ⇄, Propriedades.</summary>
    void MostrarMenuContexto(Point ponto)
    {
        if (LinhaSelecionada is { } linha) { MostrarMenuContextoLinha(linha, ponto); return; }
        if (ItemSelecionado is not { } it) return;
        var menu = new MenuFlyout();
        var situacao = new MenuFlyoutSubItem { Text = "Situação", Icon = new FontIcon { Glyph = Glifos.Situacao }, IsEnabled = Faixa.Habilitado(ComandosApp.InicioSituacao) };
        var efetiva = SituacaoEfetiva(it);
        foreach (var c in Faixas.OrdemSituacoes)
        {
            var id = ComandosApp.ComandoDaSituacao(c);
            var i = new ToggleMenuFlyoutItem { Text = Situacoes.Sit[c].Texto, IsChecked = c == efetiva, IsEnabled = Faixa.Habilitado(id) };
            i.Click += async (_, _) => await ExecutarComandoAsync(id);
            situacao.Items.Add(i);
        }
        menu.Items.Add(situacao);
        menu.Items.Add(ItemDoMenu("Editar texto", Glifos.Editar, ComandosApp.InicioEditarTexto));
        menu.Items.Add(ItemDoMenu(it.Incluido ? "Excluir item" : "Restaurar item", it.Incluido ? Glifos.Excluir : Glifos.Restaurar, ComandosApp.InicioExcluirRestaurar));
        menu.Items.Add(ItemDoMenu("Novo item abaixo", Glifos.NovoItem, ComandosApp.InicioNovoItem));
        var original = new ToggleMenuFlyoutItem { Text = "Original", IsChecked = mostrarOriginal, IsEnabled = Faixa.Habilitado(ComandosApp.InicioOriginal) };
        original.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.InicioOriginal);
        menu.Items.Add(original);
        var propriedades = new MenuFlyoutItem { Text = "Propriedades", Icon = new FontIcon { Glyph = Glifos.Propriedades } };
        propriedades.Click += (_, _) => MostrarPropriedades(true);
        menu.Items.Add(propriedades);
        UltimoMenuContexto = menu;
        menu.ShowAt(R.Navegador, new FlyoutShowOptions { Position = ponto });
    }

    /// <summary>Clique na etiqueta de situação de um item da lista de seções (correções 2026-11-15): seleciona o item e abre, junto da
    /// etiqueta, o menu das situações (a atual marcada). A escolha segue o mesmo caminho da faixa e do botão direito.</summary>
    async Task MostrarMenuDaEtiquetaAsync(ItemNavegacao item, FrameworkElement alvo)
    {
        if (item.Ref is not { } id || sessao?.Revisao.Projeto.Item(id) is null) return;
        await NaFilaAsync(() => TentarAsync(() => SelecionarAsync(id)));
        if (ItemSelecionado is not { } it || it.Id != id) return;
        var menu = new MenuFlyout { Placement = FlyoutPlacementMode.BottomEdgeAlignedRight };
        var efetiva = SituacaoEfetiva(it);
        foreach (var c in Faixas.OrdemSituacoes)
        {
            var comando = ComandosApp.ComandoDaSituacao(c);
            var i = new ToggleMenuFlyoutItem { Text = Situacoes.Sit[c].Texto, IsChecked = c == efetiva, IsEnabled = Faixa.Habilitado(comando) };
            var codigo = c;
            i.Click += async (_, _) => await MudarSituacaoPelaEtiquetaAsync(id, codigo);
            menu.Items.Add(i);
        }
        UltimoMenuEtiqueta = menu;
        menu.ShowAt(alvo);
    }

    /// <summary>A situação escolhida no menu da etiqueta: o comando da faixa (<see cref="DefinirSituacao"/>, com histórico e desfazer).
    /// Quando a situação muda e falta a justificativa (obrigatória), as Propriedades se abrem com o foco na caixa da justificativa.</summary>
    internal async Task MudarSituacaoPelaEtiquetaAsync(string itemId, string codigo)
    {
        if (selecionado != itemId) await NaFilaAsync(() => TentarAsync(() => SelecionarAsync(itemId)));
        if (ItemSelecionado is not { } it || it.Id != itemId || SituacaoEfetiva(it) == codigo) return;
        await ExecutarComandoAsync(ComandosApp.ComandoDaSituacao(codigo));
        if (ItemSelecionado is { } depois && depois.Id == itemId && depois.Situacao.Revisada is not null
            && string.IsNullOrWhiteSpace(depois.Situacao.Justificativa) && R.PropJustificativa.IsEnabled)
        {
            MostrarPropriedades(true);
            R.PropJustificativa.Focus(FocusState.Programmatic);
        }
    }


    /// <summary>Botão direito sobre uma linha de tabela: Situação ▸ (a mudança pede a justificativa nas Propriedades, obrigatória para
    /// exportar, como nos itens), Editar texto, Restaurar original, Original ⇄, Propriedades. A linha não se exclui.</summary>
    void MostrarMenuContextoLinha(string linha, Point ponto)
    {
        var menu = new MenuFlyout();
        var situacao = new MenuFlyoutSubItem { Text = "Situação", Icon = new FontIcon { Glyph = Glifos.Situacao }, IsEnabled = Faixa.Habilitado(ComandosApp.InicioSituacao) };
        var efetiva = TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(sessao!.Revisao.Projeto, linha));
        foreach (var c in Faixas.OrdemSituacoes)
        {
            var id = ComandosApp.ComandoDaSituacao(c);
            var i = new ToggleMenuFlyoutItem { Text = Situacoes.Sit[c].Texto, IsChecked = c == efetiva, IsEnabled = Faixa.Habilitado(id) };
            i.Click += async (_, _) =>
            {
                await ExecutarComandoAsync(id);
                PedirJustificativaSeFaltar(linha);
            };
            situacao.Items.Add(i);
        }
        menu.Items.Add(situacao);
        menu.Items.Add(ItemDoMenu("Editar texto", Glifos.Editar, ComandosApp.InicioEditarTexto));
        menu.Items.Add(ItemDoMenu("Restaurar original", Glifos.Restaurar, ComandosApp.InicioRestaurarOriginal));
        var original = new ToggleMenuFlyoutItem { Text = "Original", IsChecked = mostrarOriginal, IsEnabled = Faixa.Habilitado(ComandosApp.InicioOriginal) };
        original.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.InicioOriginal);
        menu.Items.Add(original);
        var propriedades = new MenuFlyoutItem { Text = "Propriedades", Icon = new FontIcon { Glyph = Glifos.Propriedades } };
        propriedades.Click += (_, _) => MostrarPropriedades(true);
        menu.Items.Add(propriedades);
        UltimoMenuContexto = menu;
        menu.ShowAt(R.Navegador, new FlyoutShowOptions { Position = ponto });
    }

    /// <summary>A situação da linha mudou e falta a justificativa (obrigatória para exportar): abre as Propriedades com o foco nela.</summary>
    void PedirJustificativaSeFaltar(string linha)
    {
        if (sessao?.Revisao.Projeto is not { } p || !p.Linhas.TryGetValue(linha, out var r) || !TabelasRevisao.FaltaJustificativa(r)) return;
        if (!PropriedadesVisivel) MostrarPropriedades(true);
        R.PropJustificativa.Focus(FocusState.Programmatic);
    }

    MenuFlyoutItem ItemDoMenu(string texto, string glifo, string id)
    {
        var i = new MenuFlyoutItem { Text = texto, Icon = new FontIcon { Glyph = glifo }, IsEnabled = Faixa.Habilitado(id) };
        if (Atalhos.Tecla(id) is { } t) i.KeyboardAcceleratorTextOverride = Atalhos.Legivel(t);
        i.Click += async (_, _) => await ExecutarComandoAsync(id);
        return i;
    }

    /// <summary>Clique na marca "ITEM EXCLUÍDO NN": o título do item excluído e Restaurar.</summary>
    void MostrarFlyoutExcluido(string id, Point ponto)
    {
        if (sessao?.Revisao.Projeto is not { } p || p.Item(id) is not { Incluido: false } it) return;
        var painel = new StackPanel { Spacing = 8, MaxWidth = 300 };
        painel.Children.Add(new TextBlock { Text = it.Criado ? "Item novo excluído" : $"Item {it.NumeroOriginal:00} excluído", Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
        painel.Children.Add(new TextBlock { Text = DadosEfetivos.TextoEfetivo(p, it, "titulo") ?? "(sem título)", TextWrapping = TextWrapping.Wrap });
        var restaurar = new Button { Content = "Restaurar", IsEnabled = Editavel };
        Dicas.Aplicar(restaurar, "Restaurar", "Traz este item excluído de volta para o relatório.");
        var flyout = new Flyout { Content = painel };
        restaurar.Click += async (_, _) => { flyout.Hide(); await TentarAsync(() => AplicarAsync(new Restaurar(id))); };
        painel.Children.Add(restaurar);
        UltimoFlyoutExcluido = flyout;
        flyout.ShowAt(R.Navegador, new FlyoutShowOptions { Position = ponto });
        _ = NaFilaAsync(() => TentarAsync(() => SelecionarAsync(id)));
    }

    // ---------- Revisão e Exibir ----------

    async Task AlternarMarcasAsync()
    {
        ajustes.MarcasRevisao = !ajustes.MarcasRevisao;
        SalvarAjustes();
        AtualizarComandos();
        SincronizarAjustes();   // a chave da página Ajustes acompanha a faixa (o primeiro clique nela volta a valer)
        if (sessao is { Atual: not null } s) await s.EnviarEstadoAsync();
    }

    void MostrarPropriedades(bool visivel)
    {
        propriedadesRecolhidasSozinhas = false; // a escolha do usuário manda
        propriedadesPedidasSemSelecao = visivel;
        R.PainelPropriedades.Visibility = visivel ? Visibility.Visible : Visibility.Collapsed;
        Faixa.Ligar(ComandosApp.ExibirPropriedades, visivel);
        LembrarPaineis();
    }

    /// <summary>A Conferência: erros finais (bloqueiam a exportação, inclusive referências quebradas e situações contraditórias) e citações
    /// literais que a numeração pode ter mudado (só avisam).</summary>
    internal string TextoConferencia()
    {
        if (sessao?.Atual is not { } r) return "Nenhum relatório aberto.";
        var p = sessao.Revisao.Projeto;
        var erros = r.Preparado.ErrosFinais.Distinct().ToList();
        // Tarefa 16: as citações a itens excluídos agora estão nos erros (ReferenciasCruzadas); aqui ficam só os avisos.
        var avisos = ConferenciaRevisao.Avisos(p).Distinct().ToList();
        var sb = new StringBuilder();
        if (erros.Count == 0 && avisos.Count == 0) return "Nada a conferir: o relatório pode ser exportado.";
        sb.AppendLine(erros.Count == 0 ? "Nenhum erro: o relatório pode ser exportado." : $"Erros que bloqueiam a exportação ({erros.Count}):");
        foreach (var e in erros) sb.AppendLine("• " + e);
        if (avisos.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Avisos ({avisos.Count}):");
            foreach (var a in avisos) sb.AppendLine("• " + a);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>F6: faixa → painel lateral → folhas → Propriedades (só as regiões visíveis), a partir de onde o foco está.</summary>
    async Task AlternarRegiaoAsync()
    {
        var regioes = new List<FrameworkElement> { Faixa };
        if (PainelVisivel) regioes.Add(R.PainelNavegacao);
        if (sessao?.Atual is not null) regioes.Add(R.Navegador);
        if (PropriedadesVisivel) regioes.Add(R.PainelPropriedades);
        var foco = Raiz.XamlRoot is null ? null : FocusManager.GetFocusedElement(Raiz.XamlRoot) as DependencyObject;
        var atual = regioes.FindIndex(r => Contem(r, foco));
        var proxima = regioes[(atual + 1) % regioes.Count];
        if (ReferenceEquals(proxima, R.Navegador)) { R.Navegador.Focus(FocusState.Keyboard); return; }
        if (FocusManager.FindFirstFocusableElement(proxima) is { } alvo) await FocusManager.TryFocusAsync(alvo, FocusState.Keyboard);
    }

    static bool Contem(DependencyObject regiao, DependencyObject? e)
    {
        for (; e is not null; e = VisualTreeHelper.GetParent(e)) if (ReferenceEquals(e, regiao)) return true;
        return false;
    }

    /// <summary>Caixa de texto com o foco: Delete, Ctrl+Z e Ctrl+Y são dela; Alt+↑/↓ numa lista de opções também.</summary>
    bool TeclaDoControleComFoco(string id)
    {
        var foco = Raiz.XamlRoot is null ? null : FocusManager.GetFocusedElement(Raiz.XamlRoot);
        if (foco is TextBox or PasswordBox or RichEditBox or AutoSuggestBox)
            return id is ComandosApp.InicioExcluirRestaurar or ComandosApp.EditarDesfazer or ComandosApp.EditarRefazer
                or ComandosApp.LegislacaoCopiar or ComandosApp.LegislacaoCopiarCitacao;
        if (foco is ComboBox) return id is ComandosApp.InicioItemAnterior or ComandosApp.InicioItemProximo;
        return false;
    }

    // ---------- painéis ----------

    void AtualizarPaineis()
    {
        if (fechada) return;
        MontarSecoes();
        MontarExcluidos();
        MontarHistorico();
        AtualizarPropriedades();
    }

    void LimparPaineis()
    {
        selecionado = ultimoCampo = itemNasPropriedades = figuraSelecionada = null;
        mostrarOriginal = false;
        navegacaoCompleta = [];
        AtualizarPaineis();
    }

    /// <summary>A lista de seções e itens, com situação e "alterado", passada pelo filtro do painel (<see cref="FiltroNavegacao"/>):
    /// filtro rápido por situação, pesquisa, Somente alterados e seções recolhidas (com o contador de itens de cada seção).</summary>
    void MontarSecoes()
    {
        var p = sessao?.Revisao.Projeto;
        foreach (var i in navegacaoCompleta)
        {
            if (i.Ref is not null && !i.Secao && p?.Item(i.Ref) is { } it)
            {
                i.Situacao = SituacaoEfetiva(it);
                i.Alterado = TemRevisao(it);
            }
        }
        var entradas = navegacaoCompleta.Select(i => new EntradaNavegacao(i.Rotulo, i.Secao, i.Situacao, i.Alterado)).ToList();
        var r = FiltroNavegacao.Aplicar(entradas, filtroSituacao, R.CaixaFiltroSecoes.Text, somenteAlterados, secoesRecolhidas);
        itensNavegacao.Clear();
        var totalItensVisiveis = 0;
        foreach (var k in r.Visiveis)
        {
            var i = navegacaoCompleta[k];
            if (i.Secao)
            {
                i.Contagem = r.ItensPorSecao.TryGetValue(k, out var n) ? n : 0;
                i.Recolhida = secoesRecolhidas.Contains(i.Rotulo);
            }
            i.Selecionado = i.Ref is not null && i.Ref == selecionado;
            i.OrdemNoFiltro = i.Situacao is null ? 0 : ++totalItensVisiveis;
            itensNavegacao.Add(i);
        }
        R.TotalItensFiltrados = totalItensVisiveis;
        AtualizarBotoesFiltro(r.ItensPorFiltro);
        if (folhaAtual > 0) MarcarItemAtual(folhaAtual);
    }

    /// <summary>Marca na lista o item selecionado (fundo mais claro e barra fina), sem remontar a lista.</summary>
    void MarcarSelecionadoNaLista()
    {
        foreach (var i in navegacaoCompleta) i.Selecionado = i.Ref is not null && i.Ref == selecionado;
    }

    /// <summary>Os botões do filtro rápido (Todos, Não atende, Falta comprovar, Divergentes, Atende), criados uma vez.</summary>
    void MontarBotoesFiltro()
    {
        R.BotoesFiltro.Children.Clear();
        FiltroSituacao[] ordemVisual = [FiltroSituacao.Todos, FiltroSituacao.Atende, FiltroSituacao.NaoAtende, FiltroSituacao.Divergentes, FiltroSituacao.FaltaComprovar];
        for (int i = 0; i < ordemVisual.Length; i++)
        {
            var f = ordemVisual[i];
            var b = new ToggleButton { Tag = f, IsChecked = f == filtroSituacao };
            Pilulas.Estilizar(b); // o filtro escolhido acende na cor de destaque, como no WinStatusBar
            // Mais compactos (2026-11-17): os cinco cabem em duas linhas no painel, sem sobra solta numa terceira.
            Grid.SetRow(b, i / 3);
            Grid.SetColumn(b, i < 3 ? i * 2 : (i - 3) * 3);
            Grid.SetColumnSpan(b, i < 3 ? 2 : 3);
            b.HorizontalAlignment = HorizontalAlignment.Stretch;
            b.Height = 34;
            b.Padding = new Thickness(3, 0, 3, 0);
            b.CornerRadius = new CornerRadius(8);
            b.ActualThemeChanged += (_, _) => AtualizarBotoesFiltro(contagensFiltro);
            var filtro = f;
            b.Click += (_, _) =>
            {
                filtroSituacao = filtro;
                MontarSecoes();
            };
            R.BotoesFiltro.Children.Add(b);
        }
        AtualizarBotoesFiltro(FiltroNavegacao.Ordem.ToDictionary(f => f, _ => 0));
    }

    IReadOnlyDictionary<FiltroSituacao, int> contagensFiltro = new Dictionary<FiltroSituacao, int>();

    /// <summary>Cada botão: bolinha na cor da situação (a mesma da etiqueta do relatório), o nome e a contagem em segundo plano — dá para
    /// ler a situação pela cor antes do texto (2026-11-17: "essa apresentação visual precisa melhorar").</summary>
    void AtualizarBotoesFiltro(IReadOnlyDictionary<FiltroSituacao, int> contagens)
    {
        contagensFiltro = contagens;
        foreach (var b in R.BotoesFiltro.Children.OfType<ToggleButton>())
        {
            if (b.Tag is not FiltroSituacao f) continue;
            var n = contagens.TryGetValue(f, out var c) ? c : 0;
            var ligado = f == filtroSituacao;
            var conteudo = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            if (ligado && f == FiltroSituacao.Todos) conteudo.Children.Add(new FontIcon { Glyph = "\uE73E", FontSize = 12 });
            if (EtiquetasSituacao.CoresDaClasse(FiltroNavegacao.Classe(f), b.ActualTheme == ElementTheme.Dark) is { } cores)
                conteudo.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
                {
                    Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center,
                    Fill = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb((byte)(cores.Ponto >> 24), (byte)(cores.Ponto >> 16), (byte)(cores.Ponto >> 8), (byte)cores.Ponto)),
                });
            conteudo.Children.Add(new TextBlock { Text = FiltroNavegacao.Rotulo(f), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center });
            if (n > 0) conteudo.Children.Add(new TextBlock { Text = n.ToString(CultureInfo.InvariantCulture), FontSize = 12.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Opacity = ligado ? 1 : .7, VerticalAlignment = VerticalAlignment.Center });
            b.Content = conteudo;
            b.IsChecked = ligado;
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, $"Mostrar {FiltroNavegacao.Rotulo(f).ToLowerInvariant()}, {n} itens");
            var dica = DicasComandos.Livre(FiltroNavegacao.Rotulo(f), DicasComandos.DescricaoFiltro(f));
            if (Dicas.De(b) != dica) Dicas.Aplicar(b, dica);
        }
    }

    void MontarExcluidos()
    {
        var p = sessao?.Revisao.Projeto;
        var excluidos = p is null ? [] : p.Itens.Where(i => !i.Incluido).OrderBy(i => i.Ordem).ToList();
        R.AbaExcluidos.Text = $"Excluídos ({excluidos.Count})";
        R.ListaExcluidos.Items.Clear();
        foreach (var x in excluidos)
        {
            var linha = new Grid { Tag = x.Id, ColumnSpacing = 8, MinHeight = 32 };
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var numero = x.Criado ? "novo" : x.NumeroOriginal.ToString("00", CultureInfo.InvariantCulture);
            var tituloExcluido = $"{numero}  {DadosEfetivos.TextoEfetivo(p!, x, "titulo") ?? ""}";
            var texto = new TextBlock { Text = tituloExcluido, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            ToolTipService.SetToolTip(texto, tituloExcluido); // M11: o título inteiro na dica
            linha.Children.Add(texto);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(linha, $"Item excluído {numero}, {DadosEfetivos.TextoEfetivo(p!, x, "titulo") ?? ""}");
            // B6: botão pequeno com borda (sem borda, parecia texto).
            var restaurar = new Button { Content = "Restaurar", IsEnabled = Editavel, Padding = new Thickness(8, 2, 8, 3), VerticalAlignment = VerticalAlignment.Center };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(restaurar, $"Restaurar o item {numero}");
            Dicas.Aplicar(restaurar, "Restaurar", "Traz este item excluído de volta para o relatório.");
            var id = x.Id;
            restaurar.Click += async (_, _) => await TentarAsync(() => AplicarAsync(new Restaurar(id)));
            Grid.SetColumn(restaurar, 1);
            linha.Children.Add(restaurar);
            R.ListaExcluidos.Items.Add(linha);
        }
        MostrarAbaDoPainel();
    }

    void MontarHistorico()
    {
        if (sessao is not { } s) { R.ListaHistorico.ItemsSource = null; return; }
        var p = s.Revisao.Projeto;
        R.ListaHistorico.ItemsSource = s.Revisao.HistoricoCompleto.Reverse().Select(r => $"{Hora(r.DataUtc)} · {r.Usuario} · {DescricaoHistorico.Texto(r, p)}").ToList();
    }

    void MostrarAbaDoPainel()
    {
        var aba = R.AbasPainel.SelectedItem;
        R.ListaNavegacao.Visibility = ReferenceEquals(aba, R.AbaSecoes) || aba is null ? Visibility.Visible : Visibility.Collapsed;
        R.PainelFiltros.Visibility = R.ListaNavegacao.Visibility;
        var excl = ReferenceEquals(aba, R.AbaExcluidos);
        R.ListaExcluidos.Visibility = excl && R.ListaExcluidos.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        R.TextoSemExcluidos.Visibility = excl && R.ListaExcluidos.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        R.ListaHistorico.Visibility = ReferenceEquals(aba, R.AbaHistorico) ? Visibility.Visible : Visibility.Collapsed;
    }

    async Task AplicarJustificativaAsync()
    {
        if (atualizandoPropriedades || itemNasPropriedades is not { } id || sessao?.Revisao.Projeto is not { } p) return;
        var texto = R.PropJustificativa.Text;
        if (TabelasRevisao.EhLinha(id))
        {
            if (!TabelasRevisao.Existe(p, id) || TextoEditado.Normalizar(texto) == TextoEditado.Normalizar(TabelasRevisao.Justificativa(p, id) ?? "")) return;
            await TentarAsync(() => AplicarAsync(new DefinirJustificativa(id, texto)));
            return;
        }
        if (p.Item(id) is not { } it) return;
        if (TextoEditado.Normalizar(texto) == TextoEditado.Normalizar(it.Situacao.Justificativa ?? "")) return;
        await TentarAsync(() => AplicarAsync(new DefinirJustificativa(id, texto)));
    }

    // ---------- IA na revisão (ajustar o item, rever a tabela, volumetria) ----------

    bool ajustandoComIa;
    CancellationTokenSource? cancelamentoIa;
    DispatcherTimer? relogioIa;
    readonly Stopwatch tempoIa = new();
    string descricaoIa = "", etapaIa = "";
    /// <summary>Itens (id + justificativa) que a IA já conferiu sem mudar nada: o aviso "a justificativa mudou" não insiste.</summary>
    readonly HashSet<string> conferidosPelaIa = new(StringComparer.Ordinal);

    // ---------- acessores do roteiro ----------
    internal bool AjustandoComIa => ajustandoComIa;
    internal bool BotaoDaIaVisivel => R.PropAjustarTexto.Visibility == Visibility.Visible;
    internal string RotuloDoBotaoDaIa => R.PropAjustarRotulo.Text;
    internal bool AvisoDeReajusteVisivel => R.PropAjustarAviso.Visibility == Visibility.Visible;

    /// <summary>Uma pergunta da revisão à IA. Só vale entre <see cref="IniciarEsperaIa"/> e <see cref="TerminarEsperaIa"/> (o Cancelar a interrompe).</summary>
    async Task<string> PerguntarNaRevisaoAsync(string prompt)
    {
        var ct = cancelamentoIa?.Token ?? CancellationToken.None;
        var pecas = await LerPecasAsync();
        // A IA, o modelo e o nível de Ajustes › Inteligência artificial › Modelos de IA (2026-11-17; padrão: o Codex com Terra 6.1, baixo),
        // sem abrir arquivo nenhum: o pedido leva tudo.
        var ia = PrepararIa(pecas, ModeloIaAjuste.Limite);
        descricaoIa = ia.Descricao;
        AtualizarEsperaIa();
        ct.ThrowIfCancellationRequested();
        return await IaDespacho.PerguntarAsync(ia.Claude, ia.Codex, prompt, ct, null, null, ia.Opcoes);
    }

    /// <summary>Começa a espera: o botão da IA desabilita, aparecem o anel, o tempo ("Consultando a IA (Terra 6.1, esforço baixo)… 0:07")
    /// e o Cancelar.</summary>
    void IniciarEsperaIa()
    {
        ajustandoComIa = true;
        cancelamentoIa = new CancellationTokenSource();
        descricaoIa = etapaIa = "";
        tempoIa.Restart();
        if (relogioIa is null)
        {
            relogioIa = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            relogioIa.Tick += (_, _) => AtualizarEsperaIa();
        }
        relogioIa.Start();
        AtualizarEsperaIa();
    }

    void TerminarEsperaIa()
    {
        ajustandoComIa = false;
        relogioIa?.Stop();
        tempoIa.Stop();
        cancelamentoIa?.Dispose();
        cancelamentoIa = null;
        AtualizarEsperaIa();
        AtualizarPropriedades();
    }

    void AtualizarEsperaIa()
    {
        if (fechada) return;
        var vis = ajustandoComIa ? Visibility.Visible : Visibility.Collapsed;
        R.PropAjustarProgresso.Visibility = vis;
        R.PropAjustarCancelar.Visibility = vis;
        R.PropAjustarCancelar.IsEnabled = cancelamentoIa is { IsCancellationRequested: false };
        R.PropAjustarAnel.IsActive = ajustandoComIa;
        if (ajustandoComIa) R.PropAjustarTexto.IsEnabled = R.PropReverComIa.IsEnabled = false;
        R.PropAjustarEstado.Text = !ajustandoComIa ? ""
            : (etapaIa.Length > 0 ? etapaIa : "Consultando a IA") + (descricaoIa.Length > 0 ? " (" + descricaoIa + ")" : "") + "… " + ModeloIaAjuste.Tempo(tempoIa.Elapsed);
    }

    /// <summary>O Cancelar da espera: encerra o programa da IA; nada muda no relatório.</summary>
    internal void CancelarIa()
    {
        try { cancelamentoIa?.Cancel(); } catch (ObjectDisposedException) { }
        AtualizarEsperaIa();
    }

    void MostrarErroDaIa(Exception e)
    {
        if (e is OperationCanceledException)
        {
            MostrarMensagem(InfoBarSeverity.Informational, "Consulta à IA cancelada", "Nada mudou no item.");
            return;
        }
        // Auditoria 2026-11-16: nenhum erro da IA (JSON, tempo, rede, processo) derruba o editor; o detalhe vai para o registro.
        RegistroErros.Gravar("ia", e);
        MostrarMensagem(InfoBarSeverity.Error, "A IA não respondeu", e is InvalidOperationException or IOException ? e.Message : "A chamada à IA falhou: " + Erro(e));
    }

    /// <summary>O aviso "A justificativa mudou: reajuste o item" (<see cref="AjustePendente"/>): só avisa; quem dispara a IA é o botão.</summary>
    bool ReajustePendente(Projeto p, string id, string? justificativa) =>
        sessao is { } s && AjustePendente.JustificativaMudou(p, s.Revisao.HistoricoCompleto, id)
        && !conferidosPelaIa.Contains(id + "\n" + TextoEditado.Normalizar(justificativa ?? ""));

    /// <summary>"Ajustar o item com a IA": grava a justificativa digitada e manda à IA o item, a situação e a justificativa (com ou sem
    /// mudança de situação). A IA reajusta o item inteiro, com cada campo no tamanho do original, e a resposta é conferida
    /// (<see cref="IaAjusteItem.Problemas"/>): grande demais ou narrando a revisão, volta uma vez para encurtar; se insistir, nada entra.
    /// O que entra é UMA alteração (um Desfazer): os campos e, se o revisor não fixou a situação e a IA a mudou, a situação.</summary>
    internal async Task AjustarTextoComIaAsync()
    {
        if (ajustandoComIa || itemNasPropriedades is not { } id) return;
        if (TabelasRevisao.EhLinha(id))
        {
            // Tarefa 15 (correção 1): o botão principal de uma tabela de índices ou de esquadrias é sempre o "Rever" sem IA (conta do
            // editor); a IA fica na segunda opção (ReverComIaAsync). No diagrama, sem conta, o "Rever" é com a IA.
            await AplicarJustificativaAsync();
            if (RecalculoLinha.ReveSemIa(id)) await ReverSemIaAsync(id);
            else await RecalcularTabelaComIaAsync(id);
            return;
        }
        await AplicarJustificativaAsync();
        if (sessao is not { Revisao.Projeto: { } p } s || p.Item(id) is not { Incluido: true } it || !Editavel) return;
        var justificativa = it.Situacao.Justificativa ?? "";
        if (justificativa.Trim().Length == 0)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Ajustar com a IA", "Escreva a justificativa antes: é dela que a IA parte.");
            R.PropJustificativa.Focus(FocusState.Programmatic);
            return;
        }
        var original = it.Situacao.Original is JsonValue v && v.TryGetValue<string>(out var so) ? so : null;
        var situacao = SituacaoEfetiva(it) ?? "NAO_COMPROVADO";
        var fixa = it.Situacao.Revisada is not null;
        var campos = Campos.Editaveis.ToDictionary(c => c, c => DadosEfetivos.TextoEfetivo(p, it, c) ?? "");
        var originais = Campos.Editaveis.ToDictionary(c => c, c =>
            it.Campos.TryGetValue(c, out var ct) && ct.Original is JsonValue ov && ov.TryGetValue<string>(out var os)
                ? Citacoes.Imprimir(os, x => p.NumeroAtual(x), x => p.Item(x)?.NumeroOriginal) : "");
        var limites = IaAjusteItem.Limites(originais, campos);
        var prompt = IaAjusteItem.Prompt(original, situacao, fixa, justificativa, campos, limites);

        IniciarEsperaIa();
        try
        {
            var novos = IaAjusteItem.Ler(await PerguntarNaRevisaoAsync(prompt));
            if (novos is { Count: > 0 } && IaAjusteItem.Problemas(novos, limites) is { Count: > 0 } recusados)
            {
                // Grande demais ou contando a história da revisão: uma segunda chance, dizendo o que foi recusado.
                etapaIa = "Pedindo um texto mais curto";
                novos = IaAjusteItem.Ler(await PerguntarNaRevisaoAsync(IaAjusteItem.PromptCorrecao(prompt, novos, recusados)));
            }
            if (!ReferenceEquals(sessao, s) || p.Item(id) is not { } agora) return;
            if (novos is not { Count: > 0 })
            {
                MostrarMensagem(InfoBarSeverity.Warning, "Ajustar com a IA", "A resposta da IA não veio no formato esperado; nada mudou.");
                return;
            }
            if (IaAjusteItem.Problemas(novos, limites) is { Count: > 0 } problemas)
            {
                MostrarMensagem(InfoBarSeverity.Warning, "Ajustar com a IA",
                    "A IA não manteve o texto curto como o original, mesmo depois de um segundo pedido; nada mudou. " + string.Join("; ", problemas.Take(4)) + ". Tente de novo ou edite o texto à mão.");
                return;
            }
            var comandos = new List<Comando>();
            var ignorados = 0;
            var sugerida = novos.TryGetValue("situacao", out var sit) && sit != situacao ? sit : null;
            if (sugerida is not null && !fixa && SituacaoEfetiva(agora) == situacao) comandos.Add(new DefinirSituacao(id, sugerida));
            foreach (var campo in Campos.Editaveis)
            {
                if (!novos.TryGetValue(campo, out var texto) || texto.Trim().Length == 0) continue;
                if (TextoEditado.Normalizar(texto) == TextoEditado.Normalizar(campos[campo])) continue;
                if ((DadosEfetivos.TextoEfetivo(p, agora, campo) ?? "") != campos[campo]) { ignorados++; continue; } // editado durante a espera
                comandos.Add(new DefinirCampo(id, campo, texto));
            }
            if (comandos.Count == 0)
            {
                conferidosPelaIa.Add(id + "\n" + TextoEditado.Normalizar(justificativa));
                MostrarMensagem(InfoBarSeverity.Success, "Ajustar com a IA", "A IA manteve o texto: nada mudou.");
                return;
            }
            await TentarAsync(() => AplicarAsync(new Lote(id, comandos, "Ajustado pela IA")));
            if (ultimosErros.Count > 0) return; // recusado pela conferência: o aviso já está na tela
            var textos = comandos.Count(c => c is DefinirCampo);
            MostrarMensagem(InfoBarSeverity.Success, "Ajustar com a IA",
                $"A IA reajustou o item ({textos} {(textos == 1 ? "campo" : "campos")}"
                + (comandos.Any(c => c is DefinirSituacao) ? $"; situação: {TextoSituacao(sugerida)}" : "") + "). Confira; um Desfazer volta tudo."
                + (sugerida is not null && fixa ? $" A IA sugeriu {TextoSituacao(sugerida)}, mas a situação que você escolheu foi mantida." : "")
                + (ignorados > 0 ? $" {ignorados} {(ignorados == 1 ? "campo editado" : "campos editados")} por você durante a espera {(ignorados == 1 ? "ficou" : "ficaram")} como estava." : ""));
        }
        catch (Exception e) { MostrarErroDaIa(e); }
        finally { TerminarEsperaIa(); }
    }

    /// <summary>Linha de tabela: a IA refaz as contas das linhas do mesmo bloco a partir da linha selecionada (editada ou não) e da
    /// justificativa, e devolve textos e situações; tudo entra como UMA alteração (um Desfazer), com a justificativa que a IA deu quando
    /// a situação de outra linha muda.</summary>
    internal async Task RecalcularTabelaComIaAsync(string linhaEditada)
    {
        if (ajustandoComIa) return;
        await AplicarJustificativaAsync();
        if (sessao is not { Revisao.Projeto: { } p } s || !Editavel || !TabelasRevisao.TentarLer(linhaEditada, out var bloco, out _)) return;
        var linhas = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var blocosJuntos = bloco == TabelasRevisao.Indices
            ? new[] { TabelasRevisao.Indices, TabelasRevisao.Chamadas, TabelasRevisao.Planta, TabelasRevisao.Volumetria }
            : new[] { bloco };
        foreach (var id in TabelasRevisao.Ids(p).Where(i => TabelasRevisao.TentarLer(i, out var b, out _) && blocosJuntos.Contains(b)))
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var c in TabelasRevisao.Caminhos(TabelasRevisao.Original(p, id)!)) d[c] = TabelasRevisao.TextoEfetivo(p, id, c) ?? "";
            d["situacao"] = TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(p, id)) ?? "";
            linhas[id] = d;
        }
        var editados = p.Linhas.TryGetValue(linhaEditada, out var rev) ? rev.Campos.Keys.ToList() : [];
        var prompt = IaRecalculoIndices.Prompt(bloco, linhaEditada, linhas, TabelasRevisao.Justificativa(p, linhaEditada), editados);

        IniciarEsperaIa();
        try
        {
            var saida = await PerguntarNaRevisaoAsync(prompt);
            if (!ReferenceEquals(sessao, s)) return;
            if (IaRecalculoIndices.Ler(saida, linhas.Keys) is not { } novas)
            {
                MostrarMensagem(InfoBarSeverity.Warning, "Rever com a IA", "A resposta da IA não veio no formato esperado; nada mudou.");
                return;
            }
            var novosVaos = IaRecalculoIndices.LerNovosVaos(saida, linhas.Keys);
            var ambientes = bloco == TabelasRevisao.Ambientes;
            var comandos = new List<Comando>();
            var trabalho = p;            // o projeto como fica com o que já foi aceito (cópias de Testar; o da sessão só muda no fim)
            int mudou = 0, ignoradas = 0, acrescentadas = 0, contas = 0;
            foreach (var id in linhas.Keys)
            {
                novas.TryGetValue(id, out var campos);
                novosVaos.TryGetValue(id, out var vaos);
                if (campos is null && vaos is null) continue;
                var antes = linhas[id];
                var daLinha = new List<Comando>();
                var textos = 0;
                // Esquadrias pedidas na instrução ("adicionar a porta de 80x210"): entram com a área calculada pelo editor.
                if (ambientes && vaos is not null)
                    foreach (var (codigo, tipo, medidas) in vaos)
                    {
                        try
                        {
                            var conta = Esquadrias.Calcular(trabalho, id);
                            var categoria = Esquadrias.Categoria(codigo, tipo.Length > 0 ? tipo : "Porta");
                            var usados = (conta?.Esquadrias ?? []).Select(e => e.Codigo)
                                .Concat(daLinha.OfType<AcrescentarVao>().Select(v => v.Codigo)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                            var cod = codigo.Length > 0 && !usados.Contains(codigo) ? codigo : ProximoCodigoLivre(usados, categoria);
                            daLinha.Add(Esquadrias.Nova(id, cod, tipo.Length > 0 ? tipo : categoria == CategoriaEsquadria.Janela ? "Janela" : "Porta", medidas));
                            acrescentadas++;
                        }
                        catch (ComandoRecusadoException) { ignoradas++; }
                    }
                string? situacaoDaIa = null, motivo = null;
                foreach (var (caminho, texto) in campos ?? [])
                {
                    if (caminho == "situacao") { situacaoDaIa = texto; continue; }
                    if (caminho == "justificativa") { motivo = texto; continue; }
                    if (!antes.ContainsKey(caminho) || texto.Trim().Length == 0) continue;
                    // No ambiente, a área de cada vão e o vão total são conta do editor, não da IA.
                    if (ambientes && (caminho.StartsWith("resultado/", StringComparison.Ordinal) || caminho.EndsWith("/area", StringComparison.Ordinal))) continue;
                    if (id == linhaEditada && editados.Contains(caminho)) continue; // o valor do usuário fica
                    if (TextoEditado.Normalizar(texto) == TextoEditado.Normalizar(antes[caminho])) continue;
                    // Editado pelo usuário enquanto a IA respondia: a edição dele fica.
                    if ((TabelasRevisao.TextoEfetivo(p, id, caminho) ?? "") != antes[caminho]) { ignoradas++; continue; }
                    daLinha.Add(new DefinirCampo(id, caminho, texto));
                    textos++;
                }
                var editorDecide = false;
                if (ambientes && daLinha.Count > 0)
                {
                    // A conta do ambiente (áreas, vão total, mínimo, situação) é refeita pelo editor depois das mudanças da IA.
                    try
                    {
                        var n = daLinha.Count;
                        daLinha = Esquadrias.Recalcular(trabalho, id, daLinha, "").Comandos.ToList();
                        editorDecide = Esquadrias.Calcular(trabalho, id)?.Minimo is not null;
                        if (daLinha.Count > n) contas++;
                    }
                    catch (ComandoRecusadoException) { ignoradas++; continue; }
                }
                if (!editorDecide && situacaoDaIa is not null && antes["situacao"].Length > 0 && TabelasRevisao.SituacaoDeLinha(situacaoDaIa) is { } codigoSituacao
                    && situacaoDaIa != antes["situacao"])
                {
                    daLinha.Add(new DefinirSituacao(id, codigoSituacao));
                    if (string.IsNullOrWhiteSpace(TabelasRevisao.Justificativa(p, id)))
                        daLinha.Add(new DefinirJustificativa(id, "IA: " + (motivo is { } j && j.Trim().Length > 0 ? j : "Recalculado a partir de " + TabelasRevisao.Rotulo(linhaEditada) + ".")));
                    textos++;
                }
                if (daLinha.Count == 0) continue;
                try
                {
                    trabalho = s.Revisao.Testar(new Lote(linhaEditada, [.. comandos, .. daLinha], "Revisto pela IA"));
                    comandos.AddRange(daLinha);
                    mudou += textos;
                }
                catch (ComandoRecusadoException) { ignoradas++; }
            }
            if (comandos.Count == 0)
            {
                conferidosPelaIa.Add(linhaEditada + "\n" + TextoEditado.Normalizar(TabelasRevisao.Justificativa(p, linhaEditada) ?? ""));
                MostrarMensagem(InfoBarSeverity.Success, "Rever com a IA", "A IA conferiu a tabela a partir de " + TabelasRevisao.Rotulo(linhaEditada) + ": nada mudou."
                    + (ignoradas > 0 ? $" ({ignoradas} {(ignoradas == 1 ? "sugestão não pôde" : "sugestões não puderam")} ser aplicada{(ignoradas == 1 ? "" : "s")}.)" : ""));
                return;
            }
            await TentarAsync(() => AplicarAsync(new Lote(linhaEditada, comandos, "Revisto pela IA")));
            if (ultimosErros.Count > 0) return;
            var partes = new List<string>();
            if (acrescentadas > 0) partes.Add($"{acrescentadas} {(acrescentadas == 1 ? "esquadria acrescentada" : "esquadrias acrescentadas")}");
            if (mudou > 0) partes.Add($"{mudou} {(mudou == 1 ? "texto atualizado" : "textos atualizados")}");
            if (contas > 0) partes.Add("a conta do ambiente refeita pelo editor (área, vão total, mínimo e situação)");
            MostrarMensagem(InfoBarSeverity.Success, "Rever com a IA",
                $"Revisão a partir de {TabelasRevisao.Rotulo(linhaEditada)}: {string.Join("; ", partes)}. Confira; um Desfazer volta tudo."
                + (ignoradas > 0 ? $" {ignoradas} {(ignoradas == 1 ? "mudança não foi aplicada" : "mudanças não foram aplicadas")} (texto editado por você durante a espera ou recusado pela conferência)." : ""));
        }
        catch (Exception e) { MostrarErroDaIa(e); }
        finally { TerminarEsperaIa(); }
    }

    /// <summary>Tarefa 15 (correção 1): a segunda opção "Rever com a IA" de uma tabela de índices ou de esquadrias, oferecida quando há texto
    /// novo na justificativa (<see cref="RecalculoLinha.OfereceReverComIa"/>); só roda com clique nela. A IA segue o que foi escrito.</summary>
    internal async Task ReverComIaAsync()
    {
        if (ajustandoComIa || itemNasPropriedades is not { } id || !TabelasRevisao.EhLinha(id)) return;
        await AplicarJustificativaAsync();
        await RecalcularTabelaComIaAsync(id);
    }

    /// <summary>Acessor do roteiro: a segunda opção "Rever com a IA" está à vista.</summary>
    internal bool OpcaoReverComIaVisivel => R.PropReverComIa.Visibility == Visibility.Visible;

    /// <summary>Tarefa 15: "Rever" sem IA numa tabela de índices ou de esquadrias (<see cref="RecalculoLinha.Rever"/>): as contas são
    /// refeitas pelo editor e o aviso mostra cada valor de antes e o de agora; tudo entra como UMA alteração (um Desfazer).</summary>
    internal async Task ReverSemIaAsync(string linha)
    {
        if (!Editavel || sessao is not { } s || !RecalculoLinha.ReveSemIa(linha)) return;
        await TentarAsync(() => NaFilaAsync(async () =>
        {
            if (!ReferenceEquals(sessao, s) || !TabelasRevisao.Existe(s.Revisao.Projeto, linha)) return;
            var revisao = RecalculoLinha.Rever(s.Revisao.Projeto, linha);
            if (revisao.Lote is { } lote)
            {
                await AplicarAsync(lote);
                if (ultimosErros.Count > 0) return; // recusado pela conferência: o aviso já está na tela
            }
            MostrarMensagem(InfoBarSeverity.Success, "Rever", revisao.Mensagem);
        }));
    }

    /// <summary>"P1", "P2"…, "J3": o primeiro código da categoria que ainda não está em uso no ambiente.</summary>
    static string ProximoCodigoLivre(IReadOnlySet<string> usados, CategoriaEsquadria categoria)
    {
        var letra = categoria == CategoriaEsquadria.Porta ? "P" : categoria == CategoriaEsquadria.Janela ? "J" : "V";
        for (var n = 1; ; n++)
            if (!usados.Contains(letra + n.ToString(CultureInfo.InvariantCulture))) return letra + n.ToString(CultureInfo.InvariantCulture);
    }

    // ---------- auxiliares ----------

    static string? SituacaoEfetiva(ItemRevisao it) =>
        it.Situacao.Revisada ?? (it.Situacao.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null);

    static bool TemRevisao(ItemRevisao it) => it.Situacao.Revisada is not null || it.Campos.Values.Any(c => c.Revisado is not null);

    static string TextoSituacao(string? codigo) =>
        codigo is not null && Situacoes.Sit.TryGetValue(codigo, out var t) ? t.Texto : codigo ?? "(sem situação)";

    /// <summary>"14:32" (hoje) ou "13/11 14:32", na hora local.</summary>
    static string Hora(DateTimeOffset utc)
    {
        var local = utc.ToLocalTime();
        return local.Date == DateTime.Today ? local.ToString("HH:mm", Formatos.Cultura) : local.ToString("dd/MM HH:mm", Formatos.Cultura);
    }
}
