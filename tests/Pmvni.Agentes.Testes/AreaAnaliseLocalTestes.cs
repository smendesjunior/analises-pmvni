using System.Security.Cryptography;
using System.Text.Json;
using Pmvni.Revisao;
using Xunit;

namespace Pmvni.Agentes.Testes;

public sealed class AreaAnaliseLocalTestes
{
    sealed class Caso : IDisposable
    {
        public readonly PastaTeste Raiz = Ambiente.PastaDeTeste("originais");
        // A árvore do repositório pode estar no OneDrive. O destino da cópia precisa ser físico.
        public readonly PastaTeste Estacao = new(Directory.CreateTempSubdirectory("pmvni-area-teste-").FullName);
        public readonly PedidoExecucao Pedido;
        public string Origem => Pedido.Processo.Pasta;
        public string Id => Path.GetFileName(Pedido.Registro.Pasta);

        public Caso()
        {
            var processo = Ambiente.NovoProcesso(Raiz);
            var registro = RegistroExecucao.Criar(processo.Pasta, Provedor.ChatGpt, new DateTime(2099, 1, 1));
            registro.Atualizar(o => o["area_local_solicitada"] = true);
            registro.GravarPedido("exemplo");
            registro.GravarAnexos([]);
            Pedido = new(Provedor.ChatGpt, processo, Ambiente.PecasFalsas(Raiz), ModoAcesso.Conta, Raiz, "exemplo", [], registro);
        }

        public string Gravar(string relativo, string texto)
        {
            var arquivo = Path.Combine(Origem, relativo);
            Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
            File.WriteAllText(arquivo, texto);
            return arquivo;
        }

        public void Associar(AreaAnaliseLocal area) => Pedido.Registro.Atualizar(o =>
        {
            o["area_local_versao"] = 1;
            o["area_local_id"] = area.Identificador;
        });

        public AreaAnaliseLocal Preparar(bool retomar = false, string? documentos = null) =>
            AreaAnaliseLocal.Preparar(Pedido with { Retomar = retomar }, [], default, documentos ?? Estacao.Caminho);

        public void Dispose() { Pedido.Registro.Dispose(); Estacao.Dispose(); Raiz.Dispose(); }
    }

