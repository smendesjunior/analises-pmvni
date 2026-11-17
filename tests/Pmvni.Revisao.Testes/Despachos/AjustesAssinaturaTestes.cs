using System.Text;
using System.Text.Json.Nodes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

/// <summary>Ajustes › Despachos e PDF › Assinatura digital (2026-11-16): de onde vem o certificado vai para o ajustes.json; a senha nunca.
/// A senha lembrada fica cifrada (DPAPI, por usuário) e só vale para o certificado com que foi guardada.</summary>
public sealed class AjustesAssinaturaTestes
{
    [Fact]
    public void padrao_e_sem_assinatura()
    {
        var a = new Ajustes();
        Assert.Equal("nenhuma", a.AssinaturaOrigem);
        Assert.False(a.AssinaturaConfigurada);
        Assert.False(a.AssinaturaLembrarSenha);
        Assert.Equal(TextosAssinatura.SemCertificado, TextosAssinatura.Estado(a, null));
        Assert.Equal("Sem assinatura digital (escolha o certificado em Ajustes › Despachos e PDF)", TextosAssinatura.NaConferencia(null));
    }

    [Fact]
    public void certificado_vai_e_volta_pelo_arquivo_sem_nenhuma_senha()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-assinatura");
        var a = new Ajustes();
        a.DefinirAssinatura("arquivo", @"C:\certificados\ficticio.pfx");
        a.AssinaturaLembrarSenha = true;
        a.Salvar(pasta);

        var texto = File.ReadAllText(Path.Combine(pasta, Ajustes.NomeArquivo));
        var j = JsonNode.Parse(texto)!.AsObject();
        Assert.Equal("arquivo", (string?)j["assinatura_origem"]);
        Assert.Equal(@"C:\certificados\ficticio.pfx", (string?)j["assinatura_arquivo"]);
        Assert.True((bool)j["assinatura_lembrar_senha"]!);
        Assert.DoesNotContain("senha\":\"", texto.Replace(" ", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);

        var b = Ajustes.Carregar(pasta);
        Assert.True(b.AssinaturaConfigurada);
        Assert.Equal("arquivo", b.AssinaturaOrigem);
        Assert.Equal(@"C:\certificados\ficticio.pfx", b.AssinaturaArquivo);
        Assert.True(b.AssinaturaLembrarSenha);
        Assert.Equal(@"C:\certificados\ficticio.pfx", b.AssinaturaIdentidade);

        // Certificado do Windows: a impressão digital (limpa), e "lembrar senha" não vale (o token pede a dele).
        b.DefinirAssinatura("windows", "ab cd 12");
        Assert.Equal("ABCD12", b.AssinaturaImpressaoDigital);
        Assert.Null(b.AssinaturaArquivo);
        Assert.False(b.AssinaturaLembrarSenha);
        b.Salvar(pasta);
        var c = Ajustes.Carregar(pasta);
        Assert.Equal("windows", c.AssinaturaOrigem);
        Assert.Equal("ABCD12", c.AssinaturaIdentidade);

        // Valor estragado no arquivo: fica sem assinatura; "restaurar" também tira.
        File.WriteAllText(Path.Combine(pasta, Ajustes.NomeArquivo), "{\"assinatura_origem\":\"outra\",\"assinatura_arquivo\":\"x.pfx\",\"assinatura_lembrar_senha\":true}");
        var d = Ajustes.Carregar(pasta);
        Assert.False(d.AssinaturaConfigurada);
        Assert.False(d.AssinaturaLembrarSenha);
        c.RestaurarPadrao();
        Assert.False(c.AssinaturaConfigurada);
        Assert.Throws<ArgumentOutOfRangeException>(() => c.DefinirAssinatura("token", "x"));
        c.DefinirAssinatura("arquivo", "  ");
        Assert.Equal("nenhuma", c.AssinaturaOrigem);
    }

