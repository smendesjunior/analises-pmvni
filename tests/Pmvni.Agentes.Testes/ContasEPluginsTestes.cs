using System.Text;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Ajustes › Inteligência artificial (correções 2026-11-15): a conta lida neste computador (sem nunca devolver o token) e as versões dos plugins
/// do kit instaladas no Codex e no Claude Code, sobre pastas e arquivos fictícios.</summary>
public class ContasEPluginsTestes
{
    const string TokenFalso = "token-ficticio-que-nunca-sai";

    static string Arquivo(string caminho, string conteudo)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo);
        return caminho;
    }

    static string Jwt(string json)
    {
        static string B64(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return B64("{\"alg\":\"none\"}") + "." + B64(json) + ".assinatura";
    }

    static OpcoesLocalizacao Opcoes(string t, bool comClaude = true) => new(
        Path.Combine(t, "local"), Path.Combine(t, "codex-home"), [Path.Combine(t, "bin")], Path.Combine(t, "kit"), null, null,
        comClaude ? Path.Combine(t, "claude") : null, comClaude ? Path.Combine(t, "claude.json") : null);

    // ---------- conta ----------

    [Fact]
    public void claude_com_sessao_mostra_nome_email_e_plano_sem_o_token()
    {
        var perfil = """{ "oauthAccount": { "displayName": "Fulana de Tal", "emailAddress": "fulana@exemplo.test", "organizationType": "claude_pro" } }""";
        var credenciais = $$"""{ "claudeAiOauth": { "accessToken": "{{TokenFalso}}", "refreshToken": "{{TokenFalso}}", "subscriptionType": "max" } }""";
        var c = ContasLocais.Claude(perfil, credenciais);
        Assert.Equal(new ContaLocal(true, "Fulana de Tal", "fulana@exemplo.test", "Claude Max"), c);
        Assert.DoesNotContain(TokenFalso, c.ToString());
    }

    [Fact]
    public void claude_sem_token_ou_com_arquivo_estragado_nao_esta_conectado()
    {
        var perfil = """{ "oauthAccount": { "displayName": "Fulana", "emailAddress": "fulana@exemplo.test" } }""";
        Assert.Equal(ContaLocal.Nenhuma, ContasLocais.Claude(perfil, null));
        Assert.Equal(ContaLocal.Nenhuma, ContasLocais.Claude(perfil, """{ "claudeAiOauth": { "accessToken": "" } }"""));
        Assert.Equal(ContaLocal.Nenhuma, ContasLocais.Claude(perfil, "{ não é json"));
        // Sessão sem perfil: conectado, sem nome nem e-mail; plano pelo tipo da organização quando não há subscriptionType.
        Assert.Equal(new ContaLocal(true, null, null, null), ContasLocais.Claude(null, $$"""{ "claudeAiOauth": { "accessToken": "{{TokenFalso}}" } }"""));
        Assert.Equal("Claude Pro", ContasLocais.Claude("""{ "oauthAccount": { "organizationType": "claude_pro" } }""",
            $$"""{ "claudeAiOauth": { "accessToken": "{{TokenFalso}}" } }""").Assinatura);
    }

    [Fact]
    public void codex_le_as_declaracoes_do_id_token_sem_o_token()
    {
        var jwt = Jwt("""{ "email": "ciclano@exemplo.test", "name": "Ciclano", "https://api.openai.com/auth": { "chatgpt_plan_type": "plus" } }""");
        var auth = $$"""{ "OPENAI_API_KEY": null, "tokens": { "access_token": "{{TokenFalso}}", "id_token": "{{jwt}}", "refresh_token": "{{TokenFalso}}" } }""";
        var c = ContasLocais.Codex(auth);
        Assert.Equal(new ContaLocal(true, "Ciclano", "ciclano@exemplo.test", "ChatGPT Plus"), c);
        Assert.DoesNotContain(TokenFalso, c.ToString());

        Assert.Equal(ContaLocal.Nenhuma, ContasLocais.Codex("""{ "tokens": { "id_token": "x.y.z" } }"""));        // sem access_token
        Assert.Equal(new ContaLocal(true, null, null, null), ContasLocais.Codex($$"""{ "tokens": { "access_token": "a", "id_token": "nao-e-jwt" } }"""));
        Assert.Equal(new ContaLocal(true, null, null, null), ContasLocais.Codex($$"""{ "tokens": { "access_token": "a", "id_token": "a.%%%.c" } }"""));
    }

    [Theory]
    [InlineData(Provedor.ChatGpt, "self_serve_business_prolite", "ChatGPT Business")]
    [InlineData(Provedor.ChatGpt, "team", "ChatGPT Business")]
    [InlineData(Provedor.ChatGpt, "pro", "ChatGPT Pro")]
    [InlineData(Provedor.ChatGpt, "go", "ChatGPT Go")]
    [InlineData(Provedor.ChatGpt, "desconhecido", null)]
    [InlineData(Provedor.Claude, "claude_max", "Claude Max")]
    [InlineData(Provedor.Claude, "team", "Claude Team")]
    [InlineData(Provedor.Claude, null, null)]
    public void nome_da_assinatura(Provedor p, string? bruto, string? esperado) => Assert.Equal(esperado, ContasLocais.NomeDaAssinatura(p, bruto));

    [Fact]
    public void ler_pelas_opcoes_usa_so_as_pastas_injetadas()
    {
        using var t = Ambiente.PastaDeTeste("contas");
        Assert.Equal(ContaLocal.Nenhuma, ContasLocais.Ler(Provedor.ChatGpt, Opcoes(t)));
        Assert.Equal(ContaLocal.Nenhuma, ContasLocais.Ler(Provedor.Claude, Opcoes(t)));

        Arquivo(Path.Combine(t, "codex-home", "auth.json"), $$"""{ "tokens": { "access_token": "{{TokenFalso}}" } }""");
        Arquivo(Path.Combine(t, "claude", ".credentials.json"), $$"""{ "claudeAiOauth": { "accessToken": "{{TokenFalso}}", "subscriptionType": "pro" } }""");
        Arquivo(Path.Combine(t, "claude.json"), """{ "oauthAccount": { "emailAddress": "fulana@exemplo.test" } }""");
        Assert.True(ContasLocais.Ler(Provedor.ChatGpt, Opcoes(t)).Conectado);
        Assert.Equal(new ContaLocal(true, null, "fulana@exemplo.test", "Claude Pro"), ContasLocais.Ler(Provedor.Claude, Opcoes(t)));
        Assert.Equal(ContaLocal.Nenhuma, ContasLocais.Ler(Provedor.Claude, Opcoes(t, comClaude: false)));   // o roteiro não lê o Claude de verdade
    }

    // ---------- plugins ----------

    [Fact]
    public void plugin_no_codex_maior_versao_com_a_data_da_pasta()
    {
        using var t = Ambiente.PastaDeTeste("plugins-codex");
        var cache = Path.Combine(t, "codex-home", "plugins", "cache", "pmvni-kit");
        Assert.Null(PluginsInstalados.NoCodex(Path.Combine(t, "codex-home"), PluginsInstalados.Analise));
        Directory.CreateDirectory(Path.Combine(cache, "analise", "2.4.0"));
        Directory.CreateDirectory(Path.Combine(cache, "analise", "2.10.0"));      // por versão, não por texto
        Directory.CreateDirectory(Path.Combine(cache, "analise", "rascunho"));    // nome não numérico: ignorado
        var nova = Path.Combine(cache, "analise", "2.10.0");
        var data = new DateTime(2026, 11, 15, 16, 50, 0, DateTimeKind.Utc);
        Directory.SetCreationTimeUtc(nova, data);
        var p = PluginsInstalados.NoCodex(Path.Combine(t, "codex-home"), PluginsInstalados.Analise);
        Assert.Equal("2.10.0", p!.Versao);
        Assert.Equal(new DateTimeOffset(data), p.Em);
        Assert.Null(PluginsInstalados.NoCodex(Path.Combine(t, "codex-home"), PluginsInstalados.AnalisePro));
    }

    [Fact]
    public void plugin_no_claude_pelo_installed_plugins()
    {
        const string Json = """
        {
          "version": 2,
          "plugins": {
            "analise@pmvni-local": [
              { "scope": "user", "version": "2.4.0", "installedAt": "2026-11-01T10:00:00.000Z", "lastUpdated": "2026-11-01T10:00:00.000Z" },
              { "scope": "project", "version": "2.5.0", "installedAt": "2026-11-15T16:50:24.307Z", "lastUpdated": "2026-11-15T16:50:24.307Z" }
            ],
            "analise-pro@pmvni-local": [ { "version": "9.9.9" } ],
            "despacho@pmvni-local": [ { "version": "2.3.0" } ]
          }
        }
        """;
        var p = PluginsInstalados.DoRegistroDoClaude(Json, PluginsInstalados.Analise);
        Assert.Equal("2.5.0", p!.Versao);                                  // não confunde com analise-pro nem despacho
        Assert.Equal(new DateTimeOffset(2026, 11, 15, 16, 50, 24, 307, TimeSpan.Zero), p.Em);

        // Formato antigo (um objeto por chave), sem lastUpdated: vale installedAt; sem data: null.
        Assert.Equal(new PluginInstalado("1.0.0", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)),
            PluginsInstalados.DoRegistroDoClaude("""{ "plugins": { "analise@x": { "version": "1.0.0", "installedAt": "2026-01-02T00:00:00Z" } } }""", "analise"));
        Assert.Equal(new PluginInstalado("1.0.0", null), PluginsInstalados.DoRegistroDoClaude("""{ "plugins": { "analise@x": [ { "version": "1.0.0" } ] } }""", "analise"));
        Assert.Null(PluginsInstalados.DoRegistroDoClaude("""{ "plugins": { "despacho@x": [ { "version": "1.0.0" } ] } }""", "analise"));
        Assert.Null(PluginsInstalados.DoRegistroDoClaude("estragado", "analise"));

        using var t = Ambiente.PastaDeTeste("plugins-claude");
        Assert.Null(PluginsInstalados.NoClaude(Path.Combine(t, "claude"), "analise"));
        Assert.Null(PluginsInstalados.NoClaude(null, "analise"));
        Arquivo(Path.Combine(t, "claude", "plugins", "installed_plugins.json"), Json);
        Assert.Equal("2.5.0", PluginsInstalados.NoClaude(Path.Combine(t, "claude"), "analise")!.Versao);
    }

    [Theory]
    [InlineData(null, "2.5.0", null)]        // primeira vez: nada a avisar
    [InlineData("2.5.0", "2.5.0", null)]     // a mesma
    [InlineData("2.4.0", "2.5.0", "2.4.0")]  // atualizou
    [InlineData("2.5.0", "2.4.0", "2.5.0")]  // voltou para uma anterior também avisa
    [InlineData("2.5.0", null, null)]        // sumiu: nada a avisar
    public void versao_anterior_se_mudou(string? vista, string? atual, string? esperado) =>
        Assert.Equal(esperado, PluginsInstalados.VersaoAnteriorSeMudou(vista, atual));
}
