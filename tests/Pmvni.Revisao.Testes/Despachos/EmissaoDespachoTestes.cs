using System.Text.Json.Nodes;
using Xunit;

namespace Pmvni.Revisao.Testes.DespachosTestes;

public sealed class EmissaoDespachoTestes
{
    static readonly DateOnly Hoje = new(2099, 3, 10);
    static readonly byte[] PdfVerdana = PdfFicticio.Criar(2, "BAAAAA+Verdana", "CAAAAA+Verdana-Bold");

    /// <summary>Tudo o que uma emissão precisa, montado sobre a árvore fictícia (kit lido no lugar).</summary>
    internal sealed class Cena : IDisposable
    {
        public PastaTeste Pasta { get; }
        public ArvoreFicticia Arvore { get; }
        public ModeloDespacho Modelo { get; }
        public TextosFixos Textos { get; }
        public EtapaDespacho Etapa { get; }
        public RascunhoDespacho Rascunho { get; }
        public DespachoFinal Final { get; }
        public string Trabalho { get; }

        public Cena(string nome, TipoDespacho t = TipoDespacho.Correcao, Func<ArvoreFicticia, EtapaDespacho>? etapa = null)
        {
            Pasta = Ambiente.PastaDeTeste(nome);
            Arvore = RelatorioFicticio.Arvore(Pasta);
            var kit = Ambiente.ExigirKit();
            Modelo = RegistroModelos.Ler(kit).Modelos.Single(m => m.Tipo == t);
            Textos = TextosFixos.Ler(kit, t);
            Etapa = etapa?.Invoke(Arvore) ?? Etapas.Nova(Arvore.Processo.Pasta, Hoje);
            Rascunho = ConteudoDespacho.Montar(t, Arvore.Pacote.Conteudo.Projeto,
                PreenchimentoDocx.LerModelo(File.ReadAllBytes(Modelo.Caminho), t), Etapa);
            if (t == TipoDespacho.Aprovacao)
            {
                Rascunho.DefinirIntervencao(Intervencao.ObraNova, true);
                Rascunho.DefinirArea("210,52");
            }
            Final = Rascunho.Congelar(Textos);
            Trabalho = Path.Combine(Pasta, "trabalho", Guid.NewGuid().ToString("N"));
        }

        public Preparacao Preparar(IConversorPdf conv) => EmissaoDespacho.Preparar(Final, Modelo, Trabalho, conv, CancellationToken.None);

        public OrigemDespacho Origem => new(Arvore.Processo, Arvore.CaminhoPacote, Arvore.Pacote.Disco.Sha256,
            DocxFicticio.Sha(DadosEfetivos.Gerar(Arvore.Pacote.Conteudo.Projeto).Json), Arvore.Pacote.Conteudo.Historico.Count, "0.1.0-teste",
            Array.Empty<string>());

        public Entrega Gravar(Preparacao p, IReadOnlyList<int>? paginas) =>
            EmissaoDespacho.Gravar(p, Rascunho, Final, Modelo, Textos, Etapa, Origem, paginas, "LibreOffice teste", Ambiente.Contexto("fulano"));

        /// <summary>Gravar com uma falha simulada antes de mover o arquivo final que a função recusar.</summary>
        public Entrega GravarCom(Preparacao p, IReadOnlyList<int>? paginas, Action<string> antesDeMover) =>
            EmissaoDespacho.Gravar(p, Rascunho, Final, Modelo, Textos, Etapa, Origem, paginas, "LibreOffice teste", Ambiente.Contexto("fulano"), antesDeMover);

        public void Dispose() => Pasta.Dispose();
    }

    /// <summary>Arquivos (caminho relativo + sha) e pastas da árvore: para provar que nada ficou pela metade.</summary>
    static (IReadOnlyList<(string, string)> Arquivos, string[] Pastas) Tudo(string raiz) =>
        (RelatorioFicticio.Retrato(raiz),
         Directory.GetDirectories(raiz, "*", SearchOption.AllDirectories).Select(d => Path.GetRelativePath(raiz, d)).Order(StringComparer.Ordinal).ToArray());

    static void FalharEm(string final, string sufixo)
    {
        if (final.EndsWith(sufixo, StringComparison.OrdinalIgnoreCase)) throw new IOException("Disco cheio (simulado).");
    }

    [Fact]
    public void falha_ao_gravar_o_recibo_desfaz_tudo()
    {
        using var c = new Cena("emissao-falha-recibo");
        var p = c.Preparar(ConversorFalso.Com(PdfVerdana));
        var antes = Tudo(c.Arvore.Raiz);

        var e = Assert.Throws<IOException>(() => c.GravarCom(p, new[] { 1, 2 }, f => FalharEm(f, ".recibo-editor.json")));

        Assert.Equal("Disco cheio (simulado).", e.Message);
        var depois = Tudo(c.Arvore.Raiz);
        Assert.Equal(antes.Arquivos, depois.Arquivos);
        Assert.Equal(antes.Pastas, depois.Pastas);
        Assert.False(Directory.Exists(c.Etapa.Pasta));
    }

