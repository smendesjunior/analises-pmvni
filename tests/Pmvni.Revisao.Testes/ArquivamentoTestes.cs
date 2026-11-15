using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Anexos no processo (Tarefa 19, desenho §12.3): destino, REVISÃO NN, cópia sem sobrescrever e recusas.</summary>
public class ArquivamentoTestes
{
    static string Sha(string caminho) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(caminho)));

    /// <summary>Processo vazio no modelo do kit, criado pelo mesmo código que o app usa.</summary>
    static string NovoProcesso(PastaTeste t) => CriacaoProcesso.Criar(t, 1, 2099, "Fulano de Exemplo").Processo.Pasta;

    static string Origem(PastaTeste t, string nome, string conteudo)
    {
        var pasta = Path.Combine(t, "origem");
        Directory.CreateDirectory(pasta);
        var caminho = Path.Combine(pasta, nome);
        File.WriteAllText(caminho, conteudo, Encoding.UTF8);
        return caminho;
    }

    static string[] ArquivosDe(string pasta) =>
        Directory.GetFiles(pasta, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(pasta, f)).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    [Fact]
    public void destino_padrao_pela_regra_do_kit()
    {
        using var t = Ambiente.PastaDeTeste("arq-padrao");
        var p = NovoProcesso(t);
        Assert.Equal(DestinoAnexo.ProjetoInicial, Arquivamento.Padrao(p));          // INICIAL existe e está vazia
        Directory.Delete(Path.Combine(p, "PROJETO", "INICIAL"));
        Assert.Equal(DestinoAnexo.ProjetoInicial, Arquivamento.Padrao(p));          // INICIAL ausente
        Directory.CreateDirectory(Path.Combine(p, "PROJETO", "INICIAL"));
        File.WriteAllText(Path.Combine(p, "PROJETO", "INICIAL", "planta.pdf"), "x");
        Assert.Equal(DestinoAnexo.ReapresentacaoNova, Arquivamento.Padrao(p));      // com arquivo
    }

    [Fact]
    public void proxima_revisao_com_dois_digitos_e_acento()
    {
        using var t = Ambiente.PastaDeTeste("arq-revisao");
        var p = NovoProcesso(t);
        Assert.Null(Arquivamento.RevisaoAtual(p));
        Assert.Equal("REVISÃO 01", Arquivamento.ProximaRevisao(p));
        Directory.CreateDirectory(Path.Combine(p, "PROJETO", "REVISÃO 01"));
        Directory.CreateDirectory(Path.Combine(p, "PROJETO", "revisão 7"));
        Assert.Equal("revisão 7", Arquivamento.RevisaoAtual(p));
        Assert.Equal("REVISÃO 08", Arquivamento.ProximaRevisao(p));

        using var t2 = Ambiente.PastaDeTeste("arq-revisao-sem-acento");
        var p2 = NovoProcesso(t2);
        Directory.CreateDirectory(Path.Combine(p2, "PROJETO", "REVISAO 02"));   // só a grafia sem acento
        Assert.Equal("REVISAO 02", Arquivamento.RevisaoAtual(p2));
        Assert.Equal("REVISÃO 03", Arquivamento.ProximaRevisao(p2));
    }

    [Fact]
    public void anexos_arquivados_como_o_kit()
    {
        using var t = Ambiente.PastaDeTeste("arq-kit");
        var p = NovoProcesso(t);
        var req = Origem(t, "req.pdf", "requerimento");
        var planta = Origem(t, "planta.pdf", "planta A");
        var corte = Origem(t, "corte.pdf", "corte");

        // Projeto: INICIAL.
        var arq1 = Arquivamento.Arquivar(p, Arquivamento.Planejar(p, new[] { (req, DestinoAnexo.Documento), (planta, DestinoAnexo.ProjetoInicial) }), CancellationToken.None);
        Assert.Equal(new[] { @"DOCUMENTOS\req.pdf", @"PROJETO\INICIAL\planta.pdf" }, arq1.Select(a => a.Relativo));
        Assert.All(arq1, a => Assert.False(a.JaEstava));

        // Duas reapresentações novas vão para a MESMA REVISÃO nova; ReapresentacaoAtual, para a última.
        var plano = Arquivamento.Planejar(p, new[] { (planta, DestinoAnexo.ReapresentacaoNova), (corte, DestinoAnexo.ReapresentacaoNova) });
        Assert.Equal(new[] { @"PROJETO\REVISÃO 01\planta.pdf", @"PROJETO\REVISÃO 01\corte.pdf" }, plano.Select(x => x.Relativo));
        var arq2 = Arquivamento.Arquivar(p, plano, CancellationToken.None);
        Assert.Equal(new[] { @"PROJETO\REVISÃO 01\planta.pdf", @"PROJETO\REVISÃO 01\corte.pdf" }, arq2.Select(a => a.Relativo));
        var extra = Origem(t, "detalhe.pdf", "detalhe");
        var arq3 = Arquivamento.Arquivar(p, Arquivamento.Planejar(p, new[] { (extra, DestinoAnexo.ReapresentacaoAtual) }), CancellationToken.None);
        Assert.Equal(@"PROJETO\REVISÃO 01\detalhe.pdf", Assert.Single(arq3).Relativo);
        // A próxima reapresentação nova abre a REVISÃO 02.
        Assert.Equal(@"PROJETO\REVISÃO 02\planta.pdf", Arquivamento.Planejar(p, new[] { (planta, DestinoAnexo.ReapresentacaoNova) })[0].Relativo);

        // Mesmo conteúdo e nome: já estava; nada é copiado.
        var antes = ArquivosDe(p);
        var repetido = Arquivamento.Planejar(p, new[] { (planta, DestinoAnexo.ProjetoInicial) });
        Assert.True(Assert.Single(repetido).JaEstava);
        var arq4 = Arquivamento.Arquivar(p, repetido, CancellationToken.None);
        Assert.True(Assert.Single(arq4).JaEstava);
        Assert.Equal(@"PROJETO\INICIAL\planta.pdf", arq4[0].Relativo);
        Assert.Equal(antes, ArquivosDe(p));

        // Mesmo nome com conteúdo diferente: "(2)", depois "(3)".
        var dir2 = Path.Combine(t, "origem2"); Directory.CreateDirectory(dir2);
        var planta2 = Path.Combine(dir2, "planta.pdf"); File.WriteAllText(planta2, "planta B");
        var dir3 = Path.Combine(t, "origem3"); Directory.CreateDirectory(dir3);
        var planta3 = Path.Combine(dir3, "planta.pdf"); File.WriteAllText(planta3, "planta C");
        var a2 = Arquivamento.Arquivar(p, Arquivamento.Planejar(p, new[] { (planta2, DestinoAnexo.ProjetoInicial) }), CancellationToken.None);
        Assert.Equal(@"PROJETO\INICIAL\planta (2).pdf", Assert.Single(a2).Relativo);
        var a3 = Arquivamento.Arquivar(p, Arquivamento.Planejar(p, new[] { (planta3, DestinoAnexo.ProjetoInicial) }), CancellationToken.None);
        Assert.Equal(@"PROJETO\INICIAL\planta (3).pdf", Assert.Single(a3).Relativo);

        // Sha da cópia == origem; o relatório devolve o mesmo valor.
        Assert.Equal(Sha(planta3), Sha(Path.Combine(p, a3[0].Relativo)));
        Assert.Equal(Sha(planta3), a3[0].Sha256);
        Assert.Equal(new FileInfo(planta3).Length, a3[0].Tamanho);
        Assert.Equal(planta3, a3[0].Origem);

        // Nenhum ".tmp" na árvore e nenhum arquivo oculto sobrando.
        Assert.DoesNotContain(ArquivosDe(p), f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
        Assert.All(Directory.GetFiles(p, "*", SearchOption.AllDirectories), f => Assert.False(new FileInfo(f).Attributes.HasFlag(FileAttributes.Hidden)));
    }

    [Fact]
    public void origem_nunca_muda()
    {
        using var t = Ambiente.PastaDeTeste("arq-origem");
        var p = NovoProcesso(t);
        var o = Origem(t, "planta.pdf", "conteudo da planta");
        File.SetAttributes(o, FileAttributes.ReadOnly | FileAttributes.Archive);
        File.SetLastWriteTimeUtc(o, new DateTime(2020, 5, 6, 7, 8, 9, DateTimeKind.Utc));
        File.SetCreationTimeUtc(o, new DateTime(2019, 1, 2, 3, 4, 5, DateTimeKind.Utc));
        var info = new FileInfo(o);
        var antes = (Sha(o), info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc, info.Attributes);

        Arquivamento.Arquivar(p, Arquivamento.Planejar(p, new[] { (o, DestinoAnexo.Documento) }), CancellationToken.None);

        info.Refresh();
        Assert.Equal(antes, (Sha(o), info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc, info.Attributes));
        File.SetAttributes(o, FileAttributes.Normal);
    }

    [Fact]
    public void planejar_so_le_nada_e_criado()
    {
        using var t = Ambiente.PastaDeTeste("arq-planejar");
        var p = NovoProcesso(t);
        var o = Origem(t, "planta.pdf", "x");
        var antes = Directory.GetFileSystemEntries(p, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray();
        var plano = Arquivamento.Planejar(p, new[] { (o, DestinoAnexo.ReapresentacaoNova), (o, DestinoAnexo.Documento) });
        Assert.Equal(@"PROJETO\REVISÃO 01\planta.pdf", plano[0].Relativo);
        Assert.Equal(@"DOCUMENTOS\planta.pdf", plano[1].Relativo);
        Assert.Equal(antes, Directory.GetFileSystemEntries(p, "*", SearchOption.AllDirectories).OrderBy(x => x).ToArray());
        Assert.False(Directory.Exists(Path.Combine(p, "PROJETO", "REVISÃO 01")));
    }

    [Fact]
    public void anexos_recusados_com_a_mensagem()
    {
        using var t = Ambiente.PastaDeTeste("arq-recusas");
        var p = NovoProcesso(t);
        var bom = Origem(t, "ok.pdf", "ok");
        var pastaAnexo = Path.Combine(t, "origem", "uma pasta"); Directory.CreateDirectory(pastaAnexo);
        var lnk = Origem(t, "atalho.lnk", "x");
        var url = Origem(t, "site.url", "[InternetShortcut]");
        var inexistente = Path.Combine(t, "origem", "sumiu.pdf");
        var alvo = Origem(t, "alvo.pdf", "alvo");
        var simbolico = Path.Combine(t, "origem", "link.pdf");
        var temSimbolico = true;
        try { File.CreateSymbolicLink(simbolico, alvo); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { temSimbolico = false; }

        var pedidos = new List<(string, DestinoAnexo)> { (pastaAnexo, DestinoAnexo.Documento), (lnk, DestinoAnexo.Documento), (url, DestinoAnexo.Documento), (inexistente, DestinoAnexo.Documento), (bom, DestinoAnexo.Documento) };
        if (temSimbolico) pedidos.Insert(3, (simbolico, DestinoAnexo.Documento));
        var plano = Arquivamento.Planejar(p, pedidos);

        Assert.Equal("Arraste arquivos, não pastas: uma pasta.", plano[0].Recusa);
        Assert.Equal("atalho.lnk é um atalho; anexe o arquivo de destino.", plano[1].Recusa);
        Assert.Equal("site.url é um atalho; anexe o arquivo de destino.", plano[2].Recusa);
        if (temSimbolico) Assert.Equal("link.pdf é um atalho; anexe o arquivo de destino.", plano[3].Recusa);
        var i = temSimbolico ? 4 : 3;
        Assert.Equal("sumiu.pdf não pôde ser lido: o arquivo não foi encontrado.", plano[i].Recusa);
        Assert.Null(plano[i + 1].Recusa);
        Assert.Equal(@"DOCUMENTOS\ok.pdf", plano[i + 1].Relativo);
        Assert.All(plano.Take(i), x => Assert.Null(x.Relativo));

        // Os outros seguem: só o bom é arquivado.
        var arq = Arquivamento.Arquivar(p, plano, CancellationToken.None);
        Assert.Equal(@"DOCUMENTOS\ok.pdf", Assert.Single(arq).Relativo);
        Assert.Equal(new[] { @"DOCUMENTOS\ok.pdf" }, ArquivosDe(p));
    }

    [Fact]
    public void arquivo_preso_para_leitura_e_recusado_com_o_motivo()
    {
        using var t = Ambiente.PastaDeTeste("arq-preso");
        var p = NovoProcesso(t);
        var o = Origem(t, "preso.pdf", "x");
        using var trava = new FileStream(o, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var plano = Arquivamento.Planejar(p, new[] { (o, DestinoAnexo.Documento) });
        var recusa = Assert.Single(plano).Recusa;
        Assert.StartsWith("preso.pdf não pôde ser lido: ", recusa);
        Assert.EndsWith(".", recusa);
    }

    [Fact]
    public void mesmo_nome_na_mesma_remessa_nao_se_sobrescreve()
    {
        using var t = Ambiente.PastaDeTeste("arq-remessa");
        var p = NovoProcesso(t);
        var d1 = Path.Combine(t, "a"); Directory.CreateDirectory(d1);
        var d2 = Path.Combine(t, "b"); Directory.CreateDirectory(d2);
        File.WriteAllText(Path.Combine(d1, "planta.pdf"), "um");
        File.WriteAllText(Path.Combine(d2, "planta.pdf"), "dois");
        var arq = Arquivamento.Arquivar(p, Arquivamento.Planejar(p, new[] { (Path.Combine(d1, "planta.pdf"), DestinoAnexo.Documento), (Path.Combine(d2, "planta.pdf"), DestinoAnexo.Documento) }), CancellationToken.None);
        Assert.Equal(new[] { @"DOCUMENTOS\planta.pdf", @"DOCUMENTOS\planta (2).pdf" }, arq.Select(a => a.Relativo));
        Assert.Equal("um", File.ReadAllText(Path.Combine(p, arq[0].Relativo)));
        Assert.Equal("dois", File.ReadAllText(Path.Combine(p, arq[1].Relativo)));
    }

    [Fact]
    public void cancelar_antes_de_copiar_nao_deixa_nada()
    {
        using var t = Ambiente.PastaDeTeste("arq-cancelar");
        var p = NovoProcesso(t);
        var o = Origem(t, "planta.pdf", "x");
        var plano = Arquivamento.Planejar(p, new[] { (o, DestinoAnexo.Documento) });
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => Arquivamento.Arquivar(p, plano, cts.Token));
        Assert.Empty(ArquivosDe(p));
    }
}
