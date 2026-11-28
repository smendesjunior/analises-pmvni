using System.Globalization;
using Pmvni.Agentes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O que "Criar processo…" pede (desenho §12.3): número, ano do protocolo e requerente, já conferidos.</summary>
public sealed record DadosNovoProcesso(string Numero, int Ano, string Requerente)
{
    public DadosNovoProcesso(int numero, int ano, string requerente) : this(numero.ToString(CultureInfo.InvariantCulture), ano, requerente) { }
}

/// <summary>Em que pé está uma etapa do análise pro na vista da execução.</summary>
public enum EstadoEtapa { Feita, Agora, Pendente, Erro }

/// <summary>Uma opção da caixa Destino de um anexo.</summary>
public sealed record OpcaoDestino(DestinoAnexo Destino, string Rotulo);

/// <summary>Novo relatório pelos agentes locais (Tarefa 20; desenho §12.2, §12.5–§12.8, §12.11): o processo sugerido, por que um botão "Analisar" está
/// desabilitado, quando pedir o aviso, os textos fixos da página, o título da execução e as etapas do análise pro. Puro (sem WinUI e sem disco), compilado
/// também nos testes. O editor não analisa nada: só inicia o agente do computador (Pmvni.Agentes) e importa o que o kit declara.</summary>
public static class FluxoNovoRelatorio
{
    public const string EscolhaOProcesso = "Escolha o processo.";
    public const string EmAndamento = "Já há uma análise em andamento neste processo.";
    public const string SemCodex = "O Codex não foi encontrado neste computador. Instale o app do ChatGPT/Codex ou escolha o programa em Ajustes › Inteligência artificial.";
    public const string SemPlugin = "O plugin analise do kit PMVNI não está habilitado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).";
    public const string SemChave = "Guarde a chave de API em Ajustes › Inteligência artificial ou use a sua conta.";
    public const string SemPython = "O Python não foi encontrado neste computador (a rota Claude do plugin analise precisa dele).";
    public const string SemAnalisePro = "A rota Claude do plugin analise do kit PMVNI não está instalada no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).";
    public const string SemClaude = "O Claude Code não foi encontrado neste computador (claude.exe).";
    public const string SemPythonGoogle = "O Python não foi encontrado neste computador (a rota Gemini do plugin analise precisa dele).";
    public const string SemAnaliseGoogle = OrquestradorAnalise.SemAnaliseGoogle;
    public const string SemAgy = OrquestradorAnalise.SemAgy;
    public const string SemPerfilApi = OrquestradorAnalise.SemPerfilApi;
    public const string CancelarGeminiTitulo = "Cancelar a análise com o Gemini?";
    /// <summary>"Analisar com o Claude" desabilitado: modo chave (Anthropic API) com um análise pro que não aceita a rota API (anterior à 1.6.4).</summary>
    public const string ClaudeSoConta = "A rota Claude do plugin analise instalado não aceita a Anthropic API (é preciso o 1.6.4 ou mais novo). Atualize pelo INSTALAR_KIT_PMVNI.cmd ou, em Ajustes › Inteligência artificial, use Usar minha conta.";

    /// <summary>Sob "Analisar com o Claude" (desenho §12.5).</summary>
    public const string TextoPedidoClaude = "A rota Claude do plugin analise usa o Claude para analisar o processo. No campo “Pedido”, você pode indicar algo que merece atenção, como uma pendência de despacho anterior. A análise segue as etapas habituais.";

    public const string DicaPedido = "Ex.: Analise a reapresentação do projeto e confira as pendências do despacho anterior.";
    public const int MaximoPedido = 20000;
    public const string ArrasteAqui = "Arraste arquivos para cá";
    public const string SolteParaAnexar = "Soltar para anexar";
    public const string JaEstaNoProcesso = "já está no processo (mesmo conteúdo)";

