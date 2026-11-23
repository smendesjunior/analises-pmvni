using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace Pmvni.App;

/// <summary>Uma onda discreta se afasta do contorno da pílula durante a análise.</summary>
public sealed partial class HaloAnalise : Grid
{
    readonly UISettings preferencias = new();
    readonly AccessibilitySettings acessibilidade = new();
    readonly Visual onda;
    bool ativo;
    bool pulsando;

    public HaloAnalise()
    {
        var borda = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 17, 143, 70))
        };
        Children.Add(borda);
        onda = ElementCompositionPreview.GetElementVisual(borda);
        onda.Opacity = 0;
        Loaded += (_, _) => Atualizar();
        Unloaded += (_, _) => Parar();
        SizeChanged += (_, _) => { Parar(); Atualizar(); };
    }

    internal bool Ativo
    {
        set { ativo = value; Atualizar(); }
    }

    void Parar()
    {
        if (!pulsando) return;
        pulsando = false;
        onda.StopAnimation("Scale");
        onda.StopAnimation("Opacity");
        onda.Opacity = 0;
        onda.Scale = Vector3.One;
    }

    void Atualizar()
    {
        if (!IsLoaded || !ativo || acessibilidade.HighContrast || !preferencias.AnimationsEnabled
            || ActualWidth <= 0 || ActualHeight <= 0)
        {
            Parar();
            return;
        }
        if (pulsando) return;
        pulsando = true;
        var largura = (float)ActualWidth;
        var altura = (float)ActualHeight;
        onda.CenterPoint = new Vector3(largura / 2, altura / 2, 0);
        // O mesmo deslocamento nas quatro bordas, inclusive no modo compacto.
        const float alcance = 5;
        var fim = new Vector3(1 + 2 * alcance / largura, 1 + 2 * alcance / altura, 1);
        var expansao = onda.Compositor.CreateVector3KeyFrameAnimation();
        expansao.InsertKeyFrame(0, Vector3.One);
        expansao.InsertKeyFrame(.72f, fim);
        expansao.InsertKeyFrame(1, fim);
        var opacidade = onda.Compositor.CreateScalarKeyFrameAnimation();
        opacidade.InsertKeyFrame(0, 0);
        opacidade.InsertKeyFrame(.16f, .78f);
        opacidade.InsertKeyFrame(.72f, 0);
        opacidade.InsertKeyFrame(1, 0);
        expansao.Duration = opacidade.Duration = TimeSpan.FromSeconds(3.4);
        expansao.IterationBehavior = opacidade.IterationBehavior = AnimationIterationBehavior.Forever;
        onda.StartAnimation("Scale", expansao);
        onda.StartAnimation("Opacity", opacidade);
    }
}
