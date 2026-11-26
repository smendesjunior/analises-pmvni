using System.Text.Json.Nodes;
using Pmvni.Composicao;

namespace Pmvni.Revisao;

/// <summary>O dados.json efetivo (<see cref="Json"/>) e os ids dos itens na ordem em que saem nele (<see cref="IdsDosItens"/>[i] = item do card i).</summary>
public sealed record DadosGerados(byte[] Json, IReadOnlyList<string> IdsDosItens);

/// <summary>Projeto → dados.json que o kit renderiza: só os itens incluídos, na ordem, com o texto revisado no lugar do original e as
/// citações "item NN" impressas com a numeração atual. Nunca muda o <see cref="Projeto"/> (trabalha em cópias).</summary>
public static class DadosEfetivos
{
    public static DadosGerados Gerar(Projeto p, bool apresentarContaNominal = false)
    {
        ArgumentNullException.ThrowIfNull(p);
        var incluidos = p.Incluidos;
        var itens = new JsonArray();
        foreach (var it in incluidos) itens.Add(ItemEfetivo(p, it));

        var documento = (JsonObject)p.Documento.DeepClone();
        documento.Remove("_textos_da_revisao");
        documento.Remove("_campos_ocultos");
        documento.Remove("_edicao_do_relatorio");
        if (apresentarContaNominal) documento["_edicao_do_relatorio"] = true;
        if (p.TextosDoModelo.Count > 0) documento["_textos_da_revisao"] = p.TextosDoModelo.DeepClone();
        if (documento["conclusao"] is JsonValue v && v.TryGetValue<string>(out var conclusao))
            documento["conclusao"] = Imprimir(p, conclusao);
        documento["itens"] = itens; // a chave já existe: fica no lugar original
        if (documento["notas_pendencias_normativas"] is JsonObject notas)
        {
            var pendentes = itens.OfType<JsonObject>().Where(i => i["situacao"]?.ToString() == "DECISAO_DO_ANALISTA")
                .Select(i => i["id"]?.ToString()).ToHashSet();
            foreach (var id in notas.Select(n => n.Key).Where(k => !pendentes.Contains(k)).ToList()) notas.Remove(id);
        }
        TabelasRevisao.Aplicar(p, documento); // linhas de tabela revisadas (sem revisão, nada muda)
        if (apresentarContaNominal && TabelasRevisao.Lista(documento, TabelasRevisao.Ambientes) is { } ambientes)
            for (var i = 0; i < ambientes.Count; i++)
            {
                if (ambientes[i] is not JsonObject ambiente) continue;
                var linha = TabelasRevisao.Id(TabelasRevisao.Ambientes, i + 1);
                if (Esquadrias.Calcular(p, linha) is not { } conta || conta.Situacao is not { } nominal) continue;
                ambiente["_situacao_nominal"] = nominal;
                if (ambiente["situacao"]?.ToString() is { } tecnica && tecnica != nominal)
                    ambiente["_situacao_tecnica"] = tecnica;
                if (nominal == "PENDENTE") ambiente["_pendencia_nominal"] = "Conta nominal pendente: falta " + conta.TextoPendencia + ".";
            }
        // Figuras: a camada de edição, se houver; senão as do documento, com o "(item NN)" das notas renumerado.
        if (p.Figuras is not null) documento["figuras"] = CamadaFiguras.Efetivas(p);
        else CamadaFiguras.RenumerarNotas(p, documento);
        var ocultos = CaminhosOcultos(p, incluidos);
        if (ocultos.Count > 0) documento["_campos_ocultos"] = ocultos;

        return new DadosGerados(JsonFiel.Escrever(documento), incluidos.Select(i => i.Id).ToList());
    }

