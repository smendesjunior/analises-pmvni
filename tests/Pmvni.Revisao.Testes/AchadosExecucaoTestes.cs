using System.Security.Cryptography;
using System.Text.Json;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class AchadosExecucaoTestes
{
    [Fact]
    public void Recupera_contagem_por_etapa_sem_duplicar_eventos_ou_exigir_achado()
    {
        using var pasta = Ambiente.PastaDeTeste("contagem-etapas");
        string raiz = pasta;
        void Evento(string nome, string etapa, int seq, int avaliados, int total) =>
            File.WriteAllText(Path.Combine(raiz, nome + ".json"),
                JsonSerializer.Serialize(new { etapa, sequencia_etapa = seq, avaliados, total }), new System.Text.UTF8Encoding(seq == 3));
        Evento("a", "analyze", 3, 2, 5);
        Evento("b", "analyze", 2, 1, 4);
        Evento("c", "analyze", 4, 8, 5);
        Evento("d", "technical_audit", 1, 0, 2);
        var painel = AchadosExecucao.Ler(raiz, raiz);
        Assert.Empty(painel.Itens);
        Assert.Equal(new ProgressoEtapa("analyze", 3, 2, 5), painel.Etapas["analyze"]);
        Assert.Equal(0, painel.Etapas["technical_audit"].Avaliados);
    }

    [Fact]
    public void Recupera_revisao_unica_e_confere_a_prova_do_item()
    {
        using var pasta = Ambiente.PastaDeTeste("achados");
        string raiz = pasta;
        var eventos = Directory.CreateDirectory(Path.Combine(raiz, "progresso")).FullName;
        var prova = Path.Combine(raiz, "auditoria.txt");
        File.WriteAllText(prova, "item A conferido");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(prova)));
        void Emitir(string nome, int seq, string situacao) => File.WriteAllText(Path.Combine(eventos, nome + ".json"),
            JsonSerializer.Serialize(new { item_id = "A", sequencia = seq, situacao, titulo = "Área do lote",
                evidencia_verificacao = "auditoria.txt", sha256_verificacao = hash }));
        Emitir("um", 1, "NAO_COMPROVADO");
        Emitir("dois", 2, "ATENDE");
        Emitir("duplicado", 2, "ATENDE");
        var item = Assert.Single(AchadosExecucao.Ler(eventos, raiz).Itens);
        Assert.Equal("ATENDE", item.Situacao);
        Assert.True(item.Verificado);
        File.AppendAllText(prova, "alterado");
        Assert.False(Assert.Single(AchadosExecucao.Ler(eventos, raiz).Itens).Verificado);
    }
}
