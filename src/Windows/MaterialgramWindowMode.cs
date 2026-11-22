using System.Diagnostics;

namespace DynamicIsland;

// Materialgram can use a lowercase process name and raise its own fullscreen window.
static class MaterialgramWindowMode
{
    internal static bool MatchesProcess(string name) =>
        string.Equals(name, "materialgram", StringComparison.OrdinalIgnoreCase);

    internal static bool MatchesWindow(IntPtr window)
    {
        uint id = Native.WindowProcessId(window);
        if (id == 0) return false;
        try
        {
            using var process = Process.GetProcessById((int)id);
            return MatchesProcess(process.ProcessName);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
