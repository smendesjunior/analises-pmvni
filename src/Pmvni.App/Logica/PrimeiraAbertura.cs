using System.Text;
using Pmvni.Agentes;

namespace Pmvni.App;

/// <summary>Tarefa 16: na abertura do Editor, se a instalação do motor PDF e dos plugins está incompleta, inicia o
/// <c>primeira-abertura.ps1</c> fora do Editor, pelo Windows: grava um <c>.cmd</c> ao lado do script e o abre com o <c>explorer.exe</c>
/// (regra do usuário para tudo o que grava em AppData/HKCU ou instala). A instalação completa não inicia nada.
/// <para>"Incompleta" é a mesma conferência que o botão "Reparar instalação" mostra: o <c>resumo-instalacao.txt</c> (uma linha
/// por componente, <c>"&lt;componente&gt;    &lt;ESTADO&gt;"</c>, e <c>"FALHA    &lt;mensagem&gt;"</c>) não existe, está vazio, ou tem um
/// componente PENDENTE, uma FALHA, ou um plugin de análise instalado abaixo da versão mínima do Editor (<see cref="PoliticaKit.VersoesMinimas"/>).
/// AUSENTE (Codex ou Claude Code não instalados) não conta: o instalador não instala esses programas e abriria a cada início sem resolver nada.</para>
/// <para>Uma falha que persiste não reinstala tudo a cada abertura: depois de uma tentativa, a próxima só sai passadas
/// <see cref="IntervaloEntreTentativas"/>.</para>
/// Sem tipos WinUI: os testes rodam sem abrir nada (o lançador é injetado).</summary>
public static class PrimeiraAbertura
{
    public const string Script = "primeira-abertura.ps1";
    public const string Cmd = "primeira-abertura.cmd";

    internal static string RegistroMaisRecente(string pasta)
    {
        var resumo = Path.Combine(pasta, "resumo-instalacao.txt");
        var log = Path.Combine(pasta, "primeira-abertura.log");
        return File.Exists(log) && (!File.Exists(resumo) || File.GetLastWriteTimeUtc(log) > File.GetLastWriteTimeUtc(resumo))
            && new FileInfo(log).Length > 0 ? log : resumo;
    }
    /// <summary>O resumo diz que falta instalar algo (ou não há resumo: nunca instalou).</summary>
    public static bool Incompleta(string? resumo)
    {
        if (string.IsNullOrWhiteSpace(resumo)) return true;
        foreach (var bruta in resumo.Split('\n'))
        {
            var linha = bruta.Trim().TrimStart('﻿');
            if (linha.Length == 0) continue;
            if (linha.StartsWith("FALHA", StringComparison.Ordinal)) return true;
            // O estado é a última coluna (o nome do componente pode ter espaços: "Docling MCP 3.5.0    INSTALADO").
            var colunas = linha.Split("    ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (colunas.Length >= 2 && colunas[^1].StartsWith("PENDENTE", StringComparison.Ordinal)) return true;
            if (colunas.Length >= 2 && PluginAbaixoDoMinimo(colunas[0], colunas[^1])) return true;
        }
        return false;
    }

    /// <summary>Tempo mínimo entre duas tentativas da primeira abertura (falha persistente não reinstala a cada início).</summary>
    public static readonly TimeSpan IntervaloEntreTentativas = TimeSpan.FromHours(6);

    /// <summary>"Codex / analise" + "INSTALADO 3.0.1": plugin de análise com versão abaixo da mínima do Editor.</summary>
    static bool PluginAbaixoDoMinimo(string componente, string estado)
    {
        var barra = componente.LastIndexOf('/');
        if (barra < 0 || !estado.StartsWith("INSTALADO ", StringComparison.Ordinal)) return false;
        var plugin = componente[(barra + 1)..].Trim();
        foreach (var (nome, minima) in PoliticaKit.VersoesMinimas.Values)
            if (string.Equals(nome, plugin, StringComparison.OrdinalIgnoreCase))
                return !(Version.TryParse(estado["INSTALADO ".Length..].Trim().Split('-', '+')[0], out var v) && v >= minima);
        return false;
    }

    /// <summary>O <c>.cmd</c> que roda o script com o PowerShell do Windows (o <c>explorer.exe</c> o abre fora do Editor).</summary>
    public static string TextoCmd(string script) =>
        "@echo off\r\n"
        + "rem Editor de Relatórios PMVNI: instalação da primeira abertura (motor PDF e plugins do usuário).\r\n"
        + "\"%SystemRoot%\\System32\\WindowsPowerShell\\v1.0\\powershell.exe\" -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \""
        + script + "\"\r\n";

    /// <summary>Confere o <paramref name="resumo"/> e, se incompleto, grava <c>primeira-abertura.cmd</c> em <paramref name="pastaScripts"/>
    /// (onde o <c>primeira-abertura.ps1</c> já foi extraído) e chama <paramref name="iniciar"/>(programa, argumento) com o
    /// <c>explorer.exe</c> e o caminho do <c>.cmd</c>. <paramref name="ultimaTentativaUtc"/>: quando a anterior foi iniciada (null = nunca);
    /// menos de <see cref="IntervaloEntreTentativas"/> antes de <paramref name="agoraUtc"/> não inicia de novo. true = iniciou (quem chama
    /// guarda <paramref name="agoraUtc"/> como a última tentativa). Revisão final I2 (erro 15): com análise em andamento ou pendente no Editor
    /// (<paramref name="analisesEmAndamento"/> não vazio; as do Claude e do Gemini seguem com o Editor fechado), adia: não reinstala o plugin
    /// analise por baixo do controlador e não conta como tentativa (a próxima abertura confere de novo).</summary>
    public static bool Conferir(string? resumo, string pastaScripts, Action<string, string> iniciar, DateTime agoraUtc, DateTime? ultimaTentativaUtc,
        IReadOnlyList<string>? analisesEmAndamento = null)
    {
        ArgumentNullException.ThrowIfNull(pastaScripts);
        ArgumentNullException.ThrowIfNull(iniciar);
        if (!Incompleta(resumo)) return false;
        if (analisesEmAndamento is { Count: > 0 }) return false;
        if (ultimaTentativaUtc is { } ultima && agoraUtc - ultima < IntervaloEntreTentativas && agoraUtc >= ultima) return false;
        var script = Path.Combine(pastaScripts, Script);
        if (!File.Exists(script)) throw new FileNotFoundException("O script da primeira abertura não foi extraído.", script);
        var cmd = Path.Combine(pastaScripts, Cmd);
        // Codificação do console do Windows: o caminho do usuário pode ter acentos ("Relatórios"); o cmd.exe lê o .cmd na página OEM.
        File.WriteAllText(ArquivoGerado.Caminho(cmd), TextoCmd(script), CodificacaoDoConsole());
        iniciar(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), cmd);
        return true;
    }

