using System.ComponentModel;
using System.Diagnostics;
using Pmvni.Revisao;

namespace Pmvni.App;

/// <summary>Abre arquivos e pastas fora do editor, pelo shell do Windows (desenho §9): só o que <see cref="AbrirExterno.Permitido"/>
/// aceita (documentos e imagens conhecidos, arquivos comuns). No modo <c>--roteiro</c> ninguém olha a janela e nenhum programa pode
/// abrir: <see cref="Simular"/> só registra o caminho em <see cref="Registradas"/>.</summary>
internal static class AberturaExterna
{
    /// <summary>Só registra, não abre nada. Já nasce ligado com <c>--roteiro</c> na linha de comando.</summary>
    public static bool Simular { get; set; } = Argumentos.Atual.Roteiro is not null;

    /// <summary>O que teria sido aberto em modo <see cref="Simular"/>: o caminho do arquivo ou <c>explorador: &lt;caminho&gt;</c>.</summary>
    public static List<string> Registradas { get; } = [];

    /// <summary>Abre o arquivo no programa padrão. false com o motivo (em português) se a lista de permissão recusou ou se o Windows não
    /// achou programa para ele; nesse caso nada foi aberto nem registrado.</summary>
    public static bool Abrir(string caminho, out string? motivo)
    {
        if (!AbrirExterno.Permitido(caminho, out motivo)) return false;
        if (Simular) { Registradas.Add(caminho); return true; }
        try
        {
            Process.Start(new ProcessStartInfo(caminho) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException)
        {
            motivo = "O Windows não conseguiu abrir o arquivo: " + e.Message;
            return false;
        }
    }

    /// <summary>Abre o Explorador com o arquivo ou a pasta selecionados (<c>explorer.exe /select,"…"</c>). Serve também para o que
    /// <see cref="Abrir"/> recusa (scripts, atalhos): o Explorador só mostra, nada executa.</summary>
    /// <summary>Abre a pasta no Explorador (Ajustes: pasta de processos, do kit, de recuperação, de dados). No roteiro, só registra.</summary>
    public static void AbrirPasta(string pasta)
    {
        if (Simular) { Registradas.Add("pasta: " + pasta); return; }
        try { Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "\"" + pasta + "\"", UseShellExecute = false })?.Dispose(); }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException) { Debug.WriteLine("[explorador] " + e.Message); }
    }

    public static void MostrarNoExplorador(string caminho)
    {
        if (Simular) { Registradas.Add("explorador: " + caminho); return; }
        try
        {
            // O Explorador lê "/select,<caminho>" como um argumento só; as aspas protegem os espaços do caminho.
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + caminho + "\"", UseShellExecute = false })?.Dispose();
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine("[explorador] " + e.Message);
        }
    }
}
