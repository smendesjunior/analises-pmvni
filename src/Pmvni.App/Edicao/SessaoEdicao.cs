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

/// <summary>Um pacote <c>.rpmvni</c> aberto no app: liga <see cref="SessaoRevisao"/> (o projeto e o histórico), o <see cref="Recompositor"/>
/// (pasta de trabalho com as imagens) e a tela. <see cref="RecomporAsync"/>: âncora → preparar → compor no host oculto → carregar e
/// aplicar na tela → <c>estadoEditor</c> (sempre, num finally: sem ele a página fica parada). <see cref="GravarAsync"/>: grava com
/// <c>Pacote.Gravar(…, esperado: o disco como foi aberto ou gravado)</c>; mudança no disco vira Conflito e nada é sobrescrito.
/// Tarefa 10 (desenho §5.1): a trava do arquivo (adquirida na abertura; ocupada ou sem como travar ⇒ somente leitura com o motivo; perdida
/// num batimento ⇒ somente leitura), o diário de recuperação (<see cref="DiarioDaSessao"/>: cada registro vai para ele e ele recomeça a cada
/// gravação) e a recuperação na abertura. Não é segura para várias threads: a janela usa uma só (os comandos vão numa fila).</summary>
internal sealed class SessaoEdicao : IAsyncDisposable
{
    readonly ModeloCarregado modelo;
    readonly ITelaEdicao tela;
    readonly GravadorPacote gravador;
    readonly string raizDados;
    readonly bool somenteLeituraDoPacote; // esquema mais novo que o conhecido: nunca editável, com ou sem trava
    readonly DiarioDaSessao diario;
    Trava? trava;
    Task<bool>? batendo;
    int descartada, gravacoesEmCurso;
    bool encerrada;

    SessaoEdicao(string caminho, PacoteAberto aberto, ResultadoTrava? travou, Recompositor recompositor, ModeloCarregado modelo, ITelaEdicao tela, string raizDados)
    {
        gravador = new GravadorPacote(caminho, aberto.Disco);
        Conteudo = aberto.Conteudo;
        this.raizDados = raizDados;
        diario = new DiarioDaSessao(raizDados);
        somenteLeituraDoPacote = aberto.SomenteLeitura;
        SomenteLeitura = aberto.SomenteLeitura;
        MotivoSomenteLeitura = aberto.MotivoSomenteLeitura;
        if (travou is not null) AplicarTrava(travou);
        Recompositor = recompositor;
        this.modelo = modelo;
        this.tela = tela;
        Revisao = new SessaoRevisao(aberto.Conteudo.Projeto, aberto.Conteudo.Historico, ContextoUsuario.Atual());
        Revisao.Registrado += r =>
        {
            if (Estado is EstadoGravacao.Salvo) DefinirEstado(EstadoGravacao.NaoSalvo);
            // Disco cheio ou pasta sem escrita: a janela mostra (nunca engolido); o pacote ainda pode ser gravado.
            // Com os pendentes: um diário que não pôde começar (ou falhou numa escrita) recomeça aqui com tudo (T10-C2).
            if (!diario.Registrar(r, Revisao.Pendentes)) FalhaNoDiario?.Invoke(diario.UltimoErro ?? "erro desconhecido");
        };
        Estado = SomenteLeitura ? EstadoGravacao.SomenteLeitura : EstadoGravacao.Salvo;
        SalvoEmLocal = aberto.Disco.GravadoUtc.ToLocalTime();
    }

    /// <summary>Quem este app é na trava: máquina, usuário do Windows, processo, agora.</summary>
    static InfoTrava Eu()
    {
        var agora = DateTimeOffset.UtcNow;
        return new InfoTrava(Environment.MachineName, ContextoUsuario.Atual().Usuario, Environment.ProcessId, agora, agora);
    }

    static ResultadoTrava Travar(string caminho) => Trava.Adquirir(caminho, Eu(), () => DateTimeOffset.UtcNow);

    /// <summary>Adquirida ⇒ editável (se o pacote deixa); ocupada ⇒ "Aberto por … desde HH:mm: somente leitura."; erro ⇒ somente leitura com o motivo.</summary>
    void AplicarTrava(ResultadoTrava r)
    {
        trava = r.Adquirida;
        if (somenteLeituraDoPacote) return;
        SomenteLeitura = trava is null;
        MotivoSomenteLeitura = r.Adquirida is not null ? null
            : r.Ocupada is { } o ? TextosGravacao.SomenteLeituraOcupada(o)
            : TextosGravacao.SomenteLeituraSemTrava(r.Erro ?? "erro desconhecido");
    }

