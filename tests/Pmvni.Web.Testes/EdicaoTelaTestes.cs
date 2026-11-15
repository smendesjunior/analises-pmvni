using System.Security.Cryptography;
using System.Text.Json;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Ponte;
using Pmvni.Revisao;
using Xunit;
using Xunit.Abstractions;

namespace Pmvni.Web.Testes;

/// <summary>Tarefa 7 da Etapa 2: seleção, campos (data-campo), edição no A4, marcas de revisão fora das folhas, âncora de rolagem,
/// atalhos e menu de contexto, no paginador. WebView2 real; cliques e teclas por CDP (Input.dispatch*). A "tela" é o host que
/// recebe o plano aplicado (como o WebView2 visível do app); o relatório vem do pacote, com os refs de sessão (it_…).</summary>
public sealed class EdicaoTelaTestes : IClassFixture<TelaFixture>
{
    static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    static readonly TimeSpan Limite = TimeSpan.FromSeconds(30);
    readonly HostOculto comp, tela;
    readonly ITestOutputHelper saida;

    public EdicaoTelaTestes(TelaFixture f, ITestOutputHelper saida)
    {
        comp = f.Composicao;
        tela = f.Tela;
        this.saida = saida;
    }

    // ---------- marcação dos campos ----------

    [Fact]
    public async Task estado_editor_marca_campos_sem_mudar_texto_nem_medidas()
    {
        using var a = await AbrirAsync("longo");
        var textoAntes = await tela.Documento.TextoPorFolhaAsync();
        var medidasAntes = await MedidasAsync();
        Assert.Equal(0, (await Js("document.querySelectorAll('[data-campo]').length")).GetInt32());

        var r = await EstadoAsync(null, true);
        var marcados = r.GetProperty("camposMarcados").GetInt32();
        Assert.True(marcados >= 300, "campos marcados: " + marcados);
        Assert.Equal(0, r.GetProperty("marcasDesenhadas").GetInt32());
        Assert.Equal(marcados, (await Js("document.querySelectorAll('section.folha [data-campo]').length")).GetInt32());
        Assert.Equal(textoAntes, await tela.Documento.TextoPorFolhaAsync());
        AssertMedidasIguais(medidasAntes, await MedidasAsync());

        // Cada um dos 60 itens tem os 5 campos, só nos pedaços dentro das folhas; o fluxo de medida e o HTML do relatório ficam limpos.
        foreach (var campo in new[] { "titulo", "verificacao", "providencia", "base_legal", "referencia" })
        {
            var refs = await Js($$"""new Set([...document.querySelectorAll('section.folha .card[data-ref] [data-campo="{{campo}}"]')].map(e => e.closest('.card').getAttribute('data-ref'))).size""");
            Assert.Equal(60, refs.GetInt32());
        }
        Assert.Equal(0, (await Js("document.querySelectorAll('div.folha.pmvni-medida [data-campo]').length")).GetInt32());
        Assert.DoesNotContain("data-campo", a.E.Render.Html);
        Assert.DoesNotContain("data-pmvni", a.E.Render.Html);

        // O título: o texto depois de "ITEM NN — " fica num span; o texto do card não muda.
        var titulo = Titulo(a.Pacote.Projeto.Itens[0]);
        var ct = await Js($$"""(() => { const ct = document.querySelector('section.folha .card[data-ref="{{a.Ids[0]}}"] .ct'); const s = ct.querySelector('span[data-campo="titulo"]'); return { texto: ct.textContent, span: s.textContent, antes: s.previousSibling.textContent }; })()""");
        Assert.Equal("ITEM 01 — " + titulo, ct.GetProperty("texto").GetString());
        Assert.Equal(titulo, ct.GetProperty("span").GetString());
        Assert.Equal(" — ", ct.GetProperty("antes").GetString());

        // Estado repetido não duplica nada; campos desligados desfazem a marcação por inteiro.
        Assert.Equal(marcados, (await EstadoAsync(null, true)).GetProperty("camposMarcados").GetInt32());
        Assert.Equal(textoAntes, await tela.Documento.TextoPorFolhaAsync());
        Assert.Equal(0, (await EstadoAsync(null, false)).GetProperty("camposMarcados").GetInt32());
        Assert.Equal(0, (await Js("document.querySelectorAll('[data-campo]').length")).GetInt32());
        Assert.Equal(textoAntes, await tela.Documento.TextoPorFolhaAsync());
        AssertMedidasIguais(medidasAntes, await MedidasAsync());
        Assert.Equal(" — " + titulo, (await Js($$"""document.querySelector('section.folha .card[data-ref="{{a.Ids[0]}}"] .ct').lastChild.textContent""")).GetString());
        Assert.Equal(1, (await Js($$"""document.querySelector('section.folha .card[data-ref="{{a.Ids[0]}}"] .ct').children.length""")).GetInt32()); // só span.id
    }

    [Fact]
    public async Task estado_editor_invalido_responde_erro()
    {
        using var a = await AbrirAsync("curto");
        var ex = await Assert.ThrowsAsync<ErroWebException>(() => tela.Documento.ChamarAsync("estadoEditor",
            new { selecao = 3, campos = true, marcas = new { visiveis = false, alterados = Array.Empty<string>(), excluidos = Array.Empty<string>() } }, Limite));
        Assert.Equal("estado_invalido", ex.Codigo);
        ex = await Assert.ThrowsAsync<ErroWebException>(() => tela.Documento.ChamarAsync("estadoEditor",
            new { selecao = (string?)null, campos = true, marcas = new { visiveis = true, alterados = Array.Empty<string>(), excluidos = new[] { new { id = "x", rotulo = "01", ancora = "y", lado = "ao lado" } } } }, Limite));
        Assert.Equal("estado_invalido", ex.Codigo);
    }

    // ---------- seleção ----------

