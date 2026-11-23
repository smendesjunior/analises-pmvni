using Pmvni.Legislacao.Testes.Ficticios;
using Xunit;

namespace Pmvni.Legislacao.Testes;

public sealed class LeituraPadronizadaTestes
{
    [Fact]
    public void Estrutura_epigrafe_ementa_divisoes_artigos_paragrafos_e_incisos()
    {
        var paginas = new[]
        {
            "PREFEITURA MUNICIPAL\nLEI COMPLEMENTAR Nº 70, DE 20 DE DEZEMBRO DE 1990\nInstitui as normas gerais para as edificações\nnos perímetros urbanos.\nTÍTULO I\nPARTE GERAL\nCAPÍTULO I - DAS DISPOSIÇÕES PRELIMINARES\nArt. 1º Qualquer construção ou re-\nforma somente poderá ser executada\n1",
            "PREFEITURA MUNICIPAL\nmediante licença.\nParágrafo único. A licença é pessoal.\nArt. 2 São documentos:\nI - planta de situação;\nII – memorial;\na) assinado;\n2",
            "PREFEITURA MUNICIPAL\nArt. 10. Esta lei entra em vigor.\n§ 1º Revogam-se as disposições.\n3",
        };
        var b = LeituraPadronizada.Estruturar(paginas);
        Assert.Equal(TipoBlocoLei.Epigrafe, b[0].Tipo);
        Assert.Equal("LEI COMPLEMENTAR Nº 70, DE 20 DE DEZEMBRO DE 1990", b[0].Texto);
        Assert.Equal(TipoBlocoLei.Ementa, b[1].Tipo);
        Assert.StartsWith("Institui as normas gerais", b[1].Texto);
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Titulo, Rotulo: "TÍTULO I", Texto: "PARTE GERAL" });
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Capitulo, Rotulo: "CAPÍTULO I", Texto: "DAS DISPOSIÇÕES PRELIMINARES" });
        var art1 = b.Single(x => x.Rotulo == "Art. 1º");
        Assert.Equal("Qualquer construção ou reforma somente poderá ser executada mediante licença.", art1.Texto);
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Paragrafo, Rotulo: "Parágrafo único" });
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Artigo, Rotulo: "Art. 2º" });
        Assert.Equal(2, b.Count(x => x.Tipo == TipoBlocoLei.Inciso));
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Alinea, Rotulo: "a)" });
        Assert.Contains(b, x => x is { Tipo: TipoBlocoLei.Artigo, Rotulo: "Art. 10" });
        Assert.DoesNotContain(b, x => x.Texto.Contains("PREFEITURA MUNICIPAL", StringComparison.Ordinal));
    }

    [Fact]
    public void Texto_vazio_nao_gera_blocos() => Assert.Empty(LeituraPadronizada.Estruturar(["", "  "]));

    // ---- Resumo automático (correções 2026-11-15) ----

    [Theory]
    [InlineData("Para os efeitos desta Lei ficam dispensados de apresentação de projeto e anotação de responsabilidade técnica (ART -CREA), ficando contudo sujeitas a concessão de licença",
        "Ficam dispensados de apresentação de projeto e anotação de responsabilidade técnica")]
    [InlineData("Qualquer construção ou reforma, de iniciativa pública ou privada, somente poderá ser executada apos exame, aprovação de projeto o concessão de licença de construção pela Prefeitura Municipal",
        "Qualquer construção ou reforma somente poderá ser executada apos exame, aprovação…")]
    [InlineData("São documentos:", "São documentos")]
    [InlineData("Esta Lei entra em vigor na data de sua publicação.", "Esta Lei entra em vigor na data de sua publicação")]
    [InlineData("(VETADO)", "Vetado")]
    [InlineData("Revogado pela Lei nº 85/1991.", "Revogado")]
    [InlineData("Nos termos do art. 5º, o alvará será emitido em até trinta dias; após o prazo, caberá recurso.", "O alvará será emitido em até trinta dias")]
    [InlineData("", "")]
    public void Resumo_automatico_sai_do_proprio_texto(string texto, string esperado) => Assert.Equal(esperado, LeituraPadronizada.Resumir(texto));

    [Fact]
    public void Resumo_tem_no_maximo_12_palavras_e_so_palavras_do_texto()
    {
        const string texto = "O proprietário do imóvel deverá manter a calçada limpa, conservada e livre de obstáculos que impeçam a passagem de pedestres e cadeiras de rodas";
        var r = LeituraPadronizada.Resumir(texto);
        var palavras = r.TrimEnd('…').Split(' ');
        Assert.True(palavras.Length <= LeituraPadronizada.MaximoPalavrasResumo);
        Assert.EndsWith("…", r);
        var doTexto = texto.Split(' ', ',').Select(p => p.ToLowerInvariant()).ToHashSet();
        Assert.All(palavras, p => Assert.Contains(p.TrimEnd(',').ToLowerInvariant(), doTexto));
    }

    [Fact]
    public void Artigos_trazem_chave_e_resumo_e_a_chave_repetida_ganha_numero()
    {
        var b = LeituraPadronizada.Estruturar(["Art. 1º Fica criado o Conselho Municipal de Obras.\nArt. 2º O conselho terá sete membros.\nArt. 1º Fica alterada a Lei anterior."]);
        var artigos = b.Where(x => x.Tipo == TipoBlocoLei.Artigo).ToList();
        Assert.Equal(["Art. 1º", "Art. 2º", "Art. 1º (2)"], artigos.Select(a => a.Chave));
        Assert.Equal("Fica criado o Conselho Municipal de Obras", artigos[0].Resumo);
        Assert.All(b.Where(x => x.Tipo != TipoBlocoLei.Artigo), x => Assert.Equal("", x.Resumo));
    }

    // ---- Figuras no ponto certo (correções 2026-11-15) ----

    static FiguraPdf Figura(int pagina, int linha, double y = 0.5, string origem = OrigemFigura.Imagem, double x = 0.1) =>
        new(pagina, linha, x, y, 0.3, 0.1, origem, "image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0], 200);

    [Fact]
    public void Figura_entre_dois_artigos_fica_entre_eles()
    {
        var paginas = new[] { "Art. 1º O quadro abaixo mostra o afastamento.\nArt. 2º O texto continua depois da figura." };
        var b = LeituraPadronizada.Estruturar(paginas, [Figura(1, 1)]);
        Assert.Equal([TipoBlocoLei.Artigo, TipoBlocoLei.Figura, TipoBlocoLei.Artigo], b.Select(x => x.Tipo));
        Assert.Equal("Figura da página 1", b[1].Rotulo);
        Assert.NotNull(b[1].Figura);
    }

    [Fact]
    public void Figura_no_meio_do_paragrafo_vem_logo_depois_dele_e_figura_final_no_fim_da_pagina()
    {
        var paginas = new[] { "Art. 1º Primeira linha do artigo\nque continua aqui.\nArt. 2º Outro.", "Art. 3º Último." };
        var b = LeituraPadronizada.Estruturar(paginas, [Figura(1, 1), Figura(1, 99, 0.9), Figura(2, 0)]);
        Assert.Equal([TipoBlocoLei.Artigo, TipoBlocoLei.Figura, TipoBlocoLei.Artigo, TipoBlocoLei.Figura, TipoBlocoLei.Figura, TipoBlocoLei.Artigo],
            b.Select(x => x.Tipo));
        Assert.Equal("Primeira linha do artigo que continua aqui.", b[0].Texto);
        Assert.Equal(2, b[4].Pagina);
    }

    [Fact]
    public void Pagina_sem_texto_mostra_a_figura_e_o_aviso_de_conteudo_grafico()
    {
        var b = LeituraPadronizada.Estruturar(["Art. 1º Texto.", "", "Art. 2º Fim."], [Figura(2, 0, origem: OrigemFigura.Pagina)]);
        Assert.Equal([TipoBlocoLei.Artigo, TipoBlocoLei.Figura, TipoBlocoLei.Artigo], b.Select(x => x.Tipo));
        Assert.Equal("Conteúdo gráfico da página 2", b[1].Rotulo);
        // Só figuras (PDF digitalizado sem texto): a leitura ainda tem o que mostrar.
        Assert.Single(LeituraPadronizada.Estruturar(["", " "], [Figura(1, 0, origem: OrigemFigura.Pagina)]));
    }

    [Fact]
    public void Figura_repetida_no_mesmo_lugar_em_quase_todas_as_paginas_e_cabecalho()
    {
        var paginas = new[] { "Art. 1º Um.", "Art. 2º Dois.", "Art. 3º Três.", "Art. 4º Quatro." };
        var brasao = Enumerable.Range(1, 4).Select(p => Figura(p, 0, 0.02)).ToList();
        var b = LeituraPadronizada.Estruturar(paginas, [.. brasao, Figura(3, 1, 0.5, x: 0.4)]);
        Assert.Single(b, x => x.Tipo == TipoBlocoLei.Figura);
        Assert.Equal(3, b.Single(x => x.Tipo == TipoBlocoLei.Figura).Pagina);
    }
}

