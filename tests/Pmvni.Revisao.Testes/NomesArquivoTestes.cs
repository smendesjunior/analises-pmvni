using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Nomes de arquivo do app (Tarefa 8): importação, número do processo da capa, nome livre sem sobrescrever e cópia de conflito.</summary>
public class NomesArquivoTestes
{
    [Fact]
    public void nomes_de_arquivo()
    {
        Assert.Equal("RELATORIO_98765_2099.RPMVNI", NomesArquivo.Importacao(98765, 2099));
        Assert.Equal("RELATORIO_0001_2099.RPMVNI", NomesArquivo.Importacao(1, 2099));
        Assert.Equal("RELATORIO.RPMVNI", NomesArquivo.Importacao(null, 2026));
        Assert.Equal("RELATORIO.RPMVNI", NomesArquivo.Importacao(null, null));

        Assert.Equal((9001, 2026), NomesArquivo.DoProcessoDaCapa("9001/2026"));
        Assert.Equal((98765, 2099), NomesArquivo.DoProcessoDaCapa(" 98765 / 2099 "));
        Assert.Equal(((int?)null, (int?)null), NomesArquivo.DoProcessoDaCapa(null));
        Assert.Equal(((int?)null, (int?)null), NomesArquivo.DoProcessoDaCapa("[Número do processo]"));

        using var pasta = Ambiente.PastaDeTeste("nomes");
        Assert.Equal(Path.Combine(pasta, "RELATORIO_9001_2026.RPMVNI"), NomesArquivo.Livre(pasta, "Relatorio_9001_2026.rpmvni"));
        File.WriteAllText(Path.Combine(pasta, "Relatorio_9001_2026.rpmvni"), "");
        File.WriteAllText(Path.Combine(pasta, "Relatorio_9001_2026_2.rpmvni"), "");
        Assert.Equal(Path.Combine(pasta, "RELATORIO_9001_2026_3.RPMVNI"), NomesArquivo.Livre(pasta, "Relatorio_9001_2026.rpmvni"));

        var pacote = Path.Combine(pasta, "Relatorio_9001_2026.rpmvni");
        Assert.Equal(Path.Combine(pasta, "RELATORIO_9001_2026 (CÓPIA 2026-11-14 14-32).RPMVNI"),
            NomesArquivo.CopiaDeConflito(pacote, new DateTime(2026, 11, 14, 14, 32, 59)));
        File.WriteAllText(Path.Combine(pasta, "Relatorio_9001_2026 (cópia 2026-11-14 14-32).rpmvni"), "");
        Assert.Equal(Path.Combine(pasta, "RELATORIO_9001_2026 (CÓPIA 2026-11-14 14-32)_2.RPMVNI"),
            NomesArquivo.CopiaDeConflito(pacote, new DateTime(2026, 11, 14, 14, 32, 0)));
    }
}
