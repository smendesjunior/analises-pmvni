namespace Pmvni.App;

/// <summary>Linha de comando do app: <c>[dados.json]</c> abre o relatório; <c>--autoteste &lt;saida.json&gt;</c> é o autoteste
/// do executável único (Tarefa 1); <c>--roteiro &lt;saida.json&gt;</c> roda o roteiro de diagnóstico da tela (Tarefa 10).</summary>
internal sealed record Argumentos(string? Dados, string? Roteiro)
{
    public static Argumentos Atual { get; private set; } = new(null, null);

    public static Argumentos Ler(string[] args)
    {
        string? dados = null, roteiro = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--autoteste") { i++; continue; } // tratado por Autoteste.Configurar
            if (args[i] == "--roteiro") { if (i + 1 < args.Length) roteiro = Path.GetFullPath(args[++i]); continue; }
            if (!args[i].StartsWith("--", StringComparison.Ordinal) && dados is null) dados = Path.GetFullPath(args[i]);
        }
        return Atual = new Argumentos(dados, roteiro);
    }
}
