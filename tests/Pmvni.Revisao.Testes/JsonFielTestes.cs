using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class JsonFielTestes
{
    private static byte[] B(string s) => new UTF8Encoding(false).GetBytes(s);
    private static string Utf8(byte[] b) => new UTF8Encoding(false, true).GetString(b);

    [Fact]
    public void numeros_preservam_o_texto()
    {
        const string json = "{\"a\":12.0,\"b\":1e-05,\"c\":123456789012345678901234567890}";
        Assert.Equal(json, Utf8(JsonFiel.Escrever(JsonFiel.Ler(B(json)))));
    }

    [Fact]
    public void chave_repetida_vale_a_ultima_na_posicao_da_primeira()
    {
        Assert.Equal("{\"x\":3,\"y\":2}", Utf8(JsonFiel.Escrever(JsonFiel.Ler(B("{\"x\":1,\"y\":2,\"x\":3}")))));
    }

    [Fact]
    public void bom_aceito_e_utf8_invalido_recusado()
    {
        var comBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(B("{\"a\":1}")).ToArray();
        Assert.Equal("{\"a\":1}", Utf8(JsonFiel.Escrever(JsonFiel.Ler(comBom))));

        var invalido = B("{\"a\":\"x").Concat(new byte[] { 0xC3, 0x28 }).Concat(B("\"}")).ToArray();
        Assert.Throws<JsonException>(() => JsonFiel.Ler(invalido));
    }

    [Theory]
    [InlineData("{\"a\":1,}")]
    [InlineData("{\"a\":1 /* x */}")]
    [InlineData("{\"a\":")]
    [InlineData("{\"a\":1}x")]
    public void json_que_o_kit_nao_aceita_e_recusado(string json)
    {
        Assert.ThrowsAny<JsonException>(() => JsonFiel.Ler(B(json)));
    }

    [Fact]
    public void raiz_que_nao_e_objeto_e_recusada()
    {
        Assert.Throws<JsonException>(() => JsonFiel.Ler(B("[1]")));
    }

    [Fact]
    public void escrita_sem_bom_e_sem_escapar_o_que_o_json_nao_exige()
    {
        const string json = "{\"t\":\"a<b>&'\\\"é\\n\"}";
        var bytes = JsonFiel.Escrever(JsonFiel.Ler(B(json)));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal(json, Utf8(bytes));

        // Fora do plano básico (emoji) o escapador do .NET grava 😀: JSON válido; a leitura devolve o mesmo texto.
        var emoji = JsonFiel.Ler(JsonFiel.Escrever(JsonFiel.Ler(B("{\"t\":\"😀\"}"))));
        Assert.Equal("😀", emoji["t"]!.GetValue<string>());
    }

    [Fact]
    public void estrutura_aninhada_e_ordem_das_chaves_sobrevivem()
    {
        const string json = "{\"z\":[1,{\"b\":null,\"a\":[true,false]}],\"a\":\"\",\"m\":{}}";
        Assert.Equal(json, Utf8(JsonFiel.Escrever(JsonFiel.Ler(B(json)))));
    }

    [Fact]
    public void id_novo_tem_formato_ulid()
    {
        using var rng = RandomNumberGenerator.Create();
        var agora = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        var a = IdItem.Novo(agora, rng);
        var b = IdItem.Novo(agora, rng);
        Assert.Matches("^it_[0-9A-HJKMNP-TV-Z]{26}$", a);
        Assert.Matches("^it_[0-9A-HJKMNP-TV-Z]{26}$", b);
        Assert.NotEqual(a, b);
        Assert.True(IdItem.Valido(a));
    }

    [Fact]
    public void ids_de_instantes_posteriores_ordenam_depois()
    {
        using var rng = RandomNumberGenerator.Create();
        var t = new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        Assert.True(string.CompareOrdinal(IdItem.Novo(t, rng), IdItem.Novo(t.AddMilliseconds(1), rng)) < 0);
    }

    [Theory]
    [InlineData("it_01JD3Q7Z8K0000000000000000", true)]
    [InlineData("it_01JD3Q7Z8K000000000000000", false)]   // 25
    [InlineData("it_01JD3Q7Z8K00000000000000000", false)] // 27
    [InlineData("it_01JD3Q7Z8K000000000000000I", false)]  // I não existe em Crockford
    [InlineData("it_01jd3q7z8k0000000000000000", false)]  // minúsculas
    [InlineData("xx_01JD3Q7Z8K0000000000000000", false)]
    [InlineData("", false)]
    public void id_valido(string id, bool esperado) => Assert.Equal(esperado, IdItem.Valido(id));
}