    [Fact]
    public async Task clique_no_card_envia_selecionado_com_o_id()
    {
        using var a = await AbrirAsync("longo");
        using var ev = new Eventos(tela.Documento);
        await EstadoAsync(null, true);
        var item = a.Ids[2]; // ITEM 03
        await RolarAsync(item);

        var (x, y) = await PontoDoCampoAsync(item, "verificacao");
        await ClicarAsync(x, y);
        var m = await ev.ProximoAsync("selecionado");
        Assert.StartsWith("evento-", m.Id);
        Assert.Equal(item, m.Dados.GetProperty("ref").GetString());
        Assert.Equal("verificacao", m.Dados.GetProperty("campo").GetString());
        var pedacos = (await Js($$"""document.querySelectorAll('section.folha .card[data-ref="{{item}}"]').length""")).GetInt32();
        Assert.True(pedacos >= 1);
        Assert.Equal(pedacos, (await Js("document.querySelectorAll('[data-pmvni-selecionado]').length")).GetInt32());
        Assert.Equal(pedacos, (await Js($$"""document.querySelectorAll('section.folha .card[data-ref="{{item}}"][data-pmvni-selecionado]').length""")).GetInt32());
        var contorno = await Js($$"""(() => { const c = getComputedStyle(document.querySelector('section.folha .card[data-ref="{{item}}"]')); return c.outlineStyle + ' ' + c.outlineWidth; })()""");
        Assert.Equal("solid 2px", contorno.GetString());

        // O título e a etiqueta de situação: o campo é o do título; fora de campo, null.
        (x, y) = await PontoDoCampoAsync(item, "titulo");
        await ClicarAsync(x, y);
        Assert.Equal("titulo", (await ev.ProximoAsync("selecionado")).Dados.GetProperty("campo").GetString());
        var pilula = await Js($$"""(() => { const r = document.querySelector('section.folha .card[data-ref="{{item}}"] .ch').getBoundingClientRect(); return { x: r.right - 4, y: r.top + r.height / 2 }; })()""");
        await ClicarAsync(pilula.GetProperty("x").GetDouble(), pilula.GetProperty("y").GetDouble());
        var semCampo = await ev.ProximoAsync("selecionado");
        Assert.Equal(item, semCampo.Dados.GetProperty("ref").GetString());
        Assert.Equal(JsonValueKind.Null, semCampo.Dados.GetProperty("campo").ValueKind);

        // Fora de item (a margem cinza): ref nulo e a seleção some.
        await ClicarAsync(3, 300);
        var fora = await ev.ProximoAsync("selecionado");
        Assert.Equal(JsonValueKind.Null, fora.Dados.GetProperty("ref").ValueKind);
        Assert.Equal(JsonValueKind.Null, fora.Dados.GetProperty("campo").ValueKind);
        Assert.Equal(0, (await Js("document.querySelectorAll('[data-pmvni-selecionado]').length")).GetInt32());
        Assert.Empty(tela.Documento.Descartadas);
    }

    [Fact]
    public async Task selecionar_marca_todos_os_pedacos_e_rola()
    {
        using var a = await AbrirAsync("extremos");
        await EstadoAsync(null, true);
        var longo = a.Ids[1]; // a verificação de 9 000 caracteres atravessa folhas
        var r = await tela.Documento.ChamarAsync("selecionar", new { @ref = longo, rolar = true }, Limite);
        Assert.Equal(longo, r.GetProperty("ref").GetString());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("campo").ValueKind);
        var pedacos = (await Js($$"""document.querySelectorAll('section.folha .card[data-ref="{{longo}}"]').length""")).GetInt32();
        Assert.True(pedacos >= 2, "pedaços: " + pedacos);
        Assert.Equal(pedacos, (await Js($$"""document.querySelectorAll('section.folha .card[data-ref="{{longo}}"][data-pmvni-selecionado]').length""")).GetInt32());
        Assert.Equal(pedacos, (await Js("document.querySelectorAll('[data-pmvni-selecionado]').length")).GetInt32());
        var topo = (await Js($$"""document.querySelector('section.folha .card[data-ref="{{longo}}"]').getBoundingClientRect().top""")).GetDouble();
        Assert.InRange(topo, -2, 1400);

        // Troca a seleção por um item de outra folha e limpa.
        await tela.Documento.ChamarAsync("selecionar", new { @ref = a.Ids[0], rolar = false }, Limite);
        Assert.Equal(0, (await Js($$"""document.querySelectorAll('[data-pmvni-selecionado]:not([data-ref="{{a.Ids[0]}}"])').length""")).GetInt32());
        await tela.Documento.ChamarAsync("selecionar", new { @ref = (string?)null, rolar = false }, Limite);
        Assert.Equal(0, (await Js("document.querySelectorAll('[data-pmvni-selecionado]').length")).GetInt32());

        var ex = await Assert.ThrowsAsync<ErroWebException>(() => tela.Documento.ChamarAsync("selecionar", new { @ref = "it_nao_existe", rolar = false }, Limite));
        Assert.Equal("ref_inexistente", ex.Codigo);

