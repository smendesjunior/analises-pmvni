using System.Runtime.CompilerServices;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Xunit;

// Um único WebView2 por vez: as classes de teste não rodam em paralelo.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Pmvni.Web.Testes;

/// <summary>Os testes rodam dentro do pacote MSIX do shell do Claude, que virtualiza o AppData: a pasta de dados vai para
/// tests\saida\dados (ignorada pelo git) pela variável PMVNI_DADOS_DIR, antes de qualquer uso de PastaDados.</summary>
internal static class Ambiente
{
    [ModuleInitializer]
    internal static void Inicializar()
    {
        Environment.SetEnvironmentVariable(PastaDados.VariavelAmbiente, Path.Combine(RaizRepositorio(), "tests", "saida", "dados"));
    }

    internal static string RaizRepositorio()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "EditorRelatoriosPMVNI.sln"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada a partir de " + AppContext.BaseDirectory);
    }

    /// <summary>Resultado de render mínimo (com &lt;head&gt;, como o do renderizador) para os testes.</summary>
    internal static ResultadoRender Resultado(string corpo, string cabecaExtra = "") =>
        new($"<!DOCTYPE html>\n<html lang=\"pt-BR\"><head><meta charset=\"utf-8\"><title>t</title>{cabecaExtra}</head><body>{corpo}</body></html>",
            Array.Empty<string>(), Array.Empty<RefElemento>());

    internal static readonly ConfigFolhas Cfg = new("data:,", "E", "P {n} DE {t}");
}
