using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.App;

/// <summary>"Ajustar o item com a IA" (correções 2026-11-16): o usuário escreveu a justificativa (com ou sem mudar a situação) e a IA
/// reescreve o item INTEIRO — verificação, providência, base legal, referência e, se a justificativa pedir e o revisor não tiver fixado
/// a situação, a própria situação. O pedido leva tudo o que a IA precisa (ela não abre arquivo nenhum: era isso que demorava), impõe o
/// tamanho de cada campo (o do original) e proíbe narrar a revisão; a resposta é conferida por <see cref="Problemas"/> antes de entrar.</summary>
internal static partial class IaAjusteItem
{
    internal static readonly string[] Chaves = ["titulo", "verificacao", "providencia", "base_legal", "referencia"];

    /// <summary>As situações de item que a IA pode devolver (as do kit).</summary>
    internal static readonly string[] Situacoes = ["ATENDE", "NAO_ATENDE", "NAO_COMPROVADO", "DIVERGENCIA_INTERNA"];

    /// <summary>Folga da conferência: um campo até 1,3× o limite ainda entra; acima disso a resposta volta para encurtar.</summary>
    internal const double Folga = 1.3;

    /// <summary>Tamanho mínimo do limite de cada campo: um original muito curto ("Nenhuma correção.") precisa de espaço para dizer a
    /// correção quando a situação muda; ainda assim é uma ou duas frases.</summary>
    static readonly Dictionary<string, int> Pisos = new(StringComparer.Ordinal)
    {
        ["titulo"] = 60, ["verificacao"] = 160, ["providencia"] = 140, ["base_legal"] = 100, ["referencia"] = 80,
    };

