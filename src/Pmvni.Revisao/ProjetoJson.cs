using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary><c>projeto.json</c> ↔ <see cref="Projeto"/>. Formato: <c>{versao_esquema, itens[{id, ordem, numero_original, incluido, criado?, chaves[],
/// situacao{original, revisada, justificativa}, campos{nome:{original?, revisado}}, outros{}}], documento{}, linhas?{id:{situacao,
/// justificativa, campos{caminho: texto}}}, acrescimos?{chave:{…}}}</c> (<c>linhas</c> só com alguma linha de tabela revisada;
/// <c>acrescimos</c> só com alguma esquadria ou chamada criada pelo usuário);
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
            // Os acréscimos entram antes das linhas: uma linha revisada pode ser uma linha acrescentada.
            if (j["acrescimos"] is JsonObject acrescimos)
                foreach (var (chave, valor) in acrescimos)
                {
                    if (!TabelasRevisao.EhChaveDeAcrescimo(chave)) throw new JsonException("projeto.json inválido: acréscimo com chave inválida: " + chave);
                    p.Acrescimos[chave] = valor!.AsObject().DeepClone().AsObject();
                }
            if (j["linhas"] is JsonObject linhas)
                foreach (var (id, valor) in linhas)
                {
                    if (!TabelasRevisao.EhLinha(id)) throw new JsonException("projeto.json inválido: linha de tabela com id inválido: " + id);
                    p.Linhas[id] = LerLinha(valor!.AsObject());
                }
            if (j["figuras"] is { } figuras) p.Figuras = CamadaFiguras.Ler(figuras);
            if (j["textos_do_modelo"] is JsonObject textos) p.TextosDoModelo = (JsonObject)textos.DeepClone();
            return p;
        }
        catch (Exception e) when (e is NullReferenceException or InvalidOperationException or InvalidCastException or FormatException)
        {
            throw new JsonException("projeto.json inválido: " + e.Message, e);
        }
    }

    internal static ItemRevisao LerItem(JsonObject j)
    {
        var item = new ItemRevisao
        {
            Id = j["id"]!.GetValue<string>(),
            Ordem = j["ordem"]!.GetValue<int>(),
            NumeroOriginal = j["numero_original"]!.GetValue<int>(),
            Incluido = j["incluido"]!.GetValue<bool>(),
            Criado = j["criado"]?.GetValue<bool>() ?? false,
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

    private static LinhaRevisao LerLinha(JsonObject j)
    {
        var l = new LinhaRevisao
        {
            Situacao = j["situacao"]?.GetValue<string>(),
            Justificativa = j["justificativa"]?.GetValue<string>(),
        };
        foreach (var (caminho, texto) in j["campos"]!.AsObject()) l.Campos[caminho] = texto!.GetValue<string>();
        return l;
    }

    public static byte[] Escrever(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var itens = new JsonArray();
        foreach (var i in p.Itens) itens.Add(EscreverItem(i));
        var o = new JsonObject
        {
            ["versao_esquema"] = p.VersaoEsquema,
            ["itens"] = itens,
            ["documento"] = p.Documento.DeepClone(),
        };
        // Linhas de tabela revisadas: a chave só existe quando há alguma (o projeto sem revisão de tabela fica como antes).
        if (p.Linhas.Count > 0)
        {
            var linhas = new JsonObject();
            foreach (var (id, l) in p.Linhas)
            {
                var campos = new JsonObject();
                foreach (var (caminho, texto) in l.Campos) campos[caminho] = texto;
                linhas[id] = new JsonObject { ["situacao"] = l.Situacao, ["justificativa"] = l.Justificativa, ["campos"] = campos };
            }
            o["linhas"] = linhas;
        }
        // O que o usuário acrescentou às tabelas (esquadrias, chamadas): a chave só existe quando há algum.
        if (p.Acrescimos.Count > 0)
        {
            var acrescimos = new JsonObject();
            foreach (var (chave, modelo) in p.Acrescimos) acrescimos[chave] = modelo.DeepClone();
            o["acrescimos"] = acrescimos;
        }
        // A camada de figuras só existe depois da primeira edição de figura (um projeto sem ela grava como antes).
        if (p.Figuras is not null) o["figuras"] = CamadaFiguras.Escrever(p.Figuras);
        if (p.TextosDoModelo.Count > 0) o["textos_do_modelo"] = p.TextosDoModelo.DeepClone();
        return JsonFiel.Escrever(o);
    }

    internal static JsonObject EscreverItem(ItemRevisao i)
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

        var item = new JsonObject
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
        // Só o item criado pelo usuário leva a marca (os recebidos gravam como antes).
        if (i.Criado) item["criado"] = true;
        return item;
    }
}
