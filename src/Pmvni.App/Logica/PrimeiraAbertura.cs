using System.Text;

namespace Pmvni.App;

/// <summary>Tarefa 16: na abertura do Editor, se a instalação do motor PDF e dos plugins está incompleta, inicia o
/// <c>primeira-abertura.ps1</c> fora do Editor, pelo Windows: grava um <c>.cmd</c> ao lado do script e o abre com o <c>explorer.exe</c>
/// (regra do usuário para tudo o que grava em AppData/HKCU ou instala). A instalação completa não inicia nada.
/// <para>"Incompleta" é a mesma conferência que o botão "Instalar ou atualizar plugins" mostra: o <c>resumo-instalacao.txt</c> (uma linha
/// por componente, <c>"&lt;componente&gt;    &lt;ESTADO&gt;"</c>, e <c>"FALHA    &lt;mensagem&gt;"</c>) não existe, está vazio, ou tem um
/// componente PENDENTE ou uma FALHA. AUSENTE (Codex ou Claude Code não instalados) não conta: o instalador não instala esses programas
/// e abriria a cada início sem resolver nada.</para>
/// Sem tipos WinUI: os testes rodam sem abrir nada (o lançador é injetado).</summary>
public static class PrimeiraAbertura
{
    public const string Script = "primeira-abertura.ps1";
    public const string Cmd = "primeira-abertura.cmd";

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
        }
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
    /// <c>explorer.exe</c> e o caminho do <c>.cmd</c>. true = iniciou.</summary>
    public static bool Conferir(string? resumo, string pastaScripts, Action<string, string> iniciar)
    {
        ArgumentNullException.ThrowIfNull(pastaScripts);
        ArgumentNullException.ThrowIfNull(iniciar);
        if (!Incompleta(resumo)) return false;
        var script = Path.Combine(pastaScripts, Script);
        if (!File.Exists(script)) throw new FileNotFoundException("O script da primeira abertura não foi extraído.", script);
        var cmd = Path.Combine(pastaScripts, Cmd);
        // Codificação do console do Windows: o caminho do usuário pode ter acentos ("Relatórios"); o cmd.exe lê o .cmd na página OEM.
        File.WriteAllText(cmd, TextoCmd(script), CodificacaoDoConsole());
        iniciar(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), cmd);
        return true;
    }

    static Encoding CodificacaoDoConsole()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException) { return new UTF8Encoding(false); }
    }
}

/// <summary>Tarefa 16 (erro 15 do diagnóstico 19486): o botão "Atualizar kit PMVNI" não inicia a atualização com uma análise em andamento
/// no Editor (o kit trocado no meio obriga a revalidar e refazer etapas). Avisa e não inicia nada; sem análise, chama <c>iniciar</c>.</summary>
public static class AtualizacaoKit
{
    public static string AvisoAnaliseEmAndamento(IReadOnlyList<string> processos) =>
        (processos.Count == 1 ? "Há uma análise em andamento (" + processos[0] + ")" : "Há análises em andamento (" + string.Join(", ", processos) + ")")
        + ". O kit não foi atualizado: trocá-lo no meio da análise obriga a refazer etapas. Atualize quando a análise terminar.";

    /// <summary><paramref name="analisesEmAndamento"/>: os processos com análise em andamento no Editor (vazio = nenhuma). Devolve o texto
    /// para o resumo dos Ajustes.</summary>
    public static async Task<string> SolicitarAsync(IReadOnlyList<string> analisesEmAndamento, Func<Task<string>> iniciar)
    {
        ArgumentNullException.ThrowIfNull(analisesEmAndamento);
        ArgumentNullException.ThrowIfNull(iniciar);
        if (analisesEmAndamento.Count > 0) return AvisoAnaliseEmAndamento(analisesEmAndamento);
        return await iniciar();
    }
}
