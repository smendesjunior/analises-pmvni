using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using Microsoft.Graphics.DirectX;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Pmvni.App;

/// <summary>"Trabalhando" (2026-11-17, pedido do usuário): o mesmo brilho arco-íris do WinStatusBar (ContourGlow, o Rainbow Button da
/// Magic UI no compositor) no contorno de onde uma IA ou ferramenta está trabalhando, até o resultado final.
/// Automático: todo ProgressRing ativo e todo ProgressBar indeterminado (estilos implícitos em Estilos.xaml) acendem o brilho no quadro em
/// volta deles (<see cref="Alvo"/>); outros lugares chamam <see cref="Definir"/>. Respeita "Efeitos de animação" do Windows.</summary>
public static class Trabalhando
{
    static readonly ConditionalWeakTable<FrameworkElement, BrilhoArcoIris> brilhos = new();

    /// <summary>O brilho do elemento, se já existe (autoteste).</summary>
    internal static BrilhoArcoIris? BrilhoDe(FrameworkElement e) => brilhos.TryGetValue(e, out var b) ? b : null;

    /// <summary>Liga ou desliga o brilho no contorno do elemento.</summary>
    public static void Definir(FrameworkElement elemento, bool ativo, float canto = 8f)
    {
        if (!ativo && !brilhos.TryGetValue(elemento, out _)) return;
        try { brilhos.GetValue(elemento, e => new BrilhoArcoIris(e, canto)).Ativo = ativo; }
        catch (Exception e) { RegistroErros.Gravar("brilho", e); }
    }

    /// <summary>O quadro "trabalhando" padrão (pílula com o anel circular e o texto centralizados), para montar em código.
    /// 1.0.0: o anel só fica ativo enquanto o quadro está à vista (antes ficava sempre ativo, e o brilho do quadro escondido continuava).</summary>
    public static Border Quadro(string texto)
    {
        var anel = new ProgressRing { IsActive = false, Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        var q = new Border
        {
            Visibility = Visibility.Collapsed, CornerRadius = new CornerRadius(24), Padding = new Thickness(24, 10, 24, 10),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center,
                Children = { anel, new TextBlock { Text = texto, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center } },
            },
        };
        AnelSegueQuadro(q, anel);
        return q;
    }

