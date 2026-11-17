using Pmvni.Composicao.Kit;

namespace Pmvni.App;

/// <summary>Botão comum, botão que alterna (ligado/desligado) ou menu (com <see cref="ComandoFaixa.Itens"/>).</summary>
public enum TipoComando { Botao, Alternar, Menu }

/// <summary>Um comando da faixa: id (de <see cref="ComandosApp"/>), rótulo, ícone Segoe Fluent (<see cref="Glifos"/>), tipo, itens de um
/// menu e, quando o rótulo é encurtado para caber na faixa, o texto completo (<see cref="Descricao"/>: dica e nome acessível). O atalho de
/// teclado não fica aqui: vem de <see cref="Atalhos.Mapa"/> (um mapa só para a faixa, a janela e a página).</summary>
public sealed record ComandoFaixa(string Id, string Rotulo, string Glifo, TipoComando Tipo = TipoComando.Botao,
    IReadOnlyList<ComandoFaixa>? Itens = null, string? Descricao = null)
{
    /// <summary>O texto completo do comando: a descrição, se houver, senão o rótulo.</summary>
    public string TextoCompleto => Descricao ?? Rotulo;
}

/// <summary>Uma aba da faixa (Arquivo, Exibir, Ajuda…) com os seus comandos, na ordem em que aparecem.</summary>
public sealed record AbaFaixa(string Nome, IReadOnlyList<ComandoFaixa> Comandos);

/// <summary>A faixa inteira de um módulo: abas, a que abre por padrão (índice em <see cref="Abas"/>) e o botão primário tonal, à
/// esquerda dos comandos e visível em todas as abas do módulo (como "Novo email"); null = sem primário (Ajustes).</summary>
public sealed record DefinicaoFaixa(Modulo Modulo, IReadOnlyList<AbaFaixa> Abas, int AbaPadrao, ComandoFaixa? Primario = null, ComandoFaixa? Secundario = null);

/// <summary>Faixa de opções por dados (desenho §9.1): uma definição por módulo; a faixa troca inteira com o módulo. Arquivo e Ajuda
/// existem em todas; a aba padrão é a segunda (Relatórios: Página Inicial; Ajustes: a primeira, porque a sua segunda é Ajuda). Só aparece o que funciona:
/// cada tarefa seguinte acrescenta as suas abas e comandos e põe o módulo em <see cref="Disponiveis"/>.</summary>
public static class Faixas
{
    /// <summary>As situações de item do kit (<see cref="Situacoes.SitItem"/>) na ordem do menu Situação.</summary>
    public static IReadOnlyList<string> OrdemSituacoes { get; } = ["ATENDE", "NAO_ATENDE", "NAO_COMPROVADO", "DIVERGENCIA_INTERNA"];

    /// <summary>Módulos que já funcionam, na ordem do trilho (Ajustes, se presente, embaixo).</summary>
    public static IReadOnlyList<Modulo> Disponiveis { get; } = [Modulo.Relatorios, Modulo.Despachos, Modulo.Processos, Modulo.Legislacao, Modulo.Acompanhamento, Modulo.Imoveis, Modulo.Essencial, Modulo.Gpi, Modulo.Alfresco, Modulo.Ajustes];

    /// <summary>Menu de recentes: <see cref="ComandoFaixa.Itens"/> vazio de propósito; a faixa pede os itens a quem a monta
    /// (<see cref="FaixaDeOpcoes.ItensDinamicos"/>) cada vez que o menu abre, porque eles vêm dos ajustes e dos arquivos que ainda existem.</summary>
    static readonly ComandoFaixa RecentesMenu = new(ComandosApp.ArquivoRecentes, "Recentes", Glifos.Recentes, TipoComando.Menu, []);

