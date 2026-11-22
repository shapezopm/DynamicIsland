using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace DynamicIsland;

// Preserve the native mouse target: alpha zero removes it from hit testing.
static class TaskbarHiddenOpacity
{
    const int ExStyle = -20;
    const long Layered = 0x80000;
    static IntPtr _window;
    static long _originalStyle;
    static byte _originalAlpha;
    static uint _originalColor, _originalFlags;
    static bool _owned;
    static DispatcherTimer? _timer;
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string name, string? title);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtrW(IntPtr window, int index);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtrW(IntPtr window, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr window, uint color, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr window, out uint color, out byte alpha, out uint flags);

    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    public static void Poll()
    {
        if (_timer != null) return;
        System.Windows.Application.Current.Exit += (_, _) => Restore();
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) => Update();
        _timer.Start();
        Update();
    }
    static void Update()
    {
        IntPtr window = FindWindow("Shell_TrayWnd", null);
        if (window != _window)
        {
            Restore();
            _window = window;
            if (window != IntPtr.Zero && GetLayeredWindowAttributes(window, out uint color, out byte alpha, out uint flags)
                && alpha <= 1 && flags == 2)
            {
                SetLayeredWindowAttributes(window, color, 255, 2);
                long style = GetWindowLongPtrW(window, ExStyle).ToInt64();
                SetWindowLongPtrW(window, ExStyle, new IntPtr(style & ~Layered));
            }
        }
        if (window == IntPtr.Zero || !GetWindowRect(window, out var rect)) return;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref info)) return;
        // Keep the native activation strip above ordinary maximized applications.
        if ((GetWindowLongPtrW(window, ExStyle).ToInt64() & 8) == 0 && !Native.IsForegroundFullscreen(IntPtr.Zero))
            SetWindowPos(window, new IntPtr(-1), 0, 0, 0, 0, 0x0013);
        bool collapsed = rect.Top >= info.Monitor.Bottom - 3 && rect.Bottom > info.Monitor.Bottom;
        if (!collapsed) { Restore(); return; }
        if (_owned) return;
        _originalStyle = GetWindowLongPtrW(window, ExStyle).ToInt64();
        _originalAlpha = 255; _originalColor = 0; _originalFlags = 2;
        if ((_originalStyle & Layered) != 0
            && !GetLayeredWindowAttributes(window, out _originalColor, out _originalAlpha, out _originalFlags)) return;
        SetWindowLongPtrW(window, ExStyle, new IntPtr(_originalStyle | Layered));
        if (SetLayeredWindowAttributes(window, _originalColor, 1, 2)) _owned = true;
        else SetWindowLongPtrW(window, ExStyle, new IntPtr(_originalStyle));
    }
    static void Restore()
    {
        if (!_owned) return;
        if (IsWindow(_window))
        {
            SetLayeredWindowAttributes(_window, _originalColor, _originalAlpha, _originalFlags);
            long style = GetWindowLongPtrW(_window, ExStyle).ToInt64();
            if ((_originalStyle & Layered) == 0) SetWindowLongPtrW(_window, ExStyle, new IntPtr(style & ~Layered));
        }
        _owned = false;
    }
}
