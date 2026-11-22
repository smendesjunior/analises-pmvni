namespace Pmvni.Legislacao;

/// <summary>Um resultado da pesquisa em todas as leis: uma página com ocorrência.</summary>
public sealed record ResultadoPesquisa(string AnexoId, int Pagina, string Trecho, int OcorrenciasNaPagina);

/// <summary>Os primeiros trechos de uma lei com o termo e em quantas páginas dela ele aparece.</summary>
public sealed record TrechosDaLei(IReadOnlyList<ResultadoPesquisa> Trechos, int Paginas);

public sealed record Pesquisa(IReadOnlyList<ResultadoPesquisa> Resultados, int Total, int Leis);

/// <summary>Índice em memória do texto de todos os anexos (desenho §11.6): carregado uma vez e atualizado a cada anexo,
/// substituição ou exclusão (só o que mudou, por sha). A pesquisa não diferencia caixa nem acento (<see cref="TextoPesquisa"/>).
/// Seguro para pesquisar enquanto outra thread chama <see cref="Atualizar"/>.</summary>
public sealed class IndiceLegislacao
{
    const int TamanhoTrecho = 160;
    const int MinimoDoTermo = 2;
    const string Reticencias = "…";

    sealed record Texto(string[] Originais, string[] Normalizados);
    sealed record Item(Anexo Anexo, Texto Texto);

    readonly Dictionary<string, Texto> porSha = new(StringComparer.Ordinal);
    volatile IReadOnlyList<Item> itens = [];

    IndiceLegislacao() { }

    public static IndiceLegislacao Carregar(AcervoLegislacao a, CancellationToken ct)
    {
        var indice = new IndiceLegislacao();
        indice.Montar(a, ct);
        return indice;
    }

    /// <summary>Relê só o que mudou: anexos novos ou com outro sha, e os que ganharam índice. O que saiu do catálogo sai da pesquisa.</summary>
    public void Atualizar(AcervoLegislacao a) => Montar(a, CancellationToken.None);

    void Montar(AcervoLegislacao a, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(a);
        lock (porSha)
        {
            var novos = new List<Item>();
            var vivos = new HashSet<string>(StringComparer.Ordinal);
            foreach (var anexo in a.Listar())
            {
                ct.ThrowIfCancellationRequested();
                if (!porSha.TryGetValue(anexo.Sha256, out var texto))
                {
                    if (a.TextoPorPagina(anexo.Id) is not { } paginas) continue; // sem índice (ainda): fora da pesquisa
                    texto = new Texto([.. paginas], [.. paginas.Select(TextoPesquisa.Normalizar)]);
                    porSha[anexo.Sha256] = texto;
                }
                vivos.Add(anexo.Sha256);
                novos.Add(new Item(anexo, texto));
            }
            foreach (var sha in porSha.Keys.Where(s => !vivos.Contains(s)).ToList()) porSha.Remove(sha);
            itens = novos;
        }
    }

    public IReadOnlySet<string> LeisComTexto(string filtro)
    {
        var termo = TextoPesquisa.Normalizar(filtro);
        return itens.Where(i => termo.Length > 0 && i.Texto.Normalizados.Any(p => p.Contains(termo, StringComparison.Ordinal)))
            .Select(i => i.Anexo.Id).ToHashSet(StringComparer.Ordinal);
    }
    /// <summary>Para a lista de leis filtrada pelo conteúdo: por lei, os trechos das primeiras <paramref name="porLei"/> páginas com o termo
    /// e o total de páginas com ocorrência. Termo normalizado com menos de 2 caracteres ⇒ vazio.</summary>
    public IReadOnlyDictionary<string, TrechosDaLei> TrechosPorLei(string filtro, int porLei = 3)
    {
        var t = TextoPesquisa.Normalizar(filtro ?? "");
        var saida = new Dictionary<string, TrechosDaLei>(StringComparer.Ordinal);
        if (t.Length < MinimoDoTermo) return saida;
        foreach (var item in itens)
        {
            var trechos = new List<ResultadoPesquisa>();
            var paginas = 0;
            for (var p = 0; p < item.Texto.Normalizados.Length; p++)
            {
                var n = Contar(item.Texto.Normalizados[p], t);
                if (n == 0) continue;
                paginas++;
                if (trechos.Count < porLei) trechos.Add(new ResultadoPesquisa(item.Anexo.Id, p + 1, Trecho(item.Texto.Originais[p], t), n));
            }
            if (paginas > 0) saida[item.Anexo.Id] = new TrechosDaLei(trechos, paginas);
        }
        return saida;
    }

