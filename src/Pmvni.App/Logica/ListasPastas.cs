using System.Globalization;
using System.Text;
using Pmvni.Revisao;

namespace Pmvni.App;

public enum OrdemProcessos { Numero, Data, Requerente }

public enum AgrupamentoDespachos { Processo, Data }

public enum FiltroTipoDespacho { Todos, Correcao, Aprovacao }

/// <summary>Uma linha da lista de processos: o processo e a data em que a pasta dele mudou (para ordenar por data).</summary>
public sealed class LinhaProcesso(Processo processo, DateTime alteradoUtc)
{
    public Processo Processo { get; } = processo;
    public DateTime AlteradoUtc { get; } = alteradoUtc;

    /// <summary>O requerente (ou o nome da pasta, quando ele não segue o padrão), como veio.</summary>
    public string Titulo => Processo.Requerente ?? Processo.NomePasta;

    /// <summary>As iniciais do requerente (o selo redondo da linha).</summary>
    public string Iniciais
    {
        get
        {
            var partes = Titulo.Split([' ', '-', '_'], StringSplitOptions.RemoveEmptyEntries).Where(x => char.IsLetter(x[0])).ToList();
            return partes.Count switch
            {
                0 => "?",
                1 => partes[0][..1].ToUpperInvariant(),
                _ => (partes[0][..1] + partes[^1][..1]).ToUpperInvariant(),
            };
        }
    }

    /// <summary>"Processo 0012/2099" ou, fora do padrão de nome, "Nome da pasta fora do padrão".</summary>
    public string Detalhe => ListasPastas.NumeroDoProcesso(Processo) is { } n ? "Processo " + n : "Nome da pasta fora do padrão";

    /// <summary>O que o Narrador lê na linha (auditoria A3): o requerente e o processo.</summary>
    public string NomeAcessivel => Titulo + ", " + Detalhe;

    public override string ToString() => NomeAcessivel;
}

public enum TipoLinhaDetalhe { Titulo, Vazio, Relatorio, Analise, ArquivoDeDespacho, Arquivo }

/// <summary>Uma linha do detalhe de um processo (à direita). Só as de <see cref="Selecionavel"/> podem ser escolhidas.</summary>
public sealed class LinhaDetalhe(TipoLinhaDetalhe tipo, string texto, string? info = null, string? caminho = null, Despacho? despacho = null)
{
    public TipoLinhaDetalhe Tipo { get; } = tipo;
    public string Texto { get; } = texto;
    public string Info { get; } = info ?? "";
    public string? Caminho { get; } = caminho;
    public Despacho? Despacho { get; } = despacho;
    public bool Selecionavel => Tipo is not (TipoLinhaDetalhe.Titulo or TipoLinhaDetalhe.Vazio);
    public bool EhTitulo => Tipo == TipoLinhaDetalhe.Titulo;
    public bool NaoEhTitulo => Tipo != TipoLinhaDetalhe.Titulo;
    public bool TemInfo => Info.Length > 0;

    /// <summary>Quando o arquivo foi alterado (UTC), se a leitura trouxe; os arquivos de despacho usam a data do despacho.</summary>
    public DateTime? AlteradoUtc { get; init; }

    /// <summary>O tamanho em bytes, se a leitura trouxe (PROJETO e DOCUMENTOS).</summary>
    public long? Tamanho { get; init; }

    /// <summary>A pasta do arquivo, relativa à pasta do processo ("" = a própria pasta do processo): a etapa em que ele está.</summary>
    public string Pasta { get; init; } = "";

    /// <summary>O caminho que dá o ícone do tipo de arquivo na frente da linha; null nos títulos e nas linhas sem arquivo.</summary>
    public string? CaminhoDoIcone => Selecionavel ? Caminho : null;

    /// <summary>O que o Narrador lê na linha: "Grupo Relatórios" nos títulos; o texto e a informação nas outras.</summary>
    public string NomeAcessivel => EhTitulo ? "Grupo " + Texto : TemInfo ? Texto + ", " + Info : Texto;

