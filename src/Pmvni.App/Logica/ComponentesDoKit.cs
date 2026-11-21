using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Pmvni.Agentes;

namespace Pmvni.App;

/// <summary>Tarefa 21: a situação de um componente do kit no cartão "Plugins e ferramentas" (Ajustes › Pastas e kit).</summary>
public enum SituacaoComponente { EmDia, AtualizacaoDisponivel, Ausente, ComErro, NaoVerificado }

/// <summary>De onde vem a versão instalada de um componente.</summary>
public enum OrigemComponente
{
    /// <summary>O plugin no cache do Codex (<c>plugins\cache\pmvni-kit\&lt;plugin&gt;\&lt;versão&gt;</c>).</summary>
    PluginNoCodex,
    /// <summary>O plugin no registro do Claude Code (<c>plugins\installed_plugins.json</c>).</summary>
    PluginNoClaude,
    /// <summary>O <c>mcp_pmvni.py</c> copiado para <c>%LOCALAPPDATA%\PMVNI\MotorPdf</c>, comparado com o que o Editor traz.</summary>
    ServidorPdf,
    /// <summary>Uma linha do <c>resumo-instalacao.txt</c> que o <c>configurar-plugins.ps1</c> grava.</summary>
    MotorPdf,
}

/// <summary>Um componente do kit no cartão. <paramref name="Parte"/>: o <c>-Componente</c> do <c>configurar-plugins.ps1</c> que o atualiza sozinho
/// (null = só pelo "Atualizar todos"). <paramref name="Chave"/>: o plugin (origens de plugin) ou o começo do nome da linha do resumo (motores).
/// <paramref name="VersaoDoEditor"/>: para os motores, a versão que o Editor instala (null = o resumo não grava versão).</summary>
public sealed record ComponenteDoKit(string Id, string Nome, string Ajuda, OrigemComponente Origem, string Chave, string? Parte, string? VersaoDoEditor = null);

/// <summary>Uma linha lida do cartão. <paramref name="SoPeloTodos"/>: o botão "Atualizar" desta linha roda o "Atualizar todos".</summary>
public sealed record LinhaComponente(ComponenteDoKit Componente, string? Instalada, string? Disponivel, SituacaoComponente Situacao, string Detalhe, bool SoPeloTodos)
{
    public string TextoSituacao => ComponentesDoKit.TextoSituacao(Situacao);
    public string TextoVersoes => ComponentesDoKit.TextoVersoes(this);
    public string TextoBotao => SoPeloTodos ? "Atualizar (com todos)" : "Atualizar";
}

/// <summary>Onde ler. <paramref name="PastaMotorPdf"/>: <c>%LOCALAPPDATA%\PMVNI\MotorPdf</c>; <paramref name="PastaPmvni"/>: <c>Documentos\PMVNI</c>
/// (o <c>estado.json</c> e o <c>instalacao.json</c> do instalador do kit); <paramref name="PastaKitPadrao"/>: o kit dos Ajustes, quando o
/// <c>pmvni-kit.json</c> não diz o <c>kit_root</c>. <paramref name="HashServidorDoEditor"/>: SHA-256 do <c>mcp_pmvni.py</c> que o Editor traz.</summary>
public sealed record OrigensDoKit(string CodexHome, string? PastaClaude, string PastaMotorPdf, string PastaPmvni, string PastaKitPadrao, string? HashServidorDoEditor);

/// <summary>O que o cartão mostra: as linhas, a fonte usada (null = não achada), o kit deste computador e a última execução do instalador do kit.</summary>
public sealed record LeituraDoKit(IReadOnlyList<LinhaComponente> Linhas, string? Fonte, bool FonteAcessivel, string KitLocal, string TextoTarefa)
{
    public string TextoFonte => Fonte is null ? "Fonte do kit não configurada neste computador (falta o pmvni-kit.json do instalador)."
        : FonteAcessivel ? "Versões disponíveis lidas de " + Fonte + "."
        : "Não foi possível verificar as versões disponíveis: a fonte do kit (" + Fonte + ") está fora do alcance agora (fora da rede da Prefeitura?).";
}

