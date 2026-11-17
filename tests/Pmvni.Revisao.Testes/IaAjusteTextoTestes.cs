using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>"Ajustar com IA" na caixa do texto da exigência (prévia do despacho): o pedido leva o texto e o contexto como dados e pede
/// só o texto melhorado; a resposta é limpa (cerca de código, rótulo, aspas, quebras) antes de entrar na caixa.</summary>
public class IaAjusteTextoTestes
{
    [Fact]
    public void o_pedido_leva_texto_e_contexto_como_dados_e_pede_so_o_texto()
    {
        var p = IaAjusteTexto.Prompt("apresentar a planta \"corrigida\" do item 12", "Estacionamento", "Vagas", "Art. 45 da Lei 1.234");
        Assert.Contains("\"apresentar a planta \\\"corrigida\\\" do item 12\"", p);
        Assert.Contains("Grupo (contexto): \"Estacionamento\"", p);
        Assert.Contains("Item do relatório (contexto): \"Vagas\"", p);
        Assert.Contains("Fundamento (contexto): \"Art. 45 da Lei 1.234\"", p);
        Assert.Contains("Não invente", p);
        Assert.Contains("NÃO abra arquivos", p);
        Assert.EndsWith("Responda SOMENTE com o texto melhorado, sem aspas, sem comentários e sem explicação.", p);
    }

    [Fact]
    public void sem_contexto_o_pedido_nao_cita_grupo_nem_fundamento()
    {
        var p = IaAjusteTexto.Prompt("texto", null, " ", "");
        Assert.DoesNotContain("Grupo (contexto)", p);
        Assert.DoesNotContain("Item do relatório (contexto)", p);
        Assert.DoesNotContain("Fundamento (contexto)", p);
    }

    [Theory]
    [InlineData("Apresentar a planta corrigida.", "Apresentar a planta corrigida.")]
    [InlineData("  \"Apresentar a planta corrigida.\"  ", "Apresentar a planta corrigida.")]
    [InlineData("“Apresentar a planta corrigida.”", "Apresentar a planta corrigida.")]
    [InlineData("```\nApresentar a planta\ncorrigida.\n```", "Apresentar a planta corrigida.")]
    [InlineData("```text\nTexto melhorado: Apresentar a planta corrigida.\n```", "Apresentar a planta corrigida.")]
    [InlineData("Exigência: Apresentar a planta corrigida.", "Apresentar a planta corrigida.")]
    public void a_resposta_e_limpa_antes_de_entrar_na_caixa(string saida, string esperado) =>
        Assert.Equal(esperado, IaAjusteTexto.Ler(saida));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n ")]
    [InlineData("\"\"")]
    [InlineData("```\n```")]
    public void resposta_vazia_nao_muda_nada(string? saida) => Assert.Null(IaAjusteTexto.Ler(saida));
}
