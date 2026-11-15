using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class ComandosTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar(string caso)
    {
        var caminho = Ambiente.Corpus(caso + ".json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    private static SessaoRevisao Sessao(ConteudoPacote c, string usuario = "teste") =>
        new(c.Projeto, c.Historico, Ambiente.Contexto(usuario));

    private static string Id(SessaoRevisao s, int numero) => s.Projeto.Incluidos[numero - 1].Id;

    private static byte[] Bytes(Projeto p) => ProjetoJson.Escrever(p);

    private static string Original(ItemRevisao i, string campo) => i.Campos[campo].Original!.GetValue<string>();

    // ---------- TextoEditado ----------

    [Fact]
    public void normalizar_texto_editado()
    {
        Assert.Equal("Linha 1 Linha 2", TextoEditado.Normalizar("  Linha 1\r\n\r\nLinha 2\n "));
        Assert.Equal("a b c d e", TextoEditado.Normalizar("a\rb\u2028c\u2029d\r\ne"));
        Assert.Equal("a & <b> 😀 \"x\" 'y'", TextoEditado.Normalizar("a & <b> 😀 \"x\" 'y'"));
        Assert.Equal("dois  espacos internos ficam", TextoEditado.Normalizar("dois  espacos internos ficam"));
        Assert.Equal("", TextoEditado.Normalizar(" \r\n "));
    }

    // ---------- situação ----------

    [Fact]
    public void definir_situacao_registra_antes_e_depois()
    {
        var s = Sessao(Importar("curto"), "fulano");
        var id = Id(s, 2);

        var r = s.Aplicar(new DefinirSituacao(id, "ATENDE"));

        Assert.NotNull(r);
        Assert.Equal("NAO_COMPROVADO", r.Antes!.GetValue<string>());
        Assert.Equal("ATENDE", r.Depois!.GetValue<string>());
        Assert.Equal("fulano", r.Usuario);
        Assert.Equal(id, r.Item);
        Assert.Equal(2, r.N);
        Assert.Equal("ATENDE", s.Projeto.Item(id)!.Situacao.Revisada);
        Assert.True(s.PodeDesfazer);
        Assert.False(s.PodeRefazer);
    }

    [Fact]
    public void voltar_a_situacao_original_zera_revisada_e_guarda_justificativa()
    {
        var s = Sessao(Importar("curto"));
        var id = Id(s, 2);
        s.Aplicar(new DefinirSituacao(id, "ATENDE"));
        s.Aplicar(new DefinirJustificativa(id, "Comprovado no protocolo."));

        var r = s.Aplicar(new DefinirSituacao(id, "NAO_COMPROVADO"));

        Assert.NotNull(r);
        var item = s.Projeto.Item(id)!;
        Assert.Null(item.Situacao.Revisada);
        Assert.Equal("Comprovado no protocolo.", item.Situacao.Justificativa);
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
    }

    [Fact]
    public void situacao_invalida_e_recusada()
    {
        var s = Sessao(Importar("curto"));
        var antes = Bytes(s.Projeto);

        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirSituacao(Id(s, 1), "CONTA_CONFERE")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirSituacao(Id(s, 1), "")));

        Assert.Equal(antes, Bytes(s.Projeto));
        Assert.Single(s.HistoricoCompleto);
        Assert.False(s.PodeDesfazer);
    }

    [Fact]
    public void situacao_igual_a_atual_nao_registra()
    {
        var s = Sessao(Importar("curto"));
        Assert.Null(s.Aplicar(new DefinirSituacao(Id(s, 2), "NAO_COMPROVADO")));
        Assert.Single(s.HistoricoCompleto);
    }

    [Fact]
    public void item_inexistente_e_recusado()
    {
        var s = Sessao(Importar("curto"));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirSituacao("it_nao_existe", "ATENDE")));
    }

    // ---------- campos ----------

    [Theory]
    [InlineData("titulo", "O título não pode ficar vazio.")]
    [InlineData("verificacao", "A verificação técnica não pode ficar vazia.")]
    [InlineData("providencia", "A providência não pode ficar vazia.")]
    [InlineData("base_legal", "A base legal não pode ficar vazia.")]
    [InlineData("referencia", "A referência não pode ficar vazia.")]
    public void campo_vazio_e_recusado_nos_cinco_campos(string campo, string mensagem)
    {
        var s = Sessao(Importar("curto"));
        var antes = Bytes(s.Projeto);

        var e = Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo(Id(s, 1), campo, "  \r\n \u2028 ")));

        Assert.Equal(mensagem, e.Message);
        Assert.Equal(antes, Bytes(s.Projeto));
        Assert.Single(s.HistoricoCompleto);
    }

    [Fact]
    public void campo_desconhecido_e_recusado()
    {
        var s = Sessao(Importar("curto"));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo(Id(s, 1), "situacao", "ATENDE")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo(Id(s, 1), "id", "x")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new RestaurarCampo(Id(s, 1), "id")));
    }

    [Fact]
    public void campo_editado_normaliza_o_texto_e_registra()
    {
        var s = Sessao(Importar("curto"));
        var id = Id(s, 1);
        var original = Original(s.Projeto.Item(id)!, "titulo");

        var r = s.Aplicar(new DefinirCampo(id, "titulo", "  Novo\r\ntitulo & <b>  \n"));

        Assert.NotNull(r);
        Assert.Equal("titulo", r.Campo);
        Assert.Equal("Novo titulo & <b>", s.Projeto.Item(id)!.Campos["titulo"].Revisado);
        Assert.Equal(original, r.Antes!["texto"]!.GetValue<string>());
        Assert.Equal("Novo titulo & <b>", r.Depois!["texto"]!.GetValue<string>());
        Assert.Equal(original, Original(s.Projeto.Item(id)!, "titulo")); // o original nunca muda
    }

    [Fact]
    public void campo_igual_ao_atual_nao_registra()
    {
        var s = Sessao(Importar("curto"));
        var id = Id(s, 1);
        var atual = DadosEfetivos.TextoEfetivo(s.Projeto, s.Projeto.Item(id)!, "titulo")!;

        Assert.Null(s.Aplicar(new DefinirCampo(id, "titulo", atual)));
        Assert.Null(s.Aplicar(new DefinirCampo(id, "titulo", "  " + atual + "\n")));

        Assert.False(s.PodeDesfazer);
        Assert.Single(s.HistoricoCompleto);
        Assert.Null(s.Projeto.Item(id)!.Campos["titulo"].Revisado);
    }

    [Fact]
    public void editar_de_volta_ao_original_zera_revisado()
    {
        var s = Sessao(Importar("curto"));
        var id = Id(s, 1);
        var original = Original(s.Projeto.Item(id)!, "titulo");
        s.Aplicar(new DefinirCampo(id, "titulo", "Outro"));
        Assert.Equal("Outro", s.Projeto.Item(id)!.Campos["titulo"].Revisado);

        var r = s.Aplicar(new DefinirCampo(id, "titulo", original));

        Assert.NotNull(r);
        Assert.Null(s.Projeto.Item(id)!.Campos["titulo"].Revisado);
    }

    [Fact]
    public void editar_reconverte_citacoes_com_a_numeracao_atual()
    {
        var s = Sessao(Importar("curto"));
        var id1 = Id(s, 1);
        var id3 = Id(s, 3);

        var r = s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 03 e itens 02 a 03; item 3.2; item 99."))!;

        var revisado = s.Projeto.Item(id1)!.Campos["verificacao"].Revisado!;
        Assert.Contains("{{ref:" + id3 + ":2}}", revisado);
        Assert.DoesNotContain("item 99{{", revisado);
        var citacoes = r.Depois!["citacoes"]!.AsArray();
        Assert.Single(citacoes);
        Assert.Equal(id3, citacoes[0]!["alvo"]!.GetValue<string>());
        Assert.Equal("item 03", citacoes[0]!["trecho"]!.GetValue<string>());

        // a citação segue o item citado quando a numeração muda
        s.Aplicar(new Excluir(Id(s, 2)));
        Assert.StartsWith("Ver item 02 e", DadosEfetivos.TextoEfetivo(s.Projeto, s.Projeto.Item(id1)!, "verificacao"));
    }

    [Fact]
    public void editar_campo_que_cita_item_excluido_mantem_a_citacao_ao_excluido()
    {
        var s = Sessao(Importar("curto"));
        var id1 = Id(s, 1);
        var id2 = Id(s, 2);
        var id3 = Id(s, 3);
        s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 02."));
        Assert.Contains("{{ref:" + id2 + ":2}}", s.Projeto.Item(id1)!.Campos["verificacao"].Revisado);
        s.Aplicar(new Excluir(id2)); // imprime "item 02" (o original); o item 02 de agora é o antigo 03

        var r = s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 02, com outra palavra."))!;

        var revisado = s.Projeto.Item(id1)!.Campos["verificacao"].Revisado!;
        Assert.Contains("{{ref:" + id2 + ":2}}", revisado);
        Assert.DoesNotContain(id3, revisado);
        Assert.Single(r.Depois!["citacoes"]!.AsArray());
        Assert.Equal(new[] { "ITEM 01 cita o item excluído que era o item 02 (campo verificacao)" }, ConferenciaRevisao.CitacoesAExcluidos(s.Projeto));
        Assert.Equal("Ver item 02, com outra palavra.", DadosEfetivos.TextoEfetivo(s.Projeto, s.Projeto.Item(id1)!, "verificacao"));
    }

    [Fact]
    public void edicao_que_acrescenta_item_novo_usa_a_numeracao_atual()
    {
        var s = Sessao(Importar("curto"));
        var id1 = Id(s, 1);
        var id3 = Id(s, 3);
        s.Aplicar(new Excluir(Id(s, 2)));

        s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 02."));

        Assert.Contains("{{ref:" + id3 + ":2}}", s.Projeto.Item(id1)!.Campos["verificacao"].Revisado); // o 02 de agora
        Assert.Empty(ConferenciaRevisao.CitacoesAExcluidos(s.Projeto));
    }

    [Fact]
    public void edicao_mantem_citacao_a_item_incluido_e_acrescenta_outra_ao_lado()
    {
        var s = Sessao(Importar("curto"));
        var id1 = Id(s, 1);
        var id2 = Id(s, 2);
        var id3 = Id(s, 3);
        s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 03."));
        var antes = s.Projeto.Item(id1)!.Campos["verificacao"].Revisado!;

        s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 03 (corrigido)."));
        Assert.Equal(antes.Replace(".", " (corrigido)."), s.Projeto.Item(id1)!.Campos["verificacao"].Revisado);

        s.Aplicar(new DefinirCampo(id1, "verificacao", "Ver item 03 e item 02."));
        var revisado = s.Projeto.Item(id1)!.Campos["verificacao"].Revisado!;
        Assert.Contains("{{ref:" + id3 + ":2}} e item {{ref:" + id2 + ":2}}", revisado);
    }

    [Fact]
    public void restaurar_campo_volta_ao_original_e_sem_revisao_nao_tem_efeito()
    {
        var s = Sessao(Importar("curto"));
        var id = Id(s, 1);
        Assert.Null(s.Aplicar(new RestaurarCampo(id, "titulo")));

        s.Aplicar(new DefinirCampo(id, "titulo", "Outro"));
        var r = s.Aplicar(new RestaurarCampo(id, "titulo"));

        Assert.NotNull(r);
        Assert.Equal("RestaurarCampo", r.Tipo);
        Assert.Null(s.Projeto.Item(id)!.Campos["titulo"].Revisado);
    }

    [Fact]
    public void campo_ausente_no_original_pode_ser_criado_e_o_desfazer_o_remove()
    {
        var c = Importar("curto");
        var item = c.Projeto.Itens[0];
        item.Chaves.Remove("providencia");
        item.Campos.Remove("providencia");
        var s = Sessao(c);
        var antes = Bytes(s.Projeto);

        s.Aplicar(new DefinirCampo(item.Id, "providencia", "Criada"));
        Assert.Equal("Criada", s.Projeto.Item(item.Id)!.Campos["providencia"].Revisado);
        Assert.True(s.Projeto.Item(item.Id)!.Campos["providencia"].Ausente);

        s.Desfazer();
        Assert.Equal(antes, Bytes(s.Projeto));
    }

    // ---------- excluir / restaurar ----------

    [Fact]
    public void excluir_e_restaurar_mudam_a_numeracao_e_a_ordem_original_volta()
    {
        var s = Sessao(Importar("curto"));
        var id2 = Id(s, 2);
        var id3 = Id(s, 3);

        s.Aplicar(new Excluir(id2));
        Assert.Equal(2, s.Projeto.NumeroAtual(id3));
        Assert.Null(s.Aplicar(new Excluir(id2))); // já excluído

        s.Aplicar(new Restaurar(id2));
        Assert.Equal(2, s.Projeto.NumeroAtual(id2));
        Assert.Null(s.Aplicar(new Restaurar(id2))); // já incluído
    }

    [Fact]
    public void excluir_o_ultimo_incluido_e_recusado()
    {
        var s = Sessao(Importar("curto"));
        var id1 = Id(s, 1);
        var id2 = Id(s, 2);
        var id3 = Id(s, 3);
        s.Aplicar(new Excluir(id1));
        s.Aplicar(new Excluir(id2));
        var antes = Bytes(s.Projeto);
        var registros = s.HistoricoCompleto.Count;

        var e = Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new Excluir(id3)));

        Assert.Equal("O relatório precisa de ao menos um item incluído.", e.Message);
        Assert.Equal(antes, Bytes(s.Projeto));
        Assert.Equal(registros, s.HistoricoCompleto.Count);
        Assert.True(s.Projeto.Item(id3)!.Incluido);
    }

    [Fact]
    public void restaurar_item_original_zera_campos_e_situacao()
    {
        var s = Sessao(Importar("curto"));
        var id = Id(s, 2);
        s.Aplicar(new DefinirSituacao(id, "ATENDE"));
        s.Aplicar(new DefinirJustificativa(id, "ok"));
        s.Aplicar(new DefinirCampo(id, "titulo", "T"));
        s.Aplicar(new DefinirCampo(id, "base_legal", "B"));
        Assert.Null(s.Aplicar(new RestaurarItemOriginal(Id(s, 1)))); // item sem revisão

        var r = s.Aplicar(new RestaurarItemOriginal(id));

        Assert.NotNull(r);
        var item = s.Projeto.Item(id)!;
        Assert.Null(item.Situacao.Revisada);
        Assert.All(item.Campos.Values, c => Assert.Null(c.Revisado));
        Assert.Equal("ok", item.Situacao.Justificativa);

        s.Desfazer();
        Assert.Equal("ATENDE", s.Projeto.Item(id)!.Situacao.Revisada);
        Assert.Equal("T", s.Projeto.Item(id)!.Campos["titulo"].Revisado);
        Assert.Equal("B", s.Projeto.Item(id)!.Campos["base_legal"].Revisado);
    }

    // ---------- desfazer / refazer / histórico ----------

    private static void Vinte(SessaoRevisao s)
    {
        string Outra(string atual) => atual == "ATENDE" ? "NAO_ATENDE" : "ATENDE";
        for (var i = 1; i <= 6; i++)
            Assert.NotNull(s.Aplicar(new DefinirCampo(Id(s, i), "titulo", "Titulo novo " + i)));
        for (var i = 8; i <= 14; i++)
        {
            var it = s.Projeto.Incluidos[i - 1];
            Assert.NotNull(s.Aplicar(new DefinirSituacao(it.Id, Outra(it.Situacao.Original!.GetValue<string>()))));
        }
        for (var i = 15; i <= 17; i++)
            Assert.NotNull(s.Aplicar(new DefinirJustificativa(Id(s, i), "Justificativa " + i)));
        Assert.NotNull(s.Aplicar(new Excluir(Id(s, 18))));
        Assert.NotNull(s.Aplicar(new Excluir(Id(s, 18))));
        Assert.NotNull(s.Aplicar(new DefinirCampo(Id(s, 19), "verificacao", "Ver item 05 e item 30.")));
        Assert.NotNull(s.Aplicar(new RestaurarCampo(Id(s, 1), "titulo")));
    }

    [Fact]
    public void desfazer_e_refazer_ilimitados_e_append_only()
    {
        var c = Importar("longo");
        var inicial = Bytes(c.Projeto);
        var s = Sessao(c);
        Vinte(s);
        var depoisDos20 = Bytes(s.Projeto);
        Assert.Equal(21, s.HistoricoCompleto.Count);

        for (var i = 0; i < 20; i++) Assert.NotNull(s.Desfazer());
        Assert.Null(s.Desfazer());
        Assert.False(s.PodeDesfazer);
        Assert.True(s.PodeRefazer);
        Assert.Equal(inicial, Bytes(s.Projeto));

        for (var i = 0; i < 20; i++) Assert.NotNull(s.Refazer());
        Assert.Null(s.Refazer());
        Assert.True(s.PodeDesfazer);
        Assert.False(s.PodeRefazer);
        Assert.Equal(depoisDos20, Bytes(s.Projeto));

        var h = s.HistoricoCompleto;
        Assert.Equal(1 + 60, h.Count);
        for (var i = 1; i < h.Count; i++) Assert.True(h[i].N > h[i - 1].N, "N deve crescer: " + h[i].N);

        var desfeitos = h.Where(r => r.Tipo == "desfeito").ToList();
        var refeitos = h.Where(r => r.Tipo == "refeito").ToList();
        Assert.Equal(20, desfeitos.Count);
        Assert.Equal(20, refeitos.Count);
        // desfaz do último ao primeiro; cada Ref aponta para o N de um comando
        Assert.Equal(Enumerable.Range(2, 20).Reverse(), desfeitos.Select(r => r.Ref!.Value));
        Assert.Equal(Enumerable.Range(2, 20), refeitos.Select(r => r.Ref!.Value));
        Assert.All(desfeitos.Concat(refeitos), r => Assert.Contains(h, x => x.N == r.Ref && x.Tipo != "desfeito" && x.Tipo != "refeito"));
    }

    [Fact]
    public void desfazer_traz_de_volta_o_estado_anterior_de_cada_tipo()
    {
        var s = Sessao(Importar("curto"));
        var id = Id(s, 2);
        var inicial = Bytes(s.Projeto);

        s.Aplicar(new DefinirSituacao(id, "ATENDE"));
        var a = Bytes(s.Projeto);
        s.Aplicar(new DefinirJustificativa(id, "j"));
        var b = Bytes(s.Projeto);
        s.Aplicar(new DefinirCampo(id, "titulo", "T"));
        var c = Bytes(s.Projeto);
        s.Aplicar(new Excluir(id));

        s.Desfazer(); Assert.Equal(c, Bytes(s.Projeto));
        s.Desfazer(); Assert.Equal(b, Bytes(s.Projeto));
        s.Desfazer(); Assert.Equal(a, Bytes(s.Projeto));
        s.Desfazer(); Assert.Equal(inicial, Bytes(s.Projeto));
    }

    [Fact]
    public void novo_comando_limpa_o_refazer()
    {
        var s = Sessao(Importar("curto"));
        s.Aplicar(new DefinirSituacao(Id(s, 2), "ATENDE"));
        s.Desfazer();
        Assert.True(s.PodeRefazer);

        s.Aplicar(new DefinirCampo(Id(s, 1), "titulo", "Outro"));

        Assert.False(s.PodeRefazer);
        Assert.Null(s.Refazer());
    }

    [Fact]
    public void registrado_e_avisado_em_cada_registro()
    {
        var s = Sessao(Importar("curto"));
        var vistos = new List<RegistroHistorico>();
        s.Registrado += vistos.Add;

        s.Aplicar(new DefinirSituacao(Id(s, 2), "ATENDE"));
        s.Aplicar(new DefinirSituacao(Id(s, 2), "ATENDE")); // sem efeito: nada registrado
        s.Desfazer();
        s.Refazer();

        Assert.Equal(new[] { "DefinirSituacao", "desfeito", "refeito" }, vistos.Select(r => r.Tipo));
    }

    [Fact]
    public void testar_nao_muda_a_sessao()
    {
        var s = Sessao(Importar("curto"));
        var antes = Bytes(s.Projeto);

        var teste = s.Testar(new DefinirCampo(Id(s, 1), "titulo", "Outro"));

        Assert.Equal("Outro", teste.Item(Id(s, 1))!.Campos["titulo"].Revisado);
        Assert.Equal(antes, Bytes(s.Projeto));
        Assert.Single(s.HistoricoCompleto);
        Assert.False(s.PodeDesfazer);
        Assert.Throws<ComandoRecusadoException>(() => s.Testar(new DefinirCampo(Id(s, 1), "titulo", " ")));
        Assert.Equal(antes, Bytes(s.Testar(new DefinirSituacao(Id(s, 2), "NAO_COMPROVADO")))); // sem efeito: devolve cópia igual
    }

    [Fact]
    public void reaplicar_registros_reproduz_o_estado()
    {
        var a = Importar("longo");
        // mesmos ids nas duas sessões (a segunda parte do mesmo pacote): clona o projeto e o histórico
        var sa = new SessaoRevisao(a.Projeto, a.Historico, Ambiente.Contexto());
        var sb = new SessaoRevisao(a.Projeto.Clonar(), a.Historico, Ambiente.Contexto());
        Vinte(sa);
        sa.Desfazer();
        sa.Desfazer();
        sa.Refazer();

        sb.Reaplicar(sa.Pendentes);

        Assert.Equal(Bytes(sa.Projeto), Bytes(sb.Projeto));
        Assert.Equal(sa.HistoricoCompleto.Count, sb.HistoricoCompleto.Count);
        Assert.Equal(sa.PodeDesfazer, sb.PodeDesfazer);
        Assert.Equal(sa.PodeRefazer, sb.PodeRefazer);
        Assert.Equal(sa.Pendentes.Count, sb.Pendentes.Count);

        // a pilha reconstruída desfaz como a original
        sa.Desfazer();
        sb.Desfazer();
        Assert.Equal(Bytes(sa.Projeto), Bytes(sb.Projeto));
        // e o próximo registro continua a numeração
        Assert.Equal(sa.Aplicar(new DefinirCampo(Id(sa, 2), "titulo", "zz"))!.N, sb.Aplicar(new DefinirCampo(Id(sb, 2), "titulo", "zz"))!.N);
    }

    [Fact]
    public void reaplicar_ignora_o_que_ja_esta_no_historico()
    {
        var c = Importar("curto");
        var s = Sessao(c);
        s.Aplicar(new DefinirSituacao(Id(s, 2), "ATENDE"));
        var registros = s.HistoricoCompleto.ToList();

        s.Reaplicar(registros); // tudo já está lá

        Assert.Equal(registros.Count, s.HistoricoCompleto.Count);
        Assert.Equal("ATENDE", s.Projeto.Item(Id(s, 2))!.Situacao.Revisada);
    }

    [Fact]
    public void reaplicar_inconsistente_nao_muda_nada()
    {
        var s = Sessao(Importar("curto"));
        var antes = Bytes(s.Projeto);
        var ruim = new RegistroHistorico(2, DateTimeOffset.UtcNow, "x", "DefinirSituacao", "it_nao_existe", null, null, JsonValue.Create("ATENDE"), null);

        Assert.Throws<InvalidOperationException>(() => s.Reaplicar(new[] { ruim }));

        Assert.Equal(antes, Bytes(s.Projeto));
        Assert.Single(s.HistoricoCompleto);
    }

    [Fact]
    public void pendentes_contam_desde_a_ultima_gravacao()
    {
        var s = Sessao(Importar("curto"));
        Assert.Empty(s.Pendentes);

        s.Aplicar(new DefinirSituacao(Id(s, 2), "ATENDE"));
        s.Aplicar(new Excluir(Id(s, 3)));
        Assert.Equal(2, s.Pendentes.Count);

        s.MarcarGravado();
        Assert.Empty(s.Pendentes);
        Assert.Equal(3, s.HistoricoCompleto.Count);

        s.Desfazer();
        Assert.Single(s.Pendentes);
        Assert.Equal("desfeito", s.Pendentes[0].Tipo);
    }

    // ---------- descrição do histórico ----------

    [Fact]
    public void descricao_do_historico()
    {
        var s = Sessao(Importar("curto"));
        var id2 = Id(s, 2);
        var id3 = Id(s, 3);

        var sit = s.Aplicar(new DefinirSituacao(id2, "ATENDE"))!;
        var exc = s.Aplicar(new Excluir(id3))!;
        var campo = s.Aplicar(new DefinirCampo(id2, "verificacao", "x"))!;
        var rest = s.Aplicar(new RestaurarCampo(id2, "verificacao"))!;
        var just = s.Aplicar(new DefinirJustificativa(id2, "j"))!;
        var orig = s.Aplicar(new RestaurarItemOriginal(id2))!;
        var res = s.Aplicar(new Restaurar(id3))!;
        var des = s.Desfazer()!;      // desfaz o Restaurar
        var ref_ = s.Refazer()!;

        Assert.Equal("Situação do ITEM 02: FALTA COMPROVAR → ATENDE", DescricaoHistorico.Texto(sit, s.Projeto));
        Assert.Equal("Excluído o ITEM 03", DescricaoHistorico.Texto(exc, s.Projeto));
        Assert.Equal("Verificação técnica do ITEM 02 editada", DescricaoHistorico.Texto(campo, s.Projeto));
        Assert.Equal("Verificação técnica do ITEM 02 restaurada ao original", DescricaoHistorico.Texto(rest, s.Projeto));
        Assert.Equal("Justificativa do ITEM 02 definida", DescricaoHistorico.Texto(just, s.Projeto));
        Assert.Equal("ITEM 02 restaurado ao original", DescricaoHistorico.Texto(orig, s.Projeto));
        Assert.Equal("Restaurado o ITEM 03", DescricaoHistorico.Texto(res, s.Projeto));
        Assert.Equal("Desfeito: Restaurado o ITEM 03", DescricaoHistorico.Texto(des, s.Projeto));
        Assert.Equal("Refeito: Restaurado o ITEM 03", DescricaoHistorico.Texto(ref_, s.Projeto));
        Assert.StartsWith("Importação", DescricaoHistorico.Texto(s.HistoricoCompleto[0], s.Projeto));
    }
}
