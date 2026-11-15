using System.Globalization;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Uma linha da lista de leis anexadas (Ajustes › Legislação e o módulo Legislação): um anexo, com os textos já prontos.</summary>
public sealed record LinhaAnexo(string Id, string Titulo, Esfera Esfera, string Arquivo, string Tamanho, string Paginas, string AnexadoEm, string? Selo)
{
    /// <summary>A linha de detalhes: "&lt;arquivo&gt; · &lt;tamanho&gt; · &lt;páginas&gt; · &lt;anexado em&gt;".</summary>
    public string Detalhe => $"{Arquivo} · {Tamanho} · {Paginas} · {AnexadoEm}";

    /// <summary>Título e esfera editáveis: não numa linha sem a cópia do editor (desenho §11.2: só Substituir e Excluir).</summary>
    public bool Editavel => Selo != ListaLegislacao.SeloSemCopia;
}

/// <summary>Textos da lista de leis anexadas (desenho §11.1): <b>uma linha por anexo</b> (nunca agrupada nem resumida), agrupadas por
/// esfera só na vista, tamanho, páginas, total, selos e o resumo da anexação. Sem tipos WinUI: compilado também nos testes.</summary>
public static class ListaLegislacao
{
    static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    const long Mega = 1_048_576;

    public const string SeloSemCopia = "Cópia do editor não encontrada";

    public static LinhaAnexo Linha(Anexo a, EstadoAnexo estado, bool indexando, TimeZoneInfo fuso)
    {
        ArgumentNullException.ThrowIfNull(a);
        var local = TimeZoneInfo.ConvertTime(a.AnexadoEm, fuso);
        return new LinhaAnexo(a.Id, a.Titulo, a.Esfera, a.ArquivoOriginal, Tamanho(a.Tamanho), Paginas(a.Paginas),
            "anexado em " + local.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture), Selo(a, estado, indexando));
    }

    /// <summary>"Cópia do editor não encontrada" › "Preparando a pesquisa…" › "Sem texto pesquisável" › "2 de 81 páginas sem texto" › null.</summary>
    static string? Selo(Anexo a, EstadoAnexo estado, bool indexando)
    {
        if (estado == EstadoAnexo.SemCopia) return SeloSemCopia;
        if (indexando) return "Preparando a pesquisa…";
        if (!a.TemTexto) return "Sem texto pesquisável";
        if (a.PaginasSemTexto.Count > 0) return $"{a.PaginasSemTexto.Count} de {a.Paginas} páginas sem texto";
        return null;
    }

    /// <summary>Grupos na ordem <see cref="Esferas.Ordem"/> (grupo vazio omitido), cada um com as suas linhas na ordem recebida. O filtro
    /// vale sobre o título e o nome do arquivo, sem caixa nem acento (a normalização da pesquisa).</summary>
    public static IReadOnlyList<(string Grupo, IReadOnlyList<LinhaAnexo> Linhas)> Agrupar(IEnumerable<LinhaAnexo> l, string? filtro)
    {
        var termo = TextoPesquisa.Normalizar(filtro ?? "");
        var lista = l.Where(x => termo.Length == 0
            || TextoPesquisa.Normalizar(x.Titulo).Contains(termo, StringComparison.Ordinal)
            || TextoPesquisa.Normalizar(x.Arquivo).Contains(termo, StringComparison.Ordinal)).ToList();
        return Esferas.Ordem
            .Select(e => (Grupo: Esferas.Rotulo(e), Linhas: (IReadOnlyList<LinhaAnexo>)lista.Where(x => x.Esfera == e).ToList()))
            .Where(g => g.Linhas.Count > 0).ToList();
    }

    /// <summary>Abaixo de 1 MB, "&lt;n&gt; KB" arredondado para cima; senão "12,3 MB" (MB = 1.048.576 bytes, uma casa).</summary>
    public static string Tamanho(long bytes)
    {
        if (bytes < Mega) return Math.Max(1, (bytes + 1023) / 1024).ToString(CultureInfo.InvariantCulture) + " KB";
        return (bytes / (double)Mega).ToString("0.0", PtBr) + " MB";
    }

    public static string Paginas(int n) => n == 1 ? "1 página" : n.ToString(CultureInfo.InvariantCulture) + " páginas";

    /// <summary>"Nenhuma lei anexada", "1 lei anexada · 2 KB", "4 leis anexadas · 12,3 MB".</summary>
    public static string Total(IReadOnlyList<Anexo> a)
    {
        if (a.Count == 0) return "Nenhuma lei anexada";
        var tamanho = Tamanho(a.Sum(x => x.Tamanho));
        return a.Count == 1 ? "1 lei anexada · " + tamanho : $"{a.Count} leis anexadas · {tamanho}";
    }

    /// <summary>"2 PDFs anexados." / "1 PDF anexado." / "Nenhum PDF anexado; 1 recusado:" / "1 PDF anexado; 4 recusados:", seguido de
    /// uma linha por recusa (separadas por "\n").</summary>
    public static string Resumo(int anexados, IReadOnlyList<string> recusas)
    {
        var parte = anexados switch { 0 => "Nenhum PDF anexado", 1 => "1 PDF anexado", _ => $"{anexados} PDFs anexados" };
        if (recusas.Count == 0) return parte + ".";
        var linhas = new List<string> { parte + "; " + (recusas.Count == 1 ? "1 recusado:" : $"{recusas.Count} recusados:") };
        linhas.AddRange(recusas);
        return string.Join("\n", linhas);
    }
}
