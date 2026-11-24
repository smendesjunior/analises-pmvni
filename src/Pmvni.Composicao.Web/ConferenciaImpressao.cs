using System.Text;
using UglyToad.PdfPig;

namespace Pmvni.Composicao.Web;

internal sealed record TrechoImpresso(string Texto, string Campo, double X, double Y, double Direita, double Base);

internal static class ConferenciaImpressao
{
    static string Compactar(string texto) => string.Concat(texto.Normalize(NormalizationForm.FormKC)
        .Where(c => !char.IsWhiteSpace(c) && c != '\u00ad')).Replace('−', '-').ToUpperInvariant();

    internal static void Conferir(byte[] pdf, IReadOnlyList<TrechoImpresso[]> esperado)
    {
        using var documento = PdfDocument.Open(pdf);
        if (esperado.Count != documento.NumberOfPages || esperado.All(f => f.Length == 0))
            throw new InvalidDataException("As folhas impressas não correspondem aos campos da composição.");
        for (var i = 0; i < esperado.Count; i++)
        {
            var folha = documento.GetPage(i + 1);
            var letras = folha.Letters.Select(l => (Texto: l.Value,
                X: (l.BoundingBox.Left + l.BoundingBox.Right) / 2, Y: folha.Height - (l.BoundingBox.Top + l.BoundingBox.Bottom) / 2)).ToList();
            foreach (var trecho in esperado[i])
            {
                var alvo = Compactar(trecho.Texto);
                if (alvo.Length == 0) continue;
                // Coordenadas relativas à folha. Um ponto de tolerância cobre o arredondamento CSS/PDF.
                var tinta = letras.Where(l => l.X >= trecho.X * folha.Width - 1 && l.X <= trecho.Direita * folha.Width + 1
                    && l.Y >= trecho.Y * folha.Height - 1 && l.Y <= trecho.Base * folha.Height + 1).OrderBy(l => l.X);
                if (Compactar(string.Concat(tinta.Select(l => l.Texto))) != alvo)
                    throw new InvalidDataException($"O PDF perdeu ou deslocou “{trecho.Texto}” em {trecho.Campo}, folha {i + 1}. O PDF anterior foi preservado.");
            }
        }
    }
}