/// <summary>Tarefa 21: o cartão "Plugins e ferramentas". A lista dos componentes fica só aqui (<see cref="Lista"/>); a leitura das versões
/// (<see cref="LerAsync"/>) e o início das atualizações (<see cref="IniciarAtualizacao"/>) são testáveis sem WinUI: o lançador é injetado.
/// <para>Toda atualização roda fora do Editor, pelo Windows: um <c>.cmd</c> gravado ao lado dos scripts extraídos e aberto com o <c>explorer.exe</c>
/// (o mesmo caminho da primeira abertura). Um componente atualiza pelo <c>configurar-plugins.ps1 -Componente &lt;parte&gt;</c>; "Atualizar todos"
/// roda o <c>INSTALAR_KIT_PMVNI.cmd atualizar</c> do kit deste computador. Com análise em andamento no Editor, nada é iniciado.</para></summary>
public static class ComponentesDoKit
{
    public const string Script = "configurar-plugins.ps1";
    public const string InstaladorDoKit = "INSTALAR_KIT_PMVNI.cmd";
    public const string CmdTodos = "atualizar-kit.cmd";
    public static string CmdDe(string parte) => "atualizar-" + parte + ".cmd";

    /// <summary>A espera máxima pela fonte do kit (pasta de rede fora do alcance não trava o cartão).</summary>
    public static readonly TimeSpan EsperaDaFonte = TimeSpan.FromSeconds(8);

    /// <summary>Os componentes do cartão, na ordem da tela. A Tarefa 22 tira o despacho do kit: ele não entra nesta lista.</summary>
    public static IReadOnlyList<ComponenteDoKit> Lista { get; } =
    [
        new("analise-codex", "Análise no Codex", "O plugin analise, com as rotas ChatGPT, Claude e Gemini.", OrigemComponente.PluginNoCodex, PluginsInstalados.Analise, "analise"),
        new("analise-claude", "Análise no Claude Code", "O registro do plugin analise no Claude Code.", OrigemComponente.PluginNoClaude, PluginsInstalados.Analise, "analise"),
        new("pmvni-pdf", "Servidor de PDF (pmvni-pdf)", "As ferramentas de PDF do kit no Codex, no Claude Code e no Claude Desktop.", OrigemComponente.ServidorPdf, "mcp_pmvni.py", "pmvni-pdf"),
        new("pymupdf", "Motor de PDF PyMuPDF", "Lê o texto e as páginas dos PDFs.", OrigemComponente.MotorPdf, "PyMuPDF", "motores", "2.1"),
        new("docling", "Motor de PDF Docling", "Converte o PDF em texto estruturado.", OrigemComponente.MotorPdf, "Docling MCP", "motores", "3.5.0"),
        new("blueprint", "Motor de PDF Blueprint", "Reconhece pranchas e carimbos.", OrigemComponente.MotorPdf, "Blueprint Pipeline", "motores"),
        new("fyp", "Motor de PDF FYP", "Reconhece ambientes de plantas baixas.", OrigemComponente.MotorPdf, "FYP-Floorplan", "motores"),
    ];

    public static string TextoSituacao(SituacaoComponente s) => s switch
    {
        SituacaoComponente.EmDia => "Em dia",
        SituacaoComponente.AtualizacaoDisponivel => "Atualização disponível",
        SituacaoComponente.Ausente => "Ausente",
        SituacaoComponente.ComErro => "Com erro",
        _ => "Não foi possível verificar",
    };

    public static string TextoVersoes(LinhaComponente l) =>
        l.Componente.Origem == OrigemComponente.MotorPdf && l.Componente.VersaoDoEditor is null
            ? (l.Instalada is null ? "Não instalado" : "Instalado") + " · versão fixada pelo Editor"
            : "Instalada: " + (l.Instalada ?? "nenhuma") + " · disponível: " + (l.Disponivel ?? "não verificada");

