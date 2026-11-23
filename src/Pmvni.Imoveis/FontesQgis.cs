namespace Pmvni.Imoveis;

/// <summary>As camadas que o módulo usa, pelo nome lógico.</summary>
public enum Camada { Lotes, Edificacoes, Zoneamento, Logradouros, HidroApp, PerimetroUrbano, Auc, Limite, Loteamentos, Quadras, NumerosLote, CursosAgerh }

/// <summary>Um arquivo encontrado na pasta QGIS (somente metadados).</summary>
public sealed record ArquivoFonte(string Caminho, string Relativo, long Tamanho, DateTime ModificadoUtc);

/// <summary>O resultado de "Verificar acesso e arquivos": o que cada camada usa (ou que falta), a base cadastral e os avisos.</summary>
public sealed record Inventario(string Pasta, IReadOnlyDictionary<Camada, ArquivoFonte?> Camadas, ArquivoFonte? BaseCadastral,
    IReadOnlyList<ArquivoFonte> Arquivos, IReadOnlyList<string> Avisos)
{
    public bool Utilizavel => BaseCadastral is not null;
    public IEnumerable<Camada> Faltando => Camadas.Where(p => p.Value is null).Select(p => p.Key);
}

/// <summary>Localiza as fontes na pasta QGIS escolhida em Ajustes: primeiro o caminho conhecido da base municipal (o mesmo do projeto
/// "Mapa PDM.qgz"), depois o mesmo nome de arquivo em qualquer subpasta. Só lê metadados; links e junções não são seguidos.</summary>
public static class FontesQgis
{
    public static string Rotulo(Camada c) => c switch
    {
        Camada.Lotes => "Lotes",
        Camada.Edificacoes => "Edificações (projeção)",
        Camada.Zoneamento => "Zoneamento",
        Camada.Logradouros => "Logradouros",
        Camada.HidroApp => "Hidrografia e faixa de APP",
        Camada.PerimetroUrbano => "Perímetro urbano",
        Camada.Auc => "Áreas urbanas consolidadas (AUC)",
        Camada.Limite => "Limite municipal",
        Camada.Loteamentos => "Loteamentos",
        Camada.Quadras => "Quadras",
        Camada.NumerosLote => "Números de lote",
        Camada.CursosAgerh => "Nomes dos cursos d'água (AGERH)",
        _ => c.ToString(),
    };

    /// <summary>Caminhos conhecidos (relativos à pasta QGIS) e, depois da barra, o padrão de nome para a busca nas subpastas.</summary>
    static readonly Dictionary<Camada, (string Relativo, string Padrao)> Conhecidos = new()
    {
        [Camada.Lotes] = (@"shapes\Lotes.shp", "Lotes.shp"),
        [Camada.Edificacoes] = (@"shapes\Edificacao.shp", "Edificacao.shp"),
        [Camada.Zoneamento] = (@"2025_10\Zoneamento_2025_10.shp", "Zoneamento*.shp"),
        [Camada.Logradouros] = (@"shapes\Logradouro.shp", "Logradouro.shp"),
        [Camada.HidroApp] = (@"shapes\Hidro_app.shp", "Hidro_app.shp"),
        [Camada.PerimetroUrbano] = (@"shapes\Per_urb_2022.shp", "Per_urb*.shp"),
        [Camada.Auc] = (@"shapes\AUC.shp", "AUC.shp"),
        [Camada.Limite] = (@"shapes\Limite_VNI.shp", "Limite_VNI.shp"),
        [Camada.Loteamentos] = (@"SHAPE_LOTEAMENTOS\NOME DO LOTEAMENTO.shp", "NOME DO LOTEAMENTO.shp"),
        [Camada.Quadras] = (@"SHAPE_LOTEAMENTOS\Quadras.shp", "Quadras.shp"),
        [Camada.NumerosLote] = (@"SHAPE_LOTEAMENTOS\NUMERO DO LOTE.shp", "NUMERO DO LOTE.shp"),
        [Camada.CursosAgerh] = (@"HIDROGRAFIA COM NOMES (AGERH-GEOBASES)\Cursos_dagua_AGERH_VNI.shp", "Cursos_dagua*.shp"),
    };

    const string BaseConhecida = @"BASE_CADASTRAL_AUXILIAR\Base_Cadastral_Auxiliar.sqlite";

    static readonly HashSet<string> Extensoes = new(StringComparer.OrdinalIgnoreCase)
        { ".shp", ".shx", ".dbf", ".prj", ".cpg", ".sqlite", ".gpkg", ".tif", ".tiff", ".qgz", ".qgs" };

