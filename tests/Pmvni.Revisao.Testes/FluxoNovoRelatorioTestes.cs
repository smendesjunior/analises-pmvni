using System.Text;
using Pmvni.Agentes;
using Pmvni.App;
using Xunit;

namespace Pmvni.Revisao.Testes;

/// <summary>Novo relatório (Tarefa 20, desenho §12.2, §12.5–§12.8, §12.11): a lógica da página, sem WinUI.</summary>
public class FluxoNovoRelatorioTestes
{
    const string Arvore = @"C:\processos-ficticios";
    static readonly Processo P0001 = new(Arvore + @"\2099\0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)", "0001 - 2099 - FULANO DE EXEMPLO (FICTICIO)", 1, 2099, "FULANO DE EXEMPLO (FICTICIO)");
    static readonly Processo P0012 = new(Arvore + @"\2099\0012-2099 - BELTRANO DE EXEMPLO (FICTICIO)", "0012-2099 - BELTRANO DE EXEMPLO (FICTICIO)", 12, 2099, "BELTRANO DE EXEMPLO (FICTICIO)");
    static readonly Processo P0001Parecido = new(Arvore + @"\2099\0001 - 2099 - FULANO", "0001 - 2099 - FULANO", 1, 2099, "FULANO");
    static readonly IReadOnlyList<Processo> Todos = [P0012, P0001Parecido, P0001];

    /// <summary>Todas as peças achadas, plugin habilitado, API do Claude proibida pelo kit (como hoje).</summary>
    static readonly Pecas Completas = new(@"C:\x\codex.exe", @"C:\x\claude.exe", @"C:\x\python.exe", false, @"C:\x\pro_runtime.py", "1.6.1", "1.6.1", true, "2.4.0", false);

    [Fact]
    public void sugestao_do_processo()
    {
        Assert.Same(P0012, FluxoNovoRelatorio.Sugerir(Modulo.Processos, P0012, null, Todos));
        Assert.Null(FluxoNovoRelatorio.Sugerir(Modulo.Processos, null, P0001.Pasta + @"\Relatorio_0001_2099.rpmvni", Todos));

        // Relatórios: o processo cuja pasta contém o pacote aberto (também numa subpasta), nunca o de nome parecido.
        Assert.Same(P0001, FluxoNovoRelatorio.Sugerir(Modulo.Relatorios, P0012, P0001.Pasta + @"\Relatorio_0001_2099.rpmvni", Todos));
        Assert.Same(P0001, FluxoNovoRelatorio.Sugerir(Modulo.Relatorios, null, P0001.Pasta + @"\REVISOES\Relatorio_0001_2099_2.rpmvni", Todos));
        Assert.Same(P0001Parecido, FluxoNovoRelatorio.Sugerir(Modulo.Relatorios, null, P0001Parecido.Pasta + @"\Relatorio_0001_2099.rpmvni", Todos));
        Assert.Null(FluxoNovoRelatorio.Sugerir(Modulo.Relatorios, null, @"C:\outro\Relatorio_0001_2099.rpmvni", Todos));
        Assert.Null(FluxoNovoRelatorio.Sugerir(Modulo.Relatorios, null, null, Todos));

        Assert.Null(FluxoNovoRelatorio.Sugerir(Modulo.Despachos, P0012, P0001.Pasta + @"\Relatorio_0001_2099.rpmvni", Todos));
        Assert.Null(FluxoNovoRelatorio.Sugerir(Modulo.Ajustes, P0012, null, Todos));
    }