/// <summary>Figuras e resumos editados guardados no acervo (correções 2026-11-15): ao lado dos dados da lei, nunca no PDF.</summary>
public sealed class LeituraPadronizadaAcervoTestes : IDisposable
{
    readonly Cenario c = new("leitura-padronizada");

    public void Dispose() => c.Dispose();

    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 9, 9];

    [Fact]
    public void Figuras_sao_guardadas_relidas_e_saem_com_a_lei_sem_tocar_no_pdf()
    {
        var lei = c.Anexar(Cenario.Lei);
        var pdf = Path.Combine(c.Dados, lei.Sha256 + ".pdf");
        var antes = Cenario.Marca.De(pdf);
        Assert.Null(c.Acervo.Figuras(lei.Id)); // ainda não lidas: quem abre pede ao extrator

        c.Acervo.GravarFiguras(lei.Id,
        [
            new FiguraPdf(1, 2, 0.1, 0.2, 0.5, 0.25, OrigemFigura.Imagem, "image/png", Png, 300),
            new FiguraPdf(3, 0, 0, 0, 1, 1, OrigemFigura.Pagina, "image/jpeg", Jpeg, 595),
            new FiguraPdf(3, 0, 0, 0, 1, 1, OrigemFigura.Falhou, "", [], 595),
        ]);
        var lidas = c.Novo().Figuras(lei.Id)!;
        Assert.Equal(3, lidas.Count);
        Assert.Equal((1, 2, 0.5, OrigemFigura.Imagem), (lidas[0].Pagina, lidas[0].Linha, lidas[0].W, lidas[0].Origem));
        Assert.Equal(Png, lidas[0].Dados);
        Assert.Equal(Jpeg, lidas[1].Dados);
        Assert.Empty(lidas[2].Dados);
        Assert.Contains(lei.Sha256.ToUpperInvariant() + ".FIGURAS.JSON.GZ", c.ArquivosDaPasta());
        Assert.Equal(antes, Cenario.Marca.De(pdf));

        c.Acervo.Excluir(lei.Id);
        Assert.DoesNotContain(c.ArquivosDaPasta(), f => f.Contains(".figuras.", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Figura_com_bytes_que_nao_conferem_com_o_tipo_invalida_o_arquivo()
    {
        var lei = c.Anexar(Cenario.Lei);
        c.Acervo.GravarFiguras(lei.Id, [new FiguraPdf(1, 0, 0, 0, 1, 1, OrigemFigura.Imagem, "image/png", Jpeg, 10)]);
        Assert.Null(c.Acervo.Figuras(lei.Id));
    }

    [Fact]
    public void Resumo_editado_e_guardado_vale_na_nova_versao_e_volta_ao_automatico()
    {
        var lei = c.Anexar(Cenario.Lei);
        Assert.Empty(c.Acervo.Resumos(lei.Id));
        c.Acervo.DefinirResumo(lei.Id, "Art. 1º", "  exigência de licença   e responsável técnico ");
        c.Acervo.DefinirResumo(lei.Id, "Art. 2º", "afastamento frontal");
        Assert.Equal("exigência de licença e responsável técnico", c.Novo().Resumos(lei.Id)["Art. 1º"]);

        var entrada = c.Acervo.Preparar(c.Original(Cenario.Lei2), lei.Id);
        c.Acervo.ConfirmarSubstituicao(lei.Id, entrada, c.Extrair(entrada.Caminho));
        Assert.Equal(2, c.Acervo.Resumos(lei.Id).Count); // a nova versão da mesma lei guarda os resumos

        c.Acervo.DefinirResumo(lei.Id, "Art. 2º", "");
        Assert.Equal(["Art. 1º"], c.Acervo.Resumos(lei.Id).Keys);
        c.Acervo.DefinirResumo(lei.Id, "Art. 1º", null);
        Assert.Empty(c.Acervo.Resumos(lei.Id));
        Assert.DoesNotContain(c.ArquivosDaPasta(), f => f.EndsWith(".resumos.json", StringComparison.Ordinal));

        c.Acervo.DefinirResumo(lei.Id, "Art. 3º", new string('x', 500));
        Assert.Equal(AcervoLegislacao.MaximoResumo, c.Acervo.Resumos(lei.Id)["Art. 3º"].Length);
        c.Acervo.Excluir(lei.Id);
        Assert.DoesNotContain(c.ArquivosDaPasta(), f => f.EndsWith(".resumos.json", StringComparison.Ordinal));
    }
}
