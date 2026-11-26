using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Pmvni.Composicao.Kit;

namespace Pmvni.Composicao;

public sealed record TextoDoModelo(string Original, string Atual, bool Visivel);

/// <summary>Endereça os textos impressos pela posição no modelo e pela identidade de cada ocorrência nas listas.</summary>
internal sealed class TextosDoModelo(IReadOnlyDictionary<string, object?> revisoes)
{
    static readonly Regex Blocos = new(
        """(<!--.*?-->|\{#.*?#\}|\{%.*?%\}|\{\{.*?\}\}|<[A-Za-z!/](?:"[^"]*"|'[^']*'|[^'">])*>)""",
        RegexOptions.Singleline | RegexOptions.CultureInvariant);

    public Dictionary<string, TextoDoModelo> Campos { get; } = new(StringComparer.Ordinal);

    internal static string Instrumentar(string modelo)
    {
        var inicio = modelo.IndexOf("<body>", StringComparison.Ordinal);
        var fim = modelo.LastIndexOf("</body>", StringComparison.Ordinal);
        if (inicio < 0 || fim < inicio) return modelo;
        inicio += "<body>".Length;
        var resultado = new StringBuilder(modelo[..inicio]);
        var i = 0;
        foreach (var token in Blocos.Split(modelo[inicio..fim]))
        {
            if (string.IsNullOrWhiteSpace(token) || token.StartsWith('<') || token.StartsWith("{%") || token.StartsWith("{#"))
            {
                resultado.Append(token);
                continue;
            }
            var slot = (++i).ToString(CultureInfo.InvariantCulture);
            if (token.StartsWith("{{"))
            {
                var expr = token[2..^2].Trim();
                if (expr.StartsWith("loop.") || expr.Split('|').Skip(1).Any(f => f.Trim() is "safe" or "pill" or "pill_vao" or "icone"))
                    resultado.Append(token);
                else
                    resultado.Append("{% texto_variavel ").Append(slot).Append(' ').Append(expr).Append(" %}");
            }
            else
            {
                // O texto já é HTML do modelo. O valor revisado será escapado ao renderizar.
                var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
                resultado.Append("{% texto_fixo ").Append(slot).Append(' ').Append(payload).Append(" %}");
            }
        }
        return resultado.Append(modelo.AsSpan(fim)).ToString();
    }

    internal void Anexar(StringBuilder html, string slot, string ocorrencia, string originalHtml)
    {
        var original = WebUtility.HtmlDecode(originalHtml).Trim();
        if (original.Length == 0)
        {
            html.Append(originalHtml);
            return;
        }
        var chave = slot + ocorrencia;
        var revisao = Dados.Obter(revisoes, chave) as IReadOnlyDictionary<string, object?>;
        var revisado = revisao is null ? null : Dados.Obter(revisao, "texto") as string;
        var visivel = revisao is null || !Equals(Dados.Obter(revisao, "visivel"), false);
        Campos[chave] = new(original, revisado ?? original, visivel);
        html.Append("<pmvni-texto data-texto-modelo=\"").Append(PythonTexto.Escape(chave)).Append('"');
        if (!visivel) html.Append(" style=\"display:none\"");
        html.Append('>');
        if (revisado is null) html.Append(originalHtml);
        else
        {
            html.Append(originalHtml.AsSpan(0, originalHtml.Length - originalHtml.TrimStart().Length));
            html.Append(PythonTexto.Escape(revisado));
            html.Append(originalHtml.AsSpan(originalHtml.TrimEnd().Length));
        }
        html.Append("</pmvni-texto>");
    }
}
