using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Pmvni.Agentes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Ajustes locais do app (<c>&lt;dados&gt;\ajustes.json</c>, Tarefa 8): lembram módulo e aba; arquivo corrompido volta ao padrão.</summary>
public class AjustesTestes
{
    [Fact]
    public void ajustes_lembram_modulo_e_aba()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes");
        var a = Ajustes.Carregar(pasta);
        Assert.Equal(Modulo.Relatorios, a.Modulo);
        Assert.Equal("sistema", a.Tema);
        Assert.Equal(30, a.IntervaloAutosaveS);
        Assert.True(a.MarcasRevisao);
        Assert.Empty(a.Recentes);

        a.Modulo = Modulo.Ajustes;
        a.AbaPorModulo[Modulo.Relatorios] = "Ajuda";
        a.AbaPorModulo[Modulo.Ajustes] = "Arquivo";
        a.Tema = "escuro";
        a.IntervaloAutosaveS = 60;
        a.MarcasRevisao = false;
        a.Recentes.Add(@"C:\x\a.rpmvni");
        a.PastaProcessos = @"C:\processos";
        a.PastaQgis = @"C:\base qgis";
        a.LeisFavoritas.AddRange(["lg_1", "lg_2"]);
        a.Salvar(pasta);
        Assert.Equal(@"C:\base qgis", Ajustes.Carregar(pasta).PastaQgis);
        Assert.Equal(new[] { "lg_1", "lg_2" }, Ajustes.Carregar(pasta).LeisFavoritas);

