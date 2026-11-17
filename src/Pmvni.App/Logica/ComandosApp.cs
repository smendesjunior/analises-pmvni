using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Ids dos comandos da faixa de opções (e dos atalhos). Cada tarefa seguinte acrescenta os seus.</summary>
public static class ComandosApp
{
    public const string ArquivoImportar = "arquivo.importar";
    public const string ArquivoAbrir = "arquivo.abrir";
    public const string ArquivoSalvar = "arquivo.salvar";
    public const string ArquivoSalvarComo = "arquivo.salvarComo";
    public const string ArquivoExportarPdf = "arquivo.exportarPdf";
    public const string ArquivoFechar = "arquivo.fechar";
    /// <summary>Menu com os relatórios abertos há pouco (Tarefa 11); os itens vêm dos ajustes, na hora de abrir o menu.</summary>
    public const string ArquivoRecentes = "arquivo.recentes";

    // Página Inicial (Tarefa 9): o item selecionado.
    public const string InicioSituacao = "inicio.situacao";
    public const string InicioSituacaoAtende = "inicio.situacao.ATENDE";
    public const string InicioSituacaoNaoAtende = "inicio.situacao.NAO_ATENDE";
    public const string InicioSituacaoNaoComprovado = "inicio.situacao.NAO_COMPROVADO";
    public const string InicioSituacaoDivergencia = "inicio.situacao.DIVERGENCIA_INTERNA";
    public const string InicioExcluirRestaurar = "inicio.excluirRestaurar";
    /// <summary>Novo item (correções 2026-11-16): cria um item de análise logo depois do selecionado (ou no fim).</summary>
    public const string InicioNovoItem = "inicio.novoItem";
    public const string InicioEditarTexto = "inicio.editarTexto";
    public const string InicioOriginal = "inicio.original";
    public const string InicioRestaurarOriginal = "inicio.restaurarOriginal";
    public const string InicioItemAnterior = "inicio.itemAnterior";
    public const string InicioItemProximo = "inicio.itemProximo";
    public const string InicioProximoPendente = "inicio.proximoPendente";

    // Revisão (Tarefa 9).
    public const string RevisaoMarcas = "revisao.marcas";
    public const string RevisaoSomenteAlterados = "revisao.somenteAlterados";
    public const string RevisaoConferencia = "revisao.conferencia";
    public const string RevisaoComparar = "revisao.comparar";

    public const string ExibirMenos = "exibir.menos";
    public const string ExibirMais = "exibir.mais";
    public const string ExibirLargura = "exibir.largura";
    public const string ExibirPagina = "exibir.pagina";
    public const string ExibirPainel = "exibir.painel";
    public const string ExibirPropriedades = "exibir.propriedades";
    public const string ExibirTema = "exibir.tema";
    public const string ExibirTemaSistema = "exibir.tema.sistema";
    public const string ExibirTemaClaro = "exibir.tema.claro";
    public const string ExibirTemaEscuro = "exibir.tema.escuro";

    // Processos (Tarefa 13): ações sobre a pasta do processo escolhido. processos.ano é um menu dinâmico (os anos vêm da pasta).
    public const string ProcessosAbrirRelatorio = "processos.abrirRelatorio";
    public const string ProcessosImportarAnalise = "processos.importarAnalise";
    public const string ProcessosAbrirArquivo = "processos.abrirArquivo";
    public const string ProcessosAbrirPasta = "processos.abrirPasta";
    public const string ProcessosAtualizar = "processos.atualizar";
    public const string ProcessosAno = "processos.ano";
    public const string ProcessosOrdenar = "processos.ordenar";
    public const string ProcessosOrdenarNumero = "processos.ordenar.numero";
    public const string ProcessosOrdenarData = "processos.ordenar.data";
    public const string ProcessosOrdenarRequerente = "processos.ordenar.requerente";

    // Despachos (Tarefa 13): só listar e abrir os que já existem (a criação é das Tarefas 15 e 16).
    public const string DespachosAbrir = "despachos.abrir";
    public const string DespachosAbrirPdf = "despachos.abrirPdf";
    public const string DespachosMostrar = "despachos.mostrar";
    public const string DespachosIrAoProcesso = "despachos.irAoProcesso";
    public const string DespachosAtualizar = "despachos.atualizar";
    public const string DespachosFiltro = "despachos.filtro";
    public const string DespachosFiltroTodos = "despachos.filtro.todos";
    public const string DespachosFiltroCorrecao = "despachos.filtro.correcao";
    public const string DespachosFiltroAprovacao = "despachos.filtro.aprovacao";
    /// <summary>Alterna entre os despachos de todos os processos e só os do processo escolhido em Processos.</summary>
    public const string DespachosDoProcesso = "despachos.doProcesso";
    public const string DespachosAgrupar = "despachos.agrupar";
    public const string DespachosAgruparProcesso = "despachos.agrupar.processo";
    public const string DespachosAgruparData = "despachos.agrupar.data";
    public const string DespachosOrdenar = "despachos.ordenar";
    public const string DespachosOrdenarRecentes = "despachos.ordenar.recentes";
    public const string DespachosOrdenarAntigos = "despachos.ordenar.antigos";

