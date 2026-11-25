using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

/// <summary>Registro do Editor na área permanente do processo em AppData. Cada execução tem identificador próprio; o controlador conserva o registro de trabalho da sessão em sua cópia local.</summary>
public sealed class RegistroExecucao : IDisposable
{
    string? ultimoJsonGravado;
    static readonly UTF8Encoding Utf8 = new(false);
    static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    readonly object trava = new();
    readonly JsonObject execucao = new();
    readonly Dictionary<string, List<string>> pendentes = new();
    readonly Dictionary<string, long> usados = new();      // bytes já no arquivo (e na fila) de cada saída
    readonly HashSet<string> truncados = new();
    readonly Dictionary<string, long> perdidas = new();    // linhas descartadas por arquivo enquanto ele não pôde ser gravado
    readonly SortedSet<string> naoGravados = new(StringComparer.Ordinal);
    long bytesPendentes;
    bool descartado;
    int falhas;
    long ultimaFalha;
    string? ultimoErro;

    bool PodeTentarDeNovo() => falhas == 0 || Environment.TickCount64 - ultimaFalha >= 1000;

    /// <summary>Quanto o registro guarda na memória enquanto um arquivo não pode ser gravado (32 MB); passou disso, as linhas novas são descartadas e
    /// contadas (uma linha "registro: N linha(s) não puderam ser gravadas…" entra no arquivo quando ele voltar a aceitar gravação).</summary>
    public long LimitePendenteBytes { get; set; } = 32L * 1024 * 1024;

    /// <summary>Quantas tentativas de gravar um arquivo de saída falharam (E/S ou acesso). O registro nunca lança para quem acompanha o agente.</summary>
    public int FalhasDeGravacao { get { lock (trava) return falhas; } }

    /// <summary>null se toda gravação deu certo; senão o aviso em português para o <c>execucao.json</c>.</summary>
    public string? Aviso
    {
        get
        {
            lock (trava)
            {
                if (falhas == 0) return null;
                var perdidasTotal = perdidas.Values.Sum();
                return "O registro não pôde gravar " + string.Join(", ", naoGravados) + " em algum momento (" + falhas + " tentativa(s) falharam"
                       + (perdidasTotal > 0 ? "; " + perdidasTotal + " linha(s) descartadas" : "") + "): " + ultimoErro;
            }
        }
    }

    /// <summary>Tamanho máximo de cada arquivo de saída (<c>saida.jsonl</c>, <c>erros.txt</c>, <c>situacoes.jsonl</c>): 50 MB. Passou disso, grava uma linha
    /// "saída truncada…" e descarta o resto (a leitura do agente continua, para ele nunca ficar bloqueado).</summary>
    public long LimiteArquivoBytes { get; set; } = 50L * 1024 * 1024;

    const long DescarregarAcima = 4L * 1024 * 1024;

    readonly bool reaberto;
    RegistroExecucao(string pasta, bool reaberto = false)
    {
        Pasta = pasta;
        this.reaberto = reaberto;
    }

    public string Pasta { get; }

    public const string NomeRecusaEntrega = "RECUSA-ENTREGA-EDITOR.JSON";

