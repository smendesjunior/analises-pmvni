using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Lógica pura do "Novo despacho" (Tarefa 16): a origem sugerida pelo módulo de onde se veio, o motivo de um tipo desabilitado e
/// quando se pode gerar e gravar.</summary>
public sealed class FluxoNovoDespachoTestes
{
    static readonly DateOnly Hoje = new(2099, 3, 10);

    static IReadOnlyList<RelatorioEncontrado> Relatorios(Processo p) => Processos.Detalhar(p, CancellationToken.None).Relatorios;

    static IReadOnlyList<Processo> Lista(ArvoreFicticia a) => Processos.Listar(a.Raiz, CancellationToken.None).Itens;

    [Fact]
    public void relatorio_na_pasta_despacho_acha_o_processo_e_reutiliza_a_etapa()
    {
        // 2026-11-16 (regra do kit): o relatório fica em DESPACHO NN - AAAA-MM-DD; o despacho da mesma análise reutiliza essa pasta.
        using var pasta = Ambiente.PastaDeTeste("fluxo-relatorio-na-etapa");
        var a = RelatorioFicticio.Arvore(pasta);
        var processo = Lista(a).First(p => p.Numero == 1);
        var etapa = Path.Combine(processo.Pasta, "DESPACHO 07 - 2099-03-01");
        Directory.CreateDirectory(etapa);
        var pacote = Path.Combine(etapa, "Relatorio_0001_2099.rpmvni");
        File.Copy(a.CaminhoPacote, pacote);

        var s = FluxoNovoDespacho.Sugerir(Modulo.Relatorios, null, pacote, Lista(a), Relatorios, Hoje);
        Assert.Equal(processo.Pasta, s.Processo?.Pasta);
        Assert.Equal(pacote, s.Pacote);
        Assert.Equal(etapa, s.Etapa?.Pasta);
        Assert.False(s.Etapa!.Nova);
    }

    [Fact]
    public void sugestao_pelo_relatorio_aberto()
    {
        using var pasta = Ambiente.PastaDeTeste("fluxo-relatorio-aberto");
        var a = RelatorioFicticio.Arvore(pasta);
        var pacote = Path.Combine(a.Raiz, "2099", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)", "Relatorio_0001_2099.rpmvni");
        Assert.Equal(a.CaminhoPacote, pacote);

        // O selecionado em Processos não conta quando se vem de Relatórios: vale o processo do pacote aberto.
        var outro = Lista(a).First(p => p.Numero == 12);
        var s = FluxoNovoDespacho.Sugerir(Modulo.Relatorios, outro, pacote, Lista(a), Relatorios, Hoje);

        Assert.Equal(1, s.Processo!.Numero);
        Assert.Equal(pacote, s.Pacote);
        Assert.Equal("DESPACHO 03 - 2099-03-10", s.Etapa!.Nome);
        Assert.True(s.Etapa.Nova);
    }

