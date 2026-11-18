using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Criar processo (Tarefa 19, desenho §12.3): como o <c>criar</c> do pasta_processo.py do kit.</summary>
public class CriacaoProcessoTestes
{
    [Theory]
    [InlineData("001AB", "001AB/2026")]
    [InlineData("../../001:A", "../../001:A/2026")]
    public void Identificador_reabre_e_identifica_a_prancha(string id, string protocolo)
    {
        using var t = Ambiente.PastaDeTeste("proc-identificador");
        var (p, _) = CriacaoProcesso.Criar(t, id, 2026, "Teste");
        var relido = Assert.Single(Processos.Listar(t, CancellationToken.None).Itens);
        Assert.Equal(protocolo, relido.Protocolo);
        Assert.True(Processos.Filtrar(relido, id));
        Assert.Equal(protocolo, PeticaoDaPrancha.Identificar(Path.Combine(p.Pasta, "DOCUMENTOS", "Folha.pdf")));
        Assert.Equal(p.Pasta, CriacaoProcesso.Criar(t, id, 2026, "Outro").Processo.Pasta);
    }

    [Fact]
    public void criar_processo_como_o_kit()
    {
        using var t = Ambiente.PastaDeTeste("proc-criar");
        var (p, criado) = CriacaoProcesso.Criar(t, 9998, 2099, "Fulána  de Tál");
        Assert.True(criado);
        Assert.Equal(Path.Combine(t, "2099", "9998 - 2099 - FULANA DE TAL"), p.Pasta);
        Assert.Equal((9998, 2099, "FULANA DE TAL"), (p.Numero, p.Ano, p.Requerente));
        Assert.True(Directory.Exists(Path.Combine(p.Pasta, "DOCUMENTOS")));
        Assert.True(Directory.Exists(Path.Combine(p.Pasta, "PROJETO", "INICIAL")));

        var (de_novo, criadoDeNovo) = CriacaoProcesso.Criar(t, 9998, 2099, "Outro Nome");
        Assert.False(criadoDeNovo);
        Assert.Equal(p.Pasta, de_novo.Pasta);
        Assert.Single(Directory.GetDirectories(Path.Combine(t, "2099")));
    }

    [Fact]
    public void criar_processo_reutiliza_nome_fora_do_padrao()
    {
        using var t = Ambiente.PastaDeTeste("proc-reuso");
        var existente = Path.Combine(t, "2099", "9997-2099 - X");
        Directory.CreateDirectory(existente);
        var (p, criado) = CriacaoProcesso.Criar(t, 9997, 2099, "Fulano");
        Assert.False(criado);
        Assert.Equal(existente, p.Pasta);
        Assert.Equal(new[] { existente }, Directory.GetDirectories(Path.Combine(t, "2099")));
        Assert.Empty(Directory.GetFileSystemEntries(existente));

        // "FINALIZADO - ..." e zeros à esquerda também são o mesmo processo.
        var finalizado = Path.Combine(t, "2098", "FINALIZADO - 0123 - 2098 - Z");
        Directory.CreateDirectory(finalizado);
        var (f, criadoF) = CriacaoProcesso.Criar(t, 123, 2098, "Fulano");
        Assert.False(criadoF);
        Assert.Equal(finalizado, f.Pasta);
        Assert.Equal(new[] { "2098", "2099" }, Directory.GetDirectories(t).Select(Path.GetFileName).OrderBy(x => x).ToArray());
    }

    [Theory]
    [InlineData("Fulána  de Tál", "FULANA DE TAL")]
    [InlineData("  joão  d'ávila & filhos ", "JOAO D'AVILA & FILHOS")]
    [InlineData("a/b\\c:d*e?f\"g<h>i|j", "A B C D E F G H I J")]
    [InlineData("nome.", "NOME")]
    public void nome_do_requerente_como_o_kit(string entrada, string esperado) => Assert.Equal(esperado, CriacaoProcesso.NomeRequerente(entrada));

    [Fact]
    public void nome_do_requerente_limitado_a_80()
    {
        var longo = new string('A', 79) + " " + new string('B', 20);
        var r = CriacaoProcesso.NomeRequerente(longo);
        Assert.Equal(79, r.Length);   // o corte cai no espaço e o espaço final sai
    }

    [Fact]
    public void pasta_do_ano_que_e_atalho_nao_recebe_processo_novo()
    {
        using var t = Ambiente.PastaDeTeste("proc-juncao");
        var fora = Path.Combine(t, "fora"); Directory.CreateDirectory(fora);
        var raiz = Path.Combine(t, "raiz"); Directory.CreateDirectory(raiz);
        if (!ArquivamentoTestes.CriarJuncao(Path.Combine(raiz, "2099"), fora)) return;    // sem permissão para atalhos neste PC: nada a provar
        var e = Assert.Throws<IOException>(() => CriacaoProcesso.Criar(raiz, 5, 2099, "Fulano"));
        Assert.Equal("A pasta 2099 é um atalho (link); o editor não cria processos nela.", e.Message);
        Assert.Empty(Directory.GetFileSystemEntries(fora));
    }

    [Fact]
    public void dados_invalidos_sao_recusados()
    {
        using var t = Ambiente.PastaDeTeste("proc-invalido");
        Assert.Throws<ArgumentException>(() => CriacaoProcesso.Criar(t, 1, 2099, "  ..  "));
        Assert.Throws<ArgumentException>(() => CriacaoProcesso.Criar(t, 0, 2099, "X"));
        Assert.Throws<ArgumentException>(() => CriacaoProcesso.Criar(t, 1, 1850, "X"));
        Assert.Throws<DirectoryNotFoundException>(() => CriacaoProcesso.Criar(Path.Combine(t, "nao-existe"), 1, 2099, "X"));
        Assert.Empty(Directory.GetFileSystemEntries(t));
    }
}
