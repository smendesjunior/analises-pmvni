using Pmvni.Composicao.Web.Legislacao;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Leitura padronizada da lei aberta, com resumo editável por artigo, figuras do PDF e navegação pelo sumário.</summary>
public sealed partial class MainWindow
{
    static Style EstiloLeitura(string chave) => (Style)Application.Current.Resources[chave];

    const string DicaResumoAutomatico = "Resumo automático do texto do artigo (não substitui a leitura). Clique no lápis para escrever outro.";
    internal const string AvisoSemImagens = "Não foi possível preparar as imagens desta lei. Figuras, tabelas em imagem e anexos gráficos estão no PDF original.";
    const string DicaResumoEditado = "Resumo escrito por você. Clique no lápis para mudar ou voltar ao automático.";

    bool leituraPadronizadaLigada;
    /// <summary>Os tópicos (Título, Capítulo, Seção) e artigos da lei aberta, com o bloco da folha a que levam: o cartão da lei na lista
    /// mostra esses itens (2026-11-16, no lugar da coluna "Neste documento").</summary>
    readonly List<(BlocoLei Bloco, FrameworkElement Alvo)> topicosLei = [];
    readonly Dictionary<string, (TextBlock Folha, TextBlock Sumario, string Automatico)> resumosNaTela = new(StringComparer.Ordinal);
    Dictionary<string, string> resumosEditados = new(StringComparer.Ordinal);
    IReadOnlyList<string>? paginasLeitura;
    CancellationTokenSource? figurasLeitura;

    readonly List<(BlocoLei Bloco, Border Moldura)> blocosDoLeitor = [];
    Border? molduraAtiva;
    TextBlock? textoRealcado;
    string buscaLeitor = "";
    int indiceBusca = -1;

    void LimparRealce()
    {
        molduraAtiva?.ClearValue(Border.BorderBrushProperty);
        textoRealcado?.TextHighlighters.Clear();
        molduraAtiva = null;
        textoRealcado = null;
    }

