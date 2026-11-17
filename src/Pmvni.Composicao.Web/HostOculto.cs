using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Pmvni.Composicao.Web;

/// <summary>
/// WebView2 sem interface: uma thread STA própria, com uma janela Win32 oculta (CreateWindowEx) que
/// hospeda um CoreWebView2Controller. Todo acesso ao CoreWebView2 passa por <see cref="ExecutarAsync{T}"/>,
/// que roda a ação na thread STA (as continuações dos awaits voltam para ela).
/// Nenhuma operação fica pendurada: cada uma tem tempo-limite, e todas as pendentes falham quando o
/// host é descartado, quando um processo do WebView2 cai ou quando a thread STA registra um erro interno.
/// </summary>
public sealed class HostOculto : IDisposable, IAsyncDisposable
{
    const int LarguraPadraoPx = 1024, AlturaPadraoPx = 1400;
    public static readonly TimeSpan TempoLimitePadrao = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan TempoLimiteNavegacaoPadrao = TimeSpan.FromSeconds(30);

    readonly Thread thread;
    readonly int larguraPx, alturaPx;
    DocumentoWeb? documento;
    readonly TaskCompletionSource prontoJanela = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly ConcurrentDictionary<object, Action<Exception>> pendentes = new();
    ContextoSta? contexto;
    IntPtr hwnd;
    WndProc? wndProc; // mantém o delegate vivo enquanto a janela existir
    CoreWebView2Controller? controller;
    volatile bool descartado;
    volatile Exception? falha;

    /// <summary>O host não serve mais: foi descartado ou um processo do WebView2 caiu (quem o usa deve criar outro).</summary>
    public bool Inutilizavel => descartado || falha is not null;

    /// <summary>Janela oculta que hospeda o controller (testes: o <c>ExtratorPdfJs</c> cria o dele sobre ela).</summary>
    internal IntPtr Janela => hwnd;

    /// <summary>Último erro lançado por um trabalho da fila STA fora de uma operação (nunca cruza o WndProc).</summary>
    public Exception? UltimoErroInterno { get; private set; }

