using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Andamento da anexação: o arquivo em processamento e a página que o PDF.js está lendo (0 enquanto copia e confere).</summary>
public sealed record ProgressoAnexo(string Nome, int Pagina, int Total);

/// <summary>A legislação anexada do app (desenho §11.1–§11.3): o acervo em <c>&lt;dados&gt;\legislacao</c>, o servidor do host
/// <c>leg.pmvni.invalid</c>, o extrator real (PDF.js num WebView2 oculto, criado na primeira vez que precisa e de novo se o processo
/// dele cair) e o índice da pesquisa em todas as leis (em memória, carregado na primeira vez e atualizado a cada mudança). Tudo o que
/// lê ou grava disco roda em <c>Task.Run</c>; anexar, substituir e refazer índices passam um por vez (o extrator é um só).</summary>
internal sealed class SessaoLegislacao : IAsyncDisposable
{
    readonly Func<Task<IExtratorPdf>> criarExtrator;
    readonly SemaphoreSlim operacao = new(1, 1);
    readonly SemaphoreSlim criacao = new(1, 1);
    readonly object trava = new();
    readonly HashSet<string> indexando = new(StringComparer.Ordinal);
    // Figuras (correções 2026-11-15): a leitura de cada lei, cancelável (excluir, substituir, fechar o app), e os PDFs cuja leitura
    // falhou nesta sessão (a lei abre sem figuras, sem tentar de novo a cada abertura).
    readonly Dictionary<string, CancellationTokenSource> figurasEmCurso = new(StringComparer.Ordinal);
    readonly HashSet<string> figurasFalharam = new(StringComparer.OrdinalIgnoreCase);
    readonly CancellationTokenSource encerrando = new();
    Task<IndiceLegislacao>? indice;
    IExtratorPdf? extrator;
    bool descartada;

    /// <param name="pasta"><c>&lt;dados&gt;\legislacao</c>.</param>
    /// <param name="criarExtrator">Cria o extrator real (na thread da interface: quem chama garante).</param>
    public SessaoLegislacao(string pasta, Func<Task<IExtratorPdf>> criarExtrator)
    {
        Acervo = new AcervoLegislacao(pasta);
        this.criarExtrator = criarExtrator;
        Extrator = new ExtratorDaSessao(this);
    }

    public AcervoLegislacao Acervo { get; }

    public ServidorLegislacao Servidor { get; } = new();

    /// <summary>O extrator que a sessão usa (o real; os passos do roteiro também passam por ele).</summary>
    public IExtratorPdf Extrator { get; }

    /// <summary>Um anexo entrou, saiu ou mudou (título, esfera, arquivo, índice). De qualquer thread.</summary>
    public event Action? Mudou;

    /// <summary>Ids dos anexos cujo índice está sendo refeito agora (selo "Preparando a pesquisa…").</summary>
    public IReadOnlySet<string> Indexando { get { lock (trava) return indexando.ToHashSet(StringComparer.Ordinal); } }

    /// <summary>O índice da pesquisa em todas as leis (preguiçoso; carregado em <c>Task.Run</c>).</summary>
    public Task<IndiceLegislacao> IndiceAsync()
    {
        lock (trava)
        {
            if (indice is null || indice.IsFaulted || indice.IsCanceled)
                indice = Task.Run(() => IndiceLegislacao.Carregar(Acervo, CancellationToken.None));
            return indice;
        }
    }

    /// <summary>Os anexos e uma linha por anexo, com o estado de cada um (cópia, índice) e o selo de quem está sendo indexado.</summary>
    public Task<(IReadOnlyList<Anexo> Anexos, IReadOnlyList<LinhaAnexo> Linhas)> ListarAsync(TimeZoneInfo fuso) => Task.Run(() =>
    {
        var emCurso = Indexando;
        // Anexos e estados numa só carga do catálogo: um anexo excluído no meio nunca derruba a releitura.
        var comEstado = Acervo.ListarComEstado();
        IReadOnlyList<Anexo> anexos = comEstado.Select(x => x.Anexo).ToList();
        IReadOnlyList<LinhaAnexo> linhas = comEstado.Select(x => ListaLegislacao.Linha(x.Anexo, x.Estado, emCurso.Contains(x.Anexo.Id), fuso)).ToList();
        return (anexos, linhas);
    });

