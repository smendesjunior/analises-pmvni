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
    [Fact]
    public void citacao_de_norma_nao_e_referencia_do_relatorio()
    {
        var s = Sessao();
        var id1 = Id(s, 1);
        // Base legal com figura de norma (8 figuras no relatório): não bloqueia.
        s.Aplicar(new DefinirCampo(id1, "base_legal", "ABNT NBR 9050:2020, 6.6.4, Figura 75."));
        // Em outro campo, "Figura N" com a norma na mesma frase também não.
        s.Aplicar(new DefinirCampo(id1, "verificacao", "A rampa segue a Figura 75 da NBR 9050."));
        // Linha de tabela com "item 12 do Anexo VI" e "Anexo VI, item 99": são da lei, não do relatório.
        s.Aplicar(new DefinirCampo("indices/02", "texto", "Limite do item 12 do Anexo VI; ver Anexo VI, item 99."));
        Assert.Empty(ReferenciasCruzadas.Quebradas(s.Projeto));
        Assert.Empty(ReferenciasCruzadas.Contraditorias(s.Projeto));

        // Sem norma por perto, a figura que não existe continua bloqueando.
        s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver a Figura 75."));
        Assert.Single(ReferenciasCruzadas.Quebradas(s.Projeto));
    }

    [Fact]
    public void contradicao_que_veio_do_kit_nao_bloqueia_por_edicao_so_de_outro_texto()
    {
        var caminho = Ambiente.Corpus("longo.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        // O kit já mandou a linha NÃO ATENDE citando o item 04 (ATENDE).
        dados["indices"]!["linhas"]![1]!["texto"] = "Faltam 0,20 m (item 04).";
        var c = Importador.Importar(JsonFiel.Escrever(dados), caminho, Modelo, Ambiente.Contexto());
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        Assert.Empty(ReferenciasCruzadas.Contraditorias(s.Projeto));

        // Editar só o cálculo da linha (a citação e as situações não mudam): continua sem bloquear.
        s.Aplicar(new DefinirCampo("indices/02", "calculo", "188,00 ÷ 480,00 × 100 = 39%"));
        Assert.True(TabelasRevisao.Revisada(s.Projeto, "indices/02"));
        Assert.Empty(ReferenciasCruzadas.Contraditorias(s.Projeto));

        // Revisar a situação do item (o par) volta a conferir.
        s.Aplicar(new DefinirSituacao(Id(s, 4), "ATENDE"));   // igual à original: não é revisão
        Assert.Empty(ReferenciasCruzadas.Contraditorias(s.Projeto));
        s.Aplicar(new DefinirSituacao("indices/02", "PENDENTE"));
        s.Aplicar(new DefinirSituacao("indices/02", "NAO_ATENDE")); // de volta à original
        Assert.Empty(ReferenciasCruzadas.Contraditorias(s.Projeto));
    }

    [Fact]
    public void figura_literal_do_kit_renumerada_so_avisa()
    {
        var s = Sessao();
        var figuras = CamadaFiguras.Inicial(s.Projeto);
        // O item 02 cita a "Figura 3"; excluir a figura 1 faz a antiga 3 virar a 2.
        s.Aplicar(new ExcluirFigura(figuras[0].Id));
        var aviso = Assert.Single(ReferenciasCruzadas.FigurasRenumeradas(s.Projeto), a => a.StartsWith("ANÁLISE · ITEM 02 ", StringComparison.Ordinal));
        Assert.Equal("ANÁLISE · ITEM 02 (campo referencia) cita \"Figura 3\", que agora é a Figura 2: confira o número", aviso);
        Assert.Contains(aviso, ConferenciaRevisao.Avisos(s.Projeto));
        Assert.DoesNotContain(aviso, ConferenciaRevisao.Erros(s.Projeto));
    }
}
