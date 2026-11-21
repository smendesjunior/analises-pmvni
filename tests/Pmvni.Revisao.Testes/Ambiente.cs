using System.Security.Cryptography;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Pasta de trabalho de um teste, em <c>tests/saida/revisao/&lt;nome&gt;-&lt;guid&gt;</c> (ignorada pelo git); apagada no Dispose.
/// Converte para <see cref="string"/> para servir direto em <see cref="Path.Combine(string, string)"/>.</summary>
public sealed class PastaTeste : IDisposable
{
    public string Caminho { get; }

    internal PastaTeste(string caminho) => Caminho = caminho;

    public static implicit operator string(PastaTeste p) => p.Caminho;

    public override string ToString() => Caminho;

    public void Dispose()
    {
        try { Directory.Delete(Caminho, true); }
        catch (IOException) { /* limpeza de temporário: não derruba o teste */ }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Testes que mexem em variável de ambiente: sem paralelismo entre eles.</summary>
[CollectionDefinition("Ambiente", DisableParallelization = true)]
public sealed class ColecaoAmbiente;

/// <summary>Caminhos do repositório usados pelos testes. Nada aqui grava em %APPDATA%, %LOCALAPPDATA% nem %TEMP%
/// (o shell do Claude é virtualizado): tudo o que o teste cria fica em <c>tests/saida</c>.</summary>
public static class Ambiente
{
    /// <summary><c>tests/</c> do repositório, achada a partir da pasta do executável.</summary>
    public static string PastaTestes { get; } = Localizar();

    public static string Corpus(string nome) => Path.Combine(PastaTestes, "corpus", nome);

    /// <summary>Cria <c>tests/saida/revisao/&lt;nome&gt;-&lt;guid&gt;</c> vazia.</summary>
    public static PastaTeste PastaDeTeste(string nome)
    {
        var caminho = Path.Combine(PastaTestes, "saida", "revisao", nome + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(caminho);
        return new PastaTeste(caminho);
    }

    /// <summary>Contexto com relógio fixo (o sorteio é o real).</summary>
    public static ContextoUsuario Contexto(string usuario = "teste") =>
        new(usuario, () => new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero), RandomNumberGenerator.Create());

    /// <summary>Raiz do kit PMVNI (somente leitura): <c>PMVNI_KIT_RAIZ</c> ou o padrão do desenho §10.10.</summary>
    /// <remarks>Para testar a fonte do kit, aponte PMVNI_KIT_RAIZ para _DESENVOLVIMENTO\repositorio dentro do kit mestre.</remarks>
    public static string RaizKit
    {
        get
        {
            var env = Environment.GetEnvironmentVariable("PMVNI_KIT_RAIZ");
            return !string.IsNullOrWhiteSpace(env)
                ? env
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "PMVNI", "SIDNEY JUNIOR",
                    "KIT DE TREINAMENTO DE IA (PMVNI)");
        }
    }

    /// <summary>A raiz do kit, lida no lugar; sem os modelos de despacho (formato novo ou antigo), o teste falha (como a Etapa 1 com o gerador do kit).</summary>
    public static string ExigirKit()
    {
        var raiz = RaizKit;
        if (FontesModelos.Escolher(FontesModelos.DaPastaDoKit(raiz)) is null)
            Assert.Fail("Kit PMVNI ausente: " + raiz + " (defina PMVNI_KIT_RAIZ)");
        return raiz;
    }

    private static string Localizar()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "tests");
            if (Directory.Exists(Path.Combine(c, "corpus"))) return c;
        }
        Assert.Fail("tests/corpus não encontrada a partir de " + AppContext.BaseDirectory);
        return "";
    }
}
