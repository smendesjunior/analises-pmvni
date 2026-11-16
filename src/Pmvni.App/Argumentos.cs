namespace Pmvni.App;

/// <summary>Linha de comando do app: <c>[arquivo]</c> — um <c>.rpmvni</c> abre; um <c>dados.json</c> é importado (pergunta onde gravar o
/// <c>.rpmvni</c> novo, como Arquivo › Importar); <c>--autoteste &lt;saida.json&gt;</c> é o autoteste
/// do executável único (Tarefa 1); <c>--roteiro &lt;saida.json&gt;</c> roda o roteiro de diagnóstico da tela (Tarefa 10).</summary>
/// <c>--anexar-leis &lt;pasta&gt;</c> (2026-11-16, instalação): anexa à Legislação todos os PDFs da pasta (e subpastas), como "Anexar PDFs…";
/// os já anexados (mesmo conteúdo) não se repetem.
/// <c>--captura &lt;pasta&gt;</c> (2026-11-16, desenvolvimento): grava um PNG da janela em cada módulo e fecha, para conferir o visual sem o roteiro.
internal sealed record Argumentos(string? Dados, string? Roteiro, string? AnexarLeis = null, string? Captura = null)
{
    public static Argumentos Atual { get; private set; } = new(null, null);

    public static Argumentos Ler(string[] args)
    {
        string? dados = null, roteiro = null, leis = null, captura = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--autoteste") { i++; continue; } // tratado por Autoteste.Configurar
            if (args[i] == "--anexar-leis") { if (i + 1 < args.Length) leis = Path.GetFullPath(args[++i]); continue; }
            if (args[i] == "--captura") { if (i + 1 < args.Length) captura = Path.GetFullPath(args[++i]); continue; }
            if (args[i] == "--roteiro") { if (i + 1 < args.Length) roteiro = Path.GetFullPath(args[++i]); continue; }
            if (!args[i].StartsWith("--", StringComparison.Ordinal) && dados is null) dados = Path.GetFullPath(args[i]);
        }
        return Atual = new Argumentos(dados, roteiro, leis, captura);
    }
}
