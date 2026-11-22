using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace DynamicIsland;

// New clipboard updates only. No history read and no network access.
internal sealed class ClipboardShelfCapture : IDisposable
{
    const int ClipboardUpdate = 0x031D;
    readonly HwndSource _source;
    readonly ShelfCaptureStore _store;
    readonly Action<string> _saved;
    readonly Func<bool> _shelfEnabled;
    readonly CancellationTokenSource _lifetime = new();
    CancellationTokenSource? _pending;
    bool _registered;
    DateTime _loggedAt;

    internal ClipboardShelfCapture(IntPtr hwnd, Action<string> saved, ShelfCaptureStore? store = null, Func<bool>? shelfEnabled = null)
    {
        _source = HwndSource.FromHwnd(hwnd) ?? throw new ArgumentException("Missing window handle.", nameof(hwnd));
        _saved = saved; _store = store ?? new();
        _shelfEnabled = shelfEnabled ?? (() => Settings.ShelfEnabled);
        _source.AddHook(OnMessage);
        // With the shelf off, the listener is not registered at all, so Windows does not even notify us.
        _registered = _shelfEnabled() && AddClipboardFormatListener(hwnd);
    }

    internal void SetListening(bool listening)
    {
        if (listening == _registered || _lifetime.IsCancellationRequested) return;
        if (listening) _registered = AddClipboardFormatListener(_source.Handle);
        else
        {
            _pending?.Cancel();
            RemoveClipboardFormatListener(_source.Handle);
            _registered = false;
        }
    }

    IntPtr OnMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Shelf is off: do not read or save clipboard content at all.
        if (message != ClipboardUpdate || _lifetime.IsCancellationRequested || !_shelfEnabled()) return IntPtr.Zero;
        _pending?.Cancel(); _pending?.Dispose();
        var cancellation = _pending = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        uint sequence = GetClipboardSequenceNumber();
        _ = CaptureAsync(sequence, cancellation.Token);
        return IntPtr.Zero;
    }

    async Task CaptureAsync(uint sequence, CancellationToken token)
    {
        try
        {
            // Debounce applications that write several formats in successive updates.
            await Task.Delay(180, token);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                token.ThrowIfCancellationRequested();
                if (sequence != GetClipboardSequenceNumber()) return;
                try
                {
                    IDataObject? data = Clipboard.GetDataObject();
                    if (data is null || IsSensitive(data) || SensitiveOwner()) return;
                    bool screenshot = IsScreenshotOwner();
                    if (data.GetDataPresent(DataFormats.FileDrop))
                    {
                        if (screenshot && data.GetData(DataFormats.FileDrop) is string[] files)
                            foreach (string screenshotFile in files.Where(File.Exists).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg"))
                                _saved(screenshotFile);
                        return;
                    }
                    string? textSnapshot = null;
                    BitmapSource? imageSnapshot = null;
                    Task<string?>? save = null;
                    if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && !screenshot)
                        textSnapshot = text;
                    else if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
                    {
                        if ((long)image.PixelWidth * image.PixelHeight > ShelfCaptureStore.MaxImagePixels) return;
                        image.Freeze();
                        imageSnapshot = image;
                    }
                    if (sequence != GetClipboardSequenceNumber() || !_shelfEnabled()) return;
                    if (textSnapshot is not null) save = _store.SaveTextAsync(textSnapshot, token);
                    else if (imageSnapshot is not null) save = _store.SaveImageAsync(imageSnapshot, token);
                    if (save is not null && await save is { } file && !token.IsCancellationRequested)
                        await _source.Dispatcher.InvokeAsync(() => { if (!token.IsCancellationRequested) _saved(file); });
                    return;
                }
                catch (ExternalException) when (attempt < 4) { await Task.Delay(100, token); }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception exception) when (exception is ExternalException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            if (DateTime.UtcNow - _loggedAt > TimeSpan.FromMinutes(1)) { _loggedAt = DateTime.UtcNow; App.Log(exception); }
        }
    }

    internal static bool IsSensitive(IDataObject data)
    {
        if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing", false)
            || data.GetDataPresent("Clipboard Viewer Ignore", false)) return true;
        if (!data.GetDataPresent("CanIncludeInClipboardHistory", false)) return false;
        object? value = data.GetData("CanIncludeInClipboardHistory", false);
        return value switch
        {
            int integer => integer == 0,
            byte[] bytes => bytes.Length >= 4 && BitConverter.ToInt32(bytes, 0) == 0,
            MemoryStream stream => stream.Length >= 4 && BitConverter.ToInt32(stream.ToArray(), 0) == 0,
            _ => true, // Unknown exclusion metadata is safer to omit.
        };
    }

    static string Owner()
    {
        try
        {
            GetWindowThreadProcessId(GetClipboardOwner(), out uint id);
            if (id == 0) return "";
            using Process process = Process.GetProcessById((int)id);
            return process.ProcessName.ToLowerInvariant();
        }
        catch (ArgumentException) { return ""; }
        catch (InvalidOperationException) { return ""; }
    }

    static bool SensitiveOwner() => Owner() is "keepass" or "keepassxc" or "bitwarden" or "1password" or "dashlane";
    static bool IsScreenshotOwner() => Owner() is "snippingtool" or "screenclippinghost" or "sharex" or "greenshot" or "lightshot";

    public void Dispose()
    {
        _lifetime.Cancel(); _pending?.Cancel();
        if (_registered) RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(OnMessage);
        _pending?.Dispose(); _lifetime.Dispose();
    }

    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern IntPtr GetClipboardOwner();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
}
