using System.Diagnostics;
using Pmvni.App;
using Pmvni.Composicao.Web;
using Pmvni.Composicao.Web.Legislacao;
using Pmvni.Legislacao;
using Pmvni.Legislacao.Testes.Ficticios;
using Xunit;

namespace Pmvni.Web.Testes;

/// <summary>As figuras da leitura padronizada na <see cref="SessaoLegislacao"/> do app (o mesmo arquivo, compilado aqui), com o PDF.js real
/// num WebView2 oculto: a leitura corre em segundo plano, nunca segura anexar nem excluir, é cancelada ao excluir a lei e, quando falha,
/// a lei segue sem figuras (roteiro, passo 48, 2026-11-15).</summary>
public sealed class FigurasSessaoTestes : IAsyncLifetime
{
    HostOculto host = null!;
    SessaoLegislacao sessao = null!;
    ExtratorPdfJs? real;
    string pasta = null!;
    string originais = null!;

    public async Task InitializeAsync()
    {
        pasta = Path.Combine(Ambiente.RaizRepositorio(), "tests", "saida", "figuras-sessao", Guid.NewGuid().ToString("N"));
        originais = Path.Combine(pasta, "originais");
        Directory.CreateDirectory(originais);
        host = await HostOculto.CriarAsync(800, 600);
        sessao = new SessaoLegislacao(Path.Combine(pasta, "legislacao"), async () =>
            real = await host.ExecutarAsync(core => ExtratorPdfJs.CriarAsync(core.Environment, host.Janela, sessao.Servidor)));
    }

