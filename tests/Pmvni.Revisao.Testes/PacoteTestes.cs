using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class PacoteTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar(string caso)
    {
        var caminho = Ambiente.Corpus(caso + ".json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static string ShaDoArquivo(string caminho) => Sha(File.ReadAllBytes(caminho));

    /// <summary>Lê as entradas de um pacote, deixa o teste alterá-las e grava um ZIP novo (sem passar pelo <see cref="Pacote.Gravar"/>).</summary>
    private static void Reescrever(string origem, string destino, Action<List<(string Nome, byte[] Bytes)>> altera)
    {
        var entradas = new List<(string Nome, byte[] Bytes)>();
        using (var zip = ZipFile.OpenRead(origem))
            foreach (var e in zip.Entries)
            {
                using var s = e.Open();
                using var m = new MemoryStream();
                s.CopyTo(m);
                entradas.Add((e.FullName, m.ToArray()));
            }
        altera(entradas);
        using var fs = new FileStream(destino, FileMode.Create, FileAccess.Write);
        using var novo = new ZipArchive(fs, ZipArchiveMode.Create);
        foreach (var (nome, bytes) in entradas)
        {
            using var s = novo.CreateEntry(nome, CompressionLevel.Optimal).Open();
            s.Write(bytes, 0, bytes.Length);
        }
    }

    private static int Indice(List<(string Nome, byte[] Bytes)> e, string nome) => e.FindIndex(x => x.Nome == nome);

    private static string PacoteBom(PastaTeste pasta, string caso = "curto")
    {
        var caminho = Path.Combine(pasta, "bom.rpmvni");
        Pacote.Gravar(caminho, Importar(caso), null);
        return caminho;
    }

    /// <summary>Muda o manifesto de <paramref name="entradas"/> sem tocar nas outras partes.</summary>
    private static void NoManifesto(List<(string Nome, byte[] Bytes)> entradas, Func<Manifesto, Manifesto> altera)
    {
        var i = Indice(entradas, "manifesto.json");
        entradas[i] = (entradas[i].Nome, altera(Manifesto.Ler(entradas[i].Bytes)).Escrever());
    }

    [Fact]
    public void ida_e_volta_preserva_tudo()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-ida");
        var c = Importar("longo");
        var caminho = Path.Combine(pasta, "relatorio.rpmvni");

        var estado = Pacote.Gravar(caminho, c, null);
        var lido = Pacote.Ler(caminho);

        Assert.False(lido.SomenteLeitura);
        Assert.Null(lido.MotivoSomenteLeitura);
        Assert.Equal(estado, lido.Disco);
        Assert.Equal(estado.Sha256, ShaDoArquivo(caminho));
        Assert.Equal(c.Recebido, lido.Conteudo.Recebido);
        Assert.Equal(ProjetoJson.Escrever(c.Projeto), ProjetoJson.Escrever(lido.Conteudo.Projeto));
        Assert.Equal(HistoricoJsonl.Escrever(c.Historico), HistoricoJsonl.Escrever(lido.Conteudo.Historico));
        Assert.Equal(9, lido.Conteudo.Assets.Count);
        Assert.Equal(c.Assets.Keys.OrderBy(k => k, StringComparer.Ordinal), lido.Conteudo.Assets.Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var (chave, bytes) in c.Assets) Assert.Equal(bytes, lido.Conteudo.Assets[chave]);
        Assert.Equal(c.Manifesto.Partes.OrderBy(p => p.Key, StringComparer.Ordinal), lido.Conteudo.Manifesto.Partes.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(c.Manifesto.CriadoEm, lido.Conteudo.Manifesto.CriadoEm);
    }

    [Fact]
    public void gravacao_que_falha_antes_da_troca_deixa_o_antigo_intacto()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-falha");
        var c = Importar("curto");
        var caminho = Path.Combine(pasta, "r.rpmvni");
        var estado = Pacote.Gravar(caminho, c, null);
        var antes = ShaDoArquivo(caminho);

        var novo = c with { Historico = [.. c.Historico, Registro(2)] };
        Assert.Throws<IOException>(() => Pacote.Gravar(caminho, novo, estado, () => throw new IOException("disco cheio")));

        Assert.Equal(antes, ShaDoArquivo(caminho));
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));
        Assert.Equal(estado, EstadoDisco.Ler(caminho));
    }

    [Fact]
    public void destino_preso_por_outro_processo_nao_corrompe()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-preso");
        var c = Importar("curto");
        var caminho = Path.Combine(pasta, "r.rpmvni");
        var estado = Pacote.Gravar(caminho, c, null);
        var antes = File.ReadAllBytes(caminho);

        var novo = c with { Historico = [.. c.Historico, Registro(2)] };
        using (new FileStream(caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            Assert.ThrowsAny<IOException>(() => Pacote.Gravar(caminho, novo, estado));

        Assert.Equal(antes, File.ReadAllBytes(caminho));
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));
        Assert.Equal(estado, EstadoDisco.Ler(caminho));
    }

    [Fact]
    public void conflito_detectado_nao_sobrescreve()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-conflito");
        var c = Importar("curto");
        var caminho = Path.Combine(pasta, "r.rpmvni");
        var e1 = Pacote.Gravar(caminho, c, null);

        var dooutro = c with { Historico = [.. c.Historico, Registro(2)] };
        var e2 = Pacote.Gravar(caminho, dooutro, e1); // "outro processo" grava
        var bytesDoOutro = File.ReadAllBytes(caminho);

        var meu = c with { Historico = [.. c.Historico, Registro(2), Registro(3)] };
        var ex = Assert.Throws<ConflitoNoDiscoException>(() => Pacote.Gravar(caminho, meu, e1));

        Assert.Equal(e2, ex.Atual);
        Assert.Equal(bytesDoOutro, File.ReadAllBytes(caminho));
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));
        Assert.Equal(2, Pacote.Ler(caminho).Conteudo.Historico.Count);
    }

    [Fact]
    public void destino_que_apareceu_sem_estado_esperado_e_conflito()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-apareceu");
        var c = Importar("curto");
        var caminho = Path.Combine(pasta, "r.rpmvni");
        Pacote.Gravar(caminho, c, null);
        var antes = File.ReadAllBytes(caminho);

        Assert.Throws<ConflitoNoDiscoException>(() => Pacote.Gravar(caminho, c, null));
        Assert.Equal(antes, File.ReadAllBytes(caminho));
    }

    [Fact]
    public void bak_guarda_a_versao_anterior()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-bak");
        var c = Importar("curto");
        var caminho = Path.Combine(pasta, "r.rpmvni");
        var e1 = Pacote.Gravar(caminho, c, null);
        var primeira = File.ReadAllBytes(caminho);

        var e2 = Pacote.Gravar(caminho, c with { Historico = [.. c.Historico, Registro(2)] }, e1);

        Assert.NotEqual(e1.Sha256, e2.Sha256);
        Assert.Equal(primeira, File.ReadAllBytes(caminho + ".bak"));
        Assert.Equal(e2.Sha256, ShaDoArquivo(caminho));
    }

    [Fact]
    public void tmp_fica_oculto()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-oculto");
        var caminho = Path.Combine(pasta, "r.rpmvni");
        var viu = false;
        Pacote.Gravar(caminho, Importar("curto"), null, () =>
        {
            var tmp = caminho + ".tmp";
            Assert.True(File.Exists(tmp));
            Assert.True(File.GetAttributes(tmp).HasFlag(FileAttributes.Hidden));
            viu = true;
        });
        Assert.True(viu);
        Assert.False(File.Exists(caminho + ".tmp"));
    }

    [Fact]
    public void pacote_final_nao_fica_oculto_nem_na_troca_nem_na_criacao()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-visivel");
        var c = Importar("curto");
        var caminho = Path.Combine(pasta, "r.rpmvni");

        var e1 = Pacote.Gravar(caminho, c, null); // File.Move
        Assert.False(File.GetAttributes(caminho).HasFlag(FileAttributes.Hidden));

        Pacote.Gravar(caminho, c with { Historico = [.. c.Historico, Registro(2)] }, e1); // File.Replace
        Assert.False(File.GetAttributes(caminho).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void leitura_do_destino_falha_depois_da_troca_a_gravacao_continua_sucesso()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-pos-troca");
        var c = Importar("curto");
        var caminho = Path.Combine(pasta, "r.rpmvni");
        var e1 = Pacote.Gravar(caminho, c, null);

        var novo = c with { Historico = [.. c.Historico, Registro(2)] };
        FileStream? preso = null;
        EstadoDisco e2;
        try
        {
            // Logo depois da troca, "o antivírus" segura o arquivo novo sem compartilhar: a releitura do estado falha.
            e2 = Pacote.Gravar(caminho, novo, e1, depoisDeTrocar: () => preso = new FileStream(caminho, FileMode.Open, FileAccess.Read, FileShare.None));
            Assert.NotNull(preso);
        }
        finally { preso?.Dispose(); }

        Assert.Equal(ShaDoArquivo(caminho), e2.Sha256);
        Assert.Equal(new FileInfo(caminho).Length, e2.Tamanho);
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));

        // A gravação seguinte, com o estado devolvido, não acusa conflito com a própria gravação anterior.
        var e3 = Pacote.Gravar(caminho, novo with { Historico = [.. novo.Historico, Registro(3)] }, e2);
        Assert.Equal(ShaDoArquivo(caminho), e3.Sha256);
        Assert.Equal(3, Pacote.Ler(caminho).Conteudo.Historico.Count);
    }

    [Fact]
    public void so_a_data_mudou_nao_e_conflito()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-data");
        var c = Importar("curto");
        var caminho = Path.Combine(pasta, "r.rpmvni");
        var e1 = Pacote.Gravar(caminho, c, null);

        File.SetLastWriteTimeUtc(caminho, File.GetLastWriteTimeUtc(caminho).AddHours(3)); // o OneDrive mexeu só na data
        Assert.NotEqual(e1.GravadoUtc, EstadoDisco.Ler(caminho).GravadoUtc);

        var e2 = Pacote.Gravar(caminho, c with { Historico = [.. c.Historico, Registro(2)] }, e1);
        Assert.Equal(2, Pacote.Ler(caminho).Conteudo.Historico.Count);
        Assert.Equal(ShaDoArquivo(caminho), e2.Sha256);
    }

    [Fact]
    public void entrada_pequena_muito_repetitiva_e_aceita()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-repetitivo");
        var bom = PacoteBom(pasta);
        var repetitivo = Path.Combine(pasta, "repetitivo.rpmvni");
        var linhas = Encoding.UTF8.GetBytes(new string('\n', 900_000)); // 900 KB que comprimem mais de 100 vezes; só linhas em branco
        Reescrever(bom, repetitivo, e =>
        {
            e[Indice(e, "historico.jsonl")] = ("historico.jsonl", linhas);
            NoManifesto(e, m => m with { Partes = new Dictionary<string, string>(m.Partes) { ["historico.jsonl"] = Sha(linhas) } });
        });

        Assert.Empty(Pacote.Ler(repetitivo).Conteudo.Historico);
    }

    [Theory]
    [InlineData("../fora.json")]
    [InlineData("/abs.json")]
    [InlineData("c:x.json")]
    [InlineData("assets\\a.png")]
    [InlineData("extra.txt")]
    public void entrada_proibida_recusa(string nome)
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-nome");
        var bom = PacoteBom(pasta);
        var ruim = Path.Combine(pasta, "ruim.rpmvni");
        Reescrever(bom, ruim, e => e.Add((nome, "{}"u8.ToArray())));

        Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(ruim));
    }

    [Fact]
    public void entrada_repetida_recusa()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-repetida");
        var bom = PacoteBom(pasta);
        var ruim = Path.Combine(pasta, "ruim.rpmvni");
        Reescrever(bom, ruim, e => e.Add(e[Indice(e, "projeto.json")]));

        var ex = Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(ruim));
        Assert.Contains("repetida", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void entrada_acima_do_limite_recusa()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-limite");
        var bom = PacoteBom(pasta);
        Assert.True(Pacote.Ler(bom).Conteudo.Projeto.Itens.Count > 0);

        var ex = Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(bom, new LimitesPacote(PorEntrada: 1024)));
        Assert.Contains("limite", ex.Message, StringComparison.Ordinal);
        Assert.NotNull(Pacote.Ler(bom)); // com o limite padrão abre
    }

    [Fact]
    public void total_acima_do_limite_recusa()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-total");
        var bom = PacoteBom(pasta);
        var ex = Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(bom, new LimitesPacote(Total: 1024)));
        Assert.Contains("limite", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void bomba_de_compressao_recusa()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-bomba");
        var bom = PacoteBom(pasta);
        var ruim = Path.Combine(pasta, "ruim.rpmvni");
        Reescrever(bom, ruim, e => e[Indice(e, "historico.jsonl")] = ("historico.jsonl", new byte[20 * 1024 * 1024]));

        var ex = Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(ruim));
        Assert.Contains("compress", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void hash_da_parte_diferente_recusa()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-hash");
        var bom = PacoteBom(pasta);
        var ruim = Path.Combine(pasta, "ruim.rpmvni");
        Reescrever(bom, ruim, e =>
        {
            var i = Indice(e, "projeto.json");
            e[i] = (e[i].Nome, [.. e[i].Bytes, (byte)' ']);
        });

        var ex = Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(ruim));
        Assert.Contains("projeto.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void parte_ausente_recusa()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-ausente");
        var bom = PacoteBom(pasta);
        var ruim = Path.Combine(pasta, "ruim.rpmvni");
        Reescrever(bom, ruim, e => e.RemoveAt(Indice(e, "historico.jsonl")));

        Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(ruim));
    }

    [Fact]
    public void arquivo_que_nao_e_zip_recusa()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-naozip");
        var caminho = Path.Combine(pasta, "x.rpmvni");
        File.WriteAllText(caminho, "isto nao e um zip");
        Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(caminho));
    }

    [Fact]
    public void asset_com_assinatura_falsa_recusa()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-assinatura");
        var bom = PacoteBom(pasta, "longo");
        var ruim = Path.Combine(pasta, "ruim.rpmvni");
        var lixo = Encoding.UTF8.GetBytes("isto nao e uma imagem");
        var nome = "assets/" + Sha(lixo) + ".png"; // nome = sha dos bytes e manifesto coerente: só a assinatura está errada
        Reescrever(bom, ruim, e =>
        {
            e.Add((nome, lixo));
            NoManifesto(e, m => m with { Partes = new Dictionary<string, string>(m.Partes) { [nome] = Sha(lixo) } });
        });

        var ex = Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(ruim));
        Assert.Contains("assinatura", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void asset_com_nome_que_nao_e_o_sha_dos_bytes_recusa()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-nomeasset");
        var bom = PacoteBom(pasta, "longo");
        var ruim = Path.Combine(pasta, "ruim.rpmvni");
        Reescrever(bom, ruim, e =>
        {
            var i = e.FindIndex(x => x.Nome.StartsWith("assets/", StringComparison.Ordinal));
            var velho = e[i].Nome;
            var falso = "assets/" + new string('a', 64) + ".png";
            var bytes = e[i].Bytes;
            e[i] = (falso, bytes);
            NoManifesto(e, m =>
            {
                var p = new Dictionary<string, string>(m.Partes);
                p.Remove(velho);
                p[falso] = Sha(bytes); // manifesto coerente com o nome falso: só "nome = sha dos bytes" está errado
                return m with { Partes = p };
            });
        });

        var ex = Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(ruim));
        Assert.Contains("sha256", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void esquema_maior_abre_so_leitura()
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-maior");
        var bom = PacoteBom(pasta);
        var novo = Path.Combine(pasta, "novo.rpmvni");
        Reescrever(bom, novo, e =>
        {
            NoManifesto(e, m => m with { VersaoEsquema = "2.0.0" });
            e.Add(("futuro/novidade.bin", [1, 2, 3])); // entrada desconhecida: ignorada
        });

        var a = Pacote.Ler(novo);
        Assert.True(a.SomenteLeitura);
        Assert.Contains("2.0.0", a.MotivoSomenteLeitura, StringComparison.Ordinal);
        Assert.NotEmpty(a.Conteudo.Projeto.Itens);

        // Esquema menor que o conhecido não existe: recusa.
        var velho = Path.Combine(pasta, "velho.rpmvni");
        Reescrever(bom, velho, e => NoManifesto(e, m => m with { VersaoEsquema = "0.9.0" }));
        Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(velho));
    }

    [Theory]
    [InlineData(@"C:\Windows\Web\Wallpaper\Windows\img0.jpg")]
    [InlineData(@"\\host\share\x.png")]
    [InlineData("//host/share/x.png")]
    [InlineData("../fora.png")]
    [InlineData("assets/../../fora.png")]
    [InlineData("img/mapa.png")]
    [InlineData("assets/0000000000000000000000000000000000000000000000000000000000000000.png")]
    public void imagem_do_documento_fora_dos_assets_do_pacote_recusa(string imagem)
    {
        using var pasta = Ambiente.PastaDeTeste("pacote-imagem");
        var bom = PacoteBom(pasta, "longo");
        foreach (var onde in new[] { "localizacao", "figura" })
        {
            var ruim = Path.Combine(pasta, onde + ".rpmvni");
            Reescrever(bom, ruim, e =>
            {
                var i = Indice(e, "projeto.json");
                var projeto = ProjetoJson.Ler(e[i].Bytes);
                if (onde == "localizacao") projeto.Documento["localizacao"]!["imagem"] = imagem;
                else projeto.Documento["figuras"]![0]!["imagem"] = imagem;
                var bytes = ProjetoJson.Escrever(projeto);
                e[i] = ("projeto.json", bytes);
                NoManifesto(e, m => m with { Partes = new Dictionary<string, string>(m.Partes) { ["projeto.json"] = Sha(bytes) } });
            });
            var ex = Assert.Throws<PacoteInvalidoException>(() => Pacote.Ler(ruim));
            Assert.Contains("imagem não é uma das imagens do pacote", ex.Message, StringComparison.Ordinal);
        }
    }

    private static RegistroHistorico Registro(int n) =>
        new(n, new DateTimeOffset(2026, 11, 14, 12, n, 0, TimeSpan.Zero), "teste", "DefinirCampo", null, null, null, null, null);
}
