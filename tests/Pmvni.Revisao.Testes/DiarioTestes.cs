using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class DiarioTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar()
    {
        var caminho = Ambiente.Corpus("curto.json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    /// <summary>5 comandos e 1 desfazer, todos registrados no diario.</summary>
    private static (SessaoRevisao Sessao, List<RegistroHistorico> Feitos) Trabalho(ConteudoPacote c, Diario d)
    {
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto("ana"));
        var feitos = new List<RegistroHistorico>();
        s.Registrado += r => { d.Registrar(r); feitos.Add(r); };
        var id = s.Projeto.Incluidos[0].Id;
        for (var i = 1; i <= 5; i++) s.Aplicar(new DefinirCampo(id, "titulo", "Titulo editado " + i));
        s.Desfazer();
        return (s, feitos);
    }

    [Fact]
    public void diario_registra_e_le_na_ordem()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-ordem");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        using var d = Diario.Iniciar(pasta, pacote, "abc");
        var (_, feitos) = Trabalho(Importar(), d);

        var rec = Diario.Ler(pasta, pacote, "abc")!;

        Assert.Equal(6, rec.Registros.Count);
        Assert.Equal(feitos.Select(r => r.N), rec.Registros.Select(r => r.N));
        Assert.True(rec.DaMesmaVersao);
        Assert.Equal(feitos.Max(r => r.DataUtc), rec.UltimoUtc);
    }

    [Fact]
    public void linha_truncada_no_fim_e_ignorada()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-truncado");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        using var d = Diario.Iniciar(pasta, pacote, "abc");
        var (_, feitos) = Trabalho(Importar(), d);
        d.Dispose();
        File.AppendAllText(Diario.CaminhoDe(pasta, pacote), "{\"n\":9,");

        var rec = Diario.Ler(pasta, pacote, "abc")!;

        Assert.Equal(feitos.Count, rec.Registros.Count);
    }

    [Fact]
    public void diario_de_outra_versao_e_marcado()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-versao");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        using var d = Diario.Iniciar(pasta, pacote, "abc");
        Trabalho(Importar(), d);

        Assert.False(Diario.Ler(pasta, pacote, "outro")!.DaMesmaVersao);
    }

    [Fact]
    public void diario_sem_registros_ou_inexistente_devolve_null_e_descartar_apaga()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-vazio");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        Assert.Null(Diario.Ler(pasta, pacote, "abc"));

        var d = Diario.Iniciar(pasta, pacote, "abc");
        Assert.Null(Diario.Ler(pasta, pacote, "abc"));
        Trabalho(Importar(), d);
        Assert.NotNull(Diario.Ler(pasta, pacote, "abc"));
        d.Descartar();
        Assert.False(File.Exists(Diario.CaminhoDe(pasta, pacote)));
    }

    [Fact]
    public void arquivar_renomeia_para_orfao()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-orfao");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var d = Diario.Iniciar(pasta, pacote, "abc");
        Trabalho(Importar(), d);
        d.Dispose();

        var novo = Diario.Arquivar(pasta, pacote);

        Assert.Matches(@"^[0-9a-f]{64}\.orfao-\d{14}\.jsonl$", Path.GetFileName(novo));
        Assert.True(File.Exists(novo));
        Assert.Null(Diario.Ler(pasta, pacote, "abc"));
    }

    [Fact]
    public void diario_aplicado_reproduz_a_sessao()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-reproduz");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        using var d = Diario.Iniciar(pasta, pacote, "abc");
        var conteudo = Importar();
        var base0 = ProjetoJson.Escrever(conteudo.Projeto); // o pacote como estava gravado
        var (original, _) = Trabalho(conteudo, d);

        var s2 = new SessaoRevisao(ProjetoJson.Ler(base0), conteudo.Historico, Ambiente.Contexto("ana"));
        var rec = Diario.Ler(pasta, pacote, "abc")!;
        Assert.Empty(rec.Divergencias(s2.HistoricoCompleto));
        s2.Reaplicar(rec.Registros);

        Assert.Equal(ProjetoJson.Escrever(original.Projeto), ProjetoJson.Escrever(s2.Projeto));
        Assert.Equal(original.PodeRefazer, s2.PodeRefazer);
    }

    [Fact]
    public void divergencia_com_o_historico_do_pacote_e_visivel()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-diverge");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        using var d = Diario.Iniciar(pasta, pacote, "abc");
        var (_, feitos) = Trabalho(Importar(), d);
        var rec = Diario.Ler(pasta, pacote, "abc")!;

        // o pacote tem um registro de mesmo N, mas com outro conteudo
        var diferente = feitos[0] with { Usuario = "outra pessoa" };
        Assert.Equal(new[] { feitos[0].N }, rec.Divergencias(new[] { diferente }));
        Assert.Empty(rec.Divergencias(new[] { feitos[0] }));
    }

    [Fact]
    public void caminho_do_diario_e_estavel_e_sem_dados_do_processo()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-caminho");
        var a = Diario.CaminhoDe(pasta, Path.Combine(pasta, "Processo 123.rpmvni"));
        var b = Diario.CaminhoDe(pasta, Path.Combine(pasta, "PROCESSO 123.RPMVNI"));

        Assert.Equal(a, b);
        Assert.Matches("^[0-9a-f]{64}\\.jsonl$", Path.GetFileName(a));
        Assert.Equal("recuperacao", Path.GetFileName(Path.GetDirectoryName(a)));
        Assert.DoesNotContain("123", Path.GetFileName(a));
    }

    [Fact]
    public void iniciar_nao_destroi_diario_com_registros()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-iniciar");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var d1 = Diario.Iniciar(pasta, pacote, "abc");
        var (_, feitos) = Trabalho(Importar(), d1);
        d1.Dispose();

        using var d2 = Diario.Iniciar(pasta, pacote, "abc");

        var orfaos = Directory.GetFiles(Path.GetDirectoryName(Diario.CaminhoDe(pasta, pacote))!, "*.orfao-*.jsonl");
        Assert.Single(orfaos);
        Assert.Equal(feitos.Count, File.ReadAllLines(orfaos[0]).Length - 1);
        Assert.Null(Diario.Ler(pasta, pacote, "abc")); // o novo comeca vazio
    }

    [Fact]
    public void iniciar_sobre_diario_sem_registros_nao_cria_orfao()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-iniciar-vazio");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        Diario.Iniciar(pasta, pacote, "abc").Dispose();
        Diario.Iniciar(pasta, pacote, "abc").Dispose();

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(Diario.CaminhoDe(pasta, pacote))!, "*.orfao-*"));
    }

    [Fact]
    public void arquivar_duas_vezes_no_mesmo_segundo_nao_sobrescreve()
    {
        using var pasta = Ambiente.PastaDeTeste("diario-orfao2");
        var pacote = Path.Combine(pasta, "a.rpmvni");
        var nomes = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var d = Diario.Iniciar(pasta, pacote, "abc");
            Trabalho(Importar(), d);
            d.Dispose();
            nomes.Add(Diario.Arquivar(pasta, pacote));
        }

        Assert.Equal(3, nomes.Distinct().Count());
        Assert.All(nomes, n => Assert.True(File.Exists(n)));
    }
}