    /// <summary>O anel de um quadro "trabalhando" gira só enquanto o quadro está visível (vale também para quadros do XAML).</summary>
    public static void AnelSegueQuadro(FrameworkElement quadro, ProgressRing anel)
    {
        anel.IsActive = quadro.Visibility == Visibility.Visible;
        quadro.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => anel.IsActive = quadro.Visibility == Visibility.Visible);
    }

    /// <summary>Visível de fato: o elemento e todos os ancestrais visíveis (um quadro dentro de uma página recolhida não está à vista).</summary>
    internal static bool VisivelDeFato(DependencyObject? e)
    {
        for (var p = e; p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is UIElement { Visibility: not Visibility.Visible }) return false;
        return true;
    }

    // ---------- automático nos indicadores de progresso ----------

    public static readonly DependencyProperty AutomaticoProperty = DependencyProperty.RegisterAttached(
        "Automatico", typeof(bool), typeof(Trabalhando), new PropertyMetadata(false, (d, e) => { if (d is Control c && e.NewValue is true) Observar(c); }));

    public static bool GetAutomatico(DependencyObject o) => (bool)o.GetValue(AutomaticoProperty);
    public static void SetAutomatico(DependencyObject o, bool v) => o.SetValue(AutomaticoProperty, v);

    static readonly ConditionalWeakTable<Control, FrameworkElement> alvos = new();

    static void Observar(Control c)
    {
        void Sincronizar()
        {
            // A visibilidade dos ancestrais é conferida pelo próprio brilho (BrilhoArcoIris vigia a cadeia inteira).
            var trabalhando = c.IsLoaded && c.Visibility == Visibility.Visible && c switch
            {
                ProgressRing r => r.IsActive,
                ProgressBar b => b.IsIndeterminate,
                _ => false,
            };
            if (trabalhando)
            {
                if (!alvos.TryGetValue(c, out var a) && Alvo(c) is { } novo) { alvos.AddOrUpdate(c, novo); a = novo; }
                if (a is not null) Definir(a, true, CantoDe(a));
            }
            else if (alvos.TryGetValue(c, out var a)) Definir(a, false);
        }
        c.Loaded += (_, _) => Sincronizar();
        c.Unloaded += (_, _) => Sincronizar();
        c.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Sincronizar());
        if (c is ProgressRing) c.RegisterPropertyChangedCallback(ProgressRing.IsActiveProperty, (_, _) => Sincronizar());
        if (c is ProgressBar) c.RegisterPropertyChangedCallback(ProgressBar.IsIndeterminateProperty, (_, _) => Sincronizar());
    }

    static float CantoDe(FrameworkElement e) => e switch
    {
        Border b => (float)Math.Max(4, b.CornerRadius.TopLeft),
        Control c => (float)Math.Max(4, c.CornerRadius.TopLeft),
        Grid g => (float)Math.Max(4, g.CornerRadius.TopLeft),
        StackPanel s => (float)Math.Max(4, s.CornerRadius.TopLeft),
        _ => 8f,
    };

    /// <summary>O quadro do trabalho: o cartão (Border/painel com cantos arredondados, InfoBar) mais próximo em volta do indicador, até 5
    /// níveis acima; senão, o painel em que ele está. Nunca a página ou a janela inteira (mais de 60% da janela).</summary>
    static FrameworkElement? Alvo(Control c)
    {
        var janela = c.XamlRoot?.Size ?? default;
        bool Grande(FrameworkElement e) => janela.Width > 0 && e.ActualWidth * e.ActualHeight > 0.6 * janela.Width * janela.Height;
        FrameworkElement? primeiro = null;
        var p = VisualTreeHelper.GetParent(c) as FrameworkElement;
        // Indicador numa fileira de botões (Cancelar, Nova análise...): sem brilho — o contorno seria dos botões, não do trabalho.
        if (p is Panel fileira && fileira.Children.Any(x => x is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase)) return null;
        for (var i = 0; i < 5 && p is not null; i++, p = VisualTreeHelper.GetParent(p) as FrameworkElement)
        {
            if (p is ScrollViewer or ListViewBase || Grande(p)) break;
            primeiro ??= p is Panel or Border ? p : null;
            if (p is InfoBar or Expander) return p;
            // Indicador dentro de um botão (a pílula da análise na barra de título): quem acende é a moldura em volta dele, chamando Definir.
            if (p is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase) return null;
            if (p is Border { CornerRadius.TopLeft: > 0 } or Grid { CornerRadius.TopLeft: > 0 } or StackPanel { CornerRadius.TopLeft: > 0 }) return p;
        }
        return primeiro is not null && !Grande(primeiro) ? primeiro : null;
    }
}

/// <summary>O brilho do contorno (cópia fiel do ContourGlow do WinStatusBar, 2026-11-17): linha com o gradiente arco-íris no contorno, o
/// mesmo gradiente desfocado esmaecendo para dentro e um halo curto por fora; linha e halo deslizam para um lado, o brilho para o outro, e
/// o brilho respira. O desfoque é feito uma vez numa máscara parada (Win2D) e o gradiente desliza por trás dela, a ~32 quadros/s.</summary>
sealed class BrilhoArcoIris
{
    static readonly Color[] Arco =
    [
        Color.FromArgb(255, 255, 66, 66), Color.FromArgb(255, 160, 255, 66), Color.FromArgb(255, 66, 160, 255),
        Color.FromArgb(255, 66, 207, 255), Color.FromArgb(255, 160, 66, 255),
    ];
    static Color[] Paleta(bool escuro) => Arco.Select(c => Lavar(c, escuro ? 0.25 : 0.1)).ToArray();
    static Color Lavar(Color c, double q) => Color.FromArgb(255, (byte)(c.R + (255 - c.R) * q), (byte)(c.G + (255 - c.G) * q), (byte)(c.B + (255 - c.B) * q));
    // Um pouco mais forte que na barra do WinStatusBar: aqui o brilho aparece sobre cartões, não sobre o fundo translúcido da barra.
    static float OpacidadeBrilho(bool escuro) => escuro ? 0.32f : 0.4f;
    static float OpacidadeLinha(bool escuro) => escuro ? 0.85f : 0.95f;
    static float OpacidadeHalo(bool escuro) => escuro ? 0.4f : 0.5f;

