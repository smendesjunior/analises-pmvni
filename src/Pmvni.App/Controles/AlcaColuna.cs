using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace Pmvni.App;

/// <summary>A alça na borda direita do título de uma coluna (como no Excel): arrastar muda a largura; dois cliques voltam ao padrão.
/// O cursor vira a seta dupla ↔ (ProtectedCursor só existe dentro de uma subclasse).</summary>
internal sealed class AlcaColuna : Grid
{
    double inicioX, inicioLargura;
    bool arrastando;

    /// <summary>A largura que a coluna tem agora (lida no começo de cada arraste).</summary>
    public required Func<double> LarguraAtual { get; init; }

    /// <summary>Durante o arraste, a cada movimento (a largura nova, já dentro dos limites).</summary>
    public event Action<double>? Mudando;

    /// <summary>Ao soltar o botão do mouse, com a largura final.</summary>
    public event Action<double>? Mudou;

    /// <summary>Dois cliques na alça: volta a coluna à largura padrão.</summary>
    public event Action? Restaurar;

    public const double Minima = 48, Maxima = 900;

    public AlcaColuna()
    {
        Width = 12;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Stretch;
        Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent); // sem fundo, a alça não recebe o mouse
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
        Children.Add(new Border { Width = 1, Margin = new Thickness(0, 10, 0, 10), HorizontalAlignment = HorizontalAlignment.Center, Opacity = .35, Background = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            arrastando = CapturePointer(e.Pointer);
            inicioX = e.GetCurrentPoint(null).Position.X;
            inicioLargura = LarguraAtual();
            e.Handled = true;
        };
        PointerMoved += (_, e) =>
        {
            if (!arrastando) return;
            Mudando?.Invoke(Limitar(inicioLargura + e.GetCurrentPoint(null).Position.X - inicioX));
            e.Handled = true;
        };
        PointerReleased += (_, e) =>
        {
            if (!arrastando) return;
            arrastando = false;
            ReleasePointerCapture(e.Pointer);
            Mudou?.Invoke(Limitar(inicioLargura + e.GetCurrentPoint(null).Position.X - inicioX));
            e.Handled = true;
        };
        PointerCaptureLost += (_, _) => arrastando = false;
        DoubleTapped += (_, e) => { Restaurar?.Invoke(); e.Handled = true; };
        Tapped += (_, e) => e.Handled = true; // não ordena a coluna ao clicar na alça
    }

    static double Limitar(double w) => Math.Round(Math.Clamp(w, Minima, Maxima));
}
