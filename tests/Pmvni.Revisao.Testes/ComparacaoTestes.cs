using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Comparar com o original (Tarefa 11; desenho §8.5): só o que mudou, com os textos como são impressos hoje.</summary>
public class ComparacaoTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar(string caso)
    {
        var caminho = Ambiente.Corpus(caso + ".json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    private static string Original(ItemRevisao i, string campo) => i.Campos[campo].Original!.GetValue<string>();

    [Fact]
    public void comparacao_lista_so_o_que_mudou()
    {
        var c = Importar("longo");
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        var i1 = s.Projeto.Incluidos[0];
        var i2 = s.Projeto.Incluidos[1];
        var i3 = s.Projeto.Incluidos[2];
        var situacao3 = i3.Situacao.Original!.GetValue<string>();
        var nova = situacao3 == "ATENDE" ? "NAO_ATENDE" : "ATENDE";

        // 1 campo (cita o item 03, que passará a ser o 02 quando o 02 sair) + 1 exclusão + 1 situação.
        s.Aplicar(new DefinirCampo(i1.Id, "verificacao", "Conforme o item 03, tudo certo."));
        s.Aplicar(new Excluir(i2.Id));
        s.Aplicar(new DefinirSituacao(i3.Id, nova));
        s.Aplicar(new DefinirJustificativa(i3.Id, "Comprovado no protocolo."));

        var d = Comparacao.Diferencas(s.Projeto, s.HistoricoCompleto);

        Assert.Equal(3, d.Count); // os outros 57 itens não mudaram: não entram
        Assert.Equal(new[] { i1.Id, i2.Id, i3.Id }, d.Select(x => x.Item));

        var um = d[0];
        Assert.Equal(1, um.NumeroAtual);
        Assert.False(um.Excluido);
        Assert.Null(um.SituacaoRevisada);
        Assert.Null(um.SituacaoOriginal);
        var campo = Assert.Single(um.Campos);
        Assert.Equal("verificacao", campo.Campo);
        Assert.Equal(Original(i1, "verificacao"), campo.Original);
        Assert.Equal("Conforme o item 02, tudo certo.", campo.Revisado); // número de hoje: o item 03 virou o 02

        var dois = d[1];
        Assert.True(dois.Excluido);
        Assert.Null(dois.NumeroAtual);
        Assert.Equal(2, dois.NumeroOriginal);
        Assert.Empty(dois.Campos);
        Assert.Null(dois.SituacaoRevisada);

        var tres = d[2];
        Assert.Equal(2, tres.NumeroAtual);
        Assert.Equal(3, tres.NumeroOriginal);
        Assert.Equal(situacao3, tres.SituacaoOriginal);
        Assert.Equal(nova, tres.SituacaoRevisada);
        Assert.Empty(tres.Campos);
        Assert.Equal("Comprovado no protocolo.", tres.Justificativa);
        Assert.Equal("Situação: " + ConferenciaRevisao.TextoSituacao(situacao3) + " → " + ConferenciaRevisao.TextoSituacao(nova), Comparacao.LinhaDaSituacao(tres));
        Assert.Equal("ITEM 02", Comparacao.Rotulo(tres));
        Assert.Equal("Item excluído (era o 02)", Comparacao.Rotulo(dois));
        Assert.Null(Comparacao.LinhaDaSituacao(um));
        Assert.Equal("Verificação técnica", Comparacao.NomeDoCampo("verificacao"));

        // O texto da janela traz, na ordem do relatório, só o que mudou e os textos exatos.
        var esperado = string.Join("\n",
            "3 itens alterados em relação ao original recebido:",
            "",
            "ITEM 01",
            "  Verificação técnica:",
            "    Original: " + Original(i1, "verificacao"),
            "    Revisado: Conforme o item 02, tudo certo.",
            "",
            "Item excluído (era o 02)",
            "  Excluído do relatório (não sai no PDF).",
            "",
            "ITEM 02",
            "  " + Comparacao.LinhaDaSituacao(tres),
            "  Justificativa: Comprovado no protocolo.",
            "",
            "Citações convertidas na importação (0):",
            "  Nenhuma.");
        Assert.Equal(esperado, Comparacao.Texto(d, Comparacao.CitacoesImportadas(s.Projeto, s.HistoricoCompleto)));
        Assert.Equal("Nenhum item foi alterado em relação ao original recebido.\n\nCitações convertidas na importação (0):\n  Nenhuma.", Comparacao.Texto([], []));
    }

    [Fact]
    public void sem_alteracoes_nada_a_comparar()
    {
        var c = Importar("curto");
        Assert.Empty(Comparacao.Diferencas(c.Projeto, c.Historico));
        Assert.Empty(Comparacao.CitacoesImportadas(c.Projeto, c.Historico));
    }

    [Fact]
    public void restaurar_ao_original_tira_o_item_da_comparacao()
    {
        var c = Importar("curto");
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        var id = s.Projeto.Incluidos[0].Id;
        s.Aplicar(new DefinirCampo(id, "titulo", "Outro título"));
        Assert.Single(Comparacao.Diferencas(s.Projeto, s.HistoricoCompleto));

        s.Aplicar(new RestaurarCampo(id, "titulo"));

        Assert.Empty(Comparacao.Diferencas(s.Projeto, s.HistoricoCompleto));
    }

    [Fact]
    public void citacoes_convertidas_aparecem_em_secao_propria_mesmo_em_item_sem_alteracao()
    {
        // O item 03 do "curto" ganha duas citações ("item 02" e "ITEM 01") antes da importação.
        var caminho = Ambiente.Corpus("curto.json");
        var dados = JsonNode.Parse(File.ReadAllBytes(caminho))!.AsObject();
        var item3 = dados["itens"]![2]!.AsObject();
        item3["verificacao"] = item3["verificacao"]!.GetValue<string>() + " Ver item 02 e ITEM 01.";
        var c = Importador.Importar(System.Text.Encoding.UTF8.GetBytes(dados.ToJsonString()), caminho, Modelo, Ambiente.Contexto());

        // Nenhum item foi revisado: a lista de diferenças é vazia, mas as citações convertidas estão todas lá.
        Assert.Empty(Comparacao.Diferencas(c.Projeto, c.Historico));
        var todas = Comparacao.CitacoesImportadas(c.Projeto, c.Historico);
        Assert.Equal(2, todas.Count);
        Assert.Equal("Verificação técnica: «item 02» → ITEM 02", todas[0].Texto);
        Assert.Equal("Verificação técnica: «ITEM 01» → ITEM 01", todas[1].Texto);
        Assert.All(todas, t => Assert.Equal(c.Projeto.Itens[2].Id, t.Item));
        Assert.Equal(3, todas[0].NumeroDoItem);

        // Exclui o item 02 e revisa o 03: a citação ao item excluído mostra o número original e a marca; o item revisado leva as suas.
        var s = new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
        s.Aplicar(new Excluir(s.Projeto.Itens[1].Id));
        var id3 = s.Projeto.Itens[2].Id;
        var atual = s.Projeto.Itens[2].Situacao.Original!.GetValue<string>();
        s.Aplicar(new DefinirSituacao(id3, atual == "ATENDE" ? "NAO_ATENDE" : "ATENDE"));

        var depois = Comparacao.CitacoesImportadas(s.Projeto, s.HistoricoCompleto);
        Assert.Equal("Verificação técnica: «item 02» → ITEM 02 (excluído)", depois[0].Texto);
        Assert.Equal(2, depois[0].NumeroDoItem); // o item 03 virou o 02
        var diferencas = Comparacao.Diferencas(s.Projeto, s.HistoricoCompleto);
        Assert.Equal(2, diferencas.Count); // o excluído (02) e o item 03; o 01 não mudou
        var tres = diferencas.Single(x => x.Item == id3);
        Assert.Equal(new[] { depois[0].Texto, depois[1].Texto }, tres.CitacoesConvertidas);
        Assert.Empty(diferencas.Single(x => x.Excluido).CitacoesConvertidas);
    }
}
