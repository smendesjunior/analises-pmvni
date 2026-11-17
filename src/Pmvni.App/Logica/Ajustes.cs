using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Agentes;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Ajustes de um agente local (ChatGPT ou Claude), sem nenhum segredo: a chave de API fica só no <c>CofreChaves</c> (DPAPI), nunca aqui.</summary>
public sealed class AjustesAgente
{
    /// <summary>Entra pela conta do programa (padrão) ou pela chave de API guardada.</summary>
    public ModoAcesso Modo { get; set; } = ModoAcesso.Conta;

    /// <summary>O programa escolhido em Ajustes (ChatGPT: <c>codex.exe</c>; Claude: <c>python.exe</c>); null = o padrão, achado pelo editor.</summary>
    public string? Executavel { get; set; }

    /// <summary>A versão do aviso que o usuário aceitou (<see cref="Ajustes.VersaoAviso"/>); null = ainda não aceitou.</summary>
    public int? AvisoVersao { get; set; }

    /// <summary>Quando aceitou o aviso (UTC).</summary>
    public DateTimeOffset? AvisoEm { get; set; }

    /// <summary>O aviso vigente já foi aceito (texto novo, com versão maior, pede de novo).</summary>
    public bool AvisoAceito => AvisoVersao is { } v && v >= Ajustes.VersaoAviso;
}

/// <summary>Uma análise do Claude que seguia quando o editor fechou (desenho §12.7): a pasta do processo e a do registro da execução
/// (<c>.pmvni-ai\editor\…</c>). Ao abrir, o editor consulta a situação de cada uma.</summary>
public sealed record ExecucaoClaudePendente(string Processo, string Registro);

/// <summary>Posição e tamanho da janela ao fechar (pixels da tela), para reabrir do mesmo jeito (Ajustes › Geral).</summary>
public sealed record JanelaGuardada(int X, int Y, int Largura, int Altura, bool Maximizada)
{
    /// <summary>Tamanho plausível (um valor estragado no arquivo não abre a janela minúscula nem gigante).</summary>
    public bool Valida => Largura is >= 320 and <= 20000 && Altura is >= 240 and <= 20000;
}

/// <summary>Ajustes locais do app em <c>&lt;raiz de dados&gt;\ajustes.json</c> (nunca no pacote): módulo e aba ativos, tema, intervalo
/// do salvamento automático, marcas de revisão, recentes, pasta de processos e pasta do kit PMVNI. Arquivo ilegível vira <c>ajustes.corrompido.json</c>
/// (sem sobrescrever outro) e o app segue com o padrão; valores fora do permitido voltam ao padrão.</summary>
public sealed class Ajustes
{
    public const string NomeArquivo = "ajustes.json";
    public static readonly IReadOnlyList<int> Intervalos = [15, 30, 60, 120];
    public static readonly IReadOnlyList<string> Temas = ["sistema", "claro", "escuro"];
    /// <summary>O máximo padrão de recentes (Ajustes › Geral › Arquivos recentes: <see cref="OpcoesRecentes"/>).</summary>
    public const int MaxRecentes = 10;
    public static readonly IReadOnlyList<int> OpcoesRecentes = [5, 10, 20];

    // Opções da tela de Ajustes (auditoria da interface, §3.3). Cada lista tem o padrão primeiro: o comportamento de antes desta rodada.
    public static readonly IReadOnlyList<string> OpcoesAbrirEm = ["ultimo", nameof(Modulo.Relatorios), nameof(Modulo.Despachos), nameof(Modulo.Processos), nameof(Modulo.Legislacao)];
    public static readonly IReadOnlyList<string> OpcoesFaixaRotulos = ["automatico", "sempre", "icones"];
    public static readonly IReadOnlyList<string> OpcoesZoomInicial = ["largura", "pagina", "100"];
    public static readonly IReadOnlyList<int> OpcoesRecuperacaoDias = [30, 7, 90];
    public static readonly IReadOnlyList<string> OpcoesProcessosOrdem = ["numero", "data", "requerente"];
    public static readonly IReadOnlyList<string> OpcoesDespachosAgrupar = ["data", "processo"];
    public static readonly IReadOnlyList<string> OpcoesDespachosOrdem = ["recentes", "antigos"];
    public static readonly IReadOnlyList<string> OpcoesLegislacaoEscopo = ["lei", "todas"];
    // A página do processo (2026-11-16): como os arquivos aparecem. Não estão na tela de Ajustes: a própria página muda e lembra.
    public static readonly IReadOnlyList<string> OpcoesArquivosExibicao = ["lista", "miniaturas"];
    public static readonly IReadOnlyList<string> OpcoesArquivosAgrupar = ["etapa", "tipo", "data", "nenhum"];
    public static readonly IReadOnlyList<string> OpcoesArquivosOrdenar = ["nome", "data", "tipo"];
    public const int MaxPedidoPadrao = 2000;

