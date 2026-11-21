using System.Diagnostics;
using System.Text;
using Pmvni.Revisao;

namespace Pmvni.Agentes;

/// <summary>Um processo filho do agente (desenho §12.12): <c>ArgumentList</c> (nunca linha de comando montada à mão), sem janela, sem shell, entrada e
/// saídas em UTF-8 sem BOM. A entrada é escrita e fechada logo depois de iniciar; as saídas são lidas por linhas em tarefas próprias, para um agente
/// que escreve muito nunca ficar parado esperando o editor.</summary>
public sealed class ExecucaoProcesso : IDisposable
{
    static readonly UTF8Encoding Utf8 = new(false);

    /// <summary>Máximo de caracteres de uma linha de saída (1 MB); o resto da linha é descartado.</summary>
    public const int LimiteLinha = 1024 * 1024;

    public const string MarcaLinhaTruncada = " …[linha truncada]";

    /// <summary>Quanto o <see cref="Fim"/> espera as saídas fecharem depois que o processo sai (3 s); só testes mexem.</summary>
    internal static TimeSpan EsperaDasSaidas { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>Trava de teste: programa fora desta pasta ⇒ <see cref="InvalidOperationException"/> antes de iniciar qualquer coisa. Atalho para
    /// <see cref="TravaDeProgramas.Permitidos"/> com uma pasta só (o roteiro usa a lista inteira: agente falso + LibreOffice).</summary>
    public static string? SomenteExecutaveisEm
    {
        get => TravaDeProgramas.Permitidos is { Count: > 0 } l ? l[0] : null;
        set => TravaDeProgramas.Permitidos = value is null ? null : [value];
    }

    /// <summary>Chamado com o caminho completo de cada programa que partiu de fato (<see cref="TravaDeProgramas.AoIniciar"/>, comum ao LibreOffice do
    /// despacho); o que a trava recusa e um início que falhou nunca chegam aqui.</summary>
    public static Action<string>? AoIniciar
    {
        get => TravaDeProgramas.AoIniciar;
        set => TravaDeProgramas.AoIniciar = value;
    }

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


    /// <param name="tarefa">O job em que o processo entra logo depois de partir (o Codex: encerrado junto com o editor); null = nenhum.</param>
    public static ExecucaoProcesso Iniciar(string exe, IReadOnlyList<string> args, string pastaTrabalho, string? entrada,
        Action<IDictionary<string, string?>> ambiente, Action<string> aoLerSaida, Action<string> aoLerErro, TarefaDoEditor? tarefa = null)
    {
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

        var p = TravaDeProgramas.Iniciar(psi) ?? throw new InvalidOperationException("Não foi possível iniciar o programa: " + exe);
        // Os filhos que ele criar daqui em diante também ficam no job (um neto criado no primeiro instante, antes desta linha, escapa: o Kill da árvore cobre o cancelamento).
        var naTarefa = tarefa?.Atribuir(p) ?? false;

        // As saídas já estão sendo lidas antes de escrever a entrada: um agente que escreve muito antes de ler tudo não trava o editor.
        var sinal = new Sinal();
        var lerSaida = Task.Run(() => Bombear(p.StandardOutput, aoLerSaida, sinal));
        var lerErro = Task.Run(() => Bombear(p.StandardError, aoLerErro, sinal));
        try
        {
            if (entrada is not null) p.StandardInput.Write(entrada);
            p.StandardInput.Close();
        }
        catch (IOException) { /* o programa já fechou a entrada (não precisava dela); o código de saída diz o resto */ }

        var fim = Task.Run(async () =>
        {
            await p.WaitForExitAsync().ConfigureAwait(false);
            // Espera limitada pelo fim das saídas: um neto que herdou os canais e continua vivo não pode prender o Fim. Passado o prazo, as tarefas de leitura
            // ficam para trás (terminam sozinhas quando o neto sair; o SafeHandle cuida do fechamento) e o que ainda chegar é descartado.
            await Task.WhenAny(Task.WhenAll(lerSaida, lerErro), Task.Delay(EsperaDasSaidas)).ConfigureAwait(false);
            sinal.Encerrado = true;
            try { return p.ExitCode; }
            catch (Exception e) when (e is InvalidOperationException or ObjectDisposedException) { return -1; }
        });
        return new ExecucaoProcesso(p, fim) { NaTarefa = naTarefa };
    }

    /// <summary>true se o processo entrou no job pedido em <see cref="Iniciar"/>.</summary>
    public bool NaTarefa { get; private init; }

    sealed class Sinal { public volatile bool Encerrado; public int FalhaAvisada; }

    /// <summary>Lê por linhas (terminadas em "\n"; o "\r" do fim sai). Cada linha tem no máximo <see cref="LimiteLinha"/> caracteres: o excedente é descartado
    /// e a linha termina com <see cref="MarcaLinhaTruncada"/>. Assim um agente que escreve sem nunca quebrar a linha não consome a memória do editor.</summary>
    static void Bombear(StreamReader leitor, Action<string> aoLer, Sinal sinal)
    {
        try
        {
            var buffer = new char[8192];
            var linha = new StringBuilder();
            var truncada = false;
            int n;
            while ((n = leitor.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (sinal.Encerrado) return;
                var resto = buffer.AsSpan(0, n);
                while (resto.Length > 0)
                {
                    var i = resto.IndexOf('\n');
                    var parte = i < 0 ? resto : resto[..i];
                    var cabe = LimiteLinha - linha.Length;
                    if (parte.Length <= cabe) linha.Append(parte);
                    else { linha.Append(parte[..cabe]); truncada = true; }
                    if (i < 0) break;
                    Emitir();
                    resto = resto[(i + 1)..];
                }
            }
            if (linha.Length > 0 || truncada) Emitir();

            void Emitir()
            {
                if (sinal.Encerrado) { linha.Clear(); truncada = false; return; }
                var texto = linha.ToString().TrimEnd('\r');
                if (truncada) texto += MarcaLinhaTruncada;
                linha.Clear();
                truncada = false;
                // Uma falha de quem recebe a linha (o registro, a tela) nunca para a leitura: o agente que escreve num canal que ninguém lê trava para sempre.
                try { aoLer(texto); }
                catch (Exception e) when (e is not OutOfMemoryException)
                {
                    if (Interlocked.Exchange(ref sinal.FalhaAvisada, 1) == 0)
                    {
                        try { Pmvni.Revisao.MensagensDeErro.Registrar?.Invoke(e); }
                        catch (Exception x) when (x is IOException or UnauthorizedAccessException) { }
                    }
                }
            }
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
        // Comandos curtos: no máximo 256 KB de saída guardados (o resto é lido e descartado).
        void Acrescentar(string l) { lock (saida) if (saida.Length < 256 * 1024) saida.AppendLine(l); }
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
