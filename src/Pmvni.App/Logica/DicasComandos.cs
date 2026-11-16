using Pmvni.Composicao.Kit;

namespace Pmvni.App;

/// <summary>O conteúdo de uma dica (o que aparece ao parar o mouse em cima de um botão): o título em negrito (nome e, se houver, o
/// atalho entre parênteses), uma frase curta dizendo o que o botão faz e, quando o botão está desabilitado, o motivo.</summary>
public sealed record TextoDica(string Titulo, string Descricao, string? Motivo = null)
{
    /// <summary>A dica em texto corrido (nome acessível, registros do roteiro): as linhas não vazias, uma por linha.</summary>
    public override string ToString() =>
        string.Join(Environment.NewLine, new[] { Titulo, Descricao, Motivo ?? "" }.Where(s => s.Length > 0));
}

/// <summary>As descrições curtas dos comandos do app (faixa de opções, cabeçalho e trilho), por id de <see cref="ComandosApp"/>, e o
/// formato único das dicas (pedido do usuário, correções 2026-11-15: "breves descrições sobre o que ele faz… além do nome dele").
/// Sem tipos WinUI: compilado também nos testes. Os controles montam a dica com <c>Dicas</c> (Controles\Dicas.cs).</summary>
public static class DicasComandos
{
    static readonly Dictionary<string, string> descricoes = new(StringComparer.Ordinal)
    {
        // Arquivo
        [ComandosApp.ArquivoImportar] = "Cria um relatório novo a partir do dados.json da análise feita pela IA.",
        [ComandosApp.ArquivoAbrir] = "Abre um relatório .rpmvni salvo no computador.",
        [ComandosApp.ArquivoRecentes] = "Mostra os relatórios abertos há pouco, para abrir um deles de novo.",
        [ComandosApp.ArquivoSalvar] = "Grava as alterações do relatório no arquivo .rpmvni.",
        [ComandosApp.ArquivoSalvarComo] = "Grava o relatório num arquivo novo, com outro nome ou em outra pasta.",
        [ComandosApp.ArquivoExportarPdf] = "Gera o PDF do relatório, com as mesmas folhas mostradas na tela.",
        [ComandosApp.ArquivoFechar] = "Fecha o relatório aberto.",
        [ComandosApp.RelatoriosNovo] = "Pede a uma IA a análise de um processo e monta o relatório dela.",

        // Página Inicial (o item selecionado)
        [ComandosApp.InicioSituacao] = "Muda a situação do item selecionado: atende, não atende, não comprovado ou divergência.",
        [ComandosApp.InicioExcluirRestaurar] = "Tira o item selecionado do relatório; num item já excluído, traz ele de volta.",
        [ComandosApp.InicioEditarTexto] = "Edita o texto do item selecionado direto na folha.",
        [ComandosApp.InicioOriginal] = "Mostra o texto original recebido da IA, sem as suas edições.",
        [ComandosApp.InicioRestaurarOriginal] = "Desfaz as edições do item selecionado e volta ao texto original.",
        [ComandosApp.InicioItemAnterior] = "Seleciona o item anterior do relatório.",
        [ComandosApp.InicioItemProximo] = "Seleciona o próximo item do relatório.",
        [ComandosApp.InicioProximoPendente] = "Vai ao próximo item que não está ATENDE ou que ainda pede justificativa.",

        // Revisão
        [ComandosApp.RevisaoMarcas] = "Mostra ou esconde, nas folhas, as marcas do que foi alterado.",
        [ComandosApp.RevisaoSomenteAlterados] = "Mostra só os itens que você alterou e esconde os outros.",
        [ComandosApp.RevisaoConferencia] = "Lista os erros que impedem exportar o PDF e os avisos sobre citações.",
        [ComandosApp.RevisaoComparar] = "Resume o que mudou no relatório em relação ao original da IA.",

        // Figuras
        [ComandosApp.FigurasInserirRegiao] = "Recorta uma região de uma prancha do projeto (PDF) e a insere como figura.",
        [ComandosApp.FigurasEditar] = "Abre o editor para pôr setas, regiões e textos na figura selecionada.",
        [ComandosApp.FigurasExcluir] = "Tira do relatório a figura selecionada.",

        // Exibir (relatório)
        [ComandosApp.ExibirMenos] = "Diminui o zoom das folhas do relatório.",
        [ComandosApp.ExibirMais] = "Aumenta o zoom das folhas do relatório.",
        [ComandosApp.ExibirLargura] = "Ajusta o zoom para a folha ocupar a largura da janela.",
        [ComandosApp.ExibirPagina] = "Ajusta o zoom para mostrar a página inteira na tela.",
        [ComandosApp.ExibirPainel] = "Mostra ou esconde o painel das seções, à esquerda das folhas.",
        [ComandosApp.ExibirPropriedades] = "Mostra ou esconde o painel com os dados do item selecionado.",
        [ComandosApp.ExibirTema] = "Escolhe as cores do app: claras, escuras ou iguais às do Windows.",
        [ComandosApp.ExibirTemaSistema] = "Usa cores claras ou escuras conforme o Windows estiver configurado.",
        [ComandosApp.ExibirTemaClaro] = "Usa sempre as cores claras.",
        [ComandosApp.ExibirTemaEscuro] = "Usa sempre as cores escuras.",

        // Despachos
        [ComandosApp.DespachosNovo] = "Cria um despacho de correção ou de aprovação a partir do relatório revisado.",
        [ComandosApp.DespachosNovoCorrecao] = "Cria um despacho pedindo as correções apontadas no relatório.",
        [ComandosApp.DespachosNovoAprovacao] = "Cria um despacho aprovando o projeto analisado.",
        [ComandosApp.DespachosAbrir] = "Abre o despacho selecionado no programa padrão do Windows (Word ou leitor de PDF).",
        [ComandosApp.DespachosAbrirPdf] = "Abre o PDF do despacho selecionado.",
        [ComandosApp.DespachosGerarPdf] = "Gera o PDF do despacho que ainda só existe em Word.",
        [ComandosApp.DespachosMostrar] = "Abre a pasta do despacho selecionado no Explorador de Arquivos.",
        [ComandosApp.DespachosIrAoProcesso] = "Mostra em Processos o processo do despacho selecionado.",
        [ComandosApp.DespachosAtualizar] = "Lê de novo a pasta de processos e atualiza a lista de despachos.",
        [ComandosApp.DespachosFiltro] = "Mostra todos os despachos ou só os de um tipo.",
        [ComandosApp.DespachosFiltroTodos] = "Mostra os despachos de correção e de aprovação.",
        [ComandosApp.DespachosFiltroCorrecao] = "Mostra só os despachos de correção.",
        [ComandosApp.DespachosFiltroAprovacao] = "Mostra só os despachos de aprovação.",
        [ComandosApp.DespachosDoProcesso] = "Mostra só os despachos do processo escolhido em Processos.",
        [ComandosApp.DespachosAgrupar] = "Agrupa a lista de despachos por processo ou por data.",
        [ComandosApp.DespachosAgruparProcesso] = "Junta os despachos de cada processo.",
        [ComandosApp.DespachosAgruparData] = "Junta os despachos pela data em que foram feitos.",
        [ComandosApp.DespachosOrdenar] = "Muda a ordem da lista de despachos pela data.",
        [ComandosApp.DespachosOrdenarRecentes] = "Põe os despachos mais novos no topo da lista.",
        [ComandosApp.DespachosOrdenarAntigos] = "Põe os despachos mais antigos no topo da lista.",

        // Processos
        [ComandosApp.ProcessosAbrirRelatorio] = "Abre o relatório ou a análise da IA selecionada na lista da direita.",
        [ComandosApp.ProcessosImportarAnalise] = "Cria o relatório a partir da análise que a IA gravou na pasta do processo.",
        [ComandosApp.ProcessosAbrirArquivo] = "Abre o arquivo selecionado no programa padrão do Windows.",
        [ComandosApp.ProcessosAbrirPasta] = "Abre a pasta do processo selecionado no Explorador de Arquivos.",
        [ComandosApp.ProcessosAtualizar] = "Lê de novo a pasta de processos e atualiza a lista.",
        [ComandosApp.ProcessosAno] = "Mostra os processos de outro ano.",
        [ComandosApp.ProcessosOrdenar] = "Muda a ordem da lista de processos.",
        [ComandosApp.ProcessosOrdenarNumero] = "Ordena os processos pelo número.",
        [ComandosApp.ProcessosOrdenarData] = "Ordena os processos pela data, os mais novos primeiro.",
        [ComandosApp.ProcessosOrdenarRequerente] = "Ordena os processos pelo nome do requerente, em ordem alfabética.",

        // Legislação
        [ComandosApp.LegislacaoAnexar] = "Acrescenta leis em PDF à biblioteca de legislação do app.",
        [ComandosApp.LegislacaoGerenciar] = "Abre os ajustes da Legislação para renomear, reler ou tirar leis anexadas.",
        [ComandosApp.LegislacaoCopiar] = "Copia o trecho selecionado da lei.",
        [ComandosApp.LegislacaoCopiarCitacao] = "Copia o trecho selecionado com o título da lei e a página, pronto para citar.",
        [ComandosApp.LegislacaoPaginaAnterior] = "Volta para a página anterior da lei.",
        [ComandosApp.LegislacaoPaginaProxima] = "Avança para a próxima página da lei.",
        [ComandosApp.LegislacaoEscopo] = "Escolhe se a pesquisa procura só na lei aberta ou em todas.",
        [ComandosApp.LegislacaoEscopoLei] = "Pesquisa só na lei aberta.",
        [ComandosApp.LegislacaoEscopoTodas] = "Pesquisa em todas as leis anexadas.",
        [ComandosApp.LegislacaoOcorrenciaAnterior] = "Vai ao resultado anterior da pesquisa.",
        [ComandosApp.LegislacaoOcorrenciaProxima] = "Vai ao próximo resultado da pesquisa.",
        [ComandosApp.LegislacaoZoomMenos] = "Diminui o zoom da lei.",
        [ComandosApp.LegislacaoZoomMais] = "Aumenta o zoom da lei.",
        [ComandosApp.LegislacaoAjustarLargura] = "Ajusta o zoom para a página da lei ocupar a largura da janela.",
        [ComandosApp.LegislacaoPaginaInteira] = "Ajusta o zoom para mostrar a página inteira da lei.",
        [ComandosApp.LegislacaoMarcadores] = "Mostra ou esconde os marcadores da lei (títulos, capítulos e artigos).",
        [ComandosApp.LegislacaoLista] = "Mostra ou esconde a lista das leis anexadas.",

        // Ajuda
        [ComandosApp.AjudaAtalhos] = "Mostra a lista dos atalhos de teclado do app.",
        [ComandosApp.AjudaSobre] = "Mostra a versão do app e informações sobre ele.",

        // Fora da faixa: cabeçalho e trilho
        [ComandosApp.EditarDesfazer] = "Desfaz a última alteração feita no relatório.",
        [ComandosApp.EditarRefazer] = "Refaz a alteração que você acabou de desfazer.",
        [ComandosApp.PesquisaFocar] = "Procura uma palavra ou trecho no que está aberto.",
        [ComandosApp.ModuloRelatorios] = "Abre, revisa e exporta os relatórios de análise de projetos.",
        [ComandosApp.ModuloDespachos] = "Lista os despachos de correção e de aprovação já feitos.",
        [ComandosApp.ModuloProcessos] = "Mostra os processos da pasta da Secretaria, com projetos e documentos.",
        [ComandosApp.ModuloLegislacao] = "Consulta as leis anexadas, com pesquisa, marcadores e citação.",
        [ComandosApp.ModuloAjustes] = "Configura pastas, IAs, aparência, legislação e outras preferências do app.",
        [ComandosApp.ModuloEssencial] = "Abre o Essencial BPMS (processos e protocolos) dentro do app.",
        [ComandosApp.ModuloGpi] = "Abre o GPI (gestão pública integrada) dentro do app.",
        [ComandosApp.ModuloAlfresco] = "Abre os documentos de Obras no Alfresco dentro do app.",
        [ComandosApp.ModuloImoveis] = "Pesquisa imóveis no cadastro e na base QGIS e abre o relatório cadastral e territorial.",
        [ComandosApp.ImoveisConfigurar] = "Abre Ajustes › Fontes de dados para escolher a pasta dos arquivos QGIS.",
        [ComandosApp.ImoveisRecarregar] = "Lê de novo a pasta QGIS configurada (depois de atualizar a base).",
        [ComandosApp.ImoveisVoltar] = "Volta à pesquisa, com os resultados como estavam.",
        [ComandosApp.ImoveisSalvarPdf] = "Grava o relatório aberto em PDF.",
        [ComandosApp.ImoveisImprimir] = "Imprime o relatório aberto.",
        [ComandosApp.SiteVoltar] = "Volta à página anterior do site.",
        [ComandosApp.SiteAvancar] = "Avança para a página seguinte do site.",
        [ComandosApp.SiteAtualizar] = "Carrega a página de novo.",
        [ComandosApp.SiteInicio] = "Volta à página inicial do sistema.",
        [ComandosApp.SiteNavegador] = "Abre a página atual no navegador do Windows.",
    };

