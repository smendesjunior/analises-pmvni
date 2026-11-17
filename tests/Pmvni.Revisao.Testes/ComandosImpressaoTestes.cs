using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Imprimir (2026-11-17): quando o comando fica habilitado em cada módulo e, quando não fica, o motivo da dica.</summary>
public class ComandosImpressaoTestes
{
    [Theory]
    [InlineData(@"C:\p\PROJETO\planta.pdf", ModoImpressao.Visualizador)]
    [InlineData(@"C:\p\foto.JPG", ModoImpressao.Visualizador)]
    [InlineData(@"C:\p\foto.png", ModoImpressao.Visualizador)]
    [InlineData(@"C:\p\leia.txt", ModoImpressao.Visualizador)]
    [InlineData(@"C:\p\DESPACHO.docx", ModoImpressao.ProgramaDoWindows)]
    [InlineData(@"C:\p\quadro.xlsx", ModoImpressao.ProgramaDoWindows)]
    [InlineData(@"C:\p\scan.tif", ModoImpressao.ProgramaDoWindows)]
    [InlineData(@"C:\p\planta.dwg", ModoImpressao.Nenhum)]
    [InlineData(@"C:\p\anexos.zip", ModoImpressao.Nenhum)]
    [InlineData(@"C:\p\pagina.html", ModoImpressao.Nenhum)]
    [InlineData(@"C:\p\programa.exe", ModoImpressao.Nenhum)]
    [InlineData(@"C:\p\sem_extensao", ModoImpressao.Nenhum)]
    [InlineData("", ModoImpressao.Nenhum)]
    [InlineData(null, ModoImpressao.Nenhum)]
    public void modo_pela_extensao(string? caminho, ModoImpressao esperado) =>
        Assert.Equal(esperado, ComandosImpressao.ModoDoArquivo(caminho));

    [Fact]
    public void relatorio_imprime_quando_exporta()
    {
        Assert.Null(ComandosImpressao.MotivoRelatorio(pronto: true, temRelatorio: true, novoRelatorioAberto: false, podeExportar: true, null));
        Assert.Equal(ComandosImpressao.SemRelatorio, ComandosImpressao.MotivoRelatorio(true, false, false, false, null));
        Assert.Equal(ComandosImpressao.Ocupado, ComandosImpressao.MotivoRelatorio(false, false, false, false, null));
        Assert.Equal(ComandosImpressao.NovoRelatorioAberto, ComandosImpressao.MotivoRelatorio(true, true, true, true, null));
        // Bloqueada a exportação, bloqueada a impressão (o PDF é o mesmo), com o motivo dela.
        var desatualizada = ComandosImpressao.MotivoRelatorio(true, true, false, false, "o relatório mudou.");
        Assert.NotNull(desatualizada);
        Assert.Contains("o relatório mudou.", desatualizada);
        Assert.Contains("Conferência", ComandosImpressao.MotivoRelatorio(true, true, false, false, null));
    }

    [Fact]
    public void legislacao_e_sites()
    {
        Assert.Null(ComandosImpressao.MotivoLegislacao(true));
        Assert.Equal(ComandosImpressao.SemLei, ComandosImpressao.MotivoLegislacao(false));
        Assert.Null(ComandosImpressao.MotivoSite(true));
        Assert.Equal(ComandosImpressao.SemSite, ComandosImpressao.MotivoSite(false));
    }

    [Fact]
    public void processos_pelo_arquivo_escolhido()
    {
        Assert.Equal(ComandosImpressao.SemArquivo, ComandosImpressao.MotivoProcessos(null, null));
        Assert.Equal(ComandosImpressao.SemArquivo, ComandosImpressao.MotivoProcessos(TipoLinhaDetalhe.Titulo, @"C:\p"));
        Assert.Equal(ComandosImpressao.RelatorioNaLista, ComandosImpressao.MotivoProcessos(TipoLinhaDetalhe.Relatorio, @"C:\p\r.rpmvni"));
        Assert.Equal(ComandosImpressao.RelatorioNaLista, ComandosImpressao.MotivoProcessos(TipoLinhaDetalhe.Analise, @"C:\p\dados.json"));
        Assert.Null(ComandosImpressao.MotivoProcessos(TipoLinhaDetalhe.Arquivo, @"C:\p\planta.pdf"));
        Assert.Null(ComandosImpressao.MotivoProcessos(TipoLinhaDetalhe.ArquivoDeDespacho, @"C:\p\DESPACHO.docx"));
        Assert.Equal(ComandosImpressao.TipoSemImpressao, ComandosImpressao.MotivoProcessos(TipoLinhaDetalhe.Arquivo, @"C:\p\planta.dwg"));
    }

    [Fact]
    public void despachos_pelo_pdf()
    {
        Assert.Null(ComandosImpressao.MotivoDespachos(compondo: false, @"C:\p\DESPACHO.pdf"));
        Assert.Equal(ComandosImpressao.SemDespacho, ComandosImpressao.MotivoDespachos(false, null));
        Assert.Equal(ComandosImpressao.SemDespacho, ComandosImpressao.MotivoDespachos(false, " "));
        Assert.Equal(ComandosImpressao.DespachoEmEdicao, ComandosImpressao.MotivoDespachos(true, @"C:\p\DESPACHO.pdf"));
    }

    [Fact]
    public void todo_motivo_e_uma_frase()
    {
        foreach (var m in new[]
        {
            ComandosImpressao.SemRelatorio, ComandosImpressao.Ocupado, ComandosImpressao.NovoRelatorioAberto, ComandosImpressao.SemLei,
            ComandosImpressao.SemSite, ComandosImpressao.SemArquivo, ComandosImpressao.RelatorioNaLista, ComandosImpressao.TipoSemImpressao,
            ComandosImpressao.SemDespacho, ComandosImpressao.DespachoEmEdicao,
        })
        {
            Assert.True(char.IsUpper(m[0]), m);
            Assert.EndsWith(".", m);
        }
    }
}