    /// <summary>O limite de caracteres de cada campo: o tamanho do texto original recebido (sem original, o do texto atual), nunca
    /// abaixo do piso do campo.</summary>
    internal static Dictionary<string, int> Limites(IReadOnlyDictionary<string, string> originais, IReadOnlyDictionary<string, string> atuais)
    {
        var r = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var k in Chaves)
        {
            var referencia = originais.TryGetValue(k, out var o) && o.Trim().Length > 0 ? o : atuais.TryGetValue(k, out var a) ? a : "";
            r[k] = Math.Max(referencia.Trim().Length, Pisos[k]);
        }
        return r;
    }

    /// <param name="situacaoOriginal">Código da situação recebida da análise (null = sem original: item criado pelo usuário).</param>
    /// <param name="situacaoAtual">Código da situação efetiva agora.</param>
    /// <param name="situacaoFixa">O revisor mudou a situação: ela está decidida e o texto tem de sustentá-la.</param>
    internal static string Prompt(string? situacaoOriginal, string situacaoAtual, bool situacaoFixa, string justificativa,
        IReadOnlyDictionary<string, string> campos, IReadOnlyDictionary<string, int> limites)
    {
        var atual = new JsonObject();
        foreach (var k in Chaves) atual[k] = campos.TryGetValue(k, out var v) ? v : "";
        var tamanhos = string.Join("; ", Chaves.Select(k => k + ": no máximo " + limites[k].ToString(CultureInfo.InvariantCulture) + " caracteres"));
        var sb = new StringBuilder();
        sb.Append("Você reescreve UM item de um relatório técnico de análise de projeto da Secretaria Municipal de Obras de Venda Nova do ")
          .Append("Imigrante/ES. Tudo o que você precisa está neste pedido: NÃO abra arquivos, NÃO rode comandos e NÃO pesquise; responda direto.\n\n");
        sb.Append("Situação atual do item: ").Append(situacaoAtual).Append(". ");
        if (situacaoFixa)
            sb.Append("O revisor humano definiu essa situação")
              .Append(situacaoOriginal is null ? "" : " (a análise tinha dado " + situacaoOriginal + ")")
              .Append("; ela está DECIDIDA: devolva \"situacao\": \"").Append(situacaoAtual).Append("\" e escreva o texto que a sustenta.\n");
        else
            sb.Append("O revisor não mudou a situação. Mantenha-a, a não ser que a instrução dele peça para rever ou torne outra situação ")
              .Append("evidente; nesse caso devolva a nova em \"situacao\" (").Append(string.Join(", ", Situacoes)).Append(").\n");
        sb.Append("Justificativa/instrução do revisor (dado JSON; é a razão da mudança, não um texto a copiar): ")
          .Append(JsonSerializer.Serialize(justificativa, Relaxado)).Append("\n\n");
        sb.Append("Regras:\n")
          .Append("1. Reajuste o item INTEIRO para ficar coerente com a situação final: a verificacao diz o que o projeto apresenta e o que ")
          .Append("a norma exige; a providencia diz o que o requerente deve fazer (se a situação final é ATENDE, a providencia é \"Nenhuma ")
          .Append("correção.\"; se não é, ela pede a correção ou o documento que falta); base_legal e referencia só mudam se a justificativa ")
          .Append("mandar ou se ficarem contraditórias com o novo texto.\n")
          .Append("2. TAMANHO: texto curto como o original, uma ou duas frases por campo. Limites: ").Append(tamanhos).Append(". Não passe deles.\n")
          .Append("3. Escreva o RESULTADO, não a história: não cite o revisor, a justificativa, a revisão, a análise anterior, a situação ")
          .Append("anterior nem os nomes das situações (ATENDE, NÃO ATENDE, FALTA COMPROVAR…). Nada de \"foi registrado como\", \"mantida a ")
          .Append("situação\", \"com base na justificativa\".\n")
          .Append("4. Não invente medidas, artigos, leis nem documentos: use só o que está no item e na justificativa. O que a justificativa ")
          .Append("afirma (por exemplo, que a lei mudou ou que a medida é aceita) entra como fato, em poucas palavras.\n")
          .Append("5. Estilo técnico, neutro, em português do Brasil; mantenha as citações \"item NN\" como estão.\n\n");
        sb.Append("Item atual (JSON):\n").Append(atual.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = Relaxado.Encoder }));
        sb.Append("\n\nResponda SOMENTE com um JSON com as chaves situacao, titulo, verificacao, providencia, base_legal e referencia, sem comentários.");
        return sb.ToString();
    }

    static readonly JsonSerializerOptions Relaxado = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>O segundo pedido, quando a resposta veio grande demais ou narrando a revisão: o mesmo pedido mais o que foi recusado.</summary>
    internal static string PromptCorrecao(string promptOriginal, IReadOnlyDictionary<string, string> resposta, IReadOnlyList<string> problemas)
    {
        var anterior = new JsonObject();
        foreach (var (k, v) in resposta) anterior[k] = v;
        return promptOriginal + "\n\nSua resposta anterior foi RECUSADA pelos motivos abaixo. Reescreva-a obedecendo às regras 2 e 3 " +
               "(encurte: corte explicações, histórico e ressalvas; fique só com o fato e a exigência).\nMotivos:\n- " + string.Join("\n- ", problemas) +
               "\nResposta anterior (JSON):\n" + anterior.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = Relaxado.Encoder }) +
               "\n\nResponda SOMENTE com o JSON corrigido.";
    }

    /// <summary>Os campos devolvidos (e "situacao", se veio com um código válido); só aceita um objeto com ao menos uma das chaves do item.</summary>
    internal static Dictionary<string, string>? Ler(string saida)
    {
        if (RespostaJson.Objeto(saida, o => Chaves.Any(o.ContainsKey)) is not { } o) return null;
        var r = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var k in Chaves)
            if (o[k] is JsonValue jv && jv.TryGetValue<string>(out var s)) r[k] = s;
        if (o["situacao"] is JsonValue sv && sv.TryGetValue<string>(out var sit) && Situacoes.Contains(sit.Trim())) r["situacao"] = sit.Trim();
        return r;
    }

    // "O item foi registrado como ATENDE com base na justificativa do revisor…", "Mantida a situação ATENDE…": a história da revisão.
    [GeneratedRegex(@"\brevisor(a|es)?\b|\bjustificativa\b|\bfoi\s+registrad[oa]\b|\bmantid[oa]\s+a\s+situa|\bsitua[çc][ãa]o\s+(anterior|original|foi|passou|mudou)|\ban[áa]lise\s+anterior\b",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Narrativa();

    // O nome de uma situação escrito em maiúsculas no meio do texto (como a etiqueta): "NÃO ATENDE", "ATENDE", "FALTA COMPROVAR".
    [GeneratedRegex(@"\b(N[ÃA]O ATENDE|ATENDE|FALTA COMPROVAR|INFORMA[ÇC][ÕO]ES DIVERGENTES)\b", RegexOptions.CultureInvariant)]
    private static partial Regex NomeDeSituacao();

    /// <summary>O que impede a resposta de entrar: campo maior que <see cref="Folga"/>× o limite, ou texto que narra a revisão / cita o
    /// nome de uma situação. Lista vazia = pode aplicar. Cada frase serve ao usuário e ao segundo pedido.</summary>
    internal static List<string> Problemas(IReadOnlyDictionary<string, string> resposta, IReadOnlyDictionary<string, int> limites)
    {
        var lista = new List<string>();
        foreach (var k in Chaves)
        {
            if (!resposta.TryGetValue(k, out var texto)) continue;
            var n = texto.Trim().Length;
            if (limites.TryGetValue(k, out var limite) && n > limite * Folga)
                lista.Add($"{Nome(k)}: {n} caracteres (o limite é {limite})");
            if (k == "titulo") continue;
            if (Narrativa().IsMatch(texto)) lista.Add($"{Nome(k)}: conta a história da revisão em vez do resultado");
            else if (NomeDeSituacao().IsMatch(texto)) lista.Add($"{Nome(k)}: cita o nome de uma situação");
        }
        return lista;
    }

    internal static string Nome(string chave) => chave switch
    {
        "titulo" => "Título",
        "verificacao" => "Verificação técnica",
        "providencia" => "Providência",
        "base_legal" => "Base legal",
        _ => "Referência",
    };
}
