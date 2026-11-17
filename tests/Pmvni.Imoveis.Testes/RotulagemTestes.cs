using System.Diagnostics;
using Pmvni.Imoveis;
using Xunit;

namespace Pmvni.Imoveis.Testes;

/// <summary>A colocação de rótulos do mapa sem sobreposição (Rotulagem): nada se cobre, a prioridade manda, o balão escolhe o lado e o
/// que não cabe fica oculto.</summary>
public class RotulagemTestes
{
    const double L = 1680, A = 1050;

    static CandidatoRotulo C(double x, double y, double w = 120, double h = 30, double ang = 0) => new(new PontoTela(x, y), w, h, ang);

    [Fact]
    public void nenhum_rotulo_posto_cobre_outro_nem_sai_da_tela()
    {
        // 400 rótulos pseudoaleatórios (semente fixa), com giro e 9 posições cada, em área apertada
        var rnd = new Random(17);
        var pedidos = Enumerable.Range(0, 400).Select(i =>
        {
            var c = new PontoTela(rnd.NextDouble() * L, rnd.NextDouble() * A);
            double w = 60 + rnd.NextDouble() * 220, h = 24 + rnd.NextDouble() * 16, ang = rnd.Next(4) == 0 ? rnd.NextDouble() * 180 - 90 : 0;
            var cands = Rotulagem.AoRedor(c, w, h, 6).Select(k => k with { Angulo = ang }).ToList();
            return new PedidoRotulo("r" + i, rnd.Next(5), cands);
        }).ToList();
        var postos = Rotulagem.Colocar(L, A, pedidos);
        Assert.NotEmpty(postos);
        Assert.True(postos.Count < pedidos.Count); // a tela não comporta todos: alguns ficaram ocultos
        for (var i = 0; i < postos.Count; i++)
        {
            Assert.All(postos[i].Contorno, v => Assert.True(v.X >= 2 && v.X <= L - 2 && v.Y >= 2 && v.Y <= A - 2));
            for (var j = i + 1; j < postos.Count; j++)
                Assert.False(Rotulagem.Sobrepoe(postos[i].Contorno, postos[j].Contorno), $"{postos[i].Id} cobre {postos[j].Id}");
        }
    }

    [Fact]
    public void prioridade_vence_a_ordem_de_chegada()
    {
        // o nome do confrontante chega antes, mas a rua (prioridade maior) fica com o lugar
        var postos = Rotulagem.Colocar(L, A,
        [
            new PedidoRotulo("dono", Rotulagem.PrioridadeLote, [C(500, 500)]),
            new PedidoRotulo("rua", Rotulagem.PrioridadeRua, [C(510, 505)]),
        ]);
        Assert.Equal("rua", Assert.Single(postos).Id);
    }

    [Fact]
    public void sem_lugar_tenta_a_proxima_posicao_e_depois_some()
    {
        var obstaculo = new Caixa(400, 400, 700, 600).Contorno();
        var postos = Rotulagem.Colocar(L, A,
        [
            new PedidoRotulo("muda", Rotulagem.PrioridadeLote, [C(500, 500), C(900, 500)]),       // a 1ª posição está ocupada
            new PedidoRotulo("some", Rotulagem.PrioridadeLote, [C(550, 450), C(10, 10)]),         // ocupada e fora da tela
            new PedidoRotulo("fora", Rotulagem.PrioridadeOutro, [C(L - 20, A / 2, 200)]),          // cortado pela borda: não entra
        ], [obstaculo]);
        var p = Assert.Single(postos);
        Assert.Equal(("muda", 1), (p.Id, p.Indice));
    }