    public static bool? PastaDisponivel(string caminho)
    {
        try { return (File.GetAttributes(caminho) & FileAttributes.Directory) != 0; }
        catch (Exception e) when (e is DirectoryNotFoundException or FileNotFoundException)
        {
            try
            {
                return TravaExecucao.EmVolumeDeRede(caminho) || !new DriveInfo(Path.GetPathRoot(Path.GetFullPath(caminho))!).IsReady ? null : false;
            }
            catch (Exception x) when (x is IOException or UnauthorizedAccessException) { return null; }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    internal static string LerTextoDaExecucao(string pastaRegistro)
    {
        var caminho = Caminho(pastaRegistro, "execucao.json");
        for (var tentativa = 0; ; tentativa++)
        {
            try
            {
                using var arquivo = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var leitor = new StreamReader(arquivo, Utf8, detectEncodingFromByteOrderMarks: true);
                return leitor.ReadToEnd();
            }
            catch (Exception e) when (tentativa < 5 && e is (IOException or UnauthorizedAccessException)
                && (e.HResult & 0xffff) is 5 or 32 or 33 or 303)
            {
                // O Windows pode negar a abertura durante a substituição atômica. Um bloqueio persistente chega ao chamador.
                Thread.Sleep(10);
            }
        }
    }

    public static DateTimeOffset? LerInicio(string pastaRegistro)
    {
        try
        {
            var j = JsonNode.Parse(LerTextoDaExecucao(pastaRegistro));
            return DateTimeOffset.TryParse(j?["inicio"]?.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }

    static string RotaDaRecusa(Provedor p) => p switch { Provedor.ChatGpt => "codex", Provedor.Gemini => "gemini", _ => "claude" };

    public static string? MotivoDaRecusaAtual(string processo, Provedor provedor, string registro)
    {
        var marcador = Path.Combine(registro, NomeRecusaEntrega);
        if (!File.Exists(marcador) || LerInicio(registro) is not { } inicio) return null;
        try
        {
            var m = JsonNode.Parse(File.ReadAllBytes(marcador));
            if (m?["versao"]?.GetValue<int>() != 1 || m["execution"]?.GetValue<string>() != Path.GetFileName(registro)
                || m["rota"]?.GetValue<string>() != RotaDaRecusa(provedor) || m["erros"] is not JsonArray { Count: > 0 } erros) return null;
            var documento = RecuperacaoAnaliseLocal.EntregaPublicada(processo, Path.GetFileName(registro))?.Documento
                ?? ResultadoAnalise.Localizar(processo, provedor, inicio.UtcDateTime, registro).Documento;
            if (documento is null || !string.Equals(documento, m["documento"]?.GetValue<string>(), StringComparison.OrdinalIgnoreCase)) return null;
            using var arquivo = File.OpenRead(documento);
            var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(arquivo));
            return hash == m["sha256"]?.GetValue<string>() ? string.Join(Environment.NewLine, erros.Select(e => e?.GetValue<string>())) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) { return null; }
    }

    public void RecusarEntrega(Provedor provedor, string documento, string sha256, IReadOnlyList<string> erros)
    {
        var marcador = Path.Combine(Pasta, NomeRecusaEntrega);
        var dados = new { versao = 1, execution = Path.GetFileName(Pasta), rota = RotaDaRecusa(provedor), documento = Path.GetFullPath(documento), sha256, erros };
        var tmp = marcador + "." + Guid.NewGuid().ToString("N").ToUpperInvariant() + ".TMP";
        try
        {
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(dados, Opcoes));
            File.Move(tmp, marcador, true);
        }
        finally { if (File.Exists(tmp)) File.Delete(tmp); }
        Atualizar(o =>
        {
            o["situacao"] = "interrompida";
            o["mensagem"] = "Entrega recusada pelo Editor. " + string.Join(" ", erros);
        });
    }

    /// <summary>Modo e rota técnica realmente usados na partida, para consultar o catálogo ao receber uma recusa.</summary>
    public (string? Modo, string? RotaCodex) AcessoDaExecucao()
    {
        lock (trava) return (execucao["modo"]?.GetValue<string>(), execucao["rota_codex_tecnica"]?.GetValue<string>());
    }

    public string? RotaAlternativaDaAuditoria()
    {
        lock (trava) return execucao["rota_auditoria_alternativa"] is JsonValue valor &&
            valor.TryGetValue<string>(out var rota) && rota is "codex" or "claude" ? rota : null;
    }

    public string? PerfilDaExecucao()
    {
        lock (trava) return execucao["perfil"]?.GetValue<string>();
    }

    public string PedidoOriginal()
    {
        var pedido = File.ReadAllText(Caminho(Pasta, "pedido.md"), Utf8);
        if (!pedido.StartsWith(PedidoAgente.PrimeiraLinha + "\n", StringComparison.Ordinal)) return pedido;
        var separador = "\n" + PedidoAgente.CabecalhoDiretrizes + "\n";
        var inicio = pedido.IndexOf(separador, StringComparison.Ordinal);
        return inicio < 0 ? "" : pedido[(inicio + separador.Length)..];
    }

    // O kit valida as provas, as etapas e os critérios antes de restaurar o manifesto.
    // Selecionar aqui o mesmo registro conserva os objetos de evidência endereçados por hash.
    public static RegistroExecucao? BuscarInterrompida(Processo alvo, string? pedido, IReadOnlyList<ArquivoArquivado> anexos, Provedor? provedor = null) =>
        BuscarInterrompida(alvo, pedido, anexos, provedor, null);

    internal static RegistroExecucao? BuscarInterrompida(Processo alvo, string? pedido, IReadOnlyList<ArquivoArquivado> anexos,
        Provedor? provedor, string? documentos)
    {
        var processo = alvo.Pasta;
        foreach (var pasta in ControlesDoProcesso.Registros(processo).OrderByDescending(Path.GetFileName, StringComparer.Ordinal))
        {
            if (MotivoInvalido(processo, pasta) is not null) continue;
            using var anterior = Abrir(pasta, processo, importarLegado: false);
            var marcada = anterior.execucao.ContainsKey(AreaAnaliseLocal.Marcador);
            try
            {
                if (provedor.HasValue && anterior.execucao["provedor"]?.GetValue<string>() != Provedores.Sufixo(provedor.Value)) return null;
                var estado = anterior.execucao["situacao"]?.GetValue<string>();
                if (estado is not ("interrompida" or "cota_esgotada" or "falhou" or "em_andamento" or "cancelada")) return null;
                if (anterior.PedidoOriginal().Trim() != (pedido ?? "").Trim()) return null;
                var registrados = anterior.LerAnexosCategorizados();
                var atendidas = Processos.CategoriasAtendidas(anexos);
                if (registrados.Count > 0 || atendidas.Count > 0)
                {
                    if (anexos.Count != registrados.Count || anexos.Any(a => !registrados.Any(r =>
                        string.Equals(r.Relativo, a.Relativo, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(r.Sha256, a.Sha256, StringComparison.OrdinalIgnoreCase) && r.Categoria == a.Categoria))) return null;
                }
                var id = Path.GetFileName(pasta);
                var consulta = marcada ? RecuperacaoAnaliseLocal.Consultar(processo, pasta, documentos) : null;
                JsonNode? manifesto;
                if (consulta is null) manifesto = JsonNode.Parse(File.ReadAllText(Caminho(pasta, "continuidade.json")));
                else
                {
                    var salvo = consulta.LerJson(Path.Combine(".pmvni-ai", "editor", id, "continuidade.json"));
                    manifesto = salvo is { } m ? JsonNode.Parse(m.GetRawText()) : null;
                    if (manifesto?["case"]?.GetValue<string>() is not { } raizDeclarada ||
                        !string.Equals(Path.TrimEndingDirectorySeparator(raizDeclarada), Path.TrimEndingDirectorySeparator(consulta.RaizRegistrada), StringComparison.OrdinalIgnoreCase))
                        return null;
                }
                if (manifesto?["execution"]?.GetValue<string>() != Path.GetFileName(pasta)
                    || manifesto["stages"] is not JsonObject etapas || etapas.Count == 0
                    || manifesto["inputs"] is not JsonArray entradas || entradas.Count == 0) return null;
                var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var entrada in entradas)
                {
                    if (entrada is not JsonArray { Count: 2 } par || par[0] is null || par[1] is null) return null;
                    var caminho = RecuperacaoAnaliseLocal.OriginalConsultado(processo, consulta?.RaizRegistrada ?? processo, par[0]!.GetValue<string>());
                    if (caminho is null) return null;
                    var esperado = par[1]!.GetValue<string>();
                    using var arquivo = File.OpenRead(caminho);
                    if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(arquivo)).Equals(esperado, StringComparison.OrdinalIgnoreCase)) return null;
                    hashes[caminho] = esperado;
                }
                IReadOnlyList<ArquivoEncontrado> pecas;
                if (atendidas.Count > 0) pecas = Processos.PecasParaAnalise(alvo, atendidas, CancellationToken.None);
                else
                {
                    var detalhe = Processos.Detalhar(alvo, CancellationToken.None);
                    if (detalhe.Avisos.Count != 0) return null;
                    pecas = detalhe.Projeto.Concat(detalhe.Documentos).ToList();
                }
                var inicio = anterior.execucao["inicio"]?.GetValue<string>();
                DateTimeOffset.TryParse(inicio, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var partida);
                if (pecas.Any(a => !hashes.ContainsKey(a.Caminho)
                    && (a.AlteradoUtc >= partida.UtcDateTime || File.GetCreationTimeUtc(a.Caminho) >= partida.UtcDateTime))) return null;
                if (anexos.Any(a => !hashes.TryGetValue(Path.GetFullPath(Path.Combine(processo, a.Relativo)), out var hash)
                    || !hash.Equals(a.Sha256, StringComparison.OrdinalIgnoreCase))) return null;
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
            {
                if (marcada) throw new IOException("Não foi possível conferir a retomada local do mesmo pedido. A execução foi preservada.", e);
                return null;
            }
            return Abrir(pasta, processo);
        }
        return null;
    }

    /// <summary>Cria a pasta (e as de cima). Se o nome já existe (mesmo segundo), usa <c>_2</c>, <c>_3</c>…</summary>
    public static RegistroExecucao Criar(string pastaProcesso, Provedor p, DateTime agoraLocal)
    {
        var baseNome = agoraLocal.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + "-" + Provedores.Sufixo(p);
        var editor = ControlesDoProcesso.Diretorio(pastaProcesso, "editor");
        AreaAnaliseLocal.ConferirCaminho(editor, local: true);
        Directory.CreateDirectory(editor);
        for (var k = 1; ; k++)
        {
            var pasta = Path.Combine(editor, k == 1 ? baseNome : baseNome + "_" + k.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(pasta) || File.Exists(pasta) || Directory.Exists(Path.Combine(ControlesDoProcesso.Legado(pastaProcesso, "editor"), Path.GetFileName(pasta)))) continue;
            var reserva = Directory.CreateDirectory(Path.Combine(editor, ".criando-" + Guid.NewGuid().ToString("N"))).FullName;
            try
            {
                try { Directory.Move(reserva, pasta); }
                catch (IOException) when (Directory.Exists(pasta)) { continue; }
                return new RegistroExecucao(pasta);
            }
            finally { if (Directory.Exists(reserva)) Directory.Delete(reserva); }
        }
    }

    /// <summary>Reabre o registro de uma execução que já existe (o editor reaberto volta a acompanhar uma análise do Claude, desenho §12.7): o que o
    /// <c>execucao.json</c> já tem continua (as mudanças seguintes se somam a ele). <c>execucao.json</c> ausente ou com JSON inválido: começa vazio. Um erro de acesso impede a retomada.
    /// Os caminhos vêm dos ajustes locais (editáveis): o registro tem de pertencer ao processo na área do Editor ou no controle legado, sem atalho
    /// (junção ou link) no caminho; senão <see cref="InvalidOperationException"/> e nada é gravado.</summary>
    public static RegistroExecucao Abrir(string pasta, string pastaProcesso) => Abrir(pasta, pastaProcesso, importarLegado: true);

    internal static RegistroExecucao Abrir(string pasta, string pastaProcesso, bool importarLegado)
    {
        if (MotivoInvalido(pastaProcesso, pasta) is { } motivo) throw new InvalidOperationException(motivo);
        if (importarLegado) pasta = ControlesDoProcesso.Execucao(pastaProcesso, "editor", Path.GetFileName(Path.TrimEndingDirectorySeparator(pasta)), gravar: true);
        if (!Directory.Exists(pasta)) throw new DirectoryNotFoundException("A pasta do registro da execução não existe: " + pasta);
        var r = new RegistroExecucao(Path.GetFullPath(pasta), reaberto: true);
        try
        {
            if (JsonNode.Parse(LerTextoDaExecucao(pasta)) is JsonObject antes)
                foreach (var (chave, valor) in antes.ToList())
                {
                    antes.Remove(chave);
                    r.execucao[chave] = valor;
                }
        }
        catch (Exception e) when (e is FileNotFoundException or JsonException) { /* registro ausente ou formato legado ilegível */ }
        return r;
    }

    /// <summary>Valida a identidade da pasta do Editor no AppData ou no layout anterior, recusando atalhos e junções.</summary>
    public static string? MotivoInvalido(string pastaProcesso, string pastaRegistro)
    {
        var recusa = $"O registro da análise ({pastaRegistro}) não fica em uma pasta de registros reconhecida para este processo.";
        if (string.IsNullOrWhiteSpace(pastaProcesso) || string.IsNullOrWhiteSpace(pastaRegistro)) return recusa;
        try
        {
            var processo = Path.GetFullPath(pastaProcesso).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var registro = Path.GetFullPath(pastaRegistro).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var editor = Path.Combine(processo, ".pmvni-ai", "editor");
            if (string.Equals(Path.GetDirectoryName(registro), ControlesDoProcesso.Diretorio(processo, "editor"), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetDirectoryName(registro), Path.Combine(ControlesDoProcesso.RaizArquivada(processo), "editor"), StringComparison.OrdinalIgnoreCase))
            {
                AreaAnaliseLocal.ConferirCaminho(registro, local: true);
                return null;
            }
            if (!string.Equals(Path.GetDirectoryName(registro), editor, StringComparison.OrdinalIgnoreCase)) return recusa;
            var nome = Path.GetFileName(registro);
            if (nome.Length == 0 || nome is "." or "..") return recusa;
            foreach (var trecho in new[] { processo, Path.Combine(processo, ".pmvni-ai"), editor, registro })
            {
                var info = new DirectoryInfo(trecho);
                if (info.Exists && info.LinkTarget is not null) return recusa;
            }
            return null;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException) { return recusa; }
    }

    static string Caminho(string pasta, string nome) => Path.Combine(pasta, nome);

    /// <summary><c>pedido.md</c>: o pedido exato, UTF-8 sem BOM.</summary>
    public void GravarPedido(string texto)
    {
        var arquivo = Caminho(Pasta, "pedido.md");
        if (!reaberto || !File.Exists(arquivo)) File.WriteAllText(ArquivoGerado.Caminho(arquivo), texto, Utf8);
    }

    public IReadOnlyList<ArquivoArquivado> LerAnexosCategorizados()
    {
        var caminho = Caminho(Pasta, "anexos.json");
        if (!File.Exists(caminho)) return [];
        using var doc = JsonDocument.Parse(File.ReadAllBytes(caminho));
        var linhas = doc.RootElement.EnumerateArray().ToArray();
        if (!linhas.Any(l => l.TryGetProperty("categoria", out var c) && c.ValueKind == JsonValueKind.String)) return [];
        var lista = new List<ArquivoArquivado>();
        foreach (var linha in linhas)
        {
            var rel = linha.GetProperty("destino").GetString();
            var sha = linha.GetProperty("sha256").GetString();
            var categoria = linha.TryGetProperty("categoria", out var c) ? c.GetString() : null;
            if (string.IsNullOrWhiteSpace(rel) || Path.IsPathRooted(rel)
                || rel.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]).Any(p => p.Length == 0 || p.StartsWith('.') || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                || categoria is not (null or "projetos" or "bci" or "art_rrt" or "escritura_certidao" or "outros_documentos")
                || sha is not { Length: 64 } || !sha.All(char.IsAsciiHexDigit))
                throw new InvalidDataException("O registro de anexos da execução contém um destino, categoria ou assinatura inválida.");
            lista.Add(new ArquivoArquivado(Path.GetFileName(rel), rel, sha, linha.GetProperty("tamanho").GetInt64(), true) { Categoria = categoria });
        }
        return lista;
    }