    public static Inventario Verificar(string pasta, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(pasta)) throw new ArgumentException("Escolha a pasta dos arquivos QGIS em Ajustes › Pastas e fontes de dados.");
        var raiz = Path.GetFullPath(pasta);
        if (!Directory.Exists(raiz)) throw new DirectoryNotFoundException($"A pasta {raiz} não está acessível (rede desconectada, unidade removida ou pasta apagada).");
        var arquivos = new List<ArquivoFonte>();
        var avisos = new List<string>();
        var pendentes = new Stack<(string Pasta, int Nivel)>();
        pendentes.Push((raiz, 0));
        while (pendentes.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (atual, nivel) = pendentes.Pop();
            try
            {
                foreach (var e in new DirectoryInfo(atual).EnumerateFileSystemInfos())
                {
                    if ((e.Attributes & FileAttributes.ReparsePoint) != 0 && e is DirectoryInfo) { avisos.Add($"Atalho de pasta não percorrido: {Relativo(raiz, e.FullName)}"); continue; }
                    if (e is DirectoryInfo && nivel < 6) pendentes.Push((e.FullName, nivel + 1));
                    else if (e is FileInfo f && Extensoes.Contains(f.Extension))
                        arquivos.Add(new ArquivoFonte(f.FullName, Relativo(raiz, f.FullName), f.Length, f.LastWriteTimeUtc));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { avisos.Add($"Leitura parcial de {Relativo(raiz, atual)}: {e.Message}"); }
        }

        var porCaminho = arquivos.ToDictionary(a => a.Caminho, StringComparer.OrdinalIgnoreCase);
        var camadas = new Dictionary<Camada, ArquivoFonte?>();
        foreach (var (camada, (rel, padrao)) in Conhecidos)
        {
            var achado = porCaminho.GetValueOrDefault(Path.Combine(raiz, rel))
                ?? arquivos.Where(a => Path.GetExtension(a.Caminho).Equals(".shp", StringComparison.OrdinalIgnoreCase) && Casa(Path.GetFileName(a.Caminho), padrao))
                           .OrderByDescending(a => a.Caminho, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            camadas[camada] = achado;
            if (achado is null) { avisos.Add($"Camada não encontrada: {Rotulo(camada)} ({Path.GetFileName(rel)})."); continue; }
            foreach (var ext in new[] { ".shx", ".dbf", ".prj" })
                if (!porCaminho.ContainsKey(Path.ChangeExtension(achado.Caminho, ext)))
                    avisos.Add($"{Rotulo(camada)}: falta o arquivo {ext} ao lado de {achado.Relativo}.");
        }
        var baseCad = porCaminho.GetValueOrDefault(Path.Combine(raiz, BaseConhecida))
            ?? arquivos.FirstOrDefault(a => Path.GetExtension(a.Caminho).Equals(".sqlite", StringComparison.OrdinalIgnoreCase));
        if (baseCad is null) avisos.Insert(0, "Base cadastral (.sqlite) não encontrada: a pesquisa precisa dela. Confira se escolheu a pasta \"2 - QGIS\".");
        if (arquivos.Count == 0) avisos.Insert(0, "Nenhum arquivo QGIS reconhecido nesta pasta.");
        return new Inventario(raiz, camadas, baseCad, arquivos, avisos);
    }

    static bool Casa(string nome, string padrao)
    {
        if (!padrao.Contains('*')) return string.Equals(nome, padrao, StringComparison.OrdinalIgnoreCase);
        var partes = padrao.Split('*');
        return nome.StartsWith(partes[0], StringComparison.OrdinalIgnoreCase) && nome.EndsWith(partes[^1], StringComparison.OrdinalIgnoreCase);
    }

    static string Relativo(string raiz, string caminho) => Path.GetRelativePath(raiz, caminho);

    /// <summary>Texto curto do inventário para os Ajustes.</summary>
    public static string Resumo(Inventario i)
    {
        var ok = i.Camadas.Count(p => p.Value is not null);
        var linhas = new List<string>
        {
            i.BaseCadastral is { } b ? $"Base cadastral: {b.Relativo} ({b.ModificadoUtc.ToLocalTime():dd/MM/yyyy HH:mm})." : "Base cadastral: não encontrada.",
            $"Camadas: {ok} de {i.Camadas.Count} encontradas; {i.Arquivos.Count} arquivos reconhecidos.",
        };
        if (i.Faltando.Any()) linhas.Add("Faltam: " + string.Join(", ", i.Faltando.Select(Rotulo)) + ".");
        return string.Join("\n", linhas);
    }
}
