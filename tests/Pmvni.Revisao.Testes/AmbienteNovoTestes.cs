using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Pedido do usuário (2026-11-16): "o cálculo de esquadrias é por ambiente — banheiro (janela + porta) = área total; o botão de
/// adicionar tem de ser por ambiente: você adiciona um ambiente e pode pôr janelas e portas". O ambiente novo entra na tabela de
/// iluminação, recebe as esquadrias, a conta se faz sobre a soma delas e tudo sai no relatório e se desfaz.</summary>
public class AmbienteNovoTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static SessaoRevisao Sessao()
    {
        var c = Importador.Importar(File.ReadAllBytes(Ambiente.Corpus("longo.json")), Ambiente.Corpus("longo.json"), Modelo, Ambiente.Contexto());
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    [Fact]
    public void ambiente_novo_com_janela_e_porta_soma_as_areas_e_sai_no_relatorio()
    {
        var s = Sessao();
        var id = TabelasRevisao.ProximoAmbiente(s.Projeto)!;
        Assert.Equal("ambientes/13", id);   // o longo tem 12 ambientes

        Assert.NotNull(s.Aplicar(new AcrescentarAmbiente(id, "Banheiro social", 3.50m, 8m, "Térreo")));
        Assert.True(TabelasRevisao.Existe(s.Projeto, id));
        Assert.True(TabelasRevisao.Acrescentado(s.Projeto, id));
        Assert.Equal("ambientes/14", TabelasRevisao.ProximoAmbiente(s.Projeto));
        Assert.Equal(id, TabelasRevisao.Ids(s.Projeto).Last(i => i.StartsWith("ambientes/", StringComparison.Ordinal)));
        var vazio = Esquadrias.Calcular(s.Projeto, id)!;
        Assert.Empty(vazio.Esquadrias);
        Assert.Equal(3.50m, vazio.Piso);
        Assert.Equal(8m, vazio.Divisor);

        // Janela 0,60 × 0,60 (0,36 m²) + porta 0,80 × 2,10 (1,68 m²) = 2,04 m²; mínimo 3,50 ÷ 8 = 0,44 m² → atende.
        foreach (var (cat, tipo, medidas) in new[] { (CategoriaEsquadria.Janela, "Janela", "0,60 × 0,60 m"), (CategoriaEsquadria.Porta, "Porta", "0,80 × 2,10 m") })
        {
            var conta = Esquadrias.Calcular(s.Projeto, id)!;
            var codigo = Esquadrias.ProximoCodigo(conta.Esquadrias, cat);
            Assert.NotNull(s.Aplicar(Esquadrias.Recalcular(s.Projeto, id, [Esquadrias.Nova(id, codigo, tipo, medidas)], "Esquadria acrescentada")));
        }
        var depois = Esquadrias.Calcular(s.Projeto, id)!;
        Assert.Equal(2, depois.Esquadrias.Count);
        Assert.Equal(2.04m, depois.Total);
        Assert.True(depois.Atende);
        Assert.Equal("CONTA_CONFERE", TabelasRevisao.SituacaoEfetiva(s.Projeto, id));

        // No dados.json efetivo: a 13ª linha da tabela, com as duas esquadrias, e o relatório passa na conferência do kit.
        var texto = System.Text.Encoding.UTF8.GetString(DadosEfetivos.Gerar(s.Projeto).Json);
        var json = System.Text.Json.Nodes.JsonNode.Parse(texto)!;
        var ambientes = json["iluminacao"]!["ambientes"]!.AsArray();
        Assert.Equal(13, ambientes.Count);
        Assert.Equal("Banheiro social", ambientes[12]!["nome"]!.GetValue<string>());
        Assert.Equal("wc", ambientes[12]!["icone"]!.GetValue<string>());
        Assert.Equal(2, ambientes[12]!["vaos"]!.AsArray().Count);
        var dados = (IReadOnlyDictionary<string, object?>)Pmvni.Composicao.JsonModelo.Ler(texto)!;
        Assert.DoesNotContain(Pmvni.Composicao.Kit.Conferencia.Conferir(dados, _ => true, false), e => e.Contains("Ambiente", StringComparison.Ordinal));
    }

    [Fact]
    public void ambiente_novo_se_desfaz_e_so_sai_sem_esquadrias()
    {
        var s = Sessao();
        var id = TabelasRevisao.ProximoAmbiente(s.Projeto)!;
        s.Aplicar(new AcrescentarAmbiente(id, "Lavabo", 2m, 8m, ""));
        var conta = Esquadrias.Calcular(s.Projeto, id)!;
        s.Aplicar(Esquadrias.Recalcular(s.Projeto, id, [Esquadrias.Nova(id, Esquadrias.ProximoCodigo(conta.Esquadrias, CategoriaEsquadria.Porta), "Porta", "0,70 × 2,10 m")], "Porta"));

        // Com esquadria, o ambiente não sai antes dela; sem esquadria, sai.
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new RemoverAcrescimo(id)));
        s.Aplicar(new RemoverAcrescimo(id, "vaos/0"));
        Assert.NotNull(s.Aplicar(new RemoverAcrescimo(id)));
        Assert.False(TabelasRevisao.Existe(s.Projeto, id));

        // Desfazer devolve o ambiente; desfazer de novo, a porta; e assim até antes de tudo.
        s.Desfazer();
        Assert.True(TabelasRevisao.Existe(s.Projeto, id));
        while (s.PodeDesfazer) s.Desfazer();
        Assert.False(TabelasRevisao.Existe(s.Projeto, id));
    }

    [Fact]
    public void ambiente_novo_recusa_dados_invalidos()
    {
        var s = Sessao();
        var id = TabelasRevisao.ProximoAmbiente(s.Projeto)!;
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarAmbiente(id, "  ", 3m, 8m, "")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarAmbiente(id, "Quarto", 0m, 6m, "")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new AcrescentarAmbiente("ambientes/20", "Quarto", 9m, 6m, "")));
    }
}