    // Confirmações e avisos da vista da execução e do fechamento (desenho §12.5–§12.7).
    public const string CancelarChatGptTitulo = "Cancelar a análise com o ChatGPT?";
    public const string CancelarChatGptTexto = "O Codex será encerrado; o que ele já gravou na pasta do processo fica como está.";
    public const string CancelarClaudeTitulo = "Cancelar a análise com o Claude?";
    public const string CancelarClaudeTexto = "O kit guarda o que já foi feito; Continuar a análise retoma de onde parou.";
    public const string CancelarAnalise = "Cancelar análise";
    public const string Voltar = "Voltar";
    public const string RetomarTitulo = "Retomar a análise?";
    public const string PedidoContinuar = "Continue a análise deste processo de onde ela parou: aproveite os arquivos de trabalho já gravados na pasta .pmvni-ai e não refaça o que já foi feito.";
    public const string FecharTitulo = "Pausar a análise e fechar?";
    public const string FecharSim = "Pausar e fechar";
    public const string FecharNao = "Continuar";
    public const string AvisoSim = "Concordar e analisar";
    public const string AvisoNao = "Cancelar";

    public static string AvisoTitulo(Provedor p) => "Analisar com o " + Provedores.Nome(p);

    /// <summary>De Processos: o processo selecionado. De Relatórios: o processo cuja pasta contém o <c>.rpmvni</c> aberto (também numa subpasta dele). Senão, nenhum.</summary>
    public static Processo? Sugerir(Modulo origem, Processo? selecionado, string? pacoteAberto, IReadOnlyList<Processo> processos)
    {
        ArgumentNullException.ThrowIfNull(processos);
        if (origem == Modulo.Processos) return selecionado;
        if (origem != Modulo.Relatorios || string.IsNullOrWhiteSpace(pacoteAberto)) return null;
        string pacote;
        try { pacote = Path.GetFullPath(pacoteAberto); }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
        // A pasta mais longa que contém o pacote (uma pasta de processo nunca fica dentro de outra, mas a mais específica é a certa).
        return processos
            .Where(p => Contem(p.Pasta, pacote))
            .OrderByDescending(p => p.Pasta.Length)
            .FirstOrDefault();
    }

