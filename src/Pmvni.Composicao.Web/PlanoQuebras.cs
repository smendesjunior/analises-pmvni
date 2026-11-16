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
/// com <see cref="Linha"/>, na linha de índice <see cref="Linha"/> (0 = primeira) dos tbody da tabela.
/// <see cref="Forcada"/>: a quebra que abre a folha é forçada (break-before/after: page…), então a margem de cima do
/// 1º bloco fica; nas não forçadas ela é truncada (sempre falso na 1ª folha).
/// Igualdade por valor, inclusive do <see cref="Caminho"/> (elemento a elemento).</summary>
public sealed record PosicaoFluxo(int[] Caminho, int? DeslocamentoTexto, int? Linha, bool Forcada)
{
    public bool Equals(PosicaoFluxo? outra) =>
        outra is not null && Caminho.AsSpan().SequenceEqual(outra.Caminho) && DeslocamentoTexto == outra.DeslocamentoTexto
        && Linha == outra.Linha && Forcada == outra.Forcada;

    public override int GetHashCode()
    {
        var h = new HashCode();
        foreach (var i in Caminho) h.Add(i);
        h.Add(DeslocamentoTexto);
        h.Add(Linha);
        h.Add(Forcada);
        return h.ToHashCode();
    }
}

/// <summary>Folha (1 = primeira) cujo conteúdo não coube na área útil; <see cref="Ref"/> é o id (data-ref) do bloco
/// ou, se o bloco não tiver, o do último bloco identificado antes dele. <see cref="AlturaMm"/>: altura do conteúdo da
/// folha. <see cref="LarguraMm"/>: só no estouro de largura (bloco mais largo que 1,5 × a área), a largura que ele pede;
/// null no estouro de altura.</summary>
public sealed record Estouro(int Folha, string? Ref, double AlturaMm, double? LarguraMm);

/// <summary>Bloco mais largo que a área: o documento sai reduzido pela escala de impressão. <see cref="Escala"/> é a que
/// o bloco pede sozinho (largura dele / largura da área); o editor avisa "documento reduzido".</summary>
public sealed record AvisoLargura(int Folha, string? Ref, double Escala, double LarguraMm);

/// <summary>Plano de quebras: onde começa cada folha, o que estourou e o hash do HTML para o qual foi calculado.
/// <see cref="Escala"/> é a escala de impressão k (1 a 1,5) do shrink-to-fit do Chromium: quando o conteúdo é mais largo
/// que a página, a área é diagramada k vezes maior e pintada em 1/k (folha.css, <c>--pmvni-k</c>); quem aplica o plano usa
/// este valor. <see cref="AvisosLargura"/> lista os blocos que causam a redução.
/// <see cref="Figuras"/>: imagens de figura reduzidas pelo paginador para caber na sobra da folha anterior (ele decide sozinho
/// quantas figuras cabem por folha); null = nenhuma.</summary>
public sealed record PlanoQuebras(int Versao, IReadOnlyList<PosicaoFluxo> InicioDasFolhas, IReadOnlyList<Estouro> Estouros,
    string Sha256Html, double Escala, IReadOnlyList<AvisoLargura> AvisosLargura, IReadOnlyList<AjusteFigura>? Figuras = null)
{
    public const int VersaoAtual = 1;
    public const double EscalaMaxima = 1.5;

    public string ParaJson() => JsonSerializer.Serialize(this, Mensagens.Opcoes);

    /// <summary>Lê e valida o plano como o <c>lerPlano</c> do paginador: versão suportada, escala finita em [1; 1,5], listas
    /// presentes, ao menos uma folha, caminhos não vazios de índices ≥ 0 e deslocamento/linha ≥ 0 (ou nulos).</summary>
    public static PlanoQuebras DeJson(JsonElement json)
    {
        var p = json.Deserialize<PlanoQuebras>(Mensagens.Opcoes) ?? throw new InvalidDataException("Plano de quebras vazio.");
        if (p.Versao != VersaoAtual) throw new InvalidDataException("Versão de plano não suportada: " + p.Versao);
        if (!double.IsFinite(p.Escala) || p.Escala < 1 || p.Escala > EscalaMaxima)
            throw new InvalidDataException("Escala de impressão inválida no plano: " + p.Escala);
        if (p.InicioDasFolhas is null || p.Estouros is null || p.AvisosLargura is null || string.IsNullOrEmpty(p.Sha256Html)
            || p.InicioDasFolhas.Count == 0 || p.InicioDasFolhas.Any(i => i?.Caminho is null))
            throw new InvalidDataException("Plano de quebras incompleto.");
        for (var n = 0; n < p.InicioDasFolhas.Count; n++)
        {
            var i = p.InicioDasFolhas[n];
            if (i.Caminho.Length == 0 || i.Caminho.Any(x => x < 0) || i.DeslocamentoTexto < 0 || i.Linha < 0)
                throw new InvalidDataException($"Plano de quebras inválido: inicioDasFolhas[{n}].");
        }
        return p;
    }

    /// <summary>sha256 (hex minúsculo) do HTML do renderizador, como string UTF-8 (com as quebras LF dele).</summary>
    public static string Sha256De(string html) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(html)));
}

/// <summary>Altura máxima (px CSS) da imagem (.fi img) da figura (.fig) no <see cref="Caminho"/> do fluxo.</summary>
public sealed record AjusteFigura(int[] Caminho, double AlturaPx);

/// <summary>Resposta de <c>compor</c>: o plano, o número de folhas e os data-ref visíveis em cada folha, na ordem.</summary>
public sealed record ResultadoComposicao(PlanoQuebras Plano, int Folhas, IReadOnlyList<IReadOnlyList<string>> RefsPorFolha);

/// <summary>Resposta de <c>aplicarPlano</c>: o número de folhas montadas (= inícios do plano), as folhas (1 = primeira) cujo
/// conteúdo passa da área útil em altura no layout atual e as que têm conteúdo além da borda esquerda ou direita da área
/// (<see cref="FolhasQueTransbordamNaLargura"/>: nada é cortado, mas sai fora da área útil). Só informativo: o conteúdo de
/// cada folha vem do plano, não destas medidas.</summary>
public sealed record ResultadoAplicacao(int Folhas, IReadOnlyList<int> FolhasQueTransbordam, IReadOnlyList<int> FolhasQueTransbordamNaLargura)
{
    /// <summary>Folhas com qualquer transbordamento (altura ou largura), em ordem: as que a tela marca com a faixa vermelha.</summary>
    public IReadOnlyList<int> FolhasMarcadas => FolhasQueTransbordam.Union(FolhasQueTransbordamNaLargura).Order().ToArray();
}
