using System.Globalization;

namespace Pmvni.Composicao;

/// <summary>Os dados do profissional que assina despachos e relatórios (2026-11-17, pedido do usuário: "ela é individual de cada profissional";
/// Ajustes › Profissional e assinatura › Profissional responsável). Campo vazio sai em branco (nunca o texto do modelo).</summary>
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

    /// <summary>O cargo do relatório ("PROFISSÃO — REGISTRO"), só com o que está nos Ajustes; campo vazio fica de fora (2026-11-17, correção:
    /// antes completava com o cargo dos dados, que trazia o registro de outro profissional).</summary>
    public string Cargo() => string.Join(" — ", new[] { ProfissaoLinha, RegistroLinha }.Where(x => x is not null));
}
