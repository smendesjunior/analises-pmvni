using System.Text.Json;
using Xunit;

namespace Pmvni.Agentes.Testes;

[Collection("Ambiente")]
public sealed class HistoricoAnalisesTestes
{
    [Fact]
    public void painel_mostra_conflitos_e_precisao_do_recibo_com_as_fontes()
    {
        using var t = Ambiente.PastaDeTeste("auditoria-local");
        var processo = Ambiente.NovoProcesso(t);
        using var registro = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, DateTime.Now);
        var id = Path.GetFileName(registro.Pasta);
        var fonte = new[] { "DOC-02, p. 5" };
        var recibo = new
        {
            versao = 1, execucao = id, rota = "codex", fase = "montagem",
            linhas = new[] { "to", "ca", "tp" }.Select(indice => new { indice, situacao = "INSUFICIENTE" }),
            auditoria_local = new {
                DuckDB = new { estado = "executado", resultados = new[] { new { campo = "tp", valor = "15", fontes = fonte } } },
                Z3 = new { estado = "executado", resultados = new[] { new { indice = "ca", resultado = "compatível com a precisão impressa", fontes = fonte } } }
            }
        };
        File.WriteAllText(Path.Combine(registro.Pasta, ConferenciaNumerica.ArquivoHistorico), JsonSerializer.Serialize(recibo));
        var painel = Assert.IsType<ConferenciaNumerica>(ConferenciaNumerica.Ler(processo.Pasta, Provedor.ChatGpt, registro.Pasta));
        Assert.Contains("tp · 15 · DOC-02, p. 5", painel.Detalhes);
        Assert.Contains("ca · compatível com a precisão impressa · DOC-02, p. 5", painel.Detalhes);
    }

    [Theory]
    [InlineData(Provedor.ChatGpt, "codex")]
    [InlineData(Provedor.Claude, "claude")]
    [InlineData(Provedor.Gemini, "gemini")]
    public void painel_numerico_segue_a_execucao_e_a_rota_mesmo_ao_reabrir_o_historico(Provedor p, string rota)
    {
        using var t = Ambiente.PastaDeTeste("historico-numerico");
        var processo = Ambiente.NovoProcesso(t);
        using var registro = RegistroExecucao.Criar(processo.Pasta, p, DateTime.Now);
        registro.Atualizar(j => { j["provedor"] = Provedores.Sufixo(p); j["situacao"] = "concluida"; });
        var nome = Path.GetFileName(registro.Pasta);
        var local = Directory.CreateDirectory(Path.Combine(processo.Pasta, ResultadoAnalise.PastaControle(p), "local"));
        var arquivo = Path.Combine(local.FullName, ConferenciaNumerica.ArquivoControle);
        string Recibo(string execucao, string rota) => JsonSerializer.Serialize(new
        {
            versao = 1, execucao, rota, fase = "preparo",
            linhas = new object[] { new { indice = "tp", situacao = "CONFERE", declarado = "11,05%", calculado = "11,05%", fontes = new[] {"QUADRO DE ÁREAS, p. 2"} },
                new { indice = "to", situacao = "INSUFICIENTE", declarado = "68,36%", calculado = "", motivo = "Falta área de projeção." },
                new { indice = "ca", situacao = "DIVERGENTE", declarado = "-1,25", calculado = "1,5",
                    fontes = new[] { "PRANCHA A-02, p. 3" }, motivo = "Confronto com o quadro de áreas." } },
        });
        ConferenciaNumerica? LerPainel() => ConferenciaNumerica.Ler(processo.Pasta, p, registro.Pasta);
        Assert.Null(LerPainel());
        File.WriteAllText(arquivo, Recibo("execucao-anterior", rota));
        Assert.Null(LerPainel());
        File.WriteAllText(arquivo, Recibo(nome, "outra-rota"));
        Assert.Null(LerPainel());
        File.WriteAllText(arquivo, Recibo(nome, rota));
        var painel = Assert.IsType<ConferenciaNumerica>(LerPainel());
        Assert.Equal("Conferidos: 1 · Divergentes: 1 · Com dados insuficientes: 1", painel.Resumo);
        Assert.Contains("CA · DIVERGENTE · declarado -1,25 · calculado 1,5", painel.Detalhes);
        Assert.Contains("PRANCHA A-02, p. 3", painel.Detalhes);
        Assert.Contains("TP · CONFERE · declarado 11,05% · calculado 11,05%", painel.Detalhes);
        Assert.Contains("QUADRO DE ÁREAS, p. 2", painel.Detalhes);
        Assert.Contains("TO · INSUFICIENTE · declarado 68,36% · calculado não disponível", painel.Detalhes);
        Assert.Contains("Falta área de projeção.", painel.Detalhes);
        File.Copy(arquivo, Path.Combine(registro.Pasta, ConferenciaNumerica.ArquivoHistorico));
        File.WriteAllText(arquivo, Recibo("novo-registro", rota));
        var historico = Assert.Single(AnaliseHistorica.Listar(Path.Combine(t, "processos")));
        Assert.Equal(painel, ConferenciaNumerica.Ler(historico.Processo.Pasta, historico.Provedor, historico.Registro));
        foreach (var indices in new[] { Array.Empty<string>(), ["to", "ca"], ["to", "ca", "ca"], ["to", "ca", "xyz"] })
        {
            var invalido = new { versao = 1, execucao = nome, rota, fase = "montagem",
                linhas = indices.Select(indice => new { indice, situacao = "CONFERE" }) };
            File.WriteAllText(arquivo, JsonSerializer.Serialize(invalido));
            Assert.Equal(painel, LerPainel());
        }
    }

    [Fact]
    public void HistoricoRecuperaExecucoesComESemRelatorioNoMesmoProcesso()
    {
        using var pasta = Ambiente.PastaDeTeste("historico-registros");
        var processo = Ambiente.NovoProcesso(pasta);
        var pacote = Path.Combine(processo.Pasta, "Relatorio.rpmvni");
        File.WriteAllText(pacote, "fictício");
        var estados = new[] { "concluida", "falhou", "sem_resultado", "cancelada", "interrompida", "em_andamento" };
        foreach (var estado in estados)
        {
            var registro = Directory.CreateDirectory(Path.Combine(processo.Pasta, ".pmvni-ai", "editor", estado));
            File.WriteAllText(Path.Combine(registro.FullName, "execucao.json"), JsonSerializer.Serialize(new
            {
                situacao = estado, provedor = "chatgpt", sessao_codex = "sessao-ficticia",
                pmvrel = estado == "concluida" ? pacote : null
            }));
        }
        var itens = AnaliseHistorica.Listar(Path.Combine(pasta, "processos"));
        Assert.Equal(estados.Order(), itens.Select(h => h.Situacao).Order());
        Assert.Equal(pacote, itens.Single(h => h.Situacao == "concluida").Pacote);
        Assert.All(itens, h => Assert.Equal("sessao-ficticia", h.Sessao));
    }
}
