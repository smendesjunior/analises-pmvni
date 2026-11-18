using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Pmvni.Agentes;

/// <summary>O que a chave do Google AI Studio respondeu na lista de modelos (não gera texto nem gasta cota).</summary>
public sealed record ChaveGemini(bool Aceita, string Mensagem, IReadOnlyList<string> Modelos);

/// <summary>"Testar" do Gemini (Ajustes › Inteligência artificial): o Antigravity CLI (<c>agy --version</c>), o login da conta (<c>agy models</c>: lista
/// vazia = sem login), o plugin análise Google, o Python e, no modo chave, a conferência real da chave na API do Google (<c>GET /v1beta/models</c>, com a
/// chave só no cabeçalho). Nada pede texto ao modelo. A lista do modo conta e a da chave dizem quais modelos e níveis de cada etapa existem de fato.</summary>
public static class DiagnosticoGemini
{
    public const string EnderecoModelos = "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200";
    public const string VersaoMinimaAgy = "1.5.7";

    static readonly Regex Versao = new(@"(\d+\.\d+\.\d+)", RegexOptions.CultureInvariant);

    /// <summary>Só testes: quem responde à lista de modelos (o padrão é a rede de verdade).</summary>
    internal static HttpMessageHandler? Manipulador { get; set; }

    public static string? VersaoAgy(string saida) => Versao.Match(saida) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>Os identificadores de <c>agy models</c> (linhas "id&lt;tab&gt;nome"; o resto, como "Fetching available models...", é ignorado).</summary>
    public static IReadOnlyList<string> ModelosDoAgy(string saida)
    {
        var lista = new List<string>();
        foreach (var bruta in saida.Split('\n'))
        {
            var partes = bruta.Trim().Split('\t', 2, StringSplitOptions.TrimEntries);
            if (partes.Length == 2 && partes[0].Length > 0 && partes[1].Length > 0 && !partes[0].Contains(' ') && !lista.Contains(partes[0])) lista.Add(partes[0]);
        }
        return lista;
    }

    /// <summary>As etapas do perfil cujo identificador (<c>modelo-nível</c>) não está na lista do runtime: "Análise (gemini-4-carbon high)".</summary>
    public static IReadOnlyList<string> EtapasSemModelo(PerfilGemini perfil, IReadOnlyCollection<string> idsDoRuntime) =>
        MatrizGemini.Etapas(perfil).Where(e => !idsDoRuntime.Contains(e.IdRuntime, StringComparer.OrdinalIgnoreCase))
            .Select(e => $"{SituacaoAnalisePro.NomeEtapa(e.Etapa)} ({e.Modelo} {e.Esforco})").ToList();

    /// <summary>As etapas do perfil cujo modelo base não está na lista da API (a API lista o modelo sem o nível).</summary>
    public static IReadOnlyList<string> EtapasSemModeloNaApi(PerfilGemini perfil, IReadOnlyCollection<string> modelosDaApi) =>
        MatrizGemini.Etapas(perfil).Where(e => !modelosDaApi.Contains(e.Modelo, StringComparer.OrdinalIgnoreCase))
            .Select(e => $"{SituacaoAnalisePro.NomeEtapa(e.Etapa)} ({e.Modelo})").Distinct().ToList();

    /// <summary>Confere a chave na API do Google: aceita ou recusada, com a frase que diz o que fazer. A chave vai só no cabeçalho <c>x-goog-api-key</c>.</summary>
    public static async Task<ChaveGemini> ConferirChaveAsync(string chave, CancellationToken ct)
    {
        try
        {
            using var http = Manipulador is null ? new HttpClient() : new HttpClient(Manipulador, disposeHandler: false);
            http.Timeout = TimeSpan.FromSeconds(15);
            using var pedido = new HttpRequestMessage(HttpMethod.Get, EnderecoModelos);
            pedido.Headers.Add("x-goog-api-key", chave);
            using var resposta = await http.SendAsync(pedido, ct).ConfigureAwait(false);
            var corpo = await resposta.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (resposta.IsSuccessStatusCode) return new ChaveGemini(true, "chave aceita pelo Google", ModelosDaApi(corpo));
            return new ChaveGemini(false, MensagemDaRecusa(resposta.StatusCode, corpo), []);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            return new ChaveGemini(false, "não foi possível falar com a API do Google (sem rede ou tempo esgotado); a chave não foi conferida", []);
        }
    }