    [Fact]
    public void motivos_exatos_por_provedor()
    {
        const string Processo = "Escolha o processo.";
        const string EmAndamento = "Já há uma análise em andamento neste processo.";
        const string SemCodex = "O Codex não foi encontrado neste computador. Instale o app do ChatGPT/Codex ou escolha o programa em Ajustes › Inteligência artificial.";
        const string SemPlugin = "O plugin analise do kit PMVNI não está habilitado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).";
        const string SemChave = "Guarde a chave de API em Ajustes › Inteligência artificial ou use a sua conta.";
        const string SemPython = "O Python não foi encontrado neste computador (o análise pro do kit precisa dele).";
        const string SemPro = "O plugin análise pro do kit PMVNI não está instalado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).";
        const string SemClaude = "O Claude Code não foi encontrado neste computador (claude.exe).";

        string? M(Provedor p, Processo? pr = null, bool andamento = false, Pecas? pecas = null, ModoAcesso modo = ModoAcesso.Conta, bool chave = false) =>
            FluxoNovoRelatorio.MotivoDesabilitado(p, pr, andamento, pecas ?? Completas, modo, chave);

        var nada = new Pecas(null, null, null, false, null, null, null, false, null, false);
        foreach (var p in Enum.GetValues<Provedor>())
        {
            Assert.Equal(Processo, M(p, null, true, nada, ModoAcesso.Chave));                      // o processo vem antes de tudo
            Assert.Equal(EmAndamento, M(p, P0001, true, nada, ModoAcesso.Chave));                  // depois, a execução em andamento
            Assert.Null(M(p, P0001));                                                              // tudo certo
        }

        // ChatGPT: programa, plugin e chave, nessa ordem.
        Assert.Equal(SemCodex, M(Provedor.ChatGpt, P0001, pecas: nada, modo: ModoAcesso.Chave));
        Assert.Equal(SemPlugin, M(Provedor.ChatGpt, P0001, pecas: Completas with { PluginAnaliseHabilitado = false }, modo: ModoAcesso.Chave));
        Assert.Equal(SemChave, M(Provedor.ChatGpt, P0001, modo: ModoAcesso.Chave));
        Assert.Null(M(Provedor.ChatGpt, P0001, modo: ModoAcesso.Chave, chave: true));
        Assert.Null(M(Provedor.ChatGpt, P0001, pecas: Completas with { Python = null, ProRuntime = null, Claude = null }));   // as peças do Claude não importam

        // Claude: Python, análise pro, Claude Code e o modo, nessa ordem.
        Assert.Equal(SemPython, M(Provedor.Claude, P0001, pecas: nada, modo: ModoAcesso.Chave));
        Assert.Equal(SemPro, M(Provedor.Claude, P0001, pecas: Completas with { ProRuntime = null, Claude = null }, modo: ModoAcesso.Chave));
        Assert.Equal(SemClaude, M(Provedor.Claude, P0001, pecas: Completas with { Claude = null }, modo: ModoAcesso.Chave));
        Assert.Null(M(Provedor.Claude, P0001, modo: ModoAcesso.Chave, chave: true));    // o kit proíbe API: o análise pro roda pela conta
        Assert.Null(M(Provedor.Claude, P0001, modo: ModoAcesso.Chave));                 // nem precisa de chave guardada para isso
        Assert.Equal(SemChave, M(Provedor.Claude, P0001, pecas: Completas with { ApiPermitidaNoKit = true }, modo: ModoAcesso.Chave));
        Assert.Null(M(Provedor.Claude, P0001, pecas: Completas with { ApiPermitidaNoKit = true }, modo: ModoAcesso.Chave, chave: true));
        Assert.Null(M(Provedor.Claude, P0001, pecas: Completas with { Codex = null, PluginAnaliseHabilitado = false }));
    }

    [Fact]
    public void aviso_uma_vez_por_versao()
    {
        Assert.True(FluxoNovoRelatorio.PrecisaAviso(new AjustesAgente()));
        Assert.False(FluxoNovoRelatorio.PrecisaAviso(new AjustesAgente { AvisoVersao = 1 }));
        Assert.Equal(1, Ajustes.VersaoAviso);
        // Com a versão 2 do texto, quem aceitou a 1 é perguntado de novo (o mesmo critério, sem depender da constante).
        Assert.True(FluxoNovoRelatorio.PrecisaAviso(new AjustesAgente { AvisoVersao = 1 }, versaoAtual: 2));
        Assert.False(FluxoNovoRelatorio.PrecisaAviso(new AjustesAgente { AvisoVersao = 2 }, versaoAtual: 2));

        Assert.Equal("Analisar com o ChatGPT inicia o Codex instalado neste computador, com a sua configuração e o plugin analise do kit PMVNI. O Codex lê os "
            + "arquivos do processo, usa os programas e as pastas de rede deste computador e envia à OpenAI o pedido e o conteúdo de que precisar, como "
            + "faz no app do ChatGPT. O editor não envia nada por conta própria: ele só inicia o Codex, mostra o andamento e importa o relatório no fim.",
            FluxoNovoRelatorio.TextoAviso(Provedor.ChatGpt));
        Assert.Equal("Analisar com o Claude inicia o análise pro do kit PMVNI, que roda sozinho numa janela minimizada e chama o Claude Code deste computador "
            + "com a sua conta do Claude (Sonnet 5.5 e Opus 5.5, como o kit define). O Claude Code lê os arquivos do processo, usa os programas e as "
            + "pastas de rede deste computador e envia à Anthropic o conteúdo de que precisar. O editor não envia nada por conta própria: ele só dá a "
            + "partida, mostra o andamento e importa o relatório no fim.",
            FluxoNovoRelatorio.TextoAviso(Provedor.Claude));
    }

