using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class ConferenciaRevisaoTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static SessaoRevisao Sessao()
    {
        var caminho = Ambiente.Corpus("curto.json");
        var c = Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    private static string Id(SessaoRevisao s, int numero) => s.Projeto.Incluidos[numero - 1].Id;

    [Fact]
    public void importado_sem_revisao_nao_tem_erros_nem_avisos()
    {
        var s = Sessao();
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
        Assert.Empty(ConferenciaRevisao.Avisos(s.Projeto));
        Assert.Empty(ConferenciaRevisao.CitacoesAExcluidos(s.Projeto));
    }

    [Fact]
    public void justificativa_faltando_vira_erro_da_conferencia()
    {
        var s = Sessao();
        var id = Id(s, 2);
        s.Aplicar(new DefinirSituacao(id, "ATENDE"));

        Assert.Equal(new[] { "ITEM 02: a situação mudou de FALTA COMPROVAR para ATENDE sem justificativa" }, ConferenciaRevisao.Erros(s.Projeto));

        s.Aplicar(new DefinirJustificativa(id, "   "));   // só espaços não vale
        Assert.Single(ConferenciaRevisao.Erros(s.Projeto));

        s.Aplicar(new DefinirJustificativa(id, "Comprovado."));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
    }

    [Fact]
    public void erro_de_justificativa_usa_a_numeracao_atual_e_ignora_excluidos()
    {
        var s = Sessao();
        var id3 = Id(s, 3);
        s.Aplicar(new DefinirSituacao(id3, "NAO_COMPROVADO")); // NÃO ATENDE → FALTA COMPROVAR
        s.Aplicar(new Excluir(Id(s, 1)));

        Assert.Equal(new[] { "ITEM 02: a situação mudou de NÃO ATENDE para FALTA COMPROVAR sem justificativa" }, ConferenciaRevisao.Erros(s.Projeto));

        s.Aplicar(new Excluir(id3));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
    }

    [Fact]
    public void citacao_a_excluido_vira_aviso()
    {
        var s = Sessao();
        var id1 = Id(s, 1);
        s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 03."));
        Assert.Empty(ConferenciaRevisao.CitacoesAExcluidos(s.Projeto));

        s.Aplicar(new Excluir(Id(s, 3)));

        Assert.Equal(new[] { "ITEM 01 cita o item excluído que era o item 03 (campo verificacao)" }, ConferenciaRevisao.CitacoesAExcluidos(s.Projeto));

        s.Desfazer();
        Assert.Empty(ConferenciaRevisao.CitacoesAExcluidos(s.Projeto));
    }

    [Fact]
    public void citacao_a_excluido_so_conta_para_itens_incluidos()
    {
        var s = Sessao();
        var id1 = Id(s, 1);
        s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 03."));
        s.Aplicar(new Excluir(Id(s, 3)));
        s.Aplicar(new Excluir(id1));

        Assert.Empty(ConferenciaRevisao.CitacoesAExcluidos(s.Projeto));
    }

    [Fact]
    public void literais_suspeitos_avisados_so_quando_a_numeracao_muda()
    {
        var s = Sessao();
        var id1 = Id(s, 1);
        s.Aplicar(new DefinirCampo(id1, "verificacao", "Conforme itens 03 a 07."));
        Assert.Equal("Conforme itens 03 a 07.", s.Projeto.Item(id1)!.Campos["verificacao"].Revisado); // faixa: continua literal
        Assert.Empty(ConferenciaRevisao.Avisos(s.Projeto));

        s.Aplicar(new Excluir(Id(s, 2)));

        var avisos = ConferenciaRevisao.Avisos(s.Projeto);
        Assert.Single(avisos);
        Assert.Contains("ITEM 01", avisos[0]);
        Assert.Contains("itens 03", avisos[0]);

        s.Desfazer();
        Assert.Empty(ConferenciaRevisao.Avisos(s.Projeto));
    }

    [Fact]
    public void literais_suspeitos_na_conclusao_tambem_sao_avisados()
    {
        var s = Sessao();
        s.Projeto.Documento["conclusao"] = "Pendentes: itens 02 a 03.";
        Assert.Empty(ConferenciaRevisao.Avisos(s.Projeto));

        s.Aplicar(new Excluir(Id(s, 1)));

        var avisos = ConferenciaRevisao.Avisos(s.Projeto);
        Assert.Single(avisos);
        Assert.Contains("conclusão", avisos[0]);
    }

    [Fact]
    public void proximo_pendente_e_circular()
    {
        var s = Sessao();                       // ATENDE, NAO_COMPROVADO, NAO_ATENDE
        var id1 = Id(s, 1);
        var id2 = Id(s, 2);
        var id3 = Id(s, 3);

        Assert.Equal(id2, Pendencias.Proximo(s.Projeto, null));
        Assert.Equal(id2, Pendencias.Proximo(s.Projeto, id1));
        Assert.Equal(id3, Pendencias.Proximo(s.Projeto, id2));
        Assert.Equal(id2, Pendencias.Proximo(s.Projeto, id3));   // volta ao começo

        s.Aplicar(new DefinirSituacao(id2, "ATENDE"));            // ATENDE sem justificativa: continua pendente
        Assert.Equal(id2, Pendencias.Proximo(s.Projeto, id1));
        s.Aplicar(new DefinirJustificativa(id2, "ok"));
        Assert.Equal(id3, Pendencias.Proximo(s.Projeto, id1));

        s.Aplicar(new Excluir(id3));
        Assert.Null(Pendencias.Proximo(s.Projeto, id1));          // nada pendente
        s.Aplicar(new Restaurar(id3));
        Assert.Equal(id3, Pendencias.Proximo(s.Projeto, id3));    // só ele: devolve ele mesmo
    }

    [Fact]
    public void proximo_pendente_com_atual_excluido_ou_desconhecido_comeca_do_primeiro()
    {
        var s = Sessao();
        var id1 = Id(s, 1);
        s.Aplicar(new Excluir(id1));
        Assert.Equal(Id(s, 1), Pendencias.Proximo(s.Projeto, id1));
        Assert.Equal(Id(s, 1), Pendencias.Proximo(s.Projeto, "it_nao_existe"));
    }
}