    [Fact]
    public void senha_lembrada_e_cifrada_e_so_vale_para_o_mesmo_certificado()
    {
        using var pasta = Ambiente.PastaDeTeste("cofre-assinatura");
        const string senha = "senha-ficticia-cofre";
        Assert.False(CofreSenhaCertificado.Existe(pasta));
        Assert.Null(CofreSenhaCertificado.Ler(pasta, @"C:\a.pfx"));

        CofreSenhaCertificado.Guardar(pasta, @"C:\a.pfx", senha);

        var arquivo = Path.Combine(pasta, "segredos", "assinatura.dpapi");
        Assert.True(File.Exists(arquivo));
        var bytes = File.ReadAllBytes(arquivo);
        Assert.DoesNotContain(senha, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        Assert.DoesNotContain(senha, Encoding.Unicode.GetString(bytes), StringComparison.Ordinal);
        Assert.Equal(senha, CofreSenhaCertificado.Ler(pasta, @"C:\a.pfx"));
        Assert.Equal(senha, CofreSenhaCertificado.Ler(pasta, @"c:\A.PFX")); // o mesmo caminho, outra caixa
        Assert.Null(CofreSenhaCertificado.Ler(pasta, @"C:\outro.pfx"));      // outro certificado: não serve
        Assert.Empty(Directory.GetFiles(Path.Combine(pasta, "segredos"), "*.tmp"));

        File.WriteAllBytes(arquivo, [1, 2, 3]);                               // corrompido: como se não houvesse
        Assert.Null(CofreSenhaCertificado.Ler(pasta, @"C:\a.pfx"));
        CofreSenhaCertificado.Remover(pasta);
        Assert.False(CofreSenhaCertificado.Existe(pasta));
        CofreSenhaCertificado.Remover(pasta);                                 // sem arquivo, sem erro
        Assert.Throws<ArgumentException>(() => CofreSenhaCertificado.Guardar(pasta, @"C:\a.pfx", ""));
    }

    [Fact]
    public void textos_da_pagina_e_da_conferencia()
    {
        var a = new Ajustes();
        a.DefinirAssinatura("arquivo", @"C:\nao-existe\ficticio.pfx");
        Assert.Equal(@"Arquivo: C:\nao-existe\ficticio.pfx  (não encontrado)", TextosAssinatura.Estado(a, null));
        var info = new InfoCertificado(AssinaturaFicticia.Titular, "AC FICTICIA", new DateTime(2030, 1, 2), new DateTime(2033, 1, 2), new string('A', 40));
        Assert.EndsWith(AssinaturaFicticia.Titular + " · válido até 02/01/2033 · emitido por AC FICTICIA", TextosAssinatura.Estado(a, info));
        Assert.Equal("Titular: " + AssinaturaFicticia.Titular + ". Válido de 02/01/2030 a 02/01/2033. Emitido por AC FICTICIA. Pronto para assinar os despachos.",
            TextosAssinatura.Conferido(info, null));
        Assert.Equal("Assinado digitalmente por " + AssinaturaFicticia.Titular,
            TextosAssinatura.NaConferencia(new AssinaturaAplicada(info, DateTimeOffset.Now, new LocalAssinatura(0, 1, 2, 3, 4))));
        Assert.DoesNotContain("senha-", TextosAssinatura.PedirSenhaTexto(@"C:\x\ficticio.pfx"), StringComparison.Ordinal);
        Assert.Contains("ficticio.pfx", TextosAssinatura.PedirSenhaTexto(@"C:\x\ficticio.pfx"), StringComparison.Ordinal);
    }

    /// <summary>A janela pede a senha numa caixa de senha (PasswordBox, sem revelar) e só guarda a senha que abriu o certificado.</summary>
    [Fact]
    public void a_janela_pede_a_senha_em_caixa_de_senha_e_nunca_a_registra()
    {
        var raiz = Path.Combine(Ambiente.PastaTestes, "..", "src", "Pmvni.App");
        var perguntas = File.ReadAllText(Path.Combine(raiz, "Perguntas.cs"));
        Assert.Contains("new PasswordBox", perguntas, StringComparison.Ordinal);
        Assert.Contains("PasswordRevealMode.Hidden", perguntas, StringComparison.Ordinal);
        var janela = File.ReadAllText(Path.Combine(raiz, "MainWindow.Assinatura.cs"));
        // A senha só vai para abrir o certificado e para o cofre (DPAPI); nunca para mensagem, registro ou ajustes.
        var usos = System.Text.RegularExpressions.Regex.Matches(janela, @"resposta\.Senha|lembrada\b").Select(m => m.Value).ToList();
        Assert.NotEmpty(usos);
        foreach (var linha in janela.Split('\n').Where(l => l.Contains("resposta.Senha", StringComparison.Ordinal) || l.Contains("lembrada)", StringComparison.Ordinal)))
            Assert.True(linha.Contains("CertificadoDigital.AbrirArquivo(", StringComparison.Ordinal) || linha.Contains("CofreSenhaCertificado.Guardar(", StringComparison.Ordinal)
                || linha.Contains("CofreSenhaCertificado.Ler(", StringComparison.Ordinal), linha.Trim());
        // Nenhuma propriedade de texto com senha nem chave "…senha" no ajustes.json (só o booleano "lembrar_senha").
        var ajustesCs = File.ReadAllText(Path.Combine(raiz, "Logica", "Ajustes.cs")).Replace("[\"assinatura_lembrar_senha\"]", "", StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\bstring\??\s+\w*Senha\b|\[""\w*senha""\]", ajustesCs);
    }
}
