using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace Pmvni.App;

internal static class InstalacaoMotorPdf
{
    internal static async Task ConferirAsync()
    {
        try
        {
            var assembly = typeof(InstalacaoMotorPdf).Assembly;
            using var manifesto = assembly.GetManifestResourceStream("motor.requirements.lock")!;
            var versao = Convert.ToHexString(SHA256.HashData(manifesto))[..20];
            var baseLocal = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PMVNI", "MotorPdf");
            var pasta = Path.Combine(baseLocal, "bootstrap", assembly.ManifestModule.ModuleVersionId.ToString());
            Directory.CreateDirectory(pasta);
            foreach (var nome in new[] { "uv.json", "requirements.lock", "instalar.ps1", "primeira-abertura.ps1" })
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
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", Path.Combine(pasta, "primeira-abertura.ps1") })
                inicio.ArgumentList.Add(arg);
            using var instalador = Process.Start(inicio)!;
            var erro = await instalador.StandardError.ReadToEndAsync();
            await instalador.WaitForExitAsync();
            if (instalador.ExitCode != 0)
                throw new IOException($"Instalação do motor PDF {versao}: {erro}");
        }
        catch (Exception e) { RegistroErros.Gravar("motor.pdf.instalacao", e); }
    }
}
