using System.IO;
using System.Reflection;

namespace DynamicIsland;

static class PluginInstaller
{
    public const string FileName = "DynamicIslandDiscordBridge.plugin.js";
    public static string BetterDiscordPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData), "BetterDiscord", "plugins");
    public static bool HasBetterDiscord => Directory.Exists(BetterDiscordPath);
    public static bool HasBetterDiscordPlugin => File.Exists(Path.Combine(BetterDiscordPath, FileName));
    public static bool HasVencord => File.Exists(Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData), "Vencord", "dist", "patcher.js"));

    public static Task InstallBetterDiscordAsync()
    {
        if (!HasBetterDiscord) throw new DirectoryNotFoundException(BetterDiscordPath);
        return SaveAsync(Path.Combine(BetterDiscordPath, FileName), overwrite: false);
    }

    public static async Task SaveAsync(string destination, bool overwrite = true)
    {
        using Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("DiscordPlugins/" + FileName)
            ?? throw new InvalidDataException($"Missing embedded Discord plugin file: {FileName}");
        string pending = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await source.CopyToAsync(output);
            File.Move(pending, destination, overwrite);
        }
        finally
        {
            if (File.Exists(pending)) File.Delete(pending);
        }
    }
}
