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
        var anexos = Acervo.Listar();
        IReadOnlyList<LinhaAnexo> linhas = anexos.Select(a => ListaLegislacao.Linha(a, Acervo.Estado(a.Id), emCurso.Contains(a.Id), fuso)).ToList();
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
        await operacao.WaitAsync(ct);
        try
        {
            foreach (var caminho in caminhos)
            {
                if (ct.IsCancellationRequested) break;
                var nome = Path.GetFileName(caminho);
                p?.Report(new ProgressoAnexo(nome, 0, 0));
                try
                {
                    var r = await PrepararEExtrairAsync(caminho, null, nome, p, ct);
                    anexados.Add(await Task.Run(() => Acervo.Confirmar(r.Entrada, r.Resultado)));
                }
                catch (AnexoRecusadoException e) { recusas.Add(e.Message); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            }
        }
        finally { operacao.Release(); }
        if (anexados.Count > 0) await AvisarMudancaAsync();
        return (anexados, recusas);
    }

    /// <summary>"Substituir arquivo…": as mesmas verificações do anexar; mantém id, título e esfera. Devolve a mensagem de recusa ou null.</summary>
    public async Task<string?> SubstituirAsync(string id, string caminho, CancellationToken ct, IProgress<ProgressoAnexo>? p = null)
    {
        var nome = Path.GetFileName(caminho);
        await operacao.WaitAsync(ct);
        try
        {
            var r = await PrepararEExtrairAsync(caminho, id, nome, p, ct);
            await Task.Run(() => Acervo.ConfirmarSubstituicao(id, r.Entrada, r.Resultado));
        }
        catch (AnexoRecusadoException e) { return e.Message; }
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
                _ => MensagensAnexo.Ilegivel(nome, e.Detalhe),
            });
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

    async Task AvisarMudancaAsync()
    {
        Task<IndiceLegislacao>? atual;
        lock (trava) atual = indice;
        if (atual is { IsCompletedSuccessfully: true }) await Task.Run(() => atual.Result.Atualizar(Acervo));
        Mudou?.Invoke();
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
            return extrator ??= await criarExtrator();
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
        }
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
                throw new ExtracaoRecusadaException(FalhaExtracao.Falhou, "O leitor de PDF não pôde ser iniciado: " + e.Message);
            }
            return await real.ExtrairAsync(caminhoPdf, progresso, ct);
        }
    }
}
