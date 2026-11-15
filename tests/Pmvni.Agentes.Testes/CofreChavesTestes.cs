using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Chave de API protegida por DPAPI do usuário do Windows (desenho §12.9), só com chaves falsas e em tests/saida.</summary>
public class CofreChavesTestes
{
    static string ChaveFalsa() => "sk-teste-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    internal static bool Contem(byte[] bytes, string chave)
    {
        foreach (var alvo in new[] { Encoding.UTF8.GetBytes(chave), Encoding.Unicode.GetBytes(chave), Encoding.ASCII.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(chave))) })
            if (bytes.AsSpan().IndexOf(alvo) >= 0) return true;
        return false;
    }

    [Fact]
    public void chave_protegida_e_recuperada()
    {
        using var t = Ambiente.PastaDeTeste("cofre");
        var chave = ChaveFalsa();
        Assert.False(CofreChaves.Existe(t, Provedor.ChatGpt));
        Assert.Null(CofreChaves.Ler(t, Provedor.ChatGpt));

        CofreChaves.Guardar(t, Provedor.ChatGpt, chave);
        var arquivo = Path.Combine(t, "segredos", "chatgpt.dpapi");
        Assert.True(File.Exists(arquivo));
        Assert.True(CofreChaves.Existe(t, Provedor.ChatGpt));
        Assert.False(CofreChaves.Existe(t, Provedor.Claude));
        Assert.False(Contem(File.ReadAllBytes(arquivo), chave), "a chave apareceu no arquivo (UTF-8, UTF-16 ou base64)");
        Assert.Equal(chave, CofreChaves.Ler(t, Provedor.ChatGpt));
        Assert.Empty(Directory.GetFiles(Path.Combine(t, "segredos"), "*.tmp"));

        // Trocar a chave substitui; espaços e quebras coladas junto saem.
        var outra = ChaveFalsa();
        CofreChaves.Guardar(t, Provedor.ChatGpt, "  " + outra + "\r\n");
        Assert.Equal(outra, CofreChaves.Ler(t, Provedor.ChatGpt));

        // Provedores separados.
        var daClaude = ChaveFalsa();
        CofreChaves.Guardar(t, Provedor.Claude, daClaude);
        Assert.True(File.Exists(Path.Combine(t, "segredos", "claude.dpapi")));
        Assert.Equal(daClaude, CofreChaves.Ler(t, Provedor.Claude));
        Assert.Equal(outra, CofreChaves.Ler(t, Provedor.ChatGpt));

        CofreChaves.Remover(t, Provedor.ChatGpt);
        Assert.False(File.Exists(arquivo));
        Assert.False(CofreChaves.Existe(t, Provedor.ChatGpt));
        Assert.Null(CofreChaves.Ler(t, Provedor.ChatGpt));
        CofreChaves.Remover(t, Provedor.ChatGpt);      // remover o que não existe não é erro
    }

    [Fact]
    public void blob_corrompido_ou_de_outro_usuario_devolve_null()
    {
        using var t = Ambiente.PastaDeTeste("cofre-corrompido");
        CofreChaves.Guardar(t, Provedor.ChatGpt, ChaveFalsa());
        var arquivo = Path.Combine(t, "segredos", "chatgpt.dpapi");
        var bytes = File.ReadAllBytes(arquivo);
        bytes[^3] ^= 0xFF;
        File.WriteAllBytes(arquivo, bytes);
        Assert.Null(CofreChaves.Ler(t, Provedor.ChatGpt));
        File.WriteAllText(arquivo, "isto não é um blob do DPAPI");
        Assert.Null(CofreChaves.Ler(t, Provedor.ChatGpt));
        File.WriteAllBytes(arquivo, Array.Empty<byte>());
        Assert.Null(CofreChaves.Ler(t, Provedor.ChatGpt));
        Assert.True(CofreChaves.Existe(t, Provedor.ChatGpt));        // o arquivo existe; só não abre
    }

    [Fact]
    public void chave_vazia_e_recusada()
    {
        using var t = Ambiente.PastaDeTeste("cofre-vazio");
        Assert.Throws<ArgumentException>(() => CofreChaves.Guardar(t, Provedor.ChatGpt, ""));
        Assert.Throws<ArgumentException>(() => CofreChaves.Guardar(t, Provedor.ChatGpt, "   \r\n"));
        Assert.Throws<ArgumentException>(() => CofreChaves.Guardar(t, Provedor.ChatGpt, null!));
        Assert.False(Directory.Exists(Path.Combine(t, "segredos")) && Directory.GetFiles(Path.Combine(t, "segredos")).Length > 0);
    }
}
