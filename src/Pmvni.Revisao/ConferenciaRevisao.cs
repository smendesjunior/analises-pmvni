using System.Globalization;
using System.Text.Json.Nodes;
using Pmvni.Composicao.Kit;

namespace Pmvni.Revisao;

/// <summary>A conferência final da revisão (a que o editor faz antes de exportar; a do kit sobre o dados.json efetivo é outra):
/// <see cref="Erros"/> bloqueiam (desde a Tarefa 16, também as referências a item ou figura excluídos e as situações contraditórias
/// entre linha de tabela e item, ver <see cref="ReferenciasCruzadas"/>); <see cref="Avisos"/> só avisam. <see cref="CitacoesAExcluidos"/>
/// continua descrevendo as citações a excluídos no formato antigo, mas elas agora também bloqueiam pelos <see cref="Erros"/>. Só os itens
/// incluídos contam e a numeração nas mensagens é a de agora (a que o relatório imprime).</summary>
public static class ConferenciaRevisao
{
    /// <summary>Situação mudada sem justificativa: <c>"ITEM 03: a situação mudou de FALTA COMPROVAR para ATENDE sem justificativa"</c>.</summary>
    public static IReadOnlyList<string> Erros(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var erros = new List<string>();
        foreach (var it in p.Incluidos)
        {
            // Item criado na revisão e ainda com os textos de nascença: não sai no PDF por engano.
            if (ItemNovo.PorPreencher(it) is { Count: > 0 } vazios)
                erros.Add(Rotulo(p, it) + " (item novo): falta preencher " + string.Join(", ", vazios.Select(c => Comparacao.NomeDoCampo(c).ToLowerInvariant())));
            if (!FaltaJustificativa(it)) continue;
            erros.Add(Rotulo(p, it) + ": a situação mudou de " + TextoSituacao(SituacaoOriginal(it)) + " para " + TextoSituacao(it.Situacao.Revisada)
                      + " sem justificativa");
        }
        // Linhas das tabelas (índices, iluminação e ventilação): a mesma regra.
        foreach (var (id, l) in p.Linhas)
        {
            if (!TabelasRevisao.FaltaJustificativa(l)) continue;
            erros.Add(TabelasRevisao.Rotulo(id) + ": a situação mudou de " + TextoSituacao(TabelasRevisao.SituacaoOriginal(p, id)) + " para "
                      + TextoSituacao(l.Situacao) + " sem justificativa");
        }
        // Tarefa 16: referências quebradas e situações contraditórias depois da edição.
        erros.AddRange(ReferenciasCruzadas.Quebradas(p));
        erros.AddRange(ReferenciasCruzadas.Contraditorias(p));
        return erros;
    }

    /// <summary>Situação revisada (diferente da original) sem justificativa preenchida. Usada também por <see cref="Pendencias"/>.
    /// O item criado pelo usuário não tem original de que diferir: nunca exige justificativa.</summary>
    internal static bool FaltaJustificativa(ItemRevisao it) =>
        !it.Criado && it.Situacao.Revisada is not null && string.IsNullOrWhiteSpace(it.Situacao.Justificativa);

    /// <summary>Trechos "item NN"/"itens NN" que ficaram literais nos textos (não viraram citação) e podem ter perdido o sentido porque a
    /// numeração dos itens mudou. Sem mudança de numeração (nenhuma exclusão), não há aviso.</summary>
    public static IReadOnlyList<string> Avisos(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        // Tarefa 16: "Figura N" literal do kit que agora aponta para outro número (independe da numeração dos itens).
        var avisos = new List<string>(ReferenciasCruzadas.FigurasRenumeradas(p));
        if (!NumeracaoMudou(p)) return avisos;

        foreach (var it in p.Incluidos)
            foreach (var (campo, texto) in TextosDoItem(it))
                foreach (var trecho in Citacoes.LiteraisSuspeitos(texto))
                    avisos.Add(Rotulo(p, it) + " cita \"" + trecho + "\" (campo " + campo + ") e a numeração dos itens mudou: confira se ainda vale");

        if (p.Documento["conclusao"] is JsonValue v && v.TryGetValue<string>(out var conclusao))
            foreach (var trecho in Citacoes.LiteraisSuspeitos(conclusao))
                avisos.Add("A conclusão cita \"" + trecho + "\" e a numeração dos itens mudou: confira se ainda vale");
        return avisos;
    }

    /// <summary>Citações (já ligadas a um item) que apontam para um item excluído: <c>"ITEM 01 cita o item excluído que era o item 03 (campo verificacao)"</c>;
    /// na conclusão, <c>"A conclusão cita o item excluído que era o item 03"</c>. O relatório imprime o número original do excluído.</summary>
    public static IReadOnlyList<string> CitacoesAExcluidos(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var avisos = new List<string>();

        string? Excluido(string id)
        {
            var alvo = p.Item(id);
            return alvo is { Incluido: false } ? alvo.NumeroOriginal.ToString("D2", CultureInfo.InvariantCulture) : null;
        }

        foreach (var it in p.Incluidos)
            foreach (var (campo, texto) in TextosDoItem(it))
                foreach (var alvo in Citacoes.Alvos(texto))
                    if (Excluido(alvo) is { } n)
                        avisos.Add(Rotulo(p, it) + " cita o item excluído que era o item " + n + " (campo " + campo + ")");

        if (p.Documento["conclusao"] is JsonValue v && v.TryGetValue<string>(out var conclusao))
            foreach (var alvo in Citacoes.Alvos(conclusao))
                if (Excluido(alvo) is { } n)
                    avisos.Add("A conclusão cita o item excluído que era o item " + n);
        return avisos;
    }

    private static bool NumeracaoMudou(Projeto p) =>
        p.Incluidos.Any(i => p.NumeroAtual(i.Id) != i.NumeroOriginal);

    /// <summary>Os textos guardados (com tokens de citação) dos campos de texto do item, na ordem dos campos editáveis.</summary>
    internal static IEnumerable<(string Campo, string Texto)> TextosDoItem(ItemRevisao it)
    {
        foreach (var nome in Campos.Editaveis)
        {
            if (!it.Campos.TryGetValue(nome, out var c)) continue;
            var texto = c.Revisado ?? (c.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null);
            if (texto is not null) yield return (nome, texto);
        }
    }

    internal static string Rotulo(Projeto p, ItemRevisao it) =>
        "ITEM " + (p.NumeroAtual(it.Id) ?? it.NumeroOriginal).ToString("D2", CultureInfo.InvariantCulture);

    internal static string? SituacaoOriginal(ItemRevisao it) =>
        it.Situacao.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static string TextoSituacao(string? codigo) =>
        codigo is not null && Situacoes.Sit.TryGetValue(codigo, out var t) ? t.Texto : codigo ?? "(sem situação)";
}
