using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Textos da barra de título do app (Tarefa 8): título da janela e estado de gravação.</summary>
public class LogicaAppTestes
{
    [Fact]
    public void titulo_da_janela()
    {
        Assert.Equal("Proc. 9001/2026 · FULANO", TituloJanela.De("9001/2026", "FULANO", "x"));
        Assert.Equal("Proc. 9001/2026 · FULANO DE TAL", TituloJanela.De(" 9001/2026 ", " FULANO DE TAL ", "x"));
        Assert.Equal("Proc. 9001/2026", TituloJanela.De("9001/2026", null, "x"));
        Assert.Equal("FULANO", TituloJanela.De("", "FULANO", "x"));
        Assert.Equal("longo", TituloJanela.De(null, null, "longo"));
        Assert.Equal("longo", TituloJanela.De("  ", "", "longo"));
    }

    [Fact]
    public void estado_de_gravacao_em_texto()
    {
        Assert.Equal("Salvo 14:32", EstadoGravacaoTexto.Texto(EstadoGravacao.Salvo, new DateTime(2026, 11, 14, 14, 32, 10)));
        Assert.Equal("Salvo 09:05", EstadoGravacaoTexto.Texto(EstadoGravacao.Salvo, new DateTime(2026, 11, 14, 9, 5, 0)));
        Assert.Equal("Salvo", EstadoGravacaoTexto.Texto(EstadoGravacao.Salvo, null));
        Assert.Equal("Salvando…", EstadoGravacaoTexto.Texto(EstadoGravacao.Salvando, null));
        Assert.Equal("Não salvo", EstadoGravacaoTexto.Texto(EstadoGravacao.NaoSalvo, DateTime.Now));
        Assert.Equal("Conflito", EstadoGravacaoTexto.Texto(EstadoGravacao.Conflito, null));
        Assert.Equal("Somente leitura", EstadoGravacaoTexto.Texto(EstadoGravacao.SomenteLeitura, null));
    }
}
