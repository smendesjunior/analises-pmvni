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
    readonly Dictionary<byte[], ImageSource> bitmaps = new(ReferenceEqualityComparer.Instance);
    (UIElement Folha, Point Ponto)? ultimoToque;
    bool redesenhoPendente;
    bool vigiando;

    /// <summary>Edição direta na prévia: a chave do trecho (<see cref="TrechoPrevia.Chave"/>) e o texto novo
    /// (ver <see cref="ConteudoDespacho.AplicarEdicao"/>).</summary>
    internal event Action<string, string>? PreviaEditada;

    /// <summary>"Ajustar com IA" na caixa do texto da exigência: recebe a chave do trecho e o texto atual e devolve o texto melhorado
    /// (a janela liga à IA da revisão; ver <see cref="IaAjusteTexto"/>). null = o botão não aparece.</summary>
    internal Func<string, string, CancellationToken, Task<string>>? AjustarTextoComIa { get; set; }

    /// <summary>O texto de uma exigência (não o fundamento nem o título do grupo): só ele ganha o "Ajustar com IA".</summary>
    static bool EhTextoDeExigencia(string chave) =>
        chave.StartsWith("exigencia:", StringComparison.Ordinal) && !chave.EndsWith(":fundamento", StringComparison.Ordinal);

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
        layout = null;
        bitmaps.Clear();
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
        // O fecho (da data à matrícula) não se separa entre folhas, como no DOCX ("manter com o próximo"): é um bloco à parte, posto
        // inteiro no que sobra da última folha do texto ou, se não couber, numa folha nova (FechoDaPrevia).
        var iData = previa.ToList().FindIndex(p => p.Estilo == EstiloPrevia.Data);
        var texto = iData > 0 ? previa.Take(iData).ToList() : previa;
        var fecho = iData > 0 ? previa.Skip(iData).ToList() : [];

        var corpo = Bloco(l, largura, texto);
        var folha = Folha(l, Moldura(corpo, largura, altura), 1);
        Folhas.Children.Add(folha);
        Folhas.UpdateLayout();
        // Cada folha recebe o que não coube na anterior (o RichTextBlock só sabe o que transborda depois de medido).
        FrameworkElement ultimo = corpo;
        while (Folhas.Children.Count < MaximoDeFolhas && Transborda(ultimo))
        {
            var resto = new RichTextBlockOverflow { Width = largura, VerticalAlignment = VerticalAlignment.Top };
            if (ultimo is RichTextBlock r) r.OverflowContentTarget = resto;
            else ((RichTextBlockOverflow)ultimo).OverflowContentTarget = resto;
            folha = Folha(l, Moldura(resto, largura, altura), Folhas.Children.Count + 1);
            Folhas.Children.Add(folha);
            Folhas.UpdateLayout();
            ultimo = resto;
        }
        if (fecho.Count == 0) return;

        var assinatura = Bloco(l, largura, fecho);
        assinatura.Measure(new Size(largura, double.PositiveInfinity));
        // A moldura tem a altura do corpo e o texto fica no alto dela: a altura do elemento é a que o texto ocupa nesta folha.
        if (FechoDaPrevia.NaUltimaFolha(ultimo.ActualHeight, assinatura.DesiredSize.Height, altura) is { } deslocamento)
        {
            Canvas.SetLeft(assinatura, l.MargemEsquerdaPt * K);
            Canvas.SetTop(assinatura, l.TopoDoCorpoPt * K + deslocamento);
            var tela = (Canvas)((Border)folha).Child;
            // Antes das imagens da frente (faixas, tarja), como o corpo.
            tela.Children.Insert(tela.Children.IndexOf((UIElement)ultimo.Parent) + 1, assinatura);
        }
        else if (Folhas.Children.Count < MaximoDeFolhas)
            Folhas.Children.Add(Folha(l, Moldura(assinatura, largura, altura), Folhas.Children.Count + 1));
    }

    /// <summary>Um bloco de texto da largura do corpo, com os parágrafos na formatação do modelo; a altura é a do conteúdo.</summary>
    RichTextBlock Bloco(LayoutDespacho l, double largura, IEnumerable<ParagrafoPrevia> paragrafos)
    {
        var b = new RichTextBlock
        {
            FontFamily = new FontFamily(l.Fonte), FontSize = l.TamanhoPt * K, Foreground = Preto, TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = false, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, Width = largura,
            TextLineBounds = TextLineBounds.Full, VerticalAlignment = VerticalAlignment.Top,
        };
        foreach (var p in paragrafos) b.Blocks.Add(Paragrafo(p, l));
        return b;
    }

    /// <summary>A área do corpo numa folha: o texto dentro dela ocupa só a altura das linhas que couberam (o resto transborda).</summary>
    static Grid Moldura(FrameworkElement texto, double largura, double altura)
    {
        var g = new Grid { Width = largura, Height = altura };
        g.Children.Add(texto);
        return g;
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
        // 1.0.0 (correção do endereço do site cortado): a última linha do rodapé do modelo ("CEP … CNPJ … - www.vendanova.es.gov.br", Verdana
        // 10) é mais larga que a área do rodapé; o Word a quebra em duas linhas e sobe o rodapé (que fica preso à distância de baixo), mas a
        // prévia desenhava cada parágrafo numa linha só, de largura fixa, e cortava o site. Agora cada parágrafo quebra como no Word e o rodapé
        // é empilhado de baixo para cima com as alturas medidas; a linha vermelha sobe junto. Nenhum texto é escondido nem reduzido.
        var blocosRodape = new List<(TextBlock Texto, double Altura)>();
        foreach (var r in l.Rodape)
        {
            var largura = Math.Max(1, (l.LarguraUtilPt - r.RecuoEsquerdoPt) * K);
            var t = new TextBlock
            {
                FontFamily = new FontFamily(l.Fonte), Foreground = Preto, TextAlignment = Alinhamento(r.Alinhamento), TextWrapping = TextWrapping.Wrap,
                Width = largura, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
                LineHeight = Math.Max(1, r.AlturaPt * K), IsHitTestVisible = false,
            };
            foreach (var trecho in r.Trechos)
                t.Inlines.Add(new Run { Text = trecho.Texto, FontSize = trecho.TamanhoPt * K, FontWeight = trecho.Negrito ? FontWeights.Bold : FontWeights.Normal });
            t.Measure(new Size(largura, double.PositiveInfinity));
            var linhasDoParagrafo = r.Trechos.Count == 0 ? 1 : Math.Max(1, (int)Math.Round(t.DesiredSize.Height / Math.Max(1, r.AlturaPt * K)));
            blocosRodape.Add((t, linhasDoParagrafo * r.AlturaPt));
            Canvas.SetLeft(t, (l.MargemEsquerdaPt + r.RecuoEsquerdoPt) * K);
        }
        var (topos, subida) = RodapeDespacho.Posicionar(l.Rodape, blocosRodape.Select(b => b.Altura).ToList());
        for (var i = 0; i < blocosRodape.Count; i++)
        {
            Canvas.SetTop(blocosRodape[i].Texto, topos[i] * K);
            c.Children.Add(blocosRodape[i].Texto);
        }
        foreach (var linha in l.Linhas)
            c.Children.Add(new Line
            {
                X1 = linha.X1Pt * K, Y1 = (linha.Y1Pt - subida) * K, X2 = linha.X2Pt * K, Y2 = (linha.Y2Pt - subida) * K, StrokeThickness = linha.EspessuraPt * K,
                Stroke = new SolidColorBrush(Cor(linha.Cor)), IsHitTestVisible = false,
            });
        Canvas.SetLeft(corpo, l.MargemEsquerdaPt * K);
        // O corpo começa abaixo da faixa do cabeçalho em toda folha (no modelo do kit a margem de cima é menor que a faixa).
        Canvas.SetTop(corpo, l.TopoDoCorpoPt * K);
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

    ImageSource Bitmap(byte[] bytes)
    {
        if (bitmaps.TryGetValue(bytes, out var b)) return b;
        b = bytes.AsSpan().StartsWith("<svg"u8) ? new SvgImageSource() : new BitmapImage();
        bitmaps[bytes] = b;
        _ = CarregarAsync(b, bytes);
        return b;
    }

    static async Task CarregarAsync(ImageSource b, byte[] bytes)
    {
        try
        {
            using var s = new InMemoryRandomAccessStream();
            await s.WriteAsync(bytes.AsBuffer());
            s.Seek(0);
            if (b is SvgImageSource svg) await svg.SetSourceAsync(s);
            else await ((BitmapImage)b).SetSourceAsync(s);
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
        if (p.Estilo == EstiloPrevia.Data) f = f with { AntesPt = PreenchimentoDocx.EspacoAntesDaDataPt };
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
        "cabecalho:destinatario" => "Destinatário",
        "cabecalho:data" => "Data (por extenso)",
        ConteudoDespacho.ChaveIntervencao => "Tipo de obra",
        ConteudoDespacho.ChaveArea => "Área total a construir (m², ex.: 210,52)",
        _ when chave.StartsWith("corpo:", StringComparison.Ordinal) => "Texto do despacho",
        _ when chave.StartsWith("grupo:", StringComparison.Ordinal) => "Título do grupo",
        _ when chave.EndsWith(":fundamento", StringComparison.Ordinal) => "Fundamento",
        _ => "Texto da exigência",
    };

    /// <summary>A caixa de edição do trecho, aberta no ponto do clique: Enter grava, Esc desiste; o tipo de obra abre as três caixas.</summary>
    void Editar(TrechoPrevia t)
    {
        if (t.Chave is not { } chave) return;
        var valor = t.Valor ?? "";
        // O cartão do Flyout tem largura máxima própria (456) e rolagem interna: conteúdo e cartão recebem medidas que combinam e cabem
        // na janela (antes, um conteúdo de 640 saía cortado à direita, com a rolagem horizontal aparecendo embaixo).
        var janela = XamlRoot?.Size ?? default;
        var m = CaixaEdicaoPrevia.Medir(janela.Width, janela.Height);
        var painel = new StackPanel { Spacing = 8, Width = m.LarguraConteudo };
        var cartao = new Style(typeof(FlyoutPresenter));
        if (Application.Current.Resources.TryGetValue("DefaultFlyoutPresenterStyle", out var padrao) && padrao is Style estiloPadrao) cartao.BasedOn = estiloPadrao;
        cartao.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, m.LarguraCartao));
        cartao.Setters.Add(new Setter(ScrollViewer.HorizontalScrollModeProperty, ScrollMode.Disabled));
        cartao.Setters.Add(new Setter(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled));
        // Dentro da janela: o cartão não vira janela própria, e o WinUI o encosta na borda quando o clique é perto dela.
        var fly = new Flyout { Content = painel, Placement = FlyoutPlacementMode.Bottom, FlyoutPresenterStyle = cartao, ShouldConstrainToRootBounds = true };
        if (chave == ConteudoDespacho.ChaveIntervencao)
        {
            painel.Children.Add(new TextBlock { Text = NomeDoCampo(chave), Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            var marcadas = valor.Replace(" e ", ",", StringComparison.Ordinal).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var caixas = ConteudoDespacho.NomesDasIntervencoes
                .Select(n => new CheckBox { Content = n, IsChecked = marcadas.Contains(n, StringComparer.OrdinalIgnoreCase) }).ToList();
            foreach (var c in caixas) painel.Children.Add(c);
            // Opção manual (2026-11-17, pedido do usuário): outro tipo de obra, digitado.
            var outro = new TextBox
            {
                Header = "Outro (digitar)", PlaceholderText = "ex.: regularização de obra",
                Text = string.Join(", ", marcadas.Where(n => !ConteudoDespacho.NomesDasIntervencoes.Contains(n, StringComparer.OrdinalIgnoreCase))),
            };
            AutomationProperties.SetName(outro, "Outro tipo de obra");
            outro.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; fly.Hide(); } };
            painel.Children.Add(outro);
            fly.Closed += (_, _) =>
            {
                var novo = string.Join(", ", caixas.Where(c => c.IsChecked == true).Select(c => (string)c.Content)
                    .Concat(string.IsNullOrWhiteSpace(outro.Text) ? [] : [outro.Text.Trim().Replace(" e ", ", ", StringComparison.Ordinal)]));
                if (!string.Equals(novo, valor, StringComparison.Ordinal)) PreviaEditada?.Invoke(chave, novo);
            };
        }
        else
        {
            // Altura conforme o texto: uma linha para um trecho curto, crescendo com as quebras até o máximo (daí em diante, rolagem vertical).
            var caixa = new TextBox
            {
                Header = NomeDoCampo(chave), Text = valor, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false,
                MinHeight = m.AlturaMinima, MaxHeight = m.AlturaMaxima, HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            ScrollViewer.SetVerticalScrollBarVisibility(caixa, ScrollBarVisibility.Auto);
            ScrollViewer.SetHorizontalScrollBarVisibility(caixa, ScrollBarVisibility.Disabled);
            AutomationProperties.SetName(caixa, NomeDoCampo(chave));
            painel.Children.Add(caixa);
            painel.Children.Add(new TextBlock { Text = "Enter grava; Esc desiste.", Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"] });
            var desistiu = false;
            caixa.KeyDown += (_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Enter) { e.Handled = true; fly.Hide(); }
            };
            // Esc desiste de qualquer ponto do cartão (também com o foco no botão da IA), não só da caixa.
            painel.AddHandler(KeyDownEvent, new KeyEventHandler((_, e) =>
            {
                if (e.Key == Windows.System.VirtualKey.Escape) desistiu = true;
            }), true);
            Action? cancelarIa = null;
            if (AjustarTextoComIa is { } ajustar && EhTextoDeExigencia(chave)) cancelarIa = AdicionarAjusteComIa(painel, caixa, chave, ajustar);
            fly.Opened += (_, _) => { caixa.Focus(FocusState.Programmatic); caixa.SelectAll(); };
            fly.Closed += (_, _) =>
            {
                cancelarIa?.Invoke(); // fechou com a IA ainda trabalhando: encerra o programa; a resposta não entra mais

                if (!desistiu && caixa.Text != valor) PreviaEditada?.Invoke(chave, caixa.Text);
            };
        }
        if (ultimoToque is { } toque) fly.ShowAt(toque.Folha, new FlyoutShowOptions { Position = toque.Ponto });
        else fly.ShowAt(Folhas);
        ultimoToque = null;
    }

    /// <summary>O "Ajustar com IA" logo abaixo da caixa: manda o texto à IA e põe a resposta na caixa, selecionada (Enter grava;
    /// Ctrl+Z ou Esc desfaz). Enquanto espera: anel, "Ajustando…" e o botão vira "Cancelar". O erro aparece ali mesmo, em português.
    /// Devolve o que cancela a espera (ao fechar o cartão).</summary>
    Action AdicionarAjusteComIa(StackPanel painel, TextBox caixa, string chave, Func<string, string, CancellationToken, Task<string>> ajustar)
    {
        const string Rotulo = "Ajustar com IA", Dica = "A IA deixa o texto mais claro e formal, com o mesmo sentido e sem fatos novos. " +
            "O resultado entra na caixa, selecionado: Enter grava; Ctrl+Z ou Esc desfaz.";
        var rotulo = new TextBlock { Text = Rotulo };
        var botao = new Button
        {
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new FontIcon { Glyph = "", FontSize = 14 }, rotulo } },
        };
        AutomationProperties.SetName(botao, Rotulo);
        Dicas.Aplicar(botao, Rotulo, Dica);
        var anel = new ProgressRing { IsActive = false, Width = 16, Height = 16, Visibility = Visibility.Collapsed };
        var estado = new TextBlock
        {
            Text = "Ajustando…", VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
            Style = (Style)Application.Current.Resources["PmvniSecondaryTextStyle"],
        };
        var linha = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { botao, anel, estado } };
        var erro = new TextBlock { Visibility = Visibility.Collapsed, Style = (Style)Application.Current.Resources["PmvniTextoErroStyle"] };
        var depois = painel.Children.IndexOf(caixa) + 1;
        painel.Children.Insert(depois, linha);
        painel.Children.Insert(depois + 1, erro);

        CancellationTokenSource? espera = null;
        void Esperando(bool sim)
        {
            anel.IsActive = sim;
            anel.Visibility = estado.Visibility = sim ? Visibility.Visible : Visibility.Collapsed;
            rotulo.Text = sim ? "Cancelar" : Rotulo;
            AutomationProperties.SetName(botao, sim ? "Cancelar o ajuste com IA" : Rotulo);
            caixa.IsReadOnly = sim; // a resposta substitui o texto: nada de editar no meio da espera
        }
        botao.Click += async (_, _) =>
        {
            if (espera is { } atual) { atual.Cancel(); return; }
            var texto = caixa.Text;
            if (texto.Trim().Length == 0)
            {
                erro.Text = "Escreva o texto da exigência antes de pedir o ajuste.";
                erro.Visibility = Visibility.Visible;
                return;
            }
            erro.Visibility = Visibility.Collapsed;
            using var cts = new CancellationTokenSource();
            espera = cts;
            Esperando(true);
            try
            {
                var novo = IaAjusteTexto.Ler(await ajustar(chave, texto, cts.Token));
                cts.Token.ThrowIfCancellationRequested();
                if (novo is null)
                {
                    erro.Text = "A IA não devolveu texto; nada mudou.";
                    erro.Visibility = Visibility.Visible;
                    return;
                }
                Esperando(false);
                // Pela seleção (e não por Text): a troca entra no Desfazer da caixa, e o Ctrl+Z volta o texto anterior.
                caixa.Focus(FocusState.Programmatic);
                caixa.SelectAll();
                caixa.SelectedText = novo;
                caixa.Select(0, caixa.Text.Length);
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                // As falhas do programa da IA já vêm em português (IaDespacho); as demais passam pela tradução (e vão para o registro).
                var motivo = MensagensDeErro.ParaUsuario(e);
                erro.Text = (e is InvalidOperationException ? motivo : "A chamada à IA falhou: " + motivo) + (motivo.EndsWith('.') ? "" : ".");
                erro.Visibility = Visibility.Visible;
            }
            finally
            {
                espera = null;
                Esperando(false);
            }
        };
        return () =>
        {
            try { espera?.Cancel(); } catch (ObjectDisposedException) { }
        };
    }
}
