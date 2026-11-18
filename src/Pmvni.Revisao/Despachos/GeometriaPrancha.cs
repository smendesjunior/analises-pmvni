using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace Pmvni.Revisao;

internal sealed class GeometriaPrancha
{
    readonly double esquerda, baseY, direita, topo, unidade;
    readonly int giro;
    public double Largura { get; }
    public double Altura { get; }

    public GeometriaPrancha(PdfPage folha)
    {
        var media = folha.MediaBox;
        var crop = folha.Elements.ContainsKey("/CropBox") ? folha.CropBox : media;
        if (crop.IsZero) crop = media;
        folha.CropBox = crop;
        esquerda = Math.Max(media.X1, crop.X1);
        baseY = Math.Max(media.Y1, crop.Y1);
        direita = Math.Min(media.X2, crop.X2);
        topo = Math.Min(media.Y2, crop.Y2);
        unidade = folha.Elements.ContainsKey("/UserUnit") ? folha.Elements.GetReal("/UserUnit") : 1;
        giro = (folha.Rotate % 360 + 360) % 360;
        if (!double.IsFinite(unidade) || unidade <= 0 || direita <= esquerda || topo <= baseY || giro % 90 != 0)
            throw new ArgumentException("A página contém uma geometria inválida.");
        Largura = (giro % 180 == 0 ? direita - esquerda : topo - baseY) * unidade;
        Altura = (giro % 180 == 0 ? topo - baseY : direita - esquerda) * unidade;
    }

    public XPoint ParaPdf(double u, double v) => giro switch
    {
        0 => new(esquerda + u/unidade, baseY + v/unidade),
        90 => new(direita - v/unidade, baseY + u/unidade),
        180 => new(direita - u/unidade, topo - v/unidade),
        _ => new(esquerda + v/unidade, topo - u/unidade)
    };

    public string Matriz()
    {
        var o = ParaPdf(0, 0);
        var u = ParaPdf(1, 0);
        var v = ParaPdf(0, 1);
        return FormattableString.Invariant($"{u.X-o.X:0.##########} {u.Y-o.Y:0.##########} {v.X-o.X:0.##########} {v.Y-o.Y:0.##########} {o.X:0.##########} {o.Y:0.##########}");
    }
}
