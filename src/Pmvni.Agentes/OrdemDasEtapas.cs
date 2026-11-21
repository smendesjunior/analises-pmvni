using System.Text.Json;

namespace Pmvni.Agentes;

/// <summary>A sequência das etapas da análise que o kit define em <c>references\etapas.json</c> do plugin analise (4.0.0+; o mesmo arquivo serve às três rotas).
/// <see cref="Todas"/> é a <c>ordem</c> do arquivo, inclusive as etapas opcionais que alguma rota desliga; <see cref="Para"/> tira as desligadas na rota
/// (<c>etapas.&lt;id&gt;.ligada_por_rota</c>, só nas etapas <c>opcional</c>). Sem o arquivo (kit 3.x) ou com arquivo inválido, vale a <see cref="Padrao"/>, a ordem
/// antiga fixa do Editor, sem erro. O Editor só lê; quem decide a ordem é o kit.</summary>
public sealed class OrdemDasEtapas
{
    static readonly string[] OrdemAntiga = ["prepare", "analyze", "technical_audit", "produce", "audit", "final_review"];

    /// <summary>A ordem do kit 3.x (e de qualquer arquivo ausente ou inválido): produce antes de audit, todas ligadas nas três rotas.</summary>
    public static OrdemDasEtapas Padrao { get; } = new(OrdemAntiga, new Dictionary<Provedor, HashSet<string>>(), doArquivo: false);

    readonly Dictionary<Provedor, HashSet<string>> desligadas;

    OrdemDasEtapas(IReadOnlyList<string> todas, Dictionary<Provedor, HashSet<string>> desligadas, bool doArquivo)
    {
        Todas = todas;
        this.desligadas = desligadas;
        DoArquivo = doArquivo;
    }

    /// <summary>Todas as etapas do arquivo, na ordem em que rodam, inclusive as desligadas em alguma rota.</summary>
    public IReadOnlyList<string> Todas { get; }

    /// <summary>true se a ordem veio do <c>etapas.json</c> do kit; false se é a <see cref="Padrao"/>.</summary>
    public bool DoArquivo { get; }

    /// <summary>As etapas que rodam na rota do provedor, na ordem; uma etapa desligada na rota não entra (nem no total nem na previsão). Sem provedor (pasta
    /// de controle desconhecida), todas.</summary>
    public IReadOnlyList<string> Para(Provedor? provedor) =>
        provedor is { } p && desligadas.TryGetValue(p, out var fora) && fora.Count > 0 ? Todas.Where(e => !fora.Contains(e)).ToList() : Todas;

    /// <summary>A rota de cada provedor no <c>ligada_por_rota</c> do arquivo (mesmos nomes das pastas de <c>scripts\rotas</c>).</summary>
    static readonly (string Rota, Provedor Provedor)[] Rotas = [("codex", Provedor.ChatGpt), ("claude", Provedor.Claude), ("gemini", Provedor.Gemini)];

    /// <summary>Lê o <c>etapas.json</c> (<see cref="Pecas.EtapasJson"/>). null, arquivo inexistente, JSON inválido ou fora do formato conhecido: a
    /// <see cref="Padrao"/>. Barato para chamar a cada segundo (o arquivo só é relido quando muda).</summary>
    public static OrdemDasEtapas Ler(string? arquivo)
    {
        if (string.IsNullOrWhiteSpace(arquivo)) return Padrao;
        try
        {
            var caminho = Path.GetFullPath(arquivo);
            var info = new FileInfo(caminho);
            if (!info.Exists) return Padrao;
            // O resultado fica guardado enquanto a data e o tamanho do arquivo não mudam (o Monitor chama isto a cada segundo).
            lock (trava)
                if (guardadas.TryGetValue(caminho, out var g) && g.Escrita == info.LastWriteTimeUtc && g.Tamanho == info.Length) return g.Ordem;
            var ordem = Interpretar(ArquivoJsonDoKit.Ler(caminho));
            lock (trava) guardadas[caminho] = new Guardada(info.LastWriteTimeUtc, info.Length, ordem);
            return ordem;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return Padrao; }
    }

    sealed record Guardada(DateTime Escrita, long Tamanho, OrdemDasEtapas Ordem);

    static readonly Dictionary<string, Guardada> guardadas = new(StringComparer.OrdinalIgnoreCase);
    static readonly object trava = new();

    /// <summary>O formato da versão 1: <c>ordem</c> (lista de ids, sem repetição, não vazia) e, em <c>etapas</c>, o <c>ligada_por_rota</c> das etapas
    /// <c>opcional</c>. Um id da <c>ordem</c> que não é texto, repetido ou vazio invalida o arquivo inteiro; um <c>ligada_por_rota</c> mal formado vale como
    /// ligado.</summary>
    static OrdemDasEtapas Interpretar(JsonElement? json)
    {
        if (json is not { ValueKind: JsonValueKind.Object } raiz
            || !raiz.TryGetProperty("ordem", out var ordem) || ordem.ValueKind != JsonValueKind.Array) return Padrao;
        var todas = new List<string>();
        foreach (var item in ordem.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } id || todas.Contains(id)) return Padrao;
            todas.Add(id);
        }
        if (todas.Count == 0) return Padrao;

        var fora = new Dictionary<Provedor, HashSet<string>>();
        if (raiz.TryGetProperty("etapas", out var etapas) && etapas.ValueKind == JsonValueKind.Object)
            foreach (var id in todas)
            {
                if (!etapas.TryGetProperty(id, out var e) || e.ValueKind != JsonValueKind.Object
                    || !e.TryGetProperty("opcional", out var opcional) || opcional.ValueKind != JsonValueKind.True
                    || !e.TryGetProperty("ligada_por_rota", out var ligada) || ligada.ValueKind != JsonValueKind.Object) continue;
                foreach (var (rota, provedor) in Rotas)
                    if (ligada.TryGetProperty(rota, out var v) && v.ValueKind == JsonValueKind.False)
                    {
                        if (!fora.TryGetValue(provedor, out var conjunto)) fora[provedor] = conjunto = new HashSet<string>(StringComparer.Ordinal);
                        conjunto.Add(id);
                    }
            }
        return new OrdemDasEtapas(todas, fora, doArquivo: true);
    }
}