    /// <summary>Novo despacho ▾ (desenho §10.2): os papéis de despacho do registro do kit, na ordem Correção, Aprovação. É o primário de
    /// Despachos e fica na Página Inicial de Processos e de Relatórios; um tipo cujo modelo falta fica desabilitado com o motivo na dica.</summary>
    static readonly ComandoFaixa NovoDespacho = new(ComandosApp.DespachosNovo, "Novo despacho", Glifos.Editar, TipoComando.Menu,
    [
        new(ComandosApp.DespachosNovoCorrecao, "Correção", ""),
        new(ComandosApp.DespachosNovoCorrecaoAutomatica, "Correção automática (itens pendentes do relatório)", ""),
        new(ComandosApp.DespachosNovoAprovacao, "Aprovação", ""),
    ]);

    /// <summary>Novo relatório (desenho §12.2): primário de Relatórios e de Processos; em Relatórios também o primeiro de Arquivo.</summary>
    static readonly ComandoFaixa NovoRelatorio = new(ComandosApp.RelatoriosNovo, "Novo relatório", Glifos.Adicionar);

    /// <summary>Imprimir (2026-11-17): em todo módulo que mostra um documento, ao lado da exportação ou da abertura do arquivo. Abre a
    /// impressão do Edge (impressoras do Windows e "Salvar como PDF"); o que se imprime em cada módulo está em <see cref="ComandosImpressao"/>.</summary>
    static readonly ComandoFaixa Imprimir = new(ComandosApp.ArquivoImprimir, "Imprimir", Glifos.Imprimir, Descricao: "Imprimir ou salvar como PDF");

    /// <summary>Anexar PDFs… (desenho §11.1): o primário da Legislação.</summary>
    static readonly ComandoFaixa AnexarLeis = new(ComandosApp.LegislacaoAnexar, "Anexar PDFs…", Glifos.Anexar);

    static readonly AbaFaixa Ajuda = new("Ajuda",
    [
        new(ComandosApp.AjudaAtalhos, "Atalhos de teclado", Glifos.Atalhos),
        new(ComandosApp.AjudaSobre, "Sobre", Glifos.Sobre),
    ]);

    /// <summary>A aba Arquivo dos módulos que não têm um relatório nas folhas (Despachos, Processos, Ajustes): importar, abrir e recentes
    /// levam ao módulo Relatórios.</summary>
    static readonly AbaFaixa ArquivoDeModulo = new("Arquivo",
    [
        new(ComandosApp.ArquivoImportar, "Importar análise da IA…", Glifos.Importar),
        new(ComandosApp.ArquivoAbrir, "Abrir", Glifos.Abrir),
        RecentesMenu,
    ]);

    /// <summary>Figuras (Etapa 4): o editor de marcações da figura selecionada e o recorte de uma região das pranchas do projeto.</summary>
    static readonly AbaFaixa FigurasDoRelatorio = new("Figuras",
    [
        new(ComandosApp.FigurasInserirRegiao, "Inserir região da prancha", "\uE7A8", Descricao: "Inserir no relatório uma região de uma prancha do projeto (PDF)"),
        new(ComandosApp.FigurasEditar, "Editar figura", Glifos.Editar, Descricao: "Editar as marcações da figura selecionada (setas, regiões, textos)"),
        new(ComandosApp.FigurasExcluir, "Excluir figura", Glifos.Excluir, Descricao: "Tirar do relatório a figura selecionada"),
    ]);

