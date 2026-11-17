using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Correções 2026-11-16: editar à mão o valor de uma linha de tabela com critério numérico recalcula a situação e o texto da
/// diferença, sem IA, num só Desfazer. Defeito do usuário: ÍNDICE 05 Recuo frontal, "2,80 m" → "3 m" com exigido "≥ 3,00 m" continuava
/// NÃO ATENDE e "Faltam 0,20 m".</summary>
public class RecalculoLinhaTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    /// <summary>O relatório "longo" com a linha ÍNDICE 05 trocada pela do usuário (Recuo frontal).</summary>
    private static SessaoRevisao ComRecuo(string projeto = "2,80 m", string limite = "≥ 3,00 m", string situacao = "NAO_ATENDE", string texto = "Faltam 0,20 m (item 03).")
    {
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        var linha = dados["indices"]!["linhas"]![4]!.AsObject();
        linha["nome"] = "Recuo frontal";
        linha["descricao"] = "Distância da testada à edificação.";
        linha["calculo"] = "cotado 2,80 m";
        linha["projeto"] = projeto;
        linha["limite"] = limite;
        linha["situacao"] = situacao;
        linha["texto"] = texto;
        var c = Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto());
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    const string L = "indices/05";

    private static JsonObject Gerada(Projeto p) => JsonFiel.Ler(DadosEfetivos.Gerar(p).Json)["indices"]!["linhas"]![4]!.AsObject();

    // ---------- o defeito ----------

    [Fact]
    public void tres_metros_com_minimo_de_tres_passa_a_atender()
    {
        var s = ComRecuo();
        var antes = ProjetoJson.Escrever(s.Projeto);

        var e = RecalculoLinha.AoEditar(s.Projeto, L, "projeto", "3 m");
        Assert.True(e.Recalculado);
        Assert.False(e.NaoInterpretado);
        var r = s.Aplicar(e.Comando);

        Assert.NotNull(r);
        Assert.Equal(nameof(Lote), r.Tipo);
        Assert.Equal("3 m", TabelasRevisao.TextoEfetivo(s.Projeto, L, "projeto"));
        Assert.Equal("CONTA_CONFERE", TabelasRevisao.SituacaoEfetiva(s.Projeto, L));          // impresso: ATENDE
        Assert.Equal("ATENDE", TabelasRevisao.SituacaoDeItem(TabelasRevisao.SituacaoEfetiva(s.Projeto, L)));
        Assert.Equal("Dentro do limite.", TabelasRevisao.TextoEfetivo(s.Projeto, L, "texto"));
        // A situação mudou: a justificativa (exigida para exportar) diz que foi o editor que recalculou.
        Assert.StartsWith(RecalculoLinha.MarcaJustificativa, TabelasRevisao.Justificativa(s.Projeto, L));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
        Assert.Contains("recalculado pelo editor (sem IA)", e.Mensagem);
        Assert.Contains("situação ATENDE", e.Mensagem);
        Assert.StartsWith("Índice 05 · Recalculado: 3 m atende a ≥ 3,00 m", DescricaoHistorico.Texto(r, s.Projeto));
        // O dados.json efetivo (o que vai para a folha e o PDF) sai recalculado.
        var g = Gerada(s.Projeto);
        Assert.Equal("3 m", g["projeto"]!.GetValue<string>());
        Assert.Equal("CONTA_CONFERE", g["situacao"]!.GetValue<string>());
        Assert.Equal("Dentro do limite.", g["texto"]!.GetValue<string>());

        // Um Desfazer volta o valor, a situação, o texto e a justificativa.
        s.Desfazer();
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
        Assert.False(s.PodeDesfazer);
    }

    [Theory]
    [InlineData("3 m", true, "Dentro do limite.")]
    [InlineData("3,00 m", true, "Dentro do limite.")]
    [InlineData("3.00 m", true, "Dentro do limite.")]          // ponto decimal
    [InlineData("3,5 m", true, "Dentro do limite.")]
    [InlineData("300 cm", true, "Dentro do limite.")]          // comprimento em outra unidade
    [InlineData("2,90 m", false, "Faltam 0,10 m (item 03).")]   // continua não atendendo: a citação acompanha
    [InlineData("2.9 m", false, "Faltam 0,10 m (item 03).")]
    [InlineData("2 m", false, "Falta 1,00 m (item 03).")]
    [InlineData("120 cm", false, "Faltam 1,80 m (item 03).")]
    public void minimo_em_metros(string valor, bool atende, string texto)
    {
        var s = ComRecuo();
        var e = RecalculoLinha.AoEditar(s.Projeto, L, "projeto", valor);
        s.Aplicar(e.Comando);
        Assert.Equal(atende ? "CONTA_CONFERE" : "NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(s.Projeto, L));
        Assert.Equal(texto, TabelasRevisao.TextoEfetivo(s.Projeto, L, "texto"));
        Assert.True(e.Recalculado);
    }

    [Theory]
    [InlineData("≤ 60%", "58%", true, "Dentro do limite.")]
    [InlineData("≤ 60%", "60,0%", true, "Dentro do limite.")]
    [InlineData("≤ 60%", "65%", false, "Excede em 5%.")]
    [InlineData("≤ 60,00%", "62,5 %", false, "Excede em 2,50%.")]
    [InlineData("máximo 60%", "61%", false, "Excede em 1%.")]
    [InlineData("até 2 pavimentos", "3 pavimentos", false, "Excede em 1 pav.")]
    [InlineData("≥ 72,00 m²", "70,50 m²", false, "Faltam 1,50 m².")]
    [InlineData("≥ 72,00 m²", "1.072,00 m²", true, "Dentro do limite.")]   // ponto de milhar
    [InlineData("mínimo 72 m2", "72 m²", true, "Dentro do limite.")]
    [InlineData("mín. 1,50 m", "1,20 m", false, "Faltam 0,30 m.")]
    [InlineData("no mínimo 15%", "12%", false, "Faltam 3%.")]
    [InlineData("entre 1,50 e 3,00 m", "2 m", true, "Dentro do limite.")]
    [InlineData("1,50 a 3,00 m", "3,20 m", false, "Excede em 0,20 m.")]
    [InlineData("de 1,50 m a 3,00 m", "1 m", false, "Faltam 0,50 m.")]
    [InlineData(">= 3 m", "3 m", true, "Dentro do limite.")]
    public void maximo_minimo_faixa_e_unidades(string limite, string valor, bool atende, string texto)
    {
        var s = ComRecuo("0", limite, "PENDENTE", "Faltam 9 m.");
        var e = RecalculoLinha.AoEditar(s.Projeto, L, "projeto", valor);
        Assert.True(e.Recalculado, e.Mensagem);
        s.Aplicar(e.Comando);
        Assert.Equal(atende ? "CONTA_CONFERE" : "NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(s.Projeto, L));
        Assert.Equal(texto, TabelasRevisao.TextoEfetivo(s.Projeto, L, "texto"));
    }

    // ---------- o que não se interpreta não se inventa ----------

    [Theory]
    [InlineData("≥ 3,00 m", "três metros")]              // valor sem número
    [InlineData("≥ 3,00 m", "2,80 a 3,10 m")]            // dois números: qual vale?
    [InlineData("conforme a zona", "3 m")]               // limite sem critério
    [InlineData("3,00 m", "3 m")]                        // limite sem dizer se é mínimo ou máximo
    [InlineData("≥ 3,00 m", "3 m²")]                     // unidades que não combinam
    [InlineData("≤ 60%", "3 m")]
    public void criterio_ilegivel_mantem_a_situacao_e_sugere_a_ia(string limite, string valor)
    {
        var s = ComRecuo("2,80 m", limite);
        var e = RecalculoLinha.AoEditar(s.Projeto, L, "projeto", valor);
        Assert.False(e.Recalculado);
        Assert.True(e.NaoInterpretado);
        Assert.IsType<DefinirCampo>(e.Comando);
        Assert.Contains("não conseguiu comparar", e.Mensagem);
        Assert.Contains("Rever com a IA", e.Mensagem);
        s.Aplicar(e.Comando);
        Assert.Equal(valor, TabelasRevisao.TextoEfetivo(s.Projeto, L, "projeto"));
        Assert.Equal("NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(s.Projeto, L));
        Assert.Equal("Faltam 0,20 m (item 03).", TabelasRevisao.TextoEfetivo(s.Projeto, L, "texto"));
    }

    [Fact]
    public void outros_textos_e_outras_tabelas_nao_recalculam()
    {
        var s = ComRecuo();
        foreach (var campo in new[] { "nome", "descricao", "calculo", "texto" })
        {
            var e = RecalculoLinha.AoEditar(s.Projeto, L, campo, "10 m");
            Assert.IsType<DefinirCampo>(e.Comando);
            Assert.False(e.Recalculado);
            Assert.False(e.NaoInterpretado);
            Assert.Null(e.Mensagem);
        }
        Assert.IsType<DefinirCampo>(RecalculoLinha.AoEditar(s.Projeto, "chamadas/01", "texto", "3 m").Comando);
        // O mesmo valor de antes: nada a recalcular (e nada a dizer).
        Assert.Null(RecalculoLinha.AoEditar(s.Projeto, L, "projeto", "2,80 m").Mensagem);
        // Texto vazio: a edição é recusada pelo comando de sempre, sem recálculo no caminho.
        var vazio = RecalculoLinha.AoEditar(s.Projeto, L, "projeto", "  ");
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(vazio.Comando));
    }

    // ---------- limite editado, texto livre, justificativa ----------

    [Fact]
    public void editar_o_limite_tambem_recalcula()
    {
        var s = ComRecuo();
        var e = RecalculoLinha.AoEditar(s.Projeto, L, "limite", "≥ 2,50 m");
        Assert.True(e.Recalculado);
        s.Aplicar(e.Comando);
        Assert.Equal("CONTA_CONFERE", TabelasRevisao.SituacaoEfetiva(s.Projeto, L));
        Assert.Equal("Dentro do limite.", TabelasRevisao.TextoEfetivo(s.Projeto, L, "texto"));
    }

    [Fact]
    public void situacao_igual_com_texto_livre_fica_como_esta()
    {
        // Continua não atendendo e o texto não é um texto de diferença: o editor não apaga o que o analista escreveu.
        var s = ComRecuo(texto: "A cota da planta diverge da implantação.");
        var e = RecalculoLinha.AoEditar(s.Projeto, L, "projeto", "2,90 m");
        Assert.False(e.Recalculado);
        Assert.IsType<DefinirCampo>(e.Comando);
        s.Aplicar(e.Comando);
        Assert.Equal("A cota da planta diverge da implantação.", TabelasRevisao.TextoEfetivo(s.Projeto, L, "texto"));
        Assert.Null(TabelasRevisao.Justificativa(s.Projeto, L));
    }

    [Fact]
    public void justificativa_do_usuario_fica_e_a_do_editor_acompanha_a_situacao()
    {
        var s = ComRecuo();
        s.Aplicar(new DefinirJustificativa(L, "Cota conferida na prancha 02."));
        s.Aplicar(RecalculoLinha.AoEditar(s.Projeto, L, "projeto", "3 m").Comando);
        Assert.Equal("Cota conferida na prancha 02.", TabelasRevisao.Justificativa(s.Projeto, L));

        var t = ComRecuo();
        t.Aplicar(RecalculoLinha.AoEditar(t.Projeto, L, "projeto", "3 m").Comando);
        Assert.Equal("Recalculado pelo editor: no projeto 3 m; exigido ≥ 3,00 m.", TabelasRevisao.Justificativa(t.Projeto, L));
        // Voltou a não atender (a situação original): a justificativa do editor sai junto.
        t.Aplicar(RecalculoLinha.AoEditar(t.Projeto, L, "projeto", "2,95 m").Comando);
        Assert.Equal("NAO_ATENDE", TabelasRevisao.SituacaoEfetiva(t.Projeto, L));
        Assert.Null(TabelasRevisao.Justificativa(t.Projeto, L));
        Assert.Equal("Faltam 0,05 m.", TabelasRevisao.TextoEfetivo(t.Projeto, L, "texto"));
        Assert.Empty(ConferenciaRevisao.Erros(t.Projeto));
    }

    // ---------- leitura de números ----------

    [Theory]
    [InlineData("3", 3, 0)]
    [InlineData("3,00", 3, 2)]
    [InlineData("3.00", 3, 2)]
    [InlineData("0,5", 0.5, 1)]
    [InlineData("1.234", 1234, 0)]
    [InlineData("1.234,56", 1234.56, 2)]
    [InlineData("12.345.678", 12345678, 0)]
    [InlineData("-2,5", -2.5, 1)]
    public void numeros_com_virgula_e_ponto(string texto, double valor, int casas)
    {
        var n = RecalculoLinha.LerNumero(texto);
        Assert.NotNull(n);
        Assert.Equal((decimal)valor, n.Value.Valor);
        Assert.Equal(casas, n.Value.Casas);
    }

    [Fact]
    public void numeros_ambiguos_nao_sao_lidos()
    {
        foreach (var t in new[] { "", "abc", "1,2,3", "1,234.5", "1.2.3", "3 m" }) Assert.Null(RecalculoLinha.LerNumero(t));
        Assert.Equal(new Medida(360m, "m²", 2), RecalculoLinha.LerMedida("360,00 m²"));
        Assert.Equal(new Medida(52m, "%", 0), RecalculoLinha.LerMedida("52%"));
        Assert.Equal(new Medida(2.8m, "m", 2), RecalculoLinha.LerMedida("cotado 2,80 m"));
        Assert.Equal(new Medida(2m, "pav", 0), RecalculoLinha.LerMedida("2 pavimentos"));
        Assert.Null(RecalculoLinha.LerMedida("12,00 ÷ 6 = 2,00 m²"));
        Assert.Null(RecalculoLinha.LerCriterio("≥ três"));
        Assert.Equal("1.234,50 m²", RecalculoLinha.Formatar(1234.5m, 2, "m²"));
        Assert.Equal("5%", RecalculoLinha.Formatar(5m, 0, "%"));
    }
}
