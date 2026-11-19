using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Pmvni.App;

internal sealed class CabecalhoMovel : Button
{
    public event Action? CliqueDoPonteiro;
    public event Action<Point>? SoltouColuna;
    private uint? ponteiro;
    private Point inicio;
    private bool movendo;

    protected override void OnPointerPressed(PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !CapturePointer(e.Pointer))
        {
            base.OnPointerPressed(e);
            return;
        }
        ponteiro = e.Pointer.PointerId;
        inicio = e.GetCurrentPoint(null).Position;
        movendo = false;
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        if (ponteiro != e.Pointer.PointerId) { base.OnPointerMoved(e); return; }
        if (Math.Abs(e.GetCurrentPoint(null).Position.X - inicio.X) >= 5)
        {
            movendo = true;
            Opacity = .65;
        }
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerRoutedEventArgs e)
    {
        if (ponteiro != e.Pointer.PointerId) { base.OnPointerReleased(e); return; }
        var destino = e.GetCurrentPoint(null).Position;
        var local = e.GetCurrentPoint(this).Position;
        var mover = movendo;
        ponteiro = null;
        ReleasePointerCapture(e.Pointer);
        Opacity = 1;
        e.Handled = true;
        if (mover) SoltouColuna?.Invoke(destino);
        else if (local.X >= 0 && local.X < ActualWidth && local.Y >= 0 && local.Y < ActualHeight) CliqueDoPonteiro?.Invoke();
    }

    protected override void OnPointerCaptureLost(PointerRoutedEventArgs e)
    {
        ponteiro = null;
        movendo = false;
        Opacity = 1;
        base.OnPointerCaptureLost(e);
    }
}
