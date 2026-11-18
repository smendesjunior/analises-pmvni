using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Ambiente do processo filho (desenho §12.9): chaves herdadas saem sempre; só a do provedor entra, e só no modo chave.</summary>
public class AmbienteFilhoTestes
{
    static Dictionary<string, string?> Herdado() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["PATH"] = @"C:\bin", ["USERPROFILE"] = @"C:\Users\x", ["OPENAI_API_KEY"] = "herdada-1", ["CODEX_API_KEY"] = "herdada-2",
        ["ANTHROPIC_API_KEY"] = "herdada-3", ["ANTHROPIC_AUTH_TOKEN"] = "herdada-4", ["AGENTE_FALSO_CENARIO"] = "x.json",
        ["PMVNI_ANALISE_PRO_ROTA"] = "api",
    };

    [Fact]
    public void provedores_e_variaveis()
    {
        Assert.Equal("ChatGPT", Provedores.Nome(Provedor.ChatGpt));
        Assert.Equal("Claude", Provedores.Nome(Provedor.Claude));
        Assert.Equal("chatgpt", Provedores.Sufixo(Provedor.ChatGpt));
        Assert.Equal("claude", Provedores.Sufixo(Provedor.Claude));
        Assert.Equal("CODEX_API_KEY", Provedores.VariavelChave(Provedor.ChatGpt));
        Assert.Equal("ANTHROPIC_API_KEY", Provedores.VariavelChave(Provedor.Claude));
        // 1.1.0: as chaves do Google também nunca são herdadas (terceiro provedor, Gemini); as quatro de antes continuam na lista.
        Assert.Equal(new[] { "OPENAI_API_KEY", "CODEX_API_KEY", "ANTHROPIC_API_KEY", "ANTHROPIC_AUTH_TOKEN", "GEMINI_API_KEY", "GOOGLE_API_KEY" }, Provedores.VariaveisRemovidas);
    }

    [Theory]
    [InlineData(Provedor.ChatGpt)]
    [InlineData(Provedor.Claude)]
    public void conta_remove_as_chaves_herdadas(Provedor p)
    {
        var env = Herdado();
        AmbienteFilho.Aplicar(env, p, ModoAcesso.Conta, null);
        // A rota herdada nunca passa; no Claude, o editor declara "conta" (análise pro 1.6.4+).
        var esperadas = p == Provedor.Claude
            ? new[] { "AGENTE_FALSO_CENARIO", "PATH", "PMVNI_ANALISE_PRO_ROTA", "USERPROFILE" }
            : new[] { "AGENTE_FALSO_CENARIO", "PATH", "USERPROFILE" };
        Assert.Equal(esperadas, env.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
        if (p == Provedor.Claude) Assert.Equal("conta", env[PoliticaKit.VariavelRota]);
        Assert.Equal(@"C:\bin", env["PATH"]);
        Assert.Equal(@"C:\Users\x", env["USERPROFILE"]);
    }

    [Fact]
    public void conta_ignora_uma_chave_que_chegue_por_engano()
    {
        var env = Herdado();
        AmbienteFilho.Aplicar(env, Provedor.ChatGpt, ModoAcesso.Conta, "chave-que-nao-deve-passar");
        Assert.DoesNotContain(env.Values, v => v == "chave-que-nao-deve-passar");
        Assert.False(env.ContainsKey("CODEX_API_KEY"));
    }

    [Fact]
    public void chave_so_na_variavel_do_provedor()
    {
        var env = Herdado();
        AmbienteFilho.Aplicar(env, Provedor.ChatGpt, ModoAcesso.Chave, "minha-chave");
        Assert.Equal("minha-chave", env["CODEX_API_KEY"]);
        Assert.False(env.ContainsKey("OPENAI_API_KEY"));
        Assert.False(env.ContainsKey("ANTHROPIC_API_KEY"));
        Assert.False(env.ContainsKey("ANTHROPIC_AUTH_TOKEN"));
        Assert.Equal(@"C:\bin", env["PATH"]);

        var env2 = Herdado();
        AmbienteFilho.Aplicar(env2, Provedor.Claude, ModoAcesso.Chave, "outra-chave");
        Assert.Equal("outra-chave", env2["ANTHROPIC_API_KEY"]);
        Assert.False(env2.ContainsKey("CODEX_API_KEY"));
        Assert.False(env2.ContainsKey("OPENAI_API_KEY"));
        Assert.False(env2.ContainsKey("ANTHROPIC_AUTH_TOKEN"));
        Assert.Equal("api", env2[PoliticaKit.VariavelRota]);             // a rota API vai declarada junto com a chave
        Assert.False(env.ContainsKey(PoliticaKit.VariavelRota));         // o ChatGPT não recebe a rota do análise pro
    }

    [Fact]
    public void nomes_em_outra_caixa_tambem_saem()
    {
        var env = new Dictionary<string, string?> { ["openai_api_key"] = "x", ["Codex_Api_Key"] = "y", ["PATH"] = "p" };
        AmbienteFilho.Aplicar(env, Provedor.ChatGpt, ModoAcesso.Conta, null);
        Assert.Equal(new[] { "PATH" }, env.Keys.ToArray());
    }

    [Fact]
    public void modo_chave_sem_chave_e_erro()
    {
        Assert.Throws<ArgumentException>(() => AmbienteFilho.Aplicar(Herdado(), Provedor.ChatGpt, ModoAcesso.Chave, null));
        Assert.Throws<ArgumentException>(() => AmbienteFilho.Aplicar(Herdado(), Provedor.ChatGpt, ModoAcesso.Chave, "  "));
    }
}
