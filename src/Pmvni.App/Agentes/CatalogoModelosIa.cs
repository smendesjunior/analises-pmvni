using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Pmvni.Agentes;
using Pmvni.Composicao.Web;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>As listas de modelos de Ajustes › Inteligência artificial › Modelos de IA (2026-11-17), lidas como o seletor do WinStatusBar:
/// <list type="bullet">
/// <item>Codex: o catálogo que o próprio app ChatGPT mantém (<c>%USERPROFILE%\.codex\models_cache.json</c>, ou o <c>model_catalog_json</c> do
/// <c>config.toml</c>); "Atualizar lista" roda <c>codex debug models</c>, que busca de novo na conta. O modelo e o nível "do app" vêm do <c>config.toml</c>.</item>
/// <item>Claude Code: o pedido <c>initialize</c> do modo stream-json (<c>claude -p --input-format stream-json</c>), que devolve os modelos da conta e os
/// níveis de cada um sem chamar modelo nenhum.</item>
/// <item>APIs: <c>GET /v1/models</c> com a chave guardada pelo Windows (DPAPI, cartões de "Suas IAs"), só na memória do pedido.</item>
/// </list>
/// A última leitura fica em <c>&lt;dados&gt;\cache\modelos-ia.json</c> (só nomes e níveis); sem rede ou sem programa, vale a guardada e, sem ela,
/// a lista conhecida (<see cref="CatalogoIa.Reserva"/>).</summary>
internal sealed class CatalogoModelosIa
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static readonly HttpClient Balanceador = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(8) };

    readonly string raizDados;
    readonly Func<string> codexHome;
    readonly System.Collections.Concurrent.ConcurrentDictionary<FonteIa, ListaModelos> listas = new();
    readonly SemaphoreSlim atualizando = new(1, 1);
    bool carregado;

    internal CatalogoModelosIa(string raizDados, Func<string> codexHome)
    {
        this.raizDados = raizDados;
        this.codexHome = codexHome;
    }

    string ArquivoCache => Path.Combine(raizDados, "cache", "modelos-ia.json");

    internal bool Atualizando => atualizando.CurrentCount == 0;

    /// <summary>A lista de uma fonte, sem esperar nada: a guardada (ou, no Codex, o catálogo do app), senão a conhecida. No Codex, o modelo e o
    /// nível do app são relidos do config.toml a cada vez (arquivo pequeno).</summary>
    internal ListaModelos Lista(FonteIa f)
    {
        Carregar();
        listas.TryGetValue(f, out var guardada);
        var local = f == FonteIa.Codex ? LerCatalogoDoCodex() : null;
        var lista = local is not null && (guardada is null || local.LidaEm >= guardada.LidaEm)
            ? local : guardada ?? local ?? CatalogoIa.Reserva(f);
        if (f != FonteIa.Codex) return lista;
        var (modelo, nivel, _) = LerConfigCodex();
        return lista with { ModeloDoApp = modelo ?? lista.ModeloDoApp, NivelDoApp = nivel ?? lista.NivelDoApp };
    }

    /// <summary>Já houve leitura (do programa ou da API) desta fonte: senão, a página lê sozinha uma vez.</summary>
    internal bool JaLida(FonteIa f)
    {
        Carregar();
        return listas.ContainsKey(f);
    }

    void Carregar()
    {
        if (carregado) return;
        carregado = true;
        try
        {
            if (File.Exists(ArquivoCache))
                foreach (var (f, l) in CatalogoIa.LerCache(File.ReadAllText(ArquivoCache, Encoding.UTF8))) listas[f] = l;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    (string? Modelo, string? Nivel, string? Catalogo) LerConfigCodex()
    {
        try
        {
            var cfg = Path.Combine(codexHome(), "config.toml");
            return File.Exists(cfg) ? CatalogoIa.LerConfigCodex(File.ReadAllText(cfg, Encoding.UTF8)) : (null, null, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (null, null, null); }
    }

    /// <summary>O catálogo que o app ChatGPT/Codex guarda no computador (atualizado por ele mesmo).</summary>
    ListaModelos? LerCatalogoDoCodex()
    {
        try
        {
            var (_, _, proprio) = LerConfigCodex();
            foreach (var arquivo in new[] { Path.Combine(codexHome(), "models_cache.json"), proprio })
            {
                if (string.IsNullOrWhiteSpace(arquivo) || !File.Exists(arquivo)) continue;
                var modelos = CatalogoIa.LerCodex(File.ReadAllText(arquivo, Encoding.UTF8));
                if (modelos.Count > 0) return new ListaModelos(modelos, "app", File.GetLastWriteTimeUtc(arquivo));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return null;
    }

    /// <summary>"Atualizar lista": lê de novo cada fonte disponível (programa ou API) e guarda o que veio. Devolve, por fonte, o problema de quem
    /// não respondeu (a lista anterior continua valendo).</summary>
    internal async Task<Dictionary<FonteIa, string>> AtualizarAsync(Pecas pecas, IReadOnlyCollection<FonteIa> fontes, CancellationToken ct)
    {
        Carregar();
        var falhas = new Dictionary<FonteIa, string>();
        await atualizando.WaitAsync(ct);
        try
        {
            var tarefas = fontes.Select(async f =>
            {
                try
                {
                    var lida = await LerAgoraAsync(f, pecas, ct);
                    if (lida is { Modelos.Count: > 0 }) listas[f] = lida;
                    else lock (falhas) falhas[f] = "a lista veio vazia";
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { lock (falhas) falhas[f] = "não respondeu a tempo"; }
                catch (Exception e) when (e is InvalidOperationException or IOException or HttpRequestException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
                {
                    lock (falhas) falhas[f] = e.Message;
                }
            }).ToList();
            await Task.WhenAll(tarefas);
            Salvar();
        }
        finally { atualizando.Release(); }
        return falhas;
    }

    void Salvar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ArquivoCache)!);
            var tmp = ArquivoCache + ".tmp";
            File.WriteAllText(ArquivoGerado.Caminho(tmp), CatalogoIa.SerializarCache(listas), new UTF8Encoding(false));
            File.Move(tmp, ArquivoGerado.Caminho(ArquivoCache), overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal async Task<OfertaDeModelos> ConsultarParaDecisaoAsync(ExecucaoEmCurso execucao, EscolhaModelosDaExecucao escolha, CancellationToken ct)
    {
        if (escolha.Oferta is { DaConta: true, Modelos.Count: > 0 } consultada) return consultada;
        var (modo, rota) = execucao.Registro.AcessoDaExecucao();
        var pecas = execucao.Pecas;
        try
        {
            if (escolha.Rota == "gemini" && MatrizGemini.DoCodigo(escolha.Perfil) is { } perfil)
            {
                var usaChave = MatrizGemini.Modo(perfil) == ModoAcesso.Chave;
                var chave = usaChave ? CofreChaves.Ler(raizDados, Provedor.Gemini) : null;
                if (usaChave && string.IsNullOrWhiteSpace(chave)) throw new InvalidOperationException("Chave do Gemini indisponível.");
                var (json, _) = await ModelosDoGemini.LerAsync(pecas, perfil, chave, raizDados, ct);
                if (json is not null && JsonNode.Parse(json) is JsonObject g && g["modelos"] is JsonArray lista)
                {
                    var ids = EscolhaModelosDaExecucao.Ids(lista.OfType<JsonObject>().Select(o => o["modelo"]));
                    var daConta = g["lista_do_provedor"] is JsonValue v && v.TryGetValue<bool>(out var sim) && sim;
                    if (ids.Count > 0) return new(ids, daConta ? MatrizGemini.Nome(perfil) : "tabela do kit para " + MatrizGemini.Nome(perfil), daConta);
                }
            }
            else if (escolha.Rota == "codex" && rota == "codex-lb")
            {
                if (TravaDeProgramas.Permitidos is null && EnderecoCodexLb(pecas.CodexHome) is { } url)
                {
                    var json = await Balanceador.GetStringAsync(url, ct);
                    if (JsonNode.Parse(json) is JsonObject lb && lb["data"] is JsonArray modelos)
                    {
                        var ids = EscolhaModelosDaExecucao.Ids(modelos.OfType<JsonObject>().Select(o => o["id"]));
                        if (ids.Count > 0) return new(ids, "catálogo atual do Codex-LB, compartilhado entre as contas", false);
                    }
                }
            }
            else if (modo is "chave" or "conta" && escolha.Rota is "codex" or "claude")
            {
                var fonte = escolha.Rota == "claude" ? (modo == "chave" ? FonteIa.AnthropicApi : FonteIa.ClaudeCode)
                    : (modo == "chave" ? FonteIa.OpenAiApi : FonteIa.Codex);
                if (fonte == FonteIa.Codex && pecas.Codex is not null && rota == "openai")
                {
                    var json = await RodarAsync(pecas.Codex, ["debug", "models", "-c", "model_provider=openai"], null, TimeSpan.FromSeconds(30), ct);
                    var ids = CatalogoIa.LerCodex(json).Select(m => m.Id).ToList();
                    if (ids.Count > 0) return new(ids, "catálogo do Codex para a OpenAI direta", false);
                }
                else if (fonte != FonteIa.Codex && await LerAgoraAsync(fonte, pecas, ct) is { Modelos.Count: > 0 } lida)
                    return new(lida.Modelos.Select(m => m.Id).ToList(), FontesIa.Nome(fonte), true);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
        catch (Exception e) when (e is IOException or HttpRequestException or InvalidOperationException or
            System.Text.Json.JsonException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or TimeoutException or System.Security.Cryptography.CryptographicException) { }
        return escolha.Oferta is { Modelos.Count: > 0 } sugestoes ? sugestoes
            : new([], "A rota não forneceu um catálogo atual para esta conta.", false);
    }

    static Uri? EnderecoCodexLb(string? pasta)
    {
        var arquivo = pasta is null ? null : Path.Combine(pasta, "config.toml");
        if (!File.Exists(arquivo)) return null;
        var secao = false;
        foreach (var bruta in File.ReadLines(arquivo!))
        {
            var linha = bruta.Split('#', 2)[0].Trim();
            if (linha.StartsWith('[')) { secao = linha == "[model_providers.codex-lb]"; continue; }
            var partes = linha.Split('=', 2, StringSplitOptions.TrimEntries);
            if (!secao || partes.Length != 2 || partes[0] != "base_url") continue;
            if (Uri.TryCreate(partes[1].Trim('"', '\''), UriKind.Absolute, out var baseUrl) && baseUrl.IsLoopback
                && baseUrl.Scheme is "http" or "https" && baseUrl.UserInfo == "")
                return new Uri(baseUrl, "/v1/models");
        }
        return null;
    }

    async Task<ListaModelos?> LerAgoraAsync(FonteIa f, Pecas pecas, CancellationToken ct)
    {
        var agora = DateTimeOffset.UtcNow;
        switch (f)
        {
            case FonteIa.Codex:
            {
                if (pecas.Codex is null) throw new InvalidOperationException("Codex não encontrado");
                var saida = await RodarAsync(pecas.Codex, ["debug", "models"], null, TimeSpan.FromSeconds(40), ct);
                var modelos = CatalogoIa.LerCodex(saida);
                return modelos.Count > 0 ? new ListaModelos(modelos, "app", agora) : LerCatalogoDoCodex();
            }
            case FonteIa.ClaudeCode:
            {
                if (pecas.Claude is null) throw new InvalidOperationException("Claude Code não encontrado");
                return await LerClaudeAsync(pecas.Claude, agora, ct);
            }
            case FonteIa.OpenAiApi:
            {
                var chave = CofreChaves.Ler(PastaDados.Raiz, Provedor.ChatGpt) ?? throw new InvalidOperationException("nenhuma chave da OpenAI guardada");
                using var pedido = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
                pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", chave);
                var corpo = await PedirAsync(pedido, ct);
                return new ListaModelos(CatalogoIa.LerOpenAiApi(corpo, Lista(FonteIa.Codex).Modelos), "api", agora);
            }
            default:
            {
                var chave = CofreChaves.Ler(PastaDados.Raiz, Provedor.Claude) ?? throw new InvalidOperationException("nenhuma chave da Anthropic guardada");
                using var pedido = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models?limit=1000");
                pedido.Headers.Add("x-api-key", chave);
                pedido.Headers.Add("anthropic-version", "2023-06-01");
                var corpo = await PedirAsync(pedido, ct);
                return new ListaModelos(CatalogoIa.LerAnthropicApi(corpo), "api", agora);
            }
        }
    }

    static async Task<string> PedirAsync(HttpRequestMessage pedido, CancellationToken ct)
    {
        if (TravaDeProgramas.Permitidos is not null)
            throw new InvalidOperationException("A consulta à API está desabilitada durante a conferência.");
        using var resposta = await Http.SendAsync(pedido, ct);
        var corpo = await resposta.Content.ReadAsStringAsync(ct);
        if (!resposta.IsSuccessStatusCode)
            throw new InvalidOperationException((int)resposta.StatusCode == 401 ? "a chave foi recusada (HTTP 401)" : $"a API respondeu HTTP {(int)resposta.StatusCode}");
        return corpo;
    }

    /// <summary>Roda o programa sem janela (no job do editor, com as chaves herdadas fora do ambiente) e devolve a saída. Tempo esgotado ou
    /// cancelado: o processo e os filhos são encerrados.</summary>
    static async Task<string> RodarAsync(string exe, IReadOnlyList<string> argumentos, string? entrada, TimeSpan limite, CancellationToken ct,
        Func<string, bool>? basta = null)
    {
        var viaCmd = exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        var si = new ProcessStartInfo(viaCmd ? Path.Combine(Environment.SystemDirectory, "cmd.exe") : exe)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (viaCmd) { si.ArgumentList.Add("/d"); si.ArgumentList.Add("/c"); si.ArgumentList.Add(exe); }
        foreach (var a in argumentos) si.ArgumentList.Add(a);
        AmbienteFilho.Aplicar(si.Environment, Provedor.ChatGpt, ModoAcesso.Conta, null);
        using var tempo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        tempo.CancelAfter(limite);
        using var p = ProcessoInterno.Iniciar(si);
        try
        {
            _ = p.StandardError.ReadToEndAsync(tempo.Token);
            if (entrada is not null)
            {
                await p.StandardInput.WriteLineAsync(entrada.AsMemory(), tempo.Token);
                await p.StandardInput.FlushAsync(tempo.Token);
            }
            else p.StandardInput.Close();
            if (basta is null)
            {
                var tudo = p.StandardOutput.ReadToEndAsync(tempo.Token);
                await p.Processo.WaitForExitAsync(tempo.Token);
                return await tudo;
            }
            // Modo conversa (Claude Code): lê linha a linha até a resposta que interessa e encerra o programa.
            while (await p.StandardOutput.ReadLineAsync(tempo.Token) is { } linha)
                if (basta(linha)) return linha;
            return "";
        }
        finally
        {
            try { if (!p.Processo.HasExited) p.Processo.Kill(entireProcessTree: true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        }
    }

    static async Task<ListaModelos?> LerClaudeAsync(string exe, DateTimeOffset agora, CancellationToken ct)
    {
        // Sem sessão salva, sem MCPs e sem hooks: a leitura não vira conversa nem roda nada do usuário.
        string[] args = ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose",
            "--no-session-persistence", "--strict-mcp-config", "--settings", "{\"disableAllHooks\":true}"];
        const string pedido = "{\"type\":\"control_request\",\"request_id\":\"pmvni-modelos\",\"request\":{\"subtype\":\"initialize\"}}";
        var linha = await RodarAsync(exe, args, pedido, TimeSpan.FromSeconds(45), ct, l => l.Contains("control_response", StringComparison.Ordinal));
        return linha.Length == 0 ? null : CatalogoIa.LerClaudeInitialize(linha, agora);
    }
}