    /// <summary>O <c>.rpmvni</c> no disco.</summary>
    public string Caminho => gravador.Caminho;

    /// <summary>O conteúdo como foi lido (manifesto, recebido, imagens); o projeto e o histórico vivos estão em <see cref="Revisao"/>.</summary>
    public ConteudoPacote Conteudo { get; }

    /// <summary>O disco como foi aberto ou gravado por último: o "esperado" da próxima gravação e a referência da exportação.</summary>
    public EstadoDisco Disco => gravador.Disco;

    /// <summary>Somente leitura: esquema mais novo, trava de outro (ou sem como travar) ou trava perdida. Muda com a trava.</summary>
    public bool SomenteLeitura { get; private set; }
    public string? MotivoSomenteLeitura { get; private set; }

    /// <summary>A trava do arquivo é desta sessão (o batimento a renova).</summary>
    public bool TemTrava => trava is not null;

    /// <summary>Há registros que o arquivo ainda não tem (estão só na memória e no diário).</summary>
    public bool TemAlteracoesNaoGravadas => Revisao.Pendentes.Count > 0;

    /// <summary>O diário de recuperação está recebendo os registros.</summary>
    public bool DiarioAtivo => diario.Ativo;
    public SessaoRevisao Revisao { get; }
    public Recompositor Recompositor { get; }
    public EstadoGravacao Estado { get; private set; }
    public DateTime? SalvoEmLocal { get; private set; }

    /// <summary>A última recomposição aplicada na tela (null antes da primeira).</summary>
    public Recomposicao? Atual { get; private set; }

    /// <summary>A última alteração já está no projeto (e no diário), mas a recomposição falhou: a tela e <see cref="Atual"/> são de antes
    /// dela. A exportação fica recusada até uma recomposição dar certo (o PDF sairia velho com o recibo do pacote novo).</summary>
    public bool Desatualizada { get; private set; }

    /// <summary>"A tela não mostra a última alteração…": o motivo da recusa da exportação com a sessão desatualizada.</summary>
    public const string TextoDesatualizada =
        "As folhas na tela não mostram a última alteração (a recomposição falhou). Use Recompor; o PDF só sai das folhas atualizadas.";

    /// <summary>Só o roteiro: faz a próxima recomposição falhar depois de preparar (como o host de composição caindo).</summary>
    internal Func<Task>? FalhaDeRoteiroNaRecomposicao { get; set; }

    /// <summary>Item selecionado (data-ref) mandado no estado do editor.</summary>
    public string? Selecao { get; set; }

    /// <summary>Campos editáveis no A4 (duplo clique, F2, Editar texto). Somente leitura desliga a edição de qualquer forma.</summary>
    public bool CamposEditaveis { get; set; } = true;

    /// <summary>Estado de gravação mudou (a barra de título acompanha).</summary>
    public event Action? Mudou;

    /// <summary>O diário não pôde ser começado ou um registro não foi gravado nele (o motivo); a janela mostra.</summary>
    public event Action<string>? FalhaNoDiario;

    /// <summary>O diário recomeçou bem depois de uma gravação (o aviso de falha pode sair).</summary>
    public event Action? DiarioVoltou;

    /// <summary>A trava mudou: perdida num batimento (somente leitura) ou trocada por um Salvar como.</summary>
    public event Action? TravaMudou;

    /// <summary>Lê o pacote e tenta a trava (fora da thread da interface) e prepara a pasta de trabalho; a tela ainda não muda. O diário
    /// só começa em <see cref="IniciarDiario"/>, depois de a janela decidir a recuperação.</summary>
    public static async Task<SessaoEdicao> AbrirAsync(string pmvrel, ModeloCarregado modelo, ITelaEdicao tela, string raizDados)
    {
        var caminho = Path.GetFullPath(pmvrel);
        var (aberto, travou, rec) = await Task.Run(() =>
        {
            var a = Pacote.Ler(caminho);
            var t = a.SomenteLeitura ? null : Travar(caminho);
            try { return (a, t, new Recompositor(a.Conteudo, modelo)); }
            catch { t?.Adquirida?.Dispose(); throw; }
        });
        return new SessaoEdicao(caminho, aberto, travou, rec, modelo, tela, raizDados);
    }

