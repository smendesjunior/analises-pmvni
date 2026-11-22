using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Pmvni.Legislacao;
using Pmvni.Revisao;
using ChaveColunaAc = Pmvni.Acompanhamento.ChaveColuna;
using LinhaDaPlanilha = Pmvni.Acompanhamento.LinhaProcesso;
using VirtualKey = Windows.System.VirtualKey;

namespace Pmvni.App;

/// <summary>Pesquisa geral na caixa da barra de título (2026-11-17). Digitar continua filtrando o módulo à vista como antes (folhas do
/// relatório, listas de Processos e Despachos, planilha, leis); além disso, abaixo da caixa abre uma lista com o que o app inteiro tem com
/// aquele termo, agrupada por tipo: funções da faixa e módulos, ajustes, processos, planilha do Acompanhamento, arquivos (relatórios e
/// despachos), leis e trechos do texto das leis. ↑/↓ escolhem, Enter abre, Esc fecha (o segundo Esc faz o de sempre). O catálogo e a
/// comparação são de <see cref="PesquisaGeral"/>; aqui ficam a lista, a leitura em segundo plano (cancelável) e o que cada escolha faz.</summary>
public sealed partial class MainWindow
{
    static readonly TimeSpan EsperaPesquisaGeral = TimeSpan.FromMilliseconds(150);
    static readonly TimeSpan ValidadeDosArquivos = TimeSpan.FromMinutes(3);
    static readonly TimeSpan ValidadeDasLeis = TimeSpan.FromMinutes(1);
    const int MaximoDeArquivos = 20000;

    DispatcherQueueTimer esperaGeral = null!;
    CancellationTokenSource? pesquisaGeralCts;
    Border molduraGeral = null!;
    StackPanel listaGeral = null!;
    ScrollViewer rolagemGeral = null!;
    TextBlock rodapeGeral = null!;
    readonly List<(ItemPesquisaGeral Item, Grid Linha)> linhasGeral = [];
    int selecionadoGeral = -1;
    bool pressionandoLinhaGeral;
    string termoGeralMostrado = "";

    // Fontes (cada uma reaproveita os itens enquanto a lista de origem é a mesma).
    IReadOnlyList<ItemPesquisaGeral>? funcoesGeral, ajustesGeral;
    (object? Fonte, IReadOnlyList<ItemPesquisaGeral> Itens) processosGeral = (null, []), despachosGeral = (null, []), planilhaGeral = (null, []), leisGeral = (null, []);
    IReadOnlyList<ItemPesquisaGeral> processosLidosGeral = [], arquivosGeral = [];
    string? pastaLidaGeral;
    DateTime arquivosLidosEm;
    Task leituraGeral = Task.CompletedTask;
    IReadOnlyList<Anexo> leisLidasGeral = [];
    DateTime leisLidasEm;
    Task leituraDasLeisGeral = Task.CompletedTask;

    // ---------- acessores do roteiro ----------
    internal bool ListaGeralAberta => molduraGeral?.Visibility == Visibility.Visible;
    internal IReadOnlyList<ItemPesquisaGeral> ResultadosGerais => linhasGeral.Select(l => l.Item).ToList();

    internal bool ResultadosVisiveisPara(string termo)
    {
        if (!ListaGeralAberta || termoGeralMostrado != termo || linhasGeral.Count == 0 || molduraGeral.ActualHeight <= 0) return false;
        var topo = molduraGeral.TransformToVisual(Raiz).TransformPoint(default).Y;
        var baseCaixa = CaixaPesquisa.TransformToVisual(Raiz).TransformPoint(new Windows.Foundation.Point(0, CaixaPesquisa.ActualHeight)).Y;
        return topo >= baseCaixa && topo < Raiz.ActualHeight;
    }

