using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Dispatching;
using Pmvni.Agentes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Uma análise recusada antes da partida (o motivo exato, em português): nada foi copiado nem iniciado.</summary>
internal sealed class AnaliseRecusadaException(string mensagem) : Exception(mensagem);

/// <summary>Uma análise em andamento (desenho §12.7): pertence à janela, não à página; trocar de módulo ou de relatório não a interrompe.</summary>
public sealed class ExecucaoEmCurso
{
    const int MaximoDeLinhas = 2000;
    readonly ObservableCollection<string> linhas = [];

    internal ExecucaoEmCurso(Provedor provedor, Processo processo, RegistroExecucao registro, DateTime inicioUtc, Pecas pecas, string? texto, OrquestradorAnalise orquestrador, bool retomada)
    {
        Provedor = provedor;
        Processo = processo;
        Registro = registro;
        InicioUtc = inicioUtc;
        Pecas = pecas;
        Texto = texto;
        Orquestrador = orquestrador;
        Retomada = retomada;
    }

    public Provedor Provedor { get; }
    public Processo Processo { get; }
    public RegistroExecucao Registro { get; }
    public DateTime InicioUtc { get; }

    /// <summary>As últimas 2.000 linhas de andamento (do Codex, ou do <c>auto-log.txt</c> do kit), na thread da janela.</summary>
    public IReadOnlyList<string> Linhas => linhas;

    internal ObservableCollection<string> LinhasObservaveis => linhas;

    /// <summary>A situação mais recente do análise pro (só Claude).</summary>
    public SituacaoKit? Situacao { get; private set; }

    /// <summary>Os eventos do kit, como o <c>situacao</c> os informou (só Claude).</summary>
    public IReadOnlyList<string> Eventos => Situacao?.Eventos ?? [];

    public Task<FimAnalise> Fim { get; internal set; } = null!;

    public Pecas Pecas { get; }
    public string? Texto { get; }
    internal OrquestradorAnalise Orquestrador { get; }

    /// <summary>O editor reaberto voltou a acompanhar uma análise do Claude que seguiu sozinha.</summary>
    public bool Retomada { get; }

    internal CancellationTokenSource Cancelamento { get; } = new();

    /// <summary>O editor deixou de acompanhar (fechou com o Claude rodando): o fim desta execução não muda nada na tela nem nos ajustes.</summary>
    internal bool Abandonada { get; set; }

    public string UltimaMensagem => Path.Combine(Registro.Pasta, "ultima-mensagem.md");

    internal void Aplicar(LoteProgresso lote)
    {
        var novas = lote.Linhas;
        if (novas.Count >= MaximoDeLinhas)
        {
            linhas.Clear();
            foreach (var l in novas.Skip(novas.Count - MaximoDeLinhas)) linhas.Add(l);
        }
        else
        {
            var excesso = linhas.Count + novas.Count - MaximoDeLinhas;
            for (var i = 0; i < excesso; i++) linhas.RemoveAt(0);
            foreach (var l in novas) linhas.Add(l);
        }
        if (lote.Situacao is not null) Situacao = lote.Situacao;
    }
}

/// <summary>Liga a página Novo relatório ao serviço da Tarefa 19 (desenho §12.2–§12.7): trava do processo, registro, anexos copiados pela regra do kit,
/// <c>pedido.md</c>/<c>anexos.json</c> e a partida pelo <see cref="OrquestradorAnalise"/>. O andamento chega agrupado (<see cref="ProgressoAgrupado"/>, no máximo
/// 10 vezes por segundo) e passa à thread da janela. Nada aqui analisa: o agente instalado no computador faz tudo.</summary>
internal sealed class SessaoNovoRelatorio(DispatcherQueue janela, Func<OrquestradorAnalise> orquestrador, Func<Pecas> pecas, Func<Provedor, ModoAcesso> modo, string raizDados)
{
    readonly List<ExecucaoEmCurso> execucoes = [];

    /// <summary>As execuções em andamento, da mais antiga à mais nova.</summary>
    public IReadOnlyList<ExecucaoEmCurso> Execucoes => execucoes;

    /// <summary>Linhas novas ou situação nova numa execução (na thread da janela).</summary>
    public event Action<ExecucaoEmCurso>? Atualizada;

    public bool EmAndamento(string pastaProcesso) =>
        execucoes.Any(e => string.Equals(Path.GetFullPath(e.Processo.Pasta), Path.GetFullPath(pastaProcesso), StringComparison.OrdinalIgnoreCase));

    public ExecucaoEmCurso? Do(string pastaProcesso) =>
        execucoes.FirstOrDefault(e => string.Equals(Path.GetFullPath(e.Processo.Pasta), Path.GetFullPath(pastaProcesso), StringComparison.OrdinalIgnoreCase));

    internal void Remover(ExecucaoEmCurso e) => execucoes.Remove(e);