    /// <summary>Anexa os arquivos um por vez, na ordem: <c>Preparar</c> → <c>ExtrairAsync</c> → <c>Confirmar</c>. Recusa ⇒ a entrada é
    /// descartada e a mensagem vai para a lista; cancelar ⇒ só o arquivo em andamento é descartado (os anteriores ficam) e a lista
    /// volta com o que foi feito até ali.</summary>
    public async Task<(IReadOnlyList<Anexo> Anexados, IReadOnlyList<string> Recusas)> AnexarAsync(
        IReadOnlyList<string> caminhos, IProgress<ProgressoAnexo>? p, CancellationToken ct)
    {
        var anexados = new List<Anexo>();
        var recusas = new List<string>();
        // Cancelar enquanto espera a reindexação em segundo plano não é erro: nada foi feito, a lista volta vazia.
        try { await operacao.WaitAsync(ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return (anexados, recusas); }
        try
        {
            foreach (var caminho in caminhos)
            {
                if (ct.IsCancellationRequested) break;
                var nome = Path.GetFileName(caminho);
                p?.Report(new ProgressoAnexo(nome, 0, 0));
                (Entrada Entrada, ResultadoExtracao Resultado)? r = null;
                try
                {
                    r = await PrepararEExtrairAsync(caminho, null, nome, p, ct);
                    var pronto = r.Value;
                    anexados.Add(await Task.Run(() => Acervo.Confirmar(pronto.Entrada, pronto.Resultado)));
                }
                catch (AnexoRecusadoException e) { recusas.Add(e.Message); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    // Falha ao gravar este arquivo (catálogo preso, disco): ele fica de fora, com o motivo; os outros seguem.
                    RegistroErros.Gravar("legislacao.anexar", e);
                    if (r is { } sobra) Descartar(sobra.Entrada);
                    recusas.Add(e is InvalidOperationException ? e.Message : MensagensAnexo.Ilegivel(nome, e));
                }
            }
        }
        finally { operacao.Release(); }
        if (anexados.Count > 0) await AvisarMudancaAsync();
        return (anexados, recusas);
    }

    public const string NaoEstaMaisAnexada = "Esta lei não está mais anexada.";

    /// <summary>"Substituir arquivo…": as mesmas verificações do anexar; mantém id, título e esfera. Devolve a mensagem de recusa ou null.</summary>
    public async Task<string?> SubstituirAsync(string id, string caminho, CancellationToken ct, IProgress<ProgressoAnexo>? p = null)
    {
        var nome = Path.GetFileName(caminho);
        CancelarFiguras(id); // a leitura das figuras do arquivo antigo não segura nem atrapalha a troca
        await operacao.WaitAsync(ct);
        (Entrada Entrada, ResultadoExtracao Resultado)? r = null;
        try
        {
            r = await PrepararEExtrairAsync(caminho, id, nome, p, ct);
            var pronto = r.Value;
            await Task.Run(() => Acervo.ConfirmarSubstituicao(id, pronto.Entrada, pronto.Resultado));
        }
        catch (AnexoRecusadoException e) { return e.Message; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            RegistroErros.Gravar("legislacao.substituir", e);
            if (r is { } sobra) Descartar(sobra.Entrada);
            return MensagensAnexo.Ilegivel(nome, e);
        }
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException)
        {
            // A lei saiu (excluída por outra janela) ou o catálogo ficou só leitura entre a escolha e a confirmação: a entrada preparada sai já.
            RegistroErros.Gravar("legislacao.substituir", e);
            if (r is { } sobra) Descartar(sobra.Entrada);
            return e is KeyNotFoundException ? NaoEstaMaisAnexada : e.Message;
        }
        finally { operacao.Release(); }
        await AvisarMudancaAsync();
        return null;
    }

    async Task<(Entrada Entrada, ResultadoExtracao Resultado)> PrepararEExtrairAsync(string caminho, string? substitui, string nome,
        IProgress<ProgressoAnexo>? p, CancellationToken ct)
    {
        var entrada = await Task.Run(() => Acervo.Preparar(caminho, substitui), CancellationToken.None);
        try
        {
            var progresso = p is null ? null : new Progress<(int Pagina, int Total)>(x => p.Report(new ProgressoAnexo(nome, x.Pagina, x.Total)));
            var resultado = await Extrator.ExtrairAsync(entrada.Caminho, progresso, ct);
            return (entrada, resultado);
        }
        catch (ExtracaoRecusadaException e)
        {
            Acervo.Descartar(entrada);
            throw new AnexoRecusadoException(e.Falha switch
            {
                FalhaExtracao.Senha => MensagensAnexo.Senha(nome),
                FalhaExtracao.Invalido => MensagensAnexo.Invalido(nome),
                _ => Registrado(e, MensagensAnexo.LeitorFalhou(nome)),
            }, e);
        }
        catch
        {
            Acervo.Descartar(entrada);
            throw;
        }
    }

