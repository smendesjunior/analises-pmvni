using System.Diagnostics;
using System.Text;

namespace Pmvni.Agentes;

/// <summary>Um processo filho do agente (desenho §12.12): <c>ArgumentList</c> (nunca linha de comando montada à mão), sem janela, sem shell, entrada e
/// saídas em UTF-8 sem BOM. A entrada é escrita e fechada logo depois de iniciar; as saídas são lidas por linhas em tarefas próprias, para um agente
/// que escreve muito nunca ficar parado esperando o editor.</summary>
public sealed class ExecucaoProcesso : IDisposable
{
    static readonly UTF8Encoding Utf8 = new(false);
    static readonly TimeSpan EsperaDasSaidas = TimeSpan.FromSeconds(3);

    /// <summary>Trava de teste e de roteiro: programa fora desta pasta ⇒ <see cref="InvalidOperationException"/> antes de iniciar qualquer coisa.</summary>
    public static string? SomenteExecutaveisEm { get; set; }

    readonly Process processo;
    readonly Task<int> fim;
    int cancelado;
    int descartado;

    ExecucaoProcesso(Process processo, Task<int> fim)
    {
        this.processo = processo;
        this.fim = fim;
        Pid = processo.Id;
    }

    public int Pid { get; }

    /// <summary>Termina com o código de saída, depois que o processo saiu e as saídas foram lidas até o fim (ou depois de 3 s sem fechar, se um neto
    /// ficou segurando o canal).</summary>
    public Task<int> Fim => fim;

    static void ConferirPrograma(string exe)
    {
        var pasta = SomenteExecutaveisEm;
        if (pasta is null) return;
        string completo, permitida;
        try
        {
            completo = Path.GetFullPath(exe);
            permitida = Path.GetFullPath(pasta).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new InvalidOperationException("Programa fora da pasta permitida: " + exe);
        }
        if (!completo.StartsWith(permitida, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Programa fora da pasta permitida: " + exe);
    }

    public static ExecucaoProcesso Iniciar(string exe, IReadOnlyList<string> args, string pastaTrabalho, string? entrada,
        Action<IDictionary<string, string?>> ambiente, Action<string> aoLerSaida, Action<string> aoLerErro)
    {
        ConferirPrograma(exe);
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = pastaTrabalho,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        ambiente(psi.Environment);

        var p = new Process { StartInfo = psi };
        if (!p.Start())
        {
            p.Dispose();
            throw new InvalidOperationException("Não foi possível iniciar o programa: " + exe);
        }

        // As saídas já estão sendo lidas antes de escrever a entrada: um agente que escreve muito antes de ler tudo não trava o editor.
        var lerSaida = Task.Run(() => Bombear(p.StandardOutput, aoLerSaida));
        var lerErro = Task.Run(() => Bombear(p.StandardError, aoLerErro));
        try
        {
            if (entrada is not null) p.StandardInput.Write(entrada);
            p.StandardInput.Close();
        }
        catch (IOException) { /* o programa já fechou a entrada (não precisava dela); o código de saída diz o resto */ }

        var fim = Task.Run(async () =>
        {
            await p.WaitForExitAsync().ConfigureAwait(false);
            await Task.WhenAny(Task.WhenAll(lerSaida, lerErro), Task.Delay(EsperaDasSaidas)).ConfigureAwait(false);
            try { return p.ExitCode; }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { return -1; }
        });
        return new ExecucaoProcesso(p, fim);
    }

    static void Bombear(StreamReader leitor, Action<string> aoLer)
    {
        try
        {
            string? linha;
            while ((linha = leitor.ReadLine()) is not null) aoLer(linha);
        }
        catch (IOException) { /* canal fechado à força (processo morto) */ }
        catch (ObjectDisposedException) { }
    }

    /// <summary>Encerra o processo e todos os filhos dele (<c>Kill(entireProcessTree: true)</c>).</summary>
    public void Cancelar()
    {
        if (Interlocked.Exchange(ref cancelado, 1) == 1) return;
        try { if (!processo.HasExited) processo.Kill(entireProcessTree: true); }
        catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { /* já saiu */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref descartado, 1) == 1) return;
        try { if (!processo.HasExited) Cancelar(); }
        catch (InvalidOperationException) { }
        processo.Dispose();
    }

    /// <summary>Roda um comando curto (diagnóstico) e devolve o código e a saída (padrão e de erro juntas). Passou do limite ⇒ mata a árvore e devolve
    /// <c>(-1, "não respondeu em N s")</c>. Cancelar mata a árvore e lança <see cref="OperationCanceledException"/>.</summary>
    public static async Task<(int Codigo, string Saida)> RodarAsync(string exe, IReadOnlyList<string> args, string pastaTrabalho,
        Action<IDictionary<string, string?>> ambiente, TimeSpan limite, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var saida = new StringBuilder();
        void Acrescentar(string l) { lock (saida) saida.AppendLine(l); }
        using var execucao = Iniciar(exe, args, pastaTrabalho, null, ambiente, Acrescentar, Acrescentar);
        using var cancelamento = ct.Register(execucao.Cancelar);
        var primeira = await Task.WhenAny(execucao.Fim, Task.Delay(limite, CancellationToken.None)).ConfigureAwait(false);
        if (ct.IsCancellationRequested)
        {
            execucao.Cancelar();
            throw new OperationCanceledException(ct);
        }
        if (primeira != execucao.Fim)
        {
            execucao.Cancelar();
            await Task.WhenAny(execucao.Fim, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
            return (-1, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"não respondeu em {(int)Math.Round(limite.TotalSeconds)} s"));
        }
        var codigo = await execucao.Fim.ConfigureAwait(false);
        lock (saida) return (codigo, saida.ToString().TrimEnd('\r', '\n'));
    }
}
