using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Pmvni.Imoveis.Testes;
using Pmvni.Revisao;
using Xunit;

namespace Pmvni.Agentes.Testes;

public partial class OrquestradorTestes
{
    [Fact]
    public void AnaliseChatGpt_PerfisSeparadosPorGrupoAuditoriaEExecucao()
    {
        using var c = new Cena("perfis-do-plugin", "ok", Provedor.ChatGpt);
        var skill = Path.Combine(c.Pasta, "skill");
        var refs = Directory.CreateDirectory(Path.Combine(skill, "references")).FullName;
        const string plano = """[{"id":1,"modelo":"terra","esforco":"low"},{"id":2,"modelo":"terra","esforco":"medium"}]""";
        const string auditoria = """{"modelo":"terra","esforco":"high"}""";
        File.WriteAllText(Path.Combine(refs, "grupos.json"), plano);
        File.WriteAllText(Path.Combine(refs, "perfil-auditoria.json"), auditoria);
        var padrao = new PerfilDoPlugin("terra", "low");
        var sol = new PerfilDoPlugin("sol", "medium");
        PerfisDoPlugin.Escolher(c.Pasta, "1", sol, padrao);
        PerfisDoPlugin.Escolher(c.Pasta, PerfisDoPlugin.Auditoria, sol, new("terra", "high"));
        var pasta = Path.Combine(c.Pasta, "execucao1");
        var copia = PerfisDoPlugin.PrepararExecucao(skill, pasta, c.Pasta, false);
        JsonNode Ler(string raiz, string arquivo) => JsonNode.Parse(File.ReadAllText(Path.Combine(raiz, "references", arquivo)))!;
        Assert.Equal(sol, PerfilDoPlugin.Ler(Ler(copia, "grupos.json")[0]!));
        Assert.Equal(new PerfilDoPlugin("terra","medium"), PerfilDoPlugin.Ler(Ler(copia, "grupos.json")[1]!));
        Assert.Equal(sol, PerfilDoPlugin.Ler(Ler(copia, "perfil-auditoria.json")));
        PerfisDoPlugin.Escolher(c.Pasta, "1", padrao, padrao);
        var retomada = PerfisDoPlugin.PrepararExecucao(skill, pasta, c.Pasta, true);
        Assert.Equal(sol, PerfilDoPlugin.Ler(Ler(retomada, "grupos.json")[0]!));
        var nova = PerfisDoPlugin.PrepararExecucao(skill, Path.Combine(c.Pasta, "execucao2"), c.Pasta, false);
        Assert.Equal(padrao, PerfilDoPlugin.Ler(Ler(nova, "grupos.json")[0]!));
        Assert.Equal(plano, File.ReadAllText(Path.Combine(refs, "grupos.json")));
        Assert.Equal(auditoria, File.ReadAllText(Path.Combine(refs, "perfil-auditoria.json")));
    }

    [Fact]
    public void AnaliseChatGpt_RelatorioTerritorialRenomeadoUsaManifestoEPaginas()
    {
        var fonte = new JsonObject { ["arquivo"] = "anexo-sem-titulo.pdf", ["sha256"] = new string('a', 64), ["paginas"] = new JsonArray(2) };
        var consulta = new JsonObject { ["modalidade"] = "RELATORIO_PDF", ["situacao"] = "CONSULTADA", ["resultado"] = "CANDIDATO", ["relatorio"] = fonte };
        var entrada = new JsonObject { ["pecas"] = new JsonArray(new JsonObject { ["relativo"] = "anexo-sem-titulo.pdf", ["sha256"] = new string('a', 64) }) };
        OrquestradorAnalise.ConferirRelatorioTerritorial(consulta, entrada, consulta);
        fonte["sha256"] = new string('b', 64);
        Assert.Throws<InvalidDataException>(() => OrquestradorAnalise.ConferirRelatorioTerritorial(consulta, entrada, consulta));
    }

