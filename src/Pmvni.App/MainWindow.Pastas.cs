using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Pmvni.Composicao;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Módulos Processos e Despachos (Tarefa 13; desenho §9 e §9.1): as pastas de processos lidas em segundo plano (nunca na thread da
/// interface, e canceladas ao trocar de módulo ou atualizar), a lista de processos com o detalhe do escolhido, a lista de despachos que já
/// existem e as ações da faixa de cada um. A pasta é só lida: o app só cria nela o <c>.rpmvni</c> de uma importação pedida (nome livre, nunca
/// sobrescreve). Abrir arquivo, mostrar no Explorador e a importação passam pela fila de comandos como todo comando da faixa.</summary>
public sealed partial class MainWindow
{
    /// <summary>O resultado de uma leitura de pastas.</summary>
    sealed record LeituraDePastas(bool Existe, IReadOnlyList<LinhaProcesso> Processos, IReadOnlyList<Despacho> Despachos, IReadOnlyList<string> Avisos);

    CancellationTokenSource? leituraDaLista, leituraDoDetalhe;
    Task tarefaDaLista = Task.CompletedTask, tarefaDoDetalhe = Task.CompletedTask, tarefaDaPasta = Task.CompletedTask;
    IReadOnlyList<LinhaProcesso> linhasProcessos = [];
    IReadOnlyList<Despacho> todosDespachos = [];
    bool processosLidos, despachosLidos, pastaAusente, atualizandoListas;
    string? pastaLida;
    Processo? processoEscolhido;
    DetalheProcesso? detalheAtual;
    LinhaDetalhe? entradaEscolhida;
    LinhaDespacho? despachoEscolhido;
    OrdemProcessos ordemProcessos = OrdemProcessos.Numero;
    int? anoProcessos;
    FiltroTipoDespacho tipoDespachos = FiltroTipoDespacho.Todos;
    AgrupamentoDespachos agrupamentoDespachos = AgrupamentoDespachos.Data;
    bool despachosRecentesPrimeiro = true, despachosDoProcesso;
    readonly Dictionary<Modulo, string> termosPesquisa = new();

    // ---------- acessores do roteiro ----------
    internal IReadOnlyList<LinhaProcesso> ProcessosMostrados => PaginaDeProcessos.ListaProcessos.ItemsSource as IReadOnlyList<LinhaProcesso> ?? [];
    internal IReadOnlyList<LinhaDetalhe> DetalheMostrado => PaginaDeProcessos.ListaDetalhe.ItemsSource as IReadOnlyList<LinhaDetalhe> ?? [];
    internal IReadOnlyList<ItemDespacho> DespachosMostrados => PaginaDeDespachos.ListaDespachos.ItemsSource as IReadOnlyList<ItemDespacho> ?? [];
    internal string TextoContagemProcessos => PaginaDeProcessos.TextoContagem.Text;
    internal string TextoContagemDespachos => PaginaDeDespachos.TextoContagem.Text;
    internal InfoBar AvisoPastaProcessos => PaginaDeProcessos.AvisoPasta;
    internal InfoBar AvisoPastaDespachos => PaginaDeDespachos.AvisoPasta;
    internal Button BotaoAbrirAjustesProcessos => PaginaDeProcessos.BotaoAbrirAjustes;
    internal InfoBar MensagemDeProcessos => PaginaDeProcessos.AvisoMensagem;
    internal InfoBar MensagemDeDespachos => PaginaDeDespachos.AvisoMensagem;
    internal bool PaginaProcessosVisivel => PaginaDeProcessos.Visibility == Visibility.Visible;
    internal bool PaginaDespachosVisivel => PaginaDeDespachos.Visibility == Visibility.Visible;
    internal string DicaDaPesquisa => CaixaPesquisa.PlaceholderText;
    internal Processo? ProcessoEscolhido => processoEscolhido;
    internal string PastaProcessosEfetiva => ajustes.PastaProcessosEfetiva;
    internal string? PastaProcessosEscolhida => ajustes.PastaProcessos;
    internal LinhaDetalhe? EntradaEscolhida => entradaEscolhida;
    internal LinhaDespacho? DespachoEscolhido => despachoEscolhido;
    internal PaginaProcessos PaginaDoProcesso => PaginaDeProcessos;
    internal OrdemProcessos OrdemDeProcessos => ordemProcessos;
    internal int? AnoDeProcessos => anoProcessos;
    internal TextBlock TextoPastaNosAjustes => PaginaDeAjustes.TextoPastaProcessos;
    internal TextBlock TextoAvisoPastaNosAjustes => PaginaDeAjustes.TextoAvisoPasta;

    /// <summary>Espera a leitura da lista (e a do detalhe) que está em curso terminar; o roteiro usa antes de conferir a tela.</summary>
    internal async Task AguardarLeiturasAsync()
    {
        await tarefaDaLista;
        await tarefaDoDetalhe;
        await tarefaDaPasta;
        await tarefaDoRecibo;
    }

    void IniciarPastas()
    {
        var processos = PaginaDeProcessos;
        var despachos = PaginaDeDespachos;
        // Ajustes › Pastas e kit › "Listas ao abrir": ordem e pastas de Processos, agrupar e ordenar Despachos (Exibir muda até fechar).
        AplicarListasDosAjustes();
        processos.ListaProcessos.SelectionChanged += (_, _) =>
        {
            if (atualizandoListas) return;
            if (processos.ListaProcessos.SelectedItem is LinhaProcesso l) _ = TentarAsync(() => EscolherProcessoAsync(l.Processo));
        };
        processos.ListaDetalhe.SelectionChanged += (_, _) => AoEscolherEntrada();
        processos.ListaDetalhe.DoubleTapped += async (_, _) =>
        {
            if (entradaEscolhida is null) return;
            await ExecutarComandoAsync(entradaEscolhida.Tipo is TipoLinhaDetalhe.Relatorio or TipoLinhaDetalhe.Analise
                ? ComandosApp.ProcessosAbrirRelatorio : ComandosApp.ProcessosAbrirArquivo);
        };
        // Duplo clique, Enter ou menu de contexto num arquivo (nas colunas da Lista ou na grade de Miniaturas).
        processos.AcaoPedida += async acao => await FazerNoArquivoEscolhidoAsync(acao);
        // Agrupar, Classificar e Lista/Miniaturas da página do processo: a escolha fica lembrada nos Ajustes.
        processos.ExibicaoMudou += () =>
        {
            ajustes.ArquivosExibicao = processos.EmMiniaturas ? "miniaturas" : "lista";
            ajustes.ArquivosAgrupar = processos.Exibicao.AgruparNosAjustes;
            ajustes.ArquivosOrdenar = processos.Exibicao.OrdenarNosAjustes;
            ajustes.ArquivosDecrescente = processos.Exibicao.Decrescente;
            SalvarAjustes();
        };
        despachos.ListaDespachos.SelectionChanged += (_, _) => AoEscolherDespacho();
        despachos.ListaDespachos.DoubleTapped += async (_, _) => { if (despachoEscolhido is not null) await ExecutarComandoAsync(ComandosApp.DespachosAbrir); };
        processos.BotaoAbrirAjustes.Click += async (_, _) => await AbrirAjustesAsync();
        despachos.BotaoAbrirAjustes.Click += async (_, _) => await AbrirAjustesAsync();
        PaginaDeAjustes.BotaoEscolherPasta.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(EscolherPastaDeProcessosAsync));
        PaginaDeAjustes.BotaoPastaPadrao.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(() => TrocarPastaProcessosAsync(null)));
        AtualizarPaginaDePastas();

        manipuladores[ComandosApp.ProcessosAbrirRelatorio] = AbrirRelatorioDoProcessoAsync;
        manipuladores[ComandosApp.ProcessosImportarAnalise] = ImportarAnaliseDoProcessoAsync;
        manipuladores[ComandosApp.ProcessosAbrirArquivo] = () => { AbrirNoPrograma(entradaEscolhida?.Caminho); return Task.CompletedTask; };
        manipuladores[ComandosApp.ProcessosAbrirPasta] = () =>
        {
            if (processoEscolhido is { } p) AberturaExterna.MostrarNoExplorador(p.Pasta);
            return Task.CompletedTask;
        };
        // A leitura corre em segundo plano e não prende a fila de comandos (Salvar, por exemplo, não espera uma pasta grande).
        manipuladores[ComandosApp.ProcessosAtualizar] = () => { tarefaDaLista = CarregarListasAsync(comDespachos: false); return Task.CompletedTask; };
        manipuladores[ComandosApp.ProcessosOrdenarNumero] = () => DefinirOrdemDeProcessos(OrdemProcessos.Numero);
        manipuladores[ComandosApp.ProcessosOrdenarData] = () => DefinirOrdemDeProcessos(OrdemProcessos.Data);
        manipuladores[ComandosApp.ProcessosOrdenarRequerente] = () => DefinirOrdemDeProcessos(OrdemProcessos.Requerente);

        // «Abrir»: com um despacho escolhido, abre ele; sem escolha (a lista fica oculta), pergunta qual despacho (.docx ou .pdf) abrir.
        manipuladores[ComandosApp.DespachosAbrir] = async () =>
        {
            if (despachoEscolhido?.ArquivoParaAbrir is { } escolhido) { AbrirNoPrograma(escolhido); return; }
            if (await Perguntas.Atual.AbrirArquivoAsync(".docx;.pdf") is { } arquivo) AbrirNoPrograma(arquivo);
        };
        manipuladores[ComandosApp.DespachosAbrirPdf] = () => { AbrirNoPrograma(despachoEscolhido?.ArquivoPdf); return Task.CompletedTask; };
        manipuladores[ComandosApp.DespachosMostrar] = () =>
        {
            if (despachoEscolhido is { } d) AberturaExterna.MostrarNoExplorador(d.CaminhoNoExplorador);
            return Task.CompletedTask;
        };
        manipuladores[ComandosApp.DespachosIrAoProcesso] = IrAoProcessoDoDespachoAsync;
        manipuladores[ComandosApp.DespachosAtualizar] = () => { tarefaDaLista = CarregarListasAsync(comDespachos: true); return Task.CompletedTask; };
        manipuladores[ComandosApp.DespachosFiltroTodos] = () => DefinirFiltroDeDespachos(FiltroTipoDespacho.Todos);
        manipuladores[ComandosApp.DespachosFiltroCorrecao] = () => DefinirFiltroDeDespachos(FiltroTipoDespacho.Correcao);
        manipuladores[ComandosApp.DespachosFiltroAprovacao] = () => DefinirFiltroDeDespachos(FiltroTipoDespacho.Aprovacao);
        manipuladores[ComandosApp.DespachosDoProcesso] = () =>
        {
            despachosDoProcesso = !despachosDoProcesso;
            SincronizarPastas();
            RenderizarDespachos();
            return Task.CompletedTask;
        };
        manipuladores[ComandosApp.DespachosAgruparProcesso] = () => DefinirAgrupamentoDeDespachos(AgrupamentoDespachos.Processo);
        manipuladores[ComandosApp.DespachosAgruparData] = () => DefinirAgrupamentoDeDespachos(AgrupamentoDespachos.Data);
        manipuladores[ComandosApp.DespachosOrdenarRecentes] = () => DefinirOrdemDeDespachos(true);
        manipuladores[ComandosApp.DespachosOrdenarAntigos] = () => DefinirOrdemDeDespachos(false);
        SincronizarPastas();
    }

    void AplicarListasDosAjustes()
    {
        ordemProcessos = ajustes.ProcessosOrdem switch { "data" => OrdemProcessos.Data, "requerente" => OrdemProcessos.Requerente, _ => OrdemProcessos.Numero };
        agrupamentoDespachos = ajustes.DespachosAgrupar == "processo" ? AgrupamentoDespachos.Processo : AgrupamentoDespachos.Data;
        despachosRecentesPrimeiro = ajustes.DespachosOrdem != "antigos";
    }

    /// <summary>O que a página do processo pede para o arquivo escolhido: abrir (como o comando da faixa: o relatório no editor, a
    /// análise importada, o arquivo no programa dele), mostrar no Explorador ou copiar o caminho.</summary>
    async Task FazerNoArquivoEscolhidoAsync(AcaoArquivo acao)
    {
        if (entradaEscolhida is not { Caminho: { } caminho } entrada) return;
        switch (acao)
        {
            case AcaoArquivo.Abrir:
                await ExecutarComandoAsync(entrada.Tipo is TipoLinhaDetalhe.Relatorio or TipoLinhaDetalhe.Analise
                    ? ComandosApp.ProcessosAbrirRelatorio : ComandosApp.ProcessosAbrirArquivo);
                break;
            case AcaoArquivo.MostrarNoExplorador:
                AberturaExterna.MostrarNoExplorador(caminho);
                break;
            case AcaoArquivo.CopiarCaminho:
                try { AreaTransferencia.Copiar(caminho); }
                catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
                {
                    MostrarMensagem(InfoBarSeverity.Warning, "O caminho não foi copiado", Erro(e));
                }
                break;
        }
    }

    /// <summary>"Abrir Ajustes" do aviso de pasta ausente: Ajustes › Pastas e kit, onde a pasta se escolhe.</summary>
    Task AbrirAjustesAsync() => NaFilaAsync(() => TentarAsync(async () =>
    {
        await MudarModuloAsync(Modulo.Ajustes);
        PaginaDeAjustes.MostrarCategoria(CategoriasAjustes.Pastas);
    }));

    // ---------- entrada e saída de um módulo ----------

    /// <summary>Entrando em Processos ou Despachos: as leituras em curso (de outro módulo) são canceladas; a lista é lida se ainda não foi
    /// (ou se a pasta de processos mudou nos Ajustes) e o que já está lido é mostrado de novo, com o detalhe lido outra vez (um relatório
    /// importado desde a última visita aparece).</summary>
    void EntrarNoModuloDePastas(Modulo m)
    {
        CancelarLeituras();
        if (MensagemDe(m) is { } aviso) aviso.IsOpen = false;
        var emProcessos = m == Modulo.Processos;
        if (m is not (Modulo.Processos or Modulo.Despachos)) return;
        if (!ListasProntas(comDespachos: !emProcessos))
        {
            tarefaDaLista = CarregarListasAsync(comDespachos: !emProcessos);
            return;
        }
        RenderizarProcessos();
        RenderizarDespachos();
        if (emProcessos && processoEscolhido is { } p) tarefaDoDetalhe = CarregarDetalheAsync(p);
    }

    InfoBar? MensagemDe(Modulo m) => m switch
    {
        Modulo.Processos => PaginaDeProcessos.AvisoMensagem,
        Modulo.Despachos => PaginaDeDespachos.AvisoMensagem,
        _ => null,
    };

    bool ListasProntas(bool comDespachos) =>
        processosLidos && (!comDespachos || despachosLidos) && string.Equals(pastaLida, ajustes.PastaProcessosEfetiva, StringComparison.OrdinalIgnoreCase);

    void CancelarLeituras()
    {
        leituraDaLista?.Cancel();
        leituraDoDetalhe?.Cancel();
        PaginaDeProcessos.Anel.IsActive = PaginaDeDespachos.Anel.IsActive = false;
    }

    /// <summary>A pasta de processos mudou nos Ajustes: o que estava lido não vale mais (e o processo escolhido, que era da outra pasta, sai).</summary>
    internal void DefinirPastaProcessos(string? pasta)
    {
        ajustes.PastaProcessos = string.IsNullOrWhiteSpace(pasta) ? null : pasta;
        SalvarAjustes();
        CancelarLeituras();
        processosLidos = despachosLidos = false;
        pastaLida = null;
        linhasProcessos = [];
        todosDespachos = [];
        processoEscolhido = null;
        detalheAtual = null;
        entradaEscolhida = null;
        despachoEscolhido = null;
        docxSemPdf = null;
        pastaAusente = false;
        // O que está na tela era da outra pasta: as listas e o detalhe ficam vazios até a leitura nova.
        atualizandoListas = true;
        try
        {
            PaginaDeProcessos.ListaProcessos.ItemsSource = null;
            PaginaDeProcessos.ListaDetalhe.ItemsSource = null;
            PaginaDeDespachos.ListaDespachos.ItemsSource = null;
        }
        finally { atualizandoListas = false; }
        PaginaDeProcessos.TextoTituloDetalhe.Text = "";
        PaginaDeProcessos.TextoPastaDetalhe.Text = "";
        PaginaDeProcessos.TextoSemProcesso.Text = "Escolha um processo na lista.";
        PaginaDeProcessos.TextoSemProcesso.Visibility = Visibility.Visible;
        PaginaDeProcessos.TextoContagem.Text = PaginaDeDespachos.TextoContagem.Text = "";
        PaginaDeProcessos.TextoListaVazia.Visibility = PaginaDeDespachos.TextoListaVazia.Visibility = Visibility.Collapsed;
        AtualizarPaginaDePastas();
        AtualizarComandos();
    }

    /// <summary>Ajustes › Pasta de processos › Escolher pasta…: o seletor de pastas do Windows (o roteiro troca por uma resposta fixa).</summary>
    async Task EscolherPastaDeProcessosAsync()
    {
        var pasta = await Perguntas.Atual.EscolherPastaAsync();
        if (pasta is not null) await TrocarPastaProcessosAsync(pasta);
    }

    /// <summary>Trocar a pasta de processos (Ajustes) com um Novo despacho aberto: o processo dele é da pasta antiga, então a página é fechada
    /// antes (no passo 2 ou 3 pergunta; "Continuar editando" mantém a pasta e o despacho). true = a pasta mudou.</summary>
    internal async Task<bool> TrocarPastaProcessosAsync(string? pasta)
    {
        if (!await SairDoNovoDespachoAsync()) return false;
        DefinirPastaProcessos(pasta);
        return true;
    }

    /// <summary>Mostra na página Ajustes a pasta de processos em uso (e se ela é a padrão) e avisa, sem travar a tela, se ela não existe.</summary>
    void AtualizarPaginaDePastas()
    {
        if (fechada) return;
        var pagina = PaginaDeAjustes;
        var padrao = string.IsNullOrWhiteSpace(ajustes.PastaProcessos);
        pagina.TextoPastaProcessos.Text = ajustes.PastaProcessosEfetiva + (padrao ? "  (pasta padrão)" : "");
        pagina.BotaoPastaPadrao.IsEnabled = !padrao;
        tarefaDaPasta = ConferirPastaDeProcessosAsync(ajustes.PastaProcessosEfetiva);
    }

    async Task ConferirPastaDeProcessosAsync(string pasta)
    {
        var existe = await Task.Run(() => Directory.Exists(pasta));
        if (fechada || !string.Equals(pasta, ajustes.PastaProcessosEfetiva, StringComparison.Ordinal)) return;
        PaginaDeAjustes.TextoAvisoPasta.Text = existe ? "" : "A pasta não foi encontrada neste computador. Escolha a pasta de processos.";
        PaginaDeAjustes.TextoAvisoPasta.Visibility = existe ? Visibility.Collapsed : Visibility.Visible;
    }

    // ---------- leitura em segundo plano ----------

    /// <summary>Lê a pasta de processos em <see cref="Task.Run(Action)"/> e mostra o resultado; uma leitura nova cancela a anterior.
    /// Pasta ausente: a mensagem com o botão "Abrir Ajustes" nas duas páginas. A thread da interface só recebe o resultado pronto.</summary>
    internal async Task CarregarListasAsync(bool comDespachos)
    {
        CancelarLeituras();
        var origem = new CancellationTokenSource();
        leituraDaLista = origem;
        var ct = origem.Token;
        var raiz = ajustes.PastaProcessosEfetiva;
        PaginaDeProcessos.Anel.IsActive = PaginaDeDespachos.Anel.IsActive = true;
        PaginaDeProcessos.TextoContagem.Text = PaginaDeDespachos.TextoContagem.Text = "Lendo a pasta de processos…";
        try
        {
            var r = await Task.Run(() => LerPastas(raiz, comDespachos, ct), ct);
            if (ct.IsCancellationRequested || fechada) return;
            linhasProcessos = r.Processos;
            todosDespachos = r.Despachos;
            processosLidos = true;
            despachosLidos = comDespachos;
            pastaLida = raiz;
            pastaAusente = !r.Existe;
            MostrarAvisoDePasta(raiz);
            if (processoEscolhido is { } escolhido && !linhasProcessos.Any(l => string.Equals(l.Processo.Pasta, escolhido.Pasta, StringComparison.OrdinalIgnoreCase)))
            {
                processoEscolhido = null;
                detalheAtual = null;
                entradaEscolhida = null;
            }
            RenderizarProcessos();
            RenderizarDespachos();
            if (processoEscolhido is null) RenderizarDetalhe();
            if (r.Avisos.Count > 0)
                MostrarAvisoDeLeitura("Parte da pasta de processos não pôde ser lida", string.Join(Environment.NewLine, r.Avisos.Take(4)));
            if (moduloAtual == Modulo.Processos && processoEscolhido is { } p) tarefaDoDetalhe = CarregarDetalheAsync(p);
        }
        catch (OperationCanceledException) { /* trocou de módulo ou atualizou: a leitura nova assume */ }
        catch (Exception e) when (!fechada)
        {
            MostrarAvisoDeLeitura("Não foi possível ler a pasta de processos", Erro(e));
        }
        finally
        {
            if (ReferenceEquals(leituraDaLista, origem))
            {
                leituraDaLista = null;
                if (!fechada) PaginaDeProcessos.Anel.IsActive = PaginaDeDespachos.Anel.IsActive = false;
            }
            if (!fechada) AtualizarComandos();
        }
    }

    static LeituraDePastas LerPastas(string raiz, bool comDespachos, CancellationToken ct)
    {
        if (!Directory.Exists(raiz)) return new LeituraDePastas(false, [], [], []);
        var leitura = Processos.Listar(raiz, ct);
        var linhas = new List<LinhaProcesso>(leitura.Itens.Count);
        foreach (var p in leitura.Itens)
        {
            ct.ThrowIfCancellationRequested();
            DateTime alterado;
            try { alterado = Directory.GetLastWriteTimeUtc(p.Pasta); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { alterado = DateTime.MinValue; }
            linhas.Add(new LinhaProcesso(p, alterado));
        }
        var avisos = leitura.Avisos.ToList();
        IReadOnlyList<Despacho> despachos = [];
        if (comDespachos)
        {
            var d = Despachos.Listar(leitura.Itens, ct);
            despachos = d.Itens;
            avisos.AddRange(d.Avisos);
        }
        return new LeituraDePastas(true, linhas, despachos, avisos);
    }

    internal async Task EscolherProcessoAsync(Processo? p)
    {
        processoEscolhido = p;
        entradaEscolhida = null;
        detalheAtual = null;
        tarefaDoDetalhe = CarregarDetalheAsync(p);
        await tarefaDoDetalhe;
    }

    async Task CarregarDetalheAsync(Processo? p)
    {
        leituraDoDetalhe?.Cancel();
        var origem = new CancellationTokenSource();
        leituraDoDetalhe = origem;
        var ct = origem.Token;
        var pagina = PaginaDeProcessos;
        if (p is null)
        {
            RenderizarDetalhe();
            return;
        }
        entradaEscolhida = null;
        detalheAtual = null;
        pagina.TextoTituloDetalhe.Text = p.Requerente ?? p.NomePasta;
        pagina.TextoPastaDetalhe.Text = p.Pasta;
        pagina.ListaDetalhe.ItemsSource = null;
        pagina.TextoSemProcesso.Text = "Lendo o processo…";
        pagina.TextoSemProcesso.Visibility = Visibility.Visible;
        AtualizarComandos();
        try
        {
            var d = await Task.Run(() => Processos.Detalhar(p, ct), ct);
            if (ct.IsCancellationRequested || fechada) return;
            detalheAtual = d;
            RenderizarDetalhe();
            if (d.Avisos.Count > 0)
                MostrarAvisoDeLeitura("Parte do processo não pôde ser lida", string.Join(Environment.NewLine, d.Avisos.Take(4)));
        }
        catch (OperationCanceledException) { /* outro processo foi escolhido, ou trocou de módulo */ }
        catch (Exception e) when (!fechada)
        {
            pagina.TextoSemProcesso.Text = "O processo não pôde ser lido.";
            MostrarAvisoDeLeitura("Não foi possível ler o processo", Erro(e));
        }
    }

    void MostrarAvisoDeLeitura(string titulo, string mensagem)
    {
        var aviso = moduloAtual == Modulo.Despachos ? PaginaDeDespachos.AvisoMensagem : PaginaDeProcessos.AvisoMensagem;
        aviso.Severity = InfoBarSeverity.Warning;
        aviso.Title = titulo;
        aviso.Message = mensagem;
        aviso.IsOpen = true;
    }

    void MostrarAvisoDePasta(string raiz)
    {
        var texto = $"A pasta de processos não foi encontrada: {raiz}. Escolha a pasta em Ajustes.";
        foreach (var aviso in new[] { PaginaDeProcessos.AvisoPasta, PaginaDeDespachos.AvisoPasta })
        {
            aviso.Message = texto;
            aviso.IsOpen = pastaAusente;
        }
    }

    // ---------- listas na tela ----------

    void RenderizarProcessos()
    {
        if (fechada) return;
        var pagina = PaginaDeProcessos;
        var termo = TermoDe(Modulo.Processos);
        var mostradas = ListasPastas.FiltrarProcessos(linhasProcessos, termo, anoProcessos, ordemProcessos);
        atualizandoListas = true;
        try
        {
            pagina.ListaProcessos.ItemsSource = mostradas;
            pagina.ListaProcessos.SelectedItem = processoEscolhido is null ? null
                : mostradas.FirstOrDefault(l => string.Equals(l.Processo.Pasta, processoEscolhido.Pasta, StringComparison.OrdinalIgnoreCase));
        }
        finally { atualizandoListas = false; }
        var filtrado = termo.Trim().Length > 0 || anoProcessos is not null;
        pagina.TextoContagem.Text = pastaAusente ? "" : (filtrado ? $"{mostradas.Count} de {linhasProcessos.Count} " : $"{linhasProcessos.Count} ")
            + (linhasProcessos.Count == 1 ? "processo" : "processos") + (anoProcessos is { } a ? $" · {a}" : "");
        pagina.TextoListaVazia.Visibility = !pastaAusente && processosLidos && mostradas.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        pagina.TextoListaVazia.Text = filtrado ? "Nenhum processo combina com o filtro." : "Nenhum processo foi encontrado nesta pasta.";
        AtualizarTextoDaPesquisaDeLista();
        AtualizarComandos();
    }

    void RenderizarDetalhe()
    {
        if (fechada) return;
        var pagina = PaginaDeProcessos;
        atualizandoListas = true;
        try
        {
            if (processoEscolhido is null)
            {
                pagina.TextoTituloDetalhe.Text = "";
                pagina.TextoPastaDetalhe.Text = "";
                pagina.ListaDetalhe.ItemsSource = null;
                pagina.TextoSemProcesso.Text = "Escolha um processo na lista.";
                pagina.TextoSemProcesso.Visibility = Visibility.Visible;
                entradaEscolhida = null;
            }
            else if (detalheAtual is { } d && string.Equals(d.Processo.Pasta, processoEscolhido.Pasta, StringComparison.OrdinalIgnoreCase))
            {
                pagina.TextoTituloDetalhe.Text = d.Processo.Requerente ?? d.Processo.NomePasta;
                pagina.TextoPastaDetalhe.Text = d.Processo.Pasta;
                pagina.ListaDetalhe.ItemsSource = ListasPastas.Detalhe(d, true) /* a coluna Projeto e documentos lista sempre PROJETO e DOCUMENTOS */;
                pagina.TextoSemProcesso.Visibility = Visibility.Collapsed;
                entradaEscolhida = null;
            }
        }
        finally { atualizandoListas = false; }
        AtualizarComandos();
    }

    void RenderizarDespachos()
    {
        if (fechada) return;
        var pagina = PaginaDeDespachos;
        IEnumerable<Despacho> fonte = todosDespachos;
        if (despachosDoProcesso && processoEscolhido is { } p)
            fonte = fonte.Where(d => d.Processo is { } dp && string.Equals(dp.Pasta, p.Pasta, StringComparison.OrdinalIgnoreCase));
        var grupos = ListasPastas.OrganizarDespachos(fonte, tipoDespachos, TermoDe(Modulo.Despachos), agrupamentoDespachos, despachosRecentesPrimeiro,
            linhasProcessos.Select(l => l.Processo).ToList());
        var itens = ListasPastas.Achatar(grupos);
        var quantos = itens.Count(i => i.EhDespacho);
        atualizandoListas = true;
        try
        {
            pagina.ListaDespachos.ItemsSource = itens;
            pagina.ListaDespachos.SelectedItem = despachoEscolhido is null ? null
                : itens.FirstOrDefault(i => i.Linha is { } l && string.Equals(l.Despacho.Pasta, despachoEscolhido.Despacho.Pasta, StringComparison.OrdinalIgnoreCase));
            // Correção 2026-11-16 (2): a lista fica oculta (Despachos só cria), então nada é escolhido sozinho; antes o primeiro
            // despacho (de outro processo) vinha escolhido e «Abrir» abria ele.
            if (pagina.ListaDespachos.SelectedItem is null) despachoEscolhido = null;
        }
        finally { atualizandoListas = false; }
        AtualizarDespachoSemPdf();
        var escopo = despachosDoProcesso && processoEscolhido is { } pe
            ? " · só do processo " + (ListasPastas.NumeroDoProcesso(pe) ?? pe.NomePasta) : "";
        pagina.TextoContagem.Text = pastaAusente ? "" : $"{quantos} {(quantos == 1 ? "despacho" : "despachos")}{escopo}";
        pagina.TextoListaVazia.Visibility = !pastaAusente && despachosLidos && quantos == 0 ? Visibility.Visible : Visibility.Collapsed;
        pagina.TextoListaVazia.Text = despachosDoProcesso && processoEscolhido is null
            ? "Escolha um processo em Processos para ver só os despachos dele."
            : TermoDe(Modulo.Despachos).Trim().Length > 0 || tipoDespachos != FiltroTipoDespacho.Todos
                ? "Nenhum despacho combina com o filtro." : "Nenhum despacho foi encontrado nesta pasta.";
        AtualizarTextoDaPesquisaDeLista();
        AtualizarComandos();
    }

    void AoEscolherEntrada()
    {
        if (atualizandoListas) return;
        var lista = PaginaDeProcessos.ListaDetalhe;
        if (lista.SelectedItem is LinhaDetalhe { Selecionavel: true } l) entradaEscolhida = l;
        else
        {
            entradaEscolhida = null;
            if (lista.SelectedItem is not null) // título ou aviso de "nenhum": não se escolhe
            {
                atualizandoListas = true;
                try { lista.SelectedItem = null; }
                finally { atualizandoListas = false; }
            }
        }
        AtualizarComandos();
    }

    void AoEscolherDespacho()
    {
        if (atualizandoListas) return;
        var lista = PaginaDeDespachos.ListaDespachos;
        if (lista.SelectedItem is ItemDespacho { Linha: { } l }) despachoEscolhido = l;
        else
        {
            despachoEscolhido = null;
            if (lista.SelectedItem is not null)
            {
                atualizandoListas = true;
                try { lista.SelectedItem = null; }
                finally { atualizandoListas = false; }
            }
        }
        AtualizarDespachoSemPdf();
        AtualizarComandos();
    }

    /// <summary>Roteiro e atalhos: escolhe na lista o processo cuja pasta começa com <paramref name="inicioDoNome"/> (como um clique) e espera o detalhe.</summary>
    internal async Task EscolherProcessoNaListaAsync(string inicioDoNome)
    {
        var l = ProcessosMostrados.First(x => x.Processo.NomePasta.StartsWith(inicioDoNome, StringComparison.Ordinal));
        PaginaDeProcessos.ListaProcessos.SelectedItem = l;
        await AguardarLeiturasAsync();
    }

    /// <summary>Escolhe na lista de detalhes a primeira linha que satisfaz <paramref name="criterio"/> (como um clique); false se nenhuma.</summary>
    internal bool EscolherEntradaNaLista(Func<LinhaDetalhe, bool> criterio)
    {
        var l = DetalheMostrado.FirstOrDefault(criterio);
        PaginaDeProcessos.ListaDetalhe.SelectedItem = l;
        return l is not null && ReferenceEquals(entradaEscolhida, l);
    }

    /// <summary>Escolhe na lista de despachos o primeiro que satisfaz <paramref name="criterio"/> (como um clique); false se nenhum.</summary>
    internal bool EscolherDespachoNaLista(Func<LinhaDespacho, bool> criterio)
    {
        var item = DespachosMostrados.FirstOrDefault(i => i.Linha is { } l && criterio(l));
        PaginaDeDespachos.ListaDespachos.SelectedItem = item;
        return item is not null && ReferenceEquals(despachoEscolhido, item.Linha);
    }

    // ---------- pesquisa da barra de título nas listas ----------

    string TermoDe(Modulo m) => termosPesquisa.TryGetValue(m, out var t) ? t : "";

    /// <summary>Digitou na caixa em Processos ou Despachos: guarda o termo do módulo e filtra a lista (em memória, sem esperar).</summary>
    void AplicarTermoDeLista(string termo)
    {
        termosPesquisa[moduloAtual] = termo;
        if (moduloAtual == Modulo.Processos) RenderizarProcessos();
        else if (moduloAtual == Modulo.Despachos) RenderizarDespachos();
    }

    /// <summary>"1 de 4": quantos aparecem de quantos há, enquanto houver termo na caixa (vazio, nada).</summary>
    void AtualizarTextoDaPesquisaDeLista()
    {
        if (fechada || moduloAtual is not (Modulo.Processos or Modulo.Despachos)) return;
        var termo = TermoDe(moduloAtual);
        if (termo.Trim().Length == 0) { TextoPesquisa.Text = ""; return; }
        var (mostrados, total) = moduloAtual == Modulo.Processos
            ? (ProcessosMostrados.Count, linhasProcessos.Count)
            : (DespachosMostrados.Count(i => i.EhDespacho), todosDespachos.Count);
        TextoPesquisa.Text = mostrados == 0 ? "Nenhum resultado"
            : $"{mostrados.ToString(CultureInfo.InvariantCulture)} de {total.ToString(CultureInfo.InvariantCulture)}";
    }

    // ---------- comandos de Processos ----------

    /// <summary>A análise da IA a importar: a linha escolhida, se for uma; senão, a mais recente do processo.</summary>
    LinhaDetalhe? AnaliseParaImportar()
    {
        if (entradaEscolhida is { Tipo: TipoLinhaDetalhe.Analise } e) return e;
        return DetalheMostrado.FirstOrDefault(l => l.Tipo == TipoLinhaDetalhe.Analise);
    }

    Task AbrirRelatorioDoProcessoAsync() => entradaEscolhida switch
    {
        { Tipo: TipoLinhaDetalhe.Relatorio, Caminho: { } c } => AbrirPacoteAsync(c),
        { Tipo: TipoLinhaDetalhe.Analise, Caminho: { } c } when processoEscolhido is { } p => ImportarAnaliseAsync(p, c),
        _ => Task.CompletedTask,
    };

    Task ImportarAnaliseDoProcessoAsync() =>
        processoEscolhido is { } p && AnaliseParaImportar() is { Caminho: { } c } ? ImportarAnaliseAsync(p, c) : Task.CompletedTask;

    /// <summary>"Importar análise da IA": o <c>dados*.json</c> do processo vira um <c>.rpmvni</c> novo na pasta do processo, com o nome
    /// <c>Relatorio_NNNN_AAAA.rpmvni</c> (o do processo; se já existe, <c>_2</c>, <c>_3</c>…: nunca sobrescreve) e abre no módulo Relatórios.</summary>
    internal async Task<bool> ImportarAnaliseAsync(Processo p, string json) => await ImportarAnaliseParaAsync(p, json, abrir: true) is not null;

    /// <summary>Como <see cref="ImportarAnaliseAsync"/>; devolve o <c>.rpmvni</c> criado (null = não importou). <paramref name="abrir"/> = false: não abre
    /// (o relatório fica nos Recentes e na pasta do processo).</summary>
    internal async Task<string?> ImportarAnaliseParaAsync(Processo p, string json, bool abrir)
    {
        string destino;
        try
        {
            destino = await Task.Run(() =>
            {
                string? capa = null;
                try { capa = DadosRelatorio.DeBytes(File.ReadAllBytes(json), "").Processo; }
                catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException) { /* a importação diz o que há de errado */ }
                return Processos.NomeImportacao(p, capa, Hoje());
            });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível importar a análise", Erro(e));
            return null;
        }
        return await ImportarParaAsync(json, destino, abrir) ? destino : null;
    }

    void AbrirNoPrograma(string? caminho)
    {
        if (caminho is null) return;
        if (!AberturaExterna.Abrir(caminho, out var motivo))
            MostrarMensagem(InfoBarSeverity.Warning, "O arquivo não foi aberto", motivo ?? "O arquivo não pôde ser aberto.");
    }

    Task DefinirOrdemDeProcessos(OrdemProcessos ordem)
    {
        ordemProcessos = ordem;
        SincronizarPastas();
        RenderizarProcessos();
        return Task.CompletedTask;
    }

    /// <summary>O ano escolhido no menu Ano ("" = todos os anos). Entra na fila de comandos, como um item de menu da faixa.</summary>
    internal Task DefinirAnoAsync(string valor) => NaFilaAsync(() => TentarAsync(() =>
    {
        anoProcessos = int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var a) ? a : null;
        SincronizarPastas();
        RenderizarProcessos();
        return Task.CompletedTask;
    }));

    // ---------- comandos de Despachos ----------

    async Task IrAoProcessoDoDespachoAsync()
    {
        if (despachoEscolhido?.Despacho.Processo is not { } p) return;
        // O processo tem de estar à vista na lista: sem filtro de ano nem de termo.
        anoProcessos = null;
        termosPesquisa[Modulo.Processos] = "";
        processoEscolhido = p;
        entradaEscolhida = null;
        detalheAtual = null;
        SincronizarPastas();
        await MudarModuloAsync(Modulo.Processos); // a leitura do detalhe segue em segundo plano
    }

    Task DefinirFiltroDeDespachos(FiltroTipoDespacho tipo)
    {
        tipoDespachos = tipo;
        SincronizarPastas();
        RenderizarDespachos();
        return Task.CompletedTask;
    }

    Task DefinirAgrupamentoDeDespachos(AgrupamentoDespachos agrupamento)
    {
        agrupamentoDespachos = agrupamento;
        SincronizarPastas();
        RenderizarDespachos();
        return Task.CompletedTask;
    }

    Task DefinirOrdemDeDespachos(bool recentesPrimeiro)
    {
        despachosRecentesPrimeiro = recentesPrimeiro;
        SincronizarPastas();
        RenderizarDespachos();
        return Task.CompletedTask;
    }

    // ---------- faixa ----------

    /// <summary>Marca (✓) nos menus e liga os botões que alternam, de acordo com o que está escolhido.</summary>
    void SincronizarPastas()
    {
        // A página do processo segue os Ajustes (ao abrir o app e depois de "Restaurar os ajustes"); igual ao que já mostra, nada muda.
        PaginaDeProcessos.DefinirExibicao(
            ExibicaoArquivos.DosAjustes(ajustes.ArquivosAgrupar, ajustes.ArquivosOrdenar, ajustes.ArquivosDecrescente), ajustes.ArquivosExibicao == "miniaturas");
        Faixa.MarcarItem(ComandosApp.ProcessosOrdenarNumero, ordemProcessos == OrdemProcessos.Numero);
        Faixa.MarcarItem(ComandosApp.ProcessosOrdenarData, ordemProcessos == OrdemProcessos.Data);
        Faixa.MarcarItem(ComandosApp.ProcessosOrdenarRequerente, ordemProcessos == OrdemProcessos.Requerente);
        Faixa.MarcarItem(ComandosApp.DespachosFiltroTodos, tipoDespachos == FiltroTipoDespacho.Todos);
        Faixa.MarcarItem(ComandosApp.DespachosFiltroCorrecao, tipoDespachos == FiltroTipoDespacho.Correcao);
        Faixa.MarcarItem(ComandosApp.DespachosFiltroAprovacao, tipoDespachos == FiltroTipoDespacho.Aprovacao);
        Faixa.Ligar(ComandosApp.DespachosDoProcesso, despachosDoProcesso);
        Faixa.MarcarItem(ComandosApp.DespachosAgruparProcesso, agrupamentoDespachos == AgrupamentoDespachos.Processo);
        Faixa.MarcarItem(ComandosApp.DespachosAgruparData, agrupamentoDespachos == AgrupamentoDespachos.Data);
        Faixa.MarcarItem(ComandosApp.DespachosOrdenarRecentes, despachosRecentesPrimeiro);
        Faixa.MarcarItem(ComandosApp.DespachosOrdenarAntigos, !despachosRecentesPrimeiro);
    }

    /// <summary>Habilita cada comando pelo contexto: o que está escolhido nas listas (sem tocar o disco) e se o app está livre.</summary>
    void AtualizarComandosPastas(bool pronto)
    {
        Faixa.Habilitar(ComandosApp.ProcessosAbrirRelatorio, pronto && entradaEscolhida is { Tipo: TipoLinhaDetalhe.Relatorio or TipoLinhaDetalhe.Analise });
        Faixa.Habilitar(ComandosApp.ProcessosImportarAnalise, pronto && processoEscolhido is not null && AnaliseParaImportar() is not null);
        Faixa.Habilitar(ComandosApp.ProcessosAbrirArquivo, entradaEscolhida is { Tipo: TipoLinhaDetalhe.ArquivoDeDespacho or TipoLinhaDetalhe.Arquivo });
        Faixa.Habilitar(ComandosApp.ProcessosAbrirPasta, processoEscolhido is not null);
        Faixa.Habilitar(ComandosApp.ProcessosAtualizar, true);
        Faixa.Habilitar(ComandosApp.ProcessosAno, processosLidos && !pastaAusente);
        Faixa.Habilitar(ComandosApp.DespachosAbrir, true);
        Faixa.Habilitar(ComandosApp.DespachosAbrirPdf, despachoEscolhido?.ArquivoPdf is not null);
        Faixa.Habilitar(ComandosApp.DespachosMostrar, despachoEscolhido is not null);
        Faixa.Habilitar(ComandosApp.DespachosIrAoProcesso, despachoEscolhido?.Despacho.Processo is not null);
        Faixa.Habilitar(ComandosApp.DespachosAtualizar, true);
        Faixa.Habilitar(ComandosApp.DespachosDoProcesso, despachosDoProcesso || processoEscolhido is not null);
    }

    /// <summary>Por que um comando de Processos ou Despachos não está disponível (o aviso que aparece ao tentar).</summary>
    string MotivoIndisponivelDasPastas(string id)
    {
        if (documento is null && id is ComandosApp.ProcessosAbrirRelatorio or ComandosApp.ProcessosImportarAnalise) return "a visualização ainda não está pronta";
        if (ocupado && id is ComandosApp.ProcessosAbrirRelatorio or ComandosApp.ProcessosImportarAnalise) return "aguarde a operação em andamento terminar";
        return id switch
        {
            ComandosApp.ProcessosAbrirRelatorio => "escolha, na lista da direita, um relatório (.rpmvni) ou uma análise da IA",
            ComandosApp.ProcessosImportarAnalise => "este processo não tem análise da IA para importar",
            ComandosApp.ProcessosAbrirArquivo => "escolha, na lista da direita, um arquivo de despacho, de PROJETO ou de DOCUMENTOS",
            ComandosApp.ProcessosAbrirPasta => "escolha um processo na lista",
            ComandosApp.ProcessosAno => "a lista de processos ainda não foi lida",
            ComandosApp.DespachosAbrirPdf => "o despacho escolhido não tem PDF",
            ComandosApp.DespachosDoProcesso => "escolha antes um processo no módulo Processos",
            ComandosApp.DespachosNovoCorrecao or ComandosApp.DespachosNovoAprovacao or ComandosApp.DespachosNovoCorrecaoAutomatica => Faixa.Dica(id).TrimEnd('.'),
            ComandosApp.DespachosGerarPdf => novo is not null ? FluxoNovoDespacho.Compondo.TrimEnd('.').ToLowerInvariant()
                : "escolha um despacho gerado pelo editor que ficou só em Word (DOCX)",
            _ => "escolha um despacho na lista",
        };
    }

    /// <summary>Os itens do menu Ano: "Todos os anos" e os anos dos processos lidos (✓ no que está escolhido); o valor é o ano ("" = todos).</summary>
    IReadOnlyList<(string Texto, string Valor, string Dica)> ItensDoMenuAno()
    {
        var itens = new List<(string, string, string)>
        {
            ((anoProcessos is null ? "✓  " : "") + "Todos os anos", "", "Mostrar os processos de todos os anos"),
        };
        foreach (var a in ListasPastas.Anos(linhasProcessos.Select(l => l.Processo)))
            itens.Add(((anoProcessos == a ? "✓  " : "") + a.ToString(CultureInfo.InvariantCulture), a.ToString(CultureInfo.InvariantCulture),
                $"Mostrar só os processos de {a}"));
        return itens;
    }
}
