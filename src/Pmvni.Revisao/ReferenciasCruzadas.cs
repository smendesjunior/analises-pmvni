using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Tarefa 16 (conferência depois da edição): as referências entre as partes do relatório que a edição pode quebrar. Entram nos
/// <see cref="ConferenciaRevisao.Erros"/>, então bloqueiam a exportação. Cada mensagem começa pela seção do relatório e pelo item, figura ou
/// linha que cita (<c>"ANÁLISE · ITEM 01 (campo verificacao): …"</c>).
/// <para>Como cada citação se resolve (o mesmo que o relatório imprime):</para>
/// <list type="bullet">
/// <item>nos textos dos itens e na conclusão, "item NN" virou token na importação ou na edição (<see cref="Citacoes"/>): o token aponta o id;</item>
/// <item>nas indicações das figuras, o campo de itens também guarda tokens (<see cref="CamadaFiguras.ItensCitados"/>);</item>
/// <item>"item NN" nas linhas de tabela e "Figura N" nos textos ficam literais (o editor não os renumera). Um número que já estava no texto
/// recebido do kit vale pela numeração original (o kit numerou assim); um número que a revisão trouxe vale pela numeração de agora (a que o
/// usuário vê ao digitar).</item>
/// </list></summary>
public static partial class ReferenciasCruzadas
{
    public const string SecaoItens = "ANÁLISE";
    public const string SecaoFiguras = "IMAGENS DO PROJETO";
    public const string SecaoConclusao = "CONCLUSÃO";

