using Pmvni.Agentes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Tarefa 21: o cartão "Plugins e ferramentas" (Ajustes › Pastas e kit). As pastas são montadas no temporário; nada é iniciado de verdade
/// (o lançador é falso).</summary>
public sealed class ComponentesDoKitTestes : IDisposable
{
    readonly string raiz = Path.Combine(Path.GetTempPath(), "pmvni-componentes-" + Guid.NewGuid().ToString("N"));
    string CodexHome => Path.Combine(raiz, "codex");
    string MotorPdf => Path.Combine(raiz, "MotorPdf");
    string Pmvni => Path.Combine(raiz, "PMVNI");
    string KitLocal => Path.Combine(raiz, "KIT");
    string Fonte => Path.Combine(raiz, "fonte");
    string Scripts => Path.Combine(raiz, "bootstrap");

    public ComponentesDoKitTestes()
    {
        foreach (var d in new[] { CodexHome, MotorPdf, Pmvni, KitLocal, Fonte, Scripts }) Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(Scripts, ComponentesDoKit.Script), "# fictício");
    }

    public void Dispose() { try { Directory.Delete(raiz, true); } catch (IOException) { } }

    OrigensDoKit Origens(string? hashDoEditor = null) => new(CodexHome, null, MotorPdf, Pmvni, KitLocal, hashDoEditor);

    void PluginNoKit(string kit, string versao)
    {
        var d = Path.Combine(kit, "01 - PLUGINS", "plugins", "analise", ".codex-plugin");
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "plugin.json"), "{\"name\":\"analise\",\"version\":\"" + versao + "\"}");
    }

    void PluginNoCodex(string versao) => Directory.CreateDirectory(Path.Combine(CodexHome, "plugins", "cache", "pmvni-kit", "analise", versao));

    void PmvniKitJson(string fonte) =>
        File.WriteAllText(Path.Combine(CodexHome, "pmvni-kit.json"),
            System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string> { ["kit_root"] = KitLocal, ["fonte"] = fonte }));

    static LinhaComponente Linha(LeituraDoKit l, string id) => l.Linhas.Single(x => x.Componente.Id == id);

    [Fact]
    public void Situacao_pela_versao_instalada_e_disponivel()
    {
        Assert.Equal(SituacaoComponente.AtualizacaoDisponivel, ComponentesDoKit.Comparar("3.9.0", "4.0.0"));
        Assert.Equal(SituacaoComponente.EmDia, ComponentesDoKit.Comparar("4.0.0", "4.0.0"));
        Assert.Equal(SituacaoComponente.EmDia, ComponentesDoKit.Comparar("4.1.0", "4.0.0")); // PC do kit mestre: o instalado pode estar à frente
        Assert.Equal(SituacaoComponente.Ausente, ComponentesDoKit.Comparar(null, "4.0.0"));
        Assert.Equal(SituacaoComponente.NaoVerificado, ComponentesDoKit.Comparar("4.0.0", null));
        Assert.Equal(SituacaoComponente.ComErro, ComponentesDoKit.Comparar("4.0.0", "4.0.0", comErro: true));
        Assert.Equal("Atualização disponível", ComponentesDoKit.TextoSituacao(SituacaoComponente.AtualizacaoDisponivel));
        Assert.Equal("Em dia", ComponentesDoKit.TextoSituacao(SituacaoComponente.EmDia));
        Assert.Equal("Ausente", ComponentesDoKit.TextoSituacao(SituacaoComponente.Ausente));
        Assert.Equal("Não foi possível verificar", ComponentesDoKit.TextoSituacao(SituacaoComponente.NaoVerificado));
    }

    [Fact]
    public async Task Leitura_das_versoes_e_dos_motores()
    {
        PluginNoKit(Fonte, "4.1.0");
        PluginNoKit(KitLocal, "4.1.0");
        PluginNoCodex("4.0.0");
        PmvniKitJson(Fonte);
        File.WriteAllText(Path.Combine(MotorPdf, "resumo-instalacao.txt"),
            "\uFEFFBlueprint Pipeline    INSTALADO\r\nFYP-Floorplan    PENDENTE\r\nDocling MCP 3.5.0    INSTALADO\r\nPyMuPDF 2.0    INSTALADO\r\nFALHA    Dependencias de visao incompletas\r\n");
        File.WriteAllText(Path.Combine(MotorPdf, "mcp_pmvni.py"), "# servidor");
        var hash = ComponentesDoKit.HashDe(Path.Combine(MotorPdf, "mcp_pmvni.py"));

        var l = await ComponentesDoKit.LerAsync(Origens(hash));

        Assert.True(l.FonteAcessivel);
        Assert.Equal(Fonte, l.Fonte);
        // O despacho sai do kit na Tarefa 22: não está na lista.
        Assert.DoesNotContain(l.Linhas, x => x.Componente.Nome.Contains("espacho", StringComparison.Ordinal));
        var codex = Linha(l, "analise-codex");
        Assert.Equal(("4.0.0", "4.1.0", SituacaoComponente.AtualizacaoDisponivel, false), (codex.Instalada, codex.Disponivel, codex.Situacao, codex.SoPeloTodos));
        Assert.Equal("Instalada: 4.0.0 · disponível: 4.1.0", codex.TextoVersoes);
        Assert.Equal(SituacaoComponente.Ausente, Linha(l, "analise-claude").Situacao);       // sem pasta do Claude Code
        var servidor = Linha(l, "pmvni-pdf");
        Assert.Equal(SituacaoComponente.EmDia, servidor.Situacao);
        Assert.Equal("Instalado em " + File.GetLastWriteTime(Path.Combine(MotorPdf, "mcp_pmvni.py")).ToString("dd/MM/yyyy") + " · igual à versão deste Editor", servidor.TextoVersoes);
        Assert.Equal("Atualiza a análise no Codex e no Claude Code (o mesmo plugin), fora do Editor.", Linha(l, "analise-claude").TextoDica);
        Assert.Contains("os quatro motores de PDF", Linha(l, "docling").TextoDica);
        Assert.Equal(("2.0", "2.1", SituacaoComponente.AtualizacaoDisponivel), (Linha(l, "pymupdf").Instalada, Linha(l, "pymupdf").Disponivel, Linha(l, "pymupdf").Situacao));
        Assert.Equal(SituacaoComponente.EmDia, Linha(l, "docling").Situacao);
        Assert.Equal(SituacaoComponente.EmDia, Linha(l, "blueprint").Situacao);
        var fyp = Linha(l, "fyp");
        Assert.Equal(SituacaoComponente.ComErro, fyp.Situacao);
        Assert.Contains("Dependencias de visao incompletas", fyp.Detalhe);

        // A versão nova ainda não chegou à cópia do kit deste computador: o botão do componente roda o "Atualizar todos" e o texto diz isso.
        PluginNoKit(KitLocal, "4.0.0");
        codex = Linha(await ComponentesDoKit.LerAsync(Origens(hash)), "analise-codex");
        Assert.True(codex.SoPeloTodos);
        Assert.Equal("Atualizar (com todos)", codex.TextoBotao);
        Assert.Contains("o botão roda o \"Atualizar todos\"", codex.Detalhe);
        File.WriteAllText(Path.Combine(KitLocal, ComponentesDoKit.InstaladorDoKit), "rem fictício");
        var iniciados = new List<string>();
        var texto = ComponentesDoKit.Atualizar(codex, [], Scripts, KitLocal, (_, a) => iniciados.Add(a));
        Assert.Equal(Path.Combine(Scripts, ComponentesDoKit.CmdTodos), Assert.Single(iniciados));
        Assert.StartsWith("Análise no Codex só se atualiza junto com o kit inteiro", texto);

        // Uma execução só do analise deixa os motores PENDENTE sem tentar: não é erro.
        File.WriteAllText(Path.Combine(MotorPdf, "resumo-instalacao.txt"),
            "Blueprint Pipeline    PENDENTE\r\nFYP-Floorplan    PENDENTE\r\nDocling MCP 3.5.0    PENDENTE\r\nPyMuPDF 2.1    PENDENTE\r\nFALHA    Plugin analise\r\n");
        var soAnalise = await ComponentesDoKit.LerAsync(Origens(hash));
        Assert.All(new[] { "pymupdf", "docling", "blueprint", "fyp" }, id => Assert.Equal(SituacaoComponente.Ausente, Linha(soAnalise, id).Situacao));
        // Falha que cita um motor só marca esse.
        File.WriteAllText(Path.Combine(MotorPdf, "resumo-instalacao.txt"), "Docling MCP 3.5.0    PENDENTE\r\nPyMuPDF 2.1    PENDENTE\r\nFALHA    Docling indisponivel\r\n");
        var soDocling = await ComponentesDoKit.LerAsync(Origens(hash));
        Assert.Equal(SituacaoComponente.ComErro, Linha(soDocling, "docling").Situacao);
        Assert.Equal(SituacaoComponente.Ausente, Linha(soDocling, "pymupdf").Situacao);

        // Sem resumo: os motores ficam ausentes.
        File.Delete(Path.Combine(MotorPdf, "resumo-instalacao.txt"));
        Assert.Equal(SituacaoComponente.Ausente, Linha(await ComponentesDoKit.LerAsync(Origens(hash)), "docling").Situacao);
    }

    [Fact]
    public async Task Fonte_inacessivel_nao_foi_possivel_verificar_sem_erro()
    {
        PluginNoKit(KitLocal, "4.0.0");
        PluginNoCodex("4.0.0");
        PmvniKitJson(@"\\servidor-que-nao-existe.invalid\pmvni\KIT");

        var l = await ComponentesDoKit.LerAsync(Origens());

        Assert.False(l.FonteAcessivel);
        var codex = Linha(l, "analise-codex");
        Assert.Equal(SituacaoComponente.NaoVerificado, codex.Situacao);
        Assert.Equal("Não foi possível verificar", codex.TextoSituacao);
        Assert.Equal("4.0.0", codex.Instalada);
        Assert.Null(codex.Disponivel);
        Assert.StartsWith("Não foi possível verificar as versões disponíveis: a fonte do kit", l.TextoFonte);
        Assert.False(codex.SoPeloTodos);
    }

    [Fact]
    public async Task Kit_mestre_usa_o_proprio_kit_como_fonte()
    {
        PluginNoKit(KitLocal, "4.0.0");
        PluginNoCodex("4.0.0");
        PmvniKitJson(@"\\servidor-que-nao-existe.invalid\pmvni\KIT");
        File.WriteAllText(Path.Combine(Pmvni, "instalacao.json"), "{\"modo_kit\":\"espelho\",\"kit_root\":\"" + KitLocal.Replace("\\", "\\\\") + "\"}");

        var l = await ComponentesDoKit.LerAsync(Origens());

        Assert.Equal(KitLocal, l.Fonte);
        Assert.Equal(SituacaoComponente.EmDia, Linha(l, "analise-codex").Situacao);
    }

    [Fact]
    public void Atualizar_um_componente_gera_o_cmd_so_dele()
    {
        var chamadas = new List<(string Programa, string Argumento)>();
        var texto = ComponentesDoKit.IniciarAtualizacao("analise", [], Scripts, KitLocal, (p, a) => chamadas.Add((p, a)));

        var (programa, argumento) = Assert.Single(chamadas);
        Assert.Equal("explorer.exe", Path.GetFileName(programa), ignoreCase: true);
        Assert.Equal(Path.Combine(Scripts, "atualizar-analise.cmd"), argumento);
        var cmd = File.ReadAllText(argumento);
        Assert.Contains("-File \"" + Path.Combine(Scripts, ComponentesDoKit.Script) + "\" -Componente analise", cmd);
        Assert.DoesNotContain("motores", cmd);
        Assert.DoesNotContain(ComponentesDoKit.InstaladorDoKit, cmd);
        Assert.Equal("Atualização iniciada fora do Editor: a análise no Codex e no Claude Code (o mesmo plugin). Quando terminar, clique em Verificar atualizações no cartão Plugins e ferramentas.", texto);

        // "Atualizar todos": o INSTALAR_KIT_PMVNI.cmd atualizar do kit deste computador.
        chamadas.Clear();
        File.WriteAllText(Path.Combine(KitLocal, ComponentesDoKit.InstaladorDoKit), "rem fictício");
        ComponentesDoKit.IniciarAtualizacao(null, [], Scripts, KitLocal, (p, a) => chamadas.Add((p, a)));
        Assert.Equal(Path.Combine(Scripts, ComponentesDoKit.CmdTodos), Assert.Single(chamadas).Argumento);
        Assert.Contains("call \"" + Path.Combine(KitLocal, ComponentesDoKit.InstaladorDoKit) + "\" atualizar", File.ReadAllText(chamadas[0].Argumento));
    }

    [Fact]
    public void Com_analise_em_andamento_nada_e_iniciado()
    {
        var chamadas = 0;
        foreach (var parte in new[] { "analise", "motores", null })
        {
            var texto = ComponentesDoKit.IniciarAtualizacao(parte, ["2026-00123"], Scripts, KitLocal, (_, _) => chamadas++);
            Assert.Equal(AtualizacaoKit.AvisoAnaliseEmAndamento(["2026-00123"]), texto);
        }
        Assert.Equal(AtualizacaoKit.AvisoAnaliseEmAndamento(["2026-00123"]),
            ComponentesDoKit.AtualizarAsync(null, ["2026-00123"], Scripts, KitLocal, (_, _) => chamadas++).GetAwaiter().GetResult());
        Assert.Equal(0, chamadas);
        Assert.Empty(Directory.GetFiles(Scripts, "*.cmd"));
    }

    [Fact]
    public void Ultima_execucao_da_tarefa_pelo_estado_json()
    {
        var t = ComponentesDoKit.TextoDaTarefa("{\"modo\":\"atualizar\",\"atualizado\":\"2026-11-21T10:15:00\",\"situacao\":\"CONCLUIDO\"}", true);
        Assert.StartsWith("Última execução do instalador do kit: 21/11/2026 às 10:15 (atualizar) · concluída sem pendências.", t);
        Assert.Contains("mantém o kit sempre atualizado", t);
        // Sem a tarefa (ou sem como conferir), o cartão não afirma que ela roda sozinha.
        Assert.Contains("A atualização automática não está configurada neste computador", ComponentesDoKit.TextoDaTarefa(null, false));
        Assert.DoesNotContain("roda sozinha", ComponentesDoKit.TextoDaTarefa(null, false));
        Assert.DoesNotContain("roda sozinha", ComponentesDoKit.TextoDaTarefa(null, null));
        Assert.Contains("fonte do kit estava fora do alcance", ComponentesDoKit.TextoDaTarefa("{\"situacao\":\"SEM_FONTE\"}", true));
        // O instalador grava situacao ADIADO, adiado_processo e adiado_desde (Adiar-Se-Analise, código 4).
        Assert.Contains("adiada: havia análise em andamento em 2026-00123, desde 21/11/2026 às 09:40",
            ComponentesDoKit.TextoDaTarefa("{\"situacao\":\"ADIADO\",\"adiado_processo\":\"2026-00123\",\"adiado_desde\":\"2026-11-21T09:40:00\"}", true));
        Assert.StartsWith("O instalador do kit ainda não registrou", ComponentesDoKit.TextoDaTarefa(null, true));
        Assert.StartsWith("O instalador do kit ainda não registrou", ComponentesDoKit.TextoDaTarefa("não é json", true));
    }

    /// <summary>Nenhum texto do Editor (literais de texto do C# e atributos do XAML do App e dos Agentes) usa mais os nomes antigos dos plugins:
    /// "análise pro"/"análise Google" ou "analise-pro"/"analise-google" só valem como nome de pasta de controle (<c>.pmvni-ai\analise-pro</c>, a
    /// constante da pasta e a lista das pastas). O roteiro fica de fora (o <c>pro_runtime.py</c> fictício dele imita um kit antigo).</summary>
    [Fact]
    public void Nenhum_texto_com_os_nomes_antigos_dos_plugins()
    {
        var raizRepo = new DirectoryInfo(AppContext.BaseDirectory);
        while (raizRepo is not null && !File.Exists(Path.Combine(raizRepo.FullName, "EditorRelatoriosPMVNI.sln"))) raizRepo = raizRepo.Parent;
        Assert.NotNull(raizRepo);
        var antigo = new System.Text.RegularExpressions.Regex(@"an[aá]lise[ -](pro|google)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var literal = new System.Text.RegularExpressions.Regex(@"""(?:[^""\\\r\n]|\\.)*""");
        var sobras = new List<string>();
        foreach (var projeto in new[] { "Pmvni.App", "Pmvni.Agentes" })
            foreach (var arquivo in Directory.EnumerateFiles(Path.Combine(raizRepo!.FullName, "src", projeto), "*.*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(raizRepo.FullName, arquivo);
                var ext = Path.GetExtension(arquivo);
                if (ext is not (".cs" or ".xaml") || rel.Contains(@"\obj\") || rel.Contains(@"\bin\") || Path.GetFileName(arquivo) == "Roteiro.cs") continue;
                var texto = File.ReadAllText(arquivo);
                if (ext == ".xaml") texto = System.Text.RegularExpressions.Regex.Replace(texto, "<!--.*?-->", "", System.Text.RegularExpressions.RegexOptions.Singleline);
                var n = 0;
                foreach (var linha in texto.Split('\n'))
                {
                    n++;
                    if (linha.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    foreach (System.Text.RegularExpressions.Match m in literal.Matches(linha))
                    {
                        var v = m.Value.Trim('"');
                        if (!antigo.IsMatch(v)) continue;
                        // Nome de pasta de controle: o nome sozinho ou dentro de um caminho .pmvni-ai.
                        if (v is "analise-pro" or "analise-google" || v.Contains(".pmvni-ai", StringComparison.Ordinal)) continue;
                        sobras.Add(rel + ":" + n + ": " + v);
                    }
                }
            }
        Assert.True(sobras.Count == 0, "Textos com os nomes antigos dos plugins:\n" + string.Join("\n", sobras));
    }

    [Fact]
    public void Nomes_de_rota_no_lugar_de_analise_pro_e_Google()
    {
        Assert.Equal("Análise — rota ChatGPT", TextosAgentes.TituloPlugin(PluginsInstalados.Analise));
        Assert.Equal("Análise — rota Claude", TextosAgentes.TituloPlugin(PluginsInstalados.AnalisePro));
        Assert.Equal("Análise — rota Gemini", TextosAgentes.TituloPlugin(PluginsInstalados.AnaliseGoogle));
        foreach (var p in new[] { Provedor.ChatGpt, Provedor.Claude, Provedor.Gemini })
        {
            Assert.DoesNotContain("análise pro", TextosAgentes.Titulo(p));
            Assert.DoesNotContain("análise Google", TextosAgentes.Titulo(p));
        }
    }
}
