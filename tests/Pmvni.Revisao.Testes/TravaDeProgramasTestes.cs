using System.Diagnostics;
using Pmvni.Agentes;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>A trava é global: estes testes rodam sozinhos (nenhum outro teste inicia um soffice fictício enquanto a lista está ligada).</summary>
[CollectionDefinition("TravaDeProgramas", DisableParallelization = true)]
public sealed class ColecaoTravaDeProgramas;

[Collection("TravaDeProgramas")]
public class TravaDeProgramasTestes
{
    [Fact]
    public async Task SaidaDoPaiEncerraFilhoQueMantemCanalAberto()
    {
        if (!OperatingSystem.IsWindows()) return;
        var inicio = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true
        };
        inicio.ArgumentList.Add("-NoProfile");
        inicio.ArgumentList.Add("-Command");
        inicio.ArgumentList.Add("$null = [Console]::ReadLine(); $p = Start-Process ping.exe -ArgumentList '-t','127.0.0.1' -WindowStyle Hidden -PassThru; $p.Id; $null = [Console]::ReadLine()");
        using var pai = ProcessoInterno.Iniciar(inicio);
        await pai.StandardInput.WriteLineAsync("iniciar");
        var pid = int.Parse((await pai.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15)))!);
        using var filho = Process.GetProcessById(pid);
        try
        {
            pai.StandardInput.Close();
            await filho.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.True(pai.Processo.HasExited);
        }
        finally { if (!filho.HasExited) filho.Kill(true); }
    }

    [Fact]
    public void lista_aceita_a_pasta_e_o_programa_exato_e_recusa_o_resto()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-programas");
        var falso = Path.Combine(pasta, "agente-falso");
        var soffice = Path.Combine(pasta, "LibreOffice", "program", "soffice.com");
        TravaDeProgramas.Permitidos = [falso, soffice];
        try
        {
            TravaDeProgramas.Conferir(Path.Combine(falso, "AgenteFalso.exe"));
            TravaDeProgramas.Conferir(soffice);
            Assert.Throws<InvalidOperationException>(() => TravaDeProgramas.Conferir(Path.Combine(pasta, "LibreOffice", "program", "soffice.exe")));
            Assert.Throws<InvalidOperationException>(() => TravaDeProgramas.Conferir(falso + "-irmao\\x.exe"));
            Assert.Throws<InvalidOperationException>(() => TravaDeProgramas.Conferir(Path.Combine(Environment.SystemDirectory, "whoami.exe")));
            Assert.Throws<InvalidOperationException>(() => TravaDeProgramas.Conferir("claude.exe"));
        }
        finally { TravaDeProgramas.Permitidos = null; }
    }

    [Fact]
    public void libreoffice_passa_pela_trava_e_o_recusado_nao_parte()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-libreoffice");
        var soffice = Path.Combine(pasta, "nao-permitido", "soffice.com");
        Directory.CreateDirectory(Path.GetDirectoryName(soffice)!);
        File.WriteAllText(soffice, "");                // existe (o conversor fica "disponível"), mas nunca é iniciado
        var docx = Path.Combine(pasta, "despacho.docx");
        File.WriteAllBytes(docx, [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var vistos = new List<string>();
        TravaDeProgramas.Permitidos = [Path.Combine(pasta, "agente-falso")];
        TravaDeProgramas.AoIniciar = c => vistos.Add(c);
        try
        {
            var conversor = new ConversorLibreOffice(soffice, TimeSpan.FromSeconds(5), Path.Combine(pasta, "lo"));
            var e = Assert.Throws<InvalidOperationException>(() => conversor.Converter(docx, Path.Combine(pasta, "trabalho"), CancellationToken.None));
            Assert.StartsWith("Programa fora da pasta permitida: ", e.Message);
        }
        finally { TravaDeProgramas.Permitidos = null; TravaDeProgramas.AoIniciar = null; }
        Assert.Empty(vistos);
    }

    [Fact]
    public async Task partida_claude_respeita_a_lista_e_catalogo_gemini_desativado_nao_inicia_programa()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-catalogos");
        var falso = Path.Combine(pasta, "agente-falso");
        var claude = Path.Combine(pasta, "claude.exe");
        var agy = Path.Combine(pasta, "agy.exe");
        var permitidosAntes = TravaDeProgramas.Permitidos;
        var auditoriaAntes = TravaDeProgramas.AoIniciar;
        var vistos = new List<string>();
        ExecucaoProcesso.SomenteExecutaveisEm = falso;
        TravaDeProgramas.AoIniciar = vistos.Add;
        try
        {
            var erroClaude = Assert.Throws<InvalidOperationException>(() => TravaDeProgramas.Iniciar(new ProcessStartInfo(claude)));
            Assert.EndsWith(claude, erroClaude.Message);
            var pecas = new Pecas(null, null, Path.Combine(falso, "python.exe"), false, null, null, null, false, null, false)
                { GoogleRuntime = Path.Combine(pasta, "google_runtime.py"), Agy = agy };
            var (lista, motivo) = await ModelosDoGemini.LerAsync(pecas, PerfilGemini.ContaUltra, null, pasta, CancellationToken.None);
            Assert.Null(lista);
            Assert.Equal(Provedores.RotaDesativada, motivo);
            Assert.Empty(vistos);
        }
        finally
        {
            TravaDeProgramas.Permitidos = permitidosAntes;
            TravaDeProgramas.AoIniciar = auditoriaAntes;
        }
    }

    [Fact]
    public void a_auditoria_aceita_o_soffice_exato_e_as_copias_do_agente()
    {
        var falso = @"C:\repo\tests\saida\agente-falso";
        var soffice = @"C:\Program Files\LibreOffice\program\soffice.com";
        string[] permitidos = [falso, soffice];
        Assert.All(new[] { falso + @"\AgenteFalso.exe", falso + @"\claude.exe", falso + @"\agy.exe", soffice },
            caminho => Assert.True(TravaDeProgramas.Permitido(caminho, permitidos)));
        Assert.False(TravaDeProgramas.Permitido(@"C:\real\agy.exe", permitidos));
        Assert.False(TravaDeProgramas.Permitido(@"C:\real\claude.exe", permitidos));
    }
}