    static readonly DefinicaoFaixa Relatorios = new(Modulo.Relatorios,
    [
        new("Arquivo",
        [
            new(ComandosApp.ArquivoImportar, "Importar análise da IA…", Glifos.Importar),
            RecentesMenu,
            new(ComandosApp.ArquivoSalvar, "Salvar", Glifos.Salvar),
            new(ComandosApp.ArquivoSalvarComo, "Salvar como", Glifos.SalvarComo),
            new(ComandosApp.ArquivoExportarPdf, "Exportar PDF", Glifos.Exportar),
            Imprimir,
            new(ComandosApp.ArquivoFechar, "Fechar", Glifos.Fechar),
        ]),
        new("Página Inicial",
        [
            new(ComandosApp.InicioSituacao, "Situação", Glifos.Situacao, TipoComando.Menu, OrdemSituacoes
                .Select(c => new ComandoFaixa(ComandosApp.ComandoDaSituacao(c), Situacoes.Sit[c].Texto, Glifos.SituacaoItem)).ToList()),
            new(ComandosApp.InicioNovoItem, "Novo item", Glifos.NovoItem),
            new(ComandosApp.InicioExcluirRestaurar, "Excluir item", Glifos.Excluir),
            new(ComandosApp.InicioEditarTexto, "Editar texto", Glifos.Editar),
            new(ComandosApp.InicioOriginal, "Original", Glifos.Original, TipoComando.Alternar),
            new(ComandosApp.InicioRestaurarOriginal, "Restaurar", Glifos.Desfazer, Descricao: "Restaurar o original"),
            new(ComandosApp.InicioItemAnterior, "Item anterior", Glifos.Anterior),
            new(ComandosApp.InicioItemProximo, "Próximo item", Glifos.Proximo),
            new(ComandosApp.InicioProximoPendente, "Próximo pendente", Glifos.Pendente),
            // Menu dinâmico: as figuras do relatório (a janela as informa cada vez que o menu abre), como Recentes.
            new(ComandosApp.FigurasAbrirEditor, "Figuras", Glifos.Figuras, TipoComando.Menu, [], Descricao: "Abrir uma figura no editor de figuras"),
            new(ComandosApp.ArquivoExportarPdf, "Exportar PDF", Glifos.Exportar),
            Imprimir,
            NovoDespacho,
        ]),
        new("Revisão",
        [
            new(ComandosApp.RevisaoMarcas, "Marcas de revisão", Glifos.MarcasRevisao, TipoComando.Alternar),
            new(ComandosApp.RevisaoSomenteAlterados, "Somente alterados", Glifos.Alterados, TipoComando.Alternar),
            new(ComandosApp.RevisaoConferencia, "Conferência", Glifos.Conferir),
            new(ComandosApp.RevisaoComparar, "Comparar", Glifos.Original, Descricao: "Comparar com o original"),
        ]),
        FigurasDoRelatorio,
        new("Exibir",
        [
            new(ComandosApp.ExibirMenos, "Diminuir", Glifos.Diminuir),
            new(ComandosApp.ExibirMais, "Aumentar", Glifos.Aumentar),
            new(ComandosApp.ExibirLargura, "Ajustar largura", Glifos.Ajustar),
            new(ComandosApp.ExibirPagina, "Página inteira", Glifos.PaginaInteira),
            new(ComandosApp.ExibirPainel, "Painel lateral", Glifos.Painel, TipoComando.Alternar),
            new(ComandosApp.ExibirPropriedades, "Propriedades", Glifos.Propriedades, TipoComando.Alternar),
            new(ComandosApp.ExibirTema, "Tema", Glifos.Tema, TipoComando.Menu,
            [
                new(ComandosApp.ExibirTemaSistema, TextosTema.Sistema, ""),
                new(ComandosApp.ExibirTemaClaro, TextosTema.Claro, ""),
                new(ComandosApp.ExibirTemaEscuro, TextosTema.Escuro, ""),
            ]),
        ]),
        Ajuda,
    ], AbaPadrao: 1, Primario: NovoRelatorio, Secundario: new(ComandosApp.ArquivoAbrir, "Abrir", Glifos.Abrir));

