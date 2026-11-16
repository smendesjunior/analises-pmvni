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

    /// <summary>O processo aberto no cabeçalho em duas linhas (correções 2026-11-15): em cima, maior e em negrito, "Proc. 9001/2026"; embaixo,
    /// menor, o requerente. Sem processo, o requerente sobe para a linha de cima; sem os dois, o nome do arquivo (e a de baixo fica vazia).</summary>
    public static (string Principal, string Secundaria) Linhas(string? processo, string? requerente, string nomeArquivo)
    {
        var p = string.IsNullOrWhiteSpace(processo) ? null : "Proc. " + processo.Trim();
        var r = string.IsNullOrWhiteSpace(requerente) ? null : requerente.Trim();
        return (p, r) switch
        {
            (null, null) => (nomeArquivo, ""),
            (null, { } so) => (so, ""),
            ({ } proc, var req) => (proc, req ?? ""),
        };
    }
}
