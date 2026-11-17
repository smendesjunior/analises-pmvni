using System.Globalization;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>A pasta da etapa do despacho na pasta do processo. <see cref="Pasta"/> é o caminho completo (ainda não existe se <see cref="Nova"/>);
/// <see cref="Nome"/> é o nome da pasta (<c>DESPACHO 03 - 2099-03-10</c>); <see cref="Data"/> é a data do despacho.</summary>
public sealed record EtapaDespacho(string Pasta, string Nome, int Numero, DateOnly Data, bool Nova);

/// <summary>Pastas <c>DESPACHO NN - AAAA-MM-DD</c> (regra do kit: despacho da mesma análise reutiliza a pasta; etapa nova usa o número
/// seguinte). Só lê: nada é criado aqui.</summary>
public static class Etapas
{
    static readonly Regex QualquerDespacho = new(@"^DESPACHO\s+(\d+)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    static readonly Regex ComData = new(@"^DESPACHO\s+(\d{1,3})\s+-\s+(\d{4}-\d{2}-\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    static readonly string[] Meses =
        { "janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro" };

    static IEnumerable<string> Subpastas(string pastaProcesso) =>
        Directory.Exists(pastaProcesso) ? Directory.EnumerateDirectories(pastaProcesso).Where(p => !Processos.EhLink(p)) : Enumerable.Empty<string>();

    /// <summary>Etapa nova: NN = maior número de subpasta <c>DESPACHO NN…</c> (com ou sem data) + 1; data = hoje.</summary>
    public static EtapaDespacho Nova(string pastaProcesso, DateOnly hoje)
    {
        ArgumentNullException.ThrowIfNull(pastaProcesso);
        var maior = 0;
        foreach (var p in Subpastas(pastaProcesso))
        {
            var m = QualquerDespacho.Match(Path.GetFileName(p));
            if (m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n)) maior = Math.Max(maior, n);
        }
        var numero = maior + 1;
        var nome = string.Create(CultureInfo.InvariantCulture, $"DESPACHO {numero:00} - {hoje:yyyy-MM-dd}");
        return new EtapaDespacho(Path.Combine(pastaProcesso, nome), nome, numero, hoje, true);
    }

    /// <summary>As pastas <c>DESPACHO NN - AAAA-MM-DD</c> com data válida, da de maior número para a de menor.</summary>
    public static IReadOnlyList<EtapaDespacho> Existentes(string pastaProcesso)
    {
        ArgumentNullException.ThrowIfNull(pastaProcesso);
        var lista = new List<EtapaDespacho>();
        foreach (var p in Subpastas(pastaProcesso))
        {
            var nome = Path.GetFileName(p);
            var m = ComData.Match(nome);
            if (!m.Success
                || !DateOnly.TryParseExact(m.Groups[2].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
                continue;
            lista.Add(new EtapaDespacho(p, nome, int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), data, false));
        }
        return lista.OrderByDescending(e => e.Numero).ThenBy(e => e.Nome, StringComparer.Ordinal).ToList();
    }

    /// <summary>A existente de hoje (2026-11-17: o despacho vai sempre para a pasta da data dele) de maior número que ainda não tem despacho (nenhum <c>.docx</c>/<c>.pdf</c> que comece por <c>Despacho_</c>, sem
    /// caixa); se não houver, a <see cref="Nova"/>.</summary>
    public static EtapaDespacho Sugerida(string pastaProcesso, DateOnly hoje) =>
        Existentes(pastaProcesso).FirstOrDefault(e => e.Data == hoje && !TemDespacho(e.Pasta)) ?? Nova(pastaProcesso, hoje);

    static bool TemDespacho(string pasta)
    {
        try
        {
            return Directory.EnumerateFiles(pasta).Select(Path.GetFileName).Any(n =>
                n!.StartsWith("Despacho_", StringComparison.OrdinalIgnoreCase)
                && (n.EndsWith(".docx", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return true; } // ilegível: não sugere reaproveitar
    }

    /// <summary>"14 de novembro de 2026"; dia 1 = "1º" (Manual de Redação da Presidência).</summary>
    public static string DataPorExtenso(DateOnly d) =>
        (d.Day == 1 ? "1º" : d.Day.ToString(CultureInfo.InvariantCulture)) + " de " + Meses[d.Month - 1] + " de "
        + d.Year.ToString(CultureInfo.InvariantCulture);
}