    [Fact]
    public void quadro_com_grade_e_rapido_com_muitos_rotulos()
    {
        var q = new QuadroRotulos(L, A);
        var rnd = new Random(3);
        var sw = Stopwatch.StartNew();
        var postos = 0;
        for (var i = 0; i < 3000; i++)
            if (q.Tentar([C(rnd.NextDouble() * L, rnd.NextDouble() * A, 40 + rnd.NextDouble() * 80, 20)]) >= 0) postos++;
        Assert.True(postos > 100);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void balao_fica_abaixo_do_lote_quando_cabe_e_nunca_cobre_o_lote()
    {
        var lote = new Caixa(800, 300, 900, 380);
        var (c, lado) = Rotulagem.EscolherBalao(lote, 500, 160, L, A, [], [], 32, 32);
        Assert.Equal(LadoBalao.Abaixo, lado);
        Assert.False(c.Intersecta(lote.Expandida(31)));
        Assert.True(c.X0 >= 32 && c.Y0 >= 32 && c.X1 <= L - 32 && c.Y1 <= A - 32);
    }

    [Fact]
    public void balao_vai_para_cima_quando_o_lote_esta_no_pe_da_tela()
    {
        var lote = new Caixa(800, 880, 900, 980);
        var (c, lado) = Rotulagem.EscolherBalao(lote, 500, 160, L, A, [], [], 32, 32);
        Assert.Equal(LadoBalao.Acima, lado);
        Assert.False(c.Intersecta(lote.Expandida(31)));
        Assert.True(c.Y0 >= 32);
    }

    [Fact]
    public void balao_evita_os_nomes_e_os_proibidos()
    {
        var lote = new Caixa(800, 300, 900, 380);
        // um nome logo abaixo do lote e a etiqueta vermelha do lote acima: o balão vai para um lado
        var nomeAbaixo = new Caixa(500, 420, 1200, 600).Contorno();
        var etiqueta = new Caixa(780, 200, 920, 290);
        var (c, lado) = Rotulagem.EscolherBalao(lote, 500, 160, L, A, [etiqueta], [nomeAbaixo], 32, 32);
        Assert.Contains(lado, new[] { LadoBalao.Direita, LadoBalao.Esquerda, LadoBalao.Acima });
        Assert.False(c.Intersecta(etiqueta));
        Assert.False(Rotulagem.Sobrepoe(c.Contorno(), nomeAbaixo));
        Assert.False(c.Intersecta(lote.Expandida(31))); // o ponto do lote (na borda) fica sempre à vista
    }

    [Fact]
    public void sobreposicao_considera_o_giro()
    {
        var a = Rotulagem.CaixaGirada(new PontoTela(100, 100), 200, 20, 45);
        var b = Rotulagem.CaixaGirada(new PontoTela(100, 100), 200, 20, -45);
        var longe = Rotulagem.CaixaGirada(new PontoTela(100, 170), 60, 20, 45); // dentro do envoltório de a, fora do retângulo girado
        Assert.True(Rotulagem.Sobrepoe(a, b));
        Assert.False(Rotulagem.Sobrepoe(a, longe));
    }
}

public class InscricoesTestes
{
    [Theory]
    [InlineData("01011180077001", "01011180077001")]
    [InlineData("01.01.118.0077.001", "01011180077001")]
    [InlineData("01020100139001(D.A)", "01020100139001")]
    [InlineData("03030090166001m", "03030090166001")]
    [InlineData("0201041118001", "02010410118001")]       // lote com 3 dígitos
    [InlineData("010201400218001", "01020140218001")]     // lote com 5 dígitos (um zero a mais)
    [InlineData("01010520076001001", "01010520076001")]   // a unidade repetida
    [InlineData("010450048002", null)]                    // 12 dígitos: ambíguo
    [InlineData("RESERVA FLORESTAL", null)]
    [InlineData("", null)]
    public void normaliza_a_chave_da_inscricao(string entrada, string? esperado) => Assert.Equal(esperado, Inscricoes.Normalizar(entrada));

    [Fact]
    public void terreno_e_formatacao()
    {
        Assert.Equal("02010410118", Inscricoes.Terreno("0201041118001"));
        Assert.Equal("01.01.118.0077.001", Inscricoes.Formatar("01011180077001"));
    }
}

/// <summary>A ligação cadastro × desenho com as chaves fora do padrão, a inscrição anterior e as inscrições baixadas.</summary>
public class VinculoTestes : IDisposable
{
    readonly string pasta = BaseFicticia.Criar();
    readonly BaseQgis b;

    public VinculoTestes() => b = BaseQgis.Carregar(pasta);

