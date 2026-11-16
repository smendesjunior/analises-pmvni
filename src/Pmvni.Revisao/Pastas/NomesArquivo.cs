using System.Globalization;
using System.Text.RegularExpressions;

namespace Pmvni.Revisao;

/// <summary>Nomes de arquivo que o app sugere ou cria: o pacote de uma importação, o número do processo lido da capa, um nome livre
/// (sem sobrescrever nada) e a cópia de um pacote em conflito.</summary>
public static class NomesArquivo
{
    static readonly Regex Processo = new(@"^\s*(\d{1,6})\s*/\s*(\d{4})\s*$", RegexOptions.CultureInvariant);

    /// <summary><c>Relatorio_98765_2099.rpmvni</c> (número com pelo menos 4 dígitos: <c>Relatorio_0001_2099.rpmvni</c>); sem número ou ano,
    /// <c>Relatorio.rpmvni</c>.</summary>
    public static string Importacao(int? numero, int? ano) =>
        numero is { } n && ano is { } a
            ? string.Create(CultureInfo.InvariantCulture, $"Relatorio_{n:D4}_{a}.rpmvni")
            : "Relatorio.rpmvni";

    /// <summary><c>"9001/2026"</c> → (9001, 2026); qualquer outra forma (vazio, campo [ ] em aberto) → (null, null).</summary>
    public static (int? Numero, int? Ano) DoProcessoDaCapa(string? processo)
    {
        if (processo is null) return (null, null);
        var m = Processo.Match(processo);
        if (!m.Success) return (null, null);
        return (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    }

    /// <summary>O caminho em <paramref name="pasta"/> para <paramref name="nome"/> que ainda não existe: <c>nome</c>, <c>nome_2</c>, <c>nome_3</c>…</summary>
    public static string Livre(string pasta, string nome)
    {
        var baseNome = Path.GetFileNameWithoutExtension(nome);
        var ext = Path.GetExtension(nome);
        var caminho = Path.Combine(pasta, nome);
        for (var i = 2; File.Exists(caminho) || Directory.Exists(caminho); i++)
            caminho = Path.Combine(pasta, string.Create(CultureInfo.InvariantCulture, $"{baseNome}_{i}{ext}"));
        return caminho;
    }

    /// <summary>Ao lado do pacote: <c>"&lt;nome&gt; (cópia 2026-11-14 14-32).rpmvni"</c> (hora local), sem sobrescrever.</summary>
    public static string CopiaDeConflito(string pacote, DateTime local)
    {
        var pasta = Path.GetDirectoryName(Path.GetFullPath(pacote))!;
        var nome = Path.GetFileNameWithoutExtension(pacote)
            + " (cópia " + local.ToString("yyyy-MM-dd HH-mm", CultureInfo.InvariantCulture) + ")" + Path.GetExtension(pacote);
        return Livre(pasta, nome);
    }
}
