using System.Collections.ObjectModel;
using System.Text;
using Microsoft.UI.Dispatching;
using Pmvni.Agentes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Uma análise recusada antes da partida (o motivo exato, em português): nada foi copiado nem iniciado.</summary>
internal sealed class AnaliseRecusadaException(string mensagem) : Exception(mensagem);

/// <summary>A cópia dos anexos parou no meio (disco cheio, arquivo preso): nada foi iniciado, mas as cópias já feitas ficaram em <see cref="Pasta"/>.</summary>
internal sealed class CopiaInterrompidaException(string pasta, Exception causa) : Exception(causa.Message, causa)
{
    public string Pasta { get; } = pasta;
}

/// <summary>Uma análise em andamento (desenho §12.7): pertence à janela, não à página; trocar de módulo ou de relatório não a interrompe.</summary>
public sealed class ExecucaoEmCurso
{
    const int MaximoDeLinhas = 2000;
    readonly ObservableCollection<string> linhas = [];
    (DateTime Gravado, long Bytes, string? Perfil, string? Acesso, string Resumo)? resumoModelos;

    internal string ResumoModelos()
    {
        var tabela = new FileInfo(Path.Combine(RegistroDeTrabalho, TabelaDeModelos.NomeDoArquivo));
        var existe = tabela.Exists;
        var data = existe ? tabela.LastWriteTimeUtc : DateTime.MinValue;
        var bytes = existe ? tabela.Length : 0;
        var perfil = Registro.PerfilDaExecucao();
        var (modo, rota) = Registro.AcessoDaExecucao();
        var acesso = rota ?? modo;
        if (resumoModelos is { } anterior && anterior.Gravado == data && anterior.Bytes == bytes && anterior.Perfil == perfil && anterior.Acesso == acesso)
            return anterior.Resumo;
        var resumo = EscolhaModelosDaExecucao.ResumoConfigurado(RegistroDeTrabalho, Provedor, perfil, acesso);
        resumoModelos = (data, bytes, perfil, acesso, resumo);
        return resumo;
    }

    internal ExecucaoEmCurso(Provedor provedor, Processo processo, RegistroExecucao registro, DateTime inicioUtc, Pecas pecas, string? texto, OrquestradorAnalise orquestrador, bool retomada)
    {
        Provedor = provedor;
        Processo = processo;
        Registro = registro;
        InicioUtc = inicioUtc;
        relogio = new RelogioAnalise(registro.TempoAtivoGravado, TimeProvider.System, RegistroExecucao.LerInicio(registro.Pasta) ?? inicioUtc);
        Pecas = pecas;
        Texto = texto;
        Orquestrador = orquestrador;
        Retomada = retomada;
    }

    public Provedor Provedor { get; }
    public Processo Processo { get; }
    public RegistroExecucao Registro { get; }
    string? pastaDeTrabalho;
    internal string PastaDeTrabalho => Volatile.Read(ref pastaDeTrabalho) ?? Processo.Pasta;
    internal string RegistroDeTrabalho => Volatile.Read(ref pastaDeTrabalho) is { } trabalho
        ? OrquestradorAnalise.RegistroDoTrabalho(trabalho, Registro.Pasta) : Registro.Pasta;
    internal void MostrarTrabalhoEm(string pasta) => Volatile.Write(ref pastaDeTrabalho, pasta);
    public DateTime InicioUtc { get; }
    internal DateTime? InicioDoControleUtc { get; set; }

    readonly RelogioAnalise relogio;
    internal TimeSpan TempoAtivo => relogio.TempoAtivo;
    internal TimeSpan TempoDecorrido => relogio.TempoDecorrido;

    internal async Task<FimAnalise> AcompanharTempoAsync(Func<Task<FimAnalise>> executar)
    {
        using var cancelar = new CancellationTokenSource();
        var gravacao = GravarPeriodicamenteAsync(cancelar.Token);
        try { return await executar(); }
        finally
        {
            relogio.Parar();
            cancelar.Cancel();
            await gravacao;
            await Task.Run(GravarTempo);
        }
    }

