using System.Globalization;
using System.Text;

namespace Pmvni.Revisao.Testes;

/// <summary>PDF 1.4 sem compressão montado no teste: <paramref name="paginas"/> objetos <c>/Type /Page</c> (A4), <c>/Count</c> igual e um
/// <c>/BaseFont</c> por fonte. Passa de 1000 bytes (o mínimo que o conversor real aceita).</summary>
public static class PdfFicticio
{
    public static byte[] Criar(int paginas, params string[] fontes)
    {
        var sb = new StringBuilder("%PDF-1.4\n");
        var n = 3;
        var idsPaginas = Enumerable.Range(0, paginas).Select(_ => n++).ToList();
        var idsFontes = fontes.Select(_ => n++).ToList();
        sb.Append("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");
        sb.Append("2 0 obj\n<< /Type /Pages /Kids [").Append(string.Join(' ', idsPaginas.Select(i => i + " 0 R")))
          .Append(CultureInfo.InvariantCulture, $"] /Count {paginas} >>\nendobj\n");
        var recursos = string.Join(' ', idsFontes.Select((id, i) => $"/F{i} {id} 0 R"));
        foreach (var id in idsPaginas)
            sb.Append(CultureInfo.InvariantCulture, $"{id} 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Resources << /Font << {recursos} >> >> >>\nendobj\n");
        for (var i = 0; i < fontes.Length; i++)
            sb.Append(CultureInfo.InvariantCulture, $"{idsFontes[i]} 0 obj\n<< /Type /Font /Subtype /TrueType /BaseFont /{fontes[i]} >>\nendobj\n");
        sb.Append('%').Append('x', 1100).Append('\n');
        sb.Append("trailer\n<< /Root 1 0 R >>\n%%EOF\n");
        return Encoding.Latin1.GetBytes(sb.ToString());
    }
}
