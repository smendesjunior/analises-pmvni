using System.Diagnostics;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Serviço de pastas (Tarefa 12): processos, despachos e abertura externa, sobre árvores fictícias em tests/corpus/processos.</summary>
public class PastasTestes
{
    static string Raiz => Ambiente.Corpus("processos");

    static Processo Achar(string inicio) =>
        Processos.Listar(Raiz, CancellationToken.None).Itens.Single(p => p.NomePasta.StartsWith(inicio, StringComparison.Ordinal));

    static void CopiarArvore(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var d in Directory.GetDirectories(origem, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destino, Path.GetRelativePath(origem, d)));
        foreach (var f in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
            File.Copy(f, Path.Combine(destino, Path.GetRelativePath(origem, f)));
    }

    [Fact]
    public void processos_mais_recentes_primeiro()
    {
        var r = Processos.Listar(Raiz, CancellationToken.None);
        Assert.Empty(r.Avisos);
        Assert.Equal(new[]
        {
            "0012-2099 - BELTRANO DE EXEMPLO (FICTICIO)", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)",
            "pasta fora do padrao", "0007 - 2098 - CICLANO DE EXEMPLO (FICTICIO)",
        }, r.Itens.Select(p => p.NomePasta));
        Assert.Equal((12, 2099, "BELTRANO DE EXEMPLO (FICTICIO)"), (r.Itens[0].Numero, r.Itens[0].Ano, r.Itens[0].Requerente));
    }

    [Fact]
    public void nome_fora_do_padrao_vem_como_veio()
    {
        var p = Achar("pasta fora");
        Assert.Null(p.Numero);
        Assert.Null(p.Ano);
        Assert.Null(p.Requerente);
        Assert.Equal("pasta fora do padrao", p.NomePasta);
    }

    [Fact]
    public void filtro_por_numero_e_nome_sem_acento()
    {
        var todos = Processos.Listar(Raiz, CancellationToken.None).Itens;
        Assert.Equal(new[] { 12 }, todos.Where(p => Processos.Filtrar(p, "12")).Select(p => p.Numero!.Value));
        Assert.Equal(new[] { 1 }, todos.Where(p => Processos.Filtrar(p, "fulano")).Select(p => p.Numero!.Value));
        Assert.Equal(new[] { 1 }, todos.Where(p => Processos.Filtrar(p, "FÚLANO")).Select(p => p.Numero!.Value));
        Assert.Equal(4, todos.Count(p => Processos.Filtrar(p, "  ")));
    }

    [Fact]
    public void detalhe_acha_analise_da_ia_e_despachos()
    {
        var d = Processos.Detalhar(Achar("0001"), CancellationToken.None);
        Assert.Empty(d.Avisos);
        var rel = Assert.Single(d.Relatorios);
        Assert.Equal(TipoRelatorio.AnaliseIa, rel.Tipo);
        Assert.EndsWith("dados_0001.json", rel.Caminho);
        Assert.Equal(new[] { (2, new DateOnly(2099, 2, 20), TipoDespacho.Aprovacao, 1), (1, new DateOnly(2099, 1, 15), TipoDespacho.Correcao, 2) },
            d.Despachos.Select(x => (x.Numero, x.Data, x.Tipo, x.Arquivos.Count)));
        Assert.Equal(new[] { "ARQ 01-03.pdf", "script.cmd" }, d.Projeto.Select(a => Path.GetFileName(a.Relativo)));
        Assert.Equal(new[] { "ART.pdf" }, d.Documentos.Select(a => Path.GetFileName(a.Relativo)));
    }

    [Fact]
    public void pmvrel_na_pasta_do_processo_aparece_como_pacote()
    {
        using var t = Ambiente.PastaDeTeste("pastas-pmvrel");
        CopiarArvore(Raiz, t);
        var p = Processos.Listar(t, CancellationToken.None).Itens.Single(x => x.Numero == 1);
        File.WriteAllText(Path.Combine(p.Pasta, "Relatorio_0001_2099.pmvrel"), "x");
        var d = Processos.Detalhar(p, CancellationToken.None);
        Assert.Equal(2, d.Relatorios.Count);
        Assert.Contains(d.Relatorios, r => r.Tipo == TipoRelatorio.Pacote && r.Caminho.EndsWith("Relatorio_0001_2099.pmvrel"));
        Assert.Contains(d.Relatorios, r => r.Tipo == TipoRelatorio.AnaliseIa);
        Assert.True(d.Relatorios[0].AlteradoUtc >= d.Relatorios[1].AlteradoUtc);
    }

    [Fact]
    public void nome_de_importacao_nao_sobrescreve()
    {
        using var t = Ambiente.PastaDeTeste("pastas-importacao");
        CopiarArvore(Raiz, t);
        var p = Processos.Listar(t, CancellationToken.None).Itens.Single(x => x.Numero == 1);
        var primeiro = Processos.NomeImportacao(p, null);
        Assert.Equal(Path.Combine(p.Pasta, "Relatorio_0001_2099.pmvrel"), primeiro);
        File.WriteAllText(primeiro, "x");
        Assert.Equal(Path.Combine(p.Pasta, "Relatorio_0001_2099_2.pmvrel"), Processos.NomeImportacao(p, null));

        var fora = Processos.Listar(t, CancellationToken.None).Itens.Single(x => x.Numero is null);
        Assert.Equal(Path.Combine(fora.Pasta, "Relatorio_9001_2026.pmvrel"), Processos.NomeImportacao(fora, "9001/2026"));
        Assert.Equal(Path.Combine(fora.Pasta, "Relatorio.pmvrel"), Processos.NomeImportacao(fora, null));
    }

    [Fact]
    public void despachos_de_todos_por_data()
    {
        var procs = Processos.Listar(Raiz, CancellationToken.None).Itens;
        var r = Despachos.Listar(procs, CancellationToken.None);
        Assert.Empty(r.Avisos);
        Assert.Equal(new[]
        {
            (new DateOnly(2099, 3, 5), TipoDespacho.NaoIdentificado), (new DateOnly(2099, 2, 20), TipoDespacho.Aprovacao),
            (new DateOnly(2099, 1, 15), TipoDespacho.Correcao), (new DateOnly(2098, 3, 2), TipoDespacho.Aprovacao),
        }, r.Itens.Select(d => (d.Data, d.Tipo)));
        Assert.All(r.Itens, d => Assert.NotNull(d.Processo));
    }

    [Theory]
    [InlineData("2099/0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)/PROJETO/ARQ 01-03.pdf", true)]
    [InlineData("2099/0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)/PROJETO/script.cmd", false)]
    [InlineData("MODELOS/atalho.lnk", false)]
    [InlineData("2099/0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)/PROJETO", false)]
    [InlineData("2099/0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)/PROJETO/ARQ 01-03.pdf:fluxo", false)]
    [InlineData("2099/nao existe.pdf", false)]
    public void abrir_externo_so_documentos(string relativo, bool permitido)
    {
        var caminho = Path.Combine(Raiz, relativo.Replace('/', Path.DirectorySeparatorChar));
        Assert.Equal(permitido, AbrirExterno.Permitido(caminho, out var motivo));
        Assert.Equal(permitido, motivo is null);
        if (relativo.EndsWith("script.cmd"))
            Assert.Equal("Este tipo de arquivo não é aberto pelo editor (.cmd). Use Mostrar no Explorador.", motivo);
    }

    [Fact]
    public void abrir_externo_lista_as_onze_extensoes()
    {
        Assert.Equal(new[] { ".doc", ".docx", ".dwg", ".dxf", ".jpeg", ".jpg", ".pdf", ".png", ".txt", ".xls", ".xlsx" },
            AbrirExterno.Extensoes.OrderBy(e => e, StringComparer.Ordinal));
    }

    [Fact]
    public void raiz_ausente_vira_aviso()
    {
        var r = Processos.Listar(Path.Combine(Raiz, "nao-existe"), CancellationToken.None);
        Assert.Empty(r.Itens);
        Assert.Contains("não foi encontrada", Assert.Single(r.Avisos));
    }

    [Fact]
    public void arvore_grande_e_lida_por_inteiro()
    {
        using var t = Ambiente.PastaDeTeste("pastas-grande");
        for (var i = 1; i <= 2000; i++)
            Directory.CreateDirectory(Path.Combine(t, (2000 + i % 20).ToString(), $"{i:D4} - {2000 + i % 20} - REQUERENTE {i} (FICTICIO)"));
        var sw = Stopwatch.StartNew();
        var r = Processos.Listar(t, CancellationToken.None);
        sw.Stop();
        Assert.Equal(2000, r.Itens.Count);
        Assert.Empty(r.Avisos);
        Console.WriteLine($"Listar 2000 processos: {sw.ElapsedMilliseconds} ms");
    }

    [Fact]
    public void cancelamento_interrompe_a_leitura()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Processos.Listar(Raiz, cts.Token));
        Assert.Throws<OperationCanceledException>(() => Despachos.Listar(Array.Empty<Processo>(), cts.Token));
        Assert.Throws<OperationCanceledException>(() => Processos.Detalhar(Achar("0001"), cts.Token));
    }
}