    HostOculto(int larguraPx, int alturaPx)
    {
        this.larguraPx = larguraPx;
        this.alturaPx = alturaPx;
        thread = new Thread(LoopSta) { IsBackground = true, Name = "HostOculto-STA" };
        thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Documento do host: carrega o relatório na sandbox e conversa com o paginador. Seguro para chamar de qualquer thread.</summary>
    public DocumentoWeb Documento => documento ?? throw new ObjectDisposedException(nameof(HostOculto));

    /// <summary>Cria um host com viewport de <paramref name="larguraPx"/> x <paramref name="alturaPx"/>, com os dados do WebView2 em
    /// <see cref="PastaDados.WebView2"/>.</summary>
    public static Task<HostOculto> CriarAsync(int larguraPx, int alturaPx) =>
        IniciarAsync(PastaDados.WebView2(), null, larguraPx, alturaPx);

    /// <summary>Chama um método do Chrome DevTools Protocol e devolve o JSON da resposta.</summary>
    public Task<string> CdpAsync(string metodo, string json) =>
        ExecutarAsync(async core => await core.CallDevToolsProtocolMethodAsync(metodo, json));

    /// <summary>Cria a thread STA, a janela oculta, o ambiente e o controller do WebView2.</summary>
    public static async Task<HostOculto> IniciarAsync(string pastaDadosUsuario, TimeSpan? tempoLimite = null,
        int larguraPx = LarguraPadraoPx, int alturaPx = AlturaPadraoPx)
    {
        var host = new HostOculto(larguraPx, alturaPx);
        host.thread.Start();
        try
        {
            await host.prontoJanela.Task.ConfigureAwait(false);
            await host.ExecutarNaStaAsync(async () =>
            {
                // Mesma fábrica do controle XAML do app: mesmas opções de ambiente e controller InPrivate (nada do relatório no perfil).
                var ambiente = await FabricaWebView2.CriarAmbienteAsync(pastaDadosUsuario);
                var referencia = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)host.hwnd);
                var novo = await ambiente.CreateCoreWebView2ControllerAsync(referencia, FabricaWebView2.OpcoesController(ambiente));
                if (host.descartado) { novo.Close(); throw new ObjectDisposedException(nameof(HostOculto)); }
                host.controller = novo;
                // Escala 1 sempre, em qualquer processo: num app com DPI por monitor (o editor, a 150 %) o controller herdaria
                // a escala do monitor, e a composição sairia diferente da do Verificador/testes (processos sem DPI).
                novo.ShouldDetectMonitorScaleChanges = false;
                novo.RasterizationScale = 1.0;
                novo.Bounds = new Rect(0, 0, host.larguraPx, host.alturaPx);
                novo.IsVisible = true;
                novo.CoreWebView2.ProcessFailed += host.ProcessoFalhou;
                host.documento = new DocumentoWeb(novo.CoreWebView2, host.NaThreadSta);
                return true;
            }, tempoLimite ?? TempoLimitePadrao).ConfigureAwait(false);
        }
        catch
        {
            host.Dispose();
            throw;
        }
        return host;
    }

    /// <summary>Executa <paramref name="acao"/> na thread STA com o CoreWebView2 deste host. Falha com
    /// <see cref="TimeoutException"/> após <paramref name="tempoLimite"/> (padrão <see cref="TempoLimitePadrao"/>),
    /// com <see cref="ObjectDisposedException"/> se o host for descartado e com <see cref="InvalidOperationException"/>
    /// se um processo do WebView2 cair.</summary>
    public Task<T> ExecutarAsync<T>(Func<CoreWebView2, Task<T>> acao, TimeSpan? tempoLimite = null)
    {
        ObjectDisposedException.ThrowIf(descartado, this);
        if (falha is { } f) return Task.FromException<T>(new InvalidOperationException(f.Message, f));
        return ExecutarNaStaAsync(() => acao(controller!.CoreWebView2), tempoLimite ?? TempoLimitePadrao);
    }

    Task NaThreadSta(Func<Task> acao) => ExecutarNaStaAsync(async () => { await acao(); return true; }, TempoLimitePadrao);

    Task<T> ExecutarNaStaAsync<T>(Func<Task<T>> acao, TimeSpan tempoLimite)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        pendentes[tcs] = e => tcs.TrySetException(e);
        var cts = new CancellationTokenSource(tempoLimite);
        var registro = cts.Token.Register(() => tcs.TrySetException(
            new TimeoutException($"Operação do HostOculto excedeu o tempo-limite de {tempoLimite.TotalSeconds:0.#} s.")));
        tcs.Task.ContinueWith(concluida =>
        {
            registro.Dispose();
            cts.Dispose();
            pendentes.TryRemove(tcs, out _);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        // Descartado entre a verificação e o registro: falha já, sem depender da fila.
        if (descartado) { tcs.TrySetException(new ObjectDisposedException(nameof(HostOculto))); return tcs.Task; }

        contexto!.Post(async _ =>
        {
            if (tcs.Task.IsCompleted) return;
            try { tcs.TrySetResult(await acao()); }
            catch (Exception e) { tcs.TrySetException(e); }
        }, null);
        return tcs.Task;
    }

    /// <summary>Navega para uma URL (o host virtual da sessão; com a sandbox, só ele é permitido) e espera o NavigationCompleted <b>desta</b> navegação (pelo NavigationId).
    /// Lança se a navegação falhar ou não terminar em <paramref name="tempoLimite"/>. Chamar na thread STA
    /// (dentro de <see cref="ExecutarAsync{T}"/>).</summary>
    internal static Task NavegarParaUrlAsync(CoreWebView2 webView, string url, TimeSpan? tempoLimite = null) =>
        NavegarAsync(webView, () => webView.Navigate(url), tempoLimite);

    static async Task NavegarAsync(CoreWebView2 webView, Action navegar, TimeSpan? tempoLimite)
    {
        var limite = tempoLimite ?? TempoLimiteNavegacaoPadrao;
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong? id = null;
        void Iniciada(CoreWebView2 s, CoreWebView2NavigationStartingEventArgs e) => id ??= e.NavigationId;
        void Concluida(CoreWebView2 s, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (id != e.NavigationId) return; // conclusão atrasada de uma navegação anterior
            if (e.IsSuccess) tcs.TrySetResult();
            else tcs.TrySetException(new InvalidOperationException("Navegação falhou: " + e.WebErrorStatus));
        }
        webView.NavigationStarting += Iniciada;
        webView.NavigationCompleted += Concluida;
        using var cts = new CancellationTokenSource(limite);
        using var registro = cts.Token.Register(() => tcs.TrySetException(
            new TimeoutException($"Navegação não terminou em {limite.TotalSeconds:0.#} s.")));
        try
        {
            navegar();
            await tcs.Task;
        }
        finally
        {
            webView.NavigationStarting -= Iniciada;
            webView.NavigationCompleted -= Concluida;
        }
    }

    void ProcessoFalhou(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs e)
    {
        // Só as quedas que inutilizam a página; GPU/utilitários o próprio WebView2 recria.
        if (e.ProcessFailedKind is not (CoreWebView2ProcessFailedKind.BrowserProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessExited
            or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)) return;
        var erro = new InvalidOperationException($"Processo do WebView2 falhou: {e.ProcessFailedKind}.");
        falha = erro;
        FalharPendentes(erro);
    }

    void ErroInterno(Exception e)
    {
        UltimoErroInterno = e;
        FalharPendentes(new InvalidOperationException("Erro inesperado na thread STA do HostOculto.", e));
    }

    void FalharPendentes(Exception erro)
    {
        foreach (var falhar in pendentes.Values) falhar(erro);
    }

    /// <summary>Encerra o host, falha o que estava pendente e apaga a pasta da sessão do documento.</summary>
    public async ValueTask DisposeAsync()
    {
        await Task.Run(Encerrar).ConfigureAwait(false);
        if (documento is not null) await documento.ApagarPastaAsync().ConfigureAwait(false);
    }

    public void Dispose()
    {
        Encerrar();
        documento?.ApagarPasta();
    }

    void Encerrar()
    {
        if (descartado) return;
        descartado = true;
        FalharPendentes(new ObjectDisposedException(nameof(HostOculto)));
        if (contexto is null || hwnd == IntPtr.Zero) return;
        contexto.Post(_ =>
        {
            try { documento?.Desanexar(); } catch (Exception e) { ErroInterno(e); }
            try { controller?.Close(); } catch (Exception e) { ErroInterno(e); }
            controller = null;
            contexto.Fechar();
            DestroyWindow(hwnd);
            hwnd = IntPtr.Zero;
            PostQuitMessage(0);
        }, null);
        if (Thread.CurrentThread != thread && !thread.Join(TimeSpan.FromSeconds(10)))
            throw new TimeoutException("A thread STA do HostOculto não encerrou em 10 s.");
        if (UltimoErroInterno is { } erro) throw new InvalidOperationException("Falha ao encerrar o HostOculto.", erro);
    }

    void LoopSta()
    {
        var instancia = GetModuleHandle(null);
        var nomeClasse = "PmvniHostOculto_" + Guid.NewGuid().ToString("N");
        bool registrada = false;
        try
        {
            wndProc = ProcessarMensagem;
            var classe = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = instancia,
                lpszClassName = nomeClasse,
            };
            if (RegisterClassEx(ref classe) == 0) throw new InvalidOperationException("RegisterClassEx falhou: " + Marshal.GetLastWin32Error());
            registrada = true;
            // Janela popup nunca mostrada (sem WS_VISIBLE): pai real para o controller do WebView2.
            hwnd = CreateWindowEx(0, nomeClasse, "Pmvni HostOculto", WS_POPUP, 0, 0, larguraPx, alturaPx, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx falhou: " + Marshal.GetLastWin32Error());
            contexto = new ContextoSta(hwnd);
            SynchronizationContext.SetSynchronizationContext(contexto);
            prontoJanela.TrySetResult();
            int resultado;
            while ((resultado = GetMessage(out var msg, IntPtr.Zero, 0, 0)) > 0)
            {
                // Trabalhos gerenciados fora do WndProc: WebView2 não deve ser fechado
                // dentro de um callback nativo nem de um despacho Win32 aninhado.
                if (msg.hwnd != hwnd || msg.message != WM_APP_EXECUTAR)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }
                // Um loop nativo aninhado pode consumir a notificação. A fila é
                // conferida também depois de voltar de qualquer despacho nativo.
                contexto.Drenar(ErroInterno);
            }
            if (resultado < 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        catch (Exception e)
        {
            prontoJanela.TrySetException(e);
            falha = e;
            ErroInterno(e);
        }
        finally
        {
            contexto?.Fechar();
            if (hwnd != IntPtr.Zero) { DestroyWindow(hwnd); hwnd = IntPtr.Zero; }
            if (registrada && !UnregisterClass(nomeClasse, instancia))
                ErroInterno(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
            // O ponteiro nativo não mantém o delegate gerenciado vivo.
            GC.KeepAlive(wndProc);
        }
    }

    IntPtr ProcessarMensagem(IntPtr janela, uint mensagem, IntPtr wParam, IntPtr lParam)
    {
        if (mensagem == WM_APP_EXECUTAR)
        {
            // A fila é drenada apenas pelo loop externo, nunca em despachos aninhados.
            return IntPtr.Zero;
        }
        return DefWindowProc(janela, mensagem, wParam, lParam);
    }

    /// <summary>SynchronizationContext que devolve os trabalhos para a thread STA via PostMessage.</summary>
    sealed class ContextoSta(IntPtr janela) : SynchronizationContext
    {
        readonly ConcurrentQueue<(SendOrPostCallback, object?)> fila = new();
        volatile bool fechado;

        // Depois de fechado, trabalhos novos são descartados: as operações a que pertenceriam já falharam
        // com ObjectDisposedException em Dispose.
        public override void Post(SendOrPostCallback d, object? state)
        {
            if (fechado) return;
            fila.Enqueue((d, state));
            PostMessage(janela, WM_APP_EXECUTAR, IntPtr.Zero, IntPtr.Zero);
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("Use Post na thread STA do HostOculto.");

        public override SynchronizationContext CreateCopy() => this;

        internal void Fechar()
        {
            fechado = true;
            fila.Clear();
        }

        // Cada item é protegido: nenhuma exceção atravessa o WndProc (fronteira nativa).
        internal void Drenar(Action<Exception> aoFalhar)
        {
            while (!fechado && fila.TryDequeue(out var item))
            {
                try { item.Item1(item.Item2); }
                catch (Exception e) { aoFalhar(e); }
            }
        }
    }

    // ---- Win32 ----
    const uint WS_POPUP = 0x80000000;
    const uint WM_APP_EXECUTAR = 0x8000 + 1;

    delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName, lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam, lParam;
        public uint time;
        public int ptX, ptY;
        public uint lPrivate;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool UnregisterClass(string nome, IntPtr instancia);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WNDCLASSEX classe);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
