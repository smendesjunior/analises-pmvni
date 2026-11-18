using System.Globalization;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf;
using System.Text;

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
        if (doc.Internals.Catalog.Elements.ContainsKey("/Perms") ||
            doc.Internals.Catalog.Elements.GetDictionary("/AcroForm")?.Elements.GetInteger("/SigFlags") > 0)
            throw new ArgumentException("Esta entrada já contém assinatura digital. Selecione a prancha original sem assinatura.");
        var geometria = new GeometriaPrancha(folha);
        if (!double.IsFinite(x) || !double.IsFinite(y) || x < 0 || y < 0 ||
            x + LarguraPt > geometria.Largura || y + AlturaPt > geometria.Altura)
            throw new ArgumentOutOfRangeException(nameof(x), "O carimbo deve caber por inteiro na prancha.");
        AssinaturaPdf.PrepararFontes();
        using var arte = typeof(CarimboAprovado).Assembly.GetManifestResourceStream("Pmvni.CarimboAprovado.pdf")
            ?? throw new InvalidOperationException("Arte do carimbo ausente no aplicativo.");
        using var modelo = XPdfForm.FromStream(arte);
        const double escala = LarguraPt / 1200;
        XRect Area(double esquerda, double topo, double largura, double altura) =>
            new(x + esquerda * escala, y + topo * escala, largura * escala, altura * escala);
        var camada = doc.AddPage();
        camada.Width = XUnit.FromPoint(geometria.Largura);
        camada.Height = XUnit.FromPoint(geometria.Altura);
        var visto = Area(641, 590, 475, 178);
        using (var gfx = XGraphics.FromPdfPage(camada))
        {
            if (observacoes is { Count: > 0 })
            {
                if (areaObservacoes is not { } bloco || !double.IsFinite(bloco.X) || !double.IsFinite(bloco.Y)
                    || !double.IsFinite(bloco.Width) || !double.IsFinite(bloco.Height)
                    || bloco.X < 0 || bloco.Y < 0 || bloco.Width <= 0 || bloco.Height <= 0
                    || bloco.Right > geometria.Largura || bloco.Bottom > geometria.Altura)
                    throw new ArgumentException("Escolha um espaço para as observações dentro da prancha.");
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
            gfx.TranslateTransform(visto.X, visto.Y);
            var interior = new XRect(0, 0, visto.Width, visto.Height);
            gfx.IntersectClip(interior);
            AssinaturaPdf.DesenharVisto(gfx, interior, analista.Info.Titular, emissao);
        }
        var forma = new PdfDictionary(doc);
        doc.Internals.AddObject(forma);
        forma.Elements.SetName("/Type", "/XObject");
        forma.Elements.SetName("/Subtype", "/Form");
        forma.Elements["/BBox"] = new PdfRectangle(new XRect(0, 0, geometria.Largura, geometria.Altura));
        forma.Elements["/Resources"] = camada.Resources;
        forma.CreateStream(camada.Contents.CreateSingleContent().Stream.UnfilteredValue);
        doc.Pages.RemoveAt(doc.PageCount - 1);
        var objetos = folha.Resources.Elements.GetDictionary("/XObject");
        if (objetos is null) folha.Resources.Elements["/XObject"] = objetos = new PdfDictionary(doc);
        var nome = "/Carimbo" + Guid.NewGuid().ToString("N");
        objetos.Elements[nome] = forma.Reference!;
        folha.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes($"q {geometria.Matriz()} cm {nome} Do Q"));
        var local = new LocalAssinatura(pagina, 0, 0, 0, 0);
        using var saida = new MemoryStream();
        doc.Save(saida);
        return AssinaturaPdf.AssinarNoLocal(saida.ToArray(), analista, emissao, null, local, "Visto do Analista Técnico", vistoNaPagina: true);
    }
}
