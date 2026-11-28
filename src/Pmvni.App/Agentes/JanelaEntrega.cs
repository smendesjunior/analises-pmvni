using System.Diagnostics;
using System.IO.Pipes;

namespace Pmvni.App;

internal static class JanelaEntrega
{
    internal static async Task<bool> AbrirAsync(string pacote, string hash, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        await using var servidor = new NamedPipeServerStream("pmvni-entrega-" + id, PipeDirection.In, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(ct);
        prazo.CancelAfter(TimeSpan.FromMinutes(3));
        var inicio = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        inicio.ArgumentList.Add(pacote);
        inicio.ArgumentList.Add("--confirmar-entrega");
        inicio.ArgumentList.Add(id);
        using var janela = Process.Start(inicio) ?? throw new IOException("Falha ao abrir a janela do relatório.");
        var conexao = servidor.WaitForConnectionAsync(prazo.Token);
        var saiu = janela.WaitForExitAsync(prazo.Token);
        if (await Task.WhenAny(conexao, saiu) != conexao) return false;
        await conexao;
        using var leitor = new StreamReader(servidor);
        return await leitor.ReadLineAsync(prazo.Token) == hash;
    }

    internal static async Task ConfirmarAsync(string id, string hash)
    {
        await using var cliente = new NamedPipeClientStream(".", "pmvni-entrega-" + id, PipeDirection.Out, PipeOptions.Asynchronous);
        await cliente.ConnectAsync(5000);
        await using var escritor = new StreamWriter(cliente);
        await escritor.WriteLineAsync(hash);
    }
}
