using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Xunit;

namespace Pmvni.Web.Testes;

public sealed class PaginadorFixture : IAsyncLifetime
{
    public HostOculto Host = null!;
    public async Task InitializeAsync() => Host = await HostOculto.CriarAsync(1000, 1200);
    public async Task DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>Paginador A4 (Tarefa 7). Cada caso é um HTML pequeno (tests/Pmvni.Web.Testes/casos) com o CSS do kit; a área útil
/// tem 254 mm = 960 px. As alturas dos casos são escolhidas para que o resultado esperado seja inequívoco.</summary>
public sealed class PaginadorTestes : IClassFixture<PaginadorFixture>
{
    static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    readonly HostOculto host;
    public PaginadorTestes(PaginadorFixture f) => host = f.Host;

    // ---------- casos ----------

    [Fact]
    public async Task blocos_simples_quebram_na_folha_seguinte()
    {
        var c = await ComporCaso("blocos");
        Assert.Equal(3, c.Folhas);
        Assert.Equal(new[] { "[0]", "[3]", "[6]" }, Inicios(c.Plano));
        Assert.Empty(c.Plano.Estouros);
        // As regras @page do kit saem; só a de folha.css fica (margem 0).
        var paginas = await Js("""
            [...document.styleSheets].flatMap(s => [...s.cssRules]).filter(r => r instanceof CSSPageRule).map(r => r.style.margin)
            """);
        Assert.Equal(new[] { "0px" }, paginas.EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal(new[] { "B1 B2 B3", "B4 B5 B6", "B7" }, await TextosDasFolhas(".bloco"));
    }

    [Fact]
    public async Task card_dividido_repete_borda_clone()
    {
        var c = await ComporCaso("card_clone");
        Assert.Equal(2, c.Folhas);
        var inicio2 = c.Plano.InicioDasFolhas[1];
        Assert.Equal(2, inicio2.Caminho.Length);
        Assert.Equal(1, inicio2.Caminho[0]);
        Assert.True(inicio2.Caminho[1] >= 2, "não quebra logo após .ch (break-after: avoid)");
        var e = await Js("""
            (() => {
              const [f1, f2] = [...document.querySelectorAll('section.folha')];
              const est = el => { const s = getComputedStyle(el); return { bt: s.borderTopWidth, bb: s.borderBottomWidth, pt: s.paddingTop, pb: s.paddingBottom, rt: s.borderTopLeftRadius, rb: s.borderBottomLeftRadius }; };
              const c1 = f1.querySelector('.area > .card'), c2 = f2.querySelector('.area > .card');
              const a1 = f1.querySelector('.area').getBoundingClientRect();
              return { c1: est(c1), c2: est(c2), fimC1: c1.getBoundingClientRect().bottom - a1.bottom };
            })()
            """);
        Assert.Equal("1px", e.GetProperty("c1").GetProperty("bb").GetString());
        Assert.Equal("8px", e.GetProperty("c1").GetProperty("pb").GetString());
        Assert.Equal("11px", e.GetProperty("c1").GetProperty("rb").GetString());
        Assert.Equal("1px", e.GetProperty("c2").GetProperty("bt").GetString());
        Assert.Equal("8px", e.GetProperty("c2").GetProperty("pt").GetString());
        Assert.Equal("11px", e.GetProperty("c2").GetProperty("rt").GetString());
        // O pedaço que quebra estende-se até o fim da área (como o Chromium faz com a caixa quebrada).
        Assert.InRange(e.GetProperty("fimC1").GetDouble(), -0.5, 0.5);
    }

    [Fact]
    public async Task refs_div_dividido_com_slice()
    {
        var c = await ComporCaso("refs_slice");
        Assert.Equal(2, c.Folhas);
        var inicio2 = c.Plano.InicioDasFolhas[1];
        Assert.Equal(2, inicio2.Caminho.Length);
        Assert.Equal(1, inicio2.Caminho[0]);
        Assert.True(inicio2.Caminho[1] > 0);
        var e = await Js("""
            (() => {
              const folhas = [...document.querySelectorAll('section.folha')];
              const est = el => { const s = getComputedStyle(el); return { bt: s.borderTopWidth, bb: s.borderBottomWidth, pt: s.paddingTop, pb: s.paddingBottom, rt: s.borderTopLeftRadius, rb: s.borderBottomLeftRadius }; };
              const visiveis = f => [...f.querySelectorAll('.rf')].filter(r => !r.closest('[data-pmvni-oculto]'));
              const r1 = folhas[0].querySelector('.area > .refs'), r2 = folhas[1].querySelector('.area > .refs');
              const v1 = visiveis(folhas[0]), v2 = visiveis(folhas[1]);
              return { r1: est(r1), r2: est(r2), ultimo1: est(v1.at(-1)).bb, ultimo2: est(v2.at(-1)).bb,
                       textos: [...v1, ...v2].map(r => r.querySelector('b').textContent) };
            })()
            """);
        Assert.Equal("1px", e.GetProperty("r1").GetProperty("bt").GetString());
        Assert.Equal("0px", e.GetProperty("r1").GetProperty("bb").GetString());
        Assert.Equal("0px", e.GetProperty("r1").GetProperty("pb").GetString());
        Assert.Equal("0px", e.GetProperty("r1").GetProperty("rb").GetString());
        Assert.Equal("0px", e.GetProperty("r2").GetProperty("bt").GetString());
        Assert.Equal("0px", e.GetProperty("r2").GetProperty("pt").GetString());
        Assert.Equal("0px", e.GetProperty("r2").GetProperty("rt").GetString());
        Assert.Equal("1px", e.GetProperty("r2").GetProperty("bb").GetString());
        // Seletores estruturais preservados: o último .rf da folha 1 não é o último da lista e mantém a borda.
        Assert.Equal("1px", e.GetProperty("ultimo1").GetString());
        Assert.Equal("0px", e.GetProperty("ultimo2").GetString());
        Assert.Equal(Enumerable.Range(1, 12).Select(i => $"Fonte {i:00}"), e.GetProperty("textos").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task tabela_repete_thead_e_nao_divide_linha()
    {
        var c = await ComporCaso("tabela");
        Assert.Equal(2, c.Folhas);
        var inicio2 = c.Plano.InicioDasFolhas[1];
        Assert.Equal(new[] { 0, 1 }, inicio2.Caminho);
        Assert.NotNull(inicio2.Linha);
        Assert.Null(inicio2.DeslocamentoTexto);
        var e = await Js("""
            (() => {
              const folhas = [...document.querySelectorAll('section.folha')];
              const linhas = f => [...f.querySelectorAll('tbody tr')].filter(r => !r.closest('[data-pmvni-oculto]')).map(r => r.cells[0].textContent);
              const t2 = folhas[1].querySelector('table');
              return { l1: linhas(folhas[0]), l2: linhas(folhas[1]),
                       thead2: t2.tHead && getComputedStyle(t2.tHead).display !== 'none' ? t2.tHead.rows[0].cells[0].textContent : null,
                       cols2: t2.querySelectorAll('colgroup col').length };
            })()
            """);
        var l1 = e.GetProperty("l1").EnumerateArray().Select(x => x.GetString()!).ToList();
        var l2 = e.GetProperty("l2").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(Enumerable.Range(1, 60).Select(i => $"LINHA {i:00}"), l1.Concat(l2));
        Assert.Equal(inicio2.Linha, l1.Count);
        Assert.Equal("ÍNDICE", e.GetProperty("thead2").GetString());
        Assert.Equal(5, e.GetProperty("cols2").GetInt32());
    }

    [Fact]
    public async Task sec_acompanha_o_bloco_seguinte()
    {
        var c = await ComporCaso("sec_acompanha");
        Assert.Equal(new[] { "[0]", "[1]" }, Inicios(c.Plano));
        // Se o título e o bloco não cabem juntos nem numa folha vazia, quebra entre eles mesmo assim.
        var g = await ComporCaso("sec_bloco_grande");
        Assert.Equal(new[] { "[0]", "[1]", "[2]" }, Inicios(g.Plano));
        Assert.Empty(g.Plano.Estouros);
    }

    [Fact]
    public async Task paragrafo_longo_divide_por_linha_com_orphans_widows()
    {
        var c = await ComporCaso("paragrafo");
        Assert.Equal(2, c.Folhas);
        var inicio2 = c.Plano.InicioDasFolhas[1];
        Assert.Equal(new[] { 1 }, inicio2.Caminho);
        Assert.True(inicio2.DeslocamentoTexto > 0);
        var e = await Js("""
            (() => {
              const ps = [...document.querySelectorAll('section.folha .area > p.longo')];
              const original = document.querySelector('.pmvni-medida p.longo').textContent;
              const linhas = p => { const r = document.createRange(); r.selectNodeContents(p); return new Set([...r.getClientRects()].map(q => Math.round(q.top))).size; };
              return { original, partes: ps.map(p => p.textContent), linhas: ps.map(linhas) };
            })()
            """);
        var partes = e.GetProperty("partes").EnumerateArray().Select(x => x.GetString()!).ToList();
        Assert.Equal(2, partes.Count);
        Assert.Equal(e.GetProperty("original").GetString(), string.Concat(partes));
        Assert.Equal(inicio2.DeslocamentoTexto, partes[0].Length);
        Assert.False(char.IsWhiteSpace(partes[1][0]));
        var linhas = e.GetProperty("linhas").EnumerateArray().Select(x => x.GetInt32()).ToList();
        Assert.Equal(17, linhas[0]); // (960 − 700) / 14,6875 = 17,7 linhas
        Assert.True(linhas[1] >= 2);

        // orphans: só cabe 1 linha → o parágrafo inteiro vai para a folha seguinte.
        var o = await ComporCaso("paragrafo_orfao");
        Assert.Equal(new[] { "[0]", "[1]" }, Inicios(o.Plano));
        Assert.Null(o.Plano.InicioDasFolhas[1].DeslocamentoTexto);

        // widows: cabem 9 de 10 linhas → quebra depois da 8ª, para levar 2 linhas.
        var v = await ComporCaso("paragrafo_viuva");
        Assert.Equal(2, v.Folhas);
        Assert.Equal(new[] { 1 }, v.Plano.InicioDasFolhas[1].Caminho);
        // (o <br> antes de "linha 9" fica no fim do 1º pedaço: não cria linha, mas deixa um \n no innerText)
        var t = await Js("[...document.querySelectorAll('section.folha .area > p.dez')].map(p => p.innerText.split('\\n').filter(l => l.trim()).length)");
        Assert.Equal(new[] { 8, 2 }, t.EnumerateArray().Select(x => x.GetInt32()));
    }

    [Fact]
    public async Task margem_superior_zerada_na_folha_seguinte()
    {
        var c = await ComporCaso("margem");
        Assert.Equal(new[] { "[0]", "[3]" }, Inicios(c.Plano));
        var e = await Js("""
            [...document.querySelectorAll('section.folha')].map(f => {
              const a = f.querySelector('.area').getBoundingClientRect();
              return f.querySelector('.area > .bloco:not([data-pmvni-oculto])').getBoundingClientRect().top - a.top;
            })
            """);
        var deslocamentos = e.EnumerateArray().Select(x => x.GetDouble()).ToList();
        Assert.InRange(deslocamentos[0], 19.9, 20.1); // na 1ª folha a margem fica
        Assert.InRange(deslocamentos[1], -0.1, 0.1);  // depois de quebra não forçada, é truncada
    }

    [Fact]
    public async Task bloco_gigante_gera_estouro()
    {
        var c = await ComporCaso("gigante", new RefElemento(".card", 0, "item-gigante", null));
        Assert.Equal(2, c.Folhas);
        var estouro = Assert.Single(c.Plano.Estouros);
        Assert.Equal(2, estouro.Folha);
        Assert.Equal("item-gigante", estouro.Ref);
        Assert.True(estouro.AlturaMm > 254, "altura " + estouro.AlturaMm);
        Assert.Equal(new[] { "item-gigante" }, c.RefsPorFolha[1]);
        // Nada é escondido: a área não corta o conteúdo.
        var overflow = await Js("[...document.querySelectorAll('section.folha .area')].map(a => getComputedStyle(a).overflow)");
        Assert.All(overflow.EnumerateArray(), x => Assert.Equal("visible", x.GetString()));
    }

    [Fact]
    public async Task conteudo_mais_largo_que_a_pagina_usa_a_escala_de_impressao()
    {
        // Como o Chromium ao imprimir (shrink-to-fit): 750 px de largura numa área de 687,87 px → a área é diagramada
        // k = 1,0903 vezes maior e pintada em 1/k; a página comporta 960·k px de layout, logo 3 blocos de 340 px (não 2).
        var c = await ComporCaso("largo", new RefElemento(".largo", 0, "largo-750", null));
        Assert.InRange(c.Plano.Escala, 1.090, 1.091);
        Assert.Equal(new[] { "[0]", "[4]" }, Inicios(c.Plano));
        Assert.Empty(c.Plano.Estouros);
        // O editor é avisado de que o documento sai reduzido, e por qual bloco.
        var aviso = Assert.Single(c.Plano.AvisosLargura);
        Assert.Equal(1, aviso.Folha);
        Assert.Equal("largo-750", aviso.Ref);
        Assert.InRange(aviso.Escala, 1.090, 1.091);
        Assert.InRange(aviso.LarguraMm, 198.3, 198.5); // 750 px
        var e = await Js("""
            (() => { const a = document.querySelector('section.folha .area'); return [a.offsetWidth, a.getBoundingClientRect().width]; })()
            """);
        var medidas = e.EnumerateArray().Select(x => x.GetDouble()).ToList();
        Assert.InRange(medidas[0], 749, 751);          // layout
        Assert.InRange(medidas[1], 687.3, 688.4);      // na folha: 182 mm
        // Recompor um documento sem transbordamento volta à escala 1, sem avisos.
        var sem = (await ComporCaso("blocos")).Plano;
        Assert.Equal(1, sem.Escala);
        Assert.Empty(sem.AvisosLargura);
    }

    [Fact]
    public async Task conteudo_mais_largo_que_o_encolhimento_maximo_gera_estouro()
    {
        // 1100 px pedem k = 1,599; o Chromium reduz no máximo 1,5 e corta o resto: exportação recusada.
        var c = await ComporCaso("muito_largo", new RefElemento(".largo", 0, "largo-1100", null));
        Assert.Equal(PlanoQuebras.EscalaMaxima, c.Plano.Escala);
        var estouro = Assert.Single(c.Plano.Estouros);
        Assert.Equal(1, estouro.Folha);
        Assert.Equal("largo-1100", estouro.Ref);
        Assert.NotNull(estouro.LarguraMm);
        Assert.InRange(estouro.LarguraMm!.Value, 290.9, 291.1); // 1100 px
        var aviso = Assert.Single(c.Plano.AvisosLargura);
        Assert.InRange(aviso.Escala, 1.598, 1.600);
        // O estouro de altura continua sem largura.
        var g = await ComporCaso("gigante");
        Assert.Null(Assert.Single(g.Plano.Estouros).LarguraMm);
    }

    [Fact]
    public async Task texto_sem_quebra_mais_largo_que_o_encolhimento_maximo_gera_estouro()
    {
        // A largura vem só do scrollWidth (texto nowrap que transborda o próprio <p>; nenhuma caixa passa da área):
        // antes era limitada a 1,5 em silêncio. Agora sai aviso e estouro, com a folha e o ref do bloco.
        var c = await ComporCaso("texto_largo", new RefElemento(".moldura", 0, "texto-largo", null));
        Assert.Equal(PlanoQuebras.EscalaMaxima, c.Plano.Escala);
        var estouro = Assert.Single(c.Plano.Estouros);
        Assert.Equal(1, estouro.Folha);
        Assert.Equal("texto-largo", estouro.Ref);
        Assert.True(estouro.LarguraMm > 182 * 1.5, "largura " + estouro.LarguraMm);
        var aviso = Assert.Single(c.Plano.AvisosLargura);
        Assert.Equal("texto-largo", aviso.Ref);
        Assert.True(aviso.Escala > PlanoQuebras.EscalaMaxima);
    }

    [Fact]
    public async Task bloco_largo_sem_folha_achada_ainda_gera_estouro()
    {
        // Se o marcador do bloco largo não for achado em nenhuma folha, o estouro de largura não pode sumir (a
        // exportação precisa ser recusada): vale a folha que mostra o ref dele, senão a 1ª.
        await ComporCaso("muito_largo", new RefElemento(".largo", 0, "largo-1100", null));
        var r = await Js("""
            (() => {
              const fluxo = document.querySelector('.pmvni-fluxo');
              const areas = [...document.querySelectorAll('section.folha > .area')];
              const solto = document.createElement('div');
              fluxo.appendChild(solto);  // sem marcador em folha nenhuma; o ref é o do último bloco identificado antes
              const largos = [{ el: solto, direita: 1100 }];
              const comRef = Paginador.larguraNoPlano({ pedida: 1.6, largos, area: 687.87, esquerda: 0 }, areas, fluxo);
              const semFolhas = Paginador.larguraNoPlano({ pedida: 1.6, largos, area: 687.87, esquerda: 0 }, [], fluxo);
              const soScroll = Paginador.larguraNoPlano({ pedida: 1.6, largos: [], area: 687.87, esquerda: 0 }, areas, fluxo);
              solto.remove();
              return { comRef: comRef.estouros, semFolhas: semFolhas.estouros, soScroll: soScroll.estouros, avisos: soScroll.avisosLargura };
            })()
            """);
        foreach (var nome in new[] { "comRef", "semFolhas", "soScroll" })
        {
            var e = Assert.Single(r.GetProperty(nome).EnumerateArray());
            Assert.Equal(1, e.GetProperty("folha").GetInt32());
            Assert.Equal(JsonValueKind.Number, e.GetProperty("larguraMm").ValueKind);
        }
        Assert.Equal("largo-1100", r.GetProperty("comRef")[0].GetProperty("ref").GetString());
        Assert.Single(r.GetProperty("avisos").EnumerateArray());
    }

    [Fact]
    public async Task quebra_forcada_fica_no_plano_e_mantem_a_margem()
    {
        var c = await ComporCaso("forcada");
        Assert.Equal(new[] { "[0]", "[1]", "[3]" }, Inicios(c.Plano));
        Assert.Equal(new[] { false, true, false }, c.Plano.InicioDasFolhas.Select(i => i.Forcada));
        var compostas = await DeslocamentoDoPrimeiroBloco();
        Assert.InRange(compostas[1], 39.9, 40.1);  // forçada: a margem de 40 px fica
        Assert.InRange(compostas[2], -0.1, 0.1);   // não forçada: truncada
        // Só com o plano (como a Tarefa 8 fará), a mesma margem em cada folha.
        var plano = c.Plano.ParaJson();
        var cfg = JsonSerializer.Serialize(new { cabecalhoUri = Ambiente.Cfg.CabecalhoUri, rodapeEsquerda = Ambiente.Cfg.RodapeEsquerda, rodapeDireitaModelo = Ambiente.Cfg.RodapeDireitaModelo });
        var refeitas = await Js($$"""
            (() => {
              const plano = {{plano}}, cfg = {{cfg}};
              return plano.inicioDasFolhas.map((_, i) => {
                const f = Paginador.montarFolhaDoPlano(plano, i, cfg);
                const a = f.area.getBoundingClientRect().top;
                const d = f.area.querySelector('.bloco:not([data-pmvni-oculto])').getBoundingClientRect().top - a;
                f.folha.remove();
                return d;
              });
            })()
            """);
        Assert.Equal(compostas, refeitas.EnumerateArray().Select(x => x.GetDouble()).ToArray());
    }

    [Fact]
    public async Task imagem_lenta_nao_altera_plano()
    {
        var html = Caso("imagem_lenta");
        var rapido = await ComImagemLenta(html, TimeSpan.Zero, () => host.Documento.ComporDetalhadoAsync());
        var lento = await ComImagemLenta(html, TimeSpan.FromSeconds(2), () => host.Documento.ComporDetalhadoAsync());
        // A imagem (750 × 500 px) é mais larga que a área: carregada, impõe a escala de impressão k = 750 / 687,87; antes de
        // carregar ocupa 0 px. A altura de uma folha é conferida depois de suas imagens carregarem, mas a escala é medida uma
        // vez, antes de compor: medir cedo demais daria k = 1.
        Assert.Equal(new[] { "[0]" }, Inicios(rapido.Plano));
        Assert.InRange(rapido.Plano.Escala, 1.090, 1.091);
        // (a URL da imagem muda a cada execução, logo o hash do HTML também)
        Assert.Equal((rapido.Plano with { Sha256Html = "" }).ParaJson(), (lento.Plano with { Sha256Html = "" }).ParaJson());
    }

    [Fact]
    public async Task documento_que_nao_estabiliza_falha_com_instavel()
    {
        // A imagem nunca chega: em vez de um plano medido sobre layout instável, compor falha com código "instavel".
        var ex = await ComImagemLenta(Caso("imagem_lenta"), Timeout.InfiniteTimeSpan, () =>
            Assert.ThrowsAsync<Pmvni.Composicao.Web.Ponte.ErroWebException>(() =>
                host.Documento.ComporDetalhadoAsync(limiteEstabilidade: TimeSpan.FromMilliseconds(1500))), exigirServida: false);
        Assert.Equal("instavel", ex.Codigo);
        Assert.Contains("imagem", ex.Dados.GetProperty("mensagem").GetString());
    }

    [Fact]
    public void plano_valida_versao_escala_e_compara_caminho_por_valor()
    {
        var bom = new PlanoQuebras(1, [new PosicaoFluxo([0], null, null, false)], [], "abc", 1.2, []);
        static JsonElement Json(PlanoQuebras p) => JsonSerializer.SerializeToElement(p, Pmvni.Composicao.Web.Ponte.Mensagens.Opcoes);
        Assert.Equal(bom.ParaJson(), PlanoQuebras.DeJson(Json(bom)).ParaJson());
        Assert.Throws<InvalidDataException>(() => PlanoQuebras.DeJson(Json(bom with { Versao = 2 })));
        Assert.Throws<InvalidDataException>(() => PlanoQuebras.DeJson(Json(bom with { Escala = 1.6 })));
        Assert.Throws<InvalidDataException>(() => PlanoQuebras.DeJson(Json(bom with { Escala = 0.9 })));
        Assert.Throws<InvalidDataException>(() => PlanoQuebras.DeJson(Json(bom with { InicioDasFolhas = [] })));
        // Como o lerPlano do paginador: caminho não vazio, índices, deslocamento e linha ≥ 0.
        Assert.Throws<InvalidDataException>(() => PlanoQuebras.DeJson(Json(bom with { InicioDasFolhas = [new PosicaoFluxo([], null, null, false)] })));
        Assert.Throws<InvalidDataException>(() => PlanoQuebras.DeJson(Json(bom with { InicioDasFolhas = [new PosicaoFluxo([2, -1], null, null, false)] })));
        Assert.Throws<InvalidDataException>(() => PlanoQuebras.DeJson(Json(bom with { InicioDasFolhas = [new PosicaoFluxo([2], -3, null, false)] })));
        Assert.Throws<InvalidDataException>(() => PlanoQuebras.DeJson(Json(bom with { InicioDasFolhas = [new PosicaoFluxo([2], null, -1, false)] })));
        Assert.Equal(new PosicaoFluxo([3, 1], 5, null, true), new PosicaoFluxo([3, 1], 5, null, true));
        Assert.Equal(new PosicaoFluxo([3, 1], 5, null, true).GetHashCode(), new PosicaoFluxo([3, 1], 5, null, true).GetHashCode());
        Assert.NotEqual(new PosicaoFluxo([3, 1], null, null, false), new PosicaoFluxo([3, 2], null, null, false));
    }

    [Fact]
    public async Task mesmo_html_mesmo_plano()
    {
        var r = RenderCorpus("longo");
        var cfg = ConfigFolhas.PadraoDeDados(Modelo, CaminhoCorpus("longo"));
        await host.Documento.CarregarAsync(r, cfg);
        var a = await host.Documento.ComporDetalhadoAsync();
        var aDeNovo = await host.Documento.ComporDetalhadoAsync(); // recompor no mesmo documento
        await host.Documento.CarregarAsync(r, cfg);
        var b = await host.Documento.ComporDetalhadoAsync();
        Assert.True(a.Folhas > 3);
        Assert.Equal(a.Plano.ParaJson(), aDeNovo.Plano.ParaJson());
        Assert.Equal(a.Plano.ParaJson(), b.Plano.ParaJson());
        Assert.Equal(JsonSerializer.Serialize(a.RefsPorFolha), JsonSerializer.Serialize(b.RefsPorFolha));
        Assert.Equal(PlanoQuebras.Sha256De(r.Html), a.Plano.Sha256Html);
    }

    [Fact]
    public async Task figura_empurrada_encolhe_para_caber_na_sobra_e_o_plano_reaplica_igual()
    {
        var c = await ComporCaso("figuras");
        Assert.Equal(new[] { "F1 F2 F3", "F4" }, await TextosDasFolhas(".ft"));
        var ajuste = Assert.Single(c.Plano.Figuras!);
        Assert.Equal(new[] { 3 }, ajuste.Caminho);
        // Reduz no máximo 40% e nunca abaixo de 40 mm (151 px).
        var natural = await Js("document.querySelector('section.folha .fig .fi img').offsetHeight");
        Assert.InRange(ajuste.AlturaPx, Math.Max(natural.GetDouble() * .6, 151) - 1, natural.GetDouble());
        // Aplicar o plano (tela/PDF) dá as mesmas folhas, com a mesma imagem reduzida.
        await host.Documento.CarregarAsync(new ResultadoRender(Caso("figuras"), [], []), Ambiente.Cfg);
        var aplicado = await host.Documento.AplicarPlanoAsync(c.Plano);
        Assert.Empty(aplicado.FolhasQueTransbordam);
        Assert.Equal(new[] { "F1 F2 F3", "F4" }, await TextosDasFolhas(".ft"));
    }

    [Fact]
    public async Task plano_json_ida_e_volta()
    {
        await host.Documento.CarregarAsync(new ResultadoRender(Caso("paragrafo"), [], []), Ambiente.Cfg);
        var bruto = await host.Documento.ComporJsonAsync();
        var plano = bruto.GetProperty("plano");
        var volta = JsonSerializer.SerializeToElement(PlanoQuebras.DeJson(plano), Pmvni.Composicao.Web.Ponte.Mensagens.Opcoes);
        Assert.True(JsonElement.DeepEquals(plano, volta), plano.GetRawText() + "\n≠\n" + volta.GetRawText());
        Assert.Equal(new[] { "versao", "inicioDasFolhas", "estouros", "sha256Html", "escala", "avisosLargura", "figuras" }, plano.EnumerateObject().Select(p => p.Name));
        Assert.Equal(new[] { "caminho", "deslocamentoTexto", "linha", "forcada" }, plano.GetProperty("inicioDasFolhas")[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task corpus_compoe_todos_os_casos()
    {
        var resumo = new JsonArray();
        foreach (var caso in new[] { "curto", "longo", "extremos", "estouro", "sem_opcionais" })
        {
            var r = RenderCorpus(caso);
            await host.Documento.CarregarAsync(r, ConfigFolhas.PadraoDeDados(Modelo, CaminhoCorpus(caso)));
            var inicio = DateTime.UtcNow;
            var c = await host.Documento.ComporDetalhadoAsync();
            var ms = (DateTime.UtcNow - inicio).TotalMilliseconds;
            Assert.True(c.Folhas >= 1);
            // Todo ref do render aparece em alguma folha.
            var vistos = c.RefsPorFolha.SelectMany(x => x).ToHashSet();
            Assert.All(r.Refs, x => Assert.Contains(x.Id, vistos));
            if (caso == "estouro") Assert.NotEmpty(c.Plano.Estouros);
            // Nestes, a etiqueta "INFORMAÇÕES DIVERGENTES" (nowrap) transborda a coluna: documento reduzido, com aviso.
            if (caso is "longo" or "extremos") Assert.NotEmpty(c.Plano.AvisosLargura);
            Assert.Equal(c.Plano.Escala > 1, c.Plano.AvisosLargura.Count > 0);
            resumo.Add(new JsonObject
            {
                ["caso"] = caso,
                ["folhas"] = c.Folhas,
                ["estouros"] = JsonNode.Parse(JsonSerializer.Serialize(c.Plano.Estouros, Pmvni.Composicao.Web.Ponte.Mensagens.Opcoes)),
                ["escala"] = c.Plano.Escala,
                ["avisosLargura"] = JsonNode.Parse(JsonSerializer.Serialize(c.Plano.AvisosLargura, Pmvni.Composicao.Web.Ponte.Mensagens.Opcoes)),
                ["ms"] = Math.Round(ms),
            });
        }
        var saida = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "paginacao-corpus.json");
        Directory.CreateDirectory(Path.GetDirectoryName(saida)!);
        File.WriteAllText(saida, resumo.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---------- apoio ----------

    static string Caso(string nome) =>
        File.ReadAllText(Path.Combine(Ambiente.RaizRepositorio(), "tests", "Pmvni.Web.Testes", "casos", nome + ".html"))
            .Replace("/*CSS_DO_KIT*/", Modelo.Css);

    static string CaminhoCorpus(string caso) => Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus", caso + ".json");

    static ResultadoRender RenderCorpus(string caso)
    {
        var r = RelatorioRenderer.Renderizar(CaminhoCorpus(caso), new OpcoesRender(false, true), Modelo);
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        return r;
    }

    async Task<ResultadoComposicao> ComporCaso(string nome, params RefElemento[] refs)
    {
        await host.Documento.CarregarAsync(new ResultadoRender(Caso(nome), [], refs), Ambiente.Cfg);
        return await host.Documento.ComporDetalhadoAsync();
    }

    /// <summary>Distância (px) do 1º bloco visível de cada folha ao topo da área.</summary>
    async Task<double[]> DeslocamentoDoPrimeiroBloco()
    {
        var e = await Js("""
            [...document.querySelectorAll('section.folha')].map(f => {
              const a = f.querySelector('.area').getBoundingClientRect().top;
              return f.querySelector('.area .bloco:not([data-pmvni-oculto])').getBoundingClientRect().top - a;
            })
            """);
        return e.EnumerateArray().Select(x => x.GetDouble()).ToArray();
    }

    static string[] Inicios(PlanoQuebras p) => p.InicioDasFolhas.Select(i =>
        "[" + string.Join(",", i.Caminho) + "]" + (i.DeslocamentoTexto is { } d ? "+" + d : "") + (i.Linha is { } l ? "#" + l : "")).ToArray();

    /// <summary>Texto dos elementos visíveis que casam com <paramref name="seletor"/>, folha a folha (unidos por espaço).</summary>
    async Task<string[]> TextosDasFolhas(string seletor)
    {
        var e = await Js($$"""
            [...document.querySelectorAll('section.folha')].map(f =>
              [...f.querySelectorAll('{{seletor}}')].filter(x => !x.closest('[data-pmvni-oculto]')).map(x => x.textContent.trim()).join(' '))
            """);
        return e.EnumerateArray().Select(x => x.GetString()!).ToArray();
    }

    async Task<JsonElement> Js(string expressao)
    {
        var r = await host.CdpAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = expressao, returnByValue = true, awaitPromise = true }));
        using var doc = JsonDocument.Parse(r);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out var ex)) throw new InvalidOperationException("JS: " + ex.GetRawText());
        return doc.RootElement.GetProperty("result").GetProperty("value").Clone();
    }

    /// <summary>Carrega o caso com a imagem (750 × 500 px) servida depois de <paramref name="atraso"/> e roda <paramref name="acao"/>.
    /// Com <see cref="Timeout.InfiniteTimeSpan"/> a imagem só é respondida depois que a ação termina. O mapeamento do host
    /// virtual não passa pelo WebResourceRequested nem pelo Fetch do CDP; por isso, só nestes testes, a imagem vem de um host
    /// de teste não mapeado (cuja requisição passa pelo WebResourceRequested) e a CSP é contornada pelo CDP (Page.setBypassCSP).</summary>
    async Task<T> ComImagemLenta<T>(string html, TimeSpan atraso, Func<Task<T>> acao, bool exigirServida = true)
    {
        // URL única por execução: sem ela, o cache de imagens do Chromium entrega a imagem da execução anterior na hora.
        var Url = "https://lenta.pmvni.invalid/lenta.svg?n=" + Guid.NewGuid().ToString("N");
        // Só inicia a requisição depois da navegação e da configuração CDP desta
        // página. O lazy-load do Chromium não deve decidir quando o teste começa.
        html = html.Replace("https://doc.pmvni.invalid/lenta.svg", "data:,");
        var svg = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="750" height="500"><rect width="750" height="500" fill="#89a"/></svg>""");
        var servidas = 0;
        var requisitada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task resposta = Task.CompletedTask;
        using var fim = new CancellationTokenSource();
        void Servir(CoreWebView2 sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if (e.Request.Uri != Url) return;
            var adiamento = e.GetDeferral();
            resposta = Responder();
            requisitada.TrySetResult();
            async Task Responder()
            {
                try
                {
                    if (atraso == Timeout.InfiniteTimeSpan) await Task.Delay(Timeout.Infinite, fim.Token).ContinueWith(_ => { });
                    else if (atraso > TimeSpan.Zero) await Task.Delay(atraso);
                    var fluxo = new MemoryStream(svg).AsRandomAccessStream();
                    e.Response = sender.Environment.CreateWebResourceResponse(fluxo, 200, "OK", "Content-Type: image/svg+xml");
                    Interlocked.Increment(ref servidas);
                }
                finally { adiamento.Complete(); }
            }
        }
        await host.ExecutarAsync(async core =>
        {
            core.WebResourceRequested += Servir;
            await core.CallDevToolsProtocolMethodAsync("Page.setBypassCSP", """{"enabled":true}""");
            return true;
        });
        try
        {
            await host.Documento.CarregarAsync(new ResultadoRender(html, [], []), Ambiente.Cfg);
            await host.ExecutarAsync(async core =>
            {
                await core.CallDevToolsProtocolMethodAsync("Page.setBypassCSP", """{"enabled":true}""");
                await core.ExecuteScriptAsync("const i = document.querySelector('img'); i.loading = 'eager'; i.src = " + JsonSerializer.Serialize(Url));
                return true;
            });
            await requisitada.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var r = await acao();
            if (exigirServida) Assert.True(servidas >= 1, "a imagem não passou pelo manipulador de teste");
            return r;
        }
        finally
        {
            fim.Cancel();
            await host.ExecutarAsync(async core =>
            {
                // Nenhum deferral/continuação pode sobreviver ao manipulador.
                await resposta;
                await core.CallDevToolsProtocolMethodAsync("Page.setBypassCSP", """{"enabled":false}""");
                core.WebResourceRequested -= Servir;
                return true;
            });
        }
    }
}
