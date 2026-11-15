using Pmvni.Legislacao.Testes.Ficticios;
using Xunit;

namespace Pmvni.Legislacao.Testes;

public sealed class EsferasTestes
{
    [Fact]
    public void ordem_e_rotulos()
    {
        Assert.Equal(["Federal", "Estadual", "Municipal", "Outras"], Esferas.Ordem.Select(Esferas.Rotulo));
    }

    [Theory]
    [InlineData(@"C:\x\LEGISLAÇÃO\MUNICIPAL\PDM.pdf", Esfera.Municipal)]
    [InlineData(@"C:\x\ESTADUAL - CBMES (NORMAS TECNICAS)\NT 01.pdf", Esfera.Estadual)]
    [InlineData(@"C:\x\Lei Federal 6.766.pdf", Esfera.Federal)]
    [InlineData(@"C:\Downloads\lei.pdf", Esfera.Outras)]
    [InlineData(@"C:\x\FEDERAL\Lei municipal comparada.pdf", Esfera.Municipal)] // o nome do arquivo vem antes das pastas
    [InlineData(@"C:\x\Federal\Municipal\lei.pdf", Esfera.Municipal)] // pasta mais próxima antes da mais distante
    [InlineData(@"C:\x\CBMES\nt.pdf", Esfera.Estadual)]
    [InlineData("/x/Legislação/MUNICIPAL/Lei Orgânica.pdf", Esfera.Municipal)]
    public void esfera_sugerida_pelo_caminho(string caminho, Esfera esperada)
    {
        Assert.Equal(esperada, Esferas.Sugerir(caminho));
    }

    [Fact]
    public void esfera_do_catalogo_desconhecida_vira_outras()
    {
        Assert.Equal(Esfera.Outras, Esferas.DeRotulo("Distrital"));
        Assert.Equal(Esfera.Estadual, Esferas.DeRotulo("Estadual"));
    }
}