    /// <summary>A versão do texto do aviso dos agentes (desenho §12.8): texto novo pede o aceite de novo.</summary>
    public const int VersaoAviso = 1;

    /// <summary>Os ajustes dos dois agentes locais (sempre os dois, com o padrão no que faltar).</summary>
    public IReadOnlyDictionary<Provedor, AjustesAgente> Agentes { get; } = new Dictionary<Provedor, AjustesAgente>
    {
        [Provedor.ChatGpt] = new(),
        [Provedor.Claude] = new(),
    };

    /// <summary>A última versão vista de cada plugin do kit em Ajustes › Inteligência artificial (<c>"plugins_vistos"</c>; chave
    /// <c>chatgpt.analise</c>, <c>claude.analise</c>, <c>claude.analise-pro</c>): quando a instalada muda, o cartão avisa "Atualizado desde a última vez".
    /// "Restaurar os ajustes" não a apaga (não é uma opção).</summary>
    public Dictionary<string, string> PluginsVistos { get; } = new(StringComparer.Ordinal);

    /// <summary>As análises do Claude em andamento (<c>"execucoes_claude"</c>): ficam aqui do início ao fim, para o editor reaberto acompanhar ou importar.</summary>
    public List<ExecucaoClaudePendente> ExecucoesClaude { get; } = new();

    public Modulo Modulo { get; set; } = Modulo.Relatorios;
    public Dictionary<Modulo, string> AbaPorModulo { get; } = new();
    public string Tema { get; set; } = "sistema";
    public int IntervaloAutosaveS { get; set; } = 30;
    public bool MarcasRevisao { get; set; } = true;
    public List<string> Recentes { get; } = new();

    int recentesMax = MaxRecentes;
    /// <summary>Quantos recentes ficam (5, 10 ou 20); diminuir corta os mais antigos.</summary>
    public int RecentesMax
    {
        get => recentesMax;
        set
        {
            if (!OpcoesRecentes.Contains(value)) throw new ArgumentOutOfRangeException(nameof(value), value, "Quantidade de recentes fora das opções.");
            recentesMax = value;
            if (Recentes.Count > value) Recentes.RemoveRange(value, Recentes.Count - value);
        }
    }

    /// <summary>Ao abrir o editor: <c>ultimo</c> (o módulo usado por último, padrão) ou o nome de um módulo.</summary>
    public string AbrirEm { get; set; } = "ultimo";
    /// <summary>Reabrir a janela com a posição e o tamanho de quando fechou (desligado: 1600 x 1000, como sempre).</summary>
    public bool LembrarJanela { get; set; }
    public JanelaGuardada? Janela { get; set; }
    /// <summary>Rótulos da faixa: <c>automatico</c> (só ícones abaixo de 1000 px), <c>sempre</c> ou <c>icones</c>.</summary>
    public string FaixaRotulos { get; set; } = "automatico";
    /// <summary>Avisos de layout: <c>indicador</c> no rodapé ou <c>faixas</c> no topo (<see cref="global::Pmvni.App.AvisosLayout.Modos"/>).</summary>
    public string AvisosLayout { get; set; } = global::Pmvni.App.AvisosLayout.ModoPadrao;
    /// <summary>Zoom ao abrir um relatório: <c>largura</c> (cabe na janela, no máximo 100 %), <c>pagina</c> ou <c>100</c>.</summary>
    public string ZoomInicial { get; set; } = "largura";
    /// <summary>Abrir o painel lateral e as Propriedades como ficaram da última vez (desligado: os dois abertos, como sempre).</summary>
    public bool LembrarPaineis { get; set; }
    public bool PainelLateral { get; set; } = true;
    public bool PainelPropriedades { get; set; } = true;
    /// <summary>Por quantos dias a pasta de recuperação guarda o que não foi gravado (7, 30 ou 90).</summary>
    public int RecuperacaoDias { get; set; } = 30;
    public string ProcessosOrdem { get; set; } = "numero";
    public bool ProcessosPastas { get; set; }
    public string DespachosAgrupar { get; set; } = "data";
    public string DespachosOrdem { get; set; } = "recentes";
    /// <summary>Os arquivos do processo em <c>lista</c> (três colunas) ou em <c>miniaturas</c> (grade com a prévia, como o Explorador).</summary>
    public string ArquivosExibicao { get; set; } = "lista";
    /// <summary>Agrupar os arquivos do processo por <c>etapa</c> (a pasta), <c>tipo</c>, <c>data</c> ou <c>nenhum</c>.</summary>
    public string ArquivosAgrupar { get; set; } = "etapa";
    /// <summary>Ordenar os arquivos do processo por <c>nome</c>, <c>data</c> ou <c>tipo</c>.</summary>
    public string ArquivosOrdenar { get; set; } = "nome";
    public bool ArquivosDecrescente { get; set; }
    /// <summary>O soffice do LibreOffice escolhido em Ajustes; null = o editor acha sozinho.</summary>
    public string? Soffice { get; set; }
    public bool DespachoAbrirPdf { get; set; }
    /// <summary>Acompanhamento de processos: preencher sozinho o local e o autor do projeto vazios (relatórios, consulta QGIS, pranchas).</summary>
    public bool AcompanhamentoDetectar { get; set; } = true;

