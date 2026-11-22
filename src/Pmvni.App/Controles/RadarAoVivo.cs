using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI.ViewManagement;

namespace Pmvni.App;

public sealed partial class RadarAoVivo : Grid
{
    readonly Ellipse onda;
    readonly UISettings preferencias = new();
    bool ativo;
    bool animando;

    public RadarAoVivo()
    {
        Width = Height = 16;
        var verde = new SolidColorBrush((Windows.UI.Color)Application.Current.Resources["SystemAccentColor"]);
        // A opacidade da onda fica só na composição: com Opacity = 0 no XAML, o elemento seguiria invisível
        // mesmo com a animação de composição rodando.
        onda = new Ellipse { Width = 8, Height = 8, Stroke = verde, StrokeThickness = 1 };
        ElementCompositionPreview.GetElementVisual(onda).Opacity = 0;
        Children.Add(onda);
        Children.Add(new Ellipse { Width = 7, Height = 7, Fill = verde });
        Loaded += (_, _) =>
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                preferencias.AnimationsEnabledChanged += PreferenciaMudou;
            Atualizar();
        };
        Unloaded += (_, _) =>
        {
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                preferencias.AnimationsEnabledChanged -= PreferenciaMudou;
            Parar();
        };
    }

    internal bool Ativo
    {
        set { if (ativo != value) { ativo = value; Atualizar(); } }
    }

    void PreferenciaMudou(UISettings sender, object args) => DispatcherQueue.TryEnqueue(Atualizar);

    void Atualizar()
    {
        if (!IsLoaded || !ativo || !preferencias.AnimationsEnabled) { Parar(); return; }
        if (animando) return;
        var visual = ElementCompositionPreview.GetElementVisual(onda);
        visual.CenterPoint = new Vector3(4, 4, 0);
        var escala = visual.Compositor.CreateVector3KeyFrameAnimation();
        escala.InsertKeyFrame(0, Vector3.One);
        escala.InsertKeyFrame(1, new Vector3(2, 2, 1));
        var opacidade = visual.Compositor.CreateScalarKeyFrameAnimation();
        opacidade.InsertKeyFrame(0, .48f);
        opacidade.InsertKeyFrame(.85f, 0);
        opacidade.InsertKeyFrame(1, 0);
        escala.Duration = opacidade.Duration = TimeSpan.FromSeconds(2.1);
        escala.IterationBehavior = opacidade.IterationBehavior = AnimationIterationBehavior.Forever;
        visual.StartAnimation("Scale", escala);
        visual.StartAnimation("Opacity", opacidade);
        animando = true;
    }

    void Parar()
    {
        if (!animando) return;
        var visual = ElementCompositionPreview.GetElementVisual(onda);
        visual.StopAnimation("Scale");
        visual.StopAnimation("Opacity");
        visual.Opacity = 0;
        animando = false;
    }
}
