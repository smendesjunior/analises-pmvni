using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>A IA no Novo despacho (correções 2026-11-15): resume as exigências vindas do relatório (só o que não atende) e aplica instruções de
/// modificação escritas na caixa acima da prévia. Usa o Claude Code (<c>claude -p</c>) ou, sem ele, o Codex (<c>codex exec</c>), com a
/// conta já logada no programa; nada é gravado: a resposta só troca as exigências do rascunho.</summary>
internal static class IaDespacho
{
    /// <summary>As regras de redação do plugin despacho do kit (SKILL.md), resumidas.</summary>
    internal const string Regras =
        "Você redige exigências de despacho de correção da Secretaria Municipal de Obras de Venda Nova do Imigrante/ES. " +
        "Texto curto, técnico e neutro, em português do Brasil. Só entram itens NÃO ATENDE, FALTA COMPROVAR e INFORMAÇÕES DIVERGENTES; " +
        "itens que atendem nunca aparecem. Cada exigência diz o problema, o local, a correção e o fundamento confirmado, nessa ordem " +
        "(primeiro o problema, depois o que fazer), sem repetir o texto técnico inteiro do relatório, sem hipóteses nem duplicações. " +
        "Não invente fundamento: mantenha o que veio ou deixe vazio.";

    internal const string PedidoResumo =
        "Resuma cada exigência de forma objetiva para o despacho, mantendo a ordem e os fundamentos; junte as duplicadas.";

