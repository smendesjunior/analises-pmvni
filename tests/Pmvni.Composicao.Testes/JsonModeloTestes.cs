using System.Numerics;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Composicao.Testes;

public class JsonModeloTestes
{
    [Fact]
    public void inteiros_viram_long_e_decimais_viram_double()
    {
        Assert.Equal(12L, Assert.IsType<long>(JsonModelo.Ler("12")));
        var d = Assert.IsType<double>(JsonModelo.Ler("12.0"));
        Assert.Equal("12.0", PythonTexto.Str(d));
        Assert.Equal("12", PythonTexto.Str(JsonModelo.Ler("12")));
        Assert.Equal(1e5, Assert.IsType<double>(JsonModelo.Ler("1E5")));
        Assert.Equal("100000.0", PythonTexto.Str(JsonModelo.Ler("1e5")));
        Assert.Equal(-3L, Assert.IsType<long>(JsonModelo.Ler("-3")));
        Assert.Equal(0L, Assert.IsType<long>(JsonModelo.Ler("-0")));
    }

    [Fact]
    public void inteiro_gigante_vira_biginteger()
    {
        var v = Assert.IsType<BigInteger>(JsonModelo.Ler("123456789012345678901234567890"));
        Assert.Equal("123456789012345678901234567890", PythonTexto.Str(v));
        Assert.Equal(long.MaxValue, Assert.IsType<long>(JsonModelo.Ler("9223372036854775807")));
    }

    [Fact]
    public void ordem_das_chaves_e_ultima_duplicada_vence_na_posicao_da_primeira()
    {
        var o = Assert.IsType<Dictionary<string, object?>>(JsonModelo.Ler("{\"b\":1,\"a\":2,\"b\":3}"));
        Assert.Equal(new[] { "b", "a" }, o.Keys.ToArray());
        Assert.Equal(3L, o["b"]);
    }

    [Fact]
    public void tipos_basicos_e_aninhamento()
    {
        var o = Assert.IsType<Dictionary<string, object?>>(JsonModelo.Ler("\ufeff{\"s\":\"á\\u00e9\\n\",\"t\":true,\"f\":false,\"n\":null,\"l\":[1,[2.5],{}]}"));
        Assert.Equal("áé\n", o["s"]);
        Assert.Equal(true, o["t"]);
        Assert.Equal(false, o["f"]);
        Assert.Null(o["n"]);
        var l = Assert.IsType<List<object?>>(o["l"]);
        Assert.Equal(1L, l[0]);
        Assert.Equal(2.5, Assert.IsType<double>(Assert.IsType<List<object?>>(l[1])[0]));
        Assert.Empty(Assert.IsType<Dictionary<string, object?>>(l[2]));
    }

    [Fact]
    public void json_invalido_lanca()
    {
        Assert.ThrowsAny<Exception>(() => JsonModelo.Ler("{\"a\":}"));
        Assert.ThrowsAny<Exception>(() => JsonModelo.Ler("[1,]"));
    }
}
