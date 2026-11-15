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

    /// <summary>Um "soffice" que deixa um filho vivo segurando os canais de saída depois de sair (como um soffice.bin que não larga os pipes):
    /// o filho (um <c>ping</c> de uns 4 s) só grava <c>fim.txt</c> se chegar ao fim, então o arquivo ausente prova que a árvore foi encerrada.
    /// Com <paramref name="sucesso"/>, grava o PDF e sai com 0; sem, sai com 1 sem PDF.</summary>
    static string SofficeComFilhoPreso(string pasta, bool sucesso)
    {
        var cmd = Path.Combine(pasta, "soffice-preso.cmd");
        var fim = Path.Combine(pasta, "fim.txt");
        var pdf = sucesso ? """(echo %%PDF-1.4& for /l %%i in (1,1,40) do echo 0123456789012345678901234567890123456789) > "%~2\copia.pdf" """ : "rem sem pdf";
        File.WriteAllText(cmd, """
            @echo off
            :laco
            if "%~1"=="" exit /b 2
            if /i "%~1"=="--outdir" goto saida
            shift
            goto laco
            :saida
            start "" /b cmd /c "ping -n 5 127.0.0.1 & echo fim> "__FIM__""
            __PDF__
            exit /b __CODIGO__
            """.Replace("__FIM__", fim, StringComparison.Ordinal).Replace("__PDF__", pdf, StringComparison.Ordinal)
               .Replace("__CODIGO__", sucesso ? "0" : "1", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal));
        return cmd;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void filho_que_segura_a_saida_nao_prende_o_mutex_alem_do_limite_e_a_arvore_morre(bool sucesso)
    {
        using var pasta = Ambiente.PastaDeTeste("perfil-filho-preso");
        var conv = new ConversorLibreOffice(SofficeComFilhoPreso(pasta, sucesso), TimeSpan.FromSeconds(1), Path.Combine(pasta, "lo"));
        var docx = Path.Combine(pasta, "despacho.docx");
        File.WriteAllBytes(docx, [1, 2, 3]);

        var relogio = Stopwatch.StartNew();
        var r = conv.Converter(docx, Path.Combine(pasta, "t1"), CancellationToken.None);
        relogio.Stop();

        // O filho dura uns 4 s e segura a saída; sem o limite a espera só acabaria com ele.
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(3.5), "demorou " + relogio.Elapsed);
        if (sucesso) Assert.NotNull(r.Pdf); else Assert.Equal("CONVERSAO_FALHOU", r.Motivo);
        // A árvore foi encerrada: o filho nunca chega a gravar fim.txt (espera além dos ~4 s dele).
        Thread.Sleep(TimeSpan.FromSeconds(6));
        Assert.False(File.Exists(Path.Combine(pasta, "fim.txt")));

        // O mutex foi solto: outra conversão (com um soffice normal) roda de imediato.
        var outra = new ConversorLibreOffice(SofficeFicticio(pasta), TimeSpan.FromSeconds(30), Path.Combine(pasta, "lo"));
        Assert.NotNull(outra.Converter(docx, Path.Combine(pasta, "t2"), CancellationToken.None).Pdf);
    }

    [Fact]
    public void cancelar_com_o_filho_preso_encerra_a_arvore_e_solta_o_mutex()
    {
        using var pasta = Ambiente.PastaDeTeste("perfil-filho-cancela");
        var conv = new ConversorLibreOffice(SofficeComFilhoPreso(pasta, true), TimeSpan.FromMinutes(5), Path.Combine(pasta, "lo"));
        var docx = Path.Combine(pasta, "despacho.docx");
        File.WriteAllBytes(docx, [1, 2, 3]);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        var relogio = Stopwatch.StartNew();
        Assert.ThrowsAny<OperationCanceledException>(() => conv.Converter(docx, Path.Combine(pasta, "t1"), cts.Token));
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(3.5), "demorou " + relogio.Elapsed);
        Thread.Sleep(TimeSpan.FromSeconds(6));
        Assert.False(File.Exists(Path.Combine(pasta, "fim.txt")));
    }
}
