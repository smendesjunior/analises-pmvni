using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Pmvni.App;

/// <summary>
/// Brilho dos botões coloridos (pedido de 2026-11-17: "o mesmo glow que o WinStatusBar dá aos dele"). É o ChatGlowLayer do
/// WinStatusBar trazido para cá: uma DropShadow do compositor, sem deslocamento, na cor de destaque (o verde institucional), com o
/// formato do botão (cantos arredondados) como máscara. Lá a sombra fica numa camada ATRÁS dos controles; aqui ela é o visual filho do
/// próprio botão (que fica POR CIMA dele), então o brilho passa por uma máscara em anel que começa exatamente na borda do botão: só
/// aparece do lado de fora e nunca tinge o fundo nem o texto.
/// Em repouso, um halo discreto; ao passar o mouse, a intensidade do WinStatusBar (0,30 no claro, 0,42 no escuro, desfoque 12);
/// pressionado, um pouco menos; desabilitado, nada. Transições só com "Efeitos de animação" do Windows ligados; no Alto Contraste não
/// há brilho. A cor vem de <c>PmvniBrilho</c> (IconesModulos.xaml: claro, escuro e alto contraste), pelo tema do botão.
/// Uso: <c>local:Brilho.Ativo="True"</c> num setter de estilo (PmvniBotaoTonalStyle) ou <see cref="SetAtivo"/>; o AccentButtonStyle
/// do WinUI recebe o setter uma vez, na abertura do app (<see cref="LigarNoDestaque"/>).
/// </summary>
public static class Brilho
{
    public static readonly DependencyProperty AtivoProperty = DependencyProperty.RegisterAttached(
        "Ativo", typeof(bool), typeof(Brilho), new PropertyMetadata(false, AoMudarAtivo));

    public static bool GetAtivo(DependencyObject d) => (bool)d.GetValue(AtivoProperty);
    public static void SetAtivo(DependencyObject d, bool valor) => d.SetValue(AtivoProperty, valor);

    static readonly DependencyProperty EstadoProperty = DependencyProperty.RegisterAttached(
        "Estado", typeof(object), typeof(Brilho), new PropertyMetadata(null));

    /// <summary>Põe o brilho em todo botão com o AccentButtonStyle do WinUI (o estilo ainda não foi usado: aceita um setter a mais).</summary>
    public static void LigarNoDestaque(Application app)
    {
        try
        {
            if (app.Resources.TryGetValue("AccentButtonStyle", out var e) && e is Style s && !s.IsSealed)
                s.Setters.Add(new Setter(AtivoProperty, true));
        }
        catch (Exception ex) { RegistroErros.Gravar("Brilho no AccentButtonStyle", ex); }
    }

    // Margem em volta do botão onde o brilho pode aparecer (o desfoque máximo é 12: o halo some antes dela).
    const float Margem = 24f;

    static readonly UISettings Interface = new();
    static readonly AccessibilitySettings Acessibilidade = new();
    static bool Animar => Interface.AnimationsEnabled && !Acessibilidade.HighContrast;