    /// <summary>Todas as descrições, por id de comando.</summary>
    public static IReadOnlyDictionary<string, string> Descricoes => descricoes;

    /// <summary>A descrição do comando: a da tabela; para os itens "Situação ▸ X", uma frase com o texto da situação do kit; senão "".</summary>
    public static string Descricao(string id)
    {
        if (descricoes.TryGetValue(id, out var d)) return d;
        if (ComandosApp.SituacaoDoComando(id) is { } codigo && Situacoes.Sit.TryGetValue(codigo, out var s))
            return $"Marca o item selecionado como \"{s.Texto}\".";
        return "";
    }

    /// <summary>O que faz cada botão do filtro rápido do painel de seções.</summary>
    public static string DescricaoFiltro(FiltroSituacao f) => f switch
    {
        FiltroSituacao.NaoAtende => "Mostra só os itens que não atendem.",
        FiltroSituacao.FaltaComprovar => "Mostra só os itens que ainda faltam comprovar.",
        FiltroSituacao.Divergentes => "Mostra só os itens com divergência entre os documentos.",
        FiltroSituacao.Atende => "Mostra só os itens que atendem.",
        _ => "Mostra todos os itens, de qualquer situação.",
    };

    /// <summary>O título da dica: o nome e, se houver, o atalho entre parênteses ("Abrir (Ctrl+O)").</summary>
    public static string Titulo(string nome, string? atalho) => string.IsNullOrEmpty(atalho) ? nome : $"{nome} ({atalho})";

    /// <summary>O atalho do comando no módulo, como o usuário lê ("Ctrl+O", "Alt+↑"), ou null.</summary>
    public static string? Atalho(string id, Modulo m) => Atalhos.Tecla(id, m) is { } t ? Atalhos.Legivel(t) : null;

    /// <summary>A dica de um comando: o nome (o texto completo do botão), o atalho do módulo e a descrição da tabela. O motivo (botão
    /// desabilitado) entra como terceira linha só quando é diferente da descrição.</summary>
    public static TextoDica DoComando(string id, string nome, Modulo m, string? motivo = null)
    {
        var descricao = Descricao(id);
        return new TextoDica(Titulo(nome, Atalho(id, m)), descricao, Normalizar(motivo, descricao));
    }

    /// <summary>Uma dica livre (botões das páginas): título, descrição e, se houver, motivo.</summary>
    public static TextoDica Livre(string nome, string descricao, string? atalho = null, string? motivo = null) =>
        new(Titulo(nome, atalho), descricao, Normalizar(motivo, descricao));

    static string? Normalizar(string? motivo, string descricao) =>
        string.IsNullOrWhiteSpace(motivo) || string.Equals(motivo.Trim(), descricao, StringComparison.Ordinal) ? null : motivo.Trim();
}
