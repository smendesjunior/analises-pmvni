using System.Text.RegularExpressions;
using Pmvni.Composicao.Web.Legislacao;
using Xunit;

namespace Pmvni.Web.Testes;

public sealed class MensagensLegislacaoTestes
{
    /// <summary>Os tipos da ponte (MensagensLegislacao.cs) e os do TypeScript (legislacao/mensagens.ts) são os mesmos, nos dois sentidos.</summary>
    [Fact]
    public void tipos_da_ponte_iguais_nos_dois_lados()
    {
        var ts = File.ReadAllText(Path.Combine(Ambiente.RaizRepositorio(), "web", "paginador", "src", "legislacao", "mensagens.ts"));
        static List<string> Lista(string fonte, string nome)
        {
            var m = Regex.Match(fonte, nome + @"\s*=\s*\[(?<itens>[^\]]*)\]\s*as const");
            Assert.True(m.Success, "lista " + nome + " não encontrada em legislacao/mensagens.ts");
            return Regex.Matches(m.Groups["itens"].Value, "'([^']+)'").Select(x => x.Groups[1].Value).ToList();
        }
        var host = Lista(ts, "TIPOS_HOST");
        var web = Lista(ts, "TIPOS_WEB");
        var hostLeitor = Lista(ts, "TIPOS_HOST_LEITOR");
        var webLeitor = Lista(ts, "TIPOS_WEB_LEITOR");
        foreach (var l in new[] { host, web, hostLeitor, webLeitor }) Assert.Equal(l.Count, l.Distinct().Count());
        // Extrator e leitor, cada um com os seus; o host aceita a união (cada página só manda os seus).
        Assert.Equal(MensagensLegislacao.TiposHostExtrator.Order(StringComparer.Ordinal), host.Order(StringComparer.Ordinal));
        Assert.Equal(MensagensLegislacao.TiposWebExtrator.Order(StringComparer.Ordinal), web.Order(StringComparer.Ordinal));
        Assert.Equal(MensagensLegislacao.TiposHostLeitor.Order(StringComparer.Ordinal), hostLeitor.Order(StringComparer.Ordinal));
        Assert.Equal(MensagensLegislacao.TiposWebLeitor.Order(StringComparer.Ordinal), webLeitor.Order(StringComparer.Ordinal));
        Assert.Equal(MensagensLegislacao.TiposHostParaWeb.Order(StringComparer.Ordinal), host.Union(hostLeitor).Order(StringComparer.Ordinal));
        Assert.Equal(MensagensLegislacao.TiposWebParaHost.Order(StringComparer.Ordinal), web.Union(webLeitor).Order(StringComparer.Ordinal));
        Assert.Equal(["cancelar", "extrair"], host.Order(StringComparer.Ordinal));
        Assert.Equal(["abrir", "fechar", "irMarcador", "irPagina", "obterSelecao", "procurar", "zoom"], hostLeitor.Order(StringComparer.Ordinal));
        Assert.Equal(["aberto", "atalho", "cspViolacao", "erro", "fechado", "paginaAtual", "paginaMudou", "procurado", "pronto", "selecao",
            "selecaoMudou", "zoomAplicado", "zoomMudou"], webLeitor.Order(StringComparer.Ordinal));
        Assert.Empty(host.Union(hostLeitor).Intersect(web.Union(webLeitor))); // nenhum tipo vale nos dois sentidos
    }

    [Fact]
    public void validacao_da_web_para_o_host()
    {
        static bool Ler(string json, out string? motivo) => MensagensLegislacao.TentarLerDaWeb(json, out _, out motivo);

        Assert.True(Ler("""{"v":1,"tipo":"pronto","id":"pronto","dados":{"versao":1}}""", out _));
        Assert.True(Ler("""{"v":1,"tipo":"textoPagina","id":"evento-1","dados":{"pagina":1,"texto":"x"}}""", out _));
        Assert.True(MensagensLegislacao.TentarLerDaWeb("""{"v":1,"tipo":"extraido","id":"abc","dados":{"paginas":3}}""", out var m, out _));
        Assert.Equal("extraido", m!.Tipo);
        Assert.Equal(3, m.Dados.GetProperty("paginas").GetInt32());

        Assert.False(Ler("""{"v":2,"tipo":"pronto","id":"x","dados":{}}""", out var motivo));
        Assert.Contains("versao", motivo);
        Assert.False(Ler("""{"v":1,"tipo":"desconhecido","id":"x","dados":{}}""", out motivo));
        Assert.Contains("tipo desconhecido", motivo);
        Assert.False(Ler("""{"v":1,"tipo":"extrair","id":"x","dados":{}}""", out motivo)); // tipo do sentido oposto
        Assert.Contains("tipo desconhecido", motivo);
        Assert.False(Ler("""{"v":1,"tipo":"pronto","dados":{}}""", out motivo));
        Assert.Contains("\"id\"", motivo);
        Assert.False(Ler("""{"v":1,"tipo":"pronto","id":"x"}""", out motivo));
        Assert.Contains("\"dados\"", motivo);
        Assert.False(Ler("""{"v":1,"tipo":"pronto","id":"x","dados":[]}""", out _));
        Assert.False(Ler("não é json", out motivo));
        Assert.Contains("json", motivo);
        Assert.False(Ler("[1]", out _));
        // Os tipos do paginador (Etapa 1) não valem na ponte da legislação.
        Assert.False(Ler("""{"v":1,"tipo":"composto","id":"x","dados":{}}""", out _));
    }

    [Fact]
    public void serializacao_do_host_para_a_web()
    {
        var json = MensagensLegislacao.SerializarParaWeb("extrair", "id-1", new { url = "https://leg.pmvni.invalid/pdf/x.pdf" });
        Assert.Equal("""{"v":1,"tipo":"extrair","id":"id-1","dados":{"url":"https://leg.pmvni.invalid/pdf/x.pdf"}}""", json);
        Assert.Throws<ArgumentException>(() => MensagensLegislacao.SerializarParaWeb("pronto", "id", new { }));
        Assert.Throws<ArgumentException>(() => MensagensLegislacao.SerializarParaWeb("compor", "id", new { }));
        Assert.Throws<ArgumentException>(() => MensagensLegislacao.SerializarParaWeb("extrair", "", new { }));
    }
}
