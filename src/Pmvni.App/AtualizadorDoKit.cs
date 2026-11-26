using System.Security.Cryptography;


namespace Pmvni.App;

internal static class AtualizadorDoKit
{
    internal static string BaseLocal => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PMVNI", "AtualizadorKit");

    internal static async Task<string> ExtrairAsync(string? pastaBase = null)
    {
        var assembly = typeof(AtualizadorDoKit).Assembly;
        var pasta = Path.Combine(pastaBase ?? BaseLocal, "bootstrap", assembly.ManifestModule.ModuleVersionId.ToString());
        Directory.CreateDirectory(pasta);
        foreach (var nome in assembly.GetManifestResourceNames().Where(n => n.StartsWith("kit.", StringComparison.Ordinal)).Select(n => n[4..]))
        {
            var destino = Path.Combine(pasta, nome);
            using var origem = assembly.GetManifestResourceStream("kit." + nome)!;
            if (File.Exists(destino))
            {
                byte[] atual;
                using (var existente = File.Open(destino, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                    atual = await SHA256.HashDataAsync(existente);
                var esperado = await SHA256.HashDataAsync(origem);
                if (atual.SequenceEqual(esperado)) continue;
                origem.Position = 0;
            }
            var temp = destino + "." + Guid.NewGuid();
            try
            {
                using (var gravacao = File.Create(temp)) await origem.CopyToAsync(gravacao);
                File.Move(temp, destino, overwrite: true);
            }
            finally { File.Delete(temp); }
        }
        return pasta;
    }

}
