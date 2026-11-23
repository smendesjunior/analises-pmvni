using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Pmvni.Revisao;

/// <summary>A assinatura digital não pôde ser feita; a mensagem é para o usuário (nunca traz a senha).</summary>
public sealed class AssinaturaDigitalException(string mensagem, Exception? interna = null) : Exception(mensagem, interna);

/// <summary>O que se mostra de um certificado: o titular (o CN; na ICP-Brasil, "NOME:CPF"), quem emitiu, a validade e a impressão digital
/// (SHA-1 em hexadecimal, que identifica o certificado no repositório do Windows).</summary>
public sealed record InfoCertificado(string Titular, string Emissor, DateTime ValidoDe, DateTime ValidoAte, string ImpressaoDigital)
{
    /// <summary>"FULANO:000… · válido até 10/03/2099 · emitido por AC …".</summary>
    public string Resumo =>
        Titular + " · válido até " + ValidoAte.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + (Emissor.Length == 0 || Emissor == Titular ? "" : " · emitido por " + Emissor);
}

/// <summary>Um certificado aberto para assinar: o do titular (com a chave privada) e os outros do arquivo (a cadeia, que vai junto na
/// assinatura). Descartar solta a chave.</summary>
public sealed class CertificadoAberto(X509Certificate2 certificado, IReadOnlyList<X509Certificate2> cadeia) : IDisposable
{
    public X509Certificate2 Certificado { get; } = certificado;
    public IReadOnlyList<X509Certificate2> Cadeia { get; } = cadeia;
    public InfoCertificado Info { get; } = CertificadoDigital.Info(certificado);

    public void Dispose()
    {
        Certificado.Dispose();
        foreach (var c in Cadeia) c.Dispose();
    }
}

/// <summary>Leitura do certificado digital da assinatura do despacho (2026-11-16): de um arquivo <c>.pfx</c>/<c>.p12</c> com senha (A1) ou
/// do repositório pessoal do Windows (A3: token ou cartão, cuja senha o próprio driver pede). Nada aqui grava: a chave do arquivo fica só
/// na memória (<see cref="X509KeyStorageFlags.EphemeralKeySet"/>) e a senha nunca vai para mensagem, exceção ou registro.</summary>
public static class CertificadoDigital
{
    public const string SenhaIncorreta = "A senha do certificado não confere. Nada foi assinado nem gravado.";
    public const string ArquivoAusente = "O arquivo do certificado não foi encontrado. Escolha-o de novo em Ajustes › Assinatura digital.";
    public const string ArquivoInvalido = "O arquivo escolhido não é um certificado digital (.pfx ou .p12) que possa ser lido.";
    public const string SemChavePrivada = "O arquivo não traz a chave privada do certificado; exporte-o de novo com a chave (.pfx ou .p12).";
    public const string AusenteNoWindows =
        "O certificado escolhido não está mais no repositório do Windows (ou o token/cartão não está conectado). Conecte-o ou escolha de novo em Ajustes › Assinatura digital.";

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static InfoCertificado CriarNoWindows(string titular)
    {
        var nome = new X500DistinguishedNameBuilder();
        nome.AddCommonName(titular);
        var parametros = new CngKeyCreationParameters { KeyUsage = CngKeyUsages.Signing };
        parametros.Parameters.Add(new CngProperty("Length", BitConverter.GetBytes(3072), CngPropertyOptions.None));
        using var chave = CngKey.Create(CngAlgorithm.Rsa, "PMVNI-" + Guid.NewGuid().ToString("N"), parametros);
        try
        {
            using var rsa = new RSACng(chave);
            var pedido = new CertificateRequest(nome.Build(), rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            pedido.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
            var agora = DateTimeOffset.Now;
            using var certificado = pedido.CreateSelfSigned(agora.AddMinutes(-5), agora.AddYears(1));
            var info = Info(certificado);
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadWrite);
            store.Add(certificado);
            return info;
        }
        catch
        {
            chave.Delete();
            throw;
        }
    }

    const int ErroSenha = unchecked((int)0x80070056); // ERROR_INVALID_PASSWORD

