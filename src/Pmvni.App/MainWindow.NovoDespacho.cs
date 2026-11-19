using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Novo despacho (Tarefa 16; desenho §10.2–§10.9): o menu "Novo despacho ▾" de Despachos, Processos e Relatórios (só os tipos do
/// registro do kit, desabilitados com o motivo quando o modelo falta ou não confere), a página Novo despacho no lugar da lista de Despachos
/// (origem → prévia editável → conferência das páginas do PDF → gravar na pasta do processo) e o "Gerar PDF" de um despacho do editor que
/// ficou só em DOCX. Nada é gravado na pasta do processo antes de "Gravar na pasta do processo"; o kit é só lido. Os botões da página passam
/// pela fila de comandos; só o "Cancelar" da espera não (ele interrompe o preparo que está na vez).</summary>
public sealed partial class MainWindow
{
    SessaoNovoDespacho? novo;
    ModelosDisponiveis? modelos;
    string? kitLido;
    Task tarefaDosModelos = Task.CompletedTask, tarefaDoNovo = Task.CompletedTask, tarefaDoRecibo = Task.CompletedTask;
    string? docxSemPdf;
    Entrega? ultimaEntrega;
    IConversorPdf? conversor;
    bool preenchendoNovo;
    /// <summary>A correção automática está em curso: o relatório é carregado sem o resumo da IA (entram as exigências como estão no relatório).</summary>
    bool automatico;

    PaginaNovoDespacho N => PaginaDeNovoDespacho;

    // ---------- acessores do roteiro ----------
    internal PaginaNovoDespacho NovoDespacho => PaginaDeNovoDespacho;
    internal SessaoNovoDespacho? DespachoEmEdicao => novo;
    /// <summary>Quem converte o DOCX em PDF: o LibreOffice do computador (o roteiro troca por um ausente para o caso "sem LibreOffice").</summary>
    internal IConversorPdf Conversor
    {
        // Um perfil do LibreOffice só, persistente, em <dados>\lo (Ruling T16-C4): a 1ª conversão inicializa o perfil; as seguintes o reaproveitam.
        get => conversor ??= new ConversorLibreOffice(ConversorLibreOffice.Localizar(ajustes.Soffice), raizPerfil: Path.Combine(PastaDados.Raiz, "lo"));
        set => conversor = value;
    }
    /// <summary>A data local de hoje (a da etapa nova); o roteiro fixa.</summary>
    internal Func<DateOnly> Hoje { get; set; } = () => DateOnly.FromDateTime(DateTime.Now);
    internal ModelosDisponiveis? ModelosDoKit => modelos;
    internal string PastaKitEfetiva => ajustes.PastaKitEfetiva;
    internal string? PastaKitEscolhida => ajustes.PastaKit;
    /// <summary>O DOCX do despacho escolhido em Despachos que o "Gerar PDF" completaria (null = comando desabilitado).</summary>
    internal string? DocxSemPdf => docxSemPdf;
    internal Entrega? UltimaEntrega => ultimaEntrega;
    internal InfoBar AvisoDespachoGravado => PaginaDeDespachos.AvisoGravado;
    internal Button BotaoAbrirPdfGravado => PaginaDeDespachos.BotaoAbrirPdfGravado;
    internal bool PaginaNovoDespachoVisivel => PaginaDeNovoDespacho.Visibility == Visibility.Visible;
    internal TextBlock TextoModelosNosAjustes => PaginaDeAjustes.TextoModelos;
    internal TextBlock TextoKitNosAjustes => PaginaDeAjustes.TextoPastaKit;

    /// <summary>Espera o que o Novo despacho tem em curso (registro do kit, troca de origem, recibo do despacho escolhido).</summary>
    internal async Task AguardarNovoDespachoAsync()
    {
        await tarefaDosModelos;
        await tarefaDoNovo;
        await tarefaDoRecibo;
    }

    void IniciarNovoDespacho()
    {
        foreach (var id in new[] { ComandosApp.DespachosNovoCorrecao, ComandosApp.DespachosNovoAprovacao })
        {
            var tipo = ComandosApp.TipoDoNovoDespacho(id)!.Value;
            manipuladores[id] = async () => await AbrirNovoDespachoAsync(tipo);
        }
        manipuladores[ComandosApp.DespachosNovoCorrecaoAutomatica] = async () => await AbrirCorrecaoAutomaticaAsync();
        manipuladores[ComandosApp.DespachosGerarPdf] = async () => { if (docxSemPdf is { } d) await AbrirGerarPdfAsync(d); };

        var n = N;
        foreach (var t in new[] { TipoDespacho.Correcao, TipoDespacho.Aprovacao })
            n.CaixaTipo.Items.Add(new ComboBoxItem { Content = RegistroModelos.Rotulo(t), Tag = t });
        n.CaixaTipo.SelectionChanged += (_, _) =>
        {
            if (!preenchendoNovo && n.CaixaTipo.SelectedItem is ComboBoxItem { Tag: TipoDespacho t }) NaOrigem(() => TrocarTipoNaVezAsync(t));
        };
        n.CaixaProcesso.SelectionChanged += (_, _) =>
        {
            if (!preenchendoNovo && n.CaixaProcesso.SelectedItem is ComboBoxItem { Tag: Processo p }) NaOrigem(() => TrocarProcessoNaVezAsync(p));
        };
        n.BotaoNovoProcesso.Click += (_, _) => NaOrigem(NovoProcessoNoDespachoAsync);
        n.BotaoSemProcesso.Click += (_, _) => NaOrigem(DespachoSemProcessoAsync);
        n.CaixaRelatorio.SelectionChanged += (_, _) =>
        {
            if (!preenchendoNovo && n.CaixaRelatorio.SelectedItem is ComboBoxItem { Tag: string c }) NaOrigem(() => TrocarRelatorioNaVezAsync(c));
        };
        n.CaixaEtapa.SelectionChanged += (_, _) =>
        {
            if (!preenchendoNovo && n.CaixaEtapa.SelectedItem is ComboBoxItem { Tag: EtapaDespacho e }) NaOrigem(() => TrocarEtapaNaVezAsync(e));
        };
        n.CaixaProtocolo.TextChanged += (_, _) => Editar(r => r.DefinirCabecalho("protocolo", n.CaixaProtocolo.Text));
        // Correções 2026-11-15: editar direto na prévia, exigência escrita à mão, pasta nova e instrução para a IA.
        n.PreviaEditada += (chave, texto) => EditarPelaPrevia(chave, texto);
        n.AjustarTextoComIa = AjustarTextoDaExigenciaAsync;
        n.BotaoAdicionarExigencia.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(async () =>
        {
            if (novo is not { Passo: PassoNovoDespacho.Conteudo, Rascunho: { } r } sessaoAtual) return;
            var texto = await Perguntas.Atual.PedirTextoAsync("Adicionar exigência", "Texto da exigência", "");
            if (string.IsNullOrWhiteSpace(texto) || !ReferenceEquals(novo, sessaoAtual)) return;
            r.AdicionarExigencia(texto.Trim());
            MontarExigencias(sessaoAtual);
            AtualizarPrevia();
        }));
        n.BotaoNovaPasta.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(CriarPastaNovaNaVezAsync));
        n.BotaoEscolherRelatorio.Click += (_, _) => NaOrigem(EscolherRelatorioNaVezAsync);
        n.BotaoAutomatico.Click += (_, _) => NaOrigem(AutomaticoNoPainelNaVezAsync);
        n.BotaoAplicarInstrucao.Click += async (_, _) => await AplicarInstrucaoAsync(n.CaixaInstrucao.Text);
        n.CaixaInstrucao.KeyDown += async (_, e) =>
        {
            if (e.Key != Windows.System.VirtualKey.Enter) return;
            e.Handled = true;
            await AplicarInstrucaoAsync(n.CaixaInstrucao.Text);
        };
        n.CaixaRequerente.TextChanged += (_, _) => Editar(r => r.DefinirCabecalho("requerente", n.CaixaRequerente.Text));
        n.CaixaAssunto.TextChanged += (_, _) => Editar(r => r.DefinirCabecalho("assunto", n.CaixaAssunto.Text));
        foreach (var (caixa, i) in new[] { (n.CaixaObraNova, Intervencao.ObraNova), (n.CaixaAlteracao, Intervencao.AlteracaoDeObra), (n.CaixaAmpliacao, Intervencao.AmpliacaoDeObra) })
        {
            caixa.Checked += (_, _) => Editar(r => r.DefinirIntervencao(i, true));
            caixa.Unchecked += (_, _) => Editar(r => r.DefinirIntervencao(i, false));
        }
        n.CaixaArea.TextChanged += (_, _) => Editar(r => r.DefinirArea(n.CaixaArea.Text));
        n.BotaoGerar.Click += async (_, _) => await GerarEConferirAsync();
        n.BotaoInterromper.Click += (_, _) => novo?.Interromper(); // fora da fila: o preparo que ele interrompe está na vez
        n.BotaoVoltar.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(() => { VoltarParaEdicao(); return Task.CompletedTask; }));
        n.BotaoGravar.Click += async (_, _) => await GravarDespachoAsync();
        n.BotaoCancelar.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(() => { FecharNovoDespacho(); return Task.CompletedTask; }));
        n.BotaoMarcarTodas.Click += (_, _) => { foreach (var c in n.CaixasDasPaginas) c.IsChecked = true; };

        var d = PaginaDeDespachos;
        d.BotaoAbrirDocxGravado.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(() => { AbrirNoPrograma(ultimaEntrega?.Docx); return Task.CompletedTask; }));
        d.BotaoAbrirPdfGravado.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(() => { AbrirNoPrograma(ultimaEntrega?.Pdf); return Task.CompletedTask; }));
        d.BotaoMostrarGravado.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(() =>
        {
            if (ultimaEntrega is { } e) AberturaExterna.MostrarNoExplorador(e.Docx);
            return Task.CompletedTask;
        }));

        PaginaDeAjustes.BotaoEscolherKit.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(async () =>
        {
            if (await Perguntas.Atual.EscolherPastaAsync() is { } pasta) DefinirPastaKit(pasta);
        }));
        PaginaDeAjustes.BotaoKitPadrao.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(() => { DefinirPastaKit(null); return Task.CompletedTask; }));
        MostrarKitNosAjustes();
    }

    /// <summary>Uma troca na origem (tipo, processo, relatório, etapa) entra na fila; o roteiro espera por <see cref="AguardarNovoDespachoAsync"/>.</summary>
    void NaOrigem(Func<Task> acao) => tarefaDoNovo = NaFilaAsync(() => TentarAsync(acao));

    // ---------- registro de modelos do kit e a faixa ----------

    /// <summary>Lê o registro do kit em segundo plano se a pasta do kit mudou desde a última leitura (ou se <paramref name="forcar"/>).</summary>
    Task AtualizarModelosAsync(bool forcar)
    {
        var kit = ajustes.PastaKitEfetiva;
        if (!forcar && kitLido == kit) return tarefaDosModelos;
        kitLido = kit;
        return tarefaDosModelos = LerModelosAsync(kit);
    }

    /// <summary>O registro de modelos (desenho §10.1), lido de novo; a faixa e a página Ajustes passam a mostrar o que ele diz.</summary>
    async Task<ModelosDisponiveis> LerModelosAsync(string kit)
    {
        ModelosDisponiveis m;
        try { m = await Task.Run(() => RegistroModelos.Ler(kit)); }
        catch (Exception e)
        {
            var motivo = "O registro de modelos do kit não pôde ser lido (" + Erro(e) + ").";
            m = new ModelosDisponiveis([], new Dictionary<TipoDespacho, string> { [TipoDespacho.Correcao] = motivo, [TipoDespacho.Aprovacao] = motivo });
        }
        if (fechada || !string.Equals(kit, ajustes.PastaKitEfetiva, StringComparison.Ordinal)) return m;
        modelos = m;
        kitLido = kit;
        MostrarKitNosAjustes();
        AtualizarComandos();
        return m;
    }

    /// <summary>Ajustes › Pasta do kit PMVNI (null = a padrão): vale na hora; o registro é lido de novo.</summary>
    internal void DefinirPastaKit(string? pasta)
    {
        ajustes.PastaKit = string.IsNullOrWhiteSpace(pasta) ? null : pasta;
        SalvarAjustes();
        modelos = null;
        MostrarKitNosAjustes();
        AtualizarComandos();
        _ = AtualizarModelosAsync(forcar: true);
        AtualizarAgentes();   // o kit tem a versão e a política do análise pro
    }

    void MostrarKitNosAjustes()
    {
        if (fechada) return;
        var p = PaginaDeAjustes;
        var padrao = string.IsNullOrWhiteSpace(ajustes.PastaKit);
        p.TextoPastaKit.Text = ajustes.PastaKitEfetiva + (padrao ? "  (pasta padrão)" : "");
        p.BotaoKitPadrao.IsEnabled = !padrao;
        p.TextoModelos.Text = modelos is not { } m ? "Lendo os modelos de despacho do kit…"
            : string.Join(Environment.NewLine, new[] { TipoDespacho.Correcao, TipoDespacho.Aprovacao }
                .Select(t => RegistroModelos.Rotulo(t) + ": " + (FluxoNovoDespacho.MotivoDesabilitado(t, m, false) ?? "disponível")));
    }

    void AtualizarComandosNovoDespacho()
    {
        var compondo = novo is not null;
        Faixa.Habilitar(ComandosApp.DespachosNovo, novo is not { Gravando: true });
        foreach (var (id, t) in new[] { (ComandosApp.DespachosNovoCorrecao, TipoDespacho.Correcao), (ComandosApp.DespachosNovoCorrecaoAutomatica, TipoDespacho.Correcao),
                     (ComandosApp.DespachosNovoAprovacao, TipoDespacho.Aprovacao) })
        {
            var motivo = modelos is { } m ? FluxoNovoDespacho.MotivoDesabilitado(t, m, novo is { Gravando: true }) : "Lendo os modelos do kit…";
            Faixa.Habilitar(id, motivo is null);
            Faixa.DefinirDica(id, motivo ?? (ComandosApp.EhCorrecaoAutomatica(id)
                ? "Despacho de correção com todos os itens pendentes do relatório mais recente do processo, direto para a conferência"
                : "Novo despacho de " + RegistroModelos.Rotulo(t).ToLower(Formatos.Cultura) + " a partir do relatório revisado"));
        }
        Faixa.Habilitar(ComandosApp.DespachosGerarPdf, !compondo && docxSemPdf is not null);
    }

    /// <summary>Despachos mostra a lista ou, com um despacho em edição, a página Novo despacho no lugar dela.</summary>
    void AtualizarPaginasDeDespachos()
    {
        var despachos = moduloAtual == Modulo.Despachos;
        PaginaDeDespachos.Visibility = despachos && novo is null ? Visibility.Visible : Visibility.Collapsed;
        PaginaDeNovoDespacho.Visibility = despachos && novo is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Gerar PDF: o despacho escolhido ----------

    /// <summary>O DOCX do despacho escolhido que tem recibo do editor com <c>sem_pdf_motivo</c> e ainda não tem o PDF (lido em segundo plano).</summary>
    void AtualizarDespachoSemPdf()
    {
        var escolhido = despachoEscolhido;
        docxSemPdf = null;
        if (escolhido is null) { tarefaDoRecibo = Task.CompletedTask; return; }
        tarefaDoRecibo = LerReciboAsync(escolhido);
    }

    async Task LerReciboAsync(LinhaDespacho escolhido)
    {
        string? docx = null;
        try { docx = await Task.Run(() => DocxSemPdfDe(escolhido.Despacho)); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        if (fechada || !ReferenceEquals(despachoEscolhido, escolhido)) return;
        docxSemPdf = docx;
        AtualizarComandos();
    }

    static string? DocxSemPdfDe(Despacho d)
    {
        foreach (var docx in d.Arquivos.Where(a => string.Equals(Path.GetExtension(a), ".docx", StringComparison.OrdinalIgnoreCase)))
        {
            var recibo = ReciboDespacho.CaminhoDe(docx);
            if (!File.Exists(recibo) || File.Exists(Path.ChangeExtension(docx, ".pdf"))) continue;
            try
            {
                var r = ReciboDespacho.Ler(recibo);
                if (r["recibo_versao"] is JsonValue v && v.TryGetValue<int>(out var versao) && versao == ReciboDespacho.Versao
                    && r["sem_pdf_motivo"] is JsonValue m && m.TryGetValue<string>(out var motivo) && motivo.Length > 0)
                    return docx;
            }
            catch (Exception e) when (e is IOException or JsonException or InvalidOperationException or InvalidDataException) { }
        }
        return null;
    }

    // ---------- abrir ----------

    /// <summary>"Novo despacho ▸ tipo" (o menu de qualquer módulo): na fila de comandos, com a origem do módulo atual.</summary>
    internal Task<bool> NovoDespachoAsync(TipoDespacho t) => NaFilaAsync(() => AbrirNovoDespachoAsync(t));

    /// <summary>Abre a página no módulo Despachos com a origem sugerida (<see cref="FluxoNovoDespacho.Sugerir"/>) e, se nada impede, já no
    /// passo 2. true = o rascunho está pronto para editar; false = a página ficou no passo 1 (o motivo à vista) ou não abriu.</summary>
    async Task<bool> AbrirNovoDespachoAsync(TipoDespacho t)
    {
        if (!automatico && novo is { SoPdf: false, Preparando: false, Gravando: false } atual
            && atual.Passo != PassoNovoDespacho.Conferencia && atual.Tipo != t)
        {
            await TrocarTipoNaVezAsync(t);
            return atual.Tipo == t && atual.Rascunho is not null;
        }
        if (!await SairDoNovoDespachoAsync()) return false;
        var kit = ajustes.PastaKitEfetiva;
        kitLido = kit;
        // O registro é a autoridade (desenho §10.1): lido de novo a cada abertura do Novo despacho.
        var leitura = LerModelosAsync(kit);
        tarefaDosModelos = leitura;
        var lidos = await leitura;
        if (FluxoNovoDespacho.MotivoDesabilitado(t, lidos, false) is { } motivo)
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Novo despacho indisponível", motivo);
            return false;
        }
        var origem = moduloAtual;
        var selecionado = origem switch
        {
            Modulo.Processos => processoEscolhido,
            Modulo.Despachos => despachoEscolhido?.Despacho.Processo ?? processoEscolhido,
            _ => null,
        };
        var aberto = origem == Modulo.Relatorios ? sessao?.Caminho : null;
        await MudarModuloAsync(Modulo.Despachos);
        if (moduloAtual != Modulo.Despachos) return false;

        var s = SessaoNovoDespacho.Novo(t, kit, Path.Combine(PastaDados.Raiz, "despachos"));
        novo = s;
        PaginaDeDespachos.AvisoGravado.IsOpen = false;
        N.Reiniciar("Novo despacho");
        AtualizarPaginasDeDespachos();
        AtualizarComandos();
        N.MostrarEspera("Lendo o processo e o relatório…", interromper: false);
        try
        {
            var raiz = ajustes.PastaProcessosEfetiva;
            var hoje = Hoje();
            var (processos, sugestao, modelo) = await Task.Run(() =>
            {
                EmissaoDespacho.LimparOrfas(s.RaizTrabalho);
                IReadOnlyList<Processo> lista = Directory.Exists(raiz) ? Processos.Listar(raiz, CancellationToken.None).Itens : [];
                var sug = FluxoNovoDespacho.Sugerir(origem, selecionado, aberto, lista, p => Processos.Detalhar(p, CancellationToken.None).Relatorios, hoje);
                return (lista, sug, SessaoNovoDespacho.LerModelo(lidos, t, kit));
            });
            if (!ReferenceEquals(novo, s)) return false;
            s.DefinirModelo(modelo);
            s.Processos = processos;
            PreencherOrigem(s);
            if (sugestao.Processo is not { } p)
            {
                N.AvisoMensagem.Severity = InfoBarSeverity.Informational;
                N.AvisoMensagem.Title = "Escolha o processo";
                N.AvisoMensagem.Message = processos.Count == 0 ? "Nenhum processo foi encontrado na pasta de processos (" + raiz + ")." : "Escolha na lista o processo do despacho.";
                N.AvisoMensagem.IsOpen = true;
                return false;
            }
            return await CarregarProcessoAsync(s, p, sugestao.Pacote, sugestao.Etapa);
        }
        finally
        {
            if (ReferenceEquals(novo, s)) N.MostrarEspera(null, false);
        }
    }

    /// <summary>O processo escolhido: os relatórios base e as etapas; depois o relatório (o sugerido ou o mais novo).</summary>
    async Task<bool> CarregarProcessoAsync(SessaoNovoDespacho s, Processo p, string? pacote, EtapaDespacho? etapa)
    {
        var hoje = Hoje();
        var (relatorios, etapas, sugerida) = await Task.Run(() => SessaoNovoDespacho.LerProcesso(p, hoje));
        if (!ReferenceEquals(novo, s)) return false;
        s.DefinirProcesso(p, relatorios, etapas, etapa ?? sugerida);
        pacote ??= FluxoNovoDespacho.MaisNovo(relatorios);
        PreencherOrigem(s);
        N.MostrarPasso(PassoNovoDespacho.Origem, false);
        N.MostrarBloqueios([]);
        // Despacho não depende de relatório (correções 2026-11-15): sem .rpmvni, começa em branco com o cabeçalho do processo.
        if (pacote is null) return MontarSemRelatorio(s);
        return await CarregarPacoteAsync(s, pacote);
    }

    /// <summary>O relatório base: sempre a versão gravada (o aberto com alterações é gravado antes); bloqueios param no passo 1; o aviso da
    /// aprovação pede confirmação; senão, o rascunho (passo 2).</summary>
    async Task<bool> CarregarPacoteAsync(SessaoNovoDespacho s, string caminho)
    {
        N.MostrarBloqueios([]);
        if (sessao is { } aberta && string.Equals(Path.GetFullPath(aberta.Caminho), Path.GetFullPath(caminho), StringComparison.OrdinalIgnoreCase)
            && aberta.Estado != EstadoGravacao.Salvo && !(aberta.Estado == EstadoGravacao.SomenteLeitura && !aberta.TemAlteracoesNaoGravadas))
        {
            if (!await GravarAgoraAsync())
            {
                if (ReferenceEquals(novo, s)) N.MostrarBloqueios([FluxoNovoDespacho.NaoSalvou]);
                return false;
            }
        }
        if (!ReferenceEquals(novo, s) || s.Processo is not { } processo) return false;
        PacoteAberto pacote;
        IReadOnlyList<string> bloqueios, avisos;
        try
        {
            var tipo = s.Tipo;
            (pacote, bloqueios, avisos) = await Task.Run(() =>
            {
                var lido = Pacote.Ler(caminho);
                return (lido, ConteudoDespacho.Bloqueios(tipo, lido.Conteudo.Projeto, processo, lido.SomenteLeitura),
                    ConteudoDespacho.Avisos(tipo, lido.Conteudo.Projeto));
            });
        }
        catch (Exception e) when (e is PacoteInvalidoException or IOException or UnauthorizedAccessException)
        {
            if (ReferenceEquals(novo, s)) N.MostrarBloqueios(["O relatório " + Path.GetFileName(caminho) + " não pôde ser lido: " + Erro(e)]);
            return false;
        }
        if (!ReferenceEquals(novo, s)) return false;
        s.DefinirPacote(caminho, pacote, bloqueios);
        PreencherOrigem(s);
        if (bloqueios.Count > 0)
        {
            N.MostrarBloqueios(bloqueios);
            MostrarPassoAtual(s);
            return false;
        }
        foreach (var aviso in avisos)
        {
            if (!await Perguntas.Atual.ConfirmarAsync("Emitir a aprovação?", aviso, "Emitir mesmo assim", "Voltar", destrutiva: false))
            {
                MostrarPassoAtual(s);
                return false;
            }
            s.AvisosConfirmados.Add(aviso);
        }
        if (!ReferenceEquals(novo, s)) return false;
        s.Montar();
        PreencherConteudo(s);
        MostrarPassoAtual(s);
        // Relatório anexado: a IA resume as exigências (só o que não atende) sozinha, se houver programa de IA e o ajuste estiver ligado.
        // Na correção automática não: entram as exigências como estão no relatório, e o despacho segue para a conferência.
        // Trocou o relatório com texto já escrito (manual ou editado): esse texto foi trazido e o resumo automático não passa por cima dele.
        if (s.Tipo == TipoDespacho.Correcao && !automatico && !s.TrouxeEdicoes && Perguntas.Atual is PerguntasDaJanela) _ = AplicarInstrucaoAsync(IaDespacho.PedidoResumo);
        return true;
    }

    // ---------- correção automática (2026-11-16) ----------

    /// <summary>"Novo despacho ▸ Correção automática" (de Relatórios, Processos ou Despachos), na fila de comandos: abre o Novo despacho de
    /// correção com o processo e o relatório da origem (o aberto em Relatórios, ou o mais recente do processo escolhido), monta todas as
    /// exigências dos itens pendentes (situação efetiva diferente de ATENDE; excluídos ficam de fora) e segue direto para a conferência.
    /// true = as páginas estão prontas para conferir; false = parou antes, com o motivo à vista.</summary>
    internal Task<bool> NovaCorrecaoAutomaticaAsync() => NaFilaAsync(AbrirCorrecaoAutomaticaAsync);

    async Task<bool> AbrirCorrecaoAutomaticaAsync()
    {
        automatico = true;
        try
        {
            // O despacho que já estava em edição só é trocado se o usuário aceitou descartá-lo (SairDoNovoDespachoAsync): com
            // "Continuar editando" (ou durante a gravação) a página não abriu uma sessão nova, e a correção automática não pode seguir
            // gerando e indo para a conferência em cima do rascunho antigo.
            var antes = novo;
            await AbrirNovoDespachoAsync(TipoDespacho.Correcao);
            if (novo is null || ReferenceEquals(novo, antes)) return false;
            return await ConcluirAutomaticoAsync();
        }
        finally { automatico = false; }
    }

    /// <summary>O botão "Criar automaticamente a partir do relatório" do painel (tipo Correção): usa o relatório escolhido na caixa ou, sem
    /// ele, o mais recente do processo; o que já foi editado só é trocado depois de confirmar.</summary>
    async Task AutomaticoNoPainelNaVezAsync()
    {
        if (novo is not { SoPdf: false, Tipo: TipoDespacho.Correcao } s || s.Passo == PassoNovoDespacho.Conferencia || s.Preparando) return;
        if (s.Processo is not null && FluxoNovoDespacho.TemEdicoes(s.Rascunho)
            && !await Perguntas.Atual.ConfirmarAsync("Substituir o despacho pelo relatório?",
                "As exigências e os campos que você já editou neste despacho serão trocados pelos do relatório.", "Substituir", "Continuar editando", destrutiva: true))
            return;
        if (!ReferenceEquals(novo, s)) return;
        automatico = true;
        try
        {
            if (s.Processo is not null && FluxoNovoDespacho.RelatorioDoAutomatico(s.Pacote is null ? null : s.CaminhoPacote, s.Relatorios) is { } caminho)
                await CarregarPacoteAsync(s, caminho);
            await ConcluirAutomaticoAsync();
        }
        finally { automatico = false; }
    }

    /// <summary>Depois de a origem ser lida: gera e mostra a conferência, ou para onde for preciso com a mensagem do
    /// <see cref="FluxoNovoDespacho.PlanejarAutomatico"/> (sem processo, sem relatório, relatório que não serve, falta preencher).</summary>
    async Task<bool> ConcluirAutomaticoAsync()
    {
        // A página não abriu (modelo indisponível, troca de módulo recusada): o motivo já está à vista.
        if (novo is not { SoPdf: false, Tipo: TipoDespacho.Correcao } s) return false;
        // Relatório ilegível ou que não pôde ser salvo: o motivo já está na faixa de bloqueio, mesmo sem a sessão ter o pacote.
        var plano = N.AvisoBloqueio.IsOpen && s.Bloqueios.Count == 0
            ? FluxoNovoDespacho.PlanejarAutomatico(s.Processo, "", [N.AvisoBloqueio.Message], null)
            : FluxoNovoDespacho.PlanejarAutomatico(s.Processo, s.Pacote is null ? null : s.CaminhoPacote, s.Bloqueios, s.Rascunho);
        if (plano.Destino == DestinoAutomatico.Conferir)
        {
            await GerarEConferirNaVezAsync();
            // Não gerou (erro ou cancelado): a mensagem do preparo fica; nada de "despacho criado".
            if (!ReferenceEquals(novo, s) || s.Passo != PassoNovoDespacho.Conferencia) return false;
        }
        N.AvisoMensagem.Severity = plano.Destino == DestinoAutomatico.Conferir ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
        N.AvisoMensagem.Title = plano.Titulo;
        N.AvisoMensagem.Message = plano.Mensagem;
        N.AvisoMensagem.IsOpen = true;
        return plano.Destino == DestinoAutomatico.Conferir && s.Preparacao is { Erros.Count: 0 };
    }

    // ---------- trocas na origem ----------

    async Task TrocarTipoNaVezAsync(TipoDespacho t)
    {
        if (novo is not { SoPdf: false } s || t == s.Tipo) return;
        if (modelos is not { } m || FluxoNovoDespacho.MotivoDesabilitado(t, m, false) is { } motivo)
        {
            N.AvisoMensagem.Severity = InfoBarSeverity.Warning;
            N.AvisoMensagem.Title = "Tipo indisponível";
            N.AvisoMensagem.Message = modelos is { } mm ? FluxoNovoDespacho.MotivoDesabilitado(t, mm, false)! : "Os modelos do kit ainda não foram lidos.";
            N.AvisoMensagem.IsOpen = true;
            PreencherOrigem(s);
            return;
        }
        var kit = s.RaizKit;
        var modelo = await Task.Run(() => SessaoNovoDespacho.LerModelo(m, t, kit));
        if (!ReferenceEquals(novo, s)) return;
        s.DefinirModelo(modelo);
        if (s.CaminhoPacote is { } c) await CarregarPacoteAsync(s, c);
        else if (s.Etapa is not null) MontarSemRelatorio(s);
        else { PreencherOrigem(s); MostrarPassoAtual(s); }
    }

    /// <summary>"Novo processo…" no Novo despacho (2026-11-17, pedido do usuário): cria a pasta e já a escolhe.</summary>
    async Task NovoProcessoNoDespachoAsync()
    {
        if (novo is not { SoPdf: false } s || await CriarPastaProcessoAsync() is not { } p) return;
        var mesmo = s.Processos.FirstOrDefault(x => string.Equals(x.Pasta, p.Pasta, StringComparison.OrdinalIgnoreCase));
        if (mesmo is null) { s.Processos = s.Processos.Append(p).ToList(); mesmo = p; }
        PreencherOrigem(s);
        await TrocarProcessoNaVezAsync(mesmo);
        PreencherOrigem(s);
    }

    /// <summary>"Sem processo" (2026-11-17, pedido do usuário: "libera o template para edição", sem caixa de diálogo): o despacho não fica
    /// ligado a um processo; o modelo abre na hora para preencher protocolo, requerente e o resto na folha, e ele é gravado em
    /// Documentos\Despachos sem processo (a etapa DESPACHO nn é criada lá).</summary>
    async Task DespachoSemProcessoAsync()
    {
        if (novo is not { SoPdf: false } s) return;
        var pasta = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Despachos sem processo");
        Directory.CreateDirectory(pasta);
        var p = new Processo(pasta, "Sem processo", null, null, null);
        var mesmo = s.Processos.FirstOrDefault(x => string.Equals(x.Pasta, p.Pasta, StringComparison.OrdinalIgnoreCase));
        if (mesmo is null) { s.Processos = s.Processos.Append(p).ToList(); mesmo = p; }
        PreencherOrigem(s);
        await TrocarProcessoNaVezAsync(mesmo);
        PreencherOrigem(s);
    }

    async Task TrocarProcessoNaVezAsync(Processo p)
    {
        if (novo is not { SoPdf: false } s || s.Processo == p) return;
        N.AvisoMensagem.IsOpen = false;
        await CarregarProcessoAsync(s, p, null, null);
    }

    bool MontarSemRelatorio(SessaoNovoDespacho s)
    {
        s.SemRelatorio();
        if (s.Etapa is null) return false;
        s.Montar();
        PreencherOrigem(s);
        PreencherConteudo(s);
        MostrarPassoAtual(s);
        return true;
    }

    async Task TrocarRelatorioNaVezAsync(string caminho)
    {
        if (caminho.Length == 0)
        {
            if (novo is { SoPdf: false } s0 && s0.Pacote is not null) MontarSemRelatorio(s0);
            return;
        }
        if (novo is not { SoPdf: false } s || string.Equals(s.CaminhoPacote, caminho, StringComparison.OrdinalIgnoreCase) && s.Pacote is not null) return;
        await CarregarPacoteAsync(s, caminho);
    }

    Task TrocarEtapaNaVezAsync(EtapaDespacho e)
    {
        if (novo is not { SoPdf: false } s || s.Etapa?.Nome == e.Nome) return Task.CompletedTask;
        s.DefinirEtapa(e);
        if (s.Rascunho is not null) PreencherConteudo(s);
        return Task.CompletedTask;
    }

    /// <summary>Roteiro: escolhe a pasta da etapa pelo nome, como na caixa, e espera a troca.</summary>
    internal async Task EscolherEtapaAsync(string nome)
    {
        var item = N.CaixaEtapa.Items.OfType<ComboBoxItem>().First(i => i.Tag is EtapaDespacho e && e.Nome == nome);
        N.CaixaEtapa.SelectedItem = item;
        await AguardarNovoDespachoAsync();
    }

    /// <summary>"Escolher arquivo…" do relatório base: o seletor (um <c>.rpmvni</c> de qualquer pasta ou o <c>dados.json</c> da análise).</summary>
    async Task EscolherRelatorioNaVezAsync()
    {
        if (novo is not { SoPdf: false, Processo: not null } s) return;
        var caminho = await Perguntas.Atual.AbrirArquivoAsync(FluxoNovoDespacho.FiltroArquivoBase);
        if (caminho is null || !ReferenceEquals(novo, s)) return;
        await UsarRelatorioEscolhidoAsync(s, caminho);
    }

    /// <summary>O arquivo escolhido à mão entra na lista e fica escolhido. O <c>dados.json</c> é importado antes, pelo Importador, para um
    /// <c>.rpmvni</c> novo no lugar que o usuário escolher (nada é gravado na pasta do processo sem ele escolher). true = o rascunho está pronto.</summary>
    internal async Task<bool> UsarRelatorioEscolhidoAsync(SessaoNovoDespacho s, string caminho)
    {
        switch (FluxoNovoDespacho.TipoDoArquivoBase(caminho))
        {
            case ArquivoBase.Pacote:
                break;
            case ArquivoBase.DadosJson:
                string? processo = null;
                try { processo = DadosRelatorio.DeBytes(await File.ReadAllBytesAsync(caminho), "").Processo; }
                catch (Exception e) when (e is JsonException or IOException) { /* a importação diz o que há de errado */ }
                var (num, ano) = NomesArquivo.DoProcessoDaCapa(processo);
                var pmvrel = await Perguntas.Atual.SalvarArquivoAsync(NomesArquivo.Importacao(num, ano), ".rpmvni");
                if (pmvrel is null || !DestinoNovo(pmvrel) || !await ImportarParaAsync(caminho, pmvrel, abrir: false)) return false;
                caminho = pmvrel;
                break;
            default:
                MostrarMensagem(InfoBarSeverity.Warning, "Arquivo não serve", FluxoNovoDespacho.ArquivoNaoServe);
                return false;
        }
        if (!ReferenceEquals(novo, s)) return false;
        if (!s.RelatoriosEscolhidos.Contains(caminho, StringComparer.OrdinalIgnoreCase)) s.RelatoriosEscolhidos.Add(caminho);
        PreencherOrigem(s);
        return await CarregarPacoteAsync(s, caminho);
    }

    /// <summary>Roteiro: escolhe o relatório base pelo caminho, como na caixa, e espera a leitura.</summary>
    internal async Task EscolherRelatorioAsync(string caminho)
    {
        var item = N.CaixaRelatorio.Items.OfType<ComboBoxItem>().First(i => i.Tag is string c && string.Equals(c, caminho, StringComparison.OrdinalIgnoreCase));
        N.CaixaRelatorio.SelectedItem = item;
        await AguardarNovoDespachoAsync();
    }

    // ---------- a página ----------

    void PreencherOrigem(SessaoNovoDespacho s)
    {
        if (fechada) return;
        var n = N;
        preenchendoNovo = true;
        try
        {
            n.CaixaTipo.SelectedItem = n.CaixaTipo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is TipoDespacho t && t == s.Tipo);
            n.CaixaTipo.IsEnabled = !s.SoPdf;
            n.CaixaProcesso.Items.Clear();
            foreach (var p in s.Processos)
                n.CaixaProcesso.Items.Add(new ComboBoxItem { Content = ListasPastas.NumeroDoProcesso(p) is { } num ? num + " · " + (p.Requerente ?? p.NomePasta) : p.NomePasta, Tag = p });
            n.CaixaProcesso.SelectedItem = n.CaixaProcesso.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is Processo p && p == s.Processo);
            n.CaixaRelatorio.Items.Clear();
            n.CaixaRelatorio.Items.Add(new ComboBoxItem { Content = "Sem relatório (escrever o despacho)", Tag = "" });
            foreach (var (caminho, escolhido) in FluxoNovoDespacho.RelatoriosDaLista(s.Relatorios, s.RelatoriosEscolhidos))
                n.CaixaRelatorio.Items.Add(new ComboBoxItem
                {
                    Content = Path.GetFileName(caminho)
                              + (sessao is { } a && string.Equals(a.Caminho, caminho, StringComparison.OrdinalIgnoreCase) ? "  (aberto em Relatórios)"
                                  : escolhido ? "  (escolhido)" : ""),
                    Tag = caminho,
                });
            n.CaixaRelatorio.SelectedItem = n.CaixaRelatorio.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is string c && string.Equals(c, s.CaminhoPacote ?? "", StringComparison.OrdinalIgnoreCase));
            n.CaixaEtapa.Items.Clear();
            foreach (var e in s.Etapas)
                n.CaixaEtapa.Items.Add(new ComboBoxItem { Content = e.Nome + (e.Nova ? "  (nova)" : ""), Tag = e });
            n.CaixaEtapa.SelectedItem = n.CaixaEtapa.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is EtapaDespacho e && e.Nome == s.Etapa?.Nome);
            n.CaixaProcesso.IsEnabled = n.CaixaRelatorio.IsEnabled = n.CaixaEtapa.IsEnabled = n.BotaoEscolherRelatorio.IsEnabled = !s.SoPdf;
            n.BotaoAutomatico.Visibility = !s.SoPdf && s.Tipo == TipoDespacho.Correcao ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { preenchendoNovo = false; }
    }

    /// <summary>Passo 2: o cabeçalho, as exigências (correção) ou o tipo de obra e a área (aprovação), a prévia e o que falta.</summary>
    void PreencherConteudo(SessaoNovoDespacho s)
    {
        if (s.Rascunho is not { } r || fechada) return;
        var n = N;
        n.CaixaProtocolo.Text = r.Protocolo;
        n.CaixaRequerente.Text = r.Requerente;
        n.CaixaAssunto.Text = r.Assunto;
        var aprovacao = r.Tipo == TipoDespacho.Aprovacao;
        n.PainelAprovacao.Visibility = aprovacao ? Visibility.Visible : Visibility.Collapsed;
        n.CaixaObraNova.IsChecked = r.Intervencoes.Contains(Intervencao.ObraNova);
        n.CaixaAlteracao.IsChecked = r.Intervencoes.Contains(Intervencao.AlteracaoDeObra);
        n.CaixaAmpliacao.IsChecked = r.Intervencoes.Contains(Intervencao.AmpliacaoDeObra);
        n.CaixaArea.Text = r.AreaTotal;
        MontarExigencias(s);
        AtualizarPrevia();
    }

    void MontarExigencias(SessaoNovoDespacho s)
    {
        if (s.Rascunho is not { } r) return;
        N.MostrarExigencias(r,
            (id, sim) => Editar(x => x.Marcar(id, sim)),
            (id, campo, texto) => Editar(x =>
            {
                switch (campo)
                {
                    case "grupo": x.DefinirGrupo(id, texto); break;
                    case "texto": x.DefinirTexto(id, texto); break;
                    default: x.DefinirFundamento(id, texto); break;
                }
            }),
            (id, delta) => _ = NaFilaAsync(() => TentarAsync(() =>
            {
                if (novo is { Passo: PassoNovoDespacho.Conteudo, Rascunho: { } atual } sessaoAtual && ReferenceEquals(atual, r))
                {
                    atual.Mover(id, delta);
                    MontarExigencias(sessaoAtual);
                    AtualizarPrevia();
                }
                return Task.CompletedTask;
            })));
    }

    /// <summary>Uma alteração no passo 2 vai para o rascunho (texto puro, uma linha) e a prévia é redesenhada.</summary>
    void Editar(Action<RascunhoDespacho> mudar)
    {
        if (novo is not { Passo: PassoNovoDespacho.Conteudo, Rascunho: { } r }) return;
        mudar(r);
        AtualizarPrevia();
    }

    void AtualizarPrevia()
    {
        if (novo is not { } s || fechada) return;
        N.MostrarPrevia(s.Previa(), s.Layout); // antes do rascunho, o modelo do kit com os campos a preencher (nunca a folha em branco)
        if (s.Rascunho is not { } r) return;
        N.MostrarFaltando(r.Faltando());
        N.BotaoGerar.IsEnabled = s.Passo == PassoNovoDespacho.Conteudo && FluxoNovoDespacho.PodeGerar(r) && !s.Preparando;
    }

    /// <summary>Um trecho editado direto na folha vai para o rascunho (<see cref="ConteudoDespacho.AplicarEdicao"/>): cabeçalho, data, título
    /// de grupo, texto ou fundamento da exigência, tipo de obra e área. O rascunho é o que vai para o DOCX e o PDF.</summary>
    internal void EditarPelaPrevia(string chave, string texto)
    {
        if (novo is not { Passo: PassoNovoDespacho.Conteudo, Rascunho: { } r } s) return;
        if (ConteudoDespacho.AplicarEdicao(r, chave, texto)) PreencherConteudo(s);
    }

    /// <summary>"Ajustar com IA" na caixa do texto da exigência: a mesma IA do "Ajustar o item com a IA" (Codex com o modelo e o esforço
    /// de Ajustes › Inteligência artificial, sem abrir arquivos; sem o Codex, o Claude Code), com o grupo, o título do item e o fundamento
    /// como contexto. Devolve a resposta crua; a caixa a limpa (<see cref="IaAjusteTexto.Ler"/>) e o usuário confere antes de gravar.</summary>
    async Task<string> AjustarTextoDaExigenciaAsync(string chave, string texto, CancellationToken ct)
    {
        var partes = chave.Split(':', 3);
        var pendencia = novo?.Rascunho?.Pendencias.FirstOrDefault(p => partes.Length >= 2 && p.ItemId == partes[1]);
        var prompt = IaAjusteTexto.Prompt(texto, pendencia?.Grupo, pendencia?.Titulo, pendencia?.Fundamento);
        var pecas = await LerPecasAsync();
        ct.ThrowIfCancellationRequested();
        // A IA, o modelo e o nível escolhidos em Ajustes › Inteligência artificial › Modelos de IA (2026-11-17).
        var ia = PrepararIa(pecas, ModeloIaAjuste.Limite);
        return await IaDespacho.PerguntarAsync(ia.Claude, ia.Codex, prompt, ct, null, null, ia.Opcoes);
    }

    /// <summary>"Nova pasta…": pede o nome e cria a pasta no processo; ela vira a pasta da etapa.</summary>
    async Task CriarPastaNovaNaVezAsync()
    {
        if (novo is not { SoPdf: false, Processo: { } processo } s) return;
        var sugestao = Pmvni.Revisao.Etapas.Nova(processo.Pasta, Hoje()).Nome;
        var nome = await Perguntas.Atual.PedirTextoAsync("Nova pasta do despacho", "Nome da pasta dentro do processo:", sugestao);
        if (string.IsNullOrWhiteSpace(nome) || !ReferenceEquals(novo, s)) return;
        nome = string.Concat(nome.Trim().Split(Path.GetInvalidFileNameChars())).Trim().TrimEnd('.', ' ');
        // "." e ".." (e um nome que só tinha caracteres proibidos) apontariam para a pasta do processo ou para fora dela.
        var raizProcesso = Path.GetFullPath(processo.Pasta).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var pasta = nome.Length == 0 ? "" : Path.GetFullPath(Path.Combine(processo.Pasta, nome));
        if (nome.Length == 0 || nome.Trim('.').Length == 0 || !pasta.StartsWith(raizProcesso, StringComparison.OrdinalIgnoreCase))
        {
            MostrarMensagem(InfoBarSeverity.Warning, "Nome de pasta inválido", "Escreva um nome de pasta (por exemplo, a data do despacho); ele não pode ser vazio, \".\" nem \"..\".");
            return;
        }
        try { Directory.CreateDirectory(pasta); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            MostrarMensagem(InfoBarSeverity.Error, "A pasta não foi criada", Erro(e));
            return;
        }
        var etapa = new EtapaDespacho(pasta, nome, s.Etapa?.Numero ?? 1, Hoje(), false);
        var (relatorios, etapas, _) = await Task.Run(() => SessaoNovoDespacho.LerProcesso(processo, Hoje()));
        var lista = etapas.Any(e => e.Nome == nome) ? etapas : etapas.Append(etapa).ToList();
        s.DefinirEtapa(lista.First(e => e.Nome == nome));
        s.AtualizarEtapas(lista);
        PreencherOrigem(s);
        if (s.Rascunho is not null) PreencherConteudo(s);
    }

    /// <summary>A caixa acima da prévia: a IA reescreve as exigências conforme a instrução (nada é gravado).</summary>
    async Task AplicarInstrucaoAsync(string instrucao)
    {
        if (novo is not { Passo: PassoNovoDespacho.Conteudo, Rascunho: { } r } s || instrucao.Trim().Length == 0) return;
        var n = N;
        n.BotaoAplicarInstrucao.Visibility = Visibility.Collapsed;
        n.AnelInstrucao.Visibility = Visibility.Visible;
        n.AnelInstrucao.IsActive = true;
        try
        {
            var pecas = await LerPecasAsync();
            var retrato = IaDespacho.Prompt(r, "");
            // A IA e o modelo de Ajustes › Inteligência artificial › Modelos de IA (2026-11-17), com o tempo-limite de sempre do despacho.
            var ia = PrepararIa(pecas, null);
            var saida = await IaDespacho.PerguntarAsync(ia.Claude, ia.Codex, IaDespacho.Prompt(r, instrucao), CancellationToken.None, null, null, ia.Opcoes);
            if (!ReferenceEquals(novo, s) || !ReferenceEquals(s.Rascunho, r)) return;
            if (IaDespacho.Prompt(r, "") != retrato)
            {
                MostrarMensagem(InfoBarSeverity.Warning, "Resposta da IA descartada", "O despacho foi editado enquanto a IA respondia; a resposta foi descartada para não apagar a sua edição. Aplique a instrução de novo.");
                return;
            }
            if (IaDespacho.Ler(saida) is not { } resp)
            {
                MostrarMensagem(InfoBarSeverity.Warning, "A IA não alterou o despacho", "A resposta da IA não veio no formato esperado; nada mudou.");
                return;
            }
            if (r.Tipo == TipoDespacho.Correcao && resp.Exigencias.Count == 0 && r.Pendencias.Any(x => x.Incluida))
            {
                MostrarMensagem(InfoBarSeverity.Warning, "A IA não alterou o despacho", "A IA devolveu o despacho sem nenhuma exigência; nada mudou. Reescreva a instrução se a ideia era tirar todas.");
                return;
            }
            if (resp.Protocolo is { Length: > 0 } pr) r.DefinirCabecalho("protocolo", pr);
            if (resp.Requerente is { Length: > 0 } rq) r.DefinirCabecalho("requerente", rq);
            if (resp.Assunto is { Length: > 0 } a) r.DefinirCabecalho("assunto", a);
            if (r.Tipo == TipoDespacho.Correcao) r.SubstituirExigencias(resp.Exigencias);
            n.CaixaInstrucao.Text = "";
            PreencherConteudo(s);
        }
        catch (Exception e)
        {
            // Auditoria 2026-11-16: nenhum erro da IA (JSON, tempo, rede, processo) derruba o editor; o detalhe vai para o registro.
            RegistroErros.Gravar("ia", e);
            MostrarMensagem(InfoBarSeverity.Error, "A IA não respondeu", e is InvalidOperationException or IOException ? e.Message : "A chamada à IA falhou: " + Erro(e));
        }
        finally
        {
            n.AnelInstrucao.IsActive = false;
            n.AnelInstrucao.Visibility = Visibility.Collapsed;
            n.BotaoAplicarInstrucao.Visibility = Visibility.Visible;
        }
    }

    void MostrarPassoAtual(SessaoNovoDespacho s)
    {
        if (fechada || !ReferenceEquals(novo, s)) return;
        N.MostrarPasso(s.Passo, s.SoPdf);
        if (s.Passo == PassoNovoDespacho.Conteudo) AtualizarPrevia();
    }

    // ---------- passo 3: gerar e conferir ----------

    /// <summary>"Gerar e conferir ▸": DOCX, validação e PDF na pasta de trabalho (nada na pasta do processo) e as páginas para conferir.</summary>
    internal Task GerarEConferirAsync() => NaFilaAsync(() => TentarAsync(GerarEConferirNaVezAsync));

    async Task GerarEConferirNaVezAsync()
    {
        if (novo is not { Passo: PassoNovoDespacho.Conteudo, Rascunho: { } r } s || !FluxoNovoDespacho.PodeGerar(r)) return;
        N.AvisoMensagem.IsOpen = false;
        N.LimparConferencia();
        // Assinatura digital (2026-11-16): com certificado configurado, a senha é pedida (ou lida do cofre) ANTES de gerar; cancelada ou
        // errada, nada é gerado nem gravado. Sem certificado, o despacho sai como sempre.
        var (certificado, desistir) = await PrepararAssinaturaAsync();
        if (desistir || !ReferenceEquals(novo, s)) { certificado?.Dispose(); return; }
        using var _ = certificado;
        var assinador = certificado is null ? null : new AssinadorComCertificado(certificado, AgoraDaAssinatura);
        N.MostrarEspera(certificado is null ? "Gerando o documento Word e o PDF do despacho…" : "Gerando o documento Word e o PDF do despacho e assinando…", interromper: true);
        try { await s.PrepararAsync(Conversor, assinador); }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(novo, s)) { N.MostrarEspera(null, false); MostrarPassoAtual(s); }
            return;
        }
        catch (Exception e)
        {
            if (!ReferenceEquals(novo, s)) return;
            N.MostrarEspera(null, false);
            MostrarMensagem(InfoBarSeverity.Error, "O despacho não pôde ser gerado", e is DespachoIncompletoException di ? string.Join(Environment.NewLine, di.Faltando) : Erro(e));
            MostrarPassoAtual(s);
            return;
        }
        if (!ReferenceEquals(novo, s)) { s.Descartar(); return; }
        N.MostrarEspera(null, false);
        await MostrarConferenciaAsync(s);
    }

    /// <summary>Passo 3 na tela: os erros da validação (e "Voltar e editar"), ou cada página do PDF com a sua caixa, ou o aviso de que sai só o DOCX.</summary>
    async Task MostrarConferenciaAsync(SessaoNovoDespacho s)
    {
        if (s.Preparacao is not { } p) return;
        var n = N;
        n.LimparConferencia();
        n.MostrarPasso(PassoNovoDespacho.Conferencia, s.SoPdf);
        n.TextoTituloConferencia.Text = s.SoPdf ? "Gerar PDF · " + Path.GetFileName(s.DocxGravado) : "3 · Conferência";
        if (p.Erros.Count > 0)
        {
            n.AvisoErros.Message = string.Join(Environment.NewLine, p.Erros);
            n.AvisoErros.IsOpen = true;
            AtualizarGravar(s);
            return;
        }
        if (p.Pdf is null)
        {
            n.AvisoSemPdf.Message = p.MensagemSemPdf ?? "";
            n.AvisoSemPdf.IsOpen = true;
            if (!s.SoPdf) n.BotaoGravar.Content = "Gravar só o Word (.docx)";
            AtualizarGravar(s);
            return;
        }
        n.TextoFontes.Text = "Fontes do PDF: " + string.Join(", ", p.Fontes) + (Conversor.Versao is { } v ? " · " + v : "")
                             + " · " + TextosAssinatura.NaConferencia(p.Assinatura);
        var escala = Raiz.XamlRoot?.RasterizationScale ?? 1.0;
        IReadOnlyList<Microsoft.UI.Xaml.Media.ImageSource> paginas;
        try { paginas = await s.PaginasAsync((uint)Math.Round(600 * escala)); }
        catch (Exception e)
        {
            MostrarMensagem(InfoBarSeverity.Error, "As páginas do PDF não puderam ser mostradas", Erro(e) + ". Sem conferir as páginas, o despacho não é gravado.");
            paginas = [];
        }
        if (!ReferenceEquals(novo, s)) return;
        n.MostrarPaginas(paginas, (pagina, conferida) =>
        {
            if (conferida) s.Conferidas.Add(pagina); else s.Conferidas.Remove(pagina);
            AtualizarGravar(s);
        });
        AtualizarGravar(s);
    }

    void AtualizarGravar(SessaoNovoDespacho s) =>
        N.BotaoGravar.IsEnabled = s.Preparacao is { } p && FluxoNovoDespacho.PodeGravar(p, s.Conferidas) && (!s.SoPdf || p.Pdf is not null)
                                  && (p.Pdf is null || N.CaixasDasPaginas.Count == p.Paginas);

    void VoltarParaEdicao()
    {
        if (novo is not { Passo: PassoNovoDespacho.Conferencia, SoPdf: false } s) return;
        s.VoltarParaEdicao();
        N.LimparConferencia();
        MostrarPassoAtual(s);
    }

    // ---------- gravar ----------

    /// <summary>"Gravar na pasta do processo" (ou "Gravar só o DOCX"): na fila. null se não gravou (o motivo à vista).</summary>
    internal Task<Entrega?> GravarDespachoAsync() => NaFilaAsync(GravarDespachoNaVezAsync);

    async Task<Entrega?> GravarDespachoNaVezAsync()
    {
        if (novo is not { Passo: PassoNovoDespacho.Conferencia, Preparacao: { } p } s) return null;
        if (!FluxoNovoDespacho.PodeGravar(p, s.Conferidas) || (s.SoPdf && p.Pdf is null)) return null;
        N.MostrarEspera("Gravando na pasta do processo…", interromper: false);
        Entrega e;
        try
        {
            e = s.SoPdf ? await s.GravarPdfAsync(Conversor.Versao, ContextoUsuario.Atual())
                : await s.GravarAsync(p.Pdf is null ? null : Conversor.Versao, ContextoUsuario.Atual(), VersaoDoApp());
        }
        catch (Exception x)
        {
            if (ReferenceEquals(novo, s)) N.MostrarEspera(null, false);
            MostrarMensagem(InfoBarSeverity.Error, "O despacho não foi gravado",
                Erro(x) + ". Nada desta gravação ficou na pasta do processo.");
            if (ReferenceEquals(novo, s)) AtualizarGravar(s);
            return null;
        }
        if (ReferenceEquals(novo, s)) N.MostrarEspera(null, false);
        ultimaEntrega = e;
        novo = null;
        AtualizarPaginasDeDespachos();
        var arquivos = new[] { e.Docx, e.Pdf, e.Recibo }.Count(a => a is not null);
        var aviso = PaginaDeDespachos.AvisoGravado;
        aviso.Title = "Despacho gravado";
        aviso.Message = $"{arquivos} arquivos em {e.Pasta}. Recibo: {Path.GetFileName(e.Recibo)}.";
        PaginaDeDespachos.BotaoAbrirPdfGravado.Visibility = e.Pdf is null ? Visibility.Collapsed : Visibility.Visible;
        aviso.IsOpen = true;
        // Ajustes › Despachos e PDF › "Abrir o PDF depois de gravar" (padrão: desligado, como sempre).
        if (ajustes.DespachoAbrirPdf && e.Pdf is { } pdfGravado) AbrirNoPrograma(pdfGravado);
        // Gerar despacho abre a pasta onde ele foi gravado (a DESPACHO NN do processo, regra do kit), com o PDF — ou o Word — selecionado.
        if ((e.Pdf ?? e.Docx) is { } gravado) AberturaExterna.MostrarNoExplorador(gravado);
        // A lista é relida (a pasta nova, ou o PDF novo, aparecem) e o despacho gravado fica escolhido.
        despachoEscolhido = null;
        tarefaDaLista = CarregarListasAsync(comDespachos: true);
        await tarefaDaLista;
        EscolherDespachoNaLista(l => string.Equals(Path.GetFullPath(l.Despacho.Pasta), Path.GetFullPath(e.Pasta), StringComparison.OrdinalIgnoreCase));
        await tarefaDoRecibo;
        AtualizarComandos();
        return e;
    }

    static string VersaoDoApp() =>
        typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? typeof(MainWindow).Assembly.GetName().Version?.ToString() ?? "?";

    // ---------- Gerar PDF ----------

    /// <summary>Despachos › Gerar PDF: abre a página direto no passo 3 com o PDF de uma cópia do DOCX gravado (que tem de conferir com o
    /// recibo) e grava só o PDF e o recibo atualizado. true = há páginas para conferir.</summary>
    internal Task<bool> GerarPdfDoDespachoAsync(string docx) => NaFilaAsync(() => AbrirGerarPdfAsync(docx));

    async Task<bool> AbrirGerarPdfAsync(string docx)
    {
        if (!await SairDoNovoDespachoAsync()) return false;
        await MudarModuloAsync(Modulo.Despachos);
        if (moduloAtual != Modulo.Despachos) return false;
        var s = SessaoNovoDespacho.ParaPdf(docx, Path.Combine(PastaDados.Raiz, "despachos"));
        novo = s;
        PaginaDeDespachos.AvisoGravado.IsOpen = false;
        N.Reiniciar("Gerar PDF");
        PreencherOrigem(s);
        N.MostrarPasso(PassoNovoDespacho.Conferencia, soPdf: true);
        AtualizarPaginasDeDespachos();
        AtualizarComandos();
        var (certificado, desistir) = await PrepararAssinaturaAsync();
        if (desistir || !ReferenceEquals(novo, s))
        {
            certificado?.Dispose();
            if (ReferenceEquals(novo, s)) FecharNovoDespacho();
            return false;
        }
        using var _ = certificado;
        N.MostrarEspera("Gerando o PDF do despacho…", interromper: true);
        try { await s.PrepararPdfAsync(Conversor, certificado is null ? null : new AssinadorComCertificado(certificado, AgoraDaAssinatura)); }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(novo, s)) FecharNovoDespacho();
            return false;
        }
        catch (Exception e)
        {
            if (ReferenceEquals(novo, s)) N.MostrarEspera(null, false);
            MostrarMensagem(InfoBarSeverity.Error, "O PDF não pôde ser gerado", Erro(e));
            return false;
        }
        if (!ReferenceEquals(novo, s)) { s.Descartar(); return false; }
        N.MostrarEspera(null, false);
        await MostrarConferenciaAsync(s);
        return s.Preparacao is { Pdf: not null, Erros.Count: 0 };
    }

    // ---------- fechar ----------

    /// <summary>Fecha a página sem gravar (a lista volta): o preparo para e a pasta de trabalho sai. Nada foi gravado na pasta do processo.</summary>
    void FecharNovoDespacho()
    {
        if (novo is not { } s) return;
        novo = null;
        s.Descartar();
        if (fechada) return;
        N.MostrarEspera(null, false);
        AtualizarPaginasDeDespachos();
        AtualizarComandos();
    }

    /// <summary>Trocar de módulo, abrir outro Novo despacho ou fechar o app: com a página no passo 2 ou 3, pergunta antes de descartar.
    /// true = pode seguir (a página foi fechada); false = "Continuar editando".</summary>
    async Task<bool> SairDoNovoDespachoAsync()
    {
        if (novo is not { } s) return true;
        if (s.Gravando)
        {
            // A gravação na pasta do processo está em curso: nada de "Nada foi gravado"; a troca fica para depois dela.
            MostrarMensagem(InfoBarSeverity.Informational, "Aguarde", FluxoNovoDespacho.GravandoAguarde);
            return false;
        }
        if (s.Passo is PassoNovoDespacho.Conteudo or PassoNovoDespacho.Conferencia || s.Preparando)
        {
            if (!await Perguntas.Atual.ConfirmarAsync("Descartar o despacho em edição?", "Nada foi gravado na pasta do processo.", "Descartar", "Continuar editando"))
                return false;
        }
        if (ReferenceEquals(novo, s)) FecharNovoDespacho();
        return true;
    }
}
