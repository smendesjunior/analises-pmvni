using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class CacheDeIconesTestes
{
    [Fact]
    public void ReaproveitaAusenciaEExpulsaExtensaoMaisAntiga()
    {
        var cache = new CacheDeIcones<string?>(2);
        Assert.Null(cache.Obter("a", _ => null));
        Assert.Equal("b", cache.Obter("b", x => x));
        Assert.Null(cache.Obter("a", _ => throw new Exception()));
        Assert.Equal("c", cache.Obter("c", x => x));
        Assert.Equal("b", cache.Obter("b", _ => throw new Exception()));
        Assert.Equal("novo", cache.Obter("a", _ => "novo"));
    }
}
