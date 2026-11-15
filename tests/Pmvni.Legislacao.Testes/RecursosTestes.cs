using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Legislacao.Testes;

public sealed class RecursosTestes
{
    static string Fonte(string relativo) => Path.Combine(Ambiente.RaizRepositorio, relativo.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void pdfjs_vendorizado_e_o_da_versao()
    {
        Assert.Equal("6.4.299", RecursosLegislacao.VersaoPdfJs);
        var versao = JsonNode.Parse(Texto(RecursosLegislacao.Ler("pdfjs/VERSAO.json")!))!;
        Assert.Equal("pdfjs-dist", versao["pacote"]!.GetValue<string>());
        Assert.Equal("6.4.299", versao["versao"]!.GetValue<string>());
        Assert.Equal("Apache-2.0", versao["licenca"]!.GetValue<string>());
        Assert.Equal(283, versao["arquivos"]!.AsObject().Count);
        Assert.Equal(283, RecursosLegislacao.ShaPdfJs.Count);
        Assert.True(RecursosLegislacao.ConferirPdfJs(out var divergentes), string.Join("; ", divergentes));
        Assert.Empty(divergentes);
        Assert.Equal(4_754_750, RecursosLegislacao.BytesPdfJs);

        Assert.Contains("Apache License", Texto(RecursosLegislacao.Ler("pdfjs/LICENSE")!));
        Assert.NotNull(RecursosLegislacao.Ler("pdfjs/standard_fonts/LICENSE_FOXIT"));
        Assert.NotNull(RecursosLegislacao.Ler("pdfjs/standard_fonts/LICENSE_LIBERATION"));
        foreach (var licenca in new[] { "LICENSE_OPENJPEG", "LICENSE_JBIG2", "LICENSE_QCMS", "LICENSE_PDFJS_OPENJPEG", "LICENSE_PDFJS_JBIG2", "LICENSE_PDFJS_QCMS" })
            Assert.NotNull(RecursosLegislacao.Ler("pdfjs/wasm/" + licenca));

        var nomes = RecursosLegislacao.ShaPdfJs.Keys;
        Assert.DoesNotContain(nomes, n => n.Contains("quickjs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nomes, n => n.Contains("nowasm", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nomes, n => n.EndsWith(".map", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(nomes, n => n.StartsWith("web/viewer.", StringComparison.Ordinal));
        Assert.DoesNotContain(nomes, n => n.StartsWith("legacy/", StringComparison.Ordinal));
        foreach (var essencial in new[] { "build/pdf.min.mjs", "build/pdf.worker.min.mjs", "web/pdf_viewer.mjs", "web/pdf_viewer.css", "wasm/openjpeg.wasm", "wasm/jbig2.wasm", "wasm/qcms_bg.wasm" })
            Assert.Contains(essencial, nomes);
    }

    [Fact]
    public void embutido_e_o_mesmo_arquivo_do_disco()
    {
        // O recurso do executável é o arquivo versionado em src/Pmvni.Legislacao/web (sha do VERSAO.json = sha do disco).
        foreach (var (caminho, sha) in RecursosLegislacao.ShaPdfJs.Take(25))
        {
            var disco = File.ReadAllBytes(Fonte("src/Pmvni.Legislacao/web/pdfjs/" + caminho));
            Assert.Equal(sha, Convert.ToHexStringLower(SHA256.HashData(disco)));
        }
        Assert.True(RecursosLegislacao.Ler("extrator.html")!.AsSpan().SequenceEqual(File.ReadAllBytes(Fonte("src/Pmvni.Legislacao/web/extrator.html"))));
        Assert.True(RecursosLegislacao.Ler("extrator.js")!.AsSpan().SequenceEqual(File.ReadAllBytes(Fonte("src/Pmvni.Legislacao/web/extrator.js"))));
    }

    [Fact]
    public void recurso_inexistente_ou_fora_da_pasta_e_nulo()
    {
        Assert.Null(RecursosLegislacao.Ler("pdfjs/nao-existe.js"));
        Assert.Null(RecursosLegislacao.Ler("../Pmvni.Legislacao.dll"));
        Assert.Null(RecursosLegislacao.Ler(""));
    }

    [Fact]
    public void extrator_nao_depende_de_rede_nem_de_eval()
    {
        var js = Texto(RecursosLegislacao.Ler("extrator.js")!);
        Assert.DoesNotContain("eval(", js);
        Assert.DoesNotContain("new Function", js);
        Assert.DoesNotContain("http://", js);
        Assert.DoesNotContain("PDFScriptingManager", js);
        Assert.Contains("pdfjsLib", js);
    }

    [Fact]
    public void pacote_npm_fixado_sem_intervalo()
    {
        var pacote = JsonNode.Parse(File.ReadAllText(Fonte("web/paginador/package.json")))!;
        Assert.Equal("6.4.299", pacote["devDependencies"]!["pdfjs-dist"]!.GetValue<string>());

        var trava = JsonNode.Parse(File.ReadAllText(Fonte("web/paginador/package-lock.json")))!;
        var entrada = trava["packages"]!["node_modules/pdfjs-dist"]!;
        Assert.Equal("6.4.299", entrada["version"]!.GetValue<string>());
        Assert.StartsWith("sha512-", entrada["integrity"]!.GetValue<string>());
        // A raiz da árvore também registra a versão exata.
        Assert.Equal("6.4.299", trava["packages"]![""]!["devDependencies"]!["pdfjs-dist"]!.GetValue<string>());
    }

    static string Texto(byte[] b) => System.Text.Encoding.UTF8.GetString(b);
}
