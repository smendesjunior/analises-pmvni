using Pmvni.Composicao.Kit;

namespace Pmvni.App;

/// <summary>Botão comum, botão que alterna (ligado/desligado) ou menu (com <see cref="ComandoFaixa.Itens"/>).</summary>
public enum TipoComando { Botao, Alternar, Menu }

/// <summary>Um comando da faixa: id (de <see cref="ComandosApp"/>), rótulo, ícone Segoe Fluent, tipo e itens de um menu. O atalho
/// de teclado não fica aqui: vem de <see cref="Atalhos.Mapa"/> (um mapa só para a faixa, a janela e a página).</summary>
public sealed record ComandoFaixa(string Id, string Rotulo, string Glifo, TipoComando Tipo = TipoComando.Botao,
    IReadOnlyList<ComandoFaixa>? Itens = null);

/// <summary>Uma aba da faixa (Arquivo, Exibir, Ajuda…) com os seus comandos, na ordem em que aparecem.</summary>
public sealed record AbaFaixa(string Nome, IReadOnlyList<ComandoFaixa> Comandos);

/// <summary>A faixa inteira de um módulo: abas e a que abre por padrão (índice em <see cref="Abas"/>).</summary>
public sealed record DefinicaoFaixa(Modulo Modulo, IReadOnlyList<AbaFaixa> Abas, int AbaPadrao);

/// <summary>Faixa de opções por dados (desenho §9.1): uma definição por módulo; a faixa troca inteira com o módulo. Arquivo e Ajuda
/// existem em todas; a aba padrão é a segunda (Relatórios: Página Inicial; Ajustes: a primeira, porque a sua segunda é Ajuda). Só aparece o que funciona:
/// cada tarefa seguinte acrescenta as suas abas e comandos e põe o módulo em <see cref="Disponiveis"/>.</summary>
public static class Faixas
{
    /// <summary>As situações de item do kit (<see cref="Situacoes.SitItem"/>) na ordem do menu Situação.</summary>
    public static IReadOnlyList<string> OrdemSituacoes { get; } = ["ATENDE", "NAO_ATENDE", "NAO_COMPROVADO", "DIVERGENCIA_INTERNA"];

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
            new(ComandosApp.ArquivoImportar, "Importar dados.json", ""),
            new(ComandosApp.ArquivoAbrir, "Abrir", ""),
            new(ComandosApp.ArquivoSalvar, "Salvar", ""),
            new(ComandosApp.ArquivoSalvarComo, "Salvar como", ""),
            new(ComandosApp.ArquivoExportarPdf, "Exportar PDF", ""),
            new(ComandosApp.ArquivoFechar, "Fechar", ""),
        ]),
        new("Página Inicial",
        [
            new(ComandosApp.InicioSituacao, "Situação", "", TipoComando.Menu, OrdemSituacoes
                .Select(c => new ComandoFaixa(ComandosApp.ComandoDaSituacao(c), Situacoes.Sit[c].Texto, "")).ToList()),
            new(ComandosApp.InicioExcluirRestaurar, "Excluir item", ""),
            new(ComandosApp.InicioEditarTexto, "Editar texto", ""),
            new(ComandosApp.InicioOriginal, "Original", "", TipoComando.Alternar),
            new(ComandosApp.InicioRestaurarOriginal, "Restaurar original", ""),
            new(ComandosApp.InicioItemAnterior, "◂ Item", ""),
            new(ComandosApp.InicioItemProximo, "Item ▸", ""),
            new(ComandosApp.InicioProximoPendente, "Próximo pendente", ""),
            new(ComandosApp.ArquivoExportarPdf, "Exportar PDF", ""),
        ]),
        new("Revisão",
        [
            new(ComandosApp.RevisaoMarcas, "Marcas de revisão", "", TipoComando.Alternar),
            new(ComandosApp.RevisaoSomenteAlterados, "Somente alterados", "", TipoComando.Alternar),
            new(ComandosApp.RevisaoConferencia, "Conferência", ""),
        ]),
        new("Exibir",
        [
            new(ComandosApp.ExibirMenos, "Diminuir", ""),
            new(ComandosApp.ExibirMais, "Aumentar", ""),
            new(ComandosApp.ExibirLargura, "Ajustar largura", ""),
            new(ComandosApp.ExibirPagina, "Página inteira", ""),
            new(ComandosApp.ExibirPainel, "Painel lateral", "", TipoComando.Alternar),
            new(ComandosApp.ExibirPropriedades, "Propriedades", "", TipoComando.Alternar),
        ]),
        Ajuda,
    ], AbaPadrao: 1);

    static readonly DefinicaoFaixa Ajustes = new(Modulo.Ajustes,
    [
        new("Arquivo",
        [
            new(ComandosApp.ArquivoImportar, "Importar dados.json", ""),
            new(ComandosApp.ArquivoAbrir, "Abrir", ""),
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
