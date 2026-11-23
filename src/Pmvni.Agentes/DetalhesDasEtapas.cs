using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Uma das frentes paralelas da etapa Análise do fluxo linear do kit (<c>fluxo_linear.BLOCOS</c>: seções 1–9, 10–18 e 19–28).
/// <see cref="Prontas"/>: os fragmentos <c>secao-NN.json</c> do intervalo já gravados; <see cref="Concluida"/>: o recibo <c>concluida.json</c> da frente
/// existe (ou o executor já registrou a frente em <c>sessao_codex.analyze.frentes</c>).</summary>
public sealed record FrenteAnalise(int Inicio, int Fim, int Prontas, bool Iniciada, bool Concluida)
{
    public string? Atividade { get; init; }
    public int Total => Fim - Inicio + 1;
}

/// <summary>Uma objeção que bloqueia a entrega, na etapa que a levantou (<see cref="Etapa"/>).</summary>
public sealed record ObjecaoPendente(string Etapa, string Motivo, string? Arquivo);

/// <summary>Quem executa a etapa: <see cref="Programa"/> (local, sem IA, pelo <c>executor</c> do <c>etapas.json</c> ou do registro) ou o modelo e o esforço
/// que o executor do kit gravou no <c>engine_state.json</c>. Modelo e esforço null = ainda não gravados (nada é inventado).</summary>
public sealed record DetalheEtapa(string Etapa, bool Programa, string? Modelo, string? Esforco);

