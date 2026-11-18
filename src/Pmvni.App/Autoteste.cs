using System.Security.Principal;
using System.Text.Json;
using Pmvni.Legislacao;

namespace Pmvni.App;

/// <summary>Argumento --autoteste &lt;arquivo.json&gt;: depois da primeira navegação, grava o resultado e fecha o app.</summary>
internal static class Autoteste
{
    static string? arquivo;

    public static bool Ativo => arquivo is not null;

    public static void Configurar(string[] args)
    {
        var i = Array.IndexOf(args, "--autoteste");
        if (i >= 0 && i + 1 < args.Length) arquivo = Path.GetFullPath(args[i + 1]);
    }

    /// <summary>Grava o resultado da primeira navegação. O bloco "legislacao" confere o PDF.js embutido (versão, arquivos, bytes e
    /// sha256 de cada arquivo contra o VERSAO.json); "ok" exige o sha conferido.</summary>
    public static void GravarSucesso(string versaoWebView2, Dictionary<string, object?>? janelas = null, Dictionary<string, object?>? brilho = null,
        Dictionary<string, object?>? agentes = null)
    {
        var shaOk = RecursosLegislacao.ConferirPdfJs(out var divergentes);
        var legislacao = new Dictionary<string, object?>
        {
            ["pdfjs"] = RecursosLegislacao.VersaoPdfJs,
            ["arquivos"] = RecursosLegislacao.ShaPdfJs.Count,
            ["bytes"] = RecursosLegislacao.BytesPdfJs,
            ["sha_ok"] = shaOk,
        };
        if (!shaOk) legislacao["divergentes"] = divergentes.Take(20).ToArray();
        // 1.0.0: as janelas dos sites (window.open, opener, document.write, window.close) também precisam passar.
        var janelasOk = janelas is null || janelas.TryGetValue("ok", out var j) && j is true;
        var brilhoOk = brilho is null || brilho.TryGetValue("ok", out var b) && b is true;
        // 1.1.0: os três provedores (ChatGPT, Claude e Gemini) na janela publicada.
        var agentesOk = agentes is null || agentes.TryGetValue("ok", out var ag) && ag is true;
        var dados = new Dictionary<string, object?>
        {
            ["ok"] = shaOk && janelasOk && brilhoOk && agentesOk,
            ["webview2"] = versaoWebView2,
            ["baseDir"] = AppContext.BaseDirectory,
            ["elevado"] = Elevado(),
            ["legislacao"] = legislacao,
        };
        if (janelas is not null) dados["janelas_dos_sites"] = janelas;
        if (brilho is not null) dados["brilho_trabalhando"] = brilho;
        if (agentes is not null) dados["agentes"] = agentes;
        if (!shaOk) dados["erro"] = "O PDF.js embutido não confere com o VERSAO.json: " + string.Join(", ", divergentes.Take(5));
        else if (!janelasOk) dados["erro"] = "As janelas dos sites não passaram no autoteste: " + (janelas!.TryGetValue("erro", out var e) ? e : "veja janelas_dos_sites");
        else if (!brilhoOk) dados["erro"] = "O brilho \"trabalhando\" não passou no autoteste: " + (brilho!.TryGetValue("erro", out var e2) ? e2 : "veja brilho_trabalhando");
        else if (!agentesOk) dados["erro"] = "Os três provedores de análise não passaram no autoteste: " + (agentes!.TryGetValue("erro", out var e3) ? e3 : "veja agentes");
        Gravar(dados);
    }

    public static void GravarErro(string erro) => Gravar(new Dictionary<string, object?>
    {
        ["erro"] = erro,
        ["baseDir"] = AppContext.BaseDirectory,
        ["elevado"] = Elevado(),
    });

    static bool Elevado()
    {
        using var identidade = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identidade).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // Grava num temporário ao lado e renomeia: quem espera pelo arquivo nunca lê JSON pela metade.
    static void Gravar(Dictionary<string, object?> dados)
    {
        if (arquivo is null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(arquivo)!);
        var temporario = arquivo + ".tmp";
        File.WriteAllText(temporario, JsonSerializer.Serialize(dados, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporario, arquivo, overwrite: true);
        arquivo = null; // grava uma vez só
    }
}