    /// <summary><c>anexos.json</c>: por anexo, o nome de origem, o destino relativo, o sha256, o tamanho e <c>COPIADO</c> ou <c>JA_ARQUIVADO</c>.</summary>
    public void GravarAnexos(IReadOnlyList<ArquivoArquivado> anexos)
    {
        if (reaberto && File.Exists(Caminho(Pasta, "anexos.json"))) return;
        var lista = new JsonArray();
        foreach (var a in anexos)
        {
            var linha = new JsonObject
            {
                ["origem"] = Path.GetFileName(a.Origem),
                ["destino"] = a.Relativo,
                ["sha256"] = a.Sha256,
                ["tamanho"] = a.Tamanho,
                ["situacao"] = a.JaEstava ? "JA_ARQUIVADO" : "COPIADO",
            };
            if (a.Categoria != null) linha["categoria"] = a.Categoria;
            lista.Add(linha);
        }
        File.WriteAllText(ArquivoGerado.Caminho(Caminho(Pasta, "anexos.json")), lista.ToJsonString(Opcoes), Utf8);
    }

    /// <summary>Onde as análises iniciadas por um Editor anterior à 3.0.0 gravavam os eventos de progresso (a pedido do Editor). Desde a Tarefa 13 o Editor
    /// calcula o progresso pelo manifesto e pelo <c>engine_state.json</c> do kit e não pede mais nada à IA; esta pasta só é lida para não deixar vazio o
    /// Monitor de uma análise antiga ainda em andamento.</summary>
    public string PastaProgresso => Path.Combine(Pasta, "progresso");
    public const string ArquivoEstimativa = "estimativa.json";

