namespace Pmvni.App;

/// <summary>Página Inicial › Figuras ▾ (correções 2026-11-16): os textos do menu das figuras do relatório. Sem tipos WinUI (testável).</summary>
public static class MenuFiguras
{
    /// <summary>"Figura 1 — Implantação" (sem título: só "Figura 1"), na ordem em que as figuras saem no relatório.</summary>
    public static IReadOnlyList<(string Id, string Texto)> Itens(IReadOnlyList<(string Id, string? Titulo)> figuras) =>
        figuras.Select((f, i) => (f.Id, "Figura " + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + (string.IsNullOrWhiteSpace(f.Titulo) ? "" : " — " + f.Titulo!.Trim()))).ToList();
}
