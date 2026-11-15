using Xunit;

namespace Pmvni.Revisao.Testes;

public class CitacoesTestes
{
    private static readonly IReadOnlyList<string> Ids = Enumerable.Range(1, 60).Select(n => "#" + n).ToList();

    [Theory]
    [InlineData("Ver o item 07.", "Ver o item {{ref:#7:2}}.")]
    [InlineData("ITEM 3 e Item 12", "ITEM {{ref:#3:1}} e Item {{ref:#12:2}}")]
    [InlineData("conforme item 007, abaixo", "conforme item {{ref:#7:3}}, abaixo")]
    [InlineData("item 60", "item {{ref:#60:2}}")]
    [InlineData("(item 5)", "(item {{ref:#5:1}})")]
    public void citacao_inequivoca_vira_token(string de, string para)
    {
        var (texto, convertidas) = Citacoes.Converter(de, Ids);
        Assert.Equal(para, texto);
        Assert.NotEmpty(convertidas);
    }

    [Theory]
    [InlineData("ver item ３")]   // dígito largo (fullwidth): não é ASCII, fica literal
    [InlineData("ver item ٣")]   // dígito arábico-índico
    [InlineData("item 1３")]
    public void digito_nao_ascii_fica_literal_sem_excecao(string de)
    {
        var (texto, convertidas) = Citacoes.Converter(de, Ids);
        Assert.Equal(de, texto);
        Assert.Empty(convertidas);
    }

    [Fact]
    public void token_com_largura_nao_ascii_fica_literal_na_impressao()
    {
        const string texto = "ver item {{ref:#1:３}}";
        Assert.Equal(texto, Citacoes.Imprimir(texto, _ => 1, _ => 1));
    }

    [Theory]
    [InlineData("ver {{ref:A:99999999999}}")]   // estoura int
    [InlineData("ver {{ref:A:5000}}")]           // largura fora de 1..3 (evitaria "D5000" gigante)
    [InlineData("ver {{ref:A:0}}")]
    [InlineData("ver {{ref:A:4}}")]
    public void token_com_largura_fora_de_1_a_3_fica_literal(string texto)
    {
        Assert.Equal(texto, Citacoes.Imprimir(texto, _ => 1, _ => 1));
    }

    [Fact]
    public void token_de_id_desconhecido_fica_literal()
    {
        const string texto = "ver {{ref:abc:2}} e {{ref:A:2}}";
        Assert.Equal("ver {{ref:abc:2}} e 01", Citacoes.Imprimir(texto, id => id == "A" ? 1 : null, id => id == "A" ? 1 : null));
    }

    [Fact]
    public void convertida_descreve_trecho_numero_e_alvo()
    {
        var (_, convertidas) = Citacoes.Converter("Ver o item 07.", Ids);
        var c = Assert.Single(convertidas);
        Assert.Equal(new CitacaoConvertida("item 07", 7, "#7"), c);
    }

    [Fact]
    public void varias_citacoes_no_mesmo_texto()
    {
        var (texto, convertidas) = Citacoes.Converter("item 1, item 2; ITEM 3", Ids);
        Assert.Equal("item {{ref:#1:1}}, item {{ref:#2:1}}; ITEM {{ref:#3:1}}", texto);
        Assert.Equal(new[] { 1, 2, 3 }, convertidas.Select(c => c.Numero));
    }

    [Theory]
    [InlineData("item 3 do art. 5")]
    [InlineData("itens 03 a 07")]
    [InlineData("item 99")]
    [InlineData("subitem 2")]
    [InlineData("item 3.2")]
    [InlineData("item 0")]
    [InlineData("item 4 da NBR 9050")]
    [InlineData("item 4, da NBR 9050")]
    [InlineData("item 4 de lei")]
    [InlineData("item 1234")]
    [InlineData("item 3,5")]
    [InlineData("iTEM 3")]
    [InlineData("item 3a")]
    [InlineData("item")]
    public void citacoes_ambiguas_ficam_literais(string texto)
    {
        var (saida, convertidas) = Citacoes.Converter(texto, Ids);
        Assert.Equal(texto, saida);
        Assert.Empty(convertidas);
    }

    [Fact]
    public void limite_superior_e_o_numero_de_itens()
    {
        var tres = Ids.Take(3).ToList();
        Assert.Equal("item {{ref:#3:1}}", Citacoes.Converter("item 3", tres).Texto);
        Assert.Equal("item 4", Citacoes.Converter("item 4", tres).Texto);
    }

    [Fact]
    public void imprimir_respeita_largura_e_excluido()
    {
        Assert.Equal("item 03", Citacoes.Imprimir("item {{ref:A:2}}", id => id == "A" ? 3 : null, _ => 99));
        Assert.Equal("item 07", Citacoes.Imprimir("item {{ref:A:2}}", _ => null, id => id == "A" ? 7 : 99));
        Assert.Equal("ITEM 3 e Item 012", Citacoes.Imprimir("ITEM {{ref:A:1}} e Item {{ref:B:3}}", id => id == "A" ? 3 : 12, _ => 0));
        Assert.Equal("item 123", Citacoes.Imprimir("item {{ref:A:2}}", _ => 123, _ => 0)); // a largura é só o mínimo
        Assert.Equal("sem token", Citacoes.Imprimir("sem token", _ => 1, _ => 1));
    }

    [Fact]
    public void converter_e_imprimir_com_a_mesma_numeracao_devolvem_o_texto_original()
    {
        var original = "Ver item 07, ITEM 3, Item 12 e item 3 do art. 5; itens 03 a 07.";
        var (texto, _) = Citacoes.Converter(original, Ids);
        var numero = Ids.Select((id, i) => (id, n: i + 1)).ToDictionary(x => x.id, x => x.n);
        Assert.Equal(original, Citacoes.Imprimir(texto, id => numero[id], id => numero[id]));
    }

    [Fact]
    public void literais_suspeitos_sao_os_item_nn_que_nao_viraram_token()
    {
        var (texto, _) = Citacoes.Converter("item 3 do art. 5; itens 03 a 07; item 08; item 99", Ids);
        Assert.Equal(new[] { "item 3", "itens 03", "item 99" }, Citacoes.LiteraisSuspeitos(texto));
        Assert.Empty(Citacoes.LiteraisSuspeitos("nada por aqui, subitem 2, item {{ref:A:2}}"));
    }
}
