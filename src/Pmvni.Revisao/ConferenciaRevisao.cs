using System.Globalization;
using System.Text.Json.Nodes;
using Pmvni.Composicao.Kit;

namespace Pmvni.Revisao;

/// <summary>A conferência final da revisão (a que o editor faz antes de exportar; a do kit sobre o dados.json efetivo é outra):
/// <see cref="Erros"/> bloqueiam, <see cref="Avisos"/> e <see cref="CitacoesAExcluidos"/> só avisam. Só os itens incluídos contam
/// e a numeração nas mensagens é a de agora (a que o relatório imprime).</summary>
public static class ConferenciaRevisao
{
    /// <summary>Situação mudada sem justificativa: <c>"ITEM 03: a situação mudou de FALTA COMPROVAR para ATENDE sem justificativa"</c>.</summary>
    public static IReadOnlyList<string> Erros(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var erros = new List<string>();
        foreach (var it in p.Incluidos)
        {
            if (!FaltaJustificativa(it)) continue;
            erros.Add(Rotulo(p, it) + ": a situação mudou de " + TextoSituacao(SituacaoOriginal(it)) + " para " + TextoSituacao(it.Situacao.Revisada)
                      + " sem justificativa");
        }
        return erros;
    }

    /// <summary>Situação revisada (diferente da original) sem justificativa preenchida. Usada também por <see cref="Pendencias"/>.</summary>
    internal static bool FaltaJustificativa(ItemRevisao it) =>
        it.Situacao.Revisada is not null && string.IsNullOrWhiteSpace(it.Situacao.Justificativa);

    /// <summary>Trechos "item NN"/"itens NN" que ficaram literais nos textos (não viraram citação) e podem ter perdido o sentido porque a
    /// numeração dos itens mudou. Sem mudança de numeração (nenhuma exclusão), não há aviso.</summary>
    public static IReadOnlyList<string> Avisos(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var avisos = new List<string>();
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
    private static IEnumerable<(string Campo, string Texto)> TextosDoItem(ItemRevisao it)
    {
        foreach (var nome in Campos.Editaveis)
        {
            if (!it.Campos.TryGetValue(nome, out var c)) continue;
            var texto = c.Revisado ?? (c.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null);
            if (texto is not null) yield return (nome, texto);
        }
    }

    private static string Rotulo(Projeto p, ItemRevisao it) =>
        "ITEM " + (p.NumeroAtual(it.Id) ?? it.NumeroOriginal).ToString("D2", CultureInfo.InvariantCulture);

    private static string? SituacaoOriginal(ItemRevisao it) =>
        it.Situacao.Original is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    internal static string TextoSituacao(string? codigo) =>
        codigo is not null && Situacoes.Sit.TryGetValue(codigo, out var t) ? t.Texto : codigo ?? "(sem situação)";
}
