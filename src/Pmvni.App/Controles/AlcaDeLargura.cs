using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

/// <summary>Alça fina na borda de um painel lateral: arrastar muda a largura do painel (correção 2026-11-16).
/// Fica dentro do painel, na borda <see cref="Borda"/>; o painel é o pai (FrameworkElement) com Width definido.</summary>
public sealed partial class AlcaDeLargura : Grid
{
    public HorizontalAlignment Borda { get; set; } = HorizontalAlignment.Right;
    public double Minimo { get; set; } = 220;
    public double Maximo { get; set; } = 720;

    double inicioX, inicioLargura;
    bool arrastando;

    public AlcaDeLargura()
    {
        Width = 6;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        VerticalAlignment = VerticalAlignment.Stretch;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        Loaded += (_, _) =>
        {
            HorizontalAlignment = Borda;
            Grid.SetRowSpan(this, 99);
            Grid.SetColumnSpan(this, 99);
            Canvas.SetZIndex(this, 100);
        };
        PointerPressed += AoPressionar;
        PointerMoved += AoMover;
        PointerReleased += (_, e) => Soltar(e);
        PointerCaptureLost += (_, _) => arrastando = false;
    }

    FrameworkElement? Painel => Parent as FrameworkElement;

    void AoPressionar(object sender, PointerRoutedEventArgs e)
    {
        if (Painel is not { } p) return;
        arrastando = CapturePointer(e.Pointer);
        inicioX = e.GetCurrentPoint(null).Position.X;
        inicioLargura = p.ActualWidth;
        e.Handled = true;
    }

    void AoMover(object sender, PointerRoutedEventArgs e)
    {
        if (!arrastando || Painel is not { } p) return;
        var dx = e.GetCurrentPoint(null).Position.X - inicioX;
        if (Borda == HorizontalAlignment.Left) dx = -dx;
        // Auditoria 2026-11-16: cada painel fica com no máximo 40 % da janela, para os dois juntos nunca esmagarem as folhas.
        var teto = XamlRoot is { } raiz && raiz.Size.Width > 0 ? Math.Max(Minimo, Math.Min(Maximo, raiz.Size.Width * 0.4)) : Maximo;
        p.Width = Math.Clamp(inicioLargura + dx, Minimo, teto);
        e.Handled = true;
    }

    void Soltar(PointerRoutedEventArgs e)
    {
        if (!arrastando) return;
        arrastando = false;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
}
