using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Leitura de processos: o que a Tarefa 19 acrescenta (relatorio_dados*.json do kit em <c>.pmvni-ai</c>).</summary>
public class ProcessosTestes
{
    [Theory]
    [InlineData("POLICIA CIENTIFICA DO ESTADO - 19774-2026", "POLICIA CIENTIFICA DO ESTADO")]
    [InlineData("POLICIA CIENTIFICA DO ESTADO 19774/2026", "POLICIA CIENTIFICA DO ESTADO")]
    [InlineData("19774/2026 POLICIA CIENTIFICA", "POLICIA CIENTIFICA")]
    [InlineData("PROC 19774 2026 - JOAO DA SILVA", "JOAO DA SILVA")]
    [InlineData("Processo nº 19774-2026 - Joao da Silva", "Joao da Silva")]
    [InlineData("2026-19774 - JOAO DA SILVA", "JOAO DA SILVA")]
    [InlineData("JOAO DA SILVA (19774-26)", "JOAO DA SILVA")]
    [InlineData("JOAO DA SILVA - 19774", "JOAO DA SILVA")]
    [InlineData("POLICIA 19774 CIENTIFICA", "POLICIA CIENTIFICA")]
    [InlineData("CONSTRUTORA 3 IRMAOS - 19774", "CONSTRUTORA 3 IRMAOS")]
    public void le_protocolo_em_nome_livre(string nome, string requerente)
    {
        var processo = Processos.InterpretarNome("pasta", nome, "2026");
        Assert.Equal("19774/2026", processo.Protocolo);
        Assert.Equal(requerente, processo.Requerente);
    }

    [Theory]
    [InlineData("2026")]
    [InlineData("CONSTRUTORA 3 IRMAOS")]
    [InlineData("PROCESSO - REQUERENTE")]
    public void nome_sem_protocolo(string nome) =>
        Assert.Null(Processos.InterpretarNome("pasta", nome, "2026").Protocolo);
    [Theory]
    [InlineData("PROC. 00042 - 2026 - PESSOA FICTICIA", 42)]
    [InlineData("procc.   1234567_2026 - PESSOA FICTICIA", 1234567)]
    public void reconhece_prefixo_e_numero_sem_renomear_a_pasta(string nome, int numero)
    {
        using var temp = Ambiente.PastaDeTeste("processo-com-prefixo");
        var caminho = Path.Combine(temp, "2026", nome);
        Directory.CreateDirectory(caminho);

        var processo = Assert.Single(Processos.Listar(temp, CancellationToken.None).Itens);
        Assert.Equal(numero, processo.Numero);
        Assert.Equal(2026, processo.Ano);
        Assert.Equal("PESSOA FICTICIA", processo.Requerente);
        Assert.Equal(caminho, processo.Pasta);
        Assert.False(CriacaoProcesso.Criar(temp, numero, 2026, "PESSOA FICTICIA").Criado);
    }

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

    [Fact]
    public void relatorio_importado_vai_para_a_pasta_despacho_e_aparece_em_processos()
    {
        // Correção 2026-11-16 (regra do kit): o relatório novo fica em DESPACHO NN - AAAA-MM-DD, não na raiz do processo.
        using var t = Ambiente.PastaDeTeste("processos-relatorio-na-etapa");
        CopiarArvore(Ambiente.Corpus("processos"), t);
        var p = Processos.Listar(t, CancellationToken.None).Itens.Single(x => x.Numero == 1);
        var hoje = new DateOnly(2099, 3, 10);
        var esperada = Pmvni.Revisao.Etapas.Sugerida(p.Pasta, hoje);
        var caminho = Processos.NomeImportacao(p, null, hoje);
        Assert.Equal(esperada.Pasta, Path.GetDirectoryName(caminho));
        Assert.StartsWith("DESPACHO ", Path.GetFileName(Path.GetDirectoryName(caminho)), StringComparison.Ordinal);
        Assert.True(Directory.Exists(esperada.Pasta));
        Assert.Equal("RELATORIO_0001_2099.RPMVNI", Path.GetFileName(caminho));

        File.WriteAllText(caminho, "x");
        Assert.Contains(Processos.Detalhar(p, CancellationToken.None).Relatorios, r => r.Caminho == caminho && r.Tipo == TipoRelatorio.Pacote);
        // O segundo não sobrescreve: mesma etapa (ainda sem despacho), nome com _2.
        Assert.Equal(Path.Combine(esperada.Pasta, "RELATORIO_0001_2099_2.RPMVNI"), Processos.NomeImportacao(p, null, hoje));
    }
}
