namespace Pmvni.App;

/// <summary>Botão comum, botão que alterna (ligado/desligado) ou menu (com <see cref="ComandoFaixa.Itens"/>).</summary>
public enum TipoComando { Botao, Alternar, Menu }

/// <summary>Um comando da faixa: id (de <see cref="ComandosApp"/>), rótulo, ícone Segoe Fluent, tipo, atalho canônico
/// (<c>Ctrl+Shift+S</c>, <c>Ctrl+Plus</c>…; o mesmo nome que a página repassa na mensagem <c>atalho</c>) e itens de um menu.</summary>
public sealed record ComandoFaixa(string Id, string Rotulo, string Glifo, TipoComando Tipo = TipoComando.Botao, string? Atalho = null,
    IReadOnlyList<ComandoFaixa>? Itens = null);

/// <summary>Uma aba da faixa (Arquivo, Exibir, Ajuda…) com os seus comandos, na ordem em que aparecem.</summary>
public sealed record AbaFaixa(string Nome, IReadOnlyList<ComandoFaixa> Comandos);

/// <summary>A faixa inteira de um módulo: abas e a que abre por padrão (índice em <see cref="Abas"/>).</summary>
public sealed record DefinicaoFaixa(Modulo Modulo, IReadOnlyList<AbaFaixa> Abas, int AbaPadrao);

/// <summary>Faixa de opções por dados (desenho §9.1): uma definição por módulo; a faixa troca inteira com o módulo. Arquivo e Ajuda
/// existem em todas; a aba padrão é a segunda (Ajustes: a primeira, porque a sua segunda é Ajuda). Só aparece o que funciona:
/// cada tarefa seguinte acrescenta as suas abas e comandos e põe o módulo em <see cref="Disponiveis"/>.</summary>
public static class Faixas
{
    /// <summary>Módulos que já funcionam, na ordem do trilho (Ajustes, se presente, embaixo).</summary>
    public static IReadOnlyList<Modulo> Disponiveis { get; } = [Modulo.Relatorios];

    static readonly AbaFaixa Ajuda = new("Ajuda",
    [
        new(ComandosApp.AjudaAtalhos, "Atalhos de teclado", ""),
        new(ComandosApp.AjudaSobre, "Sobre", ""),
    ]);

    static readonly DefinicaoFaixa Relatorios = new(Modulo.Relatorios,
    [
        new("Arquivo",
        [
            new(ComandosApp.ArquivoImportar, "Importar dados.json", "", Atalho: "Ctrl+I"),
            new(ComandosApp.ArquivoAbrir, "Abrir", "", Atalho: "Ctrl+O"),
            new(ComandosApp.ArquivoSalvar, "Salvar", "", Atalho: "Ctrl+S"),
            new(ComandosApp.ArquivoSalvarComo, "Salvar como", "", Atalho: "Ctrl+Shift+S"),
            new(ComandosApp.ArquivoExportarPdf, "Exportar PDF", "", Atalho: "Ctrl+E"),
            new(ComandosApp.ArquivoFechar, "Fechar", ""),
        ]),
        new("Exibir",
        [
            new(ComandosApp.ExibirMenos, "Diminuir", "", Atalho: "Ctrl+Minus"),
            new(ComandosApp.ExibirMais, "Aumentar", "", Atalho: "Ctrl+Plus"),
            new(ComandosApp.ExibirLargura, "Ajustar largura", "", Atalho: "Ctrl+0"),
            new(ComandosApp.ExibirPagina, "Página inteira", ""),
            new(ComandosApp.ExibirPainel, "Painel lateral", "", TipoComando.Alternar),
        ]),
        Ajuda,
    ], AbaPadrao: 1);

    static readonly DefinicaoFaixa Ajustes = new(Modulo.Ajustes,
    [
        new("Arquivo",
        [
            new(ComandosApp.ArquivoImportar, "Importar dados.json", "", Atalho: "Ctrl+I"),
            new(ComandosApp.ArquivoAbrir, "Abrir", "", Atalho: "Ctrl+O"),
        ]),
        Ajuda,
    ], AbaPadrao: 0);

    /// <summary>A faixa do módulo. Módulo cuja tarefa ainda não chegou: <see cref="ArgumentOutOfRangeException"/>.</summary>
    public static DefinicaoFaixa Para(Modulo m) => m switch
    {
        Modulo.Relatorios => Relatorios,
        Modulo.Ajustes => Ajustes,
        _ => throw new ArgumentOutOfRangeException(nameof(m), m, "Módulo ainda sem faixa de opções."),
    };

    /// <summary>Todos os comandos de uma faixa (os itens dos menus inclusive), na ordem das abas.</summary>
    public static IEnumerable<ComandoFaixa> Comandos(DefinicaoFaixa f) =>
        f.Abas.SelectMany(a => a.Comandos).SelectMany(c => c.Itens is null ? [c] : c.Itens.Prepend(c));

    /// <summary>O índice da aba lembrada (pelo nome), ou a padrão se ela não existe mais.</summary>
    public static int IndiceDaAba(DefinicaoFaixa f, string? nome)
    {
        for (var i = 0; i < f.Abas.Count; i++)
            if (string.Equals(f.Abas[i].Nome, nome, StringComparison.Ordinal)) return i;
        return f.AbaPadrao;
    }
}
