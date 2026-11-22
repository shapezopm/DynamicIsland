using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland;

// LiriQo (https://api.liriqo-alfarrizi.my.id/v1/lyrics). Unofficial; a single private host with no uptime promise.
// It aggregates upstreams (NetEase, Apple and others). Rows from a NetEase upstream are kept only when the row
// carries its own duration, because the plugin's NetEase origin check is not ported yet.
// Rows without timing are skipped: plain text is not shown as synced lyrics.
internal sealed class LiriQoProvider : ILyricsProvider
{
    internal const string ProviderId = "LiriQo";
    const string Endpoint = "https://api.liriqo-alfarrizi.my.id/v1/lyrics";
    const int MaxTracks = 8;
    static readonly TimeSpan Budget = TimeSpan.FromSeconds(8);
    static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(5);
    static readonly Regex NetEaseUpstream = new(@"net[\s_-]*ease|music\.163|music163|网易|網易|云音乐|雲音樂",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly HttpClient SharedHttp = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = Timeout.InfiniteTimeSpan };
    static readonly object Sync = new();
    static DateTimeOffset _cooldownUntil;
    readonly HttpClient _http;

    public LiriQoProvider(HttpClient? http = null) => _http = http ?? SharedHttp;

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

    // Pure parse of one response body; tests feed synthetic JSON.
    internal static ProviderSearchResult Parse(string body, TrackSnapshot track)
    {
        try
        {
            using JsonDocument json = JsonDocument.Parse(body, new() { MaxDepth = 32 });
            JsonElement root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new([], LyricsStatus.ProviderError, false, ErrorCode: "UnexpectedJsonShape");
            if (!root.TryGetProperty("tracks", out JsonElement tracks) || tracks.ValueKind != JsonValueKind.Array)
                return new([], LyricsStatus.NotFound, true);

            var candidates = new List<LyricsCandidate>();
            foreach (JsonElement row in tracks.EnumerateArray().Take(MaxTracks))
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                LyricsService.Line[] lines = ParseLines(row);
                if (lines.Length == 0) continue;

                string upstream = ProviderJson.Text(row, "upstreamProvider");
                if (upstream.Length == 0) upstream = ProviderJson.Text(row, "provider");
                double seconds = ProviderJson.Seconds(row, "duration");
                if (NetEaseUpstream.IsMatch(upstream) && seconds <= 0) continue;
                if (seconds > 0 && lines[^1].Time.TotalSeconds > seconds + ProviderJson.DurationSlack.TotalSeconds) continue;

                string title = ProviderJson.Text(row, "title");
                string artist = ProviderJson.Text(row, "artist");
                string album = ProviderJson.Text(row, "album");
                candidates.Add(new LyricsCandidate(ProviderId, null,
                    title.Length > 0 ? title : track.Title, artist.Length > 0 ? artist : track.Artist ?? "", album,
                    seconds, lines[^1].Time.TotalSeconds, lines, null, false)
                { HasValidSync = true, Upstream = upstream.Length > 0 ? upstream : null,
                    ProviderIdentityVerified = title.Length > 0 && artist.Length > 0 });
            }
            if (candidates.Count == 0) return new([], LyricsStatus.NotFound, true);
            return new(candidates.ToArray(), LyricsStatus.AwaitingVerification, true);
        }
        catch (JsonException) { return new([], LyricsStatus.ProviderError, false, ErrorCode: "MalformedJson"); }
    }

    // TTML first when it carries lines; otherwise the timed rows. A mixed or oversized timed list drops the row.
    static LyricsService.Line[] ParseLines(JsonElement row)
    {
        string ttml = ProviderJson.Text(row, "ttml");
        if (ttml.Length > 0)
        {
            LyricsService.Line[] rich = TtmlParser.Parse(ttml);
            if (rich.Length > 0) return rich;
        }
        if (!row.TryGetProperty("timed", out JsonElement timed) || timed.ValueKind != JsonValueKind.Array) return [];

        var lines = new List<LyricsService.Line>();
        foreach (JsonElement item in timed.EnumerateArray())
        {
            string text = ProviderJson.Clean(ProviderJson.Text(item, "text"));
            if (text.Length == 0) continue;
            if (!ProviderJson.Millis(item, "start", out double milliseconds)) return [];
            if (lines.Count == ProviderJson.MaxLines) return [];
            lines.Add(new(TimeSpan.FromMilliseconds(milliseconds), text));
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines.ToArray();
    }
}
