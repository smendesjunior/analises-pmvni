using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class DadosEfetivosTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();
    private static readonly OpcoesRender Rascunho = new(false, true);

    private static ConteudoPacote Importar(string caso)
    {
        var caminho = Ambiente.Corpus(caso + ".json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    private static JsonObject ItemGerado(DadosGerados g, int i) =>
        JsonFiel.Ler(g.Json)["itens"]!.AsArray()[i]!.AsObject();

    [Theory]
    [InlineData("curto")]
    [InlineData("longo")]
    [InlineData("extremos")]
    [InlineData("estouro")]
    [InlineData("sem_opcionais")]
    public void importado_gera_html_byte_identico(string caso)
    {
        var c = Importar(caso);
        using var pasta = Ambiente.PastaDeTeste("efetivo-" + caso);
        c.ExtrairAssets(pasta);
        var caminho = Path.Combine(pasta, "dados.json");

        var efetivo = RelatorioRenderer.Renderizar(DadosEfetivos.Gerar(c.Projeto).Json, caminho, Rascunho, Modelo);
        var kit = RelatorioRenderer.Renderizar(Ambiente.Corpus(caso + ".json"), Rascunho, Modelo);

        Assert.True(efetivo.Ok, string.Join("; ", efetivo.Erros));
        Assert.True(kit.Ok, string.Join("; ", kit.Erros));
        Assert.Equal(kit.Html, efetivo.Html);
    }

    [Fact]
    public void revisao_12_preserva_historico_e_apresentacao_ao_reabrir_pacote()
    {
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        dados["revisao_modelo_relatorio"] = 12;
        dados["historico_reanalise"] = JsonNode.Parse("""
            {"arquivo":"Despacho anterior","pendencias":[{"id":"1","pendencia":"Conferir o vão","origem":"Item anterior",
            "situacao":"Sanada","confronto":"Vão ampliado","evidencia":"Prancha de revisão"}]}
            """);
        var indice = dados["indices"]!["linhas"]![0]!;
        indice["apresentacao"] = new JsonObject { ["situacao"] = "PENDENTE", ["nota"] = "Ressalva do índice" };
        var ambiente = dados["iluminacao"]!["ambientes"]![0]!;
        ambiente["apresentacao"] = new JsonObject { ["situacao"] = "PENDENTE", ["nota"] = "Ressalva do vão" };
        var conteudo = Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto());
        using var pasta = Ambiente.PastaDeTeste("revisao-12");
        var pacote = Path.Combine(pasta, "reanalise.rpmvni");
        Pacote.Gravar(pacote, conteudo, null);
        var aberto = Pacote.Ler(pacote).Conteudo;
        aberto.ExtrairAssets(pasta);
        var efetivos = DadosEfetivos.Gerar(aberto.Projeto).Json;
        var reabertos = JsonFiel.Ler(efetivos);
        Assert.True(JsonNode.DeepEquals(dados["historico_reanalise"], reabertos["historico_reanalise"]));
        Assert.True(JsonNode.DeepEquals(indice["situacao"], reabertos["indices"]!["linhas"]![0]!["situacao"]));
        var render = RelatorioRenderer.Renderizar(efetivos, Path.Combine(pasta, "dados.json"), Rascunho, Modelo);
        Assert.True(render.Ok, string.Join(", ", render.Erros));
        Assert.Contains("PENDÊNCIAS DA ANÁLISE ANTERIOR", render.Html);
        Assert.Contains("Vão ampliado", render.Html);
        Assert.Contains("Ressalva do índice", render.Html);
        Assert.Contains("Ressalva do vão", render.Html);

        reabertos.Remove("diagrama");
        var semDiagrama = RelatorioRenderer.Renderizar(JsonFiel.Escrever(reabertos), Path.Combine(pasta, "dados.json"), new(false, false), Modelo);
        Assert.Contains("Tabela de índices sem diagrama de conferência.", semDiagrama.Erros);
    }

    [Fact]
    public void dados_modelo_com_instrucoes_gera_html_byte_identico()
    {
        using var pasta = Ambiente.PastaDeTeste("efetivo-modelo");
        File.WriteAllBytes(Path.Combine(pasta, "exemplo_mapa.png"), ModeloOficial.LerArquivo("exemplo_mapa.png"));
        File.WriteAllBytes(Path.Combine(pasta, "exemplo_figura.png"), ModeloOficial.LerArquivo("exemplo_figura.png"));
        var original = Path.Combine(pasta, "original.json");
        File.WriteAllBytes(original, ModeloOficial.LerArquivo("dados_modelo.json"));

        var c = Importador.Importar(File.ReadAllBytes(original), original, Modelo, Ambiente.Contexto());
        var destino = Path.Combine(pasta, "efetivo");
        Directory.CreateDirectory(destino);
        c.ExtrairAssets(destino);

        var opcoes = new OpcoesRender(true, true);
        var efetivo = RelatorioRenderer.Renderizar(DadosEfetivos.Gerar(c.Projeto).Json, Path.Combine(destino, "dados.json"), opcoes, Modelo);
        var kit = RelatorioRenderer.Renderizar(original, opcoes, Modelo);

        Assert.True(efetivo.Ok, string.Join("; ", efetivo.Erros));
        Assert.True(kit.Ok, string.Join("; ", kit.Erros));
        Assert.Equal(kit.Html, efetivo.Html);
    }

    /// <summary>Põe "ver item 03" (já convertido em token, como a importação faz) na verificação do primeiro item.</summary>
    private static void CitarOTerceiro(ConteudoPacote c)
    {
        var ids = c.Projeto.Itens.Select(i => i.Id).ToList();
        var (texto, convertidas) = Citacoes.Converter("ver item 03", ids);
        Assert.Single(convertidas);
        c.Projeto.Itens[0].Campos["verificacao"].Original = JsonValue.Create(texto);
    }

    [Fact]
    public void excluir_renumera_e_citacao_acompanha()
    {
        var c = Importar("curto");
        CitarOTerceiro(c);
        Assert.Equal("ver item 03", DadosEfetivos.TextoEfetivo(c.Projeto, c.Projeto.Itens[0], "verificacao"));

        c.Projeto.Itens[1].Incluido = false;
        var g = DadosEfetivos.Gerar(c.Projeto);

        Assert.Equal(2, JsonFiel.Ler(g.Json)["itens"]!.AsArray().Count);
        Assert.Equal("ver item 02", ItemGerado(g, 0)["verificacao"]!.GetValue<string>());
        Assert.Equal("ver item 02", DadosEfetivos.TextoEfetivo(c.Projeto, c.Projeto.Itens[0], "verificacao"));
        Assert.Equal(new[] { c.Projeto.Itens[0].Id, c.Projeto.Itens[2].Id }, g.IdsDosItens);
    }

    [Fact]
    public void citacao_a_excluido_imprime_o_numero_original()
    {
        var c = Importar("curto");
        CitarOTerceiro(c);
        c.Projeto.Itens[2].Incluido = false;
        var g = DadosEfetivos.Gerar(c.Projeto);
        Assert.Equal("ver item 03", ItemGerado(g, 0)["verificacao"]!.GetValue<string>());
    }

    [Fact]
    public void citacao_na_conclusao_e_no_texto_revisado_tambem_e_impressa()
    {
        var c = Importar("curto");
        var ids = c.Projeto.Itens.Select(i => i.Id).ToList();
        c.Projeto.Documento["conclusao"] = Citacoes.Converter("Ver item 03 e item 01.", ids).Texto;
        c.Projeto.Itens[1].Campos["titulo"].Revisado = "Titulo com item {{ref:" + ids[2] + ":1}}";
        c.Projeto.Itens[0].Incluido = false;

        var g = DadosEfetivos.Gerar(c.Projeto);
        var doc = JsonFiel.Ler(g.Json);

        Assert.Equal("Ver item 02 e item 01.", doc["conclusao"]!.GetValue<string>()); // o 3º agora é o 2º; o 1º, excluído, mantém o original
        Assert.Equal("Titulo com item 2", ItemGerado(g, 0)["titulo"]!.GetValue<string>());
    }

    [Fact]
    public void campo_revisado_substitui_e_campo_criado_vai_ao_fim()
    {
        var c = Importar("curto");
        var item = c.Projeto.Itens[0];
        item.Campos["titulo"].Revisado = "Titulo revisado";
        item.Situacao.Revisada = "ATENDE";
        item.Chaves.Remove("providencia");                                                                // o original não tinha "providencia"…
        item.Campos["providencia"] = new CampoTexto { Ausente = true, Revisado = "Criada pela edicao" };  // …e a edição a criou

        var o = ItemGerado(DadosEfetivos.Gerar(c.Projeto), 0);

        Assert.Equal(new[] { "titulo", "situacao", "verificacao", "base_legal", "referencia", "providencia" }, o.Select(p => p.Key));
        Assert.Equal("Titulo revisado", o["titulo"]!.GetValue<string>());
        Assert.Equal("ATENDE", o["situacao"]!.GetValue<string>());
        Assert.Equal("Criada pela edicao", o["providencia"]!.GetValue<string>());
        Assert.Equal("Titulo revisado", DadosEfetivos.TextoEfetivo(c.Projeto, item, "titulo"));
        Assert.Equal("Criada pela edicao", DadosEfetivos.TextoEfetivo(c.Projeto, item, "providencia"));
    }

    [Theory]
    [InlineData("a {{ref:abc:2}} b")]               // id que não existe
    [InlineData("a {{ref:x:99999999999}} b")]       // largura que estoura int
    [InlineData("a {{ref:x:5000}} b")]              // largura absurda
    public void token_digitado_pelo_usuario_que_nao_resolve_fica_literal_sem_excecao(string digitado)
    {
        var c = Importar("curto");
        var item = c.Projeto.Itens[0];
        item.Campos["titulo"].Revisado = digitado;

        var g = DadosEfetivos.Gerar(c.Projeto);

        Assert.Equal(digitado, ItemGerado(g, 0)["titulo"]!.GetValue<string>());
        Assert.Equal(digitado, DadosEfetivos.TextoEfetivo(c.Projeto, item, "titulo"));
    }

    [Fact]
    public void situacao_sem_revisao_fica_a_original()
    {
        var c = Importar("curto");
        var origem = JsonFiel.Ler(File.ReadAllBytes(Ambiente.Corpus("curto.json")))["itens"]![0]!["situacao"]!.GetValue<string>();
        Assert.Equal(origem, ItemGerado(DadosEfetivos.Gerar(c.Projeto), 0)["situacao"]!.GetValue<string>());
    }

    [Fact]
    public void null_continua_null_e_ausente_continua_ausente()
    {
        var c = Importar("curto");
        var item = c.Projeto.Itens[0];
        item.Campos["providencia"].Original = null;     // null no original
        item.Chaves.Remove("referencia");                // ausente no original
        item.Campos.Remove("referencia");

        var o = ItemGerado(DadosEfetivos.Gerar(c.Projeto), 0);

        Assert.True(o.ContainsKey("providencia"));
        Assert.Null(o["providencia"]);
        Assert.False(o.ContainsKey("referencia"));
        Assert.Null(DadosEfetivos.TextoEfetivo(c.Projeto, item, "providencia"));
        Assert.Null(DadosEfetivos.TextoEfetivo(c.Projeto, item, "referencia"));
    }

    [Fact]
    public void gerar_nao_altera_o_projeto()
    {
        var c = Importar("curto");
        CitarOTerceiro(c);
        var antes = ProjetoJson.Escrever(c.Projeto);
        _ = DadosEfetivos.Gerar(c.Projeto);
        Assert.Equal(antes, ProjetoJson.Escrever(c.Projeto));
    }

    [Fact]
    public void refs_de_sessao_trocam_so_os_cards()
    {
        var c = Importar("longo");
        using var pasta = Ambiente.PastaDeTeste("efetivo-refs");
        c.ExtrairAssets(pasta);
        c.Projeto.Itens[1].Incluido = false;
        var g = DadosEfetivos.Gerar(c.Projeto);
        var r = RelatorioRenderer.Renderizar(g.Json, Path.Combine(pasta, "dados.json"), Rascunho, Modelo);
        Assert.True(r.Ok, string.Join("; ", r.Erros));

        var s = DadosEfetivos.RefsDeSessao(r, g.IdsDosItens);

        Assert.Equal(r.Html, s.Html);
        Assert.Equal(r.Refs.Count, s.Refs.Count);
        Assert.Equal(r.Refs.Where(x => x.Seletor == ".sec"), s.Refs.Where(x => x.Seletor == ".sec"));
        Assert.Equal(r.Refs.Where(x => x.Seletor == ".fig"), s.Refs.Where(x => x.Seletor == ".fig"));
        var cards = s.Refs.Where(x => x.Seletor == ".card").ToList();
        Assert.Equal(59, cards.Count);
        for (var i = 0; i < cards.Count; i++)
        {
            Assert.Equal(i, cards[i].Indice);
            Assert.Equal(g.IdsDosItens[i], cards[i].Id);
        }
    }
}
