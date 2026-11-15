using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary><c>projeto.json</c> ↔ <see cref="Projeto"/>. Formato: <c>{versao_esquema, itens[{id, ordem, numero_original, incluido, chaves[],
/// situacao{original, revisada, justificativa}, campos{nome:{original?, revisado}}, outros{}}], documento{}}</c>;
/// um campo sem <c>original</c> é um campo ausente no original que o usuário criou.</summary>
public static class ProjetoJson
{
    /// <summary><see cref="JsonException"/> se o JSON não for um projeto.</summary>
    public static Projeto Ler(byte[] utf8)
    {
        var j = JsonFiel.Ler(utf8);
        try
        {
            var p = new Projeto
            {
                VersaoEsquema = j["versao_esquema"]!.GetValue<string>(),
                Documento = ((JsonObject)j["documento"]!).DeepClone().AsObject(),
            };
            foreach (var x in j["itens"]!.AsArray()) p.Itens.Add(LerItem(x!.AsObject()));
            return p;
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException or InvalidCastException or FormatException)
        {
            throw new JsonException("projeto.json inválido: " + e.Message, e);
        }
    }

    private static ItemRevisao LerItem(JsonObject j)
    {
        var item = new ItemRevisao
        {
            Id = j["id"]!.GetValue<string>(),
            Ordem = j["ordem"]!.GetValue<int>(),
            NumeroOriginal = j["numero_original"]!.GetValue<int>(),
            Incluido = j["incluido"]!.GetValue<bool>(),
            Outros = j["outros"]!.AsObject().DeepClone().AsObject(),
        };
        foreach (var c in j["chaves"]!.AsArray()) item.Chaves.Add(c!.GetValue<string>());

        var s = j["situacao"]!.AsObject();
        item.Situacao = new SituacaoItem
        {
            Original = s["original"]?.DeepClone(),
            Revisada = s["revisada"]?.GetValue<string>(),
            Justificativa = s["justificativa"]?.GetValue<string>(),
        };

        foreach (var (nome, valor) in j["campos"]!.AsObject())
        {
            var o = valor!.AsObject();
            item.Campos[nome] = new CampoTexto
            {
                Ausente = !o.ContainsKey("original"),
                Original = o["original"]?.DeepClone(),
                Revisado = o["revisado"]?.GetValue<string>(),
            };
        }
        return item;
    }

    public static byte[] Escrever(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var itens = new JsonArray();
        foreach (var i in p.Itens) itens.Add(EscreverItem(i));
        return JsonFiel.Escrever(new JsonObject
        {
            ["versao_esquema"] = p.VersaoEsquema,
            ["itens"] = itens,
            ["documento"] = p.Documento.DeepClone(),
        });
    }

    private static JsonObject EscreverItem(ItemRevisao i)
    {
        var chaves = new JsonArray();
        foreach (var c in i.Chaves) chaves.Add(c);

        var campos = new JsonObject();
        foreach (var (nome, c) in i.Campos)
        {
            var o = new JsonObject();
            if (!c.Ausente) o["original"] = c.Original?.DeepClone();
            o["revisado"] = c.Revisado;
            campos[nome] = o;
        }

        return new JsonObject
        {
            ["id"] = i.Id,
            ["ordem"] = i.Ordem,
            ["numero_original"] = i.NumeroOriginal,
            ["incluido"] = i.Incluido,
            ["chaves"] = chaves,
            ["situacao"] = new JsonObject
            {
                ["original"] = i.Situacao.Original?.DeepClone(),
                ["revisada"] = i.Situacao.Revisada,
                ["justificativa"] = i.Situacao.Justificativa,
            },
            ["campos"] = campos,
            ["outros"] = i.Outros.DeepClone(),
        };
    }
}
