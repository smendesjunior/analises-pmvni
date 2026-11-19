using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml.Controls;

namespace Pmvni.App;

internal sealed class PonteGeminiWeb : IDisposable
{
    readonly WebView2 web;
    readonly HttpListener servidor;
    readonly CancellationTokenSource encerramento = new();
    readonly string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    readonly string arquivo;
    string? sessao;
    int ocupado;
    int fechada;

    public PonteGeminiWeb(WebView2 web, string pasta)
    {
        this.web = web;
        arquivo = Path.Combine(pasta, "ponte-web.json");
        int porta;
        for (var tentativa = 1; ; tentativa++)
        {
            porta = RandomNumberGenerator.GetInt32(49152, 65536);
            servidor = new HttpListener();
            servidor.Prefixes.Add($"http://127.0.0.1:{porta}/");
            try { servidor.Start(); }
            catch (HttpListenerException ex)
            {
                servidor.Close();
                if (tentativa < 5) continue;
                encerramento.Dispose();
                throw new IOException("Não foi possível abrir a conexão local do Gemini. " + ex.Message, ex);
            }
            break;
        }
        try
        {
            var temporario = arquivo + "." + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporario, JsonSerializer.Serialize(new { port = porta, token }));
                File.Move(temporario, arquivo, true);
            }
            finally { File.Delete(temporario); }
            _ = Task.Run(ReceberAsync);
        }
        catch
        {
            servidor.Close();
            encerramento.Dispose();
            throw;
        }
    }

    async Task ReceberAsync()
    {
        while (servidor.IsListening)
        {
            HttpListenerContext contexto;
            try { contexto = await servidor.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { break; }
            _ = AtenderAsync(contexto);
        }
    }

    async Task AtenderAsync(HttpListenerContext contexto)
    {
        var pedido = contexto.Request;
        using var resposta = contexto.Response;
        if (!string.Equals(pedido.Headers["Authorization"], "Bearer " + token, StringComparison.Ordinal)
            || pedido.HttpMethod != "POST" || pedido.Url?.AbsolutePath is not ("/gemini-web/turn" or "/gemini-web/probe")
            || pedido.ContentLength64 is < 0 or > 8_000_000)
        {
            resposta.StatusCode = 403;
            return;
        }

        if (Interlocked.Exchange(ref ocupado, 1) == 1)
        {
            resposta.StatusCode = 409;
            return;
        }
        try
        {
            using var limite = CancellationTokenSource.CreateLinkedTokenSource(encerramento.Token);
            limite.CancelAfter(TimeSpan.FromSeconds(15));
            using var corpo = await JsonDocument.ParseAsync(pedido.InputStream, cancellationToken: limite.Token);
            limite.CancelAfter(Timeout.InfiniteTimeSpan);
            var resultado = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!web.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    resultado.SetResult(pedido.Url!.AbsolutePath.EndsWith("/probe", StringComparison.Ordinal)
                        ? new { conectado = await ConectadoAsync() }
                        : await TurnoAsync(corpo.RootElement, limite.Token));
                }
                catch (Exception ex) { resultado.SetResult(new { erro = ex.Message }); }
            }))
                resultado.SetResult(new { erro = "A janela do Gemini foi fechada." });
            var dados = JsonSerializer.SerializeToUtf8Bytes(await resultado.Task.WaitAsync(encerramento.Token));
            resposta.ContentType = "application/json; charset=utf-8";
            resposta.ContentLength64 = dados.Length;
            await resposta.OutputStream.WriteAsync(dados, encerramento.Token);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or JsonException or HttpListenerException or ObjectDisposedException)
        {
            // Cliente desconectado ou solicitação incompleta.
        }
        finally { Volatile.Write(ref ocupado, 0); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref fechada, 1) != 0) return;
        encerramento.Cancel();
        servidor.Close();
        // Se outra instância abriu uma ponte depois desta, preserve o descritor dela.
        try
        {
            if (File.Exists(arquivo) && File.ReadAllText(arquivo).Contains(token, StringComparison.Ordinal))
                File.Delete(arquivo);
        }
        catch (IOException) { }
    }

    bool NoGemini => web.Source is { Scheme: "https", Host: "gemini.google.com" };

    async Task<JsonElement> LerAsync(string corpo)
    {
        if (!NoGemini) throw new InvalidOperationException("Entre na Conta Google e volte à página do Gemini.");
        var json = await web.CoreWebView2.ExecuteScriptAsync("(() => {" + Dom + corpo + "})()");
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public async Task<bool> ConectadoAsync() => NoGemini && (await LerAsync(
        """
        return !!input() && vis("button,a,[role=button]").some(e => /conta (do )?google|google account/i.test(label(e)))
            && !vis("button,a,[role=button]").some(e => /^(entrar|fazer login|sign in)$/i.test(label(e)));
        """)).ValueKind == JsonValueKind.True;

    public async Task<string?> EmailDaContaAsync()
    {
        if (!NoGemini) return null;
        var email = await LerAsync("""
            const conta = vis('button,a,[role=button]').find(e => /conta (do )?google|google account/i.test(label(e)));
            if (!conta) return null;
            const texto = [label(conta), conta.title, conta.getAttribute('data-email')].filter(Boolean).join(' ');
            return texto.match(/[\w.!#$%&'*+/=?^_`{|}~-]+@[\w.-]+\.[a-z]{2,}/i)?.[0] ?? null;
            """);
        return email.ValueKind == JsonValueKind.String ? email.GetString() : null;
    }

    async Task<object> TurnoAsync(JsonElement pedido, CancellationToken fechamento)
    {
        var id = pedido.GetProperty("session_id").GetString();
        var modelo = pedido.GetProperty("model").GetString();
        var nivel = pedido.GetProperty("effort").GetString();
        var prompt = pedido.GetProperty("prompt").GetString();
        var segundos = Math.Clamp(pedido.GetProperty("timeout_s").GetInt32(), 1, 3600);
        var rotulo = (modelo, nivel) switch
        {
            ("gemini-4-hydrogen", "medium") => @"(?:gemini\s*)?4\s+hydrogen",
            ("gemini-4-hydrogen", "high") => @"(?:gemini\s*)?4\s+hydrogen\s+thinking",
            ("gemini-3.1-pro", "high") => @"(?:gemini\s*)?3\.1\s+pro",
            _ => null,
        };
        if (id is null || !Regex.IsMatch(id, @"^gemini-web-[a-f0-9]{12}$") || string.IsNullOrWhiteSpace(prompt)
            || rotulo is null)
            throw new InvalidOperationException("Pedido inválido para o Gemini Web.");

        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(fechamento);
        prazo.CancelAfter(TimeSpan.FromSeconds(segundos));
        var ct = prazo.Token;
        try
        {
            if (!await ConectadoAsync()) throw new InvalidOperationException("UNAUTHENTICATED 401. Entre na Conta Google no Gemini.");
            if (sessao != id)
            {
                var navegado = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                void Terminou(object? _, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs e) => navegado.TrySetResult(e.IsSuccess);
                web.CoreWebView2.NavigationCompleted += Terminou;
                try
                {
                    web.CoreWebView2.Navigate("https://gemini.google.com/app");
                    if (!await navegado.Task.WaitAsync(ct)) throw new InvalidOperationException("Falha ao abrir uma conversa nova no Gemini.");
                }
                finally { web.CoreWebView2.NavigationCompleted -= Terminou; }
                while (!await ConectadoAsync()) await Task.Delay(350, ct);
                sessao = id;
            }

            await AguardarCliqueAsync("abrir seletor de modo|open mode selector|seletor de modelo|model selector",
                "button,[role=button]", ct);
            await SelecionarAsync(@"(?:^|[\r\n])\s*" + rotulo + @"(?:\s*[·•].*)?\s*(?:$|[\r\n])", ct,
                $"O Gemini Web não oferece {modelo} ({nivel}) para seleção nesta conta.");

            var antes = (await LerAsync("""return document.querySelectorAll("model-response").length;""")).GetInt32();
            var inseriu = await LerAsync("""
                const e = input(); if (!e || (e.innerText || e.value || '').trim()) return false;
                e.focus();
                """ + "const texto = " + JsonSerializer.Serialize(prompt) + ";" + """
                if (e.tagName === 'TEXTAREA') {
                    Object.getOwnPropertyDescriptor(HTMLTextAreaElement.prototype,'value').set.call(e,texto);
                    e.dispatchEvent(new Event('input',{bubbles:true}));
                } else document.execCommand('insertText', false, texto);
                return (e.innerText || e.value || '').trim() === texto.trim();
                """);
            if (!inseriu.GetBoolean()) throw new InvalidOperationException("O Gemini não aceitou o texto completo do turno.");
            await AguardarCliqueAsync("^(enviar( mensagem| comando)?|send( message| prompt)?)$", "button", ct);

            while (true)
            {
                await Task.Delay(600, ct);
                var r = await LerAsync("""
                    const respostas = [...document.querySelectorAll('model-response')];
                    const r = respostas.at(-1);
                    const cota = vis('[role="alert"],[role="status"],mat-snack-bar-container')
                        .some(e => /limite de uso|usage limit|rate limit|flash-lite/i.test(e.innerText));
                    if (cota) return {cota:true};
                    """ + $"if (respostas.length <= {antes}) return null;" + """
                    if (vis('button').some(b => /parar a resposta|stop response|parar de gerar|stop generating/i.test(label(b)))) return null;
                    if (![...r.querySelectorAll('button')].some(b => /boa resposta|good response|resposta ruim|bad response|copiar resposta|copy response/i.test(label(b)))) return null;
                    const e = r.querySelector('message-content');
                    const code = e?.querySelectorAll('pre code');
                    const texto = code?.length === 1 ? code[0].innerText : e?.innerText;
                    const menu = [...r.querySelectorAll('button')].find(b => /mais opções|more options/i.test(label(b)));
                    if (!menu || !texto) return {erro:'O Gemini não disponibilizou os detalhes desta resposta.'};
                    menu.click();
                    return {texto};
                    """);
                if (r.ValueKind == JsonValueKind.Null) continue;
                if (r.TryGetProperty("cota", out _)) return new { erro = "RESOURCE_EXHAUSTED 429: cota esgotada no Gemini Web." };
                if (r.TryGetProperty("erro", out var erro)) return new { erro = erro.GetString() };
                await AguardarCliqueAsync("^(mostrar (as )?etapas (do|de) raciocínio|show thinking steps)$", "button,[role=menuitem]", ct);
                var prova = await LerAsync("""
                    const paines = vis('[role="dialog"],model-response,[data-test-id*="thinking" i]');
                    return paines.flatMap(e => [...e.querySelectorAll('[data-test-id*="model" i],[aria-label]')])
                        .map(e => label(e) || e.innerText || '').filter(t => /modelo usado|model used|gerado com|generated with|powered by/i.test(t));
                    """);
                var observado = string.Join(" ", prova.EnumerateArray().Select(e => e.GetString()));
                if (observado.Contains("flash-lite", StringComparison.OrdinalIgnoreCase))
                    return new { erro = "RESOURCE_EXHAUSTED 429: o Gemini respondeu com Flash-Lite." };
                var sufixo = nivel == "medium" ? @"(?![ \t]+thinking\b)" : "";
                if (!Regex.IsMatch(observado, $@"\b{rotulo}\b{sufixo}", RegexOptions.IgnoreCase))
                    throw new InvalidOperationException("O modo efetivamente usado pelo Gemini não pôde ser confirmado.");
                return new { modelo, nivel, texto = r.GetProperty("texto").GetString() };
            }
        }
        catch (OperationCanceledException) when (!fechamento.IsCancellationRequested)
        {
            sessao = null;
            throw new InvalidOperationException("O Gemini Web não confirmou o turno dentro do prazo.");
        }
    }

    Task SelecionarAsync(string rotulo, CancellationToken ct, string? mensagem = null) =>
        AguardarCliqueAsync(rotulo, "[role=menuitem],[role=menuitemradio],[role=option],button", ct, mensagem);

    async Task AguardarCliqueAsync(string rotulo, string seletor, CancellationToken ct, string? mensagem = null)
    {
        using var espera = CancellationTokenSource.CreateLinkedTokenSource(ct);
        espera.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            while (true)
            {
                var encontrado = await LerAsync(
                    "const p = new RegExp(" + JsonSerializer.Serialize(rotulo) + ", 'i');" +
                    "const itens = vis(" + JsonSerializer.Serialize(seletor) + ").filter(e => !e.disabled && e.getAttribute('aria-disabled') !== 'true' && (p.test(label(e)) || p.test((e.innerText || '').trim())));" +
                    "const e = itens.find(i => !itens.some(f => f !== i && i.contains(f))); if (!e) return false; e.click(); return true;");
                if (encontrado.GetBoolean()) return;
                await Task.Delay(200, espera.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvalidOperationException(mensagem ?? "O comando ou nível esperado está indisponível no Gemini Web.");
        }
    }

    const string Dom = """
        const vis = s => [...document.querySelectorAll(s)].filter(e => e.getClientRects().length);
        const label = e => (e.getAttribute('aria-label') || e.title || e.innerText || '').trim();
        const input = () => vis('rich-textarea .ql-editor,[contenteditable="true"][role="textbox"],textarea')[0];
        """;
}
