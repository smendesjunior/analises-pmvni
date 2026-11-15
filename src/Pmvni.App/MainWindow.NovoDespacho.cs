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
        n.CaixaRelatorio.SelectionChanged += (_, _) =>
        {
            if (!preenchendoNovo && n.CaixaRelatorio.SelectedItem is ComboBoxItem { Tag: string c }) NaOrigem(() => TrocarRelatorioNaVezAsync(c));
        };
        n.CaixaEtapa.SelectionChanged += (_, _) =>
        {
            if (!preenchendoNovo && n.CaixaEtapa.SelectedItem is ComboBoxItem { Tag: EtapaDespacho e }) NaOrigem(() => TrocarEtapaNaVezAsync(e));
        };
        n.CaixaProtocolo.TextChanged += (_, _) => Editar(r => r.DefinirCabecalho("protocolo", n.CaixaProtocolo.Text));
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
        Faixa.Habilitar(ComandosApp.DespachosNovo, !compondo);
        foreach (var (id, t) in new[] { (ComandosApp.DespachosNovoCorrecao, TipoDespacho.Correcao), (ComandosApp.DespachosNovoAprovacao, TipoDespacho.Aprovacao) })
        {
            var motivo = modelos is { } m ? FluxoNovoDespacho.MotivoDesabilitado(t, m, compondo) : compondo ? FluxoNovoDespacho.Compondo : "Lendo os modelos do kit…";
            Faixa.Habilitar(id, motivo is null);
            Faixa.DefinirDica(id, motivo ?? "Novo despacho de " + RegistroModelos.Rotulo(t).ToLower(Formatos.Cultura) + " a partir do relatório revisado");
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
        if (pacote is null)
        {
            N.MostrarBloqueios([FluxoNovoDespacho.SemPmvrel]);
            return false;
        }
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
        return true;
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
        else MostrarPassoAtual(s);
    }

    async Task TrocarProcessoNaVezAsync(Processo p)
    {
        if (novo is not { SoPdf: false } s || s.Processo == p) return;
        N.AvisoMensagem.IsOpen = false;
        await CarregarProcessoAsync(s, p, null, null);
    }

    async Task TrocarRelatorioNaVezAsync(string caminho)
    {
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
            foreach (var r in s.Relatorios)
                n.CaixaRelatorio.Items.Add(new ComboBoxItem
                {
                    Content = Path.GetFileName(r.Caminho) + (sessao is { } a && string.Equals(a.Caminho, r.Caminho, StringComparison.OrdinalIgnoreCase) ? "  (aberto em Relatórios)" : ""),
                    Tag = r.Caminho,
                });
            n.CaixaRelatorio.SelectedItem = n.CaixaRelatorio.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(i => i.Tag is string c && string.Equals(c, s.CaminhoPacote, StringComparison.OrdinalIgnoreCase));
            n.CaixaEtapa.Items.Clear();
            foreach (var e in s.Etapas)
                n.CaixaEtapa.Items.Add(new ComboBoxItem { Content = e.Nome + (e.Nova ? "  (nova)" : ""), Tag = e });
            n.CaixaEtapa.SelectedItem = n.CaixaEtapa.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is EtapaDespacho e && e.Nome == s.Etapa?.Nome);
            n.CaixaProcesso.IsEnabled = n.CaixaRelatorio.IsEnabled = n.CaixaEtapa.IsEnabled = !s.SoPdf;
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
        if (novo is not { Rascunho: { } r } s || fechada) return;
        N.MostrarPrevia(s.Previa());
        N.MostrarFaltando(r.Faltando());
        N.BotaoGerar.IsEnabled = s.Passo == PassoNovoDespacho.Conteudo && FluxoNovoDespacho.PodeGerar(r) && !s.Preparando;
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
        N.MostrarEspera("Gerando o DOCX e o PDF do despacho…", interromper: true);
        try { await s.PrepararAsync(Conversor); }
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
        n.TextoTituloConferencia.Text = s.SoPdf ? "Gerar PDF · " + Path.GetFileName(s.DocxGravado) : "3. Conferência";
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
            if (!s.SoPdf) n.BotaoGravar.Content = "Gravar só o DOCX";
            AtualizarGravar(s);
            return;
        }
        n.TextoFontes.Text = "Fontes do PDF: " + string.Join(", ", p.Fontes) + (Conversor.Versao is { } v ? " · " + v : "");
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
        N.MostrarEspera("Gerando o PDF do despacho…", interromper: true);
        try { await s.PrepararPdfAsync(Conversor); }
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
