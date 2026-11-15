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
            if (args[0] == "dormir") { Dormir(60000); return 0; }
            if (Array.IndexOf(args, "--case") >= 0) return Python(args);
            if (args[0] == "exec") return Exec(args);
            if (Array.IndexOf(args, "--version") >= 0 || Array.IndexOf(args, "-V") >= 0) return Versao();
            if (args.Length == 2 && args[0] == "login" && args[1] == "status") return LoginStatus();
            if (args.Length == 2 && args[0] == "auth" && args[1] == "status") return AuthStatus();
            return Uso();
        }
        finally { saida.Flush(); }
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
        foreach (var nome in new[] { "CODEX_API_KEY", "ANTHROPIC_API_KEY", "OPENAI_API_KEY", "ANTHROPIC_AUTH_TOKEN" })
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

        Dormir(Inteiro(cenario["dormir_ms"], 0));

        if (ultima is not null && cenario["ultima_mensagem"] is not null)
            File.WriteAllText(ultima, Texto(cenario["ultima_mensagem"], ""), Utf8);
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
    }

    static void GravarJson(string caminho, JsonNode obj)
    {
        var tmp = caminho + "." + Environment.ProcessId + ".tmp";
        File.WriteAllText(tmp, obj.ToJsonString(Opcoes), Utf8);
        // Quem lê o arquivo naquele instante (o situacao, o editor, o antivírus) o segura por milissegundos: a troca espera, nunca derruba o agente.
        for (var i = 1; ; i++)
        {
            try { File.Move(tmp, caminho, overwrite: true); return; }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && i < 400) { Thread.Sleep(10); }
        }
    }

    // ---------------------------------------------------------------- python + pro_runtime.py

    static readonly string[] Etapas = { "prepare", "analyze", "technical_audit", "produce", "audit", "final_review" };

    static readonly Dictionary<string, string> Rotulos = new()
    {
        ["CONCLUIDO"] = "concluída", ["FALHOU"] = "parou com erro", ["OBJECOES"] = "parou com objeções da revisão final",
        ["PAUSADO_COTA"] = "pausada: cota do Claude esgotada", ["CANCELADO"] = "cancelada", ["EM_ANDAMENTO"] = "em andamento",
        ["AGUARDANDO_COTA"] = "aguardando a renovação da cota", ["INTERROMPIDA"] = "interrompida (processo encerrado)",
        ["INICIANDO"] = "iniciando", ["NAO_PARTIU"] = "não partiu (a janela não abriu)",
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
        var controle = Path.Combine(caso, ".pmvni-ai", "analise-pro");
        var pastaFalso = Path.Combine(caso, ".pmvni-ai", "agente-falso");
        Directory.CreateDirectory(pastaFalso);
        File.AppendAllText(Path.Combine(pastaFalso, "chamadas.txt"),
            acao + "\t" + JsonSerializer.Serialize(args, Opcoes) + "\t" + Chave().Texto + "\n", Utf8);

        switch (acao)
        {
            case "iniciar": return Iniciar(caso, controle);
            case "situacao": return Situacao(caso, controle);
            case "auto": return Auto(caso, controle);
            default: return Uso();
        }
    }

    static int Iniciar(string caso, string controle)
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
        Evento(status, "Partida pedida pela conversa");
        GravarStatus(controle, status);

        // Parte a si mesmo desanexado (sem herdar os canais deste processo): UseShellExecute, como o kit abre o executar-auto.cmd pelo Explorer.
        var psi = new ProcessStartInfo(Environment.ProcessPath!)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            Arguments = "--case \"" + caso + "\" auto",
        };
        Process.Start(psi);
        saida.WriteLine(new JsonObject
        {
            ["situacao"] = "INICIADA", ["processo"] = caso, ["acompanhamento"] = Path.Combine(controle, "auto-status.json"),
            ["aviso"] = "A análise roda sozinha, numa janela minimizada.",
        }.ToJsonString(Opcoes));
        return 0;
    }

    static int Situacao(string caso, string controle)
    {
        var st = LerStatus(controle);
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

    static int Auto(string caso, string controle)
    {
        var auto = cenario["auto"] as JsonObject ?? new JsonObject();
        var etapaMs = Inteiro(auto["etapa_ms"], 300);
        var parar = Texto(auto["parar_na_etapa"], "");
        var status = LerStatus(controle) ?? new JsonObject { ["case"] = caso, ["inicio"] = Agora() };
        status["situacao"] = "EM_ANDAMENTO";
        status["pid"] = Environment.ProcessId;
        GravarStatus(controle, status);
        AcrescentarLog(controle, "[" + Agora() + "] análise iniciada");

        foreach (var etapa in Etapas)
        {
            status["etapa"] = etapa;
            Evento(status, "Etapa " + etapa);
            GravarStatus(controle, status);
            AcrescentarLog(controle, "[" + Agora() + "] etapa " + etapa);
            if (etapa == parar) Dormir(Inteiro(auto["dormir_ms"], 60000));
            else Dormir(etapaMs);
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
