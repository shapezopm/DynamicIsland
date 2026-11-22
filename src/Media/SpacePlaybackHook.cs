using System.ComponentModel;
using System.Runtime.InteropServices;

namespace DynamicIsland;

// Installed on the WPF thread, which owns the message pump. It handles only Space;
// no text, other key contents or input history are retained.
internal sealed class SpacePlaybackHook : IDisposable
{
    const int KeyboardLowLevel = 13, Space = 0x20;
    const int KeyDown = 0x100, KeyUp = 0x101, SysKeyDown = 0x104, SysKeyUp = 0x105;
    readonly Func<bool> _eligible;
    readonly Action _pressed;
    readonly HookProc _callback;
    readonly SpacePlaybackGesture _gesture = new(Native.IsSpaceDown);
    IntPtr _hook;

    public SpacePlaybackHook(Func<bool> eligible, Action pressed)
    {
        _eligible = eligible;
        _pressed = pressed;
        _callback = Handle;
        _hook = SetWindowsHookEx(KeyboardLowLevel, _callback, GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot register island Space handler.");
    }

    IntPtr Handle(int code, IntPtr message, IntPtr data)
    {
        int kind = unchecked((int)message.ToInt64());
        if (code >= 0 && kind is KeyDown or KeyUp or SysKeyDown or SysKeyUp)
        {
            try
            {
                if (Marshal.ReadInt32(data) == Space)
                {
                    bool down = kind is KeyDown or SysKeyDown;
                    var result = _gesture.Process(down, down && _eligible());
                    if (result != SpacePlaybackGesture.Result.Pass)
                    {
                        // The callback only posts work; playback must not block the hook.
                        if (result == SpacePlaybackGesture.Result.Toggle) _pressed();
                        return new IntPtr(1);
                    }
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }
        return CallNextHookEx(_hook, code, message, data);
    }

    public void Dispose()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        GC.KeepAlive(_callback);
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int type, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string? moduleName);
}
