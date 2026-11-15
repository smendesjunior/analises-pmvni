using Pmvni.Revisao;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>O texto que o Codex recebe pela entrada padrão (desenho §12.4).</summary>
public class PedidoAgenteTestes
{
    const string Pasta = @"C:\Processos\2099\0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)";
    static readonly Processo P = new(Pasta, "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)", 1, 2099, "FULANO DE EXEMPLO (FICTICIO)");
    const string Fixas =
        "Use $analise para analisar o processo PMVNI desta pasta, com as 28 seções, as referências do índice do kit e o relatório oficial em PDF.\n"
        + "Pasta do processo: " + Pasta + "\n"
        + "Processo: 0001/2099 — FULANO DE EXEMPLO (FICTICIO)";

    [Fact]
    public void pedido_do_codex_exato()
    {
        var anexos = new[]
        {
            new ArquivoArquivado(@"D:\Downloads\planta.pdf", @"PROJETO\INICIAL\planta.pdf", "aa", 10, false),
            new ArquivoArquivado(@"D:\Downloads\req.pdf", @"DOCUMENTOS\req.pdf", "bb", 20, true),
        };
        var esperado = Fixas + "\n"
            + "Arquivos que o usuário anexou agora, já copiados na pasta do processo pelo Editor de Relatórios PMVNI:\n"
            + @"- PROJETO\INICIAL\planta.pdf (copiado)" + "\n"
            + @"- DOCUMENTOS\req.pdf (já estava no processo)" + "\n"
            + "Pedido do usuário:\n"
            + "Linha 1\nLinha 2\nLinha 3";
        Assert.Equal(esperado, PedidoAgente.Codex(Pasta, P, anexos, "Linha 1\nLinha 2\nLinha 3"));
    }

    [Fact]
    public void sem_anexos_e_sem_texto_so_as_tres_primeiras_linhas()
    {
        Assert.Equal(Fixas, PedidoAgente.Codex(Pasta, P, Array.Empty<ArquivoArquivado>(), null));
        Assert.Equal(Fixas, PedidoAgente.Codex(Pasta, P, Array.Empty<ArquivoArquivado>(), ""));
        Assert.Equal(Fixas, PedidoAgente.Codex(Pasta, P, Array.Empty<ArquivoArquivado>(), "  \r\n  "));
    }

    [Fact]
    public void so_anexos_ou_so_texto()
    {
        var um = new[] { new ArquivoArquivado(@"D:\a.pdf", @"DOCUMENTOS\a.pdf", "aa", 1, false) };
        Assert.Equal(Fixas + "\nArquivos que o usuário anexou agora, já copiados na pasta do processo pelo Editor de Relatórios PMVNI:\n- DOCUMENTOS\\a.pdf (copiado)",
            PedidoAgente.Codex(Pasta, P, um, null));
        Assert.Equal(Fixas + "\nPedido do usuário:\nconfira o muro",
            PedidoAgente.Codex(Pasta, P, Array.Empty<ArquivoArquivado>(), "confira o muro"));
    }

    [Fact]
    public void o_texto_vai_como_foi_digitado()
    {
        var texto = "  com espaços nas pontas  \r\ne \"aspas\" & <html> $analise ";
        var pedido = PedidoAgente.Codex(Pasta, P, Array.Empty<ArquivoArquivado>(), texto);
        Assert.EndsWith("\nPedido do usuário:\n" + texto, pedido);
    }

    [Fact]
    public void processo_fora_do_padrao_usa_o_nome_da_pasta()
    {
        var fora = new Processo(@"C:\Processos\2099\pasta fora do padrao", "pasta fora do padrao", null, null, null);
        var pedido = PedidoAgente.Codex(fora.Pasta, fora, Array.Empty<ArquivoArquivado>(), null);
        Assert.Equal("Processo: pasta fora do padrao", pedido.Split('\n')[2]);
        var semRequerente = new Processo(@"C:\x", "n", 7, 2026, null);
        Assert.Equal("Processo: 0007/2026", PedidoAgente.Codex(@"C:\x", semRequerente, Array.Empty<ArquivoArquivado>(), null).Split('\n')[2]);
    }
}
