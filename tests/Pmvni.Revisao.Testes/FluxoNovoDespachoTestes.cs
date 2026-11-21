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
        // 2026-11-17: a pasta da análise é de outro dia, então o despacho de hoje vai para uma pasta nova, com a data de hoje.
        Assert.Equal("DESPACHO 08 - 2099-03-10", s.Etapa?.Nome);
        Assert.True(s.Etapa!.Nova);

        // Análise feita hoje: o despacho reutiliza a pasta dela.
        var deHoje = Path.Combine(processo.Pasta, "DESPACHO 08 - 2099-03-10");
        Directory.CreateDirectory(deHoje);
        var pacoteHoje = Path.Combine(deHoje, "Relatorio_0001_2099.rpmvni");
        File.Copy(a.CaminhoPacote, pacoteHoje);
        s = FluxoNovoDespacho.Sugerir(Modulo.Relatorios, null, pacoteHoje, Lista(a), Relatorios, Hoje);
        Assert.Equal(deHoje, s.Etapa?.Pasta);
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

        // Kit sem modelos (e nada instalado): os dois tipos com o motivo.
        using var pasta = Ambiente.PastaDeTeste("fluxo-kit-vazio");
        var vazio = RegistroModelos.Ler(pasta);
        var esperado = FontesModelos.NaoEncontrados(Path.GetFullPath(pasta.Caminho));   // Tarefa 22: sem modelos em lugar nenhum, "Atualize o kit PMVNI"
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

    // ---------- correção automática (2026-11-16) ----------

    static InfoModelo InfoCorrecao()
    {
        var modelo = RegistroModelos.Ler(Ambiente.ExigirKit()).Modelos.Single(m => m.Tipo == TipoDespacho.Correcao);
        return PreenchimentoDocx.LerModelo(File.ReadAllBytes(modelo.Caminho), TipoDespacho.Correcao);
    }

    static readonly EtapaDespacho EtapaNova = new(@"C:\x\DESPACHO 03 - 2099-03-10", "DESPACHO 03 - 2099-03-10", 3, Hoje, true);

    static SessaoRevisao Sessao()
    {
        var c = RelatorioFicticio.Curto();
        return new SessaoRevisao(c.Projeto, c.Historico, Ambiente.Contexto());
    }

    [Fact]
    public void automatico_leva_todos_os_itens_pendentes_com_a_situacao_efetiva_e_vai_para_a_conferencia()
    {
        using var pasta = Ambiente.PastaDeTeste("fluxo-automatico");
        var a = RelatorioFicticio.Arvore(pasta);
        // O curto tem 1 ATENDE, 2 FALTA COMPROVAR e 3 NÃO ATENDE. A revisão muda o 1 para INFORMAÇÕES DIVERGENTES: vale a situação efetiva.
        var s = Sessao();
        var id1 = s.Projeto.Incluidos[0].Id;
        s.Aplicar(new DefinirSituacao(id1, "DIVERGENCIA_INTERNA"));
        s.Aplicar(new DefinirJustificativa(id1, "Revisado no teste."));
        var bloqueios = ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, s.Projeto, a.Processo, somenteLeitura: false);
        var r = ConteudoDespacho.Montar(TipoDespacho.Correcao, s.Projeto, InfoCorrecao(), EtapaNova);

        var plano = FluxoNovoDespacho.PlanejarAutomatico(a.Processo, a.CaminhoPacote, bloqueios, r);

        Assert.Equal(DestinoAutomatico.Conferir, plano.Destino);
        // Todas as exigências, marcadas, na ordem do relatório; o cabeçalho vem da capa e o assunto, do modelo.
        Assert.Equal(new[] { "DIVERGENCIA_INTERNA", "NAO_COMPROVADO", "NAO_ATENDE" }, r.Pendencias.Select(p => p.Situacao));
        Assert.All(r.Pendencias, p => Assert.True(p.Incluida && p.Texto.Length > 0));
        Assert.Equal("0001/2099", r.Protocolo);
        Assert.Equal("FULANO DE EXEMPLO (FICTICIO)", r.Requerente);
        Assert.Equal("SOLICITAÇÃO DE ALVARÁ DE LICENÇA DE OBRA", r.Assunto);
        Assert.True(FluxoNovoDespacho.PodeGerar(r));
        Assert.Equal("3 exigências do relatório: 1 NÃO ATENDE, 1 FALTA COMPROVAR e 1 INFORMAÇÕES DIVERGENTES.", FluxoNovoDespacho.ResumoAutomatico(r));
        Assert.Equal("Despacho criado a partir do relatório", plano.Titulo);
        Assert.StartsWith("3 exigências do relatório:", plano.Mensagem);
        Assert.EndsWith("Confira as páginas antes de gravar.", plano.Mensagem);
    }

    [Fact]
    public void automatico_item_excluido_fica_de_fora_e_o_resumo_conta_so_o_que_entra()
    {
        var s = Sessao();
        s.Aplicar(new Excluir(s.Projeto.Incluidos[2].Id)); // o NÃO ATENDE sai do relatório
        var r = ConteudoDespacho.Montar(TipoDespacho.Correcao, s.Projeto, InfoCorrecao(), EtapaNova);

        Assert.Equal(new[] { "NAO_COMPROVADO" }, r.Pendencias.Select(p => p.Situacao));
        Assert.Equal("1 exigência do relatório: 1 FALTA COMPROVAR.", FluxoNovoDespacho.ResumoAutomatico(r));
    }

    [Fact]
    public void automatico_sem_processo_sem_relatorio_ou_sem_item_pendente_diz_por_que()
    {
        using var pasta = Ambiente.PastaDeTeste("fluxo-automatico-sem");
        var a = RelatorioFicticio.Arvore(pasta);

        var semProcesso = FluxoNovoDespacho.PlanejarAutomatico(null, null, [], null);
        Assert.Equal(DestinoAutomatico.EscolherProcesso, semProcesso.Destino);
        Assert.Equal(FluxoNovoDespacho.AutomaticoSemProcesso, semProcesso.Mensagem);

        // Processo sem .rpmvni: o rascunho em branco fica para escrever à mão, com o motivo à vista.
        var emBranco = ConteudoDespacho.MontarEmBranco(TipoDespacho.Correcao, InfoCorrecao(), Hoje, "0012/2099", "BELTRANO (FICTICIO)");
        var semRelatorio = FluxoNovoDespacho.PlanejarAutomatico(a.Processo, null, [], emBranco);
        Assert.Equal(DestinoAutomatico.SemRelatorio, semRelatorio.Destino);
        Assert.Contains("não tem relatório revisado (.rpmvni)", semRelatorio.Mensagem, StringComparison.Ordinal);

        // Todos os itens ATENDE: o bloqueio do relatório explica, e nada é gerado.
        var s = Sessao();
        foreach (var it in s.Projeto.Incluidos.Where(i => Motor.SituacaoEfetiva(i) != "ATENDE").ToList())
        {
            s.Aplicar(new DefinirSituacao(it.Id, "ATENDE"));
            s.Aplicar(new DefinirJustificativa(it.Id, "Corrigido no teste."));
        }
        var bloqueios = ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, s.Projeto, a.Processo, somenteLeitura: false);
        Assert.Contains("Nenhum item incluído tem situação diferente de ATENDE: não há exigência para o despacho de correção.", bloqueios);
        var semPendencia = FluxoNovoDespacho.PlanejarAutomatico(a.Processo, a.CaminhoPacote, bloqueios, null);
        Assert.Equal(DestinoAutomatico.Bloqueado, semPendencia.Destino);
        Assert.Equal(FluxoNovoDespacho.AutomaticoBloqueado, semPendencia.Mensagem);
    }

    [Fact]
    public void automatico_com_exigencia_sem_texto_para_no_conteudo_e_lista_o_que_falta()
    {
        using var pasta = Ambiente.PastaDeTeste("fluxo-automatico-falta");
        var a = RelatorioFicticio.Arvore(pasta);
        var r = ConteudoDespacho.Montar(TipoDespacho.Correcao, RelatorioFicticio.Curto().Projeto, InfoCorrecao(), EtapaNova);
        r.DefinirTexto(r.Pendencias[0].ItemId, ""); // um item do relatório sem providência escrita

        var plano = FluxoNovoDespacho.PlanejarAutomatico(a.Processo, a.CaminhoPacote, [], r);

        Assert.Equal(DestinoAutomatico.FaltaPreencher, plano.Destino);
        Assert.Contains("ITEM 02: texto da exigência", plano.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public void automatico_no_painel_usa_o_relatorio_escolhido_ou_o_mais_novo_e_avisa_antes_de_trocar_edicoes()
    {
        using var pasta = Ambiente.PastaDeTeste("fluxo-automatico-painel");
        var a = RelatorioFicticio.Arvore(pasta);
        var segundo = Path.Combine(a.Processo.Pasta, "Relatorio_0001_2099_2.rpmvni");
        File.Copy(a.CaminhoPacote, segundo);
        File.SetLastWriteTimeUtc(a.CaminhoPacote, new DateTime(2099, 3, 1, 12, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(segundo, new DateTime(2099, 3, 5, 12, 0, 0, DateTimeKind.Utc));
        var daPasta = Relatorios(a.Processo);

        Assert.Equal(segundo, FluxoNovoDespacho.RelatorioDoAutomatico(null, daPasta));
        Assert.Equal(segundo, FluxoNovoDespacho.RelatorioDoAutomatico("", daPasta));
        Assert.Equal(a.CaminhoPacote, FluxoNovoDespacho.RelatorioDoAutomatico(a.CaminhoPacote, daPasta));
        Assert.Null(FluxoNovoDespacho.RelatorioDoAutomatico(null, []));

        var r = ConteudoDespacho.Montar(TipoDespacho.Correcao, RelatorioFicticio.Curto().Projeto, InfoCorrecao(), EtapaNova);
        Assert.False(FluxoNovoDespacho.TemEdicoes(null));
        Assert.False(FluxoNovoDespacho.TemEdicoes(r));
        r.DefinirTexto(r.Pendencias[0].ItemId, "Texto reescrito.");
        Assert.True(FluxoNovoDespacho.TemEdicoes(r));
        var manual = ConteudoDespacho.MontarEmBranco(TipoDespacho.Correcao, InfoCorrecao(), Hoje, "0001/2099", "FULANO");
        manual.AdicionarExigencia("Exigência escrita à mão.");
        Assert.True(FluxoNovoDespacho.TemEdicoes(manual));
    }

    [Fact]
    public void automatico_esta_no_menu_novo_despacho_e_vale_fora_de_despachos()
    {
        Assert.True(ComandosApp.ValeForaDeDespachos(ComandosApp.DespachosNovoCorrecaoAutomatica));
        Assert.Equal(TipoDespacho.Correcao, ComandosApp.TipoDoNovoDespacho(ComandosApp.DespachosNovoCorrecaoAutomatica));
        Assert.True(ComandosApp.EhCorrecaoAutomatica(ComandosApp.DespachosNovoCorrecaoAutomatica));
        Assert.False(ComandosApp.EhCorrecaoAutomatica(ComandosApp.DespachosNovoCorrecao));
        foreach (var m in new[] { Modulo.Despachos, Modulo.Processos, Modulo.Relatorios })
        {
            var novo = Faixas.Comandos(Faixas.Para(m)).Single(c => c.Id == ComandosApp.DespachosNovo);
            Assert.Contains(novo.Itens!, i => i.Id == ComandosApp.DespachosNovoCorrecaoAutomatica && i.Rotulo == "Correção automática (itens pendentes do relatório)");
        }
        Assert.False(string.IsNullOrWhiteSpace(DicasComandos.Descricao(ComandosApp.DespachosNovoCorrecaoAutomatica)));
    }

    // ---------- os três itens do menu Novo despacho (verificação 2026-11-17) ----------

    static InfoModelo InfoAprovacao()
    {
        var modelo = RegistroModelos.Ler(Ambiente.ExigirKit()).Modelos.Single(m => m.Tipo == TipoDespacho.Aprovacao);
        return PreenchimentoDocx.LerModelo(File.ReadAllBytes(modelo.Caminho), TipoDespacho.Aprovacao);
    }

    [Fact]
    public void cada_item_do_menu_abre_o_tipo_certo()
    {
        Assert.Equal(TipoDespacho.Correcao, ComandosApp.TipoDoNovoDespacho(ComandosApp.DespachosNovoCorrecao));
        Assert.Equal(TipoDespacho.Correcao, ComandosApp.TipoDoNovoDespacho(ComandosApp.DespachosNovoCorrecaoAutomatica));
        Assert.Equal(TipoDespacho.Aprovacao, ComandosApp.TipoDoNovoDespacho(ComandosApp.DespachosNovoAprovacao));
        Assert.Null(ComandosApp.TipoDoNovoDespacho(ComandosApp.DespachosNovo));
        foreach (var id in new[] { ComandosApp.DespachosNovoCorrecao, ComandosApp.DespachosNovoCorrecaoAutomatica, ComandosApp.DespachosNovoAprovacao })
            Assert.True(ComandosApp.ValeForaDeDespachos(id));
        Assert.False(ComandosApp.ValeForaDeDespachos(ComandosApp.DespachosGerarPdf));
        // Em Relatórios, o menu fica na Página Inicial, na ordem Correção, Correção automática, Aprovação.
        var inicio = Faixas.Para(Modulo.Relatorios).Abas.Single(a => a.Nome == "Página Inicial");
        var menu = inicio.Comandos.Single(c => c.Id == ComandosApp.DespachosNovo);
        Assert.Equal(new[] { ComandosApp.DespachosNovoCorrecao, ComandosApp.DespachosNovoCorrecaoAutomatica, ComandosApp.DespachosNovoAprovacao },
            menu.Itens!.Select(i => i.Id));
    }

    [Fact]
    public void correcao_traz_nao_atende_falta_comprovar_e_divergentes_e_aprovacao_nao_lista_pendencias()
    {
        var s = Sessao();
        var id1 = s.Projeto.Incluidos[0].Id;
        s.Aplicar(new DefinirSituacao(id1, "DIVERGENCIA_INTERNA"));
        s.Aplicar(new DefinirJustificativa(id1, "Revisado no teste."));

        var correcao = ConteudoDespacho.Montar(TipoDespacho.Correcao, s.Projeto, InfoCorrecao(), EtapaNova);
        Assert.Equal(new[] { "DIVERGENCIA_INTERNA", "NAO_COMPROVADO", "NAO_ATENDE" }, correcao.Pendencias.Select(p => p.Situacao));
        Assert.DoesNotContain(correcao.Pendencias, p => p.Situacao == "ATENDE");

        // A aprovação nunca traz exigências, mesmo com itens pendentes no relatório (eles só viram o aviso de confirmação).
        var aprovacao = ConteudoDespacho.Montar(TipoDespacho.Aprovacao, s.Projeto, InfoAprovacao(), EtapaNova);
        Assert.Empty(aprovacao.Pendencias);
        Assert.Single(ConteudoDespacho.Avisos(TipoDespacho.Aprovacao, s.Projeto));
    }

    [Fact]
    public void automatico_com_relatorio_somente_leitura_nao_gera()
    {
        using var pasta = Ambiente.PastaDeTeste("fluxo-automatico-somente-leitura");
        var a = RelatorioFicticio.Arvore(pasta);
        var s = Sessao();
        var bloqueios = ConteudoDespacho.Bloqueios(TipoDespacho.Correcao, s.Projeto, a.Processo, somenteLeitura: true);
        Assert.Contains(bloqueios, b => b.Contains("somente leitura", StringComparison.Ordinal));

        var plano = FluxoNovoDespacho.PlanejarAutomatico(a.Processo, a.CaminhoPacote, bloqueios, null);
        Assert.Equal(DestinoAutomatico.Bloqueado, plano.Destino);
    }

    [Fact]
    public void com_despacho_em_edicao_os_tres_tipos_ficam_desabilitados_com_o_motivo()
    {
        var m = new ModelosDisponiveis([], new Dictionary<TipoDespacho, string>());
        foreach (var t in new[] { TipoDespacho.Correcao, TipoDespacho.Aprovacao })
            Assert.Equal(FluxoNovoDespacho.Compondo, FluxoNovoDespacho.MotivoDesabilitado(t, m, compondo: true));
    }
}
