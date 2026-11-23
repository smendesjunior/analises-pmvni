using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Agentes;

/// <summary>Uma execução por processo (desenho §12.7): <c>&lt;processo&gt;\.pmvni-ai\editor\execucao.lock</c> criada com <c>FileMode.CreateNew</c>, com o pid do
/// editor, o provedor e o início. Trava de um pid que não vive mais (ou que é de outro processo com o mesmo pid) é abandonada e substituída.</summary>
public sealed class TravaExecucao : IDisposable
{
    static readonly UTF8Encoding Utf8 = new(false);
    static readonly TimeSpan IlegivelAbandonadaDepoisDe = TimeSpan.FromSeconds(30);

    readonly string caminho;
    readonly string selo;
    int descartada;

    TravaExecucao(string caminho, string selo)
    {
        this.caminho = caminho;
        this.selo = selo;
    }

    static string CaminhoDa(string pastaProcesso) => Path.Combine(pastaProcesso, ".pmvni-ai", "editor", "execucao.lock");

    /// <summary>Pega a trava do processo, ou null se já há uma execução viva nele.</summary>
    public static TravaExecucao? Tentar(string pastaProcesso, Provedor p)
    {
        var caminho = CaminhoDa(pastaProcesso);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        var selo = Guid.NewGuid().ToString("N");
        var conteudo = new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["provedor"] = Provedores.Sufixo(p),
            ["inicio"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["selo"] = selo,
        }.ToJsonString();

        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            try
            {
                using (var fs = new FileStream(ArquivoGerado.Caminho(caminho), FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var w = new StreamWriter(fs, Utf8))
                    w.Write(conteudo);
                return new TravaExecucao(caminho, selo);
            }
            catch (IOException) when (File.Exists(caminho))
            {
                if (Viva(caminho)) return null;
                try { File.Delete(caminho); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
            }
        }
        return null;
    }

    /// <summary>Há uma execução viva neste processo (de qualquer provedor)?</summary>
    public static bool EmAndamento(string pastaProcesso)
    {
        var caminho = CaminhoDa(pastaProcesso);
        return File.Exists(caminho) && Viva(caminho);
    }

    /// <summary>A trava é de um processo que ainda vive? Arquivo ilegível só é abandonado depois de 30 s (pode estar sendo gravado agora).</summary>
    static bool Viva(string caminho)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(caminho));
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("pid", out var p) || !p.TryGetInt32(out var pid)) return Recente(caminho);
            using var processo = Process.GetProcessById(pid);
            if (processo.HasExited) return false;
            // Mesmo pid, processo mais novo que a trava: o pid foi reaproveitado.
            if (r.TryGetProperty("inicio", out var i) && i.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(i.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var inicio)
                && new DateTimeOffset(processo.StartTime) > inicio.AddSeconds(5))
                return false;
            return true;
        }
        catch (ArgumentException) { return false; }                // não há processo com esse pid
        catch (InvalidOperationException) { return false; }
        catch (JsonException) { return Recente(caminho); }
        catch (IOException) { return true; }                       // preso por quem a está gravando
        catch (UnauthorizedAccessException) { return true; }
        catch (System.ComponentModel.Win32Exception) { return true; }   // sem permissão para ver o processo: está vivo
    }

    static bool Recente(string caminho) => DateTime.UtcNow - File.GetLastWriteTimeUtc(caminho) < IlegivelAbandonadaDepoisDe;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref descartada, 1) == 1) return;
        try
        {
            // Só apaga a própria trava (o selo): uma trava que outra execução pegou depois não é desta.
            using var doc = JsonDocument.Parse(File.ReadAllText(caminho));
            if (doc.RootElement.TryGetProperty("selo", out var s) && s.GetString() == selo) File.Delete(caminho);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { /* já sumiu ou ficou ilegível: o abandono cuida */ }
    }
}