    static readonly DefinicaoFaixa Despachos = new(Modulo.Despachos,
    [
        ArquivoDeModulo,
        new("Página Inicial",
        [
            new(ComandosApp.DespachosAbrir, "Abrir", Glifos.Abrir),
            new(ComandosApp.DespachosAbrirPdf, "Abrir PDF", Glifos.Exportar),
            new(ComandosApp.DespachosGerarPdf, "Gerar PDF", Glifos.Exportar),
            Imprimir,
            new(ComandosApp.DespachosMostrar, "Mostrar no Explorador", Glifos.Explorador),
            new(ComandosApp.DespachosIrAoProcesso, "Ir ao processo", Glifos.IrPara),
            new(ComandosApp.DespachosAtualizar, "Atualizar lista", Glifos.Atualizar),
            new(ComandosApp.DespachosFiltro, "Tipo", Glifos.Filtro, TipoComando.Menu,
            [
                new(ComandosApp.DespachosFiltroTodos, "Todos", ""),
                new(ComandosApp.DespachosFiltroCorrecao, "Correção", ""),
                new(ComandosApp.DespachosFiltroAprovacao, "Aprovação", ""),
            ]),
            new(ComandosApp.DespachosDoProcesso, "Só do processo", Glifos.Pasta, TipoComando.Alternar, Descricao: "Só os despachos do processo escolhido"),
        ]),
        new("Exibir",
        [
            new(ComandosApp.DespachosAgrupar, "Agrupar", Glifos.Lista, TipoComando.Menu,
            [
                new(ComandosApp.DespachosAgruparProcesso, "Por processo", ""),
                new(ComandosApp.DespachosAgruparData, "Por data", ""),
            ]),
            new(ComandosApp.DespachosOrdenar, "Ordenar", Glifos.Ordenar, TipoComando.Menu,
            [
                new(ComandosApp.DespachosOrdenarRecentes, "Mais recentes primeiro", ""),
                new(ComandosApp.DespachosOrdenarAntigos, "Mais antigos primeiro", ""),
            ]),
        ]),
        Ajuda,
    ], AbaPadrao: 1, Primario: NovoDespacho);

    static readonly DefinicaoFaixa Processos = new(Modulo.Processos,
    [
        ArquivoDeModulo,
        new("Página Inicial",
        [
            new(ComandosApp.ProcessosAbrirRelatorio, "Abrir relatório", Glifos.Abrir),
            new(ComandosApp.ProcessosImportarAnalise, "Importar análise", Glifos.ImportarAnalise, Descricao: "Importar a análise da IA"),
            NovoDespacho,
            new(ComandosApp.ProcessosAbrirArquivo, "Abrir arquivo", Glifos.AbrirArquivo),
            Imprimir,
            new(ComandosApp.ProcessosAbrirPasta, "Abrir pasta", Glifos.Explorador, Descricao: "Abrir a pasta no Explorador"),
            new(ComandosApp.ProcessosAtualizar, "Atualizar", Glifos.Atualizar),
            // Menu dinâmico: os anos vêm da pasta de processos (a janela os informa cada vez que o menu abre), como Recentes.
            new(ComandosApp.ProcessosAno, "Ano", Glifos.Ano, TipoComando.Menu, []),
        ]),
        new("Exibir",
        [
            new(ComandosApp.ProcessosOrdenar, "Ordenar", Glifos.Ordenar, TipoComando.Menu,
            [
                new(ComandosApp.ProcessosOrdenarNumero, "Número", ""),
                new(ComandosApp.ProcessosOrdenarData, "Data", ""),
                new(ComandosApp.ProcessosOrdenarRequerente, "Requerente", ""),
            ]),
        ]),
        Ajuda,
    ], AbaPadrao: 1, Primario: NovoRelatorio);

