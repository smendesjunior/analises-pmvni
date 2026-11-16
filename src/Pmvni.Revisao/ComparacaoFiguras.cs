using System.Globalization;
using System.Text.Json.Nodes;

namespace Pmvni.Revisao;

/// <summary>Comparar com o original, parte das figuras: o que a camada de edição mudou em relação às figuras recebidas (figuras incluídas,
/// excluídas, textos, marcações incluídas, removidas, movidas e legendas editadas). Vazio = nenhuma figura mudou.</summary>
public static class ComparacaoFiguras
{
    public static IReadOnlyList<string> Linhas(Projeto p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var linhas = new List<string>();
        if (p.Figuras is null) return linhas;
        var iniciais = CamadaFiguras.Inicial(p).ToDictionary(f => f.Id, StringComparer.Ordinal);
        var numero = 0;
        foreach (var f in p.Figuras)
        {
            if (f.Incluida) numero++;
            var rotulo = f.Incluida ? "Figura " + numero.ToString(CultureInfo.InvariantCulture) : "Figura excluída";
            var detalhes = new List<string>();
            iniciais.TryGetValue(f.Id, out var ini);
            if (ini is null)
                detalhes.Add("Recorte de prancha incluído" + Origem(f.Recorte) + ".");
            if (!f.Incluida) detalhes.Add("Excluída do relatório (não sai no PDF).");
            foreach (var campo in CamadaFiguras.CamposTexto)
            {
                var antes = ini?.TextoBase(campo) ?? "";
                var depois = f.TextoBase(campo);
                if (ini is not null && antes != depois) detalhes.Add(Nome(campo) + ": «" + antes + "» → «" + depois + "»");
            }
            var numeros = CamadaFiguras.Numeradas(f).ToDictionary(x => x.Marcacao.Id, x => x.Numero, StringComparer.Ordinal);
            string Rot(Marcacao m) => m.Numerada
                ? "Marcação " + (numeros.TryGetValue(m.Id, out var n) ? n.ToString(CultureInfo.InvariantCulture) : "?") + (m.Elemento.Length > 0 ? " (" + m.Elemento + ")" : "")
                : "Texto «" + m.Texto + "»";
            foreach (var m in f.Marcacoes)
            {
                var velha = ini?.Buscar(m.Id);
                if (velha is null) { detalhes.Add(Rot(m) + " incluída."); continue; }
                if (!Igual(velha.Alvo, m.Alvo) || !Igual(velha.Caixa, m.Caixa) || !Igual(velha.Posicao, m.Posicao) || velha.Lado != m.Lado
                    || Math.Abs(velha.Altura - m.Altura) > 1e-9)
                    detalhes.Add(Rot(m) + " movida ou redimensionada.");
                if (velha.Elemento != m.Elemento || velha.Texto != m.Texto)
                    detalhes.Add(Rot(m) + ": legenda «" + Legenda(velha) + "» → «" + Legenda(m) + "»");
                if (velha.Itens != m.Itens)
                    detalhes.Add(Rot(m) + ": itens «" + DadosEfetivos.Imprimir(p, velha.Itens) + "» → «" + DadosEfetivos.Imprimir(p, m.Itens) + "»");
            }
            if (ini is not null)
                foreach (var velha in ini.Marcacoes.Where(v => f.Buscar(v.Id) is null))
                    detalhes.Add((velha.Numerada ? "Marcação (" + velha.Elemento + ")" : "Texto «" + velha.Texto + "»") + " removida.");
            if (detalhes.Count == 0) continue;
            linhas.Add(rotulo + (f.TextoBase("titulo") is { Length: > 0 } t ? " — " + t : ""));
            linhas.AddRange(detalhes.Select(d => "  " + d));
        }
        return linhas;
    }

    /// <summary>O bloco de texto para a janela Comparar (vazio se nenhuma figura mudou).</summary>
    public static string Texto(Projeto p)
    {
        var l = Linhas(p);
        return l.Count == 0 ? "" : "Figuras alteradas:\n" + string.Join("\n", l);
    }

    static string Legenda(Marcacao m) => m.Numerada ? (m.Elemento + (m.Texto.Length > 0 ? ". " + m.Texto : "")) : m.Texto;

    static bool Igual(double[]? a, double[]? b) =>
        a is null ? b is null : b is not null && a.Length == b.Length && a.Zip(b).All(x => Math.Abs(x.First - x.Second) < 1e-9);

    static string Nome(string campo) => campo switch
    {
        "prancha" => "Prancha",
        "titulo" => "Título",
        "texto" => "Texto",
        "fonte" => "Fonte",
        _ => campo,
    };

    static string Origem(JsonObject? r)
    {
        if (r is null) return "";
        var pdf = r["pdf"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        var pag = r["pagina"] is JsonValue pv && pv.TryGetValue<int>(out var n) ? n : 0;
        return pdf is null ? "" : " (de «" + pdf + "», página " + pag.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
