using Pmvni.Composicao.Web;
using Xunit;

namespace Pmvni.Web.Testes;

/// <summary>A cópia de desenvolvimento (o .exe em Aplicativo\ ao lado da solução) usa dados próprios; a cópia de uso não.</summary>
public class CopiaDesenvolvimentoTestes
{
    [Fact]
    public void exe_ao_lado_da_solucao_e_desenvolvimento()
    {
        var raiz = Directory.CreateTempSubdirectory("pmvni-dev").FullName;
        try
        {
            File.WriteAllText(Path.Combine(raiz, "EditorRelatoriosPMVNI.sln"), "");
            Directory.CreateDirectory(Path.Combine(raiz, "Aplicativo"));
            Assert.True(PastaDados.EhDesenvolvimento(Path.Combine(raiz, "Aplicativo", "Editor de Relatorios PMVNI.exe")));
            Assert.False(PastaDados.EhDesenvolvimento(Path.Combine(raiz, "Aplicativo", "sub", "Editor de Relatorios PMVNI.exe")));
            Assert.False(PastaDados.EhDesenvolvimento(Path.Combine(raiz, "Programs", "x", "Editor de Relatorios PMVNI.exe")));
            Assert.False(PastaDados.EhDesenvolvimento(null));
            Assert.False(PastaDados.EhDesenvolvimento(Path.Combine(raiz, "Aplicativo", "testhost.dll")));
        }
        finally { Directory.Delete(raiz, true); }
    }
}