    // ---------- recuperação, diário e trava ----------

    /// <summary>O diário deixado para este arquivo (null se não há ou não tem registros). Só com a trava nossa: com outra janela ou outro
    /// usuário editando, o diário pode ser o dela.</summary>
    public Recuperacao? LerRecuperacao()
    {
        if (SomenteLeitura) return null;
        try { return Diario.Ler(raizDados, Caminho, Disco.Sha256); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { FalhaNoDiario?.Invoke(MensagensDeErro.ParaUsuario(e)); return null; }
    }

    /// <summary>Reaplica o diário (tudo ou nada: <see cref="InvalidOperationException"/> e nada muda) e o estado vira "Não salvo".</summary>
    public void Recuperar(Recuperacao rec)
    {
        Revisao.Reaplicar(rec.Registros);
        RecuperarImagens();
        if (TemAlteracoesNaoGravadas) DefinirEstado(EstadoGravacao.NaoSalvo);
    }

    /// <summary>Guarda o diário deste arquivo como órfão (recuperação recusada, de outra versão ou Recarregar); devolve o caminho do órfão.</summary>
    public string? ArquivarDiario() => diario.Arquivar(Caminho);

    /// <summary>Começa o diário com o sha do arquivo aberto e os pendentes (o que foi recuperado). Somente leitura: nada a registrar.</summary>
    public bool IniciarDiario()
    {
        if (SomenteLeitura) return true;
        if (diario.Iniciar(Caminho, Disco.Sha256, Revisao.Pendentes)) return true;
        FalhaNoDiario?.Invoke(diario.UltimoErro ?? "erro desconhecido");
        return false;
    }

    /// <summary>Batimento (a cada 30 s, com ou sem trava): toca a pasta de trabalho (a varredura de órfãs de outra instância nunca apaga
    /// uma sessão viva) e renova a trava, se houver (fora da thread da interface). <c>false</c> = trava perdida: a sessão fica só para
    /// leitura (o diário continua com o que não foi gravado) e <see cref="TravaMudou"/> avisa.</summary>
    public async Task<bool> BaterAsync()
    {
        if (encerrada) return true;
        if (trava is not { } t)
        {
            await Task.Run(Recompositor.TocarPasta);
            return true;
        }
        var b = Task.Run(() => { Recompositor.TocarPasta(); return t.Bater(); });
        batendo = b;
        var ok = await b;
        if (!ok && !encerrada && ReferenceEquals(t, trava)) PerderTrava();
        return ok;
    }

    void PerderTrava()
    {
        var t = trava;
        trava = null;
        try { t?.Dispose(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (!somenteLeituraDoPacote)
        {
            SomenteLeitura = true;
            MotivoSomenteLeitura = TextosGravacao.TravaPerdida;
        }
        DefinirEstado(EstadoGravacao.SomenteLeitura);
        TravaMudou?.Invoke();
    }

    /// <summary>Importa o dados.json do kit para um <c>.rpmvni</c> novo (nunca sobrescreve: se o destino existe, recusa).</summary>
    public static Task<EstadoDisco> ImportarAsync(string dadosJson, string pmvrel, ModeloCarregado modelo) => Task.Run(() =>
    {
        var destino = Path.GetFullPath(pmvrel);
        if (File.Exists(destino)) throw new IOException("Já existe um arquivo com esse nome: " + Path.GetFileName(destino) + ". Escolha outro nome.");
        var origem = Path.GetFullPath(dadosJson);
        var conteudo = Importador.Importar(File.ReadAllBytes(origem), origem, modelo, ContextoUsuario.Atual());
        return Pacote.Gravar(destino, conteudo, esperado: null);
    });

    /// <summary>Recompõe e aplica na tela. Falha antes de tocar a tela deixa a tela como estava; o estado do editor vai sempre.
    /// <paramref name="preparado"/>: o que <see cref="Recompositor.Preparar"/> já fez com o projeto como está agora (o comando foi
    /// testado nele antes de ser aplicado); sem ele, prepara de novo.</summary>
    public async Task<Recomposicao> RecomporAsync(Preparado? preparado = null)
    {
        var relogio = Stopwatch.StartNew();
        var primeira = Atual is null;
        object? ancora = primeira ? null : await AncoraAsync();
        try
        {
            // A pasta de trabalho é "tocada" a cada preparação: a limpeza de órfãs de outra instância (pela data) nunca a apaga.
            await Task.Run(Recompositor.TocarPasta);
            var projeto = Revisao.Projeto;
            var p = preparado ?? await Task.Run(() => Recompositor.Preparar(projeto));
            if (FalhaDeRoteiroNaRecomposicao is { } falhar) await falhar();
            var host = await tela.HostAsync();
            ResultadoComposicao c;
            try { c = await Recompositor.ComporAsync(p, host); }
            catch { await tela.DescartarHostAsync(); throw; }

            await tela.Tela.CarregarAsync(p.Render, p.Config);
            var a = await tela.Tela.AplicarPlanoAsync(c.Plano);
            await tela.AjustarZoomAsync(primeira, a);
            var dados = DadosRelatorio.DeBytes(p.Dados, Path.GetFileNameWithoutExtension(Caminho));
            Atual = new Recomposicao(p, c, a, dados, dados.Navegacao(p.Render, c), relogio.ElapsedMilliseconds);
            Desatualizada = false;
            return Atual;
        }
        catch
        {
            // O projeto já tem a alteração (o comando foi aplicado antes): a tela e o preparado ficaram de antes dela.
            if (!primeira) Desatualizada = true;
            throw;
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

    /// <summary>Marcas de revisão: itens alterados (situação revisada ou campo revisado) e "ITEM EXCLUÍDO NN" de cada excluído, ancorado no
    /// item incluído seguinte (ou no anterior, se não houver seguinte).</summary>
    internal static object Marcas(Projeto p, bool visiveis)
    {
        var incluidos = p.Itens.Where(i => i.Incluido).OrderBy(i => i.Ordem).ToList();
        var alterados = incluidos.Where(i => i.Situacao.Revisada is not null || i.Campos.Values.Any(c => c.Revisado is not null)).Select(i => i.Id)
            .Concat(p.Linhas.Where(l => l.Value.Revisada).Select(l => l.Key)) // linhas de tabela revisadas (traço azul na linha)
            .ToArray();
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
        Conteudo with { Projeto = Revisao.Projeto.Clonar(), Historico = Revisao.HistoricoCompleto.ToList(), Assets = TodosOsAssets() };

    // ---------- imagens novas (recortes de prancha) ----------

    readonly Dictionary<string, byte[]> assetsNovos = new(StringComparer.Ordinal);

    IReadOnlyDictionary<string, byte[]> TodosOsAssets()
    {
        if (assetsNovos.Count == 0) return Conteudo.Assets;
        var todos = new Dictionary<string, byte[]>(Conteudo.Assets, StringComparer.Ordinal);
        foreach (var (k, v) in assetsNovos) todos[k] = v;
        return todos;
    }

    /// <summary>Pasta onde as imagens novas ficam guardadas até o pacote ser gravado (para a recuperação depois de uma queda).</summary>
    string PastaImagensRecuperacao => Path.Combine(raizDados, "recuperacao-imagens");

    /// <summary>Guarda uma imagem nova (PNG de um recorte) como <c>assets/&lt;sha256&gt;.png</c>: entra na pasta de trabalho (o render lê de lá),
    /// no próximo Salvar e numa cópia para a recuperação. Devolve a chave.</summary>
    public string AdicionarImagem(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        var ext = Pmvni.Composicao.CaminhoImagem.ExtensaoPelaAssinatura(png) ?? throw new InvalidOperationException("A imagem do recorte não é PNG.");
        var chave = "assets/" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(png)).ToLowerInvariant() + "." + ext;
        if (Conteudo.Assets.ContainsKey(chave) || assetsNovos.ContainsKey(chave)) return chave;
        assetsNovos[chave] = png;
        var destino = Path.Combine(Recompositor.PastaTrabalho, chave.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        File.WriteAllBytes(destino, png);
        try
        {
            Directory.CreateDirectory(PastaImagensRecuperacao);
            File.WriteAllBytes(Path.Combine(PastaImagensRecuperacao, Path.GetFileName(destino)), png);
            // As cópias antigas (mais de 7 dias) não servem mais a nenhuma recuperação.
            foreach (var velho in Directory.GetFiles(PastaImagensRecuperacao).Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-7)))
                File.Delete(velho);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Debug.WriteLine("[imagem recuperacao] " + e.Message); }
        return chave;
    }

    /// <summary>Depois de reaplicar o diário: as imagens de recortes que ainda não estavam no pacote voltam da cópia de recuperação.</summary>
    void RecuperarImagens()
    {
        foreach (var f in Revisao.Projeto.Figuras ?? new List<FiguraRevisao>())
        {
            if (Conteudo.Assets.ContainsKey(f.Imagem) || assetsNovos.ContainsKey(f.Imagem)) continue;
            var copia = Path.Combine(PastaImagensRecuperacao, Path.GetFileName(f.Imagem));
            if (!File.Exists(copia)) continue;
            var bytes = File.ReadAllBytes(copia);
            if (AdicionarImagem(bytes) != f.Imagem) assetsNovos.Remove(f.Imagem); // hash diferente: não é a mesma imagem
        }
    }

    /// <summary>Grava no mesmo arquivo. As gravações vão em fila (<see cref="GravadorPacote"/>): um Salvar pedido durante outro roda
    /// depois dele, com o conteúdo mais novo e o disco que ele deixou; nunca dá conflito consigo mesmo. Conflito (outro programa mudou o
    /// disco) ⇒ Conflito, nada é trocado; outra falha ⇒ Não salvo (o arquivo antigo fica intacto). As exceções seguem para quem chamou.</summary>
    public Task GravarAsync()
    {
        if (SomenteLeitura) throw new InvalidOperationException("O relatório está aberto somente para leitura.");
        return GravarNaFilaAsync(c => gravador.GravarAsync(c));
    }

    /// <summary>Grava num arquivo novo (nunca sobrescreve outro) e passa a editar esse arquivo; na mesma fila das gravações.</summary>
    public Task GravarComoAsync(string novo)
    {
        // O próprio arquivo, com a sessão somente leitura ou em conflito: nunca por cima (a proteção não é furada pelo Salvar como).
        if (RecusaSalvarComo(novo) is { } recusa) throw new InvalidOperationException(recusa);
        return GravarNaFilaAsync(c => gravador.GravarComoAsync(novo, c));
    }

    /// <summary>O motivo de recusar um Salvar como para <paramref name="destino"/> (null = pode).</summary>
    public string? RecusaSalvarComo(string destino) =>
        TextosGravacao.RecusaSalvarComo(destino, Caminho, SomenteLeitura, Estado == EstadoGravacao.Conflito, MotivoSomenteLeitura);

    async Task GravarNaFilaAsync(Func<Func<ConteudoPacote>, Task<EstadoDisco>> gravar)
    {
        var anterior = Estado;
        gravacoesEmCurso++;
        DefinirEstado(EstadoGravacao.Salvando);
        var registros = -1;
        try
        {
            // O conteúdo é tirado quando chega a vez desta gravação: o que já estava na fila não volta a ser gravado velho.
            var caminhoAntes = Caminho;
            await gravar(() => { registros = Revisao.HistoricoCompleto.Count; return ConteudoAtual(); });
            gravacoesEmCurso--;
            SalvoEmLocal = DateTime.Now;
            var tudo = Revisao.HistoricoCompleto.Count == registros;
            // O arquivo tem os registros até a vez desta gravação; um comando que chegou durante ela continua pendente (e no diário).
            Revisao.MarcarGravado(registros);
            if (!encerrada) DepoisDeGravar(caminhoAntes, registros);
            // Um comando aplicado enquanto gravava fica para a próxima gravação.
            DefinirEstado(SomenteLeitura ? EstadoGravacao.SomenteLeitura : tudo ? EstadoGravacao.Salvo : EstadoGravacao.NaoSalvo, final: true);
        }
        catch (ConflitoNoDiscoException) { gravacoesEmCurso--; DefinirEstado(EstadoGravacao.Conflito, final: true); throw; }
        catch
        {
            gravacoesEmCurso--;
            DefinirEstado(anterior == EstadoGravacao.Conflito ? EstadoGravacao.Conflito : EstadoGravacao.NaoSalvo, final: true);
            throw;
        }
    }

    /// <summary>Exporta o PDF das folhas que a tela mostra, a partir dos bytes efetivos. Recusa (sem gravar nada): erros finais
    /// (<see cref="ConferenciaFinalException"/>), pacote em conflito ou mudado no disco desde a abertura/última gravação
    /// (<see cref="ExportacaoDivergenteException.ArquivoMudou"/>); estouro e folhas diferentes, pelo <see cref="Exportador"/>. O recibo leva
    /// o sha256 do pacote. Os avisos de exportação (citações a excluídos) já foram confirmados por quem chamou.</summary>
    public async Task<ResultadoExportacao> ExportarAsync(string pdf)
    {
        var r = Atual ?? throw new InvalidOperationException("O relatório ainda não foi composto.");
        var p = r.Preparado;
        // A tela tem de ser a do projeto de agora: recomposição que falhou, ou qualquer diferença entre o que foi composto e o dados.json
        // efetivo do projeto, recusa (o recibo levaria o sha do pacote novo com o PDF velho).
        if (Desatualizada || !p.Dados.AsSpan().SequenceEqual(DadosEfetivos.Gerar(Revisao.Projeto).Json))
            throw new ExportacaoDivergenteException(TextoDesatualizada);
        if (p.ErrosFinais.Count > 0) throw new ConferenciaFinalException(p.ErrosFinais.Distinct().ToList());
        if (Estado == EstadoGravacao.Conflito) throw new ExportacaoDivergenteException(ExportacaoDivergenteException.ArquivoMudou);
        var caminho = Caminho;
        var agora = await Task.Run(() => EstadoDisco.Ler(caminho));
        if (!agora.MesmoConteudo(Disco)) throw new ExportacaoDivergenteException(ExportacaoDivergenteException.ArquivoMudou);
        return await Exportador.ExportarFinalAsync(p.Dados, p.CaminhoDados, p.Config, pdf, r.Composicao.Plano, modelo, Disco.Sha256);
    }

    /// <summary>Gravado: num arquivo novo (Salvar como), solta a trava do antigo e tenta a do novo (sem ela, somente leitura); o diário
    /// recomeça com o sha novo e o que chegou durante a gravação.</summary>
    void DepoisDeGravar(string caminhoAntes, int registros)
    {
        if (!string.Equals(caminhoAntes, Caminho, StringComparison.OrdinalIgnoreCase) && !somenteLeituraDoPacote)
        {
            var velha = trava;
            trava = null;
            try { velha?.Dispose(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            AplicarTrava(Travar(Caminho));
            TravaMudou?.Invoke();
        }
        var extras = Revisao.HistoricoCompleto.Skip(registros).ToList();
        if (diario.DepoisDeGravar(caminhoAntes, Caminho, Disco.Sha256, extras, continuar: !SomenteLeitura)) DiarioVoltou?.Invoke();
        else FalhaNoDiario?.Invoke(diario.UltimoErro ?? "erro desconhecido");
    }

    /// <summary>Com gravações ainda na fila, o estado continua "Salvando…" até a última terminar.</summary>
    void DefinirEstado(EstadoGravacao e, bool final = false)
    {
        if (final && gravacoesEmCurso > 0 && e != EstadoGravacao.Conflito) e = EstadoGravacao.Salvando;
        Estado = e;
        Mudou?.Invoke();
    }

    /// <summary>Só o roteiro: a queda do app sem gravar — solta a trava e a pasta de trabalho; o diário fica como está no disco.</summary>
    internal void SimularQueda()
    {
        encerrada = true;
        var t = trava;
        trava = null;
        try { t?.Dispose(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        diario.Dispose();
        _ = DisposeAsync();
    }

    /// <summary>Fecha: solta a trava, fecha o diário (guardado se há alterações não gravadas; apagado se tudo está no arquivo) e apaga a
    /// pasta de trabalho fora da thread da interface (o Dispose dela pode esperar ~1 s por arquivos presos).</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref descartada, 1) != 0) return;
        encerrada = true;
        if (batendo is { } b) try { await b; } catch { /* o batimento nunca lança; só espera ele terminar */ }
        var t = trava;
        trava = null;
        try { t?.Dispose(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        diario.Fechar(guardar: TemAlteracoesNaoGravadas);
        await Task.Run(Recompositor.Dispose);
    }
}
