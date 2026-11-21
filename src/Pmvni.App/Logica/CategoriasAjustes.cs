namespace Pmvni.App;

/// <summary>Uma categoria do menu dos Ajustes: a etiqueta (o nome da página), o título, a frase de ajuda do topo da página, o glifo e se fica
/// no rodapé do menu (como "Sobre e suporte" no WinStatusBar).</summary>
public sealed record CategoriaAjustes(string Tag, string Titulo, string Ajuda, string Glifo, bool Rodape = false);

/// <summary>As 9 categorias dos Ajustes, na ordem do menu (auditoria da interface, §3.2–3.3; como <c>SettingsNavigation.Menu</c> do
/// WinStatusBar). Sem tipos WinUI: compilado também nos testes.</summary>
public static class CategoriasAjustes
{
    public const string Geral = "geral";
    public const string Aparencia = "aparencia";
    public const string Relatorios = "relatorios";
    public const string Pastas = "pastas";
    public const string FontesDados = "fontes_dados";
    public const string Despachos = "despachos";
    public const string Legislacao = "legislacao";
    public const string Ia = "ia";
    public const string Sobre = "sobre";

    public static IReadOnlyList<CategoriaAjustes> Menu { get; } =
    [
        new(Geral, "Geral", "Como o editor abre e o que ele lembra entre um uso e outro.", Glifos.Inicio),
        new(Aparencia, "Aparência", "Tema da janela, rótulos da faixa e onde aparecem os avisos de layout.", Glifos.Tema),
        new(Relatorios, "Relatórios e revisão", "Zoom, marcas de revisão, painéis, salvamento automático e recuperação.", Glifos.Relatorio),
        new(Pastas, "Pastas e kit", "Onde ficam os processos e o kit PMVNI, os plugins e ferramentas do kit e como as listas de Processos e Despachos abrem.", Glifos.Pasta),
        new(FontesDados, "Fontes de dados", "A pasta QGIS do módulo Imóveis e a planilha do Acompanhamento de processos.", Glifos.Mapa),
        new(Despachos, "Despachos e PDF", "O LibreOffice que gera o PDF dos despachos e o que fazer depois de gravar.", Glifos.Despachos),
        new(Legislacao, "Legislação", "As leis anexadas e como a pesquisa no leitor começa.", Glifos.Livro),
        new(Ia, "Inteligência artificial", "Os agentes locais que fazem a análise e o que o Novo relatório pergunta antes de seguir.", Glifos.Ia),
        new(Sobre, "Sobre e suporte", "Atualizações, ajuda para relatar um problema, diagnóstico e informações sobre o editor.", Glifos.Sobre, Rodape: true),
    ];

    /// <summary>A categoria da etiqueta; uma etiqueta desconhecida leva à primeira (Geral).</summary>
    public static CategoriaAjustes De(string? tag) => Menu.FirstOrDefault(c => c.Tag == tag) ?? Menu[0];
}
