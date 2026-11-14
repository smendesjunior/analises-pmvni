using System.Text.Json;
using Pmvni.Composicao.Web;

namespace Pmvni.Verificador;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "autoteste-pdf") return await AutotestePdfAsync(args[1]);
        if (args.Length == 1 && args[0] == "autoteste-falhas") return await AutotesteFalhas.ExecutarAsync();
        Console.Error.WriteLine("Uso: Pmvni.Verificador autoteste-pdf <saida.pdf> | autoteste-falhas");
        return 2;
    }

    /// <summary>Imprime &lt;p&gt;ok&lt;/p&gt; num HostOculto via CDP Page.printToPDF.</summary>
    static async Task<int> AutotestePdfAsync(string saida)
    {
        var caminho = Path.GetFullPath(saida);
        using var host = await HostOculto.IniciarAsync(PastaDados.WebView2());
        var base64 = await host.ExecutarAsync(async webView =>
        {
            await HostOculto.NavegarParaHtmlAsync(webView, "<p>ok</p>");
            var parametros = JsonSerializer.Serialize(new { printBackground = true, preferCSSPageSize = true, paperWidth = 210 / 25.4, paperHeight = 297 / 25.4 });
            var resposta = await webView.CallDevToolsProtocolMethodAsync("Page.printToPDF", parametros);
            using var json = JsonDocument.Parse(resposta);
            return json.RootElement.GetProperty("data").GetString()!;
        });
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        await File.WriteAllBytesAsync(caminho, Convert.FromBase64String(base64));
        Console.WriteLine($"PDF gravado: {caminho} ({new FileInfo(caminho).Length} bytes)");
        return 0;
    }
}
