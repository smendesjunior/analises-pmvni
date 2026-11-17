using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Correção 2026-11-17: na tabela ÍNDICES URBANÍSTICOS, editar à mão um valor que entra numa conta atualiza na hora o que
/// depende dele (resultado da conta, "No projeto", situação, as outras taxas sobre a mesma área do lote e o diagrama), sem IA e num
/// só Desfazer. Defeito do usuário: mudar a área de projeção ou a do lote no "Cálculo / descrição" não mexia em mais nada.</summary>
public class DependenciasIndicesTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    const string Terreno = "indices/01", TO = "indices/02", CA = "indices/03", TP = "indices/04", Recuo = "indices/05", Subsolo = "indices/06";
    const string Planta = "planta/01", ChamadaLote = "chamadas/01";

    /// <summary>A tabela do relatório do usuário (lote de 360,00 m², ZOC 2). <paramref name="comTerreno"/>: a 1ª linha é "Área do
    /// terreno" (360,00 m²); senão é o gabarito e a área do lote só está no subtítulo.</summary>
    private static SessaoRevisao Relatorio(bool comTerreno = false)
    {
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        var indices = dados["indices"]!.AsObject();
        indices["subtitulo"] = "Lote de 360,00 m² (matrícula) • zona ZOC 2.";
        indices["zona"] = "ZOC 2";
        var linhas = indices["linhas"]!.AsArray();
        void Linha(int i, string icone, string nome, string calculo, string projeto, string limite, string situacao, string texto)
        {
            var l = linhas[i]!.AsObject();
            l["icone"] = icone;
            l["nome"] = nome;
            l["calculo"] = calculo;
            l["projeto"] = projeto;
            l["limite"] = limite;
            l["situacao"] = situacao;
            l["texto"] = texto;
        }
        if (comTerreno) Linha(0, "plot", "Área do terreno", "Conforme matrícula.", "360,00 m²", "≥ 250,00 m²", "CONTA_CONFERE", "Dentro do limite.");
        else Linha(0, "pav", "Gabarito", "Corte AA: 2 pavimentos.", "2 pavimentos", "até 2 pavimentos", "CONTA_CONFERE", "Dentro do limite.");
        Linha(1, "to", "Taxa de ocupação", "126,40 ÷ 360,00 × 100 = 35,1%.", "35,1%", "≤ 60%", "CONTA_CONFERE", "Dentro do limite.");
        Linha(2, "ca", "Coeficiente de aproveitamento", "162,00 ÷ 360,00 = 0,45", "0,45", "≤ 1,50", "CONTA_CONFERE", "Dentro do limite.");
        Linha(3, "tp", "Taxa de permeabilidade", "10% informado no projeto; 72,00 ÷ 360,00 × 100 = 20,0%.", "10%", "≥ 15%", "DIVERGENCIA",
            "O quadro informa 10%, mas a conta dá 20,0% (item 04).");
        Linha(4, "afa", "Recuo frontal", "cotado 2,80 m", "2,80 m", "≥ 3,00 m", "NAO_ATENDE", "Faltam 0,20 m (item 03).");
        Linha(5, "to", "Ocupação do subsolo", "50,00 ÷ 360,00 × 100 = 13,9%", "13,9%", "≤ 20%", "PENDENTE", "Falta a planta do subsolo (item 06).");
        var planta = dados["diagrama"]!["planta"]!["permeavel"]!.AsObject();
        planta["linha1"] = "10% no projeto";
        planta["linha2"] = "20,0% no cálculo";
        dados["diagrama"]!["chamadas"]![0]!["texto"] = "Lote de 360,00 m² conforme matrícula (item 02).";
        var c = Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto());
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    static string? T(SessaoRevisao s, string linha, string campo) => TabelasRevisao.TextoEfetivo(s.Projeto, linha, campo);
    static string? Sit(SessaoRevisao s, string linha) => TabelasRevisao.SituacaoEfetiva(s.Projeto, linha);

    static EdicaoDeLinha Editar(SessaoRevisao s, string linha, string campo, string texto)
    {
        var e = RecalculoLinha.AoEditar(s.Projeto, linha, campo, texto);
        s.Aplicar(e.Comando);
        return e;
    }

    // ---------- a conta ----------

    [Fact]
    public void le_a_conta_no_meio_da_frase()
    {
        var c = ContaIndice.Ler("10% informado no projeto; 126,40 ÷ 360,00 × 100 = 35,1%.");
        Assert.NotNull(c);
        Assert.Equal(126.40m, c.Dividendo.Valor);
        Assert.Equal(360m, c.Divisor.Valor);
        Assert.True(c.Percentual);
        Assert.Equal(new Medida(35.1m, "%", 1), c.Resultado);
        Assert.True(c.ResultadoConfere);

        var ca = ContaIndice.Ler("162,00 ÷ 360,00 = 0,45");
        Assert.NotNull(ca);
        Assert.False(ca.Percentual);
        Assert.Equal(new Medida(0.45m, "", 2), ca.Resultado);

        Assert.NotNull(ContaIndice.Ler("126,40 m² / 360,00 m² x 100 = 35,1%"));
        Assert.Null(ContaIndice.Ler("cotado 2,80 m"));
        Assert.Null(ContaIndice.Ler("1 ÷ 0 = 0"));
        Assert.Null(ContaIndice.Ler("10 ÷ 2 = 5; 20 ÷ 4 = 5"));   // duas contas: qual vale?
    }

    [Theory]
    [InlineData("130,00 ÷ 360,00 × 100 = 35,1%", "130,00 ÷ 360,00 × 100 = 36,1%")]
    [InlineData("Projeção 126,40 ÷ 400,00 × 100 = 35,1%.", "Projeção 126,40 ÷ 400,00 × 100 = 31,6%.")]
    [InlineData("162,00 ÷ 400,00 = 0,45", "162,00 ÷ 400,00 = 0,41")]          // 0,405 → 0,41 (arredonda para cima no meio)
    [InlineData("70,00 ÷ 480,00 × 100 = 15%", "70,00 ÷ 480,00 × 100 = 15%")]   // 14,58% escrito como 15%: confere, não reescreve
    [InlineData("1.200,00 ÷ 360,00 = 3,33", "1.200,00 ÷ 360,00 = 3,33")]
    public void refaz_so_o_numero_do_resultado(string texto, string esperado) =>
        Assert.Equal(esperado, ContaIndice.Ler(texto)!.Refazer(texto));

    [Fact]
    public void troca_o_divisor_e_refaz()
    {
        const string t = "10% informado no projeto; 72,00 ÷ 360,00 × 100 = 20,0%.";
        Assert.Equal("10% informado no projeto; 72,00 ÷ 400,00 × 100 = 18,0%.", ContaIndice.Ler(t)!.TrocarDivisor(t, 400m));
        Assert.True(ContaIndice.Concorda(35m, 0, 35.1m, 1));
        Assert.False(ContaIndice.Concorda(10m, 0, 35.1m, 1));
        Assert.False(ContaIndice.Concorda(35.1m, 1, 35.3m, 1));
    }

    // ---------- área de projeção: a própria linha ----------

    [Fact]
    public void mudar_a_projecao_refaz_a_conta_o_valor_no_projeto_e_a_situacao()
    {
        var s = Relatorio();
        var antes = ProjetoJson.Escrever(s.Projeto);

        var e = Editar(s, TO, "calculo", "130,00 ÷ 360,00 × 100 = 35,1%.");
        Assert.True(e.Recalculado);
        Assert.Equal("130,00 ÷ 360,00 × 100 = 36,1%.", T(s, TO, "calculo"));
        Assert.Equal("36,1%", T(s, TO, "projeto"));
        Assert.Equal("CONTA_CONFERE", Sit(s, TO));
        Assert.Contains("conta = 36,1%", e.Mensagem);
        Assert.Contains("no projeto 36,1%", e.Mensagem);

        // Passa do limite: NÃO ATENDE, com a diferença.
        Editar(s, TO, "calculo", "230,00 ÷ 360,00 × 100 = 36,1%.");
        Assert.Equal("230,00 ÷ 360,00 × 100 = 63,9%.", T(s, TO, "calculo"));
        Assert.Equal("63,9%", T(s, TO, "projeto"));
        Assert.Equal("NAO_ATENDE", Sit(s, TO));
        Assert.Equal("Excede em 3,9%.", T(s, TO, "texto"));
        Assert.StartsWith(RecalculoLinha.MarcaJustificativa, TabelasRevisao.Justificativa(s.Projeto, TO));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));

        // O dados.json efetivo (folha e PDF) sai com tudo junto.
        var g = JsonFiel.Ler(DadosEfetivos.Gerar(s.Projeto).Json)["indices"]!["linhas"]![1]!.AsObject();
        Assert.Equal("63,9%", g["projeto"]!.GetValue<string>());
        Assert.Equal("NAO_ATENDE", g["situacao"]!.GetValue<string>());

        // Cada edição é um Desfazer só.
        s.Desfazer();
        s.Desfazer();
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
    }

    [Fact]
    public void resultado_escrito_pelo_usuario_vale()
    {
        var s = Relatorio();
        Editar(s, TO, "calculo", "130,00 ÷ 360,00 × 100 = 40%");   // mudou a projeção E o resultado: não reescreve o resultado dele
        Assert.Equal("130,00 ÷ 360,00 × 100 = 40%", T(s, TO, "calculo"));
        Assert.Equal("40%", T(s, TO, "projeto"));
    }

    [Fact]
    public void texto_sem_mudar_numeros_e_calculo_sem_conta_sao_so_a_edicao()
    {
        var s = Relatorio();
        Assert.IsType<DefinirCampo>(RecalculoLinha.AoEditar(s.Projeto, TO, "calculo", "Projeção 126,40 ÷ 360,00 × 100 = 35,1%.").Comando);
        var livre = RecalculoLinha.AoEditar(s.Projeto, TO, "calculo", "Conferir a projeção na prancha 02.");
        Assert.IsType<DefinirCampo>(livre.Comando);
        Assert.Null(livre.Mensagem);
    }

    // ---------- área do lote: as outras taxas acompanham ----------

    [Fact]
    public void mudar_a_area_do_lote_numa_conta_refaz_as_outras()
    {
        var s = Relatorio();
        var antes = ProjetoJson.Escrever(s.Projeto);

        var e = Editar(s, TO, "calculo", "126,40 ÷ 400,00 × 100 = 35,1%.");
        Assert.True(e.Recalculado);
        Assert.Equal("126,40 ÷ 400,00 × 100 = 31,6%.", T(s, TO, "calculo"));
        Assert.Equal("31,6%", T(s, TO, "projeto"));
        // Coeficiente: a conta e o valor no projeto acompanham.
        Assert.Equal("162,00 ÷ 400,00 = 0,41", T(s, CA, "calculo"));
        Assert.Equal("0,41", T(s, CA, "projeto"));
        Assert.Equal("CONTA_CONFERE", Sit(s, CA));
        // Permeabilidade: a conta muda; o valor declarado (10%) não é o da conta e fica; continua INFORMAÇÕES DIVERGENTES.
        Assert.Equal("10% informado no projeto; 72,00 ÷ 400,00 × 100 = 18,0%.", T(s, TP, "calculo"));
        Assert.Equal("10%", T(s, TP, "projeto"));
        Assert.Equal("DIVERGENCIA", Sit(s, TP));
        Assert.Equal("O quadro informa 10%, mas a conta dá 20,0% (item 04).", T(s, TP, "texto"));   // texto livre da análise fica
        // FALTA COMPROVAR fica (só o usuário resolve), mas a conta e o valor acompanham.
        Assert.Equal("50,00 ÷ 400,00 × 100 = 12,5%", T(s, Subsolo, "calculo"));
        Assert.Equal("12,5%", T(s, Subsolo, "projeto"));
        Assert.Equal("PENDENTE", Sit(s, Subsolo));
        // Linhas sem conta não mudam.
        Assert.Equal("2,80 m", T(s, Recuo, "projeto"));
        // Diagrama: a área do lote na chamada "lote" e a permeabilidade "no cálculo" da planta.
        Assert.Equal("Lote de 400,00 m² conforme matrícula (item 02).", T(s, ChamadaLote, "texto"));
        Assert.Equal("18,0% no cálculo", T(s, Planta, "permeavel/linha2"));
        Assert.Equal("10% no projeto", T(s, Planta, "permeavel/linha1"));
        // O aviso diz o que mudou e que o subtítulo não é editável.
        Assert.Contains("ÍNDICE 03: conta = 0,41", e.Mensagem);
        Assert.Contains("subtítulo", e.Mensagem);
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));

        // Um Desfazer volta tudo, em todas as linhas.
        s.Desfazer();
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
        Assert.False(s.PodeDesfazer);

        // Segunda mudança da área (400 → 420): as linhas que já acompanharam continuam ligadas.
        Editar(s, TO, "calculo", "126,40 ÷ 400,00 × 100 = 35,1%.");
        Editar(s, CA, "calculo", "162,00 ÷ 420,00 = 0,41");
        Assert.Equal("126,40 ÷ 420,00 × 100 = 30,1%.", T(s, TO, "calculo"));
        Assert.Equal("30,1%", T(s, TO, "projeto"));
        Assert.Equal("0,39", T(s, CA, "projeto"));
    }

    [Fact]
    public void mudar_a_linha_do_terreno_refaz_as_taxas()
    {
        var s = Relatorio(comTerreno: true);
        var e = Editar(s, Terreno, "projeto", "400,00 m²");
        Assert.True(e.Recalculado);
        Assert.Equal("CONTA_CONFERE", Sit(s, Terreno));
        Assert.Equal("126,40 ÷ 400,00 × 100 = 31,6%.", T(s, TO, "calculo"));
        Assert.Equal("31,6%", T(s, TO, "projeto"));
        Assert.Equal("0,41", T(s, CA, "projeto"));
        Assert.Equal("Lote de 400,00 m² conforme matrícula (item 02).", T(s, ChamadaLote, "texto"));

        // E uma conta que muda a área leva junto a linha do terreno.
        var t = Relatorio(comTerreno: true);
        Editar(t, CA, "calculo", "162,00 ÷ 380,00 = 0,45");
        Assert.Equal("380,00 m²", T(t, Terreno, "projeto"));
        Assert.Equal("126,40 ÷ 380,00 × 100 = 33,3%.", T(t, TO, "calculo"));
    }

    // ---------- valor no projeto × conta: INFORMAÇÕES DIVERGENTES ----------

    [Fact]
    public void divergencia_so_sai_quando_os_numeros_batem()
    {
        var s = Relatorio();
        // 12% ainda não é o da conta (20,0%): continua divergente, nada de ATENDE.
        var e = Editar(s, TP, "projeto", "12%");
        Assert.Equal("DIVERGENCIA", Sit(s, TP));
        Assert.Equal("12% no projeto", T(s, Planta, "permeavel/linha1"));   // o diagrama acompanha o valor
        Assert.Contains("não bate com a conta", e.Mensagem);

        // 20% bate com a conta: a comparação com o limite decide (≥ 15%: ATENDE).
        var f = Editar(s, TP, "projeto", "20%");
        Assert.True(f.Recalculado);
        Assert.Equal("CONTA_CONFERE", Sit(s, TP));
        Assert.Equal("Dentro do limite.", T(s, TP, "texto"));
        Assert.Equal("20% no projeto", T(s, Planta, "permeavel/linha1"));
        Assert.StartsWith(RecalculoLinha.MarcaJustificativa, TabelasRevisao.Justificativa(s.Projeto, TP));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
    }

    [Fact]
    public void divergencia_sem_diagrama_avisa_sem_recalcular()
    {
        var s = Relatorio();
        // Limite novo com o valor ainda diferente da conta: a situação fica e o aviso diz por quê.
        var e = RecalculoLinha.AoEditar(s.Projeto, TP, "limite", "≥ 12%");
        Assert.IsType<DefinirCampo>(e.Comando);
        Assert.False(e.Recalculado);
        Assert.False(e.NaoInterpretado);
        Assert.Contains("continua diferente do resultado da conta", e.Mensagem);
    }

    [Fact]
    public void valor_editado_numa_linha_com_conta_que_bate_recalcula_como_antes()
    {
        var s = Relatorio();
        var e = Editar(s, TO, "projeto", "65%");   // o usuário diz o valor: compara com o limite (a conta é texto da análise)
        Assert.True(e.Recalculado);
        Assert.Equal("NAO_ATENDE", Sit(s, TO));
        Assert.Equal("Excede em 5%.", T(s, TO, "texto"));
        Assert.Equal("126,40 ÷ 360,00 × 100 = 35,1%.", T(s, TO, "calculo"));   // o campo que o usuário não tocou não é reescrito
    }
}
