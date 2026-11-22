using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland;

// Last resort when the media session gives no usable thumbnail (some Spotify tracks, video clips, local files).
// Uses the public Deezer catalogue — the same host the lyrics search already talks to, no key, no account.
// Only artist and title leave the PC, and only when lyrics search is switched on.
static partial class CoverFallback
{
    const int MaxJsonBytes = 256 * 1024, MaxImageBytes = 4 * 1024 * 1024;
    const string SearchUrl = "https://api.deezer.com/search?limit=5&q=";

    static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(6) };

    [GeneratedRegex(@"\s*[\(\[][^\)\]]*[\)\]]")]
    private static partial Regex Brackets();

    internal static async Task<MemoryStream?> FindAsync(string title, string artist, CancellationToken token = default)
    {
        title = Clean(title);
        artist = Clean(artist);
        if (title.Length == 0 || title.Length > 200 || artist.Length > 200) return null;

        string[] queries = artist.Length > 0
            ? [$"artist:\"{artist}\" track:\"{title}\"", $"{artist} {title}"]
            : [$"track:\"{title}\""];
        foreach (string query in queries)
        {
            string? url = await FindCoverUrlAsync(query, title, artist, token).ConfigureAwait(false);
            if (url is null) continue;
            MemoryStream? image = await DownloadAsync(url, token).ConfigureAwait(false);
            if (image is not null) return image;
        }
        return null;
    }

    static async Task<string?> FindCoverUrlAsync(string query, string title, string artist, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, SearchUrl + Uri.EscapeDataString(query));
        request.Headers.UserAgent.ParseAdd(AppVersion.Product);
        using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxJsonBytes) return null;

        await using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var limited = new MemoryStream();
        await CopyLimitedAsync(body, limited, MaxJsonBytes, token).ConfigureAwait(false);
        if (limited.Length == 0) return null;
        limited.Position = 0;

        using JsonDocument json = await JsonDocument.ParseAsync(limited, cancellationToken: token).ConfigureAwait(false);
        if (!json.RootElement.TryGetProperty("data", out JsonElement hits) || hits.ValueKind != JsonValueKind.Array) return null;
        foreach (JsonElement hit in hits.EnumerateArray())
        {
            string? name = Text(hit, "artist", "name"), found = Text(hit, "title");
            // A wrong cover is worse than none: the artist (or, without one, the title) has to agree.
            bool agrees = artist.Length > 0 ? name is not null && Agree(name, artist) : found is not null && Agree(found, title);
            if (!agrees) continue;
            foreach (string size in new[] { "cover_xl", "cover_big", "cover_medium" })
            {
                string? cover = Text(hit, "album", size);
                if (cover is not null && Uri.TryCreate(cover, UriKind.Absolute, out Uri? uri)
                    && uri.Scheme == Uri.UriSchemeHttps && uri.Host.EndsWith(".dzcdn.net", StringComparison.OrdinalIgnoreCase))
                    return uri.AbsoluteUri;
            }
        }
        return null;
    }

    static async Task<MemoryStream?> DownloadAsync(string url, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd(AppVersion.Product);
        using HttpResponseMessage response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxImageBytes) return null;
        if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true) return null;

        await using Stream body = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var image = new MemoryStream();
        await CopyLimitedAsync(body, image, MaxImageBytes, token).ConfigureAwait(false);
        if (image.Length == 0) return null;
        image.Position = 0;
        return image;
    }

    static async Task CopyLimitedAsync(Stream from, Stream to, int limit, CancellationToken token)
    {
        byte[] buffer = new byte[8192];
        int read;
        while ((read = await from.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (to.Length + read > limit) throw new IOException("PayloadTooLarge");
            to.Write(buffer, 0, read);
        }
    }

    static string? Text(JsonElement element, params string[] path)
    {
        foreach (string step in path)
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(step, out element)) return null;
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }

    static bool Agree(string a, string b)
    {
        a = Fold(a);
        b = Fold(b);
        return a.Length > 0 && b.Length > 0 && (a == b || a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal));
    }

    static string Fold(string value) => new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();

    // Quotes break the search syntax; "(feat. …)" and "[Remastered]" only make exact matches fail.
    static string Clean(string value) => Brackets().Replace(value.Replace('"', ' '), "").Trim();
}