    public TimeSpan TempoAtivoGravado
    {
        get
        {
            lock (trava)
                return execucao["tempo_ativo_ticks"] is JsonValue v && v.TryGetValue<long>(out var ticks) && ticks >= 0
                    ? TimeSpan.FromTicks(ticks) : TimeSpan.Zero;
        }
    }

    public void GravarTempoAtivo(TimeSpan tempo) =>
        Atualizar(dados => dados["tempo_ativo_ticks"] = Math.Max(TempoAtivoGravado.Ticks, tempo.Ticks));

    /// <summary>Muda o <c>execucao.json</c> e o grava de forma atômica (arquivo novo ao lado, depois troca): quem lê nunca vê JSON pela metade.</summary>
    public void Atualizar(Action<JsonObject> mudar)
    {
        lock (trava)
        {
            mudar(execucao);
            var destino = Caminho(Pasta, "execucao.json");
            var json = execucao.ToJsonString(Opcoes);
            if (json == ultimoJsonGravado && File.Exists(destino)) return;
            var tmp = destino + ".tmp";
            File.WriteAllText(ArquivoGerado.Caminho(tmp), json, Utf8);
            // Quem está lendo o execucao.json naquele instante (a tela, o OneDrive, um antivírus) pode segurar o destino por milissegundos.
            for (var tentativa = 1; ; tentativa++)
            {
                try
                {
                    File.Move(tmp, ArquivoGerado.Caminho(destino), overwrite: true);
                    ultimoJsonGravado = json;
                    return;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    if (tentativa >= 200) { try { File.Delete(tmp); } catch (IOException) { } throw; }
                    Thread.Sleep(5);
                }
            }
        }
    }