    /// <summary>De onde vem o certificado da assinatura digital dos despachos (Ajustes › Despachos e PDF › Assinatura digital, 2026-11-16):
    /// <c>nenhuma</c> (padrão: o PDF sai sem assinatura), <c>arquivo</c> (.pfx/.p12, em <see cref="AssinaturaArquivo"/>) ou <c>windows</c>
    /// (repositório do Windows, token ou cartão; em <see cref="AssinaturaImpressaoDigital"/>). Nenhuma senha fica aqui: a que o usuário
    /// pede para lembrar vai, cifrada, para o <c>CofreSenhaCertificado</c>.</summary>
    public string AssinaturaOrigem { get; set; } = "nenhuma";
    public static readonly IReadOnlyList<string> OpcoesAssinaturaOrigem = ["nenhuma", "arquivo", "windows"];
    /// <summary>O arquivo do certificado (.pfx ou .p12); só vale com <see cref="AssinaturaOrigem"/> = <c>arquivo</c>.</summary>
    public string? AssinaturaArquivo { get; set; }
    /// <summary>A impressão digital (SHA-1) do certificado do repositório do Windows; só vale com <c>windows</c>.</summary>
    public string? AssinaturaImpressaoDigital { get; set; }
    /// <summary>Guardar a senha do arquivo, cifrada (DPAPI), para não pedi-la a cada assinatura. Padrão: pedir sempre.</summary>
    public bool AssinaturaLembrarSenha { get; set; }

    /// <summary>Há um certificado escolhido: os despachos saem assinados.</summary>
    public bool AssinaturaConfigurada => AssinaturaOrigem switch
    {
        "arquivo" => !string.IsNullOrWhiteSpace(AssinaturaArquivo),
        "windows" => !string.IsNullOrWhiteSpace(AssinaturaImpressaoDigital),
        _ => false,
    };

    /// <summary>O que identifica o certificado escolhido (o caminho do arquivo ou a impressão digital): a senha lembrada vale só para ele.</summary>
    public string AssinaturaIdentidade => AssinaturaOrigem switch
    {
        "arquivo" => AssinaturaArquivo ?? "",
        "windows" => AssinaturaImpressaoDigital ?? "",
        _ => "",
    };

    /// <summary>Escolhe o certificado (ou nenhum, com <paramref name="origem"/> = <c>nenhuma</c>); os campos da outra origem são limpos.</summary>
    public void DefinirAssinatura(string origem, string? valor)
    {
        if (!OpcoesAssinaturaOrigem.Contains(origem)) throw new ArgumentOutOfRangeException(nameof(origem), origem, "Origem do certificado desconhecida.");
        var limpo = string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
        AssinaturaOrigem = limpo is null ? "nenhuma" : origem;
        AssinaturaArquivo = AssinaturaOrigem == "arquivo" ? limpo : null;
        AssinaturaImpressaoDigital = AssinaturaOrigem == "windows" ? CertificadoDigital.Limpar(limpo!) : null;
        if (AssinaturaOrigem != "arquivo") AssinaturaLembrarSenha = false;
    }
    /// <summary>Alvo da pesquisa ao abrir uma lei: <c>lei</c> (Nesta lei) ou <c>todas</c>.</summary>
    public string LegislacaoEscopo { get; set; } = "lei";
    string pedidoPadrao = "";
    /// <summary>O texto que já vem na caixa do Novo relatório (até <see cref="MaxPedidoPadrao"/> caracteres).</summary>
    public string PedidoPadrao
    {
        get => pedidoPadrao;
        set => pedidoPadrao = value is null ? "" : value.Length > MaxPedidoPadrao ? value[..MaxPedidoPadrao] : value;
    }
    public bool ConfirmarCancelar { get; set; } = true;
    /// <summary>Ajustes › Inteligência artificial › Revisão do relatório: o modelo do Codex nas perguntas curtas da revisão
    /// ("Ajustar o item com a IA", "Rever a tabela com a IA", volumetria). Padrão: Terra 6.1 (<see cref="ModeloIaAjuste.ModeloPadrao"/>).</summary>
    public string IaAjusteModelo { get; set; } = ModeloIaAjuste.ModeloPadrao;
    /// <summary>O esforço de raciocínio dessas perguntas. Padrão: baixo (<see cref="ModeloIaAjuste.EsforcoPadrao"/>).</summary>
    public string IaAjusteEsforco { get; set; } = ModeloIaAjuste.EsforcoPadrao;
    public bool ConfirmarFechar { get; set; } = true;
    /// <summary>Ajustes › Sobre e suporte › Atualizações: procurar e instalar versões novas sozinho (a cada 15 min, pelo GitHub).</summary>
    public bool AtualizarAutomaticamente { get; set; } = true;

