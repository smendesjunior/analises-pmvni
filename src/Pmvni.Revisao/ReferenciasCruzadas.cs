using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Tarefa 16 (conferência depois da edição): as referências entre as partes do relatório que a edição pode quebrar.
/// <see cref="Quebradas"/> e <see cref="Contraditorias"/> entram nos <see cref="ConferenciaRevisao.Erros"/> (bloqueiam a exportação);
/// <see cref="FigurasRenumeradas"/> só avisa. Cada mensagem começa pela seção do relatório e pelo item, figura ou linha que cita
/// (<c>"ANÁLISE · ITEM 01 (campo verificacao): …"</c>).
/// <para>Como cada citação se resolve (o mesmo que o relatório imprime):</para>
/// <list type="bullet">
/// <item>nos textos dos itens e na conclusão, "item NN" virou token na importação ou na edição (<see cref="Citacoes"/>): o token aponta o id;</item>
/// <item>nas indicações das figuras, o campo de itens também guarda tokens (<see cref="CamadaFiguras.ItensCitados"/>);</item>
/// <item>"item NN" nas linhas de tabela e "Figura N" nos textos ficam literais (o editor não os renumera). Um número que já estava no texto
/// recebido do kit vale pela numeração original (o kit numerou assim); um número que a revisão trouxe vale pela numeração de agora (a que o
/// usuário vê ao digitar).</item>
/// </list>
/// Citações de norma não são do relatório e ficam de fora: o campo <c>base_legal</c> não é lido para figuras, e "Figura N"/"item NN" com
/// NBR, ABNT, Lei, Anexo, Decreto, NT… na mesma frase são ignorados ("ABNT NBR 9050:2020, 6.6.4, Figura 75"; "item 12 do Anexo VI").</summary>
public static partial class ReferenciasCruzadas
{
    public const string SecaoItens = "ANÁLISE";
    public const string SecaoFiguras = "IMAGENS DO PROJETO";
    public const string SecaoConclusao = "CONCLUSÃO";

    /// <summary>Campos do item em que "Figura N" é sempre de norma (não da seção IMAGENS DO PROJETO).</summary>
    static readonly HashSet<string> SemFiguras = new(StringComparer.Ordinal) { "base_legal" };

