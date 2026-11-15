using System.Diagnostics;
using System.Text.Json;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O que a sessão de edição precisa da janela: a tela (WebView2 visível), o host oculto de composição (criado sob demanda e
/// descartado se cair), as marcas de revisão ligadas ou não e o zoom (depois de aplicar o plano, antes do estado do editor).</summary>
internal interface ITelaEdicao
{
    DocumentoWeb Tela { get; }
    Task<HostOculto> HostAsync();
    Task DescartarHostAsync();
    bool MarcasVisiveis { get; }
    Task AjustarZoomAsync(bool primeiraVez, ResultadoAplicacao aplicacao);
}

/// <summary>Uma recomposição aplicada na tela: o preparado (dados efetivos, render, erros finais), a composição do host oculto, o
/// que a tela respondeu, os dados da barra e da lista de navegação, e quanto levou.</summary>
internal sealed record Recomposicao(Preparado Preparado, ResultadoComposicao Composicao, ResultadoAplicacao Aplicacao, DadosRelatorio Dados,
    IReadOnlyList<ItemNavegacao> Navegacao, long Milissegundos);

/// <summary>Um pacote <c>.pmvrel</c> aberto no app: liga <see cref="SessaoRevisao"/> (o projeto e o histórico), o <see cref="Recompositor"/>
/// (pasta de trabalho com as imagens) e a tela. <see cref="RecomporAsync"/>: âncora → preparar → compor no host oculto → carregar e
/// aplicar na tela → <c>estadoEditor</c> (sempre, num finally: sem ele a página fica parada). <see cref="GravarAsync"/>: grava com
/// <c>Pacote.Gravar(…, esperado: o disco como foi aberto ou gravado)</c>; mudança no disco vira Conflito e nada é sobrescrito.
/// Não é segura para várias threads: a janela usa uma só (e não aceita dois comandos ao mesmo tempo).</summary>
internal sealed class SessaoEdicao : IAsyncDisposable
{
    readonly ModeloCarregado modelo;
    readonly ITelaEdicao tela;
    int descartada;

    SessaoEdicao(string caminho, PacoteAberto aberto, Recompositor recompositor, ModeloCarregado modelo, ITelaEdicao tela)
    {
        Caminho = caminho;
        Conteudo = aberto.Conteudo;
        Disco = aberto.Disco;
        SomenteLeitura = aberto.SomenteLeitura;
        MotivoSomenteLeitura = aberto.MotivoSomenteLeitura;
        Recompositor = recompositor;
        this.modelo = modelo;
        this.tela = tela;
        Revisao = new SessaoRevisao(aberto.Conteudo.Projeto, aberto.Conteudo.Historico, ContextoUsuario.Atual());
        Revisao.Registrado += _ => { if (Estado is EstadoGravacao.Salvo) DefinirEstado(EstadoGravacao.NaoSalvo); };
        Estado = SomenteLeitura ? EstadoGravacao.SomenteLeitura : EstadoGravacao.Salvo;
        SalvoEmLocal = aberto.Disco.GravadoUtc.ToLocalTime();
    }

    /// <summary>O <c>.pmvrel</c> no disco.</summary>
    public string Caminho { get; private set; }

    /// <summary>O conteúdo como foi lido (manifesto, recebido, imagens); o projeto e o histórico vivos estão em <see cref="Revisao"/>.</summary>
    public ConteudoPacote Conteudo { get; }

    /// <summary>O disco como foi aberto ou gravado por último: o "esperado" da próxima gravação e a referência da exportação.</summary>
    public EstadoDisco Disco { get; private set; }

    public bool SomenteLeitura { get; }
    public string? MotivoSomenteLeitura { get; }
    public SessaoRevisao Revisao { get; }
    public Recompositor Recompositor { get; }
    public EstadoGravacao Estado { get; private set; }
    public DateTime? SalvoEmLocal { get; private set; }

    /// <summary>A última recomposição aplicada na tela (null antes da primeira).</summary>
    public Recomposicao? Atual { get; private set; }

    /// <summary>Item selecionado (data-ref) mandado no estado do editor.</summary>
    public string? Selecao { get; set; }

    /// <summary>Campos editáveis no A4 (a edição chega na Tarefa 9; até lá a tela só mostra).</summary>
    public bool CamposEditaveis { get; set; }

    /// <summary>Estado de gravação mudou (a barra de título acompanha).</summary>
    public event Action? Mudou;

    /// <summary>Lê o pacote (fora da thread da interface) e prepara a pasta de trabalho; a tela ainda não muda.</summary>
    public static async Task<SessaoEdicao> AbrirAsync(string pmvrel, ModeloCarregado modelo, ITelaEdicao tela)
    {
        var caminho = Path.GetFullPath(pmvrel);
        var (aberto, rec) = await Task.Run(() =>
        {
            var a = Pacote.Ler(caminho);
            return (a, new Recompositor(a.Conteudo, modelo));
        });
        return new SessaoEdicao(caminho, aberto, rec, modelo, tela);
    }

    /// <summary>Importa o dados.json do kit para um <c>.pmvrel</c> novo (nunca sobrescreve: se o destino existe, recusa).</summary>
    public static Task<EstadoDisco> ImportarAsync(string dadosJson, string pmvrel, ModeloCarregado modelo) => Task.Run(() =>
    {
        var destino = Path.GetFullPath(pmvrel);
        if (File.Exists(destino)) throw new IOException("Já existe um arquivo com esse nome: " + Path.GetFileName(destino) + ". Escolha outro nome.");
        var origem = Path.GetFullPath(dadosJson);
        var conteudo = Importador.Importar(File.ReadAllBytes(origem), origem, modelo, ContextoUsuario.Atual());
        return Pacote.Gravar(destino, conteudo, esperado: null);
    });

    /// <summary>Recompõe e aplica na tela. Falha antes de tocar a tela deixa a tela como estava; o estado do editor vai sempre.</summary>
    public async Task<Recomposicao> RecomporAsync()
    {
        var relogio = Stopwatch.StartNew();
        var primeira = Atual is null;
        object? ancora = primeira ? null : await AncoraAsync();
        try
        {
            // A pasta de trabalho é "tocada" a cada preparação: a limpeza de órfãs de outra instância (pela data) nunca a apaga.
            await Task.Run(() => { try { Directory.SetLastWriteTimeUtc(Recompositor.PastaTrabalho, DateTime.UtcNow); } catch (IOException) { } });
            var projeto = Revisao.Projeto;
            var p = await Task.Run(() => Recompositor.Preparar(projeto));
            var host = await tela.HostAsync();
            ResultadoComposicao c;
            try { c = await Recompositor.ComporAsync(p, host); }
            catch { await tela.DescartarHostAsync(); throw; }

            await tela.Tela.CarregarAsync(p.Render, p.Config);
            var a = await tela.Tela.AplicarPlanoAsync(c.Plano);
            await tela.AjustarZoomAsync(primeira, a);
            var dados = DadosRelatorio.DeBytes(p.Dados, Path.GetFileNameWithoutExtension(Caminho));
            Atual = new Recomposicao(p, c, a, dados, dados.Navegacao(p.Render, c), relogio.ElapsedMilliseconds);
            return Atual;
        }
        finally { await EnviarEstadoAsync(ancora); }
    }

    /// <summary>Manda <c>estadoEditor</c> (seleção, campos, marcas e âncora). Erro aqui só é registrado: quem chamou já tem o seu.</summary>
    public async Task EnviarEstadoAsync(object? ancora = null)
    {
        try
        {
            var dados = new Dictionary<string, object?>
            {
                ["selecao"] = Selecao,
                ["campos"] = CamposEditaveis && !SomenteLeitura,
                ["marcas"] = Marcas(Revisao.Projeto, tela.MarcasVisiveis),
                ["ancora"] = ancora,
            };
            await tela.Tela.ChamarAsync("estadoEditor", dados, TimeSpan.FromSeconds(15));
        }
        catch (Exception e) { Debug.WriteLine("[estadoEditor] " + e.Message); }
    }

    async Task<object?> AncoraAsync()
    {
        try
        {
            var r = await tela.Tela.ChamarAsync("obterAncora", new { }, TimeSpan.FromSeconds(10));
            if (r.TryGetProperty("ref", out var rf) && rf.ValueKind == JsonValueKind.String && r.TryGetProperty("deslocamentoPx", out var d))
                return new { @ref = rf.GetString(), deslocamentoPx = d.GetDouble() };
        }
        catch (Exception e) { Debug.WriteLine("[ancora] " + e.Message); }
        return null;
    }

    /// <summary>Marcas de revisão: itens alterados (situação revisada ou campo revisado) e "◦ excl. NN" de cada excluído, ancorado no
    /// item incluído seguinte (ou no anterior, se não houver seguinte).</summary>
    internal static object Marcas(Projeto p, bool visiveis)
    {
        var incluidos = p.Itens.Where(i => i.Incluido).OrderBy(i => i.Ordem).ToList();
        var alterados = incluidos.Where(i => i.Situacao.Revisada is not null || i.Campos.Values.Any(c => c.Revisado is not null)).Select(i => i.Id).ToArray();
        var excluidos = new List<object>();
        foreach (var x in p.Itens.Where(i => !i.Incluido).OrderBy(i => i.Ordem))
        {
            var seguinte = incluidos.FirstOrDefault(i => i.Ordem > x.Ordem);
            var anterior = incluidos.LastOrDefault(i => i.Ordem < x.Ordem);
            if (seguinte is null && anterior is null) continue;
            excluidos.Add(new { id = x.Id, rotulo = x.NumeroOriginal.ToString("00"), ancora = (seguinte ?? anterior)!.Id, lado = seguinte is not null ? "antes" : "depois" });
        }
        return new { visiveis, alterados, excluidos };
    }

    /// <summary>O pacote como está agora (projeto e histórico da sessão; o resto como foi lido), numa cópia que a gravação pode ler fora da thread da interface.</summary>
    public ConteudoPacote ConteudoAtual() =>
        Conteudo with { Projeto = Revisao.Projeto.Clonar(), Historico = Revisao.HistoricoCompleto.ToList() };

    /// <summary>Grava no mesmo arquivo. Conflito (o disco mudou desde a abertura/última gravação) ⇒ Conflito, nada é trocado; outra falha ⇒
    /// Não salvo (o arquivo antigo fica intacto). As exceções seguem para quem chamou mostrar.</summary>
    public async Task GravarAsync()
    {
        if (SomenteLeitura) throw new InvalidOperationException("O relatório está aberto somente para leitura.");
        var conteudo = ConteudoAtual();
        var anterior = Estado;
        DefinirEstado(EstadoGravacao.Salvando);
        try
        {
            var esperado = Disco;
            Disco = await Task.Run(() => Pacote.Gravar(Caminho, conteudo, esperado));
            Revisao.MarcarGravado();
            SalvoEmLocal = DateTime.Now;
            DefinirEstado(EstadoGravacao.Salvo);
        }
        catch (ConflitoNoDiscoException) { DefinirEstado(EstadoGravacao.Conflito); throw; }
        catch { DefinirEstado(anterior == EstadoGravacao.Conflito ? EstadoGravacao.Conflito : EstadoGravacao.NaoSalvo); throw; }
    }

    /// <summary>Grava num arquivo novo (nunca sobrescreve outro) e passa a editar esse arquivo.</summary>
    public async Task GravarComoAsync(string novo)
    {
        var destino = Path.GetFullPath(novo);
        if (string.Equals(destino, Caminho, StringComparison.OrdinalIgnoreCase)) { await GravarAsync(); return; }
        if (File.Exists(destino)) throw new IOException("Já existe um arquivo com esse nome: " + Path.GetFileName(destino) + ". Escolha outro nome.");
        var conteudo = ConteudoAtual();
        var anterior = Estado;
        DefinirEstado(EstadoGravacao.Salvando);
        try
        {
            Disco = await Task.Run(() => Pacote.Gravar(destino, conteudo, esperado: null));
            Caminho = destino;
            Revisao.MarcarGravado();
            SalvoEmLocal = DateTime.Now;
            DefinirEstado(EstadoGravacao.Salvo);
        }
        catch { DefinirEstado(anterior); throw; }
    }

    /// <summary>Exporta o PDF das folhas que a tela mostra, a partir dos bytes efetivos. Recusa (sem gravar nada): erros finais
    /// (<see cref="ConferenciaFinalException"/>), pacote em conflito ou mudado no disco desde a abertura/última gravação
    /// (<see cref="ExportacaoDivergenteException.ArquivoMudou"/>); estouro e folhas diferentes, pelo <see cref="Exportador"/>. O recibo leva
    /// o sha256 do pacote. Os avisos de exportação (citações a excluídos) já foram confirmados por quem chamou.</summary>
    public async Task<ResultadoExportacao> ExportarAsync(string pdf)
    {
        var r = Atual ?? throw new InvalidOperationException("O relatório ainda não foi composto.");
        var p = r.Preparado;
        if (p.ErrosFinais.Count > 0) throw new ConferenciaFinalException(p.ErrosFinais.Distinct().ToList());
        if (Estado == EstadoGravacao.Conflito) throw new ExportacaoDivergenteException(ExportacaoDivergenteException.ArquivoMudou);
        var caminho = Caminho;
        var agora = await Task.Run(() => EstadoDisco.Ler(caminho));
        if (!agora.MesmoConteudo(Disco)) throw new ExportacaoDivergenteException(ExportacaoDivergenteException.ArquivoMudou);
        return await Exportador.ExportarFinalAsync(p.Dados, p.CaminhoDados, p.Config, pdf, r.Composicao.Plano, modelo, Disco.Sha256);
    }

    void DefinirEstado(EstadoGravacao e)
    {
        Estado = e;
        Mudou?.Invoke();
    }

    /// <summary>Apaga a pasta de trabalho fora da thread da interface (o Dispose dela pode esperar ~1 s por arquivos presos).</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref descartada, 1) != 0) return;
        await Task.Run(Recompositor.Dispose);
    }
}
