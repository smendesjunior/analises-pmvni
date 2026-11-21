using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
using Pmvni.Revisao;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Pasta de trabalho de um teste, em <c>tests/saida/agentes/&lt;nome&gt;-&lt;guid&gt;</c> (ignorada pelo git); apagada no Dispose.</summary>
public sealed class PastaTeste : IDisposable
{
    public string Caminho { get; }

    internal PastaTeste(string caminho) => Caminho = caminho;

    public static implicit operator string(PastaTeste p) => p.Caminho;

    public override string ToString() => Caminho;

    public void Dispose()
    {
        for (var i = 0; i < 20; i++)
        {
            try { Directory.Delete(Caminho, true); return; }
            catch (DirectoryNotFoundException) { return; }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Thread.Sleep(100); }   // processo recém-morto ainda soltando a pasta
        }
    }
}

/// <summary>Testes que mexem em variável de ambiente ou iniciam processos: um de cada vez.</summary>
[CollectionDefinition("Ambiente", DisableParallelization = true)]
public sealed class ColecaoAmbiente;

/// <summary>Define uma variável de ambiente do processo de teste e a restaura no Dispose.</summary>
public sealed class VariavelDeAmbiente : IDisposable
{
    readonly string nome;
    readonly string? antes;

    public VariavelDeAmbiente(string nome, string? valor)
    {
        this.nome = nome;
        antes = Environment.GetEnvironmentVariable(nome);
        Environment.SetEnvironmentVariable(nome, valor);
    }

    public void Dispose() => Environment.SetEnvironmentVariable(nome, antes);
}

/// <summary>Recebe o progresso sem bloquear (a UI de verdade também não pode travar o agente).</summary>
public sealed class Coletor : IProgress<ProgressoAnalise>
{
    readonly List<ProgressoAnalise> itens = new();

    public void Report(ProgressoAnalise value) { lock (itens) itens.Add(value); }

    public List<ProgressoAnalise> Todos() { lock (itens) return itens.ToList(); }

    public List<string> Linhas() { lock (itens) return itens.Where(i => i.Linha is not null).Select(i => i.Linha!).ToList(); }

    public int QuantidadeDeLinhas { get { lock (itens) return itens.Count(i => i.Linha is not null); } }
}

/// <summary>Caminhos do repositório usados pelos testes. Nada aqui grava em %APPDATA%, %LOCALAPPDATA% nem %TEMP% (o shell do Claude é virtualizado).</summary>
public static class Ambiente
{
    [ModuleInitializer]
    internal static void TravarProgramas()
    {
        // Trava de teste (desenho §12.12): nenhum programa fora do agente falso é iniciado.
        ExecucaoProcesso.SomenteExecutaveisEm = Path.GetDirectoryName(AgenteFalso);
    }

    public static string PastaTestes { get; } = Localizar();

    public static string Corpus(string nome) => Path.Combine(PastaTestes, "corpus", nome);

    public static PastaTeste PastaDeTeste(string nome)
    {
        var caminho = Path.Combine(PastaTestes, "saida", "agentes", nome + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(caminho);
        return new PastaTeste(caminho);
    }

    /// <summary>O <c>AgenteFalso.exe</c> no mesmo caminho de saída do projeto de teste (configuração e plataforma): com <c>dotnet test</c> no projeto,
    /// <c>bin\Debug\net10.0</c>; pela solução (<c>build-release.ps1</c>, Release|x64), <c>bin\x64\Release\net10.0</c>.</summary>
    public static string AgenteFalso { get; } = Path.Combine(PastaTestes, "AgenteFalso", "bin",
        Path.GetRelativePath(Path.Combine(PastaTestes, "Pmvni.Agentes.Testes", "bin"), AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)), "AgenteFalso.exe");

    public static string Cenario(string nome) => Path.Combine(Corpus("agentes"), nome + ".json");

