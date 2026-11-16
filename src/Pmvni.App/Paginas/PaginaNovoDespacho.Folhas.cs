using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Pmvni.Revisao;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace Pmvni.App;

/// <summary>As folhas da prévia (correções 2026-11-15): o despacho no modelo oficial do kit, uma folha A4 por página, desenhado direto em
/// XAML a partir do <see cref="LayoutDespacho"/> lido do DOCX do kit (faixas e brasão do cabeçalho, tarja lateral, linha vermelha e
/// endereço do rodapé, margens, Verdana e tamanhos de cada parágrafo). O corpo é um <see cref="RichTextBlock"/> só, que transborda de uma
/// folha para a seguinte (<see cref="RichTextBlockOverflow"/>): as quebras de página caem entre linhas, como no Word. Nada passa pelo
/// LibreOffice: a cada mudança, a folha é redesenhada na hora; a paginação final continua conferida nas páginas do PDF (passo 3).
/// Os trechos variáveis (protocolo, requerente, assunto, data, títulos de grupo, exigências e fundamentos, tipo de obra e área) são
/// links sem sublinhado: clicar abre a caixa de edição ali mesmo; o texto volta ao rascunho por <see cref="PreviaEditada"/>.</summary>
public sealed partial class PaginaNovoDespacho
{
    /// <summary>DIP por ponto: a folha na tela em tamanho real (96 DIP por polegada).</summary>
    const double K = 96.0 / 72.0;
    const int MaximoDeFolhas = 40;
    static readonly SolidColorBrush Preto = new(Colors.Black);
    static readonly SolidColorBrush Branco = new(Colors.White);
    static readonly SolidColorBrush Vermelho = new(Windows.UI.Color.FromArgb(255, 0xC4, 0x2B, 0x1C));
    static readonly SolidColorBrush Borda = new(Windows.UI.Color.FromArgb(0x33, 0, 0, 0));

    IReadOnlyList<ParagrafoPrevia> previa = [];
    LayoutDespacho? layout;
    readonly Dictionary<byte[], BitmapImage> bitmaps = new(ReferenceEqualityComparer.Instance);
    (UIElement Folha, Point Ponto)? ultimoToque;
    bool redesenhoPendente;
    bool vigiando;

    /// <summary>Edição direta na prévia: a chave do trecho (<see cref="TrechoPrevia.Chave"/>) e o texto novo
    /// (ver <see cref="ConteudoDespacho.AplicarEdicao"/>).</summary>
    internal event Action<string, string>? PreviaEditada;

    /// <summary>O texto da prévia como está na tela, um parágrafo por linha (o roteiro confere).</summary>
    internal string TextoDaPrevia => string.Join("\n", previa.Select(p => p.Texto));

    internal IReadOnlyList<ParagrafoPrevia> ParagrafosDaPrevia => previa;

    /// <summary>Quantas folhas a prévia tem agora.</summary>
    internal int FolhasDaPrevia => Folhas.Children.Count;

