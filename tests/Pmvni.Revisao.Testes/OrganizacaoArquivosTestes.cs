using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Página do processo (2026-11-16): agrupar os arquivos por etapa, tipo ou data (os grupos do Explorador) e ordenar por nome
/// (ordem natural), data ou tipo, crescente ou decrescente. Tudo em memória: as linhas são montadas aqui, com datas fixas.</summary>
public class OrganizacaoArquivosTestes
{
    const string Raiz = @"C:\P\2099\0001 - 2099 - FULANO";
    static readonly DateOnly Hoje = new(2026, 11, 16); // uma segunda-feira

    static DateTime Local(int ano, int mes, int dia, int hora = 12) => new(ano, mes, dia, hora, 0, 0, DateTimeKind.Local);

    /// <summary>Um arquivo de PROJETO ou DOCUMENTOS, como <see cref="ListasPastas.Detalhe"/> o entrega.</summary>
    static LinhaDetalhe Arq(string relativo, DateTime? local = null, long tamanho = 2048) =>
        new(TipoLinhaDetalhe.Arquivo, relativo, ListasPastas.TextoDoTamanho(tamanho), Path.Combine(Raiz, relativo))
        {
            AlteradoUtc = (local ?? Local(2026, 11, 16)).ToUniversalTime(), Tamanho = tamanho, Pasta = Path.GetDirectoryName(relativo) ?? "",
        };

    static LinhaDetalhe DoDespacho(int numero, DateOnly data, string nome)
    {
        var pasta = $"DESPACHO {numero:D2} - {data:yyyy-MM-dd}";
        var caminho = Path.Combine(Raiz, pasta, nome);
        var despacho = new Despacho(Path.Combine(Raiz, pasta), numero, data, TipoDespacho.Correcao, [caminho], null);
        return new(TipoLinhaDetalhe.ArquivoDeDespacho, nome, $"Despacho {numero:D2} · Correção · {data:dd/MM/yyyy}", caminho, despacho) { Pasta = pasta };
    }

    static string[] Nomes(IEnumerable<ItemArquivo> itens) => itens.Select(i => i.Texto).ToArray();

    static ExibicaoArquivos E(AgrupamentoArquivos g, OrdemArquivos o = OrdemArquivos.Nome, bool decrescente = false) => new(g, o, decrescente);

    [Fact]
    public void ordem_natural_poe_2_antes_de_10_sem_olhar_maiusculas_nem_acentos()
    {
        var nomes = new[] { "10.jpeg", "2.jpeg", "1.jpeg", "IMG_4681.JPEG", "img_4680.jpeg", "Prancha 12.pdf", "prancha 3.pdf", "Árvore.pdf", "arvore 2.pdf", "Zebra.pdf" };
        var ordenados = nomes.OrderBy(n => n, Comparer<string>.Create(OrganizacaoArquivos.CompararNatural)).ToArray();
        Assert.Equal(new[] { "1.jpeg", "2.jpeg", "10.jpeg", "arvore 2.pdf", "Árvore.pdf", "img_4680.jpeg", "IMG_4681.JPEG", "prancha 3.pdf", "Prancha 12.pdf", "Zebra.pdf" }, ordenados);

        Assert.True(OrganizacaoArquivos.CompararNatural("a2", "a10") < 0);
        Assert.True(OrganizacaoArquivos.CompararNatural("a10", "a2") > 0);
        Assert.Equal(0, OrganizacaoArquivos.CompararNatural("planta.pdf", "planta.pdf"));
        // Mesmo valor: menos zeros à esquerda primeiro; números enormes não estouram.
        Assert.True(OrganizacaoArquivos.CompararNatural("a1", "a01") < 0);
        Assert.True(OrganizacaoArquivos.CompararNatural("a99999999999999999999998", "a99999999999999999999999") < 0);
        Assert.True(OrganizacaoArquivos.CompararNatural("planta", "planta 2") < 0);
        Assert.True(OrganizacaoArquivos.CompararNatural(null, "a") < 0);
    }