    async Task GravarPeriodicamenteAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                GravarTempo();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    void GravarTempo()
    {
        try { Registro.GravarTempoAtivo(TempoAtivo); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { RegistroErros.Gravar("analise.tempo", e); }
    }

    /// <summary>As últimas 2.000 linhas de andamento (do Codex, ou do <c>auto-log.txt</c> do kit), na thread da janela.</summary>
    public IReadOnlyList<string> Linhas => linhas;

    internal ObservableCollection<string> LinhasObservaveis => linhas;

    /// <summary>A situação mais recente do análise pro (só Claude).</summary>
    public SituacaoKit? Situacao { get; private set; }

    /// <summary>Os eventos do kit, como o <c>situacao</c> os informou (só Claude).</summary>
    public IReadOnlyList<string> Eventos => Situacao?.Eventos ?? [];

    internal Task<FimAnalise>? Conclusao { get; set; }
    public Task<FimAnalise> Fim { get; internal set; } = null!;

    public Pecas Pecas { get; }
    public string? Texto { get; }
    internal OrquestradorAnalise Orquestrador { get; }

    /// <summary>O editor reaberto voltou a acompanhar uma análise do Claude que seguiu sozinha.</summary>
    public bool Retomada { get; }
    internal bool PausaSolicitada { get; set; }

    internal CancellationTokenSource Cancelamento { get; } = new();

    /// <summary>O editor deixou de acompanhar (fechou com o Claude rodando): o fim desta execução não muda nada na tela nem nos ajustes.</summary>
    internal bool Abandonada { get; set; }

    internal string? PastaAnaliseChatGpt { get; init; }
    public string UltimaMensagem => Path.Combine(PastaAnaliseChatGpt ?? RegistroDeTrabalho, "ultima-mensagem.md");

    /// <summary>Os anexos recusados no plano do clique (ficaram ilegíveis entre a prévia e a partida): não foram copiados nem enviados.</summary>
    public IReadOnlyList<string> Recusas { get; internal init; } = [];

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
        if (lote.Situacao is { } situacao)
        {
            Situacao = situacao;
            if (situacao.Codigo is "AGUARDANDO_COTA" or "PAUSADO_COTA" or "DECISAO") relogio.EmEspera(true);
            else if (situacao.Codigo is "EM_ANDAMENTO" or "RODANDO") relogio.EmEspera(false);
        }
    }
}