    /// <summary>Os parágrafos na ordem do DOCX, no modelo <paramref name="modelo"/>: o que falta aparece como "[preencher: …]" em vermelho (só
    /// aqui, nunca no DOCX).</summary>
    internal void MostrarPrevia(IReadOnlyList<ParagrafoPrevia> paragrafos, LayoutDespacho? modelo)
    {
        if (!vigiando)
        {
            // Desenhada com a página escondida, a folha não tem medida: ao aparecer, é redesenhada (para paginar de verdade).
            vigiando = true;
            RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => AgendarRedesenho());
            PainelEdicao.RegisterPropertyChangedCallback(VisibilityProperty, (_, _) => AgendarRedesenho());
        }
        if (!ReferenceEquals(modelo, layout)) bitmaps.Clear();
        previa = paragrafos;
        layout = modelo;
        AgendarRedesenho();
    }

    void LimparFolhas()
    {
        previa = [];
        Folhas.Children.Clear();
    }

    /// <summary>Várias mudanças seguidas (as caixas do passo 2 se atualizam uma a uma) dão um redesenho só, logo depois.</summary>
    void AgendarRedesenho()
    {
        if (redesenhoPendente) return;
        redesenhoPendente = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () => { redesenhoPendente = false; Desenhar(); }))
            redesenhoPendente = false;
    }

    void Desenhar()
    {
        // Escondida, a folha não tem medida (não daria para paginar): fica para quando a página aparecer.
        if (Visibility != Visibility.Visible || PainelEdicao.Visibility != Visibility.Visible) return;
        Folhas.Children.Clear();
        if (layout is not { } l || previa.Count == 0) return;
        var largura = l.LarguraUtilPt * K;
        var altura = l.AlturaUtilPt * K;
        var corpo = new RichTextBlock
        {
            FontFamily = new FontFamily(l.Fonte), FontSize = l.TamanhoPt * K, Foreground = Preto, TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = false, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Width = largura, Height = altura,
            TextLineBounds = TextLineBounds.Full,
        };
        foreach (var p in previa) corpo.Blocks.Add(Paragrafo(p, l));
        Folhas.Children.Add(Folha(l, corpo, 1));
        Folhas.UpdateLayout();
        // Cada folha recebe o que não coube na anterior (o RichTextBlock só sabe o que transborda depois de medido).
        FrameworkElement ultimo = corpo;
        while (Folhas.Children.Count < MaximoDeFolhas && Transborda(ultimo))
        {
            var resto = new RichTextBlockOverflow { Width = largura, Height = altura };
            if (ultimo is RichTextBlock r) r.OverflowContentTarget = resto;
            else ((RichTextBlockOverflow)ultimo).OverflowContentTarget = resto;
            Folhas.Children.Add(Folha(l, resto, Folhas.Children.Count + 1));
            Folhas.UpdateLayout();
            ultimo = resto;
        }
    }

    static bool Transborda(FrameworkElement e) => e switch
    {
        RichTextBlock r => r.HasOverflowContent,
        RichTextBlockOverflow o => o.HasOverflowContent,
        _ => false,
    };

    // ---------- a folha ----------

    /// <summary>Uma folha A4: imagens de trás, linha e endereço do rodapé, o corpo nas margens e as imagens da frente (faixas, brasão, tarja).</summary>
    FrameworkElement Folha(LayoutDespacho l, FrameworkElement corpo, int numero)
    {
        var c = new Canvas { Width = l.LarguraPt * K, Height = l.AlturaPt * K, Background = Branco, RequestedTheme = ElementTheme.Light };
        foreach (var i in l.Imagens.Where(i => i.AtrasDoTexto)) c.Children.Add(Imagem(i));
        foreach (var linha in l.Linhas)
            c.Children.Add(new Line
            {
                X1 = linha.X1Pt * K, Y1 = linha.Y1Pt * K, X2 = linha.X2Pt * K, Y2 = linha.Y2Pt * K, StrokeThickness = linha.EspessuraPt * K,
                Stroke = new SolidColorBrush(Cor(linha.Cor)), IsHitTestVisible = false,
            });
        foreach (var r in l.Rodape)
        {
            var t = new TextBlock
            {
                FontFamily = new FontFamily(l.Fonte), Foreground = Preto, TextAlignment = Alinhamento(r.Alinhamento), TextWrapping = TextWrapping.NoWrap,
                Width = (l.LarguraUtilPt - r.RecuoEsquerdoPt) * K, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = Math.Max(1, r.AlturaPt * K), IsHitTestVisible = false,
            };
            foreach (var trecho in r.Trechos)
                t.Inlines.Add(new Run { Text = trecho.Texto, FontSize = trecho.TamanhoPt * K, FontWeight = trecho.Negrito ? FontWeights.Bold : FontWeights.Normal });
            Canvas.SetLeft(t, (l.MargemEsquerdaPt + r.RecuoEsquerdoPt) * K);
            Canvas.SetTop(t, r.TopoPt * K);
            c.Children.Add(t);
        }
        Canvas.SetLeft(corpo, l.MargemEsquerdaPt * K);
        Canvas.SetTop(corpo, l.MargemTopoPt * K);
        c.Children.Add(corpo);
        foreach (var i in l.Imagens.Where(i => !i.AtrasDoTexto)) c.Children.Add(Imagem(i));
        // O ponto do clique: a caixa de edição abre ali (o link do trecho não é um elemento com posição própria).
        c.AddHandler(PointerPressedEvent, new PointerEventHandler((_, e) => ultimoToque = (c, e.GetCurrentPoint(c).Position)), true);
        var folha = new Border { Child = c, BorderBrush = Borda, BorderThickness = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetName(folha, "Página " + numero.ToString(CultureInfo.InvariantCulture) + " da prévia");
        return folha;
    }

    /// <summary>Uma imagem do cabeçalho com o recorte do modelo (a tarja é um pedaço da imagem da página inteira).</summary>
    FrameworkElement Imagem(ImagemPagina i)
    {
        var w = Math.Max(1, i.LarguraPt * K);
        var h = Math.Max(1, i.AlturaPt * K);
        var fw = Math.Max(0.01, 1 - i.CorteEsquerdo - i.CorteDireito);
        var fh = Math.Max(0.01, 1 - i.CorteTopo - i.CorteBaixo);
        var cheia = new Image { Source = Bitmap(i.Bytes), Width = w / fw, Height = h / fh, Stretch = Stretch.Fill };
        Canvas.SetLeft(cheia, -i.CorteEsquerdo * w / fw);
        Canvas.SetTop(cheia, -i.CorteTopo * h / fh);
        var moldura = new Canvas { Width = w, Height = h, Clip = new RectangleGeometry { Rect = new Rect(0, 0, w, h) }, IsHitTestVisible = false };
        moldura.Children.Add(cheia);
        Canvas.SetLeft(moldura, i.XPt * K);
        Canvas.SetTop(moldura, i.YPt * K);
        return moldura;
    }

    BitmapImage Bitmap(byte[] bytes)
    {
        if (bitmaps.TryGetValue(bytes, out var b)) return b;
        b = new BitmapImage();
        bitmaps[bytes] = b;
        _ = CarregarAsync(b, bytes);
        return b;
    }

    static async Task CarregarAsync(BitmapImage b, byte[] bytes)
    {
        try
        {
            using var s = new InMemoryRandomAccessStream();
            await s.WriteAsync(bytes.AsBuffer());
            s.Seek(0);
            await b.SetSourceAsync(s);
        }
        catch (Exception e) when (e is COMException or ArgumentException or IOException) { /* sem a imagem, a folha segue sem ela */ }
    }

    static Windows.UI.Color Cor(string hex) =>
        hex.Length == 7 && int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            ? Windows.UI.Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v)
            : Colors.Black;

    static TextAlignment Alinhamento(string jc) => jc switch
    {
        "both" or "distribute" => TextAlignment.Justify,
        "center" => TextAlignment.Center,
        "right" or "end" => TextAlignment.Right,
        _ => TextAlignment.Left,
    };

    // ---------- o corpo ----------

    /// <summary>Um parágrafo com a formatação do parágrafo do modelo de onde ele vem (espaço antes e depois, entrelinha, alinhamento,
    /// tamanho, negrito e sublinhado); os trechos editáveis viram links.</summary>
    Paragraph Paragrafo(ParagrafoPrevia p, LayoutDespacho l)
    {
        var f = l.Formato(p.Modelo);
        var par = new Paragraph
        {
            FontSize = f.TamanhoPt * K, TextAlignment = Alinhamento(f.Alinhamento),
            Margin = new Thickness(f.RecuoEsquerdoPt * K, f.AntesPt * K, 0, f.DepoisPt * K),
            LineHeight = f.TamanhoPt * LayoutDespacho.LinhaSimples * f.Entrelinha * K, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };
        var trechos = p.TrechosOuTexto;
        if (p.Estilo == EstiloPrevia.Vazio || trechos.All(t => t.Texto.Length == 0))
        {
            par.Inlines.Add(new Run { Text = " " });
            return par;
        }
        foreach (var t in trechos.Where(t => t.Texto.Length > 0))
        {
            var run = new Run
            {
                Text = t.Texto, FontWeight = f.Negrito || t.Negrito || t.Faltando is not null ? FontWeights.Bold : FontWeights.Normal,
                Foreground = t.Faltando is null ? Preto : Vermelho,
            };
            if (f.Sublinhado) run.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
            if (t.Chave is null) { par.Inlines.Add(run); continue; }
            var link = new Hyperlink { UnderlineStyle = UnderlineStyle.None, Foreground = run.Foreground };
            link.Inlines.Add(run);
            AutomationProperties.SetName(link, "Editar " + NomeDoCampo(t.Chave).ToLower(CultureInfo.GetCultureInfo("pt-BR")));
            Dicas.Aplicar(link, "Editar " + NomeDoCampo(t.Chave).ToLower(CultureInfo.GetCultureInfo("pt-BR")),
                "Clique para mudar este trecho do despacho.");
            var trecho = t;
            link.Click += (_, _) => Editar(trecho);
            par.Inlines.Add(link);
        }
        return par;
    }

    static string NomeDoCampo(string chave) => chave switch
    {
        "cabecalho:protocolo" => "Protocolo nº",
        "cabecalho:requerente" => "Requerente",
        "cabecalho:assunto" => "Assunto",
        "cabecalho:data" => "Data (por extenso)",
        ConteudoDespacho.ChaveIntervencao => "Tipo de obra",
        ConteudoDespacho.ChaveArea => "Área total a construir (m², ex.: 210,52)",
        _ when chave.StartsWith("grupo:", StringComparison.Ordinal) => "Título do grupo",
        _ when chave.EndsWith(":fundamento", StringComparison.Ordinal) => "Fundamento",
        _ => "Texto da exigência",
    };

    /// <summary>A caixa de edição do trecho, aberta no ponto do clique: Enter grava, Esc desiste; o tipo de obra abre as três caixas.</summary>
    void Editar(TrechoPrevia t)
    {
        if (t.Chave is not { } chave) return;
        var valor = t.Valor ?? "";
        var painel = new StackPanel { Spacing = 8, Width = 640 };
        var fly = new Flyout { Content = painel, Placement = FlyoutPlacementMode.Bottom };
        if (chave == ConteudoDespacho.ChaveIntervencao)
        {
            painel.Children.Add(new TextBlock { Text = NomeDoCampo(chave), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            var marcadas = valor.Replace(" e ", ",", StringComparison.Ordinal).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var caixas = ConteudoDespacho.NomesDasIntervencoes
                .Select(n => new CheckBox { Content = n, IsChecked = marcadas.Contains(n, StringComparer.OrdinalIgnoreCase) }).ToList();
            foreach (var c in caixas) painel.Children.Add(c);
            fly.Closed += (_, _) =>
            {
                var novo = string.Join(", ", caixas.Where(c => c.IsChecked == true).Select(c => (string)c.Content));
                if (!string.Equals(novo, valor, StringComparison.Ordinal)) PreviaEditada?.Invoke(chave, novo);
            };
        }
        else
        {
            var caixa = new TextBox { Header = NomeDoCampo(chave), Text = valor, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, MinHeight = 160, MaxHeight = 400 };
            ScrollViewer.SetVerticalScrollBarVisibility(caixa, ScrollBarVisibility.Auto);
            AutomationProperties.SetName(caixa, NomeDoCampo(chave));
            painel.Children.Add(caixa);
            painel.Children.Add(new TextBlock { Text = "Enter grava; Esc desiste.", Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] });
            var desistiu = false;
            caixa.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; fly.Hide(); }
                else if (e.Key == Windows.System.VirtualKey.Escape) desistiu = true;
            };
            fly.Opened += (_, _) => { caixa.Focus(FocusState.Programmatic); caixa.SelectAll(); };
            fly.Closed += (_, _) => { if (!desistiu && caixa.Text != valor) PreviaEditada?.Invoke(chave, caixa.Text); };
        }
        if (ultimoToque is { } toque) fly.ShowAt(toque.Folha, new FlyoutShowOptions { Position = toque.Ponto });
        else fly.ShowAt(Folhas);
        ultimoToque = null;
    }
}
