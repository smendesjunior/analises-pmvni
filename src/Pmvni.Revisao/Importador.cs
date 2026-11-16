using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Composicao;

namespace Pmvni.Revisao;

/// <summary>O dados.json foi recusado na importação; <see cref="Erros"/> traz os erros da conferência do kit (ou o motivo, se o arquivo
/// nem é um dados.json legível).</summary>
public sealed class ImportacaoRecusadaException(IReadOnlyList<string> erros) : Exception(string.Join("; ", erros))
{
    public IReadOnlyList<string> Erros { get; } = erros;
}

/// <summary>dados.json do kit → <see cref="ConteudoPacote"/> (um <see cref="ItemRevisao"/> por item, citações convertidas, imagens copiadas
/// para <c>assets/</c>). O dados.json recebido é guardado byte a byte e nunca muda.</summary>
public static class Importador
{
    public const string TipoHistorico = "ImportarCitacoes";

    public static ConteudoPacote Importar(byte[] dadosJson, string caminhoDados, ModeloCarregado m, ContextoUsuario u)
    {
        ArgumentNullException.ThrowIfNull(dadosJson);
        ArgumentNullException.ThrowIfNull(caminhoDados);
        ArgumentNullException.ThrowIfNull(m);
        ArgumentNullException.ThrowIfNull(u);

        JsonObject dados;
        try { dados = JsonFiel.Ler(dadosJson); }
        catch (JsonException e) { throw new ImportacaoRecusadaException(new[] { "O arquivo não é um dados.json válido: " + e.Message }); }

        ResultadoRender render;
        try { render = RelatorioRenderer.Renderizar(dadosJson, caminhoDados, new OpcoesRender(false, true), m); }
        catch (MoldeErro e) { throw new ImportacaoRecusadaException(new[] { "Os dados não têm a estrutura do relatório: " + e.Message }); }
        if (!render.Ok) throw new ImportacaoRecusadaException(render.Erros);

        var agora = u.Relogio();
        var origem = dados["itens"]!.AsArray();
        var ids = NovosIds(origem.Count, agora, u.Rng);
        var convertidas = new JsonArray();

        var projeto = new Projeto();
        for (var i = 0; i < origem.Count; i++)
            projeto.Itens.Add(ImportarItem(origem[i]!.AsObject(), ids[i], i + 1, ids, convertidas));

        foreach (var (chave, valor) in dados)
        {
            if (chave == "itens") projeto.Documento[chave] = new JsonArray();
            else if (chave == "conclusao" && valor is JsonValue v && v.TryGetValue<string>(out var texto))
                projeto.Documento[chave] = Converter(texto, ids, null, "conclusao", convertidas);
            else projeto.Documento[chave] = valor?.DeepClone();
        }

        var assets = CopiarImagens(projeto.Documento, Path.GetDirectoryName(Path.GetFullPath(caminhoDados))!);

        var historico = new[]
        {
            new RegistroHistorico(1, agora, u.Usuario, TipoHistorico, null, null, null, convertidas, null),
        };

        var partes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["original/recebido.json"] = Sha(dadosJson),
            ["projeto.json"] = Sha(ProjetoJson.Escrever(projeto)),
            ["historico.jsonl"] = Sha(HistoricoJsonl.Escrever(historico)),
        };
        foreach (var (chave, bytes) in assets.OrderBy(a => a.Key, StringComparer.Ordinal)) partes[chave] = Sha(bytes);

        var manifesto = new Manifesto(
            Manifesto.VersaoEsquemaAtual,
            VersaoDoApp(),
            int.Parse(m.Revisao, System.Globalization.CultureInfo.InvariantCulture),
            m.Sha256["relatorio.css"],
            agora,
            partes);

