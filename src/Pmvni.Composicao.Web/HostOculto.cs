using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Windows.Foundation;

namespace Pmvni.Composicao.Web;

/// <summary>
/// WebView2 sem interface: uma thread STA própria, com uma janela Win32 oculta (CreateWindowEx) que
/// hospeda um CoreWebView2Controller. Todo acesso ao CoreWebView2 passa por <see cref="ExecutarAsync{T}"/>,
/// que roda a ação na thread STA (as continuações dos awaits voltam para ela).
/// </summary>
public sealed class HostOculto : IDisposable
{
    const int LarguraPx = 1024, AlturaPx = 1400;

    readonly Thread thread;
    readonly TaskCompletionSource prontoJanela = new(TaskCreationOptions.RunContinuationsAsynchronously);
    ContextoSta? contexto;
    IntPtr hwnd;
    WndProc? wndProc; // mantém o delegate vivo enquanto a janela existir
    CoreWebView2Controller? controller;
    bool descartado;

    HostOculto()
    {
        thread = new Thread(LoopSta) { IsBackground = true, Name = "HostOculto-STA" };
        thread.SetApartmentState(ApartmentState.STA);
    }

    /// <summary>Cria a thread STA, a janela oculta, o ambiente e o controller do WebView2.</summary>
    public static async Task<HostOculto> IniciarAsync(string pastaDadosUsuario)
    {
        var host = new HostOculto();
        host.thread.Start();
        await host.prontoJanela.Task.ConfigureAwait(false);
        try
        {
            await host.ExecutarNaStaAsync(async () =>
            {
                var opcoes = new CoreWebView2EnvironmentOptions();
                var ambiente = await CoreWebView2Environment.CreateWithOptionsAsync(string.Empty, pastaDadosUsuario, opcoes);
                var referencia = CoreWebView2ControllerWindowReference.CreateFromWindowHandle((ulong)host.hwnd);
                host.controller = await ambiente.CreateCoreWebView2ControllerAsync(referencia);
                host.controller.Bounds = new Rect(0, 0, LarguraPx, AlturaPx);
                host.controller.IsVisible = true;
                return true;
            }).ConfigureAwait(false);
        }
        catch
        {
            host.Dispose();
            throw;
        }
        return host;
    }

    /// <summary>Executa <paramref name="acao"/> na thread STA com o CoreWebView2 deste host.</summary>
    public Task<T> ExecutarAsync<T>(Func<CoreWebView2, Task<T>> acao)
    {
        ObjectDisposedException.ThrowIf(descartado, this);
        return ExecutarNaStaAsync(() => acao(controller!.CoreWebView2));
    }

    Task<T> ExecutarNaStaAsync<T>(Func<Task<T>> acao)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        contexto!.Post(async _ =>
        {
            try { tcs.SetResult(await acao()); }
            catch (Exception e) { tcs.SetException(e); }
        }, null);
        return tcs.Task;
    }

    /// <summary>Navega para um HTML e espera NavigationCompleted (lança se falhar).</summary>
    public static Task NavegarParaHtmlAsync(CoreWebView2 webView, string html)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Concluida(CoreWebView2 s, CoreWebView2NavigationCompletedEventArgs e)
        {
            webView.NavigationCompleted -= Concluida;
            if (e.IsSuccess) tcs.TrySetResult();
            else tcs.TrySetException(new InvalidOperationException("Navegação falhou: " + e.WebErrorStatus));
        }
        webView.NavigationCompleted += Concluida;
        webView.NavigateToString(html);
        return tcs.Task;
    }

    public void Dispose()
    {
        if (descartado) return;
        descartado = true;
        if (contexto is null || hwnd == IntPtr.Zero) return;
        contexto.Post(_ =>
        {
            try { controller?.Close(); } catch { }
            controller = null;
            DestroyWindow(hwnd);
            PostQuitMessage(0);
        }, null);
        thread.Join(TimeSpan.FromSeconds(10));
    }

    void LoopSta()
    {
        try
        {
            var instancia = GetModuleHandle(null);
            var nomeClasse = "PmvniHostOculto_" + Environment.ProcessId + "_" + Environment.CurrentManagedThreadId;
            wndProc = ProcessarMensagem;
            var classe = new WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(wndProc),
                hInstance = instancia,
                lpszClassName = nomeClasse,
            };
            if (RegisterClassEx(ref classe) == 0) throw new InvalidOperationException("RegisterClassEx falhou: " + Marshal.GetLastWin32Error());
            // Janela popup nunca mostrada (sem WS_VISIBLE): pai real para o controller do WebView2.
            hwnd = CreateWindowEx(0, nomeClasse, "Pmvni HostOculto", WS_POPUP, 0, 0, LarguraPx, AlturaPx, IntPtr.Zero, IntPtr.Zero, instancia, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx falhou: " + Marshal.GetLastWin32Error());
            contexto = new ContextoSta(hwnd);
            SynchronizationContext.SetSynchronizationContext(contexto);
        }
        catch (Exception e)
        {
            prontoJanela.TrySetException(e);
            return;
        }
        prontoJanela.TrySetResult();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    IntPtr ProcessarMensagem(IntPtr janela, uint mensagem, IntPtr wParam, IntPtr lParam)
    {
        if (mensagem == WM_APP_EXECUTAR)
        {
            contexto?.Drenar();
            return IntPtr.Zero;
        }
        return DefWindowProc(janela, mensagem, wParam, lParam);
    }

    /// <summary>SynchronizationContext que devolve os trabalhos para a thread STA via PostMessage.</summary>
    sealed class ContextoSta(IntPtr janela) : SynchronizationContext
    {
        readonly ConcurrentQueue<(SendOrPostCallback, object?)> fila = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            fila.Enqueue((d, state));
            PostMessage(janela, WM_APP_EXECUTAR, IntPtr.Zero, IntPtr.Zero);
        }

        public override void Send(SendOrPostCallback d, object? state) =>
            throw new NotSupportedException("Use Post na thread STA do HostOculto.");

        public override SynchronizationContext CreateCopy() => this;

        internal void Drenar()
        {
            while (fila.TryDequeue(out var item)) item.Item1(item.Item2);
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
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassEx(ref WNDCLASSEX classe);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
}
