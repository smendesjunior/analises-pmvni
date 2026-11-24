using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Agentes.Testes;

public partial class OrquestradorTestes
{
    [Fact]
    public async Task AnaliseEmDocumentosExecutaComAsCopiasEPublicaEntregaDaMesmaExecucao()
    {
        using var cena = new Cena("orq-documentos", "ok", Provedor.ChatGpt);
        var documentos = Directory.CreateTempSubdirectory("PMVNI-Documentos-Teste-");
        try
        {
            var pasta = cena.Processo.Pasta;
            var memorial = Path.Combine(pasta, "DOCUMENTOS", "Memorial.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(memorial)!);
            File.WriteAllText(memorial, "Memorial municipal fictício");
            var transportador = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(cena.Pecas.Analise62!)!, "..", "..", "portabilidade_execucao.py"));
            Directory.CreateDirectory(Path.GetDirectoryName(transportador)!);
            File.WriteAllText(transportador, "# Controlador de teste para uma execução nova");
            var orquestrador = new OrquestradorAnalise
            {
                DocumentosParaTeste = documentos.FullName,
                AmbienteExtra = new Dictionary<string, string> { ["AGENTE_FALSO_CENARIO"] = cena.Cenario },
            };
            string? local = null;
            var pedido = cena.Pedido(Provedor.ChatGpt) with { UsarDocumentos = true, AoPrepararLocal = p => local = p };
            var fim = await Ambiente.Com(orquestrador.ExecutarAsync(pedido, cena.Progresso, CancellationToken.None), Limite);
            Assert.True(fim.Situacao == "concluida", fim.Mensagem);
            Assert.NotNull(local);
            Assert.StartsWith(documentos.FullName + Path.DirectorySeparatorChar, local, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(File.ReadAllBytes(memorial), File.ReadAllBytes(Path.Combine(local, "DOCUMENTOS", "Memorial.pdf")));
            Assert.Equal(File.ReadAllBytes(Planta(cena)), File.ReadAllBytes(Path.Combine(local, Path.GetRelativePath(pasta, Planta(cena)))));
            var recebido = Json(Path.Combine(local, ".pmvni-ai", "agente-falso", "recebido.json"));
            Assert.Equal(local, recebido["cwd"]!.GetValue<string>());
            var argumentos = recebido["argv"]!.AsArray().Select(a => a!.GetValue<string>()).ToArray();
            var entradas = argumentos.Skip(Array.IndexOf(argumentos, "--inputs") + 1).ToArray();
            Assert.Equal(2, entradas.Length);
            Assert.All(entradas, entrada => Assert.StartsWith(local + Path.DirectorySeparatorChar, entrada, StringComparison.OrdinalIgnoreCase));
            Assert.False(Directory.Exists(Path.Combine(pasta, ".pmvni-ai", "agente-falso")));
            Assert.False(Directory.Exists(Path.Combine(pasta, ".pmvni-ai", "cache-pdf")));
            var id = Path.GetFileName(cena.Registro.Pasta);
            Assert.True(RecuperacaoAnaliseLocal.Existe(pasta, id));
            var entrega = RecuperacaoAnaliseLocal.EntregaPublicada(pasta, id);
            Assert.Equal(fim.Documento, entrega!.Documento);
            Assert.Equal(fim.Pdf, entrega.Pdf);
            Assert.True(File.Exists(fim.Pdf));
            Assert.DoesNotContain(".pmvni-ai", fim.Pdf!, StringComparison.OrdinalIgnoreCase);
        }
        finally { documentos.Delete(true); }
    }
}