    static bool Contem(string pasta, string arquivo)
    {
        try
        {
            var raiz = Path.GetFullPath(pasta).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return arquivo.StartsWith(raiz, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    /// <summary>Por que o botão do provedor está desabilitado (desenho §12.11, nesta ordem: processo, execução em andamento, programa(s), plugin, modo/chave);
    /// null = pode analisar. As peças de um provedor não contam para o outro.</summary>
    /// <param name="perfilGemini">Só o Gemini: o perfil que vale para o modo (<see cref="MatrizGemini.PerfilDoModo"/>); null no modo chave = perfil não escolhido.</param>
    public static string? MotivoDesabilitado(Provedor p, Processo? processo, bool emAndamento, Pecas pecas, ModoAcesso modo, bool chaveGuardada, PerfilGemini? perfilGemini = null, bool usarAnaliseChatGpt = false)
    {
        if (!Provedores.Ativos.Contains(p)) return Provedores.RotaDesativada;
        ArgumentNullException.ThrowIfNull(pecas);
        if (processo is null) return EscolhaOProcesso;
        if (emAndamento) return EmAndamento;
        if (usarAnaliseChatGpt) return OrquestradorAnalise.MotivoDoPlugin(p, pecas);
        if (p == Provedor.ChatGpt)
        {
            if (pecas.Codex is null) return SemCodex;
            if (!pecas.PluginAnaliseHabilitado) return SemPlugin;
            // Tarefa 12: o editor roda o controlador do analise com o Python deste computador; só o analise 3.1.0+ tem o contrato que o editor lê.
            if (pecas.Python is null) return OrquestradorAnalise.SemPythonAnalise;
            if (PoliticaKit.PluginDesatualizado(p, pecas.VersaoPluginAnalise) is { } antigo) return antigo;
            if (pecas.Analise62 is null) return OrquestradorAnalise.SemControladorAnalise(pecas.VersaoPluginAnalise);
        }
        else if (p == Provedor.Gemini)
        {
            if (pecas.Python is null) return SemPythonGoogle;
            // Tarefa 18: um analise anterior ao 4.0.0 não tem o controlador da rota: pede a atualização do kit, não "não instalado".
            if (pecas.VersaoAnaliseGoogle is not null && PoliticaKit.PluginDesatualizado(p, pecas.VersaoAnaliseGoogle) is { } kitAntigo) return kitAntigo;
            if (pecas.GoogleRuntime is null) return SemAnaliseGoogle;
            if (modo != ModoAcesso.ContaGemini && pecas.Agy is null) return SemAgy;
            if (modo == ModoAcesso.Chave && perfilGemini is not (PerfilGemini.ApiCreditos or PerfilGemini.ApiGratuita)) return SemPerfilApi;
        }
        else
        {
            if (pecas.Python is null) return SemPython;
            if (pecas.VersaoAnalisePro is not null && PoliticaKit.PluginDesatualizado(p, pecas.VersaoAnalisePro) is { } kitAntigo) return kitAntigo;
            if (pecas.ProRuntime is null) return SemAnalisePro;
            if (pecas.Claude is null) return SemClaude;
        }
        if (p != Provedor.ChatGpt && PoliticaKit.PluginDesatualizado(p, p == Provedor.Gemini ? pecas.VersaoAnaliseGoogle : pecas.VersaoAnalisePro) is { } desatualizado)
            return desatualizado;
        // Modo chave = Anthropic API de fato (2026-11-17): sem plugin que a aceite, não há análise (nunca a conta no lugar).
        if (p == Provedor.Claude && modo == ModoAcesso.Chave && !pecas.ApiPermitidaNoKit) return ClaudeSoConta;
        if (OrquestradorAnalise.ModoEfetivo(p, modo, pecas) == ModoAcesso.Chave && !chaveGuardada) return SemChave;
        return null;
    }

    /// <summary>O aviso é pedido antes da primeira análise de cada provedor e de novo quando o texto muda (versão maior que a aceita).</summary>
    public static bool PrecisaAviso(AjustesAgente a, int versaoAtual = Ajustes.VersaoAviso)
    {
        ArgumentNullException.ThrowIfNull(a);
        return a.AvisoVersao is not { } v || v < versaoAtual;
    }

    /// <summary>O texto do aviso (desenho §12.8, versão <see cref="Ajustes.VersaoAviso"/>): o mesmo de Ajustes › Rever aviso.</summary>
    public static string TextoAviso(Provedor p) => TextosAgentes.Aviso(p);

    /// <summary>"0001/2099", ou o nome da pasta quando ela foge do padrão.</summary>
    public static string Identificacao(Processo p) => ListasPastas.NumeroDoProcesso(p) ?? p.NomePasta;

    /// <summary>"Analisando com o ChatGPT · 0001/2099 · 12 min"; a partir de 60 min, "2 h 05 min".</summary>
    public static string TituloExecucao(Provedor p, Processo pr, TimeSpan decorrido) =>
        $"{TituloExecucao(p, pr)} · {TempoLegivel(decorrido)}";

    public static string TituloExecucao(Provedor p, Processo pr) => $"Analisando com o {Provedores.Nome(p)} · {Identificacao(pr)}";

    internal static string TempoLegivel(TimeSpan decorrido, bool incluirSegundos = false)
    {
        var segundos = Math.Max(0, (long)decorrido.TotalSeconds);
        var minutos = segundos / 60;
        var sufixo = incluirSegundos ? string.Create(CultureInfo.InvariantCulture, $" {segundos % 60:D2} s") : "";
        return minutos switch
        {
            0 => string.Create(CultureInfo.InvariantCulture, $"{segundos} s"),
            < 60 => string.Create(CultureInfo.InvariantCulture, $"{minutos} min{sufixo}"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{minutos / 60} h {minutos % 60:D2} min{sufixo}"),
        };
    }

    /// <summary>Sem previsão porque o kit ainda não registrou nenhuma etapa desta análise (o Editor calcula a previsão desde a Tarefa 13; nada vem da IA).</summary>
    internal const string PrevisaoDepoisDasEtapas = "Aguardando as primeiras medições da execução";

    /// <summary>Sem previsão porque falta histórico para alguma etapa pendente.</summary>
    internal const string HistoricoInsuficiente = "Término: histórico insuficiente para prever";

    const string RotuloEstimativaInicial = "Término pela estimativa inicial (sem histórico suficiente)";

    /// <param name="peloEditor">A previsão é do Editor (Tarefa 13: mediana das execuções anteriores), não a <c>estimativa.json</c> que a IA gravava numa
    /// análise antiga. Só no caminho antigo aparecem os textos "…estimativa da IA".</param>
    /// <param name="semDados">O kit ainda não registrou nenhuma etapa desta análise.</param>
    /// <param name="aproximada">A previsão do Editor usou poucas amostras ou amostras de outros provedores.</param>
    /// <param name="inicial">O cálculo do Editor usou durações iniciais.</param>
    internal static string TextoEstimativa(DateTimeOffset? termino, DateTimeOffset agora, TimeSpan? restantePeloRitmo = null, bool peloEditor = false,
        bool semDados = false, bool aproximada = false, bool inicial = false)
    {
        var peloRitmo = termino is null || termino <= agora;
        if (peloRitmo && restantePeloRitmo is { } restante && restante > TimeSpan.Zero)
            termino = agora.Add(restante);
        if (termino is not { } hora) return !peloEditor ? "Aguardando estimativa da IA" : semDados ? PrevisaoDepoisDasEtapas : HistoricoInsuficiente;
        if (hora <= agora) return !peloEditor ? "Aguardando nova estimativa da IA"
            : "A previsão anterior expirou. Aguardando uma nova medição.";
        var formato = hora.Date == agora.Date ? "HH:mm" : "dd/MM HH:mm";
        var rotulo = peloEditor && inicial ? RotuloEstimativaInicial
            : peloRitmo ? "Término aproximado pelo ritmo" : peloEditor && aproximada ? "Término aproximado" : "Término estimado";
        return $"{rotulo} · {hora.ToString(formato, CultureInfo.InvariantCulture)} ({TempoLegivel(hora - agora)} restantes)";
    }

    /// <summary>O valor curto de "Previsão de término" no Monitor (o texto completo, <see cref="TextoEstimativa"/>, vai na dica). Mesmas regras de
    /// <see cref="TextoEstimativa"/>. <paramref name="aproximada"/> e <paramref name="inicial"/> marcam a previsão com "≈".</summary>
    internal static string PrevisaoCurta(DateTimeOffset? termino, DateTimeOffset agora, TimeSpan? restantePeloRitmo = null, bool peloEditor = false,
        bool semDados = false, bool aproximada = false, bool inicial = false)
    {
        var peloRitmo = termino is null || termino <= agora;
        if (peloRitmo && restantePeloRitmo is { } restante && restante > TimeSpan.Zero) termino = agora.Add(restante);
        else peloRitmo = false;
        if (termino is not { } hora) return !peloEditor ? "Aguardando a IA" : semDados ? "Aguardando medições" : "Histórico insuficiente";
        if (hora <= agora) return peloEditor ? "Aguardando nova medição" : "Aguardando a IA";
        var formato = hora.Date == agora.Date ? "HH:mm" : "dd/MM HH:mm";
        return $"{(aproximada || inicial || peloRitmo ? "≈ " : "")}{hora.ToString(formato, CultureInfo.InvariantCulture)} · {TempoLegivel(hora - agora)}";
    }

    /// <summary>"Seções 1–9 · 4 de 9" (ou "concluída", "aguardando") de uma frente paralela da etapa Análise.</summary>
    internal static string TextoFrente(FrenteAnalise f) =>
        $"Seções {f.Inicio}–{f.Fim} · " + (f.Concluida ? "concluída" : f.Iniciada ? $"{f.Prontas} de {f.Total}" : "aguardando");

    /// <summary>A objeção pendente no cartão da etapa que a levantou. No fluxo linear ela bloqueia a entrega e não volta sozinha para a análise.</summary>
    internal static string TextoObjecao(ObjecaoPendente o) => "Objeção pendente, bloqueia a entrega: " + o.Motivo.Trim().TrimEnd('.') + ".";

    /// <summary>O aviso da reabertura quando a análise parou com objeção pendente: o Editor não retoma nem pergunta sozinho (a retomada refaria a etapa).</summary>
    internal static string TextoRetomadaComObjecao(ObjecaoPendente o) =>
        $"A etapa {NomeDaEtapa(o.Etapa)} registrou objeção pendente ({o.Motivo.Trim().TrimEnd('.')}). O Editor não retoma a análise sozinho: "
        + "confira a objeção no registro e use \"Retomar análise\" ou descarte a análise. "
        + $"Atenção: \"Retomar análise\" refaz a etapa {NomeDaEtapa(o.Etapa)} com a IA e gasta cota de novo.";

    /// <summary>O nome em português da etapa; uma etapa que o Editor não conhece (kit mais novo) aparece com o id legível ("etapa_nova" vira "Etapa nova").</summary>
    internal static string NomeDaEtapa(string etapa)
    {
        if (etapa == "salvar_relatorio") return "Validar e salvar";
        var nome = SituacaoAnalisePro.NomeEtapa(etapa);
        if (nome != etapa) return nome;
        var legivel = etapa.Replace('_', ' ').Trim();
        return legivel.Length == 0 ? etapa : char.ToUpperInvariant(legivel[0]) + legivel[1..];
    }

    /// <summary>As etapas do controlador do kit com o nome em português e o estado: antes da atual, feitas; a atual, agora; depois, pendentes.
    /// <c>CONCLUIDO</c> ⇒ todas feitas; sem etapa (ou etapa desconhecida) ⇒ todas pendentes. <paramref name="ordem"/>: a sequência da rota que o kit define
    /// (<see cref="OrdemDasEtapas.Para"/>, sem as etapas desligadas); null = a ordem padrão (kit 3.x).</summary>
    public static IReadOnlyList<(string Nome, EstadoEtapa Estado)> Etapas(string? etapaAtual, string codigo, IReadOnlyList<string>? ordem = null)
    {
        var etapas = ordem ?? OrdemDasEtapas.Padrao.Todas;
        var concluido = codigo == "CONCLUIDO";
        var atual = etapaAtual is null ? -1 : etapas.ToList().IndexOf(etapaAtual);
        return etapas.Select((e, i) => (NomeDaEtapa(e),
                concluido ? EstadoEtapa.Feita
                : atual < 0 ? EstadoEtapa.Pendente
                : i < atual ? EstadoEtapa.Feita
                : i == atual ? EstadoEtapa.Agora
                : EstadoEtapa.Pendente))
            .ToList();
    }

    /// <summary>A prévia de uma linha de anexo: o caminho final relativo ao processo, "já está no processo (mesmo conteúdo)" ou a recusa.</summary>
    public static string PreviaDestino(PlanoAnexo p)
    {
        ArgumentNullException.ThrowIfNull(p);
        if (p.Recusa is not null) return p.Recusa;
        return p.JaEstava ? JaEstaNoProcesso : p.Relativo ?? "";
    }

    /// <summary>As opções da caixa Destino (desenho §12.3); "juntar à REVISÃO NN atual" só existe se o processo já tem revisão.</summary>
    public static IReadOnlyList<OpcaoDestino> OpcoesDestino(string? revisaoAtual)
    {
        var lista = new List<OpcaoDestino>
        {
            new(DestinoAnexo.Documento, "Documento (DOCUMENTOS)"),
            new(DestinoAnexo.ProjetoInicial, @"Projeto — primeira apresentação (PROJETO\INICIAL)"),
            new(DestinoAnexo.ReapresentacaoNova, "Projeto — reapresentação (nova REVISÃO NN)"),
        };
        if (!string.IsNullOrWhiteSpace(revisaoAtual)) lista.Add(new(DestinoAnexo.ReapresentacaoAtual, $"Projeto — juntar à {revisaoAtual} atual"));
        return lista;
    }

    /// <summary>Um item arrastado que não é arquivo do disco (por exemplo, um anexo arrastado do e-mail).</summary>
    public static string ItemSemCaminho(string nome) => $"{nome} não é um arquivo do disco; salve-o numa pasta e anexe de lá.";

    /// <summary>A InfoBar de sucesso (desenho §12.6).</summary>
    public static string TextoImportado(Provedor p, string pmvrel, string? pdf) =>
        $"Relatório da análise com o {Provedores.Nome(p)} importado: {Path.GetFileName(pmvrel)}." + (string.IsNullOrWhiteSpace(pdf) ? "" : $" O PDF do kit continua em {pdf}.");

    /// <summary>A cópia dos anexos parou no meio: nada foi iniciado, mas o que já foi copiado ficou no processo.</summary>
    public static string TextoCopiaInterrompida(string erro, string pastaProcesso) =>
        $"{erro.TrimEnd('.')}. Nada foi iniciado. Os anexos já copiados ficaram em {pastaProcesso}.";

    public const string RelatorioProntoTitulo = "Relatório pronto";
    public const string AbrirRelatorio = "Abrir relatório";

    public static string TextoVersaoParaRevisao(string pacote, string? pdf) =>
        $"A análise tem etapas pendentes. {Path.GetFileName(pacote)} está nos Recentes para conferência do analista."
        + (string.IsNullOrWhiteSpace(pdf) ? "" : $" PDF para revisão em {pdf}.");

    /// <summary>A análise terminou com o usuário fazendo outra coisa (Ruling FD-I3): o <c>.rpmvni</c> foi criado e nada do que estava aberto mudou.</summary>
    public static string TextoRelatorioPronto(Provedor p, string pmvrel, string? pdf) =>
        $"A análise com o {Provedores.Nome(p)} terminou: {Path.GetFileName(pmvrel)} foi criado em {Path.GetFileName(Path.GetDirectoryName(pmvrel))} e está nos Recentes."
        + (string.IsNullOrWhiteSpace(pdf) ? "" : $" O PDF do kit continua em {pdf}.");

    public static string TextoFechar(Provedor provedor, Processo p) => $"A análise com o {Provedores.Nome(provedor)} em {Identificacao(p)} será pausada. Ao abrir o editor de novo, você poderá retomá-la de onde parou.";

    public static string TextoClaudeTerminou(Processo p) => TextoKitTerminou(Provedor.Claude, p);

    /// <summary>A análise do kit (Claude ou Gemini) terminou com o editor fechado.</summary>
    public static string TextoKitTerminou(Provedor ia, Processo p) => $"A análise com o {Provedores.Nome(ia)} de {Identificacao(p)} terminou.";

    /// <summary>O <c>execucao.json</c> do registro não tem a hora de início: sem ela não há como saber se o resultado do kit é desta análise.</summary>
    public static string TextoSemInicio(Processo p, string registro, Provedor ia = Provedor.Claude) =>
        $"O registro da análise com o {Provedores.Nome(ia)} de {Identificacao(p)} não tem a hora de início (execucao.json em {registro}); o editor não a acompanha nem importa "
        + "o resultado sem ela. Quando o kit terminar, importe pelo módulo Processos › Importar análise da IA.";

    /// <summary>A importação do relatório que o kit declarou não aconteceu (falhou ou foi cancelada): "Importar relatório" tenta de novo.</summary>
    public static string TextoNaoImportado(Provedor p, Processo pr) =>
        $"O relatório da análise com o {Provedores.Nome(p)} de {Identificacao(pr)} ainda não foi importado. Use Importar relatório para tentar de novo.";

    public static string TextoProcessoExiste(int numero, int ano, string pasta) =>
        string.Create(CultureInfo.InvariantCulture, $"O processo {numero:D4}/{ano} já existe: {pasta}.");

    public static string TextoProcessoExiste(string numero, int ano, string pasta) => $"O processo {numero}/{ano} já existe: {pasta}.";

    /// <summary>Os campos do diálogo "Criar processo": número positivo aceito pelo kit, ano de 4 dígitos entre 1990 e o ano atual + 1 e requerente não vazio.
    /// null = "Criar" fica desabilitado.</summary>
    public static DadosNovoProcesso? LerNovoProcesso(string numero, string ano, string requerente, int anoAtual)
    {
        numero = (numero ?? "").Trim();
        ano = (ano ?? "").Trim();
        requerente = (requerente ?? "").Trim();
        if (numero.Length == 0) return null;
        if (ano.Length != 4 || !ano.All(char.IsAsciiDigit)) return null;
        var a = int.Parse(ano, CultureInfo.InvariantCulture);
        if (a < 1990 || a > anoAtual + 1 || requerente.Length == 0) return null;
        return new DadosNovoProcesso(numero, a, requerente);
    }
}