    // Novo despacho (Tarefa 16): o menu com os tipos do registro do kit (em Despachos, Processos e Relatórios) e Gerar PDF (só Despachos).
    public const string DespachosNovo = "despachos.novo";
    public const string DespachosNovoCorrecao = "despachos.novo.correcao";
    public const string DespachosNovoAprovacao = "despachos.novo.aprovacao";
    /// <summary>Correção automática (2026-11-16): monta o despacho de correção com todos os itens pendentes do relatório mais recente do
    /// processo e vai direto para a conferência.</summary>
    public const string DespachosNovoCorrecaoAutomatica = "despachos.novo.correcaoAutomatica";
    public const string DespachosGerarPdf = "despachos.gerarPdf";

    /// <summary>Os itens do Novo despacho valem em qualquer módulo que os mostra: abrem a página Novo despacho no módulo Despachos.
    /// Os outros comandos <c>despachos.*</c> só valem com a lista de despachos à vista.</summary>
    public static bool ValeForaDeDespachos(string id) => id is DespachosNovoCorrecao or DespachosNovoAprovacao or DespachosNovoCorrecaoAutomatica;

    /// <summary>O item "Correção automática" do menu Novo despacho.</summary>
    public static bool EhCorrecaoAutomatica(string id) => id == DespachosNovoCorrecaoAutomatica;

    /// <summary>O tipo do item <c>despachos.novo.*</c>; null para os outros ids.</summary>
    public static TipoDespacho? TipoDoNovoDespacho(string id) => id switch
    {
        DespachosNovoCorrecao or DespachosNovoCorrecaoAutomatica => TipoDespacho.Correcao,
        DespachosNovoAprovacao => TipoDespacho.Aprovacao,
        _ => null,
    };

    /// <summary>Novo relatório pelos agentes locais (Tarefa 20; desenho §12.2): abre a página Novo relatório no módulo Relatórios, de qualquer módulo que o mostra.</summary>
    public const string RelatoriosNovo = "relatorios.novo";

    // Legislação (Tarefa 18; desenho §11.5): o leitor dos PDFs anexados.
    public const string LegislacaoAnexar = "legislacao.anexar";
    public const string LegislacaoGerenciar = "legislacao.gerenciar";
    public const string LegislacaoCopiar = "legislacao.copiar";
    public const string LegislacaoCopiarCitacao = "legislacao.copiarCitacao";
    public const string LegislacaoPaginaAnterior = "legislacao.paginaAnterior";
    public const string LegislacaoPaginaProxima = "legislacao.paginaProxima";
    /// <summary>Menu "Pesquisar em": "Nesta lei" ou "Em todas as leis" (o alvo da caixa de pesquisa da barra de título).</summary>
    public const string LegislacaoEscopo = "legislacao.escopo";
    public const string LegislacaoEscopoLei = "legislacao.escopo.lei";
    public const string LegislacaoEscopoTodas = "legislacao.escopo.todas";
    public const string LegislacaoOcorrenciaAnterior = "legislacao.ocorrenciaAnterior";
    public const string LegislacaoOcorrenciaProxima = "legislacao.ocorrenciaProxima";
    public const string LegislacaoZoomMenos = "legislacao.zoomMenos";
    public const string LegislacaoZoomMais = "legislacao.zoomMais";
    public const string LegislacaoAjustarLargura = "legislacao.ajustarLargura";
    public const string LegislacaoPaginaInteira = "legislacao.paginaInteira";
    public const string LegislacaoMarcadores = "legislacao.marcadores";
    public const string LegislacaoLista = "legislacao.lista";

    // Figuras (Etapa 4): editor de marcações das figuras e recorte de prancha.
    public const string FigurasEditar = "figuras.editar";
    public const string FigurasInserirRegiao = "figuras.inserirRegiao";
    public const string FigurasExcluir = "figuras.excluir";
    /// <summary>Figuras ▾ da Página Inicial (correções 2026-11-16): menu dinâmico com as figuras do relatório; a escolhida abre no editor.</summary>
    public const string FigurasAbrirEditor = "figuras.abrirEditor";

