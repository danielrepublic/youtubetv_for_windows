using System.Runtime.InteropServices;
using System.Text;

namespace YouTubeTvShell.Tests.TestSupport;

/// <summary>
/// Minimal user32 helpers for process-level host tests against the REAL app
/// binary: find the main window, assert its maximized (fullscreen) state,
/// send OS-level keys (Esc, Alt+F4), and post a native close.
/// </summary>
internal static class NativeWindow
{
    public const uint WM_CLOSE = 0x0010;

    private const int SW_SHOWMAXIMIZED = 3;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowW(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public int ShowCmd;
        public POINT PtMinPosition;
        public POINT PtMaxPosition;
        public RECT RcNormalPosition;
    }

    private const byte VK_MENU = 0x12;
    private const byte VK_F4 = 0x73;
    private const byte VK_ESCAPE = 0x1B;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    public static IntPtr FindMainWindow(string title) => FindWindowW(null, title);

    public static bool Foreground(IntPtr hwnd) =>
        hwnd != IntPtr.Zero && SetForegroundWindow(hwnd);

    public static string GetTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>True when the window is maximized (the shell's fullscreen presentation).</summary>
    public static bool IsMaximized(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;
        var placement = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };
        return GetWindowPlacement(hwnd, ref placement) && placement.ShowCmd == SW_SHOWMAXIMIZED;
    }

    /// <summary>Posts a native close (title-bar close command equivalent). No focus needed.</summary>
    public static void PostNativeClose(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero)
            SendMessageW(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    /// <summary>Sends a real OS-level Escape keystroke to the foreground window.</summary>
    public static void SendEscape()
    {
        keybd_event(VK_ESCAPE, 0, 0, UIntPtr.Zero);
        keybd_event(VK_ESCAPE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Sends a real OS-level Alt+F4 to the foreground window.</summary>
    public static void SendAltF4()
    {
        keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
        keybd_event(VK_F4, 0, 0, UIntPtr.Zero);
        keybd_event(VK_F4, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>
    /// Best-effort focus for the XAML host: the host Esc handler only sees keys
    /// routed through XAML input, never keys swallowed by the WebView2 content
    /// HWND. Enumerates child windows, skips WebView2 (Chrome_*) surfaces, and
    /// focuses the first remaining child (the XAML island). Returns a focus map
    /// for diagnostics; true when SetFocus landed on a non-WebView2 child.
    /// </summary>
    public static bool TryFocusXamlHost(IntPtr parent, out string focusMap)
    {
        var sb = new StringBuilder();
        var focused = GetFocus();
        sb.Append("focus=0x").Append(focused.ToString("X"));
        var candidates = new List<(IntPtr Hwnd, string Class)>();
        if (parent != IntPtr.Zero)
        {
            EnumChildWindows(parent, (h, _) =>
            {
                var cls = new StringBuilder(256);
                GetClassNameW(h, cls, cls.Capacity);
                candidates.Add((h, cls.ToString()));
                return true;
            }, IntPtr.Zero);
        }

        foreach (var (hwnd, cls) in candidates)
            sb.Append("; child=0x").Append(hwnd.ToString("X")).Append('/').Append(cls);

        foreach (var (hwnd, cls) in candidates)
        {
            if (cls.Contains("Chrome", StringComparison.OrdinalIgnoreCase))
                continue;
            if (SetFocus(hwnd) == hwnd && GetFocus() == hwnd)
            {
                sb.Append("; xaml-focus=0x").Append(hwnd.ToString("X"));
                focusMap = sb.ToString();
                return true;
            }
        }

        focusMap = sb.ToString();
        return false;
    }
}