    /// <summary>O módulo mostrado ao abrir o editor: o lembrado (<see cref="Modulo"/>) com "Último usado", senão o escolhido.</summary>
    public Modulo ModuloAoAbrir() =>
        AbrirEm != "ultimo" && Enum.TryParse<Modulo>(AbrirEm, out var m) && Enum.IsDefined(m) ? m : Modulo;

    /// <summary>"Limpar lista" de Arquivo › Recentes.</summary>
    public void LimparRecentes() => Recentes.Clear();

    /// <summary>"Restaurar os ajustes": todas as opções da tela voltam ao padrão. Ficam os recentes, os ajustes dos agentes (modo, programa,
    /// aviso aceito; a chave de API nem está aqui), as análises em andamento e o módulo/aba lembrados.</summary>
    public void RestaurarPadrao()
    {
        var p = new Ajustes();
        Tema = p.Tema;
        IntervaloAutosaveS = p.IntervaloAutosaveS;
        MarcasRevisao = p.MarcasRevisao;
        PastaProcessos = null;
        PastaKit = null;
        recentesMax = MaxRecentes;
        if (Recentes.Count > MaxRecentes) Recentes.RemoveRange(MaxRecentes, Recentes.Count - MaxRecentes);
        AbrirEm = p.AbrirEm;
        LembrarJanela = p.LembrarJanela;
        Janela = null;
        FaixaRotulos = p.FaixaRotulos;
        AvisosLayout = p.AvisosLayout;
        ZoomInicial = p.ZoomInicial;
        ListaSecoes = p.ListaSecoes;
        AcompanhamentoResumo = p.AcompanhamentoResumo;
        LembrarPaineis = p.LembrarPaineis;
        PainelLateral = p.PainelLateral;
        PainelPropriedades = p.PainelPropriedades;
        RecuperacaoDias = p.RecuperacaoDias;
        ProcessosOrdem = p.ProcessosOrdem;
        ProcessosPastas = p.ProcessosPastas;
        DespachosAgrupar = p.DespachosAgrupar;
        DespachosOrdem = p.DespachosOrdem;
        ArquivosExibicao = p.ArquivosExibicao;
        ArquivosAgrupar = p.ArquivosAgrupar;
        ArquivosOrdenar = p.ArquivosOrdenar;
        ArquivosDecrescente = p.ArquivosDecrescente;
        Soffice = null;
        DespachoAbrirPdf = p.DespachoAbrirPdf;
        AcompanhamentoDetectar = p.AcompanhamentoDetectar;
        DefinirAssinatura("nenhuma", null);
        LegislacaoEscopo = p.LegislacaoEscopo;
        PedidoPadrao = "";
        ConfirmarCancelar = p.ConfirmarCancelar;
        IaAjusteModelo = p.IaAjusteModelo;
        IaAjusteEsforco = p.IaAjusteEsforco;
        ConfirmarFechar = p.ConfirmarFechar;
        AtualizarAutomaticamente = p.AtualizarAutomaticamente;
    }
    /// <summary>A pasta de processos escolhida em Ajustes; null = a padrão (<see cref="PastaProcessosPadrao"/>).</summary>
    public string? PastaProcessos { get; set; }

    /// <summary>A pasta de processos do usuário neste computador (<c>%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\PROCESSOS</c>).</summary>
    public static string PastaProcessosPadrao { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "PMVNI", "SIDNEY JUNIOR", "PROCESSOS");

    /// <summary>A pasta que Processos e Despachos leem: a escolhida em Ajustes ou, sem escolha, a padrão.</summary>
    public string PastaProcessosEfetiva => string.IsNullOrWhiteSpace(PastaProcessos) ? PastaProcessosPadrao : PastaProcessos;

