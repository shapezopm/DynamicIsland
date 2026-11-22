using System.IO;
using System.Reflection;
using System.Text.Json;

namespace DynamicIsland;

static class PluginInstaller
{
    public const string FileName = "DynamicIslandDiscordBridge.plugin.js";
    public const string VencordFileName = "DynamicIslandVencord.zip";
    const int MaxBytes = 1024 * 1024;

    static readonly string? Repository = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "PluginRepository")?.Value ?? Updater.Repository;

    public static bool Configured => Repository is { } repo && Updater.IsRepository(repo);
    public static string BetterDiscordPath => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.ApplicationData), "BetterDiscord", "plugins");

    public static async Task<bool> DownloadAsync(string assetName, string destination, Action<long, long> progress)
    {
        if (!Configured) throw new InvalidOperationException("Plugin repository is not configured.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var json = JsonDocument.Parse(await Updater.ReadReleaseAsync(Repository!, deadline.Token, assetName));
        var release = json.RootElement;
        var tag = release.GetProperty("tag_name").GetString()!;
        foreach (var entry in release.GetProperty("assets").EnumerateArray())
        {
            if (entry.GetProperty("name").GetString() != assetName) continue;
            var asset = Updater.ReadAsset(entry, tag, Repository!, assetName, MaxBytes);
            progress(0, asset.Size);
            using var response = await Updater.OpenDownloadAsync(asset.Address, deadline.Token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
                throw new InvalidDataException("Plugin asset size mismatch.");

            await using var source = await response.Content.ReadAsStreamAsync(deadline.Token);
            using var verified = new MemoryStream((int)asset.Size);
            await Updater.CopyVerifiedAsync(source, verified, asset, deadline.Token, done => progress(done, asset.Size));

            string pending = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await File.WriteAllBytesAsync(pending, verified.ToArray(), deadline.Token);
                File.Move(pending, destination, overwrite: true);
            }
            finally
            {
                if (File.Exists(pending)) File.Delete(pending);
            }
            return true;
        }
        return false;
    }
}
