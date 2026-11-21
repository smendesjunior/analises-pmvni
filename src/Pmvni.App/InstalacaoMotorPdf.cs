using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Pmvni.Revisao;

namespace Pmvni.App;

internal static class InstalacaoMotorPdf
{
    internal static string BaseLocal => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PMVNI", "MotorPdf");

    /// <summary>O resumo que o <c>configurar-plugins.ps1</c> grava (uma linha por componente); null se ainda não existe.</summary>
    static string Resumo => Path.Combine(BaseLocal, "resumo-instalacao.txt");

    /// <summary>Quando a primeira abertura foi iniciada pela última vez (data do arquivo): falha persistente tenta de novo a cada 6 h, não a cada início.</summary>
    static string Tentativa => Path.Combine(BaseLocal, "primeira-abertura.tentativa");

    /// <summary>Extrai os scripts embutidos (<c>MotorPdf\*</c>) para <c>%LOCALAPPDATA%\PMVNI\MotorPdf\bootstrap\&lt;compilação&gt;</c> e devolve a pasta.
    /// O Editor não é empacotado (MSIX): a pasta é a real do usuário, não virtualizada.</summary>
    internal static async Task<string> ExtrairAsync()
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

    /// <summary>Tarefa 21: SHA-256 do <c>mcp_pmvni.py</c> que este Editor traz (o cartão Plugins e ferramentas compara com o instalado).</summary>
    internal static string? HashDoServidorPdf()
    {
        using var s = typeof(InstalacaoMotorPdf).Assembly.GetManifestResourceStream("motor.mcp_pmvni.py");
        return s is null ? null : Convert.ToHexString(SHA256.HashData(s));
    }

    internal static async Task LimparAsync()
    {
        try
        {
            var pasta = await ExtrairAsync();
            var inicio = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true
            };
            foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(pasta, "limpar.ps1"), "-EditorAberto", Environment.ProcessPath ?? "" })
                inicio.ArgumentList.Add(arg);
            using var processo = TravaDeProgramas.Iniciar(inicio)!;
            await processo.WaitForExitAsync();
        }
        catch (Exception e) { RegistroErros.Gravar("motor.pdf.limpeza", e); }
    }

    internal static async Task<string> ConferirAsync(string kitLocal)
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
            inicio.ArgumentList.Add("-KitLocal");
            inicio.ArgumentList.Add(kitLocal);
            var inicioUtc = DateTime.UtcNow;
            using var instalador = ProcessoInterno.Iniciar(inicio);
            var erro = await instalador.StandardError.ReadToEndAsync();
            await instalador.Processo.WaitForExitAsync();
            if (File.Exists(Resumo) && File.GetLastWriteTimeUtc(Resumo) >= inicioUtc) return await File.ReadAllTextAsync(Resumo);
            if (instalador.Processo.ExitCode != 0)
                throw new IOException($"Instalação do motor PDF {versao}: {erro}");
            return "Instalação encerrada sem resumo.";
        }
        catch (Exception e) { RegistroErros.Gravar("motor.pdf.instalacao", e); return "Falha na instalação: " + e.Message; }
    }

    /// <summary>Tarefa 16: na abertura do Editor, instalação incompleta (pelo resumo) → <c>primeira-abertura.ps1</c> fora do Editor, por
    /// <c>.cmd</c> aberto com o <c>explorer.exe</c> (<see cref="PrimeiraAbertura"/>). Não espera o fim: o resumo novo vale na próxima abertura
    /// e no botão dos Ajustes. Com análise em andamento (<paramref name="analisesEmAndamento"/>), adia (revisão final I2). true = iniciou.</summary>
    internal static async Task<bool> PrimeiraAberturaAsync(IReadOnlyList<string> analisesEmAndamento)
    {
        try
        {
            var registro = PrimeiraAbertura.RegistroMaisRecente(BaseLocal);
            var resumo = File.Exists(registro) ? await File.ReadAllTextAsync(registro) : null;
            if (!PrimeiraAbertura.Incompleta(resumo)) return false;
            DateTime? ultima = File.Exists(Tentativa) ? File.GetLastWriteTimeUtc(Tentativa) : null;
            var agora = DateTime.UtcNow;
            var pasta = await ExtrairAsync();
            var iniciou = PrimeiraAbertura.Conferir(resumo, pasta, (programa, argumento) =>
            {
                var inicio = new ProcessStartInfo(programa) { UseShellExecute = false };
                inicio.ArgumentList.Add(argumento);
                using var _ = TravaDeProgramas.Iniciar(inicio);
            }, agora, ultima, analisesEmAndamento);
            if (iniciou) await File.WriteAllTextAsync(Tentativa, agora.ToString("o", System.Globalization.CultureInfo.InvariantCulture));
            return iniciou;
        }
        catch (Exception e) { RegistroErros.Gravar("motor.pdf.primeira-abertura", e); return false; }
    }
}
