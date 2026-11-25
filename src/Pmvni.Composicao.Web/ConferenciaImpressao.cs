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
        var divergencias = new List<string>();
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
                string Impresso(double margem) => Compactar(string.Concat(letras.Where(l =>
                    l.X >= trecho.X * folha.Width - margem && l.X <= trecho.Direita * folha.Width + margem &&
                    l.Y >= trecho.Y * folha.Height - margem && l.Y <= trecho.Base * folha.Height + margem)
                    .OrderBy(l => l.X).Select(l => l.Texto)));
                var observado = Impresso(0);
                if (observado != alvo && Impresso(1) != alvo)
                    divergencias.Add($"“{trecho.Texto}” em {trecho.Campo}, folha {i + 1} (lido “{observado}”)");
            }
        }
        if (divergencias.Count > 0)
            throw new InvalidDataException("O PDF perdeu ou deslocou " + string.Join(", ", divergencias.Distinct()) + ". O PDF anterior foi preservado.");
    }
}
