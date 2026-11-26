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

    [Fact]
    public async Task AnaliseChatGpt_SessaoUnicaCancelaRetomaEImportaSomentePacoteValido()
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
        var recebido = Path.Combine(c.Pasta, "fixture.rpmvni");
        var modelo = ModeloOficial.Carregar();
        var exemplo = Ambiente.Corpus("curto.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(exemplo));
        dados["capa"]!["processo"] = "0001/2099";
        Pacote.Gravar(recebido, Importador.Importar(JsonFiel.Escrever(dados), exemplo, modelo, ContextoUsuario.Atual()), null);
        var bloqueio = Path.Combine(c.Pasta, "pausar.txt");
        var invalida = Path.Combine(c.Pasta, "invalidar.txt");
        var rodadas = Path.Combine(c.Pasta, "rodadas.txt");
        File.WriteAllText(bloqueio, "pausar");
        var arquivoCenario = Path.Combine(c.Pasta, "cenario-plugin.json");
        File.WriteAllText(arquivoCenario, JsonSerializer.Serialize(new
        {
            analise_chatgpt = new { pacote = recebido, pausa = bloqueio, montagem_invalida = invalida, rodadas, grupos }
        }));
        var pedido = c.Pedido(Provedor.ChatGpt) with
        {
            UsarAnaliseChatGpt = true,
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
        Assert.Equal("cancelada", (await analise).Situacao);
        var trabalho = OrquestradorAnalise.PastaAnaliseChatGpt(pedido.RaizDados, pedido.Processo.Pasta, pedido.Registro.Pasta);
        File.Delete(bloqueio);
        File.WriteAllText(invalida, "recusar");
        var retomar = pedido with { Retomar = true, Texto = "" };
        var falhou = await orq.ExecutarAsync(retomar, c.Progresso, CancellationToken.None);
        Assert.Equal("falhou", falhou.Situacao);
        Assert.Empty(Directory.EnumerateFiles(pedido.Processo.Pasta, "*.rpmvni", SearchOption.AllDirectories));
        File.Delete(invalida);
        var registroDeErros = new System.Collections.Concurrent.ConcurrentQueue<Exception>();
        var registrarAnterior = MensagensDeErro.Registrar;
        FimAnalise entrega;
        try
        {
            MensagensDeErro.Registrar = registroDeErros.Enqueue;
            entrega = await orq.ExecutarAsync(retomar, c.Progresso, CancellationToken.None);
        }
        finally { MensagensDeErro.Registrar = registrarAnterior; }
        Assert.True(entrega.Situacao == "concluida", entrega.Mensagem + Environment.NewLine + string.Join(Environment.NewLine, registroDeErros));
        var tentativas = DiarioAnaliseChatGpt.Listar(Path.Combine(trabalho, DiarioAnaliseChatGpt.Pasta));
        Assert.Equal(3, tentativas.Count);
        var atual = tentativas[0];
        Assert.Equal(120L, atual.Consumo.Entrada);
        Assert.Equal(85L, atual.Consumo.Cache);
        Assert.Equal(12L, atual.Consumo.Saida);
        Assert.True(atual.Consumo.CotaFinal);
        Assert.Contains("janela não informada", atual.Consumo.Texto());
        Assert.Equal(10080, Assert.Single(atual.Consumo.Ultima!.Janelas).Minutos);
        var log = File.ReadAllLines(DiarioAnaliseChatGpt.LogDe(atual.Arquivo)).Select(l => JsonSerializer.Deserialize<EventoDoPlugin>(l)!).ToList();
        Assert.Contains(log, l => l.Detalhe.Contains("resultado integral fictício"));
        Assert.Contains(log, l => l.Tipo == "stderr" && l.Detalhe == "diagnóstico fictício");
        Assert.Contains(log, l => l.Tipo == "arquivo" && l.Titulo.Contains("entrada-editor"));
        Assert.Equal("fim", log.Last().Tipo);
        Assert.False(Pacote.Ler(entrega.Documento!).SomenteLeitura);
        Assert.Equal(entrega.Documento, (await orq.ExecutarAsync(retomar, c.Progresso, CancellationToken.None)).Documento);
        Assert.Equal(new[] { "exec", "exec", "exec" }, File.ReadAllLines(rodadas));
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
        var semLogin = await c.Orq().ExecutarAsync(pronto, c.Progresso, CancellationToken.None);
        Assert.Contains("Entre no Codex com sua conta ChatGPT", semLogin.Mensagem);
    }
}