/// <summary>O que o Monitor mostra em cada etapa além da situação (fluxo linear do kit, 2026-11-22): modelo e esforço, as três frentes da Análise e as
/// objeções pendentes. Tudo vem dos arquivos que o executor grava na pasta de controle; o Editor só lê.</summary>
public sealed record DetalhesDasEtapas(IReadOnlyDictionary<string, DetalheEtapa> Etapas, IReadOnlyList<FrenteAnalise> Frentes,
    IReadOnlyList<ObjecaoPendente> Objecoes)
{
    public IReadOnlyList<string> Plano { get; init; } = [];
    public IReadOnlyList<string> Validacoes { get; init; } = [];
    public IReadOnlyDictionary<string, string> Registros { get; init; } = new Dictionary<string, string>();

    public static DetalhesDasEtapas Vazio { get; } = new(new Dictionary<string, DetalheEtapa>(), [], []);

    /// <summary>Os intervalos das frentes (os mesmos de <c>fluxo_linear.BLOCOS</c> do kit).</summary>
    public static readonly IReadOnlyList<(int Inicio, int Fim)> Blocos = [(1, 9), (10, 18), (19, 28)];

    public bool TemObjecoes => Objecoes.Count > 0;

    /// <summary>"gpt-6.2-sol · esforço médio", "Programa · sem consumo de IA" ou null quando o kit ainda não gravou nada da etapa.</summary>
    public string? TextoExecutor(string etapa)
    {
        if (!Etapas.TryGetValue(etapa, out var d)) return null;
        if (d.Programa) return "Programa · sem consumo de IA";
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(d.Modelo)) partes.Add(d.Modelo!.Trim());
        if (NomeDoEsforco(d.Esforco) is { } e) partes.Add("esforço " + e);
        return partes.Count == 0 ? null : string.Join(" · ", partes);
    }

    /// <summary>low/medium/high (Codex, Claude, Gemini) ou baixo/medio/alto (tabela do kit) em português; null se vazio ou desconhecido.</summary>
    public static string? NomeDoEsforco(string? esforco) => esforco?.Trim().ToLowerInvariant() switch
    {
        "low" or "baixo" => "baixo",
        "medium" or "medio" or "médio" => "médio",
        "high" or "alto" => "alto",
        "xhigh" or "max" or "maximo" or "máximo" => "máximo",
        _ => null,
    };

    /// <summary>Lê a pasta de controle (<c>.pmvni-ai\analise</c>, <c>analise-pro</c> ou <c>analise-google</c>) e o <c>etapas.json</c> do kit instalado.
    /// <paramref name="desdeUtc"/>: o que foi gravado antes (a análise anterior) não conta. Nunca lança por arquivo ausente ou inválido.</summary>
    public static DetalhesDasEtapas Ler(string pastaControle, string? etapasJson = null, DateTime desdeUtc = default)
    {
        var etapas = new Dictionary<string, DetalheEtapa>(StringComparer.Ordinal);
        var registros = new Dictionary<string, string>(StringComparer.Ordinal);
        var programas = EtapasDePrograma(etapasJson);
        var estado = ArquivoJsonDoKit.Ler(Path.Combine(pastaControle, "engine_state.json"), desdeUtc);
        JsonElement? stages = estado is { ValueKind: JsonValueKind.Object } e && e.TryGetProperty("stages", out var s) && s.ValueKind == JsonValueKind.Object ? s : null;
        var sessoes = UsoDasIas.Objeto(estado, "sessao_codex");

        foreach (var id in programas) etapas[id] = new DetalheEtapa(id, true, null, null);
        var nomes = new HashSet<string>(StringComparer.Ordinal);
        if (stages is { } st) foreach (var p in st.EnumerateObject()) nomes.Add(p.Name);
        if (sessoes is { } ss) foreach (var p in ss.EnumerateObject()) nomes.Add(p.Name);
        foreach (var id in nomes)
        {
            var registro = UsoDasIas.Objeto(stages, id);
            var sessao = UsoDasIas.Objeto(sessoes, id);
            var linhas = new List<string>();
            if (registro is { } reg)
            {
                foreach (var chave in new[] { "note", "error" })
                    if (ProgressoLocal.Texto(reg, chave) is { Length: > 0 } texto) linhas.Add(texto);
                if (reg.TryGetProperty("evidence", out var evidencias) && evidencias.ValueKind == JsonValueKind.Array)
                    foreach (var ev in evidencias.EnumerateArray().Where(ev => ev.ValueKind == JsonValueKind.Object))
                        if (ProgressoLocal.Texto(ev, "path") is { Length: > 0 } arquivo)
                            linhas.Add("Evidência registrada · " + Path.GetFileName(arquivo));
            }
            if (sessao is { } sc && sc.TryGetProperty("eventos", out var eventos) && eventos.ValueKind == JsonValueKind.Array)
                foreach (var ev in eventos.EnumerateArray().Where(ev => ev.ValueKind == JsonValueKind.Object).TakeLast(4))
                    if (ProgressoLocal.Texto(ev, "evento") is { Length: > 0 } evento)
                        linhas.Add(string.Join(" · ", new[] { evento.Replace('_', ' '), ProgressoLocal.Texto(ev, "detalhe") }
                            .Where(t => !string.IsNullOrWhiteSpace(t))));
            if (sessao is { } rep && ProgressoLocal.Inteiro(rep, "reparos_validador") is > 0 and var total)
            {
                linhas.Add($"Sessões de reparo · {total} · {ProgressoLocal.Texto(rep, "modelo_reparo")} {ProgressoLocal.Texto(rep, "esforco_reparo")}".TrimEnd());
                if (ProgressoLocal.Texto(rep, "correcao_validador") is { Length: > 0 } motivo) linhas.Add(motivo);
            }
            if (linhas.Count != 0) registros[id] = string.Join(Environment.NewLine, linhas.Distinct());
            var programa = programas.Contains(id)
                || registro is { } r0 && (string.Equals(ProgressoLocal.Texto(r0, "executor"), UsoDasIas.Programa, StringComparison.OrdinalIgnoreCase)
                                           || string.Equals(ProgressoLocal.Texto(r0, "engine"), UsoDasIas.Programa, StringComparison.OrdinalIgnoreCase));
            if (programa) { etapas[id] = new DetalheEtapa(id, true, null, null); continue; }
            var frente = PrimeiraFrente(sessao);
            string? modelo = null, esforco = null;
            if (sessao is { } se) { modelo = ProgressoLocal.Texto(se, "modelo"); esforco = ProgressoLocal.Texto(se, "esforco") ?? ProgressoLocal.Texto(se, "nivel"); }
            if (frente is { } f) { modelo ??= ProgressoLocal.Texto(f, "modelo"); esforco ??= ProgressoLocal.Texto(f, "esforco"); }
            if (registro is { } r)
            {
                modelo ??= ProgressoLocal.Texto(r, "modelo_observado") ?? ProgressoLocal.Texto(r, "requested_model") ?? ProgressoLocal.Texto(r, "modelo_exigido")
                    ?? ProgressoLocal.Texto(r, "model");
                esforco ??= ProgressoLocal.Texto(r, "requested_effort") ?? ProgressoLocal.Texto(r, "effort") ?? ProgressoLocal.Texto(r, "nivel_usado");
            }
            if (modelo is null && esforco is null) continue;
            etapas[id] = new DetalheEtapa(id, false, modelo, esforco);
        }

        return new DetalhesDasEtapas(etapas, LerFrentes(pastaControle, sessoes, desdeUtc,
            UsoDasIas.Objeto(stages, "analyze") is { } etapaRegistrada ? ProgressoLocal.Texto(etapaRegistrada, "status") : null), LerObjecoes(estado, stages))
        {
            Registros = registros,
            Plano = LerPlano(pastaControle, desdeUtc),
            Validacoes = LerValidacoes(pastaControle, desdeUtc)
        };
    }

    static IReadOnlyList<string> LerPlano(string controle, DateTime desde)
    {
        var plano = ArquivoJsonDoKit.Ler(Path.Combine(controle, "plano_execucao.json"), desde);
        if (plano is not { ValueKind: JsonValueKind.Object } p || !p.TryGetProperty("etapas", out var etapas)
            || etapas.ValueKind != JsonValueKind.Array) return [];
        return etapas.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object)
            .Select(x => ProgressoLocal.Texto(x, "id")).OfType<string>().ToList();
    }

    static IReadOnlyList<string> LerValidacoes(string controle, DateTime desde)
    {
        var linhas = new List<string>();
        foreach (var nome in new[] { "avisos", "consertos", "consertos-publicacao" })
        {
            var json = ArquivoJsonDoKit.Ler(Path.Combine(controle, "validadores", nome + ".json"), desde);
            if (json is not { ValueKind: JsonValueKind.Array } itens) continue;
            foreach (var x in itens.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Object))
            {
                if (nome == "avisos" && ProgressoLocal.Texto(x, "regra") is { } regra)
                    linhas.Add($"Aviso · {regra} · {ProgressoLocal.Texto(x, "onde")} · {ProgressoLocal.Texto(x, "texto")}");
                else if ((ProgressoLocal.Texto(x, "campo") ?? ProgressoLocal.Texto(x, "onde")) is { } campo && x.TryGetProperty("antes", out var antes)
                    && x.TryGetProperty("depois", out var depois)) linhas.Add($"Conserto · {campo} · {antes} → {depois}");
            }
        }
        return linhas;
    }

    /// <summary>As etapas com <c>"executor": "programa"</c> no <c>etapas.json</c> do kit (vazio sem o arquivo).</summary>
    static HashSet<string> EtapasDePrograma(string? etapasJson)
    {
        var saida = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(etapasJson) || ArquivoJsonDoKit.Ler(etapasJson) is not { ValueKind: JsonValueKind.Object } raiz
            || !raiz.TryGetProperty("etapas", out var etapas) || etapas.ValueKind != JsonValueKind.Object) return saida;
        foreach (var p in etapas.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Object && ProgressoLocal.Texto(p.Value, "executor") == UsoDasIas.Programa) saida.Add(p.Name);
        return saida;
    }

    static JsonElement? PrimeiraFrente(JsonElement? sessao) =>
        sessao is { } s && s.TryGetProperty("frentes", out var lista) && lista.ValueKind == JsonValueKind.Array
            ? lista.EnumerateArray().Where(f => f.ValueKind == JsonValueKind.Object).Select(f => (JsonElement?)f).FirstOrDefault() : null;

    /// <summary>As frentes: a pasta <c>runs\secoes-&lt;chave&gt;</c> mais recente que o executor gravou nesta análise (o <c>partes_analise.json</c> aponta a
    /// definitiva depois da montagem), com <c>frente-NN\pedido.txt</c> (iniciada), <c>frente-NN\concluida.json</c> (concluída) e os <c>secao-NN.json</c>
    /// (prontas). Sem a pasta, só o registro final do executor (<c>sessao_codex.analyze.frentes</c>). Sem nada disso, vazio (o Monitor não mostra).</summary>
    internal static IReadOnlyList<FrenteAnalise> LerFrentes(string pastaControle, JsonElement? sessoes, DateTime desdeUtc, string? situacao = null)
    {
        var pasta = PastaDasPartes(pastaControle, desdeUtc);
        if (pasta is not null)
        {
            var lista = new List<FrenteAnalise>();
            foreach (var (inicio, fim) in Blocos)
            {
                var frente = Path.Combine(pasta, $"frente-{inicio:00}");
                var prontas = Enumerable.Range(inicio, fim - inicio + 1).Count(n => File.Exists(Path.Combine(pasta, $"secao-{n:00}.json")));
                var concluida = File.Exists(Path.Combine(frente, "concluida.json"));
                var iniciada = concluida || File.Exists(Path.Combine(frente, "pedido.txt")) || prontas > 0;
                string? atividade = null;
                if (ArquivoJsonDoKit.Ler(Path.Combine(frente, "atividade.json"), desdeUtc) is { ValueKind: JsonValueKind.Object } a
                    && ProgressoLocal.Inteiro(a, "inicio") == inicio && ProgressoLocal.Inteiro(a, "fim") == fim
                    && ProgressoLocal.Texto(a, "status") is { } status && status is "em_andamento" or "concluida" or "interrompida"
                    && (status != "em_andamento" || (!concluida && situacao is null or "RUNNING")))
                {
                    iniciada = true;
                    var estado = status.Replace('_', ' ');
                    atividade = string.Join(" · ", new[] { estado, ProgressoLocal.Texto(a, "descricao") }.Where(t => !string.IsNullOrWhiteSpace(t)));
                }
                lista.Add(new FrenteAnalise(inicio, fim, concluida ? fim - inicio + 1 : prontas, iniciada, concluida) { Atividade = atividade });
            }
            if (lista.Any(f => f.Iniciada)) return lista;
        }
        if (UsoDasIas.Objeto(sessoes, "analyze") is { } analise && analise.TryGetProperty("frentes", out var registradas)
            && registradas.ValueKind == JsonValueKind.Array)
        {
            var lista = new List<FrenteAnalise>();
            foreach (var f in registradas.EnumerateArray())
                if (f.ValueKind == JsonValueKind.Object && f.TryGetProperty("secoes", out var secoes) && secoes.ValueKind == JsonValueKind.Array
                    && secoes.GetArrayLength() == 2 && secoes[0].ValueKind == JsonValueKind.Number && secoes[1].ValueKind == JsonValueKind.Number
                    && secoes[0].TryGetInt32(out var a) && secoes[1].TryGetInt32(out var b) && a >= 1 && b >= a)
                    lista.Add(new FrenteAnalise(a, b, b - a + 1, true, true));
            return lista;
        }
        return [];
    }

    internal static string? PastaAtualDasFrentes(string pastaControle, DateTime desdeUtc)
    {
        try
        {
            var raiz = Path.GetFullPath(Path.Combine(pastaControle, "runs"));
            if (ArquivoJsonDoKit.Ler(Path.Combine(pastaControle, "frentes_ativas.json"), desdeUtc) is { ValueKind: JsonValueKind.Object } atual
                && ProgressoLocal.Texto(atual, "pasta") is { } pasta && Path.IsPathFullyQualified(pasta)
                && string.Equals(Path.GetDirectoryName(Path.GetFullPath(pasta)), raiz, StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(pasta)) return pasta;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        return null;
    }

    static string? PastaDasPartes(string pastaControle, DateTime desdeUtc)
    {
        try
        {
            if (PastaAtualDasFrentes(pastaControle, desdeUtc) is { } atual) return atual;
            if (ArquivoJsonDoKit.Ler(Path.Combine(pastaControle, "partes_analise.json"), desdeUtc) is { ValueKind: JsonValueKind.Object } partes
                && ProgressoLocal.Texto(partes, "pasta") is { Length: > 0 } definitiva && Directory.Exists(definitiva)) return definitiva;
            var runs = new DirectoryInfo(Path.Combine(pastaControle, "runs"));
            if (!runs.Exists) return null;
            return runs.EnumerateDirectories("secoes-*").Where(d => d.LastWriteTimeUtc >= desdeUtc)
                .OrderByDescending(d => d.LastWriteTimeUtc).FirstOrDefault()?.FullName;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
    }

    /// <summary>As objeções pendentes, na etapa que as levantou: <c>objecoes_sem_ciclo</c> (o fluxo linear bloqueia sem volta), <c>objecoes_pendentes</c>
    /// (<c>de</c>) e as etapas com <c>status: OBJECTIONS</c>. Uma por etapa.</summary>
    static IReadOnlyList<ObjecaoPendente> LerObjecoes(JsonElement? estado, JsonElement? stages)
    {
        var lista = new List<ObjecaoPendente>();
        void Somar(string? etapa, string? motivo, string? arquivo)
        {
            if (string.IsNullOrWhiteSpace(etapa) || lista.Any(o => o.Etapa == etapa)) return;
            lista.Add(new ObjecaoPendente(etapa!, string.IsNullOrWhiteSpace(motivo) ? "Objeção registrada pelo kit." : motivo!.Trim(), arquivo));
        }
        if (UsoDasIas.Objeto(estado, "objecoes_sem_ciclo") is { } semCiclo)
            Somar(ProgressoLocal.Texto(semCiclo, "etapa"), ProgressoLocal.Texto(semCiclo, "motivo"), ProgressoLocal.Texto(semCiclo, "arquivo"));
        if (UsoDasIas.Objeto(estado, "objecoes_pendentes") is { } pendente)
            Somar(ProgressoLocal.Texto(pendente, "de"), ProgressoLocal.Texto(pendente, "motivo"), ProgressoLocal.Texto(pendente, "arquivo"));
        if (stages is { } st)
            foreach (var p in st.EnumerateObject())
                if (p.Value.ValueKind == JsonValueKind.Object && ProgressoLocal.Texto(p.Value, "status") == "OBJECTIONS")
                    Somar(p.Name, ProgressoLocal.Texto(p.Value, "objecoes") ?? ProgressoLocal.Texto(p.Value, "note") ?? ProgressoLocal.Texto(p.Value, "error"), null);
        return lista;
    }
}

/// <summary>"Remontar relatório (teste)": o <c>producao_teste.py</c> do kit produz e revisa o PDF numa pasta isolada a partir da cópia certificada pela
/// auditoria técnica, sem tocar na análise. Só vale com <c>stages.technical_audit</c> <c>EXECUTED</c> e <c>copia_certificada</c> dentro de
/// <c>&lt;controle&gt;\certificados</c> (as mesmas condições que o script confere antes de rodar).</summary>
public static class ProducaoTeste
{
    public const string Script = "producao_teste.py";

    /// <summary>A rota do kit (<c>--rota</c>) de cada provedor.</summary>
    public static string Rota(Provedor p) => Localizacao.RotaDoControlador(p).Pasta;

    /// <summary>A cópia certificada existe para a análise desta pasta de controle.</summary>
    public static bool TemCopiaCertificada(string pastaControle)
    {
        if (ArquivoJsonDoKit.Ler(Path.Combine(pastaControle, "engine_state.json")) is not { ValueKind: JsonValueKind.Object } estado
            || UsoDasIas.Objeto(UsoDasIas.Objeto(estado, "stages"), "technical_audit") is not { } auditoria
            || ProgressoLocal.Texto(auditoria, "status") != "EXECUTED"
            || ProgressoLocal.Texto(auditoria, "copia_certificada") is not { Length: > 0 } copia) return false;
        try
        {
            var certificados = Path.GetFullPath(Path.Combine(pastaControle, "certificados")).TrimEnd('\\');
            var pasta = Path.GetFullPath(copia).TrimEnd('\\');
            return string.Equals(Path.GetDirectoryName(pasta), certificados, StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(pasta, "certificado.json"));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>O caminho do PDF que o script imprime na última linha não vazia da saída; null se não houver.</summary>
    public static string? PdfDaSaida(string saida) =>
        saida.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) is { } ultima
        && ultima.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) ? ultima : null;
}
