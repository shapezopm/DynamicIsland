using System.IO;
using System.Reflection;
using Microsoft.Win32;

namespace DynamicIsland;

static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "DynamicIsland";

    static string? StartupCommand
    {
        get
        {
            string? assemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
            if (!string.IsNullOrWhiteSpace(assemblyName))
            {
                string appHost = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, assemblyName + ".exe"));
                // Prefer the apphost even when launched by `dotnet run`, so Windows starts the app itself.
                if (File.Exists(appHost)) return $"\"{appHost}\"";
            }

            string? process = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(process)) return null;
            process = Path.GetFullPath(process);

            // Fallback for framework-dependent launches without an apphost executable.
            if (string.Equals(Path.GetFileNameWithoutExtension(process), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                string? assembly = Environment.GetCommandLineArgs()
                    .FirstOrDefault(arg => Path.GetExtension(arg).Equals(".dll", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(arg));
                if (!string.IsNullOrWhiteSpace(assembly))
                    return $"\"{process}\" \"{Path.GetFullPath(assembly)}\"";
            }

            return $"\"{process}\"";
        }
    }

    public static bool Enabled
    {
        get
        {
            string? expected = StartupCommand;
            if (expected is null) return false;
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            string? configured = key?.GetValue(Name) as string;
            return configured is not null && SameCommand(configured, expected);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey)
            ?? throw new InvalidOperationException("Не удалось открыть раздел автозапуска Windows.");

        if (enabled)
        {
            string command = StartupCommand
                ?? throw new InvalidOperationException("Не удалось определить путь к DynamicIsland.exe.");
            key.SetValue(Name, command, RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(Name, false);
        }
    }

    static bool SameCommand(string configured, string expected)
    {
        // The registry entry is written in a canonical quoted form by Set(). Comparing the
        // complete command avoids treating another executable whose path merely contains the
        // current process path as an enabled startup entry.
        static string Normalize(string command) => command.Trim().Trim('"');
        return string.Equals(Normalize(configured), Normalize(expected), StringComparison.OrdinalIgnoreCase);
    }
}