    const float Desfoque = 5f, TracoBrilho = 13f, Linha = 1.5f;
    const float HaloMargem = 9f, HaloDesfoque = 2.1f, HaloTraco = 1.2f, HaloCresce = 0.6f;
    const float PixelsPorSegundo = 156f;

    readonly FrameworkElement elemento;
    readonly float canto;
    readonly Compositor c;
    readonly LayerVisual raiz;
    readonly ContainerVisual raizLinha;
    readonly SpriteVisual raizBrilho, raizHalo;
    readonly CompositionDrawingSurface mascaraBrilho, mascaraHalo;
    readonly CompositionRoundedRectangleGeometry geometria;
    readonly CompositionLinearGradientBrush pincelLinha, pincelBrilho, pincelHalo;
    Canvas? hospedeHalo;
    Vector3 posto = new(float.NaN);
    bool ativo, rodando, preso;
    Vector2 tamanho;
    (Vector2 Tamanho, float Escala) mascaraFeita = (new(float.NaN), float.NaN);

    public bool Ativo { get => ativo; set { ativo = value; Atualizar(false); } }

    // Estado para o autoteste (1.0.0).
    internal bool Rodando => rodando;
    internal bool ContornoVisivel => raiz.IsVisible;
    internal bool HaloVisivel => raizHalo.IsVisible;
    internal bool TemCamadaDoHalo => hospedeHalo is not null;

    public BrilhoArcoIris(FrameworkElement elemento, float canto)
    {
        this.elemento = elemento; this.canto = canto;
        c = ElementCompositionPreview.GetElementVisual(elemento).Compositor;
        geometria = c.CreateRoundedRectangleGeometry();
        pincelLinha = Gradiente(); pincelBrilho = Gradiente(); pincelHalo = Gradiente();
        raizBrilho = Mascarado(pincelBrilho, out mascaraBrilho);
        raizLinha = c.CreateContainerVisual();
        raizLinha.Children.InsertAtTop(Traco(geometria, pincelLinha, Linha));
        raiz = c.CreateLayerVisual();
        raiz.IsVisible = false;
        // Só o contorno (pedido do usuário, 2026-11-17): o brilho desfocado para dentro cobria botões, balões e barras inteiros.
        raiz.Children.InsertAtTop(raizLinha);
        raizHalo = Mascarado(pincelHalo, out mascaraHalo);
        raizHalo.IsVisible = false;
        if (elemento.IsLoaded) Prender();
        elemento.Loaded += (_, _) => { if (!preso) Prender(); else Atualizar(true); };
        elemento.SizeChanged += (_, _) => Atualizar(true);
        elemento.ActualThemeChanged += (_, _) => Cores();
        elemento.LayoutUpdated += (_, _) => { if (raizHalo.IsVisible) Posicionar(); };
        // 1.0.0: saiu da árvore = apaga e devolve a camada do halo (o Loaded seguinte prende de novo, no lugar novo).
        elemento.Unloaded += (_, _) => { if (!elemento.IsLoaded) { Atualizar(false); Soltar(); } };
        Cores();
    }

