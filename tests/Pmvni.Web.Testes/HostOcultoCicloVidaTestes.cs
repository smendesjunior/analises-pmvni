using Microsoft.Web.WebView2.Core;
using Pmvni.Composicao.Web;
using Xunit;

namespace Pmvni.Web.Testes;

public class HostOcultoCicloVidaTestes
{
    [Fact]
    public async Task criar_controller_de_composicao_com_as_opcoes_da_janela()
    {
        await using var host = await HostOculto.CriarAsync(320, 240);
        var resultado = await host.ExecutarAsync(async web =>
        {
            var ambiente = web.Environment;
            var janela = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)host.Janela);
            var controller = await ambiente.CreateCoreWebView2CompositionControllerAsync(janela, FabricaWebView2.OpcoesController(ambiente));
            try { return await controller.CoreWebView2.ExecuteScriptAsync("1 + 1"); }
            finally { controller.Close(); }
        });
        Assert.Equal("2", resultado);
    }

    [Fact]
    public async Task criar_coletar_usar_e_encerrar_repetidamente()
    {
        for (var i = 0; i < 8; i++)
        {
            var host = await HostOculto.CriarAsync(320, 240);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Assert.Equal("2", await host.ExecutarAsync(async c => await c.ExecuteScriptAsync("1 + 1")));
            await host.DisposeAsync();
            Assert.True(host.Inutilizavel);
            Assert.Null(host.UltimoErroInterno);
            await host.DisposeAsync();
        }
    }

    [Fact]
    public async Task encerrar_falha_operacao_pendente_sem_travar()
    {
        var host = await HostOculto.CriarAsync(320, 240);
        var iniciou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var liberar = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendente = host.ExecutarAsync(async _ => { iniciou.SetResult(); return await liberar.Task; });
        await iniciou.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await host.DisposeAsync();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => pendente);
        liberar.TrySetResult(1);
        Assert.Null(host.UltimoErroInterno);
    }
}
