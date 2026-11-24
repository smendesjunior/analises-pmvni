using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Pmvni.Composicao;
using Xunit;

namespace Pmvni.Revisao.Testes;

public class ImportacaoTestes
{
    private static readonly ModeloCarregado Modelo = ModeloOficial.Carregar();

    private static ConteudoPacote Importar(string caso, ContextoUsuario? u = null)
    {
        var caminho = Ambiente.Corpus(caso + ".json");
        return Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, u ?? Ambiente.Contexto());
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    [Fact]
    public void RelatorioRecebidoSemPontosNaoPodeSerEntregueNemGerarDespacho()
    {
        var caminho = Ambiente.Corpus("curto.json");
        var dados = JsonFiel.Ler(File.ReadAllBytes(caminho));
        dados["capa"]!.AsObject().Remove("pontos_confirmar");
        var bytes = Encoding.UTF8.GetBytes(dados.ToJsonString());
        var problema = Assert.Throws<ImportacaoRecusadaException>(() => Importador.Importar(bytes, caminho, Modelo, Ambiente.Contexto(), entrega: true));
        Assert.Contains("capa.pontos_confirmar", problema.Message);
        Assert.Equal(Sha(bytes), problema.Sha256);
        var revisavel = Importador.Importar(bytes, caminho, Modelo, Ambiente.Contexto());
        Assert.False(revisavel.Projeto.Documento["capa"]!.AsObject().ContainsKey("pontos_confirmar"));
        var pacote = new PacoteAberto(revisavel, new EstadoDisco(bytes.Length, DateTime.UtcNow, Sha(bytes)), false, null);
        var processo = new Processo(Path.GetDirectoryName(caminho)!, "Fictício", 9001, 2026, null);
        Assert.Contains(ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, pacote, processo), e => e.Contains("capa.pontos_confirmar"));
    }

    [Fact]
    public void importar_corpus_curto_cria_itens_com_ids_e_original_intacto()
    {
        var bytes = File.ReadAllBytes(Ambiente.Corpus("curto.json"));
        var origem = JsonFiel.Ler(bytes);
        var c = Importar("curto");

        Assert.Equal(bytes, c.Recebido);
        Assert.Equal(3, c.Projeto.Itens.Count);
        Assert.Equal(Manifesto.VersaoEsquemaAtual, c.Projeto.VersaoEsquema);
        Assert.Equal(new[] { 1, 2, 3 }, c.Projeto.Itens.Select(i => i.Ordem));
        Assert.Equal(new[] { 1, 2, 3 }, c.Projeto.Itens.Select(i => i.NumeroOriginal));
        Assert.All(c.Projeto.Itens, i => { Assert.True(i.Incluido); Assert.True(IdItem.Valido(i.Id)); });
        Assert.Equal(3, c.Projeto.Itens.Select(i => i.Id).Distinct().Count());

        var primeiro = origem["itens"]![0]!.AsObject();
        var item = c.Projeto.Itens[0];
        Assert.Equal(primeiro["titulo"]!.GetValue<string>(), item.Campos["titulo"].Original!.GetValue<string>());
        Assert.Null(item.Campos["titulo"].Revisado);
        Assert.False(item.Campos["titulo"].Ausente);
        Assert.Equal(primeiro["situacao"]!.GetValue<string>(), item.Situacao.Original!.GetValue<string>());
        Assert.Null(item.Situacao.Revisada);
        Assert.Null(item.Situacao.Justificativa);
        Assert.Equal(primeiro.Select(p => p.Key), item.Chaves);
        Assert.Empty(item.Outros);

        Assert.Equal(new[] { 1, 2, 3 }, c.Projeto.Incluidos.Select(i => c.Projeto.NumeroAtual(i.Id)!.Value));
        Assert.Empty(c.Assets);
    }

    [Fact]
    public void documento_guarda_o_resto_como_veio_e_itens_vazio_no_lugar()
    {
        var origem = JsonFiel.Ler(File.ReadAllBytes(Ambiente.Corpus("curto.json")));
        var c = Importar("curto");
        Assert.Equal(origem.Select(p => p.Key), c.Projeto.Documento.Select(p => p.Key));
        Assert.Empty(c.Projeto.Documento["itens"]!.AsArray());
        Assert.Equal(origem["capa"]!.ToJsonString(), c.Projeto.Documento["capa"]!.ToJsonString());
        Assert.Equal(origem["config"]!.ToJsonString(), c.Projeto.Documento["config"]!.ToJsonString());
    }

    [Fact]
    public void importar_extremos_preserva_chaves_e_campos_extras()
    {
        var origem = JsonFiel.Ler(File.ReadAllBytes(Ambiente.Corpus("extremos.json")));
        var c = Importar("extremos");
        var item = c.Projeto.Itens[0];
        Assert.Equal("id", item.Chaves[^1]);
        Assert.Contains("situacao", item.Chaves);
        Assert.Equal(origem["itens"]![0]!["id"]!.GetValue<string>(), item.Outros["id"]!.GetValue<string>());
        Assert.False(item.Outros.ContainsKey("titulo"));
        Assert.False(item.Outros.ContainsKey("situacao"));
        Assert.NotEqual(item.Outros["id"]!.GetValue<string>(), item.Id); // o id do item é novo; o do plugin fica em outros
    }

    [Fact]
    public void importar_longo_copia_imagens_para_assets_e_reescreve_caminhos()
    {
        var c = Importar("longo");
        Assert.Equal(60, c.Projeto.Itens.Count);
        Assert.Equal(9, c.Assets.Count); // mapa + 8 figuras (bytes distintos)

        var figuras = c.Projeto.Documento["figuras"]!.AsArray();
        Assert.Equal(8, figuras.Count);
        var imagem = figuras[0]!["imagem"]!.GetValue<string>();
        Assert.Matches("^assets/[0-9a-f]{64}\\.png$", imagem);
        Assert.Equal(Sha(File.ReadAllBytes(Ambiente.Corpus("img/fig1.png"))), imagem["assets/".Length..^".png".Length]);

        var mapa = c.Projeto.Documento["localizacao"]!["imagem"]!.GetValue<string>();
        Assert.Equal(Sha(File.ReadAllBytes(Ambiente.Corpus("img/mapa.png"))), mapa["assets/".Length..^".png".Length]);

        foreach (var (chave, bytes) in c.Assets)
        {
            Assert.Matches("^assets/[0-9a-f]{64}\\.(png|jpg|webp|gif)$", chave);
            Assert.Equal(Sha(bytes), chave["assets/".Length..chave.LastIndexOf('.')]);
        }
        Assert.Contains(mapa, c.Assets.Keys);
        Assert.All(figuras, f => Assert.Contains(f!["imagem"]!.GetValue<string>(), c.Assets.Keys));
    }

    [Fact]
    public void extrair_assets_grava_cada_imagem_no_caminho_do_documento()
    {
        var c = Importar("longo");
        using var pasta = Ambiente.PastaDeTeste("assets");
        c.ExtrairAssets(pasta);
        foreach (var (chave, bytes) in c.Assets)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(pasta, chave.Replace('/', Path.DirectorySeparatorChar))));
        var mapa = c.Projeto.Documento["localizacao"]!["imagem"]!.GetValue<string>();
        Assert.True(File.Exists(Path.Combine(pasta, mapa)));
    }

    [Fact]
    public void imagem_repetida_vira_um_unico_asset()
    {
        using var pasta = Ambiente.PastaDeTeste("dup");
        CopiarCorpus(pasta);
        var dados = JsonFiel.Ler(File.ReadAllBytes(Path.Combine(pasta, "extremos.json")));
        dados["localizacao"]!["imagem"] = dados["figuras"]![0]!["imagem"]!.GetValue<string>(); // mesma imagem nos dois lugares
        var caminho = Path.Combine(pasta, "dup.json");
        File.WriteAllBytes(caminho, JsonFiel.Escrever(dados));

        var c = Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
        Assert.Single(c.Assets);
        Assert.Equal(c.Projeto.Documento["localizacao"]!["imagem"]!.GetValue<string>(), c.Projeto.Documento["figuras"]![0]!["imagem"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("absoluto")]
    [InlineData(@"\\host-que-nao-existe.invalid\share\x.png")]
    [InlineData("//host-que-nao-existe.invalid/share/x.png")]
    public void imagem_absoluta_ou_de_rede_recusa_importacao(string imagem)
    {
        using var pasta = Ambiente.PastaDeTeste("imagem-fora");
        CopiarCorpus(pasta);
        var dados = JsonFiel.Ler(File.ReadAllBytes(Path.Combine(pasta, "extremos.json")));
        // Um arquivo que existe de verdade, por caminho absoluto: nunca é lido.
        if (imagem == "absoluto") imagem = Path.GetFullPath(Path.Combine(pasta, dados["localizacao"]!["imagem"]!.GetValue<string>()));
        dados["localizacao"]!["imagem"] = imagem;
        var caminho = Path.Combine(pasta, "fora.json");
        File.WriteAllBytes(caminho, JsonFiel.Escrever(dados));

        var ex = Assert.Throws<ImportacaoRecusadaException>(() => Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto()));
        Assert.Contains(ex.Erros, e => e.StartsWith("Localização: mapa não encontrado", StringComparison.Ordinal));
    }

    [Fact]
    public void imagem_em_pasta_irma_como_no_kit_continua_aceita_na_importacao()
    {
        // A análise do kit cita o mapa de uma pasta irmã (../REANALISE_…/renders/mapa.png): o .. relativo é do kit e continua aceito.
        using var pasta = Ambiente.PastaDeTeste("imagem-irma");
        var sub = Path.Combine(pasta, "analise");
        Directory.CreateDirectory(sub);
        CopiarCorpus(sub);
        var dados = JsonFiel.Ler(File.ReadAllBytes(Path.Combine(sub, "extremos.json")));
        var mapa = dados["localizacao"]!["imagem"]!.GetValue<string>();
        Directory.CreateDirectory(Path.Combine(pasta, "renders"));
        File.Copy(Path.Combine(sub, mapa), Path.Combine(pasta, "renders", "mapa.png"));
        dados["localizacao"]!["imagem"] = "../renders/mapa.png";
        var caminho = Path.Combine(sub, "irma.json");
        File.WriteAllBytes(caminho, JsonFiel.Escrever(dados));

        var c = Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());
        Assert.StartsWith("assets/", c.Projeto.Documento["localizacao"]!["imagem"]!.GetValue<string>(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("../fora.png", false)]
    [InlineData("img/../../fora.png", false)]
    [InlineData("assets/x.png", true)]
    public void renderizador_da_pasta_de_trabalho_nao_le_fora_dela(string imagem, bool dentro)
    {
        // Pasta de trabalho de um pacote (ImagensSoNaPasta): nada com .. nem fora dela, mesmo que o arquivo exista.
        using var pasta = Ambiente.PastaDeTeste("render-estrito");
        var trabalho = Path.Combine(pasta, "trabalho");
        Directory.CreateDirectory(trabalho);
        CopiarCorpus(trabalho);
        var dados = JsonFiel.Ler(File.ReadAllBytes(Path.Combine(trabalho, "extremos.json")));
        var mapa = Path.Combine(trabalho, dados["localizacao"]!["imagem"]!.GetValue<string>());
        File.Copy(mapa, Path.Combine(pasta, "fora.png"));
        Directory.CreateDirectory(Path.Combine(trabalho, "assets"));
        File.Copy(mapa, Path.Combine(trabalho, "assets", "x.png"));
        dados["localizacao"]!["imagem"] = imagem;
        var bytes = JsonFiel.Escrever(dados);
        var caminho = Path.Combine(trabalho, "dados.json");

        var estrito = RelatorioRenderer.Renderizar(bytes, caminho, new OpcoesRender(false, true, ImagensSoNaPasta: true), Modelo);
        Assert.Equal(dentro, estrito.Ok);
        if (!dentro) Assert.Contains(estrito.Erros, e => e.StartsWith("Localização: mapa não encontrado", StringComparison.Ordinal));
        // Fora do modo estrito (dados.json do kit) o mesmo caminho relativo é aceito, como no gerador do kit.
        Assert.True(RelatorioRenderer.Renderizar(bytes, caminho, new OpcoesRender(false, true), Modelo).Ok);
    }

    [Fact]
    public void imagem_com_assinatura_desconhecida_recusa_importacao()
    {
        using var pasta = Ambiente.PastaDeTeste("assinatura");
        CopiarCorpus(pasta);
        File.WriteAllBytes(Path.Combine(pasta, "img", "mapa_x.png"), Encoding.UTF8.GetBytes("isto nao e uma imagem"));
        var caminho = Path.Combine(pasta, "extremos.json");

        var ex = Assert.Throws<ImportacaoRecusadaException>(() =>
            Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto()));
        Assert.Contains(ex.Erros, e => e.Contains("mapa_x.png", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("89504E470D0A1A0A00", "png")]
    [InlineData("FFD8FFE000", "jpg")]
    [InlineData("474946383761", "gif")]
    [InlineData("474946383961", "gif")]
    [InlineData("52494646AABBCCDD57454250", "webp")]
    public void extensao_vem_da_assinatura_dos_bytes(string hex, string esperada)
    {
        Assert.Equal(esperada, Importador.ExtensaoPelaAssinatura(Convert.FromHexString(hex)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("89504E47")]
    [InlineData("52494646AABBCCDD41564920")] // RIFF....AVI
    [InlineData("474946383861")]             // GIF88a
    [InlineData("FFD8")]
    public void assinatura_fora_da_lista_nao_tem_extensao(string hex)
    {
        Assert.Null(Importador.ExtensaoPelaAssinatura(Convert.FromHexString(hex)));
    }

    [Fact]
    public void dados_com_erro_de_conferencia_nao_importam()
    {
        var dados = JsonFiel.Ler(File.ReadAllBytes(Ambiente.Corpus("curto.json")));
        dados.Remove("capa");
        var ex = Assert.Throws<ImportacaoRecusadaException>(() =>
            Importador.Importar(JsonFiel.Escrever(dados), Ambiente.Corpus("curto.json"), Modelo, Ambiente.Contexto()));
        Assert.Contains("Campo obrigatório ausente: capa", ex.Erros);
    }

    [Fact]
    public void arquivo_que_nao_e_json_nao_importa()
    {
        var ex = Assert.Throws<ImportacaoRecusadaException>(() =>
            Importador.Importar(Encoding.UTF8.GetBytes("não é json"), Ambiente.Corpus("curto.json"), Modelo, Ambiente.Contexto()));
        Assert.NotEmpty(ex.Erros);
    }

    /// <summary>O curto com citações plantadas: item 1 cita o 2 e o 3 (e um "item 3 do art. 5" que fica literal); a conclusão cita o 1.</summary>
    private static ConteudoPacote ImportarComCitacoes(ContextoUsuario? u = null)
    {
        var dados = JsonFiel.Ler(File.ReadAllBytes(Ambiente.Corpus("curto.json")));
        var item1 = dados["itens"]![0]!.AsObject();
        item1["verificacao"] = "Conferir o item 02, ITEM 3 e o item 3 do art. 5.";
        dados["itens"]![2]!["titulo"] = "Igual ao Item 1";
        dados["conclusao"] = "Ver item 01 e itens 02 a 03.";
        return Importador.Importar(JsonFiel.Escrever(dados), Ambiente.Corpus("curto.json"), Modelo, u ?? Ambiente.Contexto());
    }

    [Fact]
    public void historico_comeca_com_importar_citacoes()
    {
        var u = Ambiente.Contexto("maria");
        var c = ImportarComCitacoes(u);
        var h = Assert.Single(c.Historico);
        Assert.Equal(1, h.N);
        Assert.Equal("ImportarCitacoes", h.Tipo);
        Assert.Equal("maria", h.Usuario);
        Assert.Equal(u.Relogio(), h.DataUtc);
        Assert.Null(h.Item);
        Assert.Null(h.Campo);
        Assert.Null(h.Antes);
        Assert.Null(h.Ref);

        var depois = Assert.IsType<JsonArray>(h.Depois).Select(x => x!.AsObject()).ToList();
        var ids = c.Projeto.Itens.Select(i => i.Id).ToList();
        Assert.Equal(4, depois.Count);
        Assert.Equal(("verificacao", "item 02", ids[1]), (depois[0]["campo"]!.GetValue<string>(), depois[0]["trecho"]!.GetValue<string>(), depois[0]["alvo"]!.GetValue<string>()));
        Assert.Equal(ids[0], depois[0]["item"]!.GetValue<string>());
        Assert.Equal(("ITEM 3", ids[2]), (depois[1]["trecho"]!.GetValue<string>(), depois[1]["alvo"]!.GetValue<string>()));
        Assert.Equal(("titulo", ids[2], "Item 1", ids[0]),
            (depois[2]["campo"]!.GetValue<string>(), depois[2]["item"]!.GetValue<string>(), depois[2]["trecho"]!.GetValue<string>(), depois[2]["alvo"]!.GetValue<string>()));
        Assert.Equal(("conclusao", "item 01", ids[0]), (depois[3]["campo"]!.GetValue<string>(), depois[3]["trecho"]!.GetValue<string>(), depois[3]["alvo"]!.GetValue<string>()));
        Assert.Null(depois[3]["item"]);
    }

    [Fact]
    public void citacoes_viram_token_nos_campos_e_na_conclusao_e_o_resto_fica_literal()
    {
        var c = ImportarComCitacoes();
        var ids = c.Projeto.Itens.Select(i => i.Id).ToList();
        Assert.Equal($"Conferir o item {{{{ref:{ids[1]}:2}}}}, ITEM {{{{ref:{ids[2]}:1}}}} e o item 3 do art. 5.",
            c.Projeto.Itens[0].Campos["verificacao"].Original!.GetValue<string>());
        Assert.Equal($"Igual ao Item {{{{ref:{ids[0]}:1}}}}", c.Projeto.Itens[2].Campos["titulo"].Original!.GetValue<string>());
        Assert.Equal($"Ver item {{{{ref:{ids[0]}:2}}}} e itens 02 a 03.", c.Projeto.Documento["conclusao"]!.GetValue<string>());
        // o que foi recebido não muda
        Assert.Contains("Conferir o item 02, ITEM 3", Encoding.UTF8.GetString(c.Recebido));
    }

    [Fact]
    public void historico_existe_mesmo_sem_citacoes()
    {
        var c = Importar("sem_opcionais");
        var h = Assert.Single(c.Historico);
        Assert.Equal("ImportarCitacoes", h.Tipo);
        Assert.Empty(Assert.IsType<JsonArray>(h.Depois));
    }

    [Theory]
    [InlineData("curto")]
    [InlineData("extremos")]
    [InlineData("longo")]
    [InlineData("sem_opcionais")]
    [InlineData("estouro")]
    public void imprimir_a_numeracao_original_devolve_os_textos_originais(string caso)
    {
        var origem = JsonFiel.Ler(File.ReadAllBytes(Ambiente.Corpus(caso + ".json")));
        var c = Importar(caso);
        var porId = c.Projeto.Itens.ToDictionary(i => i.Id);
        string Imp(string texto) => Citacoes.Imprimir(texto, c.Projeto.NumeroAtual, id => porId[id].NumeroOriginal);

        var itens = origem["itens"]!.AsArray();
        for (var i = 0; i < itens.Count; i++)
            foreach (var campo in Campos.Editaveis)
                Assert.Equal(itens[i]![campo]!.GetValue<string>(), Imp(c.Projeto.Itens[i].Campos[campo].Original!.GetValue<string>()));

        Assert.Equal(origem["conclusao"]!.GetValue<string>(), Imp(c.Projeto.Documento["conclusao"]!.GetValue<string>()));
    }

    [Fact]
    public void ids_dos_itens_sao_unicos_mesmo_com_sorteio_repetido()
    {
        // Sorteio fixo: o importador não pode deixar dois itens com o mesmo id.
        var u = new ContextoUsuario("t", () => new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero), new SorteioQueRepete());
        var c = Importar("curto", u);
        Assert.Equal(3, c.Projeto.Itens.Select(i => i.Id).Distinct().Count());
    }

    private sealed class SorteioQueRepete : RandomNumberGenerator
    {
        private int _n;
        public override void GetBytes(byte[] data) => GetBytes(data.AsSpan());
        public override void GetBytes(Span<byte> data) { data.Clear(); if (++_n > 3 && data.Length > 0) data[0] = (byte)_n; }
    }

    [Theory]
    [InlineData("curto")]
    [InlineData("extremos")]
    [InlineData("longo")]
    [InlineData("sem_opcionais")]
    [InlineData("estouro")]
    public void projeto_json_ida_e_volta(string caso)
    {
        var p = Importar(caso).Projeto;
        var a = ProjetoJson.Escrever(p);
        var b = ProjetoJson.Escrever(ProjetoJson.Ler(a));
        Assert.Equal(a, b);
    }

    [Fact]
    public void projeto_json_guarda_a_forma_do_plano()
    {
        var p = Importar("extremos").Projeto;
        p.Itens[0].Situacao.Revisada = "ATENDE";
        p.Itens[0].Situacao.Justificativa = "porque sim";
        p.Itens[0].Campos["titulo"].Revisado = "novo";
        p.Itens[0].Campos["referencia"] = new CampoTexto { Ausente = true, Revisado = "criado" };
        var j = JsonFiel.Ler(ProjetoJson.Escrever(p));

        Assert.Equal("1.0.0", j["versao_esquema"]!.GetValue<string>());
        var it = j["itens"]![0]!.AsObject();
        Assert.Equal(p.Itens[0].Id, it["id"]!.GetValue<string>());
        Assert.Equal(1, it["ordem"]!.GetValue<int>());
        Assert.Equal(1, it["numero_original"]!.GetValue<int>());
        Assert.True(it["incluido"]!.GetValue<bool>());
        Assert.Equal("ATENDE", it["situacao"]!["revisada"]!.GetValue<string>());
        Assert.Equal("porque sim", it["situacao"]!["justificativa"]!.GetValue<string>());
        Assert.Equal("novo", it["campos"]!["titulo"]!["revisado"]!.GetValue<string>());
        Assert.NotNull(it["campos"]!["titulo"]!["original"]);
        Assert.False(it["campos"]!["referencia"]!.AsObject().ContainsKey("original")); // sem "original" = ausente
        Assert.Equal("criado", it["campos"]!["referencia"]!["revisado"]!.GetValue<string>());
        Assert.NotNull(it["outros"]!["id"]);
        Assert.NotNull(j["documento"]!["conclusao"]);

        var volta = ProjetoJson.Ler(ProjetoJson.Escrever(p));
        Assert.True(volta.Itens[0].Campos["referencia"].Ausente);
        Assert.Equal("criado", volta.Itens[0].Campos["referencia"].Revisado);
        Assert.Equal("ATENDE", volta.Itens[0].Situacao.Revisada);
    }

    [Fact]
    public void clonar_nao_compartilha_estado()
    {
        var p = Importar("curto").Projeto;
        var c = p.Clonar();
        c.Itens[0].Campos["titulo"].Revisado = "x";
        c.Itens[0].Incluido = false;
        c.Documento["conclusao"] = "outra";
        Assert.Null(p.Itens[0].Campos["titulo"].Revisado);
        Assert.True(p.Itens[0].Incluido);
        Assert.NotEqual("outra", p.Documento["conclusao"]!.GetValue<string>());
        Assert.Equal(ProjetoJson.Escrever(p), ProjetoJson.Escrever(p.Clonar()));
    }

    [Fact]
    public void numero_atual_conta_so_os_incluidos_na_ordem()
    {
        var p = Importar("curto").Projeto;
        p.Itens[0].Incluido = false;
        Assert.Null(p.NumeroAtual(p.Itens[0].Id));
        Assert.Equal(1, p.NumeroAtual(p.Itens[1].Id));
        Assert.Equal(2, p.NumeroAtual(p.Itens[2].Id));
        Assert.Equal(2, p.Incluidos.Count);
        Assert.Null(p.NumeroAtual("inexistente"));
        Assert.Same(p.Itens[2], p.Item(p.Itens[2].Id));
        Assert.Null(p.Item("inexistente"));
    }

    [Fact]
    public void manifesto_ida_e_volta()
    {
        var c = Importar("longo");
        var m = c.Manifesto;
        Assert.Equal(Manifesto.VersaoEsquemaAtual, m.VersaoEsquema);
        Assert.Equal(14, m.TemplateRevisao);
        Assert.Equal(Modelo.Sha256["relatorio.css"], m.Sha256Css);
        Assert.Equal(Ambiente.Contexto().Relogio(), m.CriadoEm);
        Assert.False(string.IsNullOrWhiteSpace(m.AppVersao));
        Assert.Equal(Sha(c.Recebido), m.Partes["original/recebido.json"]);
        Assert.Equal(Sha(ProjetoJson.Escrever(c.Projeto)), m.Partes["projeto.json"]);
        Assert.Equal(Sha(HistoricoJsonl.Escrever(c.Historico)), m.Partes["historico.jsonl"]);
        Assert.All(c.Assets, a => Assert.Equal(Sha(a.Value), m.Partes[a.Key]));

        var volta = Manifesto.Ler(m.Escrever());
        Assert.Equal(m.VersaoEsquema, volta.VersaoEsquema);
        Assert.Equal(m.AppVersao, volta.AppVersao);
        Assert.Equal(m.TemplateRevisao, volta.TemplateRevisao);
        Assert.Equal(m.Sha256Css, volta.Sha256Css);
        Assert.Equal(m.CriadoEm, volta.CriadoEm);
        Assert.Equal(m.Partes, volta.Partes);
        Assert.Equal(m.Escrever(), volta.Escrever());
    }

    [Fact]
    public void historico_jsonl_ida_e_volta_com_a_forma_do_plano()
    {
        var antes = JsonValue.Create("a");
        var r1 = new RegistroHistorico(1, new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero).AddTicks(1234567), "ana", "DefinirCampo", "it_X", "titulo", antes, JsonValue.Create("b \"c\" é"), null);
        var r2 = new RegistroHistorico(2, new DateTimeOffset(2026, 11, 14, 12, 1, 0, TimeSpan.Zero), "ana", "desfeito", null, null, null, null, 1);
        var bytes = HistoricoJsonl.Escrever(new[] { r1, r2 });

        var linhas = Encoding.UTF8.GetString(bytes).Split('\n');
        Assert.Equal(3, linhas.Length); // duas linhas e o \n final
        Assert.Equal("", linhas[2]);
        var j = JsonFiel.Ler(Encoding.UTF8.GetBytes(linhas[0]));
        Assert.Equal(new[] { "n", "data_utc", "usuario", "tipo", "alvo", "antes", "depois", "ref" }, j.Select(p => p.Key));
        Assert.Equal("it_X", j["alvo"]!["item"]!.GetValue<string>());
        Assert.Equal("titulo", j["alvo"]!["campo"]!.GetValue<string>());
        Assert.Equal(linhas[0], HistoricoJsonl.Linha(r1));

        var volta = HistoricoJsonl.Ler(bytes);
        Assert.Equal(2, volta.Count);
        Assert.Equal(r1.DataUtc, volta[0].DataUtc);
        Assert.Equal("b \"c\" é", volta[0].Depois!.GetValue<string>());
        Assert.Equal("a", volta[0].Antes!.GetValue<string>());
        Assert.Null(volta[0].Ref);
        Assert.Equal(1, volta[1].Ref);
        Assert.Null(volta[1].Item);
        Assert.Null(volta[1].Antes);
        Assert.Equal(bytes, HistoricoJsonl.Escrever(volta));
    }

    [Fact]
    public void contexto_atual_usa_o_usuario_do_windows_sem_dominio()
    {
        var u = ContextoUsuario.Atual();
        Assert.False(string.IsNullOrWhiteSpace(u.Usuario));
        Assert.DoesNotContain('\\', u.Usuario);
        Assert.True(Math.Abs((u.Relogio() - DateTimeOffset.UtcNow).TotalSeconds) < 5);
        Assert.NotNull(u.Rng);
    }

    [Fact]
    public void texto_com_digito_nao_ascii_importa_sem_excecao_e_fica_literal()
    {
        using var pasta = Ambiente.PastaDeTeste("digito-largo");
        var dados = JsonFiel.Ler(File.ReadAllBytes(Ambiente.Corpus("curto.json")));
        dados["itens"]![0]!["verificacao"] = "ver item ３ e item 02";
        var caminho = Path.Combine(pasta, "largo.json");
        File.WriteAllBytes(caminho, JsonFiel.Escrever(dados));

        var c = Importador.Importar(File.ReadAllBytes(caminho), caminho, Modelo, Ambiente.Contexto());

        Assert.Equal("ver item ３ e item {{ref:" + c.Projeto.Itens[1].Id + ":2}}",
            c.Projeto.Itens[0].Campos["verificacao"].Original!.GetValue<string>());
    }

    /// <summary>Copia o corpus (extremos.json + img/) para a pasta, para testes que alteram imagens ou dados.</summary>
    private static void CopiarCorpus(string pasta)
    {
        File.Copy(Ambiente.Corpus("extremos.json"), Path.Combine(pasta, "extremos.json"));
        Directory.CreateDirectory(Path.Combine(pasta, "img"));
        foreach (var f in Directory.GetFiles(Ambiente.Corpus("img")))
            File.Copy(f, Path.Combine(pasta, "img", Path.GetFileName(f)));
    }
}