    [Fact]
    public void Copia_todas_as_revisoes_documentos_historicos_e_anexos_e_preserva_as_datas()
    {
        using var c = new Caso();
        string[] originais = [@"PROJETO\INICIAL\p.pdf", @"PROJETO\REVISÃO 02\p.pdf", @"DOCUMENTOS\p.pdf",
            @"DESPACHOS\01\documento.rpmvni", @"OUTROS\arquivo.dwg", "fora.txt"];
        foreach (var rel in originais) c.Gravar(rel, rel);
        string[] excluidos = [@".pmvni-ai\cache-pdf\pagina.json", @"PROJETO\~$em-uso.docx",
            @"RELATÓRIOS DE ANÁLISE\Entregas do Editor\antiga\relatorio.pdf"];
        foreach (var rel in excluidos) c.Gravar(rel, rel);
        Directory.CreateDirectory(Path.Combine(c.Origem, "pasta vazia"));
        File.WriteAllText(Path.Combine(c.Pedido.Registro.Pasta, TabelaDeModelos.NomeDoArquivo), "{}");
        var anexo = Path.Combine(c.Origem, originais[4]);
        var dados = File.ReadAllBytes(anexo);
        var pedido = c.Pedido with { Anexos = [new("entrada.dwg", originais[4], Convert.ToHexString(SHA256.HashData(dados)), dados.Length, false)] };
        var area = AreaAnaliseLocal.Preparar(pedido, [anexo], default, c.Estacao.Caminho);
        Assert.StartsWith(c.Estacao.Caminho, area.ProcessoLocal, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.GetFileName(c.Origem), Path.GetFileName(area.ProcessoLocal));
        foreach (var rel in originais)
        {
            var local = Path.Combine(area.ProcessoLocal, rel);
            Assert.Equal(rel, File.ReadAllText(local));
            Assert.Equal(File.GetLastWriteTimeUtc(Path.Combine(c.Origem, rel)), File.GetLastWriteTimeUtc(local));
        }
        foreach (var rel in excluidos) Assert.False(File.Exists(Path.Combine(area.ProcessoLocal, rel)));
        Assert.True(Directory.Exists(Path.Combine(area.ProcessoLocal, "pasta vazia")));
        foreach (var nome in new[] { "EXECUCAO.JSON", "ANEXOS.JSON", "PEDIDO.MD", TabelaDeModelos.NomeDoArquivo })
            Assert.Equal(File.ReadAllBytes(Path.Combine(c.Pedido.Registro.Pasta, nome)), File.ReadAllBytes(Path.Combine(area.RegistroLocal, nome)));
        Assert.Null(AreaAnaliseLocal.LocalExistente(c.Origem, c.Pedido.Registro.Pasta, c.Estacao.Caminho));
        c.Associar(area);
        Assert.Equal(area.ProcessoLocal, AreaAnaliseLocal.LocalExistente(c.Origem, c.Pedido.Registro.Pasta, c.Estacao.Caminho));
        Assert.Equal(area.Identificador, c.Preparar(true).Identificador);
        Assert.Equal(area.Identificador, AreaAnaliseLocal.AbrirAtiva(c.Origem, c.Pedido.Registro.Pasta, c.Estacao.Caminho).Identificador);
        c.Pedido.Registro.Atualizar(o => o["area_local_id"] = Guid.NewGuid().ToString("N"));
        Assert.Null(AreaAnaliseLocal.LocalExistente(c.Origem, c.Pedido.Registro.Pasta, c.Estacao.Caminho));
        Assert.Throws<IOException>(() => c.Preparar(true));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Recusa_mudanca_de_mesmo_tamanho_na_origem_ou_na_estacao(bool origem)
    {
        using var c = new Caso();
        c.Gravar("projeto.pdf", "123456");
        var area = c.Preparar();
        c.Associar(area);
        var alterado = Path.Combine(origem ? c.Origem : area.ProcessoLocal, "projeto.pdf");
        File.WriteAllText(alterado, "654321");
        Assert.Throws<IOException>(() => c.Preparar(true));
        Assert.Equal("654321", File.ReadAllText(alterado));
        Assert.Equal("123456", File.ReadAllText(Path.Combine(origem ? area.ProcessoLocal : c.Origem, "projeto.pdf")));
    }

    [Fact]
    public void Anexo_divergente_ou_peca_de_outro_processo_impede_a_copia()
    {
        using var a = new Caso();
        using var b = new Caso();
        var arquivo = a.Gravar("a.pdf", "peça");
        var pedido = a.Pedido with { Anexos = [new("a.pdf", "a.pdf", new string('0', 64), new FileInfo(arquivo).Length, false)] };
        Assert.Throws<IOException>(() => AreaAnaliseLocal.Preparar(pedido, [arquivo], default, a.Estacao.Caminho));
        Assert.Empty(Directory.EnumerateFiles(a.Estacao, "*", SearchOption.AllDirectories));
        Assert.Throws<IOException>(() => AreaAnaliseLocal.Preparar(a.Pedido, [b.Gravar("b.pdf", "outro")], default, a.Estacao.Caminho));
        var um = a.Preparar();
        var dois = b.Preparar(false, a.Estacao);
        Assert.NotEqual(um.ProcessoLocal, dois.ProcessoLocal);
        Assert.NotEqual(um.Identificador, dois.Identificador);
    }

    [Fact]
    public void Inicio_pendente_exige_registro_novo_sem_etapa_e_cancelamento_preserva_origem()
    {
        using var c = new Caso();
        var arquivo = c.Gravar("entrada.pdf", "íntegra");
        Assert.Throws<OperationCanceledException>(() => AreaAnaliseLocal.Preparar(c.Pedido, [arquivo], new(true), c.Estacao.Caminho));
        Assert.Empty(Directory.EnumerateFileSystemEntries(c.Estacao));
        var area = c.Preparar(true);
        Assert.True(area.InicioPendente);
        File.WriteAllText(Path.Combine(c.Pedido.Registro.Pasta, "continuidade.json"), "{}");
        Assert.ThrowsAny<IOException>(() => c.Preparar(true));
        Assert.Equal("íntegra", File.ReadAllText(arquivo));
        Assert.True(Directory.Exists(area.ProcessoLocal));
    }

    [Fact]
    public void Perda_de_vinculo_com_etapa_na_estacao_nao_autoriza_inicio_do_zero()
    {
        using var c = new Caso();
        var area = c.Preparar();
        var prova = Path.Combine(area.RegistroLocal, "continuidade.json");
        File.WriteAllText(prova, "etapas já executadas");
        Assert.Throws<IOException>(() => c.Preparar(true));
        Assert.Equal("etapas já executadas", File.ReadAllText(prova));
        Assert.Empty(Directory.GetDirectories(Path.GetDirectoryName(area.ProcessoLocal)!, "reserva-*"));
    }

    [Fact]
    public void Restauracao_de_checkpoint_no_mesmo_caminho_tambem_exige_validacao_do_kit()
    {
        using var c = new Caso();
        var area = c.Preparar();
        c.Associar(area);
        RecuperacaoAnaliseLocal.Salvar(c.Origem, area.ProcessoLocal, c.Id, "analise");
        var reserva = Path.Combine(c.Estacao, "estacao-ausente");
        Directory.Move(area.ProcessoLocal, reserva);
        var restaurada = c.Preparar(true);
        Assert.Equal(area.Identificador, restaurada.Identificador);
        Assert.Equal(restaurada.ProcessoLocal, restaurada.LocalAnterior);
        Assert.Throws<IOException>(() => AreaAnaliseLocal.AbrirAtiva(c.Origem, c.Pedido.Registro.Pasta, c.Estacao.Caminho));
        restaurada.ConfirmarTransporte();
        Assert.Equal(area.Identificador, AreaAnaliseLocal.AbrirAtiva(c.Origem, c.Pedido.Registro.Pasta, c.Estacao.Caminho).Identificador);
    }

    [Fact]
    public void Recusa_juncao_entre_as_pecas_e_destino_redirecionado()
    {
        using var c = new Caso();
        var alvo = Directory.CreateDirectory(Path.Combine(c.Estacao, "alvo")).FullName;
        var juncao = Path.Combine(c.Origem, "fora");
        Assert.True(Ambiente.CriarJuncao(juncao, alvo));
        try { Assert.Throws<IOException>(() => c.Preparar()); }
        finally { Directory.Delete(juncao); }
        var documentos = Path.Combine(c.Estacao, "Documentos");
        Assert.True(Ambiente.CriarJuncao(documentos, alvo));
        try { Assert.Throws<IOException>(() => c.Preparar(false, documentos)); }
        finally { Directory.Delete(documentos); }
        Assert.Empty(Directory.EnumerateFileSystemEntries(alvo));
    }

    [Fact]
    public void Retomada_legada_preserva_original_e_reconstroi_tentativa_parcial()
    {
        using var c = new Caso();
        c.Pedido.Registro.Atualizar(o => o.Remove("area_local_solicitada"));
        c.Gravar("original.pdf", "caso");
        File.WriteAllText(Path.Combine(c.Pedido.Registro.Pasta, "EXECUTOR.LOCK"), " ");
        var estado = JsonSerializer.Serialize(new { editor_execution = c.Id, @case = c.Origem });
        c.Gravar(@".pmvni-ai\analise\engine_state.json", estado);
        var area = AreaAnaliseLocal.Preparar(c.Pedido with { Retomar = true, Provedor = Provedor.Gemini },
            [], default, c.Estacao.Caminho);
        Assert.True(area.RetomadaLegada);
        Assert.False(area.InicioPendente);
        Assert.Equal(c.Origem, area.LocalAnterior);
        Assert.Equal("analise", area.ControleAnterior);
        var arquivo = Path.Combine(area.ProcessoLocal, @".pmvni-ai\analise\engine_state.json");
        File.WriteAllText(arquivo, "transporte interrompido");
        var reaberta = c.Preparar(true);
        Assert.Equal(estado, File.ReadAllText(arquivo));
        Assert.Equal(estado, File.ReadAllText(Path.Combine(c.Origem, @".pmvni-ai\analise\engine_state.json")));
        Assert.Equal(c.Origem, reaberta.LocalAnterior);
        reaberta.ConfirmarTransporte();
        c.Associar(reaberta);
        Assert.Null(c.Preparar(true).LocalAnterior);
    }

    [Fact]
    public void Troca_de_estacao_exige_checkpoint_da_execucao_e_reidrata_a_geracao_mais_recente()
    {
        using var c = new Caso();
        c.Gravar("entrada.pdf", "caso");
        var a = c.Preparar();
        c.Associar(a);
        var docB = Path.Combine(c.Estacao, "EstacaoB");
        Assert.Throws<IOException>(() => c.Preparar(true, docB));
        string Prova(AreaAnaliseLocal area) => Path.Combine(area.RegistroLocal, "prova.txt");
        File.WriteAllText(Prova(a), "preparo");
        RecuperacaoAnaliseLocal.Salvar(c.Origem, a.ProcessoLocal, c.Id, "analise");
        var b = AreaAnaliseLocal.Preparar(c.Pedido with { Provedor = Provedor.Claude, Retomar = true }, [], default, docB);
        Assert.Equal(a.Identificador, b.Identificador);
        Assert.Equal(a.ProcessoLocal, b.LocalAnterior);
        Assert.Equal("analise", b.ControleAnterior);
        b.ConfirmarTransporte();
        File.WriteAllText(Prova(b), "auditoria");
        RecuperacaoAnaliseLocal.Salvar(c.Origem, b.ProcessoLocal, c.Id, "analise");
        var atual = c.Preparar(true);
        Assert.Equal(b.ProcessoLocal, atual.LocalAnterior);
        Assert.Equal("auditoria", File.ReadAllText(Prova(atual)));
        var anterior = Assert.Single(Directory.GetDirectories(Path.GetDirectoryName(a.ProcessoLocal)!, "reserva-*"));
        Assert.Equal("preparo", File.ReadAllText(Path.Combine(anterior, Path.GetRelativePath(a.ProcessoLocal, Prova(a)))));
    }
}
