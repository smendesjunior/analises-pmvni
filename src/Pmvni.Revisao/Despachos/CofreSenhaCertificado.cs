using System.Security.Cryptography;
using System.Text;

namespace Pmvni.Revisao;

/// <summary>A senha do certificado da assinatura digital, quando o usuário pede "lembrar neste computador" (2026-11-16): cifrada por
/// usuário do Windows com DPAPI em <c>&lt;dados&gt;\segredos\assinatura.dpapi</c>, como a chave de API no <c>CofreChaves</c>. Nunca em
/// <c>ajustes.json</c>, em registro, em log, em receita nem no <c>guardado</c> do Recriar PC; a tela nunca a mostra. O arquivo não serve em
/// outro PC nem para outro usuário (depois de formatar, digita-se de novo), nem para outro certificado: a cifra leva junto a identidade do
/// certificado (o caminho do arquivo ou a impressão digital), então trocar de certificado faz a senha guardada deixar de valer.</summary>
public static class CofreSenhaCertificado
{
    static readonly UTF8Encoding Utf8 = new(false);

    static string Arquivo(string raizDados) => Path.Combine(raizDados, "segredos", "assinatura.dpapi");

    static byte[] Entropia(string certificado) => Utf8.GetBytes("PMVNI-Editor-Assinatura-v1|" + (certificado ?? "").Trim().ToUpperInvariant());

    /// <summary>Protege e grava (arquivo novo ao lado, depois troca). Senha vazia ⇒ <see cref="ArgumentException"/>.</summary>
    public static void Guardar(string raizDados, string certificado, string senha)
    {
        ArgumentNullException.ThrowIfNull(raizDados);
        if (string.IsNullOrEmpty(senha)) throw new ArgumentException("A senha não pode ser vazia.", nameof(senha));
        var destino = Arquivo(raizDados);
        Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
        var bytes = Utf8.GetBytes(senha);
        try
        {
            var protegida = ProtectedData.Protect(bytes, Entropia(certificado), DataProtectionScope.CurrentUser);
            var tmp = destino + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(tmp, protegida);
                File.Move(tmp, destino, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
            }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    public static bool Existe(string raizDados) => File.Exists(Arquivo(raizDados));

    /// <summary>Apaga a senha guardada (sem erro se não houver).</summary>
    public static void Remover(string raizDados)
    {
        var arquivo = Arquivo(raizDados);
        try { if (File.Exists(arquivo)) File.Delete(arquivo); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* fica; a próxima gravação a substitui */ }
    }

    /// <summary>A senha guardada para este certificado, só para abrir o certificado. Arquivo ausente, corrompido, de outro usuário ou de
    /// outro certificado ⇒ null.</summary>
    public static string? Ler(string raizDados, string certificado)
    {
        try
        {
            var arquivo = Arquivo(raizDados);
            if (!File.Exists(arquivo)) return null;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(arquivo), Entropia(certificado), DataProtectionScope.CurrentUser);
            try { return Utf8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }
}
