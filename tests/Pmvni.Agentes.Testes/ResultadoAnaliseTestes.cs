using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

/// <summary>O resultado que o próprio kit declara (<c>delivery.json</c> → <c>document</c>; desenho §12.6) e as mensagens exatas das recusas.</summary>
public class ResultadoAnaliseTestes
{
    static readonly DateTime Inicio = new(2026, 11, 15, 12, 0, 0, DateTimeKind.Utc);

    static string Escrever(string caminho, string conteudo, DateTime? escritaUtc = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(caminho)!);
        File.WriteAllText(caminho, conteudo, new UTF8Encoding(false));
        File.SetLastWriteTimeUtc(caminho, escritaUtc ?? Inicio.AddSeconds(30));
        return caminho;
    }

    static string Delivery(string pasta, Provedor p) => Path.Combine(pasta, ResultadoAnalise.PastaControle(p), "delivery.json");

    static string Entrega(string pasta, Provedor p, object? document, string? pdf = null)
    {
        var obj = new JsonObject { ["registry"] = "r" };
        if (document is not null) obj["document"] = document.ToString();
        if (pdf is not null) obj["pdf"] = pdf;
        return Escrever(Delivery(pasta, p), obj.ToJsonString());
    }

    [Fact]
    public void retomada_so_aceita_entrega_anterior_ao_clique_se_o_controle_pertencer_ao_registro()
    {
        using var t = Ambiente.PastaDeTeste("entrega-retomada");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        using var registro = RegistroExecucao.Criar(pasta, Provedor.ChatGpt, Inicio);
        registro.Atualizar(j => j["inicio"] = Inicio.ToString("o"));
        var id = Path.GetFileName(registro.Pasta);
        var doc = Escrever(Path.Combine(pasta, "dados.json"), "{}");
        var entrega = Entrega(pasta, Provedor.ChatGpt, doc);
        var controle = Path.Combine(Path.GetDirectoryName(entrega)!, "engine_state.json");
        var tentativa = Inicio.AddHours(1);
        Resultado Localizar() => ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, tentativa, registro.Pasta);
        Assert.Null(Localizar().Documento);
        Escrever(controle, new JsonObject { ["editor_execution"] = id }.ToJsonString());
        Assert.Equal(doc, Localizar().Documento);
        Assert.Equal(doc, ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, DateTime.MinValue, registro.Pasta).Documento);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(doc)));
        registro.RecusarEntrega(Provedor.ChatGpt, doc, hash, ["Falta a zona."]);
        Assert.Equal("Falta a zona.", RegistroExecucao.MotivoDaRecusaAtual(pasta, Provedor.ChatGpt, registro.Pasta));
        File.AppendAllText(doc, " ");
        Assert.Null(RegistroExecucao.MotivoDaRecusaAtual(pasta, Provedor.ChatGpt, registro.Pasta));
        Escrever(controle, new JsonObject { ["editor_execution"] = "outra" }.ToJsonString());
        File.SetLastWriteTimeUtc(entrega, tentativa.AddMinutes(1));
        Assert.Contains("outra execução", Localizar().Motivo);
        registro.AcrescentarSituacao("""{"situacao":"ENTREGUE"}""");
        registro.Descarregar();
        Assert.False(OrquestradorAnalise.PodeRetomarChatGpt(pasta, registro.Pasta));
    }

    [Fact]
    public void pastas_de_controle()
    {
        Assert.Equal(@".pmvni-ai\analise", ResultadoAnalise.PastaControle(Provedor.ChatGpt));
        Assert.Equal(@".pmvni-ai\analise-pro", ResultadoAnalise.PastaControle(Provedor.Claude));
    }

    [Fact]
    public void pdf_so_se_estiver_dentro_do_processo_e_sem_link()
    {
        using var t = Ambiente.PastaDeTeste("res-pdf");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        var doc = Escrever(Path.Combine(pasta, ".pmvni-ai", "analise", "relatorio_dados.json"), "{}");
        var dentro = Escrever(Path.Combine(pasta, ".pmvni-ai", "analise", "relatorio.pdf"), "pdf");
        var fora = Escrever(Path.Combine(t, "fora", "relatorio.pdf"), "pdf");

        string? PdfDe(string pdf)
        {
            Entrega(pasta, Provedor.ChatGpt, doc, pdf);
            var r = ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, Inicio, "reg");
            Assert.Equal(doc, r.Documento);         // o documento continua valendo; só o PDF é descartado
            return r.Pdf;
        }

        Assert.Equal(dentro, PdfDe(dentro));                                                  // absoluto dentro
        Assert.Equal(dentro, PdfDe(@".pmvni-ai\analise\relatorio.pdf"));                      // relativo dentro
        Assert.Null(PdfDe(fora));                                                             // absoluto fora
        Assert.Null(PdfDe(Path.Combine(pasta, "..", "..", "..", "fora", "relatorio.pdf")));   // subida por ".."
        Assert.Null(PdfDe(@"..\fora\relatorio.pdf"));                                         // relativo que sai
        Assert.Null(PdfDe(Path.Combine(pasta, ".pmvni-ai", "analise", "nao-existe.pdf")));    // ausente
        Assert.Null(PdfDe(pasta + "-outro\\relatorio.pdf"));                                  // prefixo parecido

        // Junção no caminho.
        var real = Path.Combine(t, "real");
        Escrever(Path.Combine(real, "relatorio.pdf"), "pdf");
        var atalho = Path.Combine(pasta, ".pmvni-ai", "analise", "atalho");
        if (Ambiente.CriarJuncao(atalho, real))
        {
            Assert.Null(PdfDe(Path.Combine(atalho, "relatorio.pdf")));
            Assert.Null(PdfDe(@".pmvni-ai\analise\atalho\relatorio.pdf"));
        }

        // A mesma regra para o PDF que a situação do kit informa (Claude).
        Assert.Equal(dentro, ResultadoAnalise.PdfDentro(pasta, dentro));
        Assert.Null(ResultadoAnalise.PdfDentro(pasta, fora));
        Assert.Null(ResultadoAnalise.PdfDentro(pasta, null));
        Assert.Null(ResultadoAnalise.PdfDentro(pasta, "  "));
    }

    [Fact]
    public void resultado_pelo_delivery()
    {
        using var t = Ambiente.PastaDeTeste("res-ok");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        var doc = Escrever(Path.Combine(pasta, ".pmvni-ai", "analise", "relatorio_dados.json"), "{}");
        var pdf = Escrever(Path.Combine(pasta, ".pmvni-ai", "analise", "relatorio.pdf"), "pdf");
        Entrega(pasta, Provedor.ChatGpt, doc, pdf);
        var r = ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, Inicio, Path.Combine(pasta, "reg"));
        Assert.Equal(doc, r.Documento);
        Assert.Equal(pdf, r.Pdf);
        Assert.Null(r.Motivo);

        // O analise-pro lê a outra pasta de controle.
        var docPro = Escrever(Path.Combine(pasta, ".pmvni-ai", "analise-pro", "relatorio_dados.json"), "{}");
        Entrega(pasta, Provedor.Claude, docPro);
        var pro = ResultadoAnalise.Localizar(pasta, Provedor.Claude, Inicio, Path.Combine(pasta, "reg"));
        Assert.Equal(docPro, pro.Documento);
        Assert.Null(pro.Pdf);

        // Caminho relativo vale em relação à pasta do processo; tolerância de 2 s no início.
        Escrever(Delivery(pasta, Provedor.ChatGpt), "{\"document\":\".pmvni-ai\\\\analise\\\\relatorio_dados.json\"}", Inicio.AddSeconds(-2));
        Assert.Equal(doc, ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, Inicio, "reg").Documento);
    }

    [Fact]
    public void resultado_recusado_com_a_mensagem()
    {
        using var t = Ambiente.PastaDeTeste("res-recusas");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        var registro = Path.Combine(pasta, ".pmvni-ai", "editor", "r1");
        string Motivo() => ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, Inicio, registro).Motivo!;
        var semNovo = $"A análise terminou, mas não deixou relatório novo (delivery.json não foi atualizado). Veja o registro: {registro}.";

        // Ausente.
        Assert.Equal(semNovo, Motivo());
        Assert.Null(ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, Inicio, registro).Documento);

        // Mais antigo que o início − 2 s.
        var doc = Escrever(Path.Combine(pasta, ".pmvni-ai", "analise", "relatorio_dados.json"), "{}");
        Escrever(Delivery(pasta, Provedor.ChatGpt), new JsonObject { ["document"] = doc }.ToJsonString(), Inicio.AddSeconds(-3));
        Assert.Equal(semNovo, Motivo());

        // JSON inválido.
        Escrever(Delivery(pasta, Provedor.ChatGpt), "{ não é json");
        Assert.StartsWith("O delivery.json da análise não pôde ser lido: ", Motivo());
        Assert.EndsWith(".", Motivo());

        // Sem document.
        Escrever(Delivery(pasta, Provedor.ChatGpt), "{\"registry\":\"x\"}");
        Assert.Equal("O delivery.json da análise não pôde ser lido: não há o campo document.", Motivo());
        Escrever(Delivery(pasta, Provedor.ChatGpt), "[1]");
        Assert.StartsWith("O delivery.json da análise não pôde ser lido: ", Motivo());

        // Fora da pasta do processo.
        var fora = Escrever(Path.Combine(t, "fora", "relatorio_dados.json"), "{}");
        Entrega(pasta, Provedor.ChatGpt, fora);
        Assert.Equal($"O relatório indicado pela análise está fora da pasta do processo: {fora}.", Motivo());
        // Prefixo parecido não é "dentro": <processo>-outro.
        var parecido = Escrever(pasta + "-outro\\relatorio_dados.json", "{}");
        Entrega(pasta, Provedor.ChatGpt, parecido);
        Assert.Equal($"O relatório indicado pela análise está fora da pasta do processo: {parecido}.", Motivo());
        // Subida por ".." também é fora.
        var subida = Path.Combine(pasta, "..", "fora", "relatorio_dados.json");
        Entrega(pasta, Provedor.ChatGpt, subida);
        Assert.StartsWith("O relatório indicado pela análise está fora da pasta do processo: ", Motivo());

        // Document ausente.
        var sumiu = Path.Combine(pasta, ".pmvni-ai", "analise", "nao-existe.json");
        Entrega(pasta, Provedor.ChatGpt, sumiu);
        Assert.Equal($"O relatório indicado pela análise não foi encontrado: {sumiu}.", Motivo());
    }

    [Fact]
    public void juncao_ou_link_no_caminho_e_recusado()
    {
        using var t = Ambiente.PastaDeTeste("res-link");
        var pasta = Ambiente.NovoProcesso(t).Pasta;
        var real = Path.Combine(t, "real");
        var doc = Escrever(Path.Combine(real, "relatorio_dados.json"), "{}");
        var link = Path.Combine(pasta, ".pmvni-ai", "analise", "atalho");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        if (!Ambiente.CriarJuncao(link, real)) return;    // sem permissão para links neste PC: nada a provar
        var viaLink = Path.Combine(link, "relatorio_dados.json");
        Entrega(pasta, Provedor.ChatGpt, viaLink);
        var r = ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, Inicio, "reg");
        Assert.Null(r.Documento);
        Assert.Equal($"O relatório indicado pela análise está fora da pasta do processo: {viaLink}.", r.Motivo);

        // O arquivo em si ser um link simbólico também é recusado.
        var arquivoLink = Path.Combine(pasta, ".pmvni-ai", "analise", "relatorio_dados.json");
        try { File.CreateSymbolicLink(arquivoLink, doc); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        Entrega(pasta, Provedor.ChatGpt, arquivoLink);
        Assert.Equal($"O relatório indicado pela análise está fora da pasta do processo: {arquivoLink}.",
            ResultadoAnalise.Localizar(pasta, Provedor.ChatGpt, Inicio, "reg").Motivo);
    }
}
