using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Legislação › Leitura padronizada (correções 2026-11-15): o texto da lei aberta, estruturado pelo processador interno
/// (<see cref="LeituraPadronizada"/>), numa folha clara com as cores e a tipografia do relatório oficial do kit (cabeçalho com o brasão e
/// as faixas azul, verde e amarela; epígrafe com a barra azul; títulos na faixa azul-marinho; selo "Art. N" azul-marinho), o resumo
/// automático de cada artigo (que o usuário pode trocar) e as figuras do PDF no ponto em que aparecem. O sumário à esquerda leva ao bloco.
/// "PDF original" volta ao leitor de PDF.</summary>
public sealed partial class MainWindow
{
    /// <summary>A paleta do relatório oficial (relatorio.css, rev10) e das faixas do cabeçalho (Cabecalho.cs): a folha é sempre clara,
    /// como papel, com o app em tema claro ou escuro.</summary>
    static class CoresLeitura
    {
        static SolidColorBrush C(uint rgb) => new(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        public static readonly SolidColorBrush Marinho = C(0x142D47);      // --navy: títulos, cabeçalho de tabela, selo do artigo
        public static readonly SolidColorBrush Azul = C(0x1A5A94);         // --blue: rótulos
        public static readonly SolidColorBrush AzulSuave = C(0xEEF4FB);    // --blue-s
        public static readonly SolidColorBrush Faixa = C(0xE4EEF9);        // .band
        public static readonly SolidColorBrush Linha = C(0xDDE4EB);        // --line
        public static readonly SolidColorBrush LinhaClara = C(0xEDF1F5);   // .ch
        public static readonly SolidColorBrush Tinta = C(0x161E28);        // --ink
        public static readonly SolidColorBrush Apoio = C(0x3F4A58);        // --mu
        public static readonly SolidColorBrush Titulo = C(0x1A2533);       // .ttl h1
        public static readonly SolidColorBrush Orgao = C(0x1D3D5E);        // cabeçalho: nome do órgão
        public static readonly SolidColorBrush Subtitulo = C(0x637083);    // cabeçalho: "RELATÓRIO TÉCNICO…"
        public static readonly SolidColorBrush Cinza = C(0xF1F3F6);        // .dates span
        public static readonly SolidColorBrush FaixaAzul = C(0x2470B3);    // faixas do cabeçalho (48 %, 28 %, 24 %)
        public static readonly SolidColorBrush FaixaVerde = C(0x2F9E62);
        public static readonly SolidColorBrush FaixaAmarela = C(0xF2C43D);
        public static readonly SolidColorBrush Verde = C(0x49B077);        // --g-b
        public static readonly SolidColorBrush Amarelo = C(0xF2C43D);      // --a-b
        public static readonly SolidColorBrush AmareloSuave = C(0xFFF5D9); // --a-s (.note)
        public static readonly SolidColorBrush AmareloTexto = C(0xA87500); // --a
        public static readonly SolidColorBrush Branco = new(Colors.White);
        public static readonly LinearGradientBrush Banda = new()
        {
            StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(1, 0),
            GradientStops = { new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x1E, 0x4F, 0x86), Offset = 0 }, new GradientStop { Color = Windows.UI.Color.FromArgb(255, 0x23, 0x64, 0xA6), Offset = 1 } },
        };
        public static readonly FontFamily Fonte = new("Verdana");
    }

    const string DicaResumoAutomatico = "Resumo automático do texto do artigo (não substitui a leitura). Clique no lápis para escrever outro.";
    const string DicaResumoEditado = "Resumo escrito por você. Clique no lápis para mudar ou voltar ao automático.";

    bool leituraPadronizadaLigada;
    readonly Dictionary<object, FrameworkElement> alvosSumario = [];
    readonly Dictionary<string, (TextBlock Folha, TextBlock Sumario, string Automatico)> resumosNaTela = new(StringComparer.Ordinal);
    Dictionary<string, string> resumosEditados = new(StringComparer.Ordinal);
    IReadOnlyList<string>? paginasLeitura;
    CancellationTokenSource? figurasLeitura;

    void IniciarLeituraPadronizada()
    {
        P.ModoLeitura.SelectionChanged += (_, _) => AplicarModoLeitura();
        P.SumarioLei.ItemClick += (_, e) =>
        {
            if (e.ClickedItem is ListViewItem { Tag: FrameworkElement alvo } || (e.ClickedItem is FrameworkElement f && alvosSumario.TryGetValue(f, out alvo!)))
                alvo.StartBringIntoView(new BringIntoViewOptions { VerticalAlignmentRatio = 0, AnimationDesired = true });
        };
    }

    /// <summary>Depois de abrir a lei: estrutura o texto (fora da thread da janela) com as figuras e os resumos guardados e mostra a
    /// leitura padronizada, se houver texto. Figuras ainda não lidas: a leitura aparece já, com o aviso "Preparando as imagens…", e é
    /// redesenhada quando o extrator as entregar (uma vez por PDF; depois ficam guardadas).</summary>
    async Task MostrarLeituraPadronizadaAsync(Anexo anexo)
    {
        figurasLeitura?.Cancel();
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
            var deslocamento = P.RolagemPadronizada.VerticalOffset;
            Desenhar(anexo, blocos, figuras is null ? EstadoFiguras.Falharam : EstadoFiguras.Prontas);
            P.RolagemPadronizada.UpdateLayout();
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
        P.SumarioLei.Items.Clear();
        resumosNaTela.Clear();
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
        P.SumarioLei.Items.Clear();
        alvosSumario.Clear();
        resumosNaTela.Clear();
        t.Children.Add(CabecalhoDaFolha());
        // Selos de cima: a esfera, o aviso de que é a leitura do texto do PDF e o andamento das imagens.
        var selos = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 6) };
        selos.Children.Add(Selo(Esferas.Rotulo(anexo.Esfera).ToUpperInvariant(), CoresLeitura.AzulSuave, CoresLeitura.Marinho, negrito: true));
        selos.Children.Add(Selo("Leitura padronizada do texto do PDF", CoresLeitura.Cinza, CoresLeitura.Apoio));
        if (estado == EstadoFiguras.Preparando)
        {
            var andamento = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            andamento.Children.Add(new ProgressRing { IsActive = true, Width = 14, Height = 14, Foreground = CoresLeitura.Azul });
            andamento.Children.Add(new TextBlock { Text = "Preparando as imagens do PDF…", Foreground = CoresLeitura.Apoio, FontSize = 13, FontFamily = CoresLeitura.Fonte });
            selos.Children.Add(new Border { Background = CoresLeitura.Cinza, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 4, 14, 5), Child = andamento });
        }
        t.Children.Add(selos);
        if (estado == EstadoFiguras.Falharam)
            t.Children.Add(Nota("Imagens do PDF", "Não foi possível preparar as imagens desta lei. Figuras, tabelas em imagem e anexos gráficos estão no PDF original."));
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
            t.Children.Add(el);
            if (b.Tipo is TipoBlocoLei.Titulo or TipoBlocoLei.Capitulo or TipoBlocoLei.Secao or TipoBlocoLei.Artigo)
                P.SumarioLei.Items.Add(ItemDoSumario(b, el));
        }
        P.RolagemPadronizada.ChangeView(null, 0, null, true);
    }

    ListViewItem ItemDoSumario(BlocoLei b, FrameworkElement alvo)
    {
        var recuo = b.Tipo switch { TipoBlocoLei.Titulo => 0, TipoBlocoLei.Capitulo => 12, TipoBlocoLei.Secao => 24, _ => 28 };
        FrameworkElement conteudo;
        if (b.Tipo == TipoBlocoLei.Artigo)
        {
            // "Art. 1º" e, embaixo, o resumo (automático em itálico; o escrito pelo usuário, normal).
            var rotulo = new TextBlock { Text = b.Rotulo, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            var resumo = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis };
            if (Application.Current.Resources.TryGetValue("PmvniSecondaryTextStyle", out var estilo) && estilo is Style s) resumo.Style = s;
            resumo.FontSize = 12;
            var painel = new StackPanel { Spacing = 1, Children = { rotulo, resumo } };
            if (resumosNaTela.TryGetValue(b.Chave, out var r)) resumosNaTela[b.Chave] = (r.Folha, resumo, r.Automatico);
            AplicarResumo(b.Chave);
            alvosSumario[painel] = alvo;
            conteudo = painel;
        }
        else
        {
            var texto = new TextBlock { Text = b.Rotulo + (b.Texto.Length > 0 ? " — " + b.Texto : ""), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
            alvosSumario[texto] = alvo;
            conteudo = texto;
        }
        return new ListViewItem { Tag = alvo, Padding = new Thickness(8 + recuo, 4, 8, 4), MinHeight = 28, Content = conteudo };
    }

    // ---- Peças da folha ----

    static FrameworkElement CabecalhoDaFolha()
    {
        // Como o cabeçalho do relatório: brasão | linha | órgão e subtítulo à direita; embaixo as faixas azul, verde e amarela.
        var topo = new Grid { ColumnSpacing = 14, Padding = new Thickness(0, 0, 0, 10) };
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        if (Brasao() is { } brasao) topo.Children.Add(new Image { Source = brasao, Height = 52, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
        var linha = new Border { Width = 1, Background = CoresLeitura.Linha, Margin = new Thickness(0, 6, 0, 6) };
        Grid.SetColumn(linha, 1);
        topo.Children.Add(linha);
        var textos = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 2 };
        textos.Children.Add(new TextBlock
        {
            Text = Pmvni.Composicao.Kit.Cabecalho.OrgaoPadrao, FontFamily = CoresLeitura.Fonte, FontWeight = FontWeights.Bold, FontSize = 16,
            Foreground = CoresLeitura.Orgao, TextAlignment = TextAlignment.Right, TextWrapping = TextWrapping.Wrap,
        });
        textos.Children.Add(new TextBlock
        {
            Text = "LEITURA PADRONIZADA DA LEGISLAÇÃO", FontFamily = CoresLeitura.Fonte, FontSize = 11.5, Foreground = CoresLeitura.Subtitulo,
            TextAlignment = TextAlignment.Right, CharacterSpacing = 20,
        });
        Grid.SetColumn(textos, 2);
        topo.Children.Add(textos);
        var faixas = new Grid { Height = 4 };
        foreach (var (peso, cor) in new[] { (48, CoresLeitura.FaixaAzul), (28, CoresLeitura.FaixaVerde), (24, CoresLeitura.FaixaAmarela) })
        {
            faixas.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(peso, GridUnitType.Star) });
            var r = new Border { Background = cor };
            Grid.SetColumn(r, faixas.ColumnDefinitions.Count - 1);
            faixas.Children.Add(r);
        }
        return new StackPanel { Margin = new Thickness(0, 0, 0, 14), Children = { topo, faixas } };
    }

    /// <summary>O brasão de texto escuro (a folha é sempre clara), do recurso embutido.</summary>
    static BitmapImage? Brasao()
    {
        try
        {
            using var recurso = typeof(MainWindow).Assembly.GetManifestResourceStream("Pmvni.App.brasao-claro.png");
            return recurso is null ? null : ImagemDe(recurso);
        }
        catch (Exception e)
        {
            RegistroErros.Gravar("leitura.brasao", e);
            return null;
        }
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

    static Border Selo(string texto, Brush fundo, Brush tinta, bool negrito = false) => new()
    {
        Background = fundo, CornerRadius = new CornerRadius(9), Padding = new Thickness(12, 5, 12, 6),
        Child = new TextBlock
        {
            Text = texto, Foreground = tinta, FontSize = 12.5, FontFamily = CoresLeitura.Fonte, CharacterSpacing = negrito ? 30 : 0,
            FontWeight = negrito ? FontWeights.Bold : FontWeights.Normal,
        },
    };

    /// <summary>Aviso no estilo da nota do relatório (fundo amarelo-claro, "!" amarelo).</summary>
    static FrameworkElement Nota(string titulo, string texto)
    {
        var g = new Grid { ColumnSpacing = 12 };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border
        {
            Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = CoresLeitura.Amarelo, VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock { Text = "!", FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Foreground = CoresLeitura.Titulo },
        });
        var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontFamily = CoresLeitura.Fonte, FontSize = 13.5, Foreground = CoresLeitura.Tinta };
        tb.Inlines.Add(new Run { Text = titulo.ToUpperInvariant(), FontWeight = FontWeights.Bold, Foreground = CoresLeitura.AmareloTexto });
        tb.Inlines.Add(new Run { Text = "   " + texto });
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        return new Border { Background = CoresLeitura.AmareloSuave, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 9, 14, 9), Margin = new Thickness(0, 4, 0, 6), Child = g };
    }

    static TextBlock Rotulinho(string texto, Brush? cor = null) => new()
    {
        Text = texto.ToUpperInvariant(), FontFamily = CoresLeitura.Fonte, FontSize = 11, FontWeight = FontWeights.Bold, CharacterSpacing = 50,
        Foreground = cor ?? CoresLeitura.Azul,
    };

    static FrameworkElement Epigrafe(string texto)
    {
        // Como o título da capa do relatório: barra azul de 6 px e o título grande.
        var g = new Grid { ColumnSpacing = 14, Margin = new Thickness(0, 10, 0, 6) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border { Background = CoresLeitura.Azul, CornerRadius = new CornerRadius(4) });
        var tb = new TextBlock
        {
            Text = texto.ToUpperInvariant(), FontFamily = CoresLeitura.Fonte, FontSize = 26, FontWeight = FontWeights.Bold, Foreground = CoresLeitura.Titulo,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, CharacterSpacing = -10,
        };
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        return g;
    }

    static FrameworkElement Ementa(string texto) => new Border
    {
        Background = CoresLeitura.AzulSuave, CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 10, 18, 14), Margin = new Thickness(0, 4, 0, 12),
        Child = new StackPanel
        {
            Spacing = 4,
            Children =
            {
                Rotulinho("Ementa"),
                new TextBlock { Text = texto, FontFamily = CoresLeitura.Fonte, FontStyle = Windows.UI.Text.FontStyle.Italic, FontSize = 15, LineHeight = 24, Foreground = CoresLeitura.Tinta, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true },
            },
        },
    };

    static FrameworkElement Titulo(BlocoLei b)
    {
        // A faixa da capa do relatório: o rótulo no degradê azul e o nome em azul-marinho sobre o azul-claro.
        var g = new Grid { Background = CoresLeitura.Faixa, CornerRadius = new CornerRadius(11), Margin = new Thickness(0, 22, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border
        {
            Background = CoresLeitura.Banda, CornerRadius = new CornerRadius(11, 0, 22, 11), Padding = new Thickness(18, 10, 30, 10),
            Child = new TextBlock { Text = b.Rotulo, FontFamily = CoresLeitura.Fonte, FontWeight = FontWeights.Bold, FontSize = 17, Foreground = CoresLeitura.Branco, VerticalAlignment = VerticalAlignment.Center },
        });
        var nome = new TextBlock
        {
            Text = b.Texto.ToUpperInvariant(), FontFamily = CoresLeitura.Fonte, FontWeight = FontWeights.Bold, FontSize = 17, Foreground = CoresLeitura.Marinho,
            VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(18, 8, 18, 8), IsTextSelectionEnabled = true,
        };
        Grid.SetColumn(nome, 1);
        g.Children.Add(nome);
        return g;
    }

    static FrameworkElement Capitulo(BlocoLei b)
    {
        // Como o título de seção do relatório: rótulo pequeno em azul e o nome grande em azul-marinho, sobre uma linha.
        var painel = new StackPanel { Spacing = 2 };
        painel.Children.Add(Rotulinho(b.Rotulo));
        if (b.Texto.Length > 0)
            painel.Children.Add(new TextBlock { Text = b.Texto.ToUpperInvariant(), FontFamily = CoresLeitura.Fonte, FontWeight = FontWeights.Bold, FontSize = 19, Foreground = CoresLeitura.Marinho, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true });
        return new Border { BorderBrush = CoresLeitura.Linha, BorderThickness = new Thickness(0, 0, 0, 2), Padding = new Thickness(0, 0, 0, 8), Margin = new Thickness(0, 18, 0, 6), Child = painel };
    }

    static FrameworkElement Secao(BlocoLei b)
    {
        // Como o título dos cartões do relatório: maiúsculas em azul-marinho com o número em azul, e a barra lateral (verde na seção,
        // amarela na subseção).
        var g = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 14, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.Children.Add(new Border { Background = b.Tipo == TipoBlocoLei.Subsecao ? CoresLeitura.Amarelo : CoresLeitura.Verde, CornerRadius = new CornerRadius(3) });
        var tb = new TextBlock { FontFamily = CoresLeitura.Fonte, FontWeight = FontWeights.Bold, FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Margin = new Thickness(0, 4, 0, 4) };
        tb.Inlines.Add(new Run { Text = b.Rotulo, Foreground = CoresLeitura.Azul });
        if (b.Texto.Length > 0) tb.Inlines.Add(new Run { Text = " — " + b.Texto.ToUpperInvariant(), Foreground = CoresLeitura.Marinho });
        Grid.SetColumn(tb, 1);
        g.Children.Add(tb);
        return g;
    }

    FrameworkElement Artigo(Anexo anexo, BlocoLei b)
    {
        // Linha de cima como o cabeçalho dos cartões do relatório: o selo "Art. N" em azul-marinho e, ao lado, o resumo; embaixo, o caput.
        var topo = new Grid { ColumnSpacing = 10 };
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topo.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        topo.Children.Add(new Border
        {
            Background = CoresLeitura.Marinho, CornerRadius = new CornerRadius(5), Padding = new Thickness(11, 3, 11, 4), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = b.Rotulo, FontFamily = CoresLeitura.Fonte, Foreground = CoresLeitura.Branco, FontWeight = FontWeights.Bold, FontSize = 15, CharacterSpacing = 20 },
        });
        var resumo = new TextBlock
        {
            FontFamily = CoresLeitura.Fonte, FontSize = 13.5, Foreground = CoresLeitura.Apoio, VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.WordEllipsis,
        };
        Grid.SetColumn(resumo, 1);
        topo.Children.Add(resumo);
        var lapis = new Button
        {
            Padding = new Thickness(6), Background = new SolidColorBrush(Colors.Transparent), BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center,
            Content = new FontIcon { Glyph = "", FontSize = 13, Foreground = CoresLeitura.Azul },
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
            Text = b.Texto, FontFamily = CoresLeitura.Fonte, FontSize = 15, LineHeight = 27, Foreground = CoresLeitura.Tinta, TextAlignment = TextAlignment.Justify,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Visibility = b.Texto.Length > 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        return new StackPanel
        {
            Margin = new Thickness(0, 16, 0, 0), Spacing = 8,
            Children = { new Border { BorderBrush = CoresLeitura.LinhaClara, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 0, 0, 6), Child = topo }, corpo },
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
        var salvar = new Button { Content = "Salvar" };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var estilo) && estilo is Style s) salvar.Style = s;
        var automatico = new Button { Content = "Voltar ao automático" };
        Dicas.Aplicar(salvar, "Salvar", "Guarda o seu resumo deste artigo com os dados da lei.");
        Dicas.Aplicar(automatico, "Voltar ao automático", "Apaga o seu resumo e volta a usar o tirado do texto do artigo.");
        var nota = new TextBlock
        {
            Text = "O resumo automático é tirado do próprio texto do artigo. O seu fica guardado com os dados da lei; o PDF não muda.",
            TextWrapping = TextWrapping.Wrap, Width = 360, FontSize = 12, Opacity = 0.8,
        };
        var flyout = new Flyout
        {
            Content = new StackPanel { Spacing = 10, Children = { caixa, nota, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { salvar, automatico } } } },
        };
        flyout.Opening += (_, _) =>
        {
            caixa.Text = resumosEditados.TryGetValue(b.Chave, out var atual) ? atual : b.Resumo;
            automatico.IsEnabled = resumosEditados.ContainsKey(b.Chave);
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
            Background = CoresLeitura.Branco, BorderBrush = CoresLeitura.Linha, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(11),
            Padding = new Thickness(14, 10, 14, 14), Margin = new Thickness(0, 10, 0, 6), Child = painel,
        };
    }

    static FrameworkElement Dispositivo(BlocoLei b)
    {
        var (recuo, cor) = b.Tipo switch
        {
            TipoBlocoLei.Paragrafo => (24, CoresLeitura.Azul),
            TipoBlocoLei.Inciso => (44, CoresLeitura.Marinho),
            TipoBlocoLei.Alinea => (66, CoresLeitura.Apoio),
            _ => (0, CoresLeitura.Marinho),
        };
        var tb = new TextBlock
        {
            FontFamily = CoresLeitura.Fonte, FontSize = 14.5, LineHeight = 26, Foreground = CoresLeitura.Tinta, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Justify, IsTextSelectionEnabled = true, Margin = new Thickness(recuo, 3, 0, 3),
        };
        if (b.Rotulo.Length > 0) tb.Inlines.Add(new Run { Text = b.Rotulo + " ", FontWeight = FontWeights.Bold, Foreground = cor });
        tb.Inlines.Add(new Run { Text = b.Texto });
        if (b.Tipo != TipoBlocoLei.Paragrafo) return tb;
        // Parágrafo: um fio claro à esquerda, como a caixa de fundamentação do relatório.
        tb.Margin = new Thickness(0);
        return new Border { BorderBrush = CoresLeitura.Linha, BorderThickness = new Thickness(3, 0, 0, 0), Margin = new Thickness(recuo - 12, 4, 0, 4), Padding = new Thickness(9, 0, 0, 0), Child = tb };
    }
}
