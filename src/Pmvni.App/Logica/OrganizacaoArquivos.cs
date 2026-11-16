using System.Globalization;
using System.Text.RegularExpressions;

namespace Pmvni.App;

/// <summary>Como os arquivos do processo são agrupados: pela etapa ou pasta em que estão (padrão), pelo tipo de arquivo, pela data
/// (como o Explorador: Hoje, Semana passada…) ou sem grupos.</summary>
public enum AgrupamentoArquivos { Etapa, Tipo, Data, Nenhum }

/// <summary>Por que os arquivos são ordenados dentro de cada grupo.</summary>
public enum OrdemArquivos { Nome, Data, Tipo }

/// <summary>A escolha de Agrupar e Classificar da página do processo (lembrada nos Ajustes).</summary>
public sealed record ExibicaoArquivos(AgrupamentoArquivos Agrupar, OrdemArquivos Ordenar, bool Decrescente)
{
    public static ExibicaoArquivos Padrao { get; } = new(AgrupamentoArquivos.Etapa, OrdemArquivos.Nome, false);

    /// <summary>Dos textos guardados em <see cref="Ajustes"/> (valor desconhecido = o padrão).</summary>
    public static ExibicaoArquivos DosAjustes(string? agrupar, string? ordenar, bool decrescente) => new(
        agrupar switch { "tipo" => AgrupamentoArquivos.Tipo, "data" => AgrupamentoArquivos.Data, "nenhum" => AgrupamentoArquivos.Nenhum, _ => AgrupamentoArquivos.Etapa },
        ordenar switch { "data" => OrdemArquivos.Data, "tipo" => OrdemArquivos.Tipo, _ => OrdemArquivos.Nome },
        decrescente);

    public string AgruparNosAjustes => Agrupar switch
    {
        AgrupamentoArquivos.Tipo => "tipo", AgrupamentoArquivos.Data => "data", AgrupamentoArquivos.Nenhum => "nenhum", _ => "etapa",
    };

    public string OrdenarNosAjustes => Ordenar switch { OrdemArquivos.Data => "data", OrdemArquivos.Tipo => "tipo", _ => "nome" };
}

/// <summary>Uma linha (modo Lista) ou um bloco (modo Miniaturas) da página do processo: um arquivo (<see cref="Linha"/>), o título de um
/// grupo ou um aviso de "nenhum". Só o arquivo pode ser escolhido.</summary>
public sealed class ItemArquivo
{
    ItemArquivo(LinhaDetalhe? linha, string texto, string info, bool titulo)
    {
        Linha = linha;
        Texto = texto;
        Info = info;
        EhTitulo = titulo;
    }

    public static ItemArquivo DoTitulo(string titulo) => new(null, titulo, "", true);
    public static ItemArquivo DoAviso(string texto) => new(null, texto, "", false);
    public static ItemArquivo DoArquivo(LinhaDetalhe linha, string texto, string info) => new(linha, texto, info, false);

    /// <summary>A linha do detalhe que este item mostra (a escolha da página é ela); null nos títulos e avisos.</summary>
    public LinhaDetalhe? Linha { get; }
    public string Texto { get; }
    public string Info { get; }
    public bool EhTitulo { get; }
    public bool NaoEhTitulo => !EhTitulo;
    public bool TemInfo => Info.Length > 0;
    public bool Selecionavel => Linha is not null;
    public string? Caminho => Linha?.Caminho;

    /// <summary>O que a dica mostra: o nome inteiro e, embaixo, a informação.</summary>
    public string Dica => TemInfo ? Texto + Environment.NewLine + Info : Texto;

    /// <summary>O que o Narrador lê: "Grupo Hoje" nos títulos; o nome e a informação nos arquivos.</summary>
    public string NomeAcessivel => EhTitulo ? "Grupo " + Texto : TemInfo ? Texto + ", " + Info : Texto;

    public override string ToString() => NomeAcessivel;
}

/// <summary>Um grupo de arquivos com o seu título ("" quando não há agrupamento).</summary>
public sealed record GrupoArquivos(string Titulo, IReadOnlyList<ItemArquivo> Itens);

/// <summary>Agrupar e ordenar os arquivos do processo (página do processo, 2026-11-16), sem tipos de interface e sem tocar o disco: tudo
/// sobre as <see cref="LinhaDetalhe"/> que a leitura já trouxe (nome, pasta, data e tamanho).</summary>
public static partial class OrganizacaoArquivos
{
    public const string SemData = "Sem data";
    public const string TituloSemGrupo = "Arquivos";

