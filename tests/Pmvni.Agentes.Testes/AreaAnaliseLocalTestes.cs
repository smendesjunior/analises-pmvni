using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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
    public void Transporte_categorizado_preserva_o_escopo_do_checkpoint_e_confere_as_pecas_selecionadas()
    {
        using var c = new Caso();
        var anexo = c.Gravar(@"PROJETO\INICIAL\P01.pdf", "prancha anexada");
        var automatico = c.Gravar(@"DOCUMENTOS\BCI\ficha.pdf", "cadastro selecionado");
        var ignorado = c.Gravar(@"PROJETO\REVISÃO 01\P01.pdf", "ignorar");
        var despacho = c.Gravar(@"DESPACHO 02 - 2099-02-02\Despacho de correção.pdf", "pendências da análise anterior");
        var antigo = c.Gravar(@"DESPACHO 01 - 2099-01-01\Despacho de correção.pdf", "versão antiga");
        var minuta = c.Gravar(@"DESPACHO 03 - 2099-03-03\Despacho minuta.docx", "ainda não emitido");
        var bytes = File.ReadAllBytes(anexo);
        var item = new ArquivoArquivado(anexo, Path.GetRelativePath(c.Origem, anexo),
            Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length, false) { Categoria = "projetos" };
        var pedido = c.Pedido with { Anexos = [item] };
        pedido.Registro.GravarAnexos(pedido.Anexos);
        string NaArea(AreaAnaliseLocal area, string original) =>
            Path.Combine(area.ProcessoLocal, Path.GetRelativePath(c.Origem, original));
        // Uma tentativa de ler a versão excluída falha por compartilhamento no Windows.
        using var bloqueado = File.Open(ignorado, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var a = AreaAnaliseLocal.Preparar(pedido, [anexo, automatico], default, c.Estacao.Caminho);
        c.Associar(a);
        Assert.False(File.Exists(NaArea(a, ignorado)));
        Assert.True(File.Exists(NaArea(a, despacho)));
        Assert.False(File.Exists(NaArea(a, antigo)));
        Assert.False(File.Exists(NaArea(a, minuta)));
        Assert.Equal(File.ReadAllBytes(automatico), File.ReadAllBytes(NaArea(a, automatico)));
        Assert.Equal(bytes, File.ReadAllBytes(NaArea(a, anexo)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(pedido.Registro.Pasta, "anexos.json")),
            File.ReadAllBytes(Path.Combine(a.RegistroLocal, "anexos.json")));
        var novo = c.Gravar(@"DOCUMENTOS\BCI\novo.pdf", "entrou depois do início");
        a.ConferirOriginais();
        var retomada = pedido with { Retomar = true };
        Assert.Equal(a.Identificador,
            AreaAnaliseLocal.Preparar(retomada, [anexo, automatico, novo], default, c.Estacao.Caminho).Identificador);
        RecuperacaoAnaliseLocal.Salvar(c.Origem, a.ProcessoLocal, c.Id, "analise");
        var b = AreaAnaliseLocal.Preparar(retomada, [anexo, automatico, novo], default, Path.Combine(c.Estacao, "B"));
        Assert.Equal(a.Identificador, b.Identificador);
        Assert.Equal(File.ReadAllBytes(NaArea(a, anexo)), File.ReadAllBytes(NaArea(b, anexo)));
        Assert.True(File.Exists(NaArea(b, automatico)));
        Assert.Equal(File.ReadAllBytes(despacho), File.ReadAllBytes(NaArea(b, despacho)));
        Assert.False(File.Exists(NaArea(b, novo)));
        Assert.False(File.Exists(NaArea(b, ignorado)));
        b.ConfirmarTransporte();
        File.AppendAllText(automatico, "alterado");
        Assert.Throws<IOException>(() => b.ConferirOriginais());
        Assert.Throws<IOException>(() =>
            AreaAnaliseLocal.Preparar(retomada, [anexo, automatico], default, Path.Combine(c.Estacao, "C")));
    }

    [Fact]
    public void Memoria_de_calculo_abre_parcela_e_recorte_da_execucao_e_recusa_substituicao()
    {
        using var c = new Caso();
        const string relativo = @"PROJETO\A-04.pdf";
        var original = c.Gravar(relativo, "%PDF-ensaio-da-parcela");
        var area = c.Preparar();
        c.Associar(area);
        area = AreaAnaliseLocal.AbrirAtiva(c.Origem, c.Pedido.Registro.Pasta, c.Estacao.Caminho);
        string Hash(string caminho) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(caminho)));
        var hash = Hash(original);
        var recorte = Path.Combine(".pmvni-ai", "cache-pdf", "v1", hash, "trecho.png");
        var imagem = Path.Combine(area.ProcessoLocal, recorte);
        Directory.CreateDirectory(Path.GetDirectoryName(imagem)!);
        File.WriteAllBytes(imagem, [137, 80, 78, 71]);
        var pasta = Directory.CreateDirectory(Path.Combine(area.ProcessoLocal, ResultadoAnalise.PastaControle(Provedor.ChatGpt), "local"));
        var controle = Path.Combine(pasta.FullName, ConferenciaNumerica.ArquivoControle);
        var dado = new { id_dado = "unico", peca = "DOC-04", pagina = 2, rotulo = "Projeção", valor = "400", unidade = "m²",
            arquivo = relativo, sha256 = hash, recorte, recurso_sha256 = Hash(imagem), regiao = "quadro superior" };
        var linhas = new object[] {
            new { indice = "to", situacao = "CONFERE", parcelas = new { area_projecao = new[] { "unico", "sem-vinculo" } } },
            new { indice = "tp", situacao = "INSUFICIENTE" }, new { indice = "ca", situacao = "INSUFICIENTE" } };
        void GravarDados(object[] dados) => File.WriteAllText(controle, JsonSerializer.Serialize(new
            { versao = 1, execucao = c.Id, rota = "codex", fase = "montagem", linhas, dados_declarados = dados }));
        ConferenciaNumerica Painel() => ConferenciaNumerica.Ler(area.ProcessoLocal, Provedor.ChatGpt, area.RegistroLocal)!;
        GravarDados([dado]);
        var fonte = Assert.Single(Painel().Evidencias);
        Assert.Equal(("to", "area_projecao", "DOC-04", 2, "400 m²"), (fonte.Indice, fonte.Parcela, fonte.Peca, fonte.Pagina, fonte.Valor));
        using (var pdf = area.AbrirEvidencia(fonte, false)) Assert.Equal(File.ReadAllBytes(original), LerTudo(pdf));
        using (var png = area.AbrirEvidencia(fonte, true)) Assert.Equal(File.ReadAllBytes(imagem), LerTudo(png));
        using (var legado = area.AbrirEvidencia(fonte with { Arquivo = "" }, false)) Assert.Equal(File.ReadAllBytes(original), LerTudo(legado));
        Assert.Throws<InvalidDataException>(() => area.AbrirEvidencia(fonte with { Arquivo = original }, false));
        Assert.Throws<InvalidDataException>(() => area.AbrirEvidencia(fonte with { Recorte = @"..\fora.png" }, true));
        Assert.Throws<InvalidDataException>(() => area.AbrirEvidencia(fonte with { Recorte = recorte.Replace(hash, "outra-peca") }, true));
        Assert.Throws<InvalidDataException>(() => area.AbrirEvidencia(fonte with { Pagina = 0 }, false));
        File.AppendAllText(imagem, "alterada");
        Assert.Throws<InvalidDataException>(() => area.AbrirEvidencia(fonte, true));
        File.AppendAllText(Path.Combine(area.ProcessoLocal, relativo), "alterada");
        Assert.Throws<InvalidDataException>(() => area.AbrirEvidencia(fonte, false));
        GravarDados([dado, dado]);
        Assert.Empty(Painel().Evidencias);
        Assert.Equal(hash, Hash(original));

        static byte[] LerTudo(Stream fluxo)
        {
            using var memoria = new MemoryStream();
            fluxo.CopyTo(memoria);
            return memoria.ToArray();
        }
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
    public void Conferencia_recupera_copia_apos_recusa_de_leitura_e_preserva_original()
    {
        using var c = new Caso();
        var original = c.Gravar("projeto.pdf", "conteúdo conferido");
        var area = c.Preparar();
        c.Associar(area);
        var copia = new FileInfo(Path.Combine(area.ProcessoLocal, "projeto.pdf"));
        int leituras = 0;
        area.ConferirOriginais(abrirCopia: caminho =>
        {
            if (caminho == copia.FullName && ++leituras == 1) throw new UnauthorizedAccessException();
            return File.OpenRead(caminho);
        });
        Assert.Equal(2, leituras);
        Assert.Equal(area.Identificador, c.Preparar(true).Identificador);
        Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(copia.FullName));
        Assert.Equal(File.GetLastWriteTimeUtc(original), File.GetLastWriteTimeUtc(copia.FullName));
    }

    [Fact]
    public void Reparo_da_copia_com_leitura_negada_pelo_Windows_reabre_os_bytes_originais()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var c = new Caso();
        var original = c.Gravar("prancha.pdf", "prancha de ensaio");
        var area = c.Preparar();
        var copia = new FileInfo(Path.Combine(area.ProcessoLocal, "prancha.pdf"));
        using var usuario = WindowsIdentity.GetCurrent();
        var anterior = copia.GetAccessControl();
        var bloqueada = copia.GetAccessControl();
        bloqueada.AddAccessRule(new FileSystemAccessRule(usuario.User!, FileSystemRights.ReadData, AccessControlType.Deny));
        copia.SetAccessControl(bloqueada);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => File.ReadAllBytes(copia.FullName));
            area.ConferirOriginais();
            Assert.Equal(File.ReadAllBytes(original), File.ReadAllBytes(copia.FullName));
            Assert.Equal(File.GetLastWriteTimeUtc(original), File.GetLastWriteTimeUtc(copia.FullName));
        }
        finally { copia.SetAccessControl(anterior); }
    }

    [Fact]
    public void Troca_da_implantacao_antes_da_publicacao_preserva_as_pecas_e_a_entrega_local()
    {
        using var c = new Caso();
        var implantacao = c.Gravar("implantacao.pdf", "revisão A");
        c.Gravar("cortes.pdf", "cortes inalterados");
        var area = c.Preparar();
        c.Associar(area);
        var fim = RecuperacaoAnaliseLocalTestes.Produto(area.ProcessoLocal, id: c.Id);
        var hashPdf = SHA256.HashData(File.ReadAllBytes(fim.Pdf!));
        FimAnalise Publicar() => RecuperacaoAnaliseLocal.Publicar(c.Origem, area.ProcessoLocal, c.Id,
            "analise", fim, () => area.ConferirOriginais());

        File.WriteAllText(implantacao, "revisão B");
        Assert.Contains("implantacao.pdf", Assert.Throws<IOException>(() => Publicar()).Message);
        Assert.Null(RecuperacaoAnaliseLocal.EntregaPublicada(c.Origem, c.Id));
        Assert.Equal("revisão A", File.ReadAllText(Path.Combine(area.ProcessoLocal, "implantacao.pdf")));
        Assert.Equal("cortes inalterados", File.ReadAllText(Path.Combine(area.ProcessoLocal, "cortes.pdf")));
        Assert.Equal(hashPdf, SHA256.HashData(File.ReadAllBytes(fim.Pdf!)));

        File.WriteAllText(implantacao, "revisão A");
        var publicada = Publicar();
        Assert.Equal(hashPdf, SHA256.HashData(File.ReadAllBytes(publicada.Pdf!)));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Retoma_de_documentos_no_appdata_com_progresso_local(bool comCheckpoint)
    {
        using var c = new Caso();
        c.Gravar("projeto.pdf", "entrada original");
        var documentos = Path.Combine(c.Estacao.Caminho, "Documentos");
        var appData = Path.Combine(c.Estacao.Caminho, "AppData");
        var antiga = c.Preparar(false, documentos);
        c.Associar(antiga);
        if (comCheckpoint) RecuperacaoAnaliseLocal.Salvar(c.Origem, antiga.ProcessoLocal, c.Id, "analise");
        File.WriteAllText(Path.Combine(antiga.RegistroLocal, "prova.txt"), "etapa validada");
        var nova = AreaAnaliseLocal.Preparar(c.Pedido with { Retomar = true }, [], default, appData, documentos);
        Assert.StartsWith(appData, nova.ProcessoLocal);
        Assert.Equal(antiga.ProcessoLocal, nova.LocalAnterior);
        Assert.Equal(antiga.Identificador, nova.Identificador);
        foreach (var registro in new[] { antiga.RegistroLocal, nova.RegistroLocal })
            Assert.Equal("etapa validada", File.ReadAllText(Path.Combine(registro, "prova.txt")));
        // Uma interrupção pode deixar parte dos caminhos traduzidos na nova área.
        File.WriteAllText(Path.Combine(nova.RegistroLocal, "prova.txt"), "transporte interrompido");
        var repetida = AreaAnaliseLocal.Preparar(c.Pedido with { Retomar = true }, [], default, appData, documentos);
        Assert.Equal("etapa validada", File.ReadAllText(Path.Combine(repetida.RegistroLocal, "prova.txt")));
        repetida.ConfirmarTransporte();
        Assert.Null(c.Preparar(true, appData).LocalAnterior);
        Assert.True(RecuperacaoAnaliseLocal.GeracaoAtual(c.Origem, repetida.ProcessoLocal, c.Id));
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
