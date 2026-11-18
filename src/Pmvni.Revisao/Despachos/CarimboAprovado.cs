using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;

namespace Pmvni.Revisao;

public static class CarimboAprovado
{
    public const double LarguraPt = 92 * 72 / 25.4;
    public const double AlturaPt = LarguraPt * 896 / 1200;

    // Coordenadas em pontos, a partir do canto superior esquerdo da folha.
    public static PdfAssinado Aplicar(byte[] prancha, int pagina, double x, double y, string peticao,
        CertificadoAberto analista, DateTimeOffset emissao,
        IReadOnlyList<ObservacaoPrancha>? observacoes = null, XRect? areaObservacoes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(peticao);
        ArgumentNullException.ThrowIfNull(analista);
        using var entrada = new MemoryStream(prancha, writable: false);
        using var doc = PdfReader.Open(entrada, PdfDocumentOpenMode.Modify);
        if ((uint)pagina >= (uint)doc.PageCount) throw new ArgumentOutOfRangeException(nameof(pagina));
        var folha = doc.Pages[pagina];
        var caixa = folha.MediaBox;
        if (folha.Rotate % 360 != 0 || caixa.X1 != 0 || caixa.Y1 != 0 ||
            (folha.Elements.ContainsKey("/CropBox") && folha.CropBox != caixa) ||
            (folha.Elements.ContainsKey("/UserUnit") && folha.Elements.GetReal("/UserUnit") != 1))
            throw new NotSupportedException("Esta geometria de folha requer transforma√ß√£o antes de aplicar o carimbo.");
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 ||
            x + LarguraPt > caixa.Width || y + AlturaPt > caixa.Height)
            throw new ArgumentOutOfRangeException(nameof(x), "O carimbo deve caber por inteiro na prancha.");
        AssinaturaPdf.PrepararFontes();
        using var arte = typeof(CarimboAprovado).Assembly.GetManifestResourceStream("Pmvni.CarimboAprovado.pdf")
            ?? throw new InvalidOperationException("Arte do carimbo ausente no aplicativo.");
        using var modelo = XPdfForm.FromStream(arte);
        const double escala = LarguraPt / 1200;
        XRect Area(double esquerda, double topo, double largura, double altura) =>
            new(x + esquerda * escala, y + topo * escala, largura * escala, altura * escala);
        using (var gfx = XGraphics.FromPdfPage(folha, XGraphicsPdfPageOptions.Append))
        {
            if (observacoes is { Count: > 0 })
            {
                if (areaObservacoes is not { } bloco || !double.IsFinite(bloco.X) || !double.IsFinite(bloco.Y)
                    || !double.IsFinite(bloco.Width) || !double.IsFinite(bloco.Height)
                    || bloco.X < 0 || bloco.Y < 0 || bloco.Width <= 0 || bloco.Height <= 0
                    || bloco.Right > caixa.Width || bloco.Bottom > caixa.Height)
                    throw new ArgumentException("Escolha um espaÁo para as observaÁıes dentro da prancha.");
                ObservacaoPrancha.Desenhar(gfx, bloco, observacoes);
            }
            gfx.DrawImage(modelo, x, y, LarguraPt, AlturaPt);
            void Preencher(XRect area, string texto)
            {
                gfx.DrawRectangle(XBrushes.White, area);
                var fonte = new XFont("Arial", 8);
                var largura = gfx.MeasureString(texto, fonte).Width;
                if (largura > area.Width) fonte = new XFont("Arial", 8 * area.Width / largura);
                gfx.DrawString(texto, fonte, XBrushes.Black, area, XStringFormats.Center);
            }
            Preencher(Area(155, 398, 350, 66), emissao.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            Preencher(Area(740, 398, 395, 66), peticao.Trim());
        }
        var visto = Area(641, 590, 475, 178);
        var local = new LocalAssinatura(pagina, visto.X, caixa.Height - visto.Bottom, visto.Width, visto.Height);
        using var saida = new MemoryStream();
        doc.Save(saida);
        return AssinaturaPdf.AssinarNoLocal(saida.ToArray(), analista, emissao, null, local, "Visto do Analista T√©cnico");
    }
}
