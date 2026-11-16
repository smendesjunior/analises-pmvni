using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Pmvni.App;

/// <summary>Ícone do tipo de arquivo na frente de cada arquivo das listas (correções 2026-11-15): o ícone que o próprio Windows associa à
/// extensão (o do Word, do Acrobat, do Edge instalados), pedido com SHGFI_USEFILEATTRIBUTES, isto é, só pela extensão, sem tocar no
/// arquivo (um arquivo do OneDrive só na nuvem não é baixado). Fica guardado uma vez por extensão. Sem ícone do Windows, um glifo Fluent.</summary>
public sealed partial class IconeArquivo : Grid
{
    public static readonly DependencyProperty CaminhoProperty = DependencyProperty.Register(
        nameof(Caminho), typeof(string), typeof(IconeArquivo), new PropertyMetadata(null, (d, _) => ((IconeArquivo)d).Aplicar()));

    // Por extensão: a imagem do Windows, ou null quando ele não deu ícone (fica o glifo). Só na thread da interface.
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.Ordinal);

    readonly Image imagem = new() { Stretch = Stretch.Uniform };
    readonly FontIcon reserva = new() { FontSize = 16, Visibility = Visibility.Collapsed };

    public IconeArquivo()
    {
        Width = 20;
        Height = 20;
        VerticalAlignment = VerticalAlignment.Center;
        Visibility = Visibility.Collapsed; // sem caminho (títulos), nada aparece
        reserva.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        Children.Add(imagem);
        Children.Add(reserva);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
    }

    /// <summary>Caminho do arquivo (só a extensão é usada) ou da pasta (terminado em barra).</summary>
    public string? Caminho
    {
        get => (string?)GetValue(CaminhoProperty);
        set => SetValue(CaminhoProperty, value);
    }

    void Aplicar()
    {
        var c = Caminho;
        if (string.IsNullOrWhiteSpace(c)) { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible;
        var chave = TiposArquivo.Chave(c);
        if (!Cache.TryGetValue(chave, out var fonte))
        {
            fonte = DoWindows(chave);
            Cache[chave] = fonte;
        }
        imagem.Source = fonte;
        imagem.Visibility = fonte is null ? Visibility.Collapsed : Visibility.Visible;
        reserva.Glyph = TiposArquivo.GlifoReserva(chave);
        reserva.Visibility = fonte is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Windows (shell32, user32, gdi32) ----------

    static ImageSource? DoWindows(string chave)
    {
        try
        {
            var pasta = chave == TiposArquivo.Pasta;
            // Um nome fictício com a extensão: com SHGFI_USEFILEATTRIBUTES o shell não abre nem procura o arquivo.
            var nome = pasta ? "pasta" : "arquivo" + chave;
            var atributos = pasta ? ArquivoDiretorio : ArquivoNormal;
            var info = new SHFILEINFO();
            var icone = IntPtr.Zero;
            // Primeiro o ícone grande da lista do sistema (48 px, nítido em telas com escala); se falhar, o de 32 px.
            if (SHGetFileInfoW(nome, atributos, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), UsarAtributos | IndiceDoSistema) != IntPtr.Zero)
            {
                var iid = IidImageList;
                if (SHGetImageList(ListaExtraGrande, ref iid, out var lista) == 0 && lista != IntPtr.Zero)
                {
                    icone = ImageList_GetIcon(lista, info.iIcon, 0x1 /* ILD_TRANSPARENT */);
                    Marshal.Release(lista);
                }
            }
            if (icone == IntPtr.Zero)
            {
                info = new SHFILEINFO();
                if (SHGetFileInfoW(nome, atributos, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), UsarAtributos | PedirIcone | IconeGrande) != IntPtr.Zero)
                    icone = info.hIcon;
            }
            if (icone == IntPtr.Zero) return null;
            try { return Converter(icone); }
            finally { DestroyIcon(icone); }
        }
        catch (Exception e)
        {
            System.Diagnostics.Debug.WriteLine("[icone] " + e.Message);
            return null;
        }
    }

    /// <summary>HICON → WriteableBitmap (BGRA pré-multiplicado). Ícones sem canal alfa usam a máscara.</summary>
    static WriteableBitmap? Converter(IntPtr icone)
    {
        if (!GetIconInfo(icone, out var ii)) return null;
        try
        {
            if (ii.hbmColor == IntPtr.Zero) return null;
            var bmp = new BITMAP();
            if (GetObjectW(ii.hbmColor, Marshal.SizeOf<BITMAP>(), ref bmp) == 0 || bmp.bmWidth <= 0 || bmp.bmHeight <= 0) return null;
            int l = bmp.bmWidth, a = bmp.bmHeight;
            var cor = Bits(ii.hbmColor, l, a);
            if (cor is null) return null;
            var temAlfa = false;
            for (var i = 3; i < cor.Length; i += 4) if (cor[i] != 0) { temAlfa = true; break; }
            if (!temAlfa)
            {
                var mascara = ii.hbmMask == IntPtr.Zero ? null : Bits(ii.hbmMask, l, a);
                for (var i = 0; i < cor.Length; i += 4)
                    cor[i + 3] = mascara is not null && mascara[i] != 0 ? (byte)0 : (byte)255;
            }
            // Pré-multiplica (o WriteableBitmap espera BGRA pré-multiplicado).
            for (var i = 0; i < cor.Length; i += 4)
            {
                var alfa = cor[i + 3];
                if (alfa == 255) continue;
                cor[i] = (byte)(cor[i] * alfa / 255);
                cor[i + 1] = (byte)(cor[i + 1] * alfa / 255);
                cor[i + 2] = (byte)(cor[i + 2] * alfa / 255);
            }
            var wb = new WriteableBitmap(l, a);
            using (var fluxo = wb.PixelBuffer.AsStream()) fluxo.Write(cor, 0, cor.Length);
            wb.Invalidate();
            return wb;
        }
        finally
        {
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);
        }
    }

    static byte[]? Bits(IntPtr bitmap, int largura, int altura)
    {
        var bi = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(), biWidth = largura, biHeight = -altura, biPlanes = 1, biBitCount = 32, biCompression = 0,
        };
        var dados = new byte[largura * altura * 4];
        var dc = GetDC(IntPtr.Zero);
        try { return GetDIBits(dc, bitmap, 0, (uint)altura, dados, ref bi, 0) == 0 ? null : dados; }
        finally { ReleaseDC(IntPtr.Zero, dc); }
    }

    const uint ArquivoNormal = 0x80, ArquivoDiretorio = 0x10;
    const uint PedirIcone = 0x100, IconeGrande = 0x0, IndiceDoSistema = 0x4000, UsarAtributos = 0x10;
    const int ListaExtraGrande = 0x2; // SHIL_EXTRALARGE (48 x 48)
    static readonly Guid IidImageList = new("46EB5926-582E-4017-9FDF-E8998DAA0950");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct ICONINFO
    {
        [MarshalAs(UnmanagedType.Bool)] public bool fIcon;
        public int xHotspot, yHotspot;
        public IntPtr hbmMask, hbmColor;
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

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll")]
    static extern int SHGetImageList(int iImageList, ref Guid riid, out IntPtr ppv);

    [DllImport("comctl32.dll")]
    static extern IntPtr ImageList_GetIcon(IntPtr himl, int i, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

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