    /// <summary>Trava → registro → anexos copiados → <c>pedido.md</c>/<c>anexos.json</c> → partida. Recusa antes da partida (peça faltando, execução no processo,
    /// falha ao copiar): <see cref="AnaliseRecusadaException"/> ou a exceção do disco, com a trava solta e nada iniciado.</summary>
    public async Task<ExecucaoEmCurso?> AnalisarAsync(Provedor p, Processo pr, IReadOnlyList<(string Origem, DestinoAnexo Destino)> anexos, string? texto)
    {
        var pc = pecas();
        var m = modo(p);
        var chaveGuardada = m == ModoAcesso.Chave && CofreChaves.Existe(raizDados, p);
        if (FluxoNovoRelatorio.MotivoDesabilitado(p, pr, EmAndamento(pr.Pasta), pc, m, chaveGuardada) is { } motivo) throw new AnaliseRecusadaException(motivo);
        texto = string.IsNullOrWhiteSpace(texto) ? null : texto;

        var (trava, registro, arquivados) = await Task.Run(() =>
        {
            var t = TravaExecucao.Tentar(pr.Pasta, p) ?? throw new AnaliseRecusadaException(FluxoNovoRelatorio.EmAndamento);
            try
            {
                var plano = Arquivamento.Planejar(pr.Pasta, anexos);
                var reg = RegistroExecucao.Criar(pr.Pasta, p, DateTime.Now);
                var feitos = Arquivamento.Arquivar(pr.Pasta, plano, CancellationToken.None);
                reg.GravarPedido(p == Provedor.ChatGpt ? PedidoAgente.Codex(pr.Pasta, pr, feitos, texto) : texto ?? "");
                reg.GravarAnexos(feitos);
                return (t, reg, feitos);
            }
            catch { t.Dispose(); throw; }   // falha antes da partida: a trava sai e nada é iniciado
        });

        // A trava pega antes de copiar os anexos passa ao orquestrador, que a segura até o fim (nunca fica um instante sem dono).
        var orq = orquestrador();
        var e = new ExecucaoEmCurso(p, pr, registro, DateTime.UtcNow, pc, texto, orq, retomada: false);
        var progresso = Progresso(e);
        var pedido = new PedidoExecucao(p, pr, pc, m, raizDados, texto, arquivados, registro);
        e.Fim = RodarAsync(() => orq.ExecutarAsync(pedido, progresso, e.Cancelamento.Token, trava), progresso, trava);
        execucoes.Add(e);
        return e;
    }

    /// <summary>Volta a acompanhar uma análise do Claude que seguiu sozinha (editor reaberto): só <c>situacao</c> e o log, com o registro original.</summary>
    public async Task<ExecucaoEmCurso> RetomarClaudeAsync(Processo pr, string pastaRegistro, DateTimeOffset inicio)
    {
        if (EmAndamento(pr.Pasta)) throw new AnaliseRecusadaException(FluxoNovoRelatorio.EmAndamento);
        var pc = pecas();
        var (trava, registro, texto) = await Task.Run(() =>
        {
            var t = TravaExecucao.Tentar(pr.Pasta, Provedor.Claude) ?? throw new AnaliseRecusadaException(FluxoNovoRelatorio.EmAndamento);
            try
            {
                var r = RegistroExecucao.Abrir(pastaRegistro, pr.Pasta);   // recusa um registro fora de <processo>\.pmvni-ai\editor
                string? pedido = null;
                try { pedido = File.ReadAllText(Path.Combine(pastaRegistro, "pedido.md"), Encoding.UTF8); }
                catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
                return (t, r, string.IsNullOrWhiteSpace(pedido) ? null : pedido);
            }
            catch { t.Dispose(); throw; }
        });
        var orq = orquestrador();
        var e = new ExecucaoEmCurso(Provedor.Claude, pr, registro, inicio.UtcDateTime, pc, texto, orq, retomada: true);
        var progresso = Progresso(e);
        var pedidoExecucao = new PedidoExecucao(Provedor.Claude, pr, pc, ModoAcesso.Conta, raizDados, texto, [], registro);
        e.Fim = RodarAsync(() => orq.AcompanharClaudeAsync(pedidoExecucao, inicio, progresso, e.Cancelamento.Token), progresso, trava);
        execucoes.Add(e);
        return e;
    }

    ProgressoAgrupado Progresso(ExecucaoEmCurso e) => new(lote => janela.TryEnqueue(() =>
    {
        e.Aplicar(lote);
        Atualizada?.Invoke(e);
    }));

    static async Task<FimAnalise> RodarAsync(Func<Task<FimAnalise>> executar, ProgressoAgrupado progresso, IDisposable? trava)
    {
        try { return await Task.Run(executar); }
        finally
        {
            progresso.Dispose();     // entrega o que faltar antes do fim (na mesma fila da janela)
            trava?.Dispose();
        }
    }

    /// <summary>ChatGPT: encerra a árvore do Codex. Claude: encerra o processo do kit (<see cref="OrquestradorAnalise.CancelarClaudeAsync"/>); o kit passa a
    /// "interrompida" e o acompanhamento termina assim. false = nada a encerrar.</summary>
    public async Task<bool> CancelarAsync(ExecucaoEmCurso e)
    {
        if (e.Provedor == Provedor.ChatGpt)
        {
            e.Cancelamento.Cancel();
            return true;
        }
        return await e.Orquestrador.CancelarClaudeAsync(e.Processo.Pasta, e.Pecas);
    }

    /// <summary>Fechar o editor com o Claude rodando: só para de acompanhar (a análise do kit continua; o editor reaberto a retoma).</summary>
    public void PararDeAcompanhar(ExecucaoEmCurso e)
    {
        e.Abandonada = true;
        e.Cancelamento.Cancel();
    }
}
