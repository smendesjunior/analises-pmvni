using System.Globalization;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Legislacao;
using Pmvni.Composicao.Web.Ponte;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Módulo Legislação (Tarefa 18; desenho §11.4–§11.7): a lista das leis anexadas (uma linha por anexo, agrupadas por esfera,
/// com filtro), o leitor de PDF (PDF.js no WebView2 da página, com a sandbox do host <c>leg.pmvni.invalid</c>), marcadores, páginas,
/// escala, a pesquisa da barra de título ("Nesta lei" pelo PDFFindController; "Em todas as leis" pelo índice do app), Copiar e Copiar
/// citação. Todo comando passa pela fila de comandos; o leitor nunca grava nada na pasta da legislação.</summary>
public sealed partial class MainWindow
{
    const string AbaLeis = "Leis";

    PaginaLegislacao P => PaginaDeLegislacao;
    LeitorPdfJs? leitorPdf;
    Task<LeitorPdfJs>? criacaoLeitor;
    Anexo? leiAberta;
    int paginaLei, totalPaginasLei;
    double escalaLei;
    bool selecaoNaLei;
    IReadOnlyList<MarcadorLei> marcadoresLei = [];
    bool escopoTodas = true;
    IReadOnlyList<Anexo> anexosDoModulo = [];
    bool definindoPaginaLei;

    // ---------- acessores do roteiro ----------
    internal PaginaLegislacao LeitorLegislacao => P;
    internal LeitorPdfJs? LeitorPdf => leitorPdf;
    internal Anexo? LeiAbertaNoLeitor => leiAberta;
    /// <summary>"Página 2 de 4 · 125%" (vazio sem lei aberta).</summary>
    internal string RodapeLegislacao => leiAberta is null ? "" : $"Página {paginaLei} de {totalPaginasLei} · {Percentual(escalaLei)}";
    internal int RequisicoesBloqueadasLegislacao => leitorPdf?.RequisicoesBloqueadas.Count ?? 0;
    internal int ViolacoesCspLegislacao => leitorPdf?.ViolacoesCsp ?? 0;
    /// <summary>A aba Leis como está: os grupos (esferas) e, em cada um, uma linha por anexo.</summary>
    internal IReadOnlyList<(string Grupo, IReadOnlyList<LinhaAnexo> Linhas)> LeisMostradas { get; private set; } = [];
    internal IReadOnlyList<MarcadorLei> MarcadoresDaLei => marcadoresLei;
    internal string AbaDoPainelLegislacao => (P.AbasPainel.SelectedItem as SelectorBarItem)?.Text ?? "";
    internal bool EscopoTodasAsLeis => escopoTodas;
    /// <summary>A aba Resultados como está: cabeçalho, aviso e, por lei, as linhas "p. N — trecho".</summary>
    internal IReadOnlyList<(string Lei, IReadOnlyList<string> Linhas)> ResultadosMostrados { get; private set; } = [];
    internal IReadOnlyList<ResultadoPesquisa> ResultadosDaPesquisa { get; private set; } = [];
    internal string CabecalhoResultados => P.TextoResultados.Text;
    internal string AvisoDosResultados => P.AvisoResultados.Visibility == Visibility.Visible ? P.AvisoResultados.Text : "";

    void IniciarLegislacao()
    {
        IniciarLeituraPadronizada();
        IniciarListaDeLeis();
        P.AbasPainel.SelectionChanged += (_, _) => MostrarVistaDoPainel();
        P.FiltroLeis.TextChanged += (_, _) => MontarListaDeLeis();
        P.ListaLeis.ItemClick += async (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement { Tag: string id }) await AbrirLeiAsync(id);
        };
        P.ArvoreMarcadores.ItemInvoked += async (_, e) =>
        {
            if ((e.InvokedItem as TreeViewNode)?.Content is NoMarcador { Externo: false } m) await IrMarcadorLegislacaoAsync(m.Caminho);
        };
        P.ListaResultados.ItemClick += async (_, e) =>
        {
            if (e.ClickedItem is FrameworkElement { Tag: ResultadoPesquisa r }) await AbrirResultadoAsync(r, CaixaPesquisa.Text);
        };
        P.BotaoAnexar.Click += async (_, _) => await AnexarLegislacaoAsync();
        P.BotaoAbrirAjustes.Click += async (_, _) => await NaFilaAsync(() => TentarAsync(GerenciarAnexosAsync));
        P.BotaoPaginaAnterior.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.LegislacaoPaginaAnterior);
        P.BotaoPaginaProxima.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.LegislacaoPaginaProxima);
        P.BotaoZoomMenos.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.LegislacaoZoomMenos);
        P.BotaoZoomMais.Click += async (_, _) => await ExecutarComandoAsync(ComandosApp.LegislacaoZoomMais);
        P.CaixaPagina.ValueChanged += async (_, e) =>
        {
            if (definindoPaginaLei || double.IsNaN(e.NewValue) || leiAberta is null) return;
            await IrPaginaLegislacaoAsync((int)Math.Round(e.NewValue));
        };

        // Anexar PDFs… pela faixa: o seletor entra na fila, a leitura dos arquivos corre fora dela (como o botão dos Ajustes).
        manipuladores[ComandosApp.LegislacaoConsultar] = P.AbrirConsultaAsync;
        manipuladores[ComandosApp.LegislacaoAnexar] = () => { DispatcherQueue.TryEnqueue(() => _ = AnexarLegislacaoAsync()); return Task.CompletedTask; };
        manipuladores[ComandosApp.LegislacaoGerenciar] = GerenciarAnexosAsync;
        manipuladores[ComandosApp.LegislacaoCopiar] = CopiarSelecaoNaVezAsync;
        manipuladores[ComandosApp.LegislacaoCopiarCitacao] = CopiarCitacaoNaVezAsync;
        manipuladores[ComandosApp.LegislacaoPaginaAnterior] = () => IrPaginaNaVezAsync(paginaLei - 1);
        manipuladores[ComandosApp.LegislacaoPaginaProxima] = () => IrPaginaNaVezAsync(paginaLei + 1);
        manipuladores[ComandosApp.LegislacaoEscopoLei] = () => TrocarEscopoAsync(todas: false);
        manipuladores[ComandosApp.LegislacaoEscopoTodas] = () => TrocarEscopoAsync(todas: true);
        manipuladores[ComandosApp.LegislacaoOcorrenciaAnterior] = async () => await ProcurarNaVezAsync(CaixaPesquisa.Text, -1);
        manipuladores[ComandosApp.LegislacaoOcorrenciaProxima] = async () => await ProcurarNaVezAsync(CaixaPesquisa.Text, 1);
        manipuladores[ComandosApp.LegislacaoZoomMenos] = () => ZoomLeiNaVezAsync("menos");
        manipuladores[ComandosApp.LegislacaoZoomMais] = () => ZoomLeiNaVezAsync("mais");
        manipuladores[ComandosApp.LegislacaoAjustarLargura] = () => ZoomLeiNaVezAsync("largura");
        manipuladores[ComandosApp.LegislacaoPaginaInteira] = () => ZoomLeiNaVezAsync("pagina");
        manipuladores[ComandosApp.LegislacaoMarcadores] = () =>
        {
            SelecionarAba(P.AbasPainel.SelectedItem == P.AbaMarcadores ? P.AbaLeis : P.AbaMarcadores);
            return Task.CompletedTask;
        };
        manipuladores[ComandosApp.LegislacaoLista] = () =>
        {
            P.Painel.Visibility = P.Painel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            AtualizarComandos();
            return Task.CompletedTask;
        };
    }

    /// <summary>Ao abrir o módulo: relê a lista, cria o leitor (uma vez) e refaz em segundo plano os índices que faltam.</summary>
    async Task EntrarNaLegislacaoAsync()
    {
        await TentarAsync(async () =>
        {
            await AtualizarListaLegislacaoAsync();
            if (reindexacao.IsCompleted && !legislacao.Acervo.SomenteLeitura)
                reindexacao = legislacao.ReindexarPendentesAsync(cancelarReindexacao.Token);
            await GarantirLeitorAsync();
            if (reabrirAoEntrar is { } r)
            {
                reabrirAoEntrar = null;
                await NaFilaAsync(() => TentarAsync(async () => { if (leiAberta?.Id == r.Id && moduloAtual == Modulo.Legislacao) await AbrirLeiNaVezAsync(r.Id, r.Pagina, null); }));
            }
        });
    }

    /// <summary>A lei aberta foi substituída com outro módulo à frente: reabre com o arquivo novo quando o usuário voltar à Legislação.</summary>
    (string Id, int Pagina)? reabrirAoEntrar;

    /// <summary>O leitor sobre o WebView2 da página (criado uma vez, InPrivate pela mesma fábrica e no mesmo ambiente da tela).</summary>
    Task<LeitorPdfJs> GarantirLeitorAsync()
    {
        if (leitorPdf is not null) return Task.FromResult(leitorPdf);
        if (criacaoLeitor is { IsFaulted: false, IsCanceled: false }) return criacaoLeitor;
        // A sandbox entra uma vez por core: depois de uma falha, o WebView2 é trocado por um novo (nada de handlers em dobro).
        if (criacaoLeitor is not null) TrocarWebView2DoLeitor();
        return criacaoLeitor = CriarLeitorAsync();
    }

    async Task<LeitorPdfJs> CriarLeitorAsync()
    {
        var ambiente = R.Navegador.CoreWebView2?.Environment ?? await ambienteDaTela.Task.WaitAsync(TimeSpan.FromSeconds(60));
        if (P.Leitor.CoreWebView2 is null) await P.Leitor.EnsureCoreWebView2Async(ambiente, FabricaWebView2.OpcoesController(ambiente));
        var l = await LeitorPdfJs.SobreAsync(P.Leitor.CoreWebView2!, legislacao.Servidor);
        l.Evento += AoEventoDoLeitor;
        leitorPdf = l;
        AplicarTemaLeitor();
        return l;
    }

    void TrocarWebView2DoLeitor()
    {
        var antigo = P.Leitor;
        if (antigo.Parent is not Grid grade) return;
        var i = grade.Children.IndexOf(antigo);
        try { antigo.Close(); } catch (Exception e) { RegistroErros.Gravar("legislacao.leitor", e); }
        var novo = new WebView2 { Opacity = 0 };
        // O substituto fica no mesmo lugar da grade (sem isto, ia para a linha 0 e a lei aparecia numa faixa estreita no alto).
        Grid.SetRow(novo, Grid.GetRow(antigo));
        Grid.SetColumn(novo, Grid.GetColumn(antigo));
        Grid.SetRowSpan(novo, Grid.GetRowSpan(antigo));
        Grid.SetColumnSpan(novo, Grid.GetColumnSpan(antigo));
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(novo, "PDF da lei");
        grade.Children[i] = novo;
        P.Leitor = novo;
    }

    /// <summary>O tema do app chega à página do leitor (só o fundo em volta das páginas muda; as páginas continuam brancas).</summary>
    async void AplicarTemaLeitor()
    {
        try
        {
            if (P.Leitor.CoreWebView2 is not { } web) return;
            var escuro = Raiz.ActualTheme == ElementTheme.Dark;
            web.Profile.PreferredColorScheme = escuro ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            await web.CallDevToolsProtocolMethodAsync("Emulation.setEmulatedMedia",
                "{\"features\":[{\"name\":\"prefers-color-scheme\",\"value\":\"" + (escuro ? "dark" : "light") + "\"}]}");
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[tema do leitor] " + e.Message); }
    }

    void AoEventoDoLeitor(MensagemWeb m)
    {
        if (fechada) return;
        switch (m.Tipo)
        {
            case "paginaMudou":
                if (Numero(m.Dados, "atual") is { } a) paginaLei = a;
                if (Numero(m.Dados, "total") is { } t && t > 0) totalPaginasLei = t;
                AtualizarRodapeLegislacao();
                AtualizarComandos();
                break;
            case "zoomMudou":
                if (m.Dados.TryGetProperty("escala", out var e) && e.ValueKind == JsonValueKind.Number) escalaLei = e.GetDouble();
                AtualizarRodapeLegislacao();
                break;
            case "selecaoMudou":
                selecaoNaLei = m.Dados.TryGetProperty("temSelecao", out var s) && s.ValueKind == JsonValueKind.True;
                AtualizarComandos();
                break;
            case "atalho":
                if (m.Dados.TryGetProperty("tecla", out var k) && k.ValueKind == JsonValueKind.String && Atalhos.Comando(k.GetString()!, Modulo.Legislacao) is { } id)
                    _ = ExecutarComandoAsync(id);
                break;
        }
    }

    static int? Numero(JsonElement d, string nome) =>
        d.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n) ? n : null;

    static string Percentual(double escala) => Math.Round(escala * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    // ---------- lista de leis (uma linha por anexo, agrupadas por esfera) ----------

    /// <summary>A lista mudou (anexo novo, excluído, renomeado, substituído): a aba Leis acompanha; a lei aberta que foi excluída
    /// fecha o leitor (aba Leis, nada aberto) e a que foi substituída reabre com o arquivo novo.</summary>
    partial void LinhasLegislacaoMudaram(IReadOnlyList<Anexo> anexos)
    {
        anexosDoModulo = anexos;
        if (anexos.Count > 0) _ = TentarAsync(CarregarIndiceDaListaAsync);
        P.TotalAcervo.Text = anexos.Count == 1 ? "1 documento" : $"{anexos.Count} documentos";
        MontarListaDeLeis();
        if (leiAberta is not { } aberta) return;
        var atual = anexos.FirstOrDefault(a => a.Id == aberta.Id);
        if (atual is null) _ = NaFilaAsync(() => TentarAsync(async () => { if (leiAberta?.Id == aberta.Id) await FecharLeiNaVezAsync(); }));
        else if (atual.Sha256 != aberta.Sha256)
        {
            // Substituída (em Ajustes, por exemplo): reabre sem trocar de módulo; fora da Legislação, reabre ao voltar a ela.
            if (moduloAtual == Modulo.Legislacao)
                _ = NaFilaAsync(() => TentarAsync(async () => { if (leiAberta?.Id == aberta.Id) await AbrirLeiNaVezAsync(aberta.Id, paginaLei, null); }));
            else reabrirAoEntrar = (aberta.Id, paginaLei);
        }
        else leiAberta = atual; // título e esfera novos (a citação usa o título da linha)
    }

    /// <summary>O filtro da aba Leis (título e nome do arquivo, sem caixa nem acento).</summary>
    internal void FiltrarLeis(string termo)
    {
        P.FiltroLeis.Text = termo;
        MontarListaDeLeis(); // o TextChanged da caixa chega depois; quem filtra vê a lista já filtrada
    }

    void SelecionarAba(SelectorBarItem aba)
    {
        if (!aba.IsEnabled) return;
        P.AbasPainel.SelectedItem = aba;
        MostrarVistaDoPainel();
    }

    void MostrarVistaDoPainel()
    {
        var aba = P.AbasPainel.SelectedItem;
        P.VistaLeis.Visibility = aba == P.AbaLeis || aba is null ? Visibility.Visible : Visibility.Collapsed;
        P.ArvoreMarcadores.Visibility = aba == P.AbaMarcadores ? Visibility.Visible : Visibility.Collapsed;
        P.VistaResultados.Visibility = aba == P.AbaResultados ? Visibility.Visible : Visibility.Collapsed;
        Faixa.Ligar(ComandosApp.LegislacaoMarcadores, aba == P.AbaMarcadores);
    }

    void MontarMarcadores()
    {
        P.ArvoreMarcadores.RootNodes.Clear();
        void Acrescentar(IList<TreeViewNode> destino, IReadOnlyList<MarcadorLei> itens, IReadOnlyList<int> caminho)
        {
            for (var i = 0; i < itens.Count; i++)
            {
                var c = caminho.Append(i).ToArray();
                var no = new TreeViewNode { Content = new NoMarcador(itens[i].Titulo, itens[i].Externo, c), IsExpanded = false };
                Acrescentar(no.Children, itens[i].Filhos, c);
                destino.Add(no);
            }
        }
        Acrescentar(P.ArvoreMarcadores.RootNodes, marcadoresLei, []);
        P.AbaMarcadores.IsEnabled = marcadoresLei.Count > 0;
        if (marcadoresLei.Count == 0 && P.AbasPainel.SelectedItem == P.AbaMarcadores) SelecionarAba(P.AbaLeis);
    }

    // ---------- abrir e fechar uma lei ----------

    /// <summary>Abre a lei no leitor (o PDF é registrado no servidor com um token novo; a anterior é fechada e sai do servidor), na
    /// página pedida e, com termo, já com a pesquisa "Nesta lei" a partir dessa página. Na fila de comandos.</summary>
    internal Task<bool> AbrirLeiAsync(string id, int? pagina = null, string? termo = null) => NaFilaAsync(() => AbrirLeiNaVezAsync(id, pagina, termo));

    async Task<bool> AbrirLeiNaVezAsync(string id, int? pagina, string? termo)
    {
        if (moduloAtual != Modulo.Legislacao) MontarModulo(Modulo.Legislacao);
        try
        {
            var anexo = (await Task.Run(legislacao.Acervo.Listar)).FirstOrDefault(a => a.Id == id);
            if (anexo is null) { MostrarMensagem(InfoBarSeverity.Warning, "Lei não encontrada", "Esta lei não está mais anexada."); return false; }
            var l = await GarantirLeitorAsync();
            P.AvisoErro.IsOpen = P.AvisoSemCopia.IsOpen = P.AvisoSemTexto.IsOpen = false;
            var caminho = await Task.Run(() => legislacao.Acervo.CaminhoPdf(id));
            if (caminho is null)
            {
                await FecharLeiNaVezAsync();
                P.AvisoSemCopia.IsOpen = true;
                return false;
            }
            LeiAberta aberta;
            try { aberta = await l.AbrirAsync(caminho, pagina, termo); }
            catch (ExtracaoRecusadaException e)
            {
                await FecharLeiNaVezAsync();
                P.AvisoErro.Title = "";
                P.AvisoErro.Message = e.Falha switch
                {
                    FalhaExtracao.Senha => MensagensAnexo.Senha(anexo.ArquivoOriginal),
                    FalhaExtracao.Invalido => MensagensAnexo.Invalido(anexo.ArquivoOriginal),
                    _ => MensagemDoLeitor(e, anexo.ArquivoOriginal),
                };
                P.AvisoErro.IsOpen = true;
                return false;
            }
            if (fechada) return false;
            leiAberta = anexo;
            totalPaginasLei = aberta.Paginas;
            paginaLei = pagina is { } p ? Math.Clamp(p, 1, Math.Max(1, aberta.Paginas)) : 1;
            escalaLei = aberta.Escala;
            selecaoNaLei = false;
            marcadoresLei = aberta.Marcadores;
            P.Leitor.Opacity = 1;
            P.TextoNadaAberto.Visibility = Visibility.Collapsed;
            P.AvisoSemTexto.IsOpen = !anexo.TemTexto;
            MontarMarcadores();
            // Leitura padronizada (correções 2026-11-15); vindo de uma pesquisa (página/termo), fica no PDF original.
            await MostrarLeituraPadronizadaAsync(anexo);
            if ((pagina is not null || termo is not null) && leituraPadronizadaLigada) P.ModoLeitura.SelectedItem = P.ModoPdf;
            // Ajustes › Legislação › "Pesquisar em": o alvo da pesquisa ao abrir uma lei (padrão "Nesta lei", como sempre foi).
            escopoTodas = ajustes.LegislacaoEscopo == "todas";
            AtualizarDicaDaPesquisaLegislacao();
            if (termo is not null)
            {
                definindoPesquisa = true;
                try { CaixaPesquisa.Text = termo; }
                finally { definindoPesquisa = false; }
                termosPesquisa[Modulo.Legislacao] = termo;
                MostrarContadorDaLei(termo, aberta.Procurado ?? new ProcuraLei(0, 0, null));
            }
            else TextoPesquisa.Text = "";
            MontarListaDeLeis();
            AtualizarRodapeLegislacao();
            AtualizarComandos();
            return true;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            var texto = Erro(e);
            try { await FecharLeiNaVezAsync(); } catch (Exception x) { RegistroErros.Gravar("legislacao.fechar", x); }
            MostrarMensagem(InfoBarSeverity.Error, "Não foi possível abrir a lei", texto);
            return false;
        }
    }

    static string MensagemDoLeitor(ExtracaoRecusadaException e, string nome)
    {
        RegistroErros.Gravar("legislacao.leitor", e);
        return MensagensAnexo.LeitorFalhou(nome);
    }

    /// <summary>Fecha a lei aberta: a página a solta, o registro do PDF sai do servidor (a URL dele passa a 403), aba Leis, nada aberto.</summary>
    async Task FecharLeiNaVezAsync()
    {
        var l = leitorPdf;
        leiAberta = null;
        marcadoresLei = [];
        selecaoNaLei = false;
        paginaLei = totalPaginasLei = 0;
        if (l is not null) await l.FecharAsync();
        if (fechada) return;
        P.Leitor.Opacity = 0;
        EsconderLeituraPadronizada();
        P.Leitor.Visibility = Visibility.Visible;
        P.TextoNadaAberto.Visibility = Visibility.Visible;
        P.AvisoSemTexto.IsOpen = false;
        MontarMarcadores();
        escopoTodas = true;
        AtualizarDicaDaPesquisaLegislacao();
        if (moduloAtual == Modulo.Legislacao) TextoPesquisa.Text = "";
        SelecionarAba(P.AbaLeis);
        MontarListaDeLeis();
        AtualizarRodapeLegislacao();
        AtualizarComandos();
    }

    // ---------- páginas, marcadores e escala ----------

    internal Task IrPaginaLegislacaoAsync(int pagina) => NaFilaAsync(() => TentarAsync(() => IrPaginaNaVezAsync(pagina)));

    async Task IrPaginaNaVezAsync(int pagina)
    {
        if (leiAberta is null || leitorPdf is null) return;
        (paginaLei, totalPaginasLei) = await leitorPdf.IrPaginaAsync(pagina);
        AtualizarRodapeLegislacao();
        AtualizarComandos();
    }

    internal Task<bool> IrMarcadorLegislacaoAsync(IReadOnlyList<int> caminho) => NaFilaAsync(async () =>
    {
        if (leiAberta is null || leitorPdf is null) return false;
        (paginaLei, totalPaginasLei) = await leitorPdf.IrMarcadorAsync(caminho);
        AtualizarRodapeLegislacao();
        AtualizarComandos();
        return true;
    });

    async Task ZoomLeiNaVezAsync(string acao)
    {
        if (leiAberta is null || leitorPdf is null) return;
        escalaLei = await leitorPdf.ZoomAsync(acao);
        AtualizarRodapeLegislacao();
    }

    void AtualizarRodapeLegislacao()
    {
        if (fechada) return;
        var aberta = leiAberta is not null;
        definindoPaginaLei = true;
        try
        {
            P.CaixaPagina.Maximum = Math.Max(1, totalPaginasLei);
            P.CaixaPagina.Value = Math.Max(1, paginaLei);
        }
        finally { definindoPaginaLei = false; }
        P.CaixaPagina.IsEnabled = aberta;
        P.TextoTotalPaginas.Text = "de " + (aberta ? totalPaginasLei : 0).ToString(CultureInfo.InvariantCulture);
        P.TextoEscala.Text = aberta ? Percentual(escalaLei) : "";
        P.BotaoPaginaAnterior.IsEnabled = aberta && paginaLei > 1;
        P.BotaoPaginaProxima.IsEnabled = aberta && paginaLei < totalPaginasLei;
        AtualizarDicasDoRodapeDaLei(aberta);
    }

    // ---------- pesquisa (a caixa da barra de título no módulo Legislação; desenho §11.6) ----------

    async Task TrocarEscopoAsync(bool todas)
    {
        if (!todas && leiAberta is null) return;
        escopoTodas = todas;
        AtualizarDicaDaPesquisaLegislacao();
        AtualizarComandos();
        await PesquisarNaLegislacaoAsync(CaixaPesquisa.Text, 0);
    }

    void AtualizarDicaDaPesquisaLegislacao()
    {
        if (moduloAtual != Modulo.Legislacao) return;
        // O texto de espera é o da pesquisa geral; o alvo ("Pesquisar nesta lei" / "em todas as leis") fica na dica do módulo.
        dicaDoModulo = DicaDaPesquisaDe(Modulo.Legislacao);
        AplicarDicaDaPesquisa(null);
    }

    /// <summary>O pedido da caixa de pesquisa com a Legislação à vista (na fila de comandos): "Nesta lei" procura no PDF aberto;
    /// "Em todas as leis", no índice. Caixa vazia volta à aba Leis.</summary>
    async Task PesquisarNaLegislacaoAsync(string termo, int passo)
    {
        if (termo.Length == 0 && P.AbasPainel.SelectedItem == P.AbaResultados) SelecionarAba(P.AbaLeis);
        if (escopoTodas || leiAberta is null) await PesquisarEmTodasNaVezAsync(termo);
        else await ProcurarNaVezAsync(termo, passo);
    }

    /// <summary>Procura na lei aberta (passo 0 = a partir da página atual; 1/-1 = próxima/anterior) e devolve o texto do contador:
    /// "k de n", "Nenhuma ocorrência" ou "Este PDF não tem texto pesquisável."; termo vazio limpa o realce.</summary>
    internal Task<string> ProcurarNaLeiAsync(string termo, int passo) => NaFilaAsync(() => ProcurarNaVezAsync(termo, passo));

    async Task<string> ProcurarNaVezAsync(string termo, int passo)
    {
        if (leiAberta is null || leitorPdf is null) return "";
        if (termo.Length > 0 && !leiAberta.TemTexto)
        {
            TextoPesquisa.Text = "Este PDF não tem texto pesquisável.";
            return TextoPesquisa.Text;
        }
        var r = await leitorPdf.ProcurarAsync(termo, passo);
        MostrarContadorDaLei(termo, r);
        return TextoPesquisa.Text;
    }

    void MostrarContadorDaLei(string termo, ProcuraLei r)
    {
        if (fechada) return;
        if (r.Pagina is { } p) { paginaLei = p; AtualizarRodapeLegislacao(); }
        TextoPesquisa.Text = termo.Length == 0 ? "" : r.Total == 0 ? "Nenhuma ocorrência"
            : $"{r.Atual.ToString(CultureInfo.InvariantCulture)} de {r.Total.ToString(CultureInfo.InvariantCulture)}";
    }

    /// <summary>Pesquisa em todas as leis anexadas (índice em memória, sem caixa nem acento): a aba Resultados com "N resultados em K
    /// leis", uma linha por página com ocorrência, agrupadas por lei; termo curto ⇒ "Digite ao menos 2 caracteres.".</summary>
    internal Task<Pesquisa> PesquisarEmTodasAsync(string termo) => NaFilaAsync(() => PesquisarEmTodasNaVezAsync(termo));

    async Task<Pesquisa> PesquisarEmTodasNaVezAsync(string termo)
    {
        var vazia = new Pesquisa([], 0, 0);
        TextoPesquisa.Text = "";
        var normalizado = Pmvni.Legislacao.TextoPesquisa.Normalizar(termo);
        if (normalizado.Length == 0)
        {
            MostrarResultados(vazia, "", []);
            if (P.AbasPainel.SelectedItem == P.AbaResultados) SelecionarAba(P.AbaLeis);
            return vazia;
        }
        if (normalizado.Length < 2)
        {
            MostrarResultados(vazia, "Digite ao menos 2 caracteres.", []);
            return vazia;
        }
        var indice = await legislacao.IndiceAsync();
        var p = await Task.Run(() => indice.Pesquisar(termo));
        var cabecalho = p.Total == 0 ? "Nenhum resultado"
            : (p.Total == 1 ? "1 resultado" : $"{p.Total.ToString(CultureInfo.InvariantCulture)} resultados")
              + (p.Leis == 1 ? " em 1 lei" : $" em {p.Leis.ToString(CultureInfo.InvariantCulture)} leis");
        MostrarResultados(p, cabecalho, p.Resultados);
        return p;
    }

    void MostrarResultados(Pesquisa p, string cabecalho, IReadOnlyList<ResultadoPesquisa> resultados)
    {
        if (fechada) return;
        ResultadosDaPesquisa = resultados;
        P.TextoResultados.Text = cabecalho;
        var cortado = p.Total > p.Resultados.Count;
        P.AvisoResultados.Text = cortado ? $"Mostrando os {p.Resultados.Count.ToString(CultureInfo.InvariantCulture)} primeiros de {p.Total.ToString(CultureInfo.InvariantCulture)} resultados." : "";
        P.AvisoResultados.Visibility = cortado ? Visibility.Visible : Visibility.Collapsed;
        var lista = P.ListaResultados;
        lista.Items.Clear();
        var grupos = new List<(string, IReadOnlyList<string>)>();
        foreach (var g in resultados.GroupBy(r => r.AnexoId))
        {
            var titulo = anexosDoModulo.FirstOrDefault(a => a.Id == g.Key)?.Titulo ?? g.Key;
            lista.Items.Add(new TextBlock
            {
                Text = titulo, Margin = new Thickness(6, 10, 0, 2), TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false,
                Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"],
            });
            var linhas = new List<string>();
            foreach (var r in g)
            {
                var texto = $"p. {r.Pagina.ToString(CultureInfo.InvariantCulture)} — {r.Trecho}";
                linhas.Add(texto);
                var item = new TextBlock { Text = texto, Tag = r, Style = (Style)Application.Current.Resources["PmvniPainelValor"],
                    IsTextSelectionEnabled = false, Padding = new Thickness(4, 3, 4, 5) };
                lista.Items.Add(item);
            }
            grupos.Add((titulo, linhas));
        }
        ResultadosMostrados = grupos;
        P.AbaResultados.IsEnabled = cabecalho.Length > 0;
        if (cabecalho.Length > 0) SelecionarAba(P.AbaResultados);
    }

    /// <summary>Clicar num resultado: abre a lei naquela página e passa a pesquisa para "Nesta lei" com o mesmo termo, a partir dessa
    /// página (a ocorrência selecionada é a dessa página).</summary>
    internal Task<bool> AbrirResultadoAsync(ResultadoPesquisa r, string termo) => NaFilaAsync(async () =>
    {
        escopoTodas = false;
        return await AbrirLeiNaVezAsync(r.AnexoId, r.Pagina, termo);
    });

    // ---------- copiar e citar (desenho §11.7) ----------

    internal Task CopiarSelecaoAsync() => NaFilaAsync(CopiarSelecaoNaVezAsync);

    internal Task CopiarCitacaoAsync() => NaFilaAsync(CopiarCitacaoNaVezAsync);

    async Task CopiarSelecaoNaVezAsync()
    {
        if (leitorPdf is null || leiAberta is null) return;
        var s = await leitorPdf.ObterSelecaoAsync();
        var texto = Citacao.TextoParaCopiar(s.Texto);
        if (texto.Length > 0) AreaTransferencia.Copiar(texto);
    }

    async Task CopiarCitacaoNaVezAsync()
    {
        if (leitorPdf is null || leiAberta is not { } lei) return;
        var s = await leitorPdf.ObterSelecaoAsync();
        if (s.PaginaInicial is not { } pi || s.PaginaFinal is not { } pf) return;
        if (Citacao.De(s.Texto, lei.Titulo, pi, pf) is { } citacao) AreaTransferencia.Copiar(citacao);
    }

    /// <summary>Gerenciar anexos (e "Abrir Ajustes" da lista vazia): Ajustes › Legislação.</summary>
    async Task GerenciarAnexosAsync()
    {
        await MudarModuloAsync(Modulo.Ajustes);
        // "Gerenciar anexos" abre Ajustes › Legislação (auditoria §3.2).
        PaginaDeAjustes.MostrarCategoria(CategoriasAjustes.Legislacao);
        PaginaDeAjustes.SecaoLegislacao.StartBringIntoView();
    }

    /// <summary>O estado que decide os comandos da Legislação (o roteiro o grava quando uma conferência falha).</summary>
    internal string EstadoDosComandosLegislacao =>
        $"lei_aberta={leiAberta?.Id ?? "-"} leitor={(leitorPdf is not null)} selecao={selecaoNaLei} texto={leiAberta?.TemTexto} todas={escopoTodas} modulo={moduloAtual} aviso={P.AvisoMensagem.IsOpen}:{P.AvisoMensagem.Title} progresso={P.ProgressoAnexo.Visibility}";

    void AtualizarComandosLegislacao()
    {
        if (legislacao is null) return;
        var lei = leiAberta is not null && leitorPdf is not null;
        var h = ComandosLegislacao.Habilitados(lei, leiAberta?.TemTexto ?? false, selecaoNaLei, marcadoresLei.Count > 0, legislacao.Acervo.SomenteLeitura, escopoTodas);
        foreach (var id in IdsDaLegislacao) Faixa.Habilitar(id, h.Contains(id));
        var dicaCopiar = selecaoNaLei ? "Copiar o trecho selecionado (Ctrl+C)" : ComandosLegislacao.DicaSemSelecao;
        var dicaCitar = selecaoNaLei ? "Copiar o trecho selecionado como citação, com o título e a página (Ctrl+Shift+C)" : ComandosLegislacao.DicaSemSelecao;
        Faixa.DefinirDica(ComandosApp.LegislacaoCopiar, dicaCopiar);
        Faixa.DefinirDica(ComandosApp.LegislacaoCopiarCitacao, dicaCitar);
        Faixa.MarcarItem(ComandosApp.LegislacaoEscopoLei, !escopoTodas);
        Faixa.MarcarItem(ComandosApp.LegislacaoEscopoTodas, escopoTodas);
        Faixa.Ligar(ComandosApp.LegislacaoLista, P.Painel.Visibility == Visibility.Visible);
        Faixa.Ligar(ComandosApp.LegislacaoMarcadores, P.AbasPainel.SelectedItem == P.AbaMarcadores);
        P.BotaoPaginaAnterior.IsEnabled = lei && paginaLei > 1;
        P.BotaoPaginaProxima.IsEnabled = lei && paginaLei < totalPaginasLei;
        P.BotaoZoomMenos.IsEnabled = Faixa.Habilitado(ComandosApp.LegislacaoZoomMenos);
        P.BotaoZoomMais.IsEnabled = Faixa.Habilitado(ComandosApp.LegislacaoZoomMais);
        AtualizarDicasDoRodapeDaLei(lei);
    }

    void AtualizarDicasDoRodapeDaLei(bool lei)
    {
        const string abrir = "Abra uma lei para usar este controle.";
        Dicas.Aplicar(P.CaixaPagina, "Página", "Digite o número da página que deseja ler.", motivo: P.CaixaPagina.IsEnabled ? null : abrir);
        Dicas.AplicarComando(P.BotaoPaginaAnterior, ComandosApp.LegislacaoPaginaAnterior, "Página anterior", Modulo.Legislacao,
            P.BotaoPaginaAnterior.IsEnabled ? null : lei ? "Você está na primeira página." : abrir);
        Dicas.AplicarComando(P.BotaoPaginaProxima, ComandosApp.LegislacaoPaginaProxima, "Próxima página", Modulo.Legislacao,
            P.BotaoPaginaProxima.IsEnabled ? null : lei ? "Você está na última página." : abrir);
        Dicas.AplicarComando(P.BotaoZoomMenos, ComandosApp.LegislacaoZoomMenos, "Diminuir o zoom", Modulo.Legislacao,
            P.BotaoZoomMenos.IsEnabled ? null : abrir);
        Dicas.AplicarComando(P.BotaoZoomMais, ComandosApp.LegislacaoZoomMais, "Aumentar o zoom", Modulo.Legislacao,
            P.BotaoZoomMais.IsEnabled ? null : abrir);
    }

    static readonly string[] IdsDaLegislacao = Faixas.Comandos(Faixas.Para(Modulo.Legislacao))
        .Select(c => c.Id).Where(id => id.StartsWith("legislacao.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToArray();
}
