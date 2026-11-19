using System.Globalization;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>O tipo da norma (botões da lista e selo do cartão).</summary>
public enum TipoNorma { Lei, Decreto, LeiComplementar, NormaTecnica, Outro }

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

    // ---- Lista nova da Legislação (2026-11-16): esferas em cartões, tipo da norma, favoritas ----

    /// <summary>O tipo da norma, tirado do título: "Lei Complementar"/"LC" → LC; "Norma Técnica"/"NT" → NT; "Decreto" → Decreto;
    /// "Lei" → Lei; o resto (portaria, resolução, instrução…) fica em <see cref="TipoNorma.Outro"/> e conta só em "Todas".</summary>
    public static TipoNorma TipoDoTitulo(string? titulo)
    {
        var t = TextoPesquisa.Normalizar(titulo ?? "");
        if (t.Length == 0) return TipoNorma.Outro;
        if (t.Contains("lei complementar", StringComparison.Ordinal) || Comeca(t, "lc")) return TipoNorma.LeiComplementar;
        if (t.Contains("norma tecnica", StringComparison.Ordinal) || Comeca(t, "nt") || Comeca(t, "nbr")) return TipoNorma.NormaTecnica;
        if (Comeca(t, "decreto")) return TipoNorma.Decreto;
        if (Comeca(t, "lei")) return TipoNorma.Lei;
        if (t.Contains("decreto", StringComparison.Ordinal)) return TipoNorma.Decreto;
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"\blei\b")) return TipoNorma.Lei;
        return TipoNorma.Outro;
    }

    /// <summary>Começa com a palavra inteira (seguida de espaço, número ou pontuação): "lc 70" sim, "lcd" não.</summary>
    static bool Comeca(string t, string palavra) =>
        t.StartsWith(palavra, StringComparison.Ordinal) && (t.Length == palavra.Length || !char.IsLetter(t[palavra.Length]));

    public static string RotuloTipo(TipoNorma t) => t switch
    {
        TipoNorma.Lei => "Lei",
        TipoNorma.Decreto => "Decreto",
        TipoNorma.LeiComplementar => "LC",
        TipoNorma.NormaTecnica => "NT",
        _ => "Outra",
    };

    /// <summary>O rótulo plural do botão de tipo: "Leis", "Decretos", "LC", "NT".</summary>
    public static string RotuloBotaoTipo(TipoNorma t) => t switch
    {
        TipoNorma.Lei => "Leis",
        TipoNorma.Decreto => "Decretos",
        _ => RotuloTipo(t),
    };

    /// <summary>Os tipos com botão, na ordem da tela (depois de "Todas").</summary>
    public static IReadOnlyList<TipoNorma> TiposComBotao { get; } = [TipoNorma.Lei, TipoNorma.Decreto, TipoNorma.LeiComplementar, TipoNorma.NormaTecnica];

    /// <summary>As esferas com cartão (Outras só conta em "Todas").</summary>
    public static IReadOnlyList<Esfera> EsferasComCartao { get; } = [Esfera.Federal, Esfera.Estadual, Esfera.Municipal];

    static bool PassaTexto(LinhaAnexo x, string termo) => termo.Length == 0
        || TextoPesquisa.Normalizar(x.Titulo).Contains(termo, StringComparison.Ordinal)
        || TextoPesquisa.Normalizar(x.Arquivo).Contains(termo, StringComparison.Ordinal);

    /// <summary>A lista à vista: filtro de texto, esfera (null = todas) e tipo (null = todas), com as favoritas no topo e, dentro de cada
    /// parte, a ordem das esferas e depois a ordem recebida.</summary>
    public static IReadOnlyList<LinhaAnexo> Filtrar(IEnumerable<LinhaAnexo> l, string? filtro, Esfera? esfera, TipoNorma? tipo, IReadOnlySet<string> favoritas)
    {
        var termo = TextoPesquisa.Normalizar(filtro ?? "");
        return l.Select((x, i) => (x, i))
            .Where(p => PassaTexto(p.x, termo) && (esfera is null || p.x.Esfera == esfera) && (tipo is null || TipoDoTitulo(p.x.Titulo) == tipo))
            .OrderBy(p => favoritas.Contains(p.x.Id) ? 0 : 1)
            .ThenBy(p => Esferas.Indice(p.x.Esfera))
            .ThenBy(p => p.i)
            .Select(p => p.x).ToList();
    }

    /// <summary>Quantas leis em cada esfera com cartão (só o filtro de texto vale).</summary>
    public static IReadOnlyDictionary<Esfera, int> ContarEsferas(IEnumerable<LinhaAnexo> l, string? filtro)
    {
        var termo = TextoPesquisa.Normalizar(filtro ?? "");
        var lista = l.Where(x => PassaTexto(x, termo)).ToList();
        return EsferasComCartao.ToDictionary(e => e, e => lista.Count(x => x.Esfera == e));
    }

    /// <summary>Quantas leis em cada tipo (filtro de texto e esfera valem); a chave null é "Todas".</summary>
    public static IReadOnlyList<(TipoNorma? Tipo, int Quantidade)> ContarTipos(IEnumerable<LinhaAnexo> l, string? filtro, Esfera? esfera)
    {
        var termo = TextoPesquisa.Normalizar(filtro ?? "");
        var lista = l.Where(x => PassaTexto(x, termo) && (esfera is null || x.Esfera == esfera)).ToList();
        var r = new List<(TipoNorma?, int)> { (null, lista.Count) };
        r.AddRange(TiposComBotao.Select(t => ((TipoNorma?)t, lista.Count(x => TipoDoTitulo(x.Titulo) == t))));
        return r;
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
    /// uma linha por recusa (separadas por "\n").
    /// <paramref name="ignorados"/> (2026-11-17): leis que já estavam anexadas (mesmo nome ou mesmo conteúdo), puladas sem erro.</summary>
    public static string Resumo(int anexados, IReadOnlyList<string> recusas, int ignorados = 0)
    {
        var parte = anexados switch { 0 => "Nenhum PDF anexado", 1 => "1 PDF anexado", _ => $"{anexados} PDFs anexados" };
        if (ignorados > 0) parte += ignorados == 1 ? " (1 já estava anexado e foi ignorado)" : $" ({ignorados} já estavam anexados e foram ignorados)";
        if (recusas.Count == 0) return parte + ".";
        var linhas = new List<string> { parte + "; " + (recusas.Count == 1 ? "1 recusado:" : $"{recusas.Count} recusados:") };
        linhas.AddRange(recusas);
        return string.Join("\n", linhas);
    }
}
