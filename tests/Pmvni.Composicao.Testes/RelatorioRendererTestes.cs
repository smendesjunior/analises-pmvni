using System.Text;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Composicao.Testes;

public class RelatorioRendererTestes : IDisposable
{
    private readonly string _pasta = Path.Combine(Path.GetTempPath(), "pmvni-render-" + Guid.NewGuid().ToString("N"));
    private readonly ModeloCarregado _modelo = ModeloOficial.Carregar();

    public RelatorioRendererTestes() => Directory.CreateDirectory(_pasta);

    public void Dispose()
    {
        try { Directory.Delete(_pasta, true); } catch (IOException) { /* limpeza do temporário: não derruba o teste */ }
    }

    /// <summary>Grava dados_modelo.json (e as imagens que ele cita) na pasta temporária, depois de aplicar <paramref name="ajuste"/> ao texto.</summary>
    private string GravarModelo(Func<string, string>? ajuste = null)
    {
        File.WriteAllBytes(Path.Combine(_pasta, "exemplo_mapa.png"), ModeloOficial.LerArquivo("exemplo_mapa.png"));
        File.WriteAllBytes(Path.Combine(_pasta, "exemplo_figura.png"), ModeloOficial.LerArquivo("exemplo_figura.png"));
        var json = Encoding.UTF8.GetString(ModeloOficial.LerArquivo("dados_modelo.json"));
        var caminho = Path.Combine(_pasta, "dados.json");
        File.WriteAllText(caminho, ajuste is null ? json : ajuste(json), new UTF8Encoding(false));
        return caminho;
    }

    /// <summary>Troca só a n-ésima (1ª = 1) ocorrência de <paramref name="de"/>.</summary>
    private static string TrocarNesima(string texto, string de, string por, int n)
    {
        var pos = -1;
        for (var i = 0; i < n; i++)
        {
            pos = texto.IndexOf(de, pos + 1, StringComparison.Ordinal);
            Assert.True(pos >= 0, "trecho não encontrado: " + de);
        }
        return texto[..pos] + por + texto[(pos + de.Length)..];
    }

    [Theory]
    [InlineData("terra_vetorial", false, true)]
    [InlineData("terra_vetorial", true, false)]
    [InlineData("relatorio_pdf", false, false)]
    public void legenda_do_mapa_aparece_uma_vez_abaixo_da_imagem(string origem, bool incorporada, bool exibirLegenda)
    {
        var arquivo = GravarModelo(json =>
        {
            var dados = System.Text.Json.Nodes.JsonNode.Parse(json)!;
            var loc = dados["localizacao"]!;
            loc["origem_grafica"] = origem;
            loc["legenda_na_imagem"] = incorporada;
            return dados.ToJsonString();
        });
        var r = RelatorioRenderer.Renderizar(arquivo, new OpcoesRender(false, true), _modelo);
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        var mapa = r.Html.IndexOf("<div class=\"lmapa\">", StringComparison.Ordinal);
        var detalhes = r.Html.IndexOf("<div class=\"lside\">", mapa, StringComparison.Ordinal);
        var legenda = "<div class=\"lleg\">";
        Assert.Equal(exibirLegenda ? 1 : 0, r.Html.Split(legenda).Length - 1);
        if (exibirLegenda) Assert.Contains(legenda, r.Html[mapa..detalhes]);
    }

    [Fact]
    public void tabela_de_vaos_distingue_conta_nominal_da_conclusao_tecnica()
    {
        var r = RelatorioRenderer.Renderizar(GravarModelo(), new OpcoesRender(false, true), _modelo);
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        Assert.Contains("<span class=\"pill g\"><i></i>Conta nominal confere</span>", r.Html);
        Assert.Contains("overflow-wrap: anywhere; word-break: break-word", r.Html);
    }

