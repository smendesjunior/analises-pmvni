using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Pmvni.Revisao;

public static class ImpressaoDespacho
{
    // PDFs antigos do Editor guardam a aparência só no widget. Para a via de papel,
    // usa o mesmo Form XObject da assinatura e mantém o arquivo assinado intacto.
    public static byte[]? PrepararCopia(byte[] original)
    {
        using var entrada = new MemoryStream(original, writable: false);
        using var doc = PdfReader.Open(entrada, PdfDocumentOpenMode.Modify);
        var campos = doc.Internals.Catalog.Elements.GetDictionary("/AcroForm")?.Elements.GetArray("/Fields");
        if (campos?.Elements.Count != 1) return null;
        using var leitura = UglyToad.PdfPig.PdfDocument.Open(original);
        for (int i = 0; i < doc.PageCount; i++)
        {
            var pagina = doc.Pages[i];
            var anotacoes = pagina.Elements.GetArray("/Annots");
            if (anotacoes is null) continue;
            for (int j = 0; j < anotacoes.Elements.Count; j++)
            {
                var campo = anotacoes.Elements.GetDictionary(j);
                if (campo?.Elements.GetName("/FT") != "/Sig") continue;
                var assinatura = campo.Elements.GetDictionary("/V");
                if (assinatura?.Elements.GetString("/Reason") != "Despacho da Secretaria Municipal de Obras") continue;
                var aparencia = campo.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
                if (aparencia?.Reference is null || aparencia.Elements.ContainsKey("/Matrix")) continue;
                var caixa = aparencia.Elements.GetRectangle("/BBox");
                var rect = campo.Elements.GetRectangle("/Rect");
                if (caixa.X1 != 0 || caixa.Y1 != 0 || caixa.Width <= 0 || caixa.Height <= 0
                    || rect.Width <= 0 || rect.Height <= 0) continue;
                // A versão nova já imprime a assinatura na página e preserva a verificação CMS.
                var palavras = leitura.GetPage(i + 1).GetWords().Where(p => p.BoundingBox.Left >= rect.X1 - 1
                    && p.BoundingBox.Right <= rect.X2 + 1 && p.BoundingBox.Bottom >= rect.Y1 - 1
                    && p.BoundingBox.Top <= rect.Y2 + 1);
                if (string.Join(" ", palavras.Select(p => p.Text)).Contains("Assinado de forma digital", StringComparison.Ordinal))
                    return null;

                var recursos = pagina.Resources;
                var objetos = recursos.Elements.GetDictionary("/XObject");
                if (objetos is null)
                {
                    objetos = new PdfDictionary(doc);
                    recursos.Elements["/XObject"] = objetos;
                }
                string nome = "/Visto" + Guid.NewGuid().ToString("N");
                objetos.Elements[nome] = aparencia.Reference;
                var comando = FormattableString.Invariant($"q {rect.Width / caixa.Width:0.########} 0 0 {rect.Height / caixa.Height:0.########} {rect.X1:0.########} {rect.Y1:0.########} cm {nome} Do Q");
                pagina.Contents.AppendContent().CreateStream(Encoding.ASCII.GetBytes(comando));
                anotacoes.Elements.RemoveAt(j);
                // Cópia derivada apenas para a impressão de despachos do Editor com um único campo.
                doc.Internals.Catalog.Elements.Remove("/AcroForm");
                using var saida = new MemoryStream();
                doc.Save(saida);
                return saida.ToArray();
            }
        }
        return null;
    }
}
