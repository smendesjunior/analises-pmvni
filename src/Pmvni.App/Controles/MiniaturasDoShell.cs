using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Pmvni.App;

/// <summary>Os pontos de uma miniatura: BGRA pré-multiplicado, de cima para baixo (o formato do <c>WriteableBitmap</c>).
/// <see cref="Pendente"/>: o Windows ainda está gerando a prévia; por ora veio o ícone do tipo, e vale pedir de novo daqui a pouco.</summary>
internal sealed record PixelsMiniatura(byte[] Bgra, int Largura, int Altura, bool Pendente = false);

/// <summary>Miniaturas dos arquivos pelo próprio Windows (página do processo em Miniaturas, 2026-11-16): a mesma prévia do Explorador
/// (<c>IShellItemImageFactory</c>), que cobre todo tipo com manipulador instalado (imagens, PDF, DOCX, DWG…) e cai no ícone do tipo
/// quando não há prévia. O Windows entrega a imagem já no tamanho pedido: a foto de 3 MB nunca é carregada inteira aqui.
/// <para>Os pedidos correm em duas threads próprias (STA, em segundo plano), nunca na da interface, e um pedido cancelado antes da sua
/// vez não chega a tocar o arquivo. Prévia que o Windows ainda não tem guardada: ele a gera por conta própria e responde "pendente"
/// (medido em 2026-11-16: fica pronta em décimos de segundo); aqui volta o ícone com <see cref="PixelsMiniatura.Pendente"/>, sem
/// prender a fila. Arquivo só na nuvem (OneDrive, não baixado): só a prévia que o Windows já tem guardada, senão o ícone do tipo — a
/// miniatura nunca baixa o arquivo. Arquivo sumido ou bloqueado: null (fica o ícone do tipo).</para></summary>
internal static class MiniaturasDoShell
{
    sealed record Pedido(string Caminho, int Lado, CancellationToken Ct, TaskCompletionSource<PixelsMiniatura?> Fim);

    const int Operarios = 2;
    static readonly BlockingCollection<Pedido> Fila = new(new ConcurrentQueue<Pedido>());
    static readonly object Trava = new();
    static bool iniciado;

    /// <summary>A miniatura de <paramref name="caminho"/> num quadrado de <paramref name="lado"/> pontos (a proporção é mantida), ou
    /// null se não deu (ou se foi cancelado). Nunca lança.</summary>
    public static Task<PixelsMiniatura?> ObterAsync(string caminho, int lado, CancellationToken ct)
    {
        if (ct.IsCancellationRequested || string.IsNullOrWhiteSpace(caminho)) return Task.FromResult<PixelsMiniatura?>(null);
        Iniciar();
        var fim = new TaskCompletionSource<PixelsMiniatura?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Fila.Add(new Pedido(caminho, Math.Clamp(lado, 16, 512), ct, fim), CancellationToken.None);
        return fim.Task;
    }

    static void Iniciar()
    {
        lock (Trava)
        {
            if (iniciado) return;
            iniciado = true;
            for (var i = 0; i < Operarios; i++)
            {
                var t = new Thread(Trabalhar) { IsBackground = true, Name = "Miniaturas " + (i + 1), Priority = ThreadPriority.BelowNormal };
                t.SetApartmentState(ApartmentState.STA);
                t.Start();
            }
        }
    }

    static void Trabalhar()
    {
        foreach (var p in Fila.GetConsumingEnumerable())
        {
            PixelsMiniatura? resultado = null;
            try
            {
                if (!p.Ct.IsCancellationRequested) resultado = Extrair(p.Caminho, p.Lado);
            }
            catch (Exception e)
            {
                // Manipulador de miniatura com defeito, arquivo que sumiu no meio: fica o ícone do tipo.
                System.Diagnostics.Debug.WriteLine("[miniatura] " + e.Message);
            }
            p.Fim.TrySetResult(resultado);
        }
    }

