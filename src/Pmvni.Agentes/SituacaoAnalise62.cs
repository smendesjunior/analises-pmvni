using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Pmvni.Agentes;

/// <summary>Uma opção de uma decisão do analista: o valor que volta ao kit e o texto que a tela mostra.</summary>
public sealed record OpcaoDecisao(string Valor, string Texto);

/// <summary>O que o kit precisa que o analista decida (contrato da Tarefa 2, campo <c>decisao</c>): o tipo (por exemplo <c>pecas</c>), a pergunta, as opções
/// (vazia = resposta livre), onde gravar a resposta (<c>arquivo_resposta</c>; relativo = dentro da pasta de controle do kit) e o JSON original, para quem
/// precisar de campos próprios do tipo (a escolha de peças da Tarefa 14).</summary>
public sealed record DecisaoAnalise(string Tipo, string? Id, string Pergunta, IReadOnlyList<OpcaoDecisao> Opcoes, string? ArquivoResposta, string Json);

/// <summary>A última linha da saída do <c>executar_analise_62.py</c> (contrato da Tarefa 2): <c>{"situacao": "ENTREGUE|ETAPA_OK|AGUARDANDO_COTA|DECISAO|FALHA",
/// "etapa": str, "retomar_em_utc": str|null, "motivo": str, "decisao": obj|null}</c>, com os códigos de saída 0 (entregue ou etapa concluída), 20 (aguardando
/// cota), 21 (decisão do analista) e 1 (falha). Chave faltando ou de tipo errado = null; linha ilegível = <see cref="Situacao"/> vazia.</summary>
public sealed record SituacaoAnalise62(string Situacao, string? Etapa, DateTimeOffset? RetomarEmUtc, string? Motivo, DecisaoAnalise? Decisao)
{
    public const string Entregue = "ENTREGUE";
    public const string EtapaOk = "ETAPA_OK";
    public const string AguardandoCota = "AGUARDANDO_COTA";
    /// <summary>"DECISAO" (o nome <c>Decisao</c> já é o do campo com a pergunta).</summary>
    public const string PedeDecisao = "DECISAO";
    public const string Falha = "FALHA";

    public const int CodigoOk = 0;
    public const int CodigoCota = 20;
    public const int CodigoDecisao = 21;
    public const int CodigoFalha = 1;

    static readonly SituacaoAnalise62 Vazia = new("", null, null, null, null);