/// <summary>Liga a página Novo relatório ao serviço da Tarefa 19 (desenho §12.2–§12.7): trava do processo, registro, anexos copiados pela regra do kit,
/// <c>pedido.md</c>/<c>anexos.json</c> e a partida pelo <see cref="OrquestradorAnalise"/>. O andamento chega agrupado (<see cref="ProgressoAgrupado"/>, no máximo
/// 10 vezes por segundo) e passa à thread da janela. Nada aqui analisa: o agente instalado no computador faz tudo.</summary>
/// <param name="decidir">Contrato da Tarefa 2 (decisão do analista): mostra a decisão que o kit pediu (na thread da janela) e devolve a resposta; null = sem
/// resposta agora (a análise fica parada esperando, sem gastar nada).</param>
internal sealed class SessaoNovoRelatorio(DispatcherQueue janela, Func<OrquestradorAnalise> orquestrador, Func<Pecas> pecas, Func<Provedor, ModoAcesso> modo, string raizDados,
    Func<string>? rotaCodex = null, Func<ExecucaoEmCurso, DecisaoAnalise, CancellationToken, Task<string?>>? decidir = null)
{
    readonly List<ExecucaoEmCurso> execucoes = [];
    volatile bool encerrada;

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
    /// <param name="perfilGemini">Só o Gemini: o perfil fixado para esta análise (o escolhido em Ajustes ou, numa retomada, o que a análise já tinha);
    /// é ele que define o modo (conta ou chave), nunca o contrário.</param>
    /// <param name="ficticio">Só o Gemini, perfil gratuito: o usuário confirmou no clique que o processo é fictício.</param>
    /// <param name="modelos">Tarefa 19: a tabela efetiva de modelos (padrão do kit + ajuste local); null = vale o padrão do kit.</param>
    public async Task<ExecucaoEmCurso?> AnalisarAsync(Provedor p, Processo pr, IReadOnlyList<(string Origem, DestinoAnexo Destino)> anexos, string? texto,
        PerfilGemini? perfilGemini = null, bool ficticio = false, bool trocarGemini = false, string? registroAnterior = null, TabelaDeModelos? modelos = null,
        string? pastaQgis = null, string? pastaKit = null, ModoAcesso? modoOriginal = null, string? rotaOriginal = null,
        bool auditoriaAlternativa = false, bool usarAnaliseChatGpt = false)
    {
        if (!Provedores.Ativos.Contains(p)) throw new AnaliseRecusadaException(Provedores.RotaDesativada);
        var pc = pecas();
        var m = usarAnaliseChatGpt ? ModoAcesso.Conta : modoOriginal ?? modo(p);
        if (p != Provedor.Gemini) { perfilGemini = null; ficticio = false; }
        var chaveGuardada = m == ModoAcesso.Chave && CofreChaves.Existe(raizDados, p);
        if (FluxoNovoRelatorio.MotivoDesabilitado(p, pr, EmAndamento(pr.Pasta), pc, m, chaveGuardada, perfilGemini, usarAnaliseChatGpt) is { } motivo)
            throw new AnaliseRecusadaException(motivo);
        texto = string.IsNullOrWhiteSpace(texto) ? null : texto;

        var orq = orquestrador();
        var (trava, registro, arquivados, recusas, retomada) = await Task.Run(() =>
        {
            var t = TravaExecucao.Tentar(pr.Pasta, p) ?? throw new AnaliseRecusadaException(FluxoNovoRelatorio.EmAndamento);
            try
            {
                var plano = Arquivamento.Planejar(pr.Pasta, anexos);
                IReadOnlyList<ArquivoArquivado> feitos;
                try { feitos = Arquivamento.Arquivar(pr.Pasta, plano, CancellationToken.None); }
                catch (Exception x) when (x is IOException or UnauthorizedAccessException && plano.Any(i => i.Recusa is null && !i.JaEstava))
                {
                    throw new CopiaInterrompidaException(pr.Pasta, x);
                }
                // A mesma análise retomada no mesmo registro (escolhida, ou achada interrompida com o mesmo pedido e as mesmas peças): no ChatGPT, o
                // controlador do kit continua o controle que já existe (sem --inputs).
                var interrompida = !usarAnaliseChatGpt && registroAnterior is null
                    ? RegistroExecucao.BuscarInterrompida(pr, texto, feitos, p, orq.DocumentosParaTeste) : null;
                var reg = registroAnterior is not null ? RegistroExecucao.Abrir(registroAnterior, pr.Pasta)
                    : interrompida ?? RegistroExecucao.Criar(pr.Pasta, p, DateTime.Now);
                if (registroAnterior is not null && texto == FluxoNovoRelatorio.PedidoContinuar && File.Exists(Path.Combine(reg.Pasta, "pedido.md")))
                    texto = reg.PedidoOriginal();
                if (registroAnterior is null && interrompida is null)
                    reg.Atualizar(o =>
                    {
                        o["area_local_solicitada"] = !usarAnaliseChatGpt;
                        if (usarAnaliseChatGpt) o["plugin"] = OrquestradorAnalise.NomeAnaliseChatGpt;
                        if (auditoriaAlternativa) o["rota_auditoria_alternativa"] = p == Provedor.Claude ? "codex" : "claude";
                    });
                reg.GravarPedido(texto ?? "");      // o pedido digitado; nenhuma IA recebe pedido do editor (Tarefa 12)
                if (registroAnterior is not null && reg.LerAnexosCategorizados() is { Count: > 0 } anteriores)
                    feitos = anteriores;
                reg.GravarAnexos(feitos);
                return (t, reg, feitos, plano.Where(i => i.Recusa is not null).Select(i => i.Recusa!).ToList(), registroAnterior is not null || interrompida is not null);
            }
            catch { t.Dispose(); throw; }   // falha antes da partida: a trava sai e nada é iniciado
        });

        if (encerrada)
        {
            // A janela fechou durante a cópia dos anexos: o Codex/kit nunca parte (o que já foi copiado fica no processo).
            trava.Dispose();
            try { registro.Atualizar(o => { o["situacao"] = "cancelada"; o["mensagem"] = "A janela foi fechada antes da partida; nada foi iniciado."; }); }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
            registro.Dispose();
            throw new AnaliseRecusadaException("A janela foi fechada antes da partida; nada foi iniciado.");
        }
        // A trava pega antes de copiar os anexos passa ao orquestrador, que a segura até o fim (nunca fica um instante sem dono).
        var e = new ExecucaoEmCurso(p, pr, registro, DateTime.UtcNow, pc, texto, orq, retomada: false)
        {
            Recusas = recusas,
            PastaAnaliseChatGpt = usarAnaliseChatGpt ? OrquestradorAnalise.PastaAnaliseChatGpt(raizDados, pr.Pasta, registro.Pasta) : null,
        };
        var progresso = Progresso(e);
        var pedido = new PedidoExecucao(p, pr, pc, m, raizDados, texto, arquivados, registro)
        {
            RotaCodex = rotaOriginal ?? rotaCodex?.Invoke(), Perfil = perfilGemini, Ficticio = ficticio, TrocarGemini = trocarGemini, Retomar = retomada, Decidir = Decisor(e),
            Modelos = modelos, PastaQgis = pastaQgis, PastaKit = pastaKit,
            UsarDocumentos = !usarAnaliseChatGpt, UsarAnaliseChatGpt = usarAnaliseChatGpt, AoPrepararLocal = e.MostrarTrabalhoEm,
            NomeProfissional = Pmvni.Composicao.Profissional.Atual?.NomeLinha,
            CargoProfissional = Pmvni.Composicao.Profissional.Atual?.Cargo(),
        };
        e.Fim = e.AcompanharTempoAsync(() => RodarAsync(() => orq.ExecutarAsync(pedido, progresso, e.Cancelamento.Token, trava), progresso, trava));
        execucoes.Add(e);
        return e;
    }

    /// <summary>Volta a acompanhar uma análise do Claude que seguiu sozinha (editor reaberto): só <c>situacao</c> e o log, com o registro original.</summary>
    /// <param name="ia">O provedor do kit dono do registro: Claude (análise pro) ou Gemini (análise Google).</param>
    public async Task<ExecucaoEmCurso> RetomarClaudeAsync(Processo pr, string pastaRegistro, DateTimeOffset inicio, Provedor ia = Provedor.Claude)
    {
        if (!Provedores.Ativos.Contains(ia)) throw new AnaliseRecusadaException(Provedores.RotaDesativada);
        if (EmAndamento(pr.Pasta)) throw new AnaliseRecusadaException(FluxoNovoRelatorio.EmAndamento);
        var pc = pecas();
        var (trava, registro, texto) = await Task.Run(() =>
        {
            var t = TravaExecucao.Tentar(pr.Pasta, ia) ?? throw new AnaliseRecusadaException(FluxoNovoRelatorio.EmAndamento);
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
        var e = new ExecucaoEmCurso(ia, pr, registro, inicio.UtcDateTime, pc, texto, orq, retomada: true);
        var progresso = Progresso(e);
        var pedidoExecucao = new PedidoExecucao(ia, pr, pc, ModoAcesso.Conta, raizDados, texto, [], registro)
        { UsarDocumentos = AreaAnaliseLocal.UsaAreaLocal(registro.Pasta),
          Retomar = true, AoPrepararLocal = e.MostrarTrabalhoEm };
        e.Fim = e.AcompanharTempoAsync(() => RodarAsync(() => orq.AcompanharClaudeAsync(pedidoExecucao, inicio, progresso, e.Cancelamento.Token), progresso, trava));
        execucoes.Add(e);
        return e;
    }

    /// <summary>A pergunta de uma decisão do kit, feita na thread da janela; null sem quem pergunte, com a janela fechada ou se ela não pôde ser mostrada.</summary>
    Func<DecisaoAnalise, CancellationToken, Task<string?>>? Decisor(ExecucaoEmCurso e) => decidir is null ? null : (d, ct) =>
    {
        if (encerrada) return Task.FromResult<string?>(null);
        var resposta = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        ct.Register(() => resposta.TrySetCanceled(ct));
        if (!janela.TryEnqueue(async () =>
            {
                try { resposta.TrySetResult(encerrada ? null : await decidir(e, d, ct)); }
                catch (Exception x) { resposta.TrySetException(x); }
            }))
            resposta.TrySetResult(null);
        return resposta.Task;
    };

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

    /// <summary>ChatGPT: encerra a árvore do controlador do analise (ele e as sessões que abriu; nada mais). Claude: encerra o processo do kit (<see cref="OrquestradorAnalise.CancelarClaudeAsync"/>); o kit passa a
    /// "interrompida" e o acompanhamento termina assim. false = nada a encerrar.</summary>
    public async Task<bool> CancelarAsync(ExecucaoEmCurso e)
    {
        if (e.Provedor == Provedor.ChatGpt)
        {
            e.Cancelamento.Cancel();
            return true;
        }
        return await e.Orquestrador.CancelarClaudeAsync(e.PastaDeTrabalho, e.Pecas, e.Provedor);
    }

    /// <summary>Cancela as execuções ativas e impede a partida de análises que ainda estavam copiando anexos.</summary>
    public void Encerrar()
    {
        encerrada = true;
        foreach (var e in execucoes.ToList())
            try { e.Cancelamento.Cancel(); }
            catch (ObjectDisposedException) { }
    }
}