    [Fact]
    public void refs_por_ordem_com_id_ausente()
    {
        // 1º item com id próprio, 2º sem id, 3º com id vazio; 1ª figura com id.
        const string tituloItem = "\"titulo\": \"[Título curto do item]\"";
        var caminho = GravarModelo(j =>
        {
            var t = TrocarNesima(j, tituloItem, tituloItem + ", \"id\": \"IT-A\"", 1);
            t = TrocarNesima(t, tituloItem, tituloItem + ", \"id\": \"\"", 3);
            return TrocarNesima(t, "\"prancha\": \"ARQ XX/XX\"", "\"prancha\": \"ARQ XX/XX\", \"id\": \"FIG-A\"", 1);
        });
        var r = RelatorioRenderer.Renderizar(caminho, new OpcoesRender(Instrucoes: false, Rascunho: true), _modelo);

        Assert.True(r.Ok, string.Join("; ", r.Erros));
        var cards = r.Refs.Where(x => x.Seletor == ".card").ToList();
        Assert.Equal(new[] { "IT-A", "item-2", "item-3" }, cards.Select(c => c.Id));
        Assert.Equal(new[] { 0, 1, 2 }, cards.Select(c => c.Indice));
        var figs = r.Refs.Where(x => x.Seletor == ".fig").ToList();
        Assert.Single(figs);
        Assert.Equal("FIG-A", figs[0].Id);
    }

    [Fact]
    public void refs_usam_item_n_quando_falta_id()
    {
        var caminho = GravarModelo();
        var r = RelatorioRenderer.Renderizar(caminho, new OpcoesRender(false, true), _modelo);
        Assert.True(r.Ok, string.Join("; ", r.Erros));
        Assert.Equal(new[] { "item-1", "item-2", "item-3" }, r.Refs.Where(x => x.Seletor == ".card").Select(c => c.Id));
        Assert.Equal(new[] { "figura-1" }, r.Refs.Where(x => x.Seletor == ".fig").Select(c => c.Id));
    }

    [Fact]
    public void refs_de_secoes_em_ordem_com_rotulo_do_h2()
    {
        var r = RelatorioRenderer.Renderizar(GravarModelo(), new OpcoesRender(false, true), _modelo);
        var secoes = r.Refs.Where(x => x.Seletor == ".sec").ToList();
        Assert.Equal(
            new[]
            {
                "LOCALIZAÇÃO NA BASE MUNICIPAL", "ANÁLISE", "ÍNDICES URBANÍSTICOS", "DIAGRAMA DOS ÍNDICES",
                "ILUMINAÇÃO E VENTILAÇÃO", "IMAGENS DO PROJETO", "FONTES E REFERÊNCIAS", "CONCLUSÃO",
            },
            secoes.Select(s => s.Rotulo));
        Assert.Equal(Enumerable.Range(1, secoes.Count).Select(n => "secao-" + n), secoes.Select(s => s.Id));
        Assert.Equal(Enumerable.Range(0, secoes.Count), secoes.Select(s => s.Indice));
    }

    [Fact]
    public void html_nao_contem_data_ref()
    {
        var r = RelatorioRenderer.Renderizar(GravarModelo(), new OpcoesRender(false, true), _modelo);
        Assert.True(r.Ok);
        Assert.DoesNotContain("data-ref", r.Html);
        Assert.StartsWith("<!doctype html>", r.Html);
    }

    [Fact]
    public void erros_de_conferencia_impedem_render()
    {
        // O modelo cru tem textos entre colchetes: sem rascunho, a conferência recusa.
        var r = RelatorioRenderer.Renderizar(GravarModelo(), new OpcoesRender(false, false), _modelo);
        Assert.False(r.Ok);
        Assert.NotEmpty(r.Erros);
        Assert.Contains(r.Erros, e => e.StartsWith("Campos ainda não preenchidos"));
        Assert.Equal("", r.Html);
        Assert.Empty(r.Refs);
    }

    [Fact]
    public void imagem_ausente_e_erro_de_conferencia()
    {
        var caminho = GravarModelo();
        File.Delete(Path.Combine(_pasta, "exemplo_figura.png"));
        var r = RelatorioRenderer.Renderizar(caminho, new OpcoesRender(false, true), _modelo);
        Assert.False(r.Ok);
        Assert.Contains(r.Erros, e => e.Contains("imagem não encontrada"));
    }

    [Fact]
    public void instrucoes_acrescentam_os_blocos_amarelos()
    {
        var sem = RelatorioRenderer.Renderizar(GravarModelo(), new OpcoesRender(false, true), _modelo);
        var com = RelatorioRenderer.Renderizar(GravarModelo(), new OpcoesRender(true, false), _modelo);
        Assert.True(com.Ok, string.Join("; ", com.Erros));
        Assert.DoesNotContain("Como usar este modelo", sem.Html);
        Assert.Contains("Como usar este modelo", com.Html);
    }
}