    public void AcrescentarSaida(string linha) => Acrescentar("saida.jsonl", linha);

    public void AcrescentarErro(string linha) => Acrescentar("erros.txt", linha);

    public void AcrescentarSituacao(string json) => Acrescentar("situacoes.jsonl", json);

    void Acrescentar(string arquivo, string linha)
    {
        lock (trava)
        {
            if (descartado || truncados.Contains(arquivo)) return;
            if (!pendentes.TryGetValue(arquivo, out var lista)) pendentes[arquivo] = lista = new List<string>();
            if (!usados.TryGetValue(arquivo, out var jaUsados))
            {
                try { jaUsados = File.Exists(Caminho(Pasta, arquivo)) ? new FileInfo(Caminho(Pasta, arquivo)).Length : 0; }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { jaUsados = 0; }
            }
            var bytes = Utf8.GetByteCount(linha) + 1;
            if (jaUsados + bytes > LimiteArquivoBytes)
            {
                var marca = "saída truncada: limite de " + (LimiteArquivoBytes >= 1024 * 1024 ? LimiteArquivoBytes / (1024 * 1024) + " MB" : LimiteArquivoBytes / 1024 + " KB")
                            + " do arquivo atingido";
                lista.Add(marca);
                truncados.Add(arquivo);
                usados[arquivo] = jaUsados;
                bytesPendentes += Utf8.GetByteCount(marca) + 1;
                return;
            }
            if (bytesPendentes + bytes > LimitePendenteBytes)
            {
                if (PodeTentarDeNovo()) DescarregarSemTrava();
                if (bytesPendentes + bytes > LimitePendenteBytes)
                {
                    // O disco não aceita gravação e a memória já guarda o teto: descarta e conta (a leitura do agente continua).
                    perdidas[arquivo] = perdidas.GetValueOrDefault(arquivo) + 1;
                    return;
                }
            }
            usados[arquivo] = jaUsados + bytes;
            lista.Add(linha);
            bytesPendentes += bytes;
            // Linhas grandes não se acumulam na memória; com o arquivo preso, uma tentativa por segundo no máximo.
            if (bytesPendentes > DescarregarAcima && PodeTentarDeNovo()) DescarregarSemTrava();
        }
    }

