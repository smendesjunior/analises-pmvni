using Xunit;

namespace Pmvni.Legislacao.Testes;

public sealed class VerificacaoPdfTestes
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(209_715_200, true)]
    [InlineData(209_715_201, false)]
    public void limite_de_tamanho(long n, bool ok) => Assert.Equal(ok, VerificacaoPdf.TamanhoPermitido(n));

    [Fact]
    public void assinatura_nos_primeiros_1024_bytes()
    {
        var antes = new byte[1019].Select(_ => (byte)' ').Concat("%PDF-"u8.ToArray()).ToArray();
        Assert.Equal(1024, antes.Length);
        Assert.True(VerificacaoPdf.TemAssinatura(antes));
        var depois = new byte[1024].Select(_ => (byte)' ').Concat("%PDF-"u8.ToArray()).ToArray();
        Assert.False(VerificacaoPdf.TemAssinatura(depois)); // só os primeiros 1.024 bytes entram
        Assert.False(VerificacaoPdf.TemAssinatura(depois.AsSpan(0, 1024)));
        Assert.True(VerificacaoPdf.TemAssinatura("lixo\n%PDF-1.7\n"u8));
        Assert.False(VerificacaoPdf.TemAssinatura("%PDF"u8));
    }

    [Fact]
    public void mensagens_exatas()
    {
        Assert.Equal("“a.pdf” não é um arquivo PDF.", MensagensAnexo.NaoPdf("a.pdf"));
        Assert.Equal("“a.pdf” está vazio.", MensagensAnexo.Vazio("a.pdf"));
        Assert.Equal("“grande.pdf” tem 200,3 MB; o limite é 200 MB por arquivo.", MensagensAnexo.Grande("grande.pdf", 210_000_000));
        Assert.Equal("“a.pdf” tem 201,0 MB; o limite é 200 MB por arquivo.", MensagensAnexo.Grande("a.pdf", 210_763_776));
        Assert.Equal("“cópia.pdf” já está anexado como “LEI 1”.", MensagensAnexo.Repetido("cópia.pdf", "LEI 1"));
        Assert.Equal("“s.pdf” está protegido por senha e não pode ser aberto pelo editor. Anexe uma cópia sem senha.", MensagensAnexo.Senha("s.pdf"));
        Assert.Equal("“c.pdf” não pôde ser lido como PDF (arquivo danificado ou incompleto).", MensagensAnexo.Invalido("c.pdf"));
        Assert.Equal("“x.pdf” não pôde ser lido: O arquivo está em uso.", MensagensAnexo.Ilegivel("x.pdf", "O arquivo está em uso."));
        Assert.Equal("“x.pdf” não pôde ser lido: acesso negado.", MensagensAnexo.Ilegivel("x.pdf", "acesso negado"));
        // O motivo vem do tipo da falha, em português (nunca o texto do sistema).
        Assert.Equal("“x.pdf” não pôde ser lido: acesso negado.", MensagensAnexo.Ilegivel("x.pdf", new UnauthorizedAccessException("Access to the path is denied.")));
        Assert.Equal("“x.pdf” não pôde ser lido: o arquivo não foi encontrado.", MensagensAnexo.Ilegivel("x.pdf", new FileNotFoundException("Could not find file")));
        Assert.Equal("“x.pdf” não pôde ser lido: o arquivo está em uso por outro programa.", MensagensAnexo.Ilegivel("x.pdf", new IOException("The process cannot access the file", unchecked((int)0x80070020))));
        Assert.Equal("“x.pdf” não pôde ser lido: erro de leitura do disco.", MensagensAnexo.Ilegivel("x.pdf", new IOException("boom")));
        Assert.Equal("“x.pdf” não pôde ser lido pelo leitor de PDF do editor.", MensagensAnexo.LeitorFalhou("x.pdf"));
        Assert.Equal("O arquivo escolhido é igual ao já anexado.", MensagensAnexo.Igual);
        Assert.Equal("O título não pode ficar vazio.", MensagensAnexo.TituloVazio);
        Assert.Equal("O título pode ter até 200 caracteres.", MensagensAnexo.TituloLongo);
        Assert.Equal("O catálogo da legislação estava danificado; foi usada a cópia anterior (catalogo.json.bak).", MensagensAnexo.Corrompido);
        Assert.Equal("O catálogo da legislação foi gravado por uma versão mais nova do editor; os anexos ficam só para leitura.", MensagensAnexo.SomenteLeitura);
        Assert.Equal("1 PDF sem registro no catálogo foi recuperado; confira o título e a esfera em Ajustes › Módulos › Legislação.", MensagensAnexo.Recuperados(1));
        Assert.Equal("2 PDFs sem registro no catálogo foram recuperados; confira o título e a esfera em Ajustes › Módulos › Legislação.", MensagensAnexo.Recuperados(2));
    }
}