    static PixelsMiniatura? Extrair(string caminho, int lado)
    {
        FileAttributes atributos;
        try { atributos = File.GetAttributes(caminho); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { return null; }
        if ((atributos & FileAttributes.Directory) != 0) return null;
        var soNaNuvem = ((uint)atributos & (AtributoOffline | AtributoBuscaAoAbrir | AtributoBuscaAoLer)) != 0;

        var iid = typeof(IShellItemImageFactory).GUID;
        if (SHCreateItemFromParsingName(caminho, IntPtr.Zero, ref iid, out var fabrica) != 0 || fabrica is null) return null;
        try
        {
            var tamanho = new SIZE { cx = lado, cy = lado };
            // Primeiro só a prévia. Arquivo local: o Windows a gera (e guarda) se ainda não tem. Só na nuvem: só a que já está guardada.
            var hr = fabrica.GetImage(tamanho, soNaNuvem ? SoPrevia | SoGuardado : SoPrevia, out var bitmap);
            var pendente = false;
            if (hr != 0 || bitmap == IntPtr.Zero)
            {
                // Sem prévia (não há manipulador, falhou ou ainda está sendo gerada): o ícone do tipo, no mesmo tamanho.
                pendente = !soNaNuvem && (hr == ExtracaoPendente || hr == Pendente);
                hr = fabrica.GetImage(tamanho, SoIcone, out bitmap);
            }
            if (hr != 0 || bitmap == IntPtr.Zero) return null;
            try { return Ler(bitmap) is { } pixels ? pixels with { Pendente = pendente } : null; }
            finally { DeleteObject(bitmap); }
        }
        finally { Marshal.ReleaseComObject(fabrica); }
    }

    /// <summary>HBITMAP de 32 bits → BGRA de cima para baixo. Prévia sem canal alfa (JPEG, PDF) vem com o alfa zerado: fica opaca.</summary>
    static PixelsMiniatura? Ler(IntPtr bitmap)
    {
        var bmp = new BITMAP();
        if (GetObjectW(bitmap, Marshal.SizeOf<BITMAP>(), ref bmp) == 0) return null;
        int l = bmp.bmWidth, a = Math.Abs(bmp.bmHeight);
        if (l is <= 0 or > 1024 || a is <= 0 or > 1024) return null;
        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = l, biHeight = -a, biPlanes = 1, biBitCount = 32, biCompression = 0,
        };
        var dados = new byte[l * a * 4];
        var dc = GetDC(IntPtr.Zero);
        try
        {
            if (GetDIBits(dc, bitmap, 0, (uint)a, dados, ref bi, 0) == 0) return null;
        }
        finally { ReleaseDC(IntPtr.Zero, dc); }

        var temAlfa = false;
        for (var i = 3; i < dados.Length; i += 4)
            if (dados[i] != 0) { temAlfa = true; break; }
        if (!temAlfa)
        {
            for (var i = 3; i < dados.Length; i += 4) dados[i] = 255;
        }
        else
        {
            // Pré-multiplicado, cada cor cabe no alfa; uma prévia que veio sem pré-multiplicar não pode estourar a superfície.
            for (var i = 0; i < dados.Length; i += 4)
            {
                var alfa = dados[i + 3];
                if (alfa == 255) continue;
                if (dados[i] > alfa) dados[i] = alfa;
                if (dados[i + 1] > alfa) dados[i + 1] = alfa;
                if (dados[i + 2] > alfa) dados[i + 2] = alfa;
            }
        }
        return new PixelsMiniatura(dados, l, a);
    }

    // ---------- Windows (shell32, user32, gdi32) ----------

    // FILE_ATTRIBUTE_OFFLINE, RECALL_ON_OPEN e RECALL_ON_DATA_ACCESS: o conteúdo não está neste computador (OneDrive só na nuvem).
    const uint AtributoOffline = 0x1000, AtributoBuscaAoAbrir = 0x40000, AtributoBuscaAoLer = 0x400000;
    // SIIGBF_ICONONLY, SIIGBF_THUMBNAILONLY e SIIGBF_INCACHEONLY.
    const int SoIcone = 0x4, SoPrevia = 0x8, SoGuardado = 0x10;
    // WTS_E_EXTRACTIONPENDING e E_PENDING: a prévia está sendo gerada.
    const int ExtracaoPendente = unchecked((int)0x8004B205), Pendente = unchecked((int)0x8000000A);

    [StructLayout(LayoutKind.Sequential)]
    struct SIZE
    {
        public int cx, cy;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
        // Sobra para as 3 máscaras/cores que o GetDIBits pode escrever depois do cabeçalho.
        public uint extra0, extra1, extra2, extra3;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    static extern int SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory? ppv);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    static extern int GetObjectW(IntPtr h, int c, ref BITMAP pv);

    [DllImport("gdi32.dll")]
    static extern int GetDIBits(IntPtr hdc, IntPtr hbm, uint start, uint cLines, [Out] byte[] lpvBits, ref BITMAPINFOHEADER lpbmi, uint usage);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeleteObject(IntPtr ho);
}