    public const string AjudaAtalhos = "ajuda.atalhos";
    public const string AjudaSobre = "ajuda.sobre";

    // Fora da faixa (Tarefa 9): Desfazer/Refazer da barra de título, F6 e a pesquisa (Ctrl+F foca a caixa da barra de título).
    public const string EditarDesfazer = "editar.desfazer";
    public const string EditarRefazer = "editar.refazer";
    public const string JanelaRegioes = "janela.regioes";
    public const string PesquisaFocar = "pesquisa.focar";

    // Trilho pelo teclado (auditoria da interface, M2): Ctrl+1 … Ctrl+5 levam ao módulo, de qualquer lugar (página e leitor inclusive).
    public const string ModuloRelatorios = "modulo.relatorios";
    public const string ModuloDespachos = "modulo.despachos";
    public const string ModuloProcessos = "modulo.processos";
    public const string ModuloLegislacao = "modulo.legislacao";
    public const string ModuloAjustes = "modulo.ajustes";
    public const string ModuloEssencial = "modulo.essencial";
    public const string ModuloGpi = "modulo.gpi";
    public const string ModuloAlfresco = "modulo.alfresco";
    public const string ModuloImoveis = "modulo.imoveis";
    public const string ImoveisConfigurar = "imoveis.configurar";
    public const string ImoveisRecarregar = "imoveis.recarregar";
    public const string ImoveisVoltar = "imoveis.voltar";
    public const string ImoveisSalvarPdf = "imoveis.salvarpdf";
    public const string ImoveisImprimir = "imoveis.imprimir";
    public const string ModuloAcompanhamento = "modulo.acompanhamento";
    public const string AcompanhamentoNovo = "acompanhamento.novo";
    public const string AcompanhamentoEditar = "acompanhamento.editar";
    public const string AcompanhamentoExcluir = "acompanhamento.excluir";
    public const string AcompanhamentoRecarregar = "acompanhamento.recarregar";
    public const string AcompanhamentoAbrirExcel = "acompanhamento.abrirexcel";
    public const string AcompanhamentoImportar = "acompanhamento.importar";
    public const string AcompanhamentoDetectar = "acompanhamento.detectar";
    public const string AcompanhamentoConfigurar = "acompanhamento.configurar";
    public const string SiteVoltar = "site.voltar";
    public const string SiteAvancar = "site.avancar";
    public const string SiteAtualizar = "site.atualizar";
    public const string SiteInicio = "site.inicio";
    public const string SiteNavegador = "site.navegador";

    /// <summary>Comandos que mexem no relatório aberto ou na tela dele (Página Inicial, Revisão, desfazer, zoom, painel). Fora do módulo
    /// Relatórios não valem: um Delete ou Ctrl+Z com a lista de processos à vista não pode alterar um relatório que ficou escondido.
    /// Arquivo (Salvar, Abrir…), pesquisa, tema e ajuda valem em qualquer módulo.</summary>
    public static bool SoDoRelatorio(string id) =>
        id.StartsWith("inicio.", StringComparison.Ordinal) || id.StartsWith("revisao.", StringComparison.Ordinal)
        || id.StartsWith("editar.", StringComparison.Ordinal) || id.StartsWith("figuras.", StringComparison.Ordinal)
        || id is ExibirMenos or ExibirMais or ExibirLargura or ExibirPagina or ExibirPainel or ExibirPropriedades or JanelaRegioes;

    const string PrefixoSituacao = InicioSituacao + ".";
    const string PrefixoTema = ExibirTema + ".";

    /// <summary>O tema (<c>sistema</c>, <c>claro</c>, <c>escuro</c>: o valor de <see cref="Ajustes.Tema"/>) do id <c>exibir.tema.*</c>; null para os outros ids.</summary>
    public static string? TemaDoComando(string id) =>
        id.StartsWith(PrefixoTema, StringComparison.Ordinal) && id.Length > PrefixoTema.Length ? id[PrefixoTema.Length..] : null;

    /// <summary>O id do item "Situação ▸ <paramref name="codigo"/>" (código do kit: ATENDE, NAO_ATENDE…).</summary>
    public static string ComandoDaSituacao(string codigo) => PrefixoSituacao + codigo;

    /// <summary>O código da situação de um id <c>inicio.situacao.CODIGO</c>; null para os outros ids.</summary>
    public static string? SituacaoDoComando(string id) =>
        id.StartsWith(PrefixoSituacao, StringComparison.Ordinal) && id.Length > PrefixoSituacao.Length ? id[PrefixoSituacao.Length..] : null;
}
