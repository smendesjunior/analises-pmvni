using System.Text.Json;
using System.Text;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;

namespace Pmvni.Verificador;

internal static class Program
{
    /// <summary>Viewport dos hosts do Verificador: o mesmo do <see cref="Exportador"/> (o plano não depende dele).</summary>
    const int LarguraHostPx = 1024, AlturaHostPx = 1400;

    static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false); // mensagens com acento lidas pelo tools/fidelidade.py
        var a = Argumentos.Ler(args);
        if (a.Erro is not null)
        {
            Console.Error.WriteLine(a.Erro);
            Console.Error.WriteLine(Argumentos.Uso);
            return 2;
        }
        switch (a.Nome)
        {
            case "autoteste-pdf": return await AutotestePdfAsync(a.Saida!);
            case "autoteste-falhas": return await AutotesteFalhas.ExecutarAsync();
        }
        var modelo = ModeloOficial.Carregar();
        if (a.ViaPacote) return HtmlViaPacote(a, modelo);
        var r = RelatorioRenderer.Renderizar(a.Dados!, new OpcoesRender(a.Instrucoes, a.Rascunho), modelo);
        if (!r.Ok)
        {
            Console.Error.WriteLine("RELATORIO_DADOS_INVALIDOS" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", r.Erros));
            return 1;
        }
        var cfg = ConfigFolhas.PadraoDeDados(modelo, a.Dados!);
        var saida = Path.GetFullPath(a.Saida!);
        return a.Nome switch
        {
            "html" => GerarHtml(r, saida),
            "plano" => await PlanoAsync(r, cfg, saida),
            "pdf" => await PdfAsync(r, cfg, saida, a.Dados!, modelo, a.Rascunho || a.Instrucoes),
            "texto" => await TextoAsync(r, cfg, saida),
            "capturas" => await CapturasAsync(r, cfg, saida, a.Dpi),
            _ => 2,
        };
    }

    /// <summary>Grava o HTML do relatório (o que o gerador do kit entrega ao Edge) como o kit o grava:
    /// UTF-8 sem BOM e, no Windows, cada LF do texto vira CRLF (Path.write_text em modo texto).</summary>
    static int GerarHtml(ResultadoRender r, string saida)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(saida)!);
        File.WriteAllBytes(saida, new UTF8Encoding(false).GetBytes(r.Html.Replace("\n", "\r\n", StringComparison.Ordinal)));
        Console.WriteLine("HTML gravado: " + saida);
        return 0;
    }

    /// <summary>V5: o caminho que o editor percorre. Importa o dados.json num pacote, grava <c>&lt;saida&gt;.pmvrel</c>, relê, extrai os assets
    /// em <c>&lt;saida&gt;-assets\</c> e grava o HTML dos dados efetivos (o projeto relido, sem revisão nenhuma); tem de sair igual ao do kit.</summary>
    static int HtmlViaPacote(Argumentos a, ModeloCarregado modelo)
    {
        var saida = Path.GetFullPath(a.Saida!);
        var semExtensao = Path.Combine(Path.GetDirectoryName(saida)!, Path.GetFileNameWithoutExtension(saida));
        var caminhoPacote = semExtensao + ".pmvrel";
        var pastaAssets = semExtensao + "-assets";
        Directory.CreateDirectory(Path.GetDirectoryName(saida)!);

        ConteudoPacote conteudo;
        try
        {
            var bytes = File.ReadAllBytes(Path.GetFullPath(a.Dados!));
            conteudo = Importador.Importar(bytes, Path.GetFullPath(a.Dados!), modelo, ContextoUsuario.Atual());
        }
        catch (ImportacaoRecusadaException e)
        {
            Console.Error.WriteLine("RELATORIO_DADOS_INVALIDOS" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", e.Erros));
            return 1;
        }

        foreach (var antigo in new[] { caminhoPacote, caminhoPacote + ".bak" }) if (File.Exists(antigo)) File.Delete(antigo);
        if (Directory.Exists(pastaAssets)) Directory.Delete(pastaAssets, true);
        Pacote.Gravar(caminhoPacote, conteudo, null);
        var lido = Pacote.Ler(caminhoPacote).Conteudo;
        lido.ExtrairAssets(pastaAssets);

        var efetivos = DadosEfetivos.Gerar(lido.Projeto);
        var r = RelatorioRenderer.Renderizar(efetivos.Json, Path.Combine(pastaAssets, "dados.json"), new OpcoesRender(a.Instrucoes, a.Rascunho), modelo);
        if (!r.Ok)
        {
            Console.Error.WriteLine("RELATORIO_DADOS_INVALIDOS" + Environment.NewLine + "- " + string.Join(Environment.NewLine + "- ", r.Erros));
            return 1;
        }
        Console.WriteLine("Pacote gravado e relido: " + caminhoPacote + " (" + lido.Assets.Count + " imagens)");
        return GerarHtml(r, saida);
    }

    /// <summary>Compõe no host oculto (como a exportação) e devolve o resultado da composição e a versão do WebView2.</summary>
    static async Task<(ResultadoComposicao C, string WebView2)> ComporAsync(ResultadoRender r, ConfigFolhas cfg)
    {
        await using var host = await HostOculto.CriarAsync(LarguraHostPx, AlturaHostPx);
        await host.Documento.CarregarAsync(r, cfg);
        var c = await host.Documento.ComporDetalhadoAsync();
        var versao = await host.ExecutarAsync(core => Task.FromResult(core.Environment.BrowserVersionString));
        return (c, versao);
    }

    /// <summary>Plano de quebras (JSON do paginador) com o número de folhas e a versão do WebView2. Só posições, hashes e
    /// medidas: nada do texto do relatório.</summary>
    static async Task<int> PlanoAsync(ResultadoRender r, ConfigFolhas cfg, string saida)
    {
        var (c, versao) = await ComporAsync(r, cfg);
        var json = new StringBuilder();
        json.Append("{\"folhas\":").Append(c.Folhas)
            .Append(",\"webview2\":").Append(JsonSerializer.Serialize(versao))
            .Append(",\"plano\":").Append(c.Plano.ParaJson()).Append('}');
        Gravar(saida, json.ToString());
        Console.WriteLine($"Plano gravado: {saida} ({c.Folhas} folhas, escala {c.Plano.Escala:0.####}, {c.Plano.Estouros.Count} estouro(s))");
        return 0;
    }

    /// <summary>Exporta pelo <see cref="Exportador"/> (PDF + recibo, com <c>"rascunho"</c> = render com --rascunho ou --instrucoes;
    /// a suíte de fidelidade usa --rascunho, como o kit). Estouro: recusa, nada gravado, código 3.</summary>
    static async Task<int> PdfAsync(ResultadoRender r, ConfigFolhas cfg, string saida, string dados, ModeloCarregado modelo, bool rascunho)
    {
        try
        {
            var x = await Exportador.ExportarAsync(r, cfg, saida, dados, modelo, rascunho);
            Console.WriteLine($"PDF gravado: {x.CaminhoPdf} ({x.Folhas} folhas, {x.LarguraPaginaMm:0.###} x {x.AlturaPaginaMm:0.###} mm)");
            return 0;
        }
        catch (ExportacaoRecusadaException e)
        {
            Console.Error.WriteLine(e.Message);
            return 3;
        }
    }

    /// <summary>A tela: compõe num host (como a exportação) e aplica o plano num documento recém-carregado de outro host,
    /// sem compor (o caminho da tela do app), em mídia de tela e escala 1 (1 px CSS = 1 px da captura a 96 dpi).</summary>
    static async Task<(HostOculto Host, ResultadoComposicao C, string WebView2)> TelaAsync(ResultadoRender r, ConfigFolhas cfg)
    {
        var (c, versao) = await ComporAsync(r, cfg);
        var host = await HostOculto.CriarAsync(LarguraHostPx, AlturaHostPx);
        try
        {
            await host.CdpAsync("Emulation.setDeviceMetricsOverride", """{"width":0,"height":0,"deviceScaleFactor":1,"mobile":false}""");
            await host.Documento.CarregarAsync(r, cfg);
            var ap = await host.Documento.AplicarPlanoAsync(c.Plano);
            if (ap.Folhas != c.Folhas) throw new InvalidDataException($"A tela montou {ap.Folhas} folhas para {c.Folhas} do plano.");
            return (host, c, versao);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    /// <summary>Texto da área útil de cada folha da tela (espaços normalizados), em JSON {folhas, webview2, textos}.</summary>
    static async Task<int> TextoAsync(ResultadoRender r, ConfigFolhas cfg, string saida)
    {
        var (host, c, versao) = await TelaAsync(r, cfg);
        await using (host)
        {
            var textos = await host.Documento.TextoPorFolhaAsync();
            Gravar(saida, JsonSerializer.Serialize(new { folhas = c.Folhas, webview2 = versao, textos }));
            Console.WriteLine($"Texto gravado: {saida} ({textos.Count} folhas)");
            return 0;
        }
    }

    /// <summary>PNG de cada folha da tela em <paramref name="dpi"/>: <c>&lt;pasta&gt;\folha-001.png</c>, …</summary>
    static async Task<int> CapturasAsync(ResultadoRender r, ConfigFolhas cfg, string pasta, int dpi)
    {
        var (host, c, _) = await TelaAsync(r, cfg);
        await using (host)
        {
            Directory.CreateDirectory(pasta);
            for (var i = 1; i <= c.Folhas; i++)
                await File.WriteAllBytesAsync(Path.Combine(pasta, $"folha-{i:000}.png"), await host.Documento.CapturarFolhaAsync(i, dpi));
            Console.WriteLine($"Capturas gravadas: {pasta} ({c.Folhas} folhas, {dpi} dpi)");
            return 0;
        }
    }

    static void Gravar(string caminho, string texto)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, texto, new UTF8Encoding(false));
    }

    /// <summary>Imprime &lt;p&gt;ok&lt;/p&gt; num HostOculto via CDP Page.printToPDF.</summary>
    static async Task<int> AutotestePdfAsync(string saida)
    {
        var caminho = Path.GetFullPath(saida);
        await using var host = await HostOculto.IniciarAsync(PastaDados.WebView2());
        await host.Documento.CarregarAsync(AutotesteFalhas.DocumentoSimples("<p>ok</p>"), new ConfigFolhas("data:,", "", ""));
        var parametros = JsonSerializer.Serialize(new { printBackground = true, preferCSSPageSize = true, paperWidth = 210 / 25.4, paperHeight = 297 / 25.4 });
        var resposta = await host.CdpAsync("Page.printToPDF", parametros);
        string base64;
        using (var json = JsonDocument.Parse(resposta)) base64 = json.RootElement.GetProperty("data").GetString()!;
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        await File.WriteAllBytesAsync(caminho, Convert.FromBase64String(base64));
        Console.WriteLine($"PDF gravado: {caminho} ({new FileInfo(caminho).Length} bytes)");
        return 0;
    }
}
