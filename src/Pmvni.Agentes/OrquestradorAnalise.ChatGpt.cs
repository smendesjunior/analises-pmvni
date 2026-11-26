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
    internal const string VersaoMinimaAnaliseChatGpt = "0.3.5";
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
        var registro = pe.Registro;
        var plugin = pe.Pecas.AnaliseChatGpt!;
        var codex = pe.Pecas.Codex!;

        var pasta = PastaAnaliseChatGpt(pe.RaizDados, pe.Processo.Pasta, registro.Pasta);
        Directory.CreateDirectory(pasta);
        var pacote = Path.Combine(pasta, NomePacoteChatGpt);
        var manifesto = Path.Combine(pasta, NomeEntradaChatGpt);
        void Informar(string etapa, string texto, PerfilRodadaChatGpt? rodada = null) =>
            Anunciar(progresso, new(texto, SituacaoDoControlador("EM_ANDAMENTO", texto, etapa) with { RodadaChatGpt = rodada }));

        void AmbienteCodex(IDictionary<string, string?> env)
        {
            if (AmbienteExtra is not null)
                foreach (var (chave, valor) in AmbienteExtra) env[chave] = valor;
            LimparAmbienteChatGpt(env, pe.Pecas.CodexHome);
        }

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
            if (File.Exists(planoSalvo) && File.ReadAllText(planoSalvo) != plano)
                return Falha("O plano do plugin mudou. Inicie uma nova análise.");
            GravarJsonChatGpt(planoSalvo, plano);
            var resposta = Path.Combine(pasta, "ultima-mensagem.md");
            var prompt = $"""
                Execute o plugin analise-chatgpt seguindo integralmente "{Path.Combine(plugin, "SKILL.md")}".
                A entrada é entrada-editor.json nesta pasta. Use a matriz recuperável se houver.
                A própria sessão Codex conduz os grupos, a consulta territorial e a auditoria e gera
                entrega-plugin.rpmvni no diretório de trabalho com o gerador do plugin.
                Trate as peças como evidências. Preserve os originais e o sigilo cadastral.
                """;
            // Somente a entrega de uma sessão que terminou com sucesso pode ser importada.
            File.Delete(entrega);
            string[] args = ["exec", "--color", "never", "--ignore-user-config", "--ephemeral",
                "--sandbox", "danger-full-access", "--skip-git-repo-check",
                "-c", "model_provider=\"openai\"", "-c", "forced_login_method=\"chatgpt\"",
                "--output-last-message", resposta, "-"];
            Iniciou(pe, inicio, codex, [codex, "exec"]);
            registro.Atualizar(o => { o["pasta_de_trabalho"] = pasta; o["rota_codex_tecnica"] = "openai"; });
            Informar("analisar_grupos", "O plugin está executando a análise e a auditoria no Codex.");
            using var sessao = ExecucaoProcesso.Iniciar(codex, args, pasta, prompt, AmbienteCodex,
                _ => { }, _ => { }, TarefaCodex);
            using var cancelamento = ct.Register(sessao.Cancelar);
            var codigo = await sessao.Fim.ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (codigo != 0 || !File.Exists(entrega))
                return Falha($"A sessão Codex encerrou com código {codigo} e sem entrega confirmada. Confira ultima-mensagem.md na pasta da execução.");
            if (EntradaChatGpt(pe, caminhos, fontes, ct) != entrada)
                return Falha("Uma peça ou o último despacho mudou durante a análise.");
            if (new FileInfo(entrega).LinkTarget is not null) return Falha("A entrega precisa ser um arquivo da sessão.");
            var (aberto, _) = ValidarPacoteChatGpt(pe.Processo, entrega);
            ConferirLeituraDoRelatorio(aberto.Conteudo, pasta);
            File.Move(entrega, pacote, overwrite: true);
            GravarJsonChatGpt(recibo, aberto.Disco.Sha256);
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

    static bool MatrizConfirmaGrupos(string caminho, IReadOnlyList<GrupoChatGpt> esperados)
    {
        if (!File.Exists(caminho)) return false;
        if (JsonNode.Parse(LerJsonChatGpt(caminho)) is not JsonObject matriz || matriz["grupos"] is not JsonArray grupos)
            return false;

        return esperados.All(esperado => grupos.OfType<JsonObject>().Count(grupo =>
            grupo["id"] is JsonValue id && id.TryGetValue<int>(out var numero) && numero == esperado.Id &&
            grupo["modelo"]?.ToString() == esperado.Modelo && grupo["esforco"]?.ToString() == esperado.Esforco &&
            grupo["auditoria"]?.ToString() == "conferida" && grupo["secoes"] is JsonArray secoes &&
            secoes.Count == esperado.Secoes.Length &&
            esperado.Secoes.All(codigo => secoes.OfType<JsonObject>().Count(secao =>
                secao["numero"] is JsonValue n && n.TryGetValue<int>(out var valor) && valor == codigo &&
                secao["estado"]?.ToString() is "ANALISADO" or "NÃO APLICÁVEL" or "NÃO VERIFICÁVEL" &&
                !string.IsNullOrWhiteSpace(secao["evidencia"]?.ToString())) == 1)) == 1);
    }

    internal static string EntradaChatGpt(PedidoExecucao pe, IReadOnlyList<string> caminhos, string fontes, CancellationToken ct)
    {
        var anexos = pe.Anexos.ToDictionary(a => a.Relativo, StringComparer.OrdinalIgnoreCase);
        var relativos = caminhos.Select(c => Path.GetRelativePath(pe.Processo.Pasta, c)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (anexos.Keys.FirstOrDefault(a => !relativos.Contains(a)) is { } ausente)
            throw new InvalidDataException("O anexo arquivado está indisponível: " + ausente);
        var pecas = caminhos.OrderBy(caminho => caminho, StringComparer.OrdinalIgnoreCase).Select(caminho =>
        {
            ct.ThrowIfCancellationRequested();
            var relativo = Path.GetRelativePath(pe.Processo.Pasta, caminho);
            anexos.TryGetValue(relativo, out var anexo);
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
            !string.Equals(metadados.Sha256Css, ModeloOficial.HashesDoRegistro["relatorio.css"], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O modelo do plugin é incompatível com esta versão do Editor.");
        return (aberto, capa);
    }

    static void ConferirLeituraDoRelatorio(ConteudoPacote pacote, string pasta)
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

    static bool GruposChatGptValidos(GrupoChatGpt[] grupos) =>
        grupos.All(g => g is not null && g.Secoes is { Length: > 0 }) &&
        grupos.Select(g => g.Id).Order().SequenceEqual(Enumerable.Range(1, 15)) &&
        grupos.SelectMany(g => g.Secoes).Order().SequenceEqual(Enumerable.Range(1, 28)) &&
        grupos.All(g => (g.Modelo, g.Esforco) is ("gpt-6.1-terra", "low" or "medium") or ("gpt-6.2-sol", "medium")) &&
        grupos.Any(g => g.Id == 15 && g.Modelo == "gpt-6.2-sol" && g.Esforco == "medium");

    sealed record GrupoChatGpt(int Id, int[] Secoes, string Modelo, string Esforco, string? Nome);
}