    [Theory]
    [InlineData("2026-11-17", "Data futura")]
    [InlineData("2026-11-16", "Hoje")]
    [InlineData("2026-11-15", "Ontem")]                    // domingo: o primeiro dia desta semana
    [InlineData("2026-11-14", "Semana passada")]
    [InlineData("2026-11-08", "Semana passada")]
    [InlineData("2026-11-07", "Anteriormente neste mês")]
    [InlineData("2026-11-01", "Anteriormente neste mês")]
    [InlineData("2026-10-31", "Último mês")]
    [InlineData("2026-10-01", "Último mês")]
    [InlineData("2026-09-30", "Anteriormente neste ano")]
    [InlineData("2026-01-01", "Anteriormente neste ano")]
    [InlineData("2025-12-31", "Há muito tempo")]
    public void grupos_de_data_como_os_do_explorador(string data, string esperado) =>
        Assert.Equal(esperado, OrganizacaoArquivos.GrupoDeData(DateOnly.ParseExact(data, "yyyy-MM-dd"), Hoje));

    [Fact]
    public void grupos_de_data_no_meio_da_semana_e_na_virada_do_mes_e_do_ano()
    {
        var quinta = new DateOnly(2026, 11, 19);
        Assert.Equal("Ontem", OrganizacaoArquivos.GrupoDeData(new DateOnly(2026, 11, 18), quinta));
        Assert.Equal("No início desta semana", OrganizacaoArquivos.GrupoDeData(new DateOnly(2026, 11, 17), quinta));
        Assert.Equal("No início desta semana", OrganizacaoArquivos.GrupoDeData(new DateOnly(2026, 11, 15), quinta));
        Assert.Equal("Semana passada", OrganizacaoArquivos.GrupoDeData(new DateOnly(2026, 11, 14), quinta));
        // A semana passada que caiu no mês (e no ano) anterior continua sendo "Semana passada".
        var virada = new DateOnly(2027, 1, 4); // segunda-feira
        Assert.Equal("Semana passada", OrganizacaoArquivos.GrupoDeData(new DateOnly(2026, 12, 30), virada));
        Assert.Equal("Último mês", OrganizacaoArquivos.GrupoDeData(new DateOnly(2026, 12, 26), virada));
        Assert.Equal("Há muito tempo", OrganizacaoArquivos.GrupoDeData(new DateOnly(2026, 11, 30), virada));
        Assert.Equal(OrganizacaoArquivos.SemData, OrganizacaoArquivos.GrupoDeData(null, Hoje));
        Assert.All(new[] { "Hoje", "Ontem", "Semana passada", "Último mês", OrganizacaoArquivos.SemData }, g => Assert.Contains(g, OrganizacaoArquivos.GruposDeData));
    }

    [Theory]
    [InlineData("", "Pasta do processo")]
    [InlineData("PROJETO", "PROJETO")]
    [InlineData(@"PROJETO\INICIAL", "INICIAL")]
    [InlineData(@"PROJETO\REVISÃO 01\fotos", @"REVISÃO 01\fotos")]
    [InlineData("DOCUMENTOS", "DOCUMENTOS")]
    [InlineData(@"DOCUMENTOS\ART", @"DOCUMENTOS\ART")]
    [InlineData("DESPACHO 01 - 2026-08-18", "Despacho 01 · 18/08/2026")]
    [InlineData("despacho 12 - 2026-11-03", "Despacho 12 · 03/11/2026")]
    [InlineData(@".pmvni-ai\REVISAO_2099-01-10", "Análises da IA")]
    public void titulo_da_etapa_ou_pasta(string pasta, string esperado) => Assert.Equal(esperado, OrganizacaoArquivos.RotuloDaPasta(pasta));

    static List<LinhaDetalhe> Projeto() =>
    [
        Arq(@"PROJETO\REVISÃO 01\10.jpeg", Local(2026, 11, 15)),
        Arq(@"PROJETO\REVISÃO 01\2.jpeg", Local(2026, 11, 16)),
        Arq(@"PROJETO\REVISÃO 01\PROCESSO 18031-2026.pdf", Local(2026, 10, 20), 370 * 1024),
        Arq(@"PROJETO\INICIAL\IMG_4681.JPEG", Local(2026, 9, 2)),
        Arq(@"PROJETO\INICIAL\IMG_4680.JPEG", Local(2026, 9, 1)),
        Arq(@"PROJETO\FINAL\PROCESSO 18031-2026.pdf", Local(2026, 11, 10)),
        Arq(@"PROJETO\FINAL\planta.dwg", Local(2025, 3, 4)),
        Arq(@"DOCUMENTOS\ART.pdf", Local(2026, 11, 2)),
        Arq(@"DOCUMENTOS\memorial.docx", Local(2026, 11, 2, 9)),
    ];

