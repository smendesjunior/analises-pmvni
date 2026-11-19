using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace Pmvni.App;

internal static class InstalacaoMotorPdf
{
    internal static async Task<string> ConferirAsync()
    {
        try
        {
            var assembly = typeof(InstalacaoMotorPdf).Assembly;
            using var manifesto = assembly.GetManifestResourceStream("motor.requirements.lock")!;
            var versao = Convert.ToHexString(SHA256.HashData(manifesto))[..20];
            var baseLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PMVNI", "MotorPdf");
            var pasta = Path.Combine(baseLocal, "bootstrap", assembly.ManifestModule.ModuleVersionId.ToString());
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
            var resumo = Path.Combine(baseLocal, "resumo-instalacao.txt");
            if (File.Exists(resumo) && File.GetLastWriteTimeUtc(resumo) >= inicioUtc) return await File.ReadAllTextAsync(resumo);
            if (instalador.ExitCode != 0)
                throw new IOException($"Instalação do motor PDF {versao}: {erro}");
            return "Instalação encerrada sem resumo.";
        }
        catch (Exception e) { RegistroErros.Gravar("motor.pdf.instalacao", e); return "Falha na instalação: " + e.Message; }
    }
}
