using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Legislacao;

/// <summary>O que o <c>catalogo.json</c> guarda (desenho §11.2).</summary>
internal sealed record DadosCatalogo(int Versao, List<Anexo> Anexos);

/// <summary>Leitura e escrita do <c>catalogo.json</c>: <c>{ "versao_catalogo": 1, "anexos": [ … ] }</c>, com os nomes em snake_case.
/// A leitura de um catálogo da versão atual é estrita (qualquer defeito ⇒ catálogo danificado); a de uma versão mais nova é
/// tolerante (o que não se entende fica de fora), porque o app só o lê.</summary>
internal static class CatalogoLegislacao
{
    static readonly JsonSerializerOptions OpcoesEscrita = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // acentos como estão, não ç
    };

    internal static bool TentarLer(byte[] bytes, out DadosCatalogo? dados)
    {
        dados = null;
        try
        {
            if (JsonNode.Parse(bytes, null, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Disallow }) is not JsonObject raiz) return false;
            if (raiz["versao_catalogo"] is not JsonValue v || !v.TryGetValue<int>(out var versao) || versao < 1) return false;
            if (raiz["anexos"] is not JsonArray lista) return false;
            var estrito = versao <= AcervoLegislacao.VersaoCatalogo;
            var anexos = new List<Anexo>();
            foreach (var item in lista)
            {
                var anexo = LerAnexo(item as JsonObject);
                if (anexo is null) { if (estrito) return false; continue; }
                anexos.Add(anexo);
            }
            if (estrito && anexos.Select(a => a.Id).Distinct(StringComparer.Ordinal).Count() != anexos.Count) return false;
            dados = new DadosCatalogo(versao, anexos);
            return true;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException or OverflowException)
        {
            return false;
        }
    }

    static Anexo? LerAnexo(JsonObject? o)
    {
        if (o is null) return null;
        try
        {
            var id = Texto(o, "id");
            var sha = Texto(o, "sha256");
            if (string.IsNullOrEmpty(id) || !EhSha(sha)) return null;
            var titulo = Texto(o, "titulo");
            var original = Texto(o, "arquivo_original");
            if (titulo is null || original is null) return null;
            var tamanho = o["tamanho"]?.GetValue<long>() ?? -1;
            var paginas = o["paginas"]?.GetValue<int>() ?? -1;
            if (tamanho < 0 || paginas < 0 || o["paginas_sem_texto"] is not JsonArray sem) return null;
            var semTexto = sem.Select(n => n!.GetValue<int>()).ToArray();
            if (!TentarData(Texto(o, "anexado_em"), out var anexado)) return null;
            DateTimeOffset? substituido = null;
            if (o["substituido_em"] is JsonValue sv)
            {
                if (!TentarData(sv.GetValue<string>(), out var s)) return null;
                substituido = s;
            }
            return new Anexo(id, sha!, titulo, Esferas.DeRotulo(Texto(o, "esfera")), original, tamanho, paginas, semTexto, anexado, substituido);
        }
        catch (Exception e) when (e is InvalidOperationException or FormatException or OverflowException or NullReferenceException)
        {
            return null;
        }
    }

    static string? Texto(JsonObject o, string chave) => o[chave] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static bool EhSha(string? s) =>
        s is { Length: 64 } && s.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    internal static string Data(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    static bool TentarData(string? s, out DateTimeOffset d) =>
        DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out d);

    internal static byte[] Serializar(int versao, IEnumerable<Anexo> anexos)
    {
        var lista = new JsonArray();
        foreach (var a in anexos)
        {
            lista.Add(new JsonObject
            {
                ["id"] = a.Id,
                ["sha256"] = a.Sha256,
                ["titulo"] = a.Titulo,
                ["esfera"] = Esferas.Rotulo(a.Esfera),
                ["arquivo_original"] = a.ArquivoOriginal,
                ["tamanho"] = a.Tamanho,
                ["paginas"] = a.Paginas,
                ["paginas_sem_texto"] = new JsonArray([.. a.PaginasSemTexto.Select(p => (JsonNode?)JsonValue.Create(p))]),
                ["anexado_em"] = Data(a.AnexadoEm),
                ["substituido_em"] = a.SubstituidoEm is { } s ? Data(s) : null,
            });
        }
        var raiz = new JsonObject { ["versao_catalogo"] = versao, ["anexos"] = lista };
        return new UTF8Encoding(false).GetBytes(raiz.ToJsonString(OpcoesEscrita));
    }
}
