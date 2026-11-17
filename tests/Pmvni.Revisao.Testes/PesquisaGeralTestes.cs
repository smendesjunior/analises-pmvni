using Pmvni.App;
using Pmvni.Legislacao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Pesquisa geral da barra de título (2026-11-17): catálogo (funções, ajustes, processos, arquivos, leis, planilha) e comparação
/// sem acento e sem caixa, por começo de palavra, com igual &gt; começa com &gt; contém e limites por grupo e no total.</summary>
public class PesquisaGeralTestes
{
    static readonly OpcaoAjusteTela[] OpcoesFicticias =
    [
        new(CategoriasAjustes.Aparencia, "", "Janela", "", EhGrupo: true),
        new(CategoriasAjustes.Aparencia, "Janela", "Tema", "Muda só a janela do editor."),
        new(CategoriasAjustes.FontesDados, "Base QGIS", "Pasta dos arquivos QGIS", "A pasta com os arquivos da base."),
        new(CategoriasAjustes.Relatorios, "Gravação", "Salvamento automático", "O relatório é gravado sozinho."),
    ];

    static IReadOnlyList<ItemPesquisaGeral> Catalogo() => [.. PesquisaGeral.Funcoes(), .. PesquisaGeral.Ajustes(OpcoesFicticias)];

    static IReadOnlyList<ItemPesquisaGeral> Achados(string termo, IEnumerable<ItemPesquisaGeral> itens) =>
        PesquisaGeral.Pesquisar(termo, itens).SelectMany(g => g.Itens).ToList();

    [Fact]
    public void normaliza_sem_acento_e_sem_caixa_e_separa_palavras()
    {
        Assert.Equal("relatorios e revisao", PesquisaGeral.Normalizar("Relatórios e Revisão"));
        Assert.Equal(["processo", "15004", "2026"], PesquisaGeral.Palavras(PesquisaGeral.Normalizar("Processo 15004/2026")));
        Assert.Empty(PesquisaGeral.Palavras(""));
    }

    [Fact]
    public void termo_curto_nao_acha_nada()
    {
        Assert.Empty(PesquisaGeral.Pesquisar("", Catalogo()));
        Assert.Empty(PesquisaGeral.Pesquisar("t", Catalogo()));
        Assert.Empty(PesquisaGeral.Pesquisar("  ", Catalogo()));
    }

    [Fact]
    public void ordem_igual_comeca_palavras_contem()
    {
        ItemPesquisaGeral I(string titulo, string onde = "", params string[] palavras) =>
            new(TipoResultadoGeral.Funcao, titulo, onde, "", "k:" + titulo, palavras);
        Assert.Equal(1000, PesquisaGeral.Pontuar(I("Tema"), "tema"));
        Assert.Equal(900, PesquisaGeral.Pontuar(I("Temas do app"), "tema"));
        Assert.Equal(800, PesquisaGeral.Pontuar(I("Salvamento automático"), "salv auto"));
        Assert.Equal(750, PesquisaGeral.Pontuar(I("Pasta do kit"), "kit"));
        Assert.Equal(500, PesquisaGeral.Pontuar(I("Rótulos", "Ajustes › Aparência"), "aparencia"));
        Assert.Equal(400, PesquisaGeral.Pontuar(I("Subtema"), "tema"));
        Assert.Equal(200, PesquisaGeral.Pontuar(I("Abc", "", "xyztemaxyz"), "tema"));
        Assert.Equal(0, PesquisaGeral.Pontuar(I("Zoom"), "tema"));
        // Sem acento e sem caixa nos dois lados.
        Assert.Equal(1000, PesquisaGeral.Pontuar(I("Legislação"), "LEGISLACAO"));
    }

    [Fact]
    public void ajuste_tema_vem_primeiro_e_leva_a_aparencia()
    {
        var grupos = PesquisaGeral.Pesquisar("tema", Catalogo());
        Assert.Equal("Ajustes", grupos[0].Nome);
        var primeiro = grupos[0].Itens[0];
        Assert.Equal("Tema", primeiro.Titulo);
        Assert.Equal("Ajustes › Aparência › Janela", primeiro.Onde);
        var opcao = Assert.IsType<OpcaoAjusteTela>(primeiro.Dado);
        Assert.Equal(CategoriasAjustes.Aparencia, opcao.Categoria);
        // Os itens do menu Exibir › Tema também aparecem, como funções.
        Assert.Contains(grupos.Single(g => g.Nome == "Funções").Itens, i => i.Titulo.StartsWith("Tema › ", StringComparison.Ordinal));
    }