    static JsonArray CaminhosOcultos(Projeto p, IReadOnlyList<ItemRevisao> incluidos)
    {
        var caminhos = new JsonArray();
        var campos = TextosRevisados.CamposOcultos(p);
        if (campos.Count == 0) return caminhos;
        var itens = incluidos.Select((i, n) => (i.Id, n)).ToDictionary(x => x.Id, x => x.n, StringComparer.Ordinal);
        var figuras = (p.Figuras ?? CamadaFiguras.Inicial(p)).Where(f => f.Incluida)
            .Select((f, n) => (f, n)).ToDictionary(x => x.f.Id, StringComparer.Ordinal);
        foreach (var (alvo, campo) in campos)
        {
            string? baseCampo = null;
            string propriedade = campo;
            if (itens.TryGetValue(alvo, out var item) && Campos.Editaveis.Contains(campo))
                baseCampo = $"itens/{item}";
            else if (figuras.TryGetValue(alvo, out var figura))
            {
                baseCampo = $"figuras/{figura.n}";
                if (campo.StartsWith("marcacao/", StringComparison.Ordinal))
                {
                    var partes = campo.Split('/');
                    if (figura.f.Buscar(partes[1]) is not { } marca) continue;
                    var pos = p.Figuras is null ? marca.PosicaoNoDesenhoOriginal : CamadaFiguras.Desenho(figura.f).ToList().FindIndex(m => m.Id == marca.Id);
                    var nota = p.Figuras is null ? figura.f.Marcacoes.IndexOf(marca) : CamadaFiguras.Numero(figura.f, marca.Id) - 1;
                    if (partes.Length == 2)
                    {
                        if (!marca.Gravada && pos >= 0) caminhos.Add($"{baseCampo}/marcacoes/{pos}");
                        if (marca.Numerada)
                            caminhos.Add($"{baseCampo}/notas/{nota}");
                        continue;
                    }
                    if (partes.Length != 3) continue;
                    if (marca.Tipo == "texto")
                        propriedade = $"marcacoes/{pos}/{partes[2]}";
                    else
                        propriedade = $"notas/{nota}/{partes[2]}";
                }
                else if (!CamadaFiguras.CamposTexto.Contains(campo)) continue;
            }
            else if (TabelasRevisao.TentarLer(alvo, out var bloco, out var posicao))
                baseCampo = bloco switch
                {
                    TabelasRevisao.Ambientes => $"iluminacao/ambientes/{posicao - 1}",
                    TabelasRevisao.Indices => $"indices/linhas/{posicao - 1}",
                    TabelasRevisao.Chamadas => $"diagrama/chamadas/{posicao - 1}",
                    TabelasRevisao.Planta or TabelasRevisao.Volumetria => $"diagrama/{bloco}",
                    "capa" => "capa",
                    _ => null,
                };
            if (baseCampo is not null) caminhos.Add(string.IsNullOrEmpty(propriedade) ? baseCampo : baseCampo + "/" + propriedade);
        }
        return caminhos;
    }

    /// <summary>O texto impresso do campo (revisado, senão o original, com as citações numeradas); null se o campo não existe, é null
    /// ou não é texto.</summary>
    public static string? TextoEfetivo(Projeto p, ItemRevisao it, string campo)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(it);
        ArgumentNullException.ThrowIfNull(campo);
        if (!it.Campos.TryGetValue(campo, out var c)) return null;
        var texto = c.Revisado ?? (c.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null);
        return texto is null ? null : Imprimir(p, texto);
    }

    /// <summary>Troca o id de cada <c>.card</c> (o card i é o i-ésimo item incluído) pelo id do item no projeto; o HTML não muda.</summary>
    public static ResultadoRender RefsDeSessao(ResultadoRender r, IReadOnlyList<string> idsDosItens)
    {
        ArgumentNullException.ThrowIfNull(r);
        ArgumentNullException.ThrowIfNull(idsDosItens);
        var refs = r.Refs.Select(x =>
        {
            if (x.Seletor != ".card") return x;
            if (x.Indice < 0 || x.Indice >= idsDosItens.Count)
                throw new ArgumentException("Há mais cards no relatório do que ids de itens: card " + x.Indice, nameof(idsDosItens));
            return x with { Id = idsDosItens[x.Indice] };
        }).ToList();
        return r with { Refs = refs };
    }

    private static JsonObject ItemEfetivo(Projeto p, ItemRevisao it)
    {
        var o = new JsonObject();
        foreach (var chave in it.Chaves)
        {
            if (chave == "situacao") o[chave] = Situacao(it);
            else if (it.Campos.TryGetValue(chave, out var c)) o[chave] = Campo(p, c);
            else if (it.Outros.TryGetPropertyValue(chave, out var outro)) o[chave] = outro?.DeepClone();
        }
        // O que a edição criou (o original não tinha a chave) vai ao fim.
        foreach (var nome in Campos.Editaveis)
            if (!it.Chaves.Contains(nome) && it.Campos.TryGetValue(nome, out var c) && c.Revisado is not null)
                o[nome] = Imprimir(p, c.Revisado);
        if (!it.Chaves.Contains("situacao") && it.Situacao.Revisada is not null) o["situacao"] = it.Situacao.Revisada;
        if (it.Situacao.Original?.ToString() == "DECISAO_DO_ANALISTA" && it.Situacao.Revisada is not null &&
            !string.IsNullOrWhiteSpace(it.Situacao.Justificativa) && o["id"]?.ToString() is string id &&
            p.Documento["notas_pendencias_normativas"] is JsonObject notas && notas[id]?.ToString() is string nota &&
            o["base_legal"]?.ToString() == nota)
            o["base_legal"] = Imprimir(p, it.Situacao.Justificativa);
        return o;
    }

    private static JsonNode? Situacao(ItemRevisao it) =>
        it.Situacao.Revisada is not null ? JsonValue.Create(it.Situacao.Revisada) : it.Situacao.Original?.DeepClone();

    private static JsonNode? Campo(Projeto p, CampoTexto c)
    {
        if (c.Revisado is not null) return JsonValue.Create(Imprimir(p, c.Revisado));
        if (c.Original is JsonValue v && v.TryGetValue<string>(out var texto)) return JsonValue.Create(Imprimir(p, texto));
        return c.Original?.DeepClone();
    }

    internal static string Imprimir(Projeto p, string texto) =>
        Citacoes.Imprimir(texto, id => p.NumeroAtual(id), id => p.Item(id)?.NumeroOriginal);
}