    /// <summary>Legislação (desenho §11.5): o leitor dos PDFs anexados. Comandos que dependem de uma lei aberta, de seleção ou de
    /// marcadores ficam desabilitados sem eles (<see cref="ComandosLegislacao.Habilitados"/>).</summary>
    static readonly DefinicaoFaixa Legislacao = new(Modulo.Legislacao,
    [
        new("Arquivo",
        [
            new(ComandosApp.LegislacaoGerenciar, "Gerenciar anexos", Glifos.Gerenciar),
            new(ComandosApp.ArquivoImportar, "Importar análise da IA…", Glifos.Importar),
            new(ComandosApp.ArquivoAbrir, "Abrir", Glifos.Abrir),
            RecentesMenu,
        ]),
        new("Página Inicial",
        [
            new(ComandosApp.LegislacaoCopiar, "Copiar", Glifos.Copiar),
            new(ComandosApp.LegislacaoCopiarCitacao, "Copiar citação", Glifos.Citacao),
            Imprimir,
            new(ComandosApp.LegislacaoPaginaAnterior, "Página anterior", Glifos.Anterior),
            new(ComandosApp.LegislacaoPaginaProxima, "Próxima página", Glifos.Proximo),
            new(ComandosApp.LegislacaoEscopo, "Pesquisar em", Glifos.Pesquisar, TipoComando.Menu,
            [
                new(ComandosApp.LegislacaoEscopoLei, "Nesta lei", ""),
                new(ComandosApp.LegislacaoEscopoTodas, "Em todas as leis", ""),
            ]),
            new(ComandosApp.LegislacaoOcorrenciaAnterior, "Ocorrência anterior", Glifos.Ocorrencia),
            new(ComandosApp.LegislacaoOcorrenciaProxima, "Próxima ocorrência", Glifos.OcorrenciaProxima),
        ]),
        new("Exibir",
        [
            new(ComandosApp.LegislacaoZoomMenos, "Diminuir", Glifos.Diminuir),
            new(ComandosApp.LegislacaoZoomMais, "Aumentar", Glifos.Aumentar),
            new(ComandosApp.LegislacaoAjustarLargura, "Ajustar largura", Glifos.Ajustar),
            new(ComandosApp.LegislacaoPaginaInteira, "Página inteira", Glifos.PaginaInteiraLei),
            new(ComandosApp.LegislacaoMarcadores, "Marcadores", Glifos.Marcadores, TipoComando.Alternar),
            new(ComandosApp.LegislacaoLista, "Lista de leis", Glifos.Lista, TipoComando.Alternar),
        ]),
        Ajuda,
    ], AbaPadrao: 1, Primario: AnexarLeis);

    /// <summary>Os sistemas da Prefeitura (2026-11-16): a navegação básica do navegador embutido.</summary>
    static readonly AbaFaixa NavegacaoDoSite = new("Página Inicial",
    [
        new(ComandosApp.SiteVoltar, "Voltar", Glifos.Voltar),
        new(ComandosApp.SiteAvancar, "Avançar", Glifos.Avancar),
        new(ComandosApp.SiteAtualizar, "Atualizar", Glifos.Atualizar),
        new(ComandosApp.SiteInicio, "Página inicial", Glifos.Inicio),
        new(ComandosApp.SiteNavegador, "Abrir no navegador", Glifos.AbrirArquivo),
        Imprimir,
    ]);

    static DefinicaoFaixa Site(Modulo m) => new(m, [ArquivoDeModulo, NavegacaoDoSite, Ajuda], AbaPadrao: 1);
    static readonly DefinicaoFaixa SiteEssencial = Site(Modulo.Essencial);
    static readonly DefinicaoFaixa SiteGpi = Site(Modulo.Gpi);
    static readonly DefinicaoFaixa SiteAlfresco = Site(Modulo.Alfresco);

