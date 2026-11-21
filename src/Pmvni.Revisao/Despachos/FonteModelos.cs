namespace Pmvni.Revisao;

/// <summary>Onde o editor lê os modelos de despacho (Tarefa 22): <see cref="Registro"/> é o <c>TEMPLATES_OFICIAIS.json</c>, cujos caminhos
/// partem de <see cref="Raiz"/>; <see cref="PastaTextos"/> tem os textos de referência (<c>modelo-despacho-*.md</c>); <see cref="Origem"/> diz,
/// em português, de onde vieram (aparece em Ajustes).</summary>
public sealed record FonteModelos(string Registro, string Raiz, string PastaTextos, string Origem)
{
    /// <summary>A fonte existe neste computador: o registro e a pasta dos textos estão lá (o conteúdo é conferido por <see cref="RegistroModelos"/>).</summary>
    public bool Existe => File.Exists(Registro) && Directory.Exists(PastaTextos);
}

/// <summary>As fontes possíveis dos modelos de despacho, na ordem em que valem (Tarefa 22, correção 1). O despacho deixou de ser plugin do kit:
/// é emitido pelo editor, e os modelos oficiais (DOCX, registro e textos) são dados. Vale primeiro a pasta do kit escolhida em Ajustes (o
/// registro <c>02 - MODELOS DE DOCUMENTOS</c>, mantido pelo usuário, e depois o <c>references\despacho</c> do plugin analise do kit); só sem
/// ela, as cópias instaladas no Codex: o plugin analise e, por último, o antigo plugin despacho (só os dados dele; ele nunca é chamado). Como
/// no kit, a cópia empacotada só vale sem o kit. Só lê.</summary>
public static class FontesModelos
{
    /// <summary>A pasta dos dados de despacho dentro do plugin analise (o registro, a subpasta <c>modelos</c> e os textos). Os caminhos do
    /// registro partem desta pasta (<c>modelos/MODELO DESPACHO CORREÇÃO.docx</c>).</summary>
    public const string PastaNoAnalise = "skills/analise/references/despacho";

    /// <summary>O plugin analise dentro da pasta do kit.</summary>
    public const string AnaliseNoKit = "01 - PLUGINS/plugins/analise";

    public const string NomeRegistro = "TEMPLATES_OFICIAIS.json";

    /// <summary>O que fazer quando nenhuma fonte foi achada (fim da mensagem de <see cref="NaoEncontrados"/>).</summary>
    public const string AtualizeOKit =
        "Atualize o kit PMVNI (rode o INSTALAR_KIT_PMVNI.cmd com a opção atualizar) ou escolha a pasta do kit em Ajustes, e abra o Novo despacho de novo.";

    /// <summary>Nenhuma fonte foi achada: o tipo fica indisponível e nada é pedido à IA no lugar do modelo. Diz onde a pasta do kit foi
    /// procurada (<paramref name="raizKit"/>; null = não informada).</summary>
    public static string NaoEncontrados(string? raizKit)
    {
        var kit = string.IsNullOrWhiteSpace(raizKit) ? "a pasta do kit não foi informada"
            : Directory.Exists(raizKit) ? "a pasta do kit (" + raizKit + ") não tem os modelos de despacho"
            : "a pasta do kit não foi encontrada em " + raizKit;
        return "Os modelos de despacho não foram encontrados: " + kit + ", e o plugin analise instalado não traz os modelos. " + AtualizeOKit;
    }

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

    /// <summary>O registro do kit, <c>02 - MODELOS DE DOCUMENTOS\TEMPLATES_OFICIAIS.json</c> (caminhos a partir da raiz do kit), com os textos
    /// do <c>references\despacho</c> do analise do kit ou, no kit antigo, das referências do plugin despacho do kit.</summary>
    public static FonteModelos DoRegistroDoKit(string raizKit)
    {
        ArgumentNullException.ThrowIfNull(raizKit);
        var novos = Path.Combine(raizKit, Local(AnaliseNoKit), Local(PastaNoAnalise));
        var textos = Directory.Exists(novos) ? novos : Path.Combine(raizKit, Local(TextosFixos.PastaReferencias));
        return new FonteModelos(Path.Combine(raizKit, Local(RegistroModelos.CaminhoRegistro)), raizKit, textos,
            "pasta do kit, 02 - MODELOS DE DOCUMENTOS (" + raizKit + ")");
    }

    /// <summary>A pasta do kit: primeiro o registro <c>02 - MODELOS DE DOCUMENTOS</c>, depois o <c>references\despacho</c> do analise dela.</summary>
    public static IReadOnlyList<FonteModelos> DaPastaDoKit(string raizKit)
    {
        ArgumentNullException.ThrowIfNull(raizKit);
        return [DoRegistroDoKit(raizKit), DoAnalise(Path.Combine(raizKit, Local(AnaliseNoKit)), "plugin analise da pasta do kit (" + raizKit + ")")];
    }

    /// <summary>Todas as fontes, na ordem: a pasta do kit (<see cref="DaPastaDoKit"/>), o plugin analise instalado e, por último, o antigo
    /// plugin despacho instalado. Pastas null (sem kit, não instalado) ficam de fora.</summary>
    public static IReadOnlyList<FonteModelos> Candidatas((string Versao, string Pasta)? analiseInstalado, (string Versao, string Pasta)? despachoInstalado,
        string? raizKit)
    {
        var lista = new List<FonteModelos>();
        if (!string.IsNullOrWhiteSpace(raizKit)) lista.AddRange(DaPastaDoKit(raizKit));
        if (analiseInstalado is { } a) lista.Add(DoAnalise(a.Pasta, "plugin analise instalado (versão " + a.Versao + ")"));
        if (despachoInstalado is { } d)
            lista.Add(DoDespachoAntigo(d.Pasta, "plugin despacho antigo instalado (versão " + d.Versao + "); atualize o kit PMVNI"));
        return lista;
    }

    /// <summary>A primeira fonte que existe (<see cref="FonteModelos.Existe"/>); null se nenhuma.</summary>
    public static FonteModelos? Escolher(IEnumerable<FonteModelos> candidatas)
    {
        ArgumentNullException.ThrowIfNull(candidatas);
        return candidatas.FirstOrDefault(f => f.Existe);
    }
}