        // A seleção também vem no estado do editor.
        await EstadoAsync(a.Ids[3], true);
        Assert.Equal(1, (await Js($$"""document.querySelectorAll('section.folha .card[data-ref="{{a.Ids[3]}}"][data-pmvni-selecionado]').length""")).GetInt32());
    }

    // ---------- edição no A4 ----------

    [Fact]
    public async Task duplo_clique_pede_edicao_e_enter_envia_texto_puro()
    {
        using var a = await AbrirAsync("longo");
        using var ev = new Eventos(tela.Documento);
        await EstadoAsync(null, true);
        var item = a.Ids[2];
        await RolarAsync(item);
        var antes = await TextoDoCampoAsync(item, "verificacao");

        var (x, y) = await PontoDoCampoAsync(item, "verificacao");
        await ClicarAsync(x, y, 2);
        var pedido = await ev.ProximoAsync("pedirEdicao");
        Assert.Equal(item, pedido.Dados.GetProperty("ref").GetString());
        Assert.Equal("verificacao", pedido.Dados.GetProperty("campo").GetString());

        var ini = await tela.Documento.ChamarAsync("editarCampo", new { @ref = item, campo = "verificacao", texto = "Texto A" }, Limite);
        Assert.Equal(item, ini.GetProperty("ref").GetString());
        Assert.Equal("verificacao", ini.GetProperty("campo").GetString());
        Assert.Equal("plaintext-only", (await Js("document.querySelector('[contenteditable]').getAttribute('contenteditable')")).GetString());
        Assert.Equal(1, (await Js("document.querySelectorAll('[contenteditable]').length")).GetInt32());
        Assert.True((await Js("document.activeElement === document.querySelector('[contenteditable]')")).GetBoolean());
        Assert.Equal("Texto A", (await Js("document.querySelector('[contenteditable]').textContent")).GetString());

        ev.Descartar();
        await InserirTextoAsync(" B");
        await TeclaAsync("Enter", "Enter", 13);
        var editar = await ev.ProximoAsync("editar");
        Assert.StartsWith("evento-", editar.Id);
        Assert.Equal(item, editar.Dados.GetProperty("ref").GetString());
        Assert.Equal("verificacao", editar.Dados.GetProperty("campo").GetString());
        Assert.Equal("Texto A B", editar.Dados.GetProperty("texto").GetString());
        Assert.Equal(0, (await Js("document.querySelectorAll('[contenteditable]').length")).GetInt32());
        // Durante a edição nada mais foi ao host além do Enter.
        Assert.False(await ev.HouveAsync("selecionado", 300));
        Assert.False(await ev.HouveAsync("atalho", 100));

        // O host recusou (ou nada mudou) e manda o estado de novo: a tela volta ao texto original.
        await EstadoAsync(null, true);
        Assert.Equal(antes, await TextoDoCampoAsync(item, "verificacao"));
        Assert.Equal(0, (await Js("[...document.querySelectorAll('section.folha [data-campo]')].filter(e => e.style.visibility !== '').length")).GetInt32());
    }

    [Fact]
    public async Task edicao_do_titulo_mantem_o_prefixo_do_item()
    {
        using var a = await AbrirAsync("curto");
        using var ev = new Eventos(tela.Documento);
        await EstadoAsync(null, true);
        var item = a.Ids[0];
        await tela.Documento.ChamarAsync("editarCampo", new { @ref = item, campo = "titulo", texto = "Novo título" }, Limite);
        Assert.Equal("ITEM 01 — Novo título", (await Js($$"""document.querySelector('section.folha .card[data-ref="{{item}}"] .ct').textContent""")).GetString());
        Assert.Equal("SPAN", (await Js("document.querySelector('[contenteditable]').tagName")).GetString());
        await InserirTextoAsync("!");
        await TeclaAsync("Enter", "Enter", 13);
        var editar = await ev.ProximoAsync("editar");
        Assert.Equal("titulo", editar.Dados.GetProperty("campo").GetString());
        Assert.Equal("Novo título!", editar.Dados.GetProperty("texto").GetString());
    }

    [Fact]
    public async Task esc_cancela_e_restaura()
    {
        using var a = await AbrirAsync("longo");
        using var ev = new Eventos(tela.Documento);
        await EstadoAsync(null, true);
        var item = a.Ids[4];
        var texto0 = await TextoPorFolhaNormalizadoAsync();
        var original = await TextoDoCampoAsync(item, "providencia");

        await tela.Documento.ChamarAsync("editarCampo", new { @ref = item, campo = "providencia", texto = "Rascunho que será descartado" }, Limite);
        await InserirTextoAsync(" mais");
        Assert.Equal("Rascunho que será descartado mais", (await Js("document.querySelector('[contenteditable]').textContent")).GetString());
        await TeclaAsync("Escape", "Escape", 27);
        var cancelada = await ev.ProximoAsync("edicaoCancelada");
        Assert.Equal(item, cancelada.Dados.GetProperty("ref").GetString());
        Assert.Equal("providencia", cancelada.Dados.GetProperty("campo").GetString());
        Assert.Equal(original, await TextoDoCampoAsync(item, "providencia"));
        Assert.Equal(0, (await Js("document.querySelectorAll('[contenteditable]').length")).GetInt32());
        Assert.Equal(0, (await Js("document.querySelectorAll('[data-pmvni-editando]').length")).GetInt32());
        Assert.Equal(texto0, await TextoPorFolhaNormalizadoAsync());
        Assert.False(await ev.HouveAsync("editar", 300));
    }

    [Fact]
    public async Task colar_html_vira_texto_puro()
    {
        using var a = await AbrirAsync("curto");
        using var ev = new Eventos(tela.Documento);
        await EstadoAsync(null, true);
        var item = a.Ids[1];
        await tela.Documento.ChamarAsync("editarCampo", new { @ref = item, campo = "verificacao", texto = "Antes " }, Limite);

        await InserirTextoAsync("<b>x</b>\nlinha");
        Assert.Equal(0, (await Js("document.querySelectorAll('[data-campo] b, [contenteditable] b').length")).GetInt32());

        // Colar HTML da área de transferência: só o texto simples entra, em uma linha.
        var colou = await Js("""
            (() => {
              const el = document.querySelector('[contenteditable]');
              const dt = new DataTransfer();
              dt.setData('text/html', '<i>y</i><img src=x onerror=alert(1)>');
              dt.setData('text/plain', 'y1\r\ny2');
              const e = new ClipboardEvent('paste', { clipboardData: dt, bubbles: true, cancelable: true });
              el.dispatchEvent(e);
              return e.defaultPrevented;
            })()
            """);
        Assert.True(colou.GetBoolean());
        await InserirTextoAsync(" & < > \" ' 😀");
        Assert.Equal(0, (await Js("document.querySelectorAll('[contenteditable] *:not(br)').length")).GetInt32());
        await TeclaAsync("Enter", "Enter", 13);
        var texto = (await ev.ProximoAsync("editar")).Dados.GetProperty("texto").GetString()!;
        Assert.Contains("<b>x</b>", texto);
        Assert.Contains("linha", texto);
        Assert.Contains("y1 y2", texto);
        Assert.Contains("& < > \" ' 😀", texto);
        Assert.DoesNotContain("\n", texto);
        Assert.DoesNotContain("\r", texto);
        Assert.DoesNotContain("<i>", texto);
        Assert.Equal(0, (await Js("document.querySelectorAll('[data-campo] b, [data-campo] i, [data-campo] img').length")).GetInt32());
    }

    [Fact]
    public async Task campo_dividido_edita_no_primeiro_pedaco()
    {
        using var a = await AbrirAsync("extremos");
        using var ev = new Eventos(tela.Documento);
        await EstadoAsync(null, true);
        var item = a.Ids[1];
        var seletor = $$"""section.folha .card[data-ref="{{item}}"] [data-campo="verificacao"]""";
        var originais = (await Js($$"""[...document.querySelectorAll('{{seletor}}')].map(e => e.textContent)""")).EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.True(originais.Count >= 2, "pedaços da verificação: " + originais.Count);
        Assert.True(originais.Sum(t => t.Length) >= 8000);

        var novo = "NOVO " + new string('x', 9000 - 5);
        await tela.Documento.ChamarAsync("editarCampo", new { @ref = item, campo = "verificacao", texto = novo }, Limite);
        var estado = await Js($$"""
            (() => {
              const todos = [...document.querySelectorAll('{{seletor}}')];
              return { n: todos.length, primeiro: todos[0].textContent.length, editavel: todos[0].hasAttribute('contenteditable'),
                       outrosEditaveis: todos.slice(1).filter(e => e.hasAttribute('contenteditable')).length,
                       ocultos: todos.slice(1).filter(e => getComputedStyle(e).visibility === 'hidden').length,
                       primeiroVisivel: getComputedStyle(todos[0]).visibility,
                       editaveisNoDocumento: document.querySelectorAll('[contenteditable]').length };
            })()
            """);
        Assert.Equal(originais.Count, estado.GetProperty("n").GetInt32());
        Assert.Equal(novo.Length, estado.GetProperty("primeiro").GetInt32());
        Assert.True(estado.GetProperty("editavel").GetBoolean());
        Assert.Equal(0, estado.GetProperty("outrosEditaveis").GetInt32());
        Assert.Equal(originais.Count - 1, estado.GetProperty("ocultos").GetInt32());
        Assert.Equal("visible", estado.GetProperty("primeiroVisivel").GetString());
        Assert.Equal(1, estado.GetProperty("editaveisNoDocumento").GetInt32());

        await TeclaAsync("Escape", "Escape", 27);
        await ev.ProximoAsync("edicaoCancelada");
        var depois = (await Js($$"""[...document.querySelectorAll('{{seletor}}')].map(e => e.textContent)""")).EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(originais, depois);
        Assert.Equal(0, (await Js($$"""[...document.querySelectorAll('{{seletor}}')].filter(e => e.style.visibility !== '' || e.hasAttribute('contenteditable')).length""")).GetInt32());
    }

    [Fact]
    public async Task concluir_edicao_devolve_o_texto_ou_sem_edicao()
    {
        using var a = await AbrirAsync("curto");
        await EstadoAsync(null, true);
        var item = a.Ids[0];
        var vazio = await tela.Documento.ChamarAsync("concluirEdicao", new { }, Limite);
        Assert.Empty(vazio.EnumerateObject());

        var ex = await Assert.ThrowsAsync<ErroWebException>(() => tela.Documento.ChamarAsync("editarCampo", new { @ref = "it_nao_existe", campo = "titulo", texto = "x" }, Limite));
        Assert.Equal("campo_inexistente", ex.Codigo);
        ex = await Assert.ThrowsAsync<ErroWebException>(() => tela.Documento.ChamarAsync("editarCampo", new { @ref = item, campo = "situacao", texto = "x" }, Limite));
        Assert.Equal("edicao_invalida", ex.Codigo);

        await tela.Documento.ChamarAsync("editarCampo", new { @ref = item, campo = "base_legal", texto = "Art. 1º" }, Limite);
        await InserirTextoAsync(" e art. 2º");
        var r = await tela.Documento.ChamarAsync("concluirEdicao", new { }, Limite);
        Assert.Equal(item, r.GetProperty("ref").GetString());
        Assert.Equal("base_legal", r.GetProperty("campo").GetString());
        Assert.Equal("Art. 1º e art. 2º", r.GetProperty("texto").GetString());
        Assert.Equal(0, (await Js("document.querySelectorAll('[contenteditable]').length")).GetInt32());
        Assert.Empty((await tela.Documento.ChamarAsync("concluirEdicao", new { }, Limite)).EnumerateObject());
    }

    // ---------- marcas de revisão ----------

    [Fact]
    public async Task marcas_ficam_fora_das_folhas_e_fora_do_pdf()
    {
        using var a = await AbrirAsync("longo");
        using var ev = new Eventos(tela.Documento);
        await EstadoAsync(null, true);
        var textoAntes = await tela.Documento.TextoPorFolhaAsync();
        var pdfAntes = await tela.Documento.ImprimirPdfAsync();
        var infoAntes = InfoPdf.Ler(pdfAntes);

        var alterado = a.Ids[1];
        var ancora = a.Ids[7];
        var excluido = new { id = "it_EXCLUIDO_07", rotulo = "07", ancora, lado = "antes" };
        var r = await EstadoAsync(null, true, Marcas(true, [alterado], [excluido]));
        Assert.Equal(2, r.GetProperty("marcasDesenhadas").GetInt32());

        // Fora das folhas: a camada é filha do <body>, nenhuma marca está dentro de section.folha.
        Assert.Equal(0, (await Js("document.querySelectorAll('section.folha .pmvni-marca').length")).GetInt32());
        Assert.Equal(0, (await Js("document.querySelectorAll('section.folha #pmvni-camada').length")).GetInt32());
        Assert.Equal("BODY", (await Js("document.getElementById('pmvni-camada').parentElement.tagName")).GetString());
        Assert.Equal("◦ excl. 07", (await Js("document.querySelector('#pmvni-camada .pmvni-marca-excl').textContent")).GetString());
        var pedacos = (await Js($$"""document.querySelectorAll('section.folha .card[data-ref="{{alterado}}"]').length""")).GetInt32();
        Assert.Equal(pedacos, (await Js("document.querySelectorAll('#pmvni-camada .pmvni-marca-traco').length")).GetInt32());

        // O traço azul (3 px) fica à esquerda do pedaço alterado, na margem cinza, com a altura dele.
        var traco = await Js($$"""
            (() => {
              const t = document.querySelector('#pmvni-camada .pmvni-marca-traco').getBoundingClientRect();
              const card = document.querySelector('section.folha .card[data-ref="{{alterado}}"]');
              const c = card.getBoundingClientRect(), f = card.closest('section.folha').getBoundingClientRect();
              return { w: t.width, h: t.height, dh: t.height - c.height, dy: t.top - c.top, direita: t.right, folhaEsq: f.left, cor: getComputedStyle(document.querySelector('#pmvni-camada .pmvni-marca-traco')).backgroundColor };
            })()
            """);
        Assert.InRange(traco.GetProperty("w").GetDouble(), 2.9, 3.1);
        Assert.InRange(traco.GetProperty("dh").GetDouble(), -1, 1);
        Assert.InRange(traco.GetProperty("dy").GetDouble(), -1, 1);
        Assert.True(traco.GetProperty("direita").GetDouble() <= traco.GetProperty("folhaEsq").GetDouble() + 0.01, "o traço deve ficar na margem cinza");

        // O botão fica na margem cinza, na altura do topo do item âncora.
        var botao = await Js($$"""
            (() => {
              const b = document.querySelector('#pmvni-camada .pmvni-marca-excl').getBoundingClientRect();
              const card = document.querySelector('section.folha .card[data-ref="{{ancora}}"]');
              const c = card.getBoundingClientRect(), f = card.closest('section.folha').getBoundingClientRect();
              return { centro: b.top + b.height / 2, topoItem: c.top, direita: b.right, esquerda: b.left, folhaEsq: f.left, h: b.height };
            })()
            """);
        Assert.InRange(botao.GetProperty("centro").GetDouble() - botao.GetProperty("topoItem").GetDouble(), -2, 2);
        Assert.True(botao.GetProperty("direita").GetDouble() <= botao.GetProperty("folhaEsq").GetDouble() + 0.01);
        Assert.True(botao.GetProperty("esquerda").GetDouble() >= 0);

        // Nem a medida, nem o texto, nem o PDF mudam.
        Assert.Equal(textoAntes, await tela.Documento.TextoPorFolhaAsync());
        var pdfDepois = await tela.Documento.ImprimirPdfAsync();
        var infoDepois = InfoPdf.Ler(pdfDepois);
        Assert.Equal(infoAntes.Paginas, infoDepois.Paginas);
        Assert.Equal(a.C.Folhas, infoDepois.Paginas);
        await AssertPaginasIguaisAsync(pdfAntes, pdfDepois, "marcas");

        // O botão leva ao host o id do excluído e o ponto do clique.
        await RolarAsync(ancora);
        var centro = await Js("(() => { const b = document.querySelector('#pmvni-camada .pmvni-marca-excl').getBoundingClientRect(); return { x: b.left + b.width / 2, y: b.top + b.height / 2 }; })()");
        var (cx, cy) = (centro.GetProperty("x").GetDouble(), centro.GetProperty("y").GetDouble());
        await ClicarAsync(cx, cy);
        var clicado = await ev.ProximoAsync("excluidoClicado");
        Assert.StartsWith("evento-", clicado.Id);
        Assert.Equal("it_EXCLUIDO_07", clicado.Dados.GetProperty("id").GetString());
        Assert.InRange(clicado.Dados.GetProperty("x").GetDouble(), cx - 1, cx + 1);
        Assert.InRange(clicado.Dados.GetProperty("y").GetDouble(), cy - 1, cy + 1);
        Assert.False(await ev.HouveAsync("selecionado", 300), "o botão de excluído não é clique em item");

        // "depois": na base do item anterior.
        var depois = new { id = "it_EXCLUIDO_07", rotulo = "07", ancora = a.Ids[6], lado = "depois" };
        await EstadoAsync(null, true, Marcas(true, [], [depois]));
        var base_ = await Js($$"""
            (() => {
              const b = document.querySelector('#pmvni-camada .pmvni-marca-excl').getBoundingClientRect();
              const pedacos = [...document.querySelectorAll('section.folha .card[data-ref="{{a.Ids[6]}}"]')];
              return { centro: b.top + b.height / 2, base: pedacos[pedacos.length - 1].getBoundingClientRect().bottom };
            })()
            """);
        Assert.InRange(base_.GetProperty("centro").GetDouble() - base_.GetProperty("base").GetDouble(), -2, 2);

        // Marcas desligadas: a camada some e o estado volta ao de antes.
        Assert.Equal(0, (await EstadoAsync(null, true, Marcas(false, [alterado], [excluido]))).GetProperty("marcasDesenhadas").GetInt32());
        Assert.Equal(0, (await Js("document.querySelectorAll('#pmvni-camada, .pmvni-marca').length")).GetInt32());
        Assert.Equal(textoAntes, await tela.Documento.TextoPorFolhaAsync());
    }

    [Fact]
    public async Task marcas_acompanham_o_zoom_e_o_botao_mantem_o_tamanho()
    {
        using var a = await AbrirAsync("longo");
        var alterado = a.Ids[1];
        var ancora = a.Ids[3];
        var excluido = new { id = "it_EXCLUIDO_04", rotulo = "04", ancora, lado = "antes" };
        await EstadoAsync(null, true, Marcas(true, [alterado], [excluido]));
        double? larguraDoBotao = null;
        foreach (var zoom in new[] { 1.0, 0.5, 2.0 })
        {
            await tela.Documento.ChamarAsync("definirZoom", new { zoom }, Limite);
            var m = await Js($$"""
                (() => {
                  const card = document.querySelector('section.folha .card[data-ref="{{alterado}}"]').getBoundingClientRect();
                  const t = document.querySelector('#pmvni-camada .pmvni-marca-traco').getBoundingClientRect();
                  const alvo = document.querySelector('section.folha .card[data-ref="{{ancora}}"]');
                  const b = document.querySelector('#pmvni-camada .pmvni-marca-excl').getBoundingClientRect();
                  const f = alvo.closest('section.folha').getBoundingClientRect();
                  return { dTopo: t.top - card.top, dAltura: t.height - card.height, larguraTraco: t.width, direitaTraco: t.right - f.left,
                           dCentro: b.top + b.height / 2 - alvo.getBoundingClientRect().top, direitaBotao: b.right - f.left, larguraBotao: b.width, alturaBotao: b.height };
                })()
                """);
            Assert.InRange(m.GetProperty("dTopo").GetDouble(), -1, 1);
            Assert.InRange(m.GetProperty("dAltura").GetDouble(), -1.5, 1.5);
            Assert.InRange(m.GetProperty("larguraTraco").GetDouble(), 2.9, 3.1); // 3 px de tela em qualquer zoom
            Assert.True(m.GetProperty("direitaTraco").GetDouble() < 0, $"zoom {zoom}: traço dentro da folha");
            Assert.InRange(m.GetProperty("dCentro").GetDouble(), -1.5, 1.5);
            Assert.InRange(m.GetProperty("direitaBotao").GetDouble(), -6.2, -5.8);
            Assert.InRange(m.GetProperty("alturaBotao").GetDouble(), 17.9, 18.1);
            larguraDoBotao ??= m.GetProperty("larguraBotao").GetDouble();
            Assert.InRange(m.GetProperty("larguraBotao").GetDouble(), larguraDoBotao.Value - 0.5, larguraDoBotao.Value + 0.5);
        }
    }

    [Fact]
    public async Task marcas_visiveis_alargam_a_margem_esquerda()
    {
        using var a = await AbrirAsync("curto");
        try
        {
            // Janela estreita: a folha encosta na margem mínima (12 px; 72 px com marcas).
            await tela.CdpAsync("Emulation.setDeviceMetricsOverride", """{"width":800,"height":1400,"deviceScaleFactor":1,"mobile":false}""");
            await Task.Delay(300);
            await EstadoAsync(null, true, Marcas(false));
            Assert.InRange(await EsquerdaDaFolhaAsync(), 11.5, 12.5);
            await EstadoAsync(null, true, Marcas(true, [a.Ids[0]]));
            Assert.InRange(await EsquerdaDaFolhaAsync(), 71.5, 72.5);
            await EstadoAsync(null, true, Marcas(false));
            Assert.InRange(await EsquerdaDaFolhaAsync(), 11.5, 12.5);
        }
        finally
        {
            await tela.CdpAsync("Emulation.setDeviceMetricsOverride", """{"width":0,"height":0,"deviceScaleFactor":1,"mobile":false}""");
        }
    }

    // ---------- âncora de rolagem ----------

    [Fact]
    public async Task ancora_restaura_a_rolagem_depois_de_recarregar()
    {
        using var a = await AbrirAsync("longo");
        await EstadoAsync(null, true);
        await RolarAsync(a.Ids[29]); // ITEM 30
        await Js("window.scrollBy(0, 37); 0");
        var scroll0 = (await Js("window.scrollY")).GetDouble();
        var ancora = await tela.Documento.ChamarAsync("obterAncora", new { }, Limite);
        var ref0 = ancora.GetProperty("ref").GetString();
        var desl0 = ancora.GetProperty("deslocamentoPx").GetDouble();
        Assert.False(string.IsNullOrEmpty(ref0));
        Assert.Contains(ref0!, a.Ids);
        saida.WriteLine($"âncora: {ref0}, deslocamento {desl0:0.0} px, rolagem {scroll0:0.0}");

        // Recarrega o documento e aplica o plano: a rolagem volta ao zero e a âncora a restaura.
        await RecarregarAsync(a);
        Assert.Equal(0, (await Js("window.scrollY")).GetDouble(), 1);
        var r = await EstadoAsync(null, true, null, new { @ref = ref0, deslocamentoPx = desl0 });
        Assert.True(r.GetProperty("camposMarcados").GetInt32() >= 300);
        var depois = await tela.Documento.ChamarAsync("obterAncora", new { }, Limite);
        Assert.Equal(ref0, depois.GetProperty("ref").GetString());
        Assert.InRange(Math.Abs(depois.GetProperty("deslocamentoPx").GetDouble() - desl0), 0, 2);
        Assert.InRange(Math.Abs((await Js("window.scrollY")).GetDouble() - scroll0), 0, 2);

        // O deslocamento é em px sem zoom: com outro zoom a âncora cai no mesmo ponto do mesmo item.
        await tela.Documento.ChamarAsync("definirZoom", new { zoom = 0.5 }, Limite);
        await EstadoAsync(null, true, null, new { @ref = ref0, deslocamentoPx = desl0 });
        var emMeio = await tela.Documento.ChamarAsync("obterAncora", new { }, Limite);
        Assert.Equal(ref0, emMeio.GetProperty("ref").GetString());
        Assert.InRange(Math.Abs(emMeio.GetProperty("deslocamentoPx").GetDouble() - desl0), 0, 2);

        // Âncora de um item que não existe mais: ignorada, sem erro.
        await EstadoAsync(null, true, null, new { @ref = "it_sumiu", deslocamentoPx = 5 });
    }

    // ---------- atalhos e menu de contexto ----------

    [Fact]
    public async Task atalhos_da_lista_vao_ao_host_e_os_outros_nao()
    {
        using var a = await AbrirAsync("curto");
        using var ev = new Eventos(tela.Documento);
        await EstadoAsync(null, true);
        await Js("window.__dp = []; document.addEventListener('keydown', e => window.__dp.push([e.key, e.ctrlKey, e.altKey, e.shiftKey, e.defaultPrevented])); 0");

        var atalhos = new (string Nome, string Key, string Code, int Vk, int Mods)[]
        {
            ("Ctrl+O", "o", "KeyO", 79, 2), ("Ctrl+I", "i", "KeyI", 73, 2), ("Ctrl+S", "s", "KeyS", 83, 2),
            ("Ctrl+Shift+S", "S", "KeyS", 83, 10), ("Ctrl+E", "e", "KeyE", 69, 2), ("Ctrl+Z", "z", "KeyZ", 90, 2),
            ("Ctrl+Y", "y", "KeyY", 89, 2), ("Ctrl+F", "f", "KeyF", 70, 2),
            ("Alt+ArrowUp", "ArrowUp", "ArrowUp", 38, 1), ("Alt+ArrowDown", "ArrowDown", "ArrowDown", 40, 1),
            ("Delete", "Delete", "Delete", 46, 0), ("F2", "F2", "F2", 113, 0), ("F6", "F6", "F6", 117, 0),
            ("Ctrl+0", "0", "Digit0", 48, 2), ("Ctrl+Plus", "=", "Equal", 187, 2), ("Ctrl+Plus", "+", "Equal", 187, 10),
            ("Ctrl+Plus", "+", "NumpadAdd", 107, 2), ("Ctrl+Minus", "-", "Minus", 189, 2), ("Ctrl+Minus", "-", "NumpadSubtract", 109, 2),
        };
        foreach (var (nome, key, code, vk, mods) in atalhos)
        {
            await TeclaAsync(key, code, vk, mods);
            var m = await ev.ProximoAsync("atalho");
            Assert.StartsWith("evento-", m.Id);
            Assert.Equal(nome, m.Dados.GetProperty("tecla").GetString());
        }
        var vistos = await Js("window.__dp");
        Assert.Equal(atalhos.Length, vistos.GetArrayLength());
        Assert.All(vistos.EnumerateArray(), v => Assert.True(v[4].GetBoolean(), "preventDefault faltando em " + v));

        // Fora da lista: nada vai ao host e o navegador segue seu caminho.
        ev.Descartar();
        await TeclaAsync("p", "KeyP", 80, 2);
        await TeclaAsync("a", "KeyA", 65, 0);
        await TeclaAsync("Delete", "Delete", 46, 2);
        Assert.False(await ev.HouveAsync("atalho", 1000));
        var ultimos = await Js("window.__dp.slice(-3)");
        Assert.All(ultimos.EnumerateArray(), v => Assert.False(v[4].GetBoolean()));

        // Durante a edição, Ctrl+Z / Ctrl+Y e os demais atalhos são do campo: nada vai ao host.
        await tela.Documento.ChamarAsync("editarCampo", new { @ref = a.Ids[0], campo = "titulo", texto = "Abc" }, Limite);
        ev.Descartar();
        await TeclaAsync("z", "KeyZ", 90, 2);
        await TeclaAsync("y", "KeyY", 89, 2);
        await TeclaAsync("s", "KeyS", 83, 2);
        await TeclaAsync("Delete", "Delete", 46, 0);
        await TeclaAsync("F2", "F2", 113, 0);
        Assert.False(await ev.HouveAsync("atalho", 1000));
        await TeclaAsync("Escape", "Escape", 27);
        await ev.ProximoAsync("edicaoCancelada");
        // E depois da edição os atalhos voltam.
        await TeclaAsync("s", "KeyS", 83, 2);
        Assert.Equal("Ctrl+S", (await ev.ProximoAsync("atalho")).Dados.GetProperty("tecla").GetString());
    }

    [Fact]
    public async Task botao_direito_envia_menu_de_contexto()
    {
        using var a = await AbrirAsync("longo");
        using var ev = new Eventos(tela.Documento);
        await EstadoAsync(null, true);
        var item = a.Ids[2];
        await RolarAsync(item);

        var (x, y) = await PontoDoCampoAsync(item, "referencia");
        await ClicarAsync(x, y, 1, "right");
        var m = await ev.ProximoAsync("menuContexto");
        Assert.StartsWith("evento-", m.Id);
        Assert.Equal(item, m.Dados.GetProperty("ref").GetString());
        Assert.Equal("referencia", m.Dados.GetProperty("campo").GetString());
        var largura = (await Js("document.documentElement.clientWidth")).GetDouble();
        var altura = (await Js("window.innerHeight")).GetDouble();
        var mx = m.Dados.GetProperty("x").GetDouble();
        var my = m.Dados.GetProperty("y").GetDouble();
        Assert.InRange(mx, 0, largura);
        Assert.InRange(my, 0, altura);
        Assert.InRange(mx, x - 1, x + 1);
        Assert.InRange(my, y - 1, y + 1);
        Assert.True((await Js($$"""document.querySelectorAll('section.folha .card[data-ref="{{item}}"][data-pmvni-selecionado]').length""")).GetInt32() > 0, "o botão direito seleciona o item");

        // Fora de item: ref e campo nulos (o host decide o menu).
        await ClicarAsync(3, 300, 1, "right");
        var fora = await ev.ProximoAsync("menuContexto");
        Assert.Equal(JsonValueKind.Null, fora.Dados.GetProperty("ref").ValueKind);
        Assert.Equal(JsonValueKind.Null, fora.Dados.GetProperty("campo").ValueKind);
    }

    // ---------- apoio ----------

    sealed record Aberto(ConteudoPacote Pacote, Recompositor Rec, Preparado E, ResultadoComposicao C, IReadOnlyList<string> Ids) : IDisposable
    {
        public void Dispose() => Rec.Dispose();
    }

    /// <summary>Recebe os eventos da web (id <c>evento-N</c>) e deixa esperar o próximo de um tipo.</summary>
    sealed class Eventos : IDisposable
    {
        readonly DocumentoWeb doc;
        readonly List<MensagemWeb> todos = new();
        int cursor;

        public Eventos(DocumentoWeb d)
        {
            doc = d;
            doc.Recebida += Ouvir;
        }

        void Ouvir(MensagemWeb m)
        {
            if (m.Id.StartsWith("evento-", StringComparison.Ordinal)) lock (todos) todos.Add(m);
        }

        public async Task<MensagemWeb> ProximoAsync(string tipo, int ms = 10000)
        {
            var fim = DateTime.UtcNow.AddMilliseconds(ms);
            while (true)
            {
                lock (todos)
                    for (var i = cursor; i < todos.Count; i++)
                        if (todos[i].Tipo == tipo) { cursor = i + 1; return todos[i]; }
                if (DateTime.UtcNow >= fim) throw new TimeoutException($"O evento '{tipo}' não chegou em {ms} ms (recebidos: {string.Join(", ", Tipos())}).");
                await Task.Delay(25);
            }
        }

        /// <summary>Espera <paramref name="ms"/> e diz se chegou (desde o último consumo) algum evento do tipo.</summary>
        public async Task<bool> HouveAsync(string tipo, int ms)
        {
            await Task.Delay(ms);
            lock (todos) return todos.Skip(cursor).Any(m => m.Tipo == tipo);
        }

        /// <summary>Esquece o que já chegou.</summary>
        public void Descartar() { lock (todos) cursor = todos.Count; }

        IEnumerable<string> Tipos() { lock (todos) return todos.Select(m => m.Tipo).ToList(); }

        public void Dispose() => doc.Recebida -= Ouvir;
    }

    async Task<Aberto> AbrirAsync(string caso)
    {
        var pacote = Importar(caso);
        var rec = new Recompositor(pacote, Modelo);
        try
        {
            var e = rec.Preparar(pacote.Projeto);
            var c = await rec.ComporAsync(e, comp);
            await tela.Documento.CarregarAsync(e.Render, e.Config);
            await tela.Documento.AplicarPlanoAsync(c.Plano);
            await tela.CdpAsync("Emulation.setFocusEmulationEnabled", """{"enabled":true}""");
            await tela.Documento.ChamarAsync("definirZoom", new { zoom = 1.0 }, Limite);
            await Js("window.scrollTo(0, 0); 0");
            await GarantirTecladoAsync();
            return new Aberto(pacote, rec, e, c, pacote.Projeto.Itens.Select(i => i.Id).ToList());
        }
        catch
        {
            rec.Dispose();
            throw;
        }
    }

    async Task RecarregarAsync(Aberto a)
    {
        await tela.Documento.CarregarAsync(a.E.Render, a.E.Config);
        await tela.Documento.AplicarPlanoAsync(a.C.Plano);
        await tela.Documento.ChamarAsync("definirZoom", new { zoom = 1.0 }, Limite);
        await GarantirTecladoAsync();
    }

    static object Marcas(bool visiveis, string[]? alterados = null, object[]? excluidos = null) =>
        new { visiveis, alterados = alterados ?? [], excluidos = excluidos ?? [] };

    Task<JsonElement> EstadoAsync(string? selecao, bool campos, object? marcas = null, object? ancora = null)
    {
        var dados = new Dictionary<string, object?>
        {
            ["selecao"] = selecao, ["campos"] = campos, ["marcas"] = marcas ?? Marcas(false),
        };
        if (ancora is not null) dados["ancora"] = ancora;
        return tela.Documento.ChamarAsync("estadoEditor", dados, Limite);
    }

    Task RolarAsync(string @ref) => tela.Documento.ChamarAsync("rolarPara", new { @ref }, Limite);

    async Task<double> EsquerdaDaFolhaAsync() =>
        (await Js("document.querySelector('section.folha').getBoundingClientRect().left")).GetDouble();

    async Task<(double X, double Y)> PontoDoCampoAsync(string id, string campo, int pedaco = 0)
    {
        var p = await Js($$"""
            (() => {
              const e = [...document.querySelectorAll('section.folha .card[data-ref="{{id}}"] [data-campo="{{campo}}"]')][{{pedaco}}];
              const r = e.getClientRects()[0];
              return { x: r.left + Math.min(r.width / 2, 40), y: r.top + Math.min(r.height / 2, 6) };
            })()
            """);
        return (p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble());
    }

    async Task<string> TextoDoCampoAsync(string id, string campo) =>
        (await Js($$"""[...document.querySelectorAll('section.folha .card[data-ref="{{id}}"] [data-campo="{{campo}}"]')].map(e => e.textContent).join('|')""")).GetString()!;

    async Task<string> TextoPorFolhaNormalizadoAsync() => string.Join("\n", await tela.Documento.TextoPorFolhaAsync());

    /// <summary>Retângulos de tudo o que fica direto na área de cada folha (as medidas da paginação).</summary>
    async Task<double[][]> MedidasAsync() =>
        JsonSerializer.Deserialize<double[][]>((await Js("""
            [...document.querySelectorAll('section.folha .area > *')].map(e => { const r = e.getBoundingClientRect(); return [r.x, r.y, r.width, r.height]; })
            """)).GetRawText())!;

    static void AssertMedidasIguais(double[][] esperado, double[][] atual)
    {
        Assert.Equal(esperado.Length, atual.Length);
        for (var i = 0; i < esperado.Length; i++)
            for (var j = 0; j < 4; j++)
                Assert.True(Math.Abs(esperado[i][j] - atual[i][j]) <= 0.01, $"medida {i}/{j}: {esperado[i][j]} x {atual[i][j]}");
    }

    async Task ClicarAsync(double x, double y, int cliques = 1, string botao = "left")
    {
        var mask = botao == "right" ? 2 : 1;
        await tela.CdpAsync("Input.dispatchMouseEvent", Json(new { type = "mouseMoved", x, y, button = "none", buttons = 0 }));
        for (var n = 1; n <= cliques; n++)
        {
            await tela.CdpAsync("Input.dispatchMouseEvent", Json(new { type = "mousePressed", x, y, button = botao, buttons = mask, clickCount = n }));
            await tela.CdpAsync("Input.dispatchMouseEvent", Json(new { type = "mouseReleased", x, y, button = botao, buttons = 0, clickCount = n }));
        }
    }

    /// <summary>Logo depois de carregar, a primeira tecla às vezes não chega à página do host oculto (o teclado ainda não chegou a
    /// ela): manda Shift (fora da lista de atalhos, sem efeito) até a página registrar um keydown. Falha claramente se não chegar.</summary>
    async Task GarantirTecladoAsync()
    {
        await Js("window.__teclado = 0; window.__ouvirTeclado = () => window.__teclado++; window.addEventListener('keydown', window.__ouvirTeclado, true); 0");
        for (var i = 0; i < 40 && (await Js("window.__teclado")).GetInt32() == 0; i++)
        {
            await TeclaAsync("Shift", "ShiftLeft", 16);
            await Task.Delay(50);
        }
        var chegou = (await Js("window.__teclado")).GetInt32() > 0;
        await Js("window.removeEventListener('keydown', window.__ouvirTeclado, true); 0");
        Assert.True(chegou, "O teclado não chegou à página (40 tentativas com Shift).");
    }

    async Task TeclaAsync(string key, string code, int vk, int modificadores = 0)
    {
        await tela.CdpAsync("Input.dispatchKeyEvent", Json(new { type = "rawKeyDown", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores }));
        await tela.CdpAsync("Input.dispatchKeyEvent", Json(new { type = "keyUp", key, code, windowsVirtualKeyCode = vk, nativeVirtualKeyCode = vk, modifiers = modificadores }));
    }

    Task InserirTextoAsync(string texto) => tela.CdpAsync("Input.insertText", Json(new { text = texto }));

    static string Json(object o) => JsonSerializer.Serialize(o);

    async Task<JsonElement> Js(string expressao)
    {
        var r = await tela.CdpAsync("Runtime.evaluate", Json(new { expression = expressao, returnByValue = true, awaitPromise = true }));
        using var doc = JsonDocument.Parse(r);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out var ex)) throw new InvalidOperationException("JS: " + ex.GetRawText());
        var resultado = doc.RootElement.GetProperty("result");
        return resultado.TryGetProperty("value", out var v) ? v.Clone() : default;
    }

    async Task AssertPaginasIguaisAsync(byte[] a, byte[] b, string nome)
    {
        var dir = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "edicao");
        Directory.CreateDirectory(dir);
        var pa = Path.Combine(dir, nome + "-antes.pdf");
        var pb = Path.Combine(dir, nome + "-depois.pdf");
        await File.WriteAllBytesAsync(pa, a);
        await File.WriteAllBytesAsync(pb, b);
        var da = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(pa));
        var db = await Windows.Data.Pdf.PdfDocument.LoadFromFileAsync(await Windows.Storage.StorageFile.GetFileFromPathAsync(pb));
        Assert.Equal(da.PageCount, db.PageCount);
        for (uint i = 0; i < da.PageCount; i++) Assert.Equal(await PixelsAsync(da, i), await PixelsAsync(db, i));
    }

    static async Task<string> PixelsAsync(Windows.Data.Pdf.PdfDocument doc, uint i)
    {
        using var pagina = doc.GetPage(i);
        using var fluxo = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await pagina.RenderToStreamAsync(fluxo);
        fluxo.Seek(0);
        var dec = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(fluxo);
        var px = await dec.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8, Windows.Graphics.Imaging.BitmapAlphaMode.Ignore,
            new Windows.Graphics.Imaging.BitmapTransform(), Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        return dec.PixelWidth + "x" + dec.PixelHeight + ":" + Convert.ToHexString(SHA256.HashData(px.DetachPixelData()));
    }

    static string CaminhoCorpus(string caso) => Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus", caso + ".json");

    static ContextoUsuario Contexto() =>
        new("teste", () => new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero), RandomNumberGenerator.Create());

    static ConteudoPacote Importar(string caso)
    {
        var caminho = CaminhoCorpus(caso);
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Contexto());
    }

    static string Titulo(ItemRevisao it) => ((System.Text.Json.Nodes.JsonValue)it.Campos["titulo"].Original!).GetValue<string>();
}
