using System.Text.Json.Nodes;
using Pmvni.Composicao;

namespace Pmvni.Revisao;

/// <summary>O dados.json efetivo (<see cref="Json"/>) e os ids dos itens na ordem em que saem nele (<see cref="IdsDosItens"/>[i] = item do card i).</summary>
public sealed record DadosGerados(byte[] Json, IReadOnlyList<string> IdsDosItens);

/// <summary>Projeto → dados.json que o kit renderiza: só os itens incluídos, na ordem, com o texto revisado no lugar do original e as
/// citações "item NN" impressas com a numeração atual. Nunca muda o <see cref="Projeto"/> (trabalha em cópias).</summary>
public static class DadosEfetivos
{
    public static DadosGerados Gerar(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var incluidos = p.Incluidos;
        var itens = new JsonArray();
        foreach (var it in incluidos) itens.Add(ItemEfetivo(p, it));

        var documento = (JsonObject)p.Documento.DeepClone();
        if (documento["conclusao"] is JsonValue v && v.TryGetValue<string>(out var conclusao))
            documento["conclusao"] = Imprimir(p, conclusao);
        documento["itens"] = itens; // a chave já existe: fica no lugar original

        return new DadosGerados(JsonFiel.Escrever(documento), incluidos.Select(i => i.Id).ToList());
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

    private static string Imprimir(Projeto p, string texto) =>
        Citacoes.Imprimir(texto, id => p.NumeroAtual(id), id =>
            p.Item(id)?.NumeroOriginal ?? throw new InvalidOperationException("Citação a um item que não existe no projeto: " + id));
}
