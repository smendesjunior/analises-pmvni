using Pmvni.Verificador;
using Xunit;

namespace Pmvni.Web.Testes;

/// <summary>Tarefa 9: leitura da linha de comando do Pmvni.Verificador (comandos da suíte de fidelidade).</summary>
public sealed class VerificadorArgumentosTestes
{
    [Theory]
    [InlineData("html")]
    [InlineData("plano")]
    [InlineData("pdf")]
    [InlineData("texto")]
    public void comandos_de_documento_leem_dados_saida_e_opcoes(string nome)
    {
        var c = Argumentos.Ler([nome, "d.json", "s.out", "--instrucoes"]);
        Assert.Null(c.Erro);
        Assert.Equal(nome, c.Nome);
        Assert.Equal("d.json", c.Dados);
        Assert.Equal("s.out", c.Saida);
        Assert.True(c.Instrucoes);
        Assert.False(c.Rascunho);

        var r = Argumentos.Ler([nome, "--rascunho", "d.json", "s.out"]);
        Assert.Null(r.Erro);
        Assert.True(r.Rascunho);
        Assert.False(r.Instrucoes);
    }

    [Fact]
    public void capturas_le_o_dpi()
    {
        var c = Argumentos.Ler(["capturas", "d.json", "pasta", "--dpi", "150"]);
        Assert.Null(c.Erro);
        Assert.Equal("capturas", c.Nome);
        Assert.Equal("pasta", c.Saida);
        Assert.Equal(150, c.Dpi);

        Assert.Equal(150, Argumentos.Ler(["capturas", "--dpi", "150", "d.json", "pasta", "--rascunho"]).Dpi);
        Assert.Equal(96, Argumentos.Ler(["capturas", "d.json", "pasta"]).Dpi); // padrão: 96 dpi (1 px CSS = 1 px)
    }

    [Theory]
    [InlineData("capturas", "d.json", "pasta", "--dpi")]          // dpi sem valor
    [InlineData("capturas", "d.json", "pasta", "--dpi", "abc")]   // dpi não numérico
    [InlineData("capturas", "d.json", "pasta", "--dpi", "10")]    // fora de 24..1200
    [InlineData("capturas", "d.json", "pasta", "--dpi", "5000")]
    [InlineData("pdf", "d.json", "s.pdf", "--dpi", "150")]        // --dpi só em capturas
    [InlineData("pdf", "d.json")]                                 // falta a saída
    [InlineData("pdf", "d.json", "s.pdf", "extra")]               // posicional a mais
    [InlineData("texto", "d.json", "s.json", "--desconhecida")]   // opção desconhecida
    [InlineData("nada", "d.json", "s.json")]                      // comando desconhecido
    [InlineData("autoteste-pdf")]                                 // falta a saída
    [InlineData("autoteste-falhas", "x")]
    public void argumentos_invalidos_dao_erro(params string[] args)
    {
        var c = Argumentos.Ler(args);
        Assert.NotNull(c.Erro);
        Assert.Contains("Uso:", Argumentos.Uso);
    }

    [Fact]
    public void via_pacote_so_no_html()
    {
        var h = Argumentos.Ler(["html", "d.json", "s.html", "--via-pacote", "--instrucoes"]);
        Assert.Null(h.Erro);
        Assert.True(h.ViaPacote);
        Assert.True(h.Instrucoes);
        Assert.False(Argumentos.Ler(["html", "d.json", "s.html"]).ViaPacote);

        foreach (var nome in new[] { "pdf", "plano", "texto", "capturas" })
            Assert.NotNull(Argumentos.Ler([nome, "d.json", "s.out", "--via-pacote"]).Erro);
    }

    [Fact]
    public void sem_argumentos_da_erro() => Assert.NotNull(Argumentos.Ler([]).Erro);

    [Fact]
    public void autotestes_continuam()
    {
        var a = Argumentos.Ler(["autoteste-pdf", "ok.pdf"]);
        Assert.Null(a.Erro);
        Assert.Equal("ok.pdf", a.Saida);
        Assert.Null(Argumentos.Ler(["autoteste-falhas"]).Erro);
    }

    [Fact]
    public void despacho_exige_tipo_dados_saida_e_data()
    {
        var d = Argumentos.Ler(["despacho", "correcao", "d.json", "s.docx", "--data", "2099-03-10"]);
        Assert.Null(d.Erro);
        Assert.Equal("despacho", d.Nome);
        Assert.Equal("correcao", d.TipoDespacho);
        Assert.Equal("d.json", d.Dados);
        Assert.Equal("s.docx", d.Saida);
        Assert.Equal(new DateOnly(2099, 3, 10), d.Data);
        Assert.Null(d.Kit);
        Assert.Empty(d.Intervencoes);
        Assert.Null(d.Area);

        Assert.NotNull(Argumentos.Ler(["despacho", "correcao", "d.json", "s.docx"]).Erro);                       // sem --data
        Assert.NotNull(Argumentos.Ler(["despacho", "correcao", "d.json", "--data", "2099-03-10"]).Erro);         // sem saída
        Assert.NotNull(Argumentos.Ler(["despacho", "d.json", "s.docx", "--data", "2099-03-10"]).Erro);           // sem tipo
        Assert.NotNull(Argumentos.Ler(["despacho", "correcao", "d.json", "s.docx", "--data", "10/03/2099"]).Erro);
        Assert.NotNull(Argumentos.Ler(["despacho", "correcao", "d.json", "s.docx", "--data"]).Erro);
        Assert.NotNull(Argumentos.Ler(["despacho", "correcao", "d.json", "s.docx", "--data", "2099-03-10", "--rascunho"]).Erro);
        Assert.NotNull(Argumentos.Ler(["pdf", "d.json", "s.pdf", "--data", "2099-03-10"]).Erro);                 // --data só no despacho
    }

    [Fact]
    public void despacho_aceita_intervencoes_repetidas_e_area()
    {
        var d = Argumentos.Ler(["despacho", "aprovacao", "d.json", "s.docx", "--intervencao", "obra-nova", "--data", "2099-03-10",
            "--intervencao", "ampliacao", "--area", "210,52", "--kit", @"C:\kit ficticio"]);
        Assert.Null(d.Erro);
        Assert.Equal("aprovacao", d.TipoDespacho);
        Assert.Equal(new[] { "obra-nova", "ampliacao" }, d.Intervencoes);
        Assert.Equal("210,52", d.Area);
        Assert.Equal(@"C:\kit ficticio", d.Kit);

        Assert.NotNull(Argumentos.Ler(["despacho", "aprovacao", "d.json", "s.docx", "--data", "2099-03-10", "--intervencao", "reforma"]).Erro);
        Assert.NotNull(Argumentos.Ler(["despacho", "aprovacao", "d.json", "s.docx", "--data", "2099-03-10", "--area"]).Erro);
    }

    [Fact]
    public void despacho_recusa_tipo_desconhecido()
    {
        var d = Argumentos.Ler(["despacho", "aprovacao_completa", "d.json", "s.docx", "--data", "2099-03-10"]);
        Assert.Equal("Tipo de despacho desconhecido: aprovacao_completa (use correcao ou aprovacao).", d.Erro);
    }
}
