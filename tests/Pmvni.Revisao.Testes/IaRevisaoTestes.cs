using Pmvni.App;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>A IA na revisão do relatório (correções 2026-11-16): o Codex com o Terra 6.1 em esforço baixo, sem abrir arquivos; o pedido
/// de ajuste do item impõe o tamanho do original e proíbe narrar a revisão; a resposta é conferida antes de entrar; o item inteiro se
/// reajusta quando a justificativa muda (o aviso sai do histórico); tudo entra como um lote (um Desfazer).</summary>
public class IaRevisaoTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar(string caso)
    {
        var caminho = Ambiente.Corpus(caso + ".json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    private static SessaoRevisao Sessao(ConteudoPacote c) => new(c.Projeto, c.Historico, Ambiente.Contexto());

    // ---------- programa, modelo e esforço ----------

    [Fact]
    public void o_ajuste_de_texto_usa_o_codex_com_terra_6_1_em_esforco_baixo()
    {
        Assert.Equal("gpt-6.1-terra", ModeloIaAjuste.ModeloPadrao);
        Assert.Equal("low", ModeloIaAjuste.EsforcoPadrao);
        var args = ModeloIaAjuste.ArgumentosCodex(ModeloIaAjuste.ModeloPadrao, ModeloIaAjuste.EsforcoPadrao);
        Assert.Equal(new[] { "-m", "gpt-6.1-terra", "-c", "model_reasoning_effort=\"low\"" }, args);

        // Com os dois programas instalados, a revisão prefere o Codex; numa pasta vazia, sem guardar a conversa, só leitura.
        var opcoes = new IaDespacho.OpcoesIa(PreferirCodex: true, ArgumentosCodex: args, SemArquivos: true, Limite: ModeloIaAjuste.Limite);
        var c = IaDespacho.Montar(@"C:\x\claude.exe", @"C:\x\codex.exe", @"C:\vazia", comPasta: false, [], opcoes);
        Assert.True(c.Codex);
        Assert.Equal(@"C:\x\codex.exe", c.Programa);
        Assert.Equal("exec", c.Argumentos[0]);
        var linha = string.Join(' ', c.Argumentos);
        Assert.Contains("--sandbox read-only", linha);
        Assert.Contains("--ephemeral", linha);
        Assert.Contains("-m gpt-6.1-terra -c model_reasoning_effort=\"low\"", linha);
        Assert.Contains(@"--cd C:\vazia", linha);
        Assert.Equal("-", c.Argumentos[^1]);
        Assert.True(ModeloIaAjuste.Limite < IaDespacho.Limite);
    }

    [Fact]
    public void sem_codex_cai_no_claude_sem_ferramentas_de_leitura()
    {
        var opcoes = new IaDespacho.OpcoesIa(PreferirCodex: true, ArgumentosCodex: ModeloIaAjuste.ArgumentosCodex(null, null), SemArquivos: true);
        var c = IaDespacho.Montar(@"C:\x\claude.exe", null, @"C:\vazia", comPasta: false, [], opcoes);
        Assert.False(c.Codex);
        Assert.Equal(@"C:\x\claude.exe", c.Programa);
        Assert.DoesNotContain("--allowedTools", c.Argumentos);
        Assert.Contains("Read Grep Glob", c.Argumentos[c.Argumentos.ToList().IndexOf("--disallowedTools") + 1]);
        Assert.Throws<InvalidOperationException>(() => IaDespacho.Montar(null, "", @"C:\vazia", false, [], opcoes));
    }

    [Fact]
    public void o_novo_despacho_continua_como_era()
    {
        // Sem opções: o Claude primeiro, com leitura só quando há pasta; sem o Claude, o Codex sem modelo nem --ephemeral.
        var claude = IaDespacho.Montar(@"C:\x\claude.exe", @"C:\x\codex.exe", @"C:\tmp", comPasta: false, []);
        Assert.False(claude.Codex);
        Assert.Equal(new[] { "-p", "--output-format", "text", "--permission-mode", "default", "--disallowedTools", "Bash Edit Write MultiEdit NotebookEdit WebFetch WebSearch Task" }, claude.Argumentos);
        var comPasta = IaDespacho.Montar(@"C:\x\claude.exe", null, @"C:\proc", comPasta: true, [@"C:\leis"]);
        Assert.Equal(new[] { "--allowedTools", "Read Grep Glob", "--add-dir", @"C:\leis" }, comPasta.Argumentos.Skip(7));
        var codex = IaDespacho.Montar(null, @"C:\x\codex.exe", @"C:\tmp", comPasta: false, []);
        Assert.True(codex.Codex);
        Assert.Equal(new[] { "exec", "--skip-git-repo-check", "--sandbox", "read-only", "--cd", @"C:\tmp", "--output-last-message" }, codex.Argumentos.Take(7));
        Assert.DoesNotContain("-m", codex.Argumentos);
        Assert.DoesNotContain("--ephemeral", codex.Argumentos);
    }

    [Fact]
    public void modelo_e_esforco_escolhidos_em_ajustes()
    {
        Assert.Equal(new[] { "-m", "gpt-6.1-sol", "-c", "model_reasoning_effort=\"high\"" }, ModeloIaAjuste.ArgumentosCodex("gpt-6.1-sol", "high"));
        Assert.Empty(ModeloIaAjuste.ArgumentosCodex(ModeloIaAjuste.DoCodex, "high"));   // vale o config.toml do usuário
        // Valor desconhecido (arquivo de ajustes mexido à mão) nunca vira argumento: volta ao padrão.
        Assert.Equal(new[] { "-m", "gpt-6.1-terra", "-c", "model_reasoning_effort=\"low\"" }, ModeloIaAjuste.ArgumentosCodex("x --dangerously", "\"; rm"));
        Assert.Equal("Terra 6.1, esforço baixo", ModeloIaAjuste.Descricao("gpt-6.1-terra", "low"));
        Assert.Equal("0:07", ModeloIaAjuste.Tempo(TimeSpan.FromSeconds(7.9)));
        Assert.Equal("2:05", ModeloIaAjuste.Tempo(TimeSpan.FromSeconds(125)));

        using var pasta = Ambiente.PastaDeTeste("ajustes-ia");
        // ia_ajuste_modelo/ia_ajuste_esforco (antigos) só migram o Codex: valor desconhecido vira o padrão (Terra 6.1, baixo).
        File.WriteAllText(Path.Combine(pasta, "ajustes.json"), "{\"ia_ajuste_modelo\":\"outro\",\"ia_ajuste_esforco\":\"ultra\"}");
        var c = Ajustes.Carregar(pasta);
        Assert.Equal(new ModeloEscolhido("gpt-6.1-terra", "low"), c.IaModelos[FonteIa.Codex]);
    }

    // ---------- o pedido e a conferência da resposta ----------

    static readonly Dictionary<string, string> Recuo = new()
    {
        ["titulo"] = "Item 02 — Recuo frontal",
        ["verificacao"] = "A planta de implantação cota o recuo frontal em 2,80 m; a zona exige no mínimo 3,00 m da testada.",
        ["providencia"] = "Ajustar a implantação para recuo frontal de pelo menos 3,00 m e cotar a distância na planta.",
        ["base_legal"] = "Plano Diretor (Lei nº 1.382/2020), anexo de parâmetros.",
        ["referencia"] = "ARQ 01/04, implantação. Figura 1.",
    };

    [Fact]
    public void o_pedido_impoe_o_tamanho_do_original_e_nao_manda_explorar_arquivos()
    {
        var limites = IaAjusteItem.Limites(Recuo, Recuo);
        Assert.Equal(160, limites["verificacao"]);                       // o original tem 99: vale o piso
        Assert.Equal(140, limites["providencia"]);
        var longo = new Dictionary<string, string>(Recuo) { ["verificacao"] = new string('x', 300) };
        Assert.Equal(300, IaAjusteItem.Limites(longo, Recuo)["verificacao"]);   // original maior que o piso: o tamanho dele
        // Sem original (item criado pelo usuário), a referência é o texto atual.
        Assert.Equal(300, IaAjusteItem.Limites(new Dictionary<string, string>(), longo)["verificacao"]);

        var p = IaAjusteItem.Prompt("NAO_ATENDE", "ATENDE", situacaoFixa: true, "LEI MUDOU, aceita esse recuo", Recuo, limites);
        Assert.Contains("verificacao: no máximo 160 caracteres", p);
        Assert.Contains("providencia: no máximo 140 caracteres", p);
        Assert.Contains("NÃO abra arquivos", p);
        Assert.Contains("\"situacao\": \"ATENDE\"", p);
        Assert.Contains("DECIDIDA", p);
        Assert.Contains("LEI MUDOU, aceita esse recuo", p);
        Assert.DoesNotContain("pasta de trabalho", p, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("03 - LEGISLACAO", p);
        // Sem mudança de situação, a justificativa é a instrução e a IA pode propor outra situação.
        var livre = IaAjusteItem.Prompt("NAO_ATENDE", "NAO_ATENDE", situacaoFixa: false, "rever: a cota correta é 3,10 m", Recuo, limites);
        Assert.Contains("O revisor não mudou a situação", livre);
        Assert.DoesNotContain("DECIDIDA", livre);
    }

    [Fact]
    public void resposta_grande_ou_que_narra_a_revisao_e_recusada()
    {
        var limites = IaAjusteItem.Limites(Recuo, Recuo);
        // O texto da captura do usuário (image3): longo e contando a história da revisão.
        var ruim = new Dictionary<string, string>
        {
            ["verificacao"] = "A planta de implantação cota o recuo frontal em 2,80 m. O item foi registrado como ATENDE com base na justificativa do revisor de que a legislação teria mudado. Não foi possível confirmar, na legislação consultada, dispositivo que reduza o recuo frontal ou que admita 2,80 m: o Anexo VI da Lei nº 1.382/2020 (Critérios Urbanísticos das Zonas Urbanas), na redação dada pela Lei nº 1.507/2022, mantém recuo frontal mínimo de 3,00 m para as zonas urbanas.",
            ["providencia"] = "Mantida a situação ATENDE, nada é exigido do requerente. A aceitação do recuo de 2,80 m depende de fundamento legal que não pôde ser confirmado nesta análise.",
            ["base_legal"] = Recuo["base_legal"],
            ["referencia"] = Recuo["referencia"],
        };
        var problemas = IaAjusteItem.Problemas(ruim, limites);
        Assert.Contains(problemas, x => x.StartsWith("Verificação técnica:") && x.Contains("caracteres"));
        Assert.Contains(problemas, x => x.StartsWith("Verificação técnica:") && x.Contains("história da revisão"));
        Assert.Contains(problemas, x => x.StartsWith("Providência:") && x.Contains("história da revisão"));
        Assert.DoesNotContain(problemas, x => x.StartsWith("Base legal"));

        var boa = new Dictionary<string, string>
        {
            ["situacao"] = "ATENDE",
            ["verificacao"] = "A planta de implantação cota o recuo frontal em 2,80 m, aceito pela legislação atual.",
            ["providencia"] = "Nenhuma correção.",
            ["base_legal"] = Recuo["base_legal"],
            ["referencia"] = Recuo["referencia"],
        };
        Assert.Empty(IaAjusteItem.Problemas(boa, limites));
        // Até 1,3× o limite ainda entra; acima, não.
        Assert.Empty(IaAjusteItem.Problemas(new Dictionary<string, string> { ["verificacao"] = new string('a', 208) }, limites));
        Assert.Single(IaAjusteItem.Problemas(new Dictionary<string, string> { ["verificacao"] = new string('a', 209) }, limites));
        // O nome de uma situação no meio do texto também volta.
        Assert.Single(IaAjusteItem.Problemas(new Dictionary<string, string> { ["providencia"] = "O item NÃO ATENDE; corrigir o recuo." }, limites));

        var segundo = IaAjusteItem.PromptCorrecao("PEDIDO", ruim, problemas);
        Assert.StartsWith("PEDIDO", segundo);
        Assert.Contains("RECUSADA", segundo);
        Assert.Contains(problemas[0], segundo);
    }

    [Fact]
    public void a_resposta_traz_os_campos_e_a_situacao()
    {
        var r = IaAjusteItem.Ler("Segue:\n```json\n{\"situacao\":\"ATENDE\",\"verificacao\":\"V\",\"providencia\":\"Nenhuma correção.\",\"outra\":1}\n```");
        Assert.NotNull(r);
        Assert.Equal("ATENDE", r["situacao"]);
        Assert.Equal("V", r["verificacao"]);
        Assert.False(r.ContainsKey("outra"));
        Assert.False(IaAjusteItem.Ler("{\"situacao\":\"TALVEZ\",\"verificacao\":\"V\"}")!.ContainsKey("situacao")); // código desconhecido não entra
        Assert.Null(IaAjusteItem.Ler("não sei"));
        Assert.Null(IaAjusteItem.Ler("{\"situacao\":\"ATENDE\"}"));   // sem nenhum campo do item, não é resposta
    }

    [Fact]
    public void o_pedido_da_tabela_vale_com_ou_sem_valor_editado_e_nao_abre_arquivos()
    {
        var linhas = new Dictionary<string, Dictionary<string, string>>
        {
            ["ambientes/01"] = new() { ["nome"] = "Quarto 1", ["vaos/0/medidas"] = "1,50 × 1,20 m", ["situacao"] = "ATENDE" },
        };
        var p = IaRecalculoIndices.Prompt("ambientes", "ambientes/01", linhas, "adicionar cálculo da porta de 80x210cm e rever se atende", []);
        Assert.Contains("O revisor não editou valores nela", p);
        Assert.Contains("adicionar cálculo da porta de 80x210cm e rever se atende", p);
        Assert.Contains("NÃO abra arquivos", p);
        Assert.DoesNotContain("pasta de trabalho", p, StringComparison.OrdinalIgnoreCase);
        var editado = IaRecalculoIndices.Prompt("indices", "indices/05", linhas, null, ["projeto"]);
        Assert.Contains("editou à mão nela: projeto", editado);
        Assert.DoesNotContain("pasta de trabalho", IaRecalculoIndices.PromptVolumetria("mais um pavimento", linhas), StringComparison.OrdinalIgnoreCase);
    }

    // ---------- um lote = uma alteração ----------

    [Fact]
    public void o_ajuste_entra_como_um_lote_e_um_desfazer_volta_tudo()
    {
        var c = Importar("curto");
        var s = Sessao(c);
        var id = s.Projeto.Incluidos[1].Id;
        var antes = ProjetoJson.Escrever(s.Projeto);
        var verificacao = DadosEfetivos.TextoEfetivo(s.Projeto, s.Projeto.Item(id)!, "verificacao");

        var r = s.Aplicar(new Lote(id,
        [
            new DefinirSituacao(id, "ATENDE"),
            new DefinirCampo(id, "verificacao", "O projeto atende ao recuo."),
            new DefinirCampo(id, "providencia", "Nenhuma correção."),
            new DefinirCampo(id, "verificacao", "O projeto atende ao recuo."),   // repetido: não muda nada, fica de fora
        ], "Ajustado pela IA"));

        Assert.NotNull(r);
        Assert.Equal(nameof(Lote), r.Tipo);
        Assert.Equal(id, r.Item);
        Assert.Equal(3, r.Depois!["passos"]!.AsArray().Count);
        Assert.Equal("ATENDE", s.Projeto.Item(id)!.Situacao.Revisada);
        Assert.Equal("O projeto atende ao recuo.", DadosEfetivos.TextoEfetivo(s.Projeto, s.Projeto.Item(id)!, "verificacao"));
        Assert.Equal("ITEM 02 · Ajustado pela IA (3 alterações)", DescricaoHistorico.Texto(r, s.Projeto));

        Assert.NotNull(s.Desfazer());
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
        Assert.Equal(verificacao, DadosEfetivos.TextoEfetivo(s.Projeto, s.Projeto.Item(id)!, "verificacao"));
        Assert.NotNull(s.Refazer());
        Assert.Equal("Nenhuma correção.", DadosEfetivos.TextoEfetivo(s.Projeto, s.Projeto.Item(id)!, "providencia"));

        // O diário reaplica o lote, o desfeito e o refeito sobre o projeto aberto de novo.
        var outro = Importar("curto");
        var copia = ProjetoJson.Ler(ProjetoJson.Escrever(c.Projeto));
        Assert.NotNull(outro);
        var s2 = new SessaoRevisao(ProjetoJson.Ler(antes), c.Historico, Ambiente.Contexto());
        s2.Reaplicar(s.Pendentes);
        Assert.Equal(ProjetoJson.Escrever(s.Projeto), ProjetoJson.Escrever(s2.Projeto));
        Assert.NotNull(copia);
    }

    [Fact]
    public void lote_recusado_nao_muda_nada_e_lote_sem_efeito_nao_registra()
    {
        var s = Sessao(Importar("curto"));
        var id = s.Projeto.Incluidos[0].Id;
        var antes = ProjetoJson.Escrever(s.Projeto);
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new Lote(id, [new DefinirCampo(id, "verificacao", "Texto novo."), new DefinirCampo(id, "titulo", "  ")], "x")));
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
        Assert.False(s.PodeDesfazer);
        Assert.Null(s.Aplicar(new Lote(id, [], "x")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new Lote(id, [new Lote(id, [], "y")], "x")));
        // Testar devolve o projeto como ficaria, sem mudar o da sessão.
        var candidato = s.Testar(new Lote(id, [new DefinirCampo(id, "verificacao", "Texto novo.")], "x"));
        Assert.Equal("Texto novo.", DadosEfetivos.TextoEfetivo(candidato, candidato.Item(id)!, "verificacao"));
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
    }

    // ---------- "a justificativa mudou: reajuste o item" ----------

    [Fact]
    public void justificativa_nova_pede_o_reajuste_ate_os_textos_mudarem()
    {
        var s = Sessao(Importar("curto"));
        var id = s.Projeto.Incluidos[1].Id;
        bool Pendente() => AjustePendente.JustificativaMudou(s.Projeto, s.HistoricoCompleto, id);
        Assert.False(Pendente());

        // Só a justificativa, sem mudar a situação: o item inteiro pode ser reajustado.
        s.Aplicar(new DefinirJustificativa(id, "a cota correta é 3,10 m"));
        Assert.True(Pendente());
        // O texto foi mexido depois (pela IA ou à mão): o aviso some.
        s.Aplicar(new DefinirCampo(id, "verificacao", "A planta cota o recuo em 3,10 m."));
        Assert.False(Pendente());
        // A justificativa muda de novo: volta.
        s.Aplicar(new DefinirJustificativa(id, "na verdade 3,20 m"));
        Assert.True(Pendente());
        // O ajuste da IA entra como lote (com ou sem situação): some.
        s.Aplicar(new Lote(id, [new DefinirSituacao(id, "ATENDE"), new DefinirCampo(id, "verificacao", "A planta cota o recuo em 3,20 m.")], "Ajustado pela IA"));
        Assert.False(Pendente());
        // Desfeito o ajuste, a justificativa volta a estar à frente do texto.
        s.Desfazer();
        Assert.True(Pendente());
        s.Refazer();
        Assert.False(Pendente());
        // Mudar só a situação também pede o reajuste; sem justificativa nenhuma, não há o que seguir.
        s.Aplicar(new DefinirSituacao(id, "NAO_ATENDE"));
        Assert.True(Pendente());
        s.Aplicar(new DefinirJustificativa(id, null));
        Assert.False(Pendente());
        // Outro item não é afetado.
        Assert.False(AjustePendente.JustificativaMudou(s.Projeto, s.HistoricoCompleto, s.Projeto.Incluidos[0].Id));
    }

    [Fact]
    public void justificativa_de_linha_de_tabela_tambem_pede_a_revisao()
    {
        var s = Sessao(Importar("longo"));
        Assert.False(AjustePendente.JustificativaMudou(s.Projeto, s.HistoricoCompleto, "ambientes/01"));
        s.Aplicar(new DefinirJustificativa("ambientes/01", "adicionar cálculo da porta de 80x210cm e rever se atende"));
        Assert.True(AjustePendente.JustificativaMudou(s.Projeto, s.HistoricoCompleto, "ambientes/01"));
        s.Aplicar(new Lote("ambientes/01", [new DefinirCampo("ambientes/01", "textos/0", "Revisto.")], "Revisto pela IA"));
        Assert.False(AjustePendente.JustificativaMudou(s.Projeto, s.HistoricoCompleto, "ambientes/01"));
    }

    [Theory]
    [InlineData("Failed to authenticate: OAuth session expired and could not be refreshed", true)]
    [InlineData("Not logged in · Please run /login", true)]
    [InlineData("stream error: 401 Unauthorized", true)]
    [InlineData("rate limit reached, try again later", false)]
    public void login_vencido_vira_mensagem_clara(string saida, bool vencido) => Assert.Equal(vencido, IaDespacho.LoginVencido(saida));}
