using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>"Ajustar com IA" na caixa de edição da prévia do despacho ("Texto da exigência"): a IA da revisão (Codex com o modelo e o
/// esforço de Ajustes, ou o Claude Code) devolve SÓ o texto melhorado — mais claro e formal, com o mesmo sentido, sem fato novo. O texto
/// volta para a caixa, selecionado: o usuário confere e grava com Enter, ou desfaz com Ctrl+Z / Esc.</summary>
internal static partial class IaAjusteTexto
{
    static readonly JsonSerializerOptions Relaxado = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>O pedido: regras de redação do despacho, o contexto (grupo, título do item, fundamento) e o texto atual, como dados JSON.</summary>
    internal static string Prompt(string texto, string? grupo = null, string? tituloItem = null, string? fundamento = null)
    {
        ArgumentNullException.ThrowIfNull(texto);
        var sb = new StringBuilder();
        sb.Append("Você revisa a redação de UMA exigência de despacho de correção da Secretaria Municipal de Obras de Venda Nova do ")
          .Append("Imigrante/ES. Tudo o que você precisa está neste pedido: NÃO abra arquivos, NÃO rode comandos e NÃO pesquise; responda direto.\n\n");
        sb.Append("Regras:\n")
          .Append("1. Deixe o texto mais claro, objetivo e formal (português do Brasil, estilo técnico e neutro), com o MESMO sentido.\n")
          .Append("2. Não invente medidas, artigos, leis, documentos nem fatos: use só o que está no texto. Mantenha números, unidades, ")
          .Append("citações de lei e referências \"item NN\" exatamente como estão.\n")
          .Append("3. Tamanho parecido com o original; uma frase ou poucas frases, sem quebras de linha, sem lista, sem título.\n")
          .Append("4. O contexto abaixo serve só para entender o texto; não o copie para dentro da exigência.\n\n");
        if (!string.IsNullOrWhiteSpace(grupo)) sb.Append("Grupo (contexto): ").Append(JsonSerializer.Serialize(grupo.Trim(), Relaxado)).Append('\n');
        if (!string.IsNullOrWhiteSpace(tituloItem)) sb.Append("Item do relatório (contexto): ").Append(JsonSerializer.Serialize(tituloItem.Trim(), Relaxado)).Append('\n');
        if (!string.IsNullOrWhiteSpace(fundamento)) sb.Append("Fundamento (contexto): ").Append(JsonSerializer.Serialize(fundamento.Trim(), Relaxado)).Append('\n');
        sb.Append("Texto atual da exigência (dado JSON): ").Append(JsonSerializer.Serialize(texto, Relaxado)).Append("\n\n");
        sb.Append("Responda SOMENTE com o texto melhorado, sem aspas, sem comentários e sem explicação.");
        return sb.ToString();
    }

    [GeneratedRegex(@"^\s*```[^\n]*\n(?<c>.*?)\n?```\s*$", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Cercado();

    [GeneratedRegex(@"^\s*(texto\s+(melhorado|ajustado|revisado)|exig[êe]ncia)\s*:\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Rotulo();

    /// <summary>O texto devolvido pela IA, limpo: sem bloco ```, sem rótulo ("Texto melhorado:"), sem aspas em volta, numa linha só.
    /// null = resposta vazia (nada muda na caixa).</summary>
    internal static string? Ler(string? saida)
    {
        if (string.IsNullOrWhiteSpace(saida)) return null;
        var s = saida.Trim();
        if (Cercado().Match(s) is { Success: true } m) s = m.Groups["c"].Value.Trim();
        s = Rotulo().Replace(s, "");
        s = TextoEditado.Normalizar(s);
        while (s.Length >= 2 && ((s[0] == '"' && s[^1] == '"') || (s[0] == '“' && s[^1] == '”') || (s[0] == '«' && s[^1] == '»')))
            s = s[1..^1].Trim();
        return s.Length == 0 ? null : s;
    }
}
