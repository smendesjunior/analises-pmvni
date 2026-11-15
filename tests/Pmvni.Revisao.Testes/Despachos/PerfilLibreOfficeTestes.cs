using System.Diagnostics;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Ruling T16-C4: um perfil do LibreOffice só, persistente (o app usa &lt;dados&gt;\lo). O LibreOffice real nunca roda em
/// <c>dotnet test</c>: um soffice fictício (.cmd) grava o PDF na pasta de saída que recebeu.</summary>
public sealed class PerfilLibreOfficeTestes
{
    /// <summary>Um "soffice" que pula os argumentos até <c>--outdir</c> e grava um PDF mínimo (≥ 1000 bytes, começando por %PDF-) lá.</summary>
    static string SofficeFicticio(string pasta)
    {
        var cmd = Path.Combine(pasta, "soffice-ficticio.cmd");
        File.WriteAllText(cmd, """
            @echo off
            :laco
            if "%~1"=="" exit /b 2
            if /i "%~1"=="--outdir" goto saida
            shift
            goto laco
            :saida
            (echo %%PDF-1.4& for /l %%i in (1,1,40) do echo 0123456789012345678901234567890123456789) > "%~2\copia.pdf"
            exit /b 0
            """.Replace("\n", "\r\n", StringComparison.Ordinal));
        return cmd;
    }

    [Fact]
    public void perfil_unico_persiste_entre_conversoes_e_nunca_e_apagado()
    {
        using var pasta = Ambiente.PastaDeTeste("perfil-lo");
        var raizPerfil = Path.Combine(pasta, "lo");
        var conv = new ConversorLibreOffice(SofficeFicticio(pasta), raizPerfil: raizPerfil);
        var docx = Path.Combine(pasta, "despacho.docx");
        File.WriteAllBytes(docx, [1, 2, 3]);

        var trabalho1 = Path.Combine(pasta, "t1");
        var r1 = conv.Converter(docx, trabalho1, CancellationToken.None);
        Assert.NotNull(r1.Pdf);
        Assert.True(Directory.Exists(raizPerfil));
        // O que o LibreOffice guarda no perfil (simulado) fica para a próxima conversão.
        Directory.CreateDirectory(Path.Combine(raizPerfil, "user"));
        File.WriteAllText(Path.Combine(raizPerfil, "user", "registrymodifications.xcu"), "perfil inicializado");

        var trabalho2 = Path.Combine(pasta, "t2");
        var r2 = conv.Converter(docx, trabalho2, CancellationToken.None);
        Assert.NotNull(r2.Pdf);
        Assert.Equal("perfil inicializado", File.ReadAllText(Path.Combine(raizPerfil, "user", "registrymodifications.xcu")));
        Assert.False(Directory.Exists(Path.Combine(trabalho2, "conv", "perfil"))); // nada de perfil novo na pasta de trabalho
        Assert.False(File.Exists(ConversorLibreOffice.ArquivoPid(raizPerfil))); // o pid sai quando a conversão termina
    }

    [Fact]
    public void orfao_que_nao_e_soffice_ou_ja_saiu_nao_e_encerrado_e_a_trava_velha_sai()
    {
        using var pasta = Ambiente.PastaDeTeste("perfil-orfao");
        var raizPerfil = Path.Combine(pasta, "lo");
        Directory.CreateDirectory(Path.Combine(raizPerfil, "user"));
        File.WriteAllText(Path.Combine(raizPerfil, "user", ".lock"), "trava de um soffice que caiu");
        // O pid registrado é o deste processo de teste (vivo, mas não é um soffice): nunca é encerrado.
        using var eu = Process.GetCurrentProcess();
        File.WriteAllText(ConversorLibreOffice.ArquivoPid(raizPerfil), eu.Id + ";" + eu.StartTime.ToUniversalTime().Ticks);

        ConversorLibreOffice.EncerrarOrfao(raizPerfil);

        Assert.False(eu.HasExited);
        Assert.False(File.Exists(ConversorLibreOffice.ArquivoPid(raizPerfil)));
        Assert.False(File.Exists(Path.Combine(raizPerfil, "user", ".lock")));
        ConversorLibreOffice.EncerrarEmCurso(); // sem conversão em curso: nada
    }
}