    /// <summary>A pasta do kit PMVNI escolhida em Ajustes (de onde os modelos de despacho são lidos, no lugar); null = a padrão.</summary>
    public string? PastaKit { get; set; }

    /// <summary>A pasta dos arquivos QGIS (módulo Imóveis, 2026-11-16), escolhida em Ajustes › Fontes de dados. Sem padrão: sem escolha,
    /// a consulta pede a configuração (nunca procura pastas sozinha).</summary>
    public string? PastaQgis { get; set; }

    /// <summary>A planilha de acompanhamento de processos (.xlsx no modelo padrão, 2026-11-17), escolhida em Ajustes › Fontes de dados ou
    /// pelo "Importar planilha" do módulo. Tudo o que muda no app é gravado nela.</summary>
    public string? PlanilhaAcompanhamento { get; set; }

    /// <summary>A aba da planilha de acompanhamento que o app mostra e grava (pelo nome; null = a primeira). Guardada na primeira leitura,
    /// para que reordenar as abas no Excel não troque a aba sem aviso.</summary>
    public string? PlanilhaAba { get; set; }

    /// <summary>O resumo por status do Acompanhamento aparece (o botão "Resumo"); lembrado entre um uso e outro.</summary>
    public bool AcompanhamentoResumo { get; set; } = true;

    /// <summary>A lista de seções do editor: "compacta" (uma linha por item, a situação como bolinha) ou "confortavel" (título em até
    /// duas linhas e a etiqueta da situação embaixo).</summary>
    public string ListaSecoes { get; set; } = "compacta";
    public static readonly IReadOnlyList<string> OpcoesListaSecoes = ["compacta", "confortavel"];

    /// <summary>As leis marcadas com a estrela na Legislação (ids dos anexos), na ordem em que foram marcadas.</summary>
    public List<string> LeisFavoritas { get; } = [];

