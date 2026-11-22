using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland;

internal static class SoundCloudWaveform
{
    const string WaveHost = "wave.sndcdn.com";
    const string MobileHost = "m.soundcloud.com";
    const int MaxSamples = 12_000;
    static readonly SemaphoreSlim BrowserLookup = new(1, 1);
    static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(5),
        MaxResponseContentBufferSize = 2 * 1024 * 1024,
    };

    internal sealed record Profile(float[] Levels, TimeSpan Duration);
    internal sealed record TrackData(Uri Wave, TimeSpan Duration);

    internal static async Task<Profile?> LoadAsync(TrackSnapshot track, CancellationToken token)
    {
        await BrowserLookup.WaitAsync(token).ConfigureAwait(false);
        Uri? address;
        try
        {
            address = await Task.Run(() => SourceApp.FindSoundCloudTrack(track, token), token).ConfigureAwait(false);
            if (address is null)
            {
                await Task.Delay(250, token).ConfigureAwait(false);
                address = await Task.Run(() => SourceApp.FindSoundCloudTrack(track, token), token).ConfigureAwait(false);
            }
        }
        finally { BrowserLookup.Release(); }
        return address is null ? null : await FetchAsync(address, track, Http, token).ConfigureAwait(false);
    }

    internal static async Task<Profile?> FetchAsync(Uri address, TrackSnapshot track, HttpClient http, CancellationToken token)
    {
        if (!TryTrackAddress(address.AbsoluteUri, out Uri? canonical) || canonical is null) return null;
        var mobile = new UriBuilder(canonical) { Host = MobileHost }.Uri;
        string html = await http.GetStringAsync(mobile, token).ConfigureAwait(false);
        TrackData? data = ReadTrack(html, canonical, track);
        if (data is null) return null;
        string json = await http.GetStringAsync(data.Wave, token).ConfigureAwait(false);
        float[]? samples = ReadLevels(json);
        return samples is null ? null : new(samples, data.Duration);
    }

    internal static bool IsSite(Uri address) =>
        address.Scheme == Uri.UriSchemeHttps && address.IsDefaultPort && address.UserInfo.Length == 0
        && address.Host is "soundcloud.com" or "www.soundcloud.com" or MobileHost;

    internal static bool TryTrackAddress(string? url, out Uri? track)
    {
        track = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? address) || !IsSite(address) || address.Query.Length > 0) return false;
        string[] parts = address.AbsolutePath.Trim('/').Split('/');
        if (parts is not [var artist, var title] || !IsSlug(artist) || !IsSlug(title)
            || artist is "you" or "pages" or "discover" or "search" or "charts"
            || title is "tracks" or "sets" or "likes" or "reposts" or "albums") return false;
        track = new Uri($"https://soundcloud.com/{artist}/{title}");
        return true;
    }

    static bool IsSlug(string value) => value.Length is > 0 and <= 200
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    internal static TrackData? ReadTrack(string html, Uri address, TrackSnapshot expected)
    {
        int script = html.IndexOf("<script id=\"__NEXT_DATA__\"", StringComparison.Ordinal);
        if (script < 0) return null;
        int open = html.IndexOf('>', script);
        if (open < 0) return null;
        int close = html.IndexOf("</script>", open + 1, StringComparison.Ordinal);
        if (close < 0) return null;

        using JsonDocument json = JsonDocument.Parse(html.AsMemory(open + 1, close - open - 1));
        JsonElement props = Property(Property(json.RootElement, "props"), "pageProps");
        string? urn = Text(props, "trackUrn");
        if (string.IsNullOrEmpty(urn)) return null;
        JsonElement entities = Property(Property(props, "initialStoreState"), "entities");
        JsonElement data = Property(Property(Property(entities, "tracks"), urn), "data");
        if (Text(data, "kind") != "track" || Text(data, "sharing") != "public"
            || !TryTrackAddress(Text(data, "permalink_url"), out Uri? permalink) || permalink != address
            || !SameText(Text(data, "title"), expected.Title)) return null;

        string? username = Text(Property(data, "user"), "username");
        if (username is null && Text(props, "userUrn") is { } userUrn)
            username = Text(Property(Property(Property(entities, "users"), userUrn), "data"), "username");
        string? creditedArtist = Text(Property(data, "publisher_metadata"), "artist");
        if (!SameText(username, expected.Artist) && !SameText(creditedArtist, expected.Artist)) return null;
        JsonElement duration = Property(data, "duration");
        if (duration.ValueKind != JsonValueKind.Number || !duration.TryGetInt32(out int milliseconds)
            || milliseconds is <= 0 or > 86_400_000) return null;
        TimeSpan length = TimeSpan.FromMilliseconds(milliseconds);
        if (!MatchesDuration(expected.Duration, length)) return null;

        string? key = Text(data, "waveformKey");
        if (key is null || !key.EndsWith("_m.json", StringComparison.Ordinal) || !IsSlug(key[..^7])) return null;
        return new(new Uri($"https://{WaveHost}/{key}"), length);
    }

    internal static bool MatchesDuration(TrackDuration duration, TimeSpan length) =>
        !duration.CanMatch || (duration.Value - length).Duration() <= TimeSpan.FromSeconds(3);

    static bool SameText(string? actual, string expected) => !string.IsNullOrWhiteSpace(actual)
        && string.Equals(actual.Trim(), expected.Trim(), StringComparison.OrdinalIgnoreCase);

    internal static float[]? ReadLevels(string document)
    {
        using JsonDocument json = JsonDocument.Parse(document);
        JsonElement source = Property(json.RootElement, "samples");
        JsonElement height = Property(json.RootElement, "height");
        if (source.ValueKind != JsonValueKind.Array || height.ValueKind != JsonValueKind.Number
            || !height.TryGetInt32(out int limit) || limit is <= 0 or > 32768) return null;
        int length = source.GetArrayLength();
        if (length is < TrackShapeBar.BarCount or > MaxSamples) return null;

        var levels = new float[TrackShapeBar.BarCount];
        var samples = source.EnumerateArray();
        for (int bar = 0; bar < levels.Length; bar++)
        {
            int count = (bar + 1) * length / levels.Length - bar * length / levels.Length;
            double energy = 0, peak = 0;
            for (int i = 0; i < count; i++)
            {
                samples.MoveNext();
                if (samples.Current.ValueKind != JsonValueKind.Number || !samples.Current.TryGetInt32(out int value)
                    || value < 0 || value > limit) return null;
                double normalized = (double)value / limit;
                energy += normalized * normalized;
                peak = Math.Max(peak, normalized);
            }
            levels[bar] = (float)(0.75 * Math.Sqrt(energy / count) + 0.25 * peak);
        }
        return levels;
    }

    static JsonElement Property(JsonElement element, string key) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out JsonElement value) ? value : default;

    static string? Text(JsonElement element, string key) =>
        Property(element, key) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;
}
