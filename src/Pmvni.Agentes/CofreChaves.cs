using System.Security.Cryptography;
using System.Text;

namespace Pmvni.Agentes;

/// <summary>A chave de API guardada por usuário do Windows com DPAPI (desenho §12.9): <c>&lt;dados&gt;\segredos\&lt;chatgpt|claude&gt;.dpapi</c>. Nunca em
/// <c>ajustes.json</c>, no registro da execução, em log, em receita nem no <c>guardado</c> do Recriar PC; a tela nunca mostra a chave. O arquivo não vai para
/// outro PC nem para outro usuário (depois de formatar, digita-se de novo).</summary>
public static class CofreChaves
{
    static readonly byte[] Entropia = Encoding.UTF8.GetBytes("PMVNI-Editor-Agentes-v1");
    static readonly UTF8Encoding Utf8 = new(false);

    static string Arquivo(string raizDados, Provedor p) => Path.Combine(raizDados, "segredos", Provedores.Sufixo(p) + ".dpapi");

    /// <summary>Protege e grava (arquivo novo ao lado, depois troca). Espaços e quebras de linha nas pontas saem; vazio ⇒ <see cref="ArgumentException"/>.</summary>
    public static void Guardar(string raizDados, Provedor p, string chave)
    {
        var limpa = chave?.Trim();
        if (string.IsNullOrEmpty(limpa)) throw new ArgumentException("A chave não pode ser vazia.", nameof(chave));

        var destino = Arquivo(raizDados, p);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        var bytes = Utf8.GetBytes(limpa);
        try
        {
            var protegida = ProtectedData.Protect(bytes, Entropia, DataProtectionScope.CurrentUser);
            var tmp = destino + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(ArquivoGerado.Caminho(tmp), protegida);
                File.Move(tmp, ArquivoGerado.Caminho(destino), overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static bool Existe(string raizDados, Provedor p) => File.Exists(Arquivo(raizDados, p));

    /// <summary>Apaga o arquivo da chave (sem erro se não existir).</summary>
    public static void Remover(string raizDados, Provedor p)
    {
        var arquivo = Arquivo(raizDados, p);
        if (File.Exists(arquivo)) File.Delete(arquivo);
    }

    /// <summary>A chave em texto, só para entregar ao processo filho. Arquivo ausente, corrompido ou de outro usuário ⇒ null.</summary>
    internal static string? Ler(string raizDados, Provedor p)
    {
        try
        {
            var arquivo = Arquivo(raizDados, p);
            if (!File.Exists(arquivo)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(arquivo), Entropia, DataProtectionScope.CurrentUser);
            try { return Utf8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }
}
