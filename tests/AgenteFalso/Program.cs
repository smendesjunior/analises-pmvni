using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AgenteFalso;

/// <summary>Agente de mentira dos testes e do roteiro (desenho §12.12): faz o papel do <c>codex.exe</c> (<c>exec</c>, <c>--version</c>, <c>login status</c>),
/// do <c>claude.exe</c> (<c>--version</c>, <c>auth status</c>) e do <c>python.exe</c> com o <c>pro_runtime.py</c> (<c>--case P iniciar|situacao|auto</c>).
/// Nunca fala com rede nem com modelo. O cenário vem do JSON indicado por <c>AGENTE_FALSO_CENARIO</c> (campos em tests/corpus/agentes).
/// O que recebeu fica em <c>&lt;cwd&gt;\.pmvni-ai\agente-falso\recebido.json</c> (exec) e <c>chamadas.txt</c> (python); só o início do sha256 de uma chave
/// é informado, nunca a chave.</summary>
static class Program
{
    static readonly JsonSerializerOptions Opcoes = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    static readonly UTF8Encoding Utf8 = new(false);
    static JsonObject cenario = new();
    static string pastaCenario = "";
    static StreamWriter saida = null!;

    static int Main(string[] args)
    {
        saida = new StreamWriter(Console.OpenStandardOutput(), Utf8) { AutoFlush = true, NewLine = "\n" };
        Console.SetError(new StreamWriter(Console.OpenStandardError(), Utf8) { AutoFlush = true, NewLine = "\n" });
        CarregarCenario();
        try
        {
            if (args.Length == 0) return Uso();
            if (cenario["analise_chatgpt"] is JsonObject chatgpt) return AnaliseChatGpt(args, chatgpt);
            if (args[0] == "dormir") { Dormir(60000); return 0; }
            if (args.Contains("--contas") && args.Any(a => Path.GetFileName(a) == "orcamento_lb.py"))
            {
                saida.WriteLine("Conta fictícia · 5 h 80% livres · semana 15% livres");
                return 0;
            }
            if (args.Any(a => a.EndsWith("executar_analise_62.py", StringComparison.OrdinalIgnoreCase))) return Analise62(args);
            if (Array.IndexOf(args, "--case") >= 0) return Python(args);
            if (args[0] == "exec" || args is ["-a", "never", "exec", ..]) return Exec(args);
            if (args.TakeLast(3).SequenceEqual(new[] { "mcp", "list", "--json" }))
            {
                saida.WriteLine("[]");
                return 0;
            }
            if (Array.IndexOf(args, "--version") >= 0 || Array.IndexOf(args, "-V") >= 0) return Versao();
            if (args.Length == 2 && args[0] == "login" && args[1] == "status") return LoginStatus();
            if (args.Length == 2 && args[0] == "auth" && args[1] == "status") return AuthStatus();
            if (args[^1] == "models") return ModelosAgy();
            return Uso();
        }
        finally { saida.Flush(); }
    }

    static int AnaliseChatGpt(string[] args, JsonObject dados)
    {
        if (dados["rota"]?.ToString() is { } rota && !args.Contains("--version"))
        {
            if (!args.Contains($"model_provider=\"{rota}\"")) return Uso();
            if (args[0] == "exec" && args.Contains("--ignore-user-config") != (rota == "openai")) return Uso();
        }
        if (args.TakeLast(2).SequenceEqual(new[] { "login", "status" })) return LoginStatus();
        if (args is ["--version"]) return Versao();
        if (args.LastOrDefault() == "app-server")
        {
            while (Console.ReadLine() is { } pedido)
            {
                var rpc = JsonNode.Parse(pedido)!;
                if (rpc["method"]?.ToString() == "initialize") saida.WriteLine("""{"id":1,"result":{}}""");
                if (rpc["method"]?.ToString() == "account/rateLimits/read")
                {
                    saida.WriteLine("""{"id":2,"result":{"accountId":"conta-ficticia","rateLimits":{"limitId":"codex","planType":"pro","primary":{"windowDurationMins":10080,"usedPercent":8,"resetsAt":1900000000},"secondary":null}}}""");
                    break;
                }
            }
            return 0;
        }
        if (args[0] != "exec" || !args.Contains("--json")) return Uso();
        var prompt = Console.In.ReadToEnd();
        if (!prompt.Contains("SKILL.md") || !prompt.Contains("entrega-plugin.rpmvni")) return Uso();
        File.AppendAllLines(dados["rodadas"]!.GetValue<string>(), ["exec"]);
        if (File.Exists(dados["pausa"]!.GetValue<string>())) Dormir(30000);
        if (File.Exists(dados["montagem_invalida"]!.GetValue<string>()))
            File.WriteAllText("entrega-plugin.rpmvni", "pacote incompleto");
        else
        {
            var id = File.ReadAllText("id-execucao.txt");
            var gruposAtuais = Enumerable.Range(1, 15).Select(numero => new { id = numero, id_execucao = id });
            File.WriteAllText("conferencia.json", System.Text.Json.JsonSerializer.Serialize(new { grupos = gruposAtuais, auditoria = new { id_execucao = id }, consulta_qgis = new { resultado = "SEM_ACESSO" } }));
            File.Copy(dados["pacote"]!.GetValue<string>(), "entrega-plugin.rpmvni");
        }
        saida.WriteLine("""{"type":"item.completed","item":{"type":"command_execution","command":"ler planta fictícia","aggregated_output":"resultado integral fictício"}}""");
        Console.Error.WriteLine("diagnóstico fictício");
        saida.WriteLine("""{"type":"turn.completed","usage":{"input_tokens":120,"cached_input_tokens":85,"output_tokens":12}}""");
        return 0;
    }
    static int Uso()
    {
        Console.Error.WriteLine("agente falso: argumentos não reconhecidos");
        return 64;
    }