    void RealcarBloco(Border alvo, bool navegar)
    {
        LimparRealce();
        molduraAtiva = alvo;
        alvo.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        if (navegar)
        {
            P.RolagemPadronizada.UpdateLayout();
            alvo.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = false });
        }
    }

    static IEnumerable<TextBlock> TextosDoBloco(UIElement? el)
    {
        if (el is TextBlock texto) yield return texto;
        if (el is Panel painel)
            foreach (var filho in painel.Children)
                foreach (var t in TextosDoBloco(filho)) yield return t;
        if (el is Border borda)
            foreach (var t in TextosDoBloco(borda.Child)) yield return t;
    }

    ProcuraLei BuscarNaLeitura(string termo, int passo, int pagina, bool navegar = true)
    {
        var chave = Pmvni.Legislacao.TextoPesquisa.Normalizar(termo);
        var ocorrencias = new List<(Border Alvo, TextBlock Texto, int Inicio, int Tamanho, int Pagina)>();
        if (chave.Length > 0)
        {
            foreach (var (bloco, alvo) in blocosDoLeitor)
            {
                if (bloco.Tipo == TipoBlocoLei.Figura) continue;
                foreach (var texto in TextosDoBloco(alvo.Child))
                {
                    if (bloco.Tipo == TipoBlocoLei.Artigo && resumosNaTela.TryGetValue(bloco.Chave, out var resumo) && ReferenceEquals(resumo.Folha, texto)) continue;
                    var (normal, mapa) = Pmvni.Legislacao.TextoPesquisa.ComMapa(texto.Text);
                    for (int i = normal.IndexOf(chave, StringComparison.Ordinal); i >= 0; i = normal.IndexOf(chave, i + chave.Length, StringComparison.Ordinal))
                    {
                        int fim = i + chave.Length;
                        ocorrencias.Add((alvo, texto, mapa[i], (fim < mapa.Length ? mapa[fim] : texto.Text.Length) - mapa[i], bloco.Pagina));
                    }
                }
            }
        }
        LimparRealce();
        if (ocorrencias.Count == 0) { buscaLeitor = chave; indiceBusca = -1; return new(0, 0, null); }
        if (buscaLeitor != chave || indiceBusca < 0)
        {
            indiceBusca = ocorrencias.FindIndex(o => o.Pagina >= pagina);
            if (indiceBusca < 0) indiceBusca = 0;
        }
        else indiceBusca = (indiceBusca + passo + ocorrencias.Count) % ocorrencias.Count;
        buscaLeitor = chave;
        var atual = ocorrencias[indiceBusca];
        RealcarBloco(atual.Alvo, navegar);
        var marca = new TextHighlighter();
        marca.Ranges.Add(new TextRange { StartIndex = atual.Inicio, Length = atual.Tamanho });
        atual.Texto.TextHighlighters.Add(marca);
        textoRealcado = atual.Texto;
        return new(ocorrencias.Count, indiceBusca + 1, atual.Pagina);
    }

    void IniciarLeituraPadronizada()
    {
        P.TextoPadronizado.RequestedTheme = ElementTheme.Default;
        P.RolagemPadronizada.ClearValue(Control.BackgroundProperty);
        P.RolagemPadronizada.Style = EstiloLeitura("PmvniRolagemFundoStyle");
        P.ModoLeitura.SelectionChanged += (_, _) => AplicarModoLeitura();
    }

    /// <summary>Depois de abrir a lei: estrutura o texto (fora da thread da janela) com as figuras e os resumos guardados e mostra a
    /// leitura padronizada, se houver texto. Figuras ainda não lidas: a leitura aparece já, com o aviso "Preparando as imagens…", e é
    /// redesenhada quando o extrator as entregar (uma vez por PDF; depois ficam guardadas).</summary>
    async Task MostrarLeituraPadronizadaAsync(Anexo anexo)
    {
        figurasLeitura?.Cancel();
        buscaLeitor = "";
        indiceBusca = -1;
        var id = anexo.Id;
        var (paginas, figuras, resumos) = await Task.Run(() =>
        {
            var a = legislacao.Acervo;
            return (a.TextoPorPagina(id), a.Figuras(id), a.Resumos(id));
        });
        var blocos = paginas is null ? [] : await Task.Run(() => LeituraPadronizada.Estruturar(paginas, figuras));
        if (fechada || leiAberta?.Id != id) return;
        paginasLeitura = paginas;
        resumosEditados = new Dictionary<string, string>(resumos, StringComparer.Ordinal);
        var semImagens = figuras is null && blocos.Count > 0;
        Desenhar(anexo, blocos, semImagens ? EstadoFiguras.Preparando : EstadoFiguras.Prontas);
        P.ModoLeitura.Visibility = blocos.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        leituraPadronizadaLigada = blocos.Count > 0;
        P.ModoLeitura.SelectedItem = leituraPadronizadaLigada ? P.ModoPadronizado : P.ModoPdf;
        AplicarModoLeitura();
        if (semImagens)
        {
            var cts = figurasLeitura = new CancellationTokenSource();
            _ = PrepararFigurasAsync(anexo, cts.Token);
        }
    }

    enum EstadoFiguras { Prontas, Preparando, Falharam }

    async Task PrepararFigurasAsync(Anexo anexo, CancellationToken ct)
    {
        try
        {
            var figuras = await legislacao.FigurasAsync(anexo.Id, ct);
            if (fechada || ct.IsCancellationRequested || leiAberta?.Id != anexo.Id || !leituraPadronizadaLigada || paginasLeitura is not { } paginas) return;
            var blocos = await Task.Run(() => LeituraPadronizada.Estruturar(paginas, figuras), ct);
            if (fechada || ct.IsCancellationRequested || leiAberta?.Id != anexo.Id || !leituraPadronizadaLigada) return;
            var selecionado = blocosDoLeitor.FirstOrDefault(x => x.Moldura == molduraAtiva).Bloco;
            var deslocamento = P.RolagemPadronizada.VerticalOffset;
            Desenhar(anexo, blocos, figuras is null ? EstadoFiguras.Falharam : EstadoFiguras.Prontas);
            P.RolagemPadronizada.UpdateLayout();
            if (buscaLeitor.Length > 0) BuscarNaLeitura(buscaLeitor, 0, paginaLei, false);
            else if (selecionado != null && blocosDoLeitor.FirstOrDefault(x => x.Bloco == selecionado).Moldura is { } alvo) RealcarBloco(alvo, false);
            P.RolagemPadronizada.ChangeView(null, deslocamento, null, true);
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception e) { RegistroErros.Gravar("legislacao.figuras", e); }
    }

    void EsconderLeituraPadronizada()
    {
        figurasLeitura?.Cancel();
        figurasLeitura = null;
        leituraPadronizadaLigada = false;
        paginasLeitura = null;
        P.ModoLeitura.Visibility = Visibility.Collapsed;
        P.VistaPadronizada.Visibility = Visibility.Collapsed;
        P.TextoPadronizado.Children.Clear();
        LimparRealce();
        blocosDoLeitor.Clear();
        topicosLei.Clear();
        resumosNaTela.Clear();
        AtualizarTopicosNaLista();
    }

    void AplicarModoLeitura()
    {
        var padronizada = leituraPadronizadaLigada && ReferenceEquals(P.ModoLeitura.SelectedItem, P.ModoPadronizado);
        P.VistaPadronizada.Visibility = padronizada ? Visibility.Visible : Visibility.Collapsed;
        P.Leitor.Visibility = padronizada ? Visibility.Collapsed : Visibility.Visible;
    }

    void Desenhar(Anexo anexo, IReadOnlyList<BlocoLei> blocos, EstadoFiguras estado)
    {
        var t = P.TextoPadronizado;
        t.Children.Clear();
        LimparRealce();
        blocosDoLeitor.Clear();
        topicosLei.Clear();
        resumosNaTela.Clear();
        t.Children.Add(CabecalhoDaFolha());
        // Selos de cima: a esfera, o aviso de que é a leitura do texto do PDF e o andamento das imagens.
        var selos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        selos.Children.Add(Selo(Esferas.Rotulo(anexo.Esfera)));
        selos.Children.Add(Selo("Leitura padronizada do texto do PDF"));
        if (estado == EstadoFiguras.Preparando)
        {
            var andamento = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            andamento.Children.Add(new ProgressRing { IsActive = true, Width = 14, Height = 14 });
            andamento.Children.Add(CartoesDePainel.Apoio("Preparando as imagens do PDF…"));
            selos.Children.Add(new Border { Style = EstiloLeitura("PmvniSeloNeutro"), Padding = new Thickness(12, 4, 14, 5), Child = andamento });
        }
        if (estado == EstadoFiguras.Falharam)
        {
            // Aviso discreto: a leitura segue normal, só sem as imagens (o PDF original as tem).
            var semImagens = Selo("Sem as imagens do PDF", alerta: true);
            ToolTipService.SetToolTip(semImagens, AvisoSemImagens);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(semImagens, AvisoSemImagens);
            selos.Children.Add(semImagens);
        }
        t.Children.Add(selos);
        if (!blocos.Any(b => b.Tipo == TipoBlocoLei.Epigrafe)) t.Children.Add(Epigrafe(anexo.Titulo));
        foreach (var b in blocos)
        {
            FrameworkElement el = b.Tipo switch
            {
                TipoBlocoLei.Epigrafe => Epigrafe(b.Texto),
                TipoBlocoLei.Ementa => Ementa(b.Texto),
                TipoBlocoLei.Titulo => Titulo(b),
                TipoBlocoLei.Capitulo => Capitulo(b),
                TipoBlocoLei.Secao or TipoBlocoLei.Subsecao => Secao(b),
                TipoBlocoLei.Artigo => Artigo(anexo, b),
                TipoBlocoLei.Figura => Figura(b),
                _ => Dispositivo(b),
            };
            var moldura = new Border { BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(6, 0, 0, 0), Child = el };
            t.Children.Add(moldura);
            blocosDoLeitor.Add((b, moldura));
            if (b.Tipo is TipoBlocoLei.Titulo or TipoBlocoLei.Capitulo or TipoBlocoLei.Secao or TipoBlocoLei.Subsecao or TipoBlocoLei.Artigo)
                topicosLei.Add((b, moldura));
        }
        MenuTexto.Aplicar(t);
        P.RolagemPadronizada.ChangeView(null, 0, null, true);
        AtualizarTopicosNaLista();
    }

    /// <summary>Um item dos tópicos no cartão da lei: "Art. 1º" e o resumo (automático em itálico; o escrito pelo usuário, normal), ou
    /// o título/capítulo/seção em negrito. Clicar leva a folha até o bloco (e mostra a leitura padronizada, se estava no PDF).</summary>
    Button ItemDoTopico(BlocoLei b, FrameworkElement alvo)
    {
        var recuo = b.Tipo switch { TipoBlocoLei.Titulo => 0, TipoBlocoLei.Capitulo => 6, TipoBlocoLei.Secao or TipoBlocoLei.Subsecao => 12, _ => 16 };
        FrameworkElement conteudo;
        if (b.Tipo == TipoBlocoLei.Artigo)
        {
            var rotulo = Rotulinho(b.Rotulo);
            var resumo = new TextBlock
            {
                Style = EstiloLeitura("PmvniSecondaryTextStyle"), TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            var linha = new Grid { ColumnSpacing = 8 };
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            linha.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(resumo, 1);
            linha.Children.Add(rotulo);
            linha.Children.Add(resumo);
            if (resumosNaTela.TryGetValue(b.Chave, out var r)) resumosNaTela[b.Chave] = (r.Folha, resumo, r.Automatico);
            AplicarResumo(b.Chave);
            conteudo = linha;
        }
        else
        {
            conteudo = new TextBlock
            {
                Text = b.Rotulo + (b.Texto.Length > 0 ? " – " + b.Texto : ""), TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis, Style = EstiloLeitura("PmvniPainelCartaoTitulo"),
            };
        }
        var botao = new Button
        {
            Content = conteudo, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(6 + recuo, 3, 6, 3), Margin = new Thickness(0, b.Tipo == TipoBlocoLei.Artigo ? 0 : 4, 0, 0),
            Style = EstiloLeitura("PmvniBarraButtonStyle"),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(botao, b.Tipo == TipoBlocoLei.Artigo ? b.Rotulo : b.Rotulo + " " + b.Texto);
        ToolTipService.SetToolTip(botao, "Ir para " + b.Rotulo);
        botao.Click += (_, _) =>
        {
            if (leituraPadronizadaLigada && !ReferenceEquals(P.ModoLeitura.SelectedItem, P.ModoPadronizado))
            {
                P.ModoLeitura.SelectedItem = P.ModoPadronizado;
                AplicarModoLeitura();
                P.RolagemPadronizada.UpdateLayout();
            }
            buscaLeitor = "";
            indiceBusca = -1;
            paginaLei = b.Pagina;
            AtualizarRodapeLegislacao();
            RealcarBloco((Border)alvo, true);
        };
        return botao;
    }


    // ---- Peças da folha ----

    static FrameworkElement CabecalhoDaFolha()
    {
        var topo = new Grid { ColumnSpacing = 14, Padding = new Thickness(0, 0, 0, 10) };
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        // Correção 2026-11-16: as leis não levam o brasão de Venda Nova (nem a linha que o separava).
        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 2 };
        textos.Children.Add(new TextBlock
        {
            Text = Pmvni.Composicao.Kit.Cabecalho.OrgaoPadrao, Style = EstiloLeitura("PmvniAjusteTitulo"), TextAlignment = TextAlignment.Right,
        });
        textos.Children.Add(new TextBlock
        {
            Text = "Leitura padronizada da legislação", Style = EstiloLeitura("PmvniSecondaryTextStyle"), TextAlignment = TextAlignment.Right,
        });
        Grid.SetColumn(textos, 2);
        topo.Children.Add(textos);
        var faixas = new Grid { Height = 4 };
        foreach (var (peso, estilo) in new[] { (48, "PmvniDivisoriaVerticalStyle"), (28, "PmvniSeloSucesso"), (24, "PmvniSeloAtencao") })
        {
            faixas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(peso, GridUnitType.Star) });
            var r = new Border { Style = EstiloLeitura(estilo), Padding = new Thickness(0), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(0) };
            Grid.SetColumn(r, faixas.ColumnDefinitions.Count - 1);
            faixas.Children.Add(r);
        }
        return new StackPanel { Margin = new Thickness(0, 0, 0, 14), Children = { topo, faixas } };
    }

    static BitmapImage ImagemDe(Stream origem)
    {
        var fluxo = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        var escrita = System.IO.WindowsRuntimeStreamExtensions.AsStreamForWrite(fluxo);
        origem.CopyTo(escrita);
        escrita.Flush();
        fluxo.Seek(0);
        var imagem = new BitmapImage();
        imagem.SetSource(fluxo);
        return imagem;
    }

    static Border Selo(string texto, bool alerta = false) => new()
    {
        Style = EstiloLeitura(alerta ? "PmvniSeloAtencao" : "PmvniSeloNeutro"),
        Padding = new Thickness(12, 5, 12, 6),
        Child = CartoesDePainel.Apoio(texto),
    };

    static FrameworkElement Nota(string titulo, string texto)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new FontIcon
        {
            Glyph = Glifos.Avisos, FontSize = 16, Width = 24, Height = 24,
            Style = EstiloLeitura("PmvniIconeAtencaoStyle"), VerticalAlignment = VerticalAlignment.Top,
        });
        var tb = CartoesDePainel.Apoio("");
        tb.Inlines.Add(new Bold { Inlines = { new Run { Text = titulo } } });
        tb.Inlines.Add(new Run { Text = "   " + texto });
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        return new Border
        {
            Style = EstiloLeitura("PmvniSeloAtencao"), Padding = new Thickness(14, 9, 14, 9),
            Margin = new Thickness(0, 4, 0, 6), Child = g,
        };
    }

    static TextBlock Rotulinho(string texto) => new()
    {
        Text = texto, Style = EstiloLeitura("PmvniPainelRotulo"),
    };

    static FrameworkElement Epigrafe(string texto)
    {
        var g = new Grid { ColumnSpacing = 14, Margin = new Thickness(0, 10, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border { Style = EstiloLeitura("PmvniDivisoriaVerticalStyle"), CornerRadius = new CornerRadius(4) });
        var tb = new TextBlock
        {
            Text = texto, Style = EstiloLeitura("PmvniTituloPaginaStyle"), IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        return g;
    }

    static FrameworkElement Ementa(string texto) => new Border
    {
        Style = EstiloLeitura("PmvniCartaoStyle"), Padding = new Thickness(18, 10, 18, 14), Margin = new Thickness(0, 4, 0, 12),
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Rotulinho("Ementa"),
                new TextBlock { Text = texto, Style = EstiloLeitura("PmvniPainelValor"), FontStyle = Windows.UI.Text.FontStyle.Italic, LineHeight = 24 },
            },
        },
    };

    static FrameworkElement Titulo(BlocoLei b)
    {
        var g = new Grid { Style = EstiloLeitura("PmvniGradeCartaoStyle"), Margin = new Thickness(0, 22, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border
        {
            Style = EstiloLeitura("PmvniSeloNeutro"), Padding = new Thickness(18, 10, 30, 10),
            VerticalAlignment = VerticalAlignment.Stretch, Child = Rotulinho(b.Rotulo),
        });
        var nome = new TextBlock
        {
            Text = b.Texto, Style = EstiloLeitura("PmvniTituloSecao"), TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(18, 8, 18, 8), IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(nome, 1);
        g.Children.Add(nome);
        return g;
    }

    static FrameworkElement Capitulo(BlocoLei b)
    {
        var painel = new StackPanel { Spacing = 2 };
        painel.Children.Add(Rotulinho(b.Rotulo));
        if (b.Texto.Length > 0)
            painel.Children.Add(new TextBlock { Text = b.Texto, Style = EstiloLeitura("PmvniTituloSecao"), TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        return new Border
        {
            Style = EstiloLeitura("PmvniCartaoStyle"), BorderThickness = new Thickness(0, 0, 0, 2),
            Padding = new Thickness(0, 0, 0, 8), Margin = new Thickness(0, 18, 0, 6), Child = painel,
        };
    }

    static FrameworkElement Secao(BlocoLei b)
    {
        var g = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 14, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border { Style = EstiloLeitura("PmvniDivisoriaVerticalStyle") });
        var tb = new TextBlock
        {
            Text = b.Rotulo + (b.Texto.Length > 0 ? " — " + b.Texto : ""),
            Style = EstiloLeitura("PmvniAjusteTitulo"), IsTextSelectionEnabled = true, Margin = new Thickness(0, 4, 0, 4),
        };
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        return g;
    }

    FrameworkElement Artigo(Anexo anexo, BlocoLei b)
    {
        var topo = new Grid { ColumnSpacing = 10 };
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.Children.Add(new Border
        {
            Style = EstiloLeitura("PmvniSeloNeutro"), Padding = new Thickness(11, 3, 11, 4), VerticalAlignment = VerticalAlignment.Center,
            Child = Rotulinho(b.Rotulo),
        });
        var resumo = new TextBlock
        {
            Style = EstiloLeitura("PmvniAjusteAjuda"),
            TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.WordEllipsis,
        };
        Grid.SetColumn(resumo, 1);
        topo.Children.Add(resumo);
        var lapis = new Button
        {
            Style = EstiloLeitura("PmvniBarraButtonStyle"), Width = 32, Height = 32, Padding = new Thickness(0),
            Content = new FontIcon { Glyph = Glifos.Editar, Style = EstiloLeitura("PmvniIconeSecundarioStyle") },
        };
        Dicas.Aplicar(lapis, "Escrever o resumo", "Troca o resumo automático deste artigo por um escrito por você.");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(lapis, "Escrever o resumo do " + b.Rotulo);
        lapis.Flyout = FlyoutDoResumo(anexo, b);
        Grid.SetColumn(lapis, 2);
        topo.Children.Add(lapis);
        resumosNaTela[b.Chave] = (resumo, resumo, b.Resumo);
        AplicarResumo(b.Chave);

        var corpo = new TextBlock
        {
            Text = b.Texto, Style = EstiloLeitura("PmvniPainelValor"), LineHeight = 27, TextAlignment = TextAlignment.Justify,
            Visibility = b.Texto.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        return new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0), Spacing = 8,
            Children = { new Border { Style = EstiloLeitura("PmvniCartaoStyle"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6), Child = topo }, corpo },
        };
    }

    /// <summary>Mostra o resumo do artigo na folha e no sumário: o escrito pelo usuário, se houver; senão o automático (itálico).</summary>
    void AplicarResumo(string chave)
    {
        if (!resumosNaTela.TryGetValue(chave, out var r)) return;
        var editado = resumosEditados.TryGetValue(chave, out var e) ? e : null;
        var texto = editado ?? r.Automatico;
        foreach (var tb in new[] { r.Folha, r.Sumario }.Distinct())
        {
            tb.Text = texto;
            tb.FontStyle = editado is null ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal;
            tb.Visibility = texto.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ToolTipService.SetToolTip(tb, editado is null ? DicaResumoAutomatico : DicaResumoEditado);
        }
    }

    Flyout FlyoutDoResumo(Anexo anexo, BlocoLei b)
    {
        var caixa = new TextBox
        {
            Header = "Resumo do " + b.Rotulo, PlaceholderText = b.Resumo, MaxLength = AcervoLegislacao.MaximoResumo, Width = 360, TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = false,
        };
        var salvar = new Button { Content = "Salvar", Style = EstiloLeitura("PmvniBotaoPrimarioStyle") };
        IconeDeAcao.SetGlifo(salvar, Glifos.Salvar);
        var automatico = new Button { Content = "Voltar ao automático" };
        IconeDeAcao.SetGlifo(automatico, Glifos.Restaurar);
        Dicas.Aplicar(salvar, "Salvar", "Guarda o seu resumo deste artigo com os dados da lei.");
        var nota = new TextBlock
        {
            Text = "O resumo automático é tirado do próprio texto do artigo. O seu fica guardado com os dados da lei; o PDF não muda.",
            Style = EstiloLeitura("PmvniAjusteAjuda"), Width = 360,
        };
        var flyout = new Flyout
        {
            Content = new StackPanel { Spacing = 10, Children = { caixa, nota, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { salvar, automatico } } } },
        };
        flyout.Opening += (_, _) =>
        {
            caixa.Text = resumosEditados.TryGetValue(b.Chave, out var atual) ? atual : b.Resumo;
            automatico.IsEnabled = resumosEditados.ContainsKey(b.Chave);
            Dicas.Aplicar(automatico, "Voltar ao automático", "Apaga o seu resumo e volta a usar o tirado do texto do artigo.",
                motivo: automatico.IsEnabled ? null : "O resumo deste artigo já é automático.");
        };
        async void Gravar(string? texto)
        {
            flyout.Hide();
            var t = (texto ?? "").Trim();
            if (t == b.Resumo) t = ""; // igual ao automático: não precisa guardar
            try
            {
                await Task.Run(() => legislacao.Acervo.DefinirResumo(anexo.Id, b.Chave, t));
                if (t.Length == 0) resumosEditados.Remove(b.Chave);
                else resumosEditados[b.Chave] = t.Length > AcervoLegislacao.MaximoResumo ? t[..AcervoLegislacao.MaximoResumo].TrimEnd() : t;
                AplicarResumo(b.Chave);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException or KeyNotFoundException or ArgumentException)
            {
                RegistroErros.Gravar("legislacao.resumo", e);
                MostrarMensagem(InfoBarSeverity.Error, "Não foi possível guardar o resumo", Erro(e));
            }
        }
        salvar.Click += (_, _) => Gravar(caixa.Text);
        automatico.Click += (_, _) => Gravar(null);
        caixa.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; Gravar(caixa.Text); } };
        return flyout;
    }

    FrameworkElement Figura(BlocoLei b)
    {
        var f = b.Figura!;
        var painel = new StackPanel { Spacing = 8 };
        var paginaInteira = f.Origem is OrigemFigura.Pagina or OrigemFigura.Falhou;
        if (paginaInteira)
            painel.Children.Add(Nota(b.Rotulo, f.Origem == OrigemFigura.Pagina
                ? "Esta página do PDF tem conteúdo gráfico que não pôde ser separado do texto: abaixo, a página inteira."
                : $"Não foi possível mostrar este conteúdo aqui. Veja a página {f.Pagina} em PDF original."));
        else painel.Children.Add(Rotulinho(b.Rotulo));
        if (f.Dados.Length > 0)
        {
            try
            {
                using var dados = new MemoryStream(f.Dados, writable: false);
                var imagem = new Image
                {
                    Source = ImagemDe(dados), Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
                    MaxWidth = Math.Max(48, f.Largura * 96 / 72),
                };
                ToolTipService.SetToolTip(imagem, $"Recorte da página {f.Pagina} do PDF");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(imagem, b.Rotulo);
                painel.Children.Add(imagem);
            }
            catch (Exception e)
            {
                RegistroErros.Gravar("legislacao.figura", e);
                if (!paginaInteira) painel.Children.Add(Nota(b.Rotulo, $"Não foi possível mostrar esta figura. Veja a página {f.Pagina} em PDF original."));
            }
        }
        return new Border
        {
            Style = EstiloLeitura("PmvniCartaoStyle"),
            Padding = new Thickness(14, 10, 14, 14), Margin = new Thickness(0, 10, 0, 6), Child = painel,
        };
    }

    static FrameworkElement Dispositivo(BlocoLei b)
    {
        var recuo = b.Tipo switch
        {
            TipoBlocoLei.Paragrafo => 24,
            TipoBlocoLei.Inciso => 44,
            TipoBlocoLei.Alinea => 66,
            _ => 0,
        };
        var tb = new TextBlock
        {
            Style = EstiloLeitura("PmvniPainelValor"), LineHeight = 26, TextAlignment = TextAlignment.Justify,
            Margin = new Thickness(recuo, 3, 0, 3),
        };
        if (b.Rotulo.Length > 0) tb.Inlines.Add(new Bold { Inlines = { new Run { Text = b.Rotulo + " " } } });
        tb.Inlines.Add(new Run { Text = b.Texto });
        if (b.Tipo != TipoBlocoLei.Paragrafo) return tb;
        // Parágrafo: um fio claro à esquerda, como a caixa de fundamentação do relatório.
        tb.Margin = new Thickness(0);
        return new Border { Style = EstiloLeitura("PmvniCartaoStyle"), BorderThickness = new Thickness(3, 0, 0, 0), Margin = new Thickness(recuo - 12, 4, 0, 4), Padding = new Thickness(9, 0, 0, 0), Child = tb };
    }
}