    /// <summary>O kit PMVNI neste computador (<c>%USERPROFILE%\OneDrive\PMVNI\SIDNEY JUNIOR\KIT DE TREINAMENTO DE IA (PMVNI)</c>).</summary>
    public static string PastaKitPadrao { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive", "PMVNI", "SIDNEY JUNIOR", "KIT DE TREINAMENTO DE IA (PMVNI)");

    /// <summary>O kit que o Novo despacho lê: <c>PMVNI_KIT_RAIZ</c> (testes) se definida, senão a escolhida em Ajustes, senão a padrão.</summary>
    public string PastaKitEfetiva =>
        Environment.GetEnvironmentVariable("PMVNI_KIT_RAIZ") is { } env && !string.IsNullOrWhiteSpace(env) ? env
        : string.IsNullOrWhiteSpace(PastaKit) ? PastaKitPadrao : PastaKit;

    public static Ajustes Carregar(string raizDados)
    {
        var arquivo = Path.Combine(raizDados, NomeArquivo);
        if (!File.Exists(arquivo)) return new Ajustes();
        JsonObject j;
        try { j = JsonNode.Parse(File.ReadAllText(arquivo)) as JsonObject ?? throw new JsonException("não é um objeto"); }
        catch (JsonException)
        {
            File.Move(arquivo, NomesArquivo.Livre(raizDados, "ajustes.corrompido.json"));
            return new Ajustes();
        }

        var a = new Ajustes();
        if (Texto(j, "modulo") is { } m && Enum.TryParse<Modulo>(m, out var modulo) && Enum.IsDefined(modulo)) a.Modulo = modulo;
        if (j["aba_por_modulo"] is JsonObject abas)
            foreach (var (chave, valor) in abas)
                if (Enum.TryParse<Modulo>(chave, out var mm) && Enum.IsDefined(mm) && valor is JsonValue v && v.TryGetValue<string>(out var aba))
                    a.AbaPorModulo[mm] = aba;
        if (Texto(j, "tema") is { } t && Temas.Contains(t)) a.Tema = t;
        if (j["intervalo_autosave_s"] is JsonValue iv && iv.TryGetValue<int>(out var s) && Intervalos.Contains(s)) a.IntervaloAutosaveS = s;
        if (j["marcas_revisao"] is JsonValue mv && mv.TryGetValue<bool>(out var marcas)) a.MarcasRevisao = marcas;
        if (j["recentes_max"] is JsonValue rm && rm.TryGetValue<int>(out var max) && OpcoesRecentes.Contains(max)) a.recentesMax = max;
        if (j["recentes"] is JsonArray rec)
            foreach (var r in rec)
                if (r is JsonValue rv && rv.TryGetValue<string>(out var c) && !string.IsNullOrWhiteSpace(c) && a.Recentes.Count < a.recentesMax) a.Recentes.Add(c);
        LerOpcoes(j, a);
        a.PastaProcessos = Texto(j, "pasta_processos");
        a.PastaKit = Texto(j, "pasta_kit");
        a.PastaQgis = Texto(j, "pasta_qgis");
        a.PlanilhaAcompanhamento = Texto(j, "planilha_acompanhamento");
        a.PlanilhaAba = Texto(j, "planilha_aba");
        if (j["acompanhamento_resumo"] is JsonValue jr && jr.TryGetValue<bool>(out var resumo)) a.AcompanhamentoResumo = resumo;
        if (Texto(j, "lista_secoes") is { } ls && OpcoesListaSecoes.Contains(ls)) a.ListaSecoes = ls;
        if (j["leis_favoritas"] is JsonArray fav)
            foreach (var f in fav)
                if (f is JsonValue fv && fv.TryGetValue<string>(out var id) && id is { Length: > 0 and <= 128 } && !a.LeisFavoritas.Contains(id)) a.LeisFavoritas.Add(id);
        if (j["agentes"] is JsonObject agentes)
            foreach (var p in Enum.GetValues<Provedor>())
                if (agentes[Provedores.Sufixo(p)] is JsonObject o) LerAgente(o, a.Agentes[p]);
        if (j["plugins_vistos"] is JsonObject vistos)
            foreach (var (chave, valor) in vistos)
                if (valor is JsonValue vv && vv.TryGetValue<string>(out var versao) && versao is { Length: > 0 and <= 64 } && chave.Length <= 64) a.PluginsVistos[chave] = versao;
        if (j["execucoes_claude"] is JsonArray execucoes)
            foreach (var e in execucoes)
                if (e is JsonObject eo && Texto(eo, "processo") is { Length: > 0 } processo && Texto(eo, "registro") is { Length: > 0 } registro)
                    a.ExecucoesClaude.Add(new ExecucaoClaudePendente(processo, registro));
        return a;
    }

    /// <summary>Se o análise pro instalado do kit proíbe a API paga (<paramref name="apiClaudePermitida"/> false), um "chave" do Claude guardado antes volta para
    /// "conta" (a opção está desabilitada na tela e nenhuma análise sairia). true = mudou algo e é preciso salvar.</summary>
    public bool NormalizarModos(bool apiClaudePermitida)
    {
        var claude = Agentes[Provedor.Claude];
        if (apiClaudePermitida || claude.Modo != ModoAcesso.Chave) return false;
        claude.Modo = ModoAcesso.Conta;
        return true;
    }

    /// <summary>Põe o arquivo no topo dos recentes (sem repetir, ignorando maiúsculas; no máximo <see cref="MaxRecentes"/>).</summary>
    public void AdicionarRecente(string caminho)
    {
        if (string.IsNullOrWhiteSpace(caminho)) return;
        Recentes.RemoveAll(r => string.Equals(r, caminho, StringComparison.OrdinalIgnoreCase));
        Recentes.Insert(0, caminho);
        if (Recentes.Count > recentesMax) Recentes.RemoveRange(recentesMax, Recentes.Count - recentesMax);
    }

    /// <summary>Os recentes que ainda existem, do mais novo ao mais antigo: o que sumiu não aparece (nada fica desabilitado). A lista
    /// guardada não muda: um arquivo numa unidade que está fora do ar pode voltar.</summary>
    public IReadOnlyList<string> RecentesExistentes() => Recentes.Where(r => File.Exists(r)).ToList();

    /// <summary>Grava num temporário ao lado e troca: o arquivo nunca fica pela metade.</summary>
    public void Salvar(string raizDados)
    {
        Directory.CreateDirectory(raizDados);
        var abas = new JsonObject();
        foreach (var (m, aba) in AbaPorModulo.OrderBy(p => p.Key)) abas[m.ToString()] = aba;
        var j = new JsonObject
        {
            ["modulo"] = Modulo.ToString(),
            ["aba_por_modulo"] = abas,
            ["tema"] = Tema,
            ["intervalo_autosave_s"] = IntervaloAutosaveS,
            ["marcas_revisao"] = MarcasRevisao,
            ["recentes_max"] = recentesMax,
            ["recentes"] = new JsonArray(Recentes.Take(recentesMax).Select(r => (JsonNode)r).ToArray()),
            ["pasta_processos"] = PastaProcessos,
            ["pasta_kit"] = PastaKit,
            ["pasta_qgis"] = PastaQgis,
            ["planilha_acompanhamento"] = PlanilhaAcompanhamento,
            ["planilha_aba"] = PlanilhaAba,
            ["acompanhamento_resumo"] = AcompanhamentoResumo,
            ["lista_secoes"] = ListaSecoes,
            ["leis_favoritas"] = new JsonArray(LeisFavoritas.Select(f => (JsonNode)f).ToArray()),
            ["agentes"] = new JsonObject(Agentes.Select(par => KeyValuePair.Create(Provedores.Sufixo(par.Key), (JsonNode?)EscreverAgente(par.Value)))),
            ["plugins_vistos"] = new JsonObject(PluginsVistos.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, (JsonNode?)p.Value))),
            ["execucoes_claude"] = new JsonArray(ExecucoesClaude.Select(e => (JsonNode)new JsonObject { ["processo"] = e.Processo, ["registro"] = e.Registro }).ToArray()),
            ["abrir_em"] = AbrirEm,
            ["lembrar_janela"] = LembrarJanela,
            ["janela"] = Janela is { } jg ? new JsonObject { ["x"] = jg.X, ["y"] = jg.Y, ["l"] = jg.Largura, ["a"] = jg.Altura, ["max"] = jg.Maximizada } : null,
            ["faixa_rotulos"] = FaixaRotulos,
            ["avisos_layout"] = AvisosLayout,
            ["zoom_inicial"] = ZoomInicial,
            ["lembrar_paineis"] = LembrarPaineis,
            ["paineis"] = new JsonObject { ["lateral"] = PainelLateral, ["propriedades"] = PainelPropriedades },
            ["recuperacao_dias"] = RecuperacaoDias,
            ["processos_ordem"] = ProcessosOrdem,
            ["processos_pastas"] = ProcessosPastas,
            ["despachos_agrupar"] = DespachosAgrupar,
            ["despachos_ordem"] = DespachosOrdem,
            ["arquivos_exibicao"] = ArquivosExibicao,
            ["arquivos_agrupar"] = ArquivosAgrupar,
            ["arquivos_ordenar"] = ArquivosOrdenar,
            ["arquivos_decrescente"] = ArquivosDecrescente,
            ["soffice"] = Soffice,
            ["despacho_abrir_pdf"] = DespachoAbrirPdf,
            ["acompanhamento_detectar"] = AcompanhamentoDetectar,
            // Assinatura digital: só de onde vem o certificado. A senha nunca vem para este arquivo.
            ["assinatura_origem"] = AssinaturaOrigem,
            ["assinatura_arquivo"] = AssinaturaArquivo,
            ["assinatura_certificado_sha1"] = AssinaturaImpressaoDigital,
            ["assinatura_lembrar_senha"] = AssinaturaLembrarSenha,
            ["legislacao_escopo"] = LegislacaoEscopo,
            ["pedido_padrao"] = PedidoPadrao,
            ["confirmar_cancelar"] = ConfirmarCancelar,
            ["ia_ajuste_modelo"] = IaAjusteModelo,
            ["ia_ajuste_esforco"] = IaAjusteEsforco,
            ["confirmar_fechar"] = ConfirmarFechar,
            ["atualizar_automaticamente"] = AtualizarAutomaticamente,
        };
        var arquivo = Path.Combine(raizDados, NomeArquivo);
        var tmp = arquivo + ".tmp";
        File.WriteAllText(tmp, j.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.Move(tmp, arquivo, overwrite: true);
    }