    /// <summary>Imóveis (2026-11-16): com um relatório aberto, voltar, PDF e imprimir; sempre, a releitura e a pasta QGIS.</summary>
    static readonly DefinicaoFaixa Imoveis = new(Modulo.Imoveis,
    [
        ArquivoDeModulo,
        new("Página Inicial",
        [
            new(ComandosApp.ImoveisVoltar, "Voltar à pesquisa", Glifos.Voltar),
            new(ComandosApp.ImoveisSalvarPdf, "Salvar PDF", Glifos.Exportar),
            new(ComandosApp.ImoveisImprimir, "Imprimir", Glifos.Imprimir),
            new(ComandosApp.ImoveisRecarregar, "Ler a base de novo", Glifos.Atualizar),
            new(ComandosApp.ImoveisConfigurar, "Pasta QGIS", Glifos.Pasta),
        ]),
        Ajuda,
    ], AbaPadrao: 1);

    /// <summary>Acompanhamento de processos (2026-11-17): a planilha de tramitação.</summary>
    static readonly DefinicaoFaixa Acompanhamento = new(Modulo.Acompanhamento,
    [
        ArquivoDeModulo,
        new("Página Inicial",
        [
            new(ComandosApp.AcompanhamentoEditar, "Editar processo", Glifos.Editar),
            new(ComandosApp.AcompanhamentoExcluir, "Excluir processo", Glifos.Excluir),
            new(ComandosApp.AcompanhamentoDetectar, "Detectar local e autor", Glifos.Conferir),
            new(ComandosApp.AcompanhamentoRecarregar, "Ler a planilha de novo", Glifos.Atualizar),
            new(ComandosApp.AcompanhamentoAbrirExcel, "Abrir no Excel", Glifos.Abrir),
            new(ComandosApp.AcompanhamentoImportar, "Escolher planilha…", Glifos.Importar),
            new(ComandosApp.AcompanhamentoConfigurar, "Local da planilha", Glifos.Pasta),
        ]),
        Ajuda,
    ], AbaPadrao: 1, Primario: new(ComandosApp.AcompanhamentoNovo, "Novo processo", Glifos.Adicionar));

    /// <summary>Ajustes não abre relatórios: só a Ajuda (auditoria de interface, 2026-11-17: a aba Arquivo aqui confundia).</summary>
    static readonly DefinicaoFaixa Ajustes = new(Modulo.Ajustes,
    [
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
        Modulo.Essencial => SiteEssencial,
        Modulo.Gpi => SiteGpi,
        Modulo.Alfresco => SiteAlfresco,
        Modulo.Imoveis => Imoveis,
        Modulo.Acompanhamento => Acompanhamento,
        _ => throw new ArgumentOutOfRangeException(nameof(m), m, "Módulo ainda sem faixa de opções."),
    };

    /// <summary>Todos os comandos de uma faixa (o primário e os itens dos menus inclusive), o primário primeiro e depois na ordem das abas.</summary>
    public static IEnumerable<ComandoFaixa> Comandos(DefinicaoFaixa f) =>
        new[] { f.Primario, f.Secundario }.OfType<ComandoFaixa>().Concat(f.Abas.SelectMany(a => a.Comandos))
            .SelectMany(c => c.Itens is null ? [c] : c.Itens.Prepend(c));

    /// <summary>O índice da aba lembrada (pelo nome), ou a padrão se ela não existe mais.</summary>
    public static int IndiceDaAba(DefinicaoFaixa f, string? nome)
    {
        for (var i = 0; i < f.Abas.Count; i++)
            if (string.Equals(f.Abas[i].Nome, nome, StringComparison.Ordinal)) return i;
        return f.AbaPadrao;
    }
}

/// <summary>Os nomes dos temas, iguais em Exibir › Tema e em Ajustes › Aparência (o termo do WinStatusBar, auditoria B4).</summary>
public static class TextosTema
{
    public const string Sistema = "Acompanhar o Windows";
    public const string Claro = "Claro";
    public const string Escuro = "Escuro";

    /// <summary>O texto do tema guardado em <see cref="Ajustes.Tema"/> (<c>sistema</c>, <c>claro</c>, <c>escuro</c>).</summary>
    public static string De(string tema) => tema switch { "claro" => Claro, "escuro" => Escuro, _ => Sistema };
}