    // 1.0.0 (correção dos halos que ficavam acesos com o app ocioso): o halo mora num Canvas de um Grid ancestral, fora do quadro; quando o
    // quadro (ou a página dele) era recolhido, nada avisava o brilho, e o halo em pílula continuava visível e animando. Agora o brilho vigia a
    // visibilidade do elemento e de todos os ancestrais e só liga com a cadeia inteira visível.
    readonly List<(UIElement Elemento, long Token)> vigias = [];

    void Vigiar()
    {
        Desvigiar();
        for (var p = (DependencyObject)elemento; p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is UIElement u) vigias.Add((u, u.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => Atualizar(true))));
    }

    void Desvigiar()
    {
        foreach (var (u, token) in vigias) u.UnregisterPropertyChangedCallback(UIElement.VisibilityProperty, token);
        vigias.Clear();
    }

    void Soltar()
    {
        if (!preso) return;
        preso = false;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) Ui.AnimationsEnabledChanged -= AoMudarAnimacoes;
        if (raizObservada is not null) raizObservada.Changed -= AoMudarRaiz;
        raizObservada = null;
        Desvigiar();
        if (hospedeHalo is { } h)
        {
            ElementCompositionPreview.SetElementChildVisual(h, null);
            (VisualTreeHelper.GetParent(h) as Panel ?? h.Parent as Panel)?.Children.Remove(h);
            hospedeHalo = null;
        }
        posto = new(float.NaN);
    }

    bool Escuro => elemento.ActualTheme == ElementTheme.Dark;

    XamlRoot? raizObservada;
    void AoMudarRaiz(XamlRoot sender, XamlRootChangedEventArgs args)
    {
        if (rodando && mascaraFeita.Escala != (float)sender.RasterizationScale) Atualizar(true);
    }

    void AoMudarAnimacoes(Windows.UI.ViewManagement.UISettings sender, object args) =>
        elemento.DispatcherQueue.TryEnqueue(() => Atualizar(true));

    static CompositionGraphicsDevice? grafico;

    static CompositionGraphicsDevice Grafico(Compositor c)
    {
        if (grafico is not null) return grafico;
        var tela = CanvasDevice.GetSharedDevice();
        tela.DeviceLost += AoPerderDispositivo;
        grafico = CanvasComposition.CreateCompositionGraphicsDevice(c, tela);
        return grafico;
    }

    static void AoPerderDispositivo(CanvasDevice s, object a)
    {
        s.DeviceLost -= AoPerderDispositivo;
        var novo = CanvasDevice.GetSharedDevice();
        novo.DeviceLost += AoPerderDispositivo;
        if (grafico is not null) CanvasComposition.SetCanvasDevice(grafico, novo);
    }

    SpriteVisual Mascarado(CompositionBrush fonte, out CompositionDrawingSurface mascara)
    {
        mascara = Grafico(c).CreateDrawingSurface2(new Windows.Graphics.SizeInt32 { Width = 1, Height = 1 }, DirectXPixelFormat.B8G8R8A8UIntNormalized, DirectXAlphaMode.Premultiplied);
        var pm = c.CreateSurfaceBrush(mascara);
        pm.Stretch = CompositionStretch.Fill;
        var p = c.CreateMaskBrush();
        p.Source = fonte;
        p.Mask = pm;
        var s = c.CreateSpriteVisual();
        s.Brush = p;
        return s;
    }

    internal (int Largura, int Altura) ConferirMascara(float escala)
    {
        DesenharMascaras(tamanho, escala);
        return (mascaraBrilho.SizeInt32.Width, mascaraBrilho.SizeInt32.Height);
    }

    void DesenharMascaras(Vector2 t, float escala)
    {
        if (mascaraFeita.Tamanho == t && mascaraFeita.Escala == escala) return;
        var k = Pilula(t);
        try
        {
            Contorno(mascaraBrilho, t, escala, new Vector2(0.5f), t - new Vector2(1f), k, TracoBrilho, Desfoque);
            var fora = t + new Vector2(2 * HaloMargem);
            Contorno(mascaraHalo, fora, escala, new Vector2(HaloMargem + 0.5f - HaloCresce), t - new Vector2(1f) + new Vector2(2 * HaloCresce), k + HaloCresce, HaloTraco, HaloDesfoque);
            mascaraFeita = (t, escala);
        }
        catch (Exception e) { RegistroErros.Gravar("brilho.mascara", e); }
    }

    static void Contorno(CompositionDrawingSurface sup, Vector2 tam, float escala, Vector2 desloc, Vector2 ret, float k, float traco, float desfoque)
    {
        var px = new Windows.Graphics.SizeInt32 { Width = Math.Max(1, (int)Math.Ceiling(tam.X * escala)), Height = Math.Max(1, (int)Math.Ceiling(tam.Y * escala)) };
        sup.Resize(px);
        using var ds = CanvasComposition.CreateDrawingSession(sup);
        ds.Clear(Microsoft.UI.Colors.Transparent);
        using var forma = new CanvasCommandList(ds);
        using (var s = forma.CreateDrawingSession())
        using (s.CreateLayer(1f, new Windows.Foundation.Rect(0, 0, px.Width, px.Height)))
            s.DrawRoundedRectangle(desloc.X * escala, desloc.Y * escala, ret.X * escala, ret.Y * escala, k * escala, k * escala, Microsoft.UI.Colors.White, traco * escala);
        using var borrado = new GaussianBlurEffect { Source = forma, BlurAmount = desfoque * escala, BorderMode = EffectBorderMode.Soft, Optimization = EffectOptimization.Quality };
        ds.DrawImage(borrado);
    }

    CompositionLinearGradientBrush Gradiente()
    {
        var p = c.CreateLinearGradientBrush();
        p.MappingMode = CompositionMappingMode.Absolute;
        p.ExtendMode = CompositionGradientExtendMode.Wrap;
        for (var i = 0; i <= Arco.Length; i++) p.ColorStops.Add(c.CreateColorGradientStop(i / (float)Arco.Length, Arco[i % Arco.Length]));
        return p;
    }

    ShapeVisual Traco(CompositionGeometry g, CompositionBrush p, float espessura)
    {
        var f = c.CreateSpriteShape(g);
        f.StrokeBrush = p;
        f.StrokeThickness = espessura;
        var v = c.CreateShapeVisual();
        v.Shapes.Add(f);
        v.RelativeSizeAdjustment = Vector2.One;
        return v;
    }

    void Prender()
    {
        if (preso) return;
        preso = true;
        // O visual filho substitui outro que o elemento já tivesse; os elementos escolhidos (cartões, InfoBar, painéis) não usam.
        ElementCompositionPreview.SetElementChildVisual(elemento, raiz);
        Grid? hospede = null;
        var topo = elemento.XamlRoot?.Content;
        for (var p = VisualTreeHelper.GetParent(elemento); p is not null; p = VisualTreeHelper.GetParent(p))
        {
            if (p is ScrollViewer) hospede = null;
            else if (hospede is null && p is Grid { CornerRadius.TopLeft: 0 } g) hospede = g;
            if (ReferenceEquals(p, topo)) break;
        }
        if (hospede is not null)
        {
            hospedeHalo = new Canvas { IsHitTestVisible = false };
            Grid.SetRowSpan(hospedeHalo, Math.Max(1, hospede.RowDefinitions.Count));
            Grid.SetColumnSpan(hospedeHalo, Math.Max(1, hospede.ColumnDefinitions.Count));
            hospede.Children.Add(hospedeHalo);
            ElementCompositionPreview.SetElementChildVisual(hospedeHalo, raizHalo);
        }
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041)) Ui.AnimationsEnabledChanged += AoMudarAnimacoes;
        raizObservada = elemento.XamlRoot;
        if (raizObservada is not null) raizObservada.Changed += AoMudarRaiz;
        Vigiar();
        Atualizar(true);
    }

    void Cores()
    {
        var paleta = Paleta(Escuro);
        foreach (var p in new[] { pincelLinha, pincelBrilho, pincelHalo })
            for (var i = 0; i < p.ColorStops.Count; i++) p.ColorStops[i].Color = paleta[i % paleta.Length];
        raizLinha.Opacity = OpacidadeLinha(Escuro);
        respira = (OpacidadeBrilho(Escuro), OpacidadeHalo(Escuro));
    }

    void Posicionar()
    {
        if (hospedeHalo is null) return;
        try
        {
            var p = elemento.TransformToVisual(hospedeHalo).TransformPoint(new Windows.Foundation.Point(0, 0));
            var d = new Vector3((float)p.X - HaloMargem, (float)p.Y - HaloMargem, 0);
            if (d != posto) { raizHalo.Offset = d; posto = d; }
        }
        catch (ArgumentException) { }
    }

    readonly record struct Deslize(CompositionLinearGradientBrush Pincel, float Distancia, double Segundos);
    readonly List<Deslize> deslizes = [];
    (float Brilho, float Halo) respira;
    Action<double>? quadro;

    void Deslizar(CompositionLinearGradientBrush p, float x0, float periodo, float sentido, float passo)
    {
        p.StartPoint = new Vector2(x0, 0);
        p.EndPoint = new Vector2(x0 + periodo, 0);
        deslizes.Add(new(p, sentido * periodo, periodo / (PixelsPorSegundo * passo)));
    }

    static float Respiro(double t, double s, float o)
    {
        var f = RelogioQuadros.Fase(t, s);
        var k = f < 0.38 ? 1 + (0.7 - 1) * (f / 0.38) : f < 0.71 ? 0.7 + (1.12 - 0.7) * ((f - 0.38) / 0.33) : 1.12 + (1 - 1.12) * ((f - 0.71) / 0.29);
        return (float)(o * k);
    }

    int quadrosDesdeConferencia;

    void AoQuadro(double t)
    {
        // Rede de segurança (~2 vezes por segundo): se o quadro saiu de vista por um caminho não vigiado, desliga e para o relógio.
        if (++quadrosDesdeConferencia >= 16)
        {
            quadrosDesdeConferencia = 0;
            if (!elemento.IsLoaded || !Trabalhando.VisivelDeFato(elemento)) { Atualizar(false); return; }
        }
        foreach (var d in deslizes) d.Pincel.Offset = new Vector2((float)(d.Distancia * RelogioQuadros.Fase(t, d.Segundos)), 0);
        raizBrilho.Opacity = Respiro(t, 7.3, respira.Brilho);
        raizHalo.Opacity = Respiro(t, 9.1, respira.Halo);
    }

    /// <summary>Padrão único (2026-11-17, pedido do usuário): todo contorno arco-íris é uma pílula, cantos totalmente circulares, como a
    /// pílula da análise na barra de título.</summary>
    static float Pilula(Vector2 t) => Math.Max(0, (t.Y - 1) / 2);

    CornerRadius? cantoOriginal;

    /// <summary>Enquanto brilha, o próprio quadro também fica em pílula, para o fundo e a borda dele acompanharem o contorno.</summary>
    void Arredondar(bool pilula)
    {
        if (pilula)
        {
            var r = new CornerRadius(Math.Max(0, (elemento.ActualHeight) / 2));
            cantoOriginal ??= Canto;
            Canto = r;
        }
        else if (cantoOriginal is { } o) { Canto = o; cantoOriginal = null; }
    }

    CornerRadius Canto
    {
        get => elemento switch { Border b => b.CornerRadius, Control c => c.CornerRadius, Grid g => g.CornerRadius, StackPanel s => s.CornerRadius, _ => default };
        set { switch (elemento) { case Border b: b.CornerRadius = value; break; case Control c: c.CornerRadius = value; break; case Grid g: g.CornerRadius = value; break; case StackPanel s: s.CornerRadius = value; break; } }
    }

    static readonly Windows.UI.ViewManagement.UISettings Ui = new();
    static bool AnimacoesLigadas { get { try { return Ui.AnimationsEnabled; } catch (Exception) { return true; } } }

    void Atualizar(bool refazer)
    {
        var t = new Vector2((float)elemento.ActualWidth, (float)elemento.ActualHeight);
        var ligar = ativo && preso && elemento.IsLoaded && Trabalhando.VisivelDeFato(elemento) && t.X > 20 && t.Y > 10;
        if (!ligar)
        {
            if (rodando)
            {
                if (quadro is not null) RelogioQuadros.Remover(quadro);
                foreach (var p in new[] { pincelLinha, pincelBrilho, pincelHalo }) p.Offset = Vector2.Zero;
                rodando = false;
            }
            raiz.IsVisible = false;
            raizHalo.IsVisible = false;
            Arredondar(false);
            return;
        }
        if (rodando && !refazer && t == tamanho) return;
        tamanho = t;
        var k = Pilula(t);
        Arredondar(true);
        raiz.Size = t; raizBrilho.Size = t; raizLinha.Size = t;
        geometria.Offset = new Vector2(0.5f);
        geometria.Size = t - new Vector2(1f);
        geometria.CornerRadius = new Vector2(k);
        raizHalo.Size = t + new Vector2(2 * HaloMargem);
        DesenharMascaras(t, (float)(elemento.XamlRoot?.RasterizationScale ?? 1));
        var periodo = Math.Max(240f, t.X * 2);
        deslizes.Clear();
        Deslizar(pincelLinha, 0, periodo, 1, 1.00f);
        Deslizar(pincelBrilho, 0, periodo * 1.37f, -1, 0.91f);
        Deslizar(pincelHalo, HaloMargem, periodo * 1.18f, 1, 0.87f);
        Cores();
        Posicionar();
        raizHalo.IsVisible = hospedeHalo is not null;
        raiz.IsVisible = true;
        rodando = true;
        if (AnimacoesLigadas) RelogioQuadros.Adicionar(quadro ??= AoQuadro);
        else
        {
            if (quadro is not null) RelogioQuadros.Remover(quadro);
            AoQuadro(0);
        }
    }
}

