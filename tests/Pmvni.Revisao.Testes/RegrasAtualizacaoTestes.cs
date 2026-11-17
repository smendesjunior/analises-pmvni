using System.Security.Cryptography;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class RegrasAtualizacaoTestes : IDisposable
{
    readonly string pasta = Path.Combine(Path.GetTempPath(), "pmvni-atualizacao-" + Guid.NewGuid().ToString("N"));

    public RegrasAtualizacaoTestes() => Directory.CreateDirectory(pasta);

    public void Dispose() { try { Directory.Delete(pasta, true); } catch (IOException) { } }

    const string Atual = "20261115120000";

    static string Url(string tag = "build-20261116000000") => RegrasAtualizacao.PrefixoDownload + tag + "/" + RegrasAtualizacao.NomeExecutavelPublicado;

    (string Arquivo, ManifestoAtualizacao Manifesto) Executavel(string nome, int tamanho = 2_000_000, string compilacao = "20261116000000", byte enchimento = 7)
    {
        var bytes = Enumerable.Repeat(enchimento, tamanho).ToArray();
        bytes[0] = 0x4D; bytes[1] = 0x5A;
        var arquivo = Path.Combine(pasta, nome);
        File.WriteAllBytes(arquivo, bytes);
        return (arquivo, new ManifestoAtualizacao("0.1.0", compilacao, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, Url(), "Notas"));
    }

    [Fact]
    public void Le_o_manifesto_e_recusa_o_que_nao_e_manifesto()
    {
        var m = RegrasAtualizacao.Ler("﻿{\"Version\":\"0.1.0\",\"Build\":\"20261116000000\",\"Sha256\":\"ab\",\"Size\":123,\"Url\":\"u\",\"Notes\":\"n\"}");
        Assert.Equal(new ManifestoAtualizacao("0.1.0", "20261116000000", "ab", 123, "u", "n"), m);
        Assert.Null(RegrasAtualizacao.Ler("<html>404</html>"));
        Assert.Null(RegrasAtualizacao.Ler("{\"Build\":\"1\",\"Sha256\":\"x\",\"Size\":\"grande\"}"));
        Assert.Null(RegrasAtualizacao.Ler("[]"));
        Assert.Null(RegrasAtualizacao.Ler(null));
    }

    [Fact]
    public void So_aceita_compilacao_mais_nova_do_proprio_repositorio()
    {
        var bom = new ManifestoAtualizacao("0.1.0", "20261116000000", new string('a', 64), 90_000_000, Url(), null);
        Assert.True(RegrasAtualizacao.Aceitavel(bom, Atual));
        Assert.False(RegrasAtualizacao.Aceitavel(bom with { Compilacao = Atual }, Atual));
        Assert.False(RegrasAtualizacao.Aceitavel(bom with { Compilacao = "20261101000000" }, Atual));
        Assert.False(RegrasAtualizacao.Aceitavel(bom with { Compilacao = "2026111600000x" }, Atual));
        Assert.False(RegrasAtualizacao.Aceitavel(bom with { Url = "https://github.com/outro/repo/releases/download/x/a.exe" }, Atual));
        Assert.False(RegrasAtualizacao.Aceitavel(bom with { Url = RegrasAtualizacao.PrefixoDownload + "../../x.exe" }, Atual));
        Assert.False(RegrasAtualizacao.Aceitavel(bom with { Url = null }, Atual));
        Assert.False(RegrasAtualizacao.Aceitavel(bom with { Sha256 = "zz" }, Atual));
        Assert.False(RegrasAtualizacao.Aceitavel(bom with { Tamanho = 0 }, Atual));
    }

    [Fact]
    public void Confere_tamanho_hash_e_cabecalho()
    {
        var (arquivo, m) = Executavel("novo.exe");
        RegrasAtualizacao.Conferir(arquivo, m);
        Assert.True(RegrasAtualizacao.PodeReaproveitar(arquivo, m));
        Assert.Throws<InvalidDataException>(() => RegrasAtualizacao.Conferir(arquivo, m with { Tamanho = m.Tamanho + 1 }));
        Assert.Throws<InvalidDataException>(() => RegrasAtualizacao.Conferir(arquivo, m with { Sha256 = new string('0', 64) }));
        Assert.False(RegrasAtualizacao.PodeReaproveitar(Path.Combine(pasta, "nao-existe.exe"), m));
    }

    [Fact]
    public void Troca_o_executavel_e_guarda_o_antigo_para_apagar_depois()
    {
        var (atual, _) = Executavel("Editor.exe", enchimento: 1);
        var (novo, m) = Executavel("baixado.exe", enchimento: 2);
        using (File.Open(atual, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete)) // como o .exe em uso
            RegrasAtualizacao.TrocarExecutavel(atual, novo, m);
        Assert.Equal(File.ReadAllBytes(novo), File.ReadAllBytes(atual));
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));
        RegrasAtualizacao.LimparAntigos(pasta);
        Assert.Empty(Directory.GetFiles(pasta, RegrasAtualizacao.PrefixoAntigo + "*"));
    }

    [Fact]
    public void Troca_recusada_deixa_o_executavel_como_estava()
    {
        var (atual, _) = Executavel("Editor.exe", enchimento: 1);
        var antes = File.ReadAllBytes(atual);
        var (novo, m) = Executavel("baixado.exe", enchimento: 2);
        Assert.Throws<InvalidDataException>(() => RegrasAtualizacao.TrocarExecutavel(atual, novo, m with { Sha256 = new string('0', 64) }));
        Assert.Equal(antes, File.ReadAllBytes(atual));
        Assert.Equal(2, Directory.GetFiles(pasta).Length);
    }

    [Fact]
    public void Texto_da_versao_e_intervalos()
    {
        Assert.Equal("1.0 · compilação 20261116021306", RegrasAtualizacao.TextoVersao("1.0.0", "20261116021306"));
        Assert.Equal("0.6.8 · compilação de desenvolvimento", RegrasAtualizacao.TextoVersao("0.6.8", null));
        // 1.0.0: a versão exibida ao usuário omite a parte final zerada; a técnica não muda.
        Assert.Equal("1.0", RegrasAtualizacao.VersaoExibida("1.0.0"));
        Assert.Equal("1.0", RegrasAtualizacao.VersaoExibida("1.0.0.0"));
        Assert.Equal("1.2.3", RegrasAtualizacao.VersaoExibida("1.2.3"));
        Assert.Equal("1.1", RegrasAtualizacao.VersaoExibida("1.1.0"));
        Assert.Equal("2.0", RegrasAtualizacao.VersaoExibida("2.0"));
        Assert.Equal("", RegrasAtualizacao.VersaoExibida(null));
        Assert.Equal("dev", RegrasAtualizacao.VersaoExibida("dev"));
        Assert.Equal(TimeSpan.FromMinutes(15), RegrasAtualizacao.ProximaConsulta(2));
        Assert.Equal(TimeSpan.FromHours(6), RegrasAtualizacao.ProximaConsulta(3));
        Assert.StartsWith("https://github.com/smendesjunior/analises-pmvni/releases/latest/download/", RegrasAtualizacao.UrlManifesto);
    }
}
