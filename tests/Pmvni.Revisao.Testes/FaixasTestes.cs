using System.Reflection;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Faixa de opções por dados (Tarefa 8): uma definição por módulo, Arquivo primeiro e Ajuda por último, aba padrão a segunda.</summary>
public class FaixasTestes
{
    /// <summary>Os módulos com faixa: os disponíveis no trilho e Ajustes (definida já, só Arquivo e Ajuda).</summary>
    private static IEnumerable<Modulo> ComFaixa() => Faixas.Disponiveis.Append(Modulo.Ajustes).Distinct();

    [Fact]
    public void toda_faixa_comeca_em_arquivo_e_termina_em_ajuda()
    {
        Assert.NotEmpty(Faixas.Disponiveis);
        Assert.Contains(Modulo.Relatorios, Faixas.Disponiveis);
        foreach (var m in ComFaixa())
        {
            var f = Faixas.Para(m);
            Assert.Equal(m, f.Modulo);
            Assert.Equal("Arquivo", f.Abas[0].Nome);
            Assert.Equal("Ajuda", f.Abas[^1].Nome);
            Assert.All(f.Abas, a => Assert.NotEmpty(a.Comandos));
        }
        Assert.Equal(new[] { "Arquivo", "Ajuda" }, Faixas.Para(Modulo.Ajustes).Abas.Select(a => a.Nome));
        Assert.Equal(new[] { "Arquivo", "Exibir", "Ajuda" }, Faixas.Para(Modulo.Relatorios).Abas.Select(a => a.Nome));
    }

    [Fact]
    public void aba_padrao_e_a_segunda_exceto_ajustes()
    {
        foreach (var m in ComFaixa())
        {
            var f = Faixas.Para(m);
            Assert.Equal(m == Modulo.Ajustes ? 0 : 1, f.AbaPadrao);
            Assert.InRange(f.AbaPadrao, 0, f.Abas.Count - 1);
        }
    }

    [Fact]
    public void ids_de_comando_unicos_e_conhecidos()
    {
        var conhecidos = typeof(ComandosApp).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(c => c.IsLiteral && c.FieldType == typeof(string))
            .Select(c => (string)c.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("arquivo.importar", conhecidos);
        Assert.Contains("ajuda.sobre", conhecidos);
        foreach (var m in ComFaixa())
        {
            var ids = Faixas.Para(m).Abas.SelectMany(a => a.Comandos).SelectMany(c => c.Itens is null ? [c] : c.Itens.Prepend(c))
                .Where(c => c.Tipo != TipoComando.Menu).Select(c => c.Id).ToList();
            Assert.All(ids, id => Assert.Contains(id, conhecidos));
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.All(Faixas.Para(m).Abas.SelectMany(a => a.Comandos), c => Assert.False(string.IsNullOrWhiteSpace(c.Rotulo)));
        }
    }

    [Fact]
    public void modulo_sem_faixa_definida_e_recusado()
    {
        foreach (var m in Enum.GetValues<Modulo>().Except(ComFaixa()))
            Assert.Throws<ArgumentOutOfRangeException>(() => Faixas.Para(m));
    }
}
