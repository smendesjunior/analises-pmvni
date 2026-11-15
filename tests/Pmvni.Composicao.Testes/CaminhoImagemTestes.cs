using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Composicao.Testes;

public class CaminhoImagemTestes
{
    const string Base = @"C:\processos\0001 - 2099\analise";

    [Theory]
    [InlineData(@"C:\Windows\x.png")]
    [InlineData(@"\Windows\x.png")]
    [InlineData(@"C:x.png")]
    [InlineData(@"\\host\share\x.png")]
    [InlineData("//host/share/x.png")]
    [InlineData(@"\\?\C:\x.png")]
    [InlineData(@"\\.\pipe\x")]
    [InlineData("img/x.png:fluxo")]
    [InlineData("")]
    public void absoluto_de_rede_ou_com_dois_pontos_e_recusado_sempre(string rel)
    {
        Assert.Null(CaminhoImagem.Resolver(Base, rel, soNaPasta: false));
        Assert.Null(CaminhoImagem.Resolver(Base, rel, soNaPasta: true));
    }

    [Fact]
    public void relativo_resolve_na_pasta_e_o_ponto_ponto_so_fora_do_modo_estrito()
    {
        Assert.Equal(Path.Combine(Base, "img", "x.png"), CaminhoImagem.Resolver(Base, "img/x.png", soNaPasta: true));
        Assert.Equal(@"C:\processos\0001 - 2099\renders\mapa.png", CaminhoImagem.Resolver(Base, "../renders/mapa.png", soNaPasta: false));
        Assert.Null(CaminhoImagem.Resolver(Base, "../renders/mapa.png", soNaPasta: true));
        Assert.Null(CaminhoImagem.Resolver(Base, "img/../../x.png", soNaPasta: true));
    }

    [Fact]
    public void pasta_do_processo_nao_sobe_mais_que_tres_niveis()
    {
        // Um dados.json de processo antigo (sem .pmvni-ai) fundo numa árvore cuja raiz tem .pmvni-ai: o limite não vira a raiz inteira.
        var raiz = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "saida", "composicao", "subida-" + Guid.NewGuid().ToString("N"));
        raiz = Path.GetFullPath(raiz);
        try
        {
            Directory.CreateDirectory(Path.Combine(raiz, ".pmvni-ai"));
            var fundo = Path.Combine(raiz, "a", "b", "c", "d");
            Directory.CreateDirectory(fundo);
            Assert.Null(CaminhoImagem.Resolver(fundo, "../../../../outro/x.png", soNaPasta: false));
            // Até três níveis acima, a pasta com .pmvni-ai continua valendo (o caso do kit: a pasta analise dentro de .pmvni-ai).
            var analise = Path.Combine(raiz, "a", "b");
            Assert.Equal(Path.Combine(raiz, "outro", "x.png"), CaminhoImagem.Resolver(analise, "../../outro/x.png", soNaPasta: false));
        }
        finally { try { Directory.Delete(raiz, true); } catch (IOException) { } }
    }
}