    [Fact]
    public void categorias_dos_ajustes_entram_mesmo_sem_a_tela()
    {
        var itens = PesquisaGeral.Ajustes([]);
        Assert.Equal(CategoriasAjustes.Menu.Count, itens.Count);
        var fontes = Achados("fontes de dados", itens).First();
        Assert.Equal("Fontes de dados", fontes.Titulo);
        Assert.Equal("Ajustes", fontes.Onde);
        // A frase de ajuda também casa ("planilha" está na ajuda de Fontes de dados).
        Assert.Contains(Achados("planilha", itens), i => i.Titulo == "Fontes de dados");
    }

    [Fact]
    public void funcoes_trazem_os_comandos_das_faixas_e_os_modulos()
    {
        var funcoes = PesquisaGeral.Funcoes();
        // Um item por id: comandos repetidos em vários módulos (Abrir, Atalhos de teclado) entram uma vez.
        Assert.Equal(funcoes.Count, funcoes.Select(f => f.Chave).Distinct().Count());
        foreach (var m in Faixas.Disponiveis) Assert.Contains(funcoes, f => f.Tipo == TipoResultadoGeral.Modulo && f.Modulo == m);
        // Todo comando sem itens de menu de toda faixa tem item (os menus entram pelos itens; os dinâmicos, como Recentes, ficam de fora).
        var ids = funcoes.Where(f => f.Tipo == TipoResultadoGeral.Funcao).Select(f => (string)f.Dado!).ToHashSet();
        foreach (var m in Faixas.Disponiveis)
            foreach (var c in Faixas.Comandos(Faixas.Para(m)).Where(c => c.Tipo != TipoComando.Menu))
                Assert.Contains(c.Id, ids);
        Assert.DoesNotContain(ComandosApp.ArquivoRecentes, ids);
        // "Correção" do menu Novo despacho: o título mostra o caminho do menu.
        var correcao = funcoes.Single(f => (f.Dado as string) == ComandosApp.DespachosNovoCorrecao);
        Assert.Equal("Novo despacho › Correção", correcao.Titulo);
        Assert.StartsWith("Comando · ", correcao.Onde, StringComparison.Ordinal);
        // O atalho aparece no "onde".
        Assert.Contains("Ctrl+S", funcoes.Single(f => (f.Dado as string) == ComandosApp.ArquivoSalvar).Onde, StringComparison.Ordinal);
    }

    [Fact]
    public void modulo_acha_pelo_nome_e_por_sinonimo()
    {
        var itens = PesquisaGeral.Funcoes();
        var leg = Achados("legislacao", itens)[0];
        Assert.Equal(TipoResultadoGeral.Modulo, leg.Tipo);
        Assert.Equal(Modulo.Legislacao, leg.Modulo);
        Assert.Contains(Achados("configurações", itens), i => i.Tipo == TipoResultadoGeral.Modulo && i.Modulo == Modulo.Ajustes);
    }

    [Fact]
    public void processos_por_numero_ano_e_requerente()
    {
        var p1 = new Processo(@"C:\P\2026\15004 - 2026 - FULANO DE TAL", "15004 - 2026 - FULANO DE TAL", 15004, 2026, "FULANO DE TAL");
        var p2 = new Processo(@"C:\P\2026\0007 - 2026 - JOSÉ CICLANO", "0007 - 2026 - JOSÉ CICLANO", 7, 2026, "JOSÉ CICLANO");
        var itens = PesquisaGeral.DeProcessos([p1, p2]);
        Assert.Equal("Processo 15004/2026", itens[0].Onde);
        Assert.Same(p1, Achados("15004", itens).Single().Dado);
        Assert.Same(p1, Achados("15004/2026", itens).Single().Dado);
        Assert.Same(p2, Achados("jose", itens).Single().Dado);
        Assert.Same(p2, Achados("0007", itens).Single().Dado); // o número com zeros, como no nome da pasta
        Assert.Equal(2, Achados("2026", itens).Count);
    }

