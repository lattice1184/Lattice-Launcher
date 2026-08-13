using System.Runtime.InteropServices;
using Avalonia.Platform;
using SkiaSharp;

namespace Launcher.App;

/// <summary>
/// 8-13 批次 34 原生启动画面：Win32 分层无边框窗 + 独立线程帧循环——动画与 Avalonia UI 线程
/// 完全并行，主窗口构造重活期间照常流畅（批次 33 Avalonia splash 被构造阻塞卡的治本）。
/// 动画极简：logo 淡入 300ms → 轻微呼吸 → 主窗口就绪后 150ms 淡出销毁。
/// 任何失败静默退化（无 splash 不影响启动）。
/// </summary>
public static class NativeSplash
{
    private const string ClassName = "LatticeNativeSplash";
    private static Thread? _thread;
    private static volatile bool _closing;

    /// <summary>启动 splash（独立线程；解码/创建失败静默返回）。重复调用忽略。</summary>
    public static void Show()
    {
        if (_thread is not null) return;
        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://Launcher.App/Assets/logo.png"));
            using var logo = SKBitmap.Decode(stream);
            if (logo is null) return;
            _closing = false;
            _thread = new Thread(() => SplashLoop(logo)) { IsBackground = true, Name = "NativeSplash" };
            _thread.Start();
        }
        catch { /* splash 失败不阻塞启动 */ }
    }

    /// <summary>主窗口就绪后调用：进入 150ms 淡出并销毁窗口。幂等，未 Show 时调用无害。</summary>
    public static void Dismiss() => _closing = true;

    private static void SplashLoop(SKBitmap logo)
    {
        IntPtr hwnd = IntPtr.Zero;
        IntPtr hInstance = IntPtr.Zero;
        try
        {
            hInstance = GetModuleHandleW(null);
            var dpi = GetDpiForSystem();
            var baseSize = (int)Math.Round(96 * dpi / 96.0);

            var wndClass = new WNDCLASSEXW
            {
                cbSize = Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = hInstance,
                lpszClassName = ClassName,
            };
            if (RegisterClassExW(ref wndClass) == 0) return;

            hwnd = CreateWindowExW(
                WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
                ClassName, "", WS_POPUP,
                0, 0, baseSize, baseSize,
                IntPtr.Zero, IntPtr.Zero, hInstance, IntPtr.Zero);
            if (hwnd == IntPtr.Zero) return;

            var start = Environment.TickCount64;
            var dismissedAt = 0L;
            while (true)
            {
                // 消息泵（分层窗无需响应，只排空队列；无消息即画帧）
                while (PeekMessageW(out var msg, IntPtr.Zero, 0, 0, PM_REMOVE))
                {
                    TranslateMessage(ref msg);
                    DispatchMessageW(ref msg);
                }

                var now = Environment.TickCount64;
                byte alpha;
                double breathe;
                if (_closing)
                {
                    if (dismissedAt == 0) dismissedAt = now;
                    var t = Math.Min(1.0, (now - dismissedAt) / 150.0);
                    alpha = (byte)Math.Round(255 * (1 - t));
                    breathe = 1.0;
                    if (t >= 1.0) break;
                }
                else
                {
                    var t = Math.Min(1.0, (now - start) / 300.0); // 淡入 300ms
                    alpha = (byte)Math.Round(255 * t);
                    breathe = 1.0 + 0.02 * Math.Sin((now - start) / 1600.0 * Math.PI * 2); // 呼吸 ±2%
                }

                var size = Math.Max(1, (int)Math.Round(baseSize * breathe));
                using var frame = logo.Resize(new SKImageInfo(size, size), SKFilterQuality.High);
                // 主屏居中（每帧重算：呼吸尺寸微变时中心不动）
                var x = (GetSystemMetrics(SM_CXSCREEN) - size) / 2;
                var y = (GetSystemMetrics(SM_CYSCREEN) - size) / 2;
                UpdateWindow(hwnd, x, y, frame, alpha);
                Thread.Sleep(16);
            }
        }
        catch { /* 帧异常静默退出 */ }
        finally
        {
            try { if (hwnd != IntPtr.Zero) DestroyWindow(hwnd); } catch { }
            try { if (hInstance != IntPtr.Zero) UnregisterClassW(ClassName, hInstance); } catch { }
            _thread = null;
        }
    }

    /// <summary>Skia RGBA8888 → 预乘 BGRA（UpdateLayeredWindow 的 AC_SRC_ALPHA 格式）→ 分层窗口</summary>
    private static void UpdateWindow(IntPtr hwnd, int x, int y, SKBitmap bmp, byte alpha)
    {
        var w = bmp.Width;
        var h = bmp.Height;
        var src = bmp.Pixels; // ReadOnlySpan<SKColor>（SkiaSharp 3.x）
        var bytes = new byte[w * h * 4];
        for (int i = 0, d = 0; i < src.Length; i++, d += 4)
        {
            var c = src[i];
            bytes[d] = (byte)(c.Blue * c.Alpha / 255); // B（预乘）
            bytes[d + 1] = (byte)(c.Green * c.Alpha / 255); // G
            bytes[d + 2] = (byte)(c.Red * c.Alpha / 255); // R
            bytes[d + 3] = c.Alpha;
        }

        var hdcScreen = GetDC(IntPtr.Zero);
        if (hdcScreen == IntPtr.Zero) return;
        var hdcMem = CreateCompatibleDC(hdcScreen);
        if (hdcMem == IntPtr.Zero) { ReleaseDC(IntPtr.Zero, hdcScreen); return; }
        try
        {
            var bmi = new BITMAPINFO
            {
                bmiHeader = new BITMAPINFOHEADER
                {
                    biSize = 40,
                    biWidth = w,
                    biHeight = -h, // top-down
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = BI_RGB,
                },
            };
            var hbmp = CreateDIBSection(hdcMem, ref bmi, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (hbmp == IntPtr.Zero) return;
            var old = SelectObject(hdcMem, hbmp);
            Marshal.Copy(bytes, 0, bits, bytes.Length);
            SelectObject(hdcMem, old);

            var blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                SourceConstantAlpha = alpha,
                AlphaFormat = AC_SRC_ALPHA,
            };
            var ptDst = new POINT { X = x, Y = y };
            var size = new SIZE { Width = w, Height = h };
            var ptSrc = new POINT();
            UpdateLayeredWindow(hwnd, hdcScreen, ref ptDst, ref size, hdcMem, ref ptSrc, 0, ref blend, ULW_ALPHA);
            DeleteObject(hbmp);
        }
        finally
        {
            DeleteDC(hdcMem);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }
    }

    // ---------- P/Invoke ----------

    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int PM_REMOVE = 0x0001;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const int BI_RGB = 0;
    private const int DIB_RGB_COLORS = 0;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const int ULW_ALPHA = 0x00000002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEXW
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int Width; public int Height; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly WndProc _wndProc = (hWnd, msg, wParam, lParam) => DefWindowProcW(hWnd, msg, wParam, lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool UnregisterClassW(string lpClassName, IntPtr hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowExW(int dwExStyle, string lpClassName, string lpWindowName,
        int dwStyle, int x, int y, int nWidth, int nHeight,
        IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool UpdateLayeredWindow(IntPtr hWnd, IntPtr hdcDst, ref POINT pptDst,
        ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, int dwFlags);

    [DllImport("user32.dll")]
    private static extern bool PeekMessageW(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin,
        uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProcW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFO pbmi, uint usage,
        out IntPtr ppvBits, IntPtr hSection, uint offset);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr ho);
}
