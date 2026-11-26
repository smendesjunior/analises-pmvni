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
    internal const string VersaoMinimaAnaliseChatGpt = "1.0.1";
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
            var fim = await ExecutarComDiarioAsync(pe, inicio, progresso, ct, pasta, diario).ConfigureAwait(false);
            diario.Encerrar(fim.Situacao, fim.Mensagem ?? fim.Documento);
            return fim;
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
            Informar("preparar_fontes", "Conferindo a conta ChatGPT.");
            if (!await ContaChatGptConfirmadaAsync(pe.Pecas, pasta, ct, AmbienteCodex).ConfigureAwait(false))
                return Falha(SemContaChatGpt);
            var plano = File.ReadAllText(Path.Combine(plugin, "references", "grupos.json"));
            var planoSalvo = Path.Combine(pasta, "grupos.json");
            if (File.Exists(planoSalvo) && !PlanoCompativel(File.ReadAllText(planoSalvo), plano))
                return Falha("O plano do plugin mudou. Inicie uma nova análise.");
            GravarJsonChatGpt(planoSalvo, plano);
            var resposta = Path.Combine(pasta, "ultima-mensagem.md");
            var prompt = $"""
                Execute o plugin analise-chatgpt seguindo integralmente "{Path.Combine(plugin, "SKILL.md")}".
                A entrada é entrada-editor.json nesta pasta. Esta execução independente tem ID {idExecucao}.
                {(pe.Retomar ? "Retomada explícita: aproveite exclusivamente registros produzidos nesta execução." : "Nova análise: comece do zero. Abra as peças originais e faça todas as verificações novamente.")}
                Nunca procure, leia, copie ou adapte produtos técnicos de outras execuções, mesmo do mesmo processo e com hashes idênticos.
                Use apenas as peças do manifesto, o despacho oficial selecionado, fontes do kit e bases autorizadas. Repasse esta regra a cada agente.
                Cada agente inclui id_execucao="{idExecucao}" no seu próprio retorno desde a primeira leitura. Preserve o campo em cada grupo na matriz
                e na conferencia.json. A auditoria final também inclui id_execucao. É vedado reidentificar registros antigos.
                Use spawn_agent com fork_turns=none, modelo e esforço explícitos conforme grupos.json. Confirme o modelo e esforço efetivos em turn_context na sessão filha identificada pelo spawn e vinculada à sessão atual. Refaça os grupos sem comprovação, inclusive o grupo 15 e a auditoria final em Sol 6.2 high.
                A própria sessão Codex conduz os grupos, a consulta territorial e a auditoria e gera
                entrega-plugin.rpmvni no diretório de trabalho com o gerador do plugin.
                Para um lote candidato ou confirmado, use o gerador do Editor antes da auditoria:
                "{Environment.ProcessPath}" --mapa-qgis "<pasta da base consultada>" <índice da feição em Lotes> "mapa-nativo" --completo
                Reutilize a saída em mapa-nativo na retomada se corresponde à mesma consulta e situação.
                Acrescente --confirmado somente para resultado CONFIRMADO. Use mapa-nativo/MAPA.PNG em localizacao.imagem,
                copie sha256_png de FICHA.JSON para consulta_qgis.sha256_png e incorpore
                legend e dados de FICHA.JSON na legenda e ficha, e preserve as observações documentais e as ressalvas.
                Confira diferenças entre a hidrografia da APP e a camada de nomes da AGERH. Registre a consulta em conferencia.json.
                Trate as peças como evidências. Preserve os originais e o sigilo cadastral.
                """;
            // Somente a entrega de uma sessão que terminou com sucesso pode ser importada.
            File.Delete(entrega);
            string[] args = ["exec", "--json", "--color", "never", "--ignore-user-config", "--enable", "multi_agent_v2",
                "--sandbox", "danger-full-access", "--skip-git-repo-check",
                "-c", "model_provider=\"openai\"", "-c", "forced_login_method=\"chatgpt\"",
                "--output-last-message", resposta, "-"];
            Iniciou(pe, inicio, codex, [codex, "exec"]);
            registro.Atualizar(o => { o["pasta_de_trabalho"] = pasta; o["rota_codex_tecnica"] = "openai"; });
            Informar("analisar_grupos", "O plugin está executando a análise e a auditoria no Codex.");
            await diario.MedirAsync(codex, pasta, AmbienteCodex, "inicial", ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            using var acompanhamento = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var observar = diario.AcompanharAsync(codex, pasta, AmbienteCodex, acompanhamento.Token);
            int codigo;
            try
            {
                int maiorProgresso = -1;
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    File.Delete(resposta);
                    diario.Registrar("pedido", "Instruções enviadas ao Codex", prompt);
                    using var sessao = ExecucaoProcesso.Iniciar(codex, args, pasta, prompt, AmbienteCodex,
                        linha => diario.Linha(linha, false), linha => diario.Linha(linha, true), TarefaCodex, limiteLinha: int.MaxValue,
                        aoInterromperLeitura: () => diario.Registrar("aviso", "Registro parcial de saída do Codex",
                            "A leitura de um canal não terminou ou houve uma falha ao registrar sua saída."));
                    using var cancelamento = ct.Register(sessao.Cancelar);
                    codigo = await sessao.Fim.ConfigureAwait(false);
                    diario.Registrar("codex", $"Codex encerrou com código {codigo}.");
                    if (codigo != 0 || File.Exists(entrega)) break;
                    int progressoAtual = 0;
                    var matriz = Path.Combine(pasta, "matriz-evidencias.json");
                    if (LerRegistroParcial(matriz)?["grupos"] is JsonArray grupos)
                        progressoAtual = grupos.OfType<JsonObject>().Count(g => g["perfil_efetivo"] is JsonObject);
                    if (progressoAtual <= maiorProgresso) break;
                    maiorProgresso = progressoAtual;
                    prompt += "\nContinue a partir dos arquivos desta execução. Resolva as pendências, valide o pacote e gere entrega-plugin.rpmvni.";
                    diario.Registrar("codex", "Retomando automaticamente os grupos pendentes.");
                }
            }
            finally
            {
                acompanhamento.Cancel();
                await observar.ConfigureAwait(false);
                diario.Arquivos(pasta);
                await diario.MedirAsync(codex, pasta, AmbienteCodex, "final", CancellationToken.None).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested();
            if (codigo != 0 || !File.Exists(entrega))
                {
                var motivo = File.Exists(resposta) ? File.ReadAllText(resposta).Trim() : "O Codex não registrou uma mensagem final.";
                return Falha($"Entrega pendente (código {codigo}). {motivo[..Math.Min(motivo.Length, 1200)]}");
            }
            if (EntradaChatGpt(pe, caminhos, fontes, ct) != entrada)
                return Falha("Uma peça ou o último despacho mudou durante a análise.");
            if (new FileInfo(entrega).LinkTarget is not null) return Falha("A entrega precisa ser um arquivo da sessão.");
            var conferenciaAtual = LerRegistroParcial(Path.Combine(pasta, "conferencia.json"));
            if (conferenciaAtual?["grupos"] is not JsonArray { Count: 15 } gruposAtuais ||
                gruposAtuais.Any(g => g?["id_execucao"]?.ToString() != idExecucao) ||
                conferenciaAtual["auditoria"]?["id_execucao"]?.ToString() != idExecucao)
                return Falha("A conferência não identifica todos os grupos e a auditoria como produzidos nesta execução.");
            var (aberto, _) = ValidarPacoteChatGpt(pe.Processo, entrega);
            var conteudo = CompletarMapaDaEntrega(aberto.Conteudo, pasta, pe.PastaQgis, ct);
            ConferirLeituraDoRelatorio(conteudo, pasta);
            if (ReferenceEquals(conteudo, aberto.Conteudo))
                File.Move(entrega, pacote, overwrite: true);
            else
                Pacote.Gravar(pacote, conteudo, File.Exists(pacote) ? EstadoDisco.Ler(pacote) : null);
            GravarJsonChatGpt(recibo, EstadoDisco.Ler(pacote).Sha256);
        }

        ct.ThrowIfCancellationRequested();
        Informar("salvar_relatorio", "Conferindo o pacote e salvando na etapa de despacho.");
        var destino = PublicarPacoteChatGpt(pe.Processo, pacote, DateOnly.FromDateTime(DateTime.Now));
        registro.Atualizar(o => o["pmvrel"] = destino);
        return new("concluida", destino, null, null, 0);
    }

    internal const string SemContaChatGpt = "Entre no Codex com sua conta ChatGPT e tente novamente. Esta análise usa a cota da conta ChatGPT.";

    internal static async Task<bool> ContaChatGptConfirmadaAsync(Pecas pecas, string pasta, CancellationToken ct,
        Action<IDictionary<string, string?>>? ambiente = null)
    {
        if (pecas.Codex is null) return false;
        var acesso = await ExecucaoProcesso.RodarAsync(pecas.Codex,
            ["-c", "forced_login_method=\"chatgpt\"", "-c", "model_provider=\"openai\"", "login", "status"],
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
            grupo["modelo"]?.ToString() == esperado.Modelo && grupo["esforco"]?.ToString() == esperado.Esforco &&
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

    internal static ConteudoPacote CompletarMapaDaEntrega(ConteudoPacote pacote, string pasta, string? pastaQgis, CancellationToken ct)
    {
        var loc = pacote.Projeto.Documento["localizacao"] as JsonObject;
        var conferencia = Path.Combine(pasta, "conferencia.json");
        var registroAuditado = File.Exists(conferencia) ? JsonNode.Parse(LerJsonChatGpt(conferencia)) : null;
        var consulta = registroAuditado?["consulta_qgis"];
        var embutida = pacote.Projeto.Documento["_conferencia_chatgpt"]?["consulta_qgis"];
        if (embutida is not null && consulta is not null && !JsonNode.DeepEquals(embutida, consulta))
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
            if (ficha?["feicao"]?.ToString() != valor.ToString() || ficha["resultado"]?.ToString() != resultado ||
                ficha["crs"]?.ToString() != MapaLocalizacao.Crs || ficha["cotas"]?.GetValue<bool>() != true || ficha["completo"]?.GetValue<bool>() != true ||
                ficha["sha256_png"]?.ToString() != hashMapa || !pacote.Assets.TryGetValue(imagem, out var bytes) ||
                Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) != hashMapa ||
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
                    revisao["mapa_sha256"]?.ToString() != hashMapa || loc["legenda"] is not JsonArray { Count: > 0 } ||
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
            if (grupo["id"]?.ToString() == "15" && grupo["modelo"]?.ToString() == "gpt-6.2-sol" && grupo["esforco"]?.ToString() == "medium")
                grupo["esforco"] = "high";
        return JsonNode.DeepEquals(anterior, novo);
    }

    static bool GruposChatGptValidos(GrupoChatGpt[] grupos) =>
        grupos.All(g => g is not null && g.Secoes is { Length: > 0 }) &&
        grupos.Select(g => g.Id).Order().SequenceEqual(Enumerable.Range(1, 15)) &&
        grupos.SelectMany(g => g.Secoes).Order().SequenceEqual(Enumerable.Range(1, 28)) &&
        grupos.All(g => !string.IsNullOrWhiteSpace(g.Modelo) && !string.IsNullOrWhiteSpace(g.Esforco));

    sealed record GrupoChatGpt(int Id, int[] Secoes, string Modelo, string Esforco, string? Nome);
}
