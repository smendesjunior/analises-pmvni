using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>O <c>&lt;nome&gt;.recibo-editor.json</c> ao lado do despacho (desenho §10.9): de onde ele veio e o que foi conferido.
/// Só dados que já estão no próprio despacho, mais hashes e versões.</summary>
public static class ReciboDespacho
{
    public const int Versao = 1;

    static readonly JsonSerializerOptions Opcoes = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary><c>x.docx</c> → <c>x.recibo-editor.json</c>.</summary>
    public static string CaminhoDe(string docx)
    {
        ArgumentNullException.ThrowIfNull(docx);
        return Path.ChangeExtension(docx, null) + NomesDespacho.ExtensaoRecibo;
    }

    public static JsonObject Ler(string caminho)
    {
        ArgumentNullException.ThrowIfNull(caminho);
        return JsonNode.Parse(File.ReadAllText(caminho, new UTF8Encoding(false)))?.AsObject()
               ?? throw new InvalidDataException("Recibo vazio: " + caminho);
    }

    /// <summary>UTF-8 sem BOM, indentado.</summary>
    internal static byte[] Escrever(JsonObject recibo) => new UTF8Encoding(false).GetBytes(recibo.ToJsonString(Opcoes) + "\n");

    internal static string Utc(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>O PDF no recibo. Assinado digitalmente (2026-11-16), leva também <c>assinatura_digital</c>: a hora, a impressão digital e
    /// a validade do certificado e a página da assinatura visível (o titular fica só no próprio PDF; a senha, em lugar nenhum).</summary>
    internal static JsonObject? Pdf(string arquivo, byte[] pdf, int paginas, IReadOnlyList<string> fontes, string? motor, AssinaturaAplicada? assinatura = null)
    {
        var o = new JsonObject
        {
            ["arquivo"] = Path.GetFileName(arquivo),
            ["sha256"] = RegistroModelos.Sha(pdf),
            ["paginas"] = paginas,
            ["fontes"] = new JsonArray(fontes.Select(f => (JsonNode?)JsonValue.Create(f)).ToArray()),
            ["motor"] = motor,
        };
        if (assinatura is not null)
            o["assinatura_digital"] = new JsonObject
            {
                ["assinado_em"] = Utc(assinatura.Quando),
                ["certificado_sha1"] = assinatura.Certificado.ImpressaoDigital,
                ["certificado_valido_ate"] = assinatura.Certificado.ValidoAte.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["pagina"] = assinatura.Local.Pagina + 1,
            };
        return o;
    }

    internal static JsonObject? Conferidas(IReadOnlyList<int> paginas, ContextoUsuario u) => new()
    {
        ["paginas"] = new JsonArray(paginas.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()),
        ["usuario"] = u.Usuario,
        ["em"] = Utc(u.Relogio()),
    };

    internal static JsonObject Montar(RascunhoDespacho r, DespachoFinal d, ModeloDespacho m, TextosFixos tx, EtapaDespacho etapa, OrigemDespacho o,
        (string Numero, int Ano) processo, string docx, byte[] bytesDocx, JsonObject? pdf, string? semPdfMotivo, JsonObject? conferidas, ContextoUsuario u)
    {
        var agora = Utc(u.Relogio());
        return new JsonObject
        {
            ["recibo_versao"] = Versao,
            ["tipo"] = RegistroModelos.NomeNoArquivo(d.Tipo).ToLowerInvariant(),
            ["papel"] = m.Papel,
            ["registro"] = new JsonObject { ["revisao"] = m.RevisaoRegistro, ["sha256"] = m.Sha256Registro },
            ["modelo"] = new JsonObject { ["caminho"] = m.CaminhoRelativo, ["sha256"] = m.Sha256 },
            ["textos_fixos"] = new JsonObject { ["arquivo"] = tx.Arquivo, ["sha256"] = tx.Sha256 },
            ["relatorio"] = new JsonObject
            {
                ["arquivo"] = Path.GetFileName(o.CaminhoPacote),
                ["sha256_pacote"] = o.Sha256Pacote,
                ["sha256_dados_efetivos"] = o.Sha256DadosEfetivos,
                ["historico_n"] = o.HistoricoN,
            },
            ["processo"] = new JsonObject { ["numero"] = int.TryParse(processo.Numero, out var n) ? JsonValue.Create(n) : JsonValue.Create(processo.Numero), ["identificador"] = processo.Numero, ["ano"] = processo.Ano, ["pasta"] = o.Processo.NomePasta },
            ["etapa"] = new JsonObject
            {
                ["pasta"] = etapa.Nome,
                ["numero"] = etapa.Numero,
                ["data"] = etapa.Data.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["nova"] = etapa.Nova,
            },
            ["exigencias"] = new JsonArray(d.Tipo != TipoDespacho.Correcao ? Array.Empty<JsonNode?>() : r.Pendencias.Where(p => p.Incluida)
                .Select(p => (JsonNode?)new JsonObject
                {
                    ["item"] = p.ItemId, ["numero"] = p.Numero, ["situacao"] = p.Situacao, ["grupo"] = p.Grupo, ["texto_editado"] = p.Texto != p.TextoOriginal,
                }).ToArray()),
            ["campos_editados"] = new JsonArray(r.CamposEditados.Select(c => (JsonNode?)JsonValue.Create(c)).ToArray()),
            ["avisos_confirmados"] = new JsonArray(o.AvisosConfirmados.Select(a => (JsonNode?)JsonValue.Create(a)).ToArray()),
            ["docx"] = new JsonObject { ["arquivo"] = Path.GetFileName(docx), ["sha256"] = RegistroModelos.Sha(bytesDocx), ["validacao"] = "OK" },
            ["pdf"] = pdf,
            ["sem_pdf_motivo"] = semPdfMotivo,
            ["paginas_conferidas"] = conferidas,
            ["usuario"] = u.Usuario,
            ["app_versao"] = o.AppVersao,
            ["gerado_em"] = agora,
        };
    }
}
