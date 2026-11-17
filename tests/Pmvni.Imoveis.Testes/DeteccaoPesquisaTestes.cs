using Pmvni.Imoveis;
using Xunit;

namespace Pmvni.Imoveis.Testes;

/// <summary>"Detectar automaticamente": o campo da pesquisa sai do que foi digitado.</summary>
public class DeteccaoPesquisaTestes
{
    static readonly CampoPesquisa[] Campos =
    [
        new("inscricao_municipal", "Inscrição municipal", "t"), new("inscricao_reduzida", "Inscrição reduzida", "t"),
        new("nome_do_contribuinte", "Contribuinte", "t"), new("logradouro", "Logradouro", "t"), new("n_metrico", "Número", "t"),
        new("bairro", "Bairro", "t"), new("quadra_contribuinte", "Quadra", "t"), new("lote_contribuinte", "Lote", "t"),
        new("loteamento", "Loteamento", "t"), new("inscricao_municipal_anterior", "Inscrição anterior", "t"),
    ];
    static readonly string[] Bairros = ["CENTRO", "VILA BETÂNIA", "SÃO JOÃO DE VIÇOSA"];
    static readonly string[] Loteamentos = ["Loteamento Exemplo"];

    static Deteccao D(string v) => DeteccaoPesquisa.Detectar(v, Campos, Bairros, Loteamentos);
    static string[] Ids(Deteccao d) => d.Alternativas.Select(a => a.CampoId).ToArray();

    [Theory]
    [InlineData("01011180077001")]
    [InlineData("01.01.118.0077.001")]
    [InlineData("0101118 0077 001")]
    public void inscricao_completa_com_ou_sem_pontos(string v)
    {
        var d = D(v);
        Assert.Equal(["inscricao_municipal"], Ids(d));
        Assert.Equal("01011180077001", d.Alternativas[0].Valor);
        Assert.Equal("Buscando por Inscrição municipal", d.Descricao);
    }

    [Fact]
    public void codigos_curtos_vao_para_reduzida_e_numero_e_com_zero_para_quadra_ou_lote()
    {
        Assert.Equal(["inscricao_reduzida", "n_metrico"], Ids(D("8860")));
        Assert.Equal(["inscricao_reduzida", "n_metrico", "lote_contribuinte"], Ids(D("0118")));
        Assert.Equal(["inscricao_reduzida", "n_metrico", "quadra_contribuinte"], Ids(D("041")));
        Assert.Equal(["inscricao_municipal", "inscricao_reduzida", "inscricao_municipal_anterior"], Ids(D("0101118")));
    }

    [Fact]
    public void logradouro_sem_o_tipo_e_com_o_numero()
    {
        var d = D("Rua das Acácias, 120");
        Assert.Equal(["logradouro"], Ids(d));
        Assert.Equal("das Acácias", d.Alternativas[0].Valor);
        Assert.Equal(new AlvoPesquisa("n_metrico", "120"), d.Numero);
        Assert.Equal("Buscando por Logradouro, número 120", d.Descricao);
        Assert.Equal("PREFEITO NICOLAU", D("av. PREFEITO NICOLAU").Alternativas[0].Valor);
        var br = D("BR-262");
        Assert.Equal("BR-262", br.Alternativas[0].Valor);
        Assert.Null(br.Numero);
    }

    [Fact]
    public void bairro_e_loteamento_conhecidos_sem_acento_nem_caixa()
    {
        Assert.Equal(["bairro"], Ids(D("vila betania")));
        Assert.Equal(["bairro"], Ids(D("Sao Joao")));
        Assert.Equal(["loteamento"], Ids(D("loteamento exemplo")));
    }

    [Fact]
    public void nome_vai_para_contribuinte_com_reserva_em_todos_os_campos()
    {
        var d = D("Joanilson Galavote");
        Assert.Equal(["nome_do_contribuinte"], Ids(d));
        Assert.Equal("Buscando por Contribuinte", d.Descricao);
        Assert.NotNull(d.Reserva);
        Assert.Equal(Campos.Length, d.Reserva!.Alternativas.Count);
    }

    static RegistroCadastro R(long l, params (string K, string? V)[] c) =>
        new(l, c.ToDictionary(x => x.K, x => x.V, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void pesquisa_automatica_filtra_pelos_campos_detectados_e_cai_na_reserva()
    {
        RegistroCadastro[] regs =
        [
            R(1, ("inscricao_municipal", "01011180077001"), ("nome_do_contribuinte", "JOANILSON GALAVOTE MOREIRA"), ("logradouro", "ANGELO ALTOE NETO"), ("n_metrico", "120"), ("bairro", "CENTRO")),
            R(2, ("inscricao_municipal", "01011180078001"), ("nome_do_contribuinte", "OSMAR SOUZA"), ("logradouro", "ANGELO ALTOE NETO"), ("n_metrico", "130"), ("bairro", "CENTRO")),
            R(3, ("inscricao_municipal", "01020030004001"), ("nome_do_contribuinte", "CENTRO COMERCIAL LTDA"), ("logradouro", "DAS ACACIAS"), ("n_metrico", "120"), ("bairro", "VILA BETÂNIA")),
        ];
        PaginaResultado P(string v, Condicao c = Condicao.Contem) => Pesquisa.Aplicar(regs, Campos, new Consulta(DeteccaoPesquisa.Automatico, c, v));
        Assert.Equal([1L], P("01.01.118.0077.001").Itens.Select(r => r.LinhaFonte));
        Assert.Equal([1L], P("Rua Angelo Altoé Neto, 120").Itens.Select(r => r.LinhaFonte));
        Assert.Equal([1L, 2L], P("centro").Itens.Select(r => r.LinhaFonte));          // bairro, não o nome "CENTRO COMERCIAL"
        var osmar = P("osmar");
        Assert.Equal([2L], osmar.Itens.Select(r => r.LinhaFonte));
        Assert.Equal("Buscando por Contribuinte", osmar.Deteccao);
        var reserva = P("acacias");                                                    // não é nome: cai em todos os campos
        Assert.Equal([3L], reserva.Itens.Select(r => r.LinhaFonte));
        Assert.StartsWith("Nada no campo detectado", reserva.Deteccao);
        Assert.Equal([2L, 3L], P("joanilson", Condicao.NaoContem).Itens.Select(r => r.LinhaFonte));
        // as opções explícitas continuam como antes
        Assert.Null(Pesquisa.Aplicar(regs, Campos, new Consulta("nome_do_contribuinte", Condicao.Contem, "osmar")).Deteccao);
    }
}
