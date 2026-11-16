using Pmvni.Composicao.Kit;
using Xunit;

namespace Pmvni.Composicao.Testes;

/// <summary>Correção 2026-11-16: a volumetria do diagrama aceita a forma (pavimentos, telhado, destaque); sem ela, o desenho é o do kit.</summary>
public class VolumetriaTestes
{
    static Dictionary<string, object?> Base() => new()
    {
        ["altura_esquerda"] = new List<object?> { "Corte AA", "7,97 m" },
        ["altura_direita"] = new List<object?> { "Corte BB", "7,86 m" },
    };

    static readonly (string, int)[] Marcas = [("telhado", 5), ("altura", 6)];

    static int Conta(string svg, string trecho) => (svg.Length - svg.Replace(trecho, "", StringComparison.Ordinal).Length) / trecho.Length;

    [Fact]
    public void sem_forma_o_desenho_e_o_de_sempre()
    {
        var svg = Diagrama.VolumetriaSvg(Base(), Marcas);
        Assert.StartsWith("<svg viewBox=\"-22 0 344 318\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"rfb\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("#D64545", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void forma_padrao_explicita_desenha_dois_pavimentos_com_telhado()
    {
        var v = Base();
        v["pavimentos"] = "2"; v["telhado"] = "duas águas"; v["destaque"] = "nenhum";
        var svg = Diagrama.VolumetriaSvg(v, Marcas);
        Assert.Contains("class=\"rfb\"", svg, StringComparison.Ordinal);
        Assert.Equal(2, Conta(svg, "class=\"fl\"")); // um piso intermediário: uma linha em cada face
        Assert.DoesNotContain("#D64545", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void mais_pavimentos_sem_telhado_e_destaque_na_lateral_direita()
    {
        var v = Base();
        v["pavimentos"] = "3"; v["telhado"] = "sem"; v["destaque"] = "Afastamento lateral direito abaixo do mínimo";
        var svg = Diagrama.VolumetriaSvg(v, Marcas);
        Assert.Equal(4, Conta(svg, "class=\"fl\"")); // dois pisos intermediários
        Assert.DoesNotContain("class=\"rfb\"", svg, StringComparison.Ordinal); // sem a água de trás do telhado
        Assert.Contains("class=\"wr\" style=\"stroke:#D64545", svg, StringComparison.Ordinal); // a face direita em vermelho
        Assert.DoesNotContain("class=\"wl\" style", svg, StringComparison.Ordinal);
        Assert.Contains("Afastamento lateral direito abaixo do mínimo", svg, StringComparison.Ordinal);
        // O prédio mais alto cabe no desenho: nenhum ponto acima do topo do viewBox.
        var topo = double.Parse(svg.Split('"')[1].Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(topo <= 0);
    }

    [Fact]
    public void pavimentos_fora_da_faixa_ficam_entre_1_e_6()
    {
        var v = Base();
        v["pavimentos"] = "40";
        Assert.Equal(10, Conta(Diagrama.VolumetriaSvg(v, Marcas), "class=\"fl\""));
        v["pavimentos"] = "abc";
        Assert.Equal(2, Conta(Diagrama.VolumetriaSvg(v, Marcas), "class=\"fl\""));
    }
}