    /// <summary>Instalada menor que a disponível → atualização disponível; igual (ou maior, no PC do kit mestre) → em dia; nenhuma instalada →
    /// ausente; disponível desconhecida (fonte fora do alcance) → não foi possível verificar. Versões que não são números comparam como texto.</summary>
    public static SituacaoComponente Comparar(string? instalada, string? disponivel, bool comErro = false)
    {
        if (comErro) return SituacaoComponente.ComErro;
        if (string.IsNullOrWhiteSpace(instalada)) return SituacaoComponente.Ausente;
        if (string.IsNullOrWhiteSpace(disponivel)) return SituacaoComponente.NaoVerificado;
        if (Version.TryParse(Numero(instalada), out var a) && Version.TryParse(Numero(disponivel), out var b))
            return a < b ? SituacaoComponente.AtualizacaoDisponivel : SituacaoComponente.EmDia;
        return string.Equals(instalada.Trim(), disponivel.Trim(), StringComparison.OrdinalIgnoreCase) ? SituacaoComponente.EmDia : SituacaoComponente.AtualizacaoDisponivel;
    }

    static string Numero(string v) => v.Trim().Split('-', '+')[0];

    /// <summary>A fonte do kit: no PC do kit mestre (<c>modo_kit</c> espelho ou local no <c>instalacao.json</c>), o próprio kit; nos outros, a
    /// <c>fonte</c> do <c>pmvni-kit.json</c> (ou do <c>instalacao.json</c>). Também devolve o kit deste computador (<c>kit_root</c>).</summary>
    public static (string? Fonte, string KitLocal) FonteDoKit(OrigensDoKit o)
    {
        var cfg = LerJson(Path.Combine(o.PastaPmvni, "instalacao.json"));
        var kit = LerJson(Path.Combine(o.CodexHome, "pmvni-kit.json"));
        var kitLocal = Texto(kit, "kit_root") ?? Texto(cfg, "kit_root") ?? o.PastaKitPadrao;
        var modo = Texto(cfg, "modo_kit");
        if (modo is "espelho" or "local") return (kitLocal, kitLocal);
        return (Texto(kit, "fonte") ?? Texto(cfg, "fonte"), kitLocal);
    }

    /// <summary>Lê tudo (só leitura; nada falha: peça ilegível é peça ausente). A fonte fora do alcance deixa as versões disponíveis dos plugins
    /// "não verificadas", sem erro.</summary>
    public static async Task<LeituraDoKit> LerAsync(OrigensDoKit o, IReadOnlyList<ComponenteDoKit>? lista = null)
    {
        ArgumentNullException.ThrowIfNull(o);
        var (fonte, kitLocal) = FonteDoKit(o);
        string? versaoNaFonte = null;
        var acessivel = false;
        if (fonte is not null)
        {
            try
            {
                (acessivel, versaoNaFonte) = await Task.Run(() =>
                {
                    var existe = Directory.Exists(fonte);
                    return (existe, existe ? VersaoDoPlugin(fonte, PluginsInstalados.Analise) : null);
                }).WaitAsync(EsperaDaFonte);
            }
            catch (Exception e) when (e is TimeoutException or IOException or UnauthorizedAccessException) { acessivel = false; versaoNaFonte = null; }
        }
        var versaoNoKitLocal = await Task.Run(() => VersaoDoPlugin(kitLocal, PluginsInstalados.Analise));
        var resumo = await Task.Run(() => LerTexto(Path.Combine(o.PastaMotorPdf, "resumo-instalacao.txt")));
        var linhasResumo = LinhasDoResumo(resumo);
        var falhas = (resumo ?? "").Split('\n').Select(l => l.Trim().TrimStart('﻿')).Where(l => l.StartsWith("FALHA", StringComparison.Ordinal))
            .Select(l => l["FALHA".Length..].Trim()).ToList();
        var hashInstalado = await Task.Run(() => HashDe(Path.Combine(o.PastaMotorPdf, "mcp_pmvni.py")));

        var linhas = new List<LinhaComponente>();
        foreach (var c in lista ?? Lista)
            linhas.Add(c.Origem switch
            {
                OrigemComponente.PluginNoCodex or OrigemComponente.PluginNoClaude => LinhaDePlugin(c, o, linhasResumo, falhas, versaoNaFonte, versaoNoKitLocal, fonte is not null && acessivel),
                OrigemComponente.ServidorPdf => LinhaDoServidor(c, hashInstalado, o.HashServidorDoEditor, falhas),
                _ => LinhaDeMotor(c, linhasResumo, falhas),
            });
        var tarefa = await Task.Run(() => TextoDaTarefa(LerTexto(Path.Combine(o.PastaPmvni, "estado.json"))));
        return new LeituraDoKit(linhas, fonte, fonte is not null && acessivel, kitLocal, tarefa);
    }

