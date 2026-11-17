using Pmvni.App;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Correções 2026-11-16 (image9): "adicionar que o afastamento frontal de 120 cm não atende" pelo pedido da volumetria. Antes a IA
/// só podia mudar textos de linhas que já existiam (a resposta com outra chave era recusada) e pôs a frase no destaque vermelho; a lista
/// de chamadas continuava com 5 e 6. Agora a resposta traz "novas_chamadas" e o editor cria a chamada 7, com a marca no desenho.</summary>
public class ChamadaNovaTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar() =>
        Importador.Importar(File.ReadAllBytes(Ambiente.Corpus("longo.json")), Ambiente.Corpus("longo.json"), Modelo, Ambiente.Contexto());

    [Fact]
    public void resposta_da_ia_com_chamada_nova_e_lida()
    {
        var ids = new[] { "volumetria/01", "chamadas/05", "chamadas/06" };
        var saida = """
            {"volumetria/01": {"destaque": "Afastamento de frente de 120 cm não atende."},
             "novas_chamadas": [
               {"ancora": "frente", "titulo": "Afastamento frontal", "texto": "120 cm; o mínimo é 3,00 m.", "situacao": "NAO_ATENDE"},
               {"ancora": "porão", "titulo": "X", "texto": "Y", "situacao": "NAO_ATENDE"},
               {"ancora": "lote", "titulo": "", "texto": "Y", "situacao": "NAO_ATENDE"},
               {"ancora": "lote", "titulo": "Z", "texto": "Y", "situacao": "TALVEZ"}]}
            """;
        // Antes da correção esta resposta era inválida (a chave "novas_chamadas" não é id de linha): nada mudava.
        var textos = IaRecalculoIndices.Ler(saida, ids);
        Assert.NotNull(textos);
        Assert.Equal("Afastamento de frente de 120 cm não atende.", textos["volumetria/01"]["destaque"]);
        Assert.False(textos.ContainsKey("novas_chamadas"));
        // Título vazio e situação inválida ficam de fora; a âncora desconhecida fica (o editor escolhe um ponto livre na vista).
        var lidas = IaRecalculoIndices.LerNovasChamadas(saida, ids);
        Assert.Equal(2, lidas.Count);
        Assert.Equal(new IaRecalculoIndices.ChamadaNova("frente", "Afastamento frontal", "120 cm; o mínimo é 3,00 m.", "NAO_ATENDE", "volumetria"), lidas[0]);
        Assert.Equal("porão", lidas[1].Ancora);
        Assert.Equal("CONTA_CONFERE", IaRecalculoIndices.LerNovasChamadas(saida.Replace("\"NAO_ATENDE\"}", "\"ATENDE\"}"), ids)[0].Situacao);
        Assert.Null(IaRecalculoIndices.Ler("{\"outra\": []}", ids));

        var pedido = IaRecalculoIndices.PromptVolumetria("adicionar que o afastamento frontal de 120 cm não atende",
            new Dictionary<string, Dictionary<string, string>> { ["volumetria/01"] = new() { ["destaque"] = "nenhum" } });
        Assert.Contains("novas_chamadas", pedido);
        Assert.Contains("frente", pedido);
        Assert.Contains("NÃO abra arquivos", pedido);
        Assert.Contains("\"vista\"", pedido);
        Assert.Contains("lateral_direita", pedido);
    }

    /// <summary>Correção 2026-11-17 (o caso do usuário): "Afastamento do muro" pedido na volumetria. A IA devolveu a âncora "lateral"
    /// (que é da planta) e a chamada 7 foi para a lista da PLANTA, com a marca no desenho da planta; a volumetria não mudou.</summary>
    [Fact]
    public void chamada_pedida_na_volumetria_com_ancora_da_planta_fica_na_volumetria()
    {
        var ids = new[] { "volumetria/01", "chamadas/05", "chamadas/06" };
        // Resposta sem "vista" (como a do caso real): vale a do desenho em que o pedido foi feito.
        var saida = """
            {"novas_chamadas": [{"ancora": "lateral", "titulo": "Afastamento do muro", "texto": "Afastamento de 100 cm indicado como incorreto.", "situacao": "NAO_ATENDE"}]}
            """;
        var nova = Assert.Single(IaRecalculoIndices.LerNovasChamadas(saida, ids));
        Assert.Equal("volumetria", nova.Vista);
        // Com "vista" explícita, a IA manda; vista inválida descarta a entrada.
        Assert.Equal("planta", IaRecalculoIndices.LerNovasChamadas(saida.Replace("{\"ancora\"", "{\"vista\": \"planta\", \"ancora\""), ids)[0].Vista);
        Assert.Empty(IaRecalculoIndices.LerNovasChamadas(saida.Replace("{\"ancora\"", "{\"vista\": \"fachada\", \"ancora\""), ids));

        var c = Importar();
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        s.Aplicar(new Lote("volumetria/01", [new AcrescentarChamada("chamadas/07", nova.Ancora, nova.Titulo, nova.Texto, nova.Situacao, nova.Vista)], "Volumetria ajustada pela IA"));
        var linha = TabelasRevisao.Original(s.Projeto, "chamadas/07")!;
        Assert.Equal("volumetria", (string?)linha["vista"]);
        Assert.Equal("lateral_direita", (string?)linha["ancora"]);   // a "lateral" da planta vira a equivalente na volumetria
        Assert.DoesNotContain("vista", TabelasRevisao.Caminhos(linha));   // a vista não é texto editável

        using var pasta = Ambiente.PastaDeTeste("chamada-vista");
        c.ExtrairAssets(pasta);
        var render = RelatorioRenderer.Renderizar(DadosEfetivos.Gerar(s.Projeto).Json, Path.Combine(pasta, "dados.json"), new OpcoesRender(false, true), Modelo);
        Assert.True(render.Ok, string.Join("; ", render.Erros));
        var html = render.Html;
        // A chamada 7 está na lista da volumetria (depois da 6), não na da planta.
        var listaPlanta = html.IndexOf("<div class=\"chip\">PLANTA DO TERRENO", StringComparison.Ordinal);
        var listaVolume = html.IndexOf("<div class=\"chip\">VOLUMETRIA DA EDIFICA", StringComparison.Ordinal);
        var item7 = html.IndexOf("<b>Afastamento do muro</b>", StringComparison.Ordinal);
        Assert.True(listaPlanta >= 0 && listaVolume > listaPlanta && item7 > listaVolume, $"planta {listaPlanta}, volumetria {listaVolume}, item 7 {item7}");
        // A marca 7 no desenho da volumetria, no ponto da lateral direita.
        var (x, y) = Pmvni.Composicao.Kit.Diagrama.PosVolumeExtra["lateral_direita"];
        var marca = html.IndexOf($"<circle cx=\"{x}\" cy=\"{y}\" r=\"8.5\"/><text x=\"{x}\"", StringComparison.Ordinal);
        Assert.True(marca > listaVolume, "a marca 7 tem de estar no desenho da volumetria");
    }

    [Fact]
    public void chamada_sem_ancora_ganha_um_ponto_livre_da_vista()
    {
        var c = Importar();
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        s.Aplicar(new Lote("volumetria/01",
        [
            new AcrescentarChamada("chamadas/07", "", "A", "X", "NAO_ATENDE", "volumetria"),
            new AcrescentarChamada("chamadas/08", "", "B", "Y", "NAO_ATENDE", "volumetria"),
            new AcrescentarChamada("chamadas/09", "", "C", "Z", "PENDENTE", "planta"),
        ], "x"));
        Assert.Equal("lateral_direita", (string?)TabelasRevisao.Original(s.Projeto, "chamadas/07")!["ancora"]);
        Assert.Equal("lateral_esquerda", (string?)TabelasRevisao.Original(s.Projeto, "chamadas/08")!["ancora"]);   // a direita já está ocupada
        Assert.Equal("planta", (string?)TabelasRevisao.Original(s.Projeto, "chamadas/09")!["vista"]);
        Assert.True(Pmvni.Composicao.Kit.Diagrama.PosPlanta.ContainsKey((string)TabelasRevisao.Original(s.Projeto, "chamadas/09")!["ancora"]!));
        // Vista desconhecida é recusada.
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarChamada("chamadas/10", "lote", "T", "X", "NAO_ATENDE", "fachada")));
    }

    [Fact]
    public void chamada_acrescentada_entra_numerada_com_a_marca_no_desenho()
    {
        var c = Importar();
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        var antes = ProjetoJson.Escrever(s.Projeto);
        Assert.Equal("chamadas/07", TabelasRevisao.ProximaChamada(s.Projeto));

        var r = s.Aplicar(new Lote("volumetria/01",
        [
            new DefinirCampo("volumetria/01", "destaque", "Afastamento de frente de 120 cm não atende."),
            new AcrescentarChamada("chamadas/07", "frente", "Afastamento frontal", "120 cm; o mínimo é 3,00 m.", "NAO_ATENDE"),
        ], "Volumetria ajustada pela IA"));

        Assert.NotNull(r);
        Assert.True(TabelasRevisao.Existe(s.Projeto, "chamadas/07"));
        Assert.True(TabelasRevisao.Acrescentado(s.Projeto, "chamadas/07"));
        Assert.Equal("chamadas/07", TabelasRevisao.Ids(s.Projeto).Last(i => i.StartsWith("chamadas/", StringComparison.Ordinal)));
        Assert.Equal("chamadas/08", TabelasRevisao.ProximaChamada(s.Projeto));
        Assert.Equal("DIAGRAMA · CHAMADA 07", TabelasRevisao.Rotulo("chamadas/07"));
        // A chamada nova se edita como as outras.
        s.Aplicar(new DefinirCampo("chamadas/07", "texto", "1,20 m; o mínimo é 3,00 m."));
        Assert.Equal("1,20 m; o mínimo é 3,00 m.", TabelasRevisao.TextoEfetivo(s.Projeto, "chamadas/07", "texto"));

        // No relatório: a chamada 7 na lista da volumetria e a marca 7 no desenho.
        using var pasta = Ambiente.PastaDeTeste("chamada-nova");
        c.ExtrairAssets(pasta);
        var render = RelatorioRenderer.Renderizar(DadosEfetivos.Gerar(s.Projeto).Json, Path.Combine(pasta, "dados.json"), new OpcoesRender(false, true), Modelo);
        Assert.True(render.Ok, string.Join("; ", render.Erros));
        Assert.Contains("<span class=\"cn\">7</span><div><div class=\"coh\"><b>Afastamento frontal</b>", render.Html);
        var (x, y) = Pmvni.Composicao.Kit.Diagrama.PosVolumeExtra["frente"];
        Assert.Contains($"<circle cx=\"{x}\" cy=\"{y}\" r=\"8.5\"/>", render.Html);

        // projeto.json vai e volta; Desfazer tira a chamada e o destaque.
        Assert.Equal(ProjetoJson.Escrever(s.Projeto), ProjetoJson.Escrever(ProjetoJson.Ler(ProjetoJson.Escrever(s.Projeto))));
        s.Desfazer();
        s.Desfazer();
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
        Assert.False(TabelasRevisao.Existe(s.Projeto, "chamadas/07"));
    }

    [Fact]
    public void chamada_fora_da_sequencia_ou_com_ancora_desconhecida_e_recusada()
    {
        var c = Importar();
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarChamada("chamadas/09", "frente", "T", "X", "NAO_ATENDE")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarChamada("chamadas/07", "porão", "T", "X", "NAO_ATENDE")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarChamada("chamadas/07", "frente", "T", "X", "ATENDE")));   // código de item, não de linha
        Assert.False(s.PodeDesfazer);
        // Duas de uma vez, na sequência; só a última sai.
        s.Aplicar(new Lote("volumetria/01", [new AcrescentarChamada("chamadas/07", "frente", "A", "X", "NAO_ATENDE"), new AcrescentarChamada("chamadas/08", "lote", "B", "Y", "PENDENTE")], "x"));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new RemoverAcrescimo("chamadas/07")));
        s.Aplicar(new RemoverAcrescimo("chamadas/08"));
        Assert.False(TabelasRevisao.Existe(s.Projeto, "chamadas/08"));
        Assert.True(TabelasRevisao.Existe(s.Projeto, "chamadas/07"));
        // O kit continua com as âncoras dele; a extra só vale para o editor.
        Assert.False(Pmvni.Composicao.Kit.Diagrama.PosVolume.ContainsKey("frente"));
    }
}