        Assert.True(File.Exists(Path.Combine(pasta, "ajustes.json")));
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));
        var b = Ajustes.Carregar(pasta);
        Assert.Equal(Modulo.Ajustes, b.Modulo);
        Assert.Equal("Ajuda", b.AbaPorModulo[Modulo.Relatorios]);
        Assert.Equal("Arquivo", b.AbaPorModulo[Modulo.Ajustes]);
        Assert.Equal("escuro", b.Tema);
        Assert.Equal(60, b.IntervaloAutosaveS);
        Assert.False(b.MarcasRevisao);
        Assert.Equal(new[] { @"C:\x\a.rpmvni" }, b.Recentes);
        Assert.Equal(@"C:\processos", b.PastaProcessos);
    }

    [Fact]
    public void ajustes_corrompidos_voltam_ao_padrao_e_guardam_o_arquivo()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-corrompidos");
        var arquivo = Path.Combine(pasta, "ajustes.json");
        File.WriteAllText(arquivo, "{ \"modulo\": \"Relatorios\", ");

        var a = Ajustes.Carregar(pasta);

        Assert.Equal(Modulo.Relatorios, a.Modulo);
        Assert.Equal(30, a.IntervaloAutosaveS);
        Assert.False(File.Exists(arquivo));
        var guardado = Path.Combine(pasta, "ajustes.corrompido.json");
        Assert.Equal("{ \"modulo\": \"Relatorios\", ", File.ReadAllText(guardado));
    }

    [Fact]
    public void valores_fora_do_permitido_voltam_ao_padrao()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-invalidos");
        File.WriteAllText(Path.Combine(pasta, "ajustes.json"),
            "{\"modulo\":\"Inexistente\",\"tema\":\"roxo\",\"intervalo_autosave_s\":7,\"aba_por_modulo\":{\"Nada\":\"x\",\"Relatorios\":\"Exibir\"}}");

        var a = Ajustes.Carregar(pasta);

        Assert.Equal(Modulo.Relatorios, a.Modulo);
        Assert.Equal("sistema", a.Tema);
        Assert.Equal(30, a.IntervaloAutosaveS);
        Assert.Equal("Exibir", Assert.Single(a.AbaPorModulo).Value);
    }

    [Fact]
    public void pasta_de_processos_efetiva_usa_o_padrao_do_usuario_ate_escolherem_outra()
    {
        var padrao = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "PMVNI", "SIDNEY JUNIOR", "PROCESSOS");
        var a = new Ajustes();
        Assert.Null(a.PastaProcessos);
        Assert.Equal(padrao, a.PastaProcessosEfetiva);
        Assert.Equal(padrao, Ajustes.PastaProcessosPadrao);

        a.PastaProcessos = "   ";
        Assert.Equal(padrao, a.PastaProcessosEfetiva);

        a.PastaProcessos = @"C:\outra\pasta";
        Assert.Equal(@"C:\outra\pasta", a.PastaProcessosEfetiva);

        // Voltar ao padrão é pôr null; o arquivo guarda a escolha e o padrão não é gravado como se fosse escolha.
        using var pasta = Ambiente.PastaDeTeste("ajustes-pasta-processos");
        new Ajustes().Salvar(pasta);
        Assert.Null(Ajustes.Carregar(pasta).PastaProcessos);
        Assert.Equal(padrao, Ajustes.Carregar(pasta).PastaProcessosEfetiva);
    }

    [Fact]
    public void ajustes_antigo_com_pasta_de_legislacao_e_lido_sem_erro_e_a_chave_e_ignorada()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-legislacao-antiga");
        File.WriteAllText(Path.Combine(pasta, "ajustes.json"),
            "{\"modulo\":\"Processos\",\"tema\":\"escuro\",\"pasta_legislacao\":\"C:\\\\legislacao\",\"pasta_processos\":\"C:\\\\processos\"}");

        var a = Ajustes.Carregar(pasta);

        Assert.Equal(Modulo.Processos, a.Modulo);
        Assert.Equal("escuro", a.Tema);
        Assert.Equal(@"C:\processos", a.PastaProcessos);
        Assert.False(File.Exists(Path.Combine(pasta, "ajustes.corrompido.json")));
        // A chave velha não volta ao arquivo na próxima gravação.
        a.Salvar(pasta);
        Assert.DoesNotContain("pasta_legislacao", File.ReadAllText(Path.Combine(pasta, "ajustes.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void recentes_no_maximo_10_sem_repetir()
    {
        var a = new Ajustes();
        for (var i = 1; i <= 12; i++) a.AdicionarRecente(@"C:\x\r" + i + ".rpmvni");
        Assert.Equal(10, a.Recentes.Count);
        Assert.Equal(@"C:\x\r12.rpmvni", a.Recentes[0]);
        Assert.Equal(@"C:\x\r3.rpmvni", a.Recentes[^1]); // r1 e r2 saíram

        // Repetir (ignorando maiúsculas) leva ao topo, sem duplicar, e guarda o caminho como foi aberto agora.
        a.AdicionarRecente(@"C:\X\R5.RPMVNI");
        Assert.Equal(10, a.Recentes.Count);
        Assert.Equal(@"C:\X\R5.RPMVNI", a.Recentes[0]);
        Assert.Single(a.Recentes, r => string.Equals(r, @"C:\x\r5.rpmvni", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(@"C:\x\r12.rpmvni", a.Recentes[1]);

        // Caminho vazio não entra; os recentes sobrevivem ao salvar e carregar.
        a.AdicionarRecente("  ");
        Assert.Equal(10, a.Recentes.Count);
        using var pasta = Ambiente.PastaDeTeste("recentes-salvar");
        a.Salvar(pasta);
        Assert.Equal(a.Recentes, Ajustes.Carregar(pasta).Recentes);
    }

    [Fact]
    public void recentes_somem_quando_o_arquivo_some()
    {
        using var pasta = Ambiente.PastaDeTeste("recentes-somem");
        string Criar(string nome) { var c = Path.Combine(pasta, nome); File.WriteAllText(c, "x"); return c; }
        var a1 = Criar("a.rpmvni");
        var b = Criar("b.rpmvni");
        var c = Criar("c.rpmvni");
        var a = new Ajustes();
        a.AdicionarRecente(a1);
        a.AdicionarRecente(b);
        a.AdicionarRecente(c);
        Assert.Equal(new[] { c, b, a1 }, a.RecentesExistentes());

        File.Delete(b);

        Assert.Equal(new[] { c, a1 }, a.RecentesExistentes());
        // Nada é desabilitado nem alterado: a lista guardada continua como está (um arquivo numa unidade offline pode voltar).
        Assert.Equal(3, a.Recentes.Count);
    }
}

/// <summary>A pasta do kit PMVNI nos Ajustes (Tarefa 16): mexe em <c>PMVNI_KIT_RAIZ</c>, então roda sem paralelismo.</summary>
[Collection("Ambiente")]
public class AjustesKitTestes
{
    [Fact]
    public void ajustes_guardam_a_pasta_do_kit()
    {
        var padrao = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "PMVNI", "SIDNEY JUNIOR",
            "KIT DE TREINAMENTO DE IA (PMVNI)");
        Assert.Equal(padrao, Ajustes.PastaKitPadrao);
        var antes = Environment.GetEnvironmentVariable("PMVNI_KIT_RAIZ");
        try
        {
            Environment.SetEnvironmentVariable("PMVNI_KIT_RAIZ", null);
            var a = new Ajustes();
            Assert.Null(a.PastaKit);
            Assert.Equal(padrao, a.PastaKitEfetiva);
            a.PastaKit = "  ";
            Assert.Equal(padrao, a.PastaKitEfetiva);
            a.PastaKit = @"C:\outro\kit";
            Assert.Equal(@"C:\outro\kit", a.PastaKitEfetiva);

            // PMVNI_KIT_RAIZ (testes, roteiro) vence a escolha dos Ajustes, que vence o padrão.
            Environment.SetEnvironmentVariable("PMVNI_KIT_RAIZ", @"C:\kit-dos-testes");
            Assert.Equal(@"C:\kit-dos-testes", a.PastaKitEfetiva);
            Environment.SetEnvironmentVariable("PMVNI_KIT_RAIZ", "");
            Assert.Equal(@"C:\outro\kit", a.PastaKitEfetiva);

            using var pasta = Ambiente.PastaDeTeste("ajustes-pasta-kit");
            a.Salvar(pasta);
            Assert.Equal(@"C:\outro\kit", Ajustes.Carregar(pasta).PastaKit);
            new Ajustes().Salvar(pasta);
            Assert.Null(Ajustes.Carregar(pasta).PastaKit);
        }
        finally { Environment.SetEnvironmentVariable("PMVNI_KIT_RAIZ", antes); }
    }

    [Fact]
    public void salvar_resiste_a_arquivo_preso_por_um_instante()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-preso");
        new Ajustes().Salvar(pasta);
        var arquivo = Path.Combine(pasta, Ajustes.NomeArquivo);
        var preso = new FileStream(arquivo, FileMode.Open, FileAccess.Read, FileShare.None);
        var soltar = Task.Run(() => { Thread.Sleep(100); preso.Dispose(); });
        new Ajustes().Salvar(pasta);
        soltar.Wait();
        Assert.False(File.Exists(arquivo + ".tmp"));
        Assert.NotNull(Ajustes.Carregar(pasta));
    }

    [Fact]
    public void ajustes_dos_agentes()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-agentes");

        // Padrão: os três agentes existem, na conta, sem programa escolhido, sem aviso aceito e sem perfil de chave.
        var a = Ajustes.Carregar(pasta);
        Assert.Equal(3, a.Agentes.Count);
        foreach (var p in Enum.GetValues<Provedor>())
        {
            Assert.Null(a.Agentes[p].PerfilApi);
            Assert.Equal(ModoAcesso.Conta, a.Agentes[p].Modo);
            Assert.Null(a.Agentes[p].Executavel);
            Assert.Null(a.Agentes[p].AvisoVersao);
            Assert.Null(a.Agentes[p].AvisoEm);
            Assert.False(a.Agentes[p].AvisoAceito);
        }

        // Salvar e carregar: modo, programa e aviso; o horário volta em UTC, em segundos.
        var aceite = new DateTimeOffset(2026, 11, 15, 13, 45, 7, TimeSpan.Zero);
        a.Agentes[Provedor.ChatGpt].Modo = ModoAcesso.Chave;
        a.Agentes[Provedor.ChatGpt].Executavel = @"C:\Programas\codex.exe";
        a.Agentes[Provedor.ChatGpt].AvisoVersao = Ajustes.VersaoAviso;
        a.Agentes[Provedor.ChatGpt].AvisoEm = aceite;
        a.Agentes[Provedor.Claude].Executavel = @"C:\Python\python.exe";
        a.Salvar(pasta);

        var texto = File.ReadAllText(Path.Combine(pasta, "ajustes.json"));
        var j = JsonNode.Parse(texto)!;
        Assert.Equal("chave", (string?)j["agentes"]!["chatgpt"]!["modo"]);
        Assert.Equal("conta", (string?)j["agentes"]!["claude"]!["modo"]);
        Assert.Equal("2026-11-15T13:45:07Z", (string?)j["agentes"]!["chatgpt"]!["aviso_em"]);
        var b = Ajustes.Carregar(pasta);
        Assert.Equal(ModoAcesso.Chave, b.Agentes[Provedor.ChatGpt].Modo);
        Assert.Equal(@"C:\Programas\codex.exe", b.Agentes[Provedor.ChatGpt].Executavel);
        Assert.Equal(1, b.Agentes[Provedor.ChatGpt].AvisoVersao);
        Assert.Equal(aceite, b.Agentes[Provedor.ChatGpt].AvisoEm);
        Assert.True(b.Agentes[Provedor.ChatGpt].AvisoAceito);
        Assert.Equal(ModoAcesso.Conta, b.Agentes[Provedor.Claude].Modo);
        Assert.Equal(@"C:\Python\python.exe", b.Agentes[Provedor.Claude].Executavel);
        Assert.False(b.Agentes[Provedor.Claude].AvisoAceito);

        // Gemini: o modo e o perfil da chave ficam guardados; o perfil só aparece no arquivo de quem o tem, e os outros dois agentes não mudam.
        Assert.Equal("conta", (string?)j["agentes"]!["gemini"]!["modo"]);
        Assert.Null(j["agentes"]!["gemini"]!["perfil_api"]);
        Assert.False(j["agentes"]!["chatgpt"]!.AsObject().ContainsKey("perfil_api"));
        Assert.False(j["agentes"]!["gemini"]!.AsObject().ContainsKey("perfil_api"));
        foreach (var (perfil, codigo) in new[] { (PerfilGemini.ApiCreditos, "api_creditos"), (PerfilGemini.ApiGratuita, "api_gratuita") })
        {
            b.Agentes[Provedor.Gemini].Modo = ModoAcesso.Chave;
            b.Agentes[Provedor.Gemini].PerfilApi = perfil;
            b.Salvar(pasta);
            var jg = JsonNode.Parse(File.ReadAllText(Path.Combine(pasta, "ajustes.json")))!["agentes"]!;
            Assert.Equal(("chave", codigo), ((string?)jg["gemini"]!["modo"], (string?)jg["gemini"]!["perfil_api"]));
            Assert.Equal("chave", (string?)jg["chatgpt"]!["modo"]);
            Assert.Equal("conta", (string?)jg["claude"]!["modo"]);
            var g = Ajustes.Carregar(pasta);
            Assert.Equal((ModoAcesso.Chave, perfil), (g.Agentes[Provedor.Gemini].Modo, g.Agentes[Provedor.Gemini].PerfilApi!.Value));
            Assert.Equal(ModoAcesso.Chave, g.Agentes[Provedor.ChatGpt].Modo);
            Assert.Null(g.Agentes[Provedor.ChatGpt].PerfilApi);
        }
        // Voltar para a conta não apaga o perfil escolhido (a escolha do usuário continua lá para quando ele voltar à chave).
        b.Agentes[Provedor.Gemini].Modo = ModoAcesso.Conta;
        b.Salvar(pasta);
        var deVolta = Ajustes.Carregar(pasta);
        Assert.Equal((ModoAcesso.Conta, PerfilGemini.ApiGratuita), (deVolta.Agentes[Provedor.Gemini].Modo, deVolta.Agentes[Provedor.Gemini].PerfilApi!.Value));
        // Um perfil desconhecido, ou o da conta no lugar do da chave, não é aceito do arquivo.
        foreach (var invalido in new[] { "conta_ultra", "premium", "" })
        {
            File.WriteAllText(Path.Combine(pasta, "ajustes.json"), "{ \"agentes\": { \"gemini\": { \"modo\": \"chave\", \"perfil_api\": \"" + invalido + "\" } } }");
            var gi = Ajustes.Carregar(pasta);
            Assert.Equal(ModoAcesso.Chave, gi.Agentes[Provedor.Gemini].Modo);
            Assert.Null(gi.Agentes[Provedor.Gemini].PerfilApi);
        }

        // Aviso de uma versão anterior pede o aceite de novo.
        b.Agentes[Provedor.ChatGpt].AvisoVersao = Ajustes.VersaoAviso - 1;
        Assert.False(b.Agentes[Provedor.ChatGpt].AvisoAceito);

        // Valor inválido volta ao padrão (modo desconhecido, executável vazio, aviso de outro tipo); o resto do arquivo vale.
        File.WriteAllText(Path.Combine(pasta, "ajustes.json"),
            "{ \"tema\": \"escuro\", \"agentes\": { \"chatgpt\": { \"modo\": \"x\", \"executavel\": \"  \", \"aviso_versao\": \"um\", \"aviso_em\": \"ontem\" }, \"claude\": 7 } }");
        var c = Ajustes.Carregar(pasta);
        Assert.Equal("escuro", c.Tema);
        foreach (var p in Enum.GetValues<Provedor>())
        {
            Assert.Equal(ModoAcesso.Conta, c.Agentes[p].Modo);
            Assert.Null(c.Agentes[p].Executavel);
            Assert.Null(c.Agentes[p].AvisoVersao);
            Assert.Null(c.Agentes[p].AvisoEm);
        }

        // O arquivo nunca leva a chave: nem um campo "chave" nem a chave falsa (UTF-8, UTF-16 ou base64), mesmo com a chave guardada no cofre.
        var chaveFalsa = "chave-falsa-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        CofreChaves.Guardar(pasta, Provedor.ChatGpt, chaveFalsa);
        var d = Ajustes.Carregar(pasta);
        d.Agentes[Provedor.ChatGpt].Modo = ModoAcesso.Chave;
        d.Salvar(pasta);
        var bytes = File.ReadAllBytes(Path.Combine(pasta, "ajustes.json"));
        var texto2 = Encoding.UTF8.GetString(bytes);
        Assert.DoesNotContain("\"chave\":", texto2);
        Assert.DoesNotContain(chaveFalsa, texto2);
        Assert.DoesNotContain(chaveFalsa, Encoding.Unicode.GetString(bytes));
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(chaveFalsa)), texto2);
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));
    }

    [Fact]
    public void claude_com_chave_mantem_o_modo_mesmo_quando_o_kit_proibe_a_api()
    {
        var a = new Ajustes();
        a.Agentes[Provedor.Claude].Modo = ModoAcesso.Chave;
        Assert.False(a.NormalizarModos(apiClaudePermitida: true));
        Assert.False(a.NormalizarModos(apiClaudePermitida: false));   // 2026-11-17: a chave vale para a Claude API; o análise pro usa a conta
        Assert.Equal(ModoAcesso.Chave, a.Agentes[Provedor.Claude].Modo);
    }

    [Fact]
    public void ajustes_guardam_execucoes_do_claude()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-execucoes");
        var a = Ajustes.Carregar(pasta);
        Assert.Empty(a.ExecucoesClaude);
        a.ExecucoesClaude.Add(new ExecucaoClaudePendente(@"C:\p\2099\0001 - 2099 - X", @"C:\p\2099\0001 - 2099 - X\.pmvni-ai\editor\2099-01-01_100000-claude"));
        a.ExecucoesClaude.Add(new ExecucaoClaudePendente(@"C:\p\2099\0002 - 2099 - Y", @"C:\p\2099\0002 - 2099 - Y\.pmvni-ai\editor\r"));
        a.Salvar(pasta);

        var j = JsonNode.Parse(File.ReadAllText(Path.Combine(pasta, "ajustes.json")))!;
        var lista = j["execucoes_claude"]!.AsArray();
        Assert.Equal(2, lista.Count);
        Assert.Equal(@"C:\p\2099\0001 - 2099 - X", lista[0]!["processo"]!.GetValue<string>());
        Assert.Equal(@"C:\p\2099\0002 - 2099 - Y\.pmvni-ai\editor\r", lista[1]!["registro"]!.GetValue<string>());

        var b = Ajustes.Carregar(pasta);
        Assert.Equal(a.ExecucoesClaude, b.ExecucoesClaude);

        // Item com chave faltando (ou de tipo errado, ou vazio) é ignorado; os bons ficam.
        j["execucoes_claude"] = JsonNode.Parse("""
            [ { "processo": "C:\\a" }, { "registro": "C:\\a\\r" }, { "processo": 3, "registro": "C:\\b" }, "x",
              { "processo": "", "registro": "C:\\c" }, { "processo": "C:\\d", "registro": "C:\\d\\r" } ]
            """);
        File.WriteAllText(Path.Combine(pasta, "ajustes.json"), j.ToJsonString());
        var c = Ajustes.Carregar(pasta);
        Assert.Equal(new[] { new ExecucaoClaudePendente(@"C:\d", @"C:\d\r") }, c.ExecucoesClaude);
    }
}
