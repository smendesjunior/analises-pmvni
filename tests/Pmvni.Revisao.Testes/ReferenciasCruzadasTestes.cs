using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Tarefa 16: a conferência depois da edição bloqueia a exportação quando um texto cita item ou figura excluídos, e quando
/// uma linha de tabela e o item que ela cita ficam com situações opostas. As mensagens apontam a seção e o item.</summary>
public class ReferenciasCruzadasTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    /// <summary>longo.json: 60 itens (o 04 é ATENDE), 8 figuras (a 2 cita os itens 02, 03 e 04), o item 01 cita a "Figura 2" e a
    /// linha indices/02 é NÃO ATENDE.</summary>
    private static SessaoRevisao Sessao()
    {
        var caminho = Ambiente.Corpus("longo.json");
        var c = Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    private static string Id(SessaoRevisao s, int numero) => s.Projeto.Incluidos[numero - 1].Id;

    [Fact]
    public void importado_sem_revisao_nao_tem_referencia_quebrada_nem_contradicao()
    {
        var s = Sessao();
        Assert.Empty(ReferenciasCruzadas.Quebradas(s.Projeto));
        Assert.Empty(ReferenciasCruzadas.Contraditorias(s.Projeto));
    }

    [Fact]
    public void Conferencia_ReferenciaQuebrada()
    {
        var s = Sessao();
        var id1 = Id(s, 1);
        var id4 = Id(s, 4);

        // Item que cita item excluído: bloqueia e aponta seção, item e campo.
        s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 04."));
        s.Aplicar(new Excluir(id4));
        var erros = ConferenciaRevisao.Erros(s.Projeto);
        Assert.Contains("ANÁLISE · ITEM 01 (campo verificacao): cita o item excluído que era o item 04", erros);
        // A figura 2 também indicava o item 04.
        Assert.Contains(erros, e => e.StartsWith("IMAGENS DO PROJETO · Figura 2 (indicação ", StringComparison.Ordinal)
                                    && e.EndsWith("): cita o item excluído que era o item 04", StringComparison.Ordinal));
        s.Desfazer();
        Assert.Empty(ReferenciasCruzadas.Quebradas(s.Projeto));

        // Item que cita figura excluída ("Figura 2" na referência do item 01).
        var fig2 = CamadaFiguras.Inicial(s.Projeto)[1].Id;
        s.Aplicar(new ExcluirFigura(fig2));
        Assert.Contains("ANÁLISE · ITEM 01 (campo referencia): cita a figura excluída que era a Figura 2", ConferenciaRevisao.Erros(s.Projeto));
        s.Aplicar(new RestaurarFigura(fig2));
        Assert.Empty(ReferenciasCruzadas.Quebradas(s.Projeto));

        // Figura nova digitada pelo usuário que não existe no relatório (8 figuras).
        s.Aplicar(new DefinirCampo(id1, "referencia", "ARQ 02/09, planta. Figura 9."));
        Assert.Contains("ANÁLISE · ITEM 01 (campo referencia): cita a Figura 9, que não existe no relatório", ConferenciaRevisao.Erros(s.Projeto));
    }

    [Fact]
    public void Conferencia_ReferenciaQuebrada_em_linha_de_tabela()
    {
        var s = Sessao();
        s.Aplicar(new DefinirCampo("indices/02", "texto", "Faltam 0,20 m (item 05)."));
        Assert.Empty(ReferenciasCruzadas.Quebradas(s.Projeto));
        s.Aplicar(new Excluir(Id(s, 5)));
        // Texto digitado na revisão: a numeração é a de agora; o item 05 de agora é o antigo 06 (incluído), então a linha não quebra
        // (as figuras que indicavam o antigo 05 quebram, mas são outra seção).
        Assert.DoesNotContain(ReferenciasCruzadas.Quebradas(s.Projeto), e => e.StartsWith("ÍNDICES", StringComparison.Ordinal));
        Assert.Contains(ReferenciasCruzadas.Quebradas(s.Projeto), e => e.StartsWith("IMAGENS DO PROJETO", StringComparison.Ordinal));
        s.Aplicar(new DefinirCampo("indices/02", "texto", "Faltam 0,20 m (item 61)."));
        Assert.Contains("ÍNDICES URBANÍSTICOS · ÍNDICE 02 (campo Texto da situação): cita o item 61, que não existe no relatório",
            ConferenciaRevisao.Erros(s.Projeto));
    }

    [Fact]
    public void Conferencia_SituacaoContraditoria()
    {
        var s = Sessao();
        // A linha NÃO ATENDE passa a citar o item 04, que é ATENDE: aponta os dois.
        s.Aplicar(new DefinirCampo("indices/02", "texto", "Faltam 0,20 m (item 04)."));
        var erro = Assert.Single(ReferenciasCruzadas.Contraditorias(s.Projeto));
        Assert.Equal("ÍNDICES URBANÍSTICOS · ÍNDICE 02 está NÃO ATENDE e cita ANÁLISE · ITEM 04, que está ATENDE: as situações se contradizem", erro);
        Assert.Contains(erro, ConferenciaRevisao.Erros(s.Projeto));

        // Item revisado para NÃO ATENDE: a contradição some.
        s.Aplicar(new DefinirSituacao(Id(s, 4), "NAO_ATENDE"));
        Assert.Empty(ReferenciasCruzadas.Contraditorias(s.Projeto));

        // Linha revisada para ATENDE com o item agora NÃO ATENDE: contradição de novo, no outro sentido.
        s.Aplicar(new DefinirSituacao("indices/02", "CONTA_CONFERE"));
        Assert.Equal("ÍNDICES URBANÍSTICOS · ÍNDICE 02 está ATENDE e cita ANÁLISE · ITEM 04, que está NÃO ATENDE: as situações se contradizem",
            Assert.Single(ReferenciasCruzadas.Contraditorias(s.Projeto)));
    }
}