/// <summary>Relógio único das animações contínuas (~32 quadros/s num temporizador da thread da janela), como o FrameClock do WinStatusBar:
/// animações do compositor manteriam a composição na taxa da tela inteira enquanto existissem.</summary>
static class RelogioQuadros
{
    static readonly TimeSpan Periodo = TimeSpan.FromMilliseconds(31);
    static readonly Stopwatch Tempo = Stopwatch.StartNew();
    [ThreadStatic] static List<Action<double>>? quadros;
    [ThreadStatic] static DispatcherQueueTimer? temporizador;

    public static double Agora => Tempo.Elapsed.TotalSeconds;
    /// <summary>Quantas animações estão no relógio desta thread (autoteste: ocioso = 0) e se o temporizador está rodando.</summary>
    internal static int Quantos => quadros?.Count ?? 0;
    internal static bool Rodando => temporizador?.IsRunning == true;
    public static double Fase(double t, double s) => s <= 0 ? 0 : t / s - Math.Floor(t / s);

    public static void Adicionar(Action<double> q)
    {
        var lista = quadros ??= [];
        if (lista.Contains(q)) return;
        lista.Add(q);
        if (temporizador is null && DispatcherQueue.GetForCurrentThread() is { } fila)
        {
            temporizador = fila.CreateTimer();
            temporizador.Interval = Periodo;
            temporizador.IsRepeating = true;
            temporizador.Tick += (_, _) => { var t = Agora; foreach (var f in lista.ToArray()) f(t); };
        }
        if (temporizador is { IsRunning: false }) temporizador.Start();
        q(Agora);
    }

    public static void Remover(Action<double> q)
    {
        if (quadros is { } lista && lista.Remove(q) && lista.Count == 0) temporizador?.Stop();
    }
}