    [Fact]
    public void titulo_da_execucao()
    {
        Assert.Equal("Analisando com o ChatGPT · 0001/2099 · 0 min", FluxoNovoRelatorio.TituloExecucao(Provedor.ChatGpt, P0001, TimeSpan.Zero));
        Assert.Equal("Analisando com o ChatGPT · 0001/2099 · 12 min", FluxoNovoRelatorio.TituloExecucao(Provedor.ChatGpt, P0001, TimeSpan.FromSeconds(12 * 60 + 59)));
        Assert.Equal("Analisando com o ChatGPT · 0001/2099 · 2 h 05 min", FluxoNovoRelatorio.TituloExecucao(Provedor.ChatGpt, P0001, TimeSpan.FromMinutes(125)));
        Assert.Equal("Analisando com o Claude · 0012/2099 · 1 h 00 min", FluxoNovoRelatorio.TituloExecucao(Provedor.Claude, P0012, TimeSpan.FromMinutes(60)));
        Assert.Equal("Analisando com o Claude · 0012/2099 · 59 min", FluxoNovoRelatorio.TituloExecucao(Provedor.Claude, P0012, TimeSpan.FromMinutes(59.9)));
        var foraDoPadrao = new Processo(@"C:\p\2099\pasta fora do padrao", "pasta fora do padrao", null, null, null);
        Assert.Equal("Analisando com o Claude · pasta fora do padrao · 3 min", FluxoNovoRelatorio.TituloExecucao(Provedor.Claude, foraDoPadrao, TimeSpan.FromMinutes(3)));
        Assert.Equal("Analisando com o Claude · 0012/2099 · 0 min", FluxoNovoRelatorio.TituloExecucao(Provedor.Claude, P0012, TimeSpan.FromSeconds(-5)));   // relógio que voltou
    }

    [Fact]
    public void etapas_em_portugues_e_estado()
    {
        var nomes = new[] { "Preparo", "Análise", "Auditoria técnica", "Produção do relatório", "Auditoria cruzada", "Revisão final" };
        var e = FluxoNovoRelatorio.Etapas("technical_audit", "EM_ANDAMENTO");
        Assert.Equal(nomes, e.Select(x => x.Nome));
        Assert.Equal(new[] { EstadoEtapa.Feita, EstadoEtapa.Feita, EstadoEtapa.Agora, EstadoEtapa.Pendente, EstadoEtapa.Pendente, EstadoEtapa.Pendente }, e.Select(x => x.Estado));
        Assert.All(FluxoNovoRelatorio.Etapas("final_review", "CONCLUIDO"), x => Assert.Equal(EstadoEtapa.Feita, x.Estado));
        Assert.All(FluxoNovoRelatorio.Etapas(null, "CONCLUIDO"), x => Assert.Equal(EstadoEtapa.Feita, x.Estado));
        Assert.All(FluxoNovoRelatorio.Etapas(null, "INICIADA"), x => Assert.Equal(EstadoEtapa.Pendente, x.Estado));
        Assert.All(FluxoNovoRelatorio.Etapas("etapa_desconhecida", "EM_ANDAMENTO"), x => Assert.Equal(EstadoEtapa.Pendente, x.Estado));
        Assert.Equal(EstadoEtapa.Agora, FluxoNovoRelatorio.Etapas("prepare", "EM_ANDAMENTO")[0].Estado);
        Assert.Equal(6, FluxoNovoRelatorio.Etapas("final_review", "EM_ANDAMENTO").Count(x => x.Estado != EstadoEtapa.Pendente));
    }

    [Fact]
    public void previa_do_destino()
    {
        using var t = Ambiente.PastaDeTeste("fluxo-novo-relatorio");
        var processo = CriacaoProcesso.Criar(t, 1, 2099, "Fulano de Exemplo").Processo.Pasta;
        Directory.CreateDirectory(Path.Combine(processo, "PROJETO", "REVISÃO 02"));
        var origem = Path.Combine(t, "origem");
        Directory.CreateDirectory(origem);
        var planta = Path.Combine(origem, "planta.pdf");
        File.WriteAllText(planta, "planta", Encoding.UTF8);
        File.Copy(planta, Path.Combine(processo, "DOCUMENTOS", "planta.pdf"));
        Directory.CreateDirectory(Path.Combine(origem, "fotos"));

        var plano = Arquivamento.Planejar(processo, [(planta, DestinoAnexo.ReapresentacaoNova), (planta, DestinoAnexo.Documento), (Path.Combine(origem, "fotos"), DestinoAnexo.Documento)]);
        Assert.Equal(@"PROJETO\REVISÃO 03\planta.pdf", FluxoNovoRelatorio.PreviaDestino(plano[0]));
        Assert.Equal("já está no processo (mesmo conteúdo)", FluxoNovoRelatorio.PreviaDestino(plano[1]));
        Assert.Equal("Arraste arquivos, não pastas: fotos.", FluxoNovoRelatorio.PreviaDestino(plano[2]));
        Assert.False(Directory.Exists(Path.Combine(processo, "PROJETO", "REVISÃO 03")));     // a prévia não grava nada
    }

