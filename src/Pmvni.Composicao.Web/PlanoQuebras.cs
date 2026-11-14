using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Pmvni.Composicao.Web.Ponte;

namespace Pmvni.Composicao.Web;

// Espelho exato de web/paginador/src/plano.ts. O JSON usa os nomes em camelCase (JsonSerializerDefaults.Web) e
// grava os nulos, como o lado TS; o teste plano_json_ida_e_volta confere a ida e volta.

/// <summary>Posição do primeiro conteúdo de uma folha no fluxo original (filhos do &lt;body&gt; do relatório).
/// <see cref="Caminho"/>: índices de filhos-elemento a partir do fluxo (ex.: [7, 2] = 3º filho do 8º bloco).
/// Sem <see cref="DeslocamentoTexto"/> nem <see cref="Linha"/>, a folha começa no início desse elemento;
/// com <see cref="DeslocamentoTexto"/>, no caractere (UTF-16, no textContent do elemento) que abre a linha;
/// com <see cref="Linha"/>, na linha de índice <see cref="Linha"/> (0 = primeira) dos tbody da tabela.</summary>
public sealed record PosicaoFluxo(int[] Caminho, int? DeslocamentoTexto, int? Linha);

/// <summary>Folha (1 = primeira) cujo conteúdo não coube na área útil; <see cref="Ref"/> é o id (data-ref) do bloco
/// ou, se o bloco não tiver, o do último bloco identificado antes dele.</summary>
public sealed record Estouro(int Folha, string? Ref, double AlturaMm);

/// <summary>Plano de quebras: onde começa cada folha, o que estourou e o hash do HTML para o qual foi calculado.
/// <see cref="Escala"/> é a escala de impressão k (>= 1) do shrink-to-fit do Chromium: quando o conteúdo é mais largo que a
/// página, a área é diagramada k vezes maior e pintada em 1/k (folha.css, <c>--pmvni-k</c>); quem aplica o plano usa este valor.</summary>
public sealed record PlanoQuebras(int Versao, IReadOnlyList<PosicaoFluxo> InicioDasFolhas, IReadOnlyList<Estouro> Estouros, string Sha256Html, double Escala)
{
    public const int VersaoAtual = 1;

    public string ParaJson() => JsonSerializer.Serialize(this, Mensagens.Opcoes);

    public static PlanoQuebras DeJson(JsonElement json) =>
        json.Deserialize<PlanoQuebras>(Mensagens.Opcoes) ?? throw new InvalidDataException("Plano de quebras vazio.");

    /// <summary>sha256 (hex minúsculo) do HTML do renderizador, como string UTF-8 (com as quebras LF dele).</summary>
    public static string Sha256De(string html) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(html)));
}

/// <summary>Resposta de <c>compor</c>: o plano, o número de folhas e os data-ref visíveis em cada folha, na ordem.</summary>
public sealed record ResultadoComposicao(PlanoQuebras Plano, int Folhas, IReadOnlyList<IReadOnlyList<string>> RefsPorFolha);