    static LinhaComponente LinhaDePlugin(ComponenteDoKit c, OrigensDoKit o, IReadOnlyDictionary<string, string> resumo, IReadOnlyList<string> falhas,
        string? naFonte, string? noKitLocal, bool fonteAcessivel)
    {
        var noClaude = c.Origem == OrigemComponente.PluginNoClaude;
        var instalada = (noClaude ? PluginsInstalados.NoClaude(o.PastaClaude, c.Chave) : PluginsInstalados.NoCodex(o.CodexHome, c.Chave))?.Versao;
        var programa = noClaude ? "Claude Code" : "Codex";
        var programaAusente = resumo.TryGetValue(programa, out var estadoPrograma) && estadoPrograma.StartsWith("AUSENTE", StringComparison.Ordinal);
        var chaveResumo = programa + " / " + c.Chave;
        var erro = instalada is null && !programaAusente && resumo.TryGetValue(chaveResumo, out var e) && e.StartsWith("PENDENTE", StringComparison.Ordinal);
        var disponivel = fonteAcessivel ? naFonte : null;
        var situacao = Comparar(instalada, disponivel, erro);
        // A versão nova ainda não está na cópia do kit deste computador: só o "Atualizar todos" a copia da fonte.
        var soPeloTodos = disponivel is not null && Comparar(noKitLocal, disponivel) != SituacaoComponente.EmDia;
        var detalhe = situacao switch
        {
            SituacaoComponente.ComErro => "A última instalação não terminou" + (falhas.Count > 0 ? ": " + string.Join("; ", falhas) : "") + ".",
            SituacaoComponente.Ausente when programaAusente => "O " + programa + " não está instalado neste computador.",
            SituacaoComponente.Ausente => "Não instalado no " + programa + ".",
            SituacaoComponente.NaoVerificado => "Não foi possível verificar a versão disponível: a fonte do kit está fora do alcance.",
            _ => c.Ajuda,
        };
        if (soPeloTodos && situacao != SituacaoComponente.EmDia)
            detalhe += " A versão " + disponivel + " ainda não está na cópia do kit deste computador: o botão roda o \"Atualizar todos\".";
        return new LinhaComponente(c, instalada, disponivel, situacao, detalhe, soPeloTodos || c.Parte is null);
    }

    static LinhaComponente LinhaDoServidor(ComponenteDoKit c, string? hashInstalado, string? hashDoEditor, IReadOnlyList<string> falhas)
    {
        var instalada = Curto(hashInstalado);
        var disponivel = Curto(hashDoEditor);
        var erro = instalada is not null && falhas.Any(f => f.StartsWith("MCP", StringComparison.Ordinal));
        var situacao = Comparar(instalada, disponivel, erro);
        var detalhe = situacao switch
        {
            SituacaoComponente.Ausente => "Ainda não copiado para este computador.",
            SituacaoComponente.ComErro => "O registro das ferramentas de PDF não terminou na última instalação.",
            SituacaoComponente.AtualizacaoDisponivel => "O Editor traz uma versão mais nova do servidor de PDF.",
            _ => c.Ajuda,
        };
        return new LinhaComponente(c, instalada, disponivel, situacao, detalhe, c.Parte is null);
    }

