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

    public const string AjudaAtalhos = "ajuda.atalhos";
    public const string AjudaSobre = "ajuda.sobre";

    // Fora da faixa (Tarefa 9): Desfazer/Refazer da barra de título, F6 e a pesquisa (Ctrl+F foca a caixa da barra de título).
    public const string EditarDesfazer = "editar.desfazer";
    public const string EditarRefazer = "editar.refazer";
    public const string JanelaRegioes = "janela.regioes";
    public const string PesquisaFocar = "pesquisa.focar";

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
