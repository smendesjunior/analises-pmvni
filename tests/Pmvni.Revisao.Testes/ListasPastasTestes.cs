using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Listas de Processos e Despachos (Tarefa 13): filtro, ordem, agrupamento e as linhas do detalhe, sobre tests/corpus/processos.</summary>
public class ListasPastasTestes
{
    static string Raiz => Ambiente.Corpus("processos");

    static IReadOnlyList<Processo> Processos() => Revisao.Processos.Listar(Raiz, CancellationToken.None).Itens;

    /// <summary>Datas de alteração inventadas: a ordem por data não pode depender do relógio do disco.</summary>
    static List<LinhaProcesso> Linhas() =>
        Processos().Select((p, i) => new LinhaProcesso(p, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i == 3 ? 40 : i == 1 ? 20 : i))).ToList();

    static string[] Pastas(IEnumerable<LinhaProcesso> l) => l.Select(x => x.Processo.NomePasta[..4]).ToArray();

    [Fact]
    public void processos_por_numero_data_e_requerente()
    {
        var linhas = Linhas();
        // Número: a ordem da leitura (ano e número do mais novo ao mais antigo; nome fora do padrão depois dos numerados do ano).
        Assert.Equal(new[] { "0012", "0001", "past", "0007" }, Pastas(ListasPastas.FiltrarProcessos(linhas, "", null, OrdemProcessos.Numero)));
        // Data: a pasta alterada mais recentemente primeiro (0007 = +40 dias, 0001 = +20, depois as outras).
        Assert.Equal(new[] { "0007", "0001", "past", "0012" }, Pastas(ListasPastas.FiltrarProcessos(linhas, "", null, OrdemProcessos.Data)));
        // Requerente: A-Z (BELTRANO, CICLANO, FULANO; o nome da pasta "pasta fora do padrao" entra como requerente).
        Assert.Equal(new[] { "0012", "0007", "0001", "past" }, Pastas(ListasPastas.FiltrarProcessos(linhas, "", null, OrdemProcessos.Requerente)));
    }

    [Fact]
    public void filtro_por_ano_e_por_termo()
    {
        var linhas = Linhas();
        Assert.Equal(new[] { "0012", "0001" }, Pastas(ListasPastas.FiltrarProcessos(linhas, "", 2099, OrdemProcessos.Numero)));
        Assert.Equal(new[] { "0007" }, Pastas(ListasPastas.FiltrarProcessos(linhas, "", 2098, OrdemProcessos.Numero)));
        Assert.Empty(ListasPastas.FiltrarProcessos(linhas, "", 2050, OrdemProcessos.Numero));
        // O termo é o de Processos.Filtrar (número ou requerente, sem acento) e se soma ao ano.
        Assert.Equal(new[] { "0001" }, Pastas(ListasPastas.FiltrarProcessos(linhas, "fulano", null, OrdemProcessos.Numero)));
        Assert.Equal(new[] { "0001" }, Pastas(ListasPastas.FiltrarProcessos(linhas, "fulano", 2099, OrdemProcessos.Numero)));
        Assert.Empty(ListasPastas.FiltrarProcessos(linhas, "fulano", 2098, OrdemProcessos.Numero));
        Assert.Equal(new[] { 2099, 2098 }, ListasPastas.Anos(Processos()));
    }

    [Fact]
    public void texto_da_linha_do_processo()
    {
        var linhas = Linhas();
        var primeiro = linhas[0];
        Assert.Equal("BELTRANO DE EXEMPLO (FICTICIO)", primeiro.Titulo);
        Assert.Equal("Processo 0012/2099", primeiro.Detalhe);
        var fora = linhas.Single(l => l.Processo.Numero is null);
        Assert.Equal("pasta fora do padrao", fora.Titulo);
        Assert.Equal("Nome da pasta fora do padrão", fora.Detalhe);
    }

    static IReadOnlyList<Despacho> Todos() => Despachos.Listar(Processos(), CancellationToken.None).Itens;

    static string[] Datas(IEnumerable<GrupoDespachos> g) => g.SelectMany(x => x.Linhas).Select(l => l.Despacho.Data.ToString("yyyy-MM-dd")).ToArray();

    [Fact]
    public void despachos_por_data_com_um_grupo_por_mes()
    {
        var g = ListasPastas.OrganizarDespachos(Todos(), FiltroTipoDespacho.Todos, "", AgrupamentoDespachos.Data, true, Processos());
        Assert.Equal(new[] { "março de 2099", "fevereiro de 2099", "janeiro de 2099", "março de 2098" }, g.Select(x => x.Titulo));
        Assert.Equal(new[] { "2099-03-05", "2099-02-20", "2099-01-15", "2098-03-02" }, Datas(g));

        var antigos = ListasPastas.OrganizarDespachos(Todos(), FiltroTipoDespacho.Todos, "", AgrupamentoDespachos.Data, false, Processos());
        Assert.Equal(new[] { "2098-03-02", "2099-01-15", "2099-02-20", "2099-03-05" }, Datas(antigos));
        Assert.Equal("março de 2098", antigos[0].Titulo);
    }

    [Fact]
    public void despachos_por_processo_na_ordem_dos_processos()
    {
        var g = ListasPastas.OrganizarDespachos(Todos(), FiltroTipoDespacho.Todos, "", AgrupamentoDespachos.Processo, true, Processos());
        Assert.Equal(new[] { "0012/2099 · BELTRANO DE EXEMPLO (FICTICIO)", "0001/2099 · FULANO DE EXEMPLO (FICTICIO)", "0007/2098 · CICLANO DE EXEMPLO (FICTICIO)" },
            g.Select(x => x.Titulo));
        Assert.Equal(new[] { 1, 2, 1 }, g.Select(x => x.Linhas.Count));
        // Dentro do processo 0001, o mais recente primeiro (aprovação de 20/02 antes da correção de 15/01).
        Assert.Equal(new[] { "2099-02-20", "2099-01-15" }, g[1].Linhas.Select(l => l.Despacho.Data.ToString("yyyy-MM-dd")));

        var antigos = ListasPastas.OrganizarDespachos(Todos(), FiltroTipoDespacho.Todos, "", AgrupamentoDespachos.Processo, false, Processos());
        Assert.Equal("0007/2098 · CICLANO DE EXEMPLO (FICTICIO)", antigos[0].Titulo);
        Assert.Equal(new[] { "2099-01-15", "2099-02-20" }, antigos[1].Linhas.Select(l => l.Despacho.Data.ToString("yyyy-MM-dd")));
    }

    [Fact]
    public void lista_achatada_tem_o_titulo_do_grupo_antes_dos_despachos_e_sem_grupo_vazio()
    {
        var g = ListasPastas.OrganizarDespachos(Todos(), FiltroTipoDespacho.Aprovacao, "", AgrupamentoDespachos.Processo, true, Processos());
        var itens = ListasPastas.Achatar(g);
        Assert.Equal(new[] { true, false, true, false }, itens.Select(i => i.EhGrupo)); // 0001 (1 aprovação) e 0007; o 0012 não tem aprovação
        Assert.Equal(new[] { "0001/2099 · FULANO DE EXEMPLO (FICTICIO)", "Despacho 02 · Aprovação", "0007/2098 · CICLANO DE EXEMPLO (FICTICIO)", "Despacho 01 · Aprovação" },
            itens.Select(i => i.Titulo));
        Assert.All(itens, i => Assert.NotEqual(i.EhGrupo, i.EhDespacho));
        Assert.Empty(ListasPastas.Achatar([new GrupoDespachos("vazio", [])]));
    }

    [Fact]
    public void despachos_filtrados_por_tipo_e_por_nome_de_arquivo()
    {
        List<Despacho> Plano(FiltroTipoDespacho t, string termo = "") =>
            ListasPastas.OrganizarDespachos(Todos(), t, termo, AgrupamentoDespachos.Data, true, Processos()).SelectMany(g => g.Linhas).Select(l => l.Despacho).ToList();

        Assert.Equal(4, Plano(FiltroTipoDespacho.Todos).Count);
        Assert.Equal(2, Plano(FiltroTipoDespacho.Aprovacao).Count);
        Assert.Equal(new[] { TipoDespacho.Correcao }, Plano(FiltroTipoDespacho.Correcao).Select(d => d.Tipo));
        // O tipo e o termo se somam: aprovação do 0001.
        Assert.Equal(new[] { new DateOnly(2099, 2, 20) }, Plano(FiltroTipoDespacho.Aprovacao, "0001").Select(d => d.Data));
        // O despacho de tipo não identificado só aparece em "Todos".
        Assert.DoesNotContain(Plano(FiltroTipoDespacho.Correcao), d => d.Tipo == TipoDespacho.NaoIdentificado);
        Assert.Contains(Plano(FiltroTipoDespacho.Todos), d => d.Tipo == TipoDespacho.NaoIdentificado);
    }

    [Fact]
    public void linha_do_despacho_abre_o_docx_antes_do_pdf()
    {
        var g = ListasPastas.OrganizarDespachos(Todos(), FiltroTipoDespacho.Todos, "", AgrupamentoDespachos.Data, true, Processos());
        var correcao = g.SelectMany(x => x.Linhas).Single(l => l.Despacho.Tipo == TipoDespacho.Correcao);
        Assert.Equal("Despacho 01 · Correção", correcao.Titulo);
        Assert.EndsWith("DESPACHO_CORRECAO_0001_2099.docx", correcao.ArquivoParaAbrir);
        Assert.EndsWith("DESPACHO_CORRECAO_0001_2099.pdf", correcao.ArquivoPdf);
        Assert.Contains("15/01/2099", correcao.Detalhe);
        Assert.Contains("Processo 0001/2099", correcao.Detalhe);

        var aprovacao = g.SelectMany(x => x.Linhas).First(l => l.Despacho.Tipo == TipoDespacho.Aprovacao);
        Assert.EndsWith(".pdf", aprovacao.ArquivoParaAbrir); // só PDF: abre o PDF
        Assert.Equal(aprovacao.ArquivoPdf, aprovacao.ArquivoParaAbrir);

        // O despacho do 0012 só tem DOCX: não há PDF para "Abrir PDF".
        var soDocx = g.SelectMany(x => x.Linhas).Single(l => l.Despacho.Tipo == TipoDespacho.NaoIdentificado);
        Assert.Null(soDocx.ArquivoPdf);
        Assert.EndsWith("OFICIO_0012.docx", soDocx.ArquivoParaAbrir);
    }

    [Fact]
    public void detalhe_do_processo_em_linhas()
    {
        var p = Processos().Single(x => x.Numero == 1);
        var d = Revisao.Processos.Detalhar(p, CancellationToken.None);

        var sem = ListasPastas.Detalhe(d, mostrarPastas: false);
        Assert.Equal(new[] { "Relatórios (1)", "dados_0001.json", "Despachos (2)" }, sem.Take(3).Select(l => l.Texto));
        Assert.Equal(TipoLinhaDetalhe.Analise, sem[1].Tipo);
        Assert.Equal("Análise da IA · REVISAO_2099-01-10 · " + d.Relatorios[0].AlteradoUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), sem[1].Info);
        Assert.Equal(new[]
        {
            "DESPACHO_APROVACAO_0001_2099.pdf", "DESPACHO_CORRECAO_0001_2099.docx", "DESPACHO_CORRECAO_0001_2099.pdf",
        }, sem.Where(l => l.Tipo == TipoLinhaDetalhe.ArquivoDeDespacho).Select(l => l.Texto).Order(StringComparer.Ordinal));
        Assert.All(sem.Where(l => l.Tipo == TipoLinhaDetalhe.ArquivoDeDespacho), l => { Assert.NotNull(l.Despacho); Assert.NotNull(l.Caminho); });
        Assert.DoesNotContain(sem, l => l.Texto.StartsWith("PROJETO", StringComparison.Ordinal));
        // Títulos e avisos de "nenhum" não são escolhíveis; relatórios e arquivos são.
        Assert.All(sem.Where(l => l.EhTitulo), l => Assert.False(l.Selecionavel));
        Assert.True(sem[1].Selecionavel);

        var com = ListasPastas.Detalhe(d, mostrarPastas: true);
        Assert.Contains(com, l => l.Texto == "PROJETO (2)" && l.EhTitulo);
        Assert.Contains(com, l => l.Texto == "DOCUMENTOS (1)" && l.EhTitulo);
        Assert.Contains(com, l => l.Texto == "ARQ 01-03.pdf" && l.Info == "0 B");
        Assert.Contains(com, l => l.Texto == "ART.pdf");
    }

    [Fact]
    public void detalhe_de_processo_sem_nada_diz_que_nao_ha()
    {
        var p = Processos().Single(x => x.Numero is null);
        var d = Revisao.Processos.Detalhar(p, CancellationToken.None);
        var linhas = ListasPastas.Detalhe(d, mostrarPastas: true);
        Assert.Contains(linhas, l => l.Tipo == TipoLinhaDetalhe.Vazio && l.Texto == "Nenhum relatório encontrado.");
        Assert.Contains(linhas, l => l.Tipo == TipoLinhaDetalhe.Vazio && l.Texto == "Nenhum despacho encontrado.");
        Assert.All(linhas.Where(l => l.Tipo == TipoLinhaDetalhe.Vazio), l => Assert.False(l.Selecionavel));
    }

    [Theory]
    [InlineData(0L, "0 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(1536L, "1,5 KB")]
    [InlineData(5L * 1024 * 1024, "5,0 MB")]
    public void tamanho_em_texto(long bytes, string esperado) => Assert.Equal(esperado, ListasPastas.TextoDoTamanho(bytes));
}