    /// <summary>"Aguardando cota até HH:MM" (hora local deste computador), o texto do Monitor durante a espera.</summary>
    public static string TextoAguardandoCota(DateTimeOffset retomarEmUtc) =>
        "Aguardando cota até " + retomarEmUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture);

    public static SituacaoAnalise62 Ler(string ultimaLinha)
    {
        if (string.IsNullOrWhiteSpace(ultimaLinha)) return Vazia;
        JsonNode? raiz;
        try { raiz = JsonNode.Parse(ultimaLinha); }
        catch (JsonException) { return Vazia; }
        if (raiz is not JsonObject o || Texto(o["situacao"]) is not { } situacao) return Vazia;
        return new SituacaoAnalise62(situacao, Texto(o["etapa"]), Data(Texto(o["retomar_em_utc"])), Texto(o["motivo"]), LerDecisao(o["decisao"]));
    }

    /// <summary>A situação nas linhas da saída: a última linha não vazia, se for o JSON do contrato; senão o último bloco que começa numa linha "{" e vai até o
    /// fim (o controlador 3.0.1 imprimia o JSON com recuo). null = a saída não termina com a situação.</summary>
    public static SituacaoAnalise62? DaSaida(IReadOnlyList<string> linhas) => JsonDaSaida(linhas) is { } json ? Ler(json) : null;

    /// <summary>O texto do JSON da situação no fim da saída (o que <see cref="DaSaida"/> lê); null se não houver.</summary>
    public static string? JsonDaSaida(IReadOnlyList<string> linhas)
    {
        var ultima = linhas.Count - 1;
        while (ultima >= 0 && string.IsNullOrWhiteSpace(linhas[ultima])) ultima--;
        if (ultima < 0) return null;
        var texto = linhas[ultima].Trim();
        if (Ler(texto).Situacao.Length > 0) return texto;
        for (var i = ultima; i >= 0 && ultima - i < 400; i--)
        {
            if (!linhas[i].StartsWith('{')) continue;
            var bloco = string.Join('\n', linhas.Skip(i).Take(ultima - i + 1));
            return Ler(bloco).Situacao.Length > 0 ? bloco : null;
        }
        return null;
    }

    static DecisaoAnalise? LerDecisao(JsonNode? n)
    {
        if (n is not JsonObject d) return null;
        var opcoes = new List<OpcaoDecisao>();
        if (d["opcoes"] is JsonArray lista)
            foreach (var item in lista)
            {
                if (Texto(item) is { } s) opcoes.Add(new OpcaoDecisao(s, s));
                else if (item is JsonObject x && (Texto(x["valor"]) ?? Texto(x["id"])) is { } valor)
                    opcoes.Add(new OpcaoDecisao(valor, Texto(x["texto"]) ?? Texto(x["rotulo"]) ?? valor));
            }
        var pergunta = Texto(d["pergunta"]) ?? Texto(d["texto"]) ?? Texto(d["motivo"]) ?? "O kit precisa de uma decisão sua para continuar a análise.";
        return new DecisaoAnalise(Texto(d["tipo"]) ?? "generica", Texto(d["id"]), pergunta, opcoes,
            Texto(d["arquivo_resposta"]) ?? Texto(d["resposta_em"]), d.ToJsonString());
    }

    static string? Texto(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;

    /// <summary>O kit grava em UTC (<c>Z</c>); sem fuso, vale UTC.</summary>
    static DateTimeOffset? Data(string? texto) =>
        texto is not null && DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d.ToUniversalTime() : null;
}

/// <summary>Onde e como o editor grava a resposta de uma decisão: só dentro da pasta de controle do kit do provedor (um caminho de fora vira o padrão
/// <c>decisao_resposta.json</c>), num JSON com o tipo, o id, a resposta e a hora.</summary>
public static class RespostaDecisao
{
    public const string ArquivoPadrao = "decisao_resposta.json";

    public static string Caminho(string pastaProcesso, Provedor p, DecisaoAnalise d, string? pastaRegistro = null)
    {
        var controle = Path.GetFullPath(Path.Combine(pastaProcesso, ResultadoAnalise.PastaControle(p)));
        var padrao = Path.Combine(controle, ArquivoPadrao);
        if (string.IsNullOrWhiteSpace(d.ArquivoResposta)) return padrao;
        try
        {
            var c = Path.GetFullPath(Path.IsPathRooted(d.ArquivoResposta) ? d.ArquivoResposta : Path.Combine(controle, d.ArquivoResposta));
            if (d.Tipo == "modelos_indisponiveis" && pastaRegistro is not null && Guid.TryParseExact(d.Id, "N", out var id))
            {
                var esperado = Path.GetFullPath(Path.Combine(pastaRegistro, "decisoes", $"modelos_indisponiveis-{id:N}.json"));
                if (string.Equals(c, esperado, StringComparison.OrdinalIgnoreCase)) return esperado;
            }
            return c.StartsWith(controle + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? c : padrao;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return padrao; }
    }

    /// <summary>Grava a resposta de uma vez (arquivo novo ao lado, depois troca) e devolve o caminho.</summary>
    public static string Gravar(string pastaProcesso, Provedor p, DecisaoAnalise d, string resposta, string? pastaRegistro = null)
    {
        var arquivo = Caminho(pastaProcesso, p, d, pastaRegistro);
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        var obj = new JsonObject
        {
            ["tipo"] = d.Tipo, ["id"] = d.Id, ["resposta"] = resposta,
            ["respondido_em_utc"] = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture), ["respondido_por"] = "analista (Editor de Relatórios)",
        };
        var tmp = arquivo + ".tmp";
        File.WriteAllText(ArquivoGerado.Caminho(tmp), obj.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            new System.Text.UTF8Encoding(false));
        File.Move(tmp, ArquivoGerado.Caminho(arquivo), overwrite: true);
        return arquivo;
    }
}
