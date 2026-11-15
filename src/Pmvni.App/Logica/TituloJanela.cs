namespace Pmvni.App;

/// <summary>Título da barra: <c>"Proc. 9001/2026 · FULANO DE TAL"</c>; sem processo nem requerente, o nome do arquivo.</summary>
public static class TituloJanela
{
    public static string De(string? processo, string? requerente, string nomeArquivo)
    {
        var p = string.IsNullOrWhiteSpace(processo) ? null : "Proc. " + processo.Trim();
        var r = string.IsNullOrWhiteSpace(requerente) ? null : requerente.Trim();
        return p is null && r is null ? nomeArquivo : string.Join(" · ", new[] { p, r }.Where(s => s is not null));
    }
}
