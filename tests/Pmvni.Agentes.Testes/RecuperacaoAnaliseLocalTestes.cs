using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.Json;
using Xunit;

namespace Pmvni.Agentes.Testes;

public class RecuperacaoAnaliseLocalTestes
{
    const string Id = "2026-11-24_teste";
    static string Em(string raiz, string nome) => Path.Combine(raiz, nome);
    static void Gravar(string path, string texto)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, texto);
    }
    static void Json(string path, object obj) => Gravar(path, JsonSerializer.Serialize(obj));
    static string Sha(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
    static string Controle(string raiz) => Em(raiz, @".pmvni-ai\analise");
    static string Registro(string raiz) => Em(raiz, Path.Combine(".pmvni-ai", "editor", Id));

    [DllImport("kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool CriarSegundoNome(string novo, string existente, IntPtr reservado);

    [Fact]
    public void Caminhos_distintos_exigem_identidade_do_arquivo_mesmo_com_bytes_iguais()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var tmp = Ambiente.PastaDeTeste("identidade-arquivo");
        var origem = Em(tmp, "nome-no-servidor");
        var alias = Em(tmp, "segundo-nome");
        var copia = Em(tmp, "copia");
        const string arquivo = "estado.json";
        Gravar(Em(origem, arquivo), "mesmos bytes");
        Gravar(Em(copia, arquivo), "mesmos bytes");
        Directory.CreateDirectory(alias);
        Assert.True(CriarSegundoNome(Em(alias, arquivo), Em(origem, arquivo), IntPtr.Zero));
        Assert.True(RecuperacaoAnaliseLocal.MesmoProcesso(origem, alias, arquivo));
        Assert.False(RecuperacaoAnaliseLocal.MesmoProcesso(origem, copia, arquivo));
    }

    static void Iniciar(string raiz)
    {
        Json(Em(Registro(raiz), "EXECUCAO.JSON"), new { id = Id });
        Json(Em(Controle(raiz), "engine_state.json"), new { editor_execution = Id, @case = raiz });
    }

    [Fact]
    public void Retoma_na_seguinte_estacao_preservando_bytes_datas_e_bloqueia_estacao_atrasada()
    {
        using var tmp = Ambiente.PastaDeTeste("checkpoint-estacoes");
        var origem = Em(tmp, "servidor");
        var a = Em(tmp, "A");
        var b = Em(tmp, "B");
        Iniciar(a);
        var prova = Em(Controle(a), @"runs\tecnica\prova.txt");
        Gravar(prova, "área e índice");
        var data = File.GetLastWriteTimeUtc(prova);
        Gravar(Em(Controle(a), @"historico\velho\teste.txt"), "antigo");
        Gravar(Em(a, @".pmvni-ai\editor\outra\CONTINUIDADE.JSON"), "outra");
        Gravar(Em(Registro(a), "PUBLICACAO-PENDENTE.JSON"), "em callback");
        var cache = @".pmvni-ai\cache-pdf\hash\texto.txt";
        Gravar(Em(a, cache), "texto");
        Gravar(Em(a, @".pmvni-ai\cache-pdf\outro\texto.txt"), "fora da lista");
        Json(Em(Controle(a), @"preparo\preparo.json"), new { arquivos = new[] { Em(a, cache) } });
        RecuperacaoAnaliseLocal.Salvar(origem, a, Id, "analise");
        Assert.Equal("analise", RecuperacaoAnaliseLocal.ControleDoCheckpoint(origem, Id));
        Assert.True(RecuperacaoAnaliseLocal.GeracaoAtual(origem, a, Id));
        Assert.Equal(a, RecuperacaoAnaliseLocal.Restaurar(origem, b, Id, @".pmvni-ai\analise"));
        var copia = Em(Controle(b), @"runs\tecnica\prova.txt");
        Assert.Equal("área e índice", File.ReadAllText(copia));
        Assert.Equal(data, File.GetLastWriteTimeUtc(copia));
        Assert.False(Directory.Exists(Em(Controle(b), "historico")));
        Assert.False(Directory.Exists(Em(b, @".pmvni-ai\editor\outra")));
        Assert.False(File.Exists(Em(Registro(b), "PUBLICACAO-PENDENTE.JSON")));
        Assert.Equal("texto", File.ReadAllText(Em(b, cache)));
        Assert.False(Directory.Exists(Em(b, @".pmvni-ai\cache-pdf\outro")));
        Gravar(copia, "segunda etapa na B");
        RecuperacaoAnaliseLocal.Salvar(origem, b, Id, "analise");
        Assert.False(RecuperacaoAnaliseLocal.GeracaoAtual(origem, a, Id));
        Assert.Throws<IOException>(() => RecuperacaoAnaliseLocal.Salvar(origem, a, Id, "analise"));
        var c = Em(tmp, "C");
        Assert.Equal(b, RecuperacaoAnaliseLocal.Restaurar(origem, c, Id, "analise"));
        Assert.Equal("segunda etapa na B", File.ReadAllText(Em(Controle(c), @"runs\tecnica\prova.txt")));
    }

    [Fact]
    public void Blob_corrompido_ou_rota_errada_nao_instalam_snapshot_parcial()
    {
        using var tmp = Ambiente.PastaDeTeste("checkpoint-danificado");
        var origem = Em(tmp, "servidor");
        var a = Em(tmp, "A");
        var b = Em(tmp, "B");
        Iniciar(a);
        var evidencia = Em(Controle(a), "prova.txt");
        Gravar(evidencia, "íntegra");
        RecuperacaoAnaliseLocal.Salvar(origem, a, Id, "analise");
        Assert.Throws<InvalidDataException>(() => RecuperacaoAnaliseLocal.Restaurar(origem, b, Id, "analise-pro"));
        var sha = Sha(evidencia);
        var blob = Em(origem, Path.Combine(".pmvni-ai", "retomadas-locais", Id, "objetos", sha[..2], sha));
        File.WriteAllBytes(blob, new byte[new FileInfo(blob).Length]);
        Assert.Throws<IOException>(() => RecuperacaoAnaliseLocal.Restaurar(origem, b, Id, "analise"));
        Assert.False(Directory.Exists(Em(b, ".pmvni-ai")));
    }

    [Fact]
    public void Cache_declarado_indisponivel_preserva_ultima_geracao()
    {
        using var tmp = Ambiente.PastaDeTeste("checkpoint-cache-ausente");
        var origem = Em(tmp, "servidor");
        var a = Em(tmp, "A");
        Iniciar(a);
        RecuperacaoAnaliseLocal.Salvar(origem, a, Id, "analise");
        var ponteiro = Em(origem, Path.Combine(".pmvni-ai", "retomadas-locais", Id, "ATUAL.JSON"));
        var anterior = Sha(ponteiro);
        Json(Em(Controle(a), @"preparo\preparo.json"), new { pagina = Em(a, @".pmvni-ai\cache-pdf\ausente.png") });
        Assert.ThrowsAny<IOException>(() => RecuperacaoAnaliseLocal.Salvar(origem, a, Id, "analise"));
        Assert.Equal(anterior, Sha(ponteiro));
    }

    [Fact]
    public void Legado_exige_trava_livre_e_vinculo_e_so_le_a_origem()
    {
        using var tmp = Ambiente.PastaDeTeste("checkpoint-legado");
        var origem = Em(tmp, "servidor");
        var local = Em(tmp, "nova");
        Iniciar(origem);
        var lockFile = Em(Registro(origem), "EXECUTOR.LOCK");
        Gravar(lockFile, "0");
        var estado = Em(Controle(origem), "engine_state.json");
        var antes = Sha(estado);
        using (var trava = new FileStream(lockFile, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            trava.Lock(0, 1);
            Assert.Throws<IOException>(() => RecuperacaoAnaliseLocal.RestaurarLegado(origem, local, Id, "analise"));
        }
        Assert.False(Directory.Exists(Em(local, ".pmvni-ai")));
        Assert.Equal(origem, RecuperacaoAnaliseLocal.RestaurarLegado(origem, local, Id, "analise"));
        Assert.Equal(antes, Sha(estado));
        Assert.Equal(antes, Sha(Em(Controle(local), "engine_state.json")));
        Json(estado, new { editor_execution = "diferente", @case = origem });
        Assert.Throws<InvalidDataException>(() => RecuperacaoAnaliseLocal.RestaurarLegado(origem, Em(tmp, "outra"), Id, "analise"));
    }

    static FimAnalise Produto(string local, bool manual = false)
    {
        var controle = Controle(local);
        var produto = Em(controle, manual ? "edicao-manual" : @"runs\produce-1");
        var doc = Em(produto, "RELATORIO_DADOS.JSON");
        var img = Em(controle, @"local\figura.png");
        Gravar(img, "png fictício");
        Json(doc, new { figuras = new[] { new { imagem = Path.GetRelativePath(produto, img) } } });
        var delivery = Em(controle, "delivery.json");
        if (manual)
        {
            Json(delivery, new { document = doc, modo = "edicao_manual", etapa_pendente = "analyze" });
            Json(Em(controle, "engine_state.json"), new { editor_execution = Id, @case = local, decisao_cota = "manual",
                decisoes = new[] { new { tipo = "limite_cota", etapa = "analyze", resposta = "manual" } } });
            return new("concluida", doc, null, "", 0);
        }
        var pdf = Em(produto, "RELATORIO.pdf");
        Gravar(pdf, "%PDF-1.4");
        Json(delivery, new { document = doc, pdf });
        var cert = Em(produto, "montagem.json");
        Json(cert, new { document_sha256 = Sha(doc), pdf_sha256 = Sha(pdf), delivery_sha256 = Sha(delivery) });
        Json(Em(controle, "engine_state.json"), new { editor_execution = Id, @case = local,
            stages = new { produce = new { evidence = new[] { new { path = cert, sha256 = Sha(cert) } } } },
            gate = new { validators = "ALL_PASSED", pdf, pdf_sha256 = Sha(pdf) } });
        return new("concluida", doc, pdf, "", 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Publica_so_o_produto_com_figuras_e_preserva_a_entrega_anterior(bool manual)
    {
        using var tmp = Ambiente.PastaDeTeste("entrega-documentos");
        var origem = Em(tmp, "servidor");
        var a = Em(tmp, "A");
        var fim = Produto(a, manual);
        Gravar(Em(Controle(a), "privado.bin"), "informação fora da entrega");
        Assert.Throws<IOException>(() => RecuperacaoAnaliseLocal.Publicar(origem, a, Id, "analise", fim,
            () => throw new IOException("original mudou")));
        Assert.Null(RecuperacaoAnaliseLocal.EntregaPublicada(origem, Id));
        Assert.False(Directory.Exists(Em(origem, RecuperacaoAnaliseLocal.Produtos)));
        var pub = RecuperacaoAnaliseLocal.Publicar(origem, a, Id, "analise", fim, () => {});
        Assert.Equal(Sha(fim.Documento!), Sha(pub.Documento!));
        Assert.Equal(pub, RecuperacaoAnaliseLocal.Publicar(origem, a, Id, "analise", fim, () => {}));
        Assert.Equal(pub.Documento, RecuperacaoAnaliseLocal.EntregaPublicada(origem, Id)?.Documento);
        Assert.Equal(manual, pub.Pdf == null);
        var dados = JsonDocument.Parse(File.ReadAllBytes(pub.Documento!));
        var fig = dados.RootElement.GetProperty("figuras")[0].GetProperty("imagem").GetString()!;
        var img = Pmvni.Composicao.CaminhoImagem.Resolver(Path.GetDirectoryName(pub.Documento!)!, fig, false);
        Assert.Equal("png fictício", File.ReadAllText(img!));
        Assert.Empty(Directory.EnumerateFiles(Em(origem, RecuperacaoAnaliseLocal.Produtos), "privado.bin", SearchOption.AllDirectories));
        Gravar(fim.Documento!, "{}");
        if (!manual) Assert.Throws<InvalidDataException>(() => RecuperacaoAnaliseLocal.Publicar(origem, a, Id, "analise", fim, () => {}));
        Assert.Equal(pub.Documento, RecuperacaoAnaliseLocal.EntregaPublicada(origem, Id)?.Documento);
        Gravar(img!, "divergiu");
        Assert.Throws<InvalidDataException>(() => RecuperacaoAnaliseLocal.EntregaPublicada(origem, Id));
    }
}
