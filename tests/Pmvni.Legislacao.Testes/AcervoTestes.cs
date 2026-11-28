using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Pmvni.Legislacao.Testes.Ficticios;
using Xunit;

namespace Pmvni.Legislacao.Testes;

public sealed class AcervoTestes : IDisposable
{
    readonly Cenario c = new("acervo");

    public void Dispose() => c.Dispose();

    static string Sha(string caminho) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(caminho)));

    string Catalogo => Path.Combine(c.Dados, "catalogo.json");
    string Bak => Path.Combine(c.Dados, "catalogo.json.bak");

    [Fact]
    public void IndiceDoKitAcompanhaInclusaoSubstituicaoERecuperacao()
    {
        var kit = Path.Combine(c.Dados, "kit");
        var inventario = Path.Combine(kit, "00 - INDICE DO KIT", "indice_do_kit.json");
        Directory.CreateDirectory(Path.GetDirectoryName(inventario)!);
        File.WriteAllText(inventario, """{"arquivos":[]}""");
        var lei = c.Anexar(Cenario.Lei);
        void Conferir(Anexo a)
        {
            NormaDoKit.Sincronizar(kit, c.Acervo, a);
            var pdf = Directory.GetFiles(kit, "*.pdf", SearchOption.AllDirectories).Single();
            var indice = JsonNode.Parse(File.ReadAllText(Path.ChangeExtension(pdf, ".json")))!;
            Assert.Equal(a.Sha256, Sha(pdf));
            Assert.Equal(a.Sha256, indice["sha256"]?.ToString());
            Assert.Equal(a.Paginas, indice["paginas"]!.AsArray().Count);
            Assert.Equal(2, JsonNode.Parse(File.ReadAllText(inventario))!["arquivos"]!.AsArray().Count);
        }
        Conferir(lei);
        var entrada = c.Acervo.Preparar(c.Original(Cenario.Lei2), lei.Id);
        var nova = c.Acervo.ConfirmarSubstituicao(lei.Id, entrada, c.Extrair(entrada.Caminho));
        Conferir(nova);
        File.Delete(Path.ChangeExtension(Directory.GetFiles(kit, "*.pdf", SearchOption.AllDirectories).Single(), ".json"));
        Conferir(nova);
    }

    [Fact]
    public void anexar_dois_lista_cada_um_individualmente()
    {
        var antesLei = Cenario.Marca.De(c.Original(Cenario.Lei));
        var antesNt = Cenario.Marca.De(c.Original(Cenario.Nt));
        File.SetAttributes(c.Original(Cenario.Nt), FileAttributes.ReadOnly | FileAttributes.Archive);
        antesNt = Cenario.Marca.De(c.Original(Cenario.Nt));
        Assert.True(antesNt.Atributos.HasFlag(FileAttributes.ReadOnly));

        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);

        var lista = c.Acervo.Listar();
        Assert.Equal([nt.Id, lei.Id], lista.Select(a => a.Id)); // Estadual antes de Municipal
        var l = lista[1];
        Assert.Equal("LEI 9.999-2099 - FICTICIA", l.Titulo);
        Assert.Equal(Esfera.Municipal, l.Esfera);
        Assert.Equal("LEI 9.999-2099 - FICTICIA.pdf", l.ArquivoOriginal);
        Assert.Equal(new FileInfo(c.Original(Cenario.Lei)).Length, l.Tamanho);
        Assert.Equal(3, l.Paginas);
        Assert.Empty(l.PaginasSemTexto);
        Assert.True(l.TemTexto);
        Assert.Equal(Cenario.Agora, l.AnexadoEm);
        Assert.Null(l.SubstituidoEm);
        Assert.Matches("^lg_[0-9a-f]{32}$", l.Id);
        Assert.Equal(Esfera.Estadual, lista[0].Esfera);
        Assert.Equal(2, lista[0].Paginas);
        Assert.Equal(Sha(c.Original(Cenario.Lei)), l.Sha256);

        Assert.True(File.ReadAllBytes(Path.Combine(c.Dados, l.Sha256 + ".pdf")).AsSpan().SequenceEqual(File.ReadAllBytes(c.Original(Cenario.Lei))));
        Assert.True(File.Exists(Path.Combine(c.Dados, l.Sha256 + ".texto.json.gz")));
        Assert.True(File.Exists(Path.Combine(c.Dados, nt.Sha256 + ".texto.json.gz")));
        Assert.True(File.Exists(Catalogo));
        Assert.Empty(Directory.GetFileSystemEntries(c.PastaEntrada));
        Assert.True((new DirectoryInfo(c.PastaEntrada).Attributes & FileAttributes.Hidden) != 0);

        Assert.Equal(antesLei, Cenario.Marca.De(c.Original(Cenario.Lei)));
        Assert.Equal(antesNt, Cenario.Marca.De(c.Original(Cenario.Nt)));
        // O catálogo guarda só o nome do arquivo, nunca o caminho.
        var json = File.ReadAllText(Catalogo);
        Assert.DoesNotContain(c.Originais, json);
        Assert.Contains("\"arquivo_original\": \"LEI 9.999-2099 - FICTICIA.pdf\"", json);
        Assert.Contains("\"anexado_em\": \"2026-11-15T14:32:00Z\"", json);
        Assert.Contains("\"versao_catalogo\": 1", json);
    }

    [Fact]
    public void excluir_um_mantem_o_outro_e_a_pesquisa()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        var marcaNt = Cenario.Marca.De(c.Original(Cenario.Nt));
        var pdfLei = Path.Combine(c.Dados, lei.Sha256 + ".pdf");
        var idxLei = Path.Combine(c.Dados, lei.Sha256 + ".texto.json.gz");
        var marcaPdf = (File.ReadAllBytes(pdfLei), File.GetLastWriteTimeUtc(pdfLei));
        var marcaIdx = (File.ReadAllBytes(idxLei), File.GetLastWriteTimeUtc(idxLei));

        c.Acervo.Excluir(nt.Id);

        Assert.Equal([lei.Id], c.Acervo.Listar().Select(a => a.Id));
        Assert.False(File.Exists(Path.Combine(c.Dados, nt.Sha256 + ".pdf")));
        Assert.False(File.Exists(Path.Combine(c.Dados, nt.Sha256 + ".texto.json.gz")));
        Assert.Equal(marcaNt, Cenario.Marca.De(c.Original(Cenario.Nt))); // o original nunca é tocado
        Assert.True(marcaPdf.Item1.AsSpan().SequenceEqual(File.ReadAllBytes(pdfLei)));
        Assert.Equal(marcaPdf.Item2, File.GetLastWriteTimeUtc(pdfLei));
        Assert.True(marcaIdx.Item1.AsSpan().SequenceEqual(File.ReadAllBytes(idxLei)));
        Assert.Equal(marcaIdx.Item2, File.GetLastWriteTimeUtc(idxLei));

        var r = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None).Pesquisar("afastamento");
        Assert.Equal(3, r.Total);
        Assert.All(r.Resultados, x => Assert.Equal(lei.Id, x.AnexoId));
        Assert.Equal([1, 2, 3], r.Resultados.Select(x => x.Pagina));
    }

    [Fact]
    public void anexar_novos_depois_de_excluir()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        c.Acervo.Excluir(nt.Id);

        var federal = c.Anexar(Cenario.Federal);
        var nt2 = c.Anexar(Cenario.Nt);

        Assert.Equal([federal.Id, nt2.Id, lei.Id], c.Acervo.Listar().Select(a => a.Id));
        Assert.NotEqual(nt.Id, nt2.Id);
        Assert.Equal(nt.Sha256, nt2.Sha256); // mesmo arquivo, linha nova
        Assert.Equal(4, IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None).Pesquisar("afastamento").Total);
    }

    [Fact]
    public void repetido_e_recusado_mesmo_com_outro_nome()
    {
        c.Anexar(Cenario.Lei);
        var copia = Path.Combine(c.Originais, "copia.pdf");
        File.Copy(c.Original(Cenario.Lei), copia);
        var antes = c.ArquivosDaPasta();
        var catalogo = File.ReadAllBytes(Catalogo);

        var ex = Assert.Throws<AnexoRecusadoException>(() => c.Acervo.Preparar(copia));

        Assert.Equal("“copia.pdf” já está anexado como “LEI 9.999-2099 - FICTICIA”.", ex.Message);
        Assert.Equal(antes, c.ArquivosDaPasta()); // nada novo (a cópia de entrada foi apagada)
        Assert.True(catalogo.AsSpan().SequenceEqual(File.ReadAllBytes(Catalogo)));
    }

    [Fact]
    public void recusas_da_preparacao_nao_deixam_nada()
    {
        var vazio = Path.Combine(c.Originais, "vazio.pdf");
        File.WriteAllBytes(vazio, []);
        Assert.Equal("“vazio.pdf” está vazio.", Assert.Throws<AnexoRecusadoException>(() => c.Acervo.Preparar(vazio)).Message);
        Assert.Equal("“NAO-E-PDF - FICTICIA.pdf” não é um arquivo PDF.",
            Assert.Throws<AnexoRecusadoException>(() => c.Acervo.Preparar(c.Original(Cenario.NaoEPdf))).Message);
        var ausente = Path.Combine(c.Originais, "nao-existe.pdf");
        var ex = Assert.Throws<AnexoRecusadoException>(() => c.Acervo.Preparar(ausente));
        Assert.StartsWith("“nao-existe.pdf” não pôde ser lido: ", ex.Message);
        Assert.EndsWith(".", ex.Message);
        Assert.False(Directory.Exists(c.PastaEntrada) && Directory.GetFileSystemEntries(c.PastaEntrada).Length > 0);
    }

    [Fact]
    public void arquivo_aberto_por_outro_processo_para_escrita_ainda_e_lido()
    {
        var caminho = Path.Combine(c.Originais, "em-uso.pdf");
        File.Copy(c.Original(Cenario.Lei), caminho);
        using var escrevendo = new FileStream(caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        var e = c.Acervo.Preparar(caminho);
        Assert.Equal(Sha(c.Original(Cenario.Lei)), e.Sha256);
        c.Acervo.Descartar(e);
    }

    [Fact]
    public void recusas_da_leitura_nao_deixam_nada()
    {
        c.Anexar(Cenario.Lei);
        var catalogo = File.ReadAllBytes(Catalogo);

        var senha = Assert.Throws<ExtracaoRecusadaException>(() => c.Anexar(Cenario.Senha));
        Assert.Equal(FalhaExtracao.Senha, senha.Falha);
        Assert.Equal("“SENHA - FICTICIA.pdf” está protegido por senha e não pode ser aberto pelo editor. Anexe uma cópia sem senha.",
            MensagensAnexo.Senha("SENHA - FICTICIA.pdf"));
        var corrompido = Assert.Throws<ExtracaoRecusadaException>(() => c.Anexar(Cenario.Corrompido));
        Assert.Equal(FalhaExtracao.Invalido, corrompido.Falha);

        Assert.Empty(Directory.GetFileSystemEntries(c.PastaEntrada));
        Assert.True(catalogo.AsSpan().SequenceEqual(File.ReadAllBytes(Catalogo)));
        Assert.Single(c.Acervo.Listar());
        Assert.Equal(3, c.ArquivosDaPasta().Count); // a cópia, o índice e o catálogo da lei: nada das recusadas
    }

    [Fact]
    public async Task cancelar_no_meio_nao_deixa_nada()
    {
        c.Anexar(Cenario.Lei);
        var catalogo = File.ReadAllBytes(Catalogo);
        c.Extrator.EsperarCancelamento = true;
        using var cts = new CancellationTokenSource();
        var entrada = c.Acervo.Preparar(c.Original(Cenario.Nt));
        var tarefa = c.Extrator.ExtrairAsync(entrada.Caminho, null, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarefa);
        c.Acervo.Descartar(entrada);

        Assert.Empty(Directory.GetFileSystemEntries(c.PastaEntrada));
        Assert.True(catalogo.AsSpan().SequenceEqual(File.ReadAllBytes(Catalogo)));
        Assert.Single(c.Acervo.Listar());
    }

    [Fact]
    public void pdf_sem_texto_pesquisavel()
    {
        var a = c.Anexar(Cenario.Digitalizada);
        Assert.Equal(1, a.Paginas);
        Assert.Equal([1], a.PaginasSemTexto);
        Assert.False(a.TemTexto);
        Assert.Empty(IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None).Pesquisar("retangulo").Resultados);
        Assert.Equal([""], c.Acervo.TextoPorPagina(a.Id));
    }

    [Fact]
    public void renomear_e_mudar_esfera()
    {
        var lei = c.Anexar(Cenario.Lei);
        c.Acervo.Renomear(lei.Id, "  Lei nº 9.999/2099 (fictícia) ");
        c.Acervo.DefinirEsfera(lei.Id, Esfera.Outras);

        var outro = c.Novo().Listar().Single();
        Assert.Equal("Lei nº 9.999/2099 (fictícia)", outro.Titulo);
        Assert.Equal(Esfera.Outras, outro.Esfera);
        Assert.Equal(lei.Id, outro.Id);
        Assert.Equal(lei.Sha256, outro.Sha256);

        var vazio = Assert.Throws<ArgumentException>(() => c.Acervo.Renomear(lei.Id, "  "));
        Assert.Equal(MensagensAnexo.TituloVazio, vazio.Message);
        var longo = Assert.Throws<ArgumentException>(() => c.Acervo.Renomear(lei.Id, new string('x', 201)));
        Assert.Equal(MensagensAnexo.TituloLongo, longo.Message);
        c.Acervo.Renomear(lei.Id, new string('y', 200)); // o limite vale
        c.Acervo.Renomear(lei.Id, "Lei nº 9.999/2099 (fictícia)");
        Assert.Equal("Lei nº 9.999/2099 (fictícia)", c.Acervo.Listar().Single().Titulo);
    }

    [Fact]
    public void substituir_mantem_a_linha_e_troca_o_arquivo()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        c.Acervo.Renomear(lei.Id, "Lei nº 9.999/2099 (fictícia)");
        c.Acervo.DefinirEsfera(lei.Id, Esfera.Federal);

        var entrada = c.Acervo.Preparar(c.Original(Cenario.Lei2), lei.Id);
        Assert.Equal(lei.Id, entrada.SubstituiId);
        var r = c.Extrair(entrada.Caminho);
        var nova = c.Acervo.ConfirmarSubstituicao(lei.Id, entrada, r);

        Assert.Equal(lei.Id, nova.Id);
        Assert.Equal("Lei nº 9.999/2099 (fictícia)", nova.Titulo);
        Assert.Equal(Esfera.Federal, nova.Esfera);
        Assert.Equal(4, nova.Paginas);
        Assert.NotEqual(lei.Sha256, nova.Sha256);
        Assert.Equal(Sha(c.Original(Cenario.Lei2)), nova.Sha256);
        Assert.Equal(Cenario.Agora, nova.SubstituidoEm);
        Assert.Equal(lei.AnexadoEm, nova.AnexadoEm);
        Assert.Equal("LEI 9.999-2099 - FICTICIA (VERSAO 2).pdf", nova.ArquivoOriginal);
        Assert.Equal(new FileInfo(c.Original(Cenario.Lei2)).Length, nova.Tamanho);
        Assert.False(File.Exists(Path.Combine(c.Dados, lei.Sha256 + ".pdf")));
        Assert.False(File.Exists(Path.Combine(c.Dados, lei.Sha256 + ".texto.json.gz")));
        Assert.True(File.Exists(Path.Combine(c.Dados, nova.Sha256 + ".pdf")));
        Assert.Equal(2, c.Acervo.Listar().Count);
        Assert.Equal(EstadoAnexo.Pronto, c.Acervo.Estado(lei.Id));

        var p = IndiceLegislacao.Carregar(c.Acervo, CancellationToken.None).Pesquisar("versao 2");
        Assert.Single(p.Resultados);
        Assert.Equal(4, p.Resultados[0].Pagina);

        // Substituir pelo mesmo arquivo, e por um já anexado em outra linha.
        Assert.Equal(MensagensAnexo.Igual, Assert.Throws<AnexoRecusadoException>(() => c.Acervo.Preparar(c.Original(Cenario.Lei2), lei.Id)).Message);
        Assert.Equal(MensagensAnexo.Repetido("NT 99-2099 - FICTICIA.pdf", nt.Titulo),
            Assert.Throws<AnexoRecusadoException>(() => c.Acervo.Preparar(c.Original(Cenario.Nt), lei.Id)).Message);
        Assert.Empty(Directory.GetFileSystemEntries(c.PastaEntrada));
        Assert.Equal(c.Novo().Listar().Select(a => a.Sha256), c.Acervo.Listar().Select(a => a.Sha256));
    }

    [Fact]
    public void catalogo_gravado_de_forma_atomica()
    {
        var lei = c.Anexar(Cenario.Lei);
        c.Anexar(Cenario.Nt);
        var bytes = File.ReadAllBytes(Catalogo);
        c.Acervo.AntesDeTrocarCatalogo = () => throw new IOException("disco cheio (simulado)");

        Assert.Throws<IOException>(() => c.Acervo.Renomear(lei.Id, "Outro título"));

        Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Catalogo)));
        Assert.False(File.Exists(Catalogo + ".tmp"));
        c.Acervo.AntesDeTrocarCatalogo = null;
        Assert.Equal("LEI 9.999-2099 - FICTICIA", c.Acervo.Listar().Single(a => a.Id == lei.Id).Titulo);
    }

    [Fact]
    public void falha_ao_confirmar_nao_deixa_copia_sem_linha()
    {
        c.Anexar(Cenario.Lei);
        var antes = c.ArquivosDaPasta();
        c.Acervo.AntesDeTrocarCatalogo = () => throw new IOException("queda (simulada)");
        Assert.Throws<IOException>(() => c.Anexar(Cenario.Nt));
        c.Acervo.AntesDeTrocarCatalogo = null;
        Assert.Equal(antes, c.ArquivosDaPasta()); // nem o PDF novo, nem o índice, nem a entrada
        Assert.Single(c.Acervo.Listar());
    }

    [Fact]
    public void catalogo_corrompido_usa_o_bak()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        c.Acervo.Renomear(lei.Id, "Título novo"); // o .bak guarda o catálogo anterior (com os dois anexos)
        var doBak = c.Novo();
        File.WriteAllBytes(Catalogo, "lixo { não é json"u8.ToArray());
        var esperado = JsonNode.Parse(File.ReadAllText(Bak))!["anexos"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToArray();

        var lista = doBak.Listar();

        Assert.Equal(esperado.Order(), lista.Select(a => a.Id).Order());
        Assert.Equal("LEI 9.999-2099 - FICTICIA", lista.Single(a => a.Id == lei.Id).Titulo);
        Assert.Equal([MensagensAnexo.Corrompido], doBak.Avisos);
        Assert.Single(Directory.GetFiles(c.Dados, "catalogo.corrompido-*.json"));
        Assert.Matches(@"^CATALOGO\.CORROMPIDO-\d{4}-\d{2}-\d{2}-\d{6}\.JSON$", Path.GetFileName(Directory.GetFiles(c.Dados, "catalogo.corrompido-*.json")[0]));
        // O catálogo volta a existir e a próxima carga não avisa de novo.
        Assert.Equal(2, c.Novo().Listar().Count);
        var terceiro = c.Novo();
        terceiro.Listar();
        Assert.Empty(terceiro.Avisos);
        _ = nt;
    }

    [Fact]
    public void pdf_sem_linha_volta_como_recuperado()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        File.Delete(Catalogo);
        File.Delete(Bak);
        var pdfs = new[] { lei.Sha256, nt.Sha256 }.Select(s => (s, bytes: File.ReadAllBytes(Path.Combine(c.Dados, s + ".pdf")))).ToArray();

        var novo = c.Novo();
        var lista = novo.Listar();

        Assert.Equal(2, lista.Count);
        Assert.All(lista, a =>
        {
            Assert.Equal("PDF recuperado " + a.Sha256[..8], a.Titulo);
            Assert.Equal(Esfera.Outras, a.Esfera);
            Assert.Equal(EstadoAnexo.Pronto, novo.Estado(a.Id));
        });
        Assert.Equal(3, lista.Single(a => a.Sha256 == lei.Sha256).Paginas); // do índice
        Assert.Equal(2, lista.Single(a => a.Sha256 == nt.Sha256).Paginas);
        Assert.Equal([MensagensAnexo.Recuperados(2)], novo.Avisos);
        foreach (var (sha, bytes) in pdfs) Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(c.Dados, sha + ".pdf"))));
        // Gravado: a próxima carga já traz as linhas, sem aviso.
        var depois = c.Novo();
        Assert.Equal(2, depois.Listar().Count);
        Assert.Empty(depois.Avisos);
    }

    [Fact]
    public void pdf_recuperado_sem_indice_fica_para_reindexar()
    {
        var lei = c.Anexar(Cenario.Lei);
        File.Delete(Catalogo);
        File.Delete(Bak);
        File.Delete(Path.Combine(c.Dados, lei.Sha256 + ".texto.json.gz"));
        var novo = c.Novo();
        var id = novo.Listar().Single().Id;
        Assert.Equal([id], novo.SemIndice().Select(a => a.Id));
        var r = c.Extrair(Path.Combine(c.Dados, lei.Sha256 + ".pdf"));
        novo.GravarIndice(id, r);
        Assert.Empty(novo.SemIndice());
        Assert.Equal(3, novo.Listar().Single().Paginas); // as páginas passam para o catálogo
    }

    [Fact]
    public void copia_ausente_fica_marcada()
    {
        var lei = c.Anexar(Cenario.Lei);
        File.Delete(Path.Combine(c.Dados, lei.Sha256 + ".pdf"));

        Assert.Equal(EstadoAnexo.SemCopia, c.Acervo.Estado(lei.Id));
        Assert.Null(c.Acervo.CaminhoPdf(lei.Id));
        c.Acervo.Excluir(lei.Id);
        Assert.Empty(c.Acervo.Listar());
        Assert.False(File.Exists(Path.Combine(c.Dados, lei.Sha256 + ".texto.json.gz")));
    }

    [Fact]
    public void caminho_do_pdf_de_um_anexo_pronto()
    {
        var lei = c.Anexar(Cenario.Lei);
        Assert.Equal(Path.Combine(c.Dados, lei.Sha256 + ".pdf"), c.Acervo.CaminhoPdf(lei.Id));
        Assert.Null(c.Acervo.CaminhoPdf("lg_" + new string('0', 32)));
    }

    [Fact]
    public void indice_ausente_ou_de_outra_versao_e_refeito()
    {
        var lei = c.Anexar(Cenario.Lei);
        var indice = Path.Combine(c.Dados, lei.Sha256 + ".texto.json.gz");
        Assert.Equal(EstadoAnexo.Pronto, c.Acervo.Estado(lei.Id));
        var original = File.ReadAllBytes(indice);

        File.Delete(indice);
        Assert.Equal([lei.Id], c.Acervo.SemIndice().Select(a => a.Id));
        Assert.Equal(EstadoAnexo.SemIndice, c.Acervo.Estado(lei.Id));
        Assert.Null(c.Acervo.TextoPorPagina(lei.Id));

        File.WriteAllBytes(indice, GzipJson(new JsonObject { ["versao_indice"] = 99, ["sha256"] = lei.Sha256, ["paginas"] = new JsonArray("a") }));
        Assert.Equal([lei.Id], c.Acervo.SemIndice().Select(a => a.Id)); // outra versão

        File.WriteAllBytes(indice, GzipJson(new JsonObject { ["versao_indice"] = 1, ["sha256"] = new string('0', 64), ["paginas"] = new JsonArray("a") }));
        Assert.Equal([lei.Id], c.Acervo.SemIndice().Select(a => a.Id)); // sha que não confere

        File.WriteAllBytes(indice, "isto não é gzip"u8.ToArray());
        Assert.Equal([lei.Id], c.Acervo.SemIndice().Select(a => a.Id));

        var r = new ResultadoExtracao(["Art. 1º um", "Art. 2º dois", "Art. 3º três"]);
        c.Acervo.GravarIndice(lei.Id, r);
        Assert.Empty(c.Acervo.SemIndice());
        Assert.Equal(["Art. 1º um", "Art. 2º dois", "Art. 3º três"], c.Acervo.TextoPorPagina(lei.Id));
        _ = original;
    }

    [Fact]
    public void indice_tem_o_formato_do_desenho()
    {
        var lei = c.Anexar(Cenario.Lei);
        using var gz = new GZipStream(File.OpenRead(Path.Combine(c.Dados, lei.Sha256 + ".texto.json.gz")), CompressionMode.Decompress);
        var no = JsonNode.Parse(gz)!;
        Assert.Equal(1, no["versao_indice"]!.GetValue<int>());
        Assert.Equal(lei.Sha256, no["sha256"]!.GetValue<string>());
        Assert.Equal("pdf.js 6.4.299", no["leitor"]!.GetValue<string>());
        Assert.Equal(3, no["paginas"]!.AsArray().Count);
        Assert.Equal("Art. 2º O afastamento frontal mínimo é de 3,00 m.\nArt. 3º A taxa de ocupação máxima é de 60%.", no["paginas"]![1]!.GetValue<string>());
    }

    [Fact]
    public void indice_aceita_texto_de_pdf_mal_formado_e_guarda_vazio_para_pagina_sem_letra()
    {
        var lei = c.Anexar(Cenario.Lei);
        // Surrogate solto não cabe em UTF-8 (texto de PDF mal formado): vira U+FFFD em vez de derrubar a gravação do índice.
        var r = new ResultadoExtracao(["Art. 1º \uD800 fim", "— · … 123", "  ... ", "Par 😀 emoji"]);
        Assert.Equal([3], r.PaginasSemTexto.Where(p => p == 3)); // "..." sem letra nem dígito
        c.Acervo.GravarIndice(lei.Id, r);

        var texto = c.Acervo.TextoPorPagina(lei.Id)!;
        Assert.Equal("Art. 1º � fim", texto[0]);
        Assert.Equal("— · … 123", texto[1]);
        Assert.Equal("", texto[2]); // página sem letra nem dígito: vazia, como no desenho
        Assert.Equal("Par 😀 emoji", texto[3]); // par válido de surrogates fica
        var atualizado = c.Acervo.Listar().Single();
        Assert.Equal(4, atualizado.Paginas);
        Assert.Equal([3], atualizado.PaginasSemTexto);
    }

    [Fact]
    public void catalogo_de_versao_mais_nova_fica_so_leitura()
    {
        var lei = c.Anexar(Cenario.Lei);
        var no = JsonNode.Parse(File.ReadAllText(Catalogo))!;
        no["versao_catalogo"] = 2;
        no["campo_novo"] = "x";
        File.WriteAllText(Catalogo, no.ToJsonString());
        var bytes = File.ReadAllBytes(Catalogo);

        var novo = c.Novo();
        Assert.Equal(lei.Id, novo.Listar().Single().Id); // ainda se lê
        Assert.True(novo.SomenteLeitura);
        var ex = Assert.Throws<InvalidOperationException>(() => novo.Preparar(c.Original(Cenario.Nt)));
        Assert.Equal(MensagensAnexo.SomenteLeitura, ex.Message);
        Assert.Throws<InvalidOperationException>(() => novo.Renomear(lei.Id, "x"));
        Assert.Throws<InvalidOperationException>(() => novo.DefinirEsfera(lei.Id, Esfera.Outras));
        Assert.Throws<InvalidOperationException>(() => novo.Excluir(lei.Id));
        Assert.True(bytes.AsSpan().SequenceEqual(File.ReadAllBytes(Catalogo))); // nada é regravado
        Assert.False(File.Exists(Bak));
    }

    [Fact]
    public void avisos_acumulam_ate_serem_lidos_e_o_indice_nao_os_consome()
    {
        c.Anexar(Cenario.Lei);
        c.Anexar(Cenario.Nt);
        c.Acervo.Renomear(c.Acervo.Listar()[0].Id, "x"); // o .bak passa a existir
        var novo = c.Novo();
        File.WriteAllBytes(Catalogo, "lixo"u8.ToArray());

        var indice = IndiceLegislacao.Carregar(novo, CancellationToken.None); // carrega o catálogo (e detecta o dano)
        indice.Atualizar(novo);
        novo.Listar();
        novo.Estado(novo.Listar()[0].Id);

        Assert.Equal([MensagensAnexo.Corrompido], novo.Avisos); // continuam lá: ninguém leu
        Assert.Equal([MensagensAnexo.Corrompido], novo.LerAvisos());
        Assert.Empty(novo.Avisos);
        Assert.Empty(novo.LerAvisos());
    }

    [Fact]
    public void excluir_com_a_copia_em_uso_avisa_e_a_proxima_carga_apaga_sem_recuperar()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        var pdf = Path.Combine(c.Dados, lei.Sha256 + ".pdf");
        var marca = Path.Combine(c.Dados, lei.Sha256 + ".excluir");

        string? aviso;
        using (new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.Read)) // outro programa segura a cópia (sem FileShare.Delete)
        {
            aviso = c.Acervo.Excluir(lei.Id);
            Assert.Equal(MensagensAnexo.CopiaEmUso, aviso);
            Assert.Equal([nt.Id], c.Acervo.Listar().Select(a => a.Id)); // a linha já saiu do catálogo
            Assert.True(File.Exists(pdf));
            Assert.True(File.Exists(marca));
            // Ainda preso: a carga não a "recupera" como PDF sem linha.
            var durante = c.Novo();
            Assert.Equal([nt.Id], durante.Listar().Select(a => a.Id));
            Assert.Empty(durante.Avisos);
            Assert.True(File.Exists(pdf));
        }

        var depois = c.Novo();
        Assert.Equal([nt.Id], depois.Listar().Select(a => a.Id));
        Assert.Empty(depois.Avisos);
        Assert.False(File.Exists(pdf));
        Assert.False(File.Exists(Path.Combine(c.Dados, lei.Sha256 + ".texto.json.gz")));
        Assert.False(File.Exists(marca));
        Assert.Null(c.Acervo.Excluir("lg_" + new string('0', 32))); // inexistente: nada, sem aviso
    }

    [Fact]
    public void excluir_normal_nao_deixa_marca_e_devolve_null()
    {
        var lei = c.Anexar(Cenario.Lei);
        Assert.Null(c.Acervo.Excluir(lei.Id));
        Assert.False(File.Exists(Path.Combine(c.Dados, lei.Sha256 + ".excluir")));
    }

    [Fact]
    public void anexar_de_novo_o_pdf_de_uma_exclusao_pendente_cancela_a_exclusao()
    {
        var lei = c.Anexar(Cenario.Lei);
        var pdf = Path.Combine(c.Dados, lei.Sha256 + ".pdf");
        string novoId;
        using (new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(MensagensAnexo.CopiaEmUso, c.Acervo.Excluir(lei.Id));
            novoId = c.Anexar(Cenario.Lei).Id; // o mesmo arquivo volta enquanto a cópia ainda está presa
            Assert.False(File.Exists(Path.Combine(c.Dados, lei.Sha256 + ".excluir")));
        }

        var lista = c.Novo().Listar();
        Assert.Equal([novoId], lista.Select(a => a.Id));
        Assert.True(File.Exists(pdf)); // a cópia viva não foi apagada pela exclusão antiga
        Assert.Equal(EstadoAnexo.Pronto, c.Acervo.Estado(novoId));
    }

    [Fact]
    public void substituir_com_a_copia_antiga_em_uso_avisa()
    {
        var lei = c.Anexar(Cenario.Lei);
        var pdf = Path.Combine(c.Dados, lei.Sha256 + ".pdf");
        using (new FileStream(pdf, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var e = c.Acervo.Preparar(c.Original(Cenario.Lei2), lei.Id);
            c.Acervo.ConfirmarSubstituicao(lei.Id, e, c.Extrair(e.Caminho));
            Assert.Equal([MensagensAnexo.CopiaEmUso], c.Acervo.LerAvisos());
        }
        Assert.Single(c.Novo().Listar()); // a cópia antiga não volta como "recuperada"
        Assert.False(File.Exists(pdf));
    }

    [Fact]
    public void catalogos_corrompidos_com_mais_de_30_dias_sao_apagados()
    {
        Directory.CreateDirectory(c.Dados);
        var velho = Path.Combine(c.Dados, "catalogo.corrompido-2026-10-01-000000.json");
        var recente = Path.Combine(c.Dados, "catalogo.corrompido-2026-11-14-000000.json");
        File.WriteAllText(velho, "x");
        File.WriteAllText(recente, "x");
        File.SetLastWriteTimeUtc(velho, Cenario.Agora.UtcDateTime.AddDays(-31));
        File.SetLastWriteTimeUtc(recente, Cenario.Agora.UtcDateTime.AddDays(-29));

        c.Acervo.LimparEntradasOrfas();

        Assert.False(File.Exists(velho));
        Assert.True(File.Exists(recente));
    }

    [Fact]
    public void entradas_orfas_com_mais_de_um_dia_sao_apagadas()
    {
        Directory.CreateDirectory(c.PastaEntrada);
        var velho = Path.Combine(c.PastaEntrada, "velho.pdf");
        var recente = Path.Combine(c.PastaEntrada, "recente.pdf");
        File.WriteAllText(velho, "x");
        File.WriteAllText(recente, "x");
        File.SetLastWriteTimeUtc(velho, Cenario.Agora.UtcDateTime.AddDays(-2));
        File.SetLastWriteTimeUtc(recente, Cenario.Agora.UtcDateTime.AddHours(-1));

        c.Acervo.LimparEntradasOrfas();

        Assert.False(File.Exists(velho));
        Assert.True(File.Exists(recente));
    }

    [Fact]
    public void duas_instancias_nao_perdem_alteracoes()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        var a = c.Novo();
        var b = c.Novo();
        a.Listar();
        b.Listar(); // as duas já leram o catálogo

        a.Renomear(lei.Id, "Título da lei");
        b.Renomear(nt.Id, "Título da NT");

        var final = c.Novo().Listar();
        Assert.Equal("Título da lei", final.Single(x => x.Id == lei.Id).Titulo);
        Assert.Equal("Título da NT", final.Single(x => x.Id == nt.Id).Titulo);
    }

    [Fact]
    public async Task duas_instancias_anexando_ao_mesmo_tempo_ficam_com_os_dois()
    {
        var a = c.Novo();
        var b = c.Novo();
        var t1 = Task.Run(() => c.Anexar(Cenario.Lei, a));
        var t2 = Task.Run(() => c.Anexar(Cenario.Nt, b));
        await Task.WhenAll(t1, t2);
        Assert.Equal(2, c.Novo().Listar().Count);
    }

    [Fact]
    public async Task gravacoes_concorrentes_na_mesma_instancia_sao_serializadas()
    {
        // O app grava fora da fila de comandos (anexar, renomear, esfera, excluir, índice, órfãs): o acervo serializa cada gravação.
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        var dig = c.Anexar(Cenario.Digitalizada);
        var texto = c.Acervo.TextoPorPagina(lei.Id)!;
        var tarefas = new List<Task>();
        for (var i = 0; i < 20; i++)
        {
            var n = i;
            tarefas.Add(Task.Run(() => c.Acervo.Renomear(lei.Id, "Lei " + n)));
            tarefas.Add(Task.Run(() => c.Acervo.DefinirEsfera(nt.Id, n % 2 == 0 ? Esfera.Outras : Esfera.Federal)));
            tarefas.Add(Task.Run(() => c.Acervo.GravarIndice(lei.Id, new ResultadoExtracao(texto))));
            tarefas.Add(Task.Run(() => c.Acervo.LimparEntradasOrfas()));
            tarefas.Add(Task.Run(() => c.Acervo.ListarComEstado()));
        }
        tarefas.Add(Task.Run(() => c.Acervo.Excluir(dig.Id)));
        tarefas.Add(Task.Run(() => c.Anexar(Cenario.Federal)));
        await Task.WhenAll(tarefas);
        var final = c.Novo().Listar();
        Assert.Equal(3, final.Count); // lei, NT e federal: nenhuma gravação perdeu a outra
        Assert.StartsWith("Lei ", final.Single(x => x.Id == lei.Id).Titulo);
        Assert.Contains(final.Single(x => x.Id == nt.Id).Esfera, new[] { Esfera.Outras, Esfera.Federal }); // a que gravou por último
        Assert.DoesNotContain(final, x => x.Id == dig.Id);
        Assert.Empty(c.Acervo.SemIndice());
    }

    [Fact]
    public async Task troca_atomica_espera_o_arquivo_preso_por_um_instante()
    {
        // O OneDrive (tests\saida fica nele) ou um antivírus segura o destino por um instante: a troca espera, em vez de falhar a gravação.
        using var pasta = Ambiente.NovaPasta("troca-presa");
        var destino = Path.Combine(pasta, "catalogo.json");
        var tmp = destino + ".tmp";
        File.WriteAllText(destino, "antigo");
        File.WriteAllText(tmp, "novo");
        var preso = new FileStream(destino, FileMode.Open, FileAccess.Read, FileShare.None);
        var soltar = Task.Run(async () => { await Task.Delay(400); preso.Dispose(); });
        AcervoLegislacao.ComRepeticao(() => File.Move(tmp, destino, overwrite: true));
        await soltar;
        Assert.Equal("novo", File.ReadAllText(destino));
        Assert.False(File.Exists(tmp));
        // Uma falha que não é passageira sobe na hora (sem esperar o prazo).
        Assert.Throws<FileNotFoundException>(() => AcervoLegislacao.ComRepeticao(() => File.Move(tmp, destino, overwrite: true)));
    }

    [Fact]
    public void listar_com_estado_numa_so_carga()
    {
        var lei = c.Anexar(Cenario.Lei);
        var nt = c.Anexar(Cenario.Nt);
        File.Delete(Path.Combine(c.Dados, nt.Sha256 + ".pdf"));
        var l = c.Acervo.ListarComEstado();
        Assert.Equal([nt.Id, lei.Id], l.Select(x => x.Anexo.Id));
        Assert.Equal([EstadoAnexo.SemCopia, EstadoAnexo.Pronto], l.Select(x => x.Estado));
    }

    [Fact]
    public void catalogo_ordena_por_esfera_e_titulo_sem_caixa()
    {
        var a = c.Anexar(Cenario.Lei);
        var b = c.Anexar(Cenario.Federal);
        var d = c.Anexar(Cenario.Nt);
        c.Acervo.DefinirEsfera(a.Id, Esfera.Outras);
        c.Acervo.DefinirEsfera(b.Id, Esfera.Outras);
        c.Acervo.Renomear(a.Id, "beta");
        c.Acervo.Renomear(b.Id, "Alfa");
        Assert.Equal([d.Id, b.Id, a.Id], c.Acervo.Listar().Select(x => x.Id)); // Estadual, depois Outras: "Alfa" antes de "beta"
    }

    [Fact]
    public void preparar_confere_o_tamanho_antes_de_ler()
    {
        // O limite do teste é pequeno (não se cria um arquivo de 200 MB em pasta do OneDrive): o PDF de 2.057 bytes é grande demais.
        var tamanho = new FileInfo(c.Original(Cenario.Corrompido)).Length;
        c.Acervo.LimiteBytes = 1000;
        var ex = Assert.Throws<AnexoRecusadoException>(() => c.Acervo.Preparar(c.Original(Cenario.Corrompido)));
        Assert.Equal(MensagensAnexo.Grande("CORROMPIDO - FICTICIA.pdf", tamanho), ex.Message);
        Assert.False(Directory.Exists(c.PastaEntrada) && Directory.GetFileSystemEntries(c.PastaEntrada).Length > 0);
        Assert.Equal(VerificacaoPdf.LimiteBytes, new AcervoLegislacao(c.Dados).LimiteBytes); // o padrão é o do desenho
    }

    static byte[] GzipJson(JsonNode no)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress)) gz.Write(Encoding.UTF8.GetBytes(no.ToJsonString()));
        return ms.ToArray();
    }
}
