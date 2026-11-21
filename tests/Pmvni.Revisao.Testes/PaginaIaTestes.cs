using System.Text.Json.Nodes;
using Pmvni.Agentes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Ajustes › Inteligência artificial no padrão do WinStatusBar (correções 2026-11-15): os textos dos cartões e o acompanhamento das versões
/// dos plugins do kit (última vista em ajustes.json; "Atualizado desde a última vez" quando muda).</summary>
public class PaginaIaTestes
{
    [Fact]
    public void ultima_versao_vista_avisa_so_quando_muda_e_fica_ate_fechar()
    {
        var vistos = new Dictionary<string, string>();
        var a = new AcompanhamentoPlugins();
        var chave = AcompanhamentoPlugins.Chave(Provedor.ChatGpt, PluginsInstalados.Analise);
        Assert.Equal("chatgpt.analise", chave);
        Assert.Equal("claude.analise-pro", AcompanhamentoPlugins.Chave(Provedor.Claude, PluginsInstalados.AnalisePro));

        Assert.True(a.Registrar(vistos, chave, "2.4.0"));       // primeira vez: guarda, sem aviso
        Assert.Null(a.Anterior(chave));
        Assert.False(a.Registrar(vistos, chave, "2.4.0"));      // a mesma: nada a salvar
        Assert.False(a.Registrar(vistos, chave, null));         // sumiu: a última vista fica
        Assert.Equal("2.4.0", vistos[chave]);

        Assert.True(a.Registrar(vistos, chave, "2.5.0"));       // atualizou
        Assert.Equal("2.5.0", vistos[chave]);
        Assert.Equal("2.4.0", a.Anterior(chave));
        Assert.False(a.Registrar(vistos, chave, "2.5.0"));      // ver de novo na mesma sessão: o aviso continua
        Assert.Equal("2.4.0", a.Anterior(chave));

        Assert.Null(new AcompanhamentoPlugins().Anterior(chave));   // editor reaberto: já foi visto, sem aviso
        Assert.False(new AcompanhamentoPlugins().Registrar(vistos, chave, "2.5.0"));
    }

    [Fact]
    public void plugins_vistos_vao_e_voltam_do_ajustes_json_e_restaurar_nao_apaga()
    {
        using var pasta = Ambiente.PastaDeTeste("plugins-vistos");
        var a = Ajustes.Carregar(pasta);
        Assert.Empty(a.PluginsVistos);
        a.PluginsVistos["chatgpt.analise"] = "2.5.0";
        a.PluginsVistos["claude.analise-pro"] = "1.6.1";
        a.Salvar(pasta);
        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(pasta, Ajustes.NomeArquivo)))!;
        Assert.Equal("2.5.0", j["plugins_vistos"]!["chatgpt.analise"]!.GetValue<string>());

        var b = Ajustes.Carregar(pasta);
        Assert.Equal(a.PluginsVistos, b.PluginsVistos);
        b.RestaurarPadrao();
        Assert.Equal(2, b.PluginsVistos.Count);

        // Valor estranho no arquivo: fica de fora.
        File.WriteAllText(Path.Combine(pasta, Ajustes.NomeArquivo), """{ "plugins_vistos": { "chatgpt.analise": 3, "claude.analise": "", "claude.analise-pro": "1.7.0" } }""");
        Assert.Equal(new Dictionary<string, string> { ["claude.analise-pro"] = "1.7.0" }, Ajustes.Carregar(pasta).PluginsVistos);
    }

    [Fact]
    public void textos_da_conta()
    {
        var nenhuma = ContaLocal.Nenhuma;
        Assert.Equal("Entrar", TextosAgentes.AcaoDaConta(nenhuma));
        Assert.Equal("Não conectado", TextosAgentes.NomeDaConta(nenhuma));
        Assert.Equal("", TextosAgentes.EmailDaConta(nenhuma));
        Assert.Equal("—", TextosAgentes.Assinatura(nenhuma));

        var completa = new ContaLocal(true, "Fulana de Tal", "fulana@exemplo.test", "Claude Max");
        Assert.Equal("Sair", TextosAgentes.AcaoDaConta(completa));
        Assert.Equal("Fulana de Tal", TextosAgentes.NomeDaConta(completa));
        Assert.Equal("fulana@exemplo.test", TextosAgentes.EmailDaConta(completa));
        Assert.Equal("Claude Max", TextosAgentes.Assinatura(completa));

        var soEmail = new ContaLocal(true, null, "fulana@exemplo.test", null);
        Assert.Equal("fulana@exemplo.test", TextosAgentes.NomeDaConta(soEmail));
        Assert.Equal("", TextosAgentes.EmailDaConta(soEmail));                  // o e-mail já está no nome
        Assert.Equal("Não informada", TextosAgentes.Assinatura(soEmail));

        var semNada = new ContaLocal(true, null, null, null);
        Assert.Equal("Conectado", TextosAgentes.NomeDaConta(semNada));
        Assert.Equal("e-mail não informado pelo programa", TextosAgentes.EmailDaConta(semNada));

        Assert.Equal("Conta OpenAI", TextosAgentes.RotuloConta(Provedor.ChatGpt));
        Assert.Equal("Conta Anthropic", TextosAgentes.RotuloConta(Provedor.Claude));
        Assert.StartsWith("https://", TextosAgentes.EnderecoConta(Provedor.Claude));
        Assert.Equal("Sair do Claude?", TextosAgentes.PerguntaSair(Provedor.Claude));
    }

    [Fact]
    public void textos_dos_plugins()
    {
        Assert.Equal("Análise — rota ChatGPT", TextosAgentes.TituloPlugin(PluginsInstalados.Analise));
        Assert.Equal("Análise — rota Claude", TextosAgentes.TituloPlugin(PluginsInstalados.AnalisePro));
        Assert.Equal("No Codex, habilitado", TextosAgentes.OndePlugin(Provedor.ChatGpt, PluginsInstalados.Analise, true));
        Assert.Equal("No Codex, não habilitado", TextosAgentes.OndePlugin(Provedor.ChatGpt, PluginsInstalados.Analise, false));
        Assert.Equal("No Claude Code", TextosAgentes.OndePlugin(Provedor.Claude, PluginsInstalados.Analise, true));
        Assert.Equal("No Codex (é o que o Claude usa nas análises)", TextosAgentes.OndePlugin(Provedor.Claude, PluginsInstalados.AnalisePro, true));

        Assert.Equal("Não instalado", TextosAgentes.SeloPlugin(null));
        Assert.Equal("Versão 2.5.0", TextosAgentes.SeloPlugin(new PluginInstalado("2.5.0", null)));
        Assert.Equal("No Claude Code · não instalado", TextosAgentes.DescricaoPlugin("No Claude Code", null));
        Assert.Equal("No Claude Code · data de instalação desconhecida", TextosAgentes.DescricaoPlugin("No Claude Code", new PluginInstalado("2.5.0", null)));
        var em = new DateTimeOffset(2026, 11, 15, 16, 50, 0, TimeSpan.Zero);
        var local = em.ToLocalTime();
        Assert.Equal($"No Codex, habilitado · instalado ou atualizado em {local:dd/MM/yyyy} às {local:HH:mm}",
            TextosAgentes.DescricaoPlugin("No Codex, habilitado", new PluginInstalado("2.5.0", em)));
        Assert.Equal("Atualizado desde a última vez (antes: 2.4.0)", TextosAgentes.AtualizadoDesde("2.4.0"));
    }
}
