using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Anexos compactados no Novo relatório (pedido do usuário, 2026-11-16): o .zip, o .rar e o .7z são abertos pelo editor e cada arquivo
/// de dentro vira um anexo comum, com o destino sugerido pela regra de organização do kit.</summary>
public sealed class CompactadosTestes : IDisposable
{
    readonly string pasta = Path.Combine(Path.GetTempPath(), "pmvni-teste-compactados-" + Guid.NewGuid().ToString("N"));

    public CompactadosTestes() => Directory.CreateDirectory(pasta);

    public void Dispose()
    {
        try { Directory.Delete(pasta, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    string Zip(string nome, params (string Caminho, string Texto)[] entradas)
    {
        var caminho = Path.Combine(pasta, nome);
        using var z = ZipFile.Open(caminho, ZipArchiveMode.Create);
        foreach (var (c, t) in entradas)
        {
            using var s = z.CreateEntry(c).Open();
            s.Write(Encoding.UTF8.GetBytes(t));
        }
        return caminho;
    }

    [Fact]
    public void zip_com_pastas_e_zip_dentro_vira_arquivos_soltos_sem_lixo()
    {
        var interno = Zip("interno.zip", ("DWG/planta.dwg", "dwg"));
        var zip = Zip("Processo 123.zip",
            ("PROJETO/Prancha 01 - Implantação.pdf", "p1"),
            ("PROJETO/Prancha 02.pdf", "p2"),
            ("DOCUMENTOS/Matrícula 4512.pdf", "m"),
            ("__MACOSX/PROJETO/._Prancha 01.pdf", "lixo"),
            ("Thumbs.db", "lixo"));
        using (var z = ZipFile.Open(zip, ZipArchiveMode.Update)) z.CreateEntryFromFile(interno, "anexos/interno.zip");

        var r = Compactados.Extrair(zip, Path.Combine(pasta, "saida"), CancellationToken.None);

        var internos = r.Arquivos.Select(a => a.Interno).ToList();
        Assert.Equal(["anexos/interno.zip/DWG/planta.dwg", "DOCUMENTOS/Matrícula 4512.pdf", "PROJETO/Prancha 01 - Implantação.pdf", "PROJETO/Prancha 02.pdf"],
            internos.Order(StringComparer.OrdinalIgnoreCase).ToList());
        Assert.All(r.Arquivos, a => Assert.True(File.Exists(a.Caminho)));
        Assert.All(r.Arquivos, a => Assert.Equal("Processo 123.zip", a.Compactado));
        Assert.Equal("p1", File.ReadAllText(r.Arquivos.Single(a => a.Interno.EndsWith("Implantação.pdf", StringComparison.Ordinal)).Caminho));
        Assert.Empty(r.Avisos);
    }

    [Fact]
    public void zip_com_nomes_repetidos_abre_tudo_e_renomeia_o_segundo()
    {
        var zip = Zip("repetidos.zip", ("Planta.pdf", "a"), ("planta.pdf", "b"), ("PROJETO/Corte.pdf", "c"));

        var r = Compactados.Extrair(zip, Path.Combine(pasta, "saida"), CancellationToken.None);

        Assert.Equal(3, r.Arquivos.Count);
        Assert.Contains(r.Arquivos, a => Path.GetFileName(a.Caminho) == "planta (2).pdf");
        Assert.Equal(["a", "b"], r.Arquivos.Where(a => a.Caminho.Contains("lanta", StringComparison.Ordinal)).Select(a => File.ReadAllText(a.Caminho)).Order().ToList());
    }

    [Fact]
    public void limpar_antigos_nunca_apaga_a_pasta_da_sessao_aberta()
    {
        var raiz = Compactados.PastaTemporaria();
        var sessao = Path.Combine(raiz, "teste-sessao-" + Guid.NewGuid().ToString("N"));
        var velha = Path.Combine(raiz, "teste-velha-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessao);
        Directory.CreateDirectory(velha);
        Directory.SetLastWriteTimeUtc(sessao, DateTime.UtcNow.AddDays(-3));
        Directory.SetLastWriteTimeUtc(velha, DateTime.UtcNow.AddDays(-3));
        try
        {
            Compactados.LimparAntigos(TimeSpan.FromDays(1), exceto: sessao + Path.DirectorySeparatorChar);
            Assert.True(Directory.Exists(sessao));
            Assert.False(Directory.Exists(velha));
        }
        finally { if (Directory.Exists(sessao)) Directory.Delete(sessao, true); }
    }

    [Fact]
    public void zip_com_caminho_que_sai_da_pasta_e_recusado()
    {
        var zip = Zip("malicioso.zip", ("../../fora.txt", "x"));
        var e = Assert.Throws<InvalidDataException>(() => Compactados.Extrair(zip, Path.Combine(pasta, "saida"), CancellationToken.None));
        Assert.Contains("sai da pasta", e.Message);
        Assert.False(File.Exists(Path.Combine(pasta, "fora.txt")));
    }

    [Fact]
    public void arquivo_corrompido_da_mensagem_clara()
    {
        var falso = Path.Combine(pasta, "quebrado.zip");
        File.WriteAllText(falso, "isto não é um zip");
        var e = Assert.Throws<InvalidDataException>(() => Compactados.Extrair(falso, Path.Combine(pasta, "saida"), CancellationToken.None));
        Assert.StartsWith("quebrado.zip não pôde ser aberto", e.Message);
    }

    [Fact]
    public void sete_zip_abre_pelo_tar_do_windows()
    {
        var tar = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(tar)) return;   // Windows sem tar.exe: o editor avisa (coberto pela mensagem); nada a testar aqui
        var origem = Path.Combine(pasta, "origem");
        Directory.CreateDirectory(Path.Combine(origem, "PROJETO"));
        File.WriteAllText(Path.Combine(origem, "PROJETO", "corte.pdf"), "c");
        File.WriteAllText(Path.Combine(origem, "requerimento.pdf"), "r");
        var arquivo = Path.Combine(pasta, "envio.7z");
        var si = new ProcessStartInfo(tar) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = origem };
        foreach (var a in new[] { "--format", "7zip", "-c", "-f", arquivo, "PROJETO", "requerimento.pdf" }) si.ArgumentList.Add(a);
        using (var p = Process.Start(si)!) p.WaitForExit();
        if (!File.Exists(arquivo)) return;   // tar.exe antigo, sem gravação de 7z

        var r = Compactados.Extrair(arquivo, Path.Combine(pasta, "saida"), CancellationToken.None);
        Assert.Equal(["PROJETO/corte.pdf", "requerimento.pdf"], r.Arquivos.Select(x => x.Interno).Order(StringComparer.Ordinal).ToList());
    }

    [Theory]
    [InlineData("DOCUMENTOS/qualquer.pdf", DestinoAnexo.Documento)]
    [InlineData("Matrícula 4512.pdf", DestinoAnexo.Documento)]
    [InlineData("RRT assinado.pdf", DestinoAnexo.Documento)]
    [InlineData("ART_projeto.pdf", DestinoAnexo.Documento)]
    [InlineData("Requerimento.pdf", DestinoAnexo.Documento)]
    [InlineData("memorial descritivo.pdf", DestinoAnexo.Documento)]
    [InlineData("PROJETO/Prancha 01.pdf", DestinoAnexo.ProjetoInicial)]
    [InlineData("planta baixa.dwg", DestinoAnexo.ProjetoInicial)]
    [InlineData("IMG_4680.JPEG", DestinoAnexo.ProjetoInicial)]
    [InlineData("parte superior.pdf", DestinoAnexo.ProjetoInicial)]   // "art" só como palavra inteira
    public void destino_sugerido_segue_a_regra_do_kit(string interno, DestinoAnexo esperado) =>
        Assert.Equal(esperado, Compactados.SugerirDestino(interno, DestinoAnexo.ProjetoInicial));

    [Fact]
    public void reconhece_as_extensoes_compactadas()
    {
        Assert.True(Compactados.EhCompactado(@"C:\x\projeto.ZIP"));
        Assert.True(Compactados.EhCompactado("a.rar"));
        Assert.True(Compactados.EhCompactado("a.7z"));
        Assert.False(Compactados.EhCompactado("a.pdf"));
        Assert.False(Compactados.EhCompactado("a.dwg"));
    }
}