    /// <summary>Exclui o anexo (catálogo, cópia e índice; o original nunca). Devolve o aviso de cópia em uso, ou null.</summary>
    public async Task<string?> ExcluirAsync(string id)
    {
        CancelarFiguras(id);
        var aviso = await Task.Run(() => Acervo.Excluir(id));
        await AvisarMudancaAsync();
        return aviso;
    }

    public async Task RenomearAsync(string id, string titulo)
    {
        await Task.Run(() => Acervo.Renomear(id, titulo));
        await AvisarMudancaAsync();
    }

    public async Task DefinirEsferaAsync(string id, Esfera e)
    {
        await Task.Run(() => Acervo.DefinirEsfera(id, e));
        await AvisarMudancaAsync();
    }

    /// <summary>Refaz, um por vez e em segundo plano, o índice dos anexos que não o têm (ou cujo índice não vale mais). O que não
    /// puder ser lido fica para a próxima abertura.</summary>
    public async Task ReindexarPendentesAsync(CancellationToken ct)
    {
        var pendentes = await Task.Run(Acervo.SemIndice, ct);
        if (pendentes.Count == 0) return;
        lock (trava) foreach (var a in pendentes) indexando.Add(a.Id);
        Mudou?.Invoke();
        try
        {
            foreach (var a in pendentes)
            {
                if (ct.IsCancellationRequested) break;
                await operacao.WaitAsync(ct);
                try
                {
                    var caminho = await Task.Run(() => Acervo.CaminhoPdf(a.Id), ct);
                    if (caminho is null) continue;
                    var r = await Extrator.ExtrairAsync(caminho, null, ct);
                    await Task.Run(() => Acervo.GravarIndice(a.Id, r), CancellationToken.None);
                }
                catch (Exception e) when (e is ExtracaoRecusadaException or AnexoRecusadoException or IOException or UnauthorizedAccessException
                    or KeyNotFoundException or InvalidOperationException) { System.Diagnostics.Debug.WriteLine("[legislacao] índice de " + a.Id + ": " + e.Message); }
                finally
                {
                    operacao.Release();
                    lock (trava) indexando.Remove(a.Id);
                }
                await AvisarMudancaAsync();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        finally
        {
            lock (trava) foreach (var a in pendentes) indexando.Remove(a.Id);
        }
    }

    /// <summary>As figuras da lei para a leitura padronizada (correções 2026-11-15): as guardadas no acervo ou, na primeira vez, as que o
    /// extrator lê do PDF (e que ficam guardadas). Null se não foi possível lê-las (sem cópia, extrator indisponível, PDF recusado): a
    /// leitura segue sem elas e avisa. Na thread da interface (o extrator real é criado nela). Corre em segundo plano, numa fila própria do
    /// extrator (nunca segura abrir, pesquisar, anexar ou excluir), com tempo limitado por lei, e é cancelada ao excluir ou substituir a lei
    /// (<see cref="CancelarFiguras"/>), ao fechar o app (<see cref="DisposeAsync"/>) e por <paramref name="ct"/> (a lei fechada ou trocada).</summary>
    public async Task<IReadOnlyList<FiguraPdf>?> FigurasAsync(string id, CancellationToken ct)
    {
        var guardadas = await Task.Run(() => Acervo.Figuras(id), ct);
        if (guardadas is not null) return guardadas;
        var caminho = await Task.Run(() => Acervo.CaminhoPdf(id), ct);
        if (caminho is null) return null;
        CancellationTokenSource propria;
        lock (trava)
        {
            if (figurasFalharam.Contains(caminho)) return null;
            ObjectDisposedException.ThrowIf(descartada, this);
            if (figurasEmCurso.Remove(id, out var anterior)) anterior.Cancel();
            propria = CancellationTokenSource.CreateLinkedTokenSource(ct, encerrando.Token);
            figurasEmCurso[id] = propria;
        }
        try
        {
            IExtratorPdf real;
            try { real = await ObterExtratorAsync(); }
            catch (Exception e) when (e is not (OperationCanceledException or ObjectDisposedException))
            {
                RegistroErros.Gravar("legislacao.extrator", e);
                return null;
            }
            if (real is not IExtratorFiguras leitor) return null;
            IReadOnlyList<FiguraPdf> figuras;
            try { figuras = await leitor.ExtrairFigurasAsync(caminho, propria.Token); }
            catch (ExtracaoRecusadaException e)
            {
                // PDF recusado, erro ou tempo esgotado: a lei segue aberta, sem figuras (com o aviso), e não tenta de novo nesta sessão.
                RegistroErros.Gravar("legislacao.figuras", e);
                lock (trava) figurasFalharam.Add(caminho);
                return null;
            }
            propria.Token.ThrowIfCancellationRequested(); // excluída ou substituída no fim da leitura: nada é gravado
            try { await Task.Run(() => Acervo.GravarFiguras(id, figuras), CancellationToken.None); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
            {
                RegistroErros.Gravar("legislacao.figuras", e); // mostra agora; na próxima vez lê de novo
            }
            return figuras;
        }
        finally
        {
            lock (trava) if (figurasEmCurso.TryGetValue(id, out var x) && ReferenceEquals(x, propria)) figurasEmCurso.Remove(id);
            propria.Dispose();
        }
    }

    /// <summary>Cancela a leitura das figuras da lei <paramref name="id"/>, se houver (excluir, substituir).</summary>
    public void CancelarFiguras(string id)
    {
        lock (trava) if (figurasEmCurso.Remove(id, out var cts)) cts.Cancel();
    }

    /// <summary>Ids das leis cujas figuras estão sendo lidas agora (testes).</summary>
    internal IReadOnlyCollection<string> FigurasEmCurso { get { lock (trava) return [.. figurasEmCurso.Keys]; } }

    async Task AvisarMudancaAsync()
    {
        Task<IndiceLegislacao>? atual;
        lock (trava) atual = indice;
        try { if (atual is { IsCompletedSuccessfully: true }) await Task.Run(() => atual.Result.Atualizar(Acervo)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or KeyNotFoundException or InvalidOperationException)
        {
            // O índice fica com o que tinha; a próxima mudança tenta de novo. A gravação já valeu.
            RegistroErros.Gravar("legislacao.indice", e);
            lock (trava) if (ReferenceEquals(indice, atual)) indice = null;
        }
        Mudou?.Invoke();
    }

    void Descartar(Entrada e)
    {
        try { Acervo.Descartar(e); }
        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { RegistroErros.Gravar("legislacao.descartar", x); }
    }

    static string Registrado(Exception e, string mensagem)
    {
        RegistroErros.Gravar("legislacao.leitor", e);
        return mensagem;
    }

    async Task<IExtratorPdf> ObterExtratorAsync()
    {
        await criacao.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(descartada, this);
            if (extrator is Pmvni.Composicao.Web.Legislacao.ExtratorPdfJs { Inutilizavel: true } caido)
            {
                extrator = null;
                try { await caido.DisposeAsync(); } catch { }
            }
            if (extrator is not null) return extrator;
            var novo = await criarExtrator();
            if (descartada)
            {
                // A sessão foi descartada enquanto o extrator era criado (a janela fechou): ele não fica vivo sem dono.
                if (novo is IAsyncDisposable d) try { await d.DisposeAsync(); } catch { }
                throw new ObjectDisposedException(nameof(SessaoLegislacao));
            }
            return extrator = novo;
        }
        finally { criacao.Release(); }
    }

    /// <summary>Descarta o extrator (o controller oculto): antes da janela que o hospeda.</summary>
    public async ValueTask DisposeAsync()
    {
        IExtratorPdf? e;
        lock (trava)
        {
            if (descartada) return;
            descartada = true;
            e = extrator;
            extrator = null;
            foreach (var cts in figurasEmCurso.Values) cts.Cancel();
            figurasEmCurso.Clear();
        }
        encerrando.Cancel();
        if (e is IAsyncDisposable d) await d.DisposeAsync();
    }

    /// <summary>O extrator público da sessão: cria o real na primeira extração (e de novo se o processo dele caiu).</summary>
    sealed class ExtratorDaSessao(SessaoLegislacao s) : IExtratorPdf
    {
        public async Task<ResultadoExtracao> ExtrairAsync(string caminhoPdf, IProgress<(int Pagina, int Total)>? progresso, CancellationToken ct)
        {
            IExtratorPdf real;
            try { real = await s.ObterExtratorAsync(); }
            catch (Exception e) when (e is not (OperationCanceledException or ObjectDisposedException))
            {
                RegistroErros.Gravar("legislacao.extrator", e);
                throw new ExtracaoRecusadaException(FalhaExtracao.Falhou, MensagensAnexo.LeitorIndisponivel);
            }
            return await real.ExtrairAsync(caminhoPdf, progresso, ct);
        }
    }
}
