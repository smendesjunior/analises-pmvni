using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI.ViewManagement;

namespace Pmvni.App;

/// <summary>Halo arredondado atrás da pílula enquanto a análise está em execução.</summary>
public sealed partial class HaloAnalise : Grid
{
    readonly UISettings preferencias = new();
    readonly AccessibilitySettings acessibilidade = new();

    readonly SpriteVisual luz;
    bool ativo;

    public HaloAnalise()
    {
        var verde = Microsoft.UI.ColorHelper.FromArgb(255, 31, 188, 88);
        var mascara = new Rectangle { RadiusX = 18, RadiusY = 18, Fill = new SolidColorBrush(verde) };
        Children.Add(mascara);
        var anel = new Border { Margin = new Thickness(-2), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
            BorderBrush = new SolidColorBrush(verde) };
        Children.Add(anel);
        var suporte = new Grid();
        Children.Insert(0, suporte);
        var compositor = ElementCompositionPreview.GetElementVisual(suporte).Compositor;
        var sombra = compositor.CreateDropShadow();
        sombra.Mask = mascara.GetAlphaMask();
        sombra.BlurRadius = 16;
        sombra.Color = verde;
        sombra.Offset = Vector3.Zero;
        luz = compositor.CreateSpriteVisual();
        luz.Brush = compositor.CreateColorBrush(Microsoft.UI.Colors.Transparent);
        luz.RelativeSizeAdjustment = Vector2.One;
        luz.Shadow = sombra;
        ElementCompositionPreview.SetElementChildVisual(suporte, luz);
        Loaded += (_, _) => Atualizar();
        Unloaded += (_, _) => { luz.StopAnimation("Opacity"); pulsando = null; };
        Visibility = Visibility.Collapsed;
    }

    internal bool Ativo
    {
        set
        {
            ativo = value;
            Atualizar();
        }
    }

    bool? pulsando;
    void Atualizar()
    {
        var visivel = ativo && !acessibilidade.HighContrast;
        Visibility = visivel ? Visibility.Visible : Visibility.Collapsed;
        var animar = visivel && preferencias.AnimationsEnabled && IsLoaded;
        if (pulsando == animar) return;
        pulsando = animar;
        luz.StopAnimation("Opacity");
        luz.Opacity = .48f;
        if (!animar) return;
        var pulso = luz.Compositor.CreateScalarKeyFrameAnimation();
        pulso.InsertKeyFrame(0, .32f);
        pulso.InsertKeyFrame(.5f, .95f);
        pulso.InsertKeyFrame(1, .32f);
        pulso.Duration = TimeSpan.FromSeconds(2.4);
        pulso.IterationBehavior = AnimationIterationBehavior.Forever;
        luz.StartAnimation("Opacity", pulso);
    }
}