    // "Figura 2", "figuras 2 e 3", "Fig. 4" (o número é da seção IMAGENS DO PROJETO).
    [GeneratedRegex(@"(?<![\p{L}\p{N}])fig(?:uras?|s?\.)\s*([0-9]{1,3}(?:\s*(?:,|e)\s*[0-9]{1,3})*)(?![\p{L}\p{N}])(?![.,][0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CitacaoFigura();

    [GeneratedRegex("[0-9]{1,3}", RegexOptions.CultureInvariant)]
    private static partial Regex Numero();

    /// <summary>Citações a item ou figura que não estão no relatório: excluídos na revisão ou números que não existem.</summary>
    public static IReadOnlyList<string> Quebradas(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var erros = new List<string>();
        var figuras = p.Figuras ?? CamadaFiguras.Inicial(p);
        var incluidas = figuras.Count(f => f.Incluida);

        // Itens: tokens de item e "Figura N" literal.
        foreach (var it in p.Incluidos)
        {
            var onde = SecaoItens + " · " + ConferenciaRevisao.Rotulo(p, it);
            foreach (var (campo, texto) in ConferenciaRevisao.TextosDoItem(it))
            {
                var prefixo = onde + " (campo " + campo + "): ";
                foreach (var alvo in Citacoes.Alvos(texto).Distinct(StringComparer.Ordinal))
                    if (ItemExcluido(p, alvo) is { } n) erros.Add(prefixo + "cita o item excluído que era o item " + n);
                foreach (var m in FigurasQuebradas(figuras, incluidas, texto, OriginalDoCampo(it, campo)))
                    erros.Add(prefixo + m);
            }
        }

        // Conclusão: como os itens (o texto da conclusão não tem revisão própria: vale o original).
        if (p.Documento["conclusao"] is JsonValue v && v.TryGetValue<string>(out var conclusao))
        {
            foreach (var alvo in Citacoes.Alvos(conclusao).Distinct(StringComparer.Ordinal))
                if (ItemExcluido(p, alvo) is { } n) erros.Add(SecaoConclusao + ": cita o item excluído que era o item " + n);
            foreach (var m in FigurasQuebradas(figuras, incluidas, conclusao, conclusao))
                erros.Add(SecaoConclusao + ": " + m);
        }

        // Figuras incluídas: as indicações que citam item excluído.
        var numero = 0;
        foreach (var f in figuras.Where(x => x.Incluida))
        {
            numero++;
            var k = 0;
            foreach (var mk in f.Marcacoes)
            {
                k++;
                var indicacao = CamadaFiguras.Numero(f, mk.Id) ?? k;
                foreach (var alvo in CamadaFiguras.ItensCitados(mk.Itens).Distinct(StringComparer.Ordinal))
                    if (ItemExcluido(p, alvo) is { } n)
                        erros.Add(SecaoFiguras + " · Figura " + numero.ToString(CultureInfo.InvariantCulture) + " (indicação "
                                  + indicacao.ToString(CultureInfo.InvariantCulture) + "): cita o item excluído que era o item " + n);
            }
        }

        // Linhas de tabela: "item NN" literal.
        foreach (var id in TabelasRevisao.Ids(p))
            foreach (var (caminho, cita) in ItensDaLinha(p, id))
            {
                var prefixo = OndeLinha(id) + " (campo " + TabelasRevisao.NomeDoCampo(caminho) + "): ";
                if (cita.Excluido is { } excluido)
                    erros.Add(prefixo + "cita o item excluído que era o item " + excluido.NumeroOriginal.ToString("D2", CultureInfo.InvariantCulture));
                else if (cita.Item is null)
                    erros.Add(prefixo + "cita o item " + cita.Numero.ToString("D2", CultureInfo.InvariantCulture) + ", que não existe no relatório");
            }
        return erros.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Linha de tabela e item que ela cita com situações opostas (ATENDE × NÃO ATENDE). Só depois da edição: a linha ou o item
    /// foi revisado (o que veio do kit já passou pela conferência dele).</summary>
    public static IReadOnlyList<string> Contraditorias(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var erros = new List<string>();
        foreach (var id in TabelasRevisao.Ids(p))
        {
            var daLinha = TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(p, id));
            if (daLinha is null) continue;
            var linhaRevisada = TabelasRevisao.Revisada(p, id);
            foreach (var it in ItensDaLinha(p, id).Select(x => x.Cita.Item).OfType<ItemRevisao>().Distinct())
            {
                var doItem = it.Situacao.Revisada ?? ConferenciaRevisao.SituacaoOriginal(it);
                if (!Opostas(daLinha, doItem) || !(linhaRevisada || it.Situacao.Revisada is not null)) continue;
                erros.Add(OndeLinha(id) + " está " + ConferenciaRevisao.TextoSituacao(daLinha) + " e cita " + SecaoItens + " · "
                          + ConferenciaRevisao.Rotulo(p, it) + ", que está " + ConferenciaRevisao.TextoSituacao(doItem) + ": as situações se contradizem");
            }
        }
        return erros.Distinct(StringComparer.Ordinal).ToList();
    }

    static bool Opostas(string? a, string? b) =>
        (a, b) is ("ATENDE", "NAO_ATENDE") or ("NAO_ATENDE", "ATENDE");

    /// <summary>O número original (dois dígitos) do item citado, se ele foi excluído; null se está incluído ou não existe.</summary>
    static string? ItemExcluido(Projeto p, string id) =>
        p.Item(id) is { Incluido: false } alvo ? alvo.NumeroOriginal.ToString("D2", CultureInfo.InvariantCulture) : null;

    static string? OriginalDoCampo(ItemRevisao it, string campo) =>
        it.Campos.TryGetValue(campo, out var c) && c.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    static string OndeLinha(string id)
    {
        TabelasRevisao.TentarLer(id, out var bloco, out _);
        var secao = bloco switch
        {
            TabelasRevisao.Indices => "ÍNDICES URBANÍSTICOS · ",
            TabelasRevisao.Ambientes => "ILUMINAÇÃO E VENTILAÇÃO · ",
            _ => "",   // o rótulo do diagrama já diz "DIAGRAMA · …"
        };
        return secao + TabelasRevisao.Rotulo(id);
    }

    static IEnumerable<int> Numeros(string texto, Regex citacao)
    {
        foreach (Match m in citacao.Matches(texto))
            foreach (Match n in Numero().Matches(m.Groups[1].Value))
                yield return int.Parse(n.Value, CultureInfo.InvariantCulture);
    }

    /// <summary>"Figura N" do texto que não está no relatório. Número que já estava no texto original: a figura de origem N, quebrada se a
    /// revisão a excluiu. Número novo: a N-ésima figura incluída de agora, quebrada se não existe.</summary>
    static IEnumerable<string> FigurasQuebradas(IReadOnlyList<FiguraRevisao> figuras, int incluidas, string texto, string? original)
    {
        var doOriginal = original is null ? new HashSet<int>() : Numeros(original, CitacaoFigura()).ToHashSet();
        foreach (var n in Numeros(texto, CitacaoFigura()).Distinct())
        {
            if (doOriginal.Contains(n))
            {
                if (figuras.FirstOrDefault(f => f.Origem == n - 1) is { Incluida: false })
                    yield return "cita a figura excluída que era a Figura " + n.ToString(CultureInfo.InvariantCulture);
            }
            else if (n < 1 || n > incluidas)
                yield return "cita a Figura " + n.ToString(CultureInfo.InvariantCulture) + ", que não existe no relatório";
        }
    }

    /// <summary>Um "item NN" literal de linha de tabela resolvido: <see cref="Item"/> incluído citado, ou <see cref="Excluido"/>, ou os dois
    /// null (o número não existe).</summary>
    readonly record struct ItemCitado(int Numero, ItemRevisao? Item, ItemRevisao? Excluido);

    /// <summary>Os "item NN" literais dos textos da linha (o texto efetivo de cada caminho), resolvidos pela regra da classe.</summary>
    static IEnumerable<(string Caminho, ItemCitado Cita)> ItensDaLinha(Projeto p, string id)
    {
        if (TabelasRevisao.Original(p, id) is not { } original) yield break;
        var incluidos = p.Incluidos;
        // Só os itens recebidos têm número original (o criado na revisão não é citado pelo kit).
        var porOriginal = p.Itens.Where(i => !i.Criado).ToDictionary(i => i.NumeroOriginal);
        foreach (var caminho in TabelasRevisao.Caminhos(original))
        {
            var texto = TabelasRevisao.TextoEfetivo(p, id, caminho);
            if (string.IsNullOrEmpty(texto)) continue;
            var doOriginal = TabelasRevisao.TextoOriginal(p, id, caminho) is { } o ? NumerosDeItem(o) : new HashSet<int>();
            foreach (var n in NumerosDeItem(texto))
            {
                ItemCitado cita;
                if (doOriginal.Contains(n))
                {
                    if (!porOriginal.TryGetValue(n, out var alvo)) continue;   // o kit citou um número que não tem: não é da edição
                    cita = alvo.Incluido ? new ItemCitado(n, alvo, null) : new ItemCitado(n, null, alvo);
                }
                else cita = n >= 1 && n <= incluidos.Count ? new ItemCitado(n, incluidos[n - 1], null) : new ItemCitado(n, null, null);
                yield return (caminho, cita);
            }
        }
    }

    /// <summary>Os números das citações "item NN" que a regra comum reconhece (<see cref="Citacoes.Converter(string, IReadOnlyList{string})"/>).</summary>
    static HashSet<int> NumerosDeItem(string texto)
    {
        // Converter com ids fictícios de 1 a 999 só para reconhecer as citações; o texto convertido não é usado.
        var (_, convertidas) = Citacoes.Converter(texto, IdsFicticios);
        return convertidas.Select(c => c.Numero).ToHashSet();
    }

    static readonly IReadOnlyList<string> IdsFicticios = Enumerable.Range(1, 999).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
}