    /// <summary>Os grupos de data do Explorador, do mais novo ao mais antigo.</summary>
    public static IReadOnlyList<string> GruposDeData { get; } =
    [
        "Data futura", "Hoje", "Ontem", "No início desta semana", "Semana passada", "Anteriormente neste mês", "Último mês",
        "Anteriormente neste ano", "Há muito tempo", SemData,
    ];

    [GeneratedRegex(@"^DESPACHO\s+(\d{1,3})\s+-\s+(\d{4})-(\d{2})-(\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PastaDeDespacho();

    /// <summary>Ordem natural, como a do Explorador: os números contam pelo valor ("2" antes de "10"), sem diferenciar maiúsculas nem
    /// acentos. Nomes iguais nessa comparação desempatam pelos zeros à esquerda e, por fim, caractere a caractere.</summary>
    public static int CompararNatural(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        var comparador = Formatos.Cultura.CompareInfo;
        int i = 0, j = 0, empate = 0;
        while (i < a.Length && j < b.Length)
        {
            int i0 = i, j0 = j;
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var na = a.AsSpan(i0, i - i0).TrimStart('0');
                var nb = b.AsSpan(j0, j - j0).TrimStart('0');
                if (na.Length != nb.Length) return na.Length < nb.Length ? -1 : 1;
                var c = na.SequenceCompareTo(nb);
                if (c != 0) return c < 0 ? -1 : 1;
                if (empate == 0) empate = (i - i0).CompareTo(j - j0);
            }
            else
            {
                // Um trecho sem dígitos de cada lado (vazio no lado que começa com dígito: número vem antes de letra).
                while (i < a.Length && !char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && !char.IsAsciiDigit(b[j])) j++;
                var c = comparador.Compare(a.AsSpan(i0, i - i0), b.AsSpan(j0, j - j0), CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace);
                if (c != 0) return c < 0 ? -1 : 1;
            }
        }
        if (a.Length - i != b.Length - j) return a.Length - i < b.Length - j ? -1 : 1;
        return empate != 0 ? empate : Math.Sign(string.CompareOrdinal(a, b));
    }

    static readonly Comparer<string> Natural = Comparer<string>.Create(CompararNatural);

    /// <summary>O grupo de data do Explorador para <paramref name="data"/>, visto de <paramref name="hoje"/> (a semana começa no domingo).
    /// Vale o primeiro que servir: "Semana passada" ganha de "Último mês" quando a semana passada caiu no mês anterior.</summary>
    public static string GrupoDeData(DateOnly? data, DateOnly hoje)
    {
        if (data is not { } d) return SemData;
        if (d > hoje) return "Data futura";
        if (d == hoje) return "Hoje";
        if (d.DayNumber == hoje.DayNumber - 1) return "Ontem";
        var inicioDaSemana = hoje.DayNumber - (int)hoje.DayOfWeek;
        if (d.DayNumber >= inicioDaSemana) return "No início desta semana";
        if (d.DayNumber >= inicioDaSemana - 7) return "Semana passada";
        var inicioDoMes = new DateOnly(hoje.Year, hoje.Month, 1);
        if (d >= inicioDoMes) return "Anteriormente neste mês";
        if (d >= inicioDoMes.AddMonths(-1)) return "Último mês";
        return d.Year == hoje.Year ? "Anteriormente neste ano" : "Há muito tempo";
    }

    /// <summary>O título da etapa ou pasta: "Despacho 01 · 18/08/2026" para <c>DESPACHO 01 - 2026-08-18</c>; "INICIAL" para
    /// <c>PROJETO\INICIAL</c> (as subpastas de PROJETO aparecem sem o prefixo); "Análises da IA" para <c>.pmvni-ai</c>; "Pasta do processo"
    /// para a raiz; o resto, como está no disco.</summary>
    public static string RotuloDaPasta(string? pastaRelativa)
    {
        var p = (pastaRelativa ?? "").Replace('/', '\\').Trim('\\');
        if (p.Length == 0) return "Pasta do processo";
        var partes = p.Split('\\');
        if (PastaDeDespacho().Match(partes[0]) is { Success: true } m)
        {
            var etapa = $"Despacho {int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture):D2} · {m.Groups[4].Value}/{m.Groups[3].Value}/{m.Groups[2].Value}";
            return partes.Length == 1 ? etapa : etapa + " · " + string.Join('\\', partes.Skip(1));
        }
        if (string.Equals(partes[0], ".pmvni-ai", StringComparison.OrdinalIgnoreCase)) return "Análises da IA";
        if (string.Equals(partes[0], "PROJETO", StringComparison.OrdinalIgnoreCase) && partes.Length > 1) return string.Join('\\', partes.Skip(1));
        return p;
    }

    /// <summary>A posição da etapa entre os grupos: despachos primeiro (o de maior número, isto é, o mais novo, antes), depois PROJETO,
    /// DOCUMENTOS, outras pastas, as análises da IA e a raiz do processo.</summary>
    static (int Classe, int Numero) ChaveDaPasta(string pastaRelativa)
    {
        var p = pastaRelativa.Replace('/', '\\').Trim('\\');
        if (p.Length == 0) return (5, 0);
        var primeira = p.Split('\\')[0];
        if (PastaDeDespacho().Match(primeira) is { Success: true } m) return (0, -int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        if (string.Equals(primeira, "PROJETO", StringComparison.OrdinalIgnoreCase)) return (1, 0);
        if (string.Equals(primeira, "DOCUMENTOS", StringComparison.OrdinalIgnoreCase)) return (2, 0);
        return string.Equals(primeira, ".pmvni-ai", StringComparison.OrdinalIgnoreCase) ? (4, 0) : (3, 0);
    }

    /// <summary>A data do arquivo, na hora local: a de alteração; nos arquivos de despacho (a leitura não traz a hora), a data do despacho.</summary>
    public static DateTime? Quando(LinhaDetalhe l) =>
        l.AlteradoUtc is { } utc ? utc.ToLocalTime() : l.Despacho?.Data.ToDateTime(TimeOnly.MinValue);

    /// <summary>O nome do arquivo, sem a pasta (a <see cref="LinhaDetalhe.Texto"/> de PROJETO e DOCUMENTOS traz a subpasta na frente).</summary>
    public static string Nome(LinhaDetalhe l) => l.Caminho is { } c ? Path.GetFileName(c) : l.Texto;

    static string Categoria(LinhaDetalhe l) => TiposArquivo.Categoria(TiposArquivo.Chave(l.Caminho));

    /// <summary>Os arquivos na ordem pedida; empates (e sempre, depois do critério) pelo nome em ordem natural. Sem data vai para o fim.</summary>
    public static IReadOnlyList<LinhaDetalhe> Ordenar(IEnumerable<LinhaDetalhe> arquivos, ExibicaoArquivos e)
    {
        IOrderedEnumerable<LinhaDetalhe> o = (e.Ordenar, e.Decrescente) switch
        {
            (OrdemArquivos.Data, false) => arquivos.OrderBy(l => Quando(l) ?? DateTime.MaxValue),
            (OrdemArquivos.Data, true) => arquivos.OrderByDescending(l => Quando(l) ?? DateTime.MinValue),
            (OrdemArquivos.Tipo, false) => arquivos.OrderBy(Categoria, Natural).ThenBy(l => TiposArquivo.Chave(l.Caminho), StringComparer.Ordinal),
            (OrdemArquivos.Tipo, true) => arquivos.OrderByDescending(Categoria, Natural).ThenByDescending(l => TiposArquivo.Chave(l.Caminho), StringComparer.Ordinal),
            (_, true) => arquivos.OrderByDescending(Nome, Natural),
            _ => arquivos.OrderBy(Nome, Natural),
        };
        return o.ThenBy(Nome, Natural).ThenBy(l => l.Caminho ?? "", StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Os arquivos (só as linhas escolhíveis) em grupos, cada um na ordem pedida. Sem agrupamento: um grupo só, de título "".
    /// A ordem dos grupos: etapas com os despachos mais novos primeiro e depois as pastas; tipos de A a Z; datas da mais nova à mais
    /// antiga. Classificar pelo mesmo critério do grupo inverte os grupos junto (Data crescente: os mais antigos primeiro).</summary>
    public static IReadOnlyList<GrupoArquivos> Organizar(IEnumerable<LinhaDetalhe> linhas, ExibicaoArquivos e, DateOnly hoje)
    {
        var arquivos = linhas.Where(l => l.Selecionavel && l.Caminho is not null).ToList();
        if (arquivos.Count == 0) return [];
        GrupoArquivos Grupo(string titulo, IEnumerable<LinhaDetalhe> itens) =>
            new(titulo, Ordenar(itens, e).Select(l => ItemArquivo.DoArquivo(l, Nome(l), Info(l, e.Agrupar))).ToList());

        switch (e.Agrupar)
        {
            case AgrupamentoArquivos.Etapa:
                return arquivos.GroupBy(l => RotuloDaPasta(l.Pasta), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(g => ChaveDaPasta(g.First().Pasta)).ThenBy(g => g.Key, Natural)
                    .Select(g => Grupo(g.Key, g)).ToList();
            case AgrupamentoArquivos.Tipo:
            {
                var grupos = arquivos.GroupBy(Categoria, StringComparer.Ordinal)
                    .OrderBy(g => g.Key == TiposArquivo.Outros ? 1 : 0).ThenBy(g => g.Key, Natural).Select(g => Grupo(g.Key, g)).ToList();
                if (e is { Ordenar: OrdemArquivos.Tipo, Decrescente: true }) grupos.Reverse();
                return grupos;
            }
            case AgrupamentoArquivos.Data:
            {
                var grupos = arquivos.GroupBy(l => GrupoDeData(Quando(l) is { } q ? DateOnly.FromDateTime(q) : null, hoje), StringComparer.Ordinal)
                    .OrderBy(g => IndiceDoGrupoDeData(g.Key)).Select(g => Grupo(g.Key, g)).ToList();
                if (e is { Ordenar: OrdemArquivos.Data, Decrescente: false })
                {
                    // Os mais antigos primeiro; "Sem data" continua no fim.
                    var semData = grupos.Where(g => g.Titulo == SemData).ToList();
                    grupos = grupos.Where(g => g.Titulo != SemData).Reverse().Concat(semData).ToList();
                }
                return grupos;
            }
            default:
                return [Grupo("", arquivos)];
        }
    }

    static int IndiceDoGrupoDeData(string titulo)
    {
        for (var i = 0; i < GruposDeData.Count; i++)
            if (GruposDeData[i] == titulo) return i;
        return GruposDeData.Count;
    }

    /// <summary>A segunda linha de um arquivo. PROJETO e DOCUMENTOS: a pasta (menos quando o grupo já é a pasta), o tamanho e a data.
    /// Relatórios e despachos: o que a leitura já diz (etapa, tipo e data).</summary>
    public static string Info(LinhaDetalhe l, AgrupamentoArquivos agrupar)
    {
        if (l.Tipo != TipoLinhaDetalhe.Arquivo) return l.Info;
        var partes = new List<string>(3);
        if (agrupar != AgrupamentoArquivos.Etapa) partes.Add(RotuloDaPasta(l.Pasta));
        if (l.Tamanho is { } t) partes.Add(ListasPastas.TextoDoTamanho(t));
        else if (l.Info.Length > 0) partes.Add(l.Info);
        if (l.AlteradoUtc is { } utc) partes.Add(Formatos.Data(utc.ToLocalTime()));
        return string.Join(" · ", partes);
    }

    /// <summary>Os grupos numa lista só (modo Lista): o título de cada grupo (se tiver) seguido dos arquivos dele; sem arquivo nenhum,
    /// a linha de aviso <paramref name="vazio"/>.</summary>
    public static List<ItemArquivo> Achatar(IReadOnlyList<GrupoArquivos> grupos, string vazio)
    {
        var saida = new List<ItemArquivo>();
        foreach (var g in grupos.Where(g => g.Itens.Count > 0))
        {
            if (g.Titulo.Length > 0) saida.Add(ItemArquivo.DoTitulo(g.Titulo));
            saida.AddRange(g.Itens);
        }
        if (saida.Count == 0) saida.Add(ItemArquivo.DoAviso(vazio));
        return saida;
    }

    /// <summary>Em qual das três colunas do modo Lista a linha fica: 0 = Relatórios (os .rpmvni, as análises da IA e os RELATORIO_* das
    /// pastas de despacho), 1 = Despachos (os demais arquivos das pastas de despacho), 2 = Projeto e documentos; -1 = não é arquivo.</summary>
    public static int Coluna(LinhaDetalhe l) => l.Tipo switch
    {
        TipoLinhaDetalhe.Relatorio or TipoLinhaDetalhe.Analise => 0,
        TipoLinhaDetalhe.ArquivoDeDespacho => l.Texto.StartsWith("RELATORIO", StringComparison.OrdinalIgnoreCase) ? 0 : 1,
        TipoLinhaDetalhe.Arquivo => 2,
        _ => -1,
    };

    /// <summary>O modo Lista: as linhas do detalhe nas três colunas, cada uma agrupada e ordenada pela mesma escolha.</summary>
    public static (List<ItemArquivo> Relatorios, List<ItemArquivo> Despachos, List<ItemArquivo> Projeto) EmColunas(
        IReadOnlyList<LinhaDetalhe> linhas, ExibicaoArquivos e, DateOnly hoje)
    {
        List<ItemArquivo> Da(int coluna, string vazio) => Achatar(Organizar(linhas.Where(l => Coluna(l) == coluna), e, hoje), vazio);
        return (Da(0, "Nenhum relatório."), Da(1, "Nenhum despacho."), Da(2, "Nenhum arquivo nas pastas PROJETO e DOCUMENTOS."));
    }
}