    void IniciarPesquisaGeral()
    {
        esperaGeral = DispatcherQueue.GetForCurrentThread().CreateTimer();
        esperaGeral.Interval = EsperaPesquisaGeral;
        esperaGeral.IsRepeating = false;
        esperaGeral.Tick += (_, _) =>
        {
            esperaGeral.Stop();
            _ = PesquisarGeralAsync();
        };
        MontarListaGeral();
        CaixaPesquisa.PreviewKeyDown += AoTeclarNaPesquisaGeral;
        CaixaPesquisa.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (pressionandoLinhaGeral || Raiz.XamlRoot is null) return;
            if (FocusManager.GetFocusedElement(Raiz.XamlRoot) is DependencyObject f && (ReferenceEquals(f, CaixaPesquisa) || DentroDe(f, molduraGeral))) return;
            FecharPesquisaGeral();
        });
        // Clique fora da caixa e da lista fecha a lista (inclusive em área que não recebe foco).
        Raiz.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (!ListaGeralAberta || e.OriginalSource is not DependencyObject o) return;
            if (DentroDe(o, molduraGeral) || DentroDe(o, CaixaPesquisa)) return;
            FecharPesquisaGeral();
        }), handledEventsToo: true);
        Activated += (_, e) => { if (e.WindowActivationState == WindowActivationState.Deactivated) FecharPesquisaGeral(); };
        CaixaPesquisa.PlaceholderText = PesquisaGeral.Placeholder;
        AutomationProperties.SetName(CaixaPesquisa, PesquisaGeral.NomeAcessivel);
    }

    static bool DentroDe(DependencyObject o, DependencyObject alvo)
    {
        for (DependencyObject? x = o; x is not null; x = VisualTreeHelper.GetParent(x) ?? (x as FrameworkElement)?.Parent)
            if (ReferenceEquals(x, alvo)) return true;
        return false;
    }

    /// <summary>A lista fica num Popup filho da grade da caixa, logo abaixo da barra de título (a área de arrastar da barra não pode cobri-la).
    /// O visual vem de XAML com ThemeResource, para seguir o tema do app.</summary>
    void MontarListaGeral()
    {
        molduraGeral = (Border)XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    Width="480" CornerRadius="8" BorderThickness="1"
                    Background="{ThemeResource AcrylicInAppFillColorDefaultBrush}" BorderBrush="{ThemeResource SurfaceStrokeColorFlyoutBrush}">
              <Grid>
                <Grid.RowDefinitions><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
                <ScrollViewer x:Name="Rolagem" MaxHeight="440" Padding="4,4,4,0" HorizontalScrollBarVisibility="Disabled">
                  <StackPanel x:Name="Lista" AutomationProperties.Name="Resultados da pesquisa"/>
                </ScrollViewer>
                <TextBlock x:Name="Rodape" Grid.Row="1" Margin="14,6,14,8" TextWrapping="Wrap"/>
              </Grid>
            </Border>
            """);
        rolagemGeral = (ScrollViewer)molduraGeral.FindName("Rolagem");
        listaGeral = (StackPanel)molduraGeral.FindName("Lista");
        rodapeGeral = (TextBlock)molduraGeral.FindName("Rodape");
        rodapeGeral.Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"];
        molduraGeral.Shadow = new ThemeShadow();
        molduraGeral.Translation = new System.Numerics.Vector3(0, 0, 32);
        molduraGeral.Visibility = Visibility.Collapsed;
        molduraGeral.HorizontalAlignment = HorizontalAlignment.Left;
        molduraGeral.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumnSpan(molduraGeral, Raiz.ColumnDefinitions.Count);
        Grid.SetRowSpan(molduraGeral, Raiz.RowDefinitions.Count);
        Canvas.SetZIndex(molduraGeral, 100);
        Raiz.Children.Add(molduraGeral);
        Raiz.SizeChanged += (_, _) => { if (ListaGeralAberta) PosicionarListaGeral(); };
    }

    // ---------- digitação e teclado ----------

    /// <summary>Chamado a cada letra (fora de quando o app escreve na caixa): espera 150 ms a digitação assentar.</summary>
    void AgendarPesquisaGeral()
    {
        esperaGeral.Stop();
        if (CaixaPesquisa.Text.Trim().Length < PesquisaGeral.MinimoDoTermo)
        {
            pesquisaGeralCts?.Cancel();
            FecharPesquisaGeral();
            return;
        }
        esperaGeral.Start();
    }

    void AoTeclarNaPesquisaGeral(object sender, KeyRoutedEventArgs e)
    {
        if (!ListaGeralAberta)
        {
            // ↓ com termo e a lista fechada: abre de novo.
            if (e.Key == VirtualKey.Down && CaixaPesquisa.Text.Trim().Length >= PesquisaGeral.MinimoDoTermo)
            {
                e.Handled = true;
                esperaGeral.Stop();
                _ = PesquisarGeralAsync(selecionarPrimeiro: true);
            }
            return;
        }
        switch (e.Key)
        {
            case VirtualKey.Down: e.Handled = true; MoverSelecaoGeral(+1); break;
            case VirtualKey.Up: e.Handled = true; MoverSelecaoGeral(-1); break;
            case VirtualKey.PageDown: e.Handled = true; MoverSelecaoGeral(+5); break;
            case VirtualKey.PageUp: e.Handled = true; MoverSelecaoGeral(-5); break;
            case VirtualKey.Enter when selecionadoGeral >= 0 && selecionadoGeral < linhasGeral.Count:
                // Só com um resultado escolhido; sem escolha, o Enter segue para o módulo (próxima ocorrência etc.).
                e.Handled = true;
                _ = EscolherResultadoGeralAsync(linhasGeral[selecionadoGeral].Item);
                break;
            case VirtualKey.Escape:
                // O primeiro Esc fecha a lista; o seguinte faz o de sempre (limpa a caixa).
                e.Handled = true;
                esperaGeral.Stop();
                pesquisaGeralCts?.Cancel();
                FecharPesquisaGeral();
                break;
            case VirtualKey.Tab:
                FecharPesquisaGeral();
                break;
        }
    }

    void MoverSelecaoGeral(int passo)
    {
        if (linhasGeral.Count == 0) return;
        var novo = selecionadoGeral < 0 ? (passo > 0 ? 0 : linhasGeral.Count - 1) : Math.Clamp(selecionadoGeral + passo, 0, linhasGeral.Count - 1);
        SelecionarGeral(novo, anunciar: true);
    }

    void SelecionarGeral(int indice, bool anunciar)
    {
        for (var i = 0; i < linhasGeral.Count; i++) Realcar(linhasGeral[i].Linha, i == indice);
        selecionadoGeral = indice;
        if (indice < 0 || indice >= linhasGeral.Count) return;
        var linha = linhasGeral[indice].Linha;
        linha.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = false });
        if (anunciar)
            Anunciar($"{linhasGeral[indice].Item.NomeAcessivel}. {(indice + 1).ToString(CultureInfo.InvariantCulture)} de {linhasGeral.Count.ToString(CultureInfo.InvariantCulture)}");
    }

    static void Realcar(Grid linha, bool sim)
    {
        if (linha.FindName("Realce") is UIElement r) r.Opacity = sim ? 1 : 0;
        if (linha.FindName("Marca") is UIElement m) m.Opacity = sim ? 1 : 0;
    }

    /// <summary>O Narrador lê o resultado escolhido com as setas e a contagem quando a lista muda (o foco fica na caixa).</summary>
    void Anunciar(string texto)
    {
        try
        {
            var peer = FrameworkElementAutomationPeer.FromElement(CaixaPesquisa) ?? FrameworkElementAutomationPeer.CreatePeerForElement(CaixaPesquisa);
            peer?.RaiseNotificationEvent(AutomationNotificationKind.ActionCompleted, AutomationNotificationProcessing.ImportantMostRecent, texto, "pmvni.pesquisa.geral");
        }
        catch (Exception e) { System.Diagnostics.Debug.WriteLine("[pesquisa geral] " + e.Message); }
    }

    void FecharPesquisaGeral()
    {
        if (!ListaGeralAberta) return;
        molduraGeral.Visibility = Visibility.Collapsed;
        selecionadoGeral = -1;
        pressionandoLinhaGeral = false;
    }

    // ---------- a pesquisa ----------

    /// <summary>Duas etapas, ambas canceladas por uma letra nova: (1) o que está em memória (funções, ajustes, processos e despachos já lidos,
    /// planilha, leis, recentes), na hora; (2) depois da leitura das pastas dos processos e do índice de texto das leis, em segundo plano.</summary>
    internal async Task PesquisarGeralAsync(bool selecionarPrimeiro = false)
    {
        pesquisaGeralCts?.Cancel();
        var cts = new CancellationTokenSource();
        pesquisaGeralCts = cts;
        var ct = cts.Token;
        var termo = CaixaPesquisa.Text.Trim();
        if (termo.Length < PesquisaGeral.MinimoDoTermo || fechada) { FecharPesquisaGeral(); return; }
        try
        {
            GarantirFontesDaPesquisaGeral();
            var itens = ItensEmMemoriaGeral();
            var grupos = await Task.Run(() => PesquisaGeral.Pesquisar(termo, itens, ct), ct);
            if (ct.IsCancellationRequested || fechada) return;
            var comTexto = PesquisaGeral.Normalizar(termo).Length >= 3 && legislacao is not null;
            var pendente = !leituraGeral.IsCompleted || !leituraDasLeisGeral.IsCompleted || comTexto;
            MostrarResultadosGerais(termo, grupos, pendente, selecionarPrimeiro);

            // Etapa 2: espera as leituras em curso e procura também no texto das leis.
            try { await leituraGeral.WaitAsync(ct); } catch (Exception e) when (e is not OperationCanceledException) { }
            try { await leituraDasLeisGeral.WaitAsync(ct); } catch (Exception e) when (e is not OperationCanceledException) { }
            IReadOnlyList<ItemPesquisaGeral> trechos = [];
            if (comTexto)
            {
                try
                {
                    var indice = await legislacao!.IndiceAsync().WaitAsync(ct);
                    var achados = await Task.Run(() => indice.Pesquisar(termo, 6), ct);
                    var leis = LeisConhecidas().DistinctBy(a => a.Id).ToDictionary(a => a.Id, StringComparer.Ordinal);
                    trechos = PesquisaGeral.DeTrechos(achados.Resultados.Take(6), leis, termo);
                }
                catch (Exception e) when (e is not OperationCanceledException) { RegistroErros.Gravar("pesquisa.geral.leis", e); }
            }
            if (ct.IsCancellationRequested || fechada) return;
            var todos = ItensEmMemoriaGeral().Concat(trechos).ToList();
            grupos = await Task.Run(() => PesquisaGeral.Pesquisar(termo, todos, ct), ct);
            if (ct.IsCancellationRequested || fechada) return;
            MostrarResultadosGerais(termo, grupos, pendente: false, selecionarPrimeiro);
        }
        catch (OperationCanceledException) { /* uma letra nova assumiu */ }
        catch (Exception e) { RegistroErros.Gravar("pesquisa.geral", e); }
    }

    /// <summary>Tudo o que já está em memória, na ordem de prioridade dos duplicados (o despacho lido pelo módulo antes do achado na varredura).</summary>
    List<ItemPesquisaGeral> ItensEmMemoriaGeral()
    {
        funcoesGeral ??= PesquisaGeral.Funcoes();
        ajustesGeral ??= PesquisaGeral.Ajustes(PaginaDeAjustes.OpcoesDaTela());
        var lista = new List<ItemPesquisaGeral>(funcoesGeral.Count + ajustesGeral.Count + 256);
        lista.AddRange(funcoesGeral);
        lista.AddRange(ajustesGeral);
        // Processos: a lista do módulo, se já foi lida; senão, a leitura da própria pesquisa.
        if (linhasProcessos.Count > 0)
            lista.AddRange(Reaproveitar(ref processosGeral, linhasProcessos, () => PesquisaGeral.DeProcessos(linhasProcessos.Select(l => l.Processo))));
        else lista.AddRange(processosLidosGeral);
        if (planilha is { } p)
            lista.AddRange(Reaproveitar(ref planilhaGeral, p, () => PesquisaGeral.DeAcompanhamento(p.Linhas.Select(ParaAcompanhado))));
        lista.AddRange(PesquisaGeral.DeRecentes(ajustes.Recentes));
        if (todosDespachos.Count > 0)
            lista.AddRange(Reaproveitar(ref despachosGeral, todosDespachos, () => PesquisaGeral.DeDespachos(todosDespachos)));
        lista.AddRange(arquivosGeral);
        var leis = LeisConhecidas();
        lista.AddRange(Reaproveitar(ref leisGeral, leis, () => PesquisaGeral.DeLeis(leis)));
        return lista;
    }

    static IReadOnlyList<ItemPesquisaGeral> Reaproveitar(ref (object? Fonte, IReadOnlyList<ItemPesquisaGeral> Itens) cache, object fonte, Func<IReadOnlyList<ItemPesquisaGeral>> criar)
    {
        if (!ReferenceEquals(cache.Fonte, fonte)) cache = (fonte, criar());
        return cache.Itens;
    }

    static ProcessoAcompanhado ParaAcompanhado(LinhaDaPlanilha l) =>
        new(l.Processo, l.Texto(ChaveColunaAc.Requerente), l.Status, l.ProcessoNormalizado);

    IReadOnlyList<Anexo> LeisConhecidas() => anexosDoModulo.Count > 0 ? anexosDoModulo : leisLidasGeral;

    /// <summary>Começa (em segundo plano, sem prender a tela) a leitura do que ainda não está em memória ou ficou velho: as pastas dos processos
    /// (processos, relatórios e arquivos das pastas DESPACHO) e a lista de leis anexadas.</summary>
    void GarantirFontesDaPesquisaGeral()
    {
        var pasta = ajustes.PastaProcessosEfetiva;
        if (leituraGeral.IsCompleted && (!string.Equals(pastaLidaGeral, pasta, StringComparison.OrdinalIgnoreCase) || DateTime.UtcNow - arquivosLidosEm > ValidadeDosArquivos))
            leituraGeral = LerPastasDaPesquisaGeralAsync(pasta);
        if (leituraDasLeisGeral.IsCompleted && anexosDoModulo.Count == 0 && legislacao is not null && DateTime.UtcNow - leisLidasEm > ValidadeDasLeis)
            leituraDasLeisGeral = LerLeisDaPesquisaGeralAsync();
    }

    async Task LerPastasDaPesquisaGeralAsync(string pasta)
    {
        try
        {
            var (processos, arquivos) = await Task.Run(() =>
            {
                if (!Directory.Exists(pasta)) return ((IReadOnlyList<ItemPesquisaGeral>)[], (IReadOnlyList<ItemPesquisaGeral>)[]);
                var lidos = Processos.Listar(pasta, CancellationToken.None).Itens;
                return (PesquisaGeral.DeProcessos(lidos), PesquisaGeral.DeArquivos(ArquivosDosProcessos(lidos)));
            });
            if (fechada) return;
            processosLidosGeral = processos;
            arquivosGeral = arquivos;
        }
        catch (Exception e) { RegistroErros.Gravar("pesquisa.geral.pastas", e); }
        finally
        {
            pastaLidaGeral = pasta;
            arquivosLidosEm = DateTime.UtcNow;
        }
    }

    /// <summary>Os relatórios (<c>.rpmvni</c>, <c>.pmvrel</c>) da pasta do processo e das pastas <c>DESPACHO NN</c>, os despachos (<c>.docx</c>,
    /// <c>.doc</c>, <c>.pdf</c>) dessas pastas e (1.0.0) os documentos da raiz do processo e das subpastas dele (DOCUMENTOS, PROJETO…, um nível).
    /// Só lê nomes (nada é baixado do OneDrive); links, arquivos ocultos ou temporários e pastas ilegíveis ficam de fora.</summary>
    internal static List<(string Caminho, Processo? Processo)> ArquivosDosProcessos(IReadOnlyList<Processo> processos)
    {
        static bool Relatorio(string f) => Path.GetExtension(f).ToLowerInvariant() is ".rpmvni" or ".pmvrel";
        static bool DaEtapa(string f) => Path.GetExtension(f).ToLowerInvariant() is ".rpmvni" or ".pmvrel" or ".docx" or ".doc" or ".pdf";
        static bool Documento(string f) => !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal)
            && Path.GetExtension(f).ToLowerInvariant() is ".pdf" or ".docx" or ".doc" or ".odt" or ".xlsx" or ".xls" or ".ods" or ".dwg" or ".dxf"
                or ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".txt" or ".rtf" or ".kml" or ".kmz";
        static bool Link(FileSystemInfo i) { try { return i.LinkTarget is not null || i.Attributes.HasFlag(FileAttributes.Hidden); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; } }
        var lista = new List<(string, Processo?)>();
        foreach (var p in processos)
        {
            if (lista.Count >= MaximoDeArquivos) break;
            try
            {
                foreach (var f in Directory.EnumerateFiles(p.Pasta))
                    if ((Relatorio(f) || Documento(f)) && !Link(new FileInfo(f))) lista.Add((f, p));
                foreach (var d in Directory.EnumerateDirectories(p.Pasta))
                {
                    if (Link(new DirectoryInfo(d))) continue;
                    var etapa = Path.GetFileName(d).StartsWith("DESPACHO", StringComparison.OrdinalIgnoreCase);
                    foreach (var f in Directory.EnumerateFiles(d))
                    {
                        if (lista.Count >= MaximoDeArquivos) break;
                        if ((etapa ? DaEtapa(f) : Documento(f)) && !Link(new FileInfo(f))) lista.Add((f, p));
                    }
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { /* pasta ilegível: segue */ }
        }
        return lista;
    }

    async Task LerLeisDaPesquisaGeralAsync()
    {
        try
        {
            var leis = await Task.Run(legislacao.Acervo.Listar);
            if (!fechada) leisLidasGeral = leis;
        }
        catch (Exception e) { RegistroErros.Gravar("pesquisa.geral.leis", e); }
        finally { leisLidasEm = DateTime.UtcNow; }
    }

    // ---------- a lista ----------

    void MostrarResultadosGerais(string termo, IReadOnlyList<GrupoPesquisaGeral> grupos, bool pendente, bool selecionarPrimeiro)
    {
        if (fechada || CaixaPesquisa.Text.Trim() != termo) return;
        var chaveAntes = selecionadoGeral >= 0 && selecionadoGeral < linhasGeral.Count ? linhasGeral[selecionadoGeral].Item.Chave : null;
        var mesmoTermo = termo == termoGeralMostrado && ListaGeralAberta;
        termoGeralMostrado = termo;
        listaGeral.Children.Clear();
        linhasGeral.Clear();
        foreach (var g in grupos)
        {
            listaGeral.Children.Add(CabecalhoGeral(g.Nome));
            foreach (var item in g.Itens)
            {
                var linha = LinhaGeral(item, linhasGeral.Count);
                linhasGeral.Add((item, linha));
                listaGeral.Children.Add(linha);
            }
        }
        if (linhasGeral.Count == 0)
            listaGeral.Children.Add(CabecalhoGeral(pendente ? "Procurando…" : $"Nada encontrado para “{termo}”.", vazio: true));
        rodapeGeral.Text = (pendente ? "Procurando também nas pastas dos processos e no texto das leis…  " : "")
            + (linhasGeral.Count > 0 ? "↑↓ escolhem · Enter abre · Esc fecha" : "Esc fecha");
        // A escolha continua no mesmo item quando a lista se completa; sem escolha, só os módulos sem pesquisa própria já escolhem o
        // primeiro (nos outros, Enter continua sendo a pesquisa do módulo até o usuário descer na lista).
        var indice = chaveAntes is null ? -1 : linhasGeral.FindIndex(l => l.Item.Chave == chaveAntes);
        if (indice < 0 && linhasGeral.Count > 0 && (selecionarPrimeiro || !pesquisaDoModuloHabilitada)) indice = 0;
        AbrirListaGeral();
        SelecionarGeral(indice, anunciar: false);
        if (!mesmoTermo || !pendente)
            Anunciar(linhasGeral.Count == 0 ? (pendente ? "Procurando" : "Nenhum resultado")
                : linhasGeral.Count == 1 ? "1 resultado" : $"{linhasGeral.Count.ToString(CultureInfo.InvariantCulture)} resultados");
    }

    void AbrirListaGeral()
    {
        molduraGeral.RequestedTheme = Raiz.ActualTheme;
        PosicionarListaGeral();
        if (!ListaGeralAberta)
        {
            rolagemGeral.ChangeView(null, 0, null, disableAnimation: true);
            molduraGeral.Visibility = Visibility.Visible;
        }
    }

    void PosicionarListaGeral()
    {
        var canto = CaixaPesquisa.TransformToVisual(Raiz).TransformPoint(new Windows.Foundation.Point(0, CaixaPesquisa.ActualHeight));
        var topo = Math.Max(canto.Y + 4, BarraTitulo.ActualHeight + 2);
        molduraGeral.Width = Math.Min(480, Raiz.ActualWidth);
        molduraGeral.MaxHeight = Math.Max(0, Raiz.ActualHeight - topo);
        molduraGeral.Margin = new Thickness(Math.Clamp(canto.X, 0, Raiz.ActualWidth - molduraGeral.Width), topo, 0, 0);
    }

    static FrameworkElement CabecalhoGeral(string texto, bool vazio = false)
    {
        var t = new TextBlock
        {
            Text = texto,
            TextWrapping = TextWrapping.Wrap,
            Margin = vazio ? new Thickness(10, 12, 10, 12) : new Thickness(10, 10, 10, 4),
            Style = (Style)Application.Current.Resources[vazio ? "PmvniSecondaryTextStyle" : "PmvniPainelCartaoTitulo"],
        };
        if (!vazio) { AutomationProperties.SetName(t, texto); AutomationProperties.SetHeadingLevel(t, AutomationHeadingLevel.Level2); }
        return t;
    }

    Grid LinhaGeral(ItemPesquisaGeral item, int indice)
    {
        var linha = (Grid)XamlReader.Load("""
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                  Background="Transparent" MinHeight="44" Margin="0,1,0,1">
              <Border x:Name="Realce" CornerRadius="4" Opacity="0" Background="{ThemeResource SubtleFillColorSecondaryBrush}"/>
              <Border x:Name="Marca" Width="3" Height="18" CornerRadius="1.5" Opacity="0" HorizontalAlignment="Left" VerticalAlignment="Center"
                      Background="{ThemeResource AccentFillColorDefaultBrush}"/>
              <Grid Padding="12,6,12,6" ColumnSpacing="12">
                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <FontIcon x:Name="Icone" VerticalAlignment="Center" AutomationProperties.AccessibilityView="Raw"/>
                <Border Grid.Column="2" VerticalAlignment="Center" CornerRadius="8" Padding="8,1,8,2" Background="{ThemeResource SubtleFillColorSecondaryBrush}">
                  <TextBlock x:Name="Categoria"/>
                </Border>
                <StackPanel Grid.Column="1" VerticalAlignment="Center" Spacing="1">
                  <TextBlock x:Name="Titulo" TextTrimming="CharacterEllipsis" MaxLines="1"/>
                  <TextBlock x:Name="Onde" TextTrimming="CharacterEllipsis" MaxLines="1"/>
                </StackPanel>
              </Grid>
            </Grid>
            """);
        var icone = (FontIcon)linha.FindName("Icone");
        icone.Glyph = item switch
        {
            { Tipo: TipoResultadoGeral.Funcao, Dado: string id } => FaixaDeOpcoes.GlifoDoComando(id, item.Glifo),
            { Tipo: TipoResultadoGeral.Modulo, Modulo: Modulo modulo } => Glifos.DoModulo(modulo),
            _ => item.Glifo,
        };
        icone.Style = (Style)Application.Current.Resources["PmvniIconeAcaoSecundario"];
        // 1.0.0: o trecho que casa com o termo fica em negrito (a cor continua a do tema, claro ou escuro).
        var titulo = (TextBlock)linha.FindName("Titulo");
        titulo.Style = (Style)Application.Current.Resources["PmvniAjusteTitulo"];
        titulo.TextWrapping = TextWrapping.NoWrap;
        foreach (var (texto, marcado) in PesquisaGeral.Destacar(item.Titulo, termoGeralMostrado))
        {
            var trecho = new Microsoft.UI.Xaml.Documents.Run { Text = texto };
            if (marcado) trecho.FontWeight = Microsoft.UI.Text.FontWeights.Bold;
            titulo.Inlines.Add(trecho);
        }
        var categoria = (TextBlock)linha.FindName("Categoria");
        var onde = (TextBlock)linha.FindName("Onde");
        categoria.Style = onde.Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"];
        categoria.Text = item.Categoria;
        onde.Text = item.Onde;
        AutomationProperties.SetName(linha, item.NomeAcessivel);
        ToolTipService.SetToolTip(linha, item.NomeAcessivel);
        // Pressionar não tira o foco da caixa (a linha não é focável); o resultado abre ao soltar sobre a mesma linha.
        linha.PointerPressed += (_, e) => { SelecionarGeral(indice, anunciar: false); pressionandoLinhaGeral = true; e.Handled = true; };
        linha.PointerExited += (_, _) => pressionandoLinhaGeral = false;
        linha.PointerCaptureLost += (_, _) => pressionandoLinhaGeral = false;
        linha.PointerReleased += (_, e) =>
        {
            if (!pressionandoLinhaGeral) return;
            pressionandoLinhaGeral = false;
            e.Handled = true;
            _ = EscolherResultadoGeralAsync(item);
        };
        return linha;
    }

    // ---------- escolher um resultado ----------

    /// <summary>Fecha a lista, limpa a caixa (o filtro do módulo volta ao normal, como apagar o termo) e vai ao resultado.</summary>
    internal async Task EscolherResultadoGeralAsync(ItemPesquisaGeral item)
    {
        esperaGeral.Stop();
        pesquisaGeralCts?.Cancel();
        FecharPesquisaGeral();
        if (CaixaPesquisa.Text.Length > 0) CaixaPesquisa.Text = "";
        await TentarAsync(() => IrAoResultadoGeralAsync(item));
    }

    async Task IrAoResultadoGeralAsync(ItemPesquisaGeral item)
    {
        switch (item.Tipo)
        {
            case TipoResultadoGeral.Modulo when item.Modulo is { } m:
                await MudarModuloAsync(m);
                break;
            case TipoResultadoGeral.Funcao when item.Dado is string id:
                // O comando vale no módulo à vista se a faixa dele o tem (Arquivo, Ajuda, Novo despacho…); senão, vai ao módulo dele primeiro.
                var alvo = Faixas.Comandos(Faixas.Para(moduloAtual)).Any(c => c.Id == id) ? moduloAtual : item.Modulo ?? moduloAtual;
                if (alvo != moduloAtual)
                {
                    await MudarModuloAsync(alvo);
                    if (moduloAtual != alvo) return;
                }
                await ExecutarComandoAsync(id);
                break;
            case TipoResultadoGeral.Ajuste when item.Dado is OpcaoAjusteTela opcao:
                await MudarModuloAsync(Modulo.Ajustes);
                if (moduloAtual == Modulo.Ajustes) PaginaDeAjustes.MostrarOpcao(opcao);
                break;
            case TipoResultadoGeral.Processo when item.Dado is Processo p:
                await IrAoProcessoPelaPesquisaAsync(p);
                break;
            case TipoResultadoGeral.Acompanhamento when item.Dado is string chave:
                await IrAoAcompanhamentoPelaPesquisaAsync(chave);
                break;
            case TipoResultadoGeral.Relatorio when item.Dado is string caminho:
                if (!File.Exists(caminho)) { MostrarMensagem(InfoBarSeverity.Warning, "O relatório não foi encontrado", caminho); return; }
                await NaFilaAsync(() => TentarAsync(() => AbrirPacoteAsync(caminho)));
                break;
            case TipoResultadoGeral.Despacho or TipoResultadoGeral.Documento when item.Dado is string caminho:
                if (!File.Exists(caminho)) { MostrarMensagem(InfoBarSeverity.Warning, "O arquivo não foi encontrado", caminho); return; }
                AbrirNoPrograma(caminho);
                break;
            case TipoResultadoGeral.Lei when item.Dado is string idLei:
                await MudarModuloAsync(Modulo.Legislacao);
                if (moduloAtual == Modulo.Legislacao) await AbrirLeiAsync(idLei);
                break;
            case TipoResultadoGeral.TrechoDeLei when item.Dado is TrechoAchado t:
                await MudarModuloAsync(Modulo.Legislacao);
                if (moduloAtual == Modulo.Legislacao) await AbrirLeiAsync(t.Resultado.AnexoId, t.Resultado.Pagina, t.Termo);
                break;
        }
    }

    /// <summary>Como "Ir ao processo" dos Despachos: o processo à vista na lista (sem filtro de ano nem de termo), escolhido, com o detalhe lido.</summary>
    async Task IrAoProcessoPelaPesquisaAsync(Processo p)
    {
        anoProcessos = null;
        termosPesquisa[Modulo.Processos] = "";
        processoEscolhido = p;
        entradaEscolhida = null;
        detalheAtual = null;
        SincronizarPastas();
        if (moduloAtual == Modulo.Processos) MontarModulo(Modulo.Processos); // já à vista: monta de novo com o escolhido
        else await MudarModuloAsync(Modulo.Processos);
    }

    /// <summary>Abre o Acompanhamento com a busca da planilha no número do processo e a linha dele escolhida (o painel do processo aberto).</summary>
    async Task IrAoAcompanhamentoPelaPesquisaAsync(string chave)
    {
        await MudarModuloAsync(Modulo.Acompanhamento);
        if (moduloAtual != Modulo.Acompanhamento) return;
        // A planilha pode estar sendo lida (entrar no módulo relê se mudou): espera um pouco por ela.
        for (var i = 0; i < 40 && (planilha is null || Ac.Progresso.Visibility == Visibility.Visible); i++) await Task.Delay(75);
        if (planilha is null || moduloAtual != Modulo.Acompanhamento) return;
        definindoPesquisa = true;
        try { CaixaPesquisa.Text = chave; }
        finally { definindoPesquisa = false; }
        termosPesquisa[Modulo.Acompanhamento] = chave;
        Ac.CaixaBusca.Text = chave; // filtra a tabela (PreencherLista)
        var item = Ac.Lista.Items.OfType<FrameworkElement>().FirstOrDefault(f => f.Tag is LinhaDaPlanilha l && l.ProcessoNormalizado == chave);
        if (item is not null)
        {
            Ac.Lista.SelectedItem = item; // mostra o painel do processo
            Ac.Lista.ScrollIntoView(item);
        }
    }
}
