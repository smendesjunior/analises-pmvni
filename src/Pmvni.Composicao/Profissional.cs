using System.Globalization;

namespace Pmvni.Composicao;

/// <summary>Os dados do profissional que assina despachos e relatórios (2026-11-17, pedido do usuário: "ela é individual de cada profissional";
/// Ajustes › Despachos e PDF › Profissional responsável). Campo vazio = o texto do modelo continua.</summary>
public sealed record Profissional(string? Nome, string? Profissao, string? Registro, string? Matricula)
{
    /// <summary>Os dados em uso neste computador (o app define ao abrir e ao mudar os ajustes); null = os modelos ficam como estão.</summary>
    public static Profissional? Atual { get; set; }

    static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    static string? Limpo(string? s) => string.IsNullOrWhiteSpace(s) ? null : string.Join(' ', s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToUpper(Pt);

    public string? NomeLinha => Limpo(Nome);
    public string? ProfissaoLinha => Limpo(Profissao);
    public string? RegistroLinha => Limpo(Registro);

    /// <summary>"MATRÍCULA 969744" (o rótulo entra se o usuário digitou só o número).</summary>
    public string? MatriculaLinha => Limpo(Matricula) is { } m ? (m.StartsWith("MATR", StringComparison.Ordinal) ? m : "MATRÍCULA " + m) : null;

    public bool Vazio => NomeLinha is null && ProfissaoLinha is null && RegistroLinha is null && MatriculaLinha is null;

    /// <summary>O cargo do relatório ("ARQUITETO E URBANISTA — CAU/ES A297986-1"), com o que houver de <paramref name="atual"/> para o que faltar.</summary>
    public string Cargo(string? atual)
    {
        var partes = (atual ?? "").Split(" — ", 2);
        var prof = ProfissaoLinha ?? (partes[0].Trim() is { Length: > 0 } p ? p : null);
        var reg = RegistroLinha ?? (partes.Length > 1 && partes[1].Trim() is { Length: > 0 } r ? r : null);
        return string.Join(" — ", new[] { prof, reg }.Where(x => x is not null));
    }
}
