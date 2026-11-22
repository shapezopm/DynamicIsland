using System.Runtime.InteropServices;
using System.IO;
using System.Text;
using System.Windows;

namespace DynamicIsland;

static class Native
{
    public enum MediaKey { VolumeDown = 9, VolumeUp = 10, NextTrack = 11, PreviousTrack = 12 }

    const int GWL_STYLE = -16;
    const int GWL_EXSTYLE = -20;
    const long WS_MAXIMIZE = 0x01000000;
    const long WS_CAPTION = 0x00C00000, WS_THICKFRAME = 0x00040000;
    const long WS_EX_TOOLWINDOW = 0x00000080;
    const long WS_EX_NOACTIVATE = 0x08000000;
    const long WS_EX_TRANSPARENT = 0x00000020, WS_EX_LAYERED = 0x00080000, WS_EX_TOPMOST = 0x00000008;
    const long WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    const long WS_POPUP = 0x80000000;
    const uint LWA_ALPHA = 2;
    const int SW_HIDE = 0, SW_SHOWNOACTIVATE = 4;
    const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    const int MDT_EFFECTIVE_DPI = 0;
    const double DefaultDpi = 96;
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
    const uint MONITOR_DEFAULTTONEAREST = 2;
    const uint MONITORINFOF_PRIMARY = 1;
    const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_SPACE = 0x20;
    const int HSHELL_APPCOMMAND = 12;
    const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;
    const byte AC_LINE_ONLINE = 1, BATTERY_FLAG_NO_BATTERY = 128, BATTERY_FLAG_UNKNOWN = 255, BATTERY_PERCENT_MAX = 100;
    const int ClassNameCapacity = 64;
    const int DWMWA_USE_HOSTBACKDROPBRUSH = 17;
    const int DQTYPE_THREAD_CURRENT = 2, DQTAT_COM_STA = 2;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    static bool _dispatcherQueueStarted;
    static readonly int ShellMessage = (int)RegisterWindowMessage("SHELLHOOK");

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }

    [StructLayout(LayoutKind.Explicit, Size = 220, CharSet = CharSet.Unicode)]
    struct DISPLAYMODE
    {
        [FieldOffset(68)] public ushort Size;
        [FieldOffset(184)] public uint Frequency;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DispatcherQueueOptions { public int dwSize, threadType, apartmentType; }

    [DllImport("user32.dll", EntryPoint = "CreateWindowExW", CharSet = CharSet.Unicode)]
    static extern IntPtr CreateWindowEx(uint exStyle, string className, string? title, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    static extern bool RegisterTouchpadCapableWindow(IntPtr hwnd, bool register);

    [DllImport("user32.dll")]
    static extern bool GetCurrentInputMessageSource(out InputMessageSource source);

    [StructLayout(LayoutKind.Sequential)]
    struct InputMessageSource
    {
        public uint DeviceType;
        public uint OriginId;
    }

    internal static void EnableTouchpadInputSource(IntPtr hwnd)
    {
        try { RegisterTouchpadCapableWindow(hwnd, true); }
        catch (EntryPointNotFoundException) { }
    }

    internal static int PhysicalTouchpadWheelDelta(int delta)
    {
        const uint touchpad = 0x10;
        if (!GetCurrentInputMessageSource(out var source) || source.DeviceType != touchpad) return delta;
        const string path = @"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\PrecisionTouchPad";
        return Microsoft.Win32.Registry.GetValue(path, "ScrollDirection", 0) is 1 ? delta : -delta;
    }

    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int command);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint key, byte alpha, uint flags);

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("CoreMessaging.dll")]
    static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out IntPtr controller);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfoEx(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsW", CharSet = CharSet.Unicode)]
    static extern bool EnumDisplaySettings(string device, int mode, ref DISPLAYMODE settings);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);
    [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    static extern bool RegisterShellHookWindow(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string name);

    [DllImport("ntdll.dll")]
    static extern int NtQueryWnfStateData(ref ulong name, IntPtr type, IntPtr scope, out uint stamp, out int data, ref uint size);

    public static IntPtr ForegroundWindow => GetForegroundWindow();

    public static void BringForward(IntPtr hwnd) => SetForegroundWindow(hwnd);

    internal static bool IsGeckoWindow(IntPtr hwnd)
    {
        var name = new StringBuilder(32);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString() == "MozillaWindowClass";
    }

    public static Point CursorPosition => GetCursorPos(out POINT at) ? new Point(at.X, at.Y) : default;

    public static bool IsCtrlDown => GetAsyncKeyState(VK_CONTROL) < 0;
    public static bool IsShiftDown => GetAsyncKeyState(VK_SHIFT) < 0;
    public static bool IsSpaceDown => GetAsyncKeyState(VK_SPACE) < 0;
    public static bool IsPlaybackModifierDown => IsCtrlDown || IsShiftDown
        || GetAsyncKeyState(0x12) < 0 || GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0;

    public static bool RegisterShellHook(IntPtr hwnd) => RegisterShellHookWindow(hwnd);

    public static MediaKey? MediaKeyOf(int message, IntPtr wParam, IntPtr lParam) =>
        message == ShellMessage && wParam.ToInt64() == HSHELL_APPCOMMAND ? (MediaKey)((lParam.ToInt64() >> 16) & 0xFFF) : null;

    public static void HideFromTaskSwitcher(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(style | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE));
    }

    public static void AllowKeyboardInput(IntPtr hwnd, bool allow)
    {
        if (hwnd == IntPtr.Zero) return;
        long style = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(allow ? style & ~WS_EX_NOACTIVATE : style | WS_EX_NOACTIVATE));
    }

    public static Int32Rect VirtualScreen => new(GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
        GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN));

    static IntPtr MonitorAt(Point point) => MonitorFromPoint(new POINT { X = (int)point.X, Y = (int)point.Y }, MONITOR_DEFAULTTONEAREST);

    public static double ScaleAt(Point point) =>
        GetDpiForMonitor(MonitorAt(point), MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi > 0 ? dpi / DefaultDpi : 1;

    public static int RefreshRateAt(Point point)
    {
        var monitor = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>(), Device = "" };
        var mode = new DISPLAYMODE { Size = (ushort)Marshal.SizeOf<DISPLAYMODE>() };
        return GetMonitorInfoEx(MonitorAt(point), ref monitor) && EnumDisplaySettings(monitor.Device, -1, ref mode)
            && mode.Frequency > 1 && mode.Frequency <= 1000 ? (int)mode.Frequency : 60;
    }

    public static Rect WorkAreaAt(Point point)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        IntPtr monitor = MonitorAt(point);
        if (!GetMonitorInfo(monitor, ref info)) return Rect.Empty;
        RECT work = info.rcWork;
        return new Rect(work.Left, work.Top, work.Right - work.Left, work.Bottom - work.Top);
    }

    public static void MoveAndSize(IntPtr hwnd, int x, int y, int width, int height) =>
        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);

    public static IntPtr CreateOverlayWindow()
    {
        long style = WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
        IntPtr hwnd = CreateWindowEx((uint)style, "Static", null, (uint)WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        SetLayeredWindowAttributes(hwnd, 0, byte.MaxValue, LWA_ALPHA);
        return hwnd;
    }

    public static IntPtr CreateBackdropWindow()
    {
        IntPtr hwnd = CreateOverlayWindow();
        int on = 1;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_HOSTBACKDROPBRUSH, ref on, sizeof(int));
        return hwnd;
    }

    public static void Destroy(IntPtr hwnd) => DestroyWindow(hwnd);

    public static void PlaceBehind(IntPtr hwnd, IntPtr front)
    {
        if (!GetWindowRect(front, out RECT rect)) return;
        SetWindowPos(hwnd, front, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, SWP_NOACTIVATE);
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
    }

    public static void PutBehind(IntPtr hwnd, IntPtr front) =>
        SetWindowPos(hwnd, front, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static void ShowQuietly(IntPtr hwnd) => ShowWindow(hwnd, SW_SHOWNOACTIVATE);

    public static void Hide(IntPtr hwnd) => ShowWindow(hwnd, SW_HIDE);

    public static void EnsureDispatcherQueue()
    {
        if (_dispatcherQueueStarted) return;
        var options = new DispatcherQueueOptions { dwSize = Marshal.SizeOf<DispatcherQueueOptions>(), threadType = DQTYPE_THREAD_CURRENT, apartmentType = DQTAT_COM_STA };
        _dispatcherQueueStarted = CreateDispatcherQueueController(options, out _) >= 0;
    }

    public static bool IsToolWindow(IntPtr hwnd) => (GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0;

    public static void KeepOnTop(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

    public static bool IsForegroundFullscreen(IntPtr self)
        => TryGetForegroundFullscreen(self, out _, out _);

    public static bool TryGetForegroundFullscreen(IntPtr self, out IntPtr foreground, out Rect screen)
    {
        screen = Rect.Empty;
        foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == self || foreground == GetDesktopWindow() || foreground == GetShellWindow()) return false;
        if (IsMessenger(foreground)) return false;

        var className = new StringBuilder(ClassNameCapacity);
        GetClassName(foreground, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "XamlExplorerHostIslandWindow") return false;

        long style = GetWindowLongPtr(foreground, GWL_STYLE).ToInt64();
        bool framed = (style & WS_CAPTION) == WS_CAPTION || (style & WS_THICKFRAME) != 0;
        if ((style & WS_MAXIMIZE) != 0 && framed) return false;
        if (!GetWindowRect(foreground, out RECT window)) return false;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        if ((info.dwFlags & MONITORINFOF_PRIMARY) == 0) return false;

        RECT monitor = info.rcMonitor;
        if (window.Left > monitor.Left || window.Top > monitor.Top || window.Right < monitor.Right || window.Bottom < monitor.Bottom)
            return false;
        screen = new Rect(monitor.Left, monitor.Top, monitor.Right - monitor.Left, monitor.Bottom - monitor.Top);
        return true;
    }

    public static bool AllowsFullscreenReveal(IntPtr window)
    {
        var windowClass = new StringBuilder(ClassNameCapacity);
        GetClassName(window, windowClass, windowClass.Capacity);
        string kind = windowClass.ToString();
        if (kind is "UnityWndClass" or "UnrealWindow" or "CryENGINE") return false;

        try
        {
            ThreadOfWindow(window, out uint id);
            using var process = System.Diagnostics.Process.GetProcessById((int)id);
            string name = process.ProcessName;
            if (kind is "GLFW30" or "LWJGL" && name.Equals("javaw", StringComparison.OrdinalIgnoreCase))
                return false;
            // Reading MainModule can fail for elevated or protected applications.
            string? executable = null;
            try { executable = process.MainModule?.FileName; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            return !IsKnownGameExecutable(name, executable);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            return true;
        }
    }

    internal static bool IsKnownGameExecutable(string name, string? executable)
    {
        if (name.EndsWith("-Win64-Shipping", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Minecraft.Windows", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrEmpty(executable)) return false;
        if (executable.Contains(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase)
            || executable.Contains(@"\Epic Games\", StringComparison.OrdinalIgnoreCase)
                && !executable.Contains(@"\Launcher\", StringComparison.OrdinalIgnoreCase)
            || executable.Contains(@"\XboxGames\", StringComparison.OrdinalIgnoreCase)
            || executable.Contains(@"\GOG Galaxy\Games\", StringComparison.OrdinalIgnoreCase)) return true;
        string? directory = Path.GetDirectoryName(executable);
        return directory is not null && File.Exists(Path.Combine(directory, "UnityPlayer.dll"));
    }

    const int RunningExclusiveDirect3D = 3;
    [DllImport("shell32.dll")]
    static extern int SHQueryUserNotificationState(out int state);

    public static bool IsExclusiveDirect3DFullscreen() =>
        SHQueryUserNotificationState(out int state) == 0 && state == RunningExclusiveDirect3D;

    [DllImport("user32.dll", EntryPoint = "GetWindowThreadProcessId")]
    static extern uint ThreadOfWindow(IntPtr window, out uint processId);

    internal static uint WindowProcessId(IntPtr window) =>
        window != IntPtr.Zero && ThreadOfWindow(window, out uint id) != 0 ? id : 0;

    // Telegram-клиенты (включая Materialgram) занимают весь экран, но островок их не прячет.
    static bool IsMessenger(IntPtr window)
    {
        try
        {
            ThreadOfWindow(window, out uint id);
            using var process = System.Diagnostics.Process.GetProcessById((int)id);
            string name = process.ProcessName;
            return name is "Telegram" or "AyuGram" || MaterialgramWindowMode.MatchesProcess(name);
        }
        catch
        {
            return false;
        }
    }

    // Окно переднего плана перекрывает весь монитор (без исключений для мессенджеров и игр).
    public static bool IsForegroundCoveringScreen(IntPtr self)
    {
        IntPtr foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == self || foreground == GetDesktopWindow() || foreground == GetShellWindow()) return false;
        if (!GetWindowRect(foreground, out RECT window)) return false;
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(foreground, MONITOR_DEFAULTTONEAREST), ref info)) return false;
        RECT monitor = info.rcMonitor;
        return window.Left <= monitor.Left && window.Top <= monitor.Top && window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }

    public static bool? IsDoNotDisturbOn()
    {
        ulong name = WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED;
        uint size = sizeof(int);
        try
        {
            if (NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, out int profile, ref size) != 0) return null;
            return size >= sizeof(int) && profile != 0;
        }
        catch
        {
            return null;
        }
    }

    // Ночной свет: состояние лежит в CloudStore; при включении запись длиннее, а байт 18 равен 0x15.
    public static bool? IsNightLightOn()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate");
            if (key?.GetValue("Data") is not byte[] data) return null;
            return data.Length >= 19 && data[18] == 0x15;
        }
        catch
        {
            return null;
        }
    }

    // Режим «в самолёте»: значение по умолчанию SystemRadioState (0 — выключен, ненулевое — включён).
    public static bool? IsAirplaneModeOn()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\RadioManagement\SystemRadioState");
            object? raw = key?.GetValue(string.Empty);
            if (raw is int number) return number != 0;
            if (raw is string text && int.TryParse(text, out int parsed)) return parsed != 0;
            return null;
        }
        catch
        {
            return null;
        }
    }

    // Для диагностики: класс и стиль текущего окна переднего плана.
    public static string DescribeForeground()
    {
        IntPtr foreground = GetForegroundWindow();
        var className = new StringBuilder(ClassNameCapacity);
        GetClassName(foreground, className, className.Capacity);
        long style = GetWindowLongPtr(foreground, GWL_STYLE).ToInt64();
        GetWindowRect(foreground, out RECT window);
        return $"class={className} style=0x{style:X} rect={window.Left},{window.Top},{window.Right},{window.Bottom}";
    }

    static bool _brightnessLogged;
    static object? _wmiServices;

    // Яркость основного (встроенного) экрана через WMI; на настольных мониторах без WMI вернёт null.
    public static int? GetBrightness()
    {
        try
        {
            // Позднее связывание с COM WbemScripting (без NuGet и без System.Management).
            const System.Reflection.BindingFlags Call = System.Reflection.BindingFlags.InvokeMethod;
            const System.Reflection.BindingFlags Get = System.Reflection.BindingFlags.GetProperty;
            if (_wmiServices is null)
            {
                object locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!)!;
                _wmiServices = locator.GetType().InvokeMember("ConnectServer", Call, null, locator, [".", @"root\WMI"]);
            }
            object services = _wmiServices!;
            object results = services.GetType().InvokeMember("ExecQuery", Call, null, services, ["SELECT CurrentBrightness FROM WmiMonitorBrightness"])!;
            foreach (object item in (System.Collections.IEnumerable)results)
                return Convert.ToInt32(item.GetType().InvokeMember("CurrentBrightness", Get, null, item, null));
            return null;
        }
        catch (Exception ex)
        {
            _wmiServices = null;
            if (!_brightnessLogged)
            {
                _brightnessLogged = true;
                App.Log("brightness: " + ex);
            }
            return null;
        }
    }

    public static bool TryGetBattery(out int percent, out bool plugged)
    {
        percent = 0;
        plugged = false;
        if (!GetSystemPowerStatus(out SYSTEM_POWER_STATUS status)) return false;
        if (status.BatteryFlag == BATTERY_FLAG_UNKNOWN || (status.BatteryFlag & BATTERY_FLAG_NO_BATTERY) != 0
            || status.BatteryLifePercent > BATTERY_PERCENT_MAX) return false;
        percent = status.BatteryLifePercent;
        plugged = status.ACLineStatus == AC_LINE_ONLINE;
        return true;
    }
}
