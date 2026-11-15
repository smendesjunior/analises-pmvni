namespace Pmvni.Agentes;

/// <summary>As linhas de comando exatas (desenho §12.4–§12.5, opções conferidas no passo 0): sempre argumentos separados (<c>ArgumentList</c>),
/// nunca texto montado à mão, nunca shell.</summary>
public static class LinhasDeComando
{
    /// <summary><c>codex exec --skip-git-repo-check --cd P --json --output-last-message M -</c>: o pedido vem pela entrada padrão (<c>-</c>).
    /// Sem <c>--sandbox</c>, <c>--model</c>, <c>--ignore-user-config</c>, <c>--ephemeral</c> nem <c>-c</c>: vale a configuração real do usuário.</summary>
    public static (string Exe, IReadOnlyList<string> Args) CodexExec(string codex, string pastaProcesso, string ultimaMensagem) =>
        (codex, new[] { "exec", "--skip-git-repo-check", "--cd", pastaProcesso, "--json", "--output-last-message", ultimaMensagem, "-" });

    /// <summary><c>python -B -X utf8 pro_runtime.py --case P iniciar|situacao</c> (com o launcher <c>py.exe</c>, o primeiro argumento é <c>-3</c>).</summary>
    public static (string Exe, IReadOnlyList<string> Args) ProRuntime(string python, bool launcher, string script, string pastaProcesso, string acao)
    {
        if (acao is not ("iniciar" or "situacao")) throw new ArgumentException("Ação desconhecida do análise pro: " + acao, nameof(acao));
        var args = new List<string>();
        if (launcher) args.Add("-3");
        args.AddRange(new[] { "-B", "-X", "utf8", script, "--case", pastaProcesso, acao });
        return (python, args);
    }

    public static (string Exe, IReadOnlyList<string> Args) CodexVersao(string codex) => (codex, new[] { "--version" });

    public static (string Exe, IReadOnlyList<string> Args) CodexLoginStatus(string codex) => (codex, new[] { "login", "status" });

    public static (string Exe, IReadOnlyList<string> Args) ClaudeVersao(string claude) => (claude, new[] { "--version" });

    public static (string Exe, IReadOnlyList<string> Args) ClaudeAuthStatus(string claude) => (claude, new[] { "auth", "status" });

    public static (string Exe, IReadOnlyList<string> Args) PythonVersao(string python, bool launcher) =>
        (python, launcher ? new[] { "-3", "--version" } : new[] { "--version" });
}
