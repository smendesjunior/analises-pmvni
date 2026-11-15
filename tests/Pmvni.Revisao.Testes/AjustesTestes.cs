using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Ajustes locais do app (<c>&lt;dados&gt;\ajustes.json</c>, Tarefa 8): lembram módulo e aba; arquivo corrompido volta ao padrão.</summary>
public class AjustesTestes
{
    [Fact]
    public void ajustes_lembram_modulo_e_aba()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes");
        var a = Ajustes.Carregar(pasta);
        Assert.Equal(Modulo.Relatorios, a.Modulo);
        Assert.Equal("sistema", a.Tema);
        Assert.Equal(30, a.IntervaloAutosaveS);
        Assert.True(a.MarcasRevisao);
        Assert.Empty(a.Recentes);

        a.Modulo = Modulo.Ajustes;
        a.AbaPorModulo[Modulo.Relatorios] = "Ajuda";
        a.AbaPorModulo[Modulo.Ajustes] = "Arquivo";
        a.Tema = "escuro";
        a.IntervaloAutosaveS = 60;
        a.MarcasRevisao = false;
        a.Recentes.Add(@"C:\x\a.pmvrel");
        a.PastaProcessos = @"C:\processos";
        a.Salvar(pasta);

        Assert.True(File.Exists(Path.Combine(pasta, "ajustes.json")));
        Assert.Empty(Directory.GetFiles(pasta, "*.tmp"));
        var b = Ajustes.Carregar(pasta);
        Assert.Equal(Modulo.Ajustes, b.Modulo);
        Assert.Equal("Ajuda", b.AbaPorModulo[Modulo.Relatorios]);
        Assert.Equal("Arquivo", b.AbaPorModulo[Modulo.Ajustes]);
        Assert.Equal("escuro", b.Tema);
        Assert.Equal(60, b.IntervaloAutosaveS);
        Assert.False(b.MarcasRevisao);
        Assert.Equal(new[] { @"C:\x\a.pmvrel" }, b.Recentes);
        Assert.Equal(@"C:\processos", b.PastaProcessos);
    }

    [Fact]
    public void ajustes_corrompidos_voltam_ao_padrao_e_guardam_o_arquivo()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-corrompidos");
        var arquivo = Path.Combine(pasta, "ajustes.json");
        File.WriteAllText(arquivo, "{ \"modulo\": \"Relatorios\", ");

        var a = Ajustes.Carregar(pasta);

        Assert.Equal(Modulo.Relatorios, a.Modulo);
        Assert.Equal(30, a.IntervaloAutosaveS);
        Assert.False(File.Exists(arquivo));
        var guardado = Path.Combine(pasta, "ajustes.corrompido.json");
        Assert.Equal("{ \"modulo\": \"Relatorios\", ", File.ReadAllText(guardado));
    }

    [Fact]
    public void valores_fora_do_permitido_voltam_ao_padrao()
    {
        using var pasta = Ambiente.PastaDeTeste("ajustes-invalidos");
        File.WriteAllText(Path.Combine(pasta, "ajustes.json"),
            "{\"modulo\":\"Inexistente\",\"tema\":\"roxo\",\"intervalo_autosave_s\":7,\"aba_por_modulo\":{\"Nada\":\"x\",\"Relatorios\":\"Exibir\"}}");

        var a = Ajustes.Carregar(pasta);

        Assert.Equal(Modulo.Relatorios, a.Modulo);
        Assert.Equal("sistema", a.Tema);
        Assert.Equal(30, a.IntervaloAutosaveS);
        Assert.Equal("Exibir", Assert.Single(a.AbaPorModulo).Value);
    }
}
