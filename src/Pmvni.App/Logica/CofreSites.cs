using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pmvni.App;

/// <summary>Um cookie de um sistema da Prefeitura, como o navegador o entrega (sem tipos do WebView2, para os testes).</summary>
public sealed record CookieSite(string Nome, string Valor, string Dominio, string Caminho, bool Seguro, bool SoHttp, int MesmoSite);

/// <summary>Usuário e senha de um sistema, para entrar de novo sozinho quando o servidor encerrar a sessão.</summary>
public sealed record CredencialSite(string Usuario, string Senha);

/// <summary>Login persistente dos sistemas da Prefeitura (2026-11-17, pedido do usuário: "o login não pode expirar nunca, até o usuário
/// deslogar manualmente"). Três camadas, todas só neste usuário do Windows (DPAPI), em <c>&lt;dados&gt;\segredos\</c>:
/// (1) os cookies de cada site, gravados a cada página carregada e devolvidos ao abrir o app — inclusive os "de sessão", que o navegador
/// jogaria fora ao fechar; (2) o app mantém a sessão viva enquanto está aberto (<see cref="PaginaSites"/>); (3) opcionalmente, usuário e senha
/// para entrar sozinho se o servidor mesmo assim encerrar a sessão. Nada disso vai para ajustes.json, log, receita ou o guardado do Recriar PC.
/// "Sair" (Ajustes › Fontes de dados) apaga as três coisas do site.</summary>
public static class CofreSites
{
    static readonly byte[] Entropia = Encoding.UTF8.GetBytes("PMVNI-Editor-Sites-v1");
    static readonly JsonSerializerOptions Json = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    static string Pasta(string raiz) => Path.Combine(raiz, "segredos");
    static string ArquivoCookies(string raiz, Modulo m) => Path.Combine(Pasta(raiz), "site-" + m.ToString().ToLowerInvariant() + "-sessao.dpapi");
    static string ArquivoLogin(string raiz, Modulo m) => Path.Combine(Pasta(raiz), "site-" + m.ToString().ToLowerInvariant() + "-login.dpapi");
    static string ArquivoSaiu(string raiz, Modulo m) => Path.Combine(Pasta(raiz), "site-" + m.ToString().ToLowerInvariant() + "-saiu");

    static void Gravar(string arquivo, byte[] claro)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        try
        {
            var protegido = ProtectedData.Protect(claro, Entropia, DataProtectionScope.CurrentUser);
            var tmp = arquivo + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(tmp, protegido); File.Move(tmp, arquivo, overwrite: true); }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } }
        }
        finally { CryptographicOperations.ZeroMemory(claro); }
    }

    static T? Ler<T>(string arquivo) where T : class
    {
        try
        {
            if (!File.Exists(arquivo)) return null;
            var claro = ProtectedData.Unprotect(File.ReadAllBytes(arquivo), Entropia, DataProtectionScope.CurrentUser);
            try { return JsonSerializer.Deserialize<T>(claro, Json); }
            finally { CryptographicOperations.ZeroMemory(claro); }
        }
        catch (Exception e) when (e is CryptographicException or JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    public static void GuardarCookies(string raiz, Modulo m, IReadOnlyList<CookieSite> cookies) =>
        Gravar(ArquivoCookies(raiz, m), JsonSerializer.SerializeToUtf8Bytes(cookies, Json));

    public static IReadOnlyList<CookieSite> LerCookies(string raiz, Modulo m) => Ler<List<CookieSite>>(ArquivoCookies(raiz, m)) ?? [];

    public static void GuardarLogin(string raiz, Modulo m, CredencialSite c)
    {
        if (string.IsNullOrWhiteSpace(c.Usuario) || string.IsNullOrEmpty(c.Senha)) throw new ArgumentException("Informe o usuário e a senha.");
        Gravar(ArquivoLogin(raiz, m), JsonSerializer.SerializeToUtf8Bytes(c with { Usuario = c.Usuario.Trim() }, Json));
    }

    public static CredencialSite? LerLogin(string raiz, Modulo m) => Ler<CredencialSite>(ArquivoLogin(raiz, m));

    public static bool TemLogin(string raiz, Modulo m) => File.Exists(ArquivoLogin(raiz, m));

    /// <summary>O usuário saiu deste site em Ajustes: nada é restaurado nem preenchido até ele entrar de novo.</summary>
    public static bool Saiu(string raiz, Modulo m) => File.Exists(ArquivoSaiu(raiz, m));

    /// <summary>"Sair": apaga os cookies guardados e o login guardado e marca o site como saído.</summary>
    public static void Sair(string raiz, Modulo m)
    {
        foreach (var a in new[] { ArquivoCookies(raiz, m), ArquivoLogin(raiz, m) }) if (File.Exists(a)) File.Delete(a);
        Directory.CreateDirectory(Pasta(raiz));
        File.WriteAllText(ArquivoSaiu(raiz, m), DateTimeOffset.UtcNow.ToString("O"));
    }

    /// <summary>O usuário entrou de novo (ou guardou um login): o site volta a ser lembrado.</summary>
    public static void Voltou(string raiz, Modulo m)
    {
        var a = ArquivoSaiu(raiz, m);
        if (File.Exists(a)) File.Delete(a);
    }

    /// <summary>O cookie é deste site? (o domínio do cookie cobre o host do endereço inicial, com ou sem ponto na frente).</summary>
    public static bool DoSite(string dominioCookie, string host)
    {
        var d = dominioCookie.TrimStart('.');
        return host.Equals(d, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + d, StringComparison.OrdinalIgnoreCase)
            || d.EndsWith("." + host, StringComparison.OrdinalIgnoreCase);
    }
}