    static void CarregarCenario()
    {
        var caminho = Environment.GetEnvironmentVariable("AGENTE_FALSO_CENARIO");
        if (string.IsNullOrWhiteSpace(caminho) || !File.Exists(caminho)) return;
        cenario = JsonNode.Parse(File.ReadAllText(caminho, Utf8))!.AsObject();
        pastaCenario = Path.GetDirectoryName(Path.GetFullPath(caminho))!;
    }

    static string Texto(JsonNode? n, string padrao) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : padrao;
    static int Inteiro(JsonNode? n, int padrao) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : padrao;

    static void Dormir(int ms)
    {
        // Sempre termina sozinho (no máximo o pedido): se o teste esquecer de matar, não sobra processo.
        var fim = Stopwatch.StartNew();
        while (fim.ElapsedMilliseconds < ms) Thread.Sleep(50);
    }

    static string Sha8(string v) => Convert.ToHexStringLower(SHA256.HashData(Utf8.GetBytes(v)))[..8];

    /// <summary>"CHAVE_PRESENTE &lt;8 hex do sha256&gt;" ou "CHAVE_AUSENTE"; devolve também o nome da variável.</summary>
    static (string Texto, string? Variavel) Chave()
    {
        foreach (var nome in new[] { "CODEX_API_KEY", "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "ANTHROPIC_AUTH_TOKEN", "GEMINI_API_KEY", "GOOGLE_API_KEY" })
        {
            var v = Environment.GetEnvironmentVariable(nome);
            if (!string.IsNullOrEmpty(v)) return ("CHAVE_PRESENTE " + Sha8(v), nome);
        }
        return ("CHAVE_AUSENTE", null);
    }

    static int Versao()
    {
        if (Texto(cenario["trava"], "") == "versao")
        {
            var pidArquivo = Texto(cenario["pid_arquivo"], "");
            if (pidArquivo.Length > 0) File.WriteAllText(pidArquivo, Environment.ProcessId.ToString(), Utf8);
            Dormir(60000);
            return 0;
        }
        string[] padrao = { "codex-cli 0.99.0", "2.9.0 (Claude Code)", "Python 3.14.0" };
        var versoes = cenario["versoes"] is JsonArray a ? a.Select(x => x?.GetValue<string>() ?? "").ToArray() : padrao;
        foreach (var v in versoes) saida.WriteLine(v);
        return 0;
    }

    static bool Logado() => Texto(cenario["login"], "sim") != "nao";

    /// <summary>O papel do <c>agy models</c> (Antigravity): a lista "id&lt;tab&gt;nome" do cenário (<c>modelos_agy</c>), ou os seis identificadores da
    /// matriz do Gemini; sem login, só o cabeçalho (lista vazia, como o agy de verdade).</summary>
    static int ModelosAgy()
    {
        saida.WriteLine("Fetching available models...");
        if (!Logado()) return 0;
        string[] padrao = { "gemini-4-hydrogen-high", "gemini-4-hydrogen-xhigh", "gemini-4-hydrogen-medium", "gemini-4-carbon-high", "gemini-4-argon-high" };
        var ids = cenario["modelos_agy"] is JsonArray a ? a.Select(x => x?.GetValue<string>() ?? "").ToArray() : padrao;
        foreach (var id in ids) saida.WriteLine(id + "\t" + id);
        return 0;
    }

    static int LoginStatus()
    {
        saida.WriteLine(Logado() ? "Logged in using ChatGPT" : "Not logged in");
        return Logado() ? 0 : 1;
    }

    static int AuthStatus()
    {
        var obj = new JsonObject
        {
            ["loggedIn"] = Logado(),
            ["authMethod"] = Logado() ? "claude.ai" : "none",
            ["apiProvider"] = "firstParty",
        };
        saida.WriteLine(obj.ToJsonString(Opcoes));
        return Logado() ? 0 : 1;
    }

    // ---------------------------------------------------------------- codex exec

    static string? Valor(string[] args, params string[] nomes)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (nomes.Contains(args[i])) return args[i + 1];
        return null;
    }

    static int Exec(string[] args)
    {
        var cd = Valor(args, "--cd", "-C");
        var ultima = Valor(args, "--output-last-message", "-o");
        string entrada;
        using (var stdin = new StreamReader(Console.OpenStandardInput(), Utf8)) entrada = stdin.ReadToEnd();

        var cwd = Environment.CurrentDirectory;
        var controle = Path.Combine(cwd, ".pmvni-ai", "agente-falso");
        Directory.CreateDirectory(controle);
        var pids = new JsonObject { ["pai"] = Environment.ProcessId };
        Process? filho = null;
        if (cenario["filho"]?.GetValue<bool>() == true)
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("dormir");
            filho = Process.Start(psi);
            if (filho is not null) pids["filho"] = filho.Id;
        }
        GravarJson(Path.Combine(controle, "pids.json"), pids);

        var (textoChave, variavel) = Chave();
        GravarJson(Path.Combine(controle, "recebido.json"), new JsonObject
        {
            ["argv"] = new JsonArray(args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
            ["cwd"] = cwd,
            ["cd"] = cd,
            ["entrada"] = entrada,
            ["chave"] = textoChave,
            ["variavel"] = variavel,
        });

        var intervalo = Inteiro(cenario["intervalo_ms"], 0);
        if (cenario["eventos"] is JsonArray eventos)
            foreach (var e in eventos)
            {
                saida.WriteLine(e?.GetValue<string>() ?? "");
                if (intervalo > 0) Thread.Sleep(intervalo);
            }
        var linhas = Inteiro(cenario["linhas"], 0);
        for (var i = 1; i <= linhas; i++)
            saida.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":\"linha " + i + "\"}}");
        if (cenario["erro"] is JsonArray erros)
            foreach (var e in erros) Console.Error.WriteLine(e?.GetValue<string>() ?? "");

        // Agente que repete a própria chave (um "echo" do shell dele): crua e escapada como JSON, na saída, no erro e na última mensagem.
        var eco = "";
        if (cenario["ecoar_chave"]?.GetValue<bool>() == true)
        {
            var valor = new[] { "CODEX_API_KEY", "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "ANTHROPIC_AUTH_TOKEN" }
                .Select(Environment.GetEnvironmentVariable).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
            eco = valor;
            saida.WriteLine("eco cru: " + valor);
            saida.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":" + JsonSerializer.Serialize("eco json padrão: " + valor) + "}}");
            saida.WriteLine("{\"type\":\"item.completed\",\"item\":{\"type\":\"agent_message\",\"text\":" + JsonSerializer.Serialize("eco json relaxado: " + valor, Opcoes) + "}}");
            Console.Error.WriteLine("eco erro: " + valor);
        }

        // Inundações: uma linha enorme sem quebra; muitas linhas na saída; muitas linhas no erro.
        var semQuebra = Inteiro(cenario["inundacao_sem_quebra_mb"], 0);
        if (semQuebra > 0)
        {
            var bloco = new string('x', 64 * 1024);
            for (var i = 0; i < semQuebra * 16; i++) saida.Write(bloco);
            saida.Write("\nfim da inundação sem quebra\n");
        }
        var linhasMb = Inteiro(cenario["inundacao_linhas_mb"], 0);
        if (linhasMb > 0)
        {
            var linha = new string('y', 1000);
            for (var i = 0; i < linhasMb * 1024; i++) saida.WriteLine(linha);
            saida.WriteLine("fim da inundação de linhas");
        }
        var erroMb = Inteiro(cenario["inundacao_erro_mb"], 0);
        if (erroMb > 0)
        {
            var linha = new string('e', 1000);
            for (var i = 0; i < erroMb * 1024; i++) Console.Error.WriteLine(linha);
        }

        Dormir(Inteiro(cenario["dormir_ms"], 0));

        if (ultima is not null && (cenario["ultima_mensagem"] is not null || eco.Length > 0))
            File.WriteAllText(ultima, Texto(cenario["ultima_mensagem"], "") + (eco.Length > 0 ? "ultima: " + eco : ""), Utf8);
        Entregar(Path.Combine(cwd, ".pmvni-ai", "analise"), pdf: false);
        return Inteiro(cenario["codigo"], 0);
    }

