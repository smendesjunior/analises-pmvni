using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Leitura de processos: o que a Tarefa 19 acrescenta (relatorio_dados*.json do kit em <c>.pmvni-ai</c>).</summary>
public class ProcessosTestes
{
    static void CopiarArvore(string origem, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var d in Directory.GetDirectories(origem, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destino, Path.GetRelativePath(origem, d)));
        foreach (var f in Directory.GetFiles(origem, "*", SearchOption.AllDirectories))
            File.Copy(f, Path.Combine(destino, Path.GetRelativePath(origem, f)));
    }

    [Fact]
    public void relatorio_dados_do_kit_aparece_em_processos()
    {
        using var t = Ambiente.PastaDeTeste("processos-relatorio-dados");
        CopiarArvore(Ambiente.Corpus("processos"), t);
        var p = Processos.Listar(t, CancellationToken.None).Itens.Single(x => x.Numero == 1);
        var antes = Processos.Detalhar(p, CancellationToken.None);
        Assert.Single(antes.Relatorios);      // só o dados_0001.json da árvore da Tarefa 12

        var analise = Path.Combine(p.Pasta, ".pmvni-ai", "analise");
        var pro = Path.Combine(p.Pasta, ".pmvni-ai", "analise-pro");
        Directory.CreateDirectory(analise);
        Directory.CreateDirectory(pro);
        File.WriteAllText(Path.Combine(analise, "relatorio_dados.json"), "{}");
        File.WriteAllText(Path.Combine(pro, "relatorio_dados_0001.json"), "{}");
        File.WriteAllText(Path.Combine(pro, "delivery.json"), "{}");                         // não é relatório
        Directory.CreateDirectory(Path.Combine(analise, "etapas"));
        File.WriteAllText(Path.Combine(analise, "etapas", "relatorio_dados.json"), "{}");    // só as subpastas diretas de .pmvni-ai

        var d = Processos.Detalhar(p, CancellationToken.None);
        Assert.Equal(3, d.Relatorios.Count);
        Assert.All(d.Relatorios, r => Assert.Equal(TipoRelatorio.AnaliseIa, r.Tipo));
        Assert.Contains(d.Relatorios, r => r.Caminho == Path.Combine(analise, "relatorio_dados.json"));
        Assert.Contains(d.Relatorios, r => r.Caminho == Path.Combine(pro, "relatorio_dados_0001.json"));
        Assert.DoesNotContain(d.Relatorios, r => r.Caminho.Contains("etapas"));

        // Contagens do resto da árvore inalteradas.
        Assert.Equal(antes.Despachos.Count, d.Despachos.Count);
        Assert.Equal(antes.Projeto.Count, d.Projeto.Count);
        Assert.Equal(antes.Documentos.Count, d.Documentos.Count);
    }
}
