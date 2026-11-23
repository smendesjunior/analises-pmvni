namespace Pmvni.App;

/// <summary>Uma categoria do menu dos Ajustes: a etiqueta (o nome da página), o título, a frase de ajuda do topo da página, o glifo, se fica
/// no rodapé do menu (como "Sobre e suporte" no WinStatusBar) e, numa subpágina, a etiqueta da categoria-mãe (o item fica dentro dela no menu).</summary>
public sealed record CategoriaAjustes(string Tag, string Titulo, string Ajuda, string Glifo, bool Rodape = false, string? Pai = null);

/// <summary>As categorias dos Ajustes, na ordem do menu (reorganização de 2026-11-23: você → onde ficam as coisas → como cada tela trabalha
/// → IA → kit → sistema; cada opção num lugar só). Sem tipos WinUI: compilado também nos testes.</summary>
public static class CategoriasAjustes
{
    public const string Geral = "geral";
    public const string Profissional = "profissional";
    public const string Pastas = "pastas";
    public const string Relatorios = "relatorios";
    public const string Modulos = "modulos";
    public const string Ia = "ia";
    public const string Modelos = "modelos";
    public const string Kit = "kit";
    public const string Sobre = "sobre";

    public static IReadOnlyList<CategoriaAjustes> Menu { get; } =
    [
        new(Geral, "Geral", "Como o editor abre e a aparência da janela.", Glifos.Inicio),
        new(Profissional, "Profissional e assinatura", "Quem assina os despachos e os relatórios e o certificado digital usado no PDF.", Glifos.Editar),
        new(Pastas, "Pastas e fontes de dados", "Onde ficam os processos, a base QGIS, a planilha do Acompanhamento e os logins dos sites da Prefeitura.", Glifos.Pasta),
        new(Relatorios, "Relatórios e revisão", "Zoom, marcas de revisão, painéis, salvamento automático, recuperação e arquivos recentes.", Glifos.Relatorio),
        new(Modulos, "Módulos", "Como Processos, Despachos, Acompanhamento e Legislação trabalham: listas, PDF dos despachos e leis anexadas.", Glifos.Lista),
        new(Ia, "Inteligência artificial", "As contas das IAs, o que o Novo relatório pergunta antes de seguir e a IA da revisão.", Glifos.Ia),
        new(Modelos, "Modelos da análise", "Defina o modelo e o raciocínio de cada etapa do Novo relatório.", Glifos.Ia, Pai: Ia),
        new(Kit, "Kit PMVNI", "A pasta do kit e os plugins e ferramentas instalados neste computador.", Glifos.Ferramenta),
        new(Sobre, "Sistema", "Atualizações, suporte, restaurar os ajustes, diagnóstico e informações sobre o editor.", Glifos.Sobre, Rodape: true),
    ];

    /// <summary>A categoria da etiqueta; uma etiqueta desconhecida leva à primeira (Geral).</summary>
    public static CategoriaAjustes De(string? tag) => Menu.FirstOrDefault(c => c.Tag == tag) ?? Menu[0];
}