    public void Dispose()
    {
        try { Directory.Delete(pasta, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    RegistroCadastro Reg(string insc) => Assert.Single(b.Cadastro.PorInscricao(insc));

    [Fact]
    public void lote_gravado_com_tres_digitos_no_desenho_e_achado()
    {
        Assert.Equal(VinculoMapa.Confirmado, b.VinculoRapido(BaseFicticia.Lote6));
        Assert.Equal(VinculoMapa.Confirmado, b.VinculoRapido("01.02.003.0006.001"));
    }

    [Fact]
    public void unidade_de_condominio_cai_no_lote_do_terreno()
    {
        Assert.Equal(VinculoMapa.MesmoTerreno, b.VinculoRapido("01020030004002"));
    }

    [Fact]
    public void registro_renumerado_usa_o_lote_da_inscricao_anterior_sem_ser_conclusivo()
    {
        var r = Reg(BaseFicticia.Renumerada);
        Assert.Equal(VinculoMapa.SemLote, b.VinculoRapido(r.InscricaoMunicipal));
        Assert.Equal(VinculoMapa.InscricaoAnterior, b.VinculoRapido(r));
        var a = b.Analisar(r);
        Assert.NotNull(a.Lote);
        Assert.False(a.Conclusivo);
    }

    [Fact]
    public void baixada_sem_lote_tem_selo_neutro_e_indica_a_sucessora()
    {
        var r = Reg(BaseFicticia.Baixada);
        var v = b.VinculoRapido(r);
        Assert.Equal(VinculoMapa.SemLote, v);
        Assert.Equal(BaseFicticia.Sucessora, Assert.Single(b.Cadastro.Sucessoras(r.InscricaoMunicipal)).InscricaoMunicipal);
        var selo = TextosConsulta.Selo(r, v, BaseFicticia.Sucessora);
        Assert.Equal(("Inscrição baixada", TomVinculo.Neutro, BaseFicticia.Sucessora), (selo.Texto, selo.Tom, selo.Sucessora));
        Assert.Contains(BaseFicticia.Sucessora, selo.Dica);
        // ativa sem lote continua com o selo vermelho
        Assert.Equal(TomVinculo.Erro, TextosConsulta.Selo(Reg(BaseFicticia.Sucessora), VinculoMapa.SemLote).Tom);
    }
}

public class NomesDoMapaTestes
{
    [Fact]
    public void dono_nao_identificado_vira_texto_certo_e_nome_longo_nao_e_cortado_no_meio()
    {
        Assert.Equal("Proprietário não identificado", MapaLocalizacao.Curto("PROPRIETARIO NAO IDENTIFICADO"));
        Assert.Equal("Não identificado", MapaLocalizacao.Abreviado("Proprietário não identificado"));
        Assert.Equal("O. Souza", MapaLocalizacao.Abreviado("Osmar Souza"));
        var longo = MapaLocalizacao.Curto("MARIACRISTINAFERNANDA DE OLIVEIRASANTOSPEREIRA");
        Assert.True(longo.Length <= 24);
        Assert.Equal("Joanilson Galavote Moreira E…", MapaLocalizacao.Encurtar("Joanilson Galavote Moreira E Socio (A) (S)", 30));
    }
}

/// <summary>As contas das cotas do mapa (lados do lote, lado de fora, leitura do texto, largura da rua).</summary>
public class CotasTestes
{
    static NetTopologySuite.Geometries.Coordinate K(double x, double y) => new(x, y);

    [Fact]
    public void lados_juntam_trechos_minusculos_e_quase_alinhados()
    {
        // retângulo 30 × 40 com um vértice a mais no meio de um lado (alinhado), um quase alinhado (1°) e um trecho de 0,2 m num canto
        var anel = new[] { K(0, 0), K(15, 0), K(30, 0.26), K(30, 0.2), K(30, 40), K(0, 40), K(0, 0) };
        var lados = Cotas.Lados(anel);
        Assert.Equal(4, lados.Count);
        Assert.Equal([30.0, 30.0, 40.0, 40.0], lados.Select(l => Math.Round(l.Comprimento, 0)).OrderBy(x => x));
        Assert.Equal(140, lados.Sum(l => l.Comprimento), 0);
    }

    [Fact]
    public void normal_aponta_para_fora_e_texto_nunca_de_cabeca_para_baixo()
    {
        var q = NetTopologySuite.Geometries.GeometryFactory.Default.CreatePolygon([K(0, 0), K(0, 10), K(10, 10), K(10, 0), K(0, 0)]);
        var baixo = new LadoCota(K(0, 0), K(10, 0), 10);
        Assert.Equal((0.0, -1.0), Cotas.NormalParaFora(baixo, q));
        var baixoInvertido = new LadoCota(K(10, 0), K(0, 0), 10);
        Assert.Equal((0.0, -1.0), Cotas.NormalParaFora(baixoInvertido, q));
        foreach (var (a, c) in new[] { (new PontoTela(100, 100), new PontoTela(0, 100)), (new PontoTela(0, 0), new PontoTela(0, 100)), (new PontoTela(0, 0), new PontoTela(-50, 60)) })
        {
            var ang = Cotas.AnguloLegivel(a, c);
            Assert.InRange(ang, -90, 90);
        }
        Assert.Equal(0, Cotas.AnguloLegivel(new PontoTela(100, 100), new PontoTela(0, 100)), 6);
    }

    [Fact]
    public void largura_da_rua_de_alinhamento_a_alinhamento_ou_so_meia()
    {
        // lote com a frente em y = 0; eixo da rua em y = −6; do outro lado, um lote começando em y = −12,4
        var outro = NetTopologySuite.Geometries.GeometryFactory.Default.CreatePolygon([K(-10, -30), K(-10, -12.4), K(10, -12.4), K(10, -30), K(-10, -30)]);
        var inteira = Cotas.LarguraDaRua(K(0, 0), K(0, -6), [outro])!;
        Assert.True(inteira.Inteira);
        Assert.Equal(12.4, inteira.Metros, 6);
        var meia = Cotas.LarguraDaRua(K(0, 0), K(0, -6), [])!;
        Assert.False(meia.Inteira);
        Assert.Equal(6, meia.Metros, 6);
        Assert.Null(Cotas.LarguraDaRua(K(0, 0), K(0, 0), [outro])); // eixo dentro do lote: sem cota
    }
}