    [Fact]
    public void segundo_arquivo_que_nao_grava_desfaz_tudo()
    {
        using var c = new Cena("emissao-falha-pdf");
        var p = c.Preparar(ConversorFalso.Com(PdfVerdana));
        var antes = Tudo(c.Arvore.Raiz);

        Assert.Throws<UnauthorizedAccessException>(() => c.GravarCom(p, new[] { 1, 2 }, f =>
        {
            if (f.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Acesso negado (simulado).");
        }));

        var depois = Tudo(c.Arvore.Raiz);
        Assert.Equal(antes.Arquivos, depois.Arquivos);
        Assert.Equal(antes.Pastas, depois.Pastas);
    }

    [Fact]
    public void falha_na_etapa_existente_mantem_a_pasta_e_o_que_ela_tinha()
    {
        using var c = new Cena("emissao-falha-existente", etapa: a => Etapas.Existentes(a.Processo.Pasta).Single(x => x.Numero == 2));
        var p = c.Preparar(ConversorFalso.Com(PdfVerdana));
        var antes = Tudo(c.Arvore.Raiz);

        Assert.Throws<IOException>(() => c.GravarCom(p, new[] { 1, 2 }, f => FalharEm(f, ".recibo-editor.json")));

        var depois = Tudo(c.Arvore.Raiz);
        Assert.Equal(antes.Arquivos, depois.Arquivos);
        Assert.Equal(antes.Pastas, depois.Pastas);
        Assert.True(Directory.Exists(c.Etapa.Pasta));
    }

    [Fact]
    public void conversor_que_lanca_vira_despacho_sem_pdf()
    {
        using var c = new Cena("emissao-conversor-lanca");

        var p = c.Preparar(new ConversorFalso(_ => throw new IOException("arquivo preso")));

        Assert.Empty(p.Erros);
        Assert.Null(p.Pdf);
        Assert.Equal("CONVERSAO_FALHOU", p.SemPdfMotivo);
        Assert.Equal("O LibreOffice não conseguiu gerar o PDF (arquivo preso). O despacho será gravado só em Word (DOCX); use Despachos › Gerar PDF para tentar de novo.",
            p.MensagemSemPdf);
    }

    [Fact]
    public void pasta_de_trabalho_que_nao_grava_vira_erro()
    {
        using var c = new Cena("emissao-trabalho-ruim");
        Directory.CreateDirectory(Path.GetDirectoryName(c.Trabalho)!);
        File.WriteAllText(c.Trabalho, "um arquivo no lugar da pasta");

        var p = c.Preparar(ConversorFalso.Com(PdfVerdana));

        var erro = Assert.Single(p.Erros);
        Assert.StartsWith("Não foi possível gravar o despacho na pasta de trabalho (", erro);
    }

    [Fact]
    public void gerar_pdf_recusa_docx_que_nao_confere_com_o_recibo()
    {
        using var c = new Cena("emissao-pdf-docx-editado");
        var e = c.Gravar(c.Preparar(new ConversorAusente()), null);
        const string msg = "O DOCX não é mais o que o editor gravou (não confere com o recibo); o PDF não é gerado por aqui. Gere um novo despacho.";

        // Outro nome (o recibo registra o nome do DOCX que o editor gravou).
        var outro = Path.Combine(e.Pasta, "outro.docx");
        File.Copy(e.Docx, outro);
        File.Copy(e.Recibo, ReciboDespacho.CaminhoDe(outro));
        var conv = ConversorFalso.Com(PdfVerdana);
        Assert.Equal(new[] { msg }, EmissaoDespacho.PrepararPdf(outro, Path.Combine(c.Pasta, "t1"), conv, CancellationToken.None).Erros);

        // Um byte mudado (o DOCX editado no Word depois de gravado).
        var bytes = File.ReadAllBytes(e.Docx);
        bytes[^1] ^= 0x01;
        File.WriteAllBytes(e.Docx, bytes);
        var p = EmissaoDespacho.PrepararPdf(e.Docx, Path.Combine(c.Pasta, "t2"), conv, CancellationToken.None);

        Assert.Equal(new[] { msg }, p.Erros);
        Assert.Equal(0, conv.Chamadas);
        Assert.False(File.Exists(Path.ChangeExtension(e.Docx, ".pdf")));
    }

    [Fact]
    public void gerar_pdf_que_falha_na_troca_do_recibo_apaga_o_pdf()
    {
        using var c = new Cena("emissao-pdf-troca");
        var e = c.Gravar(c.Preparar(new ConversorAusente()), null);
        var reciboAntes = File.ReadAllBytes(e.Recibo);
        var p = EmissaoDespacho.PrepararPdf(e.Docx, Path.Combine(c.Pasta, "trabalho-pdf"), ConversorFalso.Com(PdfVerdana), CancellationToken.None);

        Assert.Throws<IOException>(() => EmissaoDespacho.GravarPdf(p, e.Docx, new[] { 1, 2 }, "LibreOffice teste", Ambiente.Contexto(),
            _ => throw new IOException("Recibo preso (simulado).")));

        Assert.False(File.Exists(Path.ChangeExtension(e.Docx, ".pdf")));
        Assert.Equal(reciboAntes, File.ReadAllBytes(e.Recibo));
        Assert.Equal(2, Directory.GetFiles(e.Pasta).Length);
    }

    [Fact]
    public void gerar_pdf_que_falha_ao_montar_o_recibo_apaga_o_pdf()
    {
        using var c = new Cena("emissao-pdf-recibo-montar");
        var e = c.Gravar(c.Preparar(new ConversorAusente()), null);
        var reciboAntes = File.ReadAllBytes(e.Recibo);
        var p = EmissaoDespacho.PrepararPdf(e.Docx, Path.Combine(c.Pasta, "trabalho-pdf"), ConversorFalso.Com(PdfVerdana), CancellationToken.None);
        // O relógio falha só ao registrar as páginas conferidas (depois de o PDF já ter sido movido para a pasta da etapa).
        var quebrado = new ContextoUsuario("fulano", () => throw new IOException("Relógio indisponível (simulado)."),
            System.Security.Cryptography.RandomNumberGenerator.Create());

        Assert.Throws<IOException>(() => EmissaoDespacho.GravarPdf(p, e.Docx, new[] { 1, 2 }, "LibreOffice teste", quebrado));

        Assert.False(File.Exists(Path.ChangeExtension(e.Docx, ".pdf")));
        Assert.Equal(reciboAntes, File.ReadAllBytes(e.Recibo));
        Assert.Equal(2, Directory.GetFiles(e.Pasta).Length);
    }

    [Fact]
    public void preparar_gera_docx_e_pdf_so_na_pasta_de_trabalho()
    {
        using var c = new Cena("emissao-preparar");
        var antes = RelatorioFicticio.Retrato(c.Arvore.Raiz);

        var p = c.Preparar(ConversorFalso.Com(PdfVerdana));

        Assert.Empty(p.Erros);
        Assert.Equal(2, p.Paginas);
        Assert.Equal(new[] { "Verdana", "Verdana-Bold" }, p.Fontes);
        Assert.Null(p.SemPdfMotivo);
        Assert.Null(p.MensagemSemPdf);
        Assert.Equal(PdfVerdana, p.Pdf);
        Assert.Equal(c.Trabalho, p.PastaTrabalho);
        Assert.Equal(p.Docx, File.ReadAllBytes(Path.Combine(c.Trabalho, "despacho.docx")));
        Assert.Empty(ValidacaoDespacho.Comparar(File.ReadAllBytes(c.Modelo.Caminho), p.Docx));
        Assert.Equal(antes, RelatorioFicticio.Retrato(c.Arvore.Raiz));
    }

    [Fact]
    public void sem_libreoffice_fica_so_o_docx()
    {
        using var c = new Cena("emissao-sem-lo");

        var p = c.Preparar(new ConversorAusente());

        Assert.Empty(p.Erros);
        Assert.Null(p.Pdf);
        Assert.Null(p.Paginas);
        Assert.Equal("SEM_LIBREOFFICE", p.SemPdfMotivo);
        Assert.Equal("O LibreOffice não está instalado neste computador. O despacho será gravado só em Word (DOCX). Para o PDF, rode o instalador do kit (INSTALAR_KIT_PMVNI.cmd) e use Despachos › Gerar PDF.",
            p.MensagemSemPdf);
        Assert.NotEmpty(p.Docx);
    }

    [Fact]
    public void conversao_que_falha_fica_so_o_docx()
    {
        using var c = new Cena("emissao-falha");

        var p = c.Preparar(new ConversorFalso(_ => new ResultadoConversao(null, "CONVERSAO_FALHOU", "código 1")));
        var t = c.Preparar(new ConversorFalso(_ => new ResultadoConversao(null, "TEMPO_ESGOTADO", null)));

        Assert.Equal("CONVERSAO_FALHOU", p.SemPdfMotivo);
        Assert.Equal("O LibreOffice não conseguiu gerar o PDF (código 1). O despacho será gravado só em Word (DOCX); use Despachos › Gerar PDF para tentar de novo.",
            p.MensagemSemPdf);
        Assert.Equal("TEMPO_ESGOTADO", t.SemPdfMotivo);
        Assert.Equal("O LibreOffice não conseguiu gerar o PDF (tempo esgotado). O despacho será gravado só em Word (DOCX); use Despachos › Gerar PDF para tentar de novo.",
            t.MensagemSemPdf);
    }

    [Fact]
    public void pdf_sem_verdana_nao_e_entregue()
    {
        using var c = new Cena("emissao-sem-verdana");

        var p = c.Preparar(ConversorFalso.Com(PdfFicticio.Criar(1, "ArialMT")));

        Assert.Empty(p.Erros);
        Assert.Null(p.Pdf);
        Assert.Equal("SEM_VERDANA", p.SemPdfMotivo);
        Assert.Equal("O PDF saiu sem a fonte Verdana (fontes: ArialMT); será gravado só o DOCX.", p.MensagemSemPdf);
    }

    [Fact]
    public void docx_alterado_durante_a_conversao_e_erro()
    {
        using var c = new Cena("emissao-docx-mudou");

        var p = c.Preparar(new ConversorFalso(docx =>
        {
            File.WriteAllBytes(docx, DocxFicticio.Correcao());
            return new ResultadoConversao(PdfVerdana, null, null);
        }));

        Assert.Equal(new[] { "O DOCX mudou durante a conversão; gere o despacho de novo." }, p.Erros);
        Assert.Throws<InvalidOperationException>(() => c.Gravar(p, new[] { 1, 2 }));
    }

    [Fact]
    public void modelo_alterado_depois_de_aberto_e_erro()
    {
        using var c = new Cena("emissao-modelo-mudou");
        var conv = ConversorFalso.Com(PdfVerdana);

        var p = EmissaoDespacho.Preparar(c.Final, c.Modelo with { Sha256 = new string('a', 64) }, c.Trabalho, conv, CancellationToken.None);

        Assert.Equal(new[] { "O modelo do kit mudou depois de aberto; abra o Novo despacho de novo." }, p.Erros);
        Assert.Equal(0, conv.Chamadas);
    }

    [Fact]
    public void gravar_exige_todas_as_paginas()
    {
        using var c = new Cena("emissao-paginas");
        var p = c.Preparar(ConversorFalso.Com(PdfVerdana));
        var antes = RelatorioFicticio.Retrato(c.Arvore.Raiz);

        Assert.Equal("Confira as 2 páginas do PDF antes de gravar.", Assert.Throws<ArgumentException>(() => c.Gravar(p, new[] { 1 })).Message);
        Assert.Equal("Confira as 2 páginas do PDF antes de gravar.", Assert.Throws<ArgumentException>(() => c.Gravar(p, new[] { 2, 1 })).Message);

        var um = c.Preparar(ConversorFalso.Com(PdfFicticio.Criar(1, "Verdana")));
        Assert.Equal("Confira a página do PDF antes de gravar.", Assert.Throws<ArgumentException>(() => c.Gravar(um, null)).Message);
        Assert.Equal(antes, RelatorioFicticio.Retrato(c.Arvore.Raiz));
        Assert.False(Directory.Exists(c.Etapa.Pasta));
    }

    [Fact]
    public void gravar_cria_a_etapa_nova_com_os_tres_arquivos()
    {
        using var c = new Cena("emissao-gravar");
        var p = c.Preparar(ConversorFalso.Com(PdfVerdana));
        var antes = RelatorioFicticio.Retrato(c.Arvore.Raiz);

        var e = c.Gravar(p, new[] { 1, 2 });

        var pasta = Path.Combine(c.Arvore.Processo.Pasta, "DESPACHO 03 - 2099-03-10");
        const string nome = "DESPACHO_CORRECAO_1_2099_FULANO_DE_EXEMPLO_FICTICIO";
        Assert.Equal(new Entrega(pasta, Path.Combine(pasta, nome + ".DOCX"), Path.Combine(pasta, nome + ".PDF"), Path.Combine(pasta, nome + ".RECIBO-EDITOR.JSON")), e);
        Assert.Equal(new[] { nome + ".DOCX", nome + ".PDF", nome + ".RECIBO-EDITOR.JSON" },
            Directory.GetFiles(pasta).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.Equal(p.Docx, File.ReadAllBytes(e.Docx));
        Assert.Equal(PdfVerdana, File.ReadAllBytes(e.Pdf!));
        Assert.All(Directory.GetFiles(pasta), f => Assert.False(File.GetAttributes(f).HasFlag(FileAttributes.Hidden)));
        Assert.False(Directory.Exists(c.Trabalho));
        var depois = RelatorioFicticio.Retrato(c.Arvore.Raiz).Where(x => !x.Caminho.Contains("DESPACHO 03", StringComparison.Ordinal)).ToList();
        Assert.Equal(antes, depois);
    }

    [Fact]
    public void gravar_na_etapa_existente_nunca_sobrescreve()
    {
        using var c = new Cena("emissao-existente", etapa: a => Etapas.Existentes(a.Processo.Pasta).Single(x => x.Numero == 2));
        var etapa = c.Etapa;
        Assert.Equal("20 de fevereiro de 2099", c.Final.DataPorExtenso);
        var outro = Path.Combine(etapa.Pasta, "DESPACHO_APROVACAO_0001_2099.pdf");
        var shaOutro = DocxFicticio.Sha(File.ReadAllBytes(outro));

        var a = c.Gravar(c.Preparar(ConversorFalso.Com(PdfVerdana)), new[] { 1, 2 });
        var shas = new[] { a.Docx, a.Pdf!, a.Recibo }.Select(f => DocxFicticio.Sha(File.ReadAllBytes(f))).ToList();
        var b = c.Gravar(c.Preparar(ConversorFalso.Com(PdfVerdana)), new[] { 1, 2 });

        Assert.Equal(etapa.Pasta, a.Pasta);
        Assert.EndsWith("DESPACHO_CORRECAO_1_2099_FULANO_DE_EXEMPLO_FICTICIO (2).DOCX", b.Docx);
        Assert.EndsWith("DESPACHO_CORRECAO_1_2099_FULANO_DE_EXEMPLO_FICTICIO (2).RECIBO-EDITOR.JSON", b.Recibo);
        Assert.Equal(shas, new[] { a.Docx, a.Pdf!, a.Recibo }.Select(f => DocxFicticio.Sha(File.ReadAllBytes(f))));
        Assert.Equal(shaOutro, DocxFicticio.Sha(File.ReadAllBytes(outro)));
        Assert.Empty(Directory.GetFiles(etapa.Pasta, "*.tmp"));
    }

    [Fact]
    public void recibo_com_origens_e_hashes()
    {
        using var c = new Cena("emissao-recibo");
        var e = c.Gravar(c.Preparar(ConversorFalso.Com(PdfVerdana)), new[] { 1, 2 });

        var r = ReciboDespacho.Ler(e.Recibo);

        Assert.Equal(new[] { "recibo_versao", "tipo", "papel", "registro", "modelo", "textos_fixos", "relatorio", "processo", "etapa", "exigencias",
            "campos_editados", "avisos_confirmados", "docx", "pdf", "sem_pdf_motivo", "paginas_conferidas", "usuario", "app_versao", "gerado_em" },
            r.Select(kv => kv.Key));
        Assert.Equal(1, (int)r["recibo_versao"]!);
        Assert.Equal("correcao", (string?)r["tipo"]);
        Assert.Equal("correction", (string?)r["papel"]);
        Assert.Equal(10, (int)r["registro"]!["revisao"]!);
        Assert.Equal(c.Modelo.Sha256Registro, (string?)r["registro"]!["sha256"]);
        Assert.Equal(c.Modelo.CaminhoRelativo, (string?)r["modelo"]!["caminho"]);
        Assert.Equal(c.Modelo.Sha256, (string?)r["modelo"]!["sha256"]);
        Assert.Equal(c.Textos.Arquivo, (string?)r["textos_fixos"]!["arquivo"]);
        Assert.Equal(c.Textos.Sha256, (string?)r["textos_fixos"]!["sha256"]);
        Assert.Equal("RELATORIO_0001_2099.RPMVNI", (string?)r["relatorio"]!["arquivo"]);
        Assert.Equal(DocxFicticio.Sha(File.ReadAllBytes(c.Arvore.CaminhoPacote)), (string?)r["relatorio"]!["sha256_pacote"]);
        Assert.Equal(c.Origem.Sha256DadosEfetivos, (string?)r["relatorio"]!["sha256_dados_efetivos"]);
        Assert.Equal(1, (int)r["processo"]!["numero"]!);
        Assert.Equal(2099, (int)r["processo"]!["ano"]!);
        Assert.Equal("0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)", (string?)r["processo"]!["pasta"]);
        Assert.Equal("DESPACHO 03 - 2099-03-10", (string?)r["etapa"]!["pasta"]);
        Assert.Equal(3, (int)r["etapa"]!["numero"]!);
        Assert.Equal("2099-03-10", (string?)r["etapa"]!["data"]);
        Assert.True((bool)r["etapa"]!["nova"]!);
        var ex = r["exigencias"]!.AsArray();
        Assert.Equal(new[] { 2, 3 }, ex.Select(x => (int)x!["numero"]!));
        Assert.Equal(c.Rascunho.Pendencias.Select(p => p.ItemId), ex.Select(x => (string?)x!["item"]));
        Assert.Equal(new[] { "NAO_COMPROVADO", "NAO_ATENDE" }, ex.Select(x => (string?)x!["situacao"]));
        Assert.All(ex, x => Assert.False((bool)x!["texto_editado"]!));
        Assert.Equal(DocxFicticio.Sha(File.ReadAllBytes(e.Docx)), (string?)r["docx"]!["sha256"]);
        Assert.Equal("OK", (string?)r["docx"]!["validacao"]);
        Assert.Equal(Path.GetFileName(e.Docx), (string?)r["docx"]!["arquivo"]);
        Assert.Equal(DocxFicticio.Sha(File.ReadAllBytes(e.Pdf!)), (string?)r["pdf"]!["sha256"]);
        Assert.Equal(2, (int)r["pdf"]!["paginas"]!);
        Assert.Equal(new[] { "Verdana", "Verdana-Bold" }, r["pdf"]!["fontes"]!.AsArray().Select(x => (string?)x));
        Assert.Equal("LibreOffice teste", (string?)r["pdf"]!["motor"]);
        Assert.Null(r["sem_pdf_motivo"]);
        Assert.Equal(new[] { 1, 2 }, r["paginas_conferidas"]!["paginas"]!.AsArray().Select(x => (int)x!));
        Assert.Equal("fulano", (string?)r["paginas_conferidas"]!["usuario"]);
        Assert.Equal("2026-11-14T12:00:00Z", (string?)r["paginas_conferidas"]!["em"]);
        Assert.Equal("fulano", (string?)r["usuario"]);
        Assert.Equal("0.1.0-teste", (string?)r["app_versao"]);
        Assert.Equal("2026-11-14T12:00:00Z", (string?)r["gerado_em"]);
        var bytes = File.ReadAllBytes(e.Recibo);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Contains((byte)'\n', bytes);
    }

    [Fact]
    public void recibo_da_aprovacao_sem_exigencias()
    {
        using var c = new Cena("emissao-aprovacao", TipoDespacho.Aprovacao);
        var e = c.Gravar(c.Preparar(ConversorFalso.Com(PdfVerdana)), new[] { 1, 2 });

        var r = ReciboDespacho.Ler(e.Recibo);

        Assert.EndsWith("DESPACHO_APROVACAO_1_2099_FULANO_DE_EXEMPLO_FICTICIO.DOCX", e.Docx);
        Assert.Equal("aprovacao", (string?)r["tipo"]);
        Assert.Equal("approval_simple", (string?)r["papel"]);
        Assert.Empty(r["exigencias"]!.AsArray());
    }

    [Fact]
    public void gravar_so_docx_registra_o_motivo()
    {
        using var c = new Cena("emissao-so-docx");

        var e = c.Gravar(c.Preparar(new ConversorAusente()), null);

        Assert.Null(e.Pdf);
        Assert.Equal(2, Directory.GetFiles(e.Pasta).Length);
        var r = ReciboDespacho.Ler(e.Recibo);
        Assert.Null(r["pdf"]);
        Assert.Equal("SEM_LIBREOFFICE", (string?)r["sem_pdf_motivo"]);
        Assert.Null(r["paginas_conferidas"]);
    }

    [Fact]
    public void gerar_pdf_depois_completa_o_despacho()
    {
        using var c = new Cena("emissao-pdf-depois");
        var e = c.Gravar(c.Preparar(new ConversorAusente()), null);
        var shaDocx = DocxFicticio.Sha(File.ReadAllBytes(e.Docx));
        var trabalho = Path.Combine(c.Pasta, "trabalho-pdf");

        var p = EmissaoDespacho.PrepararPdf(e.Docx, trabalho, ConversorFalso.Com(PdfVerdana), CancellationToken.None);
        Assert.Empty(p.Erros);
        Assert.Equal(2, p.Paginas);
        Assert.Equal("Confira as 2 páginas do PDF antes de gravar.",
            Assert.Throws<ArgumentException>(() => EmissaoDespacho.GravarPdf(p, e.Docx, new[] { 1 }, "LibreOffice teste", Ambiente.Contexto("beltrano"))).Message);
        var f = EmissaoDespacho.GravarPdf(p, e.Docx, new[] { 1, 2 }, "LibreOffice teste", Ambiente.Contexto("beltrano"));

        Assert.Equal(Path.ChangeExtension(e.Docx, ".PDF"), f.Pdf);
        Assert.Equal(PdfVerdana, File.ReadAllBytes(f.Pdf!));
        Assert.Equal(shaDocx, DocxFicticio.Sha(File.ReadAllBytes(e.Docx)));
        var r = ReciboDespacho.Ler(e.Recibo);
        Assert.Equal(DocxFicticio.Sha(PdfVerdana), (string?)r["pdf"]!["sha256"]);
        Assert.Equal(Path.GetFileName(f.Pdf), (string?)r["pdf"]!["arquivo"]);
        Assert.Null(r["sem_pdf_motivo"]);
        Assert.Equal("beltrano", (string?)r["paginas_conferidas"]!["usuario"]);
        Assert.Equal(shaDocx, (string?)r["docx"]!["sha256"]);
        Assert.Equal(3, Directory.GetFiles(e.Pasta).Length);
        Assert.False(Directory.Exists(trabalho));
    }

    [Fact]
    public void gerar_pdf_recusa_docx_sem_recibo_do_editor()
    {
        using var c = new Cena("emissao-pdf-sem-recibo");
        var docx = Path.Combine(c.Arvore.Processo.Pasta, "DESPACHO 01 - 2099-01-15", "DESPACHO_CORRECAO_0001_2099.docx");
        var conv = ConversorFalso.Com(PdfVerdana);

        var p = EmissaoDespacho.PrepararPdf(docx, Path.Combine(c.Pasta, "trabalho-pdf"), conv, CancellationToken.None);

        const string msg = "Este despacho não foi gerado pelo editor (sem recibo); o PDF não é gerado por aqui.";
        Assert.Equal(new[] { msg }, p.Erros);
        Assert.Equal(0, conv.Chamadas);
        Assert.Equal(msg, Assert.Throws<InvalidOperationException>(() => EmissaoDespacho.GravarPdf(p, docx, new[] { 1 }, null, Ambiente.Contexto())).Message);
    }

    [Fact]
    public void argumentos_do_libreoffice()
    {
        const string t = @"C:\trabalho ficticio\x";
        var psi = ConversorLibreOffice.Preparar(@"C:\LO\soffice.com", t + @"\conv\copia.docx", t + @"\conv");

        Assert.Equal(@"C:\LO\soffice.com", psi.FileName);
        Assert.Equal(new[] { "-env:UserInstallation=" + new Uri(t + @"\conv\perfil").AbsoluteUri, "--headless", "--norestore", "--convert-to", "pdf",
            "--outdir", t + @"\conv", t + @"\conv\copia.docx" }, psi.ArgumentList);
        Assert.Equal(t + @"\conv\tmp", psi.Environment["TEMP"]);
        Assert.Equal(t + @"\conv\tmp", psi.Environment["TMP"]);
        // Nunca consulta as impressoras do Windows: sem o "Aguardando conexão de impressora…" de uma impressora de rede fora do ar (e sem
        // os ~50 s de espera por ela a cada conversão).
        Assert.Equal("1", psi.Environment["SAL_DISABLE_PRINTERLIST"]);
        Assert.Equal("1", psi.Environment["SAL_DISABLE_DEFAULTPRINTER"]);
        Assert.True(psi.CreateNoWindow);
        Assert.False(psi.UseShellExecute);
        // O app é de janela (sem console): entrada e saídas vão por pipes que o conversor fecha (entrada) e esvazia (saídas, que viram o
        // detalhe de uma falha).
        Assert.True(psi.RedirectStandardInput);
        Assert.True(psi.RedirectStandardOutput);
        Assert.True(psi.RedirectStandardError);
    }

    [Fact]
    public void perfil_do_libreoffice_em_caminho_curto()
    {
        // O soffice aborta (0xC0000409) quando o perfil fica fundo demais: o arquivo mais fundo dele tem 134 caracteres abaixo da pasta do
        // perfil (medido no LibreOffice 26.8), e o Windows corta em 260. O conversor usa o nome curto (8.3) da pasta, quando o volume o tem,
        // e recusa com o motivo um perfil que ainda passe de 120 caracteres.
        Assert.True(ConversorLibreOffice.PerfilCabe(new string('a', 120)));
        Assert.False(ConversorLibreOffice.PerfilCabe(new string('a', 121)));

        using var pasta = Ambiente.PastaDeTeste("perfil-curto");
        var funda = Path.Combine(pasta, "uma pasta com nome bem comprido", "outra pasta com nome bem comprido", "perfil");
        Directory.CreateDirectory(funda);
        var curta = ConversorLibreOffice.CaminhoCurto(funda);
        Assert.True(curta.Length <= funda.Length);
        Assert.True(Directory.Exists(curta));
        // O Windows pode preservar a grafia 8.3 em FullName. Confere a identidade pelo mesmo arquivo.
        File.WriteAllText(Path.Combine(funda, "conferencia.txt"), "perfil de teste");
        Assert.Equal("perfil de teste", File.ReadAllText(Path.Combine(curta, "conferencia.txt")));
        // Pasta que não existe: o caminho como veio.
        var inexistente = Path.Combine(pasta, "nao existe");
        Assert.Equal(inexistente, ConversorLibreOffice.CaminhoCurto(inexistente));

        // A linha de comando usa o perfil e o TEMP que o conversor passar (os curtos).
        var psi = ConversorLibreOffice.Preparar(@"C:\LO\soffice.com", @"C:\t\conv\copia.docx", @"C:\t\conv", @"C:\T~1\perfil", @"C:\T~1\tmp");
        Assert.Equal("-env:UserInstallation=" + new Uri(@"C:\T~1\perfil").AbsoluteUri, psi.ArgumentList[0]);
        Assert.Equal(@"C:\T~1\tmp", psi.Environment["TEMP"]);
        Assert.Equal(@"C:\T~1\tmp", psi.Environment["TMP"]);
    }

    [Fact]
    public void conversor_sem_soffice_nao_esta_disponivel()
    {
        var c = new ConversorLibreOffice(null);
        Assert.False(c.Disponivel);
        Assert.Null(c.Versao);
        Assert.Equal("SEM_LIBREOFFICE", c.Converter("x.docx", "y", CancellationToken.None).Motivo);
    }

    [Fact]
    public void orfas_com_mais_de_um_dia_sao_apagadas()
    {
        using var pasta = Ambiente.PastaDeTeste("orfas");
        var velha = Directory.CreateDirectory(Path.Combine(pasta, "velha")).FullName;
        File.WriteAllText(Path.Combine(velha, "despacho.docx"), "x");
        var nova = Directory.CreateDirectory(Path.Combine(pasta, "nova")).FullName;
        Directory.SetLastWriteTimeUtc(velha, DateTime.UtcNow.AddDays(-2));

        EmissaoDespacho.LimparOrfas(pasta);
        EmissaoDespacho.LimparOrfas(Path.Combine(pasta, "nao existe"));

        Assert.False(Directory.Exists(velha));
        Assert.True(Directory.Exists(nova));
    }

    [Fact]
    public void info_pdf_lista_fontes_sem_prefixo() =>
        Assert.Equal(new[] { "Verdana", "Verdana-Bold" }, Pmvni.Composicao.InfoPdf.Fontes(PdfFicticio.Criar(1, "BAAAAA+Verdana", "Verdana-Bold", "CAAAAA+Verdana")));
}

[Collection("Ambiente")]
public sealed class LocalizarLibreOfficeTestes
{
    [Fact]
    public void localizar_usa_PMVNI_SOFFICE_quando_o_arquivo_existe()
    {
        using var pasta = Ambiente.PastaDeTeste("soffice");
        var falso = Path.Combine(pasta, "soffice.com");
        File.WriteAllText(falso, "ficticio");
        var antes = Environment.GetEnvironmentVariable("PMVNI_SOFFICE");
        try
        {
            Environment.SetEnvironmentVariable("PMVNI_SOFFICE", falso);
            Assert.Equal(falso, ConversorLibreOffice.Localizar());

            var inexistente = Path.Combine(pasta, "nao existe", "soffice.com");
            Environment.SetEnvironmentVariable("PMVNI_SOFFICE", inexistente);
            Assert.NotEqual(inexistente, ConversorLibreOffice.Localizar());
        }
        finally
        {
            Environment.SetEnvironmentVariable("PMVNI_SOFFICE", antes);
        }
    }

    [Fact]
    public void programa_escolhido_em_ajustes_vale_se_for_o_soffice_e_existir()
    {
        // Ajustes › Despachos e PDF › LibreOffice: "Escolher…" guarda o caminho; "Automático" (null) acha sozinho, como antes.
        using var pasta = Ambiente.PastaDeTeste("soffice-escolhido");
        var exe = Path.Combine(pasta, "soffice.exe");
        File.WriteAllText(exe, "ficticio");
        Assert.True(ConversorLibreOffice.ProgramaValido(exe));
        Assert.Equal(exe, ConversorLibreOffice.Localizar(exe));
        // Com o soffice.com ao lado (o de console, sem janela), é ele que vale.
        var com = Path.Combine(pasta, "soffice.com");
        File.WriteAllText(com, "ficticio");
        Assert.Equal(com, ConversorLibreOffice.Localizar(exe));
        // Outro programa ou arquivo que sumiu: volta ao automático.
        var outro = Path.Combine(pasta, "notepad.exe");
        File.WriteAllText(outro, "x");
        Assert.False(ConversorLibreOffice.ProgramaValido(outro));
        Assert.Equal(ConversorLibreOffice.Localizar(), ConversorLibreOffice.Localizar(outro));
        Assert.Equal(ConversorLibreOffice.Localizar(), ConversorLibreOffice.Localizar(Path.Combine(pasta, "sumiu", "soffice.exe")));
        Assert.Equal(ConversorLibreOffice.Localizar(), ConversorLibreOffice.Localizar(null));
    }
}
