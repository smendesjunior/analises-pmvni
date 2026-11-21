namespace Pmvni.Agentes;

/// <summary>As linhas de comando exatas (desenho §12.4–§12.5, opções conferidas no passo 0): sempre argumentos separados (<c>ArgumentList</c>),
/// nunca texto montado à mão, nunca shell.</summary>
public static class LinhasDeComando
{
    /// <summary><c>python -B -X utf8 executar_analise_62.py --case P --inputs A B …</c> (Tarefa 12): o controlador do analise, rodado direto pelo editor, sem
    /// sessão Codex controladora (erros 1–5 do diagnóstico do processo 19486). Sem entradas (null ou vazia) = retomada: o controlador continua o controle que
    /// já existe na pasta do processo. Com o launcher <c>py.exe</c>, o primeiro argumento é <c>-3</c>.</summary>
    public static (string Exe, IReadOnlyList<string> Args) Analise62(string python, bool launcher, string script, string pastaProcesso, IReadOnlyList<string>? entradas)
    {
        var args = new List<string>();
        if (launcher) args.Add("-3");
        args.AddRange(["-B", "-X", "utf8", script, "--case", pastaProcesso]);
        if (entradas is { Count: > 0 })
        {
            args.Add("--inputs");
            args.AddRange(entradas);
        }
        return (python, args);
    }

    /// <summary><c>python -B -X utf8 pro_runtime.py --case P iniciar|situacao</c> (com o launcher <c>py.exe</c>, o primeiro argumento é <c>-3</c>).</summary>
    public static (string Exe, IReadOnlyList<string> Args) ProRuntime(string python, bool launcher, string script, string pastaProcesso, string acao)
    {
        if (acao is not ("iniciar" or "situacao" or "conferir")) throw new ArgumentException("Ação desconhecida da rota Claude: " + acao, nameof(acao));
        var args = new List<string>();
        if (launcher) args.Add("-3");
        args.AddRange(new[] { "-B", "-X", "utf8", script, "--case", pastaProcesso, acao });
        return (python, args);
    }

    /// <summary><c>agy --version</c> e <c>agy models</c> (a lista "id&lt;tab&gt;nome" da conta; não gera texto nem gasta cota).</summary>
    public static (string Exe, IReadOnlyList<string> Args) AgyVersao(string agy) => (agy, new[] { "--version" });

    public static (string Exe, IReadOnlyList<string> Args) AgyModelos(string agy) => (agy, new[] { "models" });

    public static (string Exe, IReadOnlyList<string> Args) CodexVersao(string codex) => (codex, new[] { "--version" });

    public static (string Exe, IReadOnlyList<string> Args) CodexLoginStatus(string codex) => (codex, new[] { "login", "status" });

    public static (string Exe, IReadOnlyList<string> Args) ClaudeVersao(string claude) => (claude, new[] { "--version" });

    public static (string Exe, IReadOnlyList<string> Args) ClaudeAuthStatus(string claude) => (claude, new[] { "auth", "status" });

    public static (string Exe, IReadOnlyList<string> Args) PythonVersao(string python, bool launcher) =>
        (python, launcher ? new[] { "-3", "--version" } : new[] { "--version" });
}