    static string CaminhoRelatorio()
    {
        var r = Texto(cenario["relatorio"], "relatorio_dados_0001.json");
        return Path.IsPathRooted(r) ? r : Path.Combine(pastaCenario, r);
    }

    /// <summary>Grava o relatório e o delivery.json como o cenário manda ("ok" é o padrão).</summary>
    static void Entregar(string pasta, bool pdf)
    {
        var modo = Texto(cenario["delivery"], pastaCenario.Length > 0 ? "ok" : "ausente");
        if (modo == "ausente") return;
        Directory.CreateDirectory(pasta);
        var delivery = Path.Combine(pasta, "delivery.json");
        if (modo == "json_invalido") { File.WriteAllText(delivery, "{ isso não é json", Utf8); return; }
        if (modo == "sem_document") { GravarJson(delivery, new JsonObject { ["registry"] = "x" }); return; }

        var local = Environment.GetEnvironmentVariable("PMVNI_PROCESSO_ORIGEM") is not null;
        if (local) pdf = true;
        var documento = Path.Combine(pasta, "relatorio_dados.json");
        File.Copy(CaminhoRelatorio(), documento, overwrite: true);
        var obj = new JsonObject { ["registry"] = Path.Combine(pasta, "registry.json"), ["document"] = modo == "fora" ? CaminhoRelatorio() : documento };
        if (pdf)
        {
            var arquivoPdf = Path.Combine(pasta, "relatorio.pdf");
            File.WriteAllText(arquivoPdf, "%PDF-1.4 falso", Utf8);
            obj["pdf"] = arquivoPdf;
        }
        obj["visual_audit"] = Path.Combine(pasta, "auditoria_visual.json");
        GravarJson(delivery, obj);
        if (local && modo == "ok")
        {
            // Recibos sintéticos com o mesmo vínculo entre entrega, montagem e estado que o controlador em produção.
            static string Sha(string caminho) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(caminho)));
            var arquivo = obj["pdf"]!.GetValue<string>();
            var montagem = Path.Combine(pasta, "montagem.json");
            GravarJson(montagem, new JsonObject { ["pdf_sha256"] = Sha(arquivo), ["document_sha256"] = Sha(documento),
                ["delivery_sha256"] = Sha(delivery) });
            GravarJson(Path.Combine(pasta, "engine_state.json"), new JsonObject
            {
                ["case"] = Directory.GetParent(pasta)!.Parent!.FullName,
                ["editor_execution"] = Environment.GetEnvironmentVariable("PMVNI_EXECUCAO_ID"),
                ["gate"] = new JsonObject { ["validators"] = "ALL_PASSED", ["pdf"] = arquivo, ["pdf_sha256"] = Sha(arquivo) },
                ["stages"] = new JsonObject { ["produce"] = new JsonObject { ["evidence"] = new JsonArray(
                    new JsonObject { ["path"] = montagem, ["sha256"] = Sha(montagem) }) } },
            });
        }
    }

    static void GravarJson(string caminho, JsonNode obj)
    {
        var tmp = caminho + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, obj.ToJsonString(Opcoes), Utf8);
        // Quem lê o arquivo naquele instante (o situacao, o editor, o antivírus) o segura por milissegundos: a troca espera, nunca derruba o agente.
        for (var i = 1; ; i++)
        {
            try { File.Move(tmp, caminho, overwrite: true); return; }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && i < 3000) { Thread.Sleep(10); }
        }
    }

    // ---------------------------------------------------------------- python + executar_analise_62.py (rota ChatGPT, Tarefa 12)

    /// <summary>O papel do controlador do analise (<c>python -B -X utf8 executar_analise_62.py --case P [--inputs …]</c>), no contrato da Tarefa 2: saída
    /// 0 (ENTREGUE ou ETAPA_OK), 20 (AGUARDANDO_COTA), 21 (DECISAO) ou 1 (FALHA), com o JSON da situação na última linha. Cada chamada usa o passo seguinte de
    /// <c>analise62</c> do cenário (o último se repete); o que o passo não diz vem do topo do cenário (eventos, erro, filho, dormir_ms, delivery…).
    /// Grava <c>recebido.json</c> e <c>pids.json</c> como o <c>exec</c> e acrescenta uma linha <c>analise62</c> ao <c>chamadas.txt</c>.</summary>
    static int Analise62(string[] args)
    {
        var caso = Valor(args, "--case") ?? Environment.CurrentDirectory;
        var cwd = Environment.CurrentDirectory;
        var controle = Path.Combine(caso, ".pmvni-ai", "agente-falso");
        Directory.CreateDirectory(controle);
        var contador = Path.Combine(controle, "analise62-chamadas.txt");
        File.AppendAllText(contador, "x\n", Utf8);
        var n = File.ReadAllLines(contador, Utf8).Length;
        var passos = cenario["analise62"] as JsonArray;
        var passo = passos is { Count: > 0 } ? passos[Math.Min(n, passos.Count) - 1] as JsonObject : null;
        JsonNode? Campo(string nome) => passo is not null && passo.ContainsKey(nome) ? passo[nome] : cenario[nome];

        string entrada;
        using (var stdin = new StreamReader(Console.OpenStandardInput(), Utf8)) entrada = stdin.ReadToEnd();
        var pids = new JsonObject { ["pai"] = Environment.ProcessId };
        if (Campo("filho")?.GetValue<bool>() == true)
        {
            var psi = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("dormir");
            var filho = Process.Start(psi);
            if (filho is not null) pids["filho"] = filho.Id;
        }
        GravarJson(Path.Combine(controle, "pids.json"), pids);

        var (textoChave, variavel) = Chave();
        var rota = Environment.GetEnvironmentVariable("PMVNI_ANALISE_ROTA_CODEX") ?? "-";
        var primeiroDoPath = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator).FirstOrDefault() ?? "";
        GravarJson(Path.Combine(controle, "recebido.json"), new JsonObject
        {
            ["argv"] = new JsonArray(args.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
            ["cwd"] = cwd, ["cd"] = caso, ["entrada"] = entrada, ["chave"] = textoChave, ["variavel"] = variavel, ["rota"] = rota,
            ["path0"] = primeiroDoPath,
            // Tarefa 19: a tabela de modelos que o editor passou (o caminho do arquivo no registro); null sem ela.
            ["modelos"] = Environment.GetEnvironmentVariable("PMVNI_MODELOS_POR_ROTA"),
        });
        File.AppendAllText(Path.Combine(controle, "chamadas.txt"),
            "analise62\t" + JsonSerializer.Serialize(args, Opcoes) + "\t" + textoChave + "\t" + rota + "\t"
            + DateTime.UtcNow.ToString("o", System.Globalization.CultureInfo.InvariantCulture) + "\n", Utf8);

        var intervalo = Inteiro(Campo("intervalo_ms"), 0);
        if (Campo("eventos") is JsonArray eventos)
            foreach (var e in eventos)
            {
                saida.WriteLine(e?.GetValue<string>() ?? "");
                if (intervalo > 0) Thread.Sleep(intervalo);
            }
        var linhas = Inteiro(Campo("linhas"), 0);
        for (var i = 1; i <= linhas; i++) saida.WriteLine("linha " + i);
        if (Campo("erro") is JsonArray erros)
            foreach (var e in erros) Console.Error.WriteLine(e?.GetValue<string>() ?? "");

        // Controlador que repete a chave (um "echo" de um filho dele): crua e escapada como JSON, na saída e no erro.
        if (Campo("ecoar_chave")?.GetValue<bool>() == true)
        {
            var valor = new[] { "CODEX_API_KEY", "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "ANTHROPIC_AUTH_TOKEN" }
                .Select(Environment.GetEnvironmentVariable).FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? "";
            saida.WriteLine("eco cru: " + valor);
            saida.WriteLine("eco json padrão: " + JsonSerializer.Serialize(valor));
            saida.WriteLine("eco json relaxado: " + JsonSerializer.Serialize(valor, Opcoes));
            Console.Error.WriteLine("eco erro: " + valor);
        }
        var semQuebra = Inteiro(Campo("inundacao_sem_quebra_mb"), 0);
        if (semQuebra > 0)
        {
            var bloco = new string('x', 64 * 1024);
            for (var i = 0; i < semQuebra * 16; i++) saida.Write(bloco);
            saida.Write("\nfim da inundação sem quebra\n");
        }
        var linhasMb = Inteiro(Campo("inundacao_linhas_mb"), 0);
        if (linhasMb > 0)
        {
            var linha = new string('y', 1000);
            for (var i = 0; i < linhasMb * 1024; i++) saida.WriteLine(linha);
            saida.WriteLine("fim da inundação de linhas");
        }
        var erroMb = Inteiro(Campo("inundacao_erro_mb"), 0);
        if (erroMb > 0)
        {
            var linha = new string('e', 1000);
            for (var i = 0; i < erroMb * 1024; i++) Console.Error.WriteLine(linha);
        }
        Dormir(Inteiro(Campo("dormir_ms"), 0));

        var codigoCenario = Inteiro(Campo("codigo"), 0);
        var situacao = Texto(Campo("situacao"), codigoCenario == 0 ? "ENTREGUE" : "FALHA");
        var motivo = Texto(Campo("motivo"), situacao == "FALHA" ? "falhou de propósito" : "");
        // Decisão respondida pelo editor: o arquivo que a decisão anterior nomeou tem de existir; o conteúdo vai para o chamadas.txt.
        if (Texto(Campo("exigir_resposta"), "") is { Length: > 0 } exigida)
        {
            var arquivo = Path.IsPathRooted(exigida) ? exigida : Path.Combine(caso, ".pmvni-ai", "analise", exigida);
            if (File.Exists(arquivo))
                File.AppendAllText(Path.Combine(controle, "chamadas.txt"), "resposta\t" + File.ReadAllText(arquivo, Utf8).ReplaceLineEndings(" ") + "\n", Utf8);
            else { situacao = "FALHA"; motivo = "a resposta da decisão não foi gravada: " + arquivo; }
        }
        if (situacao == "ENTREGUE") Entregar(Path.Combine(caso, ".pmvni-ai", "analise"), pdf: false);
        var retomar = Campo("retomar_em_s") is JsonValue v && v.TryGetValue<double>(out var s)
            ? (JsonNode?)DateTime.UtcNow.AddSeconds(s).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture) : null;
        var final = new JsonObject
        {
            ["situacao"] = situacao, ["etapa"] = Texto(Campo("etapa"), "final_review"), ["retomar_em_utc"] = retomar, ["motivo"] = motivo,
            ["decisao"] = Campo("decisao")?.DeepClone(),
        };
        if (!(Campo("sem_json")?.GetValue<bool>() ?? false)) saida.WriteLine(final.ToJsonString(Opcoes));
        if (passo?["codigo"] is null && situacao != "FALHA")
            return situacao switch { "AGUARDANDO_COTA" => 20, "DECISAO" => 21, _ => 0 };
        return situacao == "FALHA" && codigoCenario == 0 ? 1 : codigoCenario;
    }

    // ---------------------------------------------------------------- python + pro_runtime.py

    static readonly string[] Etapas = { "prepare", "analyze", "technical_audit", "produce", "audit", "final_review" };

    static readonly Dictionary<string, string> Rotulos = new()
    {
        ["CONCLUIDO"] = "concluída", ["FALHOU"] = "parou com erro", ["OBJECOES"] = "parou com objeções da revisão final",
        ["PAUSADO_COTA"] = "pausada: cota do Claude esgotada", ["CANCELADO"] = "cancelada", ["EM_ANDAMENTO"] = "em andamento",
        ["AGUARDANDO_COTA"] = "aguardando a renovação da cota", ["INTERROMPIDA"] = "interrompida (processo encerrado)",
        ["INICIANDO"] = "iniciando", ["NAO_PARTIU"] = "não partiu (a janela não abriu)",
        ["DECISAO"] = "aguardando a decisão do analista", ["FALHA"] = "parou com erro",
    };

    static string Agora() => DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

    static bool Vivo(int pid)
    {
        if (pid <= 0) return false;
        try { return !Process.GetProcessById(pid).HasExited; }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    static JsonObject? LerStatus(string controle)
    {
        var caminho = Path.Combine(controle, "auto-status.json");
        for (var i = 0; i < 20; i++)
        {
            try
            {
                if (!File.Exists(caminho)) return null;
                using var fs = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);   // não segura o arquivo contra a troca
                using var leitor = new StreamReader(fs, Utf8);
                return JsonNode.Parse(leitor.ReadToEnd())!.AsObject();
            }
            catch (IOException) { Thread.Sleep(20); }
            catch (JsonException) { Thread.Sleep(20); }
        }
        return null;
    }

    static void GravarStatus(string controle, JsonObject status)
    {
        Directory.CreateDirectory(controle);
        status["atualizado_em"] = Agora();
        GravarJson(Path.Combine(controle, "auto-status.json"), status);
    }

    static void Evento(JsonObject status, string texto)
    {
        var lista = status["eventos"] as JsonArray ?? new JsonArray();
        lista.Add(new JsonObject { ["quando"] = Agora(), ["texto"] = texto });
        status["eventos"] = lista;
    }

    static void AcrescentarLog(string controle, string linha) =>
        File.AppendAllText(Path.Combine(controle, "auto-log.txt"), linha + "\n", Utf8);

    static int Python(string[] args)
    {
        var i = Array.IndexOf(args, "--case");
        var caso = args[i + 1];
        var acao = args.Length > i + 2 ? args[i + 2] : "";
        // O mesmo programa faz o papel do análise Google (google_runtime.py): outra pasta de controle, a rota e o perfil declarados pelo editor.
        var google = args.Any(a => a.EndsWith("google_runtime.py", StringComparison.OrdinalIgnoreCase) || a == "--google");
        var controle = Path.Combine(caso, ".pmvni-ai", google ? "analise-google" : "analise-pro");
        var pastaFalso = Path.Combine(caso, ".pmvni-ai", "agente-falso");
        Directory.CreateDirectory(pastaFalso);
        File.AppendAllText(Path.Combine(pastaFalso, "chamadas.txt"),
            acao + "\t" + JsonSerializer.Serialize(args, Opcoes) + "\t" + Chave().Texto + "\t"
            + (Environment.GetEnvironmentVariable(google ? "PMVNI_ANALISE_GOOGLE_ROTA" : "PMVNI_ANALISE_PRO_ROTA") ?? "-")   // a rota que o editor declarou
            + (google ? "\t" + (Environment.GetEnvironmentVariable("PMVNI_ANALISE_GOOGLE_PERFIL") ?? "-") + "\t" + (Environment.GetEnvironmentVariable("PMVNI_ANALISE_GOOGLE_FICTICIO") ?? "-") : "")
            + "\n", Utf8);
        // Tarefa 19: a tabela de modelos que o editor passou a esta ação (o caminho; "-" sem ela), num arquivo à parte para não mudar as colunas acima.
        File.AppendAllText(Path.Combine(pastaFalso, "modelos.txt"), acao + "\t" + (Environment.GetEnvironmentVariable("PMVNI_MODELOS_POR_ROTA") ?? "-") + "\n", Utf8);

        try
        {
            switch (acao)
            {
                case "iniciar": return Iniciar(caso, controle, google);
                case "situacao": return Situacao(caso, controle);
                case "auto": return Auto(caso, controle, Pai(args), google);
                case "conferir" when google: return Conferir();
                default: return Uso();
            }
        }
        catch (Exception e)
        {
            // Um processo que cai em silêncio deixa o teste esperando por um status que nunca vem: o motivo fica ao lado das chamadas.
            try { File.AppendAllText(Path.Combine(pastaFalso, "erros.txt"), acao + "\t" + e + "\n", Utf8); } catch (IOException) { }
            throw;
        }
    }

    /// <summary>Um registro de como a análise automática partiu (ao lado das chamadas), para um teste que espera por ela saber o que houve.</summary>
    static void DiagnosticoDaPartida(string caso, string texto)
    {
        try
        {
            var pasta = Path.Combine(caso, ".pmvni-ai", "agente-falso");
            Directory.CreateDirectory(pasta);
            File.AppendAllText(Path.Combine(pasta, "partidas.txt"), Agora() + " " + texto + "\n", Utf8);
        }
        catch (IOException) { }
    }

    const int TentativasDeSubida = 3;
    const int PrazoDaSubidaMs = 10_000;

    /// <summary>O arquivo que o auto grava ao subir (o pid dele): é o que o iniciar espera antes de responder.</summary>
    static string MarcaDoAuto(string controle) => Path.Combine(controle, "auto-subiu.txt");

    /// <summary>A conferência do análise Google antes da partida: o cenário diz o código e o JSON (<c>conferir</c>); sem ele, tudo disponível.</summary>
    static int Conferir()
    {
        var c = cenario["conferir"] as JsonObject;
        var resposta = c?["saida"]?.DeepClone() as JsonObject ?? new JsonObject { ["ok"] = true, ["faltas"] = new JsonArray() };
        resposta["modo"] ??= Environment.GetEnvironmentVariable("PMVNI_ANALISE_GOOGLE_ROTA");
        resposta["perfil"] ??= Environment.GetEnvironmentVariable("PMVNI_ANALISE_GOOGLE_PERFIL");
        saida.WriteLine(resposta.ToJsonString(Opcoes));
        return Inteiro(c?["codigo"], 0);
    }

    static int Iniciar(string caso, string controle, bool google = false)
    {
        var atual = LerStatus(controle);
        var situacaoAtual = Texto(atual?["situacao"], "");
        if (atual is not null && (situacaoAtual is "EM_ANDAMENTO" or "AGUARDANDO_COTA") && Vivo(Inteiro(atual["pid"], 0)))
        {
            saida.WriteLine(new JsonObject { ["situacao"] = "JA_EM_ANDAMENTO", ["etapa"] = atual["etapa"]?.DeepClone() }.ToJsonString(Opcoes));
            return 0;
        }
        var inicio = Agora();
        var status = new JsonObject { ["case"] = caso, ["situacao"] = "INICIANDO", ["pid"] = null, ["inicio"] = inicio };
        if (google)
        {
            // Como o plugin: o modo e o perfil declarados na partida ficam no status e valem para o trabalho inteiro.
            status["modo"] = Environment.GetEnvironmentVariable("PMVNI_ANALISE_GOOGLE_ROTA");
            status["perfil"] = Environment.GetEnvironmentVariable("PMVNI_ANALISE_GOOGLE_PERFIL");
        }
        Evento(status, "Partida pedida pela conversa");
        GravarStatus(controle, status);

        // Parte a si mesmo desanexado (sem herdar os canais deste processo): UseShellExecute, como o kit abre o executar-auto.cmd pelo Explorer.
        // O auto confirma que subiu gravando a marca (antes de qualquer status); sem a marca no prazo (um processo que o sistema não deixou
        // subir, sob carga), parte de novo. Só depois de confirmado o iniciar responde, e o auto só grava EM_ANDAMENTO quando este processo sair:
        // quem acompanha nunca vê a análise andando enquanto o iniciar ainda existe (cancelar o iniciar levaria a árvore dele junto).
        var marca = MarcaDoAuto(controle);
        for (var tentativa = 1; tentativa <= TentativasDeSubida; tentativa++)
        {
            try { File.Delete(marca); } catch (IOException) { }
            var psi = new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                Arguments = "--case \"" + caso + "\" auto --pai " + Environment.ProcessId + (google ? " --google" : ""),
            };
            var filho = Process.Start(psi);
            DiagnosticoDaPartida(caso, "tentativa " + tentativa + ": filho pid=" + (filho?.Id.ToString() ?? "nenhum"));
            var espera = Stopwatch.StartNew();
            while (espera.ElapsedMilliseconds < PrazoDaSubidaMs && !File.Exists(marca) && filho is { HasExited: false }) Thread.Sleep(20);
            if (File.Exists(marca))
            {
                DiagnosticoDaPartida(caso, "tentativa " + tentativa + ": subiu em " + espera.ElapsedMilliseconds + " ms");
                break;
            }
            DiagnosticoDaPartida(caso, "tentativa " + tentativa + ": não subiu em " + espera.ElapsedMilliseconds + " ms (saiu=" + (filho?.HasExited ?? true)
                + (filho is { HasExited: true } ? ", código " + filho.ExitCode : "") + ")");
            try { if (filho is { HasExited: false }) filho.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if (tentativa == TentativasDeSubida)
            {
                saida.WriteLine(new JsonObject { ["situacao"] = "FALHOU", ["erro"] = "O processo da análise não subiu depois de " + TentativasDeSubida + " tentativas." }.ToJsonString(Opcoes));
                return 1;
            }
        }
        saida.WriteLine(new JsonObject
        {
            ["situacao"] = "INICIADA", ["processo"] = caso, ["acompanhamento"] = Path.Combine(controle, "auto-status.json"),
            ["aviso"] = "A análise roda sozinha, numa janela minimizada.",
        }.ToJsonString(Opcoes));
        return 0;
    }

    static int Situacao(string caso, string controle)
    {
        var falha = Inteiro(cenario["codigo_consulta_situacao"], 0);
        if (falha != 0)
        {
            saida.WriteLine("Consulta ao registro indisponível.");
            return falha;
        }
        var st = LerStatus(controle);
        // O auto pode gravar o status final e sair entre a leitura e a conferência do pid: com o pid morto, relê antes de dizer "interrompida".
        if (st is not null && Texto(st["situacao"], "") is "EM_ANDAMENTO" or "AGUARDANDO_COTA" && !Vivo(Inteiro(st["pid"], 0)))
            st = LerStatus(controle) ?? st;
        if (st is null)
        {
            saida.WriteLine(new JsonObject { ["case"] = caso, ["situacao"] = "NAO_INICIADA" }.ToJsonString(Opcoes));
            return 0;
        }
        var situacao = Texto(st["situacao"], "");
        if (situacao == "INICIANDO")
        {
            if (DateTime.TryParse(Texto(st["inicio"], ""), out var inicio) && (DateTime.Now - inicio).TotalSeconds > 180)
            {
                st["situacao"] = "NAO_PARTIU";
                st["erro"] = "A janela da análise não abriu.";
            }
        }
        else if ((situacao is "EM_ANDAMENTO" or "AGUARDANDO_COTA") && !Vivo(Inteiro(st["pid"], 0)))
        {
            st["situacao"] = "INTERROMPIDA";
            st["erro"] = "O processo da análise foi encerrado antes de concluir. Rodar iniciar de novo continua de onde parou.";
        }
        var final = Texto(st["situacao"], "");
        st["rotulo"] = Rotulos.TryGetValue(final, out var rotulo) ? rotulo : final;
        var eventos = st["eventos"] as JsonArray ?? new JsonArray();
        var ultimos = new JsonArray(eventos.Skip(Math.Max(0, eventos.Count - 8)).Select(e => e?.DeepClone()).ToArray());
        st["eventos"] = ultimos;
        saida.WriteLine(st.ToJsonString(Opcoes));
        return 0;
    }

    /// <summary>O pid do iniciar que partiu este auto (<c>--pai N</c>), ou 0.</summary>
    static int Pai(string[] args)
    {
        var i = Array.IndexOf(args, "--pai");
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var pid) ? pid : 0;
    }

    static int Auto(string caso, string controle, int pai, bool google = false)
    {
        // Confirma a subida ao iniciar e espera ele sair (no máximo 15 s) antes de gravar qualquer status.
        Directory.CreateDirectory(controle);
        File.WriteAllText(MarcaDoAuto(controle), Environment.ProcessId.ToString(), Utf8);
        var esperaPai = Stopwatch.StartNew();
        while (pai > 0 && Vivo(pai) && esperaPai.ElapsedMilliseconds < 15_000) Thread.Sleep(20);
        var auto = cenario["auto"] as JsonObject ?? new JsonObject();
        var etapaMs = Inteiro(auto["etapa_ms"], 300);
        var parar = Texto(auto["parar_na_etapa"], "");
        var status = LerStatus(controle) ?? new JsonObject { ["case"] = caso, ["inicio"] = Agora() };
        status["situacao"] = "EM_ANDAMENTO";
        status["pid"] = Environment.ProcessId;
        GravarStatus(controle, status);
        AcrescentarLog(controle, "[" + Agora() + "] análise iniciada");

        // Análise Google: os pontos de controle por etapa (engine_state.json, com o modo e o perfil fixados) e a pausa de uma etapa pelo cenário
        // (auto.pausar_na_etapa, só na primeira passagem): a partida seguinte continua da etapa pendente, sem refazer as concluídas.
        var estadoArq = Path.Combine(controle, "engine_state.json");
        JsonObject? estado = null;
        if (google)
        {
            try { estado = File.Exists(estadoArq) ? JsonNode.Parse(File.ReadAllText(estadoArq, Utf8)) as JsonObject : null; } catch (JsonException) { }
            estado ??= new JsonObject { ["modo"] = status["modo"]?.DeepClone(), ["perfil"] = status["perfil"]?.DeepClone(), ["stages"] = new JsonObject() };
            status["modo"] = estado["modo"]?.DeepClone();
            status["perfil"] = estado["perfil"]?.DeepClone();
            GravarJson(estadoArq, estado);
        }
        var pausar = Texto(auto["pausar_na_etapa"], "");
        var marcaPausa = Path.Combine(controle, "pausou.txt");

        foreach (var etapa in Etapas)
        {
            if (estado?["stages"] is JsonObject feitas && Texto(feitas[etapa]?["status"], "") == "EXECUTED") continue;    // já concluída numa passagem anterior
            status["etapa"] = etapa;
            Evento(status, "Etapa " + etapa);
            GravarStatus(controle, status);
            AcrescentarLog(controle, "[" + Agora() + "] etapa " + etapa);
            if (google && etapa == pausar && !File.Exists(marcaPausa))
            {
                File.WriteAllText(marcaPausa, etapa, Utf8);
                status["situacao"] = Texto(auto["pausa_situacao"], "PAUSADO_COTA");
                status["motivo"] = Texto(auto["motivo"], "cota_conta");
                status["erro"] = Texto(auto["erro"], "Cota esgotada na etapa " + etapa + ".");
                status["fim"] = Agora();
                GravarStatus(controle, status);
                AcrescentarLog(controle, "[" + Agora() + "] pausa na etapa " + etapa);
                return 0;
            }
            // Contrato da Tarefa 2 (cota): AGUARDANDO_COTA com o horário da retomada, só na primeira passagem; depois segue sozinho.
            var marcaCota = Path.Combine(controle, "aguardou-cota.txt");
            if (etapa == Texto(auto["aguardar_cota_na_etapa"], "") && !File.Exists(marcaCota))
            {
                File.WriteAllText(marcaCota, etapa, Utf8);
                var espera = Inteiro(auto["aguardar_s"], 2);
                status["situacao"] = "AGUARDANDO_COTA";
                status["retomar_em_utc"] = DateTime.UtcNow.AddSeconds(espera).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", System.Globalization.CultureInfo.InvariantCulture);
                status["motivo"] = "cota_conta";
                GravarStatus(controle, status);
                AcrescentarLog(controle, "[" + Agora() + "] aguardando a cota");
                Dormir(espera * 1000);
                status["situacao"] = "EM_ANDAMENTO";
                status.Remove("retomar_em_utc");
                status.Remove("motivo");
                GravarStatus(controle, status);
            }
            if (etapa == parar) Dormir(Inteiro(auto["dormir_ms"], 60000));
            else Dormir(etapaMs);
            if (google)
            {
                File.AppendAllText(Path.Combine(caso, ".pmvni-ai", "agente-falso", "etapas.txt"), etapa + "\n", Utf8);
                ((JsonObject)estado!["stages"]!)[etapa] = new JsonObject { ["status"] = "EXECUTED" };
                GravarJson(estadoArq, estado);
            }
        }

        // Contrato da Tarefa 2 (decisão): sem a resposta gravada, para em DECISAO com a pergunta; com ela, segue até o fim.
        if (auto["decisao"] is JsonObject decisao)
        {
            var arquivo = Path.Combine(controle, Texto(decisao["arquivo_resposta"], "decisao_resposta.json"));
            if (!File.Exists(arquivo))
            {
                status["situacao"] = "DECISAO";
                status["decisao"] = decisao.DeepClone();
                status["fim"] = Agora();
                GravarStatus(controle, status);
                AcrescentarLog(controle, "[" + Agora() + "] aguardando a decisão do analista");
                return 0;
            }
            File.AppendAllText(Path.Combine(caso, ".pmvni-ai", "agente-falso", "chamadas.txt"), "resposta\t" + File.ReadAllText(arquivo, Utf8).ReplaceLineEndings(" ") + "\n", Utf8);
            status.Remove("decisao");
        }
        var final = Texto(auto["final"], "CONCLUIDO");
        if (final == "CONCLUIDO")
        {
            Entregar(controle, pdf: true);
            var pdf = Path.Combine(controle, "relatorio.pdf");
            if (File.Exists(pdf)) status["pdf"] = pdf;
        }
        else
        {
            status["erro"] = Texto(auto["erro"], "parou");
        }
        status["situacao"] = final;
        status["fim"] = Agora();
        GravarStatus(controle, status);
        AcrescentarLog(controle, "[" + Agora() + "] fim: " + final);
        return 0;
    }
}
