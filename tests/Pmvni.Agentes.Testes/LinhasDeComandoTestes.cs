using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Linhas de comando exatas (desenho §12.4–§12.5; opções conferidas no passo 0): sempre <c>ArgumentList</c>, nunca shell.</summary>
public class LinhasDeComandoTestes
{
    [Fact]
    public void linha_do_codex_exata()
    {
        var (exe, args) = LinhasDeComando.CodexExec(@"C:\codex\codex.exe", @"C:\P\2099\0001 - 2099 - X", @"C:\P\2099\0001 - 2099 - X\.pmvni-ai\editor\r1\ultima-mensagem.md");
        Assert.Equal(@"C:\codex\codex.exe", exe);
        Assert.Equal(new[]
        {
            "exec", "--skip-git-repo-check", "--cd", @"C:\P\2099\0001 - 2099 - X", "--json", "--output-last-message",
            @"C:\P\2099\0001 - 2099 - X\.pmvni-ai\editor\r1\ultima-mensagem.md", "-",
        }, args);
    }

    [Fact]
    public void linhas_do_analise_pro_exatas()
    {
        var (exe, iniciar) = LinhasDeComando.ProRuntime(@"C:\Python\python.exe", false, @"C:\h\pro_runtime.py", @"C:\P\caso", "iniciar");
        Assert.Equal(@"C:\Python\python.exe", exe);
        Assert.Equal(new[] { "-B", "-X", "utf8", @"C:\h\pro_runtime.py", "--case", @"C:\P\caso", "iniciar" }, iniciar);

        var (exeLauncher, situacao) = LinhasDeComando.ProRuntime(@"C:\Windows\py.exe", true, @"C:\h\pro_runtime.py", @"C:\P\caso", "situacao");
        Assert.Equal(@"C:\Windows\py.exe", exeLauncher);
        Assert.Equal(new[] { "-3", "-B", "-X", "utf8", @"C:\h\pro_runtime.py", "--case", @"C:\P\caso", "situacao" }, situacao);

        Assert.Throws<ArgumentException>(() => LinhasDeComando.ProRuntime("p.exe", false, "s.py", "c", "apagar"));
    }

    [Fact]
    public void linhas_do_diagnostico()
    {
        Confere(LinhasDeComando.CodexVersao("c.exe"), "c.exe", "--version");
        Confere(LinhasDeComando.CodexLoginStatus("c.exe"), "c.exe", "login", "status");
        Confere(LinhasDeComando.ClaudeVersao("k.exe"), "k.exe", "--version");
        Confere(LinhasDeComando.ClaudeAuthStatus("k.exe"), "k.exe", "auth", "status");
        Confere(LinhasDeComando.PythonVersao("p.exe", false), "p.exe", "--version");
        Confere(LinhasDeComando.PythonVersao("py.exe", true), "py.exe", "-3", "--version");
    }

    static void Confere((string Exe, IReadOnlyList<string> Args) linha, string exe, params string[] args)
    {
        Assert.Equal(exe, linha.Exe);
        Assert.Equal(args, linha.Args);
    }

    /// <summary>Caminhos com espaços, acentos e símbolos de shell chegam inteiros no argv e no diretório de trabalho do filho.
    /// (O Windows não aceita aspas duplas em nome de pasta: elas são testadas num argumento à parte.)</summary>
    [Fact]
    public async Task caminhos_com_espacos_acentos_e_aspas_chegam_inteiros()
    {
        using var t = Ambiente.PastaDeTeste("linhas-caminhos");
        var pasta = Path.Combine(t, "2099", "0001 - 2099 - JOSÉ D'ÁVILA & FILHOS (100% ^ ! ; $ #)");
        Directory.CreateDirectory(pasta);
        var ultima = Path.Combine(pasta, ".pmvni-ai", "editor", "r 1", "ultima-mensagem.md");
        var (exe, args) = LinhasDeComando.CodexExec(Ambiente.AgenteFalso, pasta, ultima);
        var comAspas = args.Concat(new[] { "texto \"entre aspas\" e \\barra\\", "" }).ToList();

        var execucao = ExecucaoProcesso.Iniciar(exe, comAspas, pasta, "entrada ÁÉÍ\nlinha 2", _ => { }, _ => { }, _ => { });
        Assert.Equal(0, await Ambiente.Com(execucao.Fim, TimeSpan.FromSeconds(30)));

        var recebido = JsonNode.Parse(File.ReadAllText(Path.Combine(pasta, ".pmvni-ai", "agente-falso", "recebido.json")))!.AsObject();
        Assert.Equal(comAspas, recebido["argv"]!.AsArray().Select(x => x!.GetValue<string>()).ToList());
        Assert.Equal(pasta, recebido["cwd"]!.GetValue<string>());
        Assert.Equal(pasta, recebido["cd"]!.GetValue<string>());
        Assert.Equal("entrada ÁÉÍ\nlinha 2", recebido["entrada"]!.GetValue<string>());
    }
}