    /// <summary>A página OEM do console (o cmd.exe lê o .cmd nela); também usada pelo cartão Plugins e ferramentas (<see cref="ComponentesDoKit"/>).</summary>
    internal static Encoding CodificacaoDoConsole()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return new UTF8Encoding(false); }
    }
}

/// <summary>Tarefa 16 (erro 15 do diagnóstico 19486): o botão "Atualizar todos" não inicia a atualização com uma análise em andamento
/// no Editor (o kit trocado no meio obriga a revalidar e refazer etapas). Avisa e não inicia nada; sem análise, chama <c>iniciar</c>.</summary>
public static class AtualizacaoKit
{
    /// <summary>Os registros de retomada do ChatGPT só representam análises ativas enquanto a trava de execução estiver viva.</summary>
    internal static IEnumerable<ExecucaoClaudePendente> ChatGptEmAndamento(IEnumerable<ExecucaoClaudePendente> pendentes) =>
        pendentes.Where(e => TravaExecucao.EmAndamento(e.Processo));

    public static string AvisoAnaliseEmAndamento(IReadOnlyList<string> processos) =>
        (processos.Count == 1 ? "Há uma análise em andamento (" + processos[0] + ")" : "Há análises em andamento (" + string.Join(", ", processos) + ")")
        + ". O kit não foi atualizado: trocá-lo no meio da análise obriga a refazer etapas. Atualize quando a análise terminar.";

    /// <summary>Revisão final I2: o aviso do botão "Reparar instalação" com análise em andamento (o mesmo bloqueio do "Atualizar kit").</summary>
    public static string AvisoPluginsComAnalise(IReadOnlyList<string> processos) =>
        (processos.Count == 1 ? "Há uma análise em andamento (" + processos[0] + ")" : "Há análises em andamento (" + string.Join(", ", processos) + ")")
        + ". Os plugins não foram instalados nem atualizados: trocar o plugin analise no meio da análise obriga a refazer etapas. "
        + "Instale quando a análise terminar.";

    /// <summary>Revisão final I2: o botão "Reparar instalação" roda o <c>configurar-plugins.ps1</c> inteiro (inclusive o plugin analise);
    /// com análise em andamento no Editor, avisa e não chama <paramref name="instalar"/>.</summary>
    public static async Task<string> InstalarPluginsAsync(IReadOnlyList<string> analisesEmAndamento, Func<Task<string>> instalar)
    {
        ArgumentNullException.ThrowIfNull(analisesEmAndamento);
        ArgumentNullException.ThrowIfNull(instalar);
        if (analisesEmAndamento.Count > 0) return AvisoPluginsComAnalise(analisesEmAndamento);
        return await instalar();
    }
}
