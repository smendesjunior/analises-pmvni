using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Pmvni.App;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>«Novo item» (correções 2026-11-16): cria um item de análise depois do selecionado (ou no fim), com os mesmos campos dos
/// outros, numerado na sequência, desfazível, gravado no pacote, exportado e contado no resumo. O item criado não tem original.</summary>
public class NovoItemTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar(string caso)
    {
        var caminho = Ambiente.Corpus(caso + ".json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
    }

    private static SessaoRevisao Sessao(ConteudoPacote c) => new(c.Projeto, c.Historico, Ambiente.Contexto());

    private static string NovoId() => IdItem.Novo(new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero), RandomNumberGenerator.Create());

    private static void Preencher(SessaoRevisao s, string id)
    {
        s.Aplicar(new DefinirCampo(id, "titulo", "Vaga de estacionamento"));
        s.Aplicar(new DefinirCampo(id, "verificacao", "A implantação não cota a vaga exigida."));
        s.Aplicar(new DefinirCampo(id, "providencia", "Cotar a vaga na implantação."));
        s.Aplicar(new DefinirCampo(id, "base_legal", "Código de Obras (LC nº 70/1990), art. 30."));
        s.Aplicar(new DefinirCampo(id, "referencia", "ARQ 01/04, implantação."));
    }

    [Fact]
    public void novo_item_entra_depois_do_selecionado_com_os_mesmos_campos_e_renumera_os_seguintes()
    {
        var s = Sessao(Importar("curto"));
        var antes = ProjetoJson.Escrever(s.Projeto);
        var ids = s.Projeto.Incluidos.Select(i => i.Id).ToList();
        var total = ids.Count;
        var id = NovoId();

        var r = s.Aplicar(new CriarItem(id, ids[0]));

        Assert.NotNull(r);
        Assert.Equal(nameof(CriarItem), r.Tipo);
        var novo = s.Projeto.Item(id)!;
        Assert.True(novo.Criado);
        Assert.True(novo.Incluido);
        Assert.Equal(2, s.Projeto.NumeroAtual(id));                        // logo depois do primeiro
        Assert.Equal(3, s.Projeto.NumeroAtual(ids[1]));                    // o antigo segundo virou o terceiro
        Assert.Equal(total + 1, s.Projeto.Incluidos.Count);
        Assert.Equal(s.Projeto.Itens.Select(i => i.Ordem).Order(), Enumerable.Range(1, total + 1));   // ordens únicas e contíguas
        // Os mesmos campos dos outros itens, todos editáveis, com os textos de nascença; a situação inicial é FALTA COMPROVAR.
        Assert.Equal(new[] { "titulo", "situacao", "verificacao", "providencia", "base_legal", "referencia" }.Order(), novo.Chaves.Order());
        Assert.Equal(s.Projeto.Item(ids[0])!.Chaves, novo.Chaves);
        foreach (var campo in Campos.Editaveis) Assert.Equal(ItemNovo.Textos[campo], DadosEfetivos.TextoEfetivo(s.Projeto, novo, campo));
        Assert.Null(novo.Situacao.Original);
        Assert.Equal("NAO_COMPROVADO", novo.Situacao.Revisada);
        Assert.Equal("Criado o ITEM 02 (item novo)", DescricaoHistorico.Texto(r, s.Projeto));

        // Desfazer tira o item e devolve a numeração; refazer o traz de volta igual.
        s.Desfazer();
        Assert.Equal(antes, ProjetoJson.Escrever(s.Projeto));
        Assert.Null(s.Projeto.Item(id));
        s.Refazer();
        Assert.Equal(2, s.Projeto.NumeroAtual(id));
        Assert.Equal("Criado um item novo", DescricaoHistorico.Texto(r, ProjetoJson.Ler(antes)));
    }

    [Fact]
    public void sem_item_de_referencia_o_novo_item_vai_para_o_fim()
    {
        var s = Sessao(Importar("curto"));
        var total = s.Projeto.Incluidos.Count;
        var id = NovoId();
        s.Aplicar(new CriarItem(id));
        Assert.Equal(total + 1, s.Projeto.NumeroAtual(id));
        Assert.Equal(total + 1, s.Projeto.Item(id)!.NumeroOriginal);
        // Depois de um item excluído também vale (entra na posição dele).
        var primeiro = s.Projeto.Incluidos[0].Id;
        s.Aplicar(new Excluir(primeiro));
        var outro = NovoId();
        s.Aplicar(new CriarItem(outro, primeiro));
        Assert.Equal(1, s.Projeto.NumeroAtual(outro));
    }

    [Fact]
    public void item_novo_se_edita_exclui_e_muda_de_situacao_sem_pedir_justificativa()
    {
        var s = Sessao(Importar("curto"));
        var id = NovoId();
        s.Aplicar(new CriarItem(id));
        Preencher(s, id);
        s.Aplicar(new DefinirSituacao(id, "NAO_ATENDE"));
        var it = s.Projeto.Item(id)!;
        Assert.Equal("NAO_ATENDE", it.Situacao.Revisada);
        Assert.Equal("Vaga de estacionamento", DadosEfetivos.TextoEfetivo(s.Projeto, it, "titulo"));
        // Não há original de que a situação difira: a conferência final não exige justificativa; nada por preencher.
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
        Assert.Empty(ItemNovo.PorPreencher(it));
        // Não há original a restaurar.
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new RestaurarItemOriginal(id)));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new RestaurarCampo(id, "verificacao")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new DefinirCampo(id, "verificacao", " ")));
        // Excluir e restaurar valem como nos outros.
        s.Aplicar(new Excluir(id));
        Assert.Null(s.Projeto.NumeroAtual(id));
        s.Aplicar(new Restaurar(id));
        Assert.NotNull(s.Projeto.NumeroAtual(id));
        // Um texto de outro item pode citar o item novo pelo número: a citação acompanha a numeração.
        var primeiro = s.Projeto.Incluidos[0].Id;
        var numero = s.Projeto.NumeroAtual(id)!.Value;
        s.Aplicar(new DefinirCampo(primeiro, "providencia", $"Ver também o item {numero:00}."));
        s.Aplicar(new Excluir(s.Projeto.Incluidos[1].Id));
        Assert.Equal($"Ver também o item {numero - 1:00}.", DadosEfetivos.TextoEfetivo(s.Projeto, s.Projeto.Item(primeiro)!, "providencia"));
    }

    [Fact]
    public void item_novo_por_preencher_bloqueia_a_exportacao()
    {
        var s = Sessao(Importar("curto"));
        var id = NovoId();
        s.Aplicar(new CriarItem(id));
        var erro = Assert.Single(ConferenciaRevisao.Erros(s.Projeto));
        Assert.Contains("(item novo): falta preencher título, verificação técnica, providência, base legal, referência", erro);
        Assert.Equal(Campos.Editaveis, ItemNovo.PorPreencher(s.Projeto.Item(id)!));
        s.Aplicar(new DefinirCampo(id, "titulo", "Vaga de estacionamento"));
        Assert.DoesNotContain("título", Assert.Single(ConferenciaRevisao.Erros(s.Projeto)));
        Preencher(s, id);
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
        // Excluído, o item por preencher não bloqueia nada.
        var outro = NovoId();
        s.Aplicar(new CriarItem(outro));
        s.Aplicar(new Excluir(outro));
        Assert.Empty(ConferenciaRevisao.Erros(s.Projeto));
        Assert.Empty(ItemNovo.PorPreencher(s.Projeto.Incluidos[0]));   // item recebido nunca está "por preencher"
    }

    [Fact]
    public void item_novo_vai_para_o_pacote_o_dados_json_o_render_e_o_resumo()
    {
        var c = Importar("curto");
        var s = Sessao(c);
        var id = NovoId();
        s.Aplicar(new CriarItem(id, s.Projeto.Incluidos[0].Id));
        Preencher(s, id);
        s.Aplicar(new DefinirSituacao(id, "NAO_ATENDE"));

        // projeto.json: vai e volta; os itens recebidos gravam como antes (sem a chave "criado").
        var bytes = ProjetoJson.Escrever(s.Projeto);
        var relido = ProjetoJson.Ler(bytes);
        Assert.Equal(bytes, ProjetoJson.Escrever(relido));
        Assert.True(relido.Item(id)!.Criado);
        var json = JsonFiel.Ler(bytes)["itens"]!.AsArray();
        Assert.Single(json, i => i!["criado"] is not null);

        // dados.json efetivo: o item novo é o segundo, com as chaves na ordem dos outros.
        var g = DadosEfetivos.Gerar(s.Projeto);
        Assert.Equal(id, g.IdsDosItens[1]);
        var itens = JsonFiel.Ler(g.Json)["itens"]!.AsArray();
        var novo = itens[1]!.AsObject();
        Assert.Equal(itens[0]!.AsObject().Select(p => p.Key), novo.Select(p => p.Key));
        Assert.Equal("Vaga de estacionamento", novo["titulo"]!.GetValue<string>());
        Assert.Equal("NAO_ATENDE", novo["situacao"]!.GetValue<string>());

        // O kit renderiza (o PDF sai dessas folhas) e o resumo conta o item novo.
        using var pasta = Ambiente.PastaDeTeste("novo-item");
        c.ExtrairAssets(pasta);
        var antes = RelatorioRenderer.Renderizar(DadosEfetivos.Gerar(Importar("curto").Projeto).Json, Path.Combine(pasta, "dados.json"), new OpcoesRender(true, false), Modelo);
        var render = RelatorioRenderer.Renderizar(g.Json, Path.Combine(pasta, "dados.json"), new OpcoesRender(true, false), Modelo);
        Assert.True(render.Ok, string.Join("; ", render.Erros));
        Assert.Contains("Vaga de estacionamento", render.Html);
        Assert.Equal(antes.Refs.Count(x => x.Seletor == ".card") + 1, render.Refs.Count(x => x.Seletor == ".card"));
        var resumoAntes = Pmvni.Composicao.Kit.Conferencia.Resumo(Lista(JsonFiel.Ler(DadosEfetivos.Gerar(Importar("curto").Projeto).Json)));
        var resumo = Pmvni.Composicao.Kit.Conferencia.Resumo(Lista(JsonFiel.Ler(g.Json)));
        Assert.NotEqual(resumoAntes, resumo);

        // Comparar com o original: o item aparece como criado na revisão.
        var d = Comparacao.Diferencas(s.Projeto, s.HistoricoCompleto).Single(x => x.Item == id);
        Assert.True(d.Criado);
        Assert.Equal("ITEM 02 (criado na revisão)", Comparacao.Rotulo(d));
        // O diário reaplica a criação sobre o projeto reaberto.
        var s2 = new SessaoRevisao(ProjetoJson.Ler(ProjetoJson.Escrever(Importar("curto").Projeto)), c.Historico, Ambiente.Contexto());
        Assert.Throws<InvalidOperationException>(() => s2.Reaplicar(s.Pendentes.Skip(1)));   // sem a criação, o resto não bate: tudo ou nada
    }

    private static IEnumerable<object?> Lista(JsonObject dados) =>
        dados["itens"]!.AsArray().Select(i => (object?)i!.AsObject().ToDictionary(p => p.Key, p => (object?)(p.Value is JsonValue v && v.TryGetValue<string>(out var s) ? s : null)));

    [Fact]
    public void criacao_invalida_e_recusada()
    {
        var s = Sessao(Importar("curto"));
        var existente = s.Projeto.Incluidos[0].Id;
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new CriarItem("x")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new CriarItem(existente)));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new CriarItem(NovoId(), "it_nao_existe")));
        Assert.Throws<ComandoRecusadoException>(() => s.Aplicar(new CriarItem(NovoId(), null, "TALVEZ")));
        Assert.False(s.PodeDesfazer);
    }

    [Fact]
    public void novo_item_esta_na_faixa_com_dica_e_vale_so_no_relatorio()
    {
        var inicio = Faixas.Para(Modulo.Relatorios).Abas.Single(a => a.Nome == "Página Inicial");
        var c = inicio.Comandos.Single(x => x.Id == ComandosApp.InicioNovoItem);
        Assert.Equal("Novo item", c.Rotulo);
        Assert.Equal(Glifos.NovoItem, c.Glifo);
        Assert.Equal(TipoComando.Botao, c.Tipo);
        Assert.True(ComandosApp.SoDoRelatorio(ComandosApp.InicioNovoItem));
        Assert.Contains("mesmos campos", DicasComandos.Descricao(ComandosApp.InicioNovoItem));
    }
}
