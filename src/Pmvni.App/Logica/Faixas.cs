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
    public static IReadOnlyList<Modulo> Disponiveis { get; } = [Modulo.Relatorios, Modulo.Despachos, Modulo.Processos, Modulo.Legislacao, Modulo.Ajustes];

    /// <summary>Menu de recentes: <see cref="ComandoFaixa.Itens"/> vazio de propósito; a faixa pede os itens a quem a monta
    /// (<see cref="FaixaDeOpcoes.ItensDinamicos"/>) cada vez que o menu abre, porque eles vêm dos ajustes e dos arquivos que ainda existem.</summary>
    static readonly ComandoFaixa RecentesMenu = new(ComandosApp.ArquivoRecentes, "Recentes", "", TipoComando.Menu, []);

    /// <summary>Novo despacho ▾ (desenho §10.2): os papéis de despacho do registro do kit, na ordem Correção, Aprovação. O mesmo comando nas
    /// três faixas que o têm (Despachos, Processos e Relatórios); um tipo cujo modelo falta fica desabilitado com o motivo na dica.</summary>
    static readonly ComandoFaixa NovoDespacho = new(ComandosApp.DespachosNovo, "Novo despacho", "", TipoComando.Menu,
    [
        new(ComandosApp.DespachosNovoCorrecao, "Correção", ""),
        new(ComandosApp.DespachosNovoAprovacao, "Aprovação", ""),
    ]);

    /// <summary>Novo relatório (desenho §12.2): primeiro de Relatórios › Arquivo e › Página Inicial; em Processos, logo depois do Novo despacho.</summary>
    static readonly ComandoFaixa NovoRelatorio = new(ComandosApp.RelatoriosNovo, "Novo relatório", "");

    static readonly AbaFaixa Ajuda = new("Ajuda",
    [
        new(ComandosApp.AjudaAtalhos, "Atalhos de teclado", ""),
        new(ComandosApp.AjudaSobre, "Sobre", ""),
    ]);

    /// <summary>A aba Arquivo dos módulos que não têm um relatório nas folhas (Despachos, Processos, Ajustes): importar, abrir e recentes
    /// levam ao módulo Relatórios.</summary>
    static readonly AbaFaixa ArquivoDeModulo = new("Arquivo",
    [
        new(ComandosApp.ArquivoImportar, "Importar dados.json", ""),
        new(ComandosApp.ArquivoAbrir, "Abrir", ""),
        RecentesMenu,
    ]);

    static readonly DefinicaoFaixa Relatorios = new(Modulo.Relatorios,
    [
        new("Arquivo",
        [
            NovoRelatorio,
            new(ComandosApp.ArquivoImportar, "Importar dados.json", ""),
            new(ComandosApp.ArquivoAbrir, "Abrir", ""),
            RecentesMenu,
            new(ComandosApp.ArquivoSalvar, "Salvar", ""),
            new(ComandosApp.ArquivoSalvarComo, "Salvar como", ""),
            new(ComandosApp.ArquivoExportarPdf, "Exportar PDF", ""),
            new(ComandosApp.ArquivoFechar, "Fechar", ""),
        ]),
        new("Página Inicial",
        [
            NovoRelatorio,
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
            NovoDespacho,
        ]),
        new("Revisão",
        [
            new(ComandosApp.RevisaoMarcas, "Marcas de revisão", "", TipoComando.Alternar),
            new(ComandosApp.RevisaoSomenteAlterados, "Somente alterados", "", TipoComando.Alternar),
            new(ComandosApp.RevisaoConferencia, "Conferência", ""),
            new(ComandosApp.RevisaoComparar, "Comparar com o original", ""),
        ]),
        new("Exibir",
        [
            new(ComandosApp.ExibirMenos, "Diminuir", ""),
            new(ComandosApp.ExibirMais, "Aumentar", ""),
            new(ComandosApp.ExibirLargura, "Ajustar largura", ""),
            new(ComandosApp.ExibirPagina, "Página inteira", ""),
            new(ComandosApp.ExibirPainel, "Painel lateral", "", TipoComando.Alternar),
            new(ComandosApp.ExibirPropriedades, "Propriedades", "", TipoComando.Alternar),
            new(ComandosApp.ExibirTema, "Tema", "", TipoComando.Menu,
            [
                new(ComandosApp.ExibirTemaSistema, "Sistema", ""),
                new(ComandosApp.ExibirTemaClaro, "Claro", ""),
                new(ComandosApp.ExibirTemaEscuro, "Escuro", ""),
            ]),
        ]),
        Ajuda,
    ], AbaPadrao: 1);

    static readonly DefinicaoFaixa Despachos = new(Modulo.Despachos,
    [
        ArquivoDeModulo,
        new("Página Inicial",
        [
            NovoDespacho,
            new(ComandosApp.DespachosAbrir, "Abrir", ""),
            new(ComandosApp.DespachosAbrirPdf, "Abrir PDF", ""),
            new(ComandosApp.DespachosGerarPdf, "Gerar PDF", ""),
            new(ComandosApp.DespachosMostrar, "Mostrar no Explorador", ""),
            new(ComandosApp.DespachosIrAoProcesso, "Ir ao processo", ""),
            new(ComandosApp.DespachosAtualizar, "Atualizar lista", ""),
            new(ComandosApp.DespachosFiltro, "Tipo", "", TipoComando.Menu,
            [
                new(ComandosApp.DespachosFiltroTodos, "Todos", ""),
                new(ComandosApp.DespachosFiltroCorrecao, "Correção", ""),
                new(ComandosApp.DespachosFiltroAprovacao, "Aprovação", ""),
            ]),
            new(ComandosApp.DespachosDoProcesso, "Só do processo escolhido", "", TipoComando.Alternar),
        ]),
        new("Exibir",
        [
            new(ComandosApp.DespachosAgrupar, "Agrupar", "", TipoComando.Menu,
            [
                new(ComandosApp.DespachosAgruparProcesso, "Por processo", ""),
                new(ComandosApp.DespachosAgruparData, "Por data", ""),
            ]),
            new(ComandosApp.DespachosOrdenar, "Ordenar", "", TipoComando.Menu,
            [
                new(ComandosApp.DespachosOrdenarRecentes, "Mais recentes primeiro", ""),
                new(ComandosApp.DespachosOrdenarAntigos, "Mais antigos primeiro", ""),
            ]),
        ]),
        Ajuda,
    ], AbaPadrao: 1);

    static readonly DefinicaoFaixa Processos = new(Modulo.Processos,
    [
        ArquivoDeModulo,
        new("Página Inicial",
        [
            new(ComandosApp.ProcessosAbrirRelatorio, "Abrir relatório", ""),
            new(ComandosApp.ProcessosImportarAnalise, "Importar análise da IA", ""),
            NovoDespacho,
            NovoRelatorio,
            new(ComandosApp.ProcessosAbrirArquivo, "Abrir arquivo", ""),
            new(ComandosApp.ProcessosAbrirPasta, "Abrir pasta no Explorador", ""),
            new(ComandosApp.ProcessosAtualizar, "Atualizar", ""),
            // Menu dinâmico: os anos vêm da pasta de processos (a janela os informa cada vez que o menu abre), como Recentes.
            new(ComandosApp.ProcessosAno, "Ano", "", TipoComando.Menu, []),
        ]),
        new("Exibir",
        [
            new(ComandosApp.ProcessosOrdenar, "Ordenar", "", TipoComando.Menu,
            [
                new(ComandosApp.ProcessosOrdenarNumero, "Número", ""),
                new(ComandosApp.ProcessosOrdenarData, "Data", ""),
                new(ComandosApp.ProcessosOrdenarRequerente, "Requerente", ""),
            ]),
            new(ComandosApp.ProcessosMostrarPastas, "Mostrar PROJETO e DOCUMENTOS", "", TipoComando.Alternar),
        ]),
        Ajuda,
    ], AbaPadrao: 1);

    /// <summary>Legislação (desenho §11.5): o leitor dos PDFs anexados. Comandos que dependem de uma lei aberta, de seleção ou de
    /// marcadores ficam desabilitados sem eles (<see cref="ComandosLegislacao.Habilitados"/>).</summary>
    static readonly DefinicaoFaixa Legislacao = new(Modulo.Legislacao,
    [
        new("Arquivo",
        [
            new(ComandosApp.LegislacaoAnexar, "Anexar PDFs…", "\uE723"),
            new(ComandosApp.LegislacaoGerenciar, "Gerenciar anexos", "\uE713"),
            new(ComandosApp.ArquivoImportar, "Importar dados.json", "\uE8B5"),
            new(ComandosApp.ArquivoAbrir, "Abrir", "\uE8E5"),
            RecentesMenu,
        ]),
        new("Página Inicial",
        [
            new(ComandosApp.LegislacaoCopiar, "Copiar", "\uE8C8"),
            new(ComandosApp.LegislacaoCopiarCitacao, "Copiar citação", "\uE9B2"),
            new(ComandosApp.LegislacaoPaginaAnterior, "◂ Página", "\uE70E"),
            new(ComandosApp.LegislacaoPaginaProxima, "Página ▸", "\uE70D"),
            new(ComandosApp.LegislacaoEscopo, "Pesquisar em", "\uE721", TipoComando.Menu,
            [
                new(ComandosApp.LegislacaoEscopoLei, "Nesta lei", ""),
                new(ComandosApp.LegislacaoEscopoTodas, "Em todas as leis", ""),
            ]),
            new(ComandosApp.LegislacaoOcorrenciaAnterior, "◂ Ocorrência", "\uE74A"),
            new(ComandosApp.LegislacaoOcorrenciaProxima, "Ocorrência ▸", "\uE74B"),
        ]),
        new("Exibir",
        [
            new(ComandosApp.LegislacaoZoomMenos, "Diminuir", "\uE71F"),
            new(ComandosApp.LegislacaoZoomMais, "Aumentar", "\uE8A3"),
            new(ComandosApp.LegislacaoAjustarLargura, "Ajustar largura", "\uE9A6"),
            new(ComandosApp.LegislacaoPaginaInteira, "Página inteira", "\uE740"),
            new(ComandosApp.LegislacaoMarcadores, "Marcadores", "\uE8A4", TipoComando.Alternar),
            new(ComandosApp.LegislacaoLista, "Lista de leis", "\uE8FD", TipoComando.Alternar),
        ]),
        Ajuda,
    ], AbaPadrao: 1);

    static readonly DefinicaoFaixa Ajustes = new(Modulo.Ajustes,
    [
        new("Arquivo",
        [
            new(ComandosApp.ArquivoImportar, "Importar dados.json", ""),
            new(ComandosApp.ArquivoAbrir, "Abrir", ""),
            RecentesMenu,
        ]),
        Ajuda,
    ], AbaPadrao: 0);

    /// <summary>A faixa do módulo. Módulo cuja tarefa ainda não chegou: <see cref="ArgumentOutOfRangeException"/>.</summary>
    public static DefinicaoFaixa Para(Modulo m) => m switch
    {
        Modulo.Relatorios => Relatorios,
        Modulo.Despachos => Despachos,
        Modulo.Processos => Processos,
        Modulo.Legislacao => Legislacao,
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
