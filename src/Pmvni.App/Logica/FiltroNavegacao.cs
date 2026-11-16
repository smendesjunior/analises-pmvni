using System.Globalization;
using System.Text;

namespace Pmvni.App;

/// <summary>Filtro rápido da lista de seções (correções 2026-11-15): Todos, Não atende, Falta comprovar, Divergentes, Atende.</summary>
public enum FiltroSituacao { Todos, NaoAtende, FaltaComprovar, Divergentes, Atende }

/// <summary>O que o filtro precisa de cada linha da lista de seções (sem depender da tela): o rótulo, se é seção (Capa, Localização,
/// Análise…), a situação efetiva do item (código do kit) e se ele foi alterado.</summary>
public readonly record struct EntradaNavegacao(string Rotulo, bool Secao, string? Situacao, bool Alterado);

/// <summary>Resultado do filtro: os índices visíveis (na ordem do documento), quantos itens de cada seção passam no filtro (índice da
/// seção → contagem) e quantos itens há de cada situação (para os botões do filtro).</summary>
public sealed record ResultadoFiltro(IReadOnlyList<int> Visiveis, IReadOnlyDictionary<int, int> ItensPorSecao, IReadOnlyDictionary<FiltroSituacao, int> ItensPorFiltro);

/// <summary>Painel de navegação da revisão (correções 2026-11-15): filtro por situação, pesquisa por texto ou situação, seções recolhíveis
/// com contador e "Somente alterados". Seções sem item que passe no filtro somem enquanto há filtro ou pesquisa; seção recolhida continua
/// visível (com o contador), só os itens dela somem.</summary>
public static class FiltroNavegacao
{
    public static readonly IReadOnlyList<FiltroSituacao> Ordem =
        [FiltroSituacao.Todos, FiltroSituacao.NaoAtende, FiltroSituacao.FaltaComprovar, FiltroSituacao.Divergentes, FiltroSituacao.Atende];

    public static string Rotulo(FiltroSituacao f) => f switch
    {
        FiltroSituacao.NaoAtende => "Não atende",
        FiltroSituacao.FaltaComprovar => "Falta comprovar",
        FiltroSituacao.Divergentes => "Divergentes",
        FiltroSituacao.Atende => "Atende",
        _ => "Todos",
    };

    /// <summary>Classe de cor do kit que o filtro escolhe (r, a, o, g); null em Todos.</summary>
    public static string? Classe(FiltroSituacao f) => f switch
    {
        FiltroSituacao.NaoAtende => "r",
        FiltroSituacao.FaltaComprovar => "a",
        FiltroSituacao.Divergentes => "o",
        FiltroSituacao.Atende => "g",
        _ => null,
    };

    /// <summary>A situação passa no filtro (Todos aceita qualquer uma, até nenhuma).</summary>
    public static bool Passa(FiltroSituacao f, string? situacao) => f == FiltroSituacao.Todos || EtiquetasSituacao.Classe(situacao) == Classe(f);

    /// <summary>Texto para comparar: sem acentos, minúsculo, espaços simples.</summary>
    public static string Normalizar(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        var espaco = false;
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(c)) { if (!espaco && sb.Length > 0) sb.Append(' '); espaco = true; continue; }
            espaco = false;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Aplica filtro, pesquisa, "Somente alterados" e seções recolhidas (pelo rótulo da seção).</summary>
    public static ResultadoFiltro Aplicar(IReadOnlyList<EntradaNavegacao> linhas, FiltroSituacao filtro, string? pesquisa, bool somenteAlterados,
        IReadOnlySet<string> recolhidas)
    {
        var termo = Normalizar(pesquisa);
        var ativo = filtro != FiltroSituacao.Todos || termo.Length > 0 || somenteAlterados;
        var porSecao = new Dictionary<int, int>();
        var porFiltro = Ordem.ToDictionary(f => f, _ => 0);
        var passa = new bool[linhas.Count];
        var secaoDe = new int[linhas.Count];
        var secao = -1;
        for (var i = 0; i < linhas.Count; i++)
        {
            var l = linhas[i];
            if (l.Secao) { secao = i; porSecao[i] = 0; secaoDe[i] = -1; continue; }
            secaoDe[i] = secao;
            if (l.Situacao is not null && EtiquetasSituacao.Classe(l.Situacao) is not null)
            {
                porFiltro[FiltroSituacao.Todos]++;
                foreach (var f in Ordem)
                    if (f != FiltroSituacao.Todos && Passa(f, l.Situacao)) porFiltro[f]++;
            }
            passa[i] = Passa(filtro, l.Situacao) && (filtro == FiltroSituacao.Todos || l.Situacao is not null)
                       && (!somenteAlterados || l.Alterado)
                       && (termo.Length == 0 || Normalizar(l.Rotulo + " " + EtiquetasSituacao.Texto(l.Situacao)).Contains(termo, StringComparison.Ordinal));
            if (passa[i] && secao >= 0) porSecao[secao]++;
        }
        var visiveis = new List<int>();
        for (var i = 0; i < linhas.Count; i++)
        {
            var l = linhas[i];
            if (l.Secao)
            {
                var tituloBate = termo.Length > 0 && !somenteAlterados && filtro == FiltroSituacao.Todos && Normalizar(l.Rotulo).Contains(termo, StringComparison.Ordinal);
                if (!ativo || porSecao[i] > 0 || tituloBate) visiveis.Add(i);
                continue;
            }
            if (!passa[i]) continue;
            if (secaoDe[i] >= 0 && recolhidas.Contains(linhas[secaoDe[i]].Rotulo)) continue;
            visiveis.Add(i);
        }
        return new ResultadoFiltro(visiveis, porSecao, porFiltro);
    }
}
