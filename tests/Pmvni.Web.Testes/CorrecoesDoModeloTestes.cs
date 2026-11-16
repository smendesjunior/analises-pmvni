using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;
using Xunit;

namespace Pmvni.Web.Testes;

public sealed class CorrecoesDoModeloFixture : IAsyncLifetime
{
    public HostOculto Host = null!;
    public async Task InitializeAsync() => Host = await HostOculto.CriarAsync(1024, 1400);
    public async Task DisposeAsync() => await Host.DisposeAsync();
}

/// <summary>Correções do editor ao modelo oficial (<c>web/paginador/correcoes.css</c>, aplicada depois do
/// <c>relatorio.css</c> do kit, que continua byte a byte): texto livre em caixa <c>nowrap</c> de largura fixa não pode cobrir o
/// vizinho nem sair da folha, e nada muda onde o texto já cabia (a fidelidade com o kit fica como estava).</summary>
public sealed class CorrecoesDoModeloTestes(CorrecoesDoModeloFixture f) : IClassFixture<CorrecoesDoModeloFixture>, IDisposable
{
    static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    readonly string pasta = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "correcoes-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { if (Directory.Exists(pasta)) Directory.Delete(pasta, true); } catch (IOException) { /* limpeza do temporário */ }
    }

    /// <summary>Os rótulos do relatório de exemplo do usuário (captura de 16/11/2026) e uma palavra só, maior que a coluna.</summary>
    static readonly string[] RotulosLongos =
    [
        "Inscrição imobiliária", "Lote / quadra", "Área do lote (matrícula)", "Área do lote (cadastro)", "Testada",
        "Georreferenciamento", "Confrontantes/vizinhança",
    ];

    [Fact]
    public async Task rotulo_longo_da_ficha_da_localizacao_nao_invade_o_valor()
    {
        await ComporAsync(Corpus("longo", d =>
        {
            var dados = d["localizacao"]!["dados"]!.AsArray();
            for (var i = 0; i < dados.Count; i++) dados[i]!["rotulo"] = RotulosLongos[i % RotulosLongos.Length];
        }));
        var linhas = (await Js(MedirFicha)).EnumerateArray().ToList();
        Assert.Equal(16, linhas.Count);
        foreach (var l in linhas)
        {
            var rotulo = l.GetProperty("rotulo").GetString();
            // O texto do rótulo fica dentro da célula dele e acaba antes de o valor começar.
            Assert.True(l.GetProperty("rotuloDireita").GetDouble() <= l.GetProperty("celulaDireita").GetDouble() + 0.5,
                $"«{rotulo}» sai da célula do rótulo: {l.GetRawText()}");
            Assert.True(l.GetProperty("rotuloDireita").GetDouble() <= l.GetProperty("valorEsquerda").GetDouble(),
                $"«{rotulo}» invade o valor: {l.GetRawText()}");
        }
        // Os rótulos da captura quebram em duas linhas; os curtos continuam em uma.
        Assert.All(linhas.Where(l => l.GetProperty("rotulo").GetString() is "Inscrição imobiliária" or "Área do lote (matrícula)"),
            l => Assert.Equal(2, l.GetProperty("linhasDoRotulo").GetInt32()));
        Assert.All(linhas.Where(l => l.GetProperty("rotulo").GetString() is "Testada" or "Lote / quadra"),
            l => Assert.Equal(1, l.GetProperty("linhasDoRotulo").GetInt32()));
    }

    [Fact]
    public async Task etiqueta_longa_do_titulo_de_secao_nao_sai_da_folha()
    {
        const string zona = "ZONA DE OCUPAÇÃO CONTROLADA 2 (ZOC 2), COM SOBREPOSIÇÃO DA ZONA ESPECIAL DE INTERESSE TURÍSTICO E PAISAGÍSTICO";
        var c = await ComporAsync(Corpus("longo", d =>
        {
            d["indices"]!["zona"] = zona;
            d["iluminacao"]!["tag"] = "8 UNIDADES HABITACIONAIS • 3 PAVIMENTOS • 2 SALAS COMERCIAIS NO TÉRREO • COBERTURA COM ÁREA DE LAZER DESCOBERTA";
        }));
        var etiquetas = (await Js(MedirEtiquetas)).EnumerateArray().ToList();
        Assert.Equal(3, etiquetas.Count);
        foreach (var e in etiquetas)
        {
            Assert.True(e.GetProperty("direita").GetDouble() <= e.GetProperty("areaDireita").GetDouble() + 0.5, $"etiqueta além da área: {e.GetRawText()}");
            Assert.True(e.GetProperty("esquerda").GetDouble() >= e.GetProperty("tituloDireita").GetDouble(), $"etiqueta sobre o título: {e.GetRawText()}");
        }
        // A etiqueta longa não pode fazer o documento inteiro ser reduzido além do que o longo já é (k = 1,0723, pela
        // etiqueta de situação da tabela de índices, igual ao kit: golden/plano-longo.json).
        Assert.InRange(c.Plano.Escala, 1.0, 1.0724);
        Assert.Empty(c.Plano.Estouros);
    }

    /// <summary>Onde o texto já cabia, a correção não muda nada: mesmas caixas (posição e tamanho) com a folha de correção
    /// ligada e desligada, nos casos do corpus com ficha e no dados_modelo.json do kit.</summary>
    [Theory]
    [InlineData("longo")]
    [InlineData("extremos")]
    [InlineData("modelo")]
    public async Task correcoes_nao_mudam_os_casos_em_que_o_texto_ja_cabia(string caso)
    {
        await ComporAsync(caso == "modelo" ? DadosModelo() : Corpus(caso, _ => { }));
        Assert.True((await Js("!!document.getElementById('pmvni-correcoes-css')")).GetBoolean(), "a folha de correção deve estar no documento");
        var com = (await Js(MedirCaixas)).GetString()!;
        await Js("document.getElementById('pmvni-correcoes-css').disabled = true");
        try
        {
            var sem = (await Js(MedirCaixas)).GetString()!;
            Assert.True(com.Length > 200, "nada medido: " + com);
            Assert.Equal(sem, com);
        }
        finally
        {
            await Js("document.getElementById('pmvni-correcoes-css').disabled = false");
        }
    }

    // ---------- apoio ----------

    /// <summary>Por linha da ficha (nas folhas): limites do texto do rótulo, da célula dele e do conteúdo do valor.</summary>
    const string MedirFicha = """
        (() => [...document.querySelectorAll('section.folha .lft tr')].filter(tr => tr.querySelector('td.lfl')).map(tr => {
          const rot = tr.querySelector('td.lfl'), val = tr.querySelector('td.lfv');
          const caixa = (e) => { const r = document.createRange(); r.selectNodeContents(e); return r; };
          const rr = caixa(rot), rv = caixa(val);
          const linhas = new Set([...rr.getClientRects()].map(q => Math.round(q.top))).size;
          return { rotulo: rot.textContent, rotuloDireita: rr.getBoundingClientRect().right, celulaDireita: rot.getBoundingClientRect().right,
                   valorEsquerda: rv.getBoundingClientRect().left, linhasDoRotulo: linhas };
        }))()
        """;

    /// <summary>Por título de seção com etiqueta (nas folhas): limites da etiqueta, do bloco do título e da área útil.</summary>
    const string MedirEtiquetas = """
        (() => [...document.querySelectorAll('section.folha .sec > .tag')].map(t => {
          const r = t.getBoundingClientRect(), a = t.closest('.area').getBoundingClientRect();
          const titulo = t.parentElement.firstElementChild.getBoundingClientRect();
          return { texto: t.textContent, esquerda: r.left, direita: r.right, tituloDireita: titulo.right, areaDireita: a.right };
        }))()
        """;

    /// <summary>Caixas de tudo o que a folha de correção alcança (fichas, títulos de seção) e de cada folha, com 3 casas.</summary>
    const string MedirCaixas = """
        (() => JSON.stringify([...document.querySelectorAll('section.folha .lft, section.folha .lft tr, section.folha .lft td, section.folha .sec, section.folha .sec > *, section.folha .sec h2, section.folha .sec p, section.folha .area > *')].map(e => {
          const r = e.getBoundingClientRect();
          return [e.tagName, e.className, r.left.toFixed(3), r.top.toFixed(3), r.width.toFixed(3), r.height.toFixed(3)].join(' ');
        })))()
        """;

    string Corpus(string caso, Action<JsonNode> ajuste)
    {
        var raiz = Path.Combine(Ambiente.RaizRepositorio(), "tests", "corpus");
        var d = JsonNode.Parse(File.ReadAllText(Path.Combine(raiz, caso + ".json")))!;
        ajuste(d);
        // Gravado ao lado do corpus só no nome do caminho: as imagens (img/…) continuam sendo as do corpus.
        Directory.CreateDirectory(pasta);
        foreach (var img in Directory.EnumerateFiles(Path.Combine(raiz, "img")))
        {
            Directory.CreateDirectory(Path.Combine(pasta, "img"));
            File.Copy(img, Path.Combine(pasta, "img", Path.GetFileName(img)), true);
        }
        var caminho = Path.Combine(pasta, caso + ".json");
        File.WriteAllText(caminho, d.ToJsonString(), new UTF8Encoding(false));
        return caminho;
    }

    string DadosModelo()
    {
        Directory.CreateDirectory(pasta);
        foreach (var nome in new[] { "exemplo_mapa.png", "exemplo_figura.png", "dados_modelo.json" })
            File.WriteAllBytes(Path.Combine(pasta, nome), ModeloOficial.LerArquivo(nome));
        return Path.Combine(pasta, "dados_modelo.json");
    }

    async Task<ResultadoComposicao> ComporAsync(string dados)
    {
        var r = RelatorioRenderer.Renderizar(dados, new OpcoesRender(false, true), Modelo);
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        await f.Host.Documento.CarregarAsync(r, ConfigFolhas.PadraoDeDados(Modelo, dados));
        return await f.Host.Documento.ComporDetalhadoAsync();
    }

    async Task<JsonElement> Js(string expressao)
    {
        var r = await f.Host.CdpAsync("Runtime.evaluate", JsonSerializer.Serialize(new { expression = expressao, returnByValue = true, awaitPromise = true }));
        using var doc = JsonDocument.Parse(r);
        if (doc.RootElement.TryGetProperty("exceptionDetails", out var ex)) throw new InvalidOperationException("JS: " + ex.GetRawText());
        return doc.RootElement.GetProperty("result").GetProperty("value").Clone();
    }
}