        return new ConteudoPacote(manifesto, dadosJson, projeto, historico, assets);
    }

    private static ItemRevisao ImportarItem(JsonObject o, string id, int numero, IReadOnlyList<string> ids, JsonArray convertidas)
    {
        var item = new ItemRevisao { Id = id, Ordem = numero, NumeroOriginal = numero, Incluido = true };
        foreach (var (chave, valor) in o)
        {
            item.Chaves.Add(chave);
            if (chave == "situacao") item.Situacao.Original = valor?.DeepClone();
            else if (Campos.Editaveis.Contains(chave))
            {
                var no = valor is JsonValue v && v.TryGetValue<string>(out var texto)
                    ? Converter(texto, ids, id, chave, convertidas)
                    : valor?.DeepClone();
                item.Campos[chave] = new CampoTexto { Original = no, Ausente = false };
            }
            else item.Outros[chave] = valor?.DeepClone();
        }
        return item;
    }

    /// <summary>Converte as citações do texto e registra cada uma em <paramref name="convertidas"/>; devolve o texto (ou o valor original).</summary>
    private static JsonNode Converter(string texto, IReadOnlyList<string> ids, string? item, string campo, JsonArray convertidas)
    {
        var (novo, lista) = Citacoes.Converter(texto, ids);
        foreach (var c in lista)
            convertidas.Add(new JsonObject { ["item"] = item, ["campo"] = campo, ["trecho"] = c.Trecho, ["alvo"] = c.Alvo });
        return JsonValue.Create(novo)!;
    }

    private static List<string> NovosIds(int quantos, DateTimeOffset agora, RandomNumberGenerator rng)
    {
        var ids = new List<string>(quantos);
        var usados = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < quantos; i++)
        {
            string id;
            var tentativas = 0;
            do
            {
                if (++tentativas > 1000) throw new InvalidOperationException("Não foi possível sortear ids de itens distintos.");
                id = IdItem.Novo(agora, rng);
            } while (!usados.Add(id));
            ids.Add(id);
        }
        return ids;
    }

    /// <summary>Copia a imagem da localização e as das figuras para <c>assets/&lt;sha256&gt;.&lt;ext&gt;</c> e reescreve o caminho no documento.</summary>
    private static Dictionary<string, byte[]> CopiarImagens(JsonObject documento, string pastaDados)
    {
        var assets = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var jaCopiadas = new Dictionary<string, string>(StringComparer.Ordinal);
        var erros = new List<string>();

        void Reescrever(JsonNode? dono)
        {
            if (dono is not JsonObject obj || obj["imagem"] is not JsonValue v || !v.TryGetValue<string>(out var rel)) return;
            if (!jaCopiadas.TryGetValue(rel, out var destino))
            {
                // A conferência do render já recusou caminho absoluto, de rede ou fora da pasta do processo; esta é a segunda barreira.
                if (CaminhoImagem.Resolver(pastaDados, rel, soNaPasta: false) is not { } arquivo)
                {
                    erros.Add("Caminho de imagem recusado (absoluto, de rede ou fora da pasta do processo): " + rel + ".");
                    return;
                }
                // O limite de uma entrada do pacote (50 MB): uma imagem maior nunca entraria no .rpmvni, e não é lida inteira na memória.
                var limite = new LimitesPacote().PorEntrada;
                if (new FileInfo(arquivo).Length > limite)
                {
                    erros.Add("Imagem maior que o limite de " + limite / (1024 * 1024) + " MB: " + rel + ".");
                    return;
                }
                var bytes = File.ReadAllBytes(arquivo);
                var ext = ExtensaoPelaAssinatura(bytes);
                if (ext is null)
                {
                    erros.Add("Imagem em formato não aceito: " + rel + " (só PNG, JPEG, WebP ou GIF).");
                    return;
                }
                destino = "assets/" + Sha(bytes) + "." + ext;
                assets[destino] = bytes;
                jaCopiadas[rel] = destino;
            }
            obj["imagem"] = destino;
        }

        Reescrever(documento["localizacao"]);
        if (documento["figuras"] is JsonArray figuras) foreach (var f in figuras) Reescrever(f);

        if (erros.Count > 0) throw new ImportacaoRecusadaException(erros);
        return assets;
    }

    /// <summary>Extensão normalizada (<c>png</c>, <c>jpg</c>, <c>webp</c>, <c>gif</c>) pela assinatura dos bytes; null se for outro formato.</summary>
    internal static string? ExtensaoPelaAssinatura(ReadOnlySpan<byte> b) => CaminhoImagem.ExtensaoPelaAssinatura(b);

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static string VersaoDoApp()
    {
        var v = typeof(Importador).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(v)) return typeof(Importador).Assembly.GetName().Version?.ToString() ?? "0";
        var mais = v.IndexOf('+');
        return mais >= 0 ? v[..mais] : v;
    }
}
