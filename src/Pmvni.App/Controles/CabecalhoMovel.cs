using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;

namespace Pmvni.App;

internal sealed class CabecalhoMovel : Grid
{
    public CabecalhoMovel()
    {
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        PointerPressed += (_, e) => Pressionar(e);
        PointerMoved += (_, e) => Arrastar(e);
        PointerReleased += (_, e) => Soltar(e);
        PointerCaptureLost += (_, _) => { ponteiro = null; movendo = false; Opacity = 1; };
    }
    public event Action<Point>? InicioDoArraste;
    public event Action? CliqueDoPonteiro;
    public event Action<Point>? SoltouColuna;
    private uint? ponteiro;
    private Point inicio;
    private bool movendo;

    private void Pressionar(PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        InicioDoArraste?.Invoke(e.GetCurrentPoint(null).Position);
        CapturePointer(e.Pointer);
        ponteiro = e.Pointer.PointerId;
        inicio = e.GetCurrentPoint(null).Position;
        movendo = false;
        e.Handled = true;
    }

    private void Arrastar(PointerRoutedEventArgs e)
    {
        if (ponteiro != e.Pointer.PointerId) { return; }
        if (Math.Abs(e.GetCurrentPoint(null).Position.X - inicio.X) >= 5)
        {
            movendo = true;
            Opacity = .65;
        }
        e.Handled = true;
    }

    private void Soltar(PointerRoutedEventArgs e)
    {
        var destino = e.GetCurrentPoint(null).Position;
        var local = e.GetCurrentPoint(this).Position;
        var clique = ponteiro == e.Pointer.PointerId && !movendo && Math.Abs(destino.X - inicio.X) < 5;
        ponteiro = null;
        ReleasePointerCapture(e.Pointer);
        Opacity = 1;
        e.Handled = true;
        SoltouColuna?.Invoke(destino);
        if (clique && local.X >= 0 && local.X < ActualWidth && local.Y >= 0 && local.Y < ActualHeight) CliqueDoPonteiro?.Invoke();
    }

}
