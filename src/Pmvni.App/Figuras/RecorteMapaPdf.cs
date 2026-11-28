using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Windows.Data.Pdf;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace Pmvni.App;

internal static class RecorteMapaPdf
{
    // Coordenadas proporcionais da região que contém o mapa e toda a legenda impressa.
    internal static async Task ExecutarAsync(string[] args)
    {
        if (args.Length != 7 || !int.TryParse(args[2], out var pagina) || pagina < 1)
            throw new ArgumentException("Uso: --recortar-mapa-pdf SHA PAGINA X Y LARGURA ALTURA (frações de 0 a 1).");
        var r = args[3..].Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        if (r.Any(v => !double.IsFinite(v) || v < 0) || r[2] <= 0 || r[3] <= 0 || r[0]+r[2] > 1.000001 || r[1]+r[3] > 1.000001)
            throw new ArgumentException("O recorte deve estar dentro da página.");
        var pecas = JsonNode.Parse(await File.ReadAllTextAsync("entrada-editor.json"))?["pecas"] as JsonArray;
        var fonte = pecas?.FirstOrDefault(p => string.Equals(p?["sha256"]?.ToString(), args[1], StringComparison.OrdinalIgnoreCase));
        var arquivo = fonte?["caminho"]?.ToString() ?? throw new InvalidDataException("PDF fora da entrada atual.");
        using var pdf = File.OpenRead(arquivo);
        var sha = Convert.ToHexStringLower(await SHA256.HashDataAsync(pdf));
        if (!string.Equals(sha, args[1], StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("O PDF mudou.");
        pdf.Position = 0;
        using var fluxo = pdf.AsRandomAccessStream();
        var documento = await PdfDocument.LoadFromStreamAsync(fluxo);
        if (pagina > documento.PageCount) throw new InvalidDataException("Página ausente no PDF.");
        using var folha = documento.GetPage((uint)pagina - 1);
        var tamanho = folha.Size;
        using var imagem = new InMemoryRandomAccessStream();
        await folha.RenderToStreamAsync(imagem, new PdfPageRenderOptions {
            BitmapEncoderId = BitmapEncoder.PngEncoderId,
            SourceRect = new Windows.Foundation.Rect(r[0]*tamanho.Width, r[1]*tamanho.Height, r[2]*tamanho.Width, r[3]*tamanho.Height),
            DestinationWidth = (uint)Math.Clamp(Math.Round(r[2]*tamanho.Width*2), 1, 2600) });
        imagem.Seek(0);
        using var dados = imagem.AsStreamForRead();
        using var bytes = new MemoryStream();
        await dados.CopyToAsync(bytes);
        var png = bytes.ToArray();
        var hash = Convert.ToHexStringLower(SHA256.HashData(png));
        var nome = $"mapa-pdf-{hash}.png";
        var recibo = JsonSerializer.Serialize(new { arquivo_png = nome, sha256_png = hash, sha256_pdf = sha, pagina, recorte = r });
        await GravarAsync(nome, png);
        await GravarAsync(nome + ".json", System.Text.Encoding.UTF8.GetBytes(recibo));
        Console.WriteLine(recibo);
    }

    static async Task GravarAsync(string destino, byte[] bytes)
    {
        var tmp = destino + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(tmp, bytes); File.Move(tmp, destino, true); }
        finally { File.Delete(tmp); }
    }
}