    static void AoMudarAtivo(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control botao) return;
        if (botao.GetValue(EstadoProperty) is Estado antigo) { antigo.Desligar(); botao.ClearValue(EstadoProperty); }
        if (e.NewValue is true) botao.SetValue(EstadoProperty, new Estado(botao));
    }

    sealed class Estado
    {
        readonly Control botao;
        readonly Compositor c;
        readonly SpriteVisual brilho;            // o que aparece: o halo visto através do anel
        readonly SpriteVisual fonte;             // o formato do botão, que projeta a sombra (fica fora da árvore)
        readonly DropShadow sombra;
        readonly ContainerVisual raizFonte;
        readonly CompositionVisualSurface superficieFonte, superficieAnel, superficieForma;
        readonly ShapeVisual forma, anel;
        readonly CompositionRoundedRectangleGeometry geoForma, geoAnel;
        readonly CompositionSpriteShape tracoAnel;
        bool sobre, pressionado, ligado;
        Vector2 tamanho = new(float.NaN);

        public Estado(Control botao)
        {
            this.botao = botao;
            c = ElementCompositionPreview.GetElementVisual(botao).Compositor;

            // Máscara da sombra: o retângulo arredondado do botão (como no WinStatusBar).
            geoForma = c.CreateRoundedRectangleGeometry();
            var preenchido = c.CreateSpriteShape(geoForma);
            preenchido.FillBrush = c.CreateColorBrush(Microsoft.UI.Colors.White);
            forma = c.CreateShapeVisual();
            forma.Shapes.Add(preenchido);
            superficieForma = c.CreateVisualSurface();
            superficieForma.SourceVisual = forma;
            var mascaraSombra = c.CreateSurfaceBrush(superficieForma);
            mascaraSombra.Stretch = CompositionStretch.Fill;

            sombra = c.CreateDropShadow();
            sombra.Mask = mascaraSombra;
            sombra.Offset = Vector3.Zero;
            fonte = c.CreateSpriteVisual();
            fonte.Brush = c.CreateColorBrush(Color.FromArgb(0, 0, 0, 0)); // só a sombra aparece
            fonte.Shadow = sombra;
            fonte.Offset = new Vector3(Margem, Margem, 0);
            raizFonte = c.CreateContainerVisual();
            raizFonte.Children.InsertAtTop(fonte);
            superficieFonte = c.CreateVisualSurface();
            superficieFonte.SourceVisual = raizFonte;

            // Anel: traço de 2d centrado numa linha a d da borda, então cobre de 0 a 2d para fora; a borda de dentro coincide com o contorno
            // do botão, inclusive nos cantos (a curva paralela a raio r, à distância d, tem raio r + d).
            geoAnel = c.CreateRoundedRectangleGeometry();
            tracoAnel = c.CreateSpriteShape(geoAnel);
            tracoAnel.StrokeBrush = c.CreateColorBrush(Microsoft.UI.Colors.White);
            tracoAnel.StrokeThickness = Margem;
            anel = c.CreateShapeVisual();
            anel.Shapes.Add(tracoAnel);
            superficieAnel = c.CreateVisualSurface();
            superficieAnel.SourceVisual = anel;

            var mascara = c.CreateMaskBrush();
            var origem = c.CreateSurfaceBrush(superficieFonte); origem.Stretch = CompositionStretch.Fill;
            var corte = c.CreateSurfaceBrush(superficieAnel); corte.Stretch = CompositionStretch.Fill;
            mascara.Source = origem;
            mascara.Mask = corte;
            brilho = c.CreateSpriteVisual();
            brilho.Brush = mascara;
            brilho.Offset = new Vector3(-Margem, -Margem, 0);

            botao.Loaded += AoCarregar;
            botao.SizeChanged += AoMudarTamanho;
            botao.ActualThemeChanged += AoMudarTema;
            botao.IsEnabledChanged += AoMudarHabilitado;
            // Botões de ligar e desligar (filtros, pílulas da faixa): só brilham ligados, quando estão na cor de destaque.
            if (botao is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton tb) { tb.Checked += AoMarcar; tb.Unchecked += AoMarcar; }
            botao.PointerEntered += AoEntrar;
            botao.PointerExited += AoSair;
            botao.PointerCanceled += AoSair;
            botao.PointerCaptureLost += AoSair;
            // O Button marca o PointerPressed/Released como tratados: só chegam aqui com handledEventsToo.
            botao.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(AoPressionar), true);
            botao.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(AoSoltar), true);

            ElementCompositionPreview.SetElementChildVisual(botao, brilho);
            ligado = true;
            Medir();
            Aplicar(animar: false);
        }

        public void Desligar()
        {
            ligado = false;
            botao.Loaded -= AoCarregar;
            botao.SizeChanged -= AoMudarTamanho;
            botao.ActualThemeChanged -= AoMudarTema;
            botao.IsEnabledChanged -= AoMudarHabilitado;
            if (botao is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton tb) { tb.Checked -= AoMarcar; tb.Unchecked -= AoMarcar; }
            botao.PointerEntered -= AoEntrar;
            botao.PointerExited -= AoSair;
            botao.PointerCanceled -= AoSair;
            botao.PointerCaptureLost -= AoSair;
            botao.RemoveHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(AoPressionar));
            botao.RemoveHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(AoSoltar));
            ElementCompositionPreview.SetElementChildVisual(botao, null);
        }

        void AoCarregar(object s, RoutedEventArgs e) { sobre = pressionado = false; Medir(); Aplicar(false); }
        void AoMudarTamanho(object s, SizeChangedEventArgs e) => Medir();
        void AoMudarTema(FrameworkElement s, object e) => Aplicar(false);
        void AoMudarHabilitado(object s, DependencyPropertyChangedEventArgs e) { if (!botao.IsEnabled) sobre = pressionado = false; Aplicar(true); }
        void AoMarcar(object s, RoutedEventArgs e) => Aplicar(true);
        void AoEntrar(object s, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { sobre = true; Aplicar(true); }
        void AoSair(object s, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { if (!sobre && !pressionado) return; sobre = pressionado = false; Aplicar(true); }
        void AoPressionar(object s, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { if (pressionado) return; pressionado = true; Aplicar(true); }
        void AoSoltar(object s, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) { if (!pressionado) return; pressionado = false; Aplicar(true); }

        /// <summary>Tamanhos explícitos: o visual filho de um elemento não herda o tamanho dele.</summary>
        void Medir()
        {
            if (!ligado) return;
            var t = new Vector2((float)botao.ActualWidth, (float)botao.ActualHeight);
            if (t == tamanho) return;
            tamanho = t;
            float raio = Math.Max(0f, Math.Min((float)botao.CornerRadius.TopLeft, Math.Min(t.X, t.Y) / 2f));
            var total = t + new Vector2(2 * Margem);

            geoForma.Size = t;
            geoForma.CornerRadius = new Vector2(raio);
            forma.Size = t;
            superficieForma.SourceSize = t;
            fonte.Size = t;

            raizFonte.Size = total;
            superficieFonte.SourceSize = total;

            float d = Margem / 2f;
            geoAnel.Offset = new Vector2(Margem - d);
            geoAnel.Size = t + new Vector2(2 * d);
            geoAnel.CornerRadius = new Vector2(raio + d);
            anel.Size = total;
            superficieAnel.SourceSize = total;

            brilho.Size = total;
            brilho.IsVisible = t.X >= 1 && t.Y >= 1;
        }

        /// <summary>A cor do brilho no tema do botão (claro ou escuro do app, não o do Windows).</summary>
        Color Cor()
        {
            var tema = botao.ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
            foreach (var d in Application.Current.Resources.MergedDictionaries)
                if (d.ThemeDictionaries.TryGetValue(tema, out var t) && t is ResourceDictionary rd && rd.TryGetValue("PmvniBrilho", out var v) && v is SolidColorBrush b)
                    return b.Color;
            return tema == "Dark" ? Color.FromArgb(255, 0x5C, 0xB9, 0x83) : Color.FromArgb(255, 0x1E, 0x7A, 0x46);
        }

        void Aplicar(bool animar)
        {
            if (!ligado) return;
            bool escuro = botao.ActualTheme == ElementTheme.Dark;
            float opacidade, desfoque;
            if (Acessibilidade.HighContrast || !botao.IsEnabled || botao is Microsoft.UI.Xaml.Controls.Primitives.ToggleButton { IsChecked: not true }) { opacidade = 0f; desfoque = 8f; }
            else if (pressionado) { opacidade = escuro ? 0.30f : 0.22f; desfoque = 9f; }
            else if (sobre) { opacidade = escuro ? 0.42f : 0.30f; desfoque = 12f; }  // os valores do WinStatusBar
            else { opacidade = escuro ? 0.22f : 0.15f; desfoque = 8f; }               // repouso: um halo discreto
            sombra.Color = Cor();

            if (!animar || !Animar)
            {
                sombra.StopAnimation("Opacity"); sombra.StopAnimation("BlurRadius");
                sombra.Opacity = opacidade; sombra.BlurRadius = desfoque;
                return;
            }
            var duracao = TimeSpan.FromMilliseconds(sobre || pressionado ? 160 : 220);
            var a = c.CreateScalarKeyFrameAnimation(); a.InsertKeyFrame(1f, opacidade); a.Duration = duracao;
            var b = c.CreateScalarKeyFrameAnimation(); b.InsertKeyFrame(1f, desfoque); b.Duration = duracao;
            sombra.StartAnimation("Opacity", a);
            sombra.StartAnimation("BlurRadius", b);
        }
    }
}
