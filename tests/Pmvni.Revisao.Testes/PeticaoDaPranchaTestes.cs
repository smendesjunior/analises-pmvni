using Xunit;

namespace Pmvni.Revisao.Testes;

public class PeticaoDaPranchaTestes
{
    [Theory]
    [InlineData(@"C:\externo\Prancha A.pdf", null)]
    [InlineData(@"C:\externo\PROC. 123 - 2026 - X\PROJETO\INICIAL\Folha 1.pdf", "123/2026")]
    [InlineData(@"C:\externo\321-2025 - X.pdf", "321/2025")]
    [InlineData(@"C:\externo\123-2026 - X\321-2025 - Y.pdf", null)]
    public void Usa_somente_a_identidade_da_prancha(string caminho, string? esperado) =>
        Assert.Equal(esperado, PeticaoDaPrancha.Identificar(caminho));
}
