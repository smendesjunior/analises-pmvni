namespace Pmvni.Revisao;

/// <summary>Onde o editor lê os modelos de despacho (Tarefa 22): <see cref="Registro"/> é o <c>TEMPLATES_OFICIAIS.json</c>, cujos caminhos
/// partem de <see cref="Raiz"/>; <see cref="PastaTextos"/> tem os textos de referência (<c>modelo-despacho-*.md</c>); <see cref="Origem"/> diz,
/// em português, de onde vieram (aparece em Ajustes).</summary>
public sealed record FonteModelos(string Registro, string Raiz, string PastaTextos, string Origem)
{
    /// <summary>A fonte existe neste computador: o registro e a pasta dos textos estão lá (o conteúdo é conferido por <see cref="RegistroModelos"/>).</summary>
    public bool Existe => File.Exists(Registro) && Directory.Exists(PastaTextos);
}

/// <summary>As fontes possíveis dos modelos de despacho, na ordem em que valem (Tarefa 22). O despacho deixou de ser plugin do kit: é emitido
/// pelo editor, e os modelos oficiais (DOCX, registro e textos) passaram a ser dados em <c>skills\analise\references\despacho</c> do plugin
/// analise, atualizados pelo kit. Enquanto o kit não for atualizado, valem as fontes antigas (o plugin despacho instalado e a pasta
/// <c>02 - MODELOS DE DOCUMENTOS</c> do kit); o plugin despacho em si nunca é chamado. Só lê.</summary>
public static class FontesModelos
{
    /// <summary>A pasta dos dados de despacho dentro do plugin analise (o registro, a subpasta <c>modelos</c> e os textos). Os caminhos do
    /// registro partem desta pasta (<c>modelos/MODELO DESPACHO CORREÇÃO.docx</c>).</summary>
    public const string PastaNoAnalise = "skills/analise/references/despacho";

    /// <summary>O plugin analise dentro da pasta do kit.</summary>
    public const string AnaliseNoKit = "01 - PLUGINS/plugins/analise";

    public const string NomeRegistro = "TEMPLATES_OFICIAIS.json";

    /// <summary>Nenhuma fonte foi achada: o tipo fica indisponível e nada é pedido à IA no lugar do modelo.</summary>
    public const string AtualizeOKit =
        "Os modelos de despacho não foram encontrados (nem no plugin analise instalado, nem na pasta do kit escolhida em Ajustes). "
        + "Atualize o kit PMVNI: rode o INSTALAR_KIT_PMVNI.cmd com a opção atualizar e abra o Novo despacho de novo.";

    static string Local(string relativo) => relativo.Replace('/', Path.DirectorySeparatorChar);

    /// <summary>Os dados de despacho de um plugin analise (a pasta da versão, com <c>skills</c> dentro).</summary>
    public static FonteModelos DoAnalise(string pastaPlugin, string origem)
    {
        ArgumentNullException.ThrowIfNull(pastaPlugin);
        var pasta = Path.Combine(pastaPlugin, Local(PastaNoAnalise));
        return new FonteModelos(Path.Combine(pasta, NomeRegistro), pasta, pasta, origem);
    }

    /// <summary>Compatibilidade com o kit antigo: os modelos empacotados no plugin despacho (<c>skills\despacho\assets\TEMPLATES_OFICIAIS.json</c>,
    /// caminhos a partir de <c>skills\despacho</c>) e os textos em <c>skills\despacho\references</c>. Só os dados são lidos.</summary>
    public static FonteModelos DoDespachoAntigo(string pastaPlugin, string origem)
    {
        ArgumentNullException.ThrowIfNull(pastaPlugin);
        var skill = Path.Combine(pastaPlugin, "skills", "despacho");
        return new FonteModelos(Path.Combine(skill, "assets", NomeRegistro), skill, Path.Combine(skill, "references"), origem);
    }

    /// <summary>A pasta do kit no formato antigo: <c>02 - MODELOS DE DOCUMENTOS\TEMPLATES_OFICIAIS.json</c> (caminhos a partir da raiz do kit)
    /// e os textos nas referências do plugin despacho do kit.</summary>
    public static FonteModelos DoKitAntigo(string raizKit)
    {
        ArgumentNullException.ThrowIfNull(raizKit);
        return new FonteModelos(Path.Combine(raizKit, Local(RegistroModelos.CaminhoRegistro)), raizKit, Path.Combine(raizKit, Local(TextosFixos.PastaReferencias)),
            "pasta do kit, formato antigo (" + raizKit + ")");
    }

    /// <summary>A pasta do kit: primeiro o plugin analise dela (formato novo), depois o formato antigo.</summary>
    public static IReadOnlyList<FonteModelos> DaPastaDoKit(string raizKit)
    {
        ArgumentNullException.ThrowIfNull(raizKit);
        return [DoAnalise(Path.Combine(raizKit, Local(AnaliseNoKit)), "plugin analise da pasta do kit (" + raizKit + ")"), DoKitAntigo(raizKit)];
    }

    /// <summary>Todas as fontes, na ordem: o plugin analise instalado, o plugin analise da pasta do kit, o plugin despacho antigo instalado
    /// e a pasta do kit no formato antigo. Pastas null (não instalado, sem kit) ficam de fora.</summary>
    public static IReadOnlyList<FonteModelos> Candidatas((string Versao, string Pasta)? analiseInstalado, (string Versao, string Pasta)? despachoInstalado,
        string? raizKit)
    {
        var lista = new List<FonteModelos>();
        if (analiseInstalado is { } a) lista.Add(DoAnalise(a.Pasta, "plugin analise instalado (versão " + a.Versao + ")"));
        var kit = string.IsNullOrWhiteSpace(raizKit) ? null : DaPastaDoKit(raizKit);
        if (kit is not null) lista.Add(kit[0]);
        if (despachoInstalado is { } d)
            lista.Add(DoDespachoAntigo(d.Pasta, "plugin despacho antigo instalado (versão " + d.Versao + "); atualize o kit PMVNI"));
        if (kit is not null) lista.Add(kit[1]);
        return lista;
    }

    /// <summary>A primeira fonte que existe (<see cref="FonteModelos.Existe"/>); null se nenhuma. Uma fonte que existe mas não confere não é
    /// trocada por outra mais antiga: o motivo aparece no tipo indisponível.</summary>
    public static FonteModelos? Escolher(IEnumerable<FonteModelos> candidatas)
    {
        ArgumentNullException.ThrowIfNull(candidatas);
        return candidatas.FirstOrDefault(f => f.Existe);
    }
}