    [Fact]
    public void AnaliseChatGpt_MapaDoPdfPreservaImagemELegendaComBaseIndisponivel()
    {
        using var c = new Cena("pdf-territorial", "ok", Provedor.ChatGpt);
        var exemplo = Ambiente.Corpus("curto.json");
        var pacote = Importador.Importar(File.ReadAllBytes(exemplo), exemplo, ModeloOficial.Carregar(), ContextoUsuario.Atual());
        var png = ModeloOficial.LerArquivo("exemplo_mapa.png");
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png));
        var nome = $"mapa-pdf-{hash}.png";
        var shaPdf = new string('d', 64);
        var arquivo = "anexo-02.pdf";
        var mapa = new { arquivo_png = nome, sha256_png = hash, sha256_pdf = shaPdf, pagina = 1, recorte = new[] { 0d, .4, 1, .6 } };
        void Salvar(string nomeArquivo, object dados) => File.WriteAllText(Path.Combine(c.Pasta, nomeArquivo), JsonSerializer.Serialize(dados));
        Salvar("entrada-editor.json", new { pecas = new[] { new { relativo = arquivo, sha256 = shaPdf } } });
        var consulta = new { modalidade = "RELATORIO_PDF", situacao = "CONSULTADA", resultado = "CANDIDATO", fonte = "Anexo 02, p. 1",
            relatorio = new { arquivo, sha256 = shaPdf, paginas = new[] { 1 } }, mapa_pdf = mapa };
        Salvar("consulta-qgis.json", consulta);
        Salvar("conferencia.json", new { consulta_qgis = consulta });
        Salvar(nome + ".json", mapa);
        File.WriteAllBytes(Path.Combine(c.Pasta,nome), png);
        var imagem = $"assets/{hash}.png";
        pacote = pacote with { Assets = new Dictionary<string, byte[]> { [imagem] = png } };
        var doc = pacote.Projeto.Documento;
        doc["_conferencia_chatgpt"] = JsonSerializer.SerializeToNode(new { consulta_qgis = consulta });
        doc["localizacao"] = JsonSerializer.SerializeToNode(new { situacao = "A_CONFIRMAR", imagem, origem_grafica = "relatorio_pdf" });
        ConteudoPacote Conferir() => OrquestradorAnalise.CompletarMapaDaEntrega(pacote, c.Pasta, Path.Combine(c.Pasta, "base-ausente"), default);
        Assert.Same(pacote, Conferir());
        File.AppendAllText(Path.Combine(c.Pasta,nome), "alterado");
        Assert.Throws<InvalidDataException>(() => Conferir());
    }

    [Theory]
    [InlineData("turn.failed", "usage_limit_reached", true)]
    [InlineData("error", "insufficient_quota", true)]
    [InlineData("error", "rate_limit_exceeded", false)]
    [InlineData("error", "timeout", false)]
    [InlineData("item.completed", "usage_limit_reached", false)]
    public void AnaliseChatGpt_CotaPrecisaDeRecusaDoServico(string tipo, string codigo, bool esperado)
    {
        var evento = new JsonObject { ["type"] = tipo, ["error"] = new JsonObject { ["code"] = codigo } };
        Assert.Equal(esperado, DiarioAnaliseChatGpt.RecusaCota(evento));
    }

    [Fact]
    public void AnaliseChatGpt_UsaReferenciaConfirmadaPelaAuditoriaSemSubstituirOCandidato()
    {
        using var c = new Cena("revisao-cartografica", "ok", Provedor.ChatGpt);
        var candidato = Directory.CreateDirectory(Path.Combine(c.Pasta, "mapa-nativo")).FullName;
        File.WriteAllText(Path.Combine(candidato, "FICHA.JSON"), """{"resultado":"CANDIDATO"}""");
        var confirmado = Directory.CreateDirectory(Path.Combine(c.Pasta, "confirmado")).FullName;
        var mapa = Path.Combine(confirmado, "MAPA.PNG");
        File.WriteAllBytes(mapa, ModeloOficial.LerArquivo("exemplo_mapa.png"));
        var hashMapa = EstadoDisco.Ler(mapa).Sha256.ToLowerInvariant();
        var ficha = Path.Combine(confirmado, "FICHA.JSON");
        File.WriteAllText(ficha, JsonSerializer.Serialize(new { resultado = "CONFIRMADO", sha256_png = hashMapa }));
        var fontes = new JsonArray(new JsonObject { ["arquivo"] = "confirmado/FICHA.JSON", ["sha256"] = EstadoDisco.Ler(ficha).Sha256.ToLowerInvariant() });
        Assert.Equal("CONFIRMADO", OrquestradorAnalise.LerFichaCartografica(c.Pasta, fontes, hashMapa)?["resultado"]?.ToString());
        Assert.Equal("CANDIDATO", OrquestradorAnalise.LerFichaCartografica(c.Pasta, null, hashMapa)?["resultado"]?.ToString());
        File.AppendAllText(mapa, "alterado");
        Assert.Throws<InvalidDataException>(() => OrquestradorAnalise.LerFichaCartografica(c.Pasta, fontes, hashMapa));
    }

    [Fact]
    public void AnaliseChatGpt_MigraPerfilSemAlterarCobertura()
    {
        const string antigo = """[{"id":4,"secoes":[5,23],"modelo":"gpt-6.2-sol","esforco":"low"}]""";
        var atual = antigo.Replace("gpt-6.2-sol", "gpt-6.1-terra").Replace("low", "medium");
        Assert.True(OrquestradorAnalise.PlanoCompativel(antigo, atual));
        Assert.False(OrquestradorAnalise.PlanoCompativel(antigo, atual.Replace("23", "24")));
        Assert.False(OrquestradorAnalise.PlanoCompativel(antigo.Replace("gpt-6.2-sol", "outro"), atual));
    }

    [Fact]
    public void AnaliseChatGpt_ConfereTresVistasDoTerraComFontesPerfilEPng()
    {
        using var c = new Cena("vistas-terra", "ok", Provedor.ChatGpt);
        var exemplo = Ambiente.Corpus("curto.json");
        var pacote = Importador.Importar(File.ReadAllBytes(exemplo), exemplo, ModeloOficial.Carregar(), ContextoUsuario.Atual());
        var bytes = ModeloOficial.LerArquivo("exemplo_mapa.png");
        var png = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var imagem = $"assets/{png}.png";
        pacote = pacote with { Assets = new Dictionary<string, byte[]> { [imagem] = bytes } };
        File.WriteAllBytes(Path.Combine(c.Pasta, "vista.png"), bytes);
        File.WriteAllText(Path.Combine(c.Pasta, "geometria.svg"), "<svg/>");
        File.WriteAllText(Path.Combine(c.Pasta, "prancha.txt"), "Cota do projeto");
        object Fonte(string arquivo) => new { arquivo, sha256 = EstadoDisco.Ler(Path.Combine(c.Pasta, arquivo)).Sha256.ToLowerInvariant() };
        var painel = JsonSerializer.SerializeToNode(new { origem = "terra_vetorial", situacao = "conferida",
            arquivo_png = "vista.png", sha256_png = png, geometria = Fonte("geometria.svg"), fontes = new[] { Fonte("prancha.txt") },
            conferencia_visual = new { comparacao_fontes = "Orientação e cota conferidas", legibilidade = "Cota legível", largura_mm = 83 } })!;
        var grafica = JsonSerializer.SerializeToNode(new { modelo = "gpt-6.1-terra", esforco = "medium",
            perfil_efetivo = new { modelo = "gpt-6.1-terra", esforco = "medium", sessao = Guid.NewGuid(), fonte = "turn_context" } })!;
        var conferencia = new JsonObject { ["grafica"] = grafica };
        var doc = pacote.Projeto.Documento;
        doc["diagrama"] = new JsonObject();
        foreach (var nome in new[] { "mapa", "planta", "volumetria" })
        {
            var vista = new JsonObject { ["imagem"] = imagem };
            if (nome == "mapa") doc["localizacao"] = vista;
            else doc["diagrama"]![nome] = vista;
            grafica[nome] = painel.DeepClone();
        }
        void Conferir() => OrquestradorAnalise.ConferirFigurasTerra(pacote, conferencia, c.Pasta);
        Conferir();
        grafica["perfil_efetivo"]!["esforco"] = "low";
        Assert.Throws<InvalidDataException>(Conferir);
        grafica["perfil_efetivo"]!["esforco"] = "medium";
        grafica["planta"]!["fontes"] = new JsonArray();
        Assert.Throws<InvalidDataException>(Conferir);
        grafica["planta"] = painel.DeepClone();
        File.AppendAllText(Path.Combine(c.Pasta, "vista.png"), "alterado");
        Assert.Throws<InvalidDataException>(Conferir);
    }

    [Fact]
    public void AnaliseChatGpt_ReconheceAMesmaAreaSerializadaSemIgnorarDivergencias()
    {
        var conferencia = JsonNode.Parse("""{"feicao":6130,"geometria":{"feicao_area_m2":2660.6266925285272}}""")!;
        var pacote = JsonNode.Parse("""{"feicao":6130,"geometria":{"feicao_area_m2":2660.626692528527}}""")!;
        Assert.True(OrquestradorAnalise.ConsultasQgisEquivalentes(pacote, conferencia));
        pacote["feicao"] = 6131;
        Assert.False(OrquestradorAnalise.ConsultasQgisEquivalentes(pacote, conferencia));
        pacote["feicao"] = 6130;
        pacote["geometria"]!["feicao_area_m2"] = 2660.626692528528;
        Assert.False(OrquestradorAnalise.ConsultasQgisEquivalentes(pacote, conferencia));
    }

    [Theory]
    [InlineData(300, 0)]
    [InlineData(10080, 0)]
    [InlineData(300, 10080)]
    [InlineData(10080, 300)]
    [InlineData(0, 300)]
    public void AnaliseChatGpt_MedeCadaJanelaDaContaPelaDuracao(int primaria, int secundaria)
    {
        var inicio = DateTimeOffset.UtcNow;
        AmostraDaConta Amostra(int leitura)
        {
            JsonObject? Janela(int minutos) => minutos == 0 ? null : new JsonObject
            {
                ["windowDurationMins"] = minutos, ["usedPercent"] = (double)(minutos == 300 ? 10 + leitura * 7 : 50 + leitura),
                ["resetsAt"] = 1800000000L + minutos
            };
            var resposta = new JsonObject
            {
                ["accountId"] = "conta-de-teste",
                ["rateLimits"] = new JsonObject { ["primary"] = Janela(primaria), ["secondary"] = Janela(secundaria) }
            };
            return AmostraDaConta.Ler(resposta) with { Hora = inicio.AddMinutes(leitura) };
        }
        var consumo = new ConsumoDoPlugin("teste", inicio) { Inicial = Amostra(0), Ultima = Amostra(1), CotaFinal = true };
        var uso = consumo.UsoDaSessao();
        Assert.Equal(primaria == 300 || secundaria == 300 ? 7d : (double?)null, uso.Cota5h);
        Assert.Equal(primaria == 10080 || secundaria == 10080 ? 51d : (double?)null, uso.UsadaSemana);
        Assert.Null(uso.Total);
        Assert.Null(uso.Entrada);
        Assert.Null(uso.Duracao);
        var texto = consumo.Texto();
        Assert.Contains(primaria == 300 || secundaria == 300
            ? "5 horas · 10% → 17% · variação da conta 7 p.p."
            : "5 horas · janela não informada pela conta", texto);
        Assert.Contains(primaria == 10080 || secundaria == 10080
            ? "Semana · 50% → 51% · variação da conta 1 p.p."
            : "Semana · janela não informada pela conta", texto);
    }

    [Fact]
    public void AnaliseChatGpt_AnexoDeTrabalhoNaoArquivaDocumentoNoProcesso()
    {
        using var c = new Cena("entrada-local", "ok", Provedor.ChatGpt);
        var pe = c.Pedido(Provedor.ChatGpt) with { UsarAnaliseChatGpt = true };
        var local = Directory.CreateDirectory(Path.Combine(pe.Registro.Pasta, "pecas-anexadas")).FullName;
        var arquivo = Path.Combine(c.Pasta, "anexo.pdf");
        File.WriteAllText(arquivo, "%PDF-1.4 exemplo");
        var anexos = Arquivamento.Arquivar(local, Arquivamento.Planejar(local, [(arquivo, DestinoAnexo.Bci)]), CancellationToken.None);
        var anexo = Assert.Single(anexos);
        var caminho = Path.Combine(local, anexo.Relativo);
        var entrada = JsonNode.Parse(OrquestradorAnalise.EntradaChatGpt(pe with { Anexos = anexos }, [caminho], "fontes.json", CancellationToken.None))!;
        Assert.Equal(caminho, (string?)entrada["pecas"]![0]!["caminho"]);
        Assert.False(File.Exists(Path.Combine(c.Processo.Pasta, anexo.Relativo)));
        Assert.True(File.Exists(arquivo));
    }

    [Fact]
    public void AnaliseChatGpt_EntradaUsaProcessoPedidoEAnexosArquivados()
    {
        using var c = new Cena("analise-chatgpt-entrada", "ok", Provedor.ChatGpt);
        var bci = Path.Combine(c.Pasta, "cadastro-ficticio.pdf");
        var art = Path.Combine(c.Pasta, "responsabilidade-ficticia.pdf");
        File.WriteAllText(bci, "%PDF-1.4 cadastro fictício");
        File.WriteAllText(art, "%PDF-1.4 ART fictícia");
        var plano = Arquivamento.Planejar(c.Processo.Pasta, [(bci, DestinoAnexo.Bci), (art, DestinoAnexo.ArtRrt)]);
        var anexos = Arquivamento.Arquivar(c.Processo.Pasta, plano, CancellationToken.None);
        const string pedido = "Confira a reapresentação.\nDê atenção à iluminação.";
        var pe = c.Pedido(Provedor.ChatGpt, texto: pedido, anexos: anexos);
        var planta = Path.Combine(c.Processo.Pasta, "PROJETO", "INICIAL", "planta-ficticia.pdf");
        var despacho = Path.Combine(c.Processo.Pasta, "DESPACHO 01 - 2099-01-01", "Despacho_01.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(despacho)!);
        File.WriteAllText(despacho, "%PDF-1.4 despacho fictício");
        var caminhos = anexos.Select(a => Path.Combine(c.Processo.Pasta, a.Relativo)).Append(planta).ToArray();

        var json = JsonNode.Parse(OrquestradorAnalise.EntradaChatGpt(pe, caminhos, "fontes-kit.json", CancellationToken.None))!;
        Assert.Equal(1, json["processo"]!["numero"]!.GetValue<int>());
        Assert.Equal(2099, json["processo"]!["ano"]!.GetValue<int>());
        Assert.Equal(c.Processo.Protocolo, json["processo"]!["identificacao"]!.GetValue<string>());
        Assert.Equal(c.Processo.Pasta, json["processo"]!["pasta"]!.GetValue<string>());
        Assert.Equal(pedido, json["pedido_para_ia"]!.GetValue<string>());
        Assert.Equal(despacho, json["ultimo_emitido"]!.GetValue<string>());
        var pecas = json["pecas"]!.AsArray();
        var porCategoria = pecas.ToDictionary(p => (string)p!["categoria"]!);
        Assert.True((bool)porCategoria["bci"]!["anexado_nesta_analise"]!);
        Assert.True((bool)porCategoria["art_rrt"]!["anexado_nesta_analise"]!);
        Assert.False((bool)porCategoria["projetos"]!["anexado_nesta_analise"]!);
        Assert.Equal(caminhos.OrderBy(f => f, StringComparer.OrdinalIgnoreCase), pecas.Select(p => (string)p!["caminho"]!));
        Assert.All(pecas, p => Assert.Equal(64, ((string)p!["sha256"]!).Length));
    }

    [Fact]
    public void AnaliseChatGpt_PublicaPacoteEditavelNaEtapaSemSobrescrever()
    {
        using var c = new Cena("analise-chatgpt-pacote", "ok", Provedor.ChatGpt);
        var hoje = new DateOnly(2099, 2, 3);
        var anterior = Directory.CreateDirectory(Path.Combine(c.Processo.Pasta, "DESPACHO 01 - 2099-02-02")).FullName;
        var aberto = Directory.CreateDirectory(Path.Combine(c.Processo.Pasta, "DESPACHO 02 - 2099-02-03")).FullName;
        var recebido = Path.Combine(c.Pasta, "resultado.rpmvni");
        var modelo = ModeloOficial.Carregar();
        var exemplo = Ambiente.Corpus("curto.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(exemplo));
        dados["capa"]!["processo"] = "0001/2099";
        Pacote.Gravar(recebido, Importador.Importar(JsonFiel.Escrever(dados), exemplo, modelo, ContextoUsuario.Atual()), null);

        var outroProcesso = c.Processo with { Numero = 2 };
        Assert.Throws<InvalidDataException>(() => OrquestradorAnalise.PublicarPacoteChatGpt(outroProcesso, recebido, hoje));
        Assert.Empty(Directory.EnumerateFiles(aberto));

        var primeiro = OrquestradorAnalise.PublicarPacoteChatGpt(c.Processo, recebido, hoje);
        Assert.Equal(Path.Combine(aberto, "RELATORIO_0001_2099.RPMVNI"), primeiro);
        var primeiroEstado = EstadoDisco.Ler(primeiro);
        Assert.Equal(primeiro, OrquestradorAnalise.PublicarPacoteChatGpt(c.Processo, recebido, hoje.AddDays(1)));
        var outraEntrega = Path.Combine(c.Pasta, "outro-resultado.rpmvni");
        dados["conclusao"] = "Conclusão diferente no segundo relatório.";
        Pacote.Gravar(outraEntrega, Importador.Importar(JsonFiel.Escrever(dados), exemplo, modelo, ContextoUsuario.Atual()), null);
        var segundo = OrquestradorAnalise.PublicarPacoteChatGpt(c.Processo, outraEntrega, hoje);
        Assert.Equal(Path.Combine(aberto, "RELATORIO_0001_2099_2.RPMVNI"), segundo);
        Assert.True(EstadoDisco.Ler(primeiro).MesmoConteudo(primeiroEstado));

        var reaberto = Pacote.Ler(segundo);
        Assert.False(reaberto.SomenteLeitura);
        Assert.NotEmpty(reaberto.Conteudo.Projeto.Itens);
        File.WriteAllText(Path.Combine(aberto, "Despacho_0001.pdf"), "%PDF-1.4 fictício");
        Assert.Equal(segundo, OrquestradorAnalise.PublicarPacoteChatGpt(c.Processo, outraEntrega, hoje));
        var terceiraEntrega = Path.Combine(c.Pasta, "terceiro-resultado.rpmvni");
        File.Copy(outraEntrega, terceiraEntrega);
        var terceiro = OrquestradorAnalise.PublicarPacoteChatGpt(c.Processo, terceiraEntrega, hoje);
        Assert.Equal("DESPACHO 03 - 2099-02-03", Path.GetFileName(Path.GetDirectoryName(terceiro)));
        Assert.Empty(Directory.EnumerateFiles(anterior));
    }

    [Theory]
    [InlineData("openai")]
    [InlineData("codex-lb")]
    public async Task AnaliseChatGpt_SessaoUnicaCancelaRetomaEImportaSomentePacoteValido(string rota)
    {
        using var c = new Cena("analise-chatgpt-rodadas", "ok", Provedor.ChatGpt);
        var skill = Directory.CreateDirectory(Path.Combine(c.Pasta, "analise-chatgpt")).FullName;
        Directory.CreateDirectory(Path.Combine(skill, "scripts"));
        Directory.CreateDirectory(Path.Combine(skill, "references"));
        var grupos = Enumerable.Range(1, 15).Select(i => new
        {
            id = i,
            nome = "Grupo fictício " + i,
            secoes = i <= 13 ? new[] { i * 2 - 1, i * 2 } : [i + 13],
            modelo = i is 14 or 15 ? "gpt-6.2-sol" : "gpt-6.1-terra",
            esforco = i <= 2 ? "low" : "medium"
        }).ToArray();
        File.WriteAllText(Path.Combine(skill, "references", "grupos.json"), JsonSerializer.Serialize(grupos));
        var fontesMonitor = Path.Combine(skill, "references");
        var grupo = grupos[6];
        var matriz = new { grupos = new[] { new { grupo.id, grupo.modelo, grupo.esforco, auditoria = "conferida",
            secoes = grupo.secoes.Select(numero => new { numero, estado = "ANALISADO", evidencia = "Prancha fictícia 1" }) } } };
        File.WriteAllText(Path.Combine(fontesMonitor, "matriz-evidencias.json"), JsonSerializer.Serialize(matriz));
        var monitor = OrquestradorAnalise.LerMonitorChatGpt(fontesMonitor);
        Assert.Equal(1, monitor.GruposConcluidos);
        Assert.Equal(2, monitor.VerificacoesConcluidas);
        Assert.True(monitor.Rodadas.Single(r => r.Perfil.Numero == grupo.id).Confirmada);
        var parcial = new { id = grupos[0].id, secoes = new[] { new { numero = 1, estado = "ANALISADO", evidencia = "Página fictícia 4" } } };
        File.WriteAllText(Path.Combine(fontesMonitor, "grupo-01.json"), JsonSerializer.Serialize(parcial));
        File.WriteAllText(Path.Combine(fontesMonitor, "grupo-02.json"), "{");
        File.WriteAllText(Path.Combine(fontesMonitor, "consulta-qgis.json"), "{");
        monitor = OrquestradorAnalise.LerMonitorChatGpt(fontesMonitor);
        Assert.Equal(3, monitor.VerificacoesInformadas);
        Assert.Equal(1, monitor.GruposConcluidos);
        Assert.Equal(50d, monitor.Rodadas[0].Percentual);
        File.WriteAllText(Path.Combine(fontesMonitor, "id-execucao.txt"), Guid.NewGuid().ToString());
        Assert.Equal(0, OrquestradorAnalise.LerMonitorChatGpt(fontesMonitor).VerificacoesInformadas);
        var recebido = Path.Combine(c.Pasta, "fixture.rpmvni");
        var modelo = ModeloOficial.Carregar();
        var exemplo = Ambiente.Corpus("curto.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(exemplo));
        dados["capa"]!["processo"] = "0001/2099";
        var lacuna = new JsonObject { ["motivo"] = "Base indisponível na execução fictícia.", ["evidencia"] = "Consulta fictícia." };
        dados["localizacao"] = new JsonObject
        {
            ["situacao"] = "A_CONFIRMAR", ["texto"] = "A consulta fictícia registrou indisponibilidade.",
            ["fonte"] = "Consulta fictícia.", ["imagem"] = "img/mapa.png", ["sem_mapa"] = lacuna.DeepClone(),
            ["dados"] = new JsonArray(new[] { "Inscrição", "Lote", "Ruas" }.Select(rotulo =>
                new JsonObject { ["rotulo"] = rotulo, ["valor"] = "Indisponível" }).ToArray())
        };
        dados["diagrama"] = new JsonObject { ["nota"] = "Vistas sem base documental.", ["sem_calculo"] = lacuna };
        Pacote.Gravar(recebido, Importador.Importar(JsonFiel.Escrever(dados), exemplo, modelo, ContextoUsuario.Atual()), null);
        var bloqueio = Path.Combine(c.Pasta, "pausar.txt");
        var invalida = Path.Combine(c.Pasta, "invalidar.txt");
        var recusaCota = Path.Combine(c.Pasta, "cota.txt");
        var rodadas = Path.Combine(c.Pasta, "rodadas.txt");
        File.WriteAllText(bloqueio, "pausar");
        var arquivoCenario = Path.Combine(c.Pasta, "cenario-plugin.json");
        File.WriteAllText(arquivoCenario, JsonSerializer.Serialize(new
        {
            analise_chatgpt = new { pacote = recebido, rota, pausa = bloqueio, recusa_cota = recusaCota, montagem_invalida = invalida, exigir_equivalente = true, rodadas, grupos }
        }));
        var pedido = c.Pedido(Provedor.ChatGpt) with
        {
            UsarAnaliseChatGpt = true, RotaCodex = rota,
            Pecas = c.Pecas with { AnaliseChatGpt = skill, VersaoAnaliseChatGpt = OrquestradorAnalise.VersaoMinimaAnaliseChatGpt, Node = c.Pecas.Codex }
        };
        var orq = new OrquestradorAnalise { AmbienteExtra = new Dictionary<string, string> { ["AGENTE_FALSO_CENARIO"] = arquivoCenario } };
        using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var analise = orq.ExecutarAsync(pedido, c.Progresso, ct.Token);
        try
        {
            while (!File.Exists(rodadas))
                await Task.Delay(40, ct.Token);
        }
        finally { await ct.CancelAsync(); }
        var cancelada = await analise;
        Assert.True(cancelada.Situacao == "cancelada", cancelada.Mensagem);
        var trabalho = OrquestradorAnalise.PastaAnaliseChatGpt(pedido.RaizDados, pedido.Processo.Pasta, pedido.Registro.Pasta);
        File.Delete(bloqueio);
        File.WriteAllText(invalida, "recusar");
        Assert.Equal(rota, pedido.Registro.AcessoDaExecucao().RotaCodex);
        var retomar = pedido with { Retomar = true, Texto = "", RotaCodex = rota == "openai" ? "codex-lb" : "openai" };
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var parcialSalvo = Path.Combine(trabalho, "grupo-07.json");
        var id = File.ReadAllText(Path.Combine(trabalho, "id-execucao.txt"));
        var evidencia = JsonSerializer.Serialize(new { id = 7, id_execucao = id, secoes = parcial.secoes });
        File.WriteAllText(parcialSalvo, evidencia);
        File.WriteAllText(recusaCota, "recusa estruturada do serviço simulado");
        var semCota = await orq.ExecutarAsync(retomar, c.Progresso, prazo.Token);
        Assert.Equal("cota_esgotada", semCota.Situacao);
        Assert.Null(semCota.Documento);
        Assert.Equal(evidencia, File.ReadAllText(parcialSalvo));
        Assert.Equal(id, File.ReadAllText(Path.Combine(trabalho, "id-execucao.txt")));
        File.Delete(recusaCota);
        var recuperando = orq.ExecutarAsync(retomar, c.Progresso, prazo.Token);
        bool IniciouTerceiraRodada()
        {
            using var arquivo = new FileStream(rodadas, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var leitor = new StreamReader(arquivo);
            return leitor.ReadLine() is not null && leitor.ReadLine() is not null && leitor.ReadLine() == "exec";
        }
        try
        {
            while (!IniciouTerceiraRodada()) await Task.Delay(40, prazo.Token);
            Assert.Empty(Directory.EnumerateFiles(pedido.Processo.Pasta, "*.rpmvni", SearchOption.AllDirectories));
            File.Delete(invalida);
        }
        catch
        {
            await prazo.CancelAsync();
            await recuperando;
            throw;
        }
        var entrega = await recuperando;
        Assert.True(entrega.Situacao == "aguardando_abertura", entrega.Mensagem);
        var tentativas = DiarioAnaliseChatGpt.Listar(Path.Combine(trabalho, DiarioAnaliseChatGpt.Pasta));
        Assert.Equal(3, tentativas.Count);
        var atual = tentativas[0];
        Assert.True(atual.Consumo.Entrada >= 120L);
        Assert.True(atual.Consumo.Cache >= 85L);
        Assert.True(atual.Consumo.Saida >= 12L);
        Assert.True(atual.Consumo.CotaFinal);
        Assert.Contains("janela não informada", atual.Consumo.Texto());
        Assert.Equal(10080, Assert.Single(atual.Consumo.Ultima!.Janelas).Minutos);
        var log = File.ReadAllLines(DiarioAnaliseChatGpt.LogDe(atual.Arquivo)).Select(l => JsonSerializer.Deserialize<EventoDoPlugin>(l)!).ToList();
        Assert.Contains(log, l => l.Detalhe.Contains("resultado integral fictício"));
        Assert.Contains(log, l => l.Tipo == "stderr" && l.Detalhe == "diagnóstico fictício");
        Assert.Contains(log, l => l.Tipo == "arquivo" && l.Titulo.Contains("entrada-editor"));
        Assert.Contains(log, evento => evento.Tipo == "turn.failed" && evento.Detalhe.Contains("model gpt-6.2-sol"));
        Assert.Equal("entrega", log.Last().Tipo);
        Assert.Null(atual.Consumo.Fim);
        DiarioAnaliseChatGpt.ConfirmarAbertura(trabalho, entrega.Documento!);
        var aberto = DiarioAnaliseChatGpt.LerConsumo(atual.Arquivo)!;
        Assert.Equal("concluida", aberto.Estado);
        Assert.NotNull(aberto.Fim);
        Assert.Equal("fim", JsonSerializer.Deserialize<EventoDoPlugin>(File.ReadAllLines(DiarioAnaliseChatGpt.LogDe(atual.Arquivo)).Last())!.Tipo);
        Assert.False(Pacote.Ler(entrega.Documento!).SomenteLeitura);
        Assert.Equal(entrega.Documento, (await orq.ExecutarAsync(retomar, c.Progresso, CancellationToken.None)).Documento);
        var antesDaRetomadaLocal = File.ReadAllLines(rodadas);
        Assert.InRange(antesDaRetomadaLocal.Length, 3, 5); // Inclui a correção automática da montagem inválida.
        File.Move(Path.Combine(trabalho, "relatorio.rpmvni"), Path.Combine(trabalho, "entrega-plugin.rpmvni"));
        Assert.Equal(entrega.Documento, (await orq.ExecutarAsync(retomar, c.Progresso, CancellationToken.None)).Documento);
        Assert.Equal(antesDaRetomadaLocal, File.ReadAllLines(rodadas));
    }

    [Theory]
    [InlineData("CONSULTADA", 12, true)]
    [InlineData("CONSULTADA", 34, false)]
    [InlineData("INDISPONIVEL", 12, false)]
    public void AnaliseChatGpt_SoAceitaMapaDeUmaFeicaoDaConsulta(string situacao, int numero, bool esperado)
    {
        var consulta = JsonNode.Parse("""{"candidatos":[{"feicao":12}]}""")!.AsObject();
        consulta["situacao"] = situacao;
        var territorial = new JsonObject { ["resultado"] = "CANDIDATO", ["feicao"] = numero };
        Assert.Equal(esperado, OrquestradorAnalise.FeicaoDaConsultaConfere(consulta, territorial));
    }

    [Fact]
    public async Task AnaliseChatGpt_RecusaPluginAusenteEContaIndisponivel()
    {
        using var c = new Cena("analise-chatgpt-sem-conta", "ok", Provedor.ChatGpt, dados =>
        {
            dados["analise_chatgpt"] = new JsonObject();
            dados["login"] = "nao";
        });
        var pedido = c.Pedido(Provedor.ChatGpt) with { UsarAnaliseChatGpt = true };
        var semPlugin = await c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None);
        Assert.Contains("plugin analise-chatgpt", semPlugin.Mensagem);
        var pronto = pedido with { Pecas = c.Pecas with { AnaliseChatGpt = c.Pasta, VersaoAnaliseChatGpt = OrquestradorAnalise.VersaoMinimaAnaliseChatGpt, Node = c.Pecas.Codex } };
        using var cancelar = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var fim = await c.Orq().ExecutarAsync(pronto, c.Progresso, cancelar.Token);
        Assert.Equal("cancelada", fim.Situacao);
        var pasta = OrquestradorAnalise.PastaAnaliseChatGpt(pronto.RaizDados, pronto.Processo.Pasta, pronto.Registro.Pasta);
        Assert.Contains(Directory.GetFiles(Path.Combine(pasta, DiarioAnaliseChatGpt.Pasta), "*.eventos.jsonl"),
            log => File.ReadAllText(log).Contains("Aguardando a conta ChatGPT"));

    }
}