    public async Task DisposeAsync()
    {
        await sessao.DisposeAsync();
        await host.DisposeAsync();
        try { Directory.Delete(pasta, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    string Original(string nome, byte[] bytes)
    {
        var caminho = Path.Combine(originais, nome);
        File.WriteAllBytes(caminho, bytes);
        return caminho;
    }

    async Task<string> AnexarAsync(string nome, byte[] bytes)
    {
        var relogio = Stopwatch.StartNew();
        var (anexados, recusas) = await sessao.AnexarAsync([Original(nome, bytes)], null, CancellationToken.None);
        Assert.Empty(recusas);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(30), $"anexar {nome} levou {relogio.Elapsed}");
        return Assert.Single(anexados).Id;
    }

    static async Task Esperar(Func<bool> condicao, int milissegundos)
    {
        var relogio = Stopwatch.StartNew();
        while (!condicao() && relogio.ElapsedMilliseconds < milissegundos) await Task.Delay(25);
    }

    [Fact]
    public async Task excluir_a_lei_durante_a_leitura_das_figuras_cancela_a_leitura_e_nada_espera()
    {
        var id = await AnexarAsync("FIGURAS GRANDE - FICTICIA.pdf", PdfLegislacaoFicticio.GerarComFigurasGrande(150));
        var paginas = 0;
        real!.Ponte.Recebida += m => { if (m.Tipo == "figurasPagina") Interlocked.Increment(ref paginas); };

        // A lei abre (o app chama FigurasAsync em segundo plano, sem esperar).
        var lendo = sessao.FigurasAsync(id, CancellationToken.None);
        await Esperar(() => Volatile.Read(ref paginas) > 0, 20000);
        Assert.True(paginas > 0, "a leitura das figuras não começou");
        Assert.Contains(id, sessao.FigurasEmCurso);

        // Anexar outra lei com a leitura em andamento: o texto não espera as figuras.
        var outra = await AnexarAsync("LEI 9.999-2099 - FICTICIA.pdf", PdfLegislacaoFicticio.Todos["MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf"]);
        Assert.False(lendo.IsCompleted, "a leitura das figuras terminou antes: o teste não provou nada");

        // Excluir a lei durante a leitura: sai na hora e a leitura é cancelada, sem gravar nada.
        var relogio = Stopwatch.StartNew();
        Assert.Null(await sessao.ExcluirAsync(id));
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(5), "excluir levou " + relogio.Elapsed);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lendo.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(sessao.FigurasEmCurso);
        Assert.DoesNotContain(sessao.Acervo.Listar(), a => a.Id == id);
        Assert.Empty(Directory.GetFiles(Path.Combine(pasta, "legislacao"), "*.figuras.json.gz", SearchOption.AllDirectories));

        // O extrator segue: as figuras da outra lei saem (nenhuma) e ficam guardadas.
        relogio.Restart();
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<FiguraPdf>>(await sessao.FigurasAsync(outra, CancellationToken.None)));
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(10), "as figuras da outra lei levaram " + relogio.Elapsed);
        Assert.NotNull(sessao.Acervo.Figuras(outra));
    }

    [Fact]
    public async Task fechar_a_lei_cancela_a_leitura_das_figuras()
    {
        var id = await AnexarAsync("FIGURAS GRANDE - FICTICIA.pdf", PdfLegislacaoFicticio.GerarComFigurasGrande(150));
        using var cts = new CancellationTokenSource();
        var lendo = sessao.FigurasAsync(id, cts.Token);
        await Esperar(() => sessao.FigurasEmCurso.Count > 0, 10000);
        cts.Cancel(); // a lei fechada ou trocada (MainWindow.EsconderLeituraPadronizada)
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => lendo.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Empty(sessao.FigurasEmCurso);
        Assert.Null(sessao.Acervo.Figuras(id)); // nada guardado: na próxima abertura lê de novo
    }

    [Fact]
    public async Task pdf_que_faz_a_leitura_das_figuras_falhar_abre_sem_figuras_e_nao_tenta_de_novo()
    {
        // O texto foi lido ao anexar; depois a cópia guardada fica ilegível (o PDF.js recusa): as figuras falham.
        var id = await AnexarAsync("LEI 9.999-2099 - FICTICIA.pdf", PdfLegislacaoFicticio.Todos["MUNICIPAL/LEI 9.999-2099 - FICTICIA.pdf"]);
        var copia = sessao.Acervo.CaminhoPdf(id)!;
        File.SetAttributes(copia, FileAttributes.Normal);
        File.WriteAllBytes(copia, PdfLegislacaoFicticio.Todos["OUTRAS/CORROMPIDO - FICTICIA.pdf"]);

        var relogio = Stopwatch.StartNew();
        Assert.Null(await sessao.FigurasAsync(id, CancellationToken.None));
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(10), "a recusa levou " + relogio.Elapsed);
        Assert.Empty(sessao.FigurasEmCurso);
        // A lei segue anexada, com o texto (a leitura padronizada abre sem figuras).
        Assert.NotNull(sessao.Acervo.TextoPorPagina(id));
        Assert.Null(sessao.Acervo.Figuras(id));

        // Na mesma sessão não tenta de novo (abrir a lei outra vez não chama o extrator).
        var figurasPedidas = 0;
        real!.Ponte.Recebida += m => { if (m.Tipo is "figurasPagina" or "figurasExtraidas") Interlocked.Increment(ref figurasPedidas); };
        relogio.Restart();
        Assert.Null(await sessao.FigurasAsync(id, CancellationToken.None));
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(2), "a segunda abertura levou " + relogio.Elapsed);
        await Task.Delay(300);
        Assert.Equal(0, Volatile.Read(ref figurasPedidas));

        // E o extrator segue: outra lei anexa e as figuras dela saem.
        var digitalizada = await AnexarAsync("DIGITALIZADA - FICTICIA.pdf", PdfLegislacaoFicticio.Todos["OUTRAS/DIGITALIZADA - FICTICIA.pdf"]);
        Assert.Single((await sessao.FigurasAsync(digitalizada, CancellationToken.None))!);
        Assert.True(Volatile.Read(ref figurasPedidas) > 0);
    }

    [Fact]
    public async Task descartar_a_sessao_cancela_a_leitura_das_figuras()
    {
        var id = await AnexarAsync("FIGURAS GRANDE - FICTICIA.pdf", PdfLegislacaoFicticio.GerarComFigurasGrande(150));
        var lendo = sessao.FigurasAsync(id, CancellationToken.None);
        await Esperar(() => sessao.FigurasEmCurso.Count > 0, 10000);
        var relogio = Stopwatch.StartNew();
        await sessao.DisposeAsync(); // fechar o app
        await Assert.ThrowsAnyAsync<Exception>(() => lendo.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(lendo.IsCompletedSuccessfully);
        Assert.True(relogio.Elapsed < TimeSpan.FromSeconds(6), "fechar levou " + relogio.Elapsed);
    }
}