    /// <summary>Monta o pedido: regras, rascunho atual em JSON e a instrução; pede só JSON de volta.</summary>
    internal static string Prompt(RascunhoDespacho r, string instrucao)
    {
        var atual = new JsonObject
        {
            ["protocolo"] = r.Protocolo,
            ["requerente"] = r.Requerente,
            ["assunto"] = r.Assunto,
            ["exigencias"] = new JsonArray(r.Pendencias.Where(p => p.Incluida)
                .Select(p => (JsonNode)new JsonObject { ["grupo"] = p.Grupo, ["texto"] = p.Texto, ["fundamento"] = p.Fundamento }).ToArray()),
        };
        return Regras + "\n\nDespacho atual (JSON):\n" + atual.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) +
               "\n\nInstrução: " + instrucao +
               "\n\nResponda SOMENTE com um JSON no mesmo formato (protocolo, requerente, assunto, exigencias[{grupo, texto, fundamento}]), sem comentários.";
    }

    internal sealed record Resposta(string? Protocolo, string? Requerente, string? Assunto, IReadOnlyList<(string Grupo, string Texto, string Fundamento)> Exigencias);

    /// <summary>Lê a resposta do despacho. Sem a lista "exigencias" (ou com outro formato) é resposta inválida: null, e nada muda —
    /// nunca uma lista vazia que apagaria as exigências (auditoria 2026-11-16).</summary>
    internal static Resposta? Ler(string saida)
    {
        if (RespostaJson.Objeto(saida, o => o["exigencias"] is JsonArray) is not { } o) return null;
        static string? T(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        var lista = ((JsonArray)o["exigencias"]!)
            .OfType<JsonObject>()
            .Select(e => (T(e["grupo"]) ?? "", T(e["texto"]) ?? "", T(e["fundamento"]) ?? ""))
            .Where(e => e.Item2.Trim().Length > 0)
            .ToList();
        return new Resposta(T(o["protocolo"]), T(o["requerente"]), T(o["assunto"]), lista);
    }

    /// <summary>Tempo máximo de uma pergunta à IA; passado isso, o programa é encerrado e o usuário recebe a mensagem.</summary>
    internal static readonly TimeSpan Limite = TimeSpan.FromMinutes(10);

    /// <summary>Ferramentas que o Claude nunca usa nestas perguntas (só lê): valem mesmo que a configuração do usuário as permita.</summary>
    const string FerramentasProibidas = "Bash Edit Write MultiEdit NotebookEdit WebFetch WebSearch Task";

    /// <summary>Opções de uma pergunta à IA (revisão do relatório, 2026-11-16). <see cref="PreferirCodex"/>: usa o Codex quando ele existe
    /// (o padrão é o Claude Code primeiro); <see cref="ArgumentosCodex"/>: modelo e esforço (<see cref="ModeloIaAjuste.ArgumentosCodex"/>);
    /// <see cref="SemArquivos"/>: a IA responde só com o que está no pedido, numa pasta vazia, sem ferramentas de leitura e sem guardar a
    /// conversa no Codex (<c>--ephemeral</c>); <see cref="Limite"/>: tempo máximo (null = <see cref="IaDespacho.Limite"/>).
    /// Modelos de IA (2026-11-17): <see cref="ArgumentosClaude"/>: modelo e nível no Claude Code (<c>--model</c>/<c>--effort</c>,
    /// <see cref="EscolhaIa.Argumentos"/>); <see cref="Ambiente"/>: ajusta as variáveis do processo filho (tira as chaves herdadas e, nas
    /// APIs, põe só a chave da fonte escolhida — <c>AmbienteFilho.Aplicar</c>).</summary>
    internal sealed record OpcoesIa(bool PreferirCodex = false, IReadOnlyList<string>? ArgumentosCodex = null, bool SemArquivos = false, TimeSpan? Limite = null,
        IReadOnlyList<string>? ArgumentosClaude = null, Action<IDictionary<string, string?>>? Ambiente = null);

    /// <summary>A linha de comando de uma pergunta: o programa, os argumentos (sempre separados) e, no Codex, o arquivo da resposta final.</summary>
    internal sealed record ComandoIa(string Programa, IReadOnlyList<string> Argumentos, string? UltimaMensagem, bool Codex);

    /// <summary>Monta a linha de comando. Sem <paramref name="opcoes"/>: o Claude Code se existir, senão o Codex, como sempre (Novo despacho).
    /// Sem programa nenhum: <see cref="InvalidOperationException"/>.</summary>
    internal static ComandoIa Montar(string? claude, string? codex, string dir, bool comPasta, IReadOnlyList<string> extras, OpcoesIa? opcoes = null)
    {
        var temClaude = !string.IsNullOrEmpty(claude);
        var temCodex = !string.IsNullOrEmpty(codex);
        var usarCodex = temCodex && (!temClaude || opcoes?.PreferirCodex == true);
        var semArquivos = opcoes?.SemArquivos == true;
        var args = new List<string>();
        if (usarCodex)
        {
            var ultima = Path.Combine(Path.GetTempPath(), "pmvni-ia-" + Guid.NewGuid().ToString("N") + ".txt");
            args.AddRange(["exec", "--skip-git-repo-check", "--sandbox", "read-only"]);
            if (semArquivos) args.Add("--ephemeral");
            if (opcoes?.ArgumentosCodex is { } modelo) args.AddRange(modelo);
            args.AddRange(["--cd", dir, "--output-last-message", ultima, "-"]);
            return new ComandoIa(codex!, args, ultima, true);
        }
        if (!temClaude)
            throw new InvalidOperationException("Nenhum programa de IA encontrado. Instale e entre no Claude Code ou no Codex em Ajustes › Inteligência artificial.");
        args.AddRange(["-p", "--output-format", "text", "--permission-mode", "default", "--disallowedTools",
            semArquivos ? FerramentasProibidas + " Read Grep Glob" : FerramentasProibidas]);
        if (opcoes?.ArgumentosClaude is { } escolha) args.AddRange(escolha);
        if (comPasta && !semArquivos)
        {
            args.Add("--allowedTools");
            args.Add("Read Grep Glob");
            foreach (var d in extras) { args.Add("--add-dir"); args.Add(d); }
        }
        return new ComandoIa(claude!, args, null, false);
    }

    /// <summary>A pasta vazia onde rodam as perguntas sem arquivos (a IA não tem o que explorar).</summary>
    internal static string PastaVazia()
    {
        var d = Path.Combine(Path.GetTempPath(), "pmvni-ia-sem-arquivos");
        Directory.CreateDirectory(d);
        return d;
    }

    /// <summary>Roda o programa da IA com o pedido no stdin e devolve a resposta. Sem programa: <see cref="InvalidOperationException"/>.
    /// Auditoria 2026-11-16: tempo-limite (<see cref="Limite"/>), o processo entra no job do editor (morre se o editor fechar) e é encerrado
    /// em qualquer saída antecipada (inclusive o Cancelar: <paramref name="ct"/>); o Claude roda sem ferramentas de escrita nem de rede; o
    /// Codex em sandbox só leitura, com a resposta final lida de --output-last-message (sem o cabeçalho que ele imprime).</summary>
    internal static async Task<string> PerguntarAsync(string? claude, string? codex, string prompt, CancellationToken ct,
        string? pasta = null, IReadOnlyList<string>? pastasExtras = null, OpcoesIa? opcoes = null)
    {
        var semArquivos = opcoes?.SemArquivos == true;
        var dir = semArquivos ? PastaVazia() : pasta is not null && Directory.Exists(pasta) ? pasta : Path.GetTempPath();
        var extras = semArquivos ? [] : (pastasExtras ?? []).Where(Directory.Exists).ToList();
        if (extras.Count > 0)
            prompt += "\n\nPastas que você pode ler (só leitura), além da pasta de trabalho:\n" + string.Join("\n", extras.Select(e => "- " + e));
        var comando = Montar(claude, codex, dir, pasta is not null, extras, opcoes);
        var ultima = comando.UltimaMensagem;
        var tempo = opcoes?.Limite ?? Limite;
        var si = new ProcessStartInfo(comando.Programa);
        foreach (var a in comando.Argumentos) si.ArgumentList.Add(a);
        si.RedirectStandardInput = si.RedirectStandardOutput = si.RedirectStandardError = true;
        si.UseShellExecute = false;
        si.CreateNoWindow = true;
        si.StandardInputEncoding = si.StandardOutputEncoding = si.StandardErrorEncoding = new UTF8Encoding(false);
        si.WorkingDirectory = dir;
        opcoes?.Ambiente?.Invoke(si.Environment);

        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(tempo);
        using var p = Process.Start(si) ?? throw new InvalidOperationException("O programa da IA não abriu.");
        try { Pmvni.Agentes.TarefaDoEditor.Padrao.Atribuir(p); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        var saida = p.StandardOutput.ReadToEndAsync(limite.Token);
        var erro = p.StandardError.ReadToEndAsync(limite.Token);
        try
        {
            // O programa pode fechar a entrada antes de ler tudo (sem login, executável quebrado): segue para ler o motivo.
            try
            {
                await p.StandardInput.WriteAsync(prompt.AsMemory(), limite.Token);
                p.StandardInput.Close();
            }
            catch (IOException) { }
            try { await p.WaitForExitAsync(limite.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new InvalidOperationException($"A IA não respondeu em {tempo.TotalMinutes:0} minutos e foi interrompida. Tente de novo; se repetir, confira a conta em Ajustes › Inteligência artificial.");
            }
            string Ler(Task<string> t) => t.IsCompletedSuccessfully ? t.Result : "";
            await Task.WhenAny(Task.WhenAll(saida, erro), Task.Delay(TimeSpan.FromSeconds(3), CancellationToken.None));
            if (p.ExitCode != 0)
            {
                // O Claude Code em -p costuma escrever o motivo na saída comum, não na de erro: mostra o que vier, senão o código.
                var motivo = Ler(erro).Trim();
                if (motivo.Length == 0) motivo = Ler(saida).Trim();
                if (motivo.Length > 600) motivo = "…" + motivo[^600..];
                if (motivo.Length == 0) motivo = $"o programa terminou com o código {p.ExitCode} sem explicar o motivo ({Path.GetFileName(si.FileName)}). Confira se você entrou na conta em Ajustes › Inteligência artificial.";
                throw new InvalidOperationException("A IA respondeu com erro: " + motivo);
            }
            if (ultima is not null && File.Exists(ultima))
            {
                var final = await File.ReadAllTextAsync(ultima, Encoding.UTF8, CancellationToken.None);
                if (final.Trim().Length > 0) return final;
            }
            return Ler(saida);
        }
        finally
        {
            try { if (!p.HasExited) p.Kill(true); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            if (ultima is not null) try { File.Delete(ultima); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}

/// <summary>Acha o objeto JSON da resposta da IA (auditoria 2026-11-16): primeiro os blocos cercados por ```json, depois cada objeto
/// completo do texto; fica com o ÚLTIMO que o critério aceita (a IA às vezes mostra um rascunho antes do final). Chave repetida, JSON
/// quebrado ou texto com chaves soltas não derrubam nada: o candidato é só ignorado.</summary>
internal static class RespostaJson
{
    internal static JsonObject? Objeto(string saida, Func<JsonObject, bool> aceita)
    {
        JsonObject? achado = null;
        foreach (var candidato in Candidatos(saida))
        {
            try
            {
                if (JsonNode.Parse(candidato) is JsonObject o && aceita(o)) achado = o;
            }
            catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or FormatException) { }
        }
        return achado;
    }

    static IEnumerable<string> Candidatos(string s)
    {
        // Cada objeto de nível mais alto, achado pelo equilíbrio das chaves fora de strings.
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '{') continue;
            var nivel = 0;
            var emTexto = false;
            for (var j = i; j < s.Length; j++)
            {
                var c = s[j];
                if (emTexto)
                {
                    if (c == '\\') j++;
                    else if (c == '"') emTexto = false;
                    continue;
                }
                if (c == '"') emTexto = true;
                else if (c == '{') nivel++;
                else if (c == '}' && --nivel == 0)
                {
                    yield return s[i..(j + 1)];
                    i = j;
                    break;
                }
            }
        }
    }
}
