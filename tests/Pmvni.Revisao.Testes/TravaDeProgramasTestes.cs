using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>A trava do roteiro vale para todo programa que o editor inicia: o agente (pasta do agente falso) e o LibreOffice do despacho (o soffice
/// exato). O que ela recusa nunca parte nem entra na lista; o soffice permitido entra e conta como "fora do agente falso".</summary>
/// <summary>A trava é global: estes testes rodam sozinhos (nenhum outro teste inicia um soffice fictício enquanto a lista está ligada).</summary>
[CollectionDefinition("TravaDeProgramas", DisableParallelization = true)]
public sealed class ColecaoTravaDeProgramas;

[Collection("TravaDeProgramas")]
public class TravaDeProgramasTestes
{
    [Fact]
    public void lista_aceita_a_pasta_e_o_programa_exato_e_recusa_o_resto()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-programas");
        var falso = Path.Combine(pasta, "agente-falso");
        var soffice = Path.Combine(pasta, "LibreOffice", "program", "soffice.com");
        TravaDeProgramas.Permitidos = [falso, soffice];
        try
        {
            TravaDeProgramas.Conferir(Path.Combine(falso, "AgenteFalso.exe"));
            TravaDeProgramas.Conferir(soffice);
            Assert.Throws<InvalidOperationException>(() => TravaDeProgramas.Conferir(Path.Combine(pasta, "LibreOffice", "program", "soffice.exe")));
            Assert.Throws<InvalidOperationException>(() => TravaDeProgramas.Conferir(falso + "-irmao\\x.exe"));
            Assert.Throws<InvalidOperationException>(() => TravaDeProgramas.Conferir(Path.Combine(Environment.SystemDirectory, "whoami.exe")));
        }
        finally { TravaDeProgramas.Permitidos = null; }
    }

    [Fact]
    public void libreoffice_passa_pela_trava_e_o_recusado_nao_parte()
    {
        using var pasta = Ambiente.PastaDeTeste("trava-libreoffice");
        var soffice = Path.Combine(pasta, "nao-permitido", "soffice.com");
        Directory.CreateDirectory(Path.GetDirectoryName(soffice)!);
        File.WriteAllText(soffice, "");                // existe (o conversor fica "disponível"), mas nunca é iniciado
        var docx = Path.Combine(pasta, "despacho.docx");
        File.WriteAllBytes(docx, [0x50, 0x4B, 0x05, 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);
        var vistos = new List<string>();
        TravaDeProgramas.Permitidos = [Path.Combine(pasta, "agente-falso")];
        TravaDeProgramas.AoIniciar = c => vistos.Add(c);
        try
        {
            var conversor = new ConversorLibreOffice(soffice, TimeSpan.FromSeconds(5), Path.Combine(pasta, "lo"));
            var e = Assert.Throws<InvalidOperationException>(() => conversor.Converter(docx, Path.Combine(pasta, "trabalho"), CancellationToken.None));
            Assert.StartsWith("Programa fora da pasta permitida: ", e.Message);
        }
        finally { TravaDeProgramas.Permitidos = null; TravaDeProgramas.AoIniciar = null; }
        Assert.Empty(vistos);
    }

    [Fact]
    public void o_soffice_iniciado_conta_fora_do_agente_falso()
    {
        var falso = @"C:\repo\tests\saida\agente-falso";
        var iniciados = new[] { falso + @"\AgenteFalso.exe", falso + @"\AgenteFalso.exe", @"C:\Program Files\LibreOffice\program\soffice.com" };
        Assert.Equal(1, TravaDeProgramas.ForaDe(iniciados, falso));
        Assert.Equal(0, TravaDeProgramas.ForaDe(iniciados.Take(2), falso));
    }
}
