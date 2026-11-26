using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

public sealed record DefinirTextoDoModelo(string Chave, string? Texto, bool Visivel) : Comando("modelo");
public sealed record DefinirVisibilidadeDoCampo(string Alvo, string Campo, bool Visivel) : Comando(Alvo);

public static class TextosRevisados
{
    static readonly Regex ChaveValida = new(@"^(?:[1-9][0-9]*(?:/[A-Za-z0-9_.~%-]+)*|cabecalho|rodape-esquerda|rodape-direita)$", RegexOptions.CultureInvariant);

    internal static Efeito? Calcular(Projeto projeto, DefinirTextoDoModelo comando)
    {
        if (comando.Chave.Length > 256 || !ChaveValida.IsMatch(comando.Chave))
            throw new ComandoRecusadoException("O campo do modelo não é reconhecido.");
        var antes = projeto.TextosDoModelo[comando.Chave];
        var texto = comando.Texto is null ? null : TextoEditado.Normalizar(comando.Texto);
        if (comando.Chave == "rodape-direita" && texto is not null &&
            (!texto.Contains("{n}", StringComparison.Ordinal) || !texto.Contains("{t}", StringComparison.Ordinal)))
            throw new ComandoRecusadoException("O texto da paginação precisa incluir {n} para a página atual e {t} para o total.");
        JsonObject? depois = texto is not null || !comando.Visivel
            ? new JsonObject { ["texto"] = texto, ["visivel"] = comando.Visivel }
            : null;
        return JsonNode.DeepEquals(antes, depois) ? null
            : new Efeito(nameof(DefinirTextoDoModelo), comando.Item, comando.Chave, antes?.DeepClone(), depois);
    }

    internal static void Definir(Projeto projeto, string chave, JsonNode? valor)
    {
        if (valor is null) projeto.TextosDoModelo.Remove(chave);
        else projeto.TextosDoModelo[chave] = valor.DeepClone();
    }

    const string PrefixoCampo = "campo/";

    static string ChaveCampo(string alvo, string campo) =>
        PrefixoCampo + Uri.EscapeDataString(alvo) + "/" + Uri.EscapeDataString(campo);

    public static bool CampoVisivel(Projeto p, string alvo, string campo) => !p.TextosDoModelo.ContainsKey(ChaveCampo(alvo, campo));

    public static IReadOnlyList<(string Alvo, string Campo)> CamposOcultos(Projeto p) =>
        p.TextosDoModelo.Select(o => o.Key.Split('/')).Where(k => k.Length == 3 && k[0] == "campo")
            .Select(k => (Uri.UnescapeDataString(k[1]), Uri.UnescapeDataString(k[2]))).ToArray();

    internal static Efeito? Calcular(Projeto p, DefinirVisibilidadeDoCampo comando)
    {
        if (comando.Alvo.Length > 128 || comando.Campo.Length > 128)
            throw new ComandoRecusadoException("O campo não é reconhecido.");
        var chave = ChaveCampo(comando.Alvo, comando.Campo);
        var antes = p.TextosDoModelo[chave];
        var depois = comando.Visivel ? null : JsonValue.Create(false);
        return JsonNode.DeepEquals(antes, depois) ? null
            : new Efeito(nameof(DefinirVisibilidadeDoCampo), comando.Alvo, chave, antes?.DeepClone(), depois);
    }
}