    /// <summary>A recusa da API em português, com o que fazer; o corpo da resposta nunca é mostrado inteiro.</summary>
    public static string MensagemDaRecusa(HttpStatusCode codigo, string corpo)
    {
        var n = (int)codigo;
        if (n is 400 or 401 or 403 && (corpo.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase) || corpo.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) || n == 401))
            return "chave recusada pelo Google (inválida ou apagada): gere outra no Google AI Studio e guarde-a de novo";
        if (n == 403) return "chave sem permissão para a Gemini API (projeto bloqueado ou API desativada): confira o projeto no Google AI Studio";
        if (n == 429) return "limite de uso da chave atingido agora (cota ou taxa): tente mais tarde ou confira o plano no Google AI Studio";
        return $"a API do Google respondeu {n}; a chave não foi conferida";
    }

    /// <summary>Os modelos de <c>/v1beta/models</c> que geram conteúdo, sem o prefixo <c>models/</c>.</summary>
    public static IReadOnlyList<string> ModelosDaApi(string json)
    {
        var lista = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("models", out var modelos) || modelos.ValueKind != JsonValueKind.Array) return lista;
            foreach (var m in modelos.EnumerateArray())
            {
                if (m.ValueKind != JsonValueKind.Object || !m.TryGetProperty("name", out var nome) || nome.ValueKind != JsonValueKind.String) continue;
                var gera = !m.TryGetProperty("supportedGenerationMethods", out var g) || g.ValueKind != JsonValueKind.Array
                           || g.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "generateContent");
                var id = nome.GetString()!.Replace("models/", "", StringComparison.Ordinal);
                if (gera && id.Length > 0 && !lista.Contains(id)) lista.Add(id);
            }
        }
        catch (JsonException) { }
        return lista;
    }

    /// <summary>O teste inteiro. <paramref name="chave"/> só é usada no modo chave (lida do cofre por quem chama) e nunca aparece na linha nem nos avisos.</summary>
    public static async Task<SituacaoProvedor> TestarAsync(Pecas pecas, ModoAcesso modo, PerfilGemini? perfilApi, string? chave, CancellationToken ct)
    {
        var avisos = new List<string>();
        var partes = new List<string>();
        var perfil = MatrizGemini.PerfilDoModo(modo, perfilApi);

        IReadOnlyList<string>? ids = null;
        if (modo == ModoAcesso.ContaGemini)
            partes.Add("Gemini Web: sessão e modelos precisam ser verificados pela ponte do editor");
        else if (pecas.Agy is null) partes.Add("Antigravity CLI (agy) não encontrado");
        else
        {
            var versao = await Rodar(LinhasDeComando.AgyVersao(pecas.Agy), Diagnostico.LimiteComando, ct).ConfigureAwait(false);
            var numero = versao.Codigo == 0 ? VersaoAgy(versao.Saida) : null;
            if (numero is null) partes.Add("Antigravity CLI não verificado: " + Diagnostico.PrimeiraLinha(versao.Saida));
            else
            {
                partes.Add("Antigravity CLI " + numero);
                if (Version.TryParse(numero, out var v) && v < Version.Parse(VersaoMinimaAgy))
                    avisos.Add($"O análise Google precisa do Antigravity CLI {VersaoMinimaAgy} ou mais novo; atualize com \"agy update\".");
                if (modo == ModoAcesso.Conta)
                {
                    // A lista da conta: vazia = sem login. Pode levar mais que os 15 s dos outros comandos (consulta o Google).
                    var modelos = await Rodar(LinhasDeComando.AgyModelos(pecas.Agy), TimeSpan.FromSeconds(60), ct).ConfigureAwait(false);
                    if (modelos.Codigo == -1) partes.Add("login não verificado: o agy não respondeu");
                    else
                    {
                        ids = ModelosDoAgy(modelos.Saida);
                        partes.Add(ids.Count > 0 ? "conectado com a conta Google" : "não conectado (abra um terminal, rode \"agy\" e entre na conta Google)");
                    }
                }
            }
        }

        if (pecas.GoogleRuntime is null) partes.Add("análise Google não instalado no Codex");
        else
        {
            partes.Add($"análise Google {pecas.VersaoAnaliseGoogle} (kit: {pecas.VersaoAnaliseGoogleKit ?? "não encontrado"})");
            if (Version.TryParse(pecas.VersaoAnaliseGoogleKit, out var doKit) && Version.TryParse(pecas.VersaoAnaliseGoogle, out var instalada) && doKit > instalada)
                avisos.Add($"O kit tem o análise Google {pecas.VersaoAnaliseGoogleKit}; instalado {pecas.VersaoAnaliseGoogle}. Atualize pelo INSTALAR_KIT_PMVNI.cmd.");
        }

        if (pecas.Python is null) partes.Add("Python não encontrado");
        else
        {
            var py = await Rodar(LinhasDeComando.PythonVersao(pecas.Python, pecas.PythonPeloLauncher), Diagnostico.LimiteComando, ct).ConfigureAwait(false);
            var numero = py.Codigo == 0 ? Diagnostico.VersaoPython(py.Saida) : null;
            partes.Add(numero is null ? "Python não verificado: " + Diagnostico.PrimeiraLinha(py.Saida) : "Python " + numero);
        }

        if (modo == ModoAcesso.Conta)
        {
            if (ids is { Count: > 0 } && EtapasSemModelo(PerfilGemini.ContaUltra, ids) is { Count: > 0 } faltas)
                avisos.Add("A conta não oferece o modelo e o nível de: " + string.Join(", ", faltas) + ". A análise não começa sem eles (nenhum outro modelo é usado no lugar).");
        }
        else if (modo == ModoAcesso.Chave)
        {
            if (chave is null) partes.Add("nenhuma chave guardada");
            else
            {
                var r = await ConferirChaveAsync(chave, ct).ConfigureAwait(false);
                partes.Add(r.Mensagem);
                if (r.Aceita && perfil is { } pf && EtapasSemModeloNaApi(pf, r.Modelos) is { Count: > 0 } faltas)
                    avisos.Add($"A chave não lista o modelo de: {string.Join(", ", faltas)} ({MatrizGemini.Nome(pf)}). A análise não começa sem eles.");
                if (r.Aceita) avisos.Add("A chave foi aceita, mas o Google não informa por ela se o projeto tem créditos: o perfil (com créditos ou gratuita) é o que você escolher aqui.");
            }
            if (perfil is null) avisos.Add(OrquestradorAnalise.SemPerfilApi);
            else if (perfil == PerfilGemini.ApiGratuita)
                avisos.Add("API gratuita: só para testes com processos fictícios. Na camada gratuita o Google pode usar o conteúdo enviado para melhorar os produtos dele.");
        }
        return new SituacaoProvedor(string.Join(" · ", partes), avisos) { ContaConectada = ids is null ? null : ids.Count > 0 };
    }

    /// <summary>Sempre sem chaves herdadas: o diagnóstico da conta olha o login do programa, nunca uma chave do ambiente.</summary>
    static Task<(int Codigo, string Saida)> Rodar((string Exe, IReadOnlyList<string> Args) linha, TimeSpan limite, CancellationToken ct) =>
        ExecucaoProcesso.RodarAsync(linha.Exe, linha.Args, Path.GetDirectoryName(linha.Exe) is { Length: > 0 } pasta ? pasta : Environment.CurrentDirectory,
            env => AmbienteFilho.Aplicar(env, Provedor.Gemini, ModoAcesso.Conta, null), limite, ct);
}
