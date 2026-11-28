using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Legislacao;

public static class NormaDoKit
{
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static byte[] Criar(string arquivo, string hash, IReadOnlyList<string> paginas, bool licenciada, string? titulo = null)
    {
        var dispositivos = licenciada ? [] : LeituraPadronizada.Estruturar(paginas)
            .Where(b => b.Tipo is not (TipoBlocoLei.Texto or TipoBlocoLei.Figura))
            .Select(b => new { tipo = b.Tipo.ToString(), rotulo = b.Rotulo, pagina = b.Pagina }).ToArray();
        return JsonSerializer.SerializeToUtf8Bytes(new {
            esquema = "pmvni.norma.v1", arquivo, titulo = titulo ?? Path.GetFileNameWithoutExtension(arquivo), sha256 = hash, total_paginas = paginas.Count,
            modo = licenciada ? "referencia_licenciada" : "texto_por_pagina",
            paginas = licenciada ? [] : paginas.Select((texto, i) => new { pagina = i + 1, texto, sem_texto_pesquisavel = string.IsNullOrWhiteSpace(texto) }).ToArray(),
            dispositivos
        }, Json);
    }

    static string Hash(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(f));
    }

    static void Gravar(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(tmp, bytes); File.Move(tmp, path, true); }
        finally { File.Delete(tmp); }
    }

    // O catálogo do Editor é a fila durável. O recibo local só é gravado depois da publicação no inventário.
    public static void Sincronizar(string kit, AcervoLegislacao acervo, Anexo anexo)
    {
        kit = Path.GetFullPath(kit);
        var pasta = Path.Combine(kit, "03 - LEGISLACAO");
        var inventario = Path.Combine(kit, "00 - INDICE DO KIT", "indice_do_kit.json");
        var recibo = Path.Combine(acervo.Pasta, "indices-kit", anexo.Id + ".json");
        if (File.Exists(recibo))
        {
            JsonNode? r;
            try { r = JsonNode.Parse(File.ReadAllText(recibo)); } catch (JsonException) { r = null; }
            if (r?["kit"]?.ToString() == kit && r["sha256"]?.ToString() == anexo.Sha256 &&
                r["esquema"]?.ToString() == "1" && r["indice"]?.ToString() is { } path &&
                path.StartsWith(pasta + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path) && new FileInfo(path).Length == (long?)r["bytes"] &&
                File.GetLastWriteTimeUtc(path).Ticks == (long?)r["gravado"]) return;
        }
        var original = acervo.CaminhoPdf(anexo.Id);
        var paginas = acervo.TextoPorPagina(anexo.Id);
        if (original is null || paginas is null) return;
        var trava = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(kit.ToUpperInvariant())));
        using var mutex = new Mutex(false, @"Local\PMVNI-inventario-" + trava);
        try { if (!mutex.WaitOne(TimeSpan.FromSeconds(20))) throw new IOException("Inventário em uso."); }
        catch (AbandonedMutexException) { }
        try
        {
            if (!acervo.Listar().Any(a => a.Id == anexo.Id && a.Sha256 == anexo.Sha256)) return;
            var indice = JsonNode.Parse(File.ReadAllText(inventario))!.AsObject();
            var arquivos = indice["arquivos"]!.AsArray();
            var fonte = arquivos.OfType<JsonObject>().FirstOrDefault(x =>
                x["caminho"]?.ToString().StartsWith("03 - LEGISLACAO/", StringComparison.OrdinalIgnoreCase) == true &&
                x["tipo"]?.ToString() == "pdf" && string.Equals(x["sha256"]?.ToString(), anexo.Sha256, StringComparison.OrdinalIgnoreCase));
            bool restrita = fonte?["situacao"]?.ToString().Contains("LICENCIADO", StringComparison.OrdinalIgnoreCase) == true ||
                Regex.IsMatch(anexo.ArquivoOriginal, @"\b(ABNT|NBR)\b", RegexOptions.IgnoreCase);
            string pdf = fonte is null ? Path.Combine(pasta, "IMPORTADAS PELO EDITOR", anexo.Id, restrita ? "referencia.pdf" : "norma.pdf") :
                Path.GetFullPath(Path.Combine(kit, fonte["caminho"]!.ToString()));
            if (!pdf.StartsWith(pasta + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Origem da norma fora do acervo.");
            if (fonte is not null && Hash(pdf) != anexo.Sha256.ToLowerInvariant()) throw new IOException("O PDF do kit mudou.");
            if (fonte is null && !restrita)
            {
                var bytes = File.ReadAllBytes(original);
                if (Convert.ToHexStringLower(SHA256.HashData(bytes)) != anexo.Sha256.ToLowerInvariant()) throw new IOException("A cópia da lei mudou.");
                Gravar(pdf, bytes);
                Registrar(pdf, "pdf", anexo.Titulo);
            }
            var sidecar = Path.ChangeExtension(pdf, ".json");
            Gravar(sidecar, Criar(fonte is null && restrita ? anexo.ArquivoOriginal : Path.GetFileName(pdf), anexo.Sha256.ToLowerInvariant(), paginas, restrita, anexo.Titulo));
            Registrar(sidecar, "json", "Índice de consulta da norma " + anexo.Titulo);
            indice["total_arquivos"] = arquivos.Count;
            Gravar(inventario, JsonSerializer.SerializeToUtf8Bytes(indice, Json));
            Gravar(recibo, JsonSerializer.SerializeToUtf8Bytes(new { kit, sha256 = anexo.Sha256, esquema = 1,
                indice = sidecar, bytes = new FileInfo(sidecar).Length, gravado = File.GetLastWriteTimeUtc(sidecar).Ticks }));

            void Registrar(string path, string tipo, string descricao)
            {
                var rel = Path.GetRelativePath(kit, path).Replace('\\', '/');
                var item = arquivos.OfType<JsonObject>().FirstOrDefault(x => string.Equals(x["caminho"]?.ToString(), rel, StringComparison.OrdinalIgnoreCase));
                if (item is null) { item = new JsonObject(); arquivos.Add(item); }
                var info = new FileInfo(path);
                item["caminho"] = rel;
                item["sha256"] = Hash(path);
                item["tipo"] = tipo;
                item["tamanho_kb"] = ((info.Length + 1023) / 1024).ToString();
                item["o_que_e"] = descricao;
                item["situacao"] = "MANTER";
                item["marca"] = $"{info.Length}:{new DateTimeOffset(info.LastWriteTimeUtc).ToUnixTimeSeconds()}";
            }
        }
        finally { mutex.ReleaseMutex(); }
    }
}