    [Fact]
    public void arquivos_relatorio_e_despacho()
    {
        var p = new Processo(@"C:\P\2026\15004 - 2026 - FULANO", "15004 - 2026 - FULANO", 15004, 2026, "FULANO");
        var etapa = Path.Combine(p.Pasta, "DESPACHO 01 - 2026-01-15");
        var itens = PesquisaGeral.DeArquivos(
        [
            (Path.Combine(etapa, "Relatorio_15004_2026.rpmvni"), p),
            (Path.Combine(etapa, "DESPACHO_CORRECAO_15004_2026.docx"), p),
        ]);
        Assert.Equal(TipoResultadoGeral.Relatorio, itens[0].Tipo);
        Assert.Equal("Relatório · Processo 15004/2026", itens[0].Onde);
        Assert.Equal(TipoResultadoGeral.Despacho, itens[1].Tipo);
        Assert.Equal("Despacho · Processo 15004/2026 · DESPACHO 01 - 2026-01-15", itens[1].Onde);
        var correcao = Achados("despacho correcao", itens);
        Assert.Equal(itens[1].Chave, correcao[0].Chave);
        // Os dois ficam no grupo Arquivos.
        Assert.Equal(["Arquivos"], PesquisaGeral.Pesquisar("15004", itens).Select(g => g.Nome));
    }

    [Fact]
    public void duplicados_ficam_com_o_primeiro()
    {
        var caminho = @"C:\P\2026\x\DESPACHO 01 - 2026-01-01\D.docx";
        var a = PesquisaGeral.DeArquivos([(caminho, null)]);
        var b = PesquisaGeral.DeArquivos([(caminho.ToLowerInvariant(), null)]);
        Assert.Single(Achados("docx", [.. a, .. b]));
    }

    [Fact]
    public void leis_trechos_e_planilha()
    {
        var lei = new Anexo("id1", "sha", "Código de Obras", Esfera.Municipal, "LEI 1234.pdf", 10, 3, [], DateTimeOffset.Now, null);
        var leis = PesquisaGeral.DeLeis([lei]);
        Assert.Equal("Lei · " + Esferas.Rotulo(Esfera.Municipal), leis[0].Onde);
        Assert.Equal("id1", Achados("codigo obras", leis).Single().Dado);
        Assert.Equal("id1", Achados("1234", leis).Single().Dado); // pelo nome do arquivo

        var trechos = PesquisaGeral.DeTrechos([new ResultadoPesquisa("id1", 2, "O afastamento mínimo é de 1,50 m.", 1)],
            new Dictionary<string, Anexo> { ["id1"] = lei }, "afastamento");
        var t = Assert.Single(Achados("qualquer coisa", trechos));
        Assert.Equal("Código de Obras · p. 2", t.Onde);
        Assert.Equal(new TrechoAchado(new ResultadoPesquisa("id1", 2, "O afastamento mínimo é de 1,50 m.", 1), "afastamento"), t.Dado);

        var planilha = PesquisaGeral.DeAcompanhamento([new ProcessoAcompanhado("19490/ 2026", "MARIA", "EM ANÁLISE", "19490/2026"), new ProcessoAcompanhado("", "", "", "")]);
        var linha = Assert.Single(planilha);
        Assert.Equal("Acompanhamento · EM ANÁLISE", linha.Onde);
        Assert.Equal("19490/2026", Achados("19490", planilha).Single().Dado);
        Assert.Single(Achados("em analise", planilha));
    }

    [Fact]
    public void limite_por_grupo_e_total_e_ordem_dos_grupos()
    {
        var muitos = Enumerable.Range(0, 50).Select(i =>
            new ItemPesquisaGeral(i % 2 == 0 ? TipoResultadoGeral.Ajuste : TipoResultadoGeral.Processo, $"Alfa {i}", "", "", "x" + i)).ToList();
        var grupos = PesquisaGeral.Pesquisar("alfa", muitos);
        Assert.All(grupos, g => Assert.True(g.Itens.Count <= PesquisaGeral.PorGrupo));
        Assert.True(grupos.Sum(g => g.Itens.Count) <= PesquisaGeral.Total);
        // Empate de pontos: a ordem fixa (Ajustes antes de Processos); com um igual ao termo, o grupo dele vem primeiro.
        Assert.Equal(["Ajustes", "Processos"], grupos.Select(g => g.Nome));
        muitos.Add(new ItemPesquisaGeral(TipoResultadoGeral.Processo, "Alfa", "", "", "igual"));
        Assert.Equal("Processos", PesquisaGeral.Pesquisar("alfa", muitos)[0].Nome);
        Assert.Equal("Alfa", PesquisaGeral.Pesquisar("alfa", muitos)[0].Itens[0].Titulo);
        Assert.Equal(30, PesquisaGeral.Pesquisar("alfa", muitos, porGrupo: 100).Sum(g => g.Itens.Count));
    }

    [Fact]
    public void cancelamento_interrompe()
    {
        var itens = Enumerable.Range(0, 2000).Select(i => new ItemPesquisaGeral(TipoResultadoGeral.Ajuste, $"Item {i}", "", "", "c" + i)).ToList();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => PesquisaGeral.Pesquisar("item", itens, cts.Token));
    }
}