    /// <summary>As opções da tela de Ajustes (§3.3): só valores das listas entram; o resto (chave ausente num arquivo antigo, tipo errado,
    /// valor desconhecido) fica no padrão.</summary>
    static void LerOpcoes(JsonObject j, Ajustes a)
    {
        string Escolha(string chave, IReadOnlyList<string> opcoes, string atual) => Texto(j, chave) is { } s && opcoes.Contains(s) ? s : atual;
        bool Logico(JsonObject o, string chave, bool atual) => o[chave] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : atual;
        a.AbrirEm = Escolha("abrir_em", OpcoesAbrirEm, a.AbrirEm);
        a.LembrarJanela = Logico(j, "lembrar_janela", a.LembrarJanela);
        if (j["janela"] is JsonObject jo && Inteiro(jo, "x") is { } x && Inteiro(jo, "y") is { } y && Inteiro(jo, "l") is { } l && Inteiro(jo, "a") is { } al)
        {
            var g = new JanelaGuardada(x, y, l, al, Logico(jo, "max", false));
            if (g.Valida) a.Janela = g;
        }
        a.FaixaRotulos = Escolha("faixa_rotulos", OpcoesFaixaRotulos, a.FaixaRotulos);
        a.AvisosLayout = Escolha("avisos_layout", global::Pmvni.App.AvisosLayout.Modos, a.AvisosLayout);
        a.ZoomInicial = Escolha("zoom_inicial", OpcoesZoomInicial, a.ZoomInicial);
        a.LembrarPaineis = Logico(j, "lembrar_paineis", a.LembrarPaineis);
        if (j["paineis"] is JsonObject po)
        {
            a.PainelLateral = Logico(po, "lateral", a.PainelLateral);
            a.PainelPropriedades = Logico(po, "propriedades", a.PainelPropriedades);
        }
        if (Inteiro(j, "recuperacao_dias") is { } dias && OpcoesRecuperacaoDias.Contains(dias)) a.RecuperacaoDias = dias;
        a.ProcessosOrdem = Escolha("processos_ordem", OpcoesProcessosOrdem, a.ProcessosOrdem);
        a.ProcessosPastas = Logico(j, "processos_pastas", a.ProcessosPastas);
        a.DespachosAgrupar = Escolha("despachos_agrupar", OpcoesDespachosAgrupar, a.DespachosAgrupar);
        a.DespachosOrdem = Escolha("despachos_ordem", OpcoesDespachosOrdem, a.DespachosOrdem);
        a.ArquivosExibicao = Escolha("arquivos_exibicao", OpcoesArquivosExibicao, a.ArquivosExibicao);
        a.ArquivosAgrupar = Escolha("arquivos_agrupar", OpcoesArquivosAgrupar, a.ArquivosAgrupar);
        a.ArquivosOrdenar = Escolha("arquivos_ordenar", OpcoesArquivosOrdenar, a.ArquivosOrdenar);
        a.ArquivosDecrescente = Logico(j, "arquivos_decrescente", a.ArquivosDecrescente);
        a.Soffice = Texto(j, "soffice") is { } so && !string.IsNullOrWhiteSpace(so) ? so : null;
        a.DespachoAbrirPdf = Logico(j, "despacho_abrir_pdf", a.DespachoAbrirPdf);
        a.AcompanhamentoDetectar = Logico(j, "acompanhamento_detectar", a.AcompanhamentoDetectar);
        var origem = Escolha("assinatura_origem", OpcoesAssinaturaOrigem, "nenhuma");
        a.DefinirAssinatura(origem, origem == "arquivo" ? Texto(j, "assinatura_arquivo") : origem == "windows" ? Texto(j, "assinatura_certificado_sha1") : null);
        a.AssinaturaLembrarSenha = a.AssinaturaOrigem == "arquivo" && Logico(j, "assinatura_lembrar_senha", false);
        a.LegislacaoEscopo = Escolha("legislacao_escopo", OpcoesLegislacaoEscopo, a.LegislacaoEscopo);
        a.PedidoPadrao = Texto(j, "pedido_padrao") ?? "";
        a.ConfirmarCancelar = Logico(j, "confirmar_cancelar", a.ConfirmarCancelar);
        a.IaAjusteModelo = Escolha("ia_ajuste_modelo", ModeloIaAjuste.Modelos.Select(m => m.Valor).ToList(), a.IaAjusteModelo);
        a.IaAjusteEsforco = Escolha("ia_ajuste_esforco", ModeloIaAjuste.Esforcos.Select(e => e.Valor).ToList(), a.IaAjusteEsforco);
        a.ConfirmarFechar = Logico(j, "confirmar_fechar", a.ConfirmarFechar);
        a.AtualizarAutomaticamente = Logico(j, "atualizar_automaticamente", a.AtualizarAutomaticamente);
    }

