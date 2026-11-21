using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace Pmvni.App;

internal static class InstalacaoMotorPdf
{
    static string BaseLocal => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PMVNI", "MotorPdf");

    /// <summary>O resumo que o <c>configurar-plugins.ps1</c> grava (uma linha por componente); null se ainda não existe.</summary>
    static string Resumo => Path.Combine(BaseLocal, "resumo-instalacao.txt");

    /// <summary>Extrai os scripts embutidos (<c>MotorPdf\*</c>) para <c>%LOCALAPPDATA%\PMVNI\MotorPdf\bootstrap\&lt;compilação&gt;</c> e devolve a pasta.
    /// O Editor não é empacotado (MSIX): a pasta é a real do usuário, não virtualizada.</summary>
    static async Task<string> ExtrairAsync()
    {
        var assembly = typeof(InstalacaoMotorPdf).Assembly;
        var pasta = Path.Combine(BaseLocal, "bootstrap", assembly.ManifestModule.ModuleVersionId.ToString());
        Directory.CreateDirectory(pasta);
        foreach (var nome in assembly.GetManifestResourceNames().Where(n => n.StartsWith("motor.", StringComparison.Ordinal)).Select(n => n[6..]))
        {
            var destino = Path.Combine(pasta, nome);
            if (File.Exists(destino)) continue;
            var temp = destino + "." + Guid.NewGuid();
            try
            {
                using var origem = assembly.GetManifestResourceStream("motor." + nome)!;
                using (var gravacao = File.Create(temp)) await origem.CopyToAsync(gravacao);
                try { File.Move(temp, destino); } catch (IOException) when (File.Exists(destino)) { }
            }
            finally { File.Delete(temp); }
        }
        return pasta;
    }

    internal static async Task<string> ConferirAsync()
    {
        try
        {
            var assembly = typeof(InstalacaoMotorPdf).Assembly;
            using var manifesto = assembly.GetManifestResourceStream("motor.requirements.lock")!;
            var versao = Convert.ToHexString(SHA256.HashData(manifesto))[..20];
            var pasta = await ExtrairAsync();
            var inicio = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(pasta, "configurar-plugins.ps1") })
                inicio.ArgumentList.Add(arg);
            var inicioUtc = DateTime.UtcNow;
            using var instalador = Process.Start(inicio)!;
            var erro = await instalador.StandardError.ReadToEndAsync();
            await instalador.WaitForExitAsync();
            if (File.Exists(Resumo) && File.GetLastWriteTimeUtc(Resumo) >= inicioUtc) return await File.ReadAllTextAsync(Resumo);
            if (instalador.ExitCode != 0)
                throw new IOException($"Instalação do motor PDF {versao}: {erro}");
            return "Instalação encerrada sem resumo.";
        }
        catch (Exception e) { RegistroErros.Gravar("motor.pdf.instalacao", e); return "Falha na instalação: " + e.Message; }
    }

    /// <summary>Tarefa 16: na abertura do Editor, instalação incompleta (pelo resumo) → <c>primeira-abertura.ps1</c> fora do Editor, por
    /// <c>.cmd</c> aberto com o <c>explorer.exe</c> (<see cref="PrimeiraAbertura"/>). Não espera o fim: o resumo novo vale na próxima abertura
    /// e no botão dos Ajustes. true = iniciou.</summary>
    internal static async Task<bool> PrimeiraAberturaAsync()
    {
        try
        {
            var resumo = File.Exists(Resumo) ? await File.ReadAllTextAsync(Resumo) : null;
            if (!PrimeiraAbertura.Incompleta(resumo)) return false;
            var pasta = await ExtrairAsync();
            return PrimeiraAbertura.Conferir(resumo, pasta, (programa, argumento) =>
            {
                var inicio = new ProcessStartInfo(programa) { UseShellExecute = false };
                inicio.ArgumentList.Add(argumento);
                using var _ = Process.Start(inicio);
            });
        }
        catch (Exception e) { RegistroErros.Gravar("motor.pdf.primeira-abertura", e); return false; }
    }
}