    public override string ToString() => NomeAcessivel;
}

/// <summary>Um despacho na lista: o texto das duas linhas e os arquivos que se abrem.</summary>
public sealed class LinhaDespacho(Despacho despacho)
{
    public Despacho Despacho { get; } = despacho;

    public string Titulo => $"Despacho {Despacho.Numero:D2} · {ListasPastas.TextoDoTipo(Despacho.Tipo)}";

    public string Detalhe
    {
        get
        {
            var processo = Despacho.Processo is { } p ? ListasPastas.NumeroDoProcesso(p) is { } n ? $"Processo {n} · " : p.NomePasta + " · " : "";
            return Formatos.Data(Despacho.Data) + " · " + processo
                + string.Join(", ", Despacho.Arquivos.Select(a => Path.GetFileName(a)));
        }
    }

    /// <summary>O DOCX (ou DOC) do despacho; sem ele, o PDF; null se a pasta não tem nenhum dos dois.</summary>
    public string? ArquivoParaAbrir => ArquivoComExtensao(".docx", ".doc") ?? ArquivoPdf;

    public string? ArquivoPdf => ArquivoComExtensao(".pdf");

    /// <summary>O que o Narrador lê na linha: título e detalhe (data, processo e arquivos).</summary>
    public string NomeAcessivel => Titulo + ", " + Detalhe;

    public override string ToString() => NomeAcessivel;

    /// <summary>O que o Explorador mostra: o arquivo que se abriria; sem arquivo, a própria pasta do despacho.</summary>
    public string CaminhoNoExplorador => ArquivoParaAbrir ?? Despacho.Pasta;

    string? ArquivoComExtensao(params string[] extensoes) =>
        Despacho.Arquivos.FirstOrDefault(a => extensoes.Contains(Path.GetExtension(a), StringComparer.OrdinalIgnoreCase));
}

/// <summary>Uma linha da lista de despachos como a tela a mostra: o título de um grupo (<see cref="Grupo"/>) ou um despacho
/// (<see cref="Linha"/>). Só o despacho pode ser escolhido.</summary>
public sealed class ItemDespacho
{
    ItemDespacho(string? grupo, LinhaDespacho? linha) { Grupo = grupo; Linha = linha; }

    public static ItemDespacho DoGrupo(string titulo) => new(titulo, null);
    public static ItemDespacho DoDespacho(LinhaDespacho linha) => new(null, linha);

    public string? Grupo { get; }
    public LinhaDespacho? Linha { get; }
    public bool EhGrupo => Grupo is not null;
    public bool EhDespacho => Linha is not null;
    public string Titulo => Grupo ?? Linha!.Titulo;
    public string Detalhe => Linha?.Detalhe ?? "";

    /// <summary>O que o Narrador lê na linha: "Grupo …" nos títulos de grupo; o despacho nas outras.</summary>
    public string NomeAcessivel => Grupo is not null ? "Grupo " + Grupo : Linha!.NomeAcessivel;

    public override string ToString() => NomeAcessivel;
}

/// <summary>Um bloco da lista de despachos: um título (o processo ou o mês) e os despachos dele.</summary>
public sealed record GrupoDespachos(string Titulo, IReadOnlyList<LinhaDespacho> Linhas);

