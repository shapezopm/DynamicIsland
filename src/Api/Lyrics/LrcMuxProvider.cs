using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace DynamicIsland;

// LRC Mux (https://api.lrcmux.dev/get). Unofficial and undocumented for third parties.
// Requests line-level timing only; word timing is not read yet. Line `start` is in milliseconds.
// A response that mixes timed and untimed lines is rejected whole: time is never invented.
internal sealed class LrcMuxProvider : ILyricsProvider
{
    internal const string ProviderId = "LRC Mux";
    const string Endpoint = "https://api.lrcmux.dev/get";
    static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);
    static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
    static readonly object Sync = new();
    static DateTimeOffset _cooldownUntil;
    readonly HttpClient _http;

    public LrcMuxProvider(HttpClient? http = null) => _http = http ?? SharedHttp;

    public async Task<ProviderSearchResult> SearchAsync(TrackSnapshot track, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(track);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(track.Title)) return new([], LyricsStatus.NotFound, true);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        lock (Sync)
            if (_cooldownUntil > now) return new([], LyricsStatus.RateLimited, false, _cooldownUntil, "ProviderCooldown429");

        var query = new List<string>
        {
            "title=" + Uri.EscapeDataString(track.Title),
            "artist=" + Uri.EscapeDataString(track.Artist ?? ""),
            "level=line",
            "format=json",
        };
        if (!string.IsNullOrWhiteSpace(track.Album)) query.Add("album=" + Uri.EscapeDataString(track.Album));
        if (track.Duration.CanMatch) query.Add("duration=" + ProviderJson.Format(track.Duration.Value.TotalSeconds));

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(Budget);
        using var request = new HttpRequestMessage(HttpMethod.Get, Endpoint + "?" + string.Join("&", query));
        request.Headers.UserAgent.ParseAdd(AppVersion.Product + " (https://github.com/mihailkotovski/DynamicIsland)");
        request.Headers.Accept.ParseAdd("application/json");

        try
        {
            using HttpResponseMessage response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, budget.Token)
                .ConfigureAwait(false);
            int code = (int)response.StatusCode;
            if (code is 403 or 429)
            {
                DateTimeOffset until = DateTimeOffset.UtcNow + Cooldown;
                lock (Sync) _cooldownUntil = until;
                return new([], LyricsStatus.RateLimited, false, until, "Http" + code);
            }
            if (response.StatusCode == HttpStatusCode.NotFound || response.StatusCode == HttpStatusCode.NoContent)
                return new([], LyricsStatus.NotFound, true);
            if (code is >= 300 and < 400) return new([], LyricsStatus.ProviderError, false, ErrorCode: "RedirectBlocked");
            if (!response.IsSuccessStatusCode) return new([], LyricsStatus.ProviderError, false, ErrorCode: "Http" + code);

            string? type = response.Content.Headers.ContentType?.MediaType;
            if (type is null || !(type.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                || type.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
                return new([], LyricsStatus.ProviderError, false, ErrorCode: "UnexpectedContentType");
            if (response.Content.Headers.ContentLength > ProviderJson.MaxBodyBytes)
                return new([], LyricsStatus.ProviderError, false, ErrorCode: "PayloadTooLarge");

            string body = await response.Content.ReadAsStringAsync(budget.Token).ConfigureAwait(false);
            if (body.Length > ProviderJson.MaxBodyBytes) return new([], LyricsStatus.ProviderError, false, ErrorCode: "PayloadTooLarge");
            return Parse(body, track);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new([], LyricsStatus.ProviderError, false, ErrorCode: "RequestTimeout");
        }
        catch (HttpRequestException) { return new([], LyricsStatus.ProviderError, false, ErrorCode: "NetworkError"); }
    }

    // Parses one response body. Pure: no network, so tests can feed synthetic JSON.
    internal static ProviderSearchResult Parse(string body, TrackSnapshot track)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(body, new() { MaxDepth = 32 });
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new([], LyricsStatus.ProviderError, false, ErrorCode: "UnexpectedJsonShape");
            if (!root.TryGetProperty("lines", out JsonElement rows) || rows.ValueKind != JsonValueKind.Array)
                return new([], LyricsStatus.NotFound, true);

            var lines = new List<LyricsService.Line>();
            foreach (JsonElement row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) return new([], LyricsStatus.ProviderError, false, ErrorCode: "UnexpectedLineShape");
                string text = ProviderJson.Clean(ProviderJson.Text(row, "text"));
                if (text.Length == 0) continue;
                if (!ProviderJson.Millis(row, "start", out double milliseconds))
                    return new([], LyricsStatus.ProviderError, false, ErrorCode: "MixedTiming");
                if (lines.Count == ProviderJson.MaxLines) return new([], LyricsStatus.ProviderError, false, ErrorCode: "TooManyLines");
                lines.Add(new(TimeSpan.FromMilliseconds(milliseconds), text));
            }
            if (lines.Count == 0) return new([], LyricsStatus.NotFound, true);
            lines.Sort((a, b) => a.Time.CompareTo(b.Time));

            JsonElement info = root.TryGetProperty("track", out JsonElement value) ? value : default;
            string title = ProviderJson.Text(info, "title");
            string artist = ProviderJson.Text(info, "artist");
            string album = ProviderJson.Text(info, "album");
            double seconds = ProviderJson.Seconds(info, "duration");
            if (seconds > 0 && lines[^1].Time.TotalSeconds > seconds + ProviderJson.DurationSlack.TotalSeconds)
                return new([], LyricsStatus.ProviderError, false, ErrorCode: "TimingBeyondDuration");

            // meta.source names the database that answered, e.g. Deezer. It is attribution only, never a matching input.
            JsonElement sourceInfo = default;
            if (root.TryGetProperty("meta", out JsonElement meta) && meta.ValueKind == JsonValueKind.Object
                && meta.TryGetProperty("source", out JsonElement found)) sourceInfo = found;
            string upstream = ProviderJson.Text(sourceInfo, "name");

            var candidate = new LyricsCandidate(ProviderId, null,
                title.Length > 0 ? title : track.Title, artist.Length > 0 ? artist : track.Artist ?? "", album,
                seconds, lines[^1].Time.TotalSeconds, lines.ToArray(), null, false)
            { HasValidSync = true, Upstream = upstream.Length > 0 ? upstream : null };
            return new([candidate], LyricsStatus.AwaitingVerification, true);
        }
        catch (JsonException) { return new([], LyricsStatus.ProviderError, false, ErrorCode: "MalformedJson"); }
    }
}
