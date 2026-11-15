using System.Diagnostics;
using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao;
using Pmvni.Composicao.Web;

namespace Pmvni.Verificador;

/// <summary>Prova que nenhuma operação do HostOculto fica pendurada: tempo-limite, descarte,
/// queda do processo do WebView2 e erro interno na fila STA terminam a operação com exceção clara.</summary>
internal static class AutotesteFalhas
{
    /// <summary>Documento mínimo (com &lt;head&gt;) para carregar pelo caminho protegido (sessão + sandbox).</summary>
    internal static ResultadoRender DocumentoSimples(string corpo) =>
        new($"<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body>{corpo}</body></html>", Array.Empty<string>(), Array.Empty<RefElemento>());

    static readonly ConfigFolhas Cfg = new("data:,", "", "");

    public static async Task<int> ExecutarAsync()
    {
        var falhas = 0;
        var pasta = PastaDados.WebView2();

        using (var host = await HostOculto.IniciarAsync(pasta))
        {
            falhas += await Caso("navegacao presa -> TimeoutException em ~3 s", typeof(TimeoutException), TimeSpan.FromSeconds(8),
                async () =>
                {
                    // Requisições ao documento ficam presas para sempre (deferral nunca concluído): a navegação nunca termina.
                    var adiados = new List<Windows.Foundation.Deferral>();
                    await host.ExecutarAsync(wv =>
                    {
                        wv.WebResourceRequested += (_, e) => { if (e.Request.Uri.Contains("documento.html")) adiados.Add(e.GetDeferral()); };
                        return Task.FromResult(true);
                    });
                    await host.Documento.CarregarAsync(DocumentoSimples("<p>ok</p>"), Cfg, TimeSpan.FromSeconds(3));
                    return adiados.Count;
                });

            falhas += await Caso("acao que nunca termina -> TimeoutException em ~2 s", typeof(TimeoutException), TimeSpan.FromSeconds(6),
                () => host.ExecutarAsync<bool>(async _ => { await Task.Delay(Timeout.Infinite); return true; }, TimeSpan.FromSeconds(2)));

            falhas += await Caso("erro postado na fila STA -> InvalidOperationException (sem derrubar o processo)", typeof(InvalidOperationException), TimeSpan.FromSeconds(5),
                () => host.ExecutarAsync<bool>(async _ =>
                {
                    SynchronizationContext.Current!.Post(_ => throw new ApplicationException("erro de teste na fila"), null);
                    await Task.Delay(Timeout.Infinite);
                    return true;
                }, TimeSpan.FromSeconds(30)));
            Console.WriteLine($"   UltimoErroInterno: {host.UltimoErroInterno?.GetType().Name}: {host.UltimoErroInterno?.Message}");

            falhas += await Caso("host continua utilizavel depois do erro interno", null, TimeSpan.FromSeconds(30),
                async () => { await host.Documento.CarregarAsync(DocumentoSimples("<p>ok</p>"), Cfg); return true; });
        }

        using (var host = await HostOculto.IniciarAsync(pasta))
        {
            falhas += await Caso("Page.crash (processo de renderizacao cai) -> InvalidOperationException", typeof(InvalidOperationException), TimeSpan.FromSeconds(20),
                async () =>
                {
                    await host.Documento.CarregarAsync(DocumentoSimples("<p>ok</p>"), Cfg);
                    return await host.ExecutarAsync<bool>(async wv =>
                    {
                        _ = wv.CallDevToolsProtocolMethodAsync("Page.crash", "{}");
                        await Task.Delay(Timeout.Infinite);
                        return true;
                    }, TimeSpan.FromSeconds(60));
                });
        }

        var outro = await HostOculto.IniciarAsync(pasta);
        var pendente = outro.ExecutarAsync<bool>(async _ => { await Task.Delay(Timeout.Infinite); return true; }, TimeSpan.FromSeconds(60));
        await Task.Delay(300);
        outro.Dispose();
        falhas += await Caso("Dispose com operacao pendente -> ObjectDisposedException", typeof(ObjectDisposedException), TimeSpan.FromSeconds(2), () => pendente);
        falhas += await Caso("ExecutarAsync depois do Dispose -> ObjectDisposedException", typeof(ObjectDisposedException), TimeSpan.FromSeconds(2),
            () => outro.ExecutarAsync(_ => Task.FromResult(true)));

        Console.WriteLine(falhas == 0 ? "RESULTADO: todos os casos passaram" : $"RESULTADO: {falhas} caso(s) falharam");
        return falhas == 0 ? 0 : 1;
    }

    static async Task<int> Caso<T>(string nome, Type? excecaoEsperada, TimeSpan prazo, Func<Task<T>> operacao)
    {
        var relogio = Stopwatch.StartNew();
        Exception? obtida = null;
        try
        {
            var tarefa = operacao();
            if (await Task.WhenAny(tarefa, Task.Delay(prazo)) != tarefa) throw new TimeoutException("o próprio caso não terminou no prazo (operação pendurada)");
            await tarefa;
        }
        catch (Exception e) { obtida = e; }
        var ok = excecaoEsperada is null ? obtida is null : excecaoEsperada.IsInstanceOfType(obtida) && relogio.Elapsed < prazo;
        Console.WriteLine($"[{(ok ? "OK" : "FALHA")}] {nome}: {relogio.Elapsed.TotalSeconds:0.00} s, {(obtida is null ? "sem exceção" : obtida.GetType().Name + ": " + obtida.Message)}");
        return ok ? 0 : 1;
    }
}
