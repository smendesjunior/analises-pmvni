using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

/// <summary>O registro de uma execução (desenho §12.10): <c>&lt;processo&gt;\.pmvni-ai\editor\AAAA-MM-DD_HHMMSS-&lt;chatgpt|claude&gt;[_2…]</c>, criado no clique e nunca
/// reutilizado. Nada aqui entra nas pastas de controle do kit (<c>.pmvni-ai\analise</c>, <c>analise-pro</c>): elas são do agente. A chave de API nunca é gravada.</summary>
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

    /// <summary>Modo e rota técnica realmente usados na partida, para consultar o catálogo ao receber uma recusa.</summary>
    public (string? Modo, string? RotaCodex) AcessoDaExecucao()
    {
        lock (trava) return (execucao["modo"]?.GetValue<string>(), execucao["rota_codex_tecnica"]?.GetValue<string>());
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
    public static RegistroExecucao? BuscarInterrompida(Processo alvo, string? pedido, IReadOnlyList<ArquivoArquivado> anexos)
    {
        var processo = alvo.Pasta;
        var editor = Path.Combine(processo, ".pmvni-ai", "editor");
        if (!Directory.Exists(editor)) return null;
        foreach (var pasta in Directory.GetDirectories(editor).OrderDescending(StringComparer.Ordinal))
        {
            if (MotivoInvalido(processo, pasta) is not null) continue;
            try
            {
                using var anterior = Abrir(pasta, processo);
                var estado = anterior.execucao["situacao"]?.GetValue<string>();
                if (estado is not ("interrompida" or "cota_esgotada" or "falhou" or "em_andamento" or "cancelada")) return null;
                if (anterior.PedidoOriginal().Trim() != (pedido ?? "").Trim()) return null;
                var manifesto = JsonNode.Parse(File.ReadAllText(Caminho(pasta, "continuidade.json")));
                if (manifesto?["execution"]?.GetValue<string>() != Path.GetFileName(pasta)
                    || manifesto["stages"] is not JsonObject etapas || etapas.Count == 0
                    || manifesto["inputs"] is not JsonArray entradas || entradas.Count == 0) return null;
                var hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var raiz = Path.GetFullPath(processo).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (var entrada in entradas)
                {
                    if (entrada is not JsonArray { Count: 2 } par || par[0] is null || par[1] is null) return null;
                    var caminho = Path.GetFullPath(par[0]!.GetValue<string>());
                    if (!caminho.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) return null;
                    var esperado = par[1]!.GetValue<string>();
                    using var arquivo = File.OpenRead(caminho);
                    if (!Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(arquivo)).Equals(esperado, StringComparison.OrdinalIgnoreCase)) return null;
                    hashes[caminho] = esperado;
                }
                var detalhe = Processos.Detalhar(alvo, CancellationToken.None);
                if (detalhe.Avisos.Count != 0) return null;
                var inicio = anterior.execucao["inicio"]?.GetValue<string>();
                DateTimeOffset.TryParse(inicio, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var partida);
                if (detalhe.Projeto.Concat(detalhe.Documentos).Any(a => !hashes.ContainsKey(a.Caminho)
                    && (a.AlteradoUtc >= partida.UtcDateTime || File.GetCreationTimeUtc(a.Caminho) >= partida.UtcDateTime))) return null;
                if (anexos.Any(a => !hashes.TryGetValue(Path.GetFullPath(Path.Combine(processo, a.Relativo)), out var hash)
                    || !hash.Equals(a.Sha256, StringComparison.OrdinalIgnoreCase))) return null;
                return Abrir(pasta, processo);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>Cria a pasta (e as de cima). Se o nome já existe (mesmo segundo), usa <c>_2</c>, <c>_3</c>…</summary>
    public static RegistroExecucao Criar(string pastaProcesso, Provedor p, DateTime agoraLocal)
    {
        var baseNome = agoraLocal.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture) + "-" + Provedores.Sufixo(p);
        var editor = Path.Combine(pastaProcesso, ".pmvni-ai", "editor");
        Directory.CreateDirectory(editor);
        for (var k = 1; ; k++)
        {
            var pasta = Path.Combine(editor, k == 1 ? baseNome : baseNome + "_" + k.ToString(CultureInfo.InvariantCulture));
            if (Directory.Exists(pasta) || File.Exists(pasta)) continue;
            Directory.CreateDirectory(pasta);
            return new RegistroExecucao(pasta);
        }
    }

    /// <summary>Reabre o registro de uma execução que já existe (o editor reaberto volta a acompanhar uma análise do Claude, desenho §12.7): o que o
    /// <c>execucao.json</c> já tem continua (as mudanças seguintes se somam a ele). <c>execucao.json</c> ilegível ou ausente: começa vazio.
    /// Os caminhos vêm dos ajustes locais (editáveis): o registro tem de ser uma pasta filha direta de <c>&lt;processo&gt;\.pmvni-ai\editor</c>, sem atalho
    /// (junção ou link) no caminho; senão <see cref="InvalidOperationException"/> e nada é gravado.</summary>
    public static RegistroExecucao Abrir(string pasta, string pastaProcesso)
    {
        if (MotivoInvalido(pastaProcesso, pasta) is { } motivo) throw new InvalidOperationException(motivo);
        if (!Directory.Exists(pasta)) throw new DirectoryNotFoundException("A pasta do registro da execução não existe: " + pasta);
        var r = new RegistroExecucao(Path.GetFullPath(pasta), reaberto: true);
        try
        {
            if (JsonNode.Parse(File.ReadAllText(Caminho(pasta, "execucao.json"))) is JsonObject antes)
                foreach (var (chave, valor) in antes.ToList())
                {
                    antes.Remove(chave);
                    r.execucao[chave] = valor;
                }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { /* começa vazio */ }
        return r;
    }

    /// <summary>null se <paramref name="pastaRegistro"/> é uma pasta filha direta de <c>&lt;processo&gt;\.pmvni-ai\editor</c> (caminho completo, sem <c>..</c>
    /// escapando, sem junção nem link simbólico da pasta do processo até o registro); senão, o motivo em português.</summary>
    public static string? MotivoInvalido(string pastaProcesso, string pastaRegistro)
    {
        var recusa = $"O registro da análise ({pastaRegistro}) não fica em {Path.Combine(pastaProcesso ?? "", ".pmvni-ai", "editor")}; o editor não grava nele.";
        if (string.IsNullOrWhiteSpace(pastaProcesso) || string.IsNullOrWhiteSpace(pastaRegistro)) return recusa;
        try
        {
            var processo = Path.GetFullPath(pastaProcesso).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var registro = Path.GetFullPath(pastaRegistro).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var editor = Path.Combine(processo, ".pmvni-ai", "editor");
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

    /// <summary><c>anexos.json</c>: por anexo, o nome de origem, o destino relativo, o sha256, o tamanho e <c>COPIADO</c> ou <c>JA_ARQUIVADO</c>.</summary>
    public void GravarAnexos(IReadOnlyList<ArquivoArquivado> anexos)
    {
        if (reaberto && File.Exists(Caminho(Pasta, "anexos.json"))) return;
        var lista = new JsonArray();
        foreach (var a in anexos)
            lista.Add(new JsonObject
            {
                ["origem"] = Path.GetFileName(a.Origem),
                ["destino"] = a.Relativo,
                ["sha256"] = a.Sha256,
                ["tamanho"] = a.Tamanho,
                ["situacao"] = a.JaEstava ? "JA_ARQUIVADO" : "COPIADO",
            });
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
