using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Pmvni.App;

/// <summary>Gráfico de pizza do andamento dos processos (2026-11-17): uma fatia por status, na cor do status na planilha, com a dica
/// "STATUS: n (p%)" ao passar o mouse. Desenhado com Path (nada de biblioteca de gráficos).</summary>
public sealed partial class GraficoPizza : Grid
{
    public sealed record Fatia(string Rotulo, int Valor, Brush Cor);

    readonly Canvas tela = new();
    IReadOnlyList<Fatia> fatias = [];

    public GraficoPizza()
    {
        Children.Add(tela);
        SizeChanged += (_, _) => Desenhar();
    }

    public void Mostrar(IReadOnlyList<Fatia> f)
    {
        fatias = f;
        Desenhar();
    }

    void Desenhar()
    {
        tela.Children.Clear();
        var lado = Math.Min(ActualWidth, ActualHeight);
        if (lado <= 0) return;
        var total = fatias.Sum(f => f.Valor);
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var r = lado / 2 - 2;
        var contorno = Application.Current.Resources.TryGetValue("CardBackgroundFillColorDefaultBrush", out var b) && b is Brush br ? br : new SolidColorBrush(Microsoft.UI.Colors.White);
        if (total == 0)
        {
            var vazio = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"] };
            Canvas.SetLeft(vazio, c.X - r);
            Canvas.SetTop(vazio, c.Y - r);
            ToolTipService.SetToolTip(vazio, "Nenhum processo");
            tela.Children.Add(vazio);
            return;
        }
        var angulo = -90.0;
        foreach (var f in fatias.Where(f => f.Valor > 0))
        {
            var varre = 360.0 * f.Valor / total;
            Shape forma;
            if (varre >= 359.99)
            {
                forma = new Ellipse { Width = 2 * r, Height = 2 * r };
                Canvas.SetLeft(forma, c.X - r);
                Canvas.SetTop(forma, c.Y - r);
            }
            else
            {
                var a0 = angulo * Math.PI / 180;
                var a1 = (angulo + varre) * Math.PI / 180;
                var fig = new PathFigure { StartPoint = c, IsClosed = true };
                fig.Segments.Add(new LineSegment { Point = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)) });
                fig.Segments.Add(new ArcSegment
                {
                    Point = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), Size = new Size(r, r),
                    SweepDirection = SweepDirection.Clockwise, IsLargeArc = varre > 180,
                });
                var geo = new PathGeometry();
                geo.Figures.Add(fig);
                forma = new Microsoft.UI.Xaml.Shapes.Path { Data = geo };
            }
            forma.Fill = f.Cor;
            forma.Stroke = contorno;
            forma.StrokeThickness = 1.5;
            var dica = $"{f.Rotulo}: {f.Valor} ({100.0 * f.Valor / total:0}%)";
            ToolTipService.SetToolTip(forma, dica);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(forma, dica);
            tela.Children.Add(forma);
            angulo += varre;
        }
    }
}
