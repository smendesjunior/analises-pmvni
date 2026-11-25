using System.Text.Json;
using Microsoft.Graphics.Imaging;
using Microsoft.Windows.AI;
using Microsoft.Windows.AI.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Pmvni.App;

internal static class OcrDaAnalise
{
    internal const string Comando = "--ocr-da-analise";

    internal static bool Disponivel()
    {
        try { return TextRecognizer.GetReadyState() == AIFeatureReadyState.Ready; }
        catch (Exception) { return false; } // A presença da projeção WinRT não garante suporte no dispositivo.
    }

    internal static async Task<int> Executar(string[] args)
    {
        if (args.Length != 2) return 2;
        if (!Disponivel()) return 3;
        try
        {
            using var stream = File.OpenRead(Path.GetFullPath(args[1])).AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            using var imagem = ImageBuffer.CreateForSoftwareBitmap(bitmap);
            using var leitor = await TextRecognizer.CreateAsync();
            var palavras = leitor.RecognizeTextFromImage(imagem).Lines.SelectMany(l => l.Words)
                .Where(p => p.MatchConfidence >= 0.67).Select(p =>
                {
                    var b = p.BoundingBox;
                    var pontos = new[] { b.TopLeft, b.TopRight, b.BottomLeft, b.BottomRight };
                    return new object[] { pontos.Min(x => x.X), pontos.Min(x => x.Y),
                        pontos.Max(x => x.X), pontos.Max(x => x.Y), p.Text };
                }).ToArray();
            Console.WriteLine(JsonSerializer.Serialize(palavras));
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.GetType().Name); return 1; }
    }
}