    static int? Inteiro(JsonObject j, string nome) => j[nome] is JsonValue v && v.TryGetValue<int>(out var i) ? i : null;

    // Só valores conhecidos entram; qualquer outra coisa fica no padrão. Nenhuma chave de API é lida nem escrita aqui.
    static void LerAgente(JsonObject o, AjustesAgente a)
    {
        if (Texto(o, "modo") is { } m)
            a.Modo = m == "chave" ? ModoAcesso.Chave : ModoAcesso.Conta;
        a.Executavel = Texto(o, "executavel") is { } e && !string.IsNullOrWhiteSpace(e) ? e : null;
        if (o["aviso_versao"] is JsonValue v && v.TryGetValue<int>(out var versao) && versao > 0) a.AvisoVersao = versao;
        if (Texto(o, "aviso_em") is { } em && DateTimeOffset.TryParse(em, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var quando)) a.AvisoEm = quando;
    }

    static JsonObject EscreverAgente(AjustesAgente a) => new()
    {
        ["modo"] = a.Modo == ModoAcesso.Chave ? "chave" : "conta",
        ["executavel"] = a.Executavel,
        ["aviso_versao"] = a.AvisoVersao,
        ["aviso_em"] = a.AvisoEm?.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
    };

    static string? Texto(JsonObject j, string nome) => j[nome] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
}
