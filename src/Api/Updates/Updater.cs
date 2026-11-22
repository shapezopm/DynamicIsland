using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DynamicIsland;

sealed class Updater
{
    public enum Stage { Idle, Unconfigured, Checking, Latest, Available, Loading, Failed }

    public const string RestartedFlag = "--updated";

    internal static readonly string? Repository = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "UpdateRepository")?.Value;

    internal static bool IsRepository(string value) => value.Split('/') is [var owner, var name]
        && owner.Length > 0 && owner.All(c => char.IsAsciiLetterOrDigit(c) || c == '-')
        && name.Length > 0 && name is not "." and not ".."
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.');
    const string AssetName = "DynamicIsland.exe";
    const string HashPrefix = "sha256:";
    const int MaxNotes = 6;
    const int BufferSize = 1 << 16;
    const int CleanUpAttempts = 10;
    internal const long MaxAssetBytes = 256L * 1024 * 1024;
    static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
    static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);
    static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(10);
    static readonly TimeSpan CleanUpRetry = TimeSpan.FromSeconds(1);
    static readonly HttpClient Http;
    internal static Task<string> ReadReleaseAsync(string repository, CancellationToken token) =>
        Http.GetStringAsync($"https://api.github.com/repos/{repository}/releases/latest", token);

    static Updater()
    {
        CurrentVersion = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        Http = CreateClient(CurrentVersion);
    }

    internal sealed record ReleaseAsset(Uri Address, byte[] Digest, long Size);
    ReleaseAsset? _asset;
    DateTime _checkedAt;

    public static Version CurrentVersion { get; }

    public event Action? Changed;

    public Stage State { get; private set; } = Repository is { } repository && IsRepository(repository)
        ? Stage.Idle : Stage.Unconfigured;

    public Version? LatestVersion { get; private set; }

    public string[] Notes { get; private set; } = [];

    public int Percent { get; private set; }

    public long DownloadedBytes { get; private set; }

    public long TotalBytes { get; private set; }

    static string ExePath => Environment.ProcessPath ?? "";
    static string OldPath => ExePath + ".old";

    public async Task CheckAsync(bool force = false)
    {
        if (State is Stage.Unconfigured or Stage.Checking or Stage.Loading) return;
        if (!force && DateTime.UtcNow - _checkedAt < CheckInterval) return;
        _checkedAt = DateTime.UtcNow;

        SetState(Stage.Checking);
        _asset = null;
        TotalBytes = 0;
        try
        {
            using var timeout = new CancellationTokenSource(CheckTimeout);
            using var document = JsonDocument.Parse(await Http.GetStringAsync(
                $"https://api.github.com/repos/{Repository}/releases/latest", timeout.Token));
            JsonElement release = document.RootElement;
            string tag = release.GetProperty("tag_name").GetString()!;
            LatestVersion = Normalize(Version.Parse(tag.TrimStart('v')));
            Notes = release.TryGetProperty("body", out JsonElement body) ? ParseNotes(body.GetString() ?? "") : [];
            if (LatestVersion <= CurrentVersion)
            {
                SetState(Stage.Latest);
                return;
            }
            foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != AssetName) continue;
                _asset = ReadAsset(asset, tag, Repository!);
                TotalBytes = _asset.Size;
                break;
            }
            if (_asset is null) throw new InvalidDataException("The release has no update asset.");
            SetState(Stage.Available);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetState(Stage.Failed);
        }
    }

    public async Task<bool> InstallAsync()
    {
        if (State != Stage.Available || _asset is not { } asset || ExePath.Length == 0) return false;

        Percent = 0;
        DownloadedBytes = 0;
        SetState(Stage.Loading);
        string path = $"{ExePath}.{Guid.NewGuid():N}.new";
        bool created = false;
        try
        {
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, true))
            {
                created = true;
                await DownloadAsync(asset, file);
            }
            ReplaceExe(path);
            try
            {
                using Process restarted = Process.Start(new ProcessStartInfo(ExePath, RestartedFlag) { UseShellExecute = false })
                    ?? throw new InvalidOperationException("The updated application did not start.");
            }
            catch
            {
                File.Move(OldPath, ExePath, overwrite: true);
                throw;
            }
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetState(Stage.Failed);
            return false;
        }
        finally
        {
            if (created)
            {
                try { File.Delete(path); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); }
            }
        }
    }

    internal static ReleaseAsset ReadAsset(JsonElement asset, string tag, string repository, string assetName = AssetName, long maxBytes = MaxAssetBytes)
    {
        string? url = asset.GetProperty("browser_download_url").GetString();
        if (!IsRepository(repository) || !Uri.TryCreate(url, UriKind.Absolute, out Uri? address) || !IsHttps(address)
            || !address.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            || address.Query.Length != 0
            || !address.AbsolutePath.Equals($"/{repository}/releases/download/{Uri.EscapeDataString(tag)}/{assetName}", StringComparison.Ordinal))
            throw new InvalidDataException("Unexpected release download URL.");

        if (!asset.TryGetProperty("digest", out JsonElement hash) || hash.ValueKind != JsonValueKind.String
            || hash.GetString() is not { Length: 71 } digest || !digest.StartsWith(HashPrefix, StringComparison.OrdinalIgnoreCase)
            || !digest.Skip(HashPrefix.Length).All(Uri.IsHexDigit))
            throw new InvalidDataException("The update requires a SHA-256 digest.");
        if (!asset.TryGetProperty("size", out JsonElement size) || size.ValueKind != JsonValueKind.Number
            || !size.TryGetInt64(out long bytes) || (bytes <= 0 || bytes > maxBytes))
            throw new InvalidDataException("Unexpected release asset size.");
        return new(address, Convert.FromHexString(digest.AsSpan(HashPrefix.Length)), bytes);
    }

    static bool IsHttps(Uri address) => address.IsAbsoluteUri && address.Scheme == Uri.UriSchemeHttps
        && address.IsDefaultPort && address.UserInfo.Length == 0 && address.Fragment.Length == 0;

    internal static bool IsTrustedRedirect(Uri address) => IsHttps(address)
        && address.Host is "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    internal static async Task<HttpResponseMessage> OpenDownloadAsync(Uri address, CancellationToken token)
    {
        const int maxRedirects = 3;
        for (int redirects = 0; ; redirects++)
        {
            HttpResponseMessage response = await Http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, token);
            if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308)) return response;
            using (response)
            {
                if (redirects == maxRedirects || response.Headers.Location is not { } location
                    || !Uri.TryCreate(address, location, out Uri? next) || !IsTrustedRedirect(next))
                    throw new InvalidDataException("Unexpected release download redirect.");
                address = next;
            }
        }
    }

    async Task DownloadAsync(ReleaseAsset asset, Stream file)
    {
        using var deadline = new CancellationTokenSource(DownloadTimeout);
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        stalled.CancelAfter(StallTimeout);
        using HttpResponseMessage response = await OpenDownloadAsync(asset.Address, stalled.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != asset.Size)
            throw new InvalidDataException("The update response does not match the published size.");

        await using Stream from = await response.Content.ReadAsStreamAsync(stalled.Token);
        await CopyVerifiedAsync(from, file, asset, stalled.Token, done =>
        {
            stalled.CancelAfter(StallTimeout);
            int percent = (int)(done * 100 / asset.Size);
            if (percent == Percent) return;
            Percent = percent;
            DownloadedBytes = done;
            Changed?.Invoke();
        });
    }

    internal static async Task CopyVerifiedAsync(Stream from, Stream to, ReleaseAsset asset, CancellationToken token,
        Action<long>? progress = null)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        long done = 0;
        int read;
        while ((read = await from.ReadAsync(buffer, token)) > 0)
        {
            done += read;
            if (done > asset.Size) throw new InvalidDataException("The update exceeds the published size.");
            await to.WriteAsync(buffer.AsMemory(0, read), token);
            sha.AppendData(buffer, 0, read);
            progress?.Invoke(done);
        }
        if (done != asset.Size) throw new InvalidDataException("The update does not match the published size.");
        if (!CryptographicOperations.FixedTimeEquals(sha.GetHashAndReset(), asset.Digest))
            throw new InvalidDataException("The update does not match the hash GitHub gives for it.");
    }

    static void ReplaceExe(string downloadPath)
    {
        File.Move(ExePath, OldPath, true);
        try
        {
            File.Move(downloadPath, ExePath);
        }
        catch
        {
            File.Move(OldPath, ExePath);
            throw;
        }
    }

    public static void CleanUp() => Task.Run(async () =>
    {
        for (int attempt = 0; attempt < CleanUpAttempts && File.Exists(OldPath); attempt++)
        {
            try { File.Delete(OldPath); }
            catch { await Task.Delay(CleanUpRetry); }
        }
    });

    void SetState(Stage stage)
    {
        State = stage;
        Changed?.Invoke();
    }

    static string[] ParseNotes(string notes) => notes.Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 2 && line[0] is '-' or '*' or '•' && line[1] == ' ')
        .Select(line => line[2..].Replace("**", "").Replace("`", "").Trim())
        .Where(line => line.Length > 0 && !line.Contains("http", StringComparison.OrdinalIgnoreCase))
        .Take(MaxNotes)
        .ToArray();

    static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    static HttpClient CreateClient(Version current)
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            { Timeout = Timeout.InfiniteTimeSpan, MaxResponseContentBufferSize = 2 * 1024 * 1024 };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/" + current);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}
