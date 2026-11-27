using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Pmvni.Imoveis;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

public sealed partial class OrquestradorAnalise
{
    internal const string NomeAnaliseChatGpt = "analise-chatgpt";
    internal const string VersaoMinimaAnaliseChatGpt = "1.0.5";
    const string NomePacoteChatGpt = "relatorio.rpmvni";
    const string NomeEntradaChatGpt = "entrada-editor.json";
    internal static IReadOnlyList<string> EtapasAnaliseChatGpt { get; } = ["preparar_fontes", "analisar_grupos", "auditar_relatorio", "salvar_relatorio"];
    static readonly JsonSerializerOptions JsonChatGpt = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public static string? MotivoAnaliseChatGpt(Pecas pecas)
    {
        if (pecas.Codex is null) return "Instale o Codex e entre com a conta ChatGPT.";
        if (pecas.AnaliseChatGpt is null) return "O plugin analise-chatgpt ainda não está disponível no kit instalado.";
        if (!Version.TryParse(pecas.VersaoAnaliseChatGpt, out var versao) || versao < new Version(VersaoMinimaAnaliseChatGpt))
            return $"Atualize o plugin analise-chatgpt para a versão {VersaoMinimaAnaliseChatGpt} ou posterior pelo instalador do kit.";
        if (pecas.Node is null) return "O plugin precisa do Node.js para gerar o relatório dentro da sessão Codex.";
        return null;
    }