    [Fact]
    public void por_etapa_cada_pasta_e_um_grupo_e_o_nome_fica_sem_a_pasta()
    {
        var g = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Etapa), Hoje);
        Assert.Equal(new[] { "FINAL", "INICIAL", "REVISÃO 01", "DOCUMENTOS" }, g.Select(x => x.Titulo));
        Assert.Equal(new[] { "planta.dwg", "PROCESSO 18031-2026.pdf" }, Nomes(g[0].Itens));
        Assert.Equal(new[] { "IMG_4680.JPEG", "IMG_4681.JPEG" }, Nomes(g[1].Itens));
        Assert.Equal(new[] { "2.jpeg", "10.jpeg", "PROCESSO 18031-2026.pdf" }, Nomes(g[2].Itens)); // 2 antes de 10
        // O grupo já diz a pasta: a segunda linha traz só o tamanho e a data.
        Assert.Equal("370,0 KB · 20/10/2026", g[2].Itens[2].Info);
        Assert.Equal("2,0 KB · 01/09/2026", g[1].Itens[0].Info);
        Assert.All(g.SelectMany(x => x.Itens), i => { Assert.True(i.Selecionavel); Assert.NotNull(i.Linha); Assert.False(i.EhTitulo); });

        var decrescente = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Etapa, OrdemArquivos.Nome, true), Hoje);
        Assert.Equal(new[] { "FINAL", "INICIAL", "REVISÃO 01", "DOCUMENTOS" }, decrescente.Select(x => x.Titulo)); // as etapas não invertem
        Assert.Equal(new[] { "PROCESSO 18031-2026.pdf", "10.jpeg", "2.jpeg" }, Nomes(decrescente[2].Itens));
    }

    [Fact]
    public void por_etapa_os_despachos_mais_novos_vem_antes_das_pastas()
    {
        var linhas = Projeto();
        linhas.Add(DoDespacho(1, new DateOnly(2026, 8, 18), "Despacho_Correcao.docx"));
        linhas.Add(DoDespacho(2, new DateOnly(2026, 10, 5), "Despacho_Aprovacao.pdf"));
        linhas.Add(new(TipoLinhaDetalhe.Relatorio, "Relatorio_0001_2099.rpmvni", "Relatório em revisão", Path.Combine(Raiz, "Relatorio_0001_2099.rpmvni"))
            { AlteradoUtc = Local(2026, 11, 16).ToUniversalTime() });
        linhas.Add(new(TipoLinhaDetalhe.Analise, "dados.json", "Análise da IA", Path.Combine(Raiz, ".pmvni-ai", "REV", "dados.json"))
            { AlteradoUtc = Local(2026, 11, 1).ToUniversalTime(), Pasta = @".pmvni-ai\REV" });
        // Títulos e avisos do detalhe não são arquivos: ficam de fora.
        linhas.Add(new(TipoLinhaDetalhe.Titulo, "PROJETO (7)"));
        linhas.Add(new(TipoLinhaDetalhe.Vazio, "Nenhum relatório encontrado."));

        var g = OrganizacaoArquivos.Organizar(linhas, E(AgrupamentoArquivos.Etapa), Hoje);
        Assert.Equal(new[] { "Despacho 02 · 05/10/2026", "Despacho 01 · 18/08/2026", "FINAL", "INICIAL", "REVISÃO 01", "DOCUMENTOS", "Análises da IA", "Pasta do processo" },
            g.Select(x => x.Titulo));
        Assert.Equal(13, g.Sum(x => x.Itens.Count));
        // Relatórios e despachos mantêm a informação da leitura.
        Assert.Equal("Despacho 02 · Correção · 05/10/2026", g[0].Itens[0].Info);
    }

    [Fact]
    public void por_tipo_junta_pdf_imagens_dwg_e_word()
    {
        var g = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Tipo), Hoje);
        Assert.Equal(new[] { "DWG e CAD", "Imagens", "PDF", "Word" }, g.Select(x => x.Titulo));
        Assert.Equal(new[] { "2.jpeg", "10.jpeg", "IMG_4680.JPEG", "IMG_4681.JPEG" }, Nomes(g[1].Itens));
        Assert.Equal(new[] { "ART.pdf", "PROCESSO 18031-2026.pdf", "PROCESSO 18031-2026.pdf" }, Nomes(g[2].Itens));
        // Fora do grupo por etapa, a segunda linha diz a pasta.
        Assert.Equal("REVISÃO 01 · 2,0 KB · 16/11/2026", g[1].Itens[0].Info);
        Assert.Equal("DOCUMENTOS · 2,0 KB · 02/11/2026", g[2].Itens[0].Info);

        // Classificar por tipo, decrescente: os grupos invertem junto.
        var z = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Tipo, OrdemArquivos.Tipo, true), Hoje);
        Assert.Equal(new[] { "Word", "PDF", "Imagens", "DWG e CAD" }, z.Select(x => x.Titulo));

        // Extensão desconhecida tem o próprio grupo; sem extensão vai para "Outros", no fim.
        var outros = OrganizacaoArquivos.Organizar([Arq(@"PROJETO\LEIAME"), Arq(@"PROJETO\a.xyz"), Arq(@"PROJETO\b.pdf")], E(AgrupamentoArquivos.Tipo), Hoje);
        Assert.Equal(new[] { "Arquivos XYZ", "PDF", "Outros" }, outros.Select(x => x.Titulo));
    }

    [Fact]
    public void por_data_usa_os_grupos_do_explorador_do_mais_novo_ao_mais_antigo()
    {
        var g = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Data), Hoje);
        Assert.Equal(new[] { "Hoje", "Ontem", "Semana passada", "Anteriormente neste mês", "Último mês", "Anteriormente neste ano", "Há muito tempo" },
            g.Select(x => x.Titulo));
        Assert.Equal(new[] { "2.jpeg" }, Nomes(g[0].Itens));
        Assert.Equal(new[] { "ART.pdf", "memorial.docx" }, Nomes(g[3].Itens));
        Assert.Equal(new[] { "IMG_4680.JPEG", "IMG_4681.JPEG" }, Nomes(g[5].Itens));

        // Classificar por data, crescente: os grupos e os arquivos do mais antigo ao mais novo.
        var antigos = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Data, OrdemArquivos.Data), Hoje);
        Assert.Equal("Há muito tempo", antigos[0].Titulo);
        Assert.Equal("Hoje", antigos[^1].Titulo);
        Assert.Equal(new[] { "memorial.docx", "ART.pdf" }, Nomes(antigos.Single(x => x.Titulo == "Anteriormente neste mês").Itens)); // 9h antes de 12h

        var recentes = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Data, OrdemArquivos.Data, true), Hoje);
        Assert.Equal("Hoje", recentes[0].Titulo);
        Assert.Equal(new[] { "ART.pdf", "memorial.docx" }, Nomes(recentes.Single(x => x.Titulo == "Anteriormente neste mês").Itens));
    }

    [Fact]
    public void arquivo_de_despacho_usa_a_data_do_despacho_e_sem_data_fica_no_fim()
    {
        var semData = new LinhaDetalhe(TipoLinhaDetalhe.Arquivo, "solto.pdf", "", Path.Combine(Raiz, "PROJETO", "solto.pdf")) { Pasta = "PROJETO" };
        var linhas = new List<LinhaDetalhe> { semData, DoDespacho(1, new DateOnly(2026, 11, 16), "a.pdf"), Arq(@"PROJETO\b.pdf", Local(2026, 11, 10)) };
        Assert.Equal(new DateTime(2026, 11, 16), OrganizacaoArquivos.Quando(linhas[1]));
        Assert.Null(OrganizacaoArquivos.Quando(semData));

        foreach (var decrescente in new[] { false, true })
        {
            var g = OrganizacaoArquivos.Organizar(linhas, E(AgrupamentoArquivos.Data, OrdemArquivos.Data, decrescente), Hoje);
            Assert.Equal(OrganizacaoArquivos.SemData, g[^1].Titulo);
            var plano = OrganizacaoArquivos.Ordenar(linhas, E(AgrupamentoArquivos.Nenhum, OrdemArquivos.Data, decrescente));
            Assert.Same(semData, plano[^1]);
        }
        Assert.Equal("PROJETO", OrganizacaoArquivos.Info(semData, AgrupamentoArquivos.Nenhum));
        Assert.Equal("", OrganizacaoArquivos.Info(semData, AgrupamentoArquivos.Etapa));
    }

    [Fact]
    public void sem_grupo_e_um_bloco_so_ordenado_por_nome_data_ou_tipo()
    {
        var g = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Nenhum), Hoje);
        Assert.Equal("", Assert.Single(g).Titulo);
        Assert.Equal(new[] { "2.jpeg", "10.jpeg", "ART.pdf", "IMG_4680.JPEG", "IMG_4681.JPEG", "memorial.docx", "planta.dwg", "PROCESSO 18031-2026.pdf", "PROCESSO 18031-2026.pdf" },
            Nomes(g[0].Itens));

        var porData = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Nenhum, OrdemArquivos.Data, true), Hoje)[0].Itens;
        Assert.Equal(new[] { "2.jpeg", "10.jpeg", "PROCESSO 18031-2026.pdf" }, Nomes(porData.Take(3)));
        Assert.Equal("planta.dwg", porData[^1].Texto);

        var porTipo = OrganizacaoArquivos.Organizar(Projeto(), E(AgrupamentoArquivos.Nenhum, OrdemArquivos.Tipo), Hoje)[0].Itens;
        Assert.Equal(new[] { "planta.dwg", "2.jpeg", "10.jpeg", "IMG_4680.JPEG", "IMG_4681.JPEG", "ART.pdf", "PROCESSO 18031-2026.pdf", "PROCESSO 18031-2026.pdf", "memorial.docx" },
            Nomes(porTipo));
        Assert.Empty(OrganizacaoArquivos.Organizar([], ExibicaoArquivos.Padrao, Hoje));
    }

    [Fact]
    public void modo_lista_reparte_em_tres_colunas_com_o_titulo_de_cada_grupo()
    {
        var linhas = Projeto();
        linhas.Add(DoDespacho(1, new DateOnly(2026, 8, 18), "Despacho_Correcao.docx"));
        linhas.Add(DoDespacho(1, new DateOnly(2026, 8, 18), "RELATORIO_TECNICO.pdf"));
        var (rel, desp, proj) = OrganizacaoArquivos.EmColunas(linhas, ExibicaoArquivos.Padrao, Hoje);
        Assert.Equal(new[] { "Despacho 01 · 18/08/2026", "RELATORIO_TECNICO.pdf" }, Nomes(rel));
        Assert.Equal(new[] { "Despacho 01 · 18/08/2026", "Despacho_Correcao.docx" }, Nomes(desp));
        Assert.Equal(new[] { true, false }, rel.Select(i => i.EhTitulo));
        Assert.Equal("Grupo Despacho 01 · 18/08/2026", rel[0].NomeAcessivel);
        Assert.False(rel[0].Selecionavel);
        Assert.Equal("RELATORIO_TECNICO.pdf, Despacho 01 · Correção · 18/08/2026", rel[1].NomeAcessivel);
        Assert.Equal(rel[1].NomeAcessivel, rel[1].ToString());
        Assert.Equal(4 + 9, proj.Count); // 4 títulos de pasta e os 9 arquivos
        Assert.Equal("FINAL", proj[0].Texto);

        // Sem agrupamento não há título; coluna sem arquivo diz que não há (e o aviso não se escolhe).
        var (r2, d2, p2) = OrganizacaoArquivos.EmColunas(Projeto(), E(AgrupamentoArquivos.Nenhum), Hoje);
        Assert.Equal("Nenhum relatório.", Assert.Single(r2).Texto);
        Assert.False(r2[0].Selecionavel);
        Assert.False(r2[0].EhTitulo);
        Assert.Equal("Nenhum despacho.", Assert.Single(d2).Texto);
        Assert.Equal(9, p2.Count);
        Assert.DoesNotContain(p2, i => i.EhTitulo);
        Assert.Equal("Nenhum arquivo nas pastas PROJETO e DOCUMENTOS.", OrganizacaoArquivos.EmColunas([], ExibicaoArquivos.Padrao, Hoje).Projeto[0].Texto);
    }

    [Fact]
    public void o_detalhe_lido_da_pasta_traz_a_etapa_a_data_e_o_tamanho()
    {
        var p = Revisao.Processos.Listar(Ambiente.Corpus("processos"), CancellationToken.None).Itens.Single(x => x.Numero == 1);
        var linhas = ListasPastas.Detalhe(Revisao.Processos.Detalhar(p, CancellationToken.None), mostrarPastas: true);
        var arq = linhas.Single(l => l.Texto == "ARQ 01-03.pdf");
        Assert.Equal("PROJETO", arq.Pasta);
        Assert.Equal(0, arq.Tamanho);
        Assert.NotNull(arq.AlteradoUtc);
        Assert.Equal(@".pmvni-ai\REVISAO_2099-01-10", linhas.Single(l => l.Tipo == TipoLinhaDetalhe.Analise).Pasta);
        Assert.Equal("DESPACHO 02 - 2099-02-20", linhas.Single(l => l.Texto == "DESPACHO_APROVACAO_0001_2099.pdf").Pasta);

        var (rel, desp, proj) = OrganizacaoArquivos.EmColunas(linhas, ExibicaoArquivos.Padrao, Hoje);
        Assert.Equal(new[] { "Análises da IA", "dados_0001.json" }, Nomes(rel));
        Assert.Equal(new[] { "Despacho 02 · 20/02/2099", "DESPACHO_APROVACAO_0001_2099.pdf", "Despacho 01 · 15/01/2099", "DESPACHO_CORRECAO_0001_2099.docx", "DESPACHO_CORRECAO_0001_2099.pdf" },
            Nomes(desp));
        Assert.Equal(new[] { "PROJETO", "ARQ 01-03.pdf", "script.cmd", "DOCUMENTOS", "ART.pdf" }, Nomes(proj));
        // Cada item aponta para a linha do detalhe (é ela que a página escolhe).
        Assert.All(proj.Where(i => i.Selecionavel), i => Assert.Contains(i.Linha, linhas));
        Assert.Equal("", ListasPastas.PastaRelativa(p.Pasta, Path.Combine(p.Pasta, "solto.rpmvni")));
        Assert.Equal("", ListasPastas.PastaRelativa(p.Pasta, @"Z:\outro\lugar\x.pdf"));
    }

    [Fact]
    public void a_escolha_vai_e_volta_pelos_textos_dos_ajustes()
    {
        Assert.Equal(new ExibicaoArquivos(AgrupamentoArquivos.Etapa, OrdemArquivos.Nome, false), ExibicaoArquivos.Padrao);
        foreach (var agrupar in Ajustes.OpcoesArquivosAgrupar)
            foreach (var ordenar in Ajustes.OpcoesArquivosOrdenar)
            {
                var e = ExibicaoArquivos.DosAjustes(agrupar, ordenar, true);
                Assert.Equal(agrupar, e.AgruparNosAjustes);
                Assert.Equal(ordenar, e.OrdenarNosAjustes);
                Assert.True(e.Decrescente);
            }
        Assert.Equal(ExibicaoArquivos.Padrao, ExibicaoArquivos.DosAjustes("cor", null, false));
        Assert.Equal(4, Ajustes.OpcoesArquivosAgrupar.Select(a => ExibicaoArquivos.DosAjustes(a, "nome", false).Agrupar).Distinct().Count());
        Assert.Equal(3, Ajustes.OpcoesArquivosOrdenar.Select(o => ExibicaoArquivos.DosAjustes("etapa", o, false).Ordenar).Distinct().Count());
    }

    [Theory]
    [InlineData(@"C:\P\a.PDF", "PDF")]
    [InlineData(@"C:\P\IMG_4680.JPEG", "Imagens")]
    [InlineData(@"C:\P\foto.heic", "Imagens")]
    [InlineData(@"C:\P\planta.dwg", "DWG e CAD")]
    [InlineData(@"C:\P\despacho.docx", "Word")]
    [InlineData(@"C:\P\quadro.xlsx", "Excel")]
    [InlineData(@"C:\P\Relatorio_0001_2099.rpmvni", "Relatórios do editor")]
    [InlineData(@"C:\P\x.abc", "Arquivos ABC")]
    [InlineData(@"C:\P\LEIAME", "Outros")]
    public void tipo_de_arquivo_como_grupo(string caminho, string esperado) =>
        Assert.Equal(esperado, TiposArquivo.Categoria(TiposArquivo.Chave(caminho)));
}