    /// <summary>Grava no disco o que foi acrescentado (abre, acrescenta e fecha: quem acompanha o registro ao vivo nunca encontra o arquivo preso).</summary>
    public void Descarregar()
    {
        lock (trava) DescarregarSemTrava();
    }

    /// <summary>Grava arquivo por arquivo; o que falha (OneDrive segurando, disco cheio) fica pendente para a próxima vez, sem lançar: o registro nunca
    /// para a leitura do agente nem muda o resultado da análise.</summary>
    void DescarregarSemTrava()
    {
        long restante = 0;
        foreach (var (arquivo, linhas) in pendentes)
        {
            var perdidasAqui = perdidas.GetValueOrDefault(arquivo);
            if (linhas.Count == 0 && perdidasAqui == 0) continue;
            try
            {
                // O lote vai de uma vez; se a gravação falhar no meio, o arquivo volta ao tamanho de antes, para o lote repetido depois não
                // duplicar as linhas que já tinham entrado.
                var texto = new StringBuilder();
                foreach (var l in linhas) texto.Append(l).Append('\n');
                if (perdidasAqui > 0)
                    texto.Append("registro: ").Append(perdidasAqui).Append(" linha(s) não puderam ser gravadas (arquivo preso ou disco cheio) e foram descartadas\n");
                var dados = Utf8.GetBytes(texto.ToString());
                using (var fs = new FileStream(ArquivoGerado.Caminho(Caminho(Pasta, arquivo)), FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                {
                    var inicio = fs.Position;
                    try { fs.Write(dados, 0, dados.Length); fs.Flush(); }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                    {
                        try { fs.SetLength(inicio); } catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
                        throw;
                    }
                }
                linhas.Clear();
                perdidas.Remove(arquivo);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                falhas++;
                ultimaFalha = Environment.TickCount64;
                if (naoGravados.Add(arquivo))
                {
                    try { MensagensDeErro.Registrar?.Invoke(e); }      // o detalhe técnico vai para o log do app, uma vez por arquivo
                    catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
                }
                ultimoErro = MensagensDeErro.Traduzir(e);
                foreach (var l in linhas) restante += Utf8.GetByteCount(l) + 1;
            }
        }
        bytesPendentes = restante;
    }

    /// <summary>Grava o que ficou pendente e não aceita mais nada.</summary>
    public void Dispose()
    {
        lock (trava)
        {
            if (descartado) return;
            try { DescarregarSemTrava(); }
            finally { descartado = true; }
        }
    }
}
