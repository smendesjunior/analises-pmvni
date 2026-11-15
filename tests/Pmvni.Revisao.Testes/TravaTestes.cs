using Xunit;

namespace Pmvni.Revisao.Testes;

public class TravaTestes
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);

    private static InfoTrava Eu(string usuario, int pid) => new("PC-" + usuario, usuario, pid, T0, T0);

    [Fact]
    public void trava_viva_impede_segunda_instancia()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-viva");
        var pacote = Path.Combine(pasta, "a.pmvrel");
        using var primeira = Trava.Adquirir(pacote, Eu("ana", 11), () => T0).Adquirida;
        Assert.NotNull(primeira);

        var segunda = Trava.Adquirir(pacote, Eu("bia", 22), () => T0.AddSeconds(10));

        Assert.Null(segunda.Adquirida);
        Assert.Equal("ana", segunda.Ocupada!.Usuario);
        Assert.Equal("PC-ana", segunda.Ocupada.Maquina);
        Assert.Equal(11, segunda.Ocupada.Pid);
    }

    [Fact]
    public void trava_abandonada_e_retomada()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-abandonada");
        var pacote = Path.Combine(pasta, "a.pmvrel");
        using var primeira = Trava.Adquirir(pacote, Eu("ana", 11), () => T0).Adquirida;

        using var segunda = Trava.Adquirir(pacote, Eu("bia", 22), () => T0 + Trava.Abandono + TimeSpan.FromSeconds(1)).Adquirida;

        Assert.NotNull(segunda);
    }

    [Fact]
    public void batimento_renova_a_trava()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-batimento");
        var pacote = Path.Combine(pasta, "a.pmvrel");
        var agora = T0;
        using var primeira = Trava.Adquirir(pacote, Eu("ana", 11), () => agora).Adquirida!;
        agora = T0.AddSeconds(90);
        primeira.Bater();

        var segunda = Trava.Adquirir(pacote, Eu("bia", 22), () => T0.AddSeconds(150));

        Assert.Null(segunda.Adquirida);
        Assert.Equal("ana", segunda.Ocupada!.Usuario);
        Assert.Equal(T0.AddSeconds(90), segunda.Ocupada.BatimentoUtc);
    }

    [Fact]
    public void dispose_apaga_so_a_propria_trava()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-dispose");
        var pacote = Path.Combine(pasta, "a.pmvrel");
        var caminho = Trava.CaminhoDe(pacote);

        var primeira = Trava.Adquirir(pacote, Eu("ana", 11), () => T0).Adquirida!;
        primeira.Dispose();
        Assert.False(File.Exists(caminho));

        // a trava foi retomada por outro: o Dispose da antiga nao pode apaga-la
        var antiga = Trava.Adquirir(pacote, Eu("ana", 11), () => T0).Adquirida!;
        var nova = Trava.Adquirir(pacote, Eu("bia", 22), () => T0 + Trava.Abandono + TimeSpan.FromSeconds(1)).Adquirida!;
        antiga.Dispose();
        Assert.True(File.Exists(caminho));
        nova.Dispose();
        Assert.False(File.Exists(caminho));
    }

    [Fact]
    public void arquivo_de_trava_oculto()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-oculta");
        var pacote = Path.Combine(pasta, "a.pmvrel");
        using var t = Trava.Adquirir(pacote, Eu("ana", 11), () => T0).Adquirida!;

        var caminho = Trava.CaminhoDe(pacote);
        Assert.Equal(".~a.pmvrel.lock", Path.GetFileName(caminho));
        Assert.True(File.GetAttributes(caminho).HasFlag(FileAttributes.Hidden));
    }

    [Fact]
    public void retomada_concorrente_deixa_um_so_dono()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-corrida");
        var pacote = Path.Combine(pasta, "a.pmvrel");
        using var antiga = Trava.Adquirir(pacote, Eu("ana", 11), () => T0).Adquirida;
        var tarde = T0 + Trava.Abandono + TimeSpan.FromSeconds(1);
        Trava? a = null;
        try
        {
            // B le a trava abandonada e, antes de apagar, A retoma e cria a sua
            Trava.AntesDeApagar = _ =>
            {
                Trava.AntesDeApagar = null;
                a = Trava.Adquirir(pacote, Eu("carla", 33), () => tarde).Adquirida;
            };
            var b = Trava.Adquirir(pacote, Eu("bia", 22), () => tarde);

            Assert.NotNull(a);
            Assert.Null(b.Adquirida);
            Assert.Equal("carla", b.Ocupada!.Usuario);
            Assert.True(a!.Bater());
        }
        finally { Trava.AntesDeApagar = null; a?.Dispose(); }
    }

    [Fact]
    public void bater_informa_trava_perdida()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-perdida");
        var pacote = Path.Combine(pasta, "a.pmvrel");
        var antiga = Trava.Adquirir(pacote, Eu("ana", 11), () => T0).Adquirida!;
        Assert.True(antiga.Bater());

        using var nova = Trava.Adquirir(pacote, Eu("bia", 22), () => T0 + Trava.Abandono + TimeSpan.FromSeconds(1)).Adquirida!;
        Assert.False(antiga.Bater());

        File.Delete(Trava.CaminhoDe(pacote));
        Assert.False(nova.Bater());
    }

    [Fact]
    public void reinicio_apos_queda_retoma_na_hora_se_o_processo_morreu()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-queda");
        var pacote = Path.Combine(pasta, "a.pmvrel");
        using var morta = Trava.Adquirir(pacote, new InfoTrava("PC", "ana", 2_000_000_000, T0, T0), () => T0).Adquirida;

        using var nova = Trava.Adquirir(pacote, new InfoTrava("PC", "ana", Environment.ProcessId, T0, T0), () => T0.AddSeconds(5)).Adquirida;
        Assert.NotNull(nova);

        // outro usuario, ou outra maquina, nao tem como saber: continua ocupada
        using var viva = Trava.Adquirir(Path.Combine(pasta, "b.pmvrel"), new InfoTrava("PC", "ana", 2_000_000_000, T0, T0), () => T0).Adquirida;
        var outro = Trava.Adquirir(Path.Combine(pasta, "b.pmvrel"), new InfoTrava("PC", "bia", Environment.ProcessId, T0, T0), () => T0.AddSeconds(5));
        Assert.Null(outro.Adquirida);
    }

    [Fact]
    public void batimento_no_futuro_conta_como_abandonada()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-futuro");
        var pacote = Path.Combine(pasta, "a.pmvrel");
        using var adiantada = Trava.Adquirir(pacote, Eu("ana", 11), () => T0.AddMinutes(10)).Adquirida;

        Assert.Null(Trava.Adquirir(pacote, Eu("bia", 22), () => T0.AddMinutes(11)).Adquirida);
        Assert.NotNull(Trava.Adquirir(pacote, Eu("bia", 22), () => T0).Adquirida);
    }

    [Fact]
    public void pasta_inexistente_ou_somente_leitura_nao_lanca()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-semescrita");
        var r = Trava.Adquirir(Path.Combine(pasta, "nao-existe", "a.pmvrel"), Eu("ana", 11), () => T0);
        Assert.Null(r.Adquirida);
        Assert.Null(r.Ocupada);
        Assert.False(string.IsNullOrEmpty(r.Erro));
    }
}
