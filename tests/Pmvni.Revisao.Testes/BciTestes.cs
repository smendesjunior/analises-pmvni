using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class BciTestes
{
    sealed class GpiFalso : IAdaptadorGpi
    {
        public bool Sessao = true, Tela = true, Emitiu;
        public List<CandidatoGpi> Linhas = [new("01020390100002", "Cadastro Imobiliario")];
        public FichaGpi? Ficha = new("01020390100002", "2026", true);
        public TaskCompletionSource? Trava;
        public int Emissoes;
        public Task<bool> GarantirSessaoAsync(CancellationToken ct) => Task.FromResult(Sessao);
        public Task<bool> AbrirManutencaoAsync(CancellationToken ct) => Task.FromResult(Tela);
        public async Task<IReadOnlyList<CandidatoGpi>> PesquisarExatoAsync(string i, CancellationToken ct)
        {
            if (Trava is { } t) await t.Task.WaitAsync(ct);
            return Linhas;
        }
        public Task<FichaGpi?> AbrirConsultaAsync(string i, CancellationToken ct) => Task.FromResult(Ficha);
        public Task<byte[]?> EmitirAsync(string i, CancellationToken ct) { Emissoes++; Emitiu = true; return Task.FromResult<byte[]?>("%PDF-1.4"u8.ToArray()); }
    }

    static Task<ResultadoBci> G(GpiFalso g, string insc = "01.02.039.0100.002", CancellationToken ct = default) =>
        new CoordenadorBci(g, _ => { }).GerarAsync(insc, ct);

    [Theory]
    [InlineData("01020390100002", "01020390100002")]
    [InlineData("01.02.039.0100-002", "01020390100002")]
    [InlineData("0102039010000", null)]
    [InlineData("010203901000020", null)]
    [InlineData("0102039010000A", null)]
    [InlineData("", null)]
    public void chave_estrita_so_aceita_14_algarismos(string v, string? esperado) => Assert.Equal(esperado, ChaveBci.Estrita(v));

    [Fact]
    public async Task caminho_feliz_emite_uma_vez()
    {
        var g = new GpiFalso();
        var r = await G(g);
        Assert.Equal(EstadoBci.Aberto, r.Estado);
        Assert.Equal("2026", r.Ano);
        Assert.Equal(1, g.Emissoes);
    }

    [Fact]
    public async Task sem_sessao_nao_emite()
    {
        var g = new GpiFalso { Sessao = false };
        Assert.Equal(EstadoBci.LoginNecessario, (await G(g)).Estado);
        Assert.False(g.Emitiu);
    }

    [Fact]
    public async Task zero_e_varios_candidatos_e_outro_tipo()
    {
        Assert.Equal(EstadoBci.NaoEncontrado, (await G(new GpiFalso { Linhas = [] })).Estado);
        Assert.Equal(EstadoBci.NaoEncontrado, (await G(new GpiFalso { Linhas = [new("01020390100002", "Cadastro Mobiliario")] })).Estado);
        Assert.Equal(EstadoBci.NaoEncontrado, (await G(new GpiFalso { Linhas = [new("01020390100001", "Cadastro Imobiliario")] })).Estado); // mesmo lote, outra unidade
        var g = new GpiFalso { Linhas = [new("01020390100002", "Cadastro Imobiliario"), new("01020390100002", "01 Cadastro Imobiliario")] };
        Assert.Equal(EstadoBci.Ambiguo, (await G(g)).Estado);
        Assert.False(g.Emitiu);
    }

    [Fact]
    public async Task ficha_divergente_ou_em_edicao_nao_emite()
    {
        var g = new GpiFalso { Ficha = new("01020390100001", "2026", true) };
        Assert.Equal(EstadoBci.ConferenciaFalhou, (await G(g)).Estado);
        var h = new GpiFalso { Ficha = new("01020390100002", "2026", false) };
        Assert.Equal(EstadoBci.ConferenciaFalhou, (await G(h)).Estado);
        Assert.False(g.Emitiu || h.Emitiu);
    }

    [Fact]
    public async Task dois_cliques_simultaneos_geram_uma_emissao_e_cancelamento_libera()
    {
        var g = new GpiFalso { Trava = new() };
        var c = new CoordenadorBci(g, _ => { });
        using var cts = new CancellationTokenSource();
        var primeiro = c.GerarAsync("01020390100002", cts.Token);
        Assert.Equal(EstadoBci.Ocupado, (await c.GerarAsync("01020390100002", default)).Estado);
        cts.Cancel();
        Assert.Equal(EstadoBci.Cancelado, (await primeiro).Estado);
        g.Trava = null;
        Assert.Equal(EstadoBci.Aberto, (await c.GerarAsync("01020390100002", default)).Estado);
        Assert.Equal(1, g.Emissoes);
    }

    [Fact]
    public void cookie_do_site_pelo_dominio()
    {
        Assert.True(CofreSites.DoSite(".el.com.br", "gpi12.cloud.el.com.br"));
        Assert.True(CofreSites.DoSite("gpi12.cloud.el.com.br", "gpi12.cloud.el.com.br"));
        Assert.False(CofreSites.DoSite("essencialbpms.com.br", "gpi12.cloud.el.com.br"));
    }

    [Fact]
    public void cofre_guarda_le_e_sair_apaga()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "pmvni-cofre-" + Guid.NewGuid().ToString("N"));
        try
        {
            CofreSites.GuardarCookies(raiz, Modulo.Gpi, [new("JSESSIONID", "abc", "gpi12.cloud.el.com.br", "/", true, true, 1)]);
            CofreSites.GuardarLogin(raiz, Modulo.Gpi, new(" fulano ", "s3nha"));
            Assert.Single(CofreSites.LerCookies(raiz, Modulo.Gpi));
            Assert.Equal("fulano", CofreSites.LerLogin(raiz, Modulo.Gpi)!.Usuario);
            Assert.DoesNotContain("s3nha", File.ReadAllText(Directory.GetFiles(Path.Combine(raiz, "segredos"), "*login*")[0]));
            CofreSites.Sair(raiz, Modulo.Gpi);
            Assert.Empty(CofreSites.LerCookies(raiz, Modulo.Gpi));
            Assert.Null(CofreSites.LerLogin(raiz, Modulo.Gpi));
            Assert.True(CofreSites.Saiu(raiz, Modulo.Gpi));
            CofreSites.Voltou(raiz, Modulo.Gpi);
            Assert.False(CofreSites.Saiu(raiz, Modulo.Gpi));
        }
        finally { try { Directory.Delete(raiz, true); } catch (IOException) { } }
    }

    static PistasProcesso P(string nome, string? requerente, string? insc = null, string? texto = null) =>
        new(new Pmvni.Revisao.Processo(@"C:p\" + nome, nome, 1, 2026, requerente), insc, texto);

    [Fact]
    public void vinculo_pela_inscricao_lote_requerente_e_documentos()
    {
        const string I = "01011180077002", C = "JOANILSON GALAVOTE MOREIRA E SOCIO (A) (S)";
        Assert.Equal(VinculoBci.InscricaoExata, VinculoBci.Pontuar(I, C, P("a", "FULANO DE TAL", I)).Nota);
        Assert.Equal(VinculoBci.MesmoLote, VinculoBci.Pontuar(I, C, P("b", "FULANO DE TAL", "01011180077001")).Nota);
        Assert.Equal(VinculoBci.Requerente, VinculoBci.Pontuar(I, C, P("c", "Joanilson Galavote Moreira")).Nota);
        Assert.Equal(VinculoBci.InscricaoNoPdf, VinculoBci.Pontuar(I, C, P("d", "X Y", texto: "Inscrição: 01.01.118.0077.002 matrícula")).Nota);
        Assert.Equal(VinculoBci.NomeNoPdf, VinculoBci.Pontuar(I, C, P("e", "X Y", texto: "outorgante JOANILSON GALAVOTE MOREIRA, brasileiro")).Nota);
        Assert.Equal(0, VinculoBci.Pontuar(I, C, P("f", "OUTRA PESSOA", texto: "inscrição 010111800770021 x")).Nota);   // número maior não vale
    }

    [Fact]
    public void socios_do_cadastro_tambem_ligam_o_processo()
    {
        var campos = new Dictionary<string, string?>
        {
            ["nome_do_contribuinte"] = "FULANO DE TAL SILVA E SOCIO (A)(S)",
            ["nomes_razoes_encontrados"] = "FULANO DE TAL SILVA; BELTRANA SOUZA | beltrana souza",
            ["logradouro"] = "RUA SAO BRAZ",
        };
        var socios = VinculoBci.Socios(campos, campos["nome_do_contribuinte"]);
        Assert.Equal(["BELTRANA SOUZA"], socios);
        // A coluna real da base QGIS: nomes com CPF/CNPJ, separados por vírgula.
        var reais = VinculoBci.Socios(new Dictionary<string, string?>
        {
            ["vinculos"] = "FULANO DE TAL SILVA 00000000191, BELTRANA SOUZA LIMA 00000000272, IMOBILIARIA EXEMPLO LTDA  00.000.000/0001-91",
        }, campos["nome_do_contribuinte"]);
        Assert.Equal(["BELTRANA SOUZA LIMA", "IMOBILIARIA EXEMPLO LTDA"], reais);
        const string I = "09090090009001";
        var n = VinculoBci.Pontuar(I, campos["nome_do_contribuinte"], socios, P("g", "Beltrana Souza"));
        Assert.Equal(VinculoBci.Requerente, n.Nota);
        Assert.Contains("sócio", n.Motivo);
        Assert.Equal(VinculoBci.NomeNoPdf, VinculoBci.Pontuar(I, campos["nome_do_contribuinte"], socios, P("h", "X Y", texto: "outorgada BELTRANA SOUZA, brasileira")).Nota);
    }

    [Fact]
    public void escolha_unica_empate_e_nenhum()
    {
        var a = new NotaProcesso(P("a", null).Processo, 100, ""); var b = new NotaProcesso(P("b", null).Processo, 50, "");
        Assert.Same(a, VinculoBci.Escolher([a, b]).Escolhido);
        var (e, emp) = VinculoBci.Escolher([a, a with { Processo = b.Processo }]);
        Assert.Null(e); Assert.Equal(2, emp.Count);
        Assert.Null(VinculoBci.Escolher([a with { Nota = 0 }]).Escolhido);
    }
}
