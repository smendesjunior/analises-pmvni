using System.Security.Cryptography;
using System.Text;

namespace Pmvni.Agentes;

/// <summary>Metadados permanentes do Editor nesta estação. A área de execução do kit continua independente por sessão.</summary>
internal static class ControlesDoProcesso
{
    internal static Func<string, string>? BaseDeTeste { get; set; }

    internal static string Raiz(string processo)
    {
        var baseLocal = BaseDeTeste?.Invoke(processo) ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(baseLocal) || !Path.IsPathFullyQualified(baseLocal) ||
            TravaExecucao.EmVolumeDeRede(baseLocal) || AreaAnaliseLocal.EmNuvem(baseLocal))
            throw new IOException("AppData local indisponível para os controles da análise.");
        var chave = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(AreaAnaliseLocal.Identidade(processo))));
        var raiz = Path.Combine(baseLocal, "PMVNI", "Controles", chave);
        AreaAnaliseLocal.ConferirCaminho(raiz, local: true);
        return raiz;
    }

    internal static string Diretorio(string processo, string categoria) => Path.Combine(Raiz(processo), categoria);
    internal static string RaizArquivada(string processo) => Path.Combine(Raiz(processo), "legado", ".pmvni-ai");

    internal static string RaizLegada(string processo)
    {
        var antiga = Path.Combine(processo, ".pmvni-ai");
        if (Directory.Exists(antiga)) return antiga;
        var arquivo = RaizArquivada(processo);
        AreaAnaliseLocal.ConferirCaminho(arquivo, local: true);
        return Directory.Exists(arquivo) ? arquivo : antiga;
    }

    internal static string Legado(string processo, string categoria) => Path.Combine(RaizLegada(processo), categoria);
    internal static string Execucao(string processo, string categoria, string id, bool gravar = false)
    {
        if (string.IsNullOrEmpty(id) || id is "." or ".." || id != Path.GetFileName(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new IOException("Identificador de execução inválido.");
        var local = Path.Combine(Diretorio(processo, categoria), id);
        AreaAnaliseLocal.ConferirCaminho(local, local: true);
        if (Directory.Exists(local)) return local;
        var legado = Path.Combine(Legado(processo, categoria), id);
        AreaAnaliseLocal.ConferirCaminho(legado, local: false);
        if (!Directory.Exists(legado)) return local;
        if (!gravar) return legado;

        // A importação só se torna visível completa. A origem continua legível por versões anteriores.
        var pai = Directory.CreateDirectory(Path.GetDirectoryName(local)!).FullName;
        var lote = Path.Combine(pai, ".importando-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(lote);
        try
        {
            Copiar(legado, lote);
            try { Directory.Move(lote, local); }
            catch (IOException) when (Directory.Exists(local)) { }
        }
        finally { if (Directory.Exists(lote)) Directory.Delete(lote, true); }
        return local;
    }

    static void Copiar(string origem, string destino)
    {
        foreach (var item in new DirectoryInfo(origem).EnumerateFileSystemInfos())
        {
            if (item.LinkTarget is not null) throw new IOException("Link ou junção no controle anterior: " + item.FullName);
            var caminho = Path.Combine(destino, item.Name);
            if (item is DirectoryInfo)
            {
                Directory.CreateDirectory(caminho);
                Copiar(item.FullName, caminho);
            }
            else
            {
                using (var entrada = new FileStream(item.FullName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
                using (var saida = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    entrada.CopyTo(saida);
                    saida.Flush(true);
                }
                File.SetLastWriteTimeUtc(caminho, item.LastWriteTimeUtc);
            }
        }
    }

    internal static string RegistroAtual(string processo, string registro)
    {
        if (RegistroExecucao.MotivoInvalido(processo, registro) is not null) return registro;
        var local = Path.Combine(Diretorio(processo, "editor"), Path.GetFileName(Path.TrimEndingDirectorySeparator(registro)));
        if (Directory.Exists(local) && RegistroExecucao.MotivoInvalido(processo, local) is null) return local;
        var anterior = Path.Combine(Legado(processo, "editor"), Path.GetFileName(Path.TrimEndingDirectorySeparator(registro)));
        return Directory.Exists(anterior) && RegistroExecucao.MotivoInvalido(processo, anterior) is null ? anterior : registro;
    }

    internal static IEnumerable<string> Registros(string processo)
    {
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pasta in new[] { Diretorio(processo, "editor"), Legado(processo, "editor") })
        {
            string[] encontrados;
            try
            {
                if (!Directory.Exists(pasta) || new DirectoryInfo(pasta).LinkTarget is not null) continue;
                encontrados = Directory.GetDirectories(pasta);
            }
            catch (Exception erro) when (erro is IOException or UnauthorizedAccessException) { continue; }
            foreach (var registro in encontrados)
            {
                var nome = Path.GetFileName(registro);
                if (!nome.StartsWith('.') && vistos.Add(nome)) yield return registro;
            }
        }
    }
}
