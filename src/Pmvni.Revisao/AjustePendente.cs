using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>"A justificativa mudou: reajuste o item" (correções 2026-11-16). A justificativa (ou a situação) de um item ou de uma linha
/// de tabela mudou DEPOIS da última vez em que os textos dele foram mexidos: o texto pode não dizer mais o que a justificativa diz.
/// Sai do histórico (vale também depois de reabrir o relatório) e some quando os textos mudam — pela IA ou à mão. Nada dispara
/// sozinho: a tela só mostra o aviso e deixa o botão da IA à mão.</summary>
public static class AjustePendente
{
    static readonly HashSet<string> DeMotivo = new(StringComparer.Ordinal) { nameof(DefinirJustificativa), nameof(DefinirSituacao) };
    static readonly HashSet<string> DeTexto = new(StringComparer.Ordinal) { nameof(DefinirCampo), nameof(RestaurarCampo), nameof(RestaurarItemOriginal) };

    public static bool JustificativaMudou(Projeto p, IReadOnlyList<RegistroHistorico> historico, string id)
    {
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(historico);
        ArgumentNullException.ThrowIfNull(id);
        var justificativa = TabelasRevisao.EhLinha(id) ? TabelasRevisao.Justificativa(p, id) : p.Item(id)?.Situacao.Justificativa;
        if (string.IsNullOrWhiteSpace(justificativa)) return false;

        // Os registros que valem agora: um comando desfeito sai, um refeito volta.
        var ativos = new SortedDictionary<int, RegistroHistorico>();
        foreach (var r in historico)
        {
            if (r.Tipo == "desfeito" && r.Ref is { } d) ativos.Remove(d);
            else if (r.Tipo == "refeito" && r.Ref is { } f) { if (historico.FirstOrDefault(x => x.N == f) is { } o) ativos[f] = o; }
            else if (r.Tipo != Importador.TipoHistorico) ativos[r.N] = r;
        }
        int motivo = 0, texto = 0;
        foreach (var (n, r) in ativos)
            foreach (var tipo in Tipos(r, id))
            {
                if (DeMotivo.Contains(tipo)) motivo = n;
                else if (DeTexto.Contains(tipo)) texto = n;
            }
        // No mesmo registro (um lote que mexe na situação e no texto juntos), o texto já acompanha o motivo.
        return motivo > texto;
    }

    /// <summary>Os tipos das alterações que o registro faz em <paramref name="id"/> (um lote traz vários passos).</summary>
    static IEnumerable<string> Tipos(RegistroHistorico r, string id)
    {
        if (r.Tipo != nameof(Lote))
        {
            if (r.Item == id) yield return r.Tipo;
            yield break;
        }
        if (r.Depois?["passos"] is not JsonArray passos) yield break;
        foreach (var n in passos)
            if (n is JsonObject o && o["item"] is JsonValue i && i.TryGetValue<string>(out var item) && item == id
                && o["tipo"] is JsonValue t && t.TryGetValue<string>(out var tipo))
                yield return tipo;
    }
}