    [Fact]
    public void sugestao_por_processos_escolhe_o_pmvrel_mais_novo()
    {
        using var pasta = Ambiente.PastaDeTeste("fluxo-mais-novo");
        var a = RelatorioFicticio.Arvore(pasta);
        var segundo = Path.Combine(a.Processo.Pasta, "Relatorio_0001_2099_2.rpmvni");
        File.Copy(a.CaminhoPacote, segundo);
        File.SetLastWriteTimeUtc(a.CaminhoPacote, new DateTime(2099, 3, 1, 12, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(segundo, new DateTime(2099, 3, 9, 12, 0, 0, DateTimeKind.Utc));

        var s = FluxoNovoDespacho.Sugerir(Modulo.Processos, a.Processo, null, Lista(a), Relatorios, Hoje);
        Assert.Equal(1, s.Processo!.Numero);
        Assert.Equal(segundo, s.Pacote);
        Assert.Equal("DESPACHO 03 - 2099-03-10", s.Etapa!.Nome);

        // De Despachos, o mesmo; e o mais antigo volta a ser o escolhido se ficar mais novo.
        File.SetLastWriteTimeUtc(a.CaminhoPacote, new DateTime(2099, 3, 10, 8, 0, 0, DateTimeKind.Utc));
        Assert.Equal(a.CaminhoPacote, FluxoNovoDespacho.Sugerir(Modulo.Despachos, a.Processo, null, Lista(a), Relatorios, Hoje).Pacote);
    }

    [Fact]
    public void sugestao_sem_contexto_nao_escolhe_nada()
    {
        using var pasta = Ambiente.PastaDeTeste("fluxo-sem-contexto");
        var a = RelatorioFicticio.Arvore(pasta);
        Assert.Equal(new OrigemSugerida(null, null, null), FluxoNovoDespacho.Sugerir(Modulo.Despachos, null, null, Lista(a), Relatorios, Hoje));
        Assert.Equal(new OrigemSugerida(null, null, null), FluxoNovoDespacho.Sugerir(Modulo.Relatorios, null, null, Lista(a), Relatorios, Hoje));
        // Pacote aberto fora de qualquer pasta de processo: nada é sugerido (o usuário escolhe o processo).
        var fora = Path.Combine(pasta, "solto.rpmvni");
        File.Copy(a.CaminhoPacote, fora);
        Assert.Equal(new OrigemSugerida(null, null, null), FluxoNovoDespacho.Sugerir(Modulo.Relatorios, null, fora, Lista(a), Relatorios, Hoje));
        // Processo sem .rpmvni: o processo e a etapa vêm, o pacote não.
        var sem = Lista(a).First(p => p.Numero == 12);
        var s = FluxoNovoDespacho.Sugerir(Modulo.Processos, sem, null, Lista(a), Relatorios, Hoje);
        Assert.Equal(sem, s.Processo);
        Assert.Null(s.Pacote);
        Assert.NotNull(s.Etapa);
    }

    [Fact]
    public void motivo_desabilitado()
    {
        var aprovacao = new ModeloDespacho(TipoDespacho.Aprovacao, "approval_simple", "x/aprovacao.docx", @"C:\kit\x\aprovacao.docx", "aa", 10, "bb");
        var m = new ModelosDisponiveis([aprovacao],
            new Dictionary<TipoDespacho, string> { [TipoDespacho.Correcao] = "O modelo x/correcao.docx não foi encontrado no kit." });

        Assert.Equal("O modelo x/correcao.docx não foi encontrado no kit.", FluxoNovoDespacho.MotivoDesabilitado(TipoDespacho.Correcao, m, compondo: false));
        Assert.Null(FluxoNovoDespacho.MotivoDesabilitado(TipoDespacho.Aprovacao, m, compondo: false));
        Assert.Equal("Termine ou cancele o despacho em edição.", FluxoNovoDespacho.MotivoDesabilitado(TipoDespacho.Aprovacao, m, compondo: true));
        Assert.Equal("Termine ou cancele o despacho em edição.", FluxoNovoDespacho.MotivoDesabilitado(TipoDespacho.Correcao, m, compondo: true));

        // Kit inexistente: os dois tipos com o motivo do registro.
        using var pasta = Ambiente.PastaDeTeste("fluxo-kit-vazio");
        var vazio = RegistroModelos.Ler(pasta);
        var esperado = "Kit PMVNI não encontrado em " + pasta.Caminho + ". Escolha a pasta do kit em Ajustes.";
        Assert.Equal(esperado, FluxoNovoDespacho.MotivoDesabilitado(TipoDespacho.Correcao, vazio, false));
        Assert.Equal(esperado, FluxoNovoDespacho.MotivoDesabilitado(TipoDespacho.Aprovacao, vazio, false));
    }

    [Fact]
    public void pode_gravar_so_com_todas_as_paginas()
    {
        var docx = new byte[] { 1, 2, 3 };
        var pdf = PdfFicticio.Criar(2, "BAAAAA+Verdana");
        var comPdf = new Preparacao("trabalho", docx, pdf, 2, ["Verdana"], null, null, []);
        Assert.False(FluxoNovoDespacho.PodeGravar(comPdf, new[] { 1 }));
        Assert.False(FluxoNovoDespacho.PodeGravar(comPdf, Array.Empty<int>()));
        Assert.True(FluxoNovoDespacho.PodeGravar(comPdf, new[] { 1, 2 }));
        Assert.True(FluxoNovoDespacho.PodeGravar(comPdf, new[] { 2, 1 }));
        Assert.False(FluxoNovoDespacho.PodeGravar(comPdf, new[] { 1, 2, 3 }));

        var semPdf = new Preparacao("trabalho", docx, null, null, [], "SEM_LIBREOFFICE", "O LibreOffice não está instalado…", []);
        Assert.True(FluxoNovoDespacho.PodeGravar(semPdf, Array.Empty<int>()));

        var comErro = new Preparacao("trabalho", [], null, null, [], null, null, ["O modelo do kit mudou depois de aberto; abra o Novo despacho de novo."]);
        Assert.False(FluxoNovoDespacho.PodeGravar(comErro, Array.Empty<int>()));
        Assert.False(FluxoNovoDespacho.PodeGravar(comPdf with { Erros = ["x"] }, new[] { 1, 2 }));
    }

    [Fact]
    public void pode_gerar_so_sem_nada_faltando()
    {
        var kit = Ambiente.ExigirKit();
        var modelo = RegistroModelos.Ler(kit).Modelos.Single(m => m.Tipo == TipoDespacho.Aprovacao);
        var info = PreenchimentoDocx.LerModelo(File.ReadAllBytes(modelo.Caminho), TipoDespacho.Aprovacao);
        var etapa = new EtapaDespacho(@"C:\x\DESPACHO 03 - 2099-03-10", "DESPACHO 03 - 2099-03-10", 3, Hoje, true);
        var r = ConteudoDespacho.Montar(TipoDespacho.Aprovacao, RelatorioFicticio.Curto().Projeto, info, etapa);
        Assert.False(FluxoNovoDespacho.PodeGerar(r));
        r.DefinirIntervencao(Intervencao.ObraNova, true);
        Assert.False(FluxoNovoDespacho.PodeGerar(r));
        r.DefinirArea("210,52");
        Assert.True(FluxoNovoDespacho.PodeGerar(r));
    }

    [Fact]
    public void trocar_a_etapa_mantem_o_que_foi_editado()
    {
        var kit = Ambiente.ExigirKit();
        var modelo = RegistroModelos.Ler(kit).Modelos.Single(m => m.Tipo == TipoDespacho.Correcao);
        var info = PreenchimentoDocx.LerModelo(File.ReadAllBytes(modelo.Caminho), TipoDespacho.Correcao);
        var projeto = RelatorioFicticio.Curto().Projeto;
        var nova = new EtapaDespacho(@"C:\x\DESPACHO 03 - 2099-03-10", "DESPACHO 03 - 2099-03-10", 3, Hoje, true);
        var existente = new EtapaDespacho(@"C:\x\DESPACHO 02 - 2099-02-20", "DESPACHO 02 - 2099-02-20", 2, new DateOnly(2099, 2, 20), false);
        var de = ConteudoDespacho.Montar(TipoDespacho.Correcao, projeto, info, nova);
        Assert.True(de.Pendencias.Count >= 2);
        var primeiro = de.Pendencias[0].ItemId;
        var segundo = de.Pendencias[1].ItemId;
        de.DefinirCabecalho("assunto", "OUTRO ASSUNTO");
        de.DefinirTexto(segundo, "Apresentar a planta corrigida.");
        de.DefinirGrupo(segundo, "PRANCHA 01");
        de.Marcar(primeiro, false);
        de.Mover(segundo, -1);

        var para = ConteudoDespacho.Montar(TipoDespacho.Correcao, projeto, info, existente);
        FluxoNovoDespacho.TransferirEdicoes(de, para);

        Assert.Equal(new DateOnly(2099, 2, 20), para.Data);
        Assert.Equal("OUTRO ASSUNTO", para.Assunto);
        Assert.Equal(de.Pendencias.Select(p => p.ItemId), para.Pendencias.Select(p => p.ItemId));
        Assert.Equal(de.Pendencias.Select(p => (p.Texto, p.Fundamento, p.Grupo, p.Incluida)), para.Pendencias.Select(p => (p.Texto, p.Fundamento, p.Grupo, p.Incluida)));
        Assert.Equal(de.CamposEditados, para.CamposEditados);
    }

    [Fact]
    public void escolha_manual_do_relatorio_base()
    {
        // O seletor aceita o .rpmvni (usado como está) e o dados.json da análise (importado antes); o resto não serve.
        Assert.Equal(ArquivoBase.Pacote, FluxoNovoDespacho.TipoDoArquivoBase(@"D:\outra pasta\Relatorio_0001_2099.PMVREL"));
        Assert.Equal(ArquivoBase.DadosJson, FluxoNovoDespacho.TipoDoArquivoBase(@"D:\analise\dados.json"));
        Assert.Null(FluxoNovoDespacho.TipoDoArquivoBase(@"D:\analise\relatorio.pdf"));
        Assert.Null(FluxoNovoDespacho.TipoDoArquivoBase(null));
        Assert.Equal(new[] { ".rpmvni", ".pmvrel", ".json" }, FluxoNovoDespacho.FiltroArquivoBase.Split(';'));

        // A lista: os da pasta do processo e, depois, os escolhidos à mão (sem repetir um que já está nela, nem a análise da IA da pasta).
        using var pasta = Ambiente.PastaDeTeste("fluxo-escolha-manual");
        var a = RelatorioFicticio.Arvore(pasta);
        var daPasta = Relatorios(a.Processo);
        var fora = Path.Combine(pasta.Caminho, "fora", "Relatorio_0001_2099.rpmvni");
        Directory.CreateDirectory(Path.GetDirectoryName(fora)!);
        File.Copy(a.CaminhoPacote, fora);
        var comPdf = daPasta.Append(new RelatorioEncontrado(Path.Combine(a.Processo.Pasta, "dados.json"), TipoRelatorio.AnaliseIa, DateTime.UtcNow)).ToList();

        var lista = FluxoNovoDespacho.RelatoriosDaLista(comPdf, [fora, a.CaminhoPacote.ToUpperInvariant(), fora]);

        Assert.Equal(new[] { (a.CaminhoPacote, false), (fora, true) }, lista);
        Assert.Equal(new[] { (a.CaminhoPacote, false) }, FluxoNovoDespacho.RelatoriosDaLista(daPasta, []));
    }
}
