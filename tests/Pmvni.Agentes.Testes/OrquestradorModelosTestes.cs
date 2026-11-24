using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>Tarefa 19: a tabela efetiva de modelos (padrão do kit + ajuste local) vai ao controlador do kit em <c>PMVNI_MODELOS_POR_ROTA</c>, num arquivo do
/// registro da execução, e fica no histórico (<c>execucao.json</c>, com os avisos de modelo trocado pelo padrão do kit). Sem tabela, nada é passado.</summary>
public partial class OrquestradorTestes
{
    const string TabelaAjustada = """
        {"versao": 1, "origem": "padrão do kit com o ajuste local do Editor de Relatórios",
         "rotas": {"codex": {"perfis": {"padrao": {"etapas": {"analyze": {"modelo": "gpt-6.2-sol", "nivel": "medio"}}}}},
                   "claude": {"perfis": {"padrao": {"etapas": {"analyze": {"modelo": "claude-opus-5-5", "nivel": "alto"}}}}}},
         "avisos": ["Claude, Análise: o modelo claude-fable-5-2 não aparece mais na lista do app Claude; esta etapa usa o padrão do kit (claude-opus-5-5, nível alto)."]}
        """;

    static TabelaDeModelos Tabela(params string[] avisos) => new(TabelaAjustada, avisos);

    [Fact]
    public async Task RetomarPreservaOTermoEscolhidoNaExecucaoMesmoComAjusteLocalDiferente()
    {
        using var c = new Cena("orq-modelos-retomada", "ok", Provedor.ChatGpt);
        var arquivo = c.DoRegistro(TabelaDeModelos.NomeDoArquivo);
        var alterada = TabelaAjustada.Replace("gpt-6.2-sol", "modelo-escolhido");
        File.WriteAllText(arquivo, alterada);
        var pedido = c.Pedido(Provedor.ChatGpt) with { Retomar = true, Modelos = Tabela() };
        await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);
        Assert.Equal(arquivo, Json(c.Falso("recebido.json"))["modelos"]!.GetValue<string>());
        Assert.Equal(alterada, File.ReadAllText(arquivo));
    }

    [Fact]
    public void RespostaSoUsaAExcecaoNoArquivoDaDecisaoDoRegistroAtivo()
    {
        using var c = new Cena("orq-modelos-resposta", "ok", Provedor.ChatGpt);
        var pedido = c.Pedido(Provedor.ChatGpt);
        var id = Guid.NewGuid().ToString("N");
        var esperado = c.DoRegistro(Path.Combine("decisoes", $"modelos_indisponiveis-{id}.json"));
        var d = new DecisaoAnalise("modelos_indisponiveis", id, "Modelo?", [], esperado, "{}");
        Assert.Equal(esperado, RespostaDecisao.Caminho(pedido.Processo.Pasta, pedido.Provedor, d, pedido.Registro.Pasta));
        Assert.NotEqual(esperado, RespostaDecisao.Caminho(pedido.Processo.Pasta, pedido.Provedor, d with { Id = Guid.NewGuid().ToString("N") }, pedido.Registro.Pasta));
    }

    [Theory]
    [InlineData("{\"recusa_modelo\":{\"etapa\":\"analyze\",\"modelo\":\"gemini-pro\"}}", true)]
    [InlineData("{\"recusa_modelo\":null,\"faltas\":[\"sem acesso à rede\"]}", false)]
    [InlineData("{\"recusa_modelo\":{}}", false)]
    public void PreVooSomenteEncaminhaRecusaDeModeloEstruturada(string json, bool esperada) =>
        Assert.Equal(esperada, OrquestradorAnalise.RecusaDeModeloGemini(json));

    [Fact]
    public async Task ChatGpt_RecebeATabelaDeModelos_E_OHistoricoARegistra()
    {
        using var c = new Cena("orq-modelos-62", "ok", Provedor.ChatGpt);
        // Uma variável herdada do ambiente do Editor nunca chega ao controlador: vale só a tabela desta execução.
        using var herdada = new VariavelDeAmbiente(TabelaDeModelos.Variavel, @"C:\outra\tabela.json");
        var pedido = c.Pedido(Provedor.ChatGpt) with { Modelos = Tabela() };
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);
        Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);

        var arquivo = c.DoRegistro(TabelaDeModelos.NomeDoArquivo);
        Assert.Equal(arquivo, Json(c.Falso("recebido.json"))["modelos"]!.GetValue<string>());
        Assert.Equal(TabelaAjustada, File.ReadAllText(arquivo, Encoding.UTF8));
        var registro = c.Execucao()[TabelaDeModelos.ChaveNoRegistro]!.AsObject();
        Assert.Equal(arquivo, registro["arquivo"]!.GetValue<string>());
        Assert.Equal("gpt-6.2-sol", registro["tabela"]!["rotas"]!["codex"]!["perfis"]!["padrao"]!["etapas"]!["analyze"]!["modelo"]!.GetValue<string>());
        Assert.Empty(registro["avisos"]!.AsArray());
    }

    [Fact]
    public async Task Claude_RecebeATabelaNaPartida_E_OAvisoDeModeloTrocadoFicaNoHistorico()
    {
        using var c = new Cena("orq-modelos-claude", "claude_ok", Provedor.Claude);
        const string aviso = "Claude, Análise: o modelo claude-fable-5-2 não aparece mais na lista do app Claude; esta etapa usa o padrão do kit (claude-opus-5-5, nível alto).";
        var pedido = c.Pedido(Provedor.Claude) with { Modelos = Tabela(aviso) };
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(pedido, c.Progresso, CancellationToken.None), Limite);
        Assert.Equal("concluida", fim.Situacao);

        var arquivo = c.DoRegistro(TabelaDeModelos.NomeDoArquivo);
        var recebidas = File.ReadAllLines(c.Falso("modelos.txt"), Encoding.UTF8).Select(l => l.Split('\t')).ToList();
        Assert.Contains(recebidas, r => r[0] == "iniciar" && r[1] == arquivo);
        var registro = c.Execucao()[TabelaDeModelos.ChaveNoRegistro]!.AsObject();
        Assert.Equal("claude-opus-5-5", registro["tabela"]!["rotas"]!["claude"]!["perfis"]!["padrao"]!["etapas"]!["analyze"]!["modelo"]!.GetValue<string>());
        Assert.Equal(aviso, Assert.Single(registro["avisos"]!.AsArray())!.GetValue<string>());
    }

    [Fact]
    public async Task SemTabela_NadaEPassado_EValeOPadraoDoKit()
    {
        using var c = new Cena("orq-modelos-sem", "ok", Provedor.ChatGpt);
        using var herdada = new VariavelDeAmbiente(TabelaDeModelos.Variavel, @"C:\outra\tabela.json");
        var fim = await Ambiente.Com(c.Orq().ExecutarAsync(c.Pedido(Provedor.ChatGpt), c.Progresso, CancellationToken.None), Limite);
        Assert.True(fim.Situacao == "concluida", fim.Situacao + ": " + fim.Mensagem);
        Assert.Null(Json(c.Falso("recebido.json"))["modelos"]);
        Assert.False(File.Exists(c.DoRegistro(TabelaDeModelos.NomeDoArquivo)));
        Assert.Null(c.Execucao()[TabelaDeModelos.ChaveNoRegistro]);
    }
}
