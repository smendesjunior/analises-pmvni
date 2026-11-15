using System.Collections.Frozen;
using System.Text.Json;

namespace Pmvni.Composicao.Web.Ponte;

/// <summary>Mensagem válida recebida da web: envelope <c>{ "v": 1, "tipo": string, "id": string, "dados": object }</c>.</summary>
public sealed record MensagemWeb(int V, string Tipo, string Id, JsonElement Dados);

/// <summary>Mensagem descartada (inválida ou de origem não permitida), registrada para diagnóstico e testes.</summary>
public sealed record Descarte(string Motivo, string Conteudo);

/// <summary>A web respondeu à chamada com uma mensagem <c>erro</c>.</summary>
public sealed class ErroWebException(string codigo, string mensagem, JsonElement dados) : Exception(mensagem)
{
    public string Codigo { get; } = codigo;
    public JsonElement Dados { get; } = dados;
}

/// <summary>Envelope e validação da ponte (espelho de web/paginador/src/mensagens.ts).</summary>
public static class Mensagens
{
    public const int Versao = 1;

    /// <summary>Tipos do host para a web. Tarefa 7: <c>estadoEditor</c>, <c>selecionar</c>, <c>editarCampo</c>, <c>concluirEdicao</c> e
    /// <c>obterAncora</c> (dados em camelCase; <c>ref</c> é o data-ref do bloco). Tarefa 11: <c>pesquisar</c> (<c>termo</c>, <c>passo</c>).</summary>
    public static readonly FrozenSet<string> TiposHostParaWeb =
        new[]
        {
            "compor", "aplicarPlano", "textoPorFolha", "rolarPara", "definirZoom",
            "estadoEditor", "selecionar", "editarCampo", "concluirEdicao", "obterAncora",
            "pesquisar",
        }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Tipos da web para o host: respostas (<c>estadoAplicado</c>, <c>selecionado</c>, <c>edicaoIniciada</c>, <c>editar</c>,
    /// <c>semEdicao</c>, <c>ancora</c>, <c>pesquisado</c>) e eventos de interação, que chegam com id <c>evento-N</c> (<c>selecionado</c>,
    /// <c>pedirEdicao</c>, <c>editar</c>, <c>edicaoCancelada</c>, <c>atalho</c>, <c>menuContexto</c>, <c>excluidoClicado</c>).</summary>
    public static readonly FrozenSet<string> TiposWebParaHost =
        new[]
        {
            "pronto", "composto", "planoAplicado", "textoPorFolha", "folhaVisivel", "erro",
            "estadoAplicado", "selecionado", "edicaoIniciada", "editar", "semEdicao", "ancora",
            "pedirEdicao", "edicaoCancelada", "atalho", "menuContexto", "excluidoClicado",
            "pesquisado",
        }.ToFrozenSet(StringComparer.Ordinal);

    internal static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);

    /// <summary>Lê e valida uma mensagem web→host. Versão diferente de 1, tipo desconhecido (ou do sentido oposto) ou campo
    /// faltando: devolve false com o motivo.</summary>
    public static bool TentarLerDaWeb(string json, out MensagemWeb? mensagem, out string? motivo) =>
        TentarLer(json, TiposWebParaHost, out mensagem, out motivo);

    /// <summary>A mesma validação para outro conjunto de tipos web→host (a ponte da legislação usa os seus).</summary>
    internal static bool TentarLer(string json, FrozenSet<string> tiposWebParaHost, out MensagemWeb? mensagem, out string? motivo)
    {
        mensagem = null;
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException e) { motivo = "json invalido: " + e.Message; return false; }
        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object) { motivo = "mensagem nao e um objeto"; return false; }
            if (!raiz.TryGetProperty("v", out var v)) { motivo = "campo \"v\" faltando"; return false; }
            if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var versao) || versao != Versao) { motivo = "versao nao suportada: " + v.GetRawText(); return false; }
            if (!raiz.TryGetProperty("tipo", out var t) || t.ValueKind != JsonValueKind.String) { motivo = "campo \"tipo\" faltando"; return false; }
            var tipo = t.GetString()!;
            if (!tiposWebParaHost.Contains(tipo)) { motivo = "tipo desconhecido: " + tipo; return false; }
            if (!raiz.TryGetProperty("id", out var i) || i.ValueKind != JsonValueKind.String || i.GetString() is not { Length: > 0 } id) { motivo = "campo \"id\" faltando"; return false; }
            if (!raiz.TryGetProperty("dados", out var dados) || dados.ValueKind != JsonValueKind.Object) { motivo = "campo \"dados\" faltando ou invalido"; return false; }
            mensagem = new MensagemWeb(versao, tipo, id, dados.Clone());
            motivo = null;
            return true;
        }
    }

    /// <summary>Serializa uma mensagem host→web; o tipo precisa ser um dos tipos do host.</summary>
    public static string SerializarParaWeb(string tipo, string id, object dados) => Serializar(TiposHostParaWeb, tipo, id, dados);

    /// <summary>A mesma serialização para outro conjunto de tipos host→web (a ponte da legislação usa os seus).</summary>
    internal static string Serializar(FrozenSet<string> tiposHostParaWeb, string tipo, string id, object dados)
    {
        if (!tiposHostParaWeb.Contains(tipo)) throw new ArgumentException("Tipo host->web desconhecido: " + tipo, nameof(tipo));
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id vazio", nameof(id));
        var elemento = JsonSerializer.SerializeToElement(dados, Opcoes);
        if (elemento.ValueKind != JsonValueKind.Object) throw new ArgumentException("dados precisa ser um objeto JSON", nameof(dados));
        return JsonSerializer.Serialize(new { v = Versao, tipo, id, dados = elemento }, Opcoes);
    }
}
