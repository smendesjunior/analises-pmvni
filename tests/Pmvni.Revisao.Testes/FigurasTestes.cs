using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Pmvni.Composicao.Figuras;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Editor de figuras (Etapa 4): camada de marcações, comandos com histórico, renumeração e o dados.json efetivo.</summary>
public class FigurasTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar(string caso)
    {
        var caminho = Ambiente.Corpus(caso + ".json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    private static SessaoRevisao Sessao(ConteudoPacote c) => new(c.Projeto, c.Historico, Ambiente.Contexto());

    private static JsonArray FigurasEfetivas(Projeto p) => (JsonArray)JsonFiel.Ler(DadosEfetivos.Gerar(p).Json)["figuras"]!;

    private static string PrimeiraFigura(SessaoRevisao s) => CamadaFiguras.Inicial(s.Projeto)[0].Id;

    [Fact]
    public void exportacao_distingue_marcacoes_manuais_sem_vinculo_das_notas_do_kit()
    {
        var c = Importar("longo");
        var s = Sessao(c);
        var id = PrimeiraFigura(s);
        s.Aplicar(new IncluirMarcacao(id, "regiao", TiposMarcacao.Regiao, Caixa: [.1, .2, .5, .6], Elemento: "Detalhe da prancha"));
        s.Aplicar(new IncluirMarcacao(id, "seta", TiposMarcacao.Seta, Alvo: [.4, .5], Elemento: "Segundo ponto"));
        using var pasta = Ambiente.PastaDeTeste("marcas-manuais");
        c.ExtrairAssets(pasta);
        var caminho = Path.Combine(pasta, "dados.json");
        var dados = JsonFiel.Ler(DadosEfetivos.Gerar(s.Projeto).Json);
        var notas = dados["figuras"]![0]!["notas"]!.AsArray();
        Assert.Equal("Marcação manual", notas[2]!["itens"]?.ToString());
        Assert.Equal("Marcação manual", notas[3]!["itens"]?.ToString());
        var impresso = RelatorioRenderer.Renderizar(JsonFiel.Escrever(dados), caminho, new(false, false), Modelo);
        Assert.True(impresso.Ok, string.Join(", ", impresso.Erros));
        Assert.Contains("(Marcação manual)", impresso.Html);
        notas[0]!.AsObject().Remove("itens");
        Assert.Contains(RelatorioRenderer.Renderizar(JsonFiel.Escrever(dados), caminho, new(false, false), Modelo).Erros,
            erro => erro.Contains("figuras[1].notas[1].itens", StringComparison.Ordinal));
    }

    [Fact]
    public void ocultar_nota_remove_legenda_e_marcacao_do_html_impresso()
    {
        var c = Importar("longo");
        var s = Sessao(c);
        var id = PrimeiraFigura(s);
        s.Aplicar(new IncluirMarcacao(id, "detalhe", TiposMarcacao.Seta, Alvo: [.2, .3], Elemento: "Marca para ocultar"));
        using var pasta = Ambiente.PastaDeTeste("ocultar-nota");
        c.ExtrairAssets(pasta);
        var caminho = Path.Combine(pasta, "dados.json");
        string Html() => RelatorioRenderer.Renderizar(DadosEfetivos.Gerar(s.Projeto, true).Json, caminho, new(false, false), Modelo).Html;
        var antes = Html();
        Assert.Contains("Marca para ocultar", antes);
        Assert.Contains("data:image/svg+xml;base64,", antes);
        s.Aplicar(new DefinirVisibilidadeDoCampo(id, "marcacao/detalhe", false));
        var depois = Html();
        Assert.DoesNotContain("Marca para ocultar", depois);
        var figura = depois.Split("<div class=\"fig\">")[1].Split("</div>")[0];
        Assert.DoesNotContain("data:image/svg+xml", figura);
        s.Desfazer();
        Assert.Equal(antes, Html());
    }

    [Fact]
    public void nota_original_oculta_o_desenho_correto_com_marcas_fora_de_ordem()
    {
        var s = Sessao(Importar("longo"));
        var figura = s.Projeto.Documento["figuras"]![0]!;
        figura["marcacoes"] = JsonNode.Parse("""[{"tipo":"seta","numero":2},{"tipo":"texto","texto":"livre"},{"tipo":"seta","numero":1}]""");
        var camada = CamadaFiguras.Inicial(s.Projeto)[0];
        s.Aplicar(new DefinirVisibilidadeDoCampo(camada.Id, "marcacao/" + camada.Marcacoes[0].Id, false));
        var caminhos = JsonFiel.Ler(DadosEfetivos.Gerar(s.Projeto).Json)["_campos_ocultos"]!.AsArray();
        Assert.Equal(new[] { "figuras/0/marcacoes/2", "figuras/0/notas/0" }, caminhos.Select(x => x!.ToString()));
    }

    [Fact]
    public void sem_edicao_de_figura_o_dados_efetivo_nao_muda()
    {
        var c = Importar("longo");
        var antes = DadosEfetivos.Gerar(c.Projeto).Json;
        // Sem camada e sem exclusões: as notas saem como vieram (o "(item NN)" impresso é o mesmo).
        Assert.Null(c.Projeto.Figuras);
        var recebidas = (JsonArray)JsonFiel.Ler(c.Recebido)["figuras"]!;
        Assert.True(JsonNode.DeepEquals(recebidas[0]!["notas"], FigurasEfetivas(c.Projeto)[0]!["notas"]));
        Assert.Equal(antes, DadosEfetivos.Gerar(c.Projeto).Json);
    }

    [Fact]
    public void legenda_item_acompanha_a_renumeracao_mesmo_sem_camada()
    {
        var s = Sessao(Importar("longo"));
        // Figura 1 cita "item 01" e "item 02"; a figura 2, "item 02".."item 04". Excluir o item 01 renumera os seguintes.
        s.Aplicar(new Excluir(s.Projeto.Incluidos[0].Id));
        var figs = FigurasEfetivas(s.Projeto);
        Assert.Equal("item 01", figs[1]!["notas"]![0]!["itens"]!.GetValue<string>()); // era o 02
        Assert.Equal("item 03", figs[1]!["notas"]![2]!["itens"]!.GetValue<string>()); // era o 04
        Assert.Null(s.Projeto.Figuras);
    }

    [Fact]
    public void figura_do_kit_vira_camada_com_marcacoes_gravadas()
    {
        var c = Importar("longo");
        var camada = CamadaFiguras.Inicial(c.Projeto);
        Assert.Equal(8, camada.Count);
        Assert.Equal("fig-01", camada[0].Id); // o id do kit é mantido
        Assert.All(camada[0].Marcacoes, m => Assert.True(m.Gravada));
        Assert.Equal(2, CamadaFiguras.Numeradas(camada[0]).Count);
        Assert.Contains("{{ref:", camada[0].Marcacoes[0].Itens);
    }

    [Fact]
    public void incluir_mover_e_excluir_marcacao_com_desfazer_e_refazer()
    {
        var s = Sessao(Importar("longo"));
        var fig = PrimeiraFigura(s);
        var original = DadosEfetivos.Gerar(s.Projeto).Json;

        var r = s.Aplicar(new IncluirMarcacao(fig, "mk_a", TiposMarcacao.Regiao, Caixa: [.2, .3, .4, .5], Elemento: "Janela J3"));
        Assert.NotNull(r);
        Assert.Equal(fig, r!.Item);
        Assert.Equal("mk_a", r.Campo);
        Assert.Null(r.Antes); // a camada não existia
        var f = s.Projeto.Figuras!.Single(x => x.Id == fig);
        Assert.Equal(3, CamadaFiguras.Numero(f, "mk_a")); // depois das 2 gravadas

        var efetiva = FigurasEfetivas(s.Projeto)[0]!;
        Assert.Equal(3, efetiva["notas"]!.AsArray().Count);
        Assert.Equal("Janela J3", efetiva["notas"]![2]!["elemento"]!.GetValue<string>());
        Assert.Single(efetiva["marcacoes"]!.AsArray());
        Assert.Equal(3, efetiva["marcacoes"]![0]!["numero"]!.GetValue<int>());

        s.Aplicar(new RedimensionarMarcacao(fig, "mk_a", [.1, .1, .6, .7]));
        Assert.Equal(new[] { .1, .1, .6, .7 }, s.Projeto.Figuras!.Single(x => x.Id == fig).Buscar("mk_a")!.Caixa!);

        s.Aplicar(new ExcluirMarcacao(fig, "mk_a"));
        Assert.Null(s.Projeto.Figuras!.Single(x => x.Id == fig).Buscar("mk_a"));

        // Desfazer tudo volta ao relatório do kit, byte a byte.
        s.Desfazer(); s.Desfazer(); s.Desfazer();
        Assert.Null(s.Projeto.Figuras);
        Assert.Equal(original, DadosEfetivos.Gerar(s.Projeto).Json);
        s.Refazer();
        Assert.NotNull(s.Projeto.Figuras!.Single(x => x.Id == fig).Buscar("mk_a"));
    }

    [Fact]
    public void numero_visivel_segue_a_ordem_do_kit_e_se_refaz_ao_excluir()
    {
        var s = Sessao(Importar("longo"));
        var fig = "figura-nova";
        s.Aplicar(new IncluirFigura(fig, ExistenteImagem(s), "ARQ 02/03", "Recorte", "ARQ 02/03, planta."));
        s.Aplicar(new IncluirMarcacao(fig, "mk_baixo", TiposMarcacao.Seta, Alvo: [.2, .8]));
        s.Aplicar(new IncluirMarcacao(fig, "mk_cima", TiposMarcacao.Seta, Alvo: [.3, .1]));
        s.Aplicar(new IncluirMarcacao(fig, "mk_dir", TiposMarcacao.Seta, Alvo: [.9, .05]));
        var f = s.Projeto.Figuras!.Single(x => x.Id == fig);
        // margem esquerda de cima para baixo, depois a direita
        Assert.Equal(1, CamadaFiguras.Numero(f, "mk_cima"));
        Assert.Equal(2, CamadaFiguras.Numero(f, "mk_baixo"));
        Assert.Equal(3, CamadaFiguras.Numero(f, "mk_dir"));

        s.Aplicar(new ExcluirMarcacao(fig, "mk_cima"));
        f = s.Projeto.Figuras!.Single(x => x.Id == fig);
        Assert.Equal(1, CamadaFiguras.Numero(f, "mk_baixo"));
        Assert.Equal(2, CamadaFiguras.Numero(f, "mk_dir"));

        // Mover o número para cima reordena.
        s.Aplicar(new MoverMarcacao(fig, "mk_dir", Lado: "esquerda", Altura: .01));
        f = s.Projeto.Figuras!.Single(x => x.Id == fig);
        Assert.Equal(1, CamadaFiguras.Numero(f, "mk_dir"));
    }

    private static string ExistenteImagem(SessaoRevisao s) =>
        s.Projeto.Documento["figuras"]![0]!["imagem"]!.GetValue<string>();

    [Fact]
    public void vinculo_ao_item_acompanha_a_renumeracao()
    {
        var s = Sessao(Importar("longo"));
        var fig = PrimeiraFigura(s);
        var item5 = s.Projeto.Incluidos[4].Id;
        var item7 = s.Projeto.Incluidos[6].Id;
        s.Aplicar(new IncluirMarcacao(fig, "mk_v", TiposMarcacao.Seta, Alvo: [.5, .5], Elemento: "Porta P3"));
        s.Aplicar(new VincularItens(fig, "mk_v", [item7, item5]));
        Assert.Equal("itens 05 e 07", FigurasEfetivas(s.Projeto)[0]!["notas"]![2]!["itens"]!.GetValue<string>());

        s.Aplicar(new Excluir(s.Projeto.Incluidos[0].Id));
        Assert.Equal("itens 04 e 06", FigurasEfetivas(s.Projeto)[0]!["notas"]![2]!["itens"]!.GetValue<string>());
    }

    [Fact]
    public void marcacao_gravada_nao_sai_nem_se_move_mas_a_legenda_edita()
    {
        var s = Sessao(Importar("longo"));
        var fig = PrimeiraFigura(s);
        var gravada = CamadaFiguras.Inicial(s.Projeto)[0].Marcacoes[0].Id;
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new ExcluirMarcacao(fig, gravada)));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new MoverMarcacao(fig, gravada, Altura: .2)));
        Assert.Null(s.Projeto.Figuras); // recusado: nada mudou
        s.Aplicar(new DefinirLegenda(fig, gravada, "Janela J3", "Informar a área de vidro."));
        var nota = FigurasEfetivas(s.Projeto)[0]!["notas"]![0]!;
        Assert.Equal("Janela J3", nota["elemento"]!.GetValue<string>());
        Assert.Equal("Informar a área de vidro.", nota["texto"]!.GetValue<string>());
        Assert.Null(FigurasEfetivas(s.Projeto)[0]!["marcacoes"]); // só gravadas: a imagem continua a do kit
    }

    [Fact]
    public void no_maximo_seis_numeros_por_figura()
    {
        var s = Sessao(Importar("extremos")); // a figura já tem 6 notas
        var fig = PrimeiraFigura(s);
        var e = Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new IncluirMarcacao(fig, "mk_7", TiposMarcacao.Seta, Alvo: [.5, .5])));
        Assert.Contains("6", e.Message);
        // texto livre não tem número: entra
        Assert.NotNull(s.Aplicar(new IncluirMarcacao(fig, "mk_t", TiposMarcacao.Texto, Posicao: [.5, .5], Texto: "ver cota")));
    }

    [Fact]
    public void vagas_de_numero_contam_as_gravadas_e_as_novas()
    {
        var s = Sessao(Importar("extremos")); // 6 notas gravadas: nenhuma vaga
        Assert.Equal(0, CamadaFiguras.VagasNumeradas(CamadaFiguras.Inicial(s.Projeto)[0]));
        var l = Sessao(Importar("longo")); // 2 gravadas
        var fig = PrimeiraFigura(l);
        Assert.Equal(4, CamadaFiguras.VagasNumeradas(CamadaFiguras.Inicial(l.Projeto)[0]));
        l.Aplicar(new IncluirMarcacao(fig, "mk_1", TiposMarcacao.Seta, Alvo: [.5, .5]));
        l.Aplicar(new IncluirMarcacao(fig, "mk_t", TiposMarcacao.Texto, Posicao: [.5, .5], Texto: "nota"));
        Assert.Equal(3, CamadaFiguras.VagasNumeradas(l.Projeto.Figuras!.Single(x => x.Id == fig))); // o texto livre não ocupa número
    }

    [Fact]
    public void figura_do_kit_marcacao_nova_entra_e_sai_e_a_gravada_continua()
    {
        // O caso relatado (2026-11-17): figura do kit com marcações gravadas; a nova (por cima) inclui e remove; a gravada não sai.
        var s = Sessao(Importar("longo"));
        var fig = PrimeiraFigura(s);
        var gravadas = CamadaFiguras.Inicial(s.Projeto)[0].Marcacoes.Select(m => m.Id).ToList();
        s.Aplicar(new IncluirMarcacao(fig, "mk_n", TiposMarcacao.Regiao, Caixa: [.1, .1, .3, .3]));
        var f = s.Projeto.Figuras!.Single(x => x.Id == fig);
        Assert.Contains(CamadaFiguras.Desenho(f), d => d.Id == "mk_n"); // desenhada pelo editor (a gravada não)
        Assert.DoesNotContain(CamadaFiguras.Desenho(f), d => gravadas.Contains(d.Id!));

        Assert.NotNull(s.Aplicar(new ExcluirMarcacao(fig, "mk_n")));
        f = s.Projeto.Figuras!.Single(x => x.Id == fig);
        Assert.Null(f.Buscar("mk_n"));
        Assert.Equal(gravadas, f.Marcacoes.Select(m => m.Id).ToList());
        Assert.Null(FigurasEfetivas(s.Projeto)[0]!["marcacoes"]); // sem marcações novas: a figura volta a ser a imagem do kit
        Assert.Equal(2, FigurasEfetivas(s.Projeto)[0]!["notas"]!.AsArray().Count);

        var e = Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new ExcluirMarcacao(fig, gravadas[0])));
        Assert.Contains("gravada na imagem", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void excluir_e_restaurar_figura()
    {
        var s = Sessao(Importar("longo"));
        var fig = PrimeiraFigura(s);
        s.Aplicar(new ExcluirFigura(fig));
        Assert.Equal(7, FigurasEfetivas(s.Projeto).Count);
        s.Aplicar(new RestaurarFigura(fig));
        Assert.Equal(8, FigurasEfetivas(s.Projeto).Count);
    }

    [Fact]
    public void camada_grava_e_rele_no_projeto_json_e_reaplica_pelo_diario()
    {
        var c = Importar("longo");
        var aberto = c.Projeto.Clonar();
        var s = Sessao(c);
        var fig = PrimeiraFigura(s);
        s.Aplicar(new IncluirMarcacao(fig, "mk_r", TiposMarcacao.Regiao, Caixa: [.1, .2, .3, .4]));
        s.Aplicar(new DefinirTextoFigura(fig, "titulo", "Planta do térreo"));

        var relido = ProjetoJson.Ler(ProjetoJson.Escrever(s.Projeto));
        Assert.Equal(ProjetoJson.Escrever(s.Projeto), ProjetoJson.Escrever(relido));
        Assert.Equal(DadosEfetivos.Gerar(s.Projeto).Json, DadosEfetivos.Gerar(relido).Json);

        // O diário de recuperação (os registros) refaz o mesmo estado sobre o projeto como foi aberto.
        var outra = new SessaoRevisao(aberto, c.Historico, Ambiente.Contexto());
        outra.Reaplicar(s.HistoricoCompleto);
        Assert.Equal(DadosEfetivos.Gerar(s.Projeto).Json, DadosEfetivos.Gerar(outra.Projeto).Json);
        Assert.True(outra.PodeDesfazer);
    }

    [Fact]
    public void historico_descreve_os_comandos_de_figura()
    {
        var s = Sessao(Importar("longo"));
        var fig = PrimeiraFigura(s);
        var r = s.Aplicar(new IncluirMarcacao(fig, "mk_h", TiposMarcacao.Seta, Alvo: [.5, .5]))!;
        Assert.Equal("Marcação incluída na Figura 1", DescricaoHistorico.Texto(r, s.Projeto));
        var d = s.Desfazer()!;
        Assert.Equal("Desfeito: Marcação incluída na Figura 1", DescricaoHistorico.Texto(d, s.Projeto));
    }

    [Fact]
    public void converter_itens_aceita_plural()
    {
        var ids = new[] { "a", "b", "c" };
        Assert.Equal("itens {{ref:a:2}} e {{ref:c:2}}", CamadaFiguras.ConverterItens("itens 01 e 03", ids));
        Assert.Equal("item {{ref:b:2}}", CamadaFiguras.ConverterItens("item 02", ids));
        Assert.Equal("item 09", CamadaFiguras.ConverterItens("item 09", ids)); // fora da faixa: literal
    }

    [Fact]
    public void comparar_lista_as_figuras_alteradas()
    {
        var s = Sessao(Importar("longo"));
        var fig = PrimeiraFigura(s);
        Assert.Empty(ComparacaoFiguras.Linhas(s.Projeto));
        s.Aplicar(new IncluirMarcacao(fig, "mk_c", TiposMarcacao.Seta, Alvo: [.5, .5], Elemento: "Porta"));
        var linhas = ComparacaoFiguras.Linhas(s.Projeto);
        Assert.Contains(linhas, l => l.Contains("Figura 1", StringComparison.Ordinal));
        Assert.Contains(linhas, l => l.Contains("incluída", StringComparison.Ordinal) && l.Contains("Porta", StringComparison.Ordinal));
    }
}