    /// <summary>Onde o termo aparece dentro de um trecho (início e tamanho, no texto original), para destacá-lo; null se não aparece.</summary>
    public static (int Inicio, int Tamanho)? TermoNoTrecho(string trecho, string termo)
    {
        var t = TextoPesquisa.Normalizar(termo ?? "");
        if (t.Length == 0 || string.IsNullOrEmpty(trecho)) return null;
        var (normalizado, origem) = TextoPesquisa.ComMapa(trecho);
        var i = normalizado.IndexOf(t, StringComparison.Ordinal);
        if (i < 0) return null;
        var ultimo = origem[i + t.Length - 1];
        var fim = Math.Min(trecho.Length, ultimo + (char.IsHighSurrogate(trecho[ultimo]) ? 2 : 1));
        return (origem[i], fim - origem[i]);
    }

    /// <summary>Termo normalizado com menos de 2 caracteres ⇒ vazio. Um resultado por página com ocorrência, na ordem esfera,
    /// título, página; no máximo <paramref name="max"/> resultados, com o total real em <see cref="Pesquisa.Total"/>.</summary>
    public Pesquisa Pesquisar(string termo, int max = 200)
    {
        var t = TextoPesquisa.Normalizar(termo ?? "");
        if (t.Length < MinimoDoTermo) return new Pesquisa([], 0, 0);
        var resultados = new List<ResultadoPesquisa>();
        var total = 0;
        var leis = 0;
        foreach (var item in itens)
        {
            var achou = false;
            for (var p = 0; p < item.Texto.Normalizados.Length; p++)
            {
                var n = Contar(item.Texto.Normalizados[p], t);
                if (n == 0) continue;
                achou = true;
                total++;
                if (resultados.Count < max) resultados.Add(new ResultadoPesquisa(item.Anexo.Id, p + 1, Trecho(item.Texto.Originais[p], t), n));
            }
            if (achou) leis++;
        }
        return new Pesquisa(resultados, total, leis);
    }

    static int Contar(string texto, string termo)
    {
        var n = 0;
        for (var i = texto.IndexOf(termo, StringComparison.Ordinal); i >= 0; i = texto.IndexOf(termo, i + termo.Length, StringComparison.Ordinal)) n++;
        return n;
    }

    /// <summary>Até 160 caracteres do texto original (espaços colapsados) em volta da primeira ocorrência, com "…" onde cortou.</summary>
    static string Trecho(string original, string termoNormalizado)
    {
        var texto = TextoPesquisa.ColapsarEspacos(original);
        var (normalizado, origem) = TextoPesquisa.ComMapa(texto);
        var i = normalizado.IndexOf(termoNormalizado, StringComparison.Ordinal);
        if (i < 0) return texto.Length <= TamanhoTrecho ? texto : texto[..TamanhoTrecho] + Reticencias;
        var inicioAcerto = origem[i];
        var ultimo = origem[i + termoNormalizado.Length - 1];
        var fimAcerto = ultimo + (char.IsHighSurrogate(texto[ultimo]) && ultimo + 1 < texto.Length && char.IsLowSurrogate(texto[ultimo + 1]) ? 2 : 1);
        int inicio, fim;
        if (fimAcerto - inicioAcerto >= TamanhoTrecho) { inicio = inicioAcerto; fim = inicio + TamanhoTrecho; }
        else
        {
            inicio = Math.Max(0, inicioAcerto - (TamanhoTrecho - (fimAcerto - inicioAcerto)) / 2);
            fim = Math.Min(texto.Length, inicio + TamanhoTrecho);
            inicio = Math.Max(0, fim - TamanhoTrecho);
        }
        // A janela não corta um par de surrogates ao meio (emoji): encolhe um caractere.
        if (inicio > 0 && inicio < texto.Length && char.IsLowSurrogate(texto[inicio])) inicio++;
        if (fim < texto.Length && char.IsLowSurrogate(texto[fim])) fim--;
        var corte = texto[inicio..fim].Trim();
        return (inicio > 0 ? Reticencias : "") + corte + (fim < texto.Length ? Reticencias : "");
    }
}