    // "Figura 2", "figuras 2 e 3", "Fig. 4" (o número é da seção IMAGENS DO PROJETO).
    [GeneratedRegex(@"(?<![\p{L}\p{N}])fig(?:uras?|s?\.)\s*([0-9]{1,3}(?:\s*(?:,|e)\s*[0-9]{1,3})*)(?![\p{L}\p{N}])(?![.,][0-9])",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex CitacaoFigura();

    [GeneratedRegex("[0-9]{1,3}", RegexOptions.CultureInvariant)]
    private static partial Regex Numero();

    const string Marcas = @"(?<![\p{L}])(?:NBR|ABNT|ISO|Lei|LC|Anexo|Decreto|Resolu[çc][ãa]o|Portaria|NT|IT|Instru[çc][ãa]o\s+T[ée]cnica|Norma)(?![\p{L}])";

    // Marca de norma na frase: a citação é da norma, não do relatório.
    [GeneratedRegex(Marcas, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex MarcaDeNorma();

    // Marca de norma colada antes de um parêntese, só com a designação no meio ("NBR 9050 (", "ABNT NBR 9050:2020 ("): o parêntese é da norma.
    [GeneratedRegex(Marcas + @"[\s\p{N}.:/\-–]*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex NormaColadaAoFim();

    // Parêntese aberto logo depois da citação que começa com a marca de norma ("Figura 75 (NBR 9050)").
    [GeneratedRegex(@"^\s*\(\s*" + Marcas, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ParenteseDeNorma();

    // Fim de frase: ponto/ponto e vírgula seguido de espaço, quebra de linha ou parêntese (o "." de "6.6.4" não conta).
    [GeneratedRegex(@"[.;!?](?=\s)|[\r\n()]", RegexOptions.CultureInvariant)]
    private static partial Regex FimDeFrase();

    const int AlcanceNorma = 60;

    /// <summary>A citação em [<paramref name="indice"/>, +<paramref name="comprimento"/>) está na mesma frase que uma marca de norma (até
    /// <see cref="AlcanceNorma"/> caracteres antes ou depois)? O parêntese aberto logo depois da norma ("conforme NBR 9050 (Figura 75)",
    /// "NBR 9050:2020 (item 6.6.4, Figura 75)") ou logo depois da citação com a norma dentro ("Figura 75 (NBR 9050)") não separa a frase.</summary>
    internal static bool DeNorma(string texto, int indice, int comprimento)
    {
        var inicio = Math.Max(0, indice - AlcanceNorma);
        var antes = texto[inicio..indice];
        var cortes = FimDeFrase().Matches(antes);
        var k = cortes.Count - 1;
        while (k >= 0 && cortes[k].Value == "(" && NormaColadaAoFim().IsMatch(antes[..cortes[k].Index])) k--;
        if (k >= 0) antes = antes[(cortes[k].Index + cortes[k].Length)..];
        var fim = Math.Min(texto.Length, indice + comprimento + AlcanceNorma);
        var depois = texto[(indice + comprimento)..fim];
        if (ParenteseDeNorma().IsMatch(depois)) return true;
        if (FimDeFrase().Match(depois) is { Success: true } c) depois = depois[..c.Index];
        return MarcaDeNorma().IsMatch(antes) || MarcaDeNorma().IsMatch(depois);
    }

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
                if (SemFiguras.Contains(campo)) continue;
                foreach (var m in FigurasQuebradas(figuras, incluidas, texto, OriginalDoCampo(it, campo)))
                    erros.Add(prefixo + m);
            }
        }

        // Conclusão: como os itens (o texto da conclusão não tem revisão própria: vale o original).
        if (Conclusao(p) is { } conclusao)
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

    /// <summary>Linha de tabela e item que ela cita com situações opostas (ATENDE × NÃO ATENDE), quando a revisão tocou o par: a situação da
    /// linha ou a do item foi revisada, ou a citação "item NN" foi trazida pela revisão. Uma contradição que já veio do kit não bloqueia
    /// por causa de uma edição que não mexe nela (outro texto da linha, por exemplo).</summary>
    public static IReadOnlyList<string> Contraditorias(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var erros = new List<string>();
        foreach (var id in TabelasRevisao.Ids(p))
        {
            var daLinha = TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(p, id));
            if (daLinha is null) continue;
            var situacaoLinhaRevisada = p.Linhas.TryGetValue(id, out var r) && r.Situacao is not null;
            var citados = ItensDaLinha(p, id).Select(x => x.Cita).Where(c => c.Item is not null).ToList();
            foreach (var it in citados.Select(c => c.Item!).Distinct())
            {
                var doItem = it.Situacao.Revisada ?? ConferenciaRevisao.SituacaoOriginal(it);
                if (!Opostas(daLinha, doItem)) continue;
                var tocou = situacaoLinhaRevisada || it.Situacao.Revisada is not null || citados.Any(c => c.Item == it && c.Nova);
                if (!tocou) continue;
                erros.Add(OndeLinha(id) + " está " + ConferenciaRevisao.TextoSituacao(daLinha) + " e cita " + SecaoItens + " · "
                          + ConferenciaRevisao.Rotulo(p, it) + ", que está " + ConferenciaRevisao.TextoSituacao(doItem) + ": as situações se contradizem");
            }
        }
        return erros.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Avisos (não bloqueiam): "Figura N" literal que veio do kit e que, depois de exclusões ou inclusões de figura, aponta para
    /// outro número no relatório (o editor não renumera o literal). Como o aviso de "item NN" literal (<see cref="ConferenciaRevisao.Avisos"/>).</summary>
    public static IReadOnlyList<string> FigurasRenumeradas(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var avisos = new List<string>();
        if (p.Figuras is not { } figuras) return avisos;   // sem camada de figuras, nada mudou de número
        var atual = new Dictionary<int, int>();             // número original → número de agora (só as incluídas)
        var n = 0;
        foreach (var f in figuras)
        {
            if (!f.Incluida) continue;
            n++;
            if (f.Origem is { } o) atual[o + 1] = n;
        }

        IEnumerable<string> Mudadas(string texto, string? original)
        {
            if (original is null) yield break;
            var doOriginal = NumerosDeFigura(original).ToHashSet();
            foreach (var k in NumerosDeFigura(texto).Distinct())
                if (doOriginal.Contains(k) && atual.TryGetValue(k, out var agora) && agora != k)
                    yield return "cita \"Figura " + k.ToString(CultureInfo.InvariantCulture) + "\", que agora é a Figura "
                                 + agora.ToString(CultureInfo.InvariantCulture) + ": confira o número";
        }

        foreach (var it in p.Incluidos)
            foreach (var (campo, texto) in ConferenciaRevisao.TextosDoItem(it))
            {
                if (SemFiguras.Contains(campo)) continue;
                foreach (var m in Mudadas(texto, OriginalDoCampo(it, campo)))
                    avisos.Add(SecaoItens + " · " + ConferenciaRevisao.Rotulo(p, it) + " (campo " + campo + ") " + m);
            }
        if (Conclusao(p) is { } conclusao)
            foreach (var m in Mudadas(conclusao, conclusao)) avisos.Add(SecaoConclusao + " " + m);
        return avisos.Distinct(StringComparer.Ordinal).ToList();
    }

    static string? Conclusao(Projeto p) =>
        p.Documento["conclusao"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

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

    /// <summary>Os números de "Figura N" do texto que não são de norma.</summary>
    static IEnumerable<int> NumerosDeFigura(string texto)
    {
        foreach (Match m in CitacaoFigura().Matches(texto))
        {
            if (DeNorma(texto, m.Index, m.Length)) continue;
            foreach (Match n in Numero().Matches(m.Groups[1].Value))
                yield return int.Parse(n.Value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>"Figura N" do texto que não está no relatório. Número que já estava no texto original: a figura de origem N, quebrada se a
    /// revisão a excluiu. Número novo: a N-ésima figura incluída de agora, quebrada se não existe.</summary>
    static IEnumerable<string> FigurasQuebradas(IReadOnlyList<FiguraRevisao> figuras, int incluidas, string texto, string? original)
    {
        var doOriginal = original is null ? new HashSet<int>() : NumerosDeFigura(original).ToHashSet();
        foreach (var n in NumerosDeFigura(texto).Distinct())
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
    /// null (o número não existe). <see cref="Nova"/>: a citação não estava no texto recebido (a revisão a trouxe).</summary>
    readonly record struct ItemCitado(int Numero, ItemRevisao? Item, ItemRevisao? Excluido, bool Nova);

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
                    cita = alvo.Incluido ? new ItemCitado(n, alvo, null, false) : new ItemCitado(n, null, alvo, false);
                }
                else cita = n >= 1 && n <= incluidos.Count ? new ItemCitado(n, incluidos[n - 1], null, true) : new ItemCitado(n, null, null, true);
                yield return (caminho, cita);
            }
        }
    }

    /// <summary>Os números das citações "item NN" que a regra comum reconhece (<see cref="Citacoes.Ocorrencias"/>), menos as de norma.</summary>
    static HashSet<int> NumerosDeItem(string texto) =>
        Citacoes.Ocorrencias(texto).Where(o => !DeNorma(texto, o.Indice, o.Comprimento)).Select(o => o.Numero).ToHashSet();
}
