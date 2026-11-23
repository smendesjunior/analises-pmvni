using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Tarefa 22: a pasta do processo e os nomes, em código, com os mesmos exemplos do <c>self_test_pasta_processo.py</c> do antigo plugin
/// despacho (<c>numero_ano</c>, <c>nome_requerente</c>, <c>criar</c>, <c>localizar</c>, <c>uma_pasta</c>, <c>pasta_revisao</c>,
/// <c>pasta_despacho</c> e <c>copiar</c>).</summary>
public sealed class PastaProcessoTestes
{
    [Theory]
    [InlineData("18805/2026")]
    [InlineData("18805-2026")]
    [InlineData("18805 - 2026")]
    [InlineData(" 018805 2026 ")]
    public void PastaProcesso_Nomes_numero_e_ano(string texto) => Assert.Equal((18805, 2026), ConteudoDespacho.ProcessoDaCapa(texto));

    [Fact]
    public void PastaProcesso_Nomes()
    {
        Assert.Null(ConteudoDespacho.ProcessoDaCapa("18805"));   // sem ano: o editor pergunta, nunca usa o ano corrente
        Assert.Equal("LIGIA BEATRIZ ALTOE ME", CriacaoProcesso.NomeRequerente("  Lígia  Beatriz Altoé / ME "));
        Assert.Equal("DESPACHO_CORRECAO_18805_2026_LIGIA_BEATRIZ_ALTOE", NomesDespacho.Base(TipoDespacho.Correcao, 18805, 2026, "Lígia Beatriz Altoé"));

        using var t = Ambiente.PastaDeTeste("pasta-processo-kit");
        string raiz = t;
        var (p, criada) = CriacaoProcesso.Criar(raiz, 18805, 2026, "Lígia Beatriz Altoé");
        Assert.True(criada);
        Assert.Equal("18805 - 2026 - LIGIA BEATRIZ ALTOE", Path.GetFileName(p.Pasta));
        Assert.Equal("2026", Path.GetFileName(Path.GetDirectoryName(p.Pasta)));
        Assert.True(Directory.Exists(Path.Combine(p.Pasta, "DOCUMENTOS")));
        Assert.True(Directory.Exists(Path.Combine(p.Pasta, "PROJETO", "INICIAL")));
        Assert.False(CriacaoProcesso.Criar(raiz, 18805, 2026, "outro nome").Criado);   // não duplica

        Directory.CreateDirectory(Path.Combine(raiz, "2026", "FINALIZADO - 16229-2026 - RAFAEL"));
        Assert.Single(CriacaoProcesso.Localizar(raiz, "16229", 2026));
        Assert.Empty(CriacaoProcesso.Localizar(raiz, "1622", 2026));                     // número parcial não confunde
        Directory.CreateDirectory(Path.Combine(raiz, "2025", "9001 - 2024 - FORA DO ANO"));
        Assert.Single(CriacaoProcesso.Localizar(raiz, "9001", 2024));                    // pasta em outro ano também é achada
        Directory.CreateDirectory(Path.Combine(raiz, "2026", "18805-2026 - DUPLICADA"));
        Assert.Equal(2, CriacaoProcesso.Localizar(raiz, "18805", 2026).Count);
        var ambiguo = Assert.Throws<IOException>(() => CriacaoProcesso.Criar(raiz, 18805, 2026, "Lígia"));
        Assert.StartsWith("Há mais de uma pasta para o processo 18805/2026", ambiguo.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PastaProcesso_Nomes_revisao_despacho_e_copia()
    {
        using var t = Ambiente.PastaDeTeste("pasta-processo-etapas");
        var pasta = CriacaoProcesso.Criar(t, 1, 2026, "Teste").Processo.Pasta;

        Assert.Equal("REVISÃO 01", Arquivamento.ProximaRevisao(pasta));
        Directory.CreateDirectory(Path.Combine(pasta, "PROJETO", "REVISÃO 01"));
        Assert.Equal("REVISÃO 02", Arquivamento.ProximaRevisao(pasta));
        Directory.CreateDirectory(Path.Combine(pasta, "PROJETO", "REVISÃO 02"));
        Assert.Equal("REVISÃO 02", Arquivamento.RevisaoAtual(pasta));                     // "--mesma": a última

        var d1 = Etapas.Nova(pasta, new DateOnly(2026, 11, 13));
        Assert.Equal("DESPACHO 01 - 2026-11-13", d1.Nome);
        Directory.CreateDirectory(d1.Pasta);
        var d2 = Etapas.Nova(pasta, new DateOnly(2026, 11, 20));
        Assert.Equal("DESPACHO 02 - 2026-11-20", d2.Nome);
        Directory.CreateDirectory(d2.Pasta);
        Assert.Equal("DESPACHO 02 - 2026-11-20", Etapas.Existentes(pasta)[0].Nome);       // "--mesma": a última

        var a = Path.Combine(t, "req.pdf");
        File.WriteAllBytes(a, "%PDF um"u8.ToArray());
        ArquivoArquivado Copiar() => Assert.Single(Arquivamento.Arquivar(pasta, Arquivamento.Planejar(pasta, [(a, DestinoAnexo.Documento)]), CancellationToken.None));
        var primeira = Copiar();
        Assert.Equal((Path.Combine("DOCUMENTOS", "REQ.PDF"), false), (primeira.Relativo, primeira.JaEstava));
        Assert.True(Copiar().JaEstava);                                                    // mesmo conteúdo: não duplica
        File.WriteAllBytes(a, "%PDF dois"u8.ToArray());
        Assert.Equal(Path.Combine("DOCUMENTOS", "REQ (2).PDF"), Copiar().Relativo);        // nome igual, conteúdo diferente
    }
}