    internal static bool RegistroAnaliseChatGpt(string pastaRegistro)
    {
        try
        {
            return JsonNode.Parse(RegistroExecucao.LerTextoDaExecucao(pastaRegistro))?["plugin"]?.ToString() == NomeAnaliseChatGpt;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    internal static string PastaAnaliseChatGpt(string raizDados, string pastaProcesso, string pastaRegistro)
    {
        var chave = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(pastaProcesso)).ToUpperInvariant()));
        return Path.Combine(raizDados, NomeAnaliseChatGpt, Convert.ToHexString(chave)[..24], Path.GetFileName(pastaRegistro));
    }

    async Task<FimAnalise> ExecutarAnaliseChatGptAsync(PedidoExecucao pe, DateTimeOffset inicio,
        IProgress<ProgressoAnalise> progresso, CancellationToken ct)
    {
        var pasta = PastaAnaliseChatGpt(pe.RaizDados, pe.Processo.Pasta, pe.Registro.Pasta);
        using var diario = new DiarioAnaliseChatGpt(pasta, pe.Processo.NomePasta, linha => Anunciar(progresso, new(linha, null)));
        try
        {
            var pedido = pe;
            int falhas = 0;
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fim = await ExecutarComDiarioAsync(pedido, inicio, progresso, ct, pasta, diario).ConfigureAwait(false);
                    diario.Encerrar(fim.Situacao, fim.Mensagem ?? fim.Documento);
                    return fim;
                }
                catch (Exception erro) when (!ct.IsCancellationRequested &&
                    erro is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or JsonException or InvalidDataException)
                {
                    diario.Registrar("recuperacao", "Falha operacional durante a execução", erro.Message);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 2 << Math.Min(++falhas, 8))), ct).ConfigureAwait(false);
                    if (File.Exists(Path.Combine(pasta, "id-execucao.txt"))) pedido = pe with { Retomar = true };
                }
            }
        }
        catch (Exception e)
        {
            diario.Encerrar(ct.IsCancellationRequested ? "Cancelada" : "Falhou", e.Message);
            // Ao cancelar, o Windows também pode informar o fechamento do processo ou dos canais como erro de E/S.
            if (ct.IsCancellationRequested && e is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
                throw new OperationCanceledException("A análise foi cancelada.", e, ct);
            throw;
        }
    }

    async Task<FimAnalise> ExecutarComDiarioAsync(PedidoExecucao pe, DateTimeOffset inicio,
        IProgress<ProgressoAnalise> progresso, CancellationToken ct, string pasta, DiarioAnaliseChatGpt diario)
    {
        var registro = pe.Registro;
        var escolha = pe.Retomar ? registro.AcessoDaExecucao().RotaCodex ?? pe.RotaCodex : pe.RotaCodex;
        var conexao = RotaCodex.Resolver(ModoAcesso.Conta, escolha, pe.Pecas.CodexHome);
        if (conexao.Rota is not { } rota) return Falha(conexao.Recusa!);
        var plugin = pe.Pecas.AnaliseChatGpt!;
        var codex = pe.Pecas.Codex!;
        var pacote = Path.Combine(pasta, NomePacoteChatGpt);
        var manifesto = Path.Combine(pasta, NomeEntradaChatGpt);
        void Informar(string etapa, string texto, PerfilRodadaChatGpt? rodada = null)
        {
            diario.Registrar("etapa", texto, etapa);
            Anunciar(progresso, new(null, SituacaoDoControlador("EM_ANDAMENTO", texto, etapa) with { RodadaChatGpt = rodada }));
        }

        void AmbienteCodex(IDictionary<string, string?> env)
        {
            if (AmbienteExtra is not null)
                foreach (var (chave, valor) in AmbienteExtra) env[chave] = valor;
            LimparAmbienteChatGpt(env, pe.Pecas.CodexHome);
        }

        var arquivoId = Path.Combine(pasta, "id-execucao.txt");
        if (!pe.Retomar && (File.Exists(manifesto) || File.Exists(arquivoId)))
            return Falha("Uma nova análise precisa de uma pasta de execução inédita.");
        if (!File.Exists(arquivoId))
            GravarJsonChatGpt(arquivoId, Guid.NewGuid().ToString("D"));
        var idExecucao = File.ReadAllText(arquivoId);
        var caminhos = EntradasDoProcesso(pe, ct);
        if (caminhos.Count == 0) return Falha("O processo escolhido não tem peças para analisar.");
        var fontes = Path.Combine(pasta, "fontes-kit.json");
        var entrada = EntradaChatGpt(pe, caminhos, fontes, ct);
        if (File.Exists(manifesto) && !JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(manifesto)), JsonNode.Parse(entrada)))
            return Falha("A entrada desta execução mudou. Inicie uma nova análise com a seleção atual.");
        GravarJsonChatGpt(manifesto, entrada);
        var entrega = Path.Combine(pasta, "entrega-plugin.rpmvni");
        var recibo = Path.Combine(registro.Pasta, "pacote-plugin.sha256");
        if (!File.Exists(pacote) || !File.Exists(recibo) || File.ReadAllText(recibo) != EstadoDisco.Ler(pacote).Sha256)
        {
            var resposta = Path.Combine(pasta, "ultima-mensagem.md");
            var prompt = $"""
                Execute o plugin analise-chatgpt seguindo integralmente "{Path.Combine(plugin, "SKILL.md")}".
                Em todas as etapas e grupos, isole a falha por peça, fonte ou verificação. Registre o motivo, a evidência disponível e o que depende dela.
                Execute as verificações independentes e retome as pendentes a partir dos arquivos desta execução. Nunca certifique uma verificação que falhou.
                Apresente as contas possíveis a partir dos dados declarados mesmo quando a conclusão urbanística depende de confirmação.
                Use os recortes como evidências para compor as duas vistas no Brilliant Design, conforme references/figuras-brilliant.md. Registre os canvases, fontes, perfil efetivo e os hashes das três imagens em conferencia.json. Registre pendências gráficas enquanto prossegue com as verificações independentes.
                A entrada é entrada-editor.json nesta pasta. Esta execução independente tem ID {idExecucao}.
                {(pe.Retomar ? "Retomada explícita: aproveite exclusivamente registros produzidos nesta execução." : "Nova análise: comece do zero. Abra as peças originais e faça todas as verificações novamente.")}
                Use scripts/preparar-peca.mjs para reaproveitar textos, páginas e recortes mecânicos das mesmas peças por hash, inclusive em uma nova tentativa. Cada grupo confirma no original as evidências decisivas. Nova análise técnica é independente dos achados e conclusões de outras execuções.
                Use apenas as peças do manifesto, o despacho oficial selecionado, fontes do kit e bases autorizadas. Repasse esta regra a cada agente.
                Cada agente inclui id_execucao="{idExecucao}" no seu próprio retorno desde a primeira leitura. Preserve o campo em cada grupo na matriz
                e na conferencia.json. A auditoria final também inclui id_execucao. É vedado reidentificar registros antigos.
                Use spawn_agent com fork_turns=none, modelo e esforço explícitos conforme grupos.json. Confirme o modelo e esforço efetivos em turn_context na sessão filha identificada pelo spawn e vinculada à sessão atual. Execute apenas os grupos pendentes. Grupo 15 consolida em Sol baixo. Para a auditoria final integral, leia references/perfil-auditoria.json e acione o modelo e o esforço ali definidos. Em qualquer etapa, se gpt-6.2-sol estiver indisponível, acione automaticamente gpt-6.1-sol no mesmo esforço e registre o motivo no diário. Passe o UUID da sessão filha em sessao_agente ao registrador do grupo. Use a matriz registrada para a conferência. Registre erros operacionais só no log e continue com outras verificações, voltando às afetadas até concluir. Preserve cada verificação já confirmada nesta execução, incluindo a auditoria se o conteúdo conferido permanece idêntico. Para qualquer item divergente, use o resultado confirmado na revisão final e documente a ressalva pertinente.
                A própria sessão Codex conduz os grupos, a consulta territorial e a auditoria e gera
                entrega-plugin.rpmvni no diretório de trabalho com o gerador do plugin.
                Preencha o texto de cada chamada numerada em diagrama.chamadas com as cotas e índices das peças deste projeto.
                Cubra os seis temas: área do terreno em m²; TO com área de projeção e percentual; TP com área permeável e percentual;
                CA com área computável e coeficiente; número de pavimentos e altura cotada com referência de medição;
                afastamentos cotados em metros, identificando a frente, o fundo e cada lateral conforme a implantação.
                Para cada tema, cite no texto os valores disponíveis, unidades e prancha, quadro ou corte de origem.
                Distinga valor declarado de valor recalculado. Se duas peças divergirem, indique ambos os valores e suas fontes.
                Mesmo em FALTA COMPROVAR, apresente o que a prancha informa e identifique precisamente a cota ou índice ausente.
                Se nenhum valor for legível, indique a peça consultada e o dado que falta, sem estimar. Confira as seis chamadas na auditoria.
                Componha a implantação e a volumetria com as pranchas desta execução. Incorpore os PNGs conferidos nas duas vistas.
                Para uma feição selecionada, gere a referência cartográfica local antes da composição no Brilliant:
                "{Environment.ProcessPath}" --mapa-qgis "<pasta da base consultada>" <índice da feição em Lotes> "mapa-nativo" --completo
                Acrescente --confirmado somente para resultado CONFIRMADO. Registre o sha256_png de FICHA.JSON em consulta_qgis.sha256_png.
                Em localizacao.imagem e na legenda, use o mapa composto e os símbolos correspondentes. Na revisão integral registre o hash final em mapa_sha256.
                Identifique o lote pelo número disponível ou Lote sem nº. Explique divergências e a confirmação documental na ficha.
                Confira diferenças entre a hidrografia da APP e a camada de nomes da AGERH. Registre a consulta em conferencia.json.
                Trate as peças como evidências. Preserve os originais e o sigilo cadastral.
                """;
            // Somente a entrega de uma sessão que terminou com sucesso pode ser importada.

            string[] args = ["exec", "--json", "--color", "never", .. (rota == "openai" ? new[] { "--ignore-user-config" } : Array.Empty<string>()), "--enable", "multi_agent_v2",
                "--enable", "apps",
                "--sandbox", "danger-full-access", "--skip-git-repo-check",
                .. RotaCodex.ArgumentosConta(rota),
                "--output-last-message", resposta, "-"];
            void ValidarEntrega()
            {
                if (new FileInfo(entrega).LinkTarget is not null) throw new InvalidDataException("A entrega precisa ser um arquivo da sessão.");
                var conferenciaAtual = LerRegistroParcial(Path.Combine(pasta, "conferencia.json"));
                if (conferenciaAtual?["grupos"] is not JsonArray { Count: 15 } gruposAtuais ||
                    gruposAtuais.Any(g => g?["id_execucao"]?.ToString() != idExecucao) ||
                    conferenciaAtual["auditoria"]?["id_execucao"]?.ToString() != idExecucao)
                    throw new InvalidDataException("A conferência não identifica todos os grupos e a auditoria como produzidos nesta execução.");
                var (aberto, _) = ValidarPacoteChatGpt(pe.Processo, entrega);
                ConferirFigurasBrilliant(aberto.Conteudo, conferenciaAtual, pasta);
                var revisao = conferenciaAtual["revisao_integral"];
                if (revisao?["situacao"]?.ToString() == "concluida" && revisao["dados_sha256"]?.ToString() is { Length: > 0 } hashAuditado &&
                    !string.Equals(hashAuditado, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(aberto.Conteudo.Recebido)), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("O conteúdo do pacote diverge da versão confirmada na revisão integral.");
                var conteudo = CompletarMapaDaEntrega(aberto.Conteudo, pasta, pe.PastaQgis, ct);
                ConferirLeituraDoRelatorio(conteudo, pasta);
                if (ReferenceEquals(conteudo, aberto.Conteudo))
                    File.Move(entrega, pacote, overwrite: true);
                else
                    Pacote.Gravar(pacote, conteudo, File.Exists(pacote) ? EstadoDisco.Ler(pacote) : null);
                GravarJsonChatGpt(recibo, EstadoDisco.Ler(pacote).Sha256);
            }

            string? pendenciaGerador = null;
            async Task<bool> RemontarAuditadoAsync()
            {
                var dados = Path.Combine(pasta, "dados.json");
                var conferencia = Path.Combine(pasta, "conferencia.json");
                var fechamento = LerRegistroParcial(conferencia);
                var revisao = fechamento?["revisao_integral"];
                if (fechamento?["auditoria"]?["id_execucao"]?.ToString() != idExecucao ||
                    fechamento["auditoria"]?["situacao"]?.ToString() != "concluida" ||
                    revisao?["id_execucao"]?.ToString() != idExecucao || revisao["situacao"]?.ToString() != "concluida" ||
                    !File.Exists(dados) || !string.Equals(EstadoDisco.Ler(dados).Sha256,
                        revisao["dados_sha256"]?.ToString(), StringComparison.OrdinalIgnoreCase)) return false;
                var temporario = Path.Combine(pasta, $"remontagem-{Guid.NewGuid():N}.rpmvni");
                try
                {
                    var gerador = Path.Combine(plugin, "scripts", "gerar-rpmvni.mjs");
                    var resultado = await ExecucaoProcesso.RodarAsync(pe.Pecas.Node!, [gerador, dados, conferencia, temporario],
                        pasta, AmbienteCodex, TimeSpan.FromMinutes(2), ct).ConfigureAwait(false);
                    if (resultado.Codigo != 0 || !File.Exists(temporario))
                    {
                        diario.Registrar("aviso", "O gerador local identificou uma pendência", resultado.Saida);
                        pendenciaGerador = resultado.Saida[..Math.Min(resultado.Saida.Length, 1600)];
                        return false;
                    }
                    File.Move(temporario, entrega, overwrite: true);
                    diario.Registrar("entrega", "Relatório remontado localmente com os dados da revisão final.");
                    return true;
                }
                finally { File.Delete(temporario); }
            }

            var comprovante = Path.Combine(registro.Pasta, "entrega-codex.sha256");
            string? IdentidadeEntrega()
            {
                var conferencia = Path.Combine(pasta, "conferencia.json");
                if (!File.Exists(entrega) || !File.Exists(conferencia) || new FileInfo(entrega).LinkTarget is not null) return null;
                return string.Join("\n", idExecucao, EstadoDisco.Ler(entrega).Sha256, EstadoDisco.Ler(conferencia).Sha256);
            }

            bool pronto = false;
            var mesmaEntrega = pe.Retomar && File.Exists(comprovante) &&
                IdentidadeEntrega() is { } identidade && File.ReadAllText(comprovante) == identidade;
            if (pe.Retomar && (mesmaEntrega || await RemontarAuditadoAsync()))
            {
                try { ValidarEntrega(); pronto = true; }
                catch (Exception erro) when (erro is InvalidDataException or PacoteInvalidoException)
                {
                    diario.Registrar("aviso", "Pendência na montagem local", erro.Message);
                    prompt += "\nCorrija somente a pendência de entrega: " + erro.Message;
                }
            }
            if (!pronto)
            {
                Informar("preparar_fontes", "Conferindo a conta ChatGPT. " + RotaCodex.Descrever(conexao));
                int tentativasConta = 0;
                while (!await ContaChatGptConfirmadaAsync(pe.Pecas, pasta, ct, AmbienteCodex, rota).ConfigureAwait(false))
                {
                    diario.Registrar("recuperacao", "Aguardando a conta ChatGPT", SemContaChatGpt);
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 2 << Math.Min(++tentativasConta, 8))), ct).ConfigureAwait(false);
                }
                var plano = File.ReadAllText(Path.Combine(plugin, "references", "grupos.json"));
                var planoSalvo = Path.Combine(pasta, "grupos.json");
                if (File.Exists(planoSalvo))
                {
                    var anterior = File.ReadAllText(planoSalvo);
                    if (!PlanoCompativel(anterior, plano)) return Falha("As seções do plano mudaram. Confira a seleção de entrada.");
                    var matrizAnterior = Path.Combine(pasta, "matriz-evidencias.json");
                    var copia = Path.Combine(pasta, "matriz-antes-perfis-1.0.4.json");
                    if (!JsonNode.DeepEquals(JsonNode.Parse(anterior), JsonNode.Parse(plano)) && File.Exists(matrizAnterior) && !File.Exists(copia))
                        GravarJsonChatGpt(copia, File.ReadAllText(matrizAnterior));
                }
                GravarJsonChatGpt(planoSalvo, plano);
                Iniciou(pe, inicio, codex, [codex, "exec"]);
                registro.Atualizar(o => { o["pasta_de_trabalho"] = pasta; o["rota_codex_tecnica"] = rota; });
                Informar("analisar_grupos", "O plugin está executando a análise e a auditoria no Codex.");
                await diario.MedirAsync(codex, pasta, AmbienteCodex, "inicial", ct, rota).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                using var acompanhamento = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var leitorSessoes = Path.Combine(plugin, "scripts", "sessoes.mjs");
                Task MedirSessoes(CancellationToken token) => diario.MedirSessoesAsync(pe.Pecas.Node!, leitorSessoes, pasta, AmbienteCodex, token);
                var observar = diario.AcompanharAsync(codex, pasta, AmbienteCodex, acompanhamento.Token, rota, MedirSessoes);
                int codigo;
                int semAvanco = 0;
                string? pendencia = null;
                string? modeloAlternativo = null;
                try
                {
                    int maiorProgresso = -1;
                    while (true)
                    {
                        ct.ThrowIfCancellationRequested();
                        File.Delete(resposta);
                        if (File.Exists(entrega))
                            File.Move(entrega, entrega + ".anterior", overwrite: true);
                        var pedidoAtual = prompt + (pendencia is null ? "" : "\nRetome exclusivamente os grupos pendentes desta execução e corrija a pendência de entrega: " + pendencia);
                        if (pendenciaGerador is not null) pedidoAtual += "\nCorrija a montagem: " + pendenciaGerador;
                        diario.Registrar("pedido", "Instruções enviadas ao Codex", pedidoAtual);
                        string[] argumentos = modeloAlternativo is null ? args : [.. args[..^1], "--model", modeloAlternativo, "-"];
                        diario.IniciarTentativa();
                        using var sessao = ExecucaoProcesso.Iniciar(codex, argumentos, pasta, pedidoAtual, AmbienteCodex,
                            linha => diario.Linha(linha, false), linha => diario.Linha(linha, true), TarefaCodex, limiteLinha: int.MaxValue,
                            aoInterromperLeitura: () => diario.Registrar("aviso", "Registro parcial de saída do Codex",
                                "A leitura de um canal não terminou ou houve uma falha ao registrar sua saída."));
                        using var cancelamento = ct.Register(sessao.Cancelar);
                        codigo = await sessao.Fim.ConfigureAwait(false);
                        diario.Registrar("codex", $"Codex encerrou com código {codigo}.");
                        ct.ThrowIfCancellationRequested();
                        if (diario.ModeloPrincipalIndisponivel) modeloAlternativo = "gpt-6.1-sol";
                        if (EntradaChatGpt(pe, caminhos, fontes, ct) != entrada)
                            return Falha("Uma peça ou o último despacho mudou durante a análise.");
                        if (!File.Exists(entrega)) await RemontarAuditadoAsync();
                        if (File.Exists(entrega))
                        {
                            if (IdentidadeEntrega() is { } assinatura) GravarJsonChatGpt(comprovante, assinatura);
                            try { ValidarEntrega(); pronto = true; break; }
                            catch (Exception erro) when (erro is InvalidDataException or PacoteInvalidoException)
                            {
                                if (await RemontarAuditadoAsync())
                                {
                                    try { ValidarEntrega(); pronto = true; break; }
                                    catch (Exception segunda) when (segunda is InvalidDataException or PacoteInvalidoException)
                                    { erro = segunda; }
                                }
                                pendencia = erro.Message;
                                diario.Registrar("aviso", "Corrigindo pendência da entrega", erro.Message);
                            }
                        }
                        int progressoAtual = 0;
                        var matriz = Path.Combine(pasta, "matriz-evidencias.json");
                        if (LerRegistroParcial(matriz)?["grupos"] is JsonArray grupos)
                            progressoAtual = grupos.OfType<JsonObject>().Count(g => g["perfil_efetivo"] is JsonObject);
                        semAvanco = progressoAtual > maiorProgresso ? 0 : Math.Min(8, semAvanco + 1);
                        maiorProgresso = Math.Max(maiorProgresso, progressoAtual);
                        pendencia ??= "Prosseguir pela matriz de evidências e pelos resultados já registrados.";
                        if (File.Exists(resposta) && new FileInfo(resposta).Length > 0)
                        {
                            var mensagem = File.ReadAllText(resposta).Trim();
                            diario.Registrar("diagnostico", "Retorno da execução parcial", mensagem);
                            if (!File.Exists(entrega)) pendencia = mensagem[..Math.Min(mensagem.Length, 1600)];
                        }
                        diario.Registrar("recuperacao", "Retomada automática da execução", $"Saída {codigo}; {progressoAtual} grupos confirmados.");
                        if (semAvanco > 0)
                            await Task.Delay(TimeSpan.FromSeconds(Math.Min(300, 2 << semAvanco)), ct).ConfigureAwait(false);
                    }
                }
                finally
                {
                    acompanhamento.Cancel();
                    await observar.ConfigureAwait(false);
                    diario.Arquivos(pasta);
                    await MedirSessoes(CancellationToken.None).ConfigureAwait(false);
                    await diario.MedirAsync(codex, pasta, AmbienteCodex, "final", CancellationToken.None, rota).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();

            }
        }

        ct.ThrowIfCancellationRequested();
        Informar("salvar_relatorio", "Conferindo o pacote e salvando na etapa de despacho.");
        var destino = PublicarPacoteChatGpt(pe.Processo, pacote, DateOnly.FromDateTime(DateTime.Now));
        registro.Atualizar(o => o["pmvrel"] = destino);
        return new("concluida", destino, null, null, 0);
    }

    internal const string SemContaChatGpt = "Entre no Codex com sua conta ChatGPT e tente novamente. Esta análise usa a cota da conta ChatGPT.";

    internal static async Task<bool> ContaChatGptConfirmadaAsync(Pecas pecas, string pasta, CancellationToken ct,
        Action<IDictionary<string, string?>>? ambiente = null, string rota = "openai")
    {
        if (pecas.Codex is null) return false;
        var acesso = await ExecucaoProcesso.RodarAsync(pecas.Codex,
            [.. RotaCodex.ArgumentosConta(rota), "login", "status"],
            pasta, ambiente ?? (env => LimparAmbienteChatGpt(env, pecas.CodexHome)),
            TimeSpan.FromSeconds(20), ct).ConfigureAwait(false);
        return acesso.Codigo == 0 && acesso.Saida.Split('\n')
            .Any(l => l.Trim().Equals("Logged in using ChatGPT", StringComparison.OrdinalIgnoreCase));
    }

    static void LimparAmbienteChatGpt(IDictionary<string, string?> env, string? home)
    {
        AmbienteFilho.Aplicar(env, Provedor.ChatGpt, ModoAcesso.Conta, null);
        foreach (var chave in env.Keys.Where(k =>
            k.StartsWith("OPENAI_", StringComparison.OrdinalIgnoreCase) ||
            k.StartsWith("CODEX_", StringComparison.OrdinalIgnoreCase) ||
            k.Equals("CHATGPT_BASE_URL", StringComparison.OrdinalIgnoreCase)).ToArray())
            env.Remove(chave);
        if (home is not null) env["CODEX_HOME"] = home;
    }

    internal static async Task<JsonObject> ResolverFontesChatGptAsync(Pecas pecas, string? pastaKit, CancellationToken ct,
        Action<IDictionary<string, string?>>? ambiente = null)
    {
        var script = Path.Combine(pecas.AnaliseChatGpt!, "scripts", "resolver-kit.mjs");
        IReadOnlyList<string> args = string.IsNullOrWhiteSpace(pastaKit) ? [script] : [script, "--kit", pastaKit];
        var resolucao = await ExecucaoProcesso.RodarAsync(pecas.Node!, args, Path.GetDirectoryName(script)!,
            env => { ambiente?.Invoke(env); env.Remove("PMVNI_KIT_ROOT"); }, TimeSpan.FromSeconds(35), ct).ConfigureAwait(false);
        if (resolucao.Codigo is not (0 or 1) || string.IsNullOrWhiteSpace(resolucao.Saida))
            throw new InvalidDataException("O kit PMVNI não pôde ser consultado. Confira a pasta do kit em Configurações.");
        return JsonNode.Parse(resolucao.Saida) as JsonObject
            ?? throw new InvalidDataException("O resolvedor devolveu uma resposta inválida.");
    }

    internal static bool FeicaoDaConsultaConfere(JsonNode consulta, JsonNode? territorial)
    {
        if (territorial?["resultado"]?.ToString() is not ("CANDIDATO" or "CONFIRMADO")) return true;
        return consulta["situacao"]?.ToString() == "CONSULTADA" &&
            consulta["candidatos"] is JsonArray candidatos &&
            territorial["feicao"] is JsonValue feicao && feicao.TryGetValue<int>(out var numero) &&
            candidatos.OfType<JsonObject>().Any(c => c["feicao"] is JsonValue indice && indice.TryGetValue<int>(out var id) && id == numero);
    }

    static void GravarJsonChatGpt(string arquivo, string json)
    {
        var temporario = arquivo + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var disco = new FileStream(temporario, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var texto = new StreamWriter(disco))
            texto.Write(json);
        try
        {
            if (File.Exists(arquivo)) File.Replace(temporario, arquivo, null);
            else File.Move(temporario, arquivo);
        }
        finally { File.Delete(temporario); }
    }

    static bool MatrizConfirmaGrupo(JsonArray grupos, GrupoChatGpt esperado) => grupos.OfType<JsonObject>().Count(grupo =>
            grupo["id"] is JsonValue id && id.TryGetValue<int>(out var numero) && numero == esperado.Id &&
            grupo["modelo"]?.ToString() == esperado.Modelo && (grupo["esforco"]?.ToString() == esperado.Esforco || esperado.Modelo == "gpt-6.2-sol" && esperado.Esforco == "low" && grupo["esforco"]?.ToString() == "medium") &&
            grupo["auditoria"]?.ToString() == "conferida" && grupo["secoes"] is JsonArray secoes &&
            secoes.Count == esperado.Secoes.Length &&
            esperado.Secoes.All(codigo => secoes.OfType<JsonObject>().Count(secao =>
                secao["numero"] is JsonValue n && n.TryGetValue<int>(out var valor) && valor == codigo &&
                secao["estado"]?.ToString() is "ANALISADO" or "NÃO APLICÁVEL" or "NÃO VERIFICÁVEL" &&
                !string.IsNullOrWhiteSpace(secao["evidencia"]?.ToString())) == 1)) == 1;

    static string RaizDosAnexos(PedidoExecucao pe)
    {
        var local = Path.Combine(pe.Registro.Pasta, "pecas-anexadas");
        return pe.UsarAnaliseChatGpt && Directory.Exists(local) ? local : pe.Processo.Pasta;
    }

    internal static string EntradaChatGpt(PedidoExecucao pe, IReadOnlyList<string> caminhos, string fontes, CancellationToken ct)
    {
        var raizAnexos = RaizDosAnexos(pe);
        var anexos = pe.Anexos.GroupBy(a => Path.GetFullPath(Path.Combine(raizAnexos, a.Relativo)), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var relativos = caminhos.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (anexos.Keys.FirstOrDefault(a => !relativos.Contains(a)) is { } ausente)
            throw new InvalidDataException("O arquivo anexado está indisponível: " + ausente);
        var pecas = caminhos.OrderBy(caminho => caminho, StringComparer.OrdinalIgnoreCase).Select(caminho =>
        {
            ct.ThrowIfCancellationRequested();
            anexos.TryGetValue(Path.GetFullPath(caminho), out var anexo);
            var relativo = anexo?.Relativo ?? Path.GetRelativePath(pe.Processo.Pasta, caminho);
            var disco = EstadoDisco.Ler(caminho);
            if (anexo is not null && !string.Equals(anexo.Sha256, disco.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("O anexo mudou desde o arquivamento: " + relativo);
            return new { caminho, relativo, categoria = anexo?.Categoria ?? Processos.CategoriaDoArquivo(relativo),
                anexado_nesta_analise = anexo is not null, sha256 = disco.Sha256, tamanho = disco.Tamanho };
        }).ToArray();
        var ultimo = Processos.UltimoEmitido(pe.Processo.Pasta, ct);
        var entrada = new
        {
            versao = 1,
            processo = new
            {
                numero = pe.Processo.Numero, ano = pe.Processo.Ano,
                identificacao = pe.Processo.Protocolo,
                requerente = pe.Processo.Requerente, pasta = pe.Processo.Pasta, nome_pasta = pe.Processo.NomePasta,
            },
            pedido_para_ia = string.IsNullOrWhiteSpace(pe.Texto) ? null : pe.Texto,
            tratamento_dos_dados = PedidoAgente.TratamentoDoProcesso,
            pecas,
            ultimo_emitido = ultimo,
            ultimo_emitido_sha256 = ultimo is null ? null : EstadoDisco.Ler(ultimo).Sha256,
            fontes_kit = fontes,
            pasta_qgis_configurada = pe.PastaQgis,
            pasta_kit_configurada = pe.PastaKit,
            entrega_plugin = "entrega-plugin.rpmvni",
        };
        return JsonSerializer.Serialize(entrada, JsonChatGpt);
    }

    static (PacoteAberto Aberto, string? Capa) ValidarPacoteChatGpt(Processo processo, string origem)
    {
        var aberto = Pacote.Ler(origem);
        if (aberto.SomenteLeitura) throw new InvalidDataException("O relatório precisa estar em um formato editável nesta versão do Editor.");
        var capa = ((aberto.Conteudo.Projeto.Documento["capa"] as JsonObject)?["processo"] as JsonValue)?.GetValue<string>();
        var numeroNaCapa = NomesArquivo.DoProcessoDaCapa(capa);
        var numeroConhecido = processo.Numero is not null && processo.Ano is not null;
        var capaDivergente = numeroConhecido
            ? numeroNaCapa != (processo.Numero, processo.Ano)
            : processo.Protocolo is { } protocolo && !string.Equals(protocolo, capa?.Trim(), StringComparison.OrdinalIgnoreCase);
        if (capaDivergente)
            throw new InvalidDataException("A identificação na capa não corresponde ao processo escolhido no Editor.");

        var metadados = aberto.Conteudo.Manifesto;
        if (metadados.TemplateRevisao.ToString(CultureInfo.InvariantCulture) != ModeloOficial.Revisao ||
            !(string.Equals(metadados.Sha256Css, ModeloOficial.HashesDoRegistro["relatorio.css"], StringComparison.OrdinalIgnoreCase) ||
              string.Equals(metadados.Sha256Css, "b59b97a59892a33060bb71cdd4c0ae4f93063a01b8e09f004be49e522628a62a", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("O modelo do plugin é incompatível com esta versão do Editor.");
        return (aberto, capa);
    }

    internal static void ConferirFigurasBrilliant(ConteudoPacote pacote, JsonNode conferencia, string pasta)
    {
        var documento = pacote.Projeto.Documento;
        var registro = conferencia["grafica"];
        var diagrama = documento["diagrama"];
        foreach (var nome in new[] { "mapa", "planta", "volumetria" })
        {
            var painel = nome == "mapa" ? documento["localizacao"] : diagrama?[nome];
            if (nome == "mapa" && painel?["sem_mapa"] is not null) continue;
            var recorte = nome == "planta" ? "implantacao.png" : "volumetria.png";
            if (nome != "mapa" && painel is null && diagrama?["sem_calculo"] is not null &&
                !File.Exists(Path.Combine(pasta, "recortes", recorte))) continue;
            var vista = registro?[nome];
            var hash = vista?["sha256_png"]?.ToString();
            var imagem = painel?["imagem"]?.ToString();
            if (!(registro?["modelo"]?.ToString() == "gpt-6.2-sol" || registro?["modelo"]?.ToString() == "gpt-6.1-sol" &&
                registro["perfil_efetivo"]?["modelo"]?.ToString() == "gpt-6.1-sol" && registro["perfil_efetivo"]?["esforco"]?.ToString() == "low" &&
                !string.IsNullOrWhiteSpace(registro["perfil_efetivo"]?["sessao"]?.ToString()) && !string.IsNullOrWhiteSpace(registro["perfil_efetivo"]?["fonte"]?.ToString())) || registro?["esforco"]?.ToString() != "low" ||
                !Uri.TryCreate(vista?["canvas"]?.ToString(), UriKind.Absolute, out var canvas) ||
                canvas.Scheme != Uri.UriSchemeHttps || canvas.Host != "brilliant.design" || canvas.AbsolutePath != "/playground" ||
                !canvas.Query.StartsWith("?seed=", StringComparison.Ordinal) || canvas.Query.Length <= 6 ||
                hash?.Length != 64 || imagem != $"assets/{hash}.png" || !pacote.Assets.ContainsKey(imagem))
                throw new InvalidDataException($"Composição gráfica pendente: confira o canvas e o PNG de {nome} no Brilliant Design.");
        }
    }

    internal static bool ConsultasQgisEquivalentes(JsonNode pacote, JsonNode conferencia)
    {
        if (JsonNode.DeepEquals(pacote, conferencia)) return true;
        // A serialização do pacote pode abreviar a representação decimal do mesmo double.
        if (pacote["geometria"] is not JsonObject geometria ||
            geometria["feicao_area_m2"] is not JsonValue areaPacote ||
            conferencia["geometria"]?["feicao_area_m2"] is not JsonValue areaConferida ||
            !areaPacote.TryGetValue<double>(out var area) || !double.IsFinite(area) ||
            !areaConferida.TryGetValue<double>(out var conferida) || area != conferida)
            return false;
        var copia = pacote.DeepClone();
        copia["geometria"]!["feicao_area_m2"] = areaConferida.DeepClone();
        return JsonNode.DeepEquals(copia, conferencia);
    }

    internal static ConteudoPacote CompletarMapaDaEntrega(ConteudoPacote pacote, string pasta, string? pastaQgis, CancellationToken ct)
    {
        var loc = pacote.Projeto.Documento["localizacao"] as JsonObject;
        var conferencia = Path.Combine(pasta, "conferencia.json");
        var registroAuditado = File.Exists(conferencia) ? JsonNode.Parse(LerJsonChatGpt(conferencia)) : null;
        var consulta = registroAuditado?["consulta_qgis"];
        var embutida = pacote.Projeto.Documento["_conferencia_chatgpt"]?["consulta_qgis"];
        if (embutida is not null && consulta is not null && !ConsultasQgisEquivalentes(embutida, consulta))
            throw new InvalidDataException("A consulta QGIS da conferência diverge da incluída no pacote.");
        consulta ??= embutida;
        if (consulta is null && loc is null) return pacote;
        if (consulta is null) throw new InvalidDataException("A entrega não identifica a consulta QGIS conferida na auditoria.");
        var resultado = consulta["resultado"]?.ToString();
        if (resultado is "SEM_ACESSO" or "SEM_CANDIDATO")
        {
            if (loc?["sem_mapa"] is not JsonObject)
                throw new InvalidDataException("A localização sem feição precisa trazer a justificativa da consulta.");
            return pacote;
        }
        if (consulta["situacao"]?.ToString() != "CONSULTADA" || resultado is not ("CANDIDATO" or "CONFIRMADO") ||
            consulta["feicao"] is not JsonValue valor || !valor.TryGetValue<int>(out var indice) || indice < 0 ||
            loc is null || loc["situacao"]?.ToString() != (resultado == "CONFIRMADO" ? "CONFIRMADO" : "A_CONFIRMAR"))
            throw new InvalidDataException("A situação do mapa, a consulta e a feição da camada Lotes precisam corresponder.");
        var registro = Path.Combine(pasta, "consulta-qgis.json");
        var evidencia = File.Exists(registro) ? JsonNode.Parse(LerJsonChatGpt(registro)) : null;
        if (evidencia?["feicao"] is JsonValue registrada && registrada.ToString() != indice.ToString(CultureInfo.InvariantCulture))
            throw new InvalidDataException("A feição da consulta local difere da auditada.");
        var inscricaoConsultada = evidencia?["feicao_cartografica"]?["inscricao"]?.ToString();
        if (consulta["sha256_png"]?.ToString() is { } hashMapa)
        {
            var ficha = JsonNode.Parse(LerJsonChatGpt(Path.Combine(pasta, "mapa-nativo", "FICHA.JSON")));
            var imagem = loc["imagem"]?.ToString() ?? "";
            var hashFinal = registroAuditado?["grafica"]?["mapa"]?["sha256_png"]?.ToString() ?? hashMapa;
            if (ficha?["feicao"]?.ToString() != valor.ToString() || ficha["resultado"]?.ToString() != resultado ||
                ficha["crs"]?.ToString() != MapaLocalizacao.Crs || ficha["cotas"]?.GetValue<bool>() != true || ficha["completo"]?.GetValue<bool>() != true ||
                ficha["sha256_png"]?.ToString() != hashMapa || !pacote.Assets.TryGetValue(imagem, out var bytes) ||
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != hashFinal ||
                (!string.IsNullOrWhiteSpace(inscricaoConsultada) && Inscricoes.Normalizar(inscricaoConsultada) != Inscricoes.Normalizar(ficha["inscricao"]?.ToString() ?? "")))
                throw new InvalidDataException("A imagem ou a identificação da feição diverge do mapa auditado do grupo 2.");
            var fichaOriginal = ficha["legend"] is JsonArray { Count: > 0 } legenda && JsonNode.DeepEquals(loc["legenda"], legenda)
                && ficha["dados"] is JsonArray { Count: > 0 } linhas && loc["dados"] is JsonArray dadosLocais
                && linhas.All(linha => dadosLocais.Any(dado => JsonNode.DeepEquals(linha, dado)));
            if (!fichaOriginal)
            {
                var revisao = registroAuditado?["revisao_integral"];
                var hashDados = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(pacote.Recebido));
                var confirmado = JsonNode.Parse(pacote.Recebido)?["localizacao"];
                if (revisao?["situacao"]?.ToString() != "concluida" || revisao["dados_sha256"]?.ToString() != hashDados ||
                    revisao["mapa_sha256"]?.ToString() != hashFinal || loc["legenda"] is not JsonArray { Count: > 0 } ||
                    loc["dados"] is not JsonArray { Count: > 0 } || !JsonNode.DeepEquals(loc["legenda"], confirmado?["legenda"]) ||
                    !JsonNode.DeepEquals(loc["dados"], confirmado?["dados"]))
                    throw new InvalidDataException("A legenda ou a ficha foi alterada sem confirmação da revisão final para esta versão do relatório e do mapa.");
            }
            return pacote;
        }
        if (string.IsNullOrWhiteSpace(pastaQgis))
        {
            var fontes = Path.Combine(pasta, "fontes-kit.json");
            if (File.Exists(fontes)) pastaQgis = JsonNode.Parse(LerJsonChatGpt(fontes))?["fonte"]?["geoprocessamento"]?["pasta"]?.ToString();
        }
        if (string.IsNullOrWhiteSpace(pastaQgis)) throw new InvalidDataException("A base do mapa QGIS não está identificada nesta execução.");
        var mapa = MapaParaAnalise.Gerar(pastaQgis, indice, resultado == "CONFIRMADO", ct);
        if (inscricaoConsultada is { Length: > 0 } inscricao &&
            Inscricoes.Normalizar(inscricao) != Inscricoes.Normalizar(mapa.Inscricao))
            throw new InvalidDataException("A inscrição da feição da base atual diverge da consulta. Confira a base usada.");
        var png = MapaParaAnalise.ImagemPng(mapa);
        var chave = "assets/" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png)) + ".png";
        var assets = new Dictionary<string, byte[]>(pacote.Assets) { [chave] = png };
        loc["imagem"] = chave;
        loc["legenda"] = JsonSerializer.SerializeToNode(mapa.Legenda, JsonChatGpt);
        var dados = JsonSerializer.SerializeToNode(mapa.Dados, JsonChatGpt)!.AsArray();
        // Preserva dados das peças, inclusive quando há uma divergência com a ficha da base.
        foreach (var linha in (loc["dados"] as JsonArray ?? []).OfType<JsonObject>())
            if (!dados.Any(d => d?["rotulo"]?.ToString() == linha["rotulo"]?.ToString() && d?["valor"]?.ToString() == linha["valor"]?.ToString()))
                dados.Add(linha.DeepClone());
        loc["dados"] = dados;
        var fontesMapa = $"Base municipal: {string.Join(", ", mapa.Camadas)}. {MapaLocalizacao.Crs}.";
        var fonteAnterior = loc["fonte"]?.ToString() ?? "";
        if (!fonteAnterior.Contains(fontesMapa, StringComparison.Ordinal))
            loc["fonte"] = (fonteAnterior + " " + fontesMapa).Trim();
        return pacote with { Assets = assets };
    }

    internal static void ConferirLeituraDoRelatorio(ConteudoPacote pacote, string pasta)
    {
        var extracao = Path.Combine(pasta, "conferir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extracao);
        try
        {
            pacote.ExtrairAssets(extracao);
            var dados = DadosEfetivos.Gerar(pacote.Projeto, apresentarContaNominal: true);
            var render = RelatorioRenderer.Renderizar(dados.Json, Path.Combine(extracao, "dados.json"),
                new(false, false, ImagensSoNaPasta: true, UsarProfissionalLocal: false), ModeloOficial.Carregar());
            if (!render.Ok) throw new InvalidDataException(string.Join(" ", render.Erros.Take(6)));
        }
        catch (MoldeErro ex)
        {
            throw new InvalidDataException("O relatório do plugin não renderizou em revisão 18. " + ex.Message, ex);
        }
        finally
        {
            try { Directory.Delete(extracao, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Registrar(ex); }
        }
    }

    internal static string PublicarPacoteChatGpt(Processo processo, string origem, DateOnly hoje)
    {
        var (aberto, capa) = ValidarPacoteChatGpt(processo, origem);
        var recibo = origem + ".publicacao.json";
        if (File.Exists(recibo) && JsonNode.Parse(File.ReadAllText(recibo)) is JsonObject anterior &&
            anterior["sha256"]?.ToString() == aberto.Disco.Sha256 && anterior["destino"]?.GetValue<string>() is { } publicado)
        {
            var etapa = Path.GetDirectoryName(publicado);
            if (etapa is not null && string.Equals(Path.GetDirectoryName(etapa), processo.Pasta, StringComparison.OrdinalIgnoreCase) &&
                Etapas.Existentes(processo.Pasta).Any(e => string.Equals(e.Pasta, etapa, StringComparison.OrdinalIgnoreCase)) &&
                new DirectoryInfo(etapa).LinkTarget is null && File.Exists(publicado) && new FileInfo(publicado).LinkTarget is null &&
                (anterior["concluida"]?.GetValue<bool>() == true || EstadoDisco.Ler(publicado).MesmoConteudo(aberto.Disco)))
                return publicado;
        }
        var destino = Processos.NomeImportacao(processo, capa, hoje);
        var (numero, ano) = processo.Numero is not null && processo.Ano is not null
            ? (processo.Numero, processo.Ano) : NomesArquivo.DoProcessoDaCapa(capa);
        var pasta = Path.GetDirectoryName(destino)!;
        if (new DirectoryInfo(pasta).LinkTarget is not null)
            throw new IOException("A pasta de despacho aponta para outro local. Confira a pasta do processo.");
        var temporario = Path.Combine(pasta, $".pmvni-{Guid.NewGuid():N}.tmp");
        try
        {
            File.Copy(origem, temporario);
            if (!EstadoDisco.Ler(temporario).MesmoConteudo(aberto.Disco))
                throw new IOException("O conteúdo do relatório mudou durante a transferência.");
            while (true)
            {
                void RegistrarPublicacao(bool concluida) => GravarJsonChatGpt(recibo, JsonSerializer.Serialize(new
                {
                    destino, sha256 = aberto.Disco.Sha256, concluida,
                }, JsonChatGpt));
                RegistrarPublicacao(false);
                try
                {
                    File.Move(temporario, destino);
                }
                catch (IOException) when (File.Exists(destino) || Directory.Exists(destino))
                {
                    destino = NomesArquivo.Livre(pasta, NomesArquivo.Importacao(numero, ano));
                    continue;
                }
                RegistrarPublicacao(true);
                return destino;
            }
        }
        finally
        {
            try { File.Delete(temporario); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Registrar(e); }
        }
    }

    static List<GrupoChatGpt[]> OrganizarRodadas(IEnumerable<GrupoChatGpt> grupos)
    {
        var porPerfil = grupos.Where(g => g.Id != 15).OrderBy(g => g.Id)
            .GroupBy(g => (g.Modelo, g.Esforco)).Select(g => g.ToArray())
            .OrderByDescending(rodada => rodada.Any(g => g.Id == 2)).ToList();
        porPerfil.Add([grupos.Single(g => g.Id == 15)]);
        return porPerfil;
    }

    static bool PlanoCompativel(string salvo, string atual)
    {
        var anterior = JsonNode.Parse(salvo) as JsonArray;
        var novo = JsonNode.Parse(atual) as JsonArray;
        if (anterior is null || novo is null) return false;
        if (JsonNode.DeepEquals(anterior, novo)) return true;
        foreach (var grupo in anterior.OfType<JsonObject>())
            if (grupo["modelo"]?.ToString() == "gpt-6.2-sol" &&
                (grupo["esforco"]?.ToString() == "medium" || grupo["id"]?.ToString() == "15" && grupo["esforco"]?.ToString() == "high") &&
                novo.OfType<JsonObject>().FirstOrDefault(g => g["id"]?.ToString() == grupo["id"]?.ToString()) is { } destino &&
                destino["esforco"]?.ToString() is "low" or "medium")
                grupo["esforco"] = destino["esforco"]?.DeepClone();
        return JsonNode.DeepEquals(anterior, novo);
    }

    static bool GruposChatGptValidos(GrupoChatGpt[] grupos) =>
        grupos.All(g => g is not null && g.Secoes is { Length: > 0 }) &&
        grupos.Select(g => g.Id).Order().SequenceEqual(Enumerable.Range(1, 15)) &&
        grupos.SelectMany(g => g.Secoes).Order().SequenceEqual(Enumerable.Range(1, 28)) &&
        grupos.All(g => !string.IsNullOrWhiteSpace(g.Modelo) && !string.IsNullOrWhiteSpace(g.Esforco));

    sealed record GrupoChatGpt(int Id, int[] Secoes, string Modelo, string Esforco, string? Nome);
}
