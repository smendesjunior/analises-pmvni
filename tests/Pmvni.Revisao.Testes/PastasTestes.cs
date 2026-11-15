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

    [Fact]
    public void abrir_externo_recusa_fluxo_alternativo_com_arquivo_base_existente()
    {
        using var t = Ambiente.PastaDeTeste("pastas-fluxo");
        var baseArq = Path.Combine(t, "x.pdf");
        File.WriteAllText(baseArq, "x");
        Assert.True(AbrirExterno.Permitido(baseArq, out _));
        Assert.False(AbrirExterno.Permitido(baseArq + ":fluxo", out var motivo));
        Assert.Contains("caracteres não permitidos", motivo);
    }

    [Fact]
    public void abrir_externo_recusa_dupla_extensao_e_pasta_com_nome_de_arquivo()
    {
        using var t = Ambiente.PastaDeTeste("pastas-dupla");
        foreach (var nome in new[] { "x.pdf.exe", "x.pdf.lnk" })
        {
            File.WriteAllText(Path.Combine(t, nome), "x");
            Assert.False(AbrirExterno.Permitido(Path.Combine(t, nome), out _));
        }
        var pasta = Path.Combine(t, "foo.pdf");
        Directory.CreateDirectory(pasta);
        Assert.False(AbrirExterno.Permitido(pasta, out var motivo));
        Assert.Contains("Pastas", motivo);
    }

    static void CriarJuncao(string juncao, string alvo)
    {
        var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{juncao}\" \"{alvo}\"") { CreateNoWindow = true, RedirectStandardOutput = true };
        using var proc = Process.Start(psi)!;
        proc.StandardOutput.ReadToEnd();
        proc.WaitForExit();
        Assert.Equal(0, proc.ExitCode);
    }

    [Fact]
    public void juncao_e_ignorada_na_listagem_e_recusada_na_abertura_mas_pasta_comum_e_lida()
    {
        using var t = Ambiente.PastaDeTeste("pastas-juncao");
        CopiarArvore(Raiz, t);
        Assert.False(Processos.EhLink(Path.Combine(t, "2099")));

        var alvo = Path.Combine(t, "2099", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)");
        var juncao = Path.Combine(t, "2099", "0099 - 2099 - JUNCAO (FICTICIO)");
        CriarJuncao(juncao, alvo);
        Assert.True(Processos.EhLink(juncao));
        var itens = Processos.Listar(t, CancellationToken.None).Itens;
        Assert.DoesNotContain(itens, p => p.Numero == 99);
        Assert.Equal(4, itens.Count);

        var p1 = itens.Single(x => x.Numero == 1);
        CriarJuncao(Path.Combine(p1.Pasta, "DOCUMENTOS", "atalho"), Path.Combine(p1.Pasta, "PROJETO"));
        var d = Processos.Detalhar(p1, CancellationToken.None);
        Assert.Equal(new[] { "ART.pdf" }, d.Documentos.Select(a => Path.GetFileName(a.Relativo)));

        Assert.False(AbrirExterno.Permitido(juncao, out _));
        Assert.True(AbrirExterno.Permitido(Path.Combine(alvo, "PROJETO", "ARQ 01-03.pdf"), out _));
    }

    [Fact]
    public void atalho_simbolico_de_arquivo_e_recusado_quando_o_sistema_deixa_criar()
    {
        using var t = Ambiente.PastaDeTeste("pastas-simbolico");
        var alvo = Path.Combine(t, "real.pdf");
        File.WriteAllText(alvo, "x");
        var link = Path.Combine(t, "link.pdf");
        try { File.CreateSymbolicLink(link, alvo); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; } // sem privilégio para link simbólico
        Assert.False(AbrirExterno.Permitido(link, out var motivo));
        Assert.Contains("Links", motivo);
    }

    [Fact]
    public void data_invalida_de_despacho_vira_aviso()
    {
        using var t = Ambiente.PastaDeTeste("pastas-data");
        Directory.CreateDirectory(Path.Combine(t, "2099", "0005 - 2099 - TESTE (FICTICIO)", "DESPACHO 01 - 2099-02-31"));
        Directory.CreateDirectory(Path.Combine(t, "2099", "0005 - 2099 - TESTE (FICTICIO)", "DESPACHO 02 - 2099-02-28"));
        var p = Processos.Listar(t, CancellationToken.None).Itens.Single();
        var d = Processos.Detalhar(p, CancellationToken.None);
        Assert.Equal(new[] { 2 }, d.Despachos.Select(x => x.Numero));
        Assert.Contains("data", Assert.Single(d.Avisos));
    }

    [Fact]
    public void projeto_le_no_maximo_tres_niveis()
    {
        using var t = Ambiente.PastaDeTeste("pastas-niveis");
        var proc = Path.Combine(t, "2099", "0005 - 2099 - TESTE (FICTICIO)");
        foreach (var rel in new[] { "n1.pdf", "a/n2.pdf", "a/b/n3.pdf", "a/b/c/n4.pdf" })
        {
            var f = Path.Combine(proc, "PROJETO", rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(f)!);
            File.WriteAllText(f, "x");
        }
        var p = Processos.Listar(t, CancellationToken.None).Itens.Single();
        Assert.Equal(new[] { "n1.pdf", "n2.pdf", "n3.pdf" }, Processos.Detalhar(p, CancellationToken.None).Projeto.Select(a => Path.GetFileName(a.Relativo)).Order());
    }
}
