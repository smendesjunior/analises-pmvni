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
        Assert.Equal(new[] { "Arquivo", "Página Inicial", "Revisão", "Exibir", "Ajuda" }, Faixas.Para(Modulo.Relatorios).Abas.Select(a => a.Nome));
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
            // O mesmo comando pode aparecer em duas abas (Exportar PDF em Arquivo e em Página Inicial); dentro de uma aba, não.
            foreach (var aba in Faixas.Para(m).Abas)
            {
                var ids = aba.Comandos.SelectMany(c => c.Itens is null ? [c] : c.Itens.Prepend(c)).Select(c => c.Id).ToList();
                Assert.All(ids, id => Assert.Contains(id, conhecidos));
                Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            }
            Assert.All(Faixas.Para(m).Abas.SelectMany(a => a.Comandos), c => Assert.False(string.IsNullOrWhiteSpace(c.Rotulo)));
        }
    }

    [Fact]
    public void pagina_inicial_e_revisao_tem_os_comandos_da_revisao()
    {
        var f = Faixas.Para(Modulo.Relatorios);
        var inicio = f.Abas.Single(a => a.Nome == "Página Inicial");
        Assert.Equal(new[]
        {
            ComandosApp.InicioSituacao, ComandosApp.InicioExcluirRestaurar, ComandosApp.InicioEditarTexto, ComandosApp.InicioOriginal,
            ComandosApp.InicioRestaurarOriginal, ComandosApp.InicioItemAnterior, ComandosApp.InicioItemProximo, ComandosApp.InicioProximoPendente,
            ComandosApp.ArquivoExportarPdf,
        }, inicio.Comandos.Select(c => c.Id));
        var situacao = inicio.Comandos[0];
        Assert.Equal(TipoComando.Menu, situacao.Tipo);
        // As 4 situações de item do kit, pelos textos que o relatório imprime.
        Assert.Equal(Pmvni.Composicao.Kit.Situacoes.SitItem.Order(StringComparer.Ordinal), situacao.Itens!.Select(i => ComandosApp.SituacaoDoComando(i.Id)!).Order(StringComparer.Ordinal));
        Assert.All(situacao.Itens!, i => Assert.Equal(Pmvni.Composicao.Kit.Situacoes.Sit[ComandosApp.SituacaoDoComando(i.Id)!].Texto, i.Rotulo));
        Assert.Equal(TipoComando.Alternar, inicio.Comandos.Single(c => c.Id == ComandosApp.InicioOriginal).Tipo);

        var revisao = f.Abas.Single(a => a.Nome == "Revisão");
        Assert.Equal(new[] { ComandosApp.RevisaoMarcas, ComandosApp.RevisaoSomenteAlterados, ComandosApp.RevisaoConferencia }, revisao.Comandos.Select(c => c.Id));
        Assert.Equal(new[] { TipoComando.Alternar, TipoComando.Alternar, TipoComando.Botao }, revisao.Comandos.Select(c => c.Tipo));

        var exibir = f.Abas.Single(a => a.Nome == "Exibir");
        Assert.Equal(TipoComando.Alternar, exibir.Comandos.Single(c => c.Id == ComandosApp.ExibirPropriedades).Tipo);
    }

    [Fact]
    public void modulo_sem_faixa_definida_e_recusado()
    {
        foreach (var m in Enum.GetValues<Modulo>().Except(ComFaixa()))
            Assert.Throws<ArgumentOutOfRangeException>(() => Faixas.Para(m));
    }
}
