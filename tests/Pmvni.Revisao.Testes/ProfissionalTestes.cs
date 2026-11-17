using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Profissional responsável dos Ajustes (2026-11-17): o bloco de assinatura dos modelos de despacho e o cargo do relatório.</summary>
public sealed class ProfissionalTestes
{
    [Theory]
    [InlineData(TipoDespacho.Aprovacao)]
    [InlineData(TipoDespacho.Correcao)]
    public void troca_o_bloco_de_assinatura_do_modelo(TipoDespacho tipo)
    {
        var kit = Ambiente.ExigirKit();
        var modelo = File.ReadAllBytes(RegistroModelos.Ler(kit).Modelos.Single(m => m.Tipo == tipo).Caminho);
        var p = new Profissional("Fulana de Tal", "engenheira civil", "CREA-ES 123456/D", "1234");
        var novo = PreenchimentoDocx.ComProfissional(modelo, p);
        var textos = PreenchimentoDocx.LerModelo(novo, tipo).Paragrafos.Select(x => x.Texto.Trim()).Where(t => t.Length > 0).ToList();
        var linha = textos.FindIndex(t => t.All(c => c == '_'));
        Assert.Equal(["FULANA DE TAL", "ENGENHEIRA CIVIL", "CREA-ES 123456/D", "MATRÍCULA 1234"], textos.Skip(linha + 1).Take(4));
        Assert.Empty(ValidacaoDespacho.Comparar(modelo, novo));   // só o document.xml muda
        Assert.Same(modelo, PreenchimentoDocx.ComProfissional(modelo, null));
        Assert.Same(modelo, PreenchimentoDocx.ComProfissional(modelo, new Profissional(null, " ", null, null)));
    }

    [Fact]
    public void cargo_do_relatorio_usa_o_que_houver()
    {
        Assert.Equal("ENGENHEIRO — CREA 1", new Profissional(null, "engenheiro", "crea 1", null).Cargo("ARQUITETO E URBANISTA — CAU/ES A1"));
        Assert.Equal("ARQUITETO E URBANISTA — CAU X", new Profissional("Nome", null, "cau x", null).Cargo("ARQUITETO E URBANISTA — CAU/ES A1"));
        Assert.Equal("MATRÍCULA 9", new Profissional(null, null, null, "9").MatriculaLinha);
        Assert.Equal("MATRÍCULA 9", new Profissional(null, null, null, "matrícula 9").MatriculaLinha);
    }
}