/// <summary>Filtros, ordens e agrupamentos das listas de Processos e Despachos (desenho §9, §9.1), sem tipos de interface: tudo em
/// memória, sobre o que as leituras de pasta já trouxeram.</summary>
public static class ListasPastas
{
    static readonly string[] Meses =
        ["janeiro", "fevereiro", "março", "abril", "maio", "junho", "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];

    /// <summary>"0012/2099"; null se o nome da pasta não segue o padrão.</summary>
    public static string? NumeroDoProcesso(Processo p) =>
        p.Protocolo;

    public static string TextoDoTipo(TipoDespacho t) => t switch
    {
        TipoDespacho.Correcao => "Correção",
        TipoDespacho.Aprovacao => "Aprovação",
        _ => "Tipo não identificado",
    };

    /// <summary>Os anos dos processos, do mais novo ao mais antigo (sem repetir; nome fora do padrão não tem ano).</summary>
    public static IReadOnlyList<int> Anos(IEnumerable<Processo> processos) =>
        processos.Where(p => p.Ano is not null).Select(p => p.Ano!.Value).Distinct().OrderByDescending(a => a).ToList();

    /// <summary>Processos pelo ano (null = todos), pelo termo da pesquisa (<see cref="Revisao.Processos.Filtrar"/>) e na ordem pedida.
    /// Número: a ordem em que a leitura trouxe (ano e número, do mais novo ao mais antigo); Data: a pasta alterada mais recentemente
    /// primeiro; Requerente: A–Z sem acento (nome fora do padrão usa o nome da pasta). Empates mantêm a ordem de entrada.</summary>
    public static IReadOnlyList<LinhaProcesso> FiltrarProcessos(IEnumerable<LinhaProcesso> todos, string termo, int? ano, OrdemProcessos ordem)
    {
        var lista = todos.Where(l => (ano is null || l.Processo.Ano == ano) && Revisao.Processos.Filtrar(l.Processo, termo));
        return ordem switch
        {
            OrdemProcessos.Data => lista.OrderByDescending(l => l.AlteradoUtc).ToList(),
            OrdemProcessos.Requerente => lista.OrderBy(l => Normalizado(l.Titulo), StringComparer.Ordinal).ToList(),
            _ => lista.ToList(),
        };
    }

    /// <summary>Novo relatório › Processo (1.0.0): a lista filtrada e o que mostrar. O escolhido continua na lista mesmo fora do filtro
    /// (marcado como "escolhido"), para a seleção válida não se perder; com um único processo achado e nenhum escolhido, ele é sugerido
    /// para escolha. Texto: "" sem filtro; a contagem; ou o aviso de nada encontrado.</summary>
    public static (IReadOnlyList<(Processo Processo, bool ForaDoFiltro)> Itens, Processo? Unico, string Texto) FiltrarNovoRelatorio(
        IReadOnlyList<Processo> todos, string termo, Processo? escolhido)
    {
        var achados = FiltrarProcessos(todos.Select(p => new LinhaProcesso(p, DateTime.MinValue)), termo ?? "", null, OrdemProcessos.Numero)
            .Select(l => l.Processo).ToList();
        var itens = achados.Select(p => (p, false)).ToList();
        if (escolhido is not null && !achados.Any(p => MesmaPasta(p, escolhido))) itens.Insert(0, (escolhido, true));
        var filtrando = !string.IsNullOrWhiteSpace(termo);
        var texto = !filtrando ? ""
            : achados.Count == 0 ? $"Nenhum processo encontrado para “{termo!.Trim()}”. Confira o número, o ano ou o nome, ou use Criar processo…."
            : achados.Count == 1 ? "1 processo encontrado."
            : $"{achados.Count} processos encontrados. Abra a lista ao lado para escolher.";
        // Só sugere quando nada está escolhido: uma seleção válida nunca é trocada pelo filtro.
        var unico = filtrando && achados.Count == 1 && escolhido is null ? achados[0] : null;
        return (itens, unico, texto);
    }

    static bool MesmaPasta(Processo a, Processo b) => string.Equals(a.Pasta, b.Pasta, StringComparison.OrdinalIgnoreCase);

    static string Normalizado(string texto) =>
        string.Concat(texto.Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
            .ToLowerInvariant();

    /// <summary>Despachos pelo tipo, pelo nome do arquivo (<see cref="Revisao.Despachos.Filtrar"/>), agrupados e ordenados. Por data:
    /// um grupo por mês ("março de 2099"). Por processo: um grupo por processo, na ordem de <paramref name="ordemProcessos"/>.
    /// Dentro e entre os grupos, os mais recentes primeiro (<paramref name="recentesPrimeiro"/>) ou os mais antigos.</summary>
    public static IReadOnlyList<GrupoDespachos> OrganizarDespachos(IEnumerable<Despacho> todos, FiltroTipoDespacho tipo, string termo,
        AgrupamentoDespachos agrupar, bool recentesPrimeiro, IReadOnlyList<Processo> ordemProcessos)
    {
        var lista = todos
            .Where(d => tipo switch
            {
                FiltroTipoDespacho.Correcao => d.Tipo == TipoDespacho.Correcao,
                FiltroTipoDespacho.Aprovacao => d.Tipo == TipoDespacho.Aprovacao,
                _ => true,
            })
            .Where(d => Revisao.Despachos.Filtrar(d, termo))
            .ToList();
        // Mais recentes primeiro: data e número, do maior ao menor; o contrário para os mais antigos. Estável: empates ficam como vieram.
        IEnumerable<Despacho> Ordenar(IEnumerable<Despacho> origem) => recentesPrimeiro
            ? origem.OrderByDescending(d => d.Data).ThenByDescending(d => d.Numero)
            : origem.OrderBy(d => d.Data).ThenBy(d => d.Numero);

        if (agrupar == AgrupamentoDespachos.Data)
            return Ordenar(lista)
                .GroupBy(d => (d.Data.Year, d.Data.Month))
                .Select(g => new GrupoDespachos($"{Meses[g.Key.Month - 1]} de {g.Key.Year}", g.Select(d => new LinhaDespacho(d)).ToList()))
                .ToList();

        var posicao = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < ordemProcessos.Count; i++) posicao.TryAdd(ordemProcessos[i].Pasta, i);
        int Posicao(Despacho d) => d.Processo is { } p && posicao.TryGetValue(p.Pasta, out var i) ? i : int.MaxValue;
        var grupos = lista.GroupBy(d => d.Processo?.Pasta ?? "").Select(g => (Chave: Posicao(g.First()), Itens: g.ToList()));
        grupos = recentesPrimeiro ? grupos.OrderBy(g => g.Chave) : grupos.OrderByDescending(g => g.Chave);
        return grupos
            .Select(g => new GrupoDespachos(TituloDoProcesso(g.Itens[0].Processo), Ordenar(g.Itens).Select(d => new LinhaDespacho(d)).ToList()))
            .ToList();
    }

    /// <summary>Os grupos em uma lista só: o título de cada grupo seguido dos despachos dele (grupo sem despacho não aparece).</summary>
    public static IReadOnlyList<ItemDespacho> Achatar(IEnumerable<GrupoDespachos> grupos) =>
        grupos.Where(g => g.Linhas.Count > 0)
            .SelectMany(g => g.Linhas.Select(ItemDespacho.DoDespacho).Prepend(ItemDespacho.DoGrupo(g.Titulo))).ToList();

    static string TituloDoProcesso(Processo? p) =>
        p is null ? "Sem processo" : NumeroDoProcesso(p) is { } n ? $"{n} · {p.Requerente}" : p.NomePasta;

    /// <summary>As linhas do detalhe de um processo: Relatórios, Despachos (cada arquivo) e, se pedido, PROJETO e DOCUMENTOS.
    /// Um relatório em revisão (.rpmvni) abre no editor; uma análise da IA (<c>dados*.json</c>) vira um .rpmvni novo na pasta do processo.</summary>
    public static IReadOnlyList<LinhaDetalhe> Detalhe(DetalheProcesso d, bool mostrarPastas)
    {
        string Pasta(string caminho) => PastaRelativa(d.Processo.Pasta, caminho);
        var linhas = new List<LinhaDetalhe>
        {
            new(TipoLinhaDetalhe.Titulo, $"Relatórios ({d.Relatorios.Count})"),
        };
        if (d.Relatorios.Count == 0) linhas.Add(new(TipoLinhaDetalhe.Vazio, "Nenhum relatório encontrado."));
        foreach (var r in d.Relatorios)
        {
            var quando = r.AlteradoUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            linhas.Add(r.Tipo == TipoRelatorio.Pacote
                ? new(TipoLinhaDetalhe.Relatorio, Path.GetFileName(r.Caminho), "Relatório em revisão · " + quando, r.Caminho)
                    { AlteradoUtc = r.AlteradoUtc, Pasta = Pasta(r.Caminho) }
                : new(TipoLinhaDetalhe.Analise, Path.GetFileName(r.Caminho),
                    "Análise da IA · " + Path.GetFileName(Path.GetDirectoryName(r.Caminho)) + " · " + quando, r.Caminho)
                    { AlteradoUtc = r.AlteradoUtc, Pasta = Pasta(r.Caminho) });
        }

        linhas.Add(new(TipoLinhaDetalhe.Titulo, $"Despachos ({d.Despachos.Count})"));
        if (d.Despachos.Count == 0) linhas.Add(new(TipoLinhaDetalhe.Vazio, "Nenhum despacho encontrado."));
        foreach (var despacho in d.Despachos)
        {
            var info = $"Despacho {despacho.Numero:D2} · {TextoDoTipo(despacho.Tipo)} · {despacho.Data.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
            if (despacho.Arquivos.Count == 0) linhas.Add(new(TipoLinhaDetalhe.Vazio, info + " (sem arquivos)"));
            foreach (var a in despacho.Arquivos)
                linhas.Add(new(TipoLinhaDetalhe.ArquivoDeDespacho, Path.GetFileName(a), info, a, despacho) { Pasta = Pasta(a) });
        }

        if (mostrarPastas)
        {
            AcrescentarPasta(linhas, d.Processo.Pasta, "PROJETO", d.Projeto);
            AcrescentarPasta(linhas, d.Processo.Pasta, "DOCUMENTOS", d.Documentos);
        }
        return linhas;
    }

    /// <summary>A pasta de <paramref name="caminho"/> relativa à do processo ("" = a própria pasta do processo; fora dela, "").</summary>
    public static string PastaRelativa(string pastaDoProcesso, string caminho)
    {
        var relativo = Path.GetRelativePath(pastaDoProcesso, caminho);
        if (Path.IsPathRooted(relativo) || relativo.StartsWith("..", StringComparison.Ordinal)) return "";
        return Path.GetDirectoryName(relativo) ?? "";
    }

    static void AcrescentarPasta(List<LinhaDetalhe> linhas, string pastaDoProcesso, string nome, IReadOnlyList<ArquivoEncontrado> arquivos)
    {
        linhas.Add(new(TipoLinhaDetalhe.Titulo, $"{nome} ({arquivos.Count})"));
        if (arquivos.Count == 0) linhas.Add(new(TipoLinhaDetalhe.Vazio, "Nenhum arquivo encontrado."));
        foreach (var a in arquivos)
            linhas.Add(new(TipoLinhaDetalhe.Arquivo, Path.GetRelativePath(Path.Combine(pastaDoProcesso, nome), a.Caminho), TextoDoTamanho(a.Tamanho), a.Caminho)
                { AlteradoUtc = a.AlteradoUtc, Tamanho = a.Tamanho, Pasta = PastaRelativa(pastaDoProcesso, a.Caminho) });
    }

    /// <summary>"512 B", "3,4 KB", "12,0 MB" (vírgula decimal, como o resto da interface).</summary>
    public static string TextoDoTamanho(long bytes)
    {
        var ci = CultureInfo.GetCultureInfo("pt-BR");
        return bytes switch
        {
            < 1024 => bytes.ToString(CultureInfo.InvariantCulture) + " B",
            < 1024 * 1024 => (bytes / 1024.0).ToString("0.0", ci) + " KB",
            _ => (bytes / 1024.0 / 1024.0).ToString("0.0", ci) + " MB",
        };
    }
}