    [Fact]
    public void opcoes_de_destino_e_textos_da_pagina()
    {
        Assert.Equal(new[] { DestinoAnexo.Documento, DestinoAnexo.ProjetoInicial, DestinoAnexo.ReapresentacaoNova },
            FluxoNovoRelatorio.OpcoesDestino(null).Select(o => o.Destino));
        Assert.Equal(new[] { "Documento (DOCUMENTOS)", @"Projeto — primeira apresentação (PROJETO\INICIAL)", "Projeto — reapresentação (nova REVISÃO NN)" },
            FluxoNovoRelatorio.OpcoesDestino(null).Select(o => o.Rotulo));
        var comRevisao = FluxoNovoRelatorio.OpcoesDestino("REVISÃO 02");
        Assert.Equal(DestinoAnexo.ReapresentacaoAtual, comRevisao[^1].Destino);
        Assert.Equal("Projeto — juntar à REVISÃO 02 atual", comRevisao[^1].Rotulo);

        Assert.Equal("O pedido vai para os dois plugins como diretriz complementar (ex.: conferir pendências de despachos anteriores); as etapas da análise não mudam.", FluxoNovoRelatorio.TextoPedidoClaude);
        Assert.Equal("planta.pdf não é um arquivo do disco; salve-o numa pasta e anexe de lá.", FluxoNovoRelatorio.ItemSemCaminho("planta.pdf"));
        Assert.Equal("Relatório da análise com o ChatGPT importado: Relatorio_0001_2099.rpmvni. O PDF do kit continua em C:\\p\\relatorio.pdf.",
            FluxoNovoRelatorio.TextoImportado(Provedor.ChatGpt, @"C:\p\Relatorio_0001_2099.rpmvni", @"C:\p\relatorio.pdf"));
        Assert.Equal("Relatório da análise com o Claude importado: Relatorio_0001_2099_2.rpmvni.",
            FluxoNovoRelatorio.TextoImportado(Provedor.Claude, @"C:\p\Relatorio_0001_2099_2.rpmvni", null));
        Assert.Equal("A análise com o ChatGPT em 0001/2099 será pausada. Ao abrir o editor de novo, você poderá retomá-la de onde parou.", FluxoNovoRelatorio.TextoFecharChatGpt(P0001));
        Assert.Equal("A análise com o Claude continua sozinha na janela minimizada do kit. Ao abrir o editor de novo, você poderá voltar a acompanhá-la ou retomá-la de onde parou.", FluxoNovoRelatorio.TextoFecharClaude);
        Assert.Equal("A análise com o Claude de 0012/2099 terminou.", FluxoNovoRelatorio.TextoClaudeTerminou(P0012));
        Assert.Equal("O registro da análise com o Claude de 0012/2099 não tem a hora de início (execucao.json em C:\\r); o editor não a acompanha nem importa "
            + "o resultado sem ela. Quando o kit terminar, importe pelo módulo Processos › Importar análise da IA.", FluxoNovoRelatorio.TextoSemInicio(P0012, @"C:\r"));
        Assert.Equal("O relatório da análise com o Claude de 0001/2099 ainda não foi importado. Use Importar relatório para tentar de novo.",
            FluxoNovoRelatorio.TextoNaoImportado(Provedor.Claude, P0001));
        Assert.Equal("O processo 9998/2099 já existe: C:\\p\\2099\\9998 - 2099 - X.",FluxoNovoRelatorio.TextoProcessoExiste(9998, 2099, @"C:\p\2099\9998 - 2099 - X"));
    }

    [Fact]
    public void dados_do_novo_processo()
    {
        Assert.Equal(new DadosNovoProcesso(9998, 2099, "Fulána de Tál"), FluxoNovoRelatorio.LerNovoProcesso(" 9998 ", "2099", " Fulána de Tál ", 2098));
        Assert.Equal(new DadosNovoProcesso(123456, 2027, "X"), FluxoNovoRelatorio.LerNovoProcesso("123456", "2027", "X", 2026));    // ano atual + 1
        Assert.Equal(new DadosNovoProcesso(1, 1990, "X"), FluxoNovoRelatorio.LerNovoProcesso("1", "1990", "X", 2026));
        foreach (var (n, a, r) in new[]
        {
            ("", "2026", "X"), ("0", "2026", "X"), ("1234567", "2026", "X"), ("12a", "2026", "X"), ("-1", "2026", "X"),
            ("1", "1989", "X"), ("1", "2028", "X"), ("1", "26", "X"), ("1", "20266", "X"), ("1", "2026", "  "), ("1", "2026", ""),
        })
            Assert.Null(FluxoNovoRelatorio.LerNovoProcesso(n, a, r, 2026));
    }
}