    /// <summary>Abre o <c>.pfx</c>/<c>.p12</c> com a senha. Senha errada, arquivo ausente, ilegível ou sem a chave privada:
    /// <see cref="AssinaturaDigitalException"/> com a mensagem certa (e nada fica aberto nem gravado).</summary>
    public static CertificadoAberto AbrirArquivo(string caminho, string? senha)
    {
        ArgumentNullException.ThrowIfNull(caminho);
        byte[] bytes;
        try { bytes = File.ReadAllBytes(caminho); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new AssinaturaDigitalException(ArquivoAusente);
        }
        X509Certificate2Collection todos;
        try { todos = X509CertificateLoader.LoadPkcs12Collection(bytes, senha ?? "", X509KeyStorageFlags.EphemeralKeySet); }
        catch (CryptographicException e)
        {
            // A exceção original não segue como causa: quem registrar o erro não leva nada do arquivo nem da senha.
            throw new AssinaturaDigitalException(e.HResult == ErroSenha ? SenhaIncorreta : ArquivoInvalido);
        }
        var lista = todos.ToList();
        var titular = lista.Where(c => c.HasPrivateKey).OrderByDescending(ServeParaAssinar).FirstOrDefault();
        if (titular is null)
        {
            foreach (var c in lista) c.Dispose();
            throw new AssinaturaDigitalException(lista.Count == 0 ? ArquivoInvalido : SemChavePrivada);
        }
        return new CertificadoAberto(titular, lista.Where(c => !ReferenceEquals(c, titular)).ToList());
    }

    /// <summary>O certificado do repositório pessoal do Windows com a impressão digital pedida (a senha/PIN do token é pedida pelo
    /// driver na hora de assinar). Ausente: <see cref="AssinaturaDigitalException"/>.</summary>
    public static CertificadoAberto AbrirDoWindows(string impressaoDigital)
    {
        ArgumentNullException.ThrowIfNull(impressaoDigital);
        var limpa = Limpar(impressaoDigital);
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            X509Certificate2? achado = null;
            foreach (var c in store.Certificates)
            {
                if (achado is null && c.HasPrivateKey && string.Equals(c.Thumbprint, limpa, StringComparison.OrdinalIgnoreCase)) achado = c;
                else c.Dispose();
            }
            return achado is null ? throw new AssinaturaDigitalException(AusenteNoWindows) : new CertificadoAberto(achado, []);
        }
        catch (CryptographicException) { throw new AssinaturaDigitalException(AusenteNoWindows); }
    }

    /// <summary>Os certificados do repositório pessoal do Windows que servem para assinar (com chave privada, dentro da validade em
    /// <paramref name="agora"/> e sem restrição de uso que proíba assinatura), do que vence mais tarde para o que vence antes.</summary>
    public static IReadOnlyList<InfoCertificado> ListarDoWindows(DateTimeOffset agora)
    {
        var lista = new List<InfoCertificado>();
        try
        {
            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (var c in store.Certificates)
                using (c)
                    if (c.HasPrivateKey && ServeParaAssinar(c) && Problema(c, agora) is null) lista.Add(Info(c));
        }
        catch (CryptographicException) { /* sem repositório: lista vazia */ }
        return lista.OrderByDescending(i => i.ValidoAte).ThenBy(i => i.Titular, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static InfoCertificado Info(X509Certificate2 c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new InfoCertificado(Nome(c, false), Nome(c, true), c.NotBefore, c.NotAfter, c.Thumbprint);
    }

    /// <summary>O CN (na ICP-Brasil, "NOME:CPF"); sem CN, o nome simples que o Windows mostra.</summary>
    static string Nome(X509Certificate2 c, bool emissor)
    {
        var nome = c.GetNameInfo(X509NameType.SimpleName, emissor);
        return string.IsNullOrWhiteSpace(nome) ? (emissor ? c.Issuer : c.Subject) : nome.Trim();
    }

    /// <summary>O que impede assinar com o certificado em <paramref name="agora"/> (vencido, ainda não válido, sem chave privada ou de uso
    /// que não é assinatura); null se pode.</summary>
    public static string? Problema(X509Certificate2 c, DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(c);
        if (!c.HasPrivateKey) return SemChavePrivada;
        var hora = agora.LocalDateTime;
        if (hora > c.NotAfter) return "O certificado venceu em " + c.NotAfter.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + ". Escolha um certificado válido em Ajustes › Assinatura digital.";
        if (hora < c.NotBefore) return "O certificado só vale a partir de " + c.NotBefore.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + ".";
        if (!ServeParaAssinar(c)) return "Este certificado não é de assinatura digital (o uso da chave não permite assinar documentos).";
        return null;
    }

    /// <summary>Sem a extensão "uso da chave", serve; com ela, precisa de assinatura digital ou não repúdio.</summary>
    static bool ServeParaAssinar(X509Certificate2 c)
    {
        var uso = c.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        return uso is null || (uso.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) != 0;
    }

    /// <summary>A impressão digital só com os dígitos hexadecimais, em maiúsculas (o Windows a mostra com espaços).</summary>
    public static string Limpar(string impressaoDigital) =>
        string.Concat((impressaoDigital ?? "").Where(Uri.IsHexDigit)).ToUpperInvariant();
}
