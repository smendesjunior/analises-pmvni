using System.Globalization;
using Pmvni.Agentes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>O que "Criar processo…" pede (desenho §12.3): número, ano do protocolo e requerente, já conferidos.</summary>
public sealed record DadosNovoProcesso(int Numero, int Ano, string Requerente);

/// <summary>Em que pé está uma etapa do análise pro na vista da execução.</summary>
public enum EstadoEtapa { Feita, Agora, Pendente }

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
    public const string SemPython = "O Python não foi encontrado neste computador (o análise pro do kit precisa dele).";
    public const string SemAnalisePro = "O plugin análise pro do kit PMVNI não está instalado no Codex. Rode o INSTALAR_KIT_PMVNI.cmd (fora do editor).";
    public const string SemClaude = "O Claude Code não foi encontrado neste computador (claude.exe).";
    public const string ClaudeSoConta = "O análise pro do kit usa somente a conta do Claude (política do kit de 14/11/2026). Em Ajustes › Inteligência artificial, use Usar minha conta.";

    /// <summary>Sob "Analisar com o Claude" (desenho §12.5).</summary>
    public const string TextoPedidoClaude = "O pedido vai para os dois plugins como diretriz complementar (ex.: conferir pendências de despachos anteriores); as etapas da análise não mudam.";

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
    public const string FecharTitulo = "Cancelar a análise e fechar?";
    public const string FecharSim = "Cancelar análise e fechar";
    public const string FecharNao = "Continuar";
    public const string TextoFecharClaude = "A análise com o Claude continua sozinha na janela minimizada do kit. Ao abrir o editor de novo, o resultado é importado.";
    public const string AvisoSim = "Concordo e analisar";
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
    public static string? MotivoDesabilitado(Provedor p, Processo? processo, bool emAndamento, Pecas pecas, ModoAcesso modo, bool chaveGuardada)
    {
        ArgumentNullException.ThrowIfNull(pecas);
        if (processo is null) return EscolhaOProcesso;
        if (emAndamento) return EmAndamento;
        if (p == Provedor.ChatGpt)
        {
            if (pecas.Codex is null) return SemCodex;
            if (!pecas.PluginAnaliseHabilitado) return SemPlugin;
        }
        else
        {
            if (pecas.Python is null) return SemPython;
            if (pecas.ProRuntime is null) return SemAnalisePro;
            if (pecas.Claude is null) return SemClaude;
            if (modo == ModoAcesso.Chave && !pecas.ApiPermitidaNoKit) return ClaudeSoConta;
        }
        if (modo == ModoAcesso.Chave && !chaveGuardada) return SemChave;
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
    public static string TituloExecucao(Provedor p, Processo pr, TimeSpan decorrido)
    {
        var minutos = Math.Max(0, (long)Math.Floor(decorrido.TotalMinutes));
        var tempo = minutos < 60
            ? minutos.ToString(CultureInfo.InvariantCulture) + " min"
            : string.Create(CultureInfo.InvariantCulture, $"{minutos / 60} h {minutos % 60:D2} min");
        return $"Analisando com o {Provedores.Nome(p)} · {Identificacao(pr)} · {tempo}";
    }

    /// <summary>As seis etapas do controlador do kit com o nome em português e o estado: antes da atual, feitas; a atual, agora; depois, pendentes.
    /// <c>CONCLUIDO</c> ⇒ todas feitas; sem etapa (ou etapa desconhecida) ⇒ todas pendentes.</summary>
    public static IReadOnlyList<(string Nome, EstadoEtapa Estado)> Etapas(string? etapaAtual, string codigo)
    {
        var etapas = SituacaoAnalisePro.Etapas;
        var concluido = codigo == "CONCLUIDO";
        var atual = etapaAtual is null ? -1 : etapas.ToList().IndexOf(etapaAtual);
        return etapas.Select((e, i) => (SituacaoAnalisePro.NomeEtapa(e),
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

    /// <summary>A análise terminou com o usuário fazendo outra coisa (Ruling FD-I3): o <c>.rpmvni</c> foi criado e nada do que estava aberto mudou.</summary>
    public static string TextoRelatorioPronto(Provedor p, string pmvrel, string? pdf) =>
        $"A análise com o {Provedores.Nome(p)} terminou: {Path.GetFileName(pmvrel)} foi criado em {Path.GetFileName(Path.GetDirectoryName(pmvrel))} e está nos Recentes."
        + (string.IsNullOrWhiteSpace(pdf) ? "" : $" O PDF do kit continua em {pdf}.");

    public static string TextoFecharChatGpt(Processo p) => $"A análise com o ChatGPT em {Identificacao(p)} será interrompida.";

    public static string TextoClaudeTerminou(Processo p) => $"A análise com o Claude de {Identificacao(p)} terminou.";

    /// <summary>O <c>execucao.json</c> do registro não tem a hora de início: sem ela não há como saber se o resultado do kit é desta análise.</summary>
    public static string TextoSemInicio(Processo p, string registro) =>
        $"O registro da análise com o Claude de {Identificacao(p)} não tem a hora de início (execucao.json em {registro}); o editor não a acompanha nem importa "
        + "o resultado sem ela. Quando o kit terminar, importe pelo módulo Processos › Importar análise da IA.";

    /// <summary>A importação do relatório que o kit declarou não aconteceu (falhou ou foi cancelada): "Importar relatório" tenta de novo.</summary>
    public static string TextoNaoImportado(Provedor p, Processo pr) =>
        $"O relatório da análise com o {Provedores.Nome(p)} de {Identificacao(pr)} ainda não foi importado. Use Importar relatório para tentar de novo.";

    public static string TextoProcessoExiste(int numero, int ano, string pasta) =>
        string.Create(CultureInfo.InvariantCulture, $"O processo {numero:D4}/{ano} já existe: {pasta}.");

    /// <summary>Os campos do diálogo "Criar processo": número de 1 a 6 dígitos (≥ 1), ano de 4 dígitos entre 1990 e o ano atual + 1 e requerente não vazio.
    /// null = "Criar" fica desabilitado.</summary>
    public static DadosNovoProcesso? LerNovoProcesso(string numero, string ano, string requerente, int anoAtual)
    {
        numero = (numero ?? "").Trim();
        ano = (ano ?? "").Trim();
        requerente = (requerente ?? "").Trim();
        if (numero.Length is < 1 or > 6 || !numero.All(char.IsAsciiDigit)) return null;
        if (ano.Length != 4 || !ano.All(char.IsAsciiDigit)) return null;
        var n = int.Parse(numero, CultureInfo.InvariantCulture);
        var a = int.Parse(ano, CultureInfo.InvariantCulture);
        if (n < 1 || a < 1990 || a > anoAtual + 1 || requerente.Length == 0) return null;
        return new DadosNovoProcesso(n, a, requerente);
    }
}
