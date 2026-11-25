using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Agentes;

/// <summary>Exclusão mútua por processo nesta estação. Respeita travas legadas ativas e registra pid, provedor e início em AppData.</summary>
public sealed class TravaExecucao : IDisposable
{
    static readonly UTF8Encoding Utf8 = new(false);
    static readonly TimeSpan IlegivelAbandonadaDepoisDe = TimeSpan.FromSeconds(30);

    readonly string caminho;
    readonly string selo;
    readonly FileStream arquivo;
    int descartada;

    TravaExecucao(string caminho, string selo, FileStream arquivo)
    {
        this.caminho = caminho;
        this.selo = selo;
        this.arquivo = arquivo;
    }

    static string CaminhoDa(string pastaProcesso)
    {
        var caminho = Path.Combine(ControlesDoProcesso.Diretorio(pastaProcesso, "editor"), "execucao.lock");
        AreaAnaliseLocal.ConferirCaminho(caminho, local: true);
        return caminho;
    }
    static bool LegadaViva(string processo)
    {
        var antiga = Path.Combine(ControlesDoProcesso.Legado(processo, "editor"), "execucao.lock");
        return File.Exists(antiga) && Viva(antiga);
    }

    /// <summary>Pega a trava do processo, ou null se já há uma execução viva nele.</summary>
    public static TravaExecucao? Tentar(string pastaProcesso, Provedor p)
    {
        if (LegadaViva(pastaProcesso)) return null;
        var caminho = CaminhoDa(pastaProcesso);
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        var selo = Guid.NewGuid().ToString("N");
        var conteudo = new JsonObject
        {
            ["pid"] = Environment.ProcessId,
            ["maquina"] = Environment.MachineName,
            ["handle_compartilhado"] = true,
            ["provedor"] = Provedores.Sufixo(p),
            ["inicio"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["selo"] = selo,
        }.ToJsonString();

        for (var tentativa = 0; tentativa < 2; tentativa++)
        {
            try
            {
                using var fs = new FileStream(ArquivoGerado.Caminho(caminho), FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                fs.Write(Utf8.GetBytes(conteudo));
                fs.Flush();
                var guarda = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                return new TravaExecucao(caminho, selo, guarda);
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
        return File.Exists(caminho) && Viva(caminho) || LegadaViva(pastaProcesso);
    }

    /// <summary>A trava é de um processo que ainda vive? Arquivo ilegível só é abandonado depois de 30 s (pode estar sendo gravado agora).</summary>
    static bool Viva(string caminho)
    {
        try
        {
            using var doc = LerTrava(caminho);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object || !r.TryGetProperty("pid", out var p) || !p.TryGetInt32(out var pid)) return Recente(caminho);
            if (r.TryGetProperty("maquina", out var maquina) && maquina.ValueKind == JsonValueKind.String
                && !string.Equals(maquina.GetString(), Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                if (!r.TryGetProperty("handle_compartilhado", out var h) || h.ValueKind != JsonValueKind.True || !EmVolumeDeRede(caminho)) return true;
                // O servidor SMB só concede a abertura exclusiva depois que o dono liberou o arquivo.
                using var livre = new FileStream(caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                return false;
            }
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

    internal static bool EmVolumeDeRede(string caminho)
    {
        var raiz = Path.GetPathRoot(Path.GetFullPath(caminho))!;
        return raiz.StartsWith(@"\\", StringComparison.Ordinal) || new DriveInfo(raiz).DriveType == DriveType.Network;
    }

    static JsonDocument LerTrava(string caminho)
    {
        using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonDocument.Parse(fs);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref descartada, 1) == 1) return;
        arquivo.Dispose();
        try
        {
            // Só apaga a própria trava (o selo): uma trava que outra execução pegou depois não é desta.
            using var doc = JsonDocument.Parse(File.ReadAllText(caminho));
            if (doc.RootElement.TryGetProperty("selo", out var s) && s.GetString() == selo) File.Delete(caminho);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { /* já sumiu ou ficou ilegível: o abandono cuida */ }
    }
}
