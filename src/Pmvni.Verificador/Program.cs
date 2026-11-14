using System.Text.Json;
using System.Text;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;

namespace Pmvni.Verificador;

internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "autoteste-pdf") return await AutotestePdfAsync(args[1]);
        if (args.Length == 1 && args[0] == "autoteste-falhas") return await AutotesteFalhas.ExecutarAsync();
        if (args.Length >= 3 && args[0] == "html") return GerarHtml(args[1..]);
        Console.Error.WriteLine("Uso: Pmvni.Verificador autoteste-pdf <saida.pdf> | autoteste-falhas | html <dados.json> <saida.html> [--instrucoes] [--rascunho]");
        return 2;
    }

    /// <summary>Gera o HTML do relatório (o que o gerador do kit entrega ao Edge) e o grava como o kit o grava:
    /// UTF-8 sem BOM e, no Windows, cada LF do texto vira CRLF (Path.write_text em modo texto).</summary>
    static int GerarHtml(string[] args)
    {
        var flags = args.Where(a => a.StartsWith("--", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
        var pos = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (pos.Count != 2 || flags.Any(f => f is not ("--instrucoes" or "--rascunho")))
        {
            Console.Error.WriteLine("Uso: Pmvni.Verificador html <dados.json> <saida.html> [--instrucoes] [--rascunho]");
            return 2;
        }
        var r = RelatorioRenderer.Renderizar(pos[0], new OpcoesRender(flags.Contains("--instrucoes"), flags.Contains("--rascunho")), ModeloOficial.Carregar());
        if (!r.Ok)
        {
            Console.Error.WriteLine("RELATORIO_DADOS_INVALIDOS"+Environment.NewLine+"- " + string.Join(Environment.NewLine+"- ", r.Erros));
            return 1;
        }
        var saida = Path.GetFullPath(pos[1]);
        Directory.CreateDirectory(Path.GetDirectoryName(saida)!);
        File.WriteAllBytes(saida, new UTF8Encoding(false).GetBytes(r.Html.Replace("\n", "\r\n", StringComparison.Ordinal)));
        Console.WriteLine("HTML gravado: " + saida);
        return 0;
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