    static LinhaComponente LinhaDeMotor(ComponenteDoKit c, IReadOnlyDictionary<string, string> resumo, IReadOnlyList<string> falhas)
    {
        var (nome, estado) = resumo.FirstOrDefault(kv => kv.Key.StartsWith(c.Chave, StringComparison.Ordinal));
        var instalado = estado is not null && estado.StartsWith("INSTALADO", StringComparison.Ordinal);
        var erro = estado is not null && !instalado;
        string? instalada = null;
        string? disponivel = c.VersaoDoEditor;
        if (instalado)
        {
            var resto = nome![c.Chave.Length..].Trim();
            instalada = c.VersaoDoEditor is null ? "instalado" : (resto.Length > 0 ? resto : null);
            if (c.VersaoDoEditor is null) disponivel = "instalado";
        }
        var situacao = Comparar(instalada, disponivel, erro);
        var detalhe = situacao switch
        {
            SituacaoComponente.ComErro => "A última instalação não terminou" + (falhas.Count > 0 ? ": " + string.Join("; ", falhas) : "") + ".",
            SituacaoComponente.Ausente => "Ainda não instalado neste computador.",
            _ => c.Ajuda,
        } + (c.Parte == "motores" ? " Os quatro motores de PDF se atualizam juntos." : "");
        return new LinhaComponente(c, instalada, disponivel, situacao, detalhe, c.Parte is null);
    }

