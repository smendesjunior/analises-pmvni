namespace Pmvni.Composicao.Kit;

/// <summary>Vocabulário de situação do relatório (<c>SIT</c>, <c>pill</c>, filtro <c>cor</c> e conjuntos do gerar_relatorio.py do kit).</summary>
public static class Situacoes
{
    /// <summary>Chave de dados → (texto impresso, classe de cor). A ordem é a do kit (aparece nas mensagens de erro).</summary>
    public static IReadOnlyDictionary<string, (string Texto, string Cor)> Sit { get; } =
        new Dictionary<string, (string Texto, string Cor)>(StringComparer.Ordinal)
        {
            ["ATENDE"] = ("ATENDE", "g"),
            ["NAO_ATENDE"] = ("NÃO ATENDE", "r"),
            ["NAO_COMPROVADO"] = ("FALTA COMPROVAR", "a"),
            ["DECISAO_DO_ANALISTA"] = ("O ANALISTA DEVE CONFERIR", "conferir"),
            ["DIVERGENCIA_INTERNA"] = ("INFORMAÇÕES DIVERGENTES", "o"),
            ["CONTA_CONFERE"] = ("ATENDE", "g"),
            ["PENDENTE"] = ("FALTA COMPROVAR", "a"),
            ["DIVERGENCIA"] = ("INFORMAÇÕES DIVERGENTES", "o"),
            ["CONFIRMADO"] = ("LOCAL CONFIRMADO", "g"),
            ["A_CONFIRMAR"] = ("LOCAL A CONFIRMAR", "a"),
        };

    public static IReadOnlySet<string> SitLocal { get; } = new HashSet<string>(StringComparer.Ordinal) { "CONFIRMADO", "A_CONFIRMAR" };

    public static IReadOnlySet<string> SitItem { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "ATENDE", "NAO_ATENDE", "NAO_COMPROVADO", "DIVERGENCIA_INTERNA", "DECISAO_DO_ANALISTA" };

    public static IReadOnlySet<string> SitIndice { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "CONTA_CONFERE", "NAO_ATENDE", "PENDENTE", "DIVERGENCIA", "DECISAO_DO_ANALISTA" };

    /// <summary>LEGENDA_PADRAO: legenda do mapa quando os dados não trazem uma.</summary>
    public static IReadOnlyList<object?> LegendaPadrao { get; } = new List<object?>
    {
        Item("lote", "Lote do projeto"),
        Item("edif", "Edificação no lote"),
        Item("app", "Faixa de APP"),
        Item("rio", "Curso d'água"),
        Item("zona", "Zonas do PDM"),
    };

    private static Dictionary<string, object?> Item(string tipo, string texto) => new() { ["tipo"] = tipo, ["texto"] = texto };

    /// <summary>pill(k): etiqueta HTML da situação; ValueError se desconhecida.</summary>
    public static string Pill(object? k)
    {
        if (k is not string s || !Sit.TryGetValue(s, out var t))
            throw new MoldeErro("Situação desconhecida: " + PythonTexto.Repr(k) + " (use " + string.Join(", ", Sit.Keys) + ")");
        return "<span class=\"pill " + t.Cor + "\"><i></i>" + t.Texto + "</span>";
    }

    /// <summary>Filtro <c>cor</c> do kit: classe de cor da situação.</summary>
    public static string Cor(object? v)
    {
        if (v is string s && Sit.TryGetValue(s, out var t)) return t.Cor;
        throw new MoldeErro("Situação desconhecida: " + PythonTexto.Repr(v));
    }
}

/// <summary>Filtros do molde: os básicos mais <c>pill</c>, <c>icone</c> e <c>cor</c> (o dicionário FILTROS do kit).</summary>
public static class Filtros
{
    public static IReadOnlyDictionary<string, Func<object?, object?>> Todos { get; } = Montar();

    private static Dictionary<string, Func<object?, object?>> Montar()
    {
        var f = new Dictionary<string, Func<object?, object?>>(Molde.FiltrosBasicos, StringComparer.Ordinal)
        {
            ["pill"] = v => new Seguro(Situacoes.Pill(v)),
            ["pill_vao"] = v => new Seguro(v is string s && s == "CONTA_CONFERE" ? "<span class=\"pill a\"><i></i>CONTA NOMINAL CONFERE</span>" : Situacoes.Pill(v)),
            ["icone"] = v => new Seguro(Icones.Icone(v)),
            ["cor"] = v => Situacoes.Cor(v),
        };
        return f;
    }
}
