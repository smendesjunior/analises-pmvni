using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Correções 2026-11-15 (interface geral): cabeçalho em duas linhas, selo de gravação, etiqueta de situação da lista de seções e
/// chave do ícone de tipo de arquivo.</summary>
public class InterfaceGeralTestes
{
    [Fact]
    public void processo_em_duas_linhas()
    {
        Assert.Equal(("Proc. 17098/2026", "KLENER DEMARTIM DERIZ"), TituloJanela.Linhas(" 17098/2026 ", " KLENER DEMARTIM DERIZ ", "x"));
        Assert.Equal(("Proc. 9001/2026", ""), TituloJanela.Linhas("9001/2026", null, "x"));
        Assert.Equal(("FULANO", ""), TituloJanela.Linhas("", "FULANO", "x"));
        Assert.Equal(("relatorio", ""), TituloJanela.Linhas(null, "  ", "relatorio"));
    }

    [Fact]
    public void selo_de_gravacao()
    {
        Assert.Equal(TomSelo.Sucesso, EstadoGravacaoTexto.Tom(EstadoGravacao.Salvo));
        Assert.Equal(TomSelo.Atencao, EstadoGravacaoTexto.Tom(EstadoGravacao.NaoSalvo));
        Assert.Equal(TomSelo.Erro, EstadoGravacaoTexto.Tom(EstadoGravacao.Conflito));
        Assert.Equal(TomSelo.Neutro, EstadoGravacaoTexto.Tom(EstadoGravacao.Salvando));
        Assert.Equal(TomSelo.Neutro, EstadoGravacaoTexto.Tom(EstadoGravacao.SomenteLeitura));
        Assert.Equal("\uE930", EstadoGravacaoTexto.Glifo(EstadoGravacao.Salvo)); // ✓ em círculo
        foreach (var e in Enum.GetValues<EstadoGravacao>()) Assert.Single(EstadoGravacaoTexto.Glifo(e));
    }

    [Theory]
    [InlineData("ATENDE", "ATENDE", "g")]
    [InlineData("NAO_ATENDE", "NÃO ATENDE", "r")]
    [InlineData("NAO_COMPROVADO", "FALTA COMPROVAR", "a")]
    [InlineData("DIVERGENCIA_INTERNA", "INFORMAÇÕES DIVERGENTES", "o")]
    public void etiqueta_da_situacao(string codigo, string texto, string classe)
    {
        Assert.Equal(texto, EtiquetasSituacao.Texto(codigo));
        Assert.Equal(classe, EtiquetasSituacao.Classe(codigo));
        Assert.NotNull(EtiquetasSituacao.Cores(codigo, false));
        Assert.NotNull(EtiquetasSituacao.Cores(codigo, true));
        Assert.Equal($"Situação: {texto}. Mudar a situação", EtiquetasSituacao.NomeAcessivel(codigo));
    }

    [Fact]
    public void etiqueta_tem_as_cores_do_kit_no_tema_claro()
    {
        // .pill.g e .pill.a do rev10/relatorio.css: fundo, texto, borda e ponto.
        Assert.Equal(new CoresEtiqueta(0xFFD3EFDD, 0xFF1E6E43, 0xFF8FD0A9, 0xFF2F9E62), EtiquetasSituacao.Cores("ATENDE", false));
        Assert.Equal(new CoresEtiqueta(0xFFFDEBB3, 0xFF7A5600, 0xFFEBC55A, 0xFFE0A800), EtiquetasSituacao.Cores("NAO_COMPROVADO", false));
        // No escuro, o fundo é translúcido (o fundo da lista aparece) e o texto, claro.
        var escuro = EtiquetasSituacao.Cores("ATENDE", true)!.Value;
        Assert.True(escuro.Fundo >> 24 < 0xFF);
        Assert.Equal(0xFFu, escuro.Texto >> 24);
    }

    [Fact]
    public void etiqueta_sem_situacao_nao_aparece()
    {
        Assert.Equal("", EtiquetasSituacao.Texto(null));
        Assert.Null(EtiquetasSituacao.Cores(null, false));
        Assert.Null(EtiquetasSituacao.Cores("XYZ", true));
        Assert.Equal("", EtiquetasSituacao.NomeAcessivel(null));
    }

    [Fact]
    public void chave_do_icone_de_arquivo()
    {
        Assert.Equal(".pdf", TiposArquivo.Chave(@"C:\P\RELATORIO_TECNICO_21536_2026.PDF"));
        Assert.Equal(".docx", TiposArquivo.Chave(@"C:\P\DESPACHO - HISTORICO af1df.docx"));
        Assert.Equal(".dwg", TiposArquivo.Chave(@"PROJETO\sub\planta.dwg"));
        Assert.Equal(TiposArquivo.Pasta, TiposArquivo.Chave(@"C:\P\PROJETO\"));
        Assert.Equal(TiposArquivo.Pasta, TiposArquivo.Chave("x", ehPasta: true));
        Assert.Equal("", TiposArquivo.Chave(@"C:\P\LEIAME"));
        Assert.Equal("", TiposArquivo.Chave(null));
        Assert.Equal("\uE8B7", TiposArquivo.GlifoReserva(TiposArquivo.Pasta));
        Assert.Equal("\uEB9F", TiposArquivo.GlifoReserva(".jpg"));
        Assert.Equal("\uE8A5", TiposArquivo.GlifoReserva(".xyz"));
    }

    [Fact]
    public void linha_de_titulo_nao_tem_icone()
    {
        Assert.Null(new LinhaDetalhe(TipoLinhaDetalhe.Titulo, "03/11/2026").CaminhoDoIcone);
        Assert.Null(new LinhaDetalhe(TipoLinhaDetalhe.Vazio, "Nenhum despacho.").CaminhoDoIcone);
        Assert.Equal(@"C:\a.pdf", new LinhaDetalhe(TipoLinhaDetalhe.Arquivo, "a.pdf", "1 KB", @"C:\a.pdf").CaminhoDoIcone);
    }
}