    /// <summary>"Última execução do instalador do kit: 21/11/2026 às 10:15 (atualizar) · concluída sem pendências", do <c>estado.json</c> que o
    /// <c>INSTALAR_KIT_PMVNI.cmd</c> grava (também quando a tarefa "PMVNI\Atualizar kit" o roda sozinho).</summary>
    public static string TextoDaTarefa(string? estadoJson)
    {
        const string Rodape = " A tarefa \"PMVNI\\Atualizar kit\" mantém o kit sempre atualizado: roda sozinha ao conectar na rede, a cada 15 minutos e ao entrar no Windows.";
        JsonObject? e = null;
        try { e = estadoJson is null ? null : JsonNode.Parse(estadoJson) as JsonObject; }
        catch (JsonException) { }
        if (e is null) return "O instalador do kit ainda não registrou nenhuma execução neste computador." + Rodape;
        var quando = Texto(e, "atualizado") is { } t && DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d)
            ? $"{d:dd/MM/yyyy} às {d:HH:mm}" : "data desconhecida";
        var modo = Texto(e, "modo");
        var resultado = Texto(e, "situacao") switch
        {
            "CONCLUIDO" => "concluída sem pendências",
            "CONCLUIDO_COM_PENDENCIAS" => "concluída com pendências",
            "FALHOU" => "falhou",
            "ADIADO" => "adiada: havia análise em andamento" + (Texto(e, "adiado_processo") is { } pr ? " (" + pr + ")" : ""),
            "SEM_FONTE" => "a fonte do kit estava fora do alcance; nada mudou",
            "EM_ANDAMENTO" => "em andamento",
            "CANCELADO" => "cancelada",
            { } outra => outra.ToLowerInvariant(),
            null => "resultado desconhecido",
        };
        return "Última execução do instalador do kit: " + quando + (modo is null ? "" : " (" + modo + ")") + " · " + resultado + "." + Rodape;
    }

    /// <summary>Inicia a atualização fora do Editor. <paramref name="parte"/>: o <c>-Componente</c> do <c>configurar-plugins.ps1</c>; null = "Atualizar todos"
    /// (<c>INSTALAR_KIT_PMVNI.cmd atualizar</c> do <paramref name="kitLocal"/>). Grava o <c>.cmd</c> em <paramref name="pastaScripts"/> e chama
    /// <paramref name="iniciar"/>(explorer.exe, .cmd). Com análise em andamento, avisa e não grava nem inicia nada. Devolve o texto para o cartão.</summary>
    public static string IniciarAtualizacao(string? parte, string nome, IReadOnlyList<string> analisesEmAndamento, string pastaScripts, string kitLocal, Action<string, string> iniciar)
    {
        ArgumentNullException.ThrowIfNull(analisesEmAndamento);
        ArgumentNullException.ThrowIfNull(iniciar);
        if (analisesEmAndamento.Count > 0) return AtualizacaoKit.AvisoAnaliseEmAndamento(analisesEmAndamento);
        string cmd, texto;
        if (parte is null)
        {
            var instalador = Path.Combine(kitLocal, InstaladorDoKit);
            if (!File.Exists(instalador)) return "Não achei o instalador do kit em " + instalador + ". Nada foi iniciado.";
            cmd = Path.Combine(pastaScripts, CmdTodos);
            texto = "@echo off\r\nrem Editor de Relatórios PMVNI: atualizar todo o kit PMVNI (o instalador oficial do kit, modo atualizar).\r\n"
                + "call \"" + instalador + "\" atualizar\r\n";
        }
        else
        {
            var script = Path.Combine(pastaScripts, Script);
            if (!File.Exists(script)) return "O script de instalação dos plugins não foi extraído (" + script + "). Nada foi iniciado.";
            cmd = Path.Combine(pastaScripts, CmdDe(parte));
            texto = "@echo off\r\nrem Editor de Relatórios PMVNI: atualizar só um componente do kit (" + parte + ").\r\n"
                + "\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \""
                + script + "\" -Componente " + parte + "\r\n";
        }
        Directory.CreateDirectory(pastaScripts);
        File.WriteAllText(cmd, texto, PrimeiraAbertura.CodificacaoDoConsole());
        iniciar(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), cmd);
        return parte is null
            ? "Atualização de todo o kit iniciada fora do Editor (INSTALAR_KIT_PMVNI.cmd atualizar). Quando terminar, clique em Verificar atualizações."
            : "Atualização de " + nome + " iniciada fora do Editor. Quando terminar, clique em Verificar atualizações.";
    }

    /// <summary>O botão "Atualizar" de uma linha (ou "Atualizar todos", com <paramref name="linha"/> null). Uma linha que só se atualiza com o kit
    /// inteiro (<see cref="LinhaComponente.SoPeloTodos"/>) roda o "Atualizar todos", e o texto diz isso.</summary>
    public static string Atualizar(LinhaComponente? linha, IReadOnlyList<string> analisesEmAndamento, string pastaScripts, string kitLocal, Action<string, string> iniciar)
    {
        var parte = linha is null || linha.SoPeloTodos ? null : linha.Componente.Parte;
        var texto = IniciarAtualizacao(parte, linha?.Componente.Nome ?? "todo o kit", analisesEmAndamento, pastaScripts, kitLocal, iniciar);
        return linha is not null && parte is null && analisesEmAndamento.Count == 0
            ? linha.Componente.Nome + " só se atualiza junto com o kit inteiro (a versão nova ainda não foi copiada para este computador). " + texto
            : texto;
    }

    /// <summary>As linhas do resumo (<c>"&lt;componente&gt;    &lt;ESTADO&gt;"</c>), sem as de FALHA.</summary>
    static Dictionary<string, string> LinhasDoResumo(string? resumo)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var bruta in (resumo ?? "").Split('\n'))
        {
            var linha = bruta.Trim().TrimStart('﻿');
            if (linha.Length == 0 || linha.StartsWith("FALHA", StringComparison.Ordinal)) continue;
            var colunas = linha.Split("    ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (colunas.Length >= 2) d[colunas[0]] = colunas[^1];
        }
        return d;
    }

    static string? VersaoDoPlugin(string kit, string plugin) =>
        Texto(LerJson(Path.Combine(kit, "01 - PLUGINS", "plugins", plugin, ".codex-plugin", "plugin.json")), "version");

    /// <summary>SHA-256 de um arquivo (hexadecimal); null se não existe ou não dá para ler.</summary>
    public static string? HashDe(string arquivo)
    {
        try
        {
            if (!File.Exists(arquivo)) return null;
            using var s = File.OpenRead(arquivo);
            return Convert.ToHexString(SHA256.HashData(s));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static string? Curto(string? hash) => string.IsNullOrEmpty(hash) ? null : hash[..Math.Min(8, hash.Length)].ToLowerInvariant();

    static string? LerTexto(string arquivo)
    {
        try { return File.Exists(arquivo) ? File.ReadAllText(arquivo) : null; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    static JsonObject? LerJson(string arquivo)
    {
        try { return LerTexto(arquivo) is { } t ? JsonNode.Parse(t.TrimStart('﻿')) as JsonObject : null; }
        catch (JsonException) { return null; }
    }

    static string? Texto(JsonObject? o, string chave) =>
        o?[chave] is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
}
