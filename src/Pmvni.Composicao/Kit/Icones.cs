namespace Pmvni.Composicao.Kit;

/// <summary>Ícones SVG do relatório (<c>IC</c> e <c>icone</c> do gerar_relatorio.py do kit).</summary>
public static class Icones
{
    public static IReadOnlyDictionary<string, string> Ic { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["plot"] = """<path d="M4.5 6.5l12.5-2.5 2.5 12-13 4z" stroke-dasharray="2.6 1.8"/><circle cx="4.5" cy="6.5" r="1.3"/><circle cx="19.5" cy="16" r="1.3"/>""",
        ["to"] = """<rect x="3.5" y="3.5" width="17" height="17" rx="2"/><rect x="7.5" y="9" width="8.5" height="7.5" rx="1" fill="currentColor" fill-opacity=".18"/>""",
        ["ca"] = """<path d="M3.5 20.5h17M6 20.5V9.5h12v11M6 15h12M9 9.5V5h6v4.5"/>""",
        ["tp"] = """<path d="M12 3.2c3 4 6 7 6 10.6a6 6 0 0 1-12 0C6 10.2 9 7.2 12 3.2z"/><path d="M9.4 14.2A2.6 2.6 0 0 0 12 16.8"/>""",
        ["pav"] = """<rect x="3.5" y="5" width="10" height="15.5" rx="1"/><path d="M3.5 12.8h10M19 4.5v16M17.2 6.3L19 4.5l1.8 1.8M17.2 18.7l1.8 1.8 1.8-1.8"/>""",
        ["afa"] = """<path d="M3.5 4.5v15M20.5 4.5v15M6.5 12h11M8.8 9.6L6.4 12l2.4 2.4M15.2 9.6l2.4 2.4-2.4 2.4"/>""",
        ["bed"] = """<path d="M3.5 18.5V7M3.5 14h17v4.5M20.5 14v-1.6a3 3 0 0 0-3-3h-6.5V14"/><circle cx="7.3" cy="11.2" r="1.7"/>""",
        ["wc"] = """<path d="M6.5 3.5h4.5v7.5H6.5z"/><path d="M5 11h14a5 5 0 0 1-5 5h-4a5 5 0 0 1-5-5z"/><path d="M9.5 16l-1 4.5h7l-1-4.5"/>""",
        ["pot"] = """<path d="M4.5 10.5h15v4.5a5 5 0 0 1-5 5h-5a5 5 0 0 1-5-5z"/><path d="M2.5 10.5h2M19.5 10.5h2"/><path d="M9.5 3.5c-1 1.4 1 2.4 0 4M14 3.5c-1 1.4 1 2.4 0 4"/>""",
        ["wash"] = """<rect x="4.5" y="3" width="15" height="18" rx="2.2"/><circle cx="12" cy="13.2" r="4.2"/><path d="M7.5 6.6h2.2M16 6.6h.01"/>""",
        ["sun"] = """<circle cx="9" cy="9" r="3.2"/><path d="M9 2.6v1.5M9 13.9v1.5M2.6 9h1.5M13.9 9h1.5M4.5 4.5l1 1M12.5 12.5l1 1M4.5 13.5l1-1M12.5 5.5l1-1"/><path d="M11.5 18.2h7.3a2 2 0 1 0-2-2.1M14 21.2h4"/>""",
        ["pin"] = """<path d="M12 21s-6-5.4-6-10.8a6 6 0 0 1 12 0C18 15.6 12 21 12 21z"/><circle cx="12" cy="10.2" r="2.1"/>""",
    };

    /// <summary>icone(k): ValueError se o ícone não existir.</summary>
    public static string Icone(object? k)
    {
        if (k is not string s || !Ic.TryGetValue(s, out var corpo))
            throw new MoldeErro("Ícone desconhecido: " + PythonTexto.Repr(k) + " (use " + string.Join(", ", Ic.Keys) + ")");
        var cls = s == "pin" ? "ico tg" : "ico tic";
        return "<svg class=\"" + cls + "\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"1.6\" stroke-linecap=\"round\" stroke-linejoin=\"round\">" + corpo + "</svg>";
    }
}
