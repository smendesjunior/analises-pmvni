using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>Estado da conferência local apresentado no Monitor e na reabertura pelo histórico.</summary>
internal sealed record ConferenciaNumerica(string Fase, string Resumo, string Detalhes)
{
    internal sealed record Declaracao(string Rotulo, string Valor, string Fonte, string Revisao,
        string Contexto = "", string Ressalva = "", string Campo = "");
    internal sealed record Limitacao(string Fonte, string Motivo);
    internal sealed record Evidencia(string Indice, string Parcela, string Rotulo, string Valor, string Peca, int Pagina,
        string Arquivo, string Sha256, string Recorte, string Sha256Recorte, string Regiao)
    {
        internal string Titulo => $"{Indice.ToUpperInvariant()} · {Parcela} · {Rotulo} {Valor} · {Peca}, p. {Pagina}";
    }
    internal IReadOnlyList<Evidencia> Evidencias { get; init; } = [];
    internal IReadOnlyList<Declaracao> Declaracoes { get; init; } = [];
    internal bool TemInventario { get; init; }
    internal IReadOnlyList<Declaracao> Inventario { get; init; } = [];
    internal IReadOnlyList<Limitacao> Limitacoes { get; init; } = [];

    public bool Equals(ConferenciaNumerica? other) => other is not null &&
        Fase == other.Fase && Resumo == other.Resumo && Detalhes == other.Detalhes && TemInventario == other.TemInventario &&
        Declaracoes.SequenceEqual(other.Declaracoes) && Evidencias.SequenceEqual(other.Evidencias) &&
        Inventario.SequenceEqual(other.Inventario) && Limitacoes.SequenceEqual(other.Limitacoes);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fase);
        hash.Add(Resumo);
        hash.Add(Detalhes);
        foreach (var declaracao in Declaracoes) hash.Add(declaracao);
        foreach (var evidencia in Evidencias) hash.Add(evidencia);
        hash.Add(TemInventario);
        foreach (var declaracao in Inventario) hash.Add(declaracao);
        foreach (var limitacao in Limitacoes) hash.Add(limitacao);
        return hash.ToHashCode();
    }
    internal const string ArquivoControle = "CONFERENCIA_INDICES.JSON";
    internal const string ArquivoHistorico = "CONFERENCIA-NUMERICA.JSON";
    internal string Titulo => "Conferência numérica local · " + (Fase == "preparo" ? "Preparo" : "Montagem");

    internal static ConferenciaNumerica? Ler(string processo, Provedor provedor, string registro)
    {
        if (RegistroExecucao.MotivoInvalido(processo, registro) is not null) return null;
        var id = Path.GetFileName(Path.TrimEndingDirectorySeparator(registro));
        var rota = provedor switch { Provedor.ChatGpt => "codex", Provedor.Claude => "claude", _ => "gemini" };
        var atual = Path.Combine(processo, ResultadoAnalise.PastaControle(provedor), "local", ArquivoControle);
        return LerArquivo(atual, id, rota) ?? LerArquivo(Path.Combine(registro, ArquivoHistorico), id, rota);
    }

    static ConferenciaNumerica? LerArquivo(string caminho, string id, string rota)
    {
        if (ArquivoJsonDoKit.Ler(caminho) is not { ValueKind: JsonValueKind.Object } recibo
            || !recibo.TryGetProperty("versao", out var versao) || versao.ValueKind != JsonValueKind.Number || !versao.TryGetInt32(out var v) || v != 1
            || Campo(recibo, "execucao") != id || Campo(recibo, "rota") != rota
            || Campo(recibo, "fase") is not ("preparo" or "montagem")
            || !recibo.TryGetProperty("linhas", out var verificacoes) || verificacoes.ValueKind != JsonValueKind.Array
            || verificacoes.GetArrayLength() != 3) return null;

        var confirmadas = 0;
        var insuficientes = 0;
        var divergentes = 0;
        var detalhes = new Dictionary<string, string>();
        foreach (var linha in verificacoes.EnumerateArray())
        {
            if (linha.ValueKind != JsonValueKind.Object) return null;
            var indice = Campo(linha, "indice");
            if (indice is not ("to" or "ca" or "tp") || detalhes.ContainsKey(indice)) return null;
            var situacao = Campo(linha, "situacao");
            switch (situacao)
            {
                case "CONFERE": confirmadas++; break;
                case "INSUFICIENTE": insuficientes++; break;
                case "DIVERGENTE": divergentes++; break;
                default: return null;
            }
            string Valor(string campo) => Campo(linha, campo) is { Length: > 0 } valor ? valor : "não disponível";
            var textos = new List<string> { $"{indice.ToUpperInvariant()} · {situacao} · declarado {Valor("declarado")} · calculado {Valor("calculado")}" };
            if (Campo(linha, "formula") is { Length: > 0 } formula) textos.Add("Fórmula: " + formula);
            if (linha.TryGetProperty("fontes", out var fontes) && fontes.ValueKind == JsonValueKind.Array)
                foreach (var fonte in fontes.EnumerateArray())
                    if (fonte.ValueKind == JsonValueKind.String && fonte.GetString()?.Trim() is { Length: > 0 } s) textos.Add("Fonte: " + s);
            if (Campo(linha, "motivo") is { Length: > 0 } detalhe) textos.Add(detalhe);
            detalhes.Add(indice, string.Join(Environment.NewLine, textos));
        }
        var resumo = $"Conferidos: {confirmadas} · Divergentes: {divergentes} · Com dados insuficientes: {insuficientes}";
        var secoes = new List<string> { detalhes["to"], detalhes["ca"], detalhes["tp"] };
        if (recibo.TryGetProperty("auditoria_local", out var auditoria) && auditoria.ValueKind == JsonValueKind.Object)
        {
            foreach (var motor in new[] { "DuckDB", "Z3" })
            {
                if (!auditoria.TryGetProperty(motor, out var info) || info.ValueKind != JsonValueKind.Object) continue;
                var itens = new List<string> { motor + " · " + Campo(info, "estado") };
                if (info.TryGetProperty("resultados", out var lista) && lista.ValueKind == JsonValueKind.Array)
                    foreach (var item in lista.EnumerateArray())
                    {
                        if (item.ValueKind != JsonValueKind.Object) continue;
                        var fontes = item.TryGetProperty("fontes", out var f) && f.ValueKind == JsonValueKind.Array
                            ? string.Join(", ", f.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString())) : "";
                        itens.Add(motor == "Z3" ? $"{Campo(item, "indice")} · {Campo(item, "resultado")} · {fontes}"
                            : $"{Campo(item, "campo")} · {Campo(item, "valor")} · {fontes}");
                    }
                secoes.Add(string.Join(Environment.NewLine, itens));
            }
        }
        recibo.TryGetProperty("dados_declarados", out var dados);
        var temInventario = recibo.TryGetProperty("inventario_preparo", out var inventario)
            && inventario.ValueKind == JsonValueKind.Object;
        var declaracoesDoPreparo = temInventario && inventario.TryGetProperty("dados", out var extraidos)
            ? LerDeclaracoes(extraidos) : [];
        var limitacoes = new List<Limitacao>();
        if (temInventario && inventario.TryGetProperty("paginas_sem_declaracao", out var paginas) && paginas.ValueKind == JsonValueKind.Array)
            foreach (var pagina in paginas.EnumerateArray())
                if (pagina.ValueKind == JsonValueKind.Object && Campo(pagina, "situacao") is ("ilegivel" or "pendente_de_conferencia")
                    && Campo(pagina, "motivo") is { Length: > 0 } motivo)
                    limitacoes.Add(new(Campo(pagina, "fonte") ?? "", motivo));
        return new(Campo(recibo, "fase")!, resumo, string.Join(Environment.NewLine + Environment.NewLine, secoes))
        {
            Declaracoes = LerDeclaracoes(dados),
            Evidencias = LerEvidencias(verificacoes, dados),
            TemInventario = temInventario,
            Inventario = declaracoesDoPreparo,
            Limitacoes = limitacoes
        };
    }

    static List<Declaracao> LerDeclaracoes(JsonElement dados)
    {
        var declaracoes = new List<Declaracao>();
        if (dados.ValueKind == JsonValueKind.Array)
            foreach (var dado in dados.EnumerateArray())
                if (dado.ValueKind == JsonValueKind.Object && Campo(dado, "rotulo") is { Length: > 0 } rotulo)
                {
                    var valor = Campo(dado, "valor") ?? "";
                    if (Campo(dado, "unidade") is { Length: > 0 } unidade && !valor.EndsWith(unidade, StringComparison.Ordinal))
                        valor = $"{valor} {unidade}".Trim();
                    string Juntar(params string[] campos) => string.Join(" · ", campos.Select(c => Campo(dado, c))
                        .Where(c => !string.IsNullOrWhiteSpace(c)).Distinct());
                    declaracoes.Add(new(rotulo, valor, Juntar("fonte", "arquivo", "regiao"), Campo(dado, "revisao") ?? "",
                        Juntar("objeto", "quadro", "cabecalhos", "abrangencia", "elemento_medido"),
                        Campo(dado, "ressalva") ?? "", Campo(dado, "campo") ?? ""));
                }
        return declaracoes;
    }

    static List<Evidencia> LerEvidencias(JsonElement linhas, JsonElement dados)
    {
        var resultado = new List<Evidencia>();
        if (dados.ValueKind != JsonValueKind.Array) return resultado;
        var registros = dados.EnumerateArray().Where(d => d.ValueKind == JsonValueKind.Object).ToList();
        var porId = registros.ToLookup(d => Campo(d, "id_dado"));
        var porPeca = registros.ToLookup(d => Campo(d, "peca"));
        foreach (var linha in linhas.EnumerateArray())
        {
            if (!linha.TryGetProperty("parcelas", out var parcelas) || parcelas.ValueKind != JsonValueKind.Object) continue;
            foreach (var parcela in parcelas.EnumerateObject())
            {
                if (parcela.Value.ValueKind != JsonValueKind.Array) continue;
                foreach (var chave in parcela.Value.EnumerateArray())
                {
                    if (chave.ValueKind != JsonValueKind.String || chave.GetString() is not { Length: > 0 } id) continue;
                    var vinculados = porId[id].Take(2).ToArray();
                    if (vinculados.Length != 1) continue;
                    var dado = vinculados[0];
                    if (Campo(dado, "peca") is not { Length: > 0 } peca || !dado.TryGetProperty("pagina", out var pg)
                        || pg.ValueKind != JsonValueKind.Number || !pg.TryGetInt32(out var pagina) || pagina < 1) continue;
                    var hash = Campo(dado, "sha256") ?? "";
                    if (hash.Length == 0)
                    {
                        var conhecidos = porPeca[peca].Select(d => Campo(d, "sha256")).Where(h => !string.IsNullOrEmpty(h))
                            .Distinct(StringComparer.OrdinalIgnoreCase).Take(2).ToArray();
                        if (conhecidos.Length == 1) hash = conhecidos[0]!;
                    }
                    resultado.Add(new(Campo(linha, "indice")!, parcela.Name, Campo(dado, "rotulo") ?? parcela.Name,
                        string.Join(" ", new[] { Campo(dado, "valor"), Campo(dado, "unidade") }.Where(v => !string.IsNullOrEmpty(v))),
                        peca, pagina, Campo(dado, "arquivo") ?? "", hash, Campo(dado, "recorte") ?? "",
                        Campo(dado, "recurso_sha256") ?? "", Campo(dado, "regiao") ?? ""));
                }
            }
        }
        return resultado;
    }

    static string? Campo(JsonElement obj, string campo)
    {
        return obj.TryGetProperty(campo, out var valor) && valor.ValueKind == JsonValueKind.String ? valor.GetString()?.Trim() : null;
    }
}