    /// <summary>Cópia do cenário na pasta do teste, com o que o teste quiser mudar; o relatório fica apontado pelo caminho absoluto.</summary>
    public static string CenarioAjustado(string pasta, string nome, Action<JsonObject> ajustar)
    {
        var obj = JsonNode.Parse(File.ReadAllText(Cenario(nome), Encoding.UTF8))!.AsObject();
        obj["relatorio"] = Path.Combine(Corpus("agentes"), "relatorio_dados_0001.json");
        ajustar(obj);
        var destino = Path.Combine(pasta, nome + "-ajustado.json");
        File.WriteAllText(destino, obj.ToJsonString(), new UTF8Encoding(false));
        return destino;
    }

    /// <summary>Processo vazio no modelo do kit, dentro da pasta do teste.</summary>
    public static Processo NovoProcesso(string pasta)
    {
        var raiz = Path.Combine(pasta, "processos");
        Directory.CreateDirectory(raiz);
        return CriacaoProcesso.Criar(raiz, 1, 2099, "Fulano de Exemplo (Ficticio)").Processo;
    }

    /// <summary>Peças que apontam todas para o agente falso (ele faz o papel de codex, claude e python). O plugin único na versão mínima do Editor
    /// (analise 4.0.0, Tarefa 18), com os controladores em <c>skills\analise\scripts\rotas\&lt;rota&gt;</c>; o <c>config.toml</c> do Codex fica em
    /// <c>&lt;teste&gt;\codex-home</c> (sem ele, a rota "herdar" é a OpenAI).</summary>
    public static Pecas PecasFalsas(string pastaTeste, bool pluginHabilitado = true) => new(
        Codex: AgenteFalso, Claude: AgenteFalso, Python: AgenteFalso, PythonPeloLauncher: false,
        ProRuntime: Controlador(pastaTeste, "claude", "pro_runtime.py"),
        VersaoAnalisePro: "4.0.0", VersaoAnaliseProKit: "4.0.0", PluginAnaliseHabilitado: pluginHabilitado, VersaoPluginAnalise: "4.0.0", ApiPermitidaNoKit: false)
    {
        CodexHome = Path.Combine(pastaTeste, "codex-home"),
        Analise62 = Controlador(pastaTeste, "codex", "executar_analise_62.py"),
    };

    /// <summary>O controlador de uma rota no plugin analise 4.0.0 do <c>codex-home</c> do teste.</summary>
    public static string Controlador(string pastaTeste, string rota, string script) =>
        Path.Combine(pastaTeste, "codex-home", "plugins", "cache", "pmvni-kit", "analise", "4.0.0", "skills", "analise", "scripts", "rotas", rota, script);

    /// <summary>O que o código do editor lê do disco do agente falso depois de rodar: confere se o processo ainda vive.</summary>
    public static bool Vivo(int pid)
    {
        try { return !Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>Cria uma junção (ou, se não der, um link simbólico) <paramref name="link"/> → <paramref name="alvo"/>; false se este PC não deixa criar nenhum dos dois.
    /// <c>mklink /J</c> não pede administrador.</summary>
    public static bool CriarJuncao(string link, string alvo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var psi = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in new[] { "/c", "mklink", "/J", link, alvo }) psi.ArgumentList.Add(a);
        try
        {
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode == 0 && Directory.Exists(link)) return true;
        }
        catch (System.ComponentModel.Win32Exception) { }
        try { Directory.CreateSymbolicLink(link, alvo); return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    public static bool EsperarAte(Func<bool> condicao, TimeSpan limite)
    {
        var fim = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < fim)
        {
            if (condicao()) return true;
            Thread.Sleep(50);
        }
        return condicao();
    }

    public static async Task<T> Com<T>(Task<T> tarefa, TimeSpan limite)
    {
        var primeira = await Task.WhenAny(tarefa, Task.Delay(limite));
        if (primeira != tarefa) Assert.Fail("A tarefa não terminou em " + limite.TotalSeconds + " s.");
        return await tarefa;
    }

    /// <summary>Raiz do kit PMVNI (somente leitura): <c>PMVNI_KIT_RAIZ</c> ou o padrão.</summary>
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

    static string Localizar()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var c = Path.Combine(d.FullName, "tests");
            if (Directory.Exists(Path.Combine(c, "corpus"))) return c;
        }
        throw new InvalidOperationException("tests/corpus não encontrada a partir de " + AppContext.BaseDirectory);
    }
}
