using System.Globalization;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>Um campo de texto revisado: <see cref="Original"/> e <see cref="Revisado"/> como são impressos hoje (as citações com a
/// numeração atual; campo que o original não tinha: <see cref="Original"/> vazio). <see cref="Campo"/> é a chave de
/// <see cref="Campos.Editaveis"/>.</summary>
public sealed record DiferencaCampo(string Campo, string Original, string Revisado);

/// <summary>O que mudou num item em relação ao original recebido. <see cref="Item"/> é o id; <see cref="NumeroAtual"/> é null para o item
/// excluído (que mantém o <see cref="NumeroOriginal"/>). <see cref="SituacaoRevisada"/> null = a situação não foi revista (e então
/// <see cref="SituacaoOriginal"/> também é null: só aparece quando há o que comparar). <see cref="CitacoesConvertidas"/> são as citações
/// "item NN" que a importação converteu neste item (texto pronto para mostrar).</summary>
public sealed record DiferencaItem(
    string Item,
    int? NumeroAtual,
    int NumeroOriginal,
    bool Excluido,
    string? SituacaoOriginal,
    string? SituacaoRevisada,
    IReadOnlyList<DiferencaCampo> Campos,
    IReadOnlyList<string> CitacoesConvertidas,
    string? Justificativa = null);

/// <summary>O que mudou numa linha de tabela (<see cref="TabelasRevisao"/>) em relação ao original. <see cref="Campos"/> leva o caminho
/// do texto em <see cref="DiferencaCampo.Campo"/>; <see cref="SituacaoRevisada"/> null = a situação não foi revista.</summary>
public sealed record DiferencaLinha(
    string Linha,
    string Descricao,
    string? SituacaoOriginal,
    string? SituacaoRevisada,
    IReadOnlyList<DiferencaCampo> Campos,
    string? Justificativa);

/// <summary>Uma citação "item NN" que a importação converteu em token (desenho §8.5): onde estava, o trecho como veio e o item citado
/// com a numeração de hoje.</summary>
public sealed record CitacaoImportada(string Item, int? NumeroDoItem, int NumeroOriginalDoItem, string Campo, string Trecho, string Alvo,
    int? NumeroDoAlvo, int NumeroOriginalDoAlvo, bool AlvoExcluido)
{
    /// <summary><c>Verificação técnica: «item 07» → ITEM 07</c> (ou <c>→ ITEM 03 (excluído)</c>).</summary>
    public string Texto =>
        Comparacao.NomeDoCampo(Campo) + ": «" + Trecho + "» → ITEM "
        + (NumeroDoAlvo ?? NumeroOriginalDoAlvo).ToString("D2", CultureInfo.InvariantCulture) + (AlvoExcluido ? " (excluído)" : "");
}

/// <summary>Comparar com o original (desenho §8.5): só o que mudou, item a item, e as citações que a importação converteu. Tudo calculado
/// do projeto e do histórico; nada aqui muda o projeto.</summary>
public static class Comparacao
{
    /// <summary>Os itens com alguma revisão (situação, campo de texto ou exclusão), na ordem original. Item sem revisão não entra, mesmo que
    /// tenha citações convertidas (essas ficam em <see cref="CitacoesImportadas"/>).</summary>
    public static IReadOnlyList<DiferencaItem> Diferencas(Projeto p, IReadOnlyList<RegistroHistorico> h)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(h);
        var citacoes = CitacoesImportadas(p, h);
        var lista = new List<DiferencaItem>();
        foreach (var it in p.Itens.OrderBy(i => i.Ordem))
        {
            var campos = new List<DiferencaCampo>();
            foreach (var nome in Campos.Editaveis)
            {
                if (!it.Campos.TryGetValue(nome, out var c) || c.Revisado is null) continue;
                var original = c.Original is JsonValue v && v.TryGetValue<string>(out var s) ? DadosEfetivos.Imprimir(p, s) : "";
                var revisado = DadosEfetivos.Imprimir(p, c.Revisado);
                if (!c.Ausente && original == revisado) continue;
                campos.Add(new DiferencaCampo(nome, original, revisado));
            }
            var mudouSituacao = it.Situacao.Revisada is not null;
            if (campos.Count == 0 && !mudouSituacao && it.Incluido) continue;

            string? situacaoOriginal = null;
            if (mudouSituacao && it.Situacao.Original is JsonValue so && so.TryGetValue<string>(out var codigo)) situacaoOriginal = codigo;
            lista.Add(new DiferencaItem(
                it.Id, p.NumeroAtual(it.Id), it.NumeroOriginal, !it.Incluido,
                situacaoOriginal, it.Situacao.Revisada, campos,
                citacoes.Where(c => c.Item == it.Id).Select(c => c.Texto).ToList(),
                string.IsNullOrWhiteSpace(it.Situacao.Justificativa) ? null : it.Situacao.Justificativa));
        }
        return lista;
    }

    /// <summary>As linhas de tabela com alguma revisão (situação ou texto), na ordem em que saem no relatório.</summary>
    public static IReadOnlyList<DiferencaLinha> DiferencasLinhas(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var lista = new List<DiferencaLinha>();
        foreach (var id in TabelasRevisao.Ids(p))
        {
            p.Linhas.TryGetValue(id, out var l);
            if (l?.Revisada != true && !TabelasRevisao.TemAcrescimo(p, id)) continue;
            // O que o usuário acrescentou (a linha inteira ou uma esquadria) não tem original: aparece como texto novo.
            bool Novo(string caminho) =>
                TabelasRevisao.Acrescentado(p, id) || caminho.Split('/') is { Length: > 2 } partes && TabelasRevisao.Acrescentado(p, id, partes[0] + "/" + partes[1]);
            var campos = TabelasRevisao.Caminhos(TabelasRevisao.Original(p, id)!)
                .Where(c => l?.Campos.ContainsKey(c) == true || Novo(c))
                .Select(c => new DiferencaCampo(c, Novo(c) ? "" : TabelasRevisao.TextoOriginal(p, id, c) ?? "", TabelasRevisao.TextoEfetivo(p, id, c) ?? "")).ToList();
            lista.Add(new DiferencaLinha(id, TabelasRevisao.Descricao(p, id),
                l?.Situacao is null ? null : TabelasRevisao.SituacaoOriginal(p, id), l?.Situacao, campos,
                string.IsNullOrWhiteSpace(l?.Justificativa) ? null : l!.Justificativa));
        }
        return lista;
    }

    /// <summary>Todas as citações que a importação converteu (do registro <see cref="Importador.TipoHistorico"/> do histórico), em qualquer
    /// item, com ou sem revisão: o item citado aparece com o número de hoje (ou o original, se foi excluído).</summary>
    public static IReadOnlyList<CitacaoImportada> CitacoesImportadas(Projeto p, IReadOnlyList<RegistroHistorico> h)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(h);
        var lista = new List<CitacaoImportada>();
        foreach (var r in h.Where(r => r.Tipo == Importador.TipoHistorico && r.Depois is JsonArray))
        {
            foreach (var n in (JsonArray)r.Depois!)
            {
                if (n is not JsonObject o) continue;
                var item = Texto(o, "item");
                var campo = Texto(o, "campo");
                var trecho = Texto(o, "trecho");
                var alvo = Texto(o, "alvo");
                if (item is null || campo is null || trecho is null || alvo is null) continue;
                if (p.Item(item) is not { } dono || p.Item(alvo) is not { } cit) continue;
                lista.Add(new CitacaoImportada(item, p.NumeroAtual(item), dono.NumeroOriginal, campo, trecho, alvo,
                    p.NumeroAtual(alvo), cit.NumeroOriginal, !cit.Incluido));
            }
        }
        return lista;
    }

    /// <summary>O texto da janela "Comparar com o original": cada item alterado (situação original → revisada, justificativa, cada campo com
    /// o original e o revisado como são impressos, citações convertidas do item) e, à parte, todas as citações que a importação converteu
    /// (inclusive as de itens sem alteração). Texto puro, para a caixa de diálogo; nada é cortado.</summary>
    public static string Texto(IReadOnlyList<DiferencaItem> diferencas, IReadOnlyList<CitacaoImportada> citacoes,
        IReadOnlyList<DiferencaLinha>? linhasTabela = null)
    {
        ArgumentNullException.ThrowIfNull(diferencas);
        ArgumentNullException.ThrowIfNull(citacoes);
        var linhas = new List<string>
        {
            diferencas.Count == 0
                ? "Nenhum item foi alterado em relação ao original recebido."
                : diferencas.Count == 1 ? "1 item alterado em relação ao original recebido:" : diferencas.Count + " itens alterados em relação ao original recebido:",
        };
        foreach (var d in diferencas)
        {
            linhas.Add("");
            linhas.Add(Rotulo(d));
            if (d.Excluido) linhas.Add("  Excluído do relatório (não sai no PDF).");
            if (LinhaDaSituacao(d) is { } situacao) linhas.Add("  " + situacao);
            if (d.Justificativa is not null) linhas.Add("  Justificativa: " + d.Justificativa);
            foreach (var c in d.Campos)
            {
                linhas.Add("  " + NomeDoCampo(c.Campo) + ":");
                linhas.Add("    Original: " + (c.Original.Length == 0 ? "(ausente no original)" : c.Original));
                linhas.Add("    Revisado: " + c.Revisado);
            }
            foreach (var c in d.CitacoesConvertidas) linhas.Add("  Citação convertida na importação: " + c);
        }
        // Linhas das tabelas (índices, iluminação e ventilação): só aparecem quando alguma foi alterada.
        if (linhasTabela is { Count: > 0 })
        {
            linhas.Add("");
            linhas.Add(linhasTabela.Count == 1 ? "1 linha de tabela alterada:" : linhasTabela.Count + " linhas de tabela alteradas:");
            foreach (var d in linhasTabela)
            {
                linhas.Add("");
                linhas.Add(d.Descricao);
                if (d.SituacaoRevisada is not null)
                    linhas.Add("  Situação: " + ConferenciaRevisao.TextoSituacao(d.SituacaoOriginal) + " → " + ConferenciaRevisao.TextoSituacao(d.SituacaoRevisada));
                if (d.Justificativa is not null) linhas.Add("  Justificativa: " + d.Justificativa);
                foreach (var c in d.Campos)
                {
                    linhas.Add("  " + TabelasRevisao.NomeDoCampo(c.Campo) + ":");
                    linhas.Add("    Original: " + (c.Original.Length == 0 ? "(acrescentado na revisão)" : c.Original));
                    linhas.Add("    Revisado: " + c.Revisado);
                }
            }
        }
        linhas.Add("");
        linhas.Add("Citações convertidas na importação (" + citacoes.Count.ToString(CultureInfo.InvariantCulture) + "):");
        if (citacoes.Count == 0) linhas.Add("  Nenhuma.");
        foreach (var c in citacoes)
        {
            var dono = c.NumeroDoItem is { } n
                ? "ITEM " + n.ToString("D2", CultureInfo.InvariantCulture)
                : "item excluído (era o " + c.NumeroOriginalDoItem.ToString("D2", CultureInfo.InvariantCulture) + ")";
            linhas.Add("  " + dono + " · " + c.Texto);
        }
        return string.Join("\n", linhas);
    }

    /// <summary>O nome do campo como a tela o mostra.</summary>
    public static string NomeDoCampo(string campo) => campo switch
    {
        "titulo" => "Título",
        "verificacao" => "Verificação técnica",
        "providencia" => "Providência",
        "base_legal" => "Base legal",
        "referencia" => "Referência",
        _ => campo,
    };

    /// <summary><c>ITEM 03</c>, ou <c>Item excluído (era o 03)</c>.</summary>
    public static string Rotulo(DiferencaItem d) => d.NumeroAtual is { } n
        ? "ITEM " + n.ToString("D2", CultureInfo.InvariantCulture)
        : "Item excluído (era o " + d.NumeroOriginal.ToString("D2", CultureInfo.InvariantCulture) + ")";

    /// <summary><c>Situação: Não comprovado → Atende</c>; null quando a situação não foi revista.</summary>
    public static string? LinhaDaSituacao(DiferencaItem d) => d.SituacaoRevisada is null
        ? null
        : "Situação: " + ConferenciaRevisao.TextoSituacao(d.SituacaoOriginal) + " → " + ConferenciaRevisao.TextoSituacao(d.SituacaoRevisada);

    private static string? Texto(JsonObject o, string nome) => o[nome] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
