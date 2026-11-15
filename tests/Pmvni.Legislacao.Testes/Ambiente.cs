using Xunit;

namespace Pmvni.Legislacao.Testes;

/// <summary>Pasta de trabalho de um teste, em <c>tests/saida/legislacao/&lt;nome&gt;-&lt;guid&gt;</c> (ignorada pelo git); apagada no Dispose.</summary>
public sealed class PastaDeTeste : IDisposable
{
    public string Caminho { get; }

    internal PastaDeTeste(string caminho) => Caminho = caminho;

    public static implicit operator string(PastaDeTeste p) => p.Caminho;

    public override string ToString() => Caminho;

    public void Dispose()
    {
        try { Directory.Delete(Caminho, true); }
        catch (IOException) { /* limpeza de temporário: não derruba o teste */ }
        catch (UnauthorizedAccessException) { }
    }
}

/// <summary>Caminhos do repositório. Nada aqui grava em %APPDATA%, %LOCALAPPDATA% nem %TEMP% (o shell do Claude é virtualizado):
/// tudo o que o teste cria fica em <c>tests/saida</c>.</summary>
public static class Ambiente
{
    public static string RaizRepositorio { get; } = Localizar();

    /// <summary><c>tests/corpus/legislacao</c>: os PDFs fictícios versionados.</summary>
    public static string Corpus { get; } = Path.Combine(RaizRepositorio, "tests", "corpus", "legislacao");

    public static PastaDeTeste NovaPasta(string nome)
    {
        var caminho = Path.Combine(RaizRepositorio, "tests", "saida", "legislacao", nome + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(caminho);
        return new PastaDeTeste(caminho);
    }

    static string Localizar()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "EditorRelatoriosPMVNI.sln"))) return d.FullName;
        throw new InvalidOperationException("Raiz do repositório não encontrada a partir de " + AppContext.BaseDirectory);
    }
}
